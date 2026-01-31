using System;
using UnityEngine;

[ExecuteAlways]
public class AvatarTaskbarController : MonoBehaviour
{
    [Header("Animator")]
    public Animator avatarAnimator;

    [Header("Detection Settings")]
    public Vector2 snapZoneOffset = new Vector2(0, -5);
    public Vector2 snapZoneSize = new Vector2(100, 10);

    [Header("Attach Settings")]
    public GameObject attachTarget;
    public HumanBodyBones attachBone = HumanBodyBones.Head;
    public bool keepOriginalRotation = false;

    [Header("Spawn / Despawn Animation")]
    public float spawnScaleTime = 0.2f;
    public float despawnScaleTime = 0.2f;

    [Header("Debug")]
    public bool showDebugGizmo = true;
    public Color taskbarGizmoColor = Color.green;
    public Color pinkZoneGizmoColor = Color.magenta;

    public void SetAnimator(Animator newAnimator)
    {
        avatarAnimator = newAnimator;
    }

    // TODO: Phase 2 - Port taskbar detection to macOS Dock using UniWinCore
    // macOS has no Windows taskbar concept; this class is a no-op stub for now.
}
