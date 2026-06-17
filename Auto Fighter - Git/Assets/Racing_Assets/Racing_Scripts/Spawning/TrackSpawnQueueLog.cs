using System.Collections.Generic;
using System.Text;
using UnityEngine;

public static class TrackSpawnQueueLog
{
    public static void LogEnqueued(
        ITrackSpawnQueueSource newlyQueued,
        IReadOnlyList<ITrackSpawnQueueSource> pending,
        TrackSpawnerQueue.PlaybackMode mode)
    {
        if (newlyQueued == null)
            return;

        var sb = new StringBuilder();
        sb.AppendLine($"[SpawnQueue] ENQUEUED: {newlyQueued.SpawnQueueLabel}");
        sb.AppendLine($"  Pending ({pending.Count}):");

        if (pending.Count == 0)
        {
            sb.AppendLine("    (empty)");
        }
        else
        {
            for (int i = 0; i < pending.Count; i++)
            {
                ITrackSpawnQueueSource item = pending[i];
                string label = item != null ? item.SpawnQueueLabel : "(null)";
                string marker = item == newlyQueued ? " <- new" : string.Empty;
                sb.AppendLine($"    {i + 1}. {label}{marker}");
            }
        }

        sb.Append($"  Playback: {mode}");
        Debug.Log(sb.ToString());
    }

    public static void LogSpawned(
        ITrackSpawnQueueSource source,
        TrackSpawnQueueSpawnReport report,
        ProceduralTrackGenerator track)
    {
        string label = source != null ? source.SpawnQueueLabel : "Unknown";
        string objectName = !string.IsNullOrEmpty(report.ObjectName) ? report.ObjectName : label;

        if (!report.HasData)
        {
            Debug.Log($"[SpawnQueue] SPAWNED: {objectName} | placement: n/a");
            return;
        }

        TrackSpawnPlacementUtil.Placement placement = TrackSpawnPlacementUtil.Analyze(track, report.WorldPosition);
        if (!placement.Valid)
        {
            Debug.Log(
                $"[SpawnQueue] SPAWNED: {objectName} | Track: n/a | Lateral: n/a | Pos: {FormatPos(report.WorldPosition)}");
            return;
        }

        Debug.Log(
            $"[SpawnQueue] SPAWNED: {objectName} | Track: {placement.TrackProgressPercent:0.0}% | Lateral: {placement.LateralText}");
    }

    private static string FormatPos(Vector3 pos) =>
        $"({pos.x:0.0}, {pos.y:0.0}, {pos.z:0.0})";
}
