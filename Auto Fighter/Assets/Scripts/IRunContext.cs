public interface IRunContext
{
    bool IsActive(string rewardId);
    bool IsAvailable(string rewardId);
    bool Owns(string rewardId);
    bool HasExclusiveKeyActive(string key);

    void MarkOwned(string rewardId);
    void SetActive(string rewardId, bool on);
    void SetExclusive(string key, bool on);


    void ApplyScoreMultiplier(float multiplier, bool isCursed);
    void ApplyXPMultiplier(float multiplier, bool isCursed);

    void ApplyBonusTime(float time, bool isCursed);


}