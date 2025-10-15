using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(menuName = "Rewards/Ball Duplicate FX")]
public class BallDuplicateRewardSO : RewardSO
{

    [SerializeField] private int additionalBalls = 1;

    public override void Apply(IRunContext ctx)
    {
        ctx.MarkOwned(Id);
        ctx.SetActive(Id, true);
        ctx.SetAvailable(Id, true);
        ctx.SetExclusive(ExclusivityKey, true);

        ctx.ApplyAdditionalBalls(additionalBalls);

    }
}
