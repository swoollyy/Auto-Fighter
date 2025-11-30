using System.Collections;
using UnityEngine;

[DisallowMultipleComponent]
public class ShuttleTrackObstacle : MonoBehaviour
{
    [Header("Track Width")]
    [Tooltip("If > 0, use this value instead of ProceduralTrackGenerator.RoadWidth.")]
    [SerializeField] private float overrideRoadWidth = 0f;
    [Tooltip("Extra safety inset from each road edge.")]
    [SerializeField] private float edgeMargin = 0.35f;

    [Header("Obstacle Size (optional)")]
    [Tooltip("If true, tries to estimate half-width from renderers. Otherwise uses manualHalfWidth.")]
    [SerializeField] private bool autoHalfWidthFromRenderer = true;
    [SerializeField] private float manualHalfWidth = 0.5f;

    [Header("Motion")]
    [SerializeField] private float speed = 5f;
    [Tooltip("Start from left bound heading right (if false, starts from right heading left).")]
    [SerializeField] private bool startOnLeft = true;
    [Tooltip("Wait at each end before reversing direction.")]
    [SerializeField] private float waitAtEndSeconds = 0.25f;

    [Header("Track Binding (optional)")]
    [SerializeField] private ProceduralTrackGenerator trackGenerator;

    // runtime
    private Vector3 _originWS;
    private Vector3 _leftWS, _rightWS;
    private Vector3 _targetWS;
    private bool _waiting;
    private float _halfRoad, _selfHalf;

    private bool _convertedToPhysics;

    // NEW: cached Rigidbody reference so we can default to kinematic until conversion
    private Rigidbody _rb;

    // NEW: bottom offset (world-min relative to transform.position.y)
    private float _bottomOffset = 0f;
    private float _safeMargin = 0.02f;

    public void SetGenerator(ProceduralTrackGenerator gen) => trackGenerator = gen;

    private void Awake()
    {
        if (!trackGenerator)
            trackGenerator = FindObjectOfType<ProceduralTrackGenerator>();

        // Ensure a Rigidbody exists and default it to kinematic so spawning/placement
        // doesn't produce weird rotations from physics while the obstacle is scripted.
        _rb = GetComponent<Rigidbody>();
        if (_rb == null)
        {
            _rb = gameObject.AddComponent<Rigidbody>();
        }

        // By default keep it kinematic and non-gravity so the Update-driven motion is stable.
        _rb.isKinematic = true;
        _rb.useGravity = false;
        _rb.collisionDetectionMode = CollisionDetectionMode.Discrete;
        _rb.interpolation = RigidbodyInterpolation.None;
        // Freeze rotations to avoid any runtime tumbling while kinematic
        _rb.constraints = RigidbodyConstraints.FreezeRotation;
    }

    private void Start()
    {
        _originWS = transform.position;

        _halfRoad = DetermineHalfRoadWidth();
        _selfHalf = DetermineSelfHalfWidth();

        // Compute world-space half height of this obstacle (use renderers first, then colliders)
        float halfHeightWorld = 0f;
        var rends = GetComponentsInChildren<Renderer>();
        if (rends != null && rends.Length > 0)
        {
            foreach (var r in rends)
                if (r != null)
                    halfHeightWorld = Mathf.Max(halfHeightWorld, r.bounds.extents.y);
        }
        else
        {
            var cols = GetComponentsInChildren<Collider>();
            foreach (var c in cols)
                if (c != null)
                    halfHeightWorld = Mathf.Max(halfHeightWorld, c.bounds.extents.y);
        }
        // Fallback sensible value if nothing found
        if (halfHeightWorld <= 0f) halfHeightWorld = 0.5f;

        // compute bottom offset (lowest world min relative to transform.position.y)
        _bottomOffset = ComputeBottomOffset();

        // small clearance so object never touches the ground
        float safeMargin = _safeMargin;

        // Prefer projecting the origin onto the road surface first so lateral offsets are computed from a valid surface height.
        LayerMask roadMask = LayerMask.GetMask("RoadSurface");
        // use upOffset = halfHeightWorld + some padding so the raycast originates above the object top
        float upOffsetForCast = halfHeightWorld + 0.5f;
        _originWS = SpawnUtils.ProjectOntoSurface(_originWS, out _, upOffsetForCast, 25f, roadMask);

        // Compute left/right using the track tangent when possible (more robust than using transform.right)
        ComputeEdgeWorldPositions(out _leftWS, out _rightWS);

        // Project the lateral edge points down to surface (prefer Road layer) using the half-height based upOffset
        _leftWS = SpawnUtils.ProjectOntoSurface(_leftWS, out _, upOffsetForCast, 25f, roadMask);
        _rightWS = SpawnUtils.ProjectOntoSurface(_rightWS, out _, upOffsetForCast, 25f, roadMask);

        // Place at start and set initial target
        float usableLeft = -(_halfRoad - edgeMargin - _selfHalf);
        float usableRight = +(_halfRoad - edgeMargin - _selfHalf);

        // Decide initial start/target along the lateral axis derived from the track tangent
        Vector3 lateral = (_rightWS - _leftWS).normalized;
        if (lateral.sqrMagnitude < 1e-6f)
            lateral = transform.right;

        Vector3 startWS = _originWS + lateral * (startOnLeft ? usableLeft : usableRight);
        Vector3 targetWS = _originWS + lateral * (startOnLeft ? usableRight : usableLeft);

        // ensure start/target are projected too (use same mask and upOffset)
        startWS = SpawnUtils.ProjectOntoSurface(startWS, out Vector3 startNormal, upOffsetForCast, 25f, roadMask);
        _targetWS = SpawnUtils.ProjectOntoSurface(targetWS, out Vector3 targetNormal, upOffsetForCast, 25f, roadMask);

        // Choose a Y that keeps the object snug on the terrain at the start point.
        float startDesiredY = startWS.y - _bottomOffset + safeMargin;

        // Set start position and target XZ; Y will be set to same startDesiredY (we move only in XZ)
        transform.position = new Vector3(startWS.x, startDesiredY, startWS.z);
        // target keeps its X/Z, but Y set to startDesiredY to avoid interpolating into ground while moving
        _targetWS = new Vector3(_targetWS.x, startDesiredY, _targetWS.z);
    }

    private void Update()
    {
        if (_waiting) return;

        float step = Mathf.Max(0.01f, speed) * Time.deltaTime;

        // move XZ only
        Vector2 curXZ = new Vector2(transform.position.x, transform.position.z);
        Vector2 targetXZ = new Vector2(_targetWS.x, _targetWS.z);
        Vector2 nextXZ = Vector2.MoveTowards(curXZ, targetXZ, step);

        // sample terrain height under the new XZ and set Y accordingly (keeps it snug)
        float sampleY = SampleTerrainHeightUnderXZ(nextXZ, Mathf.Abs(_bottomOffset) + 1f);
        float newY = sampleY - _bottomOffset + _safeMargin;

        transform.position = new Vector3(nextXZ.x, newY, nextXZ.y);

        if ((new Vector2(transform.position.x, transform.position.z) - targetXZ).sqrMagnitude <= 0.0001f)
        {
            // Flip target (ping-pong)
            _targetWS = (_targetWS == _leftWS) ? _rightWS : _leftWS;
            if (waitAtEndSeconds > 0f)
                StartCoroutine(WaitThenResume(waitAtEndSeconds));
        }
    }

    private IEnumerator WaitThenResume(float seconds)
    {
        _waiting = true;
        yield return new WaitForSeconds(seconds);
        _waiting = false;
    }

    private float DetermineHalfRoadWidth()
    {
        float roadWidth = (overrideRoadWidth > 0f)
            ? overrideRoadWidth
            : (trackGenerator ? trackGenerator.RoadWidth : 8f); // fallback
        return Mathf.Max(0.1f, roadWidth) * 0.5f;
    }

    private float DetermineSelfHalfWidth()
    {
        if (!autoHalfWidthFromRenderer)
            return Mathf.Max(0f, manualHalfWidth);

        float approx = 0f;
        var rends = GetComponentsInChildren<Renderer>();
        if (rends != null && rends.Length > 0)
        {
            Vector3 r = transform.right;
            Bounds wb = rends[0].bounds;
            for (int i = 1; i < rends.Length; i++) wb.Encapsulate(rends[i].bounds);
            // compute width along local right
            float widthAlongRight = Vector3.Project(wb.size, r).magnitude;
            approx = widthAlongRight * 0.5f;
        }
        return Mathf.Max(0f, approx);
    }

    public void ConvertToPhysicsOnHit()
    {
        if (_convertedToPhysics) return;
        _convertedToPhysics = true;

        // Stop shuttling
        enabled = false;
        _waiting = false;

        // Add / configure Rigidbody so normal physics applies
        var rb = GetComponent<Rigidbody>();
        if (!rb)
            rb = gameObject.AddComponent<Rigidbody>();

        // We now allow physics to control motion/rotation
        rb.isKinematic = false;
        rb.useGravity = true;
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        // Allow rotations provided by physics (clear freeze rotation)
        rb.constraints = RigidbodyConstraints.None;

        // Wake up
        rb.WakeUp();
        Physics.SyncTransforms();
    }

    // Helper that checks if the incoming collider belongs to a "Projectile" object.
    // Returns true only when a valid Projectile layer exists and the collider (or its root/attachedRigidbody root)
    // is on that layer. This avoids reacting to road/terrain collisions at spawn.
    private bool IsProjectileCollider(Collider other)
    {
        if (other == null) return false;

        int projectileLayer = LayerMask.NameToLayer("Projectile");
        if (projectileLayer == -1)
        {
            // If the project doesn't define a "Projectile" layer, be conservative and do NOT convert automatically.
            // This prevents accidental conversion on road collision. If you intentionally removed the layer,
            // consider updating logic here.
            return false;
        }

        // Check attached rigidbody root first (common for pooled/projectile prefabs)
        Transform root = other.attachedRigidbody ? other.attachedRigidbody.transform : other.transform.root;
        if (root != null && root.gameObject.layer == projectileLayer)
            return true;

        // Fallback: check collider's own layer
        if (other.gameObject.layer == projectileLayer)
            return true;

        return false;
    }

    private void OnCollisionEnter(Collision collision)
    {
        // Only convert to physics if we collided with an object on the Projectile layer.
        if (IsProjectileCollider(collision.collider))
            ConvertToPhysicsOnHit();
    }

    private void OnTriggerEnter(Collider other)
    {
        // Only convert to physics if the trigger source is a Projectile.
        if (IsProjectileCollider(other))
            ConvertToPhysicsOnHit();
    }

    /// <summary>
    /// If the collider's root has a Rigidbody that is kinematic, make it non-kinematic and configure it for physics.
    /// If no Rigidbody exists, add one so the collided object becomes movable by physics.
    /// </summary>
    private void TryMakeOtherDynamic(Collider other)
    {
        if (other == null) return;

        // Ignore triggers
        if (other.isTrigger) return;

        // Find root transform to operate on the whole obstacle
        Transform root = other.attachedRigidbody ? other.attachedRigidbody.transform : other.transform.root;
        if (!root) root = other.transform;

        // Don't try to convert ourselves
        if (root == transform) return;

        // Only handle objects on the "Projectile" layer to avoid converting terrain/level geometry
        int projectileLayer = LayerMask.NameToLayer("Projectile");
        if (projectileLayer == -1) return;
        bool otherIsProjectile = root.gameObject.layer == projectileLayer || other.gameObject.layer == projectileLayer;
        if (!otherIsProjectile) return;

        Rigidbody rb = root.GetComponent<Rigidbody>() ?? root.GetComponentInChildren<Rigidbody>();
        if (rb == null)
        {
            // Add a Rigidbody so the obstacle can be affected by physics
            rb = root.gameObject.AddComponent<Rigidbody>();
            rb.mass = Mathf.Max(0.1f, 10f);
        }

        // If it was kinematic, enable dynamic physics
        if (rb.isKinematic)
            rb.isKinematic = false;

        rb.useGravity = true;
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        rb.WakeUp();
        Physics.SyncTransforms();
    }

    private void ComputeEdgeWorldPositions(out Vector3 leftWS, out Vector3 rightWS)
    {
        // Attempt to compute lateral axis based on the nearest track tangent when we have a track generator.
        // This is more robust than using this object's transform.right which may not align with the track.
        Vector3 lateral = transform.right; // fallback

        if (trackGenerator != null && trackGenerator.PathPoints != null && trackGenerator.PathPoints.Count >= 2)
        {
            // Find closest segment to origin and use its tangent
            float bestDist = float.MaxValue;
            int bestIndex = 0;
            for (int i = 0; i < trackGenerator.PathPoints.Count - 1; i++)
            {
                Vector3 a = trackGenerator.PathPoints[i];
                Vector3 b = trackGenerator.PathPoints[i + 1];
                Vector3 proj = ClosestPointOnSegment(_originWS, a, b);
                float d = (proj - _originWS).sqrMagnitude;
                if (d < bestDist)
                {
                    bestDist = d;
                    bestIndex = i;
                }
            }

            Vector3 aa = trackGenerator.PathPoints[bestIndex];
            Vector3 bb = trackGenerator.PathPoints[Mathf.Min(bestIndex + 1, trackGenerator.PathPoints.Count - 1)];
            Vector3 forward = (bb - aa).normalized;
            if (forward.sqrMagnitude > 1e-6f)
                lateral = Vector3.Cross(Vector3.up, forward).normalized;
        }

        float usableLeft = -(_halfRoad - edgeMargin - _selfHalf);
        float usableRight = +(_halfRoad - edgeMargin - _selfHalf);
        leftWS = _originWS + lateral * usableLeft;
        rightWS = _originWS + lateral * usableRight;
    }

    private static Vector3 ClosestPointOnSegment(Vector3 p, Vector3 a, Vector3 b)
    {
        Vector3 ab = b - a;
        float abSqr = ab.sqrMagnitude;
        if (abSqr < 1e-6f) return a;
        float t = Vector3.Dot(p - a, ab) / abSqr;
        t = Mathf.Clamp01(t);
        return a + ab * t;
    }

    // sample surface Y under a given XZ (fallback to currentY if none)
    private float SampleTerrainHeightUnderXZ(Vector2 xz, float upOffset = 2f)
    {
        Vector3 probe = new Vector3(xz.x, transform.position.y + upOffset, xz.y);
        Vector3 normal;
        Vector3 projected = SpawnUtils.ProjectOntoSurface(probe, out normal, upOffset, 50f, LayerMask.GetMask("RoadSurface"));
        if (projected == probe) // ProjectOntoSurface returns probe if nothing found
        {
            // fallback try any collider
            projected = SpawnUtils.ProjectOntoSurface(probe, out normal, upOffset, 50f, null);
        }
        return projected.y;
    }

    // compute bottom offset like in CrossTrackObstacle
    private float ComputeBottomOffset()
    {
        float worldMinY = float.MaxValue;
        bool found = false;

        var rends = GetComponentsInChildren<Renderer>();
        if (rends != null && rends.Length > 0)
        {
            foreach (var r in rends)
            {
                if (r == null) continue;
                try
                {
                    worldMinY = Mathf.Min(worldMinY, r.bounds.min.y);
                    found = true;
                }
                catch { }
            }
        }

        if (!found)
        {
            var cols = GetComponentsInChildren<Collider>();
            foreach (var c in cols)
            {
                if (c == null) continue;
                try
                {
                    worldMinY = Mathf.Min(worldMinY, c.bounds.min.y);
                    found = true;
                }
                catch { }
            }
        }

        if (!found)
            return 0f;

        return worldMinY - transform.position.y;
    }

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        if (Application.isPlaying)
        {
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(_leftWS + Vector3.up * 0.05f, 0.12f);
            Gizmos.DrawWireSphere(_rightWS + Vector3.up * 0.05f, 0.12f);
            Gizmos.DrawLine(_leftWS + Vector3.up * 0.05f, _rightWS + Vector3.up * 0.05f);
        }
        else
        {
            var gen = trackGenerator ? trackGenerator : FindObjectOfType<ProceduralTrackGenerator>();
            float halfRoad = (overrideRoadWidth > 0f ? overrideRoadWidth : (gen ? gen.RoadWidth : 8f)) * 0.5f;
            float selfHalf = autoHalfWidthFromRenderer ? 0.3f : Mathf.Max(0f, manualHalfWidth);
            float usableLeft = -(halfRoad - edgeMargin - selfHalf);
            float usableRight = +(halfRoad - edgeMargin - selfHalf);

            Vector3 origin = transform.position;
            Vector3 right = transform.right;
            Vector3 l = origin + right * usableLeft;
            Vector3 r = origin + right * usableRight;

            Gizmos.color = Color.yellow;
            Gizmos.DrawLine(l + Vector3.up * 0.05f, r + Vector3.up * 0.05f);
        }
    }
#endif
}