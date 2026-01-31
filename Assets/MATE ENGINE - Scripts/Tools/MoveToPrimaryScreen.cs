using UnityEngine;
using Kirurobo;

public class MoveToPrimaryScreen : MonoBehaviour
{
    public void MoveToPrimary()
    {
        var uwc = UniWindowController.current;
        if (uwc == null)
        {
            Debug.Log("[MoveToPrimaryScreen] UniWindowController not available.");
            return;
        }

        if (UniWindowController.GetMonitorCount() <= 0)
        {
            Debug.Log("[MoveToPrimaryScreen] No monitors detected.");
            return;
        }

        Rect primaryRect = UniWindowController.GetMonitorRect(0);
        Vector2 winSize = uwc.windowSize;

        float x = primaryRect.x + (primaryRect.width - winSize.x) * 0.5f;
        float y = primaryRect.y + (primaryRect.height - winSize.y) * 0.5f;

        uwc.windowPosition = new Vector2(x, y);
        Debug.Log($"[MoveToPrimaryScreen] Moved to primary monitor: ({x}, {y})");
    }
}
