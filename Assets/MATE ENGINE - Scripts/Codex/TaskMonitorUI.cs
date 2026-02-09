using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace MateEngine.Codex
{
    public class TaskMonitorUI : MonoBehaviour
    {
        // ── Configuration ─────────────────────────────────────────
        const float PanelWidth = 340f;
        const float MaxPanelHeight = 420f;
        const float PillSize = 40f;
        const float PinnedCornerMargin = 16f;
        const float FadeOutDelay = 10f;
        const float FadeOutDuration = 1.5f;
        const float ExpandSpeed = 8f;
        const int MaxDisplayedActivityEntries = 8;

        [Header("Follow Avatar")]
        [SerializeField] bool followAvatar = true;
        [SerializeField] bool followWhenExpanded = true;
        [SerializeField] HumanBodyBones followAnchorBone = HumanBodyBones.Chest;
        [SerializeField] Vector2 followOffset = new(220f, -180f);
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

        // ── State ─────────────────────────────────────────────────
        bool expanded;
        float expandLerp;
        Canvas canvas;
        RectTransform canvasRT;
        CanvasGroup panelCanvasGroup;
        RectTransform monitorRootRT;
        GameObject pillObject;
        RectTransform pillRT;
        GameObject panelObject;
        RectTransform panelRT;
        TextMeshProUGUI badgeText;
        RectTransform contentParent;

        Vector2 followVelocity;
        bool hasLastValidFollowPos;
        Vector2 lastValidFollowPos;

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
            panelObject.transform.localScale = new Vector3(1f, expandLerp, 1f);
            panelObject.SetActive(expandLerp > 0.01f);

            // Update active task count badge
            int activeCount = 0;
            foreach (var kv in cards)
                if (!kv.Value.IsCompleted) activeCount++;
            badgeText.text = activeCount > 0 ? activeCount.ToString() : "";
            pillObject.SetActive(activeCount > 0 || expanded);

            // Pulsing dot on pill
            if (activeCount > 0)
            {
                float pulse = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 4f);
                var dot = pillObject.transform.Find("Dot");
                if (dot != null)
                {
                    var img = dot.GetComponent<Image>();
                    if (img != null)
                    {
                        var c = (Color)PulsingDot;
                        c.a = pulse;
                        img.color = c;
                    }
                }
            }

            // Update cards (elapsed time, fade-outs)
            pendingRemoval.Clear();
            foreach (var kv in cards)
            {
                var card = kv.Value;
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
                var card = new TaskCardUI(contentParent, task);
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

            var pillBg = pillObject.AddComponent<Image>();
            pillBg.color = PanelBg;

            var pillBtn = pillObject.AddComponent<Button>();
            pillBtn.targetGraphic = pillBg;
            pillBtn.onClick.AddListener(() => expanded = !expanded);

            // Pulsing dot
            var dotGo = CreateRect("Dot", pillObject.transform);
            var dotRT = dotGo.GetComponent<RectTransform>();
            dotRT.anchorMin = new Vector2(0.5f, 0.5f);
            dotRT.anchorMax = new Vector2(0.5f, 0.5f);
            dotRT.sizeDelta = new Vector2(12, 12);
            dotRT.anchoredPosition = new Vector2(-4, 0);
            var dotImg = dotGo.AddComponent<Image>();
            dotImg.color = PulsingDot;

            // Badge text
            var badgeGo = CreateRect("Badge", pillObject.transform);
            var badgeRT = badgeGo.GetComponent<RectTransform>();
            badgeRT.anchorMin = Vector2.zero;
            badgeRT.anchorMax = Vector2.one;
            badgeRT.offsetMin = Vector2.zero;
            badgeRT.offsetMax = Vector2.zero;
            badgeText = badgeGo.AddComponent<TextMeshProUGUI>();
            badgeText.text = "";
            badgeText.fontSize = 14;
            badgeText.color = TextWhite;
            badgeText.alignment = TextAlignmentOptions.Center;

            pillObject.SetActive(false);

            // ── Expanded Panel ────────────────────────────────────
            panelObject = CreateRect("Panel", monitorRootGo.transform);
            panelRT = panelObject.GetComponent<RectTransform>();
            panelRT.anchorMin = new Vector2(1, 0);
            panelRT.anchorMax = new Vector2(1, 0);
            panelRT.pivot = new Vector2(1, 0);
            panelRT.anchoredPosition = new Vector2(0, PillSize + 8);
            panelRT.sizeDelta = new Vector2(PanelWidth, MaxPanelHeight);

            var panelBg = panelObject.AddComponent<Image>();
            panelBg.color = PanelBg;

            panelCanvasGroup = panelObject.AddComponent<CanvasGroup>();

            // Panel layout
            var panelLayout = panelObject.AddComponent<VerticalLayoutGroup>();
            panelLayout.padding = new RectOffset(8, 8, 8, 8);
            panelLayout.spacing = 4;
            panelLayout.childControlWidth = true;
            panelLayout.childControlHeight = true;
            panelLayout.childForceExpandWidth = true;
            panelLayout.childForceExpandHeight = false;

            // Header
            var headerGo = CreateRect("Header", panelObject.transform);
            var headerLayout = headerGo.AddComponent<HorizontalLayoutGroup>();
            headerLayout.childControlWidth = true;
            headerLayout.childControlHeight = true;
            headerLayout.childForceExpandWidth = true;
            headerLayout.childForceExpandHeight = false;
            var headerLE = headerGo.AddComponent<LayoutElement>();
            headerLE.preferredHeight = 24;

            var titleGo = CreateRect("Title", headerGo.transform);
            var titleText = titleGo.AddComponent<TextMeshProUGUI>();
            titleText.text = "Task Monitor";
            titleText.fontSize = 15;
            titleText.fontStyle = FontStyles.Bold;
            titleText.color = TextWhite;

            var closeBtnGo = CreateRect("Close", headerGo.transform);
            var closeBtnLE = closeBtnGo.AddComponent<LayoutElement>();
            closeBtnLE.preferredWidth = 24;
            closeBtnLE.preferredHeight = 24;
            var closeBtnText = closeBtnGo.AddComponent<TextMeshProUGUI>();
            closeBtnText.text = "-";
            closeBtnText.fontSize = 18;
            closeBtnText.fontStyle = FontStyles.Bold;
            closeBtnText.color = TextWhite;
            closeBtnText.alignment = TextAlignmentOptions.Center;
            var closeBtn = closeBtnGo.AddComponent<Button>();
            closeBtn.targetGraphic = closeBtnText;
            closeBtn.onClick.AddListener(() => expanded = false);

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
            contentLayout.spacing = 6;
            contentLayout.childControlWidth = true;
            contentLayout.childControlHeight = true;
            contentLayout.childForceExpandWidth = true;
            contentLayout.childForceExpandHeight = false;

            var contentCSF = contentGo.AddComponent<ContentSizeFitter>();
            contentCSF.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            scrollRect.content = contentParent;
            scrollRect.viewport = viewportRT;

            scrollGo.AddComponent<ScrollHelper>();

            panelObject.SetActive(false);
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
            bool usePanelExtents = expanded || expandLerp > 0.01f;

            if (usePanelExtents && panelRT != null)
            {
                float panelW = panelRT.rect.width > 0f ? panelRT.rect.width : PanelWidth;
                float panelH = panelRT.rect.height > 0f ? panelRT.rect.height : MaxPanelHeight;
                minX = -panelW;
                maxX = 0f;
                minY = 0f;
                maxY = (PillSize + 8f) + panelH;
                return;
            }

            if (pillRT != null)
            {
                float pillW = pillRT.rect.width > 0f ? pillRT.rect.width : PillSize;
                float pillH = pillRT.rect.height > 0f ? pillRT.rect.height : PillSize;
                minX = -pillW;
                maxX = 0f;
                minY = 0f;
                maxY = pillH;
                return;
            }

            minX = -PillSize;
            maxX = 0f;
            minY = 0f;
            maxY = PillSize;
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

            GameObject root;
            CanvasGroup canvasGroup;
            TextMeshProUGUI sourceLabel;
            TextMeshProUGUI elapsedLabel;
            TextMeshProUGUI statusLabel;
            GameObject cancelBtnGo;
            GameObject activityContainer;
            float taskStartedAt;
            Image badgeBg;

            // Group card: track active turns
            readonly HashSet<string> activeTurnIds = new();
            string lastSourceText;

            public TaskCardUI(RectTransform parent, TaskInfo task)
            {
                taskStartedAt = task.StartedAt;
                activeTurnIds.Add(task.TurnId);

                // Card root
                root = CreateRect("Card_" + task.TurnId, parent);
                var bg = root.AddComponent<Image>();
                bg.color = CardBg;
                canvasGroup = root.AddComponent<CanvasGroup>();

                var layout = root.AddComponent<VerticalLayoutGroup>();
                layout.padding = new RectOffset(8, 8, 6, 6);
                layout.spacing = 3;
                layout.childControlWidth = true;
                layout.childControlHeight = true;
                layout.childForceExpandWidth = true;
                layout.childForceExpandHeight = false;

                // Source header row
                var headerRow = CreateRect("SourceRow", root.transform);
                var headerHL = headerRow.AddComponent<HorizontalLayoutGroup>();
                headerHL.childControlWidth = true;
                headerHL.childControlHeight = true;
                headerHL.childForceExpandWidth = false;
                headerHL.childForceExpandHeight = false;
                headerHL.spacing = 6;

                // Source badge
                var badgeGo = CreateRect("Badge", headerRow.transform);
                badgeBg = badgeGo.AddComponent<Image>();
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
                sourceLabel.color = TextWhite;
                lastSourceText = BuildSourceText(task);
                sourceLabel.text = lastSourceText;

                // Elapsed time label
                var elapsedGo = CreateRect("Elapsed", headerRow.transform);
                var elapsedLE = elapsedGo.AddComponent<LayoutElement>();
                elapsedLE.preferredWidth = 40;
                elapsedLabel = elapsedGo.AddComponent<TextMeshProUGUI>();
                elapsedLabel.fontSize = 11;
                elapsedLabel.color = TextDim;
                elapsedLabel.alignment = TextAlignmentOptions.Right;
                elapsedLabel.text = "0s";

                // Cancel button
                cancelBtnGo = CreateRect("Cancel", headerRow.transform);
                var cancelLE = cancelBtnGo.AddComponent<LayoutElement>();
                cancelLE.preferredWidth = 20;
                cancelLE.preferredHeight = 20;
                var cancelBgImg = cancelBtnGo.AddComponent<Image>();
                cancelBgImg.color = new Color(0, 0, 0, 0); // transparent but raycast-active
                // TMP text must live on a child object (a GameObject can only have 1 Graphic component).
                var cancelTextGo = CreateRect("Label", cancelBtnGo.transform);
                var cancelTextRT = cancelTextGo.GetComponent<RectTransform>();
                cancelTextRT.anchorMin = Vector2.zero;
                cancelTextRT.anchorMax = Vector2.one;
                cancelTextRT.offsetMin = Vector2.zero;
                cancelTextRT.offsetMax = Vector2.zero;
                var cancelText = cancelTextGo.AddComponent<TextMeshProUGUI>();
                cancelText.text = "X";
                cancelText.fontSize = 13;
                cancelText.fontStyle = FontStyles.Bold;
                cancelText.color = CancelledOrange;
                cancelText.alignment = TextAlignmentOptions.Center;
                cancelText.raycastTarget = false;
                var cancelBtn = cancelBtnGo.AddComponent<Button>();
                cancelBtn.targetGraphic = cancelBgImg;
                cancelBtn.onClick.AddListener(OnCancelClicked);

                // Status label (replaces progress bar)
                var statusGo = CreateRect("Status", root.transform);
                statusLabel = statusGo.AddComponent<TextMeshProUGUI>();
                statusLabel.fontSize = 12;
                statusLabel.color = AccentMagenta;
                statusLabel.text = "Working...";

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
                    statusLabel.color = AccentMagenta;
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

                // Destroy existing entry children
                for (int i = activityContainer.transform.childCount - 1; i >= 0; i--)
                    UnityEngine.Object.Destroy(activityContainer.transform.GetChild(i).gameObject);

                // Show last N entries
                var log = task.ActivityLog;
                int startIdx = Mathf.Max(0, log.Count - MaxDisplayedActivityEntries);

                for (int i = startIdx; i < log.Count; i++)
                {
                    var entry = log[i];
                    var entryGo = CreateRect("Entry_" + i, activityContainer.transform);
                    var entryText = entryGo.AddComponent<TextMeshProUGUI>();
                    entryText.fontSize = 11;
                    entryText.enableWordWrapping = true;
                    entryText.overflowMode = TextOverflowModes.Truncate;
                    entryText.maxVisibleLines = 2;

                    // Icon prefix based on type
                    string icon;
                    switch (entry.Type)
                    {
                        case "query": icon = "> "; break;
                        case "response": icon = "< "; break;
                        default: icon = "$ "; break; // tool, thinking
                    }

                    string label = entry.Label ?? "";
                    // Truncate long labels for display
                    if (label.Length > 80)
                        label = label.Substring(0, 80) + "...";

                    string detail = "";
                    if (!string.IsNullOrEmpty(entry.Detail))
                        detail = "  " + entry.Detail;

                    entryText.text = icon + label + detail;

                    // Color based on status
                    if (entry.Status == TaskItemStatus.Active)
                    {
                        entryText.color = entry.Type == "tool" ? (Color)AccentMagenta : (Color)TextWhite;
                    }
                    else
                    {
                        entryText.color = TextDim;
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
                    statusLabel.color = CancelledOrange;
                }
                else
                {
                    statusLabel.text = "Done!";
                    statusLabel.color = ChatGreen;
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
