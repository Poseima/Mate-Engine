using System;
using System.Runtime.InteropServices;

namespace Utils
{
    public static partial class TrayIcon
    {
        private static class NativePlugin
        {
            [DllImport("MacStatusBar")]
            public static extern void MacStatusBar_Init(string tooltip);

            [DllImport("MacStatusBar")]
            public static extern void MacStatusBar_SetIcon(byte[] rgbaPixels, int width, int height);

            [DllImport("MacStatusBar")]
            public static extern void MacStatusBar_SetTooltip(string tooltip);

            [DllImport("MacStatusBar")]
            public static extern void MacStatusBar_RegisterMenuCallback(MenuClickDelegate callback);

            [DllImport("MacStatusBar")]
            public static extern void MacStatusBar_RegisterClickCallback(StatusBarClickDelegate callback);

            [DllImport("MacStatusBar")]
            public static extern void MacStatusBar_ClearMenu();

            [DllImport("MacStatusBar")]
            public static extern void MacStatusBar_AddMenuItem(string label, int itemId);

            [DllImport("MacStatusBar")]
            public static extern void MacStatusBar_AddSeparator();

            [DllImport("MacStatusBar")]
            public static extern void MacStatusBar_ShowMenu();

            [DllImport("MacStatusBar")]
            public static extern void MacStatusBar_Destroy();

            [DllImport("MacStatusBar")]
            public static extern void MacStatusBar_SetDockVisible(int visible);

            [DllImport("MacStatusBar")]
            public static extern int MacStatusBar_IsDockVisible();

            [DllImport("MacStatusBar")]
            public static extern void MacStatusBar_ShowNotification(string title, string message);
        }

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void MenuClickDelegate(int itemId);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void StatusBarClickDelegate(int clickType);
    }
}
