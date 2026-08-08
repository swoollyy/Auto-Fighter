using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// In-game drift-held boost charge bar. Parent under the game canvas (e.g. in-run HUD root).
/// <see cref="UIManager_Racing"/> calls <see cref="RefreshFromHud"/> while the in-run HUD is shown so this still
/// updates if this object was inactive at load (Awake/LateUpdate never ran).
/// </summary>
[DisallowMultipleComponent]
public class DriftBoostHoldBarUI : MonoBehaviour
{
    [Header("Car (optional)")]
    [Tooltip("Fallback only when no run is active: while GameManager_Racing has a spawned car, ActiveCar is always used (so a wrong/prefab reference here cannot hide the bar).")]
    [SerializeField] private CarController car;

    [Header("UI")]
    [Tooltip("Root object toggled on/off with drift hold + unlock.")]
    [SerializeField] private GameObject barRoot;

    [Tooltip("Optional Slider (0..1). Uses whole range for 0..max hold.")]
    [SerializeField] private Slider fillSlider;

    [Tooltip("Optional radial or horizontal Image with Image Type = Filled.")]
    [SerializeField] private Image fillImage;

    [Tooltip("Optional marker along the bar for min hold time (anchor X 0..1 on a child RectTransform).")]
    [SerializeField] private RectTransform minHoldMarker;

    [Header("Charge tint")]
    [Tooltip("If set, this graphic gets the red→green tint. Otherwise uses Fill Image, else Slider fill rect.")]
    [SerializeField] private Graphic chargeColorGraphic;

    [SerializeField] private Color notEnoughColor = new Color(0.92f, 0.2f, 0.18f, 1f);

    [Tooltip("Unused legacy field (kept so existing scene/prefab serialization stays stable).")]
    [SerializeField] private Color minBoostColor = new Color(1f, 0.85f, 0.15f, 1f);

    [SerializeField] private Color maxBoostColor = new Color(0.25f, 0.85f, 0.35f, 1f);

    [Tooltip("Unused legacy field (kept so existing scene/prefab serialization stays stable).")]
    [SerializeField] private bool smoothColorBlend = true;

    [Header("Visibility")]
    [Tooltip("When a GameManager exists, hide the bar until loading is done and the run is live (avoids flash after load).")]
    [SerializeField] private bool requireGameplayLive = true;

    [Tooltip("Extra delay after gameplay goes live before the bar can appear (unscaled sec). Catches one-frame input glitches.")]
    [SerializeField, Min(0f)] private float hudSettleDelayUnscaled = 0.18f;

    private Graphic _resolvedColorGraphic;
    private float _chargeGraphicBaseAlpha = 1f;
    private float _gameplayLiveSinceUnscaled = -1f;
    private bool _warnedMissingBarRoot;
    private bool _initialized;

    private void Awake() => EnsureInitialized();

    private void EnsureInitialized()
    {
        if (_initialized)
            return;
        _initialized = true;

        if (barRoot != null)
            barRoot.SetActive(false);

        if (chargeColorGraphic != null)
            _resolvedColorGraphic = chargeColorGraphic;
        else if (fillImage != null)
            _resolvedColorGraphic = fillImage;
        else if (fillSlider != null && fillSlider.fillRect != null)
            _resolvedColorGraphic = fillSlider.fillRect.GetComponent<Graphic>();

        if (_resolvedColorGraphic != null)
            _chargeGraphicBaseAlpha = _resolvedColorGraphic.color.a;
    }

    private void LateUpdate() => RefreshAutoResolve();

    private void RefreshAutoResolve()
    {
        EnsureInitialized();

        if (!TryWarnBarRoot())
            return;

        var gm = GameManager_Racing.Instance;
        CarController c = null;
        if (gm != null && gm.ActiveCar != null)
            c = gm.ActiveCar;
        else if (car != null)
            c = car;

        if (c == null)
        {
            barRoot.SetActive(false);
            return;
        }

        ApplyForCar(c, gm);
    }

    /// <summary>
    /// Called from <see cref="UIManager_Racing"/> (always-enabled) with the same car as fuel/HP.
    /// Pass null when not in the in-run HUD (skill tree, loading, run end) to hide the bar — no fallback to ActiveCar.
    /// </summary>
    public void RefreshFromHud(CarController hudBoundCar)
    {
        EnsureInitialized();

        if (!TryWarnBarRoot())
            return;

        if (hudBoundCar == null)
        {
            barRoot.SetActive(false);
            return;
        }

        ApplyForCar(hudBoundCar, GameManager_Racing.Instance);
    }

    private bool TryWarnBarRoot()
    {
        if (barRoot != null)
            return true;
        if (!_warnedMissingBarRoot)
        {
            _warnedMissingBarRoot = true;
            Debug.LogWarning("[DriftBoostHoldBarUI] barRoot is not assigned; drift charge bar will not appear.", this);
        }
        return false;
    }

    private void ApplyForCar(CarController c, GameManager_Racing gm)
    {
        bool gameplayLive = gm == null || !requireGameplayLive || gm.IsGameplayLive;
        if (!gameplayLive)
            _gameplayLiveSinceUnscaled = -1f;
        else if (_gameplayLiveSinceUnscaled < 0f)
            _gameplayLiveSinceUnscaled = Time.unscaledTime;

        bool settleOk = hudSettleDelayUnscaled <= 0f
            || gm == null
            || !requireGameplayLive
            || _gameplayLiveSinceUnscaled < 0f
            || (Time.unscaledTime - _gameplayLiveSinceUnscaled >= hudSettleDelayUnscaled);

        bool show = c.DriftHeldBoostChargeBarVisible && gameplayLive && settleOk;
        barRoot.SetActive(show);

        if (!show)
            return;

        float fill = c.DriftHeldBoostHoldFillNormalized;
        if (fillSlider != null)
            fillSlider.normalizedValue = fill;
        if (fillImage != null)
            fillImage.fillAmount = fill;

        if (minHoldMarker != null)
        {
            var a = minHoldMarker.anchorMin;
            var b = minHoldMarker.anchorMax;
            float x = c.DriftHeldBoostMinHoldMarker01;
            a.x = x;
            b.x = x;
            minHoldMarker.anchorMin = a;
            minHoldMarker.anchorMax = b;
        }

        ApplyChargeColor(c, fill);
    }

    private void ApplyChargeColor(CarController c, float fillNormalized)
    {
        if (_resolvedColorGraphic == null)
            return;

        float min01 = Mathf.Clamp01(c.DriftHeldBoostMinHoldMarker01);
        Color rgb = ChargeTintRgb(fillNormalized, min01);
        rgb.a = _chargeGraphicBaseAlpha;
        _resolvedColorGraphic.color = rgb;
    }

    /// <summary>
    /// Hard red below min boost threshold, hard green once earnable (no yellow blend).
    /// </summary>
    private Color ChargeTintRgb(float fill, float minMarker01)
    {
        if (minMarker01 < 0.001f)
            return fill > 0.001f ? maxBoostColor : notEnoughColor;

        return fill >= minMarker01 ? maxBoostColor : notEnoughColor;
    }
}
