using UnityEngine;

/// <summary>
/// Health, malfunction, crash penalties, damage smoke, and crash impact VFX. CarController reads these at Start.
/// </summary>
public class CarHealthConfig : MonoBehaviour
{
    [Header("Health")]
    [SerializeField] private float maxHP = 20f;
    [SerializeField] private float baseHpRegenPerSecond = 0f;
    [Tooltip("Accel & max-speed multiplier at 0 HP (within the damage band).")]
    [SerializeField, Range(0.05f, 1f)] private float performanceAtZeroHP = 0.28f;
    [Tooltip("Above this HP fraction, no HP-based performance loss. Below it, ramps down toward Performance At Zero HP.")]
    [SerializeField, Range(0.05f, 1f)] private float degradeStartHPFraction = 0.65f;

    [Header("Input Malfunction (Low HP)")]
    [SerializeField] private bool enableDamageMalfunction = true;
    [Tooltip("Peak random malfunctions/sec when heavily damaged (each FixedUpdate rolls chance * dt).")]
    [SerializeField, Range(0f, 2.5f)] private float maxMalfunctionChancePerSecond = 1.15f;
    [Tooltip("< 1 = more sputters at medium damage (curves chance up). 1 = linear with damage.")]
    [SerializeField, Range(0.15f, 1.5f)] private float lowHpMalfunctionChanceBiasExponent = 0.42f;
    [SerializeField] private Vector2 malfunctionBurstDuration = new Vector2(0.1f, 0.42f);
    [SerializeField] private Vector2 malfunctionCooldown = new Vector2(0.18f, 1.35f);
    [Tooltip("Throttle multiplier during a malfunction burst (lower = harsher sputter).")]
    [SerializeField, Range(0f, 1f)] private float malfunctionThrottleMultiplier = 0.18f;
    [SerializeField] private bool useSmoothMalfunction = true;
    [SerializeField] private float minimumAccelerationFloor = 2.5f;
    [SerializeField] private float minimumMaxSpeedFloor = 5f;

    [Header("Crash Penalties")]
    [Tooltip("Crash HP/fuel loss = severity × max pool × this scale. Severity is 0–1 from the crash system (e.g. 0.98 → 98% of max HP lost, capped by current HP). 0 = no HP/fuel damage from crashes.")]
    [SerializeField, Min(0f)] private float crashDamageSeverityScale = 1f;
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
    public float CrashDamageSeverityScale => crashDamageSeverityScale;
    public float BaseHpRegenPerSecond => baseHpRegenPerSecond;
    public float PerformanceAtZeroHP => performanceAtZeroHP;
    public float DegradeStartHPFraction => degradeStartHPFraction;
    public bool EnableDamageMalfunction => enableDamageMalfunction;
    public float MaxMalfunctionChancePerSecond => maxMalfunctionChancePerSecond;
    public float LowHpMalfunctionChanceBiasExponent => lowHpMalfunctionChanceBiasExponent;
    public Vector2 MalfunctionBurstDuration => malfunctionBurstDuration;
    public Vector2 MalfunctionCooldown => malfunctionCooldown;
    public float MalfunctionThrottleMultiplier => malfunctionThrottleMultiplier;
    public bool UseSmoothMalfunction => useSmoothMalfunction;
    public float MinimumAccelerationFloor => minimumAccelerationFloor;
    public float MinimumMaxSpeedFloor => minimumMaxSpeedFloor;
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
