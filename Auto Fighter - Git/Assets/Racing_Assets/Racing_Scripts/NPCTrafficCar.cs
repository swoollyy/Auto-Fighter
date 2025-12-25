using Pathfinding;
using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// NPC traffic car that follows the procedural track using A* Pathfinding.
/// - We set a destination ahead on the track
/// - RichAI/AIPath handles movement and avoidance
/// - We handle grounding and rotation
/// - NavMeshCut on the prefab makes other agents avoid this car
/// </summary>
[RequireComponent(typeof(Rigidbody))]
[RequireComponent(typeof(Collider))]
[RequireComponent(typeof(Seeker))]
public class NPCTrafficCar : MonoBehaviour
{
    // ============================================
    // TRACK REFERENCE
    // ============================================
    [Header("Track Reference")]
    [SerializeField] private ProceduralTrackGenerator trackGenerator;

    [Header("Path Sampling")]
    [SerializeField] private bool useSmoothing = true;
    [SerializeField, Min(1)] private int smoothingSubdivisionsPerSegment = 6;

    // ============================================
    // A* PATHFINDING
    // ============================================
    [Header("A* Destination")]
    [Tooltip("How far ahead on the track to set the destination.")]
    [SerializeField] private float destinationLookAhead = 25f;

    [Tooltip("How often to update the destination (seconds).")]
    [SerializeField] private float destinationUpdateInterval = 0.15f;

    [Tooltip("Minimum distance the destination must move before updating A*.")]
    [SerializeField] private float destinationMoveThreshold = 2f;

    // ============================================
    // SPEED
    // ============================================
    [Header("Speed")]
    [SerializeField] private float speed = 12f;
    [SerializeField] private bool randomizeSpeed = true;
    [SerializeField] private Vector2 speedRange = new Vector2(8f, 18f);

    // ============================================
    // LANE POSITION
    // ============================================
    [Header("Lane Position")]
    [Tooltip("Fraction of half road width for lateral offset.")]
    [SerializeField, Range(0f, 1f)] private float lateralFraction = 0.7f;
    [SerializeField] private float edgeMargin = 0.5f;
    [SerializeField] private bool randomizeLane = true;

    // ============================================
    // GROUNDING
    // ============================================
    [Header("Grounding")]
    [SerializeField] private LayerMask roadLayer;
    [SerializeField] private float raycastStartHeight = 5f;
    [SerializeField] private float raycastDownDistance = 15f;
    [SerializeField] private float groundClearance = 0.05f;

    // ============================================
    // ROTATION
    // ============================================
    [Header("Rotation")]
    [SerializeField] private bool rotateToVelocity = true;
    [SerializeField] private bool alignToGround = false;
    [SerializeField] private float rotationSpeed = 5f;

    [Tooltip("Smooth out velocity direction changes to prevent frantic rotation.")]
    [SerializeField] private float velocitySmoothTime = 0.2f;

    [Tooltip("Minimum speed to update rotation (prevents spinning when nearly stopped).")]
    [SerializeField] private float minSpeedForRotation = 1f;

    [Tooltip("Blend between track direction and movement direction (0=track, 1=velocity).")]
    [SerializeField, Range(0f, 1f)] private float trackVelocityBlend = 0.3f;

    // ============================================
    // COLLISION / CRASH
    // ============================================
    [Header("Collision Detection")]
    [SerializeField] private LayerMask crashLayers;
    [SerializeField] private bool ignoreRoadAndTerrain = true;
    [SerializeField] private bool enableOverlapDetection = true;
    [SerializeField] private float overlapCheckInterval = 0.1f;
    [SerializeField] private float overlapRadius = 1f;

    [Header("Crash Physics")]
    [SerializeField] private float minTransferVelocity = 2f;
    [SerializeField] private float crashBounceUp = 4f;
    [SerializeField] private float crashBounceBack = 6f;
    [SerializeField] private Vector2 crashSpinRange = new Vector2(180f, 400f);

    [Header("Crash SFX")]
    [SerializeField] private AudioClip crashClip;
    [SerializeField, Range(0f, 1f)] private float crashVolume = 0.8f;

    [Header("Crash VFX")]
    [SerializeField] private GameObject crashVFXPrefab;
    [SerializeField] private float crashVFXLifetime = 3f;

    [Header("Self Destruction")]
    [SerializeField] private bool destroyAfterCrash = true;
    [SerializeField] private float destroyDelay = 5f;

    [Header("Engine Audio")]
    [SerializeField] private AudioClip engineClip;
    [SerializeField, Range(0f, 1f)] private float engineVolume = 0.4f;
    [SerializeField] private float enginePitchMin = 0.7f;
    [SerializeField] private float enginePitchMax = 1.3f;

    [Header("Debug")]
    [SerializeField] private bool verboseDebug = false;
    [SerializeField] private bool drawDestinationGizmo = true;

    // ============================================
    // INTERNALS - Track Path
    // ============================================
    private readonly List<Vector3> _path = new();
    private float[] _cumLengths;
    private float _totalLength;

    private float _dist;                    // Current distance along path
    private float _lateralOffset;           // Side offset from center
    private float _pivotToBottom;           // Distance from pivot to collider bottom

    // ============================================
    // INTERNALS - Components
    // ============================================
    private Rigidbody _rb;
    private Collider _col;
    private AudioSource _engineSource;
    private Seeker _seeker;
    private IAstarAI _ai;                   // RichAI or AIPath
    private RichAI _richAI;
    private AIBase _aiBase;

    // ============================================
    // INTERNALS - State
    // ============================================
    private bool _initialized;
    private bool _crashed;
    private float _overlapTimer;
    private float _destTimer;

    private Vector3 _prevPosition;
    private Vector3 _lastVelocity;
    private Vector3 _smoothedVelocity;
    private Vector3 _smoothedForward;
    private Vector3 _groundNormal = Vector3.up;
    private Vector3 _lastDestination;
    private bool _hasSetDestination;

    // ============================================
    // UNITY LIFECYCLE
    // ============================================

    private void Awake()
    {
        _rb = GetComponent<Rigidbody>();
        _col = GetComponent<Collider>();
        _seeker = GetComponent<Seeker>();
        _ai = GetComponent<IAstarAI>();
        _richAI = GetComponent<RichAI>();
        _aiBase = GetComponent<AIBase>();

        // Configure rigidbody
        if (_rb != null)
        {
            _rb.isKinematic = true;
            _rb.useGravity = false;
            _rb.interpolation = RigidbodyInterpolation.Interpolate;
        }
    }

    private void OnEnable()
    {
        _initialized = false;
        _crashed = false;
        _overlapTimer = 0f;
        _destTimer = 0f;
        _prevPosition = transform.position;
        _lastVelocity = Vector3.zero;
        _smoothedVelocity = Vector3.zero;      // ADD THIS
        _smoothedForward = transform.forward;   // ADD THIS
        _hasSetDestination = false;
    }

    private void Start()
    {
        InitializeIfNeeded();
        SetupEngineAudio();
    }

    private void Update()
    {
        if (_crashed) return;
        if (!InitializeIfNeeded()) return;

        float dt = Time.deltaTime;
        if (dt <= 0f) return;

        // Update our track distance based on current position
        _dist = GetDistanceAlongPath(transform.position);

        // Update destination for A*
        _destTimer -= dt;
        if (_destTimer <= 0f)
        {
            float dynInterval = Mathf.Lerp(0.06f, destinationUpdateInterval, Mathf.InverseLerp(4f, 12f, speed));
            _destTimer = dynInterval;
            UpdateAStarDestination();
        }



        SyncSpeedIfNeeded();

        // Update engine audio
        UpdateEngineAudio();

        // Overlap detection for crash
        if (enableOverlapDetection)
        {
            _overlapTimer -= dt;
            if (_overlapTimer <= 0f)
            {
                _overlapTimer = overlapCheckInterval;
                CheckOverlapAndCrash();
            }
        }
    }

    private float _lastAppliedSpeed = -1f;

    private void SyncSpeedIfNeeded()
    {
        if (Mathf.Abs(_lastAppliedSpeed - speed) < 0.01f)
            return;

        _lastAppliedSpeed = speed;

        if (_ai != null) _ai.maxSpeed = speed;
        if (_richAI != null) _richAI.maxSpeed = speed;
    }

    private void LateUpdate()
    {
        if (_crashed) return;
        if (!_initialized) return;

        float dt = Time.deltaTime;
        if (dt <= 0f) return;

        // A* has moved us - now do ground snap and rotation
        Vector3 pos = transform.position;

        // Ground snap (Y only - don't fight A* in XZ)
        if (RaycastGround(pos, out RaycastHit hit))
        {
            pos.y = hit.point.y + _pivotToBottom + groundClearance;
            transform.position = pos;
            _groundNormal = hit.normal;
        }

        // Compute velocity for crash physics
        _lastVelocity = (transform.position - _prevPosition) / dt;
        _prevPosition = transform.position;

        // Smooth the velocity to prevent frantic direction changes
        if (_smoothedVelocity.sqrMagnitude < 0.01f)
            _smoothedVelocity = _lastVelocity;
        else
            _smoothedVelocity = Vector3.Lerp(_smoothedVelocity, _lastVelocity, dt / Mathf.Max(0.01f, velocitySmoothTime));

        // Rotate to face movement direction
        if (rotateToVelocity)
        {
            Vector3 moveDir = _smoothedVelocity;
            moveDir.y = 0f;

            float currentSpeed = moveDir.magnitude;

            // Only rotate if moving fast enough
            if (currentSpeed > minSpeedForRotation)
            {
                // Get track forward direction for blending
                SampleAlongPath(_dist, out _, out Vector3 trackFwd);
                Vector3 flatTrackFwd = new Vector3(trackFwd.x, 0f, trackFwd.z).normalized;

                // Blend between track direction and actual movement direction
                Vector3 blendedDir = Vector3.Slerp(flatTrackFwd, moveDir.normalized, trackVelocityBlend);

                Vector3 up = alignToGround ? _groundNormal : Vector3.up;
                Vector3 fwdOnPlane = Vector3.ProjectOnPlane(blendedDir, up);

                if (fwdOnPlane.sqrMagnitude > 0.0001f)
                {
                    Quaternion targetRot = Quaternion.LookRotation(fwdOnPlane.normalized, up);

                    // Exponential smoothing for rotation (more stable than linear)
                    float smoothFactor = 1f - Mathf.Exp(-rotationSpeed * dt);
                    transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, smoothFactor);
                }
            }
        }
    }

    // ============================================
    // A* DESTINATION
    // ============================================

    private void UpdateAStarDestination()
    {
        if (_ai == null) return;

        // Calculate destination ahead on track
        float targetDist = Mathf.Min(_dist + destinationLookAhead, _totalLength);
        SampleAlongPath(targetDist, out Vector3 trackPos, out Vector3 trackFwd);

        // Apply lane offset to destination
        Vector3 flatFwd = new Vector3(trackFwd.x, 0f, trackFwd.z);
        if (flatFwd.sqrMagnitude < 0.0001f) flatFwd = Vector3.forward;
        flatFwd.Normalize();

        Vector3 right = Vector3.Cross(Vector3.up, flatFwd).normalized;
        float halfWidth = GetHalfRoadWidth();
        float maxLateral = Mathf.Max(0f, halfWidth * lateralFraction - edgeMargin);

        Vector3 destination = trackPos + right * Mathf.Clamp(_lateralOffset, -maxLateral, maxLateral);

        // Dynamic threshold: at low speeds we must repath more often or it feels "stale/jittery".
        float dynThreshold = Mathf.Clamp(speed * destinationUpdateInterval * 0.6f, 0.15f, destinationMoveThreshold);
        // Example: speed=4, interval=0.15 => 0.36m threshold (instead of 2m)

        float dynThresholdSqr = dynThreshold * dynThreshold;

        if (!_hasSetDestination || (destination - _lastDestination).sqrMagnitude > dynThresholdSqr)
        {
            _lastDestination = destination;
            _hasSetDestination = true;

            _ai.destination = destination;
            _ai.SearchPath();


            if (verboseDebug)
                Debug.Log($"[NPCTrafficCar] Set destination: {destination}, dist={_dist:F1}m");
        }

    }

    // ============================================
    // INITIALIZATION
    // ============================================

    private bool InitializeIfNeeded()
    {
        if (_initialized) return true;

        // Find track generator
        if (trackGenerator == null)
            trackGenerator = FindFirstObjectByType<ProceduralTrackGenerator>();

        if (trackGenerator == null)
        {
            if (verboseDebug) Debug.LogWarning("[NPCTrafficCar] No track generator found!");
            return false;
        }

        // Build path
        var srcPoints = trackGenerator.PathPoints;
        if (srcPoints == null || srcPoints.Count < 2) return false;

        RebuildPath(srcPoints);
        if (_path.Count < 2 || _totalLength < 1f) return false;

        // Randomize speed
        if (randomizeSpeed)
        {
            speed = UnityEngine.Random.Range(speedRange.x, speedRange.y);
        }

        if (_ai != null)
        {
            _ai.maxSpeed = speed;
            _ai.canSearch = true;
            _ai.canMove = true;
        }

        if (_richAI != null)
        {
            _richAI.maxSpeed = speed;
            _richAI.acceleration = Mathf.Max(30f, speed * 6f);
            _richAI.slowdownTime = 0f;
            _richAI.endReachedDistance = 3f;
            _richAI.whenCloseToDestination = CloseToDestinationMode.ContinueToExactDestination;
            _richAI.rotationSpeed = 0f;
            _richAI.enableRotation = false;

            ForceAstarFixedUpdate(_richAI);   // ✅ call on RichAI
        }
        else if (_aiBase != null)
        {
            ForceAstarFixedUpdate(_aiBase);   // ✅ fallback for AIPath, etc.
        }

        // Compute pivot to bottom
        ComputePivotToBottom();

        // Find starting distance on track
        _dist = GetDistanceAlongPath(transform.position);

        // Set lateral offset
        float halfWidth = GetHalfRoadWidth();
        float usable = Mathf.Max(0f, halfWidth * lateralFraction - edgeMargin);

        if (randomizeLane)
        {
            _lateralOffset = UnityEngine.Random.Range(-usable, usable);
        }
        else
        {
            SampleAlongPath(_dist, out Vector3 center, out Vector3 fwd);
            Vector3 flatFwd = new Vector3(fwd.x, 0f, fwd.z).normalized;
            Vector3 right = Vector3.Cross(Vector3.up, flatFwd).normalized;
            _lateralOffset = Vector3.Dot(transform.position - center, right);
            _lateralOffset = Mathf.Clamp(_lateralOffset, -usable, usable);
        }

        _prevPosition = transform.position;
        _initialized = true;

        // Set initial destination
        UpdateAStarDestination();

        if (verboseDebug)
            Debug.Log($"[NPCTrafficCar] Initialized: dist={_dist:F1}m, lateral={_lateralOffset:F2}, speed={speed:F1}");

        return true;
    }

    private void ComputePivotToBottom()
    {
        if (_col == null) return;

        Bounds b = _col.bounds;
        _pivotToBottom = transform.position.y - b.min.y;
        _pivotToBottom = Mathf.Max(0f, _pivotToBottom);
    }

    // ============================================
    // PATH UTILITIES
    // ============================================

    private void RebuildPath(List<Vector3> src)
    {
        _path.Clear();
        _cumLengths = null;
        _totalLength = 0f;

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

    private void SampleAlongPath(float dist, out Vector3 pos, out Vector3 forward)
    {
        pos = Vector3.zero;
        forward = Vector3.forward;

        if (_path.Count < 2 || _cumLengths == null) return;

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

        float segStart = _cumLengths[idx];
        float segEnd = _cumLengths[Mathf.Min(idx + 1, _cumLengths.Length - 1)];
        float segLen = Mathf.Max(0.0001f, segEnd - segStart);
        float t = (dist - segStart) / segLen;

        Vector3 a = _path[idx];
        Vector3 b = _path[Mathf.Min(idx + 1, _path.Count - 1)];

        pos = Vector3.Lerp(a, b, t);
        forward = (b - a).normalized;
        if (forward.sqrMagnitude < 0.0001f) forward = Vector3.forward;
    }

    private float GetDistanceAlongPath(Vector3 worldPos)
    {
        if (_path.Count < 2 || _cumLengths == null) return 0f;

        float bestSqrDist = float.MaxValue;
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
            Vector3 proj = a + ab * t;
            float sqrDist = (worldPos - proj).sqrMagnitude;

            if (sqrDist < bestSqrDist)
            {
                bestSqrDist = sqrDist;
                bestIdx = i;
                bestT = t;
            }
        }

        float segLen = Vector3.Distance(_path[bestIdx], _path[bestIdx + 1]);
        return _cumLengths[bestIdx] + bestT * segLen;
    }

    private float GetHalfRoadWidth()
    {
        if (trackGenerator != null)
            return trackGenerator.RoadWidth * 0.5f;
        return 5f;
    }

    private bool RaycastGround(Vector3 pos, out RaycastHit hit)
    {
        Vector3 origin = pos + Vector3.up * raycastStartHeight;
        float maxDist = raycastStartHeight + raycastDownDistance;
        return Physics.Raycast(origin, Vector3.down, out hit, maxDist, roadLayer, QueryTriggerInteraction.Ignore);
    }

    private static void GenerateSmoothedPath(List<Vector3> src, int subdivisions, List<Vector3> outList)
    {
        outList.Clear();
        if (src == null || src.Count < 2) return;

        outList.Add(src[0]);

        for (int i = 0; i < src.Count - 1; i++)
        {
            Vector3 p0 = src[Mathf.Max(i - 1, 0)];
            Vector3 p1 = src[i];
            Vector3 p2 = src[i + 1];
            Vector3 p3 = src[Mathf.Min(i + 2, src.Count - 1)];

            for (int s = 1; s <= subdivisions; s++)
            {
                float t = s / (float)subdivisions;
                outList.Add(CatmullRom(p0, p1, p2, p3, t));
            }
        }
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

    // ============================================
    // COLLISION / CRASH
    // ============================================

    private void OnCollisionEnter(Collision collision)
    {
        if (_crashed) return;
        if (collision == null || collision.collider == null) return;
        if (!ShouldCrashWith(collision.collider)) return;

        Vector3 impactDir = collision.contactCount > 0
            ? -collision.GetContact(0).normal
            : transform.forward;

        float impactSpeed = collision.relativeVelocity.magnitude;
        TriggerCrash(impactDir, impactSpeed, collision.collider);
    }

    private void OnTriggerEnter(Collider other)
    {
        if (_crashed) return;
        if (other == null) return;
        if (!ShouldCrashWith(other)) return;

        Vector3 impactDir = (other.transform.position - transform.position).normalized;
        if (impactDir.sqrMagnitude < 0.001f) impactDir = transform.forward;

        float impactSpeed = _lastVelocity.magnitude;
        TriggerCrash(impactDir, impactSpeed, other);
    }

    private void CheckOverlapAndCrash()
    {
        if (_crashed) return;
        if (crashLayers.value == 0) return;

        Collider[] hits = Physics.OverlapSphere(transform.position, overlapRadius, crashLayers, QueryTriggerInteraction.Ignore);
        if (hits == null || hits.Length == 0) return;

        foreach (var other in hits)
        {
            if (other == null) continue;
            if (other.transform.IsChildOf(transform)) continue;
            if (other == _col) continue;

            if (ShouldCrashWith(other))
            {
                Vector3 impactDir = (other.transform.position - transform.position).normalized;
                TriggerCrash(impactDir, _lastVelocity.magnitude, other);
                return;
            }
        }
    }

    private bool ShouldCrashWith(Collider other)
    {
        if (other == null) return false;

        int layer = other.gameObject.layer;
        if ((crashLayers.value & (1 << layer)) == 0) return false;

        if (ignoreRoadAndTerrain)
        {
            string layerName = LayerMask.LayerToName(layer);
            if (layerName == "RoadSurface" || layerName == "Road" || layerName == "Terrain")
                return false;
        }

        return true;
    }

    private void TriggerCrash(Vector3 impactDir, float impactSpeed, Collider other)
    {
        if (_crashed) return;
        _crashed = true;

        if (verboseDebug)
            Debug.Log($"[NPCTrafficCar] CRASHED with {other.name}");

        // Stop A* movement
        if (_ai != null)
        {
            _ai.canMove = false;
            _ai.canSearch = false;
            _ai.SetPath(null);
        }

        // Stop engine audio
        StopEngineAudio();

        // Play crash SFX
        PlayCrashSfx();

        // Spawn VFX
        SpawnCrashVFX();

        // Convert to physics
        ConvertToPhysics(impactDir, impactSpeed);

        // Destroy after delay
        if (destroyAfterCrash)
        {
            Invoke(nameof(DestroySelf), destroyDelay);
        }
    }

    private static void ForceAstarFixedUpdate(object aiObj)
    {
        if (aiObj == null) return;

        var t = aiObj.GetType();
        var p = t.GetProperty("updateMode"); // AIBase.updateMode
        if (p != null && p.CanWrite && p.PropertyType.IsEnum)
        {
            try { p.SetValue(aiObj, Enum.Parse(p.PropertyType, "FixedUpdate", true)); }
            catch { }
        }
    }

    private void ConvertToPhysics(Vector3 impactDir, float impactSpeed)
    {
        if (_rb == null) return;

        _rb.isKinematic = false;
        _rb.useGravity = true;
        _rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

        // Base velocity
        Vector3 velocity = _lastVelocity;
        if (velocity.magnitude < minTransferVelocity)
            velocity = transform.forward * speed;

        // Add crash bounce
        Vector3 bounceDir = -impactDir;
        bounceDir.y = 0f;
        if (bounceDir.sqrMagnitude < 0.01f) bounceDir = -transform.forward;
        bounceDir.Normalize();

        velocity += bounceDir * crashBounceBack;
        velocity += Vector3.up * crashBounceUp;

        _rb.velocity = velocity;

        // Add spin
        float spinMag = UnityEngine.Random.Range(crashSpinRange.x, crashSpinRange.y) * Mathf.Deg2Rad;
        Vector3 spinAxis = new Vector3(
            UnityEngine.Random.Range(-0.2f, 0.2f),
            UnityEngine.Random.Range(0.5f, 1f),
            UnityEngine.Random.Range(-0.2f, 0.2f)
        ).normalized;
        _rb.angularVelocity = spinAxis * spinMag;
    }

    // ============================================
    // AUDIO
    // ============================================

    private void SetupEngineAudio()
    {
        if (engineClip == null) return;

        if (_engineSource == null)
        {
            _engineSource = gameObject.AddComponent<AudioSource>();
        }

        _engineSource.clip = engineClip;
        _engineSource.loop = true;
        _engineSource.playOnAwake = false;
        _engineSource.spatialBlend = 1f;
        _engineSource.volume = engineVolume;
        _engineSource.pitch = enginePitchMin;
        _engineSource.Play();
    }

    private void UpdateEngineAudio()
    {
        if (_engineSource == null) return;

        float speedNorm = Mathf.Clamp01(speed / speedRange.y);
        _engineSource.pitch = Mathf.Lerp(enginePitchMin, enginePitchMax, speedNorm);
    }

    private void StopEngineAudio()
    {
        if (_engineSource != null)
        {
            _engineSource.Stop();
        }
    }

    private void PlayCrashSfx()
    {
        if (crashClip == null) return;
        AudioSource.PlayClipAtPoint(crashClip, transform.position, crashVolume);
    }

    private void SpawnCrashVFX()
    {
        if (crashVFXPrefab == null) return;
        GameObject vfx = Instantiate(crashVFXPrefab, transform.position, Quaternion.identity);
        Destroy(vfx, crashVFXLifetime);
    }

    // ============================================
    // CLEANUP
    // ============================================

    private void DestroySelf()
    {
        Destroy(transform.parent.gameObject);
    }

    // ============================================
    // PUBLIC API (for spawner)
    // ============================================

    public bool HasCrashed => _crashed;

    public void SetGenerator(ProceduralTrackGenerator generator)
    {
        trackGenerator = generator;
        _initialized = false;
    }

    public void Reinitialize()
    {
        _initialized = false;
        _crashed = false;
        _hasSetDestination = false;
        InitializeIfNeeded();
    }

    // ============================================
    // DEBUG GIZMOS
    // ============================================

    private void OnDrawGizmos()
    {
        if (!drawDestinationGizmo || !_hasSetDestination) return;

        Gizmos.color = Color.green;
        Gizmos.DrawWireSphere(_lastDestination, 1f);
        Gizmos.DrawLine(transform.position, _lastDestination);
    }
}
