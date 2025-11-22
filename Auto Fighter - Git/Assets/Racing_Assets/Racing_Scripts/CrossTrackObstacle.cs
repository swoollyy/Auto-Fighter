using UnityEngine;

[DisallowMultipleComponent]
public class CrossTrackObstacle : MonoBehaviour
{
    [Header("Motion")]
    [SerializeField] private float speed = 6f;
    [Tooltip("Destroy this GameObject after it crosses. If false, just disable this script.")]
    [SerializeField] private bool destroyOnExit = true;

    [Header("Debug")]
    [SerializeField] private bool drawPathGizmos = true;

    // Runtime path
    private Vector3 _startWS;
    private Vector3 _targetWS;
    private bool _active;
    private bool _initialized;
    private float _initialDelay;
    private float _spawnedAt;

    /// <summary>
    /// Called by CrossObstacleDirector right after Instantiate.
    /// The cube will start at startWorld, then move toward targetWorld.
    /// </summary>
    public void InitializeDirect(Vector3 startWorld,
                                 Vector3 targetWorld,
                                 float crossSpeed,
                                 float delayBeforeMove)
    {
        _startWS = startWorld;
        _targetWS = targetWorld;
        speed = Mathf.Max(0.5f, crossSpeed);

        _initialDelay = Mathf.Max(0f, delayBeforeMove);
        _spawnedAt = Time.time;

        transform.position = _startWS;

        _initialized = true;
        _active = true;
    }

    private void Awake()
    {
        // In case something forgets to initialize this, just stay idle.
        _spawnedAt = Time.time;
    }

    private void Start()
    {
        // If nobody initialized us, don't try to move – this avoids weird center-of-road motion.
        if (!_initialized)
            enabled = false;
    }

    private void Update()
    {
        if (!_active)
            return;

        // Respect initial delay when spawned predictively
        if (Time.time - _spawnedAt < _initialDelay)
            return;

        float step = Mathf.Max(0.01f, speed) * Time.deltaTime;
        transform.position = Vector3.MoveTowards(transform.position, _targetWS, step);

        if ((transform.position - _targetWS).sqrMagnitude <= 0.0001f)
        {
            _active = false;
            if (destroyOnExit)
                Destroy(gameObject);
            else
                enabled = false;
        }
    }

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        if (!drawPathGizmos || !_initialized)
            return;

        Gizmos.color = Color.cyan;
        Gizmos.DrawLine(_startWS, _targetWS);
        Gizmos.DrawSphere(_startWS, 0.2f);
        Gizmos.DrawSphere(_targetWS, 0.2f);
    }
#endif
}
