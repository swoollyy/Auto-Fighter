using UnityEngine;

[DisallowMultipleComponent]
public class TireTrailController : MonoBehaviour
{
    [Header("Trail Renderers (children at wheel positions)")]
    [SerializeField] private TrailRenderer leftTrail;
    [SerializeField] private TrailRenderer rightTrail;

    [Header("Speed → Trail Settings")]
    [SerializeField] private float minSpeedForTrails = 2f;     // below this = no trails
    [SerializeField] private float maxSpeedForFullTrails = 20f; // above this = max trail length
    [SerializeField] private float minTrailTime = 0.2f;         // short streaks at low speed
    [SerializeField] private float maxTrailTime = 1.5f;         // long streaks at high speed

    [Header("Surface-based Look (optional)")]
    [SerializeField] private Material roadTrailMaterial;
    [SerializeField] private Material offroadTrailMaterial;
    [SerializeField, Range(0f, 1f)]
    private float offroadThreshold = 0.5f; // > this = mostly grass/dirt/etc

    private CarController car;

    private void Awake()
    {
        // CarController can be on this object or parent
        car = GetComponent<CarController>();
        if (car == null)
            car = GetComponentInParent<CarController>();
    }

    private void Start()
    {
        // Make absolutely sure width never changes at runtime:
        if (leftTrail != null) leftTrail.autodestruct = false;
        if (rightTrail != null) rightTrail.autodestruct = false;
    }

    private void LateUpdate()
    {
        if (car == null || leftTrail == null || rightTrail == null)
            return;

        float speed = car.CurrentSpeed;

        // ---------- EMISSION ----------
        bool shouldEmit = speed > minSpeedForTrails;
        leftTrail.emitting = shouldEmit;
        rightTrail.emitting = shouldEmit;

        if (!shouldEmit)
            return; // keep existing trails as-is, just stop adding new ones

        // ---------- TRAIL LENGTH (time) ----------
        // Map speed → [0,1]
        float t = Mathf.InverseLerp(minSpeedForTrails, maxSpeedForFullTrails, speed);
        float trailTime = Mathf.Lerp(minTrailTime, maxTrailTime, t);

        // This only affects *new* segments' lifetime, doesn't resize old ones
        leftTrail.time = trailTime;
        rightTrail.time = trailTime;

        // ---------- SURFACE MATERIAL ----------
        if (roadTrailMaterial != null && offroadTrailMaterial != null)
        {
            bool offroad = car.OffDefaultFraction > offroadThreshold;
            Material targetMat = offroad ? offroadTrailMaterial : roadTrailMaterial;

            if (leftTrail.sharedMaterial != targetMat)
            {
                leftTrail.sharedMaterial = targetMat;
                rightTrail.sharedMaterial = targetMat;
            }
        }
    }
}
