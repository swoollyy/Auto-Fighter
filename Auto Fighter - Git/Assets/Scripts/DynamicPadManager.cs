using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Central controller:
/// - Keeps at most maxActivePads enabled (upgraded or flipping) at once.
/// - Pads sit in an idle dull state until:
///     * Their individual nextEnableTime has elapsed
///     * Active/flipping count < maxActivePads
///   Then manager triggers their flip/upgrade.
/// - When a pad finishes its effect via ball interaction, it reverts to dull and enters an interaction cooldown before being eligible again.
/// - NEW: Random auto-disable ("tick off") will turn off active pads after a random active lifetime, bypassing the interaction cooldown.
/// </summary>
[DisallowMultipleComponent]
public class DynamicPadManager : MonoBehaviour
{
    [Header("Limits")]
    [SerializeField, Min(1)] private int maxActivePads = 3;

    [Header("Enable Timing")]
    [SerializeField, Min(0.1f)] private float minEnableDelay = 2f;
    [SerializeField, Min(0.1f)] private float maxEnableDelay = 6f;

    [Header("Interaction Cooldown (after ball interaction)")]
    [SerializeField, Min(0f)] private float interactionDisabledCooldownSeconds = 30f;

    [Header("Random Auto-Disable (Tick Off)")]
    [Tooltip("If enabled, active pads will randomly go off after a random active lifetime (bypassing interaction cooldown).")]
    [SerializeField] private bool enableRandomAutoDisable = true;
    [SerializeField, Min(0.1f)] private float minActiveLifetimeSeconds = 3f;
    [SerializeField, Min(0.1f)] private float maxActiveLifetimeSeconds = 10f;

    [Header("References / Auto-Discover")]
    [Tooltip("If empty, will find all DullPad components in scene at Start.")]
    [SerializeField] private List<DullPad> pads = new();

    // Count of pads currently reserved (flipping or active)
    private int _activeCount;
    // Pads that have been reserved (activation slot claimed)
    private readonly HashSet<DullPad> _reservedPads = new();
    private readonly List<DullPad> _eligibleBuffer = new();

    // NEW: per-pad scheduled auto-disable times
    private readonly Dictionary<DullPad, float> _autoDisableAt = new();

    void Start()
    {
        if (pads.Count == 0)
            pads.AddRange(FindObjectsOfType<DullPad>());

        foreach (var p in pads)
        {
            if (!p) continue;
            p.Manager = this;
            p.SetInteractionCooldown(interactionDisabledCooldownSeconds);
            p.ScheduleNextEnable(RandomEnableDelay());
        }
    }

    void Update()
    {
        TickEnableLogic();
        TickDisableLogic();
    }

    private void TickEnableLogic()
    {
        // If at capacity (including flipping), push back any idle pads whose timers just matured
        if (_activeCount >= maxActivePads)
        {
            KickBackEligibleIdlePads();
            return;
        }

        _eligibleBuffer.Clear();
        float now = Time.time;

        foreach (var p in pads)
        {
            if (!p) continue;
            if (!p.IsIdle) continue; // only idle dull pads
            if (p.NextEnableTime <= now)
                _eligibleBuffer.Add(p);
        }

        if (_eligibleBuffer.Count == 0) return;

        int freeSlots = Mathf.Max(0, maxActivePads - _activeCount);
        int toActivate = Mathf.Min(freeSlots, _eligibleBuffer.Count);

        for (int i = 0; i < toActivate; i++)
        {
            var pad = _eligibleBuffer[i];
            if (!pad) continue;
            pad.BeginFlipAndUpgrade(); // ReserveActivation happens inside
        }
    }

    // NEW: random auto-disable ticking
    private void TickDisableLogic()
    {
        if (!enableRandomAutoDisable) return;

        float now = Time.time;

        // Cleanup stale entries and ensure schedule exists for active pads
        for (int i = pads.Count - 1; i >= 0; i--)
        {
            var pad = pads[i];
            if (!pad) continue;

            if (pad.IsActivePad)
            {
                if (!_autoDisableAt.ContainsKey(pad))
                    _autoDisableAt[pad] = now + RandomActiveLifetime();

                // Time to auto-disable this pad (bypass interaction cooldown)
                if (_autoDisableAt.TryGetValue(pad, out float when) && now >= when)
                {
                    // Force disable without cooldown; will immediately return to Idle and notify manager
                    pad.ForceRevertToDullNoCooldown();
                    _autoDisableAt.Remove(pad);
                }
            }
            else
            {
                // Not active -> no pending auto-disable time
                _autoDisableAt.Remove(pad);
            }
        }
    }

    private void KickBackEligibleIdlePads()
    {
        float now = Time.time;
        foreach (var p in pads)
        {
            if (!p) continue;
            if (!p.IsIdle) continue;
            if (p.NextEnableTime <= now)
                p.ScheduleNextEnable(RandomEnableDelay()); // delay again since slots full
        }
    }

    private float RandomEnableDelay()
    {
        float min = Mathf.Max(0.01f, minEnableDelay);
        float max = Mathf.Max(min, maxEnableDelay);
        return Random.Range(min, max);
    }

    private float RandomActiveLifetime()
    {
        float min = Mathf.Max(0.01f, minActiveLifetimeSeconds);
        float max = Mathf.Max(min, maxActiveLifetimeSeconds);
        return Random.Range(min, max);
    }

    /// <summary>
    /// Called by DullPad when flip starts. Reserves an activation slot immediately.
    /// Also schedules random auto-disable if enabled.
    /// </summary>
    public void ReserveActivation(DullPad pad)
    {
        if (!pad) return;
        if (_reservedPads.Contains(pad)) return;
        _reservedPads.Add(pad);
        _activeCount++;

        if (enableRandomAutoDisable)
            _autoDisableAt[pad] = Time.time + RandomActiveLifetime();
        else
            _autoDisableAt.Remove(pad);
    }

    /// <summary>
    /// Called when pad effect ends and reverts to dull (either via interaction cooldown or forced auto-disable).
    /// </summary>
    public void OnPadDeactivated(DullPad pad)
    {
        if (!pad) return;
        _autoDisableAt.Remove(pad);

        if (_reservedPads.Remove(pad))
            _activeCount = Mathf.Max(0, _activeCount - 1);

        pad.ScheduleNextEnable(RandomEnableDelay());
    }

    // External API (optional)
    public void SetMaxActive(int max) => maxActivePads = Mathf.Max(1, max);
    public void SetEnableWindow(float minDelay, float maxDelay)
    {
        minEnableDelay = Mathf.Max(0.01f, minDelay);
        maxEnableDelay = Mathf.Max(minEnableDelay, maxDelay);
    }
    public void SetInteractionCooldown(float seconds)
    {
        interactionDisabledCooldownSeconds = Mathf.Max(0f, seconds);
        foreach (var p in pads)
            if (p) p.SetInteractionCooldown(interactionDisabledCooldownSeconds);
    }

#if UNITY_EDITOR
    void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(0.2f, 0.9f, 0.4f, 0.35f);
        foreach (var p in pads)
        {
            if (!p) continue;
            Gizmos.DrawWireSphere(p.transform.position + Vector3.up * 0.1f, 0.4f);
        }
    }
#endif
}