using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Bakes a world-XZ road mask after each successful track. Authored terrain grass maps
/// are left alone; URP grass shaders clip blades where this mask is set.
/// </summary>
[DefaultExecutionOrder(-40)]
[DisallowMultipleComponent]
public sealed class TerrainDetailGrassPainter : MonoBehaviour
{
    const string ClipKeyword = "RACER_ROAD_GRASS_CLIP";
    static readonly int ClipTexId = Shader.PropertyToID("_RacerRoadGrassClipTex");
    static readonly int ClipMinMaxId = Shader.PropertyToID("_RacerRoadGrassClipMinMax");

    [Header("Road mask")]
    [Tooltip("Extra grass-free shoulder beyond the road mesh, in meters. 0 = grass at the asphalt edge.")]
    [SerializeField, Min(0f)] private float roadClearPaddingMeters = 0f;
    [SerializeField, Min(64)] private int maskResolution = 1024;
    [SerializeField, Min(0.25f)] private float metersPerTexel = 0.5f;
    [SerializeField] private bool debugLogBounds = true;

    private readonly List<Vector3> _path = new List<Vector3>(2048);
    private Texture2D _mask;
    private Color32[] _pixels;
    private Vector4 _clipMinMax;
    private bool _clipOn;

    public int LastPaintedTerrainCount { get; private set; }

    private void LateUpdate()
    {
        if (_clipOn && _mask != null)
            ApplyGlobals();
    }

    private void OnDestroy()
    {
        ClearMask();
        if (_mask != null)
        {
            Destroy(_mask);
            _mask = null;
        }
    }

    public void PaintNow(ProceduralTrackGenerator gen)
    {
        IEnumerator e = CoPaint(gen);
        while (e.MoveNext()) { }
    }

    public IEnumerator CoPaint(ProceduralTrackGenerator gen)
    {
        LastPaintedTerrainCount = 0;
        if (gen == null || !gen.LastGenerateSucceeded)
        {
            ClearMask();
            yield break;
        }

        gen.FillRoadMeshCenterPath(_path);
        if (_path.Count < 2)
        {
            ClearMask();
            if (debugLogBounds)
                Debug.LogWarning("[TerrainDetailGrassPainter] No centerline; road clip disabled.");
            yield break;
        }

        float meshPad = Mathf.Max(0f, roadClearPaddingMeters);
        BakeMask(_path, gen, meshPad);
        LastPaintedTerrainCount = 1;
        yield return null;
    }

    public void ClearMask()
    {
        _clipOn = false;
        Shader.DisableKeyword(ClipKeyword);
        Shader.SetGlobalTexture(ClipTexId, Texture2D.blackTexture);
        Shader.SetGlobalVector(ClipMinMaxId, Vector4.zero);
    }

    private void ApplyGlobals()
    {
        Shader.SetGlobalTexture(ClipTexId, _mask);
        Shader.SetGlobalVector(ClipMinMaxId, _clipMinMax);
        Shader.EnableKeyword(ClipKeyword);
    }

    private void BakeMask(List<Vector3> path, ProceduralTrackGenerator gen, float meshPad)
    {
        float minX = float.MaxValue, maxX = float.MinValue;
        float minZ = float.MaxValue, maxZ = float.MinValue;
        for (int i = 0; i < path.Count; i++)
        {
            Vector3 p = path[i];
            if (p.x < minX) minX = p.x;
            if (p.x > maxX) maxX = p.x;
            if (p.z < minZ) minZ = p.z;
            if (p.z > maxZ) maxZ = p.z;
        }

        float halfWidth = Mathf.Max(0.5f, gen.RoadWidth * 0.5f);
        minX -= halfWidth + 2f;
        maxX += halfWidth + 2f;
        minZ -= halfWidth + 2f;
        maxZ += halfWidth + 2f;

        float worldW = Mathf.Max(8f, maxX - minX);
        float worldH = Mathf.Max(8f, maxZ - minZ);
        int tw = Mathf.Clamp(Mathf.CeilToInt(worldW / Mathf.Max(0.25f, metersPerTexel)), 64, maskResolution);
        int th = Mathf.Clamp(Mathf.CeilToInt(worldH / Mathf.Max(0.25f, metersPerTexel)), 64, maskResolution);

        EnsureMask(tw, th);
        System.Array.Clear(_pixels, 0, _pixels.Length);

        MeshFilter mf = gen != null ? gen.GetComponent<MeshFilter>() : null;
        Mesh mesh = mf != null ? mf.sharedMesh : null;
        if (mesh != null && mesh.vertexCount >= 3)
        {
            StampRoadMesh(gen, meshPad, minX, minZ, worldW, worldH, tw, th);
        }
        else
        {
            float stampStep = Mathf.Max(metersPerTexel * 0.75f, 0.35f);
            for (int i = 0; i < path.Count - 1; i++)
            {
                Vector3 a = path[i];
                Vector3 b = path[i + 1];
                float len = Vector3.Distance(a, b);
                if (len < 0.01f) continue;
                int steps = Mathf.Max(1, Mathf.CeilToInt(len / stampStep));
                for (int s = 0; s <= steps; s++)
                {
                    Vector3 p = Vector3.Lerp(a, b, s / (float)steps);
                    StampCircle(p.x, p.z, halfWidth + meshPad, minX, minZ, worldW, worldH, tw, th);
                }
            }
        }

        _mask.SetPixels32(_pixels);
        _mask.Apply(false, false);

        _clipMinMax = new Vector4(minX, minZ, maxX, maxZ);
        _clipOn = true;
        ApplyGlobals();

        if (debugLogBounds)
        {
            Debug.Log(
                $"[TerrainDetailGrassPainter] Road clip mask {tw}x{th} meshPad={meshPad:0.00}m " +
                $"bounds=({minX:0},{minZ:0})-({maxX:0},{maxZ:0})");
        }
    }

    private void StampRoadMesh(
        ProceduralTrackGenerator gen, float radius,
        float minX, float minZ, float worldW, float worldH, int tw, int th)
    {
        if (gen == null) return;
        MeshFilter mf = gen.GetComponent<MeshFilter>();
        Mesh mesh = mf != null ? mf.sharedMesh : null;
        if (mesh == null || mesh.vertexCount < 3) return;

        Vector3[] verts = mesh.vertices;
        int[] tris = mesh.triangles;
        if (tris == null || tris.Length < 3) return;

        Transform tr = gen.transform;
        for (int t = 0; t < tris.Length; t += 3)
        {
            Vector3 a = tr.TransformPoint(verts[tris[t]]);
            Vector3 b = tr.TransformPoint(verts[tris[t + 1]]);
            Vector3 c = tr.TransformPoint(verts[tris[t + 2]]);
            StampTriangle(a, b, c, radius, minX, minZ, worldW, worldH, tw, th);
        }
    }
                    
    private void StampTriangle(
        Vector3 a, Vector3 b, Vector3 c, float pad,
        float minX, float minZ, float worldW, float worldH, int tw, int th)
    {
        float x0 = Mathf.Min(a.x, Mathf.Min(b.x, c.x)) - pad;
        float x1 = Mathf.Max(a.x, Mathf.Max(b.x, c.x)) + pad;
        float z0 = Mathf.Min(a.z, Mathf.Min(b.z, c.z)) - pad;
        float z1 = Mathf.Max(a.z, Mathf.Max(b.z, c.z)) + pad;

        int ix0 = Mathf.Clamp(Mathf.FloorToInt((x0 - minX) / worldW * (tw - 1)), 0, tw - 1);
        int ix1 = Mathf.Clamp(Mathf.CeilToInt((x1 - minX) / worldW * (tw - 1)), 0, tw - 1);
        int iz0 = Mathf.Clamp(Mathf.FloorToInt((z0 - minZ) / worldH * (th - 1)), 0, th - 1);
        int iz1 = Mathf.Clamp(Mathf.CeilToInt((z1 - minZ) / worldH * (th - 1)), 0, th - 1);

        Vector2 aa = new Vector2(a.x, a.z);
        Vector2 bb = new Vector2(b.x, b.z);
        Vector2 cc = new Vector2(c.x, c.z);
        float padSq = pad * pad;

        for (int iz = iz0; iz <= iz1; iz++)
        {
            float wz = minZ + iz / (float)Mathf.Max(1, th - 1) * worldH;
            for (int ix = ix0; ix <= ix1; ix++)
            {
                float wx = minX + ix / (float)Mathf.Max(1, tw - 1) * worldW;
                Vector2 p = new Vector2(wx, wz);
                if (!PointNearTriangle(p, aa, bb, cc, padSq))
                    continue;
                _pixels[iz * tw + ix] = new Color32(255, 255, 255, 255);
            }
        }
    }

    private static bool PointNearTriangle(Vector2 p, Vector2 a, Vector2 b, Vector2 c, float padSq)
    {
        if (PointInTriangle(p, a, b, c))
            return true;
        return DistSegSq(p, a, b) <= padSq || DistSegSq(p, b, c) <= padSq || DistSegSq(p, c, a) <= padSq;
    }

    private static bool PointInTriangle(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
    {
        float d1 = Sign(p, a, b);
        float d2 = Sign(p, b, c);
        float d3 = Sign(p, c, a);
        bool hasNeg = d1 < 0f || d2 < 0f || d3 < 0f;
        bool hasPos = d1 > 0f || d2 > 0f || d3 > 0f;
        return !(hasNeg && hasPos);
    }

    private static float Sign(Vector2 p1, Vector2 p2, Vector2 p3)
    {
        return (p1.x - p3.x) * (p2.y - p3.y) - (p2.x - p3.x) * (p1.y - p3.y);
    }

    private static float DistSegSq(Vector2 p, Vector2 a, Vector2 b)
    {
        Vector2 ab = b - a;
        float den = ab.sqrMagnitude;
        if (den < 1e-8f) return (p - a).sqrMagnitude;
        float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / den);
        Vector2 q = a + ab * t;
        return (p - q).sqrMagnitude;
    }

    private void StampCircle(
        float worldX, float worldZ, float radius,
        float minX, float minZ, float worldW, float worldH, int tw, int th)
    {
        int ix0 = Mathf.Clamp(Mathf.FloorToInt((worldX - radius - minX) / worldW * (tw - 1)), 0, tw - 1);
        int ix1 = Mathf.Clamp(Mathf.CeilToInt((worldX + radius - minX) / worldW * (tw - 1)), 0, tw - 1);
        int iz0 = Mathf.Clamp(Mathf.FloorToInt((worldZ - radius - minZ) / worldH * (th - 1)), 0, th - 1);
        int iz1 = Mathf.Clamp(Mathf.CeilToInt((worldZ + radius - minZ) / worldH * (th - 1)), 0, th - 1);
        float rSq = radius * radius;

        for (int iz = iz0; iz <= iz1; iz++)
        {
            float wz = minZ + iz / (float)Mathf.Max(1, th - 1) * worldH;
            float dz = wz - worldZ;
            for (int ix = ix0; ix <= ix1; ix++)
            {
                float wx = minX + ix / (float)Mathf.Max(1, tw - 1) * worldW;
                float dx = wx - worldX;
                if (dx * dx + dz * dz > rSq) continue;
                _pixels[iz * tw + ix] = new Color32(255, 255, 255, 255);
            }
        }
    }

    private void EnsureMask(int tw, int th)
    {
        if (_mask != null && _mask.width == tw && _mask.height == th && _pixels != null && _pixels.Length == tw * th)
            return;

        if (_mask != null)
            Destroy(_mask);

        _mask = new Texture2D(tw, th, TextureFormat.R8, false, true)
        {
            name = "RacerRoadGrassClip",
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Point
        };
        _pixels = new Color32[tw * th];
    }
}
