using System;
using System.Collections.Generic;
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
    [SerializeField] private Button closeButton;       // Legacy (optional) � no longer used

    [Header("Text Fields")]
    [SerializeField] private TMP_Text nameText;
    [SerializeField] private TMP_Text descText;
    [SerializeField] private TMP_Text levelText;
    [SerializeField] private TMP_Text effectText;
    [SerializeField] private TMP_Text costText;

    [Header("Actions")]
    [SerializeField] private Button buyButton;

    [Header("Click-outside / hierarchy")]
    [Tooltip("If Buy (or other detail controls) are NOT children of Info Container, add their parent Transforms here so clicks there are not treated as 'outside' the detail UI.")]
    [SerializeField] private List<Transform> extraDetailUiRoots = new List<Transform>();

    private const string BuyRaycastBlockerChildName = "BuyRaycastBlocker";

    private RacingSkillTreeManager mgr;
    private SkillDefinition def;
    private bool wired;

    public event Action OnHidden; // Fired when infoContainer is hidden (selection cleared)

    public bool IsInfoVisible => infoContainer != null && infoContainer.activeSelf;
    public GameObject InfoContainer => infoContainer != null ? infoContainer : null;

    /// <summary>Rect used by tutorial cost spotlights (Cost_Text). Requires the detail card to be open.</summary>
    public bool TryGetCostHighlightRect(out RectTransform rect)
    {
        rect = null;
        if (!IsInfoVisible || costText == null) return false;
        rect = costText.rectTransform;
        return rect != null;
    }

    /// <summary>True if this raycast hit is part of the detail UX (card, Buy row, dimmed backdrop, optional extra roots).</summary>
    public bool IsHitInsideDetailUi(GameObject hitObject)
    {
        if (hitObject == null) return false;
        Transform t = hitObject.transform;
        if (backdrop != null && IsDescendantOf(t, backdrop.transform)) return true;
        if (infoContainer != null && IsDescendantOf(t, infoContainer.transform)) return true;
        if (buyButton != null && IsDescendantOf(t, buyButton.transform)) return true;
        if (extraDetailUiRoots != null)
        {
            for (int i = 0; i < extraDetailUiRoots.Count; i++)
            {
                Transform root = extraDetailUiRoots[i];
                if (root != null && IsDescendantOf(t, root)) return true;
            }
        }

        return false;
    }

    private static bool IsDescendantOf(Transform t, Transform ancestor)
    {
        while (t != null)
        {
            if (t == ancestor) return true;
            t = t.parent;
        }
        return false;
    }

    public void Init(RacingSkillTreeManager manager) => mgr = manager;

    void Awake()
    {
        if (!mgr) mgr = RacingSkillTreeManager.Instance;

        // Reverted global SkillTreeGlobalClickCatcher: it caused stray toolbar clicks when dismissing detail.
        // Dismiss by clicking the dimmed backdrop (must sit behind tree + card in hierarchy so nodes still receive clicks).
        WireBackdropDismiss();

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

        EnsureDetailTextsNonBlocking();
        ApplyBuyButtonIdleState();
    }

    /// <summary>
    /// TMP defaults to Raycast Target on; after Buy, refreshed text/layout can steal hits from the tree.
    /// </summary>
    private void EnsureDetailTextsNonBlocking()
    {
        void StripTmp(Transform root)
        {
            if (root == null) return;
            foreach (var tmp in root.GetComponentsInChildren<TMP_Text>(true))
            {
                if (tmp != null) tmp.raycastTarget = false;
            }
        }

        StripTmp(infoContainer != null ? infoContainer.transform : null);
        StripTmp(buyButton != null ? buyButton.transform : null);
        if (extraDetailUiRoots != null)
        {
            for (int i = 0; i < extraDetailUiRoots.Count; i++)
                StripTmp(extraDetailUiRoots[i]);
        }

        // After stripping TMP raycasts, Buy may have no blocking Graphic (common if targetGraphic was text).
        // Also keep a full-rect raycast target under labels so hits never fall through to toolbar behind the card.
        EnsureBuyButtonAlwaysRaycasts();
    }

    /// <summary>
    /// Ensures the Buy button rect always participates in GraphicRaycaster so disabled clicks do not pass through.
    /// </summary>
    private void EnsureBuyButtonAlwaysRaycasts()
    {
        if (buyButton == null) return;
        var buttonRt = buyButton.transform as RectTransform;
        if (buttonRt == null) return;

        var selfImage = buyButton.GetComponent<Image>();
        if (selfImage != null)
            selfImage.raycastTarget = true;

        if (!TransformHasAnyRaycastTargetGraphic(buyButton.transform))
            CreateOrRefreshBuyRaycastBlocker(buttonRt);

        var tg = buyButton.targetGraphic;
        if (tg == null || !tg.raycastTarget)
        {
            Image blockGraphic = selfImage != null && selfImage.raycastTarget ? selfImage : null;
            if (blockGraphic == null)
            {
                var blockerTr = buttonRt.Find(BuyRaycastBlockerChildName);
                if (blockerTr != null)
                    blockGraphic = blockerTr.GetComponent<Image>();
            }

            if (blockGraphic == null)
            {
                foreach (var img in buyButton.GetComponentsInChildren<Image>(true))
                {
                    if (img != null && img.raycastTarget)
                    {
                        blockGraphic = img;
                        break;
                    }
                }
            }

            if (blockGraphic != null)
            {
                blockGraphic.raycastTarget = true;
                buyButton.targetGraphic = blockGraphic;
            }
        }
    }

    private static bool TransformHasAnyRaycastTargetGraphic(Transform root)
    {
        if (root == null) return false;
        var graphics = root.GetComponentsInChildren<Graphic>(true);
        for (int i = 0; i < graphics.Length; i++)
        {
            var g = graphics[i];
            if (g != null && g.raycastTarget && g.gameObject.activeSelf)
                return true;
        }
        return false;
    }

    private static void CreateOrRefreshBuyRaycastBlocker(RectTransform buttonRt)
    {
        Transform existing = buttonRt.Find(BuyRaycastBlockerChildName);
        RectTransform blockerRt;
        Image img;

        if (existing != null)
        {
            blockerRt = existing as RectTransform;
            img = existing.GetComponent<Image>();
            if (blockerRt == null || img == null) return;
            if (existing.GetComponent<LayoutElement>() == null)
                existing.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
        }
        else
        {
            var go = new GameObject(BuyRaycastBlockerChildName, typeof(RectTransform), typeof(Image), typeof(LayoutElement));
            go.transform.SetParent(buttonRt, false);
            blockerRt = go.GetComponent<RectTransform>();
            img = go.GetComponent<Image>();
            var le = go.GetComponent<LayoutElement>();
            le.ignoreLayout = true;
        }

        blockerRt.SetAsFirstSibling();
        blockerRt.anchorMin = Vector2.zero;
        blockerRt.anchorMax = Vector2.one;
        blockerRt.offsetMin = Vector2.zero;
        blockerRt.offsetMax = Vector2.zero;
        blockerRt.localScale = Vector3.one;
        img.color = new Color(1f, 1f, 1f, 0f);
        img.raycastTarget = true;
    }

    private void WireBackdropDismiss()
    {
        if (backdrop == null) return;

        var img = backdrop.GetComponent<Image>();
        if (img == null)
        {
            img = backdrop.AddComponent<Image>();
            img.color = new Color(0f, 0f, 0f, 0.5f);
        }

        img.raycastTarget = true;

        // Button + IPointerClick can double-fire; use a simple click catcher on the backdrop Image.
        var btn = backdrop.GetComponent<Button>();
        if (btn != null)
        {
            btn.onClick.RemoveAllListeners();
            Destroy(btn);
        }

        var catcher = backdrop.GetComponent<BackdropClickCatcher>();
        if (catcher == null) catcher = backdrop.AddComponent<BackdropClickCatcher>();
        catcher.onClicked = OnBackdropPointerDismissDetail;
    }

    private void OnBackdropPointerDismissDetail()
    {
        if (!IsInfoVisible) return;
        EventSystem.current?.SetSelectedGameObject(null);
        HideInfo();
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
        if (buyButton) buyButton.gameObject.SetActive(true);

        if (!wired) WireLiveEvents();
        EnsureDetailTextsNonBlocking();
        Refresh();
    }

    /// <summary>
    /// Hides only the infoContainer (skill detail content), keeps root active.
    /// </summary>
    public void HideInfo()
    {
        if (infoContainer) infoContainer.SetActive(false);
        if (buyButton) buyButton.gameObject.SetActive(true);
        ApplyBuyButtonIdleState();
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
        if (buyButton) buyButton.gameObject.SetActive(true);
        ApplyBuyButtonIdleState();
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
        if (buyButton) buyButton.gameObject.SetActive(true);
        ApplyBuyButtonIdleState();
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

        EventSystem.current?.SetSelectedGameObject(null);
        if (!IsPurchasePossibleNow())
            return;

        // Use smart purchase that checks usesSprockets flag
        bool purchased = mgr.TryPurchaseSmart(def.type);

        if (purchased)
        {
            // Play UI purchase SFXs if available
            var sfx = FindObjectOfType<RacingUISoundManager>();
            if (sfx != null)
            {
                sfx.PlayPurchaseSkill();
                sfx.PlayPurchaseCurrency();
            }
            Refresh();
            EnsureDetailTextsNonBlocking();

            // Clear selection if it was the Buy button (or its child) so the next click isn't confused.
            var es = EventSystem.current;
            if (es != null && buyButton != null && es.currentSelectedGameObject != null)
            {
                Transform sel = es.currentSelectedGameObject.transform;
                Transform bt = buyButton.transform;
                if (sel == bt || sel.IsChildOf(bt))
                    es.SetSelectedGameObject(null);
            }
        }
    }

    /// <summary>True when a skill is shown and the player can buy the next level right now.</summary>
    private bool IsPurchasePossibleNow()
    {
        if (def == null || mgr == null) return false;
        int lvl = mgr.GetLevel(def.type);
        if (lvl >= def.maxLevel) return false;
        if (!mgr.IsQuestGateSatisfiedForSkill(def.type)) return false;
        return mgr.CanAffordNextLevel(def.type);
    }

    private void Refresh()
    {
        if (def == null || mgr == null || infoContainer == null || !infoContainer.activeSelf)
            return;

        int lvl = mgr.GetLevel(def.type);
        if (nameText) nameText.text = def.displayName;
        if (descText) descText.text = def.description;
        if (levelText) levelText.text = $"Lv {lvl}/{def.maxLevel}";
        if (effectText) effectText.text = FormatEffect(def.type);

        // Cost display with currency type
        if (costText)
        {
            if (lvl >= def.maxLevel)
            {
                costText.text = "Maxed";
            }
            else if (!mgr.IsQuestGateSatisfiedForSkill(def.type))
            {
                costText.text = "Quest Locked";
            }
            else
            {
                int cost = mgr.GetNextLevelCostSmart(def.type);
                string currencyName = mgr.GetCurrencyNameForSkill(def.type);
                costText.text = $"Cost: {cost} {currencyName}";
            }
        }

        // Buy only when a real purchase is possible; keep CanvasGroup so disabled state still blocks raycasts
        // (interactable=false alone can let clicks fall through to the toolbar).
        if (buyButton)
        {
            bool canBuy = IsPurchasePossibleNow();
            buyButton.navigation = new Navigation { mode = Navigation.Mode.None };
            buyButton.interactable = canBuy;

            var cg = buyButton.GetComponent<CanvasGroup>();
            if (cg == null) cg = buyButton.gameObject.AddComponent<CanvasGroup>();
            cg.interactable = canBuy;
            cg.blocksRaycasts = true;
            cg.alpha = canBuy ? 1f : 0.42f;

            if (buyButton.targetGraphic != null)
                buyButton.targetGraphic.raycastTarget = true;
        }

        EnsureDetailTextsNonBlocking();
    }

    private void ApplyBuyButtonIdleState()
    {
        if (buyButton == null) return;
        buyButton.interactable = false;
        buyButton.navigation = new Navigation { mode = Navigation.Mode.None };

        var cg = buyButton.GetComponent<CanvasGroup>();
        if (cg == null) cg = buyButton.gameObject.AddComponent<CanvasGroup>();
        cg.interactable = false;
        cg.blocksRaycasts = true;
        cg.alpha = 0.42f;

        if (buyButton.targetGraphic != null)
            buyButton.targetGraphic.raycastTarget = true;
    }

    /// <summary>
    /// Formats the effect text showing: CurrentStat + Upgrade -> NewStat
    /// Example: "100 + 15 -> 115" for Max Fuel
    /// </summary>
    private string FormatEffect(SkillType type)
    {
        if (mgr == null || def == null) return "---";

        int currentLevel = mgr.GetLevel(def.type);
        int nextLevel = currentLevel + 1;
        bool isMaxed = currentLevel >= def.maxLevel;

        // Get the actual current stat and projected stat after upgrade
        float currentStat = GetCurrentStatValue(def.type);
        float nextStat = isMaxed ? currentStat : GetStatValueAtLevel(def.type, nextLevel);
        float upgradeAmount = nextStat - currentStat;

        // Determine format based on skill type
        string format = GetFormatForSkillType(type);
        string suffix = GetSuffixForSkillType(type);

        // Format output
        if (isMaxed)
        {
            return $"{currentStat.ToString(format)}{suffix} (MAX)";
        }

        string sign = upgradeAmount >= 0 ? "+" : "";
        return $"{currentStat.ToString(format)}{suffix} {sign}{upgradeAmount.ToString(format)} -> {nextStat.ToString(format)}{suffix}";
    }

    private string GetFormatForSkillType(SkillType type)
    {
        switch (type)
        {
            // Percentage/chance types - show with 1 decimal
            case SkillType.CoinDoubleChance_Add:
            case SkillType.CoinDoubleChance_Mul:
                return "0.#";

            // Multiplier types - show with 2 decimals
            case SkillType.CoinSpawnRate_Add:
            case SkillType.CoinSpawnRate_Mul:
            case SkillType.Acceleration:
            case SkillType.Acceleration_Add:
            case SkillType.Acceleration_Mul:
            case SkillType.MaxSpeed:
            case SkillType.MaxSpeed_Add:
            case SkillType.MaxSpeed_Mul:
                return "0.##";

            // Integer-like values
            case SkillType.MaxHP_Add:
            case SkillType.MaxHP_Mul:
            case SkillType.MaxFuel_Add:
            case SkillType.MaxFuel_Mul:
            case SkillType.MashClicksPerClick_Add:
            case SkillType.MashClicksPerClick_Mul:
            case SkillType.MashPassiveClickStrength_Add:
            case SkillType.MashPassiveClickStrength_Mul:
                return "0";

            // Default
            default:
                return "0.##";
        }
    }

    private string GetSuffixForSkillType(SkillType type)
    {
        switch (type)
        {
            // Percentage types
            case SkillType.CoinDoubleChance_Add:
            case SkillType.CoinDoubleChance_Mul:
                return "%";

            // Multiplier types
            case SkillType.CoinSpawnRate_Add:
            case SkillType.CoinSpawnRate_Mul:
                return "x";

            // Time-based
            case SkillType.BoostDuration_Add:
            case SkillType.BoostDuration_Mul:
            case SkillType.BoostCooldown_Add:
            case SkillType.BoostCooldown_Mul:
                return "s";

            // Rate (per second)
            case SkillType.MashPassiveClickRate_Add:
            case SkillType.MashPassiveClickRate_Mul:
            case SkillType.HPRegen_Add:
            case SkillType.HPRegen_Mul:
                return "/s";

            default:
                return "";
        }
    }

    /// <summary>
    /// Gets the current actual stat value for a skill type.
    /// </summary>
    private float GetCurrentStatValue(SkillType type)
    {
        // Prefer the active runtime car only; skill-tree view must work without a car instance.
        var gm = GameManager_Racing.Instance;
        var car = gm != null ? gm.ActiveCar : null;

        switch (type)
        {
            // === CORE STATS (read from car's base values) ===
            case SkillType.Acceleration:
            case SkillType.Acceleration_Add:
            case SkillType.Acceleration_Mul:
                float baseAccel = car != null ? car.BaseAcceleration : 10f;
                return mgr.ApplyStatChain(baseAccel, SkillType.Acceleration_Add, SkillType.Acceleration_Mul);

            case SkillType.MaxSpeed:
            case SkillType.MaxSpeed_Add:
            case SkillType.MaxSpeed_Mul:
                float baseSpeed = car != null ? car.BaseMaxSpeed : 20f;
                return mgr.ApplyStatChain(baseSpeed, SkillType.MaxSpeed_Add, SkillType.MaxSpeed_Mul);

            case SkillType.MaxFuel_Add:
            case SkillType.MaxFuel_Mul:
                float baseFuel = car != null ? car.BaseMaxFuel : 100f;
                return mgr.ApplyStatChain(baseFuel, SkillType.MaxFuel_Add, SkillType.MaxFuel_Mul);

            case SkillType.MaxHP_Add:
            case SkillType.MaxHP_Mul:
                float baseHP = car != null ? car.BaseMaxHP : 100f;
                return mgr.ApplyStatChain(baseHP, SkillType.MaxHP_Add, SkillType.MaxHP_Mul);

            case SkillType.TurnSpeed_Add:
            case SkillType.TurnSpeed_Mul:
                float baseTurn = car != null ? car.BaseTurnSpeed : 100f;
                return mgr.ApplyStatChain(baseTurn, SkillType.TurnSpeed_Add, SkillType.TurnSpeed_Mul);

            case SkillType.DrivingFuelUse_Add:
            case SkillType.DrivingFuelUse_Mul:
                float baseDriving = car != null ? car.BaseDrivingFuelUse : 2f;
                return mgr.ApplyStatChain(baseDriving, SkillType.DrivingFuelUse_Add, SkillType.DrivingFuelUse_Mul);

            case SkillType.HPRegen_Add:
            case SkillType.HPRegen_Mul:
                float baseRegen = car != null ? car.BaseHPRegen : 0f;
                return mgr.ApplyStatChain(baseRegen, SkillType.HPRegen_Add, SkillType.HPRegen_Mul);

            // === BOOST STATS ===
            case SkillType.BoostForce_Add:
            case SkillType.BoostForce_Mul:
                float baseBoost = car != null ? car.BaseBoostForce : 50f;
                return mgr.ApplyStatChain(baseBoost, SkillType.BoostForce_Add, SkillType.BoostForce_Mul);

            case SkillType.BoostDuration_Add:
            case SkillType.BoostDuration_Mul:
                float baseDur = car != null ? car.BaseBoostDuration : 1f;
                return mgr.ApplyStatChain(baseDur, SkillType.BoostDuration_Add, SkillType.BoostDuration_Mul);

            case SkillType.BoostCooldown_Add:
            case SkillType.BoostCooldown_Mul:
                float baseCD = car != null ? car.BaseBoostCooldown : 3f;
                return mgr.ApplyStatChain(baseCD, SkillType.BoostCooldown_Add, SkillType.BoostCooldown_Mul);

            case SkillType.BoostFuelCost_Add:
            case SkillType.BoostFuelCost_Mul:
                float baseCost = car != null ? car.BaseBoostFuelCost : 10f;
                return mgr.ApplyStatChain(baseCost, SkillType.BoostFuelCost_Add, SkillType.BoostFuelCost_Mul);

            // === MASH SKILLS ===
            case SkillType.MashClicksPerClick_Add:
            case SkillType.MashClicksPerClick_Mul:
                float baseClicks = car != null ? car.BaseClicksPerClick : 1f;
                return mgr.ApplyStatChain(baseClicks, SkillType.MashClicksPerClick_Add, SkillType.MashClicksPerClick_Mul);

            case SkillType.MashPassiveClickRate_Add:
            case SkillType.MashPassiveClickRate_Mul:
                float baseRate = car != null ? car.BasePassiveClickRate : 0f;
                return mgr.ApplyStatChain(baseRate, SkillType.MashPassiveClickRate_Add, SkillType.MashPassiveClickRate_Mul);

            case SkillType.MashPassiveClickStrength_Add:
            case SkillType.MashPassiveClickStrength_Mul:
                float baseStrength = car != null ? car.BasePassiveClickStrength : 1f;
                return mgr.ApplyStatChain(baseStrength, SkillType.MashPassiveClickStrength_Add, SkillType.MashPassiveClickStrength_Mul);

            case SkillType.MashFuelPerClick_Add:
            case SkillType.MashFuelPerClick_Mul:
                float baseMashFuel = car != null ? car.BaseMashFuelPerClick : 0.3f;
                return mgr.ApplyStatChain(baseMashFuel, SkillType.MashFuelPerClick_Add, SkillType.MashFuelPerClick_Mul);

            case SkillType.CoinSpawnRate_Add:
            case SkillType.CoinSpawnRate_Mul:
                // Base is 1.0 (100%), skill modifies it
                // Show as multiplier (e.g., 1.0 -> 1.15 -> 1.3)
                return mgr.ApplyStatChain(1f, SkillType.CoinSpawnRate_Add, SkillType.CoinSpawnRate_Mul);

            case SkillType.CoinDoubleChance_Add:
            case SkillType.CoinDoubleChance_Mul:
                // Base is 0% chance, skill adds/multiplies
                // Show as percentage (e.g., 0 -> 5 -> 10)
                return mgr.ApplyStatChain(0f, SkillType.CoinDoubleChance_Add, SkillType.CoinDoubleChance_Mul) * 100f;

            // === UNLOCK SKILLS (just show level) ===
            case SkillType.BoostUnlock:
            case SkillType.DriftUnlock:
            case SkillType.TurretUnlock:
            case SkillType.ForcefieldUnlock:
            case SkillType.FuelPickupUnlock:
            case SkillType.HPPickupUnlock:
                return mgr.GetLevel(type);

            // === DEFAULT: Use raw skill value ===
            default:
                return def.GetValueAtLevel(mgr.GetLevel(def.type));
        }
    }

    private float GetStatValueAtLevel(SkillType type, int level)
    {
        float currentSkillValue = def.GetValueAtLevel(mgr.GetLevel(def.type));
        float nextSkillValue = def.GetValueAtLevel(level);
        float skillDelta = nextSkillValue - currentSkillValue;

        float currentStat = GetCurrentStatValue(type);

        // Special handling for percentage-based skills
        switch (type)
        {
            case SkillType.CoinSpawnRate_Add:
            case SkillType.CoinSpawnRate_Mul:
                // These start at base 1.0 and modify
                if (def.mode == SkillApplicationMode.Multiplicative)
                {
                    if (currentSkillValue > 0.001f)
                        return currentStat * (nextSkillValue / currentSkillValue);
                    return currentStat + skillDelta;
                }
                return currentStat + skillDelta;

            case SkillType.CoinDoubleChance_Add:
            case SkillType.CoinDoubleChance_Mul:
                // These are shown as percentages (0-100)
                // Add the skill delta * 100 for additive
                if (def.mode == SkillApplicationMode.Additive)
                    return currentStat + (skillDelta * 100f);
                // Multiplicative
                if (currentSkillValue > 0.001f)
                    return currentStat * (nextSkillValue / currentSkillValue);
                return currentStat + (skillDelta * 100f);
        }

        // Standard handling for other skills
        if (def.mode == SkillApplicationMode.Multiplicative)
        {
            if (currentSkillValue > 0.001f)
                return currentStat * (nextSkillValue / currentSkillValue);
            return currentStat + skillDelta;
        }

        return currentStat + skillDelta;
    }

    public class BackdropClickCatcher : MonoBehaviour, IPointerClickHandler
    {
        public Action onClicked;
        public void OnPointerClick(PointerEventData eventData) => onClicked?.Invoke();
    }
}