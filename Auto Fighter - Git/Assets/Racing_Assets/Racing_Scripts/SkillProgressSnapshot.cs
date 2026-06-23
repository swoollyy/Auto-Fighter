using System.Collections.Generic;

/// <summary>
/// Serializable snapshot of the player's purchasable progression at a moment in time:
/// every skill level plus the coin and sprocket balances. Quest unlocks are intentionally
/// NOT included so they survive a trial failure/restore. Captured/restored by
/// <see cref="RacingSkillTreeManager"/> and used by <see cref="DayTrialManager"/> to revert
/// to the start-of-trial state when a trial deadline is failed.
/// </summary>
[System.Serializable]
public class SkillProgressSnapshot
{
    // Parallel lists (JsonUtility cannot serialize a Dictionary). skillTypes[i] holds the
    // integer value of a SkillType, skillLevels[i] its level. Only non-zero levels are stored.
    public List<int> skillTypes = new();
    public List<int> skillLevels = new();
    public int currency;
    public int sprockets;
}
