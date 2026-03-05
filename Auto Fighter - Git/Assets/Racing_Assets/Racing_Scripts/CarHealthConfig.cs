using UnityEngine;

/// <summary>
/// Health, malfunction, crash penalties, damage smoke, and crash impact VFX. CarController reads these at Start.
/// </summary>
public class CarHealthConfig : MonoBehaviour
{
    [Header("Health")]
    [SerializeField] private float maxHP = 20f;
    [SerializeField] private float hpCrashDamageAtSeverity1 = 100f;
    [SerializeField] private float baseHpRegenPerSecond = 0f;
    [SerializeField, Range(0.1f, 1f)] private float performanceAtZeroHP = 0.455f;
    [SerializeField, Range(0f, 1f)] private float degradeStartHPFraction = 0.422f;

    [Header("Input Malfunction (Low HP)")]
    [SerializeField] private bool enableDamageMalfunction = true;
    [SerializeField, Range(0f, 1f)] private float maxMalfunctionChancePerSecond = 0.326f;
    [SerializeField] private Vector2 malfunctionBurstDuration = new Vector2(0.2f, 0.72f);
    [SerializeField] private Vector2 malfunctionCooldown = new Vector2(0.5f, 5f);
    [SerializeField, Range(0f, 1f)] private float malfunctionThrottleMultiplier = 0.35f;
    [SerializeField] private bool useSmoothMalfunction = true;
    [SerializeField] private float minimumAccelerationFloor = 4f;
    [SerializeField] private float minimumMaxSpeedFloor = 6f;

    [Header("Crash Penalties")]
    [SerializeField] private float fuelLossAtSeverity1 = 50f;
    [SerializeField] private float minHpLossPerCrash = 10f;
    [SerializeField] private float minFuelLossPerCrash = 10f;
    [SerializeField] private float crashDamageCooldown = 0.35f;

    [Header("Crash Impact VFX")]
    [SerializeField] private GameObject crashImpactVFX;
    [SerializeField] private float crashImpactVFXLifetime = 0.4f;

    [Header("Damage Smoke (VFX)")]
    [SerializeField] private ParticleSystem damageSmokeVFX;
    [SerializeField, Range(0f, 1f)] private float smokeStartHPFraction = 0.5f;
    [SerializeField] private float smokeMinRate = 5f;
    [SerializeField] private float smokeMaxRate = 30f;
    [SerializeField] private float smokeMinSize = 0.5f;
    [SerializeField] private float smokeMaxSize = 1.6f;
    [SerializeField] private Color smokeColorAtThreshold = Color.white;
    [SerializeField] private Color smokeColorAtZeroHP = new Color(0.3773585f, 0.3773585f, 0.3773585f, 1f);
    [SerializeField] private bool invertSmokeColorLerp = false;

    public float MaxHP => maxHP;
    public float HpCrashDamageAtSeverity1 => hpCrashDamageAtSeverity1;
    public float BaseHpRegenPerSecond => baseHpRegenPerSecond;
    public float PerformanceAtZeroHP => performanceAtZeroHP;
    public float DegradeStartHPFraction => degradeStartHPFraction;
    public bool EnableDamageMalfunction => enableDamageMalfunction;
    public float MaxMalfunctionChancePerSecond => maxMalfunctionChancePerSecond;
    public Vector2 MalfunctionBurstDuration => malfunctionBurstDuration;
    public Vector2 MalfunctionCooldown => malfunctionCooldown;
    public float MalfunctionThrottleMultiplier => malfunctionThrottleMultiplier;
    public bool UseSmoothMalfunction => useSmoothMalfunction;
    public float MinimumAccelerationFloor => minimumAccelerationFloor;
    public float MinimumMaxSpeedFloor => minimumMaxSpeedFloor;
    public float FuelLossAtSeverity1 => fuelLossAtSeverity1;
    public float MinHpLossPerCrash => minHpLossPerCrash;
    public float MinFuelLossPerCrash => minFuelLossPerCrash;
    public float CrashDamageCooldown => crashDamageCooldown;
    public GameObject CrashImpactVFX => crashImpactVFX;
    public float CrashImpactVFXLifetime => crashImpactVFXLifetime;
    public ParticleSystem DamageSmokeVFX => damageSmokeVFX;
    public float SmokeStartHPFraction => smokeStartHPFraction;
    public float SmokeMinRate => smokeMinRate;
    public float SmokeMaxRate => smokeMaxRate;
    public float SmokeMinSize => smokeMinSize;
    public float SmokeMaxSize => smokeMaxSize;
    public Color SmokeColorAtThreshold => smokeColorAtThreshold;
    public Color SmokeColorAtZeroHP => smokeColorAtZeroHP;
    public bool InvertSmokeColorLerp => invertSmokeColorLerp;
}
