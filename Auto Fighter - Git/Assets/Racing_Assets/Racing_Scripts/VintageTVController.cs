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
    [Tooltip("How fast crash contribution eases back to zero after a non-lethal crash.")]
    [SerializeField] private float crashEaseOutSpeed = 1.8f;
    [Tooltip("Multiplier on top of crash spike for lethal / run-ending crash (can exceed 1).")]
    [SerializeField, Min(0f)] private float lethalCrashMultiplier = 1.0f;
    [Tooltip("Upper clamp for intensity sent to the shader (raise if crash multiplier should blow past 1).")]
    [SerializeField, Min(0.01f)] private float maxShaderIntensity = 8f;
    [Tooltip("Upper clamp for noise sent to the shader.")]
    [SerializeField, Min(0.01f)] private float maxShaderNoise = 8f;

    // runtime refs
    private Rigidbody _carRb;
    private CarController _car;
    private float _boost01; // 0..1
    private float _crash01; // 0..1, from severity
    private bool _wasDrivingEffects;

    // Cached defaults: intensity/noise from material in Awake; rest from inspector at startup.
    private float _defaultIntensity;
    private float _defaultNoise;
    private float _defaultScanlineStrength;
    private float _defaultScanlineDensity;
    private float _defaultJitter;
    private float _defaultVignette;
    private float _defaultChromatic;
    private float _defaultTimeScale;

    private static readonly int ID_Intensity = Shader.PropertyToID("_Intensity");
    private static readonly int ID_ScanlineStrength = Shader.PropertyToID("_ScanlineStrength");
    private static readonly int ID_ScanlineDensity = Shader.PropertyToID("_ScanlineDensity");
    private static readonly int ID_Jitter = Shader.PropertyToID("_Jitter");
    private static readonly int ID_Noise = Shader.PropertyToID("_Noise");
    private static readonly int ID_Vignette = Shader.PropertyToID("_Vignette");
    private static readonly int ID_Chromatic = Shader.PropertyToID("_Chromatic");
    private static readonly int ID_TimeScale = Shader.PropertyToID("_TimeScale");

    private void Awake()
    {
        if (vintageMat == null)
            Debug.LogWarning("[VintageTVController] No material assigned. Effect will do nothing.");
        else
        {
            // Derive base settings directly from the assigned material so this
            // controller follows whatever defaults the shader asset is authored with.
            baseIntensity = vintageMat.GetFloat(ID_Intensity);
            noise = vintageMat.GetFloat(ID_Noise);
        }

        // Cache defaults once so we can always return to them after crash spikes or leaving gameplay.
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

        bool shouldDrive = ShouldDriveDynamicEffects();
        if (!shouldDrive)
        {
            if (_wasDrivingEffects)
                ResetToDefaultsImmediate();
            _wasDrivingEffects = false;
            return;
        }

        _wasDrivingEffects = true;

        // if car got respawned, rebind
        if (_car == null)
            TryBindToActiveCar();

        float dt = Time.deltaTime;

        // ease boost contribution back down
        if (_boost01 > 0f)
            _boost01 = Mathf.MoveTowards(_boost01, 0f, boostEaseOutSpeed * dt);

        // ease crash contribution back down (for non-lethal crashes)
        if (_crash01 > 0f)
            _crash01 = Mathf.MoveTowards(_crash01, 0f, crashEaseOutSpeed * dt);

        PushToMaterial();
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
        var car = gm.ActiveCar;
        if (car == null || !car.gameObject.activeInHierarchy) return false;

        // Post-run coin breakdown: keep last TV look until skill tree (ReturnToSkillTree).
        if (state == GameManager_Racing.GameProgressState.RunEnd)
            return true;

        // Run-end narrative moves to Dialogue before ReturnToSkillTree — don't snap TV off mid-sequence.
        if (state == GameManager_Racing.GameProgressState.Dialogue && gm.RunEnded)
            return true;

        if (state != GameManager_Racing.GameProgressState.InRun) return false;
        if (!gm.IsGameplayLive) return false;
        return true;
    }

    /// <summary>
    /// Restore inspector fields + material to cached defaults (material-sourced intensity/noise).
    /// Call when leaving the run, losing the car, or on quit/disable.
    /// </summary>
    private void ResetToDefaultsImmediate()
    {
        UnbindCarEvents();
        _boost01 = 0f;
        _crash01 = 0f;
        _wasDrivingEffects = false;

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
    }

    private void TryBindToActiveCar()
    {
        var gm = GameManager_Racing.Instance;
        if (gm == null) return;

        var active = gm.ActiveCar; // GameManager_Racing already exposes the current car :contentReference[oaicite:3]{index=3}
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
        severity01 = Mathf.Clamp01(severity01);
        if (crashVisualSeverityFloor > 0f)
            severity01 = Mathf.Max(severity01, Mathf.Clamp01(crashVisualSeverityFloor));
        _crash01 = Mathf.Max(_crash01, severity01);
    }

    private void HandleLethalCrash(float severity01)
    {
        // Run-ending: at least full severity (1), or higher if lethalCrashMultiplier > 1.
        float floor = Mathf.Max(1f, lethalCrashMultiplier);
        float fromSev = Mathf.Clamp01(severity01) * Mathf.Max(1f, lethalCrashMultiplier);
        _crash01 = Mathf.Max(_crash01, Mathf.Max(floor, fromSev));
    }

    private void PushToMaterial()
    {
        if (vintageMat == null) return;

        if (!effectEnabled)
        {
            vintageMat.SetFloat(ID_Intensity, 0f);
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

        vintageMat.SetFloat(ID_Intensity, intensity);
        vintageMat.SetFloat(ID_ScanlineStrength, scanlineStrength);
        vintageMat.SetFloat(ID_ScanlineDensity, scanlineDensity);
        vintageMat.SetFloat(ID_Jitter, jitter);
        vintageMat.SetFloat(ID_Noise, noiseOut);
        vintageMat.SetFloat(ID_Vignette, vignette);
        vintageMat.SetFloat(ID_Chromatic, chromatic);
        vintageMat.SetFloat(ID_TimeScale, timeScale);
    }
}
