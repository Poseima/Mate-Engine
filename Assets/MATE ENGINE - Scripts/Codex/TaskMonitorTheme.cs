using UnityEngine;
using TMPro;

namespace MateEngine.Codex
{
    [CreateAssetMenu(fileName = "TaskMonitorTheme", menuName = "MateEngine/Task Monitor Theme")]
    public class TaskMonitorTheme : ScriptableObject
    {
        [Header("Sprites")]
        public Sprite panelBackground;
        public Sprite panelShadow;
        public Sprite panelGlowFrame;
        public Sprite pillBackground;
        public Sprite cardBackground;

        [Header("Icons")]
        public Sprite iconCollapse;
        public Sprite iconClose;
        public Sprite iconCancel;
        public Sprite iconCheck;
        public Sprite iconRefresh;

        [Header("Typography")]
        public TMP_FontAsset font;

        [Header("Colors")]
        public Color panelTint = new(1f, 1f, 1f, 0.95f);
        public Color cardTint = new(1f, 1f, 1f, 0.90f);
        public Color pillTint = new(1f, 1f, 1f, 0.95f);
        public Color shadowTint = new(1f, 1f, 1f, 0.70f);
        public Color glowTint = new(1f, 1f, 1f, 0.25f);

        public Color text = new(1f, 1f, 1f, 1f);
        public Color textDim = new(0.70f, 0.70f, 0.80f, 1f);
        // Legacy: used as fallback if per-state accents are not configured on the theme asset.
        public Color toolAccent = new(0.88f, 0.25f, 0.98f, 1f);

        // Per-state accents (muted by default)
        public Color workingAccent = new(124f / 255f, 167f / 255f, 194f / 255f, 1f);  // #7CA7C2
        public Color thinkingAccent = new(165f / 255f, 155f / 255f, 203f / 255f, 1f); // #A59BCB
        public Color toolCallAccent = new(179f / 255f, 162f / 255f, 122f / 255f, 1f); // #B3A27A
        public Color cancelAccent = new(194f / 255f, 125f / 255f, 109f / 255f, 1f);   // #C27D6D
        public Color chipBg = new(0f, 0f, 0f, 0.28f);

        public Color iconNormal = new(1f, 1f, 1f, 0.90f);
        public Color iconHover = new(1f, 1f, 1f, 1f);
        public Color iconPressed = new(1f, 1f, 1f, 0.65f);
        public Color iconDanger = new(1f, 0.65f, 0f, 1f);

        [Header("Layout")]
        public float headerHeight = 28f;
        public float cardAccentWidth = 4f;
        public float iconSize = 18f;
        public float chipHeight = 20f;

        [Header("Motion")]
        public float panelGlowPulseSpeed = 2.5f;
        public float panelGlowPulseAmplitude = 0.35f;
        public float activityFlashDuration = 0.22f;
        public Color activityFlashTint = new(1f, 1f, 1f, 1f);

        [Header("Offsets")]
        public Vector2 panelShadowOffset = new(12f, -12f);
        public Vector2 panelShadowPadding = new(64f, 64f);
        public Vector2 panelGlowPadding = new(48f, 48f);
    }
}
