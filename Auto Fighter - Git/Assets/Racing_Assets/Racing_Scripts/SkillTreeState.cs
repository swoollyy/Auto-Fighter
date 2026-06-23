using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public class SkillTreeState
{
    // Persisted levels per skill type
    private readonly Dictionary<SkillType, int> _levels = new();

    public int GetLevel(SkillType type) => _levels.TryGetValue(type, out var lvl) ? lvl : 0;

    public bool Increment(SkillType type, int maxLevel)
    {
        int cur = GetLevel(type);
        if (cur >= maxLevel) return false;
        _levels[type] = cur + 1;
        return true;
    }

    /// <summary>
    /// Directly set a skill's level (used by snapshot restore). Level &lt;= 0 clears the entry.
    /// Call <see cref="Save"/> afterwards to persist.
    /// </summary>
    public void SetLevel(SkillType type, int level)
    {
        if (level <= 0)
            _levels.Remove(type);
        else
            _levels[type] = level;
    }

    // Simple persistence (expand later to JSON / save file)
    public void Save()
    {
        foreach (var kv in _levels)
            PlayerPrefs.SetInt($"RacingSkill_{kv.Key}", kv.Value);
        PlayerPrefs.Save();
    }

    public void Load()
    {
        foreach (SkillType t in Enum.GetValues(typeof(SkillType)))
        {
            if (PlayerPrefs.HasKey($"RacingSkill_{t}"))
                _levels[t] = PlayerPrefs.GetInt($"RacingSkill_{t}", 0);
        }
    }

    // NEW: clear all persisted skill levels
    public void ClearPersistent()
    {
        foreach (SkillType t in Enum.GetValues(typeof(SkillType)))
        {
            string key = $"RacingSkill_{t}";
            if (PlayerPrefs.HasKey(key))
                PlayerPrefs.DeleteKey(key);
        }
        _levels.Clear();
        PlayerPrefs.Save();
    }
}