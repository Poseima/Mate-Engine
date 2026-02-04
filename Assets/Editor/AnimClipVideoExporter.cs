using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

public class AnimClipVideoExporter : EditorWindow
{
    GameObject avatarPrefab;
    string outputFolder;
    int resolution = 512;
    int fps = 30;
    Color bgColor = new Color(0.18f, 0.18f, 0.18f, 1f);
    float cameraDistance = 3.5f;
    float cameraHeight = 0.65f;

    // Discovered clips
    List<(string name, string assetPath)> allClips = new();
    Vector2 scrollPos;

    const string AnimRoot = "Assets/MATE ENGINE - Animations";

    [MenuItem("Mate Engine/Export Clip Videos", false, 200)]
    static void Open()
    {
        var win = GetWindow<AnimClipVideoExporter>("Clip Video Exporter");
        win.minSize = new Vector2(400, 450);

        win.avatarPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/MATE ENGINE - Avatar/Zome.prefab");

        win.outputFolder = Path.Combine(
            Directory.GetParent(Application.dataPath).FullName, "ClipVideos");

        win.ScanClips();
    }

    void ScanClips()
    {
        allClips.Clear();
        var guids = AssetDatabase.FindAssets("t:AnimationClip", new[] { AnimRoot });
        foreach (var guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            string name = Path.GetFileNameWithoutExtension(path);
            allClips.Add((name, path));
        }
        allClips.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
    }

    void OnGUI()
    {
        EditorGUILayout.Space(8);
        EditorGUILayout.LabelField("Animation Clip Video Exporter", EditorStyles.boldLabel);
        EditorGUILayout.Space(4);

        avatarPrefab = (GameObject)EditorGUILayout.ObjectField("Avatar Prefab", avatarPrefab, typeof(GameObject), false);

        EditorGUILayout.BeginHorizontal();
        outputFolder = EditorGUILayout.TextField("Output Folder", outputFolder);
        if (GUILayout.Button("...", GUILayout.Width(30)))
        {
            string picked = EditorUtility.OpenFolderPanel("Choose Output Folder", outputFolder, "");
            if (!string.IsNullOrEmpty(picked)) outputFolder = picked;
        }
        EditorGUILayout.EndHorizontal();

        resolution = EditorGUILayout.IntSlider("Resolution", resolution, 256, 1024);
        fps = EditorGUILayout.IntSlider("FPS", fps, 15, 60);
        bgColor = EditorGUILayout.ColorField("Background", bgColor);
        cameraDistance = EditorGUILayout.Slider("Camera Distance", cameraDistance, 0.5f, 5f);
        cameraHeight = EditorGUILayout.Slider("Camera Height", cameraHeight, 0f, 2f);

        EditorGUILayout.Space(8);

        if (GUILayout.Button("Rescan Clips"))
            ScanClips();

        EditorGUILayout.Space(4);

        if (GUILayout.Button($"Test Export 1 Clip ({(allClips.Count > 0 ? allClips[0].name : "?")})", GUILayout.Height(26)))
        {
            ExportClips(0, 1);
        }

        if (GUILayout.Button($"Export All Clips ({allClips.Count})", GUILayout.Height(30)))
        {
            ExportClips(0, allClips.Count);
        }

        EditorGUILayout.Space(8);
        EditorGUILayout.LabelField($"Found {allClips.Count} clips in {AnimRoot}/", EditorStyles.miniLabel);

        scrollPos = EditorGUILayout.BeginScrollView(scrollPos, GUILayout.ExpandHeight(true));
        for (int i = 0; i < allClips.Count; i++)
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField($"{i}: {allClips[i].name}", EditorStyles.miniLabel);
            if (GUILayout.Button("Export", GUILayout.Width(55)))
                ExportClips(i, 1);
            EditorGUILayout.EndHorizontal();
        }
        EditorGUILayout.EndScrollView();
    }

    void ExportClips(int start, int count)
    {
        if (avatarPrefab == null)
        {
            EditorUtility.DisplayDialog("Error", "Assign an avatar prefab first.", "OK");
            return;
        }

        if (allClips.Count == 0)
        {
            EditorUtility.DisplayDialog("Error", "No clips found. Click Rescan.", "OK");
            return;
        }

        string ffmpeg = FindFFmpeg();
        if (string.IsNullOrEmpty(ffmpeg))
        {
            EditorUtility.DisplayDialog("Error", "ffmpeg not found. Install it via 'brew install ffmpeg'.", "OK");
            return;
        }

        if (!Directory.Exists(outputFolder))
            Directory.CreateDirectory(outputFolder);

        string tempDir = Path.Combine(outputFolder, "_frames_temp");

        const int exportLayer = 31;

        var instance = (GameObject)PrefabUtility.InstantiatePrefab(avatarPrefab);
        instance.transform.position = Vector3.zero;
        instance.transform.rotation = Quaternion.identity;
        instance.hideFlags = HideFlags.HideAndDontSave;
        SetLayerRecursive(instance, exportLayer);

        var floorGo = GameObject.CreatePrimitive(PrimitiveType.Plane);
        floorGo.name = "_ExportFloor";
        floorGo.hideFlags = HideFlags.HideAndDontSave;
        floorGo.layer = exportLayer;
        floorGo.transform.position = new Vector3(0, -0.001f, 0);
        floorGo.transform.localScale = new Vector3(10, 1, 10);
        var floorRenderer = floorGo.GetComponent<Renderer>();
        floorRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        floorRenderer.receiveShadows = false;
        floorRenderer.material = new Material(Shader.Find("Unlit/Color"));
        floorRenderer.material.color = bgColor;

        var camGo = new GameObject("_ExportCamera") { hideFlags = HideFlags.HideAndDontSave };
        var cam = camGo.AddComponent<Camera>();
        cam.cullingMask = 1 << exportLayer;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = bgColor;
        cam.nearClipPlane = 0.01f;
        cam.farClipPlane = 100f;
        cam.fieldOfView = 30f;
        camGo.transform.position = new Vector3(0, cameraHeight, cameraDistance);
        camGo.transform.LookAt(new Vector3(0, cameraHeight, 0));

        var rt = new RenderTexture(resolution, resolution, 24, RenderTextureFormat.ARGB32);
        cam.targetTexture = rt;

        var tex = new Texture2D(resolution, resolution, TextureFormat.RGB24, false);

        int exported = 0;
        int skipped = 0;

        try
        {
            int end = Mathf.Min(start + count, allClips.Count);
            for (int i = start; i < end; i++)
            {
                var (clipName, assetPath) = allClips[i];

                if (EditorUtility.DisplayCancelableProgressBar(
                    "Exporting Clips",
                    $"[{i - start + 1}/{end - start}] {clipName}",
                    (float)(i - start) / (end - start)))
                {
                    break;
                }

                var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(assetPath);
                if (clip == null)
                {
                    Debug.LogWarning($"[ClipExporter] Clip not found: {assetPath} (skipping)");
                    skipped++;
                    continue;
                }

                string mp4Path = Path.Combine(outputFolder, clipName + ".mp4");
                ExportClip(instance, clip, cam, rt, tex, tempDir, mp4Path, ffmpeg);
                exported++;
            }
        }
        finally
        {
            EditorUtility.ClearProgressBar();

            DestroyImmediate(floorGo);
            DestroyImmediate(instance);
            DestroyImmediate(camGo);
            cam.targetTexture = null;
            DestroyImmediate(rt);
            DestroyImmediate(tex);

            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }

        Debug.Log($"[ClipExporter] Done. Exported {exported}, skipped {skipped}. Output: {outputFolder}");
        EditorUtility.DisplayDialog("Export Complete",
            $"Exported {exported} clips to:\n{outputFolder}\n\nSkipped: {skipped}", "OK");

        EditorUtility.RevealInFinder(outputFolder);
    }

    void ExportClip(GameObject instance, AnimationClip clip, Camera cam, RenderTexture rt, Texture2D tex, string tempDir, string mp4Path, string ffmpeg)
    {
        if (Directory.Exists(tempDir))
            Directory.Delete(tempDir, true);
        Directory.CreateDirectory(tempDir);

        float duration = clip.length;
        if (duration < 0.1f) duration = 1f;

        int totalFrames = Mathf.CeilToInt(duration * fps);

        for (int f = 0; f < totalFrames; f++)
        {
            float t = (float)f / fps;
            clip.SampleAnimation(instance, t);
            instance.transform.position = Vector3.zero;
            instance.transform.rotation = Quaternion.identity;

            cam.Render();

            RenderTexture.active = rt;
            tex.ReadPixels(new Rect(0, 0, resolution, resolution), 0, 0);
            tex.Apply();
            RenderTexture.active = null;

            byte[] png = tex.EncodeToPNG();
            string framePath = Path.Combine(tempDir, $"frame_{f:D5}.png");
            File.WriteAllBytes(framePath, png);
        }

        var psi = new ProcessStartInfo
        {
            FileName = ffmpeg,
            Arguments = $"-y -framerate {fps} -i \"{Path.Combine(tempDir, "frame_%05d.png")}\" " +
                        $"-c:v libx264 -pix_fmt yuv420p -crf 18 \"{mp4Path}\"",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        using var proc = Process.Start(psi);
        proc.WaitForExit(30000);

        if (proc.ExitCode != 0)
        {
            string err = proc.StandardError.ReadToEnd();
            Debug.LogError($"[ClipExporter] ffmpeg failed for {mp4Path}: {err}");
        }
    }

    static string FindFFmpeg()
    {
        string[] paths = { "/usr/local/bin/ffmpeg", "/opt/homebrew/bin/ffmpeg", "/usr/bin/ffmpeg" };
        foreach (var p in paths)
            if (File.Exists(p)) return p;

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "which",
                Arguments = "ffmpeg",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                CreateNoWindow = true
            };
            using var proc = Process.Start(psi);
            string result = proc.StandardOutput.ReadToEnd().Trim();
            proc.WaitForExit(3000);
            if (File.Exists(result)) return result;
        }
        catch { }

        return null;
    }

    static void SetLayerRecursive(GameObject go, int layer)
    {
        go.layer = layer;
        foreach (Transform child in go.transform)
            SetLayerRecursive(child.gameObject, layer);
    }
}
