using System.Collections;
using System.Collections.Generic;
using UnityEngine;



public class Ball : MonoBehaviour
{

    public bool isInZone;

    Rigidbody rb;
    float debugTimer;

    Pinball PM;

    public int bounceCount { private set; get; }
    public int bumpCount { private set; get; }
    public int bumpCountConsecutive { private set; get; }

    private string lastBouncedTag;

    Vector3 prevBumpDirection = Vector3.zero;
    int sameDirHits = 0;
    float lastBumpTime = -999f;

    float sameDirDotThreshold = .98f;
    int sameDirHitLimit = 25;
    float sameDirWindow = .25f;

    bool debugTimerStart;

    bool resetBall = false;


    bool isStuck;

    public bool isTouchingPaddles;

    public bool hasBounced;

    public float maxSpeed = 50f;

    [SerializeField] private ParticleSystemForceField forceField;
    public float forceFieldRadius = 0f;

    Collider col;

    int count;
    int dirBumpCount;
    // Start is called before the first frame update
    void Awake()
    {
        rb = GetComponent<Rigidbody>();
        PM = GameObject.FindWithTag("PinballManager").GetComponent<Pinball>();
    }

    void OnEnable()
    {
        col = GetComponent<Collider>();
        XPCollectorRegistry.I?.Register(col);
    }

    void OnDisable()
    {
        XPCollectorRegistry.I?.Unregister(col);
    }

    void Start()
    {
        count = 0;
        debugTimer = 0;

        forceFieldRadius = forceField.endRange;
    }

    void FixedUpdate()
    {
        rb.velocity = Vector3.ClampMagnitude(rb.velocity, maxSpeed);
    }

    // Update is called once per frame
    void Update()
    {


        //if ball has bounced, check signal
        //if false, set signal to true, start timer, has bounced is false
        //if true, player is stuck, start timer if it isnt started
        //false scenario----
        //debugtimer starts, if timer is > .01f and theres no signal from hasBounced, not stuck, reset debugtimer
        //true scenario----
        //if timer is greater than 4 seconds and signal is true, reset the ball


        if(hasBounced)
        {
            count++;
            debugTimerStart = true;
            hasBounced = false;
        }

        Debug.Log(debugTimer + " " + count);

        if (debugTimerStart)
        {
            debugTimer += Time.deltaTime;
        }
        else
            debugTimer = 0;

        if (debugTimer > 1f && count > 1)
        {
            Debug.Log("wowww");
        }
        else
        {
            debugTimerStart = false;
            count = 0;
        }




        /*if (debugTimer >= 4f)
        {
            resetBall = true;
            Debug.Log($"Reset!");
            signal = false;
            isStuck = false;
        }

        if (resetBall)
        {
            ResetRb();
            resetBall = false;
        }*/

    }

    public void Launch(float power)
    {
        Debug.Log($"{30f * power}");
        rb.velocity = Vector3.zero;
        rb.AddForce(Vector3.forward * (40f * power), ForceMode.Impulse);
    }

    public void Push(float power)
    {
        rb.AddForce((-Vector3.right * (35f * power)) + (Vector3.forward * (5f * power)), ForceMode.Impulse);
    }

    public void Bump(Vector3 dir, float power, int bumper, Bumper bumperInstance)
    {

        bumpCount++;



        Vector3 currentDir = dir.sqrMagnitude > .0001f ? dir.normalized : Vector3.forward;

        if(Time.time - lastBumpTime > sameDirWindow)
        {
            sameDirHits = 0;
        }

        if(prevBumpDirection != Vector3.zero)
        {
            float cos = Vector3.Dot(currentDir, prevBumpDirection.normalized);
            print(cos + "chat");
            if (Mathf.Abs(cos) >= sameDirDotThreshold)
                sameDirHits++;
            else
                sameDirHits = 0;
        }

        lastBumpTime = Time.time;
        prevBumpDirection = currentDir;

        if(sameDirHits > sameDirHitLimit)
        {
            ResetRb();
            Debug.Log("thanks chatgpt");
            return;
        }


        rb.AddForce(dir * power, ForceMode.Impulse);

        if(bumper == 0)
            PM.AddScore(100, bumpCount, bumpCountConsecutive);
        else
            PM.AddScore(50, bumpCount, bumpCountConsecutive);


        GetComponent<BallElementalState>()?.OnBounce(bumperInstance);
    }


    void OnTriggerEnter(Collider col)
    {
        if (PM.CurrentState == PinballState.Play)
        {
            lastBouncedTag = col.gameObject.tag;
            bounceCount++;

                if (lastBouncedTag == "Bumper" || lastBouncedTag == "SmallBumper")
                {
                    bumpCountConsecutive++;
                }
                else bumpCountConsecutive = 0;
        }
    }

    void OnTriggerStay(Collider col)
    {
        if (col.gameObject.tag == "BallThreshold")
            isInZone = true;


    }

    void OnCollisionStay(Collision col)
    {
        if (col.gameObject.tag == "Paddle")
            isTouchingPaddles = true;
    }

    void OnCollisionExit(Collision col)
    {
        if (col.gameObject.tag == "Paddle")
            isTouchingPaddles = false;
    }



    void OnTriggerExit(Collider col)
    {
        if (col.gameObject.tag == "BallThreshold")
            isInZone = false;


    }

    public void ResetRb()
    {
        sameDirHits = 0;
        prevBumpDirection = Vector3.zero;

        float speed = rb.velocity.magnitude;
        if (speed < 1f) speed = 8f;

        Vector3 baseDir = rb.velocity.sqrMagnitude > .01f ? rb.velocity.normalized : Vector3.forward;
        baseDir.y = 0f;

        float bigDeflect = Random.Range(120f, 160f);
        Vector3 newDir = (Quaternion.AngleAxis(bigDeflect, Vector3.up) * baseDir).normalized;

        rb.velocity = newDir * speed;
    }

    public void OnPaddleHit(PaddleEffectData effect)
    {
        var elem = GetComponent<BallElementalState>();
        if (elem == null) return;

        switch (effect.Element)
        {
            case PaddleState.Fire:
                // forward the stored paddle parameters to the ball elemental state
                elem.SetFireState(
                    effect.FireBonusDamage,
                    effect.FireBurnDamage,
                    effect.FireBurnDuration,
                    effect.FireBounceDuration,
                    effect.FireCanExplode,
                    effect.FireExplosionSize,
                    effect.FireExplosionDamageFlat,
                    effect.FireIsCursed
                );
                PM.extraElemDamage = effect.FireBonusDamage;
                break;
            case PaddleState.Water:
                elem.SetWaterState(
                    effect.WaterBonusXP,
                    effect.WaterDamageFlat,
                    effect.WaterDrenchDuration,
                    effect.WaterBounceDuration,
                    effect.WaterCanBurst,
                    effect.WaterBurstSize,
                    effect.WaterBurstDamageFlat,
                    effect.WaterIsCursed
                );
                PM.extraElemDamage = effect.WaterDamageFlat;
                break;
                case PaddleState.Earth:
                    elem.SetEarthState(
                    effect.EarthBonusDamage,
                    effect.EarthFissureDuration,
                    effect.EarthXPBonus,
                    effect.EarthScoreBonus,
                    effect.EarthBounceDuration,
                    effect.EarthIsCursed
                );
                PM.fissureDamage = effect.EarthBonusDamage;
                break;
            // add other paddle-to-ball mappings here (Water, Earth, etc.)
            default:
                break;
        }
    }

    public void UpdateForcefield(float amount)
    {
        forceFieldRadius *= amount;
        forceField.endRange = forceFieldRadius;
    }

}
