using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Ground landing telegraph for thrown obstacles / bounce-backs.
/// Grows in opacity, color intensity, and brightness as impact approaches.
/// </summary>
public class URPDecalTelegraph : MonoBehaviour
{
    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    private static readonly int ColorId = Shader.PropertyToID("_Color");
    private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

    [SerializeField] private DecalProjector projector;

    [Tooltip("Vertical thickness of the projection volume (Y of DecalProjector.size).")]
    [SerializeField, Min(0.01f)] private float projectionHeight = 2.0f;

    [Tooltip("Tiny lift to avoid z-fighting on perfectly flat surfaces.")]
    [SerializeField] private float yOffset = 0.02f;

    [Header("Approach Intensity")]
    [Tooltip("Fade (opacity) when the telegraph first appears.")]
    [SerializeField, Range(0f, 1f)] private float startFade = 0.18f;

    [Tooltip("Fade (opacity) right before impact.")]
    [SerializeField, Range(0f, 1f)] private float endFade = 1f;

    [Tooltip("Color early in the approach (usually dimmer / cooler).")]
    [SerializeField, ColorUsage(true, true)] private Color startColor = new Color(1f, 0.55f, 0.12f, 1f);

    [Tooltip("Color at impact (hotter / more saturated).")]
    [SerializeField, ColorUsage(true, true)] private Color endColor = new Color(1f, 0.2f, 0.05f, 1f);

    [Tooltip("Brightness multiplier at spawn (1 = base color).")]
    [SerializeField, Min(0f)] private float startBrightness = 0.45f;

    [Tooltip("Brightness multiplier at impact (HDR-friendly; >1 makes it punchier).")]
    [SerializeField, Min(0f)] private float endBrightness = 2.4f;

    [Tooltip("Curve shaping for the ramp (x = time 0..1, y = intensity 0..1).")]
    [SerializeField] private AnimationCurve intensityCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

    [Tooltip("Also drive emission if the decal material has _EmissionColor.")]
    [SerializeField] private bool driveEmission = true;

    [SerializeField, Min(0f)] private float startEmission = 0.2f;
    [SerializeField, Min(0f)] private float endEmission = 3.5f;

    private Coroutine _co;
    private Material _runtimeMat;
    private bool _hasBaseColor;
    private bool _hasColor;
    private bool _hasEmission;

    private void Reset()
    {
        projector = GetComponent<DecalProjector>();
    }

    private void Awake()
    {
        EnsureProjectorAndMaterial();
    }

    private void OnDestroy()
    {
        if (_runtimeMat != null)
            Destroy(_runtimeMat);
    }

    public void SetWorldPose(Vector3 worldPos)
    {
        transform.position = worldPos + Vector3.up * yOffset;
        transform.rotation = Quaternion.Euler(90f, 0f, 0f); // ALWAYS forced
    }

    public void Play(float radius, float seconds, Action onComplete)
    {
        EnsureProjectorAndMaterial();
        if (projector == null)
        {
            onComplete?.Invoke();
            return;
        }

        float diameter = Mathf.Max(0.01f, radius * 2f);

        // URP DecalProjector uses size (X=width, Y=height, Z=projection depth)
        projector.size = new Vector3(diameter, Mathf.Max(0.01f, projectionHeight), diameter);

        projector.enabled = true;
        gameObject.SetActive(true);

        ApplyIntensity(0f);

        if (_co != null) StopCoroutine(_co);
        _co = StartCoroutine(Life(seconds, onComplete));
    }

    private void EnsureProjectorAndMaterial()
    {
        if (projector == null)
            projector = GetComponent<DecalProjector>();
        if (projector == null)
            return;

        if (_runtimeMat == null && projector.material != null)
        {
            _runtimeMat = new Material(projector.material);
            _runtimeMat.name = projector.material.name + " (Telegraph Instance)";
            projector.material = _runtimeMat;

            _hasBaseColor = _runtimeMat.HasProperty(BaseColorId);
            _hasColor = _runtimeMat.HasProperty(ColorId);
            _hasEmission = driveEmission && _runtimeMat.HasProperty(EmissionColorId);
        }
    }

    private IEnumerator Life(float seconds, Action onComplete)
    {
        if (seconds <= 0f)
        {
            ApplyIntensity(1f);
            onComplete?.Invoke();
            _co = null;
            yield break;
        }

        float elapsed = 0f;
        while (elapsed < seconds)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / seconds);
            float shaped = intensityCurve != null ? Mathf.Clamp01(intensityCurve.Evaluate(t)) : t;
            ApplyIntensity(shaped);
            yield return null;
        }

        ApplyIntensity(1f);
        onComplete?.Invoke();
        _co = null;
    }

    private void ApplyIntensity(float t01)
    {
        if (projector == null)
            return;

        t01 = Mathf.Clamp01(t01);

        projector.fadeFactor = Mathf.Lerp(startFade, endFade, t01);

        Color c = Color.Lerp(startColor, endColor, t01);
        float brightness = Mathf.Lerp(startBrightness, endBrightness, t01);
        Color lit = new Color(c.r * brightness, c.g * brightness, c.b * brightness, c.a);

        if (_runtimeMat != null)
        {
            if (_hasBaseColor)
                _runtimeMat.SetColor(BaseColorId, lit);
            if (_hasColor)
                _runtimeMat.SetColor(ColorId, lit);

            if (_hasEmission)
            {
                float e = Mathf.Lerp(startEmission, endEmission, t01);
                _runtimeMat.SetColor(EmissionColorId, lit * e);
                _runtimeMat.EnableKeyword("_EMISSION");
            }
        }
    }
}
