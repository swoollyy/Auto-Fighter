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

    // === Speed Uncap (BluePad interaction) ===
    [Header("Runtime Speed Uncap")]
    [SerializeField, Tooltip("DEBUG: show current uncapped state in inspector")]
    private bool _maxSpeedUncapped;                       // NEW
    private float _maxSpeedUncapUntil;                    // NEW
    private float _originalMaxSpeedBeforeUncap;           // NEW
    private float _temporaryMaxSpeedTarget;               // NEW
    private bool _restoreOriginalMaxOnExpire;             // NEW

    // Apply temporary effects while uncapped
    private bool _uncapModsApplied;
    private float _savedBounciness = -1f;
    private float _uncapDamageBonus = 1f;

    // Add near existing Glow fields
    [Header("Glow Light Smoothing")]
    [SerializeField, Tooltip("Units/sec to brighten when speeding up. Higher = snappier rise.")]
    private float glowIntensityRiseRate = 8f;
    [SerializeField, Range(0.03f, 0.6f), Tooltip("Smooth time (s) to dim when slowing down. Lower = faster.")]
    private float glowIntensityFallSmoothTime = 0.18f;

    private float _glowIntensity;      // smoothed current intensity
    private float _glowIntensityVel;   // velocity for SmoothDamp

        // ================= Kill‑Bumper Velocity Control =================
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
    /// Temporarily disable clamping OR raise maxSpeed for 'duration' seconds.
    /// If newMaxSpeed <= 0: remove clamp entirely. Otherwise raise to newMaxSpeed.
    /// Also applies temporary bounciness (+80%) and damage (+20%) bonuses during the window.
    /// </summary>
    public void TemporarilyUncapMaxSpeed(float duration, float newMaxSpeed, bool restoreOriginal = true)
    {
        duration = Mathf.Max(0.05f, duration);

        // Capture the original cap only the first time we enter the window.
        if (!_maxSpeedUncapped)
        {
            _originalMaxSpeedBeforeUncap = maxSpeed;
            _restoreOriginalMaxOnExpire = restoreOriginal;
        }
        else
        {
            // If any subsequent call asks to restore, keep that intent.
            _restoreOriginalMaxOnExpire |= restoreOriginal;
        }

        _maxSpeedUncapped = true;

        // Extend the window if re-applied while active (don't shorten an existing longer window).
        _maxSpeedUncapUntil = Mathf.Max(_maxSpeedUncapUntil, Time.time + duration);

        // Prefer the highest temporary cap while active.
        // newMaxSpeed <= 0 means "fully uncapped": keep it as 0 so FixedUpdate doesn't set maxSpeed.
        if (newMaxSpeed <= 0f)
        {
            _temporaryMaxSpeedTarget = 0f;
        }
        else
        {
            _temporaryMaxSpeedTarget = _temporaryMaxSpeedTarget <= 0f
                ? newMaxSpeed
                : Mathf.Max(_temporaryMaxSpeedTarget, newMaxSpeed);
        }

        // Apply temporary physics/damage bonuses immediately
        if (!_uncapModsApplied)
            ApplyUncapMods();

        // Optionally bump current velocity so player feels immediate boost when fully uncapped.
        var rbLocal = rb;
        if (rbLocal && newMaxSpeed > 0 && rbLocal.velocity.magnitude < newMaxSpeed * 0.5f)
        {
            rbLocal.velocity = rbLocal.velocity.normalized * Mathf.Min(newMaxSpeed * 0.65f, newMaxSpeed);
        }
    }

    // === Glow / Combo UI state ===
    [Header("Glow")]
    [SerializeField] private Color glowColor = Color.red; // UI and material "glow" color
    [SerializeField, Range(0f, 5f)] private float emissionBase = 1.5f; // base emission when no combo
    [SerializeField, Range(0f, 5f)] private float emissionPerComboStep = 0.30f; // extra intensity per combo step

    // NEW: optional light attached to the ball prefab
    [SerializeField, Tooltip("Optional child Light used to match glow color and modulate intensity with speed.")]
    private Light glowLight;

    private const float LightIntensityMin = .6f;
    private const float LightIntensityMax = 3.5f;

    private Renderer _renderer;
    private Material _runtimeMat; // unique instance so enabling emission doesn't affect shared material
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
        // CHANGED: factor is now multiplicative (2 => double damage), not additive
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
    public float maxSpeed = 50f;

    [SerializeField] private ParticleSystemForceField forceField;

    // OLD (was directly mutated). Keep for UI/debug.
    public float forceFieldRadius = 0f;

    // NEW: persistent baseline captured from prefab
    [SerializeField, Tooltip("Baseline (prefab) radius; level-up inherits via Pinball.XPBaseRadiusScale")]
    private float baseForceFieldRadius = 0f;

    // NEW: temporary multiplier for one-off effects (e.g., vacuum)
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

        // Try auto-bind the child light if not set
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
            ApplyGlow();        // sets emission and syncs light color
            UpdateGlowLightIntensity(); // initialize intensity
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

        // If we get disabled mid-uncap, restore the original cap (prevents sticky maxSpeed).
        if (_maxSpeedUncapped)
        {
            if (_restoreOriginalMaxOnExpire)
                maxSpeed = _originalMaxSpeedBeforeUncap;
            _temporaryMaxSpeedTarget = 0f;
            _maxSpeedUncapped = false;
        }

        // Ensure temp uncap mods are reverted if ball gets disabled
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

        // Capture baseline from prefab and initialize effective radius
        baseForceFieldRadius = forceField.endRange;
        forceFieldRadius = baseForceFieldRadius;
        RefreshForcefieldFromContext();
    }

    void FixedUpdate()
    {
        // Speed cap logic
        if (_maxSpeedUncapped)
        {
            // Ensure temporary mods applied in case this was set elsewhere
            if (!_uncapModsApplied)
                ApplyUncapMods();

            if (Time.time >= _maxSpeedUncapUntil)
            {
                // Expired -> restore original maxSpeed if requested and revert temporary mods
                if (_restoreOriginalMaxOnExpire)
                    maxSpeed = _originalMaxSpeedBeforeUncap;

                _temporaryMaxSpeedTarget = 0f; // fully clear the window
                RevertUncapMods();
                _maxSpeedUncapped = false;
            }
            else
            {
                // While uncapped:
                //   - newMaxSpeed <= 0f => fully uncapped (no clamp here)
                //   - newMaxSpeed  > 0f => treat as a finite cap; clamp if enabled
                if (_temporaryMaxSpeedTarget > 0f)
                {
                    maxSpeed = Mathf.Max(_temporaryMaxSpeedTarget, _originalMaxSpeedBeforeUncap);
                    if (clampDuringUncapIfFinite)
                        EnforceSpeedCapNow(maxSpeed);
                }
            }
        }
        else
        {
            // Normal clamp
            EnforceSpeedCapNow(maxSpeed);

            // Safety: if for any reason mods are still applied while not uncapped, revert them
            if (_uncapModsApplied)
                RevertUncapMods();
        }

        // Optional absolute safety hard cap (applied last, regardless of state)
        if (hardSpeedCap > 0f)
            EnforceSpeedCapNow(hardSpeedCap);

        // Update the glow light intensity based on current speed
        UpdateGlowLightIntensity();
    }

    void Update()
    {
    }

    private void RegisterBumperHitForCombo()
    {
        comboHitStreak++;

        if (!comboActive && comboHitStreak >= comboThreshold)
        {
            comboActive = true;
        }

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

        // Keep the light color in sync with glow color
        SyncGlowLightColor();
    }

    // NEW: keep the attached light color in sync with the ball glow color
    private void SyncGlowLightColor()
    {
        if (glowLight != null)
            glowLight.color = glowColor;
    }

    // NEW: map speed [0..maxSpeed] to intensity [0.5 .. 2]
    private void UpdateGlowLightIntensity()
    {
        if (glowLight == null || rb == null) return;

        // Target intensity from current speed
        float max = Mathf.Max(0.01f, maxSpeed);
        float speed = rb.velocity.magnitude;
        float t = Mathf.InverseLerp(0f, max, speed);
        float target = Mathf.Lerp(LightIntensityMin, LightIntensityMax, t);

        // Initialize smoothed value once
        if (_glowIntensity <= 0f)
            _glowIntensity = glowLight.intensity > 0f ? glowLight.intensity : target;

        // Fast rise, slower fall
        float dt = Time.unscaledDeltaTime;
        if (target >= _glowIntensity)
        {
            // Quick but not instant rise
            _glowIntensity = Mathf.MoveTowards(_glowIntensity, target, glowIntensityRiseRate * dt);
        }
        else
        {
            // Smooth, slightly slower decay
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
        {
            sameDirHits = 0;
        }

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

        // NEW: immediate velocity cap after an impulse
        if (!_maxSpeedUncapped || (_temporaryMaxSpeedTarget > 0f && clampDuringUncapIfFinite))
        {
            float cap = _maxSpeedUncapped
                ? Mathf.Max(_temporaryMaxSpeedTarget, _originalMaxSpeedBeforeUncap)
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

    // === XP Forcefield helpers (NEW model) ===

    // Recompute effective XP radius from baseline, global scale and temp scale
    public void RefreshForcefieldFromContext()
    {
        float global = Pinball.Instance ? Pinball.Instance.XPBaseRadiusScale : 1f;
        float eff = Mathf.Max(0f, baseForceFieldRadius) * Mathf.Max(0.0001f, global) * Mathf.Max(0.0001f, tempForcefieldScale);
        forceFieldRadius = eff;
        if (forceField) forceField.endRange = eff;
    }

    // Temporary scaling used by one-shot powerups (vacuum)
    public void ApplyTempForcefieldScale(float scaleFactor)
    {
        tempForcefieldScale *= Mathf.Max(0.0001f, scaleFactor);
        RefreshForcefieldFromContext();
    }

    // Backwards-compat shim (if any old callsites exist)
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

    // NEW: comprehensive copy helper
    public void CopyFrom(Ball src)
    {
        if (src == null || ReferenceEquals(src, this)) return;

        // Base stats
        BaseDamage = src.BaseDamage;

        // Permanent + temporary multipliers
        flatBonusDamage = src.flatBonusDamage;
        damageMultiplier = src.damageMultiplier;

        tempDamageMultiplier = src.tempDamageMultiplier;
        tempDamageMultiplierStore = src.tempDamageMultiplierStore;
        tempDamageBounceMultiplier = src.tempDamageBounceMultiplier;

        // Bounce windows/counters
        bonusBouncesNeeded = src.bonusBouncesNeeded;
        bonusBouncesRemaining = src.bonusBouncesRemaining;
        tmpBonusBouncesNeeded = src.tmpBonusBouncesNeeded;
        tmpBonusBouncesRemaining = src.tmpBonusBouncesRemaining;
        tempDamageBouncesRemaining = src.tempDamageBouncesRemaining;

        var col = GetComponent<Collider>();
        var srcCol = src.GetComponent<Collider>();

        col.excludeLayers = srcCol.excludeLayers;

        // Size & speed
        transform.localScale = src.transform.localScale;
        maxSpeed = src.maxSpeed;

        // PhysicMaterial properties
        EnsureUniquePhysicMaterial();
        src.EnsureUniquePhysicMaterial();
        if (col && col.material && src.col && src.col.material)
        {
            col.material.bounciness = src.col.material.bounciness;
            col.material.dynamicFriction = src.col.material.dynamicFriction;
            col.material.staticFriction = src.col.material.staticFriction;
            col.material.bounceCombine = src.col.material.bounceCombine;
            col.material.frictionCombine = src.col.material.frictionCombine;
        }

        // XP forcefield state (NEW: inherit baseline & temp, then recompute from Pinball context)
        if (forceField && src.forceField)
        {
            baseForceFieldRadius = src.baseForceFieldRadius;
            tempForcefieldScale = src.tempForcefieldScale;
            forceField.gravity = src.forceField.gravity;
            RefreshForcefieldFromContext();
        }
    }

    private void ApplyKillBumperVelocitySoftClamp(Bumper bumper)
    {
        if (!limitKillBumperImpulse || bumper == null || !bumper.IsDead) return;

        // Only skip if fully uncapped (<= 0). If we have a finite uncap target, still clamp to it.
        if (_maxSpeedUncapped && _temporaryMaxSpeedTarget <= 0f) return;
        if (rb == null) return;

        // Determine the working cap reference for this moment
        float capRef = _maxSpeedUncapped && _temporaryMaxSpeedTarget > 0f
            ? Mathf.Max(_temporaryMaxSpeedTarget, _originalMaxSpeedBeforeUncap)
            : maxSpeed;

        Vector3 v = rb.velocity;
        float speed = v.magnitude;
        if (speed <= 0.0001f) return;

        // If the impulse wasn't disproportionately large, do nothing.
        float ratio = (speed - capRef) / Mathf.Max(1f, capRef);
        if (ratio < killBumperImpulseRatioThreshold && speed <= killBumperSpeedCap) return;

        // Only clamp if above desired cap (soft approach)
        if (speed > killBumperSpeedCap)
        {
            float excess = speed - killBumperSpeedCap;
            float damped = killBumperSpeedCap + excess * (1f - killBumperDampStrength);
            v = v.normalized * Mathf.Max(killBumperSpeedCap, damped);
        }

        // Vertical clamp (optional; preserve sign)
        if (Mathf.Abs(v.y) > killBumperVerticalClamp)
            v.y = Mathf.Sign(v.y) * killBumperVerticalClamp;

        // Ensure we don't exceed our working cap or the global hard cap afterwards
        float finalCap = capRef;
        if (hardSpeedCap > 0f) finalCap = Mathf.Min(finalCap, hardSpeedCap);
        if (finalCap > 0f && v.magnitude > finalCap)
            v = v.normalized * finalCap;

        rb.velocity = v;
    }

    // Backward-compat shim
    public void GetProperties(Ball ball)
    {
        CopyFrom(ball);
    }

    public void UpdateForcefield(float amount, bool obsoleteCompatOnly) => ApplyTempForcefieldScale(amount); // keep API stable for any lingering calls

    // === Helpers for temporary uncap bonuses ===
    private void ApplyUncapMods()
    {
        // +80% bounciness (x1.8)
        EnsureUniquePhysicMaterial();
        if (col != null && col.material != null)
        {
            _savedBounciness = col.material.bounciness;
            col.material.bounciness = _savedBounciness * 1.8f;
        }

        // +20% damage multiplier
        _uncapDamageBonus = 1.75f;

        _uncapModsApplied = true;
    }

    private void RevertUncapMods()
    {
        if (!_uncapModsApplied) return;

        if (col != null && col.material != null && _savedBounciness >= 0f)
        {
            col.material.bounciness = _savedBounciness;
        }

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