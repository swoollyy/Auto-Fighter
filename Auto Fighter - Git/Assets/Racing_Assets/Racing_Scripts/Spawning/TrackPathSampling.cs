using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Shared centerline sampling for systems that must follow the same path as the generated road mesh
/// (rolling logs, placement debug, etc.).
/// </summary>
public static class TrackPathSampling
{
    public static void BuildCenterlinePath(ProceduralTrackGenerator generator, List<Vector3> output)
    {
        output.Clear();
        if (generator == null) return;
        generator.FillRoadMeshCenterPath(output);
    }

    /// <summary>
    /// Rebuilds a spawn/move path from the same centerline polyline as the road mesh collider.
    /// </summary>
    public static bool RebuildPathFromRoadCenterline(
        ProceduralTrackGenerator generator,
        List<Vector3> path,
        ref float[] cumulativeLengths,
        out float totalLength)
    {
        totalLength = 0f;
        path.Clear();
        BuildCenterlinePath(generator, path);
        if (path.Count < 2)
            return false;

        if (cumulativeLengths == null || cumulativeLengths.Length < path.Count)
            cumulativeLengths = new float[path.Count];

        BuildCumulativeLengths(path, cumulativeLengths, out totalLength);
        return totalLength > 1e-4f;
    }

    public static void BuildCumulativeLengths(IReadOnlyList<Vector3> path, float[] cumulativeOut, out float totalLength)
    {
        totalLength = 0f;
        if (path == null || path.Count < 2 || cumulativeOut == null || cumulativeOut.Length < path.Count)
            return;

        cumulativeOut[0] = 0f;
        for (int i = 1; i < path.Count; i++)
        {
            totalLength += Vector3.Distance(path[i - 1], path[i]);
            cumulativeOut[i] = totalLength;
        }
    }

    /// <summary>
    /// Matches <see cref="ProceduralTrackGenerator"/> road-mesh cross sections so lane offsets stay parallel to the driven surface in turns.
    /// </summary>
    public static Vector3 ComputeMiteredForward(IReadOnlyList<Vector3> path, int i)
    {
        if (path == null || path.Count < 2) return Vector3.forward;

        int n = path.Count;
        if (i <= 0)
        {
            Vector3 f = path[1] - path[0];
            return f.sqrMagnitude > 1e-8f ? f.normalized : Vector3.forward;
        }

        if (i >= n - 1)
        {
            Vector3 f = path[n - 1] - path[n - 2];
            return f.sqrMagnitude > 1e-8f ? f.normalized : Vector3.forward;
        }

        Vector3 fIn = path[i] - path[i - 1];
        Vector3 fOut = path[i + 1] - path[i];
        if (fIn.sqrMagnitude < 1e-8f) return fOut.sqrMagnitude > 1e-8f ? fOut.normalized : Vector3.forward;
        if (fOut.sqrMagnitude < 1e-8f) return fIn.normalized;

        Vector3 avg = fIn.normalized + fOut.normalized;
        return avg.sqrMagnitude > 1e-8f ? avg.normalized : fOut.normalized;
    }

    public static void SampleAlongPath(
        IReadOnlyList<Vector3> path,
        float[] cumulativeLengths,
        float totalLength,
        float dist,
        out Vector3 position,
        out Vector3 miteredForward)
    {
        position = Vector3.zero;
        miteredForward = Vector3.forward;

        if (path == null || path.Count < 2 || cumulativeLengths == null || totalLength <= 1e-4f)
            return;

        dist = Mathf.Clamp(dist, 0f, totalLength);

        int idx = 0;
        for (int i = 0; i < cumulativeLengths.Length - 1; i++)
        {
            if (cumulativeLengths[i + 1] >= dist)
            {
                idx = i;
                break;
            }
        }

        float segLen = cumulativeLengths[idx + 1] - cumulativeLengths[idx];
        float t = segLen > 1e-4f ? (dist - cumulativeLengths[idx]) / segLen : 0f;

        position = Vector3.Lerp(path[idx], path[idx + 1], t);

        Vector3 fwdA = ComputeMiteredForward(path, idx);
        Vector3 fwdB = ComputeMiteredForward(path, idx + 1);
        miteredForward = Vector3.Slerp(fwdA, fwdB, t);
        miteredForward.y = 0f;
        if (miteredForward.sqrMagnitude < 1e-6f)
            miteredForward = Vector3.forward;
        miteredForward.Normalize();
    }

    public static Vector3 ComputeMiteredRight(IReadOnlyList<Vector3> path, int vertexIndex)
    {
        Vector3 fwd = ComputeMiteredForward(path, vertexIndex);
        fwd.y = 0f;
        if (fwd.sqrMagnitude < 1e-6f)
            fwd = Vector3.forward;
        fwd.Normalize();
        Vector3 right = Vector3.Cross(Vector3.up, fwd);
        return right.sqrMagnitude > 1e-6f ? right.normalized : Vector3.right;
    }

    /// <summary>
    /// Projects a world position onto the path polyline. Returns arclength and signed lateral meters
    /// relative to the closest segment's horizontal forward.
    /// </summary>
    public static bool ProjectWorldPosition(
        IReadOnlyList<Vector3> path,
        float[] cumulativeLengths,
        float totalLength,
        Vector3 worldPos,
        out float distanceAlong,
        out float lateralOffset)
    {
        distanceAlong = 0f;
        lateralOffset = 0f;

        if (path == null || path.Count < 2 || cumulativeLengths == null || totalLength <= 1e-4f)
            return false;

        float bestSq = float.MaxValue;
        int bestIdx = 0;
        float bestT = 0f;

        for (int i = 0; i < path.Count - 1; i++)
        {
            Vector3 a = path[i];
            Vector3 b = path[i + 1];
            Vector3 ab = b - a;
            float abSqr = ab.sqrMagnitude;
            float t = abSqr > 1e-6f ? Mathf.Clamp01(Vector3.Dot(worldPos - a, ab) / abSqr) : 0f;
            Vector3 proj = Vector3.Lerp(a, b, t);
            float sq = (worldPos - proj).sqrMagnitude;
            if (sq < bestSq)
            {
                bestSq = sq;
                bestIdx = i;
                bestT = t;
            }
        }

        float segLen = cumulativeLengths[bestIdx + 1] - cumulativeLengths[bestIdx];
        distanceAlong = cumulativeLengths[bestIdx] + bestT * segLen;

        Vector3 center = Vector3.Lerp(path[bestIdx], path[bestIdx + 1], bestT);

        Vector3 fwdA = ComputeMiteredForward(path, bestIdx);
        Vector3 fwdB = ComputeMiteredForward(path, bestIdx + 1);
        Vector3 fwd = Vector3.Slerp(fwdA, fwdB, bestT);
        fwd.y = 0f;
        if (fwd.sqrMagnitude < 1e-6f)
            fwd = Vector3.forward;
        fwd.Normalize();

        Vector3 right = Vector3.Cross(Vector3.up, fwd);
        Vector3 flatOffset = worldPos - center;
        flatOffset.y = 0f;
        lateralOffset = Vector3.Dot(flatOffset, right);
        return true;
    }
}
