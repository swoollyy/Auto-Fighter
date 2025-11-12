using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public enum BumperType
{
    Small,
    Default,
    Large
}

[DisallowMultipleComponent]
public class Bumper : MonoBehaviour
{
    [SerializeField] public float curHealth;
    [SerializeField] public float maxHealth;
    [SerializeField] public float cooldown;

    public BumperType type;

    private readonly Dictionary<int, float> lastHitTimeByBall = new();
    [SerializeField] private float hitCooldown = 0.02f;

    private Vector3 lastContactPoint;
    private float lastContactTime;
    [SerializeField] private float contactPointTimeout = 0.25f;

    private BumperElementalState bumperElemental;
    private Pinball pinball;

    private float _lastDmgFactorForXP = 1f;
    public float LastDmgFactorForXP => _lastDmgFactorForXP;

    private Vector3 normal;

    [SerializeField] private bool isDead = false;
    public bool IsDead => isDead;

    private static readonly List<Bumper> AllBumpers = new();
    public static IEnumerable<Bumper> EnumerateAll() => AllBumpers;

    private BumperLightController _light;

    private void OnEnable() => AllBumpers.Add(this);
    private void OnDisable() => AllBumpers.Remove(this);

    private void Awake()
    {
        pinball = Pinball.Instance ?? GameObject.FindWithTag("PinballManager")?.GetComponent<Pinball>();
        bumperElemental = GetComponent<BumperElementalState>();
        curHealth = maxHealth;
        isDead = false;

        _light = GetComponent<BumperLightController>();
        if (_light == null) _light = gameObject.AddComponent<BumperLightController>();
    }

    private void OnCollisionEnter(Collision col)
    {
        var rb = col.rigidbody;
        if (rb == null) return;

        var ballComp = rb.GetComponent<Ball>();
        var ballElem = rb.GetComponent<BallElementalState>();

        int id = rb.GetInstanceID();
        if (lastHitTimeByBall.TryGetValue(id, out float last) && Time.time - last < hitCooldown)
            return;
        lastHitTimeByBall[id] = Time.time;

        // Detect if the colliding ball is currently in a ricochet window.
        var assist = rb.GetComponent<RicochetAssist>();
        bool ricochetActive = assist && assist.IsActive;

        // Light behavior:
        // - Normal hit: flash to peak then back to baseline
        // - Ricochet hit: persistent light that increases with each ricochet hit
        if (ricochetActive)
        {
            _light?.StartRicochetMode();
            _light?.IncrementRicochet();
        }
        else
        {
            _light?.PulseFlash();
        }

        var contact = col.contacts[0];
        lastContactPoint = contact.point;
        lastContactTime = Time.time;

        normal = new Vector3(contact.normal.x, 0f, contact.normal.z).normalized;
        if (normal == Vector3.zero)
        {
            normal = (col.transform.position - transform.position);
            normal.y = 0f;
            normal.Normalize();
        }

        float totalDamage = ballComp != null ? ballComp.CurrentDamage : (pinball != null ? pinball.Damage : 0f);
        bool fireTick = ballElem != null && ballElem.CurrentState == ElementalState.Fire;

        float dmgFactor = ballComp != null ? ballComp.ScoreXpDamageFactor : 1f;
        _lastDmgFactorForXP = dmgFactor;

        int bumperKind = CompareTag("SmallBumper") ? 1 : 0;
        float deltaV = bumperKind == 0 ? 150f : 200f;

        int portalBoost = ballComp != null ? ballComp.ConsumePortalBoost() : 1;
        rb.velocity = Vector3.zero;
        ballComp?.Bump(normal, deltaV, bumperKind, this, portalBoost);

        if (isDead) return;

        TakeDamage(totalDamage, elemDmg: fireTick, damageFactor: _lastDmgFactorForXP, sourceBall: ballComp);

        if (pinball != null)
        {
            var dropPos = (Time.time - lastContactTime) <= contactPointTimeout ? lastContactPoint : transform.position;
            PowerupSystem.TrySpawnPickupOnHit(pinball, dropPos, pinball as IRunContext);
        }
    }

    private Bumper FindNearestOther(float maxDistance = Mathf.Infinity)
    {
        Vector3 p = transform.position;
        Bumper nearest = null;
        float bestSqr = maxDistance * maxDistance;

        for (int i = 0; i < AllBumpers.Count; i++)
        {
            var b = AllBumpers[i];
            if (b == null || b == this) continue;
            float d = (b.transform.position - p).sqrMagnitude;
            if (d < bestSqr) { bestSqr = d; nearest = b; }
        }
        return nearest;
    }

    public void TakeDamage(float amount, bool elemDmg, float damageFactor = 1f, int xpScoreMult = 1, Ball sourceBall = null)
    {
        if (isDead) return;

        _lastDmgFactorForXP = Mathf.Max(0f, damageFactor);
        bumperElemental?.RecordCrustedIncomingDamage(amount, sourceBall);

        curHealth -= amount;

        GetComponent<BumperAnimScript>()?.BumperHit();
        pinball?.ScreenShake();

        if (DamageNumbers.IsReady)
        {
            bool hasRecent = (Time.time - lastContactTime) <= contactPointTimeout;
            Vector3 basePos = hasRecent ? lastContactPoint : transform.position;
            Vector3 offset = basePos + new Vector3(0, 4, 0);
            DamageNumbers.Spawn((float)Math.Round(amount, 1, MidpointRounding.AwayFromZero), offset);
        }

        if (pinball != null)
        {
            if (elemDmg)
            {
                if (curHealth > 0)
                    pinball.SpawnXP(transform.position, isDead: false, isTakingElemDamage: true, damageFactor: _lastDmgFactorForXP, mult: xpScoreMult);
            }
            else
            {
                if (bumperElemental != null && bumperElemental.CurrentState == BumperState.Drenched)
                    pinball.SpawnBonusWaterXP(transform.position, bumperElemental.WaterBonusXP, damageFactor: _lastDmgFactorForXP, mult: xpScoreMult);
                else
                    pinball.SpawnXP(transform.position, isDead: false, isTakingElemDamage: false, damageFactor: _lastDmgFactorForXP, mult: xpScoreMult);
            }
        }

        if (curHealth <= 0f)
            Die(elemDmg, xpScoreMult);
    }

    public void TakeFissureDamage(float amount, float damageFactor)
    {
        if (isDead) return;
        _lastDmgFactorForXP = Mathf.Max(0f, damageFactor);
        curHealth -= amount;

        GetComponent<BumperAnimScript>()?.BumperHit();
        pinball?.ScreenShake();

        if (DamageNumbers.IsReady)
        {
            bool hasRecent = (Time.time - lastContactTime) <= contactPointTimeout;
            Vector3 basePos = hasRecent ? lastContactPoint : transform.position;
            Vector3 offset = basePos + new Vector3(0, 4, 0);
            DamageNumbers.Spawn((float)Math.Round(amount, 1, MidpointRounding.AwayFromZero), offset);
        }

        if (pinball != null && bumperElemental != null)
            pinball.SpawnBonusEarthXP(transform.position, bumperElemental.EarthBonusXP, damageFactor: _lastDmgFactorForXP);

        if (curHealth <= 0f)
            Die(false, 1);
    }

    public void TakeShockDamage(float amount, float damageFactor, bool propogate = false)
    {
        if (isDead) return;
        _lastDmgFactorForXP = Mathf.Max(0f, damageFactor);
        bumperElemental?.RecordCrustedIncomingDamage(amount, null);

        if (propogate)
        {
            var nearest = FindNearestOther();
            if (nearest) nearest.TakeShockDamage(amount, _lastDmgFactorForXP, false);
        }

        curHealth -= amount;

        GetComponent<BumperAnimScript>()?.BumperHit();
        pinball?.ScreenShake();

        if (DamageNumbers.IsReady)
        {
            bool hasRecent = (Time.time - lastContactTime) <= contactPointTimeout;
            Vector3 basePos = hasRecent ? lastContactPoint : transform.position;
            Vector3 offset = basePos + new Vector3(1, 4, -1);
            DamageNumbers.Spawn((float)Math.Round(amount, 1, MidpointRounding.AwayFromZero), offset);
        }

        if (pinball != null && bumperElemental != null)
            pinball.SpawnBonusEarthXP(transform.position, bumperElemental.ElectricBonusXP, damageFactor: _lastDmgFactorForXP);

        if (curHealth <= 0f)
            Die(false, 1);
    }

    public void RicochetHit(Ball ball, Vector3 forcedDirection, int portalBoost = 1, bool elemDmgOverride = false)
    {
        if (ball == null || isDead) return;
        lastHitTimeByBall[ball.GetInstanceID()] = Time.time;

        _light?.StartRicochetMode();
        _light?.IncrementRicochet();

        Vector3 dir = forcedDirection;
        if (dir.sqrMagnitude < 0.0001f)
        {
            dir = (transform.position - ball.transform.position);
            dir.y = 0f;
            if (dir.sqrMagnitude < 0.0001f) dir = Vector3.forward;
            dir.Normalize();
        }

        int bumperKind = CompareTag("SmallBumper") ? 1 : 0;
        float deltaV = bumperKind == 0 ? 150f : 200f;
        ball.Bump(dir, deltaV, bumperKind, this, portalBoost);

        float totalDamage = ball.CurrentDamage;
        float dmgFactor = ball.ScoreXpDamageFactor;
        _lastDmgFactorForXP = dmgFactor;

        if (!isDead)
            TakeDamage(totalDamage, elemDmg: elemDmgOverride, damageFactor: dmgFactor, xpScoreMult: portalBoost, sourceBall: ball);
    }

    private void Die(bool elemDmg, int xpScoreMult)
    {
        if (isDead) return;
        isDead = true;
        curHealth = 0f;

        // Turn light off immediately
        _light?.HandleBumperDeath();

        if (pinball != null)
        {
            pinball.SpawnXP(transform.position, isDead: true, isTakingElemDamage: elemDmg, damageFactor: _lastDmgFactorForXP, mult: xpScoreMult);
            pinball.destroyedBumperBonusActive = true;
            StartCoroutine(pinball.RespawnRoutine(this));
        }
    }

    public void Revive()
    {
        curHealth = maxHealth;
        isDead = curHealth <= 0f;
        if (!isDead)
            _light?.HandleBumperRevive();
    }

    public void EndRicochetLight()
    {
        _light?.EndRicochetMode();
    }
}