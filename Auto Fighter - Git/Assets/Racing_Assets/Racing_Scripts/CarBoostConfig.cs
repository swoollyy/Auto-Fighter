using UnityEngine;

/// <summary>
/// Boost and drift-held boost settings. CarController reads these at Start; effective boost values are still modified by skills.
/// </summary>
public class CarBoostConfig : MonoBehaviour
{
    [Header("Boost Unlock")]
    [SerializeField] private bool requireBoostUnlock = true;

    [Header("Boost Screen Flash")]
    [SerializeField] private float boostFlashSpeedThreshold = 2f;
    [SerializeField] private float boostFlashCooldown = 0.3f;

    [Header("Boost")]
    [SerializeField] private KeyCode boostKey = KeyCode.Space;
    [SerializeField] private float boostForce = 30f;
    [SerializeField] private float boostSustainAcceleration = 30f;
    [SerializeField] private float boostDuration = 0.35f;
    [SerializeField] private float boostMaxSpeedMultiplier = 1.65f;
    [SerializeField] private float postBoostSlowdownDuration = 2f;
    [SerializeField] private float boostCooldown = 5f;
    [SerializeField] private float boostFuelCost = 15f;
    [Tooltip("While airborne, multiply boost impulse and sustain (button boost, drift boost, and close-call). 1 = full ground strength, 0.4 = 40%.")]
    [SerializeField, Range(0f, 1f)] private float airBoostForceMultiplier = 0.4f;

    [Header("Drift-held Boost")]
    [SerializeField] private float driftBoostSustainAcceleration = 30f;
    [SerializeField] private bool enableDriftHeldBoost = true;
    [SerializeField] private float driftBoostMinHoldSeconds = 0.8f;
    [SerializeField] private float driftBoostMaxHoldSeconds = 2.5f;
    [SerializeField] private Vector2 driftBoostForceRange = new Vector2(0.5f, 0.5f);
    [SerializeField] private Vector2 driftBoostDurationRange = new Vector2(0.4f, 0.7f);
    [SerializeField] private Vector2 driftBoostMaxSpeedMultRange = new Vector2(1.25f, 1.25f);
    [SerializeField] private float driftBoostFuelCost = 0f;
    [SerializeField] private float driftBoostCooldown = 3.3f;
    [Tooltip("Charge gain at the lightest real steer (0–1). Full steer always gains at 1. Banked charge never drains from easing off.")]
    [SerializeField, Range(0.05f, 1f)] private float driftBoostChargeMinGainRate = 0.35f;

    public bool RequireBoostUnlock => requireBoostUnlock;
    public float BoostFlashSpeedThreshold => boostFlashSpeedThreshold;
    public float BoostFlashCooldown => boostFlashCooldown;
    public KeyCode BoostKey => boostKey;
    public float BoostForce => boostForce;
    public float BoostSustainAcceleration => boostSustainAcceleration;
    public float BoostDuration => boostDuration;
    public float BoostMaxSpeedMultiplier => boostMaxSpeedMultiplier;
    public float PostBoostSlowdownDuration => postBoostSlowdownDuration;
    public float BoostCooldown => boostCooldown;
    public float BoostFuelCost => boostFuelCost;
    public float AirBoostForceMultiplier => airBoostForceMultiplier;
    public float DriftBoostSustainAcceleration => driftBoostSustainAcceleration;
    public bool EnableDriftHeldBoost => enableDriftHeldBoost;
    public float DriftBoostMinHoldSeconds => driftBoostMinHoldSeconds;
    public float DriftBoostMaxHoldSeconds => driftBoostMaxHoldSeconds;
    public Vector2 DriftBoostForceRange => driftBoostForceRange;
    public Vector2 DriftBoostDurationRange => driftBoostDurationRange;
    public Vector2 DriftBoostMaxSpeedMultRange => driftBoostMaxSpeedMultRange;
    public float DriftBoostFuelCost => driftBoostFuelCost;
    public float DriftBoostCooldown => driftBoostCooldown;
    public float DriftBoostChargeMinGainRate => driftBoostChargeMinGainRate;
}
