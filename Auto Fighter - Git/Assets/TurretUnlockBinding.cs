using UnityEngine;

[DisallowMultipleComponent]
public sealed class TurretUnlockBinding : MonoBehaviour
{
    [Header("Turret root to toggle")]
    [SerializeField] private GameObject turretRoot;

    [Header("Behavior")]
    [SerializeField] private bool defaultOffIfLocked = true;

    private RacingSkillTreeManager _mgr;

    private void Awake()
    {
        _mgr = RacingSkillTreeManager.Instance;
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
    }

    private void OnDisable()
    {
        if (_mgr != null)
        {
            _mgr.OnLevelChanged -= HandleLevelChanged;
            _mgr.OnSkillsReset -= HandleSkillsReset;
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
        if (_mgr == null) return false;
        return _mgr.GetLevel(SkillType.TurretUnlock) > 0;
    }
}