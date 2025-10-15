using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class PaddleEffectData
{
    public readonly PaddleState Element;

    // Fire fields (extend later for other elements)
    public readonly int FireBonusDamage;
    public readonly float FireBurnDamage;
    public readonly float FireBurnDuration;
    public readonly int FireBounceDuration;
    public readonly bool FireCanExplode;
    public readonly float FireExplosionSize;
    public readonly int FireExplosionDamageFlat;
    public readonly bool FireIsCursed;

    // Water fields (extend later for other elements)
    public readonly float WaterBonusXP;
    public readonly int WaterDamageFlat;
    public readonly float WaterDrenchDuration;
    public readonly int WaterBounceDuration;
    public readonly bool WaterCanBurst;
    public readonly float WaterBurstSize;
    public readonly int WaterBurstDamageFlat;
    public readonly bool WaterIsCursed;

    public readonly int EarthBonusDamage;
    public readonly float EarthFissureDuration;
    public readonly float EarthXPBonus;
    public readonly float EarthScoreBonus;
    public readonly int EarthBounceDuration;
    public readonly bool EarthIsCursed;

    public readonly int ElectricShockDamage;
    public readonly int ElectricChainCount;
    public readonly float ElectricXPBonus;
    public readonly float ElectricScoreBonus;
    public readonly int ElectricBounceDuration;
    public readonly bool ElectricIsCursed;

    public PaddleEffectData(
        PaddleState element,
        int fireBonusDamage = 0,
        float fireBurnDamage = 0f,
        float fireBurnDuration = 0f,
        int fireBounceDuration = 0,
        bool fireCanExplode = false,
        float fireExplosionSize = 0f,
        int fireExplosionDamageFlat = 0,
        bool fireIsCursed = false,

        float waterBonusXP = 0,
        int waterDamageFlat = 0,
        float waterDrenchDuration = 0f,
        int waterBounceDuration = 0,
        bool waterCanBurst = false,
        float waterBurstSize = 0f,
        int waterBurstDamageFlat = 0,
        bool waterIsCursed = false,

                int earthBonusDamage = 0,
        float earthFissureDuration = 0f,
        float earthXPBonus = 0f,
        float earthScoreBonus = 0f,
        int earthBounceDuration = 0,
        bool earthIsCursed = false,
        
        int electricShockDamage = 0,
        int electricChainCount = 0,
        float electricXPBonus = 0f,
        float electricScoreBonus = 0f,
        int electricBounceDuration = 0,
        bool electricIsCursed = false
        )
    {
        Element = element;
        FireBonusDamage = fireBonusDamage;
        FireBurnDamage = fireBurnDamage;
        FireBurnDuration = fireBurnDuration;
        FireBounceDuration = fireBounceDuration;
        FireCanExplode = fireCanExplode;
        FireExplosionSize = fireExplosionSize;
        FireExplosionDamageFlat = fireExplosionDamageFlat;
        FireIsCursed = fireIsCursed;

        WaterBonusXP = waterBonusXP;
        WaterDamageFlat = waterDamageFlat;
        WaterDrenchDuration = waterDrenchDuration;
        WaterBounceDuration = waterBounceDuration;
        WaterCanBurst = waterCanBurst;
        WaterBurstSize = waterBurstSize;
        WaterBurstDamageFlat = waterBurstDamageFlat;
        WaterIsCursed = waterIsCursed;

        EarthBonusDamage = earthBonusDamage;
        EarthFissureDuration = earthFissureDuration;
        EarthXPBonus = earthXPBonus;
        EarthScoreBonus = earthScoreBonus;
        EarthBounceDuration = earthBounceDuration;
        EarthIsCursed = earthIsCursed;

        ElectricShockDamage = electricShockDamage;
        ElectricChainCount = electricChainCount;
        ElectricXPBonus = electricXPBonus;
        ElectricScoreBonus = electricScoreBonus;
        ElectricBounceDuration = electricBounceDuration;
        ElectricIsCursed = electricIsCursed;
    }
}
