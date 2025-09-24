using System.Collections;
using System.Collections.Generic;
using UnityEngine;

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

    Pinball pm;
    Collider ball;

    void Awake()
    {
        pm = GameObject.FindWithTag("PinballManager").GetComponent<Pinball>();
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

            pm.DamageBumper(this);

            rb.velocity = Vector3.zero;
            if (this.CompareTag("Bumper"))
            {
                if (curHealth <= 0)
                {
                    pm.SpawnXP(this.transform.position, true);
                    pm.destroyedBumper = true;
                }
                else
                {
                    pm.SpawnXP(this.transform.position, false);
                    pm.destroyedBumper = false;
                }
                rb.gameObject.GetComponent<Ball>().Bump(normal, 150f, 0);
            }
            else if (this.CompareTag("SmallBumper"))
            {
                if (curHealth <= 0)
                {
                    pm.SpawnXP(this.transform.position, true);
                    pm.destroyedBumper = true;
                }
                else
                {
                    pm.SpawnXP(this.transform.position, false);
                    pm.destroyedBumper = false;
                }
                rb.gameObject.GetComponent<Ball>().Bump(normal, 50f, 1);
            }

            Debug.DrawRay(contact.point, normal * 2f, Color.red);
            rb.gameObject.GetComponent<Ball>().hasBounced = true;

        }


    }


}
