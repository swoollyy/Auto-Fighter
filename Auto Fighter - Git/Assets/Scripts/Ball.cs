using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
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
    private float tempDamageBounceMultiplier = 1f;
    private int tempDamageBouncesRemaining;
    private float forceFieldGravity;

    // === Speed Uncap (BluePad / SkillAim interaction) ===
    [Header("Runtime Speed Uncap")]
    [SerializeField, Tooltip("DEBUG: show current uncapped state in inspector")]
    private bool _maxSpeedUncapped;
    private float _maxSpeedUncapUntil;
    private float _temporaryMaxSpeedTarget;         // absolute target (baseline + additive). <=0 => fully uncapped (no finite cap)
    private bool _restoreOriginalMaxOnExpire;
    // NEW: additive bonus kept separate from baseline (never mutates baseline)
    private float _tempMaxSpeedAdd;                 // additive bonus while uncapped (0 => none)
    private bool _fullUncapped;                     // true if request was for fully uncapped (old <=0 semantic)

    // Apply temporary effects while uncapped
    private bool _uncapModsApplied;
    private float _savedBounciness = -1f;
    private float _uncapDamageBonus = 1f;

    [Header("Glow Light Smoothing")]
    [SerializeField, Tooltip("Units/sec to brighten when speeding up. Higher = snappier rise.")]
    private float glowIntensityRiseRate = 8f;
    [SerializeField, Range(0.03f, 0.6f), Tooltip("Smooth time (s) to dim when slowing down. Lower = faster.")]
    private float glowIntensityFallSmoothTime = 0.18f;

    private float _glowIntensity;
    private float _glowIntensityVel;

    [Header("Kill Bumper Velocity Control")]
    [SerializeField, Tooltip("Apply a soft clamp to post‑kill bumper impulses so the ball does not go flying.")]
    private bool limitKillBumperImpulse = true;
    [SerializeField, Tooltip("Target cap for speed right after killing a bumper (only if exceeded).")]
    private float killBumperSpeedCap = 55f;
    [SerializeField, Range(0f, 1f), Tooltip("How strongly to damp the excess above the cap (0 = none, 1 = snap directly to cap).")]
    private float killBumperDampStrength = 0.65f;
    [SerializeField, Tooltip("Clamp absolute vertical (Y) component after kill to prevent huge upward/downward launches.")]
    private float killBumperVerticalClamp = 25f;
    [SerializeField, Tooltip("Ignore soft clamp if the added impulse was small relative to current speed (ratio threshold).")]
    private float killBumperImpulseRatioThreshold = 0.35f;

    [Header("Global Velocity Clamp")]
    [SerializeField, Tooltip("Clamp to the finite uncap value (>0) while the uncap window is active. Fully uncapped (<=0) remains uncapped.")]
    private bool clampDuringUncapIfFinite = true;

    [SerializeField, Tooltip("Optional absolute safety ceiling regardless of state. 0 = disabled.")]
    private float hardSpeedCap = 0f;

    /// <summary>
    /// TEMPORARY (legacy absolute signature): raise *effective* cap to newMaxSpeed (baseline NOT mutated).
    /// newMaxSpeed <= 0 => fully uncapped (no finite cap). Kept for existing callsites (SkillAim etc).
    /// </summary>
    public void TemporarilyUncapMaxSpeed(float duration, float newMaxSpeed, bool restoreOriginal = true)
    {
        if (newMaxSpeed <= 0f)
        {
            // Fully uncapped (no finite target)
            TemporarilyUncapMaxSpeedAdd(duration, 0f, restoreOriginal, fullUncap: true);
        }
        else
        {
            // Convert absolute to additive above baseline
            float add = Mathf.Max(0f, newMaxSpeed - maxSpeed);
            TemporarilyUncapMaxSpeedAdd(duration, add, restoreOriginal, fullUncap: false);
        }
    }

    /// <summary>
    /// NEW ADDITIVE API: add 'addAmount' to baseline for the window (without mutating baseline).
    /// fullUncap overrides finite additive (unlimited except hardSpeedCap).
    /// </summary>
    public void TemporarilyUncapMaxSpeedAdd(float duration, float addAmount, bool restoreOriginal = true, bool fullUncap = false)
    {
        duration = Mathf.Max(0.05f, duration);

        // Start / extend window
        _maxSpeedUncapped = true;
        _maxSpeedUncapUntil = Mathf.Max(_maxSpeedUncapUntil, Time.time + duration);
        _restoreOriginalMaxOnExpire |= restoreOriginal;
        _fullUncapped |= fullUncap;

        if (!_fullUncapped)
        {
            // Track the highest additive requested
            _tempMaxSpeedAdd = Mathf.Max(_tempMaxSpeedAdd, addAmount);
            _temporaryMaxSpeedTarget = maxSpeed + _tempMaxSpeedAdd;
        }
        else
        {
            // Fully uncapped: ignore additive
            _temporaryMaxSpeedTarget = 0f;
            _tempMaxSpeedAdd = 0f;
        }

        if (!_uncapModsApplied)
            ApplyUncapMods();

        // Optional initial velocity bump if finite target specified
        var rbLocal = rb;
        if (rbLocal && !_fullUncapped && _temporaryMaxSpeedTarget > maxSpeed && rbLocal.velocity.magnitude < _temporaryMaxSpeedTarget * 0.5f)
        {
            rbLocal.velocity = rbLocal.velocity.normalized * Mathf.Min(_temporaryMaxSpeedTarget * 0.65f, _temporaryMaxSpeedTarget);
        }
    }

    // === Glow / Combo UI state ===
    [Header("Glow")]
    [SerializeField] private Color glowColor = Color.red;
    [SerializeField, Range(0f, 5f)] private float emissionBase = 1.5f;
    [SerializeField, Range(0f, 5f)] private float emissionPerComboStep = 0.30f;

    [SerializeField, Tooltip("Optional child Light used to match glow color and modulate intensity with speed.")]
    private Light glowLight;

    private const float LightIntensityMin = .6f;
    private const float LightIntensityMax = 3.5f;

    private Renderer _renderer;
    private Material _runtimeMat;
    private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

    public static event System.Action<Ball> OnBallActivated;
    public static event System.Action<Ball> OnBallDeactivated;
    public event System.Action<Ball> OnComboChanged;

    public Color GlowColor
    {
        get => glowColor;
        set
        {
            glowColor = value;
            ApplyGlow();
            OnComboChanged?.Invoke(this);
        }
    }

    public bool IsComboActive => comboActive;
    public float CurrentComboMultiplierUI => CurrentComboMultiplier;
    public float EmissionIntensityUI => ComputeEmissionIntensity();

    private int _nextPortalHitMult = 1;

    public void ActivatePortalBoost(int mult = 2)
    {
        _nextPortalHitMult = Mathf.Max(_nextPortalHitMult, Mathf.Max(1, mult));
    }
    public int ConsumePortalBoost()
    {
        int m = _nextPortalHitMult;
        _nextPortalHitMult = 1;
        return Mathf.Max(1, m);
    }

    [SerializeField] private int comboThreshold = 3;
    [SerializeField, Range(0.05f, 1f)] private float comboBonusPerHit = 0.10f;
    [SerializeField] private LayerMask comboBreakLayers;

    private int comboHitStreak;
    private bool comboActive;

    public float CurrentComboMultiplier
    {
        get
        {
            if (!comboActive) return 1f;
            int effectiveHits = Mathf.Max(0, comboHitStreak - (comboThreshold - 1));
            return 1f + comboBonusPerHit * effectiveHits;
        }
    }

    private const float DAMAGE_BASELINE = 5f;

    // BASELINE (permanent) max speed (CHANGED: never mutated by temporary uncaps; only grow/shrink rewards)
    [Tooltip("Baseline max speed. Temporary uncaps no longer modify this value.")]
    public float maxSpeed = 50f; // baseline

    // Effective cap used for logic (helper)
    private float EffectiveMaxSpeed
        => _maxSpeedUncapped
            ? (_fullUncapped
                ? float.PositiveInfinity
                : (_temporaryMaxSpeedTarget > 0f
                    ? _temporaryMaxSpeedTarget
                    : maxSpeed + _tempMaxSpeedAdd))
            : maxSpeed;

    public float BaseDamage
    {
        get => baseDamage;
        set => baseDamage = Mathf.Max(0f, value);
    }

    public float CurrentMultipliers => damageMultiplier * tempDamageMultiplier * tempDamageBounceMultiplier * _uncapDamageBonus;
    public float CurrentDamage => Mathf.Max(0f, (baseDamage + flatBonusDamage) * CurrentMultipliers);
    public float ScoreXpDamageFactor => Mathf.Max(0f, DAMAGE_BASELINE > 0f ? (CurrentDamage / DAMAGE_BASELINE) : 1f);

    public void AddDamageMultiplier(float addPercent)
    {
        if (Mathf.Approximately(addPercent, 0f)) return;
        damageMultiplier = Mathf.Max(0f, damageMultiplier + addPercent);
    }

    public void AddFlatDamage(int flatDamage, int bounces)
    {
        if (flatDamage == 0) return;
        flatBonusDamage = 0;
        flatBonusDamage += flatDamage;
        bonusBouncesNeeded = bounces;
        bonusBouncesRemaining = bonusBouncesNeeded;
    }

    public void AddTempDamageMultiplier(float factor, int bounces)
    {
        tempDamageMultiplierStore = Mathf.Max(0f, factor);
        tmpBonusBouncesNeeded = bounces;
        tmpBonusBouncesRemaining = tmpBonusBouncesNeeded;
        tempDamageMultiplier = 1f;
    }

    public void AddTempDamageForBounce(float factor, int bounces)
    {
        tempDamageBounceMultiplier *= Mathf.Max(0f, factor);
        tempDamageBouncesRemaining = bounces;
    }

    public void ConsumeBounceForDamageMods()
    {
        bonusBouncesRemaining--;
        tmpBonusBouncesRemaining--;
        tempDamageBouncesRemaining--;

        if (bonusBouncesRemaining < 0 && tmpBonusBouncesRemaining < 0 && tempDamageBouncesRemaining < 0)
            return;

        if (bonusBouncesRemaining == 0)
            flatBonusDamage = 0;

        if (tmpBonusBouncesRemaining > 0)
        {
            tempDamageMultiplier = 1f;
        }
        else if (tmpBonusBouncesRemaining == 0)
        {
            tempDamageMultiplier = 1 + tempDamageMultiplierStore;
            ResetTempBounceMods();
        }

        if (tempDamageBouncesRemaining == 0)
        {
            tempDamageBounceMultiplier = 1;
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

    [SerializeField] private ParticleSystemForceField forceField;

    public float forceFieldRadius = 0f;

    [SerializeField, Tooltip("Baseline (prefab) radius; level-up inherits via Pinball.XPBaseRadiusScale")]
    private float baseForceFieldRadius = 0f;

    private float tempForcefieldScale = 1f;

    public float TempForcefieldScale => tempForcefieldScale;

    public void SetTempForcefieldScaleAbsolute(float newAbsoluteScale)
    {
        newAbsoluteScale = Mathf.Max(0.0001f, newAbsoluteScale);
        float factor = newAbsoluteScale / Mathf.Max(0.0001f, tempForcefieldScale);
        ApplyTempForcefieldScale(factor);
    }

    public float forceFieldRadiusEffective => baseForceFieldRadius * (Pinball.Instance ? Pinball.Instance.XPBaseRadiusScale : 1f) * tempForcefieldScale;

    Collider col;

    int count;
    int dirBumpCount;

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
        pinball = GameObject.FindWithTag("PinballManager").GetComponent<Pinball>();
        _renderer = GetComponent<Renderer>();

        if (!glowLight)
            glowLight = GetComponentInChildren<Light>(true);
    }

    void OnEnable()
    {
        IsActive = true;
        if (Pinball.Instance != null)
            Pinball.Instance.RegisterBall(this);
        col = GetComponent<Collider>();
        XPCollectorRegistry.I?.Register(col);

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
            UpdateGlowLightIntensity();
            if (glowLight) _glowIntensity = glowLight.intensity;
        }

        OnBallActivated?.Invoke(this);
    }

    void OnDisable()
    {
        IsActive = false;
        if (Pinball.Instance != null)
            Pinball.Instance.UnregisterBall(this);
        XPCollectorRegistry.I?.Unregister(col);

        // Clear uncapped state (baseline never mutated now)
        if (_maxSpeedUncapped)
        {
            _temporaryMaxSpeedTarget = 0f;
            _tempMaxSpeedAdd = 0f;
            _fullUncapped = false;
            _maxSpeedUncapped = false;
        }

        RevertUncapMods();

        BreakCombo("Ball disabled");
        OnBallDeactivated?.Invoke(this);
    }

    void Start()
    {
        count = 0;
        debugTimer = 0;
        IsActive = true;

        forceFieldGravity = forceField.gravity.constant;

        baseForceFieldRadius = forceField.endRange;
        forceFieldRadius = baseForceFieldRadius;
        RefreshForcefieldFromContext();
    }

    void FixedUpdate()
    {
        if (_maxSpeedUncapped)
        {
            if (!_uncapModsApplied)
                ApplyUncapMods();

            if (Time.time >= _maxSpeedUncapUntil)
            {
                _temporaryMaxSpeedTarget = 0f;
                _tempMaxSpeedAdd = 0f;
                _fullUncapped = false;
                RevertUncapMods();
                _maxSpeedUncapped = false;
            }
            else
            {
                // Finite cap case
                if (!_fullUncapped && _temporaryMaxSpeedTarget > 0f)
                {
                    if (clampDuringUncapIfFinite)
                        EnforceSpeedCapNow(_temporaryMaxSpeedTarget);
                }
                // Fully uncapped: no clamp except hardSpeedCap
            }
        }
        else
        {
            EnforceSpeedCapNow(maxSpeed);
            if (_uncapModsApplied)
                RevertUncapMods();
        }

        if (hardSpeedCap > 0f)
            EnforceSpeedCapNow(hardSpeedCap);

        UpdateGlowLightIntensity();
    }

    void Update() { }

    private void RegisterBumperHitForCombo()
    {
        comboHitStreak++;
        if (!comboActive && comboHitStreak >= comboThreshold)
            comboActive = true;

        ApplyGlow();
        OnComboChanged?.Invoke(this);
    }

    private float ComputeEmissionIntensity()
    {
        if (!comboActive) return Mathf.Max(0f, emissionBase);
        int steps = Mathf.Max(0, comboHitStreak - (comboThreshold - 1));
        return Mathf.Max(0f, emissionBase + emissionPerComboStep * steps);
    }

    public void RandomizeGlowColor()
    {
        var c = UnityEngine.Random.ColorHSV(0f, 1f, 0.65f, 1f, 0.9f, 1f);
        GlowColor = c;
    }

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
            var mpb = new MaterialPropertyBlock();
            _renderer.GetPropertyBlock(mpb);
            mpb.SetColor(EmissionColorId, emissive);
            _renderer.SetPropertyBlock(mpb);
        }
        SyncGlowLightColor();
    }

    private void SyncGlowLightColor()
    {
        if (glowLight != null)
            glowLight.color = glowColor;
    }

    private void UpdateGlowLightIntensity()
    {
        if (glowLight == null || rb == null) return;

        // Map speed to intensity using effective cap if finite; fallback to baseline if infinite
        float cap = float.IsInfinity(EffectiveMaxSpeed) ? maxSpeed : Mathf.Max(0.01f, EffectiveMaxSpeed);
        float speed = rb.velocity.magnitude;
        float t = Mathf.InverseLerp(0f, cap, speed);
        float target = Mathf.Lerp(LightIntensityMin, LightIntensityMax, t);

        if (_glowIntensity <= 0f)
            _glowIntensity = glowLight.intensity > 0f ? glowLight.intensity : target;

        float dt = Time.unscaledDeltaTime;
        if (target >= _glowIntensity)
        {
            _glowIntensity = Mathf.MoveTowards(_glowIntensity, target, glowIntensityRiseRate * dt);
        }
        else
        {
            _glowIntensity = Mathf.SmoothDamp(_glowIntensity, target, ref _glowIntensityVel, glowIntensityFallSmoothTime, Mathf.Infinity, dt);
        }

        _glowIntensity = Mathf.Clamp(_glowIntensity, LightIntensityMin, LightIntensityMax);
        glowLight.intensity = _glowIntensity;
    }

    private void BreakCombo(string reason = null)
    {
        if (!comboActive && comboHitStreak == 0) return;
        comboHitStreak = 0;
        comboActive = false;
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
        rb.velocity = Vector3.zero;
        rb.AddForce(Vector3.forward * (50f * power), ForceMode.Impulse);
    }

    public void Push(float power)
    {
        rb.AddForce((-Vector3.right * (40f * power)) + (Vector3.forward * (5.5f * power)), ForceMode.Impulse);
    }

    public void Bump(Vector3 direction, float deltaV, int bumperKind, Bumper bumperInstance, int portalBoost = 1)
    {
        bumpCount++;

        Vector3 currentDir = direction.sqrMagnitude > .0001f ? direction.normalized : Vector3.forward;

        if (Time.time - lastBumpTime > sameDirWindow)
            sameDirHits = 0;

        if (prevBumpDirection != Vector3.zero)
        {
            float cos = Vector3.Dot(currentDir, prevBumpDirection.normalized);
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
            return;
        }

        rb.AddForce(currentDir * deltaV, ForceMode.Impulse);

        if (!_maxSpeedUncapped || (_temporaryMaxSpeedTarget > 0f && clampDuringUncapIfFinite))
        {
            float cap = _maxSpeedUncapped
                ? (_temporaryMaxSpeedTarget > 0f ? _temporaryMaxSpeedTarget : maxSpeed + _tempMaxSpeedAdd)
                : maxSpeed;
            EnforceSpeedCapNow(cap);
        }
        if (hardSpeedCap > 0f) EnforceSpeedCapNow(hardSpeedCap);

        ApplyKillBumperVelocitySoftClamp(bumperInstance);
        RegisterBumperHitForCombo();

        int baseScore = bumperKind == 0 ? 100 : 50;
        int adjustedScore = Mathf.RoundToInt(baseScore * CurrentComboMultiplier);

        pinball?.AddScore(adjustedScore, bumpCount, bumpCountConsecutive, ScoreXpDamageFactor, ScoreMultPortal: Mathf.Max(1, portalBoost));

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

    void OnCollisionEnter(Collision collision)
    {
        var other = collision.collider;
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

        float bigDeflect = UnityEngine.Random.Range(120f, 160f);
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

    public void RefreshForcefieldFromContext()
    {
        float global = Pinball.Instance ? Pinball.Instance.XPBaseRadiusScale : 1f;
        float eff = Mathf.Max(0f, baseForceFieldRadius) * Mathf.Max(0.0001f, global) * Mathf.Max(0.0001f, tempForcefieldScale);
        forceFieldRadius = eff;
        if (forceField) forceField.endRange = eff;
    }

    public void ApplyTempForcefieldScale(float scaleFactor)
    {
        tempForcefieldScale *= Mathf.Max(0.0001f, scaleFactor);
        RefreshForcefieldFromContext();
    }

    [Obsolete("Use ApplyTempForcefieldScale instead.")]
    public void UpdateForcefield(float amount) => ApplyTempForcefieldScale(amount);

    public void UpdateForcefieldStrength(float strength)
    {
        forceField.gravity = strength;
    }

    public void ResetForcefieldStrength()
    {
        forceField.gravity = forceFieldGravity;
    }

    public void CopyFrom(Ball src)
    {
        if (src == null || ReferenceEquals(src, this)) return;

        BaseDamage = src.BaseDamage;

        flatBonusDamage = src.flatBonusDamage;
        damageMultiplier = src.damageMultiplier;

        tempDamageMultiplier = src.tempDamageMultiplier;
        tempDamageMultiplierStore = src.tempDamageMultiplierStore;
        tempDamageBounceMultiplier = src.tempDamageBounceMultiplier;

        bonusBouncesNeeded = src.bonusBouncesNeeded;
        bonusBouncesRemaining = src.bonusBouncesRemaining;
        tmpBonusBouncesNeeded = src.tmpBonusBouncesNeeded;
        tmpBonusBouncesRemaining = src.tmpBonusBouncesRemaining;
        tempDamageBouncesRemaining = src.tempDamageBouncesRemaining;

        var dstCol = GetComponent<Collider>();
        var srcCol = src.GetComponent<Collider>();
        if (dstCol && srcCol) dstCol.excludeLayers = srcCol.excludeLayers;

        transform.localScale = src.transform.localScale;
        maxSpeed = src.maxSpeed; // baseline ONLY (CHANGED: does not copy temporary uncapped target)

        EnsureUniquePhysicMaterial();
        src.EnsureUniquePhysicMaterial();
        if (col && col.material && src.col && src.col.material)
        {
            // If source is currently uncapped, do NOT copy boosted bounciness – use original
            if (src._maxSpeedUncapped && src._uncapModsApplied && src._savedBounciness >= 0f)
                col.material.bounciness = src._savedBounciness;
            else
                col.material.bounciness = src.col.material.bounciness;

            col.material.dynamicFriction = src.col.material.dynamicFriction;
            col.material.staticFriction = src.col.material.staticFriction;
            col.material.bounceCombine = src.col.material.bounceCombine;
            col.material.frictionCombine = src.col.material.frictionCombine;
        }

        if (forceField && src.forceField)
        {
            baseForceFieldRadius = src.baseForceFieldRadius;
            tempForcefieldScale = src.tempForcefieldScale;
            forceField.gravity = src.forceField.gravity;
            RefreshForcefieldFromContext();
        }

        // Clear any active uncapped state for the duplicate
        _maxSpeedUncapped = false;
        _temporaryMaxSpeedTarget = 0f;
        _tempMaxSpeedAdd = 0f;
        _fullUncapped = false;
        RevertUncapMods();
    }

    private void ApplyKillBumperVelocitySoftClamp(Bumper bumper)
    {
        if (!limitKillBumperImpulse || bumper == null || !bumper.IsDead) return;
        if (_maxSpeedUncapped && (_fullUncapped || _temporaryMaxSpeedTarget <= 0f)) return;
        if (rb == null) return;

        float capRef = _maxSpeedUncapped && _temporaryMaxSpeedTarget > 0f
            ? Mathf.Max(_temporaryMaxSpeedTarget, maxSpeed)
            : maxSpeed;

        Vector3 v = rb.velocity;
        float speed = v.magnitude;
        if (speed <= 0.0001f) return;

        float ratio = (speed - capRef) / Mathf.Max(1f, capRef);
        if (ratio < killBumperImpulseRatioThreshold && speed <= killBumperSpeedCap) return;

        if (speed > killBumperSpeedCap)
        {
            float excess = speed - killBumperSpeedCap;
            float damped = killBumperSpeedCap + excess * (1f - killBumperDampStrength);
            v = v.normalized * Mathf.Max(killBumperSpeedCap, damped);
        }

        if (Mathf.Abs(v.y) > killBumperVerticalClamp)
            v.y = Mathf.Sign(v.y) * killBumperVerticalClamp;

        float finalCap = capRef;
        if (hardSpeedCap > 0f) finalCap = Mathf.Min(finalCap, hardSpeedCap);
        if (finalCap > 0f && v.magnitude > finalCap)
            v = v.normalized * finalCap;

        rb.velocity = v;
    }

    public void GetProperties(Ball ball)
    {
        CopyFrom(ball);
    }

    public void UpdateForcefield(float amount, bool obsoleteCompatOnly) => ApplyTempForcefieldScale(amount);

    private void ApplyUncapMods()
    {
        EnsureUniquePhysicMaterial();
        if (col != null && col.material != null)
        {
            _savedBounciness = col.material.bounciness;
            col.material.bounciness = _savedBounciness * 1.8f;
        }

        _uncapDamageBonus = 1.75f;
        _uncapModsApplied = true;
    }

    private void RevertUncapMods()
    {
        if (!_uncapModsApplied) return;

        if (col != null && col.material != null && _savedBounciness >= 0f)
            col.material.bounciness = _savedBounciness;

        _uncapDamageBonus = 1f;
        _savedBounciness = -1f;
        _uncapModsApplied = false;
    }

    private void EnforceSpeedCapNow(float cap)
    {
        if (!rb) return;
        cap = Mathf.Max(0f, cap);
        if (cap <= 0f) return;

        var v = rb.velocity;
        float speed = v.magnitude;
        if (speed > cap)
            rb.velocity = v.normalized * cap;
    }
}