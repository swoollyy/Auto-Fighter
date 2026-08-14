using UnityEngine;

/// <summary>
/// Drift, close-call, and ice surface transition settings. CarController reads these at Start.
/// </summary>
public class CarDriftConfig : MonoBehaviour
{
    [Header("Drift Unlock")]
    [SerializeField] private bool requireDriftUnlock = true;

    [Header("Drift (Arcade)")]
    [SerializeField] private KeyCode driftKey = KeyCode.LeftShift;
    [SerializeField] private float driftMinSpeed = 2.15f;
    [SerializeField] private float maxDriftSteerMultiplier = 3.1f;
    [Tooltip("While drifting, multiplies steering-input smoothing speed. Below 1 = slower buildup to full steer than normal driving; above 1 = faster.")]
    [SerializeField, Range(0.05f, 2f)] private float driftSteeringInputBuildupMultiplier = 0.55f;
    [SerializeField] private float driftBuildRate = 0.7f;
    [SerializeField] private float driftReleaseRate = 12.2f;
    [SerializeField] private float driftSideForce = 0.41f;
    [Tooltip("How much of Drift Side Force remains while accelerating in a drift (0 = old hard cut / grip turn, 1 = full slide even on throttle).")]
    [SerializeField, Range(0f, 1f)] private float driftSideForceWhileAccelerating = 0.35f;
    [Tooltip("How fast side-force blends between throttle-on and throttle-off (higher = snappier).")]
    [SerializeField, Min(0.1f)] private float driftSideForceThrottleBlendRate = 4f;
    [SerializeField] private float driftSpeedDecayPerSecond = 0.06f;
    [SerializeField] private float driftHeldSpeedDecayPerSecond = 0f;
    [SerializeField] private float driftForwardAccelMultiplier = 0f;
    [SerializeField] private bool useFullAccelWhileDrifting = true;
    [SerializeField] private bool lockToDriftPeakSpeed = true;

    [Header("Drift Braking")]
    [SerializeField] private float driftBrakeDecayPerSecond = 0.001f;

    [Header("Drift Charge")]
    [Tooltip("If true, drift charge only builds while steering. Leave false to keep charging while holding drift with stick released (drift-held boost).")]
    [SerializeField] private bool requireDirectionalInputForDriftCharge = false;
    [SerializeField] private float driftNeutralDrainRate = 2.6f;
    [SerializeField] private float driftNeutralFullResetDelay = 3.65f;
    [SerializeField] private bool resetDriftChargeOnSteerFlip = true;
    [SerializeField] private float steerFlipRetainedCharge = 0.055f;
    [SerializeField] private float steerFlipThreshold = 0.15f;
    [SerializeField] private float minChargeForFlipReset = 0f;
    [Tooltip("How long opposite steer must be held before drift charge is cut. Prevents flick / swap from instantly killing charge.")]
    [SerializeField, Min(0f)] private float steerFlipGraceSeconds = 0.22f;
    [SerializeField] private float steerFlipRebuildDelay = 0.1f;
    [SerializeField] private bool allowDriftGlideWithoutSteer = true;
    [SerializeField] private float driftGlideDecayPerSecond = 0.15f;
    [Tooltip("After landing while still holding a mid-air drift, how long to ease into full ground-drift feel (steer, side force, camera). 0 = snap.")]
    [SerializeField, Min(0f)] private float driftLandingFeelBlendSeconds = 0.38f;

    [Header("Close Call Speed Boost")]
    [SerializeField] private float closeCallBoostBaseDuration = 0.9f;
    [SerializeField] private float closeCallBoostForce = 9f;
    [SerializeField] private ForceMode closeCallBoostForceMode = ForceMode.VelocityChange;
    [SerializeField] private float closeCallBoostMaxSpeedMult = 1.3f;

    [Header("Close Call Invincibility")]
    [SerializeField] private Color closeCallInvincibilityTint = new Color(0.5f, 0.8f, 1f, 0.5f);
    [SerializeField] private Color closeCallTintColor = new Color(0.3f, 0.6f, 1f, 1f);
    [SerializeField] private float closeCallTintStrength = 0.6f;
    [SerializeField] private float invincibilityBumpForceAway = 12f;
    [SerializeField] private float invincibilityBumpForceUp = 3f;
    [SerializeField] private float invincibilityBumpTorque = 5f;

    [Header("Close Call Near Misses")]
    [SerializeField] private bool enableCloseCallNearMisses = true;
    [SerializeField] private float closeCallDistance = 0.45f;
    [SerializeField] private float closeCallMinSpeed = 2.88f;
    [SerializeField] private float closeCallCooldown = 1.42f;
    [SerializeField] private float closeCallRootCooldown = 4.39f;

    [Header("Ice Surface Transition")]
    [SerializeField] private float iceFrictionTransitionSpeed = 3f;
    [SerializeField] private float iceHandlingTransitionSpeed = 4f;
    [SerializeField] private float iceLateralSlideForce = 0.1f;
    [SerializeField] private float iceVelocityAlignmentStrength = 0.02f;

    public bool RequireDriftUnlock => requireDriftUnlock;
    public KeyCode DriftKey => driftKey;
    public float DriftMinSpeed => driftMinSpeed;
    public float MaxDriftSteerMultiplier => maxDriftSteerMultiplier;
    public float DriftSteeringInputBuildupMultiplier => driftSteeringInputBuildupMultiplier;
    public float DriftBuildRate => driftBuildRate;
    public float DriftReleaseRate => driftReleaseRate;
    public float DriftSideForce => driftSideForce;
    public float DriftSideForceWhileAccelerating => driftSideForceWhileAccelerating;
    public float DriftSideForceThrottleBlendRate => driftSideForceThrottleBlendRate;
    public float DriftSpeedDecayPerSecond => driftSpeedDecayPerSecond;
    public float DriftHeldSpeedDecayPerSecond => driftHeldSpeedDecayPerSecond;
    public float DriftForwardAccelMultiplier => driftForwardAccelMultiplier;
    public bool UseFullAccelWhileDrifting => useFullAccelWhileDrifting;
    public bool LockToDriftPeakSpeed => lockToDriftPeakSpeed;
    public float DriftBrakeDecayPerSecond => driftBrakeDecayPerSecond;
    public bool RequireDirectionalInputForDriftCharge => requireDirectionalInputForDriftCharge;
    public float DriftNeutralDrainRate => driftNeutralDrainRate;
    public float DriftNeutralFullResetDelay => driftNeutralFullResetDelay;
    public bool ResetDriftChargeOnSteerFlip => resetDriftChargeOnSteerFlip;
    public float SteerFlipRetainedCharge => steerFlipRetainedCharge;
    public float SteerFlipThreshold => steerFlipThreshold;
    public float MinChargeForFlipReset => minChargeForFlipReset;
    public float SteerFlipGraceSeconds => steerFlipGraceSeconds;
    public float SteerFlipRebuildDelay => steerFlipRebuildDelay;
    public bool AllowDriftGlideWithoutSteer => allowDriftGlideWithoutSteer;
    public float DriftGlideDecayPerSecond => driftGlideDecayPerSecond;
    public float DriftLandingFeelBlendSeconds => driftLandingFeelBlendSeconds;
    public float CloseCallBoostBaseDuration => closeCallBoostBaseDuration;
    public float CloseCallBoostForce => closeCallBoostForce;
    public ForceMode CloseCallBoostForceMode => closeCallBoostForceMode;
    public float CloseCallBoostMaxSpeedMult => closeCallBoostMaxSpeedMult;
    public Color CloseCallInvincibilityTint => closeCallInvincibilityTint;
    public Color CloseCallTintColor => closeCallTintColor;
    public float CloseCallTintStrength => closeCallTintStrength;
    public float InvincibilityBumpForceAway => invincibilityBumpForceAway;
    public float InvincibilityBumpForceUp => invincibilityBumpForceUp;
    public float InvincibilityBumpTorque => invincibilityBumpTorque;
    public bool EnableCloseCallNearMisses => enableCloseCallNearMisses;
    public float CloseCallDistance => closeCallDistance;
    public float CloseCallMinSpeed => closeCallMinSpeed;
    public float CloseCallCooldown => closeCallCooldown;
    public float CloseCallRootCooldown => closeCallRootCooldown;
    public float IceFrictionTransitionSpeed => iceFrictionTransitionSpeed;
    public float IceHandlingTransitionSpeed => iceHandlingTransitionSpeed;
    public float IceLateralSlideForce => iceLateralSlideForce;
    public float IceVelocityAlignmentStrength => iceVelocityAlignmentStrength;
}
