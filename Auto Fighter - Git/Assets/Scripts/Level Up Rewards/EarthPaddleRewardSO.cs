using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(menuName = "Rewards/Earth Paddle FX")]
public class EarthPaddleRewardSO : RewardSO
{

    [SerializeField] private int fissureDamage = 1;
    [SerializeField] private float crustedDuration = 1f;
    [SerializeField] private float fissureHitScoreMultiplier = 1f;
    [SerializeField] private float fissureHitXPMultiplier = 1f;
    [SerializeField] private int bounceDuration = 1;
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
        if(!base.IsEligible(ctx))
            return false;

        if (ctx is Pinball pb && pb.AreBothPaddlesElemental())
            return false;
        return true;
    }

    public override void ApplyToPaddle(PaddleElementalState paddle)
    {
        paddle.ApplyEarth(fissureDamage, crustedDuration, fissureHitScoreMultiplier, fissureHitXPMultiplier, bounceDuration, cursed);
    }
}
