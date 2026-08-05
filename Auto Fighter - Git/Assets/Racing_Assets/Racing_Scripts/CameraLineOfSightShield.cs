using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Camera→car line-of-sight: rays hit blockers before the car → those props fade via
/// a transparent fade shader (real alpha). Never climbs to track/spawner roots.
/// Occluded Visibility is the target alpha (0 = invisible, 1 = opaque).
/// </summary>
[RequireComponent(typeof(Camera))]
[DefaultExecutionOrder(50)]
public class CameraLineOfSightShield : MonoBehaviour
{
    private static readonly int BaseMapId = Shader.PropertyToID("_BaseMap");
    private static readonly int MainTexId = Shader.PropertyToID("_MainTex");
    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    private static readonly int ColorId = Shader.PropertyToID("_Color");
    private static readonly int BaseMapStId = Shader.PropertyToID("_BaseMap_ST");
    private static readonly int MainTexStId = Shader.PropertyToID("_MainTex_ST");

    private const string FadeShaderName = "Racing/OcclusionFade";

    [Header("Target")]
    [Tooltip("If null, uses CameraFollow.target from a parent.")]
    [SerializeField] private Transform followTarget;

    [Tooltip("World-space offset from follow target (aim at car body, not ground pivot).")]
    [SerializeField] private Vector3 focusWorldOffset = new Vector3(0f, 1.0f, 0f);

    [Header("Rays")]
    [SerializeField, Min(0f)] private float rayRadius = 0.2f;
    [SerializeField] private bool useEdgeRays = true;
    [SerializeField, Min(0f)] private float edgeRayOffset = 0.55f;

    [Tooltip("Fade the whole prop group, not only the hit child — stops before track/spawner parents.")]
    [SerializeField] private bool fadePropGroup = true;

    [SerializeField] private LayerMask occluderLayers = ~0;
    [SerializeField] private LayerMask excludeFromOccluders;
    [SerializeField] private bool includeTriggers = true;

    [Header("Fade")]
    [Tooltip("Target alpha when blocking the camera. 0 = fully invisible, 0.3 = ghosted, 1 = no fade.")]
    [SerializeField, Range(0f, 1f)] private float occludedVisibility = 0.3f;

    [SerializeField, Min(0.1f)] private float fadeSpeed = 12f;

    [Header("Behaviour")]
    [SerializeField] private bool enableShield = true;
    [SerializeField] private bool skipParticleRenderers = true;
    [SerializeField] private bool skipCoinPickups = true;
    [SerializeField, Min(8)] private int maxHits = 48;

    [Header("Debug")]
    [SerializeField] private bool drawDebugRays = true;

    private Camera _camera;
    private CameraFollow _cameraFollow;
    private RaycastHit[] _hits;
    private Shader _fadeShader;
    private readonly List<Vector3> _targets = new List<Vector3>(8);
    private readonly List<Renderer> _nullKeys = new List<Renderer>(4);
    private readonly List<FadeEntry> _teardownQueue = new List<FadeEntry>(16);
    private readonly Dictionary<Renderer, FadeEntry> _entries = new Dictionary<Renderer, FadeEntry>(32);
    private readonly HashSet<Renderer> _wantedHidden = new HashSet<Renderer>();
    private readonly HashSet<int> _seenColliders = new HashSet<int>();

    private Transform TargetRoot => followTarget != null ? followTarget.root : null;

    private sealed class FadeEntry
    {
        public Renderer Renderer;
        public Material[] OriginalShared;
        public Material[] Working;
        public Color[] BaseColors;
        public float[] BaseAlphas;
        public bool IsSprite;
        public bool Prepared;
        public bool UsesMaterialAlpha;
        public float CurrentVisibility = 1f;
        public bool ForcedOff;
    }

    private void Awake()
    {
        _camera = GetComponent<Camera>();
        _hits = new RaycastHit[Mathf.Clamp(maxHits, 8, 128)];
        _cameraFollow = GetComponentInParent<CameraFollow>();
        _fadeShader = Shader.Find(FadeShaderName);
        if (_fadeShader == null)
            Debug.LogWarning($"[CameraLineOfSightShield] Missing shader '{FadeShaderName}'. Fade will hard-hide only.");
    }

    private void LateUpdate()
    {
        if (!enableShield || _camera == null)
            return;

        PurgeDestroyedEntries();

        if (followTarget == null && _cameraFollow != null)
            followTarget = _cameraFollow.target;

        if (followTarget == null)
        {
            ClearAllOcclusion();
            return;
        }

        Vector3 origin = _camera.transform.position;
        Vector3 focus = followTarget.position + focusWorldOffset;
        if ((focus - origin).sqrMagnitude < 0.0025f)
        {
            ClearAllOcclusion();
            return;
        }

        int mask = occluderLayers.value & ~excludeFromOccluders.value;
        var triggerMode = includeTriggers
            ? QueryTriggerInteraction.Collide
            : QueryTriggerInteraction.Ignore;

        _wantedHidden.Clear();
        _seenColliders.Clear();
        BuildRayTargets(origin, focus, _targets);

        for (int i = 0; i < _targets.Count; i++)
            CastAndCollect(origin, _targets[i], mask, triggerMode);

        UpdateFades(Time.deltaTime);
    }

    private void BuildRayTargets(Vector3 cameraPos, Vector3 focus, List<Vector3> output)
    {
        output.Clear();
        output.Add(focus);
        if (!useEdgeRays || edgeRayOffset <= 0f)
            return;

        Vector3 fwd = (focus - cameraPos).normalized;
        Vector3 right = Vector3.Cross(Vector3.up, fwd);
        if (right.sqrMagnitude < 0.0001f)
            right = _camera != null ? _camera.transform.right : transform.right;
        right.Normalize();
        Vector3 up = Vector3.Cross(fwd, right).normalized;
        float o = edgeRayOffset;
        output.Add(focus + right * o);
        output.Add(focus - right * o);
        output.Add(focus + up * o);
        output.Add(focus - up * o);
    }

    private void CastAndCollect(Vector3 origin, Vector3 target, int mask, QueryTriggerInteraction triggerMode)
    {
        Vector3 delta = target - origin;
        float dist = delta.magnitude;
        if (dist < 0.05f)
            return;

        Vector3 dir = delta / dist;
        int count = rayRadius > 0.001f
            ? Physics.SphereCastNonAlloc(origin, rayRadius, dir, _hits, dist, mask, triggerMode)
            : Physics.RaycastNonAlloc(origin, dir, _hits, dist, mask, triggerMode);
        if (count <= 0)
            return;

        for (int i = 1; i < count; i++)
        {
            RaycastHit key = _hits[i];
            int j = i - 1;
            while (j >= 0 && _hits[j].distance > key.distance)
            {
                _hits[j + 1] = _hits[j];
                j--;
            }
            _hits[j + 1] = key;
        }

        Transform targetRoot = TargetRoot;
        Transform cameraRoot = _camera.transform.root;

        for (int i = 0; i < count; i++)
        {
            RaycastHit hit = _hits[i];
            if (hit.collider == null)
                continue;
            if (hit.distance >= dist - 0.01f)
                break;

            Transform t = hit.collider.transform;
            if (targetRoot != null && IsDescendantOf(t, targetRoot))
                break;
            if (cameraRoot != null && IsDescendantOf(t, cameraRoot))
                continue;
            if (ShouldIgnoreOccluder(t))
                continue;

            int id = hit.collider.GetInstanceID();
            if (!_seenColliders.Add(id))
                continue;

            AddWantedRenderersFromCollider(hit.collider);
        }
    }

    private void UpdateFades(float dt)
    {
        float hiddenGoal = Mathf.Clamp01(occludedVisibility);

        foreach (Renderer ren in _wantedHidden)
        {
            if (ren == null) continue;
            if (!_entries.TryGetValue(ren, out FadeEntry e))
            {
                e = new FadeEntry { Renderer = ren, CurrentVisibility = 1f };
                _entries[ren] = e;
            }
        }

        _teardownQueue.Clear();
        foreach (var kv in _entries)
        {
            Renderer ren = kv.Key;
            FadeEntry e = kv.Value;
            if (ren == null || e == null)
                continue;

            bool want = _wantedHidden.Contains(ren);
            float goal = want ? hiddenGoal : 1f;
            e.CurrentVisibility = Mathf.MoveTowards(e.CurrentVisibility, goal, fadeSpeed * dt);

            if (!e.Prepared)
                TryPrepare(e);

            ApplyVisibility(e);

            if (!want && e.CurrentVisibility >= 0.999f && !e.ForcedOff)
                _teardownQueue.Add(e);
        }

        for (int i = 0; i < _teardownQueue.Count; i++)
            Teardown(_teardownQueue[i]);
    }

    private void ApplyVisibility(FadeEntry e)
    {
        if (e?.Renderer == null)
            return;

        float v = Mathf.Clamp01(e.CurrentVisibility);

        if (e.Prepared && e.UsesMaterialAlpha)
            ApplyMaterialAlpha(e, v);

        // Hard-hide only at essentially zero so mid-slider values stay ghosted.
        bool hardHide = v <= 0.001f;
        if (hardHide && !e.ForcedOff)
        {
            e.Renderer.forceRenderingOff = true;
            e.ForcedOff = true;
        }
        else if (!hardHide && e.ForcedOff)
        {
            e.Renderer.forceRenderingOff = false;
            e.ForcedOff = false;
        }
    }

    private void ApplyMaterialAlpha(FadeEntry e, float visibility01)
    {
        if (e.IsSprite && e.Renderer is SpriteRenderer sr && e.BaseAlphas != null)
        {
            Color c = e.BaseColors[0];
            c.a = Mathf.Clamp01(e.BaseAlphas[0] * visibility01);
            sr.color = c;
            return;
        }

        if (e.Working == null || e.BaseColors == null || e.BaseAlphas == null)
            return;

        for (int i = 0; i < e.Working.Length; i++)
        {
            Material mat = e.Working[i];
            if (mat == null || i >= e.BaseColors.Length)
                continue;

            Color c = e.BaseColors[i];
            c.a = Mathf.Clamp01(e.BaseAlphas[i] * visibility01);
            if (mat.HasProperty(BaseColorId))
                mat.SetColor(BaseColorId, c);
            else if (mat.HasProperty(ColorId))
                mat.SetColor(ColorId, c);
        }
    }

    private void PurgeDestroyedEntries()
    {
        _nullKeys.Clear();
        foreach (var k in _entries.Keys)
        {
            if (k == null)
                _nullKeys.Add(k);
        }
        for (int i = 0; i < _nullKeys.Count; i++)
            _entries.Remove(_nullKeys[i]);
    }

    private void OnDisable() => ClearAllOcclusion();

    private void ClearAllOcclusion()
    {
        foreach (var kv in _entries)
            RestoreEntry(kv.Value);
        _entries.Clear();
        _wantedHidden.Clear();
    }

    private void AddWantedRenderersFromCollider(Collider col)
    {
        if (col == null) return;

        Transform group = fadePropGroup ? ResolvePropRoot(col.transform) : col.transform;
        if (group == null || ShouldIgnoreOccluder(group))
            return;

        var rs = group.GetComponentsInChildren<Renderer>(true);
        Transform targetRoot = TargetRoot;
        Transform cameraRoot = _camera != null ? _camera.transform.root : null;

        for (int i = 0; i < rs.Length; i++)
        {
            Renderer r = rs[i];
            if (r == null) continue;
            if (skipParticleRenderers && r is ParticleSystemRenderer) continue;
            if (targetRoot != null && IsDescendantOf(r.transform, targetRoot)) continue;
            if (cameraRoot != null && IsDescendantOf(r.transform, cameraRoot)) continue;
            if (ShouldIgnoreOccluder(r.transform)) continue;
            _wantedHidden.Add(r);
        }
    }

    private static Transform ResolvePropRoot(Transform hit)
    {
        if (hit == null) return null;

        var identity = hit.GetComponentInParent<CrashObstacleIdentity>();
        if (identity != null) return identity.transform;

        var racing = hit.GetComponentInParent<RacingObstacle>();
        if (racing != null) return racing.transform;

        var thrown = hit.GetComponentInParent<ThrownObstacle>();
        if (thrown != null) return thrown.transform;

        var rb = hit.GetComponentInParent<Rigidbody>();
        if (rb != null && !IsWorldContainer(rb.transform))
            return rb.transform;

        Transform t = hit;
        while (t.parent != null && !IsWorldContainer(t.parent))
            t = t.parent;
        return t;
    }

    private static bool IsWorldContainer(Transform t)
    {
        if (t == null) return true;
        if (t.GetComponent<ProceduralTrackGenerator>() != null) return true;
        if (t.GetComponent<TrackEnvironmentSpawner>() != null) return true;
        if (t.GetComponent<TrackObstacleSpawner>() != null) return true;
        if (t.GetComponent<TrackSpawnerQueue>() != null) return true;
        if (t.GetComponent<TrackCoinSpawner>() != null) return true;
        if (t.GetComponent<Terrain>() != null) return true;
        if (t.GetComponent<TerrainCollider>() != null) return true;

        string n = t.name;
        if (string.IsNullOrEmpty(n)) return false;
        n = n.ToLowerInvariant();
        return n.Contains("spawner")
               || n.Contains("trackgenerator")
               || n.Contains("track generator")
               || n.Contains("environment root")
               || n.Contains("obstacle root")
               || n == "track"
               || n.StartsWith("track_");
    }

    private bool ShouldIgnoreOccluder(Transform t)
    {
        if (t == null) return true;
        if (IsWorldContainer(t)) return true;
        if (skipCoinPickups && t.GetComponentInParent<CoinPickup>() != null) return true;
        return false;
    }

    private void TryPrepare(FadeEntry e)
    {
        Renderer ren = e.Renderer;
        if (ren == null || e.Prepared)
            return;

        if (ren is SpriteRenderer spr)
        {
            Color c = spr.color;
            e.IsSprite = true;
            e.UsesMaterialAlpha = true;
            e.BaseColors = new[] { c };
            e.BaseAlphas = new[] { c.a };
            e.Prepared = true;
            return;
        }

        Material[] shared = ren.sharedMaterials;
        if (shared == null || shared.Length == 0)
            return;

        e.OriginalShared = (Material[])shared.Clone();
        int n = e.OriginalShared.Length;
        e.Working = new Material[n];
        e.BaseColors = new Color[n];
        e.BaseAlphas = new float[n];

        bool anyOk = false;
        for (int i = 0; i < n; i++)
        {
            Material src = e.OriginalShared[i];
            if (src == null) continue;

            Material inst = BuildFadeMaterial(src);
            e.Working[i] = inst;
            if (inst == null)
                continue;

            if (ReadBaseColor(inst, out Color c))
            {
                e.BaseColors[i] = c;
                e.BaseAlphas[i] = Mathf.Max(0.001f, c.a);
                anyOk = true;
            }
        }

        if (!anyOk)
        {
            for (int i = 0; i < n; i++)
            {
                if (e.Working[i] != null)
                    Destroy(e.Working[i]);
            }
            e.Working = null;
            e.OriginalShared = null;
            e.BaseColors = null;
            e.BaseAlphas = null;
            e.Prepared = true;
            e.UsesMaterialAlpha = false;
            return;
        }

        ren.sharedMaterials = e.Working;
        e.UsesMaterialAlpha = true;
        e.Prepared = true;
    }

    private Material BuildFadeMaterial(Material src)
    {
        if (src == null)
            return null;

        // Dedicated transparent shader so alpha always blends (opaque Lit ignores alpha).
        if (_fadeShader != null)
        {
            var fade = new Material(_fadeShader);
            CopyAlbedoToFade(src, fade);
            return fade;
        }

        // Fallback: clone source and force URP transparent surface.
        var clone = new Material(src);
        ForceTransparentSurface(clone);
        return clone;
    }

    private static void CopyAlbedoToFade(Material src, Material dst)
    {
        Texture map = null;
        Vector4 st = new Vector4(1f, 1f, 0f, 0f);

        if (src.HasProperty(BaseMapId))
        {
            map = src.GetTexture(BaseMapId);
            if (src.HasProperty(BaseMapStId))
                st = src.GetVector(BaseMapStId);
        }
        else if (src.HasProperty(MainTexId))
        {
            map = src.GetTexture(MainTexId);
            if (src.HasProperty(MainTexStId))
                st = src.GetVector(MainTexStId);
        }

        if (map != null && dst.HasProperty(BaseMapId))
        {
            dst.SetTexture(BaseMapId, map);
            if (dst.HasProperty(BaseMapStId))
                dst.SetVector(BaseMapStId, st);
        }

        ReadBaseColor(src, out Color c);
        c.a = Mathf.Max(0.001f, c.a);
        if (dst.HasProperty(BaseColorId))
            dst.SetColor(BaseColorId, c);
        else if (dst.HasProperty(ColorId))
            dst.SetColor(ColorId, c);
    }

    private static void ForceTransparentSurface(Material m)
    {
        if (m == null) return;

        if (m.HasProperty("_Surface"))
            m.SetFloat("_Surface", 1f);
        if (m.HasProperty("_Blend"))
            m.SetFloat("_Blend", 0f);
        if (m.HasProperty("_ZWrite"))
            m.SetFloat("_ZWrite", 0f);
        if (m.HasProperty("_SrcBlend"))
            m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
        if (m.HasProperty("_DstBlend"))
            m.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
        if (m.HasProperty("_AlphaClip"))
            m.SetFloat("_AlphaClip", 0f);
        if (m.HasProperty("_Cull"))
            m.SetFloat("_Cull", 0f);

        m.SetOverrideTag("RenderType", "Transparent");
        m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        m.DisableKeyword("_SURFACE_TYPE_OPAQUE");
        m.DisableKeyword("_ALPHAPREMULTIPLY_ON");
        m.DisableKeyword("_ALPHATEST_ON");
        m.renderQueue = (int)RenderQueue.Transparent;
        m.SetShaderPassEnabled("ShadowCaster", false);
    }

    private static bool ReadBaseColor(Material m, out Color c)
    {
        if (m.HasProperty(BaseColorId))
        {
            c = m.GetColor(BaseColorId);
            return true;
        }
        if (m.HasProperty(ColorId))
        {
            c = m.GetColor(ColorId);
            return true;
        }
        c = Color.white;
        return false;
    }

    private void Teardown(FadeEntry e)
    {
        if (e == null) return;
        if (e.Renderer != null)
            _entries.Remove(e.Renderer);
        RestoreEntry(e);
    }

    private static void RestoreEntry(FadeEntry e)
    {
        if (e == null) return;

        Renderer ren = e.Renderer;
        if (ren != null)
        {
            if (e.ForcedOff)
            {
                ren.forceRenderingOff = false;
                e.ForcedOff = false;
            }

            if (e.OriginalShared != null)
                ren.sharedMaterials = e.OriginalShared;

            if (e.IsSprite && ren is SpriteRenderer sr && e.BaseColors != null && e.BaseAlphas != null)
            {
                Color c = e.BaseColors[0];
                c.a = e.BaseAlphas[0];
                sr.color = c;
            }
        }

        if (e.Working != null)
        {
            for (int i = 0; i < e.Working.Length; i++)
            {
                if (e.Working[i] != null)
                    Destroy(e.Working[i]);
            }
        }
    }

    private static bool IsDescendantOf(Transform t, Transform ancestor)
    {
        while (t != null)
        {
            if (t == ancestor)
                return true;
            t = t.parent;
        }
        return false;
    }

    private void OnDrawGizmos()
    {
        if (!drawDebugRays || !Application.isPlaying)
            return;
        if (_camera == null || followTarget == null)
            return;

        Vector3 origin = _camera.transform.position;
        Vector3 focus = followTarget.position + focusWorldOffset;
        BuildRayTargets(origin, focus, _targets);
        Gizmos.color = new Color(0.2f, 1f, 0.9f, 0.9f);
        for (int i = 0; i < _targets.Count; i++)
            Gizmos.DrawLine(origin, _targets[i]);
    }
}
