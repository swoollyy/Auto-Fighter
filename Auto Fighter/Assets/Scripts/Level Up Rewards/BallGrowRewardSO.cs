using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(menuName = "Rewards/Ball Grow")]
public class BallGrowRewardSO : RewardSO
{
    [SerializeField] private float size = 1f;
    [SerializeField] private float speed = 1f;
    [SerializeField] private float multiplier = 1f;
    [SerializeField] private float bonusHits = 1f;
    [SerializeField] private float bounciness = 1f;
    [SerializeField] private int bouncesForBonusHits = 1;
    [SerializeField] private bool bonus = false;
    [SerializeField] private bool cursed = false;

    public override void Apply(IRunContext ctx)
    {
        ctx.MarkOwned(Id);
        ctx.SetActive(Id, true);
        ctx.SetAvailable(Id, false);
        ctx.SetExclusive(ExclusivityKey, true);

        ctx.ApplyGrowFX(size, speed, bounciness, multiplier, bonusHits, bouncesForBonusHits, bonus, cursed);

    }
}
