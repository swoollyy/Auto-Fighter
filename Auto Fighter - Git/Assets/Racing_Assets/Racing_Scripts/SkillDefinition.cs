using UnityEngine;

[CreateAssetMenu(menuName = "Racing/Skill Definition", fileName = "SkillDefinition")]
public class SkillDefinition : ScriptableObject
{
    public SkillType type;
    public string displayName;
    [TextArea] public string description;

    [Min(1)] public int maxLevel = 10;

    [Header("Progression")]
    [Tooltip("If set, evaluates cost per level (x = level starting at 1). Overrides flatCost.")]
    public AnimationCurve costCurve;
    [Min(0)] public int flatCost = 10;

    [Header("Effect")]
    public SkillApplicationMode mode = SkillApplicationMode.Multiplicative;
    [Tooltip("Base additive or multiplicative value at level 0 (multiplicative: 1 = no change).")]
    public float baseValue = 1f;
    [Tooltip("Per level increment (add to base for additive, added then used as multiplier for multiplicative).")]
    public float perLevelAdd = 0.05f;

    [Header("UI Layout")]
    [Tooltip("Anchored position on the skill tree content canvas (in pixels).")]
    public Vector2 uiPosition = Vector2.zero;

    public int GetCostForLevel(int nextLevel)
    {
        if (nextLevel <= 0) nextLevel = 1;
        if (costCurve != null && costCurve.keys.Length > 0)
            return Mathf.Max(1, Mathf.RoundToInt(costCurve.Evaluate(nextLevel)));
        return flatCost;
    }

    public float GetValueAtLevel(int level)
    {
        level = Mathf.Clamp(level, 0, maxLevel);
        float v = baseValue + perLevelAdd * level;
        return mode == SkillApplicationMode.Multiplicative ? v : v; // semantics clarified in manager
    }
}