using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Resolves which Unity Terrain tiles a procedural track centerline overlaps in XZ.
/// Used by terrain sculpt, grass painting, and environment placement during loading.
/// </summary>
public static class TrackTerrainOverlap
{
    private static readonly List<Vector3> PathScratch = new List<Vector3>(2048);

    public static int CollectFromTrack(
        ProceduralTrackGenerator gen,
        float marginMeters,
        List<Terrain> results,
        Terrain[] candidates = null)
    {
        if (results == null) return 0;
        results.Clear();
        if (gen == null) return 0;

        PathScratch.Clear();
        gen.FillRoadMeshCenterPath(PathScratch);
        return CollectFromPath(PathScratch, marginMeters, results, candidates);
    }

    public static int CollectFromPath(
        IList<Vector3> pathWorldPoints,
        float marginMeters,
        List<Terrain> results,
        Terrain[] candidates = null)
    {
        if (results == null) return 0;
        results.Clear();
        if (pathWorldPoints == null || pathWorldPoints.Count < 2) return 0;

        marginMeters = Mathf.Max(0f, marginMeters);
        float minX = float.PositiveInfinity;
        float maxX = float.NegativeInfinity;
        float minZ = float.PositiveInfinity;
        float maxZ = float.NegativeInfinity;

        for (int i = 0; i < pathWorldPoints.Count; i++)
        {
            Vector3 p = pathWorldPoints[i];
            if (p.x < minX) minX = p.x;
            if (p.x > maxX) maxX = p.x;
            if (p.z < minZ) minZ = p.z;
            if (p.z > maxZ) maxZ = p.z;
        }

        minX -= marginMeters;
        maxX += marginMeters;
        minZ -= marginMeters;
        maxZ += marginMeters;

        Terrain[] terrains = candidates;
        if (terrains == null || terrains.Length == 0)
            terrains = Terrain.activeTerrains;

        if (terrains == null || terrains.Length == 0)
            terrains = Object.FindObjectsOfType<Terrain>();

        if (terrains == null) return 0;

        for (int i = 0; i < terrains.Length; i++)
        {
            Terrain t = terrains[i];
            if (t == null || t.terrainData == null) continue;
            if (!t.gameObject.activeInHierarchy) continue;

            Vector3 tp = t.transform.position;
            Vector3 sz = t.terrainData.size;
            float tMinX = tp.x;
            float tMaxX = tp.x + sz.x;
            float tMinZ = tp.z;
            float tMaxZ = tp.z + sz.z;

            if (!IntersectsXZ(tMinX, tMaxX, tMinZ, tMaxZ, minX, maxX, minZ, maxZ))
                continue;

            if (!results.Contains(t))
                results.Add(t);
        }

        results.Sort((a, b) => a.GetInstanceID().CompareTo(b.GetInstanceID()));
        return results.Count;
    }

    /// <summary>
    /// Adds terrains that share an edge (cardinal neighbors) with any seed tile.
    /// Used so sculpt can mesh seams without processing the entire 3×3 grid.
    /// </summary>
    public static int ExpandWithCardinalNeighbors(
        IList<Terrain> seeds,
        List<Terrain> results,
        Terrain[] candidates = null,
        float gapSlopMeters = 2f)
    {
        if (results == null) return 0;
        results.Clear();
        if (seeds == null || seeds.Count == 0) return 0;

        for (int i = 0; i < seeds.Count; i++)
        {
            Terrain s = seeds[i];
            if (s != null && s.terrainData != null && !results.Contains(s))
                results.Add(s);
        }

        Terrain[] terrains = candidates;
        if (terrains == null || terrains.Length == 0)
            terrains = Terrain.activeTerrains;
        if (terrains == null || terrains.Length == 0)
            terrains = Object.FindObjectsOfType<Terrain>();
        if (terrains == null) return results.Count;

        gapSlopMeters = Mathf.Max(0f, gapSlopMeters);
        int seedCount = results.Count; // only expand from original seeds once
        for (int si = 0; si < seedCount; si++)
        {
            Terrain seed = results[si];
            if (seed == null || seed.terrainData == null) continue;
            GetTerrainXZ(seed, out float sMinX, out float sMaxX, out float sMinZ, out float sMaxZ);

            for (int i = 0; i < terrains.Length; i++)
            {
                Terrain t = terrains[i];
                if (t == null || t.terrainData == null) continue;
                if (!t.gameObject.activeInHierarchy) continue;
                if (results.Contains(t)) continue;

                GetTerrainXZ(t, out float tMinX, out float tMaxX, out float tMinZ, out float tMaxZ);

                bool overlapZ = tMinZ <= sMaxZ + gapSlopMeters && tMaxZ >= sMinZ - gapSlopMeters;
                bool overlapX = tMinX <= sMaxX + gapSlopMeters && tMaxX >= sMinX - gapSlopMeters;
                bool touchEastWest =
                    overlapZ &&
                    (Mathf.Abs(tMinX - sMaxX) <= gapSlopMeters || Mathf.Abs(tMaxX - sMinX) <= gapSlopMeters);
                bool touchNorthSouth =
                    overlapX &&
                    (Mathf.Abs(tMinZ - sMaxZ) <= gapSlopMeters || Mathf.Abs(tMaxZ - sMinZ) <= gapSlopMeters);

                if (touchEastWest || touchNorthSouth)
                    results.Add(t);
            }
        }

        results.Sort((a, b) => a.GetInstanceID().CompareTo(b.GetInstanceID()));
        return results.Count;
    }

    private static void GetTerrainXZ(
        Terrain t, out float minX, out float maxX, out float minZ, out float maxZ)
    {
        Vector3 tp = t.transform.position;
        Vector3 sz = t.terrainData.size;
        minX = tp.x;
        maxX = tp.x + sz.x;
        minZ = tp.z;
        maxZ = tp.z + sz.z;
    }

    public static bool ContainsXZ(Terrain terrain, float worldX, float worldZ)
    {
        if (terrain == null || terrain.terrainData == null) return false;
        Vector3 tp = terrain.transform.position;
        Vector3 sz = terrain.terrainData.size;
        return worldX >= tp.x && worldX <= tp.x + sz.x &&
               worldZ >= tp.z && worldZ <= tp.z + sz.z;
    }

    public static bool IsOnAny(IList<Terrain> terrains, float worldX, float worldZ)
    {
        if (terrains == null || terrains.Count == 0) return false;
        for (int i = 0; i < terrains.Count; i++)
        {
            if (ContainsXZ(terrains[i], worldX, worldZ))
                return true;
        }

        return false;
    }

    private static bool IntersectsXZ(
        float aMinX, float aMaxX, float aMinZ, float aMaxZ,
        float bMinX, float bMaxX, float bMinZ, float bMaxZ)
    {
        return aMinX <= bMaxX && aMaxX >= bMinX && aMinZ <= bMaxZ && aMaxZ >= bMinZ;
    }
}
