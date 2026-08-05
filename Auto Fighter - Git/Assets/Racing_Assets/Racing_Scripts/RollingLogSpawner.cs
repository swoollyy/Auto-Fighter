using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Randomly spawns rolling logs on the procedural track. Toward-player rolls use negative arclength speed and spawn ahead of the player;
/// with-player rolls use positive speed and spawn behind. Enable <see cref="allowBothTravelDirections"/> to pick randomly.
/// </summary>
public class RollingLogSpawner : MonoBehaviour, ITrackSpawnQueueSource
{
    [Header("References")]
    [SerializeField] private ProceduralTrackGenerator trackGenerator;
    [SerializeField] private Transform playerTransform;
    [SerializeField] private GameObject rollingLogPrefab;
    [SerializeField] private Transform spawnParent;

    [Header("Path sampling")]
    [Tooltip("Ignored at runtime — logs follow ProceduralTrackGenerator's road mesh centerline (track asset useSmoothing + subdivisions).")]
    [SerializeField] private bool useSmoothing = true;
    [SerializeField, Min(1)] private int smoothingSubdivisionsPerSegment = 6;

    [Header("Spawn timing")]
    [SerializeField] private bool enableSpawning = true;
    [SerializeField, Min(0.5f)] private float minSpawnIntervalSeconds = 4f;
    [SerializeField, Min(0.5f)] private float maxSpawnIntervalSeconds = 11f;
    [SerializeField, Min(1)] private int maxActiveLogs = 4;

    [Header("Direction")]
    [Tooltip("If false, only spawns logs that roll toward the player (up-track, decreasing arclength).")]
    [SerializeField] private bool allowBothTravelDirections = false;
    [Tooltip("When both directions allowed, chance (0–1) to pick toward-player vs with-player.")]
    [SerializeField, Range(0f, 1f)] private float towardPlayerDirectionWeight = 0.65f;

    [Header("Toward player (spawn ahead on track, roll backward)")]
    [SerializeField, Min(5f)] private float towardPlayerSpawnMinAhead = 35f;
    [SerializeField, Min(5f)] private float towardPlayerSpawnMaxAhead = 95f;

    [Header("With player (spawn behind on track, roll forward)")]
    [SerializeField, Min(5f)] private float withPlayerSpawnMinBehind = 25f;
    [SerializeField, Min(5f)] private float withPlayerSpawnMaxBehind = 80f;

    [Header("Speed along path (m/s, always positive magnitude; sign comes from direction)")]
    [SerializeField] private Vector2 speedRange = new Vector2(6f, 14f);

    [Header("Lateral placement")]
    [SerializeField, Range(0f, 1f)] private float lateralFraction = 0.92f;
    [SerializeField] private float edgeInnerMargin = 0.12f;

    [Header("Raycast (spawn snap)")]
    [SerializeField] private LayerMask roadLayer = ~0;
    [SerializeField] private float raycastStartHeight = 6f;
    [SerializeField] private float raycastDownDistance = 24f;

    [Header("Progress gate")]
    [Tooltip("Do not spawn until player has progressed at least this fraction along the track.")]
    [SerializeField, Range(0f, 0.95f)] private float minNormalizedProgressToSpawn = 0.02f;

    // ------------------------------------------------------------------------
    // Per-trial config (TrialConfig). Apply BEFORE InitializeForRun.
    // ------------------------------------------------------------------------
    public void ApplyConfig(TrialConfig.RollingLogSettings s)
    {
        if (s == null || !s.overrideRollingLogs) return;

        if (s.rollingLogPrefab != null) rollingLogPrefab = s.rollingLogPrefab;

        useSmoothing = s.useSmoothing;
        smoothingSubdivisionsPerSegment = s.smoothingSubdivisionsPerSegment;

        enableSpawning = s.enableSpawning;
        minSpawnIntervalSeconds = s.minSpawnIntervalSeconds;
        maxSpawnIntervalSeconds = s.maxSpawnIntervalSeconds;
        maxActiveLogs = s.maxActiveLogs;

        allowBothTravelDirections = s.allowBothTravelDirections;
        towardPlayerDirectionWeight = s.towardPlayerDirectionWeight;

        towardPlayerSpawnMinAhead = s.towardPlayerSpawnMinAhead;
        towardPlayerSpawnMaxAhead = s.towardPlayerSpawnMaxAhead;
        withPlayerSpawnMinBehind = s.withPlayerSpawnMinBehind;
        withPlayerSpawnMaxBehind = s.withPlayerSpawnMaxBehind;

        speedRange = s.speedRange;
        lateralFraction = s.lateralFraction;
        edgeInnerMargin = s.edgeInnerMargin;

        roadLayer = s.roadLayer;
        raycastStartHeight = s.raycastStartHeight;
        raycastDownDistance = s.raycastDownDistance;
        minNormalizedProgressToSpawn = s.minNormalizedProgressToSpawn;
    }

    public TrialConfig.RollingLogSettings CaptureConfig()
    {
        return new TrialConfig.RollingLogSettings
        {
            overrideRollingLogs = true,
            rollingLogPrefab = rollingLogPrefab,
            useSmoothing = useSmoothing,
            smoothingSubdivisionsPerSegment = smoothingSubdivisionsPerSegment,
            enableSpawning = enableSpawning,
            minSpawnIntervalSeconds = minSpawnIntervalSeconds,
            maxSpawnIntervalSeconds = maxSpawnIntervalSeconds,
            maxActiveLogs = maxActiveLogs,
            allowBothTravelDirections = allowBothTravelDirections,
            towardPlayerDirectionWeight = towardPlayerDirectionWeight,
            towardPlayerSpawnMinAhead = towardPlayerSpawnMinAhead,
            towardPlayerSpawnMaxAhead = towardPlayerSpawnMaxAhead,
            withPlayerSpawnMinBehind = withPlayerSpawnMinBehind,
            withPlayerSpawnMaxBehind = withPlayerSpawnMaxBehind,
            speedRange = speedRange,
            lateralFraction = lateralFraction,
            edgeInnerMargin = edgeInnerMargin,
            roadLayer = roadLayer,
            raycastStartHeight = raycastStartHeight,
            raycastDownDistance = raycastDownDistance,
            minNormalizedProgressToSpawn = minNormalizedProgressToSpawn,
        };
    }

    private readonly List<Vector3> _path = new();
    private float[] _cumLengths;
    private float _totalLength;
    private int _lastClosestIdx;

    private float _nextSpawnTime;
    private readonly List<RollingLogAlongTrack> _active = new();
    private readonly TrackSpawnQueuePendingState _queueState = new();
    private readonly TrackSpawnQueueLastSpawn _queueLastSpawn = new();

    private void Update()
    {
        if (_queueState.IsControlled)
        {
            PruneActive();
            if (CanOfferSpawnRequest() && _queueState.TrySubmit(this))
                ScheduleNextSpawn();
            return;
        }

        AttemptSpawnOnce(scheduleOnFailure: true);
    }

    private bool CanOfferSpawnRequest()
    {
        if (!enableSpawning || rollingLogPrefab == null || trackGenerator == null || playerTransform == null)
            return false;
        if (_path.Count < 2 || _totalLength <= 0.01f)
            return false;
        if (_active.Count >= maxActiveLogs)
            return false;
        if (Time.time < _nextSpawnTime)
            return false;

        float playerS = GetPlayerDistance();
        float norm = _totalLength > 1e-4f ? playerS / _totalLength : 0f;
        return norm >= minNormalizedProgressToSpawn;
    }

    private bool AttemptSpawnOnce(bool scheduleOnFailure)
    {
        if (!enableSpawning || rollingLogPrefab == null || trackGenerator == null || playerTransform == null)
            return false;

        if (_path.Count < 2 || _totalLength <= 0.01f)
            return false;

        PruneActive();

        if (_active.Count >= maxActiveLogs)
            return false;

        if (scheduleOnFailure && Time.time < _nextSpawnTime)
            return false;

        float playerS = GetPlayerDistance();
        float norm = _totalLength > 1e-4f ? playerS / _totalLength : 0f;
        if (norm < minNormalizedProgressToSpawn)
        {
            if (scheduleOnFailure) ScheduleNextSpawn();
            return false;
        }

        bool towardPlayer = !allowBothTravelDirections || Random.value < towardPlayerDirectionWeight;
        if (!TryComputeSpawnDistance(playerS, towardPlayer, out float spawnS, out float signedSpeed))
        {
            if (scheduleOnFailure) ScheduleNextSpawn();
            return false;
        }

        bool spawned = TrySpawnAt(spawnS, signedSpeed);
        if (scheduleOnFailure)
            ScheduleNextSpawn();
        return spawned;
    }

    public void InitializeForRun(ProceduralTrackGenerator generator, Transform player)
    {
        trackGenerator = generator;
        playerTransform = player;
        RebuildPath();
        _active.Clear();
        ScheduleNextSpawn();

        if (trackGenerator != null)
        {
            trackGenerator.OnTrackGeneratedSuccessfully -= HandleTrackRegenerated;
            trackGenerator.OnTrackGeneratedSuccessfully += HandleTrackRegenerated;
        }
    }

    public void SetPlayerTransform(Transform player) => playerTransform = player;

    private void OnDisable()
    {
        if (trackGenerator != null)
            trackGenerator.OnTrackGeneratedSuccessfully -= HandleTrackRegenerated;
    }

    private void HandleTrackRegenerated(ProceduralTrackGenerator gen) => RebuildPath();

    private void ScheduleNextSpawn()
    {
        float lo = Mathf.Min(minSpawnIntervalSeconds, maxSpawnIntervalSeconds);
        float hi = Mathf.Max(minSpawnIntervalSeconds, maxSpawnIntervalSeconds);
        _nextSpawnTime = Time.time + Random.Range(lo, hi);
    }

    private void PruneActive()
    {
        for (int i = _active.Count - 1; i >= 0; i--)
        {
            if (_active[i] == null)
                _active.RemoveAt(i);
        }
    }

    private bool TryComputeSpawnDistance(float playerS, bool towardPlayer, out float spawnS, out float signedSpeed)
    {
        spawnS = 0f;
        signedSpeed = 0f;

        float speedMag = Random.Range(Mathf.Min(speedRange.x, speedRange.y), Mathf.Max(speedRange.x, speedRange.y));

        if (towardPlayer)
        {
            float lo = playerS + Mathf.Min(towardPlayerSpawnMinAhead, towardPlayerSpawnMaxAhead);
            float hi = playerS + Mathf.Max(towardPlayerSpawnMinAhead, towardPlayerSpawnMaxAhead);
            spawnS = Random.Range(lo, hi);
            signedSpeed = -speedMag;
        }
        else
        {
            float near = Mathf.Min(withPlayerSpawnMinBehind, withPlayerSpawnMaxBehind);
            float far = Mathf.Max(withPlayerSpawnMinBehind, withPlayerSpawnMaxBehind);
            float lo = playerS - far;
            float hi = playerS - near;
            spawnS = Random.Range(lo, hi);
            signedSpeed = speedMag;
        }

        spawnS = Mathf.Clamp(spawnS, 0f, _totalLength);

        // Reject if spawn band had no room (e.g. behind spawn at track start)
        if (towardPlayer)
        {
            if (spawnS <= playerS + 2f)
                return false;
        }
        else
        {
            if (spawnS >= playerS - 2f)
                return false;
        }

        return true;
    }

    private bool TrySpawnAt(float spawnS, float signedSpeed)
    {
        TrackPathSampling.SampleAlongPath(_path, _cumLengths, _totalLength, spawnS, out Vector3 center, out Vector3 flatForward);

        float halfWidth = trackGenerator != null ? trackGenerator.RoadWidth * 0.5f : 2f;
        float usable = halfWidth * lateralFraction - edgeInnerMargin;
        if (usable <= 0.05f)
            usable = halfWidth * 0.35f;

        Vector3 right = Vector3.Cross(Vector3.up, flatForward).normalized;
        float lateral = Random.Range(-usable, usable);
        Vector3 lateralPos = center + right * lateral;

        Vector3 origin = lateralPos + Vector3.up * raycastStartHeight;
        if (!Physics.Raycast(origin, Vector3.down, out RaycastHit hit, raycastStartHeight + raycastDownDistance, roadLayer, QueryTriggerInteraction.Ignore))
            return false;

        Transform parent = spawnParent != null ? spawnParent : transform;
        Vector3 spawnPos = hit.point + hit.normal * 0.05f;
        // Preserve prefab root rotation (e.g. -90 Z mesh tilt). RollingLogAlongTrack applies track heading on top.
        GameObject inst = Instantiate(rollingLogPrefab, spawnPos, Quaternion.identity, parent);
        inst.transform.localRotation = rollingLogPrefab.transform.localRotation;
        _queueLastSpawn.Record(spawnPos, rollingLogPrefab.name);

        var roll = inst.GetComponentInChildren<RollingLogAlongTrack>(true);
        if (roll == null)
        {
            Destroy(inst);
            Debug.LogError("[RollingLogSpawner] Prefab needs RollingLogAlongTrack on the root or a child.");
            return false;
        }

        // Use sampled lateral directly — projecting (lateralPos - hit.point) onto right loses offset when the hit is mostly vertical.
        float clampedLateral = Mathf.Clamp(lateral, -usable, usable);

        roll.BeginRoll(playerTransform, spawnS, signedSpeed, clampedLateral);
        _active.Add(roll);
        return true;
    }

    private float GetPlayerDistance()
    {
        Vector3 p = playerTransform.position;
        float best = float.MaxValue;
        for (int i = 0; i < _path.Count - 1; i++)
        {
            Vector3 a = _path[i], b = _path[i + 1];
            float t = Mathf.Clamp01(Vector3.Dot(p - a, b - a) / (b - a).sqrMagnitude);
            Vector3 proj = Vector3.Lerp(a, b, t);
            float d = (p - proj).sqrMagnitude;
            if (d < best)
            {
                _lastClosestIdx = i;
                best = d;
            }
        }

        float segLen = Vector3.Distance(_path[_lastClosestIdx], _path[_lastClosestIdx + 1]);
        float prog = segLen > 1e-6f
            ? Mathf.Clamp01(Vector3.Dot(p - _path[_lastClosestIdx], _path[_lastClosestIdx + 1] - _path[_lastClosestIdx]) / (segLen * segLen))
            : 0f;
        return _cumLengths[_lastClosestIdx] + prog * segLen;
    }

    private void SampleAlongPath(float dist, out Vector3 pos, out Vector3 fwd)
    {
        TrackPathSampling.SampleAlongPath(_path, _cumLengths, _totalLength, dist, out pos, out fwd);
    }

    private void RebuildPath()
    {
        _path.Clear();
        _cumLengths = null;
        _totalLength = 0f;
        _lastClosestIdx = 0;

        if (trackGenerator == null) return;

        TrackPathSampling.BuildCenterlinePath(trackGenerator, _path);
        if (_path.Count < 2) return;

        int n = _path.Count;
        _cumLengths = new float[n];
        TrackPathSampling.BuildCumulativeLengths(_path, _cumLengths, out _totalLength);
    }

    public string SpawnQueueLabel => "Rolling Logs";
    public bool IsSpawnQueueReady => enableSpawning && rollingLogPrefab != null && trackGenerator != null && playerTransform != null && _path.Count >= 2;
    public bool HasSpawnQueueCapacity => _active.Count < maxActiveLogs;
    public bool HasPendingSpawnRequest => _queueState.HasPending;
    public bool TrySubmitSpawnRequest() => _queueState.TrySubmit(this);
    public bool TryExecutePendingSpawn() => _queueState.TryExecute(() => AttemptSpawnOnce(scheduleOnFailure: false));
    public bool TryConsumeLastSpawnReport(out TrackSpawnQueueSpawnReport report) => _queueLastSpawn.TryConsume(out report);
    public void CancelPendingSpawnRequest() => _queueState.Cancel();
    public void SetQueueControlledAutonomous(bool controlled, TrackSpawnerQueue owner = null) => _queueState.Bind(controlled, owner);
}
