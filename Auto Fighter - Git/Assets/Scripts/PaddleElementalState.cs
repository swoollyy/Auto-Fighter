using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class PaddleElementalState : MonoBehaviour
{
    [SerializeField]
    private PaddleState initialState = PaddleState.None;

    public PaddleState CurrentState = PaddleState.None;



    public int FireBonusDamage { get; private set; }
    public int FireBounceDuration { get; private set; }
    public float FireBurnDamage { get; private set; }
    public float FireBurnDuration { get; private set; }
    public bool FireCanExplode { get; private set; }
    public float FireExplosionSize { get; private set; }
    public int FireExplosionDamageFlat { get; private set; }
    public bool FireIsCursed { get; private set; }

    public float WaterBonusXP { get; private set; }
    public int WaterBonusDamage { get; private set; }
    public float WaterDrenchDuration { get; private set; }
    public int WaterBounceDuration { get; private set; }
    public bool WaterCanBurst { get; private set; }
    public float WaterBurstSize { get; private set; }
    public int WaterBurstDamageFlat { get; private set; }
    public bool WaterIsCursed { get; private set; }

    public int EarthFissureDamage { get; private set; }
    public float EarthCrustDuration { get; private set; }
    public float EarthXPBonus { get; private set; }
    public float EarthScoreBonus { get; private set; }
    public int EarthBounceDuration { get; private set; }
    public bool EarthIsCursed { get; private set; }

    public int ElectricShockDamage { get; private set; }
    public int ElectricChainCount { get; private set; }
    public float ElectricXPBonus { get; private set; }
    public float ElectricScoreBonus { get; private set; }
    public int ElectricBounceDuration { get; private set; }
    public bool ElectricIsCursed { get; private set; }


    void Start()
    {
        CurrentState = initialState;
    }

    public void SetPaddleState(PaddleState newState)
    {
        if (newState == PaddleState.None || CurrentState == newState)
            return;
        switch (newState)
            {
                case PaddleState.Fire:
                    CurrentState = PaddleState.Fire;
                    break;
                case PaddleState.Water:
                    CurrentState = PaddleState.Water;
                    break;
                case PaddleState.Earth:
                    CurrentState = PaddleState.Earth;
                    break;
            case PaddleState.Electric:
                    CurrentState = PaddleState.Electric;
                    break;
                default:
                    CurrentState = PaddleState.None;
                    break;
        }
    }

    public void StoreFireData(int bonusDamage, float burnDamage, float burnDuration, int bounceDuration, bool canExplode, float explosionSize, int explosionDamageFlat, bool cursed)
    {
        FireBonusDamage = bonusDamage;
        FireBurnDamage = burnDamage;
        FireBurnDuration = burnDuration;
        FireBounceDuration = bounceDuration;
        FireCanExplode = canExplode;
        FireExplosionSize = explosionSize;
        FireExplosionDamageFlat = explosionDamageFlat;
        FireIsCursed = cursed;
    }

    public void StoreWaterData(float bonusXP, int bonusDamage, float drenchDuration, int bounceDuration, bool canBurst, float burstSize, int burstDamageFlat, bool cursed)
    {
        WaterBonusXP = bonusXP;
        WaterBonusDamage = bonusDamage;
        WaterDrenchDuration = drenchDuration;
        WaterBounceDuration = bounceDuration;
        WaterCanBurst = canBurst;
        WaterBurstSize = burstSize;
        WaterBurstDamageFlat = burstDamageFlat;
        WaterIsCursed = cursed;
        WaterBounceDuration = bounceDuration;
        WaterIsCursed = cursed;
    }

    public void StoreEarthData(int bonusDamage, float crustDuration, float xpBonus, float scoreBonus, int bounceDuration, bool cursed)
    {
        EarthFissureDamage = bonusDamage;
        EarthCrustDuration = crustDuration;
        EarthXPBonus = xpBonus;
        EarthScoreBonus = scoreBonus;
        EarthBounceDuration = bounceDuration;
        EarthIsCursed = cursed;
    }

    public void StoreElectricData(int shockDamage, int chainCount, float xpBonus, float scoreBonus, int bounceDuration, bool cursed)
    {
        ElectricShockDamage = shockDamage;
        ElectricChainCount = chainCount;
        ElectricXPBonus = xpBonus;
        ElectricScoreBonus = scoreBonus;
        ElectricBounceDuration = bounceDuration;
        ElectricIsCursed = cursed;
    }
    public void ApplyFire(int bonusDamageFlat, float burnDamage, float burnDur, int bounceDur, bool canExplode, float explosionSize, int explosionDamageFlat, bool cursed)
    {
        SetPaddleState(PaddleState.Fire);
        StoreFireData(bonusDamageFlat, burnDamage, burnDur, bounceDur, canExplode, explosionSize, explosionDamageFlat, cursed);
        // TODO: spawn paddle fire VFX/SFX here
    }

    public void ApplyWater(float bonusXP, int bonusDamage, float drenchDuration, int bounceDuration, bool canBurst, float burstSize, int burstDamageFlat, bool cursed)
    {
        SetPaddleState(PaddleState.Water);
        StoreWaterData(bonusXP, bonusDamage, drenchDuration, bounceDuration, canBurst, burstSize, burstDamageFlat, cursed);
    }

    public void ApplyEarth(int fissureDamage, float crustDuration, float xpBonus, float scoreBonus, int bounceDuration, bool cursed)
    {
        SetPaddleState(PaddleState.Earth);
        StoreEarthData(fissureDamage, crustDuration, xpBonus, scoreBonus, bounceDuration, cursed);
    }

    public void ApplyElectric(int shockDamage, int chainCount, float xpBonus, float scoreBonus, int bounceDuration, bool cursed)
    {
        SetPaddleState(PaddleState.Electric);
        StoreElectricData(shockDamage, chainCount, xpBonus, scoreBonus, bounceDuration, cursed);
    }

    public PaddleEffectData GetEffectData()
    {
        return new PaddleEffectData(
            CurrentState,
            FireBonusDamage,
            FireBurnDamage,
            FireBurnDuration,
            FireBounceDuration,
            FireCanExplode,
            FireExplosionSize,
            FireExplosionDamageFlat,
            FireIsCursed,

            WaterBonusXP,
            WaterBonusDamage,
            WaterDrenchDuration,
            WaterBounceDuration,
            WaterCanBurst,
            WaterBurstSize,
            WaterBurstDamageFlat,
            WaterIsCursed,

            EarthFissureDamage,
            EarthCrustDuration,
            EarthXPBonus,
            EarthScoreBonus,
            EarthBounceDuration,
            EarthIsCursed,
            
            ElectricShockDamage,
            ElectricChainCount,
            ElectricXPBonus,
            ElectricScoreBonus,
            ElectricBounceDuration,
            ElectricIsCursed);
    }

}
