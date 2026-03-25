using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Sphere-cast from the camera toward the car; occluders between lens and target softly fade
/// by instancing materials and lerping base color alpha (URP Lit / Shader Graph–friendly).
/// Attach to the GameObject with <see cref="Camera"/>.
/// </summary>
[RequireComponent(typeof(Camera))]
[DefaultExecutionOrder(50)]
public class CameraLineOfSightShield : MonoBehaviour
{
    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    private static readonly int ColorId = Shader.PropertyToID("_Color");

    [Header("Target")]
    [Tooltip("If null, uses CameraFollow.target from a parent.")]
    [SerializeField] private Transform followTarget;

    [Tooltip("World-space offset from follow target for the focus point (e.g. cockpit height).")]
    [SerializeField] private Vector3 focusWorldOffset = new Vector3(0f, 1.2f, 0f);

    [Header("Sweep")]
    [Tooltip("Radius of the cast — wider = harder for thin props to fully block the view cone.")]
    [SerializeField, Min(0.01f)] private float shieldRadius = 0.35f;

    [Tooltip("Extra meters past the focus point to include near-miss occluders.")]
    [SerializeField, Min(0f)] private float castPastFocus = 0.5f;

    [SerializeField] private LayerMask occluderLayers = ~0;

    [Tooltip("Layers never treated as occluders (e.g. UI, terrain).")]
    [SerializeField] private LayerMask excludeFromOccluders;

    [Header("Fade")]
    [Tooltip("Alpha multiplier on occluder materials when fully faded (0 = invisible, 1 = unchanged).")]
    [SerializeField, Range(0f, 1f)] private float occludedAlphaMultiplier = 0.22f;

    [Tooltip("How fast alpha moves toward occluded / full opacity (per second).")]
    [SerializeField, Min(0.1f)] private float fadeSpeed = 6f;

    [Header("Behaviour")]
    [SerializeField] private bool enableShield = true;

    [Tooltip("Skip particle billboards (often use incompatible shaders).")]
    [SerializeField] private bool skipParticleRenderers = true;

    [Tooltip("Max hits processed per frame (internal buffer size).")]
    [SerializeField, Min(8)] private int maxHits = 48;

    private Camera _camera;
    private CameraFollow _cameraFollow;
    private readonly List<Renderer> _scratchRenderers = new List<Renderer>(16);
    private readonly List<Renderer> _nullKeys = new List<Renderer>(4);
    private readonly List<FadeEntry> _teardownQueue = new List<FadeEntry>(16);

    private RaycastHit[] _hits;
    private readonly Dictionary<Renderer, FadeEntry> _entries = new Dictionary<Renderer, FadeEntry>(32);
    private readonly HashSet<Renderer> _wantedHidden = new HashSet<Renderer>();

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
        public float CurrentMultiplier = 1f;
    }

    private void Awake()
    {
        _camera = GetComponent<Camera>();
        _hits = new RaycastHit[Mathf.Clamp(maxHits, 8, 128)];
        _cameraFollow = GetComponentInParent<CameraFollow>();
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
        Vector3 delta = focus - origin;
        float dist = delta.magnitude;
        if (dist < 0.05f)
        {
            ClearAllOcclusion();
            return;
        }

        Vector3 dir = delta / dist;
        float castDistance = dist + castPastFocus;

        int mask = occluderLayers.value & ~excludeFromOccluders.value;
        int count = Physics.SphereCastNonAlloc(
            origin,
            shieldRadius,
            dir,
            _hits,
            castDistance,
            mask,
            QueryTriggerInteraction.Ignore);

        _wantedHidden.Clear();

        if (count > 0)
        {
            SortHitsByDistance(_hits, count);

            Transform targetRoot = TargetRoot;
            Transform cameraRoot = _camera.transform.root;

            for (int i = 0; i < count; i++)
            {
                var hit = _hits[i];
                if (hit.collider == null)
                    continue;

                Transform t = hit.collider.transform;
                if (targetRoot != null && IsDescendantOf(t, targetRoot))
                    break;

                if (IsDescendantOf(t, cameraRoot))
                    continue;

                CollectRenderers(hit.collider, _scratchRenderers, skipParticleRenderers);
                for (int r = 0; r < _scratchRenderers.Count; r++)
                {
                    Renderer ren = _scratchRenderers[r];
                    if (ren != null)
                        _wantedHidden.Add(ren);
                }
                _scratchRenderers.Clear();
            }
        }

        float dt = Time.deltaTime;
        float targetMult = occludedAlphaMultiplier;

        foreach (Renderer ren in _wantedHidden)
        {
            if (ren == null)
                continue;
            if (!_entries.TryGetValue(ren, out FadeEntry e))
            {
                e = new FadeEntry { Renderer = ren, CurrentMultiplier = 1f };
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
            float goal = want ? targetMult : 1f;
            e.CurrentMultiplier = Mathf.MoveTowards(e.CurrentMultiplier, goal, fadeSpeed * dt);

            if (!e.Prepared)
                TryPrepare(e);

            if (e.Prepared)
                ApplyMultiplier(e);

            if (!want && e.CurrentMultiplier >= 0.999f)
                _teardownQueue.Add(e);
        }

        for (int i = 0; i < _teardownQueue.Count; i++)
            Teardown(_teardownQueue[i]);
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

    private void OnDisable()
    {
        ClearAllOcclusion();
    }

    private void ClearAllOcclusion()
    {
        foreach (var kv in _entries)
        {
            if (kv.Value != null)
                RestoreEntryMaterials(kv.Value);
        }
        _entries.Clear();
        _wantedHidden?.Clear();
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
            e.BaseColors = new[] { c };
            e.BaseAlphas = new[] { c.a };
            e.OriginalShared = null;
            e.Working = null;
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
            if (src == null)
                continue;

            Material inst = new Material(src);
            e.Working[i] = inst;

            if (ReadBaseColor(inst, out Color c))
            {
                e.BaseColors[i] = c;
                e.BaseAlphas[i] = c.a;
                UrpFadeMaterialUtility.TryEnableAlphaBlend(inst);
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
            return;
        }

        ren.sharedMaterials = e.Working;
        e.Prepared = true;
    }

    private void ApplyMultiplier(FadeEntry e)
    {
        if (!e.Prepared || e.Renderer == null)
            return;

        if (e.IsSprite && e.Renderer is SpriteRenderer sr && e.BaseColors != null && e.BaseAlphas != null)
        {
            Color c = e.BaseColors[0];
            c.a = Mathf.Clamp01(e.BaseAlphas[0] * e.CurrentMultiplier);
            sr.color = c;
            return;
        }

        if (e.Working == null)
            return;

        for (int i = 0; i < e.Working.Length; i++)
        {
            Material m = e.Working[i];
            if (m == null || e.BaseColors == null || i >= e.BaseColors.Length)
                continue;

            Color c = e.BaseColors[i];
            c.a = Mathf.Clamp01(e.BaseAlphas[i] * e.CurrentMultiplier);
            if (m.HasProperty(BaseColorId))
                m.SetColor(BaseColorId, c);
            else if (m.HasProperty(ColorId))
                m.SetColor(ColorId, c);
        }
    }

    private void Teardown(FadeEntry e)
    {
        if (e == null)
            return;

        Renderer ren = e.Renderer;
        if (ren != null)
            _entries.Remove(ren);

        RestoreEntryMaterials(e);
    }

    private static void RestoreEntryMaterials(FadeEntry e)
    {
        if (e == null)
            return;

        Renderer ren = e.Renderer;
        if (ren != null)
        {
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

    private static void CollectRenderers(Collider c, List<Renderer> list, bool skipParticles)
    {
        var rs = c.GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < rs.Length; i++)
        {
            Renderer r = rs[i];
            if (r == null)
                continue;
            if (skipParticles && r is ParticleSystemRenderer)
                continue;
            list.Add(r);
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

    private static void SortHitsByDistance(RaycastHit[] hits, int count)
    {
        for (int i = 1; i < count; i++)
        {
            RaycastHit key = hits[i];
            int j = i - 1;
            while (j >= 0 && hits[j].distance > key.distance)
            {
                hits[j + 1] = hits[j];
                j--;
            }
            hits[j + 1] = key;
        }
    }
}

/// <summary>Best-effort URP Lit / Shader Graph setup so base alpha blends (see-through).</summary>
internal static class UrpFadeMaterialUtility
{
    private static readonly int Surface = Shader.PropertyToID("_Surface");
    private static readonly int Blend = Shader.PropertyToID("_Blend");
    private static readonly int ZWrite = Shader.PropertyToID("_ZWrite");
    private static readonly int SrcBlend = Shader.PropertyToID("_SrcBlend");
    private static readonly int DstBlend = Shader.PropertyToID("_DstBlend");

    public static void TryEnableAlphaBlend(Material m)
    {
        if (m == null)
            return;

        string sn = m.shader != null ? m.shader.name : string.Empty;
        bool urpLike = sn.Contains("Universal Render Pipeline") || sn.Contains("Shader Graph");

        if (urpLike && m.HasProperty(Surface))
        {
            m.SetFloat(Surface, 1f);
            if (m.HasProperty(Blend))
                m.SetFloat(Blend, 0f);
            if (m.HasProperty(ZWrite))
                m.SetFloat(ZWrite, 0f);
            if (m.HasProperty(SrcBlend))
                m.SetFloat(SrcBlend, (float)BlendMode.SrcAlpha);
            if (m.HasProperty(DstBlend))
                m.SetFloat(DstBlend, (float)BlendMode.OneMinusSrcAlpha);

            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            m.renderQueue = (int)RenderQueue.Transparent;
            return;
        }

        if (sn.Contains("Sprites") || sn.Contains("Sprite"))
        {
            m.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
            m.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
            m.renderQueue = (int)RenderQueue.Transparent;
        }
    }
}
