using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public sealed class CollectAllXPPowerup : IPowerup
{
    public string Id => "collect-all-xp";
    public float Weight => 1.0f;
    public string DebugLabel => "Collect All XP";

    [Header("Vacuum Settings")]
    [SerializeField] private float duration = 4f;
    [SerializeField] private float anchorRangeMultiplier = 400f;
    [SerializeField] private float anchorGravity = 36f;
    [SerializeField, Min(0.01f)] private float reassertInterval = 0.1f;
    [SerializeField] private bool debugLogs = true;

    // Prevent stacking runaway radius
    private static bool _vacuumActive;
    private static float _vacuumEndsAt;

    public bool CanTrigger(IRunContext ctx) => ctx is Pinball pb && pb.CurrentState == PinballState.Play;

    public void Execute(Pinball pm, Vector3 triggerPos)
    {
        if (!pm) return;

        var anchor = pm.ball;
        if (anchor == null || !anchor.isActiveAndEnabled || !anchor.IsActive)
        {
            foreach (var b in Object.FindObjectsOfType<Ball>())
            {
                if (b && b.isActiveAndEnabled && b.IsActive) { anchor = b; break; }
            }
        }
        if (!anchor) return;

        // If already active, just extend the window (do NOT snapshot inflated radii again)
        if (_vacuumActive)
        {
            _vacuumEndsAt = Mathf.Max(_vacuumEndsAt, Time.time + duration);
            if (debugLogs) Debug.Log($"[CollectAllXPPowerup] Extended vacuum -> ends at {_vacuumEndsAt:0.00}");
            return;
        }

        _vacuumActive = true;
        _vacuumEndsAt = Time.time + duration;
        pm.StartCoroutine(VacuumToAnchor(pm, anchor));
    }

    private struct FFState
    {
        public ParticleSystemForceField ff;
        public bool enabled;
        public float startRange;
        public float endRange;
        public ParticleSystem.MinMaxCurve gravity;
    }

    private IEnumerator VacuumToAnchor(Pinball pm, Ball initialAnchor)
    {
        var registry = XPCollectorRegistry.I;
        if (debugLogs) Debug.Log($"[CollectAllXPPowerup] Vacuum start -> anchor {initialAnchor?.name}");

        // Snapshot current state once (do NOT treat expanded radius as new baseline later)
        var originals = new List<FFState>(32);
        foreach (var b in Object.FindObjectsOfType<Ball>())
        {
            if (!b || !b.isActiveAndEnabled || !b.IsActive) continue;
            var ffs = b.GetComponentsInChildren<ParticleSystemForceField>(true);
            foreach (var ff in ffs)
            {
                if (!ff) continue;
                originals.Add(new FFState
                {
                    ff = ff,
                    enabled = ff.enabled,
                    startRange = ff.startRange,
                    endRange = ff.endRange,
                    gravity = ff.gravity
                });
            }
        }
        if (debugLogs) Debug.Log($"[CollectAllXPPowerup] Found {originals.Count} forcefields.");

        Ball anchorBall = initialAnchor;

        void ApplySuppressionAndBoost(Ball anchor)
        {
            for (int i = 0; i < originals.Count; i++)
            {
                var st = originals[i];
                if (!st.ff) continue;
                var owner = st.ff.GetComponentInParent<Ball>();
                if (!owner) continue;

                if (owner == anchor)
                {
                    st.ff.enabled = true;
                    st.ff.startRange = st.startRange;
                    st.ff.endRange = st.endRange * Mathf.Max(1f, anchorRangeMultiplier);
                    st.ff.gravity = new ParticleSystem.MinMaxCurve(anchorGravity);
                }
                else
                {
                    st.ff.enabled = false;
                }
            }
        }

        void SetRegistryToAnchor(Ball anchor)
        {
            if (registry == null) return;
            registry.collectors.Clear();
            if (anchor)
            {
                var col = anchor.GetComponent<Collider>();
                if (col) registry.collectors.Add(col);
            }
            registry.NotifyChanged();
        }

        void RestoreForcefields()
        {
            for (int i = 0; i < originals.Count; i++)
            {
                var st = originals[i];
                if (!st.ff) continue;
                st.ff.enabled = st.enabled;
                st.ff.startRange = st.startRange;
                st.ff.endRange = st.endRange;
                st.ff.gravity = st.gravity;
            }
            if (debugLogs) Debug.Log("[CollectAllXPPowerup] Forcefields restored.");
        }

        void RebuildRegistryFromActiveBalls()
        {
            if (registry == null) return;
            registry.collectors.Clear();

            var balls = Object.FindObjectsOfType<Ball>();
            for (int i = 0; i < balls.Length; i++)
            {
                var b = balls[i];
                if (!b || !b.isActiveAndEnabled || !b.IsActive) continue;
                var col = b.GetComponent<Collider>();
                if (col) registry.collectors.Add(col);
            }
            registry.NotifyChanged();
            if (debugLogs) Debug.Log($"[CollectAllXPPowerup] Registry rebuilt (count={registry.collectors.Count}).");
        }

        void RefreshAllBallForcefields()
        {
            foreach (var b in Object.FindObjectsOfType<Ball>())
            {
                if (b && b.isActiveAndEnabled && b.IsActive)
                    b.RefreshForcefieldFromContext(); // ensures original radius restored
            }
        }

        ApplySuppressionAndBoost(anchorBall);
        SetRegistryToAnchor(anchorBall);
        pm.ScreenShake();

        float reassertT = 0f;
        while (Time.time < _vacuumEndsAt)
        {
            // unscaled progress so slowmo doesn't extend effect
            reassertT += Time.unscaledDeltaTime;

            if (!anchorBall || !anchorBall.isActiveAndEnabled || !anchorBall.IsActive)
            {
                Ball newAnchor = null;
                foreach (var b in Object.FindObjectsOfType<Ball>())
                {
                    if (b && b.isActiveAndEnabled && b.IsActive) { newAnchor = b; break; }
                }
                if (newAnchor && newAnchor != anchorBall)
                {
                    anchorBall = newAnchor;
                    ApplySuppressionAndBoost(anchorBall);
                    SetRegistryToAnchor(anchorBall);
                    if (debugLogs) Debug.Log($"[CollectAllXPPowerup] Anchor changed -> {anchorBall.name}");
                }
            }

            if (reassertT >= reassertInterval)
            {
                reassertT = 0f;
                ApplySuppressionAndBoost(anchorBall);
                SetRegistryToAnchor(anchorBall);
            }

            yield return null;
        }

        // Restore
        RestoreForcefields();
        RebuildRegistryFromActiveBalls();
        RefreshAllBallForcefields(); // CRUCIAL: revert inflated radius to true baseline
        _vacuumActive = false;

        if (debugLogs) Debug.Log("[CollectAllXPPowerup] Vacuum end.");
    }
}