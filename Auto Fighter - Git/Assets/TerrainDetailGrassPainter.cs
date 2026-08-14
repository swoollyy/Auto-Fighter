using System.Collections;
using System.Collections.Generic;
using UnityEngine;

using Random = UnityEngine.Random;

/// <summary>
/// Paints grass beside the road, then cuts the asphalt. Boot-time detail maps (Unity Play,
/// before the first in-game run) are restored at the start of every run so later Plays match run 1.
/// </summary>
[DefaultExecutionOrder(-40)]
[DisallowMultipleComponent]
public sealed class TerrainDetailGrassPainter : MonoBehaviour
{
    [Header("Terrain")]
    [SerializeField] private Terrain targetTerrain;
    [SerializeField] private Terrain[] additionalTerrains;
    [SerializeField] private bool autoDiscoverTerrains = true;

    [Header("Play Mode Safety")]
    [SerializeField] private bool cloneTerrainDataAtRuntime = true;
    [SerializeField] private bool restoreOriginalOnDisable = true;

    [Header("Repaint Strategy")]
    [SerializeField] private bool fillEntireTerrainBeforeCut;
    [SerializeField, Range(0, 64)] private int baselineDensity = 32;
    [SerializeField] private bool stillPaintRingsAfterFill;

    [Header("Detail Layer")]
    [SerializeField, Min(0)] private int detailLayerIndex = 0;

    [Header("Band Painting")]
    [SerializeField, Min(0.5f)] private float bandStepMeters = 3f;
    [SerializeField, Min(1)] private int bandRings = 6;
    [SerializeField, Min(1)] private int maxBandRingsDuringLoad = 6;
    [SerializeField, Min(0.25f)] private float ringSpacingMeters = 2f;
    [SerializeField, Range(0f, 1f)] private float ringJitter = 0.35f;
    [SerializeField, Min(12f)] private float paintChunkMeters = 36f;

    [SerializeField] private bool clearRoadUsingPhysics;
    [SerializeField] private LayerMask roadMask;
    [SerializeField] private float roadClearRayStartHeight = 25f;
    [SerializeField] private float roadClearRayDown = 80f;
    [SerializeField] private float roadClearExtraMeters = 0.5f;

    [Header("Road Clearing")]
    [SerializeField, Min(0f)] private float roadClearPaddingMeters = 2.5f;
    [SerializeField, Min(0f)] private float roadFeatherMeters = 1.5f;

    [Header("Grass Painting")]
    [SerializeField, Min(0f)] private float paintOutwardMeters = 8f;
    [SerializeField, Range(0, 64)] private int grassDensity = 32;
    [SerializeField, Range(0.5f, 10f)] private float sampleStepMeters = 2f;

    [Header("Performance")]
    [SerializeField] private bool paintAsCoroutine = true;
    [SerializeField, Range(10, 500)] private int yieldEverySamples = 80;
    [SerializeField, Min(0.5f)] private float minBandStepDuringLoad = 3.5f;

    [Header("Debug")]
    [SerializeField] private bool debugLogBounds = true;
    [SerializeField] private bool debugStampAtCarOnce;
    [SerializeField] private int debugStampSizeCells = 18;
    [SerializeField] private int debugStampDensity = 16;

    private readonly List<Vector3> _path = new List<Vector3>(2048);
    private readonly List<Terrain> _candidates = new List<Terrain>(16);
    private readonly List<Terrain> _overlap = new List<Terrain>(16);
    private readonly List<Terrain> _clonedTerrains = new List<Terrain>(16);
    private readonly List<TerrainData> _clonedOriginals = new List<TerrainData>(16);
    private readonly Dictionary<int, int[][,]> _bootDetailLayers = new Dictionary<int, int[][,]>();
    private readonly Dictionary<int, DetailPrototype[]> _bootPrototypes = new Dictionary<int, DetailPrototype[]>();

    private int _paintPassCount;
    private TerrainData _td;
    private int _dw, _dh;
    private Vector3 _pos, _size;
    private float _cellX, _cellZ;

    public int LastPaintedTerrainCount { get; private set; }

    private void Awake()
    {
        CaptureBootDetails();
    }

    private void CaptureBootDetails()
    {
        DiscoverTerrains();
        for (int i = 0; i < _candidates.Count; i++)
        {
            Terrain t = _candidates[i];
            if (t == null || t.terrainData == null) continue;
            int id = t.GetInstanceID();
            if (_bootDetailLayers.ContainsKey(id)) continue;
            TerrainData td = t.terrainData;
            if (td.name.IndexOf("RuntimeClone", System.StringComparison.Ordinal) >= 0)
                continue;
            _bootPrototypes[id] = td.detailPrototypes;
            int n = td.detailPrototypes != null ? td.detailPrototypes.Length : 0;
            int w = td.detailWidth;
            int h = td.detailHeight;
            var layers = new int[n][,];
            if (w > 0 && h > 0)
            {
                for (int li = 0; li < n; li++)
                {
                    int[,] map = td.GetDetailLayer(0, 0, w, h, li);
                    int[,] copy = new int[map.GetLength(0), map.GetLength(1)];
                    System.Array.Copy(map, copy, map.Length);
                    layers[li] = copy;
                }
            }
            _bootDetailLayers[id] = layers;
        }
    }

    private void RestoreBootDetails(Terrain terrain)
    {
        if (terrain == null || terrain.terrainData == null) return;
        int id = terrain.GetInstanceID();
        int[][,] layers;
        if (!_bootDetailLayers.TryGetValue(id, out layers) || layers == null) return;

        TerrainData td = terrain.terrainData;
        DetailPrototype[] protos;
        if (_bootPrototypes.TryGetValue(id, out protos) && protos != null)
            td.detailPrototypes = protos;

        for (int i = 0; i < layers.Length; i++)
        {
            if (layers[i] == null) continue;
            int[,] copy = new int[layers[i].GetLength(0), layers[i].GetLength(1)];
            System.Array.Copy(layers[i], copy, layers[i].Length);
            td.SetDetailLayer(0, 0, i, copy);
        }
        terrain.Flush();
    }

    private void OnDisable()
    {
        if (!restoreOriginalOnDisable || !Application.isPlaying) return;
        for (int i = 0; i < _clonedTerrains.Count; i++)
        {
            Terrain t = _clonedTerrains[i];
            if (t == null || i >= _clonedOriginals.Count) continue;
            TerrainData orig = _clonedOriginals[i];
            TerrainData cur = t.terrainData;
            if (orig != null) t.terrainData = orig;
            if (cur != null && cur != orig) Destroy(cur);
        }
        _clonedTerrains.Clear();
        _clonedOriginals.Clear();
    }

    public void PaintNow(ProceduralTrackGenerator gen)
    {
        if (paintAsCoroutine) StartCoroutine(CoPaint(gen));
        else
        {
            IEnumerator e = CoPaint(gen);
            while (e.MoveNext()) { }
        }
    }

    public IEnumerator CoPaint(ProceduralTrackGenerator gen)
    {
        LastPaintedTerrainCount = 0;
        if (gen == null) yield break;

        gen.FillRoadMeshCenterPath(_path);
        if (_path.Count < 2)
        {
            if (debugLogBounds)
                Debug.LogWarning("[TerrainDetailGrassPainter] No centerline; skip.");
            yield break;
        }

        float roadHalf = Mathf.Max(0.01f, gen.RoadWidth * 0.5f);
        float clearRadius = roadHalf + roadClearPaddingMeters + roadClearExtraMeters + Mathf.Max(0f, roadFeatherMeters);
        float edgeClearRadius = Mathf.Max(2f, roadClearPaddingMeters) + roadClearExtraMeters + 1.5f;
        int rings = Mathf.Max(1, Mathf.Min(bandRings, maxBandRingsDuringLoad));
        if (paintOutwardMeters > 0.5f)
            rings = Mathf.Min(rings, Mathf.Max(1, Mathf.CeilToInt(paintOutwardMeters / Mathf.Max(0.25f, ringSpacingMeters))));
        float step = Mathf.Max(bandStepMeters, minBandStepDuringLoad, sampleStepMeters);
        float chunkLen = Mathf.Max(24f, paintChunkMeters);
        int density = Mathf.Max(1, grassDensity);

        DiscoverTerrains();
        CaptureBootDetails();
        float margin = clearRadius + rings * ringSpacingMeters + 8f;
        TrackTerrainOverlap.CollectFromPath(_path, margin, _overlap, _candidates.ToArray());
        if (_overlap.Count == 0 && Terrain.activeTerrain != null)
            _overlap.Add(Terrain.activeTerrain);
        _overlap.RemoveAll(t => t == null || t.terrainData == null || !t.gameObject.activeInHierarchy);

        if (_overlap.Count == 0)
        {
            if (debugLogBounds)
                Debug.LogWarning("[TerrainDetailGrassPainter] No terrains to paint.");
            yield break;
        }

        if (debugLogBounds)
            Debug.Log($"[TerrainDetailGrassPainter] Paint terrains={_overlap.Count} clearRadius={clearRadius:0.0}m rings={rings} step={step:0.0}m");

        bool restoreBoot = _paintPassCount > 0;
        _paintPassCount++;

        for (int ti = 0; ti < _overlap.Count; ti++)
        {
            Terrain terrain = _overlap[ti];
            CloneOnceIfNeeded(terrain);
            if (restoreBoot)
                RestoreBootDetails(terrain);
            if (!Bind(terrain)) continue;

            IEnumerator paint = CoPaintChunks(rings, step, chunkLen, density, clearRadius);
            while (paint.MoveNext()) yield return paint.Current;

            IEnumerator cut = CoCutChunks(roadHalf, clearRadius, edgeClearRadius, chunkLen);
            while (cut.MoveNext()) yield return cut.Current;

            terrain.drawTreesAndFoliage = true;
            if (terrain.detailObjectDistance < 40f) terrain.detailObjectDistance = 80f;
            if (terrain.detailObjectDensity < 0.2f) terrain.detailObjectDensity = 1f;
            terrain.Flush();
            LastPaintedTerrainCount++;
            yield return null;
        }
    }

    private IEnumerator CoPaintChunks(int rings, float step, float chunkLen, int density, float clearRadius)
    {
        const int dab = 1;
        int segStart = 0;
        while (segStart < _path.Count - 1)
        {
            float used = 0f;
            int segEnd = segStart;
            while (segEnd < _path.Count - 1 && used < chunkLen)
            {
                used += Vector3.Distance(_path[segEnd], _path[segEnd + 1]);
                segEnd++;
            }
            if (segEnd <= segStart) segEnd = segStart + 1;

            float pad = clearRadius + rings * ringSpacingMeters + 4f;
            if (TryChunkRect(segStart, segEnd, pad, out int x0, out int y0, out int w, out int h))
            {
                int[,] map = _td.GetDetailLayer(x0, y0, w, h, detailLayerIndex);
                bool dirty = false;
                for (int i = segStart; i < segEnd; i++)
                {
                    Vector3 a = _path[i];
                    Vector3 b = _path[i + 1];
                    float len = Vector3.Distance(a, b);
                    if (len < 0.01f) continue;
                    int steps = Mathf.Max(1, Mathf.CeilToInt(len / step));
                    Vector3 fwd = b - a;
                    fwd.y = 0f;
                    if (fwd.sqrMagnitude < 0.0001f) fwd = Vector3.forward;
                    fwd.Normalize();
                    Vector3 right = Vector3.Cross(Vector3.up, fwd).normalized;

                    for (int s = 0; s <= steps; s++)
                    {
                        Vector3 p = Vector3.Lerp(a, b, s / (float)steps);
                        for (int ring = 1; ring <= rings; ring++)
                        {
                            float off = clearRadius + ring * ringSpacingMeters
                                + (Random.value - 0.5f) * 2f * ringJitter * ringSpacingMeters;
                            dirty |= StampDensity(map, x0, y0, w, h, p - right * off, density, dab);
                            dirty |= StampDensity(map, x0, y0, w, h, p + right * off, density, dab);
                        }
                    }
                }
                if (dirty)
                    _td.SetDetailLayer(x0, y0, detailLayerIndex, map);
            }

            segStart = segEnd;
            yield return null;
        }
    }

    private IEnumerator CoCutChunks(float roadHalf, float centerRadius, float edgeRadius, float chunkLen)
    {
        int layers = _td.detailPrototypes != null ? _td.detailPrototypes.Length : 0;
        if (layers <= 0) yield break;

        float stampStep = Mathf.Max(_cellX, _cellZ, 1.25f);
        int radC = Mathf.Max(1, Mathf.CeilToInt(centerRadius / Mathf.Max(0.0001f, Mathf.Min(_cellX, _cellZ))));
        int radE = Mathf.Max(1, Mathf.CeilToInt(edgeRadius / Mathf.Max(0.0001f, Mathf.Min(_cellX, _cellZ))));
        float centerSq = centerRadius * centerRadius;
        float edgeSq = edgeRadius * edgeRadius;

        int segStart = 0;
        while (segStart < _path.Count - 1)
        {
            float used = 0f;
            int segEnd = segStart;
            while (segEnd < _path.Count - 1 && used < chunkLen)
            {
                used += Vector3.Distance(_path[segEnd], _path[segEnd + 1]);
                segEnd++;
            }
            if (segEnd <= segStart) segEnd = segStart + 1;

            if (TryChunkRect(segStart, segEnd, centerRadius + 3f, out int x0, out int y0, out int w, out int h))
            {
                for (int layer = 0; layer < layers; layer++)
                {
                    int[,] map = _td.GetDetailLayer(x0, y0, w, h, layer);
                    bool dirty = false;
                    for (int i = segStart; i < segEnd; i++)
                    {
                        Vector3 a = _path[i];
                        Vector3 b = _path[i + 1];
                        float len = Vector3.Distance(a, b);
                        if (len < 0.01f) continue;
                        int steps = Mathf.Max(1, Mathf.CeilToInt(len / stampStep));
                        for (int s = 0; s <= steps; s++)
                        {
                            float t = s / (float)steps;
                            Vector3 p = Vector3.Lerp(a, b, t);
                            int idx = t < 0.5f ? i : Mathf.Min(i + 1, _path.Count - 1);
                            Vector3 fwd = TrackPathSampling.ComputeMiteredForward(_path, idx);
                            Vector3 right = Vector3.Cross(Vector3.up, fwd);
                            right.y = 0f;
                            if (right.sqrMagnitude < 1e-8f) right = Vector3.right;
                            right.Normalize();

                            dirty |= ZeroCircle(map, x0, y0, w, h, p, radC, centerSq);
                            dirty |= ZeroCircle(map, x0, y0, w, h, p - right * roadHalf, radE, edgeSq);
                            dirty |= ZeroCircle(map, x0, y0, w, h, p + right * roadHalf, radE, edgeSq);
                        }
                    }
                    if (dirty)
                        _td.SetDetailLayer(x0, y0, layer, map);
                }
            }

            segStart = segEnd;
            yield return null;
        }
    }

    private bool StampDensity(int[,] map, int x0, int y0, int w, int h, Vector3 world, int density, int dab)
    {
        if (!TryWorldToCell(world, out int cx, out int cy)) return false;
        bool dirty = false;
        int xStart = Mathf.Max(x0, cx - dab);
        int yStart = Mathf.Max(y0, cy - dab);
        int xEnd = Mathf.Min(x0 + w - 1, cx + dab);
        int yEnd = Mathf.Min(y0 + h - 1, cy + dab);
        for (int cyi = yStart; cyi <= yEnd; cyi++)
        {
            int ly = cyi - y0;
            for (int cxi = xStart; cxi <= xEnd; cxi++)
            {
                int lx = cxi - x0;
                if (map[ly, lx] < density)
                {
                    map[ly, lx] = density;
                    dirty = true;
                }
            }
        }
        return dirty;
    }

    private bool ZeroCircle(int[,] map, int x0, int y0, int w, int h, Vector3 world, int rad, float radiusSq)
    {
        if (!TryWorldToCell(world, out int cx, out int cy)) return false;
        bool dirty = false;
        int xStart = Mathf.Max(x0, cx - rad);
        int yStart = Mathf.Max(y0, cy - rad);
        int xEnd = Mathf.Min(x0 + w - 1, cx + rad);
        int yEnd = Mathf.Min(y0 + h - 1, cy + rad);
        for (int cyi = yStart; cyi <= yEnd; cyi++)
        {
            int ly = cyi - y0;
            float dz = (cyi - cy) * _cellZ;
            float dzSq = dz * dz;
            for (int cxi = xStart; cxi <= xEnd; cxi++)
            {
                int lx = cxi - x0;
                if (map[ly, lx] == 0) continue;
                float dx = (cxi - cx) * _cellX;
                if (dx * dx + dzSq > radiusSq) continue;
                map[ly, lx] = 0;
                dirty = true;
            }
        }
        return dirty;
    }

    private bool TryChunkRect(int segStart, int segEnd, float pad, out int x0, out int y0, out int w, out int h)
    {
        x0 = y0 = w = h = 0;
        float minX = float.MaxValue, maxX = float.MinValue;
        float minZ = float.MaxValue, maxZ = float.MinValue;
        for (int i = segStart; i <= segEnd && i < _path.Count; i++)
        {
            Vector3 p = _path[i];
            if (p.x < minX) minX = p.x;
            if (p.x > maxX) maxX = p.x;
            if (p.z < minZ) minZ = p.z;
            if (p.z > maxZ) maxZ = p.z;
        }
        float invX = 1f / Mathf.Max(0.0001f, _size.x);
        float invZ = 1f / Mathf.Max(0.0001f, _size.z);
        int xa = Mathf.FloorToInt((minX - pad - _pos.x) * invX * (_dw - 1));
        int xb = Mathf.CeilToInt((maxX + pad - _pos.x) * invX * (_dw - 1));
        int ya = Mathf.FloorToInt((minZ - pad - _pos.z) * invZ * (_dh - 1));
        int yb = Mathf.CeilToInt((maxZ + pad - _pos.z) * invZ * (_dh - 1));
        xa = Mathf.Clamp(xa, 0, _dw - 1);
        xb = Mathf.Clamp(xb, 0, _dw - 1);
        ya = Mathf.Clamp(ya, 0, _dh - 1);
        yb = Mathf.Clamp(yb, 0, _dh - 1);
        if (xb < xa) { int t = xa; xa = xb; xb = t; }
        if (yb < ya) { int t = ya; ya = yb; yb = t; }
        x0 = xa;
        y0 = ya;
        w = xb - xa + 1;
        h = yb - ya + 1;
        return w > 0 && h > 0;
    }

    private bool TryWorldToCell(Vector3 world, out int cx, out int cy)
    {
        Vector3 local = world - _pos;
        float nx = local.x / Mathf.Max(0.0001f, _size.x);
        float nz = local.z / Mathf.Max(0.0001f, _size.z);
        cx = cy = 0;
        if (nx < 0f || nx > 1f || nz < 0f || nz > 1f) return false;
        cx = Mathf.Clamp(Mathf.RoundToInt(nx * (_dw - 1)), 0, _dw - 1);
        cy = Mathf.Clamp(Mathf.RoundToInt(nz * (_dh - 1)), 0, _dh - 1);
        return true;
    }

    private void DiscoverTerrains()
    {
        _candidates.Clear();
        if (targetTerrain != null) _candidates.Add(targetTerrain);
        if (additionalTerrains != null)
        {
            for (int i = 0; i < additionalTerrains.Length; i++)
                if (additionalTerrains[i] != null && !_candidates.Contains(additionalTerrains[i]))
                    _candidates.Add(additionalTerrains[i]);
        }
        if (autoDiscoverTerrains || _candidates.Count == 0)
        {
            Terrain[] found = FindObjectsOfType<Terrain>();
            for (int i = 0; i < found.Length; i++)
            {
                Terrain t = found[i];
                if (t == null || t.terrainData == null || !t.gameObject.activeInHierarchy) continue;
                if (!_candidates.Contains(t)) _candidates.Add(t);
            }
        }
    }

    private void CloneOnceIfNeeded(Terrain terrain)
    {
        if (!Application.isPlaying || !cloneTerrainDataAtRuntime || terrain == null || terrain.terrainData == null)
            return;
        if (terrain.terrainData.name.IndexOf("RuntimeClone", System.StringComparison.Ordinal) >= 0)
            return;
        if (_clonedTerrains.Contains(terrain)) return;

        TerrainData orig = terrain.terrainData;
        TerrainData clone = Instantiate(orig);
        clone.name = orig.name + " (RuntimeClone)";
        terrain.terrainData = clone;
        TerrainCollider tc = terrain.GetComponent<TerrainCollider>();
        if (tc != null) tc.terrainData = clone;
        _clonedTerrains.Add(terrain);
        _clonedOriginals.Add(orig);
    }

    private bool Bind(Terrain terrain)
    {
        if (terrain == null || terrain.terrainData == null) return false;
        _td = terrain.terrainData;
        _dw = _td.detailWidth;
        _dh = _td.detailHeight;
        _pos = terrain.transform.position;
        _size = _td.size;
        if (_dw <= 1 || _dh <= 1) return false;
        if (_td.detailPrototypes == null || detailLayerIndex >= _td.detailPrototypes.Length)
            return false;
        _cellX = _size.x / Mathf.Max(1, _dw);
        _cellZ = _size.z / Mathf.Max(1, _dh);
        return true;
    }
}
