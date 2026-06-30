using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// One asset = one trial. The config is the DRIVING source for the track generator and all three
/// spawners: when a section's "override" toggle is ON, every field below is pushed into the live
/// component at run start (config wins). When OFF, that component keeps its own scene values.
///
/// Excluded on purpose: scene-object references (trackGenerator/playerTransform/parents/mainCamera) —
/// a ScriptableObject asset can't hold scene refs; those stay wired on the components. Project-asset
/// references (segment prefab, road material, prefab lists) and LayerMasks/AnimationCurves are fine.
///
/// Create via: right-click in Project -> Create -> Racing -> Trial Config.
/// </summary>
[CreateAssetMenu(fileName = "TrialConfig", menuName = "Racing/Trial Config", order = 0)]
public class TrialConfig : ScriptableObject
{
    [Header("Identity")]
    public string trialName = "New Trial";
    [TextArea(2, 4)] public string designerNotes;

    [Header("Goal")]
    [Tooltip("Days (runs) allowed to reach the target progress before this trial fails.")]
    [Min(1)] public int dayLimit = 5;
    [Tooltip("Road progress (0 = start, 1 = end) the player must reach on any run within the day limit to advance.")]
    [Range(0f, 1f)] public float targetProgress = 0.5f;

    [Header("Per-Section Settings")]
    public TrackSettings track = new();
    public ObstacleSettings obstacles = new();
    public CreatureSettings creatures = new();
    public NpcTrafficSettings npcTraffic = new();

    // =====================================================================
    // TRACK — mirrors ProceduralTrackGenerator
    // =====================================================================
    [System.Serializable]
    public class TrackSettings
    {
        [Tooltip("Uncheck to leave the scene's ProceduralTrackGenerator untouched for this trial.")]
        public bool overrideTrack = true;

        [Header("Track Segment Settings")]
        [Tooltip("Road segment prefab (asset). Leave null to keep the generator's current prefab.")]
        public GameObject segmentPrefab;
        public int segmentCount = 200;

        [Header("Segment Length")]
        [Tooltip("If on, segmentLength/roadWidth may be auto-derived from the segment prefab bounds (can override roadWidth below). Turn off to force the roadWidth set here.")]
        public bool autoDetectSegmentLength = true;
        public float segmentLength = 10f;

        [Header("Turn Tightness (Difficulty)")]
        public float minTurnAngle = 5f;
        public float startMaxTurnAngle = 10f;
        public float endMaxTurnAngle = 40f;
        public AnimationCurve difficultyCurve = AnimationCurve.Linear(0, 0, 1, 1);

        [Header("Turn Frequency")]
        [Range(0f, 1f)] public float startTurnChance = 0.35f;
        [Range(0f, 1f)] public float endTurnChance = 0.85f;
        public AnimationCurve turnFrequencyCurve = AnimationCurve.Linear(0, 0, 1, 1);

        [Header("Small Wiggles")]
        public float maxWiggleAngle = 3f;
        [Range(0f, 1f)] public float wiggleOverDistance = 0.4f;

        [Header("Avoidance (Turn Away From Existing Track)")]
        public bool useAvoidance = true;
        public float avoidanceRadius = 40f;
        [Range(0f, 2f)] public float avoidanceStrength = 0.7f;
        [Range(0.1f, 4f)] public float avoidanceFalloff = 1.0f;

        [Header("Global Track Direction")]
        public bool constrainToGlobalDirection = true;
        [Range(0f, 1f)] public float globalAlignmentStrength = 0.3f;
        public float maxHeadingDeviation = 110f;

        [Header("Randomness")]
        public bool useRandomSeed = true;
        public int fixedSeed = 12345;

        [Header("Road Mesh")]
        public bool generateRoadMesh = true;
        [Min(1f)] public float roadWidth = 4f;
        [Tooltip("Road material (asset). Leave null to keep the generator's current material.")]
        public Material roadMaterial;
        public float uvTiling = 0.1f;

        [Header("Path Smoothing (Visual Only)")]
        public bool useSmoothing = false;
        public int smoothingSubdivisionsPerSegment = 6;

        [Header("Self-Intersection Avoidance")]
        public bool preventSelfIntersections = true;
        public int yawSearchSamples = 24;
        public float collisionPadding = 0.5f;
        public float trackRadiusMultiplier = 1.3f;
        public int recentIgnoreCount = 0;
    }

    // =====================================================================
    // OBSTACLES — mirrors TrackObstacleSpawner
    // =====================================================================
    [System.Serializable]
    public class ObstacleSettings
    {
        [Tooltip("Uncheck to use the scene TrackObstacleSpawner's own values for this trial.")]
        public bool overrideObstacles = true;

        [Header("Spawn Mode")]
        public bool preSpawnOnInitialize = true;
        public bool streamSpawnDuringRun = false;
        public bool streamWhileQueueControlled = true;

        [Header("Obstacle Types")]
        [Tooltip("Per entry: prefab, baseWeight, and distance bands (start/full/stop). Omit a type or set baseWeight 0 to keep it OUT of this trial.")]
        public List<ObstacleType> obstacleTypes = new();

        [Header("Path Sampling")]
        public bool useSmoothing = true;
        [Min(1)] public int smoothingSubdivisionsPerSegment = 6;

        [Header("Spawn Settings")]
        public float obstacleSpacing = 40f;
        public int maxActiveObstacles = 20;
        public float minSpawnDistanceAhead = 60f;
        public float maxSpawnDistanceAhead = 160f;

        [Header("Initial Pre-Spawn")]
        public float initialPreSpawnDistance = 120f;
        public float despawnBehindDistance = 10f;

        [Header("Randomization")]
        public float distanceJitter = 12f;
        [Range(0f, 1f)] public float spawnChancePerSlot = 0.45f;
        public AnimationCurve globalSpawnChanceByDistance = AnimationCurve.Linear(0f, 0.4f, 1f, 1f);
        [Range(0f, 1f)] public float lateralFraction = 0.6f;
        public float edgeInnerMargin = 0.5f;

        [Header("Spawn Stabilization")]
        public bool stabilizeRigidbodiesOnSpawn = true;
        [Min(0f)] public float spawnKinematicDuration = 2.0f;
        public bool disableGravityWhileKinematic = true;

        [Header("Raycast")]
        public LayerMask roadLayer;
        public float raycastStartHeight = 6f;
        public float raycastDownDistance = 20f;
        public float obstacleHeightOffset = 0.2f;

        [Header("Timing")]
        public float updateInterval = 0.5f;

        [Header("Debug")]
        public bool verboseDebug = false;
    }

    // =====================================================================
    // CREATURES — mirrors TrackCreatureSpawner
    // =====================================================================
    [System.Serializable]
    public class CreatureSettings
    {
        [Tooltip("Uncheck to use the scene TrackCreatureSpawner's own values for this trial.")]
        public bool overrideCreatures = true;

        [Header("Creature vs Traffic")]
        public LayerMask npcTrafficLayerMask;
        public LayerMask creatureObstacleAvoidanceLayers;

        [Header("Spawn Mode")]
        public bool preSpawnOnInitialize = true;
        public bool streamSpawnDuringRun = true;

        [Header("Creature Types (full per-type aggression tuning lives here)")]
        public List<CreatureTypeConfig> creatureTypes = new();

        [Header("Path Sampling")]
        public bool useSmoothing = true;
        [Min(1)] public int smoothingSubdivisionsPerSegment = 6;

        [Header("Spawn Settings")]
        public float creatureSpacing = 50f;
        public int maxActiveCreatures = 15;
        public float minSpawnDistanceAhead = 80f;
        public float maxSpawnDistanceAhead = 200f;

        [Header("Initial Pre-Spawn")]
        public float initialPreSpawnDistance = 150f;

        [Header("Despawn")]
        public float despawnBehindDistance = 20f;

        [Header("Randomization")]
        public float distanceJitter = 15f;
        [Range(0f, 1f)] public float spawnChancePerSlot = 0.5f;
        public AnimationCurve spawnChanceByDistance = AnimationCurve.Linear(0f, 0.3f, 1f, 1f);

        [Header("Placement")]
        [Range(0f, 1f)] public float lateralFraction = 0.7f;
        public float edgeInnerMargin = 0.5f;

        [Header("Raycast")]
        public LayerMask roadLayer;
        public float raycastStartHeight = 6f;
        public float raycastDownDistance = 20f;
        public float creatureHeightOffset = 0.1f;

        [Header("Timing")]
        public float updateInterval = 0.4f;

        [Header("Debug")]
        public bool verboseDebug = false;
    }

    // =====================================================================
    // NPC TRAFFIC — mirrors NPCTrafficCarSpawner
    // =====================================================================
    [System.Serializable]
    public class NpcTrafficSettings
    {
        [Tooltip("Uncheck to use the scene NPCTrafficCarSpawner's own values for this trial.")]
        public bool overrideNpcTraffic = true;

        [Header("Spawn Mode")]
        public bool preSpawnOnInitialize = true;
        public bool streamSpawnDuringRun = true;

        [Header("Car Prefabs")]
        public List<NPCCarType> carTypes = new();

        [Header("Path Sampling")]
        public bool useSmoothing = true;
        [Min(1)] public int smoothingSubdivisionsPerSegment = 6;

        [Header("Spawn Settings")]
        public float carSpacing = 80f;
        public int maxActiveCars = 8;
        public float minSpawnDistanceAhead = 100f;
        public float maxSpawnDistanceAhead = 300f;

        [Header("Behind Spawning")]
        public bool allowSpawnBehind = true;
        public float minSpawnDistanceBehind = 40f;
        public float maxSpawnDistanceBehind = 150f;

        [Header("Camera Culling")]
        [Tooltip("mainCamera reference stays on the spawner (it auto-finds the main camera).")]
        public float viewportMargin = 0.1f;

        [Header("Initial Pre-Spawn")]
        public float initialPreSpawnDistance = 200f;

        [Header("Despawn")]
        public float despawnBehindDistance = 30f;
        public float despawnCrashedAfter = 8f;

        [Header("Randomization")]
        public float distanceJitter = 20f;
        [Range(0f, 1f)] public float spawnChancePerSlot = 0.6f;
        public AnimationCurve spawnChanceByDistance = AnimationCurve.Linear(0f, 0.3f, 1f, 1f);

        [Header("Lane Assignment")]
        [Range(0f, 1f)] public float lateralFraction = 0.7f;
        public float edgeMargin = 0.8f;
        public bool preferLanes = true;
        [Range(0f, 1f)] public float oncomingLaneChance = 0.15f;

        [Header("Spawn Safety Check")]
        public bool avoidImmediateObstacleHits = true;
        public float spawnLookaheadDistance = 40f;
        public float spawnLookaheadStep = 4f;
        public float spawnProbeRadius = 1.2f;
        public LayerMask spawnBlockerLayers;

        [Header("Raycast")]
        public LayerMask roadLayer;
        public float raycastStartHeight = 6f;
        public float raycastDownDistance = 20f;
        public float carHeightOffset = 0.1f;

        [Header("Timing")]
        public float updateInterval = 0.4f;

        [Header("Debug")]
        public bool verboseDebug = false;
    }
}
