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
    [Tooltip("Ignored at runtime — follows ProceduralTrackGenerator's road mesh centerline.")]
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
    [Tooltip("Despawn scripted logs if they have barely moved for this many seconds.")]
    [SerializeField, Min(0.25f)] private float stuckDespawnSeconds = 3.5f;
    [Tooltip("Movement under this distance per FixedUpdate counts as 'not moving' for stuck despawn.")]
    [SerializeField, Min(0.0001f)] private float stuckMoveEpsilonPerStep = 0.02f;

    [Header("Ramp blocking")]
    [Tooltip("If enabled, scripted logs stop before entering ramp surfaces instead of climbing onto ramps.")]
    [SerializeField] private bool stopBeforeRamps = true;

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
    [Tooltip("Scripted logs and NPCTrafficCar are both kinematic while driving; PhysX will not emit contact callbacks between them. Overlap probe applies the same crash+ram as a real collision.")]
    [SerializeField] private bool enableScriptedNpcTrafficOverlapHit = true;

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
    /// <summary>Lane position as a fraction of clamped half road width (-1 = inner left, +1 = inner right). Preserved through turns.</summary>
    private float _lateralFraction;
    private float _rollAngleDeg;
    private float _pivotToBottom;
    private Quaternion _prefabRootRotation = Quaternion.identity;
    private Vector3 _lastScriptedPos;
    private float _stuckTimer;

    private bool _ready;
    private bool _freedToPhysics;
    private Vector3 _cachedWorldVel;
    private Collider _ramProbeCollider;
    private readonly Dictionary<int, float> _overlapRamUntilByRootId = new();
    private readonly Dictionary<int, float> _overlapNpcHitUntilByRootId = new();
    private ProceduralTrackGenerator _subscribedTrackGenerator;

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
    /// <param name="collision">Optional; unused except for API symmetry with <see cref="OnCollisionEnter"/>. May be null for overlap-driven hits.</param>
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
        _signedSpeed = signedSpeedAlongTrack;
        _rollAngleDeg = Random.Range(0f, 360f);
        _prefabRootRotation = transform.localRotation;
        _freedToPhysics = false;

        if (!trackGenerator)
            trackGenerator = FindObjectOfType<ProceduralTrackGenerator>();

        SubscribeTrackRegenerated();

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

        Vector3 spawnWorld = _rb.position;
        if (TrackPathSampling.ProjectWorldPosition(_path, _cumLengths, _totalLength, spawnWorld, out float projectedS, out _))
            _s = projectedS;
        else
            _s = startDistanceAlongTrack;

        _s = Mathf.Clamp(_s, 0f, _totalLength);
        _lateral = lateralOffsetWorldRoad;
        ApplyLateralClampFromRoadWidth();
        CacheLateralFraction();
        EstimatePivotToBottom();
        SnapToPath();
        _cachedWorldVel = ComputeScriptedWorldVelocity();
        _lastScriptedPos = _rb.position;
        _stuckTimer = 0f;
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
        _overlapNpcHitUntilByRootId.Clear();
        UnsubscribeTrackRegenerated();
    }

    private void SubscribeTrackRegenerated()
    {
        UnsubscribeTrackRegenerated();
        if (trackGenerator == null) return;
        trackGenerator.OnTrackGeneratedSuccessfully += HandleTrackRegenerated;
        _subscribedTrackGenerator = trackGenerator;
    }

    private void UnsubscribeTrackRegenerated()
    {
        if (_subscribedTrackGenerator != null)
        {
            _subscribedTrackGenerator.OnTrackGeneratedSuccessfully -= HandleTrackRegenerated;
            _subscribedTrackGenerator = null;
        }
    }

    private void HandleTrackRegenerated(ProceduralTrackGenerator gen)
    {
        if (!_ready || _freedToPhysics || _rb == null) return;

        RebuildPath();
        if (_path.Count < 2 || _totalLength <= 0.01f)
            return;

        if (TrackPathSampling.ProjectWorldPosition(_path, _cumLengths, _totalLength, _rb.position, out float s, out _))
            _s = Mathf.Clamp(s, 0f, _totalLength);

        ApplyLateralClampFromRoadWidth();
        SnapToPath();
        _cachedWorldVel = ComputeScriptedWorldVelocity();
        _lastScriptedPos = _rb.position;
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

    private void ProbeScriptedNpcTrafficOverlapHits()
    {
        if (!enableScriptedNpcTrafficOverlapHit || !_ready || _freedToPhysics) return;
        if (_ramProbeCollider == null) return;

        float now = Time.time;
        Collider[] hits = OverlapColliderShape(_ramProbeCollider, ~0);
        if (hits == null || hits.Length == 0) return;

        Collider logSolid = _ramProbeCollider;
        for (int i = 0; i < hits.Length; i++)
        {
            Collider hit = hits[i];
            if (hit == null) continue;
            if (hit.transform == transform || hit.transform.IsChildOf(transform)) continue;

            var npc = hit.GetComponentInParent<NPCTrafficCar>();
            if (npc == null) continue;

            Transform root = npc.transform.root != null ? npc.transform.root : npc.transform;
            int id = root.GetInstanceID();
            if (_overlapNpcHitUntilByRootId.TryGetValue(id, out float until) && now < until)
                continue;

            Vector3 contact = hit.ClosestPoint(_rb.position);
            float relSpeed = Mathf.Max(CurrentScriptedSpeed, 3f);
            npc.ApplyScriptedRollingLogOverlapHit(this, logSolid, contact, relSpeed);
            _overlapNpcHitUntilByRootId[id] = now + overlapRamCooldown;
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
        float nextS = _s + _signedSpeed * dt;
        if (nextS < 0f || nextS > _totalLength)
        {
            Destroy(gameObject);
            return;
        }

        bool blockedByRamp = false;
        if (stopBeforeRamps && IsRampAtDistance(nextS))
        {
            // Keep the log at the bottom cusp of the ramp by refusing forward scripted progress onto ramp surfaces.
            nextS = _s;
            blockedByRamp = true;
        }

        _s = nextS;
        TrackPathSampling.SampleAlongPath(_path, _cumLengths, _totalLength, _s, out Vector3 center, out Vector3 flatFwd);

        _cachedWorldVel = flatFwd * _signedSpeed;

        Vector3 right = Vector3.Cross(Vector3.up, flatFwd).normalized;
        float latUse = GetLateralMeters();

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

        float moved = Vector3.Distance(pos, _lastScriptedPos);
        if (moved <= stuckMoveEpsilonPerStep || blockedByRamp)
            _stuckTimer += dt;
        else
            _stuckTimer = 0f;
        _lastScriptedPos = pos;

        if (_stuckTimer >= stuckDespawnSeconds)
        {
            Destroy(gameObject);
            return;
        }

        ProbeScriptedOverlapRams();
        ProbeScriptedNpcTrafficOverlapHits();
    }

    private bool IsRampAtDistance(float distAlongTrack)
    {
        if (_path.Count < 2 || _totalLength <= 0.01f)
            return false;

        TrackPathSampling.SampleAlongPath(_path, _cumLengths, _totalLength, distAlongTrack, out Vector3 center, out Vector3 flatFwd);

        Vector3 right = Vector3.Cross(Vector3.up, flatFwd).normalized;
        float latUse = GetLateralMeters();

        Vector3 targetXZ = center + right * latUse;
        Vector3 origin = targetXZ + Vector3.up * raycastStartHeight;
        if (!Physics.Raycast(origin, Vector3.down, out RaycastHit hit, raycastStartHeight + raycastDownDistance, roadLayer, QueryTriggerInteraction.Ignore))
            return false;

        GroundSurface surface = hit.collider != null ? hit.collider.GetComponentInParent<GroundSurface>() : null;
        return surface != null && surface.surfaceType == SurfaceType.Ramp;
    }

    private Vector3 ComputeScriptedWorldVelocity()
    {
        if (_path.Count < 2 || _totalLength <= 0.01f)
            return Vector3.zero;
        TrackPathSampling.SampleAlongPath(_path, _cumLengths, _totalLength, _s, out _, out Vector3 flatFwd);
        return flatFwd * _signedSpeed;
    }

    private float GetLateralMeters()
    {
        if (trackGenerator == null || lateralClampFraction <= 0f)
            return _lateral;

        float half = trackGenerator.RoadWidth * 0.5f * lateralClampFraction;
        return Mathf.Clamp(_lateralFraction, -1f, 1f) * half;
    }

    private void CacheLateralFraction()
    {
        if (trackGenerator == null || lateralClampFraction <= 0f)
        {
            _lateralFraction = 0f;
            return;
        }

        float half = trackGenerator.RoadWidth * 0.5f * lateralClampFraction;
        _lateralFraction = half > 1e-4f ? Mathf.Clamp(_lateral / half, -1f, 1f) : 0f;
    }

    private void ApplyLateralClampFromRoadWidth()
    {
        if (trackGenerator == null || lateralClampFraction <= 0f) return;
        float half = trackGenerator.RoadWidth * 0.5f * lateralClampFraction;
        _lateral = Mathf.Clamp(_lateral, -half, half);
        CacheLateralFraction();
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
        TrackPathSampling.SampleAlongPath(_path, _cumLengths, _totalLength, _s, out Vector3 center, out Vector3 flatFwd);

        Vector3 right = Vector3.Cross(Vector3.up, flatFwd).normalized;
        float latUse = GetLateralMeters();

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

        TrackPathSampling.BuildCenterlinePath(trackGenerator, _path);
        if (_path.Count < 2) return;

        int n = _path.Count;
        _cumLengths = new float[n];
        TrackPathSampling.BuildCumulativeLengths(_path, _cumLengths, out _totalLength);
    }
}
