using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Ball : MonoBehaviour
{
    [Header("Damage")]
    [SerializeField] private float baseDamage = 5f;

    private int flatBonusDamage;
    private float damageMultiplier = 1f;          // permanent aggregated factor (1 = no change)
    private float tempDamageMultiplier = 1f;      // active temporary factor (1 = no change)
    private float tempDamageMultiplierStore = 1f; // queued temp factor to apply
    private int bonusBouncesNeeded;
    private int bonusBouncesRemaining;
    private int tmpBonusBouncesNeeded;
    private int tmpBonusBouncesRemaining;


    // === Glow / Combo UI state ===
    [Header("Glow")]
    [SerializeField] private Color glowColor = Color.red; // UI and material "glow" color
    [SerializeField, Range(0f, 5f)] private float emissionBase = 1.5f; // base emission when no combo
    [SerializeField, Range(0f, 5f)] private float emissionPerComboStep = 0.30f; // extra intensity per combo step

    private Renderer _renderer;
    private Material _runtimeMat; // unique instance so enabling emission doesn't affect shared material
    private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

    // Events so UI can track ball lifecycle and combo changes
    public static event System.Action<Ball> OnBallActivated;
    public static event System.Action<Ball> OnBallDeactivated;
    public event System.Action<Ball> OnComboChanged;

    // Expose for UI
    public Color GlowColor
    {
        get => glowColor;
        set
        {
            glowColor = value;
            ApplyGlow();            // immediately reflect new color
            OnComboChanged?.Invoke(this); // notify UI
        }
    }

    public bool IsComboActive => comboActive;

    // At 3 hits: 1.1x, then +0.1 per hit (already used by scoring)
    public float CurrentComboMultiplierUI => CurrentComboMultiplier;

    // Public intensity for UI (optional visual boosting of the dot)
    public float EmissionIntensityUI => ComputeEmissionIntensity();

    [SerializeField] private int comboThreshold = 3;                 // hits needed to start combo
    [SerializeField, Range(0.05f, 1f)] private float comboBonusPerHit = 0.10f; // +0.1x per bumper while active
    [SerializeField] private LayerMask comboBreakLayers;             // assign your "Wall(s)" layer(s) in Inspector

    private int comboHitStreak;    // consecutive bumper hits
    private bool comboActive;      // true once threshold reached

    public float CurrentComboMultiplier
    {
        get
        {
            if (!comboActive) return 1f;
            // At 3 hits: 1 + 0.1 * (3 - 2) = 1.1; each extra hit adds +0.1
            int effectiveHits = Mathf.Max(0, comboHitStreak - (comboThreshold - 1));
            return 1f + comboBonusPerHit * effectiveHits;
        }
    }

    private const float DAMAGE_BASELINE = 5f; // level-1 baseline: 5 base dmg @ 1x multipliers

    public float BaseDamage
    {
        get => baseDamage;
        set => baseDamage = Mathf.Max(0f, value);
    }

    // Replace CurrentDamage and CurrentMultipliers
    public float CurrentMultipliers => damageMultiplier * tempDamageMultiplier;

    public float CurrentDamage =>
        Mathf.Max(0f, (baseDamage + flatBonusDamage) * CurrentMultipliers);

    // Scale score/XP by actual dealt damage vs. baseline (includes flat and multipliers).
    // e.g., 3 dmg vs 5 baseline => 0.6x score/XP; 6 dmg vs 5 => 1.2x.
    public float ScoreXpDamageFactor => Mathf.Max(0f, DAMAGE_BASELINE > 0f ? (CurrentDamage / DAMAGE_BASELINE) : 1f);

    // Replace AddDamageMultiplier (expects +0.10f for +10%)
    public void AddDamageMultiplier(float addPercent)
    {
        if (Mathf.Approximately(addPercent, 0f)) return;
        damageMultiplier = Mathf.Max(0f, damageMultiplier + addPercent);
        // If you want compounding instead of additive, use:
        // damageMultiplier *= (1f + addPercent);
    }

    public void AddFlatDamage(int flatDamage, int bounces)
    {
        if (flatDamage == 0) return;
        flatBonusDamage = 0;
        flatBonusDamage += flatDamage;
        bonusBouncesNeeded = bounces;
        bonusBouncesRemaining = bonusBouncesNeeded;
    }

    // Replace AddTempDamageMultiplier to accept a factor (e.g., 1.10f for +10%)
    public void AddTempDamageMultiplier(float factor, int bounces)
    {
        tempDamageMultiplierStore = Mathf.Max(0f, factor);
        tmpBonusBouncesNeeded = bounces;
        tmpBonusBouncesRemaining = tmpBonusBouncesNeeded;
        tempDamageMultiplier = 1f; // not active yet
    }

    // Replace ConsumeBounceForDamageMods temp section
    public void ConsumeBounceForDamageMods()
    {
        bonusBouncesRemaining--;
        tmpBonusBouncesRemaining--;
        if (bonusBouncesRemaining < 0 && tmpBonusBouncesRemaining < 0)
            return;

        if (bonusBouncesRemaining == 0)
            flatBonusDamage = 0;

        // When the countdown elapses, activate queued temp factor
        if (tmpBonusBouncesRemaining > 0)
        {
            tempDamageMultiplier = 1f;
        }
        else if (tmpBonusBouncesRemaining == 0)
        {
            tempDamageMultiplier = 1 + tempDamageMultiplierStore;
            Debug.Log($"temp damage - {tempDamageMultiplier}");
            ResetTempBounceMods();
        }
    }

    private void ResetDamageMods()
    {
        flatBonusDamage = 0;
        bonusBouncesRemaining = 0;
    }

    // Keep this as-is or ensure it resets only the countdown
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

        _renderer = GetComponent<Renderer>();
    }

    void OnEnable()
    {
        IsActive = true;
        if (Pinball.Instance != null)
            Pinball.Instance.RegisterBall(this);
        col = GetComponent<Collider>();
        XPCollectorRegistry.I?.Register(col);

        // Ensure unique material so enabling _EMISSION doesn't affect others
        if (_renderer != null)
        {
            var shared = _renderer.sharedMaterial;
            if (shared != null && (_runtimeMat == null || _renderer.sharedMaterial == _runtimeMat))
            {
                _runtimeMat = Instantiate(shared);
                _runtimeMat.name = shared.name + " (Runtime Ball)";
                _runtimeMat.EnableKeyword("_EMISSION");
                _renderer.sharedMaterial = _runtimeMat;
            }
            ApplyGlow();
        }

        OnBallActivated?.Invoke(this);
    }

    void OnDisable()
    {
        IsActive = false;
        if (Pinball.Instance != null)
            Pinball.Instance.UnregisterBall(this);
        XPCollectorRegistry.I?.Unregister(col);

        BreakCombo("Ball disabled");
        OnBallDeactivated?.Invoke(this);
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

        if (comboActive)
        {

        }
    }

    void Update()
    {
    }

    private void RegisterBumperHitForCombo()
    {
        comboHitStreak++;

        if(!comboActive && comboHitStreak >= comboThreshold)
        {
            comboActive = true;
            Debug.Log("Combo activated!");
        }

        ApplyGlow();
        OnComboChanged?.Invoke(this);
    }

    // Computes a simple emission intensity curve: base + steps*perStep
    private float ComputeEmissionIntensity()
    {
        if (!comboActive) return Mathf.Max(0f, emissionBase);
        int steps = Mathf.Max(0, comboHitStreak - (comboThreshold - 1));
        return Mathf.Max(0f, emissionBase + emissionPerComboStep * steps);
    }

    // Assign a vibrant random glow color (not too dark)
    public void RandomizeGlowColor()
    {
        // H:[0..1], S:[0.65..1], V:[0.9..1] for bright colors
        var c = Random.ColorHSV(0f, 1f, 0.65f, 1f, 0.9f, 1f);
        GlowColor = c; // triggers ApplyGlow() and OnComboChanged for UI refresh
    }

    // Pushes emission color to the ball material
    private void ApplyGlow()
    {
        if (_renderer == null) return;

        float intensity = ComputeEmissionIntensity();
        var emissive = (Color)(glowColor * Mathf.LinearToGammaSpace(intensity));
        if (_runtimeMat != null)
        {
            _runtimeMat.EnableKeyword("_EMISSION");
            _runtimeMat.SetColor(EmissionColorId, emissive);
            _runtimeMat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
        }
        else
        {
            // Fallback via MaterialPropertyBlock if we didn't clone material
            var mpb = new MaterialPropertyBlock();
            _renderer.GetPropertyBlock(mpb);
            mpb.SetColor(EmissionColorId, emissive);
            _renderer.SetPropertyBlock(mpb);
        }
    }

    private void BreakCombo(string reason = null)
    {
        if(!comboActive && comboHitStreak == 0) return;
        comboHitStreak = 0;
        comboActive = false;

        // Refresh emission and notify UI
        ApplyGlow();
        OnComboChanged?.Invoke(this);
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

        RegisterBumperHitForCombo();

        // Compute base points and apply ONLY combo multiplier locally (XP factor untouched)
        int baseScore = bumperKind == 0 ? 100 : 50;
        int adjustedScore = Mathf.RoundToInt(baseScore * CurrentComboMultiplier);

        pinball?.AddScore(adjustedScore, bumpCount, bumpCountConsecutive, ScoreXpDamageFactor);

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

    // Break the combo when colliding with walls (but ignore bumpers/paddles)
    void OnCollisionEnter(Collision collision)
    {
        var other = collision.collider;

        // Do not break on bumpers/paddles
        if (other.CompareTag("Bumper") || other.CompareTag("SmallBumper") || other.CompareTag("Paddle"))
            return;

        bool inBreakLayer = (comboBreakLayers.value & (1 << other.gameObject.layer)) != 0;
        if (inBreakLayer || other.CompareTag("Wall"))
            BreakCombo("Wall");
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

    public void UpdateForcefieldStrength(float strength)
    {
    }
}