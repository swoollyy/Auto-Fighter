using System;
using System.Collections.Generic;
using UnityEngine;
using Pathfinding;
using Pathfinding.RVO;

/// <summary>
/// NPC traffic car: drives forward along the procedural track and dodges obstacles.
/// Track-only steering (A* is optional and unused for movement). Lane-probe + fan obstacle
/// detection picks the clearest path; steering follows track and overrides with avoidance when needed.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
[RequireComponent(typeof(Collider))]
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
    // LEGACY A* (unused – track-only steering; fields kept for prefab compat)
    // ============================================
    [Header("Legacy A* (unused)")]
    [SerializeField] private float destinationLookAhead = 25f;
    [SerializeField] private float destinationUpdateInterval = 0.15f;
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
    [Tooltip("Optional: Road + Grass (or same as Road). Used for movement validation and ground snap so NPC can drive on grass to return to track. If 0, uses roadLayer only.")]
    [SerializeField] private LayerMask driveableLayer;
    [SerializeField] private float raycastStartHeight = 5f;
    [SerializeField] private float raycastDownDistance = 15f;
    [SerializeField] private float groundClearance = 0.05f;

    // ============================================
    // VEHICLE STEERING (NEW - replaces direct RVO control)
    // ============================================
    [Header("Vehicle Steering Physics")]
    [Tooltip("Base turn rate in degrees/second at low speed (higher = snappier dodges).")]
    [SerializeField] private float baseTurnRate = 145f;

    [Tooltip("Minimum turn rate at high speed (degrees/second).")]
    [SerializeField] private float minTurnRate = 35f;

    [Tooltip("Speed at which turn rate reaches minimum.")]
    [SerializeField] private float turnRateFalloffSpeed = 20f;

    [Tooltip("How much the car accelerates toward target speed.")]
    [SerializeField] private float accelerationRate = 15f;

    [Tooltip("How much the car decelerates (braking).")]
    [SerializeField] private float decelerationRate = 25f;

    [Tooltip("Smooth steering input changes over this time.")]
    [SerializeField] private float steeringSmoothing = 0.2f;

    [Tooltip("Smooth speed changes over this time (reduces jitter).")]
    [SerializeField] private float speedSmoothing = 0.12f;

    [Header("Predictive alignment (reduces S‑slither overcorrection)")]
    [Tooltip("Target time (seconds) to align with track/target direction. Steering is scaled so we reorient in this time instead of full‑lock.")]
    [SerializeField, Min(0.1f)] private float alignmentTime = 0.4f;

    [Tooltip("Angle (degrees) below which steering is zeroed to avoid jitter and overcorrect.")]
    [SerializeField, Range(0f, 10f)] private float steeringDeadZoneDeg = 2.5f;

    [Tooltip("Damping when already turning toward target (0=none, ~0.3=less overshoot).")]
    [SerializeField, Range(0f, 0.6f)] private float steeringDamping = 0.25f;

    // ============================================
    // OBSTACLE DETECTION (NEW - replaces instant RVO dodge)
    // ============================================
    [Header("Obstacle Detection")]
    [Tooltip("Layers to detect as obstacles for steering avoidance.")]
    [SerializeField] private LayerMask obstacleDetectionLayers;

    [Tooltip("How far ahead to scan for obstacles. Larger = see clear path earlier.")]
    [SerializeField] private float obstacleDetectionRange = 38f;

    [Tooltip("Width of the detection zone (car width + margin).")]
    [SerializeField] private float obstacleDetectionWidth = 3f;

    [Tooltip("Number of rays to cast in the fan pattern (more = better coverage and gap detection).")]
    [SerializeField, Range(5, 21)] private int obstacleRayCount = 17;

    [Tooltip("Fan angle for obstacle detection (degrees).")]
    [SerializeField] private float obstacleDetectionAngle = 65f;

    [Tooltip("How strongly to steer away from obstacles (0-1).")]
    [SerializeField, Range(0f, 1f)] private float obstacleAvoidanceStrength = 1f;

    [Tooltip("Distance at which avoidance reaches maximum strength.")]
    [SerializeField] private float criticalObstacleDistance = 8f;

    [Tooltip("How quickly to blend into avoidance steering.")]
    [SerializeField] private float avoidanceBlendSpeed = 14f;

    [Tooltip("Angle of the center 'danger zone' – any hit in this cone triggers full left/right avoidance (wider = react to more obstacles).")]
    [SerializeField] private float dangerZoneAngle = 34f;

    [Tooltip("Start gentle avoidance when side obstacles are within this distance.")]
    [SerializeField] private float sideObstacleSoftDistance = 12f;

    [Tooltip("Height offset for obstacle detection rays.")]
    [SerializeField] private float obstacleRayHeight = 0.5f;

    [Header("Clear-Path / Lane Probe (find optimal path)")]
    [Tooltip("Cast rays at multiple lateral offsets to find which lane is clearest; steer toward it for a more optimal path.")]
    [SerializeField] private bool useLaneProbe = true;

    [Tooltip("Number of lateral lanes to probe (e.g. 5 = left, left-mid, center, right-mid, right).")]
    [SerializeField, Range(3, 9)] private int laneProbeCount = 5;

    [Tooltip("Lateral spread of lane probes in meters (half-width each side).")]
    [SerializeField, Min(0.5f)] private float laneProbeSpread = 2.5f;

    [Tooltip("Use lane-probe when best lane is this much clearer than center (1.05 = slight advantage).")]
    [SerializeField, Min(1f)] private float laneProbeMinAdvantage = 1.05f;

    [Tooltip("Minimum track room (m) on a side to prefer dodging that way. Higher = stay on track more; off-track is last resort.")]
    [SerializeField, Min(0.3f)] private float minTrackRoomToDodge = 2.2f;

    [Tooltip("Half-width (m) of 'our path' – only obstacles in this band ahead trigger avoidance. Stops dodging things outside our lane.")]
    [SerializeField, Min(0.4f)] private float pathHalfWidthForAvoidance = 1.4f;

    [Header("Obstacle confirmation (reduces jitter)")]
    [Tooltip("Frames obstacle must be seen in path before we start avoiding. Stops reacting to flicker.")]
    [SerializeField, Min(1)] private int obstacleConfirmFrames = 2;
    [Tooltip("Frames path must be clear before we stop avoiding. Commits to the dodge until we've passed the obstacle.")]
    [SerializeField, Min(2)] private int avoidancePersistClearFrames = 6;

    [Header("Speed Reduction on Avoidance")]
    [Tooltip("Slow down when avoiding obstacles.")]
    [SerializeField] private bool slowDownOnAvoidance = true;

    [Tooltip("Minimum speed multiplier during avoidance.")]
    [SerializeField, Range(0.3f, 1f)] private float avoidanceSpeedMultiplier = 0.6f;

    // ============================================
    // ROAD BOUNDARY DETECTION (keeps car on road)
    // ============================================
    [Header("Road Boundary Detection")]
    [Tooltip("Enable road edge detection to keep car on the road.")]
    [SerializeField] private bool enableRoadBoundaryDetection = true;

    [Tooltip("How far to the sides to cast rays for edge detection.")]
    [SerializeField] private float roadEdgeDetectionWidth = 4f;

    [Tooltip("How strongly to steer back toward road center (0-1).")]
    [SerializeField, Range(0f, 1f)] private float roadCorrectionStrength = 0.9f;

    [Tooltip("Distance from edge where correction starts.")]
    [SerializeField] private float roadEdgeSoftMargin = 1.5f;

    [Tooltip("If true, block movement when not on driveable (road/grass). No teleport - car must drive back.")]
    [SerializeField] private bool validateMovementOnRoad = true;

    [Tooltip("(Reserved – track-only steering now.)")]
    [SerializeField, Range(0f, 1f)] private float trackFollowingStrength = 0.4f;

    [Tooltip("When off track, steer this strongly toward track center (0-1).")]
    [SerializeField, Range(0f, 1f)] private float offTrackRecoveryStrength = 0.9f;

    [Tooltip("Track direction look-ahead distance (m). Higher = earlier turn-in on curves.")]
    [SerializeField] private float trackLookAhead = 12f;

    [Tooltip("Show road boundary debug rays.")]
    [SerializeField] private bool drawRoadBoundaryDebug = true;

    // ============================================
    // ROTATION (simplified - now driven by steering)
    // ============================================
    [Header("Rotation")]
    [SerializeField] private bool alignToGround = false;

    [Tooltip("Minimum speed to update rotation (prevents spinning when nearly stopped).")]
    [SerializeField] private float minSpeedForRotation = 0.5f;

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

    [Header("Crash NavmeshCut")]
    [SerializeField] private bool addNavmeshCutOnCrash = true;

    [SerializeField] private bool crashCutUsePrefab = false;
    [SerializeField] private GameObject crashCutPrefab;

    [SerializeField] private Vector3 crashCutBoxSize = new Vector3(1.5f, 3f, 1.5f);
    [SerializeField] private float crashCutUpdateDistance = 0.4f;
    [SerializeField] private float crashCutUpdateRotationDistance = 10f;
    [SerializeField] private bool crashCutUseRotationAndScale = true;
    [SerializeField] private bool crashCutCutsAddedGeometry = true;

    [Header("Debug")]
    [SerializeField] private bool verboseDebug = false;
    [SerializeField] private bool drawDestinationGizmo = true;
    [SerializeField] private bool drawObstacleRays = true;
    [SerializeField] private bool drawSteeringDebug = true;

    // ============================================
    // INTERNALS - Track Path
    // ============================================
    private readonly List<Vector3> _path = new();
    private float[] _cumLengths;
    private float _totalLength;

    private float _dist;
    private float _lateralOffset;
    private float _pivotToBottom;

    // ============================================
    // INTERNALS - Components
    // ============================================
    private Rigidbody _rb;
    private Collider _col;
    private AudioSource _engineSource;
    private Seeker _seeker;
    private IAstarAI _ai;
    private RichAI _richAI;
    private AIBase _aiBase;
    private RVOController _rvo;
    private NavmeshCut _crashCut;

    // ============================================
    // INTERNALS - Vehicle State (NEW)
    // ============================================
    private float _currentSpeed;           // Actual current speed
    private float _targetSpeed;            // Speed we want to reach
    private Vector3 _currentForward;       // Current facing direction
    private float _currentSteeringInput;   // -1 to 1 steering
    private float _smoothedSteeringInput;  // Smoothed version
    private float _steeringVelocity;       // For SmoothDamp
    private float _speedVelocity;          // For speed SmoothDamp
    private float _prevAngleToTarget;      // For damping (reduce overshoot)

    // ============================================
    // INTERNALS - Avoidance State (NEW)
    // ============================================
    private float _avoidanceUrgency;       // 0-1, how urgent is avoidance
    private Vector3 _avoidanceDirection;   // Direction to steer toward (committed when avoiding)
    private float _avoidanceBlend;         // Current blend toward avoidance
    private bool _isAvoiding;
    private int _obstacleInPathConfirmCount;  // Frames we've seen obstacle in path
    private int _avoidanceClearCount;         // Frames path has been clear (while we were avoiding)

    // ============================================
    // INTERNALS - Road Boundary State (NEW)
    // ============================================
    private float _roadCorrectionInput;    // -1 to 1, steering correction to stay on road
    private float _distanceFromTrackCenter; // How far off center we are
    private bool _leftEdgeDetected;
    private bool _rightEdgeDetected;
    private float _leftEdgeDistance;
    private float _rightEdgeDistance;
    private bool _isOnRoad;                // Current position on road layer (for recovery)
    private Vector3 _trackCenterAtDist;    // Track center at our path distance (for off-track steer)

    // ============================================
    // INTERNALS - State
    // ============================================
    private bool _initialized;
    private bool _crashed;
    private float _overlapTimer;
    private float _destTimer;

    private Vector3 _lastVelocity;
    private Vector3 _groundNormal = Vector3.up;
    private Vector3 _lastDestination;
    private bool _hasSetDestination;

    private float _baseSpeed;
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
        _seeker = GetComponent<Seeker>();       // Optional: no longer required
        _ai = GetComponent<IAstarAI>();
        _richAI = GetComponent<RichAI>();
        _aiBase = GetComponent<AIBase>();
        _rvo = GetComponent<RVOController>();

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
        _hasSetDestination = false;

        _currentSpeed = 0f;
        _currentForward = transform.forward;
        _currentSteeringInput = 0f;
        _smoothedSteeringInput = 0f;
        _prevAngleToTarget = 0f;
        _avoidanceUrgency = 0f;
        _avoidanceBlend = 0f;
        _isAvoiding = false;
        _obstacleInPathConfirmCount = 0;
        _avoidanceClearCount = 0;

        // Road boundary state
        _roadCorrectionInput = 0f;
        _distanceFromTrackCenter = 0f;
        _leftEdgeDetected = false;
        _rightEdgeDetected = false;
        _leftEdgeDistance = roadEdgeDetectionWidth;
        _rightEdgeDistance = roadEdgeDetectionWidth;

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

        if (enableSurfaceEffects)
        {
            UpdateSurfaceEffects(dt);
        }

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

    private void FixedUpdate()
    {
        if (_crashed) return;
        if (!InitializeIfNeeded()) return;

        float dt = Time.fixedDeltaTime;
        if (dt <= 0f) return;

        // Update track distance from current position (fixed step = consistent simulation)
        _dist = GetDistanceAlongPath(transform.position);

        // Road boundary first so obstacle detection can prefer staying on track
        UpdateRoadBoundaryDetection();
        UpdateObstacleDetection();
        UpdateSteering(dt);
        UpdateMovement(dt);
    }

    private void LateUpdate()
    {
        if (_crashed) return;
        if (!_initialized) return;

        // Ground normal for alignToGround (position is already snapped in FixedUpdate)
        if (RaycastGround(transform.position, out RaycastHit hit))
            _groundNormal = hit.normal;

        // Store velocity for crash physics
        _lastVelocity = _currentForward * _currentSpeed;
    }

    // ============================================
    // NEW: VEHICLE STEERING SYSTEM
    // ============================================

    private void UpdateObstacleDetection()
    {
        if (obstacleDetectionLayers.value == 0)
        {
            _avoidanceUrgency = 0f;
            _isAvoiding = false;
            _obstacleInPathConfirmCount = 0;
            _avoidanceClearCount = 0;
            return;
        }

        Vector3 origin = transform.position + Vector3.up * obstacleRayHeight;
        Vector3 forward = _currentForward;
        forward.y = 0f;
        forward.Normalize();
        if (forward.sqrMagnitude < 0.01f)
            forward = transform.forward;

        Vector3 right = Vector3.Cross(Vector3.up, forward).normalized;
        float range = obstacleDetectionRange;

        // Path check: only react to obstacles in our lane (band ahead of car)
        SampleAlongPath(_dist, out Vector3 trackCenter, out Vector3 trackFwd);
        trackFwd.y = 0f;
        if (trackFwd.sqrMagnitude < 0.01f) trackFwd = forward;
        trackFwd.Normalize();
        Vector3 trackRight = Vector3.Cross(Vector3.up, trackFwd).normalized;

        bool IsHitInPath(Vector3 hitPoint)
        {
            float hitLateral = Vector3.Dot(hitPoint - trackCenter, trackRight);
            return Mathf.Abs(hitLateral - _distanceFromTrackCenter) <= pathHalfWidthForAvoidance;
        }

        // We'll decide avoidance from this frame's rays, then apply confirmation so we don't jitter
        bool wantAvoid = false;
        Vector3 avoidDir = forward;
        float avoidUrg = 0f;

        // ---- 1) Lane probe: find which lateral lane has the longest clear path ----
        bool laneProbeChoseDirection = false;
        if (useLaneProbe && laneProbeCount >= 3 && laneProbeSpread > 0f)
        {
            int n = laneProbeCount;
            float step = (n > 1) ? (2f * laneProbeSpread / (n - 1)) : 0f;
            int centerIdxI = n / 2;
            float bestClearDist = 0f;
            int bestIdx = centerIdxI;
            float centerClearDist = 0f;

            for (int i = 0; i < n; i++)
            {
                float offset = -laneProbeSpread + step * i;
                Vector3 rayStart = origin + right * offset;
                float clearDist;
                if (Physics.Raycast(rayStart, forward, out RaycastHit hit, range, obstacleDetectionLayers, QueryTriggerInteraction.Ignore))
                {
                    if (hit.collider.transform.IsChildOf(transform)) { clearDist = range; }
                    else { clearDist = hit.distance; }
                    if (drawObstacleRays)
                        Debug.DrawLine(rayStart, hit.point, Color.Lerp(Color.red, Color.green, hit.distance / range));
                }
                else
                {
                    clearDist = range;
                    if (i == centerIdxI)
                        centerClearDist = range;
                    if (drawObstacleRays)
                        Debug.DrawRay(rayStart, forward * range, Color.green);
                }

                if (i == centerIdxI && clearDist < range)
                    centerClearDist = IsHitInPath(hit.point) ? clearDist : range;
                if (clearDist > bestClearDist)
                {
                    bestClearDist = clearDist;
                    bestIdx = i;
                }
            }

            // Use lane-probe when center is blocked and another lane is clearer – only if that lane stays on track
            bool centerBlockedEnough = centerClearDist < range * 0.9f;
            bool bestLaneClearer = bestClearDist >= centerClearDist * laneProbeMinAdvantage && bestIdx != centerIdxI;
            bool bestLaneIsLeft = bestIdx < centerIdxI;
            bool hasTrackRoomOnBestSide = bestLaneIsLeft ? (_leftEdgeDistance >= minTrackRoomToDodge) : (_rightEdgeDistance >= minTrackRoomToDodge);
            if (centerClearDist > 0f && centerBlockedEnough && bestLaneClearer && hasTrackRoomOnBestSide)
            {
                float lateralOffset = -laneProbeSpread + step * bestIdx;
                float avoidAngle = Mathf.Clamp(lateralOffset * 12f, -42f, 42f);
                wantAvoid = true;
                avoidDir = Quaternion.Euler(0f, avoidAngle, 0f) * forward;
                avoidUrg = Mathf.Max(0.25f, Mathf.Clamp01(1f - (bestClearDist / range)));
                laneProbeChoseDirection = true;
                if (drawObstacleRays)
                    Debug.DrawRay(origin + Vector3.up * 0.5f, avoidDir * 6f, Color.cyan);
            }
        }

        if (!laneProbeChoseDirection)
        {
            // ---- 2) Fan rays: center / left / right obstacle distances ----
            float closestCenterHit = float.MaxValue;
            float closestLeftHit = float.MaxValue;
            float closestRightHit = float.MaxValue;
            bool centerBlocked = false;
            float halfAngle = obstacleDetectionAngle * 0.5f;
            float angleStep = (obstacleRayCount > 1) ? (obstacleDetectionAngle / (obstacleRayCount - 1)) : 0f;
            float halfDangerZone = dangerZoneAngle * 0.5f;

            // Center ray: only block if the hit is in our path (lane)
            if (Physics.Raycast(origin, forward, out RaycastHit centerHit, range, obstacleDetectionLayers, QueryTriggerInteraction.Ignore))
            {
                if (!centerHit.collider.transform.IsChildOf(transform) && IsHitInPath(centerHit.point))
                {
                    centerBlocked = true;
                    if (centerHit.distance < closestCenterHit)
                        closestCenterHit = centerHit.distance;
                }
            }

            for (int i = 0; i < obstacleRayCount; i++)
            {
                float angle = -halfAngle + angleStep * i;
                Vector3 rayDir = Quaternion.Euler(0f, angle, 0f) * forward;

                if (Physics.Raycast(origin, rayDir, out RaycastHit hit, range, obstacleDetectionLayers, QueryTriggerInteraction.Ignore))
                {
                    if (hit.collider.transform.IsChildOf(transform)) continue;
                    if (!IsHitInPath(hit.point)) continue; // Ignore obstacles outside our lane

                    bool isInDangerZone = Mathf.Abs(angle) <= halfDangerZone;
                    bool isLeftSide = angle < -halfDangerZone;
                    bool isRightSide = angle > halfDangerZone;

                    if (isInDangerZone)
                    {
                        centerBlocked = true;
                        if (hit.distance < closestCenterHit)
                            closestCenterHit = hit.distance;
                    }
                    else if (isLeftSide)
                    {
                        if (hit.distance < closestLeftHit)
                            closestLeftHit = hit.distance;
                    }
                    else if (isRightSide)
                    {
                        if (hit.distance < closestRightHit)
                            closestRightHit = hit.distance;
                    }

                    if (drawObstacleRays)
                        Debug.DrawLine(origin, hit.point, isInDangerZone ? Color.red : Color.yellow);
                }
                else if (drawObstacleRays)
                    Debug.DrawRay(origin, rayDir * range, Color.green);
            }

            // Treat "no hit" as very large clearance so we prefer that side
            if (closestLeftHit == float.MaxValue) closestLeftHit = range * 2f;
            if (closestRightHit == float.MaxValue) closestRightHit = range * 2f;

            if (centerBlocked)
            {
                float urg = Mathf.Max(0.55f, Mathf.Clamp01(1f - (closestCenterHit - criticalObstacleDistance) /
                    Mathf.Max(0.1f, range - criticalObstacleDistance)));

                bool leftHasRoom = _leftEdgeDistance >= minTrackRoomToDodge;
                bool rightHasRoom = _rightEdgeDistance >= minTrackRoomToDodge;
                bool preferLeft;
                if (!leftHasRoom && !rightHasRoom)
                    preferLeft = _leftEdgeDistance >= _rightEdgeDistance;
                else if (!leftHasRoom)
                    preferLeft = false;
                else if (!rightHasRoom)
                    preferLeft = true;
                else
                    // Both have room: prefer the side with MORE track room (stay on track); tiebreak by obstacle clearance
                    preferLeft = _leftEdgeDistance > _rightEdgeDistance + 0.3f ? true
                        : (_rightEdgeDistance > _leftEdgeDistance + 0.3f ? false
                        : (closestLeftHit >= closestRightHit));

                float clearanceOnChosenSide = preferLeft ? closestLeftHit : closestRightHit;
                float trackRoomOnChosenSide = preferLeft ? _leftEdgeDistance : _rightEdgeDistance;
                float baseAngle = (preferLeft ? -1f : 1f) * Mathf.Lerp(38f, 22f, Mathf.InverseLerp(criticalObstacleDistance, range, clearanceOnChosenSide));
                baseAngle *= Mathf.Lerp(0.7f, 1f, urg);
                // Scale down angle when track room is limited so we don't oversteer off the track
                float roomScale = Mathf.Clamp01(trackRoomOnChosenSide / 3.5f);
                baseAngle *= Mathf.Lerp(0.6f, 1f, roomScale);
                wantAvoid = true;
                avoidDir = Quaternion.Euler(0f, baseAngle, 0f) * forward;
                avoidUrg = urg;
                if (drawObstacleRays)
                    Debug.DrawRay(origin + Vector3.up * 0.5f, avoidDir * 5f, Color.magenta);
            }
            else
            {
                float softLeft = closestLeftHit;
                float softRight = closestRightHit;
                bool bothSidesClose = softLeft < sideObstacleSoftDistance && softRight < sideObstacleSoftDistance;

                if (bothSidesClose)
                {
                    wantAvoid = false;
                    if (drawObstacleRays)
                        Debug.DrawRay(origin + Vector3.up * 0.5f, forward * 4f, Color.cyan);
                }
                else if (softLeft < sideObstacleSoftDistance || softRight < sideObstacleSoftDistance)
                {
                    bool leftHasRoom = _leftEdgeDistance >= minTrackRoomToDodge;
                    bool rightHasRoom = _rightEdgeDistance >= minTrackRoomToDodge;
                    bool preferLeftSoft;
                    if (!leftHasRoom && !rightHasRoom)
                        preferLeftSoft = _leftEdgeDistance >= _rightEdgeDistance;
                    else if (!leftHasRoom)
                        preferLeftSoft = false;
                    else if (!rightHasRoom)
                        preferLeftSoft = true;
                    else
                        preferLeftSoft = _leftEdgeDistance > _rightEdgeDistance + 0.3f ? true
                            : (_rightEdgeDistance > _leftEdgeDistance + 0.3f ? false
                            : (softLeft >= softRight));
                    float clear = preferLeftSoft ? softLeft : softRight;
                    float urgency = Mathf.Lerp(0.3f, 0.6f, 1f - Mathf.Clamp01(clear / sideObstacleSoftDistance));
                    float avoidAngle = (preferLeftSoft ? -1f : 1f) * Mathf.Lerp(18f, 28f, urgency);
                    float trackRoomSoft = preferLeftSoft ? _leftEdgeDistance : _rightEdgeDistance;
                    avoidAngle *= Mathf.Lerp(0.6f, 1f, Mathf.Clamp01(trackRoomSoft / 3.5f));
                    wantAvoid = true;
                    avoidDir = Quaternion.Euler(0f, avoidAngle, 0f) * forward;
                    avoidUrg = urgency;
                    if (drawObstacleRays)
                        Debug.DrawRay(origin + Vector3.up * 0.5f, avoidDir * 4f, Color.Lerp(Color.yellow, Color.magenta, urgency));
                }
                else
                    wantAvoid = false;
            }
        }

        // Confirm obstacle and commit to one dodge direction to avoid jitter
        if (wantAvoid)
        {
            _obstacleInPathConfirmCount++;
            _avoidanceClearCount = 0;
            if (_obstacleInPathConfirmCount >= obstacleConfirmFrames)
            {
                _isAvoiding = true;
                if (_obstacleInPathConfirmCount == obstacleConfirmFrames)
                    _avoidanceDirection = avoidDir; // Set direction once when we confirm; keep it stable
                _avoidanceUrgency = avoidUrg;
            }
        }
        else
        {
            _obstacleInPathConfirmCount = 0;
            if (_isAvoiding)
            {
                _avoidanceClearCount++;
                if (_avoidanceClearCount >= avoidancePersistClearFrames)
                    _isAvoiding = false;
            }
            else
                _avoidanceClearCount = 0;
        }
    }

    private void UpdateRoadBoundaryDetection()
    {
        if (!enableRoadBoundaryDetection)
        {
            _roadCorrectionInput = 0f;
            return;
        }

        Vector3 pos = transform.position;
        Vector3 forward = _currentForward;
        forward.y = 0f;
        forward.Normalize();
        if (forward.sqrMagnitude < 0.01f) forward = transform.forward;

        Vector3 right = Vector3.Cross(Vector3.up, forward).normalized;

        float rayHeight = 0.3f;
        float rayDownDist = raycastStartHeight + raycastDownDistance;
        Vector3 rayOrigin = pos + Vector3.up * rayHeight;

        // Get track center position at our current distance
        SampleAlongPath(_dist, out Vector3 trackCenter, out Vector3 trackForward);
        _trackCenterAtDist = trackCenter;

        // Detect if we're on road (for off-track recovery; no teleport)
        _isOnRoad = roadLayer.value != 0 && Physics.Raycast(rayOrigin, Vector3.down, rayDownDist, roadLayer, QueryTriggerInteraction.Ignore);

        // Calculate how far we are from track center (lateral offset)
        Vector3 toUs = pos - trackCenter;
        toUs.y = 0f;
        Vector3 trackRight = Vector3.Cross(Vector3.up, trackForward).normalized;
        _distanceFromTrackCenter = Vector3.Dot(toUs, trackRight);

        float halfRoadWidth = GetHalfRoadWidth();

        // Cast rays to detect road edges on left and right

        // Left edge detection
        _leftEdgeDetected = false;
        _leftEdgeDistance = roadEdgeDetectionWidth;
        for (float offset = 0.5f; offset <= roadEdgeDetectionWidth; offset += 0.5f)
        {
            Vector3 checkPos = rayOrigin - right * offset;
            if (!Physics.Raycast(checkPos, Vector3.down, out RaycastHit hit, rayDownDist, roadLayer, QueryTriggerInteraction.Ignore))
            {
                _leftEdgeDetected = true;
                _leftEdgeDistance = offset;
                break;
            }

            if (drawRoadBoundaryDebug)
                Debug.DrawLine(checkPos, hit.point, Color.cyan);
        }

        // Right edge detection
        _rightEdgeDetected = false;
        _rightEdgeDistance = roadEdgeDetectionWidth;
        for (float offset = 0.5f; offset <= roadEdgeDetectionWidth; offset += 0.5f)
        {
            Vector3 checkPos = rayOrigin + right * offset;
            if (!Physics.Raycast(checkPos, Vector3.down, out RaycastHit hit, rayDownDist, roadLayer, QueryTriggerInteraction.Ignore))
            {
                _rightEdgeDetected = true;
                _rightEdgeDistance = offset;
                break;
            }

            if (drawRoadBoundaryDebug)
                Debug.DrawLine(checkPos, hit.point, Color.cyan);
        }

        // Calculate road correction steering
        _roadCorrectionInput = 0f;

        // Method 1: Edge-based correction (steer away from detected edges)
        if (_leftEdgeDetected && _leftEdgeDistance < roadEdgeSoftMargin)
        {
            float urgency = 1f - (_leftEdgeDistance / roadEdgeSoftMargin);
            _roadCorrectionInput += urgency * roadCorrectionStrength; // Steer right

            if (drawRoadBoundaryDebug)
                Debug.DrawRay(pos + Vector3.up, right * 2f, Color.magenta);
        }

        if (_rightEdgeDetected && _rightEdgeDistance < roadEdgeSoftMargin)
        {
            float urgency = 1f - (_rightEdgeDistance / roadEdgeSoftMargin);
            _roadCorrectionInput -= urgency * roadCorrectionStrength; // Steer left

            if (drawRoadBoundaryDebug)
                Debug.DrawRay(pos + Vector3.up, -right * 2f, Color.magenta);
        }

        // Method 2: Center-seeking (pull toward track center) - always apply for tighter turns
        float normalizedOffset = halfRoadWidth > 0.01f ? (_distanceFromTrackCenter / halfRoadWidth) : 0f;
        float centerStrength = 0.65f * roadCorrectionStrength; // stronger so 1.0 actually pulls hard
        float centerCorrection = -normalizedOffset * centerStrength;
        _roadCorrectionInput += centerCorrection;

        _roadCorrectionInput = Mathf.Clamp(_roadCorrectionInput, -1f, 1f);

        if (drawRoadBoundaryDebug)
        {
            // Draw track center reference
            Debug.DrawLine(trackCenter, trackCenter + Vector3.up * 2f, Color.green);
            Debug.DrawLine(pos, trackCenter, Color.yellow);
        }
    }

    private void UpdateSteering(float dt)
    {
        // Base direction: track ahead (track-only, no A*)
        Vector3 desiredDirection = GetDesiredDirection();

        // Off track: steer strongly toward track center to drive back on
        if (!_isOnRoad && offTrackRecoveryStrength > 0f)
        {
            Vector3 toTrack = _trackCenterAtDist - transform.position;
            toTrack.y = 0f;
            if (toTrack.sqrMagnitude > 0.5f)
            {
                toTrack.Normalize();
                desiredDirection = Vector3.Slerp(desiredDirection, toTrack, offTrackRecoveryStrength);
            }
        }

        // When avoiding: use avoidance direction fully – no blend with track (track can point at obstacle)
        float targetBlend = _isAvoiding ? 1f : 0f; // Always 100% override when avoiding
        float blendSpeed = _isAvoiding ? avoidanceBlendSpeed * 8f : avoidanceBlendSpeed * 4f;
        _avoidanceBlend = Mathf.MoveTowards(_avoidanceBlend, targetBlend, blendSpeed * dt);

        Vector3 targetDirection = _avoidanceBlend > 0.02f
            ? Vector3.Slerp(desiredDirection, _avoidanceDirection, _avoidanceBlend)
            : desiredDirection;
        targetDirection.y = 0f;
        targetDirection.Normalize();

        // Angle error: how much we're turned away from target (track/desired direction)
        float angleToTarget = Vector3.SignedAngle(_currentForward, targetDirection, Vector3.up);

        // When avoiding: full steering lock so we actually turn away; no dead zone or predictive taper
        float rawSteering;
        if (_avoidanceBlend > 0.15f)
        {
            rawSteering = Mathf.Clamp(angleToTarget / 25f, -1f, 1f); // Direct steering toward avoidance direction
        }
        else if (Mathf.Abs(angleToTarget) <= steeringDeadZoneDeg)
        {
            rawSteering = 0f;
        }
        else
        {
            float speedFactor = Mathf.InverseLerp(0f, turnRateFalloffSpeed, _currentSpeed);
            float currentTurnRate = Mathf.Lerp(baseTurnRate, minTurnRate, speedFactor);
            float turnCapacity = currentTurnRate * alignmentTime;
            if (turnCapacity < 1f) turnCapacity = 1f;
            float idealSteering = angleToTarget / turnCapacity;
            rawSteering = Mathf.Clamp(idealSteering, -1f, 1f);
            float angleRate = (angleToTarget - _prevAngleToTarget) / Mathf.Max(dt, 0.001f);
            bool alreadyCorrecting = (angleToTarget > 0f && angleRate < 0f) || (angleToTarget < 0f && angleRate > 0f);
            if (steeringDamping > 0f && alreadyCorrecting)
                rawSteering *= (1f - steeringDamping);
        }
        _prevAngleToTarget = angleToTarget;

        _currentSteeringInput = rawSteering;

        // When avoiding, ignore road correction so we don't steer back into the obstacle
        if (enableRoadBoundaryDetection && _avoidanceBlend < 0.1f && Mathf.Abs(_roadCorrectionInput) > 0.05f)
        {
            float correctionWeight = Mathf.Abs(_roadCorrectionInput);
            _currentSteeringInput = Mathf.Lerp(_currentSteeringInput, _roadCorrectionInput, correctionWeight);
        }

        _currentSteeringInput = Mathf.Clamp(_currentSteeringInput, -1f, 1f);

        // When avoiding, react almost instantly (no smooth delay) so we actually turn in time
        float effectiveSmoothing = (_avoidanceBlend > 0.35f) ? steeringSmoothing * 0.12f : steeringSmoothing;
        _smoothedSteeringInput = Mathf.SmoothDamp(_smoothedSteeringInput, _currentSteeringInput,
                                                   ref _steeringVelocity, effectiveSmoothing);

        if (drawSteeringDebug)
        {
            Debug.DrawRay(transform.position + Vector3.up, desiredDirection * 3f, Color.blue);
            Debug.DrawRay(transform.position + Vector3.up, targetDirection * 3f, Color.yellow);
            Debug.DrawRay(transform.position + Vector3.up, _currentForward * 3f, Color.white);
        }
    }

    private void UpdateMovement(float dt)
    {
        // Calculate speed-dependent turn rate
        float speedFactor = Mathf.InverseLerp(0f, turnRateFalloffSpeed, _currentSpeed);
        float currentTurnRate = Mathf.Lerp(baseTurnRate, minTurnRate, speedFactor);

        // Apply steering to rotation
        float turnAmount = _smoothedSteeringInput * currentTurnRate * dt;
        _currentForward = Quaternion.Euler(0f, turnAmount, 0f) * _currentForward;
        _currentForward.y = 0f;
        _currentForward.Normalize();

        // Update target speed
        _targetSpeed = speed;

        // Reduce speed when off track so NPC can steer back instead of overshooting
        if (!_isOnRoad && offTrackRecoveryStrength > 0f)
            _targetSpeed *= 0.7f;

        // Reduce speed during avoidance
        if (slowDownOnAvoidance && _avoidanceBlend > 0.01f)
        {
            float speedReduction = Mathf.Lerp(1f, avoidanceSpeedMultiplier, _avoidanceBlend * _avoidanceUrgency);
            _targetSpeed *= speedReduction;
        }

        // Smooth speed toward target (reduces jitter from abrupt speed changes)
        float maxSpeedPerSecond = Mathf.Max(accelerationRate, decelerationRate);
        _currentSpeed = Mathf.SmoothDamp(_currentSpeed, _targetSpeed, ref _speedVelocity, speedSmoothing, maxSpeedPerSecond);

        // Move the car
        Vector3 movement = _currentForward * _currentSpeed * dt;
        Vector3 newPosition = transform.position + movement;

        // Validate movement: allow only on driveable (road or road+grass). No teleport - car must drive back.
        LayerMask moveCheckLayers = driveableLayer.value != 0 ? driveableLayer : roadLayer;
        if (validateMovementOnRoad && moveCheckLayers.value != 0)
        {
            Vector3 checkOrigin = newPosition + Vector3.up * raycastStartHeight;
            if (!Physics.Raycast(checkOrigin, Vector3.down, raycastStartHeight + raycastDownDistance, moveCheckLayers, QueryTriggerInteraction.Ignore))
            {
                // Not on driveable (e.g. void) - don't move, slow down; steering will try to bring us back
                _currentSpeed *= 0.92f;
                return;
            }
            // On driveable (road or grass): allow move; off-track recovery steering will steer back to track
        }

        // Ground snap: set height from raycast so interpolated position is correct
        if (RaycastGround(newPosition, out RaycastHit groundHit))
        {
            newPosition.y = groundHit.point.y + _pivotToBottom + groundClearance;
            _groundNormal = groundHit.normal;
        }

        // Use Rigidbody move so Unity interpolates between fixed steps (smooth visuals)
        if (_rb != null)
        {
            _rb.MovePosition(newPosition);

            if (_currentSpeed > minSpeedForRotation)
            {
                Quaternion targetRot = Quaternion.LookRotation(_currentForward, Vector3.up);
                if (alignToGround && _groundNormal != Vector3.up)
                {
                    Vector3 groundForward = Vector3.ProjectOnPlane(_currentForward, _groundNormal).normalized;
                    if (groundForward.sqrMagnitude > 0.01f)
                        targetRot = Quaternion.LookRotation(groundForward, _groundNormal);
                }
                _rb.MoveRotation(targetRot);
            }
        }
        else
        {
            transform.position = newPosition;
            if (_currentSpeed > minSpeedForRotation)
            {
                Quaternion targetRot = Quaternion.LookRotation(_currentForward, Vector3.up);
                if (alignToGround && _groundNormal != Vector3.up)
                {
                    Vector3 groundForward = Vector3.ProjectOnPlane(_currentForward, _groundNormal).normalized;
                    if (groundForward.sqrMagnitude > 0.01f)
                        targetRot = Quaternion.LookRotation(groundForward, _groundNormal);
                }
                transform.rotation = targetRot;
            }
        }
    }

    private Vector3 GetDesiredDirection()
    {
        // Track-only: direction along the track at look-ahead distance (no A*)
        SampleAlongPath(_dist + trackLookAhead, out Vector3 _, out Vector3 trackFwd);
        trackFwd.y = 0f;
        if (trackFwd.sqrMagnitude < 0.01f) trackFwd = _currentForward;
        return trackFwd.normalized;
    }

    // ============================================
    // A* DESTINATION (advisory only now)
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

        float dynThreshold = Mathf.Clamp(speed * destinationUpdateInterval * 0.6f, 0.15f, destinationMoveThreshold);
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
        _targetSpeed = speed;
        _currentSpeed = speed * 0.5f; // Start at half speed for smoother spawn

        // Disable A* movement and pathfinding (we use track + obstacle avoidance only)
        if (_ai != null)
        {
            _ai.canSearch = false;
            _ai.canMove = false;
        }
        if (_richAI != null)
        {
            _richAI.canMove = false;
            _richAI.enableRotation = false;
        }

        // Compute pivot to bottom
        ComputePivotToBottom();

        // Find starting distance on track
        _dist = GetDistanceAlongPath(transform.position);

        // Initialize forward direction from track
        SampleAlongPath(_dist, out Vector3 _, out Vector3 trackFwd);
        _currentForward = trackFwd;
        _currentForward.y = 0f;
        _currentForward.Normalize();
        if (_currentForward.sqrMagnitude < 0.01f)
            _currentForward = transform.forward;

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

        _initialized = true;

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
        _surfaceCheckTimer -= dt;
        if (_surfaceCheckTimer <= 0f)
        {
            _surfaceCheckTimer = surfaceCheckInterval;
            CheckSurfaceUnderCar();
        }

        if (_boostEndTime > 0f && Time.time > _boostEndTime)
        {
            _boostEndTime = 0f;
            _onBoostPad = false;
        }

        _currentSpeedMultiplier = Mathf.Lerp(_currentSpeedMultiplier, _targetSpeedMultiplier, surfaceSpeedLerpRate * dt);

        float newSpeed = _baseSpeed * _currentSpeedMultiplier;

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

        LayerMask checkLayers = surfaceDetectionLayers.value != 0 ? surfaceDetectionLayers : roadLayer;

        if (Physics.Raycast(origin, Vector3.down, out RaycastHit hit, maxDist, checkLayers, QueryTriggerInteraction.Collide))
        {
            GroundSurface surface = hit.collider.GetComponent<GroundSurface>();
            if (surface == null)
                surface = hit.collider.GetComponentInParent<GroundSurface>();

            if (surface != null)
            {
                ApplySurfaceEffects(surface);
            }
            else
            {
                _targetSpeedMultiplier = 1f;
                _currentSurfaceType = "Normal";
            }
        }
    }

    private void ApplySurfaceEffects(GroundSurface surface)
    {
        _currentSurfaceType = surface.surfaceType.ToString();

        _targetSpeedMultiplier = Mathf.Clamp(surface.maxSpeedMultiplier, 0.1f, 5f);

        bool isBoostPad = surface.maxSpeedMultiplier > boostPadThreshold &&
                          surface.accelerationMultiplier > boostPadThreshold;

        if (isBoostPad && !_onBoostPad)
        {
            _onBoostPad = true;
            _boostEndTime = Time.time + boostPadDuration;

            if (verboseDebug)
                Debug.Log($"[NPCTrafficCar] BOOST PAD! Speed: {speed:F1} -> {speed + boostPadSpeedBonus:F1}");
        }
        else if (!isBoostPad)
        {
            _onBoostPad = false;
        }
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
        LayerMask groundLayers = driveableLayer.value != 0 ? driveableLayer : roadLayer;
        if (groundLayers.value == 0) groundLayers = roadLayer;
        return Physics.Raycast(origin, Vector3.down, out hit, maxDist, groundLayers, QueryTriggerInteraction.Ignore);
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

    private void ConvertToPhysics(Vector3 impactDir, float impactSpeed)
    {
        if (_rb == null) return;

        _rb.isKinematic = false;
        _rb.useGravity = true;
        _rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

        Vector3 velocity = _lastVelocity;
        if (velocity.magnitude < minTransferVelocity)
            velocity = _currentForward * _currentSpeed;

        Vector3 bounceDir = -impactDir;
        bounceDir.y = 0f;
        if (bounceDir.sqrMagnitude < 0.01f) bounceDir = -transform.forward;
        bounceDir.Normalize();

        velocity += bounceDir * crashBounceBack;
        velocity += Vector3.up * crashBounceUp;

        _rb.velocity = velocity;

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
        if (_crashCut != null) return;

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
            GameObject go = new GameObject("Crash_NavmeshCut");
            go.transform.SetParent(transform, false);
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;

            _crashCut = go.AddComponent<NavmeshCut>();

            _crashCut.type = NavmeshCut.MeshType.Box;
            _crashCut.center = Vector3.zero;

            _crashCut.rectangleSize = new Vector2(crashCutBoxSize.x, crashCutBoxSize.z);
            _crashCut.height = crashCutBoxSize.y;

            _crashCut.updateDistance = crashCutUpdateDistance;
            _crashCut.useRotationAndScale = crashCutUseRotationAndScale;
            _crashCut.updateRotationDistance = crashCutUpdateRotationDistance;

            _crashCut.cutsAddedGeom = crashCutCutsAddedGeometry;
            _crashCut.radiusExpansionMode = NavmeshCut.RadiusExpansionMode.DontExpand;
            _crashCut.graphMask = GraphMask.everything;
        }

        _crashCut.enabled = true;
        _crashCut.ForceUpdate();
    }

    private void UpdateEngineAudio()
    {
        if (_engineSource == null) return;

        float speedNorm = Mathf.Clamp01(_currentSpeed / speedRange.y);
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
    public float CurrentSpeed => _currentSpeed;
    public bool IsAvoiding => _isAvoiding;
    public float AvoidanceUrgency => _avoidanceUrgency;

    public void ForceCrashFromForcefield(Vector3 worldImpactFrom, float impactSpeed, Collider source)
    {
        if (_crashed) return;

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
        if (!drawDestinationGizmo) return;

        // Draw track look-ahead point (driving target)
        if (_path != null && _path.Count >= 2 && _cumLengths != null)
        {
            float lookDist = Mathf.Min(_dist + trackLookAhead, _totalLength);
            SampleAlongPath(lookDist, out Vector3 lookPos, out Vector3 _);
            Gizmos.color = Color.green;
            Gizmos.DrawWireSphere(lookPos, 0.8f);
            Gizmos.DrawLine(transform.position, lookPos);
        }

        if (_isAvoiding)
        {
            Gizmos.color = Color.Lerp(Color.yellow, Color.red, _avoidanceUrgency);
            Gizmos.DrawWireSphere(transform.position + Vector3.up * 2f, 0.5f);
        }
    }
}