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

    // Per-hit XP/score scaling cache
    private float _lastDmgFactorForXP = 1f;

    private Vector3 normal;

    private static readonly List<Bumper> AllBumpers = new();
    public static IEnumerable<Bumper> EnumerateAll() => AllBumpers;

    // Register this bumper in the global list.
    private void OnEnable() => AllBumpers.Add(this);

    // Unregister this bumper from the global list.
    private void OnDisable() => AllBumpers.Remove(this);

    // Cache references and reset health to max.
    private void Awake()
    {
        pinball = Pinball.Instance ?? GameObject.FindWithTag("PinballManager")?.GetComponent<Pinball>();
        bumperElemental = GetComponent<BumperElementalState>();
        curHealth = maxHealth;
    }

    // Handles ball collision: debounces hits, computes impulse direction, forwards score/XP scaling, and applies damage.
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

        // Factor includes flats + multipliers vs baseline (from the ball that hit).
        float dmgFactor = ballComp != null ? ballComp.ScoreXpDamageFactor : 1f;
        _lastDmgFactorForXP = dmgFactor;

        int bumperKind = CompareTag("SmallBumper") ? 1 : 0;
        float deltaV = bumperKind == 0 ? 225f : 100f;

        rb.velocity = Vector3.zero;
        ballComp?.Bump(normal, deltaV, bumperKind, this);

        Debug.DrawRay(contact.point, normal * 2f, Color.red);

        TakeDamage(totalDamage, elemDmg: fireTick, damageFactor: _lastDmgFactorForXP);

        if (pinball != null)
        {
            var dropPos = (Time.time - lastContactTime) <= contactPointTimeout ? lastContactPoint : transform.position;
            PowerupSystem.TrySpawnPickupOnHit(pinball, dropPos, pinball as IRunContext);
        }
    }

    // Finds the nearest other bumper within an optional max distance (used by chain effects).
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

    // Core damage handler: applies damage, spawns feedback, emits XP scaled by last hit factor, and schedules respawn.
    public void TakeDamage(float amount, bool elemDmg, float damageFactor = 1f)
    {
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

        if (pinball != null)
        {
            if (elemDmg)
            {
                if (curHealth > 0)
                    pinball.SpawnXP(transform.position, isDead: false, isTakingElemDamage: true, damageFactor: _lastDmgFactorForXP);
            }
            else
            {
                if (bumperElemental != null && bumperElemental.CurrentState == BumperState.Drenched)
                    pinball.SpawnBonusWaterXP(transform.position, bumperElemental.WaterBonusXP, damageFactor: _lastDmgFactorForXP);
                else
                    pinball.SpawnXP(transform.position, isDead: false, isTakingElemDamage: false, damageFactor: _lastDmgFactorForXP);
            }
        }

        if (curHealth <= 0)
        {
            curHealth = 0;
            if (pinball != null)
            {
                pinball.SpawnXP(transform.position, isDead: true, isTakingElemDamage: elemDmg, damageFactor: _lastDmgFactorForXP);
                pinball.destroyedBumperBonusActive = true; // next score tick gets bonus
            }
            StartCoroutine(pinball.RespawnRoutine(this));
        }
    }

    // Applies Earth fissure tick damage and emits Earth XP, using last stored damage factor.
    public void TakeFissureDamage(float amount)
    {
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

        if (curHealth <= 0)
        {
            curHealth = 0;
            if (pinball != null)
            {
                pinball.SpawnXP(transform.position, isDead: true, isTakingElemDamage: false, damageFactor: _lastDmgFactorForXP);
                pinball.destroyedBumperBonusActive = true;
            }
            StartCoroutine(pinball.RespawnRoutine(this));
        }
    }

    // Applies Electric shock damage (with optional propagation), emits XP using last stored factor.
    public void TakeShockDamage(float amount, bool propogate = false)
    {
        if (propogate)
        {
            var nearest = FindNearestOther();
            if (nearest) nearest.TakeShockDamage(amount, false);
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

        if (curHealth <= 0)
        {
            curHealth = 0;
            if (pinball != null)
            {
                pinball.SpawnXP(transform.position, isDead: true, isTakingElemDamage: false, damageFactor: _lastDmgFactorForXP);
                pinball.destroyedBumperBonusActive = true;
            }
            StartCoroutine(pinball.RespawnRoutine(this));
        }
    }
}