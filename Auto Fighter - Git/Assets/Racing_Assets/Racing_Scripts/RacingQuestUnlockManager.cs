using System;
using System.Collections.Generic;
using UnityEngine;

public enum RacingQuestType
{
    Forcefield = 0,
    Turret = 1,
    CoinFriend = 2
}

public enum RacingQuestRunItem
{
    None = -1,
    Forcefield = 0,
    Turret = 1,
    CoinFriend = 2
}

[DisallowMultipleComponent]
public sealed class RacingQuestUnlockManager : MonoBehaviour
{
    [Serializable]
    private struct QuestTuning
    {
        public int forcefieldNoHpDeathRunsRequired;
        public int turretMashCompletionsRequired;
        public int coinFriendCoinsCollectedRequired;
    }

    [Serializable]
    public struct QuestProgressSnapshot
    {
        public RacingQuestType questType;
        public string title;
        public string description;
        public string reward;
        public int current;
        public int required;
        public bool unlocked;
    }

    public static RacingQuestUnlockManager Instance
    {
        get
        {
            if (_instance != null) return _instance;

            var found = FindObjectOfType<RacingQuestUnlockManager>(true);
            if (found != null)
            {
                _instance = found;
                return _instance;
            }

            var go = new GameObject("RacingQuestUnlockManager");
            _instance = go.AddComponent<RacingQuestUnlockManager>();
            DontDestroyOnLoad(go);
            return _instance;
        }
    }

    public event Action<RacingQuestType> OnQuestProgressChanged;
    public event Action<RacingQuestType> OnQuestUnlocked;
    public event Action OnInventoryChanged;

    [Header("Quest Requirements")]
    [SerializeField] private QuestTuning tuning = new QuestTuning
    {
        forcefieldNoHpDeathRunsRequired = 5,
        turretMashCompletionsRequired = 20,
        coinFriendCoinsCollectedRequired = 800
    };

    [Header("Run Item Inventory")]
    [SerializeField, Min(1)] private int defaultUnlockedItemSlots = 1;
    [SerializeField, Min(1)] private int maxItemSlots = 3;

    [Header("Testing Overrides")]
    [SerializeField] private bool testAutoCompleteForcefieldQuest;
    [SerializeField] private bool testAutoCompleteTurretQuest;
    [SerializeField] private bool testAutoCompleteCoinFriendQuest;

    private static RacingQuestUnlockManager _instance;

    private int _forcefieldNoHpDeathRunCompletions;
    private int _turretMashCompletions;
    private int _coinFriendCoinsCollected;

    private bool _forcefieldUnlocked;
    private bool _turretUnlocked;
    private bool _coinFriendUnlocked;
    private bool _forcefieldItemOwned;
    private bool _turretItemOwned;
    private bool _coinFriendItemOwned;
    private int _unlockedItemSlots;
    [SerializeField] private RacingQuestRunItem[] equippedItems = Array.Empty<RacingQuestRunItem>();
    [SerializeField] private RacingQuestRunItem[] unlockedItemOrder = Array.Empty<RacingQuestRunItem>();

    private const string KeyForcefieldNoHpDeathRunCompletions = "Quest_ForcefieldNoHpDeathRunCompletions_v1";
    private const string KeyTurretMashCompletions = "Quest_TurretMashCompletions_v1";
    private const string KeyCoinFriendCoinsCollected = "Quest_CoinFriendCoins_v1";
    private const string KeyForcefieldUnlocked = "Quest_ForcefieldUnlocked_v1";
    private const string KeyTurretUnlocked = "Quest_TurretUnlocked_v1";
    private const string KeyCoinFriendUnlocked = "Quest_CoinFriendUnlocked_v1";
    private const string KeyUnlockedItemSlots = "Quest_ItemSlots_v1";
    private const string KeyEquippedItemsCsv = "Quest_EquippedItemsCsv_v1";
    private const string KeyUnlockedItemOrderCsv = "Quest_UnlockedItemOrder_v1";
    private const string KeyForcefieldItemOwned = "Quest_ItemOwned_Forcefield_v1";
    private const string KeyTurretItemOwned = "Quest_ItemOwned_Turret_v1";
    private const string KeyCoinFriendItemOwned = "Quest_ItemOwned_CoinFriend_v1";

    public bool IsForcefieldUnlocked => _forcefieldUnlocked;
    public bool IsTurretUnlocked => _turretUnlocked;
    public bool IsCoinFriendUnlocked => _coinFriendUnlocked;
    public int UnlockedItemSlots => Mathf.Clamp(_unlockedItemSlots, 1, Mathf.Max(1, maxItemSlots));
    public IReadOnlyList<RacingQuestRunItem> EquippedItems => equippedItems;
    public IReadOnlyList<RacingQuestRunItem> UnlockedItemsInInventoryOrder => unlockedItemOrder;

    private void Awake()
    {
        if (_instance != null && _instance != this)
        {
            Destroy(gameObject);
            return;
        }

        _instance = this;
        DontDestroyOnLoad(gameObject);
        LoadState();
        EvaluateUnlocks(false);
        ApplyTestingQuestOverrides(fireEvents: false);
    }

    public void ClearAllData()
    {
        _forcefieldNoHpDeathRunCompletions = 0;
        _turretMashCompletions = 0;
        _coinFriendCoinsCollected = 0;

        _forcefieldUnlocked = false;
        _turretUnlocked = false;
        _coinFriendUnlocked = false;
        _forcefieldItemOwned = false;
        _turretItemOwned = false;
        _coinFriendItemOwned = false;

        _unlockedItemSlots = Mathf.Clamp(defaultUnlockedItemSlots, 1, Mathf.Max(1, maxItemSlots));
        equippedItems = new RacingQuestRunItem[Mathf.Max(1, maxItemSlots)];
        for (int i = 0; i < equippedItems.Length; i++)
            equippedItems[i] = RacingQuestRunItem.None;
        unlockedItemOrder = Array.Empty<RacingQuestRunItem>();

        PlayerPrefs.DeleteKey(KeyForcefieldNoHpDeathRunCompletions);
        PlayerPrefs.DeleteKey(KeyTurretMashCompletions);
        PlayerPrefs.DeleteKey(KeyCoinFriendCoinsCollected);
        PlayerPrefs.DeleteKey(KeyForcefieldUnlocked);
        PlayerPrefs.DeleteKey(KeyTurretUnlocked);
        PlayerPrefs.DeleteKey(KeyCoinFriendUnlocked);
        PlayerPrefs.DeleteKey(KeyUnlockedItemSlots);
        PlayerPrefs.DeleteKey(KeyEquippedItemsCsv);
        PlayerPrefs.DeleteKey(KeyUnlockedItemOrderCsv);
        PlayerPrefs.DeleteKey(KeyForcefieldItemOwned);
        PlayerPrefs.DeleteKey(KeyTurretItemOwned);
        PlayerPrefs.DeleteKey(KeyCoinFriendItemOwned);
        PlayerPrefs.Save();

        // If testing overrides are enabled in inspector, immediately re-apply them
        // after the reset so quest-gated unlock nodes still appear during test runs.
        ApplyTestingQuestOverrides(fireEvents: true);

        OnQuestProgressChanged?.Invoke(RacingQuestType.Forcefield);
        OnQuestProgressChanged?.Invoke(RacingQuestType.Turret);
        OnQuestProgressChanged?.Invoke(RacingQuestType.CoinFriend);
        OnInventoryChanged?.Invoke();
    }

    public void RecordForcefieldEligibleRunCompletion(bool diedFromHp)
    {
        if (diedFromHp) return;
        _forcefieldNoHpDeathRunCompletions += 1;
        SaveState();
        EvaluateUnlocks(true);
        OnQuestProgressChanged?.Invoke(RacingQuestType.Forcefield);
    }

    // Legacy compatibility: forcefield quest no longer progresses from obstacle collisions.
    public void RecordObstacleCollision(int amount = 1) { }

    public void RecordCrashMashCompletion(int amount = 1)
    {
        if (amount <= 0) return;
        _turretMashCompletions += amount;
        SaveState();
        EvaluateUnlocks(true);
        OnQuestProgressChanged?.Invoke(RacingQuestType.Turret);
    }

    public void RecordCoinsCollected(int amount)
    {
        if (amount <= 0) return;
        _coinFriendCoinsCollected += amount;
        SaveState();
        EvaluateUnlocks(true);
        OnQuestProgressChanged?.Invoke(RacingQuestType.CoinFriend);
    }

    public QuestProgressSnapshot GetSnapshot(RacingQuestType questType)
    {
        switch (questType)
        {
            case RacingQuestType.Forcefield:
                return new QuestProgressSnapshot
                {
                    questType = RacingQuestType.Forcefield,
                    title = "Forcefield",
                    description = "Complete runs without dying from HP loss.",
                    reward = "Unlocks Forcefield unlock-node purchase.",
                    current = _forcefieldNoHpDeathRunCompletions,
                    required = Mathf.Max(1, tuning.forcefieldNoHpDeathRunsRequired),
                    unlocked = _forcefieldUnlocked
                };

            case RacingQuestType.Turret:
                return new QuestProgressSnapshot
                {
                    questType = RacingQuestType.Turret,
                    title = "Turret",
                    description = "Complete crash mash recovery a certain number of times.",
                    reward = "Unlocks Turret unlock-node purchase.",
                    current = _turretMashCompletions,
                    required = Mathf.Max(1, tuning.turretMashCompletionsRequired),
                    unlocked = _turretUnlocked
                };

            default:
                return new QuestProgressSnapshot
                {
                    questType = RacingQuestType.CoinFriend,
                    title = "Collection Friend",
                    description = "Collect a certain total amount of coins.",
                    reward = "Unlocks Collection Friend unlock-node purchase.",
                    current = _coinFriendCoinsCollected,
                    required = Mathf.Max(1, tuning.coinFriendCoinsCollectedRequired),
                    unlocked = _coinFriendUnlocked
                };
        }
    }

    public bool IsItemEquipped(RacingQuestRunItem item)
    {
        if (equippedItems == null) return false;
        for (int i = 0; i < equippedItems.Length; i++)
        {
            if (equippedItems[i] == item) return true;
        }
        return false;
    }

    public RacingQuestRunItem GetEquippedItemAtSlot(int slotIndex)
    {
        if (equippedItems == null) return RacingQuestRunItem.None;
        if (slotIndex < 0 || slotIndex >= Mathf.Min(UnlockedItemSlots, equippedItems.Length))
            return RacingQuestRunItem.None;
        return equippedItems[slotIndex];
    }

    public int GetEquippedCount()
    {
        if (equippedItems == null) return 0;
        int count = 0;
        for (int i = 0; i < Mathf.Min(UnlockedItemSlots, equippedItems.Length); i++)
            if (equippedItems[i] != RacingQuestRunItem.None) count++;
        return count;
    }

    public bool IsItemAvailableToEquip(RacingQuestRunItem item)
    {
        EnsureItemOwnershipFromPurchasedUnlockNodes();

        switch (item)
        {
            case RacingQuestRunItem.Forcefield:
                return _forcefieldItemOwned;
            case RacingQuestRunItem.Turret:
                return _turretItemOwned;
            default:
                return _coinFriendItemOwned;
        }
    }

    public void NotifyUnlockNodePurchased(SkillType unlockSkillType)
    {
        bool changed = false;
        switch (unlockSkillType)
        {
            case SkillType.ForcefieldUnlock:
                if (!_forcefieldItemOwned) { _forcefieldItemOwned = true; changed = true; }
                break;
            case SkillType.TurretUnlock:
                if (!_turretItemOwned) { _turretItemOwned = true; changed = true; }
                break;
            case SkillType.CoinFriendUnlock:
                if (!_coinFriendItemOwned) { _coinFriendItemOwned = true; changed = true; }
                break;
        }

        if (changed)
        {
            SaveState();
            OnInventoryChanged?.Invoke();
        }
    }

    public bool TryEquipItem(RacingQuestRunItem item)
    {
        if (!IsItemAvailableToEquip(item)) return false;
        if (IsItemEquipped(item)) return true;

        EnsureEquippedArrayInitialized();
        int slots = Mathf.Min(UnlockedItemSlots, equippedItems.Length);
        for (int i = 0; i < slots; i++)
        {
            if (equippedItems[i] == RacingQuestRunItem.None)
            {
                equippedItems[i] = item;
                SaveState();
                OnInventoryChanged?.Invoke();
                return true;
            }
        }
        return false;
    }

    public bool TryAssignItemToSlot(RacingQuestRunItem item, int slotIndex)
    {
        if (!IsItemAvailableToEquip(item)) return false;
        EnsureEquippedArrayInitialized();

        int slots = Mathf.Min(UnlockedItemSlots, equippedItems.Length);
        if (slotIndex < 0 || slotIndex >= slots) return false;

        int fromIdx = IndexOfItem(item);
        if (fromIdx == slotIndex) return true;

        RacingQuestRunItem target = equippedItems[slotIndex];
        if (fromIdx >= 0)
        {
            equippedItems[slotIndex] = item;
            equippedItems[fromIdx] = target;
            SaveState();
            OnInventoryChanged?.Invoke();
            return true;
        }

        if (target != RacingQuestRunItem.None)
            return false; // prevent overwriting when dragging from inventory button

        equippedItems[slotIndex] = item;
        SaveState();
        OnInventoryChanged?.Invoke();
        return true;
    }

    public bool UnequipItem(RacingQuestRunItem item)
    {
        if (equippedItems == null || equippedItems.Length == 0) return false;
        int idx = IndexOfItem(item);
        if (idx < 0) return false;
        equippedItems[idx] = RacingQuestRunItem.None;
        SaveState();
        OnInventoryChanged?.Invoke();
        return true;
    }

    public void SetUnlockedItemSlots(int slotCount)
    {
        _unlockedItemSlots = Mathf.Clamp(slotCount, 1, Mathf.Max(1, maxItemSlots));
        TrimEquippedToSlots();
        SaveState();
        OnInventoryChanged?.Invoke();
    }

    private void EvaluateUnlocks(bool fireEvents)
    {
        bool ffNow = _forcefieldNoHpDeathRunCompletions >= Mathf.Max(1, tuning.forcefieldNoHpDeathRunsRequired);
        bool turretNow = _turretMashCompletions >= Mathf.Max(1, tuning.turretMashCompletionsRequired);
        bool coinNow = _coinFriendCoinsCollected >= Mathf.Max(1, tuning.coinFriendCoinsCollectedRequired);

        if (ffNow && !_forcefieldUnlocked)
        {
            _forcefieldUnlocked = true;
            AddUnlockedItemIfMissing(RacingQuestRunItem.Forcefield);
            if (fireEvents) OnQuestUnlocked?.Invoke(RacingQuestType.Forcefield);
        }

        if (turretNow && !_turretUnlocked)
        {
            _turretUnlocked = true;
            AddUnlockedItemIfMissing(RacingQuestRunItem.Turret);
            if (fireEvents) OnQuestUnlocked?.Invoke(RacingQuestType.Turret);
        }

        if (coinNow && !_coinFriendUnlocked)
        {
            _coinFriendUnlocked = true;
            AddUnlockedItemIfMissing(RacingQuestRunItem.CoinFriend);
            if (fireEvents) OnQuestUnlocked?.Invoke(RacingQuestType.CoinFriend);
        }

        EnsureUnlockedItemOrderConsistency();

        SaveState();
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (!Application.isPlaying) return;
        ApplyTestingQuestOverrides(fireEvents: true);
    }
#endif

    private void ApplyTestingQuestOverrides(bool fireEvents)
    {
        bool changed = false;

        if (testAutoCompleteForcefieldQuest)
        {
            _forcefieldNoHpDeathRunCompletions = Mathf.Max(_forcefieldNoHpDeathRunCompletions, Mathf.Max(1, tuning.forcefieldNoHpDeathRunsRequired));
            if (!_forcefieldUnlocked && fireEvents) OnQuestUnlocked?.Invoke(RacingQuestType.Forcefield);
            _forcefieldUnlocked = true;
            AddUnlockedItemIfMissing(RacingQuestRunItem.Forcefield);
            if (fireEvents) OnQuestProgressChanged?.Invoke(RacingQuestType.Forcefield);
            changed = true;
        }

        if (testAutoCompleteTurretQuest)
        {
            _turretMashCompletions = Mathf.Max(_turretMashCompletions, Mathf.Max(1, tuning.turretMashCompletionsRequired));
            if (!_turretUnlocked && fireEvents) OnQuestUnlocked?.Invoke(RacingQuestType.Turret);
            _turretUnlocked = true;
            AddUnlockedItemIfMissing(RacingQuestRunItem.Turret);
            if (fireEvents) OnQuestProgressChanged?.Invoke(RacingQuestType.Turret);
            changed = true;
        }

        if (testAutoCompleteCoinFriendQuest)
        {
            _coinFriendCoinsCollected = Mathf.Max(_coinFriendCoinsCollected, Mathf.Max(1, tuning.coinFriendCoinsCollectedRequired));
            if (!_coinFriendUnlocked && fireEvents) OnQuestUnlocked?.Invoke(RacingQuestType.CoinFriend);
            _coinFriendUnlocked = true;
            AddUnlockedItemIfMissing(RacingQuestRunItem.CoinFriend);
            if (fireEvents) OnQuestProgressChanged?.Invoke(RacingQuestType.CoinFriend);
            changed = true;
        }

        if (changed)
        {
            EnsureUnlockedItemOrderConsistency();
            SaveState();
            if (fireEvents) OnInventoryChanged?.Invoke();
        }
    }

    private void LoadState()
    {
        _forcefieldNoHpDeathRunCompletions = Mathf.Max(0, PlayerPrefs.GetInt(KeyForcefieldNoHpDeathRunCompletions, 0));
        _turretMashCompletions = Mathf.Max(0, PlayerPrefs.GetInt(KeyTurretMashCompletions, 0));
        _coinFriendCoinsCollected = Mathf.Max(0, PlayerPrefs.GetInt(KeyCoinFriendCoinsCollected, 0));

        _forcefieldUnlocked = PlayerPrefs.GetInt(KeyForcefieldUnlocked, 0) == 1;
        _turretUnlocked = PlayerPrefs.GetInt(KeyTurretUnlocked, 0) == 1;
        _coinFriendUnlocked = PlayerPrefs.GetInt(KeyCoinFriendUnlocked, 0) == 1;
        _forcefieldItemOwned = PlayerPrefs.GetInt(KeyForcefieldItemOwned, 0) == 1;
        _turretItemOwned = PlayerPrefs.GetInt(KeyTurretItemOwned, 0) == 1;
        _coinFriendItemOwned = PlayerPrefs.GetInt(KeyCoinFriendItemOwned, 0) == 1;

        _unlockedItemSlots = Mathf.Clamp(
            PlayerPrefs.GetInt(KeyUnlockedItemSlots, Mathf.Max(1, defaultUnlockedItemSlots)),
            1,
            Mathf.Max(1, maxItemSlots));

        string csv = PlayerPrefs.GetString(KeyEquippedItemsCsv, string.Empty);
        equippedItems = ParseEquippedItemsCsv(csv);
        EnsureEquippedArrayInitialized();
        string unlockOrderCsv = PlayerPrefs.GetString(KeyUnlockedItemOrderCsv, string.Empty);
        unlockedItemOrder = ParseEquippedItemsCsv(unlockOrderCsv);
        EnsureUnlockedItemOrderConsistency();
        TrimEquippedToSlots();
    }

    private void SaveState()
    {
        PlayerPrefs.SetInt(KeyForcefieldNoHpDeathRunCompletions, _forcefieldNoHpDeathRunCompletions);
        PlayerPrefs.SetInt(KeyTurretMashCompletions, _turretMashCompletions);
        PlayerPrefs.SetInt(KeyCoinFriendCoinsCollected, _coinFriendCoinsCollected);

        PlayerPrefs.SetInt(KeyForcefieldUnlocked, _forcefieldUnlocked ? 1 : 0);
        PlayerPrefs.SetInt(KeyTurretUnlocked, _turretUnlocked ? 1 : 0);
        PlayerPrefs.SetInt(KeyCoinFriendUnlocked, _coinFriendUnlocked ? 1 : 0);
        PlayerPrefs.SetInt(KeyForcefieldItemOwned, _forcefieldItemOwned ? 1 : 0);
        PlayerPrefs.SetInt(KeyTurretItemOwned, _turretItemOwned ? 1 : 0);
        PlayerPrefs.SetInt(KeyCoinFriendItemOwned, _coinFriendItemOwned ? 1 : 0);
        PlayerPrefs.SetInt(KeyUnlockedItemSlots, UnlockedItemSlots);
        PlayerPrefs.SetString(KeyEquippedItemsCsv, SerializeEquippedItemsCsv());
        PlayerPrefs.SetString(KeyUnlockedItemOrderCsv, SerializeCsv(unlockedItemOrder));
        PlayerPrefs.Save();
    }

    private void TrimEquippedToSlots()
    {
        EnsureEquippedArrayInitialized();
        int slots = UnlockedItemSlots;
        for (int i = slots; i < equippedItems.Length; i++)
            equippedItems[i] = RacingQuestRunItem.None;
    }

    private string SerializeEquippedItemsCsv()
    {
        return SerializeCsv(equippedItems);
    }

    private static string SerializeCsv(RacingQuestRunItem[] items)
    {
        if (items == null || items.Length == 0) return string.Empty;
        string[] parts = new string[items.Length];
        for (int i = 0; i < items.Length; i++)
            parts[i] = ((int)items[i]).ToString();
        return string.Join(",", parts);
    }

    private static RacingQuestRunItem[] ParseEquippedItemsCsv(string csv)
    {
        if (string.IsNullOrWhiteSpace(csv)) return Array.Empty<RacingQuestRunItem>();
        string[] parts = csv.Split(',');
        var list = new System.Collections.Generic.List<RacingQuestRunItem>(parts.Length);
        for (int i = 0; i < parts.Length; i++)
        {
            if (!int.TryParse(parts[i], out int value)) continue;
            if (value < (int)RacingQuestRunItem.None || value > (int)RacingQuestRunItem.CoinFriend) continue;
            list.Add((RacingQuestRunItem)value);
        }
        return list.ToArray();
    }

    private void EnsureEquippedArrayInitialized()
    {
        int maxSlots = Mathf.Max(1, maxItemSlots);
        if (equippedItems == null || equippedItems.Length != maxSlots)
        {
            var old = equippedItems ?? Array.Empty<RacingQuestRunItem>();
            equippedItems = new RacingQuestRunItem[maxSlots];
            for (int i = 0; i < equippedItems.Length; i++)
                equippedItems[i] = RacingQuestRunItem.None;

            int copy = Mathf.Min(old.Length, equippedItems.Length);
            for (int i = 0; i < copy; i++)
                equippedItems[i] = old[i];
        }
    }

    private int IndexOfItem(RacingQuestRunItem item)
    {
        if (equippedItems == null) return -1;
        for (int i = 0; i < equippedItems.Length; i++)
        {
            if (equippedItems[i] == item) return i;
        }
        return -1;
    }

    private void AddUnlockedItemIfMissing(RacingQuestRunItem item)
    {
        if (unlockedItemOrder == null) unlockedItemOrder = Array.Empty<RacingQuestRunItem>();
        for (int i = 0; i < unlockedItemOrder.Length; i++)
            if (unlockedItemOrder[i] == item) return;

        var next = new RacingQuestRunItem[unlockedItemOrder.Length + 1];
        for (int i = 0; i < unlockedItemOrder.Length; i++) next[i] = unlockedItemOrder[i];
        next[next.Length - 1] = item;
        unlockedItemOrder = next;
    }

    private void EnsureUnlockedItemOrderConsistency()
    {
        AddIfQuestUnlocked(RacingQuestRunItem.Forcefield, _forcefieldUnlocked);
        AddIfQuestUnlocked(RacingQuestRunItem.Turret, _turretUnlocked);
        AddIfQuestUnlocked(RacingQuestRunItem.CoinFriend, _coinFriendUnlocked);

        if (unlockedItemOrder == null) return;
        var list = new List<RacingQuestRunItem>(unlockedItemOrder.Length);
        for (int i = 0; i < unlockedItemOrder.Length; i++)
        {
            var item = unlockedItemOrder[i];
            if (!IsQuestUnlockedForItem(item)) continue;
            if (list.Contains(item)) continue;
            list.Add(item);
        }
        unlockedItemOrder = list.ToArray();
    }

    private void AddIfQuestUnlocked(RacingQuestRunItem item, bool unlocked)
    {
        if (unlocked) AddUnlockedItemIfMissing(item);
    }

    private bool IsQuestUnlockedForItem(RacingQuestRunItem item)
    {
        switch (item)
        {
            case RacingQuestRunItem.Forcefield: return _forcefieldUnlocked;
            case RacingQuestRunItem.Turret: return _turretUnlocked;
            case RacingQuestRunItem.CoinFriend: return _coinFriendUnlocked;
            default: return false;
        }
    }

    private void EnsureItemOwnershipFromPurchasedUnlockNodes()
    {
        var skillMgr = RacingSkillTreeManager.Instance;
        if (skillMgr == null) return;

        bool changed = false;
        if (!_forcefieldItemOwned && skillMgr.GetLevel(SkillType.ForcefieldUnlock) > 0) { _forcefieldItemOwned = true; changed = true; }
        if (!_turretItemOwned && skillMgr.GetLevel(SkillType.TurretUnlock) > 0) { _turretItemOwned = true; changed = true; }
        if (!_coinFriendItemOwned && skillMgr.GetLevel(SkillType.CoinFriendUnlock) > 0) { _coinFriendItemOwned = true; changed = true; }
        if (changed) SaveState();
    }
}
