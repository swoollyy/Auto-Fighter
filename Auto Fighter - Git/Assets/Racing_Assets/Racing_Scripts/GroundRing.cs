using System;
using UnityEngine;

/// <summary>
/// Simple ground ring visual that expands / fades and invokes callback on complete.
/// Designed to be pooled. Color/brightness ramp up as impact approaches when a holdOverride
/// spans the full approach (thrown-obstacle telegraph).
/// </summary>
[DisallowMultipleComponent]
public class GroundRing : MonoBehaviour
{
    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    private static readonly int ColorId = Shader.PropertyToID("_Color");
    private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

    [Header("Visual")]
    [SerializeField] private Transform ringRoot;
    [SerializeField] private float baseScale = 1f;
    [SerializeField] private UnityEngine.UI.Image debugImage; // optional for UI prototypes
    [SerializeField] private Renderer ringRenderer;

    [Header("Approach Intensity")]
    [SerializeField, ColorUsage(true, true)] private Color startColor = new Color(1f, 0.55f, 0.12f, 0.35f);
    [SerializeField, ColorUsage(true, true)] private Color endColor = new Color(1f, 0.2f, 0.05f, 1f);
    [SerializeField, Min(0f)] private float startBrightness = 0.45f;
    [SerializeField, Min(0f)] private float endBrightness = 2.2f;
    [SerializeField] private AnimationCurve intensityCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

    [Header("Timing")]
    [SerializeField] private float fadeIn = 0.06f;
    [SerializeField] private float hold = 0.18f;
    [SerializeField] private float fadeOut = 0.25f;

    private Coroutine _cr;
    private MaterialPropertyBlock _mpb;
    private bool _hasBaseColor;
    private bool _hasColor;
    private bool _hasEmission;

    private void Awake()
    {
        if (ringRenderer == null)
            ringRenderer = GetComponentInChildren<Renderer>();

        if (ringRenderer != null && ringRenderer.sharedMaterial != null)
        {
            var mat = ringRenderer.sharedMaterial;
            _hasBaseColor = mat.HasProperty(BaseColorId);
            _hasColor = mat.HasProperty(ColorId);
            _hasEmission = mat.HasProperty(EmissionColorId);
        }

        _mpb = new MaterialPropertyBlock();
    }

    // Backwards-compatible Play: optional holdOverride (in seconds)
    public void Play(float radius, Action onComplete = null, float? holdOverride = null)
    {
        gameObject.SetActive(true);
        transform.localScale = Vector3.one * (radius * 0.02f); // scale tweak: ring prefab expects 1==1m maybe adjust
        ApplyIntensity(0f);
        if (_cr != null) StopCoroutine(_cr);
        _cr = StartCoroutine(PlayRoutine(radius, onComplete, holdOverride));
    }

    private System.Collections.IEnumerator PlayRoutine(float radius, Action onComplete, float? holdOverride)
    {
        // Allow override of the serialized hold time if provided
        float actualHold = holdOverride.HasValue ? Mathf.Max(0f, holdOverride.Value) : hold;

        float startScale = transform.localScale.x;
        float peakScale = radius * 0.5f; // visual scale; adjust if needed

        // fade in
        float elapsed = 0f;
        while (elapsed < fadeIn)
        {
            elapsed += Time.deltaTime;
            float k = Mathf.Clamp01(elapsed / fadeIn);
            transform.localScale = Vector3.one * Mathf.Lerp(startScale, peakScale, k);
            ApplyIntensity(EvaluateIntensity(k * 0.15f)); // slight early ramp during expand
            yield return null;
        }

        // hold — ramp intensity toward impact over the full approach window
        elapsed = 0f;
        float holdDenom = Mathf.Max(0.0001f, actualHold);
        while (elapsed < actualHold)
        {
            elapsed += Time.deltaTime;
            float approachT = Mathf.Clamp01(elapsed / holdDenom);
            ApplyIntensity(EvaluateIntensity(Mathf.Lerp(0.15f, 1f, approachT)));
            yield return null;
        }

        ApplyIntensity(1f);

        // fade out and shrink
        elapsed = 0f;
        while (elapsed < fadeOut)
        {
            elapsed += Time.deltaTime;
            float k = Mathf.Clamp01(elapsed / fadeOut);
            transform.localScale = Vector3.one * Mathf.Lerp(peakScale, startScale * 0.2f, k);
            ApplyIntensity(1f - k);
            yield return null;
        }

        onComplete?.Invoke();
        _cr = null;
    }

    private float EvaluateIntensity(float t01)
    {
        t01 = Mathf.Clamp01(t01);
        return intensityCurve != null ? Mathf.Clamp01(intensityCurve.Evaluate(t01)) : t01;
    }

    private void ApplyIntensity(float t01)
    {
        t01 = Mathf.Clamp01(t01);
        Color c = Color.Lerp(startColor, endColor, t01);
        float brightness = Mathf.Lerp(startBrightness, endBrightness, t01);
        Color lit = new Color(c.r * brightness, c.g * brightness, c.b * brightness, c.a);

        if (debugImage != null)
            debugImage.color = lit;

        if (ringRenderer == null)
            return;

        if (_mpb == null)
            _mpb = new MaterialPropertyBlock();

        ringRenderer.GetPropertyBlock(_mpb);

        if (_hasBaseColor)
            _mpb.SetColor(BaseColorId, lit);
        if (_hasColor)
            _mpb.SetColor(ColorId, lit);
        if (_hasEmission)
            _mpb.SetColor(EmissionColorId, lit * Mathf.Lerp(0.2f, 3f, t01));

        ringRenderer.SetPropertyBlock(_mpb);
    }
}
