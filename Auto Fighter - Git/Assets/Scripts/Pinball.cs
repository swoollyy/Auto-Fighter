using DG.Tweening;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// Pinball gameplay state machine
public enum PinballState
{
    None,
    Charging,
    Push,
    Play,
    LevelUp,
    PaddleSelect,
    SkillAim,
    ResetBall,
    GameOver
}

[DisallowMultipleComponent]
public class Pinball : MonoBehaviour, IRunContext
{
    public static Pinball Instance { get; private set; }

    [Header("XP Forcefield")]
    [SerializeField, Tooltip("Global base scale applied to each ball's XP forcefield baseline.")]
    private float xpBaseRadiusScale = 1f;
    public float XPBaseRadiusScale => xpBaseRadiusScale;

    #region Reward / RunContext
    private readonly HashSet<string> owned = new();
    private readonly HashSet<string> active = new();
    private readonly HashSet<string> available = new();
    private readonly HashSet<string> exclusiveKeysActive = new();

    public bool Owns(string rewardId) => owned.Contains(rewardId);
    public bool IsActive(string rewardId) => active.Contains(rewardId);
    public bool IsAvailable(string rewardId) => available.Contains(rewardId);
    public bool HasExclusiveKeyActive(string key) =>
        !string.IsNullOrEmpty(key) && exclusiveKeysActive.Contains(key);
    public IEnumerable<string> ActiveKeys => exclusiveKeysActive;

    public void MarkOwned(string rewardId) => owned.Add(rewardId);
    public void SetActive(string rewardId, bool on) { if (on) active.Add(rewardId); else active.Remove(rewardId); }
    public void SetAvailable(string rewardId, bool on) { if (on) available.Add(rewardId); else available.Remove(rewardId); }
    public void SetExclusive(string key, bool on)
    {
        if (string.IsNullOrEmpty(key)) return;
        if (on) exclusiveKeysActive.Add(key); else exclusiveKeysActive.Remove(key);
    }

    private static readonly Dictionary<RewardRarity, float> rarityWeights = new()
    {
        {RewardRarity.Common, 88f},
        {RewardRarity.Uncommon, 52f},
        {RewardRarity.Rare, 25f},
        {RewardRarity.Epic, 12f},
        {RewardRarity.Legendary, 3f},
        {RewardRarity.Artifact, .5f},
        {RewardRarity.Cursed, .5f},
    };
    #endregion

    #region Serialized configuration
    [Header("Input")]
    [SerializeField] private KeyCode chargeKey = KeyCode.Space;
    [SerializeField] private KeyCode leftPaddleKey = KeyCode.A;
    [SerializeField] private KeyCode rightPaddleKey = KeyCode.D;

    [Header("Progression")]
    [Min(0)] public float curXP = 0;
    [Min(1)] public float maxXP;
    public int level { get; private set; } = 1;
    private float lastBaseXPMultApplied = 1f;
    private float lastBaseScoreMultApplied = 1f;

    private Tween _timeScaleTween, _fovTween;

    [Header("Debug / Portal Reset")]
    [SerializeField, Min(0f)] private float portalResetDebugCooldown = 0.75f;

    [Header("Skill Aim (Green Pad)")]
    [SerializeField] private LineRenderer aimLine;
    [SerializeField] private float camAimFov = 38f;
    [SerializeField] private float camFovTween = 0.12f;
    [SerializeField] private CameraFollowSimple camFollow;
    private readonly object _skillAimSlowmoToken = new object();

    private Ball _aimBall;
    private Transform _aimFocus;
    private float _aimWindowDur;
    private float _aimTimeLeft;
    private float _aimMinSpeed, _aimMaxSpeed;
    private float _preAimFov;
    private float _preAimTimeScale = 1f;
    private float _targetSlowMo = 0.15f;
    private float _nextHitFactor = 2f;
    private int _nextHitBounces = 1;

    private const float SkillAimUncapDuration = .5f;
    private const float SkillAimUncapMaxSpeed = 100f;

    [SerializeField] private float aimFollowDistance = 8f;
    [SerializeField] private float aimFollowTween = 0.12f;
    [SerializeField] private float aimLineMaxLen = 0.5f;

    private Vector3 _lastAimDir = Vector3.forward;
    private Transform _camDefaultTarget;
    private float _camDefaultFollowDist;
    private float _camDefaultHeight;
    private float _camDefaultFov;
    private Vector3 _camDefaultCamPos;
    private Quaternion _camDefaultCamRot;
    private Vector3 _preAimCamPos;
    private Quaternion _preAimCamRot;
    private Tween _camDistTween, _camPosTween, _camRotTween;
    private Transform _preAimCamTarget;
    private float _preAimFollowDist;
    private Transform _aimCamTarget;

    [Header("Skill Aim PostFX")]
    [SerializeField] private PostFXController postFX;
    public PostFXController PostFX => postFX;
    [SerializeField, Range(0f, 1f)] private float vignetteMax = 0.55f;

    [Header("References")]
    [SerializeField] public Ball ball;
    [SerializeField] private PinballUIM ui;
    [SerializeField] private GameObject ballClonePrefab;
    [SerializeField] private GameObject invisWalls;
    [SerializeField] private PinballFlipper leftPaddle;
    [SerializeField] private PinballFlipper rightPaddle;

    [Header("Charge/Push Tuning")]
    [SerializeField, Min(0.05f)] private float chargeMaxCharging = 1.5f;
    [SerializeField, Min(0.05f)] private float chargeMaxPush = 1.0f;
    [SerializeField, Range(0f, 1f)] private float minChargeToLaunch = 0.10f;
    [SerializeField, Range(0f, 1f)] private float chargeToEnterPush = 0.65f;

    [Header("Balls / Lives")]
    [SerializeField, Range(0, 99)] private int startingLives = 3;
    [SerializeField, Range(1, 99)] private int maxLives = 5;
    public int Lives => lives;
    public int MaxLives => maxLives;

    [Header("Score/XP Multipliers")]
    [SerializeField, Tooltip("Base (floor) score multiplier that increases with leveling.")]
    private float baseScoreMult = 1f;
    [SerializeField, Tooltip("Base (floor) XP multiplier that increases with leveling.")]
    private float baseXPMult = 1f;

    [Header("Powerups & Drops")]
    [SerializeField, Range(0f, 1f)]
    public float PowerupDropChance = .03f;

    [Header("Visual FX")]
    [SerializeField] private Camera mainCam;
    [SerializeField, Min(0f)] private float shakeDuration = 0.15f;
    [SerializeField, Min(0f)] private float shakeStrength = 0.3f;
    [SerializeField, Min(1)] private int shakeVibrato = 12;
    [SerializeField, Range(0f, 180f)] private float shakeRandomness = 90f;
    [SerializeField] private ParticleSystem xpFXPrefab;

    [Header("UX/UI")]
    [SerializeField] private BallXPBar ballXPScript;

    [Header("Bumper Death FX")]
    [SerializeField] private float explosionForce = 50f;
    [SerializeField] private float explosionRadius = 3f;

    [Header("Global Physics")]
    [SerializeField] private bool overrideGlobalGravity = true;
    [SerializeField] private Vector3 gravityOverride = new Vector3(0f, 0f, -19.62f);

    [Header("LevelUp Flow")]
    [SerializeField, Tooltip("If a Level Up occurs during SkillAim, wait this long (unscaled) after aim ends before showing rewards.")]
    private float postAimLevelUpDelay = 0.20f;
    private bool _queueLevelUpAfterAim;

    [Header("Post-LevelUp Resume SlowMo")]
    [SerializeField] private bool enableResumeSlowMo = true;
    [SerializeField, Tooltip("Initial timeScale when resuming from LevelUp (0.05..1). Lower = stronger slow-mo.")]
    [Range(0.05f, 1f)] private float resumeSlowMoStartScale = 0.18f;
    [SerializeField, Tooltip("Hold time (unscaled) at the strongest slow-mo before easing back to normal.")]
    [Min(0f)] private float resumeSlowMoHold = 0.30f;
    [SerializeField, Tooltip("Ease-out time (unscaled) to return to normal timeScale.")]
    [Min(0.05f)] private float resumeSlowMoEase = 0.30f;
    private readonly object _postLevelUpSlowmoToken = new object();
    private Tween _resumeSlowmoTween;
    #endregion

    #region Runtime state
    private PinballState currentState;
    public PinballState CurrentState => currentState;

    private bool isHoldingCharge;
    private float chargeTimer;
    public float chargePercentage { get; private set; }
    private float chargeMax;

    private readonly Queue<SkillAimRequest> _aimQueue = new();
    private struct SkillAimRequest
    {
        public Ball ball;
        public Transform focus;
        public float slowMo;
        public float windowUnscaled;
        public float minSpeed;
        public float maxSpeed;
        public float nextHitFactor;
        public int nextHitBounces;
        public SkillAimRequest(Ball b, Transform f, float s, float w, float minV, float maxV, float nf, int nb)
        {
            ball = b; focus = f; slowMo = s; windowUnscaled = w; minSpeed = minV; maxSpeed = maxV; nextHitFactor = nf; nextHitBounces = nb;
        }
    }

    private int score;
    private int mult;
    private float scoreMultiplier = 1f;
    private float xpMultiplier = 1f;
    private float scoreBonusTimer;
    private float xpBonusTimer;
    public int Score => score;
    public int Mult => mult;
    public float ScoreMultiplier => scoreMultiplier;
    public bool IsScoreMultiplierActive => scoreMultiplier > 1f;
    public float ScoreBonusTimeRemaining => scoreBonusTimer;
    public float XPMultiplier => xpMultiplier;
    public bool IsXPMultiplierActive => xpMultiplier > 1f;
    public float XPBonusTimeRemaining => xpBonusTimer;
    private float finalXPCalculated;

    public float FinalXPCalculated => finalXPCalculated;

    private bool canHitL, canHitR, hasBeenHitL, hasBeenHitR;

    private readonly List<Ball> liveBalls = new();
    private int _primaryBallId;
    public int ballCount { get; private set; }
    private int lives;

    [SerializeField, Min(0f)] private float noBallResetGrace = 0.20f;
    private float _noBallTimer;

    private Vector3 _ballStartPos;
    private Quaternion _ballStartRot;
    private int pendingLevelUps = 0;
    private RewardSO pendingPaddleReward;
    private readonly List<RewardSO> rewardPool = new();

    public bool destroyedBumperBonusActive;
    private float destroyedBumperScoreMult = 2f;
    private int xpCount = 3;
    private bool wallTimerStart;
    private float wallTimer;
    private float _defaultFixedDeltaTime;
    public float DefaultFixedDeltaTime => _defaultFixedDeltaTime;

    private float _lastTimeScaleCheck;

    private int bumperBouncesS;
    private int bumperBouncesG;
    private bool extraHitsS;
    private bool extraHitsG;
    private float bonusHitsS;
    private float bonusHitsG;
    private int bouncesForBonusS;
    private int bouncesForBonusG;

    private const float X2_MULT_MAGIC = 100f;
    private const float X4_MULT_MAGIC = 200f;

    public float Damage = 5f;
    #endregion

    #region Helper (rounding)
    private static float Round2(float v) => Mathf.Round(v * 100f) / 100f;
    #endregion

    #region Unity lifecycle
    private void Awake()
    {
        if (Instance && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        _defaultFixedDeltaTime = Time.fixedDeltaTime;
        TimeScaleHub.EnsureInitialized(_defaultFixedDeltaTime);
        if (overrideGlobalGravity)
            Physics.gravity = gravityOverride;
        DOTween.SetTweensCapacity(500, 50);
        if (ball != null)
        {
            RegisterBall(ball);
            _ballStartPos = ball.transform.position;
            _ballStartRot = ball.transform.rotation;
            _primaryBallId = ball.GetInstanceID();
        }
        maxXP = XPFormula.XpReq(level);
        maxLives = Mathf.Max(1, maxLives);
        startingLives = Mathf.Clamp(startingLives, 0, maxLives);
        lives = startingLives;
        lastBaseXPMultApplied = baseXPMult;
        lastBaseScoreMultApplied = baseScoreMult;

        xpBaseRadiusScale = Mathf.Max(0.0001f, xpBaseRadiusScale);

        if (camFollow)
        {
            _camDefaultTarget = camFollow.Target;
            _camDefaultFollowDist = camFollow.FollowDistance;
            _camDefaultHeight = camFollow.Height;
        }
        if (mainCam)
        {
            _camDefaultFov = mainCam.fieldOfView;
            _camDefaultCamPos = mainCam.transform.position;
            _camDefaultCamRot = mainCam.transform.rotation;
        }
    }

    private async void Start()
    {
        var loader = Addressables.LoadAssetsAsync<RewardSO>("Rewards", rewardPool.Add);
        await loader.Task;

        ui?.InitLives(maxLives);
        OnLivesChanged();

        PowerupSystem.EnsureInitialized();
        ChangeState(PinballState.Charging);

        ui?.Init(this);
        if (invisWalls) invisWalls.SetActive(false);

        ballCount = 1;
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
        RestoreFromSkillAim(false);
        Time.timeScale = 1f;
        Time.fixedDeltaTime = _defaultFixedDeltaTime;
    }

    private void OnValidate()
    {
        if (maxLives < 1) maxLives = 1;
        if (startingLives > maxLives) startingLives = maxLives;
        if (baseScoreMult < 1f) baseScoreMult = 1f;
        if (baseXPMult < 1f) baseXPMult = 1f;
        if (noBallResetGrace < 0f) noBallResetGrace = 0f;
        if (chargeToEnterPush < minChargeToLaunch)
            chargeToEnterPush = Mathf.Clamp01(Mathf.Max(minChargeToLaunch, chargeToEnterPush));
    }

    private void Update()
    {
        HandlePendingLevelUps();
        HandlePaddles();
        ClampBaseMultipliers();
        MaybeEnableInvisibleWalls();
        HandleGameOverRestart();
        UpdateMultipliersTimers();

        switch (currentState)
        {
            case PinballState.Play:
                HandlePlayStateDrain();
                break;
            case PinballState.Charging:
            case PinballState.Push:
                HandleChargingAndPush();
                break;
        }

        if (CurrentState == PinballState.SkillAim)
        {
            UpdateSkillAim();
            return;
        }

        if (Input.GetKeyDown("p"))
            AddXP(50f);

        TickChargeTimer();

        if (Time.timeScale < 0.99f &&
            (currentState == PinballState.Play || currentState == PinballState.Charging || currentState == PinballState.Push) &&
            !TimeScaleHub.IsAnyActive &&
            currentState != PinballState.SkillAim &&
            currentState != PinballState.LevelUp &&
            currentState != PinballState.PaddleSelect)
        {
            if (Time.unscaledTime - _lastTimeScaleCheck > 0.25f)
            {
                _lastTimeScaleCheck = Time.unscaledTime;
                NormalizeTimeScaleIfNeeded();
            }
        }
        EnsureNormalTimescaleIfNotAiming();
    }
    #endregion

    #region State machine
    public void ChangeState(PinballState newState)
    {
        if (currentState == newState) return;
        ExitState(currentState);
        currentState = newState;
        EnterState(newState);
    }

    private void EnterState(PinballState state)
    {
        switch (state)
        {
            case PinballState.Charging:
                ResetChargeState(max: chargeMaxCharging);
                NormalizeTimeScaleIfNeeded();
                break;
            case PinballState.Push:
                ResetChargeState(max: chargeMaxPush);
                NormalizeTimeScaleIfNeeded();
                break;
            case PinballState.Play:
                ui?.DefaultUI();
                chargePercentage = 0;
                chargeTimer = 0;
                Time.timeScale = 1f;
                Time.fixedDeltaTime = _defaultFixedDeltaTime;
                wallTimerStart = true;
                break;
            case PinballState.LevelUp:
                CancelAllPortalSlowmo();
                TimeScaleHub.BeginPause(this);
                LevelUp();
                var choices = GetRewardChoices();
                ui?.ShowRewardPopup(choices);
                Time.timeScale = 0f;
                Time.fixedDeltaTime = _defaultFixedDeltaTime;
                break;
            case PinballState.PaddleSelect:
                CancelAllPortalSlowmo();
                TimeScaleHub.BeginPause(this);
                ui?.PaddleSelect();
                Time.timeScale = 0f;
                Time.fixedDeltaTime = _defaultFixedDeltaTime;
                break;
            case PinballState.SkillAim:
                break;
            case PinballState.ResetBall:
                lives = Mathf.Max(0, lives - 1);
                OnLivesChanged();
                ResetBallAndFlow();
                break;
            case PinballState.GameOver:
                leftPaddle = null;
                rightPaddle = null;
                NormalizeTimeScaleIfNeeded();
                break;
        }
    }

    private void ExitState(PinballState state)
    {
        switch (state)
        {
            case PinballState.LevelUp:
            case PinballState.PaddleSelect:
                TimeScaleHub.EndPause(this);
                NormalizeTimeScaleIfNeeded();
                break;
            case PinballState.SkillAim:
                RestoreFromSkillAim();
                NormalizeTimeScaleIfNeeded();
                break;
        }
    }

    private void ResetChargeState(float max)
    {
        isHoldingCharge = false;
        chargePercentage = 0f;
        chargeTimer = 0f;
        chargeMax = Mathf.Max(0.05f, max);
    }
    #endregion

    #region UI & Lives
    private void OnLivesChanged()
    {
        ui?.UpdateLives(lives, maxLives);
    }
    #endregion

    #region Input / helpers
    private void HandlePendingLevelUps()
    {
        while (curXP >= maxXP)
        {
            pendingLevelUps++;
            curXP -= maxXP;
        }

        // Don't interrupt SkillAim; queue LevelUp until aim completes.
        if (pendingLevelUps > 0)
        {
            if (CurrentState == PinballState.SkillAim)
            {
                _queueLevelUpAfterAim = true;
                return;
            }

            if (CurrentState != PinballState.LevelUp && CurrentState != PinballState.PaddleSelect)
                StartNextLevelUp();
        }
    }

    private void HandlePaddles()
    {
        if (leftPaddle != null)
        {
            leftPaddle.PaddleMovement(Input.GetKey(leftPaddleKey));
            var elem = leftPaddle.GetComponent<PaddleElementalState>();
            bool hasElem = elem != null && elem.CurrentState != PaddleState.None;

            if (hasElem && Input.GetKeyDown(leftPaddleKey))
                HitCheck(leftPaddleKey);

            var targetBall = GetPaddleTarget();
            if (hasElem && targetBall != null && targetBall.IsTouchingPaddles &&
                (Input.GetKey(leftPaddleKey) || canHitL) && !hasBeenHitL)
            {
                hasBeenHitL = true;
                StartCoroutine(ResetLBumper(0.4f));
                targetBall.OnPaddleHit(elem.GetEffectData());
            }
        }

        if (rightPaddle != null)
        {
            rightPaddle.PaddleMovement(Input.GetKey(rightPaddleKey));
            var elem = rightPaddle.GetComponent<PaddleElementalState>();
            bool hasElem = elem != null && elem.CurrentState != PaddleState.None;

            if (hasElem && Input.GetKeyDown(rightPaddleKey))
                HitCheck(rightPaddleKey);

            var targetBall = GetPaddleTarget();
            if (hasElem && targetBall != null && targetBall.IsTouchingPaddles &&
                (Input.GetKey(rightPaddleKey) || canHitR) && !hasBeenHitR)
            {
                hasBeenHitR = true;
                StartCoroutine(ResetRBumper(0.4f));
                targetBall.OnPaddleHit(elem.GetEffectData());
            }
        }
    }

    private void ClampBaseMultipliers()
    {
        if (!Mathf.Approximately(baseXPMult, lastBaseXPMultApplied))
        {
            float gap = xpMultiplier - lastBaseXPMultApplied;
            xpMultiplier = Mathf.Max(baseXPMult, baseXPMult + gap);
            lastBaseXPMultApplied = baseXPMult;
        }
        else if (xpMultiplier < baseXPMult)
        {
            xpMultiplier = baseXPMult;
        }

        if (!Mathf.Approximately(baseScoreMult, lastBaseScoreMultApplied))
        {
            float gapS = scoreMultiplier - lastBaseScoreMultApplied;
            scoreMultiplier = Mathf.Max(baseScoreMult, baseScoreMult + gapS);
            lastBaseScoreMultApplied = baseScoreMult;
        }
        else if (scoreMultiplier < baseScoreMult)
        {
            scoreMultiplier = baseScoreMult;
        }

        // Round floors and active multipliers (global rounding control)
        baseXPMult = Round2(baseXPMult);
        baseScoreMult = Round2(baseScoreMult);
        xpMultiplier = Round2(xpMultiplier);
        scoreMultiplier = Round2(scoreMultiplier);
    }

    private void MaybeEnableInvisibleWalls()
    {
        if (!wallTimerStart) return;
        wallTimer += Time.deltaTime;
        if (wallTimer >= 0.2f)
            EnableInvisibleWalls();
    }

    private void HandleGameOverRestart()
    {
        if (currentState == PinballState.GameOver && Input.GetKey(chargeKey))
            SceneManager.LoadScene(0);
    }

    private void UpdateMultipliersTimers()
    {
        if (IsScoreMultiplierActive && scoreBonusTimer > 0f)
        {
            scoreBonusTimer -= Time.unscaledDeltaTime;
            if (scoreBonusTimer <= 0f)
                scoreBonusTimer = 0f;
        }

        if (IsXPMultiplierActive && xpBonusTimer > 0f)
        {
            xpBonusTimer -= Time.unscaledDeltaTime;
            if (xpBonusTimer <= 0f)
                xpBonusTimer = 0f;
        }
    }

    private void HandleChargingAndPush()
    {
        if (Input.GetKey(chargeKey))
            isHoldingCharge = true;

        chargePercentage = Mathf.Min(1f, chargeMax > 0f ? (chargeTimer / chargeMax) : 0f);

        if (Input.GetKeyUp(chargeKey))
        {
            isHoldingCharge = false;

            if (chargePercentage > minChargeToLaunch)
            {
                if (currentState == PinballState.Charging)
                {
                    ball?.Launch(chargePercentage);
                    if (chargePercentage > chargeToEnterPush)
                        ChangeState(PinballState.Push);
                }
                else if (currentState == PinballState.Push)
                {
                    if (ball != null && ball.IsInLaunchTube)
                    {
                        ball.Push(chargePercentage);
                        ChangeState(PinballState.Play);
                    }
                }
            }
        }

        if (currentState == PinballState.Push && (ball == null || (!ball.IsInLaunchTube && ball.GetComponent<Rigidbody>()?.velocity.z < 0f)))
        {
            ChangeState(PinballState.Charging);
        }
    }

    private void TickChargeTimer()
    {
        if (!isHoldingCharge)
        {
            chargeTimer -= Time.deltaTime;
            if (chargeTimer < 0f) chargeTimer = 0f;
        }
        else
        {
            chargeTimer += Time.deltaTime;
            if (chargeTimer > chargeMax) chargeTimer = chargeMax;
        }
    }

    private void HandlePlayStateDrain()
    {
        bool any = HasAnyUsableBalls() || IsBallUsable(ball);

        if (!any)
        {
            _noBallTimer += Time.unscaledDeltaTime;
            if (_noBallTimer >= noBallResetGrace)
            {
                ChangeState(PinballState.ResetBall);
            }
        }
        else
        {
            _noBallTimer = 0f;
        }
    }

    public void DisableBall(Ball ball)
    {
        var go = ball.gameObject;
        go.SetActive(false);
        ballCount--;
    }
    #endregion

    #region Ball registry
    public void RegisterBall(Ball b)
    {
        if (b != null && !liveBalls.Contains(b))
            liveBalls.Add(b);
    }

    public void UnregisterBall(Ball b)
    {
        if (b != null)
            liveBalls.Remove(b);
    }

    private static bool IsBallUsable(Ball b)
    {
        return b != null && b.isActiveAndEnabled && b.gameObject.activeInHierarchy && b.IsActive;
    }

    private IEnumerable<Ball> GetUsableBalls()
    {
        for (int i = liveBalls.Count - 1; i >= 0; i--)
        {
            var b = liveBalls[i];
            if (b == null)
            {
                liveBalls.RemoveAt(i);
                continue;
            }
            if (IsBallUsable(b)) yield return b;
        }
    }

    private void ForEachUsableBall(System.Action<Ball> action)
    {
        foreach (var b in GetUsableBalls())
            action(b);
    }

    private bool TryGetAnchorBall(out Ball anchor)
    {
        for (int i = 0; i < liveBalls.Count; i++)
        {
            var candidate = liveBalls[i];
            if (candidate == null) { liveBalls.RemoveAt(i); i--; continue; }
            if (IsBallUsable(candidate)) { anchor = candidate; return true; }
        }
        anchor = null;
        return false;
    }

    private Ball GetPaddleTarget()
    {
        for (int i = 0; i < liveBalls.Count; i++)
        {
            var b = liveBalls[i];
            if (b == null) { liveBalls.RemoveAt(i); i--; continue; }
            if (IsBallUsable(b) && b.IsTouchingPaddles)
                return b;
        }
        return TryGetAnchorBall(out var anchor) ? anchor : null;
    }

    private void EnsurePrimaryBallRef()
    {
        if (ball != null && ball.GetInstanceID() == _primaryBallId)
            return;

        for (int i = 0; i < liveBalls.Count; i++)
        {
            var b = liveBalls[i];
            if (b != null && b.GetInstanceID() == _primaryBallId)
            {
                ball = b;
                return;
            }
        }

        if (ball == null && liveBalls.Count > 0 && liveBalls[0] != null)
        {
            ball = liveBalls[0];
            _primaryBallId = ball.GetInstanceID();
        }
    }

    private void FreezeAndTeleportToStart(Ball target)
    {
        if (target == null) return;
        var t = target.transform;
        var rb = target.GetComponent<Rigidbody>();

        DOTween.Kill(t, false);

        if (rb != null)
        {
            rb.isKinematic = true;
            rb.velocity = Vector3.zero;
            rb.position = _ballStartPos;
            rb.rotation = _ballStartRot;
            rb.Sleep();
            rb.isKinematic = false;
        }
        else
        {
            t.position = _ballStartPos;
            t.rotation = _ballStartRot;
        }
        target.ResetRb();
    }
    #endregion

    #region Walls
    public void EnableInvisibleWalls()
    {
        if (invisWalls) invisWalls.SetActive(true);
    }

    public void DisableInvisibleWalls()
    {
        if (invisWalls) invisWalls.SetActive(false);
        wallTimer = 0f;
        wallTimerStart = false;
    }
    #endregion

    #region Ball duplication
    public void DupeBall()
    {
        if (!TryGetAnchorBall(out var anchor))
        {
            if (IsBallUsable(ball)) anchor = ball;
        }
        if (anchor == null || ballClonePrefab == null)
            return;

        var dupedBallGO = Instantiate(ballClonePrefab, anchor.transform.position, Quaternion.identity);
        var dupedBall = dupedBallGO.GetComponent<Ball>();
        if (dupedBall != null)
        {
            var anchorCol = anchor.GetComponent<Collider>();
            var dupedCol = dupedBall.GetComponent<Collider>();
            if (anchorCol != null && dupedCol != null && anchorCol.material != null)
                dupedCol.material = Instantiate(anchorCol.material);

            dupedBall.CopyFrom(anchor);
            dupedBall.ResetRb();
            dupedBall.RandomizeGlowColor();
        }
        ballCount++;
    }

    private void MergeAnchorFromActiveClone()
    {
        if (ball == null) return;
        foreach (var b in GetUsableBalls())
        {
            if (b != null && b != ball)
            {
                ball.CopyFrom(b);
                break;
            }
        }
    }
    #endregion

    #region Score / XP
    public void AddScore(int gameScore, int bumpCount, int bumpCountConsec, float damageFactor, int ScoreMultPortal = 1)
    {
        // Multipliers already rounded to 2 decimals; final points consistent.
        int finalPoints = Mathf.RoundToInt(gameScore * scoreMultiplier);
        finalPoints = Mathf.RoundToInt(finalPoints * ScoreMultPortal);

        if (extraHitsS)
        {
            bumperBouncesS++;
            if (bumperBouncesS > bouncesForBonusS)
            {
                finalPoints = Mathf.RoundToInt(finalPoints * bonusHitsS);
                bumperBouncesS = 0;
            }
        }
        if (extraHitsG)
        {
            bumperBouncesG++;
            if (bumperBouncesG > bouncesForBonusG)
            {
                xpCount = Mathf.RoundToInt(xpCount * bonusHitsG);
                bumperBouncesG = -1;
            }
            if (bumperBouncesG == 0)
                xpCount = Mathf.RoundToInt(xpCount / bonusHitsG);
        }

        if (destroyedBumperBonusActive)
        {
            score += Mathf.RoundToInt(finalPoints * destroyedBumperScoreMult);
            destroyedBumperBonusActive = false;
        }
        else score += finalPoints;

        ui?.UpdateScore(score, bumpCount, bumpCountConsec);
    }

    private bool HasAnyUsableBalls()
    {
        foreach (var _ in GetUsableBalls()) return true;
        if (IsBallUsable(ball))
        {
            if (!liveBalls.Contains(ball))
                RegisterBall(ball);
            return true;
        }
        return false;
    }

    private void ResetBallAndFlow()
    {
        EnsurePrimaryBallRef();
        _noBallTimer = 0f;
        MergeAnchorFromActiveClone();
        DisableInvisibleWalls();

        for (int i = liveBalls.Count - 1; i >= 0; i--)
        {
            var b = liveBalls[i];
            if (b == null) { liveBalls.RemoveAt(i); continue; }
            if (b != ball)
            {
                liveBalls.RemoveAt(i);
                Destroy(b.gameObject);
            }
        }

        if (ball != null)
        {
            ball.gameObject.SetActive(true);
            var rb = ball.GetComponent<Rigidbody>();
            if (rb != null)
            {
                rb.velocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
            }
            FreezeAndTeleportToStart(ball);
            if (!liveBalls.Contains(ball)) RegisterBall(ball);
        }

        if (portalResetDebugCooldown > 0f)
        {
            var portalRuntime = FindFirstObjectByType<PortalWarpRewardRuntime>();
            portalRuntime?.ForceGlobalCooldown(portalResetDebugCooldown);
            var grenadeRuntime = FindFirstObjectByType<GrenadeRewardRuntime>();
            grenadeRuntime?.ForceGlobalCooldown(portalResetDebugCooldown);
        }

        isHoldingCharge = false;
        chargeTimer = 0f;
        chargePercentage = 0f;

        ballCount = 1;

        ChangeState(lives > 0 ? PinballState.Charging : PinballState.GameOver);
    }

    public void AddXP(float xp)
    {
        finalXPCalculated = xp * xpMultiplier;
        curXP += finalXPCalculated;
        ballXPScript?.UpdateXP(Mathf.RoundToInt(curXP), Mathf.RoundToInt(maxXP), level);
    }

    public void SpawnXP(Vector3 pos, bool isDead, bool isTakingElemDamage, float damageFactor, int mult = 1)
    {
        if (!xpFXPrefab) return;

        int finalXP = Mathf.RoundToInt(xpCount * Mathf.Max(1f, damageFactor * .35f));
        finalXP *= Mathf.Max(1, mult);

        Vector3 position = new Vector3(pos.x, pos.y + 1f, pos.z);
        var xpFX = Instantiate(xpFXPrefab, position, xpFXPrefab.transform.rotation);

        int emitted;
        if (isDead) emitted = finalXP * 2;
        else if (isTakingElemDamage) emitted = Mathf.RoundToInt(finalXP / 3f);
        else emitted = finalXP;

        xpFX.Emit(emitted);
    }

    public void SpawnBonusWaterXP(Vector3 pos, float waterBonusXP, float damageFactor, int mult = 1)
    {
        if (!xpFXPrefab) return;

        int baseXP = Mathf.RoundToInt(xpCount * Mathf.Max(1f, damageFactor));
        baseXP *= Mathf.Max(1, mult);

        float bonus = Mathf.Max(0f, waterBonusXP) / 100f;
        int emitted = Mathf.RoundToInt(baseXP * (1f + bonus));

        Vector3 position = new Vector3(pos.x, pos.y + 1f, pos.z);
        var xpFX = Instantiate(xpFXPrefab, position, xpFXPrefab.transform.rotation);
        xpFX.Emit(emitted);
    }

    public void SpawnBonusEarthXP(Vector3 pos, float earthBonusXP, float damageFactor, int mult = 1)
    {
        if (!xpFXPrefab) return;

        int baseXP = Mathf.RoundToInt(xpCount * Mathf.Max(1f, damageFactor));
        baseXP *= Mathf.Max(1, mult);

        float bonus = Mathf.Max(0f, earthBonusXP) / 100f;
        int emitted = Mathf.RoundToInt(baseXP * (1f + bonus));

        Vector3 position = new Vector3(pos.x, pos.y + 1f, pos.z);
        var xpFX = Instantiate(xpFXPrefab, position, xpFXPrefab.transform.rotation);
        xpFX.Emit(emitted);
    }

    private void StartNextLevelUp()
    {
        if (pendingLevelUps <= 0) return;
        // Defer LevelUp if player is aiming a skill shot
        if (CurrentState == PinballState.SkillAim)
        {
            _queueLevelUpAfterAim = true;
            return;
        }
        ChangeState(PinballState.LevelUp);
    }

    public void LevelUp()
    {
        level++;
        maxXP = XPFormula.XpReq(level);
        ballXPScript?.UpdateXP(curXP, maxXP, level);
        ApplyLevelBonuses();
    }

    private void ShowNextLevelUpOrResume()
    {
        if (pendingLevelUps > 0)
        {
            LevelUp();
            var choices = GetRewardChoices();
            ui?.ShowRewardPopup(choices);
            Time.timeScale = 0f;
        }
        else
        {
            // Resume to Play with a short slow‑mo ramp using the TimeScaleHub (safe).
            StartCoroutine(ResumeFromLevelUpFlow());
        }
    }

    private IEnumerator ResumeFromLevelUpFlow()
    {
        ChangeState(PinballState.Play);
        // One frame to ensure UI/pause fully released
        yield return null;
        StartPostLevelUpSlowMo();
    }

    private void StartPostLevelUpSlowMo()
    {
        if (!enableResumeSlowMo) return;

        _resumeSlowmoTween?.Kill(false);

        float start = Mathf.Clamp(resumeSlowMoStartScale, 0.05f, 1f);
        // Begin strong slow-mo
        TimeScaleHub.Begin(_postLevelUpSlowmoToken, start, affectFixedDelta: true);

        var seq = DOTween.Sequence().SetUpdate(true);
        if (resumeSlowMoHold > 0f)
            seq.AppendInterval(resumeSlowMoHold);

        // Ease back to normal (1.0) and release the token
        seq.Append(DOTween.To(() => start, s =>
        {
            // Update current slow-mo scale through the hub (min-of-active is enforced there)
            TimeScaleHub.Begin(_postLevelUpSlowmoToken, s, affectFixedDelta: true);
        }, 1f, resumeSlowMoEase).SetEase(Ease.OutQuad))
        .OnComplete(() =>
        {
            TimeScaleHub.End(_postLevelUpSlowmoToken);
        });

        _resumeSlowmoTween = seq;
    }

    public void ApplyLevelBonuses()
    {
        if (level % 5 == 0)
        {
            baseScoreMult += 0.5f;
            baseXPMult += 0.5f;
        }
        else
        {
            baseScoreMult += 0.15f;
            baseXPMult += 0.15f;
        }
        baseScoreMult = Round2(baseScoreMult);
        baseXPMult = Round2(baseXPMult);
        lastBaseScoreMultApplied = baseScoreMult;
        lastBaseXPMultApplied = baseXPMult;
        // Keep active multipliers >= floors and rounded
        scoreMultiplier = Round2(Mathf.Max(scoreMultiplier, baseScoreMult));
        xpMultiplier = Round2(Mathf.Max(xpMultiplier, baseXPMult));
    }
    #endregion

    #region Green Pad Skill Aim
    public void EnterGreenPadAim(Ball ball, Transform focus, float slowMo, float windowUnscaled, float minSpeed, float maxSpeed, float nextHitFactor, int nextHitBounces)
    {
        var req = new SkillAimRequest(
            ball,
            focus,
            Mathf.Clamp(slowMo, 0.05f, 0.5f),
            Mathf.Max(0.15f, windowUnscaled),
            Mathf.Max(1f, minSpeed),
            Mathf.Max(Mathf.Max(1f, minSpeed), maxSpeed),
            Mathf.Max(1f, nextHitFactor),
            Mathf.Max(1, nextHitBounces)
        );

        if (CurrentState == PinballState.SkillAim || _aimQueue.Count > 0)
        {
            if (_aimBall == ball) return;
            if (_aimQueue.Any(r => r.ball == ball)) return;
            _aimQueue.Enqueue(req);
            return;
        }

        StartSkillAim(req);
    }

    private void StartSkillAim(SkillAimRequest req)
    {
        if (!req.ball) return;

        // Ensure no leftover portal slow-mo interferes with SkillAim
        CancelAllPortalSlowmo();

        // (existing)
        HardResetCameraToDefaults(true);
        currentState = PinballState.SkillAim;

        _aimBall = req.ball;
        _aimFocus = req.focus;
        _aimWindowDur = req.windowUnscaled;
        _aimTimeLeft = _aimWindowDur;
        _aimMinSpeed = req.minSpeed;
        _aimMaxSpeed = req.maxSpeed;
        _targetSlowMo = req.slowMo;
        _nextHitFactor = req.nextHitFactor;
        _nextHitBounces = req.nextHitBounces;
        _lastAimDir = _aimBall ? _aimBall.transform.forward : Vector3.forward;

        // Keep portals on cooldown for the duration of the aim window (prevents edge cases)
        var portalRuntime = FindFirstObjectByType<PortalWarpRewardRuntime>();
        if (portalRuntime)
            portalRuntime.ForceGlobalCooldown(_aimWindowDur * 0.20f);

        if (!mainCam) mainCam = Camera.main;

        _camDistTween?.Kill(false);
        _camPosTween?.Kill(false);
        _camRotTween?.Kill(false);

        _preAimCamPos = mainCam ? mainCam.transform.position : _preAimCamPos;
        _preAimCamRot = mainCam ? mainCam.transform.rotation : _preAimCamRot;

        TimeScaleHub.Begin(_skillAimSlowmoToken, _targetSlowMo, true);

        if (mainCam)
        {
            _fovTween?.Kill(false);
            _preAimFov = mainCam.fieldOfView;
            _fovTween = DOTween.To(() => mainCam.fieldOfView, v => mainCam.fieldOfView = v, camAimFov, camFovTween).SetUpdate(true);
        }

        if (camFollow && _aimBall)
        {
            _preAimCamTarget = camFollow.Target;
            _preAimFollowDist = camFollow.FollowDistance;

            if (_aimCamTarget == null) _aimCamTarget = new GameObject("AimCamTarget").transform;
            _aimCamTarget.SetParent(_aimBall.transform, false);
            _aimCamTarget.localPosition = new Vector3(0f, 0.4f, 0f);
            _aimCamTarget.localRotation = Quaternion.identity;

            camFollow.Target = _aimCamTarget;
            camFollow.SnapToTarget();
            _camDistTween = DOTween
                .To(() => camFollow.FollowDistance, v => camFollow.FollowDistance = v, aimFollowDistance, aimFollowTween)
                .SetUpdate(true);
        }

        if (postFX)
        {
            postFX.VignetteMax = vignetteMax;
            postFX.SetVignette(0f);
            postFX.FadeVignette(0.25f, 0.08f);
        }

        var rb = _aimBall.GetComponent<Rigidbody>();
        if (rb) rb.velocity *= .5f;

        if (aimLine)
        {
            aimLine.enabled = true;
            aimLine.positionCount = 2;
            var p = _aimBall.transform.position;
            aimLine.SetPosition(0, p);
            aimLine.SetPosition(1, p);
        }
    }

    private void UpdateSkillAim()
    {
        if (CurrentState != PinballState.SkillAim || _aimBall == null) return;

        _aimTimeLeft -= Time.unscaledDeltaTime;
        var rb = _aimBall.GetComponent<Rigidbody>();
        Vector3 ballPos = _aimBall.transform.position;

        Vector3 aimDir = Vector3.zero;

        if (mainCam)
        {
            Ray ray = mainCam.ScreenPointToRay(Input.mousePosition);
            var plane = new Plane(Vector3.up, new Vector3(0, ballPos.y, 0));
            if (plane.Raycast(ray, out float dist))
            {
                Vector3 hit = ray.GetPoint(dist);
                Vector3 delta = (hit - ballPos);
                delta.y = 0f;
                if (delta.sqrMagnitude > 0.0001f) aimDir = delta.normalized;
            }
        }

        Vector3 dir = (aimDir.sqrMagnitude > 1e-4f ? aimDir : _lastAimDir).normalized;
        _lastAimDir = dir;

        float x = Input.GetAxisRaw("Horizontal");
        float z = Input.GetAxisRaw("Vertical");
        Vector3 stick = new Vector3(x, 0f, z);
        if (stick.sqrMagnitude > 0.0001f) aimDir = stick.normalized;

        bool charging = Input.GetMouseButton(0) || Input.GetKey(KeyCode.JoystickButton0);
        float held = Mathf.Clamp01(1f - (_aimTimeLeft / Mathf.Max(0.0001f, _aimWindowDur)));
        float power = charging ? held : held * 0.75f;

        if (postFX)
        {
            float frac = 1f - Mathf.Clamp01(_aimTimeLeft / Mathf.Max(0.0001f, _aimWindowDur));
            postFX.SetVignette(Mathf.SmoothStep(0f, 1f, frac));
        }

        float projectedSpeed = Mathf.Lerp(_aimMinSpeed, _aimMaxSpeed, power);
        float desiredLen = projectedSpeed * 0.05f * Mathf.Clamp01(power);
        float length = Mathf.Min(aimLineMaxLen, desiredLen);
        length = Mathf.Max(length, 0.05f * aimLineMaxLen);

        Vector3 origin = ballPos;
        Vector3 tip = origin + dir * length;

        if (aimLine)
        {
            aimLine.positionCount = 2;
            aimLine.SetPosition(0, origin);
            aimLine.SetPosition(1, tip);
        }

        bool release = Input.GetMouseButtonUp(0) || Input.GetKeyUp(KeyCode.JoystickButton0);
        bool timedOut = _aimTimeLeft <= 0f;

        if (release || timedOut)
        {
            if (aimDir.sqrMagnitude < 0.001f)
                aimDir = (_aimFocus ? (_aimBall.transform.position - _aimFocus.position) : _aimBall.transform.forward);
            aimDir.y = 0f;
            if (aimDir.sqrMagnitude < 0.001f) aimDir = Vector3.forward;
            aimDir.Normalize();

            float speed = Mathf.Lerp(_aimMinSpeed, _aimMaxSpeed, power);
            _aimBall.AddTempDamageForBounce(_nextHitFactor, _nextHitBounces);

            if (rb)
            {
                rb.velocity = Vector3.zero;
                rb.AddForce(aimDir * speed, ForceMode.VelocityChange);
            }

            if (postFX) postFX.ChromaticPulse(0.25f, 0.06f, 0.14f);
            ScreenShake();

            if (_aimBall)
                _aimBall.TemporarilyUncapMaxSpeed(SkillAimUncapDuration, SkillAimUncapMaxSpeed, true);

            ExitGreenPadAim();
        }
    }

    private void ExitGreenPadAim()
    {
        RestoreFromSkillAim();

        while (_aimQueue.Count > 0)
        {
            var next = _aimQueue.Dequeue();
            if (next.ball && next.ball.isActiveAndEnabled && next.ball.IsActive)
            {
                StartSkillAim(next);
                return;
            }
        }

        currentState = PinballState.Play;

        // If leveling is queued or still pending, show shortly after aim completes.
        if (_queueLevelUpAfterAim || pendingLevelUps > 0)
            StartCoroutine(InvokeLevelUpAfterSkillAim());
    }

    private IEnumerator InvokeLevelUpAfterSkillAim()
    {
        _queueLevelUpAfterAim = false;
        yield return new WaitForSecondsRealtime(Mathf.Max(0.01f, postAimLevelUpDelay));
        if (pendingLevelUps > 0 && CurrentState == PinballState.Play)
            StartNextLevelUp();
    }

    private void RestoreFromSkillAim(bool tweenCamFollowDist = true)
    {
        TimeScaleHub.End(_skillAimSlowmoToken);
        HardResetCameraToDefaults(true);

        if (aimLine)
        {
            aimLine.enabled = false;
            aimLine.positionCount = 0;
        }

        _aimBall = null;
        _aimFocus = null;
        _aimTimeLeft = 0f;
        Time.fixedDeltaTime = _defaultFixedDeltaTime;
    }
    #endregion

    #region Paddle elemental windows
    public bool AreBothPaddlesElemental()
    {
        var leftElem = leftPaddle ? leftPaddle.GetComponent<PaddleElementalState>() : null;
        var rightElem = rightPaddle ? rightPaddle.GetComponent<PaddleElementalState>() : null;

        bool leftHas = leftElem != null && leftElem.CurrentState != PaddleState.None;
        bool rightHas = rightElem != null && rightElem.CurrentState != PaddleState.None;
        return leftHas && rightHas;
    }

    private void HitCheck(KeyCode paddle)
    {
        if (paddle == leftPaddleKey) StartCoroutine(RegCheck(leftPaddleKey));
        else StartCoroutine(RegCheck(rightPaddleKey));
    }

    private IEnumerator RegCheck(KeyCode paddle)
    {
        if (paddle == leftPaddleKey) canHitL = true; else canHitR = true;
        yield return new WaitForSeconds(0.6f);
        if (paddle == leftPaddleKey) canHitL = false; else canHitR = false;
    }

    private IEnumerator ResetLBumper(float cd) { yield return new WaitForSeconds(cd); hasBeenHitL = false; }
    private IEnumerator ResetRBumper(float cd) { yield return new WaitForSeconds(cd); hasBeenHitR = false; }
    #endregion

    #region Camera shake
    public void ScreenShake()
    {
        if (mainCam == null) return;
        if (camFollow != null)
            camFollow.StartShake(shakeDuration, shakeStrength, shakeVibrato, shakeRandomness);
    }

    public void ScreenShakeGrenade()
    {
        if (mainCam == null || camFollow == null) return;
        // Roughly 1.6x strength/duration, tighter vibrato
        camFollow.StartShake(shakeDuration * 1.6f, shakeStrength * 1.75f, Mathf.Max(8, shakeVibrato - 2), shakeRandomness);
    }

    #endregion

    #region Bumper respawn
    public IEnumerator RespawnRoutine(Bumper bumper)
    {
        if (!bumper || !ball) yield break;

        float f = explosionForce;
        float r = explosionRadius;
        if (bumper.type == BumperType.Small) { f *= 0.5f; r *= 0.5f; }
        else if (bumper.type == BumperType.Large) { f *= 1.5f; r *= 1.5f; }

        var rb = ball.GetComponent<Rigidbody>();
        if (rb != null)
            rb.AddExplosionForce(f, bumper.transform.position, r, 0f, ForceMode.Impulse);

        var col = bumper.GetComponent<Collider>();
        var mr = bumper.GetComponent<MeshRenderer>();
        if (col) col.enabled = false;
        if (mr) mr.enabled = false;

        yield return new WaitForSeconds(bumper.cooldown);

        bumper.Revive();
        bumper.gameObject.SetActive(true);
        if (col) col.enabled = true;
        if (mr) mr.enabled = true;
    }
    #endregion

    #region Reward application (IRunContext)
    public void ApplyXPMultiplier(float multiplier, bool cursed)
    {
        if (cursed)
        {
            xpCount += 1;
            if (xpBonusTimer > 5) xpBonusTimer = 5;
            xpMultiplier *= 2f;
        }
        else
        {
            float pct = multiplier >= 1f ? multiplier * 0.01f : multiplier;
            xpMultiplier *= (1f + pct);
        }
        xpMultiplier = Round2(xpMultiplier);
    }

    public void ApplyXPBonusTime(float time, bool cursed)
    {
        if (!IsXPMultiplierActive) return;
        if (cursed) { if (xpBonusTimer > 3) xpBonusTimer = 3; }
        else
        {
            xpBonusTimer += time;
            if (xpBonusTimer > 30) xpBonusTimer = 30f;
        }
    }

    public void ApplyScoreMultiplier(float multiplier, bool cursed)
    {
        if (Mathf.Approximately(multiplier, X2_MULT_MAGIC))
        {
            scoreMultiplier *= 2f;
        }
        else if (cursed)
        {
            scoreMultiplier *= 4f;
        }
        else
        {
            float pct = multiplier >= 1f ? multiplier * 0.01f : multiplier;
            scoreMultiplier *= (1f + pct);
        }
        scoreMultiplier = Round2(scoreMultiplier);
    }

    public void ApplyScoreBonusTime(float time, bool cursed)
    {
        if (!IsScoreMultiplierActive) return;
        if (cursed) scoreBonusTimer *= 0.1f;
        else scoreBonusTimer += time;
    }

    public void ApplyShrinkFX(float size, float speed, float bounciness, float scoreMult, float bonusBounces, int bounces, bool bonus, bool cursed)
    {
        float Size = (100f + size) / 100f;
        float Speed = (100f + speed) / 1000f;
        float Mult = (100f + scoreMult) / 100f;
        float Bounciness = ((100f * bounciness) / 10000f);

        if (scoreMult != 0)
        {
            baseScoreMult *= Mult;
            baseScoreMult = Round2(baseScoreMult);
            scoreMultiplier = Round2(scoreMultiplier);
        }

        if (bonus)
        {
            extraHitsS = true;
            bouncesForBonusS = bounces;
            bonusHitsS = bonusBounces;
        }

        ForEachUsableBall(b =>
        {
            if (bounciness != 0) b.AdjustBounciness(1f + Bounciness);
            if (size != 0) b.transform.localScale *= Size;
            if (speed != 0) b.maxSpeed *= 1f + Speed;
        });
    }

    public void ApplyGrowFX(float size, float speed, float bounciness, float xpMult, float bonusBounces, int bounces, bool bonus, bool cursed)
    {
        float Size = (100f + size) / 100f;
        float Speed = (100f + speed) / 1000f;
        float Mult = (100f + xpMult) / 100f;
        float Bounciness = ((100f * bounciness) / 10000f);

        if (xpMult != 0)
        {
            baseXPMult *= Mult;
            baseXPMult = Round2(baseXPMult);
            xpMultiplier = Round2(xpMultiplier);
        }

        if (bonus)
        {
            extraHitsG = true;
            bouncesForBonusG = bounces;
            bonusHitsG = bonusBounces;
        }

        ForEachUsableBall(b =>
        {
            if (bounciness != 0) b.AdjustBounciness(1f + Bounciness);
            if (size != 0) b.transform.localScale *= Size;
            if (speed != 0) b.maxSpeed *= 1f - Speed;
        });
    }

    public void ApplyXPForcefield(float amount)
    {
        float deltaScale = (100f + amount) / 100f;
        xpBaseRadiusScale *= Mathf.Max(0.0001f, deltaScale);
        RefreshAllBallForcefields();
    }

    public void RefreshAllBallForcefields()
    {
        ForEachUsableBall(b => b.RefreshForcefieldFromContext());
    }

    public void ApplyAdditionalBalls(int additionalBalls)
    {
        if (additionalBalls != 100)
        {
            for (int i = 0; i < additionalBalls; i++) { DupeBall(); }
        }
        else
        {
            int curBallCount = ballCount;
            for (int i = 0; i < curBallCount; i++) { DupeBall(); }
        }
    }

    public void ApplyGrantedLives(int amount)
    {
        lives = Mathf.Clamp(lives + amount, 0, maxLives);
        OnLivesChanged();
    }

    public void ApplyDamageFX(float amount)
    {
        float Amount = 0.01f * amount;
        ForEachUsableBall(b => b.AddDamageMultiplier(Amount));
    }

    public void ApplyDmgPerBounceFX(float amount, int bounces)
    {
        float Amount = 0.01f * amount;
        ForEachUsableBall(b => b.AddTempDamageMultiplier(Amount, bounces));
    }

    public void SetPaddleState(bool isLeft)
    {
        var paddle = isLeft ? leftPaddle : rightPaddle;
        var paddleElem = paddle ? paddle.GetComponent<PaddleElementalState>() : null;

        if (paddleElem == null || pendingPaddleReward == null)
        {
            if (pendingLevelUps > 0)
            {
                var choices = GetRewardChoices();
                ui?.ShowRewardPopup(choices);
                ui?.ClosePaddleSelect(true);
            }
            else
            {
                ui?.ClosePaddleSelect(false);
                // Resume with slow‑mo ramp to avoid abrupt return
                StartCoroutine(ResumeFromLevelUpFlow());
            }
            return;
        }

        pendingPaddleReward.ApplyToPaddle(paddleElem);
        pendingPaddleReward = null;

        if (pendingLevelUps > 0)
        {
            var choices = GetRewardChoices();
            ui?.ShowRewardPopup(choices);
            ui?.ClosePaddleSelect(true);
        }
        else
        {
            ui?.ClosePaddleSelect(false);
            // Resume with slow‑mo ramp
            StartCoroutine(ResumeFromLevelUpFlow());
        }
    }
    #endregion

    #region Reward selection flow
    public void OnRewardChosen(RewardSO reward)
    {
        if (reward == null) { ChangeState(PinballState.Play); return; }

        if (reward.isPaddleReward)
        {
            var leftElem = leftPaddle ? leftPaddle.GetComponent<PaddleElementalState>() : null;
            var rightElem = rightPaddle ? rightPaddle.GetComponent<PaddleElementalState>() : null;

            bool leftHasElem = leftElem != null && leftElem.CurrentState != PaddleState.None;
            bool rightHasElem = rightElem != null && rightElem.CurrentState != PaddleState.None;

            pendingPaddleReward = reward;

            if (leftHasElem && !rightHasElem) { pendingLevelUps--; SetPaddleState(false); reward.Apply(this); return; }
            if (!leftHasElem && rightHasElem) { pendingLevelUps--; SetPaddleState(true); reward.Apply(this); return; }

            reward.Apply(this);
            pendingLevelUps--;
            ChangeState(PinballState.PaddleSelect);
            return;
        }

        if (reward.Scalable && reward.ReplacesReward != null)
        {
            var old = reward.ReplacesReward;
            if (Owns(old.Id))
            {
                active.Remove(old.Id);
                owned.Remove(old.Id);

                reward.Apply(this);
                pendingLevelUps--;
                ShowNextLevelUpOrResume(); return;
            }
        }

        reward.Apply(this);
        pendingLevelUps--;
        ShowNextLevelUpOrResume();
    }

    private RewardSO PickOneWeighted(List<RewardSO> pool, RewardRarity rarity)
    {
        var candidates = pool.Where(r => r.Rarity == rarity).ToList();
        if (candidates.Count == 0) return null;
        int index = UnityEngine.Random.Range(0, candidates.Count);
        return candidates[index];
    }

    private RewardRarity RollRarity()
    {
        float total = rarityWeights.Values.Sum();
        float roll = UnityEngine.Random.Range(0f, total);

        float cumulative = 0f;
        foreach (var kv in rarityWeights)
        {
            cumulative += kv.Value;
            if (roll <= cumulative) return kv.Key;
        }
        return RewardRarity.Common;
    }

    private List<RewardSO> GetRewardChoices()
    {
        var eligible = rewardPool.Where(r => r.IsEligible(this)).ToList();
        if (eligible.Count == 0) return new List<RewardSO>();

        var picks = new List<RewardSO>();
        var localEligible = new List<RewardSO>(eligible);

        for (int i = 0; i < 4; i++)
        {
            RewardSO choice = null;
            for (int j = 0; j < 10 && choice == null; j++)
            {
                var tier = RollRarity();
                choice = PickOneWeighted(localEligible, tier);
            }
            if (choice == null) break;
            picks.Add(choice);
            localEligible.Remove(choice);
            if (localEligible.Count == 0) break;
        }
        return picks;
    }
    #endregion

    #region Timescale / camera utilities
    private void NormalizeTimeScaleIfNeeded()
    {
        if (Time.timeScale < 0.99f)
        {
            Time.timeScale = 1f;
            Time.fixedDeltaTime = _defaultFixedDeltaTime;
        }
    }

    private void HardResetCameraToDefaults(bool snapPositionRotation = true)
    {
        _camDistTween?.Kill(false); _camDistTween = null;
        _camPosTween?.Kill(false); _camPosTween = null;
        _camRotTween?.Kill(false); _camRotTween = null;
        _fovTween?.Kill(false); _fovTween = null;

        if (_aimCamTarget != null)
        {
            Destroy(_aimCamTarget.gameObject);
            _aimCamTarget = null;
        }

        if (camFollow)
        {
            camFollow.Target = _camDefaultTarget;
            camFollow.FollowDistance = _camDefaultFollowDist;
            camFollow.Height = _camDefaultHeight;

            if (snapPositionRotation)
            {
                if (camFollow.Target != null)
                {
                    camFollow.SnapToTarget();
                }
                else if (mainCam)
                {
                    mainCam.transform.SetPositionAndRotation(_camDefaultCamPos, _camDefaultCamRot);
                }
            }
        }

        if (mainCam) mainCam.fieldOfView = _camDefaultFov;
        postFX?.ClearVignette(0f);
    }

    private void CancelAllPortalSlowmo()
    {
        var runtime = FindFirstObjectByType<PortalWarpRewardRuntime>();
        runtime?.CancelAllSlowmo();
        NormalizeTimeScaleIfNeeded();
    }

    private void EnsureNormalTimescaleIfNotAiming()
    {
        if (currentState == PinballState.SkillAim ||
            currentState == PinballState.LevelUp ||
            currentState == PinballState.PaddleSelect)
            return;

        if (TimeScaleHub.IsPaused)
            return;

        if (TimeScaleHub.IsAnyActive)
            return;

        if (Time.timeScale < 0.999f)
        {
            Time.timeScale = 1f;
            Time.fixedDeltaTime = _defaultFixedDeltaTime;
        }
    }
    #endregion
}