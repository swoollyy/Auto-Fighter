using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class BumperElementalState : MonoBehaviour
{

    private Bumper bumper;
    public BumperState CurrentState  = BumperState.None;

    private float fireBurnTimer;
    private float fireBurnDuration;
    private float fireBurnDamagePerTick;
    private float fireBurnTickInterval = .5f;
    private float fireBurnTickTimer;

    private float waterBonusXP;
    private float waterDrenchDuration;

    public float WaterBonusXP => waterBonusXP;


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
            default:
                break;
        }

    }

    public void ApplyBurn(float dps, float duration)
    {
        CurrentState = BumperState.Burning;
        Debug.Log("Bumper Burn Applied");
        fireBurnDuration = duration;
        fireBurnTimer = 0f;
        fireBurnTickTimer = 0f;
        fireBurnDamagePerTick = dps * fireBurnTickInterval;
    }

    private void HandleBurning()
    {


        fireBurnTimer += Time.deltaTime;
        fireBurnTickTimer += Time.deltaTime;
        if (fireBurnTickTimer >= fireBurnTickInterval)
        {
            fireBurnTickTimer = 0f;
            bumper.TakeDamage(fireBurnDamagePerTick, elemDmg: true);
        }
        if (fireBurnTimer >= fireBurnDuration)
        {
            ClearBurn();
        }
    }

    public void ClearBurn()
    {
        fireBurnDuration = 0f;
        fireBurnTimer = 0f;
        fireBurnTickTimer = 0f;
        fireBurnDamagePerTick = 0f;
        CurrentState = BumperState.None;
    }

    public void ApplyDrenched(float duration, float bonusXP)
    {
        CurrentState = BumperState.Drenched;
        Debug.Log("Bumper Drenched Applied");
        waterDrenchDuration = duration;
        waterBonusXP = bonusXP;
    }

    private void HandleDrenched()
    {
        waterDrenchDuration -= Time.deltaTime;
        if(waterDrenchDuration <= 0f)
        {
            ClearDrenched();
        }
    }
    
    public void ClearDrenched()
    {
        waterDrenchDuration = 0f;
        waterBonusXP = 0f;
        CurrentState = BumperState.None;
    }

    public void ClearElement()
    {
        ClearBurn();
        ClearDrenched();
    }

}
