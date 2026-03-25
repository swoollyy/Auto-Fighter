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

    [Header("Master Control")]
    [Tooltip("If disabled, all skills return level 0 and have no effect.")]
    [SerializeField] private bool skillsEnabled = true;

    [Tooltip("If enabled, all skills are revealed at start (ignores individual revealedAtStart settings).")]
    [SerializeField] private bool revealAllSkillsAtStart = true;



    /// <summary>
    /// Master toggle to enable/disable all skill effects at runtime.
    /// </summary>
    public bool SkillsEnabled
    {
        get => skillsEnabled;
        set => skillsEnabled = value;
    }

    [Header("Economy")]
    [SerializeField] private int playerCurrency = 0;

    [Header("Sprockets Currency")]
    [SerializeField] private int playerSprockets = 0;

    private const string SprocketsKey = "Racing_Sprockets";

    private bool _hasEverEarnedSprockets = false;
    private const string FirstSprocketKey = "Racing_HasEarnedSprockets";

    public event Action OnFirstSprocketEarned;

    public event Action<int> OnSprocketsChanged;

    public event Action<int> OnCurrencyChanged;
    public event Action<SkillType, int> OnLevelChanged;
    public event Action OnSkillsReset;
    public event Action<SkillDefinition> OnSkillRevealed;

    private SkillTreeState _state;
    private readonly Dictionary<SkillType, SkillDefinition> _map = new();
    private readonly HashSet<SkillType> _revealedSkills = new();
    private RacingQuestUnlockManager _questMgr;

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

        playerSprockets = PlayerPrefs.GetInt(SprocketsKey, playerSprockets);

        _hasEverEarnedSprockets = PlayerPrefs.GetInt(FirstSprocketKey, 0) == 1;

        // If player already has sprockets, they've earned them before
        if (playerSprockets > 0)
            _hasEverEarnedSprockets = true;

        _revealedSkills.Clear();
        foreach (var def in skills)
        {
            if (def == null) continue;

            // Reveal if master toggle is on OR individual skill has revealedAtStart
            if (revealAllSkillsAtStart || def.revealedAtStart)
            {
                RevealSkill(def);
            }
            // Also reveal first-sprocket skills if player has earned sprockets before
            else if (def.revealOnFirstSprocket && _hasEverEarnedSprockets)
            {
                RevealSkill(def);
            }
        }

        OnCurrencyChanged?.Invoke(playerCurrency);
        OnSprocketsChanged?.Invoke(playerSprockets);
        foreach (SkillType t in Enum.GetValues(typeof(SkillType)))
            OnLevelChanged?.Invoke(t, GetLevel(t));

        HookQuestRevealEvents();
        SyncQuestUnlockReveals();
    }

    private void OnDestroy()
    {
        UnhookQuestRevealEvents();
    }

    private void RevealSkill(SkillDefinition def)
    {
        if (!def) return;
        if (_revealedSkills.Add(def.type))
            OnSkillRevealed?.Invoke(def);
    }

    public bool IsSkillRevealed(SkillType type)
    {
        if (_revealedSkills.Contains(type))
            return true;

        // Quest-completed unlock nodes should appear even if not pre-revealed.
        var quest = RacingQuestUnlockManager.Instance;
        if (quest == null) return false;

        switch (type)
        {
            case SkillType.ForcefieldUnlock: return quest.IsForcefieldUnlocked;
            case SkillType.TurretUnlock: return quest.IsTurretUnlocked;
            case SkillType.CoinFriendUnlock: return quest.IsCoinFriendUnlocked;
            default: return false;
        }
    }
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
    public int GetLevel(SkillType t) => skillsEnabled ? _state.GetLevel(t) : 0;

    public bool TryPurchase(SkillType type)
    {
        if (!_map.TryGetValue(type, out var def)) return false;
        int nextLevel = GetLevel(type) + 1;
        if (nextLevel > def.maxLevel) return false;
        if (!MeetsQuestGateForPurchase(type, nextLevel)) return false;
        int cost = def.GetCostForLevel(nextLevel);
        if (playerCurrency < cost) return false;

        if (_state.Increment(type, def.maxLevel))
        {
            playerCurrency -= cost;
            SaveCurrency();
            _state.Save();
            int newLvl = GetLevel(type);
            RacingQuestUnlockManager.Instance?.NotifyUnlockNodePurchased(type);
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
        // If skills disabled, return base value unchanged
        if (!skillsEnabled) return baseValue;

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


    /// <summary>
    /// Current sprockets balance.
    /// </summary>
    public int Sprockets => playerSprockets;

    private void SaveSprockets()
    {
        PlayerPrefs.SetInt(SprocketsKey, playerSprockets);
        PlayerPrefs.Save();
    }

    /// <summary>
    /// Add sprockets to player balance.
    /// </summary>
    public void AddSprockets(int amount)
    {
        if (amount <= 0) return;

        bool wasFirstSprocket = !_hasEverEarnedSprockets && playerSprockets == 0;

        playerSprockets += amount;
        SaveSprockets();
        OnSprocketsChanged?.Invoke(playerSprockets);

        // Check for first sprocket ever earned
        if (wasFirstSprocket)
        {
            _hasEverEarnedSprockets = true;
            PlayerPrefs.SetInt(FirstSprocketKey, 1);
            PlayerPrefs.Save();

            // Reveal skills marked for first-sprocket reveal
            RevealFirstSprocketSkills();
            OnFirstSprocketEarned?.Invoke();

            Debug.Log("[SkillTreeManager] First sprocket earned! Revealing sprocket skills.");
        }
    }

    private void RevealFirstSprocketSkills()
    {
        foreach (var def in skills)
        {
            if (def == null) continue;
            if (def.revealOnFirstSprocket && !IsSkillRevealed(def.type))
            {
                RevealSkill(def);
            }
        }
    }

    /// <summary>
    /// Remove sprockets from player balance.
    /// </summary>
    public void RemoveSprockets(int amount)
    {
        if (amount <= 0) return;
        playerSprockets = Mathf.Max(0, playerSprockets - amount);
        SaveSprockets();
        OnSprocketsChanged?.Invoke(playerSprockets);
    }

    /// <summary>
    /// Get coins to award for a close call. Returns 0 if skill not unlocked.
    /// Level 1 = 1 coin, Level 2 = 2 coins, etc.
    /// </summary>
    public int GetCloseCallCoins()
    {
        int level = GetLevel(SkillType.CloseCallCoins_Add);
        if (level <= 0) return 0;

        // Base coins = level, then apply any multiplier
        float coins = level;
        coins = ApplyStatChain(coins, SkillType.CloseCallCoins_Add, SkillType.CloseCallCoins_Mul);
        return Mathf.Max(0, Mathf.RoundToInt(coins));
    }

    /// <summary>
    /// Close-call utility: reset boost cooldowns.
    /// </summary>
    public bool IsCloseCallResetBoostCooldownsUnlocked => GetLevel(SkillType.CloseCallRefreshBoostCooldownsUnlock) > 0;

    /// <summary>
    /// Close-call utility: reset equipped item/quest ability cooldowns.
    /// </summary>
    public bool IsCloseCallResetItemCooldownsUnlocked => GetLevel(SkillType.CloseCallResetItemCooldownsUnlock) > 0;

    /// <summary>
    /// Get the duration of close call speed boost. Returns 0 if not unlocked.
    /// Base duration comes from CarController, skill adds to it.
    /// </summary>
    public float GetCloseCallSpeedBoostDuration(float baseDuration)
    {
        if (!IsCloseCallResetBoostCooldownsUnlocked) return 0f;
        return ApplyStatChain(baseDuration, SkillType.CloseCallSpeedBoostDuration_Add, SkillType.CloseCallSpeedBoostDuration_Mul);
    }

    /// <summary>
    /// Get close call invincibility duration. Returns 0 if skill not unlocked.
    /// Level determines base duration.
    /// </summary>
    public float GetCloseCallInvincibilityDuration()
    {
        int level = GetLevel(SkillType.CloseCallInvincibility_Add);
        if (level <= 0) return 0f;

        // Base duration = 0.3s per level
        float baseDuration = level * 0.3f;
        return ApplyStatChain(baseDuration, SkillType.CloseCallInvincibility_Add, SkillType.CloseCallInvincibility_Mul);
    }

    // ------------------------------------------------------------------------
    // Coin Collecting Friend
    // ------------------------------------------------------------------------
    public bool IsCoinFriendUnlocked()
    {
        return GetLevel(SkillType.CoinFriendUnlock) > 0;
    }

    public float GetCoinFriendRange(float baseRange)
    {
        float v = ApplyStatChain(baseRange, SkillType.CoinFriendRange_Add, SkillType.CoinFriendRange_Mul);
        return Mathf.Max(0.1f, v);
    }

    public float GetCoinFriendCooldown(float baseCooldown)
    {
        float v = ApplyStatChain(baseCooldown, SkillType.CoinFriendCooldown_Add, SkillType.CoinFriendCooldown_Mul);
        return Mathf.Max(0.05f, v);
    }

    public int GetCoinFriendValueBonus()
    {
        // Explicitly +1 per level.
        return Mathf.Max(0, GetLevel(SkillType.CoinFriendValue_Add));
    }

    /// <summary>
    /// Check if player can afford a sprocket cost.
    /// </summary>
    public bool CanAffordSprockets(int cost) => playerSprockets >= cost;


    /// <summary>
    /// Try to purchase a skill using sprockets instead of coins.
    /// </summary>
    public bool TryPurchaseWithSprockets(SkillType type)
    {
        if (!_map.TryGetValue(type, out var def)) return false;
        int nextLevel = GetLevel(type) + 1;
        if (nextLevel > def.maxLevel) return false;
        if (!MeetsQuestGateForPurchase(type, nextLevel)) return false;

        // Use sprocket cost (you can add a separate field to SkillDefinition for this)
        int cost = def.GetSprocketCostForLevel(nextLevel);
        if (playerSprockets < cost) return false;

        if (_state.Increment(type, def.maxLevel))
        {
            playerSprockets -= cost;
            SaveSprockets();
            _state.Save();
            int newLvl = GetLevel(type);
            RacingQuestUnlockManager.Instance?.NotifyUnlockNodePurchased(type);
            OnSprocketsChanged?.Invoke(playerSprockets);
            OnLevelChanged?.Invoke(type, newLvl);
            EvaluateProgressiveUnlocks(def, newLvl);
            return true;
        }
        return false;
    }

    /// <summary>
    /// Smart purchase that automatically uses the correct currency based on skill's usesSprockets flag.
    /// </summary>
    public bool TryPurchaseSmart(SkillType type)
    {
        if (!_map.TryGetValue(type, out var def)) return false;

        if (def.usesSprockets)
            return TryPurchaseWithSprockets(type);
        else
            return TryPurchase(type);
    }

    /// <summary>
    /// Get the next level cost in the correct currency based on skill's usesSprockets flag.
    /// </summary>
    public int GetNextLevelCostSmart(SkillType type)
    {
        if (!_map.TryGetValue(type, out var def)) return int.MaxValue;
        int nextLevel = GetLevel(type) + 1;
        if (nextLevel > def.maxLevel) return 0;

        if (def.usesSprockets)
            return def.GetSprocketCostForLevel(nextLevel);
        else
            return def.GetCostForLevel(nextLevel);
    }

    /// <summary>
    /// Check if player can afford the next level of a skill (checks correct currency).
    /// </summary>
    public bool CanAffordNextLevel(SkillType type)
    {
        if (!_map.TryGetValue(type, out var def)) return false;
        int nextLevel = GetLevel(type) + 1;
        if (nextLevel > def.maxLevel) return false;
        if (!MeetsQuestGateForPurchase(type, nextLevel)) return false;

        if (def.usesSprockets)
        {
            int cost = def.GetSprocketCostForLevel(nextLevel);
            return playerSprockets >= cost;
        }
        else
        {
            int cost = def.GetCostForLevel(nextLevel);
            return playerCurrency >= cost;
        }
    }

    /// <summary>
    /// Get the currency name for display based on skill's usesSprockets flag.
    /// </summary>
    public string GetCurrencyNameForSkill(SkillType type)
    {
        if (!_map.TryGetValue(type, out var def)) return "Coins";
        return def.usesSprockets ? "Sprockets" : "Coins";
    }

    public bool IsQuestGateSatisfiedForSkill(SkillType type)
    {
        return MeetsQuestGateForPurchase(type, GetLevel(type) + 1);
    }

    private static bool TryMapQuestGate(SkillType type, out RacingQuestType questType)
    {
        switch (type)
        {
            case SkillType.ForcefieldUnlock:
                questType = RacingQuestType.Forcefield;
                return true;
            case SkillType.TurretUnlock:
                questType = RacingQuestType.Turret;
                return true;
            case SkillType.CoinFriendUnlock:
                questType = RacingQuestType.CoinFriend;
                return true;
            default:
                questType = default;
                return false;
        }
    }

    private static bool MeetsQuestGateForPurchase(SkillType type, int nextLevel)
    {
        // Only gate the first purchase of unlock-node skills.
        if (nextLevel > 1) return true;
        if (!TryMapQuestGate(type, out var questType)) return true;

        var quest = RacingQuestUnlockManager.Instance;
        if (quest == null) return false;

        switch (questType)
        {
            case RacingQuestType.Forcefield: return quest.IsForcefieldUnlocked;
            case RacingQuestType.Turret: return quest.IsTurretUnlocked;
            default: return quest.IsCoinFriendUnlocked;
        }
    }


    // ------------------------------------------------------------------------
    // Master Skill Control
    // ------------------------------------------------------------------------

    /// <summary>
    /// Enable all skill effects.
    /// </summary>
    public void EnableAllSkills()
    {
        skillsEnabled = true;
        Debug.Log("[SkillTreeManager] All skills ENABLED");
    }

    /// <summary>
    /// Disable all skill effects (skills return to level 0 behavior).
    /// </summary>
    public void DisableAllSkills()
    {
        skillsEnabled = false;
        Debug.Log("[SkillTreeManager] All skills DISABLED");
    }

    /// <summary>
    /// Toggle skill effects on/off.
    /// </summary>
    public void ToggleSkills()
    {
        skillsEnabled = !skillsEnabled;
        Debug.Log($"[SkillTreeManager] Skills {(skillsEnabled ? "ENABLED" : "DISABLED")}");
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
        {
            if (def == null) continue;
            if (revealAllSkillsAtStart || def.revealedAtStart)
                RevealSkill(def);
        }

        OnSkillsReset?.Invoke();
        _hasEverEarnedSprockets = false;
        PlayerPrefs.DeleteKey(FirstSprocketKey);
        playerSprockets = 0;
        PlayerPrefs.DeleteKey(SprocketsKey);
        OnSprocketsChanged?.Invoke(playerSprockets);

        RacingQuestUnlockManager.Instance?.ClearAllData();
        SyncQuestUnlockReveals();
    }

    public bool IsPassiveMashUnlocked => GetLevel(SkillType.MashPassiveUnlock) > 0;
    public bool HasEverEarnedSprockets => _hasEverEarnedSprockets;
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

    private void HookQuestRevealEvents()
    {
        _questMgr = RacingQuestUnlockManager.Instance;
        if (_questMgr == null) return;
        _questMgr.OnQuestUnlocked -= HandleQuestUnlockedReveal;
        _questMgr.OnQuestUnlocked += HandleQuestUnlockedReveal;
    }

    private void UnhookQuestRevealEvents()
    {
        if (_questMgr == null) return;
        _questMgr.OnQuestUnlocked -= HandleQuestUnlockedReveal;
    }

    private void HandleQuestUnlockedReveal(RacingQuestType questType)
    {
        RevealUnlockSkillForQuest(questType);
    }

    private void SyncQuestUnlockReveals()
    {
        var q = RacingQuestUnlockManager.Instance;
        if (q == null) return;
        if (q.IsForcefieldUnlocked) RevealUnlockSkillForQuest(RacingQuestType.Forcefield);
        if (q.IsTurretUnlocked) RevealUnlockSkillForQuest(RacingQuestType.Turret);
        if (q.IsCoinFriendUnlocked) RevealUnlockSkillForQuest(RacingQuestType.CoinFriend);
    }

    public void RefreshQuestUnlockReveals()
    {
        SyncQuestUnlockReveals();
    }

    private void RevealUnlockSkillForQuest(RacingQuestType questType)
    {
        SkillType target;
        switch (questType)
        {
            case RacingQuestType.Forcefield:
                target = SkillType.ForcefieldUnlock;
                break;
            case RacingQuestType.Turret:
                target = SkillType.TurretUnlock;
                break;
            default:
                target = SkillType.CoinFriendUnlock;
                break;
        }

        if (_map.TryGetValue(target, out var def) && def != null)
            RevealSkill(def);
    }
}