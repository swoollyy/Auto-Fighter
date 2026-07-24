using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

/// <summary>
/// NPC traffic: follows procedural spline, avoids obstacles with a stable two-phase driver (track follow vs committed dodge).
/// Layer masks <see cref="roadLayer"/>, <see cref="driveableLayer"/>, <see cref="crashLayers"/>, <see cref="obstacleDetectionLayers"/>
/// are the integration points with your scene — keep them assigned on the prefab.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
[RequireComponent(typeof(Collider))]
public class NPCTrafficCar : MonoBehaviour
{
    private enum NpcDrivePhase
    {
        FollowingTrack,
        AvoidingObstacle,
        RecoveringFromAvoid,
    }

    // -------------------------------------------------------------------------
    // TRACK
    // -------------------------------------------------------------------------
    [Header("A — Track")]
    [SerializeField] private ProceduralTrackGenerator trackGenerator;
    [SerializeField] private bool useSmoothing = true;
    [SerializeField, Min(1)] private int smoothingSubdivisionsPerSegment = 6;
    [Tooltip("Meters ahead on spline for heading (curves).")]
    [SerializeField, Min(2f)] private float trackLookAhead = 14f;

    // -------------------------------------------------------------------------
    // SPEED
    // -------------------------------------------------------------------------
    [Header("B — Speed")]
    [SerializeField] private float speed = 12f;
    [SerializeField] private bool randomizeSpeed = true;
    [SerializeField] private Vector2 speedRange = new Vector2(8f, 18f);
    [FormerlySerializedAs("slowDownOnAvoidance")]
    [SerializeField] private bool slowDownWhileAvoiding = true;
    [FormerlySerializedAs("avoidanceSpeedMultiplier")]
    [SerializeField, Range(0.35f, 1f)] private float avoidingSpeedMultiplier = 0.72f;

    // -------------------------------------------------------------------------
    // GROUND & LAYERS (keep assigned on prefab)
    // -------------------------------------------------------------------------
    [Header("C — Ground & drive validation")]
    [SerializeField] private LayerMask roadLayer;
    [Tooltip("Optional: road + grass. If 0, uses roadLayer only.")]
    [SerializeField] private LayerMask driveableLayer;
    [SerializeField] private float raycastStartHeight = 5f;
    [SerializeField] private float raycastDownDistance = 15f;
    [SerializeField] private float groundClearance = 0.05f;
    [FormerlySerializedAs("validateMovementOnRoad")]
    [SerializeField] private bool validateMovementOnDriveable = true;

    // -------------------------------------------------------------------------
    // STEERING (vehicle feel)
    // -------------------------------------------------------------------------
    [Header("D — Steering / turning")]
    [FormerlySerializedAs("baseTurnRate")]
    [SerializeField, Min(20f)] private float baseTurnRateDegPerSec = 140f;
    [FormerlySerializedAs("minTurnRate")]
    [SerializeField, Min(10f)] private float minTurnRateDegPerSec = 38f;
    [SerializeField, Min(5f)] private float turnRateFalloffSpeed = 22f;
    [SerializeField, Min(1f)] private float accelerationRate = 15f;
    [SerializeField, Min(1f)] private float decelerationRate = 25f;
    [FormerlySerializedAs("steeringSmoothing")]
    [SerializeField, Min(0.02f)] private float steeringSmoothTime = 0.22f;
    [FormerlySerializedAs("speedSmoothing")]
    [SerializeField, Min(0.02f)] private float speedSmoothTime = 0.12f;
    [Tooltip("Scales how much heading error maps to steering (lower = sharper turns). " +
             "Max yaw rate is still set by Base/Min Turn Rate below.")]
    [SerializeField, Min(0.06f)] private float alignmentTime = 0.42f;
    [Tooltip("Heading error (degrees) that would request full steering if alignment alone allowed it. " +
             "Lower = snappier. Decoupled from turn rate so cranking Base Turn Rate does not weaken steering.")]
    [SerializeField, Min(10f)] private float headingErrorForFullSteerDeg = 52f;
    [SerializeField, Range(0f, 8f)] private float steeringDeadZoneDeg = 2.8f;
    [SerializeField, Range(0f, 0.55f)] private float steeringDamping = 0.22f;
    [SerializeField] private bool alignToGround = false;
    [SerializeField] private float minSpeedForRotation = 0.5f;

    // -------------------------------------------------------------------------
    // OBSTACLE SCAN (layers must match your obstacle colliders)
    // -------------------------------------------------------------------------
    [Header("E — Obstacle scan")]
    [SerializeField] private LayerMask obstacleDetectionLayers;
    [FormerlySerializedAs("obstacleDetectionRange")]
    [SerializeField, Min(4f)] private float scanRange = 34f;
    [Tooltip("Lateral path half-width used to decide if a hit is “in our lane”.")]
    [FormerlySerializedAs("pathHalfWidthForAvoidance")]
    [SerializeField, Min(0.35f)] private float pathHalfWidth = 1.35f;
    [FormerlySerializedAs("obstacleRayCount")]
    [SerializeField, Range(5, 21)] private int scanRayCount = 15;
    [FormerlySerializedAs("obstacleDetectionAngle")]
    [SerializeField, Range(20f, 100f)] private float scanFanAngleDeg = 62f;
    [Tooltip("Center cone: hits here count as forward threat.")]
    [SerializeField, Range(8f, 70f)] private float forwardThreatConeHalfAngleDeg = 16f;
    [Tooltip("Multiply forward-threat half-angle (red debug rays) after a dodge and optionally while avoiding — keeps obstacles in the “danger cone” so traffic is less likely to steer back into them.")]
    [SerializeField, Min(1f)] private float forwardThreatConeDangerBoostMultiplier = 1.35f;
    [Tooltip("After leaving AvoidingObstacle (recovery starts), keep the widened forward threat cone for this many seconds. 0 = post-dodge boost off.")]
    [SerializeField, Min(0f)] private float postDodgeThreatConeBoostDuration = 2f;
    [Tooltip("If true, the danger boost also applies during AvoidingObstacle, not only for the post-dodge timer.")]
    [SerializeField] private bool widenForwardThreatConeWhileAvoiding = true;
    [FormerlySerializedAs("obstacleRayHeight")]
    [SerializeField, Min(0f)] private float rayHeight = 0.5f;
    [FormerlySerializedAs("obstacleRayForwardOffset")]
    [SerializeField, Min(0f)] private float rayForwardOffset = 0.85f;
    [FormerlySerializedAs("avoidanceClearanceDistance")]
    [SerializeField, Min(0f)] private float hitboxClearance = 0.85f;
    [Tooltip("Scales the overlap-sphere radius used by avoidance threat checks (and its debug gizmo).")]
    [SerializeField, Range(0.25f, 2f)] private float avoidanceOverlapRadiusMultiplier = 1f;
    [FormerlySerializedAs("avoidancePredictionTime")]
    [Tooltip("Seconds × speed ≈ reaction distance cap.")]
    [SerializeField, Range(0.12f, 1.2f)] private float reactionTime = 0.5f;

    // -------------------------------------------------------------------------
    // AVOIDANCE BEHAVIOR (state machine)
    // -------------------------------------------------------------------------
    [Header("F — Avoidance behavior")]
    [Tooltip("Consecutive frames with forward threat before committing to a dodge.")]
    [FormerlySerializedAs("obstacleConfirmFrames")]
    [SerializeField, Min(1)] private int framesToCommitAvoid = 2;
    [Tooltip("While avoiding: how strongly steering favors the dodge corridor vs track tangent.")]
    [SerializeField, Range(0.5f, 1f)] private float avoidSteerWeight = 0.92f;
    [Tooltip("Max degrees per second the committed dodge direction can rotate (kills jitter).")]
    [SerializeField, Min(5f)] private float dodgeHeadingSlewDegPerSec = 42f;
    [Tooltip("Blend into dodge during pre-commit (before framesToCommitAvoid).")]
    [SerializeField, Range(0f, 0.7f)] private float preCommitSteerBlend = 0.38f;
    [FormerlySerializedAs("avoidanceExitWhenForwardClearFrames")]
    [SerializeField, Min(1)] private int framesForwardClearToExit = 3;
    [FormerlySerializedAs("enableResumeAlongTrackAfterDodge")]
    [Tooltip("Along spline tangent: if clear for this many frames while avoiding, drop dodge.")]
    [SerializeField] private bool exitWhenTrackAheadClear = true;
    [FormerlySerializedAs("resumeAlongTrackCheckDistance")]
    [SerializeField, Min(3f)] private float trackAheadClearCheckDistance = 11f;
    [FormerlySerializedAs("resumeAlongTrackClearFrames")]
    [SerializeField, Min(1)] private int framesTrackAheadClearToExit = 4;
    [Tooltip("Side hits: soft nudge only when a forward threat still exists or scrape is very close.")]
    [FormerlySerializedAs("sideObstacleSoftDistance")]
    [SerializeField, Min(2f)] private float sideNudgeMaxDistance = 11f;
    [Tooltip("Minimum time spent in avoiding phase before we allow exit back to pure track-follow.")]
    [SerializeField, Min(0f)] private float minAvoidTimeBeforeExit = 0.45f;
    [Tooltip("If the remembered threat point is still this far ahead, keep dodging.")]
    [SerializeField, Min(0f)] private float obstacleAheadBlocksExitDistance = 8f;
    [Tooltip("Obstacle point must be at least this far behind forward axis before exit is allowed.")]
    [SerializeField, Min(0f)] private float obstacleBehindRequiredForExit = 2f;
    [Tooltip("After dodge: time to hold lateral offset while aligning heading to track.")]
    [SerializeField, Min(0f)] private float recoveryMinDuration = 0.45f;
    [Tooltip("After dodge: require this many clear frames before returning to full track-follow.")]
    [SerializeField, Min(1)] private int recoveryClearFramesToExit = 4;
    [Tooltip("Heading error to track forward that counts as stabilized during recovery.")]
    [SerializeField, Range(0.5f, 25f)] private float recoveryHeadingErrorDeg = 6f;
    [Tooltip("During recovery, blend toward the frozen lateral lane target.")]
    [SerializeField, Range(0f, 1f)] private float recoveryLaneHoldSteerBlend = 0.35f;
    [Tooltip("Maximum absolute steering input allowed during recovery (prevents overshoot swings).")]
    [SerializeField, Range(0.1f, 1f)] private float recoveryMaxSteer = 0.45f;
    [Tooltip("After recovery, keep cruising near the recovered lateral offset (0 = ignore).")]
    [SerializeField, Range(0f, 1f)] private float followCruiseLaneHoldBlend = 0.42f;

    [Header("F — Avoidance stability")]
    [Tooltip("Frames the committed corridor must be blocked before we allow a side flip.")]
    [SerializeField, Min(1)] private int framesCorridorBlockedToReplan = 2;
    [FormerlySerializedAs("dodgeFlankSwitchGreenMargin")]
    [Tooltip("Opposite flank must win by this many “green” rays for this many frames to flip.")]
    [SerializeField, Min(2)] private int oppositeFlankGreenMargin = 4;
    [FormerlySerializedAs("dodgeFlankSwitchStreakFrames")]
    [SerializeField, Min(2)] private int framesOppositeFlankWinsToFlip = 4;
    [FormerlySerializedAs("minTrackRoomToDodge")]
    [Tooltip("Minimum lateral road room (m) before dodging that way.")]
    [SerializeField, Min(0.2f)] private float minRoadRoomToDodge = 2f;
    [Tooltip("When > 0, dodge picks, ray scores, and headings favor the spline center. Also stops the old 'swerve toward the more open flank' rule from shoving traffic further onto the shoulder.")]
    [SerializeField, Range(0f, 5f)] private float dodgeTowardRoadCenterWeight = 2.35f;
    [Tooltip("While committed to an obstacle dodge, blend this much steer toward lane center (0 = dodge only ignores centering).")]
    [SerializeField, Range(0f, 0.55f)] private float avoidanceLaneCenterSteerBlend = 0.22f;
    [Tooltip("If a forward threat is closer than this (m), commit to Avoiding on the first confirmed threat frame (still needs streak ≥ 1). 0 = only use Frames To Commit.")]
    [SerializeField, Min(0f)] private float urgentThreatCommitUnderDistance = 14f;
    [Tooltip("While a forward threat is closer than this (m), never pick a weak center dodge when a flank is viable — reduces dead-ahead wobble and late flank commits.")]
    [SerializeField, Min(0f)] private float deadAheadForbidCenterUnderDistance = 22f;
    [Tooltip("Left vs right ray scores must beat the previous flank pick by at least this much before switching sides. Stops symmetric obstacles from flipping dodge every frame.")]
    [SerializeField, Min(0f)] private float dodgeFlankScoreHysteresis = 4f;

    // -------------------------------------------------------------------------
    // STAY ON ROAD
    // -------------------------------------------------------------------------
    [Header("G — Stay on road")]
    [SerializeField] private bool enableRoadBoundaryDetection = true;
    [FormerlySerializedAs("roadEdgeDetectionWidth")]
    [SerializeField] private float roadEdgeProbeWidth = 4f;
    [FormerlySerializedAs("roadCorrectionStrength")]
    [SerializeField, Range(0f, 1f)] private float roadEdgeSteerWeight = 0.82f;
    [SerializeField] private float roadEdgeSoftMargin = 1.5f;
    [FormerlySerializedAs("offTrackRecoveryStrength")]
    [SerializeField, Range(0f, 1f)] private float offTrackRecoverySteerWeight = 0.92f;
    [SerializeField] private bool drawRoadBoundaryDebug = false;

    // -------------------------------------------------------------------------
    [Header("Player crash profile")]
    [Tooltip("If true, uses CrashObstacleKind.NpcTrafficCarBig weights in CrashSeverityConfig.")]
    [SerializeField] private bool useHeavyCrashProfile;

    // COLLISION / CRASH (layers must match crash targets)
    // -------------------------------------------------------------------------
    [Header("H — Crash")]
    [SerializeField] private LayerMask crashLayers;
    [SerializeField] private bool ignoreRoadAndTerrain = true;
    [SerializeField] private bool enableOverlapDetection = false;
    [SerializeField] private float overlapCheckInterval = 0f;
    [Tooltip("Scales crash overlap-detection radius used by enableOverlapDetection (and red debug sphere).")]
    [SerializeField, Range(0.2f, 2f)] private float crashOverlapRadiusMultiplier = 1f;
    [SerializeField] private float minTransferVelocity = 2f;
    [SerializeField] private float crashBounceUp = 4f;
    [SerializeField] private float crashBounceBack = 6f;
    [SerializeField] private Vector2 crashSpinRange = new Vector2(180f, 400f);
    [SerializeField] private AudioClip crashClip;
    [SerializeField, Range(0f, 1f)] private float crashVolume = 0.8f;
    [SerializeField] private GameObject crashVFXPrefab;
    [SerializeField] private float crashVFXLifetime = 3f;
    [SerializeField] private bool destroyAfterCrash = true;
    [SerializeField] private float destroyDelay = 5f;

    // -------------------------------------------------------------------------
    // AUDIO / SURFACE / BOOST
    // -------------------------------------------------------------------------
    [Header("I — Engine audio")]
    [SerializeField] private AudioClip engineClip;
    [SerializeField, Range(0f, 1f)] private float engineVolume = 0.4f;
    [SerializeField] private float enginePitchMin = 0.7f;
    [SerializeField] private float enginePitchMax = 1.3f;

    [Header("I — Surface & boost")]
    [SerializeField] private bool enableSurfaceEffects = true;
    [SerializeField] private LayerMask surfaceDetectionLayers;
    [SerializeField] private float surfaceCheckInterval = 0.1f;
    [SerializeField] private float surfaceSpeedLerpRate = 5f;
    [SerializeField] private float boostPadSpeedBonus = 8f;
    [SerializeField] private float boostPadDuration = 1.5f;
    [SerializeField] private float boostPadThreshold = 1.3f;

    // -------------------------------------------------------------------------
    // DEBUG
    // -------------------------------------------------------------------------
    [Header("J — Debug")]
    [SerializeField] private bool verboseDebug = false;
    [SerializeField] private bool drawDestinationGizmo = true;
    [SerializeField] private bool drawObstacleRays = true;
    [SerializeField] private bool drawSteeringDebug = true;
    [SerializeField] private bool drawOverlapSphereGizmo = true;
    [SerializeField] private bool drawCrashOverlapDetectionGizmo = true;

    [Header("K — Collider Alignment")]
    [Tooltip("Auto-fit the root collider to this car's visual renderers on init. Helps large variant prefabs keep hitbox aligned.")]
    [SerializeField] private bool autoFitRootColliderToVisual = true;
    [Tooltip("If true and the current root collider isn't Box/Capsule, replace it with a BoxCollider fitted to visual bounds.")]
    [SerializeField] private bool replaceUnsupportedRootCollider = true;

    // --- Path -----------------------------------------------------------------
    private readonly List<Vector3> _path = new();
    private float[] _cumLengths;
    private float _totalLength;
    private float _dist;
    private float _pivotToBottom;
    private float _avoidanceSphereRadius;

    // --- Components -----------------------------------------------------------
    private Rigidbody _rb;
    private Collider _col;
    private AudioSource _engineSource;
    private readonly Collider[] _overlapHits = new Collider[24];

    // --- Motion ---------------------------------------------------------------
    private float _currentSpeed;
    private float _targetSpeed;
    private Vector3 _currentForward = Vector3.forward;
    private float _smoothedSteering;
    private float _steeringVel;
    private float _speedVel;
    private float _prevAngleToTarget;

    // --- Avoidance ------------------------------------------------------------
    private NpcDrivePhase _phase = NpcDrivePhase.FollowingTrack;
    private float _avoidanceUrgency;
    private Vector3 _dodgeHeading = Vector3.forward;
    private int _lockedDodgeSide; // -1 = left, +1 = right
    private int _forwardThreatStreak;
    private float _postDodgeThreatConeBoostEndTime = -1f;
    private int _forwardClearWhileAvoidStreak;
    private int _trackAheadClearStreak;
    private int _corridorBlockedStreak;
    private int _oppositeFlankWinStreak;
    private Vector3 _avoidanceObstaclePoint;
    private bool _avoidanceObstaclePointValid;
    private int _lastPrimaryThreatId = -1;
    private bool _ambientSideNudge;
    private Vector3 _nudgeHeading = Vector3.forward;
    private bool _preCommitActive;
    private Vector3 _preCommitDir = Vector3.forward;
    private float _avoidPhaseTime;
    private float _recoveryPhaseTime;
    private int _recoveryClearStreak;
    private float _recoveryTargetLateralOffset;
    private float _cruiseTargetLateralOffset;
    /// <summary>Last chosen flank dodge: -1 left, +1 right, 0 none / center. Reduces left-right flip on tied scores.</summary>
    private int _lastFlankDodgePick;

    // Scan context (valid during one FixedUpdate avoidance pass)
    private Vector3 _scanOrigin;
    private Vector3 _scanCarForward;
    private Vector3 _scanCarRight;
    private Vector3 _scanTrackCenter;
    private Vector3 _scanTrackFwd;
    private Vector3 _scanTrackRight;

    // Road / recovery ----------------------------------------------------------
    private float _roadSteerHint;
    private float _distanceFromTrackCenter;
    private bool _isOnRoad = true;
    private Vector3 _trackCenterAtDist;
    private bool _leftEdgeDetected;
    private bool _rightEdgeDetected;
    private float _leftEdgeDistance = 99f;
    private float _rightEdgeDistance = 99f;

    // --- Misc state -----------------------------------------------------------
    private bool _initialized;
    private bool _crashed;
    private bool _rollingLogRamPending;
    private Vector3 _rollingLogPlanarUnit = Vector3.forward;
    private float _rollingLogHorizImpulse;
    private float _rollingLogUpImpulse;
    private float _overlapTimer;
    private Vector3 _lastVelocity;
    private Vector3 _groundNormal = Vector3.up;
    private float _baseSpeed;
    private float _currentSpeedMul = 1f;
    private float _targetSpeedMul = 1f;
    private float _surfaceTimer;
    private float _boostEndTime;
    private bool _onBoostPad;
    private string _currentSurfaceType = "Normal";

    public bool HasCrashed => _crashed;
    public bool UseHeavyCrashProfile => useHeavyCrashProfile;
    public float CurrentSpeed => _currentSpeed;
    public bool IsAvoiding => _phase == NpcDrivePhase.AvoidingObstacle;
    public float AvoidanceUrgency => _avoidanceUrgency;

    private void Awake()
    {
        _rb = GetComponent<Rigidbody>();
        _col = GetComponent<Collider>();

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
        _rollingLogRamPending = false;
        _overlapTimer = 0f;
        _currentSpeed = 0f;
        _currentForward = transform.forward;
        _smoothedSteering = 0f;
        _steeringVel = 0f;
        _prevAngleToTarget = 0f;
        _phase = NpcDrivePhase.FollowingTrack;
        ResetAvoidanceInternalState();
        _roadSteerHint = 0f;
        _distanceFromTrackCenter = 0f;
        _isOnRoad = true;
        _leftEdgeDetected = false;
        _rightEdgeDetected = false;
        _leftEdgeDistance = roadEdgeProbeWidth;
        _rightEdgeDistance = roadEdgeProbeWidth;
        _currentSpeedMul = 1f;
        _targetSpeedMul = 1f;
        _surfaceTimer = 0f;
        _boostEndTime = 0f;
        _onBoostPad = false;
        _avoidanceSphereRadius = 0f;
        _postDodgeThreatConeBoostEndTime = -1f;
    }

    private void ResetAvoidanceInternalState()
    {
        _avoidanceUrgency = 0f;
        _forwardThreatStreak = 0;
        _forwardClearWhileAvoidStreak = 0;
        _trackAheadClearStreak = 0;
        _corridorBlockedStreak = 0;
        _oppositeFlankWinStreak = 0;
        _avoidanceObstaclePointValid = false;
        _lastPrimaryThreatId = -1;
        _ambientSideNudge = false;
        _preCommitActive = false;
        _lockedDodgeSide = 0;
        _avoidPhaseTime = 0f;
        _recoveryPhaseTime = 0f;
        _recoveryClearStreak = 0;
        _recoveryTargetLateralOffset = 0f;
        _cruiseTargetLateralOffset = 0f;
        _lastFlankDodgePick = 0;
        _dodgeHeading = transform.forward;
        _dodgeHeading.y = 0f;
        if (_dodgeHeading.sqrMagnitude < 1e-4f) _dodgeHeading = Vector3.forward;
        _dodgeHeading.Normalize();
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
            UpdateSurfaceEffects(dt);

        UpdateEngineAudio();

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

        _dist = GetDistanceAlongPath(transform.position);

        UpdateRoadBoundaryDetection();
        RunDriverFixedStep(dt);
    }

    private void LateUpdate()
    {
        if (_crashed) return;
        if (!_initialized) return;

        if (RaycastGround(transform.position, out RaycastHit hit))
            _groundNormal = hit.normal;

        _lastVelocity = _currentForward * _currentSpeed;
    }

    /// <summary>Single mechanical pipeline per physics step.</summary>
    private void RunDriverFixedStep(float dt)
    {
        if (obstacleDetectionLayers.value == 0)
        {
            _phase = NpcDrivePhase.FollowingTrack;
            ResetAvoidanceInternalState();
            UpdateSteering(dt, trackOnly: true);
            UpdateMovement(dt);
            return;
        }

        SampleAlongPath(_dist, out _scanTrackCenter, out _scanTrackFwd);
        _scanTrackFwd.y = 0f;
        if (_scanTrackFwd.sqrMagnitude < 1e-4f) _scanTrackFwd = _currentForward;
        _scanTrackFwd.Normalize();
        _scanTrackRight = Vector3.Cross(Vector3.up, _scanTrackFwd).normalized;

        _scanCarForward = _currentForward;
        _scanCarForward.y = 0f;
        if (_scanCarForward.sqrMagnitude < 1e-4f) _scanCarForward = transform.forward;
        _scanCarForward.Normalize();
        _scanCarRight = Vector3.Cross(Vector3.up, _scanCarForward).normalized;

        _scanOrigin = transform.position + Vector3.up * rayHeight + _scanCarForward * rayForwardOffset;

        var scan = BuildObstacleScan();
        ProcessAvoidanceFsm(scan, dt);
        UpdateSteering(dt, trackOnly: false);
        UpdateMovement(dt);
    }

    private struct ObstacleScan
    {
        public bool ForwardThreat;
        public float ForwardThreatDist;
        public int PrimaryThreatId;
        public bool HasThreatPoint;
        public Vector3 ThreatPoint;

        public float BestLeftClear;
        public float BestRightClear;
        public float BestCenterClear;
        public Vector3 BestLeftDir;
        public Vector3 BestRightDir;
        public Vector3 BestCenterDir;
        public bool HasBestLeft;
        public bool HasBestRight;
        public bool HasBestCenter;

        public int GreensLeft;
        public int GreensRight;
        public int GreensCenter;
        public int ObstacleRayHits;

        public float ClosestLeftHit;
        public float ClosestRightHit;
        public bool SideNudgeCandidate;
    }

    private ObstacleScan BuildObstacleScan()
    {
        var s = new ObstacleScan
        {
            ForwardThreat = false,
            ForwardThreatDist = scanRange,
            PrimaryThreatId = -1,
            HasThreatPoint = false,
            ThreatPoint = transform.position,
            BestLeftClear = 0f,
            BestRightClear = 0f,
            BestCenterClear = 0f,
            HasBestLeft = false,
            HasBestRight = false,
            HasBestCenter = false,
            GreensLeft = 0,
            GreensRight = 0,
            GreensCenter = 0,
            ObstacleRayHits = 0,
            ClosestLeftHit = scanRange * 2f,
            ClosestRightHit = scanRange * 2f,
            SideNudgeCandidate = false,
        };

        float range = scanRange;
        float halfFan = scanFanAngleDeg * 0.5f;
        float step = scanRayCount > 1 ? scanFanAngleDeg / (scanRayCount - 1) : 0f;
        float boostedHalfAngle = forwardThreatConeHalfAngleDeg * ForwardThreatConeDangerMultiplierCurrent();
        float halfThreat = Mathf.Min(halfFan - 0.01f, boostedHalfAngle);

        float clearance = Mathf.Max(0.08f, _avoidanceSphereRadius + hitboxClearance);
        float reactionDist = Mathf.Clamp(
            _currentSpeed * Mathf.Max(0.15f, reactionTime) + pathHalfWidth * 2f,
            2f,
            range);
        reactionDist = Mathf.Min(range, reactionDist + clearance * 0.55f);

        Vector3 toCenterFromScan = _scanTrackCenter - _scanOrigin;
        toCenterFromScan.y = 0f;
        float toCenterScanMagSq = toCenterFromScan.sqrMagnitude;
        if (toCenterScanMagSq > 0.01f)
            toCenterFromScan /= Mathf.Sqrt(toCenterScanMagSq);
        else
            toCenterFromScan = _scanTrackFwd;

        for (int i = 0; i < scanRayCount; i++)
        {
            float ang = -halfFan + step * i;
            Vector3 rayDir = Quaternion.Euler(0f, ang, 0f) * _scanCarForward;
            bool isLeft = ang < -halfThreat;
            bool isRight = ang > halfThreat;
            bool isForwardCone = !isLeft && !isRight;

            float roadLim = GetRoadLimitedDistance(_scanOrigin, rayDir, range);
            if (roadLim <= 0.05f)
            {
                if (drawObstacleRays)
                    Debug.DrawRay(_scanOrigin, rayDir * 0.2f, Color.yellow);
                continue;
            }

            if (Physics.Raycast(_scanOrigin, rayDir, out RaycastHit hit, roadLim, obstacleDetectionLayers, QueryTriggerInteraction.Ignore))
            {
                if (hit.collider != null && hit.collider.transform.IsChildOf(transform))
                {
                    if (isLeft) s.GreensLeft++;
                    else if (isRight) s.GreensRight++;
                    else s.GreensCenter++;
                    if (drawObstacleRays)
                        Debug.DrawRay(_scanOrigin, rayDir * roadLim, Color.green);
                    continue;
                }

                s.ObstacleRayHits++;
                bool inPath = IsHitInOurPath(hit.point);
                float eff = Mathf.Max(0f, hit.distance - clearance);

                if (inPath)
                {
                    if (isForwardCone && eff <= roadLim + 0.08f && hit.distance <= reactionDist + 0.15f)
                    {
                        s.ForwardThreat = true;
                        if (hit.distance < s.ForwardThreatDist)
                        {
                            s.ForwardThreatDist = hit.distance;
                            s.ThreatPoint = hit.point;
                            s.HasThreatPoint = true;
                            s.PrimaryThreatId = hit.collider != null ? hit.collider.GetInstanceID() : -1;
                        }
                    }

                    if (isLeft && eff < s.ClosestLeftHit)
                        s.ClosestLeftHit = eff;
                    if (isRight && eff < s.ClosestRightHit)
                        s.ClosestRightHit = eff;
                }

                if (drawObstacleRays)
                    Debug.DrawLine(_scanOrigin, hit.point, inPath ? (isForwardCone ? Color.red : new Color(1f, 0.55f, 0f)) : new Color(0.5f, 0.5f, 0.5f));
            }
            else
            {
                Vector3 end = _scanOrigin + rayDir * roadLim;
                float endLat = Vector3.Dot(end - _scanTrackCenter, _scanTrackRight);
                float lateral = Mathf.Abs(endLat);
                float halfW = Mathf.Max(0.4f, GetHalfRoadWidth());
                float centerScore = 1f - Mathf.Clamp01(lateral / halfW);
                float align = Mathf.Clamp01((Vector3.Dot(rayDir, _scanTrackFwd) + 1f) * 0.5f);
                float towardDelta = Mathf.Abs(_distanceFromTrackCenter) - Mathf.Abs(endLat);
                float towardBonus = Mathf.Max(0f, towardDelta) * (1.35f + dodgeTowardRoadCenterWeight);
                float rayCentering = 0f;
                if (dodgeTowardRoadCenterWeight > 0f && toCenterScanMagSq > 0.01f)
                    rayCentering = Mathf.Max(0f, Vector3.Dot(rayDir, toCenterFromScan)) * (4.2f + dodgeTowardRoadCenterWeight * 1.4f);
                float wCenter = dodgeTowardRoadCenterWeight;
                float score = roadLim * Mathf.Max(2.5f, 5f - wCenter * 0.35f)
                    + centerScore * (2.5f + wCenter * 1.1f)
                    + align * 0.35f
                    + towardBonus
                    + rayCentering;

                if (isLeft)
                {
                    s.GreensLeft++;
                    if (!isForwardCone && score > s.BestLeftClear)
                    {
                        s.BestLeftClear = score;
                        s.BestLeftDir = rayDir;
                        s.HasBestLeft = true;
                    }
                }
                else if (isRight)
                {
                    s.GreensRight++;
                    if (!isForwardCone && score > s.BestRightClear)
                    {
                        s.BestRightClear = score;
                        s.BestRightDir = rayDir;
                        s.HasBestRight = true;
                    }
                }
                else
                {
                    s.GreensCenter++;
                    if (isForwardCone && score > s.BestCenterClear)
                    {
                        s.BestCenterClear = score;
                        s.BestCenterDir = rayDir;
                        s.HasBestCenter = true;
                    }
                }

                if (drawObstacleRays)
                    Debug.DrawRay(_scanOrigin, rayDir * roadLim, Color.green);
            }
        }

        bool sideClose = s.ClosestLeftHit < sideNudgeMaxDistance || s.ClosestRightHit < sideNudgeMaxDistance;
        bool forwardStillRelevant = s.HasThreatPoint && s.ForwardThreatDist < reactionDist * 2.2f;
        bool scrape = Mathf.Min(s.ClosestLeftHit, s.ClosestRightHit) < sideNudgeMaxDistance * 0.45f;
        s.SideNudgeCandidate = sideClose && (forwardStillRelevant || scrape);

        return s;
    }

    /// <summary>Widens the forward “red cone” after a dodge and optionally while actively avoiding.</summary>
    private float ForwardThreatConeDangerMultiplierCurrent()
    {
        if (forwardThreatConeDangerBoostMultiplier <= 1f)
            return 1f;

        bool postDodge = postDodgeThreatConeBoostDuration > 0f && Time.time < _postDodgeThreatConeBoostEndTime;
        bool whileAvoid = widenForwardThreatConeWhileAvoiding && _phase == NpcDrivePhase.AvoidingObstacle;
        if (postDodge || whileAvoid)
            return forwardThreatConeDangerBoostMultiplier;
        return 1f;
    }

    private bool IsHitInOurPath(Vector3 hitPoint)
    {
        float halfW = Mathf.Max(pathHalfWidth, scanRange * 0.02f);
        halfW += Mathf.Clamp01(_currentSpeed / 28f) * 0.5f;

        float hitLatTrack = Vector3.Dot(hitPoint - _scanTrackCenter, _scanTrackRight);
        bool inTrackBand = Mathf.Abs(hitLatTrack - _distanceFromTrackCenter) <= halfW;

        Vector3 toHit = hitPoint - _scanOrigin;
        float latCar = Vector3.Dot(toHit, _scanCarRight);
        bool inCarBand = Mathf.Abs(latCar) <= halfW;

        return inTrackBand || inCarBand;
    }

    private bool IsRoadAtPoint(Vector3 point)
    {
        if (roadLayer.value == 0) return true;
        Vector3 o = point + Vector3.up * Mathf.Max(0.8f, raycastStartHeight * 0.55f);
        float d = Mathf.Max(2f, raycastStartHeight + raycastDownDistance);
        return Physics.Raycast(o, Vector3.down, d, roadLayer, QueryTriggerInteraction.Ignore);
    }

    private float GetRoadLimitedDistance(Vector3 start, Vector3 dir, float maxDist)
    {
        if (roadLayer.value == 0) return maxDist;
        const float step = 0.65f;
        float dist = step;
        float last = 0f;
        while (dist <= maxDist)
        {
            if (!IsRoadAtPoint(start + dir * dist))
                break;
            last = dist;
            dist += step;
        }
        if (last >= maxDist - step * 0.5f)
            return maxDist;
        return Mathf.Max(0f, last);
    }

    private bool CastCorridorClear(Vector3 dir, float maxDist)
    {
        dir.y = 0f;
        if (dir.sqrMagnitude < 1e-6f) return false;
        dir.Normalize();
        float lim = GetRoadLimitedDistance(_scanOrigin, dir, maxDist);
        if (lim <= 0.05f) return false;
        if (!Physics.Raycast(_scanOrigin, dir, out RaycastHit h, lim, obstacleDetectionLayers, QueryTriggerInteraction.Ignore))
            return true;
        if (h.collider != null && h.collider.transform.IsChildOf(transform))
            return true;
        return !IsHitInOurPath(h.point);
    }

    private bool TrackAheadIsClearForExit()
    {
        Vector3 t = _scanTrackFwd;
        t.y = 0f;
        t.Normalize();
        float d = Mathf.Min(scanRange, trackAheadClearCheckDistance);
        return CastCorridorClear(t, d);
    }

    private void ProcessAvoidanceFsm(ObstacleScan scan, float dt)
    {
        bool overlapThreat = false;
        if (_col != null)
        {
            Vector3 c = _col.bounds.center;
            float rBase = _avoidanceSphereRadius + hitboxClearance;
            float r = Mathf.Max(0.15f, rBase * Mathf.Max(0.01f, avoidanceOverlapRadiusMultiplier));
            int n = Physics.OverlapSphereNonAlloc(c, r, _overlapHits, obstacleDetectionLayers, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
            {
                Collider col = _overlapHits[i];
                if (col == null || col.transform.IsChildOf(transform)) continue;
                overlapThreat = true;
                _avoidanceObstaclePoint = col.ClosestPoint(c);
                _avoidanceObstaclePointValid = true;
                _lastPrimaryThreatId = col.GetInstanceID();
                break;
            }
        }

        bool wantThreat = scan.ForwardThreat || overlapThreat;
        if (wantThreat)
            _forwardThreatStreak++;
        else
            _forwardThreatStreak = 0;

        // Soft side nudge (only when tied to forward threat / scrape)
        bool sideNudge = scan.SideNudgeCandidate && !scan.ForwardThreat;

        if (_phase == NpcDrivePhase.FollowingTrack)
        {
            bool urgentCommit = urgentThreatCommitUnderDistance > 0.05f &&
                                scan.ForwardThreat &&
                                scan.ForwardThreatDist < urgentThreatCommitUnderDistance &&
                                _forwardThreatStreak >= 1;
            if (_forwardThreatStreak >= framesToCommitAvoid || overlapThreat || urgentCommit)
            {
                _phase = NpcDrivePhase.AvoidingObstacle;
                _ambientSideNudge = false;
                _preCommitActive = false;
                _lockedDodgeSide = ChooseDodgeSide(scan);
                _dodgeHeading = BlendDodgeHeadingTowardTrackCenter(InitialDodgeHeading(_lockedDodgeSide, scan));
                _corridorBlockedStreak = 0;
                _oppositeFlankWinStreak = 0;
                _forwardClearWhileAvoidStreak = 0;
                _trackAheadClearStreak = 0;
                _avoidPhaseTime = 0f;
                _recoveryPhaseTime = 0f;
                _recoveryClearStreak = 0;
            }

            _ambientSideNudge = sideNudge;
            if (_ambientSideNudge)
                _nudgeHeading = BlendDodgeHeadingTowardTrackCenter(InitialDodgeHeading(ChooseDodgeSide(scan), scan));

            _preCommitActive = wantThreat && _forwardThreatStreak > 0 && _forwardThreatStreak < framesToCommitAvoid;
            if (_preCommitActive)
                _preCommitDir = BlendDodgeHeadingTowardTrackCenter(InitialDodgeHeading(ChooseDodgeSide(scan), scan));

            _avoidanceUrgency = wantThreat
                ? Mathf.Clamp01(Mathf.InverseLerp(scanRange, 4f, scan.ForwardThreatDist))
                : (sideNudge ? 0.35f : 0f);

            if (scan.HasThreatPoint)
            {
                _avoidanceObstaclePoint = scan.ThreatPoint;
                _avoidanceObstaclePointValid = true;
            }
            else if (!overlapThreat)
                _avoidanceObstaclePointValid = false;

            if (!wantThreat)
                _lastFlankDodgePick = 0;

            return;
        }

        if (_phase == NpcDrivePhase.RecoveringFromAvoid)
        {
            _preCommitActive = false;
            _ambientSideNudge = false;
            _avoidanceUrgency = 0f;

            if (scan.HasThreatPoint)
            {
                _avoidanceObstaclePoint = scan.ThreatPoint;
                _avoidanceObstaclePointValid = true;
                _lastPrimaryThreatId = scan.PrimaryThreatId;
            }

            // Recovery is interrupted by new threat; immediately re-enter full dodge mode.
            if (wantThreat || overlapThreat)
            {
                _phase = NpcDrivePhase.AvoidingObstacle;
                _lockedDodgeSide = ChooseDodgeSide(scan);
                _dodgeHeading = BlendDodgeHeadingTowardTrackCenter(InitialDodgeHeading(_lockedDodgeSide, scan));
                _smoothedSteering = 0f;
                _steeringVel = 0f;
                _corridorBlockedStreak = 0;
                _oppositeFlankWinStreak = 0;
                _forwardClearWhileAvoidStreak = 0;
                _trackAheadClearStreak = 0;
                _avoidPhaseTime = 0f;
            }

            return;
        }

        // --- AvoidingObstacle ---
        _preCommitActive = false;
        _ambientSideNudge = false;
        _avoidPhaseTime += Mathf.Max(0f, dt);

        if (scan.HasThreatPoint)
        {
            _avoidanceObstaclePoint = scan.ThreatPoint;
            _avoidanceObstaclePointValid = true;
        }

        _avoidanceUrgency = Mathf.Clamp01(0.35f + 0.65f * Mathf.InverseLerp(scanRange * 0.85f, 3f, scan.ForwardThreatDist));

        Vector3 ideal = IdealHeadingForLockedSide(_lockedDodgeSide, scan);
        float maxStep = dodgeHeadingSlewDegPerSec * dt;
        _dodgeHeading = Vector3.RotateTowards(_dodgeHeading, ideal, maxStep * Mathf.Deg2Rad, 0f);
        _dodgeHeading.y = 0f;
        if (_dodgeHeading.sqrMagnitude < 1e-4f) _dodgeHeading = ideal;
        _dodgeHeading.Normalize();

        bool corridorOk = CastCorridorClear(_dodgeHeading, Mathf.Min(scanRange, reactionTime * _currentSpeed + 6f));
        if (!corridorOk)
            _corridorBlockedStreak++;
        else
            _corridorBlockedStreak = 0;

        if (_corridorBlockedStreak >= framesCorridorBlockedToReplan)
        {
            int replannedLane = ChooseDodgeSide(scan, respectFlankHysteresis: false);
            if (replannedLane != _lockedDodgeSide)
            {
                _oppositeFlankWinStreak++;
                if (_oppositeFlankWinStreak >= framesOppositeFlankWinsToFlip)
                {
                    _lockedDodgeSide = replannedLane;
                    _dodgeHeading = BlendDodgeHeadingTowardTrackCenter(InitialDodgeHeading(_lockedDodgeSide, scan));
                    _corridorBlockedStreak = 0;
                    _oppositeFlankWinStreak = 0;
                }
            }
            else
                _oppositeFlankWinStreak = Mathf.Max(0, _oppositeFlankWinStreak - 1);
        }
        else
            _oppositeFlankWinStreak = 0;

        // Exit conditions
        if (!scan.ForwardThreat && !overlapThreat)
            _forwardClearWhileAvoidStreak++;
        else
            _forwardClearWhileAvoidStreak = 0;

        if (exitWhenTrackAheadClear && TrackAheadIsClearForExit())
            _trackAheadClearStreak++;
        else
            _trackAheadClearStreak = 0;

        bool exitByForward = _forwardClearWhileAvoidStreak >= framesForwardClearToExit;
        bool exitByTrack = exitWhenTrackAheadClear && _trackAheadClearStreak >= framesTrackAheadClearToExit;
        bool exitTimeSatisfied = _avoidPhaseTime >= minAvoidTimeBeforeExit;
        bool threatStillAhead = false;
        if (_avoidanceObstaclePointValid)
        {
            Vector3 toThreat = _avoidanceObstaclePoint - transform.position;
            toThreat.y = 0f;
            float along = Vector3.Dot(toThreat, _scanCarForward);
            threatStillAhead = along > -obstacleBehindRequiredForExit && along < obstacleAheadBlocksExitDistance;
        }

        if ((exitByForward || exitByTrack) && exitTimeSatisfied && !threatStillAhead)
        {
            _phase = NpcDrivePhase.RecoveringFromAvoid;
            _recoveryPhaseTime = 0f;
            _recoveryClearStreak = 0;
            _recoveryTargetLateralOffset = _distanceFromTrackCenter;
            _smoothedSteering = 0f;
            _steeringVel = 0f;
            _avoidanceUrgency = 0f;
            _forwardThreatStreak = 0;
            _forwardClearWhileAvoidStreak = 0;
            _trackAheadClearStreak = 0;
            _corridorBlockedStreak = 0;
            _oppositeFlankWinStreak = 0;
            _ambientSideNudge = false;
            _preCommitActive = false;

            if (postDodgeThreatConeBoostDuration > 0f && forwardThreatConeDangerBoostMultiplier > 1f)
                _postDodgeThreatConeBoostEndTime = Time.time + postDodgeThreatConeBoostDuration;
        }
    }

    /// <param name="respectFlankHysteresis">False when replanning after a blocked corridor so we can switch to the clearly better flank.</param>
    private int ChooseDodgeSide(ObstacleScan scan, bool respectFlankHysteresis = true)
    {
        bool leftRoom = _leftEdgeDistance >= minRoadRoomToDodge;
        bool rightRoom = _rightEdgeDistance >= minRoadRoomToDodge;
        float leftScore = scan.HasBestLeft && leftRoom ? scan.BestLeftClear : -999f;
        float rightScore = scan.HasBestRight && rightRoom ? scan.BestRightClear : -999f;
        float centerScore = scan.HasBestCenter ? scan.BestCenterClear : -999f;

        if (!leftRoom && !rightRoom && centerScore <= -900f)
        {
            int e = _leftEdgeDistance >= _rightEdgeDistance ? -1 : 1;
            _lastFlankDodgePick = e;
            return e;
        }

        int centerSide = TowardTrackCenterDodgeSideSign();
        if (dodgeTowardRoadCenterWeight > 0f)
        {
            float halfW = Mathf.Max(0.35f, GetHalfRoadWidth());
            float u = Mathf.Clamp01(Mathf.Abs(_distanceFromTrackCenter) / (halfW * 0.92f));
            float bonus = dodgeTowardRoadCenterWeight * (1.15f + u * 1.8f);
            if (centerSide == 1)
                rightScore += bonus;
            else if (centerSide == -1)
                leftScore += bonus;
            else if (Mathf.Abs(_distanceFromTrackCenter) > 0.06f)
            {
                if (_distanceFromTrackCenter < 0f)
                    rightScore += bonus * 0.9f;
                else
                    leftScore += bonus * 0.9f;
            }
            else
                centerScore += bonus * 0.65f;
        }

        // Green-ray density influence, with a small center preference to break local side bias.
        leftScore += scan.GreensLeft * 0.13f;
        rightScore += scan.GreensRight * 0.13f;
        centerScore += scan.GreensCenter * 0.2f;

        // Avoid committing toward an immediately tight flank.
        if (scan.ClosestLeftHit < sideNudgeMaxDistance)
            leftScore -= (sideNudgeMaxDistance - scan.ClosestLeftHit) * 0.22f;
        if (scan.ClosestRightHit < sideNudgeMaxDistance)
            rightScore -= (sideNudgeMaxDistance - scan.ClosestRightHit) * 0.22f;
        if (scan.ForwardThreat)
            centerScore -= Mathf.Clamp01(Mathf.InverseLerp(scanRange, 3f, scan.ForwardThreatDist)) * 0.45f;

        float best = Mathf.Max(leftScore, Mathf.Max(centerScore, rightScore));
        if (best <= -900f)
        {
            if (centerSide != 0)
            {
                _lastFlankDodgePick = centerSide;
                return centerSide;
            }
            _lastFlankDodgePick = 0;
            return 0;
        }

        bool centerAllowed = centerScore > -900f;
        if (centerAllowed && scan.ForwardThreat)
        {
            // Under active forward threat, center must actually deflect and clearly beat flanks.
            if (!scan.HasBestCenter)
                centerAllowed = false;
            else
            {
                float centerAngle = Mathf.Abs(Vector3.SignedAngle(_scanCarForward, scan.BestCenterDir, Vector3.up));
                float threatNearness = Mathf.Clamp01(Mathf.InverseLerp(scanRange, 3f, scan.ForwardThreatDist));
                float minAngle = Mathf.Lerp(3f, 10f, threatNearness);
                float flankBest = Mathf.Max(leftScore, rightScore);
                bool clearlyBetter = centerScore >= flankBest + Mathf.Lerp(0.2f, 1.0f, threatNearness);
                centerAllowed = centerAngle >= minAngle && clearlyBetter;
            }
        }

        // Head-on / close threats: "center" is often a shallow forward ray that barely decides a side — force a flank when possible.
        if (centerAllowed && scan.ForwardThreat && deadAheadForbidCenterUnderDistance > 0.05f &&
            scan.ForwardThreatDist < deadAheadForbidCenterUnderDistance)
        {
            bool flankViable = (scan.HasBestLeft && leftRoom) || (scan.HasBestRight && rightRoom);
            if (flankViable)
                centerAllowed = false;
        }

        // If center is close to the best option and safe, prefer it for smoother pathing.
        if (centerAllowed && centerScore >= best - 0.4f)
        {
            _lastFlankDodgePick = 0;
            return 0;
        }

        int flankPick;
        if (rightScore > leftScore) flankPick = 1;
        else if (leftScore > rightScore) flankPick = -1;
        else
        {
            // Tie: dodge away from the threat's lateral position (stable for dead-ahead obstacles).
            if (scan.HasThreatPoint)
            {
                Vector3 toT = scan.ThreatPoint - transform.position;
                toT.y = 0f;
                float latThreat = Vector3.Dot(toT, _scanCarRight);
                if (Mathf.Abs(latThreat) > 0.12f)
                    flankPick = latThreat > 0f ? -1 : 1;
                else
                    flankPick = TieBreakFlankWhenNoThreatLateral(centerSide);
            }
            else
                flankPick = TieBreakFlankWhenNoThreatLateral(centerSide);
        }

        // Hysteresis: symmetric scores flip dodge side every frame without a margin.
        if (respectFlankHysteresis &&
            dodgeFlankScoreHysteresis > 0.01f &&
            _lastFlankDodgePick != 0 &&
            flankPick != 0 &&
            flankPick != _lastFlankDodgePick &&
            leftScore > -900f &&
            rightScore > -900f &&
            Mathf.Abs(rightScore - leftScore) < dodgeFlankScoreHysteresis)
        {
            int h = _lastFlankDodgePick;
            bool hOk = (h < 0 && scan.HasBestLeft && leftRoom) || (h > 0 && scan.HasBestRight && rightRoom);
            if (hOk)
                flankPick = h;
        }

        // If hysteresis or tie picked an infeasible flank, take the other side.
        if (flankPick < 0 && (!scan.HasBestLeft || !leftRoom))
            flankPick = (scan.HasBestRight && rightRoom) ? 1 : -1;
        if (flankPick > 0 && (!scan.HasBestRight || !rightRoom))
            flankPick = (scan.HasBestLeft && leftRoom) ? -1 : 1;

        _lastFlankDodgePick = flankPick;
        return flankPick;
    }

    /// <summary>When left/right scores tie and threat is centered, stable secondary tie-breakers.</summary>
    private int TieBreakFlankWhenNoThreatLateral(int centerSide)
    {
        if (centerSide != 0)
            return centerSide;
        if (Mathf.Abs(_distanceFromTrackCenter) > 0.08f)
            return _distanceFromTrackCenter < 0f ? 1 : -1;
        // Stable per-instance so two identical cars do not always pick the same side.
        return (GetInstanceID() & 1) == 0 ? 1 : -1;
    }

    /// <summary>
    /// +1 = spline center lies to the car's right (prefer right dodge), -1 = to the car's left.
    /// Uses car axes so slight crab vs the spline does not flip the wrong way.
    /// </summary>
    private int TowardTrackCenterDodgeSideSign()
    {
        Vector3 toC = _scanTrackCenter - transform.position;
        toC.y = 0f;
        float m2 = toC.sqrMagnitude;
        if (m2 < 0.04f) return 0;
        toC /= Mathf.Sqrt(m2);
        float d = Vector3.Dot(toC, _scanCarRight);
        if (d > 0.1f) return 1;
        if (d < -0.1f) return -1;
        return 0;
    }

    private Vector3 InitialDodgeHeading(int side, ObstacleScan scan)
    {
        Vector3 d;
        if (side == 0 && scan.HasBestCenter)
        {
            d = scan.BestCenterDir;
            d.y = 0f;
            d = d.normalized;
        }
        else if (side > 0 && scan.HasBestRight)
        {
            d = scan.BestRightDir;
            d.y = 0f;
            d = d.normalized;
        }
        else if (side < 0 && scan.HasBestLeft)
        {
            d = scan.BestLeftDir;
            d.y = 0f;
            d = d.normalized;
        }
        else
            d = Quaternion.Euler(0f, side * 28f, 0f) * _scanCarForward;

        d.y = 0f;
        return d.sqrMagnitude > 1e-4f ? d.normalized : _scanCarForward;
    }

    private Vector3 IdealHeadingForLockedSide(int side, ObstacleScan scan)
    {
        Vector3 d;
        if (side == 0 && scan.HasBestCenter)
        {
            d = scan.BestCenterDir;
            d.y = 0f;
            d = d.sqrMagnitude > 1e-4f ? d.normalized : InitialDodgeHeading(side, scan);
        }
        else if (side > 0 && scan.HasBestRight)
        {
            d = scan.BestRightDir;
            d.y = 0f;
            d = d.sqrMagnitude > 1e-4f ? d.normalized : InitialDodgeHeading(side, scan);
        }
        else if (side < 0 && scan.HasBestLeft)
        {
            d = scan.BestLeftDir;
            d.y = 0f;
            d = d.sqrMagnitude > 1e-4f ? d.normalized : InitialDodgeHeading(side, scan);
        }
        else
            d = Quaternion.Euler(0f, side * 32f, 0f) * _scanCarForward;

        return BlendDodgeHeadingTowardTrackCenter(d);
    }

    private Vector3 BlendDodgeHeadingTowardTrackCenter(Vector3 d)
    {
        d.y = 0f;
        if (d.sqrMagnitude < 1e-4f) d = _scanCarForward;
        d.Normalize();

        if (dodgeTowardRoadCenterWeight <= 0.01f)
            return d;

        Vector3 toC = _scanTrackCenter - transform.position;
        toC.y = 0f;
        if (toC.sqrMagnitude < 0.25f)
            return d;

        toC.Normalize();
        float blend = Mathf.Clamp01(0.14f + dodgeTowardRoadCenterWeight * 0.09f);
        return Vector3.Slerp(d, toC, blend).normalized;
    }

    private bool OppositeFlankStronger(ObstacleScan scan, int oppSide)
    {
        if (oppSide > 0)
            return scan.GreensRight >= scan.GreensLeft + oppositeFlankGreenMargin && scan.HasBestRight;
        if (oppSide < 0)
            return scan.GreensLeft >= scan.GreensRight + oppositeFlankGreenMargin && scan.HasBestLeft;
        return false;
    }

    private void UpdateRoadBoundaryDetection()
    {
        if (!enableRoadBoundaryDetection)
        {
            _roadSteerHint = 0f;
            return;
        }

        Vector3 pos = transform.position;
        Vector3 fwd = _currentForward;
        fwd.y = 0f;
        if (fwd.sqrMagnitude < 1e-4f) fwd = transform.forward;
        fwd.Normalize();
        Vector3 right = Vector3.Cross(Vector3.up, fwd).normalized;

        float rayH = 0.3f;
        float down = raycastStartHeight + raycastDownDistance;
        Vector3 rayOrigin = pos + Vector3.up * rayH;

        SampleAlongPath(_dist, out Vector3 trackCenter, out Vector3 trackForward);
        _trackCenterAtDist = trackCenter;

        _isOnRoad = roadLayer.value == 0 ||
                    Physics.Raycast(rayOrigin, Vector3.down, down, roadLayer, QueryTriggerInteraction.Ignore);

        trackForward.y = 0f;
        trackForward.Normalize();
        Vector3 trackRight = Vector3.Cross(Vector3.up, trackForward).normalized;
        _distanceFromTrackCenter = Vector3.Dot(pos - trackCenter, trackRight);

        float halfRoad = GetHalfRoadWidth();

        _leftEdgeDetected = false;
        _leftEdgeDistance = roadEdgeProbeWidth;
        for (float o = 0.5f; o <= roadEdgeProbeWidth; o += 0.5f)
        {
            Vector3 p = rayOrigin - right * o;
            if (!Physics.Raycast(p, Vector3.down, down, roadLayer, QueryTriggerInteraction.Ignore))
            {
                _leftEdgeDetected = true;
                _leftEdgeDistance = o;
                break;
            }
            if (drawRoadBoundaryDebug)
                Debug.DrawLine(p, p + Vector3.down * 2f, Color.cyan);
        }

        _rightEdgeDetected = false;
        _rightEdgeDistance = roadEdgeProbeWidth;
        for (float o = 0.5f; o <= roadEdgeProbeWidth; o += 0.5f)
        {
            Vector3 p = rayOrigin + right * o;
            if (!Physics.Raycast(p, Vector3.down, down, roadLayer, QueryTriggerInteraction.Ignore))
            {
                _rightEdgeDetected = true;
                _rightEdgeDistance = o;
                break;
            }
            if (drawRoadBoundaryDebug)
                Debug.DrawLine(p, p + Vector3.down * 2f, Color.cyan);
        }

        _roadSteerHint = 0f;
        if (_leftEdgeDetected && _leftEdgeDistance < roadEdgeSoftMargin)
        {
            float u = 1f - (_leftEdgeDistance / roadEdgeSoftMargin);
            _roadSteerHint += u * roadEdgeSteerWeight;
        }
        if (_rightEdgeDetected && _rightEdgeDistance < roadEdgeSoftMargin)
        {
            float u = 1f - (_rightEdgeDistance / roadEdgeSoftMargin);
            _roadSteerHint -= u * roadEdgeSteerWeight;
        }

        float norm = halfRoad > 0.01f ? (_distanceFromTrackCenter / halfRoad) : 0f;
        _roadSteerHint += -norm * (0.62f * roadEdgeSteerWeight);
        _roadSteerHint = Mathf.Clamp(_roadSteerHint, -1f, 1f);

        if (drawRoadBoundaryDebug)
        {
            Debug.DrawLine(trackCenter, trackCenter + Vector3.up * 2f, Color.green);
            Debug.DrawLine(pos, trackCenter, Color.yellow);
        }
    }

    private void UpdateSteering(float dt, bool trackOnly)
    {
        SampleAlongPath(_dist + trackLookAhead, out _, out Vector3 trackFwd);
        trackFwd.y = 0f;
        if (trackFwd.sqrMagnitude < 1e-4f) trackFwd = _currentForward;
        trackFwd.Normalize();

        Vector3 targetDir = trackFwd;

        if (!trackOnly)
        {
            if (_phase == NpcDrivePhase.AvoidingObstacle)
            {
                float w = Mathf.Lerp(avoidSteerWeight, 0.97f, _avoidanceUrgency);
                targetDir = Vector3.Slerp(trackFwd, _dodgeHeading, w).normalized;
            }
            else if (_phase == NpcDrivePhase.RecoveringFromAvoid)
            {
                targetDir = trackFwd;
                float halfRoad = Mathf.Max(0.35f, GetHalfRoadWidth());
                float targetLat = Mathf.Clamp(_recoveryTargetLateralOffset, -halfRoad * 0.95f, halfRoad * 0.95f);
                float latErr = _distanceFromTrackCenter - targetLat;
                float normErr = Mathf.Clamp(latErr / halfRoad, -1f, 1f);
                if (Mathf.Abs(normErr) > 0.015f && recoveryLaneHoldSteerBlend > 0f)
                {
                    Vector3 holdDir = (trackFwd + _scanTrackRight * Mathf.Clamp(-normErr * 0.45f, -0.35f, 0.35f)).normalized;
                    targetDir = Vector3.Slerp(trackFwd, holdDir, recoveryLaneHoldSteerBlend).normalized;
                }
            }
            else
            {
                float halfRoad = Mathf.Max(0.35f, GetHalfRoadWidth());
                float cruiseLat = Mathf.Clamp(_cruiseTargetLateralOffset, -halfRoad * 0.95f, halfRoad * 0.95f);
                float cruiseErr = _distanceFromTrackCenter - cruiseLat;
                float cruiseNorm = Mathf.Clamp(cruiseErr / halfRoad, -1f, 1f);
                if (Mathf.Abs(cruiseNorm) > 0.02f && followCruiseLaneHoldBlend > 0f)
                {
                    Vector3 cruiseDir = (trackFwd + _scanTrackRight * Mathf.Clamp(-cruiseNorm * 0.42f, -0.3f, 0.3f)).normalized;
                    targetDir = Vector3.Slerp(targetDir, cruiseDir, followCruiseLaneHoldBlend).normalized;
                }

                if (_preCommitActive)
                    targetDir = Vector3.Slerp(targetDir, _preCommitDir, preCommitSteerBlend).normalized;
                else if (_ambientSideNudge)
                    targetDir = Vector3.Slerp(targetDir, _nudgeHeading, preCommitSteerBlend * 0.55f).normalized;
            }
        }

        if (!_isOnRoad && offTrackRecoverySteerWeight > 0f)
        {
            Vector3 toT = _trackCenterAtDist - transform.position;
            toT.y = 0f;
            if (toT.sqrMagnitude > 0.25f)
            {
                toT.Normalize();
                targetDir = Vector3.Slerp(targetDir, toT, offTrackRecoverySteerWeight).normalized;
            }
        }

        float angleToTarget = Vector3.SignedAngle(_currentForward, targetDir, Vector3.up);
        float rawSteer;
        bool lightFollow = _phase == NpcDrivePhase.FollowingTrack && !_preCommitActive && !_ambientSideNudge;
        if (Mathf.Abs(angleToTarget) <= steeringDeadZoneDeg && lightFollow)
            rawSteer = 0f;
        else
        {
            float speedF = Mathf.InverseLerp(0f, turnRateFalloffSpeed, _currentSpeed);
            float turnRate = Mathf.Lerp(baseTurnRateDegPerSec, minTurnRateDegPerSec, speedF);
            float alignScale = _phase == NpcDrivePhase.AvoidingObstacle ? Mathf.Lerp(1f, 0.65f, _avoidanceUrgency) : 1f;
            // Use heading error + alignment for steering gain — NOT (turnRate * align), so high max yaw rates
            // don't accidentally shrink steering input (that made 1800°/s feel slower than 140°/s).
            float effAlign = Mathf.Max(0.08f, alignmentTime * alignScale);
            float cap = Mathf.Max(8f, headingErrorForFullSteerDeg * effAlign);
            rawSteer = Mathf.Clamp(angleToTarget / cap, -1f, 1f);
            float angleRate = (angleToTarget - _prevAngleToTarget) / Mathf.Max(dt, 0.0001f);
            bool correcting = (angleToTarget > 0f && angleRate < 0f) || (angleToTarget < 0f && angleRate > 0f);
            if (steeringDamping > 0f && correcting)
                rawSteer *= (1f - steeringDamping);
        }
        _prevAngleToTarget = angleToTarget;

        bool heavyAvoid = _phase == NpcDrivePhase.AvoidingObstacle;
        bool recovering = _phase == NpcDrivePhase.RecoveringFromAvoid;
        if (enableRoadBoundaryDetection && !heavyAvoid && Mathf.Abs(_roadSteerHint) > 0.04f)
        {
            float w = Mathf.Abs(_roadSteerHint);
            if (recovering)
                w *= 0.2f;
            rawSteer = Mathf.Lerp(rawSteer, _roadSteerHint, w);
        }
        else if (enableRoadBoundaryDetection && heavyAvoid && avoidanceLaneCenterSteerBlend > 0f)
        {
            float halfRoad = GetHalfRoadWidth();
            if (halfRoad > 0.01f)
            {
                float norm = Mathf.Clamp(_distanceFromTrackCenter / halfRoad, -1f, 1f);
                if (Mathf.Abs(norm) > 0.035f)
                {
                    float steerCenter = Mathf.Clamp(-norm, -1f, 1f);
                    float u = avoidanceLaneCenterSteerBlend * Mathf.Clamp01(Mathf.Abs(norm) * 1.2f);
                    rawSteer = Mathf.Lerp(rawSteer, steerCenter, u);
                }
            }
        }

        rawSteer = Mathf.Clamp(rawSteer, -1f, 1f);
        if (recovering)
            rawSteer = Mathf.Clamp(rawSteer, -recoveryMaxSteer, recoveryMaxSteer);
        float smoothT = heavyAvoid ? steeringSmoothTime * 0.52f : steeringSmoothTime;
        if (recovering)
            smoothT = steeringSmoothTime * 1.35f;
        _smoothedSteering = Mathf.SmoothDamp(_smoothedSteering, rawSteer, ref _steeringVel, smoothT);

        if (drawSteeringDebug)
        {
            Debug.DrawRay(transform.position + Vector3.up, trackFwd * 3f, Color.blue);
            Debug.DrawRay(transform.position + Vector3.up, targetDir * 3f, Color.yellow);
            Debug.DrawRay(transform.position + Vector3.up, _currentForward * 3f, Color.white);
        }
    }

    private void UpdateMovement(float dt)
    {
        if (_phase == NpcDrivePhase.RecoveringFromAvoid)
            UpdateRecoveryPhase(dt);

        float speedF = Mathf.InverseLerp(0f, turnRateFalloffSpeed, _currentSpeed);
        float turnRate = Mathf.Lerp(baseTurnRateDegPerSec, minTurnRateDegPerSec, speedF);
        float turnAmt = _smoothedSteering * turnRate * dt;
        _currentForward = Quaternion.Euler(0f, turnAmt, 0f) * _currentForward;
        _currentForward.y = 0f;
        _currentForward.Normalize();

        _targetSpeed = speed;
        if (!_isOnRoad && offTrackRecoverySteerWeight > 0f)
            _targetSpeed *= 0.72f;

        if (slowDownWhileAvoiding && _phase == NpcDrivePhase.AvoidingObstacle)
        {
            float m = Mathf.Lerp(1f, avoidingSpeedMultiplier, _avoidanceUrgency);
            _targetSpeed *= m;
        }

        float maxChg = Mathf.Max(accelerationRate, decelerationRate);
        _currentSpeed = Mathf.SmoothDamp(_currentSpeed, _targetSpeed, ref _speedVel, speedSmoothTime, maxChg);

        Vector3 move = _currentForward * _currentSpeed * dt;
        Vector3 newPos = transform.position + move;

        LayerMask drive = driveableLayer.value != 0 ? driveableLayer : roadLayer;
        if (validateMovementOnDriveable && drive.value != 0)
        {
            Vector3 chk = newPos + Vector3.up * raycastStartHeight;
            if (!Physics.Raycast(chk, Vector3.down, raycastStartHeight + raycastDownDistance, drive, QueryTriggerInteraction.Ignore))
            {
                _currentSpeed *= 0.9f;
                return;
            }
        }

        if (RaycastGround(newPos, out RaycastHit gh))
        {
            newPos.y = gh.point.y + _pivotToBottom + groundClearance;
            _groundNormal = gh.normal;
        }

        if (_rb != null)
        {
            _rb.MovePosition(newPos);
            if (_currentSpeed > minSpeedForRotation || _phase == NpcDrivePhase.AvoidingObstacle)
            {
                Quaternion targetRot = Quaternion.LookRotation(_currentForward, Vector3.up);
                if (alignToGround && _groundNormal != Vector3.up)
                {
                    Vector3 gf = Vector3.ProjectOnPlane(_currentForward, _groundNormal).normalized;
                    if (gf.sqrMagnitude > 0.01f)
                        targetRot = Quaternion.LookRotation(gf, _groundNormal);
                }
                _rb.MoveRotation(targetRot);
            }
        }
        else
        {
            transform.position = newPos;
            if (_currentSpeed > minSpeedForRotation)
            {
                Quaternion targetRot = Quaternion.LookRotation(_currentForward, Vector3.up);
                if (alignToGround && _groundNormal != Vector3.up)
                {
                    Vector3 gf = Vector3.ProjectOnPlane(_currentForward, _groundNormal).normalized;
                    if (gf.sqrMagnitude > 0.01f)
                        targetRot = Quaternion.LookRotation(gf, _groundNormal);
                }
                transform.rotation = targetRot;
            }
        }
    }

    private void UpdateRecoveryPhase(float dt)
    {
        _recoveryPhaseTime += Mathf.Max(0f, dt);

        bool noForwardThreat = _forwardThreatStreak == 0;
        if (!noForwardThreat && _avoidanceObstaclePointValid)
        {
            Vector3 toThreat = _avoidanceObstaclePoint - transform.position;
            toThreat.y = 0f;
            float along = Vector3.Dot(toThreat, _scanCarForward);
            noForwardThreat = along < -obstacleBehindRequiredForExit;
        }

        float headingErr = Mathf.Abs(Vector3.SignedAngle(_currentForward, _scanTrackFwd, Vector3.up));
        bool headingStable = headingErr <= recoveryHeadingErrorDeg;
        bool laneHoldNearTarget = Mathf.Abs(_distanceFromTrackCenter - _recoveryTargetLateralOffset) <= Mathf.Max(0.25f, GetHalfRoadWidth() * 0.14f);
        bool minTimeMet = _recoveryPhaseTime >= recoveryMinDuration;

        if (noForwardThreat && headingStable && laneHoldNearTarget && minTimeMet)
            _recoveryClearStreak++;
        else
            _recoveryClearStreak = 0;

        if (_recoveryClearStreak >= recoveryClearFramesToExit)
        {
            _phase = NpcDrivePhase.FollowingTrack;
            _recoveryPhaseTime = 0f;
            _recoveryClearStreak = 0;
            _cruiseTargetLateralOffset = _recoveryTargetLateralOffset;
            _recoveryTargetLateralOffset = 0f;
            _avoidanceObstaclePointValid = false;
            _lastPrimaryThreatId = -1;
        }
    }

    private bool InitializeIfNeeded()
    {
        if (_initialized) return true;

        if (trackGenerator == null)
            trackGenerator = FindFirstObjectByType<ProceduralTrackGenerator>();

        if (trackGenerator == null)
        {
            if (verboseDebug) Debug.LogWarning("[NPCTrafficCar] No track generator found!");
            return false;
        }

        RebuildPathFromGenerator();
        if (_path.Count < 2 || _totalLength < 1f) return false;

        if (randomizeSpeed)
            speed = UnityEngine.Random.Range(speedRange.x, speedRange.y);

        _baseSpeed = speed;
        _targetSpeed = speed;
        _currentSpeed = speed * 0.5f;

        if (autoFitRootColliderToVisual)
            TryFitRootColliderToVisual();

        ComputePivotToBottom();
        ComputeAvoidanceSphereRadius();

        _dist = GetDistanceAlongPath(transform.position);
        SampleAlongPath(_dist, out _, out Vector3 tf);
        _currentForward = tf;
        _currentForward.y = 0f;
        _currentForward.Normalize();
        if (_currentForward.sqrMagnitude < 0.01f)
            _currentForward = transform.forward;

        _initialized = true;
        if (verboseDebug)
            Debug.Log($"[NPCTrafficCar] Initialized: dist={_dist:F1}m, speed={speed:F1}");
        return true;
    }

    private void TryFitRootColliderToVisual()
    {
        if (_col == null) return;

        if (!TryGetVisualLocalBounds(out Bounds localVisualBounds))
        {
            if (verboseDebug) Debug.LogWarning("[NPCTrafficCar] Collider auto-fit skipped: no renderers found.");
            return;
        }

        // Avoid degenerate collider dimensions.
        Vector3 size = localVisualBounds.size;
        size.x = Mathf.Max(0.05f, size.x);
        size.y = Mathf.Max(0.05f, size.y);
        size.z = Mathf.Max(0.05f, size.z);
        Vector3 center = localVisualBounds.center;

        if (_col is BoxCollider box)
        {
            box.center = center;
            box.size = size;
            return;
        }

        if (_col is CapsuleCollider cap)
        {
            cap.center = center;

            // Match length on the capsule axis and radius on the perpendicular axes.
            switch (cap.direction)
            {
                case 0: // X axis
                    cap.height = Mathf.Max(size.x, 0.05f);
                    cap.radius = Mathf.Max(0.025f, Mathf.Max(size.y, size.z) * 0.5f);
                    break;
                case 1: // Y axis
                    cap.height = Mathf.Max(size.y, 0.05f);
                    cap.radius = Mathf.Max(0.025f, Mathf.Max(size.x, size.z) * 0.5f);
                    break;
                default: // Z axis
                    cap.height = Mathf.Max(size.z, 0.05f);
                    cap.radius = Mathf.Max(0.025f, Mathf.Max(size.x, size.y) * 0.5f);
                    break;
            }
            return;
        }

        if (!replaceUnsupportedRootCollider) return;

        // Replace unsupported collider types (e.g. MeshCollider) with a fitted box.
        Destroy(_col);
        BoxCollider fallback = gameObject.AddComponent<BoxCollider>();
        fallback.center = center;
        fallback.size = size;
        _col = fallback;
    }

    private bool TryGetVisualLocalBounds(out Bounds localBounds)
    {
        localBounds = default;
        var renderers = GetComponentsInChildren<Renderer>(true);
        bool hasAny = false;

        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer r = renderers[i];
            if (r == null) continue;
            if (!r.enabled) continue;
            if (r is ParticleSystemRenderer || r is TrailRenderer || r is LineRenderer) continue;

            Bounds wb = r.bounds;
            if (!hasAny)
            {
                localBounds = WorldBoundsToLocalAabb(wb);
                hasAny = true;
            }
            else
            {
                localBounds.Encapsulate(WorldBoundsToLocalAabb(wb).min);
                localBounds.Encapsulate(WorldBoundsToLocalAabb(wb).max);
            }
        }

        return hasAny;
    }

    private Bounds WorldBoundsToLocalAabb(Bounds worldBounds)
    {
        Vector3 c = worldBounds.center;
        Vector3 e = worldBounds.extents;

        Vector3[] corners = new Vector3[8]
        {
            new Vector3(c.x - e.x, c.y - e.y, c.z - e.z),
            new Vector3(c.x + e.x, c.y - e.y, c.z - e.z),
            new Vector3(c.x - e.x, c.y + e.y, c.z - e.z),
            new Vector3(c.x + e.x, c.y + e.y, c.z - e.z),
            new Vector3(c.x - e.x, c.y - e.y, c.z + e.z),
            new Vector3(c.x + e.x, c.y - e.y, c.z + e.z),
            new Vector3(c.x - e.x, c.y + e.y, c.z + e.z),
            new Vector3(c.x + e.x, c.y + e.y, c.z + e.z)
        };

        Vector3 min = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
        Vector3 max = new Vector3(float.MinValue, float.MinValue, float.MinValue);
        for (int i = 0; i < corners.Length; i++)
        {
            Vector3 p = transform.InverseTransformPoint(corners[i]);
            min = Vector3.Min(min, p);
            max = Vector3.Max(max, p);
        }

        Bounds b = new Bounds((min + max) * 0.5f, max - min);
        return b;
    }

    private void ComputePivotToBottom()
    {
        if (_col == null) return;
        Bounds b = _col.bounds;
        _pivotToBottom = Mathf.Max(0f, transform.position.y - b.min.y);
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
        return _col != null ? _col.bounds.center : transform.position + Vector3.up * rayHeight;
    }

    private void RebuildPathFromGenerator()
    {
        _path.Clear();
        _cumLengths = null;
        _totalLength = 0f;
        if (trackGenerator == null) return;
        TrackPathSampling.RebuildPathFromRoadCenterline(trackGenerator, _path, ref _cumLengths, out _totalLength);
    }

    private void SampleAlongPath(float dist, out Vector3 pos, out Vector3 forward)
    {
        pos = Vector3.zero;
        forward = Vector3.forward;
        if (_path.Count < 2 || _cumLengths == null) return;
        TrackPathSampling.SampleAlongPath(_path, _cumLengths, _totalLength, dist, out pos, out forward);
    }

    private float GetDistanceAlongPath(Vector3 worldPos)
    {
        if (_path.Count < 2 || _cumLengths == null) return 0f;

        float bestSqr = float.MaxValue;
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
            float d = (worldPos - proj).sqrMagnitude;
            if (d < bestSqr)
            {
                bestSqr = d;
                bestIdx = i;
                bestT = t;
            }
        }

        float segLen = Vector3.Distance(_path[bestIdx], _path[bestIdx + 1]);
        return _cumLengths[bestIdx] + bestT * segLen;
    }

    /// <summary>
    /// Approximate arc-length along this car’s traffic spline under its current position.
    /// </summary>
    public bool TryGetDistanceAlongTrack(out float distanceAlong)
    {
        distanceAlong = 0f;
        if (_path.Count < 2 || _cumLengths == null) return false;
        distanceAlong = Mathf.Clamp(GetDistanceAlongPath(transform.position), 0f, _totalLength);
        return true;
    }

    private float GetHalfRoadWidth()
    {
        return trackGenerator != null ? trackGenerator.RoadWidth * 0.5f : 5f;
    }

    private bool RaycastGround(Vector3 pos, out RaycastHit hit)
    {
        Vector3 o = pos + Vector3.up * raycastStartHeight;
        float maxD = raycastStartHeight + raycastDownDistance;
        LayerMask g = driveableLayer.value != 0 ? driveableLayer : roadLayer;
        if (g.value == 0) g = roadLayer;
        return Physics.Raycast(o, Vector3.down, out hit, maxD, g, QueryTriggerInteraction.Ignore);
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
            2f * p1 +
            (-p0 + p2) * t +
            (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 +
            (-p0 + 3f * p1 - 3f * p2 + p3) * t3
        );
    }

    private void UpdateSurfaceEffects(float dt)
    {
        _surfaceTimer -= dt;
        if (_surfaceTimer <= 0f)
        {
            _surfaceTimer = surfaceCheckInterval;
            CheckSurfaceUnderCar();
        }

        if (_boostEndTime > 0f && Time.time > _boostEndTime)
        {
            _boostEndTime = 0f;
            _onBoostPad = false;
        }

        _currentSpeedMul = Mathf.Lerp(_currentSpeedMul, _targetSpeedMul, surfaceSpeedLerpRate * dt);
        float newSpd = _baseSpeed * _currentSpeedMul;
        if (_onBoostPad || _boostEndTime > Time.time)
            newSpd += boostPadSpeedBonus;
        speed = newSpd;
    }

    private void CheckSurfaceUnderCar()
    {
        Vector3 o = transform.position + Vector3.up * raycastStartHeight;
        float maxD = raycastStartHeight + raycastDownDistance;
        LayerMask layers = surfaceDetectionLayers.value != 0 ? surfaceDetectionLayers : roadLayer;

        if (Physics.Raycast(o, Vector3.down, out RaycastHit hit, maxD, layers, QueryTriggerInteraction.Collide))
        {
            GroundSurface surf = hit.collider.GetComponent<GroundSurface>();
            if (surf == null) surf = hit.collider.GetComponentInParent<GroundSurface>();
            if (surf != null)
                ApplySurfaceEffects(surf);
            else
            {
                _targetSpeedMul = 1f;
                _currentSurfaceType = "Normal";
            }
        }
    }

    private void ApplySurfaceEffects(GroundSurface surface)
    {
        _currentSurfaceType = surface.surfaceType.ToString();
        _targetSpeedMul = Mathf.Clamp(surface.maxSpeedMultiplier, 0.1f, 5f);

        bool boost = surface.maxSpeedMultiplier > boostPadThreshold &&
                     surface.accelerationMultiplier > boostPadThreshold;
        if (boost && !_onBoostPad)
        {
            _onBoostPad = true;
            _boostEndTime = Time.time + boostPadDuration;
            if (verboseDebug)
                Debug.Log($"[NPCTrafficCar] BOOST PAD! baseSpeed={_baseSpeed:F1}");
        }
        else if (!boost)
            _onBoostPad = false;
    }

    /// <summary>
    /// Scripted <see cref="RollingLogAlongTrack"/> uses a kinematic rigidbody; NPC traffic also moves kinematically.
    /// PhysX does not report <see cref="OnCollisionEnter"/> between those two, so the log overlap-probes each step and calls here.
    /// </summary>
    public void ApplyScriptedRollingLogOverlapHit(RollingLogAlongTrack log, Collider logCollider, Vector3 contactWorld, float impactSpeed)
    {
        if (_crashed || log == null || logCollider == null) return;
        if (!ShouldCrashWith(logCollider)) return;

        _rollingLogRamPending = false;
        if (log.TryGetVehicleRamImpulse(null, out Vector3 planarU, out float hi, out float ui))
        {
            _rollingLogRamPending = true;
            _rollingLogPlanarUnit = planarU;
            _rollingLogHorizImpulse = hi;
            _rollingLogUpImpulse = ui;
        }

        Vector3 impactDir = transform.position - contactWorld;
        impactDir.y = 0f;
        if (impactDir.sqrMagnitude < 0.001f) impactDir = -transform.forward;
        impactDir.Normalize();

        TriggerCrash(impactDir, impactSpeed, logCollider, "RollingLogOverlap");
    }

    /// <summary>
    /// Scripted <see cref="CrossTrackObstacle"/> is kinematic on its path; NPC traffic is kinematic too.
    /// PhysX may not report <see cref="OnCollisionEnter"/> between them — the cross overlap-probes and calls here.
    /// </summary>
    public void ApplyScriptedCrossTrackOverlapHit(CrossTrackObstacle cross, Collider crossCollider, float impactSpeed)
    {
        if (_crashed || cross == null || crossCollider == null) return;
        if (!ShouldCrashWith(crossCollider)) return;

        Vector3 contact = crossCollider.ClosestPoint(transform.position);
        Vector3 impactDir = transform.position - contact;
        impactDir.y = 0f;
        if (impactDir.sqrMagnitude < 0.001f) impactDir = -transform.forward;
        impactDir.Normalize();

        float rel = Mathf.Max(impactSpeed, cross.GetWorldVelocity().magnitude, _lastVelocity.magnitude, 3f);
        TriggerCrash(impactDir, rel, crossCollider, "CrossTrackOverlap");
    }

    /// <summary>
    /// Scripted <see cref="ShuttleTrackObstacle"/> is kinematic while on-path; NPC traffic is kinematic too.
    /// PhysX can skip <see cref="OnCollisionEnter"/> between scripted kinematic movers, so shuttle callbacks use this path.
    /// </summary>
    public void ApplyScriptedShuttleTrackOverlapHit(ShuttleTrackObstacle shuttle, Collider shuttleCollider, float impactSpeed)
    {
        if (_crashed || shuttle == null || shuttleCollider == null) return;
        if (!ShouldCrashWith(shuttleCollider)) return;

        Vector3 contact = shuttleCollider.ClosestPoint(transform.position);
        Vector3 impactDir = transform.position - contact;
        impactDir.y = 0f;
        if (impactDir.sqrMagnitude < 0.001f) impactDir = -transform.forward;
        impactDir.Normalize();

        float rel = Mathf.Max(impactSpeed, shuttle.GetWorldVelocity().magnitude, _lastVelocity.magnitude, 3f);
        TriggerCrash(impactDir, rel, shuttleCollider, "ShuttleTrackOverlap");
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (_crashed) return;
        if (collision == null || collision.collider == null) return;
        if (!ShouldCrashWith(collision.collider)) return;

        _rollingLogRamPending = false;
        RollingLogAlongTrack log = collision.collider.GetComponentInParent<RollingLogAlongTrack>();
        if (log != null && log.TryGetVehicleRamImpulse(collision, out Vector3 planarU, out float hi, out float ui))
        {
            _rollingLogRamPending = true;
            _rollingLogPlanarUnit = planarU;
            _rollingLogHorizImpulse = hi;
            _rollingLogUpImpulse = ui;
        }

        Vector3 impactDir = collision.contactCount > 0 ? -collision.GetContact(0).normal : transform.forward;
        float impactSpeed = collision.relativeVelocity.magnitude;
        TriggerCrash(impactDir, impactSpeed, collision.collider, "OnCollisionEnter");
    }

    private void OnTriggerEnter(Collider other)
    {
        if (_crashed) return;
        if (other == null) return;
        if (!ShouldCrashWith(other)) return;

        Vector3 impactDir = (other.transform.position - transform.position).normalized;
        if (impactDir.sqrMagnitude < 0.001f) impactDir = transform.forward;
        TriggerCrash(impactDir, _lastVelocity.magnitude, other, "OnTriggerEnter");
    }

    private void CheckOverlapAndCrash()
    {
        if (_crashed) return;
        if (crashLayers.value == 0) return;
        if (_col == null) return;

        float baseR = GetPlanarColliderRadius(_col) + 0.05f;
        float r = Mathf.Max(0.05f, baseR * Mathf.Max(0.01f, crashOverlapRadiusMultiplier));
        Collider[] hits = Physics.OverlapSphere(_col.bounds.center, r, crashLayers, QueryTriggerInteraction.Ignore);
        if (hits == null) return;

        foreach (var other in hits)
        {
            if (other == null || other.transform.IsChildOf(transform) || other == _col) continue;
            if (!ShouldCrashWith(other)) continue;

            if (Physics.ComputePenetration(
                    _col, transform.position, transform.rotation,
                    other, other.transform.position, other.transform.rotation,
                    out Vector3 pushDir, out float pushDist))
            {
                Vector3 impactDir = pushDist > 0.0001f ? -pushDir : (other.transform.position - transform.position).normalized;
                TriggerCrash(impactDir, _lastVelocity.magnitude, other, "OverlapSphere");
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
            string name = LayerMask.LayerToName(layer);
            if (name == "RoadSurface" || name == "Road" || name == "Terrain")
                return false;
        }
        return true;
    }

    private void TriggerCrash(Vector3 impactDir, float impactSpeed, Collider other, string crashMethod)
    {
        if (_crashed) return;

        if (other != null && other.GetComponentInParent<CarController>() != null)
            Debug.Log("Crash Initiator " + other.gameObject.name + " via " + crashMethod);

        _crashed = true;

        if (verboseDebug)
            Debug.Log($"[NPCTrafficCar] CRASHED with {other.name}");

        StopEngineAudio();
        PlayCrashSfx();
        SpawnCrashVFX();
        ConvertToPhysics(impactDir, impactSpeed);

        if (destroyAfterCrash)
            Invoke(nameof(DestroySelf), destroyDelay);
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

        if (_rollingLogRamPending)
        {
            float invM = 1f / Mathf.Max(_rb.mass, 0.01f);
            velocity += _rollingLogPlanarUnit * (_rollingLogHorizImpulse * invM) + Vector3.up * (_rollingLogUpImpulse * invM);
            _rollingLogRamPending = false;
        }

        _rb.velocity = velocity;

        float spinMag = UnityEngine.Random.Range(crashSpinRange.x, crashSpinRange.y) * Mathf.Deg2Rad;
        Vector3 spinAxis = new Vector3(
            UnityEngine.Random.Range(-0.2f, 0.2f),
            UnityEngine.Random.Range(0.5f, 1f),
            UnityEngine.Random.Range(-0.2f, 0.2f)
        ).normalized;
        _rb.angularVelocity = spinAxis * spinMag;
    }

    private void SetupEngineAudio()
    {
        if (engineClip == null) return;
        if (_engineSource == null)
            _engineSource = gameObject.AddComponent<AudioSource>();
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
        float n = Mathf.Clamp01(_currentSpeed / Mathf.Max(0.01f, speedRange.y));
        _engineSource.pitch = Mathf.Lerp(enginePitchMin, enginePitchMax, n);
    }

    private void StopEngineAudio()
    {
        if (_engineSource != null)
            _engineSource.Stop();
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

    private void DestroySelf()
    {
        Destroy(transform.parent != null ? transform.parent.gameObject : gameObject);
    }

    public void ForceCrashFromForcefield(Vector3 worldImpactFrom, float impactSpeed, Collider source)
    {
        if (_crashed) return;
        Vector3 impactDir = worldImpactFrom - transform.position;
        impactDir.y = 0f;
        if (impactDir.sqrMagnitude < 0.0001f) impactDir = transform.forward;
        impactDir.Normalize();
        TriggerCrash(impactDir, impactSpeed, source != null ? source : _col, "ForceCrashFromForcefield");
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

    private void OnDrawGizmos()
    {
        if (drawDestinationGizmo && _path != null && _path.Count >= 2 && _cumLengths != null)
        {
            float lookDist = Mathf.Min(_dist + trackLookAhead, _totalLength);
            SampleAlongPath(lookDist, out Vector3 lookPos, out _);
            Gizmos.color = Color.green;
            Gizmos.DrawWireSphere(lookPos, 0.8f);
            Gizmos.DrawLine(transform.position, lookPos);
        }

        if (drawDestinationGizmo && _phase == NpcDrivePhase.AvoidingObstacle)
        {
            Gizmos.color = Color.Lerp(Color.yellow, Color.red, _avoidanceUrgency);
            Vector3 p = _avoidanceObstaclePointValid ? _avoidanceObstaclePoint : transform.position + Vector3.up * 2f;
            Gizmos.DrawWireSphere(p, 0.5f);
        }

        if (drawObstacleRays)
        {
            float baseR = Application.isPlaying ? Mathf.Max(0.1f, _avoidanceSphereRadius) : 0.8f;
            float clearR = Application.isPlaying ? Mathf.Max(baseR, baseR + hitboxClearance) : 1.4f;
            Vector3 c = Application.isPlaying ? GetAvoidanceSphereCenter() : transform.position + Vector3.up * 0.5f;
            Gizmos.color = new Color(0.2f, 0.8f, 1f, 0.7f);
            Gizmos.DrawWireSphere(c, baseR);
            Gizmos.color = new Color(1f, 0.65f, 0.15f, 0.7f);
            Gizmos.DrawWireSphere(c, clearR);
        }

        if (drawOverlapSphereGizmo)
        {
            float overlapR = Application.isPlaying
                ? Mathf.Max(0.15f, (_avoidanceSphereRadius + hitboxClearance) * Mathf.Max(0.01f, avoidanceOverlapRadiusMultiplier))
                : 1.2f;
            Vector3 overlapCenter = (_col != null)
                ? _col.bounds.center
                : transform.position + Vector3.up * Mathf.Max(0.4f, rayHeight);
            Gizmos.color = new Color(1f, 0.25f, 0.8f, 0.85f);
            Gizmos.DrawWireSphere(overlapCenter, overlapR);
        }

        if (drawCrashOverlapDetectionGizmo)
        {
            float crashR = 0.9f;
            Vector3 crashCenter = transform.position + Vector3.up * Mathf.Max(0.4f, rayHeight);
            if (_col != null)
            {
                crashCenter = _col.bounds.center;
                float baseR = GetPlanarColliderRadius(_col) + 0.05f;
                crashR = Mathf.Max(0.05f, baseR * Mathf.Max(0.01f, crashOverlapRadiusMultiplier));
            }

            Gizmos.color = enableOverlapDetection
                ? new Color(1f, 0.1f, 0.1f, 0.92f)
                : new Color(0.55f, 0.55f, 0.55f, 0.75f);
            Gizmos.DrawWireSphere(crashCenter, crashR);
        }
    }
}
