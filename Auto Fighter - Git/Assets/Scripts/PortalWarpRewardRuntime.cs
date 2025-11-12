using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class PortalWarpRewardRuntime : MonoBehaviour
{
    [Header("Prefabs")]
    public GameObject PortalVisualPrefab;

    [Header("Raycast Layers")]
    public LayerMask LeftPlaneLayer;
    public LayerMask RightPlaneLayer;
    public LayerMask TopPlaneLayer;

    [Header("Distances")]
    [Min(0.1f)] public float MaxActiveDistance = 12f;
    [Min(0.01f)] public float TriggerDistance = 1.25f;
    [Min(0.01f)] public float InsideMargin = 0.35f;

    [Header("Impulse")]
    [Min(1f)] public float LateralImpulse = 28f;
    [Min(1f)] public float TopImpulse = 30f;

    [Header("Cooldown")]
    [Min(0.1f)] public float CooldownSeconds = 20f;

    [Header("Post FX (PPSv2)")]
    public PostFXController postFX;

    private readonly Dictionary<Ball, PortalWarpController> _controllers = new();
    private Pinball _pm;

    void Awake() => _pm = Pinball.Instance;

    void OnEnable()
    {
        Ball.OnBallActivated += HandleBallActivated;
        Ball.OnBallDeactivated += HandleBallDeactivated;
        EnsureControllersForExistingBalls();
    }

    void OnDisable()
    {
        Ball.OnBallActivated -= HandleBallActivated;
        Ball.OnBallDeactivated -= HandleBallDeactivated;
        DestroyAllControllers();
    }

    public void RebindAll()
    {
        DestroyAllControllers();
        EnsureControllersForExistingBalls();
    }

    private void EnsureControllersForExistingBalls()
    {
        var balls = FindObjectsByType<Ball>(FindObjectsSortMode.None);
        for (int i = 0; i < balls.Length; i++)
            if (balls[i] && balls[i].isActiveAndEnabled && balls[i].IsActive)
                TryAddController(balls[i]);
    }

    private void HandleBallActivated(Ball b)
    {
        if (!b) return;
        TryAddController(b);
    }

    private void HandleBallDeactivated(Ball b)
    {
        if (!b) return;
        TryRemoveController(b);
    }

    private void TryAddController(Ball ball)
    {
        if (!ball || _controllers.ContainsKey(ball)) return;

        var go = new GameObject("PortalWarpController (Ball)");
        go.transform.SetParent(ball.transform, false);
        var ctrl = go.AddComponent<PortalWarpController>();

        ctrl.PortalVisualPrefab = PortalVisualPrefab;
        ctrl.LeftPlaneLayer = LeftPlaneLayer;
        ctrl.RightPlaneLayer = RightPlaneLayer;
        ctrl.TopPlaneLayer = TopPlaneLayer;
        ctrl.MaxActiveDistance = MaxActiveDistance;
        ctrl.TriggerDistance = TriggerDistance;
        ctrl.InsideMargin = InsideMargin;
        ctrl.LateralImpulse = LateralImpulse;
        ctrl.TopImpulse = TopImpulse;
        ctrl.CooldownSeconds = CooldownSeconds;
        ctrl.postFX = postFX;
        ctrl.SetTarget(ball);

        // Only main ball uses anchor HUD
        ctrl.UseUi = (_pm != null && _pm.ball == ball);

        _controllers[ball] = ctrl;
    }

    private void TryRemoveController(Ball ball)
    {
        if (!ball) return;
        if (_controllers.TryGetValue(ball, out var ctrl))
        {
            if (ctrl) Destroy(ctrl.gameObject);
            _controllers.Remove(ball);
        }
    }

    public void ForceGlobalCooldown(float seconds)
    {
        foreach (var kv in _controllers)
            if (kv.Value) kv.Value.ForceCooldown(seconds);
    }

    public void CancelAllSlowmo() // NEW: used when entering paused states
    {
        foreach (var kv in _controllers)
            if (kv.Value) kv.Value.ForceStopSlowmo();
    }

    private void DestroyAllControllers()
    {
        foreach (var kv in _controllers)
            if (kv.Value) Destroy(kv.Value.gameObject);
        _controllers.Clear();
    }
}