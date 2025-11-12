using System.Collections.Generic;
using UnityEngine;

[DefaultExecutionOrder(-10000)]
public sealed class TimeScaleHub : MonoBehaviour
{
    public static TimeScaleHub I { get; private set; }

    // If any active → IsAnyActive = true (used by Pinball guards)
    public static bool IsAnyActive => I != null && I._active.Count > 0;
    // NEW: if any pause lock is active, timeScale is forced to 0
    public static bool IsPaused => I != null && I._pauseOwners.Count > 0;

    // Default fixed delta used when scale = 1 (Pinball sets this on Awake)
    private float _defaultFixedDelta = 0.02f;

    // Owner -> request
    private readonly Dictionary<object, Req> _active = new();

    // NEW: owners that hold a hard pause (timeScale = 0)
    private readonly HashSet<object> _pauseOwners = new();

    private struct Req
    {
        public float scale;
        public bool affectFixedDelta;
    }

    void Awake()
    {
        if (I && I != this) { Destroy(gameObject); return; }
        I = this;
        DontDestroyOnLoad(gameObject);
        // Use current engine baseline until Pinball provides its baseline
        _defaultFixedDelta = Time.fixedDeltaTime;
        Recompute();
    }

    public static void EnsureInitialized(float defaultFixedDelta)
    {
        if (!I)
        {
            var go = new GameObject("TimeScaleHub");
            I = go.AddComponent<TimeScaleHub>();
        }
        I._defaultFixedDelta = Mathf.Max(0.0005f, defaultFixedDelta);
        I.Recompute();
    }

    // Begin or update a slow‑mo request
    public static void Begin(object owner, float scale, bool affectFixedDelta = true)
    {
        if (!I) EnsureInitialized(Time.fixedDeltaTime);
        scale = Mathf.Clamp(scale, 0.05f, 1f);
        I._active[owner ?? I] = new Req { scale = scale, affectFixedDelta = affectFixedDelta };
        I.Recompute();
    }

    // End a slow‑mo request
    public static void End(object owner)
    {
        if (!I) return;
        I._active.Remove(owner ?? I);
        I.Recompute();
    }

    // NEW: begin a hard pause (timeScale = 0, physics frozen)
    public static void BeginPause(object owner)
    {
        if (!I) EnsureInitialized(Time.fixedDeltaTime);
        I._pauseOwners.Add(owner ?? I);
        I.Recompute();
    }

    // NEW: end a hard pause
    public static void EndPause(object owner)
    {
        if (!I) return;
        I._pauseOwners.Remove(owner ?? I);
        I.Recompute();
    }

    // Nuke everything: resets slow‑mo to normal immediately (does not clear pauses)
    public static void ForceClearAll()
    {
        if (!I) return;
        I._active.Clear();
        I.Recompute();
    }

    // NEW: if you ever need to clear all pause locks (rare)
    public static void ForceClearAllPauses()
    {
        if (!I) return;
        I._pauseOwners.Clear();
        I.Recompute();
    }

    // When you suspect drift, re-apply computed timescale
    public static void ForceRecompute() => I?.Recompute();

    private void Recompute()
    {
        // Hard freeze takes priority over any slow‑mo requests
        if (_pauseOwners.Count > 0)
        {
            Time.timeScale = 0f;
            // Physics stays frozen at ts=0; keep fixedDelta at default for when we unpause
            Time.fixedDeltaTime = _defaultFixedDelta;
            return;
        }

        // Resolve effective scale: slowest wins
        float scale = 1f;
        bool anyAffectFixed = false;

        foreach (var kv in _active)
        {
            scale = Mathf.Min(scale, kv.Value.scale);
            anyAffectFixed |= kv.Value.affectFixedDelta;
        }

        Time.timeScale = scale;

        // Keep physics consistent with visual time when any request wants that
        if (anyAffectFixed || _active.Count == 0)
            Time.fixedDeltaTime = _defaultFixedDelta * scale;
    }
}