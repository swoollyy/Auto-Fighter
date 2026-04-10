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

    [Tooltip("Small upward velocity (m/s, VelocityChange) applied once when surface sampling crosses between mostly-road and mostly-grass. 0 = off.")]
    [SerializeField, Min(0f)] private float roadGrassTransitionLiftSpeed = 0.35f;
    [Tooltip("Minimum planar speed (m/s) before a transition lift can trigger.")]
    [SerializeField, Min(0f)] private float roadGrassTransitionMinSpeed = 2.5f;
    [Tooltip("Minimum seconds between lifts so grassFraction chatter at ~50% does not spam.")]
    [SerializeField, Min(0f)] private float roadGrassTransitionLiftCooldown = 0.25f;

    [Header("HP death — tumble (single ground ray)")]
    [Tooltip("Downward ray length from the car to decide terrain after HP death (grass vs road, etc.).")]
    [SerializeField, Min(2f)] private float deathHpTerrainRayLength = 56f;

    [Tooltip("Ray start height above the car collider center (world up).")]
    [SerializeField, Min(0f)] private float deathHpTerrainRayStartHeight = 0.85f;

    [Tooltip("Extra drag multiplier on grass/dirt during HP-death tumble (on top of GroundSurface.dragMultiplier).")]
    [SerializeField, Min(1f)] private float deathHpGrassDragBoost = 1.45f;

    [Tooltip("Angular drag multiplier on grass during HP-death tumble.")]
    [SerializeField, Min(1f)] private float deathHpGrassAngularDragBoost = 1.35f;

    [Tooltip("Extra horizontal velocity damping per second on grass while tumbling (0 = drag only).")]
    [SerializeField, Min(0f)] private float deathHpGrassPlanarDampingPerSecond = 6f;

    [Tooltip("Added to planar damping × (1 + planarSpeed × this). Makes fast slides bleed off quicker on grass.")]
    [SerializeField, Min(0f)] private float deathHpGrassPlanarDampingPerSpeed = 0.12f;

    [Tooltip("Multiplies Rigidbody.drag from surface after HP death on grass (drag is weak alone for heavy slides).")]
    [SerializeField, Min(1f)] private float deathHpGrassRigidbodyDragScale = 2.35f;

    [Tooltip("Multiplies tire PhysicMaterial friction on grass during HP tumble (contact grip, not just drag).")]
    [SerializeField, Min(1f)] private float deathHpGrassFrictionScale = 1.65f;

    [Tooltip("Hard cap on planar speed (m/s) while tumbling on grass after HP death; 0 = use surface max-speed only.")]
    [SerializeField, Min(0f)] private float deathHpGrassTumbleMaxPlanarSpeed = 11f;

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
    public float RoadGrassTransitionLiftSpeed => roadGrassTransitionLiftSpeed;
    public float RoadGrassTransitionMinSpeed => roadGrassTransitionMinSpeed;
    public float RoadGrassTransitionLiftCooldown => roadGrassTransitionLiftCooldown;

    public float DeathHpTerrainRayLength => deathHpTerrainRayLength;
    public float DeathHpTerrainRayStartHeight => deathHpTerrainRayStartHeight;
    public float DeathHpGrassDragBoost => deathHpGrassDragBoost;
    public float DeathHpGrassAngularDragBoost => deathHpGrassAngularDragBoost;
    public float DeathHpGrassPlanarDampingPerSecond => deathHpGrassPlanarDampingPerSecond;
    public float DeathHpGrassPlanarDampingPerSpeed => deathHpGrassPlanarDampingPerSpeed;
    public float DeathHpGrassRigidbodyDragScale => deathHpGrassRigidbodyDragScale;
    public float DeathHpGrassFrictionScale => deathHpGrassFrictionScale;
    public float DeathHpGrassTumbleMaxPlanarSpeed => deathHpGrassTumbleMaxPlanarSpeed;
}
