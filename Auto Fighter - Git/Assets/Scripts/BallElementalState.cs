using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class BallElementalState : MonoBehaviour
{
    [SerializeField]
    private ElementalState initialState = ElementalState.None;

    public ElementalState CurrentState = ElementalState.None;
    private Ball ball;
    private float originalMaxSpeed;
    // Combination dictionary for easy expansion
    private static readonly Dictionary<(ElementalState, ElementalState), ElementalState> combinations =
        new()
        {
            {(ElementalState.Fire, ElementalState.Water), ElementalState.Steam},
            {(ElementalState.Water, ElementalState.Fire), ElementalState.Steam},
            {(ElementalState.Fire, ElementalState.Earth), ElementalState.Magma},
            {(ElementalState.Earth, ElementalState.Fire), ElementalState.Magma},
            {(ElementalState.Fire, ElementalState.Air), ElementalState.Wildfire},
            {(ElementalState.Air, ElementalState.Fire), ElementalState.Wildfire},
            {(ElementalState.Water, ElementalState.Earth), ElementalState.Sludge},
            {(ElementalState.Earth, ElementalState.Water), ElementalState.Sludge},
            {(ElementalState.Water, ElementalState.Air), ElementalState.Vapor},
            {(ElementalState.Air, ElementalState.Water), ElementalState.Vapor},
            {(ElementalState.Air, ElementalState.Earth), ElementalState.Whirlwind},
            {(ElementalState.Earth, ElementalState.Air), ElementalState.Whirlwind},
            // Add more combinations as needed
        };

    private float fireTempDamage;
    private float fireBurnDamage;
    private float fireBurnDuration;
    private bool fireExplode;
    private float fireExplosionSize;
    private int fireExplosionDamage;
    private bool fireEffectActive;
    private bool fireIsCursed;

    private float waterBonusXP;
    private int waterBonusDamage;
    private float waterDrenchDuration;
    private bool waterExplode;
    private float waterBurstSize;
    private int waterExplosionDamage;
    private bool waterEffectActive;
    private bool waterIsCursed;

    private bool areEffectsActive => fireEffectActive || waterEffectActive;

    private int fireBouncesRemaining;
    private int waterBouncesRemaining;

    // Public getters if needed
    public float FireActiveTempDamage => fireEffectActive ? fireTempDamage : 0f;
    public float FireBurnDamage => fireBurnDamage;
    public float FireBurnDuration => fireBurnDuration;
    public bool FireExplode => fireExplode;
    public float FireExplosionSize => fireExplosionSize;
    public int FireExplosionDamage => fireExplosionDamage;
    public int FireBouncesRemaining => fireBouncesRemaining;
    public bool FireEffectActive => fireEffectActive;
    public bool FireIsCursed => fireIsCursed;

    public float WaterBonusXP => waterEffectActive ? waterBonusXP : 0f;
    public int WaterBonusDamage => waterEffectActive ? waterBonusDamage : 0;
    public float WaterDrenchDuration => waterDrenchDuration;
    public bool WaterExplode => waterExplode;
    public float WaterBurstSize => waterBurstSize;
    public int WaterExplosionDamage => waterExplosionDamage;
    public int WaterBouncesRemaining => waterBouncesRemaining;
    public bool WaterEffectActive => waterEffectActive;
    public bool WaterIsCursed => waterIsCursed;


    private void Awake()
    {
        ball = GetComponent<Ball>();
        if(ball == null)
        {
            Debug.LogWarning("BallElementalState requires a Ball component on the same GameObject.");
        }
    }

    // Start is called before the first frame update
    void Start()
    {
        CurrentState = initialState;
        if(ball != null)
            originalMaxSpeed = ball.maxSpeed;

    }

    public void SetState(ElementalState newState)
    {
        if (CurrentState == newState) return;
        CurrentState = newState;
        ApplyStateEffects();
        //TODO VFX/SFX
    }

    public void CombineWith(ElementalState newElement)
    {
        var combined = CombineElements(CurrentState, newElement);
        SetState(combined);
    }

    public ElementalState CombineElements(ElementalState existing, ElementalState incoming)
    {
        if(combinations.TryGetValue((existing, incoming), out var result))
        {
            return result;
        }
        return incoming; // Default to the incoming element if no combination exists)
    }

    private void ApplyStateEffects()
    {
        if(ball == null) return;
        switch (CurrentState)
        {
            case ElementalState.Fire:
                break;
            case ElementalState.Water:
                break;
            case ElementalState.Earth:
                break;
            case ElementalState.Air:
                break;
            // Add more cases for other elemental states as needed
            default:
                ball.maxSpeed = originalMaxSpeed;
                break;
        }
    }


    public void ClearState()
    {
        CurrentState = ElementalState.None;
        //TODO Remove VFX/SFX
    }

    public void OnBounce(Bumper bumper)
    {
        if (!areEffectsActive) return;



        if (fireEffectActive && bumper != null)
        {
            var elem = bumper.gameObject.GetComponent<BumperElementalState>();
            elem.ClearElement();
            elem.ApplyBurn(fireBurnDamage, fireBurnDuration);
        }
        if (waterEffectActive && bumper != null)
        {
            var elem = bumper.gameObject.GetComponent<BumperElementalState>();
            elem.ClearElement();
            elem.ApplyDrenched(waterDrenchDuration, waterBonusXP);
        }

        switch (CurrentState)
        {
            case ElementalState.Fire:
                fireBouncesRemaining--;
                if (fireBouncesRemaining <= 0)
                {
                    fireEffectActive = false;
                    ClearState();
                }
                break;
            case ElementalState.Water:
                waterBouncesRemaining--;
                if (waterBouncesRemaining <= 0)
                {
                    waterEffectActive = false;
                    ClearState();
                }
                break;
            // Handle other elemental states with bounce effects as needed
            default:
                break;
        }




    }

    #region Elemental State Methods

    public void SetFireState(int bonusDamage, float burnDamage, float burnDuration, int bounceDuration, bool canExplode, float explosionRadius, int explosionDamageFlat, bool cursed)
    {
        fireEffectActive = true;
        waterEffectActive = false;
        Debug.Log("Fire effect applied to ball");

        fireTempDamage = bonusDamage;
        fireBurnDamage = burnDamage;
        fireBurnDuration = burnDuration;
        fireBouncesRemaining += bounceDuration;
        if(fireBouncesRemaining > bounceDuration)
            fireBouncesRemaining = bounceDuration;
        fireExplode = canExplode;
        fireExplosionSize = explosionRadius;
        fireExplosionDamage = explosionDamageFlat;
        fireIsCursed = cursed;

        SetState(ElementalState.Fire);

    }

    public void SetWaterState(float bonusXP, int bonusDamage, float drenchDuration, int bounceDuration, bool canBurst, float burstRadius, int burstDamageFlat, bool cursed)
    {
        waterEffectActive = true;
        fireEffectActive = false;
        Debug.Log("Water effect applied to ball");


        waterBonusXP = bonusXP;
        waterBonusDamage = bonusDamage;
        waterDrenchDuration = drenchDuration;
        waterBouncesRemaining += bounceDuration;
        if (waterBouncesRemaining > bounceDuration)
            waterBouncesRemaining = bounceDuration;
        waterExplode = canBurst;
        waterBurstSize = burstRadius;
        waterExplosionDamage = burstDamageFlat;

        SetState(ElementalState.Water);

    }




    #endregion
}
