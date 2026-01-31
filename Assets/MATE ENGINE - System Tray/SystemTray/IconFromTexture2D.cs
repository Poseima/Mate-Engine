using System;
using UnityEngine;

namespace Utils
{
    public static partial class TrayIcon
    {
        // Icon creation is now handled natively by MacStatusBar_SetIcon.
        // This partial class is kept for API compatibility.

        /// <summary>Updates the status bar icon from a Texture2D at runtime</summary>
        public static void UpdateIcon(Texture2D texture)
        {
            if (!_init || texture == null || !texture.isReadable) return;
            SetIconFromTexture(texture);
        }
    }
}
