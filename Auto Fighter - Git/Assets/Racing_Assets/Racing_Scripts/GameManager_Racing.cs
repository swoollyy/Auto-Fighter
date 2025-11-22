using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

using Debug = UnityEngine.Debug;

public class GameManager_Racing : MonoBehaviour
{

    public static GameManager_Racing Instance { get; private set; }

    [Header("References")]
    [SerializeField] private ProceduralTrackGenerator trackGenerator;
    [SerializeField] private GameObject carPrefab;
    [SerializeField] private UIManager_Racing uiManager;
    [SerializeField] private TrackDistanceMeter distanceSystem;
    [SerializeField] private TrackCoinSpawner trackCoinSpawner;
    [SerializeField] private TrackObstacleSpawner trackObstacleSpawner;
    [SerializeField] private CrossObstacleDirector crossObstacleDirector;

    [Header("Camera & Follow")]
    [SerializeField] private Camera mainCam;
    [SerializeField] private CameraFollow cameraFollow;

    [Header("Crash FX")]
    [SerializeField] private bool enableCrashScreenShake = true;
    [SerializeField] private bool enableCrashSlowMo = true;

    // Base values – severity will scale these
    [SerializeField] private float crashShakeDuration = 0.18f;
    [SerializeField] private float crashShakeStrength = 0.45f;
    [SerializeField, Range(1, 30)] private int crashShakeVibrato = 16;
    [SerializeField, Range(0f, 180f)] private float crashShakeRandomness = 90f;

    [Header("Crash SlowMo Settings")]
    [SerializeField, Range(0.05f, 1f)] private float crashSlowMoScale = 0.35f;
    [SerializeField] private float crashSlowMoHold = 0.25f;
    [SerializeField] private float crashSlowMoEaseOut = 0.25f;

    // Optional curve to scale effect based on severity (x=0–1 severity, y=0–1 strength)
    [SerializeField] private AnimationCurve crashSlowMoCurve = AnimationCurve.Linear(0, 0.4f, 1, 1f);


    private Coroutine _crashSlowMoRoutine;
    private bool _ownsCrashSlowMo;

    [Header("Skill Tree UI (assign the root object that holds RacingSkillUI)")]
    [SerializeField] private GameObject skillTreeRoot;
    [SerializeField] private RacingSkillUI skillTreeUI;

    [Header("Spawn Settings")]
    [SerializeField] private float spawnForwardOffset = 2f;
    [SerializeField] private float spawnHeightOffset = 0.2f;

    [Header("Balancing")]
    [SerializeField, Min(0f)] private float coinsPerDistance = 0.33f; // coins per meter

    private GameObject carInstance;
    private CarController carController;
    private bool runEnded = false;
    private bool runStarted = false;
    private Coroutine beginRunRoutine;

    // NEW: simple distance tracking and finalize guard
    private float runDistanceMeters = 0f;
    private Rigidbody _carRb;
    private int _startingCurrency = 0;
    private bool _currencyAwarded = false;

    // NEW: breakdown this run
    private int _distanceCoinsThisRun = 0;
    private int _pickupCoinsThisRun = 0;
    private int _obstacleCoinsThisRun = 0;

    public CarController ActiveCar => carController;

    void Awake()
    {

        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;

        Physics.gravity = new Vector3(0, -9.81f, 0);
        Time.timeScale = 1f;


    }

    void Start()
    {
        if (trackGenerator == null)
            trackGenerator = FindGeneratorAnyState();

        EnsureRefs();            // ensure camera/ui/distanceSystem exist
        EnsureTrackCallbacksWired();

        if (skillTreeRoot != null) skillTreeRoot.SetActive(true);
        if (skillTreeUI == null) skillTreeUI = FindObjectOfType<RacingSkillUI>();
        if (skillTreeUI != null) skillTreeUI.BindGameManager(this);

    }

    void OnDestroy()
    {
        if (trackGenerator != null)
            trackGenerator.OnTrackGeneratedSuccessfully -= HandleTrackGenerated;

    }

    private void OnDisable()
    {

        // Make sure we release any slowmo we own when this manager is disabled
        if (_crashSlowMoRoutine != null)
        {
            StopCoroutine(_crashSlowMoRoutine);
            _crashSlowMoRoutine = null;
        }

        if (_ownsCrashSlowMo)
        {
            TimeScaleHub.End(this);
            _ownsCrashSlowMo = false;
        }
    }

    void Update()
    {
        // Accumulate planar distance (XZ) while the car exists
        if (carInstance != null)
        {

            if (distanceSystem != null)
            {
                runDistanceMeters = distanceSystem.DistanceAlongTrack; // live forward progress
            }
            else
            {
                // Fallback: if distanceSystem missing, keep previous behavior of simple origin distance (not cumulative)
                // This avoids awarding inflated distance due to zig-zag motion.
                // NOTE: spawn position captured in HandleTrackGenerated when carInstance is created.
                // If needed you could store a _spawnPos; for now we just leave runDistanceMeters unchanged when no meter.
            }


        }

        if (carController == null)
            return;

        // Finalize run only when out of fuel AND forward/overall speed is tiny
        if (!_currencyAwarded && carController.IsOutOfFuel)
        {
            float forwardSpeed = 0f;
            float speed = 0f;
            if (_carRb != null)
            {
                speed = _carRb.velocity.magnitude;
                forwardSpeed = Vector3.Dot(_carRb.velocity, carInstance.transform.forward);
            }

            if (Mathf.Abs(forwardSpeed) <= 0.05f && speed <= 0.2f)
            {
                FinalizeRun();
            }
        }

        if (runEnded && Input.GetKeyDown(KeyCode.R))
        {
            RestartRun();
        }
    }







    public void BeginRun()
    {
        if (runStarted && beginRunRoutine != null) return;

        runStarted = true;
        Time.timeScale = 1f;

        var mgr = RacingSkillTreeManager.Instance;
        _startingCurrency = mgr != null ? mgr.Currency : 0;

        // NEW: reset breakdown for this run
        _distanceCoinsThisRun = 0;
        _pickupCoinsThisRun = 0;
        _obstacleCoinsThisRun = 0;

        uiManager?.UpdateRunCoins(0);   // HUD shows 0 to start
        uiManager?.ShowRunCoins();

        if (trackGenerator == null)
            trackGenerator = FindGeneratorAnyState();

        EnsureRefs();
        EnsureTrackCallbacksWired();

        if (beginRunRoutine != null) StopCoroutine(beginRunRoutine);
        beginRunRoutine = StartCoroutine(CoBeginRun());
    }

    private void RestartRun()
    {
        Debug.Log("[GameManager_Racing] Restarting run...");
        Time.timeScale = 1f;
        Scene current = SceneManager.GetActiveScene();
        SceneManager.LoadScene(current.buildIndex);
    }

    private void FinalizeRun()
    {
        if (_currencyAwarded) return;

        int distanceInt = Mathf.RoundToInt(runDistanceMeters);

        // 1) Distance coins
        int distanceCoins = Mathf.RoundToInt(distanceInt * coinsPerDistance);
        _distanceCoinsThisRun = distanceCoins;

        var mgr = RacingSkillTreeManager.Instance;
        int finalTotalCurrency = 0;

        // 2) Award distance coins into the *global* currency pool
        if (mgr != null)
        {
            mgr.AddCurrency(distanceCoins);
            finalTotalCurrency = mgr.Currency;
        }

        // 3) Full breakdown
        int totalCoinsThisRun =
            _distanceCoinsThisRun +
            _pickupCoinsThisRun +
            _obstacleCoinsThisRun;

        // 4) Show breakdown + final total in the UI
        uiManager?.ShowRunComplete(
            distanceInt,
            _distanceCoinsThisRun,
            _pickupCoinsThisRun,
            _obstacleCoinsThisRun,
            totalCoinsThisRun
        );

        _currencyAwarded = true;
        runEnded = true;

        // Pause so player can read
        Time.timeScale = 0f;

        Debug.Log(
            $"[GameManager_Racing] Run complete. Distance={distanceInt} m, " +
            $"DistanceCoins={_distanceCoinsThisRun}, " +
            $"PickupCoins={_pickupCoinsThisRun}, " +
            $"ObstacleCoins={_obstacleCoinsThisRun}, " +
            $"TotalThisRun={totalCoinsThisRun}, " +
            $"FinalTotalCurrency={finalTotalCurrency}. Press R to restart.");
    }


    private IEnumerator CoBeginRun()
    {
        EnsureTrackCallbacksWired();
        trackGenerator.GenerateTrack();

        float t = 0f;
        const float timeout = 6f;

        // Just wait until the track is ready OR we time out.
        // We DO NOT call HandleTrackGenerated here, we rely on the event.
        while (t < timeout && !TrackIsReady(trackGenerator))
        {
            t += Time.deltaTime;
            yield return null;
        }

        if (!TrackIsReady(trackGenerator))
        {
            Debug.LogError("[GameManager_Racing] Track generation timeout. Allowing retry.");
            runStarted = false;
        }

        beginRunRoutine = null;
    }

    public void RegisterCoinPickup(int amount)
    {
        if (!runStarted || runEnded || amount <= 0) return;

        _pickupCoinsThisRun += amount;

        // HUD shows *only* live run coins (pickups + obstacles)
        uiManager?.UpdateRunCoins(_pickupCoinsThisRun + _obstacleCoinsThisRun);
    }

    public void RegisterObstacleReward(int amount)
    {
        if (!runStarted || runEnded || amount <= 0) return;

        _obstacleCoinsThisRun += amount;

        // HUD shows *only* live run coins (pickups + obstacles)
        uiManager?.UpdateRunCoins(_pickupCoinsThisRun + _obstacleCoinsThisRun);
    }

    /// <summary>
    /// Called by CarController when a crash occurs.
    /// impactSpeed is in m/s, severity is 0..1 from CarController.
    /// </summary>
    public void OnCarCrash(float impactSpeed, float severity)
    {
        if (!enabled) return;

        float sev = Mathf.Clamp01(severity);

        // Remap severity through curve if provided
        if (crashSlowMoCurve != null && crashSlowMoCurve.keys.Length > 0)
        {
            sev = Mathf.Clamp01(crashSlowMoCurve.Evaluate(sev));
        }

        // Screen shake (camera)
        if (enableCrashScreenShake && cameraFollow != null)
        {
            float dur = crashShakeDuration * Mathf.Lerp(0.6f, 1.3f, sev);
            float str = crashShakeStrength * Mathf.Lerp(0.5f, 1.6f, sev);
            int vib = Mathf.RoundToInt(crashShakeVibrato * Mathf.Lerp(0.6f, 1.2f, sev));

            cameraFollow.StartShake(dur, str, vib, crashShakeRandomness);
        }

        if (enableCrashSlowMo)
        {
            StartCrashSlowMo(sev);
        }

    }

    private void StartCrashSlowMo(float severity)
    {
        // kill any previous crash slow-mo
        if (_crashSlowMoRoutine != null)
        {
            StopCoroutine(_crashSlowMoRoutine);
            _crashSlowMoRoutine = null;
        }

        if (_ownsCrashSlowMo)
        {
            TimeScaleHub.End(this);
            _ownsCrashSlowMo = false;
        }

        _crashSlowMoRoutine = StartCoroutine(CrashSlowMoRoutine(severity));
    }

    private IEnumerator CrashSlowMoRoutine(float severity)
    {
        _ownsCrashSlowMo = true;

        // Map severity (0–1) through curve → strength multiplier
        float sev = Mathf.Clamp01(severity);
        float curveVal = crashSlowMoCurve != null ? crashSlowMoCurve.Evaluate(sev) : sev;
        float targetScale = Mathf.Lerp(1f, crashSlowMoScale, curveVal);

        float hold = crashSlowMoHold;
        float easeOut = crashSlowMoEaseOut;

        Debug.Log($"[GameManager_Racing] Crash SlowMo → severity={severity:F2}, curveVal={curveVal:F2}, scale={targetScale:F2}, hold={hold:F2}, easeOut={easeOut:F2}");

        // ENTER SLOW-MO
        TimeScaleHub.Begin(this, targetScale, affectFixedDelta: true);

        // Hold using realtime (unaffected by timeScale)
        float holdEnd = Time.realtimeSinceStartup + hold;
        while (Time.realtimeSinceStartup < holdEnd)
            yield return null;

        // Ease-out blend back to normal time
        float start = Time.realtimeSinceStartup;
        float end = start + easeOut;

        while (Time.realtimeSinceStartup < end)
        {
            float t = Mathf.InverseLerp(start, end, Time.realtimeSinceStartup);
            float scale = Mathf.Lerp(targetScale, 1f, t);
            TimeScaleHub.Begin(this, scale, affectFixedDelta: true);
            yield return null;
        }

        // FULL RESTORE
        TimeScaleHub.End(this);
        _ownsCrashSlowMo = false;
        _crashSlowMoRoutine = null;
    }



    private bool TrackIsReady(ProceduralTrackGenerator gen)
    {
        return gen != null && gen.PathPoints != null && gen.PathPoints.Count > 1;
    }

    private void EnsureTrackCallbacksWired()
    {
        if (trackGenerator == null) return;
        trackGenerator.OnTrackGeneratedSuccessfully -= HandleTrackGenerated;
        trackGenerator.OnTrackGeneratedSuccessfully += HandleTrackGenerated;
    }

    private ProceduralTrackGenerator FindGeneratorAnyState()
    {
        var gen = FindObjectOfType<ProceduralTrackGenerator>();
        if (gen != null) return gen;

        var all = Resources.FindObjectsOfTypeAll<ProceduralTrackGenerator>();
        foreach (var g in all)
        {
            if (g != null && g.gameObject.scene.IsValid())
                return g;
        }
        return null;
    }



    private void EnsureRefs()
    {
        if (cameraFollow == null) cameraFollow = FindObjectOfType<CameraFollow>(true);
        if (uiManager == null) uiManager = FindObjectOfType<UIManager_Racing>(true);
        if (distanceSystem == null) distanceSystem = FindObjectOfType<TrackDistanceMeter>(true);
    }

    private void HandleTrackGenerated(ProceduralTrackGenerator gen)
    {
        Vector3 startPos;
        Vector3 startForward;
        gen.GetStartPoint(out startPos, out startForward);

        startForward = new Vector3(startForward.x, 0f, startForward.z).normalized;
        if (startForward.sqrMagnitude < 0.0001f)
            startForward = gen.transform.forward;

        Vector3 spawnPos = startPos
                         + startForward * spawnForwardOffset
                         + Vector3.up * spawnHeightOffset;

        Quaternion spawnRot = Quaternion.LookRotation(startForward, Vector3.up);

        if (carInstance == null)
        {
            carInstance = Instantiate(carPrefab, spawnPos, spawnRot);

        }
        else
            carInstance.transform.SetPositionAndRotation(spawnPos, spawnRot);


        trackCoinSpawner.InitializeForRun(trackGenerator, carInstance.transform);
        trackObstacleSpawner.InitializeForRun(trackGenerator, carInstance.transform);

        Rigidbody rb = carInstance.GetComponent<Rigidbody>();
        if (rb != null)
        {
            rb.velocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
        }

        carController = carInstance.GetComponent<CarController>();
        crossObstacleDirector.SetCar(carController);

        // NEW: reset distance tracking for the new run
        runDistanceMeters = 0f;
        _carRb = carInstance.GetComponent<Rigidbody>();
        _currencyAwarded = false;
        runEnded = false;
        Time.timeScale = 1f;
        uiManager?.HideRunComplete();

        // Re-acquire any missing refs (in case objects were inactive and just got enabled)
        EnsureRefs();

        // Bind distance meter robustly (set generator + car, rebuild path, resubscribe)
        if (distanceSystem != null)
            distanceSystem.Configure(gen, carInstance.transform);

        // Bind camera using API to reset smoothing
        if (cameraFollow != null)
            cameraFollow.SetTarget(carInstance.transform);

        // Bind UI to car (fuel UI, etc.)
        if (uiManager != null && carController != null)
            uiManager.BindCar(carController);

        // Now hide the skill tree
        if (skillTreeRoot != null) skillTreeRoot.SetActive(false);

    }
}