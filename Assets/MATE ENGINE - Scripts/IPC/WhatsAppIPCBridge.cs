using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;
using MateEngine.Codex;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

public class WhatsAppIPCBridge : MonoBehaviour
{
    [Serializable]
    public class WhatsAppRequest
    {
        public string id;
        public string type;
        public string chatJid;
        public string senderName;
        public string text;
        public string mediaPath;
        public string mediaType;
        public string timestamp;
    }

    [Header("Toggle")]
    public bool enableWhatsAppBridge = true;

    [Header("IPC Settings")]
    public int pollIntervalMs = 500;
    public bool debugLog = true;

    string requestsDir;
    string responsesDir;
    string errorsDir;

    Thread pollThread;
    volatile bool running;
    readonly ConcurrentQueue<WhatsAppRequest> incomingQueue = new();

    bool processingMessage;
    bool threadInitializing;
    float lastNotReadyLog;
    string currentRequestId;
    string currentChatJid;
    Animator avatarAnimator;

    void Awake()
    {
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string baseDir = Path.Combine(home, ".mate-engine", "ipc", "whatsapp");
        requestsDir = Path.Combine(baseDir, "requests");
        responsesDir = Path.Combine(baseDir, "responses");
        errorsDir = Path.Combine(baseDir, "errors");

        Directory.CreateDirectory(requestsDir);
        Directory.CreateDirectory(responsesDir);
        Directory.CreateDirectory(errorsDir);

        // Cleanup stale files on startup
        CleanupStaleFiles(requestsDir, TimeSpan.FromMinutes(5));
        CleanupStaleFiles(responsesDir, TimeSpan.FromMinutes(5));
    }

    void Start()
    {
        avatarAnimator = GetComponent<Animator>();
    }

    void OnEnable()
    {
        running = true;
        pollThread = new Thread(PollLoop) { IsBackground = true };
        pollThread.Start();
        if (debugLog) Debug.Log("[WhatsAppIPC] Started polling " + requestsDir);
    }

    void OnDisable()
    {
        running = false;
        try { pollThread?.Join(2000); } catch { }
        pollThread = null;
    }

    void PollLoop()
    {
        while (running)
        {
            try
            {
                if (Directory.Exists(requestsDir))
                {
                    var files = Directory.GetFiles(requestsDir, "*.json");
                    Array.Sort(files);
                    foreach (var file in files)
                    {
                        try
                        {
                            string json = File.ReadAllText(file);
                            var req = JsonConvert.DeserializeObject<WhatsAppRequest>(json);
                            if (req != null && req.type == "whatsapp_message"
                                && !string.IsNullOrEmpty(req.text))
                            {
                                incomingQueue.Enqueue(req);
                                File.Delete(file);
                            }
                            else
                            {
                                File.Move(file, Path.Combine(errorsDir, Path.GetFileName(file)));
                            }
                        }
                        catch (Exception ex)
                        {
                            if (debugLog) Debug.LogWarning("[WhatsAppIPC] Error reading file: " + ex.Message);
                            try { File.Move(file, Path.Combine(errorsDir, Path.GetFileName(file))); } catch { }
                        }
                    }
                }
            }
            catch { }

            Thread.Sleep(pollIntervalMs);
        }
    }

    void Update()
    {
        if (!enableWhatsAppBridge) return;

        if (incomingQueue.TryDequeue(out var request))
        {
            if (processingMessage)
            {
                // Steer: ack the previous request, then send new input to running turn
                SteerWithNewMessage(request);
            }
            else
            {
                processingMessage = true;
                ProcessWhatsAppMessage(request);
            }
        }
    }

    void SteerWithNewMessage(WhatsAppRequest request)
    {
        // Silently ack the previous request so NanoClaw's waiter completes (empty text = no WhatsApp message)
        if (!string.IsNullOrEmpty(currentRequestId))
            WriteResponse(currentRequestId, currentChatJid, "");

        // Send new message to the running turn (app-server injects into pending input)
        ProcessWhatsAppMessage(request);
    }

    void InitializeThread()
    {
        if (threadInitializing) return;
        var bridge = CodexBridge.Instance;
        if (bridge == null || !bridge.IsConnected || !bridge.IsAuthenticated) return;

        threadInitializing = true;
        if (debugLog) Debug.Log("[WhatsAppIPC] Auto-initializing Codex thread...");

        var data = SaveLoadHandler.Instance?.data;
        bool animDirectives = data?.enableAnimationDirectives ?? false;
        string systemPrompt = bridge.BuildSystemPrompt("", animDirectives);
        string savedThread = data?.codexThreadId;
        string model = data?.codexModel ?? "";

        if (!string.IsNullOrEmpty(savedThread))
        {
            bridge.ResumeThread(savedThread, (ok) =>
            {
                if (!ok)
                    bridge.StartThread(model, systemPrompt, OnThreadReady);
                else
                    OnThreadReady(savedThread);
            });
        }
        else
        {
            bridge.StartThread(model, systemPrompt, OnThreadReady);
        }
    }

    void OnThreadReady(string threadId)
    {
        if (debugLog) Debug.Log("[WhatsAppIPC] Thread ready: " + threadId);

        if (SaveLoadHandler.Instance != null)
        {
            SaveLoadHandler.Instance.data.codexThreadId = threadId;
            SaveLoadHandler.Instance.SaveToDisk();
        }

        threadInitializing = false;
    }

    void ProcessWhatsAppMessage(WhatsAppRequest request)
    {
        var bridge = CodexBridge.Instance;
        if (bridge == null || !bridge.IsConnected || string.IsNullOrEmpty(bridge.CurrentThreadId))
        {
            // Try to auto-initialize a thread
            if (!threadInitializing)
                InitializeThread();

            // Throttle log to once per second
            if (debugLog && Time.time - lastNotReadyLog > 1f)
            {
                Debug.LogWarning("[WhatsAppIPC] CodexBridge not ready, re-queuing message");
                lastNotReadyLog = Time.time;
            }
            incomingQueue.Enqueue(request);
            processingMessage = false;
            return;
        }

        currentRequestId = request.id;
        currentChatJid = request.chatJid;

        // Build input text with sender context and optional media path
        string inputText = $"[WhatsApp from {request.senderName}]: {request.text}";
        if (!string.IsNullOrEmpty(request.mediaPath))
            inputText += $"\n[Attached file: {request.mediaPath} (type: {request.mediaType ?? "unknown"})]";

        if (debugLog) Debug.Log("[WhatsAppIPC] Sending to Codex: " + inputText);

        string fullResponse = "";

        // Setup animation directive processor
        AnimationDirectiveProcessor animProc = null;
        var data = SaveLoadHandler.Instance?.data;
        if (data != null && data.enableAnimationDirectives)
        {
            animProc = new AnimationDirectiveProcessor();
            if (avatarAnimator == null) avatarAnimator = GetComponent<Animator>();
            var blendshapes = avatarAnimator != null
                ? avatarAnimator.GetComponentInChildren<UniversalBlendshapes>()
                : null;
            animProc.SetTargets(avatarAnimator, blendshapes);
        }

        if (avatarAnimator != null)
            avatarAnimator.SetBool("isTalking", true);

        Action<string> onStream = (partial) =>
        {
            fullResponse = partial;
            if (animProc != null)
                animProc.ProcessText(partial, this);
        };

        Action onComplete = () =>
        {
            if (avatarAnimator != null)
                avatarAnimator.SetBool("isTalking", false);

            // Clean response: strip animation directives and think tags
            string cleanResponse = fullResponse;
            if (animProc != null)
                cleanResponse = animProc.ProcessText(fullResponse, this);

            cleanResponse = Regex.Replace(cleanResponse ?? "", @"<think>[\s\S]*?</think>\s*", "").Trim();
            cleanResponse = Regex.Replace(cleanResponse ?? "", @"<!--anim:.*?-->", "").Trim();

            if (debugLog) Debug.Log("[WhatsAppIPC] Response: " + cleanResponse);

            var attachments = ExtractFilePaths(cleanResponse);
            if (debugLog && attachments.Count > 0)
                Debug.Log("[WhatsAppIPC] Attachments: " + string.Join(", ", attachments));

            WriteResponse(request.id, request.chatJid, cleanResponse, attachments);
            processingMessage = false;
        };

        bridge.SendMessage(inputText, onStream, onComplete);
    }

    void WriteResponse(string requestId, string chatJid, string text, List<string> attachments = null)
    {
        try
        {
            var response = new JObject
            {
                ["id"] = requestId,
                ["type"] = "whatsapp_response",
                ["chatJid"] = chatJid,
                ["text"] = text,
                ["attachments"] = new JArray(attachments?.ToArray() ?? Array.Empty<string>()),
                ["timestamp"] = DateTime.UtcNow.ToString("o")
            };

            string filename = $"{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}-{Guid.NewGuid().ToString("N").Substring(0, 6)}.json";
            string filePath = Path.Combine(responsesDir, filename);
            string tempPath = filePath + ".tmp";

            File.WriteAllText(tempPath, response.ToString(Formatting.Indented));
            File.Move(tempPath, filePath);

            if (debugLog) Debug.Log("[WhatsAppIPC] Wrote response: " + filename);
        }
        catch (Exception ex)
        {
            Debug.LogError("[WhatsAppIPC] Failed to write response: " + ex.Message);
        }
    }

    static readonly Regex FilePathRegex = new Regex(
        @"(?:`([^`]+\.\w{1,10})`|(?:/[\w\p{L}\p{N}.@~\- ]+)+\.\w{1,10}|\.dawn/[\w\p{L}\p{N}.@/\- ]+\.\w{1,10})",
        RegexOptions.Compiled);

    List<string> ExtractFilePaths(string text)
    {
        var result = new List<string>();
        if (string.IsNullOrEmpty(text)) return result;

        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string dawnHome = Path.Combine(home, ".dawn");
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (Match match in FilePathRegex.Matches(text))
        {
            string p = match.Value;

            // Strip backticks if present
            if (p.StartsWith("`") && p.EndsWith("`"))
                p = p.Substring(1, p.Length - 2);

            // Expand ~
            if (p.StartsWith("~/"))
                p = Path.Combine(home, p.Substring(2));

            // Resolve relative .dawn/ paths against Codex cwd
            if (p.StartsWith(".dawn/"))
                p = Path.Combine(dawnHome, p.Substring(6));

            // Resolve other relative paths against Codex cwd
            if (!Path.IsPathRooted(p))
                p = Path.Combine(dawnHome, p);

            if (seen.Add(p) && File.Exists(p))
                result.Add(p);
        }

        return result;
    }

    void CleanupStaleFiles(string directory, TimeSpan maxAge)
    {
        try
        {
            if (!Directory.Exists(directory)) return;
            var files = Directory.GetFiles(directory, "*.json");
            var cutoff = DateTime.UtcNow - maxAge;
            foreach (var file in files)
            {
                if (File.GetLastWriteTimeUtc(file) < cutoff)
                {
                    try { File.Delete(file); } catch { }
                }
            }
        }
        catch { }
    }
}
