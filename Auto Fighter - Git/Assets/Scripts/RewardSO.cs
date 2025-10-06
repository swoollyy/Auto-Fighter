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


    public virtual bool IsEligible(IRunContext ctx)
    {
        //block if already active and this reward says to block
        if(!Scalable)
        {
            if (BlockWhenActive && ctx.IsActive(Id))
                return false;

            //block if its available but cant stack
            if (ctx.IsAvailable(Id) && !CanStack)
                return false;
        }

        if (Scalable && ReplacesReward != null && !ctx.Owns(ReplacesReward.Id))
            return false;

        if(Scalable && Rarity == RewardRarity.Common && ctx.Owns(Id))
            return false;


        //if rewards can be blocked, 
        if (blockedKeys != null && blockedKeys.Count > 0)
        {
            foreach(var activeKey in ctx.ActiveKeys)
            {
                if(blockedKeys.Contains(activeKey)) return false;
            }
        }

        //block if exclusivity key clashes
        if(!string.IsNullOrEmpty(ExclusivityKey) && ctx.HasExclusiveKeyActive(ExclusivityKey))
            return false;

        return true;
    }

    public abstract void Apply(IRunContext ctx);

    public virtual void ApplyToPaddle(PaddleElementalState state) { }



}
