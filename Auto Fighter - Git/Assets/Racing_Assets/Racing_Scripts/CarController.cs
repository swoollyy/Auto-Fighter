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

    // Runtime tracking of last raw steering (pre‑smoothed) to detect direction flips
    private float _lastRawSteerValue;
    private int _driftCurrentSteerSign = 0;          // last non-zero steering sign while drifting
    private float _driftFlipBlockUntil = 0f;         // time until rebuilding allowed after flip

    [Header("Base Physics")]
    [SerializeField] private float baseDrag = 0.08f;

    [Header("Ground Detection")]
    [SerializeField] private LayerMask groundLayers = ~0;
    [SerializeField] private int samplesX = 2;
    [SerializeField] private int samplesZ = 4;
    [SerializeField] private float raycastHeightOffset = 0.5f;
    [SerializeField] private float raycastExtraDistance = 2f;
    [SerializeField] private bool debugSurfaceRays = false;

    [Tooltip("How far ground samples stretch from the collider center.\n" +
         "0.5 = inner half, 1 = full collider extents, 1.5 = 50% beyond the collider, etc.")]
    [SerializeField] private float surfaceSampleExtent = 1f;

    [Header("Fuel Settings")]
    [SerializeField] private float maxFuel = 100f;
    [SerializeField] private float fuelUsePerSecondAtFullThrottle = 5f;
    [SerializeField] private float fuelUsePerSecondBraking = 3f;
    [SerializeField] private float idleFuelUsePerSecond = 0.5f;
    [Tooltip("Speed (m/s) below which we consider the car 'idle' for idle fuel consumption.")]
    [SerializeField] private float idleSpeedThreshold = 0.5f;

    [Header("Crash / Hit Reaction")]
    [SerializeField] private LayerMask crashLayers;          // Obstacles, walls, etc.

    [Tooltip("Impact speed below which we ignore the hit (m/s).")]
    [SerializeField] private float minImpactSpeed = 4f;

    [Tooltip("Impact speed where crash severity = 1 (m/s). Higher speeds are clamped.")]
    [SerializeField] private float maxImpactSpeed = 25f;

    [Tooltip("Shortest time you lose control on a light bump.")]
    [SerializeField] private float minCrashDuration = 0.15f;

    [Tooltip("Longest time you lose control on a huge crash.")]
    [SerializeField] private float maxCrashDuration = 1.1f;

    [Tooltip("How much linear shove per 1 m/s of impact speed.")]
    [SerializeField] private float impulsePerUnitSpeed = 0.6f;

    [Tooltip("How much spin per 1 m/s of impact speed.")]
    [SerializeField] private float torquePerUnitSpeed = 0.45f;

    [SerializeField] private float crashDragMultiplier = 2f;
    [SerializeField] private float crashAngularDrag = 1.5f;


    [Header("Crash Spin Tuning")]
    [Tooltip("Multiplier for yaw spin (around Y) on crash.")]
    [SerializeField] private float crashYawTorqueMultiplier = 1f;

    [Tooltip("Multiplier for roll spin (around Z / car forward) on crash.")]
    [SerializeField] private float crashRollTorqueMultiplier = 0.6f;

    [Header("Crash Recovery")]
    [SerializeField] private float reorientDuration = 0.6f;

    [Header("Steering Direction")]
    [Tooltip("If true, steering is inverted when the car is moving backwards (screen-style controls).")]
    [SerializeField] private bool invertSteeringWhenReversing = false;

    [Tooltip("How strong steering is while reversing, relative to forward.")]
    [SerializeField] private float reverseSteerMultiplier = 1f;

    private Quaternion _initialRotation;
    private bool _isReorienting;
    private float _reorientElapsed;
    private Quaternion _reorientStartRot;
    private Quaternion _reorientTargetRot;

    private bool _inCrash;
    private float _crashTimer;
    private float _baseDrag;
    private float _baseAngularDrag;

    // Backing "base" values
    private float baseMaxFuel;
    private float baseIdleFuelUse;
    private float baseFuelUseFullThrottle;
    private float baseFuelUseBraking;
    private float baseTurnSpeed;

    [Header("Fuel Modifiers by Surface")]
    [SerializeField] private float grassFuelUseMultiplier = 1.5f;

    // Surface-only adjusted values
    private float currentAcceleration;
    private float currentMaxSpeed;
    private float currentBrakingForce;
    private float currentTurnSpeed;
    private float currentDrag;

    // Effective values
    private float effectiveAcceleration;
    private float effectiveMaxSpeed;
    private float effectiveTurnSpeed;
    private float effectiveDrag;

    private Rigidbody rb;
    private Collider carCollider;
    private BoxCollider boxCollider;
    private float steeringInput;

    // Drift runtime
    private float driftCharge = 0f;
    private bool isDrifting = false;
    private float driftEntrySpeed = 0f;
    private float driftClampSpeed = 0f;

    // Fuel runtime
    private float currentFuel;
    private bool isOutOfFuel = false;
    private float currentFuelUseMultiplier = 1f;

    [Header("Debug (read-only)")]
    [SerializeField] private float offDefaultFraction = 0f;
    [SerializeField] private float grassFraction = 0f;

    // Skill cached effects
    private SkillApplicationMode accelMode;
    private float accelValue;
    private SkillApplicationMode maxSpeedMode;
    private float maxSpeedValue;
    private SkillApplicationMode steerMode;
    private float steerValue;
    private SkillApplicationMode fuelMode;
    private float fuelValue;

    [Header("Arcade Movement Tuning")]
    [SerializeField] private float coastDecelFactor = 0.1f;
    [SerializeField] private float brakeForwardFactor = 0.7f;
    [SerializeField] private float reverseAccelFactor = 0.8f;
    [SerializeField] private float brakeToReverseSpeed = 0.5f;

    [SerializeField] private float baseSteeringDamp = 1f;
    private float currentSteeringDamp;

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

        // Remember initial rotation so we can snap/lerp back to it after a crash
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

        driftUnlocked = !requireDriftUnlock; // default if not required

        RefreshSkillEffects();
        ApplySkillEffects();
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
                    // Restore base drag, keep rotation free
                    rb.drag = _baseDrag;
                    rb.angularDrag = _baseAngularDrag;

                    // Optional: lightly kill spin so reorientation feels clean
                    rb.angularVelocity = Vector3.zero;
                }

                // Start smooth reorientation: keep current yaw, flatten X/Z
                _isReorienting = true;
                _reorientElapsed = 0f;
                _reorientStartRot = transform.rotation;

                Vector3 euler = transform.eulerAngles;
                _reorientTargetRot = Quaternion.Euler(0f, euler.y, 0f);
            }

            // Don’t process normal input while crashing
            return;
        }

        SampleGroundAndUpdateMultipliers();
        RefreshSkillEffects();
        ApplySkillEffects();
        HandleMovement();
    }

    // ─────────────────────────────────────────────
    // Skill manager wiring
    // ─────────────────────────────────────────────
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

    // ─────────────────────────────────────────────
    // INPUT
    // ─────────────────────────────────────────────
    private void HandleInput()
    {
        float rawHorizontal = Input.GetAxisRaw("Horizontal");
        float speed = rb != null ? rb.velocity.magnitude : 0f;

        bool wasDrifting = isDrifting;

        if (!driftUnlocked)
        {
            driftCharge = 0f;
            isDrifting = false;
            _driftCurrentSteerSign = 0;
        }
        else
        {
            bool driftHeld = Input.GetKey(driftKey);
            bool canDriftThisFrame = driftHeld && speed >= driftMinSpeed;

            // Determine current steering sign (filtered by threshold)
            int currentSign =
                rawHorizontal > steerFlipThreshold ? 1 :
                rawHorizontal < -steerFlipThreshold ? -1 : 0;

            // Update persistent sign tracking (only while holding drift)
            if (driftHeld)
            {
                if (currentSign != 0)
                {
                    // Flip detection: previous non-zero sign differs from new sign
                    if (resetDriftChargeOnSteerFlip &&
                        _driftCurrentSteerSign != 0 &&
                        currentSign != _driftCurrentSteerSign &&
                        driftCharge >= minChargeForFlipReset)
                    {
                        // Flip occurred: reset/retain fraction
                        driftCharge = Mathf.Clamp01(steerFlipRetainedCharge);
                        driftEntrySpeed = 0f;
                        driftClampSpeed = 0f;
                        _driftFlipBlockUntil = Time.time + steerFlipRebuildDelay;
                    }

                    _driftCurrentSteerSign = currentSign;
                }
                // If steering neutral for a while you can optionally clear sign; choosing to keep it so a flip is still detected next time.
            }
            else
            {
                _driftCurrentSteerSign = 0;
            }

            // Block rebuild during delay window
            if (Time.time < _driftFlipBlockUntil)
            {
                // Treat as if drift not allowed to build yet
                canDriftThisFrame = false;
            }

                       if (requireDirectionalInputForDriftCharge)
                           {
                bool hasDirectionalSteer = currentSign != 0;
                               if (!hasDirectionalSteer)
                                   {
                                       // Holding drift without steering: drain charge toward 0
                                       if (driftCharge > 0f && driftHeld)
                                           {
                        float drain = (driftNeutralDrainRate > 0f ? driftNeutralDrainRate : driftReleaseRate);
                        driftCharge = Mathf.MoveTowards(driftCharge, 0f, drain * Time.deltaTime);
                                           }
                                       // Prevent new build while neutral
                    canDriftThisFrame = false;
                                   }
                              else
                                  {
                                       // Directional steer present -> normal build allowed if other conditions met
                    canDriftThisFrame &= true;
                                   }
                           }

            float targetDrift = (canDriftThisFrame ? 1f : 0f);

            float rate = targetDrift > driftCharge ? driftBuildRate : driftReleaseRate;
                       // If directional input required and currently neutral, we already handled manual drain above;
                       // skip applying releaseRate again to avoid double drain (only apply when targetDrift == 0 from other causes).
                       if (requireDirectionalInputForDriftCharge && targetDrift == 0f && (rawHorizontal > -steerFlipThreshold && rawHorizontal < steerFlipThreshold) && driftCharge > 0f)
                           {
                               // already drained; do nothing here
                           }
                       else
                           {
                driftCharge = Mathf.MoveTowards(driftCharge, targetDrift, rate * Time.deltaTime);
                           }

            isDrifting = driftCharge > 0.01f;

            if (isDrifting && !wasDrifting && rb != null)
            {
                driftEntrySpeed = speed;
                driftClampSpeed = driftEntrySpeed;
            }
            else if (!isDrifting && wasDrifting)
            {
                driftEntrySpeed = 0f;
                driftClampSpeed = 0f;
            }
        }

        _lastRawSteerValue = rawHorizontal;

        float smoothRate = steeringInputSmooth;
        if (isDrifting) smoothRate *= 1.4f;

        steeringInput = Mathf.MoveTowards(
            steeringInput,
            rawHorizontal,
            smoothRate * Time.deltaTime
        );
    }

    private void TriggerCrash(Vector3 hitDirection, float crashDuration, float impulseMagnitude, float torqueMagnitude)
    {
        if (rb == null)
            return;

        // Flatten hit direction to horizontal
        hitDirection.y = 0f;
        if (hitDirection.sqrMagnitude < 0.0001f)
            hitDirection = -transform.forward;   // fallback

        hitDirection.Normalize();

        _inCrash = true;
        _crashTimer = crashDuration;

        // Let physics own the car for a bit
        rb.freezeRotation = false;
        rb.drag = _baseDrag * crashDragMultiplier;
        rb.angularDrag = crashAngularDrag;

        // Dampen existing velocity so impact feels stronger
        Vector3 v = rb.velocity;
        Vector3 flatVel = new Vector3(v.x, 0f, v.z);

        // If we’re actually moving, deflect our direction
        if (flatVel.sqrMagnitude > 0.01f)
        {
            // Treat hitDirection as pointing from obstacle -> car,
            // so the "wall normal" we bounce off is roughly hitDirection
            Vector3 normal = hitDirection.normalized;

            // Reflect our flat velocity off that normal (like a pool ball)
            Vector3 reflected = Vector3.Reflect(flatVel, normal);

            // Blend between original and reflected to avoid crazy bounces
            float deflectAmount = 0.6f; // 0 = ignore, 1 = full reflect
            Vector3 newFlatVel = Vector3.Lerp(flatVel, reflected, deflectAmount);

            // Optional: add a bit of slowdown
            newFlatVel *= 0.8f;

            rb.velocity = new Vector3(newFlatVel.x, v.y, newFlatVel.z);
        }
        else
        {
            // If we were basically stopped, just shove us away
            rb.velocity = hitDirection * impulseMagnitude * 0.5f;
        }

        // Still add some extra shove so it feels punchy
        rb.AddForce(hitDirection * impulseMagnitude, ForceMode.VelocityChange);

        // Spin: decide direction based on which SIDE the obstacle is on.
        // hitDirection is from obstacle -> car (we used contact.normal),
        // so the direction from car -> obstacle is -hitDirection.
        Vector3 toObstacleWorld = -hitDirection;
        Vector3 toObstacleLocal = transform.InverseTransformDirection(toObstacleWorld);

        float sideSign = Mathf.Sign(toObstacleLocal.x); // +right, -left

        // Fallback if almost perfectly front/back
        if (Mathf.Abs(sideSign) < 0.001f)
            sideSign = Mathf.Sign(Vector3.Dot(toObstacleWorld, transform.right));

        //
        // Build separate yaw and roll components
        //

        // Yaw spin (around world Y) – same behavior as before but tunable
        Vector3 yawTorque = Vector3.up * torqueMagnitude * crashYawTorqueMultiplier * sideSign;

        // Roll spin (around car's forward axis → Z rotation in inspector)
        Vector3 rollAxis = transform.forward;
        Vector3 rollTorque = rollAxis * torqueMagnitude * crashRollTorqueMultiplier * sideSign;

        // Combine and apply
        rb.AddTorque(yawTorque + rollTorque, ForceMode.VelocityChange);
    }

    // ─────────────────────────────────────────────
    // STEERING
    // ─────────────────────────────────────────────
    private void HandleSteering()
    {
        if (rb == null) return;

        // While crashing or reorienting, let physics (and the reorient code) own rotation.
        if (_inCrash || _isReorienting)
            return;

        float speed = rb.velocity.magnitude;
        float forwardSpeed = Vector3.Dot(rb.velocity, transform.forward);
        float steerSpeed = Mathf.Max(0f, effectiveTurnSpeed);

        // Decide steering sign based on forward vs reverse
        float steerDirection = 1f;

        if (invertSteeringWhenReversing && forwardSpeed < -0.1f)
        {
            // Moving backwards and option enabled → invert steering
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
                rb.AddForce(sideDir * driftSideForce * sideMul, ForceMode.Acceleration);
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

    // ─────────────────────────────────────────────
    // MOVEMENT + FUEL
    // ─────────────────────────────────────────────
    private void HandleMovement()
    {
        if (rb == null) return;

        Vector3 forward = transform.forward;
        bool forwardKey = Input.GetKey(KeyCode.W);
        bool reverseKey = Input.GetKey(KeyCode.S);
        float speed = rb.velocity.magnitude;
        float forwardSpeed = Vector3.Dot(rb.velocity, forward);
        bool driftActive = isDrifting;

        if (!isOutOfFuel && maxFuel > 0f)
        {
            bool accelerating = forwardKey && !driftActive;
            bool brakingOrReverse = reverseKey;
            bool nearIdleSpeed = speed <= idleSpeedThreshold + 0.001f;

            // Driving logic
            if (!driftActive)
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
                        ConsumeFuel(fuelUsePerSecondBraking * Time.fixedDeltaTime); // braking cost
                    }
                    else
                    {
                        rb.AddForce(-forward * reverseAccel, ForceMode.Acceleration);
                        ConsumeFuel(fuelUsePerSecondAtFullThrottle * Time.fixedDeltaTime); // reverse throttle
                    }
                }
                else
                {
                    // Coasting (no fuel) unless truly idle
                    if (rb.velocity.sqrMagnitude > 0.001f)
                    {
                        Vector3 velDir = rb.velocity.normalized;
                        float coastAccel = currentBrakingForce * coastDecelFactor;
                        rb.AddForce(-velDir * coastAccel, ForceMode.Acceleration);
                    }
                }
            }
            else
            {
                // Drift mode: only reverse applies braking fuel use
                if (reverseKey)
                {
                    float brakeAccel = currentBrakingForce * brakeForwardFactor;
                    rb.AddForce(-forward * brakeAccel, ForceMode.Acceleration);
                    ConsumeFuel(fuelUsePerSecondBraking * Time.fixedDeltaTime);
                }
            }

            // Idle fuel: only when not accelerating/braking and speed is very low and not drifting
            if (!accelerating &&
                !brakingOrReverse &&
                !driftActive &&
                nearIdleSpeed)
            {
                ConsumeFuel(idleFuelUsePerSecond * Time.fixedDeltaTime);
            }
        }

        rb.drag = driftActive ? effectiveDrag * 0.01f : effectiveDrag;

        speed = rb.velocity.magnitude;

        if (driftActive)
        {
            if (driftEntrySpeed > 0.1f && speed > 0.01f)
            {
                if (driftClampSpeed <= 0f)
                    driftClampSpeed = driftEntrySpeed;

                Vector3 velDir =
                    rb.velocity.sqrMagnitude > 0.0001f
                        ? rb.velocity.normalized
                        : transform.forward;

                if (reverseKey)
                {
                    speed = rb.velocity.magnitude;
                    driftClampSpeed = Mathf.Min(driftClampSpeed, speed);
                }

                if (!forwardKey && !reverseKey)
                {
                    driftClampSpeed -= driftSpeedDecayPerSecond * Time.fixedDeltaTime;
                    if (driftClampSpeed < 0f) driftClampSpeed = 0f;
                }
                
                float targetSpeed = Mathf.Min(driftClampSpeed, effectiveMaxSpeed);

                Vector3 flatForward = new Vector3(transform.forward.x, 0f, transform.forward.z).normalized;
                Vector3 flatVel = new Vector3(velDir.x, 0f, velDir.z).normalized;
                float steerInfluence = Mathf.Clamp01(Mathf.Abs(steeringInput));
                const float driftAlignStrength = 2f;
                float blend = Mathf.Clamp01(steerInfluence * driftAlignStrength * Time.fixedDeltaTime);
                Vector3 finalDir = Vector3.Slerp(flatVel, flatForward, blend);
                if (finalDir.sqrMagnitude < 0.0001f)
                    finalDir = flatForward;

                rb.velocity = finalDir.normalized * targetSpeed;
            }
        }
        else
        {
            if (speed > effectiveMaxSpeed)
                rb.velocity = rb.velocity.normalized * effectiveMaxSpeed;
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

    // ─────────────────────────────────────────────
    // SURFACE SAMPLING
    // ─────────────────────────────────────────────
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

            // Scale how far we sample from the center on X/Z
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

            // Expand or shrink sampling area relative to the collider bounds
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

    // ─────────────────────────────────────────────
    // SKILL TREE COMPOSE
    // ─────────────────────────────────────────────
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

        // Start from the surface-modified values
        effectiveAcceleration = currentAcceleration;
        effectiveMaxSpeed = currentMaxSpeed;
        effectiveTurnSpeed = currentTurnSpeed;
        effectiveDrag = currentDrag;

        if (mgr != null)
        {
            // ─────────────────────────────────────────────
            // ACCEL & MAX SPEED – use the Add/Mul stat chain
            // ─────────────────────────────────────────────
            // These are the skills you're actually buying:
            //  - Acceleration_Add / Acceleration_Mul
            //  - MaxSpeed_Add / MaxSpeed_Mul
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

            // ─────────────────────────────────────────────
            // FUEL & FUEL EFFICIENCY (already chain-based)
            // ─────────────────────────────────────────────
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

            // ─────────────────────────────────────────────
            // TURN SPEED – already using Add/Mul chain
            // ─────────────────────────────────────────────
            float newTurnSpeed = mgr.ApplyStatChain(
                baseTurnSpeed,
                SkillType.TurnSpeed_Add,
                SkillType.TurnSpeed_Mul
            );

            currentTurnSpeed = newTurnSpeed;
            effectiveTurnSpeed = currentTurnSpeed;
        }

        // Clamp velocity to new effective max speed
        if (rb != null)
        {
            float speed = rb.velocity.magnitude;
            if (speed > effectiveMaxSpeed)
                rb.velocity = rb.velocity.normalized * effectiveMaxSpeed;
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
            $"driveFuel/s={fuelUsePerSecondAtFullThrottle:F3}"
        );
    }
#endif
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

        // Only react to layers we care about
        if (((1 << collision.gameObject.layer) & crashLayers) == 0)
            return;

        float impactSpeed = collision.relativeVelocity.magnitude;

        // Ignore gentle taps
        if (impactSpeed < minImpactSpeed)
            return;

        // Map impact speed → severity 0..1
        float severity = Mathf.InverseLerp(minImpactSpeed, maxImpactSpeed, impactSpeed);

        var gm = GameManager_Racing.Instance;
        if (gm != null)
        {
            gm.OnCarCrash(impactSpeed, severity);
        }

        // Duration, impulse, torque all come from this severity
        float crashDuration = Mathf.Lerp(minCrashDuration, maxCrashDuration, severity);
        float impulseMag = impactSpeed * impulsePerUnitSpeed;
        float torqueMag = impactSpeed * torquePerUnitSpeed;

        // Direction to push the car away from the contact
        Vector3 hitDir;
        if (collision.contactCount > 0)
        {
            // contact normal points from the OTHER collider into ours, so we shove along it
            hitDir = collision.GetContact(0).normal;
        }
        else
        {
            hitDir = (transform.position - collision.transform.position).normalized;
        }

        TriggerCrash(hitDir, crashDuration, impulseMag, torqueMag);
    }

    private void OnTriggerEnter(Collider other)
    {
        // Only react to layers we care about
        if (((1 << other.gameObject.layer) & crashLayers) == 0)
            return;

        // If the obstacle has a Rigidbody, use relative velocity. Otherwise approximate.
        float impactSpeed = 0f;

        Rigidbody otherRb = other.attachedRigidbody;
        if (otherRb != null)
        {
            // Relative velocity between the two rigidbodies
            impactSpeed = (rb.velocity - otherRb.velocity).magnitude;
        }
        else
        {
            // Otherwise approximate based on the car’s own speed
            impactSpeed = rb.velocity.magnitude;
        }

        // Ignore minor bumps
        if (impactSpeed < minImpactSpeed)
            return;

        // Map impact speed to severity (0–1)
        float severity = Mathf.InverseLerp(minImpactSpeed, maxImpactSpeed, impactSpeed);
        var gm = GameManager_Racing.Instance;
        if (gm != null)
        {
            gm.OnCarCrash(impactSpeed, severity);
        }

        // Use severity to calculate values
        float crashDuration = Mathf.Lerp(minCrashDuration, maxCrashDuration, severity);
        float impulseMag = impactSpeed * impulsePerUnitSpeed;
        float torqueMag = impactSpeed * torquePerUnitSpeed;

        // Impact direction: push away from the obstacle
        Vector3 hitDir = transform.position - other.bounds.center;
        hitDir.y = 0f;
        hitDir.Normalize();

        TriggerCrash(hitDir, crashDuration, impulseMag, torqueMag);
    }

    private void UpdateCrashReorientation()
    {
        if (!_isReorienting)
            return;

        _reorientElapsed += Time.deltaTime;
        float t = Mathf.Clamp01(_reorientElapsed / reorientDuration);

        // Smoothly rotate from whatever rotation we ended the crash with,
        // back to the initial rotation from Awake.
        transform.rotation = Quaternion.Slerp(_reorientStartRot, _reorientTargetRot, t);

        if (t >= 1f)
        {
            _isReorienting = false;
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
}