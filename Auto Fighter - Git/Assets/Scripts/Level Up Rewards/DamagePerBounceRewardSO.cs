using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(menuName = "Rewards/DmgPerBounce")]
public class DamagePerBounceRewardSO : RewardSO
{
    [SerializeField] private float damageMult = 10f;
    [SerializeField] private int bouncesNeeded = 1;

    public override void Apply(IRunContext ctx)
    {
        ctx.MarkOwned(Id);
        ctx.SetActive(Id, true);
        ctx.SetAvailable(Id, true);
        ctx.SetExclusive(ExclusivityKey, true);

        ctx.ApplyDmgPerBounceFX(damageMult, bouncesNeeded);

    }
}
