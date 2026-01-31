using UnityEngine;
using Kirurobo;

public class AvatarHideHandler : MonoBehaviour
{
    public int snapThresholdPx = 12;
    public int unsnapThresholdPx = 24;
    public int edgeInsetPx = 0;

    public int adjacencyTolerancePx = 6;
    public int adjacencyMinVerticalOverlapPx = 32;

    public int snapCalibrationFrames = 10;
    public int maxSnapCompensationPx = 96;

    public bool enableSmoothing = true;
    [Range(0.01f, 0.5f)] public float smoothingTime = 0.10f;
    public float smoothingMaxSpeed = 6000f;
    public bool keepTopmostWhileSnapped = true;
    public float unsnapGraceTime = 0.12f;
    public float unsnapCooldownSeconds = 0.3f;

    Animator animator;
    AvatarAnimatorController controller;

    Transform leftHand;
    Transform rightHand;
    Camera cam;

    enum Side { None, Left, Right }
    Side snappedSide = Side.None;

    int cursorOffsetY;
    float velX, velY;
    bool smoothingActive;
    bool wasDragging;
    float snappedAt;
    float unsnapCooldownUntil;

    int dragBaseW;
    int dragBaseH;

    int snapCompX;
    int calibRemaining;

    // Cached state for edge-hide logic
    Vector2 prevWindowPos;
    Vector2 smoothVel;
    float hiddenX;        // target X when hidden
    float visibleX;       // target X when visible (restored position)
    bool cursorNearEdge;
    float cursorLeftEdgeTime;  // last time cursor left the near-edge zone

    UniWindowController uwc;

    void Start()
    {
        animator = GetComponent<Animator>();
        controller = GetComponent<AvatarAnimatorController>();
        if (animator != null && animator.isHuman && animator.avatar != null)
        {
            leftHand = animator.GetBoneTransform(HumanBodyBones.LeftHand);
            rightHand = animator.GetBoneTransform(HumanBodyBones.RightHand);
        }
        cam = Camera.main;
        if (cam == null) cam = FindObjectOfType<Camera>();
        unsnapCooldownUntil = -1f;
        dragBaseW = 0;
        dragBaseH = 0;
        snapCompX = 0;
        calibRemaining = 0;
    }

    void OnDisable()
    {
        if (snappedSide != Side.None) Unsnap();
        snappedSide = Side.None;
        unsnapCooldownUntil = -1f;
        snapCompX = 0;
        calibRemaining = 0;
    }

    void Update()
    {
        if (uwc == null)
        {
            uwc = UniWindowController.current;
            if (uwc == null) return;
            prevWindowPos = uwc.windowPosition;
        }

        Vector2 cursor = UniWindowController.GetCursorPosition();
        Vector2 winPos = uwc.windowPosition;
        Vector2 winSize = uwc.windowSize;

        // Find which monitor the window center is on
        Rect monRect = GetMonitorForWindow(winPos, winSize);

        bool isDragging = controller != null && controller.isDragging;

        // --- Dragging: detect snap opportunity ---
        if (isDragging)
        {
            if (snappedSide == Side.None && Time.time > unsnapCooldownUntil)
            {
                float winLeft = winPos.x;
                float winRight = winPos.x + winSize.x;
                float monLeft = monRect.x;
                float monRight = monRect.x + monRect.width;

                // Check left edge
                if (Mathf.Abs(winLeft - monLeft) < snapThresholdPx)
                {
                    SnapTo(Side.Left, winPos, winSize, monRect);
                }
                // Check right edge
                else if (Mathf.Abs(winRight - monRight) < snapThresholdPx)
                {
                    SnapTo(Side.Right, winPos, winSize, monRect);
                }
            }
            wasDragging = true;
        }
        else
        {
            if (wasDragging && snappedSide == Side.None)
            {
                wasDragging = false;
            }
        }

        // --- Snapped state: handle peek/hide ---
        if (snappedSide != Side.None)
        {
            // If user starts dragging while snapped, unsnap
            if (isDragging && wasDragging)
            {
                // User is actively dragging, check if they moved away from edge
                float winLeft = winPos.x;
                float winRight = winPos.x + winSize.x;
                float monLeft = monRect.x;
                float monRight = monRect.x + monRect.width;

                bool stillNearEdge = false;
                if (snappedSide == Side.Left)
                    stillNearEdge = Mathf.Abs(winLeft - monLeft) < unsnapThresholdPx * 2;
                else
                    stillNearEdge = Mathf.Abs(winRight - monRight) < unsnapThresholdPx * 2;

                if (!stillNearEdge)
                {
                    Unsnap();
                    return;
                }
            }

            // Determine if cursor is near the snapped edge
            bool nearEdge = false;
            if (snappedSide == Side.Left)
            {
                nearEdge = cursor.x <= monRect.x + unsnapThresholdPx;
            }
            else if (snappedSide == Side.Right)
            {
                nearEdge = cursor.x >= monRect.x + monRect.width - unsnapThresholdPx;
            }

            // Also check cursor is within vertical range of the monitor
            nearEdge = nearEdge && cursor.y >= monRect.y && cursor.y <= monRect.y + monRect.height;

            // Grace time: don't immediately hide when cursor leaves edge zone
            if (nearEdge)
            {
                cursorNearEdge = true;
                cursorLeftEdgeTime = 0f;
            }
            else
            {
                if (cursorNearEdge)
                {
                    cursorLeftEdgeTime = Time.time;
                    cursorNearEdge = false;
                }
            }

            bool shouldShow = cursorNearEdge ||
                              (cursorLeftEdgeTime > 0f && Time.time - cursorLeftEdgeTime < unsnapGraceTime);

            // Compute target X
            float targetX;
            if (shouldShow)
            {
                targetX = visibleX;
            }
            else
            {
                targetX = hiddenX;
            }

            // Move window toward target
            Vector2 currentPos = uwc.windowPosition;
            float targetY = currentPos.y; // keep Y stable

            if (enableSmoothing)
            {
                float newX = Mathf.SmoothDamp(currentPos.x, targetX, ref velX, smoothingTime,
                    smoothingMaxSpeed, Time.unscaledDeltaTime);
                uwc.windowPosition = new Vector2(newX, targetY);
            }
            else
            {
                uwc.windowPosition = new Vector2(targetX, targetY);
            }

            // Set animator hide state
            if (animator != null)
            {
                bool isCurrentlyHidden = !shouldShow && Mathf.Abs(uwc.windowPosition.x - hiddenX) < 2f;
                animator.SetBool("isHide", !shouldShow);
            }
        }

        prevWindowPos = uwc.windowPosition;
    }

    void SnapTo(Side side, Vector2 winPos, Vector2 winSize, Rect monRect)
    {
        snappedSide = side;
        snappedAt = Time.time;
        cursorNearEdge = false;
        cursorLeftEdgeTime = 0f;
        velX = 0f;

        // visibleX: where the window sits when peeking out (flush with edge)
        // hiddenX: where the window sits when hidden (mostly off-screen)
        if (side == Side.Left)
        {
            visibleX = monRect.x;
            hiddenX = monRect.x - winSize.x + edgeInsetPx;
        }
        else // Right
        {
            visibleX = monRect.x + monRect.width - winSize.x;
            hiddenX = monRect.x + monRect.width - edgeInsetPx;
        }

        // Immediately move to hidden position
        uwc.windowPosition = new Vector2(hiddenX, winPos.y);

        if (animator != null)
            animator.SetBool("isHide", true);

        if (keepTopmostWhileSnapped && uwc != null)
            uwc.isTopmost = true;

        wasDragging = false;
    }

    void Unsnap()
    {
        if (snappedSide == Side.None) return;

        // Restore to visible position
        Vector2 pos = uwc.windowPosition;
        uwc.windowPosition = new Vector2(visibleX, pos.y);

        snappedSide = Side.None;
        unsnapCooldownUntil = Time.time + unsnapCooldownSeconds;
        velX = 0f;

        if (animator != null)
            animator.SetBool("isHide", false);

        // Restore topmost to user's saved preference
        if (keepTopmostWhileSnapped && uwc != null)
        {
            var data = SaveLoadHandler.Instance?.data;
            uwc.isTopmost = data != null ? data.isTopmost : true;
        }
    }

    Rect GetMonitorForWindow(Vector2 winPos, Vector2 winSize)
    {
        float cx = winPos.x + winSize.x * 0.5f;
        float cy = winPos.y + winSize.y * 0.5f;

        int count = UniWindowController.GetMonitorCount();
        Rect best = new Rect(0, 0, 1920, 1080); // fallback
        float bestDist = float.MaxValue;

        for (int i = 0; i < count; i++)
        {
            Rect r = UniWindowController.GetMonitorRect(i);
            // Check if center is inside this monitor
            if (cx >= r.x && cx < r.x + r.width && cy >= r.y && cy < r.y + r.height)
                return r;

            // Otherwise track closest
            float dx = Mathf.Max(0, Mathf.Max(r.x - cx, cx - (r.x + r.width)));
            float dy = Mathf.Max(0, Mathf.Max(r.y - cy, cy - (r.y + r.height)));
            float dist = dx * dx + dy * dy;
            if (dist < bestDist)
            {
                bestDist = dist;
                best = r;
            }
        }

        return best;
    }
}
