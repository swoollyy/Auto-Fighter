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

    public LayerMask GroundLayers => groundLayers;
    public int SamplesX => samplesX;
    public int SamplesZ => samplesZ;
    public float RaycastHeightOffset => raycastHeightOffset;
    public float RaycastExtraDistance => raycastExtraDistance;
    public bool DebugSurfaceRays => debugSurfaceRays;
    public float SurfaceSampleExtent => surfaceSampleExtent;
}
