using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;
using System.Linq;

public class AllowedAppsManager : MonoBehaviour
{
    public TMP_Dropdown runningAppsDropdown;
    public Button addToAllowedListButton;
    public Transform allowedAppsListContent;
    public GameObject allowedAppItemPrefab;

    private List<string> currentRunningAppNames = new List<string>();
    private List<string> allowedApps => SaveLoadHandler.Instance.data.allowedApps;

    private void Start()
    {
        addToAllowedListButton.onClick.AddListener(() =>
        {
            if (runningAppsDropdown.options.Count == 0) return;

            string selectedApp = runningAppsDropdown.options[runningAppsDropdown.value].text;
            if (!allowedApps.Contains(selectedApp))
            {
                allowedApps.Add(selectedApp);
                UpdateAllowedListUI();
                RefreshRunningAppsDropdown();
                SaveLoadHandler.Instance.SaveToDisk();
                SaveLoadHandler.SyncAllowedAppsToAllAvatars();
            }

        });

        RefreshRunningAppsDropdown();
        UpdateAllowedListUI();
        SaveLoadHandler.SyncAllowedAppsToAllAvatars();
    }

    private void RefreshRunningAppsDropdown()
    {
        currentRunningAppNames = GetRunningAudioAppNames();

        var filteredAppNames = currentRunningAppNames
            .Where(app => !allowedApps.Contains(app))
            .OrderBy(app => app)
            .ToList();

        runningAppsDropdown.ClearOptions();
        runningAppsDropdown.AddOptions(
            filteredAppNames.Select(app => new TMP_Dropdown.OptionData(app)).ToList()
        );

        if (filteredAppNames.Count == 0)
            runningAppsDropdown.value = 0;
    }

    public void OnDropdownOpened()
    {
        RefreshRunningAppsDropdown();
    }

    private void UpdateAllowedListUI()
    {
        foreach (Transform child in allowedAppsListContent)
            Destroy(child.gameObject);

        foreach (var app in allowedApps)
        {
            var item = Instantiate(allowedAppItemPrefab, allowedAppsListContent);

            var label = item.GetComponentsInChildren<TextMeshProUGUI>()
                            .FirstOrDefault(t => t.transform.parent == item.transform);
            if (label != null) label.text = app;

            var button = item.transform.Find("Button")?.GetComponent<Button>();
            if (button != null)
            {
                button.onClick.AddListener(() =>
                {
                    allowedApps.Remove(app);
                    UpdateAllowedListUI();
                    SaveLoadHandler.Instance.SaveToDisk();
                    SaveLoadHandler.SyncAllowedAppsToAllAvatars();
                });
            }
        }
    }

    private List<string> GetRunningAudioAppNames()
    {
        // Audio session enumeration is not available on macOS (was NAudio/WASAPI).
        // Return empty list — the dropdown will show no running audio apps.
        return new List<string>();
    }

    public void RefreshAppListOnMenuOpen()
    {
        RefreshRunningAppsDropdown();
        UpdateAllowedListUI();
        SaveLoadHandler.SyncAllowedAppsToAllAvatars();
    }

    public void RefreshUI()
    {
        RefreshRunningAppsDropdown();
        UpdateAllowedListUI();
        SaveLoadHandler.SyncAllowedAppsToAllAvatars();
    }
}
