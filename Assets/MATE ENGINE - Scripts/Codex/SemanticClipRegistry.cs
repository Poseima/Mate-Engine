using System.Collections.Generic;
using UnityEngine;

namespace MateEngine.Codex
{
    [CreateAssetMenu(fileName = "SemanticClipRegistry", menuName = "Mate Engine/Semantic Clip Registry")]
    public class SemanticClipRegistry : ScriptableObject
    {
        // ── Existing face-expression entries (BlendShape) ─────────
        [System.Serializable]
        public class ClipEntry
        {
            public string id;
            public ActionType actionType;
            public string paramName;
            public float paramValue;
            public string boolFlag;
            public bool boolValue;

            public enum ActionType
            {
                SetFloat,
                SetBool,
                CrossFade,
                BlendShape
            }
        }

        // ── Blend-tree group (idle, dance) ────────────────────────
        [System.Serializable]
        public class AnimGroup
        {
            public string groupId;
            public string indexParam;        // "IdleIndex" or "DanceIndex"
            public int femaleMaxIndex;       // inclusive upper bound
            public int maleMaxIndex;         // inclusive upper bound
            public string secondaryBool;     // e.g. "isDancing"
            public bool secondaryValue;
            public string[] clearBools;
            public float revertDuration;     // seconds until auto-revert to default (0 = no revert)
        }

        // ── Bool-toggle entry (sit, sleep, walk) ─────────────────
        [System.Serializable]
        public class ToggleEntry
        {
            public string id;
            public string paramName;
            public bool paramValue;
            public string[] clearBools;
            public float revertDuration;     // seconds until auto-revert to default (0 = no revert)
        }

        // ── Clip-override entry (laugh, happy, shy, hide, pose) ──
        [System.Serializable]
        public class OverrideEntry
        {
            public string id;
            public AnimationClip[] clips;
            public string[] clearBools;
        }

        // ── Resolve result ────────────────────────────────────────
        public enum ResolveKind { None, Group, Toggle, Override, Face }

        public struct ResolveResult
        {
            public ResolveKind kind;

            // Group
            public ClipEntry resolvedClip;
            public AnimGroup group;

            // Toggle
            public ToggleEntry toggle;

            // Override
            public OverrideEntry overrideEntry;
            public AnimationClip chosenClip;
        }

        // ── Data ──────────────────────────────────────────────────

        public ClipEntry[] clips = new ClipEntry[]
        {
            new ClipEntry { id = "face_joy",     actionType = ClipEntry.ActionType.BlendShape, paramName = "Joy",     paramValue = 1f },
            new ClipEntry { id = "face_angry",   actionType = ClipEntry.ActionType.BlendShape, paramName = "Angry",   paramValue = 1f },
            new ClipEntry { id = "face_sorrow",  actionType = ClipEntry.ActionType.BlendShape, paramName = "Sorrow",  paramValue = 1f },
            new ClipEntry { id = "face_fun",     actionType = ClipEntry.ActionType.BlendShape, paramName = "Fun",     paramValue = 1f },
            new ClipEntry { id = "face_neutral", actionType = ClipEntry.ActionType.BlendShape, paramName = "Neutral", paramValue = 1f },
        };

        public AnimGroup[] groups = new AnimGroup[]
        {
            new AnimGroup
            {
                groupId = "idle",
                indexParam = "IdleIndex",
                femaleMaxIndex = 19,
                maleMaxIndex = 8,
                clearBools = new[] { "isDancing", "isSitting", "IsSleeping" },
                revertDuration = 12f  // IDLE_SWITCH_TIME — reverts to index 0 (neutral standing)
            },
            new AnimGroup
            {
                groupId = "dance",
                indexParam = "DanceIndex",
                femaleMaxIndex = 12,
                maleMaxIndex = 3,
                secondaryBool = "isDancing",
                secondaryValue = true,
                clearBools = new[] { "isSitting", "IsSleeping" },
                revertDuration = 15f  // DANCE_SWITCH_TIME — reverts isDancing=false, returns to idle
            },
        };

        public ToggleEntry[] toggles = new ToggleEntry[]
        {
            new ToggleEntry { id = "sit",        paramName = "isSitting",  paramValue = true, clearBools = new[] { "isDancing", "IsSleeping" }, revertDuration = 11f },
            new ToggleEntry { id = "sleep",      paramName = "IsSleeping", paramValue = true, clearBools = new[] { "isDancing", "isSitting" }, revertDuration = 35f },
            new ToggleEntry { id = "walk_left",  paramName = "WalkLeft",   paramValue = true, clearBools = new[] { "WalkRight", "isDancing" }, revertDuration = 3f },
            new ToggleEntry { id = "walk_right", paramName = "WalkRight",  paramValue = true, clearBools = new[] { "WalkLeft", "isDancing" }, revertDuration = 3f },
        };

        [Header("Clip-override animations (assign in inspector)")]
        public OverrideEntry[] overrides = new OverrideEntry[]
        {
            new OverrideEntry { id = "laugh", clearBools = new[] { "isDancing", "isSitting", "IsSleeping" } },
            new OverrideEntry { id = "happy", clearBools = new[] { "isDancing", "isSitting", "IsSleeping" } },
            new OverrideEntry { id = "shy",   clearBools = new[] { "isDancing", "isSitting", "IsSleeping" } },
            new OverrideEntry { id = "hide",  clearBools = new[] { "isDancing", "isSitting", "IsSleeping" } },
            new OverrideEntry { id = "pose",  clearBools = new[] { "isDancing", "isSitting", "IsSleeping" } },
        };

        // ── Lookups ───────────────────────────────────────────────

        readonly Dictionary<string, ClipEntry> faceLookup = new();
        readonly Dictionary<string, AnimGroup> groupLookup = new();
        readonly Dictionary<string, ToggleEntry> toggleLookup = new();
        readonly Dictionary<string, OverrideEntry> overrideLookup = new();

        void BuildLookups()
        {
            if (faceLookup.Count > 0) return;

            if (clips != null)
                foreach (var c in clips)
                    if (!string.IsNullOrEmpty(c.id))
                        faceLookup[c.id] = c;

            if (groups != null)
                foreach (var g in groups)
                    if (!string.IsNullOrEmpty(g.groupId))
                        groupLookup[g.groupId] = g;

            if (toggles != null)
                foreach (var t in toggles)
                    if (!string.IsNullOrEmpty(t.id))
                        toggleLookup[t.id] = t;

            if (overrides != null)
                foreach (var o in overrides)
                    if (!string.IsNullOrEmpty(o.id))
                        overrideLookup[o.id] = o;
        }

        // ── Public API ────────────────────────────────────────────

        /// <summary>
        /// Resolve any body directive ID into a typed result.
        /// For face expressions, use Find() instead.
        /// </summary>
        public ResolveResult Resolve(string id, Animator animator)
        {
            BuildLookups();

            // 1. Group (idle, dance) → random index
            if (groupLookup.TryGetValue(id, out var grp))
            {
                bool isMale = animator != null && animator.GetFloat("isMale") >= 0.5f;
                int max = isMale ? grp.maleMaxIndex : grp.femaleMaxIndex;
                int idx = Random.Range(0, max + 1);

                var entry = new ClipEntry
                {
                    id = id,
                    actionType = ClipEntry.ActionType.SetFloat,
                    paramName = grp.indexParam,
                    paramValue = idx
                };

                if (!string.IsNullOrEmpty(grp.secondaryBool))
                {
                    entry.boolFlag = grp.secondaryBool;
                    entry.boolValue = grp.secondaryValue;
                }

                return new ResolveResult { kind = ResolveKind.Group, resolvedClip = entry, group = grp };
            }

            // 2. Toggle (sit, sleep, talk, walk)
            if (toggleLookup.TryGetValue(id, out var tog))
                return new ResolveResult { kind = ResolveKind.Toggle, toggle = tog };

            // 3. Override (laugh, happy, shy, hide, pose)
            if (overrideLookup.TryGetValue(id, out var ov))
            {
                AnimationClip chosen = null;
                if (ov.clips != null && ov.clips.Length > 0)
                    chosen = ov.clips[Random.Range(0, ov.clips.Length)];

                return new ResolveResult { kind = ResolveKind.Override, overrideEntry = ov, chosenClip = chosen };
            }

            return new ResolveResult { kind = ResolveKind.None };
        }

        /// <summary>
        /// Look up a face expression by ID. Unchanged from original.
        /// </summary>
        public ClipEntry Find(string id)
        {
            BuildLookups();
            return faceLookup.TryGetValue(id, out var entry) ? entry : null;
        }
    }
}
