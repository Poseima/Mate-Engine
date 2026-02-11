using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace MateEngine.Codex
{
    public class TaskMonitorUI : MonoBehaviour
    {
        // ── Configuration ─────────────────────────────────────────
        const float PanelWidth = 320f;
        const float MaxPanelHeight = 380f;
        const float PillSize = 36f;
        const float PinnedCornerMargin = 16f;
        const float FadeOutDelay = 10f;
        const float FadeOutDuration = 1.5f;
        const float ExpandSpeed = 8f;
        const int MaxDisplayedActivityEntries = 5;
        const int MaxEntryLabelChars = 256;

        [Header("Follow Avatar")]
        [SerializeField] bool followAvatar = true;
        [SerializeField] bool followWhenExpanded = true;
        [SerializeField] HumanBodyBones followAnchorBone = HumanBodyBones.Chest;
        // Offset from the anchor bone to the monitor root (local canvas units).
        // Note: panel now sits at y=0 (no longer stacked above the pill), so this is slightly higher than before.
        [SerializeField] Vector2 followOffset = new(220f, -132f);
        [SerializeField, Min(0.01f)] float followSmoothTime = 0.08f;
        [SerializeField] Vector2 clampMargin = new(PinnedCornerMargin, PinnedCornerMargin);

        [Header("Avoid Covering Avatar")]
        [SerializeField] bool avoidAvatarCover = true;
        [SerializeField] bool preferRightSide = true;
        [SerializeField] bool allowSideSwitch = true;
        [SerializeField] float avoidGap = 24f;
        [SerializeField] float avatarBoundsRescanInterval = 0.25f;

        // ── Colors ────────────────────────────────────────────────
        static readonly Color32 PanelBg = new(20, 20, 50, 220);
        static readonly Color32 CardBg = new(30, 30, 65, 200);
        static readonly Color32 WhatsAppGreen = new(37, 211, 102, 255);
        static readonly Color32 ChatGreen = new(81, 164, 81, 255);
        static readonly Color32 AccentMagenta = new(224, 64, 251, 255);
        static readonly Color32 TextWhite = new(255, 255, 255, 255);
        static readonly Color32 TextDim = new(180, 180, 200, 255);
        static readonly Color32 PulsingDot = new(81, 164, 81, 255);
        static readonly Color32 CancelledOrange = new(255, 165, 0, 255);

        Color PanelTintColor => theme != null ? theme.panelTint : (Color)PanelBg;
        Color CardTintColor => theme != null ? theme.cardTint : (Color)CardBg;
        Color PillTintColor => theme != null ? theme.pillTint : (Color)PanelBg;
        Color ShadowTintColor => theme != null ? theme.shadowTint : new Color(0f, 0f, 0f, 0.45f);
        Color GlowTintColor => theme != null ? theme.glowTint : new Color(1f, 1f, 1f, 0.18f);
        Color TextColor => theme != null ? theme.text : (Color)TextWhite;
        Color DimTextColor => theme != null ? theme.textDim : (Color)TextDim;
        Color ToolAccentColor => theme != null ? theme.toolAccent : (Color)AccentMagenta;
        Color WorkingAccentColor => theme != null && theme.workingAccent.a > 0.001f ? theme.workingAccent : ToolAccentColor;
        Color ThinkingAccentColor => theme != null && theme.thinkingAccent.a > 0.001f ? theme.thinkingAccent : ToolAccentColor;
        Color ToolCallAccentColor => theme != null && theme.toolCallAccent.a > 0.001f ? theme.toolCallAccent : ToolAccentColor;
        Color CancelAccentColor => theme != null && theme.cancelAccent.a > 0.001f ? theme.cancelAccent : (Color)CancelledOrange;

        // ── State ─────────────────────────────────────────────────
        bool expanded;
        float expandLerp;
        Canvas canvas;
        RectTransform canvasRT;
        CanvasGroup panelCanvasGroup;
        RectTransform monitorRootRT;
        GameObject pillObject;
        RectTransform pillRT;
        Image pillBgImage;
        Image pillDotImage;
        GameObject panelObject;
        RectTransform panelRT;
        Image panelBgImage;
        GameObject panelShadowObject;
        RectTransform panelShadowRT;
        Image panelShadowImage;
        GameObject panelGlowObject;
        RectTransform panelGlowRT;
        Image panelGlowImage;
        TextMeshProUGUI badgeText;
        RectTransform contentParent;

        Vector2 followVelocity;
        bool hasLastValidFollowPos;
        Vector2 lastValidFollowPos;

        TaskMonitorTheme theme;

        Transform modelRoot;
        GameObject currentModel;
        AvatarAnimatorReceiver currentReceiver;

        Renderer[] avatarRenderers;
        GameObject cachedAvatarRoot;
        float nextBoundsScanTime;
        bool lastPlacedRight = true;
        static readonly Vector3[] boundsCorners = new Vector3[8];

        // Group card merging: maps turnId → card key
        readonly Dictionary<string, string> turnToCardKey = new();
        readonly Dictionary<string, TaskCardUI> cards = new();
        readonly List<string> cardOrder = new();
        readonly List<string> pendingRemoval = new();

        // ── Lifecycle ─────────────────────────────────────────────

        void Awake()
        {
            // Optional fallback path if AvatarBubbleHandler is not present.
            modelRoot = GameObject.Find("Model")?.transform;
        }

        void OnEnable()
        {
            if (canvas == null)
                BuildUI();

            var tracker = CodexBridge.Instance?.TaskTracker;
            if (tracker != null)
            {
                tracker.OnTaskCreated += HandleTaskCreated;
                tracker.OnTaskUpdated += HandleTaskUpdated;
                tracker.OnTaskCompleted += HandleTaskCompleted;
                tracker.OnActivityUpdated += HandleActivityUpdated;
            }
        }

        void OnDisable()
        {
            var tracker = CodexBridge.Instance?.TaskTracker;
            if (tracker != null)
            {
                tracker.OnTaskCreated -= HandleTaskCreated;
                tracker.OnTaskUpdated -= HandleTaskUpdated;
                tracker.OnTaskCompleted -= HandleTaskCompleted;
                tracker.OnActivityUpdated -= HandleActivityUpdated;
            }
        }

        void OnDestroy()
        {
            if (canvas != null)
                Destroy(canvas.gameObject);
        }

        void Update()
        {
            if (canvas == null) return;

            // Hide during BigScreen mode
            Animator bigScreenAnimator = GetActiveAvatarAnimator();
            if (bigScreenAnimator != null && bigScreenAnimator.GetBool("isBigScreen"))
            {
                canvas.enabled = false;
                return;
            }
            canvas.enabled = true;

            // Expand/collapse animation
            float target = expanded ? 1f : 0f;
            expandLerp = Mathf.MoveTowards(expandLerp, target, Time.unscaledDeltaTime * ExpandSpeed);
            bool showPanel = expandLerp > 0.01f;
            panelObject.transform.localScale = new Vector3(1f, expandLerp, 1f);
            panelObject.SetActive(showPanel);
            if (panelShadowObject != null)
            {
                panelShadowObject.transform.localScale = panelObject.transform.localScale;
                panelShadowObject.SetActive(showPanel);
            }
            if (panelGlowObject != null)
            {
                panelGlowObject.transform.localScale = panelObject.transform.localScale;
                panelGlowObject.SetActive(showPanel);
            }

            if (panelCanvasGroup != null)
            {
                float eased = Mathf.SmoothStep(0f, 1f, expandLerp);
                panelCanvasGroup.alpha = eased;
                panelCanvasGroup.blocksRaycasts = showPanel;
                panelCanvasGroup.interactable = showPanel;
            }

            // Update active task count badge
            int activeCount = 0;
            foreach (var kv in cards)
                if (!kv.Value.IsCompleted) activeCount++;
            badgeText.text = activeCount > 0 ? activeCount.ToString() : "";
            // Hide the pill while the expanded panel is visible to keep the UI "bubble-like".
            pillObject.SetActive(activeCount > 0 && !showPanel);

            // Pulsing dot on pill
            if (activeCount > 0)
            {
                float pulse = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 4f);
                if (pillDotImage != null)
                {
                    var c = (Color)PulsingDot;
                    c.a = pulse;
                    pillDotImage.color = c;
                }
            }

            if (panelGlowImage != null)
            {
                if (activeCount > 0 && showPanel)
                {
                    float speed = theme != null ? theme.panelGlowPulseSpeed : 2.5f;
                    float amp = theme != null ? theme.panelGlowPulseAmplitude : 0.35f;
                    float t = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * speed);
                    var c = GlowTintColor;
                    c.a = GlowTintColor.a * (1f + amp * t);
                    panelGlowImage.color = c;
                    panelGlowImage.enabled = true;
                }
                else
                {
                    panelGlowImage.enabled = false;
                }
            }

            // Update cards (elapsed time, fade-outs)
            pendingRemoval.Clear();
            foreach (var kv in cards)
            {
                var card = kv.Value;
                card.Tick(Time.unscaledTime);
                if (card.IsCompleted)
                {
                    float elapsed = Time.unscaledTime - card.CompletedAt;
                    if (elapsed > FadeOutDelay + FadeOutDuration)
                    {
                        pendingRemoval.Add(kv.Key);
                        continue;
                    }
                    if (elapsed > FadeOutDelay)
                    {
                        float alpha = 1f - (elapsed - FadeOutDelay) / FadeOutDuration;
                        card.SetAlpha(alpha);
                    }
                }
                else
                {
                    card.UpdateElapsed(Time.unscaledTime);
                }
            }

            foreach (var key in pendingRemoval)
            {
                if (cards.TryGetValue(key, out var card))
                {
                    // Remove turnId mappings for this card
                    var turnsToRemove = new List<string>();
                    foreach (var kv in turnToCardKey)
                        if (kv.Value == key) turnsToRemove.Add(kv.Key);
                    foreach (var tid in turnsToRemove)
                        turnToCardKey.Remove(tid);

                    card.Destroy();
                    cards.Remove(key);
                    cardOrder.Remove(key);
                }
            }

            // Auto-collapse when no cards remain
            if (cards.Count == 0 && expanded)
                expanded = false;
        }

        void LateUpdate()
        {
            if (canvas == null || canvasRT == null || monitorRootRT == null) return;
            if (!canvas.enabled) return;
            if (!followAvatar) return;
            if (expanded && !followWhenExpanded) return;

            var cam = Camera.main;
            Vector2 target = GetPinnedCornerLocal();

            if (cam != null)
            {
                var anim = GetActiveAvatarAnimator();
                var anchor = GetAnchorTransform(anim);
                if (anchor != null)
                {
                    Vector3 screen = cam.WorldToScreenPoint(anchor.position);
                    if (screen.z > 0f)
                    {
                        if (RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRT, screen, null, out var local))
                        {
                            target = ClampToCanvas(local + followOffset);
                            lastValidFollowPos = target;
                            hasLastValidFollowPos = true;
                        }
                        else
                        {
                            target = ClampToCanvas(hasLastValidFollowPos ? lastValidFollowPos : target);
                        }
                    }
                    else
                    {
                        // Behind camera: keep last valid clamped position if we have one.
                        target = ClampToCanvas(hasLastValidFollowPos ? lastValidFollowPos : target);
                    }
                }
                else
                {
                    target = ClampToCanvas(hasLastValidFollowPos ? lastValidFollowPos : target);
                }

                if (avoidAvatarCover && TryGetAvatarRectLocal(cam, out var avatarRectLocal))
                    target = ResolveAvatarOverlap(target, avatarRectLocal);
            }
            else
            {
                target = ClampToCanvas(target);
            }

            target = ClampToCanvas(target);

            float smoothTime = Mathf.Max(0.01f, followSmoothTime);
            monitorRootRT.anchoredPosition = Vector2.SmoothDamp(
                monitorRootRT.anchoredPosition,
                target,
                ref followVelocity,
                smoothTime,
                Mathf.Infinity,
                Time.unscaledDeltaTime);
        }

        // ── Card Key Logic ───────────────────────────────────────

        string GetCardKey(TaskInfo task)
        {
            if (task.Source != null && task.Source.SourceType == "whatsapp" && !string.IsNullOrEmpty(task.Source.SourceGroup))
                return "wa:" + task.Source.SourceGroup;
            return task.TurnId;
        }

        // ── Event Handlers ────────────────────────────────────────

        void HandleTaskCreated(TaskInfo task)
        {
            if (string.IsNullOrEmpty(task.TurnId)) return;

            string cardKey = GetCardKey(task);
            turnToCardKey[task.TurnId] = cardKey;

            if (cards.TryGetValue(cardKey, out var existingCard))
            {
                // Merge into existing group card
                existingCard.AddTurn(task);
            }
            else
            {
                var card = new TaskCardUI(contentParent, task, theme);
                cards[cardKey] = card;
                cardOrder.Add(cardKey);
            }

            if (!expanded) expanded = true;
        }

        void HandleTaskUpdated(TaskInfo task)
        {
            if (turnToCardKey.TryGetValue(task.TurnId, out var key) && cards.TryGetValue(key, out var card))
                card.RefreshActivity(task);
        }

        void HandleTaskCompleted(TaskInfo task)
        {
            if (!turnToCardKey.TryGetValue(task.TurnId, out var key)) return;
            if (!cards.TryGetValue(key, out var card)) return;

            card.HandleTurnCompleted(task);

            if (card.AllTurnsCompleted())
            {
                card.MarkCompleted(task);
                card.CompletedAt = Time.unscaledTime;
            }
        }

        void HandleActivityUpdated(TaskInfo task)
        {
            if (turnToCardKey.TryGetValue(task.TurnId, out var key) && cards.TryGetValue(key, out var card))
                card.RefreshActivity(task);
        }

        // ── UI Construction ───────────────────────────────────────

        void BuildUI()
        {
            var canvasGo = new GameObject("TaskMonitorCanvas", typeof(RectTransform));
            canvasGo.transform.SetParent(transform, false);

            canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 100;
            canvasRT = canvasGo.GetComponent<RectTransform>();

            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;

            canvasGo.AddComponent<GraphicRaycaster>();

            theme = CreateRuntimeTheme(Resources.Load<TaskMonitorTheme>("TaskMonitorTheme"));
            if (theme == null)
            {
                Debug.LogWarning("[TaskMonitorUI] TaskMonitorTheme not found at Resources/TaskMonitorTheme. Using fallback colors and TMP defaults.");
            }
            else if (theme.font == null)
            {
                Debug.LogWarning("[TaskMonitorUI] TaskMonitorTheme.font is not set. CJK text may render as squares if TMP defaults lack glyphs.");
            }
            else
            {
                Debug.Log($"[TaskMonitorUI] Using TMP font asset '{theme.font.name}' (atlasPopulationMode={theme.font.atlasPopulationMode}, multiAtlas={theme.font.isMultiAtlasTexturesEnabled}).");
            }

            // Root that we reposition to make the monitor "float" near the avatar.
            var monitorRootGo = CreateRect("TaskMonitorRoot", canvasGo.transform);
            monitorRootRT = monitorRootGo.GetComponent<RectTransform>();
            monitorRootRT.anchorMin = new Vector2(0.5f, 0.5f);
            monitorRootRT.anchorMax = new Vector2(0.5f, 0.5f);
            monitorRootRT.pivot = new Vector2(1f, 0f); // bottom-right pivot
            monitorRootRT.anchoredPosition = GetPinnedCornerLocal();

            // ── Pill (collapsed indicator) ────────────────────────
            pillObject = CreateRect("Pill", monitorRootGo.transform);
            pillRT = pillObject.GetComponent<RectTransform>();
            pillRT.anchorMin = new Vector2(1, 0);
            pillRT.anchorMax = new Vector2(1, 0);
            pillRT.pivot = new Vector2(1, 0);
            pillRT.anchoredPosition = Vector2.zero;
            pillRT.sizeDelta = new Vector2(PillSize, PillSize);

            pillBgImage = pillObject.AddComponent<Image>();
            if (theme != null && theme.pillBackground != null)
            {
                pillBgImage.sprite = theme.pillBackground;
                pillBgImage.type = Image.Type.Sliced;
                pillBgImage.color = PillTintColor;
            }
            else
            {
                pillBgImage.color = PillTintColor;
            }

            var pillBtn = pillObject.AddComponent<Button>();
            pillBtn.targetGraphic = pillBgImage;
            pillBtn.onClick.AddListener(() => expanded = !expanded);

            // Pulsing dot
            var dotGo = CreateRect("Dot", pillObject.transform);
            var dotRT = dotGo.GetComponent<RectTransform>();
            dotRT.anchorMin = new Vector2(0.5f, 0.5f);
            dotRT.anchorMax = new Vector2(0.5f, 0.5f);
            dotRT.sizeDelta = new Vector2(10, 10);
            dotRT.anchoredPosition = new Vector2(-4, 0);
            pillDotImage = dotGo.AddComponent<Image>();
            pillDotImage.color = PulsingDot;
            pillDotImage.raycastTarget = false;

            // Badge text
            var badgeGo = CreateRect("Badge", pillObject.transform);
            var badgeRT = badgeGo.GetComponent<RectTransform>();
            badgeRT.anchorMin = Vector2.zero;
            badgeRT.anchorMax = Vector2.one;
            badgeRT.offsetMin = Vector2.zero;
            badgeRT.offsetMax = Vector2.zero;
            badgeText = badgeGo.AddComponent<TextMeshProUGUI>();
            badgeText.text = "";
            badgeText.fontSize = 13;
            if (theme != null && theme.font != null) badgeText.font = theme.font;
            badgeText.color = TextColor;
            badgeText.alignment = TextAlignmentOptions.Center;
            badgeText.raycastTarget = false;

            pillObject.SetActive(false);

            // ── Expanded Panel ────────────────────────────────────
            panelObject = CreateRect("Panel", monitorRootGo.transform);
            panelRT = panelObject.GetComponent<RectTransform>();
            panelRT.anchorMin = new Vector2(1, 0);
            panelRT.anchorMax = new Vector2(1, 0);
            panelRT.pivot = new Vector2(1, 0);
            panelRT.anchoredPosition = new Vector2(0, 0);
            panelRT.sizeDelta = new Vector2(PanelWidth, MaxPanelHeight);

            panelBgImage = panelObject.AddComponent<Image>();
            if (theme != null && theme.panelBackground != null)
            {
                panelBgImage.sprite = theme.panelBackground;
                panelBgImage.type = Image.Type.Sliced;
                panelBgImage.color = PanelTintColor;
            }
            else
            {
                panelBgImage.color = PanelTintColor;
            }

            panelCanvasGroup = panelObject.AddComponent<CanvasGroup>();

            // Decorative shadow + glow (siblings behind the panel).
            if (theme != null && theme.panelShadow != null)
            {
                panelShadowObject = CreateRect("PanelShadow", monitorRootGo.transform);
                panelShadowRT = panelShadowObject.GetComponent<RectTransform>();
                panelShadowRT.anchorMin = panelRT.anchorMin;
                panelShadowRT.anchorMax = panelRT.anchorMax;
                panelShadowRT.pivot = panelRT.pivot;
                panelShadowRT.anchoredPosition = panelRT.anchoredPosition + (theme != null ? theme.panelShadowOffset : new Vector2(10f, -10f));
                panelShadowRT.sizeDelta = panelRT.sizeDelta + (theme != null ? theme.panelShadowPadding : new Vector2(36f, 36f));
                panelShadowImage = panelShadowObject.AddComponent<Image>();
                panelShadowImage.raycastTarget = false;
                panelShadowImage.sprite = theme.panelShadow;
                panelShadowImage.type = theme.panelShadow.border != Vector4.zero ? Image.Type.Sliced : Image.Type.Simple;
                panelShadowImage.color = ShadowTintColor;
            }

            if (theme != null && theme.panelGlowFrame != null)
            {
                panelGlowObject = CreateRect("PanelGlow", monitorRootGo.transform);
                panelGlowRT = panelGlowObject.GetComponent<RectTransform>();
                panelGlowRT.anchorMin = panelRT.anchorMin;
                panelGlowRT.anchorMax = panelRT.anchorMax;
                panelGlowRT.pivot = panelRT.pivot;
                panelGlowRT.anchoredPosition = panelRT.anchoredPosition;
                panelGlowRT.sizeDelta = panelRT.sizeDelta + (theme != null ? theme.panelGlowPadding : new Vector2(32f, 32f));
                panelGlowImage = panelGlowObject.AddComponent<Image>();
                panelGlowImage.raycastTarget = false;
                panelGlowImage.sprite = theme.panelGlowFrame;
                panelGlowImage.type = theme.panelGlowFrame.border != Vector4.zero ? Image.Type.Sliced : Image.Type.Simple;
                panelGlowImage.color = GlowTintColor;
            }

            // Order: shadow, glow, panel, pill
            int panelIndex = panelObject.transform.GetSiblingIndex();
            if (panelShadowObject != null) panelShadowObject.transform.SetSiblingIndex(panelIndex);
            if (panelGlowObject != null) panelGlowObject.transform.SetSiblingIndex(panelIndex + 1);
            pillObject.transform.SetAsLastSibling();

            // Panel layout
            var panelLayout = panelObject.AddComponent<VerticalLayoutGroup>();
            panelLayout.padding = new RectOffset(10, 10, 10, 10);
            panelLayout.spacing = 6;
            panelLayout.childControlWidth = true;
            panelLayout.childControlHeight = true;
            panelLayout.childForceExpandWidth = true;
            panelLayout.childForceExpandHeight = false;

            // Scroll area
            var scrollGo = CreateRect("Scroll", panelObject.transform);
            var scrollLE = scrollGo.AddComponent<LayoutElement>();
            scrollLE.flexibleHeight = 1;
            var scrollRect = scrollGo.AddComponent<ScrollRect>();
            scrollRect.horizontal = false;
            scrollRect.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;
            scrollRect.movementType = ScrollRect.MovementType.Clamped;

            // Viewport
            var viewportGo = CreateRect("Viewport", scrollGo.transform);
            var viewportRT = viewportGo.GetComponent<RectTransform>();
            viewportRT.anchorMin = Vector2.zero;
            viewportRT.anchorMax = Vector2.one;
            viewportRT.offsetMin = Vector2.zero;
            viewportRT.offsetMax = Vector2.zero;
            viewportGo.AddComponent<RectMask2D>();

            // Content
            var contentGo = CreateRect("Content", viewportGo.transform);
            contentParent = contentGo.GetComponent<RectTransform>();
            contentParent.anchorMin = new Vector2(0, 1);
            contentParent.anchorMax = new Vector2(1, 1);
            contentParent.pivot = new Vector2(0.5f, 1);
            contentParent.offsetMin = new Vector2(0, 0);
            contentParent.offsetMax = new Vector2(0, 0);

            var contentLayout = contentGo.AddComponent<VerticalLayoutGroup>();
            contentLayout.padding = new RectOffset(0, 0, 0, 0);
            contentLayout.spacing = 4;
            contentLayout.childControlWidth = true;
            contentLayout.childControlHeight = true;
            contentLayout.childForceExpandWidth = true;
            contentLayout.childForceExpandHeight = false;

            var contentCSF = contentGo.AddComponent<ContentSizeFitter>();
            contentCSF.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            scrollRect.content = contentParent;
            scrollRect.viewport = viewportRT;

            scrollGo.AddComponent<ScrollHelper>();

            // Collapse button (no header; floats slightly above the bubble).
            var collapseBtnGo = CreateRect("Collapse", panelObject.transform);
            var collapseLE = collapseBtnGo.AddComponent<LayoutElement>();
            collapseLE.ignoreLayout = true;
            var collapseRT = collapseBtnGo.GetComponent<RectTransform>();
            collapseRT.anchorMin = new Vector2(1f, 1f);
            collapseRT.anchorMax = new Vector2(1f, 1f);
            collapseRT.pivot = new Vector2(1f, 1f);
            float collapseSize = 26f;
            collapseRT.sizeDelta = new Vector2(collapseSize, collapseSize);
            collapseRT.anchoredPosition = new Vector2(-4f, 10f);

            var collapseBg = collapseBtnGo.AddComponent<Image>();
            if (theme != null && theme.pillBackground != null)
            {
                collapseBg.sprite = theme.pillBackground;
                collapseBg.type = Image.Type.Sliced;
                collapseBg.color = new Color(0f, 0f, 0f, 0.22f);
            }
            else
            {
                collapseBg.color = new Color(0f, 0f, 0f, 0.22f);
            }

            var collapseIconGo = CreateRect("Icon", collapseBtnGo.transform);
            var collapseIconRT = collapseIconGo.GetComponent<RectTransform>();
            collapseIconRT.anchorMin = Vector2.zero;
            collapseIconRT.anchorMax = Vector2.one;
            collapseIconRT.offsetMin = new Vector2(5, 5);
            collapseIconRT.offsetMax = new Vector2(-5, -5);
            var collapseIcon = collapseIconGo.AddComponent<Image>();
            collapseIcon.sprite = theme != null ? theme.iconCollapse : null;
            collapseIcon.preserveAspect = true;
            collapseIcon.color = theme != null ? theme.iconNormal : TextColor;
            collapseIcon.raycastTarget = false;

            var collapseBtn = collapseBtnGo.AddComponent<Button>();
            collapseBtn.targetGraphic = collapseBg;
            collapseBtn.transition = Selectable.Transition.ColorTint;
            var iconColors = collapseBtn.colors;
            iconColors.normalColor = collapseBg.color;
            iconColors.highlightedColor = new Color(collapseBg.color.r, collapseBg.color.g, collapseBg.color.b, Mathf.Min(0.34f, collapseBg.color.a + 0.12f));
            iconColors.pressedColor = new Color(collapseBg.color.r, collapseBg.color.g, collapseBg.color.b, Mathf.Min(0.44f, collapseBg.color.a + 0.22f));
            iconColors.selectedColor = iconColors.highlightedColor;
            iconColors.disabledColor = new Color(collapseBg.color.r, collapseBg.color.g, collapseBg.color.b, 0f);
            collapseBtn.colors = iconColors;
            collapseBtn.onClick.AddListener(() => expanded = false);

            panelObject.SetActive(false);
            if (panelShadowObject != null) panelShadowObject.SetActive(false);
            if (panelGlowObject != null) panelGlowObject.SetActive(false);
            expandLerp = 0f;
        }

        Animator GetActiveAvatarAnimator()
        {
            if (AvatarBubbleHandler.ActiveHandlers.Count > 0)
            {
                var a = AvatarBubbleHandler.ActiveHandlers[0]?.avatarAnimator;
                if (a != null) return a;
            }

            UpdateCurrentAvatarReceiver();
            return currentReceiver != null ? currentReceiver.avatarAnimator : null;
        }

        GameObject GetActiveAvatarRoot()
        {
            UpdateCurrentAvatarReceiver();
            if (currentModel != null) return currentModel;

            if (AvatarBubbleHandler.ActiveHandlers.Count > 0)
            {
                var a = AvatarBubbleHandler.ActiveHandlers[0]?.avatarAnimator;
                if (a != null) return a.gameObject;
            }

            return null;
        }

        void UpdateCurrentAvatarReceiver()
        {
            if (modelRoot == null)
                modelRoot = GameObject.Find("Model")?.transform;
            if (modelRoot == null) return;

            for (int i = 0; i < modelRoot.childCount; i++)
            {
                var child = modelRoot.GetChild(i);
                if (!child.gameObject.activeInHierarchy) continue;

                if (currentModel != child.gameObject)
                {
                    currentModel = child.gameObject;
                    currentReceiver = currentModel.GetComponent<AvatarAnimatorReceiver>();
                }
                return;
            }
        }

        Transform GetAnchorTransform(Animator anim)
        {
            if (anim == null) return null;
            var bone = anim.GetBoneTransform(followAnchorBone);
            return bone != null ? bone : anim.transform;
        }

        Vector2 GetPinnedCornerLocal()
        {
            if (canvasRT == null)
                return new Vector2(0f, 0f);

            float halfW = canvasRT.rect.width * 0.5f;
            float halfH = canvasRT.rect.height * 0.5f;
            return new Vector2(halfW - PinnedCornerMargin, -halfH + PinnedCornerMargin);
        }

        void GetMonitorLocalExtents(out float minX, out float maxX, out float minY, out float maxY)
        {
            // NOTE: We can't reference `out` parameters from a local function in older Unity/C# compilers,
            // so we accumulate into locals and assign to outs at the end.
            bool hasAny = false;
            float minXAcc = 0f, maxXAcc = 0f, minYAcc = 0f, maxYAcc = 0f;

            void AddRect(RectTransform rt, float fallbackW, float fallbackH)
            {
                if (rt == null) return;
                float w = rt.rect.width > 0f ? rt.rect.width : fallbackW;
                float h = rt.rect.height > 0f ? rt.rect.height : fallbackH;
                Vector2 p = rt.anchoredPosition;
                float rMinX = p.x - w;
                float rMaxX = p.x;
                float rMinY = p.y;
                float rMaxY = p.y + h;

                if (!hasAny)
                {
                    minXAcc = rMinX; maxXAcc = rMaxX; minYAcc = rMinY; maxYAcc = rMaxY;
                    hasAny = true;
                }
                else
                {
                    minXAcc = Mathf.Min(minXAcc, rMinX);
                    maxXAcc = Mathf.Max(maxXAcc, rMaxX);
                    minYAcc = Mathf.Min(minYAcc, rMinY);
                    maxYAcc = Mathf.Max(maxYAcc, rMaxY);
                }
            }

            bool includePanel = expanded || expandLerp > 0.01f;
            if (includePanel) AddRect(panelRT, PanelWidth, MaxPanelHeight);

            // During transitions we may briefly have both visible; include the pill bounds if it's active.
            if (pillObject != null && pillObject.activeSelf) AddRect(pillRT, PillSize, PillSize);

            if (!hasAny)
            {
                minXAcc = -PillSize;
                maxXAcc = 0f;
                minYAcc = 0f;
                maxYAcc = PillSize;
            }

            minX = minXAcc;
            maxX = maxXAcc;
            minY = minYAcc;
            maxY = maxYAcc;
        }

        Vector2 ClampToCanvas(Vector2 target)
        {
            if (canvasRT == null) return target;

            float halfW = canvasRT.rect.width * 0.5f;
            float halfH = canvasRT.rect.height * 0.5f;

            GetMonitorLocalExtents(out float minX, out float maxX, out float minY, out float maxY);

            float xMinAllowed = (-halfW + clampMargin.x) - minX;
            float xMaxAllowed = (halfW - clampMargin.x) - maxX;
            float yMinAllowed = (-halfH + clampMargin.y) - minY;
            float yMaxAllowed = (halfH - clampMargin.y) - maxY;

            target.x = Mathf.Clamp(target.x, xMinAllowed, xMaxAllowed);
            target.y = Mathf.Clamp(target.y, yMinAllowed, yMaxAllowed);
            return target;
        }

        bool TryGetAvatarRectLocal(Camera cam, out Rect avatarRectLocal)
        {
            avatarRectLocal = default;
            if (cam == null || canvasRT == null) return false;

            float interval = Mathf.Max(0.05f, avatarBoundsRescanInterval);
            float now = Time.unscaledTime;
            var root = GetActiveAvatarRoot();
            if (root == null)
            {
                avatarRenderers = null;
                cachedAvatarRoot = null;
                return false;
            }

            if (root != cachedAvatarRoot || avatarRenderers == null || now >= nextBoundsScanTime)
            {
                cachedAvatarRoot = root;
                avatarRenderers = root.GetComponentsInChildren<Renderer>(true);
                nextBoundsScanTime = now + interval;
            }

            if (avatarRenderers == null || avatarRenderers.Length == 0)
                return false;

            bool any = false;
            Bounds union = default;

            for (int i = 0; i < avatarRenderers.Length; i++)
            {
                var rr = avatarRenderers[i];
                if (rr == null || !rr.enabled) continue;

                if (!any)
                {
                    union = rr.bounds;
                    any = true;
                }
                else
                {
                    union.Encapsulate(rr.bounds);
                }
            }

            if (!any) return false;

            Vector3 c = union.center;
            Vector3 e = union.extents;
            boundsCorners[0] = new Vector3(c.x - e.x, c.y - e.y, c.z - e.z);
            boundsCorners[1] = new Vector3(c.x - e.x, c.y - e.y, c.z + e.z);
            boundsCorners[2] = new Vector3(c.x - e.x, c.y + e.y, c.z - e.z);
            boundsCorners[3] = new Vector3(c.x - e.x, c.y + e.y, c.z + e.z);
            boundsCorners[4] = new Vector3(c.x + e.x, c.y - e.y, c.z - e.z);
            boundsCorners[5] = new Vector3(c.x + e.x, c.y - e.y, c.z + e.z);
            boundsCorners[6] = new Vector3(c.x + e.x, c.y + e.y, c.z - e.z);
            boundsCorners[7] = new Vector3(c.x + e.x, c.y + e.y, c.z + e.z);

            float minX = float.PositiveInfinity;
            float maxX = float.NegativeInfinity;
            float minY = float.PositiveInfinity;
            float maxY = float.NegativeInfinity;

            bool anyCorner = false;
            for (int i = 0; i < 8; i++)
            {
                Vector3 sp = cam.WorldToScreenPoint(boundsCorners[i]);
                if (sp.z <= 0.0001f) continue;

                if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRT, sp, null, out var lp))
                    continue;

                anyCorner = true;
                if (lp.x < minX) minX = lp.x;
                if (lp.x > maxX) maxX = lp.x;
                if (lp.y < minY) minY = lp.y;
                if (lp.y > maxY) maxY = lp.y;
            }

            if (!anyCorner) return false;

            // Clamp to canvas rect so we don't return insane values when avatar is far offscreen.
            float halfW = canvasRT.rect.width * 0.5f;
            float halfH = canvasRT.rect.height * 0.5f;
            minX = Mathf.Clamp(minX, -halfW, halfW);
            maxX = Mathf.Clamp(maxX, -halfW, halfW);
            minY = Mathf.Clamp(minY, -halfH, halfH);
            maxY = Mathf.Clamp(maxY, -halfH, halfH);

            if (maxX < minX)
            {
                float mid = (minX + maxX) * 0.5f;
                minX = mid; maxX = mid;
            }
            if (maxY < minY)
            {
                float mid = (minY + maxY) * 0.5f;
                minY = mid; maxY = mid;
            }

            avatarRectLocal = Rect.MinMaxRect(minX, minY, maxX, maxY);
            return true;
        }

        Vector2 ResolveAvatarOverlap(Vector2 baseTarget, Rect avatarRectLocal)
        {
            float gap = Mathf.Max(0f, avoidGap);

            GetMonitorLocalExtents(out float minX, out float maxX, out float minY, out float maxY);
            Rect baseRect = Rect.MinMaxRect(
                baseTarget.x + minX,
                baseTarget.y + minY,
                baseTarget.x + maxX,
                baseTarget.y + maxY);

            float baseArea = OverlapArea(baseRect, avatarRectLocal);
            if (baseArea <= 0f)
                return baseTarget;

            Vector2 rightTarget = baseTarget;
            rightTarget.x = (avatarRectLocal.xMax + gap) - minX;
            rightTarget = ClampToCanvas(rightTarget);
            Rect rightRect = Rect.MinMaxRect(
                rightTarget.x + minX,
                rightTarget.y + minY,
                rightTarget.x + maxX,
                rightTarget.y + maxY);
            float rightArea = OverlapArea(rightRect, avatarRectLocal);

            Vector2 leftTarget = baseTarget;
            leftTarget.x = (avatarRectLocal.xMin - gap) - maxX;
            leftTarget = ClampToCanvas(leftTarget);
            Rect leftRect = Rect.MinMaxRect(
                leftTarget.x + minX,
                leftTarget.y + minY,
                leftTarget.x + maxX,
                leftTarget.y + maxY);
            float leftArea = OverlapArea(leftRect, avatarRectLocal);

            bool rightClear = rightArea <= 0f;
            bool leftClear = leftArea <= 0f;

            Vector2 chosen = baseTarget;
            bool chosenRight = lastPlacedRight;

            if (preferRightSide)
            {
                if (rightClear)
                {
                    chosen = rightTarget;
                    chosenRight = true;
                }
                else if (allowSideSwitch && leftClear)
                {
                    chosen = leftTarget;
                    chosenRight = false;
                }
                else if (allowSideSwitch)
                {
                    ChooseByAreaWithHysteresis(
                        baseTarget,
                        rightTarget, rightArea,
                        leftTarget, leftArea,
                        ref chosen,
                        ref chosenRight);
                }
                else
                {
                    chosen = rightTarget;
                    chosenRight = true;
                }
            }
            else
            {
                if (leftClear)
                {
                    chosen = leftTarget;
                    chosenRight = false;
                }
                else if (allowSideSwitch && rightClear)
                {
                    chosen = rightTarget;
                    chosenRight = true;
                }
                else if (allowSideSwitch)
                {
                    ChooseByAreaWithHysteresis(
                        baseTarget,
                        leftTarget, leftArea,
                        rightTarget, rightArea,
                        ref chosen,
                        ref chosenRight);
                }
                else
                {
                    chosen = leftTarget;
                    chosenRight = false;
                }
            }

            lastPlacedRight = chosenRight;
            return chosen;
        }

        void ChooseByAreaWithHysteresis(
            Vector2 baseTarget,
            Vector2 primaryTarget,
            float primaryArea,
            Vector2 secondaryTarget,
            float secondaryArea,
            ref Vector2 chosen,
            ref bool chosenRight)
        {
            // If one side is clearly better, pick it.
            if (primaryArea < secondaryArea * 0.8f)
            {
                chosen = primaryTarget;
                chosenRight = primaryTarget.x >= secondaryTarget.x;
                return;
            }
            if (secondaryArea < primaryArea * 0.8f)
            {
                chosen = secondaryTarget;
                chosenRight = secondaryTarget.x >= primaryTarget.x;
                return;
            }

            // Otherwise, keep the previous side if it's not worse.
            if (lastPlacedRight)
            {
                // Heuristic: right side corresponds to higher X in local canvas space.
                Vector2 rightT = primaryTarget.x >= secondaryTarget.x ? primaryTarget : secondaryTarget;
                Vector2 leftT = primaryTarget.x < secondaryTarget.x ? primaryTarget : secondaryTarget;
                float rightA = primaryTarget.x >= secondaryTarget.x ? primaryArea : secondaryArea;
                float leftA = primaryTarget.x < secondaryTarget.x ? primaryArea : secondaryArea;

                if (rightA <= leftA * 1.2f)
                {
                    chosen = rightT;
                    chosenRight = true;
                    return;
                }
            }
            else
            {
                Vector2 rightT = primaryTarget.x >= secondaryTarget.x ? primaryTarget : secondaryTarget;
                Vector2 leftT = primaryTarget.x < secondaryTarget.x ? primaryTarget : secondaryTarget;
                float rightA = primaryTarget.x >= secondaryTarget.x ? primaryArea : secondaryArea;
                float leftA = primaryTarget.x < secondaryTarget.x ? primaryArea : secondaryArea;

                if (leftA <= rightA * 1.2f)
                {
                    chosen = leftT;
                    chosenRight = false;
                    return;
                }
            }

            // Tie-break by distance from base target.
            float d1 = (primaryTarget - baseTarget).sqrMagnitude;
            float d2 = (secondaryTarget - baseTarget).sqrMagnitude;
            if (d1 <= d2)
            {
                chosen = primaryTarget;
                chosenRight = primaryTarget.x >= secondaryTarget.x;
            }
            else
            {
                chosen = secondaryTarget;
                chosenRight = secondaryTarget.x >= primaryTarget.x;
            }
        }

        static float OverlapArea(Rect a, Rect b)
        {
            float xMin = Mathf.Max(a.xMin, b.xMin);
            float xMax = Mathf.Min(a.xMax, b.xMax);
            float yMin = Mathf.Max(a.yMin, b.yMin);
            float yMax = Mathf.Min(a.yMax, b.yMax);
            if (xMax <= xMin || yMax <= yMin) return 0f;
            return (xMax - xMin) * (yMax - yMin);
        }

        TaskMonitorTheme CreateRuntimeTheme(TaskMonitorTheme source)
        {
            if (source == null) return null;

            // Clone so runtime edits (including dynamic TMP font population) won't dirty project assets.
            var runtimeTheme = Instantiate(source);
            runtimeTheme.hideFlags = HideFlags.DontSave;

            if (runtimeTheme.font != null)
                runtimeTheme.font = CreateRuntimeFont(runtimeTheme.font);

            return runtimeTheme;
        }

        TMP_FontAsset CreateRuntimeFont(TMP_FontAsset source)
        {
            if (source == null) return null;

            var runtimeFont = Instantiate(source);
            runtimeFont.hideFlags = HideFlags.DontSave;
            runtimeFont.name = source.name; // keep logs / debug output stable

            // Ensure dynamic glyph population can continue beyond the first atlas.
            runtimeFont.atlasPopulationMode = AtlasPopulationMode.Dynamic;
            runtimeFont.isMultiAtlasTexturesEnabled = true;

            // Clone material and atlas textures so dynamic population doesn't dirty imported assets in the editor.
            if (runtimeFont.material != null)
            {
                var runtimeMat = Instantiate(runtimeFont.material);
                runtimeMat.hideFlags = HideFlags.DontSave;
                runtimeFont.material = runtimeMat;
            }

            var atlases = runtimeFont.atlasTextures;
            if (atlases != null && atlases.Length > 0)
            {
                var runtimeAtlases = new Texture2D[atlases.Length];
                for (int i = 0; i < atlases.Length; i++)
                {
                    var tex = atlases[i];
                    if (tex == null) continue;
                    var runtimeTex = Instantiate(tex);
                    runtimeTex.hideFlags = HideFlags.DontSave;
                    runtimeAtlases[i] = runtimeTex;
                }

                runtimeFont.atlasTextures = runtimeAtlases;

                // Ensure the material is pointing at our runtime atlas.
                if (runtimeFont.material != null && runtimeAtlases[0] != null)
                    runtimeFont.material.mainTexture = runtimeAtlases[0];

                // Legacy field still present on TMP_FontAsset; keep it consistent.
                if (runtimeAtlases[0] != null)
                    runtimeFont.atlas = runtimeAtlases[0];
            }

            runtimeFont.ReadFontAssetDefinition();
            return runtimeFont;
        }

        static GameObject CreateRect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.localScale = Vector3.one;
            return go;
        }

        // ── Task Card (inner class) ──────────────────────────────

        class TaskCardUI
        {
            public bool IsCompleted;
            public float CompletedAt;

            readonly TaskMonitorTheme theme;

            GameObject root;
            CanvasGroup canvasGroup;
            Image cardBg;
            Color cardNormalColor;

            TextMeshProUGUI sourceLabel;
            TextMeshProUGUI elapsedLabel;
            GameObject cancelBtnGo;
            Image cancelBg;
            Image cancelIcon;
            GameObject activityContainer;
            float taskStartedAt;
            Image badgeBg;
            Image accentStrip;

            Image statusChipBg;
            Image statusIcon;
            RectTransform statusIconRT;
            TextMeshProUGUI statusLabel;

            int lastActivityCount;
            float flashStartedAt = -1f;

            // Group card: track active turns
            readonly HashSet<string> activeTurnIds = new();
            string lastSourceText;

            Color TextColor => theme != null ? theme.text : (Color)TextWhite;
            Color DimTextColor => theme != null ? theme.textDim : (Color)TextDim;
            Color ToolAccentColor => theme != null ? theme.toolAccent : (Color)AccentMagenta;
            Color WorkingAccentColor => theme != null && theme.workingAccent.a > 0.001f ? theme.workingAccent : ToolAccentColor;
            Color ThinkingAccentColor => theme != null && theme.thinkingAccent.a > 0.001f ? theme.thinkingAccent : ToolAccentColor;
            Color ToolCallAccentColor => theme != null && theme.toolCallAccent.a > 0.001f ? theme.toolCallAccent : ToolAccentColor;
            Color ChipBgColor => theme != null ? theme.chipBg : new Color(0f, 0f, 0f, 0.28f);
            Color CardTintColor => theme != null ? theme.cardTint : (Color)CardBg;
            Color IconDangerColor => theme != null ? theme.iconDanger : (Color)CancelledOrange;
            Color CancelAccentColor => theme != null && theme.cancelAccent.a > 0.001f ? theme.cancelAccent : IconDangerColor;

            float IconSize => theme != null ? Mathf.Max(12f, theme.iconSize) : 18f;
            float ChipHeight => theme != null ? Mathf.Max(16f, theme.chipHeight) : 20f;

            public TaskCardUI(RectTransform parent, TaskInfo task, TaskMonitorTheme theme)
            {
                this.theme = theme;
                taskStartedAt = task.StartedAt;
                activeTurnIds.Add(task.TurnId);
                lastActivityCount = task.ActivityLog != null ? task.ActivityLog.Count : 0;

                // Card root
                root = CreateRect("Card_" + task.TurnId, parent);
                cardBg = root.AddComponent<Image>();
                if (theme != null && theme.cardBackground != null)
                {
                    cardBg.sprite = theme.cardBackground;
                    cardBg.type = Image.Type.Sliced;
                    cardBg.color = CardTintColor;
                }
                else
                {
                    cardBg.color = CardTintColor;
                }
                cardNormalColor = cardBg.color;
                canvasGroup = root.AddComponent<CanvasGroup>();

                var layout = root.AddComponent<VerticalLayoutGroup>();
                layout.padding = new RectOffset(10, 8, 7, 7);
                layout.spacing = 5;
                layout.childControlWidth = true;
                layout.childControlHeight = true;
                layout.childForceExpandWidth = true;
                layout.childForceExpandHeight = false;

                // Accent strip (ignore layout)
                var accentGo = CreateRect("AccentStrip", root.transform);
                var accentLE = accentGo.AddComponent<LayoutElement>();
                accentLE.ignoreLayout = true;
                var accentRT = accentGo.GetComponent<RectTransform>();
                accentRT.anchorMin = new Vector2(0f, 0f);
                accentRT.anchorMax = new Vector2(0f, 1f);
                accentRT.pivot = new Vector2(0f, 0.5f);
                accentRT.anchoredPosition = Vector2.zero;
                float accentWidth = theme != null ? Mathf.Max(2f, theme.cardAccentWidth) : 4f;
                accentRT.sizeDelta = new Vector2(accentWidth, 0f);
                accentStrip = accentGo.AddComponent<Image>();
                accentStrip.raycastTarget = false;
                accentStrip.color = (task.Source != null && task.Source.SourceType == "whatsapp") ? WhatsAppGreen : ChatGreen;

                // Source header row
                var headerRow = CreateRect("SourceRow", root.transform);
                var headerHL = headerRow.AddComponent<HorizontalLayoutGroup>();
                headerHL.childControlWidth = true;
                headerHL.childControlHeight = true;
                headerHL.childForceExpandWidth = false;
                headerHL.childForceExpandHeight = false;
                headerHL.spacing = 5;

                // Source badge
                var badgeGo = CreateRect("Badge", headerRow.transform);
                badgeBg = badgeGo.AddComponent<Image>();
                if (theme != null && theme.pillBackground != null)
                {
                    badgeBg.sprite = theme.pillBackground;
                    badgeBg.type = Image.Type.Sliced;
                }
                badgeBg.raycastTarget = false;
                var badgeLE = badgeGo.AddComponent<LayoutElement>();
                badgeLE.preferredWidth = 10;
                badgeLE.preferredHeight = 10;

                if (task.Source != null && task.Source.SourceType == "whatsapp")
                    badgeBg.color = WhatsAppGreen;
                else
                    badgeBg.color = ChatGreen;

                // Source label (flexible to push elapsed + cancel right)
                var sourceLabelGo = CreateRect("SourceLabel", headerRow.transform);
                var sourceLabelLE = sourceLabelGo.AddComponent<LayoutElement>();
                sourceLabelLE.flexibleWidth = 1;
                sourceLabel = sourceLabelGo.AddComponent<TextMeshProUGUI>();
                sourceLabel.fontSize = 13;
                sourceLabel.fontStyle = FontStyles.Bold;
                if (theme != null && theme.font != null) sourceLabel.font = theme.font;
                sourceLabel.color = TextColor;
                sourceLabel.raycastTarget = false;
                lastSourceText = BuildSourceText(task);
                sourceLabel.text = lastSourceText;

                // Elapsed time label
                var elapsedGo = CreateRect("Elapsed", headerRow.transform);
                var elapsedLE = elapsedGo.AddComponent<LayoutElement>();
                elapsedLE.preferredWidth = 40;
                elapsedLabel = elapsedGo.AddComponent<TextMeshProUGUI>();
                elapsedLabel.fontSize = 10;
                if (theme != null && theme.font != null) elapsedLabel.font = theme.font;
                elapsedLabel.color = DimTextColor;
                elapsedLabel.alignment = TextAlignmentOptions.Right;
                elapsedLabel.text = "0s";
                elapsedLabel.raycastTarget = false;

                // Cancel button
                cancelBtnGo = CreateRect("Cancel", headerRow.transform);
                var cancelLE = cancelBtnGo.AddComponent<LayoutElement>();
                float hit = Mathf.Max(20f, IconSize + 8f);
                cancelLE.preferredWidth = hit;
                cancelLE.preferredHeight = hit;

                cancelBg = cancelBtnGo.AddComponent<Image>();
                if (theme != null && theme.pillBackground != null)
                {
                    cancelBg.sprite = theme.pillBackground;
                    cancelBg.type = Image.Type.Sliced;
                    cancelBg.color = new Color(0f, 0f, 0f, 0.12f);
                }
                else
                {
                    cancelBg.color = new Color(0f, 0f, 0f, 0f);
                }

                var cancelIconGo = CreateRect("Icon", cancelBtnGo.transform);
                var cancelIconRT = cancelIconGo.GetComponent<RectTransform>();
                cancelIconRT.anchorMin = Vector2.zero;
                cancelIconRT.anchorMax = Vector2.one;
                cancelIconRT.offsetMin = new Vector2(4, 4);
                cancelIconRT.offsetMax = new Vector2(-4, -4);
                cancelIcon = cancelIconGo.AddComponent<Image>();
                cancelIcon.sprite = theme != null ? (theme.iconCancel != null ? theme.iconCancel : theme.iconClose) : null;
                cancelIcon.preserveAspect = true;
                cancelIcon.color = CancelAccentColor;
                cancelIcon.raycastTarget = false;

                var cancelBtn = cancelBtnGo.AddComponent<Button>();
                cancelBtn.targetGraphic = cancelBg;
                cancelBtn.transition = Selectable.Transition.ColorTint;
                var cancelColors = cancelBtn.colors;
                cancelColors.normalColor = cancelBg.color;
                cancelColors.highlightedColor = new Color(cancelBg.color.r, cancelBg.color.g, cancelBg.color.b, Mathf.Min(0.25f, cancelBg.color.a + 0.12f));
                cancelColors.pressedColor = new Color(cancelBg.color.r, cancelBg.color.g, cancelBg.color.b, Mathf.Min(0.35f, cancelBg.color.a + 0.20f));
                cancelColors.selectedColor = cancelColors.highlightedColor;
                cancelColors.disabledColor = new Color(cancelBg.color.r, cancelBg.color.g, cancelBg.color.b, 0f);
                cancelBtn.colors = cancelColors;
                cancelBtn.onClick.AddListener(OnCancelClicked);

                // Status chip
                var statusGo = CreateRect("StatusChip", root.transform);
                statusChipBg = statusGo.AddComponent<Image>();
                if (theme != null && theme.pillBackground != null)
                {
                    statusChipBg.sprite = theme.pillBackground;
                    statusChipBg.type = Image.Type.Sliced;
                }
                statusChipBg.color = ChipBgColor;
                statusChipBg.raycastTarget = false;
                var statusLE = statusGo.AddComponent<LayoutElement>();
                statusLE.preferredHeight = ChipHeight;

                var statusIconGo = CreateRect("StatusIcon", statusGo.transform);
                statusIconRT = statusIconGo.GetComponent<RectTransform>();
                statusIconRT.anchorMin = new Vector2(0f, 0.5f);
                statusIconRT.anchorMax = new Vector2(0f, 0.5f);
                statusIconRT.pivot = new Vector2(0.5f, 0.5f);
                float icon = Mathf.Max(12f, IconSize - 4f);
                statusIconRT.sizeDelta = new Vector2(icon, icon);
                statusIconRT.anchoredPosition = new Vector2(8f + (icon * 0.5f), 0f);
                statusIcon = statusIconGo.AddComponent<Image>();
                statusIcon.sprite = theme != null ? theme.iconRefresh : null;
                statusIcon.preserveAspect = true;
                statusIcon.color = WorkingAccentColor;
                statusIcon.raycastTarget = false;

                var statusLabelGo = CreateRect("StatusLabel", statusGo.transform);
                var statusLabelRT = statusLabelGo.GetComponent<RectTransform>();
                statusLabelRT.anchorMin = Vector2.zero;
                statusLabelRT.anchorMax = Vector2.one;
                statusLabelRT.offsetMin = new Vector2(8f + icon + 6f, 0f);
                statusLabelRT.offsetMax = new Vector2(-8f, 0f);
                statusLabel = statusLabelGo.AddComponent<TextMeshProUGUI>();
                if (theme != null && theme.font != null) statusLabel.font = theme.font;
                statusLabel.fontSize = 11;
                statusLabel.color = WorkingAccentColor;
                statusLabel.alignment = TextAlignmentOptions.MidlineLeft;
                statusLabel.text = "Working...";
                statusLabel.raycastTarget = false;

                // Activity entries container (no nested ScrollRect)
                activityContainer = CreateRect("ActivityEntries", root.transform);
                var activityLayout = activityContainer.AddComponent<VerticalLayoutGroup>();
                activityLayout.spacing = 1;
                activityLayout.childControlWidth = true;
                activityLayout.childControlHeight = true;
                activityLayout.childForceExpandWidth = true;
                activityLayout.childForceExpandHeight = false;

                // Initial activity refresh
                RefreshActivity(task);
            }

            public void Tick(float now)
            {
                if (cardBg != null && flashStartedAt > 0f)
                {
                    float d = theme != null ? Mathf.Max(0.05f, theme.activityFlashDuration) : 0.22f;
                    float t = (now - flashStartedAt) / d;
                    if (t >= 1f)
                    {
                        flashStartedAt = -1f;
                        cardBg.color = cardNormalColor;
                    }
                    else
                    {
                        var flash = theme != null ? theme.activityFlashTint : new Color(1f, 1f, 1f, 1f);
                        var flashColor = new Color(
                            Mathf.Clamp01(cardNormalColor.r * flash.r),
                            Mathf.Clamp01(cardNormalColor.g * flash.g),
                            Mathf.Clamp01(cardNormalColor.b * flash.b),
                            cardNormalColor.a);
                        cardBg.color = Color.Lerp(flashColor, cardNormalColor, t);
                    }
                }

                if (!IsCompleted && statusIconRT != null && statusIcon != null && statusIcon.sprite != null)
                {
                    // Subtle spinner for "Working..."
                    if (theme != null && theme.iconRefresh != null && statusIcon.sprite == theme.iconRefresh)
                        statusIconRT.localEulerAngles = new Vector3(0f, 0f, -now * 180f);
                    else
                        statusIconRT.localEulerAngles = Vector3.zero;
                }
            }

            public void AddTurn(TaskInfo task)
            {
                activeTurnIds.Add(task.TurnId);
                taskStartedAt = task.StartedAt; // reset timer to latest

                // Update source label
                string newSource = BuildSourceText(task);
                if (newSource != lastSourceText)
                {
                    lastSourceText = newSource;
                    sourceLabel.text = lastSourceText;
                }

                // Reset to working if was completed
                if (IsCompleted)
                {
                    IsCompleted = false;
                    statusLabel.text = "Working...";
                    statusLabel.color = WorkingAccentColor;
                    if (statusIcon != null)
                    {
                        statusIcon.sprite = theme != null ? theme.iconRefresh : null;
                        statusIcon.color = WorkingAccentColor;
                        statusIcon.enabled = statusIcon.sprite != null;
                    }
                    cancelBtnGo.SetActive(true);
                    SetAlpha(1f);
                }

                RefreshActivity(task);
            }

            public void HandleTurnCompleted(TaskInfo task)
            {
                activeTurnIds.Remove(task.TurnId);
            }

            public bool AllTurnsCompleted()
            {
                return activeTurnIds.Count == 0;
            }

            public void RefreshActivity(TaskInfo task)
            {
                if (root == null || activityContainer == null) return;

                string NormalizeLabel(string label)
                {
                    if (string.IsNullOrEmpty(label)) return "";
                    label = label.Trim();
                    // Strip "$" prefixes used by some protocol/status tokens.
                    while (label.Length > 0 && label[0] == '$')
                        label = label.Substring(1).TrimStart();

                    // Defensive: strip emoji-like UTF-16 sequences so TMP doesn't render them as "□" if the
                    // chosen font asset doesn't contain emoji glyphs (and to avoid missing-glyph log spam).
                    if (label.Length > 0)
                    {
                        var sb = new StringBuilder(label.Length);
                        for (int i = 0; i < label.Length; i++)
                        {
                            char c = label[i];
                            if (char.IsHighSurrogate(c))
                            {
                                if (i + 1 < label.Length && char.IsLowSurrogate(label[i + 1]))
                                    i++; // skip surrogate pair
                                continue;
                            }
                            if (char.IsLowSurrogate(c))
                                continue;

                            if (c == '\uFE0E' || c == '\uFE0F' || c == '\u200D' || c == '\u20E3')
                                continue;

                            sb.Append(c);
                        }

                        label = sb.ToString();
                    }

                    return label;
                }

                bool IsPlaceholderLabel(string label)
                {
                    if (string.IsNullOrEmpty(label)) return true;
                    return label == "Working..." || label == "Responding..." || label == "Working" || label == "Responding";
                }

                var log = task.ActivityLog;
                int newCount = log != null ? log.Count : 0;
                if (newCount > lastActivityCount)
                    flashStartedAt = Time.unscaledTime;
                lastActivityCount = newCount;

                // Destroy existing entry children
                for (int i = activityContainer.transform.childCount - 1; i >= 0; i--)
                    UnityEngine.Object.Destroy(activityContainer.transform.GetChild(i).gameObject);

                if (log == null || newCount == 0)
                    return;

                // Filter out placeholder / empty entries, then show last N.
                var filtered = new List<ActivityEntry>(newCount);
                for (int i = 0; i < newCount; i++)
                {
                    var e = log[i];
                    var raw = NormalizeLabel(e.Label);
                    if (IsPlaceholderLabel(raw)) continue;
                    filtered.Add(e);
                }

                if (filtered.Count == 0)
                    return;

                int startIdx = Mathf.Max(0, filtered.Count - MaxDisplayedActivityEntries);

                // Pre-warm glyphs for any text we are about to render to reduce missing-glyph spam and
                // avoid transient "□" squares when the atlas is being populated at runtime.
                if (theme != null && theme.font != null && theme.font.atlasPopulationMode == AtlasPopulationMode.Dynamic)
                {
                    var sb = new StringBuilder();
                    if (!string.IsNullOrEmpty(lastSourceText))
                        sb.Append(lastSourceText);

                    sb.Append(' ');
                    sb.Append('•');

                    for (int i = startIdx; i < filtered.Count; i++)
                    {
                        var entry = filtered[i];
                        string label = NormalizeLabel(entry.Label);
                        if (label.Length > MaxEntryLabelChars)
                            label = label.Substring(0, MaxEntryLabelChars) + "...";

                        if (!string.IsNullOrEmpty(label))
                            sb.Append(label);

                        if (!string.IsNullOrEmpty(entry.Detail))
                            sb.Append(entry.Detail);
                    }

                    if (sb.Length > 0)
                    {
                        try
                        {
                            theme.font.TryAddCharacters(sb.ToString(), out _);
                        }
                        catch
                        {
                            // Ignore font warm-up failures; TMP will still attempt to render using fallbacks.
                        }
                    }
                }

                for (int i = startIdx; i < filtered.Count; i++)
                {
                    var entry = filtered[i];
                    var entryGo = CreateRect("Entry_" + i, activityContainer.transform);
                    var entryText = entryGo.AddComponent<TextMeshProUGUI>();
                    entryText.fontSize = 10;
                    if (theme != null && theme.font != null) entryText.font = theme.font;
                    entryText.enableWordWrapping = false;
                    entryText.overflowMode = TextOverflowModes.Ellipsis;
                    entryText.maxVisibleLines = 1;
                    entryText.raycastTarget = false;

                    const string icon = "• ";

                    string label = NormalizeLabel(entry.Label);
                    // Truncate long labels for display
                    if (label.Length > MaxEntryLabelChars)
                        label = label.Substring(0, MaxEntryLabelChars) + "...";

                    string detail = "";
                    if (!string.IsNullOrEmpty(entry.Detail))
                        detail = "  " + entry.Detail;

                    entryText.text = icon + label + detail;

                    // Color based on status
                    if (entry.Status == TaskItemStatus.Active)
                    {
                        switch (entry.Type)
                        {
                            case "tool":
                                entryText.color = ToolCallAccentColor;
                                break;
                            case "thinking":
                                entryText.color = ThinkingAccentColor;
                                break;
                            case "response":
                                entryText.color = WorkingAccentColor;
                                break;
                            default:
                                entryText.color = TextColor;
                                break;
                        }
                    }
                    else
                    {
                        entryText.color = DimTextColor;
                    }
                }
            }

            public void MarkCompleted(TaskInfo task)
            {
                if (IsCompleted) return; // guard: already completed (was cancelled)

                IsCompleted = true;
                cancelBtnGo.SetActive(false);

                if (task.Status == TaskStatus.Cancelled)
                {
                    statusLabel.text = "Cancelled";
                    statusLabel.color = CancelAccentColor;
                    if (statusIcon != null)
                    {
                        statusIcon.sprite = theme != null ? (theme.iconCancel != null ? theme.iconCancel : theme.iconClose) : null;
                        statusIcon.color = CancelAccentColor;
                        statusIcon.enabled = statusIcon.sprite != null;
                        if (statusIconRT != null) statusIconRT.localEulerAngles = Vector3.zero;
                    }
                }
                else
                {
                    statusLabel.text = "Done";
                    statusLabel.color = (Color)ChatGreen;
                    if (statusIcon != null)
                    {
                        statusIcon.sprite = theme != null ? theme.iconCheck : null;
                        statusIcon.color = (Color)ChatGreen;
                        statusIcon.enabled = statusIcon.sprite != null;
                        if (statusIconRT != null) statusIconRT.localEulerAngles = Vector3.zero;
                    }
                }

                // Final activity refresh
                RefreshActivity(task);
            }

            public void UpdateElapsed(float now)
            {
                if (elapsedLabel != null)
                    elapsedLabel.text = FormatDuration(now - taskStartedAt);
            }

            public void SetAlpha(float alpha)
            {
                if (canvasGroup != null)
                    canvasGroup.alpha = alpha;
            }

            public void Destroy()
            {
                if (root != null)
                    UnityEngine.Object.Destroy(root);
            }

            void OnCancelClicked()
            {
                CodexBridge.Instance?.CancelCurrentTurn();
            }

            static string BuildSourceText(TaskInfo task)
            {
                if (task.Source == null)
                    return task.ThreadName ?? "Task";

                if (task.Source.SourceType == "whatsapp")
                {
                    string group = task.Source.SourceGroup ?? "WhatsApp";
                    return "WA: " + group;
                }

                return "Chat";
            }

            static string FormatDuration(float seconds)
            {
                if (seconds < 0) return "";
                if (seconds < 60) return Mathf.FloorToInt(seconds) + "s";
                int m = Mathf.FloorToInt(seconds / 60);
                int s = Mathf.FloorToInt(seconds % 60);
                return m + "m" + (s > 0 ? s + "s" : "");
            }
        }
    }
}
