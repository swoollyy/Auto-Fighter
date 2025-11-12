using UnityEngine;

[CreateAssetMenu(menuName = "Rewards/Grenade Ability")]
public sealed class GrenadeAbilityRewardSO : RewardSO
{
    [Header("Defaults (tunable)")]
    [SerializeField, Min(0.1f)] private float cooldownSeconds = 10f;
    [SerializeField, Min(0.05f)] private float fuseSeconds = 2f;
    [SerializeField, Min(0.1f)] private float radius = 6f;
    [SerializeField, Range(0.01f, 1f)] private float maxPctAtCenter = 0.80f;
    [SerializeField, Range(0.01f, 1f)] private float minPctAtEdge = 0.60f;
    [SerializeField, Range(0.1f, 1f)] private float inheritVelFactor = 0.75f;
    [SerializeField, Range(0f, 1f)] private float velocityDrag = 0.28f;
    [SerializeField, Range(0f, 1f)] private float angularDrag = 0.15f;
    [SerializeField, Range(0f, 1f)] private float bounciness = 0.35f;
    [SerializeField, Min(0f)] private float upArcMin = 2.5f;
    [SerializeField, Min(0f)] private float upArcMax = 8.0f;

    [Header("Custom Gravity")]
    [SerializeField] private float customGravityY = -14f; // stronger downward accel (affects grenade only)

    public override void Apply(IRunContext ctx)
    {
        ctx.MarkOwned(Id);
        ctx.SetActive(Id, true);
        ctx.SetAvailable(Id, true);

        var pm = Pinball.Instance;
        if (!pm) return;

        var go = new GameObject("GrenadeRewardRuntime");
        go.transform.SetParent(pm.transform, false);
        var rt = go.AddComponent<GrenadeRewardRuntime>();
        rt.DefaultCooldown = Mathf.Max(0.1f, cooldownSeconds);
        rt.DefaultFuseSeconds = Mathf.Max(0.05f, fuseSeconds);
        rt.DefaultRadius = Mathf.Max(0.05f, radius);
        rt.DefaultMaxPctAtCenter = Mathf.Clamp01(maxPctAtCenter);
        rt.DefaultMinPctAtEdge = Mathf.Clamp01(minPctAtEdge);
        rt.InheritVelocityFactor = Mathf.Clamp01(inheritVelFactor);
        rt.LinearDrag = Mathf.Clamp01(velocityDrag);
        rt.AngularDrag = Mathf.Clamp01(angularDrag);
        rt.Bounciness = Mathf.Clamp01(bounciness);
        rt.UpArcMin = Mathf.Max(0f, upArcMin);
        rt.UpArcMax = Mathf.Max(upArcMin, upArcMax);
        rt.CustomGravityY = customGravityY;

        rt.RebindAll();
    }

    public override bool IsEligible(IRunContext ctx)
    {
        if (!base.IsEligible(ctx)) return false;
        return true;
    }
}