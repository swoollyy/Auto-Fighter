using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
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
[RequireComponent(typeof(CarAirTrickConfig))]
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
    private CarAirTrickConfig _airTrickConfig;
    private CarCrashMashConfig _crashMashConfig;
    private CarVFXAudioConfig _vfxAudioConfig;

    [Header("Base Movement (on Default surface)")]
    private bool testAlwaysAccelerate;
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
    private bool enableLandingLateralBleed;
    private float landingLateralBleedPerSecond;
    private float landingLateralBleedDuration;
    private bool enableLandingSlipStraighten;
    private float landingSlipStraightenDuration;
    private float landingSlipCarryFraction;
    private float landingSlipMaxAlignDegrees;
    private float landingSlipLateralKeep;
    private float _landingSlipStraightenLeft;
    private float _landingSlipStraightenDuration;
    private Vector3 _landingSlipStartDir = Vector3.forward;
    private bool _landingSlipStartDirValid;
    /// <summary>Steer/drift above this cancels post-landing slip straighten (player owns the turn).</summary>
    private const float LandingStraightenAbortSteer = 0.15f;

    [Header("Bad Landing Crash (from CarLandingConfig)")]
    private bool enableBadLandingCrash;
    private float badLandingMinAirborneSeconds;
    private float badLandingUpAlignDotMin;
    private float badLandingForwardNormalDotMax;
    private float badLandingCrashSeverity;
    private float badLandingSpeedRetain;
    private float badLandingVerticalSpeedRetain;
    private float badLandingTumbleTorque;
    private float badLandingAngularSpeedRetain;
    private float badLandingMaxAngularSpeed;
    private float _airborneContinuousTime;

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
        bool held = IsBoostHeldRaw();

        if (IsDrivingGameplayLockedByCrash)
        {
            _blockBoostUntilReleased = true;
            return false;
        }

        if (_blockBoostUntilReleased)
        {
            if (held) return false;
            _blockBoostUntilReleased = false;
        }

        var reader = RacingInputReader.Instance;
        if (reader != null) return reader.BoostDown;
        return Input.GetKeyDown(boostKey) || Input.GetKeyDown(boostButtonController);
    }

    private bool IsBoostHeldRaw()
    {
        var reader = RacingInputReader.Instance;
        if (reader != null) return reader.BoostHeld;
        return Input.GetKey(boostKey) || Input.GetKey(boostButtonController);
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
        // Ignore while crashed/reorienting/mashing/post-crash recovery, and require a fresh
        // press after recovery ends. Trigger deadzone dips used to clear the release-gate early
        // and fire full throttle + drag-cancel while still sideways — that felt like a boost.
        bool held = IsAccelerateHeldRaw();

        if (IsDrivingGameplayLockedByCrash || IsPostCrashRecoveryDriving)
        {
            _blockAccelUntilReleased = true;
            return false;
        }

        if (_blockAccelUntilReleased)
        {
            if (held) return false;
            _blockAccelUntilReleased = false;
        }

        return held;
    }

    private bool IsAccelerateHeldRaw()
    {
        var reader = RacingInputReader.Instance;
        if (reader != null) return reader.Accelerate > 0.1f;
        return Input.GetKey(KeyCode.W) || Input.GetAxisRaw("RightTrigger") > 0.1f;
    }

    private bool GetBrakeKeyOrTrigger()
    {
        if (IsDrivingGameplayLockedByCrash)
            return false;

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

    private bool GetBarrelRollHeld()
    {
        var reader = RacingInputReader.Instance;
        if (reader != null) return reader.BarrelRollHeld;
        return Input.GetKey(KeyCode.Q)
            || (Gamepad.current != null && Gamepad.current.leftShoulder.isPressed);
    }

    private float GetTrickStickXRaw()
    {
        var reader = RacingInputReader.Instance;
        // Non-inverted: stick left/right matches spin direction after the invert request.
        if (reader != null) return reader.TrickStickX;
        if (Gamepad.current != null)
        {
            float v = Gamepad.current.rightStick.x.ReadValue();
            return Mathf.Abs(v) < 0.12f ? 0f : Mathf.Clamp(v, -1f, 1f);
        }
        float x = 0f;
        if (Input.GetKey(KeyCode.LeftArrow)) x -= 1f;
        if (Input.GetKey(KeyCode.RightArrow)) x += 1f;
        return Mathf.Clamp(x, -1f, 1f);
    }

    private float GetTrickStickYRaw()
    {
        var reader = RacingInputReader.Instance;
        if (reader != null) return reader.TrickStickY;
        if (Gamepad.current != null)
        {
            float v = Gamepad.current.rightStick.y.ReadValue();
            return Mathf.Abs(v) < 0.12f ? 0f : Mathf.Clamp(v, -1f, 1f);
        }
        float y = 0f;
        if (Input.GetKey(KeyCode.UpArrow)) y += 1f;
        if (Input.GetKey(KeyCode.DownArrow)) y -= 1f;
        return Mathf.Clamp(y, -1f, 1f);
    }

    private bool ComputeTrickStickActive()
    {
        float horiz = ApplyAxisDeadzone(GetTrickStickXRaw(), trickInputDeadzone);
        float pitch = ApplyAxisDeadzone(GetTrickStickYRaw(), trickInputDeadzone);
        return Mathf.Abs(horiz) > 0.001f || Mathf.Abs(pitch) > 0.001f;
    }

    private float GetSteerVerticalRaw()
    {
        var reader = RacingInputReader.Instance;
        if (reader != null) return reader.SteerVertical;
        if (Gamepad.current != null)
        {
            float v = Gamepad.current.leftStick.y.ReadValue();
            return Mathf.Abs(v) < 0.12f ? 0f : Mathf.Clamp(v, -1f, 1f);
        }
        float kb = 0f;
        if (Input.GetKey(KeyCode.W)) kb += 1f;
        if (Input.GetKey(KeyCode.S)) kb -= 1f;
        return Mathf.Clamp(kb, -1f, 1f);
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
    private float steerTractionWhileAccelerating;
    private float steerTractionWhileBraking;
    private float _steerTractionBlend = 0f;
    /// <summary>0–1 ease of brake nose-align so high-speed brake does not hard-swap into a separate handling mode.</summary>
    private float _brakeAlignBlend01;
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
    private float driftSideForceWhileAccelerating;
    private float driftSideForceThrottleBlendRate;
    private float _driftSideForceThrottle01 = 1f;
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
    private float steerFlipGraceSeconds;
    private float steerFlipRebuildDelay;

    [Header("Drift Glide (Ice Feel)")]
    private bool allowDriftGlideWithoutSteer;
    private float driftGlideDecayPerSecond;
    private float driftLandingFeelBlendSeconds;

    /// <summary>
    /// 0–1 how fully ground-drift feel is applied. Stays at 0 in air; after landing while
    /// still holding a mid-air drift, eases to 1 so steer/side/speed/camera don't snap on.
    /// </summary>
    private float _driftGroundFeel01 = 1f;

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
    private float _driftSteerFlipPendingTimer = 0f;

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
    private float steepGroundNormalBlendRate = 36f;
    private float steepGroundNormalDotThreshold = 0.97f;
    private float boostSurfaceNormalBlendRate = 28f;
    private float roadGrassTransitionLiftSpeed;
    private float roadGrassTransitionMinSpeed;
    private float roadGrassTransitionLiftCooldown;
    private float _grassFractionPrevFrame = -1f;
    private float _lastRoadGrassTransitionLiftTime = -999f;
    private float _planarSpeedLastFixedUpdate;
    private float _lastGrassToRoadSpeedPreserveTime = -999f;
    private readonly RaycastHit[] _surfaceRayBuffer = new RaycastHit[8];
    private readonly RaycastHit[] _groundNormalHitBuffer = new RaycastHit[12];
    private const float MixedSurfaceLipNormalMinY = 0.88f;
    /// <summary>Flat boost pads below this normal.y are treated as near-horizontal (ignore for drive normals).</summary>
    private const float FlatBoostNormalMinY = 0.92f;
    /// <summary>Contact points within this height are “same deck” when choosing a mountable plane.</summary>
    private const float GroundNormalHeightTieBand = 0.08f;
    /// <summary>Only strip into-face dig on surfaces steeper than this (normal.y below).</summary>
    private const float MountAssistMaxNormalY = 0.90f;

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
    private bool enableLowHpExtraFuelDrain = true;
    private float extraFuelDrainPercentAtZeroHp = 100f;

    [Header("Crash / Hit (from CarCrashMashConfig)")]
    private LayerMask crashLayers;
    private bool enableDriveOnObstacleTops;
    private float obstacleTopNormalDotMin;
    private float obstacleTopCarUpDotMin;
    private LayerMask driveableObstacleLayers;
    private int obstacleTopLandingCoinReward;
    private float obstacleTopLandingFuelReward;
    private float obstacleTopLandingRewardCooldown;
    private readonly Dictionary<int, float> _obstacleTopRewardTime = new Dictionary<int, float>();
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
    private float reverseSteerEngageForwardSpeed;

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
    private bool guaranteeMinimumThrottleAcceleration = true;
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

    // Crash mash minigame unlock (gated behind SkillType.CrashMashUnlock; see CarCrashMashConfig.RequireCrashMashUnlock).
    private bool requireCrashMashUnlock;
    private bool crashMashUnlocked;
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
    private float steepAlignSpeedMultiplier = 2.1f;
    private float steepAlignMinAngle = 12f;
    private float rampVelocityRemapStrength = 0.72f;
    private float rampVelocityRemapMinAngle = 3.5f;

    [Header("Airborne Tricks (from CarAirTrickConfig)")]
    private bool enableAirTricks;
    private float airTurnSpeedMultiplier;
    private float airDriftSteerFeel;
    private float airDriftSideForceScale;
    private float airYawRate;
    private float airYawInputDeadzone;
    private float airVelocityFollowNose;
    private float airVelocityFollowRate;
    private float airVelocityFollowReturnSeconds;
    private float airTrajectorySteerRate;
    private float airSteerInputSmoothRate;
    private float airSteerInputReleaseRate;
    private float airTrajectoryBankAccel;
    private bool enableAirAimWhileTricking;
    private float airAimWhileTrickingYawMult;
    private float airAimHorizSpinYawMult;
    private float airTrajectoryWhileTrickingMult;
    private float airTrajectoryHorizSpinMult;
    private float trickPitchRate;
    private float trickYawSpinRate;
    private float trickRollRate;
    private float barrelModeBlendSpeed;
    private float trickInputDeadzone;
    private float trickInputSmoothRate;
    private float trickInputReleaseRate;
    private float trickRotationAccel;
    private float trickRotationDecel;
    private bool suppressRampAlignmentInTrickMode;
    private bool enableAirUprightRecovery;
    private float airUprightRecoverSpeed;
    private float airUprightNearGroundBoost;
    private float airUprightMinAlignDot;
    private float airGravityMultiplier;

    private float _smoothedTrickPitch;
    private float _smoothedTrickHoriz;
    private float _trickPitchAngularVel;
    private float _trickYawAngularVel;
    private float _trickRollAngularVel;
    /// <summary>0 = disc spin (yaw), 1 = barrel roll. Cross-fades while barrel is held.</summary>
    private float _barrelModeBlend;
    private bool _trickBarrelModeActive;
    private float _smoothedAirSteer;
    private float _airTrajectoryBankRate;
    private float _airUprightRecoverBlend;
    /// <summary>0–1 how fully air nose-follow applies. Drops while tricking, eases back after release.</summary>
    private float _airVelocityFollowBlend01 = 1f;

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
    private bool _isRunEndMash;
    private bool _runEndMashOffered;
    [SerializeField, Min(1f)] private float runEndMashIdleTimeout = 15f;
    private int _flipMashClicks;
    private int _flipMashClicksNeeded;
    /// <summary>Mash UI stays hidden until this time after a secondary hit during mash (<see cref="AddMashDebtFromNewCrash"/>).</summary>
    private float _flipMashUiShowAgainTime;

    private float _lastMashTime;
    private int _lastRegisteredMashFrame = -1;
    private float _currentMashSpeed;        // 0 to 1, where 1 = max speed
    private float _mashSpeedSmoothed;

    public bool IsFlipMashActive => _flipMashActive;
    public bool IsRunEndMashActive => _flipMashActive && _isRunEndMash;
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
    /// <summary>
    /// After any crash, suppress ramp landing boost/carry until the next clean takeoff.
    /// Prevents mid-air crash → touchdown from re-arming takeoff speed / landing inject.
    /// </summary>
    private bool _blockLandingBoostUntilNextTakeoff;

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
    /// <summary>Previous-frame smoothed normal — used to detect ramp/elevation steepening for velocity remap.</summary>
    private Vector3 _prevSmoothedGroundNormal = Vector3.up;
    private bool _deathVfxPlayed = false;

    public event Action OnBoostStarted;
    public event Action OnBoostEnded;

    /// <summary>True while boost speed presentation (camera lines, etc.) should be active.</summary>
    public bool IsBoostPresentationActive => _boostPresentationActive;

    private bool _boostPresentationActive;

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
    private float _rawSteerVertical;
    private float _rawTrickStickX;
    private float _rawTrickStickY;
    private bool _trickStickActive;
    private bool _barrelRollHeld;

    private bool _trickSessionThisJump;
    private Vector3 _airborneLandingUpSnapshot = Vector3.up;
    private Vector3 _airborneLandingForwardSnapshot = Vector3.forward;

    public bool IsAirTricksHeld => _airborneForTricks && enableAirTricks && _trickStickActive;
    public bool IsAirborneForTricks => _airborneForTricks;
    public bool IsInAirTrickMode => IsAirTricksHeld;

    private bool _airborneForTricks;

    // Drift-held boost runtime (per-direction)
    private float _driftHoldTimeSeconds;        // accumulates while drifting with stable direction
    private int _driftHoldDirectionSign;        // +1/-1/0 current tracked direction
    private bool _driftWasActiveLastFrame;
    /// <summary>Banked hold time when release was blocked (crash lockout / post-crash) — retry until applied or cleared.</summary>
    private float _pendingDriftHeldBoostSeconds;
    private bool _hasPendingDriftHeldBoost;

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
    private float _postCrashRecoveryUntil;
    private const float PostCrashRecoverySeconds = 0.85f;
    /// <summary>Planar crawl cap right after upright — SoftClamp-to-road-cap preserved near-top-speed and felt like a boost.</summary>
    private const float PostCrashPlanarCrawlSpeed = 1.25f;

    /// <summary>
    /// After any crash, ignore boost pads/ramps until the car is off them for a frame.
    /// Stops "recover still on the pad I boosted on" from feeling like boost carry-over.
    /// </summary>
    private bool _boostSurfacesLockedUntilExitAfterCrash;
    /// <summary>Raw pad/ramp contact this sample (before post-crash suppress strips it).</summary>
    private bool _sampleDetectedBoostSurface;

    private bool IsPostCrashRecoveryDriving => Time.time < _postCrashRecoveryUntil;

    /// <summary>
    /// After crash / mash / reorient, ignore held boost AND held throttle until released once.
    /// Otherwise sideways recovery + held W/RT fires full accel (and drag cancel) the instant
    /// control returns — that reads as a leftover boost even when boost state was cleared.
    /// </summary>
    private bool _blockBoostUntilReleased;
    private bool _blockAccelUntilReleased;

    private bool IsDrivingGameplayLockedByCrash =>
        _inCrash || _isReorienting || _flipMashActive;

    private bool _onBoostSurface;
    /// <summary>True when the active boost surface is a Ramp (inclined). False for flat Boost pads.</summary>
    private bool _onBoostRamp;
    private bool _heldOnBoostRamp;
    private bool _wasOnBoostSurface;                 // for boost-pad popup edge detection
    private float _nextBoostPadPopupTime;            // re-trigger guard for boost-pad popup
    private const float BOOST_PAD_POPUP_COOLDOWN = 0.5f;
    /// <summary>Brief hold after last boost/ramp sample so edge flicker cannot slam the speed cap / kill climb.</summary>
    private float _boostSurfaceHoldUntil;
    private const float BoostSurfaceHoldSeconds = 0.18f;
    private float _heldBoostAccel;
    private float _heldBoostMaxSpeed;
    private bool _heldBoostDuringCrash;
    private float _heldBoostCrashMultiplier = 0.5f;
    /// <summary>Last solid ramp drive normal while on a boost ramp (used when briefly ungrounded mid-climb).</summary>
    private Vector3 _heldBoostDriveNormal = Vector3.up;
    private bool _hasHeldBoostDriveNormal;
    /// <summary>Used so ramp pad force can survive a 1–2 frame ground flicker without launching after lip exit.</summary>
    private float _lastBoostRampGroundedTime = -999f;
    /// <summary>Set during surface sampling when any hit is SurfaceType.Ramp.</summary>
    private bool _sampledBoostIsRamp;

    private bool _wasAffectedByIce;                  // for ice-path popup edge detection
    private float _nextIcePathPopupTime;             // re-trigger guard for ice-path popup
    private const float ICE_PATH_POPUP_COOLDOWN = 0.5f;

    [Header("Boost Pad Screen Bloom (boost pads / ramps only)")]
    [Tooltip("When entering a boost pad/ramp, pulse the screen bloom (same post-FX system used for close-call misses). Does NOT apply to drift boost.")]
    [SerializeField] private bool boostPadBloomBurst = true;
    [SerializeField, Min(0f)] private float boostPadBloomHold = 0.22f;
    [SerializeField, Min(0.01f)] private float boostPadBloomFadeIn = 0.06f;
    [SerializeField, Min(0.01f)] private float boostPadBloomFadeOut = 0.4f;
    private ForcefieldPostFXController _boostPadPostFX;
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

    private bool _rollingLogRamPending;
    private Vector3 _rollingLogPlanarUnit;
    private float _rollingLogHorizImpulse;
    private float _rollingLogUpImpulse;

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
    /// <summary>One-shot screen flash + Vintage TV death burst when driving ends from HP.</summary>
    private bool _unableToDrivePresentationPlayed;
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

    /// <summary>
    /// After any crash recovery (mash or upright), ignore boost-pad/ramp surface for this long so
    /// we resume at road stats — not leftover pad accel / inflated smoothed max speed.
    /// </summary>
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
            testAlwaysAccelerate = _movementConfig.TestAlwaysAccelerate;
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
            testAlwaysAccelerate = false;
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
            // Prefer climb assist on steep boost/ramps; flat pads already fail the min-angle check.
            slopeDriveAssistDisableOnBoost = false;
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
            reverseSteerEngageForwardSpeed = _steeringConfig.ReverseSteerEngageForwardSpeed;
            baseSteeringDamp = _steeringConfig.BaseSteeringDamp;
            enableSteerTraction = _steeringConfig.EnableSteerTraction;
            steerTractionReorientRate = _steeringConfig.SteerTractionReorientRate;
            steerRollingAccel = _steeringConfig.SteerRollingAccel;
            minSpeedForSteerTraction = _steeringConfig.MinSpeedForSteerTraction;
            lateralFrictionWhileSteering = _steeringConfig.LateralFrictionWhileSteering;
            steerTractionBlendIn = _steeringConfig.SteerTractionBlendIn;
            steerTractionBlendOut = _steeringConfig.SteerTractionBlendOut;
            steerTractionWhileAccelerating = _steeringConfig.SteerTractionWhileAccelerating;
            steerTractionWhileBraking = _steeringConfig.SteerTractionWhileBraking;
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
            invertSteeringWhenReversing = true; reverseSteerMultiplier = 1f; reverseSteerEngageForwardSpeed = 1.5f;
            baseSteeringDamp = 8f; enableSteerTraction = true; steerTractionReorientRate = 5.59f;
            steerRollingAccel = 2.25f; minSpeedForSteerTraction = 0.1f; lateralFrictionWhileSteering = 2.46f;
            steerTractionBlendIn = 3.2f; steerTractionBlendOut = 2.8f; steerTractionWhileAccelerating = 0.4f;
            steerTractionWhileBraking = 1f;
            steerRollingAccelCoastMultiplier = 0.441f;
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
            enableLandingLateralBleed = _landingConfig.EnableLandingLateralBleed;
            landingLateralBleedPerSecond = _landingConfig.LandingLateralBleedPerSecond;
            landingLateralBleedDuration = _landingConfig.LandingLateralBleedDuration;
            enableLandingSlipStraighten = _landingConfig.EnableLandingSlipStraighten;
            landingSlipStraightenDuration = _landingConfig.LandingSlipStraightenDuration;
            landingSlipCarryFraction = _landingConfig.LandingSlipCarryFraction;
            landingSlipMaxAlignDegrees = _landingConfig.LandingSlipMaxAlignDegrees;
            landingSlipLateralKeep = _landingConfig.LandingSlipLateralKeep;
            enableBadLandingCrash = _landingConfig.EnableBadLandingCrash;
            badLandingMinAirborneSeconds = _landingConfig.BadLandingMinAirborneSeconds;
            badLandingUpAlignDotMin = _landingConfig.BadLandingUpAlignDotMin;
            badLandingForwardNormalDotMax = _landingConfig.BadLandingForwardNormalDotMax;
            badLandingCrashSeverity = _landingConfig.BadLandingCrashSeverity;
            badLandingSpeedRetain = _landingConfig.BadLandingSpeedRetain;
            badLandingVerticalSpeedRetain = _landingConfig.BadLandingVerticalSpeedRetain;
            badLandingTumbleTorque = _landingConfig.BadLandingTumbleTorque;
            badLandingAngularSpeedRetain = _landingConfig.BadLandingAngularSpeedRetain;
            badLandingMaxAngularSpeed = _landingConfig.BadLandingMaxAngularSpeed;
        }
        else
        {
            skipSpeedClampWhileAirborne = true; enableLandingCarrySpeed = true;
            landingExcessBleedPerSecond = 7.17f; landingNoClampGraceSeconds = 0.35f;
            enableLandingBoost = true; landingBoostStrength = 1f; landingBoostDuration = 1.2f; landingBoostFalloff = 1.5f;
            enableLandingLateralBleed = false; landingLateralBleedPerSecond = 14f; landingLateralBleedDuration = 0.45f;
            enableLandingSlipStraighten = true; landingSlipStraightenDuration = 0.3f;
            landingSlipCarryFraction = 0f;
            landingSlipMaxAlignDegrees = 28f; landingSlipLateralKeep = 0.75f;
            enableBadLandingCrash = true;
            badLandingMinAirborneSeconds = 0.2f;
            badLandingUpAlignDotMin = 0.88f;
            badLandingForwardNormalDotMax = 0.72f;
            badLandingCrashSeverity = 0.42f;
            badLandingSpeedRetain = 0.32f;
            badLandingVerticalSpeedRetain = 0.08f;
            badLandingTumbleTorque = 5.5f;
            badLandingAngularSpeedRetain = 0.82f;
            badLandingMaxAngularSpeed = 10f;
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
            driftSideForceWhileAccelerating = _driftConfig.DriftSideForceWhileAccelerating;
            driftSideForceThrottleBlendRate = _driftConfig.DriftSideForceThrottleBlendRate;
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
            steerFlipGraceSeconds = _driftConfig.SteerFlipGraceSeconds;
            steerFlipRebuildDelay = _driftConfig.SteerFlipRebuildDelay;
            allowDriftGlideWithoutSteer = _driftConfig.AllowDriftGlideWithoutSteer;
            driftGlideDecayPerSecond = _driftConfig.DriftGlideDecayPerSecond;
            driftLandingFeelBlendSeconds = _driftConfig.DriftLandingFeelBlendSeconds;
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
            driftSideForce = 0.41f; driftSideForceWhileAccelerating = 0.35f; driftSideForceThrottleBlendRate = 4f;
            driftSpeedDecayPerSecond = 0.06f; driftHeldSpeedDecayPerSecond = 0f;
            driftForwardAccelMultiplier = 0f; useFullAccelWhileDrifting = true; lockToDriftPeakSpeed = true;
            driftBrakeDecayPerSecond = 0.001f; requireDirectionalInputForDriftCharge = false;
            driftNeutralDrainRate = 2.6f; driftNeutralFullResetDelay = 3.65f; resetDriftChargeOnSteerFlip = true;
            steerFlipRetainedCharge = 0.055f; steerFlipThreshold = 0.15f; minChargeForFlipReset = 0f;
            steerFlipGraceSeconds = 0.22f; steerFlipRebuildDelay = 0.1f; allowDriftGlideWithoutSteer = true; driftGlideDecayPerSecond = 0.15f;
            driftLandingFeelBlendSeconds = 0.38f;
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
            enableLowHpExtraFuelDrain = _fuelConfig.EnableLowHpExtraFuelDrain;
            extraFuelDrainPercentAtZeroHp = _fuelConfig.ExtraFuelDrainPercentAtZeroHp;
        }
        else
        {
            maxFuel = 100f; fuelUsePerSecondAtFullThrottle = 0f; fuelUsePerSecondBraking = 0f;
            idleFuelUsePerSecond = 0f; idleSpeedThreshold = 0.5f;
            enableLowHpExtraFuelDrain = true;
            extraFuelDrainPercentAtZeroHp = 100f;
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
            guaranteeMinimumThrottleAcceleration = _healthConfig.GuaranteeMinimumThrottleAcceleration;
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
            guaranteeMinimumThrottleAcceleration = true;
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
            steepGroundNormalBlendRate = _groundConfig.SteepGroundNormalBlendRate;
            steepGroundNormalDotThreshold = _groundConfig.SteepGroundNormalDotThreshold;
            boostSurfaceNormalBlendRate = _groundConfig.BoostSurfaceNormalBlendRate;
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
            steepGroundNormalBlendRate = 36f;
            steepGroundNormalDotThreshold = 0.97f;
            boostSurfaceNormalBlendRate = 28f;
            roadGrassTransitionLiftSpeed = 0f;
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
            steepAlignSpeedMultiplier = _rampConfig.SteepAlignSpeedMultiplier;
            steepAlignMinAngle = _rampConfig.SteepAlignMinAngle;
            rampVelocityRemapStrength = _rampConfig.RampVelocityRemapStrength;
            rampVelocityRemapMinAngle = _rampConfig.RampVelocityRemapMinAngle;
        }
        else
        {
            enableRampAlignment = true; groundAlignSpeed = 10f; airAlignSpeed = 6f;
            groundNormalCastRadius = 0.35f; groundNormalCheckDistance = 1.23f;
            landingPredictDistance = 2.75f; landingAlignStartDistance = 1.97f;
            steepAlignSpeedMultiplier = 2.1f;
            steepAlignMinAngle = 12f;
            rampVelocityRemapStrength = 0.72f;
            rampVelocityRemapMinAngle = 3.5f;
        }

        if (_airTrickConfig != null)
        {
            enableAirTricks = _airTrickConfig.EnableAirTricks;
            airTurnSpeedMultiplier = _airTrickConfig.AirTurnSpeedMultiplier;
            airDriftSteerFeel = _airTrickConfig.AirDriftSteerFeel;
            airDriftSideForceScale = _airTrickConfig.AirDriftSideForceScale;
            airYawRate = _airTrickConfig.AirYawRate;
            airYawInputDeadzone = _airTrickConfig.AirYawInputDeadzone;
            airVelocityFollowNose = _airTrickConfig.AirVelocityFollowNose;
            airVelocityFollowRate = _airTrickConfig.AirVelocityFollowRate;
            airVelocityFollowReturnSeconds = _airTrickConfig.AirVelocityFollowReturnSeconds;
            airTrajectorySteerRate = _airTrickConfig.AirTrajectorySteerRate;
            airSteerInputSmoothRate = _airTrickConfig.AirSteerInputSmoothRate;
            airSteerInputReleaseRate = _airTrickConfig.AirSteerInputReleaseRate;
            airTrajectoryBankAccel = _airTrickConfig.AirTrajectoryBankAccel;
            enableAirAimWhileTricking = _airTrickConfig.EnableAirAimWhileTricking;
            airAimWhileTrickingYawMult = _airTrickConfig.AirAimWhileTrickingYawMult;
            airAimHorizSpinYawMult = _airTrickConfig.AirAimHorizSpinYawMult;
            airTrajectoryWhileTrickingMult = _airTrickConfig.AirTrajectoryWhileTrickingMult;
            airTrajectoryHorizSpinMult = _airTrickConfig.AirTrajectoryHorizSpinMult;
            trickPitchRate = _airTrickConfig.TrickPitchRate;
            trickYawSpinRate = _airTrickConfig.TrickYawSpinRate;
            trickRollRate = _airTrickConfig.TrickRollRate;
            barrelModeBlendSpeed = _airTrickConfig.BarrelModeBlendSpeed;
            trickInputDeadzone = _airTrickConfig.TrickInputDeadzone;
            trickInputSmoothRate = _airTrickConfig.TrickInputSmoothRate;
            trickInputReleaseRate = _airTrickConfig.TrickInputReleaseRate;
            trickRotationAccel = _airTrickConfig.TrickRotationAccel;
            trickRotationDecel = _airTrickConfig.TrickRotationDecel;
            suppressRampAlignmentInTrickMode = _airTrickConfig.SuppressRampAlignmentInTrickMode;
            enableAirUprightRecovery = _airTrickConfig.EnableAirUprightRecovery;
            airUprightRecoverSpeed = _airTrickConfig.AirUprightRecoverSpeed;
            airUprightNearGroundBoost = _airTrickConfig.AirUprightNearGroundBoost;
            airUprightMinAlignDot = _airTrickConfig.AirUprightMinAlignDot;
            airGravityMultiplier = _airTrickConfig.AirGravityMultiplier;
        }
        else
        {
            enableAirTricks = true;
            airTurnSpeedMultiplier = 0.6f;
            airDriftSteerFeel = 0.72f;
            airDriftSideForceScale = 0.55f;
            airYawRate = 110f;
            airYawInputDeadzone = 0.12f;
            airVelocityFollowNose = 0.45f;
            airVelocityFollowRate = 3.5f;
            airVelocityFollowReturnSeconds = 0.18f;
            airTrajectorySteerRate = 0f;
            airSteerInputSmoothRate = 3.5f;
            airSteerInputReleaseRate = 4.5f;
            airTrajectoryBankAccel = 90f;
            enableAirAimWhileTricking = true;
            airAimWhileTrickingYawMult = 0.75f;
            airAimHorizSpinYawMult = 0.15f;
            airTrajectoryWhileTrickingMult = 1f;
            airTrajectoryHorizSpinMult = 0.55f;
            trickPitchRate = 195f;
            trickYawSpinRate = 220f;
            trickRollRate = 220f;
            barrelModeBlendSpeed = 2.2f;
            trickInputDeadzone = 0.15f;
            trickInputSmoothRate = 11f;
            trickInputReleaseRate = 15f;
            trickRotationAccel = 340f;
            trickRotationDecel = 240f;
            suppressRampAlignmentInTrickMode = true;
            enableAirUprightRecovery = true;
            airUprightRecoverSpeed = 2.75f;
            airUprightNearGroundBoost = 16f;
            airUprightMinAlignDot = 0.992f;
            airGravityMultiplier = 1.12f;
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
            enableDriveOnObstacleTops = _crashMashConfig.EnableDriveOnObstacleTops;
            obstacleTopNormalDotMin = _crashMashConfig.ObstacleTopNormalDotMin;
            obstacleTopCarUpDotMin = _crashMashConfig.ObstacleTopCarUpDotMin;
            driveableObstacleLayers = _crashMashConfig.DriveableObstacleLayers;
            obstacleTopLandingCoinReward = _crashMashConfig.ObstacleTopLandingCoinReward;
            obstacleTopLandingFuelReward = _crashMashConfig.ObstacleTopLandingFuelReward;
            obstacleTopLandingRewardCooldown = _crashMashConfig.ObstacleTopLandingRewardCooldown;
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
            requireCrashMashUnlock = _crashMashConfig.RequireCrashMashUnlock;
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
            requireCrashMashUnlock = true; // Default to skill-gated (mash disabled) when no config is present.
            enableDriveOnObstacleTops = true;
            obstacleTopNormalDotMin = 0.72f;
            obstacleTopCarUpDotMin = 0.55f;
            driveableObstacleLayers = 0;
            obstacleTopLandingCoinReward = 3;
            obstacleTopLandingFuelReward = 0f;
            obstacleTopLandingRewardCooldown = 2.5f;
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
        _airTrickConfig = GetComponent<CarAirTrickConfig>();
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
        _unableToDrivePresentationPlayed = false;
        _deathVfxPlayed = false;
        _runEndMashOffered = false;
        _isRunEndMash = false;
        _flipMashActive = false;
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

        crashMashUnlocked = !requireCrashMashUnlock;

        currentHP = Mathf.Max(1f, maxHP);

        groundCheckLayers = groundLayers;

        // Crash-layer tops count as ground so landing atop obstacles is driveable.
        if (enableDriveOnObstacleTops)
        {
            LayerMask tops = driveableObstacleLayers.value != 0 ? driveableObstacleLayers : crashLayers;
            groundLayers |= tops;
            groundCheckLayers |= tops;
        }

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
        UpdateCrashMashUnlock();
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

        HandleInput();

        if (IsDrivingGameplayLockedByCrash || IsPostCrashRecoveryDriving)
        {
            _boostRequested = false;
            ClearBoostOverride();
        }
        else if (GetBoostDown()
                 && Time.time >= _boostBlockedUntil
                 && !isOutOfFuel
                 && !isOutOfHP)
        {
            _boostRequested = true;
        }

        if (!_inCrash && hpRegenPerSecond > 0f && currentHP < maxHP)
        {
            currentHP = Mathf.Min(maxHP, currentHP + hpRegenPerSecond * Time.deltaTime);
        }

        if (currentHP <= 0f && !isOutOfHP)
            EnterOutOfHP(1f);

        bool brakeHeldNow = GetBrakeKeyOrTrigger();
        if (brakeHeldNow)
        {
            // Kills button / pad-style boost. Drift-held boost is earned on release and must not
            // be cancelled by LT/S feathering (common in air while still on throttle).
            bool driftBoostArmedOrActive = _overrideIsDriftBoost || _activeBoostIsDrift;
            if (!driftBoostArmedOrActive
                && (_boostRequested || _isBoosting || _isPostBoost || _boostOverrideActive))
                CancelAllBoostState(0f);
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
            if (carCollider != null)
            {
                ApplyHpDeathTerrainFromGlobalRay();
                ApplyBoostSurfaceForce(true);
                UpdateIcePhysicsTransitions();
                ApplyHpDeathSoftGroundGripAndCap();
            }
            else
                ResetIcePhysicsImmediate();

            TryStartRunEndMashIfReady();
            if (_flipMashActive)
            {
                UpdateFlipMashRecoveryFixedStep(Time.fixedDeltaTime);
                return;
            }

            ClearEndOfRunDrivingState();
            return;
        }

        // Out of fuel only: coast/slide with player yaw + steer traction (no throttle/boost).
        // Pitch/roll use real Rigidbody physics (ramps, landings) — do not freeze rotation.
        if (isOutOfFuel)
        {
            // Unlock pitch/roll physics (same hands-off rotation as HP death). Coast path sets drag.
            ReleaseHandsOffDrivingPhysics(resetDragToDefaults: false);
            if (carCollider != null)
            {
                SampleGroundAndUpdateMultipliers();
                RefreshSkillEffects();
                ApplySkillEffects();
                ApplyBoostSurfaceForce(true);
                UpdateIcePhysicsTransitions();
            }
            else
                ResetIcePhysicsImmediate();

            TryStartRunEndMashIfReady();
            if (_flipMashActive)
            {
                UpdateFlipMashRecoveryFixedStep(Time.fixedDeltaTime);
                return;
            }

            ClearEndOfRunDrivingState();
            bool deadStopped = BlocksSteeringWhenDeadAndStopped();
            if (!deadStopped)
            {
                UpdateSteeringInputFixed();
                _airborneForTricks = CanUseAirborneControls();
                HandleSteering();
                if (rb != null)
                    rb.MoveRotation(transform.rotation);
            }
            else
            {
                _rawSteer = 0f;
                steeringInput = 0f;
            }

            ApplyOutOfFuelCoastAndSteerTraction();
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
            UpdateFlipMashRecoveryFixedStep(Time.fixedDeltaTime);
            return;
        }

        if (_inCrash)
        {
            // IMPORTANT: Keep sampling ground even during crash (fixes ice sticking)
            SampleGroundAndUpdateMultipliers();

            // Do not apply boost-pad thrust while crashed — that redirects the fling along the nose.
            // Lerp physic material / ice handling toward what the rays hit this frame (crash used to skip this → ice forever).
            UpdateIcePhysicsTransitions();
            ApplyCrashSurfaceResistanceFromCurrentGround();

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

            // Never arm landing carry/boost while crashed — that re-inflated speed after mid-air hits.
            _landingExcessSpeed = 0f;
            _landingBoostTimeLeft = 0f;
            _landingBoostTargetMagnitude = 0f;

            if (!_wasGroundedLastFrame && _isGrounded)
                _lastLandedTime = Time.time;

            _wasGroundedLastFrame = _isGrounded;

            _crashTimer -= dt;

            // Safety cap only (preserves direction). No nose-align / surface speed snaps while crashed.
            ApplyCrashVelocityCaps();

            // Ride out the fling with no control until nearly stopped AND firmly grounded, then upright.
            // Velocity-only settle could fire mid-air (apex / snag) and leave the car reoriented with no control.
            if (IsCrashFlingSettled())
            {
                _inCrash = false;

                if (rb != null)
                {
                    rb.drag = _baseDrag;
                    rb.angularDrag = _baseAngularDrag;
                    rb.angularVelocity = Vector3.zero;
                }

                ArmCrashDrivingInputGates();
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

                // Wipe pad/ramp state + boosted surface max, then clamp planar speed to road cap
                // BEFORE upright reorient — otherwise pre-crash boost velocity rides through the whole upright.
                float boostIgnore = Mathf.Max(PostCrashRecoverySeconds, reorientDuration + PostCrashRecoverySeconds);
                ClearPostCrashBoostAndSurfaceState(boostIgnore);
                CancelAllBoostState(boostIgnore);
                _smoothedSurfaceMaxSpeed = -1f;
                ApplySurfaceMultipliers(1f, 1f, 1f, 1f);
                ApplySkillEffects();
                SoftClampPlanarSpeedToRoadCapAfterCrash();

                if (IsDeadForAutoUpright)
                {
                    // your end-run behavior
                    ForceStopCloseCallEffects();
                    return;
                }

                // ---- 2) Auto upright after crash (no mid-run mash) ----
                // Grounded was already required by IsCrashFlingSettled.
                if (NeedsUprightFlatten())
                {
                    StartReorientToFlat();
                    // If upright refused (e.g. left ground this frame), still restore drive lock —
                    // never leave freezeRotation=false from the crash tumble.
                    if (!_isReorienting)
                        FinishCrashDrivingRecovery();
                }
                else
                {
                    FinishCrashDrivingRecovery();
                }

                _groundedTime = 0f;
            }
            return;
        }

        var gm = gmEarly;

        UpdateCrashReorientation(dt);

        bool outOfFuel = IsOutOfFuel;
        UpdateLandingSpeedPreservation();
        if (_inCrash)
            return;

        SampleGroundAndUpdateMultipliers();
        HandleBoostSurfacePopup();        // boost pad / ramp popup on entry
        RefreshSkillEffects();
        ApplySkillEffects();
        UpdateSteeringInputFixed();
        // Sample + soft-blend once per FixedUpdate. HandleMovement must not re-sample
        // (that desynced measured vs smoothed and fed curb lips into drive/orient).
        RefreshGroundNormalForDriving(carCollider != null && CheckIfGrounded());
        SmoothDrivingGroundNormal();
        AssistOntoDrivePlane();
        ApplyDrivingOrientation(Time.fixedDeltaTime);
        _airborneForTricks = CanUseAirborneControls();
        HandleSteering();
        // Push left-stick air yaw onto the rigidbody before trick spins compose on top.
        if (_airborneForTricks && rb != null)
            rb.MoveRotation(transform.rotation);
        // Redirect travel toward the flat nose while turning/drifting in air (not while tricking).
        UpdateAirVelocityFollowBlend(Time.fixedDeltaTime);
        ApplyAirborneVelocityFollowNose(Time.fixedDeltaTime);
        HandleAirborneTricks(Time.fixedDeltaTime);
        UpdateAirTrickReleaseState(Time.fixedDeltaTime);
        HandleMovement();                 // coasting + existing decel logic still works
        ApplyAirborneGravity(Time.fixedDeltaTime);

        // Update centralized speed boost system (handles ramp-down for all boosts)
        UpdateSpeedBoosts(Time.fixedDeltaTime);

        if (!outOfFuel) HandleBoost();    // block boost when fuel is 0
        ApplyBoostSurfaceForce(false);    // Apply boost pad acceleration
        UpdateIcePhysicsTransitions();
        HandleIcePathPopup();             // ice path popup when ice handling kicks in
        ApplyGroundAwareSpeedCapClamp();
        // After all velocity writes — restore planar speed lost on grass→road / lip contacts.
        ApplyGrassToRoadTransitionSpeedPreserve();

        // Crawl-cap only while crashed / uprighting / short post-crash window.
        // Do NOT use full ShouldSuppressBoostSurfaces — pad-lock-until-exit would brick driving.
        if (_inCrash || _isReorienting || _flipMashActive || IsPostCrashRecoveryDriving)
            KillPlanarSpeedAfterCrashRecovery();
        else if (ShouldSuppressBoostSurfaces())
            SoftClampPlanarSpeedToRoadCapAfterCrash();

        if (rb != null)
        {
            Vector3 hv = rb.velocity;
            _planarSpeedLastFixedUpdate = new Vector3(hv.x, 0f, hv.z).magnitude;
        }

        _grassFractionPrevFrame = grassFraction;

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

        SyncBoostPresentationState();
    }

    private void StartReorientToFlat()
    {
        // Safety: never begin upright recovery while airborne.
        if (!CheckIfGrounded())
            return;

        _isReorienting = true;
        _reorientElapsed = 0f;

        ClearDriftStateForCrashOrReorient();
        ArmCrashDrivingInputGates();
        // Keep pad suppress armed for the whole upright + short post-drive window.
        ClearPostCrashBoostAndSurfaceState(reorientDuration + PostCrashRecoverySeconds);
        KillPlanarSpeedAfterCrashRecovery();

        RestoreDrivingRotationLock();

        _reorientStartRot = transform.rotation;
        Vector3 groundUp = SampleGroundUpForReorient();
        _reorientTargetRot = ComputeFlatRotationPreservingHorizontalForward(_reorientStartRot, groundUp);
    }

    /// <summary>
    /// Re-locks rotation for normal driving (scripted yaw/tilt). Crash tumbling sets <see cref="Rigidbody.freezeRotation"/> false.
    /// </summary>
    private void RestoreDrivingRotationLock()
    {
        if (rb == null) return;
        if (isOutOfFuel || isOutOfHP) return;

        rb.angularVelocity = Vector3.zero;
        rb.rotation = transform.rotation;
        rb.freezeRotation = true;
    }

    /// <summary>
    /// Called when the player regains control after a crash (upright or after reorient).
    /// Clears boost state and kills leftover planar speed so held-throttle recovery cannot rocket.
    /// </summary>
    private void FinishCrashDrivingRecovery()
    {
        if (rb == null) return;

        RestoreDrivingRotationLock();

        Vector3 groundUp = SampleGroundUpForReorient();
        _groundNormalMeasured = groundUp;
        _lastStableGroundNormal = groundUp;

        ArmCrashDrivingInputGates();
        // Match pad-ignore + post-crash recovery so X / drift boost cannot fire in the gap.
        CancelAllBoostState(PostCrashRecoverySeconds);
        _postCrashRecoveryUntil = Time.time + PostCrashRecoverySeconds;
        ClearPostCrashBoostAndSurfaceState();

        // Force road surface stats (1x) then skill-effective road cap — never clamp to a pad-inflated max.
        _smoothedSurfaceMaxSpeed = -1f;
        ApplySurfaceMultipliers(1f, 1f, 1f, 1f);
        ApplySkillEffects();
        KillPlanarSpeedAfterCrashRecovery();
    }

    /// <summary>
    /// Wipe every transient speed source that can survive a crash tumble:
    /// boost pads/ramps, landing carry/boost, slope-assist residual, and smoothed surface max.
    /// Button boost / drift boost / close-call are cleared via <see cref="CancelAllBoostState"/>.
    /// </summary>
    private void ClearPostCrashBoostAndSurfaceState(float ignoreBoostSeconds = -1f)
    {
        if (ignoreBoostSeconds < 0f)
            ignoreBoostSeconds = PostCrashRecoverySeconds;

        _postMashRecoveryIgnoreBoostUntil = Time.time + Mathf.Max(0f, ignoreBoostSeconds);
        _boostSurfacesLockedUntilExitAfterCrash = true;

        _onBoostSurface = false;
        _onBoostRamp = false;
        _hasHeldBoostDriveNormal = false;
        _currentBoostAccel = 0f;
        _currentBoostMaxSpeed = 0f;
        _currentBoostDuringCrash = false;
        _currentBoostCrashMultiplier = 0.5f;
        _boostSurfaceHoldUntil = 0f;
        _heldBoostAccel = 0f;
        _heldBoostMaxSpeed = 0f;
        _heldBoostDuringCrash = false;
        _heldBoostCrashMultiplier = 0.5f;
        _heldOnBoostRamp = false;
        _wasOnBoostSurface = false;

        _smoothedSurfaceMaxSpeed = -1f;
        _slopeAssistSmoothed = 0f;

        _landingExcessSpeed = 0f;
        _landingBoostTimeLeft = 0f;
        _landingBoostTargetMagnitude = 0f;
        _takeoffHorizSpeed = 0f;
        _takeoffHorizDir = Vector3.forward;
        _landingSlipStraightenLeft = 0f;
        _landingSlipStartDirValid = false;
    }

    /// <summary>
    /// Safety net: snap planar speed to the non-boosted road cap (skills + HP only).
    /// Must NOT use <see cref="effectiveMaxSpeed"/> if pad multipliers leaked into currentMaxSpeed.
    /// </summary>
    private void SoftClampPlanarSpeedToRoadCapAfterCrash()
    {
        if (rb == null) return;

        float cap = GetNonBoostedRoadSpeedCap();
        Vector3 v = rb.velocity;
        Vector3 planar = Vector3.ProjectOnPlane(v, Vector3.up);
        float speed = planar.magnitude;
        if (speed <= cap + 0.05f) return;

        planar *= cap / speed;
        rb.velocity = new Vector3(planar.x, v.y, planar.z);
    }

    /// <summary>
    /// After upright/mash recovery, force a crawl — SoftClamp-to-road-cap left near-top-speed
    /// sideways velocity which then combined with held throttle into a fake boost.
    /// </summary>
    private void KillPlanarSpeedAfterCrashRecovery()
    {
        if (rb == null) return;

        Vector3 v = rb.velocity;
        Vector3 planar = Vector3.ProjectOnPlane(v, Vector3.up);
        float speed = planar.magnitude;
        if (speed <= PostCrashPlanarCrawlSpeed)
            return;

        if (speed > 0.0001f)
            planar *= PostCrashPlanarCrawlSpeed / speed;
        else
            planar = Vector3.zero;

        rb.velocity = new Vector3(planar.x, v.y, planar.z);
    }

    /// <summary>
    /// Road top speed with skills / HP degradation — ignores boost pads, ramps, and active boosts.
    /// </summary>
    private float GetNonBoostedRoadSpeedCap()
    {
        float cap = Mathf.Max(0.5f, baseMaxSpeed);
        var mgr = RacingSkillTreeManager.Instance;
        if (mgr != null)
        {
            cap = mgr.ApplyStatChain(
                cap,
                SkillType.MaxSpeed_Add,
                SkillType.MaxSpeed_Mul);
        }

        if (maxHP > 0f)
        {
            float hpFrac = HPPercent;
            if (hpFrac < degradeStartHPFraction)
            {
                float t = Mathf.Clamp01(hpFrac / Mathf.Max(0.0001f, degradeStartHPFraction));
                float perfMul = Mathf.Lerp(performanceAtZeroHP, 1f, t);
                cap *= perfMul;
            }
        }

        return Mathf.Max(0.5f, cap);
    }

    private Vector3 SampleGroundUpForReorient()
    {
        if (carCollider == null) return Vector3.up;
        Vector3 castOrigin = carCollider.bounds.center + Vector3.up * 0.25f;
        if (TryGetGroundNormal(castOrigin, groundNormalCheckDistance, out RaycastHit hit))
            return hit.normal.normalized;
        if (_lastStableGroundNormal.sqrMagnitude > 1e-6f)
            return _lastStableGroundNormal.normalized;
        return Vector3.up;
    }

    /// <summary>
    /// Upright rotation that keeps the car's horizontal facing (not euler Y, which drifts when rolled/pitched).
    /// </summary>
    private static Quaternion ComputeFlatRotationPreservingHorizontalForward(Quaternion current, Vector3 up)
    {
        up = up.sqrMagnitude > 1e-6f ? up.normalized : Vector3.up;

        Vector3 fwd = current * Vector3.forward;
        Vector3 onPlane = Vector3.ProjectOnPlane(fwd, up);
        if (onPlane.sqrMagnitude < 1e-6f)
        {
            Vector3 right = current * Vector3.right;
            onPlane = Vector3.ProjectOnPlane(right, up);
        }

        if (onPlane.sqrMagnitude < 1e-6f)
            return Quaternion.LookRotation(Vector3.ProjectOnPlane(Vector3.forward, up).normalized, up);

        return Quaternion.LookRotation(onPlane.normalized, up);
    }

    private void CompleteCrashReorientation()
    {
        Vector3 groundUp = SampleGroundUpForReorient();
        _reorientTargetRot = ComputeFlatRotationPreservingHorizontalForward(_reorientTargetRot, groundUp);
        transform.rotation = _reorientTargetRot;

        if (rb != null)
            rb.rotation = _reorientTargetRot;

        _isReorienting = false;
        FinishCrashDrivingRecovery();
    }

    /// <summary>
    /// Spawns the boost-pad/ramp popup once when the car drives onto a boost surface
    /// (rising edge of <see cref="_onBoostSurface"/>), guarded by a short cooldown.
    /// </summary>
    private void HandleBoostSurfacePopup()
    {
        bool enteredBoostSurface = _onBoostSurface && !_wasOnBoostSurface;

        if (enteredBoostSurface && Time.time >= _nextBoostPadPopupTime)
        {
            if (enablePopupText && RacingPopups.IsReady)
                RacingPopups.BoostPad(GetPopupPosition());

            // Screen bloom pulse for boost pads/ramps ONLY (drift boost does not call this).
            TriggerBoostPadBloom();

            _nextBoostPadPopupTime = Time.time + BOOST_PAD_POPUP_COOLDOWN;
        }

        _wasOnBoostSurface = _onBoostSurface;
    }

    /// <summary>
    /// Spawns the ice-path popup once when the car drives onto ice and the ice handling actually starts
    /// affecting grip (rising edge), guarded by a short cooldown.
    /// </summary>
    private void HandleIcePathPopup()
    {
        // "Affected by ice handling" = on an ice surface whose handling has reduced grip below normal.
        bool affectedByIce = _onIceSurface && _currentIceHandling < 0.99f;

        if (affectedByIce && !_wasAffectedByIce && Time.time >= _nextIcePathPopupTime)
        {
            if (enablePopupText && RacingPopups.IsReady)
                RacingPopups.IcePath(GetPopupPosition());

            // Flash the screen like a close call: reuse the same chromatic + lens + bloom glow burst.
            GameManager_Racing.Instance?.PlayCloseCallStyleFXBurst();

            _nextIcePathPopupTime = Time.time + ICE_PATH_POPUP_COOLDOWN;
        }

        _wasAffectedByIce = affectedByIce;
    }

    /// <summary>
    /// Pulses screen bloom using the same post-FX controller as close-call misses, but bloom-only
    /// (no chromatic/lens distortion). Boost-pad/ramp specific - not used for drift boost.
    /// </summary>
    private void TriggerBoostPadBloom()
    {
        if (!boostPadBloomBurst) return;

        if (_boostPadPostFX == null)
            _boostPadPostFX = FindObjectOfType<ForcefieldPostFXController>();

        if (_boostPadPostFX != null)
            _boostPadPostFX.PlayBurstCustom(0f, 0f, boostPadBloomHold, boostPadBloomFadeIn, boostPadBloomFadeOut);
    }

    private void ApplyBoostSurfaceForce(bool duringCrashOrRecovery)
    {
        if (!_onBoostSurface) return;
        if (rb == null) return;

        // Upright / post-crash ignore: never apply leftover pad force while the player can't steer it.
        if (_isReorienting) return;
        if (IsPostCrashRecoveryDriving) return;
        if (ShouldSuppressBoostSurfaces()) return;
        if (_postMashRecoveryIgnoreBoostUntil > 0f && Time.time < _postMashRecoveryIgnoreBoostUntil)
            return;

        // Brake/reverse must win — pad accel used to keep shoving while the player tried to decelerate
        // (especially right after crash recovery onto/near a pad).
        if (!duringCrashOrRecovery && GetBrakeKeyOrTrigger())
            return;

        // Check if boost works during crash/recovery
        if (duringCrashOrRecovery && !_currentBoostDuringCrash) return;

        // Calculate boost strength
        float boostStrength = _currentBoostAccel;
        if (duringCrashOrRecovery)
        {
            boostStrength *= _currentBoostCrashMultiplier;
        }

        if (boostStrength <= 0f) return;

        float currentSpeed = rb.velocity.magnitude;

        // Hard pad/surface cap only. Never use (currentSpeed + epsilon) as the cap — that made
        // forceMult≈0 at entry speed, so gravity alone stalled climbs on steep boost ramps.
        // Also avoid GetCurrentSpeedCap's "floor at current speed" (that's for clamp safety only).
        float hardCap = _currentBoostMaxSpeed > 0f
            ? _currentBoostMaxSpeed
            : Mathf.Max(effectiveMaxSpeed, currentMaxSpeed);

        Vector3 forwardDir = transform.forward;
        Vector3 groundN = Vector3.up;
        bool groundedNow = carCollider != null && CheckIfGrounded();

        if (_onBoostRamp)
        {
            if (groundedNow)
                _lastBoostRampGroundedTime = Time.time;

            // Only push while planted (or a tiny mid-climb contact flicker). Surface-hold after
            // leaving the lip must NOT keep canceling gravity / shoving uphill — that was the
            // oversized launch. Cap hold still uses BoostSurfaceHoldSeconds separately.
            const float rampForceGroundGrace = 0.06f;
            bool rampForceActive = groundedNow
                || (Time.time - _lastBoostRampGroundedTime) <= rampForceGroundGrace;
            if (!rampForceActive)
                return;

            if (groundedNow && _lastStableGroundNormal.sqrMagnitude > 1e-8f)
            {
                groundN = _lastStableGroundNormal.normalized;
                if (groundN.y < FlatBoostNormalMinY)
                {
                    _heldBoostDriveNormal = groundN;
                    _hasHeldBoostDriveNormal = true;
                }
            }
            else if (_hasHeldBoostDriveNormal)
            {
                groundN = _heldBoostDriveNormal.normalized;
            }
            else if (_lastStableGroundNormal.sqrMagnitude > 1e-8f)
            {
                groundN = _lastStableGroundNormal.normalized;
            }

            forwardDir = Vector3.ProjectOnPlane(transform.forward, groundN);
            Vector3 downAlong = Vector3.ProjectOnPlane(Physics.gravity, groundN);
            Vector3 uphill = downAlong.sqrMagnitude > 1e-8f ? -downAlong.normalized : Vector3.zero;

            if (forwardDir.sqrMagnitude < 1e-8f)
            {
                forwardDir = uphill.sqrMagnitude > 1e-8f ? uphill : transform.forward;
            }
            else
            {
                forwardDir.Normalize();
                // Light uphill bias only — heavy blend + air push was launching off the lip.
                if (uphill.sqrMagnitude > 1e-8f && Vector3.Dot(forwardDir, uphill) < 0.15f)
                    forwardDir = Vector3.Slerp(forwardDir, uphill, 0.35f).normalized;
            }

            // Gravity cancel / dig fold only while actually grounded (not the grace air frames).
            if (groundedNow)
            {
                if (downAlong.sqrMagnitude > 1e-8f)
                    rb.AddForce(-downAlong, ForceMode.Acceleration);

                float into = Vector3.Dot(rb.velocity, groundN);
                if (into < -0.35f)
                    rb.velocity -= groundN * into;
            }
        }
        else if (groundedNow)
        {
            // Flat boost pad: keep push in the horizontal plane.
            groundN = Vector3.up;
            forwardDir = Vector3.ProjectOnPlane(transform.forward, groundN);
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

        // Taper near the real hard cap. Ramps keep a gentler taper so climbs don't stall,
        // without inflating the cap (that stacked exit speed into huge air time).
        float forceMult = 1f;
        if (hardCap > 0.01f)
        {
            float speedRatio = Mathf.Clamp01(currentSpeed / hardCap);
            if (_onBoostRamp)
            {
                if (speedRatio > 0.7f)
                    forceMult = Mathf.Lerp(1f, 0.4f, Mathf.Pow(Mathf.InverseLerp(0.7f, 1f, speedRatio), 2f));
                if (currentSpeed >= hardCap * 1.05f)
                    forceMult = 0f;
            }
            else
            {
                if (speedRatio > 0.55f)
                    forceMult = Mathf.Lerp(1f, 0.35f, Mathf.Pow(Mathf.InverseLerp(0.55f, 1f, speedRatio), 2f));
                if (currentSpeed >= hardCap * 1.02f)
                    forceMult = 0f;
            }
        }

        if (forceMult > 1e-4f)
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
        if (onIceOrTransitioning && !_inCrash && !_flipMashActive && !_isReorienting && !IsPostCrashRecoveryDriving && rb != null)
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

        if (IsDrivingGameplayLockedByCrash || IsCrashInvulnerable || IsPostCrashRecoveryDriving
            || Time.time < _boostBlockedUntil)
        {
            // Never preserve a mid-boost / drift-override through crash recovery — that was
            // re-applying pre-crash boost the moment upright finished.
            _boostRequested = false;
            ClearBoostOverride();
            if (_isBoosting || _isPostBoost || _boostOverrideActive || HasAnySpeedBoost)
                CancelAllBoostState(PostCrashRecoverySeconds);
            return;
        }

        // Decelerating: cancel button boost sustain so brake isn't fighting a rocket.
        // Drift-held boost is earned on release — do not cancel it with brake/LT.
        if (GetBrakeKeyOrTrigger()
            && !_overrideIsDriftBoost
            && !_activeBoostIsDrift
            && (_isBoosting || _boostRequested || _boostOverrideActive || _isPostBoost))
        {
            CancelAllBoostState(0f);
            return;
        }

        float dt = Time.fixedDeltaTime;

        // Separate cooldown timers
        if (_boostCooldownTimer > 0f)
            _boostCooldownTimer -= dt;
        if (_driftBoostCooldownTimer > 0f)
        {
            float prevDriftCd = _driftBoostCooldownTimer;
            _driftBoostCooldownTimer -= dt;
            if (_driftBoostCooldownTimer < 0f)
                _driftBoostCooldownTimer = 0f;
            // Cooldown just ended: wipe any hold built while waiting so charge starts at 0.
            if (prevDriftCd > 0f && _driftBoostCooldownTimer <= 0f)
                ClearDriftHeldBoostCharge();
        }

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

            // Per-type cooldown
            if (isDriftBoost)
                _driftBoostCooldownTimer = Mathf.Max(0.01f, driftBoostCooldown);
            else
                _boostCooldownTimer = Mathf.Max(0.01f, boostCooldown);

            _boostTimer = Mathf.Max(0f, isOverride ? _boostOverrideDuration : boostDuration);
            _isPostBoost = false;

            // Drift-held boost and button/skill boost use the same popup as boost pads/ramps.
            if (enablePopupText && RacingPopups.IsReady)
                RacingPopups.BoostPad(GetPopupPosition());

            // Drift boost: pad-style bloom + flash only — no close-call FX / slow-mo.
            if (isDriftBoost)
            {
                TriggerBoostPadBloom();
                ScreenFlashManager.Boost();
            }

            Debug.Log($"[CarController] Boost STARTED: drift={isDriftBoost}, impulse={impulseForce:F2}, sustain={sustain:F2}, duration={_boostTimer:F2}, maxMult={_activeBoostMaxMult:F2}");

            ClearBoostOverride();
        }

        if (rb != null)
        {
            if (!(skipSpeedClampWhileAirborne && !_isGrounded))
            {
                float clampStrength = GetLandingSpeedClampStrength01();
                if (clampStrength > 0.001f)
                {
                    float cap = GetCurrentSpeedCap();

                    Vector3 v = rb.velocity;
                    Vector3 horiz = Vector3.ProjectOnPlane(v, Vector3.up);
                    float horizSpeed = horiz.magnitude;

                    if (horizSpeed > cap && horizSpeed > 0.0001f)
                    {
                        float targetSpeed = Mathf.Lerp(horizSpeed, cap, clampStrength);
                        Vector3 horizClamped = horiz * (targetSpeed / horizSpeed);
                        rb.velocity = new Vector3(horizClamped.x, v.y, horizClamped.z);
                    }
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

        float result;
        // Handle post-boost ramp-down for legacy boosts (non-centralized)
        if (_isPostBoost && postBoostSlowdownDuration > 0f && !HasAnySpeedBoost)
        {
            float t = 1f - Mathf.Clamp01(_postBoostTimer / postBoostSlowdownDuration);
            result = Mathf.Lerp(boostedCap, normalCap, t);
        }
        else
        {
            bool hasAnyBoost = _isBoosting || HasAnySpeedBoost;
            result = hasAnyBoost ? boostedCap : normalCap;
        }

        // Boost pads / ramps: raise the clamp to the pad limit (if set) and never cut below
        // current travel speed on the surface. High entry speed used to get slammed to the lagging road cap.
        // Skip during crash / reorient / post-crash ignore — leftover high planar/tangent must not re-inflate the cap.
        bool ignoreBoostSurfaceCap = ShouldSuppressBoostSurfaces();
        if (_onBoostSurface && !ignoreBoostSurfaceCap)
        {
            float padCap = _currentBoostMaxSpeed > 0f ? _currentBoostMaxSpeed : 0f;
            result = Mathf.Max(result, normalCap, padCap);
            if (rb != null)
            {
                Vector3 planar = Vector3.ProjectOnPlane(rb.velocity, Vector3.up);
                result = Mathf.Max(result, planar.magnitude);

                // Steep ramps carry speed in the tangent plane (not just horizontal) — don't clamp that away.
                Vector3 n = _lastStableGroundNormal.sqrMagnitude > 1e-6f
                    ? _lastStableGroundNormal.normalized
                    : Vector3.up;
                float tanSpeed = Vector3.ProjectOnPlane(rb.velocity, n).magnitude;
                result = Mathf.Max(result, tanSpeed);
            }
        }

        return result;
    }

    /// <summary>
    /// 0 = full post-landing no-clamp grace, 1 = full speed clamp.
    /// Eases in over the last half of <see cref="landingNoClampGraceSeconds"/> so mid-turn
    /// landings do not cliff from floaty overspeed into a hard reshape.
    /// </summary>
    private float GetLandingSpeedClampStrength01()
    {
        if (landingNoClampGraceSeconds <= 0.0001f)
            return 1f;

        float age = Time.time - _lastLandedTime;
        if (age < 0f)
            return 1f;
        if (age >= landingNoClampGraceSeconds)
            return 1f;

        float fadeStart = landingNoClampGraceSeconds * 0.5f;
        if (age <= fadeStart)
            return 0f;

        float t = (age - fadeStart) / Mathf.Max(1e-4f, landingNoClampGraceSeconds - fadeStart);
        return Smooth01(t);
    }

    /// <summary>
    /// Caps speed in the ground tangent plane when grounded so ramp / slope driving cannot exceed <see cref="GetCurrentSpeedCap"/>
    /// while still passing a horizontal-only check. Airborne keeps the legacy horizontal clamp (preserves vertical motion).
    /// Call after movement, boost, boost pads, ice, and ramp alignment.
    /// </summary>
    private void ApplyGroundAwareSpeedCapClamp()
    {
        if (rb == null) return;

        float clampStrength = GetLandingSpeedClampStrength01();
        if (clampStrength <= 0.001f) return;
        if (skipSpeedClampWhileAirborne && !_isGrounded) return;

        float cap = GetCurrentSpeedCap();
        if (cap <= 0.001f) return;

        const float capSlack = 0.05f;
        bool groundedNow = CheckIfGrounded();
        Vector3 v = rb.velocity;

        if (groundedNow)
        {
            if (IsMixedGrassRoadSurface())
            {
                Vector3 horiz = Vector3.ProjectOnPlane(v, Vector3.up);
                float hs = horiz.magnitude;
                if (hs > cap + capSlack && hs > 1e-6f)
                {
                    float target = Mathf.Lerp(hs, cap, clampStrength);
                    horiz *= target / hs;
                    rb.velocity = new Vector3(horiz.x, v.y, horiz.z);
                }
                return;
            }

            Vector3 n = _lastStableGroundNormal.sqrMagnitude > 1e-6f ? _lastStableGroundNormal.normalized : Vector3.up;
            float upDot = Mathf.Abs(Vector3.Dot(n, Vector3.up));

            if (upDot < 0.1f)
            {
                Vector3 horiz = Vector3.ProjectOnPlane(v, Vector3.up);
                float hs = horiz.magnitude;
                if (hs > cap + capSlack && hs > 1e-6f)
                {
                    float target = Mathf.Lerp(hs, cap, clampStrength);
                    horiz *= target / hs;
                    rb.velocity = new Vector3(horiz.x, v.y, horiz.z);
                }
                return;
            }

            float vn = Vector3.Dot(v, n);
            Vector3 vTan = v - vn * n;
            float tanMag = vTan.magnitude;
            if (tanMag > cap + capSlack && tanMag > 1e-6f)
            {
                float target = Mathf.Lerp(tanMag, cap, clampStrength);
                vTan *= target / tanMag;
                rb.velocity = vTan + vn * n;
            }
        }
        else
        {
            Vector3 horiz = Vector3.ProjectOnPlane(v, Vector3.up);
            float horizSpeed = horiz.magnitude;
            if (horizSpeed > cap + capSlack && horizSpeed > 1e-6f)
            {
                float target = Mathf.Lerp(horizSpeed, cap, clampStrength);
                horiz *= target / horizSpeed;
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
            _onBoostRamp = false;
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
                    _onBoostRamp = surface.surfaceType == SurfaceType.Ramp;
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
        UpdateCrashMashUnlock();
    }

    private void HandleSkillsReset()
    {
        accelValue = maxSpeedValue = steerValue = fuelValue = 0f;
        accelMode = maxSpeedMode = steerMode = fuelMode = SkillApplicationMode.Additive;
        RefreshSkillEffects();
        ApplySkillEffects();
        UpdateDriftUnlock();
        UpdateBoostUnlock();
        UpdateCrashMashUnlock();
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

        // Out of fuel: free physics — steer only while still coasting; lock yaw once stopped.
        if (isOutOfFuel)
        {
            bool deadStopped = BlocksSteeringWhenDeadAndStopped();
            _rawSteer = deadStopped ? 0f : GetSteerRaw();
            _rawSteerVertical = GetSteerVerticalRaw();
            ReadTrickStickInputState();
            driftCharge = 0f;
            isDrifting = false;
            driftButtonHeld = false;
            _boostRequested = false;
            _boostOverrideActive = false;
            _suppressThrottleBrakeThisFrame = true;
            _suppressSteeringThisFrame = deadStopped;
            if (deadStopped)
                steeringInput = 0f;
            _inputsSuppressedThisFrame = true;
            return;
        }

        if (_malfunctionTimer > 0f)
            _malfunctionTimer -= Time.deltaTime;
        if (_malfunctionCooldownRemain > 0f)
            _malfunctionCooldownRemain -= Time.deltaTime;

        _rawSteer = GetSteerRaw();
        _rawSteerVertical = GetSteerVerticalRaw();
        ReadTrickStickInputState();
        float rawHorizontal = _rawSteer; // keep the rest of your logic working
        float speed = rb != null ? rb.velocity.magnitude : 0f;
        bool prevDriftKeyHeld = driftButtonHeld;
        driftButtonHeld = GetDriftHeld();

        // NEW: starting a fresh drift-hold clears the "crash killed charge" gate
        if (driftButtonHeld && !prevDriftKeyHeld)
        {
            _crashKilledDriftHeldBoost = false;
        }

        bool wasDrifting = isDrifting;
        int prevHoldDirectionSign = _driftHoldDirectionSign;

        // Crash / upright recovery / mash: no driving gameplay input (accel, brake, boost, drift).
        if (_inCrash || _isReorienting || _flipMashActive)
        {
            ClearDriftStateForCrashOrReorient();
            ArmCrashDrivingInputGates();
            steeringInput = 0f;
            _rawSteer = 0f;
            _boostRequested = false;
            _boostOverrideActive = false;
            _suppressThrottleBrakeThisFrame = true;
            _suppressSteeringThisFrame = true;
            _inputsSuppressedThisFrame = true;
            return;
        }

        if (!driftUnlocked)
        {
            driftCharge = 0f;
            isDrifting = false;
            _driftCurrentSteerSign = 0;
            _driftSteerFlipPendingTimer = 0f;
        }
        else
        {
            // Hysteresis: once charge/drift is live, tolerate a brief post-crash speed dip
            // so isDrifting / the hold bar / camera don't stutter-reset while still holding.
            float driftSpeedGate = driftMinSpeed;
            if (driftButtonHeld && (wasDrifting || driftCharge > 0.01f))
                driftSpeedGate = driftMinSpeed * 0.72f;

            bool canDriftThisFrame = driftButtonHeld && speed >= driftSpeedGate;

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
                        _driftSteerFlipPendingTimer = 0f;

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
                    bool oppositeSteer =
                        resetDriftChargeOnSteerFlip &&
                        _driftCurrentSteerSign != 0 &&
                        currentSign != _driftCurrentSteerSign &&
                        driftCharge >= minChargeForFlipReset &&
                        Time.time >= _driftFlipBlockUntil;

                    if (oppositeSteer)
                    {
                        // Hold opposite briefly before cutting charge — flicks shouldn't dump it.
                        _driftSteerFlipPendingTimer += Time.deltaTime;
                        float grace = Mathf.Max(0f, steerFlipGraceSeconds);
                        if (_driftSteerFlipPendingTimer >= grace)
                        {
                            driftCharge *= steerFlipRetainedCharge;
                            _driftFlipBlockUntil = Time.time + steerFlipRebuildDelay;
                            _driftSteerFlipPendingTimer = 0f;
                            _driftCurrentSteerSign = currentSign;

                            if (enableDriftHeldBoost)
                                ResetDriftHeldTimer();
                        }
                        // else: keep established sign until grace expires
                    }
                    else
                    {
                        _driftSteerFlipPendingTimer = 0f;
                        _driftCurrentSteerSign = currentSign;
                    }
                }
                else
                {
                    // Neutral stick: cancel pending flip; keep last sign until full neutral reset.
                    _driftSteerFlipPendingTimer = 0f;
                }
            }
            else
            {
                // Not holding drift at all: clear steer sign
                _driftCurrentSteerSign = 0;
                _driftSteerFlipPendingTimer = 0f;
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
            if (Time.time < _driftFlipBlockUntil && driftButtonHeld && speed >= driftSpeedGate)
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
                if (_inCrash || isOutOfHP || isOutOfFuel)
                {
                    ClearDriftHeldBoostCharge();
                }
                else if (_driftBoostCooldownTimer > 0f)
                {
                    // On cooldown: never bank hold time — bar must rebuild from 0 after CD ends.
                    ClearDriftHeldBoostCharge();
                }
                else
                {
                    // Build while drift + steer are held — braking/decel does not wipe charge.
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

                    // Trigger boost ONLY on drift key release. Also retry a banked pending boost.
                    if (!driftButtonHeld && prevDriftKeyHeld)
                    {
                        TryTriggerDriftHeldBoost();
                    }
                    else if (_hasPendingDriftHeldBoost && !driftButtonHeld)
                    {
                        TryTriggerDriftHeldBoost();
                    }

                    // Hard reset direction tracker if not holding drift at all
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

                // Only reset held-boost on a fresh press. Charge can blip to zero while the
                // button stays held (post-crash accel, flip rebuild, neutral) — wiping the
                // bar/timer there causes the stutter-step the camera also picks up.
                if (enableDriftHeldBoost)
                {
                    if (!prevDriftKeyHeld)
                    {
                        ResetDriftHeldTimer();
                        _driftHoldDirectionSign = _driftCurrentSteerSign;
                    }
                    else if (_driftHoldDirectionSign == 0)
                    {
                        _driftHoldDirectionSign = _driftCurrentSteerSign;
                    }
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

        if (allowDriftGlideWithoutSteer && driftUnlocked && !_inCrash && !_isReorienting && !IsPostCrashRecoveryDriving)
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

            if (isMalfunctioning)
                _currentMalfunctionMultiplier = malfunctionThrottleMultiplier;

            // Never zero throttle from malfunction — only reduce via multiplier (clamped in GetThrottleAcceleration).
        }

        // Malfunction must not block steering or throttle input.
        _suppressThrottleBrakeThisFrame = false;
        _suppressSteeringThisFrame = false;

        _inputsSuppressedThisFrame = _suppressThrottleBrakeThisFrame;
    }

    public void SetExternalInputLock(bool locked)
    {
        _externalInputLocked = locked;
    }

    /// <summary>True while spawn / loading / cutscenes hold car input (drift reads false).</summary>
    public bool IsExternalInputLocked => _externalInputLocked;

    /// <summary>
    /// Finish-portal sequence: immune to crashes; obstacles get forcefield-launched instead.
    /// </summary>
    public void SetFinishPortalCrashShield(bool on)
    {
        if (_forcefield == null)
            _forcefield = GetComponent<CarForcefield>();
        if (_forcefield != null)
            _forcefield.SetFinishPortalShield(on);
    }

    public bool IsFinishPortalCrashShieldActive =>
        _forcefield != null && _forcefield.IsFinishPortalShield;

    private void TryTriggerDriftHeldBoost()
    {
        if (!enableDriftHeldBoost) return;

        float held = Mathf.Max(_driftHoldTimeSeconds, _pendingDriftHeldBoostSeconds);

        // Crash wiped charge — require a fresh drift press before release can boost.
        if (_crashKilledDriftHeldBoost)
        {
            ClearDriftHeldBoostCharge();
            return;
        }

        // Still cooling down — discard so a later release cannot use pre-CD charge.
        if (_driftBoostCooldownTimer > 0f)
        {
            ClearDriftHeldBoostCharge();
            return;
        }

        if (isOutOfHP || isOutOfFuel)
        {
            ClearDriftHeldBoostCharge();
            return;
        }

        // Temporary lockouts / post-crash: never bank a pre-crash drift boost to fire on recover.
        if (_inCrash || _isReorienting || _flipMashActive || IsPostCrashRecoveryDriving
            || Time.time < _boostBlockedUntil
            || _crashKilledDriftHeldBoost)
        {
            ClearDriftHeldBoostCharge();
            ResetDriftHeldTimer();
            return;
        }

        if (_inputsSuppressedThisFrame || _suppressThrottleBrakeThisFrame)
        {
            ClearDriftHeldBoostCharge();
            ResetDriftHeldTimer();
            return;
        }

        ResetDriftHeldTimer();

        if (held < driftBoostMinHoldSeconds)
        {
            _pendingDriftHeldBoostSeconds = 0f;
            _hasPendingDriftHeldBoost = false;
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
            _pendingDriftHeldBoostSeconds = 0f;
            _hasPendingDriftHeldBoost = false;
            return;
        }

        if (mgr != null)
        {
            force = mgr.GetDriftHeldBoostForceScaled(force);
            duration = mgr.GetDriftHeldBoostDurationScaled(duration);
            maxMult = mgr.GetDriftHeldBoostMaxSpeedMultScaled(maxMult);
        }

        _pendingDriftHeldBoostSeconds = 0f;
        _hasPendingDriftHeldBoost = false;

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

    private void ClearDriftHeldBoostCharge()
    {
        ResetDriftHeldTimer();
        _pendingDriftHeldBoostSeconds = 0f;
        _hasPendingDriftHeldBoost = false;
    }

    /// <summary>
    /// Clears active drift, glide, and held-boost charge so crash / reorient cannot keep
    /// drift physics, camera tilt, or the boost charge bar alive.
    /// </summary>
    private void ClearDriftStateForCrashOrReorient()
    {
        driftButtonHeld = false;
        isDrifting = false;
        driftCharge = 0f;
        _driftCurrentSteerSign = 0;
        _driftSteerFlipPendingTimer = 0f;
        _driftGlideActive = false;
        _driftWasActiveLastFrame = false;
        driftEntrySpeed = 0f;
        driftClampSpeed = 0f;
        driftPeakSpeed = 0f;
        _driftGroundFeel01 = 1f;
        _driftSideForceThrottle01 = 1f;
        ClearDriftHeldBoostCharge();
    }

    /// <summary>
    /// Clear queued boost and require a fresh boost + accelerate press after crash recovery.
    /// Held throttle through upright while sideways was launching the car like a boost.
    /// </summary>
    private void ArmCrashDrivingInputGates()
    {
        _blockBoostUntilReleased = true;
        _blockAccelUntilReleased = true;
        _boostRequested = false;
        ClearBoostOverride();
    }

    /// <summary>Eased 0–1 ground-drift feel (smoothstep). Used by physics and camera.</summary>
    private float GetDriftGroundFeelEased()
    {
        float t = Mathf.Clamp01(_driftGroundFeel01);
        return t * t * (3f - 2f * t);
    }

    private void UpdateDriftGroundFeel(bool groundedNow, float dt)
    {
        if (!groundedNow)
        {
            _driftGroundFeel01 = 0f;
            return;
        }

        bool driftActive = isDrifting || _driftGlideActive;
        if (!driftActive)
        {
            // Armed so an on-ground drift start gets full feel immediately.
            _driftGroundFeel01 = 1f;
            return;
        }

        if (driftLandingFeelBlendSeconds <= 0.0001f)
        {
            _driftGroundFeel01 = 1f;
            return;
        }

        float rate = 1f / driftLandingFeelBlendSeconds;
        _driftGroundFeel01 = Mathf.MoveTowards(_driftGroundFeel01, 1f, rate * dt);
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
    /// HP hit zero — stop driving and play the full death-hit presentation (damage flash + CRT burst/color).
    /// Safe to call repeatedly; presentation is one-shot.
    /// </summary>
    private void EnterOutOfHP(float presentationSeverity01)
    {
        if (isOutOfHP)
        {
            // Already dead — still ensure presentation if a prior path skipped it.
            PlayUnableToDriveCrashPresentation(presentationSeverity01);
            return;
        }

        isOutOfHP = true;
        ForceStopCloseCallEffects();
        _isBoosting = false;
        _isPostBoost = false;
        _boostRequested = false;

        PlayDeathVFX();
        PlayUnableToDriveCrashPresentation(presentationSeverity01);
    }

    /// <summary>
    /// Crash-intensity screen flash + Vintage TV burst/color flip for the hit that ends driving.
    /// </summary>
    private void PlayUnableToDriveCrashPresentation(float severity01)
    {
        if (_unableToDrivePresentationPlayed)
            return;
        _unableToDrivePresentationPlayed = true;

        float sev = Mathf.Clamp01(severity01);
        if (sev < 0.35f)
            sev = 1f;

        ScreenFlashManager.Damage();

        try { OnCrash?.Invoke(sev); } catch { /* ignore listener errors */ }
        try { OnLethalCrash?.Invoke(sev); } catch { /* ignore listener errors */ }

        var gm = GameManager_Racing.Instance;
        if (gm != null)
            gm.OnCarCrashLethal(sev);

        // Same CRT burst + color flip used when the car finally stops — fire it on the killing blow.
        var tv = FindObjectOfType<VintageTVController>(true);
        tv?.TriggerDeathExplosionColorFlip();
    }

    private void NotifyCrashFeedbackOnly(float severity01)
    {
        float sev = Mathf.Clamp01(severity01);
        try { OnCrash?.Invoke(sev); } catch { /* ignore listener errors */ }
    }

    /// <summary>
    /// First empty tank: stretch the Vintage TV crash flare longer than a normal hit.
    /// </summary>
    private void NotifyOutOfFuelTvFlare()
    {
        var tv = FindObjectOfType<VintageTVController>(true);
        tv?.TriggerOutOfFuelFlare();
        // Keep other OnCrash listeners (camera boost VFX, etc.).
        NotifyCrashFeedbackOnly(0.9f);
    }

    private void TriggerCrash(
        Vector3 hitDirection,
        float crashDuration,
        float impulseMagnitude,
        float torqueMagnitude,
        float severity,
        Vector3 contactPointWS,
        bool applyDamage,
        bool softLandingCrash = false,
        Vector3 softLandingCarUp = default,
        Vector3 softLandingGroundNormal = default)
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
        // Pads / landing carry / smoothed max speed — keep ignore armed through tumble + upright.
        ClearPostCrashBoostAndSurfaceState(crashDuration + reorientDuration + PostCrashRecoverySeconds);
        // Mid-air / post-ramp crashes must not keep landing boost for the eventual touchdown.
        _blockLandingBoostUntilNextTakeoff = true;
        _crashKilledDriftHeldBoost = true;
        // NEW: also prevent drift-held boost from “arming” during crash sequences
        ClearDriftStateForCrashOrReorient();
        ArmCrashDrivingInputGates();
        _boostOverrideActive = false;
        _overrideIsDriftBoost = false;

        // Clamp severity once and reuse
        float sev01 = Mathf.Clamp01(severity);

        if (!softLandingCrash)
        {
            // Flatten & normalize incoming hit direction
            hitDirection.y = 0f;
            if (hitDirection.sqrMagnitude < 0.0001f)
                hitDirection = -transform.forward;
            hitDirection.Normalize();
        }
        else if (hitDirection.sqrMagnitude > 0.0001f)
        {
            hitDirection.Normalize();
        }
        else
        {
            hitDirection = Vector3.up;
        }

        _inCrash = true;
        _crashTimer = crashDuration;

        // Store crash info for recovery calculation
        _lastCrashSeverity = Mathf.Clamp01(severity);
        _crashCount++; // counts all crashes for scaling

        // Single hook for all crash entry points (collisions, external damage, bounce-back, etc.)
        try { OnCrash?.Invoke(sev01); } catch { /* ignore listener errors */ }

        // Snapshot situational flags BEFORE we force grounded false.
        _wasAirborneDuringCrash = softLandingCrash || !_isGrounded;
        bool flippedAtImpact = NeedsFlipRecovery();

        float severityContribution = _lastCrashSeverity;

        if (_wasAirborneDuringCrash) severityContribution *= airborneClickMultiplier;
        if (flippedAtImpact) severityContribution *= flippedClickMultiplier;

        _crashSeveritySum += severityContribution;

        _groundedTime = 0f;
        _isGrounded = false;

        rb.drag = _baseDrag * crashDragMultiplier;
        rb.angularDrag = crashAngularDrag;

        if (softLandingCrash)
        {
            // Bad landing: physics tumble and speed bleed — no frozen rotation, no launch impulse.
            rb.freezeRotation = false;

            Vector3 groundN = softLandingGroundNormal.sqrMagnitude > 1e-8f
                ? softLandingGroundNormal.normalized
                : (hitDirection.sqrMagnitude > 1e-8f ? hitDirection.normalized : Vector3.up);
            Vector3 carUp = softLandingCarUp.sqrMagnitude > 1e-8f
                ? softLandingCarUp.normalized
                : transform.up;

            float upAlign = Mathf.Clamp01(Vector3.Dot(carUp, groundN));
            float misalign = 1f - upAlign;
            float impactSpeed = rb.velocity.magnitude;

            Vector3 vSoft = rb.velocity;
            Vector3 slide = Vector3.ProjectOnPlane(vSoft, groundN);
            slide *= badLandingSpeedRetain;

            float intoGround = Vector3.Dot(vSoft, groundN);
            float settledNormal = intoGround <= 0f
                ? intoGround * badLandingVerticalSpeedRetain
                : -intoGround * badLandingVerticalSpeedRetain * 0.35f;
            rb.velocity = slide + groundN * settledNormal;

            Vector3 angVel = rb.angularVelocity * Mathf.Lerp(badLandingAngularSpeedRetain, 1f, misalign);

            Vector3 correctionAxis = Vector3.Cross(carUp, groundN);
            if (correctionAxis.sqrMagnitude > 1e-8f)
            {
                correctionAxis.Normalize();
                float tumble = badLandingTumbleTorque * misalign * Mathf.Lerp(1f, 2.2f, sev01);
                tumble *= Mathf.Clamp(impactSpeed * 0.12f, 0.75f, 4f);
                angVel += correctionAxis * tumble;
            }

            Vector3 fwd = transform.forward;
            float noseIntoGround = Mathf.Abs(Vector3.Dot(fwd, groundN));
            if (noseIntoGround > badLandingForwardNormalDotMax * 0.5f)
            {
                Vector3 fwdOnPlane = Vector3.ProjectOnPlane(fwd, groundN);
                if (fwdOnPlane.sqrMagnitude > 1e-8f)
                {
                    Vector3 pitchAxis = Vector3.Cross(fwdOnPlane.normalized, groundN);
                    if (pitchAxis.sqrMagnitude > 1e-8f)
                    {
                        float sign = Mathf.Sign(Vector3.Dot(fwd, groundN));
                        angVel += pitchAxis.normalized * (noseIntoGround * impactSpeed * 0.06f * sign);
                    }
                }
            }

            float maxAng = badLandingMaxAngularSpeed;
            if (angVel.sqrMagnitude > maxAng * maxAng)
                angVel = angVel.normalized * maxAng;
            rb.angularVelocity = angVel;

            rb.angularDrag = Mathf.Max(crashAngularDrag, _baseAngularDrag * 2.5f);
            ApplyCrashVelocityCaps();

            _landingBoostTimeLeft = 0f;
            _landingBoostTargetMagnitude = 0f;
            _landingExcessSpeed = 0f;
            _takeoffHorizSpeed = 0f;
        }
        else
        {
            rb.freezeRotation = false;

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

            if (_rollingLogRamPending)
            {
                float invM = 1f / Mathf.Max(rb.mass, 0.01f);
                rb.velocity += _rollingLogPlanarUnit * (_rollingLogHorizImpulse * invM)
                    + Vector3.up * (_rollingLogUpImpulse * invM);
                _rollingLogRamPending = false;
            }

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
        }

        // Damage / fuel handling
        float sev01ForDamage = sev01;

        if (applyDamage)
        {
            bool lethalFromThisCrash = ApplyCrashHpAndFuelFromSeverity(sev01ForDamage);

            if (RacingPopups.IsReady)
                RacingPopups.Crash(_lastCrashSeverity, GetPopupPosition());

            if (lethalFromThisCrash)
            {
                if (currentHP <= 0f)
                {
                    EnterOutOfHP(sev01ForDamage);
                }
                else if (currentFuel <= 0f && !isOutOfFuel)
                {
                    isOutOfFuel = true;
                    NotifyOutOfFuelTvFlare();
                }
            }

            // Start cooldown AFTER damage
            _nextCrashAllowedTime = Time.time + crashDamageCooldown;
        }
        else
        {
            Debug.Log($"[CarController] Crash occurred but damage skipped (cooldown active, {Mathf.Max(0f, _nextCrashAllowedTime - Time.time):F2}s remain).");
        }


    }

    private bool CanUseAirborneControls()
    {
        if (!enableAirTricks || rb == null) return false;
        if (_inCrash || _isReorienting || _flipMashActive || IsPostCrashRecoveryDriving) return false;
        if (IsCrashInvulnerable || IsDeadForMashRecovery) return false;
        return !CheckIfGrounded();
    }

    private void ResetAirTrickRotationState()
    {
        _smoothedTrickPitch = 0f;
        _smoothedTrickHoriz = 0f;
        _trickPitchAngularVel = 0f;
        _trickYawAngularVel = 0f;
        _trickRollAngularVel = 0f;
        _barrelModeBlend = 0f;
        _trickBarrelModeActive = false;
        _smoothedAirSteer = 0f;
        _airTrajectoryBankRate = 0f;
        _airVelocityFollowBlend01 = 1f;
    }

    private float SmoothAirSteerInput(float rawSteer, float dt)
    {
        float rate = Mathf.Abs(rawSteer) > Mathf.Abs(_smoothedAirSteer)
            ? airSteerInputSmoothRate
            : airSteerInputReleaseRate;
        return Mathf.MoveTowards(_smoothedAirSteer, rawSteer, rate * dt);
    }

    private static float RawSteerFromAxis(float axis, float deadzone)
    {
        if (Mathf.Abs(axis) <= deadzone)
            return 0f;
        return Mathf.Sign(axis) * Mathf.InverseLerp(deadzone, 1f, Mathf.Abs(axis));
    }

    private float SmoothTrickAxis(float current, float target, float dt)
    {
        float rate = Mathf.Abs(target) > Mathf.Abs(current) ? trickInputSmoothRate : trickInputReleaseRate;
        return Mathf.MoveTowards(current, target, rate * dt);
    }

    private float MoveTrickAngularVel(float current, float target, float dt, float decelMultiplier = 1f)
    {
        if (Mathf.Approximately(current, target))
            return target;

        float rate = Mathf.Abs(target) > 0.01f
            ? trickRotationAccel
            : trickRotationDecel * decelMultiplier;
        return Mathf.MoveTowards(current, target, rate * dt);
    }

    private void ReadTrickStickInputState()
    {
        _rawTrickStickX = GetTrickStickXRaw();
        _rawTrickStickY = GetTrickStickYRaw();
        _barrelRollHeld = GetBarrelRollHeld();
        _trickStickActive = ComputeTrickStickActive();
    }

    private static float ApplyAxisDeadzone(float value, float deadzone)
    {
        float abs = Mathf.Abs(value);
        if (abs < deadzone) return 0f;
        return Mathf.Sign(value) * Mathf.InverseLerp(deadzone, 1f, abs);
    }

    /// <summary>
    /// Right stick X/Y deadzones. Pitch from stick Y / arrow keys — not throttle or brake.
    /// </summary>
    private void ResolveTrickStickInput(out float horiz, out float pitch)
    {
        horiz = ApplyAxisDeadzone(_rawTrickStickX, trickInputDeadzone);
        pitch = ApplyAxisDeadzone(_rawTrickStickY, trickInputDeadzone);
    }

    /// <summary>
    /// Stick X for horizontal spin targets, or inferred from coasting yaw/roll when stick is neutral.
    /// </summary>
    private float GetEffectiveHorizSpinInput()
    {
        if (Mathf.Abs(_smoothedTrickHoriz) > 0.05f)
            return _smoothedTrickHoriz;

        if (Mathf.Abs(_trickYawAngularVel) >= Mathf.Abs(_trickRollAngularVel) && Mathf.Abs(_trickYawAngularVel) > 1f)
            return Mathf.Clamp(_trickYawAngularVel / Mathf.Max(trickYawSpinRate, 1f), -1f, 1f);

        if (Mathf.Abs(_trickRollAngularVel) > 1f)
            return Mathf.Clamp(-_trickRollAngularVel / Mathf.Max(trickRollRate, 1f), -1f, 1f);

        return 0f;
    }

    /// <summary>
    /// 0 = pitch-heavy trick, 1 = horizontal spin / barrel roll dominates.
    /// </summary>
    private float GetTrickHorizSpinInfluence()
    {
        float horizSpin = Mathf.Max(Mathf.Abs(_smoothedTrickHoriz), Mathf.Abs(GetEffectiveHorizSpinInput()));
        float pitchInput = Mathf.Abs(_smoothedTrickPitch);
        float influence = horizSpin / Mathf.Max(horizSpin + pitchInput, 0.05f);
        return Mathf.Clamp01(Mathf.Max(influence, _barrelModeBlend * 0.9f));
    }

    private float GetTrickAirYawAimMultiplier()
    {
        if (!_trickStickActive)
            return 1f;

        float horizInfluence = GetTrickHorizSpinInfluence();
        return Mathf.Lerp(airAimWhileTrickingYawMult, airAimHorizSpinYawMult, horizInfluence);
    }

    private float GetTrickAirTrajectoryMultiplier()
    {
        if (!_trickStickActive)
            return 1f;

        float horizInfluence = GetTrickHorizSpinInfluence();
        return Mathf.Lerp(airTrajectoryWhileTrickingMult, airTrajectoryHorizSpinMult, horizInfluence);
    }

    /// <summary>
    /// Eases nose-follow travel strength back in after tricks so releasing the trick stick
    /// does not suddenly yank the flight path onto the nose.
    /// </summary>
    private void UpdateAirVelocityFollowBlend(float dt)
    {
        if (!_airborneForTricks)
        {
            _airVelocityFollowBlend01 = 1f;
            return;
        }

        // Hold at 0 only while the trick stick is held. Residual flip spin no longer delays
        // nose-follow — upright recovery still waits separately via ShouldSuppressAirUprightForTricks.
        if (_trickStickActive)
        {
            _airVelocityFollowBlend01 = 0f;
            return;
        }

        if (airVelocityFollowReturnSeconds <= 0.0001f)
        {
            _airVelocityFollowBlend01 = 1f;
            return;
        }

        float rate = 1f / airVelocityFollowReturnSeconds;
        _airVelocityFollowBlend01 = Mathf.MoveTowards(_airVelocityFollowBlend01, 1f, rate * dt);
    }

    /// <summary>
    /// While airborne and turning/drifting (not tricking), ease horizontal velocity toward the
    /// flat nose so travel follows the visual yaw. Strength is tunable — not a 1:1 lock.
    /// </summary>
    private void ApplyAirborneVelocityFollowNose(float dt)
    {
        if (rb == null || !_airborneForTricks || dt <= 0f) return;
        if (_airVelocityFollowBlend01 <= 0.001f) return;
        if (airVelocityFollowNose <= 0.001f || airVelocityFollowRate <= 0.001f) return;

        bool turningOrDrifting = Mathf.Abs(steeringInput) > 0.05f
            || isDrifting
            || driftButtonHeld;
        if (!turningOrDrifting) return;

        Vector3 vel = rb.velocity;
        Vector3 horiz = Vector3.ProjectOnPlane(vel, Vector3.up);
        if (horiz.sqrMagnitude < 0.25f) return;

        Vector3 nose = Vector3.ProjectOnPlane(transform.forward, Vector3.up);
        if (nose.sqrMagnitude < 1e-6f) return;
        nose.Normalize();

        Vector3 velDir = horiz.normalized;
        // Don't yank travel through a 180° flip if the nose is pointing roughly backward.
        if (Vector3.Dot(velDir, nose) < -0.15f)
            return;

        float intent = (isDrifting || driftButtonHeld)
            ? 1f
            : Mathf.Clamp01(Mathf.Abs(steeringInput));
        float blend = _airVelocityFollowBlend01;
        blend = blend * blend * (3f - 2f * blend); // smoothstep
        float rate = airVelocityFollowRate * Mathf.Clamp01(airVelocityFollowNose) * intent * blend;
        float t = 1f - Mathf.Exp(-rate * dt);
        Vector3 newDir = Vector3.Slerp(velDir, nose, t).normalized;
        Vector3 newHoriz = newDir * horiz.magnitude;
        rb.velocity = new Vector3(newHoriz.x, vel.y, newHoriz.z);
    }

    private void ApplyAirborneLeftStickControl(float dt)
    {
        if (_trickStickActive && !enableAirAimWhileTricking)
        {
            _smoothedAirSteer = SmoothAirSteerInput(0f, dt);
            _airTrajectoryBankRate = Mathf.MoveTowards(_airTrajectoryBankRate, 0f, airTrajectoryBankAccel * dt);
            return;
        }

        float rawSteer = RawSteerFromAxis(steeringInput, airYawInputDeadzone);
        _smoothedAirSteer = SmoothAirSteerInput(rawSteer, dt);

        if (Mathf.Abs(_smoothedAirSteer) < 0.001f)
        {
            _airTrajectoryBankRate = Mathf.MoveTowards(_airTrajectoryBankRate, 0f, airTrajectoryBankAccel * dt);
            return;
        }

        float yawRateMul = GetTrickAirYawAimMultiplier();
        float yawAmount = _smoothedAirSteer * airYawRate * yawRateMul * dt;
        if (Mathf.Abs(yawAmount) > 0.0001f)
            ApplyScriptedAirRotationLocal(0f, yawAmount, 0f);

        if (airTrajectorySteerRate <= 0.01f)
        {
            _airTrajectoryBankRate = 0f;
            return;
        }

        Vector3 vel = rb.velocity;
        Vector3 horiz = Vector3.ProjectOnPlane(vel, Vector3.up);
        if (horiz.sqrMagnitude < 4f)
        {
            _airTrajectoryBankRate = Mathf.MoveTowards(_airTrajectoryBankRate, 0f, airTrajectoryBankAccel * dt);
            return;
        }

        float trajMul = GetTrickAirTrajectoryMultiplier();
        float targetBankRate = _smoothedAirSteer * airTrajectorySteerRate * trajMul;
        _airTrajectoryBankRate = Mathf.MoveTowards(_airTrajectoryBankRate, targetBankRate, airTrajectoryBankAccel * dt);

        if (Mathf.Abs(_airTrajectoryBankRate) < 0.01f)
            return;

        float bankDeg = _airTrajectoryBankRate * dt;
        Vector3 banked = Quaternion.AngleAxis(bankDeg, Vector3.up) * horiz;
        rb.velocity = new Vector3(banked.x, vel.y, banked.z);
    }

    /// <summary>
    /// Airborne trick rotation (right stick). Spins bleed faster when the stick is released.
    /// While drifting in the air, left stick handles yaw — right-stick tricks still work.
    /// </summary>
    private void HandleAirborneTricks(float dt)
    {
        if (!_airborneForTricks || !enableAirTricks || rb == null) return;

        // Right stick is independent of left-stick air-drift steer, so always allow trick input.
        bool trickInputActive = _trickStickActive;
        float decelMul = trickInputActive ? 1f : 1.65f;
        float pitchTarget = 0f;
        float horizTarget = 0f;
        bool barrelMode = false;

        if (trickInputActive)
        {
            barrelMode = _barrelRollHeld;
            ResolveTrickStickInput(out float horiz, out float pitch);
            horizTarget = horiz;
            pitchTarget = pitch;
        }

        _smoothedTrickPitch = SmoothTrickAxis(_smoothedTrickPitch, pitchTarget, dt);
        _smoothedTrickHoriz = SmoothTrickAxis(_smoothedTrickHoriz, horizTarget, dt);

        float barrelBlendTarget = trickInputActive && barrelMode ? 1f : 0f;
        _barrelModeBlend = Mathf.MoveTowards(_barrelModeBlend, barrelBlendTarget, barrelModeBlendSpeed * dt);
        _trickBarrelModeActive = _barrelModeBlend > 0.01f;

        float horizSpin = trickInputActive ? GetEffectiveHorizSpinInput() : 0f;
        float yawShare = 1f - _barrelModeBlend;
        float rollShare = _barrelModeBlend;

        float targetPitchVel = _smoothedTrickPitch * trickPitchRate;
        float targetYawVel = horizSpin * trickYawSpinRate * yawShare;
        float targetRollVel = -horizSpin * trickRollRate * rollShare;

        // Softer bleed while cross-fading so disc spin can hand off into barrel roll naturally.
        float blendDecelMul = (_barrelModeBlend > 0.02f && _barrelModeBlend < 0.98f) ? 0.45f : 1f;

        _trickPitchAngularVel = MoveTrickAngularVel(_trickPitchAngularVel, targetPitchVel, dt, decelMul);
        _trickYawAngularVel = MoveTrickAngularVel(_trickYawAngularVel, targetYawVel, dt, decelMul * blendDecelMul);
        _trickRollAngularVel = MoveTrickAngularVel(_trickRollAngularVel, targetRollVel, dt, decelMul * blendDecelMul);

        if (Mathf.Abs(_trickPitchAngularVel) < 0.01f
            && Mathf.Abs(_trickYawAngularVel) < 0.01f
            && Mathf.Abs(_trickRollAngularVel) < 0.01f)
            return;

        ApplyScriptedAirRotationLocal(
            _trickPitchAngularVel * dt,
            _trickYawAngularVel * dt,
            _trickRollAngularVel * dt);
    }

    /// <summary>
    /// Scripted airborne rotation only — forces the car's rotation (as tricks/upright recovery did before) but
    /// never reads or writes position, so the car keeps its natural ballistic (velocity + gravity) motion and
    /// never snaps positionally while tricking or reorienting.
    /// </summary>
    private void ApplyScriptedAirRotation(Quaternion worldRotation)
    {
        if (rb == null) return;

        // Set rotation only. transform.rotation is what actually makes the spin stick on a non-kinematic body
        // (rb.MoveRotation alone is unreliable here); position is deliberately left untouched.
        rb.MoveRotation(worldRotation);
        transform.rotation = worldRotation;
        rb.angularVelocity = Vector3.zero;
    }

    private void ApplyScriptedAirRotationLocal(float pitchDeg, float yawDeg, float rollDeg)
    {
        if (rb == null) return;
        ApplyScriptedAirRotation(rb.rotation * Quaternion.Euler(pitchDeg, yawDeg, rollDeg));
    }

    private void UpdateAirTrickReleaseState(float dt)
    {
        // Only build upright recovery after the trick stick is released (and residual flip spin has bled off).
        // Keeping this at full strength during flips fights pitch and can reverse heading mid-backflip.
        if (!_airborneForTricks || rb == null)
        {
            _airUprightRecoverBlend = 0f;
            return;
        }

        bool suppressUpright = ShouldSuppressAirUprightForTricks();
        float target = suppressUpright ? 0f : 1f;
        float rate = suppressUpright ? 14f : 7.5f;
        _airUprightRecoverBlend = Mathf.MoveTowards(_airUprightRecoverBlend, target, rate * dt);
    }

    private bool ShouldSuppressAirUprightForTricks()
    {
        if (!enableAirTricks || !suppressRampAlignmentInTrickMode)
            return false;

        if (_trickStickActive)
            return true;

        // Keep recovery off while a flip/spin is still coasting after stick release.
        const float residualSpinDegPerSec = 35f;
        return Mathf.Abs(_trickPitchAngularVel) > residualSpinDegPerSec
            || Mathf.Abs(_trickYawAngularVel) > residualSpinDegPerSec
            || Mathf.Abs(_trickRollAngularVel) > residualSpinDegPerSec
            || Mathf.Abs(_smoothedTrickPitch) > 0.08f
            || Mathf.Abs(_smoothedTrickHoriz) > 0.08f;
    }

    private void ApplyAirborneGravity(float dt)
    {
        if (rb == null || !_airborneForTricks)
            return;

        float mul = Mathf.Max(0.1f, airGravityMultiplier);
        if (Mathf.Abs(mul - 1f) < 0.001f)
            return;

        // Extra acceleration beyond Unity gravity so airborne arcs feel a touch heavier.
        rb.AddForce(Physics.gravity * (mul - 1f), ForceMode.Acceleration);
    }

    /// <summary>
    /// Horizontal thrust axis while airborne. Uses current travel, then takeoff heading —
    /// never the car's pitched nose, so backflips cannot shove you into the sky.
    /// </summary>
    private Vector3 GetAirborneThrustDirection()
    {
        if (rb != null)
        {
            Vector3 travel = Vector3.ProjectOnPlane(rb.velocity, Vector3.up);
            if (travel.sqrMagnitude > 1f)
                return travel.normalized;
        }

        if (_takeoffHorizDir.sqrMagnitude > 1e-6f)
        {
            Vector3 takeoff = Vector3.ProjectOnPlane(_takeoffHorizDir, Vector3.up);
            if (takeoff.sqrMagnitude > 1e-6f)
                return takeoff.normalized;
        }

        Vector3 flatNose = Vector3.ProjectOnPlane(transform.forward, Vector3.up);
        if (flatNose.sqrMagnitude > 1e-6f)
            return flatNose.normalized;

        return Vector3.forward;
    }

    /// <summary>
    /// Out-of-fuel coast decel + steer traction so yaw input redirects travel (not just visual rotation).
    /// </summary>
    private void ApplyOutOfFuelCoastAndSteerTraction()
    {
        if (rb == null) return;

        rb.drag = effectiveDrag;

        if (!CheckIfGrounded()) return;

        RefreshGroundNormalForDriving(true);
        Vector3 forward = GetDriveForwardAlongSurface(transform.forward, _lastStableGroundNormal, true);
        float speed = rb.velocity.magnitude;
        float forwardSpeed = Vector3.Dot(rb.velocity, forward);

        if (forwardSpeed < -0.1f)
        {
            float reverseDecel = Mathf.Min(maxReverseAccelPerSecond, 3.5f);
            float newLong = Mathf.MoveTowards(forwardSpeed, 0f, reverseDecel * Time.fixedDeltaTime);
            SetLongitudinalVelocityAlongSurface(forward, newLong);
            CancelPlanarDrag(effectiveDrag);
        }
        else if (speed > 0.01f)
        {
            ApplyCoastDecelAlongSurface(GetArcadeCoastDecel(speed), Time.fixedDeltaTime);
            CancelPlanarDrag(effectiveDrag);
        }

        if (!enableSteerTraction || _onIceSurface || Mathf.Abs(steeringInput) <= 0.001f)
            return;

        Vector3 flatForward = new Vector3(transform.forward.x, 0f, transform.forward.z);
        if (flatForward.sqrMagnitude < 1e-6f) return;
        flatForward.Normalize();
        Vector3 vel = rb.velocity;
        Vector3 flatVel = new Vector3(vel.x, 0f, vel.z);

        if (flatVel.sqrMagnitude <= minSpeedForSteerTraction * minSpeedForSteerTraction)
            return;

        // When reversing, align travel to the rear of the car — never Slerp toward the nose
        // (that flips velocity 180° and feels like random spin-outs).
        Vector3 alignTarget = flatForward;
        if (Vector3.Dot(flatVel, flatForward) < 0f)
            alignTarget = -flatForward;

        float t = steerTractionReorientRate * Time.fixedDeltaTime;
        Vector3 blendedDir = Vector3.Slerp(flatVel.normalized, alignTarget, t).normalized;

        Vector3 fwdComp = alignTarget * Vector3.Dot(flatVel, alignTarget);
        Vector3 lateral = flatVel - fwdComp;
        lateral *= Mathf.Exp(-lateralFrictionWhileSteering * Time.fixedDeltaTime);

        float mag = (fwdComp + lateral).magnitude;
        Vector3 newFlat = blendedDir * mag;
        rb.velocity = new Vector3(newFlat.x, vel.y, newFlat.z);

        float coastMul = steerRollingAccelCoastMultiplier;
        if (_onIceSurface && !applySteerRollingAccelOnIce)
            coastMul = 0f;
        rb.AddForce(alignTarget * (steerRollingAccel * coastMul), ForceMode.Acceleration);
    }

    /// <summary>
    /// Pulls planar velocity onto the nose while turning. Strength comes from
    /// <see cref="_steerTractionBlend"/> (full while coasting, reduced while accelerating).
    /// </summary>
    private void ApplySteerRollingTraction()
    {
        if (rb == null || !enableSteerTraction || _onIceSurface) return;
        if (driftButtonHeld || Mathf.Abs(steeringInput) <= 0.001f) return;
        if (_steerTractionBlend <= 0.0001f) return;

        Vector3 flatForward = new Vector3(transform.forward.x, 0f, transform.forward.z);
        if (flatForward.sqrMagnitude < 1e-6f) return;
        flatForward.Normalize();

        Vector3 vel = rb.velocity;
        Vector3 flatVel = new Vector3(vel.x, 0f, vel.z);
        if (flatVel.sqrMagnitude <= minSpeedForSteerTraction * minSpeedForSteerTraction)
            return;

        Vector3 alignTarget = flatForward;
        if (Vector3.Dot(flatVel, flatForward) < 0f)
            alignTarget = -flatForward;

        float t = steerTractionReorientRate * _steerTractionBlend * Time.fixedDeltaTime;
        Vector3 blendedDir = Vector3.Slerp(flatVel.normalized, alignTarget, t).normalized;

        Vector3 fwdComp = alignTarget * Vector3.Dot(flatVel, alignTarget);
        Vector3 lateral = flatVel - fwdComp;
        lateral *= Mathf.Exp(-lateralFrictionWhileSteering * _steerTractionBlend * Time.fixedDeltaTime);

        float mag = (fwdComp + lateral).magnitude;
        Vector3 newFlat = blendedDir * mag;
        rb.velocity = new Vector3(newFlat.x, vel.y, newFlat.z);

        float coastMul = steerRollingAccelCoastMultiplier;
        if (_onIceSurface && !applySteerRollingAccelOnIce)
            coastMul = 0f;

        rb.AddForce(alignTarget * (steerRollingAccel * coastMul * _steerTractionBlend), ForceMode.Acceleration);
    }

    private void HandleSteering()
    {
        if (rb == null) return;
        if (_inCrash || _isReorienting) return;
        if (_flipMashActive) return;
        if (BlocksSteeringWhenDeadAndStopped()) return;

        float speed = rb.velocity.magnitude;
        float forwardSpeed = Vector3.Dot(rb.velocity, transform.forward);
        float steerSpeed = Mathf.Max(0f, effectiveTurnSpeed * GetTemporaryHandlingMultiplier());

        bool driftPhysicsActive = isDrifting || _driftGlideActive;

        // Reverse steer invert only once actually moving backward. Early engage while still rolling
        // forward inverted the stick and felt like the car was turning on its own / the wrong way.
        float steerDirection = 1f;
        bool reversingNow = forwardSpeed < -0.05f;
        if (invertSteeringWhenReversing
            && reversingNow
            && !(_airborneForTricks && ShouldSuppressAirUprightForTricks()))
        {
            steerDirection = -1f;
            // reverseSteerMultiplier is the only reverse turn scale (no hard cap — that made it feel stuck).
            steerSpeed *= reverseSteerMultiplier;
        }

        float topSpeedForSteering = speedForSteerCurve > 0f ? speedForSteerCurve : Mathf.Max(1f, effectiveMaxSpeed);
        float t = Mathf.Clamp01(speed / topSpeedForSteering);
        float speedSteerMul = Mathf.Lerp(lowSpeedSteerMultiplier, highSpeedSteerMultiplier, t);

        bool airborneNow = _airborneForTricks || !CheckIfGrounded();
        // Air turn uses cruise speed curve (not low-speed ground boost) and a reduced rate vs ground.
        if (airborneNow)
        {
            speedSteerMul = highSpeedSteerMultiplier;
            steerSpeed *= Mathf.Clamp(airTurnSpeedMultiplier, 0.05f, 1.5f);
        }

        // Ground: full drift sharpening via landing feel blend.
        // Air: scaled drift sharpening so hold-drift changes handling mid-jump (not just hold-boost).
        float driftFeel = GetDriftGroundFeelEased();
        float driftSteerMul = 1f;
        if (isDrifting)
        {
            float chargedSteer = Mathf.Lerp(1f, maxDriftSteerMultiplier, driftCharge);
            float feel = airborneNow ? Mathf.Clamp01(airDriftSteerFeel) : driftFeel;
            driftSteerMul = Mathf.Lerp(1f, chargedSteer, feel);
        }

        // Left stick yaws in air at airTurnSpeedMultiplier (~60% of ground). Right stick is tricks.

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
                || (IsOutOfFuel && !IsStoppedForRunEnd);

            // Out-of-fuel physics mode: allow yaw while coasting; no turning in place once stopped.
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

            float sideForceThrottleTarget = acceleratingDrift
                ? Mathf.Clamp01(driftSideForceWhileAccelerating)
                : 1f;
            float sideForceBlendRate = Mathf.Max(0.1f, driftSideForceThrottleBlendRate);
            _driftSideForceThrottle01 = Mathf.MoveTowards(
                _driftSideForceThrottle01,
                sideForceThrottleTarget,
                sideForceBlendRate * Time.deltaTime);

            if (isDrifting && speed > 0.1f && _driftSideForceThrottle01 > 0.001f)
            {
                float sideFeel = airborneNow ? Mathf.Clamp01(airDriftSideForceScale) : driftFeel;
                if (sideFeel > 0.001f)
                {
                    float sign = Mathf.Sign(steeringInput);
                    Vector3 sideDir = Vector3.Cross(Vector3.up, transform.forward) * sign;
                    // Keep air shove horizontal so it doesn't fight ballistic flight.
                    if (airborneNow)
                        sideDir.y = 0f;
                    if (sideDir.sqrMagnitude > 0.0001f)
                        sideDir.Normalize();

                    float sideMul = Mathf.Lerp(0.5f, 1f, driftCharge);
                    // Reduce lateral snap during flip rebuild delay.
                    float sideForceScale = Time.time < _driftFlipBlockUntil ? 0.4f : 1f;
                    rb.AddForce(
                        sideDir * driftSideForce * sideMul * sideForceScale * sideFeel * _driftSideForceThrottle01,
                        ForceMode.Acceleration);
                }
            }
        }

        if (useAutoAlignToVelocity &&
            !IsPostCrashRecoveryDriving &&
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

        if (_isReorienting)
        {
            forwardKey = false;
            reverseKey = false;
        }

        // Grounded check early so we can use it anywhere in this method.
        bool groundedNow = CheckIfGrounded();

        // Trick stick still blocks brake/reverse in air (barrel-roll modifier). Thrust stays on the
        // ballistic heading (see GetAirborneThrustDirection) so flips do not redirect flight.
        if (_airborneForTricks && enableAirTricks && _trickStickActive)
            reverseKey = false;

        // Treat glide the same as drift for physics retention.
        bool driftPhysicsActive = isDrifting || _driftGlideActive;

        // TEST: auto-cruise forward when not drifting. During drift, leave forwardKey as the real
        // accelerate input so hard drift (accel held) vs soft drift (accel released) still work.
        // Respect post-crash accel gate — otherwise always-accel reintroduces the recovery boost.
        bool alwaysAccel = IsAlwaysAccelerateTestActive();
        if (alwaysAccel
            && !_blockAccelUntilReleased
            && !driftPhysicsActive
            && !reverseKey
            && !_flipMashActive
            && !_isReorienting
            && !_inCrash
            && !(_inputsSuppressedThisFrame || _suppressThrottleBrakeThisFrame))
        {
            forwardKey = true;
        }

        // Throttle/brake along surface tangent when grounded (no need to wait for pitch alignment on steep terrain).
        Vector3 forward = GetDriveForwardAlongSurface(transform.forward, _lastStableGroundNormal, groundedNow);

        // In air, thrust follows the ballistic travel / takeoff heading — never the pitched nose.
        // Tricks are rotational only and must not redirect flight into the sky on a backflip.
        if (!groundedNow)
            forward = GetAirborneThrustDirection();

        float speed = rb.velocity.magnitude;
        float forwardSpeed = Vector3.Dot(rb.velocity, forward);

        // Always-accel test still drives with empty tank so fuel drain doesn't kill the experiment.
        bool canDrive = (!isOutOfFuel && maxFuel > 0f) || (alwaysAccel && forwardKey && !reverseKey);
        if (canDrive)
        {
            // Brake overrides throttle: both keys = decelerate (reverse/brake path), not full accel.
            bool accelerating = forwardKey && !reverseKey;
            bool brakingOrReverse = reverseKey;
            bool burnFuel = !isOutOfFuel && maxFuel > 0f;

            // Accel / coast / brake turn feel share the same steer-traction system (blended, not snapped).
            bool canHaveSteerTraction =
                groundedNow &&
                enableSteerTraction &&
                !driftButtonHeld &&
                !driftPhysicsActive &&
                Mathf.Abs(steeringInput) > 0.001f;

            float tractionTarget = 0f;
            if (canHaveSteerTraction)
            {
                if (brakingOrReverse)
                    tractionTarget = Mathf.Clamp01(steerTractionWhileBraking);
                else if (accelerating)
                    tractionTarget = Mathf.Clamp01(steerTractionWhileAccelerating);
                else
                    tractionTarget = 1f;
            }

            float blendSpeed = tractionTarget > _steerTractionBlend ? steerTractionBlendIn : steerTractionBlendOut;
            _steerTractionBlend = Mathf.MoveTowards(_steerTractionBlend, tractionTarget, blendSpeed * Time.fixedDeltaTime);

            // Soft brake-align authority (no hard swap into a separate high-rate nose yank).
            float brakeAlignTarget = (brakingOrReverse && groundedNow) ? 1f : 0f;
            float brakeAlignRate = brakeAlignTarget > _brakeAlignBlend01 ? steerTractionBlendIn : steerTractionBlendOut;
            _brakeAlignBlend01 = Mathf.MoveTowards(_brakeAlignBlend01, brakeAlignTarget, brakeAlignRate * Time.fixedDeltaTime);


            if (!driftPhysicsActive)
            {
                if (accelerating)
                {
                    ApplyForwardThrottleAcceleration(forward, GetThrottleAcceleration(), effectiveDrag);
                    if (burnFuel) ConsumeFuel(fuelUsePerSecondAtFullThrottle * Time.fixedDeltaTime);
                }
                else if (brakingOrReverse)
                {
                    float dt = Time.fixedDeltaTime;

                    if (!groundedNow)
                    {
                        // No mid-air braking — left trigger is used for barrel rolls with left bumper.
                    }
                    else
                    {
                        // Brake sheds speed; turn alignment stays on steer traction (same family as coast).
                        ApplyBrakeOrReverseAlongFacing(forward, dt);
                        CancelPlanarDrag(effectiveDrag);
                        if (burnFuel) ConsumeFuel(fuelUsePerSecondBraking * Time.fixedDeltaTime);
                    }
                }
                else
                {
                    // Coasting (no W/S) — skipped entirely while testAlwaysAccelerate holds throttle
                    if (groundedNow)
                    {
                        if (forwardSpeed < -0.1f)
                        {
                            float reverseDecel = Mathf.Min(maxReverseAccelPerSecond, 3.5f);
                            float newLong = Mathf.MoveTowards(forwardSpeed, 0f, reverseDecel * Time.fixedDeltaTime);
                            SetLongitudinalVelocityAlongSurface(forward, newLong);
                            CancelPlanarDrag(effectiveDrag);
                        }
                        else if (speed > 0.01f)
                        {
                            float decel = GetArcadeCoastDecel(speed);
                            ApplyCoastDecelAlongSurface(decel, Time.fixedDeltaTime);
                            CancelPlanarDrag(effectiveDrag);
                        }
                    }
                }

                if (groundedNow && _steerTractionBlend > 0.0001f)
                    ApplySteerRollingTraction();
            }
            else
            {
                // Drifting/gliding with fuel
                bool consumedFuelThisFrame = false;

                if (accelerating)
                {
                    float accelMul = (useFullAccelWhileDrifting ? 1f : driftForwardAccelMultiplier);
                    float throttleAccel = GetThrottleAcceleration() * accelMul;
                    ApplyForwardThrottleAcceleration(forward, throttleAccel, effectiveDrag * 0.01f);
                    if (burnFuel)
                    {
                        ConsumeFuel(fuelUsePerSecondAtFullThrottle * Time.fixedDeltaTime);
                        consumedFuelThisFrame = true;
                    }
                }

                if (brakingOrReverse && isDrifting)
                {
                    if (burnFuel)
                    {
                        ConsumeFuel(fuelUsePerSecondBraking * Time.fixedDeltaTime);
                        consumedFuelThisFrame = true;
                    }
                }

                // Drift/glide without accel or brake still burns fuel (you're moving fast)
                if (!consumedFuelThisFrame && burnFuel && (isDrifting || _driftGlideActive))
                {
                    ConsumeFuel(fuelUsePerSecondAtFullThrottle * Time.fixedDeltaTime);
                }
            }

            if (burnFuel && !accelerating && !brakingOrReverse && !driftPhysicsActive)
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
        bool throttleForAssist = forwardKey && !reverseKey && !isOutOfFuel && maxFuel > 0f && groundedNow
            && !IsPostCrashRecoveryDriving;
        if (throttleForAssist && TryGetSlopeDriveAssist(out Vector3 surfFwd, out float extraA))
        {
            assistDir = surfFwd;
            assistTarget = extraA;
        }

        float assistDt = Time.fixedDeltaTime;
        // While braking or in post-crash recovery, kill residual assist immediately — it was a
        // forward shove even when the player was trying to decelerate.
        if (reverseKey || IsPostCrashRecoveryDriving)
        {
            _slopeAssistSmoothed = 0f;
        }
        else
        {
            float assistStep = assistTarget > _slopeAssistSmoothed
                ? slopeDriveAssistRiseSpeed * assistDt
                : slopeDriveAssistFallSpeed * assistDt;
            _slopeAssistSmoothed = Mathf.MoveTowards(_slopeAssistSmoothed, assistTarget, assistStep);
        }
        if (_slopeAssistSmoothed > 1e-4f)
            rb.AddForce(assistDir * (_slopeAssistSmoothed * _currentMalfunctionMultiplier), ForceMode.Acceleration);

        rb.drag = driftPhysicsActive
            ? Mathf.Lerp(effectiveDrag, effectiveDrag * 0.01f, groundedNow ? GetDriftGroundFeelEased() : 1f)
            : effectiveDrag;

        speed = rb.velocity.magnitude;

        float driftFeel = GetDriftGroundFeelEased();
        if (driftPhysicsActive && groundedNow && driftFeel > 0.001f)
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
                float blend = Mathf.Clamp01(steerInfluence * driftAlignStrength * Time.fixedDeltaTime * driftFeel);

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

                float smoothRate = 15f * driftFeel;
                float smoothedMag = Mathf.Lerp(currentMag, targetMagnitude, smoothRate * Time.fixedDeltaTime);

                float y = rb.velocity.y;
                Vector3 horiz = finalDir.normalized * Mathf.Max(0f, smoothedMag);
                rb.velocity = new Vector3(horiz.x, y, horiz.z);

                if (isDrifting && Mathf.Abs(steeringInput) > 0.001f && currentMag > 0.1f)
                {
                    float sideForceThrottleTarget = holdingThrottle
                        ? Mathf.Clamp01(driftSideForceWhileAccelerating)
                        : 1f;
                    float sideForceBlendRate = Mathf.Max(0.1f, driftSideForceThrottleBlendRate);
                    _driftSideForceThrottle01 = Mathf.MoveTowards(
                        _driftSideForceThrottle01,
                        sideForceThrottleTarget,
                        sideForceBlendRate * Time.fixedDeltaTime);

                    if (_driftSideForceThrottle01 > 0.001f)
                    {
                        float sign = Mathf.Sign(steeringInput);
                        Vector3 sideDir = Vector3.Cross(Vector3.up, transform.forward) * sign;
                        float sideMul = Mathf.Lerp(0.5f, 1f, driftCharge);
                        float sideForceScale = Time.time < _driftFlipBlockUntil ? 0.4f : 1f;
                        rb.AddForce(
                            sideDir * driftSideForce * sideMul * sideForceScale * driftFeel * _driftSideForceThrottle01,
                            ForceMode.Acceleration);
                    }
                }
            }
        }
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
            amount *= GetLowHpFuelDrainMultiplier();
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
                NotifyOutOfFuelTvFlare();
        }
        else
        {
            currentFuel = Mathf.Min(currentFuel, maxFuel);
        }
    }

    /// <summary>
    /// 1 at 100% HP; scales up to 1 + (extraFuelDrainPercentAtZeroHp / 100) at 0% HP.
    /// </summary>
    private float GetLowHpFuelDrainMultiplier()
    {
        if (!enableLowHpExtraFuelDrain || maxHP <= 0f || extraFuelDrainPercentAtZeroHp <= 0f)
            return 1f;

        float missingHpFraction = 1f - Mathf.Clamp01(HPPercent);
        float extraFraction = extraFuelDrainPercentAtZeroHp / 100f;
        return 1f + extraFraction * missingHpFraction;
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
        ref float bestBoostMaxSpeedMul,
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
                    ref anyBoostRamp, ref bestBoostAccel, ref bestBoostMaxSpeed, ref bestBoostMaxSpeedMul,
                    ref boostDuringCrash, ref boostCrashMult);
            }
        }
    }

    private void SampleGroundAndUpdateMultipliers()
    {
        if (carCollider == null) return;

        // After mash/upright recovery, ignore pads briefly so leftover pad stats don't stick.
        // During crash/reorient we still sample ice/road below — boost is stripped separately.
        // (Do not early-return here: crash tumble needs ice sampling.)

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
        float bestBoostMaxSpeedMul = 1f;
        bool boostDuringCrash = false;
        float boostCrashMult = 0.5f;
        _sampledBoostIsRamp = false;

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
                ref anyBoostRamp, ref bestBoostAccel, ref bestBoostMaxSpeed, ref bestBoostMaxSpeedMul,
                ref boostDuringCrash, ref boostCrashMult);
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
                        ref anyBoostRamp, ref bestBoostAccel, ref bestBoostMaxSpeed, ref bestBoostMaxSpeedMul,
                        ref boostDuringCrash, ref boostCrashMult);
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
                        ref anyBoostRamp, ref bestBoostAccel, ref bestBoostMaxSpeed, ref bestBoostMaxSpeedMul,
                        ref boostDuringCrash, ref boostCrashMult);
                }
            }
        }

        _onBoostSurface = false;
        _onBoostRamp = false;
        _currentBoostAccel = 0f;
        _currentBoostMaxSpeed = 0f;
        _currentBoostDuringCrash = false;
        _currentBoostCrashMultiplier = 0.5f;
        _sampleDetectedBoostSurface = false;

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

            // Crash / reorient / post-crash: never inflate drive stats from pads.
            // Also remember raw pad contact so we can unlock "must exit pad" after crash.
            _sampleDetectedBoostSurface = anyBoostRamp;
            bool suppressBoost = ShouldSuppressBoostSurfaces();
            if (suppressBoost)
            {
                anyBoostRamp = false;
                avgMaxSpeedMul = 1f;
                avgAccelMul = 1f;
            }
            else if (anyBoostRamp)
            {
                avgMaxSpeedMul = Mathf.Max(avgMaxSpeedMul, bestBoostMaxSpeedMul);
            }

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
        // NOTE: boost arming below still respects ShouldSuppressBoostSurfaces().
        if (ShouldSuppressBoostSurfaces())
        {
            _onBoostSurface = false;
            _onBoostRamp = false;
            _hasHeldBoostDriveNormal = false;
            _currentBoostAccel = 0f;
            _currentBoostMaxSpeed = 0f;
            _currentBoostDuringCrash = false;
            _currentBoostCrashMultiplier = 0.5f;
            _heldBoostAccel = 0f;
            _heldBoostMaxSpeed = 0f;
            _heldBoostDuringCrash = false;
            _heldBoostCrashMultiplier = 0.5f;
            _heldOnBoostRamp = false;
            _boostSurfaceHoldUntil = 0f;
        }
        else if (anyBoostRamp)
        {
            _onBoostSurface = true;
            _onBoostRamp = _sampledBoostIsRamp;
            _currentBoostAccel = bestBoostAccel;
            _currentBoostMaxSpeed = bestBoostMaxSpeed;
            _currentBoostDuringCrash = boostDuringCrash;
            _currentBoostCrashMultiplier = boostCrashMult;
            _heldBoostAccel = bestBoostAccel;
            _heldBoostMaxSpeed = bestBoostMaxSpeed;
            _heldBoostDuringCrash = boostDuringCrash;
            _heldBoostCrashMultiplier = boostCrashMult;
            _heldOnBoostRamp = _onBoostRamp;
            _boostSurfaceHoldUntil = Time.time + BoostSurfaceHoldSeconds;
        }
        else
        {
            CheckForBoostSurface();
            if (_onBoostSurface)
            {
                _heldBoostAccel = _currentBoostAccel;
                _heldBoostMaxSpeed = _currentBoostMaxSpeed;
                _heldBoostDuringCrash = _currentBoostDuringCrash;
                _heldBoostCrashMultiplier = _currentBoostCrashMultiplier;
                _heldOnBoostRamp = _onBoostRamp;
                _boostSurfaceHoldUntil = Time.time + BoostSurfaceHoldSeconds;
                _sampleDetectedBoostSurface = true;
            }
            else if (Time.time < _boostSurfaceHoldUntil)
            {
                // Edge flicker on steep ramps: keep pad force + raised speed cap for a beat.
                _onBoostSurface = true;
                _onBoostRamp = _heldOnBoostRamp;
                _currentBoostAccel = _heldBoostAccel;
                _currentBoostMaxSpeed = _heldBoostMaxSpeed;
                _currentBoostDuringCrash = _heldBoostDuringCrash;
                _currentBoostCrashMultiplier = _heldBoostCrashMultiplier;
                _sampleDetectedBoostSurface = true;
            }
            else
            {
                _hasHeldBoostDriveNormal = false;
                _onBoostRamp = false;
            }
        }

        // Unlock only after arming logic, so pads cannot re-engage the same frame we leave them.
        TryUnlockBoostSurfacesAfterCrashExit(_sampleDetectedBoostSurface);
    }

    /// <summary>
    /// Pads / landing carry / inflated surface max must not survive crash tumble or upright reorient.
    /// Also stays suppressed until the car leaves any boost pad touched during/after the crash.
    /// </summary>
    private bool ShouldSuppressBoostSurfaces()
    {
        return _inCrash
            || _isReorienting
            || _flipMashActive
            || IsPostCrashRecoveryDriving
            || (_postMashRecoveryIgnoreBoostUntil > 0f && Time.time < _postMashRecoveryIgnoreBoostUntil)
            || _boostSurfacesLockedUntilExitAfterCrash;
    }

    /// <summary>
    /// After crash recovery time gates expire, unlock pads only once we're clearly off a boost surface.
    /// </summary>
    private void TryUnlockBoostSurfacesAfterCrashExit(bool detectedBoostSurface)
    {
        if (!_boostSurfacesLockedUntilExitAfterCrash)
            return;

        // Keep locked through tumble / upright / short post-crash window.
        if (_inCrash || _isReorienting || _flipMashActive || IsPostCrashRecoveryDriving)
            return;
        if (_postMashRecoveryIgnoreBoostUntil > 0f && Time.time < _postMashRecoveryIgnoreBoostUntil)
            return;

        if (!detectedBoostSurface)
            _boostSurfacesLockedUntilExitAfterCrash = false;
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
                _onBoostRamp = surface.surfaceType == SurfaceType.Ramp;
                _currentBoostAccel = surface.boostAcceleration;
                _currentBoostMaxSpeed = surface.boostMaxSpeed;
                _currentBoostDuringCrash = surface.boostDuringCrash;
                _currentBoostCrashMultiplier = surface.boostCrashMultiplier;
            }
        }
    }

    private bool TryGetGroundNormal(Vector3 origin, float distance, out RaycastHit hit)
    {
        // SphereCastAll: ignore flat boost-pad triggers. Prefer the highest mountable deck
        // (raised road / ramp top) — steep-prefer scoring chased curb lips and killed speed.
        hit = default;
        float radius = Mathf.Max(0.01f, groundNormalCastRadius);
        float castDist = Mathf.Max(0.01f, distance);
        int count = Physics.SphereCastNonAlloc(
            origin,
            radius,
            Vector3.down,
            _groundNormalHitBuffer,
            castDist,
            groundLayers,
            QueryTriggerInteraction.Collide);

        if (count <= 0)
            return false;

        int fallbackIdx = -1;
        float fallbackDist = float.MaxValue;
        float closestValidDist = float.MaxValue;

        for (int i = 0; i < count; i++)
        {
            RaycastHit candidate = _groundNormalHitBuffer[i];
            if (candidate.collider == null) continue;

            float d = candidate.distance;
            if (d < fallbackDist)
            {
                fallbackDist = d;
                fallbackIdx = i;
            }

            if (IsFlatBoostPadHit(candidate))
                continue;

            if (d < closestValidDist)
                closestValidDist = d;
        }

        int bestIdx = -1;
        float bestHeight = float.MinValue;
        float bestFlatness = -1f;
        bool bestIsRamp = false;
        float bestDist = float.MaxValue;
        const float nearBand = 0.45f;

        for (int i = 0; i < count; i++)
        {
            RaycastHit candidate = _groundNormalHitBuffer[i];
            if (candidate.collider == null) continue;
            if (IsFlatBoostPadHit(candidate)) continue;
            if (candidate.distance > closestValidDist + nearBand) continue;

            bool isRamp = IsRampSurfaceHit(candidate);
            float height = candidate.point.y;
            float flatness = candidate.normal.y;
            float d = candidate.distance;

            bool better = false;
            if (bestIdx < 0)
                better = true;
            else if (height > bestHeight + GroundNormalHeightTieBand)
                better = true;
            else if (height >= bestHeight - GroundNormalHeightTieBand)
            {
                if (isRamp && !bestIsRamp)
                    better = true;
                else if (isRamp == bestIsRamp && flatness > bestFlatness + 0.02f)
                    better = true;
                else if (isRamp == bestIsRamp && flatness >= bestFlatness - 0.02f && d < bestDist)
                    better = true;
            }

            if (!better) continue;
            bestIdx = i;
            bestHeight = height;
            bestFlatness = flatness;
            bestIsRamp = isRamp;
            bestDist = d;
        }

        int useIdx = bestIdx >= 0 ? bestIdx : fallbackIdx;
        if (useIdx < 0)
            return false;

        hit = _groundNormalHitBuffer[useIdx];
        if (bestIdx < 0 && IsFlatBoostPadHit(hit))
            hit.normal = Vector3.up;
        return true;
    }

    private static bool IsRampSurfaceHit(RaycastHit hit)
    {
        if (hit.collider == null) return false;
        GroundSurface surface = hit.collider.GetComponent<GroundSurface>()
                             ?? hit.collider.GetComponentInParent<GroundSurface>();
        return surface != null && surface.surfaceType == SurfaceType.Ramp;
    }

    /// <summary>
    /// Flat boost pads are trigger meshes; their edge normals are unreliable for driving.
    /// Real boost ramps are solid MeshColliders with SurfaceType.Ramp — keep those.
    /// </summary>
    private static bool IsFlatBoostPadHit(RaycastHit hit)
    {
        if (hit.collider == null) return false;
        GroundSurface surface = hit.collider.GetComponent<GroundSurface>()
                             ?? hit.collider.GetComponentInParent<GroundSurface>();
        if (surface == null || surface.surfaceType != SurfaceType.Boost)
            return false;
        return hit.collider.isTrigger || hit.normal.y >= FlatBoostNormalMinY;
    }

    private bool IsMixedGrassRoadSurface()
    {
        return grassFraction > groundNormalMixedGrassMin && grassFraction < groundNormalMixedGrassMax;
    }

    /// <summary>
    /// When a grass ray hits terrain below a slightly raised road, prefer the higher non-grass surface.
    /// Only used for surface stat sampling — does not move the car.
    /// </summary>
    private bool TryRaycastSurfaceSample(Vector3 origin, float rayDistance, out RaycastHit hit)
    {
        if (!Physics.Raycast(origin, Vector3.down, out hit, rayDistance, groundLayers, QueryTriggerInteraction.Collide))
            return false;

        GroundSurface firstSurface = hit.collider.GetComponent<GroundSurface>()
                                     ?? hit.collider.GetComponentInParent<GroundSurface>();
        if (firstSurface == null || firstSurface.surfaceType != SurfaceType.Grass)
            return true;

        int count = Physics.RaycastNonAlloc(origin, Vector3.down, _surfaceRayBuffer, rayDistance, groundLayers, QueryTriggerInteraction.Collide);
        if (count <= 1)
            return true;

        float bestY = hit.point.y;
        int bestIdx = -1;
        for (int i = 0; i < count; i++)
        {
            RaycastHit candidate = _surfaceRayBuffer[i];
            if (candidate.point.y <= bestY + 0.02f)
                continue;

            GroundSurface candidateSurface = candidate.collider.GetComponent<GroundSurface>()
                                             ?? candidate.collider.GetComponentInParent<GroundSurface>();
            if (candidateSurface == null || candidateSurface.surfaceType == SurfaceType.Grass)
                continue;

            if (candidate.point.y > bestY)
            {
                bestY = candidate.point.y;
                bestIdx = i;
            }
        }

        if (bestIdx >= 0)
            hit = _surfaceRayBuffer[bestIdx];
        return true;
    }

    /// <summary>
    /// Restore planar speed lost when mounting road from grass / hitting lips.
    /// Runs at end of FixedUpdate after movement, boost, and clamps.
    /// </summary>
    private void ApplyGrassToRoadTransitionSpeedPreserve()
    {
        if (rb == null || _inCrash || _flipMashActive || IsPostCrashRecoveryDriving) return;

        float g = grassFraction;
        float prev = _grassFractionPrevFrame;
        if (prev < 0f) return;

        float referenceSpeed = _planarSpeedLastFixedUpdate;
        if (referenceSpeed < roadGrassTransitionMinSpeed) return;

        Vector3 v = rb.velocity;
        Vector3 horiz = new Vector3(v.x, 0f, v.z);
        float currentSpeed = horiz.magnitude;
        if (currentSpeed >= referenceSpeed * 0.88f) return;

        // Trigger when leaving grass for road, or when still mixed but planar speed just dumped.
        bool enteringRoad = prev >= 0.42f && g <= 0.38f;
        bool mixedSpeedDump = IsMixedGrassRoadSurface() && currentSpeed < referenceSpeed * 0.7f;
        if (!enteringRoad && !mixedSpeedDump) return;

        float now = Time.time;
        if (now - _lastGrassToRoadSpeedPreserveTime < roadGrassTransitionLiftCooldown) return;

        if (currentSpeed > 0.05f)
            horiz = horiz.normalized * referenceSpeed;
        else
        {
            Vector3 fwd = transform.forward;
            fwd.y = 0f;
            if (fwd.sqrMagnitude < 1e-6f) return;
            horiz = fwd.normalized * referenceSpeed;
        }

        rb.velocity = new Vector3(horiz.x, v.y, horiz.z);
        _lastGrassToRoadSpeedPreserveTime = now;
    }

    /// <summary>
    /// Updates measured ground normal before soft blend. Rejects curb-lip normals while mixed grass/road.
    /// </summary>
    private void RefreshGroundNormalForDriving(bool groundedNow)
    {
        if (!groundedNow || carCollider == null) return;
        Vector3 castOrigin = carCollider.bounds.center + Vector3.up * 0.25f;
        if (TryGetGroundNormal(castOrigin, groundNormalCheckDistance, out RaycastHit hit))
        {
            // Mixed grass/road: ignore steep curb faces — keep previous measured normal.
            if (!_onBoostRamp && IsMixedGrassRoadSurface() && hit.normal.y < MixedSurfaceLipNormalMinY)
                return;
            _groundNormalMeasured = hit.normal;
        }
    }

    /// <summary>
    /// Soft blend toward measured normal. No steep/boost snap rates — those chased lips and twisted the car.
    /// </summary>
    private void SmoothDrivingGroundNormal()
    {
        if (rb == null || carCollider == null) return;
        if (!CheckIfGrounded()) return;

        _prevSmoothedGroundNormal = _lastStableGroundNormal.sqrMagnitude > 1e-8f
            ? _lastStableGroundNormal.normalized
            : Vector3.up;

        float dt = Time.fixedDeltaTime;
        float rate = groundNormalBlendRate;
        if (IsMixedGrassRoadSurface())
            rate *= groundNormalMixedSurfaceBlendScale;
        else if (_onBoostRamp)
            rate = Mathf.Max(rate, groundNormalBlendRate * 1.2f);

        Vector3 measured = _groundNormalMeasured.sqrMagnitude > 1e-8f
            ? _groundNormalMeasured.normalized
            : Vector3.up;

        float t = Mathf.Clamp01(rate * dt);
        _lastStableGroundNormal = Vector3.Slerp(_lastStableGroundNormal, measured, t);
        if (_lastStableGroundNormal.sqrMagnitude < 1e-10f)
            _lastStableGroundNormal = Vector3.up;
        else
            _lastStableGroundNormal.Normalize();
    }

    /// <summary>
    /// Soft help mounting raised road / ramps without rewriting travel onto curb lips.
    /// Cancels hard into-face dig only — does not remap full speed onto a new tangent.
    /// </summary>
    private void AssistOntoDrivePlane()
    {
        if (rb == null) return;
        if (isDrifting || _driftGlideActive) return;
        if (_onBoostSurface && !_onBoostRamp) return;
        // Grass/road lips: leave PhysX alone — remapping here dumped speed and pitched the car.
        if (IsMixedGrassRoadSurface()) return;
        if (!CheckIfGrounded()) return;

        Vector3 n = _lastStableGroundNormal.sqrMagnitude > 1e-8f
            ? _lastStableGroundNormal.normalized
            : Vector3.up;

        if (n.y >= MountAssistMaxNormalY) return;

        Vector3 v = rb.velocity;
        float into = Vector3.Dot(v, n);
        // Digging into the plane — cancel into-face only (no full-speed remormalize).
        float digThreshold = _onBoostRamp ? -0.4f : -0.55f;
        if (into < digThreshold)
            rb.velocity = v - n * into;
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

    private bool IsAlwaysAccelerateTestActive()
    {
        return _movementConfig != null ? _movementConfig.TestAlwaysAccelerate : testAlwaysAccelerate;
    }

    /// <summary>
    /// Brake/reverse in the car's wheel/nose frame.
    /// Decelerates planar speed; turn alignment uses the same steer-traction family as coasting
    /// (blended via <see cref="_brakeAlignBlend01"/>) instead of a hard high-rate brake handling mode.
    /// Reverse only after nearly stopped (or when already moving backward along the nose).
    /// </summary>
    private void ApplyBrakeOrReverseAlongFacing(Vector3 forward, float dt, bool allowReverse = true)
    {
        if (rb == null || dt <= 0f) return;

        Vector3 n = _lastStableGroundNormal.sqrMagnitude > 1e-8f
            ? _lastStableGroundNormal.normalized
            : Vector3.up;

        forward = Vector3.ProjectOnPlane(forward, n);
        if (forward.sqrMagnitude < 1e-8f) return;
        forward.Normalize();

        Vector3 v = rb.velocity;
        Vector3 vTan = Vector3.ProjectOnPlane(v, n);
        float longSpeed = Vector3.Dot(vTan, forward);
        float tanSpeed = vTan.magnitude;
        float vn = Vector3.Dot(v, n);

        float reverseStartSpeed = Mathf.Max(0.2f, brakeToReverseSpeed);
        bool nearlyStopped = tanSpeed <= reverseStartSpeed;
        bool movingBackwardAlongNose = longSpeed < -0.05f
            && tanSpeed > 1e-4f
            && Vector3.Dot(vTan / tanSpeed, forward) < -0.2f;

        // Reverse only once settled (or already reversing). Never steal a high-speed sideways slide into reverse.
        if (allowReverse && ((nearlyStopped && longSpeed <= 0.05f) || movingBackwardAlongNose))
        {
            float reverseAccel = maxReverseAccelPerSecond > 0f ? maxReverseAccelPerSecond : 1f;
            if (reverseAccelFactor > 0f) reverseAccel *= reverseAccelFactor;
            float targetReverseSpeed = -Mathf.Max(1f, effectiveMaxSpeed * 0.4f);
            float fromLong = nearlyStopped ? Mathf.Min(0f, longSpeed) : longSpeed;
            float newLong = Mathf.MoveTowards(fromLong, targetReverseSpeed, reverseAccel * dt);

            if (nearlyStopped)
            {
                // Clean reverse off the line — no leftover sideways from the brake slide.
                rb.velocity = forward * newLong + vn * n;
            }
            else
            {
                // Already reversing with some slip: keep lateral so reverse turns don't snap.
                SetLongitudinalVelocityAlongSurface(forward, newLong);
            }
            return;
        }

        // BRAKE: shed planar speed. Alignment stays soft / shared with coast traction.
        float decel = GetArcadeBrakeDecel();
        float newSpeed = Mathf.MoveTowards(tanSpeed, 0f, decel * dt);
        if (newSpeed <= 1e-4f)
        {
            rb.velocity = vn * n;
            return;
        }

        Vector3 travelDir = tanSpeed > 1e-5f ? (vTan / tanSpeed) : forward;

        // While turning with steer traction, that system owns nose-follow — only reduce speed.
        if (Mathf.Abs(steeringInput) > 0.001f && _steerTractionBlend > 0.001f)
        {
            rb.velocity = travelDir * newSpeed + vn * n;
            return;
        }

        Vector3 alignTarget = forward;
        if (Vector3.Dot(travelDir, forward) < 0f)
            alignTarget = -forward;

        float noseAlign = Mathf.Abs(Vector3.Dot(travelDir, alignTarget));
        float slip = 1f - Mathf.Clamp01(noseAlign);
        // Same rate family as coast traction; eased in by brake blend (no instant 6–14 snap).
        float alignRate = steerTractionReorientRate
            * Mathf.Lerp(0.55f, 1.15f, slip)
            * Mathf.Clamp01(_brakeAlignBlend01);
        Vector3 newDir = travelDir;
        if (alignRate > 0.001f)
        {
            newDir = Vector3.Slerp(travelDir, alignTarget, Mathf.Clamp01(alignRate * dt));
            if (newDir.sqrMagnitude < 1e-8f)
                newDir = alignTarget;
            else
                newDir.Normalize();
        }

        rb.velocity = newDir * newSpeed + vn * n;
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
    /// Forward brake rate from movement config. Previously only maxBrakeDecelPerSecond was used;
    /// baseBrakingForce / brakeForwardFactor were loaded but ignored.
    /// </summary>
    private float GetArcadeBrakeDecel()
    {
        float decel = maxBrakeDecelPerSecond > 0f ? maxBrakeDecelPerSecond : 1f;
        if (brakeForwardFactor > 0f)
            decel *= brakeForwardFactor;
        decel += Mathf.Max(0f, currentBrakingForce);
        return Mathf.Max(0f, decel);
    }

    /// <summary>
    /// Coast rate from movement config (low/high speed blend, optional exponential, coastDecelFactor).
    /// </summary>
    private float GetArcadeCoastDecel(float speed)
    {
        float low = Mathf.Max(0f, coastLowDecelPerSecond);
        float high = Mathf.Max(low, coastHighDecelPerSecond);
        float top = Mathf.Max(0.01f, effectiveMaxSpeed);
        float speedGate = top * Mathf.Max(0.01f, Mathf.Clamp01(coastHighSpeedFraction));
        float t = Mathf.Clamp01(speed / speedGate);
        float decel = Mathf.Lerp(low, high, t);

        if (useExponentialCoast && coastDampingPerSecond > 0f)
            decel = Mathf.Max(decel, speed * coastDampingPerSecond);

        if (coastDecelFactor > 0f)
            decel *= coastDecelFactor;

        return Mathf.Max(0f, decel);
    }

    /// <summary>
    /// Unity linear drag still runs every FixedUpdate; throttle already cancels the forward part.
    /// Cancel planar/tangent drag during brake/coast so arcade decel knobs control feel.
    /// </summary>
    private void CancelPlanarDrag(float activeDrag)
    {
        if (rb == null || activeDrag <= 0f) return;

        Vector3 n = _lastStableGroundNormal.sqrMagnitude > 1e-8f
            ? _lastStableGroundNormal.normalized
            : Vector3.up;
        Vector3 vTan = Vector3.ProjectOnPlane(rb.velocity, n);
        if (vTan.sqrMagnitude < 1e-6f) return;

        rb.AddForce(vTan * activeDrag, ForceMode.Acceleration);
    }

    /// <summary>
    /// Extra accel along the surface when climbing (gravity-aware). Used with smoothed blending so it doesn't snap on/off.
    /// </summary>
    private bool TryGetSlopeDriveAssist(out Vector3 surfaceForward, out float accelExtra)
    {
        surfaceForward = transform.forward;
        accelExtra = 0f;
        if (!enableSlopeDriveAssist || rb == null) return false;
        if (slopeDriveAssistDisableOnIce && _onIceSurface) return false;

        Vector3 n = _lastStableGroundNormal.sqrMagnitude > 1e-6f ? _lastStableGroundNormal.normalized : Vector3.up;

        float cosTilt = Mathf.Clamp(Vector3.Dot(n, Vector3.up), -1f, 1f);
        float tiltDeg = Mathf.Acos(cosTilt) * Mathf.Rad2Deg;

        // Flat boost pads: optional skip. Steep boost/ramps keep climb assist (pad push alone
        // still fights laggy pitch / gravity cancel timing on elevation).
        if (slopeDriveAssistDisableOnBoost && _onBoostSurface && tiltDeg < slopeDriveAssistMinAngle + 1f)
            return false;

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

    private Vector3 GetGroundCastOrigin()
    {
        return carCollider != null
            ? carCollider.bounds.center + Vector3.up * 0.25f
            : transform.position + Vector3.up * 0.25f;
    }

    private Vector3 ResolveGroundUp(Vector3 castOrigin, float rayDistance, out float groundDistance)
    {
        groundDistance = float.PositiveInfinity;
        if (TryGetGroundNormal(castOrigin, rayDistance, out RaycastHit hit))
        {
            groundDistance = hit.distance;
            _groundNormalMeasured = hit.normal;
            return hit.normal;
        }

        return _lastStableGroundNormal.sqrMagnitude > 1e-8f ? _lastStableGroundNormal : Vector3.up;
    }

    private bool BlocksDrivingOrientation()
    {
        return rb == null
            || _inCrash
            || _isReorienting
            || _flipMashActive
            || IsPostCrashRecoveryDriving
            || IsCrashInvulnerable
            || IsDeadForMashRecovery;
    }

    private bool BlocksAirOrientation()
    {
        // Crash / mash / recovery blocks. Active tricks / residual flip spin also block so
        // upright recovery cannot fight pitch and reverse heading mid-backflip.
        return BlocksDrivingOrientation() || ShouldSuppressAirUprightForTricks();
    }

    /// <summary>
    /// Ground ramp align + passive air upright recovery when not tricking.
    /// </summary>
    private void ApplyDrivingOrientation(float dt)
    {
        // no-op touch for editor remapping
        if (BlocksDrivingOrientation()) return;

        Vector3 castOrigin = GetGroundCastOrigin();
        bool groundedNow = CheckIfGrounded();

        if (groundedNow)
        {
            if (!enableRampAlignment) return;

            Vector3 groundUp = _lastStableGroundNormal.sqrMagnitude > 1e-8f
                ? _lastStableGroundNormal.normalized
                : ResolveGroundUp(castOrigin, groundNormalCheckDistance, out _);

            // While mounting road from grass, don't pitch into curb lips.
            if (IsMixedGrassRoadSurface() && groundUp.y < MixedSurfaceLipNormalMinY)
                groundUp = Vector3.up;

            float groundAlignRate = groundAlignSpeed;
            float tiltDeg = Vector3.Angle(groundUp, Vector3.up);
            if (tiltDeg >= steepAlignMinAngle)
                groundAlignRate *= Mathf.Max(1f, steepAlignSpeedMultiplier);

            AlignToUpVectorPreserveYaw(groundUp, groundAlignRate, dt);
            return;
        }

        if (BlocksAirOrientation()) return;
        if (!enableAirUprightRecovery || !_airborneForTricks) return;

        Vector3 landingUp = ResolveGroundUp(castOrigin, landingPredictDistance, out float groundDist);
        if (Vector3.Dot(transform.up, landingUp) >= airUprightMinAlignDot) return;

        float proximity = float.IsPositiveInfinity(groundDist)
            ? 0f
            : 1f - Mathf.Clamp01(groundDist / Mathf.Max(0.01f, landingPredictDistance));
        float alignSpeed = (airUprightRecoverSpeed + airUprightNearGroundBoost * proximity * proximity)
            * _airUprightRecoverBlend;

        if (alignSpeed <= 0.001f) return;

        AlignToUpVectorPreserveYaw(landingUp, alignSpeed, dt);
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

        projectedForward.Normalize();

        bool airborneNow = _airborneForTricks && !CheckIfGrounded();

        // ONLY when airborne and upside-down: keep upright recovery in the travel hemisphere
        // so mid-flip Slerp doesn't twist 180°.
        // NEVER do this on the ground — reverse velocity points opposite the nose, and flipping
        // projectedForward here made ramp-align spin the car every FixedUpdate while reversing.
        if (airborneNow && Vector3.Dot(transform.up, targetUp) < 0f)
        {
            Vector3 yawRefSource = (rb != null && rb.velocity.sqrMagnitude > 1f) ? rb.velocity : fwd;
            Vector3 yawRef = Vector3.ProjectOnPlane(yawRefSource, targetUp);
            if (yawRef.sqrMagnitude > 0.0001f && Vector3.Dot(projectedForward, yawRef.normalized) < 0f)
                projectedForward = -projectedForward;
        }

        Quaternion targetRot = Quaternion.LookRotation(projectedForward, targetUp);
        float blend = Mathf.Clamp01(alignSpeed * dt);

        if (airborneNow && rb != null)
        {
            ApplyScriptedAirRotation(Quaternion.Slerp(rb.rotation, targetRot, blend));
            return;
        }

        Quaternion newRot = Quaternion.Slerp(transform.rotation, targetRot, blend);
        transform.rotation = newRot;
        if (rb != null)
            rb.MoveRotation(newRot);
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

        if (TryRaycastSurfaceSample(origin, rayDistance, out RaycastHit hit))
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
        ref float bestBoostMaxSpeedMul,
        ref bool boostDuringCrash,
        ref float boostCrashMult)
    {
        float maxMul = 1f;
        float accelMul = 1f;
        float turnMul = 1f;
        float dragMul = 1f;
        float fuelMul = 1f;
        bool isNonDefault = false;

        if (TryRaycastSurfaceSample(origin, rayDistance, out RaycastHit hit))
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
                    if (surface.surfaceType == SurfaceType.Ramp)
                        _sampledBoostIsRamp = true;
                    if (surface.boostAcceleration > bestBoostAccel) bestBoostAccel = surface.boostAcceleration;
                    if (surface.boostMaxSpeed > bestBoostMaxSpeed) bestBoostMaxSpeed = surface.boostMaxSpeed;
                    if (surface.maxSpeedMultiplier > bestBoostMaxSpeedMul) bestBoostMaxSpeedMul = surface.maxSpeedMultiplier;
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
            EnterOutOfHP(1f);

        if (currentFuel <= 0f && !isOutOfFuel)
        {
            isOutOfFuel = true;
            NotifyOutOfFuelTvFlare();
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

        // Match obstacle collision: damage edge flash on every direct crash-FX hit.
        ScreenFlashManager.Damage();

        // Determine impact speed for presentation/physics BEFORE applying lethal HP
        // (TriggerCrash early-outs if already out of HP).
        float speedNow = rb.velocity.magnitude;
        float impactSpeed = (impactSpeedOverride >= 0f) ? impactSpeedOverride : speedNow;
        if (impactSpeedOverride >= 0f)
            impactSpeed = Mathf.Clamp(impactSpeed, 0f, maxImpactSpeed);
        else
            impactSpeed = Mathf.Clamp(impactSpeed, minImpactSpeed, maxImpactSpeed);

        float sev01 = (fxSeverityOverride01 >= 0f)
            ? Mathf.Clamp01(fxSeverityOverride01)
            : Mathf.InverseLerp(minImpactSpeed, maxImpactSpeed, impactSpeed);

        var gm = GameManager_Racing.Instance;
        if (gm != null)
            gm.OnCarCrash(impactSpeed, sev01);

        Vector3 normal = contactNormalWS.sqrMagnitude > 0.0001f ? contactNormalWS.normalized : Vector3.up;
        PlayCrashSfx(crashClipDefault, contactPointWS, crashSfxVolume);
        SpawnCrashImpactVFX(contactPointWS, normal);

        Vector3 hitDir = hitDirectionWS;
        hitDir.y = 0f;
        if (hitDir.sqrMagnitude < 0.0001f) hitDir = -transform.forward;
        hitDir.Normalize();

        float crashDuration = Mathf.Lerp(minCrashDuration, maxCrashDuration, sev01);
        float impulseMag = impactSpeed * impulsePerUnitSpeed;
        float torqueMag = impactSpeed * torquePerUnitSpeed;

        if (maxCrashFlingSpeed > 0f)
            impulseMag = Mathf.Min(impulseMag, maxCrashFlingSpeed);

        // Crash physics while still "alive", then apply HP/fuel (lethal presentation hooks inside).
        TriggerCrash(hitDir, crashDuration, impulseMag, torqueMag, sev01, contactPointWS, false);
        ApplyDirectDamage(hpDamage, fuelPercentOfMax);
    }




    private void ApplySurfaceMultipliers(float maxSpeedMul, float accelMul, float turnMul, float dragMul)
    {
        surfaceTurnMultiplier = Mathf.Max(0f, turnMul);

        float targetMaxSpeed = baseMaxSpeed * maxSpeedMul;

        // Smooth surface transitions to prevent stuttering when the cap drops (e.g. leaving a boost pad).
        // Snap upward immediately so entry onto boost/ramp never lags a low road cap into the speed clamp.
        if (_smoothedSurfaceMaxSpeed < 0f || targetMaxSpeed > _smoothedSurfaceMaxSpeed)
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

        // Do NOT zero cooldowns here — wiping them made a fresh boost fire the instant
        // post-crash lockout ended. Crash already blocks via lockout + input gates.

        // Lock out all boosts for a bit (covers post-crash drift-release + space presses)
        _boostBlockedUntil = Mathf.Max(_boostBlockedUntil, Time.time + Mathf.Max(0f, lockoutSeconds));
    }

    private bool ComputeWantsBoostPresentation()
    {
        if (isOutOfHP || isOutOfFuel)
            return false;

        var gm = GameManager_Racing.Instance;
        if (gm != null && gm.RunEnded)
            return false;

        if (_inCrash || _flipMashActive)
            return false;

        return _isBoosting || HasAnySpeedBoost || _onBoostSurface;
    }

    private void SyncBoostPresentationState()
    {
        bool want = ComputeWantsBoostPresentation();
        if (want == _boostPresentationActive)
            return;

        _boostPresentationActive = want;
        try
        {
            if (want)
                OnBoostStarted?.Invoke();
            else
                OnBoostEnded?.Invoke();
        }
        catch { /* swallow listener errors */ }
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

    /// <summary>
    /// Shared crash gate for obstacles that handle their own <see cref="OnCollisionEnter"/> (e.g. bounce-back props).
    /// Returns false when impact is below <see cref="MinImpactSpeed"/> or standard crash blockers apply.
    /// </summary>
    public bool TryCrashFromObstacleCollision(
        Collision collision,
        out Vector3 contactPoint,
        out Vector3 contactNormal,
        out float impactSpeed)
    {
        contactPoint = transform.position;
        contactNormal = Vector3.up;
        impactSpeed = 0f;

        if (rb == null || collision == null)
            return false;

        if (IsCrashInvulnerable || _isReorienting)
            return false;

        int colliderId = collision.collider.GetInstanceID();
        if (_perColliderCrashTime.TryGetValue(colliderId, out float lastCrashTime)
            && Time.time - lastCrashTime < perColliderCrashCooldown)
        {
            return false;
        }

        if (_inCrash && !_flipMashActive)
            return false;

        if (IsCloseCallInvincible)
        {
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

            return false;
        }

        var immunity = collision.collider.GetComponentInParent<LaunchImmunityMarker>();
        if (immunity != null && immunity.IsImmune)
            return false;

        contactNormal = collision.contactCount > 0
            ? collision.GetContact(0).normal
            : Vector3.up;
        impactSpeed = RefineImpactSpeed(collision, contactNormal, collision.relativeVelocity.magnitude);

        if (collision.contactCount > 0)
        {
            var c = collision.GetContact(0);
            contactPoint = c.point;
            contactNormal = c.normal;
        }
        else
        {
            contactPoint = collision.collider.ClosestPoint(transform.position);
        }

        // Top-face landing: drive atop, no crash (and optional reward).
        if (TryHandleObstacleTopLanding(collision.collider, contactPoint, contactNormal))
            return false;

        if (impactSpeed < minImpactSpeed)
            return false;

        int rootId = collision.collider.transform.root.GetInstanceID();
        _recentCrashRootTime[rootId] = Time.time;
        _perColliderCrashTime[colliderId] = Time.time;
        _closeCallTracking.Remove(rootId);

        return true;
    }

    /// <summary>
    /// True when the contact is on an upward-facing obstacle top and the car is upright enough to drive on it.
    /// </summary>
    private bool IsDriveableObstacleTopContact(Collider other, Vector3 contactNormal)
    {
        if (!enableDriveOnObstacleTops || other == null)
            return false;

        // NPC traffic / creatures are never platforms.
        if (other.GetComponentInParent<NPCTrafficCar>() != null)
            return false;
        if (other.GetComponentInParent<TrackCreature>() != null)
            return false;
        // Diving projectiles should still crash even if the normal looks "up".
        if (other.GetComponentInParent<ThrownObstacle>() != null)
            return false;

        int layerBit = 1 << other.gameObject.layer;
        LayerMask topsMask = driveableObstacleLayers.value != 0 ? driveableObstacleLayers : crashLayers;
        if ((layerBit & topsMask) == 0 && (layerBit & crashLayers) == 0)
            return false;

        float upDot = Vector3.Dot(contactNormal.normalized, Vector3.up);
        if (upDot < obstacleTopNormalDotMin)
            return false;

        if (Vector3.Dot(transform.up, Vector3.up) < obstacleTopCarUpDotMin)
            return false;

        return true;
    }

    /// <summary>
    /// If this is a driveable top landing: award once (cooldown) and skip crash. Returns true when crash should be skipped.
    /// </summary>
    private bool TryHandleObstacleTopLanding(Collider other, Vector3 contactPoint, Vector3 contactNormal)
    {
        if (!IsDriveableObstacleTopContact(other, contactNormal))
            return false;

        TryRewardObstacleTopLanding(other, contactPoint);
        return true;
    }

    private void TryRewardObstacleTopLanding(Collider other, Vector3 contactPoint)
    {
        if (other == null) return;

        Transform root = other.attachedRigidbody != null ? other.attachedRigidbody.transform : other.transform;
        int id = root.GetInstanceID();
        if (_obstacleTopRewardTime.TryGetValue(id, out float last)
            && Time.time - last < Mathf.Max(0.1f, obstacleTopLandingRewardCooldown))
            return;

        _obstacleTopRewardTime[id] = Time.time;

        Vector3 popupPos = contactPoint + Vector3.up * 1.5f;
        if (popupPos.sqrMagnitude < 0.01f)
            popupPos = GetPopupPosition();

        int coins = Mathf.Max(0, obstacleTopLandingCoinReward);
        if (coins > 0)
        {
            if (RacingCoinCollectionHub.Instance != null)
            {
                RacingCoinCollectionHub.Instance.AwardCoins(
                    coins,
                    popupPos,
                    RacingCoinRewardSource.Obstacle);
            }
            else
            {
                GameManager_Racing.Instance?.RegisterCoinPickup(coins);
                if (RacingPopups.IsReady)
                    RacingPopups.Spawn(RacingPopupType.CoinGain, coins, popupPos);
            }
        }

        float fuel = Mathf.Max(0f, obstacleTopLandingFuelReward);
        if (fuel > 0f)
            AddFuel(fuel);

        if (enablePopupText && RacingPopups.IsReady)
            RacingPopups.ObstacleTop(popupPos);
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

        // Finish portal: never take crash damage (obstacles are flung via forcefield shield).
        if (_forcefield != null && _forcefield.IsFinishPortalShield)
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

        if (damageWindowOpen)
            ScreenFlashManager.Damage();

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

        // Can't mash to recover mid-run when out of fuel/HP — only the run-end mash uses that state.
        if (IsDeadForMashRecovery && !_isRunEndMash)
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

    /// <summary>
    /// Stop the equipped forcefield's launch slow-mo (if any). Called by the run-end flow so a
    /// forcefield slow-mo can't linger into the results freeze.
    /// </summary>
    public void CancelForcefieldSlowMo()
    {
        if (_forcefield == null)
            _forcefield = GetComponent<CarForcefield>();
        _forcefield?.CancelLaunchSlowMo();
    }

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
        if (_isRunEndMash)
        {
            if (enableSprocketRewards)
                AwardMashSprockets();

            RacingQuestUnlockManager.Instance?.RecordCrashMashCompletion(1);

            _flipMashActive = false;
            _isRunEndMash = false;
            return;
        }

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

        CancelAllBoostState(PostCrashRecoverySeconds);   // lockout covers full post-crash recovery
        ArmCrashDrivingInputGates();
        _postCrashRecoveryUntil = Time.time + PostCrashRecoverySeconds;
        ClearPostCrashBoostAndSurfaceState();

        // Force road stats + crawl clamp — same as upright FinishCrashDrivingRecovery.
        _smoothedSurfaceMaxSpeed = -1f;
        ApplySurfaceMultipliers(1f, 1f, 1f, 1f);
        ApplySkillEffects();
        KillPlanarSpeedAfterCrashRecovery();

        // Only do upright reorientation if we were actually flipped
        if (_isFlippedDuringRecovery)
        {
            StartReorientToFlat();
            if (_isReorienting)
            {
                if (rb != null)
                    rb.position += Vector3.up * flipUprightLift;
            }
            else
            {
                // Grounded check refused reorient — still restore drive rotation lock.
                FinishCrashDrivingRecovery();
            }
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

        float accelFloor = guaranteeMinimumThrottleAcceleration
            ? minimumAccelerationFloor
            : minimumAccelerationFloor * 0.35f;
        effectiveAcceleration = Mathf.Max(effectiveAcceleration, accelFloor);

        // Slightly dull steering when hurt (optional subtlety on top of accel loss)
        float steerMul = Mathf.Lerp(0.82f, 1f, perfMul);
        effectiveTurnSpeed *= steerMul;
    }

    /// <summary>
    /// Forward throttle acceleration after malfunction scaling. When guaranteed, never below <see cref="minimumAccelerationFloor"/>.
    /// </summary>
    private float GetThrottleAcceleration()
    {
        float accel = effectiveAcceleration * _currentMalfunctionMultiplier;
        if (guaranteeMinimumThrottleAcceleration)
            accel = Mathf.Max(accel, minimumAccelerationFloor);
        else if (useSmoothMalfunction && _currentMalfunctionMultiplier < 1f)
            accel = Mathf.Max(accel, minimumAccelerationFloor * 0.35f);
        return accel;
    }

    /// <summary>
    /// Applies throttle along <paramref name="forward"/> and cancels linear drag on that axis while below max speed.
    /// Without this, accel/drag equilibrium can sit below <see cref="effectiveMaxSpeed"/>, making acceleration feel like a top-speed cap.
    /// </summary>
    private void ApplyForwardThrottleAcceleration(Vector3 forward, float throttleAccel, float activeDrag)
    {
        if (rb == null || throttleAccel <= 0f)
            return;

        if (forward.sqrMagnitude < 1e-8f)
            return;

        forward.Normalize();
        rb.AddForce(forward * throttleAccel, ForceMode.Acceleration);

        float speedCap = GetCurrentSpeedCap();
        if (speedCap <= 0.01f || activeDrag <= 0f)
            return;

        float forwardSpeed = Vector3.Dot(rb.velocity, forward);
        if (forwardSpeed <= 0.01f || forwardSpeed >= speedCap - 0.02f)
            return;

        // Unity's linear drag decelerates proportional to speed; cancel only the forward component while accelerating.
        rb.AddForce(forward * (activeDrag * forwardSpeed), ForceMode.Acceleration);
    }

    private bool TrySampleLandingGroundNormal(out Vector3 groundNormal)
    {
        groundNormal = Vector3.up;
        Vector3 castOrigin = carCollider != null
            ? carCollider.bounds.center + Vector3.up * 0.25f
            : transform.position + Vector3.up * 0.25f;

        if (TryGetGroundNormal(castOrigin, groundNormalCheckDistance, out RaycastHit hit))
        {
            groundNormal = hit.normal;
            return true;
        }

        if (_lastStableGroundNormal.sqrMagnitude > 1e-8f)
            groundNormal = _lastStableGroundNormal;

        return false;
    }

    private bool IsOrientationAcceptableForLanding(Vector3 groundNormal, Vector3 carUp, Vector3 carForward)
    {
        groundNormal = groundNormal.sqrMagnitude > 1e-8f ? groundNormal.normalized : Vector3.up;
        carUp = carUp.sqrMagnitude > 1e-8f ? carUp.normalized : Vector3.up;
        carForward = carForward.sqrMagnitude > 1e-8f ? carForward.normalized : transform.forward;

        float upAlign = Vector3.Dot(carUp, groundNormal);
        if (upAlign < badLandingUpAlignDotMin)
            return false;

        float fwdAlign = Mathf.Abs(Vector3.Dot(carForward, groundNormal));
        if (fwdAlign > badLandingForwardNormalDotMax)
            return false;

        return true;
    }

    private bool ShouldCrashForBadLanding(Vector3 groundNormal, float airTimeAtLanding, Vector3 carUp, Vector3 carForward)
    {
        if (!enableBadLandingCrash || rb == null) return false;
        if (_inCrash || _flipMashActive || IsPostCrashRecoveryDriving) return false;
        if (airTimeAtLanding < badLandingMinAirborneSeconds) return false;
        // Don't soft-crash mid boost-ramp climb — that retains ~32% speed and feels like a full stop.
        if (_onBoostSurface || Time.time < _boostSurfaceHoldUntil)
            return false;
        return !IsOrientationAcceptableForLanding(groundNormal, carUp, carForward);
    }

    private void TriggerBadLandingCrash(Vector3 groundNormal, Vector3 carUp)
    {
        if (rb == null) return;

        groundNormal = groundNormal.sqrMagnitude > 1e-8f ? groundNormal.normalized : Vector3.up;
        carUp = carUp.sqrMagnitude > 1e-8f ? carUp.normalized : transform.up;

        float upAlign = Mathf.Clamp01(Vector3.Dot(carUp, groundNormal));
        float sev01 = Mathf.Clamp01(Mathf.Lerp(badLandingCrashSeverity, 1f, 1f - upAlign));

        float crashDuration = Mathf.Lerp(minCrashDuration, maxCrashDuration, sev01);
        Vector3 contact = carCollider != null ? carCollider.bounds.center : transform.position;

        bool damageWindowOpen = Time.time >= _nextCrashAllowedTime;
        TriggerCrash(groundNormal, crashDuration, 0f, 0f, sev01, contact, damageWindowOpen,
            softLandingCrash: true, softLandingCarUp: carUp, softLandingGroundNormal: groundNormal);
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
        bool landingRewardsBlocked = _blockLandingBoostUntilNextTakeoff
            || _inCrash
            || _flipMashActive
            || _isReorienting
            || IsPostCrashRecoveryDriving;

        if (!groundedNow)
        {
            _airborneContinuousTime += dt;
            _airborneLandingUpSnapshot = transform.up;
            _airborneLandingForwardSnapshot = transform.forward;
            if (_trickStickActive)
                _trickSessionThisJump = true;
        }

        // Store takeoff speed and direction when we leave the ground (e.g. off a ramp)
        if (_wasGroundedLastFrame && !groundedNow)
        {
            _trickSessionThisJump = false;
            ResetAirTrickRotationState();
            _airUprightRecoverBlend = 0f;
            _landingSlipStraightenLeft = 0f;
            _landingSlipStartDirValid = false;
            _driftGroundFeel01 = 0f;

            // Clean takeoff after a crash re-enables landing boost for this new airtime only.
            if (!_inCrash && !_flipMashActive && !_isReorienting && !IsPostCrashRecoveryDriving)
                _blockLandingBoostUntilNextTakeoff = false;

            if (_blockLandingBoostUntilNextTakeoff)
            {
                _takeoffHorizSpeed = 0f;
                _takeoffHorizDir = Vector3.forward;
            }
            else
            {
                Vector3 v = rb.velocity;
                Vector3 horiz = Vector3.ProjectOnPlane(v, Vector3.up);
                _takeoffHorizSpeed = horiz.magnitude;
                _takeoffHorizDir = _takeoffHorizSpeed > 0.1f ? horiz.normalized : transform.forward;
            }
        }

        // Detect landing: was airborne last frame, grounded now
        if (!_wasGroundedLastFrame && groundedNow)
        {
            _lastLandedTime = Time.time;
            float airTimeAtLanding = _airborneContinuousTime;
            _airborneContinuousTime = 0f;

            // Soft-start ground drift if the player was already holding drift through the air.
            bool holdingAirDrift = isDrifting || _driftGlideActive
                || (driftButtonHeld && driftCharge > 0.01f);
            if (holdingAirDrift && driftLandingFeelBlendSeconds > 0.0001f)
                _driftGroundFeel01 = 0f;
            else
                _driftGroundFeel01 = 1f;

            TrySampleLandingGroundNormal(out Vector3 landingNormal);
            bool badLanding = ShouldCrashForBadLanding(
                landingNormal,
                airTimeAtLanding,
                _airborneLandingUpSnapshot,
                _airborneLandingForwardSnapshot);

            _trickSessionThisJump = false;
            ResetAirTrickRotationState();

            if (badLanding)
            {
                TriggerBadLandingCrash(landingNormal, _airborneLandingUpSnapshot);
            }
            else if (landingRewardsBlocked)
            {
                // Crash / recovery airtime: keep touchdown flags, but never reinject ramp speed.
                _landingBoostTimeLeft = 0f;
                _landingBoostTargetMagnitude = 0f;
                _landingExcessSpeed = 0f;
                _takeoffHorizSpeed = 0f;
                _landingSlipStraightenLeft = 0f;
                _landingSlipStartDirValid = false;
            }
            else
            {
                // Soft handoff into ground steer traction — grip ramps via existing blend-in rates
                // instead of appearing when a timed landing straighten window dies.
                _steerTractionBlend = 0f;

                // Soft straighten only when the player is not already owning a turn.
                if (enableLandingSlipStraighten
                    && landingSlipStraightenDuration > 0f
                    && !LandingPlayerOwnsTurn())
                {
                    _landingSlipStraightenDuration = landingSlipStraightenDuration;
                    _landingSlipStraightenLeft = landingSlipStraightenDuration;
                    CaptureLandingSlipStartDirection();
                }
                else
                {
                    _landingSlipStraightenLeft = 0f;
                    _landingSlipStartDirValid = false;
                }

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
                        // Prefer travel / takeoff dir when turning so boost doesn't yank mid-turn to the nose.
                        Vector3 injectDir = currentSpeed > 0.1f
                            ? currentHoriz.normalized
                            : (_takeoffHorizDir.sqrMagnitude > 1e-6f
                                ? Vector3.ProjectOnPlane(_takeoffHorizDir, Vector3.up).normalized
                                : Vector3.ProjectOnPlane(transform.forward, Vector3.up).normalized);
                        if (injectDir.sqrMagnitude < 0.001f)
                            injectDir = transform.forward;
                        injectDir.Normalize();

                        float currentForwardSpeed = Vector3.Dot(currentHoriz, injectDir);
                        float newForwardSpeed = Mathf.Max(currentForwardSpeed, targetSpeed);
                        Vector3 lateral = currentHoriz - injectDir * currentForwardSpeed;
                        Vector3 newHoriz = injectDir * newForwardSpeed + lateral;
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
        }

        // Soft post-landing slip straighten (fading authority — no carry→yank cliff).
        if (enableLandingSlipStraighten && groundedNow && _landingSlipStraightenLeft > 0f)
        {
            ApplyLandingSlipStraightenStep(dt);
            if (_landingSlipStraightenLeft > 0f)
            {
                _landingSlipStraightenLeft -= dt;
                if (_landingSlipStraightenLeft < 0f) _landingSlipStraightenLeft = 0f;
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

        if (enableLandingLateralBleed && groundedNow && landingLateralBleedDuration > 0f
            && Time.time - _lastLandedTime <= landingLateralBleedDuration)
        {
            BleedLandingLateralVelocity(dt);
        }

        UpdateDriftGroundFeel(groundedNow, dt);

        // Update grounded state for next frame (CRITICAL - this was missing in normal flow!)
        _wasGroundedLastFrame = groundedNow;
        if (!_inCrash)
            _isGrounded = groundedNow;
    }

    /// <summary>True when the player is actively steering/drifting — landing straighten must not fight them.</summary>
    private bool LandingPlayerOwnsTurn()
    {
        if (Mathf.Abs(steeringInput) > LandingStraightenAbortSteer)
            return true;
        if (isDrifting || _driftGlideActive)
            return true;
        if (driftButtonHeld && driftCharge > 0.01f)
            return true;
        return false;
    }

    /// <summary>
    /// Soft per-frame slip straighten after landing. Fading authority toward the nose when the
    /// player is not steering — no carry→yank phases that cliff into ground handling mid-turn.
    /// </summary>
    private void ApplyLandingSlipStraightenStep(float dt)
    {
        if (rb == null || dt <= 0f) return;
        if (_landingSlipStraightenDuration <= 0.0001f) return;

        // Mid-turn / drift: hand off immediately to normal ground handling.
        if (LandingPlayerOwnsTurn())
        {
            _landingSlipStraightenLeft = 0f;
            _landingSlipStartDirValid = false;
            return;
        }

        Vector3 vel = rb.velocity;
        Vector3 horiz = Vector3.ProjectOnPlane(vel, Vector3.up);
        float speed = horiz.magnitude;
        if (speed < 0.05f) return;

        Vector3 flatForward = Vector3.ProjectOnPlane(transform.forward, Vector3.up);
        if (flatForward.sqrMagnitude < 1e-6f) return;
        flatForward.Normalize();

        float windowT = Mathf.Clamp01(dt / _landingSlipStraightenDuration);
        float remaining01 = Mathf.Clamp01(_landingSlipStraightenLeft / _landingSlipStraightenDuration);
        float authority = Smooth01(remaining01);

        Vector3 travelDir = horiz / speed;
        Vector3 targetDir = flatForward;

        float angle = Vector3.Angle(travelDir, targetDir);
        if (angle > 0.25f && landingSlipMaxAlignDegrees > 0f && authority > 0.001f)
        {
            float step = Mathf.Min(angle, landingSlipMaxAlignDegrees * windowT * authority);
            Vector3 alignedDir = Vector3.Slerp(travelDir, targetDir, step / angle).normalized;
            horiz = alignedDir * speed;
        }

        float forwardSpeed = Vector3.Dot(horiz, flatForward);
        Vector3 lateral = horiz - flatForward * forwardSpeed;
        float latMag = lateral.magnitude;
        if (latMag > 0.05f && authority > 0.001f)
        {
            float keep = Mathf.Clamp01(landingSlipLateralKeep);
            float lateralReduceT = windowT * authority;
            float targetLat = latMag * Mathf.Lerp(1f, keep, authority);
            float newLatMag = Mathf.MoveTowards(latMag, targetLat, latMag * lateralReduceT);
            horiz = flatForward * forwardSpeed + lateral * (newLatMag / latMag);
        }

        rb.velocity = new Vector3(horiz.x, vel.y, horiz.z);
    }

    private void CaptureLandingSlipStartDirection()
    {
        _landingSlipStartDirValid = false;

        if (rb == null)
            return;

        Vector3 horiz = Vector3.ProjectOnPlane(rb.velocity, Vector3.up);
        if (horiz.sqrMagnitude > 0.05f * 0.05f)
        {
            _landingSlipStartDir = horiz.normalized;
            _landingSlipStartDirValid = true;
            return;
        }

        if (_takeoffHorizDir.sqrMagnitude > 1e-6f)
        {
            _landingSlipStartDir = Vector3.ProjectOnPlane(_takeoffHorizDir, Vector3.up).normalized;
            _landingSlipStartDirValid = _landingSlipStartDir.sqrMagnitude > 1e-6f;
            return;
        }

        Vector3 flatForward = Vector3.ProjectOnPlane(transform.forward, Vector3.up);
        if (flatForward.sqrMagnitude > 1e-6f)
        {
            _landingSlipStartDir = flatForward.normalized;
            _landingSlipStartDirValid = true;
        }
    }

    private static float Smooth01(float t)
    {
        t = Mathf.Clamp01(t);
        return t * t * (3f - 2f * t);
    }

    /// <summary>
    /// Bleeds horizontal velocity sideways component toward car forward after landing
    /// (clears air trajectory banking without killing forward ramp speed).
    /// </summary>
    private void BleedLandingLateralVelocity(float dt)
    {
        if (rb == null || landingLateralBleedPerSecond <= 0f) return;

        Vector3 vel = rb.velocity;
        Vector3 horiz = Vector3.ProjectOnPlane(vel, Vector3.up);
        if (horiz.sqrMagnitude < 0.01f) return;

        Vector3 flatForward = Vector3.ProjectOnPlane(transform.forward, Vector3.up);
        if (flatForward.sqrMagnitude < 1e-6f) return;
        flatForward.Normalize();

        float forwardSpeed = Vector3.Dot(horiz, flatForward);
        Vector3 lateral = horiz - flatForward * forwardSpeed;
        float latMag = lateral.magnitude;
        if (latMag < 0.05f) return;

        float newLatMag = Mathf.MoveTowards(latMag, 0f, landingLateralBleedPerSecond * dt);
        Vector3 newHoriz = flatForward * forwardSpeed + lateral * (newLatMag / latMag);
        rb.velocity = new Vector3(newHoriz.x, vel.y, newHoriz.z);
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

    private void UpdateCrashMashUnlock()
    {
        if (!requireCrashMashUnlock)
        {
            crashMashUnlocked = true;
            return;
        }
        var mgr = RacingSkillTreeManager.Instance;
        crashMashUnlocked = (mgr != null && mgr.GetLevel(SkillType.CrashMashUnlock) > 0);
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

        // === FORCEFIELD / FINISH-PORTAL PROTECTION ===
        // Finish portal: always skip crash and try to fling the obstacle.
        // Normal forcefield: only skip if it actually intercepts/consumes this hit.
        if (_forcefield != null && _forcefield.IsArmed)
        {
            if (((1 << collision.gameObject.layer) & crashLayers) != 0)
            {
                if (_forcefield.IsFinishPortalShield)
                {
                    _forcefield.TryInterceptObstacleForOverlapHit(collision.collider);
                    return;
                }

                if (_forcefield.TryInterceptObstacleForOverlapHit(collision.collider))
                {
                    Debug.Log($"[CarController] Forcefield launched {collision.gameObject.name} - crash skipped");
                    return;
                }
                // Not intercepted by the forcefield -> treat as a normal crash below.
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
        Vector3 contactPointEarly = transform.position;
        if (collision.contactCount > 0)
        {
            var c0 = collision.GetContact(0);
            contactNormalEarly = c0.normal;
            contactPointEarly = c0.point;
        }

        // Landing on the top face of an obstacle: drive atop it (no crash) + reward.
        if (TryHandleObstacleTopLanding(collision.collider, contactPointEarly, contactNormalEarly))
            return;

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

        var hitRollingLog = hitCol.GetComponentInParent<RollingLogAlongTrack>();
        if (hitRollingLog != null)
            impactSpeed = Mathf.Max(impactSpeed, minImpactSpeed);

        if (impactSpeed < minImpactSpeed)
            return;

        if (((1 << collision.gameObject.layer) & crashLayers) == 0)
            return;

        // See OnTriggerEnter: only the aggressive beast crashes the car (handled by
        // TrackCreature.CausePlayerCrash). Non-aggressive creatures (bugs/critters) must not run the
        // car crash pipeline, otherwise splatting one wrongly shows the crash popup.
        var hitCreatureCollision = collision.collider.GetComponentInParent<TrackCreature>();
        if (hitCreatureCollision != null && hitCreatureCollision.BehaviorType != CreatureBehaviorType.Aggressive)
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
        if (wasMashing && damageWindowOpen && !_isRunEndMash)
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

        _rollingLogRamPending = false;
        if (hitRollingLog != null &&
            hitRollingLog.TryGetVehicleRamImpulse(collision, out Vector3 logPlanar, out float logHoriz, out float logUp))
        {
            _rollingLogRamPending = true;
            _rollingLogPlanarUnit = logPlanar;
            _rollingLogHorizImpulse = logHoriz;
            _rollingLogUpImpulse = logUp;
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
    /// Applies crash drag from the current surface. Does not rewrite linear velocity —
    /// the fling must ride out naturally until settled.
    /// </summary>
    private void ApplyCrashSurfaceResistanceFromCurrentGround()
    {
        if (rb == null) return;

        rb.drag = Mathf.Max(0f, currentDrag) * Mathf.Max(0f, crashDragMultiplier);
        rb.angularDrag = crashAngularDrag;
    }

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

        // Creatures manage their own player-contact outcome: the aggressive beast crashes the
        // car via TrackCreature.CausePlayerCrash, while bugs/critters are simply splatted (run-over
        // popup + coins). Only the beast should produce a crash popup, so skip the car crash
        // pipeline for any non-aggressive creature to avoid the wrong "crash" popup on a splat.
        var hitCreatureTrigger = other.GetComponentInParent<TrackCreature>();
        if (hitCreatureTrigger != null && hitCreatureTrigger.BehaviorType != CreatureBehaviorType.Aggressive)
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

        // Targeted forcefield / finish-portal protection (see OnCollisionEnter).
        if (_forcefield != null && _forcefield.IsArmed)
        {
            if (((1 << other.gameObject.layer) & crashLayers) != 0)
            {
                if (_forcefield.IsFinishPortalShield)
                {
                    _forcefield.TryInterceptObstacleForOverlapHit(other);
                    return;
                }

                if (_forcefield.TryInterceptObstacleForOverlapHit(other))
                {
                    Debug.Log($"[CarController] Forcefield launched {other.name} - crash skipped");
                    return;
                }
                // Not intercepted -> fall through to normal crash handling.
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

        // Approximate top landing for trigger-based crash props.
        if (TryHandleObstacleTopLanding(other, contactPoint, approxNormal))
            return;

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
        if (wasMashingTrigger && damageWindowOpen && !_isRunEndMash)
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

    private void UpdateCrashReorientation(float dt)
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

        _reorientElapsed += dt;
        float t = Mathf.Clamp01(_reorientElapsed / reorientDuration);

        Quaternion newRot = Quaternion.Slerp(_reorientStartRot, _reorientTargetRot, t);
        if (rb != null)
            rb.MoveRotation(newRot);
        else
            transform.rotation = newRot;

        if (t >= 1f)
            CompleteCrashReorientation();
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

    private bool IsStoppedForRunEndMash()
    {
        return IsPlanarLinearSpeedBelowThreshold(0.25f);
    }

    /// <summary>
    /// Crash fling has settled enough to upright/recover: nearly stopped planar speed AND grounded
    /// for <see cref="groundedDurationRequired"/>. Never recovers mid-air (that left the car
    /// reoriented / rotation-locked with no real driving control).
    /// </summary>
    private bool IsCrashFlingSettled()
    {
        if (rb == null) return false;

        float minGroundedTime = Mathf.Max(0.05f, groundedDurationRequired);
        if (!_isGrounded || _groundedTime < minGroundedTime)
            return false;

        // On ground, use planar speed so tiny vertical contact noise cannot block recovery forever.
        const float settleSpeed = 0.35f;
        Vector3 planar = Vector3.ProjectOnPlane(rb.velocity, Vector3.up);
        return planar.sqrMagnitude <= settleSpeed * settleSpeed;
    }

    /// <summary>True when horizontal linear speed is near zero (rotation alone does not count as moving).</summary>
    public bool IsStoppedForRunEnd => IsPlanarLinearSpeedBelowThreshold(0.25f);

    private bool IsPlanarLinearSpeedBelowThreshold(float threshold)
    {
        if (rb == null) return true;
        Vector3 planar = Vector3.ProjectOnPlane(rb.velocity, Vector3.up);
        return planar.sqrMagnitude <= threshold * threshold;
    }

    /// <summary>Out of fuel/HP and no meaningful planar motion — block yaw input (coast-to-stop may still steer).</summary>
    private bool BlocksSteeringWhenDeadAndStopped()
    {
        return (isOutOfFuel || isOutOfHP) && IsStoppedForRunEnd;
    }

    private void TryStartRunEndMashIfReady()
    {
        if (_runEndMashOffered) return;
        if (!IsStoppedForRunEndMash()) return;

        _runEndMashOffered = true;

        if (!enableFlipRecoveryMash) return;
        if (!IsOutOfFuel && !IsOutOfHP) return;

        BeginCrashMashRecovery(NeedsFlipRecovery(), runEndMash: true);
    }

    private void ClearEndOfRunDrivingState()
    {
        _inCrash = false;
        CancelAllBoostState(0f);
        _closeCallBoosting = false;
        ForceStopCloseCallEffects();
    }

    private void UpdateFlipMashRecoveryFixedStep(float dt)
    {
        if (!_flipMashActive) return;

        if (enableMashProgressGauge && IsFlipMashUiVisible)
        {
            float severity = _lastCrashSeverity;

            float baseDrainRate = Mathf.Lerp(gaugeDrainAtMinSeverity, gaugeDrainAtMaxSeverity, severity);
            float crashBonus = _crashCount * gaugeDrainPerCrash;
            float finalGaugeDrain = Mathf.Min(baseDrainRate + crashBonus, gaugeDrainCap);
            finalGaugeDrain *= _skillDrainMultiplier;
            _mashGaugeValue = Mathf.Max(0f, _mashGaugeValue - finalGaugeDrain * dt);

            if (!_isRunEndMash && mashDrainsHealth)
            {
                float baseHealthDrain = Mathf.Lerp(mashHealthDrainAtMinSeverity, mashHealthDrainAtMaxSeverity, severity);
                float healthCrashBonus = _crashCount * mashHealthDrainPerCrash;
                float finalHealthDrain = Mathf.Min(baseHealthDrain + healthCrashBonus, mashHealthDrainCap);

                currentHP -= finalHealthDrain * dt;

                if (currentHP <= 0f)
                {
                    currentHP = 0f;
                    _flipMashActive = false;
                    EnterOutOfHP(_lastCrashSeverity > 0.01f ? _lastCrashSeverity : 1f);
                }
            }

            if (!_isRunEndMash && mashDrainsFuel)
            {
                float baseFuelDrain = Mathf.Lerp(mashFuelDrainAtMinSeverity, mashFuelDrainAtMaxSeverity, severity);
                float fuelCrashBonus = _crashCount * mashFuelDrainPerCrash;
                float finalFuelDrain = Mathf.Min(baseFuelDrain + fuelCrashBonus, mashFuelDrainCap);

                currentFuel -= finalFuelDrain * dt;

                if (currentFuel <= 0f)
                {
                    currentFuel = 0f;
                    _flipMashActive = false;
                    bool firstEmpty = !isOutOfFuel;
                    isOutOfFuel = true;
                    if (firstEmpty)
                        NotifyOutOfFuelTvFlare();
                }
            }
        }

        SampleGroundAndUpdateMultipliers();
        ApplyBoostSurfaceForce(true);
        UpdateIcePhysicsTransitions();
        ApplyCrashSurfaceResistanceFromCurrentGround();

        if (effectivePassiveClickRate > 0f && IsFlipMashUiVisible)
        {
            var skillMgr = RacingSkillTreeManager.Instance;
            bool passiveUnlocked = skillMgr != null && skillMgr.IsPassiveMashUnlocked;

            if (passiveUnlocked && effectivePassiveClickRate > 0f)
            {
                _passiveClickTimer += dt;
                float passiveInterval = 1f / effectivePassiveClickRate;

                while (_passiveClickTimer >= passiveInterval)
                {
                    _passiveClickTimer -= passiveInterval;

                    _flipMashClicks += effectivePassiveClickStrength;
                    _lastMashTime = Time.time;

                    if (effectivePassiveClickStrength > 0)
                        TrySpawnPopupRandomScreen(RacingPopupType.MashClickDamage, effectivePassiveClickStrength);

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

        ApplyCrashVelocityCaps();

        if (!_isRunEndMash)
            ConsumeFuel(idleFuelUsePerSecond * dt);
        else if (Time.time - _lastMashTime >= runEndMashIdleTimeout)
        {
            _flipMashActive = false;
            _isRunEndMash = false;
        }
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


    private void BeginCrashMashRecovery(bool isFlipped, bool runEndMash = false)
    {
        if (!enableFlipRecoveryMash) return;
        // Skill-tree gate: the crash mash minigame stays disabled until SkillType.CrashMashUnlock is
        // unlocked (or requireCrashMashUnlock is turned off). When gated off, run-end simply skips the
        // mash and finalizes normally, and mid-run crashes still auto-recover.
        if (!crashMashUnlocked) return;
        if (!runEndMash) return;

        if (!IsOutOfFuel && !IsOutOfHP) return;

        ChooseMashFaceButton();

        _flipMashUiShowAgainTime = 0f;

        _mashGaugeValue = 0f;
        _mashGaugePeakValue = 0f;
        _gaugeMaxedThisSession = false;
        _totalMashClicksThisSession = 0;
        _totalFuelGainedThisSession = 0f;
        _totalSprocketsThisSession = 0;

        _flipMashActive = true;
        _isRunEndMash = runEndMash;
        _isReorienting = false;
        _isFlippedDuringRecovery = isFlipped;
        ArmCrashDrivingInputGates();
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
        if (currentHP > 0f)
        {
            isOutOfHP = false;
            _unableToDrivePresentationPlayed = false;
        }
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

    /// <summary>Current drift charge 0–1; used for drift turn / held-boost buildup (can keep rising with stick released).</summary>
    public float DriftCharge => (_inCrash || _isReorienting) ? 0f : driftCharge;
    public bool IsDrifting => isDrifting && !_inCrash && !_isReorienting;

    /// <summary>
    /// Current left/right steer strength 0–1 (smoothed gameplay steer). Used by camera lag tightening.
    /// </summary>
    public float SteerIntensity => (_inCrash || _isReorienting) ? 0f : Mathf.Clamp01(Mathf.Abs(steeringInput));

    /// <summary>
    /// How hard the player is currently steering into the drift (0–1). Falls when the stick
    /// returns to neutral even if <see cref="DriftCharge"/> keeps building for held-boost.
    /// Airborne drift uses full intensity so camera tilt/shake still work in the air.
    /// </summary>
    public float DriftSteerIntensity
    {
        get
        {
            if (!IsDrifting) return 0f;
            float steer = Mathf.Clamp01(Mathf.Abs(steeringInput));
            if (_airborneForTricks || !CheckIfGrounded())
                return steer;
            return steer * GetDriftGroundFeelEased();
        }
    }

    /// <summary>
    /// 0–1 drift presentation feel for camera (FOV / lag / tremor). Full while airborne drifting;
    /// on ground eases after landing from a mid-air drift hold.
    /// </summary>
    public float DriftGroundFeel
    {
        get
        {
            if (_inCrash || _isReorienting) return 0f;
            if (_airborneForTricks || !CheckIfGrounded())
                return IsDrifting ? 1f : 0f;
            return GetDriftGroundFeelEased();
        }
    }

    /// <summary>
    /// Last non-zero left/right while drifting (+1 / -1). Kept when the stick returns to neutral
    /// so camera tilt direction stays latched until drift ends or direction changes.
    /// </summary>
    public int DriftSteerDirectionSign => IsDrifting ? _driftCurrentSteerSign : 0;

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
        !_inCrash &&
        !_isReorienting &&
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