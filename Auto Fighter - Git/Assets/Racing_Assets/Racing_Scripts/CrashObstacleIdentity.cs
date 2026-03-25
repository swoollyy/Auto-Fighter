using UnityEngine;

/// <summary>
/// Optional: pin an obstacle to a <see cref="CrashObstacleKind"/> and override mass / reference scale for severity.
/// Place on the obstacle root (or any parent of the collider).
/// </summary>
public class CrashObstacleIdentity : MonoBehaviour
{
    [Tooltip("Auto = infer from Shuttle / Cross / NPC / Creature / RacingObstacle / etc.")]
    [SerializeField] private CrashObstacleKind kind = CrashObstacleKind.Unknown;

    [Tooltip("If > 0, used instead of Rigidbody.mass when sampling obstacle mass.")]
    [SerializeField] private float massOverride;

    [Tooltip("If > 0, used as reference scale for this object instead of the global config reference.")]
    [SerializeField] private float referenceScaleOverride;

    public CrashObstacleKind Kind => kind;
    public bool HasExplicitKind => kind != CrashObstacleKind.Unknown;
    public float MassOverride => massOverride;
    public float ReferenceScaleOverride => referenceScaleOverride;

    public bool TryGetMassOverride(out float mass)
    {
        mass = massOverride;
        return massOverride > 0.001f;
    }

    public bool TryGetReferenceScaleOverride(out float s)
    {
        s = referenceScaleOverride;
        return referenceScaleOverride > 0.001f;
    }
}
