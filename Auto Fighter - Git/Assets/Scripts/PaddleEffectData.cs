using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class PaddleEffectData
{
    public readonly PaddleState Element;

    // Fire fields (extend later for other elements)
    public readonly int FireBonusDamage;
    public readonly float BurnDamage;
    public readonly float BurnDuration;
    public readonly int BounceDuration;
    public readonly bool CanExplode;
    public readonly float ExplosionSize;
    public readonly int ExplosionDamageFlat;
    public readonly bool IsCursed;

    public PaddleEffectData(
        PaddleState element,
        int fireBonusDamage = 0,
        float burnDamage = 0f,
        float burnDuration = 0f,
        int bounceDuration = 0,
        bool canExplode = false,
        float explosionSize = 0f,
        int explosionDamageFlat = 0,
        bool isCursed = false)
    {
        Element = element;
        FireBonusDamage = fireBonusDamage;
        BurnDamage = burnDamage;
        BurnDuration = burnDuration;
        BounceDuration = bounceDuration;
        CanExplode = canExplode;
        ExplosionSize = explosionSize;
        ExplosionDamageFlat = explosionDamageFlat;
        IsCursed = isCursed;
    }
}
