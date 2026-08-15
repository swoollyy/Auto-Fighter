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
        [Tooltip("If false, this spawner keeps its normal autonomous Update spawning and is not registered with the queue.")]
        public bool participateInQueue = true;
        [Tooltip("If true, queue playback replaces this spawner's autonomous spawning. Ignored when Participate In Queue is off.")]
        public bool takeoverAutonomousSpawning = true;

        [NonSerialized] public float nextEligibleTime;
    }

    [Header("Entries (drag spawners here)")]
    [SerializeField] private List<Entry> entries = new();

    [Header("Playback")]
    [SerializeField] private bool enableQueue = true;
    [Tooltip("Default for entries that do not override Takeover Autonomous Spawning.")]
    [SerializeField] private bool takeoverAutonomousSpawning = true;
    [SerializeField] private PlaybackMode playbackMode = PlaybackMode.Sequential;
    [SerializeField] private bool loop = true;

    [Header("Timing")]
    [SerializeField, Min(0f)] private float startDelay = 1.5f;
    [SerializeField, Min(0.05f)] private float intervalSeconds = 1.25f;
    [Tooltip("Jitter range (X = min, Y = max multiplier on intervalSeconds). When Scale Jitter By Progress is on, this is the range at 0% track progress.")]
    [SerializeField] private Vector2 intervalJitter = new(0.85f, 1.15f);
    [Tooltip("If true, the jitter range lerps from intervalJitter (0% progress) to intervalJitterAtFullProgress (100% progress).")]
    [SerializeField] private bool scaleJitterByProgress = false;
    [Tooltip("Jitter range (X = min, Y = max) used at 100% track progress when Scale Jitter By Progress is on.")]
    [SerializeField] private Vector2 intervalJitterAtFullProgress = new(0.6f, 1.4f);
    [SerializeField, Min(0.05f)] private float failedSpawnRetryDelay = 0.35f;
    [SerializeField, Min(0f)] private float waveStaggerSeconds = 0.15f;

    [Header("Gate")]
    [SerializeField, Range(0f, 1f)] private float minNormalizedProgress = 0f;

    [Header("References (optional)")]
    [SerializeField] private ProceduralTrackGenerator trackGenerator;

    [Header("Debug")]
    [SerializeField] private bool logQueueEvents = true;

    public void ApplyConfig(TrialConfig.SpawnQueueSettings s)
    {
        if (s == null || !s.overrideSpawnQueue) return;

        enableQueue = s.enableQueue;
        takeoverAutonomousSpawning = s.takeoverAutonomousSpawning;
        playbackMode = s.playbackMode;
        loop = s.loop;
        startDelay = s.startDelay;
        intervalSeconds = s.intervalSeconds;
        intervalJitter = s.intervalJitter;
        scaleJitterByProgress = s.scaleJitterByProgress;
        intervalJitterAtFullProgress = s.intervalJitterAtFullProgress;
        failedSpawnRetryDelay = s.failedSpawnRetryDelay;
        waveStaggerSeconds = s.waveStaggerSeconds;
        minNormalizedProgress = s.minNormalizedProgress;
    }

    public TrialConfig.SpawnQueueSettings CaptureConfig()
    {
        return new TrialConfig.SpawnQueueSettings
        {
            overrideSpawnQueue = true,
            enableQueue = enableQueue,
            takeoverAutonomousSpawning = takeoverAutonomousSpawning,
            playbackMode = playbackMode,
            loop = loop,
            startDelay = startDelay,
            intervalSeconds = intervalSeconds,
            intervalJitter = intervalJitter,
            scaleJitterByProgress = scaleJitterByProgress,
            intervalJitterAtFullProgress = intervalJitterAtFullProgress,
            failedSpawnRetryDelay = failedSpawnRetryDelay,
            waveStaggerSeconds = waveStaggerSeconds,
            minNormalizedProgress = minNormalizedProgress,
        };
    }

    private readonly HashSet<ITrackSpawnQueueSource> _registered = new();
    private readonly List<ITrackSpawnQueueSource> _pending = new();
    private readonly Dictionary<ITrackSpawnQueueSource, Entry> _entryBySource = new();

    private int _sequentialIndex;
    private bool _running;
    private Coroutine _playbackRoutine;
    private TrackDistanceMeter _distanceMeter;

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
        _distanceMeter = FindObjectOfType<TrackDistanceMeter>();
        _sequentialIndex = 0;
        _running = true;
        _pending.Clear();
        _registered.Clear();
        _entryBySource.Clear();

        for (int i = 0; i < entries.Count; i++)
        {
            Entry entry = entries[i];
            if (entry == null || !entry.participateInQueue)
                continue;

            ITrackSpawnQueueSource source = ResolveSource(entry);
            if (source == null) continue;

            _registered.Add(source);
            _entryBySource[source] = entry;
            entry.nextEligibleTime = 0f;

            if (enableQueue && ShouldTakeoverEntry(entry))
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
        {
            Entry entry = entries[i];
            if (entry == null || !entry.participateInQueue)
                continue;

            ResolveSource(entry)?.SetQueueControlledAutonomous(false, null);
        }
    }

    private bool ShouldTakeoverEntry(Entry entry)
    {
        if (entry == null)
            return takeoverAutonomousSpawning;

        return entry.takeoverAutonomousSpawning && takeoverAutonomousSpawning;
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
        Vector2 jitterRange = intervalJitter;
        if (scaleJitterByProgress && TryGetNormalizedProgress(out float progress))
        {
            jitterRange = new Vector2(
                Mathf.Lerp(intervalJitter.x, intervalJitterAtFullProgress.x, progress),
                Mathf.Lerp(intervalJitter.y, intervalJitterAtFullProgress.y, progress));
        }

        float jitter = UnityEngine.Random.Range(jitterRange.x, jitterRange.y);
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

        // No usable track data -> don't block playback.
        if (!TryGetNormalizedProgress(out float progress))
            return true;

        return progress >= minNormalizedProgress;
    }

    /// <summary>
    /// Player progress along the track as 0..1. Returns false when track length can't be determined.
    /// </summary>
    private bool TryGetNormalizedProgress(out float progress)
    {
        progress = 0f;

        float total = 0f;
        if (trackGenerator != null)
        {
            var pts = new List<Vector3>();
            TrackPathSampling.BuildCenterlinePath(trackGenerator, pts);
            if (pts.Count >= 2)
            {
                float[] cum = new float[pts.Count];
                TrackPathSampling.BuildCumulativeLengths(pts, cum, out total);
            }
        }

        if (total <= 0.01f)
            return false;

        if (_distanceMeter == null)
            _distanceMeter = FindObjectOfType<TrackDistanceMeter>();

        float player = _distanceMeter != null ? _distanceMeter.DistanceAlongTrack : 0f;
        progress = Mathf.Clamp01(player / total);
        return true;
    }
}
