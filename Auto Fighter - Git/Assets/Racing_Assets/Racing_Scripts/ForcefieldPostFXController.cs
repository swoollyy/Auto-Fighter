using System.Collections;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

[DisallowMultipleComponent]
public sealed class ForcefieldPostFXController : MonoBehaviour
{
    [SerializeField, Tooltip("Try to find any PostProcessVolume in the scene if none assigned.")]
    private bool autoFindVolume = true;

    [Header("Setup (URP Volume)")]
    [SerializeField] private Volume volume;

    [Header("Lens Distortion (PPSv2 Funky)")]
    [SerializeField, Tooltip("[-100..100] (negative bulges outward, positive pin-cushions)")]
    [Range(-100f, 100f)] private float lensIntensity = -40f;
    [SerializeField, Tooltip("[0..1] – lower values = more zoomed-in distortion area")]
    [Range(0.01f, 1f)] private float lensScale = 0.85f;
    [SerializeField, Tooltip("[-1..1] normalized lens center X")]
    [Range(-1f, 1f)] private float lensCenterX = 0f;
    [SerializeField, Tooltip("[-1..1] normalized lens center Y")]
    [Range(-1f, 1f)] private float lensCenterY = 0f;

    [Header("Bloom")]
    [SerializeField, Range(0f, 5f)] private float bloomIntensity = 1.2f;

    [Header("Chromatic")]
    [SerializeField, Range(0f, 1f)] private float chromaticIntensity = 0.55f;

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

    private ChromaticAberration _ca;
    private LensDistortion _ld;
    private Bloom _bloom;

    private float _baseBloom = 1.3f;
    private bool _cachedBaseBloom;

    // Active stacking bursts. Each runs its own independent timeline; contributions are summed each frame.
    private sealed class FxBurst
    {
        public float chroma;
        public float lens;
        public float lensScale;
        public float centerX;
        public float centerY;
        public float bloomPeak;
        public float fadeIn;
        public float hold;
        public float fadeOut;
        public float elapsed;

        public float Total => fadeIn + hold + fadeOut;

        public float Envelope(AnimationCurve easeInCurve, AnimationCurve easeOutCurve)
        {
            if (elapsed < fadeIn)
            {
                float k = fadeIn > 0f ? Mathf.Clamp01(elapsed / fadeIn) : 1f;
                return easeInCurve != null ? Mathf.Clamp01(easeInCurve.Evaluate(k)) : k;
            }

            if (elapsed < fadeIn + hold)
                return 1f;

            float t = fadeOut > 0f ? Mathf.Clamp01((elapsed - fadeIn - hold) / fadeOut) : 1f;
            return easeOutCurve != null ? Mathf.Clamp01(easeOutCurve.Evaluate(t)) : (1f - t);
        }
    }

    private readonly System.Collections.Generic.List<FxBurst> _bursts = new System.Collections.Generic.List<FxBurst>(8);
    private float _wobbleClock;

    void Awake()
    {
        if (!volume && autoFindVolume)
            volume = FindObjectOfType<Volume>();

        EnsureSettings();
        SnapToBase();
    }

    void OnDisable()
    {
        _bursts.Clear();
        if (resetOnDisable) SnapToBase();
    }

    public void PlayBurst()
    {
        PlayBurst(fadeIn, hold, fadeOut);
    }

    /// <summary>
    /// Play a burst using the configured intensities (chroma/lens/scale/bloom) but with explicit,
    /// caller-controlled timing. Lets callers (e.g. the forcefield) ease the effect in/out smoothly
    /// instead of relying on the short default fade that reads as a snap.
    /// </summary>
    public void PlayBurst(float fadeInSeconds, float holdSeconds, float fadeOutSeconds)
    {
        if (!EnsureSettings()) return;
        _bursts.Add(new FxBurst
        {
            chroma = Mathf.Clamp01(chromaticIntensity),
            lens = Mathf.Clamp(lensIntensity, -100f, 100f),
            lensScale = lensScale,
            centerX = lensCenterX,
            centerY = lensCenterY,
            bloomPeak = bloomIntensity,
            fadeIn = Mathf.Max(0.01f, fadeInSeconds),
            hold = Mathf.Max(0f, holdSeconds),
            fadeOut = Mathf.Max(0.01f, fadeOutSeconds),
            elapsed = 0f
        });
    }

    // Public API to play a custom burst with overrides. Bursts STACK additively (no interrupt). Unscaled-time safe.
    public void PlayBurstCustom(float chromaIntensity, float lensIntensityOverride, float holdSeconds, float fadeInSeconds = -1f, float fadeOutSeconds = -1f)
    {
        if (!EnsureSettings()) return;
        _bursts.Add(new FxBurst
        {
            chroma = Mathf.Clamp01(chromaIntensity),
            lens = Mathf.Clamp(lensIntensityOverride, -100f, 100f),
            lensScale = lensScale,
            centerX = lensCenterX,
            centerY = lensCenterY,
            bloomPeak = bloomIntensity,
            fadeIn = fadeInSeconds > 0f ? fadeInSeconds : Mathf.Max(0.01f, fadeIn),
            hold = Mathf.Max(0f, holdSeconds),
            fadeOut = fadeOutSeconds > 0f ? fadeOutSeconds : Mathf.Max(0.01f, fadeOut),
            elapsed = 0f
        });
    }

    private void LateUpdate()
    {
        if (_bursts.Count == 0)
            return;

        if (!EnsureSettings())
        {
            _bursts.Clear();
            return;
        }

        float dt = Time.unscaledDeltaTime;

        float bloomAdd = 0f;
        float chromaSum = 0f;
        float lensSum = 0f;

        // Accumulate lens scale/center as an envelope-scaled DEVIATION FROM NEUTRAL (scale 1, center 0).
        // The old weighted-average approach held scale at ~0.85 even as a burst's envelope reached 0, so
        // when the finished burst was removed the scale snapped 0.85 -> 1.0 (the "snaps back to normal"
        // pop). By scaling every deviation by the envelope, all values glide back to neutral on their own
        // and removing a spent burst causes no discontinuity.
        float scaleDeviation = 0f;
        float centerX = 0f;
        float centerY = 0f;

        for (int i = _bursts.Count - 1; i >= 0; i--)
        {
            FxBurst b = _bursts[i];
            b.elapsed += dt;

            if (b.elapsed >= b.Total)
            {
                _bursts.RemoveAt(i);
                continue;
            }

            float e = b.Envelope(easeIn, easeOut);

            // Bloom is additive over the cached base (a single burst reproduces the old lerp base->peak).
            bloomAdd += (b.bloomPeak - _baseBloom) * e;
            chromaSum += b.chroma * e;
            lensSum += b.lens * e;

            scaleDeviation += (1f - b.lensScale) * e;
            centerX += b.centerX * e;
            centerY += b.centerY * e;
        }

        SetBloom(_baseBloom + bloomAdd);
        SetCA(chromaSum);

        float scale = Mathf.Clamp(1f - scaleDeviation, 0.01f, 1f);
        float cx = Mathf.Clamp(centerX, -1f, 1f);
        float cy = Mathf.Clamp(centerY, -1f, 1f);

        // Only wobble while there is actual distortion, so it fully settles as the effect eases out.
        if (wobbleCenter && wobbleAmplitude > 0f && wobbleFrequency > 0f && Mathf.Abs(lensSum) > 1e-4f)
        {
            _wobbleClock += dt;
            float wob = Mathf.Sin(_wobbleClock * Mathf.PI * 2f * wobbleFrequency) * wobbleAmplitude;
            cx = Mathf.Clamp(cx + wob, -1f, 1f);
            cy = Mathf.Clamp(cy - wob, -1f, 1f);
        }

        SetLD(lensSum, scale, cx, cy);

        // All bursts finished this frame -> settle exactly to base (values are already at neutral here,
        // so this is just a clean snap-to-exact and no longer produces a visible jump).
        if (_bursts.Count == 0)
        {
            _wobbleClock = 0f;
            SnapToBase();
        }
    }

    public void SetChromaticIntensity(float v) => chromaticIntensity = Mathf.Clamp01(v);
    public void SetLensParams(float intensity, float scale, float centerX, float centerY)
    {
        lensIntensity = Mathf.Clamp(intensity, -100f, 100f);
        lensScale = Mathf.Clamp(scale, 0.01f, 1f);
        lensCenterX = Mathf.Clamp(centerX, -1f, 1f);
        lensCenterY = Mathf.Clamp(centerY, -1f, 1f);
    }

    private void SnapToBase()
    {
        if (!EnsureSettings()) return;
        SetCA(0f);
        SetLD(0f, 1f, 0f, 0f);
        SetBloom(_baseBloom);
    }

    private void SetCA(float intensity)
    {
        if (_ca == null) return;

        _ca.intensity.overrideState = true;
        _ca.intensity.value = Mathf.Clamp01(intensity);
    }

    private void SetBloom(float intensity)
    {
        if (_bloom == null) return;
        _bloom.intensity.overrideState = true;
        _bloom.intensity.value = Mathf.Clamp(intensity, 0f, 20f);
    }

    private void SetLD(float intensity, float scale, float cx, float cy)
    {
        if (_ld == null) return;

        _ld.intensity.overrideState = true;
        _ld.intensity.value = Mathf.Clamp(intensity, -100f, 100f);

        _ld.scale.overrideState = true;
        _ld.scale.value = Mathf.Clamp(scale, 0.01f, 1f);

        _ld.center.overrideState = true;
        _ld.center.value = new Vector2(
            Mathf.Clamp(cx, -1f, 1f),
            Mathf.Clamp(cy, -1f, 1f)
        );
    }

    private bool EnsureSettings()
    {
        if (!volume) return false;

        // Create a runtime instance so we don't mutate the asset
        if (volume.profile == null && volume.sharedProfile != null)
            volume.profile = Instantiate(volume.sharedProfile);
        if (volume.profile == null) return false;

        EnsureOverridesExist(volume.profile);

        volume.profile.TryGet(out _ca);
        volume.profile.TryGet(out _ld);
        volume.profile.TryGet(out _bloom);

        if (_ca != null)
        {
            _ca.intensity.overrideState = true;
            _ca.intensity.value = 0f;
        }

        if (_ld != null)
        {
            _ld.intensity.overrideState = true;
            _ld.intensity.value = 0f;

            _ld.scale.overrideState = true;
            _ld.scale.value = 1f;

            _ld.center.overrideState = true;
            _ld.center.value = Vector2.zero;
        }

        if (_bloom != null)
        {
            _bloom.intensity.overrideState = true;

            // Cache whatever the volume currently uses as the "base" bloom once
            if (!_cachedBaseBloom)
            {
                _baseBloom = Mathf.Clamp(_bloom.intensity.value, 0f, 20f);
                if (_baseBloom <= 0f) _baseBloom = 1.3f; // fallback
                _cachedBaseBloom = true;
            }

            // Make sure we're sitting at base when not playing FX
            _bloom.intensity.value = _baseBloom;
        }


        return (_ca != null) || (_ld != null) || (_bloom != null);
    }

    private static void EnsureOverridesExist(VolumeProfile profile)
    {
        if (!profile.TryGet<ChromaticAberration>(out _)) profile.Add<ChromaticAberration>(true);
        if (!profile.TryGet<LensDistortion>(out _)) profile.Add<LensDistortion>(true);
        if (!profile.TryGet<Bloom>(out _)) profile.Add<Bloom>(true);
    }

}