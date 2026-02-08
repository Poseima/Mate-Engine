using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using Kirurobo;

// Wrapper around file dialogs to avoid calling native SFB dialogs inside the Unity Editor,
// which can hard-crash the editor depending on Unity version / plugin compatibility.
public static class MEFileDialog
{
    private static MEFileDialogRunner s_runner;

    private static MEFileDialogRunner EnsureRunner()
    {
        if (s_runner != null) return s_runner;
        if (!Application.isPlaying) return null;

        // Reuse existing runner if this domain reloaded mid-session.
        var existing = UnityEngine.Object.FindFirstObjectByType<MEFileDialogRunner>();
        if (existing != null)
        {
            s_runner = existing;
            return s_runner;
        }

        var go = new GameObject(nameof(MEFileDialogRunner));
        go.hideFlags = HideFlags.HideAndDontSave;
        UnityEngine.Object.DontDestroyOnLoad(go);
        s_runner = go.AddComponent<MEFileDialogRunner>();
        return s_runner;
    }

    private static string NormalizeStartDirectory(string directory)
    {
        if (string.IsNullOrWhiteSpace(directory) || directory == ".")
        {
            directory = Application.persistentDataPath;
        }

        // Some native pickers behave badly if given a non-existent directory.
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            var fallback = Application.persistentDataPath;
            directory = Directory.Exists(fallback) ? fallback : "";
        }

        return directory ?? "";
    }

    private static Action TemporarilyDisableTopmostAndClickThrough()
    {
        try
        {
            var controller = UniWindowController.current;
            if (controller == null)
            {
                controller = UnityEngine.Object.FindFirstObjectByType<UniWindowController>();
            }
            if (controller == null) return null;

            bool prevTopmost = controller.isTopmost;
            bool prevClickThrough = controller.isClickThrough;

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            Debug.Log($"[MEFileDialog] Temporarily disabling window flags (topmost={prevTopmost}, clickThrough={prevClickThrough})");
#endif

            controller.isClickThrough = false;
            controller.isTopmost = false;

            return () =>
            {
                try
                {
                    if (controller == null) return;
                    controller.isTopmost = prevTopmost;
                    controller.isClickThrough = prevClickThrough;

#if UNITY_EDITOR || DEVELOPMENT_BUILD
                    Debug.Log($"[MEFileDialog] Restored window flags (topmost={prevTopmost}, clickThrough={prevClickThrough})");
#endif
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                }
            };
        }
        catch (Exception e)
        {
            Debug.LogException(e);
            return null;
        }
    }

    private static void PostToUnity(Action a)
    {
        var runner = EnsureRunner();
        if (runner != null)
        {
            MEFileDialogRunner.Post(a);
            return;
        }

        // Best-effort fallback (should only happen in edit mode / tests).
        a?.Invoke();
    }

    public static string[] OpenFilePanel(string title, string directory, SFB.ExtensionFilter[] extensions, bool multiselect)
    {
#if UNITY_EDITOR
        // EditorUtility only supports single selection. Keep the API but ignore multiselect.
        directory = NormalizeStartDirectory(directory);

        string path;
#if UNITY_EDITOR_OSX
        // Unity 6 + macOS has been observed to hang/crash more often with OpenFilePanelWithFilters.
        // Use an unfiltered picker and validate extensions in code.
        path = UnityEditor.EditorUtility.OpenFilePanel(title, directory, "");
#else
        var filters = BuildEditorFilters(extensions);
        path = UnityEditor.EditorUtility.OpenFilePanelWithFilters(title, directory, filters);
#endif
        if (string.IsNullOrEmpty(path))
        {
            return Array.Empty<string>();
        }

        if (!MatchesExtensions(path, extensions))
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            Debug.LogWarning($"[MEFileDialog] File rejected by extension filter: '{path}'");
#endif
            return Array.Empty<string>();
        }

        return new[] { path };
#else
        directory = NormalizeStartDirectory(directory);
        var restoreFlags = TemporarilyDisableTopmostAndClickThrough();
        try
        {
            return SFB.StandaloneFileBrowser.OpenFilePanel(title, directory, extensions, multiselect);
        }
        finally
        {
            restoreFlags?.Invoke();
        }
#endif
    }

    public static void OpenFilePanelAsync(
        string title,
        string directory,
        SFB.ExtensionFilter[] extensions,
        bool multiselect,
        Action<string[]> cb)
    {
#if UNITY_EDITOR
        // On macOS the Editor can freeze if a modal file picker is opened directly inside a UI callback.
        // Defer to the next editor tick to avoid re-entrancy issues.
        UnityEditor.EditorApplication.delayCall += () =>
        {
            var restoreFlags = TemporarilyDisableTopmostAndClickThrough();
            try
            {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                Debug.Log($"[MEFileDialog] Opening editor file panel: title='{title}', dir='{directory}'");
#endif
                var paths = OpenFilePanel(title, directory, extensions, multiselect);
                cb?.Invoke(paths ?? Array.Empty<string>());
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                cb?.Invoke(Array.Empty<string>());
            }
            finally
            {
                restoreFlags?.Invoke();
            }
        };
#else
        // Ensure callbacks execute on Unity main thread even if the native plugin invokes them from a worker thread.
        EnsureRunner();

        // Avoid opening the native dialog inside the same UI event that triggered this call.
        PostToUnity(() =>
        {
            var restoreFlags = TemporarilyDisableTopmostAndClickThrough();
            try
            {
#if DEVELOPMENT_BUILD
                Debug.Log($"[MEFileDialog] Opening runtime file panel: title='{title}', dir='{directory}'");
#endif

                directory = NormalizeStartDirectory(directory);
                SFB.StandaloneFileBrowser.OpenFilePanelAsync(title, directory, extensions, multiselect, paths =>
                {
                    PostToUnity(() =>
                    {
                        try
                        {
                            cb?.Invoke(paths ?? Array.Empty<string>());
                        }
                        finally
                        {
                            restoreFlags?.Invoke();
                        }
                    });
                });
            }
            catch (Exception e)
            {
                PostToUnity(() =>
                {
                    try
                    {
                        Debug.LogException(e);
                        cb?.Invoke(Array.Empty<string>());
                    }
                    finally
                    {
                        restoreFlags?.Invoke();
                    }
                });
            }
        });
#endif
    }

#if UNITY_EDITOR
    private static string[] BuildEditorFilters(SFB.ExtensionFilter[] extensions)
    {
        if (extensions == null || extensions.Length == 0)
        {
            return new[] { "All Files", "*" };
        }

        // EditorUtility.OpenFilePanelWithFilters expects:
        // [ "Display Name", "ext1,ext2", "Other Name", "ext3", ... ]
        var filters = new string[extensions.Length * 2];
        for (int i = 0; i < extensions.Length; i++)
        {
            var ext = extensions[i];
            filters[i * 2] = string.IsNullOrEmpty(ext.Name) ? "Files" : ext.Name;
            filters[i * 2 + 1] = (ext.Extensions != null && ext.Extensions.Length > 0)
                ? string.Join(",", ext.Extensions)
                : "*";
        }
        return filters;
    }
#endif

    private static bool MatchesExtensions(string path, SFB.ExtensionFilter[] extensions)
    {
        if (extensions == null || extensions.Length == 0) return true;

        var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < extensions.Length; i++)
        {
            var ext = extensions[i].Extensions;
            if (ext == null) continue;
            for (int j = 0; j < ext.Length; j++)
            {
                var e = ext[j];
                if (string.IsNullOrWhiteSpace(e)) continue;
                allowed.Add(e.Trim().TrimStart('.'));
            }
        }

        if (allowed.Count == 0) return true;
        if (allowed.Contains("*")) return true;

        var fileExt = Path.GetExtension(path);
        if (string.IsNullOrEmpty(fileExt)) return false;
        return allowed.Contains(fileExt.TrimStart('.'));
    }
}
