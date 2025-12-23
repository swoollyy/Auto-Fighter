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

    [Header("Path Length Randomization")]
    [Tooltip("If true, each shuttle picks a random fraction of the full lane width for its travel path on spawn.")]
    [SerializeField] private bool randomizePathLength = true;

    [Tooltip("Minimum fraction of the full usable lane width used for this shuttle's travel path.")]
    [SerializeField, Range(0.1f, 1f)] private float minPathFraction = 0.4f;

    [Tooltip("Maximum fraction of the full usable lane width used for this shuttle's travel path.")]
    [SerializeField, Range(0.1f, 1f)] private float maxPathFraction = 1f;

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

    private Rigidbody _rb;
    private float _bottomOffset = 0f;
    private float _safeMargin = 0.02f;

    [Header("Collision → Convert To Physics")]
    [Tooltip("When colliding with any collider on these layers the shuttle will drop its scripted path and convert to physics.")]
    [SerializeField] private LayerMask convertOnCollisionLayers = ~0;
    [Tooltip("When enabled, ignore RoadSurface/Terrain layer collisions to avoid accidental conversions.")]
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

    [Header("Travel Light State")]
    [Tooltip("If true, the light is on and yellow while the shuttle is moving.")]
    [SerializeField] private bool useTravelLightDuringMotion = true;
    [Tooltip("Color of the light while the shuttle is moving.")]
    [SerializeField] private Color travelLightColor = Color.yellow;
    [Tooltip("Multiplier applied to the original light intensity while moving. 1 = same, 2 = double, etc.")]
    [SerializeField] private float travelLightIntensityMultiplier = 1.5f;

    private Vector3 _prevPosition;
    private Vector3 _lastVelocity;

    private Collider[] _childColliders;
    private float _overlapTimer;
    private float _baseLightIntensity = 3.5f;

    // NEW: collision tracking for delayed conversion
    private int _pendingCollisionFrames = 0;
    private Collider _pendingCollider;

    public void SetGenerator(ProceduralTrackGenerator gen) => trackGenerator = gen;

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

        _prevPosition = transform.position;
        _lastVelocity = Vector3.zero;
        _overlapTimer = 0f;

        if (telegraphLight)
        {
            _baseLightIntensity = telegraphLight.intensity;
            telegraphLight.enabled = false;
        }
    }

    private void Start()
    {
        _originWS = transform.position;

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

        var preview = GetComponent<ObstaclePathPreview>();
        if (preview) { preview.SetEndpoints(_leftWS, _rightWS); preview.FadeIn(0.2f); }

        if (randomizePathLength)
        {
            float minF = Mathf.Clamp01(minPathFraction);
            float maxF = Mathf.Clamp01(maxPathFraction);
            if (maxF < minF)
            {
                float tmp = minF;
                minF = maxF;
                maxF = tmp;
            }

            float f = Random.Range(minF, maxF);
            Vector3 mid = (_leftWS + _rightWS) * 0.5f;
            _leftWS = Vector3.Lerp(mid, _leftWS, f);
            _rightWS = Vector3.Lerp(mid, _rightWS, f);
        }

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
    }

    private void Update()
    {
        if (_convertedToPhysics)
            return;

        KillPathFxIfDynamic();

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

        // >>> TRAVEL LIGHT STATE WHILE MOVING <<<
        if (useTelegraphLight && useTravelLightDuringMotion && telegraphLight)
        {
            telegraphLight.enabled = true;
            telegraphLight.color = travelLightColor;
            telegraphLight.intensity = _baseLightIntensity * Mathf.Max(0f, travelLightIntensityMultiplier);
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

        float sampleY = SampleTerrainHeightUnderXZ(nextXZ, Mathf.Abs(_bottomOffset) + 1f);
        float newY = sampleY - _bottomOffset + _safeMargin;

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
                // Time's up - actually convert to physics now
                ConvertToPhysicsOnHit();
            }
        }
    }

    private IEnumerator WaitThenResume(float seconds)
    {
        _waiting = true;

        bool telegraphOn = false;
        bool launchWarningFired = false;

        // NEW: Turn off light when waiting starts
        if (telegraphLight)
            telegraphLight.enabled = false;

        float totalWait = Mathf.Max(0.0001f, seconds);
        float telegraphStartTime = totalWait * Mathf.Clamp01(telegraphStartPercent);
        float telegraphDuration = Mathf.Max(0.0001f, totalWait - telegraphStartTime);

        float elapsed = 0f;

        while (elapsed < totalWait)
        {
            elapsed += Time.deltaTime;
            float clampedElapsed = Mathf.Min(elapsed, totalWait);
            float timeRemaining = totalWait - clampedElapsed;

            if (useTelegraphLight && telegraphLight)
            {
                if (!telegraphOn && clampedElapsed >= telegraphStartTime)
                {
                    telegraphOn = true;
                    telegraphLight.enabled = true;
                    telegraphLight.color = telegraphStartColor;
                    telegraphLight.intensity = _baseLightIntensity;
                }

                if (telegraphOn && !launchWarningFired)
                {
                    float tNorm = Mathf.Clamp01((clampedElapsed - telegraphStartTime) / telegraphDuration);
                    telegraphLight.color = Color.Lerp(telegraphStartColor, telegraphEndColor, tNorm);
                    telegraphLight.intensity = _baseLightIntensity;
                }

                if (!launchWarningFired &&
                    launchWarningLeadTime > 0f &&
                    timeRemaining <= launchWarningLeadTime)
                {
                    launchWarningFired = true;

                    telegraphLight.enabled = true;
                    telegraphLight.color = launchWarningColor;
                    telegraphLight.intensity =
                        _baseLightIntensity * Mathf.Max(1f, launchWarningIntensityMultiplier);

                    if (launchParticles)
                        launchParticles.Play();

                    if (launchAudio)
                        launchAudio.Play();
                }
            }

            yield return null;
        }

        // NEW: Turn off light when movement starts
        if (telegraphLight)
            telegraphLight.enabled = false;

        _waiting = false;
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

    public void ConvertToPhysicsOnHit()
    {
        if (_convertedToPhysics) return;
        _convertedToPhysics = true;

        Debug.Log($"[ShuttleTrackObstacle] Converting to physics on {gameObject.name}");

        enabled = false;
        _waiting = false;


        var preview = GetComponent<ObstaclePathPreview>();
        if (preview) preview.FadeOut(0.2f);

        // NEW: ENSURE LIGHT IS OFF when converting to physics
        if (telegraphLight)
            telegraphLight.enabled = false;

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

    private bool ShouldConvertForCollider(Collider other)
    {
        if (other == null) return false;

        if (ignoreRoadAndTerrain)
        {
            int road = LayerMask.NameToLayer("RoadSurface");
            int terrain = LayerMask.NameToLayer("Terrain");
            if (road >= 0 && other.gameObject.layer == road) return false;
            if (terrain >= 0 && other.gameObject.layer == terrain) return false;
            if (road >= 0 && other.transform.root != null && other.transform.root.gameObject.layer == road) return false;
            if (terrain >= 0 && other.transform.root != null && other.transform.root.gameObject.layer == terrain) return false;
        }

        if (((convertOnCollisionLayers.value) & (1 << other.gameObject.layer)) != 0) return true;

        if (other.transform.root != null)
        {
            if (((convertOnCollisionLayers.value) & (1 << other.transform.root.gameObject.layer)) != 0) return true;
        }

        return false;
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
            if (preview) preview.FadeOut(0.2f);

            if (telegraphLight) telegraphLight.enabled = false;
        }
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (_convertedToPhysics) return;
        if (collision == null || collision.collider == null) return;

        Debug.Log($"[ShuttleTrackObstacle] OnCollisionEnter with {collision.collider.name} (layer={collision.collider.gameObject.layer})");

        if (!IsInConvertLayers(collision.collider.gameObject))
            return;

        if (ignoreRoadAndTerrain)
        {
            int road = LayerMask.NameToLayer("RoadSurface");
            int terrain = LayerMask.NameToLayer("Terrain");
            int l = collision.collider.gameObject.layer;
            if (l == road || l == terrain) return;
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
        if (other == null) return;

        Debug.Log($"[ShuttleTrackObstacle] OnTriggerEnter with {other.name} (layer={other.gameObject.layer})");

        if (!IsInConvertLayers(other.gameObject))
            return;

        if (ignoreRoadAndTerrain)
        {
            int road = LayerMask.NameToLayer("RoadSurface");
            int terrain = LayerMask.NameToLayer("Terrain");
            int l = other.gameObject.layer;
            if (l == road || l == terrain) return;
        }

        Vector3 dir;
        if (_lastVelocity.sqrMagnitude > 1e-6f)
            dir = _lastVelocity.normalized;
        else
            dir = transform.forward;

        _impactDir = dir;
        _impactSpeed = _lastVelocity.magnitude;
        _hasImpactDir = true;

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
        if (_childColliders == null || _childColliders.Length == 0) return;

        Bounds combined = new Bounds(_childColliders[0].bounds.center, _childColliders[0].bounds.size);
        for (int i = 1; i < _childColliders.Length; i++)
        {
            if (_childColliders[i] == null) continue;
            combined.Encapsulate(_childColliders[i].bounds);
        }

        combined.Expand(0.01f);

        int mask = convertOnCollisionLayers.value;
        Collider[] hits = Physics.OverlapBox(combined.center, combined.extents, transform.rotation, mask);
        if (hits == null || hits.Length == 0) return;

        foreach (var hit in hits)
        {
            if (hit == null) continue;
            if (System.Array.IndexOf(_childColliders, hit) >= 0) continue;
            if (hit.isTrigger) continue;

            Debug.Log($"[ShuttleTrackObstacle] Overlap hit {hit.name} (layer={hit.gameObject.layer}) – converting.");

            // NEW: Set impact data for overlap case
            _impactDir = _lastVelocity.sqrMagnitude > 1e-6f ? _lastVelocity.normalized : transform.forward;
            _impactSpeed = _lastVelocity.magnitude;
            _hasImpactDir = true;

            ConvertToPhysicsOnHit();
            return;
        }
    }

    private void ComputeEdgeWorldPositions(out Vector3 leftWS, out Vector3 rightWS)
    {
        Vector3 lateral = transform.right;

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
            Vector3 forward = (bb - aa).normalized;
            if (forward.sqrMagnitude > 1e-6f)
                lateral = Vector3.Cross(Vector3.up, forward).normalized;
        }

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

    private bool IsInConvertLayers(GameObject go)
    {
        int layer = go.layer;
        return (convertOnCollisionLayers.value & (1 << layer)) != 0;
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