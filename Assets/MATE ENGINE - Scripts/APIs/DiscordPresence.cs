using UnityEngine;
using System;
using System.Collections.Generic;

public class DiscordPresence : MonoBehaviour
{
    public enum TimerMode
    {
        None,
        StartNow,
        FixedStartTime
    }

    [Header("Discord App Info")]
    public string appId = "123456789012345678";

    [Header("Default Text")]
    public string detailsLine = "Playing with my desktop pet";
    public string stateLine = "Just vibing";

    [Header("Timer")]
    public TimerMode timerMode = TimerMode.StartNow;
    public string fixedStartTimeISO = "2025-04-07T12:00:00Z";

    [Header("Button")]
    public string buttonLabel = "Visit Website";
    public string buttonUrl = "https://mateengine.com";

    [Header("Icons")]
    public string largeImageKey = "logo";
    public string largeImageText = "MateEngine";
    public string smallImageKey = "steam-icon";
    public string smallImageText = "Steam Edition";

    [Header("Model Root (VRMModel or CustomVRM must be child)")]
    public GameObject modelRoot;

    [Header("State-Based Overrides")]
    public List<PresenceEntry> presenceOverrides = new List<PresenceEntry>();

    [Serializable]
    public class PresenceEntry
    {
        public string stateName;
        public string details;
        public string state;
    }

    // Discord RPC is disabled on macOS (no native pipe support).
    // TODO: Phase 2 - Evaluate macOS-compatible Discord RPC solution.

    void Start()
    {
        // Discord RPC not available on macOS
        return;
    }

    void Update()
    {
        // Discord RPC not available on macOS
        return;
    }

    void OnApplicationQuit()
    {
        // No Discord client to clean up on macOS
    }
}
