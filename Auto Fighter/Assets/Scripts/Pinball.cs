using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.AddressableAssets;


public enum PinballState
{
    None,
    Charging,
    Push,
    Play,
    LevelUp,
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

    //Helpers the rewards will call
    public void MarkOwned(string rewardId) => owned.Add(rewardId);
    public void SetActive(string rewardId, bool on)
    {
        if(on)
            active.Add(rewardId);
        else active.Remove(rewardId);
    }

    public void SetAvailable(string rewardId, bool on)
    {
        if(on)
            available.Add(rewardId);
        else available.Remove(rewardId);
    }

    public void SetExclusive(string key, bool on)
    {
        if (string.IsNullOrEmpty(key))
            return;
        if(on) 
            exclusiveKeysActive.Add(key);
        else exclusiveKeysActive.Remove(key);
            
    }

    private static readonly Dictionary<RewardRarity, float> rarityWeights = new()
    {
        {RewardRarity.Common, 88f},
        {RewardRarity.Uncommon, 66f},
        {RewardRarity.Rare, 40f},
        {RewardRarity.Epic, 24f},
        {RewardRarity.Legendary, 9.5f},
        {RewardRarity.Artifact, 1f},
        {RewardRarity.Cursed, 1f},
    };

    private PinballState currentState;
    public PinballState CurrentState => currentState;

    public KeyCode chargeBind = KeyCode.Space;
    public KeyCode leftPaddleBind = KeyCode.A;
    public KeyCode rightPaddleBind = KeyCode.D;

    public int curXP  = 0;
    public int maxXP;
    public int level { get; private set; } = 1;

    private int pendingLevelUps = 0;


    public Ball ball;

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

    private int score;
    public int Score => score;
    private int ballBumpCount;
    private int ballBumpCountC;

    private float scoreMultiplier = 1f;

    private float bonusTimer;
    private bool hasMult;

    private const float x2MULT = 100f;
    private const float x4MULT = 200f;

    public float ScoreMultiplier => scoreMultiplier;
    public bool IsScoreMultiplierActive => scoreMultiplier > 1f;
    public float BonusTimeRemaining => bonusTimer;

    private readonly List<RewardSO> rewardPool = new();

    [SerializeField]
    ParticleSystem xpFXPrefab;

    [SerializeField]
    BallXPBar ballXPScript;

    int xpCount = 3;


    float wallTimer;
    bool wallTimerStart;

    public void ChangeState(PinballState state)
    {
        currentState = state;

        switch(state)
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

            case PinballState.GameOver:
                leftPaddle = null;
                rightPaddle = null;
                break;
        }
    }

    void Awake()
    {
        if(Instance && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        maxXP = XPFormula.XpReq(level);
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
    }

    // Update is called once per frame
    void Update()
    {

        if(leftPaddle != null)
            leftPaddle.PaddleMovement(Input.GetKey(leftPaddleBind));
        if(rightPaddle != null)
            rightPaddle.PaddleMovement(Input.GetKey(rightPaddleBind));

        if(wallTimerStart)
        {
            wallTimer += Time.deltaTime;

            if (wallTimer >= .2f)
                 EnableInvisibleWalls();
        }

        if (currentState == PinballState.GameOver)
        {
            if(Input.GetKey(chargeBind))
            SceneManager.LoadScene(0);
        }

        if (curXP >= maxXP)
        {
            curXP -= maxXP;
            level++;
            LevelUp();
        }

        if(Input.GetKeyDown(KeyCode.P))
        {
            AddXP(150);
        }

        //score mult
        if(IsScoreMultiplierActive && bonusTimer > 0f)
        {
            bonusTimer -= Time.unscaledDeltaTime;
            if(bonusTimer <= 0f)
            {
                bonusTimer = 0f;
                scoreMultiplier = 1f;
            }
        }



        //if in correct states
        if (currentState == PinballState.Charging || currentState == PinballState.Push)
        {
            //holding space down
            if(Input.GetKey(chargeBind))
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
                        if(ball.isInZone)
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

        if(!isHoldingCharge)
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


    public void EnableInvisibleWalls()
    {
        invisWalls.SetActive(true);
    }

    public void DupeBall()
    {
        GameObject dupedBall = Instantiate(ballClone, ball.gameObject.transform.position, Quaternion.identity);
        dupedBall.GetComponent<Ball>().ResetRb();

    }

    public void AddScore(int gameScore, int bumpCount, int bumpCountConsec)
    {
        int finalPoints = Mathf.RoundToInt(gameScore * scoreMultiplier);
        score += finalPoints;
        ballBumpCount = bumpCount;
        ballBumpCountC = bumpCountConsec;
        uim.UpdateScore(score, bumpCount, bumpCountConsec);
    }

    public void AddXP(int xp)
    {
        curXP += xp;
        while(curXP >= maxXP)
        {
            curXP -= maxXP;
            pendingLevelUps++;
        }

        ballXPScript.UpdateXP((float)curXP, (float)maxXP, level);

        if(pendingLevelUps > 0 && CurrentState != PinballState.LevelUp)
        {
            StartNextLevelUp();
        }

    }

    public void SpawnXP(Vector3 pos)
    {
        var emitParams = new ParticleSystem.EmitParams();
        //AdjustXP(emitParams);
        ParticleSystem xpFX = Instantiate(xpFXPrefab, pos, xpFXPrefab.transform.rotation);
        xpFX.Emit(xpCount);
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
    }

    public void OnRewardChosen(RewardSO reward)
    {
        reward.Apply(this);
        pendingLevelUps--;

        if (pendingLevelUps > 0)
            ChangeState(PinballState.LevelUp);
        else
            ChangeState(PinballState.Play);
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

    public void ApplyBonusTime(float time, bool cursed)
    {

        if (!IsScoreMultiplierActive) return;

            if(cursed)
            {
                bonusTimer *= .1f;
            }
            else
                bonusTimer += time;
    }

    private RewardSO PickOneWeighted(List<RewardSO> pool, RewardRarity rarity)
    {
        var candidates = pool.Where(r => r.Rarity == rarity).ToList();
        if(candidates.Count == 0) return null;

        int index = Random.Range(0, candidates.Count);
        return candidates[index];

    }

    private RewardRarity RollRarity()
    {
        float total = rarityWeights.Values.Sum();
        float roll = Random.Range(0f, total);

        float cumulative = 0f;

        foreach(var kv in rarityWeights)
        {
            cumulative += kv.Value;
            if(roll <= cumulative)
                return kv.Key;
        }

        return RewardRarity.Common;

    }

    private List<RewardSO> GetRewardChoices()
    {
        var eligible = rewardPool.Where(r => r.IsEligible(this)).ToList();
        if (eligible.Count < 3) eligible = rewardPool.ToList();

        var picks = new List<RewardSO>();
        for(int i = 0; i < 3; i++)
            {
            RewardSO choice = null;
            for(int j = 0; j < 10 && choice == null; j++)
            {
                var tier = RollRarity();
                choice = PickOneWeighted(eligible, tier);
            }
            if(choice == null) break;
            picks.Add(choice);
            eligible.Remove(choice);
            }
        return picks;

    }

}


