using System;
using System.Collections.Generic;
using UnityEngine;
using Debug = UnityEngine.Debug;

[RequireComponent(typeof(Rigidbody))]
[RequireComponent(typeof(Collider))]
public class CarController : MonoBehaviour
{
    [Header("Base Movement (on Default surface)")]
    [SerializeField] private float baseAcceleration = 25f;
    [SerializeField] private float baseMaxSpeed = 30f;
    [SerializeField] private float baseBrakingForce = 40f;

    [Header("Steering")]
    [SerializeField] private float turnSpeed = 90f;   // <<< THE ONE TURN SPEED SLIDER

    [SerializeField] private float minSpeedToSteer = 0.6f; // tweak in Inspector
    [SerializeField] private bool allowSteerWhenTryingToMove = true; // W/S lets you steer even if speed is tiny


    [Header("Steering Feel")]
    [SerializeField] private float lowSpeedSteerMultiplier = 1.2f;
    [SerializeField] private float highSpeedSteerMultiplier = 0.4f;
    [SerializeField] private float speedForSteerCurve = 25f;
    [SerializeField] private float steeringInputSmooth = 12f;

    [Header("Arcade Steering Extras")]
    [SerializeField] private bool useAutoAlignToVelocity = false;
    [SerializeField] private float autoAlignStrength = 3f;

    [Header("Ice Steering Ramp")]
    [SerializeField] private bool enableIceSteerRamp = true;
    [SerializeField] private float iceSteerRampUpRate = 2.5f;     // how fast steering "builds"
    [SerializeField] private float iceSteerRampDownRate = 6.0f;   // how fast it falls off when you stop steering
    [SerializeField, Range(0f, 1f)] private float iceSteerMinFactor = 0.15f; // starting steering on ice
    [SerializeField, Range(0f, 1f)] private float iceSteerFlipPenalty = 0.35f;



    [Header("Drift Unlock")]
    [SerializeField] private bool requireDriftUnlock = true; // if true, drift only works after skill unlocked
    private bool driftUnlocked; // runtime flag

    [Header("Drift (Arcade)")]
    [SerializeField] private KeyCode driftKey = KeyCode.LeftShift;
    [SerializeField] private float driftMinSpeed = 5f;
    [SerializeField] private float maxDriftSteerMultiplier = 2.5f;
    [SerializeField] private float driftBuildRate = 1.8f;
    [SerializeField] private float driftReleaseRate = 3.5f;
    [SerializeField] private float driftSideForce = 6f;
    [SerializeField] private float driftSpeedDecayPerSecond = 1.5f;
    [SerializeField, Tooltip("Very small decay while drift key is held (ice feel).")]
    private float driftHeldSpeedDecayPerSecond = 0.15f;
    [SerializeField, Tooltip("Forward acceleration multiplier while drifting or gliding. 1 = same as normal.")]
    private float driftForwardAccelMultiplier = 0.85f; // NEW
    [SerializeField, Tooltip("Use full forward acceleration while holding drift + W (prevents perceived slowdown).")]
    private bool useFullAccelWhileDrifting = true; // NEW
    [SerializeField, Tooltip("Preserve highest speed reached during current drift/glide while drift key held.")]
    private bool lockToDriftPeakSpeed = true; // NEW

    // NEW: gentle deceleration while drifting if S is held (softer than normal braking)
    [Header("Drift Braking")]
    [SerializeField, Tooltip("Per-second speed decay while drifting and holding S. Lower = softer, preserves ice feel.")]
    private float driftBrakeDecayPerSecond = 0.6f;



    [Header("Drift Neutral Behavior")]
    [Tooltip("Require a non-zero steering input (above steerFlipThreshold) to build/maintain drift charge. Releasing steering while holding drift will drain the charge.")]
    [SerializeField] private bool requireDirectionalInputForDriftCharge = true;
    [Tooltip("Drain rate while drift key held but no steering (if requireDirectionalInputForDriftCharge = true). If <= 0 uses driftReleaseRate.")]
    [SerializeField] private float driftNeutralDrainRate = 4.2f;

    [Header("Drift Neutral Reset")]
    [Tooltip("If you let go of steering for this long while holding drift, driftCharge fully resets so re-engaging is a fresh drift.")]
    [SerializeField] private float driftNeutralFullResetDelay = 0.15f;

    [Header("Drift Direction Change Reset")]
    [Tooltip("If true, changing steering direction while holding drift will reset (or reduce) drift charge so direction change isn’t a snap turn.")]
    [SerializeField] private bool resetDriftChargeOnSteerFlip = true;
    [Tooltip("Portion of drift charge retained after a direction flip (0 = full reset).")]
    [SerializeField, Range(0f, 1f)] private float steerFlipRetainedCharge = 0f;
    [Tooltip("Minimum absolute steering input required to read a sign (+/-) for flip detection.")]
    [SerializeField, Range(0f, 1f)] private float steerFlipThreshold = 0.20f;
    [Tooltip("Minimum drift charge required before a direction flip can trigger a reset (prevents tiny wiggles).")]
    [SerializeField, Range(0f, 1f)] private float minChargeForFlipReset = 0.15f;
    [Tooltip("Delay after a flip before drift can start rebuilding (seconds).")]
    [SerializeField, Range(0f, 1f)] private float steerFlipRebuildDelay = 0.15f;

    [Header("Drift Glide (Ice Feel)")]
    [Tooltip("Allow holding the drift key without steering to preserve most of entry speed (ice-like glide).")]
    [SerializeField] private bool allowDriftGlideWithoutSteer = true;
    [Tooltip("Per-second decay while gliding (very small to keep speed).")]
    [SerializeField] private float driftGlideDecayPerSecond = 0.05f;

    [Header("Ice Surface Transition")]
    [Tooltip("How fast friction lerps to/from ice values (higher = faster transition).")]
    [SerializeField] private float iceFrictionTransitionSpeed = 3f;

    [Tooltip("How fast handling lerps to/from ice values (higher = faster transition).")]
    [SerializeField] private float iceHandlingTransitionSpeed = 4f;

    // NEW: Ice drift-like physics
    [Tooltip("Lateral force applied when steering on ice (mimics drift slide).")]
    [SerializeField] private float iceLateralSlideForce = 4f;

    [Tooltip("How strongly velocity aligns to car forward when on ice (lower = more slide).")]
    [SerializeField] private float iceVelocityAlignmentStrength = 2f;

    private bool _driftGlideActive;          // NEW: glide mode (holding drift, no steer)

    private float _driftNeutralTimer = 0f;

    private float _lastRawSteerValue;
    private int _driftCurrentSteerSign = 0;
    private float _driftFlipBlockUntil = 0f;

    [Header("Base Physics")]
    [SerializeField] private float baseDrag = 0.08f;

    [Header("Ground Detection")]
    [SerializeField] private LayerMask groundLayers = ~0;
    [SerializeField] private int samplesX = 2;
    [SerializeField] private int samplesZ = 4;
    [SerializeField] private float raycastHeightOffset = 0.5f;
    [SerializeField] private float raycastExtraDistance = 2f;
    [SerializeField] private bool debugSurfaceRays = false;

    [Tooltip("How far ground samples stretch from the collider center.\n0.5 = inner half, 1 = full collider extents, 1.5 = 50% beyond the collider, etc.")]
    [SerializeField] private float surfaceSampleExtent = 1f;

    [Header("Fuel Settings")]
    [SerializeField] private float maxFuel = 100f;
    [SerializeField] private float fuelUsePerSecondAtFullThrottle = 5f;
    [SerializeField] private float fuelUsePerSecondBraking = 3f;
    [SerializeField] private float idleFuelUsePerSecond = 0.5f;
    [Tooltip("Speed (m/s) below which we consider the car 'idle' for idle fuel consumption.")]
    [SerializeField] private float idleSpeedThreshold = 0.5f;

    [Header("Crash / Hit Reaction")]
    [SerializeField] private LayerMask crashLayers;
    [SerializeField] private float minImpactSpeed = 4f;
    [SerializeField] private float maxImpactSpeed = 25f;
    [SerializeField] private float minCrashDuration = 0.15f;
    [SerializeField] private float maxCrashDuration = 1.1f;
    [SerializeField] private float impulsePerUnitSpeed = 0.6f;
    [SerializeField] private float torquePerUnitSpeed = 0.45f;
    [SerializeField] private float crashDragMultiplier = 2f;
    [SerializeField] private float crashAngularDrag = 1.5f;

    public float MinImpactSpeed => minImpactSpeed;
    public float MaxImpactSpeed => maxImpactSpeed;

    [Header("Crash Spin Tuning")]
    [SerializeField] private float crashYawTorqueMultiplier = 1f;
    [SerializeField] private float crashRollTorqueMultiplier = 0.6f;
    [SerializeField] private float crashPitchTorqueMultiplier = 0.35f;

    [Header("Crash Recovery")]
    [SerializeField] private float reorientDuration = 0.6f;
    [SerializeField, Tooltip("Time (seconds) the car must remain grounded before crash recovery begins.")]
    private float groundedDurationRequired = 0.5f;
    [SerializeField, Tooltip("Distance threshold for ground check raycast.")]
    private float groundCheckDistance = 0.3f;
    [SerializeField, Tooltip("Layers considered as ground for recovery check.")]
    private LayerMask groundCheckLayers = ~0;

    [Header("Steering Direction")]
    [SerializeField] private bool invertSteeringWhenReversing = false;
    [SerializeField] private float reverseSteerMultiplier = 1f;

    // ─────────────────────────────────────────────
    // HEALTH SYSTEM
    // ─────────────────────────────────────────────
    [Header("Health")]
    [SerializeField] private float maxHP = 100f;
    [SerializeField] private float hpCrashDamageAtSeverity1 = 40f;
    [Tooltip("Optional passive HP regen per second when not crashing. 0 = none.")]
    [SerializeField] private float hpRegenPerSecond = 0f;

    [Header("Performance Degradation (vs HP)")]
    [SerializeField, Range(0.1f, 1f)] private float performanceAtZeroHP = 0.35f;
    [SerializeField, Range(0f, 1f)] private float degradeStartHPFraction = 0.75f;

    [Header("Input Malfunction (Low HP)")]
    [SerializeField] private bool enableDamageMalfunction = true;
    [SerializeField, Range(0f, 1f)] private float maxMalfunctionChancePerSecond = 0.5f;
    [SerializeField] private Vector2 malfunctionBurstDuration = new Vector2(0.2f, 0.8f);
    [SerializeField] private Vector2 malfunctionCooldown = new Vector2(0.4f, 1.2f);

    [Header("Crash Penalties")]
    [SerializeField] private float fuelLossAtSeverity1 = 20f;
    [SerializeField, Tooltip("Minimum HP deducted per crash (applies after severity scaling).")]
    private float minHpLossPerCrash = 10f;
    [SerializeField, Tooltip("Minimum fuel deducted per crash (applies after severity scaling).")]
    private float minFuelLossPerCrash = 10f;

    [Header("Crash Cooldown")]
    [SerializeField, Tooltip("Seconds of invulnerability after taking crash damage.")]
    private float crashDamageCooldown = 0.75f;
    private float _nextCrashAllowedTime = 0f; // runtime timer gate

    [Header("Crash Impact VFX")]
    [Tooltip("Optional explosion/impact VFX prefab to spawn at the car collision point.")]
    [SerializeField] private GameObject crashImpactVFX;
    [Tooltip("Lifetime of the spawned VFX (seconds) when instantiated or returned to pool).")]
    [SerializeField] private float crashImpactVFXLifetime = 4f;

    [Header("Damage Smoke (VFX)")]
    [Tooltip("Smoke VFX that scales with damage.")]
    [SerializeField] private ParticleSystem damageSmokeVFX;
    [Tooltip("HP fraction where smoke begins (e.g. 0.75 = starts below 75% HP).")]
    [SerializeField, Range(0f, 1f)] private float smokeStartHPFraction = 0.75f;
    [SerializeField] private float smokeMinRate = 4f;
    [SerializeField] private float smokeMaxRate = 60f;
    [Tooltip("Particle start size at threshold HP (first appearance).")]
    [SerializeField] private float smokeMinSize = 0.5f;
    [Tooltip("Particle start size at 0 HP.")]
    [SerializeField] private float smokeMaxSize = 2.0f;
    [Tooltip("Color when smoke first appears (higher HP side).")]
    [SerializeField] private Color smokeColorAtThreshold = Color.white;
    [Tooltip("Color when car is at 0 HP (fully damaged).")]
    [SerializeField] private Color smokeColorAtZeroHP = Color.gray;
    [Tooltip("Invert color lerp direction if you want threshold to be gray and low HP to be white.")]
    [SerializeField] private bool invertSmokeColorLerp = false;

    // ─────────────────────────────────────────────
    // BOOST SYSTEM
    // ─────────────────────────────────────────────

    // Unlock gating
    [Header("Boost Unlock")]
    [SerializeField] private bool requireBoostUnlock = true;
    private bool boostUnlocked;

    [Header("Boost")]
    [SerializeField] private KeyCode boostKey = KeyCode.Space;
    [SerializeField] private float boostForce = 50f;
    [SerializeField] private float boostSustainAcceleration = 0f;
    [SerializeField] private float boostDuration = 1.2f;
    [SerializeField] private float boostMaxSpeedMultiplier = 1.35f;
    [SerializeField] private float postBoostSlowdownDuration = 0.75f;
    [SerializeField] private float boostCooldown = 1.25f;
    [SerializeField] private float boostFuelCost = 0f;
    [SerializeField] private float driftBoostSustainAcceleration = 30f;

    // NEW: Drift-held Boost configuration
    [Header("Drift-held Boost")]
    [SerializeField, Tooltip("Enable boost scaling based on how long drift is held in one direction.")]
    private bool enableDriftHeldBoost = true;
    [SerializeField, Tooltip("Minimum drift hold time (s) before any boost reward applies.")]
    private float driftBoostMinHoldSeconds = 0.35f;
    [SerializeField, Tooltip("Maximum drift hold time (s) that maps to max rewards.")]
    private float driftBoostMaxHoldSeconds = 2.00f;
    [SerializeField, Tooltip("Boost force range mapped from min→max hold.")]
    private Vector2 driftBoostForceRange = new Vector2(25f, 75f);
    [SerializeField, Tooltip("Boost duration range mapped from min→max hold.")]
    private Vector2 driftBoostDurationRange = new Vector2(0.35f, 1.50f);
    [SerializeField, Tooltip("Max speed multiplier range mapped from min→max hold.")]
    private Vector2 driftBoostMaxSpeedMultRange = new Vector2(1.10f, 1.60f);
    [SerializeField, Tooltip("Fuel cost applied when drift boost triggers (currently ignored: drift boost is free).")]
    private float driftBoostFuelCost = 0f;

    [Header("Drift-held Boost Cooldown")]
    [SerializeField, Tooltip("Cooldown (seconds) applied after using a drift-held boost (separate from normal boost cooldown).")]
    private float driftBoostCooldown = 1.25f; // default; can be overridden via skill tree

    // -------------------- SCREEN SHAKE (receiver) --------------------
    [Header("Screen Shake (Receiver)")]
    [SerializeField, Tooltip("Best: assign a camera pivot/rig that is NOT overwritten by your follow script. Fallback: Camera.main.")]
    private Transform cameraShakeTarget;

    [SerializeField, Tooltip("Overall multiplier for ALL shakes (0 = off).")]
    private float screenShakeGlobalMultiplier = 1f;

    [SerializeField, Tooltip("How quickly shake eases back to zero when no requests come in.")]
    private float screenShakeReturnSpeed = 18f;

    [Header("Verticality / Ramp Alignment")]
    [SerializeField] private bool enableRampAlignment = true;

    [SerializeField, Tooltip("How fast we align to ground normal while grounded.")]
    private float groundAlignSpeed = 10f;

    [SerializeField, Tooltip("How fast we align in air when we can predict the landing normal.")]
    private float airAlignSpeed = 6f;

    [SerializeField, Tooltip("Spherecast radius for ground normal sampling.")]
    private float groundNormalCastRadius = 0.35f;

    [SerializeField, Tooltip("How far down we check for ground normal (grounded case).")]
    private float groundNormalCheckDistance = 2.0f;

    [SerializeField, Tooltip("How far ahead/under we look for an upcoming landing surface while airborne.")]
    private float landingPredictDistance = 6.0f;

    [SerializeField, Tooltip("Only start 'landing normal' alignment when we're within this distance to the ground hit.")]
    private float landingAlignStartDistance = 3.0f;

    [Header("Death VFX")]
    [SerializeField] private GameObject deathVFX;
    [SerializeField, Tooltip("Lifetime of death VFX before cleanup.")]
    private float deathVFXLifetime = 8f;

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
    private bool _deathVfxPlayed = false;

    public event Action OnBoostStarted;
    public event Action OnBoostEnded;

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

    private Quaternion _initialRotation;
    private bool _isReorienting;
    private float _reorientElapsed;
    private Quaternion _reorientStartRot;
    private Quaternion _reorientTargetRot;

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

    private bool IsCrashInvulnerable => _inCrash || _isReorienting;

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

    [Header("Fuel Modifiers by Surface")]
    [SerializeField] private float grassFuelUseMultiplier = 1.5f;

    private float currentAcceleration;
    private float currentMaxSpeed;
    private float currentBrakingForce;
    private float currentTurnSpeed;
    private float currentDrag;

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

    [Header("Debug (read-only)")]
    [SerializeField] private float offDefaultFraction = 0f;
    [SerializeField] private float grassFraction = 0f;

    private SkillApplicationMode accelMode;
    private float accelValue;
    private SkillApplicationMode maxSpeedMode;
    private float maxSpeedValue;
    private SkillApplicationMode steerMode;
    private float steerValue;
    private SkillApplicationMode fuelMode;
    private float fuelValue;

    [Header("Arcade Coasting")]
    [SerializeField, Tooltip("Base deceleration (m/s per second) applied when you release W and are not braking or drifting.")]
    private float coastLowDecelPerSecond = 1.2f;
    [SerializeField, Tooltip("Extra deceleration at high speed (m/s per second) blended in as speed approaches max.")]
    private float coastHighDecelPerSecond = 3.5f;
    [SerializeField, Tooltip("Speed fraction (0..1) where high speed decel fully applies.")]
    private float coastHighSpeedFraction = 0.8f;
    [SerializeField, Tooltip("If true, use exponential damping instead of linear MoveTowards (slightly smoother).")]
    private bool useExponentialCoast = false;
    [SerializeField, Tooltip("Exponential damping factor (per second) when useExponentialCoast=true.")]
    private float coastDampingPerSecond = 2.0f;

    [Header("Arcade Movement Tuning")]
    [SerializeField] private float coastDecelFactor = 0.1f;
    [SerializeField] private float brakeForwardFactor = 0.7f;
    [SerializeField] private float reverseAccelFactor = 0.8f;
    [SerializeField] private float brakeToReverseSpeed = 0.5f;

    // NEW: caps so braking can’t be insanely hard at low max speeds
    [SerializeField, Tooltip("Maximum forward braking decel (m/s^2) when holding S. Lower = softer, longer stops.")]
    private float maxBrakeDecelPerSecond = 5f;
    [SerializeField, Tooltip("Maximum reverse acceleration (m/s^2) when transitioning into reverse.")]
    private float maxReverseAccelPerSecond = 4f;

    [SerializeField] private float baseSteeringDamp = 1f;
    private float currentSteeringDamp;

    // ─────────────────────────────────────────────
    // NEW: Steering traction while coasting (no throttle/brake, no drift)
    // ─────────────────────────────────────────────
    [Header("Steer Rolling Traction")]
    [SerializeField, Tooltip("Enable steering traction/forward roll while coasting.")]
    private bool enableSteerTraction = true;
    [SerializeField, Tooltip("How fast velocity direction blends toward forward when steering without throttle. Higher = snappier.")]
    private float steerTractionReorientRate = 6f;
    [SerializeField, Tooltip("Small forward acceleration applied while steering with no throttle, to mimic tires rolling.")]
    private float steerRollingAccel = 2.25f;
    [SerializeField, Tooltip("Minimum speed required to apply steer traction.")]
    private float minSpeedForSteerTraction = 0.1f;
    [SerializeField, Tooltip("Extra lateral damping while steering with no throttle (reduces sideways slip).")]
    private float lateralFrictionWhileSteering = 3.5f;

    [SerializeField, Tooltip("How quickly coasting-steer traction fades IN (per second).")]
    private float steerTractionBlendIn = 10f;

    [SerializeField, Tooltip("How quickly coasting-steer traction fades OUT (per second).")]
    private float steerTractionBlendOut = 14f;

    private float _steerTractionBlend = 0f;

    [SerializeField, Range(0f, 2f), Tooltip("Scales steerRollingAccel when NOT holding throttle/brake. 1 = current behavior.")]
    private float steerRollingAccelCoastMultiplier = 1f;

    [SerializeField, Tooltip("If true, steerRollingAccel is also applied on ice. If false, coasting-steer won't add forward push on ice.")]
    private bool applySteerRollingAccelOnIce = false;

    private bool _inputsSuppressedThisFrame = false;

    // NEW: split suppression so steering is never fully blocked by malfunction
    private bool _suppressThrottleBrakeThisFrame = false;
    private bool _suppressSteeringThisFrame = false;

    // ------------------------------------------------------------------------
    // NEW: global near-miss / close-call detection for ALL obstacles
    // Triggers the same close-call slowmo/postfx used by thrown projectiles.
    // ------------------------------------------------------------------------
    [Header("Close-Call Near-Miss (global)")]
    [SerializeField, Tooltip("Enable near-miss slow-mo/postFX when driving close to any obstacle.")]
    private bool enableCloseCallNearMisses = true;
    [SerializeField, UnityEngine.Min(0f), Tooltip("Distance (meters) within which a passing obstacle is considered a close call.")]
    private float closeCallDistance = 3.5f;
    [SerializeField, UnityEngine.Min(0f), Tooltip("Minimum car forward speed (m/s) required to consider a close call (reduces false positives when standing).")]
    private float closeCallMinSpeed = 4f;
    [SerializeField, UnityEngine.Min(0f), Tooltip("Cooldown (seconds) per obstacle to avoid repeated close-call triggers (legacy per-collider fallback).")]
    private float closeCallCooldown = 1.0f;

    [SerializeField, UnityEngine.Min(0f), Tooltip("Cooldown (seconds) per obstacle ROOT to avoid repeated close-call triggers (preferred).")]
    private float closeCallRootCooldown = 3.0f;

    [Header("Crash Sound Effects")]
    [SerializeField] private AudioClip crashClipDefault;
    [SerializeField] private AudioClip crashClipHonk; // used for CrossTrackObstacle collisions
    [SerializeField, Range(0f, 1f)] private float crashSfxVolume = 1f;

    // Add these serialized fields near the "Crash Sound Effects" group (where crashClipDefault/crashClipHonk are declared)
    [Header("Crash Sound Spatialization")]
    [SerializeField, Tooltip("If true, crash sounds are spatial (3D). If false they play as 2D UI-like sounds.")]
    private bool crashUseSpatial = true;
    [SerializeField, Range(0f, 1f), Tooltip("When spatial, how much 3D panning is applied (0 = 2D, 1 = full 3D).")]
    private float crashSpatialBlend = 1f;
    [SerializeField, Tooltip("Rolloff mode used for crash SFX when spatialized.")]
    private AudioRolloffMode crashRolloff = AudioRolloffMode.Logarithmic;
    [SerializeField, Tooltip("Min distance for spatial attenuation.")]
    private float crashMinDistance = 1f;
    [SerializeField, Tooltip("Max distance for spatial attenuation.")]
    private float crashMaxDistance = 50f;
    [SerializeField, Range(0f, 3f), Tooltip("Global multiplier for crash SFX loudness to compensate for quiet audio.")]
    private float crashVolumeMultiplier = 1.6f;

    [Header("Crash Pitch Variation")]
    [SerializeField, Range(0.5f, 2f), Tooltip("Minimum random pitch applied to crash SFX.")]
    private float crashPitchMin = 0.95f;
    [SerializeField, Range(0.5f, 2f), Tooltip("Maximum random pitch applied to crash SFX.")]
    private float crashPitchMax = 1.05f;

    [Header("Surface Transition Smoothing")]
    [SerializeField, Tooltip("How fast the surface max speed lerps toward a new, slower surface. Higher = faster, 0 = snap instantly.")]
    private float surfaceMaxSpeedLerpRate = 2.0f;  // try 1.5–4 for a nice slide

    private float _smoothedSurfaceMaxSpeed = -1f;



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

    [Header("Close-Call vs Crash Resolution")]
    [SerializeField, UnityEngine.Min(0f), Tooltip("After crashing into an obstacle, block near-miss rewards for that root for this many seconds.")]
    private float closeCallAfterCrashBlockTime = 1.0f;

    // NEW: split for steering traction code etc.
    private void Awake()
    {

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



        UpdateCrashReorientation();
        HandleInput();

        if (Input.GetKeyDown(boostKey) && !IsCrashInvulnerable && Time.time >= _boostBlockedUntil)
            _boostRequested = true;

        if (!_inCrash && hpRegenPerSecond > 0f && currentHP < maxHP)
        {
            currentHP = Mathf.Min(maxHP, currentHP + hpRegenPerSecond * Time.deltaTime);
        }

        if (currentHP <= 0f && !isOutOfHP)
        {
            isOutOfHP = true;

            // Hard stop boost immediately
            _isBoosting = false;
            _isPostBoost = false;
            _boostRequested = false;

            // Kill sustained acceleration effects
            rb.velocity *= 0.25f;

            PlayDeathVFX();
        }

        UpdateDamageVFXImmediate();
    }

    private void FixedUpdate()
    {
        float dt = Time.fixedDeltaTime;

        if (_inCrash)
        {
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

            _crashTimer -= dt;

            // Only exit crash state if timer is up AND we've been grounded long enough
            if (_crashTimer <= 0f && _groundedTime >= groundedDurationRequired)
            {
                _inCrash = false;

                if (rb != null)
                {
                    rb.freezeRotation = true;
                    rb.drag = _baseDrag;
                    rb.angularDrag = _baseAngularDrag;
                    rb.angularVelocity = Vector3.zero;
                }

                if (IsOutOfFuel || IsOutOfHP)
                {
                    _inCrash = false;           // exit crash state so timers stop
                    rb.freezeRotation = false;  // keep physics natural (optional)
                    rb.drag = _baseDrag;
                    rb.angularDrag = _baseAngularDrag;
                    return;
                }

                _isReorienting = true;
                _reorientElapsed = 0f;
                _reorientStartRot = transform.rotation;

                Vector3 euler = transform.eulerAngles;
                _reorientTargetRot = Quaternion.Euler(0f, euler.y, 0f);

                // Reset grounded tracking
                _groundedTime = 0f;
            }
            return;
        }

        SampleGroundAndUpdateMultipliers();
        RefreshSkillEffects();
        ApplySkillEffects();
        UpdateSteeringInputFixed();
        HandleSteering();
        HandleMovement();
        HandleBoost();
        UpdateIcePhysicsTransitions();
        ApplyRampAlignment(Time.fixedDeltaTime);

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
        _shakeBlendAmp = Mathf.MoveTowards(_shakeBlendAmp, _shakeAmp, Time.deltaTime * 60f);

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

        if (onIceOrTransitioning && !_inCrash && !_isReorienting && rb != null)
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

    }

    private float GetCurrentSpeedCap()
    {
        float normalCap = effectiveMaxSpeed;
        float maxMult = _isBoosting ? Mathf.Max(1f, _activeBoostMaxMult) : 1f;

        float boostedCap = normalCap * maxMult;

        if (_isPostBoost && postBoostSlowdownDuration > 0f)
        {
            float t = 1f - Mathf.Clamp01(_postBoostTimer / postBoostSlowdownDuration);
            return Mathf.Lerp(boostedCap, normalCap, t);
        }
        return _isBoosting ? boostedCap : normalCap;
    }

    private void ClearBoostOverride()
    {
        _boostOverrideActive = false;
        _overrideIsDriftBoost = false;
        _boostOverrideForce = 0f;
        _boostOverrideDuration = 0f;
        _boostOverrideMaxMult = 0f;
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

        if (isOutOfHP)
        {
            steeringInput = 0f;
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

        if (isOutOfFuel)
        {
            driftCharge = 0f;
            isDrifting = false;
            driftButtonHeld = false;

            _boostRequested = false;
            _boostOverrideActive = false;

            _inputsSuppressedThisFrame = true;
            _suppressThrottleBrakeThisFrame = true;
            _suppressSteeringThisFrame = false;

            // IMPORTANT: do NOT return; we still want to read/update steeringInput below.
        }


        if (_malfunctionTimer > 0f)
            _malfunctionTimer -= Time.deltaTime;
        if (_malfunctionCooldownRemain > 0f)
            _malfunctionCooldownRemain -= Time.deltaTime;

        _rawSteer = Input.GetAxisRaw("Horizontal");
        float rawHorizontal = _rawSteer; // keep the rest of your logic working
        float speed = rb != null ? rb.velocity.magnitude : 0f;
        bool prevDriftKeyHeld = driftButtonHeld;

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
            driftButtonHeld = Input.GetKey(driftKey);
            bool canDriftThisFrame = driftButtonHeld && speed >= driftMinSpeed;

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
                // Build time as long as drift key + a steering direction are held (independent of driftCharge / speed)
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

                // Trigger boost ONLY on drift key release (protects against crash or speed loss firing it)
                if (!driftButtonHeld && prevDriftKeyHeld)
                {
                    TryTriggerDriftHeldBoost();
                    ResetDriftHeldTimer();
                }

                // If no direction while still holding drift, do not accumulate further (but keep current hold until release)
                if (driftButtonHeld && _driftCurrentSteerSign == 0)
                {
                    // Optionally could slowly decay; for now just pause accumulation.
                }

                // Hard reset if not holding drift at all
                if (!driftButtonHeld)
                {
                    _driftHoldDirectionSign = 0;
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
                driftEntrySpeed = 0f;
                driftClampSpeed = 0f;
                driftPeakSpeed = 0f;
            }
        }

        _driftWasActiveLastFrame = isDrifting;

        // NEW: Glide logic (ice feel) – if holding drift with no directional charge but above speed threshold.
        if (allowDriftGlideWithoutSteer)
        {
            bool canGlide = driftButtonHeld && !isDrifting && speed >= driftMinSpeed;
            if (canGlide)
            {
                if (!_driftGlideActive)
                {
                    driftEntrySpeed = speed;
                    driftClampSpeed = driftEntrySpeed;
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

        float smoothRate = steeringInputSmooth;
        if (isDrifting) smoothRate *= 1.4f;

        // Apply temporary handling boost: reduce smoothing (i.e. make steering snappier)
        float handlingMult = GetTemporaryHandlingMultiplier();
        // dividing smoothing rate by handling multiplier => lower smoothing = snappier input
        if (handlingMult > 1f)
            smoothRate /= handlingMult;

        bool suppressInputs = false;
        if (enableDamageMalfunction)
        {
            float hpFrac = HPPercent;
            float dmgT = Mathf.Clamp01((degradeStartHPFraction - hpFrac) / Mathf.Max(0.0001f, degradeStartHPFraction));
            float chancePerSec = Mathf.Lerp(0f, maxMalfunctionChancePerSecond, dmgT);

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

            suppressInputs = _malfunctionTimer > 0f;
        }

        // ADJUSTMENT: never fully suppress steering; only throttle/brake during malfunction
        _suppressThrottleBrakeThisFrame = suppressInputs;
        _suppressSteeringThisFrame = false; // keep steering responsive even during malfunction

        // Smooth steering input (steering is never forced to 0 anymore)
        float targetSteer = _suppressSteeringThisFrame ? 0f : rawHorizontal;
        float smoothDelta = smoothRate * Time.deltaTime;

        // Don't lerp if target and current are very close (prevents micro-jitter)
        if (Mathf.Abs(targetSteer - steeringInput) < 0.01f)
            steeringInput = targetSteer;
        else
            steeringInput = Mathf.MoveTowards(steeringInput, targetSteer, smoothDelta);

        _inputsSuppressedThisFrame = _suppressThrottleBrakeThisFrame;
    }

    private void TryTriggerDriftHeldBoost()
    {
        if (Time.time < _boostBlockedUntil) return;
        if (!enableDriftHeldBoost) return;
        if (_inCrash) return; // prevent accidental boost trigger after a crash interruption

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

        CancelAllBoostState(crashDuration + reorientDuration + 0.1f);

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

            float hpBefore = currentHP;
            float fuelBefore = currentFuel;


            if (hpCrashDamageAtSeverity1 > 0f)
            {
                float hpLoss = Mathf.Max(minHpLossPerCrash, hpCrashDamageAtSeverity1 * sev01ForDamage);
                hpLoss = Mathf.Min(hpLoss, currentHP);
                currentHP = Mathf.Max(0f, currentHP - hpLoss);
                Debug.Log($"[CarController] Crash damage applied: -{hpLoss} HP (sev={sev01ForDamage:F2}). HP={currentHP}/{maxHP}");
            }

            if (fuelLossAtSeverity1 > 0f)
            {
                float requestedFuelLoss = Mathf.Max(minFuelLossPerCrash, fuelLossAtSeverity1 * sev01ForDamage);
                float before = currentFuel;
                ConsumeFuel(requestedFuelLoss);
                float consumed = Mathf.Max(0f, before - currentFuel);

                if (consumed + 1e-3f < minFuelLossPerCrash)
                {
                    float shortfall = minFuelLossPerCrash - consumed;
                    currentFuel = Mathf.Max(0f, currentFuel - shortfall);
                }

                Debug.Log($"[CarController] Crash fuel loss applied (sev={sev01ForDamage:F2}). Fuel={currentFuel}/{maxFuel}");
            }

            bool lethalFromThisCrash =
    (hpBefore > 0f && currentHP <= 0f) ||
    (fuelBefore > 0f && currentFuel <= 0f);

            if (lethalFromThisCrash)
            {
                var gm = GameManager_Racing.Instance;
                if (gm != null)
                    gm.OnCarCrashLethal(sev01ForDamage);
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



            bool tryingToMove = Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.S);

            if (speed < minSpeedToSteer && !(allowSteerWhenTryingToMove && tryingToMove))
            {
                // If you’re using the ice steer “charge”, force it to bleed off so it doesn’t feel sticky.
                _iceSteerCharge01 = Mathf.MoveTowards(_iceSteerCharge01, 0f, iceSteerRampDownRate * Time.deltaTime);

                // No turning-in-place.
                return;
            }

            float steerAmount = steeringInput * steerDirection * steerSpeed * speedSteerMul * driftSteerMul * iceSteerMul * Time.deltaTime;


            transform.Rotate(0f, steerAmount, 0f, Space.Self);

            if (isDrifting && speed > 0.1f)
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

        if (isOutOfFuel || isOutOfHP)
            return;

        Vector3 forward = transform.forward;
        bool forwardKey = Input.GetKey(KeyCode.W);
        bool reverseKey = Input.GetKey(KeyCode.S);

        if (_inputsSuppressedThisFrame || _suppressThrottleBrakeThisFrame)
        {
            forwardKey = false;
            reverseKey = false;
        }

        float speed = rb.velocity.magnitude;
        float forwardSpeed = Vector3.Dot(rb.velocity, forward);

        // Grounded check early so we can use it anywhere in this method.
        bool groundedNow = CheckIfGrounded();

        // Treat glide the same as drift for physics retention.
        bool driftPhysicsActive = isDrifting || _driftGlideActive;

        if (!isOutOfFuel && maxFuel > 0f)
        {
            bool accelerating = forwardKey;
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
                    rb.AddForce(forward * effectiveAcceleration, ForceMode.Acceleration);
                    ConsumeFuel(fuelUsePerSecondAtFullThrottle * Time.fixedDeltaTime);
                }
                else if (brakingOrReverse)
                {
                    float dt = Time.fixedDeltaTime;
                    float currentLong = Vector3.Dot(rb.velocity, forward);

                    if (currentLong > 0f)
                    {
                        float decel = maxBrakeDecelPerSecond > 0f ? maxBrakeDecelPerSecond : 1.0f;
                        float newLong = Mathf.MoveTowards(currentLong, 0f, decel * dt);
                        SetLongitudinalVelocityClamped(forward, newLong);
                    }
                    else
                    {
                        float reverseAccel = maxReverseAccelPerSecond > 0f ? maxReverseAccelPerSecond : 1.0f;
                        float targetReverseSpeed = -Mathf.Max(1f, effectiveMaxSpeed * 0.4f);
                        float newLong = Mathf.MoveTowards(currentLong, targetReverseSpeed, reverseAccel * dt);
                        SetLongitudinalVelocityClamped(forward, newLong);
                    }

                    ConsumeFuel(fuelUsePerSecondBraking * Time.fixedDeltaTime);
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
                            SetLongitudinalVelocityClamped(forward, newLong);
                        }
                        else if (speed > 0.01f)
                        {
                            float decel = coastLowDecelPerSecond;
                            float newMag = Mathf.Max(0f, speed - decel * Time.fixedDeltaTime);
                            rb.velocity = rb.velocity.normalized * newMag;
                        }

                        // Steer rolling traction while coasting
                        if (_steerTractionBlend > 0.0001f && enableSteerTraction && !driftButtonHeld && Mathf.Abs(steeringInput) > 0.001f)
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
                if (accelerating && !brakingOrReverse)
                {
                    float accelMul = (useFullAccelWhileDrifting ? 1f : driftForwardAccelMultiplier);
                    rb.AddForce(forward * effectiveAcceleration * accelMul, ForceMode.Acceleration);
                    ConsumeFuel(fuelUsePerSecondAtFullThrottle * Time.fixedDeltaTime);
                }

                if (brakingOrReverse && isDrifting)
                {
                    ConsumeFuel(fuelUsePerSecondBraking * Time.fixedDeltaTime);
                }
            }

            if (!accelerating && !brakingOrReverse && !driftPhysicsActive && nearIdleSpeed)
            {
                ConsumeFuel(idleFuelUsePerSecond * Time.fixedDeltaTime);
            }
        }
        else if (isOutOfFuel)
        {
            // OUT OF FUEL: rapid deceleration
            float stopDecel = 4.5f;
            float currentSpeed = rb.velocity.magnitude;
            if (currentSpeed > 0.01f)
            {
                float newMag = Mathf.Max(0f, currentSpeed - stopDecel * Time.fixedDeltaTime);
                rb.velocity = rb.velocity.normalized * newMag;
            }
        }

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

                bool gentleBrakeWhileDrifting = (reverseKey && !forwardKey);
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
                bool brakingDrift = (reverseKey && !forwardKey);

                if (!brakingDrift && lockToDriftPeakSpeed && driftButtonHeld)
                {
                    targetMagnitude = Mathf.Max(driftPeakSpeed, currentMag, driftClampSpeed);
                }
                else
                {
                    targetMagnitude = Mathf.Min(currentMag, Mathf.Max(driftClampSpeed, 0f));
                }

                float cap = GetCurrentSpeedCap();
                targetMagnitude = Mathf.Min(targetMagnitude, cap);

                if (forwardKey && !reverseKey)
                    targetMagnitude = Mathf.Max(targetMagnitude, currentMag);

                float smoothRate = 15f;
                float smoothedMag = Mathf.Lerp(currentMag, targetMagnitude, smoothRate * Time.fixedDeltaTime);

                float y = rb.velocity.y;
                Vector3 horiz = finalDir.normalized * Mathf.Max(0f, smoothedMag);
                rb.velocity = new Vector3(horiz.x, y, horiz.z);

                if (isDrifting && Mathf.Abs(steeringInput) > 0.001f && currentMag > 0.1f)
                {
                    float sign = Mathf.Sign(steeringInput);
                    Vector3 sideDir = Vector3.Cross(Vector3.up, transform.forward) * sign;
                    float sideMul = Mathf.Lerp(0.5f, 1f, driftCharge);
                    float sideForceScale = Time.time < _driftFlipBlockUntil ? 0.4f : 1f;
                    rb.AddForce(sideDir * driftSideForce * sideMul * sideForceScale, ForceMode.Acceleration);
                }
            }
        }
        else
        {
            float cap = GetCurrentSpeedCap();

            Vector3 v = rb.velocity;
            Vector3 horiz = Vector3.ProjectOnPlane(v, Vector3.up);
            float horizSpeed = horiz.magnitude;

            if (horizSpeed > cap + 0.5f && horizSpeed > 0.0001f)
            {
                Vector3 horizClamped = horiz * (cap / horizSpeed);
                rb.velocity = new Vector3(horizClamped.x, v.y, horizClamped.z);
            }
        }
    }


    private void ConsumeFuel(float amount)
    {
        if (isOutOfFuel || maxFuel <= 0f) return;

        amount *= Mathf.Max(0f, currentFuelUseMultiplier);

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
            isOutOfFuel = true;
            Debug.Log("[CarController] Fuel depleted.");
        }
        else
        {
            currentFuel = Mathf.Min(currentFuel, maxFuel);
        }
    }

    private float surfaceTurnMultiplier = 1f; // runtime surface multiplier for steering (fixes lost multiplier when skills apply)

    private void SampleGroundAndUpdateMultipliers()
    {
        if (carCollider == null) return;

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

        // NEW: Ice tracking
        int iceSamples = 0;
        float sumIceDynamicFriction = 0f;
        float sumIceStaticFriction = 0f;
        float sumIceHandling = 0f;

        if (boxCollider != null)
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
                        ref iceSamples, ref sumIceDynamicFriction, ref sumIceStaticFriction, ref sumIceHandling);
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
                        ref iceSamples, ref sumIceDynamicFriction, ref sumIceStaticFriction, ref sumIceHandling);
                }
            }
        }

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
                _lastStableGroundNormal = targetUp;
            }
            else
            {
                targetUp = _lastStableGroundNormal;
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
        ref float sumIceHandling)
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
        _boostRequested = false;
        ClearBoostOverride();

        _isBoosting = false;
        _boostTimer = 0f;

        _isPostBoost = false;
        _postBoostTimer = 0f;

        _activeBoostIsDrift = false;
        _activeBoostMaxMult = 1f;

        // Optional: wipe cooldown timers so you don't “come out of crash already cooling down”
        _boostCooldownTimer = 0f;
        _driftBoostCooldownTimer = 0f;

        // Lock out all boosts for a bit (covers post-crash drift-release + space presses)
        _boostBlockedUntil = Mathf.Max(_boostBlockedUntil, Time.time + Mathf.Max(0f, lockoutSeconds));
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

    public void ApplyExternalCrashDamage(
        Vector3 hitDirection,
        float impactSpeed,
        Vector3 contactPointWS,
        float severity)
    {
        if (rb == null)
            return;

        if (IsCrashInvulnerable)
            return;

        // Respect internal cooldown so external callers can't bypass invulnerability windows.
        bool damageWindowOpen = Time.time >= _nextCrashAllowedTime;
        float sev01 = Mathf.Clamp01(severity);

        // Clamp impact speed into the same range used elsewhere
        impactSpeed = Mathf.Clamp(impactSpeed, minImpactSpeed, maxImpactSpeed);

        // Camera shake / slow-mo / coin penalties, etc.
        var gm = GameManager_Racing.Instance;
        if (gm != null && damageWindowOpen)
        {
            gm.OnCarCrash(impactSpeed, sev01);
        }

        // 🔊 NEW: play default crash SFX + impact VFX for external hits too
        if (damageWindowOpen)
        {
            // Use default crash clip for projectiles / generic hits
            PlayCrashSfx(crashClipDefault, contactPointWS, crashSfxVolume);

            // Approximate a surface normal from the incoming hit direction
            Vector3 normal = hitDirection.sqrMagnitude > 0.0001f
                ? -hitDirection.normalized        // "surface" pushing back against hitDir
                : Vector3.up;

            SpawnCrashImpactVFX(contactPointWS, normal);
        }

        // Duration and impulse magnitudes consistent with normal collisions
        float crashDuration = Mathf.Lerp(minCrashDuration, maxCrashDuration, sev01);
        float impulseMag = impactSpeed * impulsePerUnitSpeed;
        float torqueMag = impactSpeed * torquePerUnitSpeed;

        // Let the central crash handler do physics + HP/Fuel + cooldown, etc.
        TriggerCrash(hitDirection, crashDuration, impulseMag, torqueMag, sev01, contactPointWS, damageWindowOpen);
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

    private void ApplyDamageDegradationToPerformance()
    {
        // NOTE: keep degradation strictly to performance (accel/maxSpeed/turn).
        // Do NOT alter drag, physics materials or anything that can freeze motion.
        float hpFrac = HPPercent;
        if (hpFrac >= degradeStartHPFraction)
            return;

        float t = Mathf.Clamp01((degradeStartHPFraction - hpFrac) / Mathf.Max(0.0001f, degradeStartHPFraction));
        float perfMul = Mathf.Lerp(1f, Mathf.Clamp(performanceAtZeroHP, 0.1f, 1f), t);

        effectiveAcceleration *= perfMul;
        effectiveMaxSpeed *= perfMul;
        effectiveTurnSpeed *= perfMul;

        // IMPORTANT: we do NOT touch effectiveDrag here to avoid stalling the car.
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

    private void OnCollisionEnter(Collision collision)
    {
        if (rb == null)
            return;

        // NEW: if we're already crashing or reorienting, ignore new crash logic entirely.
        if (IsCrashInvulnerable)
            return;

        // --- NEW: skip crash logic if obstacle has active forcefield immunity ---
        var immunity = collision.collider.GetComponentInParent<LaunchImmunityMarker>();
        if (immunity != null && immunity.IsImmune) return;

        if (((1 << collision.gameObject.layer) & crashLayers) == 0)
            return;

        float impactSpeed = collision.relativeVelocity.magnitude;



        if (impactSpeed < minImpactSpeed)
            return;

        float severity = Mathf.InverseLerp(minImpactSpeed, maxImpactSpeed, impactSpeed);

        Debug.Log($"Impact Speed: {impactSpeed}");

        bool damageWindowOpen = Time.time >= _nextCrashAllowedTime; // NEW

        var gm = GameManager_Racing.Instance;
        if (gm != null && damageWindowOpen)
            gm.OnCarCrash(impactSpeed, severity); // skip currency penalties if still in cooldown

        float crashDuration = Mathf.Lerp(minCrashDuration, maxCrashDuration, severity);
        float impulseMag = impactSpeed * impulsePerUnitSpeed;
        float torqueMag = impactSpeed * torquePerUnitSpeed;

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

        // NOW shuttle calc (uses correct normal)
        var shuttle = collision.collider.GetComponentInParent<ShuttleTrackObstacle>();
        if (shuttle != null && rb != null)
        {
            Vector3 rel = rb.velocity - shuttle.GetWorldVelocity();
            impactSpeed = Mathf.Abs(Vector3.Dot(rel, contactNormal));
        }



        var otherCol = collision.collider;
        var cross = otherCol.GetComponentInParent<CrossTrackObstacle>();
        if (cross != null)
        {
            PlayCrashSfx(crashClipHonk, contactPoint, crashSfxVolume);
        }
        else
        {
            PlayCrashSfx(crashClipDefault, contactPoint, crashSfxVolume);
        }

        int rootId = collision.collider.transform.root.GetInstanceID();
        _recentCrashRootTime[rootId] = Time.time;
        _closeCallTracking.Remove(rootId);


        // Spawn crash/explode VFX at the contact point (only when damage window open)
        if (damageWindowOpen)
        {
            SpawnCrashImpactVFX(contactPoint, contactNormal);
        }

        TriggerCrash(hitDir, crashDuration, impulseMag, torqueMag, severity, contactPoint, damageWindowOpen);
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

        // (rest of existing crash logic unchanged)
        float impactSpeed = 0f;
        Rigidbody otherRb = other.attachedRigidbody;
        if (otherRb != null)
            impactSpeed = (rb.velocity - otherRb.velocity).magnitude;
        else
            impactSpeed = rb.velocity.magnitude;

        if (impactSpeed < minImpactSpeed)
            return;

        float severity = Mathf.InverseLerp(minImpactSpeed, maxImpactSpeed, impactSpeed);
        bool damageWindowOpen = Time.time >= _nextCrashAllowedTime;

        var gm = GameManager_Racing.Instance;
        if (gm != null && damageWindowOpen)
            gm.OnCarCrash(impactSpeed, severity);

        float crashDuration = Mathf.Lerp(minCrashDuration, maxCrashDuration, severity);
        float impulseMag = impactSpeed * impulsePerUnitSpeed;
        float torqueMag = impactSpeed * torquePerUnitSpeed;

        Vector3 hitDir = transform.position - other.bounds.center;
        hitDir.y = 0f;
        hitDir.Normalize();

        Vector3 contactPoint = other.bounds.ClosestPoint(transform.position);

        var crossTrigger = other.GetComponentInParent<CrossTrackObstacle>();
        if (crossTrigger != null)
            PlayCrashSfx(crashClipHonk, contactPoint, crashSfxVolume);
        else
            PlayCrashSfx(crashClipDefault, contactPoint, crashSfxVolume);

        int rootId = other.transform.root.GetInstanceID();
        _recentCrashRootTime[rootId] = Time.time;
        _closeCallTracking.Remove(rootId);

        // Spawn crash/explode VFX at the contact point (only when damage window open)
        if (damageWindowOpen)
        {
            // For triggers we don't have a contact normal - use up as a reasonable default
            SpawnCrashImpactVFX(contactPoint, Vector3.up);
        }

        TriggerCrash(hitDir, crashDuration, impulseMag, torqueMag, severity, contactPoint, damageWindowOpen);
    }

    private void UpdateCrashReorientation()
    {
        if (!_isReorienting)
            return;

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
                rb.freezeRotation = true;
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
        // Steering should feel consistent with physics, not frames.
        float dt = Time.fixedDeltaTime;

        float targetSteer = _suppressSteeringThisFrame ? 0f : _rawSteer;

        float smoothRate = steeringInputSmooth;
        if (isDrifting) smoothRate *= 1.4f;

        float handlingMult = GetTemporaryHandlingMultiplier();
        if (handlingMult > 1f) smoothRate /= handlingMult;

        float smoothDelta = smoothRate * dt;

        if (Mathf.Abs(targetSteer - steeringInput) < 0.01f)
            steeringInput = targetSteer;
        else
            steeringInput = Mathf.MoveTowards(steeringInput, targetSteer, smoothDelta);
    }
    private void PlayDeathVFX()
    {
        if (_deathVfxPlayed) return;
        if (deathVFX == null) return;

        _deathVfxPlayed = true;

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
        if (currentFuel > 0f) isOutOfFuel = false; // allow driving again if we refueled
        return Mathf.Max(0f, currentFuel - before);
    }

    public float AddHP(float amount)
    {
        if (maxHP <= 0f || amount <= 0f) return 0f;
        float before = currentHP;
        currentHP = Mathf.Min(maxHP, currentHP + amount);
        return Mathf.Max(0f, currentHP - before);
    }

    private void UpdateDamageVFXImmediate()
    {
        if (!damageSmokeVFX) return;

        float hpFrac = HPPercent;
        var emission = damageSmokeVFX.emission;
        var main = damageSmokeVFX.main;

        if (hpFrac <= smokeStartHPFraction)
        {
            // Damage progress 0..1 from threshold → zero HP
            float tDamage = Mathf.Clamp01((smokeStartHPFraction - hpFrac) / Mathf.Max(0.0001f, smokeStartHPFraction));

            emission.enabled = true;
            emission.rateOverTime = Mathf.Lerp(smokeMinRate, smokeMaxRate, tDamage);

            float size = Mathf.Lerp(smokeMinSize, smokeMaxSize, tDamage);
            main.startSize = new ParticleSystem.MinMaxCurve(size);

            // Color interpolation – invert if requested (handles ambiguous request)
            float colorT = invertSmokeColorLerp ? (1f - tDamage) : tDamage;
            Color currentColor = Color.Lerp(smokeColorAtThreshold, smokeColorAtZeroHP, colorT);
            main.startColor = new ParticleSystem.MinMaxGradient(currentColor);
        }
        else
        {
            emission.enabled = false;
        }
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
    public float CurrentSpeed => rb != null ? rb.velocity.magnitude : 0f;
    public float EffectiveMaxSpeed => effectiveMaxSpeed;
    public float CurrentFuel => currentFuel;
    public bool IsOutOfFuel => isOutOfFuel;
    public bool IsOutOfHP => isOutOfHP;
    public float FuelPercent => maxFuel > 0f ? currentFuel / maxFuel : 0f;
    public float OffDefaultFraction => offDefaultFraction;
    public float GrassFraction => grassFraction;

    public float CurrentHP => currentHP;
    public float MaxHP => maxHP;
    public float HPPercent => maxHP > 0f ? currentHP / maxHP : 0f;
}