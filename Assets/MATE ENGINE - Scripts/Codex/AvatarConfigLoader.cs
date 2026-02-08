using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace MateEngine.Codex
{
    /// <summary>
    /// Loads and manages avatar configurations from ~/.mate-engine/avatars/
    /// Provides group→avatar lookup and config merging.
    /// </summary>
    public class AvatarConfigLoader
    {
        public static AvatarConfigLoader Instance { get; private set; }

        private readonly string _baseDir;
        private readonly string _avatarsDir;
        private readonly string _globalConfigPath;

        private GlobalConfig _globalConfig;
        private Dictionary<string, ResolvedAvatarConfig> _avatarConfigs = new Dictionary<string, ResolvedAvatarConfig>();
        private Dictionary<string, AvatarState> _avatarStates = new Dictionary<string, AvatarState>();
        private Dictionary<string, string> _groupToAvatarIndex = new Dictionary<string, string>();
        private Dictionary<string, string> _vrmPathToAvatarIndex = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        // Hot-reload
        private FileSystemWatcher _watcher;
        private readonly ConcurrentQueue<string> _pendingReloads = new();
        private float _lastChangeTime;
        private bool _reloadPending;
        private const float DebounceSeconds = 0.3f;
        private bool _suppressWatcher; // suppress watcher during our own writes

        public event Action<string> OnAvatarConfigChanged;
        public GlobalConfig GlobalConfig => _globalConfig;
        public IReadOnlyDictionary<string, ResolvedAvatarConfig> AvatarConfigs => _avatarConfigs;

        public AvatarConfigLoader(string baseDir = null)
        {
            _baseDir = baseDir ?? GetDefaultBaseDir();
            _avatarsDir = Path.Combine(_baseDir, "avatars");
            _globalConfigPath = Path.Combine(_baseDir, "global.json");

            Instance = this;
        }

        private static string GetDefaultBaseDir()
        {
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return Path.Combine(home, ".mate-engine");
        }

        /// <summary>
        /// Load all configs. Call this on startup and when configs may have changed.
        /// </summary>
        public void LoadAll()
        {
            Debug.Log("[AvatarConfig] Loading all avatar configurations...");
            EnsureDirectoryStructure();
            LoadGlobalConfig();
            LoadAllAvatarConfigs();
            BuildGroupIndex();
            BuildVrmPathIndex();
            Debug.Log($"[AvatarConfig] Loaded {_avatarConfigs.Count} avatar(s), active: '{_globalConfig.activeAvatar}', mode: '{_globalConfig.animationMode}'");
        }

        /// <summary>
        /// Reload just the global config (for watching changes)
        /// </summary>
        public void ReloadGlobalConfig()
        {
            LoadGlobalConfig();
        }

        /// <summary>
        /// Get the avatar that owns a given group, or null if unassigned
        /// </summary>
        public string GetAvatarForGroup(string groupFolder)
        {
            if (_groupToAvatarIndex.TryGetValue(groupFolder, out string avatarId))
                return avatarId;
            return null;
        }

        /// <summary>
        /// Get resolved config for an avatar
        /// </summary>
        public ResolvedAvatarConfig GetAvatarConfig(string avatarId)
        {
            if (_avatarConfigs.TryGetValue(avatarId, out var config))
                return config;

            if (_avatarConfigs.TryGetValue("_default", out var defaultConfig))
                return defaultConfig;

            return null;
        }

        /// <summary>
        /// Get or create state for an avatar
        /// </summary>
        public AvatarState GetAvatarState(string avatarId)
        {
            if (!_avatarStates.TryGetValue(avatarId, out var state))
            {
                state = LoadAvatarState(avatarId);
                _avatarStates[avatarId] = state;
            }
            return state;
        }

        /// <summary>
        /// Save avatar state to disk
        /// </summary>
        public void SaveAvatarState(string avatarId)
        {
            if (!_avatarStates.TryGetValue(avatarId, out var state))
                return;

            string statePath = Path.Combine(_avatarsDir, avatarId, "state.json");
            try
            {
                string json = JsonConvert.SerializeObject(state, Formatting.Indented);
                File.WriteAllText(statePath, json);
            }
            catch (Exception e)
            {
                Debug.LogError($"[AvatarConfigLoader] Failed to save state for {avatarId}: {e.Message}");
            }
        }

        /// <summary>
        /// Update thread ID for a group and save state
        /// </summary>
        public void UpdateThreadId(string avatarId, string groupFolder, string threadId)
        {
            var state = GetAvatarState(avatarId);
            if (!state.threads.ContainsKey(groupFolder))
                state.threads[groupFolder] = new GroupThreadState();

            state.threads[groupFolder].threadId = threadId;
            state.threads[groupFolder].lastActive = DateTime.UtcNow.ToString("o");
            SaveAvatarState(avatarId);
        }

        /// <summary>
        /// Get thread ID for a group, or null if none exists
        /// </summary>
        public string GetThreadId(string avatarId, string groupFolder)
        {
            var state = GetAvatarState(avatarId);
            if (state.threads.TryGetValue(groupFolder, out var threadState))
                return threadState.threadId;
            return null;
        }

        /// <summary>
        /// Get the currently active avatar's resolved config.
        /// </summary>
        public ResolvedAvatarConfig GetActiveAvatarConfig()
        {
            return GetAvatarConfig(_globalConfig?.activeAvatar ?? "_default");
        }

        /// <summary>
        /// Update a top-level field on the active avatar's config.json and save to disk.
        /// </summary>
        public void UpdateActiveAvatarField(string field, object value)
        {
            string avatarId = _globalConfig?.activeAvatar ?? "_default";
            string configPath = Path.Combine(_avatarsDir, avatarId, "config.json");

            if (!File.Exists(configPath))
            {
                Debug.LogWarning($"[AvatarConfigLoader] Config not found for active avatar '{avatarId}'");
                return;
            }

            try
            {
                string json = File.ReadAllText(configPath);
                var obj = JObject.Parse(json);
                obj[field] = value != null ? JToken.FromObject(value) : JValue.CreateNull();

                _suppressWatcher = true;
                File.WriteAllText(configPath, obj.ToString(Formatting.Indented));
                _suppressWatcher = false;

                ReloadSingleAvatar(avatarId);
                Debug.Log($"[AvatarConfigLoader] Updated '{avatarId}' {field} = {value}");
            }
            catch (Exception e)
            {
                _suppressWatcher = false;
                Debug.LogError($"[AvatarConfigLoader] Failed to update field '{field}' for '{avatarId}': {e.Message}");
            }
        }

        /// <summary>
        /// Update a codex field on the active avatar's config.json and save to disk.
        /// The hot-reload watcher will pick up the change.
        /// </summary>
        public void UpdateActiveAvatarCodexField(string field, object value)
        {
            string avatarId = _globalConfig?.activeAvatar ?? "_default";
            string configPath = Path.Combine(_avatarsDir, avatarId, "config.json");

            if (!File.Exists(configPath))
            {
                Debug.LogWarning($"[AvatarConfigLoader] Config not found for active avatar '{avatarId}'");
                return;
            }

            try
            {
                string json = File.ReadAllText(configPath);
                var obj = JObject.Parse(json);

                var codex = obj["codex"] as JObject;
                if (codex == null)
                {
                    codex = new JObject();
                    obj["codex"] = codex;
                }

                codex[field] = value != null ? JToken.FromObject(value) : JValue.CreateNull();

                _suppressWatcher = true;
                File.WriteAllText(configPath, obj.ToString(Formatting.Indented));
                _suppressWatcher = false;

                // Immediately reload this avatar
                ReloadSingleAvatar(avatarId);
                Debug.Log($"[AvatarConfigLoader] Updated '{avatarId}' codex.{field} = {value}");
            }
            catch (Exception e)
            {
                _suppressWatcher = false;
                Debug.LogError($"[AvatarConfigLoader] Failed to update field '{field}' for '{avatarId}': {e.Message}");
            }
        }

        /// <summary>
        /// Start watching config files for hot-reload.
        /// </summary>
        public void StartWatching()
        {
            if (_watcher != null) return;

            try
            {
                _watcher = new FileSystemWatcher(_avatarsDir)
                {
                    Filter = "*.json",
                    IncludeSubdirectories = true,
                    NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.CreationTime
                };
                _watcher.Changed += OnFileChanged;
                _watcher.Created += OnFileChanged;
                _watcher.EnableRaisingEvents = true;
                Debug.Log("[AvatarConfigLoader] Hot-reload watcher started on: " + _avatarsDir);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[AvatarConfigLoader] Failed to start file watcher: {e.Message}");
            }
        }

        /// <summary>
        /// Stop watching config files.
        /// </summary>
        public void StopWatching()
        {
            if (_watcher != null)
            {
                _watcher.EnableRaisingEvents = false;
                _watcher.Dispose();
                _watcher = null;
            }
        }

        private void OnFileChanged(object sender, FileSystemEventArgs e)
        {
            if (_suppressWatcher) return;

            // Only care about config.json files (not state.json)
            if (Path.GetFileName(e.FullPath) != "config.json" &&
                Path.GetFileName(e.FullPath) != "global.json")
                return;

            _pendingReloads.Enqueue(e.FullPath);
        }

        /// <summary>
        /// Call from MonoBehaviour.Update() to process pending hot-reload events on the main thread.
        /// Uses debouncing to avoid reloading mid-write.
        /// </summary>
        public void PumpChanges()
        {
            // Drain the queue and mark reload pending
            while (_pendingReloads.TryDequeue(out _))
            {
                _reloadPending = true;
                _lastChangeTime = Time.unscaledTime;
            }

            // Wait for debounce period
            if (_reloadPending && Time.unscaledTime - _lastChangeTime >= DebounceSeconds)
            {
                _reloadPending = false;
                Debug.Log("[AvatarConfigLoader] Hot-reloading configs...");
                LoadAll();
                OnAvatarConfigChanged?.Invoke(_globalConfig?.activeAvatar ?? "_default");
            }
        }

        /// <summary>
        /// Reload a single avatar's config without rebuilding all indexes.
        /// </summary>
        private void ReloadSingleAvatar(string avatarId)
        {
            // Find existing default config for merging
            ResolvedAvatarConfig defaultConfig = null;
            if (avatarId != "_default")
                _avatarConfigs.TryGetValue("_default", out defaultConfig);

            var updated = LoadAndResolveAvatarConfig(avatarId, defaultConfig);
            if (updated != null)
            {
                _avatarConfigs[avatarId] = updated;
                BuildGroupIndex();
                BuildVrmPathIndex();
            }
        }

        private void EnsureDirectoryStructure()
        {
            if (!Directory.Exists(_baseDir))
                Directory.CreateDirectory(_baseDir);

            if (!Directory.Exists(_avatarsDir))
                Directory.CreateDirectory(_avatarsDir);

            string defaultDir = Path.Combine(_avatarsDir, "_default");
            if (!Directory.Exists(defaultDir))
            {
                Directory.CreateDirectory(defaultDir);
                CreateDefaultConfigs(defaultDir);
            }

            if (!File.Exists(_globalConfigPath))
            {
                var defaultGlobal = new GlobalConfig();
                string json = JsonConvert.SerializeObject(defaultGlobal, Formatting.Indented);
                File.WriteAllText(_globalConfigPath, json);
            }
        }

        private void CreateDefaultConfigs(string defaultDir)
        {
            // Create default config.json
            var defaultConfig = new AvatarConfig
            {
                displayName = "Default Assistant",
                enableAnimationDirectives = true,
                codex = new CodexSettings
                {
                    approvalPolicy = "never",
                    sandboxPolicy = "workspace-write",
                    baseInstructions = "./base-prompt.md",
                    developerInstructions = "./developer-prompt.md"
                }
            };

            string configJson = JsonConvert.SerializeObject(defaultConfig, Formatting.Indented);
            File.WriteAllText(Path.Combine(defaultDir, "config.json"), configJson);

            // Create default base-prompt.md
            string basePrompt = @"# Virtual Assistant

You are a helpful virtual assistant.

## Guidelines
- Be friendly and helpful
- Provide clear, concise responses
- Ask for clarification when needed
";
            File.WriteAllText(Path.Combine(defaultDir, "base-prompt.md"), basePrompt);

            // Create default developer-prompt.md
            string devPrompt = @"## Response Style
- Keep responses concise unless detail is requested
- Use markdown formatting when helpful
";
            File.WriteAllText(Path.Combine(defaultDir, "developer-prompt.md"), devPrompt);

            Debug.Log("[AvatarConfigLoader] Created default config files at: " + defaultDir);
        }

        private void LoadGlobalConfig()
        {
            try
            {
                if (File.Exists(_globalConfigPath))
                {
                    string json = File.ReadAllText(_globalConfigPath);
                    _globalConfig = JsonConvert.DeserializeObject<GlobalConfig>(json) ?? new GlobalConfig();
                }
                else
                {
                    _globalConfig = new GlobalConfig();
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"[AvatarConfigLoader] Failed to load global config: {e.Message}");
                _globalConfig = new GlobalConfig();
            }
        }

        private void LoadAllAvatarConfigs()
        {
            _avatarConfigs.Clear();

            if (!Directory.Exists(_avatarsDir))
                return;

            // First load _default config
            ResolvedAvatarConfig defaultConfig = null;
            string defaultDir = Path.Combine(_avatarsDir, "_default");
            if (Directory.Exists(defaultDir))
            {
                defaultConfig = LoadAndResolveAvatarConfig("_default", null);
                if (defaultConfig != null)
                    _avatarConfigs["_default"] = defaultConfig;
            }

            // Load all other avatar configs
            foreach (var dir in Directory.GetDirectories(_avatarsDir))
            {
                string avatarId = Path.GetFileName(dir);
                if (avatarId == "_default")
                    continue;

                var config = LoadAndResolveAvatarConfig(avatarId, defaultConfig);
                if (config != null)
                    _avatarConfigs[avatarId] = config;
            }
        }

        private ResolvedAvatarConfig LoadAndResolveAvatarConfig(string avatarId, ResolvedAvatarConfig defaultConfig)
        {
            string avatarDir = Path.Combine(_avatarsDir, avatarId);
            string configPath = Path.Combine(avatarDir, "config.json");

            if (!File.Exists(configPath))
                return null;

            try
            {
                string json = File.ReadAllText(configPath);
                var rawConfig = JsonConvert.DeserializeObject<AvatarConfig>(json);

                // Merge with default
                var resolved = MergeWithDefault(avatarId, rawConfig, defaultConfig);

                // Resolve prompt file paths
                resolved.baseInstructionsContent = LoadPromptFile(avatarDir, rawConfig.codex?.baseInstructions);
                resolved.developerInstructionsContent = LoadPromptFile(avatarDir, rawConfig.codex?.developerInstructions);

                return resolved;
            }
            catch (Exception e)
            {
                Debug.LogError($"[AvatarConfigLoader] Failed to load config for {avatarId}: {e.Message}");
                return null;
            }
        }

        private ResolvedAvatarConfig MergeWithDefault(string avatarId, AvatarConfig raw, ResolvedAvatarConfig defaultConfig)
        {
            var resolved = new ResolvedAvatarConfig
            {
                avatarId = avatarId
            };

            // If we have a default config, start with its values
            if (defaultConfig != null)
            {
                resolved.displayName = defaultConfig.displayName;
                resolved.vrmPath = defaultConfig.vrmPath;
                resolved.groups = new List<string>(defaultConfig.groups);
                resolved.enableAnimationDirectives = defaultConfig.enableAnimationDirectives;
                resolved.model = defaultConfig.model;
                resolved.modelProvider = defaultConfig.modelProvider;
                resolved.approvalPolicy = defaultConfig.approvalPolicy;
                resolved.sandboxPolicy = defaultConfig.sandboxPolicy;
                resolved.workingDirectory = defaultConfig.workingDirectory;
                resolved.baseInstructionsContent = defaultConfig.baseInstructionsContent;
                resolved.developerInstructionsContent = defaultConfig.developerInstructionsContent;
            }

            // Override with raw values if present
            if (!string.IsNullOrEmpty(raw.displayName))
                resolved.displayName = raw.displayName;

            if (!string.IsNullOrEmpty(raw.vrmPath))
                resolved.vrmPath = raw.vrmPath;

            if (raw.groups != null && raw.groups.Count > 0)
                resolved.groups = new List<string>(raw.groups);

            resolved.enableAnimationDirectives = raw.enableAnimationDirectives;

            if (raw.codex != null)
            {
                if (!string.IsNullOrEmpty(raw.codex.model))
                    resolved.model = raw.codex.model;

                if (!string.IsNullOrEmpty(raw.codex.modelProvider))
                    resolved.modelProvider = raw.codex.modelProvider;

                if (!string.IsNullOrEmpty(raw.codex.approvalPolicy))
                    resolved.approvalPolicy = raw.codex.approvalPolicy;

                if (!string.IsNullOrEmpty(raw.codex.sandboxPolicy))
                    resolved.sandboxPolicy = raw.codex.sandboxPolicy;

                if (!string.IsNullOrEmpty(raw.codex.workingDirectory))
                    resolved.workingDirectory = raw.codex.workingDirectory;
            }

            return resolved;
        }

        private string LoadPromptFile(string avatarDir, string promptPath)
        {
            if (string.IsNullOrEmpty(promptPath))
                return null;

            string fullPath;
            if (promptPath.StartsWith("./"))
            {
                // Relative to avatar directory
                fullPath = Path.Combine(avatarDir, promptPath.Substring(2));
            }
            else if (Path.IsPathRooted(promptPath))
            {
                // Absolute path
                fullPath = promptPath;
            }
            else
            {
                // Assume relative to avatar directory
                fullPath = Path.Combine(avatarDir, promptPath);
            }

            // Try avatar's own file first
            if (File.Exists(fullPath))
            {
                try
                {
                    return File.ReadAllText(fullPath);
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[AvatarConfigLoader] Failed to read prompt file {fullPath}: {e.Message}");
                }
            }

            // Fall back to _default folder
            string defaultPath = Path.Combine(_avatarsDir, "_default", Path.GetFileName(promptPath));
            if (File.Exists(defaultPath))
            {
                try
                {
                    return File.ReadAllText(defaultPath);
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[AvatarConfigLoader] Failed to read default prompt file {defaultPath}: {e.Message}");
                }
            }

            return null;
        }

        private AvatarState LoadAvatarState(string avatarId)
        {
            string statePath = Path.Combine(_avatarsDir, avatarId, "state.json");

            if (File.Exists(statePath))
            {
                try
                {
                    string json = File.ReadAllText(statePath);
                    return JsonConvert.DeserializeObject<AvatarState>(json) ?? new AvatarState();
                }
                catch
                {
                    return new AvatarState();
                }
            }

            return new AvatarState();
        }

        private void BuildGroupIndex()
        {
            _groupToAvatarIndex.Clear();

            foreach (var kvp in _avatarConfigs)
            {
                string avatarId = kvp.Key;
                var config = kvp.Value;

                if (config.groups == null)
                    continue;

                foreach (var group in config.groups)
                {
                    if (_groupToAvatarIndex.ContainsKey(group))
                    {
                        Debug.LogWarning($"[AvatarConfigLoader] Group '{group}' is bound to multiple avatars. Using first: {_groupToAvatarIndex[group]}");
                        continue;
                    }
                    _groupToAvatarIndex[group] = avatarId;
                }
            }

            Debug.Log($"[AvatarConfig] Built group index: {_groupToAvatarIndex.Count} group(s)");
        }

        private void BuildVrmPathIndex()
        {
            _vrmPathToAvatarIndex.Clear();

            foreach (var kvp in _avatarConfigs)
            {
                string avatarId = kvp.Key;
                var config = kvp.Value;

                if (string.IsNullOrEmpty(config.vrmPath))
                    continue;

                // Normalize path for matching
                string normalizedPath = NormalizePath(config.vrmPath);

                if (_vrmPathToAvatarIndex.ContainsKey(normalizedPath))
                {
                    Debug.LogWarning($"[AvatarConfig] VRM path '{config.vrmPath}' is used by multiple avatars. Using first: {_vrmPathToAvatarIndex[normalizedPath]}");
                    continue;
                }
                _vrmPathToAvatarIndex[normalizedPath] = avatarId;
                Debug.Log($"[AvatarConfig] Mapped VRM '{Path.GetFileName(config.vrmPath)}' → avatar '{avatarId}'");
            }

            Debug.Log($"[AvatarConfig] Built VRM path index: {_vrmPathToAvatarIndex.Count} mapping(s)");
        }

        private string NormalizePath(string path)
        {
            if (string.IsNullOrEmpty(path))
                return path;

            // Expand ~ to home directory
            if (path.StartsWith("~/"))
            {
                string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                path = Path.Combine(home, path.Substring(2));
            }

            // Get full path and normalize separators
            try
            {
                return Path.GetFullPath(path).Replace('\\', '/');
            }
            catch
            {
                return path.Replace('\\', '/');
            }
        }

        /// <summary>
        /// Find avatar config by VRM file path. Used when user switches avatar in UI.
        /// </summary>
        /// <param name="vrmPath">Path to the VRM file being loaded</param>
        /// <returns>Avatar ID if found, "_default" otherwise</returns>
        public string FindAvatarByVrmPath(string vrmPath)
        {
            if (string.IsNullOrEmpty(vrmPath))
            {
                Debug.Log("[AvatarConfig] FindAvatarByVrmPath: empty path, using '_default'");
                return "_default";
            }

            string normalizedPath = NormalizePath(vrmPath);

            // Try exact match first
            if (_vrmPathToAvatarIndex.TryGetValue(normalizedPath, out string avatarId))
            {
                Debug.Log($"[AvatarConfig] FindAvatarByVrmPath: '{Path.GetFileName(vrmPath)}' → '{avatarId}' (exact match)");
                return avatarId;
            }

            // Try matching by filename only (for flexibility)
            string filename = Path.GetFileName(vrmPath);
            foreach (var kvp in _avatarConfigs)
            {
                if (string.IsNullOrEmpty(kvp.Value.vrmPath))
                    continue;

                if (Path.GetFileName(kvp.Value.vrmPath).Equals(filename, StringComparison.OrdinalIgnoreCase))
                {
                    Debug.Log($"[AvatarConfig] FindAvatarByVrmPath: '{filename}' → '{kvp.Key}' (filename match)");
                    return kvp.Key;
                }
            }

            Debug.Log($"[AvatarConfig] FindAvatarByVrmPath: '{Path.GetFileName(vrmPath)}' → '_default' (no match)");
            return "_default";
        }

        /// <summary>
        /// Called when user switches avatar in Unity UI. Updates activeAvatar and saves.
        /// Auto-creates config folder if no matching config exists.
        /// </summary>
        /// <param name="vrmPath">Path to the VRM file that was loaded</param>
        /// <param name="displayName">Optional display name for auto-created config</param>
        public void OnAvatarChanged(string vrmPath, string displayName = null)
        {
            Debug.Log($"[AvatarConfig] OnAvatarChanged called: vrmPath='{vrmPath}', displayName='{displayName}'");

            string oldAvatar = _globalConfig.activeAvatar;
            string newAvatar = FindAvatarByVrmPath(vrmPath);

            Debug.Log($"[AvatarConfig] FindAvatarByVrmPath result: '{newAvatar}' (current active: '{oldAvatar}')");

            // Auto-create config if using default and we have a valid path
            if (newAvatar == "_default" && !string.IsNullOrEmpty(vrmPath))
            {
                Debug.Log($"[AvatarConfig] Attempting auto-create for vrmPath='{vrmPath}', displayName='{displayName}'");
                newAvatar = AutoCreateAvatarConfig(vrmPath, displayName);
                Debug.Log($"[AvatarConfig] Auto-create result: '{newAvatar}'");
            }

            if (oldAvatar != newAvatar)
            {
                Debug.Log($"[AvatarConfig] Avatar switch: '{oldAvatar}' → '{newAvatar}'");
                SetActiveAvatar(newAvatar);
            }
            else
            {
                Debug.Log($"[AvatarConfig] Avatar unchanged: '{newAvatar}'");
            }
        }

        /// <summary>
        /// Auto-create an avatar config folder for a VRM file.
        /// </summary>
        private string AutoCreateAvatarConfig(string vrmPath, string displayName = null)
        {
            // Generate avatar ID from filename if no display name provided
            string filename = Path.GetFileNameWithoutExtension(vrmPath);
            string avatarId = SanitizeAvatarId(displayName ?? filename);

            Debug.Log($"[AvatarConfig] AutoCreateAvatarConfig: filename='{filename}', displayName='{displayName}', avatarId='{avatarId}'");

            // Don't overwrite existing configs
            if (_avatarConfigs.ContainsKey(avatarId))
            {
                Debug.Log($"[AvatarConfig] Config for '{avatarId}' already exists in memory, skipping auto-create");
                return avatarId;
            }

            string avatarDir = Path.Combine(_avatarsDir, avatarId);
            string configPath = Path.Combine(avatarDir, "config.json");

            try
            {
                Directory.CreateDirectory(avatarDir);

                var config = new AvatarConfig
                {
                    displayName = displayName ?? filename,
                    vrmPath = vrmPath,
                    enableAnimationDirectives = true,
                    codex = new CodexSettings
                    {
                        approvalPolicy = "never",
                        sandboxPolicy = "workspace-write",
                        baseInstructions = "./base-prompt.md",
                        developerInstructions = "./developer-prompt.md"
                    }
                };

                string json = JsonConvert.SerializeObject(config, Formatting.Indented);
                File.WriteAllText(configPath, json);

                Debug.Log($"[AvatarConfig] Auto-created config for '{avatarId}' at {avatarDir}");

                // Reload configs to pick up the new one
                LoadAllAvatarConfigs();
                BuildGroupIndex();
                BuildVrmPathIndex();

                return avatarId;
            }
            catch (Exception e)
            {
                Debug.LogError($"[AvatarConfig] Failed to auto-create config for '{avatarId}': {e.Message}");
                return "_default";
            }
        }

        /// <summary>
        /// Sanitize a string to be a valid avatar ID (folder name).
        /// </summary>
        private string SanitizeAvatarId(string name)
        {
            if (string.IsNullOrEmpty(name))
                return "unknown";

            // Lowercase and replace invalid chars with underscore
            var sb = new System.Text.StringBuilder();
            foreach (char c in name.ToLowerInvariant())
            {
                if (char.IsLetterOrDigit(c) || c == '-' || c == '_')
                    sb.Append(c);
                else if (c == ' ')
                    sb.Append('_');
            }

            string result = sb.ToString().Trim('_');
            return string.IsNullOrEmpty(result) ? "unknown" : result;
        }

        /// <summary>
        /// Save the global config to disk
        /// </summary>
        public void SaveGlobalConfig()
        {
            try
            {
                string json = JsonConvert.SerializeObject(_globalConfig, Formatting.Indented);
                File.WriteAllText(_globalConfigPath, json);
            }
            catch (Exception e)
            {
                Debug.LogError($"[AvatarConfigLoader] Failed to save global config: {e.Message}");
            }
        }

        /// <summary>
        /// Set the active avatar and save
        /// </summary>
        public void SetActiveAvatar(string avatarId)
        {
            string oldAvatar = _globalConfig.activeAvatar;
            _globalConfig.activeAvatar = avatarId;
            SaveGlobalConfig();
            Debug.Log($"[AvatarConfig] Active avatar set: '{avatarId}' (was: '{oldAvatar}')");
        }

        /// <summary>
        /// Set the animation mode and save
        /// </summary>
        public void SetAnimationMode(string mode)
        {
            string oldMode = _globalConfig.animationMode;
            _globalConfig.animationMode = mode;
            SaveGlobalConfig();
            Debug.Log($"[AvatarConfig] Animation mode set: '{mode}' (was: '{oldMode}')");
        }
    }
}
