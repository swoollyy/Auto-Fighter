using UnityEngine;

/// <summary>
/// Airborne trick tuning (flips, upright spins, barrel rolls). CarController reads these at Awake.
/// Hold left bumper while airborne to enter trick mode; hold left trigger + bumper for barrel rolls.
/// </summary>
public class CarAirTrickConfig : MonoBehaviour
{
    [Header("Enable")]
    [SerializeField] private bool enableAirTricks = true;

    [Header("Free Air Control (no bumper)")]
    [Tooltip("Yaw rate while airborne without trick mode — steer to aim the car toward the road.")]
    [SerializeField] private float airYawRate = 110f;
    [SerializeField, Range(0f, 1f)] private float airYawInputDeadzone = 0.12f;

    [Header("Trick Mode (left bumper held)")]
    [Tooltip("Pitch rate for front/back flips (stick Y or W/S).")]
    [SerializeField] private float trickPitchRate = 195f;
    [Tooltip("Yaw spin rate while upright (stick X, bumper only — spinning on a disc).")]
    [SerializeField] private float trickYawSpinRate = 220f;
    [Tooltip("Roll rate for barrel rolls (stick X + left trigger + bumper).")]
    [SerializeField] private float trickRollRate = 220f;
    [SerializeField, Range(0f, 1f)] private float trickInputDeadzone = 0.15f;
    [Tooltip("How quickly trick stick input ramps in (higher = snappier, lower = softer).")]
    [SerializeField, Min(0.5f)] private float trickInputSmoothRate = 11f;
    [Tooltip("How quickly trick input returns to neutral when released.")]
    [SerializeField, Min(0.5f)] private float trickInputReleaseRate = 15f;
    [Tooltip("Degrees per second² — how fast rotation speed builds toward the target rate.")]
    [SerializeField, Min(10f)] private float trickRotationAccel = 340f;
    [Tooltip("Degrees per second² — how fast rotation coasts down when input eases off.")]
    [SerializeField, Min(10f)] private float trickRotationDecel = 240f;
    [Tooltip("While holding trick bumper only — blocks auto-upright so flips are not fought.")]
    [SerializeField] private bool suppressRampAlignmentInTrickMode = true;

    [Header("Air Upright Recovery (bumper released)")]
    [Tooltip("When airborne and not tricking, slowly align wheels toward the landing surface.")]
    [SerializeField] private bool enableAirUprightRecovery = true;
    [SerializeField, Min(0f)] private float airUprightRecoverSpeed = 2.75f;
    [Tooltip("Extra align speed added when close to the ground (scaled by proximity²).")]
    [SerializeField, Min(0f)] private float airUprightNearGroundBoost = 16f;
    [Tooltip("Skip recovery when car up already aligns this closely with the target surface.")]
    [SerializeField, Range(0.85f, 1f)] private float airUprightMinAlignDot = 0.992f;

    public bool EnableAirTricks => enableAirTricks;
    public float AirYawRate => airYawRate;
    public float AirYawInputDeadzone => airYawInputDeadzone;
    public float TrickPitchRate => trickPitchRate;
    public float TrickYawSpinRate => trickYawSpinRate;
    public float TrickRollRate => trickRollRate;
    public float TrickInputDeadzone => trickInputDeadzone;
    public float TrickInputSmoothRate => trickInputSmoothRate;
    public float TrickInputReleaseRate => trickInputReleaseRate;
    public float TrickRotationAccel => trickRotationAccel;
    public float TrickRotationDecel => trickRotationDecel;
    public bool SuppressRampAlignmentInTrickMode => suppressRampAlignmentInTrickMode;
    public bool EnableAirUprightRecovery => enableAirUprightRecovery;
    public float AirUprightRecoverSpeed => airUprightRecoverSpeed;
    public float AirUprightNearGroundBoost => airUprightNearGroundBoost;
    public float AirUprightMinAlignDot => airUprightMinAlignDot;
}
