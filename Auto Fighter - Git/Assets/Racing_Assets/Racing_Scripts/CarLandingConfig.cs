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

    public bool SkipSpeedClampWhileAirborne => skipSpeedClampWhileAirborne;
    public bool EnableLandingCarrySpeed => enableLandingCarrySpeed;
    public float LandingExcessBleedPerSecond => landingExcessBleedPerSecond;
    public float LandingNoClampGraceSeconds => landingNoClampGraceSeconds;
    public bool EnableLandingBoost => enableLandingBoost;
    public float LandingBoostStrength => landingBoostStrength;
    public float LandingBoostDuration => landingBoostDuration;
    public float LandingBoostFalloff => landingBoostFalloff;
}
