using UnityEngine;
using System;

public class DesktopAmbientProbe : MonoBehaviour
{
    public Light topLight;
    public Light bottomLight;
    public Light leftLight;
    public Light rightLight;

    public bool enabledAuto = true;
    public bool driveIntensity = true;
    [Range(1f, 60f)] public float captureHz = 10f;
    public int captureWidth = 160;
    public int captureHeight = 90;
    public int bandThicknessPx = 120;
    public int excludeMarginPx = 12;
    [Range(0f, 1f)] public float smoothing = 0.85f;
    public string saveKey = "auto_ambient";
    [Range(0f, 4f)] public float minGrayIntensity = 0.3f;
    [Range(0f, 4f)] public float maxColorIntensity = 0.8f;
    [Range(0.5f, 3f)] public float saturationGamma = 1.3f;

    float nextTick;
    Vector3 hsvTop;
    Vector3 hsvBot;
    Vector3 hsvLeft;
    Vector3 hsvRight;
    Vector3 hsvTopTarget;
    Vector3 hsvBotTarget;
    Vector3 hsvLeftTarget;
    Vector3 hsvRightTarget;
    bool inited;
    bool hasSample;

    void Start()
    {
        TryLoadToggle();
        inited = true;
    }

    void OnDestroy()
    {
    }

    void TryLoadToggle()
    {
        var s = SaveLoadHandler.Instance;
        if (s != null && s.data != null && s.data.groupToggles != null)
        {
            if (s.data.groupToggles.TryGetValue(saveKey, out bool v)) enabledAuto = v;
        }
    }

    public void SetEnabled(bool v)
    {
        enabledAuto = v;
        var s = SaveLoadHandler.Instance;
        if (s != null && s.data != null)
        {
            s.data.groupToggles[saveKey] = v;
            s.SaveToDisk();
        }
    }

    void LateUpdate()
    {
        if (!inited) return;
        if (!enabledAuto) return;
        if (Time.unscaledTime >= nextTick)
        {
            nextTick = Time.unscaledTime + 1f / Mathf.Max(1f, captureHz);
        }
        SmoothTowardsTargets(Time.unscaledDeltaTime);
        ApplyToLights();
    }

    void SmoothTowardsTargets(float dt)
    {
        if (!hasSample) return;
        float tau = 0.05f + 1.5f * Mathf.Clamp01(smoothing);
        float a = 1f - Mathf.Exp(-dt / Mathf.Max(0.0001f, tau));
        hsvTop = DampHSV(hsvTop, hsvTopTarget, a);
        hsvBot = DampHSV(hsvBot, hsvBotTarget, a);
        hsvLeft = DampHSV(hsvLeft, hsvLeftTarget, a);
        hsvRight = DampHSV(hsvRight, hsvRightTarget, a);
    }

    Vector3 DampHSV(Vector3 cur, Vector3 target, float a)
    {
        float dh = Mathf.DeltaAngle(cur.x * 360f, target.x * 360f) / 360f;
        float h = Mathf.Repeat(cur.x + a * dh, 1f);
        float s = Mathf.Lerp(cur.y, target.y, a);
        float v = Mathf.Lerp(cur.z, target.z, a);
        return new Vector3(h, s, v);
    }

    void ApplyToLights()
    {
        ApplyLight(topLight, hsvTop);
        ApplyLight(bottomLight, hsvBot);
        ApplyLight(leftLight, hsvLeft);
        ApplyLight(rightLight, hsvRight);
    }

    void ApplyLight(Light L, Vector3 hsv)
    {
        if (L == null) return;
        Color c = Color.HSVToRGB(hsv.x, hsv.y, 1f);
        L.color = c;
        if (driveIntensity)
        {
            float i = Mathf.Lerp(minGrayIntensity, maxColorIntensity, Mathf.Pow(Mathf.Clamp01(hsv.y), saturationGamma));
            L.intensity = Mathf.Clamp(i, 0f, 4f);
        }
    }
}
