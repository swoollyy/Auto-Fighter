using UnityEngine;

/// <summary>
/// Ramp/airborne and landing carry-speed settings. CarController reads these at Start.
/// </summary>
public class CarLandingConfig : MonoBehaviour
{
    [Header("Ramp / Airborne Speed Preservation")]
    [SerializeField] private bool skipSpeedClampWhileAirborne = true;
    [SerializeField] private bool enableLandingCarrySpeed = true;
    [SerializeField, Min(0f)] private float landingExcessBleedPerSecond = 7.17f;
    [SerializeField, Min(0f)] private float landingNoClampGraceSeconds = 0.08f;

    [Header("Landing Boost (Ramp / Airborne)")]
    [Tooltip("When landing after being airborne, temporarily raise speed cap toward takeoff speed to reduce speed loss.")]
    [SerializeField] private bool enableLandingBoost = true;
    [Tooltip("Multiplier on takeoff speed for the landing boost cap. 1 = full mirror, 0.8 = 80% of takeoff speed.")]
    [SerializeField, Min(0f)] private float landingBoostStrength = 1f;
    [Tooltip("How long (seconds) the landing boost lasts before fully decaying.")]
    [SerializeField, Min(0.01f)] private float landingBoostDuration = 1.2f;
    [Tooltip("Falloff curve: 1 = linear decay, >1 = drops off faster at end, <1 = longer tail.")]
    [SerializeField, Min(0.1f)] private float landingBoostFalloff = 1.5f;

    [Header("Bad Landing Crash")]
    [Tooltip("Crash (with damage) when landing tilted — on side, roof, nose, or tail instead of flat on wheels.")]
    [SerializeField] private bool enableBadLandingCrash = true;
    [Tooltip("Must be airborne at least this long before a bad landing can trigger (ignores tiny hops).")]
    [SerializeField, Min(0f)] private float badLandingMinAirborneSeconds = 0.2f;
    [Tooltip("Car up must dot ground normal at least this much for a safe landing (1 = perfectly flat on the surface).")]
    [SerializeField, Range(0f, 1f)] private float badLandingUpAlignDotMin = 0.88f;
    [Tooltip("If nose/tail alignment with the ground normal exceeds this, treat as a bad landing (buried nose/tail).")]
    [SerializeField, Range(0f, 1f)] private float badLandingForwardNormalDotMax = 0.72f;
    [SerializeField, Range(0f, 1f)] private float badLandingCrashSeverity = 0.42f;
    [Tooltip("Horizontal speed kept after a bad-landing crash (no launch impulse).")]
    [SerializeField, Range(0f, 1f)] private float badLandingSpeedRetain = 0.32f;
    [SerializeField, Range(0f, 1f)] private float badLandingVerticalSpeedRetain = 0.08f;
    [Tooltip("How strongly the car tumbles to settle after a bad landing (physics, not frozen rotation).")]
    [SerializeField, Min(0f)] private float badLandingTumbleTorque = 5.5f;
    [Tooltip("Keeps trick spin through impact, scaled by misalignment.")]
    [SerializeField, Range(0f, 1f)] private float badLandingAngularSpeedRetain = 0.82f;
    [SerializeField, Min(0.5f)] private float badLandingMaxAngularSpeed = 10f;

    public bool SkipSpeedClampWhileAirborne => skipSpeedClampWhileAirborne;
    public bool EnableLandingCarrySpeed => enableLandingCarrySpeed;
    public float LandingExcessBleedPerSecond => landingExcessBleedPerSecond;
    public float LandingNoClampGraceSeconds => landingNoClampGraceSeconds;
    public bool EnableLandingBoost => enableLandingBoost;
    public float LandingBoostStrength => landingBoostStrength;
    public float LandingBoostDuration => landingBoostDuration;
    public float LandingBoostFalloff => landingBoostFalloff;
    public bool EnableBadLandingCrash => enableBadLandingCrash;
    public float BadLandingMinAirborneSeconds => badLandingMinAirborneSeconds;
    public float BadLandingUpAlignDotMin => badLandingUpAlignDotMin;
    public float BadLandingForwardNormalDotMax => badLandingForwardNormalDotMax;
    public float BadLandingCrashSeverity => badLandingCrashSeverity;
    public float BadLandingSpeedRetain => badLandingSpeedRetain;
    public float BadLandingVerticalSpeedRetain => badLandingVerticalSpeedRetain;
    public float BadLandingTumbleTorque => badLandingTumbleTorque;
    public float BadLandingAngularSpeedRetain => badLandingAngularSpeedRetain;
    public float BadLandingMaxAngularSpeed => badLandingMaxAngularSpeed;
}
