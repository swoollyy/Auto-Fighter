using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class BumperElementalState : MonoBehaviour
{

    private Bumper bumper;
    public BumperState CurrentState  = BumperState.None;

    private float fireBurnExpireAt;
    private float fireBurnNextTickAt;
    private float fireBurnDamagePerTick;
    private float fireBurnTickInterval = .5f;

    private float waterBonusXP;
    private float waterDrenchExpireAt;

    private float earthBonusXP;
    private float earthBonusScore;
    private float earthCrustExpireAt;

    private float electricBonusXP;
    private float electricBonusScore;

    public float WaterBonusXP => waterBonusXP;
    public float EarthBonusXP => earthBonusXP;
    public float EarthBonusScore => earthBonusScore;

    public float ElectricBonusXP => electricBonusXP;
    public float ElectricBonusScore => electricBonusScore;



    void Awake()
    {
        bumper = GetComponent<Bumper>();
    }


    // Start is called before the first frame update
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {

        switch(CurrentState)
{
            case BumperState.None:
                break;
            case BumperState.Burning:
                HandleBurning();
                break;
            case BumperState.Drenched:
                HandleDrenched();
                break;
            case BumperState.Crusted:
                HandleCrusted();
                break;
            case BumperState.Shocked:
                HandleShocked();
                break;
            default:
                break;
        }

    }

    public void ApplyBurn(float dps, float duration)
    {
        CurrentState = BumperState.Burning;
        Debug.Log("Bumper Burn Applied");
        fireBurnExpireAt = Time.time + duration;
        fireBurnNextTickAt = Time.time + fireBurnTickInterval;
        fireBurnDamagePerTick = dps * fireBurnTickInterval;
    }

    private void HandleBurning()
    {
        if (Time.time >= fireBurnExpireAt)
        {
            fireBurnNextTickAt += fireBurnTickInterval;
            bumper.TakeDamage(fireBurnDamagePerTick, elemDmg: true);
        }

        if (Time.time >= fireBurnExpireAt)
        {
            ClearBurn();
        }
    }

    public void ClearBurn()
    {
        fireBurnExpireAt = 0f;
        fireBurnNextTickAt = 0f;
        fireBurnDamagePerTick = 0f;
        CurrentState = BumperState.None;
    }

    public void ApplyDrenched(float duration, float bonusXP)
    {
        CurrentState = BumperState.Drenched;
        Debug.Log("Bumper Drenched Applied");
        waterDrenchExpireAt = Time.time + duration;
        waterBonusXP = bonusXP;
    }

    private void HandleDrenched()
    {
        if (Time.time >= waterDrenchExpireAt)
        {
            ClearDrenched();
        }
    }

    public void ClearDrenched()
    {
        waterDrenchExpireAt = 0f;
        waterBonusXP = 0f;
        CurrentState = BumperState.None;
    }

    public void ApplyCrusted(float duration, float bonusXP, float bonusScore)
    {
        CurrentState = BumperState.Crusted;
        Debug.Log("Bumper Crusted Applied");
        float newExpire = Time.time + duration;
        earthBonusXP = bonusXP;
        earthBonusScore = bonusScore;
        if (newExpire > earthCrustExpireAt)
            earthCrustExpireAt = newExpire;
    }

    public void HandleCrusted()
    {
        if (Time.time >= earthCrustExpireAt)
        {
            bumper.TakeFissureDamage();
            ClearCrusted();
        }
    }

    public void ClearCrusted()
    {
        earthCrustExpireAt = 0f;
        earthBonusXP = 0f;
        earthBonusScore = 0f;
        CurrentState = BumperState.None;
    }

    public void ApplyShocked(float bonusXP, float bonusScore)
    {
        CurrentState = BumperState.Shocked;
        electricBonusXP = bonusXP;
        electricBonusScore = bonusScore;
    }

    public void HandleShocked()
    {
        bumper.TakeShockDamage(true);
        ClearShocked();
    }

    public void ClearShocked()
    {
        electricBonusXP = 0f;
        electricBonusScore = 0f;
        CurrentState = BumperState.None;
    }

    public void ClearElement()
    {
        ClearBurn();
        ClearDrenched();
        ClearCrusted();
        ClearShocked();
    }

}
