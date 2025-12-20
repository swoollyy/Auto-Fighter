using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Obstacle that ONLY moves via repeated "bounce bursts" backwards along the track.
/// Between bounces, it is stationary (but still glued to the track + lateral offset).
///
/// Key upgrades:
/// - Uses a kinematic Rigidbody + MovePosition/MoveRotation (so it collides).
/// - Lands on ROAD OR OTHER OBSTACLES via a configurable groundMask.
/// - Bounce is a real arc (no "clamp-to-ground every frame" killing the hop).
/// - Optional forward collision probing so it won't tunnel into solid obstacles.
/// - Player hit applies adjustable HP + fuel% damage (via CarController method patch).
/// </summary>
[RequireComponent(typeof(Collider))]
[RequireComponent(typeof(Rigidbody))]
public class TrackObstacleBounceBack : MonoBehaviour
{
    [Header("Track Reference")]
    [SerializeField] private ProceduralTrackGenerator trackGenerator;

    [Header("Path Sampling (match TrackObstacleSpawner)")]
    [SerializeField] private bool useSmoothing = true;
    [SerializeField, Min(1)] private int smoothingSubdivisionsPerSegment = 6;

    [Header("Bounce Movement")]
    [Tooltip("How far (meters) each bounce moves backward along the track.")]
    [SerializeField, Min(0.1f)] private float bounceStepDistance = 18f;

    [Tooltip("Seconds to wait between bounces. Stationary during this time.")]
    [SerializeField, Min(0f)] private float bounceCooldown = 0.65f;

    [Tooltip("If true, cooldown uses real time (ignores Time.timeScale).")]
    [SerializeField] private bool cooldownUsesUnscaledTime = true;

    [Header("Bounce Arc")]
    [Tooltip("Peak hop height during a bounce (meters).")]
    [SerializeField, Min(0f)] private float bounceJumpHeight = 1.0f;

    [Tooltip("Seconds per bounce (arc duration).")]
    [SerializeField, Min(0.05f)] private float bounceDuration = 0.25f;

    [Header("Road Clamp")]
    [SerializeField] private bool clampToRoadWidth = true;
    [Range(0.1f, 1f)]
    [SerializeField] private float lateralClampFraction = 0.95f;

    [Header("Grounding / Landing")]
    [Tooltip("What counts as 'ground' for landing. INCLUDE road + obstacle layers you want it to sit on.")]
    [SerializeField] private LayerMask groundMask;

    [SerializeField] private float raycastStartHeight = 6f;
    [SerializeField] private float raycastDownDistance = 25f;

    [Tooltip("Extra gap above the hit surface normal so it doesn't z-fight / stick.")]
    [SerializeField] private float heightOffset = 0.02f;

    [Tooltip("If true, auto-compute bottom offset from collider bounds (recommended).")]
    [SerializeField] private bool autoBottomOffsetFromCollider = true;

    [Tooltip("Manual bottom offset from pivot to bottom of collider (meters). Used if autoBottomOffsetFromCollider=false.")]
    [SerializeField] private float pivotBottomOffset = 0.0f;

    [Tooltip("If true, use Vector3.up for height offset. If false, use hit.normal.")]
    [SerializeField] private bool useUpForHeight = true;

    [Tooltip("Face opposite the track forward (since we travel backward).")]
    [SerializeField] private bool faceDirectionOfTravel = true;

    [Header("Collision While Moving")]
    [Tooltip("If true, probe for solid obstacles during a bounce so we don't tunnel through them.")]
    [SerializeField] private bool enableForwardProbe = true;

    [Tooltip("Layers considered solid blockers during bounce motion (usually obstacles).")]
    [SerializeField] private LayerMask blockerMask;

    [Tooltip("Small skin distance for the probe cast.")]
    [SerializeField, Min(0f)] private float probeSkin = 0.03f;

    [Header("Damage On Player Hit")]
    [SerializeField] private bool damagePlayerOnHit = true;

    [Tooltip("Flat HP damage applied on hit.")]
    [SerializeField, Min(0f)] private float hitHpDamage = 15f;

    [Tooltip("Fuel damage as a FRACTION of max fuel. 0.10 = 10% of max fuel.")]
    [SerializeField, Range(0f, 1f)] private float hitFuelPercent = 0.10f;

    [Tooltip("Cooldown between player damage applications (prevents multi-hit spam).")]
    [SerializeField, Min(0f)] private float hitDamageCooldown = 0.5f;

    // --- Internals ---
    private readonly List<Vector3> _path = new();
    private float[] _cumLengths;
    private float _totalLength;

    private float _dist;              // current distance along track
    private float _lateralOffset;     // signed offset from centerline along right vector at spawn
    private bool _initialized;

    // Bounce state
    private bool _isBouncing;
    private float _nextBounceTime;

    private float _bounceStartTime;
    private float _bounceEndTime;
    private float _bounceStartDist;
    private float _bounceEndDist;

    // Physics refs
    private Rigidbody _rb;
    private Collider _col;

    // Cached bottom offset
    private float _bottomOffset;

    // Player hit gating
    private float _nextAllowedHitTime;

    private void Awake()
    {
        if (!trackGenerator) trackGenerator = FindFirstObjectByType<ProceduralTrackGenerator>();

        _rb = GetComponent<Rigidbody>();
        _col = GetComponent<Collider>();

        // Must be kinematic because we control position, but still want collisions.
        _rb.isKinematic = true;
        _rb.interpolation = RigidbodyInterpolation.Interpolate;
        _rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;

        if (_col != null)
            _col.isTrigger = false; // required for physical contact + landing on stuff
    }

    private void OnEnable()
    {
        _initialized = false;
        _isBouncing = false;
        _nextBounceTime = 0f;

        _bounceStartTime = 0f;
        _bounceEndTime = 0f;

        _nextAllowedHitTime = 0f;
    }

    private void Start()
    {
        InitializeIfNeeded();
    }

    private void FixedUpdate()
    {
        if (!InitializeIfNeeded())
            return;

        float now = cooldownUsesUnscaledTime ? Time.unscaledTime : Time.time;

        // Start new bounce if idle and cooldown passed
        if (!_isBouncing && now >= _nextBounceTime)
        {
            StartBounce(now);
        }

        // Compute target dist along track (during bounce it's lerped)
        float distNow = _dist;

        if (_isBouncing)
        {
            float tNow = cooldownUsesUnscaledTime ? Time.unscaledTime : Time.time;
            float t01 = Mathf.InverseLerp(_bounceStartTime, _bounceEndTime, tNow);

            distNow = Mathf.Lerp(_bounceStartDist, _bounceEndDist, t01);

            // End bounce at completion or clamp at start of track
            if (t01 >= 1f || distNow <= 0f)
            {
                distNow = Mathf.Max(0f, distNow);
                _dist = distNow;
                EndBounce(tNow);
            }
            else
            {
                _dist = distNow;
            }
        }

        // Sample track for center + forward
        SampleAlongPath(_dist, out Vector3 center, out Vector3 forward);

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

        // Base (track-glued) horizontal position
        Vector3 basePos = center + right * lateral;

        // Apply bounce arc (visual hop)
        float arcY = 0f;
        if (_isBouncing && bounceJumpHeight > 0f)
        {
            float tNow = cooldownUsesUnscaledTime ? Time.unscaledTime : Time.time;
            float t01 = Mathf.InverseLerp(_bounceStartTime, _bounceEndTime, tNow);
            arcY = Mathf.Sin(t01 * Mathf.PI) * bounceJumpHeight;
        }

        // We land on whatever is under us (road or obstacles). During bounce we still "hover"
        // above the landing surface by arcY, but we do NOT snap to the road each frame
        // in a way that would delete the hop.
        Vector3 desired = basePos;

        // Find landing surface directly under basePos (not including arc)
        Vector3 origin = basePos + Vector3.up * raycastStartHeight;
        float maxRay = raycastStartHeight + raycastDownDistance;

        Vector3 upAxis = Vector3.up;
        Vector3 surfaceNormal = Vector3.up;

        if (Physics.Raycast(origin, Vector3.down, out RaycastHit hit, maxRay, groundMask, QueryTriggerInteraction.Ignore))
        {
            surfaceNormal = hit.normal;
            upAxis = useUpForHeight ? Vector3.up : surfaceNormal;

            // Place feet on the surface, then add arc hop on top.
            desired = hit.point + upAxis * (heightOffset + _bottomOffset);
            desired += Vector3.up * arcY;
        }
        else
        {
            // Fallback: float at base height + arc
            desired = basePos + Vector3.up * arcY;
        }

        // Optional: prevent tunneling into blockers while bouncing
        if (enableForwardProbe && _isBouncing && blockerMask.value != 0)
        {
            Vector3 from = _rb.position;
            Vector3 to = desired;

            Vector3 delta = to - from;
            float dist = delta.magnitude;

            if (dist > 0.0001f)
            {
                Vector3 dir = delta / dist;

                // Use collider bounds as a simple probe radius (cheap and stable)
                float radius = 0.25f;
                if (_col != null)
                {
                    var b = _col.bounds;
                    radius = Mathf.Max(0.05f, Mathf.Min(b.extents.x, b.extents.z));
                }

                // Spherecast from current to desired to detect blockers
                if (Physics.SphereCast(from, radius, dir, out RaycastHit blockHit, dist + probeSkin, blockerMask, QueryTriggerInteraction.Ignore))
                {
                    // Stop at hit point (minus skin), keep our path distance, and end bounce early.
                    float stopDist = Mathf.Max(0f, blockHit.distance - probeSkin);
                    Vector3 stopped = from + dir * stopDist;

                    desired = stopped;
                    float tNow = cooldownUsesUnscaledTime ? Time.unscaledTime : Time.time;
                    EndBounce(tNow);
                }
            }
        }

        // Rotation
        Quaternion rot;
        if (faceDirectionOfTravel)
        {
            Vector3 travelDir = -flatForward;
            if (travelDir.sqrMagnitude < 0.0001f) travelDir = transform.forward;
            rot = Quaternion.LookRotation(travelDir, surfaceNormal);
        }
        else
        {
            rot = Quaternion.LookRotation(flatForward, surfaceNormal);
        }

        _rb.MovePosition(desired);
        _rb.MoveRotation(rot);
    }

    private void StartBounce(float now)
    {
        _isBouncing = true;

        float tNow = cooldownUsesUnscaledTime ? Time.unscaledTime : Time.time;
        _bounceStartTime = tNow;
        _bounceEndTime = tNow + Mathf.Max(0.05f, bounceDuration);

        _bounceStartDist = _dist;
        _bounceEndDist = Mathf.Max(0f, _dist - bounceStepDistance);
    }

    private void EndBounce(float now)
    {
        _isBouncing = false;
        _nextBounceTime = now + bounceCooldown;
    }

    private void ForceBounceNow()
    {
        float now = cooldownUsesUnscaledTime ? Time.unscaledTime : Time.time;

        _bounceEndTime = now;    // stop the current arc immediately
        _isBouncing = false;
        _nextBounceTime = now;
    }

    private bool InitializeIfNeeded()
    {


        if (_initialized) return true;

        if (!trackGenerator) trackGenerator = FindFirstObjectByType<ProceduralTrackGenerator>();
        if (!trackGenerator) return false;

        RebuildPath();
        if (_path.Count < 2 || _totalLength <= 0.01f)
            return false;

        // Compute bottom offset once
        _bottomOffset = Mathf.Max(0f, pivotBottomOffset);
        if (autoBottomOffsetFromCollider && _col != null)
        {
            // Bounds extents are world-space. We want "how far from pivot to bottom".
            // Using extents.y is a good approximation if pivot is near center.
            _bottomOffset = Mathf.Max(0f, _col.bounds.extents.y);
        }

        // Find current distance along track + compute lateral offset at spawn
        float spawnDist = GetDistanceAlongTrack(transform.position);
        _dist = spawnDist;

        SampleAlongPath(spawnDist, out Vector3 center, out Vector3 forward);

        Vector3 flatForward = forward;
        flatForward.y = 0f;
        if (flatForward.sqrMagnitude < 0.0001f) flatForward = Vector3.forward;
        flatForward.Normalize();

        Vector3 right = Vector3.Cross(Vector3.up, flatForward).normalized;
        Vector3 delta = transform.position - center;
        _lateralOffset = Vector3.Dot(delta, right);

        float now = cooldownUsesUnscaledTime ? Time.unscaledTime : Time.time;
        _nextBounceTime = now; // bounce immediately
        _isBouncing = false;

        _initialized = true;
        return true;
    }

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

    // Match spawner smoothing (same helper you already had in this script)
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

    private void OnCollisionEnter(Collision collision)
    {
        if (!damagePlayerOnHit) return;

        if (Time.time < _nextAllowedHitTime)
            return;

        var car = collision.collider.GetComponentInParent<CarController>();
        if (car == null) return;

        car.ApplyDirectDamage(hitHpDamage, hitFuelPercent);

        ForceBounceNow();

        _nextAllowedHitTime = Time.time + hitDamageCooldown;
    }
}
