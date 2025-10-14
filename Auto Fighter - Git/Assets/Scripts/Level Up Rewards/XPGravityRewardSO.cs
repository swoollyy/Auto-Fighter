using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(menuName = "Rewards/XP Gravity")]
public class XPGravityRewardSO : RewardSO
{
    [SerializeField] private float radiusIncrease = 1f;


    public override void Apply(IRunContext ctx)
    {
        ctx.MarkOwned(Id);
        ctx.SetActive(Id, true);
        ctx.SetAvailable(Id, true);
        ctx.SetExclusive(ExclusivityKey, true);

        ctx.ApplyXPForcefield(radiusIncrease);

    }
}
