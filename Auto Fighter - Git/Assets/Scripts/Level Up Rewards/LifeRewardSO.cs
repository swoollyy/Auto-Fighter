using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(menuName = "Rewards/Life Reward", fileName = "LifeReward")]
public sealed class LifeRewardSO : RewardSO
{
    // Optional manual override; leave 0 to use rarity mapping below
    [SerializeField, Tooltip("Override grant amount. Leave 0 to use rarity mapping.")]
    private int overrideAmount = 0;

    // Map rarity -> lives granted: Rare=1, Epic=2, Legendary=3, Artifact=4
    private int Amount =>
        overrideAmount > 0 ? overrideAmount :
        Rarity switch
        {
            RewardRarity.Rare => 1,
            RewardRarity.Epic => 2,
            RewardRarity.Legendary => 3,
            RewardRarity.Artifact => 4,
            _ => 1
        };

    public override bool IsEligible(IRunContext ctx)
    {
        // keep all global rules (ownership, stacking, exclusivity, etc.)
        if (!base.IsEligible(ctx))
            return false;

        // lives must be known and not already capped
        if (ctx.MaxLives <= 0 || ctx.Lives >= ctx.MaxLives)
            return false;

        // never offer a life reward that would exceed max
        // e.g., at 4/5 only Amount=1 (Rare) is eligible
        if (ctx.Lives + Amount > ctx.MaxLives)
            return false;

        return true;
    }

    public override void Apply(IRunContext ctx)
    {
        // Pinball implements this; value is clamped there as well
        ctx.ApplyGrantedLives(Amount);
    }
}
