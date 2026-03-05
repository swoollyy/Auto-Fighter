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

    public float MaxFuel => maxFuel;
    public float FuelUsePerSecondAtFullThrottle => fuelUsePerSecondAtFullThrottle;
    public float FuelUsePerSecondBraking => fuelUsePerSecondBraking;
    public float IdleFuelUsePerSecond => idleFuelUsePerSecond;
    public float IdleSpeedThreshold => idleSpeedThreshold;
}
