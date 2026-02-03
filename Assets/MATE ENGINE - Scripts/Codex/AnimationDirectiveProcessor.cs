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

        string lastProcessedRaw = "";
        int lastDirectiveEnd;

        public void SetTargets(Animator animator, UniversalBlendshapes blendshapes)
        {
            this.animator = animator;
            this.blendshapes = blendshapes;

            if (registry == null)
                registry = Resources.Load<SemanticClipRegistry>("SemanticClipRegistry");
        }

        /// <summary>
        /// Process the full accumulated stream buffer. Strips <!--anim:...--> directives,
        /// applies them, and returns clean text for display.
        /// </summary>
        public string ProcessText(string fullBuffer, MonoBehaviour coroutineHost)
        {
            if (string.IsNullOrEmpty(fullBuffer))
                return fullBuffer;

            // Find and process any new directives in the full buffer
            var matches = DirectiveRegex.Matches(fullBuffer);
            foreach (Match m in matches)
            {
                // Only process directives we haven't seen before
                if (m.Index + m.Length > lastDirectiveEnd)
                {
                    lastDirectiveEnd = m.Index + m.Length;
                    ApplyDirective(m.Groups[1].Value, coroutineHost);
                }
            }

            // Strip all directives from display text
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
                float duration = obj["duration"]?.Value<float>() ?? 0f;

                if (!string.IsNullOrEmpty(bodyId))
                    ApplyBodyClip(bodyId, duration, host);

                if (!string.IsNullOrEmpty(faceId))
                    ApplyFaceClip(faceId, duration, host);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[AnimDirective] Failed to parse directive: " + e.Message);
            }
        }

        void ApplyBodyClip(string clipId, float duration, MonoBehaviour host)
        {
            if (animator == null || !animator.isActiveAndEnabled) return;

            var entry = registry?.Find(clipId);
            if (entry == null)
            {
                Debug.LogWarning("[AnimDirective] Unknown body clip: " + clipId);
                return;
            }

            switch (entry.actionType)
            {
                case SemanticClipRegistry.ClipEntry.ActionType.SetFloat:
                    animator.SetFloat(entry.paramName, entry.paramValue);
                    if (!string.IsNullOrEmpty(entry.boolFlag))
                        animator.SetBool(entry.boolFlag, entry.boolValue);
                    break;

                case SemanticClipRegistry.ClipEntry.ActionType.SetBool:
                    animator.SetBool(entry.paramName, entry.boolValue);
                    break;

                case SemanticClipRegistry.ClipEntry.ActionType.CrossFade:
                    animator.CrossFadeInFixedTime(entry.paramName, 0.25f);
                    break;
            }

            if (duration > 0f && host != null)
            {
                host.StartCoroutine(RevertBodyAfter(duration, entry));
            }
        }

        void ApplyFaceClip(string clipId, float duration, MonoBehaviour host)
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

            if (duration > 0f && host != null)
            {
                host.StartCoroutine(RevertFaceAfter(duration, entry.paramName));
            }
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

        IEnumerator RevertBodyAfter(float seconds, SemanticClipRegistry.ClipEntry entry)
        {
            yield return new WaitForSeconds(seconds);
            if (animator == null || !animator.isActiveAndEnabled) yield break;

            // Revert dance to not dancing
            if (!string.IsNullOrEmpty(entry.boolFlag) && entry.boolFlag == "isDancing")
                animator.SetBool("isDancing", false);
        }

        IEnumerator RevertFaceAfter(float seconds, string fieldName)
        {
            yield return new WaitForSeconds(seconds);
            SetBlendshapeField(fieldName, 0f);
        }
    }
}
