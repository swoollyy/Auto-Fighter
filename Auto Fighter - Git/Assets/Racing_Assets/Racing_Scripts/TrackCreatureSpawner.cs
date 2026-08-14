using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

using Random = UnityEngine.Random;

/// <summary>
/// Spawns creatures along the procedural track.
/// Mirrors TrackObstacleSpawner and NPCTrafficCarSpawner patterns for consistency.
/// Supports passive (bug), scared (critter), and aggressive (beast) creature behaviors.
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
    private readonly List<int> _toRemove = new();
    private int _maxSlotIndex;
    private float _updateTimer;
    private int _lastClosestIdx;
    private readonly TrackSpawnQueuePendingState _queueState = new();
    private readonly TrackSpawnQueueLastSpawn _queueLastSpawn = new();

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
                _queueState.TrySubmit(this);
            }
            return;
        }

        if (!streamSpawnDuringRun)
            return;

        _updateTimer += Time.deltaTime;
        if (_updateTimer < updateInterval) return;

        _updateTimer = 0f;
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
            PreSpawnInitialWindow();

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

        // Lateral offset
        float halfWidth = trackGenerator.RoadWidth * 0.5f;
        float usable = (halfWidth * lateralFraction) - edgeInnerMargin - chosenType.extraLateralPadding;
        if (usable <= 0f)
            usable = halfWidth * 0.3f;

        Vector3 right = Vector3.Cross(Vector3.up, flatForward).normalized;
        float lateralOffset = Random.Range(-usable, usable);
        pos += right * lateralOffset;

        // Raycast to ground
        Vector3 origin = pos + Vector3.up * raycastStartHeight;
        float maxRay = raycastStartHeight + raycastDownDistance;

        if (!Physics.Raycast(origin, Vector3.down, out RaycastHit hit, maxRay, roadLayer, QueryTriggerInteraction.Ignore))
        {
            if (verboseDebug)
                Debug.LogWarning($"[TrackCreatureSpawner] No ground found at slot {slot}");
            return;
        }

        Vector3 spawnPos = hit.point + Vector3.up * (creatureHeightOffset + chosenType.extraHeightOffset);

        // Random rotation for variety
        float randomYaw = Random.Range(0f, 360f);
        Quaternion rot = Quaternion.Euler(0f, randomYaw, 0f);

        Transform parent = creatureParent != null ? creatureParent : transform;

        // Spawn the creature
        GameObject creature = Instantiate(chosenType.prefab, spawnPos, rot, parent);

        _queueLastSpawn.Record(creature.transform.position, chosenType.prefab.name);

        // Initialize the creature behavior
        InitializeCreatureBehavior(creature, chosenType, sampleDist);

        _creaturesBySlot[slot] = creature;

        if (verboseDebug)
            Debug.Log($"[TrackCreatureSpawner] Spawned {chosenType.id} ({chosenType.behaviorType}) at slot {slot}, dist={sampleDist:F0}m");
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