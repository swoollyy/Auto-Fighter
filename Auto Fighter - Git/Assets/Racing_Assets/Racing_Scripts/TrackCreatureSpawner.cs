using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

using Random = UnityEngine.Random;

/// <summary>
/// Spawns creatures along the procedural track.
/// Mirrors TrackObstacleSpawner and NPCTrafficCarSpawner patterns for consistency.
/// Supports passive (bug), scared (critter), aggressive (beast), and thrower (gorilla) creature behaviors.
/// </summary>
public class TrackCreatureSpawner : MonoBehaviour, ITrackSpawnQueueSource
{
    [Header("References")]
    [SerializeField] private ProceduralTrackGenerator trackGenerator;
    [SerializeField] private Transform playerTransform;
    [SerializeField] private Transform creatureParent;

    [Header("Creature vs Traffic")]
    [Tooltip("Layer mask for NPC traffic colliders (same layer as NPCTrafficCar bodies). Critters flee; beasts can hunt.")]
    [SerializeField] private LayerMask npcTrafficLayerMask;

    [Tooltip("Static obstacles for creature avoidance. Used at spawn when a creature's avoidance layer mask is empty.")]
    [SerializeField] private LayerMask creatureObstacleAvoidanceLayers;

    [Header("Spawn Mode")]
    [Tooltip("If true, fills ahead of player on InitializeForRun.")]
    [SerializeField] private bool preSpawnOnInitialize = true;

    [Tooltip("If true, continues spawning ahead while driving.")]
    [SerializeField] private bool streamSpawnDuringRun = true;

    [Header("Creature Types")]
    [SerializeField] private List<CreatureTypeConfig> creatureTypes = new List<CreatureTypeConfig>();

    [Header("Path Sampling")]
    [SerializeField] private bool useSmoothing = true;
    [SerializeField, Min(1)] private int smoothingSubdivisionsPerSegment = 6;

    [Header("Spawn Settings")]
    [Tooltip("Ideal spacing between potential spawn slots (meters).")]
    [SerializeField] private float creatureSpacing = 50f;

    [SerializeField] private int maxActiveCreatures = 15;

    [Tooltip("Minimum distance in front of the player where new creatures spawn.")]
    [SerializeField] private float minSpawnDistanceAhead = 80f;

    [Tooltip("Maximum distance in front of the player to spawn creatures.")]
    [SerializeField] private float maxSpawnDistanceAhead = 200f;

    [Header("Initial Pre-Spawn")]
    [Tooltip("How far ahead (from start) we pre-fill creatures before the run begins.")]
    [SerializeField] private float initialPreSpawnDistance = 150f;

    [Header("Despawn")]
    [Tooltip("Despawn creatures this far behind the player.")]
    [SerializeField] private float despawnBehindDistance = 20f;

    [Header("Randomization")]
    [SerializeField] private float distanceJitter = 15f;

    [Tooltip("Chance to fill a spawn slot (0–1). X = at track start, Y = at track end.")]
    [SerializeField] private Vector2 spawnChanceByProgress = new Vector2(0.15f, 0.5f);

    [Header("Placement")]
    [Tooltip("Fraction of road half-width used for lateral placement.")]
    [SerializeField, Range(0f, 1f)] private float lateralFraction = 0.7f;

    [SerializeField] private float edgeInnerMargin = 0.5f;

    [Header("Raycast")]
    [SerializeField] private LayerMask roadLayer;
    [SerializeField] private float raycastStartHeight = 6f;
    [SerializeField] private float raycastDownDistance = 20f;
    [SerializeField] private float creatureHeightOffset = 0.1f;

    [Header("Timing")]
    [SerializeField] private float updateInterval = 0.4f;

    [Header("Debug")]
    [SerializeField] private bool verboseDebug = false;

    // ------------------------------------------------------------------------
    // Per-trial config (TrialConfig). ApplyConfig copies a trial's CreatureSettings
    // into these fields (call BEFORE InitializeForRun). CaptureConfig snapshots the
    // current values into a settings object (used by the editor baker).
    // ------------------------------------------------------------------------
    public void ApplyConfig(TrialConfig.CreatureSettings s)
    {
        if (s == null || !s.overrideCreatures) return;

        npcTrafficLayerMask = s.npcTrafficLayerMask;
        creatureObstacleAvoidanceLayers = s.creatureObstacleAvoidanceLayers;

        preSpawnOnInitialize = s.preSpawnOnInitialize;
        streamSpawnDuringRun = s.streamSpawnDuringRun;

        creatureTypes = s.creatureTypes != null ? new List<CreatureTypeConfig>(s.creatureTypes) : new List<CreatureTypeConfig>();

        useSmoothing = s.useSmoothing;
        smoothingSubdivisionsPerSegment = s.smoothingSubdivisionsPerSegment;

        creatureSpacing = s.creatureSpacing;
        maxActiveCreatures = s.maxActiveCreatures;
        minSpawnDistanceAhead = s.minSpawnDistanceAhead;
        maxSpawnDistanceAhead = s.maxSpawnDistanceAhead;

        initialPreSpawnDistance = s.initialPreSpawnDistance;
        despawnBehindDistance = s.despawnBehindDistance;

        distanceJitter = s.distanceJitter;
        spawnChanceByProgress = s.spawnChanceByProgress;

        lateralFraction = s.lateralFraction;
        edgeInnerMargin = s.edgeInnerMargin;

        roadLayer = s.roadLayer;
        raycastStartHeight = s.raycastStartHeight;
        raycastDownDistance = s.raycastDownDistance;
        creatureHeightOffset = s.creatureHeightOffset;

        updateInterval = s.updateInterval;
        verboseDebug = s.verboseDebug;
    }

    public TrialConfig.CreatureSettings CaptureConfig()
    {
        return new TrialConfig.CreatureSettings
        {
            overrideCreatures = true,
            npcTrafficLayerMask = npcTrafficLayerMask,
            creatureObstacleAvoidanceLayers = creatureObstacleAvoidanceLayers,
            preSpawnOnInitialize = preSpawnOnInitialize,
            streamSpawnDuringRun = streamSpawnDuringRun,
            creatureTypes = creatureTypes != null ? new List<CreatureTypeConfig>(creatureTypes) : new List<CreatureTypeConfig>(),
            useSmoothing = useSmoothing,
            smoothingSubdivisionsPerSegment = smoothingSubdivisionsPerSegment,
            creatureSpacing = creatureSpacing,
            maxActiveCreatures = maxActiveCreatures,
            minSpawnDistanceAhead = minSpawnDistanceAhead,
            maxSpawnDistanceAhead = maxSpawnDistanceAhead,
            initialPreSpawnDistance = initialPreSpawnDistance,
            despawnBehindDistance = despawnBehindDistance,
            distanceJitter = distanceJitter,
            spawnChanceByProgress = spawnChanceByProgress,
            lateralFraction = lateralFraction,
            edgeInnerMargin = edgeInnerMargin,
            roadLayer = roadLayer,
            raycastStartHeight = raycastStartHeight,
            raycastDownDistance = raycastDownDistance,
            creatureHeightOffset = creatureHeightOffset,
            updateInterval = updateInterval,
            verboseDebug = verboseDebug,
        };
    }

    // -------- Internals --------
    private readonly List<Vector3> _path = new();
    private float[] _cumLengths;
    private float _totalLength;

    private readonly Dictionary<int, GameObject> _creaturesBySlot = new();
    private readonly Dictionary<int, GameObject> _hillCreaturesBySlot = new();
    private readonly List<int> _toRemove = new();
    private int _maxSlotIndex;
    private float _updateTimer;
    private int _lastClosestIdx;
    private readonly TrackSpawnQueuePendingState _queueState = new();
    private readonly TrackSpawnQueueLastSpawn _queueLastSpawn = new();
    private const float HillCreatureSpacing = 70f;
    private const int MaxHillCreatures = 5;

    #region Unity Lifecycle

    private void Update()
    {
        if (_path.Count < 2 || playerTransform == null || !HasAnyValidCreatureType())
            return;

        if (_queueState.IsControlled)
        {
            DespawnBehind(GetPlayerDistance());
            _updateTimer += Time.deltaTime;
            if (_updateTimer >= updateInterval)
            {
                _updateTimer = 0f;
                StreamHillCreatures(GetPlayerDistance());
                _queueState.TrySubmit(this);
            }
            return;
        }

        if (!streamSpawnDuringRun)
            return;

        _updateTimer += Time.deltaTime;
        if (_updateTimer < updateInterval) return;

        _updateTimer = 0f;
        StreamHillCreatures(GetPlayerDistance());
        StreamCreatures();
    }

    #endregion

    #region Public API

    /// <summary>
    /// Initialize the spawner for a new run. Call this when the race starts.
    /// </summary>
    public void InitializeForRun(ProceduralTrackGenerator generator, Transform player)
    {
        trackGenerator = generator;
        playerTransform = player;

        if (trackGenerator == null || playerTransform == null)
        {
            Debug.LogError($"[TrackCreatureSpawner] InitializeForRun missing refs. generator={generator}, player={player}");
            return;
        }

        if (verboseDebug)
            Debug.Log("[TrackCreatureSpawner] InitializeForRun: rebuilding path + slots.");

        RebuildPath();
        ClearCreatures();
        SetupSlots();

        if (preSpawnOnInitialize)
        {
            PreSpawnInitialWindow();
            PreSpawnHillCreatures();
        }

        _updateTimer = 0f;
    }

    /// <summary>
    /// Update the player reference if it changes.
    /// </summary>
    public void SetPlayerTransform(Transform player)
    {
        playerTransform = player;
    }

    /// <summary>
    /// Get the current path points (for creature navigation).
    /// </summary>
    public IReadOnlyList<Vector3> GetPath() => _path;

    /// <summary>
    /// Get cumulative lengths array (for creature navigation).
    /// </summary>
    public float[] GetCumulativeLengths() => _cumLengths;

    /// <summary>
    /// Get total track length.
    /// </summary>
    public float GetTotalLength() => _totalLength;

    /// <summary>
    /// Get track generator reference.
    /// </summary>
    public ProceduralTrackGenerator GetTrackGenerator() => trackGenerator;

    /// <summary>
    /// Layers used to find NPC traffic for creature flee/chase behavior.
    /// </summary>
    public LayerMask NpcTrafficLayerMask => npcTrafficLayerMask;

    public LayerMask CreatureObstacleAvoidanceLayers => creatureObstacleAvoidanceLayers;

    #endregion

    #region Path Building

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
        _creaturesBySlot.Clear();
        _maxSlotIndex = Mathf.FloorToInt(_totalLength / creatureSpacing);
    }

    #endregion

    #region Streaming

    private void StreamCreatures()
    {
        if (_totalLength <= 0f || _maxSlotIndex <= 0)
            return;

        float playerDist = GetPlayerDistance();

        // -------- Spawn AHEAD --------
        float spawnStartDist = Mathf.Clamp(playerDist + minSpawnDistanceAhead, 0f, _totalLength);
        float spawnEndDist = Mathf.Clamp(playerDist + maxSpawnDistanceAhead, 0f, _totalLength);

        int startSlot = Mathf.Clamp(Mathf.FloorToInt(spawnStartDist / creatureSpacing), 0, _maxSlotIndex);
        int endSlot = Mathf.Clamp(Mathf.FloorToInt(spawnEndDist / creatureSpacing), 0, _maxSlotIndex);

        for (int slot = startSlot; slot <= endSlot; slot++)
        {
            if (_creaturesBySlot.ContainsKey(slot))
                continue;

            if (_creaturesBySlot.Count >= maxActiveCreatures)
                break;

            float dist = slot * creatureSpacing;

            // Enforce min distance
            if (dist < playerDist + minSpawnDistanceAhead)
                continue;

            // Check spawn chance
            float norm = _totalLength > 0f ? Mathf.Clamp01(dist / _totalLength) : 0f;
            float effectiveChance = TrackProgressRange.Lerp01(spawnChanceByProgress, norm);
            if (effectiveChance <= 0f)
                continue;

            if (Random.value > effectiveChance)
                continue;

            TrySpawnCreatureAtDistance(slot, dist);
        }

        // -------- Despawn behind player --------
        DespawnBehind(playerDist);
    }

    private bool TrySpawnOneAhead()
    {
        if (_totalLength <= 0f || _maxSlotIndex <= 0)
            return false;

        float playerDist = GetPlayerDistance();
        float spawnStartDist = Mathf.Clamp(playerDist + minSpawnDistanceAhead, 0f, _totalLength);
        float spawnEndDist = Mathf.Clamp(playerDist + maxSpawnDistanceAhead, 0f, _totalLength);

        int startSlot = Mathf.Clamp(Mathf.FloorToInt(spawnStartDist / creatureSpacing), 0, _maxSlotIndex);
        int endSlot = Mathf.Clamp(Mathf.FloorToInt(spawnEndDist / creatureSpacing), 0, _maxSlotIndex);

        for (int slot = startSlot; slot <= endSlot; slot++)
        {
            if (_creaturesBySlot.ContainsKey(slot))
                continue;

            if (_creaturesBySlot.Count >= maxActiveCreatures)
                break;

            float dist = slot * creatureSpacing;
            if (dist < playerDist + minSpawnDistanceAhead)
                continue;

            float norm = _totalLength > 0f ? Mathf.Clamp01(dist / _totalLength) : 0f;
            float effectiveChance = TrackProgressRange.Lerp01(spawnChanceByProgress, norm);
            if (effectiveChance <= 0f)
                continue;

            if (Random.value > effectiveChance)
                continue;

            int before = _creaturesBySlot.Count;
            TrySpawnCreatureAtDistance(slot, dist);
            if (_creaturesBySlot.Count > before)
                return true;
        }

        return false;
    }

    private void DespawnBehind(float playerDist)
    {
        _toRemove.Clear();

        foreach (var kvp in _creaturesBySlot)
        {
            float dist = kvp.Key * creatureSpacing;

            // IMPORTANT: creatures can wander forward/back from their original slot,
            // so use their live distance-along-track when available.
            if (kvp.Value != null)
            {
                var tc = kvp.Value.GetComponent<TrackCreature>();
                if (tc != null && tc.IsInitialized)
                    dist = tc.DistanceAlongTrack;
            }

            bool behind = dist < playerDist - despawnBehindDistance;

            if (behind || kvp.Value == null)
                _toRemove.Add(kvp.Key);
        }

        foreach (int slot in _toRemove)
        {
            if (_creaturesBySlot.TryGetValue(slot, out var obj) && obj != null)
                Destroy(obj);

            _creaturesBySlot.Remove(slot);
        }

        _toRemove.Clear();
        foreach (var kvp in _hillCreaturesBySlot)
        {
            float dist = kvp.Key * HillCreatureSpacing;
            if (kvp.Value != null)
            {
                var tc = kvp.Value.GetComponent<TrackCreature>();
                if (tc != null && tc.IsInitialized)
                    dist = tc.DistanceAlongTrack;
            }

            if (dist < playerDist - despawnBehindDistance || kvp.Value == null)
                _toRemove.Add(kvp.Key);
        }

        foreach (int slot in _toRemove)
        {
            if (_hillCreaturesBySlot.TryGetValue(slot, out var obj) && obj != null)
                Destroy(obj);
            _hillCreaturesBySlot.Remove(slot);
        }
    }

    private void PreSpawnInitialWindow()
    {
        if (_totalLength <= 0f || _maxSlotIndex <= 0)
            return;

        float preSpawnEnd = Mathf.Clamp(initialPreSpawnDistance, 0f, _totalLength);
        int endSlot = Mathf.FloorToInt(preSpawnEnd / creatureSpacing);

        for (int slot = 0; slot <= endSlot; slot++)
        {
            if (_creaturesBySlot.ContainsKey(slot))
                continue;

            if (_creaturesBySlot.Count >= maxActiveCreatures)
                break;

            float dist = slot * creatureSpacing;

            float norm = _totalLength > 0f ? Mathf.Clamp01(dist / _totalLength) : 0f;
            float effectiveChance = TrackProgressRange.Lerp01(spawnChanceByProgress, norm);
            if (effectiveChance <= 0f) continue;
            if (Random.value > effectiveChance) continue;

            TrySpawnCreatureAtDistance(slot, dist);
        }

        if (verboseDebug)
            Debug.Log($"[TrackCreatureSpawner] PreSpawn: {_creaturesBySlot.Count} creatures up to {preSpawnEnd:F0}m.");
    }

    private void PreSpawnHillCreatures()
    {
        if (_totalLength <= 0f)
            return;

        float preSpawnEnd = Mathf.Clamp(Mathf.Max(initialPreSpawnDistance, 180f), 0f, _totalLength);
        StreamHillCreaturesInRange(0f, preSpawnEnd);
    }

    private void StreamHillCreatures(float playerDist)
    {
        if (_totalLength <= 0f)
            return;

        float startDist = Mathf.Clamp(playerDist + minSpawnDistanceAhead, 0f, _totalLength);
        float endDist = Mathf.Clamp(playerDist + Mathf.Max(maxSpawnDistanceAhead, 160f), 0f, _totalLength);
        StreamHillCreaturesInRange(startDist, endDist);
    }

    private void StreamHillCreaturesInRange(float startDist, float endDist)
    {
        CreatureTypeConfig hillType = FindHillCreatureType();
        if (hillType == null)
            return;

        GameObject prefab = ResolveHillPrefab(hillType);
        if (prefab == null)
        {
            Debug.LogWarning("[TrackCreatureSpawner] Gorilla/hill creature has no prefab (and no fallback).");
            return;
        }

        int startSlot = Mathf.Max(0, Mathf.FloorToInt(startDist / HillCreatureSpacing));
        int endSlot = Mathf.Max(startSlot, Mathf.FloorToInt(endDist / HillCreatureSpacing));

        for (int slot = startSlot; slot <= endSlot; slot++)
        {
            if (_hillCreaturesBySlot.ContainsKey(slot))
                continue;
            if (_hillCreaturesBySlot.Count >= MaxHillCreatures)
                break;

            float dist = Mathf.Clamp(slot * HillCreatureSpacing, 0f, _totalLength);
            TrySpawnHillCreatureAtDistance(slot, dist, hillType, prefab);
        }
    }

    private CreatureTypeConfig FindHillCreatureType()
    {
        if (creatureTypes == null)
            return null;

        for (int i = 0; i < creatureTypes.Count; i++)
        {
            var t = creatureTypes[i];
            if (t == null || t.baseWeight <= 0f)
                continue;
            if (t.behaviorType == CreatureBehaviorType.Thrower || t.spawnOffroadOnHills)
                return t;
        }

        return null;
    }

    private GameObject ResolveHillPrefab(CreatureTypeConfig hillType)
    {
        if (hillType != null && hillType.prefab != null)
            return hillType.prefab;

        if (creatureTypes == null)
            return null;

        for (int i = 0; i < creatureTypes.Count; i++)
        {
            var t = creatureTypes[i];
            if (t != null && t.prefab != null)
                return t.prefab;
        }

        return null;
    }

    private void TrySpawnHillCreatureAtDistance(int slot, float sampleDist, CreatureTypeConfig chosenType, GameObject prefab)
    {
        SampleAlongPath(sampleDist, out Vector3 pos, out Vector3 forward);
        if (forward.sqrMagnitude < 0.0001f)
            forward = Vector3.forward;

        Vector3 flatForward = forward;
        flatForward.y = 0f;
        if (flatForward.sqrMagnitude < 0.0001f)
            flatForward = Vector3.forward;
        flatForward.Normalize();

        if (!TryPickHillSpawnPosition(pos, flatForward, chosenType, out Vector3 spawnPos))
        {
            Debug.LogWarning($"[TrackCreatureSpawner] Gorilla hill spawn failed at {sampleDist:F0}m.");
            return;
        }

        float randomYaw = Random.Range(0f, 360f);
        Transform parent = creatureParent != null ? creatureParent : transform;
        GameObject creature = Instantiate(prefab, spawnPos, Quaternion.Euler(0f, randomYaw, 0f), parent);
        creature.name = string.IsNullOrEmpty(chosenType.id) ? "Gorilla" : chosenType.id;

        InitializeCreatureBehavior(creature, chosenType, sampleDist);
        _hillCreaturesBySlot[slot] = creature;
        _queueLastSpawn.Record(creature.transform.position, creature.name);

        Debug.Log($"[TrackCreatureSpawner] Spawned {chosenType.id} on hill at {sampleDist:F0}m  y={spawnPos.y:F1}");
    }

    #endregion

    #region Spawning

    private void TrySpawnCreatureAtDistance(int slot, float baseDist)
    {
        float jitter = Random.Range(-distanceJitter, distanceJitter);
        float sampleDist = Mathf.Clamp(baseDist + jitter, 0f, _totalLength);

        CreatureTypeConfig chosenType = ChooseCreatureType(sampleDist);
        if (chosenType == null || chosenType.prefab == null)
            return;

        SampleAlongPath(sampleDist, out Vector3 pos, out Vector3 forward);

        if (forward.sqrMagnitude < 0.0001f)
            forward = Vector3.forward;

        Vector3 flatForward = forward;
        flatForward.y = 0f;
        if (flatForward.sqrMagnitude < 0.0001f)
            flatForward = Vector3.forward;
        flatForward.Normalize();

        Vector3 spawnPos;
        bool spawnOnHills = chosenType.behaviorType != CreatureBehaviorType.Scared
            && (chosenType.behaviorType == CreatureBehaviorType.Thrower || chosenType.spawnOffroadOnHills);
        if (spawnOnHills)
        {
            if (!TryPickHillSpawnPosition(pos, flatForward, chosenType, out spawnPos))
            {
                if (verboseDebug)
                    Debug.LogWarning($"[TrackCreatureSpawner] No valid hill spawn for {chosenType.id} at slot {slot}");
                return;
            }
        }
        else
        {
            // Lateral offset on the road
            float halfWidth = trackGenerator.RoadWidth * 0.5f;
            float usable = (halfWidth * lateralFraction) - edgeInnerMargin - chosenType.extraLateralPadding;
            if (chosenType.behaviorType == CreatureBehaviorType.Scared)
                usable = Mathf.Min(usable, halfWidth * 0.7f);
            if (usable <= 0f)
                usable = halfWidth * 0.3f;

            Vector3 right = Vector3.Cross(Vector3.up, flatForward).normalized;
            float lateralOffset = Random.Range(-usable, usable);
            pos += right * lateralOffset;

            Vector3 origin = pos + Vector3.up * raycastStartHeight;
            float maxRay = raycastStartHeight + raycastDownDistance;

            if (!Physics.Raycast(origin, Vector3.down, out RaycastHit hit, maxRay, roadLayer, QueryTriggerInteraction.Ignore))
            {
                if (verboseDebug)
                    Debug.LogWarning($"[TrackCreatureSpawner] No ground found at slot {slot}");
                return;
            }

            spawnPos = hit.point + Vector3.up * (creatureHeightOffset + chosenType.extraHeightOffset);
        }

        float randomYaw = Random.Range(0f, 360f);
        Quaternion rot = Quaternion.Euler(0f, randomYaw, 0f);

        Transform parent = creatureParent != null ? creatureParent : transform;

        GameObject creature = Instantiate(chosenType.prefab, spawnPos, rot, parent);

        _queueLastSpawn.Record(creature.transform.position, chosenType.prefab.name);

        InitializeCreatureBehavior(creature, chosenType, sampleDist);

        _creaturesBySlot[slot] = creature;

        if (verboseDebug)
            Debug.Log($"[TrackCreatureSpawner] Spawned {chosenType.id} ({chosenType.behaviorType}) at slot {slot}, dist={sampleDist:F0}m");
    }

    private bool TryPickHillSpawnPosition(
        Vector3 pathPos,
        Vector3 flatForward,
        CreatureTypeConfig type,
        out Vector3 spawnPos)
    {
        spawnPos = pathPos;

        float halfWidth = trackGenerator != null ? trackGenerator.RoadWidth * 0.5f : 2f;
        float minFromRoad = Mathf.Max(0.5f, type.hillSpawnMinDistanceFromRoad);
        float rMin = halfWidth + minFromRoad;
        float configuredMax = Mathf.Max(rMin + 1f, type.hillSpawnMaxDistanceFromCenterline);
        // Hills finish blending ~22m past the road cut. A tight max radius stays on that ramp
        // and never reaches minHillHeight, so expand the search far enough to hit real peaks.
        float rMax = Mathf.Max(configuredMax, 55f);
        rMax = Mathf.Min(70f, rMax);

        float roadY = pathPos.y;
        float minHill = Mathf.Max(0f, type.minHillHeightAboveRoad);
        LayerMask roadMask = roadLayer.value != 0 ? roadLayer : (LayerMask)((1 << 13) | (1 << 14));
        Vector3 right = Vector3.Cross(Vector3.up, flatForward).normalized;

        bool found = false;
        float bestHeight = float.NegativeInfinity;
        Vector3 bestGround = pathPos;
        bool foundAny = false;
        float bestAnyHeight = float.NegativeInfinity;
        Vector3 bestAnyGround = pathPos;

        float[] alongOffsets = { -10f, -4f, 0f, 4f, 10f };
        float step = Mathf.Max(3.5f, (rMax - rMin) / 8f);
        for (int side = -1; side <= 1; side += 2)
        {
            for (float r = rMin; r <= rMax + 0.01f; r += step)
            {
                for (int a = 0; a < alongOffsets.Length; a++)
                {
                    Vector3 xz = pathPos + right * (side * r) + flatForward * alongOffsets[a];
                    if (TryEvaluateHillCandidate(xz, roadY, minHill, roadMask, ref bestHeight, ref bestGround, ref foundAny, ref bestAnyHeight, ref bestAnyGround))
                        found = true;
                }
            }
        }

        int extraTries = Mathf.Max(0, type.hillSpawnAttempts);
        for (int i = 0; i < extraTries; i++)
        {
            float side = Random.value < 0.5f ? -1f : 1f;
            float r = Random.Range(rMin, rMax);
            float along = Random.Range(-12f, 12f);
            Vector3 xz = pathPos + right * (side * r) + flatForward * along;
            if (TryEvaluateHillCandidate(xz, roadY, minHill, roadMask, ref bestHeight, ref bestGround, ref foundAny, ref bestAnyHeight, ref bestAnyGround))
                found = true;
        }

        if (!found && foundAny && bestAnyHeight >= Mathf.Min(2f, minHill))
        {
            found = true;
            bestGround = bestAnyGround;
        }

        if (!found)
        {
            Debug.LogWarning($"[TrackCreatureSpawner] No hill >= {minHill:0.0}m for {type.id} (search {rMin:0.0}-{rMax:0.0}m).");
            return false;
        }

        spawnPos = bestGround + Vector3.up * (creatureHeightOffset + type.extraHeightOffset);
        return true;
    }

    private bool TryEvaluateHillCandidate(
        Vector3 xz,
        float roadY,
        float minHill,
        LayerMask roadMask,
        ref float bestHeight,
        ref Vector3 bestGround,
        ref bool foundAny,
        ref float bestAnyHeight,
        ref Vector3 bestAnyGround)
    {
        if (Physics.CheckSphere(xz + Vector3.up * 0.5f, 1.1f, roadMask, QueryTriggerInteraction.Ignore))
            return false;
        if (!TrySampleTerrainHeight(xz.x, xz.z, out float terrainY))
            return false;

        Vector3 groundPoint = new Vector3(xz.x, terrainY, xz.z);
        float originY = Mathf.Max(terrainY + 24f, xz.y + 20f);
        Vector3 rayOrigin = new Vector3(xz.x, originY, xz.z);
        float maxRay = originY - terrainY + 40f;
        if (Physics.Raycast(rayOrigin, Vector3.down, out RaycastHit hit, maxRay, ~0, QueryTriggerInteraction.Ignore))
        {
            if (((1 << hit.collider.gameObject.layer) & roadMask) != 0)
                return false;
            if (Mathf.Abs(hit.point.y - terrainY) <= 2.5f)
                groundPoint = hit.point;
        }

        float heightAboveRoad = groundPoint.y - roadY;
        if (heightAboveRoad > bestAnyHeight)
        {
            bestAnyHeight = heightAboveRoad;
            bestAnyGround = groundPoint;
            foundAny = true;
        }

        if (heightAboveRoad < minHill)
            return false;

        if (groundPoint.y <= bestHeight)
            return true;

        bestHeight = groundPoint.y;
        bestGround = groundPoint;
        return true;
    }

    private static bool TrySampleTerrainHeight(float wx, float wz, out float heightY)
    {
        heightY = float.NegativeInfinity;
        Terrain[] terrains = Terrain.activeTerrains;
        if (terrains == null || terrains.Length == 0)
            return false;

        bool found = false;
        for (int i = 0; i < terrains.Length; i++)
        {
            Terrain t = terrains[i];
            if (t == null || t.terrainData == null)
                continue;

            Vector3 tp = t.transform.position;
            Vector3 sz = t.terrainData.size;
            if (wx < tp.x || wx > tp.x + sz.x || wz < tp.z || wz > tp.z + sz.z)
                continue;

            float y = t.SampleHeight(new Vector3(wx, 0f, wz)) + tp.y;
            if (!found || y > heightY)
            {
                heightY = y;
                found = true;
            }
        }

        return found;
    }

    private void InitializeCreatureBehavior(GameObject creature, CreatureTypeConfig config, float distanceAlongTrack)
    {
        // Get or add the TrackCreature component
        var trackCreature = creature.GetComponent<TrackCreature>();
        if (trackCreature == null)
        {
            trackCreature = creature.AddComponent<TrackCreature>();
        }

        // Initialize with config and references
        trackCreature.Initialize(this, playerTransform, config, distanceAlongTrack);
    }

    #endregion

    #region Creature Selection

    private bool HasAnyValidCreatureType()
    {
        if (creatureTypes == null || creatureTypes.Count == 0)
            return false;

        foreach (var t in creatureTypes)
        {
            if (t != null && t.prefab != null && t.baseWeight > 0f)
                return true;
        }
        return false;
    }

    private CreatureTypeConfig ChooseCreatureType(float distanceAlongTrack)
    {
        if (creatureTypes == null || creatureTypes.Count == 0 || _totalLength <= 0f)
            return null;

        float norm = Mathf.Clamp01(distanceAlongTrack / _totalLength);

        float totalWeight = 0f;
        float[] weights = new float[creatureTypes.Count];

        for (int i = 0; i < creatureTypes.Count; i++)
        {
            var t = creatureTypes[i];
            if (t == null || t.prefab == null || t.baseWeight <= 0f)
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

        float rand = Random.value * totalWeight;
        float accum = 0f;

        for (int i = 0; i < weights.Length; i++)
        {
            accum += weights[i];
            if (rand <= accum)
                return creatureTypes[i];
        }

        return creatureTypes[creatureTypes.Count - 1];
    }

    private float GetWeightForType(CreatureTypeConfig t, float normDist)
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

        return t.baseWeight * factor;
    }

    #endregion

    #region Path Utilities

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
        TrackPathSampling.SampleAlongPath(_path, _cumLengths, _totalLength, dist, out pos, out fwd);
    }

    /// <summary>
    /// Public method for creatures to sample the path.
    /// </summary>
    public void SamplePath(float dist, out Vector3 pos, out Vector3 fwd)
    {
        SampleAlongPath(dist, out pos, out fwd);
    }

    /// <summary>
    /// Get the distance along the path for a world position.
    /// </summary>
    public float GetDistanceAlongPath(Vector3 worldPos)
    {
        if (_path.Count < 2) return 0f;

        float best = float.MaxValue;
        int bestIdx = 0;

        for (int i = 0; i < _path.Count - 1; i++)
        {
            Vector3 a = _path[i], b = _path[i + 1];
            Vector3 ab = b - a;
            float abSqr = ab.sqrMagnitude;
            if (abSqr < 1e-6f) continue;

            float t = Mathf.Clamp01(Vector3.Dot(worldPos - a, ab) / abSqr);
            Vector3 proj = Vector3.Lerp(a, b, t);
            float d = (worldPos - proj).sqrMagnitude;

            if (d < best)
            {
                bestIdx = i;
                best = d;
            }
        }

        float segLen = Vector3.Distance(_path[bestIdx], _path[bestIdx + 1]);
        Vector3 seg = _path[bestIdx + 1] - _path[bestIdx];
        float prog = Mathf.Clamp01(Vector3.Dot(worldPos - _path[bestIdx], seg) / (segLen * segLen + 0.0001f));

        return _cumLengths[bestIdx] + prog * segLen;
    }

    private void ClearCreatures()
    {
        foreach (var kvp in _creaturesBySlot)
        {
            if (kvp.Value != null)
                Destroy(kvp.Value);
        }
        _creaturesBySlot.Clear();

        foreach (var kvp in _hillCreaturesBySlot)
        {
            if (kvp.Value != null)
                Destroy(kvp.Value);
        }
        _hillCreaturesBySlot.Clear();
    }

    #endregion

    #region Public Helpers

    /// <summary>
    /// Manually remove a creature (e.g., when killed by player).
    /// </summary>
    public void RemoveCreature(GameObject creature)
    {
        if (creature == null) return;

        int? slotToRemove = null;
        foreach (var kvp in _creaturesBySlot)
        {
            if (kvp.Value == creature)
            {
                slotToRemove = kvp.Key;
                break;
            }
        }

        if (slotToRemove.HasValue)
        {
            _creaturesBySlot.Remove(slotToRemove.Value);
            return;
        }

        foreach (var kvp in _hillCreaturesBySlot)
        {
            if (kvp.Value == creature)
            {
                _hillCreaturesBySlot.Remove(kvp.Key);
                return;
            }
        }
    }

    public string SpawnQueueLabel => "Creatures";
    public bool IsSpawnQueueReady => _path.Count >= 2 && playerTransform != null && HasAnyValidCreatureType();
    public bool HasSpawnQueueCapacity => _creaturesBySlot.Count < maxActiveCreatures;
    public bool HasPendingSpawnRequest => _queueState.HasPending;
    public bool TrySubmitSpawnRequest() => _queueState.TrySubmit(this);
    public bool TryExecutePendingSpawn() => _queueState.TryExecute(TrySpawnOneAhead);
    public bool TryConsumeLastSpawnReport(out TrackSpawnQueueSpawnReport report) => _queueLastSpawn.TryConsume(out report);
    public void CancelPendingSpawnRequest() => _queueState.Cancel();
    public void SetQueueControlledAutonomous(bool controlled, TrackSpawnerQueue owner = null) => _queueState.Bind(controlled, owner);

    #endregion
}