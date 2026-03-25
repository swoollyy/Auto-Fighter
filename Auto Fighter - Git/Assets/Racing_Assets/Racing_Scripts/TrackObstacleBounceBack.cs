using DG.Tweening;
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// AAA version:
/// - Kinematic RB + non-trigger collider => real collisions (can land on other obstacles).
/// - DOTween drives a smooth bounce param, FixedUpdate applies MovePosition/MoveRotation.
/// - Always follows track path (cannot be pushed off path), but pushes OTHER obstacles on impact.
/// - Car hit triggers full crash sim; optionally uses centralized CrashSeverityConfig (see useCentralizedCrashSeverity).
/// </summary>
[RequireComponent(typeof(Collider))]
[RequireComponent(typeof(Rigidbody))]
public class TrackObstacleBounceBack : MonoBehaviour
{
    [Header("Track Reference")]
    [SerializeField] private ProceduralTrackGenerator trackGenerator;

    [Header("Path Sampling (match spawner)")]
    [SerializeField] private bool useSmoothing = true;
    [SerializeField, Min(1)] private int smoothingSubdivisionsPerSegment = 6;

    [Header("Bounce Movement")]
    [Tooltip("How far (meters) each bounce moves backward along the track.")]
    [SerializeField, Min(0.1f)] private float bounceStepDistance = 18f;

    [Tooltip("Seconds per bounce (arc duration).")]
    [SerializeField, Min(0.05f)] private float bounceDuration = 0.25f;

    [Tooltip("Seconds to wait between bounces. Fully frozen during this time.")]
    [SerializeField, Min(0f)] private float bounceCooldown = 0.65f;

    [Header("Initial Cooldown Randomization")]
    [Tooltip("Randomize the initial cooldown on spawn so obstacles don't bounce in sync.")]
    [SerializeField] private bool randomizeInitialCooldown = true;

    [Tooltip("Range for the random initial cooldown (min, max) in seconds.")]
    [SerializeField] private Vector2 initialCooldownRange = new Vector2(0f, 1.5f);

    [Tooltip("If true, bounce + cooldown uses real time (ignores Time.timeScale).")]
    [SerializeField] private bool useUnscaledTime = true;

    [Header("Bounce Arc")]
    [Tooltip("Peak hop height during a bounce (meters).")]
    [SerializeField, Min(0f)] private float bounceJumpHeight = 1.0f;
    [Tooltip("When landing on a ramp (SurfaceType.Ramp), multiply jump height by this so the hop clears the ramp. Flat boost pads do not get this extra height.")]
    [SerializeField, Min(1f)] private float bounceJumpHeightRampMultiplier = 1.8f;

    [Header("Lateral Clamp")]
    [SerializeField] private bool clampToRoadWidth = true;
    [Range(0.1f, 1f)]
    [SerializeField] private float lateralClampFraction = 0.95f;

    [Header("Grounding / Landing")]
    [Tooltip("What counts as 'ground' for landing. Include Road + Obstacle layers if you want it to land on obstacles.")]
    [SerializeField] private LayerMask groundMask;

    [SerializeField, Min(0.1f)] private float raycastStartHeight = 8f;
    [SerializeField, Min(0.1f)] private float raycastDownDistance = 40f;

    [Tooltip("Extra gap above the hit surface so it doesn't z-fight / stick.")]
    [SerializeField, Min(0f)] private float heightOffset = 0.02f;

    [Header("Rotation")]
    [Tooltip("Face opposite the track forward (since we travel backward).")]
    [SerializeField] private bool faceDirectionOfTravel = true;

    [Tooltip("Lock up axis to Vector3.up for stability (recommended).")]
    [SerializeField] private bool keepUpright = true;

    [Header("Collision Filtering (AAA)")]
    [SerializeField, Tooltip("Layers this obstacle should NOT physically collide with (ex: Player/Car). We still detect hits via overlap query.")]
    private LayerMask noPhysicalCollisionMask;

    [SerializeField, Tooltip("Layers used to detect the car for damage/crash sim (usually same as Player/Car).")]
    private LayerMask carDetectMask;

    [SerializeField, Min(0.01f)] private float carDetectRadius = 0.6f;

    [Tooltip("When the obstacle is in the air (bounce arc), use this sphere radius for car hit instead of the full collider. Stops hits registering when the car is clear of the visible mesh.")]
    [SerializeField, Min(0.1f)] private float carHitRadiusWhenAirborne = 0.5f;

    [Header("Landing Preview (Decal / GroundRing)")]
    [SerializeField, Tooltip("Use the SAME prefab you use for ThrownObstacleDirector.groundRingPrefab (pooled).")]
    private GameObject landingTelegraphPrefab;

    [SerializeField, Tooltip("If true, use this obstacle's collider footprint as telegraph radius.")]
    private bool telegraphRadiusFromCollider = true;

    [SerializeField, Min(0.1f)]
    private float telegraphRadiusOverride = 1.5f;

    [SerializeField]
    private Vector2 telegraphRadiusClamp = new Vector2(0.75f, 4.0f);

    private bool _forcefieldDetached;

    [Header("Landing Settle (Fix Shake)")]
    [SerializeField, Min(0f), Tooltip("Time in seconds to let physics settle after landing before freezing.")]
    private float landingSettleTime = 0.06f;

    [SerializeField, Min(0f), Tooltip("Extra upward lift on landing to avoid micro-penetration.")]
    private float landingLift = 0.03f;

    private float _settleUntil;
    private bool _isSettling;


    [Header("Obstacle Reactions")]
    [SerializeField] private bool reactToOtherObstacles = true;

    [Tooltip("Layers considered as other obstacles that should get pushed when hit.")]
    [SerializeField] private LayerMask obstacleReactMask;

    [SerializeField, Min(0f)] private float obstacleImpulse = 10f;
    [SerializeField, Min(0f)] private float obstacleUpImpulse = 2f;

    [Tooltip("Minimum time between applying impulse to other obstacles (prevents spam vibration).")]
    [SerializeField, Min(0f)] private float obstacleImpactCooldown = 0.10f;

    [Header("Obstacle clash popup (Crash style)")]
    [SerializeField] private bool enableObstacleClashCrashPopup = true;
    [SerializeField, Min(0f)] private float obstacleClashPopupHeight = 1f;
    [SerializeField, Min(0f)] private float obstacleClashMinRelativeSpeed = 2f;
    [SerializeField, Min(0f)] private float obstacleClashPairCooldown = 0.2f;

    [Header("Damage On Player Hit")]
    [SerializeField] private bool damagePlayerOnHit = true;

    [Tooltip("If true, uses the car's CrashSeverityConfig (obstacle kind BounceBack): closing speed × mass × scale × per-kind weight. Ignores Hit HP Damage / Hit Fuel Percent.")]
    [SerializeField] private bool useCentralizedCrashSeverity = true;

    [Tooltip("When centralized severity is on: multiplied into the computed severity after the config (1 = default).")]
    [SerializeField, Min(0f)] private float centralizedSeverityExtraMultiplier = 1f;

    [Tooltip("Flat HP damage applied on hit (only when Use Centralized Crash Severity is off).")]
    [SerializeField, Min(0f)] private float hitHpDamage = 1f;

    [Tooltip("Fuel damage as a FRACTION of max fuel (only when centralized severity is off). 0.05 = 5% of max fuel.")]
    [SerializeField, Range(0f, 1f)] private float hitFuelPercent = 0.02f;

    [Tooltip("Cooldown between player damage applications.")]
    [SerializeField, Min(0f)] private float hitDamageCooldown = 0.5f;

    [Tooltip("Fallback 0–1 severity when the car has no CrashSeverityConfig assigned, or extra tuning hint. Also used for legacy flat-damage mode as FX severity.")]
    [SerializeField, Range(0f, 1f)] private float crashFxSeverity = 0.65f;

    [Tooltip("Closing speed passed into crash severity (centralized mode) and impact speed for crash sim (fling/torque). Car caps with Max Crash Fling Speed on CarCrashMashConfig.")]
    [SerializeField, Min(0f)] private float crashImpactSpeed = 14f;

    [Tooltip("When the obstacle hits the car, it loses pathing and reacts with physics. Impulse magnitude applied to the obstacle away from the car (0 = use estimated speed).")]
    [SerializeField, Min(0f)] private float carHitDetachImpulse = 0f;

    [Header("Per-Instance Ignore (Fix global ignore)")]
    [SerializeField, Min(0f), Tooltip("How long to ignore collisions with a masked collider after contact.")]
    private float ignoreCollisionSeconds = 0.25f;

    private readonly Dictionary<Collider, float> _ignoredUntil = new();
    private readonly List<Collider> _toRestore = new();


    // ---------------- internals ----------------

    private readonly List<Vector3> _path = new();
    private float[] _cumLengths;
    private float _totalLength;

    private float _dist;
    private float _lateralOffset;


    private Rigidbody _rb;
    private Collider _col;

    private float _telegraphRadiusCached = 1.5f;
    private GameObject _landingTeleInst;

    // stable pivot->bottom offset along world up
    private float _pivotToBottomUp;

    private bool _initialized;

    private enum State { FrozenCooldown, Bouncing }
    private State _state = State.FrozenCooldown;

    private float _frozenUntil;
    private Vector3 _frozenPos;
    private Quaternion _frozenRot;

    private float _bounceStartDist;
    private float _bounceEndDist;

    // DOTween drives this
    private float _bounceT;
    private Tween _bounceTween;
    private bool _landingOnRamp;

    // gating
    private float _nextAllowedCarHitTime;
    private float _nextAllowedObstacleImpulseTime;

    // for impact velocity
    private Vector3 _prevPos;
    private Vector3 _estimatedVel;

    private void Awake()
    {
        if (!trackGenerator) trackGenerator = FindFirstObjectByType<ProceduralTrackGenerator>();

        _rb = GetComponent<Rigidbody>();
        _col = GetComponent<Collider>();

        // Physics setup: kinematic but collides
        _rb.isKinematic = true;
        _rb.interpolation = RigidbodyInterpolation.Interpolate;
        _rb.collisionDetectionMode = CollisionDetectionMode.Discrete;


    }

    private void OnEnable()
    {
        _initialized = false;
        _state = State.FrozenCooldown;
        _frozenUntil = 0f;

        _bounceTween?.Kill();
        _bounceTween = null;
        _bounceT = 0f;

        _nextAllowedCarHitTime = 0f;
        _nextAllowedObstacleImpulseTime = 0f;

        _prevPos = transform.position;
        _estimatedVel = Vector3.zero;
    }

    private void OnDisable()
    {
        _bounceTween?.Kill();
        _bounceTween = null;
        ClearLandingTelegraph();
    }

    private void Start()
    {
        InitializeIfNeeded();
    }

    private void FixedUpdate()
    {
        if (!InitializeIfNeeded())
            return;

        if (_forcefieldDetached) return;

        float dt = Time.fixedDeltaTime;

        // simple velocity estimate for impulses
        Vector3 curPos = _rb.position;
        _estimatedVel = (curPos - _prevPos) / Mathf.Max(0.0001f, dt);
        _prevPos = curPos;

        float now = useUnscaledTime ? Time.unscaledTime : Time.time;

        UpdateIgnoredCollisions(now);

        if (_state == State.FrozenCooldown)
        {
            // Fully kill all motion while frozen (your request)
            if (now < _frozenUntil)
            {
                // If we're in the settle window, let physics resolve contacts without rail forcing.
                if (_isSettling && now < _settleUntil)
                    return;

                // After settle: freeze hard
                _rb.isKinematic = true;
                _rb.velocity = Vector3.zero;
                _rb.angularVelocity = Vector3.zero;
                _rb.MovePosition(_frozenPos);
                _rb.MoveRotation(_frozenRot);
                return;
            }


            // Start a new bounce
            StartBounce(now);
        }

        // Bouncing
        float t01 = Mathf.Clamp01(_bounceT);

        float distNow = Mathf.Lerp(_bounceStartDist, _bounceEndDist, t01);
        _dist = distNow;

        SampleAlongPath(distNow, out Vector3 center, out Vector3 forward);

        Vector3 flatForward = forward;
        flatForward.y = 0f;
        if (flatForward.sqrMagnitude < 0.0001f) flatForward = Vector3.forward;
        flatForward.Normalize();

        Vector3 right = Vector3.Cross(Vector3.up, flatForward).normalized;

        float lateral = _lateralOffset;
        if (clampToRoadWidth && trackGenerator != null)
        {
            float halfWidth = Mathf.Max(0.1f, trackGenerator.RoadWidth * 0.5f);
            float maxLat = halfWidth * lateralClampFraction;
            lateral = Mathf.Clamp(lateral, -maxLat, maxLat);
        }

        Vector3 basePos = center + right * lateral;

        // Ground snap under the track-glued XZ (road OR obstacle if in groundMask)
        float groundY = basePos.y;
        if (RaycastGroundY(basePos, out RaycastHit hit))
        {
            groundY = hit.point.y + _pivotToBottomUp + heightOffset;
        }
        else
        {
            // fallback: keep current Y if we somehow miss
            groundY = _rb.position.y;
        }

        // Arc (parabola): 4h t(1-t). Use higher jump only when landing on a ramp so we clear it; flat boost pads unchanged.
        float effectiveHeight = _landingOnRamp ? bounceJumpHeight * bounceJumpHeightRampMultiplier : bounceJumpHeight;
        float arc = 0f;
        if (effectiveHeight > 0f)
            arc = 4f * effectiveHeight * t01 * (1f - t01);

        Vector3 desired = new Vector3(basePos.x, groundY + arc, basePos.z);

        Quaternion desiredRot = ComputeRotation(flatForward);

        _rb.MovePosition(desired);
        _rb.MoveRotation(desiredRot);

        if (_state == State.Bouncing)
            CheckCarHitQuery(now);

        // End bounce when tween completes
        if (t01 >= 0.9999f)
        {
            EndBounce(now, desired, desiredRot);
        }
    }

    private bool InitializeIfNeeded()
    {
        if (_initialized) return true;

        if (!trackGenerator) trackGenerator = FindFirstObjectByType<ProceduralTrackGenerator>();
        if (!trackGenerator) return false;

        RebuildPath();
        if (_path.Count < 2 || _totalLength <= 0.01f)
            return false;

        // Compute a stable pivot->bottom offset along world up.
        // This avoids the "sink deeper every jump" problem.
        if (_col != null)
        {
            // pivot->bottom = pivotY - bottomY ; bottomY = bounds.centerY - extentsY
            // => pivot->bottom = (pivotY - centerY) + extentsY
            Bounds b = _col.bounds;
            _pivotToBottomUp = (transform.position.y - b.center.y) + b.extents.y;
            _pivotToBottomUp = Mathf.Max(0f, _pivotToBottomUp);
        }
        else
        {
            _pivotToBottomUp = 0f;
        }

        if (telegraphRadiusFromCollider && _col != null)
        {
            Bounds b = _col.bounds;
            float r = Mathf.Max(b.extents.x, b.extents.z);
            _telegraphRadiusCached = Mathf.Clamp(r, telegraphRadiusClamp.x, telegraphRadiusClamp.y);
        }
        else
        {
            _telegraphRadiusCached = Mathf.Clamp(telegraphRadiusOverride, telegraphRadiusClamp.x, telegraphRadiusClamp.y);
        }

        // Find current distance + lateral offset at spawn
        _dist = GetDistanceAlongTrack(transform.position);

        SampleAlongPath(_dist, out Vector3 center, out Vector3 forward);
        Vector3 flatForward = forward; flatForward.y = 0f;
        if (flatForward.sqrMagnitude < 0.0001f) flatForward = Vector3.forward;
        flatForward.Normalize();

        Vector3 right = Vector3.Cross(Vector3.up, flatForward).normalized;
        _lateralOffset = Vector3.Dot(transform.position - center, right);

        // Snap once immediately, then start bouncing
        Vector3 snapPos = transform.position;
        if (RaycastGroundY(new Vector3(transform.position.x, transform.position.y, transform.position.z), out RaycastHit hit))
        {
            snapPos.y = hit.point.y + _pivotToBottomUp + heightOffset;
        }

        Quaternion snapRot = ComputeRotation(flatForward);
        _rb.position = snapPos;
        _rb.rotation = snapRot;

        _frozenPos = snapPos;
        _frozenRot = snapRot;

        _state = State.FrozenCooldown;

        // Randomize initial cooldown so obstacles don't all bounce in sync
        float now = useUnscaledTime ? Time.unscaledTime : Time.time;
        if (randomizeInitialCooldown)
        {
            float minDelay = Mathf.Min(initialCooldownRange.x, initialCooldownRange.y);
            float maxDelay = Mathf.Max(initialCooldownRange.x, initialCooldownRange.y);
            _frozenUntil = now + UnityEngine.Random.Range(minDelay, maxDelay);
        }
        else
        {
            _frozenUntil = now; // can bounce immediately
        }

        _initialized = true;
        return true;
    }

    private void StartBounce(float now)
    {
        _isSettling = false;
        _settleUntil = 0f;

        _rb.isKinematic = true;
        _rb.velocity = Vector3.zero;
        _rb.angularVelocity = Vector3.zero;


        _state = State.Bouncing;

        _bounceStartDist = _dist;
        _bounceEndDist = Mathf.Max(0f, _dist - bounceStepDistance);

        // Preview the landing spot for THIS bounce (same as thrown obstacles)
        SampleAlongPath(_bounceEndDist, out Vector3 centerEnd, out Vector3 forwardEnd);

        Vector3 flatForward = forwardEnd;
        flatForward.y = 0f;
        if (flatForward.sqrMagnitude < 0.0001f) flatForward = Vector3.forward;
        flatForward.Normalize();

        Vector3 right = Vector3.Cross(Vector3.up, flatForward).normalized;

        // lateral clamp must match movement
        float lateral = _lateralOffset;
        if (clampToRoadWidth && trackGenerator != null)
        {
            float halfWidth = Mathf.Max(0.1f, trackGenerator.RoadWidth * 0.5f);
            float maxLat = halfWidth * lateralClampFraction;
            lateral = Mathf.Clamp(lateral, -maxLat, maxLat);
        }

        Vector3 landingXZ = centerEnd + right * lateral;

        // project to surface (use hit.point for decal placement)
        Vector3 landingPoint = landingXZ;
        _landingOnRamp = false;
        if (RaycastGroundY(landingXZ, out RaycastHit hit))
        {
            landingPoint = hit.point;
            if (hit.collider != null)
            {
                GroundSurface surface = hit.collider.GetComponent<GroundSurface>() ?? hit.collider.GetComponentInParent<GroundSurface>();
                _landingOnRamp = surface != null && surface.surfaceType == SurfaceType.Ramp;
            }
        }

        // show for (almost) the bounce duration
        float holdSeconds = Mathf.Max(0.05f, bounceDuration - 0.02f);
        SpawnLandingTelegraph(landingPoint, holdSeconds);


        _bounceTween?.Kill();
        _bounceT = 0f;

        // DOTween drives progress; FixedUpdate applies physics motion
        _bounceTween = DOTween.To(() => _bounceT, v => _bounceT = v, 1f, Mathf.Max(0.05f, bounceDuration))
            .SetEase(Ease.OutQuad)
            .SetUpdate(useUnscaledTime); // true => ignore timeScale
    }

    private void EndBounce(float now, Vector3 finalPos, Quaternion finalRot)
    {
        _bounceTween?.Kill();
        _bounceTween = null;
        _bounceT = 1f;

        // HARD snap to ground at end, then FREEZE completely until next bounce.
        // This kills the vibration/jitter during cooldown.
        Vector3 snapPos = finalPos;

        // Snap ground without arc
        Vector3 xz = new Vector3(finalPos.x, finalPos.y, finalPos.z);
        if (RaycastGroundY(xz, out RaycastHit hit))
        {
            snapPos.y = hit.point.y + _pivotToBottomUp + heightOffset;
        }

        _frozenPos = snapPos + Vector3.up * landingLift;
        _frozenRot = finalRot;

        // Let physics settle for a brief moment to prevent kinematic-vs-collider fight jitter
        _isSettling = landingSettleTime > 0f;
        _settleUntil = now + landingSettleTime;

        // Temporarily hand off to physics
        _rb.isKinematic = false;
        _rb.velocity = Vector3.zero;
        _rb.angularVelocity = Vector3.zero;

        // Put it slightly above the surface so it can land cleanly
        _rb.position = _frozenPos;
        _rb.rotation = _frozenRot;

        _state = State.FrozenCooldown;
        _frozenUntil = now + bounceCooldown;

    }

    private Quaternion ComputeRotation(Vector3 flatForward)
    {
        Vector3 lookDir = faceDirectionOfTravel ? -flatForward : flatForward;
        if (lookDir.sqrMagnitude < 0.0001f) lookDir = transform.forward;

        if (keepUpright)
            return Quaternion.LookRotation(lookDir, Vector3.up);

        // If you ever want slope tilt later, you’d feed in hit.normal, but upright is the stable/AAA choice here.
        return Quaternion.LookRotation(lookDir, Vector3.up);
    }

    private void SpawnLandingTelegraph(Vector3 landingPoint, float seconds)
    {
        if (landingTelegraphPrefab == null) return;
        if (ProjectilePool.Instance == null) return;

        // Clear any previous preview (safety)
        ClearLandingTelegraph();

        var tele = ProjectilePool.Instance.Get(landingTelegraphPrefab);
        if (tele == null) return;

        _landingTeleInst = tele;
        tele.SetActive(true);

        // Prefer URPDecalTelegraph, fall back to GroundRing, else just return later.
        var decalTele = tele.GetComponent<URPDecalTelegraph>();
        if (decalTele != null)
        {
            decalTele.SetWorldPose(landingPoint);
            decalTele.Play(
                radius: _telegraphRadiusCached,
                seconds: Mathf.Max(0.05f, seconds),
                onComplete: () =>
                {
                    if (_landingTeleInst == tele) _landingTeleInst = null;
                    ProjectilePool.Instance.Return(landingTelegraphPrefab, tele);
                }
            );
            return;
        }

        var gr = tele.GetComponent<GroundRing>();
        if (gr != null)
        {
            gr.Play(
                _telegraphRadiusCached,
                onComplete: () =>
                {
                    if (_landingTeleInst == tele) _landingTeleInst = null;
                    ProjectilePool.Instance.Return(landingTelegraphPrefab, tele);
                },
                holdOverride: Mathf.Max(0.05f, seconds)
            );
            return;
        }

        StartCoroutine(ReturnTelegraphLater(landingTelegraphPrefab, tele, Mathf.Max(0.1f, seconds)));
    }

    private void ClearLandingTelegraph()
    {
        if (_landingTeleInst == null) return;

        // If it was pooled, just return it immediately.
        if (landingTelegraphPrefab != null && ProjectilePool.Instance != null)
            ProjectilePool.Instance.Return(landingTelegraphPrefab, _landingTeleInst);

        _landingTeleInst = null;
    }

    private IEnumerator ReturnTelegraphLater(GameObject prefab, GameObject inst, float delay)
    {
        yield return new WaitForSeconds(delay);

        if (_landingTeleInst == inst) _landingTeleInst = null;

        if (prefab != null && inst != null && ProjectilePool.Instance != null)
            ProjectilePool.Instance.Return(prefab, inst);
    }


    private bool RaycastGroundY(Vector3 aroundPos, out RaycastHit hit)
    {
        Vector3 origin = new Vector3(aroundPos.x, aroundPos.y + raycastStartHeight, aroundPos.z);
        float maxRay = raycastStartHeight + raycastDownDistance;

        return Physics.Raycast(origin, Vector3.down, out hit, maxRay, groundMask, QueryTriggerInteraction.Ignore);
    }

    private void OnCollisionEnter(Collision collision)
    {
        float now = useUnscaledTime ? Time.unscaledTime : Time.time;
        IgnoreCollisionTemporarily(collision.collider, now);

        // Other obstacle reaction only
        if (reactToOtherObstacles && obstacleReactMask.value != 0)
        {
            int otherLayerMaskBit = 1 << collision.collider.gameObject.layer;
            if ((obstacleReactMask.value & otherLayerMaskBit) != 0 &&
                RacingObstacleCollisionPopups.IsObstacleBuddy(collision.collider))
            {
                RacingObstacleCollisionPopups.TrySpawnObstacleClash(
                    transform.root,
                    collision.collider.transform.root,
                    collision,
                    collision.collider,
                    collision.relativeVelocity.magnitude,
                    obstacleClashMinRelativeSpeed,
                    obstacleClashPopupHeight,
                    obstacleClashPairCooldown,
                    enableObstacleClashCrashPopup);
            }

            if (now < _nextAllowedObstacleImpulseTime) return;

            if ((obstacleReactMask.value & otherLayerMaskBit) == 0) return;

            Rigidbody otherRb = collision.rigidbody;
            if (otherRb != null && otherRb != _rb && !otherRb.isKinematic)
            {
                ContactPoint cp = collision.GetContact(0);

                Vector3 dir = _estimatedVel;
                dir.y = 0f;
                if (dir.sqrMagnitude < 0.001f) dir = transform.forward;
                dir.Normalize();

                Vector3 impulse = dir * obstacleImpulse + Vector3.up * obstacleUpImpulse;
                otherRb.AddForceAtPosition(impulse, cp.point, ForceMode.Impulse);
            }

            _nextAllowedObstacleImpulseTime = now + obstacleImpactCooldown;
        }
    }


    // ---------------- path utils ----------------

    private void RebuildPath()
    {
        _path.Clear();
        _cumLengths = null;
        _totalLength = 0f;

        List<Vector3> src = trackGenerator.PathPoints;
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

    /// <summary>
    /// Rolling log (or similar) hit — leave the bounce spline and take an impulse.
    /// </summary>
    public void ApplyRollingLogRam(Vector3 planarDirection, float horizontalImpulse, float upImpulse, Vector3 contactPoint)
    {
        if (_forcefieldDetached) return;

        DetachForForcefieldLaunch();

        Vector3 dir = planarDirection;
        dir.y = 0f;
        if (dir.sqrMagnitude < 1e-6f) dir = transform.forward;
        dir.Normalize();

        if (_rb != null)
        {
            _rb.AddForceAtPosition(dir * horizontalImpulse + Vector3.up * upImpulse, contactPoint, ForceMode.Impulse);
            Vector3 torqueAxis = Vector3.Cross(Vector3.up, dir);
            if (torqueAxis.sqrMagnitude > 1e-6f)
                _rb.AddTorque(torqueAxis.normalized * (horizontalImpulse * 0.08f), ForceMode.Impulse);
        }

        if (enableObstacleClashCrashPopup && RacingPopups.IsReady)
            RacingPopups.CrashWorld(contactPoint + Vector3.up * obstacleClashPopupHeight);
    }

    public void DetachForForcefieldLaunch()
    {
        if (_forcefieldDetached) return;
        _forcefieldDetached = true;

        // Kill any tween driving bounce param (if used)
        try { DOTween.Kill(this); } catch { } // safe kill if you used SetId(this) patterns

        StopAllCoroutines();

        // Let physics take over
        var rb = GetComponent<Rigidbody>();
        if (rb != null)
        {
            rb.isKinematic = false;
            rb.useGravity = true;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        }
    }


    private void IgnoreCollisionTemporarily(Collider other, float now)
    {
        if (other == null || _col == null) return;

        int bit = 1 << other.gameObject.layer;
        if ((noPhysicalCollisionMask.value & bit) == 0) return;

        Physics.IgnoreCollision(_col, other);

        _ignoredUntil[other] = now + ignoreCollisionSeconds;
    }


    private void UpdateIgnoredCollisions(float now)
    {
        if (_ignoredUntil.Count == 0) return;

        _toRestore.Clear();

        foreach (var kv in _ignoredUntil)
        {
            if (kv.Key == null || now >= kv.Value)
                _toRestore.Add(kv.Key);
        }

        for (int i = 0; i < _toRestore.Count; i++)
        {
            var c = _toRestore[i];
            if (c != null && _col != null)
                Physics.IgnoreCollision(_col, c, false);

            _ignoredUntil.Remove(c);
        }
    }



    private float GetDistanceAlongTrack(Vector3 worldPos)
    {
        float best = float.MaxValue;
        int bestIdx = 0;
        float bestT = 0f;

        for (int i = 0; i < _path.Count - 1; i++)
        {
            Vector3 a = _path[i];
            Vector3 b = _path[i + 1];
            Vector3 ab = b - a;
            float abSqr = ab.sqrMagnitude;
            if (abSqr < 1e-6f) continue;

            float t = Mathf.Clamp01(Vector3.Dot(worldPos - a, ab) / abSqr);
            Vector3 proj = Vector3.Lerp(a, b, t);
            float d = (worldPos - proj).sqrMagnitude;

            if (d < best)
            {
                best = d;
                bestIdx = i;
                bestT = t;
            }
        }

        float segLen = Vector3.Distance(_path[bestIdx], _path[bestIdx + 1]);
        return _cumLengths[bestIdx] + bestT * segLen;
    }

    /// <summary>Overlap using the collider's actual shape (box/sphere/capsule) so hitbox matches the visual; avoids inflated AABB when rotated.</summary>
    private static Collider[] OverlapColliderShape(Collider col, LayerMask layerMask)
    {
        if (col == null) return null;

        Transform t = col.transform;
        Vector3 scale = t.lossyScale;

        switch (col)
        {
            case BoxCollider box:
            {
                Vector3 center = t.TransformPoint(box.center);
                Vector3 halfExtents = Vector3.Scale(box.size * 0.5f, scale);
                return Physics.OverlapBox(center, halfExtents, t.rotation, layerMask, QueryTriggerInteraction.Collide);
            }
            case SphereCollider sphere:
            {
                Vector3 center = t.TransformPoint(sphere.center);
                float radius = Mathf.Max(scale.x, Mathf.Max(scale.y, scale.z)) * sphere.radius;
                return Physics.OverlapSphere(center, radius, layerMask, QueryTriggerInteraction.Collide);
            }
            case CapsuleCollider cap:
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
            default:
                // MeshCollider or other: fall back to bounds (may still be slightly generous)
                Bounds b = col.bounds;
                return Physics.OverlapBox(b.center, b.extents, t.rotation, layerMask, QueryTriggerInteraction.Collide);
        }
    }

    private void CheckCarHitQuery(float now)
    {
        if (!damagePlayerOnHit) return;
        if (now < _nextAllowedCarHitTime) return;
        if (carDetectMask.value == 0) return;

        // When airborne (bounce arc), use a small sphere so we don't hit the car from the full collider floating in the air.
        Collider[] hits;
        if (_state == State.Bouncing && carHitRadiusWhenAirborne > 0f)
            hits = Physics.OverlapSphere(_rb.position, carHitRadiusWhenAirborne, carDetectMask, QueryTriggerInteraction.Collide);
        else
            hits = OverlapColliderShape(_col, carDetectMask);
        if (hits == null || hits.Length == 0) return;

        CarController car = null;
        Collider carCol = null;

        for (int i = 0; i < hits.Length; i++)
        {
            car = hits[i].GetComponentInParent<CarController>();
            if (car != null) { carCol = hits[i]; break; }
        }
        if (car == null) return;

        // NOW ignore this specific collider instance (prevents the “yeet” impulse)
        IgnoreCollisionTemporarily(carCol, now);


        Vector3 contactPoint = carCol.ClosestPoint(_rb.position);
        Vector3 contactNormal = (_rb.position - contactPoint);
        if (contactNormal.sqrMagnitude < 0.0001f) contactNormal = Vector3.up;
        contactNormal.Normalize();

        Vector3 hitDir = (car.transform.position - _rb.position);
        hitDir.y = 0f;
        if (hitDir.sqrMagnitude < 0.0001f) hitDir = -contactNormal;
        hitDir.Normalize();

        if (useCentralizedCrashSeverity)
        {
            float extra = centralizedSeverityExtraMultiplier > 0f ? centralizedSeverityExtraMultiplier : 1f;
            car.ApplyExternalCrashDamage(
                hitDir,
                crashImpactSpeed,
                contactPoint,
                crashFxSeverity,
                transform,
                _rb,
                contactNormal,
                extra);
        }
        else
        {
            car.ApplyDirectDamageWithCrashFX(
                hitHpDamage,
                hitFuelPercent,
                contactPoint,
                contactNormal,
                hitDir,
                crashImpactSpeed,
                crashFxSeverity);
        }

        // Lose pathing and react with physics from the collision (same as forcefield detach)
        Vector3 awayFromCar = (_rb.position - car.transform.position);
        awayFromCar.y = 0f;
        if (awayFromCar.sqrMagnitude < 0.0001f) awayFromCar = -hitDir;
        awayFromCar.Normalize();

        float impulseMag = carHitDetachImpulse > 0f ? carHitDetachImpulse : _estimatedVel.magnitude;
        if (impulseMag < 1f) impulseMag = 5f; // minimum so it visibly reacts

        DetachForForcefieldLaunch();
        _rb.AddForce(awayFromCar * impulseMag + Vector3.up * 2f, ForceMode.Impulse);

        _nextAllowedCarHitTime = now + hitDamageCooldown;
    }


    private void SampleAlongPath(float dist, out Vector3 pos, out Vector3 fwd)
    {
        dist = Mathf.Clamp(dist, 0f, _totalLength);

        int idx = 0;
        for (int i = 0; i < _cumLengths.Length - 1; i++)
        {
            if (_cumLengths[i + 1] >= dist) { idx = i; break; }
        }

        float segLen = _cumLengths[idx + 1] - _cumLengths[idx];
        float t = (dist - _cumLengths[idx]) / Mathf.Max(segLen, 0.0001f);

        pos = Vector3.Lerp(_path[idx], _path[idx + 1], t);
        fwd = (_path[idx + 1] - _path[idx]).normalized;
    }

    private static void GenerateSmoothedPath(List<Vector3> src, int subdivisionsPerSegment, List<Vector3> outPts)
    {
        outPts.Clear();
        if (src == null || src.Count < 2) return;

        for (int i = 0; i < src.Count - 1; i++)
        {
            Vector3 p0 = src[Mathf.Max(i - 1, 0)];
            Vector3 p1 = src[i];
            Vector3 p2 = src[i + 1];
            Vector3 p3 = src[Mathf.Min(i + 2, src.Count - 1)];

            for (int s = 0; s < subdivisionsPerSegment; s++)
            {
                float t = s / (float)subdivisionsPerSegment;
                outPts.Add(CatmullRom(p0, p1, p2, p3, t));
            }
        }

        outPts.Add(src[src.Count - 1]);
    }

    private static Vector3 CatmullRom(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t)
    {
        float t2 = t * t;
        float t3 = t2 * t;

        return 0.5f * (
            (2f * p1) +
            (-p0 + p2) * t +
            (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 +
            (-p0 + 3f * p1 - 3f * p2 + p3) * t3
        );
    }
}