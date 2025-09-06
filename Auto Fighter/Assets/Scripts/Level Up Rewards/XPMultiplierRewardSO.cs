using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(menuName = "Rewards/Score Multiplier")]
public class XPMultiplierRewardSO : RewardSO
{
    [SerializeField] private float multiplier = 1f;
    [SerializeField] private float bonusTime = 30f;
    [SerializeField] private bool cursed = false;

    public override void Apply(IRunContext ctx)
    {
        ctx.MarkOwned(Id);
        ctx.SetActive(Id, true);
        ctx.SetExclusive(ExclusivityKey, true);

        ctx.ApplyXPMultiplier(multiplier, cursed);
        ctx.ApplyBonusTime(bonusTime, cursed);

    }
}
