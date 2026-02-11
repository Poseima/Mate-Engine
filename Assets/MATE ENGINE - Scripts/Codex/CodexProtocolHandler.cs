using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace MateEngine.Codex
{
    public class CodexProtocolHandler
    {
        Process process;
        StreamWriter stdin;
        Thread readerThread;
        volatile bool running;

        // Pending RPC responses keyed by request id
        readonly ConcurrentDictionary<string, Action<JToken, JsonRpcError>> pendingRequests = new();

        // Events marshalled to main thread via queues
        readonly ConcurrentQueue<Action> mainThreadQueue = new();

        // Public events (invoked on main thread via Pump)
        public event Action<string> OnStreamDelta;          // item/agentMessage/delta → delta text
        public event Action<string, string, string> OnReasoningDelta; // threadId, turnId, delta text
        public event Action<string> OnTurnCompleted;        // turn/completed → threadId
        public event Action<string> OnTurnStarted;          // turn/started → turnId
        public event Action<bool, string> OnLoginCompleted; // account/login/completed
        public event Action<string> OnError;                // any error

        // Task monitor events (invoked on main thread via Pump)
        public event Action<string, string> OnTurnStartedFull;              // threadId, turnId
        public event Action<string, string> OnTurnCompletedFull;            // threadId, turnId
        public event Action<string, string, JToken> OnItemStarted;          // threadId, turnId, item
        public event Action<string, string, JToken> OnItemCompleted;        // threadId, turnId, item
        public event Action<string, string, string, JArray> OnPlanUpdated;  // threadId, turnId, explanation, plan
        public event Action<string, string> OnThreadNameUpdated;            // threadId, name

        public bool IsRunning => running && process != null && !process.HasExited;

        // Current turn ID (tracked for interrupt)
        public string CurrentTurnId { get; private set; }

        // The server doesn't always emit `turn/started` (observed in Player logs).
        // Synthesize a "turn started" event when we first see any notification that includes (threadId, turnId).
        readonly object startedTurnsLock = new();
        readonly HashSet<string> startedTurns = new();

        // Some providers emit both v2 streaming deltas (item/agentMessage/delta) and raw codex/event deltas.
        // Deduplicate by picking the first source we see per itemId.
        readonly object agentDeltaSourceLock = new();
        readonly Dictionary<string, bool> agentDeltaSourceIsV2ByItemId = new();

        // ── Lifecycle ──────────────────────────────────────────────

        public void Start(string binaryPath, Dictionary<string, string> envVars)
        {
            if (running) Stop();

            // New process/session: forget any prior synthetic turn starts.
            CurrentTurnId = null;
            lock (startedTurnsLock) startedTurns.Clear();
            lock (agentDeltaSourceLock) agentDeltaSourceIsV2ByItemId.Clear();

            var psi = new ProcessStartInfo
            {
                FileName = binaryPath,
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                StandardOutputEncoding = System.Text.Encoding.UTF8,
                StandardErrorEncoding = System.Text.Encoding.UTF8
            };

            if (envVars != null)
            {
                foreach (var kv in envVars)
                    psi.EnvironmentVariables[kv.Key] = kv.Value;
            }

            process = new Process { StartInfo = psi, EnableRaisingEvents = true };
            process.Exited += (_, __) =>
            {
                running = false;
                Enqueue(() => OnError?.Invoke("codex-app-server process exited unexpectedly."));
            };

            process.Start();
            stdin = new StreamWriter(process.StandardInput.BaseStream, new System.Text.UTF8Encoding(false));
            stdin.AutoFlush = true;
            running = true;

            // stderr logger
            process.ErrorDataReceived += (_, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                    Enqueue(() => Debug.Log("[Codex stderr] " + e.Data));
            };
            process.BeginErrorReadLine();

            readerThread = new Thread(ReadLoop) { IsBackground = true, Name = "CodexReader" };
            readerThread.Start();
        }

        public void Stop()
        {
            running = false;
            try { stdin?.Close(); } catch { }
            try
            {
                if (process != null && !process.HasExited)
                {
                    process.Kill();
                    process.WaitForExit(2000);
                }
            }
            catch { }
            process?.Dispose();
            process = null;
            pendingRequests.Clear();

            CurrentTurnId = null;
            lock (startedTurnsLock) startedTurns.Clear();
            lock (agentDeltaSourceLock) agentDeltaSourceIsV2ByItemId.Clear();
        }

        // ── Send request (expects response) ────────────────────────

        public void SendRequest(string method, object @params, Action<JToken, JsonRpcError> callback = null)
        {
            var req = new JsonRpcRequest(method, @params);
            if (callback != null)
                pendingRequests[req.id] = callback;

            WriteLine(req.ToJson());
        }

        // ── Send notification (no response expected) ───────────────

        public void SendNotification(string method, object @params = null)
        {
            var notif = new JsonRpcNotification(method, @params);
            WriteLine(notif.ToJson());
        }

        // ── Send response to a server-to-client request ────────────

        public void SendResponse(string id, object result)
        {
            var resp = new { id, result };
            string json = JsonConvert.SerializeObject(resp, Formatting.None,
                new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore });
            WriteLine(json);
        }

        void WriteLine(string json)
        {
            try
            {
                stdin.WriteLine(json);
            }
            catch (Exception e)
            {
                Enqueue(() => OnError?.Invoke("Failed to write to codex stdin: " + e.Message));
            }
        }

        // ── Main-thread pump (call from Update) ───────────────────

        public void Pump()
        {
            while (mainThreadQueue.TryDequeue(out var action))
            {
                try { action(); }
                catch (Exception e) { Debug.LogException(e); }
            }
        }

        // ── Background reader ──────────────────────────────────────

        void ReadLoop()
        {
            try
            {
                var stdout = process.StandardOutput;
                while (running && !stdout.EndOfStream)
                {
                    string line = stdout.ReadLine();
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    DispatchLine(line);
                }
            }
            catch (Exception e)
            {
                if (running)
                    Enqueue(() => OnError?.Invoke("Reader thread error: " + e.Message));
            }
        }

        void DispatchLine(string line)
        {
            // DEBUG: log every line from binary
            Enqueue(() => Debug.Log("[Codex RECV] " + (line.Length > 500 ? line.Substring(0, 500) + "..." : line)));

            // 1. Try as RPC response (id + result/error, no method)
            if (CodexLineParser.IsResponse(line, out var respId, out var result, out var error))
            {
                Enqueue(() => Debug.Log("[Codex] Response id=" + respId));
                if (pendingRequests.TryRemove(respId, out var cb))
                    Enqueue(() => cb(result, error));
                return;
            }

            // 2. Try as server-to-client request (id + method) — needs a response
            if (CodexLineParser.IsServerRequest(line, out var reqId, out var reqMethod, out var reqParams))
            {
                Enqueue(() => Debug.Log("[Codex] ServerRequest method=" + reqMethod + " id=" + reqId));
                HandleServerRequest(reqId, reqMethod, reqParams);
                return;
            }

            // 3. Try as notification (method, no id)
            if (CodexLineParser.IsNotification(line, out var method, out var @params))
            {
                Enqueue(() => Debug.Log("[Codex] Notification method=" + method));
                HandleNotification(method, @params);
                return;
            }

            Enqueue(() => Debug.Log("[Codex] Unknown line: " + line));
        }

        void HandleServerRequest(string id, string method, JToken @params)
        {
            // Auto-accept all approval requests (chat mode doesn't execute code)
            switch (method)
            {
                case "item/commandExecution/requestApproval":
                case "item/fileChange/requestApproval":
                    SendResponse(id, new ApprovalResponse());
                    break;

                default:
                    // Unknown server request — send empty response
                    SendResponse(id, new { });
                    Enqueue(() => Debug.Log("[Codex] Auto-responded to server request: " + method));
                    break;
            }
        }

        void HandleNotification(string method, JToken @params)
        {
            switch (method)
            {
                // ── V2 protocol format ──────────────────────────────────
                case "item/agentMessage/delta":
                    Debug.Log("[Codex RAW delta] " + @params?.ToString(Formatting.None));
                    var deltaThreadId = @params?["threadId"]?.Value<string>() ?? @params?["thread_id"]?.Value<string>() ?? "";
                    var deltaTurnId = @params?["turnId"]?.Value<string>() ?? @params?["turn_id"]?.Value<string>() ?? "";
                    EnsureTurnStarted(deltaThreadId, deltaTurnId);
                    var v2ItemId = @params?["itemId"]?.Value<string>() ?? @params?["item_id"]?.Value<string>() ?? "";

                    var delta = @params?["delta"]?.Value<string>() ?? "";
                    if (ShouldAcceptAgentDelta(v2ItemId, isV2: true))
                        Enqueue(() => OnStreamDelta?.Invoke(delta));
                    break;

                case "turn/completed":
                    Debug.Log("[Codex RAW turn/completed] " + @params?.ToString(Formatting.None));
                    var tid = @params?["threadId"]?.Value<string>() ?? @params?["thread_id"]?.Value<string>() ?? "";
                    var completedTurnId = @params?["turn"]?["id"]?.Value<string>()
                        ?? @params?["turnId"]?.Value<string>()
                        ?? @params?["turn_id"]?.Value<string>()
                        ?? "";
                    Enqueue(() =>
                    {
                        var resolvedTurnId = !string.IsNullOrEmpty(completedTurnId) ? completedTurnId : CurrentTurnId;
                        CurrentTurnId = null;
                        OnTurnCompleted?.Invoke(tid);
                        if (!string.IsNullOrEmpty(resolvedTurnId))
                            OnTurnCompletedFull?.Invoke(tid, resolvedTurnId);
                    });
                    break;

                case "turn/started":
                    var startedTurn = @params?["turn"];
                    var startedTurnId = startedTurn?["id"]?.Value<string>() ?? "";
                    var startedThreadId = @params?["threadId"]?.Value<string>() ?? @params?["thread_id"]?.Value<string>() ?? "";
                    EnsureTurnStarted(startedThreadId, startedTurnId);
                    break;

                case "account/login/completed":
                    var success = @params?["success"]?.Value<bool>() ?? false;
                    var err = @params?["error"]?.Value<string>();
                    Enqueue(() => OnLoginCompleted?.Invoke(success, err));
                    break;

                case "error":
                    var errorMsg = @params?["error"]?["message"]?.Value<string>()
                                   ?? @params?.ToString() ?? "Unknown error";
                    Enqueue(() => OnError?.Invoke(errorMsg));
                    break;

                // ── Raw codex/event format (Chat Completions / aggregated) ─

                case "codex/event/task_complete":
                    // Fires for all providers; only act on it if the v2 turn/completed
                    // didn't already arrive (i.e. CurrentTurnId is still set).
                    var rawCompTid = @params?["conversationId"]?.Value<string>() ?? "";
                    Enqueue(() =>
                    {
                        if (CurrentTurnId != null)
                        {
                            var completed = CurrentTurnId;
                            CurrentTurnId = null;
                            OnTurnCompleted?.Invoke(rawCompTid);
                            OnTurnCompletedFull?.Invoke(rawCompTid, completed);
                        }
                    });
                    break;

                case "codex/event/stream_error":
                case "codex/event/error":
                    var rawErr = @params?["msg"]?["message"]?.Value<string>()
                              ?? @params?.ToString() ?? "Unknown error";
                    Enqueue(() => OnError?.Invoke(rawErr));
                    break;

                case "codex/event/reasoning_content_delta":
                case "codex/event/agent_reasoning_delta":
                {
                    var msg = @params?["msg"];
                    var reasoningDelta = msg?["delta"]?.Value<string>() ?? "";
                    if (string.IsNullOrEmpty(reasoningDelta))
                        break;

                    var threadId = msg?["thread_id"]?.Value<string>()
                        ?? msg?["threadId"]?.Value<string>()
                        ?? @params?["conversationId"]?.Value<string>()
                        ?? "";
                    var turnId = msg?["turn_id"]?.Value<string>() ?? msg?["turnId"]?.Value<string>() ?? "";

                    EnsureTurnStarted(threadId, turnId);
                    Enqueue(() => OnReasoningDelta?.Invoke(threadId, turnId, reasoningDelta));
                    break;
                }

                case "codex/event/agent_message_content_delta":
                {
                    var msg = @params?["msg"];
                    var contentDelta = msg?["delta"]?.Value<string>() ?? "";
                    if (string.IsNullOrEmpty(contentDelta))
                        break;

                    var rawItemId = msg?["item_id"]?.Value<string>() ?? msg?["itemId"]?.Value<string>() ?? "";
                    var threadId = msg?["thread_id"]?.Value<string>()
                        ?? msg?["threadId"]?.Value<string>()
                        ?? @params?["conversationId"]?.Value<string>()
                        ?? "";
                    var turnId = msg?["turn_id"]?.Value<string>() ?? msg?["turnId"]?.Value<string>() ?? "";

                    EnsureTurnStarted(threadId, turnId);

                    if (ShouldAcceptAgentDelta(rawItemId, isV2: false))
                        Enqueue(() => OnStreamDelta?.Invoke(contentDelta));
                    break;
                }

                case "item/started":
                {
                    var isThreadId = @params?["threadId"]?.Value<string>() ?? @params?["thread_id"]?.Value<string>() ?? "";
                    var isTurnId = @params?["turn"]?["id"]?.Value<string>()
                        ?? @params?["turnId"]?.Value<string>()
                        ?? @params?["turn_id"]?.Value<string>()
                        ?? "";
                    var isItem = @params?["item"];
                    EnsureTurnStarted(isThreadId, isTurnId);
                    Enqueue(() => OnItemStarted?.Invoke(isThreadId, isTurnId, isItem));
                    break;
                }

                case "item/completed":
                {
                    var icThreadId = @params?["threadId"]?.Value<string>() ?? @params?["thread_id"]?.Value<string>() ?? "";
                    var icTurnId = @params?["turn"]?["id"]?.Value<string>()
                        ?? @params?["turnId"]?.Value<string>()
                        ?? @params?["turn_id"]?.Value<string>()
                        ?? "";
                    var icItem = @params?["item"];
                    EnsureTurnStarted(icThreadId, icTurnId);
                    Enqueue(() => OnItemCompleted?.Invoke(icThreadId, icTurnId, icItem));
                    break;
                }

                case "thread/name/updated":
                {
                    var tnThreadId = @params?["threadId"]?.Value<string>() ?? "";
                    var tnName = @params?["threadName"]?.Value<string>() ?? @params?["name"]?.Value<string>() ?? "";
                    Enqueue(() => OnThreadNameUpdated?.Invoke(tnThreadId, tnName));
                    break;
                }

                case "turn/plan/updated":
                {
                    var tpThreadId = @params?["threadId"]?.Value<string>() ?? @params?["thread_id"]?.Value<string>() ?? "";
                    var tpTurnId = @params?["turn"]?["id"]?.Value<string>()
                        ?? @params?["turnId"]?.Value<string>()
                        ?? @params?["turn_id"]?.Value<string>()
                        ?? "";
                    var tpExplanation = @params?["explanation"]?.Value<string>() ?? "";
                    var tpPlan = @params?["plan"] as JArray ?? new JArray();
                    EnsureTurnStarted(tpThreadId, tpTurnId);
                    Enqueue(() => OnPlanUpdated?.Invoke(tpThreadId, tpTurnId, tpExplanation, tpPlan));
                    break;
                }

                // ── Informational — silently ignore ─────────────────────
                case "thread/started":
                case "thread/tokenUsage/updated":
                case "account/updated":
                case "account/rateLimits/updated":
                case "turn/diff/updated":
                case "codex/event/agent_message":
                case "codex/event/agent_message_delta":
                case "codex/event/item_started":
                case "codex/event/item_completed":
                case "codex/event/user_message":
                case "codex/event/token_count":
                case "codex/event/task_started":
                case "codex/event/mcp_startup_complete":
                case "codex/event/deprecation_notice":
                case "codex/event/warning":
                case "codex/event/agent_reasoning_section_break":
                case "codex/event/agent_reasoning":
                case "deprecationNotice":
                    break;

                default:
                    Enqueue(() => Debug.Log("[Codex] Notification: " + method));
                    break;
            }
        }

        bool ShouldAcceptAgentDelta(string itemId, bool isV2)
        {
            if (string.IsNullOrEmpty(itemId))
                return true; // can't dedupe; accept

            lock (agentDeltaSourceLock)
            {
                if (agentDeltaSourceIsV2ByItemId.TryGetValue(itemId, out bool existingIsV2))
                    return existingIsV2 == isV2;

                agentDeltaSourceIsV2ByItemId[itemId] = isV2;

                // Prevent unbounded growth in long sessions.
                if (agentDeltaSourceIsV2ByItemId.Count > 512)
                    agentDeltaSourceIsV2ByItemId.Clear();

                return true;
            }
        }

        void EnsureTurnStarted(string threadId, string turnId)
        {
            if (string.IsNullOrEmpty(threadId) || string.IsNullOrEmpty(turnId))
                return;

            string key = threadId + ":" + turnId;
            lock (startedTurnsLock)
            {
                if (startedTurns.Contains(key))
                    return;
                startedTurns.Add(key);
            }

            Enqueue(() =>
            {
                CurrentTurnId = turnId;
                OnTurnStarted?.Invoke(turnId);
                OnTurnStartedFull?.Invoke(threadId, turnId);
            });
        }

        void Enqueue(Action a) => mainThreadQueue.Enqueue(a);
    }
}
