using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

public enum BumperType
{
    Small,
    Default,
    Large
}


public class Bumper : MonoBehaviour
{

    bool drawLine;

    Vector3 savedNormal;

    Vector3 normal;

    bool hasStoppedColliding = false;

    private readonly Dictionary<int, float> lastHitTimeByBall = new();
    [SerializeField] private float hitCooldown = 0.02f;


    [SerializeField]
    public float curHealth;
    [SerializeField]
    public float maxHealth;
    [SerializeField]
    public float cooldown;

    public BumperType type;

    private BumperElementalState bumperElemental;

    Pinball pm;
    Collider ball;

    // Track the most recent collision contact so we can spawn numbers there,
    // with a short validity window to cover chained damage (e.g., on-hit effects).
    private Vector3 lastContactPoint;
    private float lastContactTime;
    [SerializeField] private float contactPointTimeout = 0.25f; // seconds



    void Awake()
    {
        pm = GameObject.FindWithTag("PinballManager").GetComponent<Pinball>();
        bumperElemental = GetComponent<BumperElementalState>();
        curHealth = maxHealth;
    }

    // Start is called before the first frame update
    void Start()
    {
        
    }

    void Update()
    {

    }

    void OnCollisionEnter(Collision col)
    {
        var rb = col.rigidbody;
        if(rb != null )
        {
            int id = rb.GetInstanceID();

            //if same ball has bumped too soon, avoid double collision causing multiple points
            if(lastHitTimeByBall.TryGetValue(id, out float last) && Time.time - last < hitCooldown)
                    return;
            lastHitTimeByBall[id] = Time.time;


            ContactPoint contact = col.contacts[0];
            normal = new Vector3(contact.normal.x, 0f, contact.normal.z).normalized;

            lastContactPoint = contact.point;
            lastContactTime = Time.time;

            Debug.Log($"baba {rb.velocity.normalized}");

            if(normal == Vector3.zero)
            {
                normal = (col.transform.position - transform.position);
                normal.y = 0f;
                normal.Normalize();
            }


                ball = col.collider;


            float baseDamage = pm.Damage;
            float elemBonus = pm.extraElemDamage;
            bool didElemHit = elemBonus > 0;

            rb.velocity = Vector3.zero;
            if (this.CompareTag("Bumper"))
            {
                if (curHealth <= 0)
                {
                    pm.destroyedBumper = true;
                }
                else
                {
                    pm.destroyedBumper = false;
                }
                rb.gameObject.GetComponent<Ball>().Bump(normal, 150f, 0, this);
            }
            else if (this.CompareTag("SmallBumper"))
            {
                if (curHealth <= 0)
                {
                    pm.destroyedBumper = true;
                }
                else
                {
                    pm.destroyedBumper = false;
                }
                rb.gameObject.GetComponent<Ball>().Bump(normal, 50f, 1, this);
            }

            Debug.DrawRay(contact.point, normal * 2f, Color.red);
            rb.gameObject.GetComponent<Ball>().hasBounced = true;


            float totalDamage = baseDamage + elemBonus;

            TakeDamage(totalDamage, elemDmg: didElemHit);
        }


    }

    public void TakeDamage(float amount, bool elemDmg)
    {
            curHealth -= amount;

        GetComponent<BumperAnimScript>().BumperHit();
        pm.ScreenShake();

        if (DamageNumbers.IsReady)
        {
            bool hasRecentContact = (Time.time - lastContactTime) <= contactPointTimeout;
            Vector3 basePos = hasRecentContact ? lastContactPoint : transform.position;

            Vector3 offset = basePos + new Vector3(0, 4, 0);

            DamageNumbers.Spawn(amount, offset);
        }


        if (elemDmg)
        {
            if (curHealth > 0)
                pm.SpawnXP(transform.position, isDead: false, isTakingElemDamage: true);
        }
        else
        {
            if (bumperElemental.CurrentState == BumperState.Drenched)
            {
                Debug.Log("Water Bonus XP Spawned");
                pm.SpawnBonusWaterXP(transform.position, bumperElemental.WaterBonusXP);
            }
            else
            {
                pm.SpawnXP(transform.position, isDead: false, isTakingElemDamage: false);
            }
        }

        if (curHealth <= 0)
        {
            curHealth = 0;
            if (pm != null)
            {
                pm.SpawnXP(transform.position, isDead: true, isTakingElemDamage: elemDmg);
                pm.destroyedBumper = true;
            }
            StartCoroutine(pm.RespawnRoutine(this));
            return;

        }
    }

    public void TakeFissureDamage()
    {
        curHealth -= pm.fissureDamage;

        GetComponent<BumperAnimScript>().BumperHit();
        pm.ScreenShake();

        if (DamageNumbers.IsReady)
        {
            bool hasRecentContact = (Time.time - lastContactTime) <= contactPointTimeout;
            Vector3 basePos = hasRecentContact ? lastContactPoint : transform.position;

            Vector3 offset = basePos + new Vector3(0, 4, 0);

            DamageNumbers.Spawn(pm.fissureDamage, offset);
        }

        pm.SpawnBonusEarthXP(transform.position, bumperElemental.EarthBonusXP);

        if (curHealth <= 0)
        {
            curHealth = 0;
            if (pm != null)
            {
                pm.SpawnXP(transform.position, isDead: true, isTakingElemDamage: false);
                pm.destroyedBumper = true;
            }
            StartCoroutine(pm.RespawnRoutine(this));
            return;

        }


    }
}
