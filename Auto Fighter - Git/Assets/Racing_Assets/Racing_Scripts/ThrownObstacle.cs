using System;
using System.Collections;
using UnityEngine;

/// <summary>
/// Deterministic thrown projectile:
/// - Moves via Rigidbody.MovePosition along a straight horizontal path while adding a height arc (configurable).
/// - Rigidbody is non-kinematic, gravity disabled, collision detection ContinuousDynamic so collisions with CarController/obstacles invoke physics callbacks.
/// - Explosive variant spawns a GroundRing and applies binary full damage to any car/obstacle whose center is inside radius.
/// - On destruction awards currency to player via RacingSkillTreeManager.AddCurrency.
/// - Returns itself to ProjectilePool when done.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
[RequireComponent(typeof(Collider))]
public class ThrownObstacle : MonoBehaviour
{
    // runtime config
    private ThrownObstacleDirector _director;
    private Vector3 _spawnPos;
    private Vector3 _landPos;
    private float _speed;
    private float _arcHeight;
    private bool _explosive;
    private float _explosionRadius;
    private float _explosionImpulse;
    private LayerMask _hitLayers;
    private GameObject _prefabRef;
    private GameObject _ringPrefab;
    private int _rewardOnDestroy;

    [Header("VFX")]
    [Tooltip("Optional impact VFX prefab to spawn at contact/arrival point.")]
    [SerializeField] private GameObject impactVFXPrefab;
    [Tooltip("If using impact VFX prefab, how long (seconds) before destroying the spawned VFX (fallback).")]
    [SerializeField] private float impactVFXLifetime = 4f;

    [Header("Orientation")]
    [Tooltip("If true the projectile will orient smoothly toward its motion/target. Disable to avoid sharp rotations on arrival.")]
    [SerializeField] private bool orientToMotion = false;

    private Rigidbody _rb;
    private Collider _col;

    private float _travelDistance;
    private float _travelT; // 0..1
    private Vector3 _flatDir;
    private float _lifetimeMax = 12f;

    private bool _initialized;
    private bool _hasImpacted;

    // NEW: whether the director already spawned a preview ring for this projectile
    private bool _previewRingSpawned;

    // NEW: close-call detection
    private bool _closeCallArmed;
    private float _closestDistanceToCar = float.MaxValue;
    [Header("Close Call")]
    [Tooltip("Distance (meters) within which a passing projectile is considered a close call.")]
    [SerializeField, Min(0f)] private float closeCallThreshold = 3.5f; // tune in inspector
    [Tooltip("If true, close-call triggers will be sent to the director/manager.")]
    [SerializeField] private bool enableCloseCall = true;

    // NEW: suppression window to avoid creating explosions / crash damage when forcefield intercepts the projectile
    private float _suppressExplodeUntil = 0f;

    public void Initialize(
        ThrownObstacleDirector director,
        Vector3 spawnPos,
        Vector3 landPos,
        float speed,
        float arcHeight,
        bool explosive,
        float explosionRadius,
        float explosionImpulse,
        LayerMask hitLayers,
        GameObject prefabReference,
        GameObject ringPrefab,
        int rewardOnDestroy,
        bool previewRingSpawned = false    // NEW optional param
    )
    {
        _director = director;
        _spawnPos = spawnPos;
        _landPos = landPos;
        _speed = Mathf.Max(0.01f, speed);
        _arcHeight = Mathf.Max(0f, arcHeight);
        _explosive = explosive;
        _explosionRadius = explosionRadius;
        _explosionImpulse = explosionImpulse;
        _hitLayers = hitLayers;
        _prefabRef = prefabReference;
        _ringPrefab = ringPrefab;
        _rewardOnDestroy = rewardOnDestroy;

        _previewRingSpawned = previewRingSpawned; // store preview flag

        if (_rb == null) _rb = GetComponent<Rigidbody>();

        // keep gravity off; collision enabled only after we place the projectile to avoid phantom hits at pool root
        _rb.useGravity = false;
        _rb.isKinematic = false;
        _rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        _rb.interpolation = RigidbodyInterpolation.Interpolate;

        _col = GetComponent<Collider>();
        _col.isTrigger = false;

        // IMPORTANT: disable collider while we position the projectile to avoid instant overlaps/collisions
        _col.enabled = false;

        // position and orient the projectile at the intended spawn origin
        transform.position = _spawnPos;
        transform.LookAt(_landPos);

        // clear old physics velocities (safety)
        _rb.velocity = Vector3.zero;
        _rb.angularVelocity = Vector3.zero;
        _rb.WakeUp();

        _flatDir = (_landPos - _spawnPos);
        _flatDir.y = 0f;
        _travelDistance = _flatDir.magnitude;
        if (_travelDistance < 0.001f) _travelDistance = Vector3.Distance(_spawnPos, _landPos);
        _flatDir = (_travelDistance > 0f) ? (_flatDir / _travelDistance) : Vector3.forward;

        _travelT = 0f;
        _initialized = true;
        _hasImpacted = false;

        // optional lifetime
        StopAllCoroutines();
        StartCoroutine(AutoTimeoutCoroutine(_lifetimeMax));

        // enable collider on the next physics step so the projectile is already in the correct place
        StopCoroutine(nameof(EnableColliderNextFixedUpdate));
        StartCoroutine(EnableColliderNextFixedUpdate());

        // reset close-call tracking
        _closeCallArmed = false;
        _closestDistanceToCar = float.MaxValue;

        // Reset suppression
        _suppressExplodeUntil = 0f;
    }

    /// <summary>
    /// Called by projectiles when they deactivate so director can track concurrent count
    /// </summary>
    private IEnumerator EnableColliderNextFixedUpdate()
    {
        // Wait a single FixedUpdate so transform positioning has been applied and we avoid colliding at pool root/previous location
        yield return new WaitForFixedUpdate();

        if (_col != null)
        {
            // Enable the collider now that the projectile is positioned where it should be
            _col.enabled = true;
        }

        if (_rb != null)
            _rb.WakeUp();
    }

    private IEnumerator AutoTimeoutCoroutine(float sec)
    {
        yield return new WaitForSeconds(sec);
        ExplodeOrDeactivate();
    }

    void FixedUpdate()
    {
        if (!_initialized || _hasImpacted) return;

        // Update close-call tracking: distance to active car
        if (enableCloseCall && _director != null && _director.PlayerTransform != null)
        {
            float d = Vector3.Distance(transform.position, _director.PlayerTransform.position);
            if (d < _closestDistanceToCar) _closestDistanceToCar = d;
            if (d <= closeCallThreshold) _closeCallArmed = true;
        }

        // horizontal step per FixedDeltaTime
        float step = _speed * Time.fixedDeltaTime;
        float remaining = _travelDistance * (1f - _travelT);
        float advance = Mathf.Min(step, remaining);
        if (_travelDistance <= 0f) advance = _speed * Time.fixedDeltaTime;

        // update t
        float moved = advance / Mathf.Max(0.0001f, _travelDistance);
        _travelT = Mathf.Clamp01(_travelT + moved);

        // compute horizontal position
        Vector3 horiz = Vector3.Lerp(_spawnPos, _landPos, _travelT);
        // arc height (parabolic)
        float y = Mathf.Sin(Mathf.Clamp01(_travelT) * Mathf.PI) * _arcHeight;
        Vector3 target = new Vector3(horiz.x, Mathf.Lerp(_spawnPos.y, _landPos.y, _travelT) + y, horiz.z);

        // move rigidbody so physics collisions happen
        _rb.MovePosition(target);

        // rotate to face motion only if enabled (user requested no rotation)
        if (orientToMotion)
        {
            Vector3 fwd = (target - transform.position);
            if (fwd.sqrMagnitude > 1e-6f) transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(fwd.normalized, Vector3.up), 0.9f);
        }

        // handle arrival
        if (_travelT >= 1f)
        {
            OnArrived();
        }
    }

    private void OnArrived()
    {
        if (_hasImpacted) return;
        _hasImpacted = true;

        // If suppressed by forcefield, avoid explosion/damage and simply deactivate gracefully
        if (Time.time < _suppressExplodeUntil)
        {
            // Play non-damaging impact VFX and deactivate
            SpawnImpactVFX(transform.position, Vector3.up);
            ExplodeOrDeactivate();
            return;
        }

        if (_explosive)
        {
            // only spawn a new ring here if director did not already preview one
            if (!_previewRingSpawned)
                SpawnRing();

            // spawn impact VFX at arrival position then explode
            SpawnImpactVFX(transform.position, Vector3.up);

            // Notify manager/director about explosion proximity (even if it didn't collide with car)
            var gm = GameManager_Racing.Instance;
            gm?.HandleProjectileExplosion(transform.position, _explosionRadius);

            ExplodeAt(transform.position);
        }
        else
        {
            // plain impact: apply damage/physics if appropriate, then deactivate
            // spawn small impact VFX
            SpawnImpactVFX(transform.position, Vector3.up);

            // No explosion, but still notify proximity if very close to car (optional)
            var gm = GameManager_Racing.Instance;
            gm?.HandleProjectileProximity(transform.position, _explosionRadius * 0.5f);

            ExplodeOrDeactivate();
        }
    }

    // spawn a visual ring (pooled)
    private void SpawnRing()
    {
        if (_ringPrefab == null) return;
        var ring = ProjectilePool.Instance.Get(_ringPrefab);
        if (ring == null) return;

        // Position BEFORE activation to avoid visible pop at pool location
        ring.transform.position = _landPos + Vector3.up * 0.05f;
        ring.transform.rotation = Quaternion.identity;
        ring.SetActive(true);

        var gr = ring.GetComponent<GroundRing>();
        if (gr != null)
            gr.Play(_explosionRadius, onComplete: () => ProjectilePool.Instance.Return(_ringPrefab, ring));
        else
            ProjectilePool.Instance.Return(_ringPrefab, ring);
    }

    // explosion effect: apply binary full damage to car/obstacles within radius, apply impulses to Rigidbodies
    private void ExplodeAt(Vector3 pos)
    {
        // If suppressed by forcefield, skip damaging overlap behavior and just deactivate.
        if (Time.time < _suppressExplodeUntil)
        {
            SpawnImpactVFX(pos, Vector3.up);
            ExplodeOrDeactivate();
            return;
        }

        // spawn impact VFX (if any) at explosion center
        SpawnImpactVFX(pos, Vector3.up);

        // overlap (respect configured hit layers so we don't pick up unrelated colliders)
        Collider[] hits = Physics.OverlapSphere(pos, _explosionRadius, _hitLayers.value, QueryTriggerInteraction.Ignore);
        var mgr = RacingSkillTreeManager.Instance;
        var gm = GameManager_Racing.Instance;

        foreach (var c in hits)
        {
            if (c == null) continue;

            // Car
            var car = c.GetComponentInParent<CarController>();
            if (car)
            {
                // if car center is inside radius do full crash: call GameManager.OnCarCrash for camera/coin penalty
                float d = Vector3.Distance(car.transform.position, pos);
                if (d <= _explosionRadius)
                {
                    float severity = 1f; // binary full

                    // Notify GameManager (visuals, coins penalties, slow-mo, etc.)
                    gm?.OnCarCrash(0f, severity);

                    // Prefer using CarController public API so internal cooldown / HP/fuel logic is used
                    try
                    {
                        car.ApplyExternalCrashDamage(severity);
                    }
                    catch (Exception)
                    {
                        // Fallback: try reflection-only approach if the public API isn't available for some reason
                        TryApplyCarDamageViaReflection(car, severity);
                    }
                }
            }

            // Obstacles (RacingObstacle)
            var obstacle = c.GetComponentInParent<RacingObstacle>();
            if (obstacle)
            {
                // Ensure obstacle uses physics (non-kinematic) and get a usable Rigidbody
                Rigidbody obr = EnsureRigidbodyForObstacle(obstacle.gameObject);

                // If mass is tiny, assign a sane default
                if (obr.mass < 0.01f) obr.mass = Mathf.Max(1f, 10f);

                Vector3 dir = (obr.position - pos).normalized;
                float mass = Mathf.Max(0.01f, obr.mass);
                // Mass-aware impulse: desired deltaV = explosion impulse magnitude; impulse = deltaV * mass
                Vector3 impulse = dir * (_explosionImpulse * mass);
                obr.AddForce(impulse, ForceMode.Impulse);

                // apply damage if RacingObstacle supports ApplyDamage (it does)
                obstacle.ApplyDamage(_explosionRadius > 0f ? 10f : 5f); // simple flat value – tweakable
            }

            // Apply impulse to any other rigidbody in radius (for emergent physics)
            var rb = c.attachedRigidbody;
            if (rb && (obstacle == null))
            {
                // Ensure it's non-kinematic before applying force
                if (rb.isKinematic)
                {
                    rb.isKinematic = false;
                    rb.WakeUp();
                }

                float mass = Mathf.Max(0.01f, rb.mass);
                Vector3 dir = (rb.position - pos).normalized;
                Vector3 impulse = dir * (_explosionImpulse * mass);
                rb.AddForce(impulse, ForceMode.Impulse);
            }
        }

        ExplodeOrDeactivate();
    }

    private void ExplodeOrDeactivate()
    {
        // Award reward to player if destroyed by player (the director / other code should call DestroyProjectileToReward when appropriate).
        // Here we just deactivate.
        StartCoroutine(DeactivateNextFrame());
    }

    private IEnumerator DeactivateNextFrame()
    {
        // Wait for physics to finish the current step to avoid race/queued contact callbacks.
        yield return new WaitForFixedUpdate();

        // If the projectile never impacted anything but was armed as a close call, notify director/manager
        if (!_hasImpacted && _closeCallArmed && enableCloseCall)
        {
            // provide closest distance info if available
            _director?.NotifyProjectileCloseCall(this, _closestDistanceToCar);
            GameManager_Racing.Instance?.HandleProjectileCloseCall(transform.position, _closestDistanceToCar);
        }

        // Reset physics state so the pooled instance won't carry over velocities/collisions.
        if (_rb != null)
        {
            _rb.velocity = Vector3.zero;
            _rb.angularVelocity = Vector3.zero;
            _rb.isKinematic = true;
        }
        if (_col != null)
        {
            // disable collider while pooled (will be re-enabled in Initialize)
            _col.enabled = false;
        }

        _director?.NotifyProjectileStopped(this);
        ProjectilePool.Instance.Return(_prefabRef, gameObject);
        _initialized = false;
    }

    void OnCollisionEnter(Collision collision)
    {
        if (_hasImpacted) return;

        var other = collision.collider;
        if (((1 << other.gameObject.layer) & _hitLayers.value) == 0) return;

        // If collided with a RacingObstacle, apply knockback to that obstacle
        var ro = other.GetComponentInParent<RacingObstacle>();
        if (ro != null)
        {
            // Ensure the obstacle is converted from any scripted motion to physics if it supports that
            var shuttle = ro.GetComponentInChildren<ShuttleTrackObstacle>(true);
            if (shuttle)
                shuttle.ConvertToPhysicsOnHit();

            Rigidbody obr = EnsureRigidbodyForObstacle(ro.gameObject);

            // If mass is not set sensibly, ensure a reasonable mass
            if (obr.mass < 0.01f) obr.mass = Mathf.Max(0.1f, 10f);

            Vector3 away = (obr.position - transform.position).normalized;
            float mass = Mathf.Max(0.01f, obr.mass);
            // mass-aware impulse (= deltaV * mass). Use explosionImpulse * 0.7 as desired deltaV
            Vector3 impulse = away * (_explosionImpulse * 0.7f * mass);
            obr.AddForce(impulse, ForceMode.Impulse);

            // if explosive, also explode
            if (_explosive)
            {
                // if director already previewed the ring, OnArrived logic will avoid double-spawn
                SpawnRing();

                // prefer the actual contact point if available
                Vector3 contactPoint = transform.position;
                if (collision.contactCount > 0)
                    contactPoint = collision.GetContact(0).point;

                // spawn impact VFX at contact point and explode there
                SpawnImpactVFX(contactPoint, collision.GetContact(0).normal);
                // Notify manager/director about explosion proximity
                GameManager_Racing.Instance?.HandleProjectileExplosion(contactPoint, _explosionRadius);
                ExplodeAt(contactPoint);
                return;
            }
        }

        // If collided with a car, apply crash/damage now (non-explosive should still hurt)
        var car = other.GetComponentInParent<CarController>();
        if (car != null)
        {
            // If suppressed by forcefield, skip applying crash/damage to the car
            if (Time.time < _suppressExplodeUntil)
            {
                // spawn a harmless impact VFX and deactivate
                SpawnImpactVFX(transform.position, Vector3.up);
                ExplodeOrDeactivate();
                return;
            }

            var gm = GameManager_Racing.Instance;
            float impactSpeed = collision.relativeVelocity.magnitude;
            // severity scaled reasonably (tweak divisor to taste)
            float severity = Mathf.Clamp01(impactSpeed / 20f);
            gm?.OnCarCrash(impactSpeed, severity);

            // Prefer public API to apply HP/fuel changes and respect cooldowns
            try
            {
                car.ApplyExternalCrashDamage(severity);
            }
            catch (Exception)
            {
                // fallback to reflection if something unexpected happens
                TryApplyCarDamageViaReflection(car, severity);
            }
        }

        // For all other cases treat as arrival
        _hasImpacted = true;

        // choose contact point if available
        Vector3 impactPoint = transform.position;
        Vector3 normal = Vector3.up;
        if (collision.contactCount > 0)
        {
            var contact = collision.GetContact(0);
            impactPoint = contact.point;
            normal = contact.normal;
        }

        // If suppressed by forcefield, avoid explosion/damage and deactivate
        if (Time.time < _suppressExplodeUntil)
        {
            SpawnImpactVFX(impactPoint, normal);
            ExplodeOrDeactivate();
            return;
        }

        if (_explosive)
        {
            if (!_previewRingSpawned)
                SpawnRing();
            SpawnImpactVFX(impactPoint, normal);
            // Notify manager/director about explosion proximity
            GameManager_Racing.Instance?.HandleProjectileExplosion(impactPoint, _explosionRadius);
            ExplodeAt(impactPoint);
        }
        else
        {
            // plain impact; let physics handle collision consequences (CarController should receive OnCollisionEnter)
            SpawnImpactVFX(impactPoint, normal);
            ExplodeOrDeactivate();
        }
    }

    // Called externally (e.g. turret shot, player hit) to destroy projectile early with reward to player
    public void DestroyByPlayer()
    {
        // award currency
        var mgr = RacingSkillTreeManager.Instance;
        if (mgr != null && _rewardOnDestroy > 0)
            mgr.AddCurrency(_rewardOnDestroy);

        // small VFX can be spawned here
        SpawnImpactVFX(transform.position, Vector3.up);
        // notify explosion proximity (small radius)
        GameManager_Racing.Instance?.HandleProjectileProximity(transform.position, _explosionRadius * 0.5f);
        ExplodeOrDeactivate();
    }

    // NEW: public API invoked by CarForcefield when the forcefield intercepts a thrown projectile.
    // It will:
    // - ensure the projectile uses physics (non-kinematic),
    // - add an immunity marker so the projectile won't damage the car for a short window,
    // - apply a mass-aware impulse away from the car,
    // - suppress explosions/damage for the ignoreWithCarSeconds window.
    public void InterceptedByForcefield(Vector3 awayDir, float awayDeltaV, float upDeltaV, float ignoreWithCarSeconds)
    {
        if (_rb == null) _rb = GetComponent<Rigidbody>();

        // ensure physics enabled
        _rb.isKinematic = false;
        _rb.useGravity = true;
        _rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        _rb.interpolation = RigidbodyInterpolation.Interpolate;
        _rb.WakeUp();

        // ensure a LaunchImmunityMarker exists so other systems know this was force-launched
        var immunity = GetComponent<LaunchImmunityMarker>();
        if (!immunity) immunity = gameObject.AddComponent<LaunchImmunityMarker>();
        immunity.Activate(Mathf.Max(0f, ignoreWithCarSeconds + 0.1f));

        // set suppression window so this projectile won't explode or damage cars during that time
        _suppressExplodeUntil = Time.time + Mathf.Max(0f, ignoreWithCarSeconds);

        // apply mass-aware impulse
        float mass = Mathf.Max(0.01f, _rb.mass);
        Vector3 desiredDeltaV = awayDir.normalized * awayDeltaV + Vector3.up * upDeltaV;
        Vector3 impulse = desiredDeltaV * mass;
        _rb.AddForce(impulse, ForceMode.Impulse);

        // Small visual/feedback: spawn impact VFX at current position to show "deflection"
        SpawnImpactVFX(transform.position, Vector3.up);
    }

    // Use reflection to reduce CarController private HP / fuel since no public API exposed for subtraction.
    private void TryApplyCarDamageViaReflection(CarController car, float severity)
    {
        if (car == null) return;

        try
        {
            var t = car.GetType();
            var currentHPField = t.GetField("currentHP", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var maxHPField = t.GetField("maxHP", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var fuelField = t.GetField("currentFuel", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var fuelMaxField = t.GetField("maxFuel", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

            float maxHP = (float)(maxHPField?.GetValue(car) ?? 0f);
            float maxFuel = (float)(fuelMaxField?.GetValue(car) ?? 0f);

            // Use fields from CarController for base crash penalties if available
            var hpCrashField = t.GetField("hpCrashDamageAtSeverity1", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var fuelLossField = t.GetField("fuelLossAtSeverity1", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

            float hpAt1 = hpCrashField != null ? (float)hpCrashField.GetValue(car) : Mathf.Max(10f, maxHP * 0.15f);
            float fuelAt1 = fuelLossField != null ? (float)fuelLossField.GetValue(car) : Mathf.Max(5f, maxFuel * 0.1f);

            // full (binary) damage: severity is expected 0..1; we pass computed severity
            float hpLoss = Mathf.Max(0f, hpAt1 * Mathf.Clamp01(severity));
            float fuelLoss = Mathf.Max(0f, fuelAt1 * Mathf.Clamp01(severity));

            if (currentHPField != null)
            {
                float curHP = (float)currentHPField.GetValue(car);
                curHP = Mathf.Max(0f, curHP - hpLoss);
                currentHPField.SetValue(car, curHP);
            }

            if (fuelField != null)
            {
                float curFuel = (float)fuelField.GetValue(car);
                curFuel = Mathf.Max(0f, curFuel - fuelLoss);
                fuelField.SetValue(car, curFuel);
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[ThrownObstacle] Reflection damage apply failed: {ex}");
        }
    }

    /// <summary>
    /// Ensure the target GameObject (obstacle root) has an active, non-kinematic Rigidbody and return it.
    /// Also attempts to convert shuttle-style scripted obstacles to physics-driven by calling ConvertToPhysicsOnHit when available.
    /// </summary>
    private Rigidbody EnsureRigidbodyForObstacle(GameObject rootObj)
    {
        if (rootObj == null) return null;

        // If the obstacle provides a ShuttleTrackObstacle conversion API, call it first
        var shuttle = rootObj.GetComponentInChildren<ShuttleTrackObstacle>(true);
        if (shuttle != null)
        {
            shuttle.ConvertToPhysicsOnHit();
        }

        // Find existing rigidbody (on root or children)
        Rigidbody found = rootObj.GetComponent<Rigidbody>() ?? rootObj.GetComponentInChildren<Rigidbody>();
        if (found == null)
        {
            // add a Rigidbody and configure it for dynamic physics
            found = rootObj.AddComponent<Rigidbody>();
            found.mass = Mathf.Max(0.1f, 10f);
        }

        // Ensure it's ready for impulses
        if (found.isKinematic)
            found.isKinematic = false;
        found.useGravity = true;
        found.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        found.interpolation = RigidbodyInterpolation.Interpolate;
        found.WakeUp();

        // Make sure transforms are synchronized before applying forces
        Physics.SyncTransforms();

        return found;
    }

    /// <summary>
    /// Spawn the optional impact VFX at the provided world position.
    /// Normal is used to orient the VFX if desired.
    /// </summary>
    private void SpawnImpactVFX(Vector3 worldPos, Vector3 normal)
    {
        if (impactVFXPrefab == null) return;

        // Prefer pooling pattern when ProjectilePool exists & prefab was pooled there
        // (ProjectilePool returns inactive instance ready to position)
        try
        {
            GameObject inst = null;
            if (ProjectilePool.Instance != null)
            {
                inst = ProjectilePool.Instance.Get(impactVFXPrefab);
                if (inst != null)
                {
                    inst.transform.position = worldPos;
                    inst.transform.rotation = Quaternion.LookRotation(normal, Vector3.up);
                    inst.SetActive(true);
                    // If this VFX is not a GroundRing (pooled with its own return), return it after lifetime
                    // We'll schedule a return to the pool after impactVFXLifetime seconds.
                    StartCoroutine(ReturnPooledVFXLater(impactVFXPrefab, inst, impactVFXLifetime));
                    return;
                }
            }

            // Fallback to Instantiate
            inst = Instantiate(impactVFXPrefab, worldPos, Quaternion.LookRotation(normal, Vector3.up));
            Destroy(inst, impactVFXLifetime);
        }
        catch (Exception)
        {
            // If anything goes wrong with pool, fallback to simple instantiate/destroy
            var inst = Instantiate(impactVFXPrefab, worldPos, Quaternion.LookRotation(normal, Vector3.up));
            Destroy(inst, impactVFXLifetime);
        }
    }

    private IEnumerator ReturnPooledVFXLater(GameObject prefab, GameObject instance, float delay)
    {
        yield return new WaitForSeconds(delay);
        if (instance != null && prefab != null && ProjectilePool.Instance != null)
            ProjectilePool.Instance.Return(prefab, instance);
    }
}

public class SimpleSpin : MonoBehaviour
{
    public Vector3 rpm = new Vector3(0f, 360f, 0f); // degrees per second

    void Update()
    {
        transform.Rotate(rpm * Time.deltaTime, Space.Self);
    }
}