using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using MateEngine.Codex;

public static class CodexSetupMenu
{
    [MenuItem("Mate Engine/Setup WhatsApp IPC Bridge", false, 99)]
    static void SetupWhatsAppIPCBridge()
    {
        // Check if already exists
        var existing = Object.FindFirstObjectByType<WhatsAppIPCBridge>(FindObjectsInactive.Include);
        if (existing != null)
        {
            EditorUtility.DisplayDialog("WhatsApp IPC Bridge", "WhatsAppIPCBridge already exists on: " + existing.gameObject.name, "OK");
            Selection.activeGameObject = existing.gameObject;
            return;
        }

        // Find VRMModel (the avatar GameObject)
        GameObject avatar = null;
        foreach (var go in Object.FindObjectsByType<GameObject>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (go.name == "VRMModel" && go.GetComponent<Animator>() != null)
            {
                avatar = go;
                break;
            }
        }

        if (avatar == null)
        {
            EditorUtility.DisplayDialog("WhatsApp IPC Bridge", "Cannot find 'VRMModel' GameObject with Animator.\nOpen the main scene first.", "OK");
            return;
        }

        var bridge = Undo.AddComponent<WhatsAppIPCBridge>(avatar);
        Selection.activeGameObject = avatar;

        Debug.Log("[Codex Setup] WhatsAppIPCBridge added to " + avatar.name);
        EditorUtility.DisplayDialog("WhatsApp IPC Bridge",
            "WhatsAppIPCBridge added to '" + avatar.name + "'.\n\n" +
            "IPC directory: ~/.mate-engine/ipc/whatsapp/\n" +
            "Enable/disable via the Inspector toggle.", "OK");
    }

    [MenuItem("Mate Engine/Setup Codex Bridge", false, 100)]
    static void SetupCodexBridge()
    {
        if (Object.FindFirstObjectByType<CodexBridge>() != null)
        {
            EditorUtility.DisplayDialog("Codex Bridge", "CodexBridge already exists in the scene.", "OK");
            Selection.activeGameObject = Object.FindFirstObjectByType<CodexBridge>().gameObject;
            return;
        }

        var go = new GameObject("CodexBridge");
        go.AddComponent<CodexBridge>();
        Undo.RegisterCreatedObjectUndo(go, "Create CodexBridge");
        Selection.activeGameObject = go;

        Debug.Log("[Codex Setup] CodexBridge GameObject created. It will persist across scenes via DontDestroyOnLoad.");
        EditorUtility.DisplayDialog("Codex Bridge", "CodexBridge created successfully.\n\nUse OAuth at runtime or set CODEX_API_KEY env variable.", "OK");
    }

    [MenuItem("Mate Engine/Create Semantic Clip Registry", false, 101)]
    static void CreateClipRegistry()
    {
        string path = "Assets/Resources/SemanticClipRegistry.asset";

        if (AssetDatabase.LoadAssetAtPath<SemanticClipRegistry>(path) != null)
        {
            EditorUtility.DisplayDialog("Clip Registry", "SemanticClipRegistry already exists at:\n" + path, "OK");
            Selection.activeObject = AssetDatabase.LoadAssetAtPath<SemanticClipRegistry>(path);
            return;
        }

        if (!AssetDatabase.IsValidFolder("Assets/Resources"))
            AssetDatabase.CreateFolder("Assets", "Resources");

        var asset = ScriptableObject.CreateInstance<SemanticClipRegistry>();
        AssetDatabase.CreateAsset(asset, path);
        AssetDatabase.SaveAssets();
        Selection.activeObject = asset;

        Debug.Log("[Codex Setup] SemanticClipRegistry created at " + path);
        EditorUtility.DisplayDialog("Clip Registry", "SemanticClipRegistry created at:\n" + path, "OK");
    }

    [MenuItem("Mate Engine/Duplicate Steam DLC Card Below Codex", false, 103)]
    static void DuplicateSteamDLCCard()
    {
        var buttonsHandler = Object.FindFirstObjectByType<SettingsHandlerButtons>(FindObjectsInactive.Include);
        if (buttonsHandler == null)
        {
            EditorUtility.DisplayDialog("Duplicate DLC Card", "Cannot find SettingsHandlerButtons in the scene.\nOpen the main scene first.", "OK");
            return;
        }

        Transform canvasTransform = buttonsHandler.transform.Find("SettingsMenuCanvas");
        if (canvasTransform == null)
        {
            EditorUtility.DisplayDialog("Duplicate DLC Card", "Cannot find 'SettingsMenuCanvas'.", "OK");
            return;
        }

        RectTransform canvasRect = canvasTransform.GetComponent<RectTransform>();
        Transform menuContent = canvasRect.Find("Main Menu/Viewport/Content/MenuPanel/Main Menu");
        if (menuContent == null)
        {
            EditorUtility.DisplayDialog("Duplicate DLC Card", "Cannot find settings menu content.", "OK");
            return;
        }

        // Find the source card
        Transform source = menuContent.Find("= STEAM DLC");
        if (source == null)
        {
            EditorUtility.DisplayDialog("Duplicate DLC Card", "Cannot find '= STEAM DLC' section.", "OK");
            return;
        }

        // Find the = CODEX card to position below it
        Transform codexCard = menuContent.Find("= CODEX");
        RectTransform codexRect = codexCard != null ? codexCard.GetComponent<RectTransform>() : null;

        // Duplicate the entire hierarchy
        var clone = Object.Instantiate(source.gameObject, menuContent);
        clone.name = "= STEAM DLC 2";
        Undo.RegisterCreatedObjectUndo(clone, "Duplicate Steam DLC Card");

        // Position below = CODEX (446 units gap, same as between STEAM DLC and CODEX)
        var cloneRect = clone.GetComponent<RectTransform>();
        if (codexRect != null)
        {
            var pos = codexRect.anchoredPosition;
            pos.y -= 446f;
            cloneRect.anchoredPosition = pos;
        }
        else
        {
            // Fallback: below source card
            var srcRect = source.GetComponent<RectTransform>();
            var pos = srcRect.anchoredPosition;
            pos.y -= 892f; // two gaps below original
            cloneRect.anchoredPosition = pos;
        }

        cloneRect.sizeDelta = source.GetComponent<RectTransform>().sizeDelta;

        // Expand scroll content bottom padding so the new card is fully scrollable
        var contentTransform = canvasRect.Find("Main Menu/Viewport/Content");
        if (contentTransform != null)
        {
            var vlg = contentTransform.GetComponent<VerticalLayoutGroup>();
            if (vlg != null)
            {
                float cardBottom = Mathf.Abs(cloneRect.anchoredPosition.y) + 500f;
                if (vlg.padding.bottom < cardBottom)
                {
                    vlg.padding.bottom = Mathf.CeilToInt(cardBottom);
                    EditorUtility.SetDirty(contentTransform.gameObject);
                    Debug.Log("[Codex Setup] Expanded scroll content bottom padding to " + vlg.padding.bottom);
                }
            }
        }

        EditorUtility.SetDirty(clone);
        Selection.activeGameObject = clone;

        Debug.Log("[Codex Setup] Duplicated '= STEAM DLC' as '= STEAM DLC 2' below = CODEX card.");
        EditorUtility.DisplayDialog("Duplicate DLC Card",
            "Created '= STEAM DLC 2' below the Codex AI card.\n\n" +
            "Position: " + cloneRect.anchoredPosition, "OK");
    }

    [MenuItem("Mate Engine/Convert Steam DLC 2 to AI Settings Card", false, 104)]
    static void ConvertSteamDLC2ToAISettings()
    {
        var buttonsHandler = Object.FindFirstObjectByType<SettingsHandlerButtons>(FindObjectsInactive.Include);
        if (buttonsHandler == null)
        {
            EditorUtility.DisplayDialog("Convert Card", "Cannot find SettingsHandlerButtons.\nOpen the main scene first.", "OK");
            return;
        }

        GameObject settingsGO = buttonsHandler.gameObject;
        var codexHandler = settingsGO.GetComponent<SettingsHandlerCodex>();
        if (codexHandler == null)
        {
            EditorUtility.DisplayDialog("Convert Card", "SettingsHandlerCodex not found. Run 'Setup Codex Settings UI' first.", "OK");
            return;
        }

        Transform canvasTransform = settingsGO.transform.Find("SettingsMenuCanvas");
        if (canvasTransform == null)
        {
            EditorUtility.DisplayDialog("Convert Card", "Cannot find 'SettingsMenuCanvas'.", "OK");
            return;
        }

        RectTransform canvasRect = canvasTransform.GetComponent<RectTransform>();
        Transform menuContent = canvasRect.Find("Main Menu/Viewport/Content/MenuPanel/Main Menu");
        if (menuContent == null)
        {
            EditorUtility.DisplayDialog("Convert Card", "Cannot find settings menu content.", "OK");
            return;
        }

        // Find = CODEX CONFIGURATION (or = STEAM DLC 2 before conversion)
        Transform card = menuContent.Find("= CODEX CONFIGURATION");
        if (card == null)
            card = menuContent.Find("= STEAM DLC 2");
        if (card == null)
        {
            EditorUtility.DisplayDialog("Convert Card", "Cannot find '= CODEX CONFIGURATION' or '= STEAM DLC 2'. Run 'Duplicate Steam DLC Card Below Codex' first.", "OK");
            return;
        }

        // Rename section header
        card.name = "= CODEX CONFIGURATION";

        // Find and update TITLE text
        var titleTMP = FindTMPInChildren(card, "TITLE");
        if (titleTMP != null)
        {
            titleTMP.text = "CODEX CONFIGURATION";
            // Disable LocalizeStringEvent if present
            var localize = titleTMP.GetComponent("LocalizeStringEvent") as MonoBehaviour;
            if (localize != null) localize.enabled = false;
            EditorUtility.SetDirty(titleTMP.gameObject);
        }

        // Find and update INFO text
        var infoTMP = FindTMPInChildren(card, "INFO");
        if (infoTMP != null)
        {
            infoTMP.text = "Configure the AI model and animation behavior for your avatar conversations.";
            var localize = infoTMP.GetComponent("LocalizeStringEvent") as MonoBehaviour;
            if (localize != null) localize.enabled = false;
            EditorUtility.SetDirty(infoTMP.gameObject);
        }

        // Find the BUY/button object and its parent (BLUR card)
        Transform buyTransform = FindTransformInChildren(card, "BUY");
        if (buyTransform == null)
            buyTransform = FindTransformInChildren(card, "LOGIN"); // in case it was already renamed

        Transform blurCard = null;
        if (buyTransform != null)
            blurCard = buyTransform.parent;

        // Destroy the BUY button
        if (buyTransform != null)
        {
            Undo.DestroyObjectImmediate(buyTransform.gameObject);
            Debug.Log("[Codex Setup] Removed BUY button from card.");
        }

        if (blurCard == null)
        {
            // Fallback: find BLUR card by name
            blurCard = FindTransformInChildren(card, "BLUR");
        }

        if (blurCard == null)
        {
            EditorUtility.DisplayDialog("Convert Card", "Cannot find BLUR card container. Card may have unexpected hierarchy.", "OK");
            return;
        }

        RectTransform blurRect = blurCard.GetComponent<RectTransform>();

        // ── Clean up old controls before creating new ones ──
        string[] oldControls = { "CodexModelDropdown", "CodexProviderDropdown",
                                  "CodexAnimDirectivesToggle", "ModelLabel", "ProviderLabel" };
        foreach (var controlName in oldControls)
        {
            var old = FindTransformInChildren(blurCard, controlName);
            if (old != null) Object.DestroyImmediate(old.gameObject);
        }

        // ── Create Model Label + Dropdown ──
        CreateLabel("ModelLabel", blurRect, "Model", -90f);

        var dropdownGO = CreateDropdown("CodexModelDropdown", blurRect, "Select Model...");
        var dropdownRect = dropdownGO.GetComponent<RectTransform>();
        dropdownRect.anchoredPosition = new Vector2(-57.8f, -120f);
        dropdownRect.sizeDelta = new Vector2(400f, 40f);
        var modelDropdown = dropdownGO.GetComponent<TMP_Dropdown>();
        Undo.RegisterCreatedObjectUndo(dropdownGO, "Create Model Dropdown");

        // ── Create Provider Label + Dropdown ──
        CreateLabel("ProviderLabel", blurRect, "Provider", -170f);

        var providerDropdownGO = CreateDropdown("CodexProviderDropdown", blurRect, "Select Provider...");
        var providerDropdownRect = providerDropdownGO.GetComponent<RectTransform>();
        providerDropdownRect.anchoredPosition = new Vector2(-57.8f, -200f);
        providerDropdownRect.sizeDelta = new Vector2(400f, 40f);
        var providerDropdown = providerDropdownGO.GetComponent<TMP_Dropdown>();
        Undo.RegisterCreatedObjectUndo(providerDropdownGO, "Create Provider Dropdown");

        // ── Create Animation Directives Toggle ──
        var toggleGO = CreateToggle("CodexAnimDirectivesToggle", blurRect, "Enable Animation Directives");
        var toggleRect = toggleGO.GetComponent<RectTransform>();
        toggleRect.anchoredPosition = new Vector2(-57.8f, -250f);
        toggleRect.sizeDelta = new Vector2(400f, 30f);
        var animToggle = toggleGO.GetComponent<Toggle>();
        Undo.RegisterCreatedObjectUndo(toggleGO, "Create Animation Toggle");

        // ── Wire references into SettingsHandlerCodex ──
        var codexSO = new SerializedObject(codexHandler);
        SetObjectRef(codexSO, "modelDropdown", modelDropdown);
        SetObjectRef(codexSO, "providerDropdown", providerDropdown);
        SetObjectRef(codexSO, "enableAnimDirectivesToggle", animToggle);
        codexSO.ApplyModifiedProperties();

        EditorUtility.SetDirty(settingsGO);
        EditorUtility.SetDirty(card.gameObject);
        Selection.activeGameObject = card.gameObject;

        Debug.Log("[Codex Setup] Converted '= STEAM DLC 2' to '= CODEX CONFIGURATION' with model dropdown and animation toggle.");
        EditorUtility.DisplayDialog("Convert Card",
            "Converted to '= CODEX CONFIGURATION':\n\n" +
            "- Title: AI SETTINGS\n" +
            "- Model dropdown: " + (modelDropdown != null ? "OK" : "FAILED") + "\n" +
            "- Provider dropdown: " + (providerDropdown != null ? "OK" : "FAILED") + "\n" +
            "- Animation toggle: " + (animToggle != null ? "OK" : "FAILED") + "\n" +
            "- BUY button: removed\n\n" +
            "References wired to SettingsHandlerCodex.", "OK");
    }

    static Transform FindTransformInChildren(Transform root, string name)
    {
        foreach (var t in root.GetComponentsInChildren<Transform>(true))
        {
            if (t.name == name) return t;
        }
        return null;
    }

    [MenuItem("Mate Engine/Setup Codex Settings UI", false, 102)]
    static void SetupCodexSettingsUI()
    {
        var buttonsHandler = Object.FindFirstObjectByType<SettingsHandlerButtons>(FindObjectsInactive.Include);
        if (buttonsHandler == null)
        {
            EditorUtility.DisplayDialog("Codex Settings UI", "Cannot find SettingsHandlerButtons in the scene.\nOpen the main scene first.", "OK");
            return;
        }

        GameObject settingsGO = buttonsHandler.gameObject;

        // Add SettingsHandlerCodex if not already present
        var codexHandler = settingsGO.GetComponent<SettingsHandlerCodex>();
        if (codexHandler == null)
        {
            codexHandler = Undo.AddComponent<SettingsHandlerCodex>(settingsGO);
            Debug.Log("[Codex Setup] Added SettingsHandlerCodex component to " + settingsGO.name);
        }

        // Wire codexHandler into SettingsHandlerButtons
        var so = new SerializedObject(buttonsHandler);
        var codexProp = so.FindProperty("codexHandler");
        if (codexProp != null && codexProp.objectReferenceValue == null)
        {
            codexProp.objectReferenceValue = codexHandler;
            so.ApplyModifiedProperties();
            Debug.Log("[Codex Setup] Wired codexHandler reference in SettingsHandlerButtons.");
        }

        // Find the SettingsMenuCanvas
        Transform canvasTransform = settingsGO.transform.Find("SettingsMenuCanvas");
        if (canvasTransform == null)
        {
            EditorUtility.DisplayDialog("Codex Settings UI", "Cannot find 'SettingsMenuCanvas' under the Settings GameObject.", "OK");
            return;
        }

        RectTransform canvasRect = canvasTransform.GetComponent<RectTransform>();

        // Find the Main Menu content inside the settings scroll view
        Transform menuContentTransform = canvasRect.Find("Main Menu/Viewport/Content/MenuPanel/Main Menu");
        bool usingScrollMenu = menuContentTransform != null;
        if (menuContentTransform == null)
            menuContentTransform = canvasRect.Find("OuterMenu");

        if (menuContentTransform == null)
        {
            EditorUtility.DisplayDialog("Codex Settings UI",
                "Cannot find the settings content container under SettingsMenuCanvas.\nExpected: 'Main Menu/Viewport/Content/MenuPanel/Main Menu' or 'OuterMenu'.",
                "OK");
            return;
        }

        // ── Wire Login button from the repurposed = CODEX card ──
        Button loginButton = null;
        TextMeshProUGUI statusText = null;

        Transform codexSection = menuContentTransform.Find("= CODEX");
        if (codexSection != null)
        {
            // Traverse: = CODEX > CODEX_AUTH > BLUR > LOGIN
            loginButton = FindButtonInChildren(codexSection, "LOGIN");
            // Use the INFO text as status display
            statusText = FindTMPInChildren(codexSection, "INFO");

            if (loginButton != null)
                Debug.Log("[Codex Setup] Found LOGIN button in = CODEX section.");
            else
                Debug.LogWarning("[Codex Setup] Could not find LOGIN button in = CODEX section. Check hierarchy.");

            if (statusText != null)
                Debug.Log("[Codex Setup] Found INFO text (status) in = CODEX section.");
        }
        else
        {
            Debug.LogWarning("[Codex Setup] Could not find '= CODEX' section in settings menu. Rename '= STEAM DLC 2' to '= CODEX' first.");
        }

        // ── Find model dropdown, provider dropdown + animation toggle ──
        // First check = CODEX CONFIGURATION card (preferred), then fall back to CodexPanel
        TMP_Dropdown modelDropdown = null;
        TMP_Dropdown providerDropdown = null;
        Toggle animToggle = null;
        GameObject selectionTarget = settingsGO;

        Transform aiSettingsSection = menuContentTransform.Find("= CODEX CONFIGURATION");
        if (aiSettingsSection != null)
        {
            // Look for controls inside the = CODEX CONFIGURATION card
            var dropdownT = FindTransformInChildren(aiSettingsSection, "CodexModelDropdown");
            if (dropdownT != null)
                modelDropdown = dropdownT.GetComponent<TMP_Dropdown>();

            var providerDropdownT = FindTransformInChildren(aiSettingsSection, "CodexProviderDropdown");
            if (providerDropdownT != null)
                providerDropdown = providerDropdownT.GetComponent<TMP_Dropdown>();

            var toggleT = FindTransformInChildren(aiSettingsSection, "CodexAnimDirectivesToggle");
            if (toggleT != null)
                animToggle = toggleT.GetComponent<Toggle>();

            if (modelDropdown != null && animToggle != null)
            {
                Debug.Log("[Codex Setup] Found model dropdown, provider dropdown and animation toggle in = CODEX CONFIGURATION card.");
                selectionTarget = aiSettingsSection.gameObject;

                // Remove legacy CodexPanel if it exists since controls now live in the card
                foreach (var t in settingsGO.GetComponentsInChildren<Transform>(true))
                {
                    if (t.name == "CodexPanel")
                    {
                        Debug.Log("[Codex Setup] Removing legacy CodexPanel (controls now in = CODEX CONFIGURATION card).");
                        Undo.DestroyObjectImmediate(t.gameObject);
                        break;
                    }
                }
            }
            else
            {
                Debug.LogWarning("[Codex Setup] = CODEX CONFIGURATION card found but missing controls. Run 'Convert Steam DLC 2 to AI Settings Card' first.");
            }
        }

        // Fall back to CodexPanel if controls not found in = CODEX CONFIGURATION
        if (modelDropdown == null || animToggle == null)
        {
            Transform existing = null;
            foreach (var t in settingsGO.GetComponentsInChildren<Transform>(true))
            {
                if (t.name != "CodexPanel") continue;
                if (existing == null)
                    existing = t;
                else
                    Undo.DestroyObjectImmediate(t.gameObject);
            }

            if (existing != null && existing.parent != menuContentTransform)
            {
                existing.SetParent(menuContentTransform, false);
                Debug.Log("[Codex Setup] Moved CodexPanel under settings content container.");
            }

            GameObject panel;
            RectTransform panelRect;

            if (existing == null)
            {
                panel = CreateUIObject("CodexPanel", menuContentTransform.GetComponent<RectTransform>());
                panelRect = panel.GetComponent<RectTransform>();
                Undo.RegisterCreatedObjectUndo(panel, "Create Codex Settings Panel");

                var vlg = panel.AddComponent<VerticalLayoutGroup>();
                vlg.spacing = 8;
                vlg.childAlignment = TextAnchor.UpperCenter;
                vlg.childControlWidth = true;
                vlg.childControlHeight = false;
                vlg.childForceExpandWidth = true;
                vlg.childForceExpandHeight = false;
                vlg.padding = new RectOffset(10, 10, 10, 10);

                var dropdownGO = CreateDropdown("CodexModelDropdown", panelRect, "Select Model...");
                modelDropdown = dropdownGO.GetComponent<TMP_Dropdown>();
                var dropdownLayout = dropdownGO.AddComponent<LayoutElement>();
                dropdownLayout.preferredHeight = 35;

                var providerDdGO = CreateDropdown("CodexProviderDropdown", panelRect, "Select Provider...");
                providerDropdown = providerDdGO.GetComponent<TMP_Dropdown>();
                var providerLayout = providerDdGO.AddComponent<LayoutElement>();
                providerLayout.preferredHeight = 35;

                var toggleGO = CreateToggle("CodexAnimDirectivesToggle", panelRect, "Enable Animation Directives");
                animToggle = toggleGO.GetComponent<Toggle>();
                var toggleLayout = toggleGO.AddComponent<LayoutElement>();
                toggleLayout.preferredHeight = 25;
            }
            else
            {
                panel = existing.gameObject;
                panelRect = existing.GetComponent<RectTransform>();
                modelDropdown = modelDropdown ?? panelRect.Find("CodexModelDropdown")?.GetComponent<TMP_Dropdown>();
                providerDropdown = providerDropdown ?? panelRect.Find("CodexProviderDropdown")?.GetComponent<TMP_Dropdown>();
                animToggle = animToggle ?? panelRect.Find("CodexAnimDirectivesToggle")?.GetComponent<Toggle>();
            }

            // Position the CodexPanel
            RectTransform legacyAiAnchor = null;
            if (usingScrollMenu)
                legacyAiAnchor = menuContentTransform.Find("= AI")?.GetComponent<RectTransform>();

            if (usingScrollMenu && legacyAiAnchor != null)
            {
                panelRect.anchorMin = legacyAiAnchor.anchorMin;
                panelRect.anchorMax = legacyAiAnchor.anchorMax;
                panelRect.pivot = legacyAiAnchor.pivot;
                panelRect.anchoredPosition = legacyAiAnchor.anchoredPosition;
                panelRect.sizeDelta = new Vector2(Mathf.Max(legacyAiAnchor.sizeDelta.x, 500f), 260f);
            }
            else if (usingScrollMenu)
            {
                panelRect.anchorMin = new Vector2(0.5f, 0.5f);
                panelRect.anchorMax = new Vector2(0.5f, 0.5f);
                panelRect.pivot = new Vector2(0.5f, 0.5f);
                panelRect.sizeDelta = new Vector2(500, 260);
                panelRect.anchoredPosition = new Vector2(-57.8f, -2604f);
            }
            else
            {
                panelRect.anchorMin = new Vector2(0, 0);
                panelRect.anchorMax = new Vector2(1, 0);
                panelRect.pivot = new Vector2(0.5f, 0);
                panelRect.sizeDelta = new Vector2(0, 230);
                panelRect.anchoredPosition = new Vector2(0, 10);
            }

            selectionTarget = panel;
        }

        if (usingScrollMenu)
            DisableLegacyAiControls(menuContentTransform);

        // ── Wire all references into SettingsHandlerCodex ──
        var codexSO = new SerializedObject(codexHandler);
        if (loginButton != null) SetObjectRef(codexSO, "loginButton", loginButton);
        if (statusText != null) SetObjectRef(codexSO, "statusText", statusText);
        if (modelDropdown != null) SetObjectRef(codexSO, "modelDropdown", modelDropdown);
        if (providerDropdown != null) SetObjectRef(codexSO, "providerDropdown", providerDropdown);
        if (animToggle != null) SetObjectRef(codexSO, "enableAnimDirectivesToggle", animToggle);
        codexSO.ApplyModifiedProperties();

        EditorUtility.SetDirty(settingsGO);
        Selection.activeGameObject = selectionTarget;

        string dropdownSource = aiSettingsSection != null && modelDropdown != null ? "= CODEX CONFIGURATION card" : "CodexPanel";
        Debug.Log("[Codex Setup] Codex Settings UI wired successfully.");
        EditorUtility.DisplayDialog("Codex Settings UI",
            "Codex UI wired:\n\n" +
            "- Login button from = CODEX card: " + (loginButton != null ? "OK" : "NOT FOUND") + "\n" +
            "- Status text from = CODEX card: " + (statusText != null ? "OK" : "NOT FOUND") + "\n" +
            "- Model dropdown (" + dropdownSource + "): " + (modelDropdown != null ? "OK" : "NOT FOUND") + "\n" +
            "- Provider dropdown (" + dropdownSource + "): " + (providerDropdown != null ? "OK" : "NOT FOUND") + "\n" +
            "- Animation toggle (" + dropdownSource + "): " + (animToggle != null ? "OK" : "NOT FOUND") + "\n" +
            "- New Chat button: created at runtime by cloning Login\n\n" +
            "All references wired to SettingsHandlerCodex.", "OK");
    }

    static Button FindButtonInChildren(Transform root, string name)
    {
        foreach (var t in root.GetComponentsInChildren<Transform>(true))
        {
            if (t.name == name)
            {
                var btn = t.GetComponent<Button>();
                if (btn != null) return btn;
            }
        }
        return null;
    }

    static TextMeshProUGUI FindTMPInChildren(Transform root, string name)
    {
        foreach (var t in root.GetComponentsInChildren<Transform>(true))
        {
            if (t.name == name)
            {
                var tmp = t.GetComponent<TextMeshProUGUI>();
                if (tmp != null) return tmp;
            }
        }
        return null;
    }

    static void SetObjectRef(SerializedObject so, string propName, Object value)
    {
        var prop = so.FindProperty(propName);
        if (prop != null) prop.objectReferenceValue = value;
    }

    static GameObject CreateUIObject(string name, RectTransform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.layer = 5;
        go.transform.SetParent(parent, false);
        return go;
    }

    static GameObject CreateLabel(string name, RectTransform parent, string text, float yPos)
    {
        var go = CreateUIObject(name, parent);
        var tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.text = text;
        tmp.fontSize = 16;
        tmp.fontStyle = FontStyles.Bold;
        tmp.alignment = TextAlignmentOptions.MidlineLeft;
        tmp.color = Color.white;
        var rect = go.GetComponent<RectTransform>();
        rect.anchoredPosition = new Vector2(-57.8f, yPos);
        rect.sizeDelta = new Vector2(400f, 25f);
        return go;
    }

    static GameObject CreateDropdown(string name, RectTransform parent, string placeholder = "Select...")
    {
        var go = CreateUIObject(name, parent);

        var image = go.AddComponent<Image>();
        image.color = new Color(0.2f, 0.2f, 0.25f, 1f);

        var dropdown = go.AddComponent<TMP_Dropdown>();

        var captionGO = CreateUIObject("Label", go.GetComponent<RectTransform>());
        var captionText = captionGO.AddComponent<TextMeshProUGUI>();
        captionText.text = placeholder;
        captionText.fontSize = 14;
        captionText.alignment = TextAlignmentOptions.MidlineLeft;
        captionText.color = Color.white;
        var captionRect = captionGO.GetComponent<RectTransform>();
        captionRect.anchorMin = Vector2.zero;
        captionRect.anchorMax = Vector2.one;
        captionRect.offsetMin = new Vector2(10, 0);
        captionRect.offsetMax = new Vector2(-25, 0);
        dropdown.captionText = captionText;

        var arrowGO = CreateUIObject("Arrow", go.GetComponent<RectTransform>());
        var arrowText = arrowGO.AddComponent<TextMeshProUGUI>();
        arrowText.text = "\u25BC";
        arrowText.fontSize = 12;
        arrowText.alignment = TextAlignmentOptions.Center;
        arrowText.color = Color.white;
        var arrowRect = arrowGO.GetComponent<RectTransform>();
        arrowRect.anchorMin = new Vector2(1, 0);
        arrowRect.anchorMax = new Vector2(1, 1);
        arrowRect.sizeDelta = new Vector2(25, 0);
        arrowRect.anchoredPosition = new Vector2(-12.5f, 0);

        var templateGO = CreateUIObject("Template", go.GetComponent<RectTransform>());
        var templateRect = templateGO.GetComponent<RectTransform>();
        templateRect.anchorMin = new Vector2(0, 0);
        templateRect.anchorMax = new Vector2(1, 0);
        templateRect.pivot = new Vector2(0.5f, 1f);
        templateRect.sizeDelta = new Vector2(0, 150);
        var templateImage = templateGO.AddComponent<Image>();
        templateImage.color = new Color(0.15f, 0.15f, 0.2f, 1f);
        var scrollRect = templateGO.AddComponent<ScrollRect>();

        var viewportGO = CreateUIObject("Viewport", templateRect);
        var viewportRect = viewportGO.GetComponent<RectTransform>();
        viewportRect.anchorMin = Vector2.zero;
        viewportRect.anchorMax = Vector2.one;
        viewportRect.sizeDelta = Vector2.zero;
        var viewportMask = viewportGO.AddComponent<Mask>();
        viewportMask.showMaskGraphic = false;
        viewportGO.AddComponent<Image>();
        scrollRect.viewport = viewportRect;

        var contentGO = CreateUIObject("Content", viewportRect);
        var contentRect = contentGO.GetComponent<RectTransform>();
        contentRect.anchorMin = new Vector2(0, 1);
        contentRect.anchorMax = new Vector2(1, 1);
        contentRect.pivot = new Vector2(0.5f, 1f);
        contentRect.sizeDelta = new Vector2(0, 28);
        scrollRect.content = contentRect;

        var itemGO = CreateUIObject("Item", contentRect);
        var itemRect = itemGO.GetComponent<RectTransform>();
        itemRect.anchorMin = new Vector2(0, 0.5f);
        itemRect.anchorMax = new Vector2(1, 0.5f);
        itemRect.sizeDelta = new Vector2(0, 28);
        var itemToggle = itemGO.AddComponent<Toggle>();

        var itemBgGO = CreateUIObject("Item Background", itemRect);
        var itemBgImage = itemBgGO.AddComponent<Image>();
        itemBgImage.color = new Color(0.25f, 0.25f, 0.3f, 1f);
        var itemBgRect = itemBgGO.GetComponent<RectTransform>();
        itemBgRect.anchorMin = Vector2.zero;
        itemBgRect.anchorMax = Vector2.one;
        itemBgRect.sizeDelta = Vector2.zero;

        var checkGO = CreateUIObject("Item Checkmark", itemRect);
        var checkImage = checkGO.AddComponent<Image>();
        checkImage.color = Color.white;
        var checkRect = checkGO.GetComponent<RectTransform>();
        checkRect.anchorMin = new Vector2(0, 0.5f);
        checkRect.anchorMax = new Vector2(0, 0.5f);
        checkRect.sizeDelta = new Vector2(20, 20);
        checkRect.anchoredPosition = new Vector2(15, 0);
        itemToggle.graphic = checkImage;

        var itemLabelGO = CreateUIObject("Item Label", itemRect);
        var itemLabel = itemLabelGO.AddComponent<TextMeshProUGUI>();
        itemLabel.fontSize = 14;
        itemLabel.alignment = TextAlignmentOptions.MidlineLeft;
        itemLabel.color = Color.white;
        var itemLabelRect = itemLabelGO.GetComponent<RectTransform>();
        itemLabelRect.anchorMin = Vector2.zero;
        itemLabelRect.anchorMax = Vector2.one;
        itemLabelRect.offsetMin = new Vector2(30, 0);
        itemLabelRect.offsetMax = new Vector2(-10, 0);

        dropdown.template = templateRect;
        dropdown.itemText = itemLabel;

        templateGO.SetActive(false);

        return go;
    }

    static GameObject CreateToggle(string name, RectTransform parent, string label)
    {
        var go = CreateUIObject(name, parent);
        var toggle = go.AddComponent<Toggle>();

        var bgGO = CreateUIObject("Background", go.GetComponent<RectTransform>());
        var bgImage = bgGO.AddComponent<Image>();
        bgImage.color = new Color(0.2f, 0.2f, 0.25f, 1f);
        var bgRect = bgGO.GetComponent<RectTransform>();
        bgRect.anchorMin = new Vector2(0, 0.5f);
        bgRect.anchorMax = new Vector2(0, 0.5f);
        bgRect.sizeDelta = new Vector2(20, 20);
        bgRect.anchoredPosition = new Vector2(15, 0);

        var checkGO = CreateUIObject("Checkmark", bgRect);
        var checkImage = checkGO.AddComponent<Image>();
        checkImage.color = new Color(0.4f, 0.8f, 0.4f, 1f);
        var checkRect = checkGO.GetComponent<RectTransform>();
        checkRect.anchorMin = new Vector2(0.1f, 0.1f);
        checkRect.anchorMax = new Vector2(0.9f, 0.9f);
        checkRect.sizeDelta = Vector2.zero;

        toggle.graphic = checkImage;
        toggle.targetGraphic = bgImage;

        var labelGO = CreateUIObject("Label", go.GetComponent<RectTransform>());
        var text = labelGO.AddComponent<TextMeshProUGUI>();
        text.text = label;
        text.fontSize = 14;
        text.alignment = TextAlignmentOptions.MidlineLeft;
        text.color = Color.white;
        var labelRect = labelGO.GetComponent<RectTransform>();
        labelRect.anchorMin = Vector2.zero;
        labelRect.anchorMax = Vector2.one;
        labelRect.offsetMin = new Vector2(35, 0);
        labelRect.offsetMax = Vector2.zero;

        return go;
    }

    static void DisableLegacyAiControls(Transform menuContentTransform)
    {
        var aiSection = menuContentTransform.Find("= AI");
        if (aiSection != null) aiSection.gameObject.SetActive(false);

        var debugSection = menuContentTransform.Find("= DEBUG");
        if (debugSection != null)
        {
            var deleteHistory = debugSection.Find("Delete AI History");
            if (deleteHistory != null) deleteHistory.gameObject.SetActive(false);
        }
    }
}
