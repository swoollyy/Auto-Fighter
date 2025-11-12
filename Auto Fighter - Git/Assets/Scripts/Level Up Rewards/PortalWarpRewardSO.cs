using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(menuName = "Rewards/Portal Warp")]
public class PortalWarpRewardSO : RewardSO
{
    [Header("Portal Prefab")]
    [Tooltip("Cube-like visual. Untilted, no rotation at runtime.")]
    [SerializeField] private GameObject portalVisualPrefab;

    [Header("Raycast Layers")]
    [SerializeField] private LayerMask leftPlaneLayer;
    [SerializeField] private LayerMask rightPlaneLayer;
    [SerializeField] private LayerMask topPlaneLayer;

    [Header("Distances")]
    [SerializeField, Min(0.1f)] private float maxActiveDistance = 12f;
    [SerializeField, Min(0.01f)] private float triggerDistance = 1.25f;
    [SerializeField, Min(0.01f)] private float insideMargin = 0.35f;

    [Header("Impulse")]
    [SerializeField, Min(1f)] private float lateralImpulse = 28f;
    [SerializeField, Min(1f)] private float topImpulse = 30f;

    [Header("Cooldown (seconds)")]
    [SerializeField, Min(0.1f)] private float cooldownSeconds = 20f;

    public override void Apply(IRunContext ctx)
    {
        ctx.MarkOwned(Id);
        ctx.SetActive(Id, true);
        ctx.SetAvailable(Id, false);
        ctx.SetExclusive(ExclusivityKey, true);

        var pm = Pinball.Instance;

        var go = new GameObject("PortalWarpRuntime");
        go.transform.SetParent(pm.transform, false);
        var runtime = go.AddComponent<PortalWarpRewardRuntime>();

        runtime.PortalVisualPrefab = portalVisualPrefab;
        runtime.LeftPlaneLayer = leftPlaneLayer;
        runtime.RightPlaneLayer = rightPlaneLayer;
        runtime.TopPlaneLayer = topPlaneLayer;

        runtime.MaxActiveDistance = maxActiveDistance;
        runtime.TriggerDistance = triggerDistance;
        runtime.InsideMargin = insideMargin;

        runtime.LateralImpulse = lateralImpulse;
        runtime.TopImpulse = topImpulse;
        runtime.postFX = pm.PostFX;

        runtime.CooldownSeconds = cooldownSeconds;

        // NEW: fix race with OnEnable by rebuilding controllers after config is set
        runtime.RebindAll();
    }
}