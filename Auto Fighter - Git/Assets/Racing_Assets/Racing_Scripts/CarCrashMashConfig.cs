using UnityEngine;

/// <summary>
/// Crash recovery, mash (flip recovery), impact, popup, screen shake, gauge, and related settings. CarController reads these at Start.
/// </summary>
public class CarCrashMashConfig : MonoBehaviour
{
    [Header("Mash Gauge Skill Scaling")]
    [SerializeField, Range(0f, 0.5f)] private float drainScalePerClickPower = 0.5f;
    [SerializeField, Range(0f, 0.3f)] private float drainScalePerPassiveStrength = 0.12f;
    [SerializeField, Range(1f, 5f)] private float maxSkillDrainMultiplier = 5f;
    [SerializeField, Range(0.5f, 1f)] private float minSkillDrainMultiplier = 1f;

    [Header("Flip Recovery (Mash) - Input")]
    [SerializeField] private bool randomizeMashFaceButton = true;
    [SerializeField] private bool allowSpaceMashInEditor = true;

    [Header("Flip Mash Rewards")]
    [SerializeField] private float mashBaseFuelPerClick = 0f;
    [SerializeField] private float mashFuelSpeedBonusMax = 2f;
    [SerializeField] private float mashMaxSpeedThreshold = 0.05f;
    [SerializeField] private float mashMinSpeedThreshold = 0.5f;

    [Header("Crash Recovery Click Model")]
    [SerializeField] private bool useMultiplicativeMashClicks = true;
    [SerializeField] private int mashBaseClicks = 15;
    [SerializeField] private float mashDistanceWeight = 350f;
    [SerializeField] private float mashDistanceExponent = 1.34f;
    [SerializeField] private float mashSeverityWeight = 1.75f;
    [SerializeField] private float mashSeverityExponent = 1.25f;
    [SerializeField] private float mashCrashCountWeight = 1.5f;
    [SerializeField] private float mashSeveritySumWeight = 1f;

    [Header("Crash Recovery Multipliers")]
    [SerializeField] private bool enableCrashRecoveryAlways = true;
    [SerializeField] private float flippedClickMultiplier = 1.5f;
    [SerializeField] private float airborneClickMultiplier = 1.25f;

    [Header("Central crash severity")]
    [Tooltip("Assign a Crash Severity Config asset on the car prefab. Severity drives HP/fuel (CarHealthConfig × severity), popups, and mash drain. Add CrashObstacleIdentity on props when you need explicit mass/scale/kind.")]
    [SerializeField] private CrashSeverityConfig crashSeverityConfig;

    [Header("Crash / Hit Reaction")]
    [SerializeField] private LayerMask crashLayers = (LayerMask)1837056;
    [SerializeField] private float minImpactSpeed = 1.6f;
    [SerializeField] private float maxImpactSpeed = 14f;
    [SerializeField] private float minCrashDuration = 1.08f;
    [SerializeField] private float maxCrashDuration = 2.13f;
    [SerializeField] private float impulsePerUnitSpeed = 1.12f;
    [SerializeField] private float torquePerUnitSpeed = 18.8f;
    [Tooltip("Cap on how fast the car can be flung after a crash (velocity magnitude).")]
    [SerializeField, Min(0.1f)] private float maxCrashFlingSpeed = 20f;
    [SerializeField] private float crashDragMultiplier = 1f;
    [SerializeField] private float crashAngularDrag = 1f;

    [Header("Popup Text Settings")]
    [SerializeField] private bool enablePopupText = true;
    [SerializeField] private float popupVerticalOffset = 0.25f;
    [SerializeField] private float minHPDamageForPopup = 0.1f;
    [SerializeField] private float minFuelLossForPopup = 0.1f;
    [SerializeField] private float minFuelGainForPopup = 0.1f;
    [SerializeField] private Vector2 mashFuelPopupHorizontalRange = new Vector2(-0.6f, 0.6f);
    [SerializeField] private Vector2 mashFuelPopupVerticalRange = new Vector2(-0.6f, 0.6f);

    [Header("Mash Screen Shake")]
    [SerializeField] private bool enableMashScreenShake = true;
    [SerializeField] private float mashShakeDuration = 0.08f;
    [SerializeField] private float mashShakeStrength = 0.15f;
    [SerializeField] private int mashShakeVibrato = 10;
    [SerializeField] private float mashShakeRandomness = 0.5f;

    [Header("Crash Spin Tuning")]
    [SerializeField] private float crashYawTorqueMultiplier = 10.58f;
    [SerializeField] private float crashRollTorqueMultiplier = 7.47f;
    [SerializeField] private float crashPitchTorqueMultiplier = 9.17f;

    [Header("Crash Recovery")]
    [SerializeField] private float reorientDuration = 0.25f;
    [SerializeField] private float groundedDurationRequired = 0.5f;
    [SerializeField] private float groundCheckDistance = 0.3f;
    [SerializeField] private LayerMask groundCheckLayers = (LayerMask)24576;

    [Header("Flip Recovery (Mash)")]
    [SerializeField] private bool enableFlipRecoveryMash = true;
    [SerializeField, Range(-1f, 1f)] private float flipDotThreshold = 0f;
    [SerializeField, Range(0f, 180f)] private float flipAngleThreshold = 80f;

    [Header("Crash Recovery Click Calculation")]
    [SerializeField] private int mashClicksMin = 6;
    [SerializeField] private int mashClicksMaxFromSeverity = 3000;
    [SerializeField] private int mashClicksPerCrash = 25;
    [SerializeField] private int mashClicksMaxFromCrashCount = 20000;
    [SerializeField] private float mashClicksPerDistanceUnit = 0f;
    [SerializeField] private float mashDistanceUnit = 0f;
    [SerializeField] private int mashClicksMaxFromDistance = 0;
    [SerializeField] private AnimationCurve mashClicksByProgress = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
    [SerializeField] private float mashProgressTotalDistanceFallback = 1000f;
    [SerializeField] private int mashClicksRandomVariance = 0;
    [SerializeField] private int mashClicksAbsoluteMin = 2;
    [SerializeField] private int mashClicksAbsoluteMax = 500000;

    [Header("Mash Click Skills")]
    [SerializeField] private int baseClicksPerClick = 1;
    [SerializeField] private float basePassiveClickRate = 0f;
    [SerializeField] private int basePassiveClickStrength = 1;
    [Tooltip("Each integer step of (effective − baseline) clicks per press multiplies required mash by this. 1 = no extra scaling.")]
    [SerializeField, Min(1f)] private float mashDifficultyPerClickPowerStep = 1.4f;

    [Header("Mash Progress Gauge")]
    [SerializeField] private bool enableMashProgressGauge = true;
    [SerializeField] private float gaugeFillPerClick = 0.05f;
    [SerializeField] private float gaugeFillSpeedBonus = 2f;
    [Tooltip("Extra multiplier for the speed-based gauge fill bonus. 1 = unchanged, 2 = twice as strong speed bonus.")]
    [SerializeField, Min(0f)] private float gaugeFillSpeedMultiplier = 1f;
    [Tooltip("Fuel: start of good tier. Sprockets: no gauge factor below this (1×); from here to max tier ramps 1× → Gauge Multiplier At Good.")]
    [SerializeField] private float gaugeGoodThreshold = 0.7f;
    [Tooltip("Fuel per mash click only. Not used for sprocket payout.")]
    [SerializeField] private float gaugeMultiplierAtZero = 0.5f;
    [Tooltip("Fuel per mash click; also the high end of the sprocket good-band ramp (sprockets lerp 1× → this between good and max tier).")]
    [SerializeField] private float gaugeMultiplierAtGood = 1.5f;
    [Tooltip("Fuel per mash click only. Sprockets at max tier use Sprocket Gauge Bonus Max instead, not this value.")]
    [SerializeField] private float gaugeMultiplierAtMax = 2f;

    [Header("Mash Drain (Health/Fuel/Gauge)")]
    [SerializeField] private bool mashDrainsHealth = true;
    [SerializeField] private bool mashDrainsFuel = true;
    [SerializeField] private float mashHealthDrainAtMinSeverity = 2f;
    [SerializeField] private float mashHealthDrainAtMaxSeverity = 12f;
    [SerializeField] private float mashHealthDrainPerCrash = 0.5f;
    [SerializeField] private float mashHealthDrainCap = 50f;
    [SerializeField] private float mashFuelDrainAtMinSeverity = 2f;
    [SerializeField] private float mashFuelDrainAtMaxSeverity = 12f;
    [SerializeField] private float mashFuelDrainPerCrash = 0.25f;
    [SerializeField] private float mashFuelDrainCap = 30f;
    [SerializeField] private float gaugeDrainAtMinSeverity = 0.08f;
    [SerializeField] private float gaugeDrainAtMaxSeverity = 0.25f;
    [SerializeField] private float gaugeDrainPerCrash = 0.02f;
    [SerializeField] private float gaugeDrainCap = 0.75f;

    [Header("Sprocket Rewards")]
    [SerializeField] private bool enableSprocketRewards = true;
    [SerializeField] private float sprocketBasePercent = 0.2f;
    [Tooltip("Extra multiplier on sprocket payout when the mash gauge hit max tier. Below good threshold sprockets get 1× from gauge; from good to max ramps 1 → Gauge Multiplier At Good; max tier uses this value only (not Gauge Multiplier At Max).")]
    [SerializeField] private float sprocketGaugeBonusMax = 1.5f;
    [SerializeField] private int sprocketMinReward = 5;
    [SerializeField] private int sprocketMaxReward = 5000000;

    [Header("Flip Upright")]
    [SerializeField] private float flipUprightLift = 0.2f;

    [Header("Misc")]
    [SerializeField] private float surfaceMaxSpeedLerpRate = 2.78f;
    [SerializeField] private float closeCallAfterCrashBlockTime = 1f;

    [Header("Per-Collider Crash Cooldown")]
    [SerializeField] private float perColliderCrashCooldown = 0.5f;

    public float DrainScalePerClickPower => drainScalePerClickPower;
    public float DrainScalePerPassiveStrength => drainScalePerPassiveStrength;
    public float MaxSkillDrainMultiplier => maxSkillDrainMultiplier;
    public float MinSkillDrainMultiplier => minSkillDrainMultiplier;
    public bool RandomizeMashFaceButton => randomizeMashFaceButton;
    public bool AllowSpaceMashInEditor => allowSpaceMashInEditor;
    public float MashBaseFuelPerClick => mashBaseFuelPerClick;
    public float MashFuelSpeedBonusMax => mashFuelSpeedBonusMax;
    public float MashMaxSpeedThreshold => mashMaxSpeedThreshold;
    public float MashMinSpeedThreshold => mashMinSpeedThreshold;
    public bool UseMultiplicativeMashClicks => useMultiplicativeMashClicks;
    public int MashBaseClicks => mashBaseClicks;
    public float MashDistanceWeight => mashDistanceWeight;
    public float MashDistanceExponent => mashDistanceExponent;
    public float MashSeverityWeight => mashSeverityWeight;
    public float MashSeverityExponent => mashSeverityExponent;
    public float MashCrashCountWeight => mashCrashCountWeight;
    public float MashSeveritySumWeight => mashSeveritySumWeight;
    public bool EnableCrashRecoveryAlways => enableCrashRecoveryAlways;
    public float FlippedClickMultiplier => flippedClickMultiplier;
    public float AirborneClickMultiplier => airborneClickMultiplier;
    public CrashSeverityConfig CrashSeverityConfig => crashSeverityConfig;
    public LayerMask CrashLayers => crashLayers;
    public float MinImpactSpeed => minImpactSpeed;
    public float MaxImpactSpeed => maxImpactSpeed;
    public float MinCrashDuration => minCrashDuration;
    public float MaxCrashDuration => maxCrashDuration;
    public float ImpulsePerUnitSpeed => impulsePerUnitSpeed;
    public float TorquePerUnitSpeed => torquePerUnitSpeed;
    public float MaxCrashFlingSpeed => maxCrashFlingSpeed;
    public float CrashDragMultiplier => crashDragMultiplier;
    public float CrashAngularDrag => crashAngularDrag;
    public bool EnablePopupText => enablePopupText;
    public float PopupVerticalOffset => popupVerticalOffset;
    public float MinHPDamageForPopup => minHPDamageForPopup;
    public float MinFuelLossForPopup => minFuelLossForPopup;
    public float MinFuelGainForPopup => minFuelGainForPopup;
    public Vector2 MashFuelPopupHorizontalRange => mashFuelPopupHorizontalRange;
    public Vector2 MashFuelPopupVerticalRange => mashFuelPopupVerticalRange;
    public bool EnableMashScreenShake => enableMashScreenShake;
    public float MashShakeDuration => mashShakeDuration;
    public float MashShakeStrength => mashShakeStrength;
    public int MashShakeVibrato => mashShakeVibrato;
    public float MashShakeRandomness => mashShakeRandomness;
    public float CrashYawTorqueMultiplier => crashYawTorqueMultiplier;
    public float CrashRollTorqueMultiplier => crashRollTorqueMultiplier;
    public float CrashPitchTorqueMultiplier => crashPitchTorqueMultiplier;
    public float ReorientDuration => reorientDuration;
    public float GroundedDurationRequired => groundedDurationRequired;
    public float GroundCheckDistance => groundCheckDistance;
    public LayerMask GroundCheckLayers => groundCheckLayers;
    public bool EnableFlipRecoveryMash => enableFlipRecoveryMash;
    public float FlipDotThreshold => flipDotThreshold;
    public float FlipAngleThreshold => flipAngleThreshold;
    public int MashClicksMin => mashClicksMin;
    public int MashClicksMaxFromSeverity => mashClicksMaxFromSeverity;
    public int MashClicksPerCrash => mashClicksPerCrash;
    public int MashClicksMaxFromCrashCount => mashClicksMaxFromCrashCount;
    public float MashClicksPerDistanceUnit => mashClicksPerDistanceUnit;
    public float MashDistanceUnit => mashDistanceUnit;
    public int MashClicksMaxFromDistance => mashClicksMaxFromDistance;
    public AnimationCurve MashClicksByProgress => mashClicksByProgress;
    public float MashProgressTotalDistanceFallback => mashProgressTotalDistanceFallback;
    public int MashClicksRandomVariance => mashClicksRandomVariance;
    public int MashClicksAbsoluteMin => mashClicksAbsoluteMin;
    public int MashClicksAbsoluteMax => mashClicksAbsoluteMax;
    public int BaseClicksPerClick => baseClicksPerClick;
    public float BasePassiveClickRate => basePassiveClickRate;
    public int BasePassiveClickStrength => basePassiveClickStrength;
    public float MashDifficultyPerClickPowerStep => mashDifficultyPerClickPowerStep;
    public bool EnableMashProgressGauge => enableMashProgressGauge;
    public float GaugeFillPerClick => gaugeFillPerClick;
    public float GaugeFillSpeedBonus => gaugeFillSpeedBonus;
    public float GaugeFillSpeedMultiplier => gaugeFillSpeedMultiplier;
    public float GaugeGoodThreshold => gaugeGoodThreshold;
    public float GaugeMultiplierAtZero => gaugeMultiplierAtZero;
    public float GaugeMultiplierAtGood => gaugeMultiplierAtGood;
    public float GaugeMultiplierAtMax => gaugeMultiplierAtMax;
    public bool MashDrainsHealth => mashDrainsHealth;
    public bool MashDrainsFuel => mashDrainsFuel;
    public float MashHealthDrainAtMinSeverity => mashHealthDrainAtMinSeverity;
    public float MashHealthDrainAtMaxSeverity => mashHealthDrainAtMaxSeverity;
    public float MashHealthDrainPerCrash => mashHealthDrainPerCrash;
    public float MashHealthDrainCap => mashHealthDrainCap;
    public float MashFuelDrainAtMinSeverity => mashFuelDrainAtMinSeverity;
    public float MashFuelDrainAtMaxSeverity => mashFuelDrainAtMaxSeverity;
    public float MashFuelDrainPerCrash => mashFuelDrainPerCrash;
    public float MashFuelDrainCap => mashFuelDrainCap;
    public float GaugeDrainAtMinSeverity => gaugeDrainAtMinSeverity;
    public float GaugeDrainAtMaxSeverity => gaugeDrainAtMaxSeverity;
    public float GaugeDrainPerCrash => gaugeDrainPerCrash;
    public float GaugeDrainCap => gaugeDrainCap;
    public bool EnableSprocketRewards => enableSprocketRewards;
    public float SprocketBasePercent => sprocketBasePercent;
    public float SprocketGaugeBonusMax => sprocketGaugeBonusMax;
    public int SprocketMinReward => sprocketMinReward;
    public int SprocketMaxReward => sprocketMaxReward;
    public float FlipUprightLift => flipUprightLift;
    public float SurfaceMaxSpeedLerpRate => surfaceMaxSpeedLerpRate;
    public float CloseCallAfterCrashBlockTime => closeCallAfterCrashBlockTime;
    public float PerColliderCrashCooldown => perColliderCrashCooldown;
}
