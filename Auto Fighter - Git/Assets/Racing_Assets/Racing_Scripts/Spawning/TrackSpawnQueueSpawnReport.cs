using UnityEngine;

public struct TrackSpawnQueueSpawnReport
{
    public bool HasData;
    public Vector3 WorldPosition;
    public string ObjectName;
}

/// <summary>Records the most recent queue-driven spawn for diagnostics.</summary>
public class TrackSpawnQueueLastSpawn
{
    private TrackSpawnQueueSpawnReport _report;
    private bool _has;

    public void Record(Vector3 worldPosition, string objectName = null)
    {
        _report = new TrackSpawnQueueSpawnReport
        {
            HasData = true,
            WorldPosition = worldPosition,
            ObjectName = string.IsNullOrEmpty(objectName) ? null : objectName
        };
        _has = true;
    }

    public bool TryConsume(out TrackSpawnQueueSpawnReport report)
    {
        if (!_has)
        {
            report = default;
            return false;
        }

        report = _report;
        _has = false;
        return true;
    }
}
