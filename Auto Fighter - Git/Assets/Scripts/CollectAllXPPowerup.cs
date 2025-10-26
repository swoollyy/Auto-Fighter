using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public sealed class CollectAllXPPowerup : IPowerup
{
    public string Id => "collect-all-xp";
    public float Weight => 1.0f;
    public string DebugLabel => "Collect All XP";

    // Only trigger while actively playing.
    public bool CanTrigger(IRunContext ctx) => ctx is Pinball pb && pb.CurrentState == PinballState.Play;

    // Temporarily redirects all XP to the anchor ball by boosting its forcefield and suppressing others.
    public void Execute(Pinball pm, Vector3 triggerPos)
    {
        if (!pm) return;

        var anchor = pm.ball;
        if (!anchor)
        {
            var balls = Object.FindObjectsOfType<Ball>();
            for (int i = 0; i < balls.Length; i++)
            {
                if (balls[i] && balls[i].isActiveAndEnabled && balls[i].IsActive)
                {
                    anchor = balls[i];
                    break;
                }
            }
        }

        pm.StartCoroutine(VacuumToAnchor(pm, anchor));
    }

    // Performs the XP “vacuum” effect: boosts anchor’s XP field, damps others, and restores after a delay.
    private static IEnumerator VacuumToAnchor(Pinball pm, Ball anchor)
    {
        const float duration = 1.75f;
        const float boostFactor = 400f;

        List<Collider> snapshot = null;
        var registry = XPCollectorRegistry.I;
        var anchorCol = anchor ? anchor.GetComponent<Collider>() : null;

        if (registry != null && anchorCol != null)
        {
            snapshot = new List<Collider>(registry.collectors);
            registry.collectors.Clear();
            registry.collectors.Add(anchorCol);
        }

        if (anchor != null)
            anchor.UpdateForcefield(boostFactor);

        var others = new List<Ball>(8);
        const float dampFactor = 0.01f;
        var allBalls = Object.FindObjectsOfType<Ball>();
        for (int i = 0; i < allBalls.Length; i++)
        {
            var b = allBalls[i];
            if (!b || b == anchor) continue;
            others.Add(b);
            b.UpdateForcefield(dampFactor);
        }

        pm.ScreenShake();

        float t = 0f;
        while (t < duration)
        {
            t += Time.unscaledDeltaTime;
            yield return null;
        }

        if (anchor != null)
            anchor.UpdateForcefield(1f / boostFactor);

        for (int i = 0; i < others.Count; i++)
        {
            var b = others[i];
            if (b) b.UpdateForcefield(1f / dampFactor);
        }

        if (registry != null && snapshot != null)
        {
            registry.collectors.Clear();
            for (int i = 0; i < snapshot.Count; i++)
            {
                var c = snapshot[i];
                if (c) registry.collectors.Add(c);
            }
        }
    }
}