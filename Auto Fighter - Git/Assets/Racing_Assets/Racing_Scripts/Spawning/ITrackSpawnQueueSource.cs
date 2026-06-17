/// <summary>
/// Spawner hooks for <see cref="TrackSpawnerQueue"/>.
/// Each spawner keeps its own cooldown/rules; when ready it submits a request.
/// The queue decides when requests become real spawns on the field.
/// </summary>
public interface ITrackSpawnQueueSource
{
    string SpawnQueueLabel { get; }
    bool IsSpawnQueueReady { get; }
    bool HasSpawnQueueCapacity { get; }
    bool HasPendingSpawnRequest { get; }

    /// <summary>Spawner calls when its own cooldown says it is ready. Does not spawn yet.</summary>
    bool TrySubmitSpawnRequest();

    /// <summary>Queue calls this to perform the actual spawn.</summary>
    bool TryExecutePendingSpawn();

    /// <summary>Queue reads placement info from the last successful execute.</summary>
    bool TryConsumeLastSpawnReport(out TrackSpawnQueueSpawnReport report);

    void CancelPendingSpawnRequest();

    void SetQueueControlledAutonomous(bool controlled, TrackSpawnerQueue owner = null);
}
