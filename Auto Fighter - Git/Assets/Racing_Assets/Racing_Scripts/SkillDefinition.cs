using UnityEngine;
using System.Collections.Generic;

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

    [Header("Unlock Visibility")]
    [Tooltip("If true this skill is visible from the start of a new run.")]
    public bool revealedAtStart = false;

    [System.Serializable]
    public class ProgressiveUnlock
    {
        [Min(1), Tooltip("When THIS skill reaches this level, unlock the listed skill(s).")]
        public int requiredLevel = 1;
        [Tooltip("Skills revealed when requiredLevel is reached.")]
        public List<SkillDefinition> unlocks = new();
    }

    [Tooltip("Configure which other skills become visible as this skill levels up.")]
    public List<ProgressiveUnlock> progressiveUnlocks = new();

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
        return mode == SkillApplicationMode.Multiplicative ? v : v;
    }

    /// <summary>
    /// Returns all skills that should unlock at (or below) the passed newLevel.
    /// </summary>
    public IEnumerable<SkillDefinition> GetUnlocksForLevel(int newLevel)
    {
        if (progressiveUnlocks == null) yield break;
        foreach (var pu in progressiveUnlocks)
        {
            if (pu == null) continue;
            if (newLevel >= pu.requiredLevel && pu.unlocks != null)
                foreach (var s in pu.unlocks)
                    if (s) yield return s;
        }
    }
}