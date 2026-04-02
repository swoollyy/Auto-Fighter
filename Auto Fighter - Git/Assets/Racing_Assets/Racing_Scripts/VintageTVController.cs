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
    [Tooltip("Extra intensity added at max crash severity (1.0). Scales linearly with severity.")]
    [Range(0f, 1f)]
    [SerializeField] private float crashIntensityAdd = 0.45f;
    [Tooltip("How fast crash contribution eases back to zero after a non-lethal crash.")]
    [SerializeField] private float crashEaseOutSpeed = 1.8f;
    [Tooltip("Optional override for lethal (run-ending) crash spike; leave at 1 to use full crashIntensityAdd.")]
    [Range(0f, 2f)]
    [SerializeField] private float lethalCrashMultiplier = 1.0f;

    // runtime refs
    private Rigidbody _carRb;
    private CarController _car;
    private float _boost01; // 0..1
    private float _crash01; // 0..1, from severity

    // Cached defaults (as authored in inspector) so crash can spike and ease back to these.
    private float _defaultIntensity;
    private float _defaultNoise;

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

        // Cache defaults once so we can always return to them after crash spikes.
        _defaultIntensity = Mathf.Clamp01(baseIntensity);
        _defaultNoise = Mathf.Clamp01(noise);
    }

    private void OnEnable()
    {
        TryBindToActiveCar();
    }

    private void OnDisable()
    {
        UnbindCarEvents();
    }

    private void Update()
    {
        if (allowKeyboardToggles && Input.GetKeyDown(toggleKey))
            effectEnabled = !effectEnabled;

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
        // Non-lethal crash: spike crash contribution based on severity (0..1)
        severity01 = Mathf.Clamp01(severity01);
        _crash01 = Mathf.Max(_crash01, severity01);
    }

    private void HandleLethalCrash(float severity01)
    {
        // Run-ending crash: always go to max crash spike (optionally scaled)
        float sev = Mathf.Clamp01(severity01);
        float target = Mathf.Clamp01(lethalCrashMultiplier * Mathf.Max(sev, 1f));
        _crash01 = Mathf.Max(_crash01, target);
    }

    private void PushToMaterial()
    {
        if (vintageMat == null) return;

        if (!effectEnabled)
        {
            vintageMat.SetFloat(ID_Intensity, 0f);
            return;
        }

        // Start from the authored default intensity and apply crash scaling first.
        float crashScale = 1f + (_crash01 * crashIntensityAdd); // 0..(1+crashIntensityAdd)
        float intensity = Mathf.Clamp01(_defaultIntensity * crashScale);

        // Speed contribution (m/s)
        if (scaleWithSpeed && _carRb != null)
        {
            float spd = _carRb.velocity.magnitude;
            float s01 = Mathf.InverseLerp(speedMin, speedMax, spd);
            intensity += s01 * speedIntensityAdd;
        }

        // Boost contribution
        if (boostPulse)
        {
            intensity += _boost01 * boostIntensityAdd;
        }

        intensity = Mathf.Clamp01(intensity);

        vintageMat.SetFloat(ID_Intensity, intensity);
        vintageMat.SetFloat(ID_ScanlineStrength, scanlineStrength);
        vintageMat.SetFloat(ID_ScanlineDensity, scanlineDensity);
        vintageMat.SetFloat(ID_Jitter, jitter);
        // Noise scales with the same crash factor so both feel linked.
        float noiseScale = Mathf.Clamp01(_defaultNoise * crashScale);
        vintageMat.SetFloat(ID_Noise, noiseScale);
        vintageMat.SetFloat(ID_Vignette, vignette);
        vintageMat.SetFloat(ID_Chromatic, chromatic);
        vintageMat.SetFloat(ID_TimeScale, timeScale);
    }
}
