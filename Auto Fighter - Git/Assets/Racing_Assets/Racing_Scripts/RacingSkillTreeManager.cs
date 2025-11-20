using System;
using System.Collections.Generic;
using UnityEngine;

[DefaultExecutionOrder(-100)]
public class RacingSkillTreeManager : MonoBehaviour
{
    public static RacingSkillTreeManager Instance { get; private set; }

    [Header("Load")]
    [Tooltip("Assign all skill definitions here or load via Resources/Addressables.")]
    public List<SkillDefinition> skills = new();

    [Header("Economy")]
    [SerializeField] private int playerCurrency = 0;

    // Events
    public event Action<int> OnCurrencyChanged;
    public event Action<SkillType, int> OnLevelChanged;
    public event Action OnSkillsReset;

    private SkillTreeState _state;
    private readonly Dictionary<SkillType, SkillDefinition> _map = new();

    private const string CurrencyKey = "Racing_Currency";

    private static readonly HashSet<SkillType> _additiveSkillTypes = new HashSet<SkillType>
{
    // Core car stats
    SkillType.Acceleration_Add,
    SkillType.MaxSpeed_Add,

    // Fuel / turning / turret
    SkillType.MaxFuel_Add,
    SkillType.IdleFuelUse_Add,
    SkillType.DrivingFuelUse_Add,
    SkillType.TurnSpeed_Add,
    SkillType.TurretDamage_Add,
    SkillType.TurretProjectileSpeed_Add,
    SkillType.TurretCooldown_Add,
    SkillType.TurretBulletLifetime_Add,
    SkillType.TurretConeAngle_Add,
    SkillType.TurretScanRadius_Add,
};

    private static readonly HashSet<SkillType> _multiplicativeSkillTypes = new HashSet<SkillType>
{
    // Core car stats
    SkillType.Acceleration_Mul,
    SkillType.MaxSpeed_Mul,

    // Fuel / turning / turret
    SkillType.MaxFuel_Mul,
    SkillType.IdleFuelUse_Mul,
    SkillType.DrivingFuelUse_Mul,
    SkillType.TurnSpeed_Mul,
    SkillType.TurretDamage_Mul,
    SkillType.TurretProjectileSpeed_Mul,
    SkillType.TurretCooldown_Mul,
    SkillType.TurretBulletLifetime_Mul,
    SkillType.TurretConeAngle_Mul,
    SkillType.TurretScanRadius_Mul,
};

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
        _state.ClearPersistent(); // start fresh

        playerCurrency = PlayerPrefs.GetInt(CurrencyKey, playerCurrency);
    }

    void OnApplicationQuit()
    {
        ClearAllData();
    }

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
            return true;
        }
        return false;
    }

    // ---------------- Unified skill effect retrieval ----------------
    // For a given skill type:
    // If Multiplicative: value returned is the multiplier (>=1 ideally).
    // If Additive: value returned is the additive amount (raw units).
    // Level 0 → returns neutral (1 for mult, 0 for add).
    // Level 0 → strict neutral: multiplicative = 1, additive = 0.
    public float GetRawEffectValue(SkillType type)
    {
        if (!_map.TryGetValue(type, out var def))
        {
            // No definition -> treat as neutral (no effect)
            return 0f;
        }

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

    // Convenience for UI: always returns a multiplier (>=1).
    // Additive effects are displayed as (1 + additiveValue / referenceUnit).
    // For simplicity (no stat context here) we treat additive as (1 + value).
    public float GetDisplayMultiplier(SkillType type)
    {
        var mode = GetEffectMode(type);
        var v = GetRawEffectValue(type);
        return mode == SkillApplicationMode.Multiplicative ? v : (1f + v);
    }

    /// <summary>
    /// Apply a single skill to a base stat value.
    /// If the skill is Additive: result = baseValue + value.
    /// If Multiplicative:       result = baseValue * value.
    /// If no definition / level 0: returns baseValue unchanged.
    /// </summary>
    public float ApplyStat(SkillType type, float baseValue)
    {
        if (!_map.TryGetValue(type, out var def))
            return baseValue;

        int lvl = GetLevel(type);
        if (lvl <= 0)
            return baseValue;

        float v = def.GetValueAtLevel(lvl);

        if (def.mode == SkillApplicationMode.Multiplicative)
        {
            // v is the multiplier (e.g. 1.10 for +10%)
            return baseValue * Mathf.Max(0f, v);
        }
        else
        {
            // v is the additive delta (can be negative if you want)
            return baseValue + v;
        }
    }

    /// <summary>
    /// Apply multiple skills in sequence to the same base stat.
    /// Lets you chain e.g. Additive then Multiplicative for the same parameter.
    /// </summary>
    public float ApplyStatChain(float baseValue, params SkillType[] types)
    {
        float val = baseValue;
        if (types == null) return val;

        for (int i = 0; i < types.Length; i++)
            val = ApplyStat(types[i], val);

        return val;
    }


    // Backward compatibility wrappers for UI calls
    public float GetAccelerationMultiplier() => GetDisplayMultiplier(SkillType.Acceleration);
    public float GetMaxSpeedMultiplier() => GetDisplayMultiplier(SkillType.MaxSpeed);
    public float GetFuelEfficiencyMultiplier() => GetDisplayMultiplier(SkillType.FuelEfficiency);
    public float GetSteeringMultiplier() => GetDisplayMultiplier(SkillType.SteeringResponsiveness);

    // ---------------- Reset / Clear ----------------
    public void ClearAllData()
    {
        _state.ClearPersistent();
        playerCurrency = 0;
        SaveCurrency();

        OnCurrencyChanged?.Invoke(playerCurrency);
        foreach (SkillType t in Enum.GetValues(typeof(SkillType)))
            OnLevelChanged?.Invoke(t, 0);

        OnSkillsReset?.Invoke();
    }
}