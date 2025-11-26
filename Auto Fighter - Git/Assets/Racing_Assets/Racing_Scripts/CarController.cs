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
    [Tooltip("Require a non‑zero steering input (above steerFlipThreshold) to build/maintain drift charge. Releasing steering while holding drift will drain the charge.")]
    [SerializeField] private bool requireDirectionalInputForDriftCharge = true;
    [Tooltip("Drain rate while drift key held but no steering (if requireDirectionalInputForDriftCharge = true). If <= 0 uses driftReleaseRate.")]
    [SerializeField] private float driftNeutralDrainRate = 4.2f;

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
    [Tooltip("Allow holding the drift key without steering to preserve most of entry speed (ice‑like glide).")]
    [SerializeField] private bool allowDriftGlideWithoutSteer = true;
    [Tooltip("Per‑second decay while gliding (very small to keep speed).")]
    [SerializeField] private float driftGlideDecayPerSecond = 0.05f;

    private bool _driftGlideActive;          // NEW: glide mode (holding drift, no steer)

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
    [SerializeField, Tooltip("Fuel cost applied when drift boost triggers.")]
    private float driftBoostFuelCost = 0f;

    // Runtime boost state
    private float _boostCooldownTimer;
    private bool _boostRequested;
    private bool _isBoosting;
    private float _boostTimer;
    private bool _isPostBoost;
    private float _postBoostTimer;

    // Drift-held boost runtime (per-direction)
    private float _driftHoldTimeSeconds;        // accumulates while drifting with stable direction
    private int _driftHoldDirectionSign;        // +1/-1/0 current tracked direction
    private bool _driftWasActiveLastFrame;

    // Overrides per boost activation (allows custom parameters from drift-held boost)
    private bool _boostOverrideActive;
    private float _boostOverrideForce;
    private float _boostOverrideDuration;
    private float _boostOverrideMaxMult;

    private Quaternion _initialRotation;
    private bool _isReorienting;
    private float _reorientElapsed;
    private Quaternion _reorientStartRot;
    private Quaternion _reorientTargetRot;

    private bool _inCrash;
    private float _crashTimer;
    private float _baseDrag;
    private float _baseAngularDrag;

    private float baseMaxFuel;
    private float baseIdleFuelUse;
    private float baseFuelUseFullThrottle;
    private float baseFuelUseBraking;
    private float baseTurnSpeed;

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
        currentFuelUseMultiplier = 1f;

        baseMaxFuel = maxFuel;
        baseIdleFuelUse = idleFuelUsePerSecond;
        baseFuelUseFullThrottle = fuelUsePerSecondAtFullThrottle;
        baseFuelUseBraking = fuelUsePerSecondBraking;

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
    }

    private void HandleBoost()
    {
        if (_boostCooldownTimer > 0f)
            _boostCooldownTimer -= Time.fixedDeltaTime;

        if (_isBoosting)
        {
            _boostTimer -= Time.fixedDeltaTime;

            float sustainAccel = _boostOverrideActive ? driftBoostSustainAcceleration : boostSustainAcceleration;
            if (sustainAccel > 0f)
                rb.AddForce(transform.forward * sustainAccel, ForceMode.Acceleration);

            if (_boostTimer <= 0f)
            {
                _isBoosting = false;
                _isPostBoost = postBoostSlowdownDuration > 0f;
                _postBoostTimer = postBoostSlowdownDuration;

                // Clear any override once boost ends
                _boostOverrideActive = false;
                _boostOverrideForce = 0f;
                _boostOverrideDuration = 0f;
                _boostOverrideMaxMult = 0f;
            }
        }
        else if (_isPostBoost)
        {
            _postBoostTimer -= Time.fixedDeltaTime;
            if (_postBoostTimer <= 0f)
                _isPostBoost = false;
        }

        if (_boostRequested)
        {
            if (_boostOverrideActive) _boostCooldownTimer = 0f;
            _boostRequested = false;
            if (_boostCooldownTimer <= 0f)
            {
                // Fuel gate
                float cost = boostFuelCost;
                if (_boostOverrideActive) cost = driftBoostFuelCost;

                if (cost > 0f)
                {
                    if (isOutOfFuel || currentFuel < cost)
                        return;
                }

                // Apply the impulse (override if drift-held boost requested)
                float impulseForce = _boostOverrideActive ? _boostOverrideForce : boostForce;
                rb.AddForce(transform.forward * impulseForce, ForceMode.Acceleration);
                Debug.Log($"Boost activated! Force={impulseForce}");
                if (cost > 0f) ConsumeFuel(cost);

                _isBoosting = true;
                _boostTimer = Mathf.Max(0f, _boostOverrideActive ? _boostOverrideDuration : boostDuration);
                _isPostBoost = false;
                _boostCooldownTimer = boostCooldown;
            }
        }

        float cap = GetCurrentSpeedCap();
        float speed = rb.velocity.magnitude;
        if (speed > cap)
            rb.velocity = rb.velocity.normalized * cap;
    }

    private float GetCurrentSpeedCap()
    {
        float normalCap = effectiveMaxSpeed;
        float maxMult = _isBoosting
            ? (_boostOverrideActive ? Mathf.Max(1f, _boostOverrideMaxMult) : Mathf.Max(1f, boostMaxSpeedMultiplier))
            : 1f;

        float boostedCap = normalCap * maxMult;

        if (_isPostBoost && postBoostSlowdownDuration > 0f)
        {
            float t = 1f - Mathf.Clamp01(_postBoostTimer / postBoostSlowdownDuration);
            return Mathf.Lerp(boostedCap, normalCap, t);
        }
        return _isBoosting ? boostedCap : normalCap;
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
    }

    private void HandleSkillsReset()
    {
        accelValue = maxSpeedValue = steerValue = fuelValue = 0f;
        accelMode = maxSpeedMode = steerMode = fuelMode = SkillApplicationMode.Additive;
        RefreshSkillEffects();
        ApplySkillEffects();
        UpdateDriftUnlock();
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

            if (driftButtonHeld)
            {
                if (currentSign != 0)
                {
                    if (resetDriftChargeOnSteerFlip &&
                        _driftCurrentSteerSign != 0 &&
                        currentSign != _driftCurrentSteerSign &&
                        driftCharge >= minChargeForFlipReset)
                    {
                        // Direction flip: retain some charge but reset drift-held boost accumulation
                        driftCharge = Mathf.Clamp01(steerFlipRetainedCharge);

                        // Reset drift-held timer on flip
                        ResetDriftHeldTimer();

                        if (rb != null)
                        {
                            float currentSpeed = rb.velocity.magnitude;
                            if (driftEntrySpeed <= 0.01f)
                                driftEntrySpeed = currentSpeed;

                            driftClampSpeed = Mathf.Max(driftClampSpeed, currentSpeed);
                            driftPeakSpeed = Mathf.Max(driftPeakSpeed, currentSpeed);
                        }

                        _driftFlipBlockUntil = Time.time + steerFlipRebuildDelay;
                    }
                    _driftCurrentSteerSign = currentSign;
                }
            }
            else
            {
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
                // Drift ended – evaluate boost
                if (enableDriftHeldBoost)
                    TryTriggerDriftHeldBoost();

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
                    if (Random.value < p)
                    {
                        _malfunctionTimer = Random.Range(malfunctionBurstDuration.x, malfunctionBurstDuration.y);
                        _malfunctionCooldownRemain = Random.Range(malfunctionCooldown.x, malfunctionCooldown.y);
                    }
                }
            }
            suppressInputs = _malfunctionTimer > 0f;
        }

        steeringInput = Mathf.MoveTowards(
            steeringInput,
            suppressInputs ? 0f : rawHorizontal,
            smoothRate * Time.deltaTime
        );

        _inputsSuppressedThisFrame = suppressInputs;
    }

    // Evaluate and trigger a drift-held boost if thresholds are met
    private void TryTriggerDriftHeldBoost()
    {
        if (!enableDriftHeldBoost) return;
        if (_inCrash) return; // prevent accidental boost trigger after a crash interruption

        float held = _driftHoldTimeSeconds;
        ResetDriftHeldTimer();

        if (held < driftBoostMinHoldSeconds)
            return; // below minimum threshold

        float clamped = Mathf.Min(held, driftBoostMaxHoldSeconds);
        float norm = Mathf.InverseLerp(driftBoostMinHoldSeconds, driftBoostMaxHoldSeconds, clamped);

        float force = Mathf.Lerp(driftBoostForceRange.x, driftBoostForceRange.y, norm);
        float duration = Mathf.Lerp(driftBoostDurationRange.x, driftBoostDurationRange.y, norm);
        float maxMult = Mathf.Lerp(driftBoostMaxSpeedMultRange.x, driftBoostMaxSpeedMultRange.y, norm);

        _boostOverrideActive = true;
        _boostOverrideForce = force;
        _boostOverrideDuration = duration;
        _boostOverrideMaxMult = maxMult;

        if (driftBoostFuelCost > 0f)
        {
            if (!isOutOfFuel && currentFuel >= driftBoostFuelCost)
                ConsumeFuel(driftBoostFuelCost);
            else
            {
                _boostOverrideActive = false;
                return;
            }
        }

        _boostRequested = true;
    }

    private void ResetDriftHeldTimer()
    {
        _driftHoldTimeSeconds = 0f;
        _driftHoldDirectionSign = 0;
    }

    private void TriggerCrash(Vector3 hitDirection, float crashDuration, float impulseMagnitude, float torqueMagnitude, float severity, Vector3 contactPointWS, bool applyDamage)
    {
        if (rb == null)
            return;

        hitDirection.y = 0f;
        if (hitDirection.sqrMagnitude < 0.0001f)
            hitDirection = -transform.forward;
        hitDirection.Normalize();

        _inCrash = true;
        _crashTimer = crashDuration;

        rb.freezeRotation = false;
        rb.drag = _baseDrag * crashDragMultiplier;
        rb.angularDrag = crashAngularDrag;

        Vector3 v = rb.velocity;
        Vector3 flatVel = new Vector3(v.x, 0f, v.z);

        if (flatVel.sqrMagnitude > 0.01f)
        {
            Vector3 normal = hitDirection.normalized;
            Vector3 reflected = Vector3.Reflect(flatVel, normal);

            float deflectAmount = Mathf.Lerp(0.3f, 0.8f, Mathf.Clamp01(severity));
            Vector3 newFlatVel = Vector3.Lerp(flatVel, reflected, deflectAmount);

            float slowMul = Mathf.Lerp(0.9f, 0.6f, Mathf.Clamp01(severity));
            newFlatVel *= slowMul;

            rb.velocity = new Vector3(newFlatVel.x, v.y, newFlatVel.z);
        }
        else
        {
            rb.velocity = hitDirection * impulseMagnitude * 0.5f;
        }

        rb.AddForce(hitDirection * impulseMagnitude, ForceMode.VelocityChange);

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

        float sev01 = Mathf.Clamp01(severity);

        if (applyDamage)
        {
            if (hpCrashDamageAtSeverity1 > 0f)
            {
                float hpLoss = Mathf.Max(minHpLossPerCrash, hpCrashDamageAtSeverity1 * sev01);
                hpLoss = Mathf.Min(hpLoss, currentHP);
                currentHP = Mathf.Max(0f, currentHP - hpLoss);
                Debug.Log($"[CarController] Crash damage applied: -{hpLoss} HP (sev={sev01:F2}). HP={currentHP}/{maxHP}");
            }
            if (fuelLossAtSeverity1 > 0f)
            {
                float requestedFuelLoss = Mathf.Max(minFuelLossPerCrash, fuelLossAtSeverity1 * sev01);
                float before = currentFuel;
                ConsumeFuel(requestedFuelLoss);
                float consumed = Mathf.Max(0f, before - currentFuel);
                if (consumed + 1e-3f < minFuelLossPerCrash)
                {
                    float shortfall = minFuelLossPerCrash - consumed;
                    currentFuel = Mathf.Max(0f, currentFuel - shortfall);
                }
                Debug.Log($"[CarController] Crash fuel loss applied (sev={sev01:F2}). Fuel={currentFuel}/{maxFuel}");
            }

            // Set cooldown timer AFTER applying damage
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
        float steerSpeed = Mathf.Max(0f, effectiveTurnSpeed);

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
                    float brakeAccel = currentBrakingForce * brakeForwardFactor;
                    float reverseAccel = effectiveAcceleration * reverseAccelFactor;

                    if (forwardSpeed > brakeToReverseSpeed)
                    {
                        rb.AddForce(-forward * brakeAccel, ForceMode.Acceleration);
                        ConsumeFuel(fuelUsePerSecondBraking * Time.fixedDeltaTime);
                    }
                    else
                    {
                        rb.AddForce(-forward * reverseAccel, ForceMode.Acceleration);
                        ConsumeFuel(fuelUsePerSecondAtFullThrottle * Time.fixedDeltaTime);
                    }
                }
                else
                {
                    // Arcade gradual slowdown (no artificial drag slam).
                    float currentSpeed = rb.velocity.magnitude;
                    if (currentSpeed > 0.01f)
                    {
                        float maxRef = Mathf.Max(1f, effectiveMaxSpeed);
                        float speedFrac = Mathf.Clamp01(currentSpeed / maxRef);
                        float highBlend = Mathf.Clamp01((speedFrac - coastHighSpeedFraction) / Mathf.Max(0.0001f, 1f - coastHighSpeedFraction));
                        float decelPerSecond = Mathf.Lerp(coastLowDecelPerSecond, coastHighDecelPerSecond, highBlend);

                        if (useExponentialCoast)
                        {
                            float k = Mathf.Max(0f, coastDampingPerSecond);
                            float damp = Mathf.Exp(-k * Time.fixedDeltaTime);
                            float targetMag = currentSpeed * damp;
                            targetMag = Mathf.Max(0f, targetMag - decelPerSecond * 0.15f * Time.fixedDeltaTime);
                            rb.velocity = rb.velocity.normalized * targetMag;
                        }
                        else
                        {
                            float newMag = Mathf.Max(0f, currentSpeed - decelPerSecond * Time.fixedDeltaTime);
                            rb.velocity = rb.velocity.normalized * newMag;
                        }
                    }

                    // NEW: Steering traction while coasting (unchanged)
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

                rb.velocity = finalDir.normalized * Mathf.Max(0f, targetMagnitude);

                if (isDrifting && brakingDrift && currentMag > 0.1f)
                {
                    float assist = currentBrakingForce * 0.15f; // gentle
                    rb.AddForce(-flatForward * assist, ForceMode.Acceleration);
                }

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

    private void ApplyDamageDegradationToPerformance()
    {
        float hpFrac = HPPercent;
        if (hpFrac >= degradeStartHPFraction)
            return;

        float t = Mathf.Clamp01((degradeStartHPFraction - hpFrac) / Mathf.Max(0.0001f, degradeStartHPFraction));
        float perfMul = Mathf.Lerp(1f, Mathf.Clamp(performanceAtZeroHP, 0.1f, 1f), t);

        effectiveAcceleration *= perfMul;
        effectiveMaxSpeed *= perfMul;
        effectiveTurnSpeed *= perfMul;
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
        if (collision.contactCount > 0)
        {
            var c = collision.GetContact(0);
            hitDir = c.normal;
            contactPoint = c.point;
        }
        else
        {
            hitDir = (transform.position - collision.transform.position).normalized;
        }

        TriggerCrash(hitDir, crashDuration, impulseMag, torqueMag, severity, contactPoint, damageWindowOpen);
    }

    private void OnTriggerEnter(Collider other)
    {
        if (((1 << other.gameObject.layer) & crashLayers) == 0)
            return;

        float impactSpeed = 0f;
        Rigidbody otherRb = other.attachedRigidbody;
        if (otherRb != null)
            impactSpeed = (rb.velocity - otherRb.velocity).magnitude;
        else
            impactSpeed = rb.velocity.magnitude;

        if (impactSpeed < minImpactSpeed)
            return;

        float severity = Mathf.InverseLerp(minImpactSpeed, maxImpactSpeed, impactSpeed);
        bool damageWindowOpen = Time.time >= _nextCrashAllowedTime; // NEW

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

        TriggerCrash(hitDir, crashDuration, impulseMag, torqueMag, severity, contactPoint, damageWindowOpen);
    }

    private void UpdateCrashReorientation()
    {
        if (!_isReorienting)
            return;

        _reorientElapsed += Time.deltaTime;
        float t = Mathf.Clamp01(_reorientElapsed / reorientDuration);
        transform.rotation = Quaternion.Slerp(_reorientStartRot, _reorientTargetRot, t);
        if (t >= 1f)
            _isReorienting = false;
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
    public float FuelPercent => maxFuel > 0f ? currentFuel / maxFuel : 0f;
    public float OffDefaultFraction => offDefaultFraction;
    public float GrassFraction => grassFraction;

    public float CurrentHP => currentHP;
    public float MaxHP => maxHP;
    public float HPPercent => maxHP > 0f ? currentHP / maxHP : 0f;
}