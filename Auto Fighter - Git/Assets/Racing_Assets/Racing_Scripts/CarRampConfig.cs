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

    public bool EnableRampAlignment => enableRampAlignment;
    public float GroundAlignSpeed => groundAlignSpeed;
    public float AirAlignSpeed => airAlignSpeed;
    public float GroundNormalCastRadius => groundNormalCastRadius;
    public float GroundNormalCheckDistance => groundNormalCheckDistance;
    public float LandingPredictDistance => landingPredictDistance;
    public float LandingAlignStartDistance => landingAlignStartDistance;
}
