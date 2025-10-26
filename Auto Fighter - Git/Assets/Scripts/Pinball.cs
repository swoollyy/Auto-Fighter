using DG.Tweening;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.SceneManagement;

/// Pinball gameplay state machine
public enum PinballState
{
    None,
    Charging,   // player holds to charge the initial launch
    Push,       // short pre-launch "push" window inside the tube
    Play,       // normal play
    LevelUp,    // reward selection
    PaddleSelect,
    ResetBall,  // lose a ball, respawn or end
    GameOver
}

/// Central runtime manager for the pinball minigame.
/// Responsibilities:
/// - State machine (charging/push/play/level-up/respawn)
/// - Player input for paddles and launch
/// - Lives/score/xp and reward application
/// - Ball registry and duplication
/// - Camera shake and basic FX
[DisallowMultipleComponent]
public class Pinball : MonoBehaviour, IRunContext
{
    public static Pinball Instance { get; private set; }

    #region Reward/RunContext state (ownership, activation, exclusivity)

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

    [Header("References")]
    [SerializeField] public Ball ball;           // primary ball anchor (kept alive across respawns)
    [SerializeField] private PinballUIM ui;
    [SerializeField] private GameObject ballClonePrefab;
    [SerializeField] private GameObject invisWalls;
    [SerializeField] private PinballFlipper leftPaddle;
    [SerializeField] private PinballFlipper rightPaddle;

    [Header("Charge/Push Tuning")]
    [Tooltip("Max seconds of charge time for initial launch")]
    [SerializeField, Min(0.05f)] private float chargeMaxCharging = 1.5f;
    [Tooltip("Max seconds for secondary push window while in the tube")]
    [SerializeField, Min(0.05f)] private float chargeMaxPush = 1.0f;
    [Tooltip("Minimum fraction (0..1) of charge required to commit a launch")]
    [SerializeField, Range(0f, 1f)] private float minChargeToLaunch = 0.10f;
    [Tooltip("Threshold fraction (0..1) to transition into Push window after launch")]
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
    [Tooltip("If true, sets global Physics.gravity to a pinball-friendly value on Awake.")]
    [SerializeField] private bool overrideGlobalGravity = true;
    [Tooltip("Gravity used if 'overrideGlobalGravity' is enabled.")]
    [SerializeField] private Vector3 gravityOverride = new Vector3(0f, 0f, -19.62f);

    #endregion

    #region Runtime state

    private PinballState currentState;
    public PinballState CurrentState => currentState;

    // Charge state (transient)
    private bool isHoldingCharge;
    private float chargeTimer;
    public float chargePercentage { get; private set; } // exposed for UI meter
    private float chargeMax; // per-state (Charging/Push)

    // Score/XP & flow
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

    // Paddles elemental window
    private bool canHitL, canHitR, hasBeenHitL, hasBeenHitR;

    // Ball tracking
    private readonly List<Ball> liveBalls = new();
    private int _primaryBallId;
    public int ballCount { get; private set; }
    private int lives;

    // "No ball" debounce in Play
    [SerializeField, Min(0f)] private float noBallResetGrace = 0.20f;
    private float _noBallTimer;

    // Respawn positioning
    private Vector3 _ballStartPos;
    private Quaternion _ballStartRot;

    // Camera shake reset
    private Vector3 _camDefaultLocalPos;
    private Quaternion _camDefaultLocalRot;

    // Reward/Level-up
    private int pendingLevelUps = 0;
    private RewardSO pendingPaddleReward;
    private readonly List<RewardSO> rewardPool = new();

    // Misc gameplay
    public bool destroyedBumperBonusActive;
    private float destroyedBumperScoreMult = 2f;
    private int xpCount = 3;
    private bool wallTimerStart;
    private float wallTimer;

    // Extra “bonus hits” (from rewards)
    private int bumperBouncesS;
    private int bumperBouncesG;
    private bool extraHitsS;
    private bool extraHitsG;
    private float bonusHitsS;
    private float bonusHitsG;
    private int bouncesForBonusS;
    private int bouncesForBonusG;

    // Constants used by some rewards as magic values
    private const float X2_MULT_MAGIC = 100f;
    private const float X4_MULT_MAGIC = 200f;

    public float Damage = 5f;

    #endregion

    #region Unity lifecycle

    private void Awake()
    {
        if (Instance && Instance != this) { Destroy(gameObject); return; }
        Instance = this;

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

        if (mainCam != null)
        {
            _camDefaultLocalPos = mainCam.transform.localPosition;
            _camDefaultLocalRot = mainCam.transform.localRotation;
        }
    }

    private async void Start()
    {
        // Load all reward assets (label: "Rewards")
        var loader = Addressables.LoadAssetsAsync<RewardSO>("Rewards", rewardPool.Add);
        await loader.Task;

        ui?.InitLives(maxLives);
        OnLivesChanged();

        ChangeState(PinballState.Charging);

        ui?.Init(this);
        if (invisWalls) invisWalls.SetActive(false);

        ballCount = 1;
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
        ResetCameraShakeState();
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
        // Global tick
        HandlePendingLevelUps();
        HandlePaddles();
        ClampBaseMultipliers();
        MaybeEnableInvisibleWalls();
        HandleGameOverRestart();
        UpdateMultipliersTimers();

        // State ticks
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

        TickChargeTimer();
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
                break;

            case PinballState.Push:
                ResetChargeState(max: chargeMaxPush);
                break;

            case PinballState.Play:
                ui?.DefaultUI();
                chargePercentage = 0;
                chargeTimer = 0;
                Time.timeScale = 1f;
                wallTimerStart = true;
                break;

            case PinballState.LevelUp:
                LevelUp();
                var choices = GetRewardChoices();
                ui?.ShowRewardPopup(choices);
                Time.timeScale = 0f;
                break;

            case PinballState.PaddleSelect:
                ui?.PaddleSelect();
                Time.timeScale = 0f;
                break;

            case PinballState.ResetBall:
                lives = Mathf.Max(0, lives - 1);
                OnLivesChanged();
                ResetBallAndFlow();
                break;

            case PinballState.GameOver:
                leftPaddle = null;
                rightPaddle = null;
                break;
        }
    }

    private void ExitState(PinballState state)
    {
        switch (state)
        {
            case PinballState.LevelUp:
            case PinballState.PaddleSelect:
                Time.timeScale = 1f;
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

    #region Input/state helpers

    private void HandlePendingLevelUps()
    {
        while (curXP >= maxXP)
        {
            pendingLevelUps++;
            curXP -= maxXP;
        }

        if (pendingLevelUps > 0 && CurrentState != PinballState.LevelUp)
            StartNextLevelUp();
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
        if (xpMultiplier < baseXPMult) xpMultiplier = baseXPMult;
        if (scoreMultiplier < baseScoreMult) scoreMultiplier = baseScoreMult;
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
        // Score mult
        if (IsScoreMultiplierActive && scoreBonusTimer > 0f)
        {
            scoreBonusTimer -= Time.unscaledDeltaTime;
            if (scoreBonusTimer <= 0f)
            {
                scoreBonusTimer = 0f;
                scoreMultiplier = baseScoreMult;
            }
        }

        // XP mult
        if (IsXPMultiplierActive && xpBonusTimer > 0f)
        {
            xpBonusTimer -= Time.unscaledDeltaTime;
            if (xpBonusTimer <= 0f)
            {
                xpBonusTimer = 0f;
                xpMultiplier = baseXPMult;
            }
        }
    }

    /// Charging/Push mechanic:
    /// - Player holds to charge in Charging/Push states.
    /// - On release, if charge is above threshold, launch/push the ball.
    /// - Push is valid only while the ball is still in the launch tube.
    private void HandleChargingAndPush()
    {
        if (Input.GetKey(chargeKey))
            isHoldingCharge = true;

        chargePercentage = Mathf.Min(1f, chargeMax > 0f ? (chargeTimer / chargeMax) : 0f);

        // On release, attempt to launch/push
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

        // If we’re in Push but ball has left the tube and is turning back, reset to Charging
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

    /// Debounced "no-ball" detector while in Play.
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

        DOTween.Kill(t, complete: false);

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
            dupedBall.BaseDamage = anchor.BaseDamage;
            dupedBall.maxSpeed = anchor.maxSpeed;
            dupedBall.transform.localScale = anchor.transform.localScale;

            var anchorCol = anchor.GetComponent<Collider>();
            var dupedCol = dupedBall.GetComponent<Collider>();
            if (anchorCol != null && dupedCol != null && anchorCol.material != null)
                dupedCol.material = Instantiate(anchorCol.material);

            dupedBall.ResetRb();
        }
        ballCount++;
    }

    #endregion

    #region Score/XP

    public void AddScore(int gameScore, int bumpCount, int bumpCountConsec, float damageFactor)
    {
        int finalPoints = Mathf.RoundToInt(gameScore * scoreMultiplier * Mathf.Max(1f, damageFactor));

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

    /// Core respawn flow:
    /// - Keep progression/rewards intact; only reset the ball stack.
    /// - Remove clones, re-seat main ball to launcher, go to Charging or GameOver.
    private void ResetBallAndFlow()
    {
        EnsurePrimaryBallRef();
        _noBallTimer = 0f;

        DisableInvisibleWalls();
        ResetCameraShakeState();

        // Remove clones, keep main ball
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

        // Restore main ball
        if (ball != null)
        {
            // IsActive will be updated by OnEnable when we activate the GO
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

        // Reset charge flow values
        isHoldingCharge = false;
        chargeTimer = 0f;
        chargePercentage = 0f;

        ballCount = 1;

        ChangeState(lives > 0 ? PinballState.Charging : PinballState.GameOver);
    }

    public void AddXP(float xp)
    {
        float finalXP = xp * xpMultiplier;
        curXP += finalXP;
        ballXPScript?.UpdateXP(Mathf.RoundToInt(curXP), Mathf.RoundToInt(maxXP), level);
    }

    public void SpawnXP(Vector3 pos, bool isDead, bool isTakingElemDamage, float damageFactor)
    {
        if (!xpFXPrefab) return;

        int finalXP = Mathf.RoundToInt(xpCount * xpMultiplier * Mathf.Max(1f, damageFactor));


        Vector3 position = new Vector3(pos.x, pos.y + 1f, pos.z);
        var xpFX = Instantiate(xpFXPrefab, position, xpFXPrefab.transform.rotation);

        if (isDead) xpFX.Emit(finalXP * 2);
        else if (isTakingElemDamage) xpFX.Emit(Mathf.RoundToInt(finalXP / 3f));
        else xpFX.Emit(finalXP);
    }

    public void SpawnBonusWaterXP(Vector3 pos, float waterBonusXP, float damageFactor)
    {
        if (!xpFXPrefab) return;

        int finalXP = Mathf.RoundToInt(xpCount * xpMultiplier * Mathf.Max(1f, damageFactor));


        float bonus = Mathf.Max(0f, waterBonusXP) / 100f;
        Vector3 position = new Vector3(pos.x, pos.y + 1f, pos.z);
        var xpFX = Instantiate(xpFXPrefab, position, xpFXPrefab.transform.rotation);
        xpFX.Emit(Mathf.RoundToInt(finalXP * (1f + bonus)));
    }

    public void SpawnBonusEarthXP(Vector3 pos, float earthBonusXP, float damageFactor)
    {
        if (!xpFXPrefab) return;

        int finalXP = Mathf.RoundToInt(xpCount * xpMultiplier * Mathf.Max(1f, damageFactor));


        float bonus = Mathf.Max(0f, earthBonusXP) / 100f;
        Vector3 position = new Vector3(pos.x, pos.y + 1f, pos.z);
        var xpFX = Instantiate(xpFXPrefab, position, xpFXPrefab.transform.rotation);
        xpFX.Emit(Mathf.RoundToInt(finalXP * (1f + bonus)));
    }

    private void StartNextLevelUp()
    {
        if (pendingLevelUps <= 0) return;
        ChangeState(PinballState.LevelUp);
    }

    public void LevelUp()
    {
        level++;
        maxXP = XPFormula.XpReq(level);
        ballXPScript?.UpdateXP(curXP, maxXP, level);
        ApplyLevelBonuses();
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
    }

    #endregion

    #region Paddle single-shot elemental hit window

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

    public void ResetCameraShakeState()
    {
        if (mainCam == null) return;
        var t = mainCam.transform;
        t.DOKill(false);
        t.localPosition = _camDefaultLocalPos;
        t.localRotation = _camDefaultLocalRot;
    }

    public void ScreenShake()
    {
        if (!mainCam) return;

        ResetCameraShakeState();

        mainCam.transform
            .DOShakePosition(
                shakeDuration,
                shakeStrength,
                shakeVibrato,
                shakeRandomness,
                snapping: false,
                fadeOut: true
            )
            .SetEase(Ease.OutQuad)
            .SetUpdate(false);
    }

    #endregion

    #region Bumper respawn/explosion

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

        bumper.curHealth = bumper.maxHealth;
        bumper.gameObject.SetActive(true);
        if (col) col.enabled = true;
        if (mr) mr.enabled = true;
    }

    #endregion

    #region Reward application (IRunContext impl)

    public void ApplyXPMultiplier(float multiplier, bool cursed)
    {
        if (cursed)
        {
            xpCount += 1;
            if (xpBonusTimer > 5) xpBonusTimer = 5;
            xpMultiplier *= 2;
        }
        else xpMultiplier += multiplier;
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
        if (Mathf.Approximately(multiplier, X2_MULT_MAGIC)) scoreMultiplier *= 2f;
        else if (cursed) scoreMultiplier *= 4f;
        else scoreMultiplier += multiplier;
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

        if (scoreMult != 0) baseScoreMult *= Mult;

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

        if (xpMult != 0) baseXPMult *= Mult;

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
        float Amount = (100f + amount) / 100f;
        ForEachUsableBall(b => b.UpdateForcefield(Amount));
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
        float Amount = (100f + amount) / 100f;
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
                ChangeState(PinballState.Play);
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
            ChangeState(PinballState.Play);
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

            if (leftHasElem && !rightHasElem) { SetPaddleState(false); return; }
            if (!leftHasElem && rightHasElem) { SetPaddleState(true); return; }

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
                ChangeState(pendingLevelUps > 0 ? PinballState.LevelUp : PinballState.Play);
                return;
            }
        }

        reward.Apply(this);
        pendingLevelUps--;
        ChangeState(pendingLevelUps > 0 || currentState == PinballState.PaddleSelect ? PinballState.LevelUp : PinballState.Play);
    }

    private RewardSO PickOneWeighted(List<RewardSO> pool, RewardRarity rarity)
    {
        var candidates = pool.Where(r => r.Rarity == rarity).ToList();
        if (candidates.Count == 0) return null;
        int index = Random.Range(0, candidates.Count);
        return candidates[index];
    }

    private RewardRarity RollRarity()
    {
        float total = rarityWeights.Values.Sum();
        float roll = Random.Range(0f, total);

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

        for (int i = 0; i < 6; i++)
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
}