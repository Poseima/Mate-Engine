using System;
using System.Collections.Generic;

namespace Utils
{
    public static partial class TrayIcon
    {
        private static void ProcessMenuActions(List<(string, Action)> actions)
        {
            MenuActions = new Dictionary<string, Action>();
            ItemIdToLabel = new Dictionary<int, string>();

            if (actions == null)
                return;

            foreach (var (label, callback) in actions)
            {
                if (label == LEFT_CLICK)
                {
                    OnLeftClick = callback;
                    continue;
                }

                MenuActions[label] = callback;
            }
        }

        private static string TruncateString(string str, int maxLength)
        {
            if (string.IsNullOrEmpty(str)) return "";
            return str.Length < maxLength ? str : str.Substring(0, maxLength - 1);
        }
    }
}
