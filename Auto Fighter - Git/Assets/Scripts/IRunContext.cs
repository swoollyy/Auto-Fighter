using System.Collections.Generic;

public interface IRunContext
{

    bool IsActive(string rewardId);
    bool IsAvailable(string rewardId);
    bool Owns(string rewardId);
    bool HasExclusiveKeyActive(string key);

    int Lives { get; }
    int MaxLives { get; }

    void ApplyGrantedLives(int amount);

    void MarkOwned(string rewardId);
    void SetActive(string rewardId, bool on);
    void SetAvailable(string rewardId, bool on);
    void SetExclusive(string key, bool on);

    IEnumerable<string> ActiveKeys { get; }

    void ApplyScoreMultiplier(float multiplier, bool isCursed);
    void ApplyXPMultiplier(float multiplier, bool isCursed);

    void ApplyScoreBonusTime(float time, bool isCursed);
    void ApplyXPBonusTime(float time, bool isCursed);

    void ApplyShrinkFX(float size, float speed, float bounciness, float scoreMult, float bonusHits, int bounces, bool bonus, bool isCursed);
    void ApplyGrowFX(float size, float speed, float bounciness, float scoreMult, float bonusHits, int bounces, bool bonus, bool isCursed);

    void ApplyDamageFX(float amount);
    void ApplyDmgPerBounceFX(float damageMult, int bouncesNeeded);



    void ApplyXPForcefield(float radiusIncrease);

    void ApplyAdditionalBalls(int additionalBalls);

}