using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(menuName = "Rewards/Ball Duplicate FX")]
public class BallDuplicateRewardSO : RewardSO
{

    [SerializeField] private int additionalBalls = 1;
    [SerializeField] private bool cursed = false;

    public override void Apply(IRunContext ctx)
    {
        ctx.MarkOwned(Id);
        ctx.SetActive(Id, true);
        ctx.SetAvailable(Id, true);
        ctx.SetExclusive(ExclusivityKey, true);

        ctx.ApplyAdditionalBalls(additionalBalls);

    }

    public override bool IsEligible(IRunContext ctx)
    {
        // keep all global rules (ownership, stacking, exclusivity, etc.)
        if (!base.IsEligible(ctx))
            return false;


        if (cursed && ctx.Lives <= 1)
            return false;

        return true;
    }
}
