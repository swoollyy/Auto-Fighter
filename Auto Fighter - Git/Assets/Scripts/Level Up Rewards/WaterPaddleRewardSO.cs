using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(menuName = "Rewards/Water Paddle FX")]
public class WaterPaddleRewardSO : RewardSO
{
    [SerializeField] private float bonusXPPerc = 1f;
    [SerializeField] private int bonusDamageFlat = 1;
    [SerializeField] private float drenchDuration = 1f;
    [SerializeField] private int explosionDamageFlat = 1;
    [SerializeField] private int bounceDuration = 1;
    [SerializeField] private float explosionSize = 1f;
    [SerializeField] private bool canExplode = false;
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
        Debug.Log("niceu!");
        paddle.ApplyWater(bonusXPPerc, bonusDamageFlat, drenchDuration, bounceDuration, canExplode, explosionSize, explosionDamageFlat, cursed);

    }

}
