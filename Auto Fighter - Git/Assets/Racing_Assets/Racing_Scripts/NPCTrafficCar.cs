using Pathfinding;
using Pathfinding.RVO;
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
    [SerializeField] private bool enableOverlapDetection = false;
    [SerializeField] private float overlapCheckInterval = 0f;
    [SerializeField] private float overlapRadius = 0f;

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

    [Header("Surface Effects")]
    [Tooltip("Enable detection of GroundSurface components for speed modifiers.")]
    [SerializeField] private bool enableSurfaceEffects = true;

    [Tooltip("Layers to check for surface effects.")]
    [SerializeField] private LayerMask surfaceDetectionLayers;

    [Tooltip("How often to check surface (seconds).")]
    [SerializeField] private float surfaceCheckInterval = 0.1f;

    [Tooltip("How quickly to lerp to new speed multiplier.")]
    [SerializeField] private float surfaceSpeedLerpRate = 5f;

    [Header("Boost Pad Response")]
    [Tooltip("Extra speed added when on boost pad.")]
    [SerializeField] private float boostPadSpeedBonus = 8f;

    [Tooltip("How long boost lasts after leaving pad.")]
    [SerializeField] private float boostPadDuration = 1.5f;

    [Tooltip("Threshold: surface with speedMul > this is considered a boost pad.")]
    [SerializeField] private float boostPadThreshold = 1.3f;

    [Header("Debug")]
    [SerializeField] private bool verboseDebug = false;
    [SerializeField] private bool drawDestinationGizmo = true;

    // ============================================
    // RVO "THREAT" BOOST (speed/accel while avoiding)
    // ============================================
    [Header("RVO Avoidance Boost")]
    [SerializeField] private bool enableAvoidanceBoost = true;

    [SerializeField, Min(1f)] private float avoidanceSpeedMult = 1.35f;
    [SerializeField, Min(1f)] private float avoidanceAccelMult = 1.75f;

    [SerializeField, Min(0f)] private float avoidanceHoldSeconds = 0.25f; // extra time AFTER RVO stops avoiding
    [SerializeField, Min(0f)] private float avoidanceRampIn = 16f;        // higher = faster boost
    [SerializeField, Min(0f)] private float avoidanceRampOut = 9f;        // higher = faster return

    [SerializeField, Tooltip("Avoidance boost only applies if the NPC's SPAWN speed is <= this value.")]
    private float maxSpawnSpeedForAvoidanceBoost = 5f;

    [Header("Crash NavmeshCut")]
    [SerializeField] private bool addNavmeshCutOnCrash = true;

    [SerializeField] private bool crashCutUsePrefab = false;
    [SerializeField] private GameObject crashCutPrefab; // optional: prefab with NavmeshCut already configured

    // If not using prefab, we create one with these settings:
    [SerializeField] private Vector3 crashCutBoxSize = new Vector3(1.5f, 3f, 1.5f); // Width, Height, Depth
    [SerializeField] private float crashCutUpdateDistance = 0.4f;
    [SerializeField] private float crashCutUpdateRotationDistance = 10f;
    [SerializeField] private bool crashCutUseRotationAndScale = true;
    [SerializeField] private bool crashCutCutsAddedGeometry = true;

    private NavmeshCut _crashCut;

    private float _spawnSpeed;
    private bool _allowAvoidanceBoostForThisUnit;

    private RVOController _rvo;

    private float _defaultMaxSpeed;      // baseline (including surface effects)
    private float _defaultRichAccel;     // baseline accel we want to return to

    private float _avoidUntil = -999f;
    private float _avoidBlend01 = 0f;

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

    private float _baseSpeed;                   // Original speed before modifiers
    private float _currentSpeedMultiplier = 1f;
    private float _targetSpeedMultiplier = 1f;
    private float _surfaceCheckTimer;
    private float _boostEndTime;
    private bool _onBoostPad;
    private string _currentSurfaceType = "Normal";

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
        _rvo = GetComponent<RVOController>();

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

        _currentSpeedMultiplier = 1f;
        _targetSpeedMultiplier = 1f;
        _surfaceCheckTimer = 0f;
        _boostEndTime = 0f;
        _onBoostPad = false;

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


        if (enableSurfaceEffects)
        {
            UpdateSurfaceEffects(dt);
        }
        UpdateAvoidanceBoost(dt);
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

        // Only apply baseline if we're NOT currently blending boost.
        if (_avoidBlend01 > 0.0001f) return;

        if (_ai != null) _ai.maxSpeed = speed;
        if (_richAI != null) _richAI.maxSpeed = speed;
    }

    private void LateUpdate()
    {
        if (_crashed) return;
        if (!_initialized) return;

        float dt = Time.deltaTime;
        if (dt <= 0f) return;

        // ============================================
        // GROUND SNAP (Y only)
        // ============================================
        Vector3 pos = transform.position;
        if (RaycastGround(pos, out RaycastHit hit))
        {
            pos.y = hit.point.y + _pivotToBottom + groundClearance;
            transform.position = pos;
            _groundNormal = hit.normal;
        }

        // ============================================
        // VELOCITY CALCULATION (use A* velocity when available)
        // ============================================
        Vector3 currentVel = Vector3.zero;
        if (_aiBase != null)
        {
            currentVel = _aiBase.velocity;
        }
        else
        {
            currentVel = (transform.position - _prevPosition) / dt;
        }
        _prevPosition = transform.position;

        // Frame-rate independent exponential smoothing for velocity
        float smoothFactor = 1f - Mathf.Exp(-dt / Mathf.Max(0.01f, velocitySmoothTime));
        _smoothedVelocity = Vector3.Lerp(_smoothedVelocity, currentVel, smoothFactor);
        _lastVelocity = currentVel;

        // ============================================
        // ROTATION - just face velocity direction
        // ============================================
        if (rotateToVelocity)
        {
            Vector3 vel = _smoothedVelocity;
            vel.y = 0f;

            if (vel.sqrMagnitude > minSpeedForRotation * minSpeedForRotation)
            {
                Quaternion targetRot = Quaternion.LookRotation(vel.normalized, Vector3.up);
                transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, rotationSpeed * dt);
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

        if (randomizeSpeed)
        {
            speed = UnityEngine.Random.Range(speedRange.x, speedRange.y);
        }

        // Store base speed for surface modifiers
        _baseSpeed = speed;

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

            ConfigureAstarUpdateMode(_richAI);
        }
        else if (_aiBase != null)
        {
            ConfigureAstarUpdateMode(_aiBase);
        }
        _defaultMaxSpeed = speed;
        _defaultRichAccel = _richAI.acceleration;
        _spawnSpeed = speed;
        _allowAvoidanceBoostForThisUnit = _spawnSpeed <= maxSpawnSpeedForAvoidanceBoost;


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

    private void UpdateSurfaceEffects(float dt)
    {
        // Check surface periodically
        _surfaceCheckTimer -= dt;
        if (_surfaceCheckTimer <= 0f)
        {
            _surfaceCheckTimer = surfaceCheckInterval;
            CheckSurfaceUnderCar();
        }

        // Handle boost duration
        if (_boostEndTime > 0f && Time.time > _boostEndTime)
        {
            _boostEndTime = 0f;
            _onBoostPad = false;
        }

        // Lerp speed multiplier
        _currentSpeedMultiplier = Mathf.Lerp(_currentSpeedMultiplier, _targetSpeedMultiplier, surfaceSpeedLerpRate * dt);

        // Calculate final speed
        float newSpeed = _baseSpeed * _currentSpeedMultiplier;

        // Add boost bonus if active
        if (_onBoostPad || _boostEndTime > Time.time)
        {
            newSpeed += boostPadSpeedBonus;
        }

        speed = newSpeed;
    }

    private void CheckSurfaceUnderCar()
    {
        Vector3 origin = transform.position + Vector3.up * raycastStartHeight;
        float maxDist = raycastStartHeight + raycastDownDistance;

        // Use surfaceDetectionLayers if set, otherwise use roadLayer
        LayerMask checkLayers = surfaceDetectionLayers.value != 0 ? surfaceDetectionLayers : roadLayer;

        if (Physics.Raycast(origin, Vector3.down, out RaycastHit hit, maxDist, checkLayers, QueryTriggerInteraction.Collide))
        {
            // Check for GroundSurface component
            GroundSurface surface = hit.collider.GetComponent<GroundSurface>();
            if (surface == null)
                surface = hit.collider.GetComponentInParent<GroundSurface>();

            if (surface != null)
            {
                ApplySurfaceEffects(surface);
            }
            else
            {
                // No surface component - reset to normal
                _targetSpeedMultiplier = 1f;
                _currentSurfaceType = "Normal";
            }
        }
    }

    private void ApplySurfaceEffects(GroundSurface surface)
    {
        _currentSurfaceType = surface.surfaceType.ToString();

        // Apply speed multiplier from surface
        _targetSpeedMultiplier = Mathf.Clamp(surface.maxSpeedMultiplier, 0.1f, 5f);

        // Detect boost pad (high speed + high acceleration = boost)
        bool isBoostPad = surface.maxSpeedMultiplier > boostPadThreshold &&
                          surface.accelerationMultiplier > boostPadThreshold;

        if (isBoostPad && !_onBoostPad)
        {
            // Just entered boost pad
            _onBoostPad = true;
            _boostEndTime = Time.time + boostPadDuration;

            if (verboseDebug)
                Debug.Log($"[NPCTrafficCar] BOOST PAD! Speed: {speed:F1} -> {speed + boostPadSpeedBonus:F1}");
        }
        else if (!isBoostPad)
        {
            _onBoostPad = false;
        }

        // Update A* speed immediately for boost response
        if (_ai != null) _ai.maxSpeed = speed;
        if (_richAI != null) _richAI.maxSpeed = speed;
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
        if (_col == null) return;

        // Tight search radius based on our real collider size (not an arbitrary 1.0m sphere)
        float searchRadius = Mathf.Max(0.05f, GetPlanarColliderRadius(_col) + 0.05f);

        Collider[] hits = Physics.OverlapSphere(
            _col.bounds.center,
            searchRadius,
            crashLayers,
            QueryTriggerInteraction.Ignore
        );

        if (hits == null || hits.Length == 0) return;

        foreach (var other in hits)
        {
            if (other == null) continue;
            if (other.transform.IsChildOf(transform)) continue;
            if (other == _col) continue;
            if (!ShouldCrashWith(other)) continue;

            // ✅ Only crash if colliders are ACTUALLY overlapping
            if (Physics.ComputePenetration(
                    _col, transform.position, transform.rotation,
                    other, other.transform.position, other.transform.rotation,
                    out Vector3 pushDir, out float pushDist))
            {
                Vector3 impactDir = (pushDist > 0.0001f) ? -pushDir : (other.transform.position - transform.position).normalized;
                TriggerCrash(impactDir, _lastVelocity.magnitude, other);
                return;
            }
        }
    }

    private static float GetPlanarColliderRadius(Collider c)
    {
        // “radius” in XZ plane from bounds (good for boxy cars)
        Bounds b = c.bounds;
        return Mathf.Max(b.extents.x, b.extents.z);
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
        EnableCrashNavmeshCut();

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

    private static void ConfigureAstarUpdateMode(object aiObj)
    {
        if (aiObj == null) return;

        var t = aiObj.GetType();
        var p = t.GetProperty("updateMode"); // AIBase.updateMode
        if (p != null && p.CanWrite && p.PropertyType.IsEnum)
        {
            // Use Update mode for smoother movement (not FixedUpdate)
            try { p.SetValue(aiObj, Enum.Parse(p.PropertyType, "Update", true)); }
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

    private void EnableCrashNavmeshCut()
    {
        if (!addNavmeshCutOnCrash) return;
        if (_crashCut != null) return; // already created

        // Prefer prefab if you want exact inspector settings without code drift
        if (crashCutUsePrefab && crashCutPrefab != null)
        {
            GameObject go = Instantiate(crashCutPrefab, transform);
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;

            _crashCut = go.GetComponent<NavmeshCut>();
            if (_crashCut == null) _crashCut = go.AddComponent<NavmeshCut>();
        }
        else
        {
            // Create child
            GameObject go = new GameObject("Crash_NavmeshCut");
            go.transform.SetParent(transform, false);
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;

            _crashCut = go.AddComponent<NavmeshCut>();

            // Match your screenshot-ish settings
            _crashCut.type = NavmeshCut.MeshType.Box;
            _crashCut.center = Vector3.zero;

            // A* uses 'rectangleSize' for box width/depth and 'height' for Y
            _crashCut.rectangleSize = new Vector2(crashCutBoxSize.x, crashCutBoxSize.z);
            _crashCut.height = crashCutBoxSize.y;

            _crashCut.updateDistance = crashCutUpdateDistance;
            _crashCut.useRotationAndScale = crashCutUseRotationAndScale;
            _crashCut.updateRotationDistance = crashCutUpdateRotationDistance;

            _crashCut.cutsAddedGeom = crashCutCutsAddedGeometry;
            _crashCut.radiusExpansionMode = NavmeshCut.RadiusExpansionMode.DontExpand;
            _crashCut.graphMask = GraphMask.everything;
        }

        // Force immediate update so others repath ASAP
        _crashCut.enabled = true;
        _crashCut.ForceUpdate();
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

    private void UpdateAvoidanceBoost(float dt)
    {
        if (!enableAvoidanceBoost) return;
        if (!_allowAvoidanceBoostForThisUnit)
            return;
        if (_crashed) return;
        if (_ai == null) return;

        // If RVO says we're actively avoiding, extend the boost window.
        // This acts like your "threat is imminent / dodge now" signal.
        if (_rvo != null && _rvo.AvoidingAnyAgents)
            _avoidUntil = Time.time + avoidanceHoldSeconds;

        bool active = Time.time <= _avoidUntil;

        float target = active ? 1f : 0f;
        float rate = active ? avoidanceRampIn : avoidanceRampOut;
        _avoidBlend01 = Mathf.MoveTowards(_avoidBlend01, target, rate * dt);

        // While not boosted, keep baseline caches fresh (surface effects may change speed/accel)
        if (_avoidBlend01 <= 0.0001f)
        {
            _defaultMaxSpeed = speed;
            if (_richAI != null) _defaultRichAccel = _richAI.acceleration;
            return;
        }

        float boostedSpeed = speed * avoidanceSpeedMult;
        float appliedSpeed = Mathf.Lerp(speed, boostedSpeed, _avoidBlend01);

        if (_ai != null) _ai.maxSpeed = appliedSpeed;
        if (_richAI != null) _richAI.maxSpeed = appliedSpeed;

        // Accel only applies if we're using RichAI (you are)
        if (_richAI != null)
        {
            // If default accel wasn't cached yet, treat current as default
            if (_defaultRichAccel <= 0f) _defaultRichAccel = _richAI.acceleration;

            float boostedAccel = _defaultRichAccel * avoidanceAccelMult;
            _richAI.acceleration = Mathf.Lerp(_defaultRichAccel, boostedAccel, _avoidBlend01);
        }
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

    public void ForceCrashFromForcefield(Vector3 worldImpactFrom, float impactSpeed, Collider source)
    {
        if (_crashed) return;

        // Impact dir should point from THIS NPC toward the thing that hit it.
        Vector3 impactDir = (worldImpactFrom - transform.position);
        impactDir.y = 0f;
        if (impactDir.sqrMagnitude < 0.0001f) impactDir = transform.forward;
        impactDir.Normalize();

        TriggerCrash(impactDir, impactSpeed, source != null ? source : _col);
    }

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
