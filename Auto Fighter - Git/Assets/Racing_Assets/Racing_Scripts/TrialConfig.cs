using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// One asset = one trial. The config is the DRIVING source for the track generator and all spawners:
/// when a section's "override" toggle is ON, every field below is pushed into the live component at
/// run start (config wins). When OFF, that component keeps its own scene values.
///
/// Excluded on purpose: scene-object references (trackGenerator/playerTransform/parents) —
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

    [Header("Skill Tree (this trial)")]
    [Tooltip("If enabled, only skills listed in Allowed Skills can appear in the tree and be purchased during this trial. Progressive unlocks for later skills are still remembered, but stay hidden until a trial that allows them.")]
    public bool restrictSkillsToAllowlist = false;

    [Tooltip("This trial's skillset. Used when Restrict Skills To Allowlist is on, and always used as the 'previous trial' kit when debug-jumping ahead (even if restrict is off).")]
    public List<SkillType> allowedSkills = new();

    /// <summary>True if this skill may be shown / purchased while this trial is active.</summary>
    public bool IsSkillAllowed(SkillType type)
    {
        if (!restrictSkillsToAllowlist)
            return true;
        if (allowedSkills == null || allowedSkills.Count == 0)
            return false;
        return allowedSkills.Contains(type);
    }

    [Header("Per-Section Settings")]
    public TrackSettings track = new();
    public ObstacleSettings obstacles = new();
    public CreatureSettings creatures = new();
    public NpcTrafficSettings npcTraffic = new();
    public CoinSettings coins = new();
    public ThrownSettings thrown = new();
    public RollingLogSettings rollingLogs = new();
    public CrossObstacleSettings crossObstacles = new();
    public IcePathSettings icePaths = new();
    public BounceObstacleSettings bounceObstacles = new();
    public SpawnQueueSettings spawnQueue = new();

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
        [Tooltip("Max turn angle lerps from Start → End over track progress.")]
        public float minTurnAngle = 5f;
        public float startMaxTurnAngle = 10f;
        public float endMaxTurnAngle = 40f;

        [Header("Turn Frequency")]
        [Tooltip("Turn chance lerps from Start → End over track progress.")]
        [Range(0f, 1f)] public float startTurnChance = 0.35f;
        [Range(0f, 1f)] public float endTurnChance = 0.85f;

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

        [Header("Existing-Road Clearance")]
        [Tooltip("Soft clearance from existing track (meters). Closer than this is heavily penalized when scoring yaws.")]
        [Min(1f)] public float softTrackClearanceMeters = 22f;
        [Tooltip("Penalty strength for soft clearance violations (higher = less folding into yourself).")]
        [Min(0f)] public float softProximityPenalty = 4.5f;
        [Tooltip("Extra soft look-ahead distance for avoiding headings that aim into existing track.")]
        [Min(0f)] public float foldAvoidLookAheadMeters = 40f;
        [Tooltip("How strongly to punish aiming toward nearby existing road (anti fold-in).")]
        [Min(0f)] public float foldInAimPenalty = 6.0f;
        [Tooltip("Hard-reject anti-parallel switchbacks closer than this many road widths (centerline).")]
        [Min(1.5f)] public float parallelRejectRoadWidths = 2.4f;
        [Tooltip("Minimum centerline separation for parallel stretches, as a multiple of roadWidth. 1.0 = edges touching, 2.0 = one road-width of dirt between them.")]
        [Min(1f)] public float minSelfClearanceRoadWidths = 2.0f;
        [Tooltip("Extra meters added on top of minSelfClearanceRoadWidths * roadWidth for parallel / nearby stretches.")]
        [Min(0f)] public float minSelfClearanceExtraMeters = 4f;
        [Tooltip("How many extra straight segments to simulate ahead when validating a yaw (catches fold-ins early).")]
        [Range(0, 4)] public int selfCollisionLookAheadSegments = 2;

        [Header("Start / End Separation")]
        [Tooltip("Reject tracks whose finish sits too close to the start (XZ), so players can't cut offroad to the end.")]
        public bool enforceMinStartEndSeparation = true;
        [Tooltip("Absolute minimum planar (XZ) distance between start and FINISH.")]
        public float minStartEndDistance = 120f;
        [Tooltip("Also require finish at least this fraction of total path length away from start on XZ.")]
        [Range(0.05f, 0.75f)] public float minStartEndDistancePathFraction = 0.28f;
        [Tooltip("Legacy field (generator uses start-region keep-out instead of mid-build full end-distance rejects).")]
        [Range(0.05f, 1f)] public float startSeparationEnforceAfterNormalized = 1f;
        [Tooltip("Anti-shortcut keep-out around start/early road during generation (meters). Not the finish distance.")]
        [Min(0f)] public float startRegionKeepOutMeters = 90f;
        [Range(0.05f, 0.5f)] public float startRegionKeepOutAfterNormalized = 0.12f;
        [Range(0.05f, 0.45f)] public float startKeepOutEarlyPathFraction = 0.22f;
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
        [Tooltip("Obstacle spacing in meters. X = at track start, Y = at track end (usually lower Y = denser later).")]
        public Vector2 obstacleSpacingByProgress = new Vector2(40f, 18f);
        public int maxActiveObstacles = 20;
        public float minSpawnDistanceAhead = 60f;
        public float maxSpawnDistanceAhead = 160f;

        [Header("Initial Pre-Spawn")]
        public float initialPreSpawnDistance = 120f;
        public float despawnBehindDistance = 10f;

        [Header("Randomization")]
        public float distanceJitter = 12f;
        [Tooltip("Chance to fill a spawn slot (0–1). X = at track start, Y = at track end.")]
        public Vector2 spawnChanceByProgress = new Vector2(0.18f, 0.45f);
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
        [Tooltip("Chance to fill a spawn slot (0–1). X = at track start, Y = at track end.")]
        public Vector2 spawnChanceByProgress = new Vector2(0.15f, 0.5f);

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

        [Header("Initial Pre-Spawn")]
        public float initialPreSpawnDistance = 200f;

        [Header("Despawn")]
        public float despawnBehindDistance = 30f;
        public float despawnCrashedAfter = 8f;

        [Header("Randomization")]
        public float distanceJitter = 20f;
        [Tooltip("Chance to fill a spawn slot (0–1). X = at track start, Y = at track end.")]
        public Vector2 spawnChanceByProgress = new Vector2(0.18f, 0.6f);

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

    // =====================================================================
    // COINS — mirrors TrackCoinSpawner
    // =====================================================================
    [System.Serializable]
    public class CoinSettings
    {
        [Tooltip("Uncheck to use the scene TrackCoinSpawner's own values for this trial.")]
        public bool overrideCoins = true;

        [Header("Coin Type Weights")]
        [Tooltip("Which coin types can spawn and their distance-based weight multipliers.")]
        public List<TrackCoinSpawner.CoinTypeWeight> coinTypeWeights = new List<TrackCoinSpawner.CoinTypeWeight>()
        {
            new TrackCoinSpawner.CoinTypeWeight { coinType = CoinType.Bronze, enabled = true, weightByProgress = new Vector2(1.12f, 1.12f) },
            new TrackCoinSpawner.CoinTypeWeight { coinType = CoinType.Silver, enabled = true, weightByProgress = new Vector2(0.95f, 0.95f) },
            new TrackCoinSpawner.CoinTypeWeight { coinType = CoinType.Gold, enabled = true, weightByProgress = new Vector2(0.8f, 0.8f) },
            new TrackCoinSpawner.CoinTypeWeight { coinType = CoinType.Platinum, enabled = true, weightByProgress = new Vector2(0.4f, 0.4f) },
            new TrackCoinSpawner.CoinTypeWeight { coinType = CoinType.Diamond, enabled = true, weightByProgress = new Vector2(0.15f, 0.15f) },
            new TrackCoinSpawner.CoinTypeWeight { coinType = CoinType.Legendary, enabled = true, weightByProgress = new Vector2(0.05f, 0.05f) }
        };

        [Header("Spawn Layout")]
        [Min(0.1f)] public float coinSpacing = 6f;
        public int maxActiveCoins = 120;
        public float minSpawnDistanceAhead = 40f;
        public float maxSpawnDistanceAhead = 140f;
        public float despawnBehindDistance = 25f;
        public float initialPreSpawnDistance = 80f;

        [Header("Spawn Probability")]
        [Tooltip("Chance a coin slot fills (0–1). X = at track start, Y = at track end.")]
        public Vector2 spawnChanceByProgress = new Vector2(0.85f, 0.98f);

        [Header("Skill Integration")]
        public bool applySkillSpawnRate = true;

        [Header("Placement")]
        public float coinHeightOffset = 0.5f;
        [Range(0f, 1f)] public float lateralFractionOfHalfWidth = 0.7f;
        public float edgeInnerMargin = 0.25f;

        [Header("Raycast")]
        public LayerMask roadLayer;
        public float raycastStartHeight = 5f;
        public float raycastDownDistance = 15f;
        public bool alignToSurfaceNormal = true;

        [Header("Jitter & Update")]
        public float distanceJitter = 1.5f;
        public bool useSmoothing = true;
        [Min(1)] public int smoothingSubdivisionsPerSegment = 6;
        public float updateInterval = 0.2f;

        [Header("Debug")]
        public bool verboseDebug = false;
    }

    // =====================================================================
    // THROWN — mirrors ThrownObstacleDirector
    // =====================================================================
    [System.Serializable]
    public class ThrownSettings
    {
        [Tooltip("Uncheck to use the scene ThrownObstacleDirector's own values for this trial.")]
        public bool overrideThrown = false;

        [Header("Prefabs")]
        public GameObject projectilePrefabPlain;
        public GameObject projectilePrefabExplosive;
        public GameObject groundRingPrefab;

        [Header("Spawn Control")]
        public bool enabledSpawning = true;
        [Min(0f)] public float spawnCooldownBase = 3.5f;
        public Vector2 leadDistanceRange = new Vector2(12f, 36f);
        [Range(1, 6)] public int maxConcurrent = 2;
        [Range(0f, 3f)] public float concurrentScaleByProgress = 1.5f;

        [Header("Spawn Gate")]
        [Range(0f, 0.5f)] public float spawnEnableProgress = 0.10f;

        [Header("Spawn Cooldown Scaling")]
        [Min(0.05f)] public float minSpawnCooldown = 0.6f;
        public Vector2 spawnCooldownRandomRange = new Vector2(0.85f, 1.15f);

        [Header("Meteor Flight")]
        public float baseProjectileSpeed = 18f;
        public Vector2 flightTimeClamp = new Vector2(0.55f, 3.25f);
        [Min(2f)] public float meteorSpawnHeight = 22f;
        [Min(1f)] public float meteorHorizontalOffset = 14f;
        [Min(0f)] public float minLeadDistance = 6.0f;
        [Min(0f)] public float minLandingDistanceFromPlayer = 4.0f;
        public bool allowCloseLandings = true;
        public LayerMask hitLayers = ~0;
        public bool explosiveByDefault = false;
        public float explosionRadius = 6f;
        public float explosionKnockback = 12f;

        [Header("Projectile Size Variation")]
        public Vector2 projectileSizeRange = new Vector2(0.92f, 1.12f);
        [Range(0f, 1f)] public float sizeGainOverDistance = 0.25f;

        [Header("Spawn Variance")]
        [Range(0f, 3f)] public float lateralJitter = 1.0f;
        [Range(0f, 5f)] public float forwardJitter = 1.8f;

        [Header("Rewards")]
        public int destroyReward = 12;

        [Header("Accuracy / Misses")]
        [Tooltip("Hit accuracy (0–1). X = at track start, Y = at track end.")]
        public Vector2 accuracyByProgress = new Vector2(0.10f, 0.10f);
        [Min(0f)] public float maxMissLateral = 4f;
        [Min(0f)] public float maxMissForward = 6f;

        [Header("Explosion Frequency")]
        [Tooltip("Chance a throw is explosive (0–1). X = at track start, Y = at track end.")]
        public Vector2 explosionChanceByProgress = new Vector2(0.03f, 0.09f);

        [Header("Debug")]
        public bool debugDraw = false;
    }

    // =====================================================================
    // ROLLING LOGS — mirrors RollingLogSpawner
    // =====================================================================
    [System.Serializable]
    public class RollingLogSettings
    {
        [Tooltip("Uncheck to use the scene RollingLogSpawner's own values for this trial.")]
        public bool overrideRollingLogs = false;

        [Header("Prefab")]
        public GameObject rollingLogPrefab;

        [Header("Path sampling")]
        public bool useSmoothing = true;
        [Min(1)] public int smoothingSubdivisionsPerSegment = 6;

        [Header("Spawn timing")]
        public bool enableSpawning = true;
        [Min(0.5f)] public float minSpawnIntervalSeconds = 4f;
        [Min(0.5f)] public float maxSpawnIntervalSeconds = 11f;
        [Min(1)] public int maxActiveLogs = 4;

        [Header("Direction")]
        public bool allowBothTravelDirections = false;
        [Range(0f, 1f)] public float towardPlayerDirectionWeight = 0.65f;

        [Header("Toward player (spawn ahead, roll backward)")]
        [Min(5f)] public float towardPlayerSpawnMinAhead = 35f;
        [Min(5f)] public float towardPlayerSpawnMaxAhead = 95f;

        [Header("With player (spawn behind, roll forward)")]
        [Min(5f)] public float withPlayerSpawnMinBehind = 25f;
        [Min(5f)] public float withPlayerSpawnMaxBehind = 80f;

        [Header("Speed along path (m/s magnitude)")]
        public Vector2 speedRange = new Vector2(6f, 14f);

        [Header("Lateral placement")]
        [Range(0f, 1f)] public float lateralFraction = 0.92f;
        public float edgeInnerMargin = 0.12f;

        [Header("Raycast (spawn snap)")]
        public LayerMask roadLayer = ~0;
        public float raycastStartHeight = 6f;
        public float raycastDownDistance = 24f;

        [Header("Progress gate")]
        [Range(0f, 0.95f)] public float minNormalizedProgressToSpawn = 0.02f;
    }

    // =====================================================================
    // CROSS OBSTACLES — mirrors CrossObstacleDirector
    // =====================================================================
    [System.Serializable]
    public class CrossObstacleSettings
    {
        [Tooltip("Uncheck to use the scene CrossObstacleDirector's own values for this trial.")]
        public bool overrideCrossObstacles = false;

        [Header("Prefab")]
        public GameObject crossObstaclePrefab;

        [Header("Spawn Control")]
        public bool enabledSpawning = true;
        public float minPlayerSpeed = 4f;
        [Tooltip("Seconds between cross spawns. X = at track start, Y = at track end (lower Y = more frequent later).")]
        public Vector2 spawnCooldownByProgress = new Vector2(5f, 2f);
        public float minLeadDistance = 15f;
        public float maxLeadDistance = 80f;
        public float maxCurvatureHorizonScale = 0.65f;

        [Header("Cross Speed")]
        [Tooltip("Random cross speed range (m/s). Not progress-based.")]
        public Vector2 crossSpeedRange = new Vector2(5f, 11f);
        [Tooltip("Multiplies cross speed. X = at track start, Y = at track end.")]
        public Vector2 crossSpeedMulByProgress = new Vector2(1f, 1f);

        [Header("Size Scaling")]
        [Tooltip("Random scale range. Not progress-based.")]
        public Vector2 obstacleScaleRange = new Vector2(0.8f, 1.35f);
        [Tooltip("Multiplies chosen scale. X = at track start, Y = at track end.")]
        public Vector2 sizeMulByProgress = new Vector2(1f, 1f);

        [Header("Yaw / Accuracy")]
        [Tooltip("Aim yaw error in degrees. X = at track start, Y = at track end.")]
        public Vector2 yawErrorDegreesByProgress = new Vector2(22f, 4f);
        [Tooltip("Miss blend 0–1 (higher = more miss). X = at track start, Y = at track end.")]
        public Vector2 missAmountByProgress = new Vector2(0.6f, 0.05f);

        [Header("Spawn Cooldown Randomness")]
        public Vector2 spawnCooldownRandomRange = new Vector2(0.8f, 1.2f);

        [Header("Spawn Randomness")]
        public Vector2 initialSpawnDelayRange = new Vector2(1.5f, 4.5f);
        public bool useActualPathLengthForPrediction = true;

        [Header("Cross Path Length")]
        public float offRoadPadding = 12f;

        [Header("Ramp Avoidance")]
        [Min(0f)] public float avoidRampRadius = 4f;
        public LayerMask rampCheckLayers = ~0;

        [Header("Curvature Sampling")]
        public float curvatureSampleLength = 12f;
        public float highCurvatureThreshold = 0.35f;

        [Header("Yaw Impact (Weighted Miss Variety)")]
        public bool enableYawWeightedMisses = true;
        public float yawSpeedImpactMax = 0.4f;
        public float yawDistanceImpactMax = 12f;
        public float yawAngleAmplifyMax = 0.6f;

        [Header("Debug")]
        public bool debugGizmos = false;
        public bool verboseLog = false;
    }

    // =====================================================================
    // ICE PATHS — mirrors IcePathSpawner (full-width icy road sections)
    // =====================================================================
    [System.Serializable]
    public class IcePathSettings
    {
        [Tooltip("Uncheck to use the scene IcePathSpawner's own values for this trial.")]
        public bool overrideIcePaths = false;

        [Header("Prefab / Material")]
        [Tooltip("Optional template for GroundSurface / IcePath values. Visual is generated as a full-width road mesh.")]
        public GameObject iceSegmentPrefab;
        public Material iceMaterial;

        [Header("Spawn Mode")]
        public bool preSpawnOnInitialize = true;
        public bool streamSpawnDuringRun = true;

        [Header("Path Sampling")]
        public bool useSmoothing = true;
        [Min(1)] public int smoothingSubdivisionsPerSegment = 6;

        [Header("Spawn Settings")]
        public float sectionSpacing = 80f;
        [Tooltip("Max ice sections spawned this run. Despawning does not free budget. 0 = unlimited.")]
        [Min(0)] public int maxActiveSections = 8;
        public float minSpawnDistanceAhead = 40f;
        public float maxSpawnDistanceAhead = 220f;

        [Header("Initial Pre-Spawn")]
        public float initialPreSpawnDistance = 250f;
        public float despawnBehindDistance = 30f;

        [Header("Randomization")]
        public float distanceJitter = 12f;
        [Tooltip("Chance to spawn an ice section (0–1). X = at track start, Y = at track end.")]
        public Vector2 spawnChanceByProgress = new Vector2(0.4f, 0.55f);

        [Header("Ice Section Shape")]
        [Tooltip("Along-track length of each full-width icy patch (meters).")]
        [Min(4f)] public float sectionLength = 28f;
        [Min(0f)] public float sectionLengthJitter = 8f;
        [Tooltip("1 = exact road width. Slightly above 1 covers road edges.")]
        [Range(0.9f, 1.15f)] public float roadWidthScale = 1.02f;

        [Header("Raycast")]
        public LayerMask roadLayer = ~0;
        public float raycastStartHeight = 6f;
        public float raycastDownDistance = 40f;
        public float iceHeightOffset = 0.02f;

        [Header("Ice Mesh")]
        [Min(0.25f)] public float iceSampleSpacing = 1.0f;
        public float iceUVTiling = 0.15f;
        public bool addIceMeshColliderTrigger = true;

        [Header("Timing")]
        public float updateInterval = 0.5f;

        [Header("Debug")]
        public bool verboseDebug = false;
    }

    // =====================================================================
    // BOUNCE OBSTACLES — mirrors BounceBackObstacleSpawner
    // =====================================================================
    [System.Serializable]
    public class BounceObstacleSettings
    {
        [Tooltip("Uncheck to use the scene BounceBackObstacleSpawner's own values for this trial.")]
        public bool overrideBounceObstacles = false;

        [Header("Prefab")]
        public GameObject bounceBackPrefab;

        [Header("Path Sampling")]
        public bool useSmoothing = true;
        [Min(1)] public int smoothingSubdivisionsPerSegment = 6;

        [Header("Spawn Timing")]
        public bool enableSpawning = true;
        [Min(0.5f)] public float minSpawnIntervalSeconds = 5f;
        [Min(0.5f)] public float maxSpawnIntervalSeconds = 12f;
        [Min(1)] public int maxActive = 6;

        [Header("Placement")]
        [Min(5f)] public float minSpawnDistanceAhead = 40f;
        [Min(5f)] public float maxSpawnDistanceAhead = 120f;
        [Min(0.5f)] public float obstacleSpacing = 35f;
        [Range(0f, 1f)] public float spawnChancePerSlot = 0.55f;
        [Range(0f, 1f)] public float lateralFraction = 0.6f;
        public float edgeInnerMargin = 0.5f;
        public float distanceJitter = 10f;

        [Header("Raycast")]
        public LayerMask roadLayer = ~0;
        public float raycastStartHeight = 6f;
        public float raycastDownDistance = 24f;

        [Header("Progress Gate")]
        [Range(0f, 0.95f)] public float minNormalizedProgressToSpawn = 0.02f;
    }

    // =====================================================================
    // SPAWN QUEUE — mirrors TrackSpawnerQueue playback (not scene entry refs)
    // =====================================================================
    [System.Serializable]
    public class SpawnQueueSettings
    {
        [Tooltip("Uncheck to leave the scene TrackSpawnerQueue untouched for this trial.")]
        public bool overrideSpawnQueue = true;

        [Header("Playback")]
        public bool enableQueue = true;
        public bool takeoverAutonomousSpawning = true;
        public TrackSpawnerQueue.PlaybackMode playbackMode = TrackSpawnerQueue.PlaybackMode.Sequential;
        public bool loop = true;

        [Header("Timing")]
        [Min(0f)] public float startDelay = 3f;
        [Min(0.05f)] public float intervalSeconds = 5f;
        public Vector2 intervalJitter = new Vector2(1f, 3f);
        public bool scaleJitterByProgress = true;
        public Vector2 intervalJitterAtFullProgress = new Vector2(2f, 4f);
        [Min(0.05f)] public float failedSpawnRetryDelay = 0.35f;
        [Min(0f)] public float waveStaggerSeconds = 0.15f;

        [Header("Gate")]
        [Range(0f, 1f)] public float minNormalizedProgress = 0f;
    }
}
