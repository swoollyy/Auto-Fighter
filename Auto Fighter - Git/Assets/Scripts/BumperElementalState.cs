using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class BumperElementalState : MonoBehaviour
{

    private Bumper bumper;
    public BumperState CurrentState = BumperState.None;

    private float fireBurnExpireAt;
    private float fireBurnNextTickAt;
    private float fireBurnDamagePerTick;
    private float fireBurnTickInterval = .5f;

    private float waterBonusXP;
    private float waterDrenchExpireAt;

    private float earthFissureDamage;
    private float earthBonusXP;
    private float earthBonusScore;
    private float earthCrustExpireAt;

    private float electricShockDamage;
    private float electricBonusXP;
    private float electricBonusScore;

    public float WaterBonusXP => waterBonusXP;

    public float EarthFissureDamage => earthFissureDamage;
    public float EarthBonusXP => earthBonusXP;
    public float EarthBonusScore => earthBonusScore;

    public float ElectricShockDamage => electricShockDamage;
    public float ElectricBonusXP => electricBonusXP;
    public float ElectricBonusScore => electricBonusScore;

    // NEW: accumulation for Earth fissure
    private float earthAccumulatedDamage;
    private Ball lastBallDuringCrust; // last ball that dealt damage while crusted


    void Awake()
    {
        bumper = GetComponent<Bumper>();
    }

    void Start() { }

    void Update()
    {
        switch (CurrentState)
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
        fireBurnExpireAt = Time.time + duration;
        fireBurnNextTickAt = Time.time + fireBurnTickInterval;
        fireBurnDamagePerTick = dps * fireBurnTickInterval;
    }

    private void HandleBurning()
    {
        if (Time.time >= fireBurnNextTickAt)
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
        if (CurrentState == BumperState.Burning)
            CurrentState = BumperState.None;
    }

    public void ApplyDrenched(float duration, float bonusXP)
    {
        CurrentState = BumperState.Drenched;
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
        if (CurrentState == BumperState.Drenched)
            CurrentState = BumperState.None;
    }

    public void ApplyCrusted(float damage, float duration, float bonusXP, float bonusScore, float bonusUnused, float bonusUnused2)
    {
        // NOTE: parameters kept for compatibility (reward code passes more args).
        CurrentState = BumperState.Crusted;
        float newExpire = Time.time + duration;
        earthFissureDamage = damage; // legacy baseline, not directly used for eruption now
        earthBonusXP = bonusXP;
        earthBonusScore = bonusScore;
        if (newExpire > earthCrustExpireAt)
            earthCrustExpireAt = newExpire;

        // NEW reset accumulation
        earthAccumulatedDamage = 0f;
        lastBallDuringCrust = null;
    }

    // Overload used by reward (keeping original signature)
    public void ApplyCrusted(float damage, float duration, float bonusXP, float bonusScore)
    {
        ApplyCrusted(damage, duration, bonusXP, bonusScore, 0f, 0f);
    }

    public void HandleCrusted()
    {
        if (Time.time >= earthCrustExpireAt)
        {
            // NEW: eruption damage = 75% of accumulated damage during crust.
            // Minimum: 75% of the current damage of a relevant ball.
            float eruptionFromAccum = 0.75f * earthAccumulatedDamage;

            // Pick ball reference: last one that hit while crusted, else primary pinball ball.
            Ball anchorBall = lastBallDuringCrust;
            if (anchorBall == null)
                anchorBall = Pinball.Instance != null ? Pinball.Instance.ball : null;

            float minDamage = 0f;
            if (anchorBall != null)
                minDamage = 0.75f * anchorBall.CurrentDamage;

            float finalDamage = Mathf.Max(eruptionFromAccum, minDamage);

            bumper.TakeFissureDamage(finalDamage, bumper.LastDmgFactorForXP);

            // Clear / reset state
            ClearCrusted();
        }
    }

    public void ClearCrusted()
    {
        earthFissureDamage = 0f;
        earthCrustExpireAt = 0f;
        earthBonusXP = 0f;
        earthBonusScore = 0f;
        earthAccumulatedDamage = 0f;
        lastBallDuringCrust = null;
        if (CurrentState == BumperState.Crusted)
            CurrentState = BumperState.None;
    }

    public void ApplyShocked(float damage, float bonusXP, float bonusScore)
    {
        CurrentState = BumperState.Shocked;
        electricShockDamage = damage;
        electricBonusXP = bonusXP;
        electricBonusScore = bonusScore;
    }

    public void HandleShocked()
    {
        bumper.TakeShockDamage(electricShockDamage, bumper.LastDmgFactorForXP, true);
        ClearShocked();
    }

    public void ClearShocked()
    {
        electricShockDamage = 0f;
        electricBonusXP = 0f;
        electricBonusScore = 0f;
        if (CurrentState == BumperState.Shocked)
            CurrentState = BumperState.None;
    }

    public void ClearElement()
    {
        ClearBurn();
        ClearDrenched();
        ClearCrusted();
        ClearShocked();
    }

    // NEW: record incoming damage while crusted
    public void RecordCrustedIncomingDamage(float amount, Ball sourceBall)
    {
        if (CurrentState != BumperState.Crusted) return;
        if (amount > 0f)
            earthAccumulatedDamage += amount;
        if (sourceBall != null)
            lastBallDuringCrust = sourceBall;
    }

}