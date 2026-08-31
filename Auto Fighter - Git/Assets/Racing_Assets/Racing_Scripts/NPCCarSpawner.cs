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

    [Tooltip("Minimum distance behind player to spawn.")]
    [SerializeField] private float minSpawnDistanceBehind = 40f;

    [Tooltip("Maximum distance behind player to spawn.")]
    [SerializeField] private float maxSpawnDistanceBehind = 150f;

    [Header("Initial Pre-Spawn")]
    [Tooltip("How far ahead (from start) we pre-fill cars before the run begins.")]
    [SerializeField] private float initialPreSpawnDistance = 200f;

    [Header("Despawn")]
    [Tooltip("Only applies to crashed wrecks: cull them when this far behind the player. Driving (non-crashed) cars are never despawned by the spawner.")]
    [SerializeField] private float despawnBehindDistance = 30f;

    [Tooltip("Unused by spawner (kept for TrialConfig compatibility). Crashed cars self-destroy via NPCTrafficCar.destroyDelay.")]
    [SerializeField] private float despawnCrashedAfter = 8f;

    [Header("Randomization")]
    [SerializeField] private float distanceJitter = 20f;

    [Tooltip("Chance to fill a spawn slot (0–1). X = at track start, Y = at track end.")]
    [SerializeField] private Vector2 spawnChanceByProgress = new Vector2(0.18f, 0.6f);

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

        initialPreSpawnDistance = s.initialPreSpawnDistance;
        despawnBehindDistance = s.despawnBehindDistance;
        despawnCrashedAfter = s.despawnCrashedAfter;

        distanceJitter = s.distanceJitter;
        spawnChanceByProgress = s.spawnChanceByProgress;

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
            initialPreSpawnDistance = initialPreSpawnDistance,
            despawnBehindDistance = despawnBehindDistance,
            despawnCrashedAfter = despawnCrashedAfter,
            distanceJitter = distanceJitter,
            spawnChanceByProgress = spawnChanceByProgress,
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

        // Prefer track start from generator (same centerline as road mesh).
        if (trackGenerator != null)
        {
            trackGenerator.GetStartPoint(out basePos, out forward);
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

        if (trackGenerator == null) return;
        TrackPathSampling.RebuildPathFromRoadCenterline(trackGenerator, _path, ref _cumLengths, out _totalLength);
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
        TrySpawnInAheadWindow(playerDist);

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
        int before = _carsBySlot.Count;
        TrySpawnInAheadWindow(playerDist);
        if (_carsBySlot.Count > before)
            return true;

        if (allowSpawnBehind)
        {
            float behindStartDist = Mathf.Clamp(playerDist - maxSpawnDistanceBehind, 0f, _totalLength);
            float behindEndDist = Mathf.Clamp(playerDist - minSpawnDistanceBehind, 0f, _totalLength);

            int behindStartSlot = Mathf.Clamp(Mathf.FloorToInt(behindStartDist / carSpacing), 0, _maxSlotIndex);
            int behindEndSlot = Mathf.Clamp(Mathf.FloorToInt(behindEndDist / carSpacing), 0, _maxSlotIndex);

            for (int slot = behindStartSlot; slot <= behindEndSlot; slot++)
            {
                int countBefore = _carsBySlot.Count;
                TrySpawnAtSlot(slot, playerDist);
                if (_carsBySlot.Count > countBefore)
                    return true;
            }
        }

        return false;
    }

    private void TrySpawnInAheadWindow(float playerDist)
    {
        float minD = Mathf.Clamp(playerDist + minSpawnDistanceAhead, 0f, _totalLength);
        float maxD = Mathf.Clamp(playerDist + Mathf.Max(minSpawnDistanceAhead, maxSpawnDistanceAhead), 0f, _totalLength);
        if (minD >= _totalLength - 0.5f)
            return;

        float spacing = Mathf.Max(0.01f, carSpacing);
        int startSlot = Mathf.Clamp(Mathf.CeilToInt(minD / spacing), 0, _maxSlotIndex);
        int endSlot = Mathf.Clamp(Mathf.FloorToInt(maxD / spacing), 0, _maxSlotIndex);

        for (int slot = startSlot; slot <= endSlot; slot++)
            TrySpawnAtSlot(slot, playerDist);

        // Tight windows (e.g. 50–70m with 50m slots) often contain no grid point.
        // Still honor min-ahead by placing at the window start if that slot is free.
        if (startSlot > endSlot && _carsBySlot.Count < maxActiveCars)
        {
            int slot = Mathf.Clamp(Mathf.RoundToInt(minD / spacing), 0, _maxSlotIndex);
            if (_carsBySlot.ContainsKey(slot))
                return;

            float norm = _totalLength > 0f ? Mathf.Clamp01(minD / _totalLength) : 0f;
            float chance = TrackProgressRange.Lerp01(spawnChanceByProgress, norm);
            if (chance <= 0f || UnityEngine.Random.value > chance)
                return;

            TrySpawnCarAtDistance(slot, minD, playerDist);
        }
    }

    private void TrySpawnAtSlot(int slot, float playerDist)
    {
        if (_carsBySlot.ContainsKey(slot))
            return;

        if (_carsBySlot.Count >= maxActiveCars)
            return;

        float dist = slot * carSpacing;
        if (dist < playerDist + minSpawnDistanceAhead)
            return;

        // Check spawn chance
        float norm = _totalLength > 0f ? Mathf.Clamp01(dist / _totalLength) : 0f;
        float effectiveChance = TrackProgressRange.Lerp01(spawnChanceByProgress, norm);
        if (effectiveChance <= 0f)
            return;

        if (UnityEngine.Random.value > effectiveChance)
            return;

        TrySpawnCarAtDistance(slot, dist, playerDist);
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

            // Driving cars must never be culled — only crashed wrecks (or already-destroyed nulls).
            var npc = kvp.Value.GetComponent<NPCTrafficCar>();
            if (npc == null || !npc.HasCrashed)
                continue;

            // Crashed wrecks: cull when behind the player and far enough.
            // (NPCTrafficCar also self-destroys after destroyDelay; this frees slots sooner.)
            Vector3 toCar = kvp.Value.transform.position - playerPos;
            toCar.y = 0f;

            bool behind = Vector3.Dot(toCar, playerForward) < 0f;
            bool farEnough = toCar.sqrMagnitude > despawnSqr;

            if (behind && farEnough)
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
        int startSlot = Mathf.Max(1, Mathf.CeilToInt(minSpawnDistanceAhead / Mathf.Max(0.01f, carSpacing)));

        for (int slot = startSlot; slot <= endSlot; slot++)
        {
            if (_carsBySlot.ContainsKey(slot))
                continue;

            if (_carsBySlot.Count >= maxActiveCars)
                break;

            float dist = slot * carSpacing;

            float norm = _totalLength > 0f ? Mathf.Clamp01(dist / _totalLength) : 0f;
            float effectiveChance = TrackProgressRange.Lerp01(spawnChanceByProgress, norm);
            if (effectiveChance <= 0f) continue;
            if (UnityEngine.Random.value > effectiveChance) continue;

            if (dist < minSpawnDistanceAhead)
                continue;

            TrySpawnCarAtDistance(slot, dist, 0f);
        }

        if (verboseDebug)
            Debug.Log($"[NPCTrafficCarSpawner] PreSpawn: {_carsBySlot.Count} cars up to {preSpawnEnd:F0}m.");
    }

    // -------- Spawning --------

    private void TrySpawnCarAtDistance(int slot, float baseDist, float playerDist)
    {
        float minAheadDist = playerDist + minSpawnDistanceAhead;
        float jitter = UnityEngine.Random.Range(0f, Mathf.Max(0f, distanceJitter));
        float sampleDist = Mathf.Clamp(baseDist + jitter, minAheadDist, _totalLength);
        if (sampleDist + 0.05f < minAheadDist)
            return;

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

        if (IsTooCloseInFrontOfPlayer(pos))
        {
            if (verboseDebug)
                Debug.Log($"[NPCTrafficCarSpawner] Skipped slot {slot} - spawn is closer than {minSpawnDistanceAhead:0}m in front of the player");
            return;
        }

        // Raycast to ground
        Vector3 origin = pos + Vector3.up * raycastStartHeight;
        float maxRay = raycastStartHeight + raycastDownDistance;

        if (!Physics.Raycast(origin, Vector3.down, out RaycastHit hit, maxRay, roadLayer, QueryTriggerInteraction.Ignore))
        {
            if (verboseDebug)
                Debug.LogWarning($"[NPCTrafficCarSpawner] No ground found at slot {slot}");
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

    private bool IsTooCloseInFrontOfPlayer(Vector3 spawnPos)
    {
        if (playerTransform == null || minSpawnDistanceAhead <= 0.01f)
            return false;

        Vector3 toSpawn = spawnPos - playerTransform.position;
        toSpawn.y = 0f;
        float planar = toSpawn.magnitude;
        if (planar >= minSpawnDistanceAhead)
            return false;

        Vector3 pFwd = playerTransform.forward;
        pFwd.y = 0f;
        if (pFwd.sqrMagnitude < 1e-6f)
            return planar < minSpawnDistanceAhead;

        pFwd.Normalize();
        return Vector3.Dot(toSpawn, pFwd) > 0f;
    }

    private float GetPlayerDistance()
    {
        if (playerTransform == null || _path.Count < 2 || _cumLengths == null)
            return 0f;

        Vector3 p = playerTransform.position;

        // Stay on the current stretch first so a nearby parallel/paperclip does not
        // report the player as being far behind (which made min-ahead land on them).
        int start = Mathf.Max(0, _lastClosestIdx - 10);
        int end = Mathf.Min(_path.Count - 2, _lastClosestIdx + 28);
        float best = FindClosestSegment(p, start, end, out int bestIdx);

        if (best > 20f * 20f)
        {
            FindClosestSegment(p, 0, _path.Count - 2, out bestIdx);
        }

        _lastClosestIdx = Mathf.Clamp(bestIdx, 0, _path.Count - 2);
        float segLen = Vector3.Distance(_path[_lastClosestIdx], _path[_lastClosestIdx + 1]);
        Vector3 seg = _path[_lastClosestIdx + 1] - _path[_lastClosestIdx];
        float prog = Mathf.Clamp01(Vector3.Dot(p - _path[_lastClosestIdx], seg) / (segLen * segLen + 0.0001f));
        return _cumLengths[_lastClosestIdx] + prog * segLen;
    }

    private float FindClosestSegment(Vector3 p, int start, int end, out int bestIdx)
    {
        float best = float.MaxValue;
        bestIdx = Mathf.Clamp(start, 0, Mathf.Max(0, _path.Count - 2));
        for (int i = start; i <= end; i++)
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
                best = d;
                bestIdx = i;
            }
        }
        return best;
    }

    private void SampleAlongPath(float dist, out Vector3 pos, out Vector3 fwd)
    {
        TrackPathSampling.SampleAlongPath(_path, _cumLengths, _totalLength, dist, out pos, out fwd);
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