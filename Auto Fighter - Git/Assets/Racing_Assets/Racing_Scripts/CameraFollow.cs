using System;
using System.Collections;
using UnityEngine;

/// <summary>
/// Camera follow with yaw‑only tracking, optional screen shake,
/// runtime FOV control utilities, plus optional speed‑based automatic FOV.
/// </summary>
public class CameraFollow : MonoBehaviour
{
    [Header("Follow Target")]
    [SerializeField] public Transform target;

    [Header("Camera Settings")]
    [SerializeField] private Vector3 offset = new Vector3(0, 8, -12);
    [SerializeField, Range(5f, 45f)] private float cameraPitch = 20f;

    [Header("FOV")]
    [Tooltip("Camera used for FOV changes. If null, auto-grab Camera.main at runtime.")]
    [SerializeField] private Camera cam;
    [SerializeField] private float defaultFOV = 60f;
    [SerializeField] private bool overrideDefaultFOVFromCamera = true;

    [Header("FOV Control")]
    [SerializeField] private KeyCode fovIncreaseKey = KeyCode.Tab;

    [Header("Map Peek (Hold Key)")]
    [SerializeField] private float mapPeekMultiplier = 1.25f;   // 1.15–1.35 feels good
    [SerializeField] private float mapPeekMaxFOV = 95f;         // safety cap
    [SerializeField] private float mapPeekRampIn = 0.10f;       // seconds
    [SerializeField] private float mapPeekRampOut = 0.18f;      // seconds



    [Header("FOV Animation")]
    [Tooltip("Curve speed for FOV lerp. Higher = faster.")]
    [SerializeField] private float fovLerpSpeed = 6f;

    // ★ Speed-based FOV settings
    [Header("Auto Speed FOV")]
    [SerializeField] private bool useSpeedBasedFOV = true;                 // enable automatic speed FOV
    [SerializeField] private CarController car;                            // optional explicit reference
    [SerializeField] private float fovSpeedMin = 0f;                       // speed where FOV = fovAtMinSpeed
    [SerializeField] private float fovSpeedMax = 40f;                      // speed where FOV = fovAtMaxSpeed
    [SerializeField] private float fovAtMinSpeed = 58f;                    // low-speed FOV
    [SerializeField] private float fovAtMaxSpeed = 70f;                    // high-speed FOV
    [SerializeField] private float speedFovSmooth = 4f;                    // smoothing factor (lerp rate)

    [Header("Smoothing")]
    [SerializeField] private float positionFollowSpeed = 8f;
    [SerializeField] private float rotationFollowSpeed = 8f;

    [Tooltip("How quickly the *camera's forward* catches up to the car's forward.\nLower = more lag, more looseness.")]
    [SerializeField] private float rotationLag = 4f;
    [Tooltip("While drifting, multiply all camera follow rates by this (higher = less lag / sharper). Eases back after drift.")]
    [SerializeField, Min(1f)] private float driftLagSharpness = 2.2f;
    [Tooltip("How quickly follow rates snap up when drift starts.")]
    [SerializeField, Min(0.5f)] private float driftLagSharpnessRampIn = 12f;
    [Tooltip("How quickly follow rates ease back to normal after drift ends.")]
    [SerializeField, Min(0.5f)] private float driftLagSharpnessRampOut = 4f;

    [Header("Turn Lag Tightening")]
    [Tooltip("At full steer, multiply all camera follow rates by this (position, rotation lag, look, roll). Higher = less lag / tighter tracking. 1 = no change.")]
    [SerializeField, Min(1f)] private float turnLagSharpness = 1.85f;
    [Tooltip("Steer strength (0–1) where lag tightening begins.")]
    [SerializeField, Range(0f, 1f)] private float turnLagTightenStart = 0.25f;
    [Tooltip("Steer strength (0–1) where lag tightening reaches full Turn Lag Sharpness.")]
    [SerializeField, Range(0f, 1f)] private float turnLagTightenFull = 1f;
    [Tooltip("How quickly lag tightens as you push into a turn (higher = snappier).")]
    [SerializeField, Min(0.5f)] private float turnLagSharpnessRampIn = 10f;
    [Tooltip("How quickly lag loosens when you ease off the stick (higher = faster return to base lag).")]
    [SerializeField, Min(0.5f)] private float turnLagSharpnessRampOut = 5f;

    [Header("Drift Camera Tremor")]
    [Tooltip("Maximum local positional shake while drifting. Keep this small.")]
    [SerializeField, Min(0f)] private float driftTremorStrength = 0.045f;
    [Tooltip("How rapidly the small drift tremors change direction.")]
    [SerializeField, Min(0f)] private float driftTremorFrequency = 28f;
    [Tooltip("How quickly the tremor fades in and out with drifting.")]
    [SerializeField, Min(0f)] private float driftTremorResponse = 18f;

    [Header("Drift FOV Zoom")]
    [Tooltip("How many FOV degrees to zoom in (subtract) at full drift charge.")]
    [SerializeField, Min(0f)] private float driftZoomInDeltaFOV = 8f;
    [Tooltip("Seconds to blend the drift zoom-in.")]
    [SerializeField, Min(0.01f)] private float driftFovRampIn = 0.15f;
    [Tooltip("Seconds to blend the drift zoom back out.")]
    [SerializeField, Min(0.01f)] private float driftFovRampOut = 0.25f;

    [Header("Air Trick Camera Lock")]
    [Tooltip("While tricking in the air: keep camera upright with a fixed heading, but still follow the car's position.")]
    [SerializeField] private bool enableTrickCameraFreeze = true;
    [Tooltip("How fast the camera blends into the frozen trick pose.")]
    [SerializeField, Min(0.5f)] private float trickCameraBlendInSpeed = 12f;
    [Tooltip("Unused for release (release uses catch-up lag). Kept so existing scene values don't warn.")]
    [SerializeField, Min(0.5f)] private float trickCameraBlendOutSpeed = 3.5f;
    [Tooltip("Heading catch-up rate after landing from a trick (higher = faster ease off the lock). Then settles to normal Rotation Lag.")]
    [SerializeField, Min(0.5f)] private float trickReleaseCatchupLag = 9f;
    [Tooltip("How quickly post-trick catch-up lag blends down to the normal Rotation Lag.")]
    [SerializeField, Min(0.5f)] private float trickReleaseLagReturnSpeed = 6f;

    private Vector3 smoothedForward = Vector3.zero;
    private float _trickCameraBlend;
    private Vector3 _trickLockForward = Vector3.forward;
    private Quaternion _trickLockRotation;
    private bool _wasInAirTrickMode;
    private bool _holdTrickCameraUntilLand;
    private bool _postTrickReleaseActive;
    private float _postTrickLag;

    // Screen shake state
    private float shakeTimer = 0f;
    private float shakeDuration = 0f;
    private float shakeStrength = 0f;
    private int shakeVibrato = 10;
    private float shakeRandomness = 0f;
    private float shakeSeed;
    private float _driftTremorBlend;
    private float _driftTremorSeed;
    private float _driftFovOffsetCurrent;
    private float _driftLagSharpnessBlend;
    private float _turnLagSharpnessBlend;
    private Coroutine _mapPeekCR;
    private bool _mapPeekHeld;

    // FOV animation state
    private float _startFOV;
    private float _targetFOV;
    private float _fovLerpT;
    private float _fovLerpDuration;
    private bool _fovAnimating;

    // ★ runtime speed-FOV target
    private float _speedFovCurrent; // smoothed applied value

    // NEW: suppression flag so ZoomPulse can block auto speed-FOV while doing its realtime tween
    private bool _suppressAutoFov = false;

    // Boost VFX + zoom
    [Header("Boost VFX")]
    [Tooltip("Optional GameObject (or ParticleSystem) parented to camera to play during boosts.")]
    [SerializeField] private GameObject boostVFXObject;
    [SerializeField, Tooltip("After a boost ends, keep the boost VFX lines emitting for this long before stopping, so they trail off instead of cutting out instantly. Existing particles also finish their own lifetime on top of this.")]
    private float boostVfxLingerSeconds = 0.35f;
    [SerializeField, Tooltip("Extra FOV degrees added on top of speed-based FOV while boost presentation is active (pads, ramps, manual boost).")]
    private float boostZoomOutDeltaFOV = 6f;

    [SerializeField, Tooltip("Seconds to blend the boost FOV offset in.")]
    private float boostFovRampIn = 0.12f;
    [SerializeField, Tooltip("Seconds to blend the boost FOV offset out.")]
    private float boostFovRampOut = 0.35f;
    [Tooltip("At high speed, reduce boost FOV offset so velocity-based zoom and boost presentation do not stack.")]
    [SerializeField, Range(0f, 1f)] private float boostFovOffsetSpeedFalloff = 0.85f;

    private ParticleSystem _boostPS;
    private CarController _subscribedCar;

    // Boost presentation: additive FOV offset blended into the same pipeline as speed FOV (no fighting coroutines).
    private float _boostFovOffsetTarget;
    private float _boostFovOffsetCurrent;

    // Z-rotation ("roll") mapping to accentuate turns/drift
    [Header("Turn-Driven Z-Rotation (Camera Roll)")]
    [Tooltip("Enable roll mapping (camera Z rotation) based on car turning.")]
    [SerializeField] private bool enableZRoll = true;

    [Tooltip("Invert the Z roll sign (useful if you want opposite roll direction).")]
    [SerializeField] private bool invertZRoll = false;

    [Tooltip("Base scale applied to computed roll from yaw-rate (deg/sec -> degrees).")]
    [SerializeField, Min(0f)] private float zRollScale = 0.06f; // deg roll per deg/sec

    [Tooltip("Divider for converting yaw-rate (deg/sec) into a normalized [-1..1] before scaling.")]
    [SerializeField, Min(1f)] private float zRollYawRateDivisor = 120f;

    [Tooltip("How much drifting/lateral velocity amplifies roll (0 = none).")]
    [SerializeField, Range(0f, 3f)] private float driftInfluence = 0.9f;

    [Tooltip("Maximum allowed absolute roll angle in degrees.")]
    [SerializeField, Range(0f, 45f)] private float maxRollDegrees = 18f;

    [Tooltip("Multiplier applied to camera roll while actively drifting.")]
    [SerializeField, Min(1f)] private float driftRollMultiplier = 2.2f;

    [Tooltip("Maximum camera roll allowed at full drift turn intensity.")]
    [SerializeField, Range(0f, 45f)] private float maxDriftRollDegrees = 6f;

    [Tooltip("How fast signed drift tilt moves toward the stick (-1..1 units per second). Lower = slower build / softer side-to-side crossfade.")]
    [SerializeField, Min(0.1f)] private float driftTiltChangeSpeed = 2.4f;

    [Tooltip("While reversing mid-drift, multiply Drift Tilt Change Speed by this so left↔right crossfades through center instead of snapping.")]
    [SerializeField, Range(0.05f, 1f)] private float driftTiltCrossfadeSpeedMult = 0.45f;

    [Tooltip("How fast tilt returns to level after drift ends (-1..1 units per second).")]
    [SerializeField, Min(0.1f)] private float driftTiltReleaseSpeed = 1.6f;

    [Tooltip("Roll smoothing after drift fully ends (lower = less snappy level-out).")]
    [SerializeField, Min(0.5f)] private float driftTiltRollReleaseSmooth = 3f;

    [Tooltip("Smoothing speed for roll interpolation (higher = faster).")]
    [SerializeField, Min(0f)] private float rollSmoothing = 8f;

    [Tooltip("Scale used when using lateral velocity to detect drift influence. Higher = less influence from lateral speed.")]
    [SerializeField, Min(0.1f)] private float lateralVelocityNormalization = 6f;




    // internal roll state
    private float _currentZRoll = 0f;
    /// <summary>Smoothed signed drift bank in [-1, 1]. Crosses 0 when flipping sides mid-drift.</summary>
    private float _driftRollSigned = 0f;
    private Vector3 _prevTargetForwardFlat = Vector3.forward;
    private bool _boostVfxPlaying;
    private float _boostVfxLingerTimer;
    // Map peek cached values (prevents stacking)
    private float _mapPeekPressBaseline;
    private float _mapPeekPressTarget;

    private void Awake()
    {
        if (cam == null)
            cam = Camera.main;

        if (cam != null && overrideDefaultFOVFromCamera)
            defaultFOV = cam.fieldOfView;

        _startFOV = _targetFOV = defaultFOV;
        _fovLerpDuration = 0f;
        _fovAnimating = false;
        _driftTremorSeed = UnityEngine.Random.value * 1000f;

        // ★ Try to auto-bind car if not supplied
        if (car == null && target != null)
            car = target.GetComponent<CarController>() ?? target.GetComponentInParent<CarController>();

        // cache particle system if VFX object present
        if (boostVFXObject != null)
            _boostPS = boostVFXObject.GetComponent<ParticleSystem>();

        if (_boostPS != null)
            _boostPS.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        else if (boostVFXObject != null)
            boostVFXObject.SetActive(false);

        _speedFovCurrent = defaultFOV;

        // initialize prev forward
        if (target != null)
        {
            var tf = target.forward;
            tf.y = 0f;
            if (tf.sqrMagnitude < 0.0001f) tf = Vector3.forward;
            _prevTargetForwardFlat = tf.normalized;
        }

        SubscribeToCarBoosts();
    }

    private void OnDisable()
    {
        UnsubscribeFromCarBoosts();
    }

    private void SubscribeToCarBoosts()
    {
        if (_subscribedCar != null)
        {
            try { _subscribedCar.OnCrash -= HandleCrashForceStopBoostVfx; } catch { }
            _subscribedCar = null;
        }

        if (car == null && target != null)
            car = target.GetComponent<CarController>() ?? target.GetComponentInParent<CarController>();

        if (car != null)
        {
            _subscribedCar = car;
            try { _subscribedCar.OnCrash += HandleCrashForceStopBoostVfx; } catch { }
        }
    }

    private void UnsubscribeFromCarBoosts()
    {
        if (_subscribedCar != null)
        {
            try { _subscribedCar.OnCrash -= HandleCrashForceStopBoostVfx; } catch { }
            _subscribedCar = null;
        }
    }

    private void HandleCrashForceStopBoostVfx(float _)
    {
        _boostFovOffsetTarget = 0f;
        StopBoostVfxParticles();
    }

    private void SyncBoostPresentation(bool wantPresentation, float dt)
    {
        _boostFovOffsetTarget = wantPresentation ? Mathf.Max(0f, boostZoomOutDeltaFOV) : 0f;

        if (wantPresentation)
        {
            _boostVfxLingerTimer = Mathf.Max(0f, boostVfxLingerSeconds);

            if (!_boostVfxPlaying)
            {
                _boostVfxPlaying = true;
                if (boostVFXObject != null)
                {
                    if (_boostPS != null)
                        _boostPS.Play(true);
                    else
                        boostVFXObject.SetActive(true);
                }
            }
            return;
        }

        if (!_boostVfxPlaying)
            return;

        // Boost just ended: keep emitting through the linger window so the lines trail off instead of cutting out.
        if (_boostVfxLingerTimer > 0f)
        {
            _boostVfxLingerTimer -= dt;
            return;
        }

        StopBoostVfxParticles(hardClear: false);
    }

    private void StopBoostVfxParticles(bool hardClear = true)
    {
        _boostVfxPlaying = false;
        _boostVfxLingerTimer = 0f;

        if (boostVFXObject == null)
            return;

        if (_boostPS != null)
        {
            // Soft stop: stop spawning new particles but let already-spawned lines live out their lifetime
            // (graceful trail-off). Hard clear wipes them instantly (used on crash).
            _boostPS.Stop(true, hardClear
                ? ParticleSystemStopBehavior.StopEmittingAndClear
                : ParticleSystemStopBehavior.StopEmitting);
        }
        else
        {
            boostVFXObject.SetActive(false);
        }
    }

    private float ComputeSpeedFovTarget()
    {
        if (!useSpeedBasedFOV || car == null)
            return defaultFOV;

        float speed = car.CurrentSpeed;
        float norm = Mathf.InverseLerp(fovSpeedMin, fovSpeedMax, speed);
        return Mathf.Lerp(fovAtMinSpeed, fovAtMaxSpeed, norm);
    }

    private void UpdateBoostFovOffset(float dt)
    {
        float rampSeconds = _boostFovOffsetTarget > _boostFovOffsetCurrent
            ? Mathf.Max(0.01f, boostFovRampIn)
            : Mathf.Max(0.01f, boostFovRampOut > 0f ? boostFovRampOut : 0.2f);

        float t = 1f - Mathf.Exp(-dt / rampSeconds);
        _boostFovOffsetCurrent = Mathf.Lerp(_boostFovOffsetCurrent, _boostFovOffsetTarget, t);
    }

    private void UpdateDriftFovOffset(float dt)
    {
        // Negative FOV = zoom in. Scales with drift charge while actively drifting.
        float driftTarget = car != null && car.IsDrifting
            ? -Mathf.Max(0f, driftZoomInDeltaFOV)
                * Mathf.Lerp(0.45f, 1f, Mathf.Clamp01(car.DriftCharge))
                * Mathf.Clamp01(car.DriftGroundFeel)
            : 0f;

        float rampSeconds = driftTarget < _driftFovOffsetCurrent
            ? Mathf.Max(0.01f, driftFovRampIn)
            : Mathf.Max(0.01f, driftFovRampOut);

        float t = 1f - Mathf.Exp(-dt / rampSeconds);
        _driftFovOffsetCurrent = Mathf.Lerp(_driftFovOffsetCurrent, driftTarget, t);
    }

    private void UpdateUnifiedAutoFov(float dt)
    {
        if (cam == null || _fovAnimating)
            return;

        if (_subscribedCar != null)
            SyncBoostPresentation(_subscribedCar.IsBoostPresentationActive, dt);

        UpdateBoostFovOffset(dt);
        UpdateDriftFovOffset(dt);

        float baseTarget = useSpeedBasedFOV ? ComputeSpeedFovTarget() : defaultFOV;

        float speedNorm = useSpeedBasedFOV && car != null
            ? Mathf.InverseLerp(fovSpeedMin, fovSpeedMax, car.CurrentSpeed)
            : 0f;
        float boostScale = Mathf.Lerp(1f, 1f - boostFovOffsetSpeedFalloff, speedNorm);
        float combinedTarget = baseTarget + _boostFovOffsetCurrent * boostScale + _driftFovOffsetCurrent;
        _speedFovCurrent = Mathf.Lerp(_speedFovCurrent, combinedTarget, speedFovSmooth * dt);

        if (!_suppressAutoFov)
            cam.fieldOfView = _speedFovCurrent;
    }

    private void LateUpdate()
    {
        if (target == null) return;

        Vector3 fallbackFlat = smoothedForward.sqrMagnitude > 0.0001f
            ? smoothedForward
            : (_prevTargetForwardFlat.sqrMagnitude > 0.0001f ? _prevTargetForwardFlat : Vector3.forward);
        Vector3 targetForwardFlat = FlattenForward(target.forward, fallbackFlat);

        if (smoothedForward == Vector3.zero)
            smoothedForward = targetForwardFlat;

        bool inAirTrickMode = enableTrickCameraFreeze && car != null && car.IsInAirTrickMode;
        bool airborneForTricks = car != null && car.IsAirborneForTricks;

        if (inAirTrickMode && !_wasInAirTrickMode)
            CaptureTrickCameraLock();

        if (!inAirTrickMode && _wasInAirTrickMode)
        {
            // Released trick stick: stay locked until landing if still airborne.
            if (airborneForTricks)
            {
                _holdTrickCameraUntilLand = true;
                _trickCameraBlend = 1f;
            }
            else
            {
                BeginTrickCameraRelease(targetForwardFlat);
            }
        }

        if (_holdTrickCameraUntilLand && !airborneForTricks)
        {
            BeginTrickCameraRelease(targetForwardFlat);
            _holdTrickCameraUntilLand = false;
        }

        _wasInAirTrickMode = inAirTrickMode;

        bool useTrickLockPose = inAirTrickMode || _holdTrickCameraUntilLand;

        // Only blend INTO trick lock while actively tricking.
        if (inAirTrickMode)
        {
            _trickCameraBlend = Mathf.Lerp(
                _trickCameraBlend,
                1f,
                1f - Mathf.Exp(-trickCameraBlendInSpeed * Time.deltaTime));
        }

        bool fullyLocked = useTrickLockPose && _trickCameraBlend >= 0.995f;
        float blendT = Mathf.SmoothStep(0f, 1f, _trickCameraBlend);

        Vector3 basePos;
        Quaternion baseRot;
        if (fullyLocked)
        {
            smoothedForward = _trickLockForward;
            basePos = ComputeTrickPositionFollow();
            baseRot = _trickLockRotation;
        }
        else if (useTrickLockPose && _trickCameraBlend > 0.001f)
        {
            // Blending into freeze only — mix toward locked pose.
            ComputeNormalFollow(targetForwardFlat, out Vector3 normalPos, out Quaternion normalRot);
            Vector3 trickPos = ComputeTrickPositionFollow();
            basePos = Vector3.Lerp(normalPos, trickPos, blendT);
            baseRot = Quaternion.Slerp(normalRot, _trickLockRotation, blendT);
        }
        else
        {
            // Normal follow (including post-landing trick release).
            ComputeNormalFollow(targetForwardFlat, out basePos, out baseRot);
        }

        Vector3 shakeOffset = ComputeShakeOffset(baseRot) + ComputeDriftTremorOffset(baseRot);

        transform.position = basePos + shakeOffset;
        transform.rotation = baseRot;

        UpdateFOV(Time.deltaTime);

        if (cam != null && car != null)
            UpdateUnifiedAutoFov(Time.deltaTime);

        if (!fullyLocked)
            _prevTargetForwardFlat = targetForwardFlat;

        HandleMapPeekInput();
    }

    private static Vector3 FlattenForward(Vector3 forward, Vector3 fallbackFlat)
    {
        Vector3 flat = forward;
        flat.y = 0f;
        if (flat.sqrMagnitude < 0.0001f)
        {
            flat = fallbackFlat;
            flat.y = 0f;
            if (flat.sqrMagnitude < 0.0001f)
                flat = Vector3.forward;
        }
        return flat.normalized;
    }

    private void CaptureTrickCameraLock()
    {
        Vector3 forward = smoothedForward.sqrMagnitude > 0.0001f
            ? FlattenForward(smoothedForward, Vector3.forward)
            : FlattenForward(target.forward, _prevTargetForwardFlat);

        _trickLockForward = forward;

        Vector3 e = Quaternion.LookRotation(forward, Vector3.up).eulerAngles;
        e.x = cameraPitch;
        e.z = 0f;
        _trickLockRotation = Quaternion.Euler(e);
    }

    /// <summary>
    /// Leave trick lock without a hard snap: start from the locked heading, use a fast
    /// catch-up lag briefly, then settle back into the default Rotation Lag.
    /// </summary>
    private void BeginTrickCameraRelease(Vector3 targetForwardFlat)
    {
        _trickCameraBlend = 0f;
        _currentZRoll = 0f;
        _prevTargetForwardFlat = targetForwardFlat;
        smoothedForward = _trickLockForward;
        _postTrickReleaseActive = true;
        _postTrickLag = Mathf.Max(trickReleaseCatchupLag, rotationLag);
    }

    private Vector3 ComputeTrickPositionFollow()
    {
        Quaternion lockYaw = Quaternion.LookRotation(_trickLockForward, Vector3.up);
        Vector3 desiredPos = target.position + lockYaw * offset;
        return Vector3.Lerp(transform.position, desiredPos, positionFollowSpeed * Time.deltaTime);
    }

    private void ComputeNormalFollow(Vector3 targetForwardFlat, out Vector3 basePos, out Quaternion baseRot)
    {
        float driftTarget = car != null && car.IsDrifting
            ? Mathf.Lerp(0.35f, 1f, Mathf.Clamp01(car.DriftCharge)) * Mathf.Clamp01(car.DriftGroundFeel)
            : 0f;
        float sharpnessRamp = driftTarget > _driftLagSharpnessBlend
            ? driftLagSharpnessRampIn
            : driftLagSharpnessRampOut;
        _driftLagSharpnessBlend = Mathf.Lerp(
            _driftLagSharpnessBlend,
            driftTarget,
            1f - Mathf.Exp(-sharpnessRamp * Time.deltaTime));

        float driftSharpness = Mathf.Lerp(1f, Mathf.Max(1f, driftLagSharpness), _driftLagSharpnessBlend);

        // Harder steer → less camera rotation lag so heading matches the car more closely.
        float steer01 = car != null ? Mathf.Clamp01(car.SteerIntensity) : 0f;
        float turnTarget = 0f;
        if (turnLagTightenFull > turnLagTightenStart + 0.0001f)
            turnTarget = Mathf.InverseLerp(turnLagTightenStart, turnLagTightenFull, steer01);
        else if (steer01 >= turnLagTightenFull)
            turnTarget = 1f;

        float turnRamp = turnTarget > _turnLagSharpnessBlend
            ? turnLagSharpnessRampIn
            : turnLagSharpnessRampOut;
        _turnLagSharpnessBlend = Mathf.Lerp(
            _turnLagSharpnessBlend,
            turnTarget,
            1f - Mathf.Exp(-turnRamp * Time.deltaTime));

        float turnSharpness = Mathf.Lerp(1f, Mathf.Max(1f, turnLagSharpness), _turnLagSharpnessBlend);

        // Drift + turn sharpness tighten every camera lag channel (position, heading, look, roll).
        float sharpness = driftSharpness * turnSharpness;
        float effectiveRotationLag = rotationLag * sharpness;
        float effectivePositionFollow = positionFollowSpeed * sharpness;
        float effectiveRotationFollow = rotationFollowSpeed * sharpness;
        float effectiveRollSmoothing = rollSmoothing * sharpness;

        // While entering trick lock (or holding it until landing), keep orbit heading on the freeze basis.
        if (enableTrickCameraFreeze && car != null && (car.IsInAirTrickMode || _holdTrickCameraUntilLand))
        {
            smoothedForward = _trickLockForward;
            _postTrickReleaseActive = false;
        }
        else
        {
            float lag = effectiveRotationLag;
            if (_postTrickReleaseActive)
            {
                // Fast ease off the lock, then blend lag rate itself down to the normal default.
                _postTrickLag = Mathf.Lerp(
                    _postTrickLag,
                    effectiveRotationLag,
                    1f - Mathf.Exp(-trickReleaseLagReturnSpeed * Time.deltaTime));
                lag = Mathf.Max(_postTrickLag, effectiveRotationLag);

                float headingErr = Vector3.Angle(smoothedForward, targetForwardFlat);
                if (headingErr < 2.5f && Mathf.Abs(_postTrickLag - effectiveRotationLag) < 0.08f)
                {
                    _postTrickReleaseActive = false;
                    lag = effectiveRotationLag;
                }
            }

            smoothedForward = Vector3.Slerp(smoothedForward, targetForwardFlat, lag * Time.deltaTime);
        }

        Quaternion yawOnly = Quaternion.LookRotation(smoothedForward, Vector3.up);

        Vector3 desiredPos = target.position + yawOnly * offset;
        basePos = Vector3.Lerp(transform.position, desiredPos, effectivePositionFollow * Time.deltaTime);

        Vector3 e = yawOnly.eulerAngles;
        e.x = cameraPitch;

        float rollAngle = 0f;
        if (enableZRoll)
        {
            float signedYawDelta = Vector3.SignedAngle(_prevTargetForwardFlat, targetForwardFlat, Vector3.up);
            signedYawDelta = Mathf.Clamp(signedYawDelta, -45f, 45f);
            float yawRateDegPerSec = signedYawDelta / Mathf.Max(1e-6f, Time.deltaTime);
            float yawNorm = Mathf.Clamp(yawRateDegPerSec / zRollYawRateDivisor, -1f, 1f);

            float sign = invertZRoll ? -1f : 1f;
            float baseRoll = yawNorm * zRollScale * 180f * sign;

            // Drift bank: signed tilt tracks turn intensity and must ease through 0 when flipping sides.
            bool driftingNow = car != null && car.IsDrifting;
            float signedTiltTarget = 0f;
            if (driftingNow)
            {
                int driftDir = car.DriftSteerDirectionSign;
                float intensity = Mathf.Clamp01(car.DriftSteerIntensity);
                if (driftDir != 0)
                    signedTiltTarget = driftDir * intensity;
            }

            float tiltSpeed = driftingNow ? driftTiltChangeSpeed : driftTiltReleaseSpeed;
            // Opposite-side target while still tilted: slow crossfade through center (no snap).
            bool crossingSides = driftingNow
                && Mathf.Abs(_driftRollSigned) > 0.01f
                && Mathf.Abs(signedTiltTarget) > 0.01f
                && Mathf.Sign(_driftRollSigned) != Mathf.Sign(signedTiltTarget);
            if (crossingSides)
                tiltSpeed *= driftTiltCrossfadeSpeedMult;

            _driftRollSigned = Mathf.MoveTowards(
                _driftRollSigned,
                signedTiltTarget,
                Mathf.Max(0.01f, tiltSpeed) * Time.deltaTime);

            float rollTarget;
            if (Mathf.Abs(_driftRollSigned) > 0.001f)
            {
                // Hard steer into the drift → maxDriftRollDegrees; ease / flip → scales with signed tilt.
                rollTarget = sign * (maxDriftRollDegrees * _driftRollSigned);
            }
            else
            {
                // Non-drift camera roll (yaw-based).
                float lateralFactor = 0f;
                var rb = target.GetComponent<Rigidbody>();
                if (rb != null)
                {
                    float lateralVel = Vector3.Dot(rb.velocity, target.right);
                    lateralFactor = Mathf.Clamp01(Mathf.Abs(lateralVel) / lateralVelocityNormalization);
                }

                float rollAmp = 1f + (driftInfluence * lateralFactor);
                rollTarget = Mathf.Clamp(baseRoll * rollAmp, -maxRollDegrees, maxRollDegrees);
            }

            // Softer roll lerp only after drift ends; while drifting, track the signed tilt closely.
            float rollSmooth = (!driftingNow && Mathf.Abs(_driftRollSigned) > 0.001f)
                ? Mathf.Min(effectiveRollSmoothing, driftTiltRollReleaseSmooth)
                : effectiveRollSmoothing;
            _currentZRoll = Mathf.Lerp(_currentZRoll, rollTarget, 1f - Mathf.Exp(-rollSmooth * Time.deltaTime));
            rollAngle = _currentZRoll;
        }

        e.z = rollAngle;
        Quaternion desiredRot = Quaternion.Euler(e);
        baseRot = Quaternion.Slerp(transform.rotation, desiredRot, effectiveRotationFollow * Time.deltaTime);
    }

    private Vector3 ComputeShakeOffset(Quaternion baseRot)
    {
        if (shakeTimer <= 0f || shakeDuration <= 0f || shakeStrength <= 0f)
        {
            shakeTimer = 0f;
            return Vector3.zero;
        }

        shakeTimer -= Time.deltaTime;
        float remaining = Mathf.Max(0f, shakeTimer);
        float elapsed = Mathf.Clamp(shakeDuration - remaining, 0f, shakeDuration);
        float t = shakeDuration > 0f ? (elapsed / shakeDuration) : 1f;
        float amplitude = 1f - t;
        amplitude *= amplitude;

        float frequency = Mathf.Max(1, shakeVibrato);
        float angle = (elapsed + shakeSeed) * frequency * Mathf.PI * 2f;
        Vector2 osc = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));

        if (shakeRandomness > 0f)
        {
            float r1 = Mathf.PerlinNoise(shakeSeed, elapsed * 10f) * 2f - 1f;
            float r2 = Mathf.PerlinNoise(shakeSeed + 37.1f, elapsed * 10f) * 2f - 1f;
            osc += new Vector2(r1, r2) * (shakeRandomness / 180f);
        }

        if (osc.sqrMagnitude > 0.0001f)
            osc.Normalize();

        Vector3 right = baseRot * Vector3.right;
        Vector3 up = baseRot * Vector3.up;
        return (right * osc.x + up * osc.y) * (shakeStrength * amplitude);
    }

    private Vector3 ComputeDriftTremorOffset(Quaternion baseRot)
    {
        // Tremor follows smoothed drift-bank intensity (not charge alone), so side flips ease out/in.
        float driftTarget = Mathf.Abs(_driftRollSigned);
        if (car != null && car.IsDrifting)
            driftTarget *= Mathf.Lerp(0.55f, 1f, Mathf.Clamp01(car.DriftCharge)) * Mathf.Clamp01(car.DriftGroundFeel);

        _driftTremorBlend = Mathf.Lerp(
            _driftTremorBlend,
            driftTarget,
            1f - Mathf.Exp(-driftTremorResponse * Time.deltaTime));

        if (_driftTremorBlend <= 0.001f || driftTremorStrength <= 0f || driftTremorFrequency <= 0f)
            return Vector3.zero;

        float sampleTime = Time.time * driftTremorFrequency;
        float x = Mathf.PerlinNoise(_driftTremorSeed, sampleTime) * 2f - 1f;
        float y = Mathf.PerlinNoise(_driftTremorSeed + 37.1f, sampleTime) * 2f - 1f;
        Vector3 right = baseRot * Vector3.right;
        Vector3 up = baseRot * Vector3.up;
        return (right * x + up * y) * (driftTremorStrength * _driftTremorBlend);
    }

    public void SetTarget(Transform t)
    {
        target = t;
        smoothedForward = Vector3.zero;
        _trickCameraBlend = 0f;
        _wasInAirTrickMode = false;
        _holdTrickCameraUntilLand = false;
        _postTrickReleaseActive = false;
        if (car == null && t != null)
            car = t.GetComponent<CarController>() ?? t.GetComponentInParent<CarController>();

        SubscribeToCarBoosts();
    }

    public void StartShake(float duration, float strength, int vibrato, float randomness)
    {
        shakeDuration = Mathf.Max(0f, duration);
        shakeTimer = shakeDuration;
        shakeStrength = Mathf.Max(0f, strength);
        shakeVibrato = Mathf.Max(1, vibrato);
        shakeRandomness = Mathf.Max(0f, randomness);
        shakeSeed = UnityEngine.Random.value * 1000f;
    }

    // FOV API
    public void SetFieldOfView(float targetFOV, float duration = 0f)
    {
        if (cam == null) return;
        targetFOV = Mathf.Clamp(targetFOV, 1f, 179f);

        if (duration <= 0f)
        {
            cam.fieldOfView = targetFOV;
            _fovAnimating = false;
            // ★ Sync speed-based base to immediate value to avoid snap after animation
            _speedFovCurrent = targetFOV;
            return;
        }

        _startFOV = cam.fieldOfView;
        _targetFOV = targetFOV;
        _fovLerpT = 0f;
        _fovLerpDuration = duration;
        _fovAnimating = true;
    }

    private void HandleMapPeekInput()
    {
        if (cam == null) return;

        bool fovHeld = (RacingInputReader.Instance != null && RacingInputReader.Instance.FovPeekHeld) || Input.GetKey(fovIncreaseKey);

        if (fovHeld && !_mapPeekHeld)
        {
            _mapPeekHeld = true;

            if (_mapPeekCR != null) StopCoroutine(_mapPeekCR);

            _suppressAutoFov = true;
            _mapPeekPressBaseline = GetBaselineFov();
            _mapPeekPressTarget = Mathf.Min(mapPeekMaxFOV, _mapPeekPressBaseline * mapPeekMultiplier);
            float from = cam.fieldOfView;

            _mapPeekCR = StartCoroutine(MapPeekHoldCoroutine(
                fromFOV: from,
                toFOV: _mapPeekPressTarget,
                rampIn: Mathf.Max(0.01f, mapPeekRampIn)
            ));
        }

        if (!fovHeld && _mapPeekHeld)
        {
            _mapPeekHeld = false;

            if (_mapPeekCR != null) StopCoroutine(_mapPeekCR);

            _mapPeekCR = StartCoroutine(MapPeekReturnCoroutine(
                fromFOV: cam.fieldOfView,
                duration: Mathf.Max(0.01f, mapPeekRampOut)
            ));
        }
    }




    private IEnumerator MapPeekCoroutine(float fromFOV, float toFOV, float duration, bool holdUntilRelease)
    {
        float t0 = Time.realtimeSinceStartup;
        float t1 = t0 + duration;

        while (Time.realtimeSinceStartup < t1)
        {
            float u = Mathf.InverseLerp(t0, t1, Time.realtimeSinceStartup);
            float eased = Mathf.SmoothStep(0f, 1f, u);
            cam.fieldOfView = Mathf.Lerp(fromFOV, toFOV, eased);
            yield return null;
        }

        cam.fieldOfView = toFOV;

        if (holdUntilRelease)
        {
            // keep holding this FOV while key is held
            while (_mapPeekHeld)
                yield return null;
        }


        // re-enable auto only after we fully returned
        if (!holdUntilRelease)
            _suppressAutoFov = false;

        _mapPeekCR = null;
    }

    public void ResetFieldOfView(float duration = 0f)
    {
        SetFieldOfView(defaultFOV, duration);
    }

    // existing ZoomPulse (zoom in) left intact
    public void ZoomPulse(float deltaFOV, float totalDuration)
    {
        if (cam == null) return;
        if (totalDuration <= 0f || Mathf.Approximately(deltaFOV, 0f)) return;
        StartCoroutine(ZoomPulseCoroutine(Mathf.Abs(deltaFOV), Mathf.Max(0.05f, totalDuration)));
    }

    private IEnumerator MapPeekHoldCoroutine(float fromFOV, float toFOV, float rampIn)
    {
        float t0 = Time.realtimeSinceStartup;
        float t1 = t0 + rampIn;

        while (Time.realtimeSinceStartup < t1)
        {
            float u = Mathf.InverseLerp(t0, t1, Time.realtimeSinceStartup);
            float eased = Mathf.SmoothStep(0f, 1f, u);
            cam.fieldOfView = Mathf.Lerp(fromFOV, toFOV, eased);
            yield return null;
        }

        cam.fieldOfView = toFOV;

        // HOLD while key is held
        while (_mapPeekHeld)
            yield return null;

        // If release happened while we were holding, LateUpdate KeyUp will start return coroutine.
        _mapPeekCR = null;
    }

    private IEnumerator ZoomPulseCoroutine(float deltaFOV, float totalDuration)
    {
        if (cam == null) yield break;

        // Remember current auto-FOV target so we can return to it exactly
        float autoFovBefore = _speedFovCurrent;

        // Suppress automatic FOV updates while we run the realtime pulse
        _suppressAutoFov = true;

        float half = Mathf.Max(0.01f, totalDuration * 0.5f);
        float startFOV = cam.fieldOfView;
        float targetOut = Mathf.Clamp(startFOV + deltaFOV, 1f, 179f);

        // quick out (unscaled so slow-mo doesn't stall)
        float startRealtime = Time.realtimeSinceStartup;
        float endRealtime = startRealtime + half;
        while (Time.realtimeSinceStartup < endRealtime)
        {
            float u = Mathf.InverseLerp(startRealtime, endRealtime, Time.realtimeSinceStartup);
            float eased = Mathf.SmoothStep(0f, 1f, u);
            cam.fieldOfView = Mathf.Lerp(startFOV, targetOut, eased);
            yield return null;
        }

        // ensure arrived exactly
        cam.fieldOfView = targetOut;

        // back in (unscaled) — return to the remembered auto FOV target
        startRealtime = Time.realtimeSinceStartup;
        endRealtime = startRealtime + half;
        while (Time.realtimeSinceStartup < endRealtime)
        {
            float u = Mathf.InverseLerp(startRealtime, endRealtime, Time.realtimeSinceStartup);
            float eased = Mathf.SmoothStep(0f, 1f, u);
            cam.fieldOfView = Mathf.Lerp(targetOut, autoFovBefore, eased);
            yield return null;
        }

        // Finalize: restore the auto-FOV target and resume auto updates
        cam.fieldOfView = autoFovBefore;
        _speedFovCurrent = autoFovBefore;
        _suppressAutoFov = false;
    }

    private float GetBaselineFov()
    {
        return useSpeedBasedFOV
            ? ComputeSpeedFovTarget() + _boostFovOffsetCurrent + _driftFovOffsetCurrent
            : defaultFOV;
    }

    private IEnumerator MapPeekReturnCoroutine(float fromFOV, float duration)
    {
        float t0 = Time.realtimeSinceStartup;
        float t1 = t0 + duration;

        while (Time.realtimeSinceStartup < t1)
        {
            float u = Mathf.InverseLerp(t0, t1, Time.realtimeSinceStartup);
            float eased = Mathf.SmoothStep(0f, 1f, u);

            float liveBaseline = GetBaselineFov();
            cam.fieldOfView = Mathf.Lerp(fromFOV, liveBaseline, eased);
            yield return null;
        }

        cam.fieldOfView = GetBaselineFov();
        _suppressAutoFov = false;
        _mapPeekCR = null;
    }

    private void UpdateFOV(float dt)
    {
        if (!_fovAnimating || cam == null) return;

        if (_fovLerpDuration <= 0f)
        {
            cam.fieldOfView = _targetFOV;
            _fovAnimating = false;
            _speedFovCurrent = cam.fieldOfView;
            return;
        }

        _fovLerpT += dt * fovLerpSpeed / _fovLerpDuration;
        float t = Mathf.Clamp01(_fovLerpT);
        cam.fieldOfView = Mathf.Lerp(_startFOV, _targetFOV, t);

        if (t >= 1f)
        {
            _fovAnimating = false;
            _speedFovCurrent = cam.fieldOfView; // ★ hand over to auto system seamlessly
        }
    }
}