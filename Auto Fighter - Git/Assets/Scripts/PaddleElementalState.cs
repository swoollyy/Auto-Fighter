using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class PaddleElementalState : MonoBehaviour
{
    [SerializeField]
    private PaddleState initialState = PaddleState.None;

    public PaddleState CurrentState = PaddleState.None;

    public int FireBonusDamage { get; private set; }
    public float BurnDamage { get; private set; }
    public float BurnDuration { get; private set; }
    public int BounceDuration { get; private set; }
    public bool CanExplode { get; private set; }
    public float ExplosionSize { get; private set; }
    public int ExplosionDamageFlat { get; private set; }
    public bool IsCursed { get; private set; }


    void Start()
    {
        CurrentState = initialState;
    }

    public void SetPaddleState(PaddleState newState)
    {
        if(newState != null)
        switch(newState)
            {
                case PaddleState.Fire:
                    CurrentState = PaddleState.Fire;
                    break;
            }
    }

    public void StoreFireData(int bonusDamage, float burnDamage, float burnDuration, int bounceDuration, bool canExplode, float explosionSize, int explosionDamageFlat, bool cursed)
    {
        FireBonusDamage = bonusDamage;
        BurnDamage = burnDamage;
        BurnDuration = burnDuration;
        BounceDuration = bounceDuration;
        CanExplode = canExplode;
        ExplosionSize = explosionSize;
        ExplosionDamageFlat = explosionDamageFlat;
        IsCursed = cursed;
    }

    public void ApplyFire(int bonusDamageFlat, float burnDamage, float burnDur, int bounceDur, bool canExplode, float explosionSize, int explosionDamageFlat, bool cursed)
    {
        SetPaddleState(PaddleState.Fire);
        StoreFireData(bonusDamageFlat, burnDamage, burnDur, bounceDur, canExplode, explosionSize, explosionDamageFlat, cursed);
        // TODO: spawn paddle fire VFX/SFX here
    }

    public PaddleEffectData GetEffectData()
    {
        return new PaddleEffectData(
            CurrentState,
            FireBonusDamage,
            BurnDamage,
            BurnDuration,
            BounceDuration,
            CanExplode,
            ExplosionSize,
            ExplosionDamageFlat,
            IsCursed);
    }

}
