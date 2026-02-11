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
        public string groupFolder;
        public string chatJid;
        public string senderName;
        public string text;
        public string mediaPath;
        public string mediaType;
        public string timestamp;
    }

    public static WhatsAppIPCBridge Instance { get; private set; }

    /// <summary>Current group folder being processed (for approval context)</summary>
    public string CurrentGroupFolder => currentGroupFolder;
    /// <summary>Current chat JID being processed (for approval context)</summary>
    public string CurrentChatJid => currentChatJid;

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
    string currentGroupFolder;
    Animator avatarAnimator;
    AvatarConfigLoader configLoader;

    void Awake()
    {
        Instance = this;

        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string baseDir = Path.Combine(home, ".mate-engine", "ipc", "whatsapp");
        requestsDir = Path.Combine(baseDir, "requests");
        responsesDir = Path.Combine(baseDir, "responses");
        errorsDir = Path.Combine(baseDir, "errors");

        Directory.CreateDirectory(requestsDir);
        Directory.CreateDirectory(responsesDir);
        Directory.CreateDirectory(errorsDir);

        // Initialize avatar config loader with hot-reload
        configLoader = new AvatarConfigLoader();
        configLoader.LoadAll();
        configLoader.StartWatching();
        if (debugLog) Debug.Log($"[WhatsAppIPC] Loaded {configLoader.AvatarConfigs.Count} avatar config(s)");

        // Cleanup stale files on startup
        CleanupStaleFiles(requestsDir, TimeSpan.FromMinutes(5));
        CleanupStaleFiles(responsesDir, TimeSpan.FromMinutes(5));

        // Also cleanup approval IPC dirs
        string approvalBase = Path.Combine(baseDir, "approvals");
        CleanupStaleFiles(Path.Combine(approvalBase, "requests"), TimeSpan.FromMinutes(5));
        CleanupStaleFiles(Path.Combine(approvalBase, "responses"), TimeSpan.FromMinutes(5));
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
        configLoader?.StopWatching();
        if (Instance == this) Instance = null;
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
                            Debug.Log($"[WhatsAppIPC] Raw JSON: {json}");
                            var req = JsonConvert.DeserializeObject<WhatsAppRequest>(json);
                            Debug.Log($"[WhatsAppIPC] Parsed: groupFolder='{req?.groupFolder}', type='{req?.type}'");
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
        // Pump hot-reload config changes on main thread
        configLoader?.PumpChanges();

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
            WriteResponse(currentRequestId, currentChatJid, "", groupFolder: currentGroupFolder);

        // Send new message to the running turn (app-server injects into pending input)
        ProcessWhatsAppMessage(request);
    }


    void ProcessWhatsAppMessage(WhatsAppRequest request)
    {
        // Lookup avatar for this group
        string groupFolder = request.groupFolder ?? "_default";
        string avatarId = configLoader.GetAvatarForGroup(groupFolder);

        // Skip unassigned groups - no response
        if (avatarId == null)
        {
            if (debugLog) Debug.Log($"[WhatsAppIPC] Ignoring message from unassigned group '{groupFolder}'");
            processingMessage = false;
            return;
        }

        var avatarConfig = configLoader.GetAvatarConfig(avatarId);

        if (avatarConfig == null)
        {
            Debug.LogError($"[WhatsAppIPC] No config found for avatar '{avatarId}', group '{groupFolder}'");
            WriteResponse(request.id, request.chatJid, "Configuration error", groupFolder: groupFolder);
            processingMessage = false;
            return;
        }

        var bridge = CodexBridge.Instance;
        if (bridge == null || !bridge.IsConnected || string.IsNullOrEmpty(bridge.CurrentThreadId))
        {
            // Try to auto-initialize a thread with avatar config
            if (!threadInitializing)
                InitializeThreadForAvatar(avatarId, groupFolder, avatarConfig);

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
        currentGroupFolder = groupFolder;

        // Build input text with sender context and optional media path
        string inputText = $"[WhatsApp from {request.senderName}]: {request.text}";
        if (!string.IsNullOrEmpty(request.mediaPath))
            inputText += $"\n[Attached file: {request.mediaPath} (type: {request.mediaType ?? "unknown"})]";

        if (debugLog) Debug.Log($"[WhatsAppIPC] [{groupFolder}→{avatarId}] Sending to Codex: " + inputText);

        string fullResponse = "";

        // Setup animation directive processor based on avatar config and animation mode
        AnimationDirectiveProcessor animProc = null;
        bool shouldProcessAnimations = ShouldProcessAnimations(avatarId, avatarConfig);

        if (shouldProcessAnimations)
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

            WriteResponse(request.id, request.chatJid, cleanResponse, attachments, groupFolder);
            processingMessage = false;
        };

        // Register source metadata for task monitor
        bridge.TaskTracker.RegisterSourceMetadata(bridge.CurrentThreadId, new MateEngine.Codex.TaskSourceMetadata
        {
            SourceType = "whatsapp",
            SourceGroup = groupFolder,
            SourceSender = request.senderName,
            InputPreview = request.text
        });

        // Pass avatar config values to override app settings
        bridge.SendMessage(inputText, onStream, onComplete,
            avatarConfig.model, avatarConfig.modelProvider, avatarConfig.workingDirectory,
            avatarConfig.approvalPolicy, avatarConfig.sandboxPolicy);
    }

    bool ShouldProcessAnimations(string avatarId, ResolvedAvatarConfig config)
    {
        if (!config.enableAnimationDirectives)
        {
            if (debugLog) Debug.Log($"[WhatsAppIPC] Animation disabled for avatar '{avatarId}'");
            return false;
        }

        var globalConfig = configLoader.GlobalConfig;
        if (globalConfig.animationMode == "rush")
        {
            if (debugLog) Debug.Log($"[WhatsAppIPC] Animation mode: RUSH - processing animations for '{avatarId}'");
            return true;
        }

        // Focus mode: only active avatar's groups trigger animations
        bool isFocused = avatarId == globalConfig.activeAvatar;
        if (debugLog) Debug.Log($"[WhatsAppIPC] Animation mode: FOCUS - avatar '{avatarId}' {(isFocused ? "IS" : "is NOT")} active (active: '{globalConfig.activeAvatar}')");
        return isFocused;
    }

    void InitializeThreadForAvatar(string avatarId, string groupFolder, ResolvedAvatarConfig config)
    {
        if (threadInitializing) return;
        var bridge = CodexBridge.Instance;
        if (bridge == null || !bridge.IsConnected || !bridge.IsAuthenticated) return;

        threadInitializing = true;
        if (debugLog) Debug.Log($"[WhatsAppIPC] Initializing thread for avatar '{avatarId}', group '{groupFolder}'...");

        // Build system prompt from avatar config
        string baseInstructions = config.baseInstructionsContent ?? "";
        string devInstructions = config.developerInstructionsContent ?? "";
        string systemPrompt = bridge.BuildSystemPrompt(baseInstructions);

        if (debugLog) Debug.Log($"[WhatsAppIPC] Developer instructions: {(string.IsNullOrEmpty(devInstructions) ? "(none)" : devInstructions.Length + " chars")}");

        // Check for existing thread
        string savedThreadId = configLoader.GetThreadId(avatarId, groupFolder);
        string model = config.model ?? "";
        string provider = config.modelProvider ?? "";

        if (!string.IsNullOrEmpty(savedThreadId))
        {
            if (debugLog) Debug.Log($"[WhatsAppIPC] Resuming existing thread '{savedThreadId}' for {avatarId}/{groupFolder}");
            bridge.ResumeThread(savedThreadId, (ok) =>
            {
                if (!ok)
                {
                    if (debugLog) Debug.Log($"[WhatsAppIPC] Resume failed, creating new thread for {avatarId}/{groupFolder}");
                    StartNewThread(avatarId, groupFolder, model, provider, systemPrompt, devInstructions);
                }
                else
                    OnThreadReadyForAvatar(avatarId, groupFolder, savedThreadId);
            });
        }
        else
        {
            if (debugLog) Debug.Log($"[WhatsAppIPC] No saved thread, creating new for {avatarId}/{groupFolder}");
            StartNewThread(avatarId, groupFolder, model, provider, systemPrompt, devInstructions);
        }
    }

    void StartNewThread(string avatarId, string groupFolder, string model, string provider, string systemPrompt, string developerInstructions)
    {
        var bridge = CodexBridge.Instance;
        bridge.StartThread(model, provider, systemPrompt, developerInstructions, (threadId) =>
        {
            OnThreadReadyForAvatar(avatarId, groupFolder, threadId);
        });
    }

    void OnThreadReadyForAvatar(string avatarId, string groupFolder, string threadId)
    {
        if (debugLog) Debug.Log($"[WhatsAppIPC] Thread ready for {avatarId}/{groupFolder}: {threadId}");

        // Save thread ID to avatar state
        configLoader.UpdateThreadId(avatarId, groupFolder, threadId);
        threadInitializing = false;
    }

    void WriteResponse(string requestId, string chatJid, string text, List<string> attachments = null, string groupFolder = null)
    {
        try
        {
            var response = new JObject
            {
                ["id"] = requestId,
                ["type"] = "whatsapp_response",
                ["groupFolder"] = groupFolder ?? "_default",
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
