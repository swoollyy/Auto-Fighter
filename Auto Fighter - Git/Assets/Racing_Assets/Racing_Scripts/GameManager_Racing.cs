using System;
using System.Collections;
using System.Collections.Generic;
using Unity.AI.Navigation;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Debug = UnityEngine.Debug;

public class GameManager_Racing : MonoBehaviour
{
    public enum GameProgressState
    {
        /// <summary>Initial intro / first-time tutorial sequence.</summary>
        InitIntro = 0,
        /// <summary>Front-end menu for this mode (can be same view as SkillTree for now).</summary>
        MainMenu = 1,
        /// <summary>Skill tree / garage where you configure builds and spend meta-currency.</summary>
        SkillTree = 2,
        /// <summary>Loading and gating while a run scene/track is being prepared.</summary>
        LoadingRun = 3,
        /// <summary>Active incremental racing run (core gameplay loop).</summary>
        InRun = 4,
        /// <summary>Game is paused via pause menu (overlay on top of current context).</summary>
        Paused = 5,
        /// <summary>Post-run breakdown, rewards, and summary.</summary>
        RunEnd = 6,
        /// <summary>Dialogue / cutscene sequences that temporarily own flow.</summary>
        Dialogue = 7
    }

    private enum RunFlowState
    {
        SkillTree = 0,
        Loading = 1,
        InRun = 2,
        RunEnd = 3
    }

    public static GameManager_Racing Instance { get; private set; }

    [Header("References")]
    [SerializeField] private ProceduralTrackGenerator trackGenerator;
    [SerializeField] private GameObject carPrefab;
    [SerializeField] private UIManager_Racing uiManager;
    [SerializeField] private RunStartIntroUI runStartIntro;
    [SerializeField] private TrackDistanceMeter distanceSystem;
    [SerializeField] private TrackCoinSpawner trackCoinSpawner;
    [SerializeField] private TrackObstacleSpawner trackObstacleSpawner;
    [SerializeField] private CrossObstacleDirector crossObstacleDirector;
    [SerializeField] private BounceBackObstacleSpawner bounceBackObstacleSpawner;
    [SerializeField] private ThrownObstacleDirector thrownObstacleDirector;
    [SerializeField] private RollingLogSpawner rollingLogSpawner;
    [SerializeField] private TrackFuelSpawner trackFuelSpawner;
    [SerializeField] private TrackHPSpawner trackHPSpawner;
    [SerializeField] private TrackHelpPatronSpawner helpPatronSpawner;
    [SerializeField] private IcePathSpawner icePathSpawner;
    [SerializeField] private NPCTrafficCarSpawner npcCarSpawner;
    [SerializeField] private IcePathScreenFlashDriver iceScreenFlashDriver;
    [SerializeField] private TrackCreatureSpawner creatureSpawner;
    [SerializeField] private TrackEnvironmentSpawner trackEnvironmentSpawner;
    [SerializeField] private TrackSpawnerQueue trackSpawnerQueue;
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
    private const KeyCode PAD_TRIANGLE = KeyCode.JoystickButton3; // PS Triangle / Xbox Y
    private const KeyCode QUICK_RESTART_KEY = KeyCode.V;

    [Header("TEST - Remove when done testing")]
    [Tooltip("Press Start (Options) on PS5 controller to spawn one NPC traffic car ahead.")]
    [SerializeField] private bool enableTestSpawnCar = false;


    private Coroutine _crashSlowMoRoutine;
    private bool _ownsCrashSlowMo;
    // Separate hub owners so crash and close-call cannot overwrite each other (shared `this` caused flicker).
    private readonly object _crashSlowMoOwner = new object();
    private readonly object _closeCallSlowMoOwner = new object();

    [Header("Skill Tree UI (assign the root object that holds RacingSkillUI)")]
    [SerializeField] private GameObject skillTreeRoot;
    [SerializeField] private RacingSkillUI skillTreeUI;

    [Header("Spawn Settings")]
    [SerializeField] private float spawnForwardOffset = 2f;
    [SerializeField] private float spawnHeightOffset = 0.2f;

    [Header("Balancing")]
    [SerializeField, Min(0f)] private float baseCoinsPerMeter = 0.33f; // base coins per meter (now skill-modifiable)

    [Header("Coin Friend Defaults (from prefab)")]
    [SerializeField, Min(0.1f)] private float coinFriendBaseRange = 30f;
    [SerializeField, Min(0.05f)] private float coinFriendBaseCooldown = 3f;
    [SerializeField, Min(0)] private int coinFriendBaseValueBonus = 0;

    [Header("Crash Penalties")]
    [Tooltip("Enable currency loss when crashing mid-run. Skipped once the car is out of fuel/HP (run ending).")]
    [SerializeField] private bool enableCurrencyLossOnCrash = true;
    [Tooltip("At max severity (1.0) crash, remove this fraction (0..1) of this run's collected coins.")]
    [SerializeField, Range(0f, 1f)] private float currencyLossPercentAtSeverity1 = 0.05f;
    [Tooltip("Minimum coins to remove on any mid-run crash when you have run coins.")]
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

    // Cached post-FX controller used to replay the close-call-style burst for other events (e.g. ice path).
    private ForcefieldPostFXController _closeCallStylePostFX;

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
    [SerializeField, Tooltip("Delay after the run-end mash finishes before the final explosion and results screen.")]
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
    private Coroutine _afterTrackGenCr;
    private Coroutine _finalizeRunCR;
    private Coroutine _flowIrisCr;
    private Coroutine _beginRunIrisCr;
    private Coroutine _restartIrisCr;
    private bool _finalizePending;
    private bool _acceptRunEndContinueInput;
    private RunFlowState _flowState = RunFlowState.SkillTree;

    private float runDistanceMeters = 0f;
    // Furthest normalized road progress (0-1) reached during the current run; fed to DayTrialManager at run end.
    private float _maxRunNormalizedProgress = 0f;
    private Rigidbody _carRb;
    private int _startingCurrency = 0;
    private bool _currencyAwarded = false;

    private int _distanceCoinsThisRun = 0;
    private int _pickupCoinsThisRun = 0;
    private int _obstacleCoinsThisRun = 0;
    private int _sprocketsThisRun;

    private bool _depositSoundPlayed = false;
    private bool _loadingGameplayGateActive = false;
    private bool _audioPausedByLoadingGate = false;
    /// <summary>False until iris engulf finishes and loading UI is actually showing.</summary>
    private bool _beginRunIrisEngulfComplete = true;
    /// <summary>When true, track gen runs without SetSection(Loading) so skill tree/results stay visible under goo.</summary>
    private bool _deferLoadingUiReveal;
    private float _loadingScreenShownAtRealtime = -1f;
    private const float MinLoadingScreenSeconds = 0.45f;
    [Header("Post-Loading Iris")]
    [SerializeField, Min(0.05f), Tooltip("Goo iris open duration after loading (unscaled). Slower than the default close so DAY/static can read through the reveal.")]
    private float postLoadingIrisOpenSeconds = 1.15f;
    private bool _finishPortalSequenceActive = false;
    /// <summary>Set when this run ends via the finish portal (win), not fuel/HP death.</summary>
    private bool _endedViaFinishPortal;
    /// <summary>Trial-complete results: no quick replay — skill tree only.</summary>
    private bool _blockQuickReplayThisRunEnd;
    private GameProgressState _progressState = GameProgressState.InitIntro;
    private GameProgressState _progressStateBeforeDialogue = GameProgressState.SkillTree;
    [Tooltip("Story flag set when the player beats trial 1 (finish portal). Used for post-win skill-tree dialogue.")]
    [SerializeField] private string trial1CompleteStoryFlag = "trial1_complete";
    [Header("Loading Audio Gate")]
    [Tooltip("Music/audio sources allowed to keep playing while loading gate is active (these get ignoreListenerPause enabled during loading).")]
    [SerializeField] private AudioSource[] loadingMusicWhitelist;
    private readonly System.Collections.Generic.Dictionary<AudioSource, bool> _musicIgnorePauseOriginal =
        new System.Collections.Generic.Dictionary<AudioSource, bool>(16);

    [Header("Narrative Progression")]
    [Tooltip("Story flag set by init intro dialogue on completion.")]
    [SerializeField] private string initFinishedStoryFlag = "init_finish";
    [Tooltip("Raised once when entering SkillTree after init is finished. Use this to trigger first-time skill tree narrative.")]
    [SerializeField] private string firstSkillTreeEntryStoryFlag = "skilltree_first_entry";

    [Header("Debug — Start Trial")]
    [Tooltip("ON: on boot, jump to Debug Start Trial Index (0 = first TrialConfig on DayTrialManager). Use to preview each level's track/spawner config.")]
    [SerializeField] private bool debugForceStartTrial = false;
    [Tooltip("0-based index into DayTrialManager → Trials (0 = trial 1 / Track1, 1 = second trial, …).")]
    [SerializeField, Min(0)] private int debugStartTrialIndex = 0;
    [Tooltip("When starting on trial > 0, max every skill from earlier trials' allowlists so the car is fully upgraded for those tracks.")]
    [SerializeField] private bool debugMaxPreviousTrialSkills = true;

    /// <summary>Inspector debug: jump to a later trial on boot (skips opening narrative).</summary>
    public bool IsDebugForceStartTrial => debugForceStartTrial;
    /// <summary>0-based trial index used when <see cref="IsDebugForceStartTrial"/> is on.</summary>
    public int DebugStartTrialIndex => debugStartTrialIndex;

    public float DistanceAlongTrack => runDistanceMeters;
    public bool IsGameplayLive => !_loadingGameplayGateActive && runStarted && !runEnded;


    public bool RunEnded => runEnded;
    public GameProgressState ProgressState => _progressState;
    public CarController ActiveCar => carController;
    public float CoinFriendBaseRange => Mathf.Max(0.1f, coinFriendBaseRange);
    public float CoinFriendBaseCooldown => Mathf.Max(0.05f, coinFriendBaseCooldown);
    public int CoinFriendBaseValueBonus => Mathf.Max(0, coinFriendBaseValueBonus);

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
        SyncCoinFriendDefaultsFromCarPrefab();

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

        // A scene (re)load creates a fresh GameManager. The TimeScaleHub is DontDestroyOnLoad, so any
        // slow-mo/pause owners registered by objects from the PREVIOUS scene remain as stale dictionary
        // entries (their owners were destroyed). Those stale entries make Time.timeScale resolve to the
        // slowest stale scale every recompute, which reads as an ever-stronger, permanent slow-mo across
        // runs and blocks the results screen. Purge everything on spawn so each run starts at full speed.
        TimeScaleHub.ForceClearAll();
        TimeScaleHub.ForceClearAllPauses();
        Time.timeScale = 1f;

        // Scene may not have a DayTrialManager wired yet — create one so days advance on every run.
        DayTrialManager.EnsureExists();
        EnsureNarrativeDirectorActive();

        // Must run in Awake: NarrativeDirector.Start / ReturnToSkillTree can CheckTriggers
        // before the debug trial jump coroutine, which would play Init on Track 2.
        NarrativeDirector.ApplyOpeningNarrativeSkipFromProgress();

        // Before any Start() CheckTriggers: cover dialogue so Trial 1 / Taskmaster / Max Fuel
        // paint under goo. Same reveal as Init → skill tree.
        if (GooIrisScreenTransition.PendingOpenAfterSceneLoad)
            GooIrisScreenTransition.EnsureExists().CoverDialogueForReveal();
    }

    /// <summary>
    /// Narrative Director is sometimes left inactive in the scene; Awake/Start never run then,
    /// so Taskmaster intro and other garage triggers never fire.
    /// </summary>
    private static void EnsureNarrativeDirectorActive()
    {
        var director = FindObjectOfType<NarrativeDirector>(true);
        if (director != null && !director.gameObject.activeInHierarchy)
            director.gameObject.SetActive(true);
    }

    void Start()
    {
        if (trackGenerator == null)
            trackGenerator = FindGeneratorAnyState();

        EnsureRefs();            // ensure camera/ui/distanceSystem exist
        EnsureTrackCallbacksWired();
        EnsureFinishPortalDirector();

        if (skillTreeRoot != null) skillTreeRoot.SetActive(true);
        if (skillTreeUI == null) skillTreeUI = FindObjectOfType<RacingSkillUI>();
        if (skillTreeUI != null) skillTreeUI.BindGameManager(this);

        WireNarrativeEvents();
        GooIrisScreenTransition.EnsureExists();
        DayTrialManager.EnsureExists();
        // After DayTrial.Start (and any same-frame EnsureExists Start) so LoadState can't overwrite the jump.
        StartCoroutine(CoApplyDebugStartTrialIfNeeded());

        bool pendingIrisOpen = GooIrisScreenTransition.PendingOpenAfterSceneLoad;
        if (pendingIrisOpen)
            GooIrisScreenTransition.EnsureExists().CoverDialogueForReveal();

        if (DialogueManager.Instance != null && DialogueManager.Instance.IsPlaying)
        {
            // Dialogue may already be running (NarrativeDirector.Start can beat this Start).
            // Treat it as Dialogue so completion reliably returns to the skill tree / canvas.
            SetProgressState(GameProgressState.Dialogue);
        }
        else
        {
            // No intro this boot (skipped narrative, or already seen this session after a run restart).
            // UIManager used to leave the game canvas off waiting for dialogue end — show skill tree now.
            // After Cross reload, defer narrative until iris opens so garage/CheckTriggers aren't mid-black.
            ReturnToSkillTree(
                raiseFirstSkillTreeEntryFlag: !pendingIrisOpen,
                notifySkillTreeNarrative: !pendingIrisOpen);
        }

        // Safety: if another Start() order still left us without dialogue and without a canvas,
        // recover on the next frame.
        StartCoroutine(CoEnsureSkillTreeVisibleIfNoDialogue());
        StartCoroutine(CoOpenIrisAfterSceneLoadIfPending());
    }

    private IEnumerator CoApplyDebugStartTrialIfNeeded()
    {
        // DayTrial.Start may still run later this frame if EnsureExists just created it.
        yield return null;
        ApplyDebugStartTrialIfNeeded();
    }

    private void ApplyDebugStartTrialIfNeeded()
    {
        if (!debugForceStartTrial) return;

        var day = DayTrialManager.Instance ?? DayTrialManager.EnsureExists();
        if (day == null)
        {
            Debug.LogWarning("[GameManager] Debug start trial: DayTrialManager missing.");
            return;
        }

        day.DebugJumpToTrial(debugStartTrialIndex, debugMaxPreviousTrialSkills);
    }

    private IEnumerator CoOpenIrisAfterSceneLoadIfPending()
    {
        var iris = GooIrisScreenTransition.EnsureExists();
        if (!GooIrisScreenTransition.PendingOpenAfterSceneLoad && !iris.IsSealed && !iris.IsBlockingScreen())
            yield break;

        // One garage reveal: cover dialogue, paint skill tree + CheckTriggers under goo, open onto it.
        iris.CoverDialogueForReveal();
        uiManager?.HideLoading();
        if (skillTreeRoot != null)
            skillTreeRoot.SetActive(true);
        uiManager?.SetGameCanvasVisible(true);
        uiManager?.SetSection(UIManager_Racing.UISection.SkillTree);

        TryRaiseFirstSkillTreeEntryNarrativeFlag();
        HelpPatronProgress.PrepareForTriggerCheck();
        NarrativeDirector.NotifyReturnedToSkillTree();

        yield return null;
        yield return null;

        yield return iris.CoOpenRevealingDialogue();
    }

    private System.Collections.IEnumerator CoEnsureSkillTreeVisibleIfNoDialogue()
    {
        yield return null;
        if (DialogueManager.Instance != null && DialogueManager.Instance.IsPlaying)
            yield break;
        if (_progressState == GameProgressState.InRun ||
            _progressState == GameProgressState.LoadingRun ||
            _progressState == GameProgressState.RunEnd)
            yield break;

        uiManager?.SetGameCanvasVisible(true);
        uiManager?.SetSection(UIManager_Racing.UISection.SkillTree);
        if (_progressState != GameProgressState.SkillTree &&
            _progressState != GameProgressState.MainMenu &&
            _progressState != GameProgressState.Paused)
        {
            SetProgressState(GameProgressState.SkillTree);
            _flowState = RunFlowState.SkillTree;
        }
    }

    private void SyncCoinFriendDefaultsFromCarPrefab()
    {
        if (carPrefab == null) return;
        var friend = carPrefab.GetComponentInChildren<CoinCollectingFriend>(true);
        if (friend == null) return;

        coinFriendBaseRange = friend.AuthoredBaseRange;
        coinFriendBaseCooldown = friend.AuthoredBaseCooldown;
    }

    void OnDestroy()
    {
        UnwireNarrativeEvents();
        if (trackGenerator != null)
            trackGenerator.OnTrackGeneratedSuccessfully -= OnTrackGeneratedDeferSpawn;

        if (_afterTrackGenCr != null)
        {
            StopCoroutine(_afterTrackGenCr);
            _afterTrackGenCr = null;
        }

        EndFinishPortalPresentation();
        FindObjectOfType<ForcefieldPostFXController>(true)?.ResetAllEffectsImmediate();
    }

    private void OnDisable()
    {
        ExitLoadingGameplayGate();

        // Release ALL slow-mo this manager may own (crash and close-call use separate owner tokens).
        if (_crashSlowMoRoutine != null)
        {
            StopCoroutine(_crashSlowMoRoutine);
            _crashSlowMoRoutine = null;
        }
        if (_closeCallCR != null)
        {
            StopCoroutine(_closeCallCR);
            _closeCallCR = null;
        }
        _ownsCrashSlowMo = false;
        TimeScaleHub.End(_crashSlowMoOwner);
        TimeScaleHub.End(_closeCallSlowMoOwner);
        TimeScaleHub.End(this); // legacy cleanup if anything still keyed to the manager instance

        EndFinishPortalPresentation();
        FindObjectOfType<ForcefieldPostFXController>(true)?.ResetAllEffectsImmediate();
    }

    void Update()
    {
        SyncFlowStateFromUISection();

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
                // Track the furthest progress reached this run (the car can be knocked back, so keep the max).
                if (distanceSystem.Normalized > _maxRunNormalizedProgress)
                    _maxRunNormalizedProgress = distanceSystem.Normalized;
            }
            else
            {
                // Fallback: if distanceSystem missing, keep previous behavior of simple origin distance (not cumulative)
                // This avoids awarding inflated distance due to zig-zag motion.
                // NOTE: spawn position captured in HandleTrackGenerated when carInstance is created.
                // If needed you could store a _spawnPos; for now we just leave runDistanceMeters unchanged when no meter.
            }
        }

        // Run-end results panel:
        // - Triangle / V  → quick replay (same as pressing Play; skip skill tree)
        // - Cross / X     → return to skill tree
        bool uiIsRunEnd = uiManager != null && uiManager.CurrentSection == UIManager_Racing.UISection.RunEnd;
        bool canCheckRunEndInput =
            _flowState == RunFlowState.RunEnd &&
            uiIsRunEnd &&
            _acceptRunEndContinueInput &&
            (_finalizePending || runEnded) &&
            !runStarted &&
            !_loadingGameplayGateActive;
        if (canCheckRunEndInput)
        {
            bool notInFlipMash = carController == null || !carController.IsFlipMashActive;
            if (notInFlipMash && RestartAllowedNow())
            {
                if (!_blockQuickReplayThisRunEnd && WasQuickReplayPressed())
                {
                    if (_finalizeRunCR != null) StopCoroutine(_finalizeRunCR);
                    _finalizeRunCR = null;
                    _finalizePending = false;
                    QuickReplayFromRunEnd();
                    return;
                }

                if (WasReturnToSkillTreePressed())
                {
                    if (_finalizeRunCR != null) StopCoroutine(_finalizeRunCR);
                    _finalizeRunCR = null;
                    _finalizePending = false;
                    // Full scene reload — same as before: leaves the run world and boots the
                    // normal skill-tree screen (not an overlay on top of the live track).
                    TryPlayDepositSoundOnReset();
                    RestartRun();
                    return;
                }
            }
        }

        if (carController == null)
            return;

        Debug.Log($"FPS ~ {1f / Mathf.Max(0.0001f, Time.unscaledDeltaTime):F0}");

        // Finalize run only when out of fuel/HP AND planar linear speed is tiny (rotation alone does not block results).
        // Finish-portal sequence owns run-end when the player reaches the track end.
        if (!_currencyAwarded
            && !_finishPortalSequenceActive
            && (carController.IsOutOfFuel || carController.IsOutOfHP))
        {
            bool stopped = carController.IsStoppedForRunEnd;

            if (stopped)
            {
                if (!_finalizePending)
                {
                    _finalizePending = true;

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
        // Back-compat — same as later-run skill-tree Play.
        BeginRunFromSkillTree();
    }

    /// <summary>
    /// Later runs: skill tree stays visible while goo closes. Loading UI + track gen start
    /// only after the iris is fully sealed (avoids lag during the close animation).
    /// </summary>
    public void BeginRunFromSkillTree()
    {
        if (runStarted || _loadingGameplayGateActive || beginRunRoutine != null || _afterTrackGenCr != null)
            return;
        if (_beginRunIrisCr != null)
            return;

        _beginRunIrisCr = StartCoroutine(CoBeginRunFromSkillTree());
    }

    /// <summary>
    /// First run: controls overlay was dismissed. Continue iris ~50%→seal and start loading.
    /// Never forces the skill tree canvas back on (that caused the garage flash in the hole).
    /// </summary>
    public void BeginRunAfterControlsDismissed()
    {
        if (runStarted || _loadingGameplayGateActive || beginRunRoutine != null || _afterTrackGenCr != null)
            return;
        if (_beginRunIrisCr != null)
            return;

        _beginRunIrisCr = StartCoroutine(CoBeginRunAfterControls());
    }

    private IEnumerator CoBeginRunFromSkillTree()
    {
        var iris = GooIrisScreenTransition.EnsureExists();
        _beginRunIrisEngulfComplete = false;
        _loadingScreenShownAtRealtime = -1f;
        _deferLoadingUiReveal = true;

        GameplayUIInputGuard.IsTutorialHighlightActive = true;

        // Hard-pin skill tree for the entire goo close — SetSection(Loading) is ignored until unlock.
        if (skillTreeRoot != null && !skillTreeRoot.activeSelf)
            skillTreeRoot.SetActive(true);
        uiManager?.SetGameCanvasVisible(true);
        uiManager?.LockSection(UIManager_Racing.UISection.SkillTree);

        // SkillTree root Image is authored translucent (~0.72). Keep it opaque while gen runs
        // under the iris so the hole never reads as empty void.
        Image skillTreeBg = skillTreeRoot != null ? skillTreeRoot.GetComponent<Image>() : null;
        Color skillTreeBgColor = default;
        float skillTreeBgAlpha = -1f;
        if (skillTreeBg != null)
        {
            skillTreeBgColor = skillTreeBg.color;
            skillTreeBgAlpha = skillTreeBgColor.a;
            skillTreeBgColor.a = 1f;
            skillTreeBg.color = skillTreeBgColor;
        }

        iris.EnsureReadyToCloseOverScreen();
        Coroutine closeCr = iris.StartCoroutine(iris.CoCloseAndHold());

        float sealWaitUntil = Time.unscaledTime + 3f;
        while (!iris.IsSealed && Time.unscaledTime < sealWaitUntil)
        {
            if (skillTreeRoot != null && !skillTreeRoot.activeSelf)
                skillTreeRoot.SetActive(true);
            yield return null;
        }
        if (closeCr != null && !iris.IsSealed)
            yield return closeCr;
        if (!iris.IsSealed)
            iris.SnapSealed();

        // Fully sealed — show loading, then start heavy track gen (not during goo close).
        RevealLoadingUi();
        iris.HideVisualKeepSealed();

        if (skillTreeBg != null && skillTreeBgAlpha >= 0f)
        {
            skillTreeBgColor.a = skillTreeBgAlpha;
            skillTreeBg.color = skillTreeBgColor;
        }

        _loadingScreenShownAtRealtime = Time.realtimeSinceStartup;
        _beginRunIrisEngulfComplete = true;
        GameplayUIInputGuard.IsTutorialHighlightActive = false;

        BeginRunCore(revealLoadingUi: false);
        _deferLoadingUiReveal = false; // already revealed above; don't re-defer progress UI
        _beginRunIrisCr = null;
    }

    private IEnumerator CoBeginRunAfterControls()
    {
        var iris = GooIrisScreenTransition.EnsureExists();
        _beginRunIrisEngulfComplete = false;
        _loadingScreenShownAtRealtime = -1f;
        _deferLoadingUiReveal = true;

        GameplayUIInputGuard.IsTutorialHighlightActive = true;

        // Iris already ~50% from controls. Do NOT SetSection(SkillTree) — that flashes the garage.
        // CONTROLS stays opaque behind the goo; closing the hole swallows the text.
        iris.RestoreDefaultSortOrder();
        Coroutine closeCr = iris.StartCoroutine(iris.CoCloseAndHold(restoreDefaultSort: false));

        if (closeCr != null)
            yield return closeCr;
        if (!iris.IsSealed)
            iris.SnapSealed();

        // Safe to drop CONTROLS now — screen is fully sealed black.
        FirstRunControlsOverlay.DestroyIfPresent();

        RevealLoadingUi();
        iris.HideVisualKeepSealed();

        _loadingScreenShownAtRealtime = Time.realtimeSinceStartup;
        _beginRunIrisEngulfComplete = true;
        GameplayUIInputGuard.IsTutorialHighlightActive = false;

        BeginRunCore(revealLoadingUi: false);
        _deferLoadingUiReveal = false;
        _beginRunIrisCr = null;
    }

    private void BeginRunCore(bool revealLoadingUi)
    {
        // Full new-run reset (skill-tree Play and quick-replay share this path).
        runEnded = false;
        _finalizePending = false;
        _finishPortalSequenceActive = false;
        _endedViaFinishPortal = false;
        _blockQuickReplayThisRunEnd = false;
        _acceptRunEndContinueInput = false;
        _currencyAwarded = false;
        _deathStopBurstPlayed = false;
        runDistanceMeters = 0f;
        _maxRunNormalizedProgress = 0f;
        _flowState = RunFlowState.Loading;
        SetProgressState(GameProgressState.LoadingRun);
        // Iris paths call RevealLoadingUi first (defer=false). Do not flip defer back on here —
        // that left later-run progress updates writing to an inactive Loading_PANEL if anything
        // stole the section between reveal and this call.
        if (revealLoadingUi)
            _deferLoadingUiReveal = false;
        if (_finalizeRunCR != null)
        {
            StopCoroutine(_finalizeRunCR);
            _finalizeRunCR = null;
        }
        if (_afterTrackGenCr != null)
        {
            StopCoroutine(_afterTrackGenCr);
            _afterTrackGenCr = null;
        }
        runStarted = true;
        EnterLoadingGameplayGate();
        EnsureFinishPortalDirector();
        EndFinishPortalPresentation();
        FindObjectOfType<ForcefieldPostFXController>(true)?.ResetAllEffectsImmediate();
        (runStartIntro != null ? runStartIntro : FindObjectOfType<RunStartIntroUI>(true))?.PrepareForNewRun();

        if (revealLoadingUi || !_deferLoadingUiReveal)
        {
            uiManager?.ShowLoading("Generating track...", 0.05f);
            uiManager?.UpdateRunCoins(0);
            uiManager?.ShowRunCoins();
            uiManager?.UpdateRunSprockets(0);
        }
        else
        {
            // Prep loading text only — never SetSection(Loading) or HideTotalCoins while skill tree is up.
            uiManager?.SetLoadingState("Generating track...", 0.05f);
        }

        var mgr = RacingSkillTreeManager.Instance;
        _startingCurrency = mgr != null ? mgr.Currency : 0;
        _depositSoundPlayed = false;

        _distanceCoinsThisRun = 0;
        _pickupCoinsThisRun = 0;
        _obstacleCoinsThisRun = 0;
        _sprocketsThisRun = 0;

        if (trackGenerator == null)
            trackGenerator = FindGeneratorAnyState();

        EnsureRefs();
        EnsureTrackCallbacksWired();

        if (beginRunRoutine != null) StopCoroutine(beginRunRoutine);
        beginRunRoutine = StartCoroutine(CoBeginRun());
    }

    private void RevealLoadingUi()
    {
        _deferLoadingUiReveal = false;
        // ShowLoading clears the section lock and swaps to the loading panel.
        uiManager?.UnlockSection();
        // Hard-kill skill tree / results so they cannot stay drawn over Loading_PANEL
        // (Loading is the first Canvas sibling and draws underneath otherwise).
        if (skillTreeRoot != null && skillTreeRoot.activeSelf)
            skillTreeRoot.SetActive(false);
        uiManager?.HideRunComplete();
        uiManager?.ShowLoading("Generating track...", 0.05f);
        uiManager?.UpdateRunCoins(0);
        uiManager?.ShowRunCoins();
        uiManager?.UpdateRunSprockets(0);
    }

    private void ApplyLoadingUiState(string message, float progress01)
    {
        // NEVER call ShowLoading while deferred — that SetSection(Loading) kills the skill tree under goo.
        // Once revealed (or BeginRunCore(reveal:true)), keep the panel alive if anything stole the section.
        if (!_deferLoadingUiReveal)
        {
            if (uiManager == null || uiManager.CurrentSection != UIManager_Racing.UISection.Loading)
            {
                if (skillTreeRoot != null && skillTreeRoot.activeSelf)
                    skillTreeRoot.SetActive(false);
                uiManager?.UnlockSection();
                uiManager?.ShowLoading(message, progress01);
                return;
            }
        }
        uiManager?.SetLoadingState(message, progress01);
    }

    private void RestartRun()
    {
        if (_restartIrisCr != null)
            return;
        _restartIrisCr = StartCoroutine(CoRestartRunWithIris());
    }

    private IEnumerator CoRestartRunWithIris()
    {
        Debug.Log("[GameManager_Racing] Restarting run (iris close → reload)...");
        var iris = GooIrisScreenTransition.EnsureExists();
        // Run on the DDOL iris so close state can't be abandoned mid-busy when this GM dies.
        yield return iris.StartCoroutine(iris.CoCloseAndHold());
        GooIrisScreenTransition.PendingOpenAfterSceneLoad = true;
        iris.CoverDialogueForReveal();

        // Do NOT reset Vintage TV here — results are still on screen. Scene reload +
        // VintageTVController.Awake restores defaults once the skill tree is up.
        TimeScaleHub.ForceClearAll();
        TimeScaleHub.ForceClearAllPauses();
        Time.timeScale = 1f;
        Scene current = SceneManager.GetActiveScene();
        SceneManager.LoadScene(current.buildIndex);
        _restartIrisCr = null;
    }

    /// <summary>
    /// Results panel Triangle / V → same as skill-tree Play: loading, new track, new day.
    /// Does not open the skill tree.
    /// </summary>
    private void QuickReplayFromRunEnd()
    {
        if (runStarted || _loadingGameplayGateActive || beginRunRoutine != null || _afterTrackGenCr != null)
            return;
        if (_beginRunIrisCr != null)
            return;

        Debug.Log("[GameManager_Racing] Quick replay from run-end (Play again, skip skill tree).");
        _beginRunIrisCr = StartCoroutine(CoQuickReplayWithIris());
    }

    private IEnumerator CoQuickReplayWithIris()
    {
        var iris = GooIrisScreenTransition.EnsureExists();
        _beginRunIrisEngulfComplete = false;
        _loadingScreenShownAtRealtime = -1f;
        _deferLoadingUiReveal = true;

        PlayDepositSoundImmediateIfNeeded();

        CancelAllTransientSlowMo();
        TimeScaleHub.ForceClearAll();
        TimeScaleHub.ForceClearAllPauses();
        Time.timeScale = 1f;

        _acceptRunEndContinueInput = false;
        runEnded = false;

        EndFinishPortalPresentation();
        FindObjectOfType<VintageTVController>(true)?.ResetVisualToDefaults();

        if (carInstance != null)
        {
            cameraFollow?.SetTarget(null);
            Destroy(carInstance);
            carInstance = null;
            carController = null;
            _carRb = null;
        }

        // Engulf results; start track gen only after seal so goo close stays smooth.
        GameplayUIInputGuard.IsTutorialHighlightActive = true;
        uiManager?.SetGameCanvasVisible(true);
        uiManager?.LockSection(UIManager_Racing.UISection.RunEnd);
        iris.EnsureReadyToCloseOverScreen();
        Coroutine closeCr = iris.StartCoroutine(iris.CoCloseAndHold());

        if (closeCr != null)
            yield return closeCr;
        if (!iris.IsSealed)
            iris.SnapSealed();

        uiManager?.HideRunComplete();
        RevealLoadingUi();
        iris.HideVisualKeepSealed();

        _loadingScreenShownAtRealtime = Time.realtimeSinceStartup;
        _beginRunIrisEngulfComplete = true;
        GameplayUIInputGuard.IsTutorialHighlightActive = false;

        BeginRunCore(revealLoadingUi: false);
        _deferLoadingUiReveal = false;
        _beginRunIrisCr = null;
    }

    private void PlayDepositSoundImmediateIfNeeded()
    {
        if (_depositSoundPlayed) return;
        if (!_currencyAwarded) return;

        var mgr = RacingSkillTreeManager.Instance;
        if (mgr == null) return;

        int deposited = mgr.Currency - _startingCurrency;
        if (deposited <= 0) return;

        PlayDepositCoinsSound();
        _depositSoundPlayed = true;
    }

    /// <summary>
    /// Called by <see cref="FinishPortalDirector"/> when the player enters the end portal / hits ~100%.
    /// Prevents the fuel/HP finalize path from racing the cinematic.
    /// </summary>
    public bool TryBeginFinishPortalSequence()
    {
        if (_currencyAwarded || runEnded || _finishPortalSequenceActive)
            return false;
        if (!runStarted || _loadingGameplayGateActive)
            return false;

        _finishPortalSequenceActive = true;
        _finalizePending = false;
        if (_finalizeRunCR != null)
        {
            StopCoroutine(_finalizeRunCR);
            _finalizeRunCR = null;
        }
        return true;
    }

    /// <summary>
    /// Ends the run after the finish-portal hyper-tunnel intro (counts as full track progress).
    /// Win path: same stats / run-complete UI as a normal end — no vintage TV death burst or color flip.
    /// </summary>
    /// <param name="keepTunnelPresentation">If true, leave hyper-tunnel + forced FOV running under the results UI.</param>
    public void CompleteRunFromFinishPortal(bool keepTunnelPresentation = true)
    {
        _maxRunNormalizedProgress = Mathf.Max(_maxRunNormalizedProgress, 1f);
        _finishPortalSequenceActive = false;
        _endedViaFinishPortal = true;

        // Results UI owns the canvas again (stats panel).
        if (uiManager != null)
            uiManager.SetGameCanvasVisible(true);

        if (!keepTunnelPresentation)
        {
            FinishPortalDirector.Instance?.StopPresentation();
        }
        else
        {
            // Keep tunnel FOV/shake; do not call EndForcedFov here.
        }

        // Intentionally skip PlayDeathStopBurst / VintageTV color flip — this is a win, not a death.
        FinalizeRun();
    }

    public void EndFinishPortalPresentation()
    {
        FinishPortalDirector.Instance?.StopPresentation();
    }

    private void EnsureFinishPortalDirector()
    {
        if (FinishPortalDirector.Instance != null)
            return;
        if (FindObjectOfType<FinishPortalDirector>(true) != null)
            return;

        Debug.LogWarning(
            "[GameManager_Racing] No FinishPortalDirector in the scene. " +
            "Run Racing → Setup Finish Portal System In Open Scene (do not create at runtime).");
    }

    private void FinalizeRun()
    {
        if (_currencyAwarded) return;
        _finishPortalSequenceActive = false;

        // Forcefield quest progression: count this run only if the player did NOT die from HP.
        // Fuel death / normal completion still counts.
        bool diedFromHp = carController != null && carController.IsOutOfHP;
        RacingQuestUnlockManager.Instance?.RecordForcefieldEligibleRunCompletion(diedFromHp);

        int distanceInt = Mathf.RoundToInt(runDistanceMeters);

        // 1) Distance coins
        // Apply skill chain to base coins-per-meter
        var mgr = RacingSkillTreeManager.Instance;
        float coinsPerMeter = mgr != null ? mgr.GetDistanceCoinsPerMeter(baseCoinsPerMeter) : baseCoinsPerMeter;
        int distanceCoins = Mathf.RoundToInt(distanceInt * coinsPerMeter);
        _distanceCoinsThisRun = distanceCoins;

        // 2) Deposit all run earnings into the wallet at once (pickups/obstacles were tracked, not deposited mid-run).
        int totalCoinsThisRun =
            _distanceCoinsThisRun +
            _pickupCoinsThisRun +
            _obstacleCoinsThisRun;

        int finalTotalCurrency = _startingCurrency;
        if (mgr != null && totalCoinsThisRun > 0)
        {
            mgr.AddCurrency(totalCoinsThisRun);
            finalTotalCurrency = mgr.Currency;
        }
        else if (mgr != null)
        {
            finalTotalCurrency = mgr.Currency;
        }

        // Predict trial-1 win presentation before DayTrial advances (title / no quick-restart).
        var day = DayTrialManager.Instance;
        bool beatTrial1ViaPortal = false;
        if (_endedViaFinishPortal && day != null && !day.AllTrialsCompleted
            && day.CurrentTrialIndex == 0 && day.CurrentConfig != null
            && _maxRunNormalizedProgress >= day.CurrentConfig.targetProgress)
        {
            beatTrial1ViaPortal = true;
            _blockQuickReplayThisRunEnd = true;
        }

        string resultsTitle = beatTrial1ViaPortal ? "Trial Complete" : null;
        string resultsHint = beatTrial1ViaPortal
            ? "LMB / X: Skill Tree"
            : null;

        // 3) Show breakdown + wallet total (matches skill tree) and run-only sprockets
        uiManager?.ShowRunComplete(
            distanceInt,
            _distanceCoinsThisRun,
            _pickupCoinsThisRun,
            _obstacleCoinsThisRun,
            totalCoinsThisRun,
            finalTotalCurrency,
            _sprocketsThisRun,
            resultsTitle,
            resultsHint);
        uiManager?.SetSection(UIManager_Racing.UISection.RunEnd);
        PlayRunCompleteCoinSound();

        _currencyAwarded = true;
        runStarted = false;
        runEnded = true;
        _acceptRunEndContinueInput = true;
        _flowState = RunFlowState.RunEnd;
        SetProgressState(GameProgressState.RunEnd);

        NarrativeDirector.NotifyRunCompleted();

        // Pause so player can read
        StartCoroutine(CoFreezeAfterRunComplete());

        Debug.Log(
            $"[GameManager_Racing] Run complete. Distance={distanceInt} m, " +
            $"DistanceCoins={_distanceCoinsThisRun}, " +
            $"PickupCoins={_pickupCoinsThisRun}, " +
            $"ObstacleCoins={_obstacleCoinsThisRun}, " +
            $"TotalThisRun={totalCoinsThisRun}, " +
            $"FinalTotalCurrency={finalTotalCurrency}. " +
            (beatTrial1ViaPortal
                ? "Trial Complete — LMB/X=skill tree (quick restart disabled)."
                : "LMB/X=skill tree, RMB/V/Triangle=play again."));

        // Day / Trial progression: this completed run counts as one day. Pass/advance, tick a day,
        // or (on the last allowed day without reaching the target) fail and revert to the trial baseline.
        // Done last so any baseline restore (which reverts coins/sprockets/skills) happens after rewards
        // are tallied for this run.
        DayTrialManager.Instance?.NotifyRunCompleted(_maxRunNormalizedProgress);

        if (beatTrial1ViaPortal)
            NarrativeDirector.MarkTrial1Complete();
    }


    private IEnumerator CoBeginRun()
    {
        EnsureTrackCallbacksWired();
        ApplyLoadingUiState("Generating track...", 0.05f);
        // Apply the active trial's track settings before generation so BuildTrack reads them.
        DayTrialManager.Instance?.ApplyCurrentTrialToTrack(trackGenerator);

        IEnumerator genCo = trackGenerator.GenerateTrackCo();
        while (genCo.MoveNext())
        {
            int attempts = trackGenerator.LastGenerationAttempts;
            string status = trackGenerator.GenerationStatusMessage;
            if (string.IsNullOrEmpty(status))
                status = attempts > 0 ? $"Generating track (attempt {attempts})..." : "Generating track...";
            float progress = Mathf.Clamp(0.05f + attempts * 0.003f, 0.05f, 0.52f);
            ApplyLoadingUiState(status, progress);
            yield return genCo.Current;
        }

        if (!trackGenerator.LastGenerateSucceeded)
        {
            Debug.LogError("[GameManager_Racing] Track generation failed after retries.");
            _deferLoadingUiReveal = false;
            _beginRunIrisEngulfComplete = true;
            GameplayUIInputGuard.IsTutorialHighlightActive = false;
            FirstRunControlsOverlay.DestroyIfPresent();
            if (_beginRunIrisCr != null)
            {
                StopCoroutine(_beginRunIrisCr);
                _beginRunIrisCr = null;
            }
            var iris = GooIrisScreenTransition.EnsureExists();
            iris.SnapOpenHidden();
            uiManager?.HideLoading();
            uiManager?.UnlockSection();
            if (skillTreeRoot != null)
                skillTreeRoot.SetActive(true);
            uiManager?.SetSection(UIManager_Racing.UISection.SkillTree);
            ExitLoadingGameplayGate();
            carController?.SetExternalInputLock(true);
            runStarted = false;
            _flowState = RunFlowState.SkillTree;
        }
        else
        {
            // Terrain sculpt already finished inside GenerateTrackCo. Post-gen (grass/spawn)
            // continues in OnTrackGeneratedDeferSpawn — don't leave a sticky "Finalizing" label
            // over the synchronous grass paint hitch.
            ApplyLoadingUiState("Preparing run...", 0.55f);
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
            // Pass raw severity — CrashSlowMoRoutine applies the curve once.
            StartCrashSlowMo(sev);
        }

        // Mid-run crash coin penalty only — not after fuel/HP are gone (run ending / settle).
        if (enableCurrencyLossOnCrash && !_currencyAwarded && !_finalizePending)
        {
            if (carController != null && (carController.IsOutOfFuel || carController.IsOutOfHP))
                return;

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

        // Outside the blast damage radius but still close enough to feel the boom → treat as a
        // close call (popup + bloom flash/brightening + close-call slow-mo), not a crash hit.
        float damageRadius = Mathf.Max(0.01f, explosionRadius);
        if (dist > damageRadius)
        {
            HandleProjectileCloseCall(explosionPos, dist);

            // Keep a light boom shake so it still reads as a nearby meteor impact.
            if (enableCrashScreenShake && cameraFollow != null)
            {
                float dur = Mathf.Lerp(0.04f, explosionShakeBaseDuration * 0.55f, proximity);
                float str = Mathf.Lerp(0.03f, explosionShakeBaseStrength * 0.45f, proximity);
                int vib = Mathf.RoundToInt(crashShakeVibrato * Mathf.Lerp(0.5f, 0.9f, proximity));
                cameraFollow.StartShake(dur, str, vib, crashShakeRandomness);
            }
            return;
        }

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

        // Scaled slow-motion based on proximity (reuses crash slow-mo routine)
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
                RegisterCoinPickup(coins);

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

        // Play a small PPS burst (chromatic + lens + bloom glow) centered on event
        PlayCloseCallStyleFXBurst();

        // Play close-call SFX
        if (closeCallClip != null)
        {
            Play2DClip(closeCallClip, closeCallVolume);
        }

        // Start a gentle slow-mo for the close-call — never while crash slow-mo owns time (crash is top priority).
        if (_ownsCrashSlowMo || _crashSlowMoRoutine != null)
            return;

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

    /// <summary>
    /// Plays the close-call screen "flair": the same chromatic + lens + bloom glow post-FX burst
    /// used when a close call happens. Reused by other events (e.g. entering an ice path) that want
    /// to flash the screen exactly like a close call. Uses the shared close-call FX tuning values.
    /// </summary>
    public void PlayCloseCallStyleFXBurst()
    {
        if (_closeCallStylePostFX == null)
            _closeCallStylePostFX = FindObjectOfType<ForcefieldPostFXController>();

        if (_closeCallStylePostFX != null)
            _closeCallStylePostFX.PlayBurstCustom(closeCallChromatic, closeCallLens, closeCallSlowMoHold * 0.9f, 0.04f, 0.15f);
    }

    private IEnumerator CloseCallSlowMoRoutine()
    {
        // Crash always wins — bail if a crash slow-mo started after we were queued.
        if (_ownsCrashSlowMo)
        {
            _closeCallCR = null;
            yield break;
        }

        TimeScaleHub.Begin(_closeCallSlowMoOwner, Mathf.Clamp(closeCallSlowMoScale, 0.05f, 1f), affectFixedDelta: true);

        float holdEnd = Time.realtimeSinceStartup + Mathf.Max(0f, closeCallSlowMoHold);
        while (Time.realtimeSinceStartup < holdEnd)
        {
            if (_ownsCrashSlowMo)
            {
                TimeScaleHub.End(_closeCallSlowMoOwner);
                _closeCallCR = null;
                yield break;
            }
            yield return null;
        }

        float ease = Mathf.Max(0f, closeCallSlowMoEaseOut);
        float t0 = Time.realtimeSinceStartup;
        float t1 = t0 + ease;
        while (Time.realtimeSinceStartup < t1)
        {
            if (_ownsCrashSlowMo)
            {
                TimeScaleHub.End(_closeCallSlowMoOwner);
                _closeCallCR = null;
                yield break;
            }
            float t = Mathf.InverseLerp(t0, t1, Time.realtimeSinceStartup);
            float scale = Mathf.Lerp(closeCallSlowMoScale, 1f, t);
            TimeScaleHub.Begin(_closeCallSlowMoOwner, scale, affectFixedDelta: true);
            yield return null;
        }

        TimeScaleHub.End(_closeCallSlowMoOwner);
        _closeCallCR = null;
    }

    /// <summary>
    /// Hard-stops every transient slow-mo source (crash, close-call, and the car's forcefield launch)
    /// and clears all hub owners so they can't stack or fight a subsequent effect / the run-end freeze.
    /// </summary>
    private void CancelAllTransientSlowMo()
    {
        if (_crashSlowMoRoutine != null)
        {
            StopCoroutine(_crashSlowMoRoutine);
            _crashSlowMoRoutine = null;
        }
        if (_closeCallCR != null)
        {
            StopCoroutine(_closeCallCR);
            _closeCallCR = null;
        }
        _ownsCrashSlowMo = false;

        carController?.CancelForcefieldSlowMo();

        TimeScaleHub.End(_crashSlowMoOwner);
        TimeScaleHub.End(_closeCallSlowMoOwner);
        TimeScaleHub.ForceClearAll();
    }

    private void StartCrashSlowMo(float severity, float holdMultiplier = 1f)
    {
        // Crash is always top priority: kill close-call + forcefield slow-mo so they cannot overwrite it.
        if (_closeCallCR != null)
        {
            StopCoroutine(_closeCallCR);
            _closeCallCR = null;
        }
        TimeScaleHub.End(_closeCallSlowMoOwner);
        carController?.CancelForcefieldSlowMo();

        if (_crashSlowMoRoutine != null)
        {
            StopCoroutine(_crashSlowMoRoutine);
            _crashSlowMoRoutine = null;
        }

        if (_ownsCrashSlowMo)
            TimeScaleHub.End(_crashSlowMoOwner);

        // Claim ownership before the coroutine runs so same-frame close-calls cannot sneak in.
        _ownsCrashSlowMo = true;
        _crashSlowMoRoutine = StartCoroutine(CrashSlowMoRoutine(severity, holdMultiplier));
    }

    private IEnumerator CrashSlowMoRoutine(float severity, float holdMultiplier)
    {
        float sev = Mathf.Clamp01(severity);
        float curveVal = crashSlowMoCurve != null && crashSlowMoCurve.keys.Length > 0
            ? Mathf.Clamp01(crashSlowMoCurve.Evaluate(sev))
            : sev;
        float targetScale = Mathf.Lerp(1f, crashSlowMoScale, curveVal);

        float hold = crashSlowMoHold * Mathf.Max(1f, holdMultiplier);
        float easeOut = crashSlowMoEaseOut;

        // Re-assert every frame during hold so no other source can weaken crash slow-mo mid-fling.
        float holdEnd = Time.realtimeSinceStartup + hold;
        while (Time.realtimeSinceStartup < holdEnd)
        {
            TimeScaleHub.Begin(_crashSlowMoOwner, targetScale, affectFixedDelta: true);
            yield return null;
        }

        float start = Time.realtimeSinceStartup;
        float end = start + easeOut;

        while (Time.realtimeSinceStartup < end)
        {
            float t = Mathf.InverseLerp(start, end, Time.realtimeSinceStartup);
            float scale = Mathf.Lerp(targetScale, 1f, t);
            TimeScaleHub.Begin(_crashSlowMoOwner, scale, affectFixedDelta: true);
            yield return null;
        }

        TimeScaleHub.End(_crashSlowMoOwner);
        _ownsCrashSlowMo = false;
        _crashSlowMoRoutine = null;
    }

    private IEnumerator CoFinalizeRunAfterDelay()
    {
        // Let FixedUpdate offer run-end mash before we wait for it to finish.
        yield return null;
        yield return new WaitForFixedUpdate();

        while (carController != null && carController.IsFlipMashActive)
        {
            if (_currencyAwarded || carInstance == null || _carRb == null)
            {
                _finalizePending = false;
                _finalizeRunCR = null;
                yield break;
            }

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

        float endTime = Time.realtimeSinceStartup + Mathf.Max(0f, runEndSettleDelay);
        while (Time.realtimeSinceStartup < endTime)
        {
            if (_currencyAwarded || carController == null || carInstance == null || _carRb == null)
            {
                _finalizePending = false;
                _finalizeRunCR = null;
                yield break;
            }

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

        PlayDeathStopBurst();
        FinalizeRun();
    }

    private void PlayDeathStopBurst()
    {
        if (_deathStopBurstPlayed) return;
        _deathStopBurstPlayed = true;

        carController?.PlayDeathVFXExtra();
        FindObjectOfType<VintageTVController>(true)?.TriggerDeathExplosionColorFlip();

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
        {
            // Clear any in-flight slow-mo first so the final death-stop is a single, controlled burst
            // instead of compounding on top of a crash/close-call/forcefield slow-mo already running.
            CancelAllTransientSlowMo();
            StartCrashSlowMo(deathStopSlowMoSeverity);
        }
    }

    private void EnsureTrackCallbacksWired()
    {
        if (trackGenerator == null) return;
        trackGenerator.OnTrackGeneratedSuccessfully -= OnTrackGeneratedDeferSpawn;
        trackGenerator.OnTrackGeneratedSuccessfully += OnTrackGeneratedDeferSpawn;
    }

    /// <summary>
    /// Terrain heightmaps are updated during track generation; wait one frame so TerrainColliders and physics
    /// match before raycasting to place obstacles/environment on hills.
    /// </summary>
    private void OnTrackGeneratedDeferSpawn(ProceduralTrackGenerator gen)
    {
        ApplyLoadingUiState("Preparing spawn points...", 0.60f);
        if (_afterTrackGenCr != null)
            StopCoroutine(_afterTrackGenCr);
        _afterTrackGenCr = StartCoroutine(CoHandleTrackGeneratedAfterTerrainHeight(gen));
    }

    private IEnumerator CoHandleTrackGeneratedAfterTerrainHeight(ProceduralTrackGenerator gen)
    {
        yield return null;
        Physics.SyncTransforms();
        yield return StartCoroutine(CoHandleTrackGenerated(gen));
        _afterTrackGenCr = null;
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

        // Stop every slow-mo source (crash/close-call/forcefield) and clear all hub owners so nothing
        // re-registers a frame later and un-freezes the game. Without this, the death-stop slow-mo
        // coroutine (which runs on realtime) keeps calling TimeScaleHub.Begin and repeatedly overrides
        // the freeze, producing the "stuck in heavy slow-mo, can't pass results" symptom.
        CancelAllTransientSlowMo();

        Time.timeScale = 0f;
    }

    /// <summary>
    /// Call when the UI flow returns the player to the skill tree (e.g. "Back to Skill Tree" button).
    /// Plays the deposit sound only if the run awarded currency and the player's bank increased.
    /// This prevents the sound on first app boot or when nothing was deposited.
    /// </summary>
    /// <param name="raiseFirstSkillTreeEntryFlag">
    /// When false, defers <see cref="TryRaiseFirstSkillTreeEntryNarrativeFlag"/> so iris can open first.
    /// </param>
    /// <param name="notifySkillTreeNarrative">
    /// When false, defers <see cref="NarrativeDirector.NotifyReturnedToSkillTree"/> until after iris open.
    /// </param>
    public void ReturnToSkillTree(bool raiseFirstSkillTreeEntryFlag = true, bool notifySkillTreeNarrative = true)
    {
        // Never yank the player out of an in-flight begin-run load.
        if (_progressState == GameProgressState.LoadingRun || _beginRunIrisCr != null)
            return;

        // Ensure game canvas is on so player can see skill tree and interact (fixes canvas staying off after run/dialogue).
        uiManager?.UnlockSection();
        uiManager?.SetGameCanvasVisible(true);
        uiManager?.SetGameplayCanvasInputLocked(false);
        // Static flag can survive scene reload if a tutorial spotlight was active.
        GameplayUIInputGuard.IsTutorialHighlightActive = false;

        // Show the skill tree root (same as existing flow)
        if (skillTreeRoot != null)
            skillTreeRoot.SetActive(true);
        uiManager?.SetSection(UIManager_Racing.UISection.SkillTree);
        ExitLoadingGameplayGate();
        carController?.SetExternalInputLock(true);
        _acceptRunEndContinueInput = false;
        _flowState = RunFlowState.SkillTree;
        SetProgressState(GameProgressState.SkillTree);
        if (raiseFirstSkillTreeEntryFlag)
            TryRaiseFirstSkillTreeEntryNarrativeFlag();
        // Post-run garage dialogue (e.g. can-afford Max Fuel) — not during results.
        if (notifySkillTreeNarrative)
        {
            NarrativeDirector.NotifyReturnedToSkillTree();
        }

        // Hide run UI if present
        uiManager?.HideRunComplete();

        EndFinishPortalPresentation();
        FindObjectOfType<VintageTVController>(true)?.ResetVisualToDefaults();

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
        if (trackObstacleSpawner == null) trackObstacleSpawner = FindObjectOfType<TrackObstacleSpawner>(true);
        if (trackCoinSpawner == null) trackCoinSpawner = FindObjectOfType<TrackCoinSpawner>(true);
        if (trackFuelSpawner == null) trackFuelSpawner = FindObjectOfType<TrackFuelSpawner>(true);
        if (trackHPSpawner == null) trackHPSpawner = FindObjectOfType<TrackHPSpawner>(true);
        if (helpPatronSpawner == null) helpPatronSpawner = TrackHelpPatronSpawner.EnsureExists();
        if (icePathSpawner == null) icePathSpawner = FindObjectOfType<IcePathSpawner>(true);
        if (npcCarSpawner == null) npcCarSpawner = FindObjectOfType<NPCTrafficCarSpawner>(true);
        if (creatureSpawner == null) creatureSpawner = FindObjectOfType<TrackCreatureSpawner>(true);
        if (trackEnvironmentSpawner == null) trackEnvironmentSpawner = FindObjectOfType<TrackEnvironmentSpawner>(true);
        if (crossObstacleDirector == null) crossObstacleDirector = FindObjectOfType<CrossObstacleDirector>(true);
        if (bounceBackObstacleSpawner == null) bounceBackObstacleSpawner = FindObjectOfType<BounceBackObstacleSpawner>(true);
        if (thrownObstacleDirector == null) thrownObstacleDirector = FindObjectOfType<ThrownObstacleDirector>(true);
        if (rollingLogSpawner == null) rollingLogSpawner = FindObjectOfType<RollingLogSpawner>(true);
        if (iceScreenFlashDriver == null) iceScreenFlashDriver = FindObjectOfType<IcePathScreenFlashDriver>(true);
        if (trackSpawnerQueue == null) trackSpawnerQueue = FindObjectOfType<TrackSpawnerQueue>(true);
    }

    private IEnumerator CoHandleTrackGenerated(ProceduralTrackGenerator gen)
    {
        // Bake a road clip mask. Authored terrain grass maps are not rewritten.
        if (terrainGrassPainter != null)
        {
            ApplyLoadingUiState("Clearing grass on road...", 0.62f);
            yield return null;
            yield return StartCoroutine(terrainGrassPainter.CoPaint(trackGenerator));
        }

        ApplyLoadingUiState("Spawning player car...", 0.70f);
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


        yield return StartCoroutine(CoRespawnCarAtTrackStart(spawnPos, spawnRot));

        yield return StartCoroutine(CoHideLoadingNextFrame());
    }

    /// <summary>Spawn or reposition the car at the given position/rotation and bind all systems. Used by normal track flow and by TEST spawn hotkey.</summary>
    private IEnumerator CoRespawnCarAtTrackStart(Vector3 spawnPos, Quaternion spawnRot)
    {
        if (carPrefab == null) yield break;

        if (carInstance == null)
            carInstance = Instantiate(carPrefab, spawnPos, spawnRot);
        else
            carInstance.transform.SetPositionAndRotation(spawnPos, spawnRot);

        _deathStopBurstPlayed = false;
        yield return null;

        if (helpPatronSpawner == null)
            helpPatronSpawner = TrackHelpPatronSpawner.EnsureExists();

        // Apply the active trial's spawner settings before each spawner initializes for this run.
        DayTrialManager.Instance?.ApplyCurrentTrialToSpawners(
            trackObstacleSpawner,
            creatureSpawner,
            npcCarSpawner,
            trackCoinSpawner,
            thrownObstacleDirector,
            rollingLogSpawner,
            crossObstacleDirector,
            icePathSpawner,
            bounceBackObstacleSpawner,
            trackSpawnerQueue,
            helpPatronSpawner);

        uiManager?.SetLoadingState("Spawning creatures...", 0.76f);
        creatureSpawner?.InitializeForRun(trackGenerator, carInstance.transform);
        yield return null;

        
        uiManager?.SetLoadingState("Placing obstacles...", 0.72f);
        trackObstacleSpawner?.InitializeForRun(trackGenerator, carInstance.transform);
        yield return null;

        uiManager?.SetLoadingState("Placing environment...", 0.80f);
        if (trackEnvironmentSpawner != null)
            yield return trackEnvironmentSpawner.CoInitializeForRun(trackGenerator, carInstance.transform);
        else
            yield return null;

        uiManager?.SetLoadingState("Spawning coins...", 0.84f);
        trackCoinSpawner?.InitializeForRun(trackGenerator, carInstance.transform);
        yield return null;
        uiManager?.SetLoadingState("Spawning pickups...", 0.86f);
        trackFuelSpawner?.InitializeForRun(trackGenerator, carInstance.transform);
        trackHPSpawner?.InitializeForRun(trackGenerator, carInstance.transform);
        helpPatronSpawner?.InitializeForRun(trackGenerator, carInstance.transform);
        yield return null;
        uiManager?.SetLoadingState("Spawning hazards...", 0.89f);
        icePathSpawner?.InitializeForRun(trackGenerator, carInstance.transform);
        rollingLogSpawner?.InitializeForRun(trackGenerator, carInstance.transform);
        bounceBackObstacleSpawner?.InitializeForRun(trackGenerator, carInstance.transform);
        yield return null;
        uiManager?.SetLoadingState("Spawning traffic...", 0.91f);
        npcCarSpawner?.InitializeForRun(trackGenerator, carInstance.transform);
        yield return null;
        trackSpawnerQueue?.InitializeForRun(trackGenerator, carInstance.transform);
        yield return null;


        Rigidbody rb = carInstance.GetComponent<Rigidbody>();
        if (rb != null)
        {
            rb.velocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
        }

        carController = carInstance.GetComponent<CarController>();
        carController?.SetExternalInputLock(true);
        crossObstacleDirector?.SetCar(carController);
        thrownObstacleDirector?.SetCar(carController);
        iceScreenFlashDriver?.SetCarController(carController);

        runDistanceMeters = 0f;
        _maxRunNormalizedProgress = 0f;
        _carRb = carInstance.GetComponent<Rigidbody>();
        _currencyAwarded = false;
        runEnded = false;
        uiManager?.HideRunComplete();

        EnsureRefs();

        if (distanceSystem != null && trackGenerator != null)
            distanceSystem.Configure(trackGenerator, carInstance.transform);

        if (cameraFollow != null)
            cameraFollow.SetTarget(carInstance.transform, snapImmediate: true);

        if (uiManager != null && carController != null)
            uiManager.BindCar(carController);

        ApplyLoadingUiState("Ready!", 0.99f);
    }


    private bool RestartAllowedNow()
    {
        // Once the run-end freeze has actually kicked in (results are up and gameplay is frozen),
        // ALWAYS allow run-end input so the player can never get trapped by a lingering
        // slow-mo owner. This is the safety net behind the hub purge / freeze cleanup.
        if (runEnded && Time.timeScale <= 0.0001f)
            return true;

        // Otherwise only allow continue after all slowmo/pause owners are done (lets the dramatic
        // death-stop slow-mo play out before the freeze without being skippable early).
        return !TimeScaleHub.IsPaused
            && !TimeScaleHub.IsAnyActive;
    }

    /// <summary>Right-click, Triangle (pad North/Y), or V — quick replay into a new run.</summary>
    private static bool WasQuickReplayPressed()
    {
        if (Input.GetMouseButtonDown(1))
            return true;
        if (Mouse.current != null && Mouse.current.rightButton.wasPressedThisFrame)
            return true;

        if (Input.GetKeyDown(QUICK_RESTART_KEY))
            return true;

        if (Input.GetKeyDown(PAD_TRIANGLE))
            return true;

        var reader = RacingInputReader.Instance;
        if (reader != null && reader.MashNorthDown)
            return true;

        if (Gamepad.current != null && Gamepad.current.buttonNorth.wasPressedThisFrame)
            return true;

        return false;
    }

    /// <summary>Left-click, Cross (pad South/X), or R — back to skill tree.</summary>
    private static bool WasReturnToSkillTreePressed()
    {
        if (Input.GetMouseButtonDown(0))
            return true;
        if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
            return true;

        if (Input.GetKeyDown(KeyCode.R))
            return true;

        if (Input.GetKeyDown(PAD_X))
            return true;

        var reader = RacingInputReader.Instance;
        if (reader != null && reader.MashSouthDown)
            return true;

        if (Gamepad.current != null && Gamepad.current.buttonSouth.wasPressedThisFrame)
            return true;

        return false;
    }

    private IEnumerator CoHideLoadingNextFrame()
    {
        // Wait until goo sealed and loading canvas was uncovered.
        while (!_beginRunIrisEngulfComplete)
            yield return null;

        // Keep loading up for a beat even if gen was instant (later runs).
        if (_loadingScreenShownAtRealtime > 0f)
        {
            float shownUntil = _loadingScreenShownAtRealtime + MinLoadingScreenSeconds;
            while (Time.realtimeSinceStartup < shownUntil)
                yield return null;
        }

        // let instantiates + layout rebuilds finish
        yield return null;
        cameraFollow?.SnapToTargetImmediate();

        // Cover loading → in-game swap with sealed black, then iris-open onto the track.
        var iris = GooIrisScreenTransition.EnsureExists();
        iris.SnapSealed();

        uiManager?.HideLoading();
        ExitLoadingGameplayGate();
        uiManager?.SetSection(UIManager_Racing.UISection.InGameDefault);
        uiManager?.ShowRunCoins();
        uiManager?.UpdateRunCoins(_pickupCoinsThisRun + _obstacleCoinsThisRun);
        uiManager?.UpdateRunSprockets(_sprocketsThisRun);

        carController?.SetExternalInputLock(true);
        _flowState = RunFlowState.InRun;
        SetProgressState(GameProgressState.InRun);

        // DAY/RUN + TV static go up under sealed goo so they persist through the open,
        // then keep their authored hold + fade after the iris finishes.
        RunStartIntroUI intro = runStartIntro;
        if (intro == null)
            intro = FindObjectOfType<RunStartIntroUI>(true);
        bool introShown = intro != null && intro.isActiveAndEnabled && intro.ShowIntroImmediate();

        yield return iris.CoOpen(postLoadingIrisOpenSeconds);

        if (introShown)
            yield return intro.CoHoldAndFadeOut();

        carController?.SetExternalInputLock(false);
        _acceptRunEndContinueInput = false;
    }

    private void SetProgressState(GameProgressState state)
    {
        _progressState = state;
    }

    /// <summary>
    /// Convenience entry point for wiring a future main menu UI.
    /// For now this uses the same view as SkillTree, but keeps a distinct enum for clarity.
    /// </summary>
    public void EnterMainMenu()
    {
        // TODO: when a dedicated main menu exists, activate its canvas here.
        SetProgressState(GameProgressState.MainMenu);
    }

    /// <summary>
    /// Global pause toggle; can be wired to ESC / Start. Pauses time and marks state as Paused.
    /// Does not change the underlying flow state, only overlays a pause menu.
    /// </summary>
    public void TogglePause()
    {
        if (_progressState == GameProgressState.Paused)
        {
            Time.timeScale = 1f;
            SetProgressState(IsGameplayLive ? GameProgressState.InRun : GameProgressState.SkillTree);
        }
        else if (_progressState == GameProgressState.InRun || _progressState == GameProgressState.SkillTree)
        {
            Time.timeScale = 0f;
            SetProgressState(GameProgressState.Paused);
        }
    }

    private void TryRaiseFirstSkillTreeEntryNarrativeFlag()
    {
        if (string.IsNullOrWhiteSpace(initFinishedStoryFlag) || string.IsNullOrWhiteSpace(firstSkillTreeEntryStoryFlag))
            return;

        // Gate first-skill-tree narrative until init intro has explicitly finished.
        if (!NarrativeDirector.HasStoryFlag(initFinishedStoryFlag))
        {
            Debug.Log(
                $"[GameManager] Skipping first skill-tree narrative — missing flag '{initFinishedStoryFlag}'.");
            return;
        }

        if (NarrativeDirector.HasStoryFlag(firstSkillTreeEntryStoryFlag))
            return;

        Debug.Log(
            $"[GameManager] '{initFinishedStoryFlag}' present — raising '{firstSkillTreeEntryStoryFlag}' " +
            "and checking narrative triggers.");
        NarrativeDirector.SetStoryFlag(firstSkillTreeEntryStoryFlag);
        NarrativeDirector.Instance?.CheckTriggers();
    }

    private void WireNarrativeEvents()
    {
        if (DialogueManager.Instance == null) return;
        DialogueManager.Instance.OnSequenceStarted -= HandleDialogueSequenceStarted;
        DialogueManager.Instance.OnSequenceCompleting -= HandleDialogueSequenceCompleting;
        DialogueManager.Instance.OnSequenceCompleted -= HandleDialogueSequenceCompleted;
        DialogueManager.Instance.OnSequenceStarted += HandleDialogueSequenceStarted;
        DialogueManager.Instance.OnSequenceCompleting += HandleDialogueSequenceCompleting;
        DialogueManager.Instance.OnSequenceCompleted += HandleDialogueSequenceCompleted;
    }

    private void UnwireNarrativeEvents()
    {
        if (DialogueManager.Instance == null) return;
        DialogueManager.Instance.OnSequenceStarted -= HandleDialogueSequenceStarted;
        DialogueManager.Instance.OnSequenceCompleting -= HandleDialogueSequenceCompleting;
        DialogueManager.Instance.OnSequenceCompleted -= HandleDialogueSequenceCompleted;
    }

    private void HandleDialogueSequenceCompleting(DialogueSequenceSO completed)
    {
        // Keep the last init line + goo bubbles up so the iris can close over that screen.
        if (SequenceCompletesWithFlag(completed, initFinishedStoryFlag))
            DialogueManager.Instance?.RequestHoldUiUntilReleased();
    }

    private void HandleDialogueSequenceStarted(DialogueSequenceSO _)
    {
        // Any dialogue / cutscene sequence moves the global state into Dialogue.
        // Init intro vs later narrative is distinguished by story flags, not enum.
        if (_progressState == GameProgressState.SkillTree ||
            _progressState == GameProgressState.InitIntro ||
            _progressState == GameProgressState.RunEnd ||
            _progressState == GameProgressState.InRun)
        {
            _progressStateBeforeDialogue = _progressState;
            SetProgressState(GameProgressState.Dialogue);
        }
    }

    private void HandleDialogueSequenceCompleted(DialogueSequenceSO completed)
    {
        // Centralized flow ownership: GameManager decides where to go after dialogue.
        // Accept Dialogue or InitIntro — init can finish while still marked InitIntro if
        // OnSequenceStarted was missed due to Start() ordering.
        bool inDialogueFlow =
            _progressState == GameProgressState.Dialogue ||
            _progressState == GameProgressState.InitIntro;

        if (!inDialogueFlow)
            return;

        if (runEnded)
        {
            ReturnToSkillTree();
            return;
        }

        // Only the sequence that SETS init_finish should run the iris handoff — not every later
        // dialogue while that flag is already present (e.g. Init_SkillTree).
        if (SequenceCompletesWithFlag(completed, initFinishedStoryFlag))
        {
            Debug.Log(
                $"[GameManager] '{completed?.name}' completed with '{initFinishedStoryFlag}' — " +
                "iris close over init dialogue → skill tree.");
            if (_flowIrisCr != null)
                StopCoroutine(_flowIrisCr);
            _flowIrisCr = StartCoroutine(CoInitDialogueToSkillTreeWithIris());
            return;
        }

        // In-run pickup dialogue (Taskmaster): unpause and keep racing.
        if (_progressStateBeforeDialogue == GameProgressState.InRun)
        {
            SetProgressState(GameProgressState.InRun);
            return;
        }

        // Non-init dialogue that kept the skill tree visible (e.g. Init_SkillTree overlay):
        // return to SkillTree state without requiring init_finish again.
        // Never steal the Loading section mid begin-run.
        if (_progressState == GameProgressState.LoadingRun ||
            _progressState == GameProgressState.InRun ||
            _flowState == RunFlowState.Loading)
            return;

        SetProgressState(GameProgressState.SkillTree);
        uiManager?.SetGameCanvasVisible(true);
        uiManager?.SetSection(UIManager_Racing.UISection.SkillTree);
    }

    private IEnumerator CoInitDialogueToSkillTreeWithIris()
    {
        var iris = GooIrisScreenTransition.EnsureExists();

        iris.EnsureReadyToCloseOverScreen();
        iris.SetSortOrder(GooIrisScreenTransition.SortOverDialogue);
        DialogueUI.RefreshHostSortForIris();
        yield return iris.CoCloseAndHold(restoreDefaultSort: false);

        // Sealed black: tear down held init dialogue, show skill tree, start Init_SkillTree
        // UNDER the goo so it's already up as the iris opens.
        DialogueManager.Instance?.ReleaseHeldDialogueUi();
        ReturnToSkillTree(raiseFirstSkillTreeEntryFlag: false, notifySkillTreeNarrative: false);

        TryRaiseFirstSkillTreeEntryNarrativeFlag();
        NarrativeDirector.NotifyReturnedToSkillTree();

        yield return null;
        yield return null;

        yield return iris.CoOpenRevealingDialogue();
        _flowIrisCr = null;
    }

    private static bool SequenceCompletesWithFlag(DialogueSequenceSO sequence, string flag)
    {
        if (sequence == null || string.IsNullOrWhiteSpace(flag))
            return false;
        return string.Equals(
            sequence.setStoryFlagOnComplete?.Trim(),
            flag.Trim(),
            System.StringComparison.OrdinalIgnoreCase);
    }

    private void SyncFlowStateFromUISection()
    {
        if (uiManager == null) return;

        switch (uiManager.CurrentSection)
        {
            case UIManager_Racing.UISection.SkillTree:
                _flowState = RunFlowState.SkillTree;
                _acceptRunEndContinueInput = false;
                break;

            case UIManager_Racing.UISection.Loading:
                _flowState = RunFlowState.Loading;
                _acceptRunEndContinueInput = false;
                break;

            case UIManager_Racing.UISection.RunEnd:
                if (runEnded)
                {
                    _flowState = RunFlowState.RunEnd;
                    _acceptRunEndContinueInput = true;
                }
                break;

            default:
                _flowState = RunFlowState.InRun;
                _acceptRunEndContinueInput = false;
                break;
        }
    }

    private void EnterLoadingGameplayGate()
    {
        if (_loadingGameplayGateActive) return;
        _loadingGameplayGateActive = true;

        // Freeze gameplay simulation while loading UI is visible.
        Time.timeScale = 0f;

        // Keep only whitelisted music alive while all other audio is paused.
        ApplyMusicWhitelistBypass(enable: true);
        if (!AudioListener.pause)
        {
            AudioListener.pause = true;
            _audioPausedByLoadingGate = true;
        }
    }

    private void ExitLoadingGameplayGate()
    {
        if (!_loadingGameplayGateActive) return;
        _loadingGameplayGateActive = false;

        // Resume normal gameplay simulation.
        Time.timeScale = 1f;

        if (_audioPausedByLoadingGate)
        {
            AudioListener.pause = false;
            _audioPausedByLoadingGate = false;
        }

        ApplyMusicWhitelistBypass(enable: false);
    }

    private void ApplyMusicWhitelistBypass(bool enable)
    {
        if (loadingMusicWhitelist == null || loadingMusicWhitelist.Length == 0)
            return;

        for (int i = 0; i < loadingMusicWhitelist.Length; i++)
        {
            AudioSource src = loadingMusicWhitelist[i];
            if (src == null) continue;

            if (enable)
            {
                if (!_musicIgnorePauseOriginal.ContainsKey(src))
                    _musicIgnorePauseOriginal[src] = src.ignoreListenerPause;
                src.ignoreListenerPause = true;
            }
            else if (_musicIgnorePauseOriginal.TryGetValue(src, out bool prev))
            {
                src.ignoreListenerPause = prev;
            }
        }

        if (!enable)
            _musicIgnorePauseOriginal.Clear();
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

        // Raw severity — CrashSlowMoRoutine applies the curve once (same as OnCarCrash).
        StartCrashSlowMo(Mathf.Clamp01(severity), lethalCrashSlowMoHoldMultiplier);
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