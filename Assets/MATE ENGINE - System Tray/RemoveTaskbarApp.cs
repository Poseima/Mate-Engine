using UnityEngine;

public class RemoveTaskbarApp : MonoBehaviour
{
    private bool _isHidden = true;
    public bool IsHidden => _isHidden;

    void Start()
    {
        // Start with Dock icon hidden (accessory app)
        if (_isHidden)
            Utils.TrayIcon.SetDockVisible(false);
    }

    public void ToggleAppMode()
    {
        _isHidden = !_isHidden;

        if (_isHidden)
        {
            Utils.TrayIcon.SetDockVisible(false);
            Debug.Log("[RemoveTaskbarApp] Dock icon hidden (Accessory mode)");
        }
        else
        {
            Utils.TrayIcon.SetDockVisible(true);
            Debug.Log("[RemoveTaskbarApp] Dock icon visible (Regular mode)");
        }
    }
}
