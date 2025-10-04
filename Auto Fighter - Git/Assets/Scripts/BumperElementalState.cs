using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class BumperElementalState : MonoBehaviour
{

    private Bumper bumper;
    public BumperState CurrentState  = BumperState.None;

    private float burnTimer;
    private float burnDuration;
    private float burnDamagePerTick;
    private float burnTickInterval = .5f;
    private float burnTickTimer;

    private float drenchDuration;


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
        burnDuration = duration;
        burnTimer = 0f;
        burnTickTimer = 0f;
        burnDamagePerTick = dps * burnTickInterval;
    }

    private void HandleBurning()
    {
        burnTimer += Time.deltaTime;
        burnTickTimer += Time.deltaTime;
        if (burnTickTimer >= burnTickInterval)
        {
            burnTickTimer = 0f;
            bumper.TakeDamage(burnDamagePerTick, elemDmg: true);
        }
        if (burnTimer >= burnDuration)
        {
            ClearBurn();
        }
    }

    public void ClearBurn()
    {
        burnDuration = 0f;
        burnTimer = 0f;
        burnTickTimer = 0f;
        burnDamagePerTick = 0f;
        CurrentState = BumperState.None;
    }

    public void ApplyDrenched(float duration)
    {
        CurrentState = BumperState.Drenched;
        drenchDuration = duration;
    }

    private void HandleDrenched()
    {
        drenchDuration -= Time.deltaTime;
        if(drenchDuration <= 0f)
        {
            ClearDrenched();
        }
    }
    
    public void ClearDrenched()
    {
        drenchDuration = 0f;
        CurrentState = BumperState.None;
    }

}
