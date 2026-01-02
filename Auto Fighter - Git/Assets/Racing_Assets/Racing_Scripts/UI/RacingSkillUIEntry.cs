using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class RacingSkillUIEntry : MonoBehaviour
{
    [Header("UI")]
    [SerializeField] private TMP_Text nameText;
    [SerializeField] private TMP_Text descText;
    [SerializeField] private TMP_Text levelText;
    [SerializeField] private TMP_Text effectText;
    [SerializeField] private TMP_Text costText;
    [SerializeField] private Button buyButton; // disabled (buy in detail panel)

    [SerializeField] private Image button; // disabled (buy in detail panel)

    [SerializeField] private Color affordableColor = Color.green;
    [SerializeField] private Color unaffordableColor = Color.red;
    [SerializeField] private Color maxedColor = new Color(0.7f, 0.7f, 0.7f, 1f);

    private SkillDefinition def;
    private RacingSkillTreeManager mgr;

    public System.Action<SkillDefinition> Selected;

    public SkillDefinition GetDefinition() => def;

    public void Bind(SkillDefinition definition, RacingSkillTreeManager manager)
    {
        def = definition;
        mgr = manager;

        if (nameText) nameText.text = def.displayName;
        if (descText) descText.text = def.description;

        if (buyButton)
        {
            buyButton.onClick.RemoveAllListeners();
            buyButton.interactable = false;
            buyButton.gameObject.SetActive(false);
        }

        var rootBtn = GetComponent<Button>();
        if (rootBtn)
        {
            rootBtn.onClick.RemoveAllListeners();
            rootBtn.onClick.AddListener(() => { if (def != null) Selected?.Invoke(def); });
        }

        Refresh();
    }

    public void Refresh()
    {
        if (!def || mgr == null) return;

        int lvl = mgr.GetLevel(def.type);
        bool isMaxed = lvl >= def.maxLevel;

        if (levelText) levelText.text = $"Lv {lvl}/{def.maxLevel}";
        if (effectText) effectText.text = $"Effect: {FormatEffect(def.type)}";

        if (!costText) return;

        if (isMaxed)
        {
            costText.text = "Maxed";
            costText.color = maxedColor;
            return;
        }

        // Show cost with currency type
        int cost = mgr.GetNextLevelCostSmart(def.type);
        string currencyName = mgr.GetCurrencyNameForSkill(def.type);
        costText.text = $"{cost} {currencyName}";

        // Color based on affordability
        bool canAfford = mgr.CanAffordNextLevel(def.type);
        costText.color = canAfford ? affordableColor : unaffordableColor;
    }

    public void ColorChange()
    {
        if (!def || mgr == null) return;

        int lvl = mgr.GetLevel(def.type);
        bool isMaxed = lvl >= def.maxLevel;

        if (button)
        {
            if (isMaxed)
            {
                button.color = maxedColor;
            }
            else
            {
                // Use smart affordability check
                bool canAfford = mgr.CanAffordNextLevel(def.type);
                button.color = canAfford ? affordableColor : unaffordableColor;
            }
        }
    }

    public void LateUpdate()
    {
        ColorChange();
    }

    private int GetRelevantCurrency()
    {
        if (mgr == null || def == null) return 0;
        return def.usesSprockets ? mgr.Sprockets : mgr.Currency;
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
                return $"Spawn x{mgr.GetCoinSpawnRateMultiplier():0.##}";
            case SkillType.CoinDoubleChance_Add:
            case SkillType.CoinDoubleChance_Mul:
                return $"Double {(mgr.GetCoinDoubleChance() * 100f):0.#}%";
            default:
                return "x1";
        }
    }
}