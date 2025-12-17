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
        _a = a; _b = b;
        ApplyPositions();
    }

    public void FadeIn(float seconds = -1f) => FadeTo(1f, seconds);
    public void FadeOut(float seconds = -1f) => FadeTo(0f, seconds);

    public void FadeTo(float targetAlpha, float seconds = -1f)
    {
        if (seconds <= 0f) seconds = defaultFadeSeconds;

        if (_fadeCo != null) StopCoroutine(_fadeCo);
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
        // If the obstacle is moving/rotating and you want the line to “track” its endpoints,
        // call SetEndpoints each frame from the owner. Otherwise this is fine for static endpoints.
        ApplyPositions();
    }

    private void ApplyPositions()
    {
        if (_lr == null) return;

        Vector3 p0 = _a; p0.y += yOffset;
        Vector3 p1 = _b; p1.y += yOffset;

        _lr.SetPosition(0, p0);
        _lr.SetPosition(1, p1);
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
