using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public abstract class RewardSO : ScriptableObject
{
    [Header("Identity")]
    [SerializeField] private string rewardID;
    [SerializeField] private string displayName;
    [TextArea, SerializeField] private string description;

    [Header("Classification")]
    [SerializeField] private RewardCategory category;
    [SerializeField] private RewardRarity rarity;
    [SerializeField] public bool isPaddleReward;

    [Header("Behavior")]
    [Tooltip("If true, this reward should not be offered if an instance is currently active")]
    [SerializeField] private bool blockWhenActive = true;
    [SerializeField] private bool canStack = true;

    [Header("Scaling")]
    [SerializeField] private bool scalable = false;
    [SerializeField] private RewardSO replacesReward;

    [Tooltip("Exclusive group key. Rewards sharing same key cannot co-exist")]
    [SerializeField] private string exclusivityKey;
    [SerializeField] private List<string> blockedKeys;

    public string Id => rewardID;
    public string Name => displayName;
    public string Description => description;
    public RewardCategory Category => category;
    public RewardRarity Rarity => rarity;
    public bool BlockWhenActive => blockWhenActive;
    public bool CanStack => canStack;
    public string ExclusivityKey => exclusivityKey;
    public List<string> BlockedKeys => blockedKeys;
    public bool Scalable => scalable;
    public RewardSO ReplacesReward => replacesReward;

    // Rarity color map (kept simple & centralized)
    public static Color GetRarityColor(RewardRarity r)
    {
        // Common gray, Uncommon green, Rare blue, Epic magenta-purple,
        // Legendary orange, Artifact pink, Cursed dark purple.
        return r switch
        {
            RewardRarity.Common => new Color32(160, 160, 160, 255),
            RewardRarity.Uncommon => new Color32(80, 200, 120, 255),
            RewardRarity.Rare => new Color32(80, 120, 255, 255),
            RewardRarity.Epic => new Color32(180, 0, 200, 255),
            RewardRarity.Legendary => new Color32(255, 140, 0, 255),
            RewardRarity.Artifact => new Color32(255, 105, 180, 255),
            RewardRarity.Cursed => new Color32(80, 0, 120, 255),
            _ => Color.white
        };
    }

    public virtual bool IsEligible(IRunContext ctx)
    {
        if (!Scalable)
        {
            if (BlockWhenActive && ctx.IsActive(Id))
                return false;
            if (ctx.IsAvailable(Id) && !CanStack)
                return false;
        }

        if (Scalable && ReplacesReward != null && !ctx.Owns(ReplacesReward.Id))
            return false;

        if (Scalable && Rarity == RewardRarity.Common && ctx.Owns(Id))
            return false;

        if (blockedKeys != null && blockedKeys.Count > 0)
        {
            foreach (var activeKey in ctx.ActiveKeys)
                if (blockedKeys.Contains(activeKey)) return false;
        }

        if (!string.IsNullOrEmpty(ExclusivityKey) && ctx.HasExclusiveKeyActive(ExclusivityKey))
            return false;

        return true;
    }

    public abstract void Apply(IRunContext ctx);

    public virtual void ApplyToPaddle(PaddleElementalState state) { }
}