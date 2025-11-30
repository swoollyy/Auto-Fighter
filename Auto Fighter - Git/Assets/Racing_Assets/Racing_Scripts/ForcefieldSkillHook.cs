using UnityEngine;

[DisallowMultipleComponent]
public sealed class ForcefieldSkillHook : MonoBehaviour
{
    [Header("Target")]
    [SerializeField] private CarForcefield forcefield;

    [Header("Base (unskilled)")]
    [SerializeField, Min(0.1f)] private float baseRadius = 2.35f;
    [SerializeField, Min(0f)] private float baseCooldown = 6f;
    [SerializeField] private Vector2 baseAwayVelocityChange = new Vector2(7f, 18f);
    [SerializeField] private Vector2 baseUpVelocityChange = new Vector2(2.5f, 8f);

    [Header("Unlock Skill (optional)")]
    [SerializeField] private bool useUnlockSkill = true;
    [SerializeField] private SkillType unlockSkill = SkillType.ForcefieldUnlock;

    [Header("Cooldown Skill (optional)")]
    [SerializeField] private bool useCooldownSkill = true;
    [SerializeField] private SkillType cooldownAdd = SkillType.ForcefieldCooldown_Add;
    [SerializeField] private SkillType cooldownMul = SkillType.ForcefieldCooldown_Mul;

    [Header("Knockback Skill (optional)")]
    [SerializeField] private bool useKnockbackSkill = true;
    [SerializeField] private SkillType knockbackAdd = SkillType.ForcefieldKnockback_Add;
    [SerializeField] private SkillType knockbackMul = SkillType.ForcefieldKnockback_Mul;

    private RacingSkillTreeManager _mgr;

    private void Reset()
    {
        forcefield = GetComponent<CarForcefield>();
    }

    private void Awake()
    {
        if (!forcefield) forcefield = GetComponent<CarForcefield>();
    }

    private void OnEnable()
    {
        _mgr = RacingSkillTreeManager.Instance;
        if (_mgr != null)
        {
            _mgr.OnLevelChanged += HandleSkillChanged;
            _mgr.OnSkillsReset += HandleSkillsReset;
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
    }

    private void HandleSkillChanged(SkillType _, int __) => Apply();
    private void HandleSkillsReset() => Apply();

    private void Apply()
    {
        if (!forcefield) return;

        // Unlock
        bool unlocked = !useUnlockSkill || _mgr == null
            ? true
            : (_mgr.GetLevel(unlockSkill) > 0);
        forcefield.enabled = unlocked;
        // ensure the visual is off when locked (component disabled) or when not armed
                if (!unlocked)
            forcefield.SetArmed(false);


        // Cooldown
        float cooldown = baseCooldown;
        if (_mgr != null && useCooldownSkill)
            cooldown = _mgr.ApplyStatChain(baseCooldown, cooldownAdd, cooldownMul);
        forcefield.SetCooldown(Mathf.Max(0f, cooldown));

        // Knockback (single scalar affects both vectors)
        Vector2 awayVC = baseAwayVelocityChange;
        Vector2 upVC = baseUpVelocityChange;

        if (_mgr != null && useKnockbackSkill)
        {
            float kbScale = _mgr.ApplyStatChain(1f, knockbackAdd, knockbackMul);
            kbScale = Mathf.Max(0f, kbScale);
            awayVC *= kbScale;
            upVC *= kbScale;
        }

        // Push via public API (build-safe)
        forcefield.SetKnockback(awayVC, upVC);

#if UNITY_EDITOR
        // Optional: keep inspector in sync when viewing component in editor
        var so = new UnityEditor.SerializedObject(forcefield);
        so.FindProperty("awayVelocityChange").vector2Value = awayVC;
        so.FindProperty("upVelocityChange").vector2Value = upVC;
        so.ApplyModifiedPropertiesWithoutUndo();
#endif
    }
}