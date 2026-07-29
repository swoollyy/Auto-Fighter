using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// After a procedural track is built, reshapes terrain: smooth macro hills via low-frequency noise
/// on a captured baseline, while forcing height under the road corridor (XZ distance to centerline)
/// to stay below a flat road plane (default Y=0).
/// </summary>
[DisallowMultipleComponent]
public sealed class TerrainAroundFlatRoad : MonoBehaviour
{
    [Header("Targets")]
    [Tooltip("Leave empty and enable Auto Discover to process every Terrain in the scene.")]
    [SerializeField] private Terrain[] terrains;

    [SerializeField] private bool autoDiscoverTerrains = true;

    [Header("Flat road (world)")]
    [Tooltip("Road is treated as an infinite horizontal plane at this world Y.")]
    [SerializeField] private float roadWorldY = 0f;

    [Tooltip("Terrain is clamped to at least this far below the road plane (avoids z-fighting).")]
    [SerializeField] private float carveBelowRoadMeters = 0.35f;

    [Header("Corridor (XZ)")]
    [Tooltip("Added to half road width for the fully carved zone.")]
    [SerializeField] private float extraHalfWidthMeters = 1.5f;

    [Tooltip("Beyond the carved zone, blend from carved heights up to full hills over this distance. Short values make steep, wonky road-facing slopes.")]
    [SerializeField] private float blendDistanceMeters = 22f;

    [Tooltip("Extra safety band outside the carved zone that is still clamped below road to prevent tiny terrain triangles from poking through.")]
    [SerializeField, Min(0f)] private float seamGuardBandMeters = 1.25f;

    [Tooltip("How far below road this seam-guard band is clamped.")]
    [SerializeField, Min(0f)] private float seamGuardBelowRoadMeters = 0.12f;

    [Header("Hills (additive on baseline heightmap)")]
    [Tooltip("Vertical strength of hills. Does not change how sharp they look by itself.")]
    [SerializeField] private float noiseAmplitudeMeters = 14f;

    [Tooltip("Noise frequency in world space. Lower = broad rolling shapes; higher = smaller, busier bumps.")]
    [SerializeField] private float noiseWorldScale = 0.012f;

    [Tooltip("Macro shape octaves only. Keep at 1–2 for drivably smooth slopes; 3+ adds speed-bump ripples.")]
    [SerializeField, Range(1, 6)] private int noiseOctaves = 2;

    [Tooltip("How strong each finer octave is. Lower ≈ smoother (try 0.3–0.45 for rolling hills).")]
    [SerializeField, Range(0.15f, 1f)] private float noisePersistence = 0.38f;

    [Tooltip("0 = signed rolling (peaks + dug valleys). 1 = hills only (no indented ditches on slopes). Try 0.7–0.9.")]
    [SerializeField, Range(0f, 1f)] private float hillBias = 0.8f;

    [Tooltip("Averages noise over this radius in meters. Softens sharp peaks without changing amplitude much.")]
    [SerializeField, Min(0f)] private float noiseSpatialSmoothingMeters = 10f;

    [Tooltip("Optional fine ripples on top of macro hills. Keep near 0 for smooth driving.")]
    [SerializeField, Min(0f)] private float microDetailAmplitudeMeters = 0.25f;

    [Tooltip("World-space frequency for micro detail (ignored when micro amplitude is 0).")]
    [SerializeField] private float microDetailWorldScale = 0.04f;

    [SerializeField] private bool randomizeNoiseSeedPerApply = true;

    [SerializeField] private Vector2 fixedNoiseSeedOffset;

    [Header("Heightmap smoothing")]
    [Tooltip("Box-blur passes after sculpt. Kills residual speed-bump frequencies on slopes.")]
    [SerializeField, Range(0, 4)] private int heightmapBlurPasses = 2;

    [Tooltip("Blur radius in heightmap samples (cells). 2–3 is usually enough.")]
    [SerializeField, Range(1, 6)] private int heightmapBlurRadiusSamples = 2;

    [Header("Performance")]
    [SerializeField] private bool spreadWorkAcrossFrames = true;

    [SerializeField, Min(1)] private int heightmapRowsPerYield = 6;
    [Tooltip("Soft CPU budget for height carving each frame. Lower = smoother loading UI, higher = faster total completion.")]
    [SerializeField, Min(0.25f)] private float heightGenFrameBudgetMs = 2.0f;

    [Tooltip("Resample centerline so distance checks are cheaper.")]
    [SerializeField, Min(0.5f)] private float polylineResampleStepMeters = 2f;

    [SerializeField, Min(32)] private int maxPolylinePoints = 1536;

    [Header("Play mode")]
    [SerializeField] private bool cloneTerrainDataAtRuntime = true;

    [SerializeField] private bool restoreOriginalTerrainDataOnDisable = true;

    private readonly List<Terrain> _resolvedTerrains = new List<Terrain>(16);
    private readonly List<TerrainData> _originalTerrainData = new List<TerrainData>(16);
    private readonly List<float[,]> _baselines = new List<float[,]>(16);
    private readonly List<Vector2> _polyScratch = new List<Vector2>(2048);
    private readonly List<Vector3> _pathScratch = new List<Vector3>(2048);

    private bool _clonedAny;
    private Vector2 _noiseSeed;
    private float[] _blurRowScratch;
    private float[] _blurColScratch;

    public bool SpreadWorkAcrossFrames => spreadWorkAcrossFrames;

    private void Awake()
    {
        ResolveTerrains();
        if (!Application.isPlaying) return;

        if (cloneTerrainDataAtRuntime)
        {
            _clonedAny = false;
            for (int i = 0; i < _resolvedTerrains.Count; i++)
            {
                Terrain t = _resolvedTerrains[i];
                if (t == null) continue;
                TerrainData td = t.terrainData;
                if (td == null) continue;

                _originalTerrainData.Add(td);
                TerrainData inst = Instantiate(td);
                inst.name = td.name + " (RuntimeClone)";
                t.terrainData = inst;
                _clonedAny = true;
            }
        }

        CaptureBaselines();
    }

    private void OnDisable()
    {
        if (!Application.isPlaying || !restoreOriginalTerrainDataOnDisable || !_clonedAny) return;

        for (int i = 0; i < _resolvedTerrains.Count; i++)
        {
            Terrain t = _resolvedTerrains[i];
            if (t == null) continue;
            if (i >= _originalTerrainData.Count) break;
            TerrainData orig = _originalTerrainData[i];
            TerrainData cur = t.terrainData;
            if (orig != null) t.terrainData = orig;
            if (cur != null && cur != orig) Destroy(cur);
        }

        _originalTerrainData.Clear();
        _clonedAny = false;
    }

    private void ResolveTerrains()
    {
        _resolvedTerrains.Clear();
        if (terrains != null)
        {
            for (int i = 0; i < terrains.Length; i++)
                if (terrains[i] != null)
                    _resolvedTerrains.Add(terrains[i]);
        }

        if (autoDiscoverTerrains || _resolvedTerrains.Count == 0)
        {
            Terrain[] found = FindObjectsOfType<Terrain>();
            for (int i = 0; i < found.Length; i++)
                if (found[i] != null && !_resolvedTerrains.Contains(found[i]))
                    _resolvedTerrains.Add(found[i]);
        }

        _resolvedTerrains.RemoveAll(t => t == null || t.terrainData == null);
        _resolvedTerrains.Sort((a, b) => a.GetInstanceID().CompareTo(b.GetInstanceID()));
    }

    private void CaptureBaselines()
    {
        _baselines.Clear();
        for (int i = 0; i < _resolvedTerrains.Count; i++)
        {
            Terrain t = _resolvedTerrains[i];
            TerrainData td = t.terrainData;
            int w = td.heightmapResolution;
            int h = td.heightmapResolution;
            _baselines.Add(td.GetHeights(0, 0, w, h));
        }
    }

    /// <summary>Re-read heightmaps as baselines (e.g. after editing terrain in editor).</summary>
    public void RecaptureBaselines()
    {
        ResolveTerrains();
        CaptureBaselines();
    }

    public void ApplyFromTrackSync(ProceduralTrackGenerator gen)
    {
        IEnumerator e = ApplyFromTrackAsync(gen);
        while (e.MoveNext()) { }
    }

    public IEnumerator ApplyFromTrackAsync(ProceduralTrackGenerator gen)
    {
        if (!Application.isPlaying)
        {
            Debug.LogWarning("[TerrainAroundFlatRoad] Apply runs in Play Mode only (terrain data cloning / baseline).");
            yield break;
        }

        if (gen == null || !gen.LastGenerateSucceeded)
            yield break;

        ResolveTerrains();
        if (_baselines.Count != _resolvedTerrains.Count)
            CaptureBaselines();

        if (_baselines.Count == 0)
        {
            Debug.LogWarning("[TerrainAroundFlatRoad] No terrains or baselines; nothing to do.");
            yield break;
        }

        gen.FillRoadMeshCenterPath(_pathScratch);
        if (_pathScratch.Count < 2)
        {
            Debug.LogWarning("[TerrainAroundFlatRoad] No centerline path; skip.");
            yield break;
        }

        BuildPolylineXZResampled(_pathScratch, _polyScratch);
        if (_polyScratch.Count < 2)
            yield break;

        float inner = gen.RoadWidth * 0.5f + extraHalfWidthMeters;
        float outer = inner + Mathf.Max(0.05f, blendDistanceMeters);
        float margin = outer + 1f;

        if (randomizeNoiseSeedPerApply)
            _noiseSeed = new Vector2(Random.Range(0f, 9999f), Random.Range(0f, 9999f));
        else
            _noiseSeed = fixedNoiseSeedOffset;

        ComputePathBoundsXZ(_polyScratch, margin, out float pMinX, out float pMaxX, out float pMinZ, out float pMaxZ);

        for (int ti = 0; ti < _resolvedTerrains.Count; ti++)
        {
            Terrain terrain = _resolvedTerrains[ti];
            if (terrain == null || terrain.terrainData == null) continue;
            if (ti >= _baselines.Count) break;

            TerrainData td = terrain.terrainData;
            float[,] baseline = _baselines[ti];
            if (baseline == null) continue;

            Vector3 tp = terrain.transform.position;
            Vector3 ts = td.size;
            if (!IntersectsXZRect(tp.x, tp.x + ts.x, tp.z, tp.z + ts.z, pMinX, pMaxX, pMinZ, pMaxZ))
                continue;

            float hMaxNorm = NormFromWorldY(terrain, roadWorldY - carveBelowRoadMeters);
            float hSeamGuardNorm = NormFromWorldY(terrain, roadWorldY - seamGuardBelowRoadMeters);
            int res = td.heightmapResolution;
            float[,] dst = new float[res, res];
            float lastYieldRealtime = Time.realtimeSinceStartup;
            float frameBudgetSeconds = Mathf.Max(0.00025f, heightGenFrameBudgetMs * 0.001f);

            for (int y = 0; y < res; y++)
            {
                if (spreadWorkAcrossFrames && y > 0)
                {
                    bool rowCadenceHit = (y % heightmapRowsPerYield) == 0;
                    bool budgetExceeded = (Time.realtimeSinceStartup - lastYieldRealtime) >= frameBudgetSeconds;
                    if (rowCadenceHit || budgetExceeded)
                    {
                        yield return null;
                        lastYieldRealtime = Time.realtimeSinceStartup;
                    }
                }

                float nz = y / (float)Mathf.Max(1, res - 1);
                float worldZ = tp.z + nz * ts.z;

                for (int x = 0; x < res; x++)
                {
                    float nx = x / (float)Mathf.Max(1, res - 1);
                    float worldX = tp.x + nx * ts.x;

                    float b = baseline[y, x];

                    if (worldX < pMinX || worldX > pMaxX || worldZ < pMinZ || worldZ > pMaxZ)
                    {
                        dst[y, x] = b;
                        continue;
                    }

                    Vector2 wxz = new Vector2(worldX, worldZ);
                    float d = DistancePointToPolylineXZ(wxz, _polyScratch);

                    float noiseM = SampleNoiseMeters(worldX, worldZ);
                    float hill = Mathf.Clamp01(b + noiseM / ts.y);

                    float carved = Mathf.Min(hill, hMaxNorm);
                    // Quintic smootherstep: C2-continuous at carve/hill boundaries (avoids kink "speed bumps").
                    float wBlend = Smoother01((d - inner) / Mathf.Max(0.001f, outer - inner));
                    float outH = Mathf.Lerp(carved, hill, wBlend);

                    // Feather seam guard instead of a hard Min clamp (hard edges create ledge bumps).
                    float guardOuter = inner + seamGuardBandMeters;
                    if (d < guardOuter && seamGuardBandMeters > 1e-4f)
                    {
                        float gT = Smoother01((d - inner) / seamGuardBandMeters);
                        float capped = Mathf.Min(outH, hSeamGuardNorm);
                        outH = Mathf.Lerp(capped, outH, gT);
                    }

                    dst[y, x] = outH;
                }
            }

            if (heightmapBlurPasses > 0)
            {
                for (int pass = 0; pass < heightmapBlurPasses; pass++)
                {
                    if (spreadWorkAcrossFrames)
                    {
                        yield return null;
                        lastYieldRealtime = Time.realtimeSinceStartup;
                    }

                    BoxBlurHeightmapInPlace(dst, res, heightmapBlurRadiusSamples);
                }

                // Re-enforce road corridor after blur so smoothed hills can't creep back under the road.
                for (int y = 0; y < res; y++)
                {
                    if (spreadWorkAcrossFrames && y > 0 && (y % heightmapRowsPerYield) == 0)
                    {
                        yield return null;
                        lastYieldRealtime = Time.realtimeSinceStartup;
                    }

                    float nz = y / (float)Mathf.Max(1, res - 1);
                    float worldZ = tp.z + nz * ts.z;

                    for (int x = 0; x < res; x++)
                    {
                        float nx = x / (float)Mathf.Max(1, res - 1);
                        float worldX = tp.x + nx * ts.x;

                        if (worldX < pMinX || worldX > pMaxX || worldZ < pMinZ || worldZ > pMaxZ)
                            continue;

                        float d = DistancePointToPolylineXZ(new Vector2(worldX, worldZ), _polyScratch);
                        if (d > outer)
                            continue;

                        float h = dst[y, x];
                        if (d <= inner)
                        {
                            dst[y, x] = Mathf.Min(h, hMaxNorm);
                            continue;
                        }

                        float wBlend = Smoother01((d - inner) / Mathf.Max(0.001f, outer - inner));
                        float carved = Mathf.Min(h, hMaxNorm);
                        h = Mathf.Lerp(carved, h, wBlend);

                        float guardOuter = inner + seamGuardBandMeters;
                        if (d < guardOuter && seamGuardBandMeters > 1e-4f)
                        {
                            float gT = Smoother01((d - inner) / seamGuardBandMeters);
                            float capped = Mathf.Min(h, hSeamGuardNorm);
                            h = Mathf.Lerp(capped, h, gT);
                        }

                        dst[y, x] = h;
                    }
                }
            }

            td.SetHeights(0, 0, dst);
            RefreshTerrainCollider(terrain, td);
        }

        Physics.SyncTransforms();
    }

    private void BoxBlurHeightmapInPlace(float[,] map, int res, int radius)
    {
        radius = Mathf.Clamp(radius, 1, 6);
        int diam = radius * 2 + 1;
        float inv = 1f / diam;

        if (_blurRowScratch == null || _blurRowScratch.Length < res)
            _blurRowScratch = new float[res];
        if (_blurColScratch == null || _blurColScratch.Length < res)
            _blurColScratch = new float[res];

        // Horizontal pass
        for (int y = 0; y < res; y++)
        {
            float sum = 0f;
            for (int i = -radius; i <= radius; i++)
                sum += map[y, Mathf.Clamp(i, 0, res - 1)];

            for (int x = 0; x < res; x++)
            {
                _blurRowScratch[x] = sum * inv;
                float remove = map[y, Mathf.Clamp(x - radius, 0, res - 1)];
                float add = map[y, Mathf.Clamp(x + radius + 1, 0, res - 1)];
                sum += add - remove;
            }

            for (int x = 0; x < res; x++)
                map[y, x] = _blurRowScratch[x];
        }

        // Vertical pass
        for (int x = 0; x < res; x++)
        {
            float sum = 0f;
            for (int i = -radius; i <= radius; i++)
                sum += map[Mathf.Clamp(i, 0, res - 1), x];

            for (int y = 0; y < res; y++)
            {
                _blurColScratch[y] = sum * inv;
                float remove = map[Mathf.Clamp(y - radius, 0, res - 1), x];
                float add = map[Mathf.Clamp(y + radius + 1, 0, res - 1), x];
                sum += add - remove;
            }

            for (int y = 0; y < res; y++)
                map[y, x] = _blurColScratch[y];
        }
    }

    private static void RefreshTerrainCollider(Terrain terrain, TerrainData td)
    {
        if (terrain == null || td == null) return;

        // Flush heightmap updates so rendering/queries are up to date.
        terrain.Flush();

        // Force TerrainCollider to use the freshly updated TerrainData.
        var tc = terrain.GetComponent<TerrainCollider>();
        if (tc != null)
        {
            tc.enabled = false;
            tc.terrainData = td;
            tc.enabled = true;
        }
    }

    private static float NormFromWorldY(Terrain terrain, float worldY)
    {
        float baseY = terrain.transform.position.y;
        float h = terrain.terrainData.size.y;
        if (h < 1e-4f) return 0f;
        return Mathf.Clamp01((worldY - baseY) / h);
    }

    private static bool IntersectsXZRect(float tMinX, float tMaxX, float tMinZ, float tMaxZ,
        float bMinX, float bMaxX, float bMinZ, float bMaxZ)
    {
        if (tMaxX < bMinX || tMinX > bMaxX) return false;
        if (tMaxZ < bMinZ || tMinZ > bMaxZ) return false;
        return true;
    }

    private static void ComputePathBoundsXZ(IReadOnlyList<Vector2> poly, float margin,
        out float minX, out float maxX, out float minZ, out float maxZ)
    {
        minX = maxX = poly[0].x;
        minZ = maxZ = poly[0].y;
        for (int i = 1; i < poly.Count; i++)
        {
            Vector2 p = poly[i];
            if (p.x < minX) minX = p.x;
            if (p.x > maxX) maxX = p.x;
            if (p.y < minZ) minZ = p.y;
            if (p.y > maxZ) maxZ = p.y;
        }

        minX -= margin;
        maxX += margin;
        minZ -= margin;
        maxZ += margin;
    }

    private void BuildPolylineXZResampled(List<Vector3> worldPath, List<Vector2> dst)
    {
        dst.Clear();
        if (worldPath.Count < 2) return;

        var pts = new List<Vector2>(worldPath.Count);
        for (int i = 0; i < worldPath.Count; i++)
            pts.Add(new Vector2(worldPath[i].x, worldPath[i].z));

        float step = Mathf.Max(0.5f, polylineResampleStepMeters);
        dst.Add(pts[0]);

        float carry = 0f;
        for (int i = 0; i < pts.Count - 1; i++)
        {
            Vector2 a = pts[i];
            Vector2 b = pts[i + 1];
            Vector2 ab = b - a;
            float segLen = ab.magnitude;
            if (segLen < 1e-6f) continue;
            Vector2 dir = ab / segLen;

            float d = carry;
            while (d < segLen)
            {
                Vector2 q = a + dir * d;
                if ((q - dst[dst.Count - 1]).sqrMagnitude > 1e-6f)
                {
                    dst.Add(q);
                    if (dst.Count >= maxPolylinePoints)
                    {
                        DownsampleIfNeeded(dst, maxPolylinePoints);
                        return;
                    }
                }

                d += step;
            }

            carry = d - segLen;
        }

        Vector2 last = pts[pts.Count - 1];
        if ((last - dst[dst.Count - 1]).sqrMagnitude > 1e-6f)
            dst.Add(last);

        DownsampleIfNeeded(dst, maxPolylinePoints);
    }

    private static void DownsampleIfNeeded(List<Vector2> dst, int maxPts)
    {
        if (dst.Count <= maxPts) return;
        int step = Mathf.CeilToInt(dst.Count / (float)maxPts);
        var tmp = new List<Vector2>(maxPts + 2);
        for (int i = 0; i < dst.Count; i += step)
            tmp.Add(dst[i]);
        if (tmp[tmp.Count - 1].x != dst[dst.Count - 1].x || tmp[tmp.Count - 1].y != dst[dst.Count - 1].y)
            tmp.Add(dst[dst.Count - 1]);
        dst.Clear();
        dst.AddRange(tmp);
    }

    private static float DistancePointToPolylineXZ(Vector2 p, IReadOnlyList<Vector2> poly)
    {
        float best = float.MaxValue;
        for (int i = 0; i < poly.Count - 1; i++)
        {
            float d = DistancePointToSegmentXZ(p, poly[i], poly[i + 1]);
            if (d < best) best = d;
        }

        return best;
    }

    private static float DistancePointToSegmentXZ(Vector2 p, Vector2 a, Vector2 b)
    {
        Vector2 ab = b - a;
        float den = ab.sqrMagnitude;
        if (den < 1e-8f) return Vector2.Distance(p, a);
        float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / den);
        Vector2 q = a + ab * t;
        return Vector2.Distance(p, q);
    }

    private float SampleNoiseMeters(float worldX, float worldZ)
    {
        if (noiseSpatialSmoothingMeters < 1e-4f)
            return SampleNoiseMetersRaw(worldX, worldZ);

        // 3x3 weighted cross (center + cardinals + diagonals) for smoother macro shapes.
        float r = noiseSpatialSmoothingMeters;
        float rDiag = r * 0.70710678f;
        float s = SampleNoiseMetersRaw(worldX, worldZ) * 4f;
        s += SampleNoiseMetersRaw(worldX + r, worldZ) * 2f;
        s += SampleNoiseMetersRaw(worldX - r, worldZ) * 2f;
        s += SampleNoiseMetersRaw(worldX, worldZ + r) * 2f;
        s += SampleNoiseMetersRaw(worldX, worldZ - r) * 2f;
        s += SampleNoiseMetersRaw(worldX + rDiag, worldZ + rDiag);
        s += SampleNoiseMetersRaw(worldX + rDiag, worldZ - rDiag);
        s += SampleNoiseMetersRaw(worldX - rDiag, worldZ + rDiag);
        s += SampleNoiseMetersRaw(worldX - rDiag, worldZ - rDiag);
        return s * (1f / 16f);
    }

    private float SampleNoiseMetersRaw(float worldX, float worldZ)
    {
        float sum = 0f;
        float amp = 1f;
        float freq = noiseWorldScale;
        for (int o = 0; o < noiseOctaves; o++)
        {
            float nx = worldX * freq + _noiseSeed.x + o * 19.1f;
            float nz = worldZ * freq + _noiseSeed.y + o * 27.7f;
            float s = Mathf.PerlinNoise(nx, nz);
            sum += (s - 0.5f) * 2f * amp;
            amp *= noisePersistence;
            freq *= 2f;
        }

        // Soft-rectify: squashes dug-out valleys that make one face of a hill look indented/wonky.
        float pos = Mathf.Max(0f, sum);
        float neg = Mathf.Min(0f, sum);
        float shaped = pos + neg * (1f - hillBias);

        float meters = shaped * noiseAmplitudeMeters;

        if (microDetailAmplitudeMeters > 1e-4f)
        {
            float mx = worldX * microDetailWorldScale + _noiseSeed.x + 101.3f;
            float mz = worldZ * microDetailWorldScale + _noiseSeed.y + 77.9f;
            float micro = (Mathf.PerlinNoise(mx, mz) - 0.5f) * 2f;
            meters += micro * microDetailAmplitudeMeters;
        }

        return meters;
    }

    /// <summary>Ken Perlin's smootherstep — zero 1st and 2nd derivatives at 0 and 1.</summary>
    private static float Smoother01(float t)
    {
        t = Mathf.Clamp01(t);
        return t * t * t * (t * (t * 6f - 15f) + 10f);
    }
}
