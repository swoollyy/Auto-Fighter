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

    [Header("Behavior")]
    [Tooltip("If true, this reward should not be offered if an instance is currently active")]
    [SerializeField] private bool blockWhenActive = true;
    [SerializeField] private bool canStack = true;

    [Tooltip("Exclusive group key. Rewards sharing same key cannot co-exist")]
    [SerializeField] private string exclusivityKey;

    public string Id => rewardID;
    public string Name => displayName;
    public string Description => description;
    public RewardCategory Category => category;
    public RewardRarity Rarity => rarity;
    public bool BlockWhenActive => blockWhenActive;
    public bool CanStack => canStack;
    public string ExclusivityKey => exclusivityKey;

    public virtual bool IsEligible(IRunContext ctx)
    {
        //block if already active and this reward says to block
        if (BlockWhenActive && ctx.IsActive(Id))
            return false;

        if(ctx.IsAvailable(Id) && !CanStack)
            return false;

        //block if exclusivity key clashes
        if(!string.IsNullOrEmpty(ExclusivityKey) && ctx.HasExclusiveKeyActive(ExclusivityKey))
            return false;

        return true;
    }

    public abstract void Apply(IRunContext ctx);


}
