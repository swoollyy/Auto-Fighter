using UnityEngine;

/// <summary>
/// Ramp alignment and landing prediction. CarController reads these at Start.
/// </summary>
public class CarRampConfig : MonoBehaviour
{
    [Header("Verticality / Ramp Alignment")]
    [SerializeField] private bool enableRampAlignment = true;
    [SerializeField] private float groundAlignSpeed = 10f;
    [SerializeField] private float airAlignSpeed = 6f;
    [SerializeField] private float groundNormalCastRadius = 0.35f;
    [SerializeField] private float groundNormalCheckDistance = 1.23f;
    [SerializeField] private float landingPredictDistance = 2.75f;
    [SerializeField] private float landingAlignStartDistance = 1.97f;

    [Header("Ramp / elevation climb feel")]
    [Tooltip("Multiplies ground align speed when the surface is steeper than Steep Align Min Angle (helps pitch catch the ramp sooner).")]
    [SerializeField, Min(1f)] private float steepAlignSpeedMultiplier = 2.1f;
    [Tooltip("Ground tilt (degrees from flat) at which steep align boost begins.")]
    [SerializeField, Min(0f)] private float steepAlignMinAngle = 12f;
    [Tooltip("When the ground normal steepens, remap this fraction of speed onto the new surface tangent so horizontal momentum becomes climb speed (0 = off).")]
    [SerializeField, Range(0f, 1f)] private float rampVelocityRemapStrength = 0.72f;
    [Tooltip("Minimum normal-angle change (degrees) before velocity remap kicks in.")]
    [SerializeField, Min(0.5f)] private float rampVelocityRemapMinAngle = 3.5f;

    public bool EnableRampAlignment => enableRampAlignment;
    public float GroundAlignSpeed => groundAlignSpeed;
    public float AirAlignSpeed => airAlignSpeed;
    public float GroundNormalCastRadius => groundNormalCastRadius;
    public float GroundNormalCheckDistance => groundNormalCheckDistance;
    public float LandingPredictDistance => landingPredictDistance;
    public float LandingAlignStartDistance => landingAlignStartDistance;
    public float SteepAlignSpeedMultiplier => steepAlignSpeedMultiplier;
    public float SteepAlignMinAngle => steepAlignMinAngle;
    public float RampVelocityRemapStrength => rampVelocityRemapStrength;
    public float RampVelocityRemapMinAngle => rampVelocityRemapMinAngle;
}
