using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Tracks occupied distance ranges along the track centerline so ice paths,
/// boost pads, and boost ramps do not spawn on top of each other.
/// </summary>
public static class TrackSurfaceSpawnRegistry
{
    private struct Reservation
    {
        public int Id;
        public float Start;
        public float End;
    }

    /// <summary>Extra meters kept clear between surface features.</summary>
    public const float SeparationGap = 3f;

    private static readonly List<Reservation> _reservations = new();
    private static int _nextId = 1;

    public static void Clear()
    {
        _reservations.Clear();
        _nextId = 1;
    }

    public static bool Overlaps(float start, float end)
    {
        float paddedStart = start - SeparationGap;
        float paddedEnd = end + SeparationGap;

        for (int i = 0; i < _reservations.Count; i++)
        {
            Reservation r = _reservations[i];
            if (paddedStart < r.End && r.Start < paddedEnd)
                return true;
        }

        return false;
    }

    public static int Register(float start, float end)
    {
        if (end <= start)
            return 0;

        int id = _nextId++;
        _reservations.Add(new Reservation { Id = id, Start = start, End = end });
        return id;
    }

    public static void Unregister(int id)
    {
        if (id <= 0)
            return;

        for (int i = _reservations.Count - 1; i >= 0; i--)
        {
            if (_reservations[i].Id == id)
            {
                _reservations.RemoveAt(i);
                return;
            }
        }
    }
}

public static class TrackSurfaceSpawnUtil
{
    private static readonly Dictionary<int, float> _halfExtentCache = new();

    public static bool IsBoostLikeSurfacePrefab(GameObject prefab)
    {
        return TryGetBoostLikeSurfaceType(prefab, out _);
    }

    public static bool TryGetBoostLikeSurfaceType(GameObject prefab, out SurfaceType surfaceType)
    {
        surfaceType = SurfaceType.Default;
        if (prefab == null)
            return false;

        GroundSurface gs = prefab.GetComponentInChildren<GroundSurface>(true);
        if (gs == null)
            return false;

        if (gs.surfaceType != SurfaceType.Boost && gs.surfaceType != SurfaceType.Ramp)
            return false;

        surfaceType = gs.surfaceType;
        return true;
    }

    public static bool TryGetBoostAlongTrackSpan(GameObject prefab, float centerDist, out float start, out float end)
    {
        start = end = 0f;
        if (!TryGetBoostLikeSurfaceType(prefab, out SurfaceType surfaceType))
            return false;

        float half = GetHalfExtentAlongTrack(prefab, surfaceType);
        start = Mathf.Max(0f, centerDist - half);
        end = centerDist + half;
        return end > start;
    }

    public static float GetIcePathLength(int segmentsPerPath, float segmentLength)
    {
        return Mathf.Max(0f, segmentsPerPath) * Mathf.Max(0.01f, segmentLength);
    }

    private static float GetHalfExtentAlongTrack(GameObject prefab, SurfaceType surfaceType)
    {
        int id = prefab.GetInstanceID();
        if (_halfExtentCache.TryGetValue(id, out float cached))
            return cached;

        float half = surfaceType == SurfaceType.Ramp ? 14f : 5f;

        GameObject temp = Object.Instantiate(prefab);
        temp.SetActive(false);
        temp.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        temp.transform.localScale = Vector3.one;

        bool measured = false;
        Collider[] colliders = temp.GetComponentsInChildren<Collider>(true);
        if (colliders != null && colliders.Length > 0)
        {
            Bounds b = default;
            for (int i = 0; i < colliders.Length; i++)
            {
                Collider col = colliders[i];
                if (col == null || col.isTrigger)
                    continue;

                if (!measured)
                {
                    b = col.bounds;
                    measured = true;
                }
                else
                {
                    b.Encapsulate(col.bounds);
                }
            }

            if (measured)
                half = Mathf.Max(2f, b.size.z * 0.5f);
        }

        if (!measured)
        {
            Renderer[] renderers = temp.GetComponentsInChildren<Renderer>(true);
            if (renderers != null && renderers.Length > 0)
            {
                Bounds b = renderers[0].bounds;
                for (int i = 1; i < renderers.Length; i++)
                    b.Encapsulate(renderers[i].bounds);

                half = Mathf.Max(2f, b.size.z * 0.5f);
            }
        }

        Object.Destroy(temp);
        _halfExtentCache[id] = half;
        return half;
    }
}
