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

    private SkillDefinition def;
    private RacingSkillTreeManager mgr;

    public SkillType Type => def ? def.type : default;

    // Selection event
    public System.Action<SkillDefinition> Selected;

    // NEW: definition accessor (fixes GetDefinition compile error)
    public SkillDefinition GetDefinition() => def;
    // (Alternative usage later: public SkillDefinition Definition => def;)

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
        if (levelText) levelText.text = $"Lv {lvl}/{def.maxLevel}";
        if (effectText) effectText.text = $"Effect: {FormatEffect(def.type)}";

        if (costText)
        {
            if (lvl >= def.maxLevel) costText.text = "Maxed";
            else costText.text = $"Cost: {mgr.GetNextLevelCost(def.type)}";
        }
    }

    public void UpdateInteractable() { /* no-op (buy moved to panel) */ }

    private string FormatEffect(SkillType type)
    {
        float m = 1f;
        switch (type)
        {
            case SkillType.Acceleration: m = mgr.GetAccelerationMultiplier(); break;
            case SkillType.MaxSpeed: m = mgr.GetMaxSpeedMultiplier(); break;
            case SkillType.FuelEfficiency: m = mgr.GetFuelEfficiencyMultiplier(); break;
            case SkillType.SteeringResponsiveness: m = mgr.GetSteeringMultiplier(); break;
            default: m = 1f; break;
        }
        return $"x{m:0.##}";
    }
}