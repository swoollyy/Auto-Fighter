using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Dedicated spawner for NPC traffic cars along the procedural track.
/// Spawns cars ahead of the player that drive forward.
/// Mirrors TrackObstacleSpawner patterns for consistency.
/// </summary>
public class NPCTrafficCarSpawner : MonoBehaviour, ITrackSpawnQueueSource
{
    [Header("References")]
    [SerializeField] private ProceduralTrackGenerator trackGenerator;
    [SerializeField] private Transform playerTransform;
    [SerializeField] private Transform carParent;

    [Header("Spawn Mode")]
    [Tooltip("If true, fills ahead of player on InitializeForRun.")]
    [SerializeField] private bool preSpawnOnInitialize = true;

    [Tooltip("If true, continues spawning ahead while driving.")]
    [SerializeField] private bool streamSpawnDuringRun = true;

    [Header("Car Prefabs")]
    [SerializeField] private List<NPCCarType> carTypes = new List<NPCCarType>();

    [Header("Path Sampling")]
    [SerializeField] private bool useSmoothing = true;
    [SerializeField, Min(1)] private int smoothingSubdivisionsPerSegment = 6;
    
    [Header("Spawn Settings")]
    [Tooltip("Ideal spacing between potential spawn slots (meters).")]
    [SerializeField] private float carSpacing = 80f;

    [SerializeField] private int maxActiveCars = 8;

    [Tooltip("Minimum distance in front of the player where new cars spawn.")]
    [SerializeField] private float minSpawnDistanceAhead = 100f;

    [Tooltip("Maximum distance in front of the player to spawn cars.")]
    [SerializeField] private float maxSpawnDistanceAhead = 300f;

    [Header("Behind Spawning")]
    [Tooltip("Allow spawning cars behind the player.")]
    [SerializeField] private bool allowSpawnBehind = true;

    [Tooltip("Minimum distance behind player to spawn (must be outside camera view).")]
    [SerializeField] private float minSpawnDistanceBehind = 40f;

    [Tooltip("Maximum distance behind player to spawn.")]
    [SerializeField] private float maxSpawnDistanceBehind = 150f;

    [Header("Camera Culling")]
    [Tooltip("Reference to main camera (auto-finds if null).")]
    [SerializeField] private Camera mainCamera;

    [Tooltip("Extra margin outside viewport to ensure car is fully off-screen before spawning.")]
    [SerializeField] private float viewportMargin = 0.1f;

    [Header("Initial Pre-Spawn")]
    [Tooltip("How far ahead (from start) we pre-fill cars before the run begins.")]
    [SerializeField] private float initialPreSpawnDistance = 200f;

    [Header("Despawn")]
    [Tooltip("Despawn cars whose *current* position along the track spline is this far behind the player (uses each NPC’s live progress, not its spawn slot).")]
    [SerializeField] private float despawnBehindDistance = 30f;

    [Tooltip("Also despawn crashed cars after this duration.")]
    [SerializeField] private float despawnCrashedAfter = 8f;

    [Header("Randomization")]
    [SerializeField] private float distanceJitter = 20f;

    [Tooltip("Chance to spawn a car at each slot.")]
    [SerializeField, Range(0f, 1f)] private float spawnChancePerSlot = 0.6f;

    [Tooltip("Spawn chance multiplier based on distance (0=start, 1=end).")]
    [SerializeField] private AnimationCurve spawnChanceByDistance = AnimationCurve.Linear(0f, 0.3f, 1f, 1f);

    [Header("Lane Assignment")]
    [Tooltip("Fraction of road half-width used for lane offset.")]
    [SerializeField, Range(0f, 1f)] private float lateralFraction = 0.7f;

    [SerializeField] private float edgeMargin = 0.8f;

    [Tooltip("If true, prefer spawning in lanes (left/right) rather than center.")]
    [SerializeField] private bool preferLanes = true;

    [Tooltip("Chance to spawn in the oncoming lane (driving toward player).")]
    [SerializeField, Range(0f, 1f)] private float oncomingLaneChance = 0.15f;

    [Header("Spawn Safety Check")]
    [SerializeField] private bool avoidImmediateObstacleHits = true;

    [Tooltip("How far ahead along the path we check for blockers from the spawn point.")]
    [SerializeField] private float spawnLookaheadDistance = 40f;

    [Tooltip("Distance between check samples along the path.")]
    [SerializeField] private float spawnLookaheadStep = 4f;

    [Tooltip("Approx radius of the car for overlap checks.")]
    [SerializeField] private float spawnProbeRadius = 1.2f;

    [Tooltip("Layers that should block NPC car spawns (obstacles, parked cars, etc).")]
    [SerializeField] private LayerMask spawnBlockerLayers;


    [Header("Raycast")]
    [SerializeField] private LayerMask roadLayer;
    [SerializeField] private float raycastStartHeight = 6f;
    [SerializeField] private float raycastDownDistance = 20f;
    [SerializeField] private float carHeightOffset = 0.1f;

    [Header("Timing")]
    [SerializeField] private float updateInterval = 0.4f;

    [Header("Debug")]
    [SerializeField] private bool verboseDebug = false;

    // ------------------------------------------------------------------------
    // Per-trial config (TrialConfig). ApplyConfig copies a trial's NpcTrafficSettings
    // into these fields (call BEFORE InitializeForRun). CaptureConfig snapshots the
    // current values into a settings object (used by the editor baker).
    // ------------------------------------------------------------------------
    public void ApplyConfig(TrialConfig.NpcTrafficSettings s)
    {
        if (s == null || !s.overrideNpcTraffic) return;

        preSpawnOnInitialize = s.preSpawnOnInitialize;
        streamSpawnDuringRun = s.streamSpawnDuringRun;

        carTypes = s.carTypes != null ? new List<NPCCarType>(s.carTypes) : new List<NPCCarType>();

        useSmoothing = s.useSmoothing;
        smoothingSubdivisionsPerSegment = s.smoothingSubdivisionsPerSegment;

        carSpacing = s.carSpacing;
        maxActiveCars = s.maxActiveCars;
        minSpawnDistanceAhead = s.minSpawnDistanceAhead;
        maxSpawnDistanceAhead = s.maxSpawnDistanceAhead;

        allowSpawnBehind = s.allowSpawnBehind;
        minSpawnDistanceBehind = s.minSpawnDistanceBehind;
        maxSpawnDistanceBehind = s.maxSpawnDistanceBehind;

        viewportMargin = s.viewportMargin;

        initialPreSpawnDistance = s.initialPreSpawnDistance;
        despawnBehindDistance = s.despawnBehindDistance;
        despawnCrashedAfter = s.despawnCrashedAfter;

        distanceJitter = s.distanceJitter;
        spawnChancePerSlot = s.spawnChancePerSlot;
        spawnChanceByDistance = s.spawnChanceByDistance;

        lateralFraction = s.lateralFraction;
        edgeMargin = s.edgeMargin;
        preferLanes = s.preferLanes;
        oncomingLaneChance = s.oncomingLaneChance;

        avoidImmediateObstacleHits = s.avoidImmediateObstacleHits;
        spawnLookaheadDistance = s.spawnLookaheadDistance;
        spawnLookaheadStep = s.spawnLookaheadStep;
        spawnProbeRadius = s.spawnProbeRadius;
        spawnBlockerLayers = s.spawnBlockerLayers;

        roadLayer = s.roadLayer;
        raycastStartHeight = s.raycastStartHeight;
        raycastDownDistance = s.raycastDownDistance;
        carHeightOffset = s.carHeightOffset;

        updateInterval = s.updateInterval;
        verboseDebug = s.verboseDebug;
    }

    public TrialConfig.NpcTrafficSettings CaptureConfig()
    {
        return new TrialConfig.NpcTrafficSettings
        {
            overrideNpcTraffic = true,
            preSpawnOnInitialize = preSpawnOnInitialize,
            streamSpawnDuringRun = streamSpawnDuringRun,
            carTypes = carTypes != null ? new List<NPCCarType>(carTypes) : new List<NPCCarType>(),
            useSmoothing = useSmoothing,
            smoothingSubdivisionsPerSegment = smoothingSubdivisionsPerSegment,
            carSpacing = carSpacing,
            maxActiveCars = maxActiveCars,
            minSpawnDistanceAhead = minSpawnDistanceAhead,
            maxSpawnDistanceAhead = maxSpawnDistanceAhead,
            allowSpawnBehind = allowSpawnBehind,
            minSpawnDistanceBehind = minSpawnDistanceBehind,
            maxSpawnDistanceBehind = maxSpawnDistanceBehind,
            viewportMargin = viewportMargin,
            initialPreSpawnDistance = initialPreSpawnDistance,
            despawnBehindDistance = despawnBehindDistance,
            despawnCrashedAfter = despawnCrashedAfter,
            distanceJitter = distanceJitter,
            spawnChancePerSlot = spawnChancePerSlot,
            spawnChanceByDistance = spawnChanceByDistance,
            lateralFraction = lateralFraction,
            edgeMargin = edgeMargin,
            preferLanes = preferLanes,
            oncomingLaneChance = oncomingLaneChance,
            avoidImmediateObstacleHits = avoidImmediateObstacleHits,
            spawnLookaheadDistance = spawnLookaheadDistance,
            spawnLookaheadStep = spawnLookaheadStep,
            spawnProbeRadius = spawnProbeRadius,
            spawnBlockerLayers = spawnBlockerLayers,
            roadLayer = roadLayer,
            raycastStartHeight = raycastStartHeight,
            raycastDownDistance = raycastDownDistance,
            carHeightOffset = carHeightOffset,
            updateInterval = updateInterval,
            verboseDebug = verboseDebug,
        };
    }

    // -------- Internals --------

    private readonly List<Vector3> _path = new();
    private float[] _cumLengths;
    private float _totalLength;

    private readonly Dictionary<int, GameObject> _carsBySlot = new();
    private readonly List<int> _toRemove = new();
    private int _maxSlotIndex;
    private float _updateTimer;
    private int _lastClosestIdx;
    private readonly TrackSpawnQueuePendingState _queueState = new();
    private readonly TrackSpawnQueueLastSpawn _queueLastSpawn = new();

    private void Update()
    {
        // P key: always allow test spawn (bypasses streamSpawnDuringRun and all other restrictions)
        if (Input.GetKeyDown(KeyCode.P))
        {
            SpawnOneNPCCarForTest();
            return;
        }

        if (_path.Count < 2 || playerTransform == null || !HasAnyValidCarType())
            return;

        if (_queueState.IsControlled)
        {
            DespawnBehind(GetPlayerDistance());
            _updateTimer += Time.deltaTime;
            if (_updateTimer >= updateInterval)
            {
                _updateTimer = 0f;
                _queueState.TrySubmit(this);
            }
            return;
        }

        if (!streamSpawnDuringRun)
            return;

        _updateTimer += Time.deltaTime;
        if (_updateTimer < updateInterval) return;

        _updateTimer = 0f;
        StreamCars();
    }

    public void InitializeForRun(ProceduralTrackGenerator generator, Transform player)
    {
        trackGenerator = generator;
        playerTransform = player;

        if (trackGenerator == null || playerTransform == null)
        {
            Debug.LogError($"[NPCTrafficCarSpawner] InitializeForRun missing refs. generator={generator}, player={player}");
            return;
        }

        if (verboseDebug)
            Debug.Log("[NPCTrafficCarSpawner] InitializeForRun: rebuilding path + slots.");

        RebuildPath();
        ClearCars();
        SetupSlots();

        if (preSpawnOnInitialize)
            PreSpawnInitialWindow();

        _updateTimer = 0f;
    }

    public void SetPlayerTransform(Transform player)
    {
        playerTransform = player;
    }

    /// <summary>
    /// TEST only (P key).
    /// Force-spawns one NPC traffic car at the beginning of the track, bypassing:
    /// - camera/viewport culling
    /// - spawn distance windows
    /// - spawn blockers / overlap checks
    /// - streaming / updateInterval
    /// It only requires that we have at least one valid car prefab; if no track data is
    /// available, it falls back to the spawner (or player) position.
    /// </summary>
    public void SpawnOneNPCCarForTest()
    {
        // Only requirement: at least one valid car prefab to spawn
        if (!HasAnyValidCarType())
            return;

        // 1) Choose a prefab (ignore distance weighting; just grab something valid)
        GameObject prefab = null;
        foreach (var t in carTypes)
        {
            if (t != null && t.prefab != null && t.weight > 0f)
            {
                prefab = t.prefab;
                break;
            }
        }
        if (prefab == null)
            return;

        // 2) Decide a base position + forward at the *start of the track* if we can.
        Vector3 basePos = transform.position;
        Vector3 forward = transform.forward.sqrMagnitude > 0.0001f ? transform.forward : Vector3.forward;

        // Prefer track start from generator
        if (trackGenerator != null && trackGenerator.PathPoints != null && trackGenerator.PathPoints.Count >= 2)
        {
            var pts = trackGenerator.PathPoints;
            basePos = pts[0];
            Vector3 next = pts[1];
            forward = (next - basePos);
            forward.y = 0f;
            if (forward.sqrMagnitude < 0.0001f) forward = Vector3.forward;
            forward.Normalize();
        }
        // Fallback: use player position if available
        else if (playerTransform != null)
        {
            basePos = playerTransform.position;
            forward = playerTransform.forward;
            forward.y = 0f;
            if (forward.sqrMagnitude < 0.0001f) forward = Vector3.forward;
            forward.Normalize();
        }

        // 3) Ground it: prefer road raycast, but always spawn even if it misses
        Vector3 spawnPos;
        Vector3 origin = basePos + Vector3.up * raycastStartHeight;
        float maxRay = raycastStartHeight + raycastDownDistance;
        if (Physics.Raycast(origin, Vector3.down, out RaycastHit hit, maxRay, roadLayer, QueryTriggerInteraction.Ignore))
            spawnPos = hit.point + Vector3.up * carHeightOffset;
        else
            spawnPos = basePos + Vector3.up * Mathf.Max(1f, carHeightOffset);

        Quaternion rot = Quaternion.LookRotation(forward, Vector3.up);
        Transform parent = carParent != null ? carParent : transform;

        GameObject car = Instantiate(prefab, spawnPos, rot, parent);

        var npc = car.GetComponent<NPCTrafficCar>();
        if (npc != null)
            npc.SetGenerator(trackGenerator);

        if (verboseDebug)
            Debug.Log($"[NPCTrafficCarSpawner] TEST SPAWN (P key): {prefab.name} at {spawnPos}, forward={forward}");
    }

    // -------- Path Building --------

    private void RebuildPath()
    {
        _path.Clear();
        _cumLengths = null;
        _totalLength = 0f;
        _lastClosestIdx = 0;

        var src = trackGenerator.PathPoints;
        if (src == null || src.Count < 2) return;

        if (useSmoothing)
            GenerateSmoothedPath(src, smoothingSubdivisionsPerSegment, _path);
        else
            _path.AddRange(src);

        int n = _path.Count;
        _cumLengths = new float[n];
        float len = 0f;
        for (int i = 1; i < n; i++)
        {
            len += Vector3.Distance(_path[i - 1], _path[i]);
            _cumLengths[i] = len;
        }
        _totalLength = len;
    }

    private void SetupSlots()
    {
        _carsBySlot.Clear();
        _maxSlotIndex = Mathf.FloorToInt(_totalLength / carSpacing);
    }

    // -------- Streaming --------

    private void StreamCars()
    {
        if (_totalLength <= 0f || _maxSlotIndex <= 0)
            return;

        float playerDist = GetPlayerDistance();

        // -------- Spawn AHEAD --------
        float spawnStartDist = Mathf.Clamp(playerDist + minSpawnDistanceAhead, 0f, _totalLength);
        float spawnEndDist = Mathf.Clamp(playerDist + maxSpawnDistanceAhead, 0f, _totalLength);

        int startSlot = Mathf.Clamp(Mathf.FloorToInt(spawnStartDist / carSpacing), 0, _maxSlotIndex);
        int endSlot = Mathf.Clamp(Mathf.FloorToInt(spawnEndDist / carSpacing), 0, _maxSlotIndex);

        for (int slot = startSlot; slot <= endSlot; slot++)
        {
            TrySpawnAtSlot(slot, playerDist);
        }

        // -------- Spawn BEHIND --------
        if (allowSpawnBehind)
        {
            float behindStartDist = Mathf.Clamp(playerDist - maxSpawnDistanceBehind, 0f, _totalLength);
            float behindEndDist = Mathf.Clamp(playerDist - minSpawnDistanceBehind, 0f, _totalLength);

            int behindStartSlot = Mathf.Clamp(Mathf.FloorToInt(behindStartDist / carSpacing), 0, _maxSlotIndex);
            int behindEndSlot = Mathf.Clamp(Mathf.FloorToInt(behindEndDist / carSpacing), 0, _maxSlotIndex);

            for (int slot = behindStartSlot; slot <= behindEndSlot; slot++)
            {
                TrySpawnAtSlot(slot, playerDist);
            }
        }

        // Despawn far behind
        DespawnBehind(playerDist);
    }

    private bool TrySpawnOneAhead()
    {
        if (_totalLength <= 0f || _maxSlotIndex <= 0)
            return false;

        float playerDist = GetPlayerDistance();
        float spawnStartDist = Mathf.Clamp(playerDist + minSpawnDistanceAhead, 0f, _totalLength);
        float spawnEndDist = Mathf.Clamp(playerDist + maxSpawnDistanceAhead, 0f, _totalLength);

        int startSlot = Mathf.Clamp(Mathf.FloorToInt(spawnStartDist / carSpacing), 0, _maxSlotIndex);
        int endSlot = Mathf.Clamp(Mathf.FloorToInt(spawnEndDist / carSpacing), 0, _maxSlotIndex);

        for (int slot = startSlot; slot <= endSlot; slot++)
        {
            int before = _carsBySlot.Count;
            TrySpawnAtSlot(slot, playerDist);
            if (_carsBySlot.Count > before)
                return true;
        }

        if (allowSpawnBehind)
        {
            float behindStartDist = Mathf.Clamp(playerDist - maxSpawnDistanceBehind, 0f, _totalLength);
            float behindEndDist = Mathf.Clamp(playerDist - minSpawnDistanceBehind, 0f, _totalLength);

            int behindStartSlot = Mathf.Clamp(Mathf.FloorToInt(behindStartDist / carSpacing), 0, _maxSlotIndex);
            int behindEndSlot = Mathf.Clamp(Mathf.FloorToInt(behindEndDist / carSpacing), 0, _maxSlotIndex);

            for (int slot = behindStartSlot; slot <= behindEndSlot; slot++)
            {
                int before = _carsBySlot.Count;
                TrySpawnAtSlot(slot, playerDist);
                if (_carsBySlot.Count > before)
                    return true;
            }
        }

        return false;
    }

    private void TrySpawnAtSlot(int slot, float playerDist)
    {
        if (_carsBySlot.ContainsKey(slot))
            return;

        if (_carsBySlot.Count >= maxActiveCars)
            return;

        float dist = slot * carSpacing;

        // Check spawn chance
        float norm = _totalLength > 0f ? Mathf.Clamp01(dist / _totalLength) : 0f;
        float difficultyMult = spawnChanceByDistance != null
            ? Mathf.Max(0f, spawnChanceByDistance.Evaluate(norm))
            : 1f;

        float effectiveChance = spawnChancePerSlot * difficultyMult;
        if (effectiveChance <= 0f)
            return;

        if (UnityEngine.Random.value > effectiveChance)
            return;

        // Pre-check: get spawn position and ensure it's NOT in camera view
        SampleAlongPath(dist, out Vector3 candidatePos, out _);
        if (IsPositionInCameraView(candidatePos))
        {
            if (verboseDebug)
                Debug.Log($"[NPCTrafficCarSpawner] Skipped slot {slot} - in camera view");
            return;
        }

        TrySpawnCarAtDistance(slot, dist);
    }

    private void DespawnBehind(float playerDist)
    {
        _toRemove.Clear();

        if (playerTransform == null)
            return;

        Vector3 playerPos = playerTransform.position;
        Vector3 playerForward = playerTransform.forward;
        playerForward.y = 0f;
        if (playerForward.sqrMagnitude < 0.0001f)
            playerForward = Vector3.forward;
        playerForward.Normalize();

        float despawnSqr = despawnBehindDistance * despawnBehindDistance;

        foreach (var kvp in _carsBySlot)
        {
            if (kvp.Value == null)
            {
                _toRemove.Add(kvp.Key);
                continue;
            }

            // Robust world-space "behind" test. The previous arc-length-along-spline comparison
            // could mis-project on curved / self-approaching track sections (and wrap near a loop),
            // wrongly flagging a car that was actually in front of the player and making visible
            // cars vanish. A car is now only culled when it is genuinely behind the player's travel
            // direction, beyond the despawn distance, AND not currently on screen. Crashed cars are
            // despawned separately by NPCTrafficCar's own crash timer and are unaffected by this.
            Vector3 toCar = kvp.Value.transform.position - playerPos;
            toCar.y = 0f;

            bool behind = Vector3.Dot(toCar, playerForward) < 0f;
            bool farEnough = toCar.sqrMagnitude > despawnSqr;
            bool offScreen = !IsPositionInCameraView(kvp.Value.transform.position);

            if (behind && farEnough && offScreen)
                _toRemove.Add(kvp.Key);
        }

        foreach (int slot in _toRemove)
        {
            if (_carsBySlot.TryGetValue(slot, out var obj) && obj != null)
                DestroyCarCompletely(obj);
                
            _carsBySlot.Remove(slot);
        }
    }

    private void DestroyCarCompletely(GameObject car)
    {
        if (car == null) return;

        // Destroy all children first (lights, trails, FX, etc.)
        for (int i = car.transform.childCount - 1; i >= 0; i--)
        {
            var child = car.transform.GetChild(i);
            if (child != null)
                Destroy(child.gameObject);
        }

        // Destroy parent
        Destroy(car);
    }

    private void PreSpawnInitialWindow()
    {
        if (_totalLength <= 0f || _maxSlotIndex <= 0)
            return;

        float preSpawnEnd = Mathf.Clamp(initialPreSpawnDistance, 0f, _totalLength);
        int endSlot = Mathf.FloorToInt(preSpawnEnd / carSpacing);

        // Start a bit ahead so cars aren't right at spawn
        int startSlot = Mathf.Max(1, Mathf.FloorToInt(minSpawnDistanceAhead / carSpacing));

        for (int slot = startSlot; slot <= endSlot; slot++)
        {
            if (_carsBySlot.ContainsKey(slot))
                continue;

            if (_carsBySlot.Count >= maxActiveCars)
                break;

            float dist = slot * carSpacing;

            float norm = _totalLength > 0f ? Mathf.Clamp01(dist / _totalLength) : 0f;
            float difficultyMult = spawnChanceByDistance != null
                ? Mathf.Max(0f, spawnChanceByDistance.Evaluate(norm))
                : 1f;

            float effectiveChance = spawnChancePerSlot * difficultyMult;
            if (effectiveChance <= 0f) continue;
            if (UnityEngine.Random.value > effectiveChance) continue;

            TrySpawnCarAtDistance(slot, dist);
        }

        if (verboseDebug)
            Debug.Log($"[NPCTrafficCarSpawner] PreSpawn: {_carsBySlot.Count} cars up to {preSpawnEnd:F0}m.");
    }

    // -------- Spawning --------

    private void TrySpawnCarAtDistance(int slot, float baseDist)
    {
        float jitter = UnityEngine.Random.Range(-distanceJitter, distanceJitter);
        float sampleDist = Mathf.Clamp(baseDist + jitter, 0f, _totalLength);

        GameObject chosenPrefab = ChooseCarPrefab(sampleDist);
        if (chosenPrefab == null)
            return;

        SampleAlongPath(sampleDist, out Vector3 pos, out Vector3 forward);

        if (forward.sqrMagnitude < 0.0001f)
            forward = Vector3.forward;

        Vector3 flatForward = forward;
        flatForward.y = 0f;
        if (flatForward.sqrMagnitude < 0.0001f)
            flatForward = Vector3.forward;
        flatForward.Normalize();

        // Lane offset
        float halfWidth = trackGenerator.RoadWidth * 0.5f;
        float usable = (halfWidth * lateralFraction) - edgeMargin;
        if (usable <= 0f)
            usable = halfWidth * 0.3f;

        Vector3 right = Vector3.Cross(Vector3.up, flatForward).normalized;

        float lateralOffset;
        bool isOncoming = UnityEngine.Random.value < oncomingLaneChance;

        if (preferLanes)
        {
            // Pick left or right lane
            float lanePos = UnityEngine.Random.value > 0.5f ? usable * 0.5f : -usable * 0.5f;
            // Add small random variance within lane
            lanePos += UnityEngine.Random.Range(-usable * 0.2f, usable * 0.2f);
            lateralOffset = Mathf.Clamp(lanePos, -usable, usable);
        }
        else
        {
            lateralOffset = UnityEngine.Random.Range(-usable, usable);
        }

        if (!IsPathClearForSpawn(sampleDist, lateralOffset, spawnLookaheadDistance))
        {
            if (verboseDebug)
                Debug.Log($"[NPCTrafficCarSpawner] Skipped slot {slot} - path blocked within {spawnLookaheadDistance}m");
            return;
        }

        pos += right * lateralOffset;

        // Raycast to ground
        Vector3 origin = pos + Vector3.up * raycastStartHeight;
        float maxRay = raycastStartHeight + raycastDownDistance;

        if (!Physics.Raycast(origin, Vector3.down, out RaycastHit hit, maxRay, roadLayer, QueryTriggerInteraction.Ignore))
        {
            if (verboseDebug)
                Debug.LogWarning($"[NPCTrafficCarSpawner] No ground found at slot {slot}");
            return;
        }

        Vector3 spawnPos = hit.point + Vector3.up * carHeightOffset;
        if (IsPositionInCameraView(spawnPos))
        {
            if (verboseDebug)
                Debug.Log($"[NPCTrafficCarSpawner] Skipped spawn at slot {slot} - final position in camera view");
            return;
        }

        // Rotation: face forward (or backward if oncoming)
        Vector3 spawnForward = isOncoming ? -flatForward : flatForward;
        Quaternion rot = Quaternion.LookRotation(spawnForward, Vector3.up);

        Transform parent = carParent != null ? carParent : transform;

        // Spawn
        GameObject car = Instantiate(chosenPrefab, hit.point + Vector3.up * carHeightOffset, rot, parent);

        _queueLastSpawn.Record(car.transform.position, chosenPrefab.name);

        // Inject track generator reference
        var npcScript = car.GetComponent<NPCTrafficCar>();
        if (npcScript != null)
        {
            npcScript.SetGenerator(trackGenerator);

            // If oncoming, could set negative speed or reverse flag (if supported)
            // For now, oncoming cars just face backward but NPCTrafficCar drives forward along track
        }

        _carsBySlot[slot] = car;

        if (verboseDebug)
            Debug.Log($"[NPCTrafficCarSpawner] Spawned car at slot {slot}, dist={sampleDist:F0}m, oncoming={isOncoming}");
    }

    // -------- Car Selection --------

    private bool HasAnyValidCarType()
    {
        if (carTypes == null || carTypes.Count == 0)
            return false;

        foreach (var t in carTypes)
        {
            if (t != null && t.prefab != null && t.weight > 0f)
                return true;
        }
        return false;
    }

    private GameObject ChooseCarPrefab(float distanceAlongTrack)
    {
        if (carTypes == null || carTypes.Count == 0 || _totalLength <= 0f)
            return null;

        float norm = Mathf.Clamp01(distanceAlongTrack / _totalLength);

        float totalWeight = 0f;
        float[] weights = new float[carTypes.Count];

        for (int i = 0; i < carTypes.Count; i++)
        {
            var t = carTypes[i];
            if (t == null || t.prefab == null || t.weight <= 0f)
            {
                weights[i] = 0f;
                continue;
            }

            float w = GetWeightForType(t, norm);
            weights[i] = w;
            totalWeight += w;
        }

        if (totalWeight <= 0f)
            return null;

        float rand = UnityEngine.Random.value * totalWeight;
        float accum = 0f;

        for (int i = 0; i < weights.Length; i++)
        {
            accum += weights[i];
            if (rand <= accum)
                return carTypes[i].prefab;
        }

        return carTypes[carTypes.Count - 1].prefab;
    }

    private float GetWeightForType(NPCCarType t, float normDist)
    {
        float start = Mathf.Clamp01(t.startAtNormalizedDist);
        float full = Mathf.Clamp01(t.fullWeightNormalizedDist);
        float stop = Mathf.Clamp01(t.stopAtNormalizedDist);

        // Ensure ordering
        if (full < start) full = start;
        if (stop < full) stop = full;

        if (normDist < start || normDist > stop)
            return 0f;

        float factor;
        if (normDist < full)
            factor = Mathf.InverseLerp(start, full, normDist);
        else
            factor = 1f;

        return t.weight * factor;
    }

    // -------- Path Utilities --------

    private float GetPlayerDistance()
    {
        Vector3 p = playerTransform.position;
        float best = float.MaxValue;

        for (int i = 0; i < _path.Count - 1; i++)
        {
            Vector3 a = _path[i], b = _path[i + 1];
            Vector3 ab = b - a;
            float abSqr = ab.sqrMagnitude;
            if (abSqr < 1e-6f) continue;

            float t = Mathf.Clamp01(Vector3.Dot(p - a, ab) / abSqr);
            Vector3 proj = Vector3.Lerp(a, b, t);
            float d = (p - proj).sqrMagnitude;

            if (d < best)
            {
                _lastClosestIdx = i;
                best = d;
            }
        }

        float segLen = Vector3.Distance(_path[_lastClosestIdx], _path[_lastClosestIdx + 1]);
        Vector3 seg = _path[_lastClosestIdx + 1] - _path[_lastClosestIdx];
        float prog = Mathf.Clamp01(Vector3.Dot(p - _path[_lastClosestIdx], seg) / (segLen * segLen + 0.0001f));

        return _cumLengths[_lastClosestIdx] + prog * segLen;
    }

    private void SampleAlongPath(float dist, out Vector3 pos, out Vector3 fwd)
    {
        dist = Mathf.Clamp(dist, 0f, _totalLength);

        int idx = 0;
        for (int i = 0; i < _cumLengths.Length - 1; i++)
        {
            if (_cumLengths[i + 1] >= dist)
            {
                idx = i;
                break;
            }
        }

        float segStart = _cumLengths[idx];
        float segEnd = _cumLengths[idx + 1];
        float segLen = segEnd - segStart;
        float t = segLen > 0.0001f ? (dist - segStart) / segLen : 0f;

        pos = Vector3.Lerp(_path[idx], _path[idx + 1], t);
        fwd = (_path[idx + 1] - _path[idx]).normalized;
    }

    private void ClearCars()
    {
        foreach (var kvp in _carsBySlot)
        {
            if (kvp.Value != null)
                DestroyCarCompletely(kvp.Value);
        }
        _carsBySlot.Clear();
    }



    private static void GenerateSmoothedPath(List<Vector3> src, int subdivisions, List<Vector3> outList)
    {
        outList.Clear();
        if (src == null || src.Count < 2) return;

        outList.Add(src[0]);

        for (int i = 0; i < src.Count - 1; i++)
        {
            Vector3 p0 = src[Mathf.Max(i - 1, 0)];
            Vector3 p1 = src[i];
            Vector3 p2 = src[i + 1];
            Vector3 p3 = src[Mathf.Min(i + 2, src.Count - 1)];

            for (int s = 1; s <= subdivisions; s++)
            {
                float t = s / (float)subdivisions;
                outList.Add(CatmullRom(p0, p1, p2, p3, t));
            }
        }
    }

    private bool IsPathClearForSpawn(float startDist, float lateralOffset, float checkDistance)
    {
        if (!avoidImmediateObstacleHits) return true;
        if (spawnBlockerLayers.value == 0) return true;

        float step = Mathf.Max(0.5f, spawnLookaheadStep);
        float endDist = Mathf.Clamp(startDist + Mathf.Max(0f, checkDistance), 0f, _totalLength);

        for (float d = startDist; d <= endDist; d += step)
        {
            SampleAlongPath(d, out Vector3 p, out Vector3 fwd);

            Vector3 flatFwd = fwd; flatFwd.y = 0f;
            if (flatFwd.sqrMagnitude < 0.0001f) flatFwd = Vector3.forward;
            flatFwd.Normalize();

            Vector3 right = Vector3.Cross(Vector3.up, flatFwd).normalized;
            p += right * lateralOffset;

            // ground it similarly to spawn logic
            Vector3 origin = p + Vector3.up * raycastStartHeight;
            float maxRay = raycastStartHeight + raycastDownDistance;
            if (Physics.Raycast(origin, Vector3.down, out RaycastHit hit, maxRay, roadLayer, QueryTriggerInteraction.Ignore))
                p = hit.point + Vector3.up * carHeightOffset;

            // Overlap check at this sample point
            if (Physics.CheckSphere(p, spawnProbeRadius, spawnBlockerLayers, QueryTriggerInteraction.Ignore))
                return false;
        }

        return true;
    }


    private bool IsPositionInCameraView(Vector3 worldPos)
    {
        if (mainCamera == null)
            mainCamera = Camera.main;

        if (mainCamera == null)
            return false; // Can't check, assume not visible

        Vector3 viewportPoint = mainCamera.WorldToViewportPoint(worldPos);

        // Check if in front of camera and within viewport (with margin)
        bool inFront = viewportPoint.z > 0f;
        bool inViewportX = viewportPoint.x > -viewportMargin && viewportPoint.x < 1f + viewportMargin;
        bool inViewportY = viewportPoint.y > -viewportMargin && viewportPoint.y < 1f + viewportMargin;

        return inFront && inViewportX && inViewportY;
    }

    private static Vector3 CatmullRom(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t)
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

    public string SpawnQueueLabel => "NPC Traffic";
    public bool IsSpawnQueueReady => _path.Count >= 2 && playerTransform != null && HasAnyValidCarType();
    public bool HasSpawnQueueCapacity => _carsBySlot.Count < maxActiveCars;
    public bool HasPendingSpawnRequest => _queueState.HasPending;
    public bool TrySubmitSpawnRequest() => _queueState.TrySubmit(this);
    public bool TryExecutePendingSpawn() => _queueState.TryExecute(TrySpawnOneAhead);
    public bool TryConsumeLastSpawnReport(out TrackSpawnQueueSpawnReport report) => _queueLastSpawn.TryConsume(out report);
    public void CancelPendingSpawnRequest() => _queueState.Cancel();
    public void SetQueueControlledAutonomous(bool controlled, TrackSpawnerQueue owner = null) => _queueState.Bind(controlled, owner);
}

/// <summary>
/// Configuration for a type of NPC traffic car.
/// </summary>
[System.Serializable]
public class NPCCarType
{
    [Header("Identity")]
    public string id;
    public GameObject prefab;

    [Header("Weight")]
    [Min(0f)] public float weight = 1f;

    [Header("Distance Band (normalized 0-1 along track)")]
    [Range(0f, 1f)] public float startAtNormalizedDist = 0f;
    [Range(0f, 1f)] public float fullWeightNormalizedDist = 0.1f;
    [Range(0f, 1f)] public float stopAtNormalizedDist = 1f;
}