using UnityEngine;

/// <summary>
/// When <see cref="CrossTrackObstacle"/> or <see cref="ShuttleTrackObstacle"/> stays on its scripted path,
/// still applies a small upward velocity kick to props they touch (not path-loss instigators, not player, not other movers).
/// </summary>
public static class TrackMoverNonPathBump
{
    /// <summary>
    /// Applies upward <see cref="ForceMode.VelocityChange"/> to <paramref name="other"/>'s Rigidbody when appropriate.
    /// </summary>
    public static void TryApplyUpLaunch(
        Collider other,
        Transform moverTransform,
        Vector3 moverLastVelocity,
        float collisionRelativeSpeed,
        float baseUpVelocityChange,
        float speedScaleMin,
        float speedScaleMax,
        float referenceSpeedForScale,
        bool wakeKinematicObstacles)
    {
        if (other == null || moverTransform == null) return;
        if (baseUpVelocityChange <= 0f) return;

        if (other.transform == moverTransform || other.transform.IsChildOf(moverTransform))
            return;

        if (other.GetComponentInParent<CrossTrackObstacle>() != null) return;
        if (other.GetComponentInParent<ShuttleTrackObstacle>() != null) return;
        if (other.GetComponentInParent<NPCTrafficCar>() != null) return;

        Rigidbody otherRb = other.attachedRigidbody != null
            ? other.attachedRigidbody
            : other.GetComponentInParent<Rigidbody>();

        if (otherRb == null) return;
        if (otherRb.transform == moverTransform || otherRb.transform.IsChildOf(moverTransform)) return;

        float rel = Mathf.Max(moverLastVelocity.magnitude, collisionRelativeSpeed);
        if (otherRb.velocity.sqrMagnitude > 0.01f)
            rel = Mathf.Max(rel, (moverLastVelocity - otherRb.velocity).magnitude);

        float refSp = Mathf.Max(0.5f, referenceSpeedForScale);
        float t = Mathf.Clamp01(rel / refSp);
        float scale = Mathf.Lerp(speedScaleMin, speedScaleMax, t);
        float dv = baseUpVelocityChange * scale;

        if (SpawnUtils.IsEmbeddedLocked(otherRb)) return;

        if (otherRb.isKinematic)
        {
            if (!wakeKinematicObstacles) return;

            int road = LayerMask.NameToLayer("RoadSurface");
            int terrain = LayerMask.NameToLayer("Terrain");
            Transform root = otherRb.transform.root;
            if (road >= 0 && (other.gameObject.layer == road || (root != null && root.gameObject.layer == road)))
                return;
            if (terrain >= 0 && (other.gameObject.layer == terrain || (root != null && root.gameObject.layer == terrain)))
                return;

            otherRb.isKinematic = false;
            otherRb.useGravity = true;
            otherRb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            otherRb.interpolation = RigidbodyInterpolation.Interpolate;
            otherRb.constraints = RigidbodyConstraints.None;
            otherRb.WakeUp();
            Physics.SyncTransforms();
        }

        otherRb.AddForce(Vector3.up * dv, ForceMode.VelocityChange);
    }
}
