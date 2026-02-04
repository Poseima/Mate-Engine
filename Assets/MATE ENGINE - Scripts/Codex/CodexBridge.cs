using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace MateEngine.Codex
{
    public class CodexBridge : MonoBehaviour
    {
        public static CodexBridge Instance { get; private set; }

        [Header("Binary")]
        [Tooltip("Name of the codex-app-server binary inside StreamingAssets")]
        public string binaryName = "codex-app-server";

        // ── State ──────────────────────────────────────────────────

        public bool IsConnected { get; private set; }
        public bool IsAuthenticated { get; private set; }
        public string CurrentThreadId { get; private set; }
        public List<ModelInfo> AvailableModels { get; private set; } = new();
        public List<ProviderInfo> AvailableProviders { get; private set; } = new();

        readonly CodexProtocolHandler protocol = new();

        // Current turn state
        Action<string> currentStreamCb;
        Action currentCompleteCb;
        string streamBuffer = "";

        // ── Events ─────────────────────────────────────────────────

        public event Action OnConnected;
        public event Action<bool, string> OnLoginResult;
        public event Action<List<ModelInfo>> OnModelsLoaded;
        public event Action<List<ProviderInfo>> OnProvidersLoaded;
        public event Action<string> OnError;

        // ── Lifecycle ──────────────────────────────────────────────

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }

        void Start()
        {
            if (!IsConnected)
                Initialize();
        }

        void Update()
        {
            protocol.Pump();
        }

        void OnDestroy()
        {
            protocol.Stop();
            if (Instance == this) Instance = null;
        }

        void OnApplicationQuit()
        {
            protocol.Stop();
        }

        // ── Initialize ────────────────────────────────────────────

        public void Initialize(string apiKey = null)
        {
            string binaryPath = Path.Combine(Application.streamingAssetsPath, binaryName);

            #if UNITY_STANDALONE_OSX || UNITY_EDITOR_OSX
            try
            {
                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "chmod",
                    Arguments = "+x \"" + binaryPath + "\"",
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                System.Diagnostics.Process.Start(psi)?.WaitForExit(3000);
            }
            catch { }
            #endif

            if (!File.Exists(binaryPath))
            {
                Debug.LogError("[CodexBridge] Binary not found at: " + binaryPath);
                OnError?.Invoke("codex-app-server binary not found. Place it in StreamingAssets.");
                return;
            }

            var env = LoadShellEnvironment();
            Debug.Log("[CodexBridge] Loaded " + env.Count + " env vars from shell profile.");
            if (!string.IsNullOrEmpty(apiKey))
                env["CODEX_API_KEY"] = apiKey;

            // Wire protocol events
            protocol.OnStreamDelta += HandleDelta;
            protocol.OnTurnCompleted += HandleTurnCompleted;
            protocol.OnLoginCompleted += HandleLoginCompleted;
            protocol.OnError += (msg) => { Debug.LogError("[CodexBridge] " + msg); OnError?.Invoke(msg); };

            protocol.Start(binaryPath, env);

            // Send initialize request then initialized notification
            var initParams = new InitializeParams
            {
                clientInfo = new ClientInfo
                {
                    name = "MateEngine",
                    title = "Mate Engine",
                    version = Application.version ?? "1.0.0"
                }
            };

            Debug.Log("[CodexBridge] Sending initialize request to binary...");
            protocol.SendRequest("initialize", initParams, (result, error) =>
            {
                if (error != null)
                {
                    Debug.LogError("[CodexBridge] initialize failed: " + error.message);
                    OnError?.Invoke("initialize failed: " + error.message);
                    return;
                }

                // Send initialized notification to complete handshake
                protocol.SendNotification("initialized");
                Debug.Log("[CodexBridge] Handshake complete.");

                IsConnected = true;

                // If API key was provided, authenticate; otherwise check cached auth
                if (!string.IsNullOrEmpty(apiKey))
                {
                    Debug.Log("[CodexBridge] API key provided, authenticating via API key...");
                    AuthenticateApiKey(apiKey);
                }
                else
                {
                    Debug.Log("[CodexBridge] No API key provided, checking cached auth status...");
                    CheckAuthStatus();
                }

                OnConnected?.Invoke();
            });
        }

        // ── Auth ───────────────────────────────────────────────────

        public void AuthenticateApiKey(string apiKey)
        {
            var p = new LoginApiKeyParams { apiKey = apiKey };
            protocol.SendRequest("account/login/start", p, (result, error) =>
            {
                if (error != null)
                {
                    IsAuthenticated = false;
                    OnLoginResult?.Invoke(false, error.message);
                    return;
                }
                IsAuthenticated = true;
                OnLoginResult?.Invoke(true, null);
                Debug.Log("[CodexBridge] API key auth succeeded.");
            });
        }

        public void AuthenticateOAuth()
        {
            var p = new LoginChatGptParams();
            protocol.SendRequest("account/login/start", p, (result, error) =>
            {
                if (error != null)
                {
                    OnLoginResult?.Invoke(false, error.message);
                    return;
                }
                var url = result?["authUrl"]?.Value<string>();
                if (!string.IsNullOrEmpty(url))
                {
                    Application.OpenURL(url);
                    Debug.Log("[CodexBridge] Opened browser for OAuth: " + url);
                }
            });
        }

        public void CheckAuthStatus()
        {
            // Check if auth.json exists on disk
            string authPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex", "auth.json");
            Debug.Log("[CodexBridge] Auth file path: " + authPath);
            Debug.Log("[CodexBridge] Auth file exists: " + File.Exists(authPath));
            if (File.Exists(authPath))
            {
                try
                {
                    var content = File.ReadAllText(authPath);
                    Debug.Log("[CodexBridge] Auth file size: " + content.Length + " bytes");
                    // Log keys only (not values) to avoid leaking tokens
                    var json = JObject.Parse(content);
                    Debug.Log("[CodexBridge] Auth file keys: " + string.Join(", ", json.Properties()));
                }
                catch (Exception e)
                {
                    Debug.LogWarning("[CodexBridge] Failed to read auth file: " + e.Message);
                }
            }

            Debug.Log("[CodexBridge] Sending getAuthStatus RPC to binary...");
            var p = new GetAuthStatusParams();
            protocol.SendRequest("getAuthStatus", p, (result, error) =>
            {
                if (error != null)
                {
                    Debug.LogError("[CodexBridge] Auth status check failed: " + error.message);
                    return;
                }
                Debug.Log("[CodexBridge] getAuthStatus response: " + (result?.ToString() ?? "null"));
                var authMethod = result?["authMethod"]?.Value<string>();
                var requiresOpenaiAuth = result?["requiresOpenaiAuth"]?.Value<bool>() ?? true;

                if (!string.IsNullOrEmpty(authMethod))
                {
                    IsAuthenticated = true;
                    OnLoginResult?.Invoke(true, null);
                    Debug.Log("[CodexBridge] Auto-authenticated via cached " + authMethod);
                }
                else if (!requiresOpenaiAuth)
                {
                    // Provider handles its own auth (e.g. API key via env) — no OpenAI login needed
                    IsAuthenticated = true;
                    OnLoginResult?.Invoke(true, null);
                    Debug.Log("[CodexBridge] Provider does not require OpenAI auth — treating as authenticated.");
                }
                else
                {
                    Debug.Log("[CodexBridge] No cached auth — manual login required.");
                }
            });
        }

        void HandleLoginCompleted(bool success, string error)
        {
            IsAuthenticated = success;
            OnLoginResult?.Invoke(success, error);
            if (success)
                Debug.Log("[CodexBridge] OAuth login succeeded.");
            else
                Debug.LogWarning("[CodexBridge] OAuth login failed: " + error);
        }

        // ── Models ─────────────────────────────────────────────────

        public void ListModels(Action<List<ModelInfo>> callback = null)
        {
            protocol.SendRequest("model/list", new ModelListParams(), (result, error) =>
            {
                if (error != null)
                {
                    Debug.LogWarning("[CodexBridge] model/list error: " + error.message);
                    return;
                }
                try
                {
                    var list = result?["data"]?.ToObject<List<ModelInfo>>() ?? new List<ModelInfo>();
                    AvailableModels = list;
                    OnModelsLoaded?.Invoke(list);
                    callback?.Invoke(list);
                }
                catch (Exception e)
                {
                    Debug.LogWarning("[CodexBridge] Failed to parse model list: " + e.Message);
                }
            });
        }

        // ── Providers ──────────────────────────────────────────────

        public void ListProviders(Action<List<ProviderInfo>> callback = null)
        {
            protocol.SendRequest("provider/list", new ProviderListParams(), (result, error) =>
            {
                if (error != null)
                {
                    Debug.LogWarning("[CodexBridge] provider/list error: " + error.message);
                    return;
                }
                try
                {
                    var list = result?["data"]?.ToObject<List<ProviderInfo>>() ?? new List<ProviderInfo>();
                    AvailableProviders = list;
                    OnProvidersLoaded?.Invoke(list);
                    callback?.Invoke(list);
                }
                catch (Exception e)
                {
                    Debug.LogWarning("[CodexBridge] Failed to parse provider list: " + e.Message);
                }
            });
        }

        // ── Thread management ──────────────────────────────────────

        public void StartThread(string model, string systemPrompt, Action<string> onThreadStarted = null)
        {
            string dawnHome = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".dawn");
            if (!Directory.Exists(dawnHome)) Directory.CreateDirectory(dawnHome);

            Debug.Log("[CodexBridge] System prompt:\n" + systemPrompt);

            // Load dawn.md as developer instructions (agent behavior rules)
            string dawnInstructions = null;
            string dawnPath = Path.Combine(Application.streamingAssetsPath, "codex-dawn.md");
            if (File.Exists(dawnPath))
            {
                dawnInstructions = File.ReadAllText(dawnPath);
                Debug.Log("[CodexBridge] Loaded dawn instructions (" + dawnInstructions.Length + " chars) from: " + dawnPath);
            }
            else
            {
                Debug.LogWarning("[CodexBridge] codex-dawn.md not found at: " + dawnPath);
            }

            string provider = SaveLoadHandler.Instance?.data?.codexProvider;

            var p = new ThreadStartParams
            {
                model = string.IsNullOrEmpty(model) ? null : model,
                modelProvider = string.IsNullOrEmpty(provider) ? null : provider,
                baseInstructions = systemPrompt,
                developerInstructions = dawnInstructions,
                approvalPolicy = "never",
                cwd = dawnHome,
            };
            protocol.SendRequest("thread/start", p, (result, error) =>
            {
                if (error != null)
                {
                    OnError?.Invoke("thread/start failed: " + error.message);
                    return;
                }
                // Response contains { thread: { id: "..." }, model, ... }
                CurrentThreadId = result?["thread"]?["id"]?.Value<string>() ?? "";
                Debug.Log("[CodexBridge] Thread started: " + CurrentThreadId);
                onThreadStarted?.Invoke(CurrentThreadId);
            });
        }

        public void ResumeThread(string threadId, Action<bool> onResult = null)
        {
            var p = new ThreadResumeParams { threadId = threadId };
            protocol.SendRequest("thread/resume", p, (result, error) =>
            {
                if (error != null)
                {
                    Debug.LogWarning("[CodexBridge] thread/resume failed: " + error.message);
                    CurrentThreadId = null;
                    onResult?.Invoke(false);
                    return;
                }
                CurrentThreadId = threadId;
                Debug.Log("[CodexBridge] Thread resumed: " + threadId);
                onResult?.Invoke(true);
            });
        }

        // ── Messaging (matches LLMCharacter.Chat callback pattern) ─

        public void SendMessage(string text, Action<string> onStream, Action onComplete)
        {
            if (!IsConnected || !protocol.IsRunning)
            {
                Debug.LogWarning("[CodexBridge] Not connected. Ignoring message.");
                onComplete?.Invoke();
                return;
            }

            if (string.IsNullOrEmpty(CurrentThreadId))
            {
                Debug.LogWarning("[CodexBridge] No active thread. Ignoring message.");
                onComplete?.Invoke();
                return;
            }

            streamBuffer = "";
            currentStreamCb = onStream;
            currentCompleteCb = onComplete;

            string dawnHome = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".dawn");

            var p = new TurnStartParams
            {
                threadId = CurrentThreadId,
                input = new List<UserInput>
                {
                    new UserInput { type = "text", text = text }
                },
                approvalPolicy = "never",
                sandboxPolicy = new SandboxPolicyParam(),
                cwd = dawnHome,
            };

            // Override model + provider per-turn to match current Codex Configuration
            string currentModel = SaveLoadHandler.Instance?.data?.codexModel ?? "";
            if (!string.IsNullOrEmpty(currentModel))
            {
                p.collaborationMode = new CollaborationModeParam
                {
                    settings = new CollaborationModeSettings { model = currentModel }
                };
            }

            string currentProvider = SaveLoadHandler.Instance?.data?.codexProvider ?? "";
            if (!string.IsNullOrEmpty(currentProvider))
                p.providerId = currentProvider;

            protocol.SendRequest("turn/start", p, (result, error) =>
            {
                if (error != null)
                {
                    OnError?.Invoke("turn/start failed: " + error.message);
                    currentStreamCb = null;
                    currentCompleteCb?.Invoke();
                    currentCompleteCb = null;
                }
            });
        }

        void HandleDelta(string delta)
        {
            if (string.IsNullOrEmpty(delta)) return;

            Debug.Log("[CodexBridge RAW delta] " + delta);

            // Strip <think>…</think> blocks (some models wrap reasoning in these)
            string clean = StripThinkTags(delta);
            if (string.IsNullOrEmpty(clean)) return;

            streamBuffer += clean;
            currentStreamCb?.Invoke(streamBuffer);
        }

        void HandleTurnCompleted(string threadId)
        {
            if (!string.IsNullOrEmpty(threadId))
                CurrentThreadId = threadId;

            currentCompleteCb?.Invoke();
            currentStreamCb = null;
            currentCompleteCb = null;
            streamBuffer = "";
        }

        static string StripThinkTags(string text)
        {
            if (text == null) return text;
            // Remove <think>…</think> blocks including newlines around them
            var result = System.Text.RegularExpressions.Regex.Replace(
                text, @"<think>[\s\S]*?</think>\s*", "", System.Text.RegularExpressions.RegexOptions.None);
            return result.TrimStart('\n', '\r');
        }

        // ── Interrupt ──────────────────────────────────────────────

        public void InterruptTurn()
        {
            if (!string.IsNullOrEmpty(CurrentThreadId) && !string.IsNullOrEmpty(protocol.CurrentTurnId))
            {
                var p = new TurnInterruptParams
                {
                    threadId = CurrentThreadId,
                    turnId = protocol.CurrentTurnId
                };
                protocol.SendRequest("turn/interrupt", p);
            }

            currentStreamCb = null;
            currentCompleteCb?.Invoke();
            currentCompleteCb = null;
            streamBuffer = "";
        }

        // ── System prompt helper ───────────────────────────────────

        public string BuildSystemPrompt(string userPrompt, bool includeClipTable)
        {
            string basePrompt = "";
            string basePath = Path.Combine(Application.streamingAssetsPath, "codex-system-prompt.md");
            if (File.Exists(basePath))
                basePrompt = File.ReadAllText(basePath);

            string combined = basePrompt;
            if (!string.IsNullOrEmpty(userPrompt))
                combined += "\n\n" + userPrompt;

            if (includeClipTable)
            {
                string clipPath = Path.Combine(Application.streamingAssetsPath, "codex-clip-table.md");
                if (File.Exists(clipPath))
                    combined += "\n\n" + File.ReadAllText(clipPath);
            }

            return combined;
        }

        // ── Shell environment loader ────────────────────────────────

        /// <summary>
        /// Launches a login shell to capture the user's full environment (from ~/.zshrc etc.).
        /// macOS GUI apps don't inherit terminal env vars, so we need this to get
        /// proxy settings, API keys, and other vars the codex binary needs.
        /// </summary>
        static Dictionary<string, string> LoadShellEnvironment()
        {
            var result = new Dictionary<string, string>();
            try
            {
                string shell = Environment.GetEnvironmentVariable("SHELL") ?? "/bin/zsh";
                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = shell,
                    Arguments = "-ilc env",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };
                using var proc = System.Diagnostics.Process.Start(psi);
                string output = proc.StandardOutput.ReadToEnd();
                proc.WaitForExit(5000);

                foreach (string line in output.Split('\n'))
                {
                    int eq = line.IndexOf('=');
                    if (eq > 0)
                    {
                        string key = line.Substring(0, eq);
                        string val = line.Substring(eq + 1);
                        result[key] = val;
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning("[CodexBridge] Failed to load shell environment: " + e.Message);
            }
            return result;
        }
    }
}
