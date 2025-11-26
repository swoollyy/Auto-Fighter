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

    // FOV animation state
    private float _startFOV;
    private float _targetFOV;
    private float _fovLerpT;
    private float _fovLerpDuration;
    private bool _fovAnimating;

    // ★ runtime speed-FOV target
    private float _speedFovCurrent; // smoothed applied value

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

        _speedFovCurrent = defaultFOV;
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

        // ★ Auto speed FOV (only when not manually animating)
        if (useSpeedBasedFOV && !_fovAnimating && cam != null && car != null)
        {
            float speed = car.CurrentSpeed;
            float norm = Mathf.InverseLerp(fovSpeedMin, fovSpeedMax, speed);
            float target = Mathf.Lerp(fovAtMinSpeed, fovAtMaxSpeed, norm);

            // Smooth approach
            _speedFovCurrent = Mathf.Lerp(_speedFovCurrent, target, speedFovSmooth * Time.deltaTime);
            cam.fieldOfView = _speedFovCurrent;
        }
    }

    public void SetTarget(Transform t)
    {
        target = t;
        smoothedForward = Vector3.zero;
        if (car == null && t != null)
            car = t.GetComponent<CarController>() ?? t.GetComponentInParent<CarController>();
    }

    public void StartShake(float duration, float strength, int vibrato, float randomness)
    {
        shakeDuration = Mathf.Max(0f, duration);
        shakeTimer = shakeDuration;
        shakeStrength = Mathf.Max(0f, strength);
        shakeVibrato = Mathf.Max(1, vibrato);
        shakeRandomness = Mathf.Max(0f, randomness);
        shakeSeed = Random.value * 1000f;
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

    public void ResetFieldOfView(float duration = 0f)
    {
        SetFieldOfView(defaultFOV, duration);
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