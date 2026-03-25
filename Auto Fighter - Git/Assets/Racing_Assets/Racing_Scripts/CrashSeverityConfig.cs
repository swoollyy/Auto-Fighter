using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Central tuning for crash severity (0–100%). Assign on <see cref="CarCrashMashConfig"/>.
/// </summary>
[CreateAssetMenu(fileName = "CrashSeverityConfig", menuName = "Racing/Crash Severity Config", order = 10)]
public class CrashSeverityConfig : ScriptableObject
{
    [Header("Speed → severity")]
    [Tooltip("Closing speeds below this contribute 0 on the speed curve (after normalization).")]
    [SerializeField, Min(0f)] private float minClosingSpeed = 0.05f;

    [Tooltip("Upper end for normalizing closing speed. Final cap uses max(this, car effective max speed at impact).")]
    [SerializeField, Min(0.5f)] private float referenceMaxClosingSpeed = 14f;

    [Tooltip("X = normalized closing speed in 0–1 (after min/max remap). Y = multiplier applied to the speed term.")]
    [SerializeField] private AnimationCurve speedSeverityCurve = AnimationCurve.Linear(0f, 0f, 1f, 1f);

    [Header("Mass / scale")]
    [Tooltip("Car mass used when comparing to obstacle mass (incremental upgrades can differ; match typical player RB mass).")]
    [SerializeField, Min(0.01f)] private float carMassReference = 20f;

    [Tooltip("Rigidbody mass used when obstacle has no RB.")]
    [SerializeField, Min(0.01f)] private float defaultStaticObstacleMass = 3f;

    [Tooltip("Average lossy scale (xyz mean) compared to reference for a +scale bonus.")]
    [SerializeField, Min(0.01f)] private float referenceObstacleScale = 0.4f;

    [Tooltip("How much overscale above reference adds severity (0 = ignore scale).")]
    [SerializeField, Min(0f)] private float scaleBonusPerUnitOverReference = 0.35f;

    [Tooltip("Mass factor = Lerp(massFactorAtLight, massFactorAtHeavy, InverseLerp(lightMass, heavyMass, obstacleMass)).")]
    [SerializeField, Min(0.01f)] private float lightObstacleMass = 0.5f;
    [SerializeField, Min(0.01f)] private float heavyObstacleMass = 80f;
    [SerializeField, Range(0.1f, 3f)] private float massFactorAtLight = 0.55f;
    [SerializeField, Range(0.1f, 3f)] private float massFactorAtHeavy = 1.45f;

    [Header("Global")]
    [SerializeField, Range(0.1f, 5f)] private float globalSeverityMultiplier = 1f;

    [Tooltip("0 = off. Otherwise final severity is at least (this × clamp01(massTerm×scaleTerm×kindWeight×global)). Stops speed×mass×scale from going to ~0 on slow scrapes against big static props.")]
    [SerializeField, Range(0f, 1f)] private float minSeverityBlendFromObstacleHeft = 0.22f;

    [Tooltip("Multiplies crash fuel loss vs HP (both scale with severity × max pool). 1 = same fraction of max fuel as HP; 0.5 = half the fuel sting.")]
    [SerializeField, Range(0f, 3f)] private float fuelDamageScaleRelativeToHp = 0.55f;

    [Header("Per obstacle kind")]
    [SerializeField] private List<CrashKindSeverityEntry> perKind = new List<CrashKindSeverityEntry>();

    public float MinClosingSpeed => minClosingSpeed;
    public float ReferenceMaxClosingSpeed => referenceMaxClosingSpeed;
    public AnimationCurve SpeedSeverityCurve => speedSeverityCurve;
    public float CarMassReference => carMassReference;
    public float DefaultStaticObstacleMass => defaultStaticObstacleMass;
    public float ReferenceObstacleScale => referenceObstacleScale;
    public float ScaleBonusPerUnitOverReference => scaleBonusPerUnitOverReference;
    public float LightObstacleMass => lightObstacleMass;
    public float HeavyObstacleMass => heavyObstacleMass;
    public float MassFactorAtLight => massFactorAtLight;
    public float MassFactorAtHeavy => massFactorAtHeavy;
    public float GlobalSeverityMultiplier => globalSeverityMultiplier;
    public float MinSeverityBlendFromObstacleHeft => minSeverityBlendFromObstacleHeft;
    public float FuelDamageScaleRelativeToHp => fuelDamageScaleRelativeToHp;

    public CrashKindSeverityEntry GetEntry(CrashObstacleKind kind)
    {
        if (perKind == null) return CrashKindSeverityEntry.Default(kind);
        for (int i = 0; i < perKind.Count; i++)
        {
            if (perKind[i].kind == kind)
                return perKind[i];
        }

        return CrashKindSeverityEntry.Default(kind);
    }

    private void Reset()
    {
        EnsureDefaultTable();
    }

    [ContextMenu("Rebuild default per-kind table")]
    public void EnsureDefaultTable()
    {
        perKind = new List<CrashKindSeverityEntry>();
        foreach (CrashObstacleKind k in Enum.GetValues(typeof(CrashObstacleKind)))
        {
            if (k == CrashObstacleKind.Unknown) continue;
            perKind.Add(CrashKindSeverityEntry.Default(k));
        }
    }
}

[Serializable]
public struct CrashKindSeverityEntry
{
    public CrashObstacleKind kind;
    [Tooltip("Multiplies combined speed×mass×scale term.")]
    public float severityWeight;
    [Tooltip("Extra scale sensitivity for this kind (added to global scale bonus multiplier).")]
    public float extraScaleBonus;

    public static CrashKindSeverityEntry Default(CrashObstacleKind k)
    {
        float w = 1f;
        float extraScale = 0f;
        switch (k)
        {
            case CrashObstacleKind.TrackPropStatic:
                w = 1.2f;
                break;
            case CrashObstacleKind.RacingObstacle:
                w = 1.1f;
                break;
            case CrashObstacleKind.Shuttle:
                w = 1.35f;
                break;
            case CrashObstacleKind.CrossTrack:
                w = 1.35f;
                break;
            case CrashObstacleKind.BounceBack:
                w = 1.15f;
                break;
            case CrashObstacleKind.ThrownObstacle:
                w = 1.25f;
                break;
            case CrashObstacleKind.NpcTrafficCar:
                w = 1.3f;
                break;
            case CrashObstacleKind.NpcTrafficCarBig:
                w = 1.65f;
                break;
            case CrashObstacleKind.TrackCreaturePassive:
                w = 0.85f;
                break;
            case CrashObstacleKind.TrackCreatureAggressive:
                w = 1.45f;
                break;
            case CrashObstacleKind.RollingLog:
                w = 1.32f;
                break;
        }

        return new CrashKindSeverityEntry
        {
            kind = k,
            severityWeight = w,
            extraScaleBonus = extraScale,
        };
    }
}
