using System;
using System.Collections.Generic;
using UnityEngine;

namespace Utils
{
    public static partial class TrayIcon
    {
        private static bool _init = false;

        private static Dictionary<string, Action> MenuActions;
        private static Dictionary<int, string> ItemIdToLabel;
        private static Action OnLeftClick;

        public static Func<List<(string, Action)>> OnBuildMenu;

        // Must be kept alive to prevent GC of native callback
        private static MenuClickDelegate _menuCallbackDelegate;
        private static StatusBarClickDelegate _clickCallbackDelegate;

        /// <summary>Create a menu bar status icon (macOS NSStatusBar)</summary>
        public static void Init(string appName, string tooltip, Texture2D iconTexture, List<(string, Action)> actions = null)
        {
            if (_init)
            {
                Debug.LogError("Init can only be called once...");
                return;
            }

            if (string.IsNullOrEmpty(tooltip))
            {
                Debug.LogError("A description when hovered is required...");
                return;
            }

            ProcessMenuActions(actions);

            try
            {
                // Initialize the native status bar
                NativePlugin.MacStatusBar_Init(tooltip);

                // Set icon from texture
                if (iconTexture != null && iconTexture.isReadable)
                {
                    SetIconFromTexture(iconTexture);
                }

                // Register native callbacks (prevent GC)
                _menuCallbackDelegate = OnNativeMenuClick;
                _clickCallbackDelegate = OnNativeStatusBarClick;
                NativePlugin.MacStatusBar_RegisterMenuCallback(_menuCallbackDelegate);
                NativePlugin.MacStatusBar_RegisterClickCallback(_clickCallbackDelegate);

                _init = true;
                Application.quitting += CleanupResources;

#if UNITY_EDITOR
                Debug.Log("Successfully added macOS Status Bar Icon");
#endif
            }
            catch (Exception e)
            {
                Debug.LogError($"Failed to initialize macOS status bar: {e.Message}");
            }
        }

        private static void SetIconFromTexture(Texture2D texture)
        {
            int width = texture.width;
            int height = texture.height;
            Color32[] pixels = texture.GetPixels32();

            // Convert to RGBA byte array (flip vertically for macOS coordinate system)
            byte[] rgba = new byte[width * height * 4];
            for (int y = 0; y < height; y++)
            {
                int srcRow = (height - 1 - y);
                for (int x = 0; x < width; x++)
                {
                    Color32 p = pixels[srcRow * width + x];
                    int idx = (y * width + x) * 4;
                    rgba[idx + 0] = p.r;
                    rgba[idx + 1] = p.g;
                    rgba[idx + 2] = p.b;
                    rgba[idx + 3] = p.a;
                }
            }

            NativePlugin.MacStatusBar_SetIcon(rgba, width, height);
        }

        [AOT.MonoPInvokeCallback(typeof(MenuClickDelegate))]
        private static void OnNativeMenuClick(int itemId)
        {
            if (ItemIdToLabel != null && ItemIdToLabel.TryGetValue(itemId, out string label))
            {
                if (MenuActions != null && MenuActions.TryGetValue(label, out Action callback))
                {
                    callback?.Invoke();
                }
            }
        }

        [AOT.MonoPInvokeCallback(typeof(StatusBarClickDelegate))]
        private static void OnNativeStatusBarClick(int clickType)
        {
            if (clickType == 0) // left click
            {
                if (OnLeftClick != null)
                {
                    OnLeftClick.Invoke();
                }
                else
                {
                    // Default: show context menu on left click too
                    ShowContextMenu();
                }
            }
            else // right click
            {
                ShowContextMenu();
            }
        }

        private static void ShowContextMenu()
        {
            var menuEntries = OnBuildMenu != null ? OnBuildMenu() : null;

            MenuActions = new Dictionary<string, Action>();
            ItemIdToLabel = new Dictionary<int, string>();

            NativePlugin.MacStatusBar_ClearMenu();

            int itemId = 1000;
            if (menuEntries != null)
            {
                foreach (var entry in menuEntries)
                {
                    if (entry.Item1 == SEPARATOR)
                    {
                        NativePlugin.MacStatusBar_AddSeparator();
                    }
                    else if (entry.Item1 == LEFT_CLICK)
                    {
                        OnLeftClick = entry.Item2;
                    }
                    else
                    {
                        NativePlugin.MacStatusBar_AddMenuItem(entry.Item1, itemId);
                        MenuActions[entry.Item1] = entry.Item2;
                        ItemIdToLabel[itemId] = entry.Item1;
                        itemId++;
                    }
                }
            }

            NativePlugin.MacStatusBar_ShowMenu();
        }

        private static void CleanupResources()
        {
            if (_init)
            {
                NativePlugin.MacStatusBar_Destroy();
                _init = false;
            }

            _menuCallbackDelegate = null;
            _clickCallbackDelegate = null;

#if UNITY_EDITOR
            Debug.Log("Cleaned up macOS Status Bar Icon");
#endif
        }

        // --- Dock Visibility ---
        public static void SetDockVisible(bool visible)
        {
            NativePlugin.MacStatusBar_SetDockVisible(visible ? 1 : 0);
        }

        public static bool IsDockVisible()
        {
            return NativePlugin.MacStatusBar_IsDockVisible() != 0;
        }
    }
}
