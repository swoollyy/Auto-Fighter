using System.Collections.Generic;
using UnityEngine;

public static class TrackSpawnPlacementUtil
{
    public struct Placement
    {
        public bool Valid;
        public float TrackProgressPercent;
        public string LateralText;
    }

    /// <summary>
    /// Lateral text is measured from track center: 0% = center, 100% right = right road edge, 100% left = left road edge.
    /// </summary>
    public static Placement Analyze(ProceduralTrackGenerator track, Vector3 worldPos)
    {
        Placement result = default;
        if (track == null) return result;

        var pts = new List<Vector3>();
        TrackPathSampling.BuildCenterlinePath(track, pts);
        if (pts.Count < 2) return result;

        float[] cum = new float[pts.Count];
        TrackPathSampling.BuildCumulativeLengths(pts, cum, out float totalLen);
        if (totalLen <= 0.01f) return result;

        if (!TrackPathSampling.ProjectWorldPosition(pts, cum, totalLen, worldPos, out float distAlong, out float lateralMeters))
            return result;

        float halfWidth = Mathf.Max(0.01f, track.RoadWidth * 0.5f);
        float ratio = Mathf.Abs(lateralMeters) / halfWidth;
        float lateralPct = ratio * 100f;

        string lateralText;
        if (lateralPct < 3f)
            lateralText = "center";
        else if (ratio > 1.02f)
            lateralText = lateralMeters > 0f ? "off-track right" : "off-track left";
        else if (lateralMeters > 0f)
            lateralText = $"{lateralPct:0}% right";
        else
            lateralText = $"{lateralPct:0}% left";

        result.Valid = true;
        result.TrackProgressPercent = (distAlong / totalLen) * 100f;
        result.LateralText = lateralText;
        return result;
    }
}
