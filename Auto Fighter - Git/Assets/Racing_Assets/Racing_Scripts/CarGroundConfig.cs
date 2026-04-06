using UnityEngine;

/// <summary>
/// Ground detection and surface sampling. CarController reads these at Start.
/// </summary>
public class CarGroundConfig : MonoBehaviour
{
    [Header("Ground Detection")]
    [Tooltip("Include Default if Terrain uses that layer for ground hits.")]
    [SerializeField] private LayerMask groundLayers = (LayerMask)(90112 | 1);
    [SerializeField] private int samplesX = 6;
    [SerializeField] private int samplesZ = 6;
    [SerializeField] private float raycastHeightOffset = 0.5f;
    [SerializeField] private float raycastExtraDistance = -0.72f;
    [SerializeField] private bool debugSurfaceRays = true;
    [SerializeField] private float surfaceSampleExtent = 1.13f;

    [Header("Tipped / rollover surface sampling")]
    [Tooltip("When the car is rolled or on its side, the normal underside ray grid misses the road. If enabled, uses world-down rays from the collider bounds instead while tilted.")]
    [SerializeField] private bool useTippedOverWorldDownSampler = true;
    [Tooltip("Use the tipped sampler when transform.up·worldUp is below this (0 = any roll, 1 = never). Default ~0.55 ≈ past ~56° from upright.")]
    [SerializeField, Range(-0.2f, 1f)] private float tippedOverSurfaceUpDotThreshold = 0.55f;

    [Header("Grass / road edge (raised mesh)")]
    [Tooltip("How fast driving physics blends toward the raycast ground normal (higher = snappier).")]
    [SerializeField, Min(0f)] private float groundNormalBlendRate = 14f;
    [Tooltip("Multiplier on blend speed when part grass / part road (lower = heavier filtering, less normal jitter at the lip).")]
    [SerializeField, Range(0.08f, 1f)] private float groundNormalMixedSurfaceBlendScale = 0.42f;
    [Tooltip("grassFraction must be inside [min,max] to count as mixed surface for normal smoothing.")]
    [SerializeField, Range(0f, 0.45f)] private float groundNormalMixedGrassMin = 0.06f;
    [SerializeField, Range(0.55f, 1f)] private float groundNormalMixedGrassMax = 0.94f;

    [Tooltip("Acceleration in your horizontal travel direction while straddling grass and road; 0 = off. Softens speed loss on small road height steps.")]
    [SerializeField, Min(0f)] private float roadReturnAssistAccel = 7f;
    [SerializeField, Min(0f)] private float roadReturnAssistMinSpeed = 2.5f;

    public LayerMask GroundLayers => groundLayers;
    public int SamplesX => samplesX;
    public int SamplesZ => samplesZ;
    public float RaycastHeightOffset => raycastHeightOffset;
    public float RaycastExtraDistance => raycastExtraDistance;
    public bool DebugSurfaceRays => debugSurfaceRays;
    public float SurfaceSampleExtent => surfaceSampleExtent;
    public bool UseTippedOverWorldDownSampler => useTippedOverWorldDownSampler;
    public float TippedOverSurfaceUpDotThreshold => tippedOverSurfaceUpDotThreshold;

    public float GroundNormalBlendRate => groundNormalBlendRate;
    public float GroundNormalMixedSurfaceBlendScale => groundNormalMixedSurfaceBlendScale;
    public float GroundNormalMixedGrassMin => groundNormalMixedGrassMin;
    public float GroundNormalMixedGrassMax => groundNormalMixedGrassMax;
    public float RoadReturnAssistAccel => roadReturnAssistAccel;
    public float RoadReturnAssistMinSpeed => roadReturnAssistMinSpeed;
}
