using UnityEngine;

/// <summary>
/// Fuel settings. CarController reads these at Start; max fuel and use rates are still modified by skills.
/// </summary>
public class CarFuelConfig : MonoBehaviour
{
    [Header("Fuel Settings")]
    [SerializeField] private float maxFuel = 100f;
    [SerializeField] private float fuelUsePerSecondAtFullThrottle = 0f;
    [SerializeField] private float fuelUsePerSecondBraking = 0f;
    [SerializeField] private float idleFuelUsePerSecond = 0f;
    [SerializeField] private float idleSpeedThreshold = 0.5f;

    [Header("Low HP Extra Fuel Drain")]
    [Tooltip("When enabled, fuel use scales up as HP drops. No extra drain at 100% HP; scales linearly to Extra Drain At 0 HP at 0% HP.")]
    [SerializeField] private bool enableLowHpExtraFuelDrain = true;
    [Tooltip("Extra fuel consumption at 0% HP, as a percent of base use (0 = none, 100 = double fuel at 0 HP).")]
    [SerializeField, Range(0f, 100f)] private float extraFuelDrainPercentAtZeroHp = 100f;

    public float MaxFuel => maxFuel;
    public float FuelUsePerSecondAtFullThrottle => fuelUsePerSecondAtFullThrottle;
    public float FuelUsePerSecondBraking => fuelUsePerSecondBraking;
    public float IdleFuelUsePerSecond => idleFuelUsePerSecond;
    public float IdleSpeedThreshold => idleSpeedThreshold;
    public bool EnableLowHpExtraFuelDrain => enableLowHpExtraFuelDrain;
    public float ExtraFuelDrainPercentAtZeroHp => extraFuelDrainPercentAtZeroHp;
}
