using System;
using System.Collections.Generic;
using UnityEngine;
using Pathfinding;

/// <summary>
/// NPC traffic car: drives forward along the procedural track and dodges obstacles.
/// Track-only steering with ray-based obstacle avoidance.
/// Fan/lane probes find valid on-track paths; steering follows track and commits to stable dodge directions.
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
    // SPEED
    // ============================================
    [Header("Speed")]
    [SerializeField] private float speed = 12f;
    [SerializeField] private bool randomizeSpeed = true;
    [SerializeField] private Vector2 speedRange = new Vector2(8f, 18f);

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

    [Tooltip("How quickly to blend into avoidance steering.")]
    [SerializeField] private float avoidanceBlendSpeed = 14f;

    [Tooltip("Angle of the center 'danger zone' – any hit in this cone triggers full left/right avoidance (wider = react to more obstacles).")]
    [SerializeField] private float dangerZoneAngle = 34f;

    [Tooltip("Start gentle avoidance when side obstacles are within this distance.")]
    [SerializeField] private float sideObstacleSoftDistance = 12f;

    [Tooltip("Height offset for obstacle detection rays.")]
    [SerializeField] private float obstacleRayHeight = 0.5f;

    [Tooltip("Push ray origin forward from car pivot to avoid self-hits and disappearing debug rays.")]
    [SerializeField, Min(0f)] private float obstacleRayForwardOffset = 0.8f;

    [Header("Hitbox-Aware Avoidance Sphere")]
    [Tooltip("Extra clearance (meters) to maintain between NPC hitbox sphere and obstacles.")]
    [SerializeField, Min(0f)] private float avoidanceClearanceDistance = 0.9f;

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
    [Tooltip("Legacy: only used if lane-probe exit evaluation is enabled for clearing.")]
    [SerializeField, Min(2)] private int avoidancePersistClearFrames = 6;

    [Tooltip("Consecutive frames with no avoidance request before dropping dodge state (resume track).")]
    [SerializeField, Min(1)] private int avoidanceExitWhenForwardClearFrames = 2;

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
    private float _pivotToBottom;
    private float _avoidanceSphereRadius;

    // ============================================
    // INTERNALS - Components
    // ============================================
    private Rigidbody _rb;
    private Collider _col;
    private AudioSource _engineSource;
    private IAstarAI _ai;
    private RichAI _richAI;
    private NavmeshCut _crashCut;
    private readonly Collider[] _avoidanceOverlapHits = new Collider[24];

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
    private Vector3 _rayPathDirection;     // Preferred non-blocked ray direction for pathing
    private float _rayPathConfidence;      // Legacy; kept 0 — heading uses track unless _applyRayPathToHeading
    private bool _applyRayPathToHeading;    // True only when dodging: obstacle blocking + at least one clear ray
    private bool _isAvoiding;
    private int _obstacleInPathConfirmCount;  // Frames we've seen obstacle in path
    private int _avoidanceClearCount;         // Frames path has been clear (while we were avoiding)
    private int _committedObstacleId = -1;    // Obstacle we are currently committed to dodge
    private int _committedObstacleMissingFrames;

    [Header("Avoidance Stabilization")]
    [Tooltip("While avoiding, update the dodge direction when new avoidance evidence differs by more than this angle.")]
    [SerializeField, Range(1f, 45f)] private float avoidanceDirectionUpdateAngleDeg = 12f;

    [Tooltip("While avoiding, update the dodge direction when new urgency spikes above this factor.")]
    [SerializeField, Range(1f, 1.5f)] private float avoidanceUrgencyUpdateFactor = 1.12f;

    [Tooltip("How many frames a committed obstacle can be missed before we allow switching commit.")]
    [SerializeField, Min(1)] private int committedObstacleMissingGraceFrames = 3;

    [Tooltip("Seconds ahead to anticipate collision for early, wider avoidance.")]
    [SerializeField, Range(0.15f, 1.5f)] private float avoidancePredictionTime = 0.55f;

    // For gizmos: world position of the obstacle that is currently driving avoidance.
    private Vector3 _avoidanceObstaclePoint;
    private bool _avoidanceObstaclePointValid;

    [Header("Avoidance Exit Hysteresis")]
    [Tooltip("While avoiding, only stop avoidance when the center probe lane is clear by at least this fraction of ray range.")]
    [SerializeField, Range(0.8f, 1f)] private float avoidanceExitLaneCenterClearFraction = 0.98f;

    [Tooltip("While avoiding (fan rays), only stop avoidance when both side distances are clear by this multiplier.")]
    [SerializeField, Range(1f, 2f)] private float avoidanceExitSoftDistanceMultiplier = 1.25f;

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
    private Vector3 _lastVelocity;
    private Vector3 _groundNormal = Vector3.up;

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
        _ai = GetComponent<IAstarAI>();
        _richAI = GetComponent<RichAI>();

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
        _currentSpeed = 0f;
        _currentForward = transform.forward;
        _currentSteeringInput = 0f;
        _smoothedSteeringInput = 0f;
        _prevAngleToTarget = 0f;
        _avoidanceUrgency = 0f;
        _avoidanceBlend = 0f;
        _rayPathDirection = transform.forward;
        _rayPathConfidence = 0f;
        _applyRayPathToHeading = false;
        _isAvoiding = false;
        _obstacleInPathConfirmCount = 0;
        _avoidanceClearCount = 0;
        _committedObstacleId = -1;
        _committedObstacleMissingFrames = 0;

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
        _avoidanceSphereRadius = 0f;
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
            _rayPathDirection = _currentForward.sqrMagnitude > 0.01f ? _currentForward : transform.forward;
            _rayPathConfidence = 0f;
            _applyRayPathToHeading = false;
            _isAvoiding = false;
            _obstacleInPathConfirmCount = 0;
            _avoidanceClearCount = 0;
            _avoidanceObstaclePointValid = false;
            _committedObstacleId = -1;
            _committedObstacleMissingFrames = 0;
            return;
        }

        Vector3 origin = transform.position + Vector3.up * obstacleRayHeight;
        Vector3 forward = _currentForward;
        forward.y = 0f;
        forward.Normalize();
        if (forward.sqrMagnitude < 0.01f)
            forward = transform.forward;
        origin += forward * obstacleRayForwardOffset;

        Vector3 right = Vector3.Cross(Vector3.up, forward).normalized;
        float range = obstacleDetectionRange;
        float effectiveDangerZoneAngle = Mathf.Max(10f, dangerZoneAngle);
        float effectiveSideSoftDistance = Mathf.Max(2.5f, sideObstacleSoftDistance);
        float dynamicReactionDistance = Mathf.Clamp(
            _currentSpeed * Mathf.Max(0.2f, avoidancePredictionTime) + pathHalfWidthForAvoidance * 2.2f,
            1.5f,
            Mathf.Max(2f, range));
        float hitboxClearance = Mathf.Max(0.1f, _avoidanceSphereRadius + avoidanceClearanceDistance);
        // Clearance is an early-planning buffer: react sooner, not with a late snap.
        dynamicReactionDistance = Mathf.Min(range, dynamicReactionDistance + hitboxClearance * 0.6f);

        bool usedLaneProbeForExitEval = false;
        float centerClearDistForExit = range;
        bool centerBlockedForExitEval = false;
        float leftClearDistForExit = range * 2f;
        float rightClearDistForExit = range * 2f;

        SampleAlongPath(_dist, out Vector3 trackCenter, out Vector3 trackFwd);
        trackFwd.y = 0f;
        if (trackFwd.sqrMagnitude < 0.01f) trackFwd = forward;
        trackFwd.Normalize();
        Vector3 trackRight = Vector3.Cross(Vector3.up, trackFwd).normalized;

        Vector3 avoidanceTriggerPoint = transform.position;
        bool avoidanceTriggerPointValid = false;

        bool IsHitInPath(Vector3 hitPoint)
        {
            float dynamicPathHalfWidth = Mathf.Max(pathHalfWidthForAvoidance, obstacleDetectionWidth * 0.55f);
            dynamicPathHalfWidth += Mathf.Clamp01(_currentSpeed / 30f) * 0.55f;

            float hitLateralTrack = Vector3.Dot(hitPoint - trackCenter, trackRight);
            bool withinTrackBand = Mathf.Abs(hitLateralTrack - _distanceFromTrackCenter) <= dynamicPathHalfWidth;

            Vector3 toHit = hitPoint - origin;
            float lateralCar = Vector3.Dot(toHit, right);
            bool withinCarBand = Mathf.Abs(lateralCar) <= dynamicPathHalfWidth;

            return withinTrackBand || withinCarBand;
        }

        bool IsRoadAtPoint(Vector3 point)
        {
            if (roadLayer.value == 0) return true;
            Vector3 checkOrigin = point + Vector3.up * Mathf.Max(0.8f, raycastStartHeight * 0.6f);
            float checkDist = Mathf.Max(2f, raycastStartHeight + raycastDownDistance);
            return Physics.Raycast(checkOrigin, Vector3.down, checkDist, roadLayer, QueryTriggerInteraction.Ignore);
        }

        // Clamp each ray to drivable road length so side rays stop at road edge.
        float GetRoadLimitedRayDistance(Vector3 rayStart, Vector3 rayDir, float maxDist)
        {
            if (roadLayer.value == 0) return maxDist;

            const float step = 0.6f;
            float dist = step;
            float lastOnRoad = 0f;
            while (dist <= maxDist)
            {
                Vector3 p = rayStart + rayDir * dist;
                if (!IsRoadAtPoint(p))
                    break;
                lastOnRoad = dist;
                dist += step;
            }

            // If all sampled points are still on road, full length is valid.
            if (lastOnRoad >= maxDist - step * 0.5f)
                return maxDist;

            return Mathf.Max(0f, lastOnRoad);
        }

        bool wantAvoid = false;
        Vector3 avoidDir = forward;
        float avoidUrg = 0f;
        int chosenObstacleId = -1;
        bool committedObstacleSeenThisFrame = false;

        float halfAngle = obstacleDetectionAngle * 0.5f;
        float angleStep = (obstacleRayCount > 1) ? (obstacleDetectionAngle / (obstacleRayCount - 1)) : 0f;
        float halfDangerZone = effectiveDangerZoneAngle * 0.5f;

        float bestGreenScore = float.MinValue;
        Vector3 bestGreenDir = forward;
        bool hasBestGreenDir = false;
        int greenRayCount = 0;
        int obstacleRayHitCount = 0; // rays that hit a non-self obstacle (for gating ray-based heading)
        int greenLeftCount = 0;
        int greenRightCount = 0;

        float closestCenterHit = float.MaxValue;
        float closestLeftHit = float.MaxValue;
        float closestRightHit = float.MaxValue;
        Vector3 closestCenterHitPoint = Vector3.zero;
        Vector3 closestLeftHitPoint = Vector3.zero;
        Vector3 closestRightHitPoint = Vector3.zero;
        bool hasClosestCenterHitPoint = false;
        bool hasClosestLeftHitPoint = false;
        bool hasClosestRightHitPoint = false;
        bool centerBlocked = false;

        for (int i = 0; i < obstacleRayCount; i++)
        {
            float angle = -halfAngle + angleStep * i;
            Vector3 rayDir = Quaternion.Euler(0f, angle, 0f) * forward;
            bool isLeftSide = angle < -halfDangerZone;
            bool isRightSide = angle > halfDangerZone;
            bool isDanger = !isLeftSide && !isRightSide;
            float roadLimitDist = GetRoadLimitedRayDistance(origin, rayDir, range);
            if (roadLimitDist <= 0.05f)
            {
                if (drawObstacleRays)
                    Debug.DrawRay(origin, rayDir * 0.2f, Color.yellow);
                continue;
            }

            if (Physics.Raycast(origin, rayDir, out RaycastHit hit, roadLimitDist, obstacleDetectionLayers, QueryTriggerInteraction.Ignore))
            {
                if (hit.collider.transform.IsChildOf(transform))
                {
                    greenRayCount++;
                    if (isLeftSide) greenLeftCount++;
                    else if (isRightSide) greenRightCount++;
                    if (drawObstacleRays)
                        Debug.DrawRay(origin, rayDir * roadLimitDist, Color.green);
                    continue;
                }

                obstacleRayHitCount++;

                bool inPath = IsHitInPath(hit.point);
                float effHitDist = Mathf.Max(0f, hit.distance - hitboxClearance);

                if (inPath)
                {
                    if (_committedObstacleId != -1 && hit.collider.GetInstanceID() == _committedObstacleId)
                        committedObstacleSeenThisFrame = true;

                    if (isDanger)
                    {
                        centerBlocked = centerBlocked || effHitDist <= dynamicReactionDistance;
                        if (effHitDist < closestCenterHit)
                        {
                            closestCenterHit = effHitDist;
                            closestCenterHitPoint = hit.point;
                            hasClosestCenterHitPoint = true;
                        }
                        chosenObstacleId = hit.collider != null ? hit.collider.GetInstanceID() : chosenObstacleId;
                    }
                    else if (isLeftSide)
                    {
                        if (effHitDist < closestLeftHit)
                        {
                            closestLeftHit = effHitDist;
                            closestLeftHitPoint = hit.point;
                            hasClosestLeftHitPoint = true;
                        }
                        if (chosenObstacleId == -1 && hit.collider != null)
                            chosenObstacleId = hit.collider.GetInstanceID();
                    }
                    else if (isRightSide)
                    {
                        if (effHitDist < closestRightHit)
                        {
                            closestRightHit = effHitDist;
                            closestRightHitPoint = hit.point;
                            hasClosestRightHitPoint = true;
                        }
                        if (chosenObstacleId == -1 && hit.collider != null)
                            chosenObstacleId = hit.collider.GetInstanceID();
                    }
                }

                if (drawObstacleRays)
                    Debug.DrawLine(origin, hit.point, inPath ? (isDanger ? Color.red : Color.yellow) : new Color(1f, 0.6f, 0f));
            }
            else
            {
                Vector3 rayEnd = origin + rayDir * roadLimitDist;
                greenRayCount++;
                if (isLeftSide) greenLeftCount++;
                else if (isRightSide) greenRightCount++;

                // Pick the clearest path: prefer longest on-road reach, then endpoint nearest track center,
                // then slightly prefer rays aligned with track forward (stable on curves).
                float halfW = Mathf.Max(0.35f, GetHalfRoadWidth());
                float lateralFromCenter = Mathf.Abs(Vector3.Dot(rayEnd - trackCenter, trackRight));
                float centerScore = 1f - Mathf.Clamp01(lateralFromCenter / halfW);
                float alignTrack = Mathf.Clamp01((Vector3.Dot(rayDir.normalized, trackFwd) + 1f) * 0.5f);
                float score = roadLimitDist * 6f + centerScore * 2.75f + alignTrack * 0.4f;
                if (score > bestGreenScore)
                {
                    bestGreenScore = score;
                    bestGreenDir = rayDir;
                    hasBestGreenDir = true;
                }

                if (drawObstacleRays)
                    Debug.DrawRay(origin, rayDir * roadLimitDist, Color.green);
            }
        }

        if (closestLeftHit == float.MaxValue) closestLeftHit = range * 2f;
        if (closestRightHit == float.MaxValue) closestRightHit = range * 2f;

        _rayPathDirection = hasBestGreenDir ? bestGreenDir.normalized : forward;
        _rayPathConfidence = 0f;

        usedLaneProbeForExitEval = false;
        centerBlockedForExitEval = centerBlocked;
        leftClearDistForExit = closestLeftHit;
        rightClearDistForExit = closestRightHit;

        // Clearest path = best open ray; fallback = step away from tighter side / track edges (no obstacle "lock-on").
        Vector3 ClearestPathAvoidDirection(float fallbackTurnDeg)
        {
            if (hasBestGreenDir && greenRayCount > 0)
                return bestGreenDir.normalized;

            bool leftHasRoom = _leftEdgeDistance >= minTrackRoomToDodge;
            bool rightHasRoom = _rightEdgeDistance >= minTrackRoomToDodge;
            bool preferLeft;
            if (!leftHasRoom && !rightHasRoom)
                preferLeft = _leftEdgeDistance >= _rightEdgeDistance;
            else if (!leftHasRoom)
                preferLeft = false;
            else if (!rightHasRoom)
                preferLeft = true;
            else if (greenLeftCount != greenRightCount)
                preferLeft = greenLeftCount > greenRightCount;
            else
                preferLeft = closestLeftHit >= closestRightHit;

            float turnSign = preferLeft ? -1f : 1f;
            float room = preferLeft ? _leftEdgeDistance : _rightEdgeDistance;
            float roomScale = Mathf.Lerp(0.65f, 1f, Mathf.Clamp01(room / 3.5f));
            return Quaternion.Euler(0f, turnSign * fallbackTurnDeg * roomScale, 0f) * forward;
        }

        void PickAvoidanceTriggerForSafety()
        {
            if (hasClosestCenterHitPoint)
            {
                avoidanceTriggerPoint = closestCenterHitPoint;
                avoidanceTriggerPointValid = true;
                return;
            }
            if (hasClosestLeftHitPoint && hasClosestRightHitPoint)
            {
                avoidanceTriggerPoint = closestLeftHit <= closestRightHit ? closestLeftHitPoint : closestRightHitPoint;
                avoidanceTriggerPointValid = true;
            }
            else if (hasClosestLeftHitPoint)
            {
                avoidanceTriggerPoint = closestLeftHitPoint;
                avoidanceTriggerPointValid = true;
            }
            else if (hasClosestRightHitPoint)
            {
                avoidanceTriggerPoint = closestRightHitPoint;
                avoidanceTriggerPointValid = true;
            }
        }

        if (centerBlocked)
        {
            wantAvoid = true;
            avoidDir = ClearestPathAvoidDirection(22f);
            avoidUrg = 0.72f;
            PickAvoidanceTriggerForSafety();
        }
        else
        {
            // Soft side reaction only while still "coupled" to a forward obstacle or a genuinely tight side pass.
            // Avoids steering away the whole time a side ray grazes something we've already cleared in front of.
            bool sideClose = closestLeftHit < effectiveSideSoftDistance || closestRightHit < effectiveSideSoftDistance;
            float minSideDist = Mathf.Min(closestLeftHit, closestRightHit);
            bool forwardStillRelevant = hasClosestCenterHitPoint &&
                                         closestCenterHit < Mathf.Max(dynamicReactionDistance * 2.4f, range * 0.48f);
            bool imminentSideScrape = minSideDist < effectiveSideSoftDistance * 0.52f;
            if (sideClose && (forwardStillRelevant || imminentSideScrape))
            {
                wantAvoid = true;
                avoidDir = ClearestPathAvoidDirection(14f);
                avoidUrg = 0.38f;
                PickAvoidanceTriggerForSafety();
            }
        }

        // Hitbox fallback: only trigger before commitment and never override with a sharp new turn.
        // It should reinforce buffer behavior, not cause a close-range "spurt".
        if (!_isAvoiding)
        {
            Vector3 sphereCenter = GetAvoidanceSphereCenter();
            float overlapRadius = Mathf.Max(0.2f, _avoidanceSphereRadius + avoidanceClearanceDistance);
            int overlapCount = Physics.OverlapSphereNonAlloc(
                sphereCenter,
                overlapRadius,
                _avoidanceOverlapHits,
                obstacleDetectionLayers,
                QueryTriggerInteraction.Ignore
            );

            if (overlapCount > 0)
            {
                Collider nearest = null;
                float nearestSq = float.MaxValue;
                Vector3 nearestPoint = sphereCenter;

                for (int i = 0; i < overlapCount; i++)
                {
                    Collider c = _avoidanceOverlapHits[i];
                    if (c == null || c.transform.IsChildOf(transform)) continue;
                    Vector3 p = c.ClosestPoint(sphereCenter);
                    float sq = (p - sphereCenter).sqrMagnitude;
                    if (sq < nearestSq)
                    {
                        nearestSq = sq;
                        nearest = c;
                        nearestPoint = p;
                    }
                }

                if (nearest != null)
                {
                    // If we already have an avoid direction from ray planning, keep it.
                    // If we don't, use the best planned path direction (ray/forward), not a panic away-vector.
                    if (!wantAvoid)
                    {
                        wantAvoid = true;
                        avoidDir = (_rayPathDirection.sqrMagnitude > 0.01f ? _rayPathDirection : forward).normalized;
                        avoidUrg = 0.62f;
                    }
                    else
                    {
                        avoidUrg = Mathf.Max(avoidUrg, 0.62f);
                    }

                    chosenObstacleId = nearest.GetInstanceID();
                    avoidanceTriggerPoint = nearestPoint;
                    avoidanceTriggerPointValid = true;
                }
            }
        }

        // Heading stays on the track unless we're dodging, at least one ray actually hits an obstacle,
        // and we still have an open corridor to aim for.
        _applyRayPathToHeading = wantAvoid && obstacleRayHitCount > 0 && hasBestGreenDir && greenRayCount > 0;

        // Safety clamp only when we have NO open-ray corridor — otherwise trust bestGreenDir (long clear paths
        // can still have a positive dot vs. a left-cluster trigger point; overriding caused wrong-way dodges).
        if (wantAvoid && avoidanceTriggerPointValid && !(hasBestGreenDir && greenRayCount > 0))
        {
            Vector3 toObstacle = avoidanceTriggerPoint - origin;
            toObstacle.y = 0f;
            if (toObstacle.sqrMagnitude > 0.01f)
            {
                toObstacle.Normalize();
                float towardDot = Vector3.Dot(avoidDir.normalized, toObstacle);
                if (towardDot > 0.15f)
                {
                    float obstacleSideSign = Mathf.Sign(Vector3.SignedAngle(forward, toObstacle, Vector3.up)); // +right, -left
                    if (Mathf.Abs(obstacleSideSign) < 0.1f)
                        obstacleSideSign = 1f;

                    float correctiveAngle = 22f;
                    avoidDir = Quaternion.Euler(0f, -obstacleSideSign * correctiveAngle, 0f) * forward;
                    avoidUrg = Mathf.Max(avoidUrg, 0.68f);
                }
            }
        }

        bool wasAvoiding = _isAvoiding;
        if (wantAvoid)
        {
            if (_committedObstacleId != -1)
            {
                if (committedObstacleSeenThisFrame)
                    _committedObstacleMissingFrames = 0;
                else
                    _committedObstacleMissingFrames++;
            }

            _obstacleInPathConfirmCount++;
            _avoidanceClearCount = 0;
            if (_obstacleInPathConfirmCount >= obstacleConfirmFrames)
            {
                _isAvoiding = true;

                if (!wasAvoiding)
                {
                    _avoidanceDirection = avoidDir;
                    _committedObstacleId = chosenObstacleId;
                    _committedObstacleMissingFrames = 0;
                }
                else
                {
                    bool canSwitchCommit = _committedObstacleId == -1 || _committedObstacleMissingFrames >= committedObstacleMissingGraceFrames;
                    bool hasNewObstacleToCommit = chosenObstacleId != -1 && chosenObstacleId != _committedObstacleId;

                    if (hasNewObstacleToCommit && canSwitchCommit)
                    {
                        _committedObstacleId = chosenObstacleId;
                        _committedObstacleMissingFrames = 0;
                    }

                    // Re-evaluate clearest path every frame while avoiding — don't freeze the last dodge vector
                    // (that was steering cars back into obstacles during recovery).
                    const float followClearestSlerp = 0.52f;
                    _avoidanceDirection = Vector3.Slerp(_avoidanceDirection, avoidDir, followClearestSlerp).normalized;
                    avoidUrg = Mathf.Lerp(_avoidanceUrgency, avoidUrg, 0.38f);
                }

                _avoidanceUrgency = avoidUrg;

                if (avoidanceTriggerPointValid)
                {
                    _avoidanceObstaclePoint = avoidanceTriggerPoint;
                    _avoidanceObstaclePointValid = true;
                }
            }
        }
        else
        {
            _obstacleInPathConfirmCount = 0;
            if (_isAvoiding)
            {
                // We're only in this branch when wantAvoid is false — the planner says no dodge is needed.
                // Drop avoidance after brief hysteresis instead of waiting for every ray to clear the object.
                bool isTrulyClear;
                if (usedLaneProbeForExitEval)
                {
                    isTrulyClear = centerClearDistForExit >= range * avoidanceExitLaneCenterClearFraction;
                }
                else
                {
                    isTrulyClear = true;
                }

                if (isTrulyClear)
                    _avoidanceClearCount++;
                else
                    _avoidanceClearCount = 0;

                int exitFramesNeeded = usedLaneProbeForExitEval
                    ? avoidancePersistClearFrames
                    : avoidanceExitWhenForwardClearFrames;

                if (_avoidanceClearCount >= exitFramesNeeded)
                {
                    _isAvoiding = false;
                    _avoidanceUrgency = 0f;
                    _avoidanceObstaclePointValid = false;
                    _committedObstacleId = -1;
                    _committedObstacleMissingFrames = 0;
                }
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

        // Blend into avoidance based on urgency so turns start earlier and feel less snap-like.
        float targetBlend = _isAvoiding ? Mathf.Lerp(0.55f, 0.92f, Mathf.Clamp01(_avoidanceUrgency)) : 0f;
        float blendSpeed = _isAvoiding ? avoidanceBlendSpeed * 2.6f : avoidanceBlendSpeed * 2.2f;
        _avoidanceBlend = Mathf.MoveTowards(_avoidanceBlend, targetBlend, blendSpeed * dt);

        Vector3 targetDirection = _avoidanceBlend > 0.02f
            ? Vector3.Slerp(desiredDirection, _avoidanceDirection, _avoidanceBlend)
            : desiredDirection;
        targetDirection.y = 0f;
        targetDirection.Normalize();

        // Angle error: how much we're turned away from target (track/desired direction)
        float angleToTarget = Vector3.SignedAngle(_currentForward, targetDirection, Vector3.up);

        // Use the same predictive steering model while avoiding (with shorter align time) to keep dodges smooth.
        float rawSteering;
        if (Mathf.Abs(angleToTarget) <= steeringDeadZoneDeg && _avoidanceBlend < 0.2f)
        {
            rawSteering = 0f;
        }
        else
        {
            float speedFactor = Mathf.InverseLerp(0f, turnRateFalloffSpeed, _currentSpeed);
            float currentTurnRate = Mathf.Lerp(baseTurnRate, minTurnRate, speedFactor);
            float avoidAlignScale = Mathf.Lerp(1f, 0.68f, _avoidanceBlend); // Smaller = stronger steering, still smooth
            float effectiveAlignmentTime = Mathf.Max(0.12f, alignmentTime * avoidAlignScale);
            float turnCapacity = currentTurnRate * effectiveAlignmentTime;
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

        // Keep some smoothing even in avoidance to prevent twitch.
        float effectiveSmoothing = (_avoidanceBlend > 0.35f) ? steeringSmoothing * 0.5f : steeringSmoothing;
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

            // Keep the collider turning even when we slow down for avoidance.
            // Otherwise the translation can change direction via _currentForward while the rigidbody rotation stays stale.
            if (_currentSpeed > minSpeedForRotation || _isAvoiding)
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
        // Default: pure track tangent — no ray steering unless an obstacle blocks and we have a clear corridor.
        SampleAlongPath(_dist + trackLookAhead, out Vector3 _, out Vector3 trackFwd);
        trackFwd.y = 0f;
        if (trackFwd.sqrMagnitude < 0.01f) trackFwd = _currentForward;
        trackFwd.Normalize();

        if (_applyRayPathToHeading && _rayPathDirection.sqrMagnitude > 0.01f)
        {
            const float headingRayBlend = 0.22f;
            Vector3 blended = Vector3.Slerp(trackFwd, _rayPathDirection, headingRayBlend);
            blended.y = 0f;
            if (blended.sqrMagnitude > 0.01f)
                return blended.normalized;
        }

        return trackFwd;
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
        ComputeAvoidanceSphereRadius();

        // Find starting distance on track
        _dist = GetDistanceAlongPath(transform.position);

        // Initialize forward direction from track
        SampleAlongPath(_dist, out Vector3 _, out Vector3 trackFwd);
        _currentForward = trackFwd;
        _currentForward.y = 0f;
        _currentForward.Normalize();
        if (_currentForward.sqrMagnitude < 0.01f)
            _currentForward = transform.forward;

        _initialized = true;

        if (verboseDebug)
            Debug.Log($"[NPCTrafficCar] Initialized: dist={_dist:F1}m, speed={speed:F1}");

        return true;
    }

    private void ComputePivotToBottom()
    {
        if (_col == null) return;

        Bounds b = _col.bounds;
        _pivotToBottom = transform.position.y - b.min.y;
        _pivotToBottom = Mathf.Max(0f, _pivotToBottom);
    }

    private void ComputeAvoidanceSphereRadius()
    {
        if (_col == null)
        {
            _avoidanceSphereRadius = 0.8f;
            return;
        }

        _avoidanceSphereRadius = Mathf.Max(0.3f, GetPlanarColliderRadius(_col));
    }

    private Vector3 GetAvoidanceSphereCenter()
    {
        if (_col != null) return _col.bounds.center;
        return transform.position + Vector3.up * obstacleRayHeight;
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
        InitializeIfNeeded();
    }

    // ============================================
    // DEBUG GIZMOS
    // ============================================

    private void OnDrawGizmos()
    {
        // Draw track look-ahead point (driving target)
        if (drawDestinationGizmo && _path != null && _path.Count >= 2 && _cumLengths != null)
        {
            float lookDist = Mathf.Min(_dist + trackLookAhead, _totalLength);
            SampleAlongPath(lookDist, out Vector3 lookPos, out Vector3 _);
            Gizmos.color = Color.green;
            Gizmos.DrawWireSphere(lookPos, 0.8f);
            Gizmos.DrawLine(transform.position, lookPos);
        }

        if (drawDestinationGizmo && _isAvoiding)
        {
            Gizmos.color = Color.Lerp(Color.yellow, Color.red, _avoidanceUrgency);
            Vector3 gizmoPos = _avoidanceObstaclePointValid ? _avoidanceObstaclePoint : (transform.position + Vector3.up * 2f);
            Gizmos.DrawWireSphere(gizmoPos, 0.5f);
        }

        // Hitbox-aware avoidance sphere debug (shares obstacle debug toggle).
        if (drawObstacleRays)
        {
            float baseRadius = Application.isPlaying ? Mathf.Max(0.1f, _avoidanceSphereRadius) : 0.8f;
            float clearRadius = Application.isPlaying ? Mathf.Max(baseRadius, baseRadius + avoidanceClearanceDistance) : 1.4f;
            Vector3 c = Application.isPlaying ? GetAvoidanceSphereCenter() : (transform.position + Vector3.up * 0.5f);

            Gizmos.color = new Color(0.2f, 0.8f, 1f, 0.7f);
            Gizmos.DrawWireSphere(c, baseRadius);

            Gizmos.color = new Color(1f, 0.65f, 0.15f, 0.7f);
            Gizmos.DrawWireSphere(c, clearRadius);
        }
    }
}