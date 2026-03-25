using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// World-space Crash-style popups (WHAM / KAPOW, etc.) for special movers and props hitting each other.
/// Uses a short per-pair cooldown to limit noise from scraping contacts.
/// </summary>
public static class RacingObstacleCollisionPopups
{
    private const float PairEntryTtlSeconds = 2.5f;

    private static readonly Dictionary<long, float> s_pairLastTime = new(64);

    /// <summary>Another collider we treat as a track obstacle / special mover for clash popups.</summary>
    public static bool IsObstacleBuddy(Collider col)
    {
        if (col == null) return false;
        return col.GetComponentInParent<RacingObstacle>() != null
            || col.GetComponentInParent<TrackObstacleBounceBack>() != null
            || col.GetComponentInParent<CrossTrackObstacle>() != null
            || col.GetComponentInParent<ShuttleTrackObstacle>() != null
            || col.GetComponentInParent<RollingLogAlongTrack>() != null;
    }

    public static void TrySpawnObstacleClash(
        Transform selfRoot,
        Transform otherRoot,
        Collision collision,
        Collider otherCollider,
        float relativeSpeed,
        float minRelativeSpeed,
        float popupHeight,
        float pairCooldownSeconds,
        bool enabled = true)
    {
        Vector3? fromContact = null;
        if (collision != null && collision.contactCount > 0)
            fromContact = collision.GetContact(0).point;

        TrySpawnObstacleClashAt(
            selfRoot,
            otherRoot,
            otherCollider,
            fromContact,
            relativeSpeed,
            minRelativeSpeed,
            popupHeight,
            pairCooldownSeconds,
            enabled);
    }

    /// <summary>Trigger / overlap paths: pass a concrete world point (e.g. closest point on other).</summary>
    public static void TrySpawnObstacleClashApprox(
        Transform selfRoot,
        Transform otherRoot,
        Collider otherCollider,
        Vector3 worldPoint,
        float relativeSpeed,
        float minRelativeSpeed,
        float popupHeight,
        float pairCooldownSeconds,
        bool enabled = true)
    {
        TrySpawnObstacleClashAt(
            selfRoot,
            otherRoot,
            otherCollider,
            worldPoint,
            relativeSpeed,
            minRelativeSpeed,
            popupHeight,
            pairCooldownSeconds,
            enabled);
    }

    private static void TrySpawnObstacleClashAt(
        Transform selfRoot,
        Transform otherRoot,
        Collider otherCollider,
        Vector3? contactFromCollision,
        float relativeSpeed,
        float minRelativeSpeed,
        float popupHeight,
        float pairCooldownSeconds,
        bool enabled)
    {
        if (!enabled) return;
        if (!RacingPopups.IsReady) return;
        if (selfRoot == null || otherRoot == null || otherCollider == null) return;
        if (relativeSpeed < minRelativeSpeed) return;

        long key = PairKey(selfRoot, otherRoot);
        float now = Time.time;
        PruneOld(now);
        if (s_pairLastTime.TryGetValue(key, out float last) && now - last < pairCooldownSeconds)
            return;

        s_pairLastTime[key] = now;

        Vector3 p = contactFromCollision ?? otherCollider.ClosestPoint(selfRoot.position);
        RacingPopups.CrashWorld(p + Vector3.up * popupHeight);
    }

    private static long PairKey(Transform a, Transform b)
    {
        int ia = a != null ? a.GetInstanceID() : 0;
        int ib = b != null ? b.GetInstanceID() : 0;
        if (ia > ib)
            (ia, ib) = (ib, ia);
        return ((long)ia << 32) | (uint)ib;
    }

    private static void PruneOld(float now)
    {
        if (s_pairLastTime.Count <= 80) return;

        var toRemove = new List<long>(16);
        foreach (var kv in s_pairLastTime)
        {
            if (now - kv.Value > PairEntryTtlSeconds)
                toRemove.Add(kv.Key);
        }
        for (int i = 0; i < toRemove.Count; i++)
            s_pairLastTime.Remove(toRemove[i]);
    }
}
