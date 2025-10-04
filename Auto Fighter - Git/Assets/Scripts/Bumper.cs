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

            Debug.Log($"baba {rb.velocity.normalized}");

            if(normal == Vector3.zero)
            {
                normal = (col.transform.position - transform.position);
                normal.y = 0f;
                normal.Normalize();
            }


                ball = col.collider;

            var ballElem = rb.gameObject.GetComponent<BallElementalState>();
            float tempDamage = ballElem != null ? ballElem.ActiveTempDamage : 0f;
            TakeDamage(pm.Damage + tempDamage, elemDmg: false);

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

        }


    }

    public void TakeDamage(float amount, bool elemDmg)
    {
        curHealth -= amount;

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
        if (elemDmg)
        {
            if(curHealth > 0)
                pm.SpawnXP(transform.position, isDead: false, isTakingElemDamage: true);
        }
        else
        {
            pm.SpawnXP(transform.position, isDead: false, isTakingElemDamage: false);
        }
    }
}
