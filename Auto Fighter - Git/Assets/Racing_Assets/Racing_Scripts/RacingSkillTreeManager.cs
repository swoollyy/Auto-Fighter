using System;
using System.Collections.Generic;
using System.Linq;
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
    public event Action<SkillDefinition> OnSkillRevealed;

    private SkillTreeState _state;
    private readonly Dictionary<SkillType, SkillDefinition> _map = new();
    private readonly HashSet<SkillType> _revealedSkills = new();

    private const string CurrencyKey = "Racing_Currency";

    void Awake()
    {
        if (Instance && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);

        _map.Clear();
        foreach (var def in skills)
        {
            if (def && !_map.ContainsKey(def.type))
                _map[def.type] = def;
        }

        _state = new SkillTreeState();
        ClearAllData(); // TEMP (remove if you want persistence)

        playerCurrency = PlayerPrefs.GetInt(CurrencyKey, playerCurrency);

        _revealedSkills.Clear();
        foreach (var def in skills)
            if (def && def.revealedAtStart)
                RevealSkill(def);

        OnCurrencyChanged?.Invoke(playerCurrency);
        foreach (SkillType t in Enum.GetValues(typeof(SkillType)))
            OnLevelChanged?.Invoke(t, GetLevel(t));
    }

    private void RevealSkill(SkillDefinition def)
    {
        if (!def) return;
        if (_revealedSkills.Add(def.type))
            OnSkillRevealed?.Invoke(def);
    }

    public bool IsSkillRevealed(SkillType type) => _revealedSkills.Contains(type);
    public IReadOnlyCollection<SkillType> RevealedSkills => _revealedSkills;

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
    public void RemoveCurrency(int amount)
    {
        if (amount <= 0) return;
        playerCurrency = Mathf.Max(0, playerCurrency - amount);
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
            int newLvl = GetLevel(type);
            OnCurrencyChanged?.Invoke(playerCurrency);
            OnLevelChanged?.Invoke(type, newLvl);
            EvaluateProgressiveUnlocks(def, newLvl);
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

    // Add these helpers anywhere in the class body
    public float GetFuelPickupSpawnRateMultiplier()
    {
        // 1.0 baseline, then apply add/mul chain, clamped >= 0
        float baseVal = 1f;
        baseVal = ApplyStatChain(baseVal, SkillType.FuelPickupSpawnRate_Add, SkillType.FuelPickupSpawnRate_Mul);
        return Mathf.Max(0f, baseVal);
    }

    public float GetFuelPickupAmount(float baseAmount = 15f)
    {
        // Start at baseAmount (default 15), then apply add/mul chain
        float v = ApplyStatChain(baseAmount, SkillType.FuelPickupAmount_Add, SkillType.FuelPickupAmount_Mul);
        return Mathf.Max(0f, v);
    }

    public bool IsFuelPickupUnlocked()
    {
        return GetLevel(SkillType.FuelPickupUnlock) > 0;
    }

    public float GetHPPickupSpawnRateMultiplier()
    {
        float baseVal = 1f;
        baseVal = ApplyStatChain(baseVal, SkillType.HPPickupSpawnRate_Add, SkillType.HPPickupSpawnRate_Mul);
        return Mathf.Max(0f, baseVal);
    }

    public float GetHPPickupAmount(float baseAmount = 20f)
    {
        float v = ApplyStatChain(baseAmount, SkillType.HPPickupAmount_Add, SkillType.HPPickupAmount_Mul);
        return Mathf.Max(0f, v);
    }

    public bool IsHPPickupUnlocked()
    {
        return GetLevel(SkillType.HPPickupUnlock) > 0;
    }

    public float ApplyStatChain(float baseValue, params SkillType[] types)
    {
        float val = baseValue;
        if (types == null) return val;
        for (int i = 0; i < types.Length; i++)
            val = ApplyStat(types[i], val);
        return val;
    }

    public float GetCoinSpawnRateMultiplier()
    {
        // Effective probability scaling; clamp to [0, +inf) then caller clamps to [0,1]
        float baseVal = 1f;
        baseVal = ApplyStatChain(baseVal, SkillType.CoinSpawnRate_Add, SkillType.CoinSpawnRate_Mul);
        return Mathf.Max(0f, baseVal);
    }

    // NEW: feature flag for obstacle-on-obstacle impact damage
    public bool IsForcefieldImpactDamageUnlocked()
    {
        return GetLevel(SkillType.ForcefieldImpactDamageUnlock) > 0;
    }

    // NEW: damage amount (base 1.0f scaled by add/mul chain)
    public float GetForcefieldImpactDamageAmount(float baseAmount = 1f)
    {
        float v = ApplyStatChain(baseAmount, SkillType.ForcefieldImpactDamage_Add, SkillType.ForcefieldImpactDamage_Mul);
        return Mathf.Max(0f, v);
    }

    public float GetCoinDoubleChance()
    {
        // Starts at 0; additive levels add raw probability, multiplicative levels scale it.
        float chance = ApplyStatChain(
            0f,
            SkillType.CoinDoubleChance_Add,
            SkillType.CoinDoubleChance_Mul
        );
        return Mathf.Clamp01(chance);
    }

    // ------------------------------------------------------------------------
    // NEW: Drift‑Held Boost helpers
    // ------------------------------------------------------------------------
    public bool IsDriftHeldBoostUnlocked()
    {
        return GetLevel(SkillType.DriftHeldBoostUnlock) > 0;
    }

    public float GetDriftHeldBoostForceScaled(float baseForce)
    {
        return ApplyStatChain(baseForce, SkillType.DriftHeldBoostForce_Add, SkillType.DriftHeldBoostForce_Mul);
    }

    public float GetDriftHeldBoostDurationScaled(float baseDuration)
    {
        return ApplyStatChain(baseDuration, SkillType.DriftHeldBoostDuration_Add, SkillType.DriftHeldBoostDuration_Mul);
    }

    public float GetDriftHeldBoostMaxSpeedMultScaled(float baseMaxMult)
    {
        return ApplyStatChain(baseMaxMult, SkillType.DriftHeldBoostMaxSpeedMult_Add, SkillType.DriftHeldBoostMaxSpeedMult_Mul);
    }

    // ------------------------------------------------------------------------
    // NEW: Distance coins per meter
    // ------------------------------------------------------------------------
    public float GetDistanceCoinsPerMeter(float baseCoinsPerMeter)
    {
        float v = ApplyStatChain(baseCoinsPerMeter, SkillType.DistanceCoinsPerMeter_Add, SkillType.DistanceCoinsPerMeter_Mul);
        return Mathf.Max(0f, v);
    }


    // ------------------------------------------------------------------------
    // NEW: Coin base value additive helper
    // Returns integer amount to add to each coin's configured value (default base 0)
    // ------------------------------------------------------------------------
    public int GetCoinBaseAdd()
    {
        // start from 0 (no base add); apply add/mul chain (mul on 0 is neutral, but kept for symmetry)
        float v = ApplyStatChain(0f, SkillType.CoinBase_Add, SkillType.CoinBase_Mul);
        return Mathf.RoundToInt(Mathf.Max(0f, v));
    }

    // ------------------------------------------------------------------------
    // NEW: Drift‑held boost cooldown helper (returns seconds)
    // ------------------------------------------------------------------------
    public float GetDriftHeldBoostCooldownScaled(float baseCooldown)
    {
        float v = ApplyStatChain(baseCooldown, SkillType.DriftHeldBoostCooldown_Add, SkillType.DriftHeldBoostCooldown_Mul);
        return Mathf.Max(0.01f, v);
    }

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

    public float GetAccelerationMultiplier() => GetDisplayMultiplier(SkillType.Acceleration);
    public float GetMaxSpeedMultiplier() => GetDisplayMultiplier(SkillType.MaxSpeed);
    public float GetFuelEfficiencyMultiplier() => GetDisplayMultiplier(SkillType.FuelEfficiency);
    public float GetSteeringMultiplier() => GetDisplayMultiplier(SkillType.SteeringResponsiveness);
    public bool IsBoostUnlocked() => GetLevel(SkillType.BoostUnlock) > 0;
    public float GetBoostForceScaled(float baseForce) =>
        ApplyStatChain(baseForce, SkillType.BoostForce_Add, SkillType.BoostForce_Mul);
    public float GetBoostDurationScaled(float baseDuration) =>
        ApplyStatChain(baseDuration, SkillType.BoostDuration_Add, SkillType.BoostDuration_Mul);
    public float GetBoostMaxSpeedMultScaled(float baseMult) =>
        ApplyStatChain(baseMult, SkillType.BoostMaxSpeedMult_Add, SkillType.BoostMaxSpeedMult_Mul);
    public float GetBoostCooldownScaled(float baseCooldown) =>
        ApplyStatChain(baseCooldown, SkillType.BoostCooldown_Add, SkillType.BoostCooldown_Mul);
    public float GetBoostFuelCostScaled(float baseCost) =>
        ApplyStatChain(baseCost, SkillType.BoostFuelCost_Add, SkillType.BoostFuelCost_Mul);
}