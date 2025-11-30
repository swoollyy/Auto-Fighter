using UnityEngine;
using DG.Tweening;
using UnityEngine.Rendering.PostProcessing; // PPSv2

[DisallowMultipleComponent]
public class PostFXController : MonoBehaviour
{
    [Header("Volume / Profile")]
    [SerializeField] private PostProcessVolume volume;

    [Header("Vignette")]
    [SerializeField, Range(0f, 1f)] private float vignetteMax = 0.55f;
    [SerializeField] private float vignetteSmoothness = 0.65f;
    [SerializeField] private bool vignetteRounded = false;

    [Header("Chromatic Aberration")]
    [SerializeField, Range(0f, 1f)] private float chromaMax = 0.35f;

    [Header("Bloom (Explosion Pulse)")]
    [SerializeField, Range(0f, 15f)] private float bloomMax = 6f;
    [SerializeField] private float bloomBaseIntensity = 0f;

    private Vignette _vig;
    private ChromaticAberration _ca;
    private Bloom _bloom;                     // NEW

    private float _vigLogical;                // 0..1 logical vignette
    private Tween _vigTween, _caTween, _bloomTween;

    void Awake()
    {
        if (!volume) volume = GetComponent < PostProcessVolume>();
        if (!volume || !volume.profile)
        {
            Debug.LogWarning("[PostFXController_PPSv2] Assign a PostProcessVolume with a profile.");
            return;
        }

        volume.profile.TryGetSettings(out _vig);
        volume.profile.TryGetSettings(out _ca);
        volume.profile.TryGetSettings(out _bloom); // NEW

        if (_vig != null)
        {
            _vig.intensity.overrideState = true;
            _vig.smoothness.overrideState = true;
            _vig.rounded.overrideState = true;
            _vig.smoothness.value = vignetteSmoothness;
            _vig.rounded.value = vignetteRounded;
            SetVignette(0f);
        }

        if (_ca != null)
        {
            _ca.intensity.overrideState = true;
            _ca.intensity.value = 0f;
        }

        if (_bloom != null)
        {
            _bloom.intensity.overrideState = true;
            _bloom.intensity.value = bloomBaseIntensity;
        }
    }

    public void SetVignette(float logical01)
    {
        _vigLogical = Mathf.Clamp01(logical01);
        if (_vig != null) _vig.intensity.value = _vigLogical * vignetteMax;
    }

    public void FadeVignette(float logical01, float seconds)
    {
        logical01 = Mathf.Clamp01(logical01);
        _vigTween?.Kill(false);
        float start = _vigLogical;
        _vigTween = DOTween.To(() => start, v => { start = v; SetVignette(v); },
                               logical01, seconds).SetEase(Ease.OutQuad).SetUpdate(true);
    }

    public void ClearVignette(float seconds = 0.12f) => FadeVignette(0f, seconds);

    public void ChromaticPulse(float peak = 1.25f, float up = 0.06f, float down = 0.14f)
    {
        if (_ca == null) return;
        peak = Mathf.Clamp(peak, 0f, chromaMax);
        _caTween?.Kill(false);

        _caTween = DOTween.To(() => _ca.intensity.value, v => _ca.intensity.value = v,
                              peak, up).SetEase(Ease.OutQuad).SetUpdate(true)
            .OnComplete(() =>
            {
                _caTween = DOTween.To(() => _ca.intensity.value, v => _ca.intensity.value = v,
                                      0f, down).SetEase(Ease.InQuad).SetUpdate(true);
            });
    }

    // NEW: bloom pulse for explosions
    public void BloomPulse(float peakFraction, float upTime, float downTime)
    {
        if (_bloom == null) return;
        peakFraction = Mathf.Clamp01(peakFraction);
        float peak = bloomBaseIntensity + bloomMax * peakFraction;

        _bloomTween?.Kill(false);
        _bloomTween = DOTween.Sequence()
            .Append(DOTween.To(() => _bloom.intensity.value, v => _bloom.intensity.value = v,
                               peak, upTime).SetEase(Ease.OutQuad))
            .Append(DOTween.To(() => _bloom.intensity.value, v => _bloom.intensity.value = v,
                               bloomBaseIntensity, downTime).SetEase(Ease.InQuad))
            .SetUpdate(true);
    }

    // Exposed tweakables
    public float VignetteMax { get => vignetteMax; set => vignetteMax = Mathf.Clamp01(value); }
    public float ChromaMax { get => chromaMax; set => chromaMax = Mathf.Clamp01(value); }
    public float BloomMax { get => bloomMax; set => bloomMax = Mathf.Max(0f, value); }
    public float BloomBaseIntensity { get => bloomBaseIntensity; set => bloomBaseIntensity = Mathf.Max(0f, value); }
}