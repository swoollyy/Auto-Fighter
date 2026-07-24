using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Deterministic meteor projectile:
/// - Moves via Rigidbody.MovePosition along a straight dive from sky spawn → road impact.
/// - Ignores world/road collisions mid-flight so it does not detonate early.
/// - Explosive variant applies radius overlap damage on arrival; plain uses a smaller impact zone.
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

    // NEW: non-explosive landing impact radius (derived from collider footprint)
    private float _impactRadius = 1.5f;

    [Header("VFX")]
    [Tooltip("Optional impact VFX prefab to spawn at contact/arrival point.")]
    [SerializeField] private GameObject impactVFXPrefab;
    [Tooltip("If using impact VFX prefab, how long (seconds) before destroying the spawned VFX (fallback).")]
    [SerializeField] private float impactVFXLifetime = 4f;

    [Header("Orientation")]
    [Tooltip("If true the projectile will orient smoothly toward its motion/target. Disable to avoid sharp rotations on arrival.")]
    [SerializeField] private bool orientToMotion = false;

    [Header("Impact comic (Crash popup)")]
    [Tooltip("World-space WHAM/KAPOW style text at the impact point. At most once per throw (avoids doubles with obstacle-vs-obstacle popups on the prop, since the projectile is not an 'obstacle buddy').")]
    [SerializeField] private bool enableImpactCrashPopup = true;
    [SerializeField, Min(0f)] private float impactCrashPopupHeight = 1f;

    [Header("Plain Impact Zone")]
    [Tooltip("Multiplier applied to the derived collider footprint to form the non-explosive 'impact zone' radius.")]
    [SerializeField, Min(0.1f)] private float plainImpactRadiusMultiplier = 1.0f;
    [Tooltip("Clamp for non-explosive impact radius.")]
    [SerializeField] private Vector2 plainImpactRadiusClamp = new Vector2(0.75f, 5.0f);
    [Tooltip("If true, plain projectiles apply an AoE-style crash/knockback on arrival using the derived radius.")]
    [SerializeField] private bool enablePlainImpactZone = true;

    private Rigidbody _rb;
    private Collider _col;

    private float _travelDistance;
    private float _travelT; // 0..1
    private Vector3 _flatDir;
    private float _lifetimeMax = 12f;

    private bool _initialized;
    private bool _hasImpacted;
    private bool _impactCrashPopupFired;

    // whether the director already spawned a preview telegraph
    private bool _previewRingSpawned;

    // close-call detection
    private bool _closeCallArmed;
    private float _closestDistanceToCar = float.MaxValue;
    [Header("Close Call")]
    [Tooltip("Distance (meters) within which a passing projectile is considered a close call.")]
    [SerializeField, Min(0f)] private float closeCallThreshold = 3.5f;
    [Tooltip("If true, close-call triggers will be sent to the director/manager.")]
    [SerializeField] private bool enableCloseCall = true;

    // suppression window to avoid explosions/crash damage when forcefield intercepts
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
        bool previewRingSpawned = false
    )
    {
        _director = director;
        _spawnPos = spawnPos;
        _landPos = landPos;
        _speed = Mathf.Max(0.01f, speed);
        _arcHeight = Mathf.Max(0f, arcHeight);
        _explosive = explosive;
        _explosionRadius = Mathf.Max(0.01f, explosionRadius);
        _explosionImpulse = explosionImpulse;
        _hitLayers = hitLayers;
        _prefabRef = prefabReference;
        _ringPrefab = ringPrefab;
        _rewardOnDestroy = rewardOnDestroy;

        _previewRingSpawned = previewRingSpawned;

        if (_rb == null) _rb = GetComponent<Rigidbody>();
        if (_col == null) _col = GetComponent<Collider>();

        // keep gravity off; collision enabled only after we place the projectile to avoid phantom hits at pool root
        _rb.useGravity = false;
        _rb.isKinematic = false;
        _rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        _rb.interpolation = RigidbodyInterpolation.Interpolate;

        _col.isTrigger = false;

        // IMPORTANT: disable collider while we position the projectile to avoid instant overlaps/collisions
        _col.enabled = false;

        // position and orient the projectile at the intended spawn origin
        transform.position = _spawnPos;
        Vector3 dive = _landPos - _spawnPos;
        if (dive.sqrMagnitude > 1e-6f)
            transform.rotation = Quaternion.LookRotation(dive.normalized, Vector3.up);
        else
            transform.LookAt(_landPos);

        // clear old physics velocities (safety)
        _rb.velocity = Vector3.zero;
        _rb.angularVelocity = Vector3.zero;
        _rb.WakeUp();

        // Full 3D dive path (meteor), not a flat lob with a mid-air sine peak.
        _flatDir = dive;
        _travelDistance = _flatDir.magnitude;
        if (_travelDistance < 0.001f) _travelDistance = 0.001f;
        _flatDir /= _travelDistance;

        _travelT = 0f;
        _initialized = true;
        _hasImpacted = false;
        _impactCrashPopupFired = false;

        // derive plain impact radius from collider footprint (world-ish)
        _impactRadius = DeriveFootprintRadius(_col);
        _impactRadius *= Mathf.Max(0.1f, plainImpactRadiusMultiplier);
        _impactRadius = Mathf.Clamp(_impactRadius, plainImpactRadiusClamp.x, plainImpactRadiusClamp.y);

        // optional lifetime
        StopAllCoroutines();
        StartCoroutine(AutoTimeoutCoroutine(_lifetimeMax));

        // enable collider on the next physics step so the projectile is already in the correct place
        StopCoroutine(nameof(EnableColliderNextFixedUpdate));
        StartCoroutine(EnableColliderNextFixedUpdate());

        // reset close-call tracking
        _closeCallArmed = false;
        _closestDistanceToCar = float.MaxValue;

        // reset suppression
        _suppressExplodeUntil = 0f;
    }

    private static float DeriveFootprintRadius(Collider col)
    {
        if (col == null) return 1.5f;

        // Prefer actual collider geometry if available (local space, then scale)
        if (col is SphereCollider sc)
        {
            float s = Mathf.Max(col.transform.lossyScale.x, col.transform.lossyScale.z);
            return Mathf.Max(0.1f, sc.radius * s);
        }

        if (col is CapsuleCollider cc)
        {
            // Capsule radius is in local space; footprint depends on X/Z scale
            float s = Mathf.Max(col.transform.lossyScale.x, col.transform.lossyScale.z);
            return Mathf.Max(0.1f, cc.radius * s);
        }

        // Fallback: use bounds extents in XZ
        Bounds b = col.bounds;
        return Mathf.Max(0.1f, Mathf.Max(b.extents.x, b.extents.z));
    }

    private IEnumerator EnableColliderNextFixedUpdate()
    {
        yield return new WaitForFixedUpdate();

        if (_col != null) _col.enabled = true;
        if (_rb != null) _rb.WakeUp();
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

        float step = _speed * Time.fixedDeltaTime;
        float remaining = _travelDistance * (1f - _travelT);
        float advance = Mathf.Min(step, remaining);
        if (_travelDistance <= 0f) advance = _speed * Time.fixedDeltaTime;

        float moved = advance / Mathf.Max(0.0001f, _travelDistance);
        _travelT = Mathf.Clamp01(_travelT + moved);

        // Straight dive from sky spawn → road impact (optional mild bow via arcHeight).
        Vector3 target = Vector3.Lerp(_spawnPos, _landPos, _travelT);
        if (_arcHeight > 0.01f)
        {
            // Positive arcHeight bows the path slightly above the chord without changing endpoints.
            float bow = Mathf.Sin(Mathf.Clamp01(_travelT) * Mathf.PI) * _arcHeight;
            target += Vector3.up * bow;
        }

        _rb.MovePosition(target);

        if (orientToMotion)
        {
            Vector3 fwd = (_landPos - transform.position);
            if (fwd.sqrMagnitude > 1e-6f)
                transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(fwd.normalized, Vector3.up), 0.9f);
        }

        if (_travelT >= 1f) OnArrived();
    }

    private static bool IsDirectHitTarget(Collider other)
    {
        if (other == null) return false;
        return other.GetComponentInParent<CarController>() != null
            || other.GetComponentInParent<RacingObstacle>() != null
            || other.GetComponentInParent<CrossTrackObstacle>() != null
            || other.GetComponentInParent<TrackObstacleBounceBack>() != null
            || other.GetComponentInParent<ShuttleTrackObstacle>() != null
            || other.GetComponentInParent<RollingLogAlongTrack>() != null
            || other.GetComponentInParent<ThrownObstacle>() != null;
    }

    private void OnArrived()
    {
        if (_hasImpacted) return;
        _hasImpacted = true;

        // Snap to intended impact so AoE is centered on the predicted road point.
        Vector3 impactPos = _landPos;
        if (_rb != null) _rb.position = impactPos;
        transform.position = impactPos;

        // If suppressed by forcefield, avoid explosion/damage and simply deactivate gracefully
        if (Time.time < _suppressExplodeUntil)
        {
            SpawnImpactVFX(impactPos, Vector3.up);
            ExplodeOrDeactivate();
            return;
        }

        SpawnImpactVFX(impactPos, Vector3.up);

        if (_explosive)
        {
            if (!_previewRingSpawned) SpawnRing();
            _director?.NotifyProjectileExploded(this, impactPos, _explosionRadius);
            ExplodeAt(impactPos, skipCrashPopup: false);
        }
        else
        {
            if (Time.time >= _suppressExplodeUntil)
                TryThrownImpactCrashPopup(impactPos);

            ApplyPlainImpactZone(impactPos);
            GameManager_Racing.Instance?.HandleProjectileProximity(impactPos, _impactRadius);
            ExplodeOrDeactivate();
        }
    }

    // (legacy) ring prefab support: if you’ve swapped to decals and this prefab has no GroundRing component,
    // it will simply be returned to the pool and do nothing.
    private void SpawnRing()
    {
        if (_ringPrefab == null) return;

        var ring = ProjectilePool.Instance.Get(_ringPrefab);
        if (ring == null) return;

        ring.transform.position = _landPos + Vector3.up * 0.05f;
        ring.transform.rotation = Quaternion.identity;
        ring.SetActive(true);

        var gr = ring.GetComponent<GroundRing>();
        if (gr != null)
            gr.Play(_explosionRadius, onComplete: () => ProjectilePool.Instance.Return(_ringPrefab, ring));
        else
            ProjectilePool.Instance.Return(_ringPrefab, ring);
    }

    // NEW: plain arrival AoE impact
    private void ApplyPlainImpactZone(Vector3 pos)
    {
        ApplyBlastPhysicsOverlaps(pos, _impactRadius, isExplosive: false);
    }

    /// <summary>
    /// Stable instance id per obstacle root so multi-collider props only take one blast impulse.
    /// Order matches displacement handling (special movers before generic rigidbodies).
    /// </summary>
    private static int GetBlastDisplacementRootId(Collider c)
    {
        var log = c.GetComponentInParent<RollingLogAlongTrack>();
        if (log != null) return log.gameObject.GetInstanceID();
        var bounce = c.GetComponentInParent<TrackObstacleBounceBack>();
        if (bounce != null) return bounce.gameObject.GetInstanceID();
        var cross = c.GetComponentInParent<CrossTrackObstacle>();
        if (cross != null) return cross.gameObject.GetInstanceID();
        var ro = c.GetComponentInParent<RacingObstacle>();
        if (ro != null) return ro.gameObject.GetInstanceID();
        var shuttle = c.GetComponentInParent<ShuttleTrackObstacle>();
        if (shuttle != null) return shuttle.gameObject.GetInstanceID();
        Rigidbody rb = c.attachedRigidbody;
        if (rb == null) rb = c.GetComponentInParent<Rigidbody>();
        if (rb != null && rb.GetComponentInParent<CarController>() == null)
            return rb.gameObject.GetInstanceID();
        return c.transform.root.GetInstanceID();
    }

    private static Vector3 HorizontalAwayFromBlast(Vector3 blastCenter, Vector3 referencePoint)
    {
        Vector3 d = referencePoint - blastCenter;
        d.y = 0f;
        if (d.sqrMagnitude < 1e-6f) d = Vector3.forward;
        d.Normalize();
        return d;
    }

    private void ApplyBlastPhysicsOverlaps(Vector3 pos, float radius, bool isExplosive)
    {
        Collider[] hits = Physics.OverlapSphere(pos, radius, _hitLayers.value, QueryTriggerInteraction.Ignore);
        var processed = new HashSet<int>();

        foreach (var c in hits)
        {
            if (c == null) continue;

            var car = c.GetComponentInParent<CarController>();
            if (car != null)
            {
                int cid = car.gameObject.GetInstanceID();
                if (!processed.Add(cid)) continue;

                float d = Vector3.Distance(car.transform.position, pos);
                if (d > radius) continue;

                float severity = 1f - Mathf.Clamp01(d / Mathf.Max(0.01f, radius));
                if (!isExplosive) severity *= 0.65f;

                Vector3 hitDir = (car.transform.position - pos);
                if (hitDir.sqrMagnitude < 0.0001f) hitDir = -car.transform.forward;
                hitDir.Normalize();

                var carCol = car.GetComponent<Collider>();
                Vector3 contactPoint = carCol != null ? carCol.ClosestPoint(pos) : car.transform.position;

                float impactSpeed = Mathf.Max(car.CurrentSpeed, isExplosive ? 12f : 9f);
                car.ApplyExternalCrashDamage(hitDir, impactSpeed, contactPoint, severity, transform, _rb);
                continue;
            }

            int oid = GetBlastDisplacementRootId(c);
            if (!processed.Add(oid)) continue;

            TryApplyBlastDisplacement(c, pos, radius, isExplosive);
        }
    }

    private void TryApplyBlastDisplacement(Collider c, Vector3 blastCenter, float blastRadius, bool isExplosive)
    {
        Vector3 closest = c.ClosestPoint(blastCenter);
        float distH = Vector2.Distance(
            new Vector2(closest.x, closest.z),
            new Vector2(blastCenter.x, blastCenter.z));
        float falloff = 1f - Mathf.Clamp01(distH / Mathf.Max(0.01f, blastRadius));
        if (falloff < 0.01f) return;

        float scale = isExplosive ? 1f : 0.35f;
        float baseImpulse = _explosionImpulse * scale * falloff;
        Vector3 planar = HorizontalAwayFromBlast(blastCenter, closest);

        var rollingLog = c.GetComponentInParent<RollingLogAlongTrack>();
        if (rollingLog != null)
        {
            rollingLog.ApplyBeastStrike(blastCenter, Mathf.Max(6f, baseImpulse * 1.15f));
            return;
        }

        var bounceBack = c.GetComponentInParent<TrackObstacleBounceBack>();
        if (bounceBack != null)
        {
            bounceBack.ApplyRollingLogRam(
                planar,
                Mathf.Max(4f, baseImpulse * 0.95f),
                Mathf.Max(1f, baseImpulse * 0.4f),
                closest);
            return;
        }

        var cross = c.GetComponentInParent<CrossTrackObstacle>();
        if (cross != null)
        {
            cross.ApplyRollingLogRam(planar, Mathf.Max(5f, baseImpulse * 1.05f));
            return;
        }

        var obstacle = c.GetComponentInParent<RacingObstacle>();
        if (obstacle != null)
        {
            Rigidbody obr = EnsureRigidbodyForObstacle(obstacle.gameObject);
            if (obr != null)
            {
                if (obr.mass < 0.01f) obr.mass = Mathf.Max(1f, 10f);

                Vector3 dir = (obr.position - blastCenter);
                if (dir.sqrMagnitude < 0.0001f) dir = Vector3.up;
                dir.Normalize();

                float mass = Mathf.Max(0.01f, obr.mass);
                Vector3 impulse = dir * (_explosionImpulse * scale * mass * falloff);
                obr.AddForce(impulse, ForceMode.Impulse);
            }

            obstacle.ApplyDamage(isExplosive ? 10f : 5f);
            return;
        }

        var shuttle = c.GetComponentInParent<ShuttleTrackObstacle>();
        if (shuttle != null)
        {
            shuttle.ConvertToPhysicsOnHit();
            Rigidbody srb = shuttle.GetComponent<Rigidbody>();
            if (srb != null)
            {
                Vector3 dir = (srb.worldCenterOfMass - blastCenter);
                if (dir.sqrMagnitude < 1e-6f) dir = planar + Vector3.up * 0.25f;
                dir.Normalize();
                float mass = Mathf.Max(0.01f, srb.mass);
                srb.AddForce(dir * (baseImpulse * mass), ForceMode.Impulse);
            }
            return;
        }

        Rigidbody rb = c.attachedRigidbody;
        if (rb == null) rb = c.GetComponentInParent<Rigidbody>();
        if (rb == null) return;
        if (rb.GetComponentInParent<CarController>() != null) return;

        if (rb.isKinematic)
        {
            rb.isKinematic = false;
            rb.useGravity = true;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            rb.WakeUp();
        }

        Vector3 away = (rb.worldCenterOfMass - blastCenter);
        if (away.sqrMagnitude < 1e-6f) away = planar + Vector3.up * 0.15f;
        away.Normalize();
        float m = Mathf.Max(0.01f, rb.mass);
        rb.AddForce(away * (baseImpulse * m), ForceMode.Impulse);
    }

    // explosion effect
    private void ExplodeAt(Vector3 pos, bool skipCrashPopup = false)
    {
        if (Time.time < _suppressExplodeUntil)
        {
            SpawnImpactVFX(pos, Vector3.up);
            ExplodeOrDeactivate();
            return;
        }

        if (!skipCrashPopup)
            TryThrownImpactCrashPopup(pos);

        SpawnImpactVFX(pos, Vector3.up);

        ApplyBlastPhysicsOverlaps(pos, _explosionRadius, isExplosive: true);

        ExplodeOrDeactivate();
    }

    private void ExplodeOrDeactivate()
    {
        StartCoroutine(DeactivateNextFrame());
    }

    private IEnumerator DeactivateNextFrame()
    {
        yield return new WaitForFixedUpdate();

        // Close call only matters if we never impacted
        if (!_hasImpacted && _closeCallArmed && enableCloseCall)
        {
            _director?.NotifyProjectileCloseCall(this, _closestDistanceToCar);
            GameManager_Racing.Instance?.HandleProjectileCloseCall(transform.position, _closestDistanceToCar);
        }

        if (_rb != null)
        {
            _rb.velocity = Vector3.zero;
            _rb.angularVelocity = Vector3.zero;
            _rb.isKinematic = true;
        }
        if (_col != null)
        {
            _col.enabled = false;
        }

        _director?.NotifyProjectileStopped(this);
        ProjectilePool.Instance.Return(_prefabRef, gameObject);
        _initialized = false;
    }

    private void TryThrownImpactCrashPopup(Vector3 worldPos)
    {
        if (_impactCrashPopupFired) return;
        if (!enableImpactCrashPopup) return;
        if (!RacingPopups.IsReady) return;

        _impactCrashPopupFired = true;
        RacingPopups.CrashWorld(worldPos + Vector3.up * impactCrashPopupHeight);
    }

    void OnCollisionEnter(Collision collision)
    {
        if (_hasImpacted) return;

        var other = collision.collider;
        if (((1 << other.gameObject.layer) & _hitLayers.value) == 0) return;

        // Mid-dive: only react to cars/obstacles. Road/world must not detonate the meteor early.
        if (!IsDirectHitTarget(other) && _travelT < 0.92f)
            return;

        bool hitCar = false;

        // If collided with a RacingObstacle, apply knockback to that obstacle
        var ro = other.GetComponentInParent<RacingObstacle>();
        if (ro != null)
        {
            var shuttle = ro.GetComponentInChildren<ShuttleTrackObstacle>(true);
            if (shuttle) shuttle.ConvertToPhysicsOnHit();

            Rigidbody obr = EnsureRigidbodyForObstacle(ro.gameObject);
            if (obr != null)
            {
                if (obr.mass < 0.01f) obr.mass = Mathf.Max(0.1f, 10f);

                Vector3 away = (obr.position - transform.position).normalized;
                float mass = Mathf.Max(0.01f, obr.mass);
                Vector3 impulse = away * (_explosionImpulse * 0.7f * mass);
                obr.AddForce(impulse, ForceMode.Impulse);
            }

            ro.ApplyDamage(_explosive ? 10f : 5f);

            if (_explosive)
            {
                SpawnRing();

                Vector3 contactPoint = transform.position;
                Vector3 normal = Vector3.up;
                if (collision.contactCount > 0)
                {
                    var ct = collision.GetContact(0);
                    contactPoint = ct.point;
                    normal = ct.normal;
                }

                SpawnImpactVFX(contactPoint, normal);
                _director?.NotifyProjectileExploded(this, contactPoint, _explosionRadius);
                _hasImpacted = true;
                ExplodeAt(contactPoint, skipCrashPopup: false);
                return;
            }
        }

        // Car collision should still hurt (even non-explosive)
        var car = other.GetComponentInParent<CarController>();
        if (car != null)
        {
            hitCar = true;
            if (Time.time < _suppressExplodeUntil)
            {
                SpawnImpactVFX(transform.position, Vector3.up);
                _hasImpacted = true;
                ExplodeOrDeactivate();
                return;
            }

            float impactSpeed = collision.relativeVelocity.magnitude;
            float min = car.MinImpactSpeed;
            float max = car.MaxImpactSpeed;
            impactSpeed = Mathf.Clamp(impactSpeed, min, max);

            Vector3 hitDir = (car.transform.position - transform.position).normalized;

            Vector3 contactPoint = collision.contactCount > 0
                ? collision.GetContact(0).point
                : car.transform.position;

            float severity = Mathf.InverseLerp(min, max, impactSpeed);
            car.ApplyExternalCrashDamage(hitDir, impactSpeed, contactPoint, severity, transform, _rb);
        }

        // For all other cases treat as impact and deactivate / explode depending on type
        _hasImpacted = true;

        Vector3 impactPoint = _landPos;
        Vector3 nrm = Vector3.up;
        if (collision.contactCount > 0)
        {
            var contact = collision.GetContact(0);
            impactPoint = contact.point;
            nrm = contact.normal;
        }

        // Prefer the scripted landing point when we're already near the end of the dive.
        if (_travelT >= 0.85f)
            impactPoint = _landPos;

        if (Time.time < _suppressExplodeUntil)
        {
            SpawnImpactVFX(impactPoint, nrm);
            ExplodeOrDeactivate();
            return;
        }

        if (_explosive)
        {
            if (!_previewRingSpawned) SpawnRing();
            SpawnImpactVFX(impactPoint, nrm);
            _director?.NotifyProjectileExploded(this, impactPoint, _explosionRadius);
            ExplodeAt(impactPoint, skipCrashPopup: hitCar);
        }
        else
        {
            if (!hitCar)
                TryThrownImpactCrashPopup(impactPoint);
            SpawnImpactVFX(impactPoint, nrm);
            // Plain mid-air car hit: still apply small impact zone at contact.
            if (hitCar)
                ApplyPlainImpactZone(impactPoint);
            ExplodeOrDeactivate();
        }
    }

    public void DestroyByPlayer()
    {
        if (_rewardOnDestroy > 0)
            GameManager_Racing.Instance?.RegisterObstacleReward(_rewardOnDestroy);

        SpawnImpactVFX(transform.position, Vector3.up);
        GameManager_Racing.Instance?.HandleProjectileProximity(transform.position, _impactRadius);
        ExplodeOrDeactivate();
    }

    public void InterceptedByForcefield(Vector3 awayDir, float awayDeltaV, float upDeltaV, float ignoreWithCarSeconds)
    {
        if (_rb == null) _rb = GetComponent<Rigidbody>();

        _rb.isKinematic = false;
        _rb.useGravity = true;
        _rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        _rb.interpolation = RigidbodyInterpolation.Interpolate;
        _rb.WakeUp();

        var immunity = GetComponent<LaunchImmunityMarker>();
        if (!immunity) immunity = gameObject.AddComponent<LaunchImmunityMarker>();
        immunity.Activate(Mathf.Max(0f, ignoreWithCarSeconds + 0.1f));

        _suppressExplodeUntil = Time.time + Mathf.Max(0f, ignoreWithCarSeconds);

        float mass = Mathf.Max(0.01f, _rb.mass);
        Vector3 desiredDeltaV = awayDir.normalized * awayDeltaV + Vector3.up * upDeltaV;
        Vector3 impulse = desiredDeltaV * mass;
        _rb.AddForce(impulse, ForceMode.Impulse);

        SpawnImpactVFX(transform.position, Vector3.up);
    }

    private Rigidbody EnsureRigidbodyForObstacle(GameObject rootObj)
    {
        if (rootObj == null) return null;

        var shuttle = rootObj.GetComponentInChildren<ShuttleTrackObstacle>(true);
        if (shuttle != null) shuttle.ConvertToPhysicsOnHit();

        Rigidbody found = rootObj.GetComponent<Rigidbody>() ?? rootObj.GetComponentInChildren<Rigidbody>();
        if (found == null)
        {
            found = rootObj.AddComponent<Rigidbody>();
            found.mass = Mathf.Max(0.1f, 10f);
        }

        if (found.isKinematic) found.isKinematic = false;
        found.useGravity = true;
        found.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        found.interpolation = RigidbodyInterpolation.Interpolate;
        found.WakeUp();

        Physics.SyncTransforms();
        return found;
    }

    private void SpawnImpactVFX(Vector3 worldPos, Vector3 normal)
    {
        if (impactVFXPrefab == null) return;

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
                    StartCoroutine(ReturnPooledVFXLater(impactVFXPrefab, inst, impactVFXLifetime));
                    return;
                }
            }

            inst = Instantiate(impactVFXPrefab, worldPos, Quaternion.LookRotation(normal, Vector3.up));
            Destroy(inst, impactVFXLifetime);
        }
        catch (Exception)
        {
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
    public Vector3 rpm = new Vector3(0f, 360f, 0f);

    void Update()
    {
        transform.Rotate(rpm * Time.deltaTime, Space.Self);
    }
}
