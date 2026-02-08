using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using LLMUnity;
using MateEngine.Codex;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace LLMUnitySamples
{
    public class ChatBot : MonoBehaviour
    {
        [Header("Containers")]
        public Transform chatContainer;
        public Transform inputContainer;

        [Header("Colors & Font")]
        public Color playerColor = new Color32(81, 164, 81, 255);
        public Color aiColor = new Color32(29, 29, 73, 255);
        public Color fontColor = Color.white;
        public Font font;
        public int fontSize = 16;

        [Header("Bubble Layout")]
        public int bubbleWidth = 600;
        public float textPadding = 10f;
        public float bubbleSpacing = 10f;
        public float bottomPadding = 10f;
        public Sprite sprite;
        public Sprite roundedSprite16;
        public Sprite roundedSprite32;
        public Sprite roundedSprite64;

        [Header("LLM (Legacy — used as fallback when Codex is not configured)")]
        public LLMCharacter llmCharacter;

        [Header("Codex")]
        public bool useCodex = true;

        [Header("Input Settings")]
        public string inputPlaceholder = "Message me";

        [Header("Streaming Audio")]
        public AudioSource streamAudioSource;

        [Header("Bubble Materials")]
        public Material playerMaterial;
        public Material aiMaterial;
        [Header("Text Materials")]
        public Material playerTextMaterial;
        public Material aiTextMaterial;
        [Header("Scroll")]
        public ScrollRect scrollRect;
        public bool autoScrollOnNewMessage = true;
        public bool respectUserScroll = true;

        [Header("History")]
        [Min(0)] public int maxMessages = 100;
        public bool trimOnlyWhenAtBottom = true;
        public bool enableOffscreenTrim = false;

        [Header("Font Colors (per side)")]
        public Color playerFontColor = Color.white;
        public Color aiFontColor = Color.white;

        [Header("Rounded Sprite Radius")]
        [Range(0, 64)]
        public int cornerRadius = 16;
        private bool layoutDirty;

        private InputBubble inputBubble;
        private List<Bubble> chatBubbles = new List<Bubble>();
        private bool blockInput = true;
        private BubbleUI playerUI, aiUI;
        private bool warmUpDone = false;
        private int lastBubbleOutsideFOV = -1;

        private Animator avatarAnimator;
        private Animator lastAvatarAnimator;
        private static readonly int isTalkingHash = Animator.StringToHash("isTalking");

        private AnimationDirectiveProcessor animDirectiveProcessor;
        private bool codexReady;

        struct HistoryMessage
        {
            public bool isUser;
            public string text;
        }

        const int CodexHistoryBatchSize = 50;
        readonly List<HistoryMessage> codexHistory = new();
        int codexHistoryCursor;
        bool codexHistoryLoaded;
        bool codexHistoryLoading;
        string codexHistoryThreadId = "";

        static readonly Regex ThinkRegex = new Regex(@"<think>[\s\S]*?</think>\s*", RegexOptions.Compiled);
        static readonly Regex AnimRegex = new Regex(@"<!--anim:.*?-->", RegexOptions.Singleline | RegexOptions.Compiled);
        static readonly Regex CodexRolloutRegex = new Regex(@"rollout-(\d{4}-\d{2}-\d{2}T\d{2}-\d{2}-\d{2})", RegexOptions.Compiled);

        bool UseCodexBackend => useCodex && CodexBridge.Instance != null && CodexBridge.Instance.IsConnected;

        void Start()
        {
            avatarAnimator = GetComponent<Animator>();

            if (font == null) font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (cornerRadius <= 16) sprite = roundedSprite16;
            else if (cornerRadius <= 32) sprite = roundedSprite32;
            else sprite = roundedSprite64;

            playerUI = new BubbleUI
            {
                sprite = sprite,
                font = font,
                fontSize = fontSize,
                fontColor = playerFontColor,
                bubbleColor = playerColor,
                bottomPosition = 0,
                leftPosition = 0,
                textPadding = textPadding,
                bubbleOffset = bubbleSpacing,
                bubbleWidth = bubbleWidth,
                bubbleHeight = -1
            };

            aiUI = new BubbleUI
            {
                sprite = sprite,
                font = font,
                fontSize = fontSize,
                fontColor = aiFontColor,
                bubbleColor = aiColor,
                bottomPosition = 0,
                leftPosition = 1,
                textPadding = textPadding,
                bubbleOffset = bubbleSpacing,
                bubbleWidth = bubbleWidth,
                bubbleHeight = -1
            };

            Transform inputParent = inputContainer != null ? inputContainer : chatContainer;

            inputBubble = new InputBubble(inputParent, playerUI, "InputBubble", "Loading...", 4);
            inputBubble.AddSubmitListener(onInputFieldSubmit);
            inputBubble.AddValueChangedListener(onValueChanged);
            inputBubble.setInteractable(false);

            if (scrollRect != null)
                scrollRect.onValueChanged.AddListener(OnScrollChanged);

            FindAvatarSmart();
            InitializeBackend();
        }

        void InitializeBackend()
        {
            if (useCodex)
            {
                StartCoroutine(InitCodexRoutine());
            }
            else
            {
                ShowLoadedMessages();
                _ = llmCharacter.Warmup(WarmUpCallback);
            }
        }

        IEnumerator InitCodexRoutine()
        {
            // Wait one frame so CodexBridge singleton can initialize
            yield return null;

            var bridge = CodexBridge.Instance;
            if (bridge == null)
            {
                Debug.LogWarning("[ChatBot] CodexBridge not found in scene. Falling back to LLMCharacter.");
                useCodex = false;
                ShowLoadedMessages();
                _ = llmCharacter.Warmup(WarmUpCallback);
                yield break;
            }

            // CodexBridge auto-initializes in Start(); just wait for connection
            float timeout = 10f;
            while (!bridge.IsConnected && timeout > 0f)
            {
                timeout -= Time.deltaTime;
                yield return null;
            }

            if (!bridge.IsConnected)
            {
                Debug.LogWarning("[ChatBot] CodexBridge failed to connect. Falling back to LLMCharacter.");
                useCodex = false;
                ShowLoadedMessages();
                _ = llmCharacter.Warmup(WarmUpCallback);
                yield break;
            }

            // Wait briefly for auto-auth (CheckAuthStatus runs after handshake)
            float authTimeout = 3f;
            while (!bridge.IsAuthenticated && authTimeout > 0f)
            {
                authTimeout -= Time.deltaTime;
                yield return null;
            }

            // If still not authenticated, show prompt and wait for manual login
            if (!bridge.IsAuthenticated)
            {
                inputBubble.SetPlaceHolderText("Login in Settings to start chatting");
                inputBubble.setInteractable(false);
                bridge.OnLoginResult += OnCodexLoginResult;
                yield break;
            }

            // Already authenticated — start thread
            StartCodexThread(bridge);
        }

        void OnCodexLoginResult(bool success, string error)
        {
            if (!success) return;

            var bridge = CodexBridge.Instance;
            if (bridge != null)
            {
                bridge.OnLoginResult -= OnCodexLoginResult;
                StartCodexThread(bridge);
            }
        }

        void StartCodexThread(CodexBridge bridge)
        {
            var config = AvatarConfigLoader.Instance?.GetActiveAvatarConfig();
            string userPrompt = config?.baseInstructionsContent ?? "";
            var promptBinder = FindFirstObjectByType<AISystemPromptBinder>();
            if (promptBinder != null && promptBinder.input != null && !string.IsNullOrEmpty(promptBinder.input.text))
                userPrompt = promptBinder.input.text;

            string systemPrompt = bridge.BuildSystemPrompt(userPrompt);
            string savedThread = SaveLoadHandler.Instance?.data?.codexThreadId;
            string model = config?.model ?? "";
            string provider = config?.modelProvider ?? "";

            if (!string.IsNullOrEmpty(savedThread))
            {
                bridge.ResumeThread(savedThread, (ok) =>
                {
                    if (!ok)
                        bridge.StartThread(model, provider, systemPrompt, null, OnThreadReady);
                    else
                        OnThreadReady(savedThread);
                });
            }
            else
            {
                bridge.StartThread(model, provider, systemPrompt, null, OnThreadReady);
            }
        }

        void OnThreadReady(string threadId)
        {
            Debug.Log($"[ChatBot][OnThreadReady] UseCodexBackend={UseCodexBackend}, threadId={threadId}, codexHistoryThreadId={codexHistoryThreadId}, codexHistoryLoaded={codexHistoryLoaded}");

            // Save thread ID for persistence
            if (SaveLoadHandler.Instance != null)
            {
                SaveLoadHandler.Instance.data.codexThreadId = threadId;
                SaveLoadHandler.Instance.SaveToDisk();
            }

            codexReady = true;

            if (UseCodexBackend)
            {
                if (codexHistoryThreadId != threadId)
                {
                    Debug.Log($"[ChatBot][OnThreadReady] Thread ID changed ({codexHistoryThreadId} -> {threadId}), clearing bubbles and resetting history");
                    ClearChatBubbles();
                    codexHistoryLoaded = false;
                }

                codexHistoryThreadId = threadId;
                if (!codexHistoryLoaded)
                {
                    codexHistory.Clear();
                    codexHistory.AddRange(LoadCodexHistory(threadId));
                    LoadRecentCodexHistory();
                }
            }

            // Setup animation directive processor if enabled
            var data = SaveLoadHandler.Instance?.data;
            if (data != null && data.enableAnimationDirectives)
            {
                animDirectiveProcessor = new AnimationDirectiveProcessor();
            }

            WarmUpCallback();
        }

        void FindAvatarSmart()
        {
            Animator found = null;
            var loader = FindFirstObjectByType<VRMLoader>();
            if (loader != null)
            {
                var current = loader.GetCurrentModel();
                if (current != null) found = current.GetComponentsInChildren<Animator>(true).FirstOrDefault(a => a && a.gameObject.activeInHierarchy);
            }
            if (found == null)
            {
                var modelParent = GameObject.Find("Model");
                if (modelParent != null) found = modelParent.GetComponentsInChildren<Animator>(true).FirstOrDefault(a => a && a.gameObject.activeInHierarchy);
            }
            if (found == null)
            {
                var all = GameObject.FindObjectsByType<Animator>(FindObjectsInactive.Include, FindObjectsSortMode.None);
                found = all.FirstOrDefault(a => a && a.isActiveAndEnabled);
            }
            if (found != avatarAnimator)
            {
                avatarAnimator = found;
                lastAvatarAnimator = avatarAnimator;
            }
        }

        void RefreshAvatarIfChanged()
        {
            if (avatarAnimator == null || lastAvatarAnimator == null || avatarAnimator != lastAvatarAnimator)
            {
                FindAvatarSmart();
            }
        }


        private void MarkLayoutDirty()
        {
            layoutDirty = true;
        }

        void OnDisable()
        {
            if (streamAudioSource != null && streamAudioSource.isPlaying)
            {
                streamAudioSource.Stop();
                streamAudioSource.volume = 1f;
            }
            if (avatarAnimator != null) avatarAnimator.SetBool(isTalkingHash, false);
        }

        Bubble AddBubble(string message, bool isPlayerMessage, bool allowTrim = true, bool prepend = false)
        {
            Bubble bubble = new Bubble(chatContainer, isPlayerMessage ? playerUI : aiUI, isPlayerMessage ? "PlayerBubble" : "AIBubble", message);
            if (prepend)
                chatBubbles.Insert(0, bubble);
            else
                chatBubbles.Add(bubble);
            bubble.OnResize(MarkLayoutDirty);

            var image = bubble.GetRectTransform().GetComponentInChildren<Image>(true);
            if (image != null)
            {
                image.material = isPlayerMessage ? playerMaterial : aiMaterial;
            }
            var text = bubble.GetRectTransform().GetComponentInChildren<Text>(true);
            if (text != null)
            {
                Material m = isPlayerMessage ? playerTextMaterial : aiTextMaterial;
                if (m != null)
                {
                    text.material = m;
                }
            }

            if (autoScrollOnNewMessage && (!respectUserScroll || IsAtBottom()))
            {
                StartCoroutine(ScrollToBottomNextFrame());
            }

            if (allowTrim)
                TrimHistoryIfNeeded();

            return bubble;
        }

        void TrimHistoryIfNeeded()
        {
            if (maxMessages <= 0) return;
            if (UseCodexBackend) return;

            if (chatBubbles.Count > maxMessages)
            {
                if (!trimOnlyWhenAtBottom || IsAtBottom())
                {
                    int removeCount = chatBubbles.Count - maxMessages;
                    for (int i = 0; i < removeCount; i++)
                    {
                        chatBubbles[i].Destroy();
                    }
                    chatBubbles.RemoveRange(0, removeCount);
                    UpdateBubblePositions();
                }
            }
        }

        bool IsAtBottom(float tolerance = 0.01f)
        {
            if (scrollRect == null) return true;
            return scrollRect.verticalNormalizedPosition <= tolerance;
        }

        void ShowLoadedMessages()
        {
            if (UseCodexBackend)
            {
                // Codex persists sessions server-side; no local chat to replay
                StartCoroutine(ScrollToBottomNextFrame());
                return;
            }

            int start = 1;
            int total = llmCharacter.chat.Count;
            if (maxMessages > 0)
                start = Mathf.Max(1, total - maxMessages);

            for (int i = start; i < total; i++)
            {
                AddBubble(llmCharacter.chat[i].content, i % 2 == 1);
            }
            StartCoroutine(ScrollToBottomNextFrame());
        }

        void OnScrollChanged(Vector2 _)
        {
            if (!UseCodexBackend || !codexHistoryLoaded || codexHistoryLoading) return;
            if (codexHistoryCursor <= 0 || scrollRect == null) return;
            if (scrollRect.verticalNormalizedPosition >= 0.98f)
            {
                codexHistoryLoading = true;
                LoadOlderCodexHistoryBatch();
                StartCoroutine(ResetCodexHistoryLoading());
            }
        }

        void LoadRecentCodexHistory()
        {
            codexHistoryLoaded = true;
            codexHistoryCursor = Mathf.Max(0, codexHistory.Count - CodexHistoryBatchSize);

            // Disable offscreen trim during history load — layout isn't ready yet
            // and containerHeight is near-zero, causing all bubbles to be destroyed.
            bool savedTrim = enableOffscreenTrim;
            enableOffscreenTrim = false;

            int bubblesCreated = 0;
            Debug.Log($"[ChatBot][LoadRecentCodexHistory] codexHistory.Count={codexHistory.Count}, codexHistoryCursor={codexHistoryCursor}, displaying indices [{codexHistoryCursor}..{codexHistory.Count - 1}]");

            for (int i = codexHistoryCursor; i < codexHistory.Count; i++)
            {
                var msg = codexHistory[i];
                AddBubble(msg.text, msg.isUser, allowTrim: false);
                bubblesCreated++;
            }

            Debug.Log($"[ChatBot][LoadRecentCodexHistory] Created {bubblesCreated} bubble(s)");
            UpdateBubblePositions();
            enableOffscreenTrim = savedTrim;
            StartCoroutine(ScrollToBottomNextFrame());
        }

        void LoadOlderCodexHistoryBatch()
        {
            if (codexHistoryCursor <= 0) return;
            int newStart = Mathf.Max(0, codexHistoryCursor - CodexHistoryBatchSize);
            Debug.Log($"[ChatBot][LoadOlderCodexHistoryBatch] Loading batch [{newStart}..{codexHistoryCursor - 1}] (codexHistoryCursor was {codexHistoryCursor})");
            for (int i = codexHistoryCursor - 1; i >= newStart; i--)
            {
                var msg = codexHistory[i];
                AddBubble(msg.text, msg.isUser, allowTrim: false, prepend: true);
            }

            codexHistoryCursor = newStart;
            UpdateBubblePositions();
            Canvas.ForceUpdateCanvases();
            if (scrollRect != null)
                scrollRect.verticalNormalizedPosition = 1f;
        }

        IEnumerator ResetCodexHistoryLoading()
        {
            yield return null;
            codexHistoryLoading = false;
        }

        List<HistoryMessage> LoadCodexHistory(string threadId)
        {
            var messages = new List<HistoryMessage>();
            if (string.IsNullOrEmpty(threadId))
            {
                Debug.Log("[ChatBot][LoadCodexHistory] threadId is null/empty, returning empty list");
                return messages;
            }

            string sessionsDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex", "sessions");

            Debug.Log($"[ChatBot][LoadCodexHistory] sessionsDir={sessionsDir}, exists={Directory.Exists(sessionsDir)}");

            if (!Directory.Exists(sessionsDir))
            {
                Debug.LogWarning("[ChatBot] Codex sessions directory not found: " + sessionsDir);
                return messages;
            }

            var sessionFiles = FindCodexSessionFiles(sessionsDir, threadId);
            Debug.Log($"[ChatBot][LoadCodexHistory] Found {sessionFiles.Count} session file(s) for thread {threadId}");

            if (sessionFiles.Count == 0)
            {
                Debug.LogWarning("[ChatBot] Codex session file not found for thread: " + threadId);
                return messages;
            }

            int totalLines = 0;
            int eventMsgLines = 0;
            int passedFilter = 0;

            foreach (string sessionFile in sessionFiles)
            {
                Debug.Log($"[ChatBot][LoadCodexHistory] Parsing file: {sessionFile}");
                foreach (string line in File.ReadLines(sessionFile))
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    totalLines++;
                    try
                    {
                        var obj = JObject.Parse(line);
                        if (obj["type"]?.Value<string>() != "event_msg") continue;
                        eventMsgLines++;
                        var payload = obj["payload"] as JObject;
                        if (payload == null) continue;

                        string payloadType = payload["type"]?.Value<string>() ?? "";
                        if (payloadType != "user_message" && payloadType != "agent_message") continue;

                        string text = payload["message"]?.Value<string>() ?? "";
                        if (string.IsNullOrWhiteSpace(text)) continue;

                        text = StripAnimDirectives(StripThinkTags(text)).Trim();
                        if (string.IsNullOrWhiteSpace(text)) continue;

                        passedFilter++;
                        messages.Add(new HistoryMessage
                        {
                            isUser = payloadType == "user_message",
                            text = text
                        });
                    }
                    catch
                    {
                        // Ignore malformed lines
                    }
                }
            }

            Debug.Log($"[ChatBot][LoadCodexHistory] totalLines={totalLines}, eventMsgLines={eventMsgLines}, passedFilter={passedFilter}, finalMessageCount={messages.Count}");
            return messages;
        }

        List<string> FindCodexSessionFiles(string sessionsDir, string threadId)
        {
            var files = new List<string>();
            try
            {
                foreach (var file in Directory.EnumerateFiles(sessionsDir, $"*{threadId}.jsonl", SearchOption.AllDirectories))
                {
                    files.Add(file);
                }
            }
            catch
            {
                return new List<string>();
            }

            files.Sort((a, b) => GetCodexSessionSortKey(a).CompareTo(GetCodexSessionSortKey(b)));
            return files;
        }

        DateTime GetCodexSessionSortKey(string path)
        {
            DateTime timestamp = ExtractCodexRolloutTimestamp(path);
            if (timestamp != DateTime.MinValue) return timestamp;
            try
            {
                return new FileInfo(path).LastWriteTimeUtc;
            }
            catch
            {
                return DateTime.MinValue;
            }
        }

        DateTime ExtractCodexRolloutTimestamp(string path)
        {
            if (string.IsNullOrEmpty(path)) return DateTime.MinValue;
            string fileName = Path.GetFileNameWithoutExtension(path);
            var match = CodexRolloutRegex.Match(fileName);
            if (!match.Success) return DateTime.MinValue;
            if (DateTime.TryParseExact(
                    match.Groups[1].Value,
                    "yyyy-MM-dd'T'HH-mm-ss",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                    out DateTime parsed))
            {
                return parsed;
            }
            return DateTime.MinValue;
        }

        string StripThinkTags(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;
            return ThinkRegex.Replace(text, "").TrimStart('\n', '\r');
        }

        string StripAnimDirectives(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;
            return AnimRegex.Replace(text, "").TrimStart('\n', '\r');
        }

        void ClearChatBubbles()
        {
            for (int i = 0; i < chatBubbles.Count; i++)
            {
                chatBubbles[i].Destroy();
            }
            chatBubbles.Clear();
            lastBubbleOutsideFOV = -1;
            codexHistoryCursor = 0;
            codexHistoryLoading = false;
            UpdateBubblePositions();
        }

        void onInputFieldSubmit(string newText)
        {
            inputBubble.ActivateInputField();
            if (blockInput || newText.Trim() == "" || Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift))
            {
                StartCoroutine(BlockInteraction());
                return;
            }
            blockInput = true;

            string message = inputBubble.GetText().Replace("\v", "\n");

            AddBubble(message, true);
            Bubble aiBubble = AddBubble("...", false);

            if (streamAudioSource != null)
                streamAudioSource.Play();
            if (avatarAnimator != null) avatarAnimator.SetBool(isTalkingHash, true);

            Action<string> streamCb = (partial) => { aiBubble.SetText(partial); layoutDirty = true; };
            Action completeCb = () =>
            {
                if (avatarAnimator != null) avatarAnimator.SetBool(isTalkingHash, false);

                aiBubble.SetText(aiBubble.GetText());
                layoutDirty = true;

                if (streamAudioSource != null && streamAudioSource.isPlaying)
                    StartCoroutine(FadeOutStreamAudio());

                AllowInput();
            };

            // Wrap stream callback with animation directive processor if enabled
            if (animDirectiveProcessor != null && UseCodexBackend)
            {
                animDirectiveProcessor.Reset();
                FindAvatarSmart(); // ensure we have current avatar
                var animator = avatarAnimator;
                var blendshapes = animator != null ? animator.GetComponentInChildren<UniversalBlendshapes>() : null;
                animDirectiveProcessor.SetTargets(animator, blendshapes);

                var innerStreamCb = streamCb;
                streamCb = (partial) =>
                {
                    string clean = animDirectiveProcessor.ProcessText(partial, this);
                    innerStreamCb(clean);
                };
            }

            if (UseCodexBackend)
            {
                CodexBridge.Instance.SendMessage(message, streamCb, completeCb);
            }
            else
            {
                Callback<string> llmStreamCb = (partial) => streamCb(partial);
                EmptyCallback llmCompleteCb = () => completeCb();
                Task chatTask = llmCharacter.Chat(message, llmStreamCb, llmCompleteCb);
            }

            inputBubble.SetText("");
        }

        private IEnumerator FadeOutStreamAudio(float duration = 0.5f)
        {
            float startVolume = streamAudioSource.volume;

            while (streamAudioSource.volume > 0f)
            {
                streamAudioSource.volume -= startVolume * Time.deltaTime / duration;
                yield return null;
            }

            streamAudioSource.Stop();
            streamAudioSource.volume = startVolume;
        }

        public void WarmUpCallback()
        {
            warmUpDone = true;
            inputBubble.SetPlaceHolderText(inputPlaceholder);
            AllowInput();
        }

        public void AllowInput()
        {
            blockInput = false;
            inputBubble.ReActivateInputField();
        }

        public void CancelRequests()
        {
            if (UseCodexBackend)
                CodexBridge.Instance.InterruptTurn();
            else
                llmCharacter.CancelRequests();
            AllowInput();
        }

        IEnumerator<string> BlockInteraction()
        {
            inputBubble.setInteractable(false);
            yield return null;
            inputBubble.setInteractable(true);
            inputBubble.MoveTextEnd();
        }

        void onValueChanged(string newText)
        {
            if (Input.GetKey(KeyCode.Return))
            {
                if (inputBubble.GetText().Trim() == "")
                    inputBubble.SetText("");
            }
        }

        public void UpdateBubblePositions()
        {
            float y = bottomPadding;
            float containerHeight = chatContainer.GetComponent<RectTransform>().rect.height;
            Debug.Log($"[ChatBot][UpdateBubblePositions] bubbleCount={chatBubbles.Count}, containerHeight={containerHeight}, enableOffscreenTrim={enableOffscreenTrim}, lastBubbleOutsideFOV={lastBubbleOutsideFOV}");
            for (int i = chatBubbles.Count - 1; i >= 0; i--)
            {
                Bubble bubble = chatBubbles[i];
                RectTransform childRect = bubble.GetRectTransform();
                childRect.anchoredPosition = new Vector2(childRect.anchoredPosition.x, y);

                if (enableOffscreenTrim)
                {
                    if (y > containerHeight && lastBubbleOutsideFOV == -1)
                    {
                        Debug.Log($"[ChatBot][UpdateBubblePositions] OFFSCREEN TRIM: marking bubble {i} as outside FOV (y={y}, containerHeight={containerHeight})");
                        lastBubbleOutsideFOV = i;
                    }
                }

                y += bubble.GetSize().y + bubbleSpacing;
            }
            var contentRect = chatContainer.GetComponent<RectTransform>();
            contentRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, y + bottomPadding);
        }

        void Update()
        {
            RefreshAvatarIfChanged();

            if (!inputBubble.inputFocused() && warmUpDone)
            {
                inputBubble.ActivateInputField();
                StartCoroutine(BlockInteraction());
            }

            if (enableOffscreenTrim && lastBubbleOutsideFOV != -1)
            {
                Debug.Log($"[ChatBot][Update] OFFSCREEN TRIM FIRING: destroying bubbles 0..{lastBubbleOutsideFOV} out of {chatBubbles.Count} total");
                for (int i = 0; i <= lastBubbleOutsideFOV; i++)
                {
                    chatBubbles[i].Destroy();
                }
                chatBubbles.RemoveRange(0, lastBubbleOutsideFOV + 1);
                Debug.Log($"[ChatBot][Update] After trim: {chatBubbles.Count} bubbles remain");
                lastBubbleOutsideFOV = -1;
                UpdateBubblePositions();
            }
        }

        public void ExitGame()
        {
            Debug.Log("Exit button clicked");
            Application.Quit();
        }

        IEnumerator ScrollToBottomNextFrame()
        {
            yield return null;
            Canvas.ForceUpdateCanvases();
            if (scrollRect != null)
                scrollRect.verticalNormalizedPosition = 0f;
        }

        bool onValidateWarning = true;
        void OnValidate()
        {
            if (cornerRadius <= 16) sprite = roundedSprite16;
            else if (cornerRadius <= 32) sprite = roundedSprite32;
            else sprite = roundedSprite64;

            if (!useCodex && onValidateWarning && llmCharacter != null && !llmCharacter.remote && llmCharacter.llm != null && llmCharacter.llm.model == "")
            {
                Debug.LogWarning($"Please select a model in the {llmCharacter.llm.gameObject.name} GameObject!");
                onValidateWarning = false;
            }
        }

        void LateUpdate()
        {
            if (!layoutDirty) return;
            layoutDirty = false;

            UpdateBubblePositions();
            if (autoScrollOnNewMessage && (!respectUserScroll || IsAtBottom()))
            {
                if (scrollRect != null) scrollRect.verticalNormalizedPosition = 0f;
            }
        }

    }
}
