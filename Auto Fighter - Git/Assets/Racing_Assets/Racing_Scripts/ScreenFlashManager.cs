using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Manages screen flash/edge glow effects for game events.
/// Integrates with CoinDatabase for coin-specific flashes.
/// Supports continuous pulsing flash for invincibility.
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
    [SerializeField] private float defaultSoftness = 0.3f;

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

    // Material property IDs
    private static readonly int ColorID = Shader.PropertyToID("_Color");
    private static readonly int IntensityID = Shader.PropertyToID("_Intensity");
    private static readonly int InnerRadiusID = Shader.PropertyToID("_InnerRadius");
    private static readonly int OuterRadiusID = Shader.PropertyToID("_OuterRadius");
    private static readonly int SoftnessID = Shader.PropertyToID("_Softness");

    private Material _instanceMaterial;
    private Tween _currentTween;
    private float _currentIntensity;

    // Persistent flash state
    private bool _icePersistentActive;
    private Tween _iceTween;

    // Continuous invincibility flash state
    private bool _invincibilityActive;
    private float _invincibilityEndTime;
    private Tween _invincibilityTween;

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
        if (_instanceMaterial) Destroy(_instanceMaterial);
    }

    void Update()
    {
        // Check if invincibility has expired
        if (_invincibilityActive && Time.unscaledTime >= _invincibilityEndTime)
        {
            StopInvincibilityFlash();
        }
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
        return _currentTween != null && _currentTween.IsActive() && _currentIntensity > 0.001f;
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
        // Priority: Invincibility > Ice
        if (_invincibilityActive)
        {
            StartInvincibilityPulse();
        }
        else if (_icePersistentActive)
        {
            SetIcePersistent(true);
        }
    }

    // === CORE FLASH METHODS ===

    /// <summary>
    /// Flash with full customization.
    /// </summary>
    public void Flash(Color color, float intensity, float duration, float innerRadius = 0.3f, float outerRadius = 1.2f, float softness = 0.3f)
    {
        if (_instanceMaterial == null) return;

        _currentTween?.Kill();
        _invincibilityTween?.Kill(); // Pause invincibility pulse during flash

        _instanceMaterial.SetColor(ColorID, color);
        _instanceMaterial.SetFloat(InnerRadiusID, innerRadius);
        _instanceMaterial.SetFloat(OuterRadiusID, outerRadius);
        _instanceMaterial.SetFloat(SoftnessID, softness);

        _currentIntensity = 0f;
        _currentTween = DOTween.To(
            () => _currentIntensity,
            x => {
                _currentIntensity = x;
                _instanceMaterial.SetFloat(IntensityID, x);
            },
            intensity,
            duration * 0.3f
        )
        .SetEase(Ease.OutQuad)
        .SetUpdate(true)
        .OnComplete(() => {
            _currentTween = DOTween.To(
                () => _currentIntensity,
                x => {
                    _currentIntensity = x;
                    _instanceMaterial.SetFloat(IntensityID, x);
                },
                0f,
                duration * 0.7f
            )
            .SetEase(Ease.InQuad)
            .SetUpdate(true)
            .OnComplete(RestorePersistentIfNeeded);
        });
    }

    public void SetIcePersistent(bool active)
    {
        if (_instanceMaterial == null) return;

        _icePersistentActive = active;
        _iceTween?.Kill();

        // Don't override invincibility flash
        if (_invincibilityActive) return;

        // If a one-shot flash is currently playing, let it finish.
        if (IsFlashingNow())
            return;

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

    /// <summary>
    /// Additive flash that stacks with current intensity (for rapid events).
    /// </summary>
    public void FlashAdditive(Color color, float intensityAdd, float duration, float innerRadius = 0.4f)
    {
        if (_instanceMaterial == null) return;

        _instanceMaterial.SetColor(ColorID, color);
        _instanceMaterial.SetFloat(InnerRadiusID, innerRadius);

        float targetIntensity = Mathf.Min(_currentIntensity + intensityAdd, 3f);

        _currentTween?.Kill();
        _currentIntensity = targetIntensity;
        _instanceMaterial.SetFloat(IntensityID, _currentIntensity);

        _currentTween = DOTween.To(
            () => _currentIntensity,
            x => {
                _currentIntensity = x;
                _instanceMaterial.SetFloat(IntensityID, x);
            },
            0f,
            duration
        )
        .SetEase(Ease.OutQuad)
        .SetUpdate(true)
        .OnComplete(RestorePersistentIfNeeded);
    }

    // === INVINCIBILITY CONTINUOUS FLASH ===

    /// <summary>
    /// Start continuous pulsing flash for invincibility.
    /// </summary>
    public void StartInvincibilityFlash(float duration)
    {
        _invincibilityActive = true;
        _invincibilityEndTime = Time.unscaledTime + duration;

        // Stop any other persistent effects
        _iceTween?.Kill();
        _currentTween?.Kill();

        StartInvincibilityPulse();
    }

    private void StartInvincibilityPulse()
    {
        if (!_invincibilityActive || _instanceMaterial == null) return;

        _invincibilityTween?.Kill();

        // Set material properties
        _instanceMaterial.SetColor(ColorID, invincibilityColor);
        _instanceMaterial.SetFloat(InnerRadiusID, invincibilityInnerRadius);
        _instanceMaterial.SetFloat(OuterRadiusID, defaultOuterRadius);
        _instanceMaterial.SetFloat(SoftnessID, defaultSoftness);

        float pulseDuration = 1f / Mathf.Max(0.1f, invincibilityPulseSpeed);

        // Pulse from low to high
        _invincibilityTween = DOTween.To(
            () => _currentIntensity,
            x => {
                _currentIntensity = x;
                _instanceMaterial.SetFloat(IntensityID, x);
            },
            invincibilityIntensityHigh,
            pulseDuration * 0.5f
        )
        .SetEase(Ease.InOutSine)
        .SetUpdate(true)
        .OnComplete(() => {
            if (!_invincibilityActive) return;

            // Pulse from high to low
            _invincibilityTween = DOTween.To(
                () => _currentIntensity,
                x => {
                    _currentIntensity = x;
                    _instanceMaterial.SetFloat(IntensityID, x);
                },
                invincibilityIntensityLow,
                pulseDuration * 0.5f
            )
            .SetEase(Ease.InOutSine)
            .SetUpdate(true)
            .OnComplete(() => {
                // Continue pulsing if still active
                if (_invincibilityActive && Time.unscaledTime < _invincibilityEndTime)
                {
                    StartInvincibilityPulse();
                }
                else
                {
                    StopInvincibilityFlash();
                }
            });
        });
    }

    /// <summary>
    /// Stop invincibility flash and fade out.
    /// </summary>
    public void StopInvincibilityFlash()
    {
        _invincibilityActive = false;
        _invincibilityTween?.Kill();

        // Fade out
        if (_instanceMaterial != null)
        {
            DOTween.To(
                () => _currentIntensity,
                x => {
                    _currentIntensity = x;
                    _instanceMaterial?.SetFloat(IntensityID, x);
                },
                0f,
                0.2f
            ).SetEase(Ease.OutQuad).SetUpdate(true).OnComplete(() => {
                // Check if ice should be restored
                if (_icePersistentActive)
                    SetIcePersistent(true);
            });
        }
    }

    /// <summary>
    /// Flash for invincibility impact (quick burst, then resumes pulse).
    /// </summary>
    public void FlashInvincibilityImpact()
    {
        // Quick bright flash
        Flash(invincibilityImpactColor, invincibilityImpactIntensity, invincibilityImpactDuration, invincibilityImpactInnerRadius);

        // RestorePersistentIfNeeded will restart the pulse after the flash completes
    }

    // === PRESET METHODS ===

    public void FlashMash()
    {
        FlashAdditive(mashColor, mashIntensity, mashDuration, mashInnerRadius);
    }

    /// <summary>
    /// Flash for coin - uses CoinDatabase to get settings.
    /// </summary>
    public void FlashCoin(CoinType coinType)
    {
        var data = CoinDatabase.Get(coinType);
        if (data != null)
        {
            Flash(data.primaryColor, data.flashIntensity, data.flashDuration, data.flashInnerRadius);
        }
        else
        {
            // Fallback
            Flash(Color.yellow, 1f, 0.25f, 0.4f);
        }
    }

    /// <summary>
    /// Flash for coin using CoinDataSO directly.
    /// </summary>
    public void FlashCoin(CoinDataSO coinData)
    {
        if (coinData != null)
        {
            Flash(coinData.primaryColor, coinData.flashIntensity, coinData.flashDuration, coinData.flashInnerRadius);
        }
        else
        {
            Flash(Color.yellow, 1f, 0.25f, 0.4f);
        }
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

    // Invincibility
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