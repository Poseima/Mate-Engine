using UnityEngine;

namespace MateEngine.Codex
{
    [CreateAssetMenu(fileName = "SemanticClipRegistry", menuName = "Mate Engine/Semantic Clip Registry")]
    public class SemanticClipRegistry : ScriptableObject
    {
        [System.Serializable]
        public class ClipEntry
        {
            public string id;                   // e.g. "idle_f07", "face_joy"
            public ActionType actionType;
            public string paramName;            // Animator param ("IdleIndex") or blendshape field ("Joy")
            public float paramValue;            // e.g. 6 for idle_f07
            public string boolFlag;             // optional secondary bool ("isDancing")
            public bool boolValue;              // value for boolFlag

            public enum ActionType
            {
                SetFloat,       // Animator.SetFloat
                SetBool,        // Animator.SetBool
                CrossFade,      // Animator.CrossFadeInFixedTime
                BlendShape      // UniversalBlendshapes field
            }
        }

        public ClipEntry[] clips = new ClipEntry[]
        {
            // ── Female Idle (0-19) ─────────────────────────────
            new ClipEntry { id = "idle_f01", actionType = ClipEntry.ActionType.SetFloat, paramName = "IdleIndex", paramValue = 0 },
            new ClipEntry { id = "idle_f02", actionType = ClipEntry.ActionType.SetFloat, paramName = "IdleIndex", paramValue = 1 },
            new ClipEntry { id = "idle_f03", actionType = ClipEntry.ActionType.SetFloat, paramName = "IdleIndex", paramValue = 2 },
            new ClipEntry { id = "idle_f04", actionType = ClipEntry.ActionType.SetFloat, paramName = "IdleIndex", paramValue = 3 },
            new ClipEntry { id = "idle_f05", actionType = ClipEntry.ActionType.SetFloat, paramName = "IdleIndex", paramValue = 4 },
            new ClipEntry { id = "idle_f06", actionType = ClipEntry.ActionType.SetFloat, paramName = "IdleIndex", paramValue = 5 },
            new ClipEntry { id = "idle_f07", actionType = ClipEntry.ActionType.SetFloat, paramName = "IdleIndex", paramValue = 6 },
            new ClipEntry { id = "idle_f08", actionType = ClipEntry.ActionType.SetFloat, paramName = "IdleIndex", paramValue = 7 },
            new ClipEntry { id = "idle_f09", actionType = ClipEntry.ActionType.SetFloat, paramName = "IdleIndex", paramValue = 8 },
            new ClipEntry { id = "idle_f10", actionType = ClipEntry.ActionType.SetFloat, paramName = "IdleIndex", paramValue = 9 },
            new ClipEntry { id = "idle_f11", actionType = ClipEntry.ActionType.SetFloat, paramName = "IdleIndex", paramValue = 10 },
            new ClipEntry { id = "idle_f12", actionType = ClipEntry.ActionType.SetFloat, paramName = "IdleIndex", paramValue = 11 },
            new ClipEntry { id = "idle_f13", actionType = ClipEntry.ActionType.SetFloat, paramName = "IdleIndex", paramValue = 12 },
            new ClipEntry { id = "idle_f14", actionType = ClipEntry.ActionType.SetFloat, paramName = "IdleIndex", paramValue = 13 },
            new ClipEntry { id = "idle_f15", actionType = ClipEntry.ActionType.SetFloat, paramName = "IdleIndex", paramValue = 14 },
            new ClipEntry { id = "idle_f16", actionType = ClipEntry.ActionType.SetFloat, paramName = "IdleIndex", paramValue = 15 },
            new ClipEntry { id = "idle_f17", actionType = ClipEntry.ActionType.SetFloat, paramName = "IdleIndex", paramValue = 16 },
            new ClipEntry { id = "idle_f18", actionType = ClipEntry.ActionType.SetFloat, paramName = "IdleIndex", paramValue = 17 },
            new ClipEntry { id = "idle_f19", actionType = ClipEntry.ActionType.SetFloat, paramName = "IdleIndex", paramValue = 18 },
            new ClipEntry { id = "idle_f20", actionType = ClipEntry.ActionType.SetFloat, paramName = "IdleIndex", paramValue = 19 },

            // ── Male Idle (0-8) ────────────────────────────────
            new ClipEntry { id = "idle_m01", actionType = ClipEntry.ActionType.SetFloat, paramName = "IdleIndex", paramValue = 0, boolFlag = "isMale", boolValue = true },
            new ClipEntry { id = "idle_m02", actionType = ClipEntry.ActionType.SetFloat, paramName = "IdleIndex", paramValue = 1, boolFlag = "isMale", boolValue = true },
            new ClipEntry { id = "idle_m03", actionType = ClipEntry.ActionType.SetFloat, paramName = "IdleIndex", paramValue = 2, boolFlag = "isMale", boolValue = true },
            new ClipEntry { id = "idle_m04", actionType = ClipEntry.ActionType.SetFloat, paramName = "IdleIndex", paramValue = 3, boolFlag = "isMale", boolValue = true },
            new ClipEntry { id = "idle_m05", actionType = ClipEntry.ActionType.SetFloat, paramName = "IdleIndex", paramValue = 4, boolFlag = "isMale", boolValue = true },
            new ClipEntry { id = "idle_m06", actionType = ClipEntry.ActionType.SetFloat, paramName = "IdleIndex", paramValue = 5, boolFlag = "isMale", boolValue = true },
            new ClipEntry { id = "idle_m07", actionType = ClipEntry.ActionType.SetFloat, paramName = "IdleIndex", paramValue = 6, boolFlag = "isMale", boolValue = true },
            new ClipEntry { id = "idle_m08", actionType = ClipEntry.ActionType.SetFloat, paramName = "IdleIndex", paramValue = 7, boolFlag = "isMale", boolValue = true },
            new ClipEntry { id = "idle_m09", actionType = ClipEntry.ActionType.SetFloat, paramName = "IdleIndex", paramValue = 8, boolFlag = "isMale", boolValue = true },

            // ── Female Dance (0-13) ────────────────────────────
            new ClipEntry { id = "dance_f01", actionType = ClipEntry.ActionType.SetFloat, paramName = "DanceIndex", paramValue = 0, boolFlag = "isDancing", boolValue = true },
            new ClipEntry { id = "dance_f02", actionType = ClipEntry.ActionType.SetFloat, paramName = "DanceIndex", paramValue = 1, boolFlag = "isDancing", boolValue = true },
            new ClipEntry { id = "dance_f03", actionType = ClipEntry.ActionType.SetFloat, paramName = "DanceIndex", paramValue = 2, boolFlag = "isDancing", boolValue = true },
            new ClipEntry { id = "dance_f04", actionType = ClipEntry.ActionType.SetFloat, paramName = "DanceIndex", paramValue = 3, boolFlag = "isDancing", boolValue = true },
            new ClipEntry { id = "dance_f05", actionType = ClipEntry.ActionType.SetFloat, paramName = "DanceIndex", paramValue = 4, boolFlag = "isDancing", boolValue = true },
            new ClipEntry { id = "dance_f06", actionType = ClipEntry.ActionType.SetFloat, paramName = "DanceIndex", paramValue = 5, boolFlag = "isDancing", boolValue = true },
            new ClipEntry { id = "dance_f07", actionType = ClipEntry.ActionType.SetFloat, paramName = "DanceIndex", paramValue = 6, boolFlag = "isDancing", boolValue = true },
            new ClipEntry { id = "dance_f08", actionType = ClipEntry.ActionType.SetFloat, paramName = "DanceIndex", paramValue = 7, boolFlag = "isDancing", boolValue = true },
            new ClipEntry { id = "dance_f09", actionType = ClipEntry.ActionType.SetFloat, paramName = "DanceIndex", paramValue = 8, boolFlag = "isDancing", boolValue = true },
            new ClipEntry { id = "dance_f10", actionType = ClipEntry.ActionType.SetFloat, paramName = "DanceIndex", paramValue = 9, boolFlag = "isDancing", boolValue = true },
            new ClipEntry { id = "dance_f11", actionType = ClipEntry.ActionType.SetFloat, paramName = "DanceIndex", paramValue = 10, boolFlag = "isDancing", boolValue = true },
            new ClipEntry { id = "dance_f12", actionType = ClipEntry.ActionType.SetFloat, paramName = "DanceIndex", paramValue = 11, boolFlag = "isDancing", boolValue = true },
            new ClipEntry { id = "dance_f13", actionType = ClipEntry.ActionType.SetFloat, paramName = "DanceIndex", paramValue = 12, boolFlag = "isDancing", boolValue = true },
            new ClipEntry { id = "dance_f14", actionType = ClipEntry.ActionType.SetFloat, paramName = "DanceIndex", paramValue = 13, boolFlag = "isDancing", boolValue = true },

            // ── Male Dance (0-3) ───────────────────────────────
            new ClipEntry { id = "dance_m01", actionType = ClipEntry.ActionType.SetFloat, paramName = "DanceIndex", paramValue = 0, boolFlag = "isDancing", boolValue = true },
            new ClipEntry { id = "dance_m02", actionType = ClipEntry.ActionType.SetFloat, paramName = "DanceIndex", paramValue = 1, boolFlag = "isDancing", boolValue = true },
            new ClipEntry { id = "dance_m03", actionType = ClipEntry.ActionType.SetFloat, paramName = "DanceIndex", paramValue = 2, boolFlag = "isDancing", boolValue = true },
            new ClipEntry { id = "dance_m04", actionType = ClipEntry.ActionType.SetFloat, paramName = "DanceIndex", paramValue = 3, boolFlag = "isDancing", boolValue = true },

            // ── Face expressions (BlendShape) ──────────────────
            new ClipEntry { id = "face_joy",     actionType = ClipEntry.ActionType.BlendShape, paramName = "Joy",     paramValue = 1f },
            new ClipEntry { id = "face_angry",   actionType = ClipEntry.ActionType.BlendShape, paramName = "Angry",   paramValue = 1f },
            new ClipEntry { id = "face_sorrow",  actionType = ClipEntry.ActionType.BlendShape, paramName = "Sorrow",  paramValue = 1f },
            new ClipEntry { id = "face_fun",     actionType = ClipEntry.ActionType.BlendShape, paramName = "Fun",     paramValue = 1f },
            new ClipEntry { id = "face_neutral", actionType = ClipEntry.ActionType.BlendShape, paramName = "Neutral", paramValue = 1f },
        };

        readonly System.Collections.Generic.Dictionary<string, ClipEntry> lookup = new();

        public ClipEntry Find(string id)
        {
            if (lookup.Count == 0 && clips != null)
            {
                foreach (var c in clips)
                    if (!string.IsNullOrEmpty(c.id))
                        lookup[c.id] = c;
            }
            return lookup.TryGetValue(id, out var entry) ? entry : null;
        }
    }
}
