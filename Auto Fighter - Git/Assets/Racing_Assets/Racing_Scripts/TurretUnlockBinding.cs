using UnityEngine;

[DisallowMultipleComponent]
public sealed class TurretUnlockBinding : MonoBehaviour
{
    [Header("Turret root to toggle")]
    [SerializeField] private GameObject turretRoot;

    [Header("Behavior")]
    [SerializeField] private bool defaultOffIfLocked = true;

    private RacingSkillTreeManager _mgr;
    private RacingQuestUnlockManager _questMgr;

    private void Awake()
    {
        _mgr = RacingSkillTreeManager.Instance;
        _questMgr = RacingQuestUnlockManager.Instance;
    }

    private void OnEnable()
    {
        if (_mgr == null) _mgr = RacingSkillTreeManager.Instance;

        // Initial sync for this spawned car
        SyncTurretActive();

        if (_mgr != null)
        {
            _mgr.OnLevelChanged += HandleLevelChanged;
            _mgr.OnSkillsReset += HandleSkillsReset;
        }
        if (_questMgr != null)
        {
            _questMgr.OnQuestUnlocked += HandleQuestUnlocked;
            _questMgr.OnInventoryChanged += HandleInventoryChanged;
        }
    }

    private void OnDisable()
    {
        if (_mgr != null)
        {
            _mgr.OnLevelChanged -= HandleLevelChanged;
            _mgr.OnSkillsReset -= HandleSkillsReset;
        }
        if (_questMgr != null)
        {
            _questMgr.OnQuestUnlocked -= HandleQuestUnlocked;
            _questMgr.OnInventoryChanged -= HandleInventoryChanged;
        }
    }

    private void HandleLevelChanged(SkillType type, int level)
    {
        if (type == SkillType.TurretUnlock)
            SyncTurretActive();
    }

    private void HandleSkillsReset()
    {
        SyncTurretActive();
    }

    private void HandleQuestUnlocked(RacingQuestType _)
    {
        SyncTurretActive();
    }

    private void HandleInventoryChanged()
    {
        SyncTurretActive();
    }

    private void SyncTurretActive()
    {
        if (!turretRoot) return;

        bool unlocked = IsUnlocked();
        if (!unlocked && defaultOffIfLocked)
            turretRoot.SetActive(false);
        else
            turretRoot.SetActive(unlocked);
    }

    private bool IsUnlocked()
    {
        bool unlockedBySkill = _mgr != null && _mgr.GetLevel(SkillType.TurretUnlock) > 0;
        if (!unlockedBySkill) return false;
        // Run-time activation is inventory-driven: node purchased + equipped in active slot.
        return _questMgr != null && _questMgr.IsItemEquipped(RacingQuestRunItem.Turret);
    }
}