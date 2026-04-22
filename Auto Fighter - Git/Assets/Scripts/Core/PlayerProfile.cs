namespace AutoFighter.Core
{
    /// <summary>
    /// Lightweight accessors for cross-scene player profile values.
    /// Narrative/UI systems can use this instead of directly touching SaveSystem.
    /// </summary>
    public static class PlayerProfile
    {
        public static string GetPlayerNameOrDefault(string fallback = "Player")
        {
            if (SaveSystem.Current == null) SaveSystem.Load();
            string name = SaveSystem.Current != null ? SaveSystem.Current.playerName : string.Empty;
            return string.IsNullOrWhiteSpace(name) ? fallback : name;
        }

        public static float GetCursorSpeedOrDefault()
        {
            if (SaveSystem.Current == null) SaveSystem.Load();
            float value = SaveSystem.Current != null
                ? SaveSystem.Current.cursorSpeedPixelsPerSecond
                : SaveData.DefaultCursorSpeedPixelsPerSecond;

            if (value <= 0f) value = SaveData.DefaultCursorSpeedPixelsPerSecond;
            return value;
        }
    }
}
