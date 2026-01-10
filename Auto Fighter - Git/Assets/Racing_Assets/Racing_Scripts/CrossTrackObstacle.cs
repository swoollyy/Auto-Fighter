using System;
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

    [Header("Screen Shake")]
    [SerializeField] private bool enableScreenShake = true;
    [SerializeField] private float shakeIntensity = 0.18f;
    [SerializeField] private float shakeFrequency = 22f;
    [SerializeField] private float shakeMaxDistance = 35f;
    [SerializeField] private float shakeFullIntensityDistance = 6f;

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

    // Flag to prevent multiple conversions
    private bool _convertedToPhysics;

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

        var preview = GetComponent<ObstaclePathPreview>();
        if (preview) { preview.SetEndpoints(_startWS, _targetWS); preview.FadeIn(0.2f); }

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
        _convertedToPhysics = false;
    }

    private void Awake()
    {
        // Ensure we have a Rigidbody and default it to kinematic (scripted path)
        _rb = GetComponent<Rigidbody>();
        if (_rb == null)
            _rb = gameObject.AddComponent<Rigidbody>();

        _rb.isKinematic = true;
        _rb.useGravity = false;
        _rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;

        _rb.interpolation = RigidbodyInterpolation.Interpolate;

        _rb.constraints = RigidbodyConstraints.FreezeRotation;


        // If reactLayers is untouched (~0 = everything), auto-ignore common ground
        if (reactLayers == ~0)
        {
            int road = LayerMask.NameToLayer("RoadSurface");
            int terrain = LayerMask.NameToLayer("Terrain");
            if (road >= 0) reactLayers &= ~(1 << road);
            if (terrain >= 0) reactLayers &= ~(1 << terrain);
        }

        _convertedToPhysics = false;
    }

    // -------------------------- MOVEMENT --------------------------

    private void FixedUpdate()
    {
        if (!_initialized || !_active || _convertedToPhysics)
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

        if (enableScreenShake && _active && !_convertedToPhysics)
        {
            CarController.RequestWorldShake(
                transform.position,
                shakeIntensity,
                shakeFrequency,
                shakeMaxDistance,
                shakeFullIntensityDistance
            );
        }

        Vector3 dir = toTarget / dist;
        float step = speed * Time.fixedDeltaTime;
        step = Mathf.Min(step, dist);
        Vector3 nextPos = current + dir * step;

        // NO MORE re-projecting onto the surface here.
        // We trust the director's path (start/target) fully.
        _lastVelocity = (nextPos - _prevPosition) / Mathf.Max(Time.fixedDeltaTime, 0.0001f);
        _prevPosition = nextPos;

        Vector3 move = nextPos - current;
        float moveDist = move.magnitude;




        if (_rb != null && _rb.isKinematic)
            _rb.MovePosition(nextPos);
        else
            transform.position = nextPos;
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
        if (!_initialized || !_active || _convertedToPhysics) return;
        if (collision == null || collision.collider == null) return;

        // Cache impact direction from collision
        Vector3 impactDir = Vector3.zero;
        if (collision.contactCount > 0)
        {
            impactDir = -collision.GetContact(0).normal;
        }

        HandleImpactWithCollider(collision.collider, collision, impactDir);
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!_initialized || !_active || _convertedToPhysics) return;
        if (other == null) return;

        // Estimate impact direction from positions
        Vector3 impactDir = (transform.position - other.bounds.center).normalized;
        impactDir.y = 0f;
        if (impactDir.sqrMagnitude < 1e-6f) impactDir = _lastVelocity.normalized;

        HandleImpactWithCollider(other, null, impactDir);
    }

    [Header("Mass Comparison")]
    [SerializeField] private float massComparisonTolerance = 0.05f;
    [Tooltip("Extra mass added to the curve result (e.g., for metal shells, etc.).")]
    [SerializeField] private float defaultAddedMass = 0f;
    [Tooltip("Impulse Δv range applied to the other object when we are heavier.")]
    [SerializeField] private Vector2 pushDeltaVRange = new Vector2(1.5f, 3.0f);

    [Header("Upward Velocity Boost")]
    [Tooltip("Upward velocity boost range (min, max) applied to objects hit by this cross obstacle.")]
    [SerializeField] private Vector2 upwardBoostRange = new Vector2(2.0f, 5.0f);
    [Tooltip("If true, the upward boost scales with impact severity (relative speed). If false, uses a random value from the range.")]
    [SerializeField] private bool scaleUpwardBoostBySeverity = true;
    [Tooltip("Minimum relative speed to apply any upward boost.")]
    [SerializeField] private float minSpeedForUpwardBoost = 2f;
    [Tooltip("Speed at which the upward boost reaches its maximum value.")]
    [SerializeField] private float maxSpeedForUpwardBoost = 15f;

    [Header("Explosion Force (When Hit By Heavier Object)")]
    [Tooltip("Enable explosive physics reaction when this obstacle is hit by a heavier object.")]
    [SerializeField] private bool enableExplosionOnHeavierImpact = true;
    [Tooltip("Base explosion force applied to this obstacle when hit by heavier object.")]
    [SerializeField] private float explosionForceBase = 15f;
    [Tooltip("Explosion force multiplier based on mass difference (heavier = more force).")]
    [SerializeField] private float explosionForceMassScale = 0.5f;
    [Tooltip("Maximum explosion force cap.")]
    [SerializeField] private float explosionForceMax = 40f;
    [Tooltip("Upward bias for explosion force (0-1, where 1 = fully upward).")]
    [SerializeField, Range(0f, 1f)] private float explosionUpwardBias = 0.35f;
    [Tooltip("Torque applied during explosion for dramatic spin.")]
    [SerializeField] private Vector2 explosionTorqueRange = new Vector2(8f, 20f);
    [Tooltip("Apply explosion force to the OTHER object as well (mutual explosion).")]
    [SerializeField] private bool applyMutualExplosion = true;
    [Tooltip("Force multiplier applied to the heavier object (usually smaller since it's heavier).")]
    [SerializeField, Range(0f, 1f)] private float mutualExplosionScale = 0.3f;

    private void HandleImpactWithCollider(Collider other, Collision collision, Vector3 impactDir)
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
                // Calculate relative speed for severity scaling
                float relativeSpeed = _lastVelocity.magnitude;
                if (playerRb.velocity.sqrMagnitude > 0.01f)
                {
                    relativeSpeed = (_lastVelocity - playerRb.velocity).magnitude;
                }

                Vector3 away = playerRb.position - transform.position;
                away.y = 0f;
                if (away.sqrMagnitude < 1e-6f) away = transform.forward;
                away.Normalize();

                float dv = UnityEngine.Random.Range(pushDeltaVRange.x, pushDeltaVRange.y);
                Vector3 deltaV = away * dv;

                // Calculate upward boost
                float upwardBoost = CalculateUpwardBoost(relativeSpeed);
                deltaV.y += upwardBoost;

                playerRb.AddForce(deltaV * Mathf.Max(0.01f, playerRb.mass), ForceMode.Impulse);

                if (debugMassComparison && upwardBoost > 0f)
                {
                    Debug.Log($"[CrossTrackObstacle] Applied upward boost: {upwardBoost:F2} (relSpeed={relativeSpeed:F2})");
                }
            }

            return; // DO NOT convert this obstacle
        }

        // Non-player collision: mass comparison rules.
        float obstCurveMass = ComputeMassFromScale();
        float obstMass = obstCurveMass;

        Rigidbody otherRb = other.attachedRigidbody ?? other.GetComponentInParent<Rigidbody>();

        var otherShuttle = other.GetComponentInParent<ShuttleTrackObstacle>();

        if (otherRb != null && otherRb.isKinematic)
        {
            // If it's a shuttle, let it convert itself (handles preview/light correctly)
            if (otherShuttle != null)
            {
                otherShuttle.ConvertToPhysicsOnHit();
            }
            else
            {
                var root = other.transform.root != null ? other.transform.root.gameObject : other.gameObject;
                int roadLayer = LayerMask.NameToLayer("RoadSurface");
                int terrainLayer = LayerMask.NameToLayer("Terrain");

                if (root.layer != roadLayer && root.layer != terrainLayer)
                {
                    ForceMakeDynamic(otherRb);
                    Physics.SyncTransforms();
                }
            }
        }


        var otherCross = other.GetComponentInParent<CrossTrackObstacle>();

        float otherMass;
        string otherMassSource;

        if (otherCross != null && otherCross != this)
        {
            otherMass = Mathf.Max(0.0001f, otherCross.ComputeMassFromScale());
            otherMassSource = "otherCrossCurve";
        }
        else if (otherShuttle != null)
        {
            // ShuttleTrackObstacle: use its rigidbody mass if available
            otherMass = otherRb != null ? Mathf.Max(0.0001f, otherRb.mass) : 10f;
            otherMassSource = "shuttleRb.mass";
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

        // Calculate relative speed for physics
        float relSpeed = _lastVelocity.magnitude;
        if (otherRb != null && otherRb.velocity.sqrMagnitude > 0.01f)
        {
            relSpeed = (_lastVelocity - otherRb.velocity).magnitude;
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

            // If the other is a shuttle, tell it to convert to physics
            if (otherShuttle != null)
            {
                otherShuttle.ConvertToPhysicsOnHit();
                otherRb = otherShuttle.GetComponent<Rigidbody>();
            }

            if (otherRb != null)
            {
                Vector3 away = otherRb.position - transform.position;
                away.y = 0f;
                if (away.sqrMagnitude < 1e-6f) away = transform.forward;
                away.Normalize();

                float dv = UnityEngine.Random.Range(pushDeltaVRange.x, pushDeltaVRange.y);
                Vector3 deltaV = away * dv;

                // Calculate upward boost
                float upwardBoost = CalculateUpwardBoost(relSpeed);
                deltaV.y += upwardBoost;

                otherRb.AddForce(deltaV * Mathf.Max(0.01f, otherRb.mass), ForceMode.Impulse);

                // Add torque for dramatic effect
                float torque = UnityEngine.Random.Range(explosionTorqueRange.x, explosionTorqueRange.y) * 0.5f;
                Vector3 torqueDir = new Vector3(
                    UnityEngine.Random.Range(-1f, 1f),
                    UnityEngine.Random.Range(-1f, 1f),
                    UnityEngine.Random.Range(-1f, 1f)
                ).normalized;
                otherRb.AddTorque(torqueDir * torque, ForceMode.VelocityChange);

                if (debugMassComparison)
                {
                    Debug.Log($"[CrossTrackObstacle] ACTION: cross heavier -> kept path, pushed other ({otherRb.gameObject.name}) with upBoost={upwardBoost:F2}.");
                }
            }
            else if (debugMassComparison)
            {
                Debug.Log("[CrossTrackObstacle] ACTION: cross heavier -> kept path, but other had no rigidbody.");
            }

            return;
        }

        // Otherwise, we are lighter or equal → convert THIS obstacle to physics with EXPLOSION.
        if (debugMassComparison)
            Debug.Log($"[CrossTrackObstacle] ACTION: cross lighter or equal -> converting self to physics with explosion. massDiff={(otherMass - obstMass):F2}");

        EnsureRigidbody();

        // Calculate explosion parameters based on mass difference
        float massDiff = Mathf.Max(0f, otherMass - obstMass);
        float explosionForce = explosionForceBase + (massDiff * explosionForceMassScale);
        explosionForce = Mathf.Min(explosionForce, explosionForceMax);

        // Convert to physics with explosion
        ConvertToPhysicsWithExplosion(impactDir, explosionForce, relSpeed);

        // If the other is also a shuttle, convert it too
        if (otherShuttle != null)
        {
            otherShuttle.ConvertToPhysicsOnHit();
            otherRb = otherShuttle.GetComponent<Rigidbody>();
        }

        // Make sure other object is dynamic
        var otherRootObj = other.transform.root;
        if (otherRootObj != null && otherRootObj.gameObject.layer != LayerMask.NameToLayer("RoadSurface"))
        {
            TryMakeOtherDynamicGeneral(otherRootObj.gameObject);
            otherRb = otherRootObj.GetComponent<Rigidbody>() ?? otherRb;
        }

        // Apply mutual explosion to the other object if enabled
        if (applyMutualExplosion && otherRb != null)
        {
            Vector3 awayFromUs = (otherRb.position - transform.position);
            awayFromUs.y = 0f;
            if (awayFromUs.sqrMagnitude < 1e-6f) awayFromUs = -impactDir;
            awayFromUs.Normalize();

            float otherExplosionForce = explosionForce * mutualExplosionScale;
            Vector3 otherForceDir = Vector3.Lerp(awayFromUs, Vector3.up, explosionUpwardBias * 0.5f).normalized;

            otherRb.AddForce(otherForceDir * otherExplosionForce, ForceMode.VelocityChange);

            // Smaller torque for the heavier object
            float otherTorque = UnityEngine.Random.Range(explosionTorqueRange.x, explosionTorqueRange.y) * mutualExplosionScale;
            Vector3 otherTorqueDir = new Vector3(
                UnityEngine.Random.Range(-1f, 1f),
                UnityEngine.Random.Range(-1f, 1f),
                UnityEngine.Random.Range(-1f, 1f)
            ).normalized;
            otherRb.AddTorque(otherTorqueDir * otherTorque, ForceMode.VelocityChange);

            if (debugMassComparison)
            {
                Debug.Log($"[CrossTrackObstacle] Applied mutual explosion to {otherRb.gameObject.name}: force={otherExplosionForce:F2}");
            }
        }
    }


    public Vector3 GetWorldVelocity()
    {
        // If still on scripted motion, return the transform-derived velocity
        if (!_convertedToPhysics)
            return _lastVelocity;  // or however you track scripted velocity

        // After conversion, use real rigidbody velocity
        return _rb != null ? _rb.velocity : Vector3.zero;
    }

    /// <summary>
    /// Calculates the upward velocity boost based on relative speed and configuration.
    /// </summary>
    private float CalculateUpwardBoost(float relativeSpeed)
    {
        if (relativeSpeed < minSpeedForUpwardBoost)
            return 0f;

        if (scaleUpwardBoostBySeverity)
        {
            // Scale boost based on how fast the impact was
            float severity = Mathf.InverseLerp(minSpeedForUpwardBoost, maxSpeedForUpwardBoost, relativeSpeed);
            return Mathf.Lerp(upwardBoostRange.x, upwardBoostRange.y, severity);
        }
        else
        {
            // Random value from range
            return UnityEngine.Random.Range(upwardBoostRange.x, upwardBoostRange.y);
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

    /// <summary>
    /// Standard conversion to physics (when hit by player or reaching end).
    /// </summary>
    private void ConvertToPhysicsOnHit()
    {
        if (_convertedToPhysics) return;
        _convertedToPhysics = true;

        _active = false;           // stop scripted motion
        enabled = false;           // disable this script completely

        if (_rb == null) return;

        var preview = GetComponent<ObstaclePathPreview>();
        if (preview) preview.FadeOut(0.2f);

        _rb.isKinematic = false;
        _rb.useGravity = true;
        _rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        _rb.interpolation = RigidbodyInterpolation.Interpolate;
        _rb.constraints = RigidbodyConstraints.None;

        // give it its last kinematic velocity so physics continues smoothly
        _rb.velocity = _lastVelocity;

        // small upward nudge to avoid it being clipped inside surfaces
        _rb.position += Vector3.up * 0.01f;

        _rb.WakeUp();
        Physics.SyncTransforms();
    }

    /// <summary>
    /// Explosive conversion to physics when hit by a heavier object.
    /// Sends this obstacle flying dramatically.
    /// </summary>
    private void ConvertToPhysicsWithExplosion(Vector3 impactDir, float force, float relativeSpeed)
    {
        if (_convertedToPhysics) return;
        _convertedToPhysics = true;

        _active = false;
        enabled = false;

        if (_rb == null) return;

        _rb.isKinematic = false;
        _rb.useGravity = true;
        _rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        _rb.interpolation = RigidbodyInterpolation.Interpolate;
        _rb.constraints = RigidbodyConstraints.None;

        // Small upward nudge to avoid clipping
        _rb.position += Vector3.up * 0.05f;

        if (enableExplosionOnHeavierImpact)
        {
            // Calculate explosion direction: away from impact with upward bias
            Vector3 explosionDir = -impactDir;
            explosionDir.y = 0f;
            if (explosionDir.sqrMagnitude < 1e-6f)
                explosionDir = _lastVelocity.normalized;
            explosionDir.Normalize();

            // Blend in upward component
            Vector3 finalDir = Vector3.Lerp(explosionDir, Vector3.up, explosionUpwardBias).normalized;

            // Apply the explosion force
            _rb.AddForce(finalDir * force, ForceMode.VelocityChange);

            // Add dramatic spin
            float torqueMag = UnityEngine.Random.Range(explosionTorqueRange.x, explosionTorqueRange.y);
            Vector3 torqueDir = new Vector3(
                UnityEngine.Random.Range(-1f, 1f),
                UnityEngine.Random.Range(-1f, 1f),
                UnityEngine.Random.Range(-1f, 1f)
            ).normalized;
            _rb.AddTorque(torqueDir * torqueMag, ForceMode.VelocityChange);

            if (debugMassComparison)
            {
                Debug.Log($"[CrossTrackObstacle] Explosion applied: force={force:F2}, dir={finalDir}, torque={torqueMag:F2}");
            }
        }
        else
        {
            // Just inherit last velocity
            _rb.velocity = _lastVelocity;
        }

        _rb.WakeUp();
        Physics.SyncTransforms();
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

    /// <summary>
    /// Public accessor for mass computation (used by other obstacles for comparison).
    /// </summary>
    public float ComputeMassFromScale()
    {
        float scale = transform.localScale.x; // assume uniform
        if (massByScaleCurve == null || massByScaleCurve.length == 0)
            return Mathf.Max(0.01f, fallbackMass);

        float curveMass = massByScaleCurve.Evaluate(scale);
        return Mathf.Max(0.01f, curveMass + defaultAddedMass);
    }

    private void EnsureRigidbody()
    {
        if (_rb == null)
        {
            _rb = GetComponent<Rigidbody>();
            if (_rb == null)
                _rb = gameObject.AddComponent<Rigidbody>();
        }

        // Always enforce kinematic-mover settings.
        _rb.isKinematic = true;
        _rb.useGravity = false;
        _rb.detectCollisions = true;
        _rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
        _rb.interpolation = RigidbodyInterpolation.Interpolate;
        _rb.constraints = RigidbodyConstraints.FreezeRotation;
    }

    private void ForceMakeDynamic(Rigidbody rb)
    {
        if (rb == null) return;
        if (!rb.isKinematic) return;

        rb.isKinematic = false;
        rb.useGravity = true;
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        rb.constraints = RigidbodyConstraints.None;
        rb.WakeUp();
    }

    private void ResolveKinematicImpact(Collider other, Vector3 impactDir, Vector3 hitPosition)
    {
        if (_convertedToPhysics) return;
        if (!IsOnReactLayer(other)) return;

        // NEVER do the “force dynamic + shove” routine to the player/car
        if (other.GetComponentInParent<CarController>() != null)
            return;

        Rigidbody otherRb =
            other.attachedRigidbody ??
            other.GetComponentInParent<Rigidbody>();

        // If the other object has no RB, add one
        if (otherRb == null)
        {
            var root = other.transform.root;
            otherRb = root.gameObject.AddComponent<Rigidbody>();
        }

        // Convert the other object to physics IMMEDIATELY
        ForceMakeDynamic(otherRb);

        // Force solver ownership THIS FRAME
        Physics.SyncTransforms();

        // Compute relative speed
        float relSpeed = _lastVelocity.magnitude;

        // Explosion direction away from cross obstacle
        Vector3 away = (otherRb.position - transform.position);
        away.y = 0f;
        if (away.sqrMagnitude < 1e-6f)
            away = -impactDir;
        away.Normalize();

        // Apply impulse so the solver separates them
        float push = Mathf.Lerp(4f, 12f, Mathf.InverseLerp(0f, 15f, relSpeed));
        otherRb.AddForce(away * push, ForceMode.VelocityChange);

        // Optional upward kick so it doesn’t “pin”
        otherRb.AddForce(Vector3.up * 2.5f, ForceMode.VelocityChange);

        // If the other object is heavier → explode THIS obstacle
        float myMass = ComputeMassFromScale();
        float otherMass = otherRb.mass;

        if (otherMass >= myMass)
        {
            ConvertToPhysicsWithExplosion(impactDir, explosionForceBase, relSpeed);
        }
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
        rb.constraints = RigidbodyConstraints.None;
        rb.WakeUp();
    }

    /// <summary>
    /// Public property to check if this obstacle is still on its scripted path.
    /// </summary>
    public bool IsOnScriptedPath => _active && _initialized && !_convertedToPhysics;

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