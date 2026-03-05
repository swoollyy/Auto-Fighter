using UnityEngine;

/// <summary>
/// Base movement, coast, brake, and steer-traction settings. CarController reads these at Start and applies upgrades via its existing pipeline.
/// </summary>
public class CarMovementConfig : MonoBehaviour
{
    [Header("Base Movement (on Default surface)")]
    [SerializeField] private float baseAcceleration = 5.2f;
    [SerializeField] private float baseMaxSpeed = 3.95f;
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
}
