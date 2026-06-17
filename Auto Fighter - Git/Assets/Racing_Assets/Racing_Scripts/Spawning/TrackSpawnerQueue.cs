using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Spawners use their own cooldowns to submit spawn requests. This queue applies its own timing/order
/// and then tells the spawner to execute on the field.
/// </summary>
[DisallowMultipleComponent]
public class TrackSpawnerQueue : MonoBehaviour
{
    public enum PlaybackMode
    {
        Sequential = 0,
        Burst = 1,
        Random = 2,
        Wave = 3
    }

    [Serializable]
    public class Entry
    {
        public MonoBehaviour source;
        [Min(1)] public int weight = 1;
        [Min(1)] public int burstCount = 1;
        [Min(0f)] public float spacingInBurst = 0.2f;
        [Min(0f)] public float cooldownAfterUse = 0f;

        [NonSerialized] public float nextEligibleTime;
    }

    [Header("Entries (drag spawners here)")]
    [SerializeField] private List<Entry> entries = new();

    [Header("Playback")]
    [SerializeField] private bool enableQueue = true;
    [SerializeField] private bool takeoverAutonomousSpawning = true;
    [SerializeField] private PlaybackMode playbackMode = PlaybackMode.Sequential;
    [SerializeField] private bool loop = true;

    [Header("Timing")]
    [SerializeField, Min(0f)] private float startDelay = 1.5f;
    [SerializeField, Min(0.05f)] private float intervalSeconds = 1.25f;
    [SerializeField] private Vector2 intervalJitter = new(0.85f, 1.15f);
    [SerializeField, Min(0.05f)] private float failedSpawnRetryDelay = 0.35f;
    [SerializeField, Min(0f)] private float waveStaggerSeconds = 0.15f;

    [Header("Gate")]
    [SerializeField, Range(0f, 1f)] private float minNormalizedProgress = 0f;

    [Header("References (optional)")]
    [SerializeField] private ProceduralTrackGenerator trackGenerator;

    [Header("Debug")]
    [SerializeField] private bool logQueueEvents = true;

    private readonly HashSet<ITrackSpawnQueueSource> _registered = new();
    private readonly List<ITrackSpawnQueueSource> _pending = new();
    private readonly Dictionary<ITrackSpawnQueueSource, Entry> _entryBySource = new();

    private int _sequentialIndex;
    private bool _running;
    private Coroutine _playbackRoutine;

    public bool AcceptSpawnRequest(ITrackSpawnQueueSource source)
    {
        if (!_running || !enableQueue || source == null)
            return false;

        if (!_registered.Contains(source))
            return false;

        if (source.HasPendingSpawnRequest)
            return true;

        bool added = false;
        if (!_pending.Contains(source))
        {
            _pending.Add(source);
            added = true;
        }

        if (added && logQueueEvents)
            TrackSpawnQueueLog.LogEnqueued(source, _pending, playbackMode);

        return true;
    }

    public void InitializeForRun(ProceduralTrackGenerator generator, Transform player)
    {
        trackGenerator = generator;
        _sequentialIndex = 0;
        _running = true;
        _pending.Clear();
        _registered.Clear();
        _entryBySource.Clear();

        for (int i = 0; i < entries.Count; i++)
        {
            Entry entry = entries[i];
            ITrackSpawnQueueSource source = ResolveSource(entry);
            if (source == null) continue;

            _registered.Add(source);
            _entryBySource[source] = entry;
            entry.nextEligibleTime = 0f;

            if (takeoverAutonomousSpawning)
                source.SetQueueControlledAutonomous(true, this);
        }

        if (_playbackRoutine != null)
            StopCoroutine(_playbackRoutine);

        _playbackRoutine = StartCoroutine(CoPlayback());
    }

    public void StopQueue()
    {
        _running = false;
        _pending.Clear();

        if (_playbackRoutine != null)
        {
            StopCoroutine(_playbackRoutine);
            _playbackRoutine = null;
        }

        for (int i = 0; i < entries.Count; i++)
            ResolveSource(entries[i])?.SetQueueControlledAutonomous(false, null);
    }

    private void OnDisable() => StopQueue();

    private IEnumerator CoPlayback()
    {
        if (startDelay > 0f)
            yield return new WaitForSeconds(startDelay);

        while (_running && enableQueue)
        {
            while (!IsProgressGateOpen())
                yield return null;

            PrunePending();

            while (_pending.Count == 0)
                yield return null;

            switch (playbackMode)
            {
                case PlaybackMode.Burst:
                    yield return CoBurstExecute();
                    break;
                case PlaybackMode.Wave:
                    yield return CoWaveExecute();
                    break;
                case PlaybackMode.Random:
                    yield return CoExecuteSingle(PickRandomPending());
                    break;
                default:
                    yield return CoExecuteSingle(PickSequentialPending());
                    if (!loop && _sequentialIndex == 0)
                        _running = false;
                    break;
            }
        }
    }

    private IEnumerator CoExecuteSingle(ITrackSpawnQueueSource source)
    {
        bool executed = false;
        if (source != null)
            executed = TryExecuteSource(source);

        yield return new WaitForSeconds(executed ? GetIntervalSeconds(LookupEntry(source)) : failedSpawnRetryDelay);
    }

    private IEnumerator CoBurstExecute()
    {
        ITrackSpawnQueueSource source = PickSequentialPending();
        Entry entry = LookupEntry(source);
        int count = entry != null ? Mathf.Max(1, entry.burstCount) : 1;
        float spacing = entry != null ? entry.spacingInBurst : 0.2f;

        bool anyExecuted = false;
        for (int i = 0; i < count; i++)
        {
            if (source == null || !source.HasPendingSpawnRequest)
                source = PickSequentialPending();

            if (source == null)
                break;

            if (TryExecuteSource(source))
                anyExecuted = true;

            if (i < count - 1 && spacing > 0f)
                yield return new WaitForSeconds(spacing);
        }

        yield return new WaitForSeconds(anyExecuted ? GetIntervalSeconds(entry) : failedSpawnRetryDelay);

        if (!loop && _sequentialIndex == 0)
            _running = false;
    }

    private IEnumerator CoWaveExecute()
    {
        bool anyExecuted = false;
        for (int i = 0; i < entries.Count; i++)
        {
            ITrackSpawnQueueSource source = ResolveSource(entries[i]);
            if (source != null && source.HasPendingSpawnRequest && TryExecuteSource(source))
                anyExecuted = true;

            if (i < entries.Count - 1 && waveStaggerSeconds > 0f)
                yield return new WaitForSeconds(waveStaggerSeconds);
        }

        yield return new WaitForSeconds(anyExecuted ? GetIntervalSeconds(null) : failedSpawnRetryDelay);
    }

    private bool TryExecuteSource(ITrackSpawnQueueSource source)
    {
        if (source == null)
            return false;

        Entry entry = LookupEntry(source);
        if (entry != null && Time.time < entry.nextEligibleTime)
            return false;

        if (!source.HasPendingSpawnRequest)
        {
            _pending.Remove(source);
            return false;
        }

        if (!source.TryExecutePendingSpawn())
        {
            _pending.Remove(source);
            return false;
        }

        if (logQueueEvents)
        {
            if (source.TryConsumeLastSpawnReport(out TrackSpawnQueueSpawnReport report))
                TrackSpawnQueueLog.LogSpawned(source, report, trackGenerator);
            else
                TrackSpawnQueueLog.LogSpawned(source, default, trackGenerator);
        }

        _pending.Remove(source);
        return true;
    }

    private ITrackSpawnQueueSource PickSequentialPending()
    {
        if (entries.Count == 0)
            return _pending.Count > 0 ? _pending[0] : null;

        for (int i = 0; i < entries.Count; i++)
        {
            int idx = (_sequentialIndex + i) % entries.Count;
            ITrackSpawnQueueSource source = ResolveSource(entries[idx]);
            if (source != null && source.HasPendingSpawnRequest && _pending.Contains(source))
            {
                _sequentialIndex = (idx + 1) % entries.Count;
                return source;
            }
        }

        return null;
    }

    private ITrackSpawnQueueSource PickRandomPending()
    {
        if (_pending.Count == 0)
            return null;

        int total = 0;
        for (int i = 0; i < _pending.Count; i++)
        {
            Entry entry = LookupEntry(_pending[i]);
            if (entry == null) continue;
            if (Time.time < entry.nextEligibleTime) continue;
            total += Mathf.Max(1, entry.weight);
        }

        if (total <= 0)
            return _pending[UnityEngine.Random.Range(0, _pending.Count)];

        int roll = UnityEngine.Random.Range(0, total);
        int accum = 0;
        for (int i = 0; i < _pending.Count; i++)
        {
            Entry entry = LookupEntry(_pending[i]);
            if (entry == null) continue;
            if (Time.time < entry.nextEligibleTime) continue;

            accum += Mathf.Max(1, entry.weight);
            if (roll < accum)
                return _pending[i];
        }

        return _pending[_pending.Count - 1];
    }

    private void PrunePending()
    {
        for (int i = _pending.Count - 1; i >= 0; i--)
        {
            ITrackSpawnQueueSource source = _pending[i];
            if (source == null || !source.HasPendingSpawnRequest)
                _pending.RemoveAt(i);
        }
    }

    private float GetIntervalSeconds(Entry usedEntry)
    {
        float jitter = UnityEngine.Random.Range(intervalJitter.x, intervalJitter.y);
        float wait = Mathf.Max(0.05f, intervalSeconds * jitter);

        if (usedEntry != null && usedEntry.cooldownAfterUse > 0f)
            usedEntry.nextEligibleTime = Time.time + usedEntry.cooldownAfterUse;

        return wait;
    }

    private Entry LookupEntry(ITrackSpawnQueueSource source)
    {
        if (source == null) return null;
        _entryBySource.TryGetValue(source, out Entry entry);
        return entry;
    }

    private static ITrackSpawnQueueSource ResolveSource(Entry entry)
    {
        if (entry == null || entry.source == null)
            return null;

        if (entry.source is ITrackSpawnQueueSource direct)
            return direct;

        return entry.source.GetComponent<ITrackSpawnQueueSource>();
    }

    private bool IsProgressGateOpen()
    {
        if (minNormalizedProgress <= 0f)
            return true;

        float total = 0f;
        float player = 0f;

        if (trackGenerator != null && trackGenerator.PathPoints != null && trackGenerator.PathPoints.Count >= 2)
        {
            var pts = trackGenerator.PathPoints;
            for (int i = 1; i < pts.Count; i++)
                total += Vector3.Distance(pts[i - 1], pts[i]);
        }

        var meter = FindObjectOfType<TrackDistanceMeter>();
        if (meter != null)
            player = meter.DistanceAlongTrack;

        if (total <= 0.01f)
            return true;

        return (player / total) >= minNormalizedProgress;
    }
}
