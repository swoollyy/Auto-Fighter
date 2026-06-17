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
        if (track == null || track.PathPoints == null || track.PathPoints.Count < 2)
            return result;

        var pts = track.PathPoints;
        int n = pts.Count;

        float totalLen = 0f;
        float bestSq = float.MaxValue;
        int bestSeg = 0;
        float bestT = 0f;
        float distAlongBest = 0f;
        float accum = 0f;

        for (int i = 0; i < n - 1; i++)
        {
            Vector3 a = pts[i];
            Vector3 b = pts[i + 1];
            Vector3 ab = b - a;
            float segLen = ab.magnitude;
            float t = segLen > 1e-6f ? Mathf.Clamp01(Vector3.Dot(worldPos - a, ab) / (segLen * segLen)) : 0f;
            Vector3 proj = Vector3.Lerp(a, b, t);
            float sq = (worldPos - proj).sqrMagnitude;

            if (sq < bestSq)
            {
                bestSq = sq;
                bestSeg = i;
                bestT = t;
                distAlongBest = accum + t * segLen;
            }

            accum += segLen;
        }

        totalLen = accum;
        if (totalLen <= 0.01f)
            return result;

        Vector3 segA = pts[bestSeg];
        Vector3 segB = pts[bestSeg + 1];
        Vector3 center = Vector3.Lerp(segA, segB, bestT);

        Vector3 forward = segB - segA;
        forward.y = 0f;
        if (forward.sqrMagnitude < 1e-6f)
            forward = Vector3.forward;
        forward.Normalize();

        Vector3 right = Vector3.Cross(Vector3.up, forward);
        Vector3 flatOffset = worldPos - center;
        flatOffset.y = 0f;
        float lateralMeters = Vector3.Dot(flatOffset, right);

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
        result.TrackProgressPercent = (distAlongBest / totalLen) * 100f;
        result.LateralText = lateralText;
        return result;
    }
}
