using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Manages screen flash/edge glow effects for game events.
/// One-shot flashes (boost, coins, crash, mash, etc.) stack as impulses:
/// intensity multiplies, edge size grows, durations extend each other, and colors blend.
/// Persistent ice / invincibility layers restore when the impulse stack empties.
/// </summary>
public class ScreenFlashManager : MonoBehaviour
{
    public static ScreenFlashManager Instance { get; private set; }

    [Header("Setup")]
    [Tooltip("The RawImage or Image covering the full screen with EdgeGlow material.")]
    [SerializeField] private Graphic flashImage;
    [SerializeField] private Material edgeGlowMaterial;

    [Header("Default Settings")]
    [SerializeField] private float defaultOuterRadius = 1.2f;
    [SerializeField] private float defaultSoftness = 0.55f;

    [Header("Stacking")]
    [Tooltip("How hard stacked flashes amplify intensity. 1 = pure sum; 1.25 ≈ +25% per extra active flash.")]
    [SerializeField, Min(1f)] private float intensityStackMultiplier = 1.25f;
    [Tooltip("Hard cap on combined intensity after stacking.")]
    [SerializeField, Min(0.1f)] private float maxStackedIntensity = 4f;
    [Tooltip("0 = keep average edge thickness; 1 = shrink inner radius to the thickest (smallest inner) flash.")]
    [SerializeField, Range(0f, 1f)] private float sizeStackStrength = 0.85f;
    [Tooltip("When a new flash fires, extend every active flash so remaining life is at least newDuration × this. 1 = full new duration; 0 = no extend (still no cut-off).")]
    [SerializeField, Min(0f)] private float durationExtendOnStack = 1f;
    [Tooltip("Rise portion of each impulse envelope (rest is fall).")]
    [SerializeField, Range(0.05f, 0.6f)] private float riseFraction = 0.45f;
    [Tooltip("Minimum rise time so short flashes still ease in (unscaled seconds).")]
    [SerializeField, Min(0f)] private float minRiseSeconds = 0.1f;
    [Tooltip("Softness at the start of a flash (lerps to the flash's softness). Higher = fuzzier edge on appear.")]
    [SerializeField, Range(0.05f, 1f)] private float edgeAppearSoftness = 0.85f;
    [Tooltip("Inner radius at the start of a flash (lerps to the flash's inner). Higher = thinner ring hugging the screen edge, then grows inward.")]
    [SerializeField, Range(0f, 1f)] private float edgeAppearInnerRadius = 0.78f;

    [Header("Preset: Mash Click")]
    [SerializeField] private Color mashColor = new Color(1f, 0.9f, 0.3f, 1f);
    [SerializeField] private float mashIntensity = 0.8f;
    [SerializeField] private float mashDuration = 0.15f;
    [SerializeField] private float mashInnerRadius = 0.5f;

    [Header("Preset: Damage")]
    [SerializeField] private Color damageColor = new Color(1f, 0.2f, 0.1f, 1f);
    [SerializeField] private float damageIntensity = 1.5f;
    [SerializeField] private float damageDuration = 0.35f;
    [SerializeField] private float damageInnerRadius = 0.2f;

    [Header("Preset: Heal/HP Gain")]
    [SerializeField] private Color healColor = new Color(0.3f, 1f, 0.4f, 1f);
    [SerializeField] private float healIntensity = 1f;
    [SerializeField] private float healDuration = 0.3f;
    [SerializeField] private float healInnerRadius = 0.4f;

    [Header("Preset: Fuel Gain")]
    [SerializeField] private Color fuelColor = new Color(0.2f, 0.8f, 1f, 1f);
    [SerializeField] private float fuelIntensity = 0.8f;
    [SerializeField] private float fuelDuration = 0.25f;
    [SerializeField] private float fuelInnerRadius = 0.45f;

    [Header("Preset: Boost")]
    [SerializeField] private Color boostColor = new Color(0.3f, 0.7f, 1f, 1f);
    [SerializeField] private float boostIntensity = 1.3f;
    [SerializeField] private float boostDuration = 0.2f;
    [SerializeField] private float boostInnerRadius = 0.35f;

    [Header("Preset: Level Up / Big Event")]
    [SerializeField] private Color levelUpColor = new Color(1f, 1f, 1f, 1f);
    [SerializeField] private float levelUpIntensity = 2f;
    [SerializeField] private float levelUpDuration = 0.5f;
    [SerializeField] private float levelUpInnerRadius = 0.1f;

    [Header("Persistent: Ice Path")]
    [SerializeField] private Color iceColor = new Color(0.55f, 0.85f, 1f, 1f);
    [SerializeField, Min(0f)] private float icePersistentIntensity = 0.75f;
    [SerializeField, Min(0f)] private float iceFadeIn = 0.12f;
    [SerializeField, Min(0f)] private float iceFadeOut = 0.12f;
    [SerializeField] private float iceInnerRadius = 0.55f;
    [SerializeField] private float iceOuterRadius = 1.2f;
    [SerializeField] private float iceSoftness = 0.35f;

    [Header("Preset: Sprocket Gain")]
    [SerializeField] private Color sprocketColor = new Color(0.7f, 0.5f, 0.2f, 1f);
    [SerializeField] private float sprocketIntensity = 1.2f;
    [SerializeField] private float sprocketDuration = 0.3f;
    [SerializeField] private float sprocketInnerRadius = 0.35f;

    [Header("Preset: Invincibility (Continuous)")]
    [SerializeField] private Color invincibilityColor = new Color(0.7f, 0.85f, 1f, 1f);
    [SerializeField] private float invincibilityIntensityLow = 0.3f;
    [SerializeField] private float invincibilityIntensityHigh = 0.8f;
    [SerializeField] private float invincibilityInnerRadius = 0.5f;
    [SerializeField] private float invincibilityPulseSpeed = 3f;

    [Header("Preset: Invincibility Impact")]
    [SerializeField] private Color invincibilityImpactColor = Color.white;
    [SerializeField] private float invincibilityImpactIntensity = 2f;
    [SerializeField] private float invincibilityImpactDuration = 0.15f;
    [SerializeField] private float invincibilityImpactInnerRadius = 0.2f;

    private static readonly int ColorID = Shader.PropertyToID("_Color");
    private static readonly int IntensityID = Shader.PropertyToID("_Intensity");
    private static readonly int InnerRadiusID = Shader.PropertyToID("_InnerRadius");
    private static readonly int OuterRadiusID = Shader.PropertyToID("_OuterRadius");
    private static readonly int SoftnessID = Shader.PropertyToID("_Softness");

    private struct FlashImpulse
    {
        public Color color;
        public float peakIntensity;
        public float innerRadius;
        public float outerRadius;
        public float softness;
        public float startTime;
        public float duration;

        public float EndTime => startTime + duration;

        /// <summary>0 at start, 1 after rise completes (and through fall).</summary>
        public float Appear01(float now, float riseFrac, float minRise)
        {
            float age = now - startTime;
            if (age <= 0f || duration <= 0f) return 0f;
            float rise = RiseSeconds(duration, riseFrac, minRise);
            if (age >= rise) return 1f;
            float u = age / rise;
            // Smoothstep — soft start and settle, not a harsh pop.
            return u * u * (3f - 2f * u);
        }

        public float Evaluate(float now, float riseFrac, float minRise)
        {
            float age = now - startTime;
            if (age < 0f || duration <= 0f || age >= duration)
                return 0f;

            float rise = RiseSeconds(duration, riseFrac, minRise);
            if (age <= rise)
                return peakIntensity * Appear01(now, riseFrac, minRise);

            float fall = Mathf.Max(0.0001f, duration - rise);
            float v = (age - rise) / fall;
            // Smoothstep fall toward 0
            float w = v * v * (3f - 2f * v);
            return peakIntensity * (1f - w);
        }

        private static float RiseSeconds(float duration, float riseFrac, float minRise)
        {
            float rise = duration * Mathf.Clamp(riseFrac, 0.05f, 0.6f);
            if (minRise > 0f)
                rise = Mathf.Max(rise, Mathf.Min(minRise, duration * 0.85f));
            return Mathf.Max(0.0001f, rise);
        }
    }

    private Material _instanceMaterial;
    private readonly List<FlashImpulse> _impulses = new(16);
    private float _currentIntensity;
    private bool _wasCompositingImpulses;

    private bool _icePersistentActive;
    private Tween _iceTween;

    private bool _invincibilityActive;
    private float _invincibilityEndTime;
    private Tween _invincibilityTween;
    private Tween _invincibilityFadeTween;

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        SetupMaterial();
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
        _iceTween?.Kill();
        _invincibilityTween?.Kill();
        _invincibilityFadeTween?.Kill();
        if (_instanceMaterial) Destroy(_instanceMaterial);
    }

    void Update()
    {
        if (_invincibilityActive && Time.unscaledTime >= _invincibilityEndTime)
            StopInvincibilityFlash();

        CompositeImpulsesAndApply();
    }

    private void SetupMaterial()
    {
        if (edgeGlowMaterial == null)
        {
            Debug.LogWarning("[ScreenFlashManager] No EdgeGlow material assigned!");
            return;
        }

        _instanceMaterial = new Material(edgeGlowMaterial);

        if (flashImage)
        {
            flashImage.material = _instanceMaterial;
            _instanceMaterial.SetFloat(IntensityID, 0f);
        }
    }

    private bool IsFlashingNow()
    {
        return _impulses.Count > 0 && _currentIntensity > 0.001f;
    }

    private void ApplyIceMaterialSettings()
    {
        _instanceMaterial.SetColor(ColorID, iceColor);
        _instanceMaterial.SetFloat(InnerRadiusID, iceInnerRadius);
        _instanceMaterial.SetFloat(OuterRadiusID, iceOuterRadius > 0f ? iceOuterRadius : defaultOuterRadius);
        _instanceMaterial.SetFloat(SoftnessID, iceSoftness > 0f ? iceSoftness : defaultSoftness);
    }

    private void RestorePersistentIfNeeded()
    {
        if (_impulses.Count > 0)
            return;

        // Priority: Invincibility > Ice
        if (_invincibilityActive)
            StartInvincibilityPulse();
        else if (_icePersistentActive)
            SetIcePersistent(true);
    }

    // === CORE FLASH METHODS ===

    /// <summary>
    /// Push a one-shot edge flash. Stacks with other active flashes (no cut-off).
    /// </summary>
    public void Flash(Color color, float intensity, float duration, float innerRadius = 0.3f, float outerRadius = 1.2f, float softness = -1f)
    {
        PushImpulse(color, intensity, duration, innerRadius, outerRadius, softness > 0f ? softness : defaultSoftness);
    }

    /// <summary>
    /// Additive-style flash for rapid events — same stacking path as <see cref="Flash"/>.
    /// </summary>
    public void FlashAdditive(Color color, float intensityAdd, float duration, float innerRadius = 0.4f)
    {
        PushImpulse(color, intensityAdd, duration, innerRadius, defaultOuterRadius, defaultSoftness);
    }

    private void PushImpulse(Color color, float intensity, float duration, float innerRadius, float outerRadius, float softness)
    {
        if (_instanceMaterial == null) return;
        if (intensity <= 0f || duration <= 0f) return;

        float now = Time.unscaledTime;
        float dur = Mathf.Max(0.01f, duration);

        // Extend active flashes so stacked events lengthen each other instead of cutting off.
        if (durationExtendOnStack > 0f && _impulses.Count > 0)
        {
            float minEnd = now + dur * durationExtendOnStack;
            for (int i = 0; i < _impulses.Count; i++)
            {
                FlashImpulse imp = _impulses[i];
                if (imp.EndTime < minEnd)
                {
                    imp.duration = minEnd - imp.startTime;
                    _impulses[i] = imp;
                }
            }
        }

        _impulses.Add(new FlashImpulse
        {
            color = color,
            peakIntensity = intensity,
            innerRadius = innerRadius,
            outerRadius = outerRadius > 0f ? outerRadius : defaultOuterRadius,
            softness = softness > 0f ? softness : defaultSoftness,
            startTime = now,
            duration = dur
        });

        // Pause persistent layers while impulses drive the material.
        _invincibilityTween?.Kill();
        _invincibilityFadeTween?.Kill();
        _iceTween?.Kill();
    }

    private void CompositeImpulsesAndApply()
    {
        if (_instanceMaterial == null) return;

        float now = Time.unscaledTime;
        for (int i = _impulses.Count - 1; i >= 0; i--)
        {
            if (now >= _impulses[i].EndTime)
                _impulses.RemoveAt(i);
        }

        if (_impulses.Count == 0)
        {
            if (_wasCompositingImpulses)
            {
                _wasCompositingImpulses = false;
                _currentIntensity = 0f;
                if (!_invincibilityActive && !_icePersistentActive)
                    _instanceMaterial.SetFloat(IntensityID, 0f);
                RestorePersistentIfNeeded();
            }
            return;
        }

        _wasCompositingImpulses = true;

        float sumI = 0f;
        float r = 0f, g = 0f, b = 0f;
        float weightedInner = 0f;
        float weightedSoft = 0f;
        float minInner = float.MaxValue;
        float maxOuter = 0f;
        int active = 0;

        for (int i = 0; i < _impulses.Count; i++)
        {
            FlashImpulse imp = _impulses[i];
            float e = imp.Evaluate(now, riseFraction, minRiseSeconds);
            if (e <= 0.0001f) continue;

            float appear = imp.Appear01(now, riseFraction, minRiseSeconds);
            // Grow the glowing band inward from a soft screen-edge ribbon.
            float softNow = Mathf.Lerp(Mathf.Max(imp.softness, edgeAppearSoftness), imp.softness, appear);
            float innerNow = Mathf.Lerp(
                Mathf.Max(imp.innerRadius, edgeAppearInnerRadius),
                imp.innerRadius,
                appear);

            sumI += e;
            r += imp.color.r * e;
            g += imp.color.g * e;
            b += imp.color.b * e;
            weightedInner += innerNow * e;
            weightedSoft += softNow * e;
            if (innerNow < minInner) minInner = innerNow;
            if (imp.outerRadius > maxOuter) maxOuter = imp.outerRadius;
            active++;
        }

        if (active == 0 || sumI <= 0.0001f)
        {
            _currentIntensity = 0f;
            _instanceMaterial.SetFloat(IntensityID, 0f);
            return;
        }

        float stackBoost = 1f + (active - 1) * (intensityStackMultiplier - 1f);
        float intensity = Mathf.Min(maxStackedIntensity, sumI * stackBoost);

        Color blended = new Color(r / sumI, g / sumI, b / sumI, 1f);
        float avgInner = weightedInner / sumI;
        float inner = Mathf.Lerp(avgInner, minInner, sizeStackStrength);
        float soft = weightedSoft / sumI;
        float outer = maxOuter > 0f ? maxOuter : defaultOuterRadius;

        _currentIntensity = intensity;
        _instanceMaterial.SetColor(ColorID, blended);
        _instanceMaterial.SetFloat(IntensityID, intensity);
        _instanceMaterial.SetFloat(InnerRadiusID, inner);
        _instanceMaterial.SetFloat(OuterRadiusID, outer);
        _instanceMaterial.SetFloat(SoftnessID, soft);
    }

    public void SetIcePersistent(bool active)
    {
        if (_instanceMaterial == null) return;

        _icePersistentActive = active;
        _iceTween?.Kill();

        if (_invincibilityActive) return;
        if (IsFlashingNow()) return;

        float startIntensity = _instanceMaterial.GetFloat(IntensityID);

        if (active)
        {
            ApplyIceMaterialSettings();

            _iceTween = DOTween.To(
                () => startIntensity,
                x =>
                {
                    startIntensity = x;
                    _instanceMaterial.SetFloat(IntensityID, x);
                },
                icePersistentIntensity,
                iceFadeIn
            ).SetEase(Ease.OutQuad).SetUpdate(true);
        }
        else
        {
            _iceTween = DOTween.To(
                () => startIntensity,
                x =>
                {
                    startIntensity = x;
                    _instanceMaterial.SetFloat(IntensityID, x);
                },
                0f,
                iceFadeOut
            ).SetEase(Ease.OutQuad).SetUpdate(true);
        }
    }

    // === INVINCIBILITY CONTINUOUS FLASH ===

    public void StartInvincibilityFlash(float duration)
    {
        _invincibilityActive = true;
        _invincibilityEndTime = Time.unscaledTime + duration;

        _iceTween?.Kill();
        _invincibilityFadeTween?.Kill();
        _impulses.Clear();
        _wasCompositingImpulses = false;

        StartInvincibilityPulse();
    }

    private void StartInvincibilityPulse()
    {
        if (!_invincibilityActive || _instanceMaterial == null) return;
        if (_impulses.Count > 0) return; // impulses own the material until they finish

        _invincibilityTween?.Kill();

        _instanceMaterial.SetColor(ColorID, invincibilityColor);
        _instanceMaterial.SetFloat(InnerRadiusID, invincibilityInnerRadius);
        _instanceMaterial.SetFloat(OuterRadiusID, defaultOuterRadius);
        _instanceMaterial.SetFloat(SoftnessID, defaultSoftness);

        float pulseDuration = 1f / Mathf.Max(0.1f, invincibilityPulseSpeed);

        _invincibilityTween = DOTween.To(
            () => _currentIntensity,
            x =>
            {
                _currentIntensity = x;
                _instanceMaterial.SetFloat(IntensityID, x);
            },
            invincibilityIntensityHigh,
            pulseDuration * 0.5f
        )
        .SetEase(Ease.InOutSine)
        .SetUpdate(true)
        .OnComplete(() =>
        {
            if (!_invincibilityActive || _impulses.Count > 0) return;

            _invincibilityTween = DOTween.To(
                () => _currentIntensity,
                x =>
                {
                    _currentIntensity = x;
                    _instanceMaterial.SetFloat(IntensityID, x);
                },
                invincibilityIntensityLow,
                pulseDuration * 0.5f
            )
            .SetEase(Ease.InOutSine)
            .SetUpdate(true)
            .OnComplete(() =>
            {
                if (_invincibilityActive && Time.unscaledTime < _invincibilityEndTime && _impulses.Count == 0)
                    StartInvincibilityPulse();
                else if (!_invincibilityActive || Time.unscaledTime >= _invincibilityEndTime)
                    StopInvincibilityFlash();
            });
        });
    }

    public void StopInvincibilityFlash()
    {
        _invincibilityActive = false;
        _invincibilityTween?.Kill();
        _invincibilityFadeTween?.Kill();

        if (_impulses.Count > 0)
            return; // stacked flashes keep driving; ice restores when they finish

        if (_instanceMaterial != null)
        {
            _invincibilityFadeTween = DOTween.To(
                () => _currentIntensity,
                x =>
                {
                    _currentIntensity = x;
                    _instanceMaterial?.SetFloat(IntensityID, x);
                },
                0f,
                0.2f
            ).SetEase(Ease.OutQuad).SetUpdate(true).OnComplete(() =>
            {
                if (_icePersistentActive && _impulses.Count == 0)
                    SetIcePersistent(true);
            });
        }
    }

    public void FlashInvincibilityImpact()
    {
        Flash(invincibilityImpactColor, invincibilityImpactIntensity, invincibilityImpactDuration, invincibilityImpactInnerRadius);
    }

    // === PRESET METHODS ===

    public void FlashMash()
    {
        FlashAdditive(mashColor, mashIntensity, mashDuration, mashInnerRadius);
    }

    public void FlashCoin(CoinType coinType)
    {
        var data = CoinDatabase.Get(coinType);
        if (data != null)
            Flash(data.primaryColor, data.flashIntensity, data.flashDuration, data.flashInnerRadius);
        else
            Flash(Color.yellow, 1f, 0.25f, 0.4f);
    }

    public void FlashCoin(CoinDataSO coinData)
    {
        if (coinData != null)
            Flash(coinData.primaryColor, coinData.flashIntensity, coinData.flashDuration, coinData.flashInnerRadius);
        else
            Flash(Color.yellow, 1f, 0.25f, 0.4f);
    }

    public void FlashDamage()
    {
        Flash(damageColor, damageIntensity, damageDuration, damageInnerRadius);
    }

    public void FlashDamage(float damageAmount)
    {
        float scaledIntensity = Mathf.Lerp(damageIntensity * 0.5f, damageIntensity * 1.5f, Mathf.Clamp01(damageAmount / 50f));
        Flash(damageColor, scaledIntensity, damageDuration, damageInnerRadius);
    }

    public void FlashHeal()
    {
        Flash(healColor, healIntensity, healDuration, healInnerRadius);
    }

    public void FlashHeal(float amount)
    {
        float scaledIntensity = Mathf.Lerp(healIntensity * 0.5f, healIntensity * 1.2f, Mathf.Clamp01(amount / 30f));
        Flash(healColor, scaledIntensity, healDuration, healInnerRadius);
    }

    public void FlashFuelGain()
    {
        Flash(fuelColor, fuelIntensity, fuelDuration, fuelInnerRadius);
    }

    public void FlashFuelGain(float amount)
    {
        float scaledIntensity = Mathf.Lerp(fuelIntensity * 0.5f, fuelIntensity * 1.2f, Mathf.Clamp01(amount / 20f));
        Flash(fuelColor, scaledIntensity, fuelDuration, fuelInnerRadius);
    }

    public void FlashBoost()
    {
        Flash(boostColor, boostIntensity, boostDuration, boostInnerRadius);
    }

    public void FlashLevelUp()
    {
        Flash(levelUpColor, levelUpIntensity, levelUpDuration, levelUpInnerRadius);
    }

    public void FlashSprocket()
    {
        Flash(sprocketColor, sprocketIntensity, sprocketDuration, sprocketInnerRadius);
    }

    public void FlashSprocket(int amount)
    {
        float scaledIntensity = Mathf.Lerp(sprocketIntensity * 0.7f, sprocketIntensity * 1.5f, Mathf.Clamp01(amount / 30f));
        Flash(sprocketColor, scaledIntensity, sprocketDuration, sprocketInnerRadius);
    }

    public void Flash(ScreenFlashType type)
    {
        switch (type)
        {
            case ScreenFlashType.Mash: FlashMash(); break;
            case ScreenFlashType.Damage: FlashDamage(); break;
            case ScreenFlashType.Heal: FlashHeal(); break;
            case ScreenFlashType.FuelGain: FlashFuelGain(); break;
            case ScreenFlashType.Boost: FlashBoost(); break;
            case ScreenFlashType.LevelUp: FlashLevelUp(); break;
            case ScreenFlashType.Sprocket: FlashSprocket(); break;
            case ScreenFlashType.Invincibility: StartInvincibilityFlash(1f); break;
        }
    }

    // === STATIC SHORTCUTS ===

    public static void Mash() => Instance?.FlashMash();
    public static void Coin(CoinType type) => Instance?.FlashCoin(type);
    public static void Coin(CoinDataSO data) => Instance?.FlashCoin(data);
    public static void Damage() => Instance?.FlashDamage();
    public static void Damage(float amount) => Instance?.FlashDamage(amount);
    public static void Heal() => Instance?.FlashHeal();
    public static void Heal(float amount) => Instance?.FlashHeal(amount);
    public static void FuelGain() => Instance?.FlashFuelGain();
    public static void FuelGain(float amount) => Instance?.FlashFuelGain(amount);
    public static void Boost() => Instance?.FlashBoost();
    public static void LevelUp() => Instance?.FlashLevelUp();
    public static void Sprocket() => Instance?.FlashSprocket();
    public static void Sprocket(int amount) => Instance?.FlashSprocket(amount);

    public static void Invincibility(float duration) => Instance?.StartInvincibilityFlash(duration);
    public static void InvincibilityImpact() => Instance?.FlashInvincibilityImpact();
    public static void StopInvincibility() => Instance?.StopInvincibilityFlash();
}

public enum ScreenFlashType
{
    Mash,
    Damage,
    Heal,
    FuelGain,
    Boost,
    LevelUp,
    Sprocket,
    Invincibility
}
