using System;
using System.Collections.Generic;
using UnityEngine;

[DefaultExecutionOrder(-100)]
public class RacingSkillTreeManager : MonoBehaviour
{
    public static RacingSkillTreeManager Instance { get; private set; }

    [Header("Load")]
    public List<SkillDefinition> skills = new();

    [Header("Economy")]
    [SerializeField] private int playerCurrency = 0;

    public event Action<int> OnCurrencyChanged;
    public event Action<SkillType, int> OnLevelChanged;
    public event Action OnSkillsReset;
    public event Action<SkillDefinition> OnSkillRevealed; // NEW

    private SkillTreeState _state;
    private readonly Dictionary<SkillType, SkillDefinition> _map = new();

    // NEW: revealed (visible) skills
    private readonly HashSet<SkillType> _revealedSkills = new();

    private const string CurrencyKey = "Racing_Currency";

    void Awake()
    {
        if (Instance && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);

        _map.Clear();
        foreach (var def in skills)
            if (def && !_map.ContainsKey(def.type))
                _map[def.type] = def;

        _state = new SkillTreeState();
        _state.ClearPersistent(); // fresh start

        playerCurrency = PlayerPrefs.GetInt(CurrencyKey, playerCurrency);

        // Seed initial revealed skills
        foreach (var def in skills)
        {
            if (def && def.revealedAtStart)
                RevealSkill(def);
        }
    }

    void OnApplicationQuit()
    {
        ClearAllData();
    }

    // NEW: reveal logic
    private void RevealSkill(SkillDefinition def)
    {
        if (!def) return;
        if (_revealedSkills.Add(def.type))
            OnSkillRevealed?.Invoke(def);
    }

    public bool IsSkillRevealed(SkillType type) => _revealedSkills.Contains(type);
    public IReadOnlyCollection<SkillType> RevealedSkills => _revealedSkills;

    // ---------------- Economy ----------------
    public int Currency => playerCurrency;
    private void SaveCurrency()
    {
        PlayerPrefs.SetInt(CurrencyKey, playerCurrency);
        PlayerPrefs.Save();
    }
    public void AddCurrency(int amount)
    {
        if (amount <= 0) return;
        playerCurrency += amount;
        SaveCurrency();
        OnCurrencyChanged?.Invoke(playerCurrency);
    }

    public int GetNextLevelCost(SkillType type)
    {
        if (!_map.TryGetValue(type, out var def)) return int.MaxValue;
        int nextLevel = GetLevel(type) + 1;
        if (nextLevel > def.maxLevel) return 0;
        return def.GetCostForLevel(nextLevel);
    }

    public SkillDefinition GetDefinition(SkillType type)
    {
        _map.TryGetValue(type, out var def);
        return def;
    }

    public IReadOnlyList<SkillDefinition> AllSkills => skills;

    // ---------------- Levels & purchases ----------------
    public int GetLevel(SkillType t) => _state.GetLevel(t);

    public bool TryPurchase(SkillType type)
    {
        if (!_map.TryGetValue(type, out var def)) return false;
        int nextLevel = GetLevel(type) + 1;
        if (nextLevel > def.maxLevel) return false;

        int cost = def.GetCostForLevel(nextLevel);
        if (playerCurrency < cost) return false;

        if (_state.Increment(type, def.maxLevel))
        {
            playerCurrency -= cost;
            SaveCurrency();
            _state.Save();

            OnCurrencyChanged?.Invoke(playerCurrency);
            OnLevelChanged?.Invoke(type, GetLevel(type));

            // NEW: evaluate unlocks
            EvaluateProgressiveUnlocks(def, GetLevel(type));
            return true;
        }
        return false;
    }

    private void EvaluateProgressiveUnlocks(SkillDefinition def, int newLevel)
    {
        if (!def) return;
        foreach (var unlocked in def.GetUnlocksForLevel(newLevel))
            RevealSkill(unlocked);
    }

    // ---------------- Raw effect retrieval unchanged ----------------
    public float GetRawEffectValue(SkillType type)
    {
        if (!_map.TryGetValue(type, out var def))
            return 0f;
        int lvl = GetLevel(type);
        if (lvl <= 0)
            return def.mode == SkillApplicationMode.Multiplicative ? 1f : 0f;
        return def.GetValueAtLevel(lvl);
    }

    public SkillApplicationMode GetEffectMode(SkillType type)
    {
        if (_map.TryGetValue(type, out var def))
            return def.mode;
        return SkillApplicationMode.Additive;
    }

    public float GetDisplayMultiplier(SkillType type)
    {
        var mode = GetEffectMode(type);
        var v = GetRawEffectValue(type);
        return mode == SkillApplicationMode.Multiplicative ? v : (1f + v);
    }

    public float ApplyStat(SkillType type, float baseValue)
    {
        if (!_map.TryGetValue(type, out var def))
            return baseValue;

        int lvl = GetLevel(type);
        if (lvl <= 0) return baseValue;

        float v = def.GetValueAtLevel(lvl);
        if (def.mode == SkillApplicationMode.Multiplicative)
            return baseValue * Mathf.Max(0f, v);
        return baseValue + v;
    }

    public float ApplyStatChain(float baseValue, params SkillType[] types)
    {
        float val = baseValue;
        if (types == null) return val;
        for (int i = 0; i < types.Length; i++)
            val = ApplyStat(types[i], val);
        return val;
    }

    public float GetAccelerationMultiplier() => GetDisplayMultiplier(SkillType.Acceleration);
    public float GetMaxSpeedMultiplier() => GetDisplayMultiplier(SkillType.MaxSpeed);
    public float GetFuelEfficiencyMultiplier() => GetDisplayMultiplier(SkillType.FuelEfficiency);
    public float GetSteeringMultiplier() => GetDisplayMultiplier(SkillType.SteeringResponsiveness);

    // ---------------- Reset ----------------
    public void ClearAllData()
    {
        _state.ClearPersistent();
        playerCurrency = 0;
        SaveCurrency();

        OnCurrencyChanged?.Invoke(playerCurrency);
        foreach (SkillType t in Enum.GetValues(typeof(SkillType)))
            OnLevelChanged?.Invoke(t, 0);

        _revealedSkills.Clear();
        foreach (var def in skills)
            if (def && def.revealedAtStart)
                RevealSkill(def);

        OnSkillsReset?.Invoke();
    }
}