using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(menuName = "Rewards/Fire Paddle FX")]
public class FirePaddleRewardSO : RewardSO
{
    [SerializeField] private int bonusDamageFlat = 1;
    [SerializeField] private float burnDamage = 1f;
    [SerializeField] private float burnDuration = 1f;
    [SerializeField] private int explosionDamageFlat = 1;
    [SerializeField] private int bounceDuration = 1;
    [SerializeField] private float explosionSize = 1f;
    [SerializeField] private bool canExplode = false;
    [SerializeField] private bool cursed = false;


    void OnEnable()
    {
        isPaddleReward = true;
    }

    public override void Apply(IRunContext ctx)
    {
        ctx.MarkOwned(Id);
        ctx.SetActive(Id, true);
        ctx.SetAvailable(Id, false);
        ctx.SetExclusive(ExclusivityKey, true);



    }

    public override void ApplyToPaddle(PaddleElementalState paddle)
    {
        Debug.Log("nice!");
        paddle.ApplyFire(bonusDamageFlat, burnDamage, burnDuration, bounceDuration, canExplode, explosionSize, explosionDamageFlat, cursed);

    }

}
