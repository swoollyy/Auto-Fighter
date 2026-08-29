using UnityEngine;

/// <summary>
/// Airborne trick tuning (flips, upright spins, barrel rolls). CarController reads these at Awake.
/// Hold right stick (or arrow keys) while airborne to flip/spin; hold left bumper for barrel rolls.
/// Left stick still yaws in the air at a reduced turn rate vs ground.
/// </summary>
public class CarAirTrickConfig : MonoBehaviour
{
    [Header("Enable")]
    [SerializeField] private bool enableAirTricks = true;

    [Header("Air Turn (left stick)")]
    [Tooltip("Air yaw as a fraction of current ground turn speed (0.6 = 60% of ground feel).")]
    [SerializeField, Range(0.05f, 1.5f)] private float airTurnSpeedMultiplier = 0.6f;
    [Tooltip("While drifting in air: how much of the ground drift steer sharpening applies (0 = cruise air turn only, 1 = full drift steer mul).")]
    [SerializeField, Range(0f, 1f)] private float airDriftSteerFeel = 0.72f;
    [Tooltip("While drifting in air: fraction of ground drift side-force applied horizontally (keeps the slide feel without full ground physics).")]
    [SerializeField, Range(0f, 1f)] private float airDriftSideForceScale = 0.55f;
    [Tooltip("Legacy absolute air yaw rate (deg/s) used by older air-aim path.")]
    [SerializeField] private float airYawRate = 110f;
    [SerializeField, Range(0f, 1f)] private float airYawInputDeadzone = 0.12f;
    [Tooltip("How strongly horizontal travel follows the flat nose while turning/drifting in air. 0 = visual yaw only (ballistic path). 1 = travel snaps hard toward the nose. Off while tricking.")]
    [SerializeField, Range(0f, 1f)] private float airVelocityFollowNose = 0.45f;
    [Tooltip("How quickly travel catches the nose at full Air Velocity Follow Nose (higher = snappier).")]
    [SerializeField, Min(0.1f)] private float airVelocityFollowRate = 3.5f;
    [Tooltip("After releasing tricks, how long to ease nose-follow travel back in so the path doesn't snap onto the nose. Lower = nose control returns sooner.")]
    [SerializeField, Min(0f)] private float airVelocityFollowReturnSeconds = 0.18f;
    [Tooltip("Legacy: bank path from stick at this deg/s. Prefer Air Velocity Follow Nose for nose-tracking. 0 = off.")]
    [SerializeField, Min(0f)] private float airTrajectorySteerRate = 0f;
    [Tooltip("How quickly left-stick air aim ramps in (lower = softer).")]
    [SerializeField, Min(0.5f)] private float airSteerInputSmoothRate = 3.5f;
    [Tooltip("How quickly air aim eases off when the stick returns to center.")]
    [SerializeField, Min(0.5f)] private float airSteerInputReleaseRate = 4.5f;
    [Tooltip("How quickly flight-path banking builds toward the target rate (deg/s²).")]
    [SerializeField, Min(10f)] private float airTrajectoryBankAccel = 90f;

    [Header("Air Aim While Tricking")]
    [Tooltip("Allow left-stick yaw and trajectory steering while the trick stick is held.")]
    [SerializeField] private bool enableAirAimWhileTricking = true;
    [Tooltip("Left-stick yaw strength during pitch-heavy tricks (flips).")]
    [SerializeField, Range(0f, 1.5f)] private float airAimWhileTrickingYawMult = 0.75f;
    [Tooltip("Left-stick yaw strength during disc spin / barrel roll (right stick X).")]
    [SerializeField, Range(0f, 1f)] private float airAimHorizSpinYawMult = 0.15f;
    [Tooltip("Trajectory steer strength during flips.")]
    [SerializeField, Range(0f, 1.5f)] private float airTrajectoryWhileTrickingMult = 1f;
    [Tooltip("Trajectory steer strength during disc spin / barrel roll.")]
    [SerializeField, Range(0f, 1f)] private float airTrajectoryHorizSpinMult = 0.55f;

    [Header("Trick Mode (right stick in air)")]
    [Tooltip("Pitch rate for front/back flips (right stick Y or arrow keys).")]
    [SerializeField] private float trickPitchRate = 195f;
    [Tooltip("Yaw spin rate while upright (right stick X, no barrel modifier).")]
    [SerializeField] private float trickYawSpinRate = 220f;
    [Tooltip("Roll rate for barrel rolls (right stick X + left bumper).")]
    [SerializeField] private float trickRollRate = 220f;
    [Tooltip("How quickly disc spin cross-fades into barrel roll while the barrel button is held (0–1 per second).")]
    [SerializeField, Min(0.35f)] private float barrelModeBlendSpeed = 2.2f;
    [Tooltip("How quickly barrel roll cross-fades back to disc spin when the barrel button is released (0–1 per second). Higher = snappier dump of leftover roll.")]
    [SerializeField, Min(0.35f)] private float barrelModeReleaseSpeed = 8f;
    [SerializeField, Range(0f, 1f)] private float trickInputDeadzone = 0.15f;
    [Tooltip("How quickly trick stick input ramps in (higher = snappier, lower = softer).")]
    [SerializeField, Min(0.5f)] private float trickInputSmoothRate = 11f;
    [Tooltip("How quickly trick input returns to neutral when released.")]
    [SerializeField, Min(0.5f)] private float trickInputReleaseRate = 15f;
    [Tooltip("Degrees per second² — how fast rotation speed builds toward the target rate.")]
    [SerializeField, Min(10f)] private float trickRotationAccel = 340f;
    [Tooltip("Degrees per second² — how fast rotation coasts down when input eases off.")]
    [SerializeField, Min(10f)] private float trickRotationDecel = 240f;
    [Tooltip("While trick stick is held in the air — blocks auto-upright so flips are not fought.")]
    [SerializeField] private bool suppressRampAlignmentInTrickMode = true;
                    
    [Header("Air Upright Recovery (trick stick released)")]
    [Tooltip("When airborne and not tricking, slowly align wheels toward the landing surface.")]
    [SerializeField] private bool enableAirUprightRecovery = true;
    [SerializeField, Min(0f)] private float airUprightRecoverSpeed = 2.75f;
    [Tooltip("Seconds to wait after releasing the trick stick (and residual spin) before upright recovery starts.")]
    [SerializeField, Min(0f)] private float airUprightRecoverDelay = 0.15f;
    [Tooltip("Seconds to ease upright recovery from 0 to full after the delay. Lower = snappier straighten.")]
    [SerializeField, Min(0.05f)] private float airUprightRecoverBlendInSeconds = 0.7f;
    [Tooltip("Extra align speed added when close to the ground (scaled by proximity²).")]
    [SerializeField, Min(0f)] private float airUprightNearGroundBoost = 16f;
    [Tooltip("Skip recovery when car up already aligns this closely with the target surface.")]
    [SerializeField, Range(0.85f, 1f)] private float airUprightMinAlignDot = 0.992f;

    [Header("Air Gravity")]
    [Tooltip("Multiplier on Physics.gravity while airborne. 1 = default; >1 = heavier / less floaty.")]
    [SerializeField, Min(0.1f)] private float airGravityMultiplier = 1.12f;

    public bool EnableAirTricks => enableAirTricks;
    public float AirTurnSpeedMultiplier => airTurnSpeedMultiplier;
    public float AirDriftSteerFeel => airDriftSteerFeel;
    public float AirDriftSideForceScale => airDriftSideForceScale;
    public float AirYawRate => airYawRate;
    public float AirYawInputDeadzone => airYawInputDeadzone;
    public float AirVelocityFollowNose => airVelocityFollowNose;
    public float AirVelocityFollowRate => airVelocityFollowRate;
    public float AirVelocityFollowReturnSeconds => airVelocityFollowReturnSeconds;
    public float AirTrajectorySteerRate => airTrajectorySteerRate;
    public float AirSteerInputSmoothRate => airSteerInputSmoothRate;
    public float AirSteerInputReleaseRate => airSteerInputReleaseRate;
    public float AirTrajectoryBankAccel => airTrajectoryBankAccel;
    public bool EnableAirAimWhileTricking => enableAirAimWhileTricking;
    public float AirAimWhileTrickingYawMult => airAimWhileTrickingYawMult;
    public float AirAimHorizSpinYawMult => airAimHorizSpinYawMult;
    public float AirTrajectoryWhileTrickingMult => airTrajectoryWhileTrickingMult;
    public float AirTrajectoryHorizSpinMult => airTrajectoryHorizSpinMult;
    public float TrickPitchRate => trickPitchRate;
    public float TrickYawSpinRate => trickYawSpinRate;
    public float TrickRollRate => trickRollRate;
    public float BarrelModeBlendSpeed => barrelModeBlendSpeed;
    public float BarrelModeReleaseSpeed => barrelModeReleaseSpeed;
    public float TrickInputDeadzone => trickInputDeadzone;
    public float TrickInputSmoothRate => trickInputSmoothRate;
    public float TrickInputReleaseRate => trickInputReleaseRate;
    public float TrickRotationAccel => trickRotationAccel;
    public float TrickRotationDecel => trickRotationDecel;
    public bool SuppressRampAlignmentInTrickMode => suppressRampAlignmentInTrickMode;
    public bool EnableAirUprightRecovery => enableAirUprightRecovery;
    public float AirUprightRecoverSpeed => airUprightRecoverSpeed;
    public float AirUprightRecoverDelay => airUprightRecoverDelay;
    public float AirUprightRecoverBlendInSeconds => airUprightRecoverBlendInSeconds;
    public float AirUprightNearGroundBoost => airUprightNearGroundBoost;
    public float AirUprightMinAlignDot => airUprightMinAlignDot;
    public float AirGravityMultiplier => airGravityMultiplier;
}
