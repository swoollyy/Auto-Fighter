using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Occlusion fade between camera and follow target. Default: multi-point line-of-sight samples on the car
/// silhouette (center, edges, diagonals). Optional legacy mode: sphere cast + linear cone filter.
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
    [Tooltip("Cone base radius at the focus point (apex at camera; 0 at lens). Wider = more occluders caught.")]
    [SerializeField, Min(0.01f)] private float shieldRadius = 0.65f;

    [Tooltip("Sphere cast radius used only to gather candidates (should be ≥ cone base radius).")]
    [SerializeField, Min(0.01f)] private float sweepProbeRadius = 0.75f;

    [Tooltip("Extra meters past the focus along the axis; past the focus the cone radius stays at shieldRadius (cylinder cap).")]
    [SerializeField, Min(0f)] private float castPastFocus = 0.5f;

    [Tooltip("Small extra allowance on cone radius (m) to avoid edge sparkles.")]
    [SerializeField, Min(0f)] private float coneRadialSlop = 0.04f;

    [Header("Line Of Sight Sampling")]
    [Tooltip("Use multi-point line-of-sight checks to detect true visual blockers between camera and car.")]
    [SerializeField] private bool useSampledLineOfSight = true;

    [Tooltip("Small cast radius per sample ray. Helps catch thin colliders.")]
    [SerializeField, Min(0f)] private float losSampleCastRadius = 0.16f;

    [Tooltip("Approximate half-width of the car silhouette used for LOS sample points.")]
    [SerializeField, Min(0f)] private float targetSampleHalfWidth = 0.9f;

    [Tooltip("Approximate half-height around focus point used for LOS sample points.")]
    [SerializeField, Min(0f)] private float targetSampleHalfHeight = 0.7f;

    [Tooltip("Include diagonal silhouette samples for more robust occlusion at corners.")]
    [SerializeField] private bool includeDiagonalSamples = true;

    [Tooltip("Also sweep a thick capsule between camera and focus to catch props near the view corridor.")]
    [SerializeField] private bool useVolumetricOverlap = true;

    [Tooltip("When a collider is hit, fade every renderer on that object's root (e.g. tree leaves + trunk).")]
    [SerializeField] private bool collectFromOccluderRoot = true;

    [Tooltip("Test renderer bounds along the view cone so foliage without colliders still fades.")]
    [SerializeField] private bool useRendererBoundsFallback = true;

    [Tooltip("Include trigger colliders in physics queries (some foliage uses trigger volumes).")]
    [SerializeField] private bool includeTriggerColliders = true;

    [Tooltip("Extra ring samples along the camera→car axis (0 = silhouette only).")]
    [SerializeField, Range(0, 4)] private int losAxisRingSamples = 2;

    [Header("Debug Gizmos")]
    [Tooltip("Automatically draw LOS sample gizmos in the Scene view.")]
    [SerializeField] private bool drawDebugGizmos = true;

    [Tooltip("Only draw while playing.")]
    [SerializeField] private bool gizmosOnlyInPlayMode = true;

    [SerializeField] private LayerMask occluderLayers = ~0;

    [Tooltip("Layers never treated as occluders (e.g. UI, terrain).")]
    [SerializeField] private LayerMask excludeFromOccluders;

    [Header("Fade")]
    [Tooltip("Alpha multiplier on occluder materials when fully faded (0 = invisible, 1 = unchanged).")]
    [SerializeField, Range(0f, 1f)] private float occludedAlphaMultiplier = 0.1f;

    [Tooltip("Also dims RGB when occluded (0 = alpha only, 1 = strong ghosting).")]
    [SerializeField, Range(0f, 1f)] private float occludedRgbDim = 0.55f;

    [Tooltip("How fast alpha moves toward occluded / full opacity (per second).")]
    [SerializeField, Min(0.1f)] private float fadeSpeed = 10f;

    [Header("Behaviour")]
    [SerializeField] private bool enableShield = true;

    [Tooltip("Skip particle billboards (often use incompatible shaders).")]
    [SerializeField] private bool skipParticleRenderers = true;

    [Tooltip("Max hits processed per frame (internal buffer size).")]
    [SerializeField, Min(8)] private int maxHits = 64;

    private Camera _camera;
    private CameraFollow _cameraFollow;
    private readonly List<Renderer> _scratchRenderers = new List<Renderer>(16);
    private readonly List<Renderer> _nullKeys = new List<Renderer>(4);
    private readonly List<FadeEntry> _teardownQueue = new List<FadeEntry>(16);
    private readonly List<Vector3> _focusSamples = new List<Vector3>(24);
    private readonly HashSet<Transform> _visitedOccluderRoots = new HashSet<Transform>();

    private RaycastHit[] _hits;
    private Collider[] _overlapBuffer;
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
        int bufferSize = Mathf.Clamp(maxHits, 8, 128);
        _hits = new RaycastHit[bufferSize];
        _overlapBuffer = new Collider[bufferSize];
        _cameraFollow = GetComponentInParent<CameraFollow>();
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        sweepProbeRadius = Mathf.Max(sweepProbeRadius, shieldRadius);
    }
#endif

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

        int mask = occluderLayers.value & ~excludeFromOccluders.value;
        QueryTriggerInteraction triggerMode = includeTriggerColliders
            ? QueryTriggerInteraction.Collide
            : QueryTriggerInteraction.Ignore;
        _wantedHidden.Clear();

        if (useSampledLineOfSight)
            CollectOccludersFromLineOfSightSamples(origin, focus, dist, mask, triggerMode);

        if (useVolumetricOverlap)
            CollectOccludersFromVolumeOverlap(origin, focus, dist, mask, triggerMode);

        if (!useSampledLineOfSight && !useVolumetricOverlap)
        {
            Vector3 dir = delta / dist;
            float castDistance = dist + castPastFocus;
            float probeR = Mathf.Max(sweepProbeRadius, shieldRadius);
            int count = Physics.SphereCastNonAlloc(
                origin,
                probeR,
                dir,
                _hits,
                castDistance,
                mask,
                triggerMode);

            if (count > 0)
            {
                SortHitsByDistance(_hits, count);
                ProcessPhysicsHits(_hits, count, origin, focus, dist);
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
            float m = Mathf.Clamp01(e.CurrentMultiplier);
            c.a = Mathf.Clamp01(e.BaseAlphas[0] * m);
            if (m < 0.999f && occludedRgbDim > 0f)
            {
                float rgbScale = Mathf.Lerp(m, 1f, 1f - occludedRgbDim);
                c.r *= rgbScale;
                c.g *= rgbScale;
                c.b *= rgbScale;
            }
            sr.color = c;
            return;
        }

        if (e.Working == null)
            return;

        for (int i = 0; i < e.Working.Length; i++)
        {
            Material mat = e.Working[i];
            if (mat == null || e.BaseColors == null || i >= e.BaseColors.Length)
                continue;

            Color c = e.BaseColors[i];
            float mult = Mathf.Clamp01(e.CurrentMultiplier);
            c.a = Mathf.Clamp01(e.BaseAlphas[i] * mult);
            if (mult < 0.999f && occludedRgbDim > 0f)
            {
                float rgbScale = Mathf.Lerp(mult, 1f, 1f - occludedRgbDim);
                c.r *= rgbScale;
                c.g *= rgbScale;
                c.b *= rgbScale;
            }
            if (mat.HasProperty(BaseColorId))
                mat.SetColor(BaseColorId, c);
            else if (mat.HasProperty(ColorId))
                mat.SetColor(ColorId, c);
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

    private void CollectRenderersFromOccluderRoot(Transform hitTransform, List<Renderer> list)
    {
        if (hitTransform == null) return;

        Transform root = collectFromOccluderRoot ? hitTransform.root : hitTransform;
        var rs = root.GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < rs.Length; i++)
            TryAddRenderer(rs[i], list, skipParticleRenderers);
    }

    private void TryAddRenderer(Renderer r, List<Renderer> list, bool skipParticles)
    {
        if (r == null || !r.enabled)
            return;
        if (skipParticles && r is ParticleSystemRenderer)
            return;

        Transform targetRoot = TargetRoot;
        Transform cameraRoot = _camera != null ? _camera.transform.root : null;
        if (targetRoot != null && IsDescendantOf(r.transform, targetRoot))
            return;
        if (cameraRoot != null && IsDescendantOf(r.transform, cameraRoot))
            return;

        list.Add(r);
    }

    private void AddWantedRenderersFromCollider(Collider col)
    {
        if (col == null) return;

        _scratchRenderers.Clear();
        CollectRenderersFromOccluderRoot(col.transform, _scratchRenderers);
        for (int r = 0; r < _scratchRenderers.Count; r++)
        {
            Renderer ren = _scratchRenderers[r];
            if (ren != null)
                _wantedHidden.Add(ren);
        }
        _scratchRenderers.Clear();
    }

    private void ProcessPhysicsHits(RaycastHit[] hits, int count, Vector3 cameraPos, Vector3 focus, float focusDist)
    {
        Transform targetRoot = TargetRoot;
        Transform cameraRoot = _camera.transform.root;

        for (int i = 0; i < count; i++)
        {
            var hit = hits[i];
            if (hit.collider == null)
                continue;

            Transform t = hit.collider.transform;
            if (targetRoot != null && IsDescendantOf(t, targetRoot))
                break;

            if (IsDescendantOf(t, cameraRoot))
                continue;

            if (!IsHitInsideViewCone(hit.point, cameraPos, focus, focusDist, shieldRadius, castPastFocus, coneRadialSlop))
                continue;

            AddWantedRenderersFromCollider(hit.collider);
        }
    }

    private void CollectOccludersFromVolumeOverlap(
        Vector3 cameraPos,
        Vector3 focus,
        float focusDist,
        int mask,
        QueryTriggerInteraction triggerMode)
    {
        float radius = Mathf.Max(shieldRadius, sweepProbeRadius) + coneRadialSlop;
        int count = Physics.OverlapCapsuleNonAlloc(
            cameraPos,
            focus,
            radius,
            _overlapBuffer,
            mask,
            triggerMode);

        if (count <= 0)
            return;

        _visitedOccluderRoots.Clear();
        Transform targetRoot = TargetRoot;
        Transform cameraRoot = _camera.transform.root;

        for (int i = 0; i < count; i++)
        {
            Collider col = _overlapBuffer[i];
            if (col == null)
                continue;

            Transform t = col.transform;
            if (targetRoot != null && IsDescendantOf(t, targetRoot))
                continue;
            if (IsDescendantOf(t, cameraRoot))
                continue;

            Transform root = collectFromOccluderRoot ? t.root : t;
            if (!_visitedOccluderRoots.Add(root))
                continue;

            if (useRendererBoundsFallback)
            {
                _scratchRenderers.Clear();
                CollectRenderersFromOccluderRoot(t, _scratchRenderers);
                bool anyBoundsHit = false;
                for (int r = 0; r < _scratchRenderers.Count; r++)
                {
                    Renderer ren = _scratchRenderers[r];
                    if (ren != null && RendererBoundsOccludes(ren, cameraPos, focus, focusDist))
                    {
                        _wantedHidden.Add(ren);
                        anyBoundsHit = true;
                    }
                }
                _scratchRenderers.Clear();

                if (!anyBoundsHit && col.bounds.size.sqrMagnitude > 0f)
                {
                    if (RendererBoundsOccludes(col, cameraPos, focus, focusDist))
                        AddWantedRenderersFromCollider(col);
                }
            }
            else
            {
                AddWantedRenderersFromCollider(col);
            }
        }
    }

    private bool RendererBoundsOccludes(Renderer ren, Vector3 cameraPos, Vector3 focus, float focusDist)
    {
        if (ren == null) return false;
        return BoundsOccludesView(ren.bounds, cameraPos, focus, focusDist);
    }

    private bool RendererBoundsOccludes(Collider col, Vector3 cameraPos, Vector3 focus, float focusDist)
    {
        if (col == null) return false;
        return BoundsOccludesView(col.bounds, cameraPos, focus, focusDist);
    }

    private bool BoundsOccludesView(Bounds bounds, Vector3 cameraPos, Vector3 focus, float focusDist)
    {
        if (focusDist < 0.001f)
            return false;

        Vector3 axis = (focus - cameraPos) / focusDist;
        Ray ray = new Ray(cameraPos, axis);
        if (bounds.IntersectRay(ray, out float enter) && enter <= focusDist + castPastFocus)
        {
            Vector3 hitPoint = ray.GetPoint(Mathf.Clamp(enter, 0f, focusDist + castPastFocus));
            if (IsHitInsideViewCone(hitPoint, cameraPos, focus, focusDist, shieldRadius, castPastFocus, coneRadialSlop))
                return true;
        }

        Vector3 c = bounds.center;
        Vector3 e = bounds.extents;
        for (int xi = -1; xi <= 1; xi += 2)
        {
            for (int yi = -1; yi <= 1; yi += 2)
            {
                for (int zi = -1; zi <= 1; zi += 2)
                {
                    Vector3 corner = c + new Vector3(e.x * xi, e.y * yi, e.z * zi);
                    Vector3 fromCam = corner - cameraPos;
                    float s = Vector3.Dot(fromCam, axis);
                    if (s < 0f || s > focusDist + castPastFocus)
                        continue;
                    if (IsHitInsideViewCone(corner, cameraPos, focus, focusDist, shieldRadius, castPastFocus, coneRadialSlop))
                        return true;
                }
            }
        }

        return false;
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

    /// <summary>
    /// True if <paramref name="worldPoint"/> lies inside the view cone: linear taper from camera (0 radius)
    /// to <paramref name="focusDist"/> (radius <paramref name="radiusAtFocus"/>), then constant radius until past-focus.
    /// </summary>
    private static bool IsHitInsideViewCone(
        Vector3 worldPoint,
        Vector3 cameraPos,
        Vector3 focusPos,
        float focusDist,
        float radiusAtFocus,
        float pastFocus,
        float radialSlop)
    {
        if (focusDist < 0.001f)
            return false;

        Vector3 axis = (focusPos - cameraPos) / focusDist;
        Vector3 fromCam = worldPoint - cameraPos;
        float s = Vector3.Dot(fromCam, axis);
        if (s < 0f)
            return false;

        float axialEnd = focusDist + Mathf.Max(0f, pastFocus);
        if (s > axialEnd)
            return false;

        Vector3 onAxis = cameraPos + axis * s;
        float radial = Vector3.Distance(worldPoint, onAxis);

        float maxR;
        if (s <= focusDist)
            maxR = radiusAtFocus * (s / focusDist);
        else
            maxR = radiusAtFocus;

        return radial <= maxR + radialSlop;
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

    private void OnDrawGizmos()
    {
        if (!drawDebugGizmos)
            return;
        if (gizmosOnlyInPlayMode && !Application.isPlaying)
            return;

        Camera camRef = _camera != null ? _camera : GetComponent<Camera>();
        if (camRef == null)
            return;

        Transform targetRef = followTarget;
        if (targetRef == null)
        {
            CameraFollow cf = _cameraFollow != null ? _cameraFollow : GetComponentInParent<CameraFollow>();
            if (cf != null)
                targetRef = cf.target;
        }
        if (targetRef == null)
            return;

        Vector3 origin = camRef.transform.position;
        Vector3 focus = targetRef.position + focusWorldOffset;
        Vector3 toFocus = focus - origin;
        if (toFocus.sqrMagnitude < 0.0025f)
            return;

        BuildFocusSamples(origin, focus, _focusSamples);

        Gizmos.color = new Color(0f, 1f, 1f, 0.95f);
        Gizmos.DrawWireSphere(origin, Mathf.Max(0.03f, losSampleCastRadius));

        for (int i = 0; i < _focusSamples.Count; i++)
        {
            Vector3 sample = _focusSamples[i];
            Gizmos.color = new Color(1f, 0.85f, 0.25f, 0.9f);
            Gizmos.DrawLine(origin, sample);
            Gizmos.DrawWireSphere(sample, 0.075f);
        }
    }

    private void CollectOccludersFromLineOfSightSamples(
        Vector3 cameraPos,
        Vector3 focus,
        float focusDist,
        int mask,
        QueryTriggerInteraction triggerMode)
    {
        BuildFocusSamples(cameraPos, focus, _focusSamples);

        for (int s = 0; s < _focusSamples.Count; s++)
        {
            Vector3 sample = _focusSamples[s];
            Vector3 ray = sample - cameraPos;
            float distance = ray.magnitude;
            if (distance < 0.05f)
                continue;

            Vector3 dir = ray / distance;
            int count;
            if (losSampleCastRadius > 0f)
            {
                count = Physics.SphereCastNonAlloc(
                    cameraPos,
                    losSampleCastRadius,
                    dir,
                    _hits,
                    distance + castPastFocus,
                    mask,
                    triggerMode);
            }
            else
            {
                count = Physics.RaycastNonAlloc(
                    cameraPos,
                    dir,
                    _hits,
                    distance + castPastFocus,
                    mask,
                    triggerMode);
            }

            if (count <= 0)
                continue;

            SortHitsByDistance(_hits, count);
            ProcessPhysicsHits(_hits, count, cameraPos, focus, focusDist);
        }
    }

    private void BuildFocusSamples(Vector3 cameraPos, Vector3 focus, List<Vector3> output)
    {
        output.Clear();

        Vector3 toFocus = focus - cameraPos;
        float focusDist = toFocus.magnitude;
        Vector3 fwd = toFocus.sqrMagnitude > 0.0001f ? toFocus.normalized : (_camera != null ? _camera.transform.forward : transform.forward);
        Vector3 right = Vector3.Cross(Vector3.up, fwd);
        if (right.sqrMagnitude < 0.0001f)
            right = _camera != null ? _camera.transform.right : transform.right;
        right.Normalize();
        Vector3 up = Vector3.Cross(fwd, right).normalized;

        void AddSilhouetteAt(Vector3 center)
        {
            output.Add(center);
            output.Add(center + right * targetSampleHalfWidth);
            output.Add(center - right * targetSampleHalfWidth);
            output.Add(center + up * targetSampleHalfHeight);
            output.Add(center - up * targetSampleHalfHeight);

            if (includeDiagonalSamples)
            {
                output.Add(center + right * targetSampleHalfWidth + up * targetSampleHalfHeight);
                output.Add(center + right * targetSampleHalfWidth - up * targetSampleHalfHeight);
                output.Add(center - right * targetSampleHalfWidth + up * targetSampleHalfHeight);
                output.Add(center - right * targetSampleHalfWidth - up * targetSampleHalfHeight);
            }
        }

        AddSilhouetteAt(focus);

        int rings = Mathf.Clamp(losAxisRingSamples, 0, 4);
        for (int ring = 1; ring <= rings; ring++)
        {
            float t = ring / (float)(rings + 1);
            Vector3 ringCenter = cameraPos + fwd * (focusDist * t);
            float ringScale = Mathf.Lerp(0.35f, 1f, t);
            float halfW = targetSampleHalfWidth * ringScale;
            float halfH = targetSampleHalfHeight * ringScale;

            output.Add(ringCenter);
            output.Add(ringCenter + right * halfW);
            output.Add(ringCenter - right * halfW);
            output.Add(ringCenter + up * halfH);
            output.Add(ringCenter - up * halfH);
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
    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    private static readonly int ColorId = Shader.PropertyToID("_Color");

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
            m.DisableKeyword("_ALPHATEST_ON");
            m.renderQueue = (int)RenderQueue.Transparent;
            return;
        }

        if (m.HasProperty(BaseColorId) || m.HasProperty(ColorId))
        {
            m.SetOverrideTag("RenderType", "Transparent");
            m.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
            m.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
            if (m.HasProperty(ZWrite))
                m.SetFloat(ZWrite, 0f);
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
