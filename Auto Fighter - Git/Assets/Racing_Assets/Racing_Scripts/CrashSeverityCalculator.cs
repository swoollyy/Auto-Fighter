using UnityEngine;

/// <summary>
/// Builds a 0–1 crash severity from closing speed, obstacle mass/scale, and kind weights.
/// </summary>
public static class CrashSeverityCalculator
{
    public struct Input
    {
        public Transform ObstacleRoot;
        public Rigidbody ObstacleRigidbody;
        public Rigidbody CarRigidbody;
        /// <summary>Car's effective max speed at impact (for normalizing closing speed).</summary>
        public float CarEffectiveMaxSpeed;
        /// <summary>World-space contact normal (e.g. collision contact normal).</summary>
        public Vector3 ContactNormalWorld;
        /// <summary>If true, compute closing speed from <see cref="Collision"/> instead of overrides.</summary>
        public Collision Collision;
        /// <summary>When set, used as closing speed if Collision is null.</summary>
        public float ClosingSpeedOverride;
        /// <summary>Extra multiplier (e.g. bull rush).</summary>
        public float ExtraSeverityMultiplier;
    }

    /// <summary>
    /// 0–1 severity. Falls back to <paramref name="legacySeverity01"/> if config is null.
    /// </summary>
    public static float Compute(
        CrashSeverityConfig config,
        in Input input,
        float legacySeverity01,
        out CrashObstacleKind resolvedKind)
    {
        resolvedKind = DetectKind(input.ObstacleRoot, input.ObstacleRigidbody);

        if (config == null)
            return Mathf.Clamp01(legacySeverity01);

        float closing = ResolveClosingSpeed(in input);
        closing = Mathf.Max(0f, closing);

        float capSpeed = Mathf.Max(config.ReferenceMaxClosingSpeed, Mathf.Max(0.5f, input.CarEffectiveMaxSpeed));
        float denom = Mathf.Max(1e-4f, capSpeed - config.MinClosingSpeed);
        float tSpeed = Mathf.Clamp01((closing - config.MinClosingSpeed) / denom);
        AnimationCurve curve = config.SpeedSeverityCurve;
        if (curve == null || curve.length == 0)
            curve = AnimationCurve.Linear(0f, 0f, 1f, 1f);
        float speedTerm = Mathf.Max(0f, curve.Evaluate(tSpeed));

        float obstacleMass = SampleObstacleMass(input.ObstacleRoot, input.ObstacleRigidbody, config);
        float massT = Mathf.InverseLerp(config.LightObstacleMass, config.HeavyObstacleMass, obstacleMass);
        float massTerm = Mathf.Lerp(config.MassFactorAtLight, config.MassFactorAtHeavy, Mathf.Clamp01(massT));

        float refScale = config.ReferenceObstacleScale;
        var id = input.ObstacleRoot != null ? input.ObstacleRoot.GetComponentInParent<CrashObstacleIdentity>() : null;
        if (id != null && id.TryGetReferenceScaleOverride(out float rs))
            refScale = rs;

        float avgScale = AverageLossyScale(input.ObstacleRoot);
        float scaleRatio = avgScale / Mathf.Max(1e-4f, refScale);
        CrashKindSeverityEntry entry = config.GetEntry(resolvedKind);
        float scaleSensitivity = config.ScaleBonusPerUnitOverReference + entry.extraScaleBonus;
        float scaleTerm = 1f + Mathf.Max(0f, scaleRatio - 1f) * scaleSensitivity;

        float coreHeft = massTerm * scaleTerm * entry.severityWeight * config.GlobalSeverityMultiplier;
        float combined = speedTerm * coreHeft;

        float extra = input.ExtraSeverityMultiplier;
        if (extra > 0f)
            combined *= extra;

        float blend = config.MinSeverityBlendFromObstacleHeft;
        if (blend > 0f)
        {
            float heftPortion = Mathf.Clamp01(coreHeft / 4f);
            float heftFloor = blend * heftPortion;
            if (extra > 0f)
                heftFloor *= extra;
            combined = Mathf.Max(combined, heftFloor);
        }

        return Mathf.Clamp01(combined);
    }

    private static float ResolveClosingSpeed(in Input input)
    {
        if (input.Collision != null && input.Collision.contactCount > 0)
        {
            var c = input.Collision.GetContact(0);
            Vector3 n = c.normal.sqrMagnitude > 1e-8f ? c.normal.normalized : Vector3.up;
            // Approach rate along contact normal
            float along = -Vector3.Dot(input.Collision.relativeVelocity, n);
            return Mathf.Max(along, input.Collision.relativeVelocity.magnitude * 0.35f);
        }

        if (input.ClosingSpeedOverride > 1e-6f)
            return input.ClosingSpeedOverride;

        if (input.CarRigidbody != null && input.ObstacleRigidbody != null)
            return (input.CarRigidbody.velocity - input.ObstacleRigidbody.velocity).magnitude;

        if (input.CarRigidbody != null)
            return input.CarRigidbody.velocity.magnitude;

        return 0f;
    }

    private static float SampleObstacleMass(Transform root, Rigidbody obstacleRb, CrashSeverityConfig config)
    {
        if (root != null)
        {
            var id = root.GetComponentInParent<CrashObstacleIdentity>();
            if (id != null && id.TryGetMassOverride(out float mo))
                return Mathf.Max(0.01f, mo);
        }

        if (obstacleRb != null && !obstacleRb.isKinematic)
            return Mathf.Max(0.01f, obstacleRb.mass);

        if (obstacleRb != null)
            return Mathf.Max(0.01f, obstacleRb.mass);

        return config.DefaultStaticObstacleMass;
    }

    private static float AverageLossyScale(Transform root)
    {
        if (root == null) return 1f;
        Vector3 l = root.lossyScale;
        return (l.x + l.y + l.z) / 3f;
    }

    public static CrashObstacleKind DetectKind(Transform obstacleRoot, Rigidbody obstacleRb)
    {
        Transform t = obstacleRb != null ? obstacleRb.transform : obstacleRoot;
        if (t == null)
            return CrashObstacleKind.Unknown;

        var id = t.GetComponentInParent<CrashObstacleIdentity>();
        if (id != null && id.HasExplicitKind)
            return id.Kind;

        if (t.GetComponentInParent<CrossTrackObstacle>() != null)
            return CrashObstacleKind.CrossTrack;
        if (t.GetComponentInParent<ShuttleTrackObstacle>() != null)
            return CrashObstacleKind.Shuttle;
        if (t.GetComponentInParent<TrackObstacleBounceBack>() != null)
            return CrashObstacleKind.BounceBack;
        if (t.GetComponentInParent<RollingLogAlongTrack>() != null)
            return CrashObstacleKind.RollingLog;
        if (t.GetComponentInParent<ThrownObstacle>() != null)
            return CrashObstacleKind.ThrownObstacle;

        var npc = t.GetComponentInParent<NPCTrafficCar>();
        if (npc != null)
            return npc.UseHeavyCrashProfile ? CrashObstacleKind.NpcTrafficCarBig : CrashObstacleKind.NpcTrafficCar;

        var creature = t.GetComponentInParent<TrackCreature>();
        if (creature != null)
        {
            return creature.BehaviorType == CreatureBehaviorType.Aggressive
                ? CrashObstacleKind.TrackCreatureAggressive
                : CrashObstacleKind.TrackCreaturePassive;
        }

        if (t.GetComponentInParent<RacingObstacle>() != null)
            return CrashObstacleKind.RacingObstacle;

        return CrashObstacleKind.TrackPropStatic;
    }
}
