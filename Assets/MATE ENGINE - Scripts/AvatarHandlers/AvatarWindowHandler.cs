using UnityEngine;
using System;
using System.Collections.Generic;

public class AvatarWindowHandler : MonoBehaviour
{
    [Header("Snap Safety")]
    public float minDragHoldSecondsToSit = 1f;
    public float unsnapCooldownSeconds = 0.3f;

    [Header("Snap Probe Offset")]
    public float probeZoneYOffsetLocal = 0f;
    [Header("Snap Probe")]
    public float probeRadiusPx = 24f;
    public bool showProbeGizmo = true;
    public Color probeGizmoColor = Color.magenta;
    [Header("Snap Guard Zone")]
    public bool useGuardZone = true;
    public float probeGuardPx = 240f;
    public Color probeGuardGizmoColor = Color.cyan;
    [Header("Sit Blockers")]
    public List<string> blockSitIfBoolTrue = new List<string>();
    [Header("Window Sit BlendTree")]
    public int totalWindowSitAnimations = 4;
    [Header("Seat Alignment")]
    [Range(-256f, 256f)] public float seatOffsetPx = 0f;
    [Range(-0.05f, 0.05f)] public float windowSitYOffset = 0f;
    [Header("Occluder")]
    public Material occluderMaterial;
    public Camera targetCamera;
    public float targetQuadZOffset = 0.001f;
    public float othersQuadZOffset = 0.002f;
    public int maxOtherQuads = 12;
    [Header("Occluder Pool")]
    public bool precreateQuadsOnStart = true;
    public int prewarmOtherQuads = 6;
    [Header("Target Quad Z Auto-Scale")]
    public bool autoScaleTargetZ = true;
    public float targetZBase = 3.2f;
    public float targetZRefScale = 1.0f;
    public float targetZSensitivity = 3.0f;
    public float targetZMin = 0.05f;
    public float targetZMax = 10f;
    [Header("Snap Smoothing")]
    public bool enableSnapSmoothing = true;
    [Range(0.01f, 0.5f)] public float snapSmoothingTime = 0.12f;
    public float snapSmoothingMaxSpeed = 6000f;
    [Header("Snap Trigger")]
    public int minDragPixelsToSnap = 4;
    [Header("Snap Guard")]
    public int snapGuardFrames = 8;
    public int snapLatchFrames = 18;
    public int unsnapVerticalBand = 16;
    [Header("Transparent-Window-Filter")]
    [Range(0, 255)] public int layeredAlphaIgnoreBelow = 230;
    public bool ignoreLayeredClickThrough = true;
    public bool ignoreLayeredToolOrNoActivate = true;
    public bool ignoreLayeredWithColorKey = true;
    [Header("Performance")]
    public float windowEnumFPS = 15f;
    public float windowEnumIdleFPS = 8f;

    // Window sitting is not available on macOS — this component is a no-op stub.
    // All public fields are preserved to avoid breaking serialized references in scenes/prefabs.

    public struct RECT { public int Left, Top, Right, Bottom; }
    public struct POINT { public int X, Y; }

    public void ForceExitWindowSitting() { }
    public void SetBaseOffset(float v) { }
    public void SetBaseScale(float v) { }
    public float GetBaseOffset() => 0f;
    public float GetBaseScale() => 1f;
    public float GetScaleCompPx() => 0f;
}
