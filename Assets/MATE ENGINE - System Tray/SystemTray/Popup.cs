using System;
using UnityEngine;

namespace Utils
{
    public static partial class TrayIcon
    {
        public enum ToolTipIcon
        {
            None,
            Info,
            Warning,
            Error
        }

        /// <summary>Displays a notification from the menu bar icon</summary>
        public static void ShowBalloonTip(string title, string message, ToolTipIcon iconType, bool useSound = true)
        {
            if (!_init)
            {
                Debug.LogError("TrayIcon is not initialized yet...");
                return;
            }

            try
            {
                NativePlugin.MacStatusBar_ShowNotification(
                    TruncateString(title, 64),
                    TruncateString(message, 256)
                );
            }
            catch (Exception e)
            {
                Debug.LogError($"Failed to show notification: {e.Message}");
            }
        }
    }
}
