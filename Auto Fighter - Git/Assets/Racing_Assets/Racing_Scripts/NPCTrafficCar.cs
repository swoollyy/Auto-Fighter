using Pathfinding;
using Pathfinding.RVO;
using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// NPC traffic car that follows the procedural track using A* Pathfinding.
/// REWORKED: Uses proper vehicle steering physics instead of direct RVO velocity.
/// - RVO/A* provide advisory directions, not direct movement
/// - Steering is constrained by turn radius that increases with speed
/// - Forward obstacle detection triggers gradual steering, not instant teleport
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
    // A* PATHFINDING (now advisory only)
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
    // VEHICLE STEERING (NEW - replaces direct RVO control)
    // ============================================
    [Header("Vehicle Steering Physics")]
    [Tooltip("Base turn rate in degrees/second at low speed.")]
    [SerializeField] private float baseTurnRate = 120f;

    [Tooltip("Minimum turn rate at high speed (degrees/second).")]
    [SerializeField] private float minTurnRate = 25f;

    [Tooltip("Speed at which turn rate reaches minimum.")]
    [SerializeField] private float turnRateFalloffSpeed = 20f;

    [Tooltip("How much the car accelerates toward target speed.")]
    [SerializeField] private float accelerationRate = 15f;

    [Tooltip("How much the car decelerates (braking).")]
    [SerializeField] private float decelerationRate = 25f;

    [Tooltip("Smooth steering input changes over this time.")]
    [SerializeField] private float steeringSmoothing = 0.15f;

    // ============================================
    // OBSTACLE DETECTION (NEW - replaces instant RVO dodge)
    // ============================================
    [Header("Obstacle Detection")]
    [Tooltip("Layers to detect as obstacles for steering avoidance.")]
    [SerializeField] private LayerMask obstacleDetectionLayers;

    [Tooltip("How far ahead to scan for obstacles.")]
    [SerializeField] private float obstacleDetectionRange = 15f;

    [Tooltip("Width of the detection zone (car width + margin).")]
    [SerializeField] private float obstacleDetectionWidth = 2.5f;

    [Tooltip("Number of rays to cast in the fan pattern.")]
    [SerializeField, Range(3, 15)] private int obstacleRayCount = 7;

    [Tooltip("Fan angle for obstacle detection (degrees).")]
    [SerializeField] private float obstacleDetectionAngle = 45f;

    [Tooltip("How strongly to steer away from obstacles (0-1).")]
    [SerializeField, Range(0f, 1f)] private float obstacleAvoidanceStrength = 0.8f;

    [Tooltip("Distance at which avoidance reaches maximum strength.")]
    [SerializeField] private float criticalObstacleDistance = 5f;

    [Tooltip("How quickly to blend into avoidance steering.")]
    [SerializeField] private float avoidanceBlendSpeed = 8f;

    [Tooltip("Angle of the center 'danger zone' that triggers avoidance (degrees). Obstacles outside this zone only influence steering direction.")]
    [SerializeField] private float dangerZoneAngle = 15f;

    [Tooltip("Height offset for obstacle detection rays.")]
    [SerializeField] private float obstacleRayHeight = 0.5f;

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

    [Tooltip("If true, validate each movement step stays on road layer.")]
    [SerializeField] private bool validateMovementOnRoad = true;

    [Tooltip("How much to blend track-following vs free steering (0=pure track, 1=free).")]
    [SerializeField, Range(0f, 1f)] private float trackFollowingStrength = 0.7f;

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

    // ============================================
    // INTERNALS - Avoidance State (NEW)
    // ============================================
    private float _avoidanceUrgency;       // 0-1, how urgent is avoidance
    private Vector3 _avoidanceDirection;   // Direction to steer toward
    private float _avoidanceBlend;         // Current blend toward avoidance
    private bool _isAvoiding;

    // ============================================
    // INTERNALS - Road Boundary State (NEW)
    // ============================================
    private float _roadCorrectionInput;    // -1 to 1, steering correction to stay on road
    private float _distanceFromTrackCenter; // How far off center we are
    private bool _leftEdgeDetected;
    private bool _rightEdgeDetected;
    private float _leftEdgeDistance;
    private float _rightEdgeDistance;

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
        _seeker = GetComponent<Seeker>();
        _ai = GetComponent<IAstarAI>();
        _richAI = GetComponent<RichAI>();
        _aiBase = GetComponent<AIBase>();
        _rvo = GetComponent<RVOController>();

        // Configure rigidbody - we handle movement ourselves now
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
        _avoidanceUrgency = 0f;
        _avoidanceBlend = 0f;
        _isAvoiding = false;

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

        // Update our track distance based on current position
        _dist = GetDistanceAlongPath(transform.position);

        // Update destination for A* (advisory only now)
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

        // ============================================
        // NEW: Vehicle steering-based movement
        // ============================================
        UpdateObstacleDetection();
        UpdateRoadBoundaryDetection();
        UpdateSteering(dt);
        UpdateMovement(dt);

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

    private void LateUpdate()
    {
        if (_crashed) return;
        if (!_initialized) return;

        float dt = Time.deltaTime;
        if (dt <= 0f) return;

        // Ground snap (Y only)
        Vector3 pos = transform.position;
        if (RaycastGround(pos, out RaycastHit hit))
        {
            pos.y = hit.point.y + _pivotToBottom + groundClearance;
            transform.position = pos;
            _groundNormal = hit.normal;
        }

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
            return;
        }

        Vector3 origin = transform.position + Vector3.up * obstacleRayHeight;
        Vector3 forward = _currentForward;
        forward.y = 0f;
        forward.Normalize();

        if (forward.sqrMagnitude < 0.01f)
            forward = transform.forward;

        // Track hits separately for center (danger zone) and sides
        float closestCenterHit = float.MaxValue;
        float closestLeftHit = float.MaxValue;
        float closestRightHit = float.MaxValue;
        bool centerBlocked = false;
        int leftHits = 0;
        int rightHits = 0;

        float halfAngle = obstacleDetectionAngle * 0.5f;
        float angleStep = obstacleDetectionAngle / (obstacleRayCount - 1);
        float halfDangerZone = dangerZoneAngle * 0.5f;

        // Scale detection range based on speed
        float range = Mathf.Lerp(obstacleDetectionRange * 0.5f, obstacleDetectionRange,
                                  Mathf.InverseLerp(5f, 20f, _currentSpeed));

        for (int i = 0; i < obstacleRayCount; i++)
        {
            float angle = -halfAngle + angleStep * i;
            Vector3 rayDir = Quaternion.Euler(0f, angle, 0f) * forward;

            if (Physics.Raycast(origin, rayDir, out RaycastHit hit, range, obstacleDetectionLayers, QueryTriggerInteraction.Ignore))
            {
                // Ignore self
                if (hit.collider.transform.IsChildOf(transform)) continue;

                bool isInDangerZone = Mathf.Abs(angle) <= halfDangerZone;
                bool isLeftSide = angle < -halfDangerZone;
                bool isRightSide = angle > halfDangerZone;

                if (isInDangerZone)
                {
                    // CENTER hit - this is a real threat
                    centerBlocked = true;
                    if (hit.distance < closestCenterHit)
                        closestCenterHit = hit.distance;
                }
                else if (isLeftSide)
                {
                    // LEFT side hit - don't steer left
                    leftHits++;
                    if (hit.distance < closestLeftHit)
                        closestLeftHit = hit.distance;
                }
                else if (isRightSide)
                {
                    // RIGHT side hit - don't steer right
                    rightHits++;
                    if (hit.distance < closestRightHit)
                        closestRightHit = hit.distance;
                }

                if (drawObstacleRays)
                    Debug.DrawLine(origin, hit.point, isInDangerZone ? Color.red : Color.yellow);
            }
            else if (drawObstacleRays)
            {
                Debug.DrawRay(origin, rayDir * range, Color.green);
            }
        }

        // ONLY trigger avoidance if CENTER is blocked
        if (centerBlocked)
        {
            // Calculate urgency based on center obstacle distance
            _avoidanceUrgency = Mathf.Clamp01(1f - (closestCenterHit - criticalObstacleDistance) /
                                              (obstacleDetectionRange - criticalObstacleDistance));

            // Decide which way to steer based on side clearance
            float avoidAngle;

            bool leftClear = (leftHits == 0) || (closestLeftHit > closestCenterHit * 1.5f);
            bool rightClear = (rightHits == 0) || (closestRightHit > closestCenterHit * 1.5f);

            if (leftClear && !rightClear)
            {
                // Left is clear, right is blocked - steer left
                avoidAngle = -35f;
            }
            else if (rightClear && !leftClear)
            {
                // Right is clear, left is blocked - steer right
                avoidAngle = 35f;
            }
            else if (leftClear && rightClear)
            {
                // Both sides clear - pick based on road edge distance
                avoidAngle = (_rightEdgeDistance >= _leftEdgeDistance) ? 35f : -35f;
            }
            else
            {
                // Both sides blocked - pick the one with more distance
                if (closestLeftHit > closestRightHit)
                    avoidAngle = -30f; // Left has more room
                else
                    avoidAngle = 30f;  // Right has more room
            }

            // Scale avoidance angle by urgency
            avoidAngle *= Mathf.Lerp(0.5f, 1f, _avoidanceUrgency);

            _avoidanceDirection = Quaternion.Euler(0f, avoidAngle, 0f) * forward;
            _isAvoiding = true;

            if (drawObstacleRays)
            {
                Debug.DrawRay(origin + Vector3.up * 0.5f, _avoidanceDirection * 5f, Color.magenta);
            }
        }
        else
        {
            // Center is clear - no avoidance needed even if sides detect something
            _avoidanceUrgency = 0f;
            _isAvoiding = false;
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

        // Get track center position at our current distance
        SampleAlongPath(_dist, out Vector3 trackCenter, out Vector3 trackForward);

        // Calculate how far we are from track center (lateral offset)
        Vector3 toUs = pos - trackCenter;
        toUs.y = 0f;
        Vector3 trackRight = Vector3.Cross(Vector3.up, trackForward).normalized;
        _distanceFromTrackCenter = Vector3.Dot(toUs, trackRight);

        float halfRoadWidth = GetHalfRoadWidth();

        // Cast rays to detect road edges on left and right
        float rayHeight = 0.3f;
        Vector3 rayOrigin = pos + Vector3.up * rayHeight;
        float rayDownDist = raycastStartHeight + raycastDownDistance;

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

        // Method 2: Center-seeking correction (gentle pull toward track center)
        float normalizedOffset = _distanceFromTrackCenter / halfRoadWidth;
        float centerCorrection = -normalizedOffset * 0.3f * roadCorrectionStrength;

        // Only apply center correction if we're not already correcting for an edge
        if (Mathf.Abs(_roadCorrectionInput) < 0.1f)
        {
            _roadCorrectionInput += centerCorrection;
        }

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
        // Get target direction from A* path (advisory)
        Vector3 desiredDirection = GetDesiredDirection();

        // Blend desired direction with track direction for better lane-keeping
        SampleAlongPath(_dist + 5f, out Vector3 _, out Vector3 trackFwd);
        trackFwd.y = 0f;
        trackFwd.Normalize();
        if (trackFwd.sqrMagnitude > 0.01f)
        {
            desiredDirection = Vector3.Slerp(trackFwd, desiredDirection, trackFollowingStrength);
        }

        float targetBlend = _isAvoiding ? _avoidanceUrgency * obstacleAvoidanceStrength : 0f;
        // Ramp up fast when urgent, decay fast when not avoiding
        float blendSpeed;
        if (_isAvoiding && _avoidanceUrgency > 0.7f)
            blendSpeed = avoidanceBlendSpeed * 3f;  // Fast ramp up for urgent
        else if (!_isAvoiding)
            blendSpeed = avoidanceBlendSpeed * 4f;  // Fast decay when path is clear
        else
            blendSpeed = avoidanceBlendSpeed;
        _avoidanceBlend = Mathf.MoveTowards(_avoidanceBlend, targetBlend, blendSpeed * dt);

        Vector3 targetDirection;
        if (_avoidanceBlend > 0.01f)
        {
            targetDirection = Vector3.Slerp(desiredDirection, _avoidanceDirection, _avoidanceBlend);
        }
        else
        {
            targetDirection = desiredDirection;
        }

        targetDirection.y = 0f;
        targetDirection.Normalize();

        // Calculate steering input (-1 to 1) based on angle to target direction
        float angleToTarget = Vector3.SignedAngle(_currentForward, targetDirection, Vector3.up);
        _currentSteeringInput = Mathf.Clamp(angleToTarget / 45f, -1f, 1f);

        // Add road boundary correction (higher priority than normal steering)
        if (enableRoadBoundaryDetection && Mathf.Abs(_roadCorrectionInput) > 0.05f)
        {
            // Road correction overrides normal steering proportionally to its urgency
            float correctionWeight = Mathf.Abs(_roadCorrectionInput);
            _currentSteeringInput = Mathf.Lerp(_currentSteeringInput, _roadCorrectionInput, correctionWeight);
        }

        _currentSteeringInput = Mathf.Clamp(_currentSteeringInput, -1f, 1f);

        // Smooth the steering input
        _smoothedSteeringInput = Mathf.SmoothDamp(_smoothedSteeringInput, _currentSteeringInput,
                                                   ref _steeringVelocity, steeringSmoothing);

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

        // Reduce speed during avoidance
        if (slowDownOnAvoidance && _avoidanceBlend > 0.01f)
        {
            float speedReduction = Mathf.Lerp(1f, avoidanceSpeedMultiplier, _avoidanceBlend * _avoidanceUrgency);
            _targetSpeed *= speedReduction;
        }

        // Accelerate/decelerate toward target speed
        if (_currentSpeed < _targetSpeed)
        {
            _currentSpeed = Mathf.MoveTowards(_currentSpeed, _targetSpeed, accelerationRate * dt);
        }
        else
        {
            _currentSpeed = Mathf.MoveTowards(_currentSpeed, _targetSpeed, decelerationRate * dt);
        }

        // Move the car
        Vector3 movement = _currentForward * _currentSpeed * dt;
        Vector3 newPosition = transform.position + movement;

        // Validate movement stays on road
        if (validateMovementOnRoad && roadLayer.value != 0)
        {
            Vector3 checkOrigin = newPosition + Vector3.up * raycastStartHeight;
            if (!Physics.Raycast(checkOrigin, Vector3.down, raycastStartHeight + raycastDownDistance, roadLayer, QueryTriggerInteraction.Ignore))
            {
                // New position is NOT on road - try to correct
                // First, try moving along track direction instead
                SampleAlongPath(_dist + _currentSpeed * dt, out Vector3 trackPos, out Vector3 trackFwd);

                // Apply our lateral offset to stay in lane
                Vector3 trackRight = Vector3.Cross(Vector3.up, trackFwd).normalized;
                float halfWidth = GetHalfRoadWidth();
                float maxLateral = Mathf.Max(0f, halfWidth * lateralFraction - edgeMargin);
                float clampedOffset = Mathf.Clamp(_lateralOffset, -maxLateral, maxLateral);

                Vector3 correctedPosition = trackPos + trackRight * clampedOffset;

                // Verify corrected position is on road
                Vector3 correctedCheckOrigin = correctedPosition + Vector3.up * raycastStartHeight;
                if (Physics.Raycast(correctedCheckOrigin, Vector3.down, raycastStartHeight + raycastDownDistance, roadLayer, QueryTriggerInteraction.Ignore))
                {
                    newPosition = correctedPosition;
                    // Also correct our forward direction toward track
                    _currentForward = Vector3.Slerp(_currentForward, trackFwd, 0.3f);
                    _currentForward.y = 0f;
                    _currentForward.Normalize();
                }
                else
                {
                    // Even corrected position is off road - don't move, slow down
                    _currentSpeed *= 0.9f;
                    return; // Skip this frame's movement
                }
            }
        }

        transform.position = newPosition;

        // Update rotation to face movement direction
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

    private Vector3 GetDesiredDirection()
    {
        // Primary: direction toward A* destination
        if (_hasSetDestination)
        {
            Vector3 toDestination = _lastDestination - transform.position;
            toDestination.y = 0f;

            if (toDestination.sqrMagnitude > 1f)
            {
                // Also consider the RVO's desired velocity if available
                if (_rvo != null && _aiBase != null)
                {
                    Vector3 rvoVelocity = _aiBase.velocity;
                    rvoVelocity.y = 0f;

                    if (rvoVelocity.sqrMagnitude > 0.1f)
                    {
                        // Blend between direct path and RVO suggestion
                        // RVO suggestion is weighted lower since we handle avoidance ourselves
                        return Vector3.Slerp(toDestination.normalized, rvoVelocity.normalized, 0.3f);
                    }
                }

                return toDestination.normalized;
            }
        }

        // Fallback: track direction
        SampleAlongPath(_dist + 5f, out Vector3 aheadPos, out Vector3 trackFwd);
        trackFwd.y = 0f;
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

        // Configure A* (movement disabled - we handle it)
        if (_ai != null)
        {
            _ai.maxSpeed = speed;
            _ai.canSearch = true;
            _ai.canMove = false; // CRITICAL: We handle movement ourselves now
        }

        if (_richAI != null)
        {
            _richAI.maxSpeed = speed;
            _richAI.canMove = false; // CRITICAL: We handle movement ourselves now
            _richAI.enableRotation = false;
        }

        // RVO still runs for its avoidance calculations, but we don't use its velocity directly
        // It affects the AIBase.velocity which we read as advisory input

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
        if (!drawDestinationGizmo || !_hasSetDestination) return;

        Gizmos.color = Color.green;
        Gizmos.DrawWireSphere(_lastDestination, 1f);
        Gizmos.DrawLine(transform.position, _lastDestination);

        // Show avoidance state
        if (_isAvoiding)
        {
            Gizmos.color = Color.Lerp(Color.yellow, Color.red, _avoidanceUrgency);
            Gizmos.DrawWireSphere(transform.position + Vector3.up * 2f, 0.5f);
        }
    }
}