using System;
using UnityEngine;
using Kirurobo;

public static class MonitorHelper
{
    /// <summary>
    /// Returns the taskbar Rect for the monitor containing the given window.
    /// macOS has no Windows-style taskbar; returns an empty rect.
    /// </summary>
    public static Rect GetTaskbarRectForWindow(IntPtr windowHandle)
    {
        return new Rect(0, 0, 0, 0);
    }

    /// <summary>
    /// Returns the scale factor for the monitor containing the given window.
    /// Uses Screen.dpi as an approximation. On Retina displays this returns 2.0.
    /// </summary>
    public static float GetScaleForWindow(IntPtr windowHandle)
    {
        float dpi = Screen.dpi;
        return dpi > 0f ? dpi / 96f : 1f;
    }

    /// <summary>
    /// Returns the Rect of the monitor that contains the center of the current window.
    /// Falls back to monitor 0 if no match is found.
    /// </summary>
    public static Rect GetCurrentMonitorRect()
    {
        var uwc = UniWindowController.current;
        if (uwc == null) return new Rect(0, 0, Screen.width, Screen.height);

        Vector2 winPos = uwc.windowPosition;
        Vector2 winSize = uwc.windowSize;
        float cx = winPos.x + winSize.x * 0.5f;
        float cy = winPos.y + winSize.y * 0.5f;

        int count = UniWindowController.GetMonitorCount();
        for (int i = 0; i < count; i++)
        {
            Rect mr = UniWindowController.GetMonitorRect(i);
            if (cx >= mr.x && cx < mr.x + mr.width && cy >= mr.y && cy < mr.y + mr.height)
                return mr;
        }

        if (count > 0) return UniWindowController.GetMonitorRect(0);
        return new Rect(0, 0, Screen.width, Screen.height);
    }
}
