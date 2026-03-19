using System;
using System.Collections;
using Unity.AI.Navigation;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
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
    [SerializeField] private ThrownObstacleDirector thrownObstacleDirector;
    [SerializeField] private TrackFuelSpawner trackFuelSpawner;
    [SerializeField] private TrackHPSpawner trackHPSpawner;
    [SerializeField] private IcePathSpawner icePathSpawner;
    [SerializeField] private NPCTrafficCarSpawner npcCarSpawner;
    [SerializeField] private IcePathScreenFlashDriver iceScreenFlashDriver;
    [SerializeField] private TrackCreatureSpawner creatureSpawner;
    [SerializeField] private TrackEnvironmentSpawner trackEnvironmentSpawner;
    [SerializeField] private TerrainDetailGrassPainter terrainGrassPainter; 

    [Header("NavMesh (for NPC AI)")]
    [Tooltip("Enable runtime NavMesh baking for NPC cars.")]
    [SerializeField] private bool enableNavMeshBaking = true;
    [Tooltip("NavMeshSurface component on the track (auto-created if null).")]
    [SerializeField] private NavMeshSurface navMeshSurface;
    [Tooltip("Agent type ID (0 = Humanoid default, or use custom).")]
    [SerializeField] private int navMeshAgentTypeID = 0;


    [Header("URP Renderer Control")]
    [SerializeField] private UniversalRendererData urpRendererAsset;
    [SerializeField] private string[] enabledRendererFeatures = new string[] { };
    [SerializeField] private string[] disabledRendererFeatures = new string[] { };

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

    private const KeyCode PAD_X = KeyCode.JoystickButton1; // PS5 Cross (X)

    [Header("TEST - Remove when done testing")]
    [Tooltip("Press Start (Options) on PS5 controller to spawn one NPC traffic car ahead.")]
    [SerializeField] private bool enableTestSpawnCar = false;


    [SerializeField, Min(0.05f)]
    private float xHoldSeconds = 0.35f; // tap vs hold threshold (realtime)

    private float _xDownRealtime = -1f;

    private Coroutine _crashSlowMoRoutine;
    private bool _ownsCrashSlowMo;

    [Header("Skill Tree UI (assign the root object that holds RacingSkillUI)")]
    [SerializeField] private GameObject skillTreeRoot;
    [SerializeField] private RacingSkillUI skillTreeUI;

    [Header("Spawn Settings")]
    [SerializeField] private float spawnForwardOffset = 2f;
    [SerializeField] private float spawnHeightOffset = 0.2f;

    [Header("Balancing")]
    [SerializeField, Min(0f)] private float baseCoinsPerMeter = 0.33f; // base coins per meter (now skill-modifiable)

    [Header("Crash Penalties")]
    [Tooltip("Enable currency loss when crashing.")]
    [SerializeField] private bool enableCurrencyLossOnCrash = true;
    [Tooltip("At max severity (1.0) crash, remove this fraction (0..1) of current currency.")]
    [SerializeField, Range(0f, 1f)] private float currencyLossPercentAtSeverity1 = 0.05f;
    [Tooltip("Minimum coins to remove on any crash when you have currency.")]
    [SerializeField] private int minCurrencyLossPerCrash = 2;

    [Header("Explosion Proximity FX")]
    [SerializeField, Range(0f, 3f)] private float explosionShakeBaseDuration = 0.18f;
    [SerializeField, Range(0f, 2f)] private float explosionShakeBaseStrength = 0.45f;
    [SerializeField, Range(0.1f, 4f)] private float explosionShakeDistanceFalloff = 2.0f; // multiplier to radius

    [Header("Explosion PostFX")]
    [SerializeField, Range(0f, 2f)] private float explosionChromaticMultiplier = 1.0f;
     [SerializeField] private float explosionLensMultiplier = 1.0f;

    [Header("Close-Call (Near Miss) FX")]
    [SerializeField, Range(0.01f, 1f)] private float closeCallSlowMoScale = 0.6f;
    [SerializeField, Min(0f)] private float closeCallSlowMoHold = 0.20f;
    [SerializeField, Min(0f)] private float closeCallSlowMoEaseOut = 0.20f;
    [SerializeField, Range(0f, 10f)] private float closeCallChromatic = 0.5f;
    [SerializeField, Range(-100f, 100f)] private float closeCallLens = -12f;
    [SerializeField, Range(0f, 10f)] private float closeCallZoomDeltaFOV = 2f; // how many degrees to zoom in
    [SerializeField, Range(0.05f, 1f)] private float closeCallZoomDuration = 0.25f;

    [Header("Close-Call Audio")]
    [SerializeField, Tooltip("One-shot SFX played when a close-call (near miss) occurs.")]
    private AudioClip closeCallClip;
    [SerializeField, Range(0f, 1f)]
    private float closeCallVolume = 0.9f;

    [Header("Audio Clips")]
    [SerializeField] private AudioClip runCompleteCoinClip;
    [SerializeField] private float runCompleteCoinVolume = 1f;
    [SerializeField] private AudioClip depositCoinsClip;
    [SerializeField] private float depositCoinsVolume = 1f;

    [Header("Run End Timing")]
    [SerializeField, Tooltip("Extra delay after the car is fully stopped before showing the Run Complete screen.")]
    private float runEndSettleDelay = .9f;

    [SerializeField, Min(0f)]
    private float freezeAfterRunCompleteDelayRealtime = 0.75f;

    [SerializeField] private float deathStopShakeMult = 1.35f;
    [SerializeField] private float deathStopSlowMoSeverity = 1f;

    [SerializeField] private float lethalCrashSlowMoHoldMultiplier = 2.25f; // longer hold if crash causes death


    private bool _deathStopBurstPlayed = false;

    // runtime
    private Coroutine _closeCallCR;

    private GameObject carInstance;
    private CarController carController;
    private bool runEnded = false;
    private bool runStarted = false;
    private Coroutine beginRunRoutine;

    private Coroutine _finalizeRunCR;
    private bool _finalizePending;
    private float _stopConditionBeganRealtime;

    private float runDistanceMeters = 0f;
    private Rigidbody _carRb;
    private int _startingCurrency = 0;
    private bool _currencyAwarded = false;

    private int _distanceCoinsThisRun = 0;
    private int _pickupCoinsThisRun = 0;
    private int _obstacleCoinsThisRun = 0;
    private int _sprocketsThisRun;

    private bool _depositSoundPlayed = false;

    public float DistanceAlongTrack => runDistanceMeters;


    public bool RunEnded => runEnded;
    public CarController ActiveCar => carController;

    private const string PREF_KEY_PLAY_DEPOSIT = "GM_PlayDepositOnLoad_v1";

    void Awake()
    {
        QualitySettings.vSyncCount = 0;          // since you said you don’t want to disable vsync, leave this alone if you do want vsync
        Application.targetFrameRate = 120;       // or 60/144 based on preference

        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        ConfigureURPRenderers();

        Instance = this;

        int pendingDeposit = PlayerPrefs.GetInt(PREF_KEY_PLAY_DEPOSIT, 0);
        if (pendingDeposit > 0)
        {
            PlayDepositCoinsSound();
            PlayerPrefs.DeleteKey(PREF_KEY_PLAY_DEPOSIT);
            PlayerPrefs.Save();
            _depositSoundPlayed = true;
        }

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
        // TEST: Start button (PS5 Options) spawns one NPC car. Remove this block when done testing.
        if (enableTestSpawnCar && npcCarSpawner != null &&
            Gamepad.current != null && Gamepad.current.startButton.wasPressedThisFrame)
        {
            npcCarSpawner.SpawnOneNPCCarForTest();
            return;
        }

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

        // Restart can be pressed after run has ended even if car was destroyed; handle it without requiring carController.
        bool canCheckRestart = _finalizePending || runEnded;
        if (canCheckRestart)
        {
            bool notInFlipMash = carController == null || !carController.IsFlipMashActive;
            if (notInFlipMash)
            {
                bool restartPressed = RacingInputReader.Instance != null
                    ? RacingInputReader.Instance.RestartDown
                    : (Input.GetKeyDown(KeyCode.R) || (RestartAllowedNow() && Input.GetKeyDown(PAD_X)));

                if (restartPressed)
                {
                    if (_finalizeRunCR != null) StopCoroutine(_finalizeRunCR);
                    _finalizeRunCR = null;
                    _finalizePending = false;
                    TryPlayDepositSoundOnReset();
                    RestartRun();
                    return;
                }
            }
        }

        if (carController == null)
            return;

        Debug.Log($"FPS ~ {1f / Mathf.Max(0.0001f, Time.unscaledDeltaTime):F0}");

        // Finalize run only when out of fuel AND forward/overall speed is tiny
        if (!_currencyAwarded && (carController.IsOutOfFuel || carController.IsOutOfHP))
        {
            float forwardSpeed = 0f;
            float speed = 0f;

            if (_carRb != null && carInstance != null)
            {
                speed = _carRb.velocity.magnitude;
                forwardSpeed = Vector3.Dot(_carRb.velocity, carInstance.transform.forward);
            }

            bool stopped = Mathf.Abs(forwardSpeed) <= 0.05f && speed <= 0.2f;

            if (stopped)
            {
                if (!_finalizePending)
                {
                    _finalizePending = true;

                    if (!_deathStopBurstPlayed)
                    {
                        _deathStopBurstPlayed = true;

                        // replay the explosion VFX on the stopped wreck
                        carController?.PlayDeathVFXExtra();

                        // screen shake + slowmo for the extra explosion
                        if (enableCrashScreenShake && cameraFollow != null)
                        {
                            cameraFollow.StartShake(
                                crashShakeDuration * deathStopShakeMult,
                                crashShakeStrength * deathStopShakeMult,
                                crashShakeVibrato,
                                crashShakeRandomness
                            );
                        }

                        if (enableCrashSlowMo)
                            StartCrashSlowMo(deathStopSlowMoSeverity);
                    }

                    _stopConditionBeganRealtime = Time.realtimeSinceStartup;

                    if (_finalizeRunCR != null) StopCoroutine(_finalizeRunCR);
                    _finalizeRunCR = StartCoroutine(CoFinalizeRunAfterDelay());
                }
            }
            else
            {
                // If the car moves again (tiny bounce, slope, etc.), cancel the pending finalize.
                _finalizePending = false;
                if (_finalizeRunCR != null)
                {
                    StopCoroutine(_finalizeRunCR);
                    _finalizeRunCR = null;
                }
            }
        }
    }

    private bool XDown() => Input.GetKeyDown(PAD_X);
    private bool XHeld() => Input.GetKey(PAD_X);
    private bool XUp() => Input.GetKeyUp(PAD_X);

    private void TickXHoldTimer()
    {
        if (XDown())
            _xDownRealtime = Time.realtimeSinceStartup;

        // Safety: if something eats the up event, clear stale timer
        if (!XHeld() && _xDownRealtime > 0f && !XUp())
            _xDownRealtime = -1f;
    }

    // New helper: same gating logic used by ReturnToSkillTree for deposit audio, but callable before a scene restart.
    private void TryPlayDepositSoundOnReset()
    {
        // Don't set twice for this run
        if (_depositSoundPlayed) return;
        if (!_currencyAwarded) return; // nothing was awarded this run

        var mgr = RacingSkillTreeManager.Instance;
        if (mgr == null) return;

        int deposited = mgr.Currency - _startingCurrency;
        if (deposited > 0)
        {
            // Persist a flag so the freshly loaded scene can play the sound after reload finishes.
            PlayerPrefs.SetInt(PREF_KEY_PLAY_DEPOSIT, deposited);
            PlayerPrefs.Save();
            _depositSoundPlayed = true;
        }
    }

    public void BeginRun()
    {
        if (runStarted && beginRunRoutine != null) return;

        runStarted = true;
        uiManager?.ShowLoading("Generating track...");
        Time.timeScale = 1f;

        var mgr = RacingSkillTreeManager.Instance;
        _startingCurrency = mgr != null ? mgr.Currency : 0;
        _depositSoundPlayed = false;

        // NEW: reset breakdown for this run
        _distanceCoinsThisRun = 0;
        _pickupCoinsThisRun = 0;
        _obstacleCoinsThisRun = 0;
        _sprocketsThisRun = 0;

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
        // Apply skill chain to base coins-per-meter
        var mgr = RacingSkillTreeManager.Instance;
        float coinsPerMeter = mgr != null ? mgr.GetDistanceCoinsPerMeter(baseCoinsPerMeter) : baseCoinsPerMeter;
        int distanceCoins = Mathf.RoundToInt(distanceInt * coinsPerMeter);
        _distanceCoinsThisRun = distanceCoins;

        // 2) Award distance coins into the *global* currency pool
        int finalTotalCurrency = 0;
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

        int totalSprockets = mgr?.Sprockets ?? 0;

        // 4) Show breakdown + final total in the UI
        uiManager?.ShowRunComplete(
            distanceInt,
            _distanceCoinsThisRun,
            _pickupCoinsThisRun,
            _obstacleCoinsThisRun,
            totalCoinsThisRun,
            _sprocketsThisRun,
            totalSprockets
        );
        PlayRunCompleteCoinSound();

        _currencyAwarded = true;
        runEnded = true;

        NarrativeDirector.NotifyRunCompleted();

        // Pause so player can read
        StartCoroutine(CoFreezeAfterRunComplete());

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
        uiManager?.ShowLoading("Generating track...");
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
            uiManager?.HideLoading();
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

    // Remove from run coins first (obstacles, then pickups). Returns actual removed.
    private int DeductRunCoins(int amount)
    {
        int before = _pickupCoinsThisRun + _obstacleCoinsThisRun;
        int toRemove = Mathf.Clamp(amount, 0, before);

        int takeFromObstacles = Mathf.Min(_obstacleCoinsThisRun, toRemove);
        _obstacleCoinsThisRun -= takeFromObstacles;
        toRemove -= takeFromObstacles;

        int takeFromPickups = Mathf.Min(_pickupCoinsThisRun, toRemove);
        _pickupCoinsThisRun -= takeFromPickups;
        toRemove -= takeFromPickups;

        // Update HUD
        uiManager?.UpdateRunCoins(_pickupCoinsThisRun + _obstacleCoinsThisRun);

        return before - (_pickupCoinsThisRun + _obstacleCoinsThisRun);
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

        // Currency penalties (percentage-based)
        if (enableCurrencyLossOnCrash)
        {
            int runCoins = _pickupCoinsThisRun + _obstacleCoinsThisRun;
            if (runCoins > 0)
            {
                int requested = Mathf.RoundToInt(runCoins * (severity * Mathf.Clamp01(currencyLossPercentAtSeverity1)));
                int minLoss = Mathf.Clamp(minCurrencyLossPerCrash, 0, runCoins);
                int loss = Mathf.Clamp(Mathf.Max(requested, minLoss), 0, runCoins);

                int removed = DeductRunCoins(loss);

                if (removed > 0 && RacingPopups.IsReady && carController != null)
                {
                    Vector3 popupPos = carController.transform.position + Vector3.up * 2f;
                    RacingPopups.CoinLoss(removed, popupPos);
                }

                Debug.Log($"[GameManager_Racing] Crash penalty: lost {removed} run coins (severity={severity:F2}).");
            }
        }
    }

    public void HandleProjectileExplosion(Vector3 explosionPos, float explosionRadius)
    {
        if (!enabled) return;

        // compute distance to the active car
        var activeCar = ActiveCar;
        if (activeCar == null) return;

        Vector3 carPos = activeCar.transform.position;
        float dist = Vector3.Distance(carPos, explosionPos);

        // define effect distance (where effect falls off to zero)
        float effectMaxDist = explosionRadius * Mathf.Max(1f, explosionShakeDistanceFalloff);

        float proximity = 0f;
        if (effectMaxDist > 0f)
            proximity = Mathf.Clamp01(1f - (dist / effectMaxDist)); // 1 = at center, 0 = outside effect

        if (proximity <= 0f) return;

        // Screen shake scaled by proximity
        if (enableCrashScreenShake && cameraFollow != null)
        {
            float dur = Mathf.Lerp(0.05f, explosionShakeBaseDuration, proximity);
            float str = Mathf.Lerp(0.05f, explosionShakeBaseStrength * explosionShakeBaseStrength * 1f, proximity); // small baseline
            int vib = Mathf.RoundToInt(crashShakeVibrato * Mathf.Lerp(0.6f, 1.2f, proximity));
            cameraFollow.StartShake(dur, str, vib, crashShakeRandomness);
        }

        // PostFX burst scaled by proximity
        var postFX = FindObjectOfType<ForcefieldPostFXController>();
        if (postFX != null)
        {
            float chroma = Mathf.Clamp01(explosionChromaticMultiplier * proximity);
            float lens = Mathf.Lerp(0f, explosionLensMultiplier * proximity * 1.0f, proximity);
            float hold = Mathf.Lerp(0.05f, 0.25f, proximity);
            postFX.PlayBurstCustom(chroma, 0f, hold, 0.06f, 0.18f);
        }

        // NEW: trigger a scaled slow-motion based on proximity (reuses crash slow-mo routine)
        if (enableCrashSlowMo && proximity > 0f)
        {
            // Use proximity as severity (0..1) so CrashSlowMoCurve and related settings control feel
            StartCrashSlowMo(proximity);
        }
    }

    // Generic proximity ping for non-explosive projectiles (small visual cue)
    public void HandleProjectileProximity(Vector3 pos, float radius)
    {
        // reuse explosion handler with smaller multipliers (no screen shake if desired)
        HandleProjectileExplosion(pos, radius);
    }

    // Called on a close call (near miss). No screenshake — play slight slow-mo + postfx + camera zoom pulse.
    public void HandleProjectileCloseCall(Vector3 pos, float closestDistance)
    {
        if (!enabled) return;

        var mgr = RacingSkillTreeManager.Instance;

        // === CLOSE CALL COINS (Skill) ===
        if (mgr != null)
        {
            int coins = mgr.GetCloseCallCoins();
            if (coins > 0)
            {
                // Award coins
                mgr.AddCurrency(coins);
                _pickupCoinsThisRun += coins; // Track for run summary

                // Update live UI
                int totalCoins = _distanceCoinsThisRun + _pickupCoinsThisRun + _obstacleCoinsThisRun;
                uiManager?.UpdateRunCoins(totalCoins);

                // Spawn coin popup
                if (RacingPopups.IsReady)
                {
                    Vector3 popupPos = pos + Vector3.up * 2f;
                    RacingPopups.Spawn(RacingPopupType.CoinGain, coins, popupPos);
                }

                Debug.Log($"[CloseCall] Awarded {coins} coins!");
            }
        }

        // === NOTIFY CAR FOR BOOST/INVINCIBILITY ===
        var car = ActiveCar;
        if (car != null)
        {
            car.OnCloseCall(pos, closestDistance);
        }

        // === EXISTING CLOSE CALL EFFECTS ===

        // Spawn popup
        if (RacingPopups.IsReady)
        {
            Vector3 popupPos = pos + Vector3.up * 1.5f;
            RacingPopups.CloseCall(closestDistance, popupPos);
        }

        // Play a small PPS burst (chromatic + lens) centered on event
        var postFX = FindObjectOfType<ForcefieldPostFXController>();
        if (postFX != null)
        {
            float chroma = closeCallChromatic;
            float lens = closeCallLens;
            postFX.PlayBurstCustom(chroma, lens, closeCallSlowMoHold * 0.9f, 0.04f, 0.15f);
        }

        // Play close-call SFX
        if (closeCallClip != null)
        {
            Play2DClip(closeCallClip, closeCallVolume);
        }

        // Start a gentle slow-mo for the close-call
        if (_closeCallCR != null)
        {
            StopCoroutine(_closeCallCR);
            _closeCallCR = null;
        }
        _closeCallCR = StartCoroutine(CloseCallSlowMoRoutine());

        // Camera slight zoom pulse (no shake)
        if (cameraFollow != null && closeCallZoomDeltaFOV > 0f)
        {
            cameraFollow.ZoomPulse(closeCallZoomDeltaFOV, closeCallZoomDuration);
        }
    }

    private IEnumerator CloseCallSlowMoRoutine()
    {
        // Enter slow-mo (realtime safe)
        TimeScaleHub.Begin(this, Mathf.Clamp(closeCallSlowMoScale, 0.05f, 1f), affectFixedDelta: true);

        float holdEnd = Time.realtimeSinceStartup + Mathf.Max(0f, closeCallSlowMoHold);
        while (Time.realtimeSinceStartup < holdEnd)
            yield return null;

        float ease = Mathf.Max(0f, closeCallSlowMoEaseOut);
        float t0 = Time.realtimeSinceStartup;
        float t1 = t0 + ease;
        while (Time.realtimeSinceStartup < t1)
        {
            float t = Mathf.InverseLerp(t0, t1, Time.realtimeSinceStartup);
            float scale = Mathf.Lerp(closeCallSlowMoScale, 1f, t);
            TimeScaleHub.Begin(this, scale, affectFixedDelta: true);
            yield return null;
        }

        TimeScaleHub.End(this);
        _closeCallCR = null;
    }

    private void StartCrashSlowMo(float severity, float holdMultiplier = 1f)
    {
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

        _crashSlowMoRoutine = StartCoroutine(CrashSlowMoRoutine(severity, holdMultiplier));
    }

    private IEnumerator CrashSlowMoRoutine(float severity, float holdMultiplier)
    {
        _ownsCrashSlowMo = true;

        float sev = Mathf.Clamp01(severity);
        float curveVal = crashSlowMoCurve != null ? crashSlowMoCurve.Evaluate(sev) : sev;
        float targetScale = Mathf.Lerp(1f, crashSlowMoScale, curveVal);

        float hold = crashSlowMoHold * Mathf.Max(1f, holdMultiplier);   // <<< ONLY CHANGE THAT MATTERS
        float easeOut = crashSlowMoEaseOut;

        TimeScaleHub.Begin(this, targetScale, affectFixedDelta: true);

        float holdEnd = Time.realtimeSinceStartup + hold;
        while (Time.realtimeSinceStartup < holdEnd)
            yield return null;

        float start = Time.realtimeSinceStartup;
        float end = start + easeOut;

        while (Time.realtimeSinceStartup < end)
        {
            float t = Mathf.InverseLerp(start, end, Time.realtimeSinceStartup);
            float scale = Mathf.Lerp(targetScale, 1f, t);
            TimeScaleHub.Begin(this, scale, affectFixedDelta: true);
            yield return null;
        }

        TimeScaleHub.End(this);
        _ownsCrashSlowMo = false;
        _crashSlowMoRoutine = null;
    }

    private IEnumerator CoFinalizeRunAfterDelay()
    {
        float endTime = _stopConditionBeganRealtime + Mathf.Max(0f, runEndSettleDelay);

        // Wait in REALTIME so slowmo / Time.timeScale does not mess with the delay.
        while (Time.realtimeSinceStartup < endTime)
        {
            // Abort if conditions changed
            if (_currencyAwarded || carController == null || carInstance == null || _carRb == null)
            {
                _finalizePending = false;
                _finalizeRunCR = null;
                yield break;
            }

            // If we started moving again, abort
            float speed = _carRb.velocity.magnitude;
            float forwardSpeed = Vector3.Dot(_carRb.velocity, carInstance.transform.forward);
            bool stopped = Mathf.Abs(forwardSpeed) <= 0.05f && speed <= 0.2f;

            if (!stopped)
            {
                _finalizePending = false;
                _finalizeRunCR = null;
                yield break;
            }

            yield return null;
        }

        _finalizePending = false;
        _finalizeRunCR = null;

        FinalizeRun();
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

    private IEnumerator CoFreezeAfterRunComplete()
    {
        float end = Time.realtimeSinceStartup + Mathf.Max(0f, freezeAfterRunCompleteDelayRealtime);
        while (Time.realtimeSinceStartup < end)
            yield return null;

        // Ensure any crash slowmo controller is not fighting the freeze
        TimeScaleHub.End(this);

        Time.timeScale = 0f;
    }

    /// <summary>
    /// Call when the UI flow returns the player to the skill tree (e.g. "Back to Skill Tree" button).
    /// Plays the deposit sound only if the run awarded currency and the player's bank increased.
    /// This prevents the sound on first app boot or when nothing was deposited.
    /// </summary>
    public void ReturnToSkillTree()
    {
        // Ensure game canvas is on so player can see skill tree and interact (fixes canvas staying off after run/dialogue).
        uiManager?.SetGameCanvasVisible(true);

        // Show the skill tree root (same as existing flow)
        if (skillTreeRoot != null)
            skillTreeRoot.SetActive(true);

        // Hide run UI if present
        uiManager?.HideRunComplete();

        // Only play deposit sound if run awarded currency and we haven't played it yet for this run
        if (_depositSoundPlayed) return;
        if (!_currencyAwarded) return; // nothing was awarded this run

        var mgr = RacingSkillTreeManager.Instance;
        if (mgr == null) return;

        int deposited = mgr.Currency - _startingCurrency;
        if (deposited > 0)
        {
            PlayDepositCoinsSound();
            _depositSoundPlayed = true;
        }
    }

    public void RegisterSprocketGain(int amount)
    {
        if (amount <= 0) return;
        _sprocketsThisRun += amount;

        // Update live UI
        uiManager?.UpdateRunSprockets(_sprocketsThisRun);
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


        RespawnCarAtTrackStart(spawnPos, spawnRot);

        if (terrainGrassPainter != null)
        {
            // coroutine version avoids a nasty spike
            StartCoroutine(terrainGrassPainter.CoPaint(trackGenerator));
        }

        StartCoroutine(CoHideLoadingNextFrame());
    }

    /// <summary>Spawn or reposition the car at the given position/rotation and bind all systems. Used by normal track flow and by TEST spawn hotkey.</summary>
    private void RespawnCarAtTrackStart(Vector3 spawnPos, Quaternion spawnRot)
    {
        if (carPrefab == null) return;

        if (carInstance == null)
            carInstance = Instantiate(carPrefab, spawnPos, spawnRot);
        else
            carInstance.transform.SetPositionAndRotation(spawnPos, spawnRot);

        _deathStopBurstPlayed = false;
        trackCoinSpawner.InitializeForRun(trackGenerator, carInstance.transform);
        trackObstacleSpawner.InitializeForRun(trackGenerator, carInstance.transform);
        trackFuelSpawner.InitializeForRun(trackGenerator, carInstance.transform);
        trackHPSpawner.InitializeForRun(trackGenerator, carInstance.transform);
        icePathSpawner.InitializeForRun(trackGenerator, carInstance.transform);
        npcCarSpawner.InitializeForRun(trackGenerator, carInstance.transform);
        AstarRuntimeBootstrap.Instance?.ScanForTrack(trackGenerator.transform);
        creatureSpawner?.InitializeForRun(trackGenerator, carInstance.transform);
        trackEnvironmentSpawner?.InitializeForRun(trackGenerator, carInstance.transform);

        Rigidbody rb = carInstance.GetComponent<Rigidbody>();
        if (rb != null)
        {
            rb.velocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
        }

        carController = carInstance.GetComponent<CarController>();
        crossObstacleDirector.SetCar(carController);
        thrownObstacleDirector.SetCar(carController);
        iceScreenFlashDriver.SetCarController(carController);

        runDistanceMeters = 0f;
        _carRb = carInstance.GetComponent<Rigidbody>();
        _currencyAwarded = false;
        runEnded = false;
        Time.timeScale = 1f;
        uiManager?.HideRunComplete();

        EnsureRefs();

        if (distanceSystem != null && trackGenerator != null)
            distanceSystem.Configure(trackGenerator, carInstance.transform);

        if (cameraFollow != null)
            cameraFollow.SetTarget(carInstance.transform);

        if (uiManager != null && carController != null)
            uiManager.BindCar(carController);

        if (skillTreeRoot != null)
            skillTreeRoot.SetActive(false);
    }


    private bool RestartAllowedNow()
    {
        // Only allow "Press X to continue" after all slowmo/pause owners are done.
        return Time.timeScale >= 0.99f
            && !TimeScaleHub.IsPaused
            && !TimeScaleHub.IsAnyActive;
    }

    private IEnumerator CoHideLoadingNextFrame()
    {
        // let instantiates + layout rebuilds finish
        yield return null;
        uiManager?.HideLoading();
    }


    private void BakeNavMeshForTrack()
    {
        if (!enableNavMeshBaking) return;

        // NavMeshSurface must be ON the track generator itself (segments are children of it)
        if (navMeshSurface == null)
        {
            // Try to find existing one on the track generator
            navMeshSurface = trackGenerator.GetComponent<NavMeshSurface>();

            // If still null, ADD it to the track generator (not a child!)
            if (navMeshSurface == null)
            {
                navMeshSurface = trackGenerator.gameObject.AddComponent<NavMeshSurface>();
                Debug.Log("[GameManager_Racing] Added NavMeshSurface to track generator");
            }
        }

        // Configure the surface
        navMeshSurface.collectObjects = CollectObjects.Children;  // Bake all child segments
        navMeshSurface.agentTypeID = navMeshAgentTypeID;

        // IMPORTANT: Set the layer mask to only include road
        int roadLayer = LayerMask.NameToLayer("RoadSurface");
        if (roadLayer >= 0)
        {
            navMeshSurface.layerMask = 1 << roadLayer;
        }
        else
        {
            // Fallback: try RoadSurface layer
            int roadSurfaceLayer = LayerMask.NameToLayer("Road");
            if (roadSurfaceLayer >= 0)
                navMeshSurface.layerMask = 1 << roadSurfaceLayer;
        }

        // Bake!
        System.Diagnostics.Stopwatch sw = new System.Diagnostics.Stopwatch();
        sw.Start();

        navMeshSurface.BuildNavMesh();

        sw.Stop();
        Debug.Log($"[GameManager_Racing] NavMesh baked in {sw.ElapsedMilliseconds}ms on {trackGenerator.gameObject.name}");
    }

    public void OnCarCrashLethal(float severity)
    {
        if (!enabled) return;
        if (!enableCrashSlowMo) return;

        float sev = Mathf.Clamp01(severity);

        // match OnCarCrash remap behavior
        if (crashSlowMoCurve != null && crashSlowMoCurve.keys.Length > 0)
            sev = Mathf.Clamp01(crashSlowMoCurve.Evaluate(sev));

        StartCrashSlowMo(sev, lethalCrashSlowMoHoldMultiplier);
    }

    private void PlayRunCompleteCoinSound()
    {
        if (runCompleteCoinClip == null) return;
        Play2DClip(runCompleteCoinClip, runCompleteCoinVolume);
    }
    public void PlayDepositCoinsSound()
    {
        if (depositCoinsClip == null) return;
        Play2DClip(depositCoinsClip, depositCoinsVolume);
    }

    private void Play2DClip(AudioClip clip, float volume = 1f)
    {
        if (clip == null) return;
        GameObject go = new GameObject("SFX_2D_" + clip.name);
        // Attach to camera if available so it's logically grouped in the hierarchy (optional)
        if (mainCam != null) go.transform.SetParent(mainCam.transform, false);

        var src = go.AddComponent<AudioSource>();
        src.clip = clip;
        src.playOnAwake = false;
        src.loop = false;
        src.spatialBlend = 0f; // 2D — no panning
        src.volume = Mathf.Clamp01(volume);
        src.dopplerLevel = 0f;
        src.rolloffMode = AudioRolloffMode.Linear; // irrelevant for 2D but harmless
        src.Play();
        Destroy(go, clip.length / Mathf.Max(0.01f, src.pitch));
    }

    private void ConfigureURPRenderers()
    {
        if (urpRendererAsset == null) return;

        // Disable specified features
        foreach (var featureName in disabledRendererFeatures)
        {
            var feature = urpRendererAsset.rendererFeatures.Find(f => f.name == featureName);
            if (feature != null) feature.SetActive(false);
        }

        // Enable specified features
        foreach (var featureName in enabledRendererFeatures)
        {
            var feature = urpRendererAsset.rendererFeatures.Find(f => f.name == featureName);
            if (feature != null) feature.SetActive(true);
        }
    }

}