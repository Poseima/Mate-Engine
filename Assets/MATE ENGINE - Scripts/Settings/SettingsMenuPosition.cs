using UnityEngine;
using System;
using System.Collections.Generic;
using Kirurobo;

public class SettingsMenuPosition : MonoBehaviour
{
    [Serializable]
    public class MenuEntry
    {
        public RectTransform settingsMenu;
        [HideInInspector] public float originalX;
        [HideInInspector] public float originalY;
        [HideInInspector] public Vector3 originalScale;
        [HideInInspector] public Vector2 lastApplied;
    }

    [Header("Menus to track")]
    public List<MenuEntry> menus = new List<MenuEntry>();

    [Header("Edge margin in Pixels")]
    public float edgeMargin = 50f;

    [Header("Scale with window")]
    public float referenceHeight = 1080f;
    public float minScale = 0.4f;
    public float maxScale = 1.2f;

    [Header("Checks per second")]
    public float checkFPS = 20f;

    [Header("Monitor refresh (sec)")]
    public float monitorRefreshInterval = 2f;

    private float nextCheck;
    private float nextMonitorRefresh;
    private Rect currentMonitorRect;

    void Start()
    {
        foreach (var menu in menus)
        {
            if (!menu.settingsMenu) continue;
            menu.originalX = menu.settingsMenu.anchoredPosition.x;
            menu.originalY = menu.settingsMenu.anchoredPosition.y;
            menu.originalScale = menu.settingsMenu.localScale;
            menu.lastApplied = menu.settingsMenu.anchoredPosition;
        }
    }

    void Update()
    {
        if (Time.time < nextCheck) return;
        nextCheck = Time.time + 1f / checkFPS;

        if (!UniWindowController.current) return;

        Vector2 winPos = UniWindowController.current.windowPosition;
        Vector2 winSize = UniWindowController.current.windowSize;

        // Periodically refresh which monitor the window is on
        if (Time.time >= nextMonitorRefresh)
        {
            nextMonitorRefresh = Time.time + monitorRefreshInterval;
            currentMonitorRect = FindMonitorForWindow(winPos, winSize);
        }

        // If we never found a valid monitor, skip
        if (currentMonitorRect.width <= 0) return;

        float scaleFactor = Mathf.Clamp(winSize.y / referenceHeight, minScale, maxScale);

        float winLeft = winPos.x;
        float winRight = winPos.x + winSize.x;
        float monLeft = currentMonitorRect.x;
        float monRight = currentMonitorRect.x + currentMonitorRect.width;

        bool nearRightEdge = (monRight - winRight) < edgeMargin;
        bool nearLeftEdge = (winLeft - monLeft) < edgeMargin;

        foreach (var menu in menus)
        {
            if (!menu.settingsMenu) continue;

            float targetX;

            if (nearRightEdge)
            {
                // Window near right edge — flip menu to the left side
                targetX = -Mathf.Abs(menu.originalX);
            }
            else if (nearLeftEdge)
            {
                // Window near left edge — flip menu to the right side
                targetX = Mathf.Abs(menu.originalX);
            }
            else
            {
                targetX = menu.originalX;
            }

            Vector2 newPos = new Vector2(targetX, menu.originalY) * scaleFactor;
            menu.settingsMenu.localScale = menu.originalScale * scaleFactor;

            if (newPos != menu.lastApplied)
            {
                menu.settingsMenu.anchoredPosition = newPos;
                menu.lastApplied = newPos;
            }
        }
    }

    /// <summary>
    /// Find which monitor contains the center of the window.
    /// Falls back to the first monitor if none match.
    /// </summary>
    private Rect FindMonitorForWindow(Vector2 winPos, Vector2 winSize)
    {
        int monitorCount = UniWindowController.GetMonitorCount();
        if (monitorCount <= 0) return new Rect();

        Vector2 windowCenter = winPos + winSize * 0.5f;

        for (int i = 0; i < monitorCount; i++)
        {
            Rect monRect = UniWindowController.GetMonitorRect(i);
            if (windowCenter.x >= monRect.x && windowCenter.x < monRect.x + monRect.width &&
                windowCenter.y >= monRect.y && windowCenter.y < monRect.y + monRect.height)
            {
                return monRect;
            }
        }

        // Fallback to primary monitor
        return UniWindowController.GetMonitorRect(0);
    }
}
