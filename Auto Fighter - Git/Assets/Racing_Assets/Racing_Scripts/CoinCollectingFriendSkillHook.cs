using UnityEngine;

[DisallowMultipleComponent]
public sealed class CoinCollectingFriendSkillHook : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private CoinCollectingFriend collectingFriend;

    [Header("Unlock Skill")]
    [SerializeField] private bool useUnlockSkill = true;
    [SerializeField] private SkillType unlockSkill = SkillType.CoinFriendUnlock;

    [Header("Range Skill Chain")]
    [SerializeField] private bool useRangeSkills = true;
    [SerializeField] private SkillType rangeAddSkill = SkillType.CoinFriendRange_Add;
    [SerializeField] private SkillType rangeMulSkill = SkillType.CoinFriendRange_Mul;

    [Header("Cooldown Skill Chain")]
    [SerializeField] private bool useCooldownSkills = true;
    [SerializeField] private SkillType cooldownAddSkill = SkillType.CoinFriendCooldown_Add;
    [SerializeField] private SkillType cooldownMulSkill = SkillType.CoinFriendCooldown_Mul;

    [Header("Value Bonus Skill (+1 per level)")]
    [SerializeField] private bool useValueBonusSkill = true;
    [SerializeField] private SkillType valueBonusSkill = SkillType.CoinFriendValue_Add;

    private RacingSkillTreeManager _mgr;
    private RacingQuestUnlockManager _questMgr;

    private void Reset()
    {
        if (collectingFriend == null)
            collectingFriend = GetComponentInChildren<CoinCollectingFriend>(true);
    }

    private void Awake()
    {
        if (collectingFriend == null)
            collectingFriend = GetComponentInChildren<CoinCollectingFriend>(true);
    }

    private void OnEnable()
    {
        _mgr = RacingSkillTreeManager.Instance;
        _questMgr = RacingQuestUnlockManager.Instance;
        if (_mgr != null)
        {
            _mgr.OnLevelChanged += HandleSkillChanged;
            _mgr.OnSkillsReset += HandleSkillsReset;
        }
        if (_questMgr != null)
        {
            _questMgr.OnQuestUnlocked += HandleQuestUnlocked;
            _questMgr.OnInventoryChanged += HandleInventoryChanged;
        }

        Apply();
    }

    private void OnDisable()
    {
        if (_mgr != null)
        {
            _mgr.OnLevelChanged -= HandleSkillChanged;
            _mgr.OnSkillsReset -= HandleSkillsReset;
        }
        if (_questMgr != null)
        {
            _questMgr.OnQuestUnlocked -= HandleQuestUnlocked;
            _questMgr.OnInventoryChanged -= HandleInventoryChanged;
        }
    }

    private void HandleSkillChanged(SkillType _, int __) => Apply();
    private void HandleSkillsReset() => Apply();
    private void HandleQuestUnlocked(RacingQuestType _) => Apply();
    private void HandleInventoryChanged() => Apply();

    private void Apply()
    {
        if (collectingFriend == null) return;

        bool unlocked = true;
        if (useUnlockSkill)
            unlocked = _mgr != null && _mgr.GetLevel(unlockSkill) > 0;

        // Run-time activation is inventory-driven: node purchased + equipped in active slot.
        if (unlocked)
            unlocked = _questMgr != null && _questMgr.IsItemEquipped(RacingQuestRunItem.CoinFriend);

        CarController runtimeCar = ResolveRuntimeCar();
        bool canRunFriend = unlocked && runtimeCar != null;
        collectingFriend.gameObject.SetActive(canRunFriend);
        if (!canRunFriend) return;

        collectingFriend.SetPlayerCar(runtimeCar);

        float baseRange = ResolveBaseRange();
        float range = baseRange;
        if (useRangeSkills && _mgr != null)
            range = _mgr.ApplyStatChain(baseRange, rangeAddSkill, rangeMulSkill);

        float baseCd = ResolveBaseCooldown();
        float cooldown = baseCd;
        if (useCooldownSkills && _mgr != null)
            cooldown = _mgr.ApplyStatChain(baseCd, cooldownAddSkill, cooldownMulSkill);

        int bonus = ResolveValueBonus();

        collectingFriend.ApplySkillStats(range, cooldown, bonus);
    }

    private float ResolveBaseRange()
    {
        var gm = GameManager_Racing.Instance;
        if (gm != null)
            return gm.CoinFriendBaseRange;
        if (collectingFriend != null)
            return collectingFriend.AuthoredBaseRange;
        return 0.1f;
    }

    private float ResolveBaseCooldown()
    {
        var gm = GameManager_Racing.Instance;
        if (gm != null)
            return gm.CoinFriendBaseCooldown;
        if (collectingFriend != null)
            return collectingFriend.AuthoredBaseCooldown;
        return 0.05f;
    }

    private int ResolveValueBonus()
    {
        int baseValueBonus = 0;
        var gm = GameManager_Racing.Instance;
        if (gm != null)
            baseValueBonus = gm.CoinFriendBaseValueBonus;

        if (!useValueBonusSkill || _mgr == null)
            return Mathf.Max(0, baseValueBonus);

        int skillBonus = valueBonusSkill == SkillType.CoinFriendValue_Add
            ? _mgr.GetCoinFriendValueBonus()
            : Mathf.Max(0, _mgr.GetLevel(valueBonusSkill));

        return Mathf.Max(0, baseValueBonus + skillBonus);
    }

    private CarController ResolveRuntimeCar()
    {
        // Prefer the car we're parented under: GameManager.ActiveCar is assigned later in CoRespawnCarAtTrackStart,
        // after Instantiate — OnEnable runs during Instantiate with ActiveCar still null, which left the friend disabled.
        CarController local = GetComponentInParent<CarController>();
        if (local != null)
            return local;

        var gm = GameManager_Racing.Instance;
        return gm != null ? gm.ActiveCar : null;
    }
}
