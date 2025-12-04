using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.PostProcessing;
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

    [Header("Steering Feel")]
    [SerializeField] private float lowSpeedSteerMultiplier = 1.2f;
    [SerializeField] private float highSpeedSteerMultiplier = 0.4f;
    [SerializeField] private float speedForSteerCurve = 25f;
    [SerializeField] private float steeringInputSmooth = 12f;

    [Header("Arcade Steering Extras")]
    [SerializeField] private bool useAutoAlignToVelocity = false;
    [SerializeField] private float autoAlignStrength = 3f;

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


    // Runtime boost state
    private float _boostCooldownTimer;
    private float _driftBoostCooldownTimer;          // NEW: separate drift boost cooldown
    private bool _boostRequested;
    private bool _isBoosting;
    private float _boostTimer;
    private bool _isPostBoost;
    private float _postBoostTimer;

    public event Action OnBoostStarted;
    public event Action OnBoostEnded;

    private float baseBoostForce;
    private float baseBoostSustainAcceleration;
    private float baseBoostDuration;
    private float baseBoostMaxSpeedMult;
    private float baseBoostCooldown;
    private float baseBoostFuelCost;
    private float baseDriftBoostCooldown;

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

        RefreshSkillEffects();
        ApplySkillEffects();

        UpdateDamageVFXImmediate();
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
        HandleSteering();

        if (Input.GetKeyDown(boostKey))
            _boostRequested = true;

        if (!_inCrash && hpRegenPerSecond > 0f && currentHP < maxHP)
        {
            currentHP = Mathf.Min(maxHP, currentHP + hpRegenPerSecond * Time.deltaTime);
        }

        if (currentHP <= 0f)
            isOutOfHP = true;

        UpdateDamageVFXImmediate();
    }

    private void FixedUpdate()
    {
        float dt = Time.fixedDeltaTime;

        if (_inCrash)
        {
            _crashTimer -= dt;
            if (_crashTimer <= 0f)
            {
                _inCrash = false;

                if (rb != null)
                {
                    rb.freezeRotation = true;
                    rb.drag = _baseDrag;
                    rb.angularDrag = _baseAngularDrag;
                    rb.angularVelocity = Vector3.zero;
                }

                _isReorienting = true;
                _reorientElapsed = 0f;
                _reorientStartRot = transform.rotation;

                Vector3 euler = transform.eulerAngles;
                _reorientTargetRot = Quaternion.Euler(0f, euler.y, 0f);
            }
            return;
        }

        SampleGroundAndUpdateMultipliers();
        RefreshSkillEffects();
        ApplySkillEffects();
        HandleMovement();
        HandleBoost();

        // NEW: periodic near-miss sweep to detect close calls against ANY obstacle layers (uses crashLayers)
        // Throttle frequency to _closeCallSweepInterval to avoid expensive queries every fixed frame.
        if (enableCloseCallNearMisses && Time.time - _lastCloseCallSweep >= _closeCallSweepInterval)
        {
            _lastCloseCallSweep = Time.time;
            CheckNearbyObstaclesForCloseCall();
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
            QueryTriggerInteraction.Ignore
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
                goto SpeedCapClamp;
            }

            // Cooldowns: separate for normal vs drift
            if (isDriftBoost)
            {
                if (_driftBoostCooldownTimer > 0f)
                {
                    Debug.Log($"[CarController] Drift boost ignored: cooldown {_driftBoostCooldownTimer:F2}s remaining.");
                    ClearBoostOverride();
                    goto SpeedCapClamp;
                }
            }
            else
            {
                if (_boostCooldownTimer > 0f)
                {
                    Debug.Log($"[CarController] Boost request ignored: cooldown {_boostCooldownTimer:F2}s remaining.");
                    ClearBoostOverride();
                    goto SpeedCapClamp;
                }
            }

            // Fuel cost: drift boost is FREE (no fuel usage)
            float cost = isDriftBoost ? 0f : boostFuelCost;
            if (cost > 0f && (isOutOfFuel || currentFuel < cost))
            {
                Debug.Log("[CarController] Boost request ignored: not enough fuel.");
                ClearBoostOverride();
                goto SpeedCapClamp;
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

    SpeedCapClamp:
        float cap = GetCurrentSpeedCap();
        float speed = rb.velocity.magnitude;
        if (speed > cap)
            rb.velocity = rb.velocity.normalized * cap;
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
        if (_malfunctionTimer > 0f)
            _malfunctionTimer -= Time.deltaTime;
        if (_malfunctionCooldownRemain > 0f)
            _malfunctionCooldownRemain -= Time.deltaTime;

        float rawHorizontal = Input.GetAxisRaw("Horizontal");
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
        steeringInput = Mathf.MoveTowards(
            steeringInput,
            _suppressSteeringThisFrame ? 0f : rawHorizontal,
            smoothRate * Time.deltaTime
        );

        _inputsSuppressedThisFrame = _suppressThrottleBrakeThisFrame;
    }

    private void TryTriggerDriftHeldBoost()
    {
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

        // Clamp severity once and reuse
        float sev01 = Mathf.Clamp01(severity);

        // Flatten & normalize incoming hit direction
        hitDirection.y = 0f;
        if (hitDirection.sqrMagnitude < 0.0001f)
            hitDirection = -transform.forward;
        hitDirection.Normalize();

        _inCrash = true;
        _crashTimer = crashDuration;

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
            float steerAmount = steeringInput * steerDirection * steerSpeed * speedSteerMul * driftSteerMul * Time.deltaTime;
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

        Vector3 forward = transform.forward;
        bool forwardKey = Input.GetKey(KeyCode.W);
        bool reverseKey = Input.GetKey(KeyCode.S);

        if (_inputsSuppressedThisFrame)
        {
            forwardKey = false;
            reverseKey = false;
        }

        if (_suppressThrottleBrakeThisFrame)
        {
            forwardKey = false;
            reverseKey = false;
        }

        float speed = rb.velocity.magnitude;
        float forwardSpeed = Vector3.Dot(rb.velocity, forward);

        // Treat glide the same as drift for physics retention.
        bool driftPhysicsActive = isDrifting || _driftGlideActive;

        if (!isOutOfFuel && maxFuel > 0f)
        {
            bool accelerating = forwardKey;
            bool brakingOrReverse = reverseKey;
            bool nearIdleSpeed = speed <= idleSpeedThreshold + 0.001f;

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

                    // 1) Moving forward: just ease toward 0 with a fixed decel rate.
                    if (currentLong > 0f)
                    {
                        // Use maxBrakeDecelPerSecond as the only brake strength knob.
                        float decel = maxBrakeDecelPerSecond > 0f
                            ? maxBrakeDecelPerSecond
                            : 1.0f; // fallback if not set

                        float newLong = Mathf.MoveTowards(currentLong, 0f, decel * dt);
                        SetLongitudinalVelocityClamped(forward, newLong);
                    }
                    // 2) Once we're basically stopped or moving backward, gently build up reverse.
                    else
                    {
                        float reverseAccel = maxReverseAccelPerSecond > 0f
                            ? maxReverseAccelPerSecond
                            : 1.0f; // fallback

                        float targetReverseSpeed = -Mathf.Max(1f, effectiveMaxSpeed * 0.4f);
                        float newLong = Mathf.MoveTowards(currentLong, targetReverseSpeed, reverseAccel * dt);
                        SetLongitudinalVelocityClamped(forward, newLong);
                    }

                    // Keep fuel usage simple
                    ConsumeFuel(fuelUsePerSecondBraking * Time.fixedDeltaTime);
                }
                else
                {
                    // --- PATCH 2: super gentle, constant coasting (no speed-based ramp) ---
                    float currentSpeed = rb.velocity.magnitude;
                    if (currentSpeed > 0.01f)
                    {
                        // Treat coastLowDecelPerSecond as a simple "coast decel" knob.
                        // For a max speed of ~4, something like 0.2–0.5 feels like a long, smooth roll.
                        float decel = coastLowDecelPerSecond;

                        float newMag = Mathf.Max(0f, currentSpeed - decel * Time.fixedDeltaTime);
                        rb.velocity = rb.velocity.normalized * newMag;
                    }

                    // Steering traction while coasting (unchanged)
                    if (enableSteerTraction && !driftButtonHeld && Mathf.Abs(steeringInput) > 0.001f)
                    {
                        Vector3 flatForward = new Vector3(transform.forward.x, 0f, transform.forward.z).normalized;
                        Vector3 vel = rb.velocity;
                        Vector3 flatVel = new Vector3(vel.x, 0f, vel.z);

                        if (flatVel.sqrMagnitude > (minSpeedForSteerTraction * minSpeedForSteerTraction))
                        {
                            Vector3 blendedDir = Vector3.Slerp(flatVel.normalized, flatForward, steerTractionReorientRate * Time.fixedDeltaTime).normalized;

                            Vector3 fwdComp = flatForward * Vector3.Dot(flatVel, flatForward);
                            Vector3 lateral = flatVel - fwdComp;
                            lateral *= Mathf.Exp(-lateralFrictionWhileSteering * Time.fixedDeltaTime);
                            float mag = (fwdComp + lateral).magnitude;

                            Vector3 newFlat = blendedDir * mag;
                            rb.velocity = new Vector3(newFlat.x, vel.y, newFlat.z);
                        }

                        rb.AddForce(flatForward * steerRollingAccel, ForceMode.Acceleration);
                    }
                }
            }
            else
            {
                // Acceleration while drifting / gliding
                if (accelerating && !brakingOrReverse)
                {
                    float accelMul = (useFullAccelWhileDrifting ? 1f : driftForwardAccelMultiplier);
                    rb.AddForce(forward * effectiveAcceleration * accelMul, ForceMode.Acceleration);
                    ConsumeFuel(fuelUsePerSecondAtFullThrottle * Time.fixedDeltaTime);
                }

                // NEW: gentle deceleration while drifting when holding S (no harsh brake force)
                if (brakingOrReverse && isDrifting)
                {
                    // Only fuel cost here; actual decel controlled by driftBrakeDecayPerSecond below
                    ConsumeFuel(fuelUsePerSecondBraking * Time.fixedDeltaTime);
                }
                // No passive coast drag while drifting/gliding.
            }

            if (!accelerating && !brakingOrReverse && !driftPhysicsActive && nearIdleSpeed)
            {
                ConsumeFuel(idleFuelUsePerSecond * Time.fixedDeltaTime);
            }
        }

        rb.drag = driftPhysicsActive ? effectiveDrag * 0.01f : effectiveDrag;

        speed = rb.velocity.magnitude;

        if (driftPhysicsActive)
        {
            if (driftEntrySpeed > 0.1f && speed > 0.01f)
            {
                if (driftClampSpeed <= 0f)
                    driftClampSpeed = driftEntrySpeed;

                if (driftButtonHeld)
                    driftPeakSpeed = Mathf.Max(driftPeakSpeed, rb.velocity.magnitude);

                Vector3 velDir = rb.velocity.sqrMagnitude > 0.0001f
                    ? rb.velocity.normalized
                    : transform.forward;

                bool gentleBrakeWhileDrifting = (reverseKey && !forwardKey);
                bool noThrottleNoBrake = (!forwardKey && !reverseKey);

                // Drift speed clamp evolution:
                // - S while drifting = controlled decay by driftBrakeDecayPerSecond
                // - No input = decay by driftSpeedDecayPerSecond or driftGlideDecayPerSecond
                if (gentleBrakeWhileDrifting)
                {
                    driftClampSpeed -= driftBrakeDecayPerSecond * Time.fixedDeltaTime;
                }
                else if (noThrottleNoBrake)
                {
                    float decayPerSecond = (_driftGlideActive && !isDrifting)
                        ? driftGlideDecayPerSecond
                        : driftSpeedDecayPerSecond;
                    driftClampSpeed -= decayPerSecond * Time.fixedDeltaTime;
                }

                if (driftClampSpeed < 0f) driftClampSpeed = 0f;

                float currentMag = rb.velocity.magnitude;

                Vector3 flatForward = new Vector3(transform.forward.x, 0f, transform.forward.z).normalized;
                Vector3 flatVel = new Vector3(velDir.x, 0f, velDir.z).normalized;

                float steerInfluence = Mathf.Clamp01(Mathf.Abs(steeringInput));
                const float driftAlignStrength = 2f;
                float blend = Mathf.Clamp01(steerInfluence * driftAlignStrength * Time.deltaTime);

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

                rb.velocity = finalDir.normalized * Mathf.Max(0f, targetMagnitude);

                // Note: the old extra brake AddForce while drifting+S is removed.
                // All drift braking now flows through driftClampSpeed & driftBrakeDecayPerSecond.

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
            if (speed > cap)
                rb.velocity = rb.velocity.normalized * cap;
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

                    EvaluateSurface(origin, rayDistance,
                        ref sumMaxSpeedMul, ref sumAccelMul, ref sumTurnMul,
                        ref sumDragMul, ref sumFuelMul,
                        ref samplesCounted, ref nonDefaultSamples, ref grassSamplesLocal);
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

                    EvaluateSurface(origin, rayDistance,
                        ref sumMaxSpeedMul, ref sumAccelMul, ref sumTurnMul,
                        ref sumDragMul, ref sumFuelMul,
                        ref samplesCounted, ref nonDefaultSamples, ref grassSamplesLocal);
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
        }
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

        if (Physics.Raycast(origin, Vector3.down, out RaycastHit hit, rayDistance, groundLayers, QueryTriggerInteraction.Ignore))
        {
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

    private void ApplySurfaceMultipliers(float maxSpeedMul, float accelMul, float turnMul, float dragMul)
    {
        currentMaxSpeed = baseMaxSpeed * maxSpeedMul;
        currentAcceleration = baseAcceleration * accelMul;
        currentBrakingForce = baseBrakingForce;
        currentTurnSpeed = baseTurnSpeed * turnMul;
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

            float newTurnSpeed = mgr.ApplyStatChain(
                baseTurnSpeed,
                SkillType.TurnSpeed_Add,
                SkillType.TurnSpeed_Mul
            );

            currentTurnSpeed = newTurnSpeed;
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
            float speed = rb.velocity.magnitude;
            float cap = GetCurrentSpeedCap();
            if (speed > cap)
                rb.velocity = rb.velocity.normalized * cap;
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
        // Preserve vertical component (y)
        Vector3 vel = rb.velocity;
        rb.velocity = forwardDir.normalized * newLong + Vector3.up * vel.y;
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
