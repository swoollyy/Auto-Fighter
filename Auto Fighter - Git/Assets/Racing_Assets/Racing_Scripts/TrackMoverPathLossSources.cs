using UnityEngine;

/// <summary>
/// Cross-track and shuttle movers only leave their scripted path when interacting with these obstacle families
/// (plus dedicated APIs: <see cref="CarForcefield"/>, etc.).
/// </summary>
public static class TrackMoverPathLossSources
{
    /// <summary>
    /// True if this collider belongs to a rolling log, thrown obstacle, bounce-back obstacle, or aggressive track beast.
    /// </summary>
    public static bool IsInstigator(Collider other)
    {
        if (other == null) return false;
        if (other.GetComponentInParent<RollingLogAlongTrack>() != null) return true;
        if (other.GetComponentInParent<ThrownObstacle>() != null) return true;
        if (other.GetComponentInParent<GorillaThrownProp>() != null) return true;
        if (other.GetComponentInParent<TrackObstacleBounceBack>() != null) return true;
        var tc = other.GetComponentInParent<TrackCreature>();
        return tc != null && tc.IsLargeCreature;
    }
}
