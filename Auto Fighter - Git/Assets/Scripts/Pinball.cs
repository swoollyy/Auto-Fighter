using DG.Tweening;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.SceneManagement;
using static UnityEngine.ProBuilder.AutoUnwrapSettings;


public enum PinballState
{
    None,
    Charging,
    Push,
    Play,
    LevelUp,
    PaddleSelect,
    GameOver
}

public class Pinball : MonoBehaviour, IRunContext
{

    public static Pinball Instance { get; private set; }


    //
    private readonly HashSet<string> owned = new();
    private readonly HashSet<string> active = new();
    private readonly HashSet<string> available = new();
    private readonly HashSet<string> exclusiveKeysActive = new();

    // IRunContext
    public bool Owns(string rewardId) => owned.Contains(rewardId);
    public bool IsActive(string rewardId) => active.Contains(rewardId);
    public bool IsAvailable(string rewardId) => available.Contains(rewardId);
    public bool HasExclusiveKeyActive(string key) =>
        !string.IsNullOrEmpty(key) && exclusiveKeysActive.Contains(key);

    public IEnumerable<string> ActiveKeys => exclusiveKeysActive;


    //Helpers the rewards will call
    public void MarkOwned(string rewardId) => owned.Add(rewardId);
    public void SetActive(string rewardId, bool on)
    {
        if (on)
            active.Add(rewardId);
        else active.Remove(rewardId);
    }

    public void SetAvailable(string rewardId, bool on)
    {
        if (on)
            available.Add(rewardId);
        else available.Remove(rewardId);
    }

    public void SetExclusive(string key, bool on)
    {
        if (string.IsNullOrEmpty(key))
            return;
        if (on)
            exclusiveKeysActive.Add(key);
        else exclusiveKeysActive.Remove(key);

    }

    private static readonly Dictionary<RewardRarity, float> rarityWeights = new()
    {
        {RewardRarity.Common, 88f},
        {RewardRarity.Uncommon, 66f},
        {RewardRarity.Rare, 40f},
        {RewardRarity.Epic, 24f},
        {RewardRarity.Legendary, 5f},
        {RewardRarity.Artifact, .5f},
        {RewardRarity.Cursed, .5f},
    };


    private PinballState currentState;
    public PinballState CurrentState => currentState;

    public KeyCode chargeBind = KeyCode.Space;
    public KeyCode leftPaddleBind = KeyCode.A;
    public KeyCode rightPaddleBind = KeyCode.D;

    public float curXP = 0;
    public float maxXP;
    public int level { get; private set; } = 1;

    private int pendingLevelUps = 0;


    public Ball ball;
    BallElementalState elementalState;

    private Collider ballCol;

    public PinballUIM uim;

    public GameObject ballClone;

    public GameObject invisWalls;

    public PinballFlipper leftPaddle;
    public PinballFlipper rightPaddle;

    bool isHoldingCharge;
    bool hasPressedCharge;

    float chargeTimer;
    public float chargePercentage;
    float chargeMax;

    public int ballCount;

    private int score;
    private int mult;
    public int Score => score;
    public int Mult => mult;
    private int ballBumpCount;
    private int ballBumpCountC;

    private float scoreMultiplier = 1f;
    private float xpMultiplier = 1f;
    private float baseScoreMult = 1f;
    private float baseXPMult = 1f;

    private float scoreBonusTimer;
    private float xpBonusTimer;
    private bool hasMult;

    private int bumperBouncesS;
    private int bumperBouncesG;

    private bool extraHitsS;
    private bool extraHitsG;
    private float bonusHitsS;
    private float bonusHitsG;
    private int bouncesForBonusS;
    private int bouncesForBonusG;

    private bool canHitL;
    private bool canHitR;
    private bool hasBeenHitL;
    private bool hasBeenHitR;

    public bool ExtraHitsG => extraHitsG;
    public bool ExtraHitsS => extraHitsS;
    public float BonusHitsG => bonusHitsG;
    public float BonusHitsS => bonusHitsS;
    public int BouncesForBonusG => bouncesForBonusG;
    public int BouncesForBonusS => bouncesForBonusS;


    [SerializeField] private float defaultBaseDamage = 5f;

    public float Damage => TryGetAnchorBall(out var b) ? b.CurrentDamage : defaultBaseDamage;


    private const float x2MULT = 100f;
    private const float x4MULT = 200f;

    public float ScoreMultiplier => scoreMultiplier;
    public bool IsScoreMultiplierActive => scoreMultiplier > 1f;
    public float ScoreBonusTimeRemaining => scoreBonusTimer;

    public float XPMultiplier => xpMultiplier;
    public bool IsXPMultiplierActive => xpMultiplier > 1f;
    public float XPBonusTimeRemaining => xpBonusTimer;


    private RewardSO pendingPaddleReward;

    private readonly List<RewardSO> rewardPool = new();

    public bool destroyedBumper;
    private float destroyedMult = 2f;


    [SerializeField]
    ParticleSystem xpFXPrefab;

    [SerializeField]
    BallXPBar ballXPScript;

    private int xpCount = 3;
    public int DroppedXP => xpCount;

    public float explosionForce = 50f;
    public float explosionRadius = 3f;


    float wallTimer;
    bool wallTimerStart;

    [SerializeField] private Camera mainCam;
    [SerializeField] private float shakeDuration = 0.15f;
    [SerializeField] private float shakeStrength = 0.3f;
    [SerializeField] private int shakeVibrato = 12;
    [SerializeField, Range(0f, 1f)] private float shakeRandomness = 90f;

    private readonly List<Ball> liveBalls = new();




    public void ChangeState(PinballState state)
    {
        currentState = state;

        switch (state)
        {
            case PinballState.None:
                break;
            case PinballState.Charging:
                hasPressedCharge = false;
                chargePercentage = 0;
                chargeTimer = 0;
                chargeMax = 1.5f;
                break;
            case PinballState.Push:
                hasPressedCharge = false;
                chargePercentage = 0;
                chargeTimer = 0;
                chargeMax = 1f;
                break;
            case PinballState.Play:
                uim.DefaultUI();
                chargePercentage = 0;
                chargeTimer = 0;
                Time.timeScale = 1f;
                wallTimerStart = true;
                break;
            case PinballState.LevelUp:
                LevelUp();
                var choices = GetRewardChoices();
                uim.ShowRewardPopup(choices);
                Time.timeScale = 0f;
                break;
            case PinballState.PaddleSelect:
                uim.PaddleSelect();
                Time.timeScale = 0f;
                break;

            case PinballState.GameOver:
                leftPaddle = null;
                rightPaddle = null;
                break;
        }
    }

    void Awake()
    {
        if (Instance && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        maxXP = XPFormula.XpReq(level);
        Debug.Log($"Initial max XP {maxXP}");


        elementalState = ball.GetComponent<BallElementalState>();

        ballCol = ball.gameObject.GetComponent<Collider>();
    }

    // Start is called before the first frame update
    async void Start()
    {
        //load all reward assets
        var loader = Addressables.LoadAssetsAsync<RewardSO>("Rewards", rewardPool.Add);
        await loader.Task;

        

        ChangeState(PinballState.Charging);
        uim.Init(this);
        invisWalls.SetActive(false);

        ballCount = 1;
    }

    // Update is called once per frame
    void Update()
    {
        Debug.Log($"Current XP: {curXP} / {maxXP} | Level: {level} | Pending Level Ups: {pendingLevelUps}");


        if (curXP >= maxXP)
        {
            pendingLevelUps++;
            curXP -= maxXP;
        }

        if (pendingLevelUps > 0 && CurrentState != PinballState.LevelUp)
        {
            Debug.Log($"Doing this");
            StartNextLevelUp();
        }


        if (leftPaddle != null)
        {
            leftPaddle.PaddleMovement(Input.GetKey(leftPaddleBind));

            var leftElem = leftPaddle.GetComponent<PaddleElementalState>();
            bool hasElem = leftElem != null && leftElem.CurrentState != PaddleState.None;

            if (hasElem)
            {
                if (Input.GetKeyDown(leftPaddleBind))
                {
                    HitCheck(leftPaddleBind);
                }

                var leftPaddleBall = GetPaddleTarget();

                if(leftPaddleBall != null && leftPaddleBall.isTouchingPaddles && (Input.GetKey(leftPaddleBind) || canHitL) && !hasBeenHitL)
                {
                    hasBeenHitL = true;
                    StartCoroutine(ResetLBumper(.4f));
                    if (leftElem != null)
                        leftPaddleBall.OnPaddleHit(leftElem.GetEffectData());
                }
            }
        }
        if (rightPaddle != null)
        {
            rightPaddle.PaddleMovement(Input.GetKey(rightPaddleBind));

            var rightElem = rightPaddle.GetComponent<PaddleElementalState>();
            var hasElem = rightElem != null && rightElem.CurrentState != PaddleState.None;

            if (hasElem)
            {
                if (Input.GetKeyDown(rightPaddleBind))
                    HitCheck(rightPaddleBind);

                var rightPaddleBall = GetPaddleTarget();

                if (rightPaddleBall != null && rightPaddleBall.isTouchingPaddles && (Input.GetKey(rightPaddleBind) || canHitR) && !hasBeenHitR)
                {
                    hasBeenHitR = true;
                    StartCoroutine(ResetRBumper(.4f));
                    if (rightElem != null)
                        rightPaddleBall.OnPaddleHit(rightElem.GetEffectData());
                }

            }

        }

        if (xpMultiplier < baseXPMult)
            xpMultiplier = baseXPMult;
        if (scoreMultiplier < baseScoreMult)
            scoreMultiplier = baseScoreMult;

        if (wallTimerStart)
        {
            wallTimer += Time.deltaTime;

            if (wallTimer >= .2f)
                EnableInvisibleWalls();
        }

        if (currentState == PinballState.GameOver)
        {
            if (Input.GetKey(chargeBind))
                SceneManager.LoadScene(0);
        }

        if (Input.GetKeyDown(KeyCode.P))
        {
            AddXP(13);
        }

        //score mult
        if (IsScoreMultiplierActive && scoreBonusTimer > 0f)
        {
            scoreBonusTimer -= Time.unscaledDeltaTime;
            if (scoreBonusTimer <= 0f)
            {
                scoreBonusTimer = 0f;
                scoreMultiplier = baseScoreMult;
            }
        }

        //xp mult
        if (IsXPMultiplierActive && xpBonusTimer > 0f)
        {
            xpBonusTimer -= Time.unscaledDeltaTime;
            if (xpBonusTimer <= 0f)
            {
                xpBonusTimer = 0f;
                xpMultiplier = baseXPMult;
            }
        }



        //if in correct states
        if (currentState == PinballState.Charging || currentState == PinballState.Push)
        {
            //holding space down
            if (Input.GetKey(chargeBind))
            {
                isHoldingCharge = true;
                hasPressedCharge = true;
            }





            chargePercentage = Mathf.Min(1, chargeTimer / chargeMax);

            //if space is let up
            if (Input.GetKeyUp(chargeBind))
            {
                isHoldingCharge = false;
                //if charge at 25%
                if (chargePercentage > .10f)
                {
                    //if in charging
                    if (currentState == PinballState.Charging)

                    {
                        //launch ball
                        ball.Launch(chargePercentage);
                        //if charge enough %, push state to pre-charge, reset charge % and timer, change max
                        if (chargePercentage > .648f)
                        {
                            ChangeState(PinballState.Push);
                        }
                    }
                    //if in push state
                    else
                    {
                        //if ball in CollisionStay in Zone, push it
                        if (ball.isInZone)
                        {
                            ball.Push(chargePercentage);
                            ChangeState(PinballState.Play);
                        }
                    }

                }
            }

            if (currentState == PinballState.Push && !ball.isInZone && ball.GetComponent<Rigidbody>().velocity.z < 0)
            {
                ChangeState(PinballState.Charging);
            }


        }

        if (!isHoldingCharge)
        {
            chargeTimer -= Time.deltaTime;
            if (chargeTimer < 0)
                chargeTimer = 0;
        }
        else
        {
            chargeTimer += Time.deltaTime;
            if (chargeTimer > chargeMax)
                chargeTimer = chargeMax;
        }
    }


    public void RegisterBall(Ball b)
    {
        if(b != null && !liveBalls.Contains(b))
            liveBalls.Add(b);
    }

    public void UnregisterBall(Ball b)
    {
        if(b != null)
            liveBalls.Remove(b);
    }
    private static bool IsBallUsable(Ball b)
    {
        return b != null
            && b.isActiveAndEnabled
            && b.gameObject.activeInHierarchy
            && b.isActive; 
    }

    private IEnumerable<Ball> GetUsableBalls()
    {
        for(int i = liveBalls.Count -1; i >= 0; i--)
        {
            var b = liveBalls[i];
            if(b == null) { liveBalls.RemoveAt(i);
                continue;
            }
            if (IsBallUsable(b)) yield return b;
        }
    }

    private void ForEachUsableBall(System.Action<Ball> action)
    {
        foreach(var b in GetUsableBalls())
            action(b);
    }

    private bool TryGetAnchorBall(out Ball anchor)
    {
        for (int i = 0; i < liveBalls.Count; i++)
        {
            var candidate = liveBalls[i];

            // prune dead/missing entries
            if (candidate == null)
            {
                liveBalls.RemoveAt(i);
                i--;
                continue;
            }

            if (IsBallUsable(candidate))
            {
                anchor = candidate; // earliest active in insertion order
                return true;
            }
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
            if (IsBallUsable(b) && b.isTouchingPaddles)
                return b;
        }

        return TryGetAnchorBall(out var anchor) ? anchor : null;
    }
    public void EnableInvisibleWalls()
    {
        invisWalls.SetActive(true);
    }

    public void DupeBall()
    {
        if (!TryGetAnchorBall(out var anchor))
        {
            if (IsBallUsable(ball))
                anchor = ball;
        }

        if (anchor == null)
        {
            Debug.Log("No valid ball to duplicate!");
            return;
        }


        var dupedBallGO = Instantiate(ballClone, anchor.transform.position, Quaternion.identity);
        var dupedBall = dupedBallGO.GetComponent<Ball>();

        if(dupedBall != null && anchor != null)
        {
            dupedBall.BaseDamage = anchor.BaseDamage;
            dupedBall.maxSpeed = anchor.maxSpeed;
            dupedBall.transform.localScale = anchor.transform.localScale;


            var anchorCol = anchor.GetComponent<Collider>();
            var dupedCol = dupedBall.GetComponent<Collider>();



            if (anchorCol != null && dupedCol != null && anchorCol.material != null)
            {
                var src = anchorCol.material;
                dupedCol.material = Instantiate(src);
            }

            dupedBall.ResetRb();

        }
    }

    public void AddScore(int gameScore, int bumpCount, int bumpCountConsec)
    {
        int finalPoints = Mathf.RoundToInt(gameScore * scoreMultiplier);

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
            {
                xpCount = Mathf.RoundToInt(xpCount / bonusHitsG);
            }

        }


        if (destroyedBumper)
        {
            score += Mathf.RoundToInt(finalPoints * destroyedMult);
            destroyedBumper = false;
        }
        else
            score += finalPoints;
        ballBumpCount = bumpCount;
        ballBumpCountC = bumpCountConsec;
        uim.UpdateScore(score, bumpCount, bumpCountConsec);
    }

    public void AddXP(float xp)
    {
        float finalXP = xp * xpMultiplier;
        curXP += finalXP;
        curXP = Mathf.RoundToInt(curXP);

        ballXPScript.UpdateXP(Mathf.RoundToInt(curXP), Mathf.RoundToInt(maxXP), level);



    }

    public void SpawnXP(Vector3 pos, bool isDead, bool isTakingElemDamage)
    {
        var emitParams = new ParticleSystem.EmitParams();
        Vector3 position = new Vector3(pos.x, pos.y + 1f, pos.z);
        //AdjustXP(emitParams);
        ParticleSystem xpFX = Instantiate(xpFXPrefab, position, xpFXPrefab.transform.rotation);
        if (isDead)
        {
            xpFX.Emit(xpCount * 2);
        }
        else if (isTakingElemDamage)
        {
            xpFX.Emit(Mathf.RoundToInt(xpCount / 3));
        }
        else xpFX.Emit(xpCount);

    }

    public void SpawnBonusWaterXP(Vector3 pos, float waterBonusXP)
    {
        //bonus XP starts at 25% and goes up to 100%

        float bonusXP = waterBonusXP / 100f;
        var emitParams = new ParticleSystem.EmitParams();
        Vector3 position = new Vector3(pos.x, pos.y + 1f, pos.z);
        //AdjustXP(emitParams);
        ParticleSystem xpFX = Instantiate(xpFXPrefab, position, xpFXPrefab.transform.rotation);
        xpFX.Emit(Mathf.RoundToInt(xpCount * (1 + bonusXP)));
    }

    public void SpawnBonusEarthXP(Vector3 pos, float earthBonusXP)
    {
        float bonusXP = earthBonusXP / 100f;
        var emitParams = new ParticleSystem.EmitParams();
        Vector3 position = new Vector3(pos.x, pos.y + 1f, pos.z);
        //AdjustXP(emitParams);
        ParticleSystem xpFX = Instantiate(xpFXPrefab, position, xpFXPrefab.transform.rotation);
        xpFX.Emit(Mathf.RoundToInt(xpCount * (1 + bonusXP)));
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
        ballXPScript.UpdateXP((float)curXP, (float)maxXP, level);

        ApplyLevelBonuses();
    }


    public void ApplyLevelBonuses()
    {
        if (level % 5 == 0)
        {
            baseScoreMult += .5f;
            baseXPMult += .5f;
        }
        else
        {
            baseScoreMult += .15f;
            baseXPMult += .15f;
        }
    }

    private void HitCheck(KeyCode paddles)
    {
        if (paddles == leftPaddleBind)
            StartCoroutine(RegCheck(leftPaddleBind));
        else StartCoroutine(RegCheck(rightPaddleBind));
    }

    public IEnumerator RespawnRoutine(Bumper bumper)
    {
        if (bumper.type == BumperType.Small)
        {
            explosionForce *= .5f;
            explosionRadius *= .5f;
            ball.GetComponent<Rigidbody>().AddExplosionForce(explosionForce,
                bumper.transform.position, explosionRadius, 0f, ForceMode.Impulse);
        }
        else if (bumper.type == BumperType.Default)
        {
            ball.GetComponent<Rigidbody>().AddExplosionForce(explosionForce,
    bumper.transform.position, explosionRadius, 0f, ForceMode.Impulse);
        }
        else if (bumper.type == BumperType.Large)
        {
            explosionForce *= 1.5f;
            explosionRadius *= 1.5f;
            ball.GetComponent<Rigidbody>().AddExplosionForce(explosionForce,
    bumper.transform.position, explosionRadius, 0f, ForceMode.Impulse);
        }

        Collider col = bumper.GetComponent<Collider>();
        MeshRenderer mr = bumper.GetComponent<MeshRenderer>();
        if (col) col.enabled = false;
        if (mr) mr.enabled = false;

        yield return new WaitForSeconds(bumper.cooldown);

        bumper.curHealth = bumper.maxHealth;
        bumper.gameObject.SetActive(true);
        if (col) col.enabled = true;
        if (mr) mr.enabled = true;

    }

    private IEnumerator RegCheck(KeyCode paddle)
    {
        if (paddle == leftPaddleBind)
            canHitL = true;
        else canHitR = true;

        yield return new WaitForSeconds(.6f);

        if (paddle == leftPaddleBind)
            canHitL = false;
        else canHitR = false;
    }

    private IEnumerator ResetLBumper(float cd)
    {
        yield return new WaitForSeconds(cd);

        hasBeenHitL = false;
    }
    private IEnumerator ResetRBumper(float cd)
    {
        yield return new WaitForSeconds(cd);

        hasBeenHitR = false;
    }


    public void ScreenShake()
    {
        DOTween.Kill(mainCam.transform);

        // simple DOShakePosition call
        mainCam.transform.DOShakePosition(
            shakeDuration,
            shakeStrength,
            shakeVibrato,
            shakeRandomness,
            false,   // snapping
            true     // fade out at end
        ).SetEase(Ease.OutQuad)
         .SetUpdate(false); // works even if Time.timeScale = 0
    }





    #region Ability Application

    public void ApplyXPMultiplier(float multiplier, bool cursed)
    {
        if (cursed)
        {
            xpCount += 1;
            if (xpBonusTimer > 5)
                xpBonusTimer = 5;
            xpMultiplier *= 2;
        }
        else
            xpMultiplier += multiplier;
    }

    public void ApplyXPBonusTime(float time, bool cursed)
    {
        if (!IsXPMultiplierActive) return;

        if (cursed)
        {
            if (xpBonusTimer > 3)
                xpBonusTimer = 3;
        }
        else
        {
            xpBonusTimer += time;
            if (xpBonusTimer > 30)
                xpBonusTimer = 30f;
        }
    }

    public void ApplyScoreMultiplier(float multiplier, bool cursed)
    {


        if (multiplier == x2MULT)
            scoreMultiplier *= 2;
        else if (cursed)
            scoreMultiplier *= 4;
        else
            scoreMultiplier += multiplier;
    }

    public void ApplyScoreBonusTime(float time, bool cursed)
    {

        if (!IsScoreMultiplierActive) return;

        if (cursed)
        {
            scoreBonusTimer *= .1f;
        }
        else
            scoreBonusTimer += time;
    }

    public void ApplyShrinkFX(float size, float speed, float bounciness, float scoreMult, float bonusBounces, int bounces, bool bonus, bool cursed)
    {
        float Size = (100f + size) / 100f;
        float Speed = (100f + speed) / 1000f;
        float Mult = (100f + scoreMult) / 100f;
        float Bounciness = ((100f * bounciness) / 10000f);

        if (scoreMult != 0)
            baseScoreMult *= Mult;

        if (bonus)
        {
            extraHitsS = true;
            bouncesForBonusS = bounces;
            bonusHitsS = bonusBounces;
        }

        ForEachUsableBall(b =>
        {
            if (bounciness != 0)
                b.AdjustBounciness(1f + Bounciness);

            if (size != 0)
                b.transform.localScale *= Size;

            if (speed != 0)
                b.maxSpeed *= 1f + Speed;
        });
    }
    public void ApplyGrowFX(float size, float speed, float bounciness, float xpMult, float bonusBounces, int bounces, bool bonus, bool cursed)
    {
        float Size = (100f + size) / 100f;
        float Speed = (100f + speed) / 1000f;
        float Mult = (100f + xpMult) / 100f;
        float Bounciness = ((100f * bounciness) / 10000f);

        if (xpMult != 0)
            baseXPMult *= Mult;

        if (bonus)
        {
            extraHitsG = true;
            bouncesForBonusG = bounces;
            bonusHitsG = bonusBounces;
        }

        ForEachUsableBall(b =>
        {
            if (bounciness != 0)
                b.AdjustBounciness(1f + Bounciness);

            if (size != 0)
                b.transform.localScale *= Size;

            if (speed != 0)
                b.maxSpeed *= 1f - Speed;
        });

    }

    public void ApplyXPForcefield(float amount)
    {
        float Amount = (100f + amount) / 100f;
        ForEachUsableBall(b =>
        {
            b.UpdateForcefield(Amount);
        });
    }

    public void ApplyAdditionalBalls(int additionalBalls)
    {
        if(additionalBalls != 100)
        for (int i = 0; i < additionalBalls; i++)
        {
            DupeBall();
            ballCount++;
        }
        else
        {
            int curBallCount = ballCount;
            for (int i = 0; i < curBallCount; i++)
            {
                DupeBall();
                ballCount++;
            }
        }



    }

    public void SetPaddleState(bool isLeft)
    {
        var paddle = isLeft ? leftPaddle : rightPaddle;
        var paddleElem = paddle.gameObject.GetComponent<PaddleElementalState>();
        pendingPaddleReward.ApplyToPaddle(paddleElem);
        pendingPaddleReward = null;
        if (pendingLevelUps > 0)
        {
            var choices = GetRewardChoices();
            uim.ShowRewardPopup(choices);
            uim.ClosePaddleSelect(true);
        }
        else
        {
            uim.ClosePaddleSelect(false);
            ChangeState(PinballState.Play);
        }
    }

    #endregion


    #region Reward Scriptable Object Methods

    public void OnRewardChosen(RewardSO reward)
    {
        if (reward.isPaddleReward)
        {
            var leftPaddleElement = leftPaddle.GetComponent<PaddleElementalState>();
            var rightPaddleElement = rightPaddle.GetComponent<PaddleElementalState>();
            bool leftHasElem = leftPaddleElement.CurrentState != PaddleState.None;
            bool rightHasElem = rightPaddleElement.CurrentState != PaddleState.None;
            pendingPaddleReward = reward;
            if (leftHasElem && !rightHasElem)
            {
                SetPaddleState(false);
            }
            else if (!leftHasElem && rightHasElem)
            {
                SetPaddleState(true);
            }
            else
            {
                reward.Apply(this);
                pendingLevelUps--;

                ChangeState(PinballState.PaddleSelect);
                return;
            }

        }
        if (reward.Scalable && reward.ReplacesReward != null)
        {
            var old = reward.ReplacesReward;

            if (Owns(old.Id))
            {
                //remove old ownership
                active.Remove(old.Id);
                owned.Remove(old.Id);

                //mark the new tier as owned
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
            if (roll <= cumulative)
                return kv.Key;
        }

        return RewardRarity.Common;

    }

    private List<RewardSO> GetRewardChoices()
    {
        var eligible = rewardPool.Where(r => r.IsEligible(this)).ToList();

        if (eligible.Count == 0)
            return new List<RewardSO>();


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


