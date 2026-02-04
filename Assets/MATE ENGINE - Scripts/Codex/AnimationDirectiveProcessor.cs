using System;
using System.Collections;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace MateEngine.Codex
{
    public class AnimationDirectiveProcessor
    {
        static readonly Regex DirectiveRegex = new Regex(
            @"<!--anim:(.*?)-->",
            RegexOptions.Singleline | RegexOptions.Compiled
        );

        Animator animator;
        UniversalBlendshapes blendshapes;
        SemanticClipRegistry registry;
        AnimatorOverrideController overrideController;

        // The Custom Dance state uses a placeholder clip we can override at runtime.
        const string ProxyStateName = "Custom Dance";
        const string PlaceholderClipName = "CUSTOM_DANCE";

        string lastProcessedRaw = "";
        int lastDirectiveEnd;

        public void SetTargets(Animator animator, UniversalBlendshapes blendshapes)
        {
            this.animator = animator;
            this.blendshapes = blendshapes;

            if (registry == null)
                registry = Resources.Load<SemanticClipRegistry>("SemanticClipRegistry");

            // Reuse existing override controller if AvatarDancePlayer already created one,
            // otherwise wrap the current controller.
            if (animator != null && animator.runtimeAnimatorController != null)
            {
                if (animator.runtimeAnimatorController is AnimatorOverrideController existing)
                    overrideController = existing;
                else
                {
                    overrideController = new AnimatorOverrideController(animator.runtimeAnimatorController);
                    animator.runtimeAnimatorController = overrideController;
                }
            }
        }

        /// <summary>
        /// Process the full accumulated stream buffer. Strips directives,
        /// applies them, and returns clean text for display.
        /// </summary>
        public string ProcessText(string fullBuffer, MonoBehaviour coroutineHost)
        {
            if (string.IsNullOrEmpty(fullBuffer))
                return fullBuffer;

            var matches = DirectiveRegex.Matches(fullBuffer);
            foreach (Match m in matches)
            {
                if (m.Index + m.Length > lastDirectiveEnd)
                {
                    Debug.Log("[AnimDirective] Processing directive at pos " + m.Index + ": " + m.Groups[1].Value);
                    lastDirectiveEnd = m.Index + m.Length;
                    ApplyDirective(m.Groups[1].Value, coroutineHost);
                }
            }

            string clean = DirectiveRegex.Replace(fullBuffer, "").TrimStart('\n', '\r');
            return clean;
        }

        public void Reset()
        {
            lastProcessedRaw = "";
            lastDirectiveEnd = 0;
        }

        void ApplyDirective(string json, MonoBehaviour host)
        {
            try
            {
                var obj = JObject.Parse(json);

                string bodyId = obj["body"]?.Value<string>();
                string faceId = obj["face"]?.Value<string>();

                Debug.Log("[AnimDirective] Parsed → body=" + (bodyId ?? "null") + " face=" + (faceId ?? "null"));

                if (!string.IsNullOrEmpty(bodyId))
                    ApplyBodyClip(bodyId, host);

                if (!string.IsNullOrEmpty(faceId))
                    ApplyFaceClip(faceId);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[AnimDirective] Failed to parse directive: " + e.Message);
            }
        }

        void ApplyBodyClip(string clipId, MonoBehaviour host)
        {
            if (animator == null || !animator.isActiveAndEnabled)
            {
                Debug.LogWarning("[AnimDirective] Animator null or inactive, skipping body clip: " + clipId);
                return;
            }

            var result = registry?.Resolve(clipId, animator) ?? default;

            switch (result.kind)
            {
                case SemanticClipRegistry.ResolveKind.Group:
                    ApplyGroup(result, host);
                    break;

                case SemanticClipRegistry.ResolveKind.Toggle:
                    ApplyToggle(result.toggle, host);
                    break;

                case SemanticClipRegistry.ResolveKind.Override:
                    ApplyOverride(result, host);
                    break;

                default:
                    Debug.LogWarning("[AnimDirective] Unknown body clip: " + clipId);
                    break;
            }
        }

        // ── Group (idle, dance) ───────────────────────────────────
        // Auto-reverts to neutral idle (index 0) after revertDuration.
        // The default AvatarAnimatorController then resumes its own cycling.

        void ApplyGroup(SemanticClipRegistry.ResolveResult result, MonoBehaviour host)
        {
            var grp = result.group;
            var entry = result.resolvedClip;

            ClearBools(grp.clearBools);

            animator.SetFloat(entry.paramName, entry.paramValue);
            if (!string.IsNullOrEmpty(entry.boolFlag))
                animator.SetBool(entry.boolFlag, entry.boolValue);

            Debug.Log("[AnimDirective] Group '" + grp.groupId + "' → " + entry.paramName + "=" + entry.paramValue + " (revert in " + grp.revertDuration + "s)");

            if (grp.revertDuration > 0f && host != null)
                host.StartCoroutine(RevertGroupAfter(grp.revertDuration, entry, grp));
        }

        IEnumerator RevertGroupAfter(float seconds, SemanticClipRegistry.ClipEntry entry, SemanticClipRegistry.AnimGroup grp)
        {
            yield return new WaitForSeconds(seconds);
            if (animator == null || !animator.isActiveAndEnabled) yield break;

            // Return to neutral idle pose (index 0)
            animator.SetFloat(entry.paramName, 0f);
            if (!string.IsNullOrEmpty(grp.secondaryBool))
                animator.SetBool(grp.secondaryBool, false);
        }

        // ── Toggle (sit, sleep, walk) ─────────────────────────────
        // Auto-reverts after revertDuration, returning to idle state.

        void ApplyToggle(SemanticClipRegistry.ToggleEntry tog, MonoBehaviour host)
        {
            ClearBools(tog.clearBools);
            animator.SetBool(tog.paramName, tog.paramValue);

            Debug.Log("[AnimDirective] Toggle '" + tog.id + "' → " + tog.paramName + "=" + tog.paramValue + " (revert in " + tog.revertDuration + "s)");

            if (tog.revertDuration > 0f && host != null)
                host.StartCoroutine(RevertToggleAfter(tog.revertDuration, tog));
        }

        IEnumerator RevertToggleAfter(float seconds, SemanticClipRegistry.ToggleEntry tog)
        {
            yield return new WaitForSeconds(seconds);
            if (animator == null || !animator.isActiveAndEnabled) yield break;
            animator.SetBool(tog.paramName, false);
        }

        // ── Override (laugh, happy, shy, hide, pose) ──────────────
        // Auto-revert after clip.length seconds — plays once then returns to idle.

        void ApplyOverride(SemanticClipRegistry.ResolveResult result, MonoBehaviour host)
        {
            var clip = result.chosenClip;
            if (clip == null)
            {
                Debug.LogWarning("[AnimDirective] Override '" + result.overrideEntry.id + "' has no clips assigned.");
                return;
            }

            if (overrideController == null)
            {
                Debug.LogWarning("[AnimDirective] No AnimatorOverrideController available for clip override.");
                return;
            }

            ClearBools(result.overrideEntry.clearBools);

            // Swap the placeholder clip in the Custom Dance state with our target clip
            overrideController[PlaceholderClipName] = clip;

            // Keep the animator in the Custom Dance state for the full clip duration.
            // The state's outgoing transition fires when isCustomDancing == false,
            // so we must hold it true until the revert coroutine clears it.
            animator.SetBool("isCustomDancing", true);

            // Transition to the proxy state
            animator.CrossFadeInFixedTime(ProxyStateName, 0.25f);

            Debug.Log("[AnimDirective] Override '" + result.overrideEntry.id + "' → playing " + clip.name + " (" + clip.length + "s) via " + ProxyStateName);

            // Auto-revert to idle after the clip finishes
            if (host != null)
                host.StartCoroutine(RevertOverrideAfter(clip.length));
        }

        IEnumerator RevertOverrideAfter(float seconds)
        {
            yield return new WaitForSeconds(seconds);
            if (animator == null || !animator.isActiveAndEnabled) yield break;

            animator.SetBool("isCustomDancing", false);
            animator.CrossFadeInFixedTime("Idle", 0.5f);
        }

        // ── Face ──────────────────────────────────────────────────
        // Persistent — face expression stays until changed by another directive.

        void ApplyFaceClip(string clipId)
        {
            if (blendshapes == null) return;

            var entry = registry?.Find(clipId);
            if (entry == null)
            {
                Debug.LogWarning("[AnimDirective] Unknown face clip: " + clipId);
                return;
            }

            if (entry.actionType != SemanticClipRegistry.ClipEntry.ActionType.BlendShape) return;

            SetBlendshapeField(entry.paramName, entry.paramValue);
        }

        void SetBlendshapeField(string fieldName, float value)
        {
            if (blendshapes == null) return;

            switch (fieldName)
            {
                case "Joy":     blendshapes.Joy = value; break;
                case "Angry":   blendshapes.Angry = value; break;
                case "Sorrow":  blendshapes.Sorrow = value; break;
                case "Fun":     blendshapes.Fun = value; break;
                case "Neutral": blendshapes.Neutral = value; break;
            }
        }

        // ── Helpers ───────────────────────────────────────────────

        void ClearBools(string[] bools)
        {
            if (bools == null || animator == null) return;
            foreach (var b in bools)
                animator.SetBool(b, false);
        }
    }
}
