using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace MateEngine.Codex
{
    /// <summary>
    /// Global state stored in ~/.mate-engine/global.json
    /// </summary>
    [Serializable]
    public class GlobalConfig
    {
        public string activeAvatar = "_default";
        public string animationMode = "focus"; // "focus" or "rush"
    }

    /// <summary>
    /// Per-avatar configuration stored in ~/.mate-engine/avatars/{name}/config.json
    /// </summary>
    [Serializable]
    public class AvatarConfig
    {
        public string displayName;
        public string vrmPath;
        public List<string> groups = new List<string>();
        public bool enableAnimationDirectives = true;
        public CodexSettings codex = new CodexSettings();
    }

    /// <summary>
    /// Codex-specific settings within an avatar config
    /// </summary>
    [Serializable]
    public class CodexSettings
    {
        public string model;
        public string modelProvider;

        /// <summary>
        /// Approval policy for tool use / file changes (kebab-case, Codex protocol).
        /// Valid values:
        ///   "never"      - Auto-execute everything, no approval requests generated (default)
        ///   "on-request" - Always ask for approval before executing commands/file changes
        ///   "on-failure" - Ask for approval only when a command fails
        ///   "untrusted"  - Ask for approval unless command is in trusted list
        /// When set to anything other than "never" and a WhatsApp session is active,
        /// approval requests are forwarded as WhatsApp polls automatically.
        /// </summary>
        public string approvalPolicy = "never";

        /// <summary>
        /// Sandbox policy for command execution (kebab-case).
        /// Valid values:
        ///   "workspace-write"    - Write to workspace roots only (default, safest)
        ///   "danger-full-access" - No sandbox (required for browser automation / Playwright)
        ///   "read-only"          - Read-only access
        ///   "external-sandbox"   - Sandbox managed externally
        /// </summary>
        public string sandboxPolicy = "workspace-write";
        public string workingDirectory;

        /// <summary>
        /// Path to base instructions .md file (relative to avatar folder or absolute)
        /// </summary>
        public string baseInstructions;

        /// <summary>
        /// Path to developer instructions .md file (relative to avatar folder or absolute)
        /// </summary>
        public string developerInstructions;
    }

    /// <summary>
    /// Per-avatar runtime state stored in ~/.mate-engine/avatars/{name}/state.json
    /// </summary>
    [Serializable]
    public class AvatarState
    {
        public Dictionary<string, GroupThreadState> threads = new Dictionary<string, GroupThreadState>();
    }

    /// <summary>
    /// Per-group thread state within an avatar
    /// </summary>
    [Serializable]
    public class GroupThreadState
    {
        public string threadId;
        public string lastActive;
    }

    /// <summary>
    /// Merged avatar config with resolved prompt content (not paths)
    /// </summary>
    [Serializable]
    public class ResolvedAvatarConfig
    {
        public string avatarId;
        public string displayName;
        public string vrmPath;
        public List<string> groups = new List<string>();
        public bool enableAnimationDirectives = true;

        // Codex settings
        public string model;
        public string modelProvider;
        public string approvalPolicy = "never";
        public string sandboxPolicy = "workspace-write";
        public string workingDirectory;

        // Resolved prompt content (actual text, not file paths)
        public string baseInstructionsContent;
        public string developerInstructionsContent;
    }
}
