using System.Collections;
using UnityEngine;
using UnityEngine.Rendering.PostProcessing; // PPSv2

[DisallowMultipleComponent]
public sealed class ForcefieldPostFXController : MonoBehaviour
{
    [Header("Setup (PPSv2)")]
    [SerializeField] private PostProcessVolume volume;
    [SerializeField, Tooltip("Try to find any PostProcessVolume in the scene if none assigned.")]
    private bool autoFindVolume = true;

    [Header("Chromatic Aberration (PPSv2)")]
    [SerializeField, Range(0f, 1f)] private float chromaticIntensity = 0.6f;

    [Header("Lens Distortion (PPSv2 Funky)")]
    [SerializeField, Tooltip("[-100..100] (negative bulges outward, positive pin-cushions)")]
    [Range(-100f, 100f)] private float lensIntensity = -40f;
    [SerializeField, Tooltip("[0..1] – lower values = more zoomed-in distortion area")]
    [Range(0.01f, 1f)] private float lensScale = 0.85f;
    [SerializeField, Tooltip("[-1..1] normalized lens center X")]
    [Range(-1f, 1f)] private float lensCenterX = 0f;
    [SerializeField, Tooltip("[-1..1] normalized lens center Y")]
    [Range(-1f, 1f)] private float lensCenterY = 0f;

    [Header("Funky Wobble (optional)")]
    [SerializeField] private bool wobbleCenter = true;
    [SerializeField, Tooltip("Center wobble amplitude (normalized)")]
    [Range(0f, 0.5f)] private float wobbleAmplitude = 0.06f;
    [SerializeField, Tooltip("Center wobble frequency (Hz)")]
    [Range(1f, 40f)] private float wobbleFrequency = 12f;

    [Header("Timing (unscaled, slow‑mo safe)")]
    [SerializeField, UnityEngine.Min(0.01f)] private float fadeIn = 0.08f;
    [SerializeField, UnityEngine.Min(0f)] private float hold = 0.18f;
    [SerializeField, UnityEngine.Min(0.01f)] private float fadeOut = 0.22f;

    [Header("Curves")]
    [SerializeField] private AnimationCurve easeIn = AnimationCurve.EaseInOut(0, 0, 1, 1);
    [SerializeField] private AnimationCurve easeOut = AnimationCurve.EaseInOut(0, 1, 1, 0);

    [Header("Cleanup")]
    [SerializeField] private bool resetOnDisable = true;

    // PPSv2 overrides
    private ChromaticAberration _ca;
    private LensDistortion _ld;

    private Coroutine _fxCR;

    void Awake()
    {
        if (!volume && autoFindVolume)
            volume = FindObjectOfType<PostProcessVolume>();

        EnsureSettings();
        SnapToBase();
    }

    void OnDisable()
    {
        if (resetOnDisable) SnapToBase();
        if (_fxCR != null)
        {
            StopCoroutine(_fxCR);
            _fxCR = null;
        }
    }

    public void PlayBurst()
    {
        if (!EnsureSettings()) return;
        if (_fxCR != null) StopCoroutine(_fxCR);
        _fxCR = StartCoroutine(BurstRoutine());
    }

    // NEW: public API to play a custom burst with overrides (unscaled time safe).
    public void PlayBurstCustom(float chromaIntensity, float lensIntensityOverride, float holdSeconds, float fadeInSeconds = -1f, float fadeOutSeconds = -1f)
    {
        if (!EnsureSettings()) return;
        if (_fxCR != null) StopCoroutine(_fxCR);
        float fi = fadeInSeconds > 0f ? fadeInSeconds : fadeIn;
        float fo = fadeOutSeconds > 0f ? fadeOutSeconds : fadeOut;
        _fxCR = StartCoroutine(BurstRoutineCustom(chromaIntensity, lensIntensityOverride, holdSeconds, fi, fo));
    }

    public void SetChromaticIntensity(float v) => chromaticIntensity = Mathf.Clamp01(v);
    public void SetLensParams(float intensity, float scale, float centerX, float centerY)
    {
        lensIntensity = Mathf.Clamp(intensity, -100f, 100f);
        lensScale = Mathf.Clamp(scale, 0.01f, 1f);
        lensCenterX = Mathf.Clamp(centerX, -1f, 1f);
        lensCenterY = Mathf.Clamp(centerY, -1f, 1f);
    }

    private IEnumerator BurstRoutine()
    {
        // Start from base
        SetCA(0f);
        SetLD(0f, 1f, 0f, 0f);

        // Fade in
        float t = 0f;
        while (t < fadeIn)
        {
            t += Time.unscaledDeltaTime;
            float k = Mathf.Clamp01(t / fadeIn);
            float e = easeIn != null ? Mathf.Clamp01(easeIn.Evaluate(k)) : k;

            SetCA(Mathf.Lerp(0f, chromaticIntensity, e));
            SetLD(Mathf.Lerp(0f, lensIntensity, e),
                  Mathf.Lerp(1f, lensScale, e),
                  Mathf.Lerp(0f, lensCenterX, e),
                  Mathf.Lerp(0f, lensCenterY, e));
            yield return null;
        }

        // Hold (optional wobble)
        float wobbleT = 0f;
        float endHold = Time.unscaledTime + hold;
        while (Time.unscaledTime < endHold)
        {
            if (wobbleCenter && wobbleAmplitude > 0f && wobbleFrequency > 0f)
            {
                wobbleT += Time.unscaledDeltaTime;
                float wob = Mathf.Sin(wobbleT * Mathf.PI * 2f * wobbleFrequency) * wobbleAmplitude;
                SetCA(chromaticIntensity);
                SetLD(lensIntensity, lensScale,
                      Mathf.Clamp(lensCenterX + wob, -1f, 1f),
                      Mathf.Clamp(lensCenterY - wob, -1f, 1f));
            }
            else
            {
                SetCA(chromaticIntensity);
                SetLD(lensIntensity, lensScale, lensCenterX, lensCenterY);
            }
            yield return null;
        }

        // Fade out
        t = 0f;
        while (t < fadeOut)
        {
            t += Time.unscaledDeltaTime;
            float k = Mathf.Clamp01(t / fadeOut);
            float e = easeOut != null ? Mathf.Clamp01(easeOut.Evaluate(k)) : (1f - k);

            SetCA(Mathf.Lerp(chromaticIntensity, 0f, k));
            SetLD(Mathf.Lerp(lensIntensity, 0f, k),
                  Mathf.Lerp(lensScale, 1f, k),
                  Mathf.Lerp(lensCenterX, 0f, k),
                  Mathf.Lerp(lensCenterY, 0f, k));
            yield return null;
        }

        // Back to base
        SnapToBase();
        _fxCR = null;
    }

    // Custom burst coroutine using explicit passed values
    private IEnumerator BurstRoutineCustom(float chroma, float lensInt, float holdSeconds, float fadeInSeconds, float fadeOutSeconds)
    {
        // local copies
        float localChroma = Mathf.Clamp01(chroma);
        float localLens = Mathf.Clamp(lensInt, -100f, 100f);
        float localLensScale = Mathf.Clamp(lensScale, 0.01f, 1f);
        float localCenterX = lensCenterX;
        float localCenterY = lensCenterY;
        float fi = Mathf.Max(0.01f, fadeInSeconds);
        float fo = Mathf.Max(0.01f, fadeOutSeconds);
        AnimationCurve easeInLocal = easeIn;
        AnimationCurve easeOutLocal = easeOut;
        bool wobble = wobbleCenter;
        float wobAmp = wobbleAmplitude;
        float wobFreq = wobbleFrequency;

        // start
        SetCA(0f);
        SetLD(0f, 1f, 0f, 0f);

        // fade in
        float t = 0f;
        while (t < fi)
        {
            t += Time.unscaledDeltaTime;
            float k = Mathf.Clamp01(t / fi);
            float e = easeInLocal != null ? Mathf.Clamp01(easeInLocal.Evaluate(k)) : k;
            SetCA(Mathf.Lerp(0f, localChroma, e));
            SetLD(Mathf.Lerp(0f, localLens, e),
                  Mathf.Lerp(1f, localLensScale, e),
                  Mathf.Lerp(0f, localCenterX, e),
                  Mathf.Lerp(0f, localCenterY, e));
            yield return null;
        }

        // hold
        float wobT = 0f;
        float endHold = Time.unscaledTime + Mathf.Max(0f, holdSeconds);
        while (Time.unscaledTime < endHold)
        {
            if (wobble && wobAmp > 0f && wobFreq > 0f)
            {
                wobT += Time.unscaledDeltaTime;
                float wob = Mathf.Sin(wobT * Mathf.PI * 2f * wobFreq) * wobAmp;
                SetCA(localChroma);
                SetLD(localLens, localLensScale,
                    Mathf.Clamp(localCenterX + wob, -1f, 1f),
                    Mathf.Clamp(localCenterY - wob, -1f, 1f));
            }
            else
            {
                SetCA(localChroma);
                SetLD(localLens, localLensScale, localCenterX, localCenterY);
            }
            yield return null;
        }

        // fade out
        t = 0f;
        while (t < fo)
        {
            t += Time.unscaledDeltaTime;
            float k = Mathf.Clamp01(t / fo);
            float e = easeOutLocal != null ? Mathf.Clamp01(easeOutLocal.Evaluate(k)) : (1f - k);
            SetCA(Mathf.Lerp(localChroma, 0f, k));
            SetLD(Mathf.Lerp(localLens, 0f, k),
                  Mathf.Lerp(localLensScale, 1f, k),
                  Mathf.Lerp(localCenterX, 0f, k),
                  Mathf.Lerp(localCenterY, 0f, k));
            yield return null;
        }

        SnapToBase();
        _fxCR = null;
    }

    private void SnapToBase()
    {
        if (!EnsureSettings()) return;
        SetCA(0f);
        SetLD(0f, 1f, 0f, 0f);
    }

    // PPSv2 setters
    private void SetCA(float intensity)
    {
        if (_ca == null) return;
        _ca.enabled.value = true;
        _ca.intensity.overrideState = true;
        _ca.intensity.value = Mathf.Clamp01(intensity);
    }

    private void SetLD(float intensity, float scale, float cx, float cy)
    {
        if (_ld == null) return;
        _ld.enabled.value = true;

        _ld.intensity.overrideState = true;
        _ld.intensity.value = Mathf.Clamp(intensity, -100f, 100f);

        _ld.scale.overrideState = true;
        _ld.scale.value = Mathf.Clamp(scale, 0.01f, 1f);

        _ld.centerX.overrideState = true;
        _ld.centerX.value = Mathf.Clamp(cx, -1f, 1f);

        _ld.centerY.overrideState = true;
        _ld.centerY.value = Mathf.Clamp(cy, -1f, 1f);
    }

    private bool EnsureSettings()
    {
        if (!volume || !volume.profile) return false;

        // Chromatic Aberration
        if (!volume.profile.TryGetSettings(out _ca))
        {
            _ca = volume.profile.AddSettings<ChromaticAberration>();
        }
        _ca.enabled.overrideState = true;
        _ca.enabled.value = true;
        _ca.intensity.overrideState = true;
        _ca.intensity.value = 0f;
        _ca.fastMode.overrideState = true;
        _ca.fastMode.value = true;

        // Lens Distortion
        if (!volume.profile.TryGetSettings(out _ld))
        {
            _ld = volume.profile.AddSettings<LensDistortion>();
        }
        _ld.enabled.overrideState = true;
        _ld.enabled.value = true;

        _ld.intensity.overrideState = true;
        _ld.intensity.value = 0f;

        _ld.scale.overrideState = true;
        _ld.scale.value = 1f;

        _ld.centerX.overrideState = true;
        _ld.centerX.value = 0f;

        _ld.centerY.overrideState = true;
        _ld.centerY.value = 0f;

        return true;
    }
}