using UnityEngine;

/// <summary>
/// Attach to any object that moves via transform.position / tweening / kinematic motion.
/// Provides a reliable "velocity" value for trigger-based interactions.
/// </summary>
public class KinematicVelocityTracker : MonoBehaviour
{
    public Vector3 Velocity { get; private set; }
    public float Speed => Velocity.magnitude;

    [Tooltip("If true, uses FixedUpdate for velocity. Use FixedUpdate if you move in FixedUpdate.")]
    [SerializeField] private bool useFixedUpdate = false;

    private Vector3 _prevPos;
    private bool _inited;

    private void OnEnable()
    {
        _prevPos = transform.position;
        Velocity = Vector3.zero;
        _inited = true;
    }

    private void Update()
    {
        if (useFixedUpdate) return;
        Tick(Time.deltaTime);
    }

    private void FixedUpdate()
    {
        if (!useFixedUpdate) return;
        Tick(Time.fixedDeltaTime);
    }

    private void Tick(float dt)
    {
        if (!_inited) { _prevPos = transform.position; _inited = true; return; }
        if (dt <= 0f) return;

        Vector3 pos = transform.position;
        Velocity = (pos - _prevPos) / dt;
        _prevPos = pos;
    }
}
