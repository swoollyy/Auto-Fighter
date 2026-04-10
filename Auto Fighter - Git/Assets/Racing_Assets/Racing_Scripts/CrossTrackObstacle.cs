using System;
using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public class CrossTrackObstacle : MonoBehaviour
{
    [Header("Motion")]
    [SerializeField] private float speed = 6f;
    [Tooltip("Destroy this GameObject after it crosses. If false, just disable this script.")]
    [SerializeField] private bool destroyOnExit = true;

    [Header("Flat Path Constraint")]
    [Tooltip("If enabled, cross-track travel stays flat and target path is trimmed before elevation changes exceed tolerance.")]
    [SerializeField] private bool keepPathFlatAndTrimOnElevation = false;
    [SerializeField, Min(0f)] private float maxAllowedPathElevationDelta = 0.15f;
    [SerializeField, Range(1, 24)] private int endpointTrimIterations = 10;

    [Header("Visual tilt (optional)")]
    [Tooltip("Child transform that tilts with the road. The rigidbody on this object only does position + yaw. If empty, tries a child named Visual, TiltRoot, or Model; otherwise tilts this transform (legacy).")]
    [SerializeField] private Transform tiltVisualRoot;

    [Header("Surface / hills")]
    [Tooltip("How fast parent yaw blends toward travel heading. When a tilt visual is set, only the parent yaws; the child handles slope tilt.")]
    [SerializeField, Min(0f)] private float surfaceAlignRotationSpeed = 16f;
    [Tooltip("How fast the tilt visual blends toward sampled ground normal (non-flat paths only).")]
    [SerializeField, Min(0f)] private float surfaceNormalSmoothSpeed = 14f;
    [Tooltip("Raycast tuning for ground samples.")]
    [SerializeField, Min(1f)] private float surfaceProbeUpOffset = 48f;
    [SerializeField, Min(10f)] private float surfaceMaxDownDist = 200f;
    [Tooltip("Extra meters above detected ground (center Y = ground + half-height + this). Small float so it never clips terrain.")]
    [SerializeField, Min(0f)] private float clearanceAboveGround = 0.08f;
    [Tooltip("Raycasts for path motion and line preview only hit these layers (road/grass). Leave at Nothing to auto-fill RoadSurface+Grass+Road. Obstacles/props are excluded so the path cannot “bridge” hilltops or ride on props.")]
    [SerializeField] private LayerMask surfaceGroundLayers;

    [Header("Path preview line")]
    [SerializeField, Range(4, 64)] private int previewPathSegments = 36;
    [Tooltip("Rebuild draped polyline every frame while moving (accurate on hills).")]
    [SerializeField] private bool previewUpdateEveryFrame = true;

    [Header("Debug")]
    [SerializeField] private bool drawPathGizmos = true;
    [SerializeField] private bool debugMassComparison = false;

    [Header("Screen Shake")]
    [SerializeField] private bool enableScreenShake = true;
    [SerializeField] private float shakeIntensity = 0.18f;
    [SerializeField] private float shakeFrequency = 22f;
    [SerializeField] private float shakeMaxDistance = 35f;
    [SerializeField] private float shakeFullIntensityDistance = 6f;

    // Runtime path
    private Vector3 _startWS;
    private Vector3 _targetWS;
    private bool _active;
    private bool _initialized;
    private float _initialDelay;
    private float _spawnedAt;

    private Vector3 _prevPosition;
    private Vector3 _lastVelocity;

    [SerializeField, Tooltip("Layers this cross will react to. Colliders on other layers will be ignored (e.g. Terrain).")]
    private LayerMask reactLayers = ~0;

    [Header("Obstacle clash popup (Crash style)")]
    [SerializeField] private bool enableCrossClashCrashPopup = true;
    [SerializeField, Min(0f)] private float crossClashPopupHeight = 1f;
    [SerializeField, Min(0f)] private float crossClashMinRelativeSpeed = 2f;
    [SerializeField, Min(0f)] private float crossClashPairCooldown = 0.2f;

    // Cached Rigidbody
    private Rigidbody _rb;

    // Flag to prevent multiple conversions
    private bool _convertedToPhysics;
    private bool _travelFxEnabled;
    private ObstaclePathPreview _preview;
    private float _pathHeightOffset;
    private Vector3[] _previewScratch;
    /// <summary>When true, path line/lights stay off while still on kinematic script (e.g. after hitting a beast but remaining heavier).</summary>
    private bool _suppressPathPreview;

    /// <summary>Resolved tilt target; equals <see cref="transform"/> when no separate visual.</summary>
    private Transform _resolvedTiltRoot;
    private Vector3 _smoothedGroundNormal = Vector3.up;

    [Header("Forcefield impact damage (pair cooldown)")]
    [SerializeField] private float forcefieldImpactPairCooldown = 0.25f;

    private readonly Dictionary<int, float> _forcefieldImpactCooldownByOtherRb = new Dictionary<int, float>(16);

    [Header("Travel FX")]
    [Tooltip("All child lights that should only be on while this obstacle is actively traveling on its scripted path.")]
    [SerializeField] private Light[] travelLights;

    // -------------------------- INITIALIZATION --------------------------

    /// <summary>
    /// Called by CrossObstacleDirector right after Instantiate.
    /// Director is responsible for grounding start/target.
    /// We just follow that path.
    /// </summary>
    public void InitializeDirect(Vector3 startWorld, Vector3 targetWorld, float crossSpeed, float delayBeforeMove)
    {
        _startWS = startWorld;
        _targetWS = targetWorld;
        _pathHeightOffset = ComputeHalfHeightWorld();

        Vector2 startXZ = new Vector2(_startWS.x, _startWS.z);
        Vector2 targetXZ = new Vector2(_targetWS.x, _targetWS.z);

        if (keepPathFlatAndTrimOnElevation)
        {
            float startGroundY = SampleRoadSurfaceY(new Vector3(_startWS.x, 0f, _startWS.z));
            float refY = SampleRoadSurfaceY(_startWS);
            _targetWS = TrimTargetTowardStart(_startWS, _targetWS, refY);

            // Flat travel: world-up offset from surface under each endpoint.
            _startWS.y = startGroundY + _pathHeightOffset;
            _targetWS.y = _startWS.y;
        }
        else
        {
            _startWS = GroundedCenterAtXZ(new Vector2(_startWS.x, _startWS.z), startWorld);
            _targetWS = GroundedCenterAtXZ(new Vector2(_targetWS.x, _targetWS.z), targetWorld);
        }

        _preview = GetComponent<ObstaclePathPreview>();
        if (_preview)
            // Polyline points are already at obstacle center height; only nudge the line for visibility.
            _preview.SetYOffset(0.05f);

        speed = Mathf.Max(0.5f, crossSpeed);

        _initialDelay = Mathf.Max(0f, delayBeforeMove);
        _spawnedAt = Time.time;

        transform.position = _startWS;

        EnsureRigidbody();
        // Upright travel along chord; no terrain-tilt rotation fighting MovePosition.
        _rb.constraints = RigidbodyConstraints.FreezeRotation;

        // Mass from scale curve
        float computedMass = ComputeMassFromScale();
        _rb.mass = Mathf.Max(0.01f, computedMass);

        // init velocity tracking
        _prevPosition = transform.position;
        _lastVelocity = Vector3.zero;

        _initialized = true;
        _active = true;
        _convertedToPhysics = false;
        _suppressPathPreview = false;

        ResolveTiltVisualRoot();

        Vector2 flatDir = targetXZ - startXZ;
        if (flatDir.sqrMagnitude > 1e-6f) flatDir.Normalize();
        Quaternion parentYaw = flatDir.sqrMagnitude > 1e-6f
            ? Quaternion.LookRotation(new Vector3(flatDir.x, 0f, flatDir.y), Vector3.up)
            : Quaternion.identity;

        _rb.MoveRotation(parentYaw);
        transform.rotation = parentYaw;

        if (UseSeparateTiltVisual)
        {
            if (keepPathFlatAndTrimOnElevation)
                _resolvedTiltRoot.localRotation = Quaternion.identity;
            else
            {
                SampleRoadSurface(new Vector3(_startWS.x, 0f, _startWS.z), _startWS, out Vector3 n0);
                _smoothedGroundNormal = n0.sqrMagnitude > 1e-8f ? n0.normalized : Vector3.up;
                _resolvedTiltRoot.rotation = ComputeAlignedRotation(flatDir, _smoothedGroundNormal);
            }
        }
        else if (!keepPathFlatAndTrimOnElevation)
        {
            SampleRoadSurface(new Vector3(_startWS.x, 0f, _startWS.z), _startWS, out Vector3 n0);
            _smoothedGroundNormal = n0.sqrMagnitude > 1e-8f ? n0.normalized : Vector3.up;
            Quaternion tilted = ComputeAlignedRotation(flatDir, _smoothedGroundNormal);
            _rb.MoveRotation(tilted);
            transform.rotation = tilted;
        }

        RebuildPathPreview();
        UpdateTravelFxState();
    }

    private void LateUpdate()
    {
        if (!previewUpdateEveryFrame) return;
        if (_preview == null || !_initialized || _convertedToPhysics) return;
        if (!_active || _rb == null || !_rb.isKinematic) return;
        if (_suppressPathPreview) return;
        RebuildPathPreview();
    }

    /// <summary>Preview matches motion: flat XZ + constant Y, or XZ lerp with Y from ground + clearance.</summary>
    private void RebuildPathPreview()
    {
        if (_preview == null || !_initialized) return;

        int segs = Mathf.Clamp(previewPathSegments, 4, 64);
        if (_previewScratch == null || _previewScratch.Length < segs)
            _previewScratch = new Vector3[segs];

        bool fullSpan = Time.time < _spawnedAt + _initialDelay;

        if (keepPathFlatAndTrimOnElevation)
        {
            Vector2 aXZ = fullSpan ? new Vector2(_startWS.x, _startWS.z) : new Vector2(transform.position.x, transform.position.z);
            Vector2 bXZ = new Vector2(_targetWS.x, _targetWS.z);
            if (Vector2.Distance(aXZ, bXZ) < 0.02f)
            {
                _preview.SetEndpoints(_startWS, _targetWS);
                return;
            }

            for (int i = 0; i < segs; i++)
            {
                float t = segs <= 1 ? 0f : i / (float)(segs - 1);
                Vector2 xz = Vector2.Lerp(aXZ, bXZ, t);
                _previewScratch[i] = new Vector3(xz.x, _startWS.y, xz.y);
            }
        }
        else
        {
            Vector2 aXZ = fullSpan ? new Vector2(_startWS.x, _startWS.z) : new Vector2(transform.position.x, transform.position.z);
            Vector2 bXZ = new Vector2(_targetWS.x, _targetWS.z);
            if (Vector2.Distance(aXZ, bXZ) < 0.02f)
            {
                _preview.SetEndpoints(_startWS, _targetWS);
                return;
            }

            for (int i = 0; i < segs; i++)
            {
                float t = segs <= 1 ? 0f : i / (float)(segs - 1);
                Vector2 xz = Vector2.Lerp(aXZ, bXZ, t);
                Vector3 stab = fullSpan
                    ? Vector3.Lerp(_startWS, _targetWS, t)
                    : Vector3.Lerp(transform.position, _targetWS, t);
                _previewScratch[i] = GroundedCenterAtXZ(xz, stab);
            }
        }

        _preview.SetPolylineWorld(_previewScratch, segs);
    }

    /// <summary>World position for obstacle center: ground under XZ + half-height + clearance (world-up, not slope-normal).</summary>
    private Vector3 GroundedCenterAtXZ(Vector2 xz, Vector3 stabilityRef) =>
        GroundedCenterAtXZ(xz, stabilityRef, out _);

    private Vector3 GroundedCenterAtXZ(Vector2 xz, Vector3 stabilityRef, out Vector3 groundNormal)
    {
        Vector3 ground = SampleRoadSurface(new Vector3(xz.x, 0f, xz.y), stabilityRef, out groundNormal);
        float y = ground.y + _pathHeightOffset + Mathf.Max(0f, clearanceAboveGround);
        return new Vector3(xz.x, y, xz.y);
    }

    private void ResolveTiltVisualRoot()
    {
        if (tiltVisualRoot != null)
        {
            _resolvedTiltRoot = tiltVisualRoot;
            return;
        }

        Transform t = transform.Find("Visual");
        if (t == null) t = transform.Find("TiltRoot");
        if (t == null) t = transform.Find("Model");
        _resolvedTiltRoot = t != null ? t : transform;
    }

    private bool UseSeparateTiltVisual => _resolvedTiltRoot != null && _resolvedTiltRoot != transform;

    private void Awake()
    {
        // Ensure we have a Rigidbody and default it to kinematic (scripted path)
        _rb = GetComponent<Rigidbody>();
        if (_rb == null)
            _rb = gameObject.AddComponent<Rigidbody>();

        _rb.isKinematic = true;
        _rb.useGravity = false;
        _rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;

        _rb.interpolation = RigidbodyInterpolation.Interpolate;

        _rb.constraints = RigidbodyConstraints.FreezeRotation;


        // If reactLayers is untouched (~0 = everything), auto-ignore common ground
        if (reactLayers == ~0)
        {
            int road = LayerMask.NameToLayer("RoadSurface");
            int terrain = LayerMask.NameToLayer("Terrain");
            if (road >= 0) reactLayers &= ~(1 << road);
            if (terrain >= 0) reactLayers &= ~(1 << terrain);
        }

        _convertedToPhysics = false;
        _preview = GetComponent<ObstaclePathPreview>();
        CacheTravelLightsIfNeeded();
        SetTravelFxEnabled(false, true);
        ResolveTiltVisualRoot();

        if (surfaceGroundLayers.value == 0)
        {
            surfaceGroundLayers = LayerMask.GetMask("RoadSurface", "Grass", "Road");
        }
    }

    // -------------------------- MOVEMENT --------------------------

    private void FixedUpdate()
    {
        if (!_initialized || _convertedToPhysics)
        {
            SetTravelFxEnabled(false, true);
            return;
        }

        // Physics took over without our conversion flags (or desync): never show path line or scripted-drive a dynamic body.
        if (_rb == null || !_rb.isKinematic)
        {
            SetTravelFxEnabled(false, true);
            if (_active)
            {
                _convertedToPhysics = true;
                _active = false;
            }
            return;
        }

        if (!_active)
        {
            SetTravelFxEnabled(false, true);
            return;
        }

        if (Time.time < _spawnedAt + _initialDelay)
        {
            _prevPosition = transform.position;
            _lastVelocity = Vector3.zero;
            SetTravelFxEnabled(false, true);
            return;
        }

        SetTravelFxEnabled(!_suppressPathPreview);

        Vector3 current = transform.position;
        float step = speed * Time.fixedDeltaTime;

        if (enableScreenShake && _active && !_convertedToPhysics)
        {
            CarController.RequestWorldShake(
                transform.position,
                shakeIntensity,
                shakeFrequency,
                shakeMaxDistance,
                shakeFullIntensityDistance
            );
        }

        Vector3 nextPos;
        Quaternion parentYawTarget;
        Quaternion slopeAlignTarget;

        if (keepPathFlatAndTrimOnElevation)
        {
            Vector2 curXZ = new Vector2(current.x, current.z);
            Vector2 targetXZ = new Vector2(_targetWS.x, _targetWS.z);
            Vector2 toTargetXZ = targetXZ - curXZ;
            float dist = toTargetXZ.magnitude;
            if (dist < 0.01f)
            {
                OnReachedEnd();
                return;
            }

            Vector2 dirXZ = toTargetXZ / dist;
            float horizStep = Mathf.Min(step, dist);
            Vector2 nextXZ = curXZ + dirXZ * horizStep;
            nextPos = new Vector3(nextXZ.x, _startWS.y, nextXZ.y);
            parentYawTarget = ComputeAlignedRotation(dirXZ, Vector3.up);
            slopeAlignTarget = parentYawTarget;
        }
        else
        {
            // Constant speed on XZ toward target; Y from ground under that XZ (never fly on a 3D chord).
            Vector2 curXZ = new Vector2(current.x, current.z);
            Vector2 targetXZ = new Vector2(_targetWS.x, _targetWS.z);
            Vector2 toTargetXZ = targetXZ - curXZ;
            float dist = toTargetXZ.magnitude;
            if (dist < 0.01f)
            {
                OnReachedEnd();
                return;
            }

            Vector2 dirXZ = toTargetXZ / dist;
            float horizStep = Mathf.Min(step, dist);
            Vector2 nextXZ = curXZ + dirXZ * horizStep;
            Vector3 stabilityRef = Vector3.Lerp(current, new Vector3(_targetWS.x, current.y, _targetWS.z), 0.18f);
            nextPos = GroundedCenterAtXZ(nextXZ, stabilityRef, out Vector3 groundN);
            float nSmooth = 1f - Mathf.Exp(-surfaceNormalSmoothSpeed * Time.fixedDeltaTime);
            Vector3 gn = groundN.sqrMagnitude > 1e-8f ? groundN.normalized : Vector3.up;
            _smoothedGroundNormal = Vector3.Slerp(_smoothedGroundNormal, gn, nSmooth).normalized;
            parentYawTarget = Quaternion.LookRotation(new Vector3(dirXZ.x, 0f, dirXZ.y), Vector3.up);
            slopeAlignTarget = ComputeAlignedRotation(dirXZ, _smoothedGroundNormal);
        }

        float rotT = 1f - Mathf.Exp(-surfaceAlignRotationSpeed * Time.fixedDeltaTime);

        _lastVelocity = (nextPos - _prevPosition) / Mathf.Max(Time.fixedDeltaTime, 0.0001f);
        _prevPosition = nextPos;

        if (_rb != null && _rb.isKinematic)
        {
            _rb.MovePosition(nextPos);
            if (UseSeparateTiltVisual)
            {
                Quaternion newParent = Quaternion.Slerp(_rb.rotation, parentYawTarget, rotT);
                _rb.MoveRotation(newParent);
                if (keepPathFlatAndTrimOnElevation)
                {
                    _resolvedTiltRoot.localRotation = Quaternion.Slerp(
                        _resolvedTiltRoot.localRotation,
                        Quaternion.identity,
                        rotT);
                }
                else
                {
                    _resolvedTiltRoot.rotation = Quaternion.Slerp(
                        _resolvedTiltRoot.rotation,
                        slopeAlignTarget,
                        rotT);
                }
            }
            else
            {
                Quaternion singleTarget = keepPathFlatAndTrimOnElevation ? parentYawTarget : slopeAlignTarget;
                Quaternion newRot = Quaternion.Slerp(_rb.rotation, singleTarget, rotT);
                _rb.MoveRotation(newRot);
            }
        }
        else
        {
            if (UseSeparateTiltVisual)
            {
                Quaternion newParent = Quaternion.Slerp(transform.rotation, parentYawTarget, rotT);
                transform.SetPositionAndRotation(nextPos, newParent);
                if (keepPathFlatAndTrimOnElevation)
                {
                    _resolvedTiltRoot.localRotation = Quaternion.Slerp(
                        _resolvedTiltRoot.localRotation,
                        Quaternion.identity,
                        rotT);
                }
                else
                {
                    _resolvedTiltRoot.rotation = Quaternion.Slerp(
                        _resolvedTiltRoot.rotation,
                        slopeAlignTarget,
                        rotT);
                }
            }
            else
            {
                Quaternion singleTarget = keepPathFlatAndTrimOnElevation ? parentYawTarget : slopeAlignTarget;
                Quaternion newRot = Quaternion.Slerp(transform.rotation, singleTarget, rotT);
                transform.SetPositionAndRotation(nextPos, newRot);
            }
        }
    }

    private void OnReachedEnd()
    {
        _active = false;
        SetTravelFxEnabled(false, true);
        if (destroyOnExit)
            Destroy(gameObject);
        else
            enabled = false;
    }

    // -------------------------- COLLISION LOGIC --------------------------

    private void OnCollisionEnter(Collision collision)
    {
        if (collision == null || collision.collider == null) return;

        // After physics conversion: skill-gated forcefield chain damage vs other props.
        if (_convertedToPhysics && _rb != null)
        {
            ForcefieldImpactDamageHelper.TryApply(
                collision,
                _rb,
                _forcefieldImpactCooldownByOtherRb,
                forcefieldImpactPairCooldown,
                minRelativeSpeed: 0f);
            return;
        }

        if (!_initialized || !_active) return;

        // Cache impact direction from collision
        Vector3 impactDir = Vector3.zero;
        if (collision.contactCount > 0)
        {
            impactDir = -collision.GetContact(0).normal;
        }

        HandleImpactWithCollider(collision.collider, collision, impactDir);
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!_initialized || !_active || _convertedToPhysics) return;
        if (other == null) return;

        // Estimate impact direction from positions
        Vector3 impactDir = (transform.position - other.bounds.center).normalized;
        impactDir.y = 0f;
        if (impactDir.sqrMagnitude < 1e-6f) impactDir = _lastVelocity.normalized;

        HandleImpactWithCollider(other, null, impactDir);
    }

    [Header("Mass Comparison")]
    [SerializeField] private float massComparisonTolerance = 0.05f;
    [Tooltip("Extra mass added to the curve result (e.g., for metal shells, etc.).")]
    [SerializeField] private float defaultAddedMass = 0f;
    [Tooltip("Impulse Δv range applied to the other object when we are heavier.")]
    [SerializeField] private Vector2 pushDeltaVRange = new Vector2(1.5f, 3.0f);

    [Header("Upward Velocity Boost")]
    [Tooltip("Upward velocity boost range (min, max) applied to objects hit by this cross obstacle.")]
    [SerializeField] private Vector2 upwardBoostRange = new Vector2(2.0f, 5.0f);
    [Tooltip("If true, the upward boost scales with impact severity (relative speed). If false, uses a random value from the range.")]
    [SerializeField] private bool scaleUpwardBoostBySeverity = true;
    [Tooltip("Minimum relative speed to apply any upward boost.")]
    [SerializeField] private float minSpeedForUpwardBoost = 2f;
    [Tooltip("Speed at which the upward boost reaches its maximum value.")]
    [SerializeField] private float maxSpeedForUpwardBoost = 15f;

    [Header("Guaranteed Path-Preserving Bounce")]
    [Tooltip("Minimum upward velocity added when the cross obstacle keeps its scripted path after hitting NON-player objects.")]
    [SerializeField, Min(0f)] private float guaranteedUpwardBounceOnPathPreserve = 0.75f;
    [Tooltip("Minimum upward velocity added when the cross obstacle keeps path after hitting the player. Kept lower to avoid over-launching the car.")]
    [SerializeField, Min(0f)] private float guaranteedUpwardBounceOnPlayerHit = 0.35f;

    [Header("Player / phantom hit")]
    [Tooltip("Closing speed (|crossVel - carVel|) below this: ignore player touch — no push, cross stays on path. Stops braking/coast phantom hits.")]
    [SerializeField, Min(0f)] private float minRelativeSpeedForPlayerCrossImpact = 2.25f;

    [Header("Explosion Force (When Hit By Heavier Object)")]
    [Tooltip("Enable explosive physics reaction when this obstacle is hit by a heavier object.")]
    [SerializeField] private bool enableExplosionOnHeavierImpact = true;
    [Tooltip("Base explosion force applied to this obstacle when hit by heavier object.")]
    [SerializeField] private float explosionForceBase = 15f;
    [Tooltip("Explosion force multiplier based on mass difference (heavier = more force).")]
    [SerializeField] private float explosionForceMassScale = 0.5f;
    [Tooltip("Maximum explosion force cap.")]
    [SerializeField] private float explosionForceMax = 40f;
    [Tooltip("Upward bias for explosion force (0-1, where 1 = fully upward).")]
    [SerializeField, Range(0f, 1f)] private float explosionUpwardBias = 0.35f;
    [Tooltip("Torque applied during explosion for dramatic spin.")]
    [SerializeField] private Vector2 explosionTorqueRange = new Vector2(8f, 20f);
    [Tooltip("Apply explosion force to the OTHER object as well (mutual explosion).")]
    [SerializeField] private bool applyMutualExplosion = true;
    [Tooltip("Force multiplier applied to the heavier object (usually smaller since it's heavier).")]
    [SerializeField, Range(0f, 1f)] private float mutualExplosionScale = 0.3f;

    private void HandleImpactWithCollider(Collider other, Collision collision, Vector3 impactDir)
    {
        // Check layer mask first – if this collider isn't in reactLayers, ignore.
        if (!IsOnReactLayer(other))
            return;

        // Player special-case: ALWAYS keep path, never convert to physics.
        if (TryResolveActivePlayerFromCollider(other, out CarController car))
        {
            var playerRb = other.attachedRigidbody ?? other.GetComponentInParent<Rigidbody>();
            float myMass = ComputeMassFromScale();

            if (debugMassComparison)
            {
                Debug.Log($"[CrossTrackObstacle] COLLIDE player: crossMass={myMass:F2}, " +
                          $"playerRb={(playerRb != null ? playerRb.mass.ToString("F2") : "(no rb)")} cross keeps path");
            }

            if (playerRb != null)
            {
                float relativeSpeed = (_lastVelocity - playerRb.velocity).magnitude;
                if (relativeSpeed < minRelativeSpeedForPlayerCrossImpact)
                {
                    if (debugMassComparison)
                        Debug.Log($"[CrossTrackObstacle] Skip player touch (relSpeed={relativeSpeed:F2} < min {minRelativeSpeedForPlayerCrossImpact:F2})");
                    return;
                }

                Vector3 away = playerRb.position - transform.position;
                away.y = 0f;
                if (away.sqrMagnitude < 1e-6f) away = transform.forward;
                away.Normalize();

                float dv = UnityEngine.Random.Range(pushDeltaVRange.x, pushDeltaVRange.y);
                Vector3 deltaV = away * dv;

                float upwardBoost = Mathf.Max(CalculateUpwardBoost(relativeSpeed), guaranteedUpwardBounceOnPlayerHit);
                deltaV.y += upwardBoost;

                playerRb.AddForce(deltaV * Mathf.Max(0.01f, playerRb.mass), ForceMode.Impulse);

                if (debugMassComparison && upwardBoost > 0f)
                {
                    Debug.Log($"[CrossTrackObstacle] Applied upward boost: {upwardBoost:F2} (relSpeed={relativeSpeed:F2})");
                }
            }

            return; // DO NOT convert this obstacle
        }

        // Never mass-compare the active car (fixes missed CarController parent / root mismatch → phantom explosion).
        if (IsColliderOnActivePlayerCar(other))
            return;

        Rigidbody otherRb = other.attachedRigidbody ?? other.GetComponentInParent<Rigidbody>();

        if (RacingObstacleCollisionPopups.IsObstacleBuddy(other))
        {
            float relForPopup = _lastVelocity.magnitude;
            if (otherRb != null && otherRb.velocity.sqrMagnitude > 0.01f)
                relForPopup = (_lastVelocity - otherRb.velocity).magnitude;

            RacingObstacleCollisionPopups.TrySpawnObstacleClash(
                transform.root,
                other.transform.root,
                collision,
                other,
                relForPopup,
                crossClashMinRelativeSpeed,
                crossClashPopupHeight,
                crossClashPairCooldown,
                enableCrossClashCrashPopup);
        }

        // Non-player collision: mass comparison rules.
        float obstCurveMass = ComputeMassFromScale();
        float obstMass = obstCurveMass;

        var otherShuttle = other.GetComponentInParent<ShuttleTrackObstacle>();

        if (otherRb != null && otherRb.isKinematic)
        {
            // If it's a shuttle, let it convert itself (handles preview/light correctly)
            if (otherShuttle != null)
            {
                otherShuttle.ConvertToPhysicsOnHit();
            }
            else
            {
                var root = other.transform.root != null ? other.transform.root.gameObject : other.gameObject;
                int roadLayer = LayerMask.NameToLayer("RoadSurface");
                int terrainLayer = LayerMask.NameToLayer("Terrain");

                if (root.layer != roadLayer && root.layer != terrainLayer)
                {
                    ForceMakeDynamic(otherRb);
                    Physics.SyncTransforms();
                }
            }
        }


        var otherCross = other.GetComponentInParent<CrossTrackObstacle>();

        float otherMass;
        string otherMassSource;

        if (otherCross != null && otherCross != this)
        {
            otherMass = Mathf.Max(0.0001f, otherCross.ComputeMassFromScale());
            otherMassSource = "otherCrossCurve";
        }
        else if (otherShuttle != null)
        {
            // ShuttleTrackObstacle: use its rigidbody mass if available
            otherMass = otherRb != null ? Mathf.Max(0.0001f, otherRb.mass) : 10f;
            otherMassSource = "shuttleRb.mass";
        }
        else if (otherRb != null)
        {
            otherMass = Mathf.Max(0.0001f, otherRb.mass);
            otherMassSource = "otherRb.mass";
        }
        else
        {
            // Static geometry with no Rigidbody - IGNORE IT instead of treating as infinite mass
            // This prevents boost pads, track props, triggers, etc. from knocking us off path
            if (debugMassComparison)
            {
                string otherName = other.transform.root != null ? other.transform.root.name : other.gameObject.name;
                Debug.Log($"[CrossTrackObstacle] IGNORING static collider '{otherName}' (no Rigidbody) - not a valid obstacle.");
            }
            return; // EXIT EARLY - don't react to static geometry
        }

        if (debugMassComparison)
        {
            string otherName = other.transform.root != null ? other.transform.root.name : other.gameObject.name;
            bool otherKinematic = otherRb != null && otherRb.isKinematic;
            Debug.Log($"[CrossTrackObstacle] COLLIDE '{gameObject.name}' -> '{otherName}': " +
                      $"crossMass={obstMass:F2} otherMass={otherMass:F2} (src={otherMassSource}) otherHasRb={(otherRb != null)} " +
                      $"otherKinematic={otherKinematic} tolerance={massComparisonTolerance:F3}");
        }

        // Calculate relative speed for physics
        float relSpeed = _lastVelocity.magnitude;
        if (otherRb != null && otherRb.velocity.sqrMagnitude > 0.01f)
        {
            relSpeed = (_lastVelocity - otherRb.velocity).magnitude;
        }

        // If we are strictly heavier, we KEEP our kinematic scripted path.
        if (obstMass > otherMass + massComparisonTolerance)
        {
            var root = other.transform.root;
            if (root != null && root.gameObject.layer != LayerMask.NameToLayer("RoadSurface"))
            {
                TryMakeOtherDynamicGeneral(root.gameObject);
                otherRb = root.GetComponent<Rigidbody>() ?? otherRb;
            }

            // If the other is a shuttle, tell it to convert to physics
            if (otherShuttle != null)
            {
                otherShuttle.ConvertToPhysicsOnHit();
                otherRb = otherShuttle.GetComponent<Rigidbody>();
            }

            if (otherRb != null)
            {
                Vector3 away = otherRb.position - transform.position;
                away.y = 0f;
                if (away.sqrMagnitude < 1e-6f) away = transform.forward;
                away.Normalize();

                float dv = UnityEngine.Random.Range(pushDeltaVRange.x, pushDeltaVRange.y);
                Vector3 deltaV = away * dv;

                // Calculate upward boost
                float upwardBoost = Mathf.Max(CalculateUpwardBoost(relSpeed), guaranteedUpwardBounceOnPathPreserve);
                deltaV.y += upwardBoost;

                otherRb.AddForce(deltaV * Mathf.Max(0.01f, otherRb.mass), ForceMode.Impulse);

                // Add torque for dramatic effect
                float torque = UnityEngine.Random.Range(explosionTorqueRange.x, explosionTorqueRange.y) * 0.5f;
                Vector3 torqueDir = new Vector3(
                    UnityEngine.Random.Range(-1f, 1f),
                    UnityEngine.Random.Range(-1f, 1f),
                    UnityEngine.Random.Range(-1f, 1f)
                ).normalized;
                otherRb.AddTorque(torqueDir * torque, ForceMode.VelocityChange);

                if (debugMassComparison)
                {
                    Debug.Log($"[CrossTrackObstacle] ACTION: cross heavier -> kept path, pushed other ({otherRb.gameObject.name}) with upBoost={upwardBoost:F2}.");
                }
            }
            else if (debugMassComparison)
            {
                Debug.Log("[CrossTrackObstacle] ACTION: cross heavier -> kept path, but other had no rigidbody.");
            }

            var creatureHit = other.GetComponentInParent<TrackCreature>();
            if (creatureHit != null &&
                creatureHit.BehaviorType == CreatureBehaviorType.Aggressive &&
                !creatureHit.IsDead)
            {
                _suppressPathPreview = true;
                SetTravelFxEnabled(false, true);
            }

            return;
        }

        // Otherwise, we are lighter or equal → convert THIS obstacle to physics with EXPLOSION.
        if (debugMassComparison)
            Debug.Log($"[CrossTrackObstacle] ACTION: cross lighter or equal -> converting self to physics with explosion. massDiff={(otherMass - obstMass):F2}");

        EnsureRigidbody();

        // Calculate explosion parameters based on mass difference
        float massDiff = Mathf.Max(0f, otherMass - obstMass);
        float explosionForce = explosionForceBase + (massDiff * explosionForceMassScale);
        explosionForce = Mathf.Min(explosionForce, explosionForceMax);

        // Convert to physics with explosion
        ConvertToPhysicsWithExplosion(impactDir, explosionForce, relSpeed);

        // If the other is also a shuttle, convert it too
        if (otherShuttle != null)
        {
            otherShuttle.ConvertToPhysicsOnHit();
            otherRb = otherShuttle.GetComponent<Rigidbody>();
        }

        // Make sure other object is dynamic
        var otherRootObj = other.transform.root;
        if (otherRootObj != null && otherRootObj.gameObject.layer != LayerMask.NameToLayer("RoadSurface"))
        {
            TryMakeOtherDynamicGeneral(otherRootObj.gameObject);
            otherRb = otherRootObj.GetComponent<Rigidbody>() ?? otherRb;
        }

        // Apply mutual explosion to the other object if enabled
        if (applyMutualExplosion && otherRb != null)
        {
            Vector3 awayFromUs = (otherRb.position - transform.position);
            awayFromUs.y = 0f;
            if (awayFromUs.sqrMagnitude < 1e-6f) awayFromUs = -impactDir;
            awayFromUs.Normalize();

            float otherExplosionForce = explosionForce * mutualExplosionScale;
            Vector3 otherForceDir = Vector3.Lerp(awayFromUs, Vector3.up, explosionUpwardBias * 0.5f).normalized;

            otherRb.AddForce(otherForceDir * otherExplosionForce, ForceMode.VelocityChange);

            // Smaller torque for the heavier object
            float otherTorque = UnityEngine.Random.Range(explosionTorqueRange.x, explosionTorqueRange.y) * mutualExplosionScale;
            Vector3 otherTorqueDir = new Vector3(
                UnityEngine.Random.Range(-1f, 1f),
                UnityEngine.Random.Range(-1f, 1f),
                UnityEngine.Random.Range(-1f, 1f)
            ).normalized;
            otherRb.AddTorque(otherTorqueDir * otherTorque, ForceMode.VelocityChange);

            if (debugMassComparison)
            {
                Debug.Log($"[CrossTrackObstacle] Applied mutual explosion to {otherRb.gameObject.name}: force={otherExplosionForce:F2}");
            }
        }
    }



    public Vector3 GetWorldVelocity()
    {
        // If still on scripted motion, return the transform-derived velocity
        if (!_convertedToPhysics)
            return _lastVelocity;  // or however you track scripted velocity

        // After conversion, use real rigidbody velocity
        return _rb != null ? _rb.velocity : Vector3.zero;
    }

    /// <summary>
    /// Calculates the upward velocity boost based on relative speed and configuration.
    /// </summary>
    private float CalculateUpwardBoost(float relativeSpeed)
    {
        if (relativeSpeed < minSpeedForUpwardBoost)
            return 0f;

        if (scaleUpwardBoostBySeverity)
        {
            // Scale boost based on how fast the impact was
            float severity = Mathf.InverseLerp(minSpeedForUpwardBoost, maxSpeedForUpwardBoost, relativeSpeed);
            return Mathf.Lerp(upwardBoostRange.x, upwardBoostRange.y, severity);
        }
        else
        {
            // Random value from range
            return UnityEngine.Random.Range(upwardBoostRange.x, upwardBoostRange.y);
        }
    }

    private bool IsOnReactLayer(Collider col)
    {
        if (col == null) return false;

        int layer = col.gameObject.layer;


        // Now check if it's in the react layers
        if (((reactLayers.value) & (1 << layer)) != 0) return true;

        // also check the root in case of nested colliders
        if (col.transform.root != null)
        {
            int layerRoot = col.transform.root.gameObject.layer;
            if (((reactLayers.value) & (1 << layerRoot)) != 0) return true;
        }

        return false;
    }

    /// <summary>
    /// Car colliders must match even when <see cref="CarController"/> sits on a different root than <c>transform.root</c> (spawn parent, etc.).
    /// </summary>
    private static bool TryResolveActivePlayerFromCollider(Collider other, out CarController car)
    {
        car = null;
        if (other == null) return false;

        car = other.GetComponentInParent<CarController>();
        if (car != null) return true;

        var active = GameManager_Racing.Instance != null ? GameManager_Racing.Instance.ActiveCar : null;
        if (active == null) return false;

        Transform t = other.transform;
        if (t == active.transform || t.IsChildOf(active.transform))
        {
            car = active;
            return true;
        }

        return false;
    }

    private static bool IsColliderOnActivePlayerCar(Collider other)
    {
        if (other == null) return false;
        var active = GameManager_Racing.Instance != null ? GameManager_Racing.Instance.ActiveCar : null;
        if (active == null) return false;
        Transform t = other.transform;
        return t == active.transform || t.IsChildOf(active.transform);
    }

    /// <summary>
    /// Stops scripted cross-track motion so physics / forcefield can control this body (e.g. player forcefield).
    /// </summary>
    public void DetachFromScriptedPathForForcefield()
    {
        ConvertToPhysicsOnHit();
    }

    /// <summary>
    /// Standard conversion to physics (when hit by player or reaching end).
    /// </summary>
    private void ConvertToPhysicsOnHit()
    {
        if (_convertedToPhysics) return;
        _convertedToPhysics = true;

        _active = false;           // stop scripted motion

        SetTravelFxEnabled(false, true);

        if (_rb == null) return;

        _rb.isKinematic = false;
        _rb.useGravity = true;
        _rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        _rb.interpolation = RigidbodyInterpolation.Interpolate;
        _rb.constraints = RigidbodyConstraints.None;

        // give it its last kinematic velocity so physics continues smoothly
        _rb.velocity = _lastVelocity;

        // small upward nudge to avoid it being clipped inside surfaces
        _rb.position += Vector3.up * 0.01f;

        _rb.WakeUp();
        Physics.SyncTransforms();
    }

    /// <summary>
    /// Explosive conversion to physics when hit by a heavier object.
    /// Sends this obstacle flying dramatically.
    /// </summary>
    private void ConvertToPhysicsWithExplosion(Vector3 impactDir, float force, float relativeSpeed)
    {
        if (_convertedToPhysics) return;
        _convertedToPhysics = true;

        _active = false;
        SetTravelFxEnabled(false, true);

        if (_rb == null) return;

        _rb.isKinematic = false;
        _rb.useGravity = true;
        _rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        _rb.interpolation = RigidbodyInterpolation.Interpolate;
        _rb.constraints = RigidbodyConstraints.None;

        // Small upward nudge to avoid clipping
        _rb.position += Vector3.up * 0.05f;

        if (enableExplosionOnHeavierImpact)
        {
            // Calculate explosion direction: away from impact with upward bias
            Vector3 explosionDir = -impactDir;
            explosionDir.y = 0f;
            if (explosionDir.sqrMagnitude < 1e-6f)
                explosionDir = _lastVelocity.normalized;
            explosionDir.Normalize();

            // Blend in upward component
            Vector3 finalDir = Vector3.Lerp(explosionDir, Vector3.up, explosionUpwardBias).normalized;

            // Apply the explosion force
            _rb.AddForce(finalDir * force, ForceMode.VelocityChange);

            // Add dramatic spin
            float torqueMag = UnityEngine.Random.Range(explosionTorqueRange.x, explosionTorqueRange.y);
            Vector3 torqueDir = new Vector3(
                UnityEngine.Random.Range(-1f, 1f),
                UnityEngine.Random.Range(-1f, 1f),
                UnityEngine.Random.Range(-1f, 1f)
            ).normalized;
            _rb.AddTorque(torqueDir * torqueMag, ForceMode.VelocityChange);

            if (debugMassComparison)
            {
                Debug.Log($"[CrossTrackObstacle] Explosion applied: force={force:F2}, dir={finalDir}, torque={torqueMag:F2}");
            }
        }
        else
        {
            // Just inherit last velocity
            _rb.velocity = _lastVelocity;
        }

        _rb.WakeUp();
        Physics.SyncTransforms();
    }

    // -------------------------- MASS / HELPERS --------------------------

    [Header("Size → Mass (hard mapping)")]
    [SerializeField]
    private AnimationCurve massByScaleCurve = new AnimationCurve(
        new Keyframe(0.1f, 5f),
        new Keyframe(1f, 12f),
        new Keyframe(2f, 30f)
    );

    [Tooltip("Fallback mass if the curve is invalid.")]
    [SerializeField] private float fallbackMass = 10f;

    /// <summary>
    /// Public accessor for mass computation (used by other obstacles for comparison).
    /// </summary>
    public float ComputeMassFromScale()
    {
        float scale = transform.localScale.x; // assume uniform
        if (massByScaleCurve == null || massByScaleCurve.length == 0)
            return Mathf.Max(0.01f, fallbackMass);

        float curveMass = massByScaleCurve.Evaluate(scale);
        return Mathf.Max(0.01f, curveMass + defaultAddedMass);
    }

    private void EnsureRigidbody()
    {
        if (_rb == null)
        {
            _rb = GetComponent<Rigidbody>();
            if (_rb == null)
                _rb = gameObject.AddComponent<Rigidbody>();
        }

        // Always enforce kinematic-mover settings.
        _rb.isKinematic = true;
        _rb.useGravity = false;
        _rb.detectCollisions = true;
        _rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
        _rb.interpolation = RigidbodyInterpolation.Interpolate;
        _rb.constraints = RigidbodyConstraints.FreezeRotation;
    }

    private void OnDisable()
    {
        SetTravelFxEnabled(false, true);
    }

    private void UpdateTravelFxState()
    {
        bool movingOnScriptedPath =
            _initialized &&
            _active &&
            !_convertedToPhysics &&
            !_suppressPathPreview &&
            enabled &&
            _rb != null &&
            _rb.isKinematic &&
            Time.time >= _spawnedAt + _initialDelay;

        SetTravelFxEnabled(movingOnScriptedPath);
    }

    private void CacheTravelLightsIfNeeded()
    {
        if (travelLights != null && travelLights.Length > 0) return;
        travelLights = GetComponentsInChildren<Light>(true);
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

        if (travelLights == null) return;
        for (int i = 0; i < travelLights.Length; i++)
        {
            if (travelLights[i] != null)
                travelLights[i].enabled = enabledNow;
        }
    }

    private void ForceMakeDynamic(Rigidbody rb)
    {
        if (rb == null) return;
        if (!rb.isKinematic) return;

        rb.isKinematic = false;
        rb.useGravity = true;
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        rb.constraints = RigidbodyConstraints.None;
        rb.WakeUp();
    }

    private void ResolveKinematicImpact(Collider other, Vector3 impactDir, Vector3 hitPosition)
    {
        if (_convertedToPhysics) return;
        if (!IsOnReactLayer(other)) return;

        if (TryResolveActivePlayerFromCollider(other, out _) || IsColliderOnActivePlayerCar(other))
            return;

        Rigidbody otherRb =
            other.attachedRigidbody ??
            other.GetComponentInParent<Rigidbody>();

        // If the other object has no RB, add one
        if (otherRb == null)
        {
            var root = other.transform.root;
            otherRb = root.gameObject.AddComponent<Rigidbody>();
        }

        // Convert the other object to physics IMMEDIATELY
        ForceMakeDynamic(otherRb);

        // Force solver ownership THIS FRAME
        Physics.SyncTransforms();

        // Compute relative speed
        float relSpeed = _lastVelocity.magnitude;

        // Explosion direction away from cross obstacle
        Vector3 away = (otherRb.position - transform.position);
        away.y = 0f;
        if (away.sqrMagnitude < 1e-6f)
            away = -impactDir;
        away.Normalize();

        // Apply impulse so the solver separates them
        float push = Mathf.Lerp(4f, 12f, Mathf.InverseLerp(0f, 15f, relSpeed));
        otherRb.AddForce(away * push, ForceMode.VelocityChange);

        // Optional upward kick so it doesn’t “pin”
        otherRb.AddForce(Vector3.up * 2.5f, ForceMode.VelocityChange);

        // If the other object is heavier → explode THIS obstacle
        float myMass = ComputeMassFromScale();
        float otherMass = otherRb.mass;

        if (otherMass >= myMass)
        {
            ConvertToPhysicsWithExplosion(impactDir, explosionForceBase, relSpeed);
        }
    }


    private void TryMakeOtherDynamicGeneral(GameObject obj)
    {
        if (!obj) return;
        var rb = obj.GetComponent<Rigidbody>();
        if (rb == null) return;

        rb.isKinematic = false;
        rb.useGravity = true;
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        rb.constraints = RigidbodyConstraints.None;
        rb.WakeUp();
    }

    /// <summary>
    /// Public property to check if this obstacle is still on its scripted path.
    /// </summary>
    public bool IsOnScriptedPath =>
        _active && _initialized && !_convertedToPhysics && _rb != null && _rb.isKinematic;

    private Vector3 SampleRoadSurface(Vector3 worldProbe, out Vector3 normal) =>
        SampleRoadSurface(worldProbe, worldProbe, out normal);

    /// <summary>
    /// Picks a stable ground hit near the expected height (reduces jitter when road + terrain overlap).
    /// </summary>
    private Vector3 SampleRoadSurface(Vector3 worldProbe, Vector3 stabilityRef, out Vector3 normal)
    {
        float refY = Mathf.Max(stabilityRef.y, worldProbe.y);
        Vector3 origin = new Vector3(worldProbe.x, refY + surfaceProbeUpOffset, worldProbe.z);
        float maxDist = surfaceProbeUpOffset + surfaceMaxDownDist;
        RaycastHit[] hits = Physics.RaycastAll(origin, Vector3.down, maxDist, surfaceGroundLayers, QueryTriggerInteraction.Ignore);

        if (hits == null || hits.Length == 0)
            return SampleRoadSurfaceLegacy(worldProbe, out normal);

        // First hit along the ray = topmost surface under the probe (stable; avoids picking
        // hillside/prop colliders whose Y happens to match a high stabilityRef).
        int best = 0;
        float bestDist = hits[0].distance;
        for (int i = 1; i < hits.Length; i++)
        {
            if (hits[i].distance < bestDist)
            {
                bestDist = hits[i].distance;
                best = i;
            }
        }

        normal = hits[best].normal.sqrMagnitude > 1e-8f ? hits[best].normal.normalized : Vector3.up;
        return new Vector3(worldProbe.x, hits[best].point.y, worldProbe.z);
    }

    private Vector3 SampleRoadSurfaceLegacy(Vector3 worldProbe, out Vector3 normal)
    {
        Vector3 projected = SpawnUtils.ProjectOntoSurface(worldProbe, out normal, 2f, 50f, surfaceGroundLayers);
        if ((projected - worldProbe).sqrMagnitude < 1e-6f)
            projected = SpawnUtils.ProjectOntoSurface(worldProbe, out normal, 2f, 50f, LayerMask.GetMask("RoadSurface"));
        if ((projected - worldProbe).sqrMagnitude < 1e-6f)
            projected = SpawnUtils.ProjectOntoSurface(worldProbe, out normal, 2f, 50f, null);
        if (normal.sqrMagnitude < 1e-8f) normal = Vector3.up;
        else normal.Normalize();
        return projected;
    }

    private float SampleRoadSurfaceY(Vector3 ws)
    {
        Vector3 n;
        return SampleRoadSurface(ws, out n).y;
    }

    private static Quaternion ComputeAlignedRotation(Vector2 dirXZ, Vector3 surfaceNormal)
    {
        surfaceNormal = surfaceNormal.sqrMagnitude > 1e-8f ? surfaceNormal.normalized : Vector3.up;
        Vector3 moveFlat = new Vector3(dirXZ.x, 0f, dirXZ.y);
        if (moveFlat.sqrMagnitude < 1e-8f)
            return Quaternion.LookRotation(Vector3.forward, surfaceNormal);

        moveFlat.Normalize();
        Vector3 forward = Vector3.ProjectOnPlane(moveFlat, surfaceNormal);
        if (forward.sqrMagnitude < 1e-8f)
            forward = Vector3.Cross(surfaceNormal, Vector3.right);
        if (forward.sqrMagnitude < 1e-8f)
            forward = Vector3.Cross(surfaceNormal, Vector3.forward);
        forward.Normalize();
        return Quaternion.LookRotation(forward, surfaceNormal);
    }

    private Vector3 TrimTargetTowardStart(Vector3 start, Vector3 target, float refY)
    {
        float targetY = SampleRoadSurfaceY(target);
        if (Mathf.Abs(targetY - refY) <= maxAllowedPathElevationDelta)
            return target;

        Vector3 lo = start;
        Vector3 hi = target;
        for (int i = 0; i < endpointTrimIterations; i++)
        {
            Vector3 mid = Vector3.Lerp(lo, hi, 0.5f);
            float midY = SampleRoadSurfaceY(mid);
            bool valid = Mathf.Abs(midY - refY) <= maxAllowedPathElevationDelta;
            if (valid) lo = mid; else hi = mid;
        }
        return lo;
    }

    private float ComputeHalfHeightWorld()
    {
        // Parent-only half-height: use this GameObject's own bounds, not children.
        // This matches center-pivot prefabs where "half height" should ground the bottom.
        float maxHalfHeight = 0f;
        bool found = false;

        Renderer selfRenderer = GetComponent<Renderer>();
        if (selfRenderer != null && !(selfRenderer is LineRenderer))
        {
            maxHalfHeight = Mathf.Max(maxHalfHeight, selfRenderer.bounds.extents.y);
            found = true;
        }

        Collider[] selfColliders = GetComponents<Collider>();
        for (int i = 0; i < selfColliders.Length; i++)
        {
            Collider c = selfColliders[i];
            if (c == null || c.isTrigger) continue;
            maxHalfHeight = Mathf.Max(maxHalfHeight, c.bounds.extents.y);
            found = true;
        }

        if (!found)
            return 0.5f * Mathf.Max(0.1f, transform.lossyScale.y);

        return Mathf.Max(0.05f, maxHalfHeight);
    }

    /// <summary>
    /// Heavy hit from a rolling log — break scripted cross motion and launch with explosion rules.
    /// </summary>
    public void ApplyRollingLogRam(Vector3 fromLogPlanarDirection, float relativeSpeed)
    {
        if (_convertedToPhysics) return;

        Vector3 d = fromLogPlanarDirection;
        d.y = 0f;
        if (d.sqrMagnitude < 1e-6f)
        {
            d = _lastVelocity.sqrMagnitude > 1e-4f ? -_lastVelocity.normalized : -transform.forward;
            d.y = 0f;
            if (d.sqrMagnitude < 1e-6f) d = -transform.forward;
        }
        d.Normalize();

        ConvertToPhysicsWithExplosion(-d, explosionForceBase * 0.95f, Mathf.Max(relativeSpeed, 4f));

        if (enableCrossClashCrashPopup && RacingPopups.IsReady)
            RacingPopups.CrashWorld(transform.position + Vector3.up * crossClashPopupHeight);
    }

#if UNITY_EDITOR
    private void OnDrawGizmos()
    {
        if (!drawPathGizmos) return;
        if (!_initialized && Application.isPlaying == false) return;

        Gizmos.color = Color.cyan;
        Gizmos.DrawSphere(_startWS, 0.15f);
        Gizmos.color = Color.magenta;
        Gizmos.DrawSphere(_targetWS, 0.15f);
        Gizmos.color = Color.yellow;
        Gizmos.DrawLine(_startWS, _targetWS);
    }
#endif
}