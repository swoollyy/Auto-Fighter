using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Streams bounce-back obstacles along the track using its own spawn interval/cooldown.
/// When queue-controlled, it submits requests on its timer and spawns only when the queue executes.
/// </summary>
[DisallowMultipleComponent]
public class BounceBackObstacleSpawner : MonoBehaviour, ITrackSpawnQueueSource
{
    [Header("References")]
    [SerializeField] private ProceduralTrackGenerator trackGenerator;
    [SerializeField] private Transform playerTransform;
    [SerializeField] private GameObject bounceBackPrefab;
    [SerializeField] private Transform spawnParent;

    [Header("Path Sampling")]
    [SerializeField] private bool useSmoothing = true;
    [SerializeField, Min(1)] private int smoothingSubdivisionsPerSegment = 6;

    [Header("Spawn Timing")]
    [SerializeField] private bool enableSpawning = true;
    [SerializeField, Min(0.5f)] private float minSpawnIntervalSeconds = 5f;
    [SerializeField, Min(0.5f)] private float maxSpawnIntervalSeconds = 12f;
    [SerializeField, Min(1)] private int maxActive = 6;

    [Header("Placement")]
    [SerializeField, Min(5f)] private float minSpawnDistanceAhead = 40f;
    [SerializeField, Min(5f)] private float maxSpawnDistanceAhead = 120f;
    [SerializeField, Min(0.5f)] private float obstacleSpacing = 35f;
    [SerializeField, Range(0f, 1f)] private float spawnChancePerSlot = 0.55f;
    [SerializeField, Range(0f, 1f)] private float lateralFraction = 0.6f;
    [SerializeField] private float edgeInnerMargin = 0.5f;
    [SerializeField] private float distanceJitter = 10f;

    [Header("Raycast")]
    [SerializeField] private LayerMask roadLayer = ~0;
    [SerializeField] private float raycastStartHeight = 6f;
    [SerializeField] private float raycastDownDistance = 24f;

    [Header("Progress Gate")]
    [SerializeField, Range(0f, 0.95f)] private float minNormalizedProgressToSpawn = 0.02f;

    // ------------------------------------------------------------------------
    // Per-trial config (TrialConfig). Apply BEFORE InitializeForRun.
    // ------------------------------------------------------------------------
    public void ApplyConfig(TrialConfig.BounceObstacleSettings s)
    {
        if (s == null || !s.overrideBounceObstacles) return;

        if (s.bounceBackPrefab != null) bounceBackPrefab = s.bounceBackPrefab;

        useSmoothing = s.useSmoothing;
        smoothingSubdivisionsPerSegment = s.smoothingSubdivisionsPerSegment;

        enableSpawning = s.enableSpawning;
        minSpawnIntervalSeconds = s.minSpawnIntervalSeconds;
        maxSpawnIntervalSeconds = s.maxSpawnIntervalSeconds;
        maxActive = s.maxActive;

        minSpawnDistanceAhead = s.minSpawnDistanceAhead;
        maxSpawnDistanceAhead = s.maxSpawnDistanceAhead;
        obstacleSpacing = s.obstacleSpacing;
        spawnChancePerSlot = s.spawnChancePerSlot;
        lateralFraction = s.lateralFraction;
        edgeInnerMargin = s.edgeInnerMargin;
        distanceJitter = s.distanceJitter;

        roadLayer = s.roadLayer;
        raycastStartHeight = s.raycastStartHeight;
        raycastDownDistance = s.raycastDownDistance;
        minNormalizedProgressToSpawn = s.minNormalizedProgressToSpawn;
    }

    public TrialConfig.BounceObstacleSettings CaptureConfig()
    {
        return new TrialConfig.BounceObstacleSettings
        {
            overrideBounceObstacles = true,
            bounceBackPrefab = bounceBackPrefab,
            useSmoothing = useSmoothing,
            smoothingSubdivisionsPerSegment = smoothingSubdivisionsPerSegment,
            enableSpawning = enableSpawning,
            minSpawnIntervalSeconds = minSpawnIntervalSeconds,
            maxSpawnIntervalSeconds = maxSpawnIntervalSeconds,
            maxActive = maxActive,
            minSpawnDistanceAhead = minSpawnDistanceAhead,
            maxSpawnDistanceAhead = maxSpawnDistanceAhead,
            obstacleSpacing = obstacleSpacing,
            spawnChancePerSlot = spawnChancePerSlot,
            lateralFraction = lateralFraction,
            edgeInnerMargin = edgeInnerMargin,
            distanceJitter = distanceJitter,
            roadLayer = roadLayer,
            raycastStartHeight = raycastStartHeight,
            raycastDownDistance = raycastDownDistance,
            minNormalizedProgressToSpawn = minNormalizedProgressToSpawn,
        };
    }

    private readonly List<Vector3> _path = new();
    private readonly Dictionary<int, GameObject> _bySlot = new();
    private readonly TrackSpawnQueuePendingState _queueState = new();
    private readonly TrackSpawnQueueLastSpawn _queueLastSpawn = new();

    private float[] _cumLengths;
    private float _totalLength;
    private int _maxSlotIndex;
    private int _lastClosestIdx;
    private float _nextSpawnTime;

    private void Update()
    {
        if (!enableSpawning || bounceBackPrefab == null || trackGenerator == null || playerTransform == null)
            return;

        if (_path.Count < 2 || _totalLength <= 0.01f)
            return;

        PruneDestroyed();
        DespawnBehind(GetPlayerDistance());

        if (_queueState.IsControlled)
        {
            if (CanOfferSpawnRequest() && _queueState.TrySubmit(this))
                ScheduleNextSpawn();
            return;
        }

        if (Time.time < _nextSpawnTime)
            return;

        if (TrySpawnOneAhead())
            ScheduleNextSpawn();
        else
            ScheduleNextSpawn();
    }

    public void InitializeForRun(ProceduralTrackGenerator generator, Transform player)
    {
        trackGenerator = generator;
        playerTransform = player;
        RebuildPath();
        ClearAll();
        ScheduleNextSpawn();
    }

    public void SetPlayerTransform(Transform player) => playerTransform = player;

    private bool CanOfferSpawnRequest()
    {
        if (Time.time < _nextSpawnTime)
            return false;
        if (_bySlot.Count >= maxActive)
            return false;

        float playerS = GetPlayerDistance();
        float norm = _totalLength > 1e-4f ? playerS / _totalLength : 0f;
        return norm >= minNormalizedProgressToSpawn;
    }

    private bool TrySpawnOneAhead()
    {
        if (_bySlot.Count >= maxActive)
            return false;

        float playerDist = GetPlayerDistance();
        float spawnStartDist = Mathf.Clamp(playerDist + minSpawnDistanceAhead, 0f, _totalLength);
        float spawnEndDist = Mathf.Clamp(playerDist + maxSpawnDistanceAhead, 0f, _totalLength);

        int startSlot = Mathf.Clamp(Mathf.FloorToInt(spawnStartDist / obstacleSpacing), 0, _maxSlotIndex);
        int endSlot = Mathf.Clamp(Mathf.FloorToInt(spawnEndDist / obstacleSpacing), 0, _maxSlotIndex);

        for (int slot = startSlot; slot <= endSlot; slot++)
        {
            if (_bySlot.ContainsKey(slot))
                continue;

            float dist = slot * obstacleSpacing;
            if (dist < playerDist + minSpawnDistanceAhead)
                continue;

            if (Random.value > spawnChancePerSlot)
                continue;

            int before = _bySlot.Count;
            TrySpawnAtSlot(slot, dist);
            if (_bySlot.Count > before)
                return true;
        }

        return false;
    }

    private void TrySpawnAtSlot(int slot, float baseDist)
    {
        float sampleDist = Mathf.Clamp(baseDist + Random.Range(-distanceJitter, distanceJitter), 0f, _totalLength);
        SampleAlongPath(sampleDist, out Vector3 pos, out Vector3 forward);

        if (forward.sqrMagnitude < 0.0001f)
            forward = Vector3.forward;

        float halfWidth = trackGenerator.RoadWidth * 0.5f;
        float usable = (halfWidth * lateralFraction) - edgeInnerMargin;
        if (usable <= 0f)
            usable = halfWidth * 0.5f;

        Vector3 flatForward = forward;
        flatForward.y = 0f;
        if (flatForward.sqrMagnitude < 0.0001f)
            flatForward = Vector3.forward;
        flatForward.Normalize();

        Vector3 right = Vector3.Cross(Vector3.up, flatForward);
        pos += right * Random.Range(-usable, usable);

        Vector3 origin = pos + Vector3.up * raycastStartHeight;
        float maxRay = raycastStartHeight + raycastDownDistance;
        if (!Physics.Raycast(origin, Vector3.down, out RaycastHit hit, maxRay, roadLayer, QueryTriggerInteraction.Ignore))
            return;

        Quaternion rot = Quaternion.LookRotation(flatForward, hit.normal);
        Transform parent = spawnParent != null ? spawnParent : transform;
        GameObject obstacle = Instantiate(bounceBackPrefab, hit.point, rot, parent);
        _bySlot[slot] = obstacle;
        _queueLastSpawn.Record(obstacle.transform.position, bounceBackPrefab.name);
    }

    private void DespawnBehind(float playerDist)
    {
        List<int> remove = new();
        foreach (var kvp in _bySlot)
        {
            float dist = kvp.Key * obstacleSpacing;
            if (dist < playerDist - 20f || kvp.Value == null)
                remove.Add(kvp.Key);
        }

        for (int i = 0; i < remove.Count; i++)
        {
            int slot = remove[i];
            if (_bySlot.TryGetValue(slot, out GameObject go) && go != null)
                Destroy(go);
            _bySlot.Remove(slot);
        }
    }

    private void PruneDestroyed()
    {
        List<int> remove = new();
        foreach (var kvp in _bySlot)
        {
            if (kvp.Value == null)
                remove.Add(kvp.Key);
        }

        for (int i = 0; i < remove.Count; i++)
            _bySlot.Remove(remove[i]);
    }

    private void ClearAll()
    {
        foreach (var kvp in _bySlot)
        {
            if (kvp.Value != null)
                Destroy(kvp.Value);
        }
        _bySlot.Clear();
    }

    private void ScheduleNextSpawn()
    {
        float lo = Mathf.Min(minSpawnIntervalSeconds, maxSpawnIntervalSeconds);
        float hi = Mathf.Max(minSpawnIntervalSeconds, maxSpawnIntervalSeconds);
        _nextSpawnTime = Time.time + Random.Range(lo, hi);
    }

    private void RebuildPath()
    {
        _path.Clear();
        _cumLengths = null;
        _totalLength = 0f;
        _lastClosestIdx = 0;

        if (trackGenerator == null) return;
        if (!TrackPathSampling.RebuildPathFromRoadCenterline(trackGenerator, _path, ref _cumLengths, out _totalLength))
            return;

        _maxSlotIndex = Mathf.FloorToInt(_totalLength / Mathf.Max(0.01f, obstacleSpacing));
    }

    private float GetPlayerDistance()
    {
        Vector3 p = playerTransform.position;
        float best = float.MaxValue;
        for (int i = 0; i < _path.Count - 1; i++)
        {
            Vector3 a = _path[i], b = _path[i + 1];
            float t = Mathf.Clamp01(Vector3.Dot(p - a, (b - a)) / (b - a).sqrMagnitude);
            Vector3 proj = Vector3.Lerp(a, b, t);
            float d = (p - proj).sqrMagnitude;
            if (d < best) { _lastClosestIdx = i; best = d; }
        }

        float segLen = Vector3.Distance(_path[_lastClosestIdx], _path[_lastClosestIdx + 1]);
        float prog = Mathf.Clamp01(Vector3.Dot(p - _path[_lastClosestIdx], (_path[_lastClosestIdx + 1] - _path[_lastClosestIdx])) / Mathf.Pow(segLen, 2));
        return _cumLengths[_lastClosestIdx] + prog * segLen;
    }

    private void SampleAlongPath(float dist, out Vector3 pos, out Vector3 forward)
    {
        pos = _path[0];
        forward = Vector3.forward;
        if (_path.Count < 2) return;

        dist = Mathf.Clamp(dist, 0f, _totalLength);
        int idx = 0;
        while (idx < _cumLengths.Length - 1 && _cumLengths[idx + 1] < dist) idx++;

        float segStart = _cumLengths[idx];
        float segEnd = _cumLengths[idx + 1];
        float t = segEnd > segStart ? (dist - segStart) / (segEnd - segStart) : 0f;
        pos = Vector3.Lerp(_path[idx], _path[idx + 1], t);
        forward = (_path[idx + 1] - _path[idx]).normalized;
    }

    private static void GenerateSmoothedPath(List<Vector3> raw, int subdivisions, List<Vector3> outList)
    {
        outList.Clear();
        if (raw == null || raw.Count < 2) return;
        outList.Add(raw[0]);
        for (int i = 0; i < raw.Count - 1; i++)
        {
            Vector3 p0 = raw[Mathf.Max(i - 1, 0)];
            Vector3 p1 = raw[i];
            Vector3 p2 = raw[i + 1];
            Vector3 p3 = raw[Mathf.Min(i + 2, raw.Count - 1)];
            for (int s = 1; s <= subdivisions; s++)
            {
                float t = s / (float)subdivisions;
                outList.Add(0.5f * ((2f * p1) + (-p0 + p2) * t + (2f * p0 - 5f * p1 + 4f * p2 - p3) * (t * t) + (-p0 + 3f * p1 - 3f * p2 + p3) * (t * t * t)));
            }
        }
    }

    public string SpawnQueueLabel => "Bounce Back";
    public bool IsSpawnQueueReady => enableSpawning && bounceBackPrefab != null && trackGenerator != null && playerTransform != null && _path.Count >= 2;
    public bool HasSpawnQueueCapacity => _bySlot.Count < maxActive;
    public bool HasPendingSpawnRequest => _queueState.HasPending;
    public bool TrySubmitSpawnRequest() => _queueState.TrySubmit(this);
    public bool TryExecutePendingSpawn() => _queueState.TryExecute(() =>
    {
        bool spawned = TrySpawnOneAhead();
        if (spawned)
            ScheduleNextSpawn();
        return spawned;
    });
    public bool TryConsumeLastSpawnReport(out TrackSpawnQueueSpawnReport report) => _queueLastSpawn.TryConsume(out report);
    public void CancelPendingSpawnRequest() => _queueState.Cancel();
    public void SetQueueControlledAutonomous(bool controlled, TrackSpawnerQueue owner = null) => _queueState.Bind(controlled, owner);
}
