using System.Collections;
using UnityEngine;

/// <summary>
/// Road-width finish portal. Visuals are scene-authored (Racing → Setup Finish Portal System).
/// Runtime only places/sizes/animates — does not create quads.
/// </summary>
[DisallowMultipleComponent]
public class FinishPortalGate : MonoBehaviour
{
    [Header("Sizing")]
    [SerializeField, Min(0.5f)] private float height = 6f;
    [SerializeField, Min(0.05f)] private float depth = 1.2f;
    [SerializeField, Min(0f)] private float widthPadding = 0.35f;
    [SerializeField, Min(0f)] private float forwardOffset = 1.5f;

    [Header("Visual Refs (scene)")]
    [SerializeField] private Transform visual;
    [SerializeField] private Transform swirl;
    [SerializeField] private MeshRenderer visualRenderer;
    [SerializeField] private MeshRenderer swirlRenderer;
    [SerializeField] private BoxCollider trigger;

    [Header("Visual")]
    [SerializeField] private Color portalTintA = new Color(0.15f, 1.4f, 2.2f, 0.85f);
    [SerializeField] private Color portalTintB = new Color(1.8f, 0.2f, 1.6f, 0.85f);
    [SerializeField, Min(0.1f)] private float colorPulseSpeed = 1.6f;
    [SerializeField, Min(0f)] private float spinDegreesPerSecond = 35f;
    [SerializeField, Range(0.05f, 1f)] private float visualBaseAlpha = 0.55f;
    [SerializeField, Range(0.05f, 1f)] private float swirlBaseAlpha = 0.35f;

    [Header("Exit")]
    [SerializeField, Min(0.05f)] private float defaultExitDuration = 0.9f;

    private FinishPortalDirector _director;
    private MaterialPropertyBlock _mpb;
    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    private static readonly int ColorId = Shader.PropertyToID("_Color");
    private float _roadWidth = 4f;
    private bool _armed = true;
    private bool _exiting;
    private Coroutine _exitCr;
    private Vector3 _visualBaseScale = Vector3.one;
    private Vector3 _swirlBaseScale = Vector3.one;

    public void Initialize(FinishPortalDirector director)
    {
        _director = director;
        ResolveVisuals();
        EnsureMaterials();
    }

#if UNITY_EDITOR
    /// <summary>Called by FinishPortalSystemSetup after creating child quads.</summary>
    public void EditorAssignBuiltVisuals()
    {
        if (trigger == null)
            trigger = GetComponent<BoxCollider>();
        if (trigger == null)
            trigger = gameObject.AddComponent<BoxCollider>();
        trigger.isTrigger = true;

        if (visual == null)
        {
            var t = transform.Find("PortalVisual");
            if (t != null) visual = t;
        }
        if (swirl == null)
        {
            var t = transform.Find("PortalSwirl");
            if (t != null) swirl = t;
        }

        if (visual != null && visualRenderer == null)
            visualRenderer = visual.GetComponent<MeshRenderer>();
        if (swirl != null && swirlRenderer == null)
            swirlRenderer = swirl.GetComponent<MeshRenderer>();

        EnsureMaterials();
        UnityEditor.EditorUtility.SetDirty(this);
    }
#endif

    private void ResolveVisuals()
    {
        if (trigger == null)
            trigger = GetComponent<BoxCollider>();
        if (visual == null)
        {
            var t = transform.Find("PortalVisual");
            if (t != null) visual = t;
        }
        if (swirl == null)
        {
            var t = transform.Find("PortalSwirl");
            if (t != null) swirl = t;
        }
        if (visual != null && visualRenderer == null)
            visualRenderer = visual.GetComponent<MeshRenderer>();
        if (swirl != null && swirlRenderer == null)
            swirlRenderer = swirl.GetComponent<MeshRenderer>();
    }

    private void EnsureMaterials()
    {
        if (visualRenderer != null && visualRenderer.sharedMaterial == null)
            visualRenderer.sharedMaterial = CreatePortalMaterial(portalTintA, visualBaseAlpha);
        if (swirlRenderer != null && swirlRenderer.sharedMaterial == null)
            swirlRenderer.sharedMaterial = CreatePortalMaterial(portalTintB, swirlBaseAlpha);

        if (visualRenderer != null)
        {
            visualRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            visualRenderer.receiveShadows = false;
        }
        if (swirlRenderer != null)
        {
            swirlRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            swirlRenderer.receiveShadows = false;
        }
    }
     
    /// <param name="approachCatchMeters">
    /// Extra trigger depth along the approach (behind the portal face). Use for end portals so a
    /// thin gate past the cliff cannot be missed. &lt;= 0 keeps authored <see cref="depth"/>.
    /// </param>
    public void PlaceAtTrackEnd(Vector3 endPos, Vector3 endForward, float roadWidth, float approachCatchMeters = -1f)
    {
        ResolveVisuals();
        EnsureMaterials();
        if (_exitCr != null)
        {
            StopCoroutine(_exitCr);
            _exitCr = null;
        }
        _exiting = false;
        _armed = true;
        _roadWidth = Mathf.Max(1f, roadWidth);

        Vector3 fwd = endForward.sqrMagnitude > 1e-6f ? endForward.normalized : Vector3.forward;
        fwd.y = 0f;
        if (fwd.sqrMagnitude < 1e-6f) fwd = Vector3.forward;
        fwd.Normalize();

        transform.position = endPos + fwd * forwardOffset + Vector3.up * (height * 0.45f);
        transform.rotation = Quaternion.LookRotation(fwd, Vector3.up);

        float width = _roadWidth + widthPadding * 2f;
        float triggerDepth = depth;
        float triggerCenterZ = 0f;
        if (approachCatchMeters > depth)
        {
            // Keep the visible face near endPos, but extend the trigger backward over the approach.
            triggerDepth = approachCatchMeters;
            triggerCenterZ = -(approachCatchMeters - depth) * 0.5f;
        }

        if (trigger != null)
        {
            trigger.isTrigger = true;
            trigger.enabled = true;
            trigger.size = new Vector3(width, height, triggerDepth);
            trigger.center = new Vector3(0f, 0f, triggerCenterZ);
        }

        _visualBaseScale = new Vector3(width, height, 1f);
        _swirlBaseScale = new Vector3(width * 0.92f, height * 0.92f, 1f);
        if (visual != null)
            visual.localScale = _visualBaseScale;
        if (swirl != null)
            swirl.localScale = _swirlBaseScale;

        gameObject.SetActive(true);
    }

    public void Disarm()
    {
        _armed = false;
        if (trigger != null)
            trigger.enabled = false;
    }

    public void Rearm()
    {
        if (_exiting) return;
        _armed = true;
        if (trigger != null)
            trigger.enabled = true;
    }

    public void PlayExitAnimation(float duration = -1f)
    {
        Disarm();
        if (_exitCr != null)
            StopCoroutine(_exitCr);
        _exitCr = StartCoroutine(CoExit(duration > 0f ? duration : defaultExitDuration));
    }

    private IEnumerator CoExit(float duration)
    {
        _exiting = true;
        duration = Mathf.Max(0.05f, duration);
        float t0 = Time.unscaledTime;
        float t1 = t0 + duration;

        Vector3 v0 = visual != null ? visual.localScale : _visualBaseScale;
        Vector3 s0 = swirl != null ? swirl.localScale : _swirlBaseScale;

        while (Time.unscaledTime < t1)
        {
            float u = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(t0, t1, Time.unscaledTime));
            float scaleMul = Mathf.Lerp(1f, 0.05f, u);
            float alphaMul = 1f - u;

            if (visual != null)
                visual.localScale = v0 * scaleMul;
            if (swirl != null)
            {
                swirl.localScale = s0 * scaleMul;
                swirl.Rotate(0f, 0f, spinDegreesPerSecond * 3f * Time.unscaledDeltaTime, Space.Self);
            }

            Color c = Color.Lerp(portalTintA, portalTintB, 0.5f);
            c.a = visualBaseAlpha * alphaMul;
            ApplyTint(visualRenderer, c);
            Color c2 = Color.Lerp(portalTintB, portalTintA, 0.5f);
            c2.a = swirlBaseAlpha * alphaMul;
            ApplyTint(swirlRenderer, c2);

            yield return null;
        }

        _exitCr = null;
        gameObject.SetActive(false);
        _exiting = false;
    }

    private static Material CreatePortalMaterial(Color tint, float alpha)
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null)
            shader = Shader.Find("Unlit/Color");
        if (shader == null)
            shader = Shader.Find("Sprites/Default");

        var mat = new Material(shader);
        tint.a = alpha;
        if (mat.HasProperty(BaseColorId))
            mat.SetColor(BaseColorId, tint);
        if (mat.HasProperty(ColorId))
            mat.SetColor(ColorId, tint);

        if (mat.HasProperty("_Surface"))
            mat.SetFloat("_Surface", 1f);
        mat.renderQueue = 3000;
        return mat;
    }

    private void Update()
    {
        if (_exiting) return;
        if (!gameObject.activeInHierarchy) return;

        if (swirl != null && spinDegreesPerSecond != 0f)
            swirl.Rotate(0f, 0f, spinDegreesPerSecond * Time.deltaTime, Space.Self);

        float t = 0.5f + 0.5f * Mathf.Sin(Time.time * colorPulseSpeed);
        Color c = Color.Lerp(portalTintA, portalTintB, t);
        c.a = visualBaseAlpha;
        ApplyTint(visualRenderer, c);
        Color c2 = Color.Lerp(portalTintB, portalTintA, t);
        c2.a = swirlBaseAlpha * 0.75f;
        ApplyTint(swirlRenderer, c2);
    }

    private void ApplyTint(MeshRenderer rend, Color c)
    {
        if (rend == null) return;
        if (_mpb == null) _mpb = new MaterialPropertyBlock();
        rend.GetPropertyBlock(_mpb);
        if (rend.sharedMaterial != null && rend.sharedMaterial.HasProperty(BaseColorId))
            _mpb.SetColor(BaseColorId, c);
        if (rend.sharedMaterial != null && rend.sharedMaterial.HasProperty(ColorId))
            _mpb.SetColor(ColorId, c);
        rend.SetPropertyBlock(_mpb);
    }

    private void OnTriggerEnter(Collider other) => TryNotifyEnter(other);

    // Rearm while the car is still overlapping must still be able to finish the run —
    // OnTriggerEnter alone will not fire again until exit+re-enter.
    private void OnTriggerStay(Collider other) => TryNotifyEnter(other);

    private void TryNotifyEnter(Collider other)
    {
        if (!_armed || _director == null || _exiting) return;
        if (!IsActiveCar(other)) return;
        _director.NotifyPortalEntered(this);
    }

    private static bool IsActiveCar(Collider other)
    {
        if (other == null) return false;

        var car = other.GetComponentInParent<CarController>();
        if (car == null) return false;

        var gm = GameManager_Racing.Instance;
        if (gm == null)
            return true; // best-effort if GM missing

        // Prefer ActiveCar, but don't miss the finish if the ref briefly lags a respawn.
        if (gm.ActiveCar != null)
            return car == gm.ActiveCar;

        return gm.IsGameplayLive;
    }
}
