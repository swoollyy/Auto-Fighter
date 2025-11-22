using UnityEngine;

public class CameraFollow : MonoBehaviour
{
    [Header("Follow Target")]
    [SerializeField] public Transform target;

    [Header("Camera Settings")]
    [SerializeField] private Vector3 offset = new Vector3(0, 8, -12);
    [SerializeField, Range(5f, 45f)] private float cameraPitch = 20f;

    [Header("Smoothing")]
    [SerializeField] private float positionFollowSpeed = 8f;
    [SerializeField] private float rotationFollowSpeed = 8f;

    [Tooltip("How quickly the *camera's forward* catches up to the car's forward.\nLower = more lag, more looseness.")]
    [SerializeField] private float rotationLag = 4f;

    // Internal smoothed direction so camera doesn't hard-lock to target.forward
    private Vector3 smoothedForward = Vector3.zero;

    // ─────────────────────────────────────────────
    // Screen shake state
    // ─────────────────────────────────────────────
    private float shakeTimer = 0f;
    private float shakeDuration = 0f;
    private float shakeStrength = 0f;
    private int shakeVibrato = 10;
    private float shakeRandomness = 0f;
    private float shakeSeed; // for deterministic-ish noise per shake

    private void LateUpdate()
    {
        if (target == null) return;

        // -------------------------
        // BUILD FLAT (YAW-ONLY) FORWARD
        // -------------------------
        Vector3 targetForwardFlat = target.forward;
        targetForwardFlat.y = 0f;

        if (targetForwardFlat.sqrMagnitude < 0.0001f)
        {
            targetForwardFlat = Vector3.forward; // safe fallback
        }
        targetForwardFlat.Normalize();

        // Initialize smoothedForward once
        if (smoothedForward == Vector3.zero)
        {
            smoothedForward = targetForwardFlat;
        }

        // This is the "rubber band" part: camera forward slowly catches up
        smoothedForward = Vector3.Slerp(
            smoothedForward,
            targetForwardFlat,
            rotationLag * Time.deltaTime
        );

        // Build yaw-only rotation from smoothed direction
        Quaternion yawOnly = Quaternion.LookRotation(smoothedForward, Vector3.up);

        // -------------------------
        // POSITION FOLLOW (YAW-ONLY)
        // -------------------------
        // Use yawOnly instead of target.TransformDirection so we IGNORE car pitch/roll
        Vector3 desiredPos = target.position + yawOnly * offset;

        Vector3 basePos = Vector3.Lerp(
            transform.position,
            desiredPos,
            positionFollowSpeed * Time.deltaTime
        );

        // -------------------------
        // ROTATION WITH FIXED PITCH
        // -------------------------
        Vector3 e = yawOnly.eulerAngles;
        e.x = cameraPitch; // fixed downward tilt

        Quaternion desiredRot = Quaternion.Euler(e);

        Quaternion baseRot = Quaternion.Slerp(
            transform.rotation,
            desiredRot,
            rotationFollowSpeed * Time.deltaTime
        );

        // -------------------------
        // APPLY SCREEN SHAKE (POSITIONAL)
        // -------------------------
        Vector3 shakeOffset = Vector3.zero;

        if (shakeTimer > 0f && shakeDuration > 0f && shakeStrength > 0f)
        {
            shakeTimer -= Time.deltaTime;
            float remaining = Mathf.Max(0f, shakeTimer);
            float elapsed = Mathf.Clamp(shakeDuration - remaining, 0f, shakeDuration);

            float t = shakeDuration > 0f ? (elapsed / shakeDuration) : 1f;
            // amplitude fades out from 1 -> 0 over time (ease-out)
            float amplitude = 1f - t;
            amplitude *= amplitude; // ease-out^2 for a nicer falloff

            // Vibrato is how many "wiggles" per second
            float frequency = Mathf.Max(1, shakeVibrato);
            float angle = (elapsed + shakeSeed) * frequency * Mathf.PI * 2f;

            // Base directional oscillation
            Vector2 osc = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));

            // Randomness factor: jitter direction a bit
            float randFactor = shakeRandomness / 180f; // 0..1-ish
            if (randFactor > 0f)
            {
                // pseudo-stable random using seed + elapsed
                float r1 = Mathf.PerlinNoise(shakeSeed, elapsed * 10f) * 2f - 1f;
                float r2 = Mathf.PerlinNoise(shakeSeed + 37.1f, elapsed * 10f) * 2f - 1f;
                Vector2 rand = new Vector2(r1, r2) * randFactor;
                osc += rand;
            }

            if (osc.sqrMagnitude > 0.0001f)
                osc.Normalize();

            // Convert 2D shake into world-space offset along camera's right/up
            Vector3 right = baseRot * Vector3.right;
            Vector3 up = baseRot * Vector3.up;

            shakeOffset =
                (right * osc.x + up * osc.y) *
                (shakeStrength * amplitude);
        }
        else
        {
            shakeTimer = 0f; // ensure we don't go negative
        }

        transform.position = basePos + shakeOffset;
        transform.rotation = baseRot;
    }

    public void SetTarget(Transform t)
    {
        target = t;
        smoothedForward = Vector3.zero; // re-init on new target
    }

    /// <summary>
    /// Starts a camera shake.
    /// duration: how long the shake lasts (seconds).
    /// strength: max positional offset magnitude.
    /// vibrato: how many "wiggles" per second (frequency).
    /// randomness: how much to randomize direction (0 = clean sine wave).
    /// </summary>
    public void StartShake(float duration, float strength, int vibrato, float randomness)
    {
        shakeDuration = Mathf.Max(0f, duration);
        shakeTimer = shakeDuration;
        shakeStrength = Mathf.Max(0f, strength);
        shakeVibrato = Mathf.Max(1, vibrato);
        shakeRandomness = Mathf.Max(0f, randomness);

        // new random seed each time so shakes look different
        shakeSeed = Random.value * 1000f;
    }
}
