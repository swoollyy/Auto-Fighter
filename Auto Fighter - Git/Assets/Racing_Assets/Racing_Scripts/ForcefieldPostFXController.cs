using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

[DisallowMultipleComponent]
public sealed class ForcefieldPostFXController : MonoBehaviour
{
    private const float BloomHardMax = 800f;

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
    private ColorAdjustments _colorAdj;

    private float _baseBloom = 1.3f;
    private bool _cachedBaseBloom;
    private float _baseSaturation;
    private bool _cachedBaseSaturation;
    private float _baseHueShift;
    private bool _cachedBaseHueShift;

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
        public bool allowWobble = true;

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

    private readonly List<FxBurst> _bursts = new List<FxBurst>(8);
    private float _wobbleClock;

    // Sustained finish-portal punch (bloom + saturation + hue). Holds at full until cleared/reset.
    private bool _portalHoldActive;
    private bool _portalHoldFadingOut;
    private bool _portalHoldLatched;
    private float _portalHoldWeight;
    private float _portalBloomTarget = 80f;
    private float _portalSaturationTarget = 60f;
    private float _portalHueMin = -50f;
    private float _portalHueMax = 50f;
    private float _portalHueScrollSpeed = 2.5f; // full min↔max cycles per second
    private float _portalHueClock;
    private float _portalFadeDuration = 0.35f;
    private float _portalFadeElapsed;
    private float _portalFadeOutFrom = 1f;

    void Awake()
    {
        if (!volume && autoFindVolume)
            volume = FindObjectOfType<Volume>();

        EnsureSettings();
        SnapToBase();
    }

    void OnDisable()
    {
        if (resetOnDisable)
            ResetAllEffectsImmediate();
        else
        {
            _bursts.Clear();
            ClearPortalDriveHoldImmediate();
        }
    }

    void OnDestroy()
    {
        ResetAllEffectsImmediate();
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
    // allowWobble: finish-portal / cinematic bursts should pass false — center wobble + CA creates mid-screen glitch lines.
    // includeBloom: false keeps bloom at the cached base (no punch).
    // lensScaleOverride: < 0 uses the serialized lensScale; 1 = no scale zoom (avoids the "camera slides off center" look).
    public void PlayBurstCustom(
        float chromaIntensity,
        float lensIntensityOverride,
        float holdSeconds,
        float fadeInSeconds = -1f,
        float fadeOutSeconds = -1f,
        bool allowWobble = true,
        bool includeBloom = true,
        float lensScaleOverride = -1f)
    {
        if (!EnsureSettings()) return;
        _bursts.Add(new FxBurst
        {
            chroma = Mathf.Clamp01(chromaIntensity),
            lens = Mathf.Clamp(lensIntensityOverride, -100f, 100f),
            lensScale = lensScaleOverride > 0f ? Mathf.Clamp(lensScaleOverride, 0.01f, 1f) : lensScale,
            centerX = lensCenterX,
            centerY = lensCenterY,
            bloomPeak = includeBloom ? bloomIntensity : _baseBloom,
            fadeIn = fadeInSeconds > 0f ? fadeInSeconds : Mathf.Max(0.01f, fadeIn),
            hold = Mathf.Max(0f, holdSeconds),
            fadeOut = fadeOutSeconds > 0f ? fadeOutSeconds : Mathf.Max(0.01f, fadeOut),
            elapsed = 0f,
            allowWobble = allowWobble
        });
    }

    /// <summary>0..1 envelope for the sustained portal punch (bloom / sat / hue).</summary>
    public float PortalHoldWeight => _portalHoldWeight;
    public void BeginPortalDriveHold(
        float bloomPeak,
        float saturation,
        float fadeInSeconds,
        float hueShiftMin = -50f,
        float hueShiftMax = 50f,
        float hueScrollCyclesPerSecond = 2.5f)
    {
        if (!EnsureSettings()) return;

        _portalBloomTarget = Mathf.Clamp(bloomPeak, 0f, BloomHardMax);
        _portalSaturationTarget = Mathf.Clamp(saturation, -100f, 100f);
        _portalHueMin = Mathf.Clamp(hueShiftMin, -180f, 180f);
        _portalHueMax = Mathf.Clamp(hueShiftMax, -180f, 180f);
        _portalHueScrollSpeed = Mathf.Max(0f, hueScrollCyclesPerSecond);
        _portalHueClock = 0f;
        _portalFadeDuration = Mathf.Max(0.01f, fadeInSeconds);
        _portalFadeElapsed = 0f;
        _portalHoldActive = true;
        _portalHoldFadingOut = false;
        _portalHoldLatched = false;
        _portalHoldWeight = 0f;
    }

    public void EndPortalDriveHold(float fadeOutSeconds = 0.25f)
    {
        if (_portalHoldWeight <= 0f && !_portalHoldActive && !_portalHoldLatched)
            return;

        _portalHoldActive = false;
        _portalHoldLatched = false;
        _portalHoldFadingOut = true;
        _portalFadeOutFrom = Mathf.Max(_portalHoldWeight, 0.0001f);
        _portalFadeDuration = Mathf.Max(0.01f, fadeOutSeconds);
        _portalFadeElapsed = 0f;
    }

    public void ClearPortalDriveHoldImmediate()
    {
        _portalHoldActive = false;
        _portalHoldFadingOut = false;
        _portalHoldLatched = false;
        _portalHoldWeight = 0f;
        _portalFadeElapsed = 0f;
        _portalHueClock = 0f;
        if (_colorAdj != null)
        {
            SetSaturation(_baseSaturation);
            SetHueShift(_baseHueShift);
        }
    }

    /// <summary>Hard reset for run teardown / quit / disable — no leftover bloom or saturation.</summary>
    public void ResetAllEffectsImmediate()
    {
        _bursts.Clear();
        _wobbleClock = 0f;
        ClearPortalDriveHoldImmediate();
        SnapToBase();
    }

    private void LateUpdate()
    {
        float dt = Time.unscaledDeltaTime;
        UpdatePortalHoldWeight(dt);

        bool hasBursts = _bursts.Count > 0;
        bool hasPortal = _portalHoldWeight > 0.0001f || _portalHoldActive || _portalHoldFadingOut || _portalHoldLatched;
        if (!hasBursts && !hasPortal)
            return;

        if (!EnsureSettings())
        {
            _bursts.Clear();
            ClearPortalDriveHoldImmediate();
            return;
        }

        float bloomAdd = 0f;
        float chromaSum = 0f;
        float lensSum = 0f;
        bool anyWobble = false;

        // Accumulate lens scale/center as an envelope-scaled DEVIATION FROM NEUTRAL (scale 1, center 0).
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

            bloomAdd += (b.bloomPeak - _baseBloom) * e;
            chromaSum += b.chroma * e;
            lensSum += b.lens * e;
            if (b.allowWobble) anyWobble = true;

            if (Mathf.Abs(b.lens) > 1e-4f)
            {
                scaleDeviation += (1f - b.lensScale) * e;
                centerX += b.centerX * e;
                centerY += b.centerY * e;
            }
        }

        float bloom = _baseBloom + bloomAdd;
        if (_portalHoldWeight > 0f)
        {
            float portalBloom = Mathf.Lerp(_baseBloom, _portalBloomTarget, _portalHoldWeight);
            bloom = Mathf.Max(bloom, portalBloom);
            SetSaturation(Mathf.Lerp(_baseSaturation, _portalSaturationTarget, _portalHoldWeight));

            // Throttle hue between min/max while rings/rays are up; amplitude follows punch weight.
            _portalHueClock += dt * _portalHueScrollSpeed;
            float hueWave = 0.5f + 0.5f * Mathf.Sin(_portalHueClock * Mathf.PI * 2f);
            float hueTarget = Mathf.Lerp(_portalHueMin, _portalHueMax, hueWave);
            SetHueShift(Mathf.Lerp(_baseHueShift, hueTarget, _portalHoldWeight));
        }
        else
        {
            SetSaturation(_baseSaturation);
            SetHueShift(_baseHueShift);
        }

        SetBloom(bloom);
        SetCA(chromaSum);

        float scale = Mathf.Clamp(1f - scaleDeviation, 0.01f, 1f);
        float cx = Mathf.Clamp(centerX, -1f, 1f);
        float cy = Mathf.Clamp(centerY, -1f, 1f);

        if (wobbleCenter && anyWobble && wobbleAmplitude > 0f && wobbleFrequency > 0f && Mathf.Abs(lensSum) > 1e-4f)
        {
            _wobbleClock += dt;
            float wob = Mathf.Sin(_wobbleClock * Mathf.PI * 2f * wobbleFrequency) * wobbleAmplitude;
            cx = Mathf.Clamp(cx + wob, -1f, 1f);
            cy = Mathf.Clamp(cy - wob, -1f, 1f);
        }

        SetLD(lensSum, scale, cx, cy);

        if (_bursts.Count == 0 && _portalHoldWeight <= 0f && !_portalHoldActive && !_portalHoldFadingOut && !_portalHoldLatched)
        {
            _wobbleClock = 0f;
            SnapToBase();
        }
    }

    private void UpdatePortalHoldWeight(float dt)
    {
        // Once fully kicked in, stay pinned until ResetAllEffectsImmediate / ClearPortalDriveHold.
        if (_portalHoldLatched)
        {
            _portalHoldWeight = 1f;
            return;
        }

        if (_portalHoldActive)
        {
            _portalFadeElapsed += dt;
            float k = Mathf.Clamp01(_portalFadeElapsed / Mathf.Max(0.01f, _portalFadeDuration));
            // SmoothStep matches the void/rings engulf curve (slow bump → full when visuals own the frame).
            _portalHoldWeight = Mathf.SmoothStep(0f, 1f, k);
            if (k >= 1f)
            {
                _portalHoldWeight = 1f;
                _portalHoldLatched = true;
                _portalHoldActive = false; // latched hold; no fade unless explicitly ended
            }
            return;
        }

        if (!_portalHoldFadingOut)
            return;

        _portalFadeElapsed += dt;
        float t = Mathf.Clamp01(_portalFadeElapsed / Mathf.Max(0.01f, _portalFadeDuration));
        // easeOut is authored 1→0 in this component.
        float e = easeOut != null ? Mathf.Clamp01(easeOut.Evaluate(t)) : (1f - t);
        _portalHoldWeight = _portalFadeOutFrom * e;
        if (t >= 1f || _portalHoldWeight <= 0.001f)
        {
            _portalHoldFadingOut = false;
            _portalHoldWeight = 0f;
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
        SetSaturation(_baseSaturation);
        SetHueShift(_baseHueShift);
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
        _bloom.intensity.value = Mathf.Clamp(intensity, 0f, BloomHardMax);
    }

    private void SetSaturation(float saturation)
    {
        if (_colorAdj == null) return;
        _colorAdj.saturation.overrideState = true;
        _colorAdj.saturation.value = Mathf.Clamp(saturation, -100f, 100f);
    }

    private void SetHueShift(float hueShift)
    {
        if (_colorAdj == null) return;
        _colorAdj.hueShift.overrideState = true;
        _colorAdj.hueShift.value = Mathf.Clamp(hueShift, -180f, 180f);
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
        volume.profile.TryGet(out _colorAdj);

        if (_ca != null)
            _ca.intensity.overrideState = true;

        if (_ld != null)
        {
            _ld.intensity.overrideState = true;
            _ld.scale.overrideState = true;
            _ld.center.overrideState = true;
        }

        if (_bloom != null)
        {
            _bloom.intensity.overrideState = true;

            if (!_cachedBaseBloom)
            {
                _baseBloom = Mathf.Clamp(_bloom.intensity.value, 0f, BloomHardMax);
                if (_baseBloom <= 0f) _baseBloom = 1.3f;
                _cachedBaseBloom = true;
            }
        }

        if (_colorAdj != null)
        {
            _colorAdj.saturation.overrideState = true;
            _colorAdj.hueShift.overrideState = true;
            if (!_cachedBaseSaturation)
            {
                _baseSaturation = Mathf.Clamp(_colorAdj.saturation.value, -100f, 100f);
                _cachedBaseSaturation = true;
            }
            if (!_cachedBaseHueShift)
            {
                _baseHueShift = Mathf.Clamp(_colorAdj.hueShift.value, -180f, 180f);
                _cachedBaseHueShift = true;
            }
        }

        return (_ca != null) || (_ld != null) || (_bloom != null) || (_colorAdj != null);
    }

    private static void EnsureOverridesExist(VolumeProfile profile)
    {
        if (!profile.TryGet<ChromaticAberration>(out _)) profile.Add<ChromaticAberration>(true);
        if (!profile.TryGet<LensDistortion>(out _)) profile.Add<LensDistortion>(true);
        if (!profile.TryGet<Bloom>(out _)) profile.Add<Bloom>(true);
        if (!profile.TryGet<ColorAdjustments>(out _)) profile.Add<ColorAdjustments>(true);
    }
}
