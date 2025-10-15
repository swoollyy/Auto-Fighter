using System.Collections.Generic;

public interface IRunContext
{

    bool IsActive(string rewardId);
    bool IsAvailable(string rewardId);
    bool Owns(string rewardId);
    bool HasExclusiveKeyActive(string key);

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

    void ApplyFireFX(int bonusDamage, float burnDamage, float burnDuration, int bounceDuration, bool canExplode, float explosionSize, int explosionDamageFlat, bool cursed);

    void ApplyWaterFX(float bonusXP, int bonusDamage, float drenchDuration, int bounceDuration, bool canExplode, float explosionSize, int explosionDamageFlat, bool cursed);

    void ApplyEarthFX(int fissureDamage, float crustedDuration, float fissureHitScoreMultiplier, float fissureHitXPMultiplier, int bounceDuration, bool cursed);

    void ApplyElectricFX(int shockDamage, int chainCount, float scoreMultiplier, float xpMultiplier, int bounceDuration, bool cursed);
    void ApplyXPForcefield(float radiusIncrease);

    void ApplyAdditionalBalls(int additionalBalls);

}