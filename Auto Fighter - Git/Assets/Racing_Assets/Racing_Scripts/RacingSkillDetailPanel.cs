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
    [SerializeField] private Button closeButton;       // Legacy (optional) — no longer used

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
    public GameObject InfoContainer => infoContainer != null ? infoContainer : null;

    public void Init(RacingSkillTreeManager manager) => mgr = manager;

    void Awake()
    {
        if (!mgr) mgr = RacingSkillTreeManager.Instance;

        // Remove the full-screen "big button" behaviour that blocks clicks.
        // Instead, we make the visual backdrop inert (non-raycast) and install a global click catcher
        // on the skill tree root so clicks that land outside the info box will hide it while allowing
        // clicks on other skill buttons to still register.
        WireStaticBackdrop();

        // Ensure root stays active so backdrop can always catch clicks (if desired).
        if (root && !root.activeSelf) root.SetActive(true);
        if (infoContainer && infoContainer.activeSelf) { /* ok */ }

        // Ensure a RacingUISoundManager exists on the UI root (auto-create if missing)
        var sfxMgr = FindObjectOfType<RacingUISoundManager>();
        if (sfxMgr == null && root != null)
        {
            // Add manager to root so inspector-exposed clips can be assigned by designer
            sfxMgr = root.AddComponent<RacingUISoundManager>();
        }

        // Attach global click catcher to the root so we can detect clicks anywhere (without blocking raycasts).
        if (root != null)
        {
            var catcher = root.GetComponent<SkillTreeGlobalClickCatcher>();
            if (catcher == null) catcher = root.AddComponent<SkillTreeGlobalClickCatcher>();
            catcher.Init(this);
        }
    }

    private void WireStaticBackdrop()
    {
        if (backdrop == null) return;

        // If it's an Image (UI panel) make it visual-only (no raycast target) so it doesn't block clicks.
        var img = backdrop.GetComponent<UnityEngine.UI.Image>();
        if (img != null)
        {
            img.raycastTarget = false;
        }

        // If there's a Button component, remove its listeners so it won't swallow clicks.
        var btn = backdrop.GetComponent<Button>();
        if (btn != null)
        {
            btn.onClick.RemoveAllListeners();
            // remove the Button component entirely to avoid accidental blocking in editor
            DestroyImmediate(btn);
        }

        // Remove any legacy BackdropClickCatcher that relied on the backdrop receiving events.
        var oldCatcher = backdrop.GetComponent<BackdropClickCatcher>();
        if (oldCatcher != null)
            DestroyImmediate(oldCatcher);

        // Keep the backdrop GameObject as a visual only.
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
        bool purchased = mgr.TryPurchase(def.type);
        if (purchased)
        {
            // Play UI purchase SFXs if available
            var sfx = FindObjectOfType<RacingUISoundManager>();
            if (sfx != null)
            {
                sfx.PlayPurchaseSkill();
                sfx.PlayPurchaseCurrency(); // user said currency sound may be played along with purchase
            }
            Refresh();
        }
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
        var mgr = RacingSkillTreeManager.Instance;
        if (mgr == null) return "x1";

        switch (type)
        {
            case SkillType.Acceleration: return $"x{mgr.GetAccelerationMultiplier():0.##}";
            case SkillType.MaxSpeed: return $"x{mgr.GetMaxSpeedMultiplier():0.##}";
            case SkillType.FuelEfficiency: return $"x{mgr.GetFuelEfficiencyMultiplier():0.##}";
            case SkillType.SteeringResponsiveness: return $"x{mgr.GetSteeringMultiplier():0.##}";
            case SkillType.CoinSpawnRate_Add:
            case SkillType.CoinSpawnRate_Mul:
                return $"SpawnRate x{mgr.GetCoinSpawnRateMultiplier():0.##}";
            case SkillType.CoinDoubleChance_Add:
            case SkillType.CoinDoubleChance_Mul:
                return $"Double Chance {(mgr.GetCoinDoubleChance() * 100f):0.#}%";
            default:
                return "x1";
        }
    }
}

public class BackdropClickCatcher : MonoBehaviour, IPointerClickHandler
{
    public Action onClicked;
    public void OnPointerClick(PointerEventData eventData) => onClicked?.Invoke();
}