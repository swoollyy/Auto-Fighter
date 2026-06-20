using System.Collections;
using DG.Tweening;
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

    [Header("Impact Fling")]
    [Tooltip("Enable extra fling impulse when converting to physics.")]
    [SerializeField] private bool enableImpactFling = true;
    [Tooltip("Horizontal impulse scale based on impact speed.")]
    [SerializeField] private float impactHorizontalMultiplier = 0.6f;
    [Tooltip("Upward impulse scale based on impact speed.")]
    [SerializeField] private float impactUpwardMultiplier = 0.35f;
    [Tooltip("Maximum extra speed added by the fling (clamps the boost).")]
    [SerializeField] private float impactMaxExtraSpeed = 10f;

    [Header("Physics Jump Rotation")]
    [Tooltip("Enable cartoony random rotation when jumping to physics.")]
    [SerializeField] private bool enableJumpRotation = true;
    [Tooltip("Base angular velocity magnitude applied on jump (degrees/sec).")]
    [SerializeField] private Vector2 jumpAngularSpeedRange = new Vector2(180f, 360f);
    [Tooltip("How much impact severity scales the angular velocity (0 = constant, 1 = full scale).")]
    [SerializeField, Range(0f, 1f)] private float impactAngularScale = 0.7f;

    [Header("Motion")]
    [SerializeField] private float speed = 5f;
    [Tooltip("If true, choose a random speed from speedRange at startup.")]
    [SerializeField] private bool useRandomSpeed = false;
    [Tooltip("Random speed range (min–max) used when useRandomSpeed is enabled.")]
    [SerializeField] private Vector2 speedRange = new Vector2(3f, 8f);
    [Tooltip("Start from left bound heading right (if false, starts from right heading left).")]
    [SerializeField] private bool startOnLeft = true;
    [Tooltip("Wait at each end before reversing direction.")]
    [SerializeField] private float waitAtEndSeconds = 0.25f;

    [Tooltip("If true, use a random wait time at each end instead of a fixed value.")]
    [SerializeField] private bool useRandomWaitAtEnd = false;
    [Tooltip("Random wait range (min–max seconds) at each end.")]
    [SerializeField] private Vector2 waitAtEndRange = new Vector2(0.1f, 0.6f);

    [Header("Track Binding (optional)")]
    [SerializeField] private ProceduralTrackGenerator trackGenerator;

    [Tooltip("On spawn, align the shuttle's rotation to the track tangent so its body matches the drawn travel line (removes the slight off-angle look). Keeps the spawned facing direction and forces it upright.")]
    [SerializeField] private bool alignRotationToTrack = true;

    [Header("Path Length Randomization")]
    [Tooltip("If true, each shuttle picks a random fraction of the full lane width for its travel path on spawn.")]
    [SerializeField] private bool randomizePathLength = true;

    [Tooltip("Minimum fraction of the full usable lane width used for this shuttle's travel path.")]
    [SerializeField, Range(0.1f, 1f)] private float minPathFraction = 0.4f;

    [Tooltip("Maximum fraction of the full usable lane width used for this shuttle's travel path.")]
    [SerializeField, Range(0.1f, 1f)] private float maxPathFraction = 1f;

    [Header("Flat Path Constraint")]
    [Tooltip("Keep shuttle path strictly flat in world Y and trim endpoint span when edge terrain starts rising/falling.")]
    [SerializeField] private bool keepPathFlatAndTrimOnElevation = true;
    [Tooltip("Max allowed terrain height delta (meters) from path origin before endpoint is trimmed inward.")]
    [SerializeField, Min(0f)] private float maxAllowedPathElevationDelta = 0.15f;
    [Tooltip("Iterations used when trimming each endpoint inward for flat travel.")]
    [SerializeField, Range(1, 24)] private int endpointTrimIterations = 10;

    [Header("Screen Shake")]
    [SerializeField] private bool enableScreenShake = true;

    [SerializeField] private float moveShakeIntensity = 0.14f;
    [SerializeField] private float moveShakeFrequency = 20f;

    [SerializeField] private float waitShakeIntensity = 0.10f;
    [SerializeField] private float waitShakeFrequency = 14f;

    [SerializeField] private float snapShakeIntensity = 0.22f;
    [SerializeField] private float snapShakeFrequency = 28f;

    [SerializeField] private float shakeMaxDistance = 35f;
    [SerializeField] private float shakeFullIntensityDistance = 6f;

    private bool _didSnapShakeThisWait = false;

    // runtime
    private Vector3 _originWS;
    private Vector3 _leftWS, _rightWS;
    private Vector3 _targetWS;
    private bool _waiting;
    private float _halfRoad, _selfHalf;
    private Vector3 _impactDir;
    private float _impactSpeed; // NEW: track impact speed for rotation scaling
    private bool _hasImpactDir;
    private bool _convertedToPhysics;
    private bool _travelFxEnabled;

    private Rigidbody _rb;
    private float _bottomOffset = 0f;
    private float _safeMargin = 0.02f;

    [Header("Physics")]
    [Tooltip("Rigidbody mass for this mover (crash / physics).")]
    [SerializeField, Min(0.01f)] private float obstacleMass = 12f;

    [Header("Cross-track interaction")]
    [Tooltip("When a scripted CrossTrackObstacle hits this shuttle, the cross keeps its path and this shuttle converts to physics with an extra fling.")]
    [SerializeField] private bool enableCrossTrackRam = true;
    [Tooltip("Multiplies impact severity used for fling / spin when rammed by a cross (1 = default).")]
    [SerializeField, Min(0.5f)] private float crossRamImpactSeverityScale = 1.35f;
    [Tooltip("Minimum relative speed (m/s) assumed for cross ram if collision data is weak.")]
    [SerializeField, Min(0f)] private float crossRamMinEffectiveSpeed = 7f;

    [Header("Bump — obstacles when shuttle keeps path")]
    [Tooltip("If true, hitting props (not path-loss instigators) adds upward velocity while the shuttle stays scripted.")]
    [SerializeField] private bool enableNonPathLossObstacleBump = true;
    [SerializeField, Min(0f)] private float nonPathLossUpVelocityChange = 3.5f;
    [SerializeField, Min(0.5f)] private float nonPathLossBumpSpeedRef = 10f;
    [SerializeField] private Vector2 nonPathLossBumpSpeedScaleRange = new Vector2(0.45f, 1.15f);
    [SerializeField] private bool nonPathLossWakeKinematicObstacles = true;

    [Header("Collision → Convert To Physics")]
    [Tooltip("When enabled, ignore RoadSurface/Terrain layer collisions (still only converts for log / thrown / bounce-back / aggressive beast).")]
    [SerializeField] private bool ignoreRoadAndTerrain = true;
    [Tooltip("Minimum movement speed (m/s) used when transferring scripted motion into Rigidbody velocity on conversion.")]
    [SerializeField] private float minTransferVelocity = 0.25f;

    [Header("Collision Priority Frame Delay")]
    [Tooltip("Extra FixedUpdate frames to wait after detecting collision before converting to physics. Gives more time for car crash logic.")]
    [SerializeField, Range(0, 5)] private int collisionDelayFrames = 2;

    [Tooltip("Enable an overlap-check fallback that detects colliders touching this obstacle even when it's moved via transform.")]
    [SerializeField] private bool enableOverlapDetection = true;
    [Tooltip("How often (seconds) to run the overlap check. Lower = more responsive, higher = cheaper.")]
    [SerializeField] private float overlapCheckInterval = 0.05f;

    [Header("Obstacle clash popup (Crash style)")]
    [SerializeField] private bool enableShuttleClashCrashPopup = true;
    [SerializeField, Min(0f)] private float shuttleClashPopupHeight = 1f;
    [SerializeField, Min(0f)] private float shuttleClashMinRelativeSpeed = 2f;
    [SerializeField, Min(0f)] private float shuttleClashPairCooldown = 0.2f;

    [Header("Telegraphing & FX")]
    [Tooltip("Optional light used to telegraph when the shuttle is about to move again.")]
    [SerializeField] private Light telegraphLight;
    [Tooltip("Enable or disable telegraph light behavior.")]
    [SerializeField] private bool useTelegraphLight = true;
    [Tooltip("Fraction of the wait duration after which the light turns on (0 = immediately, 1 = never).")]
    [SerializeField, Range(0f, 1f)] private float telegraphStartPercent = 0.5f;
    [Tooltip("Light color at the moment it turns on.")]
    [SerializeField] private Color telegraphStartColor = Color.green;
    [Tooltip("Light color right before the shuttle starts moving again.")]
    [SerializeField] private Color telegraphEndColor = Color.red;

    [Tooltip("Optional particle system to play right before the shuttle starts moving again.")]
    [SerializeField] private ParticleSystem launchParticles;
    [Tooltip("Optional audio source to play a sound right before the shuttle starts moving again.")]
    [SerializeField] private AudioSource launchAudio;

    [Header("Final Launch Warning")]
    [Tooltip("Seconds before movement resumes to trigger a strong warning (light flash, particles, sound).")]
    [SerializeField, Min(0f)] private float launchWarningLeadTime = 0.2f;

    [Tooltip("Multiplier applied to the base light intensity during the final warning flash.")]
    [SerializeField] private float launchWarningIntensityMultiplier = 2.5f;

    [Tooltip("Color of the light during the final warning flash. Set this equal to Travel Light Color if you want them matching.")]
    [SerializeField] private Color launchWarningColor = Color.yellow;

    [Header("Pre-shuttle scale bob (DOTween)")]
    [Tooltip("Brief scale-up anticipation right before the shuttle moves again from an end wait.")]
    [SerializeField] private bool enablePreShuttleScaleBob = true;
    [Tooltip("Transform to scale (uses this component’s transform if null).")]
    [SerializeField] private Transform scaleBobTarget;
    [Tooltip("Begin the bob when this many seconds remain in the end wait (should be ≥ rise+fall duration for full effect).")]
    [SerializeField, Min(0.05f)] private float preShuttleBobLeadTime = 0.4f;
    [Tooltip("Uniform local scale multiplier at the peak of the bob.")]
    [SerializeField, Min(1.01f)] private float preShuttleBobPeakMultiplier = 1.08f;
    [SerializeField, Min(0.02f)] private float preShuttleBobRiseDuration = 0.16f;
    [SerializeField, Min(0.02f)] private float preShuttleBobFallDuration = 0.14f;
    [SerializeField] private Ease preShuttleBobRiseEase = Ease.OutBack;
    [SerializeField] private Ease preShuttleBobFallEase = Ease.InQuad;

    [Header("Travel Light State")]
    [Tooltip("If true, the telegraph light stays on; intensity is low while idle/waiting and higher while moving / ramping to launch.")]
    [SerializeField] private bool useTravelLightDuringMotion = true;
    [Tooltip("Color of the light while the shuttle is moving (and base tint while idle).")]
    [SerializeField] private Color travelLightColor = Color.yellow;
    [Tooltip("Multiplier applied to the light’s baseline intensity while moving along the lane.")]
    [SerializeField] private float travelLightIntensityMultiplier = 1.5f;
    [Tooltip("Multiplier while stopped / waiting before the telegraph ramp (light stays on at this level).")]
    [SerializeField, Min(0.01f)] private float idleLightIntensityMultiplier = 0.35f;

    private Vector3 _prevPosition;
    private Vector3 _lastVelocity;

    private Collider[] _childColliders;
    private float _overlapTimer;
    private float _baseLightIntensity = 3.5f;
    private float _spawnGraceUntil = 0f;
    private ObstaclePathPreview _preview;

    // NEW: collision tracking for delayed conversion
    private int _pendingCollisionFrames = 0;
    private Collider _pendingCollider;

    private Vector3 _scaleBobBaseLocal;
    private bool _scaleBobBaseCaptured;
    private bool _preShuttleBobFiredThisWait;

    public void SetGenerator(ProceduralTrackGenerator gen) => trackGenerator = gen;

    /// <summary>True while the shuttle is still lane-scripted (not yet knocked to physics).</summary>
    public bool IsActiveScriptedShuttle => enabled && !_convertedToPhysics && _rb != null && _rb.isKinematic;

    private void Awake()
    {
        if (!trackGenerator)
            trackGenerator = FindObjectOfType<ProceduralTrackGenerator>();

        _rb = GetComponent<Rigidbody>();
        if (_rb == null)
        {
            _rb = gameObject.AddComponent<Rigidbody>();
        }

        _rb.isKinematic = true;
        _rb.useGravity = false;
        _rb.collisionDetectionMode = CollisionDetectionMode.Discrete;
        _rb.interpolation = RigidbodyInterpolation.None;
        _rb.constraints = RigidbodyConstraints.FreezeRotation;
        _rb.mass = Mathf.Max(0.01f, obstacleMass);

        _prevPosition = transform.position;
        _lastVelocity = Vector3.zero;
        _overlapTimer = 0f;

        if (telegraphLight)
        {
            _baseLightIntensity = Mathf.Max(0.01f, telegraphLight.intensity);
            if (useTelegraphLight && useTravelLightDuringMotion)
            {
                telegraphLight.enabled = true;
                telegraphLight.color = travelLightColor;
                telegraphLight.intensity = _baseLightIntensity * idleLightIntensityMultiplier;
            }
            else
                telegraphLight.enabled = false;
        }

        _preview = GetComponent<ObstaclePathPreview>();
        SetTravelFxEnabled(false, true);
    }

    private void Start()
    {
        _originWS = transform.position;

        // Align body to the track frame BEFORE measuring self half-width / edges so everything
        // (and the drawn travel line) shares the same orientation.
        if (alignRotationToTrack)
            AlignRotationToTrack();

        _halfRoad = DetermineHalfRoadWidth();
        _selfHalf = DetermineSelfHalfWidth();

        _childColliders = GetComponentsInChildren<Collider>();

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
        if (halfHeightWorld <= 0f) halfHeightWorld = 0.5f;

        _bottomOffset = ComputeBottomOffset();

        float safeMargin = _safeMargin;

        LayerMask roadMask = LayerMask.GetMask("RoadSurface");
        float upOffsetForCast = halfHeightWorld + 0.5f;
        _originWS = SpawnUtils.ProjectOntoSurface(_originWS, out _, upOffsetForCast, 25f, roadMask);

        ComputeEdgeWorldPositions(out _leftWS, out _rightWS);

        _leftWS = SpawnUtils.ProjectOntoSurface(_leftWS, out _, upOffsetForCast, 25f, roadMask);
        _rightWS = SpawnUtils.ProjectOntoSurface(_rightWS, out _, upOffsetForCast, 25f, roadMask);

        if (keepPathFlatAndTrimOnElevation)
            TrimPathEndpointsForFlatTravel(_originWS);

        if (_preview) _preview.SetEndpoints(_leftWS, _rightWS);

        Vector3 startWS = startOnLeft ? _leftWS : _rightWS;
        Vector3 targetWS = startOnLeft ? _rightWS : _leftWS;

        startWS = SpawnUtils.ProjectOntoSurface(startWS, out Vector3 startNormal, upOffsetForCast, 25f, roadMask);
        _targetWS = SpawnUtils.ProjectOntoSurface(targetWS, out Vector3 targetNormal, upOffsetForCast, 25f, roadMask);

        float startDesiredY = startWS.y - _bottomOffset + safeMargin;

        transform.position = new Vector3(startWS.x, startDesiredY, startWS.z);
        _targetWS = new Vector3(_targetWS.x, startDesiredY, _targetWS.z);

        if (useRandomSpeed)
        {
            float min = Mathf.Min(speedRange.x, speedRange.y);
            float max = Mathf.Max(speedRange.x, speedRange.y);
            speed = Random.Range(min, max);
        }

        _prevPosition = transform.position;
        _lastVelocity = Vector3.zero;
        _spawnGraceUntil = Time.time + 0.1f;

        CaptureScaleBobBase();
        SetTravelFxEnabled(true);
    }

    private void OnEnable()
    {
        // Reset state flags for pooling / re-enable support
        _convertedToPhysics = false;
        _fxKilled = false;
        _waiting = false;
        _pendingCollisionFrames = 0;
        _pendingCollider = null;

        _prevPosition = transform.position;
        _lastVelocity = Vector3.zero;
        _overlapTimer = 0f;

        // Ensure light and preview are OFF until Start() properly initializes
        SetTravelFxEnabled(false, true);

        if (_scaleBobBaseCaptured)
        {
            KillShuttleScaleTweens(false);
            GetScaleBobTarget().localScale = _scaleBobBaseLocal;
        }
    }

    private void Update()
    {
        if (_convertedToPhysics)
            return;

        KillPathFxIfDynamic();
        bool isTravelingNow = !_waiting && !_convertedToPhysics && enabled && _rb != null && _rb.isKinematic;
        SetTravelFxEnabled(isTravelingNow);

        if (enableOverlapDetection)
        {
            _overlapTimer -= Time.deltaTime;
            if (_overlapTimer <= 0f)
            {
                _overlapTimer = Mathf.Max(0.01f, overlapCheckInterval);
                CheckForOverlapAndConvert();
            }
        }

        if (_waiting)
        {
            if (enableScreenShake)
            {
                CarController.RequestWorldShake(transform.position, waitShakeIntensity, waitShakeFrequency,
                    shakeMaxDistance, shakeFullIntensityDistance);
            }
            return;
        }

        float step = Mathf.Max(0.01f, speed) * Time.deltaTime;

        Vector2 curXZ = new Vector2(transform.position.x, transform.position.z);
        Vector2 targetXZ = new Vector2(_targetWS.x, _targetWS.z);
        Vector2 nextXZ = Vector2.MoveTowards(curXZ, targetXZ, step);

        if (enableScreenShake)
        {
            CarController.RequestWorldShake(transform.position, moveShakeIntensity, moveShakeFrequency,
                shakeMaxDistance, shakeFullIntensityDistance);
        }

        // Keep travel Y flat across the lane; do not follow terrain elevation while moving.
        float newY = transform.position.y;

        Vector3 newPos = new Vector3(nextXZ.x, newY, nextXZ.y);

        _lastVelocity = (newPos - _prevPosition) / Mathf.Max(Time.deltaTime, 1e-6f);
        _prevPosition = newPos;

        transform.position = newPos;

        if ((new Vector2(transform.position.x, transform.position.z) - targetXZ).sqrMagnitude <= 0.0001f)
        {
            _targetWS = (_targetWS == _leftWS) ? _rightWS : _leftWS;

            if (useRandomSpeed)
            {
                float sMin = Mathf.Min(speedRange.x, speedRange.y);
                float sMax = Mathf.Max(speedRange.x, speedRange.y);
                speed = Random.Range(sMin, sMax);
            }

            float pauseTime = waitAtEndSeconds;

            if (useRandomWaitAtEnd)
            {
                float wMin = Mathf.Min(waitAtEndRange.x, waitAtEndRange.y);
                float wMax = Mathf.Max(waitAtEndRange.x, waitAtEndRange.y);
                pauseTime = Random.Range(wMin, wMax);
            }

            if (pauseTime > 0f)
            {
                if (enableScreenShake)
                {
                    CarController.RequestWorldShake(transform.position, snapShakeIntensity, snapShakeFrequency,
                        shakeMaxDistance, shakeFullIntensityDistance);
                }

                _didSnapShakeThisWait = true;
                StartCoroutine(WaitThenResume(pauseTime));
            }
        }
    }

    private void FixedUpdate()
    {
        if (_convertedToPhysics) return;
        KillPathFxIfDynamic();

        // NEW: Process pending collision delay
        if (_pendingCollisionFrames > 0)
        {
            _pendingCollisionFrames--;
            if (_pendingCollisionFrames <= 0)
            {
                Collider pending = _pendingCollider;
                _pendingCollider = null;

                // Never drop scripted path from player contact (incl. delayed queue if the car touched during the wait).
                if (pending != null && pending.enabled
                    && TrackMoverPathLossSources.IsInstigator(pending)
                    && !IsPlayerCarCollider(pending))
                    ConvertToPhysicsOnHit();
            }
        }
    }

    private IEnumerator WaitThenResume(float seconds)
    {
        _waiting = true;
        _preShuttleBobFiredThisWait = false;

        // Path preview off while waiting; telegraph light stays on and ramps in this coroutine.
        SetTravelFxEnabled(false, true);

        float totalWait = Mathf.Max(0.0001f, seconds);

        float elapsed = 0f;

        while (elapsed < totalWait)
        {
            elapsed += Time.deltaTime;
            float clampedElapsed = Mathf.Min(elapsed, totalWait);
            float timeRemaining = totalWait - clampedElapsed;

            UpdateShuttleLightDuringWait(clampedElapsed, totalWait, timeRemaining);

            if (enablePreShuttleScaleBob
                && _scaleBobBaseCaptured
                && !_preShuttleBobFiredThisWait
                && timeRemaining <= preShuttleBobLeadTime)
            {
                _preShuttleBobFiredThisWait = true;
                PlayPreShuttleScaleBob(timeRemaining);
            }

            yield return null;
        }

        _waiting = false;
        SetTravelFxEnabled(true);
    }

    private float DetermineHalfRoadWidth()
    {
        float roadWidth = (overrideRoadWidth > 0f)
            ? overrideRoadWidth
            : (trackGenerator ? trackGenerator.RoadWidth : 8f);
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
            Bounds wb = default;
            bool hasBounds = false;

            for (int i = 0; i < rends.Length; i++)
            {
                var rend = rends[i];
                if (!rend) continue;

                // PATCH: ignore path preview line renderer when estimating obstacle width
                if (rend is LineRenderer) continue;

                if (!string.IsNullOrEmpty(bottomOffsetIgnoreTag) && rend.CompareTag(bottomOffsetIgnoreTag))
                    continue;

                if (!hasBounds) { wb = rend.bounds; hasBounds = true; }
                else wb.Encapsulate(rend.bounds);
            }

            if (hasBounds)
            {
                float widthAlongRight = Vector3.Project(wb.size, r).magnitude;
                approx = widthAlongRight * 0.5f;
            }
        }


        return Mathf.Max(0f, approx);
    }

    private void OnDisable()
    {
        StopAllCoroutines();
        KillShuttleScaleTweens(true);
        // Ensure FX are killed when disabled
        SetTravelFxEnabled(false, true);
    }

    private Transform GetScaleBobTarget() => scaleBobTarget != null ? scaleBobTarget : transform;

    private void CaptureScaleBobBase()
    {
        _scaleBobBaseLocal = GetScaleBobTarget().localScale;
        _scaleBobBaseCaptured = true;
    }

    private void KillShuttleScaleTweens(bool resetToBase)
    {
        Transform t = GetScaleBobTarget();
        DOTween.Kill(t, false);
        if (resetToBase && _scaleBobBaseCaptured)
            t.localScale = _scaleBobBaseLocal;
    }

    private void PlayPreShuttleScaleBob(float timeBudget)
    {
        if (!_scaleBobBaseCaptured) return;

        Transform t = GetScaleBobTarget();
        DOTween.Kill(t, false);
        t.localScale = _scaleBobBaseLocal;

        float rise = preShuttleBobRiseDuration;
        float fall = preShuttleBobFallDuration;
        float total = rise + fall;
        if (timeBudget > 0f && total > 1e-4f && timeBudget < total)
        {
            float k = timeBudget / total;
            rise = Mathf.Max(0.02f, rise * k);
            fall = Mathf.Max(0.02f, fall * k);
        }

        Vector3 peak = _scaleBobBaseLocal * preShuttleBobPeakMultiplier;

        DOTween.Sequence()
            .SetTarget(t)
            .SetUpdate(true)
            .Append(t.DOScale(peak, rise).SetEase(preShuttleBobRiseEase))
            .Append(t.DOScale(_scaleBobBaseLocal, fall).SetEase(preShuttleBobFallEase));
    }

    /// <summary>
    /// Cross wins: cross stays on its spline; this shuttle is launched off the lane.
    /// Safe if both collision callbacks run — no-ops after first convert.
    /// </summary>
    public void ApplyCrossTrackRamFromCross(CrossTrackObstacle cross, Collision collision)
    {
        if (!enableCrossTrackRam || _convertedToPhysics || cross == null || !cross.IsOnScriptedPath)
            return;

        _pendingCollisionFrames = 0;
        _pendingCollider = null;

        Vector3 planar = Vector3.zero;
        if (collision != null && collision.relativeVelocity.sqrMagnitude > 1e-4f)
        {
            planar = collision.relativeVelocity;
            planar.y = 0f;
        }

        if (planar.sqrMagnitude < 1e-4f)
        {
            Vector3 cv = cross.GetWorldVelocity();
            cv.y = 0f;
            if (cv.sqrMagnitude > 1e-4f)
                planar = cv;
        }

        if (planar.sqrMagnitude < 1e-4f)
        {
            planar = transform.position - cross.transform.position;
            planar.y = 0f;
        }

        if (planar.sqrMagnitude < 1e-6f)
            planar = cross.transform.forward;
        planar.Normalize();

        float rel = crossRamMinEffectiveSpeed;
        if (collision != null && collision.relativeVelocity.sqrMagnitude > 1e-4f)
            rel = Mathf.Max(rel, collision.relativeVelocity.magnitude);
        rel = Mathf.Max(rel, cross.GetWorldVelocity().magnitude, _lastVelocity.magnitude);
        rel *= crossRamImpactSeverityScale;

        _impactDir = planar;
        _impactSpeed = rel;
        _hasImpactDir = true;

        ConvertToPhysicsOnHit();
    }

    public void ConvertToPhysicsOnHit()
    {
        if (_convertedToPhysics) return;
        _convertedToPhysics = true;

        Debug.Log($"[ShuttleTrackObstacle] Converting to physics on {gameObject.name}");

        enabled = false;
        _waiting = false;

        KillShuttleScaleTweens(true);

        // Kill all FX immediately
        _fxKilled = true;

        SetTravelFxEnabled(false, true);
        if (_preview != null) _preview.enabled = false;

        if (!_rb)
            _rb = GetComponent<Rigidbody>() ?? gameObject.AddComponent<Rigidbody>();

        _rb.isKinematic = false;
        _rb.useGravity = true;
        _rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        _rb.interpolation = RigidbodyInterpolation.Interpolate;
        _rb.constraints = RigidbodyConstraints.None; // Allow rotation

        Vector3 transferVel = _lastVelocity;
        if (transferVel.magnitude < minTransferVelocity)
            transferVel = transform.forward * Mathf.Max(minTransferVelocity, speed * 0.5f);

        // --- IMPACT FLING ADD-ON ---
        if (enableImpactFling)
        {
            Vector3 dir;
            if (_hasImpactDir && _impactDir.sqrMagnitude > 1e-6f)
            {
                dir = _impactDir.normalized;
            }
            else if (transferVel.sqrMagnitude > 1e-6f)
            {
                dir = transferVel.normalized;
            }
            else
            {
                dir = transform.forward;
            }

            float speedMag = transferVel.magnitude;

            float horizBoostMag = Mathf.Min(speedMag * impactHorizontalMultiplier, impactMaxExtraSpeed);
            float vertBoostMag = speedMag * impactUpwardMultiplier;

            Vector3 extra = dir * horizBoostMag + Vector3.up * vertBoostMag;
            transferVel += extra;

            _hasImpactDir = false;
        }

        _rb.velocity = transferVel;
        _rb.position += Vector3.up * 0.01f;

        // NEW: Add cartoony random rotation based on impact
        if (enableJumpRotation)
        {
            float impactSeverity = Mathf.Clamp01(_impactSpeed / 20f); // normalize impact speed to 0-1
            float angularMag = Mathf.Lerp(jumpAngularSpeedRange.x, jumpAngularSpeedRange.y,
                                          Mathf.Lerp(0.5f, 1f, impactSeverity * impactAngularScale));

            // Create random rotation axis (favor Y and roll for cartoony effect)
            Vector3 randomAxis = new Vector3(
                Random.Range(-0.5f, 0.5f),  // some pitch
                Random.Range(0.5f, 1f),     // always some yaw
                Random.Range(-1f, 1f)       // lots of roll
            ).normalized;

            // Apply angular velocity in degrees/sec (Unity converts internally)
            _rb.angularVelocity = randomAxis * (angularMag * Mathf.Deg2Rad);
        }

        _rb.WakeUp();
        Physics.SyncTransforms();
    }

    /// <summary>
    /// Matches <see cref="CrossTrackObstacle"/> player detection: scripted shuttle stays on path; car crash is handled by <see cref="CarController"/>.
    /// </summary>
    private static bool IsPlayerCarCollider(Collider other)
    {
        if (other == null) return false;
        if (other.GetComponentInParent<CarController>() != null)
            return true;
        var active = GameManager_Racing.Instance != null ? GameManager_Racing.Instance.ActiveCar : null;
        if (active == null) return false;
        Transform t = other.transform;
        return t == active.transform || t.IsChildOf(active.transform);
    }

    private bool _fxKilled = false;

    private void KillPathFxIfDynamic()
    {
        if (_fxKilled) return;

        // If anything made us dynamic, we are no longer "on path"
        if (_rb != null && !_rb.isKinematic)
        {
            _fxKilled = true;

            var preview = GetComponent<ObstaclePathPreview>();
            if (preview) preview.FadeOut(0f);
            SetTravelFxEnabled(false, true);
        }
    }

    private void SetTravelFxEnabled(bool enabledNow, bool instant = false)
    {
        if (_travelFxEnabled == enabledNow && !instant) return;
        _travelFxEnabled = enabledNow;

        if (_preview != null)
        {
            if (enabledNow)
            {
                _preview.enabled = true;
                _preview.FadeIn(instant ? 0f : 0.08f);
            }
            else
            {
                _preview.FadeOut(instant ? 0f : 0.08f);
            }
        }

        // Telegraph light: stay on whenever the shuttle is active; only path preview toggles with travel.
        if (!_waiting)
            ApplyShuttleLightOutsideWait(enabledNow);
    }

    /// <summary>Moving along lane, or idle dim when path preview is off but not in end-wait coroutine.</summary>
    private void ApplyShuttleLightOutsideWait(bool pathPreviewTraveling)
    {
        if (telegraphLight == null || !useTelegraphLight || !useTravelLightDuringMotion)
        {
            if (telegraphLight != null && (!useTelegraphLight || !useTravelLightDuringMotion))
                telegraphLight.enabled = false;
            return;
        }

        if (_fxKilled || _convertedToPhysics || !enabled)
        {
            telegraphLight.enabled = false;
            return;
        }

        telegraphLight.enabled = true;
        if (pathPreviewTraveling)
        {
            telegraphLight.color = travelLightColor;
            telegraphLight.intensity = _baseLightIntensity * Mathf.Max(0.01f, travelLightIntensityMultiplier);
        }
        else
        {
            telegraphLight.color = travelLightColor;
            telegraphLight.intensity = _baseLightIntensity * Mathf.Max(0.01f, idleLightIntensityMultiplier);
        }
    }

    private void UpdateShuttleLightDuringWait(float elapsed, float totalWait, float timeRemaining)
    {
        if (telegraphLight == null || !useTelegraphLight || !useTravelLightDuringMotion || _fxKilled || _convertedToPhysics)
            return;

        telegraphLight.enabled = true;

        float tNorm = elapsed / totalWait;
        float startT = Mathf.Clamp01(telegraphStartPercent);
        float idleI = _baseLightIntensity * Mathf.Max(0.01f, idleLightIntensityMultiplier);
        float moveI = _baseLightIntensity * Mathf.Max(0.01f, travelLightIntensityMultiplier);

        if (tNorm < startT)
        {
            telegraphLight.color = travelLightColor;
            telegraphLight.intensity = idleI;
            return;
        }

        float teleT = Mathf.InverseLerp(startT, 1f, tNorm);
        float intensity = Mathf.Lerp(idleI, moveI, teleT);
        telegraphLight.color = Color.Lerp(travelLightColor, telegraphEndColor, teleT);

        if (launchWarningLeadTime > 0f && timeRemaining <= launchWarningLeadTime)
        {
            float w = 1f - Mathf.Clamp01(timeRemaining / launchWarningLeadTime);
            float warnPeak = moveI * Mathf.Max(1f, launchWarningIntensityMultiplier);
            intensity = Mathf.Lerp(intensity, warnPeak, w);
            telegraphLight.color = Color.Lerp(telegraphLight.color, launchWarningColor, w);
        }

        telegraphLight.intensity = intensity;
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (_convertedToPhysics) return;
        if (Time.time < _spawnGraceUntil) return;
        if (collision == null || collision.collider == null) return;

        Debug.Log($"[ShuttleTrackObstacle] OnCollisionEnter with {collision.collider.name} (layer={collision.collider.gameObject.layer})");

        if (ignoreRoadAndTerrain)
        {
            int road = LayerMask.NameToLayer("RoadSurface");
            int terrain = LayerMask.NameToLayer("Terrain");
            int l = collision.collider.gameObject.layer;
            if (l == road || l == terrain) return;
        }

        if (IsPlayerCarCollider(collision.collider))
        {
            _pendingCollisionFrames = 0;
            _pendingCollider = null;
            return;
        }

        var npc = collision.collider.GetComponentInParent<NPCTrafficCar>();
        if (npc != null)
        {
            float rel = collision.relativeVelocity.sqrMagnitude > 1e-6f
                ? collision.relativeVelocity.magnitude
                : _lastVelocity.magnitude;
            npc.ApplyScriptedShuttleTrackOverlapHit(this, collision.collider, rel);
            return;
        }

        var cross = collision.collider.GetComponentInParent<CrossTrackObstacle>();
        if (cross != null && cross.IsOnScriptedPath)
        {
            ApplyCrossTrackRamFromCross(cross, collision);
            return;
        }

        if (!TrackMoverPathLossSources.IsInstigator(collision.collider))
        {
            if (enableNonPathLossObstacleBump)
            {
                float rel = collision.relativeVelocity.sqrMagnitude > 1e-6f
                    ? collision.relativeVelocity.magnitude
                    : 0f;
                TrackMoverNonPathBump.TryApplyUpLaunch(
                    collision.collider,
                    transform,
                    _lastVelocity,
                    rel,
                    nonPathLossUpVelocityChange,
                    nonPathLossBumpSpeedScaleRange.x,
                    nonPathLossBumpSpeedScaleRange.y,
                    nonPathLossBumpSpeedRef,
                    nonPathLossWakeKinematicObstacles);
            }

            return;
        }

        // NEW: Store impact data
        Vector3 dir = Vector3.zero;
        if (_lastVelocity.sqrMagnitude > 1e-6f)
        {
            dir = _lastVelocity.normalized;
        }
        else if (collision.contactCount > 0)
        {
            dir = -collision.GetContact(0).normal;
        }
        else
        {
            dir = transform.forward;
        }

        _impactDir = dir;
        _impactSpeed = collision.relativeVelocity.magnitude;
        _hasImpactDir = true;

        if (RacingObstacleCollisionPopups.IsObstacleBuddy(collision.collider))
        {
            RacingObstacleCollisionPopups.TrySpawnObstacleClash(
                transform.root,
                collision.collider.transform.root,
                collision,
                collision.collider,
                collision.relativeVelocity.magnitude,
                shuttleClashMinRelativeSpeed,
                shuttleClashPopupHeight,
                shuttleClashPairCooldown,
                enableShuttleClashCrashPopup);
        }

        // NEW: Start delayed conversion to give car crash logic time to fire
        _pendingCollider = collision.collider;
        _pendingCollisionFrames = collisionDelayFrames;

        // If delay is 0, convert immediately
        if (collisionDelayFrames <= 0)
        {
            ConvertToPhysicsOnHit();
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        if (_convertedToPhysics) return;
        if (Time.time < _spawnGraceUntil) return;
        if (other == null) return;

        Debug.Log($"[ShuttleTrackObstacle] OnTriggerEnter with {other.name} (layer={other.gameObject.layer})");

        if (ignoreRoadAndTerrain)
        {
            int road = LayerMask.NameToLayer("RoadSurface");
            int terrain = LayerMask.NameToLayer("Terrain");
            int l = other.gameObject.layer;
            if (l == road || l == terrain) return;
        }

        if (IsPlayerCarCollider(other))
        {
            _pendingCollisionFrames = 0;
            _pendingCollider = null;
            return;
        }

        var npc = other.GetComponentInParent<NPCTrafficCar>();
        if (npc != null)
        {
            npc.ApplyScriptedShuttleTrackOverlapHit(this, other, _lastVelocity.magnitude);
            return;
        }

        var crossT = other.GetComponentInParent<CrossTrackObstacle>();
        if (crossT != null && crossT.IsOnScriptedPath)
        {
            ApplyCrossTrackRamFromCross(crossT, null);
            return;
        }

        if (!TrackMoverPathLossSources.IsInstigator(other))
        {
            if (enableNonPathLossObstacleBump)
            {
                TrackMoverNonPathBump.TryApplyUpLaunch(
                    other,
                    transform,
                    _lastVelocity,
                    _lastVelocity.magnitude,
                    nonPathLossUpVelocityChange,
                    nonPathLossBumpSpeedScaleRange.x,
                    nonPathLossBumpSpeedScaleRange.y,
                    nonPathLossBumpSpeedRef,
                    nonPathLossWakeKinematicObstacles);
            }

            return;
        }

        Vector3 dir;
        if (_lastVelocity.sqrMagnitude > 1e-6f)
            dir = _lastVelocity.normalized;
        else
            dir = transform.forward;

        _impactDir = dir;
        _impactSpeed = _lastVelocity.magnitude;
        _hasImpactDir = true;

        if (RacingObstacleCollisionPopups.IsObstacleBuddy(other))
        {
            Vector3 approx = other.ClosestPoint(transform.position);
            RacingObstacleCollisionPopups.TrySpawnObstacleClashApprox(
                transform.root,
                other.transform.root,
                other,
                approx,
                _lastVelocity.magnitude,
                shuttleClashMinRelativeSpeed,
                shuttleClashPopupHeight,
                shuttleClashPairCooldown,
                enableShuttleClashCrashPopup);
        }

        // NEW: Start delayed conversion
        _pendingCollider = other;
        _pendingCollisionFrames = collisionDelayFrames;

        if (collisionDelayFrames <= 0)
        {
            ConvertToPhysicsOnHit();
        }
    }

    private void TryMakeOtherDynamic(Collider other)
    {
        if (other == null) return;
        if (other.isTrigger) return;

        Transform root = other.attachedRigidbody ? other.attachedRigidbody.transform : other.transform.root;
        if (!root) root = other.transform;
        if (root == transform) return;

        int projectileLayer = LayerMask.NameToLayer("Projectile");
        if (projectileLayer == -1) return;
        bool otherIsProjectile = root.gameObject.layer == projectileLayer || other.gameObject.layer == projectileLayer;
        if (!otherIsProjectile) return;

        Rigidbody rb = root.GetComponent<Rigidbody>() ?? root.GetComponentInChildren<Rigidbody>();
        if (rb == null)
        {
            rb = root.gameObject.AddComponent<Rigidbody>();
            rb.mass = Mathf.Max(0.1f, 10f);
        }

        if (rb.isKinematic)
            rb.isKinematic = false;

        rb.useGravity = true;
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        rb.WakeUp();
        Physics.SyncTransforms();
    }

    private void CheckForOverlapAndConvert()
    {
        if (_convertedToPhysics) return;
        if (Time.time < _spawnGraceUntil) return;
        if (_childColliders == null || _childColliders.Length == 0) return;

        Bounds combined = new Bounds(_childColliders[0].bounds.center, _childColliders[0].bounds.size);
        for (int i = 1; i < _childColliders.Length; i++)
        {
            if (_childColliders[i] == null) continue;
            combined.Encapsulate(_childColliders[i].bounds);
        }

        combined.Expand(0.01f);

        Collider[] hits = Physics.OverlapBox(
            combined.center,
            combined.extents,
            transform.rotation,
            ~0,
            QueryTriggerInteraction.Ignore);
        if (hits == null || hits.Length == 0) return;

        foreach (var hit in hits)
        {
            if (hit == null) continue;
            if (System.Array.IndexOf(_childColliders, hit) >= 0) continue;
            if (hit.isTrigger) continue;

            if (IsPlayerCarCollider(hit))
                continue;

            var npc = hit.GetComponentInParent<NPCTrafficCar>();
            if (npc != null)
            {
                npc.ApplyScriptedShuttleTrackOverlapHit(this, hit, _lastVelocity.magnitude);
                continue;
            }

            var crossOv = hit.GetComponentInParent<CrossTrackObstacle>();
            if (crossOv != null && crossOv.IsOnScriptedPath)
            {
                ApplyCrossTrackRamFromCross(crossOv, null);
                return;
            }

            if (!TrackMoverPathLossSources.IsInstigator(hit))
                continue;

            Debug.Log($"[ShuttleTrackObstacle] Overlap hit {hit.name} (layer={hit.gameObject.layer}) – converting.");

            // NEW: Set impact data for overlap case
            _impactDir = _lastVelocity.sqrMagnitude > 1e-6f ? _lastVelocity.normalized : transform.forward;
            _impactSpeed = _lastVelocity.magnitude;
            _hasImpactDir = true;

            if (RacingObstacleCollisionPopups.IsObstacleBuddy(hit))
            {
                Vector3 approx = hit.ClosestPoint(combined.center);
                RacingObstacleCollisionPopups.TrySpawnObstacleClashApprox(
                    transform.root,
                    hit.transform.root,
                    hit,
                    approx,
                    _lastVelocity.magnitude,
                    shuttleClashMinRelativeSpeed,
                    shuttleClashPopupHeight,
                    shuttleClashPairCooldown,
                    enableShuttleClashCrashPopup);
            }

            ConvertToPhysicsOnHit();
            return;
        }
    }

    /// <summary>
    /// Resolves the track tangent (forward) and lateral (cross-track) directions at the shuttle's origin.
    /// Falls back to the transform's own axes when no track generator/path is available.
    /// Both the travel-line endpoints and the body rotation use this so they always agree.
    /// </summary>
    private bool ResolveTrackFrame(out Vector3 forward, out Vector3 lateral)
    {
        forward = transform.forward; forward.y = 0f;
        lateral = transform.right;

        if (trackGenerator != null && trackGenerator.PathPoints != null && trackGenerator.PathPoints.Count >= 2)
        {
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
            Vector3 f = bb - aa; f.y = 0f;
            if (f.sqrMagnitude > 1e-6f)
            {
                forward = f.normalized;
                lateral = Vector3.Cross(Vector3.up, forward).normalized;
                return true;
            }
        }

        if (forward.sqrMagnitude < 1e-6f) forward = transform.forward;
        if (lateral.sqrMagnitude < 1e-6f) lateral = transform.right;
        return false;
    }

    /// <summary>
    /// Snaps the shuttle's orientation to the track frame so its body lines up with the drawn travel line.
    /// Preserves which way it was facing (down-track vs up-track) and forces it upright.
    /// </summary>
    private void AlignRotationToTrack()
    {
        if (!ResolveTrackFrame(out Vector3 trackForward, out _))
            return;

        trackForward.y = 0f;
        if (trackForward.sqrMagnitude < 1e-6f)
            return;
        trackForward.Normalize();

        // Keep the spawned facing hemisphere so we only correct the slight off-angle (never flip 180°).
        Vector3 currentForward = transform.forward; currentForward.y = 0f;
        if (currentForward.sqrMagnitude > 1e-6f && Vector3.Dot(trackForward, currentForward) < 0f)
            trackForward = -trackForward;

        transform.rotation = Quaternion.LookRotation(trackForward, Vector3.up);
    }

    private void ComputeEdgeWorldPositions(out Vector3 leftWS, out Vector3 rightWS)
    {
        ResolveTrackFrame(out _, out Vector3 lateral);

        if (lateral.sqrMagnitude < 1e-6f)
            lateral = transform.right;

        float roadInnerHalf = Mathf.Max(0.1f, _halfRoad - edgeMargin);
        float obstacleHalf = Mathf.Max(0f, _selfHalf);
        float usableOffset = Mathf.Max(0.1f, roadInnerHalf - obstacleHalf);

        leftWS = _originWS + lateral * -usableOffset;
        rightWS = _originWS + lateral * usableOffset;
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

    public Vector3 GetWorldVelocity()
    {
        // While still on scripted motion, use the transform-derived velocity.
        if (!_convertedToPhysics) return _lastVelocity;

        // After conversion, use the real rigidbody velocity.
        return _rb != null ? _rb.velocity : Vector3.zero;
    }

    private float SampleTerrainHeightUnderXZ(Vector2 xz, float upOffset = 2f)
    {
        Vector3 probe = new Vector3(xz.x, transform.position.y + upOffset, xz.y);
        Vector3 normal;
        Vector3 projected = SpawnUtils.ProjectOntoSurface(probe, out normal, upOffset, 50f, LayerMask.GetMask("RoadSurface"));
        if (projected == probe)
        {
            projected = SpawnUtils.ProjectOntoSurface(probe, out normal, upOffset, 50f, null);
        }
        return projected.y;
    }

    private void TrimPathEndpointsForFlatTravel(Vector3 originWS)
    {
        float refSurfaceY = SampleTerrainHeightUnderXZ(new Vector2(originWS.x, originWS.z), Mathf.Abs(_bottomOffset) + 1f);
        _leftWS = TrimEndpointTowardOrigin(_leftWS, originWS, refSurfaceY);
        _rightWS = TrimEndpointTowardOrigin(_rightWS, originWS, refSurfaceY);
    }

    private Vector3 TrimEndpointTowardOrigin(Vector3 endpoint, Vector3 origin, float refSurfaceY)
    {
        Vector3 candidate = endpoint;
        float candidateY = SampleTerrainHeightUnderXZ(new Vector2(candidate.x, candidate.z), Mathf.Abs(_bottomOffset) + 1f);
        if (Mathf.Abs(candidateY - refSurfaceY) <= maxAllowedPathElevationDelta)
            return candidate;

        // Binary trim from endpoint toward origin until terrain height is close to origin band.
        Vector3 lo = origin;
        Vector3 hi = endpoint;
        for (int i = 0; i < endpointTrimIterations; i++)
        {
            Vector3 mid = Vector3.Lerp(lo, hi, 0.5f);
            float midY = SampleTerrainHeightUnderXZ(new Vector2(mid.x, mid.z), Mathf.Abs(_bottomOffset) + 1f);
            bool valid = Mathf.Abs(midY - refSurfaceY) <= maxAllowedPathElevationDelta;
            if (valid) lo = mid; else hi = mid;
        }
        return lo;
    }

    [SerializeField]
    [Tooltip("Renderers/Colliders with this tag are ignored when computing bottom offset (e.g., FX, lights, etc.).")]
    private string bottomOffsetIgnoreTag = "FXIgnoreBottom";

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
                if (!string.IsNullOrEmpty(bottomOffsetIgnoreTag) && r.CompareTag(bottomOffsetIgnoreTag))
                    continue;

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
                if (!string.IsNullOrEmpty(bottomOffsetIgnoreTag) && c.CompareTag(bottomOffsetIgnoreTag))
                    continue;

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