using UnityEngine;

/// <summary>
/// Steering and ice-steer settings. CarController reads these at Start; effective turn speed is still driven by upgrades/surfaces.
/// </summary>
public class CarSteeringConfig : MonoBehaviour
{
    [Header("Steering")]
    [SerializeField] private float turnSpeed = 11f;
    [SerializeField] private float minSpeedToSteer = 0.4f;
    [SerializeField] private bool allowSteerWhenTryingToMove = true;

    [Header("Steering Feel")]
    [SerializeField] private float lowSpeedSteerMultiplier = 8f;
    [SerializeField] private float highSpeedSteerMultiplier = 1.3f;
    [SerializeField] private float speedForSteerCurve = 3.45f;
    [Tooltip("How quickly steering input follows your stick (turn-in). Higher = snappier.")]
    [SerializeField] private float steeringInputSmooth = 9f;
    [Tooltip("How quickly steering returns to center when you release the stick. Lower = slower, less snap-back. If 0, uses Steering Input Smooth for both.")]
    [SerializeField, Min(0f)] private float steeringReturnSmooth = 0f;

    [Header("Arcade Steering Extras")]
    [SerializeField] private bool useAutoAlignToVelocity = false;
    [SerializeField] private float autoAlignStrength = 3f;

    [Header("Ice Steering Ramp")]
    [SerializeField] private bool enableIceSteerRamp = true;
    [SerializeField] private float iceSteerRampUpRate = 10f;
    [SerializeField] private float iceSteerRampDownRate = 1.83f;
    [SerializeField, Range(0f, 1f)] private float iceSteerMinFactor = 0.755f;
    [SerializeField, Range(0f, 1f)] private float iceSteerFlipPenalty = 0.35f;

    [Header("Steering Direction")]
    [SerializeField] private bool invertSteeringWhenReversing = true;
    [SerializeField] private float reverseSteerMultiplier = 1f;
    [Tooltip("While the player is holding reverse/brake, steering flips to reverse mode once forward speed drops below this (m/s) - instead of waiting for the car to fully start moving backward. Makes the forward->reverse turn switch seamless. Set 0 for the old behavior (flip only once actually moving backward).")]
    [SerializeField, Min(0f)] private float reverseSteerEngageForwardSpeed = 1.5f;

    [Header("Steer Rolling Traction")]
    [SerializeField] private float baseSteeringDamp = 8f;
    [SerializeField] private bool enableSteerTraction = true;
    [SerializeField] private float steerTractionReorientRate = 5.59f;
    [SerializeField] private float steerRollingAccel = 2.25f;
    [SerializeField] private float minSpeedForSteerTraction = 0.1f;
    [SerializeField] private float lateralFrictionWhileSteering = 2.46f;
    [SerializeField] private float steerTractionBlendIn = 8.21f;
    [SerializeField] private float steerTractionBlendOut = 7.1f;
    [SerializeField, Range(0f, 2f)] private float steerRollingAccelCoastMultiplier = 0.441f;
    [SerializeField] private bool applySteerRollingAccelOnIce = false;

    public float TurnSpeed => turnSpeed;
    public float MinSpeedToSteer => minSpeedToSteer;
    public bool AllowSteerWhenTryingToMove => allowSteerWhenTryingToMove;
    public float LowSpeedSteerMultiplier => lowSpeedSteerMultiplier;
    public float HighSpeedSteerMultiplier => highSpeedSteerMultiplier;
    public float SpeedForSteerCurve => speedForSteerCurve;
    public float SteeringInputSmooth => steeringInputSmooth;
    public float SteeringReturnSmooth => steeringReturnSmooth;
    public bool UseAutoAlignToVelocity => useAutoAlignToVelocity;
    public float AutoAlignStrength => autoAlignStrength;
    public bool EnableIceSteerRamp => enableIceSteerRamp;
    public float IceSteerRampUpRate => iceSteerRampUpRate;
    public float IceSteerRampDownRate => iceSteerRampDownRate;
    public float IceSteerMinFactor => iceSteerMinFactor;
    public float IceSteerFlipPenalty => iceSteerFlipPenalty;
    public bool InvertSteeringWhenReversing => invertSteeringWhenReversing;
    public float ReverseSteerMultiplier => reverseSteerMultiplier;
    public float ReverseSteerEngageForwardSpeed => reverseSteerEngageForwardSpeed;
    public float BaseSteeringDamp => baseSteeringDamp;
    public bool EnableSteerTraction => enableSteerTraction;
    public float SteerTractionReorientRate => steerTractionReorientRate;
    public float SteerRollingAccel => steerRollingAccel;
    public float MinSpeedForSteerTraction => minSpeedForSteerTraction;
    public float LateralFrictionWhileSteering => lateralFrictionWhileSteering;
    public float SteerTractionBlendIn => steerTractionBlendIn;
    public float SteerTractionBlendOut => steerTractionBlendOut;
    public float SteerRollingAccelCoastMultiplier => steerRollingAccelCoastMultiplier;
    public bool ApplySteerRollingAccelOnIce => applySteerRollingAccelOnIce;
}
