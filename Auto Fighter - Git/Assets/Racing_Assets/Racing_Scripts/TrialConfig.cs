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

    [Tooltip("Skills available on this track/trial (e.g. Max Fuel, Drift). Ignored when Restrict Skills To Allowlist is off.")]
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
            new TrackCoinSpawner.CoinTypeWeight { coinType = CoinType.Bronze, enabled = true, globalScale = 1.12f },
            new TrackCoinSpawner.CoinTypeWeight { coinType = CoinType.Silver, enabled = true, globalScale = 0.95f },
            new TrackCoinSpawner.CoinTypeWeight { coinType = CoinType.Gold, enabled = true, globalScale = 0.8f },
            new TrackCoinSpawner.CoinTypeWeight { coinType = CoinType.Platinum, enabled = true, globalScale = 0.4f },
            new TrackCoinSpawner.CoinTypeWeight { coinType = CoinType.Diamond, enabled = true, globalScale = 0.15f },
            new TrackCoinSpawner.CoinTypeWeight { coinType = CoinType.Legendary, enabled = true, globalScale = 0.05f }
        };

        [Header("Spawn Layout")]
        [Min(0.1f)] public float coinSpacing = 6f;
        public int maxActiveCoins = 120;
        public float minSpawnDistanceAhead = 40f;
        public float maxSpawnDistanceAhead = 140f;
        public float despawnBehindDistance = 25f;
        public float initialPreSpawnDistance = 80f;

        [Header("Spawn Probability")]
        [Range(0f, 1f)] public float baseSpawnChance = 0.85f;
        public AnimationCurve spawnChanceDistanceCurve = AnimationCurve.Linear(0, 1, 1, 1);
        public AnimationCurve lateTrackSpawnBonusCurve = new AnimationCurve(
            new Keyframe(0f, 1f),
            new Keyframe(0.8f, 1f),
            new Keyframe(1f, 1.15f)
        );

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
        [Range(0f, 1f)] public float baseAccuracy = 0.10f;
        public AnimationCurve accuracyByDistance = AnimationCurve.Linear(0, 1, 1, 1);
        [Min(0f)] public float maxMissLateral = 4f;
        [Min(0f)] public float maxMissForward = 6f;

        [Header("Explosion Frequency")]
        [Range(0f, 1f)] public float explosionBaseChance = 0.06f;
        public AnimationCurve explosionChanceByDistance = AnimationCurve.Linear(0, 0.5f, 1, 1.5f);

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
        public float spawnCooldownSeconds = 5f;
        public float minLeadDistance = 15f;
        public float maxLeadDistance = 80f;
        public float maxCurvatureHorizonScale = 0.65f;

        [Header("Cross Speed")]
        public Vector2 crossSpeedRange = new Vector2(5f, 11f);
        public AnimationCurve crossSpeedMultiplierCurve = AnimationCurve.Linear(0, 1, 1, 1);

        [Header("Size Scaling")]
        public Vector2 obstacleScaleRange = new Vector2(0.8f, 1.35f);
        public AnimationCurve sizeCurve = AnimationCurve.Linear(0, 1, 1, 1);

        [Header("Yaw / Accuracy")]
        public AnimationCurve yawErrorDegreesCurve = AnimationCurve.Linear(0, 22, 1, 4);
        public AnimationCurve accuracyCurve = AnimationCurve.Linear(0, 0.6f, 1, 0.05f);

        [Header("Spawn Interval Scaling")]
        public AnimationCurve spawnIntervalCurve = AnimationCurve.Linear(0, 1f, 1, 0.4f);

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
}
