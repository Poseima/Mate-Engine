using UnityEngine;
using UnityEngine.UI;
using TMPro;
using MateEngine.Codex;
using System.Collections.Generic;

public class SettingsHandlerCodex : MonoBehaviour
{
    [Header("Auth")]
    public Button loginButton;
    public TextMeshProUGUI statusText;

    [Header("Model")]
    public TMP_Dropdown modelDropdown;

    [Header("Provider")]
    public TMP_Dropdown providerDropdown;

    [Header("Options")]
    public Toggle enableAnimDirectivesToggle;

    [Header("Conversation")]
    public Button newConversationButton;
    public Button newChatButton;

    private List<ModelInfo> cachedModels = new();
    private List<ProviderInfo> cachedProviders = new();
    private Button runtimeNewChatButton;
    private bool subscribedToBridge;

    void Start()
    {
        // Clone login button to create New Chat button at runtime (same style, different label/action)
        if (loginButton != null && newChatButton == null)
        {
            CreateNewChatButton();
        }

        loginButton?.onClick.AddListener(OnLoginClicked);
        newConversationButton?.onClick.AddListener(OnNewConversationClicked);
        newChatButton?.onClick.AddListener(OnNewConversationClicked);
        runtimeNewChatButton?.onClick.AddListener(OnNewConversationClicked);
        modelDropdown?.onValueChanged.AddListener(OnModelChanged);
        providerDropdown?.onValueChanged.AddListener(OnProviderChanged);
        enableAnimDirectivesToggle?.onValueChanged.AddListener(OnAnimDirectivesChanged);

        TrySubscribeToBridge();

        LoadSettings();
        UpdateStatus();
    }

    void OnEnable()
    {
        TrySubscribeToBridge();
        UpdateStatus();
    }

    void TrySubscribeToBridge()
    {
        if (subscribedToBridge || CodexBridge.Instance == null) return;
        CodexBridge.Instance.OnLoginResult += OnLoginResult;
        CodexBridge.Instance.OnModelsLoaded += OnModelsLoaded;
        CodexBridge.Instance.OnProvidersLoaded += OnProvidersLoaded;
        CodexBridge.Instance.OnConnected += OnConnected;
        subscribedToBridge = true;
    }

    void OnDestroy()
    {
        if (CodexBridge.Instance != null && subscribedToBridge)
        {
            CodexBridge.Instance.OnLoginResult -= OnLoginResult;
            CodexBridge.Instance.OnModelsLoaded -= OnModelsLoaded;
            CodexBridge.Instance.OnProvidersLoaded -= OnProvidersLoaded;
            CodexBridge.Instance.OnConnected -= OnConnected;
        }
    }

    // ── New Chat Button (runtime clone of Login button) ──────────

    void CreateNewChatButton()
    {
        var clone = Instantiate(loginButton.gameObject, loginButton.transform.parent);
        clone.name = "NewChatButton";

        // Position next to the login button
        var srcRect = loginButton.GetComponent<RectTransform>();
        var cloneRect = clone.GetComponent<RectTransform>();
        var pos = srcRect.anchoredPosition;
        pos.x += srcRect.sizeDelta.x + 20f;
        cloneRect.anchoredPosition = pos;
        cloneRect.sizeDelta = srcRect.sizeDelta;

        // Change label
        var label = clone.GetComponentInChildren<TextMeshProUGUI>();
        if (label != null) label.text = "New Chat";

        runtimeNewChatButton = clone.GetComponent<Button>();
        runtimeNewChatButton.onClick.RemoveAllListeners();
    }

    // ── Login ──────────────────────────────────────────────────

    void OnLoginClicked()
    {
        var bridge = CodexBridge.Instance;
        if (bridge == null)
        {
            UpdateStatus("CodexBridge not found");
            return;
        }

        if (bridge.IsAuthenticated)
        {
            UpdateStatus("Already authenticated");
            return;
        }

        if (!bridge.IsConnected)
        {
            UpdateStatus("Connecting...");
            bridge.OnConnected += OnConnectedThenLogin;
            bridge.Initialize();
            return;
        }

        UpdateStatus("Opening browser...");
        bridge.AuthenticateOAuth();
    }

    void OnConnectedThenLogin()
    {
        var bridge = CodexBridge.Instance;
        if (bridge != null)
        {
            bridge.OnConnected -= OnConnectedThenLogin;
            if (!bridge.IsAuthenticated)
            {
                UpdateStatus("Opening browser...");
                bridge.AuthenticateOAuth();
            }
            else
            {
                UpdateStatus("Authenticated");
            }
        }
    }

    void OnLoginResult(bool success, string error)
    {
        if (success)
        {
            UpdateStatus("Authenticated");
            CodexBridge.Instance?.ListModels();
            CodexBridge.Instance?.ListProviders();
        }
        else
        {
            UpdateStatus("Login failed: " + (error ?? "unknown"));
        }
    }

    void OnConnected()
    {
        UpdateStatus("Connected");
    }

    // ── Models ─────────────────────────────────────────────────

    void OnModelsLoaded(List<ModelInfo> models)
    {
        cachedModels = models ?? new List<ModelInfo>();

        if (modelDropdown == null) return;

        modelDropdown.ClearOptions();
        var options = new List<string>();
        int selectedIndex = 0;
        string savedModel = SaveLoadHandler.Instance?.data?.codexModel ?? "";

        for (int i = 0; i < cachedModels.Count; i++)
        {
            var m = cachedModels[i];
            string label = !string.IsNullOrEmpty(m.displayName) ? m.displayName : m.id;
            options.Add(label);
            if (m.id == savedModel || m.model == savedModel)
                selectedIndex = i;
        }

        modelDropdown.AddOptions(options);
        if (options.Count > 0)
            modelDropdown.SetValueWithoutNotify(selectedIndex);
    }

    void OnModelChanged(int index)
    {
        if (index < 0 || index >= cachedModels.Count) return;
        SaveLoadHandler.Instance.data.codexModel = cachedModels[index].id;
        Save();
    }

    // ── Providers ─────────────────────────────────────────────

    void OnProvidersLoaded(List<ProviderInfo> providers)
    {
        cachedProviders = providers ?? new List<ProviderInfo>();

        if (providerDropdown == null) return;

        providerDropdown.ClearOptions();
        var options = new List<string>();
        int selectedIndex = 0;
        string savedProvider = SaveLoadHandler.Instance?.data?.codexProvider ?? "";

        for (int i = 0; i < cachedProviders.Count; i++)
        {
            var p = cachedProviders[i];
            string label = !string.IsNullOrEmpty(p.name) ? p.name : p.id;
            options.Add(label);
            if (p.id == savedProvider)
                selectedIndex = i;
        }

        providerDropdown.AddOptions(options);
        if (options.Count > 0)
            providerDropdown.SetValueWithoutNotify(selectedIndex);
    }

    void OnProviderChanged(int index)
    {
        if (index < 0 || index >= cachedProviders.Count) return;
        SaveLoadHandler.Instance.data.codexProvider = cachedProviders[index].id;
        Save();
    }

    // ── Animation Directives ───────────────────────────────────

    void OnAnimDirectivesChanged(bool value)
    {
        SaveLoadHandler.Instance.data.enableAnimationDirectives = value;
        Save();
    }

    // ── New Conversation ───────────────────────────────────────

    void OnNewConversationClicked()
    {
        SaveLoadHandler.Instance.data.codexThreadId = "";
        Save();

        var bridge = CodexBridge.Instance;
        if (bridge != null && bridge.IsConnected && bridge.IsAuthenticated)
        {
            string model = SaveLoadHandler.Instance.data.codexModel ?? "";
            bool animDirectives = SaveLoadHandler.Instance.data.enableAnimationDirectives;
            string systemPrompt = bridge.BuildSystemPrompt("", animDirectives);
            bridge.StartThread(model, systemPrompt, (threadId) =>
            {
                SaveLoadHandler.Instance.data.codexThreadId = threadId;
                Save();
                UpdateStatus("New conversation started");
            });
        }
    }

    // ── Load / Apply / Reset ───────────────────────────────────

    public void LoadSettings()
    {
        var data = SaveLoadHandler.Instance?.data;
        if (data == null) return;

        enableAnimDirectivesToggle?.SetIsOnWithoutNotify(data.enableAnimationDirectives);
    }

    public void ApplySettings()
    {
        var data = SaveLoadHandler.Instance?.data;
        if (data == null) return;

        data.enableAnimationDirectives = enableAnimDirectivesToggle?.isOn ?? data.enableAnimationDirectives;
        Save();
    }

    public void ResetToDefaults()
    {
        enableAnimDirectivesToggle?.SetIsOnWithoutNotify(false);
        SaveLoadHandler.Instance.data.enableAnimationDirectives = false;
        SaveLoadHandler.Instance.data.codexModel = "";
        SaveLoadHandler.Instance.data.codexProvider = "";
        SaveLoadHandler.Instance.data.codexThreadId = "";
        Save();
        UpdateStatus();
    }

    // ── Helpers ────────────────────────────────────────────────

    void UpdateStatus(string message = null)
    {
        if (statusText == null) return;

        if (message != null)
        {
            statusText.text = message;
            return;
        }

        var bridge = CodexBridge.Instance;
        if (bridge == null)
            statusText.text = "Not initialized";
        else if (bridge.IsAuthenticated)
            statusText.text = "Authenticated";
        else if (bridge.IsConnected)
            statusText.text = "Connected (not logged in)";
        else
            statusText.text = "Not connected";
    }

    void Save()
    {
        SaveLoadHandler.Instance.SaveToDisk();
    }
}
