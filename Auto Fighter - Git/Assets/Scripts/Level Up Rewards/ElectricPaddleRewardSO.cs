using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(menuName = "Rewards/Electric Paddle FX")]
public class ElectricPaddleRewardSO : RewardSO
{
    [SerializeField] private int shockDamage = 1;
    [SerializeField] private int chainCount = 1;
    [SerializeField] private int bounceDuration = 1;
    [SerializeField] private float xpBonus = 0.1f;
    [SerializeField] private float scoreBonus = 0.1f;
    [SerializeField] private bool cursed = false;
    public override void Apply(IRunContext ctx)
    {
        ctx.MarkOwned(Id);
        ctx.SetActive(Id, true);
        ctx.SetAvailable(Id, false);
        ctx.SetExclusive(ExclusivityKey, true);
        isPaddleReward = true;


    }
    public override bool IsEligible(IRunContext ctx)
    {
        if (!base.IsEligible(ctx))
            return false;

        if (ctx is Pinball pb && pb.AreBothPaddlesElemental())
            return false;

        return true;
    }
    public override void ApplyToPaddle(PaddleElementalState paddle)
    {
        Debug.Log("nice!");
        paddle.ApplyElectric(shockDamage, chainCount, xpBonus, scoreBonus, bounceDuration, cursed);

    }
}
