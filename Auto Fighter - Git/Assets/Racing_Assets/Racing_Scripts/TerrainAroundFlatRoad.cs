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
    [Tooltip("Box-blur passes after sculpt on tiles the track overlaps. Kills residual speed-bump frequencies.")]
    [SerializeField, Range(0, 4)] private int heightmapBlurPasses = 2;

    [Tooltip("Blur passes for neighbor tiles (no road corridor). 0–1 keeps seams soft without full cost.")]
    [SerializeField, Range(0, 2)] private int neighborBlurPasses = 1;

    [Tooltip("Blur radius in heightmap samples (cells). 2–3 is usually enough.")]
    [SerializeField, Range(1, 6)] private int heightmapBlurRadiusSamples = 2;

    [Header("Performance")]
    [SerializeField] private bool spreadWorkAcrossFrames = true;

    [SerializeField, Min(1)] private int heightmapRowsPerYield = 20;
    [Tooltip("Soft CPU budget for height carving each frame. Lower = smoother loading UI, higher = faster total completion.")]
    [SerializeField, Min(0.25f)] private float heightGenFrameBudgetMs = 10f;

    [Tooltip("If true, skip 3×3 spatial noise smoothing during apply (much faster; still world-continuous).")]
    [SerializeField] private bool useFastNoiseDuringApply = true;

    [Tooltip("If true, only sculpt terrains the road overlaps. If false, sculpt every scene terrain.")]
    [SerializeField] private bool sculptRoadOverlappingTerrainsOnly = true;

    [Tooltip("Deprecated: neighbors are no longer sculpted. Kept so old scenes don't remap other fields.")]
    [SerializeField] private bool sculptOverlapAndNeighborsOnly = true;

    [Tooltip("Extra meters beyond the road blend band where hills are generated. Outside this, terrain stays on baseline (no full-tile noise).")]
    [SerializeField, Min(5f)] private float hillBandExtraMeters = 55f;

    [Tooltip("Resample centerline so distance checks are cheaper.")]
    [SerializeField, Min(0.5f)] private float polylineResampleStepMeters = 2f;

    [SerializeField, Min(32)] private int maxPolylinePoints = 1536;

    [Header("Play mode")]
    [SerializeField] private bool cloneTerrainDataAtRuntime = true;

    [SerializeField] private bool restoreOriginalTerrainDataOnDisable = true;

    private readonly List<Terrain> _resolvedTerrains = new List<Terrain>(16);
    private readonly Dictionary<Terrain, TerrainData> _authoredByTerrain = new Dictionary<Terrain, TerrainData>();
    private readonly Dictionary<Terrain, int[][,]> _authoredDetails = new Dictionary<Terrain, int[][,]>();
    private readonly List<float[,]> _baselines = new List<float[,]>(16);
    private readonly List<int[][,]> _detailBaselines = new List<int[][,]>(16);
    private readonly List<Vector2> _polyScratch = new List<Vector2>(2048);
    private readonly List<Vector3> _pathScratch = new List<Vector3>(2048);

    private bool _clonedAny;
    private Vector2 _noiseSeed;
    private float[] _blurRowScratch;
    private float[] _blurColScratch;

    private readonly List<Terrain> _overlapScratch = new List<Terrain>(16);
    private readonly List<Terrain> _sculptScratch = new List<Terrain>(16);

    private bool _fastNoiseThisApply;
    private float _roadCutRadius;

    /// <summary>Terrains that last overlapped the track corridor during ApplyFromTrack.</summary>
    public int LastOverlappingTerrainCount { get; private set; }

    /// <summary>All terrain planes that received the shared world-noise sculpt last apply.</summary>
    public int LastSculptedTerrainCount { get; private set; }

    public bool SpreadWorkAcrossFrames => spreadWorkAcrossFrames;

    private void Awake()
    {
        // Snapshot every scene terrain's authored grass now — Unity Play, before any
        // in-game run clones/paints. Do not wait for the Inspector list / auto-discover
        // sculpt set; a tile first seen after it already has a RuntimeClone cannot be reset.
        ResolveTerrains();
        Terrain[] found = FindObjectsOfType<Terrain>();
        for (int i = 0; i < found.Length; i++)
        {
            Terrain t = found[i];
            if (t == null || t.terrainData == null) continue;
            RememberAuthored(t, t.terrainData);
        }
    }

    private void OnDisable()
    {
        if (!Application.isPlaying || !restoreOriginalTerrainDataOnDisable || !_clonedAny) return;

        for (int i = 0; i < _resolvedTerrains.Count; i++)
        {
            Terrain t = _resolvedTerrains[i];
            if (t == null) continue;
            TerrainData orig;
            if (!_authoredByTerrain.TryGetValue(t, out orig) || orig == null)
                continue;
            TerrainData cur = t.terrainData;
            t.terrainData = orig;
            if (cur != null && cur != orig) Destroy(cur);
        }

        _baselines.Clear();
        _detailBaselines.Clear();
        _resolvedTerrains.Clear();
        _clonedAny = false;
        // Keep _authoredByTerrain / _authoredDetails — those were captured at Unity Play
        // before any grass paint. Clearing them made later runs recapture dirty maps.
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

    /// <summary>
    /// Adds any missing scene terrains (with runtime clones + baselines) so apply can sculpt
    /// every plane the track crosses — even when the Inspector only listed the start tile.
    /// </summary>
    private void EnsureAllSceneTerrainsPrepared()
    {
        Terrain[] found = FindObjectsOfType<Terrain>();
        for (int i = 0; i < found.Length; i++)
        {
            Terrain t = found[i];
            if (t == null || t.terrainData == null) continue;
            if (_resolvedTerrains.Contains(t)) continue;
            PrepareAndAddTerrain(t);
        }

        _resolvedTerrains.RemoveAll(t => t == null || t.terrainData == null);
    }

    private void PrepareAndAddTerrain(Terrain t)
    {
        TerrainData td = t.terrainData;
        if (td == null) return;
        RememberAuthored(t, td);
        if (_resolvedTerrains.Contains(t)) return;
        _resolvedTerrains.Add(t);
    }

    private void RememberAuthored(Terrain t, TerrainData td)
    {
        if (t == null || td == null) return;
        if (IsRuntimeClone(td)) return;
        if (!_authoredByTerrain.ContainsKey(t))
            _authoredByTerrain[t] = td;
        if (!_authoredDetails.ContainsKey(t))
            _authoredDetails[t] = CopyAllDetailLayers(td);
    }

    /// <summary>
    /// Every run does what the first run does: clone the authored TerrainData, never reuse
    /// last run's painted grass maps.
    /// </summary>
    private void RecreateRuntimeCloneFromOriginal(Terrain t)
    {
        if (!Application.isPlaying || !cloneTerrainDataAtRuntime || t == null) return;
        TerrainData cur = t.terrainData;
        if (cur == null) return;

        RememberAuthored(t, cur);

        TerrainData authored;
        if (!_authoredByTerrain.TryGetValue(t, out authored) || authored == null)
        {
            if (IsRuntimeClone(cur))
            {
                Debug.LogWarning("[TerrainAroundFlatRoad] No authored TerrainData for " + t.name + "; cannot reset grass.");
                RefreshTerrainCollider(t, cur);
                return;
            }
            authored = cur;
            _authoredByTerrain[t] = authored;
        }

        TerrainData inst = Object.Instantiate(authored);
        inst.name = authored.name + " (RuntimeClone)";
        // First Unity detail-patch build must already lack blades on the new asphalt.
        // That is what kept run 1 clear before the shader clip existed.
        CutDetailsUnderPath(inst, t.transform.position, inst.size, _pathScratch, _roadCutRadius);

        t.terrainData = inst;
        RefreshTerrainCollider(t, inst);
        t.Flush();
        _clonedAny = true;

        if (cur != authored && cur != inst)
            Destroy(cur);

        int ti = _resolvedTerrains.IndexOf(t);
        if (ti >= 0)
        {
            int res = inst.heightmapResolution;
            while (_baselines.Count <= ti)
                _baselines.Add(null);
            _baselines[ti] = inst.GetHeights(0, 0, res, res);

            while (_detailBaselines.Count <= ti)
                _detailBaselines.Add(null);
            _detailBaselines[ti] = null;
            CaptureDetailBaseline(t);
        }
    }

    private static void CutDetailsUnderPath(
        TerrainData td, Vector3 terrainPos, Vector3 terrainSize, List<Vector3> path, float radius)
    {
        if (td == null || path == null || path.Count < 2 || radius < 0.1f) return;
        int w = td.detailWidth;
        int h = td.detailHeight;
        int n = td.detailPrototypes != null ? td.detailPrototypes.Length : 0;
        if (w <= 0 || h <= 0 || n <= 0) return;
        if (terrainSize.x < 1e-4f || terrainSize.z < 1e-4f) return;

        float invX = w / terrainSize.x;
        float invZ = h / terrainSize.z;
        float rSq = radius * radius;
        float stamp = Mathf.Max(0.35f, radius * 0.35f);

        for (int layer = 0; layer < n; layer++)
        {
            int[,] map = td.GetDetailLayer(0, 0, w, h, layer);
            bool changed = false;
            for (int i = 0; i < path.Count - 1; i++)
            {
                Vector3 a = path[i];
                Vector3 b = path[i + 1];
                float len = Vector2.Distance(new Vector2(a.x, a.z), new Vector2(b.x, b.z));
                if (len < 0.01f) continue;
                int steps = Mathf.Max(1, Mathf.CeilToInt(len / stamp));
                for (int s = 0; s <= steps; s++)
                {
                    float u = s / (float)steps;
                    float wx = a.x + (b.x - a.x) * u;
                    float wz = a.z + (b.z - a.z) * u;
                    int cx = Mathf.FloorToInt((wx - terrainPos.x) * invX);
                    int cz = Mathf.FloorToInt((wz - terrainPos.z) * invZ);
                    int radX = Mathf.CeilToInt(radius * invX) + 1;
                    int radZ = Mathf.CeilToInt(radius * invZ) + 1;
                    int x0 = Mathf.Max(0, cx - radX);
                    int x1 = Mathf.Min(w - 1, cx + radX);
                    int z0 = Mathf.Max(0, cz - radZ);
                    int z1 = Mathf.Min(h - 1, cz + radZ);
                    if (x1 < 0 || z1 < 0 || x0 >= w || z0 >= h) continue;
                    for (int z = z0; z <= z1; z++)
                    {
                        float worldZ = terrainPos.z + (z + 0.5f) / h * terrainSize.z;
                        float dz = worldZ - wz;
                        for (int x = x0; x <= x1; x++)
                        {
                            if (map[z, x] == 0) continue;
                            float worldX = terrainPos.x + (x + 0.5f) / w * terrainSize.x;
                            float dx = worldX - wx;
                            if (dx * dx + dz * dz > rSq) continue;
                            map[z, x] = 0;
                            changed = true;
                        }
                    }
                }
            }

            if (changed)
                td.SetDetailLayer(0, 0, layer, map);
        }
    }

    private static int[][,] CopyAllDetailLayers(TerrainData td)
    {
        int n = td.detailPrototypes != null ? td.detailPrototypes.Length : 0;
        int w = td.detailWidth;
        int h = td.detailHeight;
        var layers = new int[n][,];
        if (w <= 0 || h <= 0) return layers;
        for (int i = 0; i < n; i++)
        {
            int[,] map = td.GetDetailLayer(0, 0, w, h, i);
            int[,] copy = new int[map.GetLength(0), map.GetLength(1)];
            System.Array.Copy(map, copy, map.Length);
            layers[i] = copy;
        }
        return layers;
    }

    private static bool IsRuntimeClone(TerrainData td)
    {
        return td != null && td.name.IndexOf("RuntimeClone", System.StringComparison.Ordinal) >= 0;
    }

    private void CaptureBaselines()
    {
        _baselines.Clear();
        _detailBaselines.Clear();
        for (int i = 0; i < _resolvedTerrains.Count; i++)
        {
            Terrain t = _resolvedTerrains[i];
            TerrainData td = t.terrainData;
            int w = td.heightmapResolution;
            int h = td.heightmapResolution;
            _baselines.Add(td.GetHeights(0, 0, w, h));
            CaptureDetailBaseline(t);
        }
    }

    private void EnsureBaselineSlot(Terrain t)
    {
        int ti = _resolvedTerrains.IndexOf(t);
        if (ti < 0 || t == null || t.terrainData == null) return;

        while (_baselines.Count <= ti)
            _baselines.Add(null);

        if (_baselines[ti] != null) return;

        TerrainData td = t.terrainData;
        int res = td.heightmapResolution;
        _baselines[ti] = td.GetHeights(0, 0, res, res);
        CaptureDetailBaseline(t);
    }

    private void EnsureBaselinesForSculptScratch()
    {
        while (_baselines.Count < _resolvedTerrains.Count)
            _baselines.Add(null);

        for (int i = 0; i < _sculptScratch.Count; i++)
            EnsureBaselineSlot(_sculptScratch[i]);
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
        // Only generate hills / carve inside this skirt around the road — not the whole tile.
        float sculptMargin = outer + Mathf.Max(5f, hillBandExtraMeters);

        if (randomizeNoiseSeedPerApply)
            _noiseSeed = new Vector2(Random.Range(0f, 9999f), Random.Range(0f, 9999f));
        else
            _noiseSeed = fixedNoiseSeedOffset;

        ComputePathBoundsXZ(_polyScratch, sculptMargin, out float pMinX, out float pMaxX, out float pMinZ, out float pMaxZ);

        Terrain[] sceneTerrains = FindObjectsOfType<Terrain>();
        LastOverlappingTerrainCount = TrackTerrainOverlap.CollectFromPath(
            _pathScratch, sculptMargin, _overlapScratch, sceneTerrains);

        _sculptScratch.Clear();
        if (sculptRoadOverlappingTerrainsOnly)
        {
            // Road tiles only — never generate hills on unused neighbor planes.
            for (int i = 0; i < _overlapScratch.Count; i++)
            {
                Terrain t = _overlapScratch[i];
                if (t != null && !_sculptScratch.Contains(t))
                    _sculptScratch.Add(t);
            }
        }
        else
        {
            if (sceneTerrains != null)
            {
                for (int i = 0; i < sceneTerrains.Length; i++)
                {
                    Terrain t = sceneTerrains[i];
                    if (t != null && t.terrainData != null && t.gameObject.activeInHierarchy)
                        _sculptScratch.Add(t);
                }
            }
        }

        if (_sculptScratch.Count == 0)
        {
            Debug.LogWarning("[TerrainAroundFlatRoad] No terrain planes to sculpt; skip.");
            yield break;
        }

        _roadCutRadius = gen.RoadWidth * 0.5f + extraHalfWidthMeters + 1.5f;

        // Every run: clone authored TerrainData (same as the first Play). Tiles we painted
        // last run but do not sculpt this run still need that reset or leftover blades stay.
        EnsureAllSceneTerrainsPrepared();
        for (int i = 0; i < _sculptScratch.Count; i++)
        {
            Terrain t = _sculptScratch[i];
            if (t != null && !_resolvedTerrains.Contains(t))
                PrepareAndAddTerrain(t);
        }
        for (int i = 0; i < _resolvedTerrains.Count; i++)
        {
            Terrain t = _resolvedTerrains[i];
            if (t == null) continue;
            RecreateRuntimeCloneFromOriginal(t);
            if (spreadWorkAcrossFrames)
                yield return null;
        }

        EnsureBaselinesForSculptScratch();

        _fastNoiseThisApply = useFastNoiseDuringApply;
        LastSculptedTerrainCount = 0;

        for (int si = 0; si < _sculptScratch.Count; si++)
        {
            Terrain terrain = _sculptScratch[si];
            if (terrain == null || terrain.terrainData == null) continue;

            int ti = _resolvedTerrains.IndexOf(terrain);
            if (ti < 0 || ti >= _baselines.Count) continue;

            TerrainData td = terrain.terrainData;
            float[,] baseline = _baselines[ti];
            if (baseline == null) continue;

            Vector3 tp = terrain.transform.position;
            Vector3 ts = td.size;
            bool nearTrack = _overlapScratch.Contains(terrain) ||
                             IntersectsXZRect(tp.x, tp.x + ts.x, tp.z, tp.z + ts.z, pMinX, pMaxX, pMinZ, pMaxZ);

            float hMaxNorm = NormFromWorldY(terrain, roadWorldY - carveBelowRoadMeters);
            float hSeamGuardNorm = NormFromWorldY(terrain, roadWorldY - seamGuardBelowRoadMeters);
            int res = td.heightmapResolution;
            float[,] dst = new float[res, res];
            float lastYieldRealtime = Time.realtimeSinceStartup;
            float effectiveBudgetMs = heightGenFrameBudgetMs;
            int effectiveRowsPerYield = heightmapRowsPerYield;
            if (useFastNoiseDuringApply)
            {
                // Bigger chunks per frame — Finalizing terrain was crawling from tiny yields.
                effectiveBudgetMs = Mathf.Max(effectiveBudgetMs, 16f);
                effectiveRowsPerYield = Mathf.Max(effectiveRowsPerYield, 64);
            }
            float frameBudgetSeconds = Mathf.Max(0.00025f, effectiveBudgetMs * 0.001f);
            float invHeightY = 1f / Mathf.Max(1e-4f, ts.y);

            // Heightmap indices for the road + hill skirt only.
            int xMin = 0, xMax = res - 1, yMin = 0, yMax = res - 1;
            if (nearTrack)
            {
                WorldRectToHeightmapIndices(terrain, td, pMinX, pMaxX, pMinZ, pMaxZ,
                    out xMin, out xMax, out yMin, out yMax);
            }
            else
            {
                // No road on this tile — restore baseline and skip expensive work.
                for (int y = 0; y < res; y++)
                for (int x = 0; x < res; x++)
                    dst[y, x] = baseline[y, x];
                td.SetHeights(0, 0, dst);
                CloseAllHoles(td);
                RefreshTerrainCollider(terrain, td);
                LastSculptedTerrainCount++;
                continue;
            }

            // 1) Cheap: entire tile back to baseline (clears prior full-tile hills).
            for (int y = 0; y < res; y++)
            for (int x = 0; x < res; x++)
                dst[y, x] = baseline[y, x];

            // 2) Expensive noise + road carve ONLY inside the road skirt.
            for (int y = yMin; y <= yMax; y++)
            {
                if (spreadWorkAcrossFrames && y > yMin)
                {
                    bool rowCadenceHit = ((y - yMin) % effectiveRowsPerYield) == 0;
                    bool budgetExceeded = (Time.realtimeSinceStartup - lastYieldRealtime) >= frameBudgetSeconds;
                    if (rowCadenceHit || budgetExceeded)
                    {
                        yield return null;
                        lastYieldRealtime = Time.realtimeSinceStartup;
                    }
                }

                float nz = y / (float)Mathf.Max(1, res - 1);
                float worldZ = tp.z + nz * ts.z;

                for (int x = xMin; x <= xMax; x++)
                {
                    float nx = x / (float)Mathf.Max(1, res - 1);
                    float worldX = tp.x + nx * ts.x;

                    if (worldX < pMinX || worldX > pMaxX || worldZ < pMinZ || worldZ > pMaxZ)
                        continue;

                    float b = baseline[y, x];
                    float noiseM = SampleNoiseMeters(worldX, worldZ);
                    float hill = Mathf.Clamp01(b + noiseM * invHeightY);

                    // Exact centerline distance — never approximate (hills-on-road regression).
                    float d = DistancePointToPolylineXZ(new Vector2(worldX, worldZ), _polyScratch);

                    // Outside carve/blend but still in hill skirt: keep the hills.
                    if (d > outer)
                    {
                        dst[y, x] = hill;
                        continue;
                    }

                    float carved = Mathf.Min(hill, hMaxNorm);
                    float wBlend = Smoother01((d - inner) / Mathf.Max(0.001f, outer - inner));
                    float outH = Mathf.Lerp(carved, hill, wBlend);

                    float guardOuter = inner + seamGuardBandMeters;
                    if (d < guardOuter && seamGuardBandMeters > 1e-4f)
                    {
                        float gT = Smoother01((d - inner) / seamGuardBandMeters);
                        float capped = Mathf.Min(outH, hSeamGuardNorm);
                        outH = Mathf.Lerp(capped, outH, gT);
                    }

                    // Hard guarantee: under the asphalt footprint terrain stays below the road.
                    if (d <= inner)
                        outH = Mathf.Min(outH, hMaxNorm);

                    dst[y, x] = outH;
                }
            }

            // Blur bleeds hills into the corridor. Skip during fast load; otherwise re-carve exactly after.
            int blurPasses = nearTrack ? heightmapBlurPasses : neighborBlurPasses;
            if (useFastNoiseDuringApply)
                blurPasses = 0;

            if (blurPasses > 0)
            {
                for (int pass = 0; pass < blurPasses; pass++)
                {
                    if (spreadWorkAcrossFrames)
                    {
                        yield return null;
                        lastYieldRealtime = Time.realtimeSinceStartup;
                    }

                    BoxBlurHeightmapInPlace(dst, res, heightmapBlurRadiusSamples);
                }

                if (nearTrack)
                {
                    for (int y = yMin; y <= yMax; y++)
                    {
                        if (spreadWorkAcrossFrames && y > yMin && (y % effectiveRowsPerYield) == 0)
                        {
                            yield return null;
                            lastYieldRealtime = Time.realtimeSinceStartup;
                        }

                        float nz = y / (float)Mathf.Max(1, res - 1);
                        float worldZ = tp.z + nz * ts.z;

                        for (int x = xMin; x <= xMax; x++)
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
            }

            td.SetHeights(0, 0, dst);
            CloseAllHoles(td);
            RefreshTerrainCollider(terrain, td);
            LastSculptedTerrainCount++;
        }

        _fastNoiseThisApply = false;
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

    private void CaptureDetailBaseline(Terrain t)
    {
        int ti = _resolvedTerrains.IndexOf(t);
        if (ti < 0 || t == null || t.terrainData == null) return;

        while (_detailBaselines.Count <= ti)
            _detailBaselines.Add(null);
        if (_detailBaselines[ti] != null) return;

        TerrainData td = t.terrainData;
        int w = td.detailWidth;
        int h = td.detailHeight;
        int n = td.detailPrototypes != null ? td.detailPrototypes.Length : 0;
        if (w <= 0 || h <= 0 || n <= 0)
        {
            _detailBaselines[ti] = new int[0][,];
            return;
        }

        var layers = new int[n][,];
        for (int i = 0; i < n; i++)
            layers[i] = td.GetDetailLayer(0, 0, w, h, i);
        _detailBaselines[ti] = layers;
    }

    private void RestoreDetailBaseline(Terrain t)
    {
        int ti = _resolvedTerrains.IndexOf(t);
        if (ti < 0 || ti >= _detailBaselines.Count || t == null || t.terrainData == null) return;

        int[][,] layers = _detailBaselines[ti];
        if (layers == null) return;

        TerrainData td = t.terrainData;
        for (int i = 0; i < layers.Length; i++)
        {
            if (layers[i] == null) continue;
            td.SetDetailLayer(0, 0, i, (int[,])layers[i].Clone());
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

    /// <summary>
    /// Undo any leftover terrain holes (they cut collision as well as grass).
    /// </summary>
    private static void CloseAllHoles(TerrainData td)
    {
        if (td == null) return;
        int res = td.holesResolution;
        if (res <= 1) return;

        bool[,] holes = new bool[res, res];
        for (int y = 0; y < res; y++)
            for (int x = 0; x < res; x++)
                holes[y, x] = true;
        td.SetHoles(0, 0, holes);
    }

    private static void WorldRectToHeightmapIndices(
        Terrain terrain, TerrainData td,
        float minX, float maxX, float minZ, float maxZ,
        out int x0, out int x1, out int y0, out int y1)
    {
        Vector3 tp = terrain.transform.position;
        Vector3 ts = td.size;
        int res = td.heightmapResolution;
        float invX = 1f / Mathf.Max(1e-4f, ts.x);
        float invZ = 1f / Mathf.Max(1e-4f, ts.z);

        x0 = Mathf.Clamp(Mathf.FloorToInt((minX - tp.x) * invX * (res - 1)), 0, res - 1);
        x1 = Mathf.Clamp(Mathf.CeilToInt((maxX - tp.x) * invX * (res - 1)), 0, res - 1);
        // Unity heightmap Y maps to world Z.
        y0 = Mathf.Clamp(Mathf.FloorToInt((minZ - tp.z) * invZ * (res - 1)), 0, res - 1);
        y1 = Mathf.Clamp(Mathf.CeilToInt((maxZ - tp.z) * invZ * (res - 1)), 0, res - 1);

        if (x1 < x0) { int t = x0; x0 = x1; x1 = t; }
        if (y1 < y0) { int t = y0; y0 = y1; y1 = t; }
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
        if (_fastNoiseThisApply || noiseSpatialSmoothingMeters < 1e-4f)
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
