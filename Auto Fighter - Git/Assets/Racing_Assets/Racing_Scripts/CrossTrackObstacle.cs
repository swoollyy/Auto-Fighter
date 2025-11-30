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
    [SerializeField] private bool debugMassComparison = false;

    // Runtime path
    private Vector3 _startWS;
    private Vector3 _targetWS;
    private bool _active;
    private bool _initialized;
    private float _initialDelay;
    private float _spawnedAt;

    private Vector3 _prevPosition;
    private Vector3 _lastVelocity;

    [SerializeField, Tooltip("Layers this cross will react to. Colliders on other layers will be ignored (e.g. Terrain).")]
    private LayerMask reactLayers = ~0;

    // Cached Rigidbody
    private Rigidbody _rb;

    // -------------------------- INITIALIZATION --------------------------

    /// <summary>
    /// Called by CrossObstacleDirector right after Instantiate.
    /// Director is responsible for grounding start/target.
    /// We just follow that path.
    /// </summary>
    public void InitializeDirect(Vector3 startWorld, Vector3 targetWorld, float crossSpeed, float delayBeforeMove)
    {
        // Trust director's start/target completely, including Y
        _startWS = startWorld;
        _targetWS = targetWorld;
        speed = Mathf.Max(0.5f, crossSpeed);

        _initialDelay = Mathf.Max(0f, delayBeforeMove);
        _spawnedAt = Time.time;

        transform.position = _startWS;

        EnsureRigidbody();

        // Mass from scale curve
        float computedMass = ComputeMassFromScale();
        _rb.mass = Mathf.Max(0.01f, computedMass);

        // init velocity tracking
        _prevPosition = transform.position;
        _lastVelocity = Vector3.zero;

        _initialized = true;
        _active = true;
    }

    private void Awake()
    {
        // Ensure we have a Rigidbody and default it to kinematic (scripted path)
        _rb = GetComponent<Rigidbody>();
        if (_rb == null)
            _rb = gameObject.AddComponent<Rigidbody>();

        _rb.isKinematic = true;
        _rb.useGravity = false;
        _rb.collisionDetectionMode = CollisionDetectionMode.Discrete;
        _rb.interpolation = RigidbodyInterpolation.None;
        _rb.constraints = RigidbodyConstraints.FreezeRotation;

        // If reactLayers is untouched (~0 = everything), auto-ignore common ground
        if (reactLayers == ~0)
        {
            int road = LayerMask.NameToLayer("RoadSurface");
            int terrain = LayerMask.NameToLayer("Terrain");
            if (road >= 0) reactLayers &= ~(1 << road);
            if (terrain >= 0) reactLayers &= ~(1 << terrain);
        }
    }

    // -------------------------- MOVEMENT --------------------------

    private void FixedUpdate()
    {
        if (!_initialized || !_active)
            return;

        if (Time.time < _spawnedAt + _initialDelay)
        {
            _prevPosition = transform.position;
            _lastVelocity = Vector3.zero;
            return;
        }

        Vector3 current = transform.position;
        Vector3 toTarget = _targetWS - current;
        float dist = toTarget.magnitude;

        if (dist < 0.01f)
        {
            OnReachedEnd();
            return;
        }

        Vector3 dir = toTarget / dist;
        float step = speed * Time.fixedDeltaTime;
        step = Mathf.Min(step, dist);
        Vector3 nextPos = current + dir * step;

        // NO MORE re-projecting onto the surface here.
        // We trust the director's path (start/target) fully.
        _lastVelocity = (nextPos - _prevPosition) / Mathf.Max(Time.fixedDeltaTime, 0.0001f);
        _prevPosition = nextPos;

        if (_rb != null && _rb.isKinematic)
        {
            _rb.MovePosition(nextPos);
        }
        else
        {
            transform.position = nextPos;
        }
    }

    private void OnReachedEnd()
    {
        _active = false;
        if (destroyOnExit)
            Destroy(gameObject);
        else
            enabled = false;
    }

    // -------------------------- COLLISION LOGIC --------------------------

    private void OnCollisionEnter(Collision collision)
    {
        if (!_initialized || !_active) return;
        if (collision == null || collision.collider == null) return;
        HandleImpactWithCollider(collision.collider, collision);
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!_initialized || !_active) return;
        if (other == null) return;
        HandleImpactWithCollider(other, null);
    }

    [Header("Mass Comparison")]
    [SerializeField] private float massComparisonTolerance = 0.05f;
    [Tooltip("Extra mass added to the curve result (e.g., for metal shells, etc.).")]
    [SerializeField] private float defaultAddedMass = 0f;
    [Tooltip("Impulse Δv range applied to the other object when we are heavier.")]
    [SerializeField] private Vector2 pushDeltaVRange = new Vector2(1.5f, 3.0f);

    private void HandleImpactWithCollider(Collider other, Collision collision)
    {
        // Check layer mask first – if this collider isn't in reactLayers, ignore.
        if (!IsOnReactLayer(other))
            return;

        // Player special-case: ALWAYS keep path, never convert to physics.
        var car = other.GetComponentInParent<CarController>();
        if (car != null)
        {
            var playerRb = other.attachedRigidbody ?? other.GetComponentInParent<Rigidbody>();
            float myMass = ComputeMassFromScale();

            if (debugMassComparison)
            {
                Debug.Log($"[CrossTrackObstacle] COLLIDE player: crossMass={myMass:F2}, " +
                          $"playerRb={(playerRb != null ? playerRb.mass.ToString("F2") : "(no rb)")} cross keeps path");
            }

            if (playerRb != null)
            {
                Vector3 away = playerRb.position - transform.position;
                away.y = 0f;
                if (away.sqrMagnitude < 1e-6f) away = transform.forward;
                away.Normalize();

                float dv = Random.Range(pushDeltaVRange.x, pushDeltaVRange.y);
                Vector3 deltaV = away * dv;
                playerRb.AddForce(deltaV * Mathf.Max(0.01f, playerRb.mass), ForceMode.Impulse);
            }

            return; // DO NOT convert this obstacle
        }

        // Non-player collision: mass comparison rules.
        float obstCurveMass = ComputeMassFromScale();
        float obstMass = obstCurveMass;

        Rigidbody otherRb = other.attachedRigidbody ?? other.GetComponentInParent<Rigidbody>();
        var otherCross = other.GetComponentInParent<CrossTrackObstacle>();

        float otherMass;
        string otherMassSource;

        if (otherCross != null && otherCross != this)
        {
            otherMass = Mathf.Max(0.0001f, otherCross.ComputeMassFromScale());
            otherMassSource = "otherCrossCurve";
        }
            else if (otherRb != null)
            {
                otherMass = Mathf.Max(0.0001f, otherRb.mass);
                otherMassSource = "otherRb.mass";
            }
            else
            {
                // treat static geometry as effectively infinite mass
                otherMass = float.MaxValue;
                otherMassSource = "static(infinite)";
            }

            if (debugMassComparison)
            {
                string otherName = other.transform.root != null ? other.transform.root.name : other.gameObject.name;
                bool otherKinematic = otherRb != null && otherRb.isKinematic;
                Debug.Log($"[CrossTrackObstacle] COLLIDE '{gameObject.name}' -> '{otherName}': " +
                          $"crossMass={obstMass:F2} otherMass={otherMass:F2} (src={otherMassSource}) otherHasRb={(otherRb != null)} " +
                          $"otherKinematic={otherKinematic} tolerance={massComparisonTolerance:F3}");
            }

            // If we are strictly heavier, we KEEP our kinematic scripted path.
            if (obstMass > otherMass + massComparisonTolerance)
            {
                var root = other.transform.root;
                if (root != null && root.gameObject.layer != LayerMask.NameToLayer("RoadSurface"))
                {
                    TryMakeOtherDynamicGeneral(root.gameObject);
                    otherRb = root.GetComponent<Rigidbody>() ?? otherRb;
                }

                if (otherRb != null)
                {
                    Vector3 away = otherRb.position - transform.position;
                    away.y = 0f;
                    if (away.sqrMagnitude < 1e-6f) away = transform.forward;
                    away.Normalize();

                    float dv = Random.Range(pushDeltaVRange.x, pushDeltaVRange.y);
                    Vector3 deltaV = away * dv;
                    otherRb.AddForce(deltaV * Mathf.Max(0.01f, otherRb.mass), ForceMode.Impulse);

                    if (debugMassComparison)
                        Debug.Log($"[CrossTrackObstacle] ACTION: cross heavier -> kept path, pushed other ({otherRb.gameObject.name}).");
                }
                else if (debugMassComparison)
                {
                    Debug.Log("[CrossTrackObstacle] ACTION: cross heavier -> kept path, but other had no rigidbody.");
                }

                return;
            }

            // Otherwise, we are lighter or equal → convert THIS obstacle to physics.
            if (debugMassComparison)
                Debug.Log("[CrossTrackObstacle] ACTION: cross lighter or equal -> converting self to physics.");

            EnsureRigidbody();
            ConvertToPhysicsOnHit();

            // Optionally push the other body a bit as well (reaction effect).
            var otherRootObj = other.transform.root;
            if (otherRootObj != null && otherRootObj.gameObject.layer != LayerMask.NameToLayer("RoadSurface"))
            {
                TryMakeOtherDynamicGeneral(otherRootObj.gameObject);
                otherRb = otherRootObj.GetComponent<Rigidbody>() ?? otherRb;
            }

            if (otherRb != null)
            {
                Vector3 away = otherRb.position - transform.position;
                away.y = 0f;
                if (away.sqrMagnitude < 1e-6f) away = transform.forward;
                away.Normalize();

                float dv = Random.Range(pushDeltaVRange.x, pushDeltaVRange.y);
                Vector3 deltaV = away * dv;
                otherRb.AddForce(deltaV * Mathf.Max(0.01f, otherRb.mass), ForceMode.Impulse);
            }
        }

    private bool IsOnReactLayer(Collider col)
    {
        if (col == null) return false;
        int layer = col.gameObject.layer;
        if (((reactLayers.value) & (1 << layer)) != 0) return true;

        // also check the root in case of nested colliders
        if (col.transform.root != null)
        {
            int layerRoot = col.transform.root.gameObject.layer;
            if (((reactLayers.value) & (1 << layerRoot)) != 0) return true;
        }

        return false;
    }

    private void ConvertToPhysicsOnHit()
    {
        _active = false;           // stop scripted motion
        enabled = false;           // disable this script completely

        if (_rb == null) return;

        _rb.isKinematic = false;
        _rb.useGravity = true;
        _rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        _rb.interpolation = RigidbodyInterpolation.Interpolate;

        // give it its last kinematic velocity so physics continues smoothly
        _rb.velocity = _lastVelocity;

        // small upward nudge to avoid it being clipped inside surfaces
        _rb.position += Vector3.up * 0.01f;
    }

    // -------------------------- MASS / HELPERS --------------------------

    [Header("Size → Mass (hard mapping)")]
    [SerializeField]
    private AnimationCurve massByScaleCurve = new AnimationCurve(
        new Keyframe(0.1f, 5f),
        new Keyframe(1f, 12f),
        new Keyframe(2f, 30f)
    );

    [Tooltip("Fallback mass if the curve is invalid.")]
    [SerializeField] private float fallbackMass = 10f;

    private float ComputeMassFromScale()
    {
        float scale = transform.localScale.x; // assume uniform
        if (massByScaleCurve == null || massByScaleCurve.length == 0)
            return Mathf.Max(0.01f, fallbackMass);

        float curveMass = massByScaleCurve.Evaluate(scale);
        return Mathf.Max(0.01f, curveMass + defaultAddedMass);
    }

    private void EnsureRigidbody()
    {
        if (_rb != null) return;

        _rb = GetComponent<Rigidbody>();
        if (_rb == null)
            _rb = gameObject.AddComponent<Rigidbody>();

        _rb.isKinematic = true;
        _rb.useGravity = false;
        _rb.collisionDetectionMode = CollisionDetectionMode.Discrete;
        _rb.interpolation = RigidbodyInterpolation.None;
        _rb.constraints = RigidbodyConstraints.FreezeRotation;
    }

    private void TryMakeOtherDynamicGeneral(GameObject obj)
    {
        if (!obj) return;
        var rb = obj.GetComponent<Rigidbody>();
        if (rb == null) return;

        rb.isKinematic = false;
        rb.useGravity = true;
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        rb.interpolation = RigidbodyInterpolation.Interpolate;
    }

#if UNITY_EDITOR
    private void OnDrawGizmos()
    {
        if (!drawPathGizmos) return;
        if (!_initialized && Application.isPlaying == false) return;

        Gizmos.color = Color.cyan;
        Gizmos.DrawSphere(_startWS, 0.15f);
        Gizmos.color = Color.magenta;
        Gizmos.DrawSphere(_targetWS, 0.15f);
        Gizmos.color = Color.yellow;
        Gizmos.DrawLine(_startWS, _targetWS);
    }
#endif
}
