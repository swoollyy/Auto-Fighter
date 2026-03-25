using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

using Random = UnityEngine.Random;

[DisallowMultipleComponent]
public sealed class TerrainDetailGrassPainter : MonoBehaviour
{
    [Header("Terrain")]
    [SerializeField] private Terrain targetTerrain;

    [Header("Play Mode Safety")]
    [SerializeField] private bool cloneTerrainDataAtRuntime = true;
    [SerializeField] private bool restoreOriginalOnDisable = true;

    private TerrainData _originalTd;
    private bool _clonedThisSession;

    [Header("Repaint Strategy")]
    [Tooltip("If true, we first fill the entire terrain detail layer to a baseline value, then cut the road out and optionally add ring paint.")]
    [SerializeField] private bool fillEntireTerrainBeforeCut = true;

    [Tooltip("Baseline density used when fillEntireTerrainBeforeCut is true. Higher = thicker grass off-road (runtime cost increases).")]
    [SerializeField, Range(0, 64)] private int baselineDensity = 32;

    [Tooltip("If fillEntireTerrainBeforeCut is true, set this false to skip Phase 2 rings (since the world is already grass).")]
    [SerializeField] private bool stillPaintRingsAfterFill = false;

    [Header("Detail Layer")]
    [Tooltip("Which Terrain Detail layer index to paint (the grass-blade detail prototype index).")]
    [SerializeField, Min(0)] private int detailLayerIndex = 0;

    [Header("Band Painting (Recommended)")]
    [SerializeField, Min(0.5f)] private float bandStepMeters = 3f;
    [SerializeField, Min(1)] private int bandRings = 10;
    [SerializeField, Min(0.25f)] private float ringSpacingMeters = 2.0f;
    [SerializeField, Range(0f, 1f)] private float ringJitter = 0.35f;

    [SerializeField] private bool clearRoadUsingPhysics = true;
    [SerializeField] private LayerMask roadMask; // set to your Road layer
    [SerializeField] private float roadClearRayStartHeight = 25f;
    [SerializeField] private float roadClearRayDown = 80f;
    [SerializeField] private float roadClearExtraMeters = 0.75f;


    [Header("Road Clearing")]
    [Tooltip("Extra meters beyond road width to keep completely clear of grass details.")]
    [SerializeField, Min(0f)] private float roadClearPaddingMeters = 1.5f;

    [Tooltip("Additional feather band in meters where we fade down to zero (looks less 'cut out').")]
    [SerializeField, Min(0f)] private float roadFeatherMeters = 1.5f;

    [Header("Grass Painting")]
    [Tooltip("How far from the road edge we paint grass (meters).")]
    [SerializeField, Min(0f)] private float paintOutwardMeters = 30f;

    [Tooltip("Detail density for Phase 2 ring painting (when used). Unity accepts high values; more instances = higher GPU cost when drawing grass.")]
    [SerializeField, Range(0, 64)] private int grassDensity = 32;

    [Tooltip("How often we sample along the track to stamp (meters). Lower = more accurate, slower.")]
    [SerializeField, Range(0.5f, 10f)] private float sampleStepMeters = 2f;

    [Header("Performance")]
    [Tooltip("If true, yields periodically while painting to avoid a big spike.")]
    [SerializeField] private bool paintAsCoroutine = true;

    [Tooltip("How many samples before yielding one frame.")]
    [SerializeField, Range(10, 500)] private int yieldEverySamples = 80;

    [Header("Debug")]
    [SerializeField] private bool debugLogBounds = true;
    [SerializeField] private bool debugStampAtCarOnce = true;
    [SerializeField] private int debugStampSizeCells = 18;
    [SerializeField] private int debugStampDensity = 16;

    private bool _didDebugStamp;

    private readonly List<Vector3> _centerPathScratch = new List<Vector3>(2048);

    private TerrainData _td;
    private int _detailW;
    private int _detailH;
    private Vector3 _terrainPos;
    private Vector3 _terrainSize;


    private void Awake()
    {
        if (targetTerrain == null) targetTerrain = Terrain.activeTerrain;
        if (targetTerrain == null) return;

        if (Application.isPlaying && cloneTerrainDataAtRuntime)
        {
            _originalTd = targetTerrain.terrainData;
            if (_originalTd != null)
            {
                TerrainData clone = Instantiate(_originalTd);
                clone.name = _originalTd.name + " (RuntimeClone)";
                targetTerrain.terrainData = clone;
                _clonedThisSession = true;
            }
        }
    }

    private void OnDisable()
    {
        if (!restoreOriginalOnDisable) return;
        if (!Application.isPlaying) return;

        if (_clonedThisSession && targetTerrain != null && _originalTd != null)
        {
            targetTerrain.terrainData = _originalTd;
            _clonedThisSession = false;
        }
    }


    public void PaintNow(ProceduralTrackGenerator gen)
    {
        if (paintAsCoroutine) StartCoroutine(CoPaint(gen));
        else PaintInternal(gen, allowYield: false);
    }

    public IEnumerator CoPaint(ProceduralTrackGenerator gen)
    {
        yield return PaintInternal(gen, allowYield: true);
    }

    private IEnumerator PaintInternal(ProceduralTrackGenerator gen, bool allowYield)
    {
        if (gen == null) yield break;

        gen.FillRoadMeshCenterPath(_centerPathScratch);
        if (_centerPathScratch.Count < 2)
        {
            if (debugLogBounds)
                Debug.LogWarning("[TerrainDetailGrassPainter] No road centerline path; skip paint.");
            yield break;
        }

        if (targetTerrain == null) targetTerrain = Terrain.activeTerrain;
        if (targetTerrain == null) yield break;

        _td = targetTerrain.terrainData;
        if (_td == null) yield break;

        _detailW = _td.detailWidth;
        _detailH = _td.detailHeight;

        _terrainPos = targetTerrain.transform.position;
        _terrainSize = _td.size;

        if (fillEntireTerrainBeforeCut)
        {
            if (debugLogBounds)
                Debug.Log("[TerrainDetailGrassPainter] Filling entire detail layer baseline...");

            FillEntireLayer(detailLayerIndex, baselineDensity);

            if (allowYield) yield return null;
        }

        _debugRayHits = 0;
        _debugRayMisses = 0;

        Physics.SyncTransforms();
        if (allowYield)
        {
            // During loading we can intentionally keep Time.timeScale at 0.
            // WaitForFixedUpdate would stall forever in that case.
            if (Time.timeScale <= 0f)
                yield return null;
            else
                yield return new WaitForFixedUpdate();
        }

        // Road width info
        float roadHalf = Mathf.Max(0.01f, gen.RoadWidth * 0.5f);
        float clearRadius = roadHalf + roadClearPaddingMeters + roadClearExtraMeters;

        int samples = 0;

        // ============================================================
        // PHASE 1: CLEAR grass along the entire road path first
        // ============================================================
        if (debugLogBounds)
            Debug.Log("[TerrainDetailGrassPainter] Phase 1: Clearing grass along road...");

        for (int i = 0; i < _centerPathScratch.Count - 1; i++)
        {
            Vector3 a = _centerPathScratch[i];
            Vector3 b = _centerPathScratch[i + 1];

            float segLen = Vector3.Distance(a, b);
            if (segLen < 0.01f) continue;

            // Use smaller steps for clearing to ensure full coverage
            float clearStepMeters = 1.0f;
            int steps = Mathf.Max(1, Mathf.CeilToInt(segLen / clearStepMeters));

            for (int s = 0; s <= steps; s++)
            {
                float t = (steps == 0) ? 0f : (s / (float)steps);
                Vector3 p = Vector3.Lerp(a, b, t);

                // Clear a wide swath centered on the road
                ClearGrassAtPoint(p, clearRadius);

                samples++;
                if (allowYield && yieldEverySamples > 0 && (samples % yieldEverySamples) == 0)
                    yield return null;
            }
        }

        if (debugLogBounds)
            Debug.Log($"[TerrainDetailGrassPainter] Phase 1 complete: cleared {samples} points");

        // ============================================================
        // PHASE 2: Paint grass in rings OUTSIDE the road
        // ============================================================


        if(!fillEntireTerrainBeforeCut || stillPaintRingsAfterFill)
        {
            if (debugLogBounds)
                Debug.Log("[TerrainDetailGrassPainter] Phase 2: Painting grass beside road...");

            samples = 0;

            for (int i = 0; i < _centerPathScratch.Count - 1; i++)
            {
                Vector3 a = _centerPathScratch[i];
                Vector3 b = _centerPathScratch[i + 1];

                float segLen = Vector3.Distance(a, b);
                if (segLen < 0.01f) continue;

                int steps = Mathf.Max(1, Mathf.CeilToInt(segLen / Mathf.Max(0.25f, bandStepMeters)));
                for (int s = 0; s <= steps; s++)
                {
                    float t = (steps == 0) ? 0f : (s / (float)steps);
                    Vector3 p = Vector3.Lerp(a, b, t);

                    Vector3 fwd = (b - a);
                    fwd.y = 0f;
                    if (fwd.sqrMagnitude < 0.0001f) fwd = Vector3.forward;
                    fwd.Normalize();

                    Vector3 right = Vector3.Cross(Vector3.up, fwd).normalized;

                    // Paint outward rings on BOTH sides, starting OUTSIDE the clear zone
                    for (int ring = 1; ring <= bandRings; ring++)
                    {
                        float baseOff = clearRadius + ring * ringSpacingMeters;
                        float jitter = (Random.value - 0.5f) * 2f * ringJitter * ringSpacingMeters;
                        float off = baseOff + jitter;

                        Vector3 leftPos = p - right * off;
                        Vector3 rightPos = p + right * off;

                        PaintPoint(leftPos, grassDensity);
                        PaintPoint(rightPos, grassDensity);
                    }

                    samples++;
                    if (allowYield && yieldEverySamples > 0 && (samples % yieldEverySamples) == 0)
                        yield return null;
                }
            }

        }


        if (debugLogBounds)
        {
            Debug.Log($"[TerrainDetailGrassPainter] Phase 2 complete: samples={samples} detailSize={_detailW}x{_detailH}");
            Debug.Log($"[TerrainDetailGrassPainter] Road raycast hits={_debugRayHits}, misses={_debugRayMisses}, roadMask={roadMask.value}");
        }
        _debugRayHits = 0;
        _debugRayMisses = 0;

        targetTerrain.Flush();


    }

    private int _debugRayHits = 0;
    private int _debugRayMisses = 0;

    private void PaintPoint(Vector3 world, int density)
    {
        Vector2 norm = WorldToTerrainNorm(world);
        if (norm.x < 0f || norm.x > 1f || norm.y < 0f || norm.y > 1f) return;

        // If we can detect road under this point, do NOT paint.
        if (clearRoadUsingPhysics && roadMask.value != 0)
        {
            Vector3 origin = new Vector3(world.x, _terrainPos.y + _terrainSize.y + roadClearRayStartHeight, world.z);
            float maxRay = _terrainSize.y + roadClearRayDown;

            if (Physics.Raycast(origin, Vector3.down, out RaycastHit hit, maxRay, roadMask, QueryTriggerInteraction.Ignore))
            {
                _debugRayHits++;
                // road collider under this point => don't place grass
                return;
            }
            else
            {
                _debugRayMisses++;
            }
        }

        int cx = Mathf.RoundToInt(norm.x * (_detailW - 1));
        int cy = Mathf.RoundToInt(norm.y * (_detailH - 1));

        // Dab size in cells (small square)
        const int dab = 2;
        int x0 = Mathf.Clamp(cx - dab, 0, _detailW - 1);
        int y0 = Mathf.Clamp(cy - dab, 0, _detailH - 1);
        int w = Mathf.Clamp(cx + dab, 0, _detailW - 1) - x0 + 1;
        int h = Mathf.Clamp(cy + dab, 0, _detailH - 1) - y0 + 1;

        int[,] layer = _td.GetDetailLayer(x0, y0, w, h, detailLayerIndex);
        for (int yy = 0; yy < h; yy++)
            for (int xx = 0; xx < w; xx++)
                layer[yy, xx] = Mathf.Max(layer[yy, xx], density);

        _td.SetDetailLayer(x0, y0, detailLayerIndex, layer);
    }

    private void ClearGrassAtPoint(Vector3 world, float clearRadiusMeters)
    {
        Vector2 norm = WorldToTerrainNorm(world);
        if (norm.x < 0f || norm.x > 1f || norm.y < 0f || norm.y > 1f) return;

        float metersPerCellX = _terrainSize.x / Mathf.Max(1, _detailW);
        float metersPerCellZ = _terrainSize.z / Mathf.Max(1, _detailH);

        int cx = Mathf.RoundToInt(norm.x * (_detailW - 1));
        int cy = Mathf.RoundToInt(norm.y * (_detailH - 1));

        int radCellsX = Mathf.CeilToInt(clearRadiusMeters / Mathf.Max(0.0001f, metersPerCellX));
        int radCellsY = Mathf.CeilToInt(clearRadiusMeters / Mathf.Max(0.0001f, metersPerCellZ));

        int x0 = Mathf.Clamp(cx - radCellsX, 0, _detailW - 1);
        int x1 = Mathf.Clamp(cx + radCellsX, 0, _detailW - 1);
        int y0 = Mathf.Clamp(cy - radCellsY, 0, _detailH - 1);
        int y1 = Mathf.Clamp(cy + radCellsY, 0, _detailH - 1);

        int w = x1 - x0 + 1;
        int h = y1 - y0 + 1;

        if (w <= 0 || h <= 0) return;

        int[,] layer = _td.GetDetailLayer(x0, y0, w, h, detailLayerIndex);

        for (int yy = 0; yy < h; yy++)
        {
            for (int xx = 0; xx < w; xx++)
            {
                int dx = (x0 + xx) - cx;
                int dy = (y0 + yy) - cy;

                float distMeters = Mathf.Sqrt(
                    (dx * metersPerCellX) * (dx * metersPerCellX) +
                    (dy * metersPerCellZ) * (dy * metersPerCellZ)
                );

                // Clear grass within the radius
                if (distMeters <= clearRadiusMeters)
                {
                    layer[yy, xx] = 0;
                }
            }
        }

        _td.SetDetailLayer(x0, y0, detailLayerIndex, layer);
    }

    private void FillEntireLayer(int layerIndex, int density)
    {
        int[,] full = new int[_detailH, _detailW];
        for (int y = 0; y < _detailH; y++)
            for (int x = 0; x < _detailW; x++)
                full[y, x] = density;

        _td.SetDetailLayer(0, 0, layerIndex, full);
    }


    private void StampAtWorld(Vector3 world, float clearR, float featherR, float paintR)
    {
        // Convert world -> normalized terrain local (0..1)
        Vector2 norm = WorldToTerrainNorm(world);
        if (norm.x < 0f || norm.x > 1f || norm.y < 0f || norm.y > 1f) return;

        // Convert meters to detail cells
        float metersPerCellX = _terrainSize.x / Mathf.Max(1, _detailW);
        float metersPerCellZ = _terrainSize.z / Mathf.Max(1, _detailH);

        int cx = Mathf.RoundToInt(norm.x * (_detailW - 1));
        int cy = Mathf.RoundToInt(norm.y * (_detailH - 1));

        int radCellsX = Mathf.CeilToInt(paintR / Mathf.Max(0.0001f, metersPerCellX));
        int radCellsY = Mathf.CeilToInt(paintR / Mathf.Max(0.0001f, metersPerCellZ));

        int x0 = Mathf.Clamp(cx - radCellsX, 0, _detailW - 1);
        int x1 = Mathf.Clamp(cx + radCellsX, 0, _detailW - 1);
        int y0 = Mathf.Clamp(cy - radCellsY, 0, _detailH - 1);
        int y1 = Mathf.Clamp(cy + radCellsY, 0, _detailH - 1);

        int w = x1 - x0 + 1;
        int h = y1 - y0 + 1;

        // Pull region, modify, push back
        int[,] layer = _td.GetDetailLayer(x0, y0, w, h, detailLayerIndex);

        for (int yy = 0; yy < h; yy++)
        {
            for (int xx = 0; xx < w; xx++)
            {
                int dx = (x0 + xx) - cx;
                int dy = (y0 + yy) - cy;

                float distMeters =
                    Mathf.Sqrt((dx * metersPerCellX) * (dx * metersPerCellX) +
                               (dy * metersPerCellZ) * (dy * metersPerCellZ));

                // Inside clear: force 0
                if (distMeters <= clearR)
                {
                    layer[yy, xx] = 0;
                    continue;
                }

                // Between clear and feather: fade up from 0 to density
                if (distMeters <= featherR)
                {
                    float u = Mathf.InverseLerp(clearR, featherR, distMeters);
                    int target = Mathf.RoundToInt(grassDensity * u);
                    if (target > layer[yy, xx]) layer[yy, xx] = target;
                    continue;
                }

                // Between feather and paint radius: set full density
                if (distMeters <= paintR)
                {
                    if (grassDensity > layer[yy, xx]) layer[yy, xx] = grassDensity;
                }
            }
        }

        _td.SetDetailLayer(x0, y0, detailLayerIndex, layer);
    }

    private Vector2 WorldToTerrainNorm(Vector3 world)
    {
        Vector3 local = world - _terrainPos;
        float nx = local.x / Mathf.Max(0.0001f, _terrainSize.x);
        float nz = local.z / Mathf.Max(0.0001f, _terrainSize.z);
        return new Vector2(nx, nz);
    }

    private void DebugStampSquareAtWorld(Vector3 world)
    {
        if (_td == null) return;

        Vector2 norm = WorldToTerrainNorm(world);
        if (debugLogBounds)
            Debug.Log($"[GrassPainter] DebugStamp world={world} norm={norm} terrainPos={_terrainPos} size={_terrainSize}");

        if (norm.x < 0f || norm.x > 1f || norm.y < 0f || norm.y > 1f)
        {
            Debug.LogWarning("[GrassPainter] Debug stamp point OUTSIDE terrain bounds -> nothing will paint.");
            return;
        }

        int cx = Mathf.RoundToInt(norm.x * (_detailW - 1));
        int cy = Mathf.RoundToInt(norm.y * (_detailH - 1));

        int half = Mathf.Max(1, debugStampSizeCells / 2);
        int x0 = Mathf.Clamp(cx - half, 0, _detailW - 1);
        int y0 = Mathf.Clamp(cy - half, 0, _detailH - 1);
        int w = Mathf.Clamp(cx + half, 0, _detailW - 1) - x0 + 1;
        int h = Mathf.Clamp(cy + half, 0, _detailH - 1) - y0 + 1;

        int[,] layer = _td.GetDetailLayer(x0, y0, w, h, detailLayerIndex);

        for (int yy = 0; yy < h; yy++)
            for (int xx = 0; xx < w; xx++)
                layer[yy, xx] = Mathf.Max(layer[yy, xx], debugStampDensity);

        _td.SetDetailLayer(x0, y0, detailLayerIndex, layer);

        Debug.Log($"[GrassPainter] DebugStamp applied at detail ({cx},{cy}) size={w}x{h} density={debugStampDensity} layer={detailLayerIndex}");
    }

}
