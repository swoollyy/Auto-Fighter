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

    private float tempDamage;
    private float burnDamage;
    private float burnDuration;
    private bool fireExplode;
    private float fireExplosionSize;
    private int fireExplosionDamage;

    private int bouncesRemaining;
    private bool effectActive;

    // Public getters if needed
    public float ActiveTempDamage => effectActive ? tempDamage : 0f;
    public float BurnDamage => burnDamage;
    public float BurnDuration => burnDuration;
    public bool FireExplode => fireExplode;
    public float FireExplosionSize => fireExplosionSize;
    public int FireExplosionDamage => fireExplosionDamage;
    public int BouncesRemaining => bouncesRemaining;
    public bool EffectActive => effectActive;


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
        ClearStateEffects();
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

    private void ClearStateEffects()
    {
        if(ball == null) return;
        ball.maxSpeed = originalMaxSpeed;
    }

    public void ClearState()
    {
        ClearStateEffects();
        CurrentState = ElementalState.None;
        //TODO Remove VFX/SFX
    }

    public void OnBounce(Bumper bumper)
    {
        if (!effectActive) return;
        bouncesRemaining--;
        if (bouncesRemaining <= 0)
        {
            ClearState();
            effectActive = false;
        }

        if(burnDamage > 0 && bumper != null)
        {
            var elem = bumper.gameObject.GetComponent<BumperElementalState>();
            elem.ApplyBurn(burnDamage, burnDuration);
        }



    }

    #region Elemental State Methods

    public void SetFireState(int bonusDamage, float burnDamage, float burnDuration, int bounceDuration, bool canExplode, float explosionSize, int explosionDamageFlat, bool cursed)
    {
        effectActive = true;

        tempDamage = bonusDamage;
        this.burnDamage = burnDamage;
        this.burnDuration = burnDuration;
        bouncesRemaining += bounceDuration;
        if(bouncesRemaining > bounceDuration)
            bouncesRemaining = bounceDuration;
        fireExplode = canExplode;
        fireExplosionSize = explosionSize;
        fireExplosionDamage = explosionDamageFlat;
        // Optionally handle 'cursed'

        SetState(ElementalState.Fire);

    }




    #endregion
}
