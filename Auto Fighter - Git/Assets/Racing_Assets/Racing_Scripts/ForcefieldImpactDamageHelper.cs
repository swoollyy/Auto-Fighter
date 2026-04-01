using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Shared obstacle-on-obstacle damage when at least one rigidbody was recently launched by the car forcefield.
/// </summary>
public static class ForcefieldImpactDamageHelper
{
    public const float DefaultCooldown = 0.25f;

    /// <summary>
    /// Applies skill-gated impact damage between two collision participants when appropriate.
    /// </summary>
    /// <param name="collision">Unity collision data.</param>
    /// <param name="myRb">This object's rigidbody (non-null).</param>
    /// <param name="pairCooldownByOtherRbId">Per-instance cooldown map (other rigidbody instance ID).</param>
    /// <param name="cooldownSeconds">Min seconds between damage applications for the same pair.</param>
    /// <param name="minRelativeSpeed">Minimum relative speed (m/s) to apply damage; 0 = no extra gate.</param>
    /// <returns>True if damage was applied.</returns>
    public static bool TryApply(
        Collision collision,
        Rigidbody myRb,
        Dictionary<int, float> pairCooldownByOtherRbId,
        float cooldownSeconds,
        float minRelativeSpeed = 0f)
    {
        if (collision == null || myRb == null || pairCooldownByOtherRbId == null)
            return false;

        Rigidbody otherRb = collision.rigidbody;
        if (otherRb == null || otherRb == myRb)
            return false;

        var mgr = RacingSkillTreeManager.Instance;
        if (mgr == null || !mgr.IsForcefieldImpactDamageUnlocked())
            return false;

        bool thisLaunched = IsLaunchTagActive(myRb.gameObject);
        bool otherLaunched = IsLaunchTagActive(otherRb.gameObject);
        if (!thisLaunched && !otherLaunched)
            return false;

        float relSpeed = collision.relativeVelocity.magnitude;
        if (minRelativeSpeed > 0f && relSpeed < minRelativeSpeed)
            return false;

        int otherId = otherRb.GetInstanceID();
        if (pairCooldownByOtherRbId.TryGetValue(otherId, out float lastT))
        {
            if (Time.time - lastT < cooldownSeconds)
                return false;
        }

        pairCooldownByOtherRbId[otherId] = Time.time;

        float dmg = mgr.GetForcefieldImpactDamageAmount(1f);

        var roMe = myRb.GetComponentInParent<RacingObstacle>();
        var roOther = otherRb.GetComponentInParent<RacingObstacle>();
        if (roMe != null)
            roMe.ApplyDamage(dmg);
        else
        {
            var idMe = myRb.GetComponentInParent<IDamageable>();
            idMe?.ApplyDamage(dmg);
        }

        if (roOther != null)
            roOther.ApplyDamage(dmg);
        else
        {
            var idOther = otherRb.GetComponentInParent<IDamageable>();
            idOther?.ApplyDamage(dmg);
        }

        return true;
    }

    private static bool IsLaunchTagActive(GameObject go)
    {
        if (go == null) return false;
        var tag = go.GetComponentInParent<ForcefieldLaunchTag>();
        return tag != null && tag.IsActive;
    }
}
