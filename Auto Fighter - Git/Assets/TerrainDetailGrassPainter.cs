using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class TerrainDetailGrassPainter : MonoBehaviour
{
    [Header("Terrain")]
    [SerializeField] private Terrain targetTerrain;

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

    [Tooltip("Detail density written into the terrain detailmap (0..16-ish typical, but Unity allows higher).")]
    [SerializeField, Range(0, 32)] private int grassDensity = 10;

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


    private TerrainData _td;
    private int _detailW;
    private int _detailH;
    private Vector3 _terrainPos;
    private Vector3 _terrainSize;

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
        if (gen == null || gen.PathPoints == null || gen.PathPoints.Count < 2) yield break;
        if (targetTerrain == null) targetTerrain = Terrain.activeTerrain;
        if (targetTerrain == null) yield break;

        _td = targetTerrain.terrainData;
        if (_td == null) yield break;

        _detailW = _td.detailWidth;
        _detailH = _td.detailHeight;

        _terrainPos = targetTerrain.transform.position;
        _terrainSize = _td.size;

        if (debugStampAtCarOnce && !_didDebugStamp)
        {
            _didDebugStamp = true;

            // If you don't have a car ref here, just stamp at the first path point
            DebugStampSquareAtWorld(gen.PathPoints[0]);
        }

        // Road width comes from generator usage pattern in your spawners (same concept)
        // We'll clear (roadWidth/2 + padding) fully, then feather to grass over roadFeatherMeters.
        float roadHalf = Mathf.Max(0.01f, gen.RoadWidth * 0.5f);
        float clearRadius = roadHalf + roadClearPaddingMeters;
        float featherRadius = clearRadius + roadFeatherMeters;
        float paintRadius = featherRadius + paintOutwardMeters;

        int samples = 0;
        int inBounds = 0;
        int outBounds = 0;

        // Iterate along the path points and stamp into detail map
        for (int i = 0; i < gen.PathPoints.Count - 1; i++)
        {
            Vector3 a = gen.PathPoints[i];
            Vector3 b = gen.PathPoints[i + 1];

            float segLen = Vector3.Distance(a, b);
            if (segLen < 0.01f) continue;

            int steps = Mathf.Max(1, Mathf.CeilToInt(segLen / Mathf.Max(0.25f, bandStepMeters)));
            for (int s = 0; s <= steps; s++)
            {
                float t = (steps == 0) ? 0f : (s / (float)steps);
                Vector3 p = Vector3.Lerp(a, b, t);

                // direction along segment
                Vector3 fwd = (b - a);
                fwd.y = 0f;
                if (fwd.sqrMagnitude < 0.0001f) fwd = Vector3.forward;
                fwd.Normalize();

                Vector3 right = Vector3.Cross(Vector3.up, fwd).normalized;

                // Clear radius based on road width (but we'll ALSO do physics road clearing)
                float hardClear = roadHalf + roadClearPaddingMeters + roadClearExtraMeters;

                // Paint outward rings on BOTH sides
                for (int ring = 1; ring <= bandRings; ring++)
                {
                    float baseOff = hardClear + ring * ringSpacingMeters;
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


        if (debugLogBounds)
        {   
            Debug.Log($"[TerrainDetailGrassPainter] layer={detailLayerIndex} samples={samples} inBounds={inBounds} outBounds={outBounds} detailSize={_detailW}x{_detailH}");
        }
        targetTerrain.Flush();
    }

    private void PaintPoint(Vector3 world, int density)
    {
        Vector2 norm = WorldToTerrainNorm(world);
        if (norm.x < 0f || norm.x > 1f || norm.y < 0f || norm.y > 1f) return;

        // If we can detect road under this point, do NOT paint.
        if (clearRoadUsingPhysics && roadMask.value != 0)
        {
            Vector3 origin = new Vector3(world.x, _terrainPos.y + _terrainSize.y + roadClearRayStartHeight, world.z);
            float maxRay = _terrainSize.y + roadClearRayDown;

            if (Physics.Raycast(origin, Vector3.down, out _, maxRay, roadMask, QueryTriggerInteraction.Ignore))
            {
                // road collider under this point => don’t place grass
                return;
            }
        }

        int cx = Mathf.RoundToInt(norm.x * (_detailW - 1));
        int cy = Mathf.RoundToInt(norm.y * (_detailH - 1));

        // Dab size in cells (small square)
        const int dab = 2; // 2 => 5x5. raise to 3 => 7x7 if you want denser-looking fields
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
