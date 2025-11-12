using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class GrenadeRewardRuntime : MonoBehaviour
{
    [Header("Defaults")]
    public float DefaultCooldown = 10f;
    public float DefaultFuseSeconds = 2f;
    public float DefaultRadius = 6f;
    [Range(0.01f, 1f)] public float DefaultMaxPctAtCenter = 0.80f;
    [Range(0.01f, 1f)] public float DefaultMinPctAtEdge = 0.60f;

    [Header("Motion/Physics")]
    [Range(0f, 1f)] public float InheritVelocityFactor = 0.75f;
    [Range(0f, 1f)] public float LinearDrag = 0.28f;
    [Range(0f, 1f)] public float AngularDrag = 0.15f;
    [Range(0f, 1f)] public float Bounciness = 0.35f;
    [Min(0f)] public float UpArcMin = 2.5f;
    [Min(0f)] public float UpArcMax = 8.0f;
    public float CustomGravityY = -14f;

    private readonly Dictionary<Ball, GrenadeController> _controllers = new();

    void OnEnable()
    {
        Ball.OnBallActivated += HandleBallActivated;
        Ball.OnBallDeactivated += HandleBallDeactivated;
        RebindAll();
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
        var balls = FindObjectsByType<Ball>(FindObjectsSortMode.None);
        for (int i = 0; i < balls.Length; i++)
        {
            var b = balls[i];
            if (b && b.isActiveAndEnabled && b.IsActive)
                AttachTo(b);
        }
    }

    public void ForceGlobalCooldown(float seconds)
    {
        foreach (var kv in _controllers)
            if (kv.Value) kv.Value.ForceCooldown(seconds);
    }

    private void HandleBallActivated(Ball b) => AttachTo(b);

    private void HandleBallDeactivated(Ball b)
    {
        if (!b) return;
        if (_controllers.TryGetValue(b, out var ctrl))
        {
            if (ctrl) Destroy(ctrl.gameObject);
            _controllers.Remove(b);
        }
    }

    private void AttachTo(Ball ball)
    {
        if (!ball || _controllers.ContainsKey(ball)) return;

        var go = new GameObject("GrenadeController (Ball)");
        go.transform.SetParent(ball.transform, false);
        var ctrl = go.AddComponent<GrenadeController>();
        ctrl.Bind(ball,
            DefaultCooldown, DefaultFuseSeconds, DefaultRadius,
            DefaultMaxPctAtCenter, DefaultMinPctAtEdge,
            InheritVelocityFactor, UpArcMin, UpArcMax,
            LinearDrag, AngularDrag, Bounciness, CustomGravityY);

        _controllers[ball] = ctrl;
    }

    private void DestroyAllControllers()
    {
        foreach (var kv in _controllers)
            if (kv.Value) Destroy(kv.Value.gameObject);
        _controllers.Clear();
    }
}