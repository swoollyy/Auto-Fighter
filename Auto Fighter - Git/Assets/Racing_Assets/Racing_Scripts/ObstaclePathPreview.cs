using System.Collections;
using UnityEngine;

[RequireComponent(typeof(LineRenderer))]
public class ObstaclePathPreview : MonoBehaviour
{
    [Header("Line")]
    [SerializeField] private float width = 0.15f;
    [SerializeField] private float yOffset = 0.05f;
    [SerializeField] private bool useWorldSpace = true;

    [Header("Fade")]
    [SerializeField] private float defaultFadeSeconds = 0.25f;

    private LineRenderer _lr;
    private Coroutine _fadeCo;

    private Vector3 _a, _b;
    private float _alpha = 1f;

    private bool _usePolyline;
    private Vector3[] _polylinePts = new Vector3[64];
    private int _polylineCount;

    private void Awake()
    {
        _lr = GetComponent<LineRenderer>();
        _lr.useWorldSpace = useWorldSpace;
        _lr.positionCount = 2;
        _lr.widthMultiplier = width;

        // IMPORTANT: line material must support transparency.
        if (_lr.material == null)
            _lr.material = new Material(Shader.Find("Sprites/Default"));
    }

    public void SetEndpoints(Vector3 a, Vector3 b)
    {
        _usePolyline = false;
        _a = a; _b = b;
        if (_lr != null)
            _lr.positionCount = 2;
        ApplyPositions();
    }

    /// <summary>World-space path (e.g. draped on terrain). Count must be >= 2.</summary>
    public void SetPolylineWorld(Vector3[] worldPoints, int count)
    {
        if (worldPoints == null || count < 2) return;
        _usePolyline = true;
        _polylineCount = count;
        if (_polylinePts == null || _polylinePts.Length < count)
            _polylinePts = new Vector3[Mathf.NextPowerOfTwo(count)];
        for (int i = 0; i < count; i++)
            _polylinePts[i] = worldPoints[i];
        if (_lr != null)
            _lr.positionCount = count;
        ApplyPositions();
    }

    public void SetYOffset(float worldYOffset)
    {
        yOffset = worldYOffset;
        ApplyPositions();
    }

    public void FadeIn(float seconds = -1f) => FadeTo(1f, seconds);
    public void FadeOut(float seconds = -1f) => FadeTo(0f, seconds);

    public void FadeTo(float targetAlpha, float seconds = -1f)
    {
        if (seconds <= 0f) seconds = defaultFadeSeconds;

        if (_fadeCo != null) StopCoroutine(_fadeCo);
        if(this.gameObject.activeInHierarchy)
            _fadeCo = StartCoroutine(FadeRoutine(targetAlpha, seconds));
    }

    private IEnumerator FadeRoutine(float target, float seconds)
    {
        float start = _alpha;
        float t = 0f;

        while (t < 1f)
        {
            t += Time.deltaTime / Mathf.Max(0.0001f, seconds);
            _alpha = Mathf.Lerp(start, target, Mathf.Clamp01(t));
            ApplyAlpha();
            yield return null;
        }

        _alpha = target;
        ApplyAlpha();
    }

    private void LateUpdate()
    {
        // If the obstacle is moving/rotating and you want the line to �track� its endpoints,
        // call SetEndpoints each frame from the owner. Otherwise this is fine for static endpoints.
        ApplyPositions();
    }

    private void ApplyPositions()
    {
        if (_lr == null) return;

        if (_usePolyline)
        {
            for (int i = 0; i < _polylineCount; i++)
            {
                Vector3 p = _polylinePts[i];
                p.y += yOffset;
                _lr.SetPosition(i, p);
            }
            return;
        }

        Vector3 p0 = _a; p0.y += yOffset;
        Vector3 p1 = _b; p1.y += yOffset;

        _lr.SetPosition(0, p0);
        _lr.SetPosition(1, p1);
    }

    private void OnDisable()
    {
        if (_fadeCo != null) StopCoroutine(_fadeCo);
        _fadeCo = null;

        _alpha = 0f;
        ApplyAlpha();

        if (_lr) _lr.enabled = false;   // hard off
    }

    private void OnEnable()
    {
        if (_lr) _lr.enabled = true;
    }

    private void ApplyAlpha()
    {
        if (_lr == null) return;

        // Preserve existing color, just change alpha
        Color c0 = _lr.startColor; c0.a = _alpha;
        Color c1 = _lr.endColor; c1.a = _alpha;
        _lr.startColor = c0;
        _lr.endColor = c1;

        // Also push alpha into material color (Sprites/Default uses _Color)
        if (_lr.material && _lr.material.HasProperty("_Color"))
        {
            Color mc = _lr.material.color;
            mc.a = _alpha;
            _lr.material.color = mc;
        }
    }
}
