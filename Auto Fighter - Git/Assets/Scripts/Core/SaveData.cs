using System;

namespace AutoFighter.Core
{
    /// <summary>
    /// Single-slot save payload. Add fields here as the game grows.
    /// Bump <see cref="version"/> whenever you change the schema so
    /// <see cref="SaveSystem"/> can migrate older saves.
    /// </summary>
    [Serializable]
    public class SaveData
    {
        public const int CurrentVersion = 1;

        public int version = CurrentVersion;

        public long lastSavedUtcTicks;

        public string playerName = string.Empty;
        public string selectedCharacterClass = string.Empty;

        public long softCurrency;
        public int highestLevelReached;
        public int totalRuns;

        public static SaveData CreateDefault()
        {
            return new SaveData
            {
                version = CurrentVersion,
                lastSavedUtcTicks = DateTime.UtcNow.Ticks,
            };
        }
    }
}
