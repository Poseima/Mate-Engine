using System;
using System.IO;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Debug = UnityEngine.Debug;

public class SystemStartHandler : MonoBehaviour
{
    [Header("UI (Optional)")]
    public Toggle autoStartToggle;
    public TMP_Text checkmarkText;

    [Header("Settings")]
    public string runKeyName = "MateEngine";
    public string commandLineArgs = "";

    private bool _isApplyingUI;
    private string _plistPath;
    private string _plistLabel;

    private void Awake()
    {
        if (SaveLoadHandler.Instance == null)
        {
            Debug.LogError("[SystemStartHandler] SaveLoadHandler.Instance is null. Place SaveLoadHandler in the scene first.");
            enabled = false;
            return;
        }

        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        _plistLabel = "com.mateengine." + runKeyName.ToLowerInvariant();
        _plistPath = Path.Combine(home, "Library", "LaunchAgents", _plistLabel + ".plist");
    }

    private void Start()
    {
        if (autoStartToggle != null)
            autoStartToggle.onValueChanged.AddListener(OnUIToggleChanged);

        LoadFromSaveWithoutNotify();
        TryApplyAutostart(SaveLoadHandler.Instance.data.startWithWindows);
    }

    private void OnDestroy()
    {
        if (autoStartToggle != null)
            autoStartToggle.onValueChanged.RemoveListener(OnUIToggleChanged);
    }

    private void OnUIToggleChanged(bool isOn)
    {
        if (_isApplyingUI) return;

        SaveLoadHandler.Instance.data.startWithWindows = isOn;
        SaveLoadHandler.Instance.SaveToDisk();

        TryApplyAutostart(isOn);
        UpdateCheckmarkText(isOn);
    }

    public void OnCheckmarkClicked()
    {
        bool newState = !GetSavedState();
        SetStateFromCode(newState);
    }

    public void SetStateFromCode(bool isOn)
    {
        SaveLoadHandler.Instance.data.startWithWindows = isOn;
        SaveLoadHandler.Instance.SaveToDisk();
        TryApplyAutostart(isOn);
        ApplyToUIWithoutNotify(isOn);
    }

    private void LoadFromSaveWithoutNotify()
    {
        ApplyToUIWithoutNotify(GetSavedState());
    }

    private bool GetSavedState()
    {
        return SaveLoadHandler.Instance.data != null && SaveLoadHandler.Instance.data.startWithWindows;
    }

    private void ApplyToUIWithoutNotify(bool isOn)
    {
        _isApplyingUI = true;
        try
        {
            if (autoStartToggle != null)
                autoStartToggle.SetIsOnWithoutNotify(isOn);
            UpdateCheckmarkText(isOn);
        }
        finally
        {
            _isApplyingUI = false;
        }
    }

    private void UpdateCheckmarkText(bool isOn)
    {
        if (checkmarkText != null)
            checkmarkText.text = isOn ? "☑ Start at Login" : "☐ Start at Login";
    }

    private void TryApplyAutostart(bool enable)
    {
        if (string.IsNullOrEmpty(_plistPath))
        {
            Debug.LogWarning("[SystemStartHandler] Plist path not initialized.");
            return;
        }

        try
        {
            if (enable)
            {
                WriteLaunchAgentPlist();
                Debug.Log($"[SystemStartHandler] Launch Agent created: {_plistPath}");
            }
            else
            {
                RemoveLaunchAgentPlist();
                Debug.Log($"[SystemStartHandler] Launch Agent removed: {_plistPath}");
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"[SystemStartHandler] Failed to {(enable ? "create" : "remove")} Launch Agent: {e.Message}");
        }
    }

    private void WriteLaunchAgentPlist()
    {
        string appPath = GetAppBundlePath();
        if (string.IsNullOrEmpty(appPath))
        {
            Debug.LogWarning("[SystemStartHandler] Could not determine .app bundle path.");
            return;
        }

        // Ensure LaunchAgents directory exists
        string dir = Path.GetDirectoryName(_plistPath);
        if (!Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        string argsXml = "";
        if (!string.IsNullOrEmpty(commandLineArgs))
        {
            string[] parts = commandLineArgs.Split(' ');
            foreach (string part in parts)
            {
                if (!string.IsNullOrWhiteSpace(part))
                    argsXml += $"\n        <string>{EscapeXml(part.Trim())}</string>";
            }
        }

        string plist = $@"<?xml version=""1.0"" encoding=""UTF-8""?>
<!DOCTYPE plist PUBLIC ""-//Apple//DTD PLIST 1.0//EN"" ""http://www.apple.com/DTDs/PropertyList-1.0.dtd"">
<plist version=""1.0"">
<dict>
    <key>Label</key>
    <string>{EscapeXml(_plistLabel)}</string>
    <key>ProgramArguments</key>
    <array>
        <string>/usr/bin/open</string>
        <string>{EscapeXml(appPath)}</string>{argsXml}
    </array>
    <key>RunAtLoad</key>
    <true/>
</dict>
</plist>";

        File.WriteAllText(_plistPath, plist);
    }

    private void RemoveLaunchAgentPlist()
    {
        if (File.Exists(_plistPath))
            File.Delete(_plistPath);
    }

    private string GetAppBundlePath()
    {
        // Application.dataPath on macOS: /path/to/App.app/Contents/Data
        // Go up two levels to get the .app bundle
        string dataPath = Application.dataPath;
        string appPath = Path.GetFullPath(Path.Combine(dataPath, "../.."));

        if (appPath.EndsWith(".app"))
            return appPath;

        // In editor, dataPath is the Assets folder — no .app bundle
        Debug.LogWarning("[SystemStartHandler] Not running from an .app bundle.");
        return null;
    }

    private static string EscapeXml(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        return s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;")
                .Replace("\"", "&quot;").Replace("'", "&apos;");
    }
}
