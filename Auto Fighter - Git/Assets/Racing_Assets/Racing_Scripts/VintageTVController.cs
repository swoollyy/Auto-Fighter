using UnityEngine;

[DisallowMultipleComponent]
public class VintageTVController : MonoBehaviour
{
    [Header("Material (uses Hidden/VintageTV)")]
    [SerializeField] private Material vintageMat;

    [Header("Enable / Debug")]
    [SerializeField] private bool effectEnabled = true;
    [SerializeField] private bool allowKeyboardToggles = true;
    [SerializeField] private KeyCode toggleKey = KeyCode.F6;

    [Header("Base Look")]
    [Range(0f, 1f)] public float baseIntensity = 0.45f;
    [Range(0f, 1f)] public float scanlineStrength = 0.45f;
    [Range(100f, 1200f)] public float scanlineDensity = 520f;
    [Range(0f, 1f)] public float jitter = 0.12f;
    [Range(0f, 1f)] public float noise = 0.08f;
    [Range(0f, 1f)] public float vignette = 0.25f;
    [Range(0f, 1f)] public float chromatic = 0.08f;

    [Header("Scaling (optional)")]
    [SerializeField] private bool scaleWithSpeed = true;

    [Tooltip("Speed (m/s) where speed scaling starts contributing.")]
    [SerializeField] private float speedMin = 6f;

    [Tooltip("Speed (m/s) where speed scaling hits max contribution.")]
    [SerializeField] private float speedMax = 38f;

    [Tooltip("How much extra intensity you get at speedMax (0..1 added).")]
    [Range(0f, 1f)]
    [SerializeField] private float speedIntensityAdd = 0.25f;

    [Header("Boost реакция (optional)")]
    [SerializeField] private bool boostPulse = true;

    [Tooltip("Extra intensity added while boosting (then it eases out).")]
    [Range(0f, 1f)]
    [SerializeField] private float boostIntensityAdd = 0.25f;

    [Tooltip("How fast boost intensity eases back to zero after boost ends.")]
    [SerializeField] private float boostEaseOutSpeed = 4f;

    [Header("Time Scale")]
    [SerializeField] private float timeScale = 1f;

    [Header("Crash Reaction")]
    [Tooltip("Minimum crash severity used for TV spike (0–1). Raw severity below this is treated as this value so light hits still read clearly.")]
    [Range(0f, 1f)]
    [SerializeField] private float crashVisualSeverityFloor = 0.5f;
    [Tooltip("At severity 1.0, intensity/noise scale by (1 + this). E.g. 3 => up to 4× material defaults.")]
    [SerializeField, Min(0f)] private float crashIntensityAdd = 0.8f;
    [Tooltip("Fallback ease rate if the car isn't available to sync fade length. Unused while a crash fling/recovery is driving the envelope.")]
    [SerializeField] private float crashEaseOutSpeed = 1.8f;
    [Tooltip("After the crash fling settles, CRT + color-flip take at least this long to ease out (scaled seconds).")]
    [SerializeField, Min(0.05f)] private float minCrashFxFadeSeconds = 0.55f;
    [Tooltip("Non-fatal crashes always spike to this CRT severity (same peak as a full burst), then ease out. Can exceed 1.")]
    [SerializeField, Min(0.01f)] private float nonFatalBurstSeverity = 2f;
    [Tooltip("Multiplier on top of crash spike for lethal / run-ending crash (can exceed 1).")]
    [SerializeField, Min(0f)] private float lethalCrashMultiplier = 1.0f;
    [Tooltip("Upper clamp for intensity sent to the shader (raise if crash multiplier should blow past 1).")]
    [SerializeField, Min(0.01f)] private float maxShaderIntensity = 8f;
    [Tooltip("Upper clamp for noise sent to the shader.")]
    [SerializeField, Min(0.01f)] private float maxShaderNoise = 8f;
    [Tooltip("Upper clamp for chromatic aberration when crash scaling is applied.")]
    [SerializeField, Min(0.01f)] private float maxShaderChromatic = 4f;
    [Tooltip("Full-screen color invert amount at full crash burst (0–1). No vignette ring.")]
    [Range(0f, 1f)]
    [SerializeField] private float crashColorFlip = 1f;
    [Tooltip("Brightness of the flipped look. Lower if the invert washes to white on dark tracks.")]
    [Range(0.05f, 1f)]
    [SerializeField] private float crashColorFlipGain = 0.85f;
    [Tooltip("How fast hit-based color flips ease out.")]
    [SerializeField, Min(0.01f)] private float crashColorFlipEaseOutSpeed = 1.1f;
    [Tooltip("How fast the final death-explosion color flip eases out (lower = longer).")]
    [SerializeField, Min(0.01f)] private float deathExplosionColorFlipEaseOutSpeed = 0.28f;
    [Tooltip("Hold the final death-explosion color flip before it starts fading.")]
    [SerializeField, Min(0f)] private float deathExplosionColorFlipHoldSeconds = 0.4f;

    [Header("Out of Fuel Flare")]
    [Tooltip("CRT spike severity when the tank first hits empty. Controls pulse strength directly (not clamped by crash floor / non-fatal peak).")]
    [SerializeField, Min(0f)] private float outOfFuelFlareSeverity = 0.9f;
    [Tooltip("How long to hold the spiked look before easing out (seconds).")]
    [SerializeField, Min(0f)] private float outOfFuelFlareHoldSeconds = 2.0f;
    [Tooltip("Ease-out speed after the hold. Lower = longer stretch. Uses crash ease if <= 0.")]
    [SerializeField, Min(0f)] private float outOfFuelFlareEaseOutSpeed = 0.35f;

    // runtime refs
    private Rigidbody _carRb;
    private CarController _car;
    private float _boost01; // 0..1
    private float _crash01; // 0..1+, from severity (CRT intensity/noise)
    private float _crashHoldUntil; // unscaledTime — don't ease crash flare before this
    private float _crashEaseOutSpeedOverride = -1f; // >= 0 overrides crashEaseOutSpeed until flare settles
    private float _crashFxPeak;
    private float _colorFlipPeak;
    private bool _crashFxOwnsColorFlip;
    private bool _crashFxFading;
    private float _crashFxFadeElapsed;
    private float _crashFxFadeDuration = 0.55f;
    private float _crashFxFadeFrom;
    private float _colorFlipFadeFrom;
    private bool _wasDrivingEffects;
    private float _colorFlip01; // 0..1 — separate from CRT so fuel punch can skip flip
    private float _colorFlipHoldUntil;
    private float _colorFlipEaseOutSpeed = 1.1f;
    private float _suppressCrashColorFlipUntil; // blocks the OnCrash that fuel-empty also fires

    // Cached defaults from inspector/scene at Awake (never from the shared material).
    private float _defaultIntensity;
    private float _defaultNoise;
    private float _defaultScanlineStrength;
    private float _defaultScanlineDensity;
    private float _defaultJitter;
    private float _defaultVignette;
    private float _defaultChromatic;
    private float _defaultTimeScale;

    private bool _introOverrideActive;
    private VintageTVLookSettings _introLook = new VintageTVLookSettings();
    private bool _introBlendOutActive;
    private float _introBlendDuration;
    private float _introBlendElapsed;
    private VintageTVLookSettings _introBlendFrom;

    private static readonly int ID_Intensity = Shader.PropertyToID("_Intensity");
    private static readonly int ID_ScanlineStrength = Shader.PropertyToID("_ScanlineStrength");
    private static readonly int ID_ScanlineDensity = Shader.PropertyToID("_ScanlineDensity");
    private static readonly int ID_Jitter = Shader.PropertyToID("_Jitter");
    private static readonly int ID_Noise = Shader.PropertyToID("_Noise");
    private static readonly int ID_Vignette = Shader.PropertyToID("_Vignette");
    private static readonly int ID_Chromatic = Shader.PropertyToID("_Chromatic");
    private static readonly int ID_TimeScale = Shader.PropertyToID("_TimeScale");
    private static readonly int ID_ColorFlip = Shader.PropertyToID("_ColorFlip");
    private static readonly int ID_ColorFlipGain = Shader.PropertyToID("_ColorFlipGain");

    /// <summary>True while a run-start intro is forcing CRT look settings (including blend-out).</summary>
    public bool IsIntroOverrideActive => _introOverrideActive || _introBlendOutActive;

    private void Awake()
    {
        if (vintageMat == null)
            Debug.LogWarning("[VintageTVController] No material assigned. Effect will do nothing.");

        // IMPORTANT: do NOT read intensity/noise back from the shared material.
        // Crash/results freezes mutate that asset in-memory; on skill-tree scene reload
        // those dirty values would become the new "defaults" and stick forever.
        // Inspector / scene-serialized fields are the source of truth.
        _defaultIntensity = baseIntensity;
        _defaultNoise = noise;
        _defaultScanlineStrength = scanlineStrength;
        _defaultScanlineDensity = scanlineDensity;
        _defaultJitter = jitter;
        _defaultVignette = vignette;
        _defaultChromatic = chromatic;
        _defaultTimeScale = timeScale;

        ApplyDefaultsToMaterial();
    }

    private void OnEnable()
    {
        TryBindToActiveCar();
    }

    private void OnDisable()
    {
        ResetToDefaultsImmediate();
    }

    private void OnDestroy()
    {
        ResetToDefaultsImmediate();
    }

    private void OnApplicationQuit()
    {
        ResetToDefaultsImmediate();
    }

    private void Update()
    {
        if (allowKeyboardToggles && Input.GetKeyDown(toggleKey))
            effectEnabled = !effectEnabled;

        // Run-start intro owns the material until cleared / blended out.
        if (_introOverrideActive)
        {
            ApplyIntroLookToMaterial();
            return;
        }

        if (_introBlendOutActive)
        {
            TickIntroBlendOut();
            return;
        }

        float dt = Time.unscaledDeltaTime;

        // Color flip always fades (hit flips + final explosion) — never permanently frozen.
        TickColorFlip(dt);

        // Hold CRT burst for the whole results screen — check BEFORE the idle
        // reset path so a missing/disabled car (or early leave prep) can't snap CRT off
        // while the results UI is still visible.
        if (IsPostRunResultsFreeze())
        {
            if (_car == null)
                TryBindToActiveCar();
            _wasDrivingEffects = true;
            PushToMaterial();
            return;
        }

        bool shouldDrive = ShouldDriveDynamicEffects();
        if (!shouldDrive)
        {
            // Always keep the shared fullscreen material at authored defaults while in
            // skill tree / menus — don't only reset on the edge (freeze can re-dirty it).
            if (_wasDrivingEffects)
                ResetToDefaultsImmediate();
            else
                ApplyDefaultsToMaterial();
            _wasDrivingEffects = false;
            return;
        }

        _wasDrivingEffects = true;

        // if car got respawned, rebind
        if (_car == null)
            TryBindToActiveCar();

        // ease boost contribution back down
        if (_boost01 > 0f)
            _boost01 = Mathf.MoveTowards(_boost01, 0f, boostEaseOutSpeed * Time.deltaTime);

        // Hold crash flare while tumbling, then fade with recovery (not a fixed snap-off).
        TickCrashVisual();

        PushToMaterial();
    }

    private void TickColorFlip(float dt)
    {
        if (_crashFxOwnsColorFlip) return;
        if (_colorFlip01 <= 0f) return;
        if (Time.unscaledTime < _colorFlipHoldUntil) return;

        float ease = _colorFlipEaseOutSpeed > 0f ? _colorFlipEaseOutSpeed : crashColorFlipEaseOutSpeed;
        _colorFlip01 = Mathf.MoveTowards(_colorFlip01, 0f, ease * dt);
        if (_colorFlip01 <= 0.0001f)
            _colorFlip01 = 0f;
    }

    /// <summary>
    /// Clear crash/boost spikes and restore the authored CRT defaults.
    /// Call when leaving results for skill tree or quick-restarting a run.
    /// </summary>
    public void ResetVisualToDefaults()
    {
        CancelIntroOverride();
        ResetToDefaultsImmediate();
    }

    /// <summary>
    /// Force absolute CRT look for the run-start intro (ignores speed/boost/crash dynamics).
    /// Pass the live serialized settings object so Play Mode inspector edits keep applying.
    /// </summary>
    public void BeginIntroOverride(VintageTVLookSettings look)
    {
        if (look == null)
            look = VintageTVLookSettings.CreateMaxNoiseDefaults();

        CancelIntroBlendOnly();
        _introLook = look;
        _introOverrideActive = true;
        _boost01 = 0f;
        _crash01 = 0f;
        ClearCrashFxEnvelope();
        ClearColorFlip();
        ApplyIntroLookToMaterial();
    }

    /// <summary>
    /// End intro override. If <paramref name="blendSeconds"/> &gt; 0, lerp CRT params
    /// from the intro look back to gameplay defaults over that duration; otherwise snap.
    /// </summary>
    public void EndIntroOverride(float blendSeconds = 0f)
    {
        if (!_introOverrideActive && !_introBlendOutActive)
            return;

        if (blendSeconds <= 0.0001f)
        {
            FinishIntroOverrideImmediate();
            return;
        }

        VintageTVLookSettings from = _introLook != null
            ? _introLook.Clone()
            : VintageTVLookSettings.CreateMaxNoiseDefaults();
        _introBlendFrom = from;
        _introOverrideActive = false;
        _introBlendOutActive = true;
        _introBlendDuration = blendSeconds;
        _introBlendElapsed = 0f;
        ApplyLerpedIntroLook(from, 0f);
    }

    private void CancelIntroOverride()
    {
        _introOverrideActive = false;
        CancelIntroBlendOnly();
    }

    private void CancelIntroBlendOnly()
    {
        _introBlendOutActive = false;
        _introBlendElapsed = 0f;
        _introBlendDuration = 0f;
        _introBlendFrom = null;
    }

    private void FinishIntroOverrideImmediate()
    {
        CancelIntroOverride();
        ApplyDefaultsToMaterial();
        // Re-sync public fields so the next dynamic push starts from defaults.
        baseIntensity = _defaultIntensity;
        noise = _defaultNoise;
        scanlineStrength = _defaultScanlineStrength;
        scanlineDensity = _defaultScanlineDensity;
        jitter = _defaultJitter;
        vignette = _defaultVignette;
        chromatic = _defaultChromatic;
        timeScale = _defaultTimeScale;
    }

    private void TickIntroBlendOut()
    {
        _introBlendElapsed += Time.unscaledDeltaTime;
        float u = _introBlendDuration > 0f
            ? Mathf.Clamp01(_introBlendElapsed / _introBlendDuration)
            : 1f;
        // Smoothstep so the settle into defaults reads less linear/harsh.
        float eased = u * u * (3f - 2f * u);
        ApplyLerpedIntroLook(_introBlendFrom, eased);

        if (u >= 1f)
            FinishIntroOverrideImmediate();
    }

    private void ApplyIntroLookToMaterial()
    {
        if (vintageMat == null) return;

        if (!effectEnabled)
        {
            vintageMat.SetFloat(ID_Intensity, 0f);
            vintageMat.SetFloat(ID_ColorFlip, 0f);
            return;
        }

        VintageTVLookSettings look = _introLook ?? VintageTVLookSettings.CreateMaxNoiseDefaults();
        ApplyLookSettingsToMaterial(look);
    }

    private void ApplyLerpedIntroLook(VintageTVLookSettings from, float t01)
    {
        if (vintageMat == null) return;

        if (!effectEnabled)
        {
            vintageMat.SetFloat(ID_Intensity, 0f);
            vintageMat.SetFloat(ID_ColorFlip, 0f);
            return;
        }

        if (from == null)
            from = VintageTVLookSettings.CreateMaxNoiseDefaults();

        VintageTVLookSettings blended = new VintageTVLookSettings
        {
            intensity = Mathf.Lerp(from.intensity, _defaultIntensity, t01),
            noise = Mathf.Lerp(from.noise, _defaultNoise, t01),
            scanlineStrength = Mathf.Lerp(from.scanlineStrength, _defaultScanlineStrength, t01),
            scanlineDensity = Mathf.Lerp(from.scanlineDensity, _defaultScanlineDensity, t01),
            jitter = Mathf.Lerp(from.jitter, _defaultJitter, t01),
            vignette = Mathf.Lerp(from.vignette, _defaultVignette, t01),
            chromatic = Mathf.Lerp(from.chromatic, _defaultChromatic, t01),
            timeScale = Mathf.Lerp(from.timeScale, _defaultTimeScale, t01)
        };
        ApplyLookSettingsToMaterial(blended);
    }

    private void ApplyLookSettingsToMaterial(VintageTVLookSettings look)
    {
        if (vintageMat == null || look == null) return;

        vintageMat.SetFloat(ID_Intensity, Mathf.Clamp(look.intensity, 0f, maxShaderIntensity));
        vintageMat.SetFloat(ID_Noise, Mathf.Clamp(look.noise, 0f, maxShaderNoise));
        vintageMat.SetFloat(ID_ScanlineStrength, Mathf.Clamp01(look.scanlineStrength));
        vintageMat.SetFloat(ID_ScanlineDensity, look.scanlineDensity);
        vintageMat.SetFloat(ID_Jitter, Mathf.Clamp01(look.jitter));
        vintageMat.SetFloat(ID_Vignette, Mathf.Clamp01(look.vignette));
        vintageMat.SetFloat(ID_Chromatic, Mathf.Clamp(look.chromatic, 0f, maxShaderChromatic));
        vintageMat.SetFloat(ID_TimeScale, look.timeScale);
        vintageMat.SetFloat(ID_ColorFlip, 0f);
        vintageMat.SetFloat(ID_ColorFlipGain, crashColorFlipGain);
    }

    /// <summary>
    /// Boost/crash/speed modulate the TV during the run and through the run-end results screen.
    /// Resets when flow leaves for skill tree / menu (ProgressState no longer InRun or RunEnd).
    /// </summary>
    private static bool ShouldDriveDynamicEffects()
    {
        var gm = GameManager_Racing.Instance;
        if (gm == null) return false;

        var state = gm.ProgressState;

        // Post-run coin breakdown: keep last TV look until skill tree (no car required).
        if (state == GameManager_Racing.GameProgressState.RunEnd)
            return true;

        // Run-end narrative moves to Dialogue before ReturnToSkillTree — don't snap TV off mid-sequence.
        if (state == GameManager_Racing.GameProgressState.Dialogue && gm.RunEnded)
            return true;

        var car = gm.ActiveCar;
        if (car == null || !car.gameObject.activeInHierarchy) return false;

        if (state != GameManager_Racing.GameProgressState.InRun) return false;
        if (!gm.IsGameplayLive) return false;
        return true;
    }

    /// <summary>True while results (or post-run dialogue) should hold the CRT burst frozen.</summary>
    private static bool IsPostRunResultsFreeze()
    {
        var gm = GameManager_Racing.Instance;
        if (gm == null) return false;
        if (gm.ProgressState == GameManager_Racing.GameProgressState.RunEnd)
            return true;
        if (gm.ProgressState == GameManager_Racing.GameProgressState.Dialogue && gm.RunEnded)
            return true;
        return false;
    }

    /// <summary>
    /// Restore inspector fields + material to Awake-cached defaults.
    /// Call when leaving the run, losing the car, or on quit/disable.
    /// </summary>
    private void ResetToDefaultsImmediate()
    {
        UnbindCarEvents();
        _boost01 = 0f;
        _crash01 = 0f;
        _crashHoldUntil = 0f;
        _crashEaseOutSpeedOverride = -1f;
        ClearCrashFxEnvelope();
        ClearColorFlip();
        _wasDrivingEffects = false;
        // Don't clear an active intro override / blend from crash/unbind paths.
        if (_introOverrideActive)
        {
            ApplyIntroLookToMaterial();
            return;
        }
        if (_introBlendOutActive)
            return;

        baseIntensity = _defaultIntensity;
        noise = _defaultNoise;
        scanlineStrength = _defaultScanlineStrength;
        scanlineDensity = _defaultScanlineDensity;
        jitter = _defaultJitter;
        vignette = _defaultVignette;
        chromatic = _defaultChromatic;
        timeScale = _defaultTimeScale;

        ApplyDefaultsToMaterial();
    }

    private void ApplyDefaultsToMaterial()
    {
        if (vintageMat == null) return;

        vintageMat.SetFloat(ID_Intensity, _defaultIntensity);
        vintageMat.SetFloat(ID_ScanlineStrength, _defaultScanlineStrength);
        vintageMat.SetFloat(ID_ScanlineDensity, _defaultScanlineDensity);
        vintageMat.SetFloat(ID_Jitter, _defaultJitter);
        vintageMat.SetFloat(ID_Noise, _defaultNoise);
        vintageMat.SetFloat(ID_Vignette, _defaultVignette);
        vintageMat.SetFloat(ID_Chromatic, _defaultChromatic);
        vintageMat.SetFloat(ID_TimeScale, _defaultTimeScale);
        vintageMat.SetFloat(ID_ColorFlip, 0f);
        vintageMat.SetFloat(ID_ColorFlipGain, crashColorFlipGain);
    }

    private void TryBindToActiveCar()
    {
        var gm = GameManager_Racing.Instance;
        if (gm == null) return;

        var active = gm.ActiveCar; // current run car from game manager
        if (active == null) return;

        if (_car == active) return;

        UnbindCarEvents();

        _car = active;
        _carRb = _car.GetComponent<Rigidbody>();

        // Subscribe to boost and crash events on CarController
        _car.OnBoostStarted += HandleBoostStarted;
        _car.OnBoostEnded += HandleBoostEnded;
        _car.OnCrash += HandleCrash;
        _car.OnLethalCrash += HandleLethalCrash;
    }

    private void UnbindCarEvents()
    {
        if (_car != null)
        {
            _car.OnBoostStarted -= HandleBoostStarted;
            _car.OnBoostEnded -= HandleBoostEnded;
            _car.OnCrash -= HandleCrash;
            _car.OnLethalCrash -= HandleLethalCrash;
        }

        _car = null;
        _carRb = null;
        _boost01 = 0f;
        _crash01 = 0f;
        _crashHoldUntil = 0f;
        _crashEaseOutSpeedOverride = -1f;
        ClearCrashFxEnvelope();
        ClearColorFlip();
    }

    private void ClearCrashFxEnvelope()
    {
        _crashFxPeak = 0f;
        _colorFlipPeak = 0f;
        _crashFxOwnsColorFlip = false;
        _crashFxFading = false;
        _crashFxFadeElapsed = 0f;
        _crashFxFadeDuration = Mathf.Max(0.05f, minCrashFxFadeSeconds);
        _crashFxFadeFrom = 0f;
        _colorFlipFadeFrom = 0f;
    }

    private void BeginCrashFxBurst(float crashPeak, bool includeColorFlip)
    {
        float peak = Mathf.Max(0.01f, crashPeak);
        _crashFxPeak = Mathf.Max(_crashFxPeak, peak);
        _crash01 = Mathf.Max(_crash01, _crashFxPeak);
        _crashFxFading = false;
        _crashFxFadeElapsed = 0f;

        if (!includeColorFlip)
            return;

        _crashFxOwnsColorFlip = true;
        _colorFlipPeak = Mathf.Max(_colorFlipPeak, Mathf.Clamp01(crashColorFlip));
        _colorFlip01 = Mathf.Max(_colorFlip01, _colorFlipPeak);
    }

    /// <summary>
    /// Hold CRT/color-flip at full crash strength while the car is still tumbling/falling,
    /// then smoothstep back to the regular look over recovery (reorient + post-crash crawl).
    /// Remaining motion (hill fall, obstacle slide) slows the fade so it doesn't die off first.
    /// </summary>
    private void TickCrashVisual()
    {
        bool timedHold = Time.unscaledTime < _crashHoldUntil;
        bool inFling = _car != null && _car.IsInCrash;

        if (inFling)
        {
            if (_crashFxPeak < 0.01f)
                _crashFxPeak = Mathf.Max(_crash01, 0.01f);
            _crash01 = Mathf.Max(_crash01, _crashFxPeak);
            if (_crashFxOwnsColorFlip)
            {
                if (_colorFlipPeak < 0.01f)
                    _colorFlipPeak = Mathf.Max(_colorFlip01, Mathf.Clamp01(crashColorFlip));
                _colorFlip01 = Mathf.Max(_colorFlip01, _colorFlipPeak);
            }
            _crashFxFading = false;
            _crashFxFadeElapsed = 0f;
            return;
        }

        if (timedHold)
            return;

        if (_crash01 <= 0.0001f && (!_crashFxOwnsColorFlip || _colorFlip01 <= 0.0001f))
        {
            FinishCrashFxFade();
            return;
        }

        if (!_crashFxFading)
        {
            _crashFxFading = true;
            _crashFxFadeElapsed = 0f;
            _crashFxFadeFrom = Mathf.Max(_crash01, _crashFxPeak);
            _colorFlipFadeFrom = _crashFxOwnsColorFlip ? Mathf.Max(_colorFlip01, _colorFlipPeak) : _colorFlip01;
            _crashFxFadeDuration = ResolveCrashFxFadeDuration();
        }

        float fadeDt = Time.deltaTime;
        if (fadeDt < 0.00001f)
            fadeDt = Time.unscaledDeltaTime;

        if (_car != null)
        {
            float motion01 = _car.CrashFxMotion01;
            bool stillInRecovery = _car.IsReorienting || _car.IsPostCrashRecovery;
            bool stillAirborne = !_car.IsGrounded;
            if ((stillInRecovery || stillAirborne) && motion01 > 0.05f)
                fadeDt *= Mathf.Lerp(1f, 0.12f, motion01);
        }

        _crashFxFadeElapsed += fadeDt;
        float u = _crashFxFadeDuration > 0.0001f
            ? Mathf.Clamp01(_crashFxFadeElapsed / _crashFxFadeDuration)
            : 1f;
        float w = u * u * (3f - 2f * u);

        _crash01 = Mathf.Lerp(_crashFxFadeFrom, 0f, w);
        if (_crashFxOwnsColorFlip)
            _colorFlip01 = Mathf.Lerp(_colorFlipFadeFrom, 0f, w);

        if (u >= 1f)
            FinishCrashFxFade();
    }

    private float ResolveCrashFxFadeDuration()
    {
        float minFade = Mathf.Max(0.05f, minCrashFxFadeSeconds);
        float duration = minFade;

        if (_car != null)
        {
            float reorientLeft = _car.IsReorienting
                ? _car.CrashReorientDuration * (1f - _car.CrashReorientProgress01)
                : 0f;
            float postLeft = _car.IsPostCrashRecovery
                ? _car.PostCrashRecoveryRemaining
                : (_car.IsReorienting ? _car.PostCrashRecoveryWindow : 0f);
            duration = Mathf.Max(duration, reorientLeft + postLeft);
        }

        if (_crashEaseOutSpeedOverride >= 0.01f)
        {
            float from = Mathf.Max(_crashFxFadeFrom, 0.01f);
            duration = Mathf.Max(duration, from / _crashEaseOutSpeedOverride);
        }

        return duration;
    }

    private void FinishCrashFxFade()
    {
        _crash01 = 0f;
        _crashFxPeak = 0f;
        _crashFxFading = false;
        _crashFxFadeElapsed = 0f;
        _crashEaseOutSpeedOverride = -1f;
        if (_crashFxOwnsColorFlip)
        {
            _colorFlip01 = 0f;
            _crashFxOwnsColorFlip = false;
            _colorFlipPeak = 0f;
        }
    }

    private void ClearColorFlip()
    {
        _colorFlip01 = 0f;
        _colorFlipHoldUntil = 0f;
        _colorFlipEaseOutSpeed = crashColorFlipEaseOutSpeed;
        _crashFxOwnsColorFlip = false;
        _colorFlipPeak = 0f;
    }

    private void PulseColorFlip(float amount, float easeOutSpeed, float holdSeconds)
    {
        float peak = Mathf.Clamp01(amount);
        if (peak <= 0f) return;
        _colorFlip01 = Mathf.Max(_colorFlip01, peak);
        _colorFlipEaseOutSpeed = easeOutSpeed > 0f ? easeOutSpeed : crashColorFlipEaseOutSpeed;
        _colorFlipHoldUntil = Time.unscaledTime + Mathf.Max(0f, holdSeconds);
    }

    private void HandleBoostStarted()
    {
        if (!boostPulse) return;
        _boost01 = 1f; // immediate spike; we’ll ease out later
    }

    private void HandleBoostEnded()
    {
        // don’t hard drop to 0; let the ease-out handle it
    }

    private void HandleCrash(float severity01)
    {
        // Fuel-empty also invokes OnCrash for non-TV listeners — ignore that for Vintage TV
        // so Out Of Fuel Flare Severity actually controls the punch.
        if (Time.unscaledTime < _suppressCrashColorFlipUntil)
            return;

        // Hold at full burst for the whole fling, then fade with recovery (TickCrashVisual).
        float peak = Mathf.Max(0.01f, nonFatalBurstSeverity);
        if (crashVisualSeverityFloor > 0f)
            peak = Mathf.Max(peak, Mathf.Clamp01(crashVisualSeverityFloor));
        BeginCrashFxBurst(peak, includeColorFlip: true);
    }

    private void HandleLethalCrash(float severity01)
    {
        // Run-ending CRT spike. Color flip comes from hit feel + final explosion separately.
        float floor = Mathf.Max(1f, lethalCrashMultiplier);
        float fromSev = Mathf.Clamp01(severity01) * Mathf.Max(1f, lethalCrashMultiplier);
        BeginCrashFxBurst(Mathf.Max(floor, fromSev), includeColorFlip: true);
    }

    /// <summary>
    /// CRT punch when the tank first hits empty — intensity/noise only, no color flip.
    /// Strength comes only from Out Of Fuel Flare Severity (not crash floor / non-fatal peak).
    /// </summary>
    public void TriggerOutOfFuelFlare()
    {
        float severity = Mathf.Max(0f, outOfFuelFlareSeverity);

        _crash01 = Mathf.Max(_crash01, severity);
        _crashFxPeak = Mathf.Max(_crashFxPeak, severity);
        _crashFxFading = false;
        _crashFxFadeElapsed = 0f;
        _crashHoldUntil = Time.unscaledTime + Mathf.Max(0f, outOfFuelFlareHoldSeconds);
        _crashEaseOutSpeedOverride = outOfFuelFlareEaseOutSpeed > 0f
            ? outOfFuelFlareEaseOutSpeed
            : crashEaseOutSpeed;

        // CarController also fires OnCrash for non-TV listeners on the same frame.
        _suppressCrashColorFlipUntil = Time.unscaledTime + 0.2f;

        if (!IsIntroOverrideActive && ShouldDriveDynamicEffects())
            PushToMaterial();
    }

    /// <summary>
    /// Final death-explosion punch: same CRT burst as a crash, plus the longer color flip.
    /// Always fades out (CRT held through the color-flip hold, then eases).
    /// </summary>
    public void TriggerDeathExplosionColorFlip()
    {
        // Match non-fatal crash CRT spike so the eruption has the full vintage-TV burst.
        float peak = Mathf.Max(0.01f, nonFatalBurstSeverity);
        if (crashVisualSeverityFloor > 0f)
            peak = Mathf.Max(peak, Mathf.Clamp01(crashVisualSeverityFloor));
        _crash01 = Mathf.Max(_crash01, peak);
        _crashFxPeak = Mathf.Max(_crashFxPeak, peak);
        _crashFxFading = false;
        _crashFxFadeElapsed = 0f;
        _crashHoldUntil = Time.unscaledTime + Mathf.Max(0f, deathExplosionColorFlipHoldSeconds);
        _crashEaseOutSpeedOverride = deathExplosionColorFlipEaseOutSpeed > 0f
            ? deathExplosionColorFlipEaseOutSpeed
            : crashEaseOutSpeed;

        PulseColorFlip(
            crashColorFlip,
            deathExplosionColorFlipEaseOutSpeed,
            deathExplosionColorFlipHoldSeconds);
        // Death explosion owns the invert fade; don't let crash-recovery envelope cut it short.
        _crashFxOwnsColorFlip = false;

        if (!IsIntroOverrideActive)
            PushToMaterial();
    }

    private void PushToMaterial()
    {
        if (vintageMat == null) return;

        if (!effectEnabled)
        {
            vintageMat.SetFloat(ID_Intensity, 0f);
            vintageMat.SetFloat(ID_ColorFlip, 0f);
            return;
        }

        float crashScale = 1f + (_crash01 * crashIntensityAdd);
        float intensity = _defaultIntensity * crashScale;

        if (scaleWithSpeed && _carRb != null)
        {
            float spd = _carRb.velocity.magnitude;
            float s01 = Mathf.InverseLerp(speedMin, speedMax, spd);
            intensity += s01 * speedIntensityAdd;
        }

        if (boostPulse)
            intensity += _boost01 * boostIntensityAdd;

        intensity = Mathf.Clamp(intensity, 0f, maxShaderIntensity);

        float noiseOut = Mathf.Clamp(_defaultNoise * crashScale, 0f, maxShaderNoise);
        float chromaOut = Mathf.Clamp(_defaultChromatic * crashScale, 0f, maxShaderChromatic);
        float jitterOut = Mathf.Clamp01(jitter * Mathf.Lerp(1f, 1.4f, Mathf.Clamp01(_crash01)));

        float flip01 = Mathf.Clamp01(_colorFlip01);
        float vignetteOut = flip01 > 0.0001f ? 0f : vignette;

        vintageMat.SetFloat(ID_Intensity, intensity);
        vintageMat.SetFloat(ID_ScanlineStrength, scanlineStrength);
        vintageMat.SetFloat(ID_ScanlineDensity, scanlineDensity);
        vintageMat.SetFloat(ID_Jitter, jitterOut);
        vintageMat.SetFloat(ID_Noise, noiseOut);
        vintageMat.SetFloat(ID_Vignette, vignetteOut);
        vintageMat.SetFloat(ID_Chromatic, chromaOut);
        vintageMat.SetFloat(ID_TimeScale, timeScale);
        vintageMat.SetFloat(ID_ColorFlip, flip01);
        vintageMat.SetFloat(ID_ColorFlipGain, crashColorFlipGain);
    }
}

/// <summary>
/// Absolute CRT look values pushed during the run-start intro (not multiplied by crash/speed).
/// </summary>
[System.Serializable]
public class VintageTVLookSettings
{
    [Tooltip("Overall CRT blend / multiplies grain visibility.")]
    [Min(0f)] public float intensity = 6f;

    [Tooltip("Film-grain / static amount.")]
    [Min(0f)] public float noise = 6f;

    [Range(0f, 1f)] public float scanlineStrength = 0.85f;
    [Range(100f, 1200f)] public float scanlineDensity = 420f;
    [Range(0f, 1f)] public float jitter = 0.35f;
    [Range(0f, 1f)] public float vignette = 0.55f;

    [Tooltip("RGB split strength.")]
    [Min(0f)] public float chromatic = 0.45f;

    [Tooltip("How fast the grain animates.")]
    [Min(0f)] public float timeScale = 2.5f;

    public static VintageTVLookSettings CreateMaxNoiseDefaults()
    {
        return new VintageTVLookSettings();
    }

    public VintageTVLookSettings Clone()
    {
        return new VintageTVLookSettings
        {
            intensity = intensity,
            noise = noise,
            scanlineStrength = scanlineStrength,
            scanlineDensity = scanlineDensity,
            jitter = jitter,
            vignette = vignette,
            chromatic = chromatic,
            timeScale = timeScale
        };
    }
}
