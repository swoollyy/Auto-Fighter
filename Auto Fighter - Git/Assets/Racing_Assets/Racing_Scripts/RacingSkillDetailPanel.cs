using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

public class RacingSkillDetailPanel : MonoBehaviour
{
    [Header("Root / Backdrop")]
    [SerializeField] private GameObject root;          // Stays active (overall panel parent / skill tree layer)
    [SerializeField] private GameObject backdrop;      // Clickable area to dismiss ONLY the info
    [SerializeField] private GameObject infoContainer; // NEW: the actual skill info box (card)
    [SerializeField] private Button closeButton;       // Legacy (optional) – no longer used

    [Header("Text Fields")]
    [SerializeField] private TMP_Text nameText;
    [SerializeField] private TMP_Text descText;
    [SerializeField] private TMP_Text levelText;
    [SerializeField] private TMP_Text effectText;
    [SerializeField] private TMP_Text costText;

    [Header("Actions")]
    [SerializeField] private Button buyButton;

    private RacingSkillTreeManager mgr;
    private SkillDefinition def;
    private bool wired;

    public event Action OnHidden; // Fired when infoContainer is hidden (selection cleared)

    public bool IsInfoVisible => infoContainer != null && infoContainer.activeSelf;

    public void Init(RacingSkillTreeManager manager) => mgr = manager;

    void Awake()
    {
        if (!mgr) mgr = RacingSkillTreeManager.Instance;
        WireStaticBackdrop();
        // Ensure root stays active so backdrop can always catch clicks (if desired).
        if (root && !root.activeSelf) root.SetActive(true);
        if (infoContainer && infoContainer.activeSelf) { /* ok */ }
    }

    private void WireStaticBackdrop()
    {
        if (backdrop != null)
        {
            var btn = backdrop.GetComponent<Button>();
            if (btn)
            {
                btn.onClick.RemoveAllListeners();
                btn.onClick.AddListener(HideInfo); // Only hide info now
            }
            else
            {
                var catcher = backdrop.GetComponent<BackdropClickCatcher>();
                if (!catcher) catcher = backdrop.AddComponent<BackdropClickCatcher>();
                catcher.onClicked = HideInfo;
            }
        }

        // Close button no longer needed; keep optional
        if (closeButton != null)
        {
            closeButton.onClick.RemoveAllListeners();
            closeButton.onClick.AddListener(HideInfo);
        }
    }

    /// <summary>
    /// Show (or update) the skill info for a given definition.
    /// </summary>
    public void Show(SkillDefinition definition)
    {
        def = definition;
        if (!def || mgr == null) return;

        if (root && !root.activeSelf) root.SetActive(true);
        if (infoContainer && !infoContainer.activeSelf) infoContainer.SetActive(true);

        if (!wired) WireLiveEvents();
        Refresh();
    }

    /// <summary>
    /// Hides only the infoContainer (skill detail content), keeps root active.
    /// </summary>
    public void HideInfo()
    {
        if (infoContainer) infoContainer.SetActive(false);
        UnwireLiveEvents();
        def = null;
        OnHidden?.Invoke();
    }

    /// <summary>
    /// Legacy full hide if ever needed elsewhere (not used by backdrop now).
    /// </summary>
    public void Hide()
    {
        if (infoContainer) infoContainer.SetActive(false);
        if (root) root.SetActive(false);
        UnwireLiveEvents();
        def = null;
        OnHidden?.Invoke();
    }

    /// <summary>
    /// Immediate full hide (same as Hide, bypassing transitions).
    /// </summary>
    public void HideImmediate()
    {
        if (infoContainer) infoContainer.SetActive(false);
        if (root) root.SetActive(false);
        UnwireLiveEvents();
        def = null;
        OnHidden?.Invoke();
    }

    private void WireLiveEvents()
    {
        if (wired || mgr == null) return;
        mgr.OnCurrencyChanged += HandleCurrencyChanged;
        mgr.OnLevelChanged += HandleLevelChanged;
        if (buyButton)
        {
            buyButton.onClick.RemoveAllListeners();
            buyButton.onClick.AddListener(OnBuyClicked);
        }
        wired = true;
    }

    private void UnwireLiveEvents()
    {
        if (!wired || mgr == null) return;
        mgr.OnCurrencyChanged -= HandleCurrencyChanged;
        mgr.OnLevelChanged -= HandleLevelChanged;
        if (buyButton) buyButton.onClick.RemoveAllListeners();
        wired = false;
    }

    private void HandleCurrencyChanged(int _) => Refresh();
    private void HandleLevelChanged(SkillType _, int __) => Refresh();

    private void OnBuyClicked()
    {
        if (mgr == null || def == null) return;
        if (mgr.TryPurchase(def.type))
            Refresh();
    }

    private void Refresh()
    {
        if (def == null || mgr == null || infoContainer == null || !infoContainer.activeSelf)
            return;

        int lvl = mgr.GetLevel(def.type);
        if (nameText) nameText.text = def.displayName;
        if (descText) descText.text = def.description;
        if (levelText) levelText.text = $"Lv {lvl}/{def.maxLevel}";
        if (effectText) effectText.text = $"Effect: {FormatEffect(def.type)}";

        if (costText)
        {
            costText.text = (lvl >= def.maxLevel) ? "Maxed" : $"Cost: {mgr.GetNextLevelCost(def.type)}";
        }

        if (buyButton)
        {
            bool canBuy = false;
            if (lvl < def.maxLevel)
            {
                int cost = mgr.GetNextLevelCost(def.type);
                canBuy = cost > 0 && mgr.Currency >= cost;
            }
            buyButton.interactable = canBuy;
        }
    }

    private string FormatEffect(SkillType type)
    {
        float m = type switch
        {
            SkillType.Acceleration => mgr.GetAccelerationMultiplier(),
            SkillType.MaxSpeed => mgr.GetMaxSpeedMultiplier(),
            SkillType.FuelEfficiency => mgr.GetFuelEfficiencyMultiplier(),
            SkillType.SteeringResponsiveness => mgr.GetSteeringMultiplier(),
            _ => 1f
        };
        return $"x{m:0.##}";
    }
}

/// <summary>
/// Simple click catcher if backdrop has no Button.
/// </summary>
public class BackdropClickCatcher : MonoBehaviour, IPointerClickHandler
{
    public Action onClicked;
    public void OnPointerClick(PointerEventData eventData) => onClicked?.Invoke();
}