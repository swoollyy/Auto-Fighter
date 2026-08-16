using System;
#if UNITY_EDITOR
using UnityEditor;
#endif
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;

using Debug = UnityEngine.Debug;
using Random = UnityEngine.Random;
using Object = UnityEngine.Object;

[RequireComponent(typeof(Transform))]
public class ProceduralTrackGenerator : MonoBehaviour
{
    // 2D helper (XZ plane)
    private struct Segment2D
    {
        public Vector2 a;
        public Vector2 b;

        public Segment2D(Vector2 a, Vector2 b)
        {
            this.a = a;
            this.b = b;
        }
    }

#if UNITY_EDITOR
    [CustomEditor(typeof(ProceduralTrackGenerator))]
    public class ProceduralTrackGeneratorEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            ProceduralTrackGenerator gen = (ProceduralTrackGenerator)target;

            GUILayout.Space(8);
            GUI.backgroundColor = Color.green;

            if (GUILayout.Button("GENERATE TRACK (Manual Only)", GUILayout.Height(30)))
            {
                EditorApplication.delayCall += () =>
                {
                    if (gen != null)
                        gen.GenerateTrack();
                };
            }

            GUI.backgroundColor = Color.white;
        }
    }
#endif

    // ================================================================
    //  TRACK PARAMETERS
    // ================================================================
    [Header("Track Segment Settings")]
    [SerializeField] private GameObject segmentPrefab;
    [SerializeField] private int segmentCount = 200;
    public int SegmentCount => segmentCount;

    [Header("Segment Length")]
    [SerializeField] private bool autoDetectSegmentLength = true;
    [SerializeField] private float segmentLength = 10f;

    [Header("Turn Tightness (Difficulty)")]
    [Tooltip("Min turn angle. Max turn lerps from Start → End over track progress.")]
    [SerializeField] private float minTurnAngle = 5f;
    [SerializeField] private float startMaxTurnAngle = 10f;
    [SerializeField] private float endMaxTurnAngle = 40f;
    [SerializeField] private AnimationCurve difficultyCurve = AnimationCurve.Linear(0, 0, 1, 1);

    [Header("Turn Frequency")]
    [Tooltip("Chance of turning each segment. Lerps from Start → End over track progress.")]
    [Range(0f, 1f)][SerializeField] private float startTurnChance = 0.35f;
    [Range(0f, 1f)][SerializeField] private float endTurnChance = 0.85f;
    [SerializeField] private AnimationCurve turnFrequencyCurve = AnimationCurve.Linear(0, 0, 1, 1);

    [Header("Small Wiggles")]
    [Tooltip("Maximum small wiggle angle added every segment.")]
    [SerializeField] private float maxWiggleAngle = 3f;
    [Tooltip("How much the wiggle grows over distance (0 = constant, 1 = full over distance).")]
    [Range(0f, 1f)][SerializeField] private float wiggleOverDistance = 0.4f;

    [Header("Avoidance (Turn Away From Existing Track)")]
    [SerializeField] private bool useAvoidance = true;
    [Tooltip("How far ahead/around we 'sense' existing track to avoid it.")]
    [SerializeField] private float avoidanceRadius = 40f;
    [Tooltip("Overall strength of avoidance steering (0 = off, 1 = full).")]
    [Range(0f, 2f)][SerializeField] private float avoidanceStrength = 0.7f;
    [Tooltip("Higher = more weight to close segments vs far ones.")]
    [Range(0.1f, 4f)][SerializeField] private float avoidanceFalloff = 1.0f;

    [Header("Global Track Direction")]
    [SerializeField] private bool constrainToGlobalDirection = true;
    [Range(0f, 1f)][SerializeField] private float globalAlignmentStrength = 0.3f;
    [Tooltip("Hard cap on how far away from the starting forward we can aim.")]
    [SerializeField] private float maxHeadingDeviation = 110f;

    public enum TerrainCorner
    {
        Random = 0,
        SouthWest = 1,
        SouthEast = 2,
        NorthWest = 3,
        NorthEast = 4
    }

    [Header("Preferred Terrain (stay on the active tile)")]
    [Tooltip("Main terrain the track should start on and stay on. If empty, uses the single active terrain.")]
    [SerializeField] private Terrain preferredTerrain;

    [Tooltip("Start generation at an inset corner of the preferred terrain, facing inward.")]
    [SerializeField] private bool startAtPreferredTerrainCorner = true;

    [SerializeField] private TerrainCorner preferredStartCorner = TerrainCorner.Random;

    [Tooltip("Legacy meter inset used for stay-on-terrain padding. Start placement uses the corner fraction below.")]
    [SerializeField, Min(5f)] private float preferredTerrainEdgeInset = 55f;

    [Tooltip("How far from the chosen corner toward the terrain center to start (fraction of terrain size). 0.25 = 25% in from the corner.")]
    [SerializeField, Range(0.05f, 0.45f)] private float preferredStartCornerInsetFraction = 0.25f;

    [Tooltip("Keep the track on the preferred terrain. Retries when a stay-on-tile path can't be found.")]
    [SerializeField] private bool preferStayOnPreferredTerrain = true;

    [Tooltip("If true, never leave the preferred terrain — abort and retry instead of spilling onto neighbors.")]
    [SerializeField] private bool hardClampToPreferredTerrain = true;

    [Tooltip("When the road nears the terrain border, peel inward instead of sliding along the edge. 0 = off.")]
    [SerializeField, Range(0f, 1f)] private float preferredTerrainStayBias = 0.85f;

    [Tooltip("Begin peeling away from the border when this close to the preferred-terrain inset boundary.")]
    [SerializeField, Min(5f)] private float preferredTerrainEdgeSteerMeters = 120f;

    [Tooltip("Extra XZ margin so the road width (not just the centerline) stays inside the preferred terrain.")]
    [SerializeField, Min(0f)] private float preferredTerrainRoadHalfPadding = 1.5f;

    [Header("Advanced Steering (scored yaw picker)")]
    [Tooltip("How many yaw candidates to score each segment across ±maxTurn.")]
    [SerializeField, Min(12)] private int advancedYawSamples = 48;

    [Tooltip("Extra random yaw probes per segment for variety.")]
    [SerializeField, Min(0)] private int advancedRandomProbes = 8;

    [Tooltip("Cost for ending a segment near the terrain edge.")]
    [SerializeField, Min(0f)] private float edgeProximityPenalty = 2.4f;

    [Tooltip("Extra cost for moving closer to the edge (vs current position).")]
    [SerializeField, Min(0f)] private float edgeApproachPenalty = 3.5f;

    [Tooltip("Distance from inset edge where edge penalties / center-seeking ramp up.")]
    [SerializeField, Min(10f)] private float edgeComfortZoneMeters = 140f;

    [Tooltip("Reward for aiming toward terrain center when near an edge.")]
    [SerializeField, Min(0f)] private float centerSeekReward = 4.0f;

    [Tooltip("Soft clearance from existing track. Closer than this is heavily penalized when scoring yaws.")]
    [SerializeField, Min(1f)] private float softTrackClearanceMeters = 22f;

    [Tooltip("Penalty strength for soft clearance violations (higher = less folding into yourself).")]
    [SerializeField, Min(0f)] private float softProximityPenalty = 4.5f;

    [Tooltip("Extra soft look-ahead distance for avoiding headings that aim into existing track.")]
    [SerializeField, Min(0f)] private float foldAvoidLookAheadMeters = 40f;

    [Tooltip("How strongly to punish aiming toward nearby existing road (anti fold-in).")]
    [SerializeField, Min(0f)] private float foldInAimPenalty = 6.0f;

    [Tooltip("How strongly to follow the turn/wiggle intent when safe.")]
    [SerializeField, Min(0f)] private float turnIntentFollowWeight = 1.15f;

    [Tooltip("Small reward for keeping the same turn sign (flowing curves).")]
    [SerializeField, Min(0f)] private float turnPersistenceReward = 0.85f;

    [Tooltip("Mild reward for non-zero turn so the road stays dynamic.")]
    [SerializeField, Min(0f)] private float curveEnergyReward = 0.45f;

    [Tooltip("Penalty for huge yaw jumps vs previous committed turn (smoothness).")]
    [SerializeField, Min(0f)] private float yawJerkPenalty = 0.02f;

    [Tooltip("Mild reward for long-range travel direction. Keep modest so curves still happen.")]
    [SerializeField, Min(0f)] private float flowProgressWeight = 2.2f;

    [Tooltip("Penalty for reversing into yourself (paperclip folds). Only strong when near existing road.")]
    [SerializeField, Min(0f)] private float reverseHeadingPenalty = 4.0f;

    [Tooltip("Hard-reject anti-parallel switchbacks closer than this many road widths (centerline).")]
    [SerializeField, Min(1.5f)] private float parallelRejectRoadWidths = 2.4f;


    [SerializeField] private TerrainDetailGrassPainter grassPainter;

    [Tooltip("Optional: hills around the track while keeping terrain below the flat road plane (see TerrainAroundFlatRoad). Runs after road mesh, before grass.")]
    [SerializeField] private TerrainAroundFlatRoad terrainAroundFlatRoad;

    [Header("Randomness")]
    [SerializeField] private bool useRandomSeed = true;
    [SerializeField] private int fixedSeed = 12345;

    [Header("Road Mesh")]
    [SerializeField] private bool generateRoadMesh = true;
    [SerializeField] private float roadWidth = 4f;
    [SerializeField] private Material roadMaterial;
    [SerializeField] private float uvTiling = 0.1f;
    [Tooltip("Use segment junction points (where turns happen) for the visual road mesh instead of segment centers. Keeps lane markings aligned at segment joins.")]
    [SerializeField] private bool meshFromSegmentJunctions = true;

    [Header("Path Smoothing (Visual Only)")]
    [SerializeField] private bool useSmoothing = false;
    [SerializeField] private int smoothingSubdivisionsPerSegment = 6;

    [Header("Self-Intersection Avoidance")]
    [Tooltip("If true, we strictly avoid overlapping / intersecting track.")]
    [SerializeField] private bool preventSelfIntersections = true;

    [Tooltip("How many yaw samples we try around the preferred yaw to avoid intersections.")]
    [SerializeField] private int yawSearchSamples = 24;

    [Tooltip("Extra padding on top of half the road width when checking for self-collision.")]
    [SerializeField] private float collisionPadding = 0.5f;

    [Tooltip("Multiplier for how much of half roadWidth counts as collision radius.")]
    [SerializeField] private float trackRadiusMultiplier = 1.3f;

    [Tooltip("DEPRECATED for hard collision — hard checks always skip only the previous connected segment. Kept for serialization.")]
    [SerializeField] private int recentIgnoreCount = 0;

    [Tooltip("Minimum centerline separation for parallel stretches, as a multiple of roadWidth. 1.0 = edges touching, 2.0 = one road-width of dirt between them.")]
    [SerializeField, Min(1f)] private float minSelfClearanceRoadWidths = 2.0f;

    [Tooltip("Extra meters added on top of minSelfClearanceRoadWidths * roadWidth for parallel / nearby stretches.")]
    [SerializeField, Min(0f)] private float minSelfClearanceExtraMeters = 4f;

    [Tooltip("How many extra straight segments to simulate ahead when validating a yaw (catches fold-ins early).")]
    [SerializeField, Range(0, 4)] private int selfCollisionLookAheadSegments = 2;

    [Header("Start / End Separation")]
    [Tooltip("Reject tracks whose finish sits too close to the start (XZ), so players can't cut offroad to the end.")]
    [SerializeField] private bool enforceMinStartEndSeparation = true;

    [Tooltip("Absolute minimum planar (XZ) distance between start and FINISH. Combined with the path-fraction rule below (whichever is larger).")]
    [SerializeField] private float minStartEndDistance = 120f;

    [Tooltip("Also require finish at least this fraction of total path length away from start on XZ.")]
    [Range(0.05f, 0.75f)]
    [SerializeField] private float minStartEndDistancePathFraction = 0.28f;

    [Tooltip("Anti-shortcut keep-out around the start / early road (meters). Soft-scored during build; not a hard reject (hard rejects caused 1000+ dead-ends).")]
    [SerializeField, Min(0f)] private float startRegionKeepOutMeters = 45f;

    [Tooltip("After this fraction of the track is built, soft-penalize (not hard-reject) entering the start-region keep-out.")]
    [Range(0.05f, 0.5f)]
    [SerializeField] private float startRegionKeepOutAfterNormalized = 0.15f;

    [Tooltip("Soft-penalize later road near the first portion of the path (fraction of total segments).")]
    [Range(0.05f, 0.45f)]
    [SerializeField] private float startKeepOutEarlyPathFraction = 0.18f;

    [Tooltip("How strongly the yaw scorer prefers staying away from the start region and finishing far (soft guide).")]
    [SerializeField, Min(0f)] private float startSeparationSoftWeight = 5.0f;

    [Tooltip("Legacy serialized field.")]
    [SerializeField, HideInInspector] private float startSeparationEnforceAfterNormalized = 1f;

    [Tooltip("Legacy serialized field.")]
    [SerializeField, HideInInspector] private bool hardRejectStartProximityDuringBuild = false;

    [Header("Generation Retries")]
    [Tooltip("How many BuildTrack attempts to run back-to-back before yielding a frame (keeps the game responsive).")]
    [SerializeField, Min(1)] private int attemptsPerBurst = 5;
    [Tooltip("If true, GenerateTrackCo keeps bursting until a valid track is found (never fails the load for soft validation).")]
    [SerializeField] private bool keepRetryingUntilSuccess = true;
    [Tooltip("Used by sync GenerateTrack / when keepRetryingUntilSuccess is false.")]
    [SerializeField, Min(1)] private int syncMaxRetries = 50;
    [Tooltip("After this many failed attempts, lightly relax collision / start-end rules for subsequent tries (restored after each attempt).")]
    [SerializeField, Min(1)] private int softRelaxAfterAttempts = 25;
    [Tooltip("After this many failed attempts, more aggressively relax rules so generation can still finish.")]
    [SerializeField, Min(1)] private int hardRelaxAfterAttempts = 80;
    [Tooltip("Safety cap even when keepRetryingUntilSuccess is on (0 = no cap).")]
    [SerializeField, Min(0)] private int absoluteMaxAttempts = 0;

    // Expose raw path centers
    public List<Vector3> PathPoints => _pathPoints;

    public float RoadWidth => roadWidth;

    public bool LastGenerateSucceeded { get; private set; }
    /// <summary>Total BuildTrack attempts used by the last GenerateTrack / GenerateTrackCo call.</summary>
    public int LastGenerationAttempts { get; private set; }
    /// <summary>Loading-friendly status for the current/last generation pass.</summary>
    public string GenerationStatusMessage { get; private set; } = "Generating track...";

    // ------------------------------------------------------------------------
    // Per-trial config (TrialConfig). ApplyConfig copies a trial's TrackSettings
    // into these fields (call BEFORE GenerateTrackCo so BuildTrack reads them).
    // CaptureConfig does the reverse, used by the editor baker to snapshot the
    // current scene setup into a TrialConfig asset.
    // ------------------------------------------------------------------------
    public void ApplyConfig(TrialConfig.TrackSettings s)
    {
        if (s == null || !s.overrideTrack) return;

        // Object refs: null means "keep whatever the generator already has".
        if (s.segmentPrefab != null) segmentPrefab = s.segmentPrefab;
        if (s.roadMaterial != null) roadMaterial = s.roadMaterial;

        segmentCount = s.segmentCount;
        autoDetectSegmentLength = s.autoDetectSegmentLength;
        segmentLength = s.segmentLength;

        minTurnAngle = s.minTurnAngle;
        startMaxTurnAngle = s.startMaxTurnAngle;
        endMaxTurnAngle = s.endMaxTurnAngle;

        startTurnChance = s.startTurnChance;
        endTurnChance = s.endTurnChance;

        maxWiggleAngle = s.maxWiggleAngle;
        wiggleOverDistance = s.wiggleOverDistance;

        useAvoidance = s.useAvoidance;
        avoidanceRadius = s.avoidanceRadius;
        avoidanceStrength = s.avoidanceStrength;
        avoidanceFalloff = s.avoidanceFalloff;

        constrainToGlobalDirection = s.constrainToGlobalDirection;
        globalAlignmentStrength = s.globalAlignmentStrength;
        maxHeadingDeviation = s.maxHeadingDeviation;

        useRandomSeed = s.useRandomSeed;
        fixedSeed = s.fixedSeed;

        generateRoadMesh = s.generateRoadMesh;
        roadWidth = s.roadWidth;
        uvTiling = s.uvTiling;

        useSmoothing = s.useSmoothing;
        smoothingSubdivisionsPerSegment = s.smoothingSubdivisionsPerSegment;

        preventSelfIntersections = s.preventSelfIntersections;
        yawSearchSamples = s.yawSearchSamples;
        collisionPadding = s.collisionPadding;
        trackRadiusMultiplier = s.trackRadiusMultiplier;
        recentIgnoreCount = s.recentIgnoreCount;

        enforceMinStartEndSeparation = s.enforceMinStartEndSeparation;
        minStartEndDistance = s.minStartEndDistance;
        minStartEndDistancePathFraction = s.minStartEndDistancePathFraction;
        startSeparationEnforceAfterNormalized = s.startSeparationEnforceAfterNormalized;
        startRegionKeepOutMeters = s.startRegionKeepOutMeters;
        startRegionKeepOutAfterNormalized = s.startRegionKeepOutAfterNormalized;
        startKeepOutEarlyPathFraction = s.startKeepOutEarlyPathFraction;
    }

    public TrialConfig.TrackSettings CaptureConfig()
    {
        return new TrialConfig.TrackSettings
        {
            overrideTrack = true,
            segmentPrefab = segmentPrefab,
            segmentCount = segmentCount,
            autoDetectSegmentLength = autoDetectSegmentLength,
            segmentLength = segmentLength,
            minTurnAngle = minTurnAngle,
            startMaxTurnAngle = startMaxTurnAngle,
            endMaxTurnAngle = endMaxTurnAngle,
            startTurnChance = startTurnChance,
            endTurnChance = endTurnChance,
            maxWiggleAngle = maxWiggleAngle,
            wiggleOverDistance = wiggleOverDistance,
            useAvoidance = useAvoidance,
            avoidanceRadius = avoidanceRadius,
            avoidanceStrength = avoidanceStrength,
            avoidanceFalloff = avoidanceFalloff,
            constrainToGlobalDirection = constrainToGlobalDirection,
            globalAlignmentStrength = globalAlignmentStrength,
            maxHeadingDeviation = maxHeadingDeviation,
            useRandomSeed = useRandomSeed,
            fixedSeed = fixedSeed,
            generateRoadMesh = generateRoadMesh,
            roadWidth = roadWidth,
            roadMaterial = roadMaterial,
            uvTiling = uvTiling,
            useSmoothing = useSmoothing,
            smoothingSubdivisionsPerSegment = smoothingSubdivisionsPerSegment,
            preventSelfIntersections = preventSelfIntersections,
            yawSearchSamples = yawSearchSamples,
            collisionPadding = collisionPadding,
            trackRadiusMultiplier = trackRadiusMultiplier,
            recentIgnoreCount = recentIgnoreCount,
            enforceMinStartEndSeparation = enforceMinStartEndSeparation,
            minStartEndDistance = minStartEndDistance,
            minStartEndDistancePathFraction = minStartEndDistancePathFraction,
            startSeparationEnforceAfterNormalized = startSeparationEnforceAfterNormalized,
            startRegionKeepOutMeters = startRegionKeepOutMeters,
            startRegionKeepOutAfterNormalized = startRegionKeepOutAfterNormalized,
            startKeepOutEarlyPathFraction = startKeepOutEarlyPathFraction,
        };
    }

    /// <summary>Centerline used for the road mesh (junction points + optional smoothing).</summary>
    public void FillRoadMeshCenterPath(List<Vector3> dst)
    {
        dst.Clear();
        List<Vector3> rawPath = SelectMeshSourcePath();
        if (rawPath == null || rawPath.Count < 2) return;

        List<Vector3> path = useSmoothing
            ? GenerateSmoothedPath(rawPath, smoothingSubdivisionsPerSegment)
            : new List<Vector3>(rawPath);

        // Match BuildRoadMeshFromPath so scripted movers raycast/snapped to the same centerline as the collider mesh.
        path = DedupePathPoints(path);
        dst.AddRange(path);
    }

    // Fired when a full, valid track (no abort) is finished generating (after retries).
    public event Action<ProceduralTrackGenerator> OnTrackGeneratedSuccessfully;

    // ================================================================
    //  INTERNALS
    // ================================================================
    private MeshFilter _meshFilter;
    private MeshRenderer _meshRenderer;
    private MeshCollider _meshCollider;

    private readonly List<Transform> _spawnedSegments = new List<Transform>();
    private readonly List<Vector3> _pathPoints = new List<Vector3>();
    /// <summary>Segment start/end corners used for the road mesh (turns occur at these points).</summary>
    private readonly List<Vector3> _junctionPathPoints = new List<Vector3>();
    private readonly List<Segment2D> _segments2D = new List<Segment2D>();

    private Vector3 _currentEndPosition;
    private Quaternion _currentRotation;

    private float _currentTurnDirectionSign;
    private bool _hasInitializedHeading = false;
    private Vector3 _globalForwardRef;
    private Vector3 _globalHeadingSmoothed;
    private float _lastCommittedYaw;

    private bool _abortedGeneration = false;
    private string _lastBuildFailReason = "";
    private int _stuckRelaxLevel = 0; // 0=strict, 1=no look-ahead, 2=loose clearance, 3=may leave terrain

    private Vector3 _trackStartPosition;
    private bool _hasPreferredTerrainBounds;
    private float _preferredMinX, _preferredMaxX, _preferredMinZ, _preferredMaxZ;
    private Vector2 _preferredCenterXZ;
    private Vector2 _flowGoalXZ;
    private Vector2 _recentFlowHeadingXZ = Vector2.up;
    private readonly List<Vector2> _guideWaypoints = new List<Vector2>(16);
    private int _guideWaypointIndex;
    private float _arcYawRate;

    // For noise-based wiggle
    private float _noiseOffset;

    // Add these fields near the top of the class:
    [HideInInspector] public Vector2 skidMinXZ;
    [HideInInspector] public Vector2 skidInvSizeXZ;

    // ================================================================
    //  RUNTIME QUICK TEST KEYS
    // ================================================================
    private void Update()
    {
        if (!Application.isPlaying) return;

        if (Input.GetKeyDown(KeyCode.G))
        {
            RunMultipleGenerations(1);
        }
        else if (Input.GetKeyDown(KeyCode.H))
        {
            RunMultipleGenerations(5);
        }
        else if (Input.GetKeyDown(KeyCode.J))
        {
            RunMultipleGenerations(10);
        }
    }

    private void RunMultipleGenerations(int iterations)
    {
        for (int i = 0; i < iterations; i++)
        {
            GenerateTrack();
        }

        Debug.Log($"[ProceduralTrackGenerator] Finished {iterations} generation(s).");
    }

    // ================================================================
    //  PUBLIC ENTRY (WITH RETRIES)
    // ================================================================
    public void GenerateTrack(int maxRetries = -1)
    {
        if (maxRetries < 0)
            maxRetries = syncMaxRetries;

        bool success = TryBuildTrackWithRetriesSync(maxRetries);
        LastGenerateSucceeded = success;

        if (success)
        {
            terrainAroundFlatRoad?.ApplyFromTrackSync(this);
            // Grass is applied once by GameManager after spawn/physics sync.
            OnTrackGeneratedSuccessfully?.Invoke(this);
        }
    }

    /// <summary>
    /// Async generation: retries in small bursts and yields between bursts so the game stays responsive.
    /// When <see cref="keepRetryingUntilSuccess"/> is on, keeps going until a valid track exists.
    /// </summary>
    public IEnumerator GenerateTrackCo(int maxRetries = -1)
    {
        bool unlimited = keepRetryingUntilSuccess && maxRetries < 0;
        int cap = maxRetries >= 0 ? maxRetries : (unlimited ? int.MaxValue : syncMaxRetries);

        IEnumerator buildCo = CoTryBuildTrackWithRetries(cap, unlimited);
        while (buildCo.MoveNext())
            yield return buildCo.Current;

        if (LastGenerateSucceeded)
        {
            float terrainMargin = 40f;
            var overlapTerrains = new List<Terrain>(16);
            int planeCount = TrackTerrainOverlap.CollectFromTrack(this, terrainMargin, overlapTerrains);
            GenerationStatusMessage = planeCount > 0
                ? $"Sculpting hills near road ({planeCount} planes)..."
                : "Sculpting hills near road...";
            yield return null;

            if (terrainAroundFlatRoad != null && terrainAroundFlatRoad.SpreadWorkAcrossFrames)
            {
                IEnumerator sculpt = terrainAroundFlatRoad.ApplyFromTrackAsync(this);
                while (sculpt.MoveNext())
                    yield return sculpt.Current;
            }
            else
                terrainAroundFlatRoad?.ApplyFromTrackSync(this);

            if (terrainAroundFlatRoad != null && terrainAroundFlatRoad.LastSculptedTerrainCount > 0)
                GenerationStatusMessage = $"Terrain ready ({terrainAroundFlatRoad.LastSculptedTerrainCount} planes).";

            // Grass is applied once by GameManager after spawn/physics sync (avoids double paint).
            OnTrackGeneratedSuccessfully?.Invoke(this);
            GenerationStatusMessage = "Track ready.";
        }
        else
        {
            GenerationStatusMessage = "Track generation failed.";
        }
    }

    private bool TryBuildTrackWithRetriesSync(int maxRetries)
    {
        CacheMeshComponents();
        CaptureRelaxationBaseline();

        bool success = false;
        int attempt = 0;
        LastGenerationAttempts = 0;

        while (!success && attempt < maxRetries)
        {
            attempt++;
            LastGenerationAttempts = attempt;
            GenerationStatusMessage = $"Generating track (attempt {attempt})...";

            ApplyRelaxationForAttempt(attempt);
            ClearTrackImmediateSafe();
            _abortedGeneration = false;
            success = BuildTrack();
            RestoreRelaxationBaseline();

            if (!success)
            {
                Debug.LogWarning(
                    $"[ProceduralTrackGenerator] Track generation failed. Retrying ({attempt}/{maxRetries})...");
            }
        }

        LastGenerateSucceeded = success;
        if (!success)
        {
            Debug.LogError(
                "[ProceduralTrackGenerator] Track generation failed after maximum retries.");
        }

        return success;
    }

    private IEnumerator CoTryBuildTrackWithRetries(int maxAttempts, bool unlimited)
    {
        CacheMeshComponents();
        CaptureRelaxationBaseline();

        bool success = false;
        int attempt = 0;
        int burstSize = Mathf.Max(1, attemptsPerBurst);
        LastGenerationAttempts = 0;
        LastGenerateSucceeded = false;
        GenerationStatusMessage = "Generating track...";

        while (!success)
        {
            for (int i = 0; i < burstSize && !success; i++)
            {
                attempt++;
                LastGenerationAttempts = attempt;
                GenerationStatusMessage = $"Generating track (attempt {attempt})...";

                if (!unlimited && attempt > maxAttempts)
                    break;
                if (absoluteMaxAttempts > 0 && attempt > absoluteMaxAttempts)
                {
                    Debug.LogError(
                        $"[ProceduralTrackGenerator] Hit absoluteMaxAttempts ({absoluteMaxAttempts}). Stopping.");
                    unlimited = false;
                    break;
                }

                ApplyRelaxationForAttempt(attempt);
                ClearTrackImmediateSafe();
                _abortedGeneration = false;
                success = BuildTrack();
                RestoreRelaxationBaseline();

                if (!success && (attempt % burstSize == 1 || attempt <= 5 || attempt % 25 == 0))
                {
                    Debug.LogWarning(
                        $"[ProceduralTrackGenerator] Attempt {attempt} failed: {_lastBuildFailReason}. Retrying...");
                }
            }

            if (success)
                break;

            if (!unlimited && attempt >= maxAttempts)
            {
                Debug.LogError(
                    $"[ProceduralTrackGenerator] Track generation failed after {attempt} attempts.");
                break;
            }

            // Breathe so loading UI / iris / input stay alive.
            yield return null;
        }

        LastGenerateSucceeded = success;
        if (success)
            Debug.Log($"[ProceduralTrackGenerator] Track generated successfully after {attempt} attempt(s).");
    }

    private struct RelaxationBaseline
    {
        public int recentIgnoreCount;
        public float trackRadiusMultiplier;
        public float collisionPadding;
        public float minStartEndDistance;
        public float minStartEndDistancePathFraction;
        public int yawSearchSamples;
        public float startSeparationEnforceAfterNormalized;
    }

    private RelaxationBaseline _relaxBaseline;
    private bool _relaxBaselineCaptured;

    private void CaptureRelaxationBaseline()
    {
        _relaxBaseline = new RelaxationBaseline
        {
            recentIgnoreCount = recentIgnoreCount,
            trackRadiusMultiplier = trackRadiusMultiplier,
            collisionPadding = collisionPadding,
            minStartEndDistance = minStartEndDistance,
            minStartEndDistancePathFraction = minStartEndDistancePathFraction,
            yawSearchSamples = yawSearchSamples,
            startSeparationEnforceAfterNormalized = startSeparationEnforceAfterNormalized
        };
        _relaxBaselineCaptured = true;
    }

    private void RestoreRelaxationBaseline()
    {
        if (!_relaxBaselineCaptured) return;
        recentIgnoreCount = _relaxBaseline.recentIgnoreCount;
        trackRadiusMultiplier = _relaxBaseline.trackRadiusMultiplier;
        collisionPadding = _relaxBaseline.collisionPadding;
        minStartEndDistance = _relaxBaseline.minStartEndDistance;
        minStartEndDistancePathFraction = _relaxBaseline.minStartEndDistancePathFraction;
        yawSearchSamples = _relaxBaseline.yawSearchSamples;
        startSeparationEnforceAfterNormalized = _relaxBaseline.startSeparationEnforceAfterNormalized;
    }

    /// <summary>
    /// Temporarily soften start/end separation only — never weaken self-collision
    /// beyond the stuck-rescue path (that was letting roads fold into each other).
    /// </summary>
    private void ApplyRelaxationForAttempt(int attempt)
    {
        if (!_relaxBaselineCaptured) return;

        // Start relaxing end-distance early — this was the #1 whole-attempt reject after packing fixed.
        if (attempt >= 3)
        {
            minStartEndDistancePathFraction = Mathf.Min(minStartEndDistancePathFraction, Mathf.Max(0.12f, _relaxBaseline.minStartEndDistancePathFraction * 0.75f));
            minStartEndDistance = Mathf.Min(minStartEndDistance, Mathf.Max(180f, _relaxBaseline.minStartEndDistance * 0.85f));
            yawSearchSamples = Mathf.Max(yawSearchSamples, _relaxBaseline.yawSearchSamples + 12);
        }

        if (attempt >= 8)
        {
            minStartEndDistance = Mathf.Min(minStartEndDistance, Mathf.Max(120f, _relaxBaseline.minStartEndDistance * 0.65f));
            minStartEndDistancePathFraction = Mathf.Min(minStartEndDistancePathFraction, 0.1f);
            yawSearchSamples = Mathf.Max(yawSearchSamples, 48);
        }

        if (attempt >= 15)
        {
            // Guaranteed progress: accept almost any finish distance rather than spinning forever.
            minStartEndDistance = Mathf.Min(minStartEndDistance, 80f);
            minStartEndDistancePathFraction = 0.05f;
        }
    }

    // ================================================================
    //  CORE TRACK GENERATION
    // ================================================================
    private bool BuildTrack()
    {
        if (segmentPrefab == null)
        {
            Debug.LogError("[ProceduralTrackGenerator] No segmentPrefab assigned!");
            return false;
        }

        // Detect length from prefab, and also use prefab X-size as road width hint if needed
        Renderer r = segmentPrefab.GetComponentInChildren<Renderer>();
        if (autoDetectSegmentLength && r != null)
        {
            segmentLength = r.bounds.size.z;
            if (roadWidth <= 0.01f)
                roadWidth = r.bounds.size.x;
        }

        if (useRandomSeed)
        {
            int seed = Random.Range(int.MinValue, int.MaxValue);
            Random.InitState(seed);
            _noiseOffset = seed * 0.001f;
        }
        else
        {
            Random.InitState(fixedSeed);
            _noiseOffset = fixedSeed * 0.001f;
        }

        _pathPoints.Clear();
        _junctionPathPoints.Clear();
        _spawnedSegments.Clear();
        _segments2D.Clear();
        _abortedGeneration = false;
        _lastBuildFailReason = "";
        _stuckRelaxLevel = 0;

        _currentRotation = transform.rotation;
        _currentEndPosition = transform.position;
        _trackStartPosition = _currentEndPosition;

        _currentTurnDirectionSign = Random.value < 0.5f ? -1f : 1f;

        _hasInitializedHeading = false;
        _globalForwardRef = transform.forward;

        ApplyPreferredTerrainStartPlacement();

        float effLength = Mathf.Max(0.001f, segmentLength);
        bool requireTerrain = ShouldRequirePreferredTerrain();

        for (int i = 0; i < segmentCount && !_abortedGeneration; i++)
        {
            float tNorm = (segmentCount <= 1) ? 0f : (float)i / (segmentCount - 1);

            float difficultyT = difficultyCurve != null ? difficultyCurve.Evaluate(tNorm) : tNorm;
            float maxTurnAngle = Mathf.Lerp(startMaxTurnAngle, endMaxTurnAngle, difficultyT);
            maxTurnAngle = Mathf.Max(minTurnAngle, maxTurnAngle);

            float frequencyT = turnFrequencyCurve != null ? turnFrequencyCurve.Evaluate(tNorm) : tNorm;
            float turnChance = Mathf.Lerp(startTurnChance, endTurnChance, frequencyT);

            float preferredYaw = ComputeTurnYawSimple(i, tNorm, maxTurnAngle, turnChance);

            if (constrainToGlobalDirection)
                preferredYaw = ApplyGlobalDirectionBias(preferredYaw, maxTurnAngle);

            float turnBudget = maxTurnAngle;
            if (requireTerrain)
            {
                turnBudget = GetEdgePeelTurnBudget(maxTurnAngle);
                preferredYaw = ApplyPreferredTerrainStayBias(preferredYaw, turnBudget);
            }

            float chosenYaw = preferredYaw;

            if (preventSelfIntersections || enforceMinStartEndSeparation || requireTerrain)
            {
                if (!TryFindCollisionFreeYaw(preferredYaw, turnBudget, effLength, out chosenYaw))
                {
                    _lastBuildFailReason = requireTerrain
                        ? "no valid segment yaw (terrain, intersection, or start proximity)"
                        : "no valid segment yaw (intersection or start proximity)";
                    _abortedGeneration = true;
                    break;
                }
            }

            CommitSegment(chosenYaw, effLength);
        }

        if (!_abortedGeneration && preventSelfIntersections && !PassesSelfClearancePathCheck())
        {
            _lastBuildFailReason = "path packed too close to itself";
            _abortedGeneration = true;
        }

        if (!_abortedGeneration && requireTerrain && !PassesPreferredTerrainPathCheck())
        {
            _lastBuildFailReason = "left preferred terrain";
            _abortedGeneration = true;
        }

        if (!_abortedGeneration && !PassesStartEndSeparationCheck(effLength))
        {
            _lastBuildFailReason = "finish too close to start";
            _abortedGeneration = true;
        }

        if (!_abortedGeneration && generateRoadMesh)
            BuildRoadMeshFromPath();
        else if (!generateRoadMesh)
        {
            if (_meshFilter != null) _meshFilter.sharedMesh = null;
            if (_meshCollider != null) _meshCollider.sharedMesh = null;
        }

        return !_abortedGeneration && _pathPoints.Count > 1;
    }

    /// <summary>
    /// Builds the track by sampling a smooth Catmull-Rom through large-scale waypoints,
    /// then applying continuous multi-sine heading sway (not Perlin clumps / sawtooth).
    /// </summary>
    private bool TryBuildTrackFromSpline(float segLength)
    {
        string lastFail = "";
        for (int variant = 0; variant < 6; variant++)
        {
            if (variant > 0)
                BuildGuideWaypoints();

            // Always keep real clearance; only drop proximity when deeply stuck.
            bool proximity = _stuckRelaxLevel < 3;
            if (TryBuildSplineVariant(segLength, proximity, out lastFail))
                return true;
        }

        _lastBuildFailReason = string.IsNullOrEmpty(lastFail) ? "spline path rejected" : lastFail;
        return false;
    }

    private bool TryBuildSplineVariant(
        float segLength,
        bool requireProximityClearance,
        out string failReason)
    {
        failReason = "";
        var controls = new List<Vector2>(24);
        Vector2 start = new Vector2(_trackStartPosition.x, _trackStartPosition.z);
        controls.Add(start);

        Vector3 fwd3 = _currentRotation * Vector3.forward;
        Vector2 fwd = new Vector2(fwd3.x, fwd3.z);
        if (fwd.sqrMagnitude < 1e-6f) fwd = Vector2.up;
        fwd.Normalize();
        controls.Add(start + fwd * (segLength * 2.5f));

        for (int i = 0; i < _guideWaypoints.Count; i++)
            controls.Add(_guideWaypoints[i]);

        if (controls.Count < 4)
        {
            failReason = "not enough spline controls";
            return false;
        }

        List<Vector2> dense = SampleCatmullRom(controls, samplesPerSpan: 32);
        List<Vector2> spaced = ResamplePolyline(dense, segLength);
        int needPts = segmentCount + 1;
        if (spaced.Count < 3)
        {
            failReason = "spline resample too short";
            return false;
        }

        if (spaced.Count < needPts)
            ExtendPolylineWithArc(spaced, needPts, segLength);
        while (spaced.Count > needPts)
            spaced.RemoveAt(spaced.Count - 1);

        // Shape macros first, then continuous sine sway (Perlin clumps into "wiggle then flat").
        SoftSmoothPolyline(spaced, passes: 1, strength: 0.22f);
        LimitMaxTurnPerSegment(spaced, segLength);
        ApplyContinuousHeadingWiggle(spaced, segLength);

        if (ShouldRequirePreferredTerrain() && !PolylineStaysOnPreferredTerrain(spaced))
        {
            failReason = "spline left preferred terrain";
            return false;
        }

        if (preventSelfIntersections && !PolylineClearsItself(spaced, requireProximityClearance))
        {
            failReason = "spline self-overlap";
            return false;
        }

        if (enforceMinStartEndSeparation)
        {
            float minSep = GetEffectiveMinStartEndDistance(segLength);
            if (_stuckRelaxLevel >= 1) minSep *= 0.85f;
            if (_stuckRelaxLevel >= 2) minSep *= 0.7f;
            if (_stuckRelaxLevel >= 3) minSep *= 0.55f;
            if ((spaced[spaced.Count - 1] - spaced[0]).sqrMagnitude < minSep * minSep)
            {
                failReason = "start/end too close";
                return false;
            }
        }

        float y = _trackStartPosition.y;
        ClearSpawnedSegmentsOnly();
        _pathPoints.Clear();
        _junctionPathPoints.Clear();
        _segments2D.Clear();

        Vector2 d0 = spaced[1] - spaced[0];
        if (d0.sqrMagnitude > 1e-8f)
            _currentRotation = Quaternion.LookRotation(new Vector3(d0.x, 0f, d0.y).normalized, Vector3.up);
        _currentEndPosition = new Vector3(spaced[0].x, y, spaced[0].y);

        for (int i = 0; i < spaced.Count - 1; i++)
            CommitSegmentTo(spaced[i], spaced[i + 1], y);

        _abortedGeneration = false;
        _lastBuildFailReason = "";
        return _segments2D.Count >= 2;
    }

    /// <summary>
    /// Light Laplacian — removes micro zig-zag without flattening intentional sharp bends.
    /// </summary>
    private void SoftSmoothPolyline(List<Vector2> poly, int passes, float strength)
    {
        if (poly == null || poly.Count < 4) return;
        passes = Mathf.Max(1, passes);
        strength = Mathf.Clamp01(strength);

        for (int pass = 0; pass < passes; pass++)
        {
            var tmp = new Vector2[poly.Count];
            tmp[0] = poly[0];
            tmp[poly.Count - 1] = poly[poly.Count - 1];
            for (int i = 1; i < poly.Count - 1; i++)
                tmp[i] = Vector2.Lerp(poly[i], (poly[i - 1] + poly[i + 1]) * 0.5f, strength);
            for (int i = 1; i < poly.Count - 1; i++)
            {
                poly[i] = tmp[i];
                if (_hasPreferredTerrainBounds)
                    poly[i] = ClampToPreferredInset(poly[i]);
            }
        }
    }

    /// <summary>
    /// Caps insane per-segment flips (mesh teeth) but allows sharp drift turns
    /// up toward configured endMaxTurnAngle.
    /// </summary>
    private void LimitMaxTurnPerSegment(List<Vector2> poly, float spacing)
    {
        if (poly == null || poly.Count < 3) return;
        spacing = Mathf.Max(0.25f, spacing);

        // Allow tight bends for drifting; only block near-instant reverse flips.
        float maxTurnDeg = Mathf.Clamp(Mathf.Max(endMaxTurnAngle, startMaxTurnAngle), 18f, 48f);
        // High turn chance → permit sharper apexes.
        float avgChance = Mathf.Clamp01((startTurnChance + endTurnChance) * 0.5f);
        maxTurnDeg = Mathf.Lerp(maxTurnDeg * 0.85f, maxTurnDeg, avgChance);

        var rebuilt = new List<Vector2>(poly.Count);
        rebuilt.Add(poly[0]);
        Vector2 dir = poly[1] - poly[0];
        if (dir.sqrMagnitude < 1e-8f) dir = Vector2.up;
        dir.Normalize();
        Vector2 pos = poly[0];

        for (int i = 1; i < poly.Count; i++)
        {
            Vector2 desired = poly[i] - pos;
            if (desired.sqrMagnitude > 1e-8f)
            {
                float ang = Vector2.SignedAngle(dir, desired);
                ang = Mathf.Clamp(ang, -maxTurnDeg, maxTurnDeg);
                dir = Rotate2(dir, ang);
            }

            SteerDirOffPreferredEdge(pos, ref dir);
            pos = pos + dir * spacing;
            if (_hasPreferredTerrainBounds)
                pos = ClampToPreferredInset(pos);
            rebuilt.Add(pos);
        }

        poly.Clear();
        poly.AddRange(rebuilt);
    }

    private void ClearSpawnedSegmentsOnly()
    {
        for (int i = 0; i < _spawnedSegments.Count; i++)
        {
            if (_spawnedSegments[i] != null)
                DestroyImmediateSafe(_spawnedSegments[i].gameObject);
        }
        _spawnedSegments.Clear();
    }

    private static void DestroyImmediateSafe(GameObject go)
    {
        if (go == null) return;
        if (Application.isPlaying) Object.Destroy(go);
        else Object.DestroyImmediate(go);
    }

    private void CommitSegmentTo(Vector2 a, Vector2 b, float y)
    {
        Vector3 segmentStart = new Vector3(a.x, y, a.y);
        Vector3 segmentEnd = new Vector3(b.x, y, b.y);
        Vector3 delta = segmentEnd - segmentStart;
        delta.y = 0f;
        if (delta.sqrMagnitude < 1e-8f)
            delta = _currentRotation * Vector3.forward;
        Quaternion segmentRot = Quaternion.LookRotation(delta.normalized, Vector3.up);
        Vector3 centerPos = (segmentStart + segmentEnd) * 0.5f;

        if (segmentPrefab != null)
        {
            GameObject seg = Object.Instantiate(segmentPrefab, centerPos, segmentRot, transform);
            _spawnedSegments.Add(seg.transform);
            int roadLayer = LayerMask.NameToLayer("Road");
            if (roadLayer >= 0) seg.layer = roadLayer;
            foreach (var rend in seg.GetComponentsInChildren<Renderer>(true))
            {
                if (rend != null)
                    rend.enabled = false;
            }
        }

        _pathPoints.Add(centerPos);
        if (_junctionPathPoints.Count == 0)
            _junctionPathPoints.Add(segmentStart);
        _junctionPathPoints.Add(segmentEnd);
        _segments2D.Add(new Segment2D(a, b));

        _currentEndPosition = segmentEnd;
        _currentRotation = segmentRot;
        Vector2 heading = new Vector2(delta.x, delta.z);
        if (heading.sqrMagnitude > 1e-8f)
            _recentFlowHeadingXZ = heading.normalized;
    }

    private static List<Vector2> SampleCatmullRom(List<Vector2> controls, int samplesPerSpan)
    {
        var result = new List<Vector2>(Mathf.Max(8, controls.Count * samplesPerSpan));
        if (controls == null || controls.Count < 2) return result;

        samplesPerSpan = Mathf.Max(2, samplesPerSpan);
        var pts = new List<Vector2>(controls.Count + 2);
        pts.Add(controls[0]);
        pts.AddRange(controls);
        pts.Add(controls[controls.Count - 1]);

        for (int i = 1; i < pts.Count - 2; i++)
        {
            Vector2 p0 = pts[i - 1];
            Vector2 p1 = pts[i];
            Vector2 p2 = pts[i + 1];
            Vector2 p3 = pts[i + 2];
            for (int s = 0; s < samplesPerSpan; s++)
            {
                float t = s / (float)samplesPerSpan;
                result.Add(CatmullRom(p0, p1, p2, p3, t));
            }
        }

        result.Add(controls[controls.Count - 1]);
        return result;
    }

    private static Vector2 CatmullRom(Vector2 p0, Vector2 p1, Vector2 p2, Vector2 p3, float t)
    {
        float t2 = t * t;
        float t3 = t2 * t;
        return 0.5f * (
            (2f * p1) +
            (-p0 + p2) * t +
            (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 +
            (-p0 + 3f * p1 - 3f * p2 + p3) * t3);
    }

    private static List<Vector2> ResamplePolyline(List<Vector2> src, float spacing)
    {
        var dst = new List<Vector2>();
        if (src == null || src.Count == 0) return dst;
        spacing = Mathf.Max(0.25f, spacing);
        dst.Add(src[0]);
        float distToNext = spacing;
        for (int i = 0; i < src.Count - 1; i++)
        {
            Vector2 a = src[i];
            Vector2 b = src[i + 1];
            float segLen = Vector2.Distance(a, b);
            if (segLen < 1e-6f) continue;
            Vector2 dir = (b - a) / segLen;
            float traveled = 0f;
            while (traveled + distToNext <= segLen + 1e-4f)
            {
                traveled += distToNext;
                dst.Add(a + dir * Mathf.Min(traveled, segLen));
                distToNext = spacing;
            }
            distToNext -= (segLen - traveled);
        }

        Vector2 last = src[src.Count - 1];
        if ((dst[dst.Count - 1] - last).sqrMagnitude > 0.0001f)
            dst.Add(last);
        return dst;
    }

    private void ExtendPolylineWithArc(List<Vector2> poly, int needPts, float spacing)
    {
        if (poly == null || poly.Count < 2) return;
        Vector2 dir = poly[poly.Count - 1] - poly[poly.Count - 2];
        if (dir.sqrMagnitude < 1e-8f) dir = Vector2.up;
        dir.Normalize();

        // Gentle sustained arcs only — high turn rates read as mesh zig-zag.
        float sign = Random.value < 0.5f ? -1f : 1f;
        float turnDeg = Random.Range(0.8f, 1.8f) * sign;
        int flipEvery = Random.Range(14, 24);
        int sinceFlip = 0;

        while (poly.Count < needPts)
        {
            sinceFlip++;
            if (sinceFlip >= flipEvery)
            {
                sinceFlip = 0;
                flipEvery = Random.Range(14, 24);
                sign = -sign;
                turnDeg = Random.Range(0.8f, 1.8f) * sign;
            }

            float turn = turnDeg * Mathf.Deg2Rad;
            float c = Mathf.Cos(turn);
            float s = Mathf.Sin(turn);
            dir = new Vector2(dir.x * c - dir.y * s, dir.x * s + dir.y * c);

            Vector2 prev = poly[poly.Count - 1];
            SteerDirOffPreferredEdge(prev, ref dir);
            Vector2 next = prev + dir * spacing;
            if (_hasPreferredTerrainBounds)
                next = ClampToPreferredInset(next);
            poly.Add(next);
        }
    }

    private Vector2 ClampToPreferredInset(Vector2 p)
    {
        if (!_hasPreferredTerrainBounds) return p;
        float inset = Mathf.Max(roadWidth * 2f, preferredTerrainEdgeInset * 0.2f, 25f);
        p.x = Mathf.Clamp(p.x, _preferredMinX + inset, _preferredMaxX - inset);
        p.y = Mathf.Clamp(p.y, _preferredMinZ + inset, _preferredMaxZ - inset);
        return p;
    }

    private bool PolylineStaysOnPreferredTerrain(List<Vector2> poly)
    {
        for (int i = 0; i < poly.Count - 1; i++)
        {
            if (!SegmentStaysOnPreferredTerrain(poly[i], poly[i + 1]))
                return false;
        }
        return true;
    }

    private bool PolylineClearsItself(List<Vector2> poly, bool requireProximityClearance = true)
    {
        // Soft asphalt floor — roads may turn toward each other and skim close (drift hooks).
        float softClear = roadWidth * 0.9f;
        float softClearSq = softClear * softClear;
        // Paperclip = sustained reverse-parallel fold, not a single sharp bend.
        float paperclipDist = roadWidth * Mathf.Max(1.9f, parallelRejectRoadWidths * 0.9f);
        float paperclipDistSq = paperclipDist * paperclipDist;

        int minIndexGap = Mathf.Max(4, recentIgnoreCount + 3);
        int paperclipGap = Mathf.Max(minIndexGap + 2, 7);
        int n = poly.Count - 1;
        int paperclipHits = 0;

        for (int i = 0; i < n; i++)
        {
            Vector2 a0 = poly[i];
            Vector2 a1 = poly[i + 1];
            Vector2 adir = a1 - a0;
            if (adir.sqrMagnitude < 1e-8f) continue;
            adir.Normalize();

            for (int j = 0; j <= i - minIndexGap; j++)
            {
                Vector2 b0 = poly[j];
                Vector2 b1 = poly[j + 1];
                if (SegmentsProperlyIntersect(a0, a1, b0, b1))
                    return false;

                if (!requireProximityClearance)
                    continue;

                float distSq = SegmentSegmentDistanceSq(a0, a1, b0, b1);
                // True asphalt overlap only.
                if (distSq < softClearSq)
                    return false;

                // Count reverse-parallel close pairs for paperclip detection.
                if (i - j < paperclipGap) continue;
                Vector2 bdir = b1 - b0;
                if (bdir.sqrMagnitude < 1e-8f) continue;
                bdir.Normalize();
                if (Vector2.Dot(adir, bdir) > -0.72f) continue;
                if (distSq < paperclipDistSq)
                    paperclipHits++;
            }
        }

        // A few near approaches are fine; a long anti-parallel coil is not.
        int paperclipBudget = Mathf.Max(2, Mathf.RoundToInt(segmentCount * 0.04f));
        if (paperclipHits > paperclipBudget)
            return false;

        return true;
    }

    /// <summary>Brute-force any yaw in ±maxTurn that is legal under current stuck-relax rules.</summary>
    private bool TryPickAnyLegalYaw(float maxTurnAngle, float segLength, out float resultYaw)
    {
        resultYaw = 0f;
        maxTurnAngle = Mathf.Clamp(maxTurnAngle, 1f, 179f);
        bool requireTerrain = preferStayOnPreferredTerrain && _stuckRelaxLevel < 3;

        int samples = 72;
        for (int i = 0; i <= samples; i++)
        {
            float yaw = Mathf.Lerp(-maxTurnAngle, maxTurnAngle, i / (float)samples);
            if (!IsYawValid(yaw, segLength, requireTerrain))
                continue;
            resultYaw = yaw;
            return true;
        }

        // Random probes as last ditch.
        for (int i = 0; i < 48; i++)
        {
            float yaw = Random.Range(-maxTurnAngle, maxTurnAngle);
            if (!IsYawValid(yaw, segLength, requireTerrain))
                continue;
            resultYaw = yaw;
            return true;
        }

        return false;
    }

    private void CommitSegment(float yawDeg, float length)
    {
        Quaternion segmentRot = _currentRotation * Quaternion.Euler(0f, yawDeg, 0f);
        Vector3 forward3D = segmentRot * Vector3.forward;

        Vector3 segmentStart = _currentEndPosition;
        Vector3 segmentEnd = _currentEndPosition + forward3D * length;
        Vector3 centerPos = (segmentStart + segmentEnd) * 0.5f;

        GameObject seg = Object.Instantiate(segmentPrefab, centerPos, segmentRot, transform);
        _spawnedSegments.Add(seg.transform);

        seg.layer = LayerMask.NameToLayer("Road");

        foreach (var rend in seg.GetComponentsInChildren<Renderer>(true))
        {
            if (rend != null)
                rend.enabled = false;
        }

        _pathPoints.Add(centerPos);

        if (_junctionPathPoints.Count == 0)
            _junctionPathPoints.Add(segmentStart);
        _junctionPathPoints.Add(segmentEnd);

        Vector2 a2d = new Vector2(segmentStart.x, segmentStart.z);
        Vector2 b2d = new Vector2(segmentEnd.x, segmentEnd.z);
        _segments2D.Add(new Segment2D(a2d, b2d));

        _currentEndPosition = segmentEnd;
        _currentRotation = segmentRot;
    }

    // ================================================================
    //  FLOW YAW PICKER (waypoint arcs — road goes somewhere)
    // ================================================================
    private bool TryPickFlowYaw(int index, float tNorm, float maxStepTurn, float segLength, out float bestYaw)
    {
        bestYaw = 0f;
        maxStepTurn = Mathf.Max(5f, maxStepTurn);

        // Occasionally reshape the sustained arc (S-curves), not every segment.
        float reshapeChance = Mathf.Lerp(0.04f, 0.11f, tNorm);
        if (Random.value < reshapeChance)
        {
            float mag = Random.Range(6f, maxStepTurn * 0.9f);
            if (Random.value < 0.4f)
                _currentTurnDirectionSign = -_currentTurnDirectionSign;
            _arcYawRate = mag * _currentTurnDirectionSign;
        }

        float intent = _arcYawRate;

        // Steer toward next guide waypoint (primary "going somewhere" signal).
        Vector2 pos = new Vector2(_currentEndPosition.x, _currentEndPosition.z);
        Vector3 curFwd3 = _currentRotation * Vector3.forward;
        Vector2 curFwd = new Vector2(curFwd3.x, curFwd3.z);
        if (curFwd.sqrMagnitude > 1e-6f) curFwd.Normalize();

        if (_guideWaypointIndex < _guideWaypoints.Count)
        {
            Vector2 toWp = _guideWaypoints[_guideWaypointIndex] - pos;
            if (toWp.sqrMagnitude > 1e-4f)
            {
                toWp.Normalize();
                float wpYaw = Vector2.SignedAngle(curFwd, toWp);
                intent = Mathf.LerpAngle(intent, wpYaw, 0.55f);
            }
        }
        else
        {
            // Past last waypoint: keep touring toward open far side / center weave.
            Vector2 toGoal = _flowGoalXZ - pos;
            if (toGoal.sqrMagnitude > 1e-4f)
            {
                toGoal.Normalize();
                float goalYaw = Vector2.SignedAngle(curFwd, toGoal);
                intent = Mathf.LerpAngle(intent, goalYaw, 0.25f);
            }
        }

        // Edge: peel away from the nearest border instead of sliding along it.
        if (_hasPreferredTerrainBounds && NeedsEdgePeel())
        {
            Vector2 inward = GetNearestEdgeInwardNormal(pos);
            if (inward.sqrMagnitude > 1e-4f)
            {
                float edgeDist = MinDistanceToPreferredInsetEdge(pos);
                float edgeT = 1f - Mathf.Clamp01(edgeDist / Mathf.Max(1f, preferredTerrainEdgeSteerMeters));
                float peelYaw = SignedAngle2D(curFwd, inward);
                intent = Mathf.LerpAngle(intent, peelYaw, 0.45f + edgeT * 0.5f);
            }
        }

        // No per-step wobble — that created the sawtooth road.
        intent = Mathf.Clamp(intent, -maxStepTurn, maxStepTurn);

        // Search a narrow band around intent — flowing roads don't need ±60° lottery.
        float bestScore = float.NegativeInfinity;
        bool found = false;
        int samples = 24;
        for (int i = 0; i <= samples; i++)
        {
            float u = i / (float)samples;
            float yaw = Mathf.Lerp(-maxStepTurn, maxStepTurn, u);
            // Prefer candidates near intent.
            if (Mathf.Abs(Mathf.DeltaAngle(yaw, intent)) > maxStepTurn * 0.85f && i % 2 == 1)
                continue;

            if (!IsYawValid(yaw, segLength, preferStayOnPreferredTerrain && _stuckRelaxLevel < 3))
                continue;

            float score = ScoreFlowYaw(yaw, intent, maxStepTurn, segLength, tNorm);
            if (score > bestScore)
            {
                bestScore = score;
                bestYaw = yaw;
                found = true;
            }
        }

        // Refine around intent.
        for (int i = -8; i <= 8; i++)
        {
            float yaw = Mathf.Clamp(intent + i * (maxStepTurn / 16f), -maxStepTurn, maxStepTurn);
            if (!IsYawValid(yaw, segLength, preferStayOnPreferredTerrain && _stuckRelaxLevel < 3))
                continue;
            float score = ScoreFlowYaw(yaw, intent, maxStepTurn, segLength, tNorm);
            if (score > bestScore)
            {
                bestScore = score;
                bestYaw = yaw;
                found = true;
            }
        }

        return found;
    }

    private float ScoreFlowYaw(float yaw, float intent, float maxStepTurn, float segLength, float tNorm)
    {
        Quaternion testRot = _currentRotation * Quaternion.Euler(0f, yaw, 0f);
        Vector3 forward3D = testRot * Vector3.forward;
        Vector3 start3D = _currentEndPosition;
        Vector3 end3D = start3D + forward3D * segLength;
        Vector2 startXZ = new Vector2(start3D.x, start3D.z);
        Vector2 endXZ = new Vector2(end3D.x, end3D.z);
        Vector2 fwdXZ = new Vector2(forward3D.x, forward3D.z);
        if (fwdXZ.sqrMagnitude > 1e-8f) fwdXZ.Normalize();

        float score = 0f;

        // Stay close to sustained arc intent.
        score -= Mathf.Abs(Mathf.DeltaAngle(yaw, intent)) * 0.55f;

        // Clearance from existing road.
        float clearance = Mathf.Max(softTrackClearanceMeters * 0.85f, roadWidth * 2.8f, 14f);
        float minDist = MinDistanceSegmentToExistingTrack(startXZ, endXZ);
        if (minDist < clearance)
        {
            float prox = 1f - Mathf.Clamp01(minDist / clearance);
            score -= prox * prox * 50f;
            score -= (clearance - minDist) * 2.8f;
        }
        else
            score += Mathf.Clamp01((minDist - clearance) / clearance) * 4f;

        score -= ScoreParallelSwitchbackPenalty(startXZ, endXZ) * 12f;
        score -= ScoreFoldInAimPenalty(endXZ, fwdXZ) * 4f;

        if (_hasPreferredTerrainBounds)
            score += ScoreEdgePeelYaw(yaw, segLength);

        // Follow waypoint / progress.
        if (_guideWaypointIndex < _guideWaypoints.Count)
        {
            Vector2 toWp = _guideWaypoints[_guideWaypointIndex] - endXZ;
            if (toWp.sqrMagnitude > 1e-4f)
            {
                toWp.Normalize();
                score += Vector2.Dot(fwdXZ, toWp) * 14f;
            }
        }

        // Soft start-region avoid.
        float intrusion = StartRegionIntrusion01(startXZ, endXZ);
        score -= intrusion * intrusion * 16f;

        // Prefer open heading ahead.
        score += ScoreForwardClearance(endXZ, fwdXZ) * 3f;

        score += (Random.value - 0.5f) * 0.08f;
        return score;
    }

    private void DensifySplineControls(List<Vector2> controls)
    {
        if (controls == null || controls.Count < 3) return;

        float avgChance = Mathf.Clamp01((startTurnChance + endTurnChance) * 0.5f);
        // Mild mid-bends — wide kinks made Catmull loop into itself.
        float insertChance = Mathf.Lerp(0.3f, 0.7f, avgChance);
        float latScale = Mathf.Lerp(0.4f, 0.85f, avgChance);
        float turnSpan = Mathf.Max(25f, (startMaxTurnAngle + endMaxTurnAngle) * 0.5f);
        float latMeters = Mathf.Lerp(28f, 75f, Mathf.Clamp01(turnSpan / 55f)) * latScale;

        var denser = new List<Vector2>(controls.Count * 2);
        denser.Add(controls[0]);
        for (int i = 0; i < controls.Count - 1; i++)
        {
            Vector2 a = controls[i];
            Vector2 b = controls[i + 1];
            Vector2 ab = b - a;
            float len = ab.magnitude;
            if (len > 120f && Random.value < insertChance)
            {
                Vector2 mid = Vector2.Lerp(a, b, Random.Range(0.4f, 0.6f));
                Vector2 perp = len > 1e-4f
                    ? new Vector2(-ab.y, ab.x) / len
                    : Vector2.right;
                float sign = Random.value < 0.5f ? -1f : 1f;
                // Cap lateral so mid-points stay roughly between neighbors (no hairpins).
                float maxLat = Mathf.Min(latMeters, len * 0.28f);
                mid += perp * (sign * Random.Range(maxLat * 0.4f, maxLat));
                if (_hasPreferredTerrainBounds)
                    mid = ClampToPreferredInset(mid);
                denser.Add(mid);
            }
            denser.Add(b);
        }

        controls.Clear();
        controls.AddRange(denser);
    }

    /// <summary>
    /// Smooth lateral displacement along the path. Wavelength is tens of meters so it reads as
    /// road character, not segment zig-zag. Strength follows maxWiggleAngle + turn chance.
    /// </summary>
    /// <summary>
    /// Continuous multi-sine heading sway along the whole path. Unlike Perlin lateral offsets,
    /// this never "turns off" for long stretches.
    /// </summary>
    private void ApplyContinuousHeadingWiggle(List<Vector2> poly, float spacing)
    {
        if (poly == null || poly.Count < 3) return;
        if (maxWiggleAngle <= 0.01f) return;

        spacing = Mathf.Max(0.25f, spacing);
        float peakYaw = Mathf.Clamp(maxWiggleAngle * 0.55f, 1.25f, 9f);
        float growEnd = Mathf.Clamp01(wiggleOverDistance);

        // Wavelengths in meters — always oscillating, never dwelling near zero for long.
        float w1 = (2f * Mathf.PI) / 70f;
        float w2 = (2f * Mathf.PI) / 115f;
        float w3 = (2f * Mathf.PI) / 175f;
        float p1 = _noiseOffset * 7.13f;
        float p2 = _noiseOffset * 4.27f + 1.7f;
        float p3 = _noiseOffset * 2.91f + 3.1f;

        float followCap = Mathf.Clamp(Mathf.Max(endMaxTurnAngle, startMaxTurnAngle), 16f, 42f);
        var rebuilt = new List<Vector2>(poly.Count);
        rebuilt.Add(poly[0]);

        Vector2 dir = poly[1] - poly[0];
        if (dir.sqrMagnitude < 1e-8f) dir = Vector2.up;
        dir.Normalize();
        Vector2 pos = poly[0];
        float dist = 0f;

        for (int i = 1; i < poly.Count; i++)
        {
            float t = i / (float)(poly.Count - 1);
            float ampScale = Mathf.Lerp(0.75f, 1f, Mathf.Clamp01(t / Mathf.Max(0.2f, growEnd)));
            ampScale *= Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / 0.05f));

            Vector2 want = poly[i] - pos;
            if (want.sqrMagnitude > 1e-8f)
            {
                float follow = Vector2.SignedAngle(dir, want);
                follow = Mathf.Clamp(follow, -followCap, followCap);
                dir = Rotate2(dir, follow);
            }

            float sway =
                Mathf.Sin(dist * w1 + p1) * 0.5f +
                Mathf.Sin(dist * w2 + p2) * 0.32f +
                Mathf.Sin(dist * w3 + p3) * 0.18f;
            dir = Rotate2(dir, sway * peakYaw * ampScale);
            if (dir.sqrMagnitude > 1e-8f) dir.Normalize();

            SteerDirOffPreferredEdge(pos, ref dir);
            pos += dir * spacing;
            if (_hasPreferredTerrainBounds)
                pos = ClampToPreferredInset(pos);
            rebuilt.Add(pos);
            dist += spacing;
        }

        poly.Clear();
        poly.AddRange(rebuilt);
    }

    private void BuildGuideWaypoints()
    {
        _guideWaypoints.Clear();
        Vector2 start = new Vector2(_trackStartPosition.x, _trackStartPosition.z);

        Vector3 fwd3 = _currentRotation * Vector3.forward;
        Vector2 fwd = new Vector2(fwd3.x, fwd3.z);
        if (fwd.sqrMagnitude < 1e-6f) fwd = Vector2.up;
        fwd.Normalize();

        float targetLen = Mathf.Max(400f, segmentCount * Mathf.Max(0.001f, segmentLength) * 0.92f);
        float avgChance = Mathf.Clamp01((startTurnChance + endTurnChance) * 0.5f);
        float wantTurn = Mathf.Lerp(160f, 320f, avgChance);

        if (!_hasPreferredTerrainBounds)
        {
            BuildRandomOpenWaypoints(start, fwd, targetLen);
            return;
        }

        float inset = Mathf.Max(roadWidth * 2.5f, preferredTerrainEdgeInset * 0.22f, 40f);
        float minX = _preferredMinX + inset;
        float maxX = _preferredMaxX - inset;
        float minZ = _preferredMinZ + inset;
        float maxZ = _preferredMaxZ - inset;

        List<Vector2> best = null;
        float bestTurn = -1f;
        for (int attempt = 0; attempt < 8; attempt++)
        {
            var tour = BuildRandomTerrainTour(start, fwd, minX, maxX, minZ, maxZ, targetLen);
            float turn = MeasureControlTurning(start, tour);
            if (turn > bestTurn)
            {
                bestTurn = turn;
                best = tour;
            }
            if (turn >= wantTurn && tour.Count >= 5)
                break;
        }

        if (best != null)
        {
            _guideWaypoints.AddRange(best);
            _flowGoalXZ = best[best.Count - 1];
        }
    }

    private void BuildRandomOpenWaypoints(Vector2 start, Vector2 fwd, float targetLen)
    {
        Vector2 side = new Vector2(-fwd.y, fwd.x);
        float avgChance = Mathf.Clamp01((startTurnChance + endTurnChance) * 0.5f);
        float sign = Random.value < 0.5f ? -1f : 1f;
        float along = 0f;
        float pathLen = 0f;
        Vector2 prev = start;
        int legs = Random.Range(
            Mathf.RoundToInt(Mathf.Lerp(6, 10, avgChance)),
            Mathf.RoundToInt(Mathf.Lerp(9, 14, avgChance)) + 1);
        float latMin = Mathf.Lerp(70f, 110f, avgChance);
        float latMax = Mathf.Lerp(140f, 210f, avgChance);
        for (int i = 0; i < legs && pathLen < targetLen; i++)
        {
            along += Random.Range(Mathf.Lerp(160f, 100f, avgChance), Mathf.Lerp(240f, 170f, avgChance));
            if (Random.value < Mathf.Lerp(0.5f, 0.85f, avgChance)) sign = -sign;
            float lat = sign * Random.Range(latMin, latMax);
            Vector2 p = start + fwd * along + side * lat;
            pathLen += Vector2.Distance(prev, p);
            prev = p;
            _guideWaypoints.Add(p);
        }
        if (_guideWaypoints.Count > 0)
            _flowGoalXZ = _guideWaypoints[_guideWaypoints.Count - 1];
    }

    private List<Vector2> BuildRandomTerrainTour(
        Vector2 start, Vector2 fwd,
        float minX, float maxX, float minZ, float maxZ,
        float targetLen)
    {
        var tour = new List<Vector2>(16);
        Vector2 center = new Vector2((minX + maxX) * 0.5f, (minZ + maxZ) * 0.5f);
        float span = Mathf.Min(maxX - minX, maxZ - minZ);

        float avgChance = Mathf.Clamp01((startTurnChance + endTurnChance) * 0.5f);
        float avgMaxTurn = (startMaxTurnAngle + endMaxTurnAngle) * 0.5f;
        float turnT = Mathf.Clamp01(avgMaxTurn / 55f);

        // High turn chance → shorter legs, more bends.
        float minStep = Mathf.Clamp(span * Mathf.Lerp(0.14f, 0.07f, avgChance), 80f, 190f);
        float maxStep = Mathf.Clamp(span * Mathf.Lerp(0.26f, 0.14f, avgChance), 140f, 260f);
        int maxLegs = Random.Range(
            Mathf.RoundToInt(Mathf.Lerp(8, 13, avgChance)),
            Mathf.RoundToInt(Mathf.Lerp(11, 20, avgChance)) + 1);

        Vector2 heading = Rotate2(fwd, Random.Range(-70f, 70f));
        float turnSign = Random.value < 0.5f ? -1f : 1f;

        Vector2 pos = start;
        float pathLen = 0f;
        float minFinishSep = GetEffectiveMinStartEndDistance(Mathf.Max(0.001f, segmentLength));

        float turnProb = Mathf.Lerp(0.5f, 0.85f, avgChance);
        // Waypoint-scale bends; segment limiter allows sharp apexes for drift.
        float bigTurnMin = Mathf.Lerp(30f, 50f, avgChance);
        float bigTurnMax = Mathf.Lerp(60f, 105f, Mathf.Max(avgChance, turnT));

        for (int i = 0; i < maxLegs && pathLen < targetLen; i++)
        {
            float progress = Mathf.Clamp01(pathLen / Mathf.Max(1f, targetLen));
            float localChance = Mathf.Lerp(startTurnChance, endTurnChance, progress);
            float localMax = Mathf.Lerp(startMaxTurnAngle, endMaxTurnAngle, progress);

            // Occasional hook toward earlier road — close approach / sharp corner, not a paperclip coil.
            bool didHook = false;
            if (tour.Count >= 3 && Random.value < Mathf.Lerp(0.12f, 0.32f, localChance))
            {
                int targetIdx = Random.Range(0, Mathf.Max(1, tour.Count - 2));
                Vector2 toward = tour[targetIdx] - pos;
                if (toward.sqrMagnitude > 1e-4f)
                {
                    toward.Normalize();
                    float hookAng = Vector2.SignedAngle(heading, toward);
                    float maxHook = Mathf.Lerp(70f, 120f, localChance);
                    hookAng = Mathf.Clamp(hookAng, -maxHook, maxHook);
                    heading = Rotate2(heading, hookAng);
                    turnSign = hookAng >= 0f ? 1f : -1f;
                    didHook = true;
                }
            }

            if (!didHook)
            {
                if (i == 0 || Random.value < Mathf.Max(turnProb, localChance))
                {
                    if (Random.value < Mathf.Lerp(0.3f, 0.55f, localChance))
                        turnSign = -turnSign;
                    float mag = Random.Range(bigTurnMin, Mathf.Max(bigTurnMin + 5f, bigTurnMax));
                    heading = Rotate2(heading, turnSign * mag);
                }
                else
                {
                    float mild = Mathf.Max(minTurnAngle, localMax * 0.3f);
                    heading = Rotate2(heading, Random.Range(-mild, mild) * turnSign);
                }
            }

            float step = Random.Range(minStep, maxStep);
            if (didHook)
                step *= Random.Range(0.55f, 0.8f);
            step *= Mathf.Lerp(1f, 0.85f, progress * endTurnChance);
            Vector2 next = pos + heading * step;

            for (int bounce = 0; bounce < 4; bounce++)
            {
                bool hit = false;
                if (next.x < minX || next.x > maxX)
                {
                    heading.x = -heading.x;
                    hit = true;
                }
                if (next.y < minZ || next.y > maxZ)
                {
                    heading.y = -heading.y;
                    hit = true;
                }
                if (!hit) break;
                heading = Rotate2(heading.normalized, turnSign * Random.Range(15f, 35f));
                Vector2 toMid = center - pos;
                if (toMid.sqrMagnitude > 1e-4f)
                    heading = Vector2.Lerp(heading, toMid.normalized, 0.7f).normalized;
                next = pos + heading * step * 0.85f;
            }

            next.x = Mathf.Clamp(next.x, minX, maxX);
            next.y = Mathf.Clamp(next.y, minZ, maxZ);
            // Don't leave waypoints sitting on the border — that makes the spline hug the wall.
            if (MinDistanceToPreferredInsetEdge(next) < Mathf.Min(24f, span * 0.06f))
            {
                Vector2 pull = center - next;
                if (pull.sqrMagnitude > 1e-4f)
                    next += pull.normalized * Random.Range(span * 0.08f, span * 0.16f);
                next.x = Mathf.Clamp(next.x, minX, maxX);
                next.y = Mathf.Clamp(next.y, minZ, maxZ);
            }

            if ((next - pos).sqrMagnitude < (minStep * 0.35f) * (minStep * 0.35f))
                continue;

            if (didHook && tour.Count > 0)
            {
                Vector2 apex = Vector2.Lerp(pos, next, 0.45f);
                Vector2 ab = next - pos;
                float abLen = ab.magnitude;
                if (abLen > 1e-4f)
                {
                    Vector2 perp = new Vector2(-ab.y, ab.x) / abLen;
                    apex += perp * (turnSign * Random.Range(span * 0.04f, span * 0.1f));
                    apex.x = Mathf.Clamp(apex.x, minX, maxX);
                    apex.y = Mathf.Clamp(apex.y, minZ, maxZ);
                    pathLen += Vector2.Distance(pos, apex);
                    tour.Add(apex);
                    pos = apex;
                }
            }

            pathLen += Vector2.Distance(pos, next);
            pos = next;
            tour.Add(pos);
        }

        if (tour.Count > 0)
        {
            Vector2 finish = tour[tour.Count - 1];
            if ((finish - start).magnitude < minFinishSep * 0.85f)
            {
                Vector2 away = finish - start;
                if (away.sqrMagnitude < 1e-4f) away = heading;
                away.Normalize();
                Vector2 pushed = start + away * Mathf.Max(minFinishSep, span * 0.45f);
                pushed.x = Mathf.Clamp(pushed.x, minX, maxX);
                pushed.y = Mathf.Clamp(pushed.y, minZ, maxZ);
                Vector2 mid = Vector2.Lerp(finish, pushed, 0.5f)
                              + Rotate2(away, 90f) * Random.Range(-span * 0.14f, span * 0.14f);
                mid.x = Mathf.Clamp(mid.x, minX, maxX);
                mid.y = Mathf.Clamp(mid.y, minZ, maxZ);
                tour.Add(mid);
                tour.Add(pushed);
            }
        }

        return tour;
    }

    private static Vector2 Rotate2(Vector2 v, float degrees)
    {
        float r = degrees * Mathf.Deg2Rad;
        float c = Mathf.Cos(r);
        float s = Mathf.Sin(r);
        return new Vector2(v.x * c - v.y * s, v.x * s + v.y * c);
    }

    private static float MeasureControlTurning(Vector2 start, List<Vector2> pts)
    {
        if (pts == null || pts.Count < 2) return 0f;
        float turn = 0f;
        Vector2 prev = pts[0] - start;
        for (int i = 1; i < pts.Count; i++)
        {
            Vector2 cur = pts[i] - pts[i - 1];
            if (prev.sqrMagnitude < 1e-6f || cur.sqrMagnitude < 1e-6f)
            {
                prev = cur;
                continue;
            }
            turn += Mathf.Abs(Vector2.SignedAngle(prev, cur));
            prev = cur;
        }
        return turn;
    }

    private static float MeasurePolylineTurning(List<Vector2> poly)
    {
        if (poly == null || poly.Count < 3) return 0f;
        float turn = 0f;
        for (int i = 1; i < poly.Count - 1; i++)
        {
            Vector2 a = poly[i] - poly[i - 1];
            Vector2 b = poly[i + 1] - poly[i];
            if (a.sqrMagnitude < 1e-8f || b.sqrMagnitude < 1e-8f) continue;
            turn += Mathf.Abs(Vector2.SignedAngle(a, b));
        }
        return turn;
    }

    private void AdvanceGuideWaypointIfNeeded()
    {
        if (_guideWaypointIndex >= _guideWaypoints.Count) return;
        Vector2 pos = new Vector2(_currentEndPosition.x, _currentEndPosition.z);
        float reach = Mathf.Max(segmentLength * 1.6f, roadWidth * 4f, 28f);
        if ((pos - _guideWaypoints[_guideWaypointIndex]).sqrMagnitude <= reach * reach)
            _guideWaypointIndex++;
    }

    // ================================================================
    //  ADVANCED SCORED YAW PICKER (legacy fallback helpers still used by scoring utils)
    // ================================================================
    private bool TryPickBestAdvancedYaw(
        int index,
        float tNorm,
        float maxTurnAngle,
        float turnChance,
        float segLength,
        out float bestYaw)
    {
        bestYaw = 0f;
        float turnIntent = BuildTurnIntentYaw(index, tNorm, maxTurnAngle, turnChance);

        int samples = Mathf.Max(12, advancedYawSamples);
        float bestScore = float.NegativeInfinity;
        bool found = false;

        for (int i = 0; i <= samples; i++)
        {
            float u = i / (float)samples;
            float yaw = Mathf.Lerp(-maxTurnAngle, maxTurnAngle, u);
            if (!TryScoreYaw(yaw, segLength, turnIntent, maxTurnAngle, tNorm, ref bestScore, ref bestYaw))
                continue;
            found = true;
        }

        // Dense probes around turn intent (local refinement).
        for (int i = -6; i <= 6; i++)
        {
            float yaw = Mathf.Clamp(turnIntent + i * (maxTurnAngle / 18f), -maxTurnAngle, maxTurnAngle);
            if (!TryScoreYaw(yaw, segLength, turnIntent, maxTurnAngle, tNorm, ref bestScore, ref bestYaw))
                continue;
            found = true;
        }

        for (int i = 0; i < advancedRandomProbes; i++)
        {
            float yaw = Random.Range(-maxTurnAngle, maxTurnAngle);
            if (!TryScoreYaw(yaw, segLength, turnIntent, maxTurnAngle, tNorm, ref bestScore, ref bestYaw))
                continue;
            found = true;
        }

        return found;
    }

    private bool TryScoreYaw(
        float yaw,
        float segLength,
        float turnIntent,
        float maxTurnAngle,
        float tNorm,
        ref float bestScore,
        ref float bestYaw)
    {
        bool requireTerrain = preferStayOnPreferredTerrain && _stuckRelaxLevel < 3;
        if (!IsYawValid(yaw, segLength, requireTerrain))
            return false;

        float score = ScoreYawCandidate(yaw, segLength, turnIntent, maxTurnAngle, tNorm);
        if (score > bestScore)
        {
            bestScore = score;
            bestYaw = yaw;
        }

        return true;
    }

    private float BuildTurnIntentYaw(int index, float tNorm, float maxTurnAngle, float turnChance)
    {
        float wiggleScale = Mathf.Lerp(1f, 1f + wiggleOverDistance, tNorm);
        float noiseT = _noiseOffset + index * 0.15f;
        float noise = Mathf.PerlinNoise(noiseT, 0.1234f) * 2f - 1f;
        float yaw = noise * maxWiggleAngle * wiggleScale;

        // Long-horizon curve personality (smooth arcs).
        float arcNoise = Mathf.PerlinNoise(_noiseOffset * 0.37f + index * 0.035f, 0.77f) * 2f - 1f;
        yaw += arcNoise * Mathf.Lerp(minTurnAngle, maxTurnAngle * 0.7f, tNorm) * 0.85f;

        if (Random.value < turnChance)
        {
            float sign = (Random.value < 0.35f) ? -_currentTurnDirectionSign : _currentTurnDirectionSign;
            if (Mathf.Abs(sign) < 0.1f) sign = Random.value < 0.5f ? -1f : 1f;
            float bigTurn = Random.Range(minTurnAngle, maxTurnAngle);
            yaw += sign * bigTurn;
            _currentTurnDirectionSign = sign;
        }

        if (useAvoidance)
            yaw += ComputeAvoidanceYawBias(maxTurnAngle) * 0.5f;

        // Light pull toward far-side goal — strong enough to avoid pure accordion, weak enough for curves.
        if (flowProgressWeight > 1e-4f)
        {
            Vector2 pos = new Vector2(_currentEndPosition.x, _currentEndPosition.z);
            Vector2 toGoal = _flowGoalXZ - pos;
            if (toGoal.sqrMagnitude > 1e-4f)
            {
                toGoal.Normalize();
                Vector3 curFwd3 = _currentRotation * Vector3.forward;
                Vector2 curFwd = new Vector2(curFwd3.x, curFwd3.z);
                if (curFwd.sqrMagnitude > 1e-6f)
                    curFwd.Normalize();
                float desiredYaw = Vector2.SignedAngle(curFwd, toGoal);
                yaw = Mathf.LerpAngle(yaw, desiredYaw, 0.12f);
            }
        }

        return Mathf.Clamp(yaw, -maxTurnAngle, maxTurnAngle);
    }

    private float ScoreYawCandidate(float yaw, float segLength, float turnIntent, float maxTurnAngle, float tNorm)
    {
        Quaternion testRot = _currentRotation * Quaternion.Euler(0f, yaw, 0f);
        Vector3 forward3D = testRot * Vector3.forward;
        Vector3 start3D = _currentEndPosition;
        Vector3 end3D = start3D + forward3D * segLength;
        Vector3 mid3D = (start3D + end3D) * 0.5f;

        Vector2 startXZ = new Vector2(start3D.x, start3D.z);
        Vector2 midXZ = new Vector2(mid3D.x, mid3D.z);
        Vector2 endXZ = new Vector2(end3D.x, end3D.z);
        Vector2 fwdXZ = new Vector2(forward3D.x, forward3D.z);
        if (fwdXZ.sqrMagnitude > 1e-8f) fwdXZ.Normalize();

        float score = 0f;

        // --- Terrain edge / center (dominant when near borders) ---
        if (_hasPreferredTerrainBounds)
        {
            float distStart = MinDistanceToPreferredInsetEdge(startXZ);
            float distMid = MinDistanceToPreferredInsetEdge(midXZ);
            float distEnd = MinDistanceToPreferredInsetEdge(endXZ);
            float distMin = Mathf.Min(distMid, distEnd);

            float comfort = Mathf.Max(1f, edgeComfortZoneMeters);
            float edgeNorm = 1f - Mathf.Clamp01(distMin / comfort);

            // Punish being near the edge.
            score -= edgeProximityPenalty * edgeNorm * edgeNorm * 10f;

            // Punish approaching the edge (moving from safer → riskier).
            float approach = distStart - distEnd;
            if (approach > 0f)
                score -= edgeApproachPenalty * approach * (0.35f + edgeNorm * 1.65f);

            // Near edge: strongly reward peeling away from the nearest border.
            Vector2 inward = GetNearestEdgeInwardNormal(endXZ);
            if (inward.sqrMagnitude > 1e-6f)
            {
                float align = Vector2.Dot(fwdXZ, inward); // -1..1
                score += centerSeekReward * Mathf.Max(0f, align) * edgeNorm * 10f;
                // Extra punish aiming into the edge / sliding along it.
                score -= centerSeekReward * Mathf.Max(0f, -align) * edgeNorm * 8f;
                if (align < 0.15f)
                    score -= centerSeekReward * edgeNorm * 6f;
            }

            // Mild reward for staying deep in the interior.
            score += Mathf.Clamp01(distEnd / comfort) * 1.25f;
        }

        // --- Soft self-proximity / anti fold-in (dominant vs turn intent) ---
        float clearance = Mathf.Max(
            softTrackClearanceMeters,
            GetMinSelfClearanceMeters() * 1.35f,
            roadWidth * 3.0f,
            16f);

        float minDist = MinDistanceSegmentToExistingTrack(startXZ, endXZ);
        if (minDist < clearance)
        {
            float proxNorm = 1f - Mathf.Clamp01(minDist / Mathf.Max(0.01f, clearance));
            score -= softProximityPenalty * proxNorm * proxNorm * 55f;
            score -= (clearance - minDist) * 3.5f;
        }
        else
        {
            score += Mathf.Clamp01((minDist - clearance) / clearance) * 3.5f;
        }

        // Kill paperclip / parallel switchbacks in scoring.
        score -= ScoreParallelSwitchbackPenalty(startXZ, endXZ) * 14f;

        // Flow: mild preference to keep moving across the tile; don't flatten all curves.
        if (flowProgressWeight > 1e-4f)
        {
            Vector2 recent = _recentFlowHeadingXZ.sqrMagnitude > 1e-6f ? _recentFlowHeadingXZ : fwdXZ;
            float alignRecent = Vector2.Dot(fwdXZ, recent);
            score += flowProgressWeight * Mathf.Max(0f, alignRecent) * 2.5f;

            // Only punish true reverse folds when already near existing road (paperclips).
            if (alignRecent < -0.25f && minDist < clearance * 1.25f)
                score -= reverseHeadingPenalty * alignRecent * alignRecent * 10f;

            Vector2 toGoal = _flowGoalXZ - endXZ;
            if (toGoal.sqrMagnitude > 1e-4f)
            {
                toGoal.Normalize();
                float alignGoal = Vector2.Dot(fwdXZ, toGoal);
                score += flowProgressWeight * Mathf.Max(0f, alignGoal) * 2.0f;
            }
        }

        // Ahead look: discourage aiming into nearby existing track.
        score += ScoreForwardClearance(endXZ, fwdXZ) * 4.5f;
        score -= ScoreFoldInAimPenalty(endXZ, fwdXZ) * foldInAimPenalty;

        // Soft look-ahead probes: if continuing this heading would skim existing road, reject in score.
        if (foldAvoidLookAheadMeters > 1f && _segments2D.Count > 2)
        {
            float probeStep = Mathf.Max(segLength, foldAvoidLookAheadMeters * 0.35f);
            Vector2 p0 = endXZ;
            for (int step = 1; step <= 2; step++)
            {
                Vector2 p1 = p0 + fwdXZ * probeStep;
                float d = MinDistanceSegmentToExistingTrack(p0, p1);
                float need = Mathf.Max(clearance * 0.85f, GetHardSelfClearanceMeters() * 1.5f);
                if (d < need)
                {
                    float n = 1f - Mathf.Clamp01(d / need);
                    score -= softProximityPenalty * n * n * (18f + step * 10f);
                }
                p0 = p1;
            }
        }

        // Prefer complex fill that stays off the start region, and finishes far away.
        if (enforceMinStartEndSeparation && startSeparationSoftWeight > 1e-4f)
        {
            Vector2 trackStartXZ = new Vector2(_trackStartPosition.x, _trackStartPosition.z);
            float keepOut = Mathf.Max(1f, GetStartRegionKeepOutMeters());
            float minSep = Mathf.Max(keepOut, GetEffectiveMinStartEndDistance(segLength));
            float distEnd = Vector2.Distance(endXZ, trackStartXZ);
            float distMid = Vector2.Distance(midXZ, trackStartXZ);
            float ramp = Mathf.SmoothStep(0.1f, 1f, tNorm);

            // Hard anti-cut is soft-only: punish intrusion into start / early road band.
            float intrusion = StartRegionIntrusion01(startXZ, endXZ);
            score -= startSeparationSoftWeight * intrusion * intrusion * 22f;

            float leaveStart = Mathf.Clamp01((distEnd - keepOut) / Mathf.Max(1f, minSep - keepOut));
            score += startSeparationSoftWeight * leaveStart * (0.55f + ramp) * 10f;

            if (distEnd < keepOut || distMid < keepOut)
            {
                float inside = 1f - Mathf.Clamp01(Mathf.Min(distEnd, distMid) / keepOut);
                score -= startSeparationSoftWeight * inside * inside * 18f;
            }

            if (tNorm > 0.4f)
            {
                float endNorm = Mathf.Clamp01(distEnd / minSep);
                score += startSeparationSoftWeight * endNorm * ramp * 8f;
                if (distEnd < minSep)
                {
                    float shortfall = 1f - endNorm;
                    score -= startSeparationSoftWeight * shortfall * shortfall * ramp * 12f;
                }
            }
        }

        // --- Dynamic turn personality ---
        float intentDelta = Mathf.Abs(Mathf.DeltaAngle(yaw, turnIntent));
        score -= (intentDelta / Mathf.Max(1f, maxTurnAngle)) * turnIntentFollowWeight * 6f;

        if (Mathf.Abs(yaw) > 0.75f && Mathf.Sign(yaw) == Mathf.Sign(_currentTurnDirectionSign) &&
            Mathf.Abs(_currentTurnDirectionSign) > 0.1f)
            score += turnPersistenceReward * 2f;

        score += (Mathf.Abs(yaw) / Mathf.Max(1f, maxTurnAngle)) * curveEnergyReward * (0.7f + tNorm) * 4f;

        float jerk = Mathf.Abs(Mathf.DeltaAngle(yaw, _lastCommittedYaw));
        score -= jerk * yawJerkPenalty;

        // Tiny noise so equal scores don't always pick the same side.
        score += (Random.value - 0.5f) * 0.05f;

        return score;
    }

    private float ScoreFoldInAimPenalty(Vector2 origin, Vector2 forward)
    {
        if (forward.sqrMagnitude < 1e-8f || _segments2D.Count < 3)
            return 0f;

        float sense = Mathf.Max(foldAvoidLookAheadMeters, softTrackClearanceMeters, roadWidth * 4f, 24f);
        float best = sense;
        Vector2 nearest = origin;

        int count = _segments2D.Count;
        int lastExclusive = Mathf.Max(0, count - 2); // ignore live tip + previous
        for (int i = 0; i < lastExclusive; i++)
        {
            Segment2D s = _segments2D[i];
            Vector2 mid = (s.a + s.b) * 0.5f;
            float d = Vector2.Distance(origin, mid);
            if (d < best)
            {
                best = d;
                nearest = mid;
            }
        }

        if (best >= sense)
            return 0f;

        Vector2 toNear = nearest - origin;
        if (toNear.sqrMagnitude < 1e-6f)
            return 0f;
        toNear.Normalize();

        float aim = Vector2.Dot(forward, toNear); // 1 = aiming straight at old road
        if (aim <= 0.05f)
            return 0f;

        float prox = 1f - Mathf.Clamp01(best / sense);
        return aim * aim * prox * prox * 10f;
    }

    private void UpdateFlowGoalFromStart()
    {
        Vector2 start = new Vector2(_trackStartPosition.x, _trackStartPosition.z);
        if (_hasPreferredTerrainBounds)
        {
            // Aim across the tile toward the opposite side from the start corner.
            Vector2 away = start - _preferredCenterXZ;
            if (away.sqrMagnitude < 1e-4f)
                away = new Vector2(1f, 1f);
            away.Normalize();
            _flowGoalXZ = _preferredCenterXZ - away * Mathf.Max(
                (_preferredMaxX - _preferredMinX),
                (_preferredMaxZ - _preferredMinZ)) * 0.42f;
        }
        else
        {
            Vector3 fwd = _currentRotation * Vector3.forward;
            _flowGoalXZ = start + new Vector2(fwd.x, fwd.z).normalized * 400f;
        }
    }

    private void RefreshRecentFlowHeading()
    {
        int count = _segments2D.Count;
        if (count <= 0) return;

        Vector2 sum = Vector2.zero;
        int take = Mathf.Min(8, count);
        for (int i = count - take; i < count; i++)
        {
            Segment2D s = _segments2D[i];
            Vector2 d = s.b - s.a;
            if (d.sqrMagnitude < 1e-8f) continue;
            sum += d.normalized;
        }

        if (sum.sqrMagnitude > 1e-6f)
            _recentFlowHeadingXZ = sum.normalized;
    }

    private float ScoreParallelSwitchbackPenalty(Vector2 a, Vector2 b)
    {
        Vector2 dir = b - a;
        if (dir.sqrMagnitude < 1e-8f || _segments2D.Count < 3)
            return 0f;
        dir.Normalize();

        float rejectDist = Mathf.Max(GetHardSelfClearanceMeters() * 1.35f, roadWidth * parallelRejectRoadWidths);
        float penalty = 0f;
        int lastExclusive = Mathf.Max(0, _segments2D.Count - 2);

        for (int i = 0; i < lastExclusive; i++)
        {
            Segment2D s = _segments2D[i];
            Vector2 sdir = s.b - s.a;
            if (sdir.sqrMagnitude < 1e-8f) continue;
            sdir.Normalize();

            float align = Vector2.Dot(dir, sdir);
            // Soft-penalize close reverse folds most; mild on same-direction parallels.
            float anti = Mathf.Max(0f, -align);
            float parallel = Mathf.Abs(align);
            if (parallel < 0.78f) continue;

            float dist = Mathf.Sqrt(SegmentSegmentDistanceSq(a, b, s.a, s.b));
            if (dist >= rejectDist) continue;

            float prox = 1f - Mathf.Clamp01(dist / rejectDist);
            penalty += prox * prox * (3f + anti * 12f);
        }

        return penalty;
    }

    private bool SegmentIsParallelSwitchback(Vector2 a, Vector2 b)
    {
        Vector2 dir = b - a;
        if (dir.sqrMagnitude < 1e-8f || _segments2D.Count < 3)
            return false;
        dir.Normalize();

        // Only hard-reject anti-parallel (U-turn paperclips), not same-direction neighbors.
        float rejectDist = Mathf.Max(GetHardSelfClearanceMeters() * 1.25f, roadWidth * parallelRejectRoadWidths);
        float rejectDistSq = rejectDist * rejectDist;
        int lastExclusive = Mathf.Max(0, _segments2D.Count - 2);

        for (int i = 0; i < lastExclusive; i++)
        {
            Segment2D s = _segments2D[i];
            Vector2 sdir = s.b - s.a;
            if (sdir.sqrMagnitude < 1e-8f) continue;
            sdir.Normalize();

            float anti = Vector2.Dot(dir, sdir);
            if (anti > -0.75f) continue; // not a reverse-parallel fold

            if (SegmentSegmentDistanceSq(a, b, s.a, s.b) < rejectDistSq)
                return true;
        }

        return false;
    }

    private float ScoreForwardClearance(Vector2 origin, Vector2 forward)
    {
        if (forward.sqrMagnitude < 1e-8f || _segments2D.Count == 0)
            return 0f;

        float sense = Mathf.Max(softTrackClearanceMeters, GetMinSelfClearanceMeters() * 2f, roadWidth * 4f, 20f);
        float best = sense;
        Vector2 probe = origin + forward * (sense * 0.75f);

        int count = _segments2D.Count;
        // Skip only the live tip segment.
        int lastExclusive = Mathf.Max(0, count - 1);
        for (int i = 0; i < lastExclusive; i++)
        {
            Segment2D s = _segments2D[i];
            float d = Mathf.Sqrt(SegmentSegmentDistanceSq(origin, probe, s.a, s.b));
            if (d < best) best = d;
        }

        float norm = Mathf.Clamp01(best / sense);
        return norm * 3f - (1f - norm) * 8f;
    }

    private float MinDistanceSegmentToExistingTrack(Vector2 a, Vector2 b)
    {
        if (_segments2D.Count == 0)
            return softTrackClearanceMeters * 4f;

        float best = float.PositiveInfinity;
        int count = _segments2D.Count;
        // Skip only the previous connected segment (shared vertex).
        int lastExclusive = Mathf.Max(0, count - 1);

        for (int i = 0; i < lastExclusive; i++)
        {
            Segment2D s = _segments2D[i];
            float d = Mathf.Sqrt(SegmentSegmentDistanceSq(a, b, s.a, s.b));
            if (d < best) best = d;
        }

        return float.IsInfinity(best) ? softTrackClearanceMeters * 4f : best;
    }

    // ================================================================
    //  LEGACY TURN / AVOIDANCE HELPERS (intent + repulsion)
    // ================================================================
    private float ComputeTurnYawSimple(int index, float tNorm, float maxTurnAngle, float turnChance)
    {
        float wiggleScale = Mathf.Lerp(1f, 1f + wiggleOverDistance, tNorm);
        float noiseT = _noiseOffset + index * 0.15f;
        float noise = Mathf.PerlinNoise(noiseT, 0.1234f) * 2f - 1f;
        float wiggle = noise * maxWiggleAngle * wiggleScale;

        float avoidanceBias = ComputeAvoidanceYawBias(maxTurnAngle);

        float yaw = wiggle + avoidanceBias;

        if (Random.value < turnChance)
        {
            float sign = Random.value < 0.5f ? -1f : 1f;
            float bigTurn = Random.Range(minTurnAngle, maxTurnAngle);
            yaw += sign * bigTurn;
            _currentTurnDirectionSign = sign;
        }

        return Mathf.Clamp(yaw, -maxTurnAngle, maxTurnAngle);
    }

    // ================================================================
    //  AVOIDANCE LOGIC
    // ================================================================
    private float ComputeAvoidanceYawBias(float maxTurnAngle)
    {
        if (!useAvoidance || _segments2D.Count == 0 || avoidanceRadius <= 0.01f)
            return 0f;

        Vector2 pos = new Vector2(_currentEndPosition.x, _currentEndPosition.z);
        Vector3 forward3D = _currentRotation * Vector3.forward;
        Vector2 forward2D = new Vector2(forward3D.x, forward3D.z).normalized;

        Vector2 repulsion = Vector2.zero;

        int count = _segments2D.Count;
        for (int i = 0; i < count; i++)
        {
            Segment2D s = _segments2D[i];

            Vector2 closest = ClosestPointOnSegment2D(pos, s.a, s.b);
            Vector2 toMe = pos - closest;
            float dist = toMe.magnitude;

            if (dist < 1e-3f || dist > avoidanceRadius)
                continue;

            float norm = 1f - (dist / avoidanceRadius);
            float weight = Mathf.Pow(norm, avoidanceFalloff);

            repulsion += toMe.normalized * weight;
        }

        if (repulsion.sqrMagnitude < 1e-6f)
            return 0f;

        repulsion.Normalize();

        float signedAngle = SignedAngle2D(forward2D, repulsion);
        float bias = signedAngle * avoidanceStrength;

        return Mathf.Clamp(bias, -maxTurnAngle, maxTurnAngle);
    }

    private Vector2 ClosestPointOnSegment2D(Vector2 p, Vector2 a, Vector2 b)
    {
        Vector2 ab = b - a;
        float abSqr = ab.sqrMagnitude;
        if (abSqr < 1e-6f) return a;
        float t = Vector2.Dot(p - a, ab) / abSqr;
        t = Mathf.Clamp01(t);
        return a + ab * t;
    }

    private float SignedAngle2D(Vector2 from, Vector2 to)
    {
        Vector3 from3 = new Vector3(from.x, 0f, from.y);
        Vector3 to3 = new Vector3(to.x, 0f, to.y);
        if (from3.sqrMagnitude < 1e-8f || to3.sqrMagnitude < 1e-8f)
            return 0f;
        return Vector3.SignedAngle(from3, to3, Vector3.up);
    }

    // ================================================================
    //  GLOBAL DIRECTION BIASING
    // ================================================================
    private float ApplyGlobalDirectionBias(float yaw, float maxTurnAngle)
    {
        Vector3 forward = (_currentRotation * Vector3.forward).normalized;

        if (!_hasInitializedHeading)
        {
            _globalHeadingSmoothed = forward;
            _hasInitializedHeading = true;
        }

        _globalHeadingSmoothed = Vector3.Slerp(_globalHeadingSmoothed, forward, 0.1f);
        _globalHeadingSmoothed.Normalize();

        float deviation = Vector3.SignedAngle(_globalHeadingSmoothed, _globalForwardRef, Vector3.up);

        if (Mathf.Abs(deviation) > maxHeadingDeviation)
        {
            float correctionSign = deviation > 0 ? -1f : 1f;
            float correction = correctionSign * globalAlignmentStrength * 2.0f;
            yaw += correction;
        }

        yaw = Mathf.Clamp(yaw, -maxTurnAngle, maxTurnAngle);
        return yaw;
    }

    // ================================================================
    //  COLLISION-FREE YAW SEARCH
    // ================================================================
    private bool TryFindCollisionFreeYaw(float preferredYawDeg, float maxTurnAngle, float segLength, out float resultYaw)
    {
        bool requireTerrain = ShouldRequirePreferredTerrain();
        preferredYawDeg = Mathf.Clamp(preferredYawDeg, -maxTurnAngle, maxTurnAngle);
        bool peel = requireTerrain && NeedsEdgePeel();

        if (IsYawValid(preferredYawDeg, segLength, requireTerrain))
        {
            if (!peel || YawPeelsOffEdge(preferredYawDeg, segLength))
            {
                resultYaw = preferredYawDeg;
                return true;
            }
        }

        // Hugging the border: pick the valid heading that turns back into the tile,
        // not the smallest yaw that merely stays legal (that slides along the wall).
        if (peel && TryPickBestPeelYaw(maxTurnAngle, segLength, requireTerrain, out resultYaw))
            return true;

        int samples = Mathf.Max(1, yawSearchSamples);
        if (requireTerrain)
            samples = Mathf.Max(samples, 36);

        if (TrySearchYawBand(preferredYawDeg, maxTurnAngle, segLength, requireTerrain, samples, out resultYaw))
            return true;

        // Near a tile edge the intended turn may be too small to stay on the terrain.
        // Allow a sharper bounce rather than leaving the active tile.
        if (requireTerrain)
        {
            float bounceTurn = Mathf.Max(maxTurnAngle, 95f);
            if (peel && TryPickBestPeelYaw(bounceTurn, segLength, requireTerrain, out resultYaw))
                return true;
            if (TrySearchYawBand(preferredYawDeg, bounceTurn, segLength, requireTerrain, 48, out resultYaw))
                return true;
            if (TrySearchYawBand(0f, 150f, segLength, requireTerrain, 60, out resultYaw))
                return true;
        }

        resultYaw = preferredYawDeg;
        return false;
    }

    private bool TryFindYaw(float preferredYawDeg, float maxTurnAngle, float segLength, bool requirePreferredTerrain, out float resultYaw)
    {
        preferredYawDeg = Mathf.Clamp(preferredYawDeg, -maxTurnAngle, maxTurnAngle);
        if (IsYawValid(preferredYawDeg, segLength, requirePreferredTerrain))
        {
            resultYaw = preferredYawDeg;
            return true;
        }

        int samples = Mathf.Max(1, yawSearchSamples);
        if (preferStayOnPreferredTerrain)
            samples = Mathf.Max(samples, 36);

        return TrySearchYawBand(preferredYawDeg, maxTurnAngle, segLength, requirePreferredTerrain, samples, out resultYaw);
    }

    private bool TrySearchYawBand(
        float preferredYawDeg,
        float maxTurnAngle,
        float segLength,
        bool requirePreferredTerrain,
        int samples,
        out float resultYaw)
    {
        resultYaw = preferredYawDeg;
        samples = Mathf.Max(1, samples);
        maxTurnAngle = Mathf.Max(1f, maxTurnAngle);
        float step = maxTurnAngle / samples;

        for (int i = 1; i <= samples; i++)
        {
            float delta = step * i;

            float yawPlus = Mathf.Clamp(preferredYawDeg + delta, -maxTurnAngle, maxTurnAngle);
            if (IsYawValid(yawPlus, segLength, requirePreferredTerrain))
            {
                resultYaw = yawPlus;
                return true;
            }

            float yawMinus = Mathf.Clamp(preferredYawDeg - delta, -maxTurnAngle, maxTurnAngle);
            if (IsYawValid(yawMinus, segLength, requirePreferredTerrain))
            {
                resultYaw = yawMinus;
                return true;
            }
        }

        return false;
    }

    private bool IsYawValid(float yawDeg, float segLength, bool requirePreferredTerrain = false)
    {
        // The tile itself is the bound when we must stay on one terrain.
        // A global heading cap would otherwise force the road off the far edge.
        if (!requirePreferredTerrain && constrainToGlobalDirection && maxHeadingDeviation < 179f)
        {
            Quaternion newRot = _currentRotation * Quaternion.Euler(0f, yawDeg, 0f);
            Vector3 newForward = newRot * Vector3.forward;
            float deviation = Vector3.SignedAngle(newForward, _globalForwardRef, Vector3.up);
            if (Mathf.Abs(deviation) > maxHeadingDeviation)
                return false;
        }

        Quaternion testRot = _currentRotation * Quaternion.Euler(0f, yawDeg, 0f);
        Vector3 forward3D = testRot * Vector3.forward;

        Vector3 start3D = _currentEndPosition;
        Vector3 end3D = _currentEndPosition + forward3D * segLength;

        Vector2 a = new Vector2(start3D.x, start3D.z);
        Vector2 b = new Vector2(end3D.x, end3D.z);

        if (requirePreferredTerrain && !SegmentStaysOnPreferredTerrain(a, b))
            return false;

        if (preventSelfIntersections)
        {
            if (SegmentCollidesWithExistingTrack(a, b, skipConnectedTail: true))
                return false;

            int lookAhead = selfCollisionLookAheadSegments;
            if (lookAhead > 0 && segLength > 0.01f)
            {
                Vector2 dir = b - a;
                float lenSq = dir.sqrMagnitude;
                if (lenSq > 1e-8f)
                {
                    dir /= Mathf.Sqrt(lenSq);
                    Vector2 p = b;
                    for (int k = 0; k < lookAhead; k++)
                    {
                        Vector2 q = p + dir * segLength;
                        if (SegmentCollidesWithExistingTrack(p, q, skipConnectedTail: true))
                            return false;
                        p = q;
                    }
                }
            }
        }

        // Late path must stay outside a start exclusion bubble so the finish can't curl back for a grass cut.
        if (enforceMinStartEndSeparation && minStartEndDistance > 0.01f && segmentCount > 1)
        {
            float builtNorm = (float)_segments2D.Count / (segmentCount - 1);
            if (builtNorm >= startSeparationEnforceAfterNormalized)
            {
                Vector2 startXZ = new Vector2(_trackStartPosition.x, _trackStartPosition.z);
                float minSepSq = minStartEndDistance * minStartEndDistance;
                if ((a - startXZ).sqrMagnitude < minSepSq
                    || (b - startXZ).sqrMagnitude < minSepSq
                    || PointSegmentDistanceSq(startXZ, a, b) < minSepSq)
                {
                    return false;
                }
            }
        }

        return true;
    }

    private float GetStartRegionKeepOutMeters()
    {
        if (startRegionKeepOutMeters > 0.01f)
            return startRegionKeepOutMeters;
        return Mathf.Max(roadWidth * 6f, 40f);
    }

    /// <summary>Soft-score helper: how badly a segment intrudes on the start / early road band.</summary>
    private float StartRegionIntrusion01(Vector2 a, Vector2 b)
    {
        if (!enforceMinStartEndSeparation || segmentCount <= 1) return 0f;
        float builtNorm = (float)_segments2D.Count / Mathf.Max(1, segmentCount - 1);
        if (builtNorm < startRegionKeepOutAfterNormalized) return 0f;

        float keepOut = GetStartRegionKeepOutMeters();
        if (keepOut < 0.01f) return 0f;

        Vector2 startXZ = new Vector2(_trackStartPosition.x, _trackStartPosition.z);
        float best = Mathf.Sqrt(PointSegmentDistanceSq(startXZ, a, b));
        best = Mathf.Min(best, Vector2.Distance(a, startXZ), Vector2.Distance(b, startXZ));

        int earlyMax = Mathf.Clamp(
            Mathf.CeilToInt(segmentCount * Mathf.Clamp01(startKeepOutEarlyPathFraction)),
            1,
            Mathf.Max(1, _segments2D.Count - 1));
        if (_segments2D.Count > earlyMax)
        {
            for (int i = 0; i < earlyMax; i++)
            {
                Segment2D s = _segments2D[i];
                float d = Mathf.Sqrt(SegmentSegmentDistanceSq(a, b, s.a, s.b));
                if (d < best) best = d;
            }
        }

        if (best >= keepOut) return 0f;
        return 1f - Mathf.Clamp01(best / keepOut);
    }

    private float GetHardSelfClearanceMeters()
    {
        return GetParallelSelfClearanceMeters();
    }

    private float GetMinSelfClearanceMeters()
    {
        return GetParallelSelfClearanceMeters();
    }

    /// <summary>
    /// Centerline gap required when two stretches run alongside each other.
    /// 2.0 * roadWidth + extra ≈ one full road-width of dirt between asphalt edges.
    /// </summary>
    private float GetParallelSelfClearanceMeters()
    {
        float fromWidths = roadWidth * Mathf.Max(1.15f, minSelfClearanceRoadWidths);
        return Mathf.Max(fromWidths + minSelfClearanceExtraMeters, roadWidth + 6f);
    }

    /// <summary>
    /// Corners may sit closer; parallel / anti-parallel stretches need a real off-road strip.
    /// </summary>
    private float GetRequiredClearanceMeters(Vector2 dirA, Vector2 dirB)
    {
        if (dirA.sqrMagnitude < 1e-8f || dirB.sqrMagnitude < 1e-8f)
            return GetParallelSelfClearanceMeters();

        dirA.Normalize();
        dirB.Normalize();
        float absDot = Mathf.Abs(Vector2.Dot(dirA, dirB));
        float parallelT = Mathf.SmoothStep(0.3f, 0.82f, absDot);
        float cornerNeed = roadWidth * 1.08f;
        return Mathf.Lerp(cornerNeed, GetParallelSelfClearanceMeters(), parallelT);
    }

    /// <summary>
    /// How many recent same-heading segments still count as "this road continuing",
    /// not a second nearby stretch. Must be past the parallel-gap distance along the path.
    /// </summary>
    private int GetSameChainSkipCount()
    {
        float spacing = Mathf.Max(1f, segmentLength);
        int skip = Mathf.CeilToInt(GetParallelSelfClearanceMeters() / spacing) + 1;
        return Mathf.Clamp(skip, 3, 8);
    }

    /// <summary>
    /// Returns true if segment AB is too close to / intersects existing track.
    /// When skipConnectedTail, ignores the immediately previous segment (shared vertex).
    /// Recent same-direction segments are the current road, not a packed neighbor.
    /// Anti-parallel folds are always tested so U-turns can't run 1m beside themselves.
    /// </summary>
    private bool SegmentCollidesWithExistingTrack(Vector2 a, Vector2 b, bool skipConnectedTail)
    {
        int count = _segments2D.Count;
        if (count <= 0) return false;

        int lastIndexExclusive = count;
        if (skipConnectedTail && count >= 1)
            lastIndexExclusive = count - 1;

        Vector2 adir = b - a;
        int sameChainSkip = GetSameChainSkipCount();

        for (int i = 0; i < lastIndexExclusive; i++)
        {
            Segment2D s = _segments2D[i];

            if (SegmentsProperlyIntersect(a, b, s.a, s.b))
                return true;

            Vector2 bdir = s.b - s.a;
            int age = (count - 1) - i;
            if (IsSameChainContinuation(age, adir, bdir, sameChainSkip))
                continue;

            float need = GetRequiredClearanceMeters(adir, bdir);
            float distSq = SegmentSegmentDistanceSq(a, b, s.a, s.b);
            if (distSq < need * need)
                return true;
        }

        return false;
    }

    private static bool IsSameChainContinuation(int age, Vector2 dirA, Vector2 dirB, int sameChainSkip)
    {
        if (age <= 0 || age >= sameChainSkip)
            return false;
        if (dirA.sqrMagnitude < 1e-8f || dirB.sqrMagnitude < 1e-8f)
            return false;
        // Only skip forward continuation. Negative dot = folding back / paperclip.
        return Vector2.Dot(dirA.normalized, dirB.normalized) > 0.2f;
    }

    /// <summary>Final audit: true if no proper self-intersections.</summary>
    private bool PassesSelfIntersectionPathCheck()
    {
        int count = _segments2D.Count;
        for (int i = 0; i < count; i++)
        {
            Segment2D a = _segments2D[i];
            for (int j = 0; j < i - 1; j++)
            {
                Segment2D b = _segments2D[j];
                if (SegmentsProperlyIntersect(a.a, a.b, b.a, b.b))
                    return false;
            }
        }
        return true;
    }

    private bool PassesSelfClearancePathCheck()
    {
        if (!PassesSelfIntersectionPathCheck())
            return false;

        int count = _segments2D.Count;
        int sameChainSkip = GetSameChainSkipCount();
        for (int i = 0; i < count; i++)
        {
            Segment2D a = _segments2D[i];
            Vector2 adir = a.b - a.a;
            for (int j = 0; j < i - 1; j++)
            {
                Segment2D b = _segments2D[j];
                int age = i - j;
                Vector2 bdir = b.b - b.a;
                if (IsSameChainContinuation(age, adir, bdir, sameChainSkip))
                    continue;

                float need = GetRequiredClearanceMeters(adir, bdir);
                if (SegmentSegmentDistanceSq(a.a, a.b, b.a, b.b) < need * need)
                    return false;
            }
        }

        return true;
    }

    private bool IsYawOnPreferredTerrain(float yawDeg, float segLength)
    {
        Quaternion testRot = _currentRotation * Quaternion.Euler(0f, yawDeg, 0f);
        Vector3 forward3D = testRot * Vector3.forward;
        Vector3 start3D = _currentEndPosition;
        Vector3 end3D = _currentEndPosition + forward3D * segLength;
        return SegmentStaysOnPreferredTerrain(
            new Vector2(start3D.x, start3D.z),
            new Vector2(end3D.x, end3D.z));
    }

    private void ApplyPreferredTerrainStartPlacement()
    {
        _hasPreferredTerrainBounds = false;
        Terrain terrain = ResolvePreferredTerrain();
        if (terrain == null || terrain.terrainData == null)
            return;

        Vector3 tp = terrain.transform.position;
        Vector3 sz = terrain.terrainData.size;
        _preferredMinX = tp.x;
        _preferredMaxX = tp.x + sz.x;
        _preferredMinZ = tp.z;
        _preferredMaxZ = tp.z + sz.z;
        _preferredCenterXZ = new Vector2(
            (_preferredMinX + _preferredMaxX) * 0.5f,
            (_preferredMinZ + _preferredMaxZ) * 0.5f);
        _hasPreferredTerrainBounds = true;

        Vector2 startXZ = new Vector2(_currentEndPosition.x, _currentEndPosition.z);
        float keepInset = Mathf.Max(roadWidth * 2f, preferredTerrainEdgeInset * 0.35f, 30f);
        bool startAlreadyInside = PointInRect(
            startXZ,
            _preferredMinX + keepInset,
            _preferredMaxX - keepInset,
            _preferredMinZ + keepInset,
            _preferredMaxZ - keepInset);

        // Keep the restored start pose when it already sits on the active tile.
        if (startAlreadyInside)
            return;

        if (!startAtPreferredTerrainCorner)
        {
            SnapStartOntoPreferredTerrain(keepInset);
            return;
        }

        // Start ~25% in from the corner toward center so generation isn't hugging the tile edge.
        float frac = Mathf.Clamp(preferredStartCornerInsetFraction, 0.05f, 0.45f);
        float insetX = sz.x * frac;
        float insetZ = sz.z * frac;

        TerrainCorner corner = preferredStartCorner;
        if (corner == TerrainCorner.Random)
            corner = (TerrainCorner)Random.Range(1, 5); // SW..NE

        float x;
        float z;
        switch (corner)
        {
            case TerrainCorner.SouthEast:
                x = _preferredMaxX - insetX;
                z = _preferredMinZ + insetZ;
                break;
            case TerrainCorner.NorthWest:
                x = _preferredMinX + insetX;
                z = _preferredMaxZ - insetZ;
                break;
            case TerrainCorner.NorthEast:
                x = _preferredMaxX - insetX;
                z = _preferredMaxZ - insetZ;
                break;
            case TerrainCorner.SouthWest:
            default:
                x = _preferredMinX + insetX;
                z = _preferredMinZ + insetZ;
                break;
        }

        Vector3 start = new Vector3(x, transform.position.y, z);
        Vector3 toCenter = new Vector3(_preferredCenterXZ.x - x, 0f, _preferredCenterXZ.y - z);
        if (toCenter.sqrMagnitude < 1e-4f)
            toCenter = transform.forward;

        _currentEndPosition = start;
        _trackStartPosition = start;
        _currentRotation = Quaternion.LookRotation(toCenter.normalized, Vector3.up);
        _globalForwardRef = _currentRotation * Vector3.forward;
        _hasInitializedHeading = false;
    }

    private void SnapStartOntoPreferredTerrain(float inset)
    {
        float minX = _preferredMinX + inset;
        float maxX = _preferredMaxX - inset;
        float minZ = _preferredMinZ + inset;
        float maxZ = _preferredMaxZ - inset;
        if (maxX <= minX || maxZ <= minZ)
        {
            minX = _preferredMinX;
            maxX = _preferredMaxX;
            minZ = _preferredMinZ;
            maxZ = _preferredMaxZ;
        }

        float x = Mathf.Clamp(_currentEndPosition.x, minX, maxX);
        float z = Mathf.Clamp(_currentEndPosition.z, minZ, maxZ);
        Vector3 start = new Vector3(x, _currentEndPosition.y, z);
        Vector3 toCenter = new Vector3(_preferredCenterXZ.x - x, 0f, _preferredCenterXZ.y - z);
        if (toCenter.sqrMagnitude < 1e-4f)
            toCenter = _currentRotation * Vector3.forward;

        _currentEndPosition = start;
        _trackStartPosition = start;
        _currentRotation = Quaternion.LookRotation(toCenter.normalized, Vector3.up);
        _globalForwardRef = _currentRotation * Vector3.forward;
        _hasInitializedHeading = false;
    }

    private bool ShouldRequirePreferredTerrain()
    {
        if (!preferStayOnPreferredTerrain || !_hasPreferredTerrainBounds)
            return false;
        if (hardClampToPreferredTerrain)
            return true;
        return _stuckRelaxLevel < 3;
    }

    private Terrain ResolvePreferredTerrain()
    {
        if (preferredTerrain != null && preferredTerrain.terrainData != null
            && preferredTerrain.gameObject.activeInHierarchy)
            return preferredTerrain;

        // If only one terrain is enabled, that is the track's tile.
        Terrain[] active = Terrain.activeTerrains;
        if (active != null)
        {
            Terrain only = null;
            int n = 0;
            for (int i = 0; i < active.Length; i++)
            {
                Terrain t = active[i];
                if (t == null || t.terrainData == null) continue;
                only = t;
                n++;
            }
            if (n == 1)
                return only;
        }

        Terrain[] found = FindObjectsOfType<Terrain>();
        // Prefer the tile that contains world origin (main terrain center in this project).
        for (int i = 0; i < found.Length; i++)
        {
            Terrain t = found[i];
            if (t == null || t.terrainData == null || !t.gameObject.activeInHierarchy) continue;
            if (TrackTerrainOverlap.ContainsXZ(t, 0f, 0f))
                return t;
        }

        if (Terrain.activeTerrain != null && Terrain.activeTerrain.terrainData != null)
            return Terrain.activeTerrain;

        for (int i = 0; i < found.Length; i++)
        {
            if (found[i] != null && found[i].terrainData != null && found[i].gameObject.activeInHierarchy)
                return found[i];
        }

        return null;
    }

    private float ApplyPreferredTerrainStayBias(float yaw, float maxTurnAngle)
    {
        if (!preferStayOnPreferredTerrain || !_hasPreferredTerrainBounds || preferredTerrainStayBias <= 1e-4f)
            return yaw;

        Vector2 pos = new Vector2(_currentEndPosition.x, _currentEndPosition.z);
        float distToEdge = MinDistanceToPreferredInsetEdge(pos);
        float zone = Mathf.Max(1f, preferredTerrainEdgeSteerMeters);
        if (distToEdge >= zone)
            return yaw;

        Vector2 inward = GetNearestEdgeInwardNormal(pos);
        if (inward.sqrMagnitude < 1e-6f)
            return yaw;

        Vector3 forward = (_currentRotation * Vector3.forward);
        Vector2 fwd = new Vector2(forward.x, forward.z);
        if (fwd.sqrMagnitude < 1e-6f)
            return yaw;
        fwd.Normalize();

        float align = Vector2.Dot(fwd, inward);
        float edgeT = 1f - Mathf.Clamp01(distToEdge / zone);
        float peelYaw = SignedAngle2D(fwd, inward);

        // Already heading back into the tile with room to spare: keep organic turns.
        bool hugging = align < 0.28f || distToEdge < Mathf.Min(32f, zone * 0.28f);
        float strength = hugging
            ? Mathf.Clamp01(Mathf.Lerp(0.55f, 1f, edgeT) * Mathf.Max(0.5f, preferredTerrainStayBias))
            : edgeT * preferredTerrainStayBias * 0.22f;

        yaw = Mathf.LerpAngle(yaw, peelYaw, strength);
        return Mathf.Clamp(yaw, -maxTurnAngle, maxTurnAngle);
    }

    private float GetEdgePeelTurnBudget(float maxTurnAngle)
    {
        if (!NeedsEdgePeel())
            return maxTurnAngle;

        Vector2 pos = new Vector2(_currentEndPosition.x, _currentEndPosition.z);
        float dist = MinDistanceToPreferredInsetEdge(pos);
        float zone = Mathf.Max(1f, preferredTerrainEdgeSteerMeters);
        float edgeT = 1f - Mathf.Clamp01(dist / zone);
        float peelCap = Mathf.Max(maxTurnAngle, Mathf.Min(36f, Mathf.Max(endMaxTurnAngle, 24f)));
        return Mathf.Lerp(maxTurnAngle, peelCap, edgeT);
    }

    private bool NeedsEdgePeel()
    {
        if (!_hasPreferredTerrainBounds)
            return false;

        Vector2 pos = new Vector2(_currentEndPosition.x, _currentEndPosition.z);
        float dist = MinDistanceToPreferredInsetEdge(pos);
        float zone = Mathf.Max(1f, preferredTerrainEdgeSteerMeters);
        if (dist >= zone)
            return false;

        Vector3 forward = _currentRotation * Vector3.forward;
        Vector2 fwd = new Vector2(forward.x, forward.z);
        if (fwd.sqrMagnitude < 1e-6f)
            return dist < 24f;
        fwd.Normalize();

        float align = Vector2.Dot(fwd, GetNearestEdgeInwardNormal(pos));
        return align < 0.28f || dist < Mathf.Min(32f, zone * 0.28f);
    }

    private bool YawPeelsOffEdge(float yawDeg, float segLength)
    {
        GetYawSegmentXZ(yawDeg, segLength, out Vector2 startXZ, out Vector2 endXZ, out Vector2 fwdXZ);
        float distStart = MinDistanceToPreferredInsetEdge(startXZ);
        float distEnd = MinDistanceToPreferredInsetEdge(endXZ);
        float align = Vector2.Dot(fwdXZ, GetNearestEdgeInwardNormal(startXZ));
        return distEnd > distStart + 0.4f || align > 0.22f;
    }

    private bool TryPickBestPeelYaw(float maxTurnAngle, float segLength, bool requireTerrain, out float resultYaw)
    {
        resultYaw = 0f;
        float best = float.NegativeInfinity;
        bool found = false;
        int samples = 48;
        maxTurnAngle = Mathf.Max(1f, maxTurnAngle);
        for (int i = 0; i <= samples; i++)
        {
            float yaw = Mathf.Lerp(-maxTurnAngle, maxTurnAngle, i / (float)samples);
            if (!IsYawValid(yaw, segLength, requireTerrain))
                continue;
            float score = ScoreEdgePeelYaw(yaw, segLength);
            if (score > best)
            {
                best = score;
                resultYaw = yaw;
                found = true;
            }
        }
        return found;
    }

    private float ScoreEdgePeelYaw(float yaw, float segLength)
    {
        GetYawSegmentXZ(yaw, segLength, out Vector2 startXZ, out Vector2 endXZ, out Vector2 fwdXZ);
        float distStart = MinDistanceToPreferredInsetEdge(startXZ);
        float zone = Mathf.Max(1f, preferredTerrainEdgeSteerMeters);
        if (distStart >= zone)
            return 0f;

        Vector2 inward = GetNearestEdgeInwardNormal(startXZ);
        float align = Vector2.Dot(fwdXZ, inward);
        float distEnd = MinDistanceToPreferredInsetEdge(endXZ);

        float score = align * 18f;
        score += (distEnd - distStart) * 2.2f;
        score += distEnd * 0.04f;
        if (align < 0.12f)
            score -= 12f;
        score -= Mathf.Abs(yaw) * 0.015f;
        return score;
    }

    private void GetYawSegmentXZ(float yawDeg, float segLength, out Vector2 startXZ, out Vector2 endXZ, out Vector2 fwdXZ)
    {
        Quaternion testRot = _currentRotation * Quaternion.Euler(0f, yawDeg, 0f);
        Vector3 forward3D = testRot * Vector3.forward;
        Vector3 start3D = _currentEndPosition;
        Vector3 end3D = start3D + forward3D * segLength;
        startXZ = new Vector2(start3D.x, start3D.z);
        endXZ = new Vector2(end3D.x, end3D.z);
        fwdXZ = new Vector2(forward3D.x, forward3D.z);
        if (fwdXZ.sqrMagnitude > 1e-8f)
            fwdXZ.Normalize();
    }

    /// <summary>
    /// Inward normal of the nearest preferred-terrain border(s). Corners blend both axes
    /// so the road peels diagonally instead of sliding along one wall toward the other.
    /// </summary>
    private Vector2 GetNearestEdgeInwardNormal(Vector2 p)
    {
        if (!TryGetPreferredInsetRect(out float minX, out float maxX, out float minZ, out float maxZ))
            return Vector2.zero;

        float zone = Mathf.Max(1f, preferredTerrainEdgeSteerMeters);
        float dLeft = p.x - minX;
        float dRight = maxX - p.x;
        float dBottom = p.y - minZ;
        float dTop = maxZ - p.y;

        Vector2 n = Vector2.zero;
        if (dLeft < zone) n.x += 1f - Mathf.Clamp01(dLeft / zone);
        if (dRight < zone) n.x -= 1f - Mathf.Clamp01(dRight / zone);
        if (dBottom < zone) n.y += 1f - Mathf.Clamp01(dBottom / zone);
        if (dTop < zone) n.y -= 1f - Mathf.Clamp01(dTop / zone);

        if (n.sqrMagnitude < 1e-6f)
        {
            Vector2 toCenter = _preferredCenterXZ - p;
            if (toCenter.sqrMagnitude < 1e-6f)
                return Vector2.zero;
            return toCenter.normalized;
        }
        return n.normalized;
    }

    /// <summary>
    /// Rotate a 2D heading away from the nearest terrain border so polylines don't clamp-slide along it.
    /// </summary>
    private void SteerDirOffPreferredEdge(Vector2 pos, ref Vector2 dir)
    {
        if (!_hasPreferredTerrainBounds || dir.sqrMagnitude < 1e-8f)
            return;

        float dist = MinDistanceToPreferredInsetEdge(pos);
        float zone = Mathf.Max(40f, preferredTerrainEdgeSteerMeters);
        if (dist >= zone)
            return;

        Vector2 inward = GetNearestEdgeInwardNormal(pos);
        if (inward.sqrMagnitude < 1e-6f)
            return;

        float align = Vector2.Dot(dir.normalized, inward);
        if (align >= 0.3f && dist > 20f)
            return;

        float edgeT = 1f - Mathf.Clamp01(dist / zone);
        dir = Vector2.Lerp(dir, inward, Mathf.Lerp(0.2f, 0.65f, edgeT));
        if (dir.sqrMagnitude > 1e-8f)
            dir.Normalize();
    }

    private bool TryGetPreferredInsetRect(out float minX, out float maxX, out float minZ, out float maxZ)
    {
        minX = maxX = minZ = maxZ = 0f;
        if (!_hasPreferredTerrainBounds)
            return false;

        float roadPad = roadWidth * 0.5f + preferredTerrainRoadHalfPadding;
        float inset = Mathf.Max(roadPad, preferredTerrainEdgeInset * 0.25f);
        minX = _preferredMinX + inset;
        maxX = _preferredMaxX - inset;
        minZ = _preferredMinZ + inset;
        maxZ = _preferredMaxZ - inset;
        if (maxX <= minX || maxZ <= minZ)
        {
            minX = _preferredMinX + roadPad;
            maxX = _preferredMaxX - roadPad;
            minZ = _preferredMinZ + roadPad;
            maxZ = _preferredMaxZ - roadPad;
        }
        return maxX > minX && maxZ > minZ;
    }

    private bool SegmentStaysOnPreferredTerrain(Vector2 a, Vector2 b)
    {
        if (!TryGetPreferredInsetRect(out float minX, out float maxX, out float minZ, out float maxZ))
            return true;

        // Sample start, mid, end so long segments can't clip a corner.
        Vector2 mid = (a + b) * 0.5f;
        return PointInRect(a, minX, maxX, minZ, maxZ)
               && PointInRect(mid, minX, maxX, minZ, maxZ)
               && PointInRect(b, minX, maxX, minZ, maxZ);
    }

    private bool PassesPreferredTerrainPathCheck()
    {
        if (!_hasPreferredTerrainBounds)
            return true;

        for (int i = 0; i < _segments2D.Count; i++)
        {
            Segment2D s = _segments2D[i];
            if (!SegmentStaysOnPreferredTerrain(s.a, s.b))
                return false;
        }

        return true;
    }

    private float MinDistanceToPreferredInsetEdge(Vector2 p)
    {
        if (!TryGetPreferredInsetRect(out float minX, out float maxX, out float minZ, out float maxZ))
            return edgeComfortZoneMeters;

        if (p.x < minX || p.x > maxX || p.y < minZ || p.y > maxZ)
            return 0f;

        return Mathf.Min(
            p.x - minX,
            maxX - p.x,
            p.y - minZ,
            maxZ - p.y);
    }

    private static bool PointInRect(Vector2 p, float minX, float maxX, float minZ, float maxZ)
    {
        return p.x >= minX && p.x <= maxX && p.y >= minZ && p.y <= maxZ;
    }

    private float GetEffectiveMinStartEndDistance(float segLength)
    {
        float pathLen = Mathf.Max(1, segmentCount) * Mathf.Max(0.001f, segLength);
        float fromFraction = pathLen * Mathf.Clamp01(minStartEndDistancePathFraction);
        return Mathf.Max(0f, minStartEndDistance, fromFraction);
    }

    private bool PassesStartEndSeparationCheck(float segLength)
    {
        if (!enforceMinStartEndSeparation)
            return true;

        float minSep = GetEffectiveMinStartEndDistance(segLength);
        if (minSep <= 0.01f)
            return true;

        Vector3 start = transform.position;
        Vector3 end = _currentEndPosition;
        if (_junctionPathPoints.Count > 0)
        {
            start = _junctionPathPoints[0];
            end = _junctionPathPoints[_junctionPathPoints.Count - 1];
        }

        Vector2 s = new Vector2(start.x, start.z);
        Vector2 e = new Vector2(end.x, end.z);
        return (e - s).sqrMagnitude >= minSep * minSep;
    }

    private static float PointSegmentDistanceSq(Vector2 p, Vector2 a, Vector2 b)
    {
        Vector2 ab = b - a;
        float abLenSq = ab.sqrMagnitude;
        if (abLenSq < 1e-8f)
            return (p - a).sqrMagnitude;

        float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / abLenSq);
        Vector2 closest = a + ab * t;
        return (p - closest).sqrMagnitude;
    }

    // ================================================================
    //  SIMPLE 2D GEOMETRY HELPERS
    // ================================================================
    private float SegmentSegmentDistanceSq(Vector2 p1, Vector2 q1, Vector2 p2, Vector2 q2)
    {
        Vector2 u = q1 - p1;
        Vector2 v = q2 - p2;
        Vector2 w = p1 - p2;

        float a = Vector2.Dot(u, u);
        float b = Vector2.Dot(u, v);
        float c = Vector2.Dot(u, w);
        float d = Vector2.Dot(v, v);
        float e = Vector2.Dot(v, w);

        const float EPS = 1e-6f;

        float D = a * d - b * b;
        float sc, sN, sD = D;
        float tc, tN, tD = D;

        if (D < EPS)
        {
            sN = 0.0f; sD = 1.0f;
            tN = e; tD = d;
        }
        else
        {
            sN = (b * e - c * d);
            tN = (a * e - b * c);
            if (sN < 0.0f)
            {
                sN = 0.0f;
                tN = e;
                tD = d;
            }
            else if (sN > sD)
            {
                sN = sD;
                tN = e + b;
                tD = d;
            }
        }

        if (tN < 0.0f)
        {
            tN = 0.0f;
            if (-c < 0.0f) sN = 0.0f;
            else if (-c > a) sN = sD;
            else
            {
                sN = -c;
                sD = a;
            }
        }
        else if (tN > tD)
        {
            tN = tD;
            if ((-c + b) < 0.0f) sN = 0;
            else if ((-c + b) > a) sN = sD;
            else
            {
                sN = (-c + b);
                sD = a;
            }
        }

        sc = (Mathf.Abs(sN) < EPS ? 0.0f : sN / sD);
        tc = (Mathf.Abs(tN) < EPS ? 0.0f : tN / tD);

        Vector2 dP = w + (u * sc) - (v * tc);
        return dP.sqrMagnitude;
    }

    private int Orientation(Vector2 a, Vector2 b, Vector2 c)
    {
        float val = (b.x - a.x) * (c.y - a.y) - (b.y - a.y) * (c.x - a.x);
        const float eps = 1e-6f;
        if (val > eps) return 1;   // CCW
        if (val < -eps) return -1; // CW
        return 0;                  // collinear
    }

    private bool OnSegment(Vector2 a, Vector2 b, Vector2 p)
    {
        return p.x >= Mathf.Min(a.x, b.x) - 1e-6f &&
               p.x <= Mathf.Max(a.x, b.x) + 1e-6f &&
               p.y >= Mathf.Min(a.y, b.y) - 1e-6f &&
               p.y <= Mathf.Max(a.y, b.y) + 1e-6f;
    }

    private bool SegmentsProperlyIntersect(Vector2 p1, Vector2 p2, Vector2 p3, Vector2 p4)
    {
        int o1 = Orientation(p1, p2, p3);
        int o2 = Orientation(p1, p2, p4);
        int o3 = Orientation(p3, p4, p1);
        int o4 = Orientation(p3, p4, p2);

        if (o1 != o2 && o3 != o4)
            return true;

        if (o1 == 0 && OnSegment(p1, p2, p3)) return true;
        if (o2 == 0 && OnSegment(p1, p2, p4)) return true;
        if (o3 == 0 && OnSegment(p3, p4, p1)) return true;
        if (o4 == 0 && OnSegment(p3, p4, p2)) return true;

        return false;
    }

    // ================================================================
    //  CLEAR HELPERS
    // ================================================================
    private void ClearTrackImmediateSafe()
    {
#if UNITY_EDITOR
        if (!Application.isPlaying)
        {
            EditorApplication.delayCall += () =>
            {
                foreach (var seg in _spawnedSegments)
                    if (seg != null)
                        Object.DestroyImmediate(seg.gameObject);

                foreach (Transform child in transform)
                    if (child != null)
                        Object.DestroyImmediate(child.gameObject);

                _spawnedSegments.Clear();

                if (_meshFilter != null && _meshFilter.sharedMesh != null)
                    DestroyImmediate(_meshFilter.sharedMesh);

                if (_meshCollider != null)
                    _meshCollider.sharedMesh = null;

                _pathPoints.Clear();
                _junctionPathPoints.Clear();
                _segments2D.Clear();
            };
            return;
        }
#endif
        foreach (Transform child in transform)
            Destroy(child.gameObject);

        _spawnedSegments.Clear();

        if (_meshFilter != null && _meshFilter.sharedMesh != null)
            Destroy(_meshFilter.sharedMesh);

        if (_meshCollider != null)
            _meshCollider.sharedMesh = null;

        _pathPoints.Clear();
        _junctionPathPoints.Clear();
        _segments2D.Clear();
    }

    // ================================================================
    //  ROAD MESH GENERATION + COLLIDER
    // ================================================================
    private void CacheMeshComponents()
    {
        _meshFilter = GetComponent<MeshFilter>();
        if (!_meshFilter) _meshFilter = gameObject.AddComponent<MeshFilter>();

        _meshRenderer = GetComponent<MeshRenderer>();
        if (!_meshRenderer) _meshRenderer = gameObject.AddComponent<MeshRenderer>();

        _meshCollider = GetComponent<MeshCollider>();
        if (!_meshCollider) _meshCollider = gameObject.AddComponent<MeshCollider>();

        if (roadMaterial != null)
            _meshRenderer.sharedMaterial = roadMaterial;

        _meshCollider.convex = false;
    }

    private void BuildRoadMeshFromPath()
    {
        List<Vector3> rawPath = SelectMeshSourcePath();
        if (rawPath == null || rawPath.Count < 2) return;

        List<Vector3> path = useSmoothing
            ? GenerateSmoothedPath(rawPath, smoothingSubdivisionsPerSegment)
            : new List<Vector3>(rawPath);

        path = DedupePathPoints(path);

        if (path.Count < 2) return;

        int count = path.Count;

        // Cumulative arc length along the centerline (shared V for both road edges).
        float[] cumulative = new float[count];
        float totalLength = 0f;
        cumulative[0] = 0f;
        for (int i = 1; i < count; i++)
        {
            totalLength += Vector3.Distance(path[i], path[i - 1]);
            cumulative[i] = totalLength;
        }

        Vector3[] verts = new Vector3[count * 2];
        Vector2[] uvs = new Vector2[count * 2];
        Vector2[] uv2s = new Vector2[count * 2];
        Vector4[] tangents = new Vector4[count * 2];
        int[] tris = new int[(count - 1) * 6];

        float halfWidth = roadWidth * 0.5f;

        for (int i = 0; i < count; i++)
        {
            Vector3 pos = path[i];
            Vector3 forward = TrackPathSampling.ComputeMiteredForward(path, i);
            Vector3 right = Vector3.Cross(Vector3.up, forward).normalized;
            if (right.sqrMagnitude < 1e-8f)
                right = Vector3.right;

            int L = i * 2;
            int R = i * 2 + 1;

            verts[L] = pos - right * halfWidth;
            verts[R] = pos + right * halfWidth;

            float v = cumulative[i] * uvTiling;
            uvs[L] = new Vector2(0f, v);
            uvs[R] = new Vector2(1f, v);

            float t = (totalLength > 0f) ? (cumulative[i] / totalLength) : 0f;
            uv2s[L] = new Vector2(0f, t);
            uv2s[R] = new Vector2(1f, t);

            tangents[L] = new Vector4(right.x, right.y, right.z, 1f);
            tangents[R] = tangents[L];
        }

        int ti = 0;
        for (int i = 0; i < count - 1; i++)
        {
            int i0 = i * 2;
            int i1 = i * 2 + 1;
            int i2 = (i + 1) * 2;
            int i3 = (i + 1) * 2 + 1;

            tris[ti++] = i0;
            tris[ti++] = i2;
            tris[ti++] = i1;

            tris[ti++] = i2;
            tris[ti++] = i3;
            tris[ti++] = i1;
        }

        Mesh m = new Mesh
        {
            name = "ProceduralRoadMesh",
            vertices = verts,
            uv = uvs,
            uv2 = uv2s,
            tangents = tangents,
            triangles = tris
        };
        if (verts.Length > 65000)
            m.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;

        m.RecalculateNormals();
        m.RecalculateBounds();

        _meshFilter.sharedMesh = m;
        if (_meshCollider != null)
            _meshCollider.sharedMesh = m;
    }

    private List<Vector3> SelectMeshSourcePath()
    {
        if (meshFromSegmentJunctions && _junctionPathPoints.Count >= 2)
            return _junctionPathPoints;
        return _pathPoints;
    }

    /// <summary>Average incoming/outgoing tangents so cross-sections miter at corners (prevents lane-marking shear).</summary>
    private static Vector3 ComputeMiteredForward(IReadOnlyList<Vector3> path, int i) =>
        TrackPathSampling.ComputeMiteredForward(path, i);

    private static List<Vector3> DedupePathPoints(List<Vector3> path, float minDist = 0.02f)
    {
        if (path == null || path.Count < 2) return path;

        float minSqr = minDist * minDist;
        var result = new List<Vector3>(path.Count) { path[0] };
        for (int i = 1; i < path.Count; i++)
        {
            if ((path[i] - result[result.Count - 1]).sqrMagnitude > minSqr)
                result.Add(path[i]);
        }

        if (result.Count < 2)
            result.Add(path[path.Count - 1]);

        return result;
    }

    // ================================================================
    //  PATH SMOOTHING
    // ================================================================
    private List<Vector3> GenerateSmoothedPath(List<Vector3> raw, int subdivisions)
    {
        var res = new List<Vector3>();
        int n = raw.Count;

        res.Add(raw[0]);

        for (int i = 0; i < n - 1; i++)
        {
            Vector3 p0 = raw[Mathf.Max(i - 1, 0)];
            Vector3 p1 = raw[i];
            Vector3 p2 = raw[i + 1];
            Vector3 p3 = raw[Mathf.Min(i + 2, n - 1)];

            for (int s = 1; s <= subdivisions; s++)
            {
                float t = s / (float)subdivisions;
                res.Add(CatmullRom(p0, p1, p2, p3, t));
            }
        }

        return res;
    }

    private Vector3 CatmullRom(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t)
    {
        float t2 = t * t;
        float t3 = t2 * t;

        return 0.5f * (
            (2f * p1) +
            (-p0 + p2) * t +
            (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 +
            (-p0 + 3f * p1 - 3f * p2 + p3) * t3
        );
    }

    // ================================================================
    //  START POINT FOR CAR SPAWN
    // ================================================================
    public void GetStartPoint(out Vector3 pos, out Vector3 forward)
    {
        var pts = new List<Vector3>();
        FillRoadMeshCenterPath(pts);

        if (pts.Count < 2)
        {
            pos = transform.position;
            forward = transform.forward;
            return;
        }

        pos = pts[0];
        forward = TrackPathSampling.ComputeMiteredForward(pts, 0);
        forward.y = 0f;
        if (forward.sqrMagnitude < 1e-6f)
            forward = (pts[1] - pts[0]).normalized;
        forward.Normalize();
    }

    /// <summary>Last centerline point + travel forward (same source as the road mesh / distance meter).</summary>
    public void GetEndPoint(out Vector3 pos, out Vector3 forward)
    {
        var pts = new List<Vector3>();
        FillRoadMeshCenterPath(pts);

        if (pts.Count < 2)
        {
            pos = transform.position;
            forward = transform.forward;
            return;
        }

        int last = pts.Count - 1;
        pos = pts[last];
        forward = TrackPathSampling.ComputeMiteredForward(pts, last);
        forward.y = 0f;
        if (forward.sqrMagnitude < 1e-6f)
            forward = (pts[last] - pts[last - 1]).normalized;
        forward.Normalize();
    }

    // ================================================================
    //  GIZMOS
    // ================================================================
    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.yellow;
        for (int i = 0; i < _pathPoints.Count - 1; i++)
            Gizmos.DrawLine(_pathPoints[i], _pathPoints[i + 1]);
    }
}
