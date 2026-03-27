using System.Collections.Generic;
using UnityEngine;

/// <summary>
    /// Kinematic log that rolls along the procedural track and rams other obstacles.
    /// It only leaves the scripted path when struck by a beast or when colliding with another rolling log.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Rigidbody))]
public class RollingLogAlongTrack : MonoBehaviour
{
    [Header("Track")]
    [SerializeField] private ProceduralTrackGenerator trackGenerator;
    [SerializeField] private bool useSmoothing = true;
    [SerializeField, Min(1)] private int smoothingSubdivisionsPerSegment = 6;

    [Header("Placement")]
    [SerializeField] private LayerMask roadLayer = ~0;
    [SerializeField] private float raycastStartHeight = 6f;
    [SerializeField] private float raycastDownDistance = 24f;
    [SerializeField] private float heightOffset = 0.12f;
    [Tooltip("Clamp lateral offset to this fraction of half road width.")]
    [SerializeField, Range(0.05f, 1f)] private float lateralClampFraction = 0.85f;

    [Header("Roll visual")]
    [SerializeField, Min(0.05f)] private float rollRadius = 0.35f;
    [SerializeField] private Vector3 rollLocalAxis = Vector3.right;

    [Header("Collision (scripted phase)")]
    [Tooltip("Discrete avoids speculative early contacts on fast kinematic movers. Continuous can register hits before the mesh visually reaches the car.")]
    [SerializeField] private CollisionDetectionMode scriptedCollisionDetection = CollisionDetectionMode.Discrete;
    [Tooltip("Interpolation blends between FixedUpdate steps so the mesh can lag behind the rigidbody pose used for physics — crashes can feel early. None keeps visuals aligned with hit detection.")]
    [SerializeField] private RigidbodyInterpolation scriptedInterpolation = RigidbodyInterpolation.None;
    [Tooltip("Clamp contact offset on solid colliders (smaller = tighter contacts). 0 = leave prefab values unchanged.")]
    [SerializeField, Min(0f)] private float maxColliderContactOffset = 0.02f;

    [Header("Lifecycle")]
    [SerializeField, Min(5f)] private float despawnDistanceFromPlayer = 120f;

    [Header("Physics release")]
    [Tooltip("Optional extra layers to ignore for detaching (in addition to static world with no obstacle scripts).")]
    [SerializeField] private LayerMask detachIgnoreLayers;
    [SerializeField, Min(0f)] private float ramHorizontalImpulse = 14f;
    [SerializeField, Min(0f)] private float ramUpImpulse = 3f;
    [Tooltip("Player CarController + NPC: multiplies ramUpImpulse (old generic car path used 0.6×).")]
    [SerializeField, Min(0f)] private float vehicleRamUpMultiplier = 1.65f;
    [Tooltip("Player + NPC: multiplies ramHorizontalImpulse for vehicle hits.")]
    [SerializeField, Min(0f)] private float vehicleRamHorizontalMultiplier = 1f;
    [SerializeField, Min(0f)] private float selfDetachExtraUp = 1.5f;
    [SerializeField, Min(0f)] private float beastKnockHorizontal = 16f;
    [SerializeField, Min(0f)] private float beastKnockUp = 14f;
    [Header("Scripted overlap fallback")]
    [Tooltip("Fallback for kinematic-vs-kinematic setups: if collision callbacks don't fire, detect overlaps and still ram bounce-back obstacles.")]
    [SerializeField] private bool enableScriptedOverlapRamFallback = true;
    [SerializeField, Min(0f)] private float overlapRamCooldown = 0.2f;

    [Header("RacingObstacle ram (props)")]
    [Tooltip("Extra horizontal oomph vs RacingObstacle: base ramHorizontalImpulse is multiplied by this.")]
    [SerializeField, Min(0.01f)] private float racingObstacleRamPushMultiplier = 1.15f;
    [Tooltip("Extra upward oomph vs RacingObstacle: base ramUpImpulse is multiplied by this.")]
    [SerializeField, Min(0.01f)] private float racingObstacleRamUpMultiplier = 1.22f;
    [Tooltip("Added on top after the multiplier (impulse units). Small nudge without changing global ram.")]
    [SerializeField, Min(0f)] private float racingObstacleRamPushAdd = 1.25f;
    [SerializeField, Min(0f)] private float racingObstacleRamUpAdd = 0.35f;

    [Header("RacingObstacle hit feedback (popup + VFX)")]
    [Tooltip("Spawn Crash-style popup text at the hit (same asset as car crashes).")]
    [SerializeField] private bool racingObstacleHitShowCrashPopup = true;
    [Tooltip("World-space height above the contact point for the popup (matches other world popups).")]
    [SerializeField, Min(0f)] private float racingObstacleHitPopupHeight = 1.15f;
    [Tooltip("Optional one-shot VFX at the impact point.")]
    [SerializeField] private GameObject racingObstacleHitVfxPrefab;
    [Tooltip("Seconds before the instantiated VFX root is destroyed. 0 = never auto-destroy.")]
    [SerializeField, Min(0f)] private float racingObstacleHitVfxLifetime = 4f;
    [Tooltip("Added to the contact point for VFX spawn position.")]
    [SerializeField] private Vector3 racingObstacleHitVfxOffset = new Vector3(0f, 0.2f, 0f);

    private Rigidbody _rb;
    private Transform _player;

    private List<Vector3> _path = new();
    private float[] _cumLengths;
    private float _totalLength;

    private float _s;
    private float _signedSpeed;
    private float _lateral;
    private float _rollAngleDeg;
    private float _pivotToBottom;
    private Quaternion _prefabRootRotation = Quaternion.identity;

    private bool _ready;
    private bool _freedToPhysics;
    private Vector3 _cachedWorldVel;
    private Collider _ramProbeCollider;
    private readonly Dictionary<int, float> _overlapRamUntilByRootId = new();

    public bool IsScriptedAlongPath => _ready && !_freedToPhysics;
    public float CurrentScriptedSpeed => Mathf.Abs(_signedSpeed);
    public float RigidbodyMass => _rb != null ? _rb.mass : 1f;

    public Vector3 GetWorldVelocity()
    {
        if (_freedToPhysics && _rb != null && !_rb.isKinematic)
            return _rb.velocity;
        return _cachedWorldVel;
    }

    /// <summary>
    /// Planar push unit and impulse strengths for player/NPC vehicles. NPC applies Δv = impulse / mass in <see cref="NPCTrafficCar"/> to match <see cref="Rigidbody.AddForce"/> Impulse.
    /// </summary>
    public bool TryGetVehicleRamImpulse(Collision collision, out Vector3 planarUnit, out float horizontalImpulse, out float upImpulse)
    {
        planarUnit = Vector3.forward;
        horizontalImpulse = 0f;
        upImpulse = 0f;
        if (!isActiveAndEnabled || (!_ready && !_freedToPhysics))
            return false;

        Vector3 planar;
        if (_freedToPhysics && _rb != null && !_rb.isKinematic)
        {
            planar = _rb.velocity;
            planar.y = 0f;
            if (planar.sqrMagnitude < 1e-4f)
                planar = transform.forward;
        }
        else
        {
            planar = _cachedWorldVel;
            planar.y = 0f;
            if (planar.sqrMagnitude < 1e-4f)
                planar = transform.forward * Mathf.Sign(_signedSpeed);
        }
        planar.Normalize();
        planarUnit = planar;

        _ = collision;
        horizontalImpulse = ramHorizontalImpulse * vehicleRamHorizontalMultiplier;
        upImpulse = ramUpImpulse * vehicleRamUpMultiplier;
        return true;
    }

    public void BeginRoll(
        Transform player,
        float startDistanceAlongTrack,
        float signedSpeedAlongTrack,
        float lateralOffsetWorldRoad)
    {
        _player = player;
        _s = startDistanceAlongTrack;
        _signedSpeed = signedSpeedAlongTrack;
        _lateral = lateralOffsetWorldRoad;
        _rollAngleDeg = Random.Range(0f, 360f);
        _prefabRootRotation = transform.localRotation;
        _freedToPhysics = false;

        if (!trackGenerator)
            trackGenerator = FindObjectOfType<ProceduralTrackGenerator>();

        _rb = GetComponent<Rigidbody>();
        _rb.isKinematic = true;
        _rb.useGravity = false;
        _rb.collisionDetectionMode = scriptedCollisionDetection;
        _rb.interpolation = scriptedInterpolation;
        _rb.constraints = RigidbodyConstraints.None;

        ApplyColliderContactOffsets();
        ResolveRamProbeCollider();

        RebuildPath();
        if (_path.Count < 2 || _totalLength <= 0.01f)
        {
            Destroy(gameObject);
            return;
        }

        _s = Mathf.Clamp(_s, 0f, _totalLength);
        ApplyLateralClampFromRoadWidth();
        EstimatePivotToBottom();
        SnapToPath();
        _cachedWorldVel = ComputeScriptedWorldVelocity();
        _ready = true;
    }

    /// <summary>
    /// Beast creature hit the log (trigger path) — log leaves the spline and flies away.
    /// </summary>
    public void ApplyBeastStrike(Vector3 beastPosition, float strikeSpeed)
    {
        if (_freedToPhysics || !_ready) return;

        Vector3 away = _rb.position - beastPosition;
        away.y = 0f;
        if (away.sqrMagnitude < 1e-4f) away = -transform.forward;
        away.Normalize();

        float rel = Mathf.Max(strikeSpeed, 4f);
        ReleaseToPhysics(null, away, rel, beastKnockHorizontal, beastKnockUp + rel * 0.35f);
    }

    /// <summary>
    /// Forcefield interception: detach this log from scripted pathing and launch into physics.
    /// </summary>
    public void ApplyForcefieldLaunch(Vector3 forcefieldPosition, float relativeSpeed, float horizontalImpulse, float upImpulse)
    {
        if (_freedToPhysics || !_ready) return;

        Vector3 away = _rb.position - forcefieldPosition;
        away.y = 0f;
        if (away.sqrMagnitude < 1e-4f) away = transform.forward;
        away.Normalize();

        float rel = Mathf.Max(relativeSpeed, CurrentScriptedSpeed, 3f);
        ReleaseToPhysics(null, away, rel, Mathf.Max(0f, horizontalImpulse), Mathf.Max(0f, upImpulse));
    }

    private void ApplyColliderContactOffsets()
    {
        if (maxColliderContactOffset <= 0f) return;
        var cols = GetComponentsInChildren<Collider>(true);
        for (int i = 0; i < cols.Length; i++)
        {
            Collider c = cols[i];
            if (c == null || c.isTrigger) continue;
            c.contactOffset = Mathf.Min(c.contactOffset, maxColliderContactOffset);
        }
    }

    private void OnDisable()
    {
        _ready = false;
        _overlapRamUntilByRootId.Clear();
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (!_ready || _freedToPhysics || collision == null || collision.collider == null)
            return;

        Collider o = collision.collider;
        if (o.transform == transform || o.transform.IsChildOf(transform))
            return;

        RamOtherObstacle(collision);

        if (!ShouldReleaseOnCollision(o))
            return;

        if (((1 << o.gameObject.layer) & detachIgnoreLayers.value) != 0)
            return;

        Vector3 planar = _cachedWorldVel;
        planar.y = 0f;
        if (planar.sqrMagnitude < 1e-4f)
            planar = transform.forward * Mathf.Sign(_signedSpeed);
        planar.Normalize();

        float rel = Mathf.Max(collision.relativeVelocity.magnitude, CurrentScriptedSpeed);
        ReleaseToPhysics(collision, planar, rel, ramHorizontalImpulse * 0.25f, selfDetachExtraUp + ramUpImpulse * 0.35f);
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!_ready || _freedToPhysics || other == null)
            return;
        if (other.transform == transform || other.transform.IsChildOf(transform))
            return;

        // Fallback for setups where obstacle colliders are triggers or filtered from collision events.
        // Still apply ram logic so bounce/cross/shuttle react correctly.
        Vector3 contact = other.ClosestPoint(transform.position);
        float relSpeed = Mathf.Max(CurrentScriptedSpeed, 3f);
        RamOtherObstacle(other, contact, relSpeed);
    }

    private void ResolveRamProbeCollider()
    {
        _ramProbeCollider = null;
        Collider[] cols = GetComponentsInChildren<Collider>(true);
        for (int i = 0; i < cols.Length; i++)
        {
            Collider c = cols[i];
            if (c == null || c.isTrigger) continue;
            _ramProbeCollider = c;
            return;
        }
    }

    private void ProbeScriptedOverlapRams()
    {
        if (!enableScriptedOverlapRamFallback || !_ready || _freedToPhysics) return;
        if (_ramProbeCollider == null) return;

        float now = Time.time;
        Collider[] hits = OverlapColliderShape(_ramProbeCollider, ~0);
        if (hits == null || hits.Length == 0) return;

        for (int i = 0; i < hits.Length; i++)
        {
            Collider hit = hits[i];
            if (hit == null) continue;
            if (hit.transform == transform || hit.transform.IsChildOf(transform)) continue;

            var bounce = hit.GetComponentInParent<TrackObstacleBounceBack>();
            if (bounce == null) continue;

            Transform root = bounce.transform.root != null ? bounce.transform.root : bounce.transform;
            int id = root.GetInstanceID();
            if (_overlapRamUntilByRootId.TryGetValue(id, out float until) && now < until)
                continue;

            Vector3 contact = hit.ClosestPoint(_rb.position);
            float relSpeed = Mathf.Max(CurrentScriptedSpeed, 3f);
            RamOtherObstacle(hit, contact, relSpeed);
            _overlapRamUntilByRootId[id] = now + overlapRamCooldown;
        }
    }

    private static Collider[] OverlapColliderShape(Collider col, LayerMask layerMask)
    {
        if (col == null) return null;

        Transform t = col.transform;
        Vector3 scale = t.lossyScale;

        if (col is BoxCollider box)
        {
            Vector3 center = t.TransformPoint(box.center);
            Vector3 halfExtents = Vector3.Scale(box.size * 0.5f, scale);
            return Physics.OverlapBox(center, halfExtents, t.rotation, layerMask, QueryTriggerInteraction.Collide);
        }

        if (col is SphereCollider sphere)
        {
            Vector3 center = t.TransformPoint(sphere.center);
            float radius = Mathf.Max(scale.x, Mathf.Max(scale.y, scale.z)) * sphere.radius;
            return Physics.OverlapSphere(center, radius, layerMask, QueryTriggerInteraction.Collide);
        }

        if (col is CapsuleCollider cap)
        {
            float height = cap.height;
            float radius = cap.radius;
            int axis = cap.direction; // 0=X, 1=Y, 2=Z
            Vector3 axisDir = axis == 0 ? Vector3.right : (axis == 1 ? Vector3.up : Vector3.forward);
            float halfHeight = Mathf.Max(0f, height * 0.5f - radius);
            Vector3 p1 = t.TransformPoint(cap.center + axisDir * halfHeight);
            Vector3 p2 = t.TransformPoint(cap.center - axisDir * halfHeight);
            float r = Mathf.Max(scale.x, Mathf.Max(scale.y, scale.z)) * radius;
            return Physics.OverlapCapsule(p1, p2, r, layerMask, QueryTriggerInteraction.Collide);
        }

        Bounds b = col.bounds;
        return Physics.OverlapBox(b.center, b.extents, t.rotation, layerMask, QueryTriggerInteraction.Collide);
    }

    private bool ShouldReleaseOnCollision(Collider o)
    {
        // User intent: only beast strikes or log-vs-log should release from path.
        // Beast is handled via ApplyBeastStrike(); this path handles log-vs-log collisions.
        var otherLog = o.GetComponentInParent<RollingLogAlongTrack>();
        return otherLog != null && otherLog != this;
    }

    private void RamOtherObstacle(Collision collision)
    {
        Collider o = collision.collider;
        ContactPoint cp = collision.contactCount > 0 ? collision.GetContact(0) : default;
        Vector3 contact = collision.contactCount > 0 ? cp.point : o.bounds.center;

        Vector3 planar = _cachedWorldVel;
        planar.y = 0f;
        if (planar.sqrMagnitude < 1e-4f)
            planar = transform.forward * Mathf.Sign(_signedSpeed);
        planar.Normalize();

        float relSpeed = Mathf.Max(collision.relativeVelocity.magnitude, CurrentScriptedSpeed, 3f);
        RamOtherObstacle(o, contact, relSpeed);
    }

    private void RamOtherObstacle(Collider o, Vector3 contact, float relSpeed)
    {
        Vector3 planar = _cachedWorldVel;
        planar.y = 0f;
        if (planar.sqrMagnitude < 1e-4f)
            planar = transform.forward * Mathf.Sign(_signedSpeed);
        planar.Normalize();

        var bounce = o.GetComponentInParent<TrackObstacleBounceBack>();
        if (bounce != null)
        {
            bounce.ApplyRollingLogRam(planar, ramHorizontalImpulse, ramUpImpulse, contact);
            return;
        }

        var cross = o.GetComponentInParent<CrossTrackObstacle>();
        if (cross != null)
        {
            cross.ApplyRollingLogRam(planar, relSpeed);
            return;
        }

        var shuttle = o.GetComponentInParent<ShuttleTrackObstacle>();
        if (shuttle != null)
        {
            shuttle.ConvertToPhysicsOnHit();
            Rigidbody srb = shuttle.GetComponentInParent<Rigidbody>();
            if (srb != null)
                srb.AddForce(planar * ramHorizontalImpulse + Vector3.up * ramUpImpulse, ForceMode.Impulse);
            return;
        }

        var racing = o.GetComponentInParent<RacingObstacle>();
        if (racing != null)
        {
            Rigidbody orb = o.attachedRigidbody != null ? o.attachedRigidbody : o.GetComponentInParent<Rigidbody>();
            if (orb != null)
            {
                if (orb.isKinematic)
                {
                    orb.isKinematic = false;
                    orb.useGravity = true;
                }
                float push = ramHorizontalImpulse * racingObstacleRamPushMultiplier + racingObstacleRamPushAdd;
                float up = ramUpImpulse * racingObstacleRamUpMultiplier + racingObstacleRamUpAdd;
                orb.AddForceAtPosition(planar * push + Vector3.up * up, contact, ForceMode.Impulse);
            }

            // Rock/critter props: CrashWorld is handled on RacingObstacle.OnCollisionEnter via
            // RacingObstacleCollisionPopups (pair cooldown avoids double hits from multi-contact / ground clips).
            // Trees still use this path (RacingObstacle returns before buddy clash for Tree type).
            PlayRacingObstacleHitFeedback(contact, planar, racing);
            return;
        }

        if (o.GetComponentInParent<CarController>() != null)
        {
            Rigidbody carRb = o.attachedRigidbody != null ? o.attachedRigidbody : o.GetComponentInParent<Rigidbody>();
            if (carRb != null && carRb != _rb && !carRb.isKinematic)
            {
                float h = ramHorizontalImpulse * vehicleRamHorizontalMultiplier;
                float u = ramUpImpulse * vehicleRamUpMultiplier;
                carRb.AddForceAtPosition(planar * h + Vector3.up * u, contact, ForceMode.Impulse);
            }
            return;
        }

        Rigidbody otherRb = o.attachedRigidbody != null ? o.attachedRigidbody : o.GetComponentInParent<Rigidbody>();
        if (otherRb != null && otherRb != _rb && !otherRb.isKinematic)
            otherRb.AddForceAtPosition(planar * ramHorizontalImpulse + Vector3.up * (ramUpImpulse * 0.6f), contact, ForceMode.Impulse);
    }

    private void PlayRacingObstacleHitFeedback(Vector3 contactWorld, Vector3 planarPushDir, RacingObstacle racing)
    {
        if (!racingObstacleHitShowCrashPopup && racingObstacleHitVfxPrefab == null)
            return;

        // Non-tree RacingObstacle: obstacle side spawns CrashWorld with per-pair cooldown (covers duplicate physics contacts).
        bool crashFromObstacleSide = racing != null && racing.Type != ObstacleTyping.Tree;

        if (racingObstacleHitShowCrashPopup && RacingPopups.IsReady && !crashFromObstacleSide)
        {
            Vector3 popupPos = contactWorld + Vector3.up * racingObstacleHitPopupHeight;
            RacingPopups.CrashWorld(popupPos);
        }

        if (racingObstacleHitVfxPrefab == null) return;

        Vector3 spawnPos = contactWorld + racingObstacleHitVfxOffset;
        Vector3 fwd = planarPushDir.sqrMagnitude > 1e-6f ? planarPushDir.normalized : transform.forward;
        Quaternion rot = Quaternion.LookRotation(fwd, Vector3.up);
        GameObject vfx = Instantiate(racingObstacleHitVfxPrefab, spawnPos, rot);
        if (racingObstacleHitVfxLifetime > 0f)
            Destroy(vfx, racingObstacleHitVfxLifetime);
    }

    private void ReleaseToPhysics(Collision collision, Vector3 planarOutDir, float relativeSpeed, float selfHorizImpulse, float selfUpImpulse)
    {
        if (_freedToPhysics) return;
        _freedToPhysics = true;
        _ready = false;

        _rb.isKinematic = false;
        _rb.useGravity = true;
        _rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        _rb.interpolation = RigidbodyInterpolation.Interpolate;
        _rb.constraints = RigidbodyConstraints.None;

        Vector3 v = _cachedWorldVel;
        if (collision != null && collision.relativeVelocity.sqrMagnitude > 1f)
            v += collision.relativeVelocity * 0.35f;
        _rb.velocity = v;

        Vector3 impulseDir = planarOutDir.sqrMagnitude > 1e-4f ? planarOutDir.normalized : transform.forward;
        _rb.AddForce(impulseDir * selfHorizImpulse + Vector3.up * selfUpImpulse, ForceMode.Impulse);

        Vector3 torqueAxis = Vector3.Cross(Vector3.up, impulseDir);
        if (torqueAxis.sqrMagnitude > 1e-4f)
            _rb.AddTorque(torqueAxis.normalized * (relativeSpeed * 0.15f), ForceMode.Impulse);
    }

    private void FixedUpdate()
    {
        if (_freedToPhysics)
        {
            if (_player != null &&
                (_rb.position - _player.position).sqrMagnitude > despawnDistanceFromPlayer * despawnDistanceFromPlayer)
                Destroy(gameObject);
            return;
        }

        if (!_ready || _player == null)
            return;

        if ((_rb.position - _player.position).sqrMagnitude > despawnDistanceFromPlayer * despawnDistanceFromPlayer)
        {
            Destroy(gameObject);
            return;
        }

        float dt = Time.fixedDeltaTime;
        _s += _signedSpeed * dt;
        if (_s < 0f || _s > _totalLength)
        {
            Destroy(gameObject);
            return;
        }

        SampleAlongPath(_s, out Vector3 center, out Vector3 pathFwd);
        Vector3 flatFwd = pathFwd;
        flatFwd.y = 0f;
        if (flatFwd.sqrMagnitude < 1e-6f)
            flatFwd = Vector3.forward;
        flatFwd.Normalize();

        _cachedWorldVel = flatFwd * _signedSpeed;

        Vector3 right = Vector3.Cross(Vector3.up, flatFwd).normalized;
        float latUse = _lateral;
        if (trackGenerator != null && lateralClampFraction > 0f)
        {
            float half = trackGenerator.RoadWidth * 0.5f * lateralClampFraction;
            latUse = Mathf.Clamp(latUse, -half, half);
        }

        Vector3 targetXZ = center + right * latUse;

        float y = _rb.position.y;
        Vector3 origin = targetXZ + Vector3.up * raycastStartHeight;
        if (Physics.Raycast(origin, Vector3.down, out RaycastHit hit, raycastStartHeight + raycastDownDistance, roadLayer, QueryTriggerInteraction.Ignore))
            y = hit.point.y + _pivotToBottom + heightOffset;

        Vector3 pos = new Vector3(targetXZ.x, y, targetXZ.z);
        float speedMag = Mathf.Abs(_signedSpeed);
        float rollRadPerSec = rollRadius > 1e-4f ? speedMag / rollRadius : 0f;
        float sign = Mathf.Sign(_signedSpeed);
        _rollAngleDeg += sign * rollRadPerSec * Mathf.Rad2Deg * dt;

        Quaternion align = Quaternion.LookRotation(flatFwd, Vector3.up);
        Quaternion rollSpin = Quaternion.AngleAxis(_rollAngleDeg, rollLocalAxis.normalized);
        Quaternion rot = align * _prefabRootRotation * rollSpin;

        _rb.MovePosition(pos);
        _rb.MoveRotation(rot);
        ProbeScriptedOverlapRams();
    }

    private Vector3 ComputeScriptedWorldVelocity()
    {
        if (_path.Count < 2 || _totalLength <= 0.01f)
            return Vector3.zero;
        SampleAlongPath(_s, out _, out Vector3 pathFwd);
        Vector3 flatFwd = pathFwd;
        flatFwd.y = 0f;
        if (flatFwd.sqrMagnitude < 1e-6f)
            return Vector3.zero;
        flatFwd.Normalize();
        return flatFwd * _signedSpeed;
    }

    private void ApplyLateralClampFromRoadWidth()
    {
        if (trackGenerator == null || lateralClampFraction <= 0f) return;
        float half = trackGenerator.RoadWidth * 0.5f * lateralClampFraction;
        _lateral = Mathf.Clamp(_lateral, -half, half);
    }

    private void EstimatePivotToBottom()
    {
        _pivotToBottom = 0.1f;
        var col = GetComponentInChildren<Collider>();
        if (col != null && !col.isTrigger)
        {
            Bounds b = col.bounds;
            _pivotToBottom = Mathf.Max(0.02f, (transform.position.y - b.center.y) + b.extents.y);
        }
        else
        {
            var rend = GetComponentInChildren<Renderer>();
            if (rend != null)
            {
                Bounds b = rend.bounds;
                _pivotToBottom = Mathf.Max(0.02f, (transform.position.y - b.center.y) + b.extents.y);
            }
        }
    }

    private void SnapToPath()
    {
        SampleAlongPath(_s, out Vector3 center, out Vector3 pathFwd);
        Vector3 flatFwd = pathFwd;
        flatFwd.y = 0f;
        if (flatFwd.sqrMagnitude < 1e-6f)
            flatFwd = Vector3.forward;
        flatFwd.Normalize();

        Vector3 right = Vector3.Cross(Vector3.up, flatFwd).normalized;
        float latUse = _lateral;
        if (trackGenerator != null && lateralClampFraction > 0f)
        {
            float half = trackGenerator.RoadWidth * 0.5f * lateralClampFraction;
            latUse = Mathf.Clamp(latUse, -half, half);
        }

        Vector3 targetXZ = center + right * latUse;

        float y = transform.position.y;
        Vector3 origin = targetXZ + Vector3.up * raycastStartHeight;
        if (Physics.Raycast(origin, Vector3.down, out RaycastHit hit, raycastStartHeight + raycastDownDistance, roadLayer, QueryTriggerInteraction.Ignore))
            y = hit.point.y + _pivotToBottom + heightOffset;

        Vector3 pos = new Vector3(targetXZ.x, y, targetXZ.z);
        Quaternion align = Quaternion.LookRotation(flatFwd, Vector3.up);
        Quaternion rollSpin = Quaternion.AngleAxis(_rollAngleDeg, rollLocalAxis.normalized);
        _rb.position = pos;
        _rb.rotation = align * _prefabRootRotation * rollSpin;
    }

    private void RebuildPath()
    {
        _path.Clear();
        _cumLengths = null;
        _totalLength = 0f;

        if (trackGenerator == null) return;

        var src = trackGenerator.PathPoints;
        if (src == null || src.Count < 2) return;

        if (useSmoothing)
            GenerateSmoothedPath(src, smoothingSubdivisionsPerSegment, _path);
        else
            _path.AddRange(src);

        int n = _path.Count;
        _cumLengths = new float[n];
        float len = 0f;
        for (int i = 1; i < n; i++)
        {
            len += Vector3.Distance(_path[i - 1], _path[i]);
            _cumLengths[i] = len;
        }
        _totalLength = len;
    }

    private void SampleAlongPath(float dist, out Vector3 pos, out Vector3 fwd)
    {
        dist = Mathf.Clamp(dist, 0f, _totalLength);
        int idx = 0;
        for (int i = 0; i < _cumLengths.Length - 1; i++)
        {
            if (_cumLengths[i + 1] >= dist)
            {
                idx = i;
                break;
            }
        }

        float segLen = _cumLengths[idx + 1] - _cumLengths[idx];
        float t = segLen > 1e-4f ? (dist - _cumLengths[idx]) / segLen : 0f;
        pos = Vector3.Lerp(_path[idx], _path[idx + 1], t);
        fwd = (_path[idx + 1] - _path[idx]).normalized;
    }

    private static void GenerateSmoothedPath(List<Vector3> raw, int subdivisions, List<Vector3> outList)
    {
        outList.Clear();
        outList.Add(raw[0]);
        for (int i = 0; i < raw.Count - 1; i++)
        {
            Vector3 p0 = raw[Mathf.Max(i - 1, 0)];
            Vector3 p1 = raw[i];
            Vector3 p2 = raw[i + 1];
            Vector3 p3 = raw[Mathf.Min(i + 2, raw.Count - 1)];
            for (int s = 1; s <= subdivisions; s++)
            {
                float t = s / (float)subdivisions;
                outList.Add(CatmullRom(p0, p1, p2, p3, t));
            }
        }
    }

    private static Vector3 CatmullRom(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t)
    {
        return 0.5f * (
            2f * p1 +
            (-p0 + p2) * t +
            (2f * p0 - 5f * p1 + 4f * p2 - p3) * (t * t) +
            (-p0 + 3f * p1 - 3f * p2 + p3) * (t * t * t)
        );
    }
}
