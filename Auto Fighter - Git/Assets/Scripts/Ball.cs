using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Ball : MonoBehaviour
{
    [Header("Damage")]
    [SerializeField] private float baseDamage = 5f;

    private int flatBonusDamage;
    private float damageMultiplier = 1f;
    private float tempDamageMultiplier = 1f;
    private float tempDamageMultiplierStore = 1f;
    private int bonusBouncesNeeded;
    private int bonusBouncesRemaining;
    private int tmpBonusBouncesNeeded;
    private int tmpBonusBouncesRemaining;

    private const float DAMAGE_BASELINE = 5f; // level-1 baseline: 5 base dmg @ 1x multipliers

    public float BaseDamage
    {
        get => baseDamage;
        set => baseDamage = Mathf.Max(0f, value);
    }

    public float CurrentDamage => Mathf.Max(0f, ((baseDamage + flatBonusDamage) * damageMultiplier) * tempDamageMultiplier);

    // Scale score/XP by actual dealt damage vs. baseline (includes flat and multipliers).
    // e.g., 3 dmg vs 5 baseline => 0.6x score/XP; 6 dmg vs 5 => 1.2x.
    public float ScoreXpDamageFactor => Mathf.Max(0f, DAMAGE_BASELINE > 0f ? (CurrentDamage / DAMAGE_BASELINE) : 1f);

    public void AddDamageMultiplier(float multiplier)
    {
        if (Mathf.Approximately(multiplier, 1f)) return;
        damageMultiplier += multiplier;
    }

    public void AddFlatDamage(int flatDamage, int bounces)
    {
        if (flatDamage == 0) return;
        flatBonusDamage = 0;
        flatBonusDamage += flatDamage;
        bonusBouncesNeeded = bounces;
        bonusBouncesRemaining = bonusBouncesNeeded;
    }

    public void AddTempDamageMultiplier(float multiplier, int bounces)
    {
        tempDamageMultiplierStore = 1f;
        tempDamageMultiplierStore += multiplier;
        tmpBonusBouncesNeeded = bounces;
        tmpBonusBouncesRemaining = tmpBonusBouncesNeeded;
    }

    public void ConsumeBounceForDamageMods()
    {
        bonusBouncesRemaining--;
        tmpBonusBouncesRemaining--;
        if (bonusBouncesRemaining < 0 && tmpBonusBouncesRemaining < 0)
            return;

        if (bonusBouncesRemaining == 0)
        {
            flatBonusDamage = 0;
        }
        if (tmpBonusBouncesRemaining > 0)
        {
            tempDamageMultiplier = 1f;
        }
        else if (tmpBonusBouncesRemaining == 0)
        {
            tempDamageMultiplier = tempDamageMultiplierStore;
            ResetTempBounceMods();
        }
    }

    private void ResetDamageMods()
    {
        flatBonusDamage = 0;
        bonusBouncesRemaining = 0;
    }

    private void ResetTempBounceMods()
    {
        tmpBonusBouncesRemaining = tmpBonusBouncesNeeded;
    }

    private PhysicMaterial runtimePhysMat;

    public void EnsureUniquePhysicMaterial()
    {
        if (col == null) col = GetComponent<Collider>();
        if (col == null) return;
        if (runtimePhysMat != null) return;
        var src = col.material;

        if (src != null)
        {
            runtimePhysMat = Instantiate(src);
            runtimePhysMat.name = src.name + " (Runtime)";
        }
        else
        {
            runtimePhysMat = new PhysicMaterial("RuntimePhysMat");
        }

        col.material = runtimePhysMat;
    }

    public void AdjustBounciness(float factor)
    {
        EnsureUniquePhysicMaterial();
        if (col != null && col.material != null)
        {
            col.material.bounciness *= factor;
        }
    }

    // Launch tube and paddle contact flags (read-only outside)
    public bool IsInLaunchTube { get; private set; }
    public bool IsTouchingPaddles { get; private set; }
    public bool IsActive { get; private set; }

    Rigidbody rb;
    float debugTimer;

    Pinball pinball;

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

    public float maxSpeed = 50f;

    [SerializeField] private ParticleSystemForceField forceField;
    public float forceFieldRadius = 0f;

    Collider col;

    int count;
    int dirBumpCount;

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
        pinball = GameObject.FindWithTag("PinballManager").GetComponent<Pinball>();
    }

    void OnEnable()
    {
        IsActive = true;
        if (Pinball.Instance != null)
            Pinball.Instance.RegisterBall(this);
        col = GetComponent<Collider>();
        XPCollectorRegistry.I?.Register(col);
    }

    void OnDisable()
    {
        IsActive = false;
        if (Pinball.Instance != null)
            Pinball.Instance.UnregisterBall(this);
        XPCollectorRegistry.I?.Unregister(col);
    }

    void Start()
    {
        count = 0;
        debugTimer = 0;

        IsActive = true;

        forceFieldRadius = forceField.endRange;
    }

    void FixedUpdate()
    {
        rb.velocity = Vector3.ClampMagnitude(rb.velocity, maxSpeed);
    }

    void Update()
    {
    }

    public void ApplyPaddleDamageEffect(PaddleEffectData effect)
    {
        int flat = 0;
        int bounces = 0;

        switch (effect.Element)
        {
            case PaddleState.Fire:
                flat = effect.FireBonusDamage;
                bounces = effect.FireBounceDuration;
                break;
            case PaddleState.Water:
                flat = effect.WaterDamageFlat;
                bounces = effect.WaterBounceDuration;
                break;
            case PaddleState.Earth:
                flat = 0;
                bounces = effect.EarthBounceDuration;
                break;
            case PaddleState.Electric:
                flat = 0;
                bounces = effect.ElectricBounceDuration;
                break;
            default:
                break;
        }

        AddFlatDamage(flat, bounces);
    }

    public void Launch(float power)
    {
        Debug.Log($"{30f * power}");
        rb.velocity = Vector3.zero;
        rb.AddForce(Vector3.forward * (45f * power), ForceMode.Impulse);
    }

    public void Push(float power)
    {
        rb.AddForce((-Vector3.right * (40f * power)) + (Vector3.forward * (5.5f * power)), ForceMode.Impulse);
    }

    public void Bump(Vector3 direction, float deltaV, int bumperKind, Bumper bumperInstance)
    {
        bumpCount++;

        Vector3 currentDir = direction.sqrMagnitude > .0001f ? direction.normalized : Vector3.forward;

        if (Time.time - lastBumpTime > sameDirWindow)
        {
            sameDirHits = 0;
        }

        if (prevBumpDirection != Vector3.zero)
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

        if (sameDirHits > sameDirHitLimit)
        {
            ResetRb();
            Debug.Log("thanks chatgpt");
            return;
        }

        rb.AddForce(currentDir * deltaV, ForceMode.Impulse);

        if (bumperKind == 0)
            pinball?.AddScore(100, bumpCount, bumpCountConsecutive, ScoreXpDamageFactor);
        else
            pinball?.AddScore(50, bumpCount, bumpCountConsecutive, ScoreXpDamageFactor);

        GetComponent<BallElementalState>()?.OnBounce(bumperInstance);
        ConsumeBounceForDamageMods();
    }

    void OnTriggerEnter(Collider other)
    {
        if (pinball != null && pinball.CurrentState == PinballState.Play)
        {
            lastBouncedTag = other.gameObject.tag;
            bounceCount++;

            if (lastBouncedTag == "Bumper" || lastBouncedTag == "SmallBumper")
            {
                bumpCountConsecutive++;
            }
            else bumpCountConsecutive = 0;
        }
    }

    void OnTriggerStay(Collider other)
    {
        if (other.gameObject.tag == "BallThreshold")
            IsInLaunchTube = true;
    }

    void OnCollisionStay(Collision collision)
    {
        if (collision.gameObject.tag == "Paddle")
            IsTouchingPaddles = true;
    }

    void OnCollisionExit(Collision collision)
    {
        if (collision.gameObject.tag == "Paddle")
            IsTouchingPaddles = false;
    }

    void OnTriggerExit(Collider other)
    {
        if (other.gameObject.tag == "BallThreshold")
            IsInLaunchTube = false;
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
                break;
            case PaddleState.Electric:
                elem.SetElectricState(
                    effect.ElectricShockDamage,
                    effect.ElectricChainCount,
                    effect.ElectricXPBonus,
                    effect.ElectricScoreBonus,
                    effect.ElectricBounceDuration,
                    effect.ElectricIsCursed
                );
                break;
            default:
                break;
        }

        ApplyPaddleDamageEffect(effect);
    }

    public void UpdateForcefield(float amount)
    {
        forceFieldRadius *= amount;
        forceField.endRange = forceFieldRadius;
    }
}