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
    [SerializeField] private float driftBuildRate = 0.7f;
    [SerializeField] private float driftReleaseRate = 12.2f;
    [SerializeField] private float driftSideForce = 0.41f;
    [SerializeField] private float driftSpeedDecayPerSecond = 0.06f;
    [SerializeField] private float driftHeldSpeedDecayPerSecond = 0f;
    [SerializeField] private float driftForwardAccelMultiplier = 0f;
    [SerializeField] private bool useFullAccelWhileDrifting = true;
    [SerializeField] private bool lockToDriftPeakSpeed = true;

    [Header("Drift Braking")]
    [SerializeField] private float driftBrakeDecayPerSecond = 0.001f;

    [Header("Drift Charge")]
    [SerializeField] private bool requireDirectionalInputForDriftCharge = false;
    [SerializeField] private float driftNeutralDrainRate = 2.6f;
    [SerializeField] private float driftNeutralFullResetDelay = 3.65f;
    [SerializeField] private bool resetDriftChargeOnSteerFlip = true;
    [SerializeField] private float steerFlipRetainedCharge = 0.055f;
    [SerializeField] private float steerFlipThreshold = 0.15f;
    [SerializeField] private float minChargeForFlipReset = 0f;
    [SerializeField] private float steerFlipRebuildDelay = 0.1f;
    [SerializeField] private bool allowDriftGlideWithoutSteer = true;
    [SerializeField] private float driftGlideDecayPerSecond = 0.15f;

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
    public float DriftBuildRate => driftBuildRate;
    public float DriftReleaseRate => driftReleaseRate;
    public float DriftSideForce => driftSideForce;
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
    public float SteerFlipRebuildDelay => steerFlipRebuildDelay;
    public bool AllowDriftGlideWithoutSteer => allowDriftGlideWithoutSteer;
    public float DriftGlideDecayPerSecond => driftGlideDecayPerSecond;
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
