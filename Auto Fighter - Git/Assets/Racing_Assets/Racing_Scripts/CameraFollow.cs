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

    private Vector3 smoothedForward = Vector3.zero;

    // Screen shake state
    private float shakeTimer = 0f;
    private float shakeDuration = 0f;
    private float shakeStrength = 0f;
    private int shakeVibrato = 10;
    private float shakeRandomness = 0f;
    private float shakeSeed;
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
    [SerializeField, Tooltip("How many FOV degrees to increase for a boost pulse (zoom out).")]
    private float boostZoomOutDeltaFOV = 6f;
    [SerializeField, Tooltip("Total duration for boost zoom out pulse (seconds) - used for legacy quick pulses.")]
    private float boostZoomOutDuration = 0.28f;

    // New: smoothing controls for boost FOV ramp in/out
    [SerializeField, Tooltip("Seconds to smoothly ramp the FOV up when boost starts (use small values, e.g. 0.08-0.18).")]
    private float boostFovRampIn = 0.12f;
    [SerializeField, Tooltip("Seconds to smoothly restore FOV after boost ends. If zero, uses the car's postBoostSlowdownDuration.")]
    private float boostFovRampOut = 0.35f;

    private ParticleSystem _boostPS;
    private Coroutine _boostZoomCR;
    private CarController _subscribedCar;

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

    [Tooltip("Smoothing speed for roll interpolation (higher = faster).")]
    [SerializeField, Min(0f)] private float rollSmoothing = 8f;

    [Tooltip("Scale used when using lateral velocity to detect drift influence. Higher = less influence from lateral speed.")]
    [SerializeField, Min(0.1f)] private float lateralVelocityNormalization = 6f;




    // internal roll state
    private float _currentZRoll = 0f;
    private Vector3 _prevTargetForwardFlat = Vector3.forward;
    private bool _carIsBoosting;
    private float _boostEndTime;
    private float _boostPostDuration;
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

        // ★ Try to auto-bind car if not supplied
        if (car == null && target != null)
            car = target.GetComponent<CarController>() ?? target.GetComponentInParent<CarController>();

        // cache particle system if VFX object present
        if (boostVFXObject != null)
            _boostPS = boostVFXObject.GetComponent<ParticleSystem>();

        _boostPS.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

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
        // Unsubscribe previous
        if (_subscribedCar != null)
        {
            try { _subscribedCar.OnBoostStarted -= HandleBoostStarted; } catch { }
            try { _subscribedCar.OnBoostEnded -= HandleBoostEnded; } catch { }
            _subscribedCar = null;
        }

        // Prefer explicit car reference, else try target
        if (car == null && target != null)
            car = target.GetComponent<CarController>() ?? target.GetComponentInParent<CarController>();

        if (car != null)
        {
            _subscribedCar = car;
            try { _subscribedCar.OnBoostStarted += HandleBoostStarted; } catch { }
            try { _subscribedCar.OnBoostEnded += HandleBoostEnded; } catch { }
        }
    }

    private void UnsubscribeFromCarBoosts()
    {
        if (_subscribedCar != null)
        {
            try { _subscribedCar.OnBoostStarted -= HandleBoostStarted; } catch { }
            try { _subscribedCar.OnBoostEnded -= HandleBoostEnded; } catch { }
            _subscribedCar = null;
        }
    }

    private void HandleBoostStarted()
    {
        // Play VFX
        if (boostVFXObject != null)
        {
            if (_boostPS != null)
            {
                _boostPS.Play(true);
            }
            else
            {
                boostVFXObject.SetActive(true);
            }
        }

        Debug.Log($"[CameraFollow] Received OnBoostStarted. Starting FOV pulse + VFX. deltaFOV={boostZoomOutDeltaFOV:F2} dur={boostZoomOutDuration:F2}");

        // mark boosting state
        _carIsBoosting = true;

        // restart coroutine (ensures latest boost lifecycle is used)
        if (_boostZoomCR != null) StopCoroutine(_boostZoomCR);
        _boostZoomCR = StartCoroutine(BoostZoomDuringBoostCoroutine(Mathf.Abs(boostZoomOutDeltaFOV)));
    }

    private void HandleBoostEnded()
    {
        // Stop VFX (graceful)
        if (boostVFXObject != null)
        {
            if (_boostPS != null)
            {
                _boostPS.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            }
            else
            {
                boostVFXObject.SetActive(false);
            }
        }

        // mark boosting ended and record timestamp + post duration (read from CarController via reflection)
        _carIsBoosting = false;
        _boostEndTime = Time.time;
        _boostPostDuration = GetPrivateFloat(_subscribedCar, "postBoostSlowdownDuration", 0.25f);
    }

    // NEW: zoom out pulse coroutine (unscaled realtime safe)
    private IEnumerator BoostZoomDuringBoostCoroutine(float deltaFOV)
    {
        if (cam == null) yield break;

        _suppressAutoFov = true;

        // sample current FOV and compute target
        float startFOV = cam.fieldOfView;
        float targetOut = Mathf.Clamp(startFOV + deltaFOV, 1f, 179f);

        // ramp in (smooth)
        float rampIn = Mathf.Max(0.01f, boostFovRampIn);
        float t0 = Time.realtimeSinceStartup;
        float t1 = t0 + rampIn;
        while (Time.realtimeSinceStartup < t1)
        {
            float u = Mathf.InverseLerp(t0, t1, Time.realtimeSinceStartup);
            float eased = Mathf.SmoothStep(0f, 1f, u);
            cam.fieldOfView = Mathf.Lerp(startFOV, targetOut, eased);
            yield return null;
        }
        cam.fieldOfView = targetOut;

        // HOLD: wait while the car reports boosting
        while (_carIsBoosting)
            yield return null;

        // Determine restore duration: prefer explicit ramp-out if >0, else use car's postBoostSlowdownDuration
        float restoreDuration = boostFovRampOut > 0f ? boostFovRampOut : Mathf.Max(0.01f, _boostPostDuration);
        float fromFOV = cam.fieldOfView;
        float restoreStart = Time.realtimeSinceStartup;
        float restoreEnd = restoreStart + restoreDuration;

        // smooth restore back to the auto-FOV target (sample current auto target)
        float autoTarget = _speedFovCurrent;
        while (Time.realtimeSinceStartup < restoreEnd)
        {
            float u = Mathf.InverseLerp(restoreStart, restoreEnd, Time.realtimeSinceStartup);
            float eased = Mathf.SmoothStep(0f, 1f, u);
            cam.fieldOfView = Mathf.Lerp(fromFOV, _speedFovCurrent, eased);
            yield return null;
        }

        _speedFovCurrent = cam.fieldOfView;
        _suppressAutoFov = false;
        _boostZoomCR = null;
    }

    // Reflection helper to read private float fields from CarController (safe fallback)
    private float GetPrivateFloat(object obj, string fieldName, float fallback = 0f)
    {
        if (obj == null) return fallback;
        try
        {
            var fi = obj.GetType().GetField(fieldName, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (fi != null && fi.FieldType == typeof(float))
                return (float)fi.GetValue(obj);
        }
        catch { /* ignore reflection errors */ }
        return fallback;
    }

    private void LateUpdate()
    {
        if (target == null) return;

        // Build yaw-only forward
        Vector3 targetForwardFlat = target.forward;
        targetForwardFlat.y = 0f;
        if (targetForwardFlat.sqrMagnitude < 0.0001f)
            targetForwardFlat = Vector3.forward;
        targetForwardFlat.Normalize();

        if (smoothedForward == Vector3.zero)
            smoothedForward = targetForwardFlat;

        smoothedForward = Vector3.Slerp(smoothedForward, targetForwardFlat, rotationLag * Time.deltaTime);
        Quaternion yawOnly = Quaternion.LookRotation(smoothedForward, Vector3.up);

        // Position follow
        Vector3 desiredPos = target.position + yawOnly * offset;
        Vector3 basePos = Vector3.Lerp(transform.position, desiredPos, positionFollowSpeed * Time.deltaTime);

        // Rotation with fixed pitch
        Vector3 e = yawOnly.eulerAngles;
        e.x = cameraPitch;

        // Compute Z roll based on recent turn sharpness + lateral velocity (drift)
        float rollAngle = 0f;
        if (enableZRoll)
        {
            // Yaw-rate estimate (deg/sec) by comparing previous flat forward to current
            float signedYawDelta = Vector3.SignedAngle(_prevTargetForwardFlat, targetForwardFlat, Vector3.up);
            float yawRateDegPerSec = signedYawDelta / Mathf.Max(1e-6f, Time.deltaTime);

            // normalize yawRate
            float yawNorm = Mathf.Clamp(yawRateDegPerSec / zRollYawRateDivisor, -1f, 1f);

            // lateral velocity factor from Rigidbody (if available) to amplify when drifting
            float lateralFactor = 0f;
            if (target != null)
            {
                var rb = target.GetComponent<Rigidbody>();
                if (rb != null)
                {
                    // use target.right (vehicle local lateral) to sample lateral component
                    float lateralVel = Vector3.Dot(rb.velocity, target.right);
                    lateralFactor = Mathf.Clamp01(Mathf.Abs(lateralVel) / lateralVelocityNormalization);
                }
            }

            // combine into roll target (signed)
            float sign = invertZRoll ? -1f : 1f;
            float baseRoll = yawNorm * zRollScale * 180f * sign; // yawNorm * (zRollScale * 180) -> degrees
            float driftAmp = 1f + (driftInfluence * lateralFactor);
            float rollTarget = Mathf.Clamp(baseRoll * driftAmp, -maxRollDegrees, maxRollDegrees);

            // smooth
            _currentZRoll = Mathf.Lerp(_currentZRoll, rollTarget, 1f - Mathf.Exp(-rollSmoothing * Time.deltaTime));
            rollAngle = _currentZRoll;
        }

        // apply roll (Z) into Euler before creating quaternion
        e.z = rollAngle;

        Quaternion desiredRot = Quaternion.Euler(e);
        Quaternion baseRot = Quaternion.Slerp(transform.rotation, desiredRot, rotationFollowSpeed * Time.deltaTime);

        // Shake
        Vector3 shakeOffset = Vector3.zero;
        if (shakeTimer > 0f && shakeDuration > 0f && shakeStrength > 0f)
        {
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
            shakeOffset = (right * osc.x + up * osc.y) * (shakeStrength * amplitude);
        }
        else
        {
            shakeTimer = 0f;
        }

        transform.position = basePos + shakeOffset;
        transform.rotation = baseRot;

        // Manual animation step
        UpdateFOV(Time.deltaTime);

        // ★ Auto speed FOV (only when not manually animating) and not suppressed by ZoomPulse
        if (useSpeedBasedFOV && !_fovAnimating && cam != null && car != null)
        {
            float speed = car.CurrentSpeed;
            float norm = Mathf.InverseLerp(fovSpeedMin, fovSpeedMax, speed);
            float target = Mathf.Lerp(fovAtMinSpeed, fovAtMaxSpeed, norm);

            // ALWAYS keep the baseline up to date
            _speedFovCurrent = Mathf.Lerp(_speedFovCurrent, target, speedFovSmooth * Time.deltaTime);

            // Only APPLY it when we are not suppressed by peek/boost pulses
            if (!_suppressAutoFov)
                cam.fieldOfView = _speedFovCurrent;
        }

        // record previous forward for next frame yaw-rate estimate
        _prevTargetForwardFlat = targetForwardFlat;

        HandleMapPeekInput();
    }

    public void SetTarget(Transform t)
    {
        target = t;
        smoothedForward = Vector3.zero;
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

        if (Input.GetKeyDown(fovIncreaseKey))
        {
            _mapPeekHeld = true;

            if (_mapPeekCR != null) StopCoroutine(_mapPeekCR);

            // Freeze APPLY (baseline still updates in LateUpdate)
            _suppressAutoFov = true;

            // Baseline decides the TARGET (prevents stacking)
            _mapPeekPressBaseline = GetBaselineFov();
            _mapPeekPressTarget = Mathf.Min(mapPeekMaxFOV, _mapPeekPressBaseline * mapPeekMultiplier);

            // Start from CURRENT FOV (prevents ugly snap when re-pressing mid-return)
            float from = cam.fieldOfView;

            _mapPeekCR = StartCoroutine(MapPeekHoldCoroutine(
                fromFOV: from,
                toFOV: _mapPeekPressTarget,
                rampIn: Mathf.Max(0.01f, mapPeekRampIn)
            ));
        }

        if (Input.GetKeyUp(fovIncreaseKey))
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
        _boostZoomCR = null;
    }

    private float GetBaselineFov()
    {
        // If speed-FOV is on, baseline is the current speed-smoothed value.
        // Otherwise baseline is the configured default.
        return (useSpeedBasedFOV ? _speedFovCurrent : defaultFOV);
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