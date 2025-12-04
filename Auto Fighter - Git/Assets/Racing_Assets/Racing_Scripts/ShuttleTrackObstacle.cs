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

    // runtime
    private Vector3 _originWS;
    private Vector3 _leftWS, _rightWS;
    private Vector3 _targetWS;
    private bool _waiting;
    private float _halfRoad, _selfHalf;
    private Vector3 _impactDir;
    private bool _hasImpactDir;
    private bool _convertedToPhysics;

    // NEW: cached Rigidbody reference so we can default to kinematic until conversion
    private Rigidbody _rb;

    // NEW: bottom offset (world-min relative to transform.position.y)
    private float _bottomOffset = 0f;
    private float _safeMargin = 0.02f;

    // NEW: configurable layers that cause conversion to dynamic physics (set in inspector)
    [Header("Collision → Convert To Physics")]
    [Tooltip("When colliding with any collider on these layers the shuttle will drop its scripted path and convert to physics.")]
    [SerializeField] private LayerMask convertOnCollisionLayers = ~0;
    [Tooltip("When enabled, ignore RoadSurface/Terrain layer collisions to avoid accidental conversions.")]
    [SerializeField] private bool ignoreRoadAndTerrain = true;
    [Tooltip("Minimum movement speed (m/s) used when transferring scripted motion into Rigidbody velocity on conversion.")]
    [SerializeField] private float minTransferVelocity = 0.25f;

    // NEW: overlap-based detection fallback (helps detect collisions while kinematic/moved-by-transform)
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

    // NEW: track previous position / last velocity to produce a physical velocity on conversion
    private Vector3 _prevPosition;
    private Vector3 _lastVelocity;

    // cached child colliders used for overlap sampling
    private Collider[] _childColliders;
    private float _overlapTimer;
    private float _baseLightIntensity = 3.5f;

    public void SetGenerator(ProceduralTrackGenerator gen) => trackGenerator = gen;

    private void Awake()
    {
        if (!trackGenerator)
            trackGenerator = FindObjectOfType<ProceduralTrackGenerator>();

        // Ensure a Rigidbody exists ...
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

        // capture child colliders for overlap checks
        _childColliders = GetComponentsInChildren<Collider>();

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

        // Randomize path length (shrink from both sides toward midpoint)
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

        // Decide initial start/target directly from the (possibly randomized) edges
        Vector3 startWS = startOnLeft ? _leftWS : _rightWS;
        Vector3 targetWS = startOnLeft ? _rightWS : _leftWS;

        // Re-project these endpoints to keep them cleanly on the road surface
        startWS = SpawnUtils.ProjectOntoSurface(startWS, out Vector3 startNormal, upOffsetForCast, 25f, roadMask);
        _targetWS = SpawnUtils.ProjectOntoSurface(targetWS, out Vector3 targetNormal, upOffsetForCast, 25f, roadMask);

        // Choose a Y that keeps the object snug on the terrain at the start point.
        float startDesiredY = startWS.y - _bottomOffset + safeMargin;

        // Set start position and target XZ; Y will be set to same startDesiredY (we move only in XZ)
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

        if (enableOverlapDetection)
        {
            _overlapTimer -= Time.deltaTime;
            if (_overlapTimer <= 0f)
            {
                _overlapTimer = Mathf.Max(0.01f, overlapCheckInterval);
                CheckForOverlapAndConvert();
            }
        }

        if (_waiting) return;

        // >>> TRAVEL LIGHT STATE WHILE MOVING <<<
        if (useTelegraphLight && useTravelLightDuringMotion && telegraphLight)
        {
            telegraphLight.enabled = true;
            telegraphLight.color = travelLightColor;
            telegraphLight.intensity = _baseLightIntensity * Mathf.Max(0f, travelLightIntensityMultiplier);
        }

        float step = Mathf.Max(0.01f, speed) * Time.deltaTime;

        // move XZ only
        Vector2 curXZ = new Vector2(transform.position.x, transform.position.z);
        Vector2 targetXZ = new Vector2(_targetWS.x, _targetWS.z);
        Vector2 nextXZ = Vector2.MoveTowards(curXZ, targetXZ, step);

        float sampleY = SampleTerrainHeightUnderXZ(nextXZ, Mathf.Abs(_bottomOffset) + 1f);
        float newY = sampleY - _bottomOffset + _safeMargin;

        Vector3 newPos = new Vector3(nextXZ.x, newY, nextXZ.y);

        _lastVelocity = (newPos - _prevPosition) / Mathf.Max(Time.deltaTime, 1e-6f);
        _prevPosition = newPos;

        transform.position = newPos;

        if ((new Vector2(transform.position.x, transform.position.z) - targetXZ).sqrMagnitude <= 0.0001f)
        {
            // Flip the shuttle target
            _targetWS = (_targetWS == _leftWS) ? _rightWS : _leftWS;

            // Randomize speed for this leg, if enabled
            if (useRandomSpeed)
            {
                float sMin = Mathf.Min(speedRange.x, speedRange.y);
                float sMax = Mathf.Max(speedRange.x, speedRange.y);
                speed = Random.Range(sMin, sMax);
            }

            // Determine wait duration for this endpoint
            float pauseTime = waitAtEndSeconds;

            if (useRandomWaitAtEnd)
            {
                float wMin = Mathf.Min(waitAtEndRange.x, waitAtEndRange.y);
                float wMax = Mathf.Max(waitAtEndRange.x, waitAtEndRange.y);
                pauseTime = Random.Range(wMin, wMax);
            }

            if (pauseTime > 0f)
                StartCoroutine(WaitThenResume(pauseTime));
        }
    }

    private IEnumerator WaitThenResume(float seconds)
    {
        _waiting = true;

        bool telegraphOn = false;
        bool launchWarningFired = false;

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

            // Handle telegraph light behavior
            if (useTelegraphLight && telegraphLight)
            {
                // Turn on telegraph light at the configured start fraction
                if (!telegraphOn && clampedElapsed >= telegraphStartTime)
                {
                    telegraphOn = true;
                    telegraphLight.enabled = true;
                    telegraphLight.color = telegraphStartColor;
                    telegraphLight.intensity = _baseLightIntensity;
                }

                // Normal lerp phase (only if we haven't hit the final warning yet)
                if (telegraphOn && !launchWarningFired)
                {
                    float tNorm = Mathf.Clamp01((clampedElapsed - telegraphStartTime) / telegraphDuration);
                    telegraphLight.color = Color.Lerp(telegraphStartColor, telegraphEndColor, tNorm);
                    telegraphLight.intensity = _baseLightIntensity;
                }

                // FINAL WARNING WINDOW: fire right before launch
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

        // Right when movement actually starts, turn off the telegraph light.
        if (telegraphLight)
            telegraphLight.enabled = false;

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
            Bounds wb = default;
            bool hasBounds = false;

            for (int i = 0; i < rends.Length; i++)
            {
                var rend = rends[i];
                if (!rend) continue;

                // Ignore FX / telegraph meshes
                if (!string.IsNullOrEmpty(bottomOffsetIgnoreTag) &&
                    rend.CompareTag(bottomOffsetIgnoreTag))
                    continue;

                if (!hasBounds)
                {
                    wb = rend.bounds;
                    hasBounds = true;
                }
                else
                {
                    wb.Encapsulate(rend.bounds);
                }
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

        // Stop scripted movement
        enabled = false;
        _waiting = false;

        if (telegraphLight)
            telegraphLight.enabled = false;

        if (!_rb)
            _rb = GetComponent<Rigidbody>() ?? gameObject.AddComponent<Rigidbody>();

        _rb.isKinematic = false;
        _rb.useGravity = true;
        _rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        _rb.interpolation = RigidbodyInterpolation.Interpolate;
        _rb.constraints = RigidbodyConstraints.None;

        // Base transfer velocity from shuttle motion
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
        // --- END IMPACT FLING ---

        _rb.velocity = transferVel;
        _rb.position += Vector3.up * 0.01f;
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
        _hasImpactDir = true;

        ConvertToPhysicsOnHit();
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
        _hasImpactDir = true;

        ConvertToPhysicsOnHit();
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
            ConvertToPhysicsOnHit();
            return;
        }
    }

    private void ComputeEdgeWorldPositions(out Vector3 leftWS, out Vector3 rightWS)
    {
        Vector3 lateral = transform.right; // fallback

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
