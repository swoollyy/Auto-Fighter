using UnityEngine;

/// <summary>
/// Base movement, coast, brake, and steer-traction settings. CarController reads these at Start and applies upgrades via its existing pipeline.
/// </summary>
public class CarMovementConfig : MonoBehaviour
{
    [Header("TEST — Always Accelerate (easy revert)")]
    [Tooltip("ON: always throttle forward when not drifting (start + after crash), no coast. While drifting, real accelerate input picks hard vs soft drift. Brake/reverse still work. OFF: normal movement.")]
    [SerializeField] private bool testAlwaysAccelerate = false;

    [Header("Base Movement (on Default surface)")]
    [SerializeField] private float baseAcceleration = 5.2f;
    [SerializeField] private float baseMaxSpeed = 3.95f;
    [Tooltip("Extra brake decel (m/s²) added on top of Max Brake Decel × Brake Forward Factor.")]
    [SerializeField] private float baseBrakingForce = 0.003f;

    [Header("Base Physics")]
    [SerializeField] private float baseDrag = 0.13f;

    [Header("Arcade Coasting")]
    [SerializeField] private float coastLowDecelPerSecond = 0.39f;
    [SerializeField] private float coastHighDecelPerSecond = 5.55f;
    [SerializeField] private float coastHighSpeedFraction = 1f;
    [SerializeField] private bool useExponentialCoast = false;
    [SerializeField] private float coastDampingPerSecond = 4.48f;

    [Header("Arcade Movement Tuning")]
    [SerializeField] private float coastDecelFactor = 0.74f;
    [SerializeField] private float brakeForwardFactor = 0.7f;
    [SerializeField] private float reverseAccelFactor = 1.06f;
    [SerializeField] private float brakeToReverseSpeed = 0.5f;
    [SerializeField] private float maxBrakeDecelPerSecond = 1f;
    [SerializeField] private float maxReverseAccelPerSecond = 5.06f;

    [Header("Slope / ramp assist")]
    [Tooltip("Extra acceleration along the ground surface when driving uphill (smoothly ramped; helps steep ramps without changing tilt).")]
    [SerializeField] private bool enableSlopeDriveAssist = true;
    [Tooltip("Peak extra accel (m/s² style, ForceMode.Acceleration) at max steepness + straight climb.")]
    [SerializeField] private float slopeDriveAssistMaxAccel = 7.5f;
    [Tooltip("Below this ground tilt (degrees from flat), assist stays off.")]
    [SerializeField] private float slopeDriveAssistMinAngle = 10f;
    [Tooltip("At this tilt and steeper, steepness factor reaches 1.")]
    [SerializeField] private float slopeDriveAssistMaxAngle = 52f;
    [Tooltip("How fast assist ramps up when you start climbing (higher = snappier).")]
    [SerializeField] private float slopeDriveAssistRiseSpeed = 32f;
    [Tooltip("How fast assist falls off when you crest or release throttle (higher = less lingering).")]
    [SerializeField] private float slopeDriveAssistFallSpeed = 48f;
    [Tooltip("If true, skip slope assist on flat boost pads only. Steep boost/ramps still get climb assist (flat pads already fail the min-angle check).")]
    [SerializeField] private bool slopeDriveAssistDisableOnBoost = false;
    [Tooltip("If true, no slope assist on ice.")]
    [SerializeField] private bool slopeDriveAssistDisableOnIce = true;

    public bool TestAlwaysAccelerate => testAlwaysAccelerate;
    public float BaseAcceleration => baseAcceleration;
    public float BaseMaxSpeed => baseMaxSpeed;
    public float BaseBrakingForce => baseBrakingForce;
    public float BaseDrag => baseDrag;
    public float CoastLowDecelPerSecond => coastLowDecelPerSecond;
    public float CoastHighDecelPerSecond => coastHighDecelPerSecond;
    public float CoastHighSpeedFraction => coastHighSpeedFraction;
    public bool UseExponentialCoast => useExponentialCoast;
    public float CoastDampingPerSecond => coastDampingPerSecond;
    public float CoastDecelFactor => coastDecelFactor;
    public float BrakeForwardFactor => brakeForwardFactor;
    public float ReverseAccelFactor => reverseAccelFactor;
    public float BrakeToReverseSpeed => brakeToReverseSpeed;
    public float MaxBrakeDecelPerSecond => maxBrakeDecelPerSecond;
    public float MaxReverseAccelPerSecond => maxReverseAccelPerSecond;

    public bool EnableSlopeDriveAssist => enableSlopeDriveAssist;
    public float SlopeDriveAssistMaxAccel => slopeDriveAssistMaxAccel;
    public float SlopeDriveAssistMinAngle => slopeDriveAssistMinAngle;
    public float SlopeDriveAssistMaxAngle => slopeDriveAssistMaxAngle;
    public float SlopeDriveAssistRiseSpeed => slopeDriveAssistRiseSpeed;
    public float SlopeDriveAssistFallSpeed => slopeDriveAssistFallSpeed;
    public bool SlopeDriveAssistDisableOnBoost => slopeDriveAssistDisableOnBoost;
    public bool SlopeDriveAssistDisableOnIce => slopeDriveAssistDisableOnIce;
}
