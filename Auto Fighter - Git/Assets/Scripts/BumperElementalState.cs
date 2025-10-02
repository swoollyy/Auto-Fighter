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
        if (CurrentState == BumperState.Burning)
        {
            burnTimer += Time.deltaTime;
            burnTickTimer += Time.deltaTime;
            if (burnTickTimer >= burnTickInterval)
            {
                burnTickTimer = 0f;
                bumper.TakeDamage(burnDamagePerTick);
            }
            if (burnTimer >= burnDuration)
            {
                ClearBurn();
            }
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

    public void ClearBurn()
    {
        burnDuration = 0f;
        burnTimer = 0f;
        burnTickTimer = 0f;
        burnDamagePerTick = 0f;
        CurrentState = BumperState.None;
    }
}
