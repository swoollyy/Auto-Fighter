using System;
using System.Collections.Generic;
using UnityEngine;
using Debug = UnityEngine.Debug;

[RequireComponent(typeof(Rigidbody))]
[RequireComponent(typeof(Collider))]
[RequireComponent(typeof(CarMovementConfig))]
[RequireComponent(typeof(CarSteeringConfig))]
[RequireComponent(typeof(CarLandingConfig))]
[RequireComponent(typeof(CarDriftConfig))]
[RequireComponent(typeof(CarBoostConfig))]
[RequireComponent(typeof(CarFuelConfig))]
[RequireComponent(typeof(CarHealthConfig))]
[RequireComponent(typeof(CarGroundConfig))]
[RequireComponent(typeof(CarRampConfig))]
[RequireComponent(typeof(CarCrashMashConfig))]
[RequireComponent(typeof(CarVFXAudioConfig))]
public class CarController : MonoBehaviour
{
    // Config components (read at Awake; base values applied so upgrades/surfaces work unchanged)
    private CarMovementConfig _movementConfig;
    private CarSteeringConfig _steeringConfig;
    private CarLandingConfig _landingConfig;
    private CarDriftConfig _driftConfig;
    private CarBoostConfig _boostConfig;
    private CarFuelConfig _fuelConfig;
    private CarHealthConfig _healthConfig;
    private CarGroundConfig _groundConfig;
    private CarRampConfig _rampConfig;
    private CarCrashMashConfig _crashMashConfig;
    private CarVFXAudioConfig _vfxAudioConfig;

    [Header("Base Movement (on Default surface)")]
    private float baseAcceleration;
    private float baseMaxSpeed;
    private float baseBrakingForce;

    [Header("Steering")]
    private float turnSpeed;

    private float minSpeedToSteer;
    private bool allowSteerWhenTryingToMove;

    private readonly Dictionary<int, float> _perColliderCrashTime = new Dictionary<int, float>();
    private float perColliderCrashCooldown;

    // Reference to forcefield for protection checks
    private CarForcefield _forcefield;

    [Header("Ramp / Airborne Speed Preservation")]
    private bool skipSpeedClampWhileAirborne;
    private bool enableLandingCarrySpeed;
    private float landingExcessBleedPerSecond;
    private float landingNoClampGraceSeconds;
    private bool enableLandingBoost;
    private float landingBoostStrength;
    private float landingBoostDuration;
    private float landingBoostFalloff;

    [Header("Mash Gauge Skill Scaling")]
    private float drainScalePerClickPower;
    private float drainScalePerPassiveStrength;
    private float maxSkillDrainMultiplier;
    private float minSkillDrainMultiplier;

    [Header("Steering Feel")]
    private float lowSpeedSteerMultiplier;
    private float highSpeedSteerMultiplier;
    private float speedForSteerCurve;
    private float steeringInputSmooth;
    private float steeringReturnSmooth;

    [Header("Arcade Steering Extras")]
    private bool useAutoAlignToVelocity;
    private float autoAlignStrength;

    [Header("Ice Steering Ramp")]
    private bool enableIceSteerRamp;
    private float iceSteerRampUpRate;
    private float iceSteerRampDownRate;
    private float iceSteerMinFactor;
    private float iceSteerFlipPenalty;

    [Header("Arcade Coasting")]
    private float coastLowDecelPerSecond;
    private float coastHighDecelPerSecond;
    private float coastHighSpeedFraction;
    private bool useExponentialCoast;
    private float coastDampingPerSecond;


    [Header("Flip Recovery (Mash) - Input")]
    private bool randomizeMashFaceButton;
    private bool allowSpaceMashInEditor;

    // 0..3 => Unity: JoystickButton0..3
    // (PS) 0=Cross(✕) 1=Circle(◯) 2=Square(□) 3=Triangle(△)
    private int _mashFaceButtonIndex = 0;

    public int MashFaceButtonIndex => _mashFaceButtonIndex;

    private static readonly KeyCode[] PS_FACE_KEYS =
    {
    KeyCode.JoystickButton1, // Cross (X)
    KeyCode.JoystickButton2, // Circle
    KeyCode.JoystickButton0, // Square
    KeyCode.JoystickButton3  // Triangle
};

    public KeyCode MashFaceButtonKey
        => PS_FACE_KEYS[Mathf.Clamp(_mashFaceButtonIndex, 0, 3)];

    public enum FaceButton { Cross, Circle, Square, Triangle }

    [Header("Mash Input Mapping (Set these to match your controller backend)")]
    private KeyCode psCrossKey = KeyCode.JoystickButton1;    // your current X
    private KeyCode psCircleKey = KeyCode.JoystickButton2;
    private KeyCode psSquareKey = KeyCode.JoystickButton0;
    private KeyCode psTriangleKey = KeyCode.JoystickButton3;

    private FaceButton _requiredMashButton = FaceButton.Cross;

    public FaceButton RequiredMashButton => _requiredMashButton;

    public KeyCode MashRequiredKey
    {
        get
        {
            return _requiredMashButton switch
            {
                FaceButton.Cross => psCrossKey,
                FaceButton.Circle => psCircleKey,
                FaceButton.Square => psSquareKey,
                FaceButton.Triangle => psTriangleKey,
                _ => psCrossKey
            };
        }
    }

    private static RacingInputReader.FaceButton ToReaderFace(FaceButton fb)
    {
        return fb switch
        {
            FaceButton.Cross => RacingInputReader.FaceButton.South,
            FaceButton.Circle => RacingInputReader.FaceButton.East,
            FaceButton.Square => RacingInputReader.FaceButton.West,
            FaceButton.Triangle => RacingInputReader.FaceButton.North,
            _ => RacingInputReader.FaceButton.South
        };
    }

    public bool GetMashRequiredButtonDown()
    {
        // Mash minigame is suspended while UI is hidden (e.g. extra hit mid-recovery).
        if (_flipMashActive && !IsFlipMashUiVisible) return false;

        var reader = RacingInputReader.Instance;
        if (reader != null)
            return reader.GetMashDown(ToReaderFace(_requiredMashButton));
        return Input.GetKeyDown(MashRequiredKey);
    }

    private bool GetBoostDown()
    {
        var reader = RacingInputReader.Instance;
        if (reader != null) return reader.BoostDown;
        return Input.GetKeyDown(boostKey) || Input.GetKeyDown(boostButtonController);
    }

    private bool GetDriftHeld()
    {
        var reader = RacingInputReader.Instance;
        if (reader != null) return reader.DriftHeld;
        return Input.GetKey(driftKey) || Input.GetKey(driftButtonController);
    }

    private float GetSteerRaw()
    {
        var reader = RacingInputReader.Instance;
        if (reader != null) return reader.Steer;
        return Input.GetAxisRaw("Horizontal");
    }

    private bool GetAccelerateKeyOrTrigger()
    {
        var reader = RacingInputReader.Instance;
        if (reader != null) return reader.Accelerate > 0.1f;
        return Input.GetKey(KeyCode.W) || Input.GetAxisRaw("RightTrigger") > 0.1f;
    }

    private bool GetBrakeKeyOrTrigger()
    {
        var reader = RacingInputReader.Instance;
        if (reader != null) return reader.Brake > 0.1f;
        return Input.GetKey(KeyCode.S) || Input.GetAxisRaw("Vertical") < -0.1f || Input.GetAxisRaw("LeftTrigger") > 0.1f;
    }

    private bool GetFireHeld()
    {
        var reader = RacingInputReader.Instance;
        if (reader != null) return reader.FireHeld;
        return Input.GetKey(KeyCode.Mouse0) || Input.GetButton("Fire1");
    }

    private static readonly string[] PS_FACE_SYMBOLS = { "✕", "◯", "□", "△" };

    public string MashSymbolPS
        => PS_FACE_SYMBOLS[Mathf.Clamp(_mashFaceButtonIndex, 0, 3)];

    public string MashSymbolXbox
    {
        get
        {
            return _requiredMashButton switch
            {
                FaceButton.Cross => "A",
                FaceButton.Circle => "B",
                FaceButton.Square => "X",
                FaceButton.Triangle => "Y",
                _ => "A"
            };
        }
    }

    [Header("Flip Mash Rewards")]
    private float mashBaseFuelPerClick;
    private float mashFuelSpeedBonusMax;
    private float mashMaxSpeedThreshold;
    private float mashMinSpeedThreshold;

    [Header("Arcade Movement Tuning")]
    private float coastDecelFactor;
    private float brakeForwardFactor;
    private float reverseAccelFactor;
    private float brakeToReverseSpeed;

    // NEW: caps so braking can’t be insanely hard at low max speeds
    private float maxBrakeDecelPerSecond;
    private float maxReverseAccelPerSecond;

    private bool enableSlopeDriveAssist;
    private float slopeDriveAssistMaxAccel;
    private float slopeDriveAssistMinAngle;
    private float slopeDriveAssistMaxAngle;
    private float slopeDriveAssistRiseSpeed;
    private float slopeDriveAssistFallSpeed;
    private bool slopeDriveAssistDisableOnBoost;
    private bool slopeDriveAssistDisableOnIce;
    private float _slopeAssistSmoothed;

    private float baseSteeringDamp;
    private float currentSteeringDamp;


    // ─────────────────────────────────────────────
    // NEW: Steering traction while coasting (no throttle/brake, no drift)
    // ─────────────────────────────────────────────
    [Header("Steer Rolling Traction (from CarSteeringConfig)")]
    private bool enableSteerTraction;
    private float steerTractionReorientRate;
    private float steerRollingAccel;
    private float minSpeedForSteerTraction;
    private float lateralFrictionWhileSteering;
    private float steerTractionBlendIn;
    private float steerTractionBlendOut;
    private float _steerTractionBlend = 0f;
    private float steerRollingAccelCoastMultiplier;
    private bool applySteerRollingAccelOnIce;

    private bool _inputsSuppressedThisFrame = false;

    // NEW: split suppression so steering is never fully blocked by malfunction
    private bool _suppressThrottleBrakeThisFrame = false;
    private bool _suppressSteeringThisFrame = false;
    private bool _externalInputLocked = false;

    [Header("Drift Unlock")]
    private bool requireDriftUnlock;
    private bool driftUnlocked;

    [Header("Drift (Arcade)")]
    private KeyCode driftKey;
    private KeyCode driftButtonController = KeyCode.JoystickButton2;
    private float driftMinSpeed;
    private float maxDriftSteerMultiplier;
    private float driftSteeringInputBuildupMultiplier;
    private float driftBuildRate;
    private float driftReleaseRate;
    private float driftSideForce;
    private float driftSpeedDecayPerSecond;
    private float driftHeldSpeedDecayPerSecond;
    private float driftForwardAccelMultiplier;
    private bool useFullAccelWhileDrifting;
    private bool lockToDriftPeakSpeed;

    [Header("Crash Recovery Click Model (Multiplicative)")]
    private bool useMultiplicativeMashClicks;
    private int mashBaseClicks;
    private float mashDistanceWeight;
    private float mashDistanceExponent;
    private float mashSeverityWeight;
    private float mashSeverityExponent;
    private float mashCrashCountWeight;
    private float mashSeveritySumWeight;

    [Header("Crash Recovery Multipliers")]
    private bool enableCrashRecoveryAlways;
    private float flippedClickMultiplier;
    private float airborneClickMultiplier;

    [Header("Drift Braking")]
    private float driftBrakeDecayPerSecond;

    [Header("Close Call Speed Boost (Skill-Based)")]
    private float closeCallBoostBaseDuration;
    private float closeCallBoostForce;
    private ForceMode closeCallBoostForceMode;
    private float closeCallBoostMaxSpeedMult;

    [Header("Close Call Invincibility / Visuals")]
    private Color closeCallInvincibilityTint;
    private Color closeCallTintColor;
    private float closeCallTintStrength;

    private Renderer[] _renderers;
    private MaterialPropertyBlock _mpb;

    // Cache original colors per renderer
    private Dictionary<Renderer, Color> _originalColors = new Dictionary<Renderer, Color>();

    private float invincibilityBumpForceAway;
    private float invincibilityBumpForceUp;
    private float invincibilityBumpTorque;

    [Header("Drift Neutral Behavior")]
    private bool requireDirectionalInputForDriftCharge;
    private float driftNeutralDrainRate;

    [Header("Drift Neutral Reset")]
    private float driftNeutralFullResetDelay;

    [Header("Drift Direction Change Reset")]
    [Tooltip("If true, changing steering direction while holding drift will reset (or reduce) drift charge so direction change isn’t a snap turn.")]
    private bool resetDriftChargeOnSteerFlip;
    private float steerFlipRetainedCharge;
    private float steerFlipThreshold;
    private float minChargeForFlipReset;
    private float steerFlipRebuildDelay;

    [Header("Drift Glide (Ice Feel)")]
    private bool allowDriftGlideWithoutSteer;
    private float driftGlideDecayPerSecond;

    [Header("Close-Call Near-Miss (global)")]
    private bool enableCloseCallNearMisses;
    private float closeCallDistance;
    private float closeCallMinSpeed;
    private float closeCallCooldown;
    private float closeCallRootCooldown;

    [Header("Ice Surface Transition")]
    private float iceFrictionTransitionSpeed;
    private float iceHandlingTransitionSpeed;
    private float iceLateralSlideForce;
    private float iceVelocityAlignmentStrength;

    private bool _driftGlideActive;          // NEW: glide mode (holding drift, no steer)

    private float _driftNeutralTimer = 0f;

    private float _lastRawSteerValue;
    private int _driftCurrentSteerSign = 0;
    private float _driftFlipBlockUntil = 0f;

    [Header("Base Physics (from CarMovementConfig)")]
    private float baseDrag;

    [Header("Ground Detection (from CarGroundConfig)")]
    private LayerMask groundLayers;
    private int samplesX;
    private int samplesZ;
    private float raycastHeightOffset;
    private float raycastExtraDistance;
    private bool debugSurfaceRays;
    private float surfaceSampleExtent;
    private bool useTippedOverWorldDownSampler;
    private float tippedOverSurfaceUpDotThreshold;
    private float groundNormalBlendRate;
    private float groundNormalMixedSurfaceBlendScale;
    private float groundNormalMixedGrassMin;
    private float groundNormalMixedGrassMax;
    private float roadGrassTransitionLiftSpeed;
    private float roadGrassTransitionMinSpeed;
    private float roadGrassTransitionLiftCooldown;
    private float _prevGrassFractionForTransitionLift = -1f;
    private float _lastRoadGrassTransitionLiftTime = -999f;

    /// <summary>True when HP-death tumble uses grass-like slowdown (global ray or heavy grass sample fallback).</summary>
    private bool _hpDeathGrassEffectActive;

    private float deathHpTerrainRayLength = 56f;
    private float deathHpTerrainRayStartHeight = 0.85f;
    private float deathHpGrassDragBoost = 1.45f;
    private float deathHpGrassAngularDragBoost = 1.35f;
    private float deathHpGrassPlanarDampingPerSecond = 6f;
    private float deathHpGrassPlanarDampingPerSpeed = 0.12f;
    private float deathHpGrassRigidbodyDragScale = 2.35f;
    private float deathHpGrassFrictionScale = 1.65f;
    private float deathHpGrassTumbleMaxPlanarSpeed = 11f;

    [Header("Fuel (from CarFuelConfig)")]
    private float maxFuel;
    private float fuelUsePerSecondAtFullThrottle;
    private float fuelUsePerSecondBraking;
    private float idleFuelUsePerSecond;
    private float idleSpeedThreshold;

    [Header("Crash / Hit (from CarCrashMashConfig)")]
    private LayerMask crashLayers;
    private float minImpactSpeed;
    private float maxImpactSpeed;
    private float minCrashDuration;
    private float maxCrashDuration;
    private float impulsePerUnitSpeed;
    private float torquePerUnitSpeed;
    private float maxCrashFlingSpeed;
    private float crashDragMultiplier;
    private float crashAngularDrag;

    [Header("Velocity Safety")]
    [Tooltip("Optional total velocity magnitude cap after crash launches (0 = disabled). Prevents ridiculous flings while still allowing a launch.")]
    [SerializeField, Min(0f)] private float maxTotalVelocityMagnitude = 0f;

    [Header("Popup / Mash Shake / Crash Spin (from CarCrashMashConfig)")]
    private bool enablePopupText;
    private float popupVerticalOffset;
    private float minHPDamageForPopup;
    private float minFuelLossForPopup;
    private float minFuelGainForPopup;
    private Vector2 mashFuelPopupHorizontalRange;
    private Vector2 mashFuelPopupVerticalRange;
    private bool enableMashScreenShake;
    private float mashShakeDuration;
    private float mashShakeStrength;
    private int mashShakeVibrato;
    private float mashShakeRandomness;
    private float crashYawTorqueMultiplier;
    private float crashRollTorqueMultiplier;
    private float crashPitchTorqueMultiplier;
    private float reorientDuration;
    private float groundedDurationRequired;
    private float groundCheckDistance;
    private LayerMask groundCheckLayers;

    public float MinImpactSpeed => minImpactSpeed;
    public float MaxImpactSpeed => maxImpactSpeed;

    [Header("Steering Direction (from CarSteeringConfig)")]
    private bool invertSteeringWhenReversing;
    private float reverseSteerMultiplier;

    [Header("Health (from CarHealthConfig)")]
    private float maxHP;
    private float baseMaxHP;
    private float baseHpRegenPerSecond;
    private float hpRegenPerSecond = 0f;

    private float performanceAtZeroHP;
    private float degradeStartHPFraction;
    private bool enableDamageMalfunction;
    private float maxMalfunctionChancePerSecond;
    private Vector2 malfunctionBurstDuration;
    private Vector2 malfunctionCooldown;
    private float malfunctionThrottleMultiplier;
    private bool useSmoothMalfunction;
    private float minimumAccelerationFloor;
    private float minimumMaxSpeedFloor;
    private float _lowHpMalfunctionChanceBiasExponent = 0.42f;
    /// <summary>From <see cref="CarHealthConfig"/>: multiplies (severity × max HP/fuel) crash loss.</summary>
    private float crashDamageSeverityScale = 1f;
    private float crashDamageCooldown;
    private float _nextCrashAllowedTime = 0f;

    /// <summary>Optional centralized crash severity (from <see cref="CarCrashMashConfig"/>).</summary>
    private CrashSeverityConfig _crashSeverityConfig;

    private float _crashFuelDamageScale = 1f;

    private GameObject crashImpactVFX;
    private float crashImpactVFXLifetime;
    private ParticleSystem damageSmokeVFX;
    private float smokeStartHPFraction;
    private float smokeMinRate;
    private float smokeMaxRate;
    private float smokeMinSize;
    private float smokeMaxSize;
    private Color smokeColorAtThreshold;
    private Color smokeColorAtZeroHP;
    private bool invertSmokeColorLerp;

    /// <summary> True only after config and skill effects have been applied in Awake. Gates stat-dependent VFX and logic until then. </summary>
    private bool _statsInitialized;
    private bool _damageSmokeActive;
    private GameObject _damageSmokeRootGO;
    private ParticleSystem[] _damageSmokeSystems;

    // ─────────────────────────────────────────────
    // BOOST SYSTEM
    // ─────────────────────────────────────────────

    [Header("Boost (from CarBoostConfig)")]
    private bool requireBoostUnlock;
    private bool boostUnlocked;
    private float boostFlashSpeedThreshold;
    private float boostFlashCooldown;
    private bool _wasOnBoost;
    private float _lastBoostFlashTime;
    private KeyCode boostKey;
    private KeyCode boostButtonController = KeyCode.JoystickButton1;
    private float boostForce;
    private float boostSustainAcceleration;
    private float boostDuration;
    private float boostMaxSpeedMultiplier;
    private float postBoostSlowdownDuration;
    private float boostCooldown;
    private float boostFuelCost;
    private float driftBoostSustainAcceleration;
    private bool enableDriftHeldBoost;
    private float driftBoostMinHoldSeconds;
    private float driftBoostMaxHoldSeconds;
    private Vector2 driftBoostForceRange;
    private Vector2 driftBoostDurationRange;
    private Vector2 driftBoostMaxSpeedMultRange;
    private float driftBoostFuelCost;
    private float driftBoostCooldown;

    [Header("Screen Shake / Ramp / Death (from CarVFXAudioConfig, CarRampConfig)")]
    private Transform cameraShakeTarget;
    private float screenShakeGlobalMultiplier;
    private float screenShakeReturnSpeed;
    private bool enableRampAlignment;
    private float groundAlignSpeed;
    private float airAlignSpeed;
    private float groundNormalCastRadius;
    private float groundNormalCheckDistance;
    private float landingPredictDistance;
    private float landingAlignStartDistance;
    private GameObject deathVFX;
    private float deathVFXLifetime;
    private AudioClip deathExplodeClip;
    private float deathExplodeVolume;

    [Header("Flip Recovery / Mash (from CarCrashMashConfig)")]
    private bool enableFlipRecoveryMash;
    private float flipDotThreshold;
    private float flipAngleThreshold;
    private float mashUiHideSecondsOnExtraHit = 0.45f;
    private int mashClicksMin;
    private int mashClicksMaxFromSeverity;
    private int mashClicksPerCrash;
    private int mashClicksMaxFromCrashCount;
    private float mashClicksPerDistanceUnit;
    private float mashDistanceUnit;
    private int mashClicksMaxFromDistance;
    private AnimationCurve mashClicksByProgress;
    private float mashProgressTotalDistanceFallback;
    private int mashClicksRandomVariance;
    private int mashClicksAbsoluteMin;
    private int mashClicksAbsoluteMax;
    private int baseClicksPerClick;
    private float basePassiveClickRate;
    private int basePassiveClickStrength;
    private float mashDifficultyPerClickPowerStep = 1.4f;

    // Runtime (after skills applied)
    private int effectiveClicksPerClick = 1;
    private float effectivePassiveClickRate = 0f;
    private int effectivePassiveClickStrength = 1;
    private float effectiveFuelPerClick = 0f;

    // Passive click timer
    private float _passiveClickTimer;



    [Header("Mash Gauge / Drain (from CarCrashMashConfig)")]
    private bool enableMashProgressGauge;
    private float gaugeFillPerClick;
    private float gaugeFillSpeedBonus;
    private float gaugeFillSpeedMultiplier;
    private float gaugeGoodThreshold;
    private float gaugeMaxThreshold = 0.94f;
    private float gaugeMultiplierAtZero;
    private float gaugeMultiplierAtGood;
    private float gaugeMultiplierAtMax;
    private bool mashDrainsHealth;
    private bool mashDrainsFuel;
    private float mashHealthDrainAtMinSeverity;
    private float mashHealthDrainAtMaxSeverity;
    private float mashHealthDrainPerCrash;
    private float mashHealthDrainCap;
    private float mashFuelDrainAtMinSeverity;

    private float mashFuelDrainAtMaxSeverity;
    private float mashFuelDrainPerCrash;
    private float mashFuelDrainCap;

    [Header("Mash Gauge Drain (from CarCrashMashConfig)")]
    private float gaugeDrainAtMinSeverity;
    private float gaugeDrainAtMaxSeverity;
    private float gaugeDrainPerCrash;
    private float gaugeDrainCap;

    // Public properties for UI to position threshold markers
    public float GaugeGoodThreshold => gaugeGoodThreshold;
    public float GaugeMaxThreshold => gaugeMaxThreshold;

    [Header("Sprocket (from CarCrashMashConfig)")]
    private bool enableSprocketRewards;
    private float sprocketBasePercent;
    private float sprocketGaugeBonusMax;
    private int sprocketMinReward;
    private int sprocketMaxReward;

    // Runtime gauge state
    private float _mashGaugeValue;           // 0 to 1
    private float _mashGaugePeakValue;       // highest value reached this session
    private int _totalMashClicksThisSession; // total clicks for sprocket calculation
    private bool _gaugeMaxedThisSession;     // did player max out the gauge?
    private float _totalFuelGainedThisSession;   // track for end popup
    private int _totalSprocketsThisSession;      // track for end popup

    // Public properties for UI
    public float MashGaugeValue => _mashGaugeValue;
    public float MashGaugePeakValue => _mashGaugePeakValue;
    public bool MashGaugeMaxed => _gaugeMaxedThisSession;
    public int TotalMashClicksThisSession => _totalMashClicksThisSession;
    public float TotalFuelGainedThisSession => _totalFuelGainedThisSession;
    public int TotalSprocketsThisSession => _totalSprocketsThisSession;




    // Small lift to avoid sticking into ground when you snap upright.
    private float flipUprightLift;

    // Runtime
    private bool _flipMashActive;
    private int _flipMashClicks;
    private int _flipMashClicksNeeded;
    /// <summary>Mash UI stays hidden until this time after a secondary hit during mash (<see cref="AddMashDebtFromNewCrash"/>).</summary>
    private float _flipMashUiShowAgainTime;

    private float _lastMashTime;
    private int _lastRegisteredMashFrame = -1;
    private float _currentMashSpeed;        // 0 to 1, where 1 = max speed
    private float _mashSpeedSmoothed;

    public bool IsFlipMashActive => _flipMashActive;
    /// <summary>True when mash recovery is active and the crash-mash HUD/minigame accepts input (false briefly after another hit mid-mash).</summary>
    public bool IsFlipMashUiVisible => _flipMashActive && Time.time >= _flipMashUiShowAgainTime;
    public float FlipMashProgress => _flipMashClicksNeeded > 0 ? (float)_flipMashClicks / _flipMashClicksNeeded : 0f;
    public int FlipMashClicksRemaining => Mathf.Max(0, _flipMashClicksNeeded - _flipMashClicks);
    public float MashSpeedRating => _mashSpeedSmoothed;  // 0-1, for UI speed indicator
    public bool IsFlippedDuringRecovery => _isFlippedDuringRecovery;
    public float LastCrashSeverity => _lastCrashSeverity;

    private int _crashCount;                    // counts all crashes for scaling
    private float _crashSeveritySum;            // cumulative severity this run (for repeated-crash ramp)
    private bool _isFlippedDuringRecovery;      // track if we were flipped when recovery started
    private float _lastCrashSeverity;           // severity of the most recent crash (0-1)
    private bool _wasAirborneDuringCrash;       // was car airborne when crash happened

    [Header("Death Explosion (from CarVFXAudioConfig)")]
    private bool deathExplodeUseSpatial;
    private float deathExplodeSpatialBlend;
    private AudioRolloffMode deathExplodeRolloff;
    private float deathExplodeMinDistance;
    private float deathExplodeMaxDistance;
    private float deathExplodeVolumeMultiplier;
    private float deathExplodePitchMin;
    private float deathExplodePitchMax;
    private float deathExplodeSfxCooldown;



    // runtime
    private bool _wasGroundedLastFrame = false;
    private float _landingExcessSpeed = 0f;      // extra cap allowance that decays
    private float _lastLandedTime = -999f;
    private float _takeoffHorizSpeed = 0f;       // horizontal speed when we went airborne (for landing boost)
    private Vector3 _takeoffHorizDir = Vector3.forward; // direction when we went airborne (for landing velocity injection)
    private float _landingBoostTimeLeft = 0f;
    private float _landingBoostDuration = 1f;
    private float _landingBoostTargetMagnitude = 0f;

    private float _lastDeathExplodeSfxTime = -999f;

    private Vector3 _shakeBaseLocalPos;
    private float _shakeAmp;
    private float _shakeFreq;
    private float _shakeBlendAmp;
    private Vector3 _lastAppliedShakeOffset = Vector3.zero;
    private float _iceSteerCharge01 = 0f;
    private int _iceSteerSign = 0;

    // Runtime boost state
    private float _boostCooldownTimer;
    private float _driftBoostCooldownTimer;          // NEW: separate drift boost cooldown
    private bool _boostRequested;
    private bool _isBoosting;
    private float _boostTimer;
    private bool _isPostBoost;
    private float _postBoostTimer;
    private float _groundedTime = 0f;
    private bool _isGrounded = false;
    private Vector3 _lastStableGroundNormal = Vector3.up;
    /// <summary>Latest spherecast normal; blended into <see cref="_lastStableGroundNormal"/> for driving math.</summary>
    private Vector3 _groundNormalMeasured = Vector3.up;
    private bool _deathVfxPlayed = false;

    public event Action OnBoostStarted;
    public event Action OnBoostEnded;

    /// <summary>Raised when a non-lethal crash occurs; argument is severity 0..1.</summary>
    public event Action<float> OnCrash;
    /// <summary>Raised when a crash results in death; argument is severity 0..1 used for damage.</summary>
    public event Action<float> OnLethalCrash;

    private float baseBoostForce;
    private float baseBoostSustainAcceleration;
    private float baseBoostDuration;
    private float baseBoostMaxSpeedMult;
    private float baseBoostCooldown;
    private float baseBoostFuelCost;
    private float baseDriftBoostCooldown;

    private float _rawSteer;

    // Drift-held boost runtime (per-direction)
    private float _driftHoldTimeSeconds;        // accumulates while drifting with stable direction
    private int _driftHoldDirectionSign;        // +1/-1/0 current tracked direction
    private bool _driftWasActiveLastFrame;

    // Overrides for *next* boost activation (drift-held boost)
    private bool _boostOverrideActive;
    private bool _overrideIsDriftBoost;         // NEW: marks override as drift-held boost
    private float _boostOverrideForce;
    private float _boostOverrideDuration;
    private float _boostOverrideMaxMult;

    // Active boost runtime characteristics
    private bool _activeBoostIsDrift;           // NEW: tracks current boost type
    private float _activeBoostMaxMult = 1f;     // NEW: max speed multiplier during current boost

    // ═══════════════════════════════════════════════════════════════════════════
    // CENTRALIZED SPEED BOOST SYSTEM
    // All temporary max speed increases are managed here with natural ramp-down
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Represents a single active speed boost with natural ramp-down behavior.
    /// </summary>
    [Serializable]
    public struct SpeedBoostEntry
    {
        public string id;                    // Unique identifier for this boost type
        public float maxSpeedIncrease;       // Peak max speed increase (absolute, in m/s) OR multiplier if isMultiplier=true
        public float totalDuration;          // Original total duration
        public float remainingTime;          // Time remaining on this boost
        public float rampDownStartFraction;  // When to start ramping down (0-1, e.g., 0.3 = last 30%)
        public bool isMultiplier;            // If true, maxSpeedIncrease is a multiplier; if false, it's additive

        /// <summary>
        /// Returns the current effective speed increase (additive), accounting for ramp-down.
        /// For multiplier boosts, pass baseMaxSpeed to calculate the additive equivalent.
        /// </summary>
        public float GetCurrentSpeedIncrease(float baseMaxSpeed)
        {
            if (remainingTime <= 0f || totalDuration <= 0f) return 0f;

            float normalizedTime = remainingTime / totalDuration;
            float peakValue = isMultiplier ? (baseMaxSpeed * (maxSpeedIncrease - 1f)) : maxSpeedIncrease;

            // Before ramp-down phase: full value
            if (normalizedTime > rampDownStartFraction)
            {
                return peakValue;
            }

            // During ramp-down: smoothly interpolate to zero
            float rampProgress = normalizedTime / Mathf.Max(0.001f, rampDownStartFraction);
            // Use smooth step for more natural feel
            float smoothT = rampProgress * rampProgress * (3f - 2f * rampProgress);
            return peakValue * smoothT;
        }

        /// <summary>
        /// Returns the current multiplier if this is a multiplier-based boost, otherwise 1.
        /// </summary>
        public float GetCurrentMultiplier()
        {
            if (!isMultiplier || remainingTime <= 0f || totalDuration <= 0f) return 1f;

            float normalizedTime = remainingTime / totalDuration;

            // Before ramp-down phase: full multiplier
            if (normalizedTime > rampDownStartFraction)
            {
                return maxSpeedIncrease;
            }

            // During ramp-down: smoothly interpolate from maxSpeedIncrease to 1
            float rampProgress = normalizedTime / Mathf.Max(0.001f, rampDownStartFraction);
            float smoothT = rampProgress * rampProgress * (3f - 2f * rampProgress);
            return Mathf.Lerp(1f, maxSpeedIncrease, smoothT);
        }

        /// <summary>
        /// Returns the fraction of boost remaining (0-1).
        /// </summary>
        public float GetRemainingFraction()
        {
            if (totalDuration <= 0f) return 0f;
            return Mathf.Clamp01(remainingTime / totalDuration);
        }
    }

    // Active speed boosts list
    private List<SpeedBoostEntry> _activeSpeedBoosts = new List<SpeedBoostEntry>();

    [Header("Speed Boost Ramp-Down (from CarVFXAudioConfig)")]
    private float defaultBoostRampDownFraction;
    private float closeCallBoostRampDownFraction;
    private float regularBoostRampDownFraction;

    // Speed boost IDs for easy reference
    private const string BOOST_ID_REGULAR = "regular_boost";
    private const string BOOST_ID_DRIFT = "drift_boost";
    private const string BOOST_ID_CLOSE_CALL = "close_call_boost";
    private const string BOOST_ID_SURFACE = "surface_boost";

    // ═══════════════════════════════════════════════════════════════════════════
    // END CENTRALIZED SPEED BOOST SYSTEM FIELDS
    // ═══════════════════════════════════════════════════════════════════════════

    private Quaternion _initialRotation;
    private bool _isReorienting;
    private float _reorientElapsed;
    private Quaternion _reorientStartRot;
    private Quaternion _reorientTargetRot;

    private bool _onBoostSurface;
    private float _currentBoostAccel;
    private float _currentBoostMaxSpeed;
    private bool _currentBoostDuringCrash;
    private float _currentBoostCrashMultiplier;

    // Runtime ice state
    private bool _onIceSurface;
    private float _iceDynamicFrictionTarget = 1f;
    private float _iceStaticFrictionTarget = 1f;
    private float _iceHandlingTarget = 1f;
    private float _currentIceDynamicFriction = 1f;
    private float _currentIceStaticFriction = 1f;
    private float _currentIceHandling = 1f;

    private PhysicMaterial _carPhysicMaterial;
    private float _originalDynamicFriction;
    private float _originalStaticFriction;

    private bool IsCrashInvulnerable => _isReorienting;

    private bool _inCrash;
    private float _crashTimer;
    private float _baseDrag;
    private float _baseAngularDrag;

    private float baseMaxFuel;
    private float baseIdleFuelUse;
    private float baseFuelUseFullThrottle;
    private float baseFuelUseBraking;
    private float baseTurnSpeed;

    private float _tempHandlingMultiplier = 1f;
    private float _tempHandlingExpireAt = 0f;

    [Header("Fuel Modifiers (from CarVFXAudioConfig)")]
    private float grassFuelUseMultiplier;

    private float currentAcceleration;
    private float currentMaxSpeed;
    private float currentBrakingForce;
    private float currentTurnSpeed;
    private float currentDrag;

    private bool _crashKilledDriftHeldBoost = false;

    private float effectiveAcceleration;
    private float effectiveMaxSpeed;
    private float effectiveTurnSpeed;
    private float effectiveDrag;
    private float _boostBlockedUntil = 0f;

    private Rigidbody rb;
    private Collider carCollider;
    private BoxCollider boxCollider;
    private float steeringInput;

    private float driftCharge = 0f;
    private bool isDrifting = false;
    private float driftEntrySpeed = 0f;
    private float driftClampSpeed = 0f;
    private bool driftButtonHeld = false; // NEW: track if drift key is currently held
    private float driftPeakSpeed = 0f;     // NEW: highest speed attained while holding drift

    private float currentFuel;
    private bool isOutOfFuel = false;
    private float currentFuelUseMultiplier = 1f;

    private float currentHP;
    private bool isOutOfHP = false;
    private float _malfunctionTimer;
    private float _malfunctionCooldownRemain;
    private float _currentMalfunctionMultiplier = 1f; // 1.0 = no malfunction, lower = reduced throttle

    [Header("Debug Surface (from CarVFXAudioConfig)")]
    private float offDefaultFraction;
    private float grassFraction;

    private SkillApplicationMode accelMode;
    private float accelValue;
    private SkillApplicationMode maxSpeedMode;
    private float maxSpeedValue;
    private SkillApplicationMode steerMode;
    private float steerValue;
    private SkillApplicationMode fuelMode;
    private float fuelValue;


    private float _skillDrainMultiplier = 1f;



    // Runtime state
    private bool _closeCallInvincible;
    private float _closeCallInvincibilityEndTime;
    private float _closeCallInvincibilityDuration;
    private bool _closeCallBoosting;
    private float _closeCallBoostEndTime;

    [Header("Crash SFX (from CarVFXAudioConfig)")]
    private AudioClip crashClipDefault;
    private AudioClip crashClipHonk;
    private float crashSfxVolume;
    private bool crashUseSpatial;
    private float crashSpatialBlend;
    private AudioRolloffMode crashRolloff;
    private float crashMinDistance;
    private float crashMaxDistance;
    private float crashVolumeMultiplier;
    private float crashPitchMin;
    private float crashPitchMax;

    [Header("Surface (from CarCrashMashConfig)")]
    private float surfaceMaxSpeedLerpRate;

    private float _smoothedSurfaceMaxSpeed = -1f;

    /// <summary>After mash recovery, ignore boost/surface speed for this long so we resume at run-start stats, not boosted.</summary>
    private float _postMashRecoveryIgnoreBoostUntil = 0f;

    // "Dead" for mash = no minigame if out of fuel OR out of HP
    private bool IsDeadForMashRecovery => IsOutOfFuel || IsOutOfHP;

    // "Dead" for auto-upright = only true death should block final reorientation
    // (out of fuel should STILL allow the auto flatten at the end)
    private bool IsDeadForAutoUpright => IsOutOfHP;

    private readonly Dictionary<int, float> _lastCloseCallTime = new Dictionary<int, float>();

    // NEW: per-root tracking while inside close-call radius (for exit-based near-misses)
    private class CloseCallTrack
    {
        public Vector3 lastPos;
        public float lastDistance;
        public float minDistance;
        public bool isInside;
        public float lastSeenTime;
    }

    private readonly Dictionary<int, CloseCallTrack> _closeCallTracking = new Dictionary<int, CloseCallTrack>();

    // NEW: record roots we actually crashed into recently so they never award a near-miss
    private readonly Dictionary<int, float> _recentCrashRootTime = new Dictionary<int, float>();

    // NEW helper to limit overlap sphere frequency (cheap throttle)
    private float _lastCloseCallSweep = 0f;
    private float _closeCallSweepInterval = 0.18f;

    [Header("Close-Call vs Crash (from CarCrashMashConfig)")]
    private float closeCallAfterCrashBlockTime;

    /// <summary>
    /// Copy base/default values from config components into CarController fields at Awake.
    /// Ensures upgrades (ApplySurfaceMultipliers / ApplySkillEffects) still use the same pipeline.
    /// </summary>
    private void ApplyConfigToFields()
    {
        if (_movementConfig != null)
        {
            baseAcceleration = _movementConfig.BaseAcceleration;
            baseMaxSpeed = _movementConfig.BaseMaxSpeed;
            baseBrakingForce = _movementConfig.BaseBrakingForce;
            baseDrag = _movementConfig.BaseDrag;
            coastLowDecelPerSecond = _movementConfig.CoastLowDecelPerSecond;
            coastHighDecelPerSecond = _movementConfig.CoastHighDecelPerSecond;
            coastHighSpeedFraction = _movementConfig.CoastHighSpeedFraction;
            useExponentialCoast = _movementConfig.UseExponentialCoast;
            coastDampingPerSecond = _movementConfig.CoastDampingPerSecond;
            coastDecelFactor = _movementConfig.CoastDecelFactor;
            brakeForwardFactor = _movementConfig.BrakeForwardFactor;
            reverseAccelFactor = _movementConfig.ReverseAccelFactor;
            brakeToReverseSpeed = _movementConfig.BrakeToReverseSpeed;
            maxBrakeDecelPerSecond = _movementConfig.MaxBrakeDecelPerSecond;
            maxReverseAccelPerSecond = _movementConfig.MaxReverseAccelPerSecond;
            enableSlopeDriveAssist = _movementConfig.EnableSlopeDriveAssist;
            slopeDriveAssistMaxAccel = _movementConfig.SlopeDriveAssistMaxAccel;
            slopeDriveAssistMinAngle = _movementConfig.SlopeDriveAssistMinAngle;
            slopeDriveAssistMaxAngle = _movementConfig.SlopeDriveAssistMaxAngle;
            slopeDriveAssistRiseSpeed = _movementConfig.SlopeDriveAssistRiseSpeed;
            slopeDriveAssistFallSpeed = _movementConfig.SlopeDriveAssistFallSpeed;
            slopeDriveAssistDisableOnBoost = _movementConfig.SlopeDriveAssistDisableOnBoost;
            slopeDriveAssistDisableOnIce = _movementConfig.SlopeDriveAssistDisableOnIce;
        }
        else
        {
            baseAcceleration = 5.2f; baseMaxSpeed = 3.95f; baseBrakingForce = 0.003f; baseDrag = 0.13f;
            coastLowDecelPerSecond = 0.39f; coastHighDecelPerSecond = 5.55f; coastHighSpeedFraction = 1f;
            useExponentialCoast = false; coastDampingPerSecond = 4.48f; coastDecelFactor = 0.74f;
            brakeForwardFactor = 0.7f; reverseAccelFactor = 1.06f; brakeToReverseSpeed = 0.5f;
            maxBrakeDecelPerSecond = 1f; maxReverseAccelPerSecond = 5.06f;
            enableSlopeDriveAssist = true;
            slopeDriveAssistMaxAccel = 7.5f;
            slopeDriveAssistMinAngle = 10f;
            slopeDriveAssistMaxAngle = 52f;
            slopeDriveAssistRiseSpeed = 32f;
            slopeDriveAssistFallSpeed = 48f;
            slopeDriveAssistDisableOnBoost = true;
            slopeDriveAssistDisableOnIce = true;
        }

        if (_steeringConfig != null)
        {
            turnSpeed = _steeringConfig.TurnSpeed;
            minSpeedToSteer = _steeringConfig.MinSpeedToSteer;
            allowSteerWhenTryingToMove = _steeringConfig.AllowSteerWhenTryingToMove;
            lowSpeedSteerMultiplier = _steeringConfig.LowSpeedSteerMultiplier;
            highSpeedSteerMultiplier = _steeringConfig.HighSpeedSteerMultiplier;
            speedForSteerCurve = _steeringConfig.SpeedForSteerCurve;
            steeringInputSmooth = _steeringConfig.SteeringInputSmooth;
            steeringReturnSmooth = _steeringConfig.SteeringReturnSmooth;
            useAutoAlignToVelocity = _steeringConfig.UseAutoAlignToVelocity;
            autoAlignStrength = _steeringConfig.AutoAlignStrength;
            enableIceSteerRamp = _steeringConfig.EnableIceSteerRamp;
            iceSteerRampUpRate = _steeringConfig.IceSteerRampUpRate;
            iceSteerRampDownRate = _steeringConfig.IceSteerRampDownRate;
            iceSteerMinFactor = _steeringConfig.IceSteerMinFactor;
            iceSteerFlipPenalty = _steeringConfig.IceSteerFlipPenalty;
            invertSteeringWhenReversing = _steeringConfig.InvertSteeringWhenReversing;
            reverseSteerMultiplier = _steeringConfig.ReverseSteerMultiplier;
            baseSteeringDamp = _steeringConfig.BaseSteeringDamp;
            enableSteerTraction = _steeringConfig.EnableSteerTraction;
            steerTractionReorientRate = _steeringConfig.SteerTractionReorientRate;
            steerRollingAccel = _steeringConfig.SteerRollingAccel;
            minSpeedForSteerTraction = _steeringConfig.MinSpeedForSteerTraction;
            lateralFrictionWhileSteering = _steeringConfig.LateralFrictionWhileSteering;
            steerTractionBlendIn = _steeringConfig.SteerTractionBlendIn;
            steerTractionBlendOut = _steeringConfig.SteerTractionBlendOut;
            steerRollingAccelCoastMultiplier = _steeringConfig.SteerRollingAccelCoastMultiplier;
            applySteerRollingAccelOnIce = _steeringConfig.ApplySteerRollingAccelOnIce;
        }
        else
        {
            turnSpeed = 11f; minSpeedToSteer = 0.4f; allowSteerWhenTryingToMove = true;
            lowSpeedSteerMultiplier = 8f; highSpeedSteerMultiplier = 1.3f; speedForSteerCurve = 3.45f;
            steeringInputSmooth = 9f; steeringReturnSmooth = 0f; useAutoAlignToVelocity = false; autoAlignStrength = 3f;
            enableIceSteerRamp = true; iceSteerRampUpRate = 10f; iceSteerRampDownRate = 1.83f;
            iceSteerMinFactor = 0.755f; iceSteerFlipPenalty = 0.35f;
            invertSteeringWhenReversing = true; reverseSteerMultiplier = 1f;
            baseSteeringDamp = 8f; enableSteerTraction = true; steerTractionReorientRate = 5.59f;
            steerRollingAccel = 2.25f; minSpeedForSteerTraction = 0.1f; lateralFrictionWhileSteering = 2.46f;
            steerTractionBlendIn = 8.21f; steerTractionBlendOut = 7.1f; steerRollingAccelCoastMultiplier = 0.441f;
            applySteerRollingAccelOnIce = false;
        }

        if (_landingConfig != null)
        {
            skipSpeedClampWhileAirborne = _landingConfig.SkipSpeedClampWhileAirborne;
            enableLandingCarrySpeed = _landingConfig.EnableLandingCarrySpeed;
            landingExcessBleedPerSecond = _landingConfig.LandingExcessBleedPerSecond;
            landingNoClampGraceSeconds = _landingConfig.LandingNoClampGraceSeconds;
            enableLandingBoost = _landingConfig.EnableLandingBoost;
            landingBoostStrength = _landingConfig.LandingBoostStrength;
            landingBoostDuration = _landingConfig.LandingBoostDuration;
            landingBoostFalloff = _landingConfig.LandingBoostFalloff;
        }
        else
        {
            skipSpeedClampWhileAirborne = true; enableLandingCarrySpeed = true;
            landingExcessBleedPerSecond = 7.17f; landingNoClampGraceSeconds = 0.08f;
            enableLandingBoost = true; landingBoostStrength = 1f; landingBoostDuration = 1.2f; landingBoostFalloff = 1.5f;
        }

        if (_driftConfig != null)
        {
            requireDriftUnlock = _driftConfig.RequireDriftUnlock;
            driftKey = _driftConfig.DriftKey;
            driftMinSpeed = _driftConfig.DriftMinSpeed;
            maxDriftSteerMultiplier = _driftConfig.MaxDriftSteerMultiplier;
            driftSteeringInputBuildupMultiplier = _driftConfig.DriftSteeringInputBuildupMultiplier;
            driftBuildRate = _driftConfig.DriftBuildRate;
            driftReleaseRate = _driftConfig.DriftReleaseRate;
            driftSideForce = _driftConfig.DriftSideForce;
            driftSpeedDecayPerSecond = _driftConfig.DriftSpeedDecayPerSecond;
            driftHeldSpeedDecayPerSecond = _driftConfig.DriftHeldSpeedDecayPerSecond;
            driftForwardAccelMultiplier = _driftConfig.DriftForwardAccelMultiplier;
            useFullAccelWhileDrifting = _driftConfig.UseFullAccelWhileDrifting;
            lockToDriftPeakSpeed = _driftConfig.LockToDriftPeakSpeed;
            driftBrakeDecayPerSecond = _driftConfig.DriftBrakeDecayPerSecond;
            requireDirectionalInputForDriftCharge = _driftConfig.RequireDirectionalInputForDriftCharge;
            driftNeutralDrainRate = _driftConfig.DriftNeutralDrainRate;
            driftNeutralFullResetDelay = _driftConfig.DriftNeutralFullResetDelay;
            resetDriftChargeOnSteerFlip = _driftConfig.ResetDriftChargeOnSteerFlip;
            steerFlipRetainedCharge = _driftConfig.SteerFlipRetainedCharge;
            steerFlipThreshold = _driftConfig.SteerFlipThreshold;
            minChargeForFlipReset = _driftConfig.MinChargeForFlipReset;
            steerFlipRebuildDelay = _driftConfig.SteerFlipRebuildDelay;
            allowDriftGlideWithoutSteer = _driftConfig.AllowDriftGlideWithoutSteer;
            driftGlideDecayPerSecond = _driftConfig.DriftGlideDecayPerSecond;
            closeCallBoostBaseDuration = _driftConfig.CloseCallBoostBaseDuration;
            closeCallBoostForce = _driftConfig.CloseCallBoostForce;
            closeCallBoostForceMode = _driftConfig.CloseCallBoostForceMode;
            closeCallBoostMaxSpeedMult = _driftConfig.CloseCallBoostMaxSpeedMult;
            closeCallInvincibilityTint = _driftConfig.CloseCallInvincibilityTint;
            closeCallTintColor = _driftConfig.CloseCallTintColor;
            closeCallTintStrength = _driftConfig.CloseCallTintStrength;
            invincibilityBumpForceAway = _driftConfig.InvincibilityBumpForceAway;
            invincibilityBumpForceUp = _driftConfig.InvincibilityBumpForceUp;
            invincibilityBumpTorque = _driftConfig.InvincibilityBumpTorque;
            enableCloseCallNearMisses = _driftConfig.EnableCloseCallNearMisses;
            closeCallDistance = _driftConfig.CloseCallDistance;
            closeCallMinSpeed = _driftConfig.CloseCallMinSpeed;
            closeCallCooldown = _driftConfig.CloseCallCooldown;
            closeCallRootCooldown = _driftConfig.CloseCallRootCooldown;
            iceFrictionTransitionSpeed = _driftConfig.IceFrictionTransitionSpeed;
            iceHandlingTransitionSpeed = _driftConfig.IceHandlingTransitionSpeed;
            iceLateralSlideForce = _driftConfig.IceLateralSlideForce;
            iceVelocityAlignmentStrength = _driftConfig.IceVelocityAlignmentStrength;
        }
        else
        {
            requireDriftUnlock = true; driftKey = KeyCode.LeftShift; driftMinSpeed = 2.15f;
            maxDriftSteerMultiplier = 3.1f; driftSteeringInputBuildupMultiplier = 0.55f; driftBuildRate = 0.7f; driftReleaseRate = 12.2f;
            driftSideForce = 0.41f; driftSpeedDecayPerSecond = 0.06f; driftHeldSpeedDecayPerSecond = 0f;
            driftForwardAccelMultiplier = 0f; useFullAccelWhileDrifting = true; lockToDriftPeakSpeed = true;
            driftBrakeDecayPerSecond = 0.001f; requireDirectionalInputForDriftCharge = false;
            driftNeutralDrainRate = 2.6f; driftNeutralFullResetDelay = 3.65f; resetDriftChargeOnSteerFlip = true;
            steerFlipRetainedCharge = 0.055f; steerFlipThreshold = 0.15f; minChargeForFlipReset = 0f;
            steerFlipRebuildDelay = 0.1f; allowDriftGlideWithoutSteer = true; driftGlideDecayPerSecond = 0.15f;
            closeCallBoostBaseDuration = 0.9f; closeCallBoostForce = 9f; closeCallBoostForceMode = ForceMode.VelocityChange;
            closeCallBoostMaxSpeedMult = 1.3f; enableCloseCallNearMisses = true; closeCallDistance = 0.45f;
            closeCallMinSpeed = 2.88f; closeCallCooldown = 1.42f; closeCallRootCooldown = 4.39f;
            iceFrictionTransitionSpeed = 3f; iceHandlingTransitionSpeed = 4f; iceLateralSlideForce = 0.1f;
            iceVelocityAlignmentStrength = 0.02f;
        }

        if (_boostConfig != null)
        {
            requireBoostUnlock = _boostConfig.RequireBoostUnlock;
            boostFlashSpeedThreshold = _boostConfig.BoostFlashSpeedThreshold;
            boostFlashCooldown = _boostConfig.BoostFlashCooldown;
            boostKey = _boostConfig.BoostKey;
            boostForce = _boostConfig.BoostForce;
            boostSustainAcceleration = _boostConfig.BoostSustainAcceleration;
            boostDuration = _boostConfig.BoostDuration;
            boostMaxSpeedMultiplier = _boostConfig.BoostMaxSpeedMultiplier;
            postBoostSlowdownDuration = _boostConfig.PostBoostSlowdownDuration;
            boostCooldown = _boostConfig.BoostCooldown;
            boostFuelCost = _boostConfig.BoostFuelCost;
            driftBoostSustainAcceleration = _boostConfig.DriftBoostSustainAcceleration;
            enableDriftHeldBoost = _boostConfig.EnableDriftHeldBoost;
            driftBoostMinHoldSeconds = _boostConfig.DriftBoostMinHoldSeconds;
            driftBoostMaxHoldSeconds = _boostConfig.DriftBoostMaxHoldSeconds;
            driftBoostForceRange = _boostConfig.DriftBoostForceRange;
            driftBoostDurationRange = _boostConfig.DriftBoostDurationRange;
            driftBoostMaxSpeedMultRange = _boostConfig.DriftBoostMaxSpeedMultRange;
            driftBoostFuelCost = _boostConfig.DriftBoostFuelCost;
            driftBoostCooldown = _boostConfig.DriftBoostCooldown;
        }
        else
        {
            requireBoostUnlock = true; boostFlashSpeedThreshold = 2f; boostFlashCooldown = 0.3f;
            boostKey = KeyCode.Space; boostForce = 30f; boostSustainAcceleration = 30f; boostDuration = 0.35f;
            boostMaxSpeedMultiplier = 1.65f; postBoostSlowdownDuration = 2f; boostCooldown = 5f; boostFuelCost = 15f;
            driftBoostSustainAcceleration = 30f; enableDriftHeldBoost = true; driftBoostMinHoldSeconds = 0.8f;
            driftBoostMaxHoldSeconds = 2.5f; driftBoostCooldown = 3.3f;
        }

        if (_fuelConfig != null)
        {
            maxFuel = _fuelConfig.MaxFuel;
            fuelUsePerSecondAtFullThrottle = _fuelConfig.FuelUsePerSecondAtFullThrottle;
            fuelUsePerSecondBraking = _fuelConfig.FuelUsePerSecondBraking;
            idleFuelUsePerSecond = _fuelConfig.IdleFuelUsePerSecond;
            idleSpeedThreshold = _fuelConfig.IdleSpeedThreshold;
        }
        else
        {
            maxFuel = 100f; fuelUsePerSecondAtFullThrottle = 0f; fuelUsePerSecondBraking = 0f;
            idleFuelUsePerSecond = 0f; idleSpeedThreshold = 0.5f;
        }

        if (_healthConfig != null)
        {
            maxHP = _healthConfig.MaxHP;
            crashDamageSeverityScale = _healthConfig.CrashDamageSeverityScale;
            baseHpRegenPerSecond = _healthConfig.BaseHpRegenPerSecond;
            performanceAtZeroHP = _healthConfig.PerformanceAtZeroHP;
            degradeStartHPFraction = _healthConfig.DegradeStartHPFraction;
            enableDamageMalfunction = _healthConfig.EnableDamageMalfunction;
            maxMalfunctionChancePerSecond = _healthConfig.MaxMalfunctionChancePerSecond;
            malfunctionBurstDuration = _healthConfig.MalfunctionBurstDuration;
            malfunctionCooldown = _healthConfig.MalfunctionCooldown;
            malfunctionThrottleMultiplier = _healthConfig.MalfunctionThrottleMultiplier;
            useSmoothMalfunction = _healthConfig.UseSmoothMalfunction;
            minimumAccelerationFloor = _healthConfig.MinimumAccelerationFloor;
            minimumMaxSpeedFloor = _healthConfig.MinimumMaxSpeedFloor;
            _lowHpMalfunctionChanceBiasExponent = _healthConfig.LowHpMalfunctionChanceBiasExponent;
            crashDamageCooldown = _healthConfig.CrashDamageCooldown;
            crashImpactVFX = _healthConfig.CrashImpactVFX;
            crashImpactVFXLifetime = _healthConfig.CrashImpactVFXLifetime;
            damageSmokeVFX = _healthConfig.DamageSmokeVFX;
            smokeStartHPFraction = _healthConfig.SmokeStartHPFraction;
            smokeMinRate = _healthConfig.SmokeMinRate;
            smokeMaxRate = _healthConfig.SmokeMaxRate;
            smokeMinSize = _healthConfig.SmokeMinSize;
            smokeMaxSize = _healthConfig.SmokeMaxSize;
            smokeColorAtThreshold = _healthConfig.SmokeColorAtThreshold;
            smokeColorAtZeroHP = _healthConfig.SmokeColorAtZeroHP;
            invertSmokeColorLerp = _healthConfig.InvertSmokeColorLerp;
        }
        else
        {
            maxHP = 20f; crashDamageSeverityScale = 1f; baseHpRegenPerSecond = 0f;
            performanceAtZeroHP = 0.28f; degradeStartHPFraction = 0.65f; enableDamageMalfunction = true;
            maxMalfunctionChancePerSecond = 1.15f; _lowHpMalfunctionChanceBiasExponent = 0.42f;
            minimumAccelerationFloor = 2.5f; minimumMaxSpeedFloor = 5f;
            crashDamageCooldown = 0.35f; smokeStartHPFraction = 0.5f; smokeMinRate = 5f; smokeMaxRate = 30f;
            smokeMinSize = 0.5f; smokeMaxSize = 1.6f; invertSmokeColorLerp = false;
        }

        if (_groundConfig != null)
        {
            groundLayers = _groundConfig.GroundLayers;
            samplesX = _groundConfig.SamplesX;
            samplesZ = _groundConfig.SamplesZ;
            raycastHeightOffset = _groundConfig.RaycastHeightOffset;
            raycastExtraDistance = _groundConfig.RaycastExtraDistance;
            debugSurfaceRays = _groundConfig.DebugSurfaceRays;
            surfaceSampleExtent = _groundConfig.SurfaceSampleExtent;
            useTippedOverWorldDownSampler = _groundConfig.UseTippedOverWorldDownSampler;
            tippedOverSurfaceUpDotThreshold = _groundConfig.TippedOverSurfaceUpDotThreshold;
            groundNormalBlendRate = _groundConfig.GroundNormalBlendRate;
            groundNormalMixedSurfaceBlendScale = _groundConfig.GroundNormalMixedSurfaceBlendScale;
            groundNormalMixedGrassMin = _groundConfig.GroundNormalMixedGrassMin;
            groundNormalMixedGrassMax = _groundConfig.GroundNormalMixedGrassMax;
            roadGrassTransitionLiftSpeed = _groundConfig.RoadGrassTransitionLiftSpeed;
            roadGrassTransitionMinSpeed = _groundConfig.RoadGrassTransitionMinSpeed;
            roadGrassTransitionLiftCooldown = _groundConfig.RoadGrassTransitionLiftCooldown;
            deathHpTerrainRayLength = _groundConfig.DeathHpTerrainRayLength;
            deathHpTerrainRayStartHeight = _groundConfig.DeathHpTerrainRayStartHeight;
            deathHpGrassDragBoost = _groundConfig.DeathHpGrassDragBoost;
            deathHpGrassAngularDragBoost = _groundConfig.DeathHpGrassAngularDragBoost;
            deathHpGrassPlanarDampingPerSecond = _groundConfig.DeathHpGrassPlanarDampingPerSecond;
            deathHpGrassPlanarDampingPerSpeed = _groundConfig.DeathHpGrassPlanarDampingPerSpeed;
            deathHpGrassRigidbodyDragScale = _groundConfig.DeathHpGrassRigidbodyDragScale;
            deathHpGrassFrictionScale = _groundConfig.DeathHpGrassFrictionScale;
            deathHpGrassTumbleMaxPlanarSpeed = _groundConfig.DeathHpGrassTumbleMaxPlanarSpeed;
        }
        else
        {
            samplesX = 6; samplesZ = 6; raycastHeightOffset = 0.5f; raycastExtraDistance = -0.72f;
            debugSurfaceRays = true; surfaceSampleExtent = 1.13f;
            useTippedOverWorldDownSampler = true;
            tippedOverSurfaceUpDotThreshold = 0.55f;
            groundNormalBlendRate = 14f;
            groundNormalMixedSurfaceBlendScale = 0.42f;
            groundNormalMixedGrassMin = 0.06f;
            groundNormalMixedGrassMax = 0.94f;
            roadGrassTransitionLiftSpeed = 0.35f;
            roadGrassTransitionMinSpeed = 2.5f;
            roadGrassTransitionLiftCooldown = 0.25f;
            deathHpTerrainRayLength = 56f;
            deathHpTerrainRayStartHeight = 0.85f;
            deathHpGrassDragBoost = 1.45f;
            deathHpGrassAngularDragBoost = 1.35f;
            deathHpGrassPlanarDampingPerSecond = 6f;
            deathHpGrassPlanarDampingPerSpeed = 0.12f;
            deathHpGrassRigidbodyDragScale = 2.35f;
            deathHpGrassFrictionScale = 1.65f;
            deathHpGrassTumbleMaxPlanarSpeed = 11f;
        }

        if (_rampConfig != null)
        {
            enableRampAlignment = _rampConfig.EnableRampAlignment;
            groundAlignSpeed = _rampConfig.GroundAlignSpeed;
            airAlignSpeed = _rampConfig.AirAlignSpeed;
            groundNormalCastRadius = _rampConfig.GroundNormalCastRadius;
            groundNormalCheckDistance = _rampConfig.GroundNormalCheckDistance;
            landingPredictDistance = _rampConfig.LandingPredictDistance;
            landingAlignStartDistance = _rampConfig.LandingAlignStartDistance;
        }
        else
        {
            enableRampAlignment = true; groundAlignSpeed = 10f; airAlignSpeed = 6f;
            groundNormalCastRadius = 0.35f; groundNormalCheckDistance = 1.23f;
            landingPredictDistance = 2.75f; landingAlignStartDistance = 1.97f;
        }

        if (_crashMashConfig != null)
        {
            drainScalePerClickPower = _crashMashConfig.DrainScalePerClickPower;
            drainScalePerPassiveStrength = _crashMashConfig.DrainScalePerPassiveStrength;
            maxSkillDrainMultiplier = _crashMashConfig.MaxSkillDrainMultiplier;
            minSkillDrainMultiplier = _crashMashConfig.MinSkillDrainMultiplier;
            randomizeMashFaceButton = _crashMashConfig.RandomizeMashFaceButton;
            allowSpaceMashInEditor = _crashMashConfig.AllowSpaceMashInEditor;
            mashBaseFuelPerClick = _crashMashConfig.MashBaseFuelPerClick;
            mashFuelSpeedBonusMax = _crashMashConfig.MashFuelSpeedBonusMax;
            mashMaxSpeedThreshold = _crashMashConfig.MashMaxSpeedThreshold;
            mashMinSpeedThreshold = _crashMashConfig.MashMinSpeedThreshold;
            useMultiplicativeMashClicks = _crashMashConfig.UseMultiplicativeMashClicks;
            mashBaseClicks = _crashMashConfig.MashBaseClicks;
            mashDistanceWeight = _crashMashConfig.MashDistanceWeight;
            mashDistanceExponent = _crashMashConfig.MashDistanceExponent;
            mashSeverityWeight = _crashMashConfig.MashSeverityWeight;
            mashSeverityExponent = _crashMashConfig.MashSeverityExponent;
            mashCrashCountWeight = _crashMashConfig.MashCrashCountWeight;
            mashSeveritySumWeight = _crashMashConfig.MashSeveritySumWeight;
            enableCrashRecoveryAlways = _crashMashConfig.EnableCrashRecoveryAlways;
            flippedClickMultiplier = _crashMashConfig.FlippedClickMultiplier;
            airborneClickMultiplier = _crashMashConfig.AirborneClickMultiplier;
            crashLayers = _crashMashConfig.CrashLayers;
            minImpactSpeed = _crashMashConfig.MinImpactSpeed;
            maxImpactSpeed = _crashMashConfig.MaxImpactSpeed;
            minCrashDuration = _crashMashConfig.MinCrashDuration;
            maxCrashDuration = _crashMashConfig.MaxCrashDuration;
            impulsePerUnitSpeed = _crashMashConfig.ImpulsePerUnitSpeed;
            torquePerUnitSpeed = _crashMashConfig.TorquePerUnitSpeed;
            maxCrashFlingSpeed = _crashMashConfig.MaxCrashFlingSpeed;
            crashDragMultiplier = _crashMashConfig.CrashDragMultiplier;
            crashAngularDrag = _crashMashConfig.CrashAngularDrag;
            enablePopupText = _crashMashConfig.EnablePopupText;
            popupVerticalOffset = _crashMashConfig.PopupVerticalOffset;
            minHPDamageForPopup = _crashMashConfig.MinHPDamageForPopup;
            minFuelLossForPopup = _crashMashConfig.MinFuelLossForPopup;
            minFuelGainForPopup = _crashMashConfig.MinFuelGainForPopup;
            mashFuelPopupHorizontalRange = _crashMashConfig.MashFuelPopupHorizontalRange;
            mashFuelPopupVerticalRange = _crashMashConfig.MashFuelPopupVerticalRange;
            enableMashScreenShake = _crashMashConfig.EnableMashScreenShake;
            mashShakeDuration = _crashMashConfig.MashShakeDuration;
            mashShakeStrength = _crashMashConfig.MashShakeStrength;
            mashShakeVibrato = _crashMashConfig.MashShakeVibrato;
            mashShakeRandomness = _crashMashConfig.MashShakeRandomness;
            crashYawTorqueMultiplier = _crashMashConfig.CrashYawTorqueMultiplier;
            crashRollTorqueMultiplier = _crashMashConfig.CrashRollTorqueMultiplier;
            crashPitchTorqueMultiplier = _crashMashConfig.CrashPitchTorqueMultiplier;
            reorientDuration = _crashMashConfig.ReorientDuration;
            groundedDurationRequired = _crashMashConfig.GroundedDurationRequired;
            groundCheckDistance = _crashMashConfig.GroundCheckDistance;
            groundCheckLayers = _crashMashConfig.GroundCheckLayers;
            enableFlipRecoveryMash = _crashMashConfig.EnableFlipRecoveryMash;
            flipDotThreshold = _crashMashConfig.FlipDotThreshold;
            flipAngleThreshold = _crashMashConfig.FlipAngleThreshold;
            mashUiHideSecondsOnExtraHit = Mathf.Max(0f, _crashMashConfig.MashUiHideSecondsOnExtraHit);
            mashClicksMin = _crashMashConfig.MashClicksMin;
            mashClicksMaxFromSeverity = _crashMashConfig.MashClicksMaxFromSeverity;
            mashClicksPerCrash = _crashMashConfig.MashClicksPerCrash;
            mashClicksMaxFromCrashCount = _crashMashConfig.MashClicksMaxFromCrashCount;
            mashClicksPerDistanceUnit = _crashMashConfig.MashClicksPerDistanceUnit;
            mashDistanceUnit = _crashMashConfig.MashDistanceUnit;
            mashClicksMaxFromDistance = _crashMashConfig.MashClicksMaxFromDistance;
            mashClicksByProgress = _crashMashConfig.MashClicksByProgress;
            mashProgressTotalDistanceFallback = _crashMashConfig.MashProgressTotalDistanceFallback;
            mashClicksRandomVariance = _crashMashConfig.MashClicksRandomVariance;
            mashClicksAbsoluteMin = _crashMashConfig.MashClicksAbsoluteMin;
            mashClicksAbsoluteMax = _crashMashConfig.MashClicksAbsoluteMax;
            baseClicksPerClick = _crashMashConfig.BaseClicksPerClick;
            basePassiveClickRate = _crashMashConfig.BasePassiveClickRate;
            basePassiveClickStrength = _crashMashConfig.BasePassiveClickStrength;
            mashDifficultyPerClickPowerStep = Mathf.Max(1f, _crashMashConfig.MashDifficultyPerClickPowerStep);
            enableMashProgressGauge = _crashMashConfig.EnableMashProgressGauge;
            gaugeFillPerClick = _crashMashConfig.GaugeFillPerClick;
            gaugeFillSpeedBonus = _crashMashConfig.GaugeFillSpeedBonus;
            gaugeFillSpeedMultiplier = Mathf.Max(0f, _crashMashConfig.GaugeFillSpeedMultiplier);
            gaugeGoodThreshold = _crashMashConfig.GaugeGoodThreshold;
            gaugeMaxThreshold = Mathf.Clamp(_crashMashConfig.GaugeMaxThreshold, gaugeGoodThreshold + 0.01f, 0.999f);
            gaugeMultiplierAtZero = _crashMashConfig.GaugeMultiplierAtZero;
            gaugeMultiplierAtGood = _crashMashConfig.GaugeMultiplierAtGood;
            gaugeMultiplierAtMax = _crashMashConfig.GaugeMultiplierAtMax;
            mashDrainsHealth = _crashMashConfig.MashDrainsHealth;
            mashDrainsFuel = _crashMashConfig.MashDrainsFuel;
            mashHealthDrainAtMinSeverity = _crashMashConfig.MashHealthDrainAtMinSeverity;
            mashHealthDrainAtMaxSeverity = _crashMashConfig.MashHealthDrainAtMaxSeverity;
            mashHealthDrainPerCrash = _crashMashConfig.MashHealthDrainPerCrash;
            mashHealthDrainCap = _crashMashConfig.MashHealthDrainCap;
            mashFuelDrainAtMinSeverity = _crashMashConfig.MashFuelDrainAtMinSeverity;
            mashFuelDrainAtMaxSeverity = _crashMashConfig.MashFuelDrainAtMaxSeverity;
            mashFuelDrainPerCrash = _crashMashConfig.MashFuelDrainPerCrash;
            mashFuelDrainCap = _crashMashConfig.MashFuelDrainCap;
            gaugeDrainAtMinSeverity = _crashMashConfig.GaugeDrainAtMinSeverity;
            gaugeDrainAtMaxSeverity = _crashMashConfig.GaugeDrainAtMaxSeverity;
            gaugeDrainPerCrash = _crashMashConfig.GaugeDrainPerCrash;
            gaugeDrainCap = _crashMashConfig.GaugeDrainCap;
            enableSprocketRewards = _crashMashConfig.EnableSprocketRewards;
            sprocketBasePercent = _crashMashConfig.SprocketBasePercent;
            sprocketGaugeBonusMax = _crashMashConfig.SprocketGaugeBonusMax;
            sprocketMinReward = _crashMashConfig.SprocketMinReward;
            sprocketMaxReward = _crashMashConfig.SprocketMaxReward;
            flipUprightLift = _crashMashConfig.FlipUprightLift;
            surfaceMaxSpeedLerpRate = _crashMashConfig.SurfaceMaxSpeedLerpRate;
            closeCallAfterCrashBlockTime = _crashMashConfig.CloseCallAfterCrashBlockTime;
            perColliderCrashCooldown = _crashMashConfig.PerColliderCrashCooldown;
            _crashSeverityConfig = _crashMashConfig.CrashSeverityConfig;
            _crashFuelDamageScale = _crashSeverityConfig != null ? _crashSeverityConfig.FuelDamageScaleRelativeToHp : 1f;
        }
        else
        {
            perColliderCrashCooldown = 0.5f; surfaceMaxSpeedLerpRate = 2.78f; closeCallAfterCrashBlockTime = 1f;
            mashDifficultyPerClickPowerStep = 1.4f;
            mashUiHideSecondsOnExtraHit = 0.45f;
            _crashSeverityConfig = null;
            _crashFuelDamageScale = 1f;
        }

        if (_vfxAudioConfig != null)
        {
            cameraShakeTarget = _vfxAudioConfig.CameraShakeTarget;
            screenShakeGlobalMultiplier = _vfxAudioConfig.ScreenShakeGlobalMultiplier;
            screenShakeReturnSpeed = _vfxAudioConfig.ScreenShakeReturnSpeed;
            deathVFX = _vfxAudioConfig.DeathVFX;
            deathVFXLifetime = _vfxAudioConfig.DeathVFXLifetime;
            deathExplodeClip = _vfxAudioConfig.DeathExplodeClip;
            deathExplodeVolume = _vfxAudioConfig.DeathExplodeVolume;
            deathExplodeUseSpatial = _vfxAudioConfig.DeathExplodeUseSpatial;
            deathExplodeSpatialBlend = _vfxAudioConfig.DeathExplodeSpatialBlend;
            deathExplodeRolloff = _vfxAudioConfig.DeathExplodeRolloff;
            deathExplodeMinDistance = _vfxAudioConfig.DeathExplodeMinDistance;
            deathExplodeMaxDistance = _vfxAudioConfig.DeathExplodeMaxDistance;
            deathExplodeVolumeMultiplier = _vfxAudioConfig.DeathExplodeVolumeMultiplier;
            deathExplodePitchMin = _vfxAudioConfig.DeathExplodePitchMin;
            deathExplodePitchMax = _vfxAudioConfig.DeathExplodePitchMax;
            deathExplodeSfxCooldown = _vfxAudioConfig.DeathExplodeSfxCooldown;
            defaultBoostRampDownFraction = _vfxAudioConfig.DefaultBoostRampDownFraction;
            closeCallBoostRampDownFraction = _vfxAudioConfig.CloseCallBoostRampDownFraction;
            regularBoostRampDownFraction = _vfxAudioConfig.RegularBoostRampDownFraction;
            grassFuelUseMultiplier = _vfxAudioConfig.GrassFuelUseMultiplier;
            offDefaultFraction = _vfxAudioConfig.OffDefaultFraction;
            grassFraction = _vfxAudioConfig.GrassFraction;
            crashClipDefault = _vfxAudioConfig.CrashClipDefault;
            crashClipHonk = _vfxAudioConfig.CrashClipHonk;
            crashSfxVolume = _vfxAudioConfig.CrashSfxVolume;
            crashUseSpatial = _vfxAudioConfig.CrashUseSpatial;
            crashSpatialBlend = _vfxAudioConfig.CrashSpatialBlend;
            crashRolloff = _vfxAudioConfig.CrashRolloff;
            crashMinDistance = _vfxAudioConfig.CrashMinDistance;
            crashMaxDistance = _vfxAudioConfig.CrashMaxDistance;
            crashVolumeMultiplier = _vfxAudioConfig.CrashVolumeMultiplier;
            crashPitchMin = _vfxAudioConfig.CrashPitchMin;
            crashPitchMax = _vfxAudioConfig.CrashPitchMax;
        }
        else
        {
            screenShakeGlobalMultiplier = 1f; screenShakeReturnSpeed = 18f;
            defaultBoostRampDownFraction = 0.35f; closeCallBoostRampDownFraction = 0.5f; regularBoostRampDownFraction = 0.25f;
            grassFuelUseMultiplier = 1.5f; offDefaultFraction = 0f; grassFraction = 0f;
        }
    }

    // NEW: split for steering traction code etc.
    private void Awake()
    {
        _movementConfig = GetComponent<CarMovementConfig>();
        _steeringConfig = GetComponent<CarSteeringConfig>();
        _landingConfig = GetComponent<CarLandingConfig>();
        _driftConfig = GetComponent<CarDriftConfig>();
        _boostConfig = GetComponent<CarBoostConfig>();
        _fuelConfig = GetComponent<CarFuelConfig>();
        _healthConfig = GetComponent<CarHealthConfig>();
        _groundConfig = GetComponent<CarGroundConfig>();
        _rampConfig = GetComponent<CarRampConfig>();
        _crashMashConfig = GetComponent<CarCrashMashConfig>();
        _vfxAudioConfig = GetComponent<CarVFXAudioConfig>();
        _forcefield = GetComponent<CarForcefield>();
        ApplyConfigToFields();
        CacheDamageSmokeSystems();
        ForceDisableDamageSmokeVFXImmediate();

        Instance = this;

        if (cameraShakeTarget == null)
            cameraShakeTarget = Camera.main != null ? Camera.main.transform : null;


        rb = GetComponent<Rigidbody>();
        carCollider = GetComponent<Collider>();
        boxCollider = carCollider as BoxCollider;

        rb.interpolation = RigidbodyInterpolation.Interpolate;
        rb.collisionDetectionMode = CollisionDetectionMode.Continuous;
        rb.freezeRotation = true;
        rb.velocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;

        rb.drag = baseDrag;
        rb.angularDrag = 0.25f;

        _baseDrag = rb.drag;
        _baseAngularDrag = rb.angularDrag;

        Vector3 flatForward = new Vector3(transform.forward.x, 0f, transform.forward.z).normalized;
        if (flatForward.sqrMagnitude > 0.0001f)
            transform.rotation = Quaternion.LookRotation(flatForward, Vector3.up);

        if (carCollider != null)
        {
            _carPhysicMaterial = carCollider.material;
            if (_carPhysicMaterial != null)
            {
                _originalDynamicFriction = _carPhysicMaterial.dynamicFriction;
                _originalStaticFriction = _carPhysicMaterial.staticFriction;
            }
            else
            {
                // Create a default physic material if none exists
                _carPhysicMaterial = new PhysicMaterial("CarPhysicMat");
                _carPhysicMaterial.dynamicFriction = .18f;
                _carPhysicMaterial.staticFriction = 0f;
                _carPhysicMaterial.frictionCombine = PhysicMaterialCombine.Minimum;
                _carPhysicMaterial.bounceCombine = PhysicMaterialCombine.Average;
                _carPhysicMaterial.bounciness = 0f;
                carCollider.material = _carPhysicMaterial;
                _originalDynamicFriction = .18f;
                _originalStaticFriction = 0f;
            }
        }

        _renderers = GetComponentsInChildren<Renderer>(true);
        _mpb = new MaterialPropertyBlock();

        _originalColors.Clear();
        foreach (var r in _renderers)
        {
            if (r.sharedMaterial != null && r.sharedMaterial.HasProperty("_Color"))
            {
                _originalColors[r] = r.sharedMaterial.color;
            }
        }

        _currentIceDynamicFriction = 1f;
        _currentIceStaticFriction = 1f;
        _currentIceHandling = 1f;

        _initialRotation = transform.rotation;

        baseTurnSpeed = turnSpeed;
        currentSteeringDamp = baseSteeringDamp;

        ApplySurfaceMultipliers(1f, 1f, 1f, 1f);

        currentFuel = maxFuel;
        isOutOfFuel = false;
        isOutOfHP = false;
        currentFuelUseMultiplier = 1f;

        baseMaxFuel = maxFuel;
        baseMaxHP = maxHP;
        baseIdleFuelUse = idleFuelUsePerSecond;
        baseFuelUseFullThrottle = fuelUsePerSecondAtFullThrottle;
        baseFuelUseBraking = fuelUsePerSecondBraking;

        baseBoostForce = boostForce;
        baseBoostSustainAcceleration = boostSustainAcceleration;
        baseBoostDuration = boostDuration;
        baseBoostMaxSpeedMult = boostMaxSpeedMultiplier;
        baseBoostCooldown = boostCooldown;
        baseDriftBoostCooldown = driftBoostCooldown;
        baseBoostFuelCost = boostFuelCost;
        boostUnlocked = !requireBoostUnlock;

        driftUnlocked = !requireDriftUnlock;

        currentHP = Mathf.Max(1f, maxHP);

        groundCheckLayers = groundLayers;
        RefreshSkillEffects();
        ApplySkillEffects();

        // Ensure we never leave stats invalid (e.g. skill chain returned 0 before ready)
        if (maxHP <= 0f && baseMaxHP > 0f)
        {
            maxHP = baseMaxHP;
            currentHP = maxHP;
        }
        _statsInitialized = true;

        UpdateDamageVFXImmediate();

        _smoothedSurfaceMaxSpeed = -1f;

    }

    private void OnEnable()
    {
        WireManagerEvents();
        UpdateDriftUnlock();
        UpdateBoostUnlock();
        RefreshSkillEffects();
        ApplySkillEffects();
    }

    private void OnDisable()
    {
        UnwireManagerEvents();
    }

    private void Update()
    {

        if (IsFlipMashUiVisible && GetMashRequiredButtonDown())
        {
            RegisterFlipMashClick();
        }

        if (Input.GetKeyDown(KeyCode.F9))
        {
            var mgr = RacingSkillTreeManager.Instance;
            if (mgr != null)
            {
                mgr.ToggleSkills();
            }
        }

        UpdateCrashReorientation();

        HandleInput();

        if (GetBoostDown() && !IsCrashInvulnerable && Time.time >= _boostBlockedUntil && !isOutOfFuel && !isOutOfHP)
            _boostRequested = true;

        if (!_inCrash && hpRegenPerSecond > 0f && currentHP < maxHP)
        {
            currentHP = Mathf.Min(maxHP, currentHP + hpRegenPerSecond * Time.deltaTime);
        }

        if (currentHP <= 0f && !isOutOfHP)
        {
            isOutOfHP = true;
            ForceStopCloseCallEffects();
            // Hard stop boost immediately
            _isBoosting = false;
            _isPostBoost = false;
            _boostRequested = false;

            PlayDeathVFX();
        }

        bool brakeHeldNow = GetBrakeKeyOrTrigger();
        if (brakeHeldNow)
        {
            // Kills active/queued boost + locks out boosts (your existing behavior)
            if (_boostRequested || _isBoosting || _isPostBoost || _driftHoldTimeSeconds > 0f || _boostOverrideActive)
                CancelAllBoostState(0f);

            // PATCH: also kill stored drift-held boost charge immediately
            if (_driftHoldTimeSeconds > 0f || _driftHoldDirectionSign != 0)
                ResetDriftHeldTimer();
        }

        UpdateDamageVFXImmediate();
    }

    private void FixedUpdate()
    {
        float dt = Time.fixedDeltaTime;
        var gmEarly = GameManager_Racing.Instance;
        bool runEnded = gmEarly != null && gmEarly.RunEnded;
        // End-of-run: hard-stop (unchanged UX for results / menu).
        if (runEnded)
        {
            if (rb != null)
            {
                rb.velocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
            }
            _isReorienting = false;
            return;
        }

        // Out of HP: no scripted steering — let the Rigidbody tumble.
        if (isOutOfHP)
        {
            ReleaseHandsOffDrivingPhysics(resetDragToDefaults: false);
            // Single downward ray from the car picks terrain (grass vs road); applies drag / caps like alive driving.
            if (carCollider != null)
            {
                ApplyHpDeathTerrainFromGlobalRay();
                ApplyBoostSurfaceForce(true);
                UpdateIcePhysicsTransitions();
                ApplyHpDeathSoftGroundGripAndCap();
            }
            else
                ResetIcePhysicsImmediate();

            _flipMashActive = false;
            _inCrash = false;
            _isReorienting = false;
            _crashTimer = 0f;
            _groundedTime = 0f;
            _isBoosting = false;
            _isPostBoost = false;
            _postBoostTimer = 0f;
            _boostTimer = 0f;
            _boostRequested = false;
            ClearBoostOverride();
            ClearAllSpeedBoosts();
            _closeCallBoosting = false;
            _currentBoostMaxSpeed = 0f;
            _activeBoostMaxMult = 1f;
            return;
        }

        // Out of fuel only: physics tumble, but player can still yaw the car (no throttle/brake/boost pipeline).
        if (isOutOfFuel)
        {
            ReleaseHandsOffDrivingPhysics();
            if (carCollider != null)
            {
                SampleGroundAndUpdateMultipliers();
                ApplyBoostSurfaceForce(true);
                UpdateIcePhysicsTransitions();
            }
            else
                ResetIcePhysicsImmediate();

            _flipMashActive = false;
            _inCrash = false;
            _isReorienting = false;
            _crashTimer = 0f;
            _groundedTime = 0f;
            _isBoosting = false;
            _isPostBoost = false;
            _postBoostTimer = 0f;
            _boostTimer = 0f;
            _boostRequested = false;
            ClearBoostOverride();
            ClearAllSpeedBoosts();
            _closeCallBoosting = false;
            _currentBoostMaxSpeed = 0f;
            _activeBoostMaxMult = 1f;

            UpdateSteeringInputFixed();
            HandleSteering();
            CheckBoostFlash();
            return;
        }

        if (_closeCallInvincible && Time.time >= _closeCallInvincibilityEndTime)
        {
            _closeCallInvincible = false;
            ClearCloseCallTint();
            ScreenFlashManager.StopInvincibility(); // optional now
        }

        // Sync legacy _closeCallBoosting flag with centralized system
        if (_closeCallBoosting && !HasSpeedBoost(BOOST_ID_CLOSE_CALL))
        {
            _closeCallBoosting = false;
        }


        if (_flipMashActive)
        {
            if (_closeCallInvincible && Time.time >= _closeCallInvincibilityEndTime)
            {
                _closeCallInvincible = false;
                ScreenFlashManager.StopInvincibility(); // Stop the continuous pulse
            }

            // Sync legacy _closeCallBoosting flag with centralized system
            if (_closeCallBoosting && !HasSpeedBoost(BOOST_ID_CLOSE_CALL))
            {
                _closeCallBoosting = false;
            }


            if (enableMashProgressGauge && _flipMashActive && IsFlipMashUiVisible)
            {
                float severity = _lastCrashSeverity; // 0 to 1

                // === GAUGE DRAIN (severity + crash count) ===
                float baseDrainRate = Mathf.Lerp(gaugeDrainAtMinSeverity, gaugeDrainAtMaxSeverity, severity);
                float crashBonus = _crashCount * gaugeDrainPerCrash;
                float finalGaugeDrain = Mathf.Min(baseDrainRate + crashBonus, gaugeDrainCap);
                finalGaugeDrain *= _skillDrainMultiplier;
                _mashGaugeValue = Mathf.Max(0f, _mashGaugeValue - finalGaugeDrain * Time.deltaTime);

                if (mashDrainsHealth)
                {
                    float baseHealthDrain = Mathf.Lerp(mashHealthDrainAtMinSeverity, mashHealthDrainAtMaxSeverity, severity);
                    float healthCrashBonus = _crashCount * mashHealthDrainPerCrash;
                    float finalHealthDrain = Mathf.Min(baseHealthDrain + healthCrashBonus, mashHealthDrainCap);

                    currentHP -= finalHealthDrain * Time.deltaTime;

                    if (currentHP <= 0f)
                    {
                        currentHP = 0f;
                        _flipMashActive = false;
                        isOutOfHP = true;
                    }
                }

                // FUEL DRAIN (separate - not else!)
                if (mashDrainsFuel)
                {
                    float baseFuelDrain = Mathf.Lerp(mashFuelDrainAtMinSeverity, mashFuelDrainAtMaxSeverity, severity);
                    float fuelCrashBonus = _crashCount * mashFuelDrainPerCrash;
                    float finalFuelDrain = Mathf.Min(baseFuelDrain + fuelCrashBonus, mashFuelDrainCap);

                    currentFuel -= finalFuelDrain * Time.deltaTime;

                    if (currentFuel <= 0f)
                    {
                        currentFuel = 0f;
                        _flipMashActive = false;
                        bool firstEmpty = !isOutOfFuel;
                        isOutOfFuel = true;
                        if (firstEmpty)
                            NotifyCrashFeedbackOnly(0.72f);
                    }
                }
            }

            // IMPORTANT: Keep sampling ground even during recovery (fixes ice sticking)
            SampleGroundAndUpdateMultipliers();

            // Apply boost surface during recovery
            ApplyBoostSurfaceForce(true);

            // Must run after sampling: friction/handling *current* values lerp toward raycast targets (otherwise ice sticks after leaving ice).
            UpdateIcePhysicsTransitions();

            // === PASSIVE AUTO-CLICKS (Skill-based) ===
            if (effectivePassiveClickRate > 0f && IsFlipMashUiVisible)
            {
                _passiveClickTimer += Time.fixedDeltaTime;
                var skillMgr = RacingSkillTreeManager.Instance;
                bool passiveUnlocked = skillMgr != null && skillMgr.IsPassiveMashUnlocked;

                if (passiveUnlocked && effectivePassiveClickRate > 0f)
                {
                    _passiveClickTimer += Time.fixedDeltaTime;
                    float passiveInterval = 1f / effectivePassiveClickRate;

                    while (_passiveClickTimer >= passiveInterval)
                    {
                        _passiveClickTimer -= passiveInterval;

                        // Award passive clicks
                        _flipMashClicks += effectivePassiveClickStrength;

                        if (effectivePassiveClickStrength > 0)
                            TrySpawnPopupRandomScreen(RacingPopupType.MashClickDamage, effectivePassiveClickStrength);

                        // Give partial fuel for passive clicks
                        float passiveFuelReward = effectiveFuelPerClick * 0.5f;
                        if (passiveFuelReward > 0f && maxFuel > 0f)
                        {
                            float before = currentFuel;
                            currentFuel = Mathf.Min(currentFuel + passiveFuelReward, maxFuel);
                            float actual = currentFuel - before;

                            if (actual > 0.01f)
                                TrySpawnPopupRandomScreen(RacingPopupType.MashFuelReward, actual);
                        }

                        if (_flipMashClicks >= _flipMashClicksNeeded)
                        {
                            EndFlipMashRecoveryAndUpright();
                            break;
                        }
                    }
                }
            }

            // Crash-origin state: keep post-crash fling caps enforced while mashing/recovering.
            ApplyCrashVelocityCaps();

            // Fuel burn while in recovery
            ConsumeFuel(idleFuelUsePerSecond * Time.fixedDeltaTime);

            return;
        }

        if (_inCrash)
        {
            // IMPORTANT: Keep sampling ground even during crash (fixes ice sticking)
            SampleGroundAndUpdateMultipliers();

            // Apply boost surface during crash
            ApplyBoostSurfaceForce(true);

            // Lerp physic material / ice handling toward what the rays hit this frame (crash used to skip this → ice forever).
            UpdateIcePhysicsTransitions();

            // Check if grounded during crash
            _isGrounded = CheckIfGrounded();

            if (_isGrounded)
            {
                _groundedTime += dt;
            }
            else
            {
                _groundedTime = 0f; // Reset timer if we're airborne
            }

            if (!_wasGroundedLastFrame && _isGrounded)
            {
                _lastLandedTime = Time.time;

                if (enableLandingCarrySpeed && rb != null)
                {
                    Vector3 v = rb.velocity;
                    float horizSpeed = Vector3.ProjectOnPlane(v, Vector3.up).magnitude;

                    // Use your *current* cap (includes boost/postboost logic), but WITHOUT any landing carry.
                    float capNoCarry = GetCurrentSpeedCap_NoLandingCarry();
                    _landingExcessSpeed = Mathf.Max(_landingExcessSpeed, horizSpeed - capNoCarry);
                }
            }

            // bleed off excess allowance over time
            if (enableLandingCarrySpeed && _landingExcessSpeed > 0f)
            {
                _landingExcessSpeed = Mathf.MoveTowards(
                    _landingExcessSpeed,
                    0f,
                    landingExcessBleedPerSecond * Time.fixedDeltaTime
                );
            }

            _wasGroundedLastFrame = _isGrounded;

            _crashTimer -= dt;

            // Enforce crash-only velocity caps continuously while crash state is active.
            // This catches external pushes/collisions that occur after the initial crash impulse.
            ApplyCrashVelocityCaps();

            // Only exit crash state if timer is up AND we've been grounded long enough
            if (_crashTimer <= 0f && _groundedTime >= groundedDurationRequired)
            {
                _inCrash = false;

                if (rb != null)
                {
                    rb.drag = _baseDrag;
                    rb.angularDrag = _baseAngularDrag;
                    rb.angularVelocity = Vector3.zero;
                }

                _isBoosting = false;
                _isPostBoost = false;
                _postBoostTimer = 0f;
                _activeBoostMaxMult = 1f;

                // Clear centralized speed boosts
                ClearAllSpeedBoosts();

                // Also clear legacy close call boost flag
                _closeCallBoosting = false;
                _currentBoostMaxSpeed = 0f;
                _closeCallBoosting = false;
                _landingExcessSpeed = 0f;
                _landingBoostTimeLeft = 0f;
                _landingBoostTargetMagnitude = 0f;

                if (IsDeadForAutoUpright)
                {
                    // your end-run behavior
                    ForceStopCloseCallEffects();
                    return;
                }

                // ---- 2) Crash recovery decision point (ONLY when HP>0 AND fuel>0) ----
                if (enableFlipRecoveryMash && !IsDeadForMashRecovery)
                {
                    bool isFlipped = NeedsFlipRecovery();

                    // Trigger recovery on any crash if enabled, or only when flipped
                    if (enableCrashRecoveryAlways || isFlipped)
                    {
                        BeginCrashMashRecovery(isFlipped);
                        _groundedTime = 0f;
                        return; // IMPORTANT: do not start any auto flatten while mashing
                    }
                }

                // ---- 3) Final "make it flat" guarantee ----
                if (NeedsUprightFlatten())
                {
                    StartReorientToFlat();
                }
                else
                {
                    rb.angularVelocity = Vector3.zero;
                }

                _groundedTime = 0f;
            }
            return;
        }

        var gm = gmEarly;

        bool outOfFuel = IsOutOfFuel;
        UpdateLandingSpeedPreservation();
        SampleGroundAndUpdateMultipliers();
        RefreshSkillEffects();
        ApplySkillEffects();
        UpdateSteeringInputFixed();
        HandleSteering();
        HandleMovement();                 // coasting + existing decel logic still works

        // Update centralized speed boost system (handles ramp-down for all boosts)
        UpdateSpeedBoosts(Time.fixedDeltaTime);

        if (!outOfFuel) HandleBoost();    // block boost when fuel is 0
        ApplyBoostSurfaceForce(false);    // Apply boost pad acceleration
        UpdateIcePhysicsTransitions();
        ApplyRampAlignment(Time.fixedDeltaTime);
        SmoothDrivingGroundNormal();
        ApplyRoadGrassTransitionLift();
        ApplyGroundAwareSpeedCapClamp();

        CheckBoostFlash();

        // NEW: periodic near-miss sweep to detect close calls against ANY obstacle layers (uses crashLayers)
        // Throttle frequency to _closeCallSweepInterval to avoid expensive queries every fixed frame.
        if (enableCloseCallNearMisses && Time.time - _lastCloseCallSweep >= _closeCallSweepInterval)
        {
            _lastCloseCallSweep = Time.time;
            CheckNearbyObstaclesForCloseCall();
        }
    }

    private void LateUpdate()
    {
        // If the car spawns at runtime, Camera.main may exist later
        if (cameraShakeTarget == null && Camera.main != null)
            cameraShakeTarget = Camera.main.transform;

        if (cameraShakeTarget == null) return;

        // Remove last frame's shake so we get the TRUE camera-follow baseline
        Vector3 baselineLocal = cameraShakeTarget.localPosition - _lastAppliedShakeOffset;

        // Blend toward requested shake strength
        _shakeBlendAmp = Mathf.MoveTowards(_shakeBlendAmp, _shakeAmp, Time.deltaTime * 5f);

        Vector3 newOffset = Vector3.zero;

        if (_shakeBlendAmp > 0.0001f && _shakeFreq > 0.0001f)
        {
            float t = Time.time * _shakeFreq;
            float nx = (Mathf.PerlinNoise(t, 10.1f) - 0.5f) * 2f;
            float ny = (Mathf.PerlinNoise(20.2f, t) - 0.5f) * 2f;
            float nz = (Mathf.PerlinNoise(t, t * 0.37f) - 0.5f) * 2f;

            newOffset = new Vector3(nx, ny, nz) * _shakeBlendAmp;
        }
        else
        {
            // ease out offset (not position)
            newOffset = Vector3.Lerp(_lastAppliedShakeOffset, Vector3.zero, Time.deltaTime * screenShakeReturnSpeed);
        }

        cameraShakeTarget.localPosition = baselineLocal + newOffset;
        _lastAppliedShakeOffset = newOffset;

        // Reset per-frame requests (obstacles re-request every frame while active)
        _shakeAmp = 0f;
        _shakeFreq = 0f;
    }

    private void StartReorientToFlat()
    {
        _isReorienting = true;
        _reorientElapsed = 0f;

        rb.angularVelocity = Vector3.zero;

        _reorientStartRot = transform.rotation;

        // Keep yaw, remove pitch/roll
        Vector3 e = transform.eulerAngles;
        _reorientTargetRot = Quaternion.Euler(0f, e.y, 0f);
    }

    private void ApplyBoostSurfaceForce(bool duringCrashOrRecovery)
    {
        if (!_onBoostSurface) return;
        if (rb == null) return;

        // Check if boost works during crash/recovery
        if (duringCrashOrRecovery && !_currentBoostDuringCrash) return;

        // Calculate boost strength
        float boostStrength = _currentBoostAccel;
        if (duringCrashOrRecovery)
        {
            boostStrength *= _currentBoostCrashMultiplier;
        }

        if (boostStrength <= 0f) return;

        // Check max speed limit
        float currentSpeed = rb.velocity.magnitude;
        float maxSpeed = _currentBoostMaxSpeed > 0f ? _currentBoostMaxSpeed : effectiveMaxSpeed;

        if (currentSpeed >= maxSpeed) return;

        // Apply acceleration along ground tangent when grounded (matches HandleMovement on ramps/hills).
        Vector3 forwardDir = transform.forward;
        if (carCollider != null && CheckIfGrounded())
        {
            Vector3 castOrigin = carCollider.bounds.center + Vector3.up * 0.25f;
            if (TryGetGroundNormal(castOrigin, groundNormalCheckDistance, out RaycastHit hit))
            {
                Vector3 n = hit.normal;
                forwardDir = Vector3.ProjectOnPlane(transform.forward, n);
                if (forwardDir.sqrMagnitude < 1e-8f)
                {
                    forwardDir = transform.forward;
                    forwardDir.y = 0f;
                }
                forwardDir.Normalize();
            }
            else
            {
                forwardDir.y = 0f;
                forwardDir.Normalize();
            }
        }
        else
        {
            forwardDir.y = 0f;
            forwardDir.Normalize();
        }

        // Scale force if approaching max speed
        float speedRatio = currentSpeed / maxSpeed;
        float forceMult = Mathf.Lerp(1f, 0f, Mathf.Pow(speedRatio, 2f));

        rb.AddForce(forwardDir * boostStrength * forceMult, ForceMode.Acceleration);
    }

    private void UpdateIcePhysicsTransitions()
    {
        float dt = Time.fixedDeltaTime;

        // Lerp friction multipliers
        _currentIceDynamicFriction = Mathf.Lerp(
            _currentIceDynamicFriction,
            _iceDynamicFrictionTarget,
            iceFrictionTransitionSpeed * dt
        );

        _currentIceStaticFriction = Mathf.Lerp(
            _currentIceStaticFriction,
            _iceStaticFrictionTarget,
            iceFrictionTransitionSpeed * dt
        );

        _currentIceHandling = Mathf.Lerp(
            _currentIceHandling,
            _iceHandlingTarget,
            iceHandlingTransitionSpeed * dt
        );

        // Apply friction to physic material
        if (_carPhysicMaterial != null)
        {
            _carPhysicMaterial.dynamicFriction = _originalDynamicFriction * _currentIceDynamicFriction;
            _carPhysicMaterial.staticFriction = _originalStaticFriction * _currentIceStaticFriction;
        }

        // NEW: Ice drift-like physics (rotation vs velocity misalignment)
        bool onIceOrTransitioning = _onIceSurface || _currentIceHandling < 0.99f;

        // Skip arcade ice slide during crash / mash tumble so surface type still updates (friction above) without fighting recovery motion.
        if (onIceOrTransitioning && !_inCrash && !_flipMashActive && !_isReorienting && rb != null)
        {
            float speed = rb.velocity.magnitude;

            // Only apply ice physics when moving
            if (speed > 0.5f && Mathf.Abs(steeringInput) > 0.001f)
            {
                // Ice reduces grip = car rotates but doesn't immediately change velocity direction
                // This creates the "sliding" feel similar to drift

                Vector3 flatVel = new Vector3(rb.velocity.x, 0f, rb.velocity.z);
                Vector3 flatForward = new Vector3(transform.forward.x, 0f, transform.forward.z).normalized;

                if (flatVel.sqrMagnitude > 0.01f)
                {
                    // Blend velocity toward car forward based on ice handling (lower = more slide)
                    float alignStrength = iceVelocityAlignmentStrength * _currentIceHandling;
                    Vector3 targetDir = Vector3.Slerp(flatVel.normalized, flatForward, alignStrength * dt);

                    // Preserve speed while adjusting direction
                    float currentSpeed = flatVel.magnitude;
                    rb.velocity = new Vector3(
                        targetDir.x * currentSpeed,
                        rb.velocity.y,
                        targetDir.z * currentSpeed
                    );

                    // Add lateral slide force when steering (mimics drift side force)
                    float slideAmount = 1f - _currentIceHandling; // More slide when handling is lower
                    float steerSign = Mathf.Sign(steeringInput);
                    Vector3 sideDir = Vector3.Cross(Vector3.up, transform.forward) * steerSign;
                    rb.AddForce(sideDir * iceLateralSlideForce * slideAmount * Mathf.Abs(steeringInput), ForceMode.Acceleration);
                }
            }
        }
    }

    // NEW: Detect nearby obstacles and fire close-call ONLY when exiting the radius
    private void CheckNearbyObstaclesForCloseCall()
    {
        if (!enableCloseCallNearMisses) return;
        if (carCollider == null || rb == null) return;

        // Speed guard – no near-miss when basically stopped.
        if (rb.velocity.magnitude < closeCallMinSpeed) return;

        var gm = GameManager_Racing.Instance;
        float now = Time.time;

        // Overlap nearby colliders in crashLayers
        Collider[] hits = Physics.OverlapSphere(
            transform.position,
            Mathf.Max(0.01f, closeCallDistance),
            crashLayers,
            QueryTriggerInteraction.Collide
        );

        // Roots currently inside the radius this sweep
        var rootsInsideNow = new HashSet<int>();

        if (hits != null && hits.Length > 0)
        {
            for (int i = 0; i < hits.Length; i++)
            {
                var other = hits[i];
                if (other == null) continue;

                // Ignore our own collider(s)
                if (other == carCollider) continue;
                if (other.transform.root == transform.root) continue;

                int rootId = other.transform.root.GetInstanceID();
                rootsInsideNow.Add(rootId);

                // Skip if currently overlapping/penetrating (this is an actual collision, not a near-miss)
                bool penetrates = false;
                if (carCollider != null && other != null)
                {
                    Vector3 dir; float dist;
                    penetrates = Physics.ComputePenetration(
                        carCollider, carCollider.transform.position, carCollider.transform.rotation,
                        other, other.transform.position, other.transform.rotation,
                        out dir, out dist
                    );
                    if (penetrates) continue;
                }

                // Compute closest point & distance to car center
                Vector3 closest = other.bounds.ClosestPoint(transform.position);
                float d = Vector3.Distance(closest, transform.position);

                if (d > closeCallDistance) continue;

                // Update or create tracking state for this root
                if (!_closeCallTracking.TryGetValue(rootId, out var track))
                {
                    track = new CloseCallTrack
                    {
                        lastPos = closest,
                        lastDistance = d,
                        minDistance = d,
                        isInside = true,
                        lastSeenTime = now
                    };
                    _closeCallTracking[rootId] = track;
                }
                else
                {
                    track.lastPos = closest;
                    track.lastDistance = d;
                    track.minDistance = (track.minDistance <= 0f) ? d : Mathf.Min(track.minDistance, d);
                    track.isInside = true;
                    track.lastSeenTime = now;
                }
            }
        }

        // Now look for roots that WERE inside but are no longer in the radius (exit event)
        // We iterate over a copy because we may remove entries.
        var keys = new List<int>(_closeCallTracking.Keys);
        foreach (int rootId in keys)
        {
            var track = _closeCallTracking[rootId];
            bool stillInside = rootsInsideNow.Contains(rootId);

            if (track.isInside && !stillInside)
            {
                // We've just exited this root's close-call radius.
                // Check that we didn't recently crash into it.
                bool crashedRecently = _recentCrashRootTime.TryGetValue(rootId, out float crashTime) &&
                                       (now - crashTime) < closeCallAfterCrashBlockTime;

                if (!crashedRecently && gm != null)
                {
                    // Per-root cooldown
                    float cooldownToUse = closeCallRootCooldown > 0f ? closeCallRootCooldown : closeCallCooldown;
                    if (!_lastCloseCallTime.TryGetValue(rootId, out float lastT) || now - lastT >= cooldownToUse)
                    {
                        // Fire near-miss at the last known closest position / distance
                        gm.HandleProjectileCloseCall(track.lastPos, track.minDistance);
                        _lastCloseCallTime[rootId] = now;

                        Debug.Log($"[CarController] Close-call NEAR-MISS triggered on EXIT for root {rootId}, minDist={track.minDistance:F2}");
                    }
                }

                // Remove tracking after exit
                _closeCallTracking.Remove(rootId);
            }
            else
            {
                // If still inside, ensure flag is consistent
                track.isInside = stillInside;
            }
        }
    }


    // BOOST HANDLER – now fully decouples drift boost from normal boost
    private void HandleBoost()
    {

        if (IsCrashInvulnerable || Time.time < _boostBlockedUntil)
        {
            _boostRequested = false;
            ClearBoostOverride();
            return;
        }

        float dt = Time.fixedDeltaTime;

        // Separate cooldown timers
        if (_boostCooldownTimer > 0f)
            _boostCooldownTimer -= dt;
        if (_driftBoostCooldownTimer > 0f)
            _driftBoostCooldownTimer -= dt;

        // Active boost sustain
        if (_isBoosting)
        {
            _boostTimer -= dt;

            float sustainAccel = _activeBoostIsDrift ? driftBoostSustainAcceleration : boostSustainAcceleration;
            if (sustainAccel > 0f)
                rb.AddForce(transform.forward * sustainAccel, ForceMode.Acceleration);

            if (_boostTimer <= 0f)
            {
                _isBoosting = false;
                try { OnBoostEnded?.Invoke(); } catch { /* swallow */ }

                _isPostBoost = postBoostSlowdownDuration > 0f;
                _postBoostTimer = postBoostSlowdownDuration;

                // Clear active type
                _activeBoostIsDrift = false;
                _activeBoostMaxMult = 1f;

                // Clear centralized speed boosts
                ClearAllSpeedBoosts();

                // Also clear legacy close call boost flag
                _closeCallBoosting = false;

                // Prevent drift from holding an old boosted cap after boost expiry.
                float normalCapAfterBoost = GetCurrentSpeedCap_NoLandingCarry();
                driftClampSpeed = Mathf.Min(driftClampSpeed, normalCapAfterBoost);
                driftPeakSpeed = Mathf.Min(driftPeakSpeed, normalCapAfterBoost);
            }
        }
        else if (_isPostBoost)
        {
            _postBoostTimer -= dt;
            if (_postBoostTimer <= 0f)
                _isPostBoost = false;
        }

        // New boost request (normal or drift-held override)
        if (_boostRequested)
        {
            _boostRequested = false;

            bool isOverride = _boostOverrideActive;
            bool isDriftBoost = isOverride && _overrideIsDriftBoost;

            // Unlock: only blocks normal boost, drift boost ignores boost unlock
            if (!boostUnlocked && !isDriftBoost)
            {
                Debug.Log("[CarController] Boost request ignored: Boost locked in inspector/skill tree.");
                ClearBoostOverride();
                return;
            }

            // Cooldowns: separate for normal vs drift
            if (isDriftBoost)
            {
                if (_driftBoostCooldownTimer > 0f)
                {
                    Debug.Log($"[CarController] Drift boost ignored: cooldown {_driftBoostCooldownTimer:F2}s remaining.");
                    ClearBoostOverride();
                    return;

                }
            }
            else
            {
                if (_boostCooldownTimer > 0f)
                {
                    Debug.Log($"[CarController] Boost request ignored: cooldown {_boostCooldownTimer:F2}s remaining.");
                    ClearBoostOverride();
                    return;

                }
            }

            // Fuel cost: drift boost is FREE (no fuel usage)
            float cost = isDriftBoost ? 0f : boostFuelCost;
            if (cost > 0f && (isOutOfFuel || currentFuel < cost))
            {
                Debug.Log("[CarController] Boost request ignored: not enough fuel.");
                ClearBoostOverride();
                return;

            }

            float impulseForce = isOverride ? _boostOverrideForce : boostForce;
            rb.AddForce(transform.forward * impulseForce, ForceMode.Acceleration);

            float sustain = isDriftBoost ? driftBoostSustainAcceleration : boostSustainAcceleration;
            if (sustain > 0f)
                rb.AddForce(transform.forward * sustain, ForceMode.Acceleration);

            if (cost > 0f)
                ConsumeFuel(cost);

            // Activate boost
            _isBoosting = true;
            _activeBoostIsDrift = isDriftBoost;
            _activeBoostMaxMult = isOverride
                ? Mathf.Max(1f, _boostOverrideMaxMult)
                : Mathf.Max(1f, boostMaxSpeedMultiplier);

            // Add to centralized speed boost system for natural ramp-down
            float boostDur = isOverride ? _boostOverrideDuration : boostDuration;
            string boostId = isDriftBoost ? BOOST_ID_DRIFT : BOOST_ID_REGULAR;
            AddSpeedBoost(
                boostId,
                _activeBoostMaxMult,
                boostDur,
                regularBoostRampDownFraction,
                isMultiplier: true
            );

            try { OnBoostStarted?.Invoke(); } catch { /* swallow */ }

            // Per-type cooldown
            if (isDriftBoost)
                _driftBoostCooldownTimer = Mathf.Max(0.01f, driftBoostCooldown);
            else
                _boostCooldownTimer = Mathf.Max(0.01f, boostCooldown);

            _boostTimer = Mathf.Max(0f, isOverride ? _boostOverrideDuration : boostDuration);
            _isPostBoost = false;

            Debug.Log($"[CarController] Boost STARTED: drift={isDriftBoost}, impulse={impulseForce:F2}, sustain={sustain:F2}, duration={_boostTimer:F2}, maxMult={_activeBoostMaxMult:F2}");

            ClearBoostOverride();
        }

        if (rb != null)
        {
            bool landingGrace = (Time.time - _lastLandedTime) <= landingNoClampGraceSeconds;

            if (!(skipSpeedClampWhileAirborne && !_isGrounded) && !landingGrace)
            {
                float cap = GetCurrentSpeedCap();

                Vector3 v = rb.velocity;
                Vector3 horiz = Vector3.ProjectOnPlane(v, Vector3.up);
                float horizSpeed = horiz.magnitude;

                if (horizSpeed > cap && horizSpeed > 0.0001f)
                {
                    Vector3 horizClamped = horiz * (cap / horizSpeed);
                    rb.velocity = new Vector3(horizClamped.x, v.y, horizClamped.z);
                }
            }
        }
    }

    private float GetCurrentSpeedCap()
    {
        float cap = GetCurrentSpeedCap_NoLandingCarry();

        if (enableLandingCarrySpeed && _landingExcessSpeed > 0f)
            cap += _landingExcessSpeed;

        cap += GetLandingBoostCapAdd();

        return cap;
    }

    /// <summary>Extra speed cap from landing boost (decays over duration with falloff).</summary>
    private float GetLandingBoostCapAdd()
    {
        if (!enableLandingBoost || _landingBoostTimeLeft <= 0f || _landingBoostDuration <= 0f)
            return 0f;

        float t = Mathf.Clamp01(_landingBoostTimeLeft / _landingBoostDuration);
        float factor = Mathf.Pow(t, landingBoostFalloff);
        return _landingBoostTargetMagnitude * factor;
    }

    private float GetCurrentSpeedCap_NoLandingCarry()
    {
        float normalCap = effectiveMaxSpeed;

        // ═══════════════════════════════════════════════════════════════════════
        // CENTRALIZED SPEED BOOST SYSTEM - SPEED CAP CALCULATION
        // All speed boosts with natural ramp-down are calculated here
        // ═══════════════════════════════════════════════════════════════════════

        // Get total boost from centralized system (handles ramp-down automatically)
        float centralizedBoostIncrease = GetTotalSpeedBoostIncrease();

        // Legacy boost handling (for backwards compatibility during transition)
        // These will be migrated to the centralized system over time
        float legacyMult = 1f;

        // Regular/Drift boost - now handled by centralized system but keep legacy path for post-boost
        if (_isBoosting && !HasSpeedBoost(BOOST_ID_REGULAR) && !HasSpeedBoost(BOOST_ID_DRIFT))
        {
            // Legacy path: boost is active but not yet migrated
            legacyMult = Mathf.Max(legacyMult, _activeBoostMaxMult);
        }

        float boostedCap = normalCap * legacyMult + centralizedBoostIncrease;

        // Handle post-boost ramp-down for legacy boosts (non-centralized)
        if (_isPostBoost && postBoostSlowdownDuration > 0f && !HasAnySpeedBoost)
        {
            float t = 1f - Mathf.Clamp01(_postBoostTimer / postBoostSlowdownDuration);
            return Mathf.Lerp(boostedCap, normalCap, t);
        }

        bool hasAnyBoost = _isBoosting || HasAnySpeedBoost;
        return hasAnyBoost ? boostedCap : normalCap;
    }

    /// <summary>
    /// Caps speed in the ground tangent plane when grounded so ramp / slope driving cannot exceed <see cref="GetCurrentSpeedCap"/>
    /// while still passing a horizontal-only check. Airborne keeps the legacy horizontal clamp (preserves vertical motion).
    /// Call after movement, boost, boost pads, ice, and ramp alignment.
    /// </summary>
    private void ApplyGroundAwareSpeedCapClamp()
    {
        if (rb == null) return;

        bool landingGrace = (Time.time - _lastLandedTime) <= landingNoClampGraceSeconds;
        if (landingGrace) return;
        if (skipSpeedClampWhileAirborne && !_isGrounded) return;

        float cap = GetCurrentSpeedCap();
        if (cap <= 0.001f) return;

        const float capSlack = 0.05f;
        bool groundedNow = CheckIfGrounded();
        Vector3 v = rb.velocity;

        if (groundedNow)
        {
            Vector3 n = _lastStableGroundNormal.sqrMagnitude > 1e-6f ? _lastStableGroundNormal.normalized : Vector3.up;
            float upDot = Mathf.Abs(Vector3.Dot(n, Vector3.up));

            if (upDot < 0.1f)
            {
                Vector3 horiz = Vector3.ProjectOnPlane(v, Vector3.up);
                float hs = horiz.magnitude;
                if (hs > cap + capSlack && hs > 1e-6f)
                {
                    horiz *= cap / hs;
                    rb.velocity = new Vector3(horiz.x, v.y, horiz.z);
                }
                return;
            }

            float vn = Vector3.Dot(v, n);
            Vector3 vTan = v - vn * n;
            float tanMag = vTan.magnitude;
            if (tanMag > cap + capSlack && tanMag > 1e-6f)
            {
                vTan *= cap / tanMag;
                rb.velocity = vTan + vn * n;
            }
        }
        else
        {
            Vector3 horiz = Vector3.ProjectOnPlane(v, Vector3.up);
            float horizSpeed = horiz.magnitude;
            if (horizSpeed > cap + capSlack && horizSpeed > 1e-6f)
            {
                horiz *= cap / horizSpeed;
                rb.velocity = new Vector3(horiz.x, v.y, horiz.z);
            }
        }
    }

    private void ClearBoostOverride()
    {
        _boostOverrideActive = false;
        _overrideIsDriftBoost = false;
        _boostOverrideForce = 0f;
        _boostOverrideDuration = 0f;
        _boostOverrideMaxMult = 0f;
    }

    /// <param name="resetDragToDefaults">If false, caller must assign <see cref="Rigidbody.drag"/> / angularDrag (HP death uses grass-aware drag).</param>
    private void ReleaseHandsOffDrivingPhysics(bool resetDragToDefaults = true)
    {
        if (rb == null) return;
        rb.freezeRotation = false;
        if (resetDragToDefaults)
        {
            rb.drag = _baseDrag;
            rb.angularDrag = _baseAngularDrag;
        }
    }

    /// <summary>
    /// After HP death: one world-down ray from the car samples <see cref="GroundSurface"/> so grass uses high drag,
    /// lower effective speed cap, and extra planar damping — not the default road tumble drag.
    /// </summary>
    private void ApplyHpDeathTerrainFromGlobalRay()
    {
        if (rb == null || carCollider == null) return;

        _hpDeathGrassEffectActive = false;

        Vector3 origin = carCollider.bounds.center + Vector3.up * deathHpTerrainRayStartHeight;
        float rayLen = Mathf.Max(2f, deathHpTerrainRayLength);

#if UNITY_EDITOR
        if (debugSurfaceRays)
            Debug.DrawRay(origin, Vector3.down * rayLen, new Color(0.2f, 0.85f, 0.35f));
#endif

        void ClearBoostIceDefaults()
        {
            _onBoostSurface = false;
            _currentBoostAccel = 0f;
            _currentBoostMaxSpeed = 0f;
            _currentBoostDuringCrash = false;
            _currentBoostCrashMultiplier = 0.5f;
            _onIceSurface = false;
            _iceDynamicFrictionTarget = 1f;
            _iceStaticFrictionTarget = 1f;
            _iceHandlingTarget = 1f;
        }

        if (Physics.Raycast(origin, Vector3.down, out RaycastHit hit, rayLen, groundLayers, QueryTriggerInteraction.Collide))
        {
            GroundSurface surface = hit.collider.GetComponent<GroundSurface>()
                                    ?? hit.collider.GetComponentInParent<GroundSurface>();
            if (surface != null)
            {
                float maxMul = Mathf.Max(0.01f, surface.maxSpeedMultiplier);
                float accelMul = Mathf.Max(0.01f, surface.accelerationMultiplier);
                float turnMul = Mathf.Max(0.01f, surface.turnSpeedMultiplier);
                float dragMul = Mathf.Max(0.01f, surface.dragMultiplier);

                ClearBoostIceDefaults();

                if (surface.surfaceType == SurfaceType.Grass || surface.surfaceType == SurfaceType.Dirt)
                    dragMul *= Mathf.Max(1f, deathHpGrassDragBoost);

                ApplySurfaceMultipliers(maxMul, accelMul, turnMul, dragMul);

                grassFraction = surface.surfaceType == SurfaceType.Grass ? 1f : 0f;
                offDefaultFraction = surface.surfaceType != SurfaceType.Default ? 1f : 0f;
                currentFuelUseMultiplier = surface.surfaceType == SurfaceType.Grass
                    ? Mathf.Max(1f, grassFuelUseMultiplier)
                    : 1f;
                currentSteeringDamp = baseSteeringDamp;

                if (surface.surfaceType == SurfaceType.Ice)
                {
                    _onIceSurface = true;
                    _iceDynamicFrictionTarget = surface.iceDynamicFrictionMultiplier;
                    _iceStaticFrictionTarget = surface.iceStaticFrictionMultiplier;
                    _iceHandlingTarget = surface.iceHandlingMultiplier;
                }

                if (surface.surfaceType == SurfaceType.Boost || surface.surfaceType == SurfaceType.Ramp)
                {
                    _onBoostSurface = true;
                    _currentBoostAccel = surface.boostAcceleration;
                    _currentBoostMaxSpeed = surface.boostMaxSpeed;
                    _currentBoostDuringCrash = surface.boostDuringCrash;
                    _currentBoostCrashMultiplier = surface.boostCrashMultiplier;
                }

                _hpDeathGrassEffectActive = surface.surfaceType == SurfaceType.Grass || surface.surfaceType == SurfaceType.Dirt;

                RefreshSkillEffects();
                ApplySkillEffects();

                rb.drag = effectiveDrag;
                rb.angularDrag = _baseAngularDrag *
                    (_hpDeathGrassEffectActive ? Mathf.Max(1f, deathHpGrassAngularDragBoost) : 1f);

                if (_hpDeathGrassEffectActive && deathHpGrassPlanarDampingPerSecond > 0f)
                    ApplyHpDeathGrassPlanarDamping(Time.fixedDeltaTime);

                return;
            }
        }

        // No GroundSurface on hit: keep multi-sample behavior (mixed grass / ice / boost).
        ClearBoostIceDefaults();
        SampleGroundAndUpdateMultipliers();
        RefreshSkillEffects();
        ApplySkillEffects();

        rb.drag = effectiveDrag;
        _hpDeathGrassEffectActive = grassFraction >= 0.5f;
        rb.angularDrag = _baseAngularDrag *
            (_hpDeathGrassEffectActive ? Mathf.Max(1f, deathHpGrassAngularDragBoost) : 1f);

        if (_hpDeathGrassEffectActive && deathHpGrassPlanarDampingPerSecond > 0f)
            ApplyHpDeathGrassPlanarDamping(Time.fixedDeltaTime);
    }

    private void ApplyHpDeathGrassPlanarDamping(float dt)
    {
        if (rb == null || dt <= 0f) return;
        Vector3 v = rb.velocity;
        Vector3 h = new Vector3(v.x, 0f, v.z);
        float mag = h.magnitude;
        if (mag < 1e-6f) return;
        float dps = deathHpGrassPlanarDampingPerSecond;
        if (deathHpGrassPlanarDampingPerSpeed > 0f)
            dps *= 1f + mag * deathHpGrassPlanarDampingPerSpeed;
        float damp = Mathf.Exp(-dps * dt);
        h *= damp;
        rb.velocity = new Vector3(h.x, v.y, h.z);
    }

    /// <summary>
    /// After ice/friction lerp: extra Rigidbody drag + tire friction on grass/dirt so slides don’t ignore surface like road ice.
    /// Optional hard cap on planar tumble speed (alive grass feels slow mostly from accel; dead slides need this).
    /// </summary>
    private void ApplyHpDeathSoftGroundGripAndCap()
    {
        if (rb == null || !isOutOfHP || !_hpDeathGrassEffectActive) return;

        rb.drag = Mathf.Min(80f, effectiveDrag * Mathf.Max(1f, deathHpGrassRigidbodyDragScale));

        if (_carPhysicMaterial != null)
        {
            float f = Mathf.Max(1f, deathHpGrassFrictionScale);
            _carPhysicMaterial.dynamicFriction = Mathf.Clamp01(_carPhysicMaterial.dynamicFriction * f);
            _carPhysicMaterial.staticFriction = Mathf.Clamp01(_carPhysicMaterial.staticFriction * f);
        }

        if (deathHpGrassTumbleMaxPlanarSpeed <= 0f) return;

        Vector3 v = rb.velocity;
        Vector3 h = new Vector3(v.x, 0f, v.z);
        float m = h.magnitude;
        if (m > deathHpGrassTumbleMaxPlanarSpeed)
        {
            h *= deathHpGrassTumbleMaxPlanarSpeed / m;
            rb.velocity = new Vector3(h.x, v.y, h.z);
        }
    }

    /// <summary>
    /// Snap ice + grip material to non-ice. Used when we stop sampling (hands-off driving) so tumble physics matches the ground visually.
    /// </summary>
    private void ResetIcePhysicsImmediate()
    {
        _onIceSurface = false;
        _iceDynamicFrictionTarget = 1f;
        _iceStaticFrictionTarget = 1f;
        _iceHandlingTarget = 1f;
        _currentIceDynamicFriction = 1f;
        _currentIceStaticFriction = 1f;
        _currentIceHandling = 1f;
        _iceSteerCharge01 = 0f;
        _iceSteerSign = 0;
        if (_carPhysicMaterial != null)
        {
            _carPhysicMaterial.dynamicFriction = _originalDynamicFriction;
            _carPhysicMaterial.staticFriction = _originalStaticFriction;
        }
    }

    private void WireManagerEvents()
    {
        var mgr = RacingSkillTreeManager.Instance;
        if (mgr != null)
        {
            mgr.OnLevelChanged += HandleSkillLevelChanged;
            mgr.OnSkillsReset += HandleSkillsReset;
        }
    }

    private void UnwireManagerEvents()
    {
        var mgr = RacingSkillTreeManager.Instance;
        if (mgr != null)
        {
            mgr.OnLevelChanged -= HandleSkillLevelChanged;
            mgr.OnSkillsReset -= HandleSkillsReset;
        }
    }

    private void HandleSkillLevelChanged(SkillType _, int __)
    {
        RefreshSkillEffects();
        ApplySkillEffects();
        UpdateDriftUnlock();
        UpdateBoostUnlock();
    }

    private void HandleSkillsReset()
    {
        accelValue = maxSpeedValue = steerValue = fuelValue = 0f;
        accelMode = maxSpeedMode = steerMode = fuelMode = SkillApplicationMode.Additive;
        RefreshSkillEffects();
        ApplySkillEffects();
        UpdateDriftUnlock();
        UpdateBoostUnlock();
    }

    private void HandleInput()
    {
        var gm = GameManager_Racing.Instance;

        if (_externalInputLocked)
        {
            steeringInput = 0f;
            _rawSteer = 0f;
            _boostRequested = false;
            _boostOverrideActive = false;
            driftButtonHeld = false;
            isDrifting = false;
            driftCharge = 0f;

            _suppressThrottleBrakeThisFrame = true;
            _suppressSteeringThisFrame = true;
            _inputsSuppressedThisFrame = true;
            return;
        }

        if (gm != null && gm.RunEnded)
        {
            steeringInput = 0f;
            _rawSteer = 0f;
            _boostRequested = false;
            _boostOverrideActive = false;
            driftButtonHeld = false;
            isDrifting = false;
            driftCharge = 0f;
            _suppressThrottleBrakeThisFrame = true;
            _suppressSteeringThisFrame = true;
            _inputsSuppressedThisFrame = true;
            return;
        }

        if (isOutOfHP)
        {
            steeringInput = 0f;
            _rawSteer = 0f;
            driftCharge = 0f;
            isDrifting = false;
            driftButtonHeld = false;
            _boostRequested = false;
            _boostOverrideActive = false;
            _inputsSuppressedThisFrame = true;
            _suppressThrottleBrakeThisFrame = true;
            _suppressSteeringThisFrame = true;
            return;
        }

        // Out of fuel: free physics — keep steering read/smoothing; no throttle, drift, or boost.
        if (isOutOfFuel)
        {
            _rawSteer = GetSteerRaw();
            driftCharge = 0f;
            isDrifting = false;
            driftButtonHeld = false;
            _boostRequested = false;
            _boostOverrideActive = false;
            _suppressThrottleBrakeThisFrame = true;
            _suppressSteeringThisFrame = false;
            _inputsSuppressedThisFrame = true;
            return;
        }

        if (_malfunctionTimer > 0f)
            _malfunctionTimer -= Time.deltaTime;
        if (_malfunctionCooldownRemain > 0f)
            _malfunctionCooldownRemain -= Time.deltaTime;

        _rawSteer = GetSteerRaw();
        float rawHorizontal = _rawSteer; // keep the rest of your logic working
        float speed = rb != null ? rb.velocity.magnitude : 0f;
        bool prevDriftKeyHeld = driftButtonHeld;
        driftButtonHeld = GetDriftHeld();
        bool brakeHeld = GetBrakeKeyOrTrigger();

        // NEW: starting a fresh drift-hold clears the "crash killed charge" gate
        if (driftButtonHeld && !prevDriftKeyHeld)
        {
            _crashKilledDriftHeldBoost = false;
        }

        bool wasDrifting = isDrifting;
        int prevHoldDirectionSign = _driftHoldDirectionSign;


        if (!driftUnlocked)
        {
            driftCharge = 0f;
            isDrifting = false;
            _driftCurrentSteerSign = 0;
        }
        else
        {
            bool canDriftThisFrame = driftButtonHeld && speed >= driftMinSpeed;

            if (driftButtonHeld && !prevDriftKeyHeld)
            {
                _crashKilledDriftHeldBoost = false;
            }

            int currentSign =
                rawHorizontal > steerFlipThreshold ? 1 :
                rawHorizontal < -steerFlipThreshold ? -1 : 0;

            // NEW: track how long we've been neutral on the stick while holding drift
            if (driftButtonHeld)
            {
                if (currentSign == 0)
                {
                    _driftNeutralTimer += Time.deltaTime;

                    // If we've been neutral long enough, treat this as a full drift reset
                    if (driftCharge > 0f && _driftNeutralTimer >= driftNeutralFullResetDelay)
                    {
                        driftCharge = 0f;
                        _driftCurrentSteerSign = 0;
                        driftEntrySpeed = 0f;
                        driftClampSpeed = 0f;
                        driftPeakSpeed = 0f;
                        _driftFlipBlockUntil = 0f;

                        // NEW: also reset drift-held boost accumulation
                        if (enableDriftHeldBoost)
                            ResetDriftHeldTimer();
                    }
                }
                else
                {
                    // As soon as we push a direction again, clear the neutral timer
                    _driftNeutralTimer = 0f;
                }
            }
            else
            {
                // Not even holding drift: no neutral accumulation
                _driftNeutralTimer = 0f;
            }

            if (driftButtonHeld)
            {
                // Only care about non-zero steer
                if (currentSign != 0)
                {
                    if (resetDriftChargeOnSteerFlip &&
                        _driftCurrentSteerSign != 0 &&
                        currentSign != _driftCurrentSteerSign &&
                        driftCharge >= minChargeForFlipReset &&
                        Time.time >= _driftFlipBlockUntil)
                    {
                        // Hard direction flip while drifting:
                        // reduce/reset charge and briefly block rebuild
                        driftCharge *= steerFlipRetainedCharge;
                        _driftFlipBlockUntil = Time.time + steerFlipRebuildDelay;

                        // Also restart drift-held timer so the new direction is "fresh"
                        if (enableDriftHeldBoost)
                            ResetDriftHeldTimer();
                    }

                    // Update active steering sign (used by drift-held boost)
                    _driftCurrentSteerSign = currentSign;
                }
                // If currentSign == 0 we *keep* the last sign for a short time;
                // neutral-full reset above will clear it if we stay neutral.
            }
            else
            {
                // Not holding drift at all: clear steer sign
                _driftCurrentSteerSign = 0;
            }

            if (Time.time < _driftFlipBlockUntil)
                canDriftThisFrame = false;

            if (requireDirectionalInputForDriftCharge)
            {
                bool hasDirectionalSteer = currentSign != 0;
                if (!hasDirectionalSteer)
                {
                    if (driftCharge > 0f && driftButtonHeld)
                    {
                        float drain = (driftNeutralDrainRate > 0f ? driftNeutralDrainRate : driftReleaseRate);
                        driftCharge = Mathf.MoveTowards(driftCharge, 0f, drain * Time.deltaTime);
                    }
                    canDriftThisFrame = false;
                }
            }

            float targetDrift = (canDriftThisFrame ? 1f : 0f);

            // Post–steer-flip rebuild: don't dump remaining drift charge at driftReleaseRate while
            // the player still holds drift (avoids isDrifting blipping off and speed clamps dipping).
            if (Time.time < _driftFlipBlockUntil && driftButtonHeld && speed >= driftMinSpeed)
                targetDrift = Mathf.Max(targetDrift, driftCharge);

            float rate = targetDrift > driftCharge ? driftBuildRate : driftReleaseRate;

            if (!(requireDirectionalInputForDriftCharge && targetDrift == 0f &&
                  (rawHorizontal > -steerFlipThreshold && rawHorizontal < steerFlipThreshold) && driftCharge > 0f))
            {
                driftCharge = Mathf.MoveTowards(driftCharge, targetDrift, rate * Time.deltaTime);
            }

            isDrifting = driftCharge > 0.01f;

            // Drift-held boost accumulation
            if (enableDriftHeldBoost)
            {
                // PATCH: if player is braking, do NOT accumulate drift-held boost.
                // Also wipe any stored drift-held charge so release cannot fling you.
                if (brakeHeld || _inCrash || isOutOfHP || isOutOfFuel)
                {
                    ResetDriftHeldTimer();
                }
                else
                {
                    // Build time as long as drift key + a steering direction are held
                    if (driftButtonHeld && _driftCurrentSteerSign != 0)
                    {
                        if (_driftHoldDirectionSign == 0 || _driftHoldDirectionSign == _driftCurrentSteerSign)
                        {
                            _driftHoldDirectionSign = _driftCurrentSteerSign;
                        }
                        else
                        {
                            // Direction flip: start new accumulation
                            ResetDriftHeldTimer();
                            _driftHoldDirectionSign = _driftCurrentSteerSign;
                        }

                        _driftHoldTimeSeconds += Time.deltaTime;
                    }

                    // Trigger boost ONLY on drift key release
                    if (!driftButtonHeld && prevDriftKeyHeld)
                    {
                        TryTriggerDriftHeldBoost();
                        ResetDriftHeldTimer();
                    }

                    // Hard reset if not holding drift at all
                    if (!driftButtonHeld)
                    {
                        _driftHoldDirectionSign = 0;
                    }
                }
            }


            if (isDrifting && !wasDrifting && rb != null)
            {
                driftEntrySpeed = speed;
                driftClampSpeed = driftEntrySpeed;
                driftPeakSpeed = driftEntrySpeed;

                // Reset held boost timer on brand new drift start
                if (enableDriftHeldBoost)
                {
                    ResetDriftHeldTimer();
                    _driftHoldDirectionSign = _driftCurrentSteerSign;
                }
            }
            else if (!isDrifting && wasDrifting)
            {
                // Charge can hit zero briefly (neutral steer, flip rebuild, etc.) while drift is
                // still held — keep speed anchors so drift/glide physics don't pull speed down.
                if (driftButtonHeld && rb != null)
                {
                    float v = rb.velocity.magnitude;
                    driftEntrySpeed = v;
                    driftClampSpeed = Mathf.Max(driftClampSpeed, v);
                    driftPeakSpeed = Mathf.Max(driftPeakSpeed, v);
                }
                else
                {
                    driftEntrySpeed = 0f;
                    driftClampSpeed = 0f;
                    driftPeakSpeed = 0f;
                }
            }
        }

        _driftWasActiveLastFrame = isDrifting;

        if (allowDriftGlideWithoutSteer && driftUnlocked)
        {
            bool canGlide = driftButtonHeld && !isDrifting && speed >= driftMinSpeed;
            if (canGlide)
            {
                if (!_driftGlideActive)
                {
                    driftEntrySpeed = speed;
                    driftClampSpeed = driftEntrySpeed;
                    driftPeakSpeed = Mathf.Max(driftPeakSpeed, speed);
                }
                _driftGlideActive = true;
            }
            else if (_driftGlideActive && (!driftButtonHeld || speed < 0.5f))
            {
                _driftGlideActive = false;
                driftEntrySpeed = 0f;
                driftClampSpeed = 0f;
            }
        }
        else
        {
            _driftGlideActive = false;
        }

        _lastRawSteerValue = rawHorizontal;

        bool suppressInputs = false;
        _currentMalfunctionMultiplier = 1f; // Reset each frame

        if (enableDamageMalfunction && _statsInitialized)
        {
            float hpFrac = HPPercent;
            float dmgT = Mathf.Clamp01((degradeStartHPFraction - hpFrac) / Mathf.Max(0.0001f, degradeStartHPFraction));
            float biasExp = Mathf.Clamp(_lowHpMalfunctionChanceBiasExponent, 0.12f, 2f);
            float biasedDmgT = Mathf.Pow(dmgT, biasExp);
            float chancePerSec = maxMalfunctionChancePerSecond * biasedDmgT;

            if (_malfunctionTimer <= 0f)
            {
                if (_malfunctionCooldownRemain <= 0f && chancePerSec > 0f)
                {
                    float p = chancePerSec * Time.deltaTime;
                    if (UnityEngine.Random.value < p)
                    {
                        _malfunctionTimer = UnityEngine.Random.Range(malfunctionBurstDuration.x, malfunctionBurstDuration.y);
                        _malfunctionCooldownRemain = UnityEngine.Random.Range(malfunctionCooldown.x, malfunctionCooldown.y);
                    }
                }
            }

            bool isMalfunctioning = _malfunctionTimer > 0f;

            if (useSmoothMalfunction)
            {
                // IMPROVED: Instead of blocking throttle entirely, reduce effectiveness
                // This prevents the car from becoming completely immobile
                if (isMalfunctioning)
                {
                    _currentMalfunctionMultiplier = malfunctionThrottleMultiplier;
                }
                // Don't set suppressInputs - we handle throttle reduction in ApplyAcceleration instead
            }
            else
            {
                // Legacy behavior: complete suppression during malfunction
                suppressInputs = isMalfunctioning;
            }
        }

        // ADJUSTMENT: never fully suppress steering; only throttle/brake during malfunction (legacy mode only)
        _suppressThrottleBrakeThisFrame = suppressInputs;
        _suppressSteeringThisFrame = false; // keep steering responsive even during malfunction

        _inputsSuppressedThisFrame = _suppressThrottleBrakeThisFrame;
    }

    public void SetExternalInputLock(bool locked)
    {
        _externalInputLocked = locked;
    }

    /// <summary>True while spawn / loading / cutscenes hold car input (drift reads false).</summary>
    public bool IsExternalInputLocked => _externalInputLocked;

    private void TryTriggerDriftHeldBoost()
    {
        if (Time.time < _boostBlockedUntil) return;
        if (!enableDriftHeldBoost) return;
        if (_inCrash) return; // prevent accidental boost trigger after a crash interruption

        bool brakeHeld = GetBrakeKeyOrTrigger();
        if (brakeHeld) return;
        if (isOutOfHP || isOutOfFuel) return;
        if (_inputsSuppressedThisFrame || _suppressThrottleBrakeThisFrame) return;

        float held = _driftHoldTimeSeconds;
        ResetDriftHeldTimer();

        if (held < driftBoostMinHoldSeconds)
        {
            Debug.Log($"[CarController] Drift-held boost: held {held:F2}s < min {driftBoostMinHoldSeconds:F2}s -> NO BOOST");
            return; // below minimum threshold
        }

        float clamped = Mathf.Min(held, driftBoostMaxHoldSeconds);
        float norm = Mathf.InverseLerp(driftBoostMinHoldSeconds, driftBoostMaxHoldSeconds, clamped);

        float force = Mathf.Lerp(driftBoostForceRange.x, driftBoostForceRange.y, norm);
        float duration = Mathf.Lerp(driftBoostDurationRange.x, driftBoostDurationRange.y, norm);
        float maxMult = Mathf.Lerp(driftBoostMaxSpeedMultRange.x, driftBoostMaxSpeedMultRange.y, norm);

        // Apply skill scaling and gate by skill unlock (if a manager is present)
        var mgr = RacingSkillTreeManager.Instance;
        bool unlocked = mgr == null ? true : mgr.IsDriftHeldBoostUnlocked();

        Debug.Log($"[CarController] Drift-held boost attempt: held={held:F2}s norm={norm:F2} force={force:F2} dur={duration:F2} maxMult={maxMult:F2} unlocked={unlocked}");

        if (!unlocked)
        {
            // Skill exists and is locked -> do not trigger drift-held boost
            Debug.Log("[CarController] Drift-held boost aborted: skill locked.");
            return;
        }

        if (mgr != null)
        {
            force = mgr.GetDriftHeldBoostForceScaled(force);
            duration = mgr.GetDriftHeldBoostDurationScaled(duration);
            maxMult = mgr.GetDriftHeldBoostMaxSpeedMultScaled(maxMult);
        }

        _boostOverrideActive = true;
        _overrideIsDriftBoost = true;
        _boostOverrideForce = force;
        _boostOverrideDuration = duration;
        _boostOverrideMaxMult = maxMult;

        // Drift-held boost is FREE: no fuel deduction here.
        _boostRequested = true;
        Debug.Log($"[CarController] Drift-held boost REQUESTED -> force={force:F2}, duration={duration:F2}, maxMult={maxMult:F2}");
    }

    private void ResetDriftHeldTimer()
    {
        _driftHoldTimeSeconds = 0f;
        _driftHoldDirectionSign = 0;
    }

    /// <summary>
    /// One-shot crash HP/fuel: damage = severity × max pool × health-config scale (fuel also × <see cref="_crashFuelDamageScale"/>).
    /// Capped by current HP/fuel. Severity is 0–1 from <see cref="ResolveCrashSeverity"/>.
    /// </summary>
    private bool ApplyCrashHpAndFuelFromSeverity(float sev01)
    {
        sev01 = Mathf.Clamp01(sev01);
        float hpBefore = currentHP;
        float fuelBefore = currentFuel;

        if (maxHP > 0f && crashDamageSeverityScale > 0f)
        {
            float hpDamage = maxHP * sev01 * crashDamageSeverityScale;
            float hpLoss = Mathf.Min(hpDamage, currentHP);
            currentHP = Mathf.Max(0f, currentHP - hpLoss);

            if (hpLoss >= minHPDamageForPopup)
                TrySpawnPopup(RacingPopupType.HPDamage, hpLoss);

            Debug.Log($"[CarController] Crash HP: -{hpLoss:F1} (sev={sev01:F2} of max {maxHP:F1}). Now {currentHP:F1}/{maxHP:F1}");
        }

        if (maxFuel > 0f && crashDamageSeverityScale > 0f)
        {
            float fuelDamage = maxFuel * sev01 * crashDamageSeverityScale * _crashFuelDamageScale;
            ConsumeFuel(fuelDamage);
            float actualFuelLoss = fuelBefore - currentFuel;

            if (actualFuelLoss >= minFuelLossForPopup)
                TrySpawnPopup(RacingPopupType.FuelLoss, actualFuelLoss);

            Debug.Log($"[CarController] Crash fuel: -{actualFuelLoss:F1} (sev={sev01:F2} of max {maxFuel:F1}). Now {currentFuel:F1}/{maxFuel:F1}");
        }

        return (hpBefore > 0f && currentHP <= 0f) || (fuelBefore > 0f && currentFuel <= 0f);
    }

    /// <summary>
    /// Fires <see cref="OnCrash"/> for listeners (e.g. Vintage TV) without entering crash physics.
    /// Used when the car is already out of fuel/HP but should still react visually to impacts.
    /// </summary>
    private void NotifyCrashFeedbackOnly(float severity01)
    {
        float sev = Mathf.Clamp01(severity01);
        try { OnCrash?.Invoke(sev); } catch { /* ignore listener errors */ }
    }

    private void TriggerCrash(
        Vector3 hitDirection,
        float crashDuration,
        float impulseMagnitude,
        float torqueMagnitude,
        float severity,
        Vector3 contactPointWS,
        bool applyDamage)
    {
        if (rb == null)
            return;

        if (isOutOfFuel || isOutOfHP)
        {
            NotifyCrashFeedbackOnly(severity);
            return;
        }

        if (_inCrash && !_flipMashActive)
            return;

        if (_isBoosting)
        {
            _isBoosting = false;
            _boostTimer = 0f;
        }

        // Clear boost override
        ClearBoostOverride();

        // Clear close-call boost
        if (_closeCallBoosting)
        {
            _closeCallBoosting = false;
        }

        // Reset post-boost state
        _isPostBoost = false;
        _postBoostTimer = 0f;

        // Clear any active boost max speed multiplier
        _activeBoostMaxMult = 1f;

        // Clear centralized speed boosts
        ClearAllSpeedBoosts();

        // Also clear legacy close call boost flag
        _closeCallBoosting = false;
        _currentBoostMaxSpeed = 0f;

        CancelAllBoostState(crashDuration + reorientDuration + 0.1f);
        _crashKilledDriftHeldBoost = true;
        // NEW: also prevent drift-held boost from “arming” during crash sequences
        ResetDriftHeldTimer();
        _boostOverrideActive = false;
        _overrideIsDriftBoost = false;

        // Clamp severity once and reuse
        float sev01 = Mathf.Clamp01(severity);

        // Flatten & normalize incoming hit direction
        hitDirection.y = 0f;
        if (hitDirection.sqrMagnitude < 0.0001f)
            hitDirection = -transform.forward;
        hitDirection.Normalize();

        _inCrash = true;
        _crashTimer = crashDuration;

        // Store crash info for recovery calculation
        _lastCrashSeverity = Mathf.Clamp01(severity);
        _crashCount++; // counts all crashes for scaling

        // Single hook for all crash entry points (collisions, external damage, bounce-back, etc.)
        try { OnCrash?.Invoke(sev01); } catch { /* ignore listener errors */ }

        // Snapshot situational flags BEFORE we force grounded false.
        _wasAirborneDuringCrash = !_isGrounded;
        bool flippedAtImpact = NeedsFlipRecovery();

        float severityContribution = _lastCrashSeverity;

        if (_wasAirborneDuringCrash) severityContribution *= airborneClickMultiplier;
        if (flippedAtImpact) severityContribution *= flippedClickMultiplier;

        _crashSeveritySum += severityContribution;

        _groundedTime = 0f;
        _isGrounded = false;

        rb.freezeRotation = false;
        rb.drag = _baseDrag * crashDragMultiplier;
        rb.angularDrag = crashAngularDrag;

        // Current velocity
        Vector3 v = rb.velocity;
        Vector3 flatVel = new Vector3(v.x, 0f, v.z);

        // We'll decide the impulse direction here
        Vector3 impulseDir = hitDirection;

        if (flatVel.sqrMagnitude > 0.01f)
        {
            // Reflect current velocity around a "surface normal" (hitDirection)
            Vector3 normal = hitDirection;
            Vector3 reflected = Vector3.Reflect(flatVel, normal);

            float deflectAmount = Mathf.Lerp(0.3f, 0.8f, sev01);
            Vector3 newFlatVel = Vector3.Lerp(flatVel, reflected, deflectAmount);

            float slowMul = Mathf.Lerp(0.9f, 0.6f, sev01);
            newFlatVel *= slowMul;

            rb.velocity = new Vector3(newFlatVel.x, v.y, newFlatVel.z);

            // MAIN IDEA: base impulse opposite previous motion
            impulseDir = -flatVel.normalized;
        }
        else
        {
            // If we were basically stopped, just kick along the hit direction
            rb.velocity = hitDirection * impulseMagnitude * 0.5f;
            impulseDir = hitDirection;
        }

        // ─────────────────────────────────────────────
        // NEW: add a vertical "bump" so we don't get glued to static stuff
        // ─────────────────────────────────────────────
        // Stronger bump at higher severity; tweak 0.15f / 0.45f to taste.
        float verticalBoost = Mathf.Lerp(0.15f, 0.45f, sev01);

        Vector3 bumpDir = impulseDir;
        bumpDir.y += verticalBoost;
        bumpDir.Normalize();

        // Apply the crash impulse with vertical pop
        rb.AddForce(bumpDir * impulseMagnitude, ForceMode.VelocityChange);

        // Cap crash launch velocity.
        ApplyCrashVelocityCaps();

        // --- Torque (spin) stays as you had it ---

        Vector3 toObstacleWorld = -hitDirection;
        Vector3 toObstacleLocal = transform.InverseTransformDirection(toObstacleWorld);

        float sideSign = Mathf.Sign(toObstacleLocal.x);
        if (Mathf.Abs(sideSign) < 0.001f)
            sideSign = Mathf.Sign(Vector3.Dot(toObstacleWorld, transform.right));

        Vector3 yawTorque = Vector3.up * torqueMagnitude * crashYawTorqueMultiplier * sideSign;
        Vector3 rollAxis = transform.forward;
        Vector3 rollTorque = rollAxis * torqueMagnitude * crashRollTorqueMultiplier * sideSign;

        Vector3 contactOffset = contactPointWS - transform.position;
        Vector3 pitchAxis = transform.right;
        float pitchSign = Mathf.Sign(Vector3.Dot(Vector3.Cross(contactOffset, hitDirection), pitchAxis));
        Vector3 pitchTorque = pitchAxis * torqueMagnitude * crashPitchTorqueMultiplier * pitchSign;

        rb.AddTorque(yawTorque + rollTorque + pitchTorque, ForceMode.VelocityChange);

        // Damage / fuel handling
        float sev01ForDamage = sev01;

        if (applyDamage)
        {
            bool lethalFromThisCrash = ApplyCrashHpAndFuelFromSeverity(sev01ForDamage);

            if (RacingPopups.IsReady)
                RacingPopups.Crash(_lastCrashSeverity, GetPopupPosition());

            if (lethalFromThisCrash)
            {
                var gm = GameManager_Racing.Instance;
                if (gm != null)
                    gm.OnCarCrashLethal(sev01ForDamage);

                // Run-ending crash: notify listeners with severity used for damage.
                try { OnLethalCrash?.Invoke(sev01ForDamage); } catch { /* ignore listener errors */ }
            }

            // Start cooldown AFTER damage
            _nextCrashAllowedTime = Time.time + crashDamageCooldown;
        }
        else
        {
            Debug.Log($"[CarController] Crash occurred but damage skipped (cooldown active, {Mathf.Max(0f, _nextCrashAllowedTime - Time.time):F2}s remain).");
        }


    }

    private void HandleSteering()
    {
        if (rb == null) return;
        if (_inCrash || _isReorienting) return;
        if (_flipMashActive) return;

        float speed = rb.velocity.magnitude;
        float forwardSpeed = Vector3.Dot(rb.velocity, transform.forward);
        float steerSpeed = Mathf.Max(0f, effectiveTurnSpeed * GetTemporaryHandlingMultiplier());

        bool driftPhysicsActive = isDrifting || _driftGlideActive;

        float steerDirection = 1f;
        if (invertSteeringWhenReversing && forwardSpeed < -0.1f)
        {
            steerDirection = -1f;
            steerSpeed *= reverseSteerMultiplier;
        }

        float topSpeedForSteering = speedForSteerCurve > 0f ? speedForSteerCurve : Mathf.Max(1f, effectiveMaxSpeed);
        float t = Mathf.Clamp01(speed / topSpeedForSteering);
        float speedSteerMul = Mathf.Lerp(lowSpeedSteerMultiplier, highSpeedSteerMultiplier, t);
        float driftSteerMul = isDrifting ? Mathf.Lerp(1f, maxDriftSteerMultiplier, driftCharge) : 1f;

        if (Mathf.Abs(steeringInput) > 0.001f)
        {
            float iceSteerMul = 1f;

            if (enableIceSteerRamp && _onIceSurface && speed > 0.25f)
            {
                float absIn = Mathf.Abs(steeringInput);
                int signNow = absIn > 0.001f ? (steeringInput > 0f ? 1 : -1) : 0;

                // If we flick directions, knock charge down a bit (prevents instant snap)
                if (signNow != 0 && _iceSteerSign != 0 && signNow != _iceSteerSign)
                    _iceSteerCharge01 = Mathf.Max(0f, _iceSteerCharge01 - iceSteerFlipPenalty);

                if (signNow != 0) _iceSteerSign = signNow;

                // Build while steering, decay when not
                float target = absIn > 0.05f ? 1f : 0f;
                float rate = target > _iceSteerCharge01 ? iceSteerRampUpRate : iceSteerRampDownRate;
                _iceSteerCharge01 = Mathf.MoveTowards(_iceSteerCharge01, target, rate * Time.deltaTime);

                // Convert charge -> usable steering factor
                iceSteerMul = Mathf.Lerp(iceSteerMinFactor, 1f, _iceSteerCharge01);
            }



            bool tryingToMove =
                (!IsOutOfFuel && (GetAccelerateKeyOrTrigger() || GetBrakeKeyOrTrigger()))
                || IsOutOfFuel;

            // Out-of-fuel physics mode: always allow yaw (not gated by minSpeedToSteer / allowSteerWhenTryingToMove).
            if (speed < minSpeedToSteer && !isOutOfFuel && !(allowSteerWhenTryingToMove && tryingToMove))
            {
                // If you’re using the ice steer “charge”, force it to bleed off so it doesn’t feel sticky.
                _iceSteerCharge01 = Mathf.MoveTowards(_iceSteerCharge01, 0f, iceSteerRampDownRate * Time.deltaTime);

                // No turning-in-place.
                return;
            }

            float steerAmount = steeringInput * steerDirection * steerSpeed * speedSteerMul * driftSteerMul * iceSteerMul * Time.deltaTime;


            transform.Rotate(0f, steerAmount, 0f, Space.Self);

            bool acceleratingDrift = !_inputsSuppressedThisFrame
                && !_suppressThrottleBrakeThisFrame
                && GetAccelerateKeyOrTrigger()
                && !GetBrakeKeyOrTrigger();

            if (isDrifting && speed > 0.1f && !acceleratingDrift)
            {
                float sign = Mathf.Sign(steeringInput);
                Vector3 sideDir = Vector3.Cross(Vector3.up, transform.forward) * sign;
                float sideMul = Mathf.Lerp(0.5f, 1f, driftCharge);
                // Reduce lateral snap during flip rebuild delay.
                float sideForceScale = Time.time < _driftFlipBlockUntil ? 0.4f : 1f;
                rb.AddForce(sideDir * driftSideForce * sideMul * sideForceScale, ForceMode.Acceleration);
            }
        }

        if (useAutoAlignToVelocity &&
            Mathf.Abs(steeringInput) < 0.001f &&
            rb.velocity.sqrMagnitude > 0.1f)
        {
            Vector3 flatVel = new Vector3(rb.velocity.x, 0f, rb.velocity.z);
            if (flatVel.sqrMagnitude > 0.0001f)
            {
                Vector3 velDir = flatVel.normalized;
                float forwardDot = Vector3.Dot(velDir, transform.forward);
                if (forwardDot > 0.1f)
                {
                    Quaternion targetRot = Quaternion.LookRotation(velDir, Vector3.up);
                    transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, Time.deltaTime * autoAlignStrength);
                }
            }
        }
    }

    private void HandleMovement()
    {
        if (rb == null) return;

        // Keyboard + gamepad (via RacingInputReader or legacy)
        bool forwardKey = GetAccelerateKeyOrTrigger();
        bool reverseKey = GetBrakeKeyOrTrigger();

        if (_inputsSuppressedThisFrame || _suppressThrottleBrakeThisFrame)
        {
            forwardKey = false;
            reverseKey = false;
        }

        if (_flipMashActive)
        {
            // You are flipped: no throttle/brake/steer (the only “input” is the UI mash)
            forwardKey = false;
            reverseKey = false;
            steeringInput = 0f; // if steeringInput is your cached axis
        }

        // Grounded check early so we can use it anywhere in this method.
        bool groundedNow = CheckIfGrounded();
        RefreshGroundNormalForDriving(groundedNow);

        // Throttle/brake along surface tangent when grounded (no need to wait for pitch alignment on steep terrain).
        Vector3 forward = GetDriveForwardAlongSurface(transform.forward, _lastStableGroundNormal, groundedNow);

        float speed = rb.velocity.magnitude;
        float forwardSpeed = Vector3.Dot(rb.velocity, forward);

        // Treat glide the same as drift for physics retention.
        bool driftPhysicsActive = isDrifting || _driftGlideActive;

        if (!isOutOfFuel && maxFuel > 0f)
        {
            // Brake overrides throttle: both keys = decelerate (reverse/brake path), not full accel.
            bool accelerating = forwardKey && !reverseKey;
            bool brakingOrReverse = reverseKey;
            bool nearIdleSpeed = speed <= idleSpeedThreshold + 0.001f;

            bool wantsSteerTraction =
    groundedNow &&
    enableSteerTraction &&
    !driftButtonHeld &&
    Mathf.Abs(steeringInput) > 0.001f &&
    !accelerating &&
    !brakingOrReverse;

            float blendSpeed = wantsSteerTraction ? steerTractionBlendIn : steerTractionBlendOut;
            float blendTarget = wantsSteerTraction ? 1f : 0f;
            _steerTractionBlend = Mathf.MoveTowards(_steerTractionBlend, blendTarget, blendSpeed * Time.fixedDeltaTime);


            if (!driftPhysicsActive)
            {
                if (accelerating)
                {
                    // IMPROVED: Apply malfunction multiplier for smooth throttle reduction instead of complete cutoff
                    float malfunctionAdjustedAccel = effectiveAcceleration * _currentMalfunctionMultiplier;
                    // Ensure we always have some minimum acceleration during malfunction to prevent immobility
                    if (useSmoothMalfunction && _currentMalfunctionMultiplier < 1f)
                    {
                        float hpFrac = HPPercent;
                        float mFloorScale = hpFrac < degradeStartHPFraction ? 0.18f : 0.45f;
                        malfunctionAdjustedAccel = Mathf.Max(malfunctionAdjustedAccel, minimumAccelerationFloor * mFloorScale);
                    }
                    rb.AddForce(forward * malfunctionAdjustedAccel, ForceMode.Acceleration);
                    ConsumeFuel(fuelUsePerSecondAtFullThrottle * Time.fixedDeltaTime);
                }
                else if (brakingOrReverse)
                {
                    float dt = Time.fixedDeltaTime;

                    // In air: only reduce speed magnitude (don't apply reverse – that would flip velocity backward).
                    if (!groundedNow)
                    {
                        Vector3 v = rb.velocity;
                        float mag = v.magnitude;
                        if (mag > 0.01f)
                        {
                            float decel = maxBrakeDecelPerSecond > 0f ? maxBrakeDecelPerSecond : 12f;
                            float newMag = Mathf.Max(0f, mag - decel * dt);
                            rb.velocity = v.normalized * newMag;
                        }
                        ConsumeFuel(fuelUsePerSecondBraking * Time.fixedDeltaTime);
                    }
                    else
                    {
                        Vector3 n = _lastStableGroundNormal.sqrMagnitude > 1e-8f ? _lastStableGroundNormal.normalized : Vector3.up;
                        Vector3 vTan = Vector3.ProjectOnPlane(rb.velocity, n);
                        float currentLong = Vector3.Dot(vTan, forward);

                        if (currentLong > 0f)
                        {
                            float decel = maxBrakeDecelPerSecond > 0f ? maxBrakeDecelPerSecond : 1.0f;
                            float newLong = Mathf.MoveTowards(currentLong, 0f, decel * dt);
                            SetLongitudinalVelocityAlongSurface(forward, newLong);
                        }
                        else
                        {
                            float reverseAccel = maxReverseAccelPerSecond > 0f ? maxReverseAccelPerSecond : 1.0f;
                            float targetReverseSpeed = -Mathf.Max(1f, effectiveMaxSpeed * 0.4f);
                            float newLong = Mathf.MoveTowards(currentLong, targetReverseSpeed, reverseAccel * dt);
                            SetLongitudinalVelocityAlongSurface(forward, newLong);
                        }

                        ConsumeFuel(fuelUsePerSecondBraking * Time.fixedDeltaTime);
                    }
                }
                else
                {
                    // Coasting (no W/S)
                    if (groundedNow)
                    {
                        if (forwardSpeed < -0.1f)
                        {
                            float reverseDecel = Mathf.Min(maxReverseAccelPerSecond, 3.5f);
                            float newLong = Mathf.MoveTowards(forwardSpeed, 0f, reverseDecel * Time.fixedDeltaTime);
                            SetLongitudinalVelocityAlongSurface(forward, newLong);
                        }
                        else if (speed > 0.01f)
                        {
                            float decel = coastLowDecelPerSecond;
                            ApplyCoastDecelAlongSurface(decel, Time.fixedDeltaTime);
                        }

                        // Steer rolling traction while coasting
                        if (_steerTractionBlend > 0.0001f && enableSteerTraction && !_onIceSurface && !driftButtonHeld && Mathf.Abs(steeringInput) > 0.001f)
                        {
                            Vector3 flatForward = new Vector3(transform.forward.x, 0f, transform.forward.z).normalized;
                            Vector3 vel = rb.velocity;
                            Vector3 flatVel = new Vector3(vel.x, 0f, vel.z);

                            if (flatVel.sqrMagnitude > (minSpeedForSteerTraction * minSpeedForSteerTraction))
                            {
                                float t = steerTractionReorientRate * _steerTractionBlend * Time.fixedDeltaTime;
                                Vector3 blendedDir = Vector3.Slerp(flatVel.normalized, flatForward, t).normalized;

                                Vector3 fwdComp = flatForward * Vector3.Dot(flatVel, flatForward);
                                Vector3 lateral = flatVel - fwdComp;

                                float latDamp = lateralFrictionWhileSteering * _steerTractionBlend;
                                lateral *= Mathf.Exp(-latDamp * Time.fixedDeltaTime);

                                float mag = (fwdComp + lateral).magnitude;
                                Vector3 newFlat = blendedDir * mag;
                                rb.velocity = new Vector3(newFlat.x, vel.y, newFlat.z);
                            }

                            float coastMul = steerRollingAccelCoastMultiplier;
                            if (_onIceSurface && !applySteerRollingAccelOnIce)
                                coastMul = 0f;

                            rb.AddForce(flatForward * (steerRollingAccel * coastMul * _steerTractionBlend), ForceMode.Acceleration);
                        }
                    }
                }
            }
            else
            {
                // Drifting/gliding with fuel
                bool consumedFuelThisFrame = false;

                if (accelerating)
                {
                    float accelMul = (useFullAccelWhileDrifting ? 1f : driftForwardAccelMultiplier);
                    // IMPROVED: Apply malfunction multiplier during drift too
                    float malfunctionAdjustedAccel = effectiveAcceleration * _currentMalfunctionMultiplier;
                    if (useSmoothMalfunction && _currentMalfunctionMultiplier < 1f)
                    {
                        float hpFrac = HPPercent;
                        float mFloorScale = hpFrac < degradeStartHPFraction ? 0.18f : 0.45f;
                        malfunctionAdjustedAccel = Mathf.Max(malfunctionAdjustedAccel, minimumAccelerationFloor * mFloorScale);
                    }
                    rb.AddForce(forward * malfunctionAdjustedAccel * accelMul, ForceMode.Acceleration);
                    ConsumeFuel(fuelUsePerSecondAtFullThrottle * Time.fixedDeltaTime);
                    consumedFuelThisFrame = true;
                }

                if (brakingOrReverse && isDrifting)
                {
                    ConsumeFuel(fuelUsePerSecondBraking * Time.fixedDeltaTime);
                    consumedFuelThisFrame = true;
                }

                // Drift/glide without accel or brake still burns fuel (you're moving fast)
                if (!consumedFuelThisFrame && (isDrifting || _driftGlideActive))
                {
                    ConsumeFuel(fuelUsePerSecondAtFullThrottle * Time.fixedDeltaTime);
                }
            }

            if (!accelerating && !brakingOrReverse && !driftPhysicsActive && nearIdleSpeed)
            {
                ConsumeFuel(idleFuelUsePerSecond * Time.fixedDeltaTime);
            }
        }
        else if (isOutOfFuel)
        {
        }

        // Uphill assist: ramps up/down smoothly so steep ramps don't feel underpowered or jerky.
        float assistTarget = 0f;
        Vector3 assistDir = forward;
        bool throttleForAssist = forwardKey && !reverseKey && !isOutOfFuel && maxFuel > 0f && groundedNow;
        if (throttleForAssist && TryGetSlopeDriveAssist(out Vector3 surfFwd, out float extraA))
        {
            assistDir = surfFwd;
            assistTarget = extraA;
        }

        float assistDt = Time.fixedDeltaTime;
        float assistStep = assistTarget > _slopeAssistSmoothed
            ? slopeDriveAssistRiseSpeed * assistDt
            : slopeDriveAssistFallSpeed * assistDt;
        _slopeAssistSmoothed = Mathf.MoveTowards(_slopeAssistSmoothed, assistTarget, assistStep);
        if (_slopeAssistSmoothed > 1e-4f)
            rb.AddForce(assistDir * (_slopeAssistSmoothed * _currentMalfunctionMultiplier), ForceMode.Acceleration);

        rb.drag = driftPhysicsActive ? effectiveDrag * 0.01f : effectiveDrag;

        speed = rb.velocity.magnitude;

        if (driftPhysicsActive && groundedNow)
        {
            if (driftEntrySpeed > 0.1f && speed > 0.01f)
            {
                if (driftClampSpeed <= 0f)
                    driftClampSpeed = driftEntrySpeed;

                if (driftButtonHeld)
                    driftPeakSpeed = Mathf.Max(driftPeakSpeed, rb.velocity.magnitude);

                Vector3 velDir = rb.velocity.sqrMagnitude > 0.0001f ? rb.velocity.normalized : transform.forward;

                bool gentleBrakeWhileDrifting = reverseKey;
                bool noThrottleNoBrake = (!forwardKey && !reverseKey);

                if (gentleBrakeWhileDrifting)
                {
                    driftClampSpeed -= driftBrakeDecayPerSecond * Time.fixedDeltaTime;
                }
                else if (noThrottleNoBrake)
                {
                    float decayPerSecond = (_driftGlideActive && !isDrifting) ? driftGlideDecayPerSecond : driftSpeedDecayPerSecond;
                    driftClampSpeed -= decayPerSecond * Time.fixedDeltaTime;
                }

                if (driftClampSpeed < 0f) driftClampSpeed = 0f;

                float currentMag = rb.velocity.magnitude;

                Vector3 flatForward = new Vector3(transform.forward.x, 0f, transform.forward.z).normalized;
                Vector3 flatVel = new Vector3(velDir.x, 0f, velDir.z).normalized;

                float steerInfluence = Mathf.Clamp01(Mathf.Abs(steeringInput));
                const float driftAlignStrength = 2f;
                float blend = Mathf.Clamp01(steerInfluence * driftAlignStrength * Time.fixedDeltaTime);

                Vector3 finalDir = (_driftGlideActive && !isDrifting)
                    ? flatVel
                    : Vector3.Slerp(flatVel, flatForward, blend);

                if (finalDir.sqrMagnitude < 0.0001f)
                    finalDir = flatForward;

                float targetMagnitude;
                bool brakingDrift = reverseKey;

                bool speedBoostActiveForDrift = _isBoosting || HasAnySpeedBoost;

                bool holdingThrottle = forwardKey && !reverseKey;

                if (!brakingDrift && lockToDriftPeakSpeed && driftButtonHeld && speedBoostActiveForDrift)
                {
                    targetMagnitude = Mathf.Max(driftPeakSpeed, currentMag, driftClampSpeed);
                }
                else
                {
                    float clampMag = Mathf.Max(driftClampSpeed, 0f);
                    if (!holdingThrottle)
                    {
                        // Coasting drift keeps speed feel (locked glide) without requiring throttle.
                        targetMagnitude = Mathf.Max(currentMag, clampMag);
                    }
                    else
                    {
                        // Throttle during non-boost drift should not create free speed gain.
                        targetMagnitude = Mathf.Min(currentMag, clampMag);
                    }
                }

                float cap = GetCurrentSpeedCap();
                targetMagnitude = Mathf.Min(targetMagnitude, cap);

                if (holdingThrottle && speedBoostActiveForDrift)
                    targetMagnitude = Mathf.Max(targetMagnitude, currentMag);

                float smoothRate = 15f;
                float smoothedMag = Mathf.Lerp(currentMag, targetMagnitude, smoothRate * Time.fixedDeltaTime);

                float y = rb.velocity.y;
                Vector3 horiz = finalDir.normalized * Mathf.Max(0f, smoothedMag);
                rb.velocity = new Vector3(horiz.x, y, horiz.z);

                if (isDrifting && Mathf.Abs(steeringInput) > 0.001f && currentMag > 0.1f && !holdingThrottle)
                {
                    float sign = Mathf.Sign(steeringInput);
                    Vector3 sideDir = Vector3.Cross(Vector3.up, transform.forward) * sign;
                    float sideMul = Mathf.Lerp(0.5f, 1f, driftCharge);
                    float sideForceScale = Time.time < _driftFlipBlockUntil ? 0.4f : 1f;
                    rb.AddForce(sideDir * driftSideForce * sideMul * sideForceScale, ForceMode.Acceleration);
                }
            }
        }
        // Non-drift speed cap: ApplyGroundAwareSpeedCapClamp (end of FixedUpdate, after boost pads & ramp sampling).
    }


    private void ConsumeFuel(float amount)
    {
        if (isOutOfFuel || maxFuel <= 0f) return;

        // Skip fuel consumption during mash IF we're draining health instead
        if (_flipMashActive && mashDrainsHealth) return;

        // If draining fuel during mash, that's handled in the Update loop, not here
        if (_flipMashActive && !mashDrainsHealth) return;

        if (!_flipMashActive)
        {
            amount *= Mathf.Max(0f, currentFuelUseMultiplier);
        }


        var mgr = RacingSkillTreeManager.Instance;
        int lvlFuel = mgr?.GetLevel(SkillType.FuelEfficiency) ?? 0;

        float eff = 1f;
        if (lvlFuel > 0)
        {
            if (fuelMode == SkillApplicationMode.Multiplicative)
                eff = Mathf.Max(0.01f, fuelValue);
            else
                eff = Mathf.Max(0.01f, 1f + fuelValue);
        }

        amount /= eff;

        currentFuel -= amount;
        if (currentFuel <= 0f)
        {
            currentFuel = 0f;
            bool firstEmpty = !isOutOfFuel;
            isOutOfFuel = true;
            Debug.Log("[CarController] Fuel depleted.");
            if (firstEmpty)
                NotifyCrashFeedbackOnly(0.72f);
        }
        else
        {
            currentFuel = Mathf.Min(currentFuel, maxFuel);
        }
    }

    private float surfaceTurnMultiplier = 1f; // runtime surface multiplier for steering (fixes lost multiplier when skills apply)

    /// <summary>
    /// World-axis bounds, rays along <see cref="Vector3.down"/> — used when the car is tipped so local "underside" samples miss the track.
    /// </summary>
    private void AccumulateSurfaceSamplesWorldDownFromColliderBounds(
        ref float sumMaxSpeedMul,
        ref float sumAccelMul,
        ref float sumTurnMul,
        ref float sumDragMul,
        ref float sumFuelMul,
        ref int samplesCounted,
        ref int nonDefaultSamples,
        ref int grassSamplesLocal,
        ref int iceSamples,
        ref float sumIceDynamicFriction,
        ref float sumIceStaticFriction,
        ref float sumIceHandling,
        ref bool anyBoostRamp,
        ref float bestBoostAccel,
        ref float bestBoostMaxSpeed,
        ref bool boostDuringCrash,
        ref float boostCrashMult)
    {
        Bounds bounds = carCollider.bounds;
        Vector3 center = bounds.center;
        Vector3 extents = bounds.extents * surfaceSampleExtent;
        float minThroughCar = bounds.size.y + raycastHeightOffset + 0.2f;
        float extraReach = raycastExtraDistance >= 0f ? raycastExtraDistance : -raycastExtraDistance;
        float rayDistance = minThroughCar + extraReach;

        for (int ix = 0; ix < samplesX; ix++)
        {
            float tx = (ix + 0.5f) / samplesX;
            float x = Mathf.Lerp(center.x - extents.x, center.x + extents.x, tx);

            for (int iz = 0; iz < samplesZ; iz++)
            {
                float tz = (iz + 0.5f) / samplesZ;
                float z = Mathf.Lerp(center.z - extents.z, center.z + extents.z, tz);

                Vector3 origin = new Vector3(x, bounds.max.y + raycastHeightOffset, z);

                if (debugSurfaceRays)
                    Debug.DrawLine(origin, origin + Vector3.down * rayDistance, new Color(1f, 0.4f, 0.9f));

                EvaluateSurfaceWithIce(origin, rayDistance,
                    ref sumMaxSpeedMul, ref sumAccelMul, ref sumTurnMul,
                    ref sumDragMul, ref sumFuelMul,
                    ref samplesCounted, ref nonDefaultSamples, ref grassSamplesLocal,
                    ref iceSamples, ref sumIceDynamicFriction, ref sumIceStaticFriction, ref sumIceHandling,
                    ref anyBoostRamp, ref bestBoostAccel, ref bestBoostMaxSpeed, ref boostDuringCrash, ref boostCrashMult);
            }
        }
    }

    private void SampleGroundAndUpdateMultipliers()
    {
        if (carCollider == null) return;

        // After mash recovery, force default (non-boost) surface so we don't keep boosted speed/accel from before crash.
        if (_postMashRecoveryIgnoreBoostUntil > 0f && Time.time < _postMashRecoveryIgnoreBoostUntil)
        {
            ApplySurfaceMultipliers(1f, 1f, 1f, 1f);
            _onBoostSurface = false;
            _currentBoostAccel = 0f;
            _currentBoostMaxSpeed = 0f;
            _currentBoostDuringCrash = false;
            _currentBoostCrashMultiplier = 0.5f;
            currentSteeringDamp = baseSteeringDamp;
            offDefaultFraction = 0f;
            grassFraction = 0f;
            currentFuelUseMultiplier = 1f;
            _onIceSurface = false;
            _iceDynamicFrictionTarget = 1f;
            _iceStaticFrictionTarget = 1f;
            _iceHandlingTarget = 1f;
            return;
        }

        int totalSamples = samplesX * samplesZ;
        if (totalSamples <= 0)
        {
            ApplySurfaceMultipliers(1f, 1f, 1f, 1f);
            currentSteeringDamp = baseSteeringDamp;
            offDefaultFraction = 0f;
            grassFraction = 0f;
            currentFuelUseMultiplier = 1f;

            // NEW: Reset ice state when no samples
            _onIceSurface = false;
            _iceDynamicFrictionTarget = 1f;
            _iceStaticFrictionTarget = 1f;
            _iceHandlingTarget = 1f;
            return;
        }

        float sumMaxSpeedMul = 0f;
        float sumAccelMul = 0f;
        float sumTurnMul = 0f;
        float sumDragMul = 0f;
        float sumFuelMul = 0f;

        int samplesCounted = 0;
        int nonDefaultSamples = 0;
        int grassSamplesLocal = 0;

        // Boost/Ramp: take best from any sample so we don't miss ramp when only one wheel hits
        bool anyBoostRamp = false;
        float bestBoostAccel = 0f;
        float bestBoostMaxSpeed = 0f;
        bool boostDuringCrash = false;
        float boostCrashMult = 0.5f;

        // NEW: Ice tracking
        int iceSamples = 0;
        float sumIceDynamicFriction = 0f;
        float sumIceStaticFriction = 0f;
        float sumIceHandling = 0f;

        bool tippedForSurface =
            useTippedOverWorldDownSampler &&
            Vector3.Dot(transform.up, Vector3.up) < tippedOverSurfaceUpDotThreshold;

        if (tippedForSurface)
        {
            AccumulateSurfaceSamplesWorldDownFromColliderBounds(
                ref sumMaxSpeedMul, ref sumAccelMul, ref sumTurnMul, ref sumDragMul, ref sumFuelMul,
                ref samplesCounted, ref nonDefaultSamples, ref grassSamplesLocal,
                ref iceSamples, ref sumIceDynamicFriction, ref sumIceStaticFriction, ref sumIceHandling,
                ref anyBoostRamp, ref bestBoostAccel, ref bestBoostMaxSpeed, ref boostDuringCrash, ref boostCrashMult);
        }
        else if (boxCollider != null)
        {
            Vector3 size = boxCollider.size;
            Vector3 center = boxCollider.center;

            float halfX = size.x * 0.5f * surfaceSampleExtent;
            float halfZ = size.z * 0.5f * surfaceSampleExtent;
            float halfY = size.y * 0.5f;

            for (int ix = 0; ix < samplesX; ix++)
            {
                float tx = (ix + 0.5f) / samplesX;
                float localX = Mathf.Lerp(-halfX, halfX, tx);

                for (int iz = 0; iz < samplesZ; iz++)
                {
                    float tz = (iz + 0.5f) / samplesZ;
                    float localZ = Mathf.Lerp(-halfZ, halfZ, tz);

                    Vector3 localPoint = new Vector3(localX, -halfY + raycastHeightOffset, localZ) + center;
                    Vector3 origin = transform.TransformPoint(localPoint);
                    float rayDistance = size.y + raycastExtraDistance;

                    if (debugSurfaceRays)
                        Debug.DrawLine(origin, origin + Vector3.down * rayDistance, Color.cyan);

                    EvaluateSurfaceWithIce(origin, rayDistance,
                        ref sumMaxSpeedMul, ref sumAccelMul, ref sumTurnMul,
                        ref sumDragMul, ref sumFuelMul,
                        ref samplesCounted, ref nonDefaultSamples, ref grassSamplesLocal,
                        ref iceSamples, ref sumIceDynamicFriction, ref sumIceStaticFriction, ref sumIceHandling,
                        ref anyBoostRamp, ref bestBoostAccel, ref bestBoostMaxSpeed, ref boostDuringCrash, ref boostCrashMult);
                }
            }
        }
        else
        {
            Bounds bounds = carCollider.bounds;
            Vector3 center = bounds.center;
            Vector3 extents = bounds.extents * surfaceSampleExtent;

            for (int ix = 0; ix < samplesX; ix++)
            {
                float tx = (ix + 0.5f) / samplesX;
                float x = Mathf.Lerp(center.x - extents.x, center.x + extents.x, tx);

                for (int iz = 0; iz < samplesZ; iz++)
                {
                    float tz = (iz + 0.5f) / samplesZ;
                    float z = Mathf.Lerp(center.z - extents.z, center.z + extents.z, tz);

                    Vector3 origin = new Vector3(x, bounds.max.y + raycastHeightOffset, z);
                    float rayDistance = bounds.size.y + raycastHeightOffset + raycastExtraDistance;

                    if (debugSurfaceRays)
                        Debug.DrawLine(origin, origin + Vector3.down * rayDistance, Color.cyan);

                    EvaluateSurfaceWithIce(origin, rayDistance,
                        ref sumMaxSpeedMul, ref sumAccelMul, ref sumTurnMul,
                        ref sumDragMul, ref sumFuelMul,
                        ref samplesCounted, ref nonDefaultSamples, ref grassSamplesLocal,
                        ref iceSamples, ref sumIceDynamicFriction, ref sumIceStaticFriction, ref sumIceHandling,
                        ref anyBoostRamp, ref bestBoostAccel, ref bestBoostMaxSpeed, ref boostDuringCrash, ref boostCrashMult);
                }
            }
        }

        _onBoostSurface = false;
        _currentBoostAccel = 0f;
        _currentBoostMaxSpeed = 0f;
        _currentBoostDuringCrash = false;
        _currentBoostCrashMultiplier = 0.5f;


        if (samplesCounted == 0)
        {
            ApplySurfaceMultipliers(1f, 1f, 1f, 1f);
            currentSteeringDamp = baseSteeringDamp;
            offDefaultFraction = 0f;
            grassFraction = 0f;
            currentFuelUseMultiplier = 1f;

            // NEW: No ice
            _onIceSurface = false;
            _iceDynamicFrictionTarget = 1f;
            _iceStaticFrictionTarget = 1f;
            _iceHandlingTarget = 1f;
        }
        else
        {
            float avgMaxSpeedMul = sumMaxSpeedMul / samplesCounted;
            float avgAccelMul = sumAccelMul / samplesCounted;
            float avgTurnMul = sumTurnMul / samplesCounted;
            float avgDragMul = sumDragMul / samplesCounted;

            ApplySurfaceMultipliers(avgMaxSpeedMul, avgAccelMul, avgTurnMul, avgDragMul);
            currentSteeringDamp = baseSteeringDamp;
            offDefaultFraction = (float)nonDefaultSamples / samplesCounted;
            grassFraction = (float)grassSamplesLocal / samplesCounted;
            currentFuelUseMultiplier = Mathf.Max(0.01f, sumFuelMul / samplesCounted);

            // NEW: Ice handling
            if (iceSamples > 0)
            {
                _onIceSurface = true;
                _iceDynamicFrictionTarget = sumIceDynamicFriction / iceSamples;
                _iceStaticFrictionTarget = sumIceStaticFriction / iceSamples;
                _iceHandlingTarget = sumIceHandling / iceSamples;
            }
            else
            {
                _onIceSurface = false;
                _iceDynamicFrictionTarget = 1f;
                _iceStaticFrictionTarget = 1f;
                _iceHandlingTarget = 1f;
            }
        }

        // Use boost from multi-sample if any (more reliable than single ray on ramps); else fallback to single ray
        if (anyBoostRamp)
        {
            _onBoostSurface = true;
            _currentBoostAccel = bestBoostAccel;
            _currentBoostMaxSpeed = bestBoostMaxSpeed;
            _currentBoostDuringCrash = boostDuringCrash;
            _currentBoostCrashMultiplier = boostCrashMult;
        }
        else
        {
            CheckForBoostSurface();
        }
    }

    private void CheckForBoostSurface()
    {
        Vector3 origin = transform.position + Vector3.up * 0.5f;
        float rayDist = 4f;

        if (Physics.Raycast(origin, Vector3.down, out RaycastHit hit, rayDist, groundLayers, QueryTriggerInteraction.Collide))
        {
            GroundSurface surface = hit.collider.GetComponent<GroundSurface>()
                                 ?? hit.collider.GetComponentInParent<GroundSurface>();

            if (surface != null && (surface.surfaceType == SurfaceType.Boost || surface.surfaceType == SurfaceType.Ramp))
            {
                _onBoostSurface = true;
                _currentBoostAccel = surface.boostAcceleration;
                _currentBoostMaxSpeed = surface.boostMaxSpeed;
                _currentBoostDuringCrash = surface.boostDuringCrash;
                _currentBoostCrashMultiplier = surface.boostCrashMultiplier;
            }
        }
    }

    private bool TryGetGroundNormal(Vector3 origin, float distance, out RaycastHit hit)
    {
        // SphereCast is much more stable than a single ray on ramps/edges.
        return Physics.SphereCast(
            origin,
            Mathf.Max(0.01f, groundNormalCastRadius),
            Vector3.down,
            out hit,
            Mathf.Max(0.01f, distance),
            groundLayers,
            QueryTriggerInteraction.Collide
        );
    }

    /// <summary>
    /// Updates <see cref="_lastStableGroundNormal"/> before movement forces so throttle/brake use the same frame's surface.
    /// (Ramp alignment still runs later for rotation smoothing.)
    /// </summary>
    private void RefreshGroundNormalForDriving(bool groundedNow)
    {
        if (!groundedNow || carCollider == null) return;
        Vector3 castOrigin = carCollider.bounds.center + Vector3.up * 0.25f;
        if (TryGetGroundNormal(castOrigin, groundNormalCheckDistance, out RaycastHit hit))
            _groundNormalMeasured = hit.normal;
    }

    /// <summary>
    /// Blends <see cref="_lastStableGroundNormal"/> toward the raycast normal so grass/road lip hits do not snap
    /// tangent projections and speed caps frame-to-frame.
    /// </summary>
    private void SmoothDrivingGroundNormal()
    {
        if (rb == null || carCollider == null) return;
        if (!CheckIfGrounded()) return;

        float dt = Time.fixedDeltaTime;
        float t = Mathf.Clamp01(groundNormalBlendRate * dt);
        float g = grassFraction;
        if (g > groundNormalMixedGrassMin && g < groundNormalMixedGrassMax)
            t *= groundNormalMixedSurfaceBlendScale;

        _lastStableGroundNormal = Vector3.Slerp(_lastStableGroundNormal, _groundNormalMeasured, t);
        if (_lastStableGroundNormal.sqrMagnitude < 1e-10f)
            _lastStableGroundNormal = Vector3.up;
        else
            _lastStableGroundNormal.Normalize();
    }

    /// <summary>
    /// Tiny upward nudge when grass sampling crosses between mostly-road and mostly-grass (replaces old forward accel assist).
    /// </summary>
    private void ApplyRoadGrassTransitionLift()
    {
        if (roadGrassTransitionLiftSpeed <= 0f || rb == null) return;
        if (_onIceSurface) return;
        if (isDrifting || _driftGlideActive) return;
        if (!CheckIfGrounded()) return;

        float g = grassFraction;
        float prev = _prevGrassFractionForTransitionLift;
        _prevGrassFractionForTransitionLift = g;
        if (prev < 0f) return;

        bool crossedMid =
            (prev < 0.5f && g >= 0.5f) ||
            (prev > 0.5f && g < 0.5f);
        if (!crossedMid) return;

        float now = Time.time;
        if (now - _lastRoadGrassTransitionLiftTime < roadGrassTransitionLiftCooldown) return;

        Vector3 flatV = Vector3.ProjectOnPlane(rb.velocity, Vector3.up);
        if (flatV.magnitude < roadGrassTransitionMinSpeed) return;

        rb.AddForce(Vector3.up * roadGrassTransitionLiftSpeed, ForceMode.VelocityChange);
        _lastRoadGrassTransitionLiftTime = now;
    }

    /// <summary>World forward projected onto the ground plane when grounded; full <paramref name="worldForward"/> in air.</summary>
    private static Vector3 GetDriveForwardAlongSurface(Vector3 worldForward, Vector3 groundNormal, bool grounded)
    {
        Vector3 f = worldForward.normalized;
        if (!grounded) return f;
        Vector3 n = groundNormal.sqrMagnitude > 1e-8f ? groundNormal.normalized : Vector3.up;
        Vector3 onPlane = Vector3.ProjectOnPlane(f, n);
        if (onPlane.sqrMagnitude < 1e-10f)
            return f;
        return onPlane.normalized;
    }

    /// <summary>
    /// Sets speed along <paramref name="surfaceForward"/> while preserving velocity tangent to <see cref="_lastStableGroundNormal"/>.
    /// </summary>
    private void SetLongitudinalVelocityAlongSurface(Vector3 surfaceForward, float newLong)
    {
        if (rb == null) return;
        Vector3 n = _lastStableGroundNormal.sqrMagnitude > 1e-8f ? _lastStableGroundNormal.normalized : Vector3.up;
        Vector3 v = rb.velocity;
        Vector3 fwd = surfaceForward.normalized;
        Vector3 vTan = Vector3.ProjectOnPlane(v, n);
        Vector3 lateral = vTan - fwd * Vector3.Dot(vTan, fwd);
        Vector3 newVTan = fwd * newLong + lateral;
        float vn = Vector3.Dot(v, n);
        rb.velocity = newVTan + vn * n;
    }

    private void ApplyCoastDecelAlongSurface(float decel, float dt)
    {
        if (rb == null) return;
        Vector3 n = _lastStableGroundNormal.sqrMagnitude > 1e-8f ? _lastStableGroundNormal.normalized : Vector3.up;
        Vector3 v = rb.velocity;
        Vector3 vTan = Vector3.ProjectOnPlane(v, n);
        float tanMag = vTan.magnitude;
        if (tanMag < 0.01f) return;
        float newTanMag = Mathf.Max(0f, tanMag - decel * dt);
        vTan *= newTanMag / tanMag;
        float vn = Vector3.Dot(v, n);
        rb.velocity = vTan + vn * n;
    }

    /// <summary>
    /// Extra accel along the surface when climbing (gravity-aware). Used with smoothed blending so it doesn't snap on/off.
    /// </summary>
    private bool TryGetSlopeDriveAssist(out Vector3 surfaceForward, out float accelExtra)
    {
        surfaceForward = transform.forward;
        accelExtra = 0f;
        if (!enableSlopeDriveAssist || rb == null) return false;
        if (slopeDriveAssistDisableOnBoost && _onBoostSurface) return false;
        if (slopeDriveAssistDisableOnIce && _onIceSurface) return false;

        Vector3 n = _lastStableGroundNormal.sqrMagnitude > 1e-6f ? _lastStableGroundNormal.normalized : Vector3.up;

        float cosTilt = Mathf.Clamp(Vector3.Dot(n, Vector3.up), -1f, 1f);
        float tiltDeg = Mathf.Acos(cosTilt) * Mathf.Rad2Deg;

        float minAng = Mathf.Min(slopeDriveAssistMinAngle, slopeDriveAssistMaxAngle);
        float maxAng = Mathf.Max(slopeDriveAssistMinAngle, slopeDriveAssistMaxAngle);
        if (tiltDeg < minAng) return false;

        float steepT = Mathf.InverseLerp(minAng, maxAng, tiltDeg);
        steepT = Mathf.Clamp01(steepT);
        steepT = steepT * steepT * (3f - 2f * steepT);

        Vector3 g = Physics.gravity;
        Vector3 downAlong = Vector3.ProjectOnPlane(g, n);
        if (downAlong.sqrMagnitude < 1e-10f) return false;
        downAlong.Normalize();

        surfaceForward = Vector3.ProjectOnPlane(transform.forward, n);
        if (surfaceForward.sqrMagnitude < 1e-10f) return false;
        surfaceForward.Normalize();

        float climb = Vector3.Dot(surfaceForward, -downAlong);
        if (climb <= 0f) return false;
        climb = Mathf.SmoothStep(0.12f, 1f, climb);

        accelExtra = slopeDriveAssistMaxAccel * steepT * climb;
        return accelExtra > 1e-4f;
    }

    private void AlignToUpVectorPreserveYaw(Vector3 targetUp, float alignSpeed, float dt)
    {
        targetUp = targetUp.sqrMagnitude > 0.0001f ? targetUp.normalized : Vector3.up;

        // Preserve yaw by projecting our forward onto the target plane.
        Vector3 fwd = transform.forward;
        Vector3 projectedForward = Vector3.ProjectOnPlane(fwd, targetUp);

        // If we're near-vertical, fall back to projecting right, etc.
        if (projectedForward.sqrMagnitude < 0.0001f)
            projectedForward = Vector3.ProjectOnPlane(transform.right, targetUp);

        if (projectedForward.sqrMagnitude < 0.0001f)
            return;

        Quaternion targetRot = Quaternion.LookRotation(projectedForward.normalized, targetUp);

        // Smooth align
        transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, Mathf.Clamp01(alignSpeed * dt));
    }

    private void ApplyRampAlignment(float dt)
    {
        if (!enableRampAlignment) return;
        if (rb == null) return;
        if (_inCrash || _isReorienting) return;     // don't fight crash/recovery
        if (IsCrashInvulnerable) return;
        if (IsDeadForMashRecovery || _flipMashActive)
            return;

        // We will align to either:
        // - current ground normal (if grounded)
        // - predicted landing normal (if airborne and close enough)
        Vector3 targetUp = Vector3.up;

        // Use your existing grounded concept if you want:
        // _isGrounded is set during crash only in your code,
        // so here we do a lightweight check.
        bool groundedNow = CheckIfGrounded();

        // origin slightly above car, so casts don't start inside ramps
        Vector3 castOrigin = carCollider != null ? carCollider.bounds.center + Vector3.up * 0.25f : transform.position + Vector3.up * 0.25f;

        if (groundedNow)
        {
            if (TryGetGroundNormal(castOrigin, groundNormalCheckDistance, out RaycastHit hit))
            {
                targetUp = hit.normal;
                _groundNormalMeasured = hit.normal;
            }
            else
            {
                targetUp = _lastStableGroundNormal.sqrMagnitude > 1e-8f ? _lastStableGroundNormal : Vector3.up;
            }

            // Align faster while grounded
            AlignToUpVectorPreserveYaw(targetUp, groundAlignSpeed, dt);
            return;
        }

        // --- Airborne: prevent “weird rotation” after leaving a ramp ---
        // If we’re falling and close enough to something below, start blending toward that landing normal.
        bool falling = rb.velocity.y <= 0.25f;

        if (falling && TryGetGroundNormal(castOrigin, landingPredictDistance, out RaycastHit landHit))
        {
            float dist = landHit.distance;

            // Only start aligning when approaching the surface (so we don't snap mid-air)
            if (dist <= landingAlignStartDistance)
            {
                targetUp = landHit.normal;
                AlignToUpVectorPreserveYaw(targetUp, airAlignSpeed, dt);
                return;
            }
        }

        // Otherwise: keep the last stable ramp normal influence VERY lightly (or do nothing).
        // Doing nothing is safest for "no gameplay changes".
        // If you want slight stabilization, uncomment:
        // AlignToUpVectorPreserveYaw(_lastStableGroundNormal, airAlignSpeed * 0.25f, dt);
    }

    private void EvaluateSurface(
        Vector3 origin,
        float rayDistance,
        ref float sumMaxSpeedMul,
        ref float sumAccelMul,
        ref float sumTurnMul,
        ref float sumDragMul,
        ref float sumFuelMul,
        ref int samplesCounted,
        ref int nonDefaultSamples,
        ref int grassSamplesLocal)
    {
        float maxMul = 1f;
        float accelMul = 1f;
        float turnMul = 1f;
        float dragMul = 1f;
        float fuelMul = 1f;
        bool isNonDefault = false;

        if (Physics.Raycast(origin, Vector3.down, out RaycastHit hit, rayDistance, groundLayers, QueryTriggerInteraction.Collide))
        {

            if (debugSurfaceRays)
                Debug.Log($"[Surface] Hit {hit.collider.name} (trigger={hit.collider.isTrigger})");

            GroundSurface surface =
                hit.collider.GetComponent<GroundSurface>() ??
                hit.collider.GetComponentInParent<GroundSurface>();

            if (surface != null)
            {
                maxMul = surface.maxSpeedMultiplier;
                accelMul = surface.accelerationMultiplier;
                turnMul = surface.turnSpeedMultiplier;
                dragMul = surface.dragMultiplier;

                if (surface.surfaceType != SurfaceType.Default)
                    isNonDefault = true;
                if (surface.surfaceType == SurfaceType.Grass)
                {
                    fuelMul = Mathf.Max(1f, grassFuelUseMultiplier);
                    grassSamplesLocal++;
                }
            }
        }

        sumMaxSpeedMul += maxMul;
        sumAccelMul += accelMul;
        sumTurnMul += turnMul;
        sumDragMul += dragMul;
        sumFuelMul += fuelMul;
        samplesCounted++;
        if (isNonDefault) nonDefaultSamples++;
    }


    private void EvaluateSurfaceWithIce(
        Vector3 origin,
        float rayDistance,
        ref float sumMaxSpeedMul,
        ref float sumAccelMul,
        ref float sumTurnMul,
        ref float sumDragMul,
        ref float sumFuelMul,
        ref int samplesCounted,
        ref int nonDefaultSamples,
        ref int grassSamplesLocal,
        ref int iceSamples,
        ref float sumIceDynamicFriction,
        ref float sumIceStaticFriction,
        ref float sumIceHandling,
        ref bool anyBoostRamp,
        ref float bestBoostAccel,
        ref float bestBoostMaxSpeed,
        ref bool boostDuringCrash,
        ref float boostCrashMult)
    {
        float maxMul = 1f;
        float accelMul = 1f;
        float turnMul = 1f;
        float dragMul = 1f;
        float fuelMul = 1f;
        bool isNonDefault = false;

        if (Physics.Raycast(origin, Vector3.down, out RaycastHit hit, rayDistance, groundLayers, QueryTriggerInteraction.Collide))
        {
            if (debugSurfaceRays)
                Debug.Log($"[Surface] Hit {hit.collider.name} (trigger={hit.collider.isTrigger})");

            GroundSurface surface =
                hit.collider.GetComponent<GroundSurface>() ??
                hit.collider.GetComponentInParent<GroundSurface>();

            if (surface != null)
            {
                maxMul = surface.maxSpeedMultiplier;
                accelMul = surface.accelerationMultiplier;
                turnMul = surface.turnSpeedMultiplier;
                dragMul = surface.dragMultiplier;

                if (surface.surfaceType != SurfaceType.Default)
                    isNonDefault = true;

                if (surface.surfaceType == SurfaceType.Grass)
                {
                    fuelMul = Mathf.Max(1f, grassFuelUseMultiplier);
                    grassSamplesLocal++;
                }

                // NEW: Ice surface detection
                if (surface.surfaceType == SurfaceType.Ice)
                {
                    iceSamples++;
                    sumIceDynamicFriction += surface.iceDynamicFrictionMultiplier;
                    sumIceStaticFriction += surface.iceStaticFrictionMultiplier;
                    sumIceHandling += surface.iceHandlingMultiplier;
                }

                // Boost/Ramp: take best across samples so ramp is detected when any wheel hits it
                if (surface.surfaceType == SurfaceType.Boost || surface.surfaceType == SurfaceType.Ramp)
                {
                    anyBoostRamp = true;
                    if (surface.boostAcceleration > bestBoostAccel) bestBoostAccel = surface.boostAcceleration;
                    if (surface.boostMaxSpeed > bestBoostMaxSpeed) bestBoostMaxSpeed = surface.boostMaxSpeed;
                    boostDuringCrash = surface.boostDuringCrash;
                    boostCrashMult = surface.boostCrashMultiplier;
                }
            }
        }

        sumMaxSpeedMul += maxMul;
        sumAccelMul += accelMul;
        sumTurnMul += turnMul;
        sumDragMul += dragMul;
        sumFuelMul += fuelMul;
        samplesCounted++;
        if (isNonDefault) nonDefaultSamples++;
    }

    /// <summary>
    /// Checks if the car is currently on the ground using raycasts from multiple points.
    /// </summary>
    private bool CheckIfGrounded()
    {
        if (carCollider == null) return false;

        Bounds bounds = carCollider.bounds;
        Vector3 center = bounds.center;
        Vector3 extents = bounds.extents;

        // Check from multiple points under the car
        Vector3[] checkPoints = new Vector3[]
        {
        center, // center
        center + transform.right * extents.x * 0.7f, // front right
        center - transform.right * extents.x * 0.7f, // front left
        center + transform.forward * extents.z * 0.5f, // front
        center - transform.forward * extents.z * 0.5f  // rear
        };

        int groundHits = 0;
        float rayDistance = extents.y + groundCheckDistance;

        foreach (Vector3 point in checkPoints)
        {
            Vector3 rayOrigin = new Vector3(point.x, center.y, point.z);

            if (Physics.Raycast(rayOrigin, Vector3.down, rayDistance, groundCheckLayers, QueryTriggerInteraction.Collide))
            {
                groundHits++;
            }

#if UNITY_EDITOR
            if (debugSurfaceRays)
            {
                Debug.DrawRay(rayOrigin, Vector3.down * rayDistance,
                    Physics.Raycast(rayOrigin, Vector3.down, rayDistance, groundCheckLayers, QueryTriggerInteraction.Collide)
                        ? Color.green : Color.red, 0.1f);
            }
#endif
        }

        // Require at least 2 points touching ground to be considered grounded
        return groundHits >= 2;
    }

    // Add inside CarController class
    public void ApplyDirectDamage(float hpDamage, float fuelPercentOfMax)
    {
        // Flat HP
        if (hpDamage > 0f)
        {
            currentHP = Mathf.Max(0f, currentHP - hpDamage);
        }

        // Fuel as % of max fuel
        if (fuelPercentOfMax > 0f)
        {
            float fuelLoss = Mathf.Max(0f, maxFuel * fuelPercentOfMax);
            ConsumeFuel(fuelLoss);
        }

        // If this hit causes death by HP or fuel, keep behavior consistent with your lethal hooks.
        if (currentHP <= 0f && !isOutOfHP)
        {
            isOutOfHP = true;
            PlayDeathVFX();
        }

        if (currentFuel <= 0f && !isOutOfFuel)
        {
            isOutOfFuel = true;
            NotifyCrashFeedbackOnly(0.72f);
        }
    }

    public void ApplyDirectDamageWithCrashFX(
        float hpDamage,
        float fuelPercentOfMax,
        Vector3 contactPointWS,
        Vector3 contactNormalWS,
        Vector3 hitDirectionWS,
        float impactSpeedOverride = -1f,   // if < 0 => derive from rb speed
        float fxSeverityOverride01 = -1f   // if < 0 => derive from impactSpeed
    )
    {
        if (rb == null) return;

        // Fixed damage ONLY
        ApplyDirectDamage(hpDamage, fuelPercentOfMax);

        // Determine impact speed for presentation/physics
        float speedNow = rb.velocity.magnitude;
        float impactSpeed = (impactSpeedOverride >= 0f) ? impactSpeedOverride : speedNow;
        if (impactSpeedOverride >= 0f)
            impactSpeed = Mathf.Clamp(impactSpeed, 0f, maxImpactSpeed);
        else
            impactSpeed = Mathf.Clamp(impactSpeed, minImpactSpeed, maxImpactSpeed);


        // Determine severity for presentation ONLY (NOT damage)
        float sev01 = (fxSeverityOverride01 >= 0f)
            ? Mathf.Clamp01(fxSeverityOverride01)
            : Mathf.InverseLerp(minImpactSpeed, maxImpactSpeed, impactSpeed);

        // Run global crash hooks (shake/slowmo/etc.)
        var gm = GameManager_Racing.Instance;
        if (gm != null)
            gm.OnCarCrash(impactSpeed, sev01);

        // SFX/VFX
        Vector3 normal = contactNormalWS.sqrMagnitude > 0.0001f ? contactNormalWS.normalized : Vector3.up;
        PlayCrashSfx(crashClipDefault, contactPointWS, crashSfxVolume);
        SpawnCrashImpactVFX(contactPointWS, normal);

        // Full crash state + fling + disable movement, BUT skip severity-based HP/fuel
        Vector3 hitDir = hitDirectionWS;
        hitDir.y = 0f;
        if (hitDir.sqrMagnitude < 0.0001f) hitDir = -transform.forward;
        hitDir.Normalize();

        float crashDuration = Mathf.Lerp(minCrashDuration, maxCrashDuration, sev01);
        float impulseMag = impactSpeed * impulsePerUnitSpeed;
        float torqueMag = impactSpeed * torquePerUnitSpeed;

        // Direct hits (e.g. bounce-back) can pass high impactSpeedOverride; cap impulse so we don't exceed fling cap
        if (maxCrashFlingSpeed > 0f)
            impulseMag = Mathf.Min(impulseMag, maxCrashFlingSpeed);

        // applyDamage = false so TriggerCrash runs the crash state/physics
        // without applying your severity-based HP/fuel logic.
        TriggerCrash(hitDir, crashDuration, impulseMag, torqueMag, sev01, contactPointWS, false);
    }




    private void ApplySurfaceMultipliers(float maxSpeedMul, float accelMul, float turnMul, float dragMul)
    {
        surfaceTurnMultiplier = Mathf.Max(0f, turnMul);

        float targetMaxSpeed = baseMaxSpeed * maxSpeedMul;

        // Smooth surface transitions to prevent stuttering
        if (_smoothedSurfaceMaxSpeed < 0f)
        {
            _smoothedSurfaceMaxSpeed = targetMaxSpeed;
        }
        else
        {
            float lerpSpeed = surfaceMaxSpeedLerpRate;
            _smoothedSurfaceMaxSpeed = Mathf.Lerp(_smoothedSurfaceMaxSpeed, targetMaxSpeed, lerpSpeed * Time.fixedDeltaTime);
        }

        currentMaxSpeed = _smoothedSurfaceMaxSpeed;
        currentAcceleration = baseAcceleration * accelMul;
        currentBrakingForce = baseBrakingForce;
        currentTurnSpeed = baseTurnSpeed * surfaceTurnMultiplier;
        currentDrag = baseDrag * Mathf.Max(0f, dragMul);
    }



    private void RefreshSkillEffects()
    {
        var mgr = RacingSkillTreeManager.Instance;
        if (mgr == null)
        {
            accelValue = maxSpeedValue = steerValue = fuelValue = 0f;
            accelMode = maxSpeedMode = steerMode = fuelMode = SkillApplicationMode.Additive;
            return;
        }

        accelMode = mgr.GetEffectMode(SkillType.Acceleration);
        accelValue = mgr.GetRawEffectValue(SkillType.Acceleration);

        maxSpeedMode = mgr.GetEffectMode(SkillType.MaxSpeed);
        maxSpeedValue = mgr.GetRawEffectValue(SkillType.MaxSpeed);

        steerMode = mgr.GetEffectMode(SkillType.SteeringResponsiveness);
        steerValue = mgr.GetRawEffectValue(SkillType.SteeringResponsiveness);

        fuelMode = mgr.GetEffectMode(SkillType.FuelEfficiency);
        fuelValue = mgr.GetRawEffectValue(SkillType.FuelEfficiency);
    }

    private void CancelAllBoostState(float lockoutSeconds)
    {
        bool wasBoosting = _isBoosting;
        _boostRequested = false;
        ClearBoostOverride();

        _isBoosting = false;
        _boostTimer = 0f;

        _isPostBoost = false;
        _postBoostTimer = 0f;

        _activeBoostIsDrift = false;
        _activeBoostMaxMult = 1f;

        // Clear centralized speed boosts
        ClearAllSpeedBoosts();

        // Also clear legacy close call boost flag
        _closeCallBoosting = false;

        // Optional: wipe cooldown timers so you don't “come out of crash already cooling down”
        _boostCooldownTimer = 0f;
        _driftBoostCooldownTimer = 0f;

        // Lock out all boosts for a bit (covers post-crash drift-release + space presses)
        _boostBlockedUntil = Mathf.Max(_boostBlockedUntil, Time.time + Mathf.Max(0f, lockoutSeconds));

        if (wasBoosting)
        {
            try { OnBoostEnded?.Invoke(); } catch { /* swallow */ }
        }
    }

    private void ApplySkillEffects()
    {
        var mgr = RacingSkillTreeManager.Instance;

        effectiveAcceleration = currentAcceleration;
        effectiveMaxSpeed = currentMaxSpeed;
        effectiveTurnSpeed = currentTurnSpeed;
        effectiveDrag = currentDrag;

        if (mgr != null)
        {
            effectiveAcceleration = mgr.ApplyStatChain(
                currentAcceleration,
                SkillType.Acceleration_Add,
                SkillType.Acceleration_Mul
            );

            effectiveMaxSpeed = mgr.ApplyStatChain(
                currentMaxSpeed,
                SkillType.MaxSpeed_Add,
                SkillType.MaxSpeed_Mul
        );

            float prevMaxFuel = maxFuel;

            maxFuel = mgr.ApplyStatChain(
                baseMaxFuel,
                SkillType.MaxFuel_Add,
                SkillType.MaxFuel_Mul
            );

            if (!Mathf.Approximately(prevMaxFuel, maxFuel))
            {
                if (prevMaxFuel <= 0f)
                {
                    currentFuel = maxFuel;
                }
                else
                {
                    float percent = Mathf.Clamp01(currentFuel / prevMaxFuel);
                    currentFuel = percent * maxFuel;
                }
                currentFuel = Mathf.Clamp(currentFuel, 0f, maxFuel);
            }

            float prevMaxHP = maxHP;

            maxHP = mgr.ApplyStatChain(
                baseMaxHP,
                SkillType.MaxHP_Add,
                SkillType.MaxHP_Mul
            );

            // Scale current HP proportionally when max changes
            if (!Mathf.Approximately(prevMaxHP, maxHP))
            {
                if (prevMaxHP <= 0f)
                {
                    currentHP = maxHP;
                }
                else
                {
                    float percent = Mathf.Clamp01(currentHP / prevMaxHP);
                    currentHP = percent * maxHP;
                }
                currentHP = Mathf.Clamp(currentHP, 0f, maxHP);
            }

            idleFuelUsePerSecond = mgr.ApplyStatChain(
                baseIdleFuelUse,
                SkillType.IdleFuelUse_Add,
                SkillType.IdleFuelUse_Mul
            );

            float drivingFactor = mgr.ApplyStatChain(
                1f,
                SkillType.DrivingFuelUse_Add,
                SkillType.DrivingFuelUse_Mul
            );

            hpRegenPerSecond = mgr.ApplyStatChain(
    baseHpRegenPerSecond,
    SkillType.HPRegen_Add,
    SkillType.HPRegen_Mul
);

            effectiveClicksPerClick = Mathf.Max(1, Mathf.RoundToInt(
    mgr.ApplyStatChain(baseClicksPerClick, SkillType.MashClicksPerClick_Add, SkillType.MashClicksPerClick_Mul)
));

            int extraClickPower = effectiveClicksPerClick - baseClicksPerClick;
            float clickDrainBonus = extraClickPower * drainScalePerClickPower;

            // How much extra passive strength above base
            int extraPassiveStrength = effectivePassiveClickStrength - basePassiveClickStrength;
            float passiveDrainBonus = extraPassiveStrength * drainScalePerPassiveStrength;

            // Also factor in passive click RATE if unlocked (more clicks/sec = more fill = more drain needed)
            float passiveRateBonus = 0f;
            if (mgr.IsPassiveMashUnlocked && effectivePassiveClickRate > 0f)
            {
                // Every 1.0 clicks/sec above base adds some drain
                float extraRate = effectivePassiveClickRate - (basePassiveClickRate > 0f ? basePassiveClickRate : 0.5f);
                passiveRateBonus = Mathf.Max(0f, extraRate) * drainScalePerPassiveStrength;
            }

            // IMPORTANT:
            // Mash-related skills should not make the gauge *harder* to fill. Historically this used to
            // scale drain UP as skills increased (and could combine with other scaling to feel exponential).
            // We instead convert skill bonuses into a drain multiplier in (0..1], so more skill means
            // less drain (or at worst unchanged), never more.
            float totalDrainBonus = Mathf.Max(0f, clickDrainBonus + passiveDrainBonus + passiveRateBonus);
            float minDrainMult = Mathf.Clamp(minSkillDrainMultiplier, 0.05f, 1f);
            _skillDrainMultiplier = 1f / (1f + totalDrainBonus);
            _skillDrainMultiplier = Mathf.Clamp(_skillDrainMultiplier, minDrainMult, 1f);

            Debug.Log($"[MashGauge] Skill drain multiplier: {_skillDrainMultiplier:F2} " +
                      $"(clickPower: +{extraClickPower}, passiveStr: +{extraPassiveStrength}, passiveRate: {effectivePassiveClickRate:F1})");

            if (mgr.IsPassiveMashUnlocked)
            {
                // Base rate comes from the unlock skill (e.g., 0.5 clicks/sec at level 1)
                // Then rate skills modify it further
                float baseRate = basePassiveClickRate > 0f ? basePassiveClickRate : 0.5f; // Default if base is 0
                effectivePassiveClickRate = mgr.ApplyStatChain(
                    baseRate,
                    SkillType.MashPassiveClickRate_Add,
                    SkillType.MashPassiveClickRate_Mul
                );
            }
            else
            {
                effectivePassiveClickRate = 0f; // Locked - no passive clicks
            }

            effectivePassiveClickStrength = Mathf.Max(1, Mathf.RoundToInt(
                mgr.ApplyStatChain(basePassiveClickStrength, SkillType.MashPassiveClickStrength_Add, SkillType.MashPassiveClickStrength_Mul)
            ));

            effectiveFuelPerClick = mgr.ApplyStatChain(
                mashBaseFuelPerClick,
                SkillType.MashFuelPerClick_Add,
                SkillType.MashFuelPerClick_Mul
            );

            fuelUsePerSecondAtFullThrottle = baseFuelUseFullThrottle * drivingFactor;
            fuelUsePerSecondBraking = baseFuelUseBraking * drivingFactor;

            idleFuelUsePerSecond = Mathf.Max(0f, idleFuelUsePerSecond);
            fuelUsePerSecondAtFullThrottle = Mathf.Max(0f, fuelUsePerSecondAtFullThrottle);
            fuelUsePerSecondBraking = Mathf.Max(0f, fuelUsePerSecondBraking);

            // Apply skill chain to the base turn speed, then combine with surface multiplier.
            float newTurnSpeed = mgr.ApplyStatChain(
                baseTurnSpeed,
                SkillType.TurnSpeed_Add,
                SkillType.TurnSpeed_Mul
            );

            // IMPORTANT FIX:
            // Combine skill-modified base turn speed with the surface multiplier so surface
            // turnSpeedMultiplier (from GroundSurface) actually affects final steering.
            currentTurnSpeed = newTurnSpeed * surfaceTurnMultiplier;
            effectiveTurnSpeed = currentTurnSpeed;

            // Force applies to BOTH main boost impulse and sustain acceleration
            boostForce = mgr.ApplyStatChain(baseBoostForce, SkillType.BoostForce_Add, SkillType.BoostForce_Mul);
            boostForce = Mathf.Max(0f, boostForce);

            boostSustainAcceleration = mgr.ApplyStatChain(baseBoostSustainAcceleration, SkillType.BoostForce_Add, SkillType.BoostForce_Mul);
            boostSustainAcceleration = Mathf.Max(0f, boostSustainAcceleration);

            boostDuration = mgr.ApplyStatChain(baseBoostDuration, SkillType.BoostDuration_Add, SkillType.BoostDuration_Mul);
            boostDuration = Mathf.Max(0.05f, boostDuration);

            boostMaxSpeedMultiplier = mgr.ApplyStatChain(baseBoostMaxSpeedMult, SkillType.BoostMaxSpeedMult_Add, SkillType.BoostMaxSpeedMult_Mul);
            boostMaxSpeedMultiplier = Mathf.Max(1f, boostMaxSpeedMultiplier);

            boostCooldown = mgr.ApplyStatChain(baseBoostCooldown, SkillType.BoostCooldown_Add, SkillType.BoostCooldown_Mul);
            boostCooldown = Mathf.Max(0.05f, boostCooldown);

            boostFuelCost = mgr.ApplyStatChain(baseBoostFuelCost, SkillType.BoostFuelCost_Add, SkillType.BoostFuelCost_Mul);
            boostFuelCost = Mathf.Max(0f, boostFuelCost);

            boostCooldown = mgr != null
            ? mgr.ApplyStatChain(baseBoostCooldown, SkillType.BoostCooldown_Add, SkillType.BoostCooldown_Mul)
            : baseBoostCooldown;
            boostCooldown = Mathf.Max(0.05f, boostCooldown);

            // NEW: compute the drift-held boost cooldown separately (unbind from regular boostCooldown)
            float driftCd = baseDriftBoostCooldown;
            if (mgr != null)
                driftCd = mgr.GetDriftHeldBoostCooldownScaled(baseDriftBoostCooldown);
            // store runtime value in a private runtime field (reuse driftBoostCooldown as runtime)
            driftBoostCooldown = Mathf.Max(0.01f, driftCd);

        }

        ApplyDamageDegradationToPerformance();

        if (rb != null)
        {
            float cap = GetCurrentSpeedCap();

            Vector3 v = rb.velocity;
            Vector3 horiz = Vector3.ProjectOnPlane(v, Vector3.up); // ignore vertical
            float horizSpeed = horiz.magnitude;

            if (horizSpeed > cap && horizSpeed > 0.0001f)
            {
                Vector3 horizClamped = horiz * (cap / horizSpeed);
                rb.velocity = new Vector3(horizClamped.x, v.y, horizClamped.z);
            }
        }

#if UNITY_EDITOR
        if (mgr != null)
        {
            Debug.Log(
                $"[CarController] Skills(Add-chain) → " +
                $"Accel_Add={mgr.GetLevel(SkillType.Acceleration_Add)}, " +
                $"MaxSpeed_Add={mgr.GetLevel(SkillType.MaxSpeed_Add)}, " +
                $"Turn_Add={mgr.GetLevel(SkillType.TurnSpeed_Add)}, " +
                $"MaxFuel_Add={mgr.GetLevel(SkillType.MaxFuel_Add)} | " +
                $"effAccel={effectiveAcceleration:F2}, " +
                $"effMaxSpeed={effectiveMaxSpeed:F2}, " +
                $"effTurn={effectiveTurnSpeed:F2}, " +
                $"maxFuel={maxFuel:F1}, " +
                $"idleFuel/s={idleFuelUsePerSecond:F3}, " +
                $"driveFuel/s={fuelUsePerSecondAtFullThrottle:F3} | " +
                $"HP={currentHP:F1}/{maxHP:F1}"
            );
        }
#endif
    }

    /// <param name="severityFallback01">Used when <see cref="CrashSeverityConfig"/> is not assigned or <paramref name="otherRoot"/> is null.</param>
    /// <param name="otherRoot">Obstacle root for kind/mass/scale resolution (recommended for all external crashes).</param>
    /// <param name="extraSeverityMultiplier">Applied after central severity (e.g. bull rush / designer bias).</param>
    public void ApplyExternalCrashDamage(
        Vector3 hitDirection,
        float impactSpeed,
        Vector3 contactPointWS,
        float severityFallback01,
        Transform otherRoot = null,
        Rigidbody otherRb = null,
        Vector3? contactNormalWS = null,
        float extraSeverityMultiplier = 1f)
    {
        if (rb == null)
            return;

        if (IsCrashInvulnerable)
            return;

        if (_inCrash && !_flipMashActive)
            return;

        // Respect internal cooldown so external callers can't bypass invulnerability windows.
        bool damageWindowOpen = Time.time >= _nextCrashAllowedTime;

        float rawImpact = Mathf.Max(0f, impactSpeed);
        float clampedForFx = Mathf.Clamp(rawImpact, minImpactSpeed, maxImpactSpeed);

        Vector3 normalForSeverity = contactNormalWS ?? (hitDirection.sqrMagnitude > 0.0001f
            ? -hitDirection.normalized
            : Vector3.up);

        float impactForLegacy = Mathf.Clamp(rawImpact, minImpactSpeed, maxImpactSpeed);
        float legacyFromSpeed = Mathf.InverseLerp(minImpactSpeed, maxImpactSpeed, impactForLegacy);

        float sev01 = _crashSeverityConfig != null && otherRoot != null
            ? ResolveCrashSeverity(rawImpact, otherRoot, otherRb, normalForSeverity, legacyFromSpeed, extraSeverityMultiplier, null)
            : Mathf.Clamp01(severityFallback01);

        // Camera shake / slow-mo / coin penalties, etc.
        var gm = GameManager_Racing.Instance;
        if (gm != null && damageWindowOpen)
        {
            gm.OnCarCrash(clampedForFx, sev01);
        }

        // 🔊 NEW: play default crash SFX + impact VFX for external hits too
        if (damageWindowOpen)
        {
            // Use default crash clip for projectiles / generic hits
            PlayCrashSfx(crashClipDefault, contactPointWS, crashSfxVolume);

            Vector3 normal = normalForSeverity.sqrMagnitude > 1e-6f ? normalForSeverity.normalized : Vector3.up;
            SpawnCrashImpactVFX(contactPointWS, normal);
        }

        // Duration and impulse magnitudes consistent with normal collisions
        float crashDuration = Mathf.Lerp(minCrashDuration, maxCrashDuration, sev01);
        float impulseMag = clampedForFx * impulsePerUnitSpeed;
        float torqueMag = clampedForFx * torquePerUnitSpeed;

        // Let the central crash handler do physics + HP/Fuel + cooldown, etc.
        TriggerCrash(hitDirection, crashDuration, impulseMag, torqueMag, sev01, contactPointWS, damageWindowOpen);
    }

    // True if we're meaningfully rolled/pitched (even slightly)
    private bool NeedsUprightFlatten()
    {
        // dot=1 perfectly upright, lower = tilted/flipped
        float dot = Vector3.Dot(transform.up, Vector3.up);

        // This threshold is intentionally strict: even "a bit tilted" should reorient.
        // 0.999 ~ about 2.6 degrees off upright.
        if (dot < 0.999f) return true;

        // Extra safety: if you ever get tiny but visible roll/pitch, catch it anyway.
        Vector3 e = transform.eulerAngles;
        float pitch = Mathf.DeltaAngle(0f, e.x);
        float roll = Mathf.DeltaAngle(0f, e.z);

        return Mathf.Abs(pitch) > 1.0f || Mathf.Abs(roll) > 1.0f;
    }

    private void ChooseMashFaceButton()
    {
        if (!randomizeMashFaceButton)
        {
            _mashFaceButtonIndex = Mathf.Clamp(_mashFaceButtonIndex, 0, 3);
            _requiredMashButton = (FaceButton)_mashFaceButtonIndex;
            return;
        }

        int r = UnityEngine.Random.Range(0, 4);

        // SINGLE SOURCE OF TRUTH for the UI
        _mashFaceButtonIndex = r;

        // Keep the "required" system in lockstep so input + UI can never disagree
        _requiredMashButton = (FaceButton)r;
    }


    // === BOOST PAD SCREEN FLASH ===
    private void CheckBoostFlash()
    {
        if (baseMaxSpeed <= 0f) return;

        float speedMultiplier = currentMaxSpeed / baseMaxSpeed;
        bool isOnBoost = speedMultiplier >= boostFlashSpeedThreshold;

        // Flash when ENTERING boost (not every frame)
        if (isOnBoost && !_wasOnBoost)
        {
            if (Time.time - _lastBoostFlashTime >= boostFlashCooldown)
            {
                ScreenFlashManager.Boost();
                _lastBoostFlashTime = Time.time;
            }
        }

        _wasOnBoost = isOnBoost;
    }


    public void ApplyTemporaryHandlingBoost(float multiplier, float duration)
    {
        if (multiplier <= 1f || duration <= 0f) return;
        _tempHandlingMultiplier = Mathf.Max(1f, multiplier);
        _tempHandlingExpireAt = Time.time + Mathf.Max(0f, duration);
    }

    private float GetTemporaryHandlingMultiplier()
    {
        return Time.time < _tempHandlingExpireAt ? _tempHandlingMultiplier : 1f;
    }

    private bool NeedsFlipRecovery()
    {
        float upDot = Vector3.Dot(transform.up, Vector3.up);
        if (upDot > flipDotThreshold) return false;

        float angle = Vector3.Angle(transform.up, Vector3.up);
        return angle >= flipAngleThreshold;
    }

    public void RegisterFlipMashClick()
    {
        if (!_flipMashActive) return;
        if (!IsFlipMashUiVisible) return;
        if (_lastRegisteredMashFrame == Time.frameCount) return; // prevent duplicate same-frame clicks from multiple input paths

        // Can't recover if dead from fuel OR HP
        if (IsDeadForMashRecovery)
        {
            _flipMashActive = false;
            return;
        }
        _lastRegisteredMashFrame = Time.frameCount;

        _totalMashClicksThisSession += Mathf.Max(1, effectiveClicksPerClick);

        // Calculate mash speed (time since last click)
        float timeSinceLastMash = Time.time - _lastMashTime;
        _lastMashTime = Time.time;

        ScreenFlashManager.Mash();

        var cameraFollow = Camera.main.GetComponent<CameraFollow>();

        // Screen shake for mash
        if (enableMashScreenShake && cameraFollow != null)
        {
            cameraFollow.StartShake(mashShakeDuration, mashShakeStrength, mashShakeVibrato, mashShakeRandomness);
        }

        // Convert to 0-1 speed rating (faster = higher)
        _currentMashSpeed = CalculateMashSpeedRating(timeSinceLastMash);
        _mashSpeedSmoothed = Mathf.Lerp(_mashSpeedSmoothed, _currentMashSpeed, 0.3f);

        // Apply rewards
        ApplyMashRewards(_currentMashSpeed);

        if (enableMashProgressGauge)
        {
            UpdateMashGauge(_currentMashSpeed);
        }

        _flipMashClicks += effectiveClicksPerClick;

        // Show per-mash "damage" (click strength) contribution to crash recovery progress.
        if (effectiveClicksPerClick > 0)
            TrySpawnPopupRandomScreen(RacingPopupType.MashClickDamage, effectiveClicksPerClick);

        if (_flipMashClicks >= _flipMashClicksNeeded)
            EndFlipMashRecoveryAndUpright();
    }

    /// <summary>
    /// Get the position above the car for popup spawning.
    /// </summary>
    private Vector3 GetPopupPosition()
    {
        return transform.position + Vector3.up * popupVerticalOffset;
    }

    /// <summary>
    /// Spawn a popup if the system is ready and enabled.
    /// </summary>
    private void TrySpawnPopup(RacingPopupType type, float value)
    {
        if (!enablePopupText) return;
        if (!RacingPopups.IsReady) return;
        RacingPopups.Spawn(type, value, GetPopupPosition());
    }

    private void TrySpawnPopupRandomScreen(RacingPopupType type, float value)
    {
        if (!enablePopupText) return;
        if (!RacingPopups.IsReady) return;

        RacingPopups.SpawnRandomScreen(type, value, mashFuelPopupHorizontalRange, mashFuelPopupVerticalRange);
    }

    private float CalculateMashSpeedRating(float timeBetweenClicks)
    {
        // Clamp between thresholds
        if (timeBetweenClicks <= mashMaxSpeedThreshold)
            return 1f; // Max speed
        if (timeBetweenClicks >= mashMinSpeedThreshold)
            return 0f; // No bonus

        // Inverse lerp: faster clicks = higher rating
        return 1f - Mathf.InverseLerp(mashMaxSpeedThreshold, mashMinSpeedThreshold, timeBetweenClicks);
    }

    private void ApplyMashRewards(float speedRating)
    {
        // Calculate tiered gauge multiplier
        float gaugeMultiplier = CalculateGaugeMultiplier(_mashGaugeValue);

        // === FUEL REWARD ===
        float speedMultiplier = Mathf.Lerp(1f, mashFuelSpeedBonusMax, speedRating);
        float fuelReward = effectiveFuelPerClick * speedMultiplier * gaugeMultiplier;

        if (fuelReward > 0f && maxFuel > 0f)
        {
            float before = currentFuel;
            currentFuel = Mathf.Min(currentFuel + fuelReward, maxFuel);
            float actual = currentFuel - before;

            if (actual > 0f)
            {
                _totalFuelGainedThisSession += actual;

                // Show fuel gain popup
                TrySpawnPopupRandomScreen(RacingPopupType.MashFuelReward, actual);
            }
        }

        Debug.Log($"[Flip Mash] Speed: {speedRating:F2}, Gauge: {_mashGaugeValue:F2}, Tier Multiplier: {gaugeMultiplier:F2}, Fuel: {fuelReward:F2}");
    }

    private float CalculateGaugeMultiplier(float gaugeValue)
    {
        if (gaugeValue >= gaugeMaxThreshold)
        {
            // At or above max threshold - full max multiplier
            return gaugeMultiplierAtMax;
        }
        else if (gaugeValue >= gaugeGoodThreshold)
        {
            // Between good and max - lerp between good and max multipliers
            float t = Mathf.InverseLerp(gaugeGoodThreshold, gaugeMaxThreshold, gaugeValue);
            return Mathf.Lerp(gaugeMultiplierAtGood, gaugeMultiplierAtMax, t);
        }
        else
        {
            // Below good threshold - lerp between zero and good multipliers
            float t = Mathf.InverseLerp(0f, gaugeGoodThreshold, gaugeValue);
            return Mathf.Lerp(gaugeMultiplierAtZero, gaugeMultiplierAtGood, t);
        }
    }

    /// <summary>
    /// Sprocket payout only: no bonus below good threshold (1×), ramps to <see cref="gaugeMultiplierAtGood"/>
    /// up to max threshold; at/above max the curve is 1× so <see cref="sprocketGaugeBonusMax"/> alone covers max tier.
    /// </summary>
    private float CalculateSprocketGaugeMultiplier(float peakGauge)
    {
        if (peakGauge < gaugeGoodThreshold)
            return 1f;
        if (peakGauge >= gaugeMaxThreshold)
            return 1f;

        float span = gaugeMaxThreshold - gaugeGoodThreshold;
        if (span < 1e-6f)
            return gaugeMultiplierAtGood;

        float t = Mathf.InverseLerp(gaugeGoodThreshold, gaugeMaxThreshold, peakGauge);
        return Mathf.Lerp(1f, gaugeMultiplierAtGood, t);
    }

    // ============================================
    // FUTURE EXPANSION STUBS (implement when ready)
    // ============================================

    /*
    private void ApplyMashBoostReward(float speedRating)
    {
        // Example: Grant temporary speed boost based on mash speed
        // float boostAmount = mashBaseBoost * Mathf.Lerp(1f, mashBoostSpeedBonusMax, speedRating);
        // AddTemporarySpeedBoost(boostAmount, mashBoostDuration);
    }
    
    private void ApplyMashCoinReward(float speedRating)
    {
        // Example: Chance to spawn coins based on mash speed
        // float coinChance = mashBaseCoinChance * Mathf.Lerp(1f, mashCoinSpeedBonusMax, speedRating);
        // if (Random.value < coinChance) AwardCoins(mashCoinsPerProc);
    }
    
    */

    public bool IsCloseCallInvincible => _closeCallInvincible && Time.time < _closeCallInvincibilityEndTime;

    /// <summary>
    /// The total duration of the current/last invincibility window.
    /// </summary>
    public float CloseCallInvincibilityDuration => _closeCallInvincibilityDuration;

    /// <summary>
    /// Time remaining on close call invincibility (0 if not active).
    /// </summary>
    public float CloseCallInvincibilityRemaining => IsCloseCallInvincible
        ? Mathf.Max(0f, _closeCallInvincibilityEndTime - Time.time)
        : 0f;

    /// <summary>
    /// Apply close call rewards (called by GameManager when close call occurs).
    /// </summary>
    public void OnCloseCall(Vector3 obstaclePosition, float distance)
    {
        var mgr = RacingSkillTreeManager.Instance;
        if (mgr == null) return;

        // === RESET BOOST COOLDOWNS (repurposed close-call unlock) ===
        if (mgr.IsCloseCallResetBoostCooldownsUnlocked)
            ResetBoostCooldownsNow();

        // === RESET ITEM/QUEST COOLDOWNS (new unlock) ===
        if (mgr.IsCloseCallResetItemCooldownsUnlocked)
            ResetEquippedItemCooldownsNow();

        // === INVINCIBILITY ===
        float invincDuration = mgr.GetCloseCallInvincibilityDuration();
        if (invincDuration > 0f)
        {
            ApplyCloseCallInvincibility(invincDuration);
        }
    }

    private void ResetBoostCooldownsNow()
    {
        _boostCooldownTimer = 0f;
        _driftBoostCooldownTimer = 0f;
    }

    private void ResetEquippedItemCooldownsNow()
    {
        var questMgr = RacingQuestUnlockManager.Instance;

        try
        {
            // Forcefield: if equipped and currently cooling down, arm immediately.
            if (_forcefield == null)
                _forcefield = GetComponent<CarForcefield>();

            bool forcefieldEquipped = questMgr == null || questMgr.IsItemEquipped(RacingQuestRunItem.Forcefield);
            if (_forcefield != null && forcefieldEquipped)
            {
                if (!_forcefield.enabled)
                    _forcefield.enabled = true;
                _forcefield.ArmNow();
            }
        }
        catch { /* swallow */ }

        try
        {
            // Turret: reset internal fire cooldown timer.
            bool turretEquipped = questMgr == null || questMgr.IsItemEquipped(RacingQuestRunItem.Turret);
            if (turretEquipped)
            {
                var turret = GetComponentInChildren<CarTurretController>(true);
                if (turret != null)
                    turret.ResetCooldownNow();
            }
        }
        catch { /* swallow */ }

        try
        {
            // Coin friend lives outside the car in most setups.
            bool coinFriendEquipped = questMgr == null || questMgr.IsItemEquipped(RacingQuestRunItem.CoinFriend);
            if (coinFriendEquipped)
            {
                var friend = FindObjectOfType<CoinCollectingFriend>(true);
                if (friend != null)
                    friend.ResetCooldownNow();
            }
        }
        catch { /* swallow */ }
    }

    /// <summary>
    /// Apply a short speed boost from close call.
    /// Uses the centralized speed boost system for natural ramp-down.
    /// </summary>
    private void ApplyCloseCallSpeedBoost(float duration)
    {
        // Use centralized speed boost system with natural ramp-down
        AddSpeedBoost(
            BOOST_ID_CLOSE_CALL,
            closeCallBoostMaxSpeedMult,
            duration,
            closeCallBoostRampDownFraction,
            isMultiplier: true
        );

        // Keep legacy flag for backwards compatibility with other systems checking it
        _closeCallBoosting = true;
        _closeCallBoostEndTime = Time.time + duration;

        // Apply initial impulse force (optional - gives immediate "punch")
        if (rb != null && closeCallBoostForce > 0f)
        {
            Vector3 forwardDir = transform.forward;
            forwardDir.y = 0f;
            forwardDir.Normalize();
            rb.AddForce(forwardDir * closeCallBoostForce, closeCallBoostForceMode);
        }

        Debug.Log($"[CloseCall] Speed boost started for {duration:F2}s, max speed mult: {closeCallBoostMaxSpeedMult}x (with ramp-down at {closeCallBoostRampDownFraction:P0})");
    }

    /// <summary>
    /// Apply invincibility from close call.
    /// </summary>
    private void ApplyCloseCallInvincibility(float duration)
    {
        _closeCallInvincible = true;
        ApplyCloseCallTint();
        _closeCallInvincibilityDuration = duration;
        _closeCallInvincibilityEndTime = Time.time + duration;

        // Start continuous screen flash for invincibility duration
        ScreenFlashManager.Invincibility(duration);

        // Show "INVINCIBLE!" popup at car position
        if (RacingPopups.IsReady)
        {
            Vector3 popupPos = transform.position + Vector3.up * 2.5f;
            RacingPopups.Invincible(popupPos);
        }

        Debug.Log($"[CloseCall] Invincibility applied for {duration:F2}s");
    }

    private void EndFlipMashRecoveryAndUpright()
    {
        if (IsDeadForMashRecovery) { _flipMashActive = false; return; }

        // Show summary popup for fuel gained this session
        if (_totalFuelGainedThisSession > 0.1f && RacingPopups.IsReady)
        {
            // Could show a "Total Fuel: +X" popup here if desired
        }

        // Award sprockets based on performance
        if (enableSprocketRewards)
        {
            AwardMashSprockets();
        }

        // REMOVED: The old gauge-based fuel curve reward
        // The fuel is now gained per-click with gauge multiplier instead

        RacingQuestUnlockManager.Instance?.RecordCrashMashCompletion(1);

        _flipMashActive = false;

        CancelAllBoostState(0.35f);   // small lockout prevents instant re-trigger

        // Also kill any leftover landing carry allowance so you can't "keep" boosted cap as excess speed
        _landingExcessSpeed = 0f;
        _landingBoostTimeLeft = 0f;
        _landingBoostTargetMagnitude = 0f;

        // Force surface max speed to re-sample cleanly next tick (prevents stale smoothed value)
        _smoothedSurfaceMaxSpeed = -1f;

        // For a short period after recovery, ignore boost surface so we resume at run-start max speed/accel, not boosted.
        _postMashRecoveryIgnoreBoostUntil = Time.time + 0.5f;

        // Clear boost surface state so we don't use ramp/boost pad stats until the ignore window ends
        _onBoostSurface = false;
        _currentBoostAccel = 0f;
        _currentBoostMaxSpeed = 0f;

        // Re-evaluate currentMaxSpeed -> effectiveMaxSpeed right now (will use default surface due to ignore window)
        SampleGroundAndUpdateMultipliers();
        ApplySkillEffects();

        // Only do upright reorientation if we were actually flipped
        if (_isFlippedDuringRecovery)
        {
            _isReorienting = true;
            _reorientElapsed = 0f;
            _reorientStartRot = transform.rotation;

            Vector3 euler = transform.eulerAngles;
            _reorientTargetRot = Quaternion.Euler(0f, euler.y, 0f);

            if (rb != null)
                rb.position += Vector3.up * flipUprightLift;
        }
        _isFlippedDuringRecovery = false;
    }

    private void ApplyCloseCallTint()
    {
        foreach (var r in _renderers)
        {
            if (!_originalColors.TryGetValue(r, out var baseColor))
                continue;

            r.GetPropertyBlock(_mpb);

            Color tinted = Color.Lerp(baseColor, closeCallTintColor, closeCallTintStrength);
            _mpb.SetColor("_EmissionColor", closeCallTintColor * 2.5f);

            r.SetPropertyBlock(_mpb);
        }
    }

    private void ClearCloseCallTint()
    {
        foreach (var r in _renderers)
        {
            if (!_originalColors.TryGetValue(r, out var baseColor))
                continue;

            r.GetPropertyBlock(_mpb);
            _mpb.SetColor("_EmissionColor", closeCallTintColor * 2.5f);
            r.SetPropertyBlock(_mpb);
        }
    }


    private void ApplyDamageDegradationToPerformance()
    {
        if (!_statsInitialized) return;
        if (maxHP <= 0f) return;

        float hpFrac = HPPercent;
        float perfMul = 1f;
        if (hpFrac < degradeStartHPFraction)
        {
            float t = Mathf.Clamp01(hpFrac / Mathf.Max(0.0001f, degradeStartHPFraction));
            perfMul = Mathf.Lerp(performanceAtZeroHP, 1f, t);
        }

        effectiveAcceleration *= perfMul;
        effectiveMaxSpeed *= perfMul;
        effectiveMaxSpeed = Mathf.Max(effectiveMaxSpeed, minimumMaxSpeedFloor);
        effectiveAcceleration = Mathf.Max(effectiveAcceleration, minimumAccelerationFloor * 0.35f);

        // Slightly dull steering when hurt (optional subtlety on top of accel loss)
        float steerMul = Mathf.Lerp(0.82f, 1f, perfMul);
        effectiveTurnSpeed *= steerMul;
    }

    /// <summary>
    /// Tracks airborne-to-grounded transitions and preserves landing speed.
    /// Must be called every FixedUpdate when NOT in crash/flip state.
    /// </summary>
    private void UpdateLandingSpeedPreservation()
    {
        if (rb == null) return;

        bool groundedNow = CheckIfGrounded();
        float dt = Time.fixedDeltaTime;

        // Store takeoff speed and direction when we leave the ground (e.g. off a ramp)
        if (_wasGroundedLastFrame && !groundedNow)
        {
            Vector3 v = rb.velocity;
            Vector3 horiz = Vector3.ProjectOnPlane(v, Vector3.up);
            _takeoffHorizSpeed = horiz.magnitude;
            _takeoffHorizDir = _takeoffHorizSpeed > 0.1f ? horiz.normalized : transform.forward;
        }

        // Detect landing: was airborne last frame, grounded now
        if (!_wasGroundedLastFrame && groundedNow)
        {
            _lastLandedTime = Time.time;

            if (enableLandingBoost && _takeoffHorizSpeed > 0.1f)
            {
                _landingBoostTimeLeft = landingBoostDuration;
                _landingBoostDuration = landingBoostDuration;
                _landingBoostTargetMagnitude = _takeoffHorizSpeed * landingBoostStrength;

                // Immediate velocity injection: landing often kills velocity, so restore horizontal speed so the car keeps going fast
                float targetSpeed = _takeoffHorizSpeed * landingBoostStrength;
                Vector3 currentHoriz = Vector3.ProjectOnPlane(rb.velocity, Vector3.up);
                float currentSpeed = currentHoriz.magnitude;
                if (currentSpeed < targetSpeed - 0.1f)
                {
                    // Forward-only boost: never add left/right push. Preserve existing lateral, only raise forward component.
                    Vector3 flatForward = Vector3.ProjectOnPlane(transform.forward, Vector3.up);
                    if (flatForward.sqrMagnitude < 0.001f) flatForward = _takeoffHorizDir;
                    flatForward.Normalize();

                    float currentForwardSpeed = Vector3.Dot(currentHoriz, flatForward);
                    float newForwardSpeed = Mathf.Max(currentForwardSpeed, targetSpeed);
                    Vector3 lateral = currentHoriz - flatForward * currentForwardSpeed;
                    Vector3 newHoriz = flatForward * newForwardSpeed + lateral;
                    rb.velocity = new Vector3(newHoriz.x, rb.velocity.y, newHoriz.z);
                }
            }

            if (enableLandingCarrySpeed)
            {
                Vector3 v = rb.velocity;
                float horizSpeed = Vector3.ProjectOnPlane(v, Vector3.up).magnitude;

                // Calculate excess speed above current cap
                float capNoCarry = GetCurrentSpeedCap_NoLandingCarry();
                float excess = horizSpeed - capNoCarry;

                if (excess > 0f)
                {
                    // Keep the higher of current excess or new excess (in case of consecutive jumps)
                    _landingExcessSpeed = Mathf.Max(_landingExcessSpeed, excess);
                }
            }
        }

        // Decay landing boost timer
        if (_landingBoostTimeLeft > 0f)
        {
            _landingBoostTimeLeft -= dt;
            if (_landingBoostTimeLeft < 0f) _landingBoostTimeLeft = 0f;
        }

        // Bleed off excess allowance over time while grounded
        if (enableLandingCarrySpeed && _landingExcessSpeed > 0f && groundedNow)
        {
            _landingExcessSpeed = Mathf.MoveTowards(
                _landingExcessSpeed,
                0f,
                landingExcessBleedPerSecond * dt
            );
        }

        // Update grounded state for next frame (CRITICAL - this was missing in normal flow!)
        _wasGroundedLastFrame = groundedNow;
        _isGrounded = groundedNow;
    }

    private void SetLongitudinalVelocityClamped(Vector3 forwardDir, float newLong)
    {
        Vector3 v = rb.velocity;

        Vector3 fwd = forwardDir.normalized;
        Vector3 flat = Vector3.ProjectOnPlane(v, Vector3.up);

        // Keep lateral component on the ground plane
        Vector3 lateral = flat - fwd * Vector3.Dot(flat, fwd);

        Vector3 newFlat = fwd * newLong + lateral;
        rb.velocity = new Vector3(newFlat.x, v.y, newFlat.z);
    }

    private void UpdateBoostUnlock()
    {
        if (!requireBoostUnlock)
        {
            boostUnlocked = true;
            return;
        }
        var mgr = RacingSkillTreeManager.Instance;
        boostUnlocked = (mgr != null && mgr.GetLevel(SkillType.BoostUnlock) > 0);
    }

    private void UpdateDriftUnlock()
    {
        var mgr = RacingSkillTreeManager.Instance;
        if (!requireDriftUnlock)
        {
            driftUnlocked = true;
            return;
        }
        driftUnlocked = (mgr != null && mgr.GetLevel(SkillType.DriftUnlock) > 0);
    }

    private void UpdateMashGauge(float speedRating)
    {
        // Speed contribution scales from 1x at slow clicks to (bonus * multiplier) at fastest clicks.
        float speedFillMultiplier = Mathf.Lerp(1f, gaugeFillSpeedBonus * gaugeFillSpeedMultiplier, speedRating);
        float fillAmount = gaugeFillPerClick * speedFillMultiplier;

        // Fill can reach 100%; max fuel tier starts at gaugeMaxThreshold (e.g. 0.94).
        _mashGaugeValue = Mathf.Clamp01(_mashGaugeValue + fillAmount);

        // Track peak
        if (_mashGaugeValue > _mashGaugePeakValue)
            _mashGaugePeakValue = _mashGaugeValue;

        // Check for max gauge (using threshold, not 100%)
        if (_mashGaugeValue >= gaugeMaxThreshold && !_gaugeMaxedThisSession)
        {
            _gaugeMaxedThisSession = true;

            // Visual/audio feedback for reaching max tier
            ScreenFlashManager.Instance?.Flash(Color.cyan, 1.5f, 0.3f, 0.3f);
        }
    }

    /// <summary>
    /// Bump an obstacle up and away when hit during close call invincibility.
    /// </summary>
    private void BumpObstacleAway(Collision collision)
    {
        // Try to get rigidbody on the obstacle
        Rigidbody obstacleRb = collision.collider.attachedRigidbody;
        if (obstacleRb == null)
        {
            // No rigidbody - try to get one from parent
            obstacleRb = collision.collider.GetComponentInParent<Rigidbody>();
        }

        if (obstacleRb == null || obstacleRb.isKinematic)
        {
            // Can't bump static/kinematic objects, just ignore
            return;
        }

        // Calculate bump direction based on collision
        Vector3 bumpDir;
        Vector3 contactPoint = transform.position;

        if (collision.contactCount > 0)
        {
            var contact = collision.GetContact(0);
            contactPoint = contact.point;
            // Direction FROM car TO obstacle (push it away)
            bumpDir = (collision.transform.position - transform.position).normalized;
        }
        else
        {
            // Fallback: push in direction from car to obstacle
            bumpDir = (collision.transform.position - transform.position).normalized;
        }

        // Ensure lateral direction (zero out Y, then re-normalize)
        Vector3 lateralDir = new Vector3(bumpDir.x, 0f, bumpDir.z);
        if (lateralDir.sqrMagnitude > 0.001f)
        {
            lateralDir.Normalize();
        }
        else
        {
            // Fallback to car's forward if no clear lateral direction
            lateralDir = transform.forward;
        }

        // Calculate forces
        Vector3 awayForce = lateralDir * invincibilityBumpForceAway;
        Vector3 upForce = Vector3.up * invincibilityBumpForceUp;
        Vector3 totalForce = awayForce + upForce;

        // Apply impulse to obstacle
        obstacleRb.AddForce(totalForce, ForceMode.VelocityChange);

        // Add some spin for visual flair
        if (invincibilityBumpTorque > 0f)
        {
            Vector3 torqueAxis = Vector3.Cross(Vector3.up, lateralDir); // Perpendicular to push direction
            obstacleRb.AddTorque(torqueAxis * invincibilityBumpTorque, ForceMode.VelocityChange);
        }

        // Optional: Play a sound or VFX
        // PlayBumpSound(contactPoint);
        // SpawnBumpVFX(contactPoint);
    }

    private static Vector3 NormalizeContactNormal(Vector3 v)
    {
        return v.sqrMagnitude > 1e-8f ? v.normalized : Vector3.up;
    }

    /// <summary>
    /// Improves impact/closing speed for movers (shuttle/cross) and contact-relative motion.
    /// </summary>
    private float RefineImpactSpeed(Collision collision, Vector3 contactNormal, float impactSpeed)
    {
        if (collision != null && collision.contactCount > 0)
        {
            var c = collision.GetContact(0);
            Vector3 n = c.normal.sqrMagnitude > 1e-6f ? c.normal.normalized : Vector3.up;
            float along = -Vector3.Dot(collision.relativeVelocity, n);
            impactSpeed = Mathf.Max(impactSpeed, along);
            impactSpeed = Mathf.Max(impactSpeed, collision.relativeVelocity.magnitude * 0.35f);
        }

        Collider hitCol = collision.collider;
        var shuttle = hitCol.GetComponentInParent<ShuttleTrackObstacle>();
        if (shuttle != null && rb != null)
        {
            Vector3 rel = rb.velocity - shuttle.GetWorldVelocity();
            impactSpeed = Mathf.Max(impactSpeed, rel.magnitude);
            if (contactNormal.sqrMagnitude > 1e-6f)
            {
                Vector3 n = contactNormal.normalized;
                impactSpeed = Mathf.Max(impactSpeed, Mathf.Abs(Vector3.Dot(rel, n)));
            }
            return impactSpeed;
        }

        var cross = hitCol.GetComponentInParent<CrossTrackObstacle>();
        if (cross != null && rb != null)
        {
            Vector3 rel = rb.velocity - cross.GetWorldVelocity();
            impactSpeed = Mathf.Max(impactSpeed, rel.magnitude);
            if (contactNormal.sqrMagnitude > 1e-6f)
            {
                Vector3 n = contactNormal.normalized;
                impactSpeed = Mathf.Max(impactSpeed, Mathf.Abs(Vector3.Dot(rel, n)));
            }
        }

        var rollLog = hitCol.GetComponentInParent<RollingLogAlongTrack>();
        if (rollLog != null && rb != null)
        {
            Vector3 rel = rb.velocity - rollLog.GetWorldVelocity();
            impactSpeed = Mathf.Max(impactSpeed, rel.magnitude);
            if (contactNormal.sqrMagnitude > 1e-6f)
            {
                Vector3 n = contactNormal.normalized;
                impactSpeed = Mathf.Max(impactSpeed, Mathf.Abs(Vector3.Dot(rel, n)));
            }
        }

        return impactSpeed;
    }

    private float RefineImpactSpeedTrigger(Collider other, float impactSpeed)
    {
        var shuttle = other.GetComponentInParent<ShuttleTrackObstacle>();
        if (shuttle != null && rb != null)
        {
            Vector3 rel = rb.velocity - shuttle.GetWorldVelocity();
            return Mathf.Max(impactSpeed, rel.magnitude);
        }

        var rollLogT = other.GetComponentInParent<RollingLogAlongTrack>();
        if (rollLogT != null && rb != null)
        {
            Vector3 rel = rb.velocity - rollLogT.GetWorldVelocity();
            return Mathf.Max(impactSpeed, rel.magnitude);
        }

        var cross = other.GetComponentInParent<CrossTrackObstacle>();
        if (cross != null && rb != null)
        {
            Vector3 rel = rb.velocity - cross.GetWorldVelocity();
            return Mathf.Max(impactSpeed, rel.magnitude);
        }

        return impactSpeed;
    }


    /// <summary>
    /// Central 0–1 crash severity. Uses <see cref="CrashSeverityConfig"/> when assigned; otherwise legacy impact-speed lerp.
    /// </summary>
    private float ResolveCrashSeverity(
        float refinedClosingSpeed,
        Transform obstacleRoot,
        Rigidbody obstacleRb,
        Vector3 contactNormalWorld,
        float legacySeverity01,
        float extraSeverityMultiplier = 1f,
        Collision collision = null)
    {
        float legacy = Mathf.Clamp01(legacySeverity01);
        if (_crashSeverityConfig == null)
            return legacy;

        if (obstacleRoot == null && obstacleRb != null)
            obstacleRoot = obstacleRb.transform;
        if (obstacleRoot == null)
            return legacy;

        var inp = new CrashSeverityCalculator.Input
        {
            ObstacleRoot = obstacleRoot,
            ObstacleRigidbody = obstacleRb,
            CarRigidbody = rb,
            CarEffectiveMaxSpeed = EffectiveMaxSpeed,
            ContactNormalWorld = NormalizeContactNormal(contactNormalWorld),
            Collision = collision,
            ClosingSpeedOverride = Mathf.Max(0f, refinedClosingSpeed),
            ExtraSeverityMultiplier = extraSeverityMultiplier
        };

        return CrashSeverityCalculator.Compute(_crashSeverityConfig, inp, legacy, out _);
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (rb == null)
            return;

        // NEW: if we're already crashing or reorienting, ignore new crash logic entirely.
        if (IsCrashInvulnerable)
            return;

        if (_isReorienting)
            return;

        // TrackObstacleBounceBack already runs ApplyExternalCrashDamage from its own OnCollisionEnter.
        // Running the full car crash pipeline here too applies the same impact twice (double TriggerCrash / FX / severity).
        if (collision.collider.GetComponentInParent<TrackObstacleBounceBack>() != null)
            return;

        // === NEW: FORCEFIELD PROTECTION CHECK ===
        // If the forcefield is armed and this collider is on obstacle layers,
        // the forcefield should handle it - don't process as crash
        if (_forcefield != null && _forcefield.IsArmed)
        {
            if (((1 << collision.gameObject.layer) & crashLayers) != 0)
            {
                // Let forcefield handle this - it will intercept via its trigger
                // This prevents the collision from also triggering crash logic
                Debug.Log($"[CarController] Collision ignored - forcefield is armed and will handle {collision.gameObject.name}");
                return;
            }
        }

        // === NEW: PER-COLLIDER CRASH COOLDOWN ===
        int colliderId = collision.collider.GetInstanceID();
        if (_perColliderCrashTime.TryGetValue(colliderId, out float lastCrashTime))
        {
            if (Time.time - lastCrashTime < perColliderCrashCooldown)
            {
                Debug.Log($"[CarController] Collision ignored - same collider cooldown ({collision.collider.name})");
                return;
            }
        }

        bool duringMashRecovery = _flipMashActive;
        bool duringCrashState = _inCrash;

        bool hitNpcTraffic = collision.collider.GetComponentInParent<NPCTrafficCar>() != null;

        float impactSpeed = collision.relativeVelocity.magnitude;

        Vector3 contactNormalEarly = Vector3.up;
        if (collision.contactCount > 0)
            contactNormalEarly = collision.GetContact(0).normal;

        impactSpeed = RefineImpactSpeed(collision, contactNormalEarly, impactSpeed);

        // NPC traffic: always count as a real crash for the player (same-direction / rear clips used to fall under minImpactSpeed).
        if (hitNpcTraffic)
            impactSpeed = Mathf.Max(impactSpeed, minImpactSpeed);

        // Cross / shuttle movers: any contact should crash the car (slow coast-ins used to skip crash while the obstacle still converted / reacted).
        var hitCol = collision.collider;
        if (hitCol.GetComponentInParent<CrossTrackObstacle>() != null
            || hitCol.GetComponentInParent<ShuttleTrackObstacle>() != null)
        {
            impactSpeed = Mathf.Max(impactSpeed, minImpactSpeed);
        }

        if (impactSpeed < minImpactSpeed)
            return;

        if (((1 << collision.gameObject.layer) & crashLayers) == 0)
            return;

        if (hitNpcTraffic)
        {
            GameObject initiator = collision.contactCount > 0
                ? collision.GetContact(0).otherCollider.gameObject
                : collision.collider.gameObject;
            Debug.Log("Crash Initiator " + initiator.name + " via OnCollisionEnter");
        }

        if (IsCloseCallInvincible)
        {
            // Bump the obstacle away
            BumpObstacleAway(collision);
            ScreenFlashManager.InvincibilityImpact();
            var camController = Camera.main?.GetComponent<CameraFollow>();
            camController?.StartShake(0.15f, 0.15f, 3, 0.15f);

            if (RacingPopups.IsReady)
            {
                Vector3 impactPos = collision.contactCount > 0
                    ? collision.GetContact(0).point + Vector3.up * 1.5f
                    : transform.position + Vector3.up * 2f;
                RacingPopups.Crash(impactPos);
            }

            Debug.Log("[CloseCall] Invincibility blocked crash - bumped obstacle away!");
            return;
        }

        // --- skip crash logic if obstacle has active forcefield immunity ---
        var immunity = collision.collider.GetComponentInParent<LaunchImmunityMarker>();
        if (immunity != null && immunity.IsImmune) return;

        bool damageWindowOpen = Time.time >= _nextCrashAllowedTime;

        // If we're in mash recovery and damage window is open, add debt instead of restarting
        bool wasMashing = _flipMashActive;

        if (_inCrash && !wasMashing)
            return;

        Vector3 hitDir;
        Vector3 contactPoint = transform.position;
        Vector3 contactNormal = Vector3.up;

        if (collision.contactCount > 0)
        {
            var c = collision.GetContact(0);
            hitDir = c.normal;
            contactPoint = c.point;
            contactNormal = c.normal;
        }
        else
        {
            hitDir = (transform.position - collision.transform.position).normalized;
        }

        Rigidbody otherRb = collision.collider.attachedRigidbody;
        // Static props: use the collider's transform (or RB transform), not .root — root is often the whole track chunk
        // and lossy scale / CrashObstacleIdentity on the rock are wrong, which drove severity to the minimum.
        Transform obstacleRootForSeverity = otherRb != null ? otherRb.transform : collision.collider.transform;
        float impactForLegacy = Mathf.Clamp(impactSpeed, minImpactSpeed, maxImpactSpeed);
        float legacySeverity = Mathf.InverseLerp(minImpactSpeed, maxImpactSpeed, impactForLegacy);
        float severity = ResolveCrashSeverity(impactSpeed, obstacleRootForSeverity, otherRb, contactNormal, legacySeverity, 1f, collision);

        ScreenFlashManager.Damage();

        var gm = GameManager_Racing.Instance;
        if (gm != null && damageWindowOpen)
            gm.OnCarCrash(impactSpeed, severity);

        float crashDuration = Mathf.Lerp(minCrashDuration, maxCrashDuration, severity);
        float impulseMag = impactSpeed * impulsePerUnitSpeed;
        float torqueMag = impactSpeed * torquePerUnitSpeed;

        var otherCol = hitCol;
        var cross = otherCol.GetComponentInParent<CrossTrackObstacle>();
        if (cross != null)
        {
            PlayCrashSfx(crashClipHonk, contactPoint, crashSfxVolume);
        }
        else
        {
            PlayCrashSfx(crashClipDefault, contactPoint, crashSfxVolume);
        }

        // === Track per-root AND per-collider ===
        int rootId = collision.collider.transform.root.GetInstanceID();
        _recentCrashRootTime[rootId] = Time.time;
        _perColliderCrashTime[colliderId] = Time.time;  // NEW: per-collider tracking
        _closeCallTracking.Remove(rootId);

        if (damageWindowOpen)
        {
            SpawnCrashImpactVFX(contactPoint, contactNormal);
        }

        // If we were mashing, add debt and apply damage but DON'T restart crash state
        if (wasMashing && damageWindowOpen)
        {
            // Store new severity for drain calculations
            _lastCrashSeverity = Mathf.Max(_lastCrashSeverity, severity);
            _crashCount++;
            _crashSeveritySum += severity;

            ApplyCrashHpAndFuelFromSeverity(severity);

            // Add mash debt
            AddMashDebtFromNewCrash(NeedsFlipRecovery());

            // Show crash popup
            if (RacingPopups.IsReady)
                RacingPopups.Crash(_lastCrashSeverity, GetPopupPosition());

            // Reset cooldown
            _nextCrashAllowedTime = Time.time + crashDamageCooldown;

            // Apply some knockback without full crash state
            if (rb != null)
            {
                Vector3 knockDir = hitDir;
                knockDir.y += 0.2f;
                knockDir.Normalize();
                rb.AddForce(knockDir * impulseMag * 0.5f, ForceMode.VelocityChange);
            }

            return; // Don't trigger full crash state
        }

        TriggerCrash(hitDir, crashDuration, impulseMag, torqueMag, severity, contactPoint, damageWindowOpen);
    }

    private void ForceStopCloseCallEffects()
    {
        _closeCallBoosting = false;
        _closeCallInvincible = false;

        // Remove close call boost from centralized system
        RemoveSpeedBoost(BOOST_ID_CLOSE_CALL);

        ScreenFlashManager.StopInvincibility();
    }

    /// <summary>
    /// Get the current health drain rate based on last crash severity and crash count.
    /// </summary>
    public float CurrentMashHealthDrainRate
    {
        get
        {
            if (!_flipMashActive) return 0f;
            float baseDrain = Mathf.Lerp(mashHealthDrainAtMinSeverity, mashHealthDrainAtMaxSeverity, _lastCrashSeverity);
            float crashBonus = _crashCount * mashHealthDrainPerCrash;
            return Mathf.Min(baseDrain + crashBonus, mashHealthDrainCap);
        }
    }

    /// <summary>
    /// Get the current fuel drain rate based on last crash severity and crash count.
    /// </summary>
    public float CurrentMashFuelDrainRate
    {
        get
        {
            if (!_flipMashActive) return 0f;
            float baseDrain = Mathf.Lerp(mashFuelDrainAtMinSeverity, mashFuelDrainAtMaxSeverity, _lastCrashSeverity);
            float crashBonus = _crashCount * mashFuelDrainPerCrash;
            return Mathf.Min(baseDrain + crashBonus, mashFuelDrainCap);
        }
    }

    /// <summary>
    /// Get the current gauge drain rate based on last crash severity and crash count.
    /// </summary>
    public float CurrentGaugeDrainRate
    {
        get
        {
            if (!_flipMashActive) return 0f;
            float baseDrain = Mathf.Lerp(gaugeDrainAtMinSeverity, gaugeDrainAtMaxSeverity, _lastCrashSeverity);
            float crashBonus = _crashCount * gaugeDrainPerCrash;
            float drain = Mathf.Min(baseDrain + crashBonus, gaugeDrainCap);
            return drain * _skillDrainMultiplier;  // Include skill scaling
        }
    }

    // Add helper inside the class
    private void PlayCrashSfx(AudioClip clip, Vector3 worldPos, float volume = 1f)
    {
        if (clip == null) return;

        GameObject go = new GameObject("SFX_Crash_" + clip.name);
        go.transform.position = worldPos;

        var src = go.AddComponent<AudioSource>();
        src.clip = clip;
        src.playOnAwake = false;
        src.loop = false;
        src.dopplerLevel = 0f;

        // Spatial settings
        src.spatialBlend = crashUseSpatial ? Mathf.Clamp01(crashSpatialBlend) : 0f;
        src.rolloffMode = crashRolloff;
        src.minDistance = Mathf.Max(0.01f, crashMinDistance);
        src.maxDistance = Mathf.Max(src.minDistance + 0.1f, crashMaxDistance);

        // Apply volume + multiplier and clamp
        src.volume = Mathf.Clamp01(volume * crashVolumeMultiplier);

        // Randomize pitch for variety
        float pitch = UnityEngine.Random.Range(crashPitchMin, crashPitchMax);
        src.pitch = Mathf.Clamp(pitch, 0.01f, 3f);

        src.Play();
        Destroy(go, clip.length / Mathf.Max(0.01f, src.pitch));
    }

    private void PlayDeathExplodeSfx(Vector3 worldPos, float volume01 = 1f, bool respectCooldown = true)
    {
        if (deathExplodeClip == null) return;

        if (respectCooldown)
        {
            if (Time.time < _lastDeathExplodeSfxTime + Mathf.Max(0f, deathExplodeSfxCooldown))
                return;
        }
        _lastDeathExplodeSfxTime = Time.time;

        GameObject go = new GameObject("SFX_Death_" + deathExplodeClip.name);
        go.transform.position = worldPos;

        var src = go.AddComponent<AudioSource>();
        src.clip = deathExplodeClip;
        src.playOnAwake = false;
        src.loop = false;
        src.dopplerLevel = 0f;

        // Spatial settings
        src.spatialBlend = deathExplodeUseSpatial ? Mathf.Clamp01(deathExplodeSpatialBlend) : 0f;
        src.rolloffMode = deathExplodeRolloff;
        src.minDistance = Mathf.Max(0.01f, deathExplodeMinDistance);
        src.maxDistance = Mathf.Max(src.minDistance + 0.1f, deathExplodeMaxDistance);

        // Volume + multiplier
        src.volume = Mathf.Clamp01(volume01 * deathExplodeVolume * deathExplodeVolumeMultiplier);

        // Pitch variation
        float pitch = UnityEngine.Random.Range(deathExplodePitchMin, deathExplodePitchMax);
        src.pitch = Mathf.Clamp(pitch, 0.01f, 3f);

        src.Play();
        Destroy(go, deathExplodeClip.length / Mathf.Max(0.01f, src.pitch));
    }

    /// <summary>
    /// Applies crash-only total velocity caps. Intended for crash/recovery windows only.
    /// </summary>
    private void ApplyCrashVelocityCaps()
    {
        if (rb == null) return;

        float speed = rb.velocity.magnitude;

        if (maxCrashFlingSpeed > 0f && speed > maxCrashFlingSpeed)
        {
            rb.velocity = rb.velocity.normalized * maxCrashFlingSpeed;
            speed = maxCrashFlingSpeed;
        }

        if (maxTotalVelocityMagnitude > 0f && speed > maxTotalVelocityMagnitude)
            rb.velocity = rb.velocity * (maxTotalVelocityMagnitude / speed);
    }

    private void AwardMashSprockets()
    {
        var mgr = RacingSkillTreeManager.Instance;
        if (mgr == null) return;

        // Clicks × base%; good-band ramp (no sub-good bonus); max tier only via sprocketGaugeBonusMax.
        float baseReward = _totalMashClicksThisSession * sprocketBasePercent;
        float multiplier = CalculateSprocketGaugeMultiplier(_mashGaugePeakValue);
        if (_gaugeMaxedThisSession)
            multiplier *= sprocketGaugeBonusMax;
        int sprocketReward = Mathf.RoundToInt(baseReward * multiplier);

        // Clamp to min/max
        sprocketReward = Mathf.Clamp(sprocketReward, sprocketMinReward, sprocketMaxReward);

        // Track for summary
        _totalSprocketsThisSession = sprocketReward;

        // Award sprockets
        if (sprocketReward > 0)
        {
            mgr.AddSprockets(sprocketReward);

            // Notify game manager for UI tracking
            GameManager_Racing.Instance?.RegisterSprocketGain(sprocketReward);

            // Show sprocket popup
            if (RacingPopups.IsReady)
            {
                RacingPopups.Spawn(RacingPopupType.SprocketGain, sprocketReward, GetPopupPosition());
            }

            // Screen flash for sprocket reward
            ScreenFlashManager.Sprocket(sprocketReward);

            Debug.Log($"[Mash Complete] Clicks: {_totalMashClicksThisSession}, Peak Gauge: {_mashGaugePeakValue:P0}, Maxed: {_gaugeMaxedThisSession}, Sprockets: {sprocketReward}");
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        // Ignore our own auxiliary trigger(s) (forcefield bubble etc.)
        if (other.isTrigger)
        {
            // If it’s the child forcefield trigger just bail.
            if (other.name == "ForcefieldTrigger" || other.GetComponentInParent<CarForcefield>() != null)
                return;
        }

        // NEW: if we're already crashing or reorienting, ignore new crash logic entirely.
        if (IsCrashInvulnerable)
            return;

        var immunity = other.GetComponentInParent<LaunchImmunityMarker>();
        if (immunity != null && immunity.IsImmune) return;

        if (((1 << other.gameObject.layer) & crashLayers) == 0)
            return;

        bool hitNpcTrafficTrigger = other.GetComponentInParent<NPCTrafficCar>() != null;
        if (IsCloseCallInvincible && hitNpcTrafficTrigger)
            return;
        if (hitNpcTrafficTrigger)
            Debug.Log("Crash Initiator " + other.gameObject.name + " via OnTriggerEnter");

        float impactSpeed = 0f;
        Rigidbody otherRb = other.attachedRigidbody;
        if (otherRb != null)
            impactSpeed = (rb.velocity - otherRb.velocity).magnitude;
        else
            impactSpeed = rb.velocity.magnitude;

        impactSpeed = RefineImpactSpeedTrigger(other, impactSpeed);

        if (hitNpcTrafficTrigger)
            impactSpeed = Mathf.Max(impactSpeed, minImpactSpeed);

        if (other.GetComponentInParent<CrossTrackObstacle>() != null
            || other.GetComponentInParent<ShuttleTrackObstacle>() != null)
        {
            impactSpeed = Mathf.Max(impactSpeed, minImpactSpeed);
        }

        if (impactSpeed < minImpactSpeed)
            return;

        if (_forcefield != null && _forcefield.IsArmed)
        {
            if (((1 << other.gameObject.layer) & crashLayers) != 0)
            {
                Debug.Log($"[CarController] Trigger ignored - forcefield is armed and will handle {other.name}");
                return;
            }
        }

        // === NEW: PER-COLLIDER CRASH COOLDOWN ===
        int colliderId = other.GetInstanceID();
        if (_perColliderCrashTime.TryGetValue(colliderId, out float lastCrashTime))
        {
            if (Time.time - lastCrashTime < perColliderCrashCooldown)
            {
                Debug.Log($"[CarController] Trigger ignored - same collider cooldown ({other.name})");
                return;
            }
        }

        bool damageWindowOpen = Time.time >= _nextCrashAllowedTime;

        if (_inCrash && !_flipMashActive)
            return;

        Vector3 hitDir = transform.position - other.bounds.center;
        hitDir.y = 0f;
        hitDir.Normalize();

        Vector3 contactPoint = other.bounds.ClosestPoint(transform.position);

        Transform obstacleRootForSeverity = otherRb != null ? otherRb.transform : other.transform;
        Vector3 approxNormal = (transform.position - contactPoint).sqrMagnitude > 1e-6f
            ? (transform.position - contactPoint).normalized
            : Vector3.up;
        float impactForLegacy = Mathf.Clamp(impactSpeed, minImpactSpeed, maxImpactSpeed);
        float legacySeverity = Mathf.InverseLerp(minImpactSpeed, maxImpactSpeed, impactForLegacy);
        float severity = ResolveCrashSeverity(impactSpeed, obstacleRootForSeverity, otherRb, approxNormal, legacySeverity, 1f, null);

        var gm = GameManager_Racing.Instance;
        if (gm != null && damageWindowOpen)
            gm.OnCarCrash(impactSpeed, severity);

        float crashDuration = Mathf.Lerp(minCrashDuration, maxCrashDuration, severity);
        float impulseMag = impactSpeed * impulsePerUnitSpeed;
        float torqueMag = impactSpeed * torquePerUnitSpeed;

        var crossTrigger = other.GetComponentInParent<CrossTrackObstacle>();
        if (crossTrigger != null)
            PlayCrashSfx(crashClipHonk, contactPoint, crashSfxVolume);
        else
            PlayCrashSfx(crashClipDefault, contactPoint, crashSfxVolume);

        int rootId = other.transform.root.GetInstanceID();
        _recentCrashRootTime[rootId] = Time.time;
        _perColliderCrashTime[colliderId] = Time.time;
        _closeCallTracking.Remove(rootId);

        // Spawn crash/explode VFX at the contact point (only when damage window open)
        if (damageWindowOpen)
        {
            // For triggers we don't have a contact normal - use up as a reasonable default
            SpawnCrashImpactVFX(contactPoint, Vector3.up);
        }

        bool wasMashingTrigger = _flipMashActive;
        if (wasMashingTrigger && damageWindowOpen)
        {
            _lastCrashSeverity = Mathf.Max(_lastCrashSeverity, severity);
            _crashCount++;
            _crashSeveritySum += severity;

            ApplyCrashHpAndFuelFromSeverity(severity);

            AddMashDebtFromNewCrash(NeedsFlipRecovery());

            if (RacingPopups.IsReady)
                RacingPopups.Crash(_lastCrashSeverity, GetPopupPosition());

            _nextCrashAllowedTime = Time.time + crashDamageCooldown;

            NotifyCrashFeedbackOnly(severity);

            if (rb != null)
            {
                Vector3 knockDir = hitDir;
                knockDir.y += 0.2f;
                knockDir.Normalize();
                rb.AddForce(knockDir * impulseMag * 0.5f, ForceMode.VelocityChange);
            }

            return;
        }

        TriggerCrash(hitDir, crashDuration, impulseMag, torqueMag, severity, contactPoint, damageWindowOpen);
    }

    private void UpdateCrashReorientation()
    {
        var gm = GameManager_Racing.Instance;

        if (!_isReorienting) return;

        // Never fight flip-mash mode.
        if (_flipMashActive) return;


        if (IsDeadForMashRecovery || (gm != null && gm.RunEnded))
        {
            _isReorienting = false;
            _flipMashActive = false;
            return;
        }

        _reorientElapsed += Time.deltaTime;
        float t = Mathf.Clamp01(_reorientElapsed / reorientDuration);

        // Smoothly slerp while reorienting
        transform.rotation = Quaternion.Slerp(_reorientStartRot, _reorientTargetRot, t);

        if (t >= 1f)
        {
            // Ensure exact, no residual tilt on X/Z — snap final rotation to zero X/Z
            Vector3 finalEuler = transform.eulerAngles;
            transform.rotation = Quaternion.Euler(0f, finalEuler.y, 0f);

            // Also enforce on Rigidbody to avoid physics re-introducing tilt
            if (rb != null)
            {
                rb.angularVelocity = Vector3.zero;
                rb.rotation = transform.rotation;
                rb.drag = _baseDrag;
                rb.angularDrag = _baseAngularDrag;
            }

            _isReorienting = false;
        }
    }

    // Helper: spawn impact VFX at world position with optional orientation (normal)
    private void SpawnCrashImpactVFX(Vector3 worldPos, Vector3 normal)
    {
        if (crashImpactVFX == null) return;

        // Try ProjectilePool if it exists
        try
        {
            if (ProjectilePool.Instance != null)
            {
                GameObject inst = ProjectilePool.Instance.Get(crashImpactVFX);
                if (inst != null)
                {
                    inst.transform.position = worldPos;
                    inst.transform.rotation = Quaternion.LookRotation(normal, Vector3.up);
                    inst.SetActive(true);
                    // schedule return to pool
                    StartCoroutine(ReturnPooledVFXLater(crashImpactVFX, inst, Mathf.Max(0.01f, crashImpactVFXLifetime)));
                    return;
                }
            }
        }
        catch { /* ignore pool errors, fallback to Instantiate */ }

        // Fallback: instantiate and destroy after lifetime
        var go = Instantiate(crashImpactVFX, worldPos, Quaternion.LookRotation(normal, Vector3.up));
        Destroy(go, Mathf.Max(0.01f, crashImpactVFXLifetime));
    }

    private void UpdateSteeringInputFixed()
    {
        float dt = Time.fixedDeltaTime;

        float targetSteer = _suppressSteeringThisFrame ? 0f : _rawSteer;

        float smoothRate = steeringInputSmooth;
        bool returningToCenter = Mathf.Abs(targetSteer) < 0.01f;
        if (returningToCenter && steeringReturnSmooth > 0f)
            smoothRate = steeringReturnSmooth;
        if (isDrifting)
            smoothRate *= driftSteeringInputBuildupMultiplier;

        float handlingMult = GetTemporaryHandlingMultiplier();
        if (handlingMult > 1f)
            smoothRate /= handlingMult;

        float smoothDelta = smoothRate * dt;

        if (Mathf.Abs(targetSteer - steeringInput) < 0.01f)
            steeringInput = targetSteer;
        else
            steeringInput = Mathf.MoveTowards(steeringInput, targetSteer, smoothDelta);
    }

    private void TryStartPostCrashRecovery()
    {
        if (IsDeadForMashRecovery) return;
        if (!enableFlipRecoveryMash) return;

        bool isFlipped = NeedsFlipRecovery();

        // Trigger recovery on any crash if enabled, or only when flipped
        if (!enableCrashRecoveryAlways && !isFlipped) return;

        // If we are already recovering, DO NOT restart a new sequence.
        // Instead, increase the remaining clicks based on the new crash.
        if (_flipMashActive)
        {
            AddMashDebtFromNewCrash(isFlipped);
            return;
        }

        // If we were mid-reorient and got crashed again, cancel reorient and start mash instead.
        if (_isReorienting)
        {
            _isReorienting = false;
        }

        BeginCrashMashRecovery(isFlipped);
    }

    /// <summary>
    /// When we get crashed again during an active mash recovery: bump total clicks needed (never easier than before),
    /// hide mash UI briefly, then require the player to mash from <b>zero</b> progress again at the new total.
    /// </summary>
    private void AddMashDebtFromNewCrash(bool isFlipped)
    {
        // Ensure flipped flag persists if any crash in the chain required it
        _isFlippedDuringRecovery |= isFlipped;

        // "How many clicks would recovery require if it started right now?"
        int requiredIfStartedNow = CalculateMashClicksNeeded(isFlipped);

        // Total difficulty must account for work already done + new crash, then we reset progress to 0 below.
        int desiredTotalNeeded = _flipMashClicks + requiredIfStartedNow;

        // Always grow when taking another crash, even if requiredIfStartedNow happens to be smaller.
        if (desiredTotalNeeded <= _flipMashClicksNeeded)
            desiredTotalNeeded = _flipMashClicksNeeded + 1;

        _flipMashClicksNeeded = Mathf.Clamp(desiredTotalNeeded, mashClicksAbsoluteMin, mashClicksAbsoluteMax);

        _flipMashClicks = 0;
        _passiveClickTimer = 0f;

        _mashGaugeValue = 0f;
        _mashGaugePeakValue = 0f;
        _gaugeMaxedThisSession = false;
        _totalMashClicksThisSession = 0;
        _totalFuelGainedThisSession = 0f;
        _totalSprocketsThisSession = 0;

        _flipMashUiShowAgainTime = Time.time + mashUiHideSecondsOnExtraHit;

        // Treat new crash as a "speed reset" so you can't buffer super-fast clicks through impacts
        _lastMashTime = Time.time;
        _lastRegisteredMashFrame = -1;
        _currentMashSpeed = 0f;
        _mashSpeedSmoothed = 0f;
    }


    private void BeginCrashMashRecovery(bool isFlipped)
    {
        if (IsDeadForMashRecovery) return;
        if (!enableFlipRecoveryMash) return;

        ChooseMashFaceButton();

        _flipMashUiShowAgainTime = 0f;

        _mashGaugeValue = 0f;
        _mashGaugePeakValue = 0f;
        _gaugeMaxedThisSession = false;
        _totalMashClicksThisSession = 0;
        _totalFuelGainedThisSession = 0f;
        _totalSprocketsThisSession = 0;

        _flipMashActive = true;
        _isReorienting = false;
        _isFlippedDuringRecovery = isFlipped;
        // Calculate dynamic click count
        _flipMashClicksNeeded = CalculateMashClicksNeeded(isFlipped);
        _flipMashClicks = 0;
        _passiveClickTimer = 0f;

        // Reset mash speed tracking
        _lastMashTime = Time.time;
        _currentMashSpeed = 0f;
        _mashSpeedSmoothed = 0f;
    }

    private int CalculateMashClicksNeeded(bool isFlipped)
    {
        // Distance (progress) is intended to be the primary driver.
        // We compute: baseClicks * distanceFactor * severityFactor * crashFactor * (optional multipliers).
        // This creates a smooth "always scaling" feel instead of lots of independent min/max caps.

        float distanceTraveled = 0f;
        var gm = GameManager_Racing.Instance;
        if (gm != null)
            distanceTraveled = gm.DistanceAlongTrack;

        float progress01 = GetTrackProgress01(distanceTraveled);
        float t = mashClicksByProgress != null ? Mathf.Clamp01(mashClicksByProgress.Evaluate(progress01)) : progress01;

        float totalClicks;

        if (useMultiplicativeMashClicks)
        {
            // Distance factor: 1 + weight * t^exp   (huge near the end)
            float distanceFactor = 1f + mashDistanceWeight * Mathf.Pow(t, mashDistanceExponent);

            // Severity factor: 1 + weight * sev^exp
            float sev01 = Mathf.Clamp01(_lastCrashSeverity);
            float severityFactor = 1f + mashSeverityWeight * Mathf.Pow(sev01, mashSeverityExponent);

            // Crash factor: 1 + weight * (crashCount-1)  (monotonic; first crash ~= 1)
            float crashFactor = 1f + mashCrashCountWeight * Mathf.Max(0, _crashCount - 1);

            // Cumulative severity factor (repeated crashing ramps fast)
            float severitySumFactor = 1f + mashSeveritySumWeight * Mathf.Max(0f, _crashSeveritySum);

            totalClicks = mashBaseClicks * distanceFactor * crashFactor * severitySumFactor;
        }
        else
        {
            // Legacy additive model (kept for safety/back-compat).
            float severityClicks = Mathf.Lerp(mashClicksMin, mashClicksMaxFromSeverity, _lastCrashSeverity);
            int crashCountClicks = Mathf.Min(_crashCount * mashClicksPerCrash, mashClicksMaxFromCrashCount);

            int distanceClicks = 0;
            if (mashClicksMaxFromDistance > 0)
                distanceClicks = Mathf.Clamp(Mathf.RoundToInt(mashClicksMaxFromDistance * t), 0, mashClicksMaxFromDistance);

            totalClicks = severityClicks + crashCountClicks + distanceClicks;
        }

        // Mash click-strength difficulty scaling (non-compounding).
        // Baseline (effective == base) remains 1x.
        // Above baseline, required clicks scale linearly from current strength ratio and tuning step,
        // rather than exponentiating on top of prior levels.
        int safeBaseClickStrength = Mathf.Max(1, baseClicksPerClick);
        float strengthRatio = Mathf.Max(1f, (float)effectiveClicksPerClick / safeBaseClickStrength);
        float step = Mathf.Max(1f, mashDifficultyPerClickPowerStep);
        float mashStrengthDifficultyMultiplier = strengthRatio;
        if (effectiveClicksPerClick > safeBaseClickStrength)
            mashStrengthDifficultyMultiplier = strengthRatio * step;
        totalClicks *= mashStrengthDifficultyMultiplier;

        return Mathf.Clamp(Mathf.RoundToInt(totalClicks), mashClicksAbsoluteMin, mashClicksAbsoluteMax);
    }

    /// <summary>
    /// Returns normalized progress (0..1) for the run based on distance traveled.
    /// Tries to read a total track length from GameManager_Racing via reflection (so we don't hard-depend on a specific API),
    /// otherwise falls back to mashProgressTotalDistanceFallback.
    /// </summary>
    private float GetTrackProgress01(float distanceTraveled)
    {
        float total = 0f;

        var gm = GameManager_Racing.Instance;
        if (gm != null)
        {
            try
            {
                var t = gm.GetType();

                // Try common property/field names
                var prop = t.GetProperty("TrackTotalLength") ?? t.GetProperty("TotalTrackLength") ?? t.GetProperty("TrackLength");
                if (prop != null && prop.PropertyType == typeof(float))
                    total = (float)prop.GetValue(gm, null);

                if (total <= 0f)
                {
                    var field = t.GetField("TrackTotalLength") ?? t.GetField("totalTrackLength") ?? t.GetField("trackTotalLength") ?? t.GetField("TrackLength") ?? t.GetField("trackLength");
                    if (field != null && field.FieldType == typeof(float))
                        total = (float)field.GetValue(gm);
                }
            }
            catch { /* ignore and fall back */ }
        }

        if (total <= 0f)
            total = mashProgressTotalDistanceFallback;

        if (total <= 0f) return 0f;
        return Mathf.Clamp01(distanceTraveled / total);
    }
    private bool WillStartMashRecoveryNow()
    {
        if (IsDeadForMashRecovery) return false;
        if (!enableFlipRecoveryMash) return false;

        bool isFlipped = NeedsFlipRecovery();
        if (!enableCrashRecoveryAlways && !isFlipped) return false;

        return true;
    }



    // Keep old method for any other calls
    private void BeginFlipMashRecovery()
    {
        BeginCrashMashRecovery(NeedsFlipRecovery());
    }

    private void PlayDeathVFX()
    {
        if (_deathVfxPlayed) return;
        if (deathVFX == null) return;

        _deathVfxPlayed = true;

        PlayDeathExplodeSfx(transform.position);

        Vector3 spawnPos = transform.position;

        try
        {
            if (ProjectilePool.Instance != null)
            {
                GameObject inst = ProjectilePool.Instance.Get(deathVFX);
                if (inst != null)
                {
                    inst.transform.SetPositionAndRotation(
                        spawnPos,
                        deathVFX.transform.rotation // usually identity, but importantly: matches prefab root
                    );
                    inst.SetActive(true);
                    StartCoroutine(ReturnPooledVFXLater(deathVFX, inst, deathVFXLifetime));
                    return;
                }
            }
        }
        catch { /* fallback below */ }

        GameObject go = Instantiate(
            deathVFX,
            spawnPos,
            deathVFX.transform.rotation
        );
        Destroy(go, deathVFXLifetime);
    }

    public void PlayDeathVFXExtra()
    {
        if (deathVFX == null) return;

        Vector3 spawnPos = transform.position;
        PlayDeathExplodeSfx(spawnPos);

        // Intentionally BYPASS _deathVfxPlayed so we can “double explode”
        try
        {
            if (ProjectilePool.Instance != null)
            {
                GameObject inst = ProjectilePool.Instance.Get(deathVFX);
                if (inst != null)
                {
                    inst.transform.SetPositionAndRotation(
                        spawnPos,
                        deathVFX.transform.rotation // usually identity, but importantly: matches prefab root
                    );
                    inst.SetActive(true);
                    StartCoroutine(ReturnPooledVFXLater(deathVFX, inst, deathVFXLifetime));
                    return;
                }
            }
        }
        catch { /* fallback below */ }

        GameObject go = Instantiate(deathVFX, spawnPos, Quaternion.identity);
        Destroy(go, deathVFXLifetime);
    }


    private System.Collections.IEnumerator ReturnPooledVFXLater(GameObject prefab, GameObject instance, float delay)
    {
        yield return new WaitForSeconds(delay);
        if (instance != null && prefab != null && ProjectilePool.Instance != null)
            ProjectilePool.Instance.Return(prefab, instance);
    }

    // Add this method near other public APIs (e.g. below ConsumeFuel or at the end of the class)
    public float AddFuel(float amount)
    {
        if (maxFuel <= 0f || amount <= 0f) return 0f;
        float before = currentFuel;
        currentFuel = Mathf.Min(maxFuel, currentFuel + amount);
        if (currentFuel > 0f) isOutOfFuel = false;
        if (rb != null && !isOutOfFuel && !isOutOfHP)
            rb.freezeRotation = true;

        float actual = currentFuel - before;
        if (actual >= minFuelLossForPopup)
            TrySpawnPopup(RacingPopupType.FuelGain, actual);

        return Mathf.Max(0f, actual);
    }

    public float AddHP(float amount)
    {
        if (maxHP <= 0f || amount <= 0f) return 0f;
        float before = currentHP;
        currentHP = Mathf.Min(maxHP, currentHP + amount);
        if (currentHP > 0f) isOutOfHP = false;
        if (rb != null && !isOutOfFuel && !isOutOfHP)
            rb.freezeRotation = true;

        ScreenFlashManager.Heal();

        float actual = currentHP - before;
        if (actual >= minHPDamageForPopup)
            TrySpawnPopup(RacingPopupType.HPGain, actual);

        return Mathf.Max(0f, actual);
    }

    private void CacheDamageSmokeSystems()
    {
        _damageSmokeSystems = null;
        _damageSmokeRootGO = null;
        _damageSmokeActive = false;

        if (!damageSmokeVFX) return;

        // The reference may point to a child ParticleSystem inside a prefab (e.g. `SmokeVFX`).
        // Cache the whole prefab root so we can reliably disable/enable the entire smoke VFX.
        Transform root = damageSmokeVFX.transform;
        Transform t = root;
        while (t != null)
        {
            string n = t.name;
            if (!string.IsNullOrEmpty(n) &&
                (n.IndexOf("SmokeVFX", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                 n.IndexOf("DamageSmoke", System.StringComparison.OrdinalIgnoreCase) >= 0))
            {
                root = t;
                break;
            }
            t = t.parent;
        }

        _damageSmokeRootGO = root.gameObject;
        _damageSmokeSystems = root.GetComponentsInChildren<ParticleSystem>(true);
    }

    private void ForceDisableDamageSmokeVFXImmediate()
    {
        _damageSmokeActive = false;

        if (_damageSmokeRootGO != null && _damageSmokeRootGO.activeSelf)
            _damageSmokeRootGO.SetActive(false);

        if (_damageSmokeSystems == null) return;

        // Ensure nothing is still emitting if something else re-activates it.
        for (int i = 0; i < _damageSmokeSystems.Length; i++)
        {
            var ps = _damageSmokeSystems[i];
            if (!ps) continue;
            var e = ps.emission;
            e.enabled = false;
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }
    }

    private void ForceEnableDamageSmokeVFX()
    {
        if (_damageSmokeRootGO != null && !_damageSmokeRootGO.activeSelf)
            _damageSmokeRootGO.SetActive(true);

        if (_damageSmokeSystems == null) return;
        for (int i = 0; i < _damageSmokeSystems.Length; i++)
        {
            var ps = _damageSmokeSystems[i];
            if (!ps) continue;
            ps.Play(true);
        }
    }

    private void UpdateDamageVFXImmediate()
    {
        if (!damageSmokeVFX) return;

        if (_damageSmokeRootGO == null && _damageSmokeSystems == null)
            CacheDamageSmokeSystems();

        // Don't run any stat-dependent VFX until config and skills have been applied
        if (!_statsInitialized)
        {
            ForceDisableDamageSmokeVFXImmediate();
            return;
        }

        float hpFrac = HPPercent;
        bool shouldBeOn = hpFrac <= smokeStartHPFraction;

        if (!shouldBeOn)
        {
            if (_damageSmokeActive)
                ForceDisableDamageSmokeVFXImmediate();
            return;
        }

        // Enable root once when we cross the threshold
        if (!_damageSmokeActive)
        {
            ForceEnableDamageSmokeVFX();
            _damageSmokeActive = true;
        }

        // Damage progress 0..1 from threshold → zero HP
        float tDamage = Mathf.Clamp01((smokeStartHPFraction - hpFrac) / Mathf.Max(0.0001f, smokeStartHPFraction));

        // Apply tuning to the configured system (this is the one designers tweak in the prefab/config)
        var emission = damageSmokeVFX.emission;
        var main = damageSmokeVFX.main;
        emission.enabled = true;
        emission.rateOverTime = Mathf.Lerp(smokeMinRate, smokeMaxRate, tDamage);

        float size = Mathf.Lerp(smokeMinSize, smokeMaxSize, tDamage);
        main.startSize = new ParticleSystem.MinMaxCurve(size);

        float colorT = invertSmokeColorLerp ? (1f - tDamage) : tDamage;
        Color currentColor = Color.Lerp(smokeColorAtThreshold, smokeColorAtZeroHP, colorT);
        main.startColor = new ParticleSystem.MinMaxGradient(currentColor);
    }

    public static CarController Instance { get; private set; }

    public static void RequestWorldShake(
        Vector3 sourceWorldPos,
        float intensity,
        float frequency,
        float maxDistance,
        float fullIntensityDistance = 0f)
    {
        if (intensity <= 0f || frequency <= 0f || maxDistance <= 0f) return;
        if (Instance == null) return;
        if (Instance.screenShakeGlobalMultiplier <= 0f) return;

        float d = Vector3.Distance(Instance.transform.position, sourceWorldPos);
        if (d > maxDistance) return;

        float t = 1f - Mathf.InverseLerp(fullIntensityDistance, maxDistance, d);
        float amp = intensity * Mathf.Clamp01(t) * Instance.screenShakeGlobalMultiplier;

        Instance._shakeAmp = Mathf.Max(Instance._shakeAmp, amp);
        Instance._shakeFreq = Mathf.Max(Instance._shakeFreq, frequency);
    }


    // PUBLIC READ-ONLY
    /// <summary>World velocity magnitude (m/s). Used by in-game HUD speedometer (<see cref="UIManager_Racing"/>).</summary>
    public float CurrentSpeed => rb != null ? rb.velocity.magnitude : 0f;
    public float EffectiveMaxSpeed => effectiveMaxSpeed;
    public bool IsOutOfFuel => isOutOfFuel;
    public bool IsOutOfHP => isOutOfHP;

    /// <summary>Returns true if a malfunction burst is currently active (throttle is reduced).</summary>
    public bool IsMalfunctioning => _malfunctionTimer > 0f;
    /// <summary>Returns the current malfunction throttle multiplier (1.0 = normal, lower = reduced throttle).</summary>
    public float MalfunctionMultiplier => _currentMalfunctionMultiplier;
    public float CurrentFuel => currentFuel;
    public float MaxFuel => maxFuel;

    public bool IsOnIceSurface => _onIceSurface;

    /// <summary>Current drift charge 0–1; falls off when not steering or when releasing drift (same as used for drift turn).</summary>
    public float DriftCharge => driftCharge;
    public bool IsDrifting => isDrifting;

    // ── Drift-held boost (hold drift + steer → release for boost) — UI meter ──

    /// <summary>Seconds accumulated this hold toward drift-held boost (same value used on drift release).</summary>
    public float DriftHeldBoostHoldSeconds => _driftHoldTimeSeconds;

    /// <summary>Min hold time before a boost can fire on drift release (from boost config / skills).</summary>
    public float DriftHeldBoostMinHoldSeconds => driftBoostMinHoldSeconds;

    /// <summary>Hold time at which drift-held boost strength stops scaling (clamp cap).</summary>
    public float DriftHeldBoostMaxHoldSeconds => driftBoostMaxHoldSeconds;

    /// <summary>Fill 0..1 for a bar: hold / max hold.</summary>
    public float DriftHeldBoostHoldFillNormalized =>
        driftBoostMaxHoldSeconds > 1e-4f
            ? Mathf.Clamp01(_driftHoldTimeSeconds / driftBoostMaxHoldSeconds)
            : 0f;

    /// <summary>Where the "minimum boost" threshold sits on the same 0..1 bar as <see cref="DriftHeldBoostHoldFillNormalized"/>.</summary>
    public float DriftHeldBoostMinHoldMarker01 =>
        driftBoostMaxHoldSeconds > 1e-4f
            ? Mathf.Clamp01(driftBoostMinHoldSeconds / driftBoostMaxHoldSeconds)
            : 0f;

    /// <summary>Show a drift boost charge bar: feature on, drift unlocked, skill unlocked, drift button held, input live.</summary>
    public bool DriftHeldBoostChargeBarVisible =>
        enableDriftHeldBoost &&
        driftUnlocked &&
        driftButtonHeld &&
        !_externalInputLocked &&
        _driftBoostCooldownTimer <= 0f &&
        (RacingSkillTreeManager.Instance == null || RacingSkillTreeManager.Instance.IsDriftHeldBoostUnlocked());

    public float FuelPercent => maxFuel > 0f ? currentFuel / maxFuel : 0f;
    public float OffDefaultFraction => offDefaultFraction;
    public float GrassFraction => grassFraction;

    public float CurrentHP => currentHP;
    public float MaxHP => maxHP;
    public float HPPercent => maxHP > 0f ? currentHP / maxHP : 0f;

    public bool IsGaugeCurrentlyAtMax => _mashGaugeValue >= gaugeMaxThreshold;

    public float BaseAcceleration => baseAcceleration;
    public float BaseMaxSpeed => baseMaxSpeed;
    public float BaseMaxFuel => baseMaxFuel;
    public float BaseMaxHP => baseMaxHP;
    public float BaseTurnSpeed => baseTurnSpeed;
    public float BaseDrivingFuelUse => baseFuelUseFullThrottle;
    public float BaseHPRegen => baseHpRegenPerSecond;
    public float BaseBoostForce => baseBoostForce;
    public float BaseBoostDuration => baseBoostDuration;
    public float BaseBoostCooldown => baseBoostCooldown;
    public float BaseBoostFuelCost => baseBoostFuelCost;
    public float BaseClicksPerClick => baseClicksPerClick;
    public float BasePassiveClickRate => basePassiveClickRate;
    public float BasePassiveClickStrength => basePassiveClickStrength;
    public float BaseMashFuelPerClick => mashBaseFuelPerClick;

    // ═══════════════════════════════════════════════════════════════════════════
    // CENTRALIZED SPEED BOOST SYSTEM - METHODS
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Adds or refreshes a speed boost. If a boost with the same ID exists, it will be replaced.
    /// </summary>
    /// <param name="id">Unique identifier for this boost type</param>
    /// <param name="maxSpeedValue">Either additive speed increase OR multiplier (based on isMultiplier)</param>
    /// <param name="duration">How long the boost lasts</param>
    /// <param name="rampDownFraction">Fraction of duration where ramp-down begins (0.3 = last 30%)</param>
    /// <param name="isMultiplier">If true, maxSpeedValue is a multiplier (e.g., 1.5 = 50% more); if false, additive</param>
    public void AddSpeedBoost(string id, float maxSpeedValue, float duration, float rampDownFraction = -1f, bool isMultiplier = false)
    {
        if (string.IsNullOrEmpty(id) || duration <= 0f) return;

        // Use default ramp-down if not specified
        if (rampDownFraction < 0f)
        {
            rampDownFraction = defaultBoostRampDownFraction;
        }

        // Remove existing boost with same ID
        RemoveSpeedBoost(id);

        var entry = new SpeedBoostEntry
        {
            id = id,
            maxSpeedIncrease = maxSpeedValue,
            totalDuration = duration,
            remainingTime = duration,
            rampDownStartFraction = Mathf.Clamp01(rampDownFraction),
            isMultiplier = isMultiplier
        };

        _activeSpeedBoosts.Add(entry);

        Debug.Log($"[SpeedBoost] Added boost '{id}': value={maxSpeedValue:F2}, duration={duration:F2}s, rampDown={rampDownFraction:P0}, isMultiplier={isMultiplier}");
    }

    /// <summary>
    /// Removes a speed boost by ID.
    /// </summary>
    public void RemoveSpeedBoost(string id)
    {
        for (int i = _activeSpeedBoosts.Count - 1; i >= 0; i--)
        {
            if (_activeSpeedBoosts[i].id == id)
            {
                Debug.Log($"[SpeedBoost] Removed boost '{id}'");
                _activeSpeedBoosts.RemoveAt(i);
            }
        }
    }

    /// <summary>
    /// Checks if a specific boost is currently active.
    /// </summary>
    public bool HasSpeedBoost(string id)
    {
        for (int i = 0; i < _activeSpeedBoosts.Count; i++)
        {
            if (_activeSpeedBoosts[i].id == id && _activeSpeedBoosts[i].remainingTime > 0f)
                return true;
        }
        return false;
    }

    /// <summary>
    /// Gets the remaining time on a specific boost, or 0 if not active.
    /// </summary>
    public float GetSpeedBoostRemainingTime(string id)
    {
        for (int i = 0; i < _activeSpeedBoosts.Count; i++)
        {
            if (_activeSpeedBoosts[i].id == id)
                return _activeSpeedBoosts[i].remainingTime;
        }
        return 0f;
    }

    /// <summary>
    /// Clears all active speed boosts.
    /// </summary>
    public void ClearAllSpeedBoosts()
    {
        if (_activeSpeedBoosts.Count > 0)
        {
            Debug.Log($"[SpeedBoost] Cleared {_activeSpeedBoosts.Count} active boosts");
            _activeSpeedBoosts.Clear();
        }
    }

    /// <summary>
    /// Updates all active speed boosts (call this in FixedUpdate).
    /// </summary>
    private void UpdateSpeedBoosts(float deltaTime)
    {
        for (int i = _activeSpeedBoosts.Count - 1; i >= 0; i--)
        {
            var boost = _activeSpeedBoosts[i];
            boost.remainingTime -= deltaTime;

            if (boost.remainingTime <= 0f)
            {
                Debug.Log($"[SpeedBoost] Boost '{boost.id}' expired");
                _activeSpeedBoosts.RemoveAt(i);
            }
            else
            {
                _activeSpeedBoosts[i] = boost;
            }
        }
    }

    /// <summary>
    /// Calculates the total speed cap increase from all active boosts.
    /// This should be added to the base effectiveMaxSpeed.
    /// </summary>
    private float GetTotalSpeedBoostIncrease()
    {
        if (_activeSpeedBoosts.Count == 0) return 0f;

        float totalIncrease = 0f;
        float totalMultiplier = 1f;

        for (int i = 0; i < _activeSpeedBoosts.Count; i++)
        {
            var boost = _activeSpeedBoosts[i];

            if (boost.isMultiplier)
            {
                // Multiplicative boosts stack multiplicatively
                totalMultiplier *= boost.GetCurrentMultiplier();
            }
            else
            {
                // Additive boosts stack additively
                totalIncrease += boost.GetCurrentSpeedIncrease(effectiveMaxSpeed);
            }
        }

        // Apply multiplier to base speed, then add additive boosts
        float multipliedBase = effectiveMaxSpeed * (totalMultiplier - 1f);
        return multipliedBase + totalIncrease;
    }

    /// <summary>
    /// Gets the total speed multiplier from all active multiplier-based boosts.
    /// </summary>
    private float GetTotalSpeedBoostMultiplier()
    {
        if (_activeSpeedBoosts.Count == 0) return 1f;

        float totalMultiplier = 1f;

        for (int i = 0; i < _activeSpeedBoosts.Count; i++)
        {
            var boost = _activeSpeedBoosts[i];
            if (boost.isMultiplier)
            {
                totalMultiplier *= boost.GetCurrentMultiplier();
            }
        }

        return totalMultiplier;
    }

    /// <summary>
    /// Returns the count of currently active speed boosts.
    /// </summary>
    public int ActiveSpeedBoostCount => _activeSpeedBoosts.Count;

    /// <summary>
    /// Returns true if any speed boost is currently active.
    /// </summary>
    public bool HasAnySpeedBoost => _activeSpeedBoosts.Count > 0;

    /// <summary>
    /// Public property to check if close call boost is specifically active.
    /// </summary>
    public bool IsCloseCallBoostActive => HasSpeedBoost(BOOST_ID_CLOSE_CALL);

    // ═══════════════════════════════════════════════════════════════════════════
    // END CENTRALIZED SPEED BOOST SYSTEM - METHODS
    // ═══════════════════════════════════════════════════════════════════════════

}