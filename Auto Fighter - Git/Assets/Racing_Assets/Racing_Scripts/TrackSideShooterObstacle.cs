using UnityEngine;
using DG.Tweening;

/// <summary>
/// Roadside turret obstacle: snaps to the left or right edge of the road, faces inward,
/// and fires lead-aimed projectiles when the player is nearby.
/// Register the prefab on <see cref="TrackObstacleSpawner"/> like shuttle/static props.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Rigidbody))]
public class TrackSideShooterObstacle : MonoBehaviour
{
    public enum SideMode
    {
        Random,
        PreferSpawnSide,
        AlwaysLeft,
        AlwaysRight
    }

    [Header("Track Binding")]
    [SerializeField] private ProceduralTrackGenerator trackGenerator;
    [Tooltip("If > 0, overrides ProceduralTrackGenerator.RoadWidth.")]
    [SerializeField] private float overrideRoadWidth = 0f;
    [Tooltip("How far onto the road the muzzle sits past the road edge (meters). Positive = muzzle on the driving surface.")]
    [SerializeField] private float muzzleOntoTrack = 0.35f;
    [Tooltip("Extra push of the body further off-road (meters), on top of natural muzzle offset.")]
    [SerializeField, Min(0f)] private float extraOffRoadPadding = 0.15f;
    [SerializeField] private bool autoHalfWidthFromRenderer = true;
    [SerializeField] private float manualHalfWidth = 0.5f;
    [SerializeField] private float heightOffset = 0.02f;
    [SerializeField] private SideMode sideMode = SideMode.PreferSpawnSide;

    [Header("Facing")]
    [Tooltip("Yaw bias toward track forward (0 = pure inward, 1 = fully down-track).")]
    [SerializeField, Range(0f, 1f)] private float faceDownTrackBias = 0.15f;

    [Header("Engagement")]
    [Tooltip("Start shooting when the player is within this world distance (meters).")]
    [SerializeField, Min(1f)] private float engageRange = 45f;
    [Tooltip("Stop shooting when the player is farther than this (should be >= engage).")]
    [SerializeField, Min(1f)] private float disengageRange = 60f;
    [Tooltip("Only fire if the player is roughly in front of the muzzle (dot > this).")]
    [SerializeField, Range(-1f, 1f)] private float minAimDot = 0.15f;

    [Header("Accuracy")]
    [Tooltip("1 = perfect lead aim. 0 = mostly shoot toward current car position with full miss cone.")]
    [SerializeField, Range(0f, 1f)] private float aimAccuracy = 0.55f;
    [Tooltip("Max yaw/pitch error (degrees) at aimAccuracy = 0. Scales down as accuracy rises.")]
    [SerializeField, Min(0f)] private float aimErrorMaxDegrees = 14f;
    [Tooltip("How much perfect lead is diluted toward aiming at the car's current position (1 = full dilution at accuracy 0).")]
    [SerializeField, Range(0f, 1f)] private float leadBleedOff = 0.85f;

    [Header("Firing")]
    [SerializeField] private Transform muzzle;
    [SerializeField] private GameObject projectilePrefab;
    [Tooltip("Projectile travel speed (m/s).")]
    [SerializeField, Min(1f)] private float projectileSpeed = 28f;
    [Tooltip("Seconds between shots while engaged.")]
    [SerializeField, Min(0.05f)] private float fireCooldown = 1.6f;
    [Tooltip("Extra lead time added on top of ballistic intercept (seconds). 0 = pure intercept.")]
    [SerializeField, Min(0f)] private float extraLeadTime = 0f;
    [Tooltip("Max distance a shot is allowed to travel.")]
    [SerializeField, Min(5f)] private float maxShotRange = 70f;
    [SerializeField, Min(0.1f)] private float projectileLifetime = 3.5f;
    [Tooltip("Random delay before the first shot after engaging.")]
    [SerializeField] private Vector2 firstShotDelayRange = new Vector2(0.15f, 0.6f);

    [Header("Damage (applied by projectile on car hit)")]
    [SerializeField, Min(0f)] private float hitHpDamage = 8f;
    [Tooltip("Fuel removed as a fraction of max fuel (0.05 = 5%).")]
    [SerializeField, Range(0f, 1f)] private float hitFuelPercent = 0.03f;
    [SerializeField, Range(0f, 1f)] private float crashFxSeverity = 0.45f;
    [SerializeField, Min(0f)] private float crashImpactSpeed = 12f;

    [Header("Pre-shot bob (DOTween)")]
    [Tooltip("Squash/dip wind-up then a kick when the shot fires (same style as shuttle pre-move bob).")]
    [SerializeField] private bool enablePreShotBob = true;
    [Tooltip("Transform to animate (uses this object if null).")]
    [SerializeField] private Transform bobTarget;
    [Tooltip("Slow crouch/build before the shot. Only plays when a bullet will actually fire.")]
    [SerializeField, Min(0.05f)] private float windUpDuration = 0.65f;
    [SerializeField, Min(0.05f)] private float shootKickDuration = 0.12f;
    [SerializeField, Min(0.05f)] private float settleDuration = 0.18f;
    [Tooltip("Local Y scale multiplier during wind-up crouch.")]
    [SerializeField, Range(0.5f, 1f)] private float windUpSquashY = 0.88f;
    [Tooltip("Local XZ scale multiplier during wind-up crouch.")]
    [SerializeField, Min(1f)] private float windUpWidenXZ = 1.08f;
    [Tooltip("Uniform local scale multiplier at the shoot kick peak.")]
    [SerializeField, Min(1.01f)] private float shootPeakMultiplier = 1.1f;
    [Tooltip("How far the box dips down (local Y meters) during wind-up.")]
    [SerializeField, Min(0f)] private float windUpDip = 0.1f;
    [Tooltip("How far the box hops up (local Y meters) on the shoot kick.")]
    [SerializeField, Min(0f)] private float shootHop = 0.12f;
    [SerializeField] private Ease windUpEase = Ease.InCubic;
    [SerializeField] private Ease shootEase = Ease.OutBack;
    [SerializeField] private Ease settleEase = Ease.OutQuad;

    [Header("Debug")]
    [SerializeField] private bool verboseDebug = false;

    private Rigidbody _rb;
    private Transform _player;
    private CarController _car;
    private Rigidbody _carRb;
    private Collider _carBodyCollider;
    private Collider[] _ownColliders;

    private bool _initialized;
    private bool _engaged;
    private bool _knockedOffBase;
    private float _nextFireTime;
    private int _sideSign = 1; // +1 = right edge, -1 = left edge

    private Vector3 _bobBaseLocalScale = Vector3.one;
    private Vector3 _bobBaseLocalPos;
    private bool _bobBaseCaptured;
    private bool _windUpActive;
    private Sequence _preShotSeq;

    public void SetGenerator(ProceduralTrackGenerator generator) => trackGenerator = generator;

    public void SetPlayer(Transform player)
    {
        _player = player;
        CacheCar(player);
    }

    /// <summary>
    /// True once this turret has been knocked off its roadside base and can no longer fire.
    /// </summary>
    public bool IsKnockedOffBase => _knockedOffBase;

    /// <summary>
    /// Hit by a beast, projectile, or similar: stop firing and fling as a physics prop.
    /// </summary>
    public void ConvertToPhysicsOnHit(Vector3 worldImpulse)
    {
        if (_knockedOffBase)
            return;

        _knockedOffBase = true;
        enabled = false;
        _engaged = false;
        KillPreShotTweens(true);

        if (_rb == null)
            _rb = GetComponent<Rigidbody>() ?? gameObject.AddComponent<Rigidbody>();

        _rb.constraints = RigidbodyConstraints.None;
        _rb.isKinematic = false;
        _rb.useGravity = true;
        _rb.interpolation = RigidbodyInterpolation.Interpolate;
        _rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        _rb.WakeUp();

        if (worldImpulse.sqrMagnitude > 1e-6f)
            _rb.AddForce(worldImpulse, ForceMode.Impulse);
    }

    private void OnDisable()
    {
        KillPreShotTweens(true);
    }

    private void Awake()
    {
        _rb = GetComponent<Rigidbody>();
        if (_rb == null) _rb = gameObject.AddComponent<Rigidbody>();
        _rb.isKinematic = true;
        _rb.useGravity = false;
        _rb.interpolation = RigidbodyInterpolation.None;
        _rb.collisionDetectionMode = CollisionDetectionMode.Discrete;
        _rb.constraints = RigidbodyConstraints.FreezeAll;

        _ownColliders = GetComponentsInChildren<Collider>(true);
    }

    private void Start()
    {
        if (trackGenerator == null)
            trackGenerator = FindObjectOfType<ProceduralTrackGenerator>();

        if (_player == null)
        {
            var car = FindObjectOfType<CarController>();
            if (car != null)
                SetPlayer(car.transform);
        }

        EnsureMuzzle();
        SnapToRoadsideAndFaceInward();
        CaptureBobBase();
        _initialized = true;
        _nextFireTime = Time.time + Random.Range(firstShotDelayRange.x, firstShotDelayRange.y);

        if (verboseDebug)
            Debug.Log($"[TrackSideShooter] Ready on side={(_sideSign < 0 ? "left" : "right")} at {transform.position}");
    }

    private void Update()
    {
        if (!_initialized || _knockedOffBase || projectilePrefab == null || _player == null)
            return;

        float dist = Vector3.Distance(transform.position, _player.position);
        if (!_engaged)
        {
            if (dist <= engageRange)
            {
                _engaged = true;
                _nextFireTime = Time.time + Random.Range(firstShotDelayRange.x, firstShotDelayRange.y);
            }
            return;
        }

        if (dist > Mathf.Max(engageRange, disengageRange))
        {
            _engaged = false;
            if (_windUpActive)
                KillPreShotTweens(true);
            return;
        }

        if (_windUpActive || Time.time < _nextFireTime)
            return;

        // Never start the bob unless we can actually shoot right now.
        if (!CanFireNow())
        {
            _nextFireTime = Time.time + 0.2f;
            return;
        }

        if (enablePreShotBob && _bobBaseCaptured)
            BeginWindUpAndFire();
        else if (TryFire())
            _nextFireTime = Time.time + fireCooldown;
        else
            _nextFireTime = Time.time + 0.2f;
    }

    private void EnsureMuzzle()
    {
        if (muzzle != null) return;

        var existing = transform.Find("Muzzle");
        if (existing != null)
        {
            muzzle = existing;
            return;
        }

        var go = new GameObject("Muzzle");
        go.transform.SetParent(transform, false);
        go.transform.localPosition = new Vector3(0f, 0.5f, 0.6f);
        go.transform.localRotation = Quaternion.identity;
        muzzle = go.transform;
    }

    private void SnapToRoadsideAndFaceInward()
    {
        EnsureMuzzle();

        Vector3 origin = transform.position;
        if (!TryResolveTrackFrame(origin, out Vector3 forward, out Vector3 lateral, out Vector3 centerOnPath))
        {
            forward = transform.forward; forward.y = 0f;
            if (forward.sqrMagnitude < 1e-6f) forward = Vector3.forward;
            forward.Normalize();
            lateral = Vector3.Cross(Vector3.up, forward).normalized;
            centerOnPath = origin;
        }

        _sideSign = ResolveSideSign(origin, centerOnPath, lateral);

        float halfRoad = DetermineHalfRoadWidth();
        // Muzzle XZ should sit just onto the driving surface.
        float muzzleLateral = Mathf.Max(0.05f, halfRoad - muzzleOntoTrack);
        Vector3 muzzleTargetFlat = centerOnPath + lateral * (_sideSign * muzzleLateral);
        muzzleTargetFlat.y = centerOnPath.y;

        // Face inward (toward road center), with a small down-track bias.
        Vector3 inward = (-_sideSign * lateral).normalized;
        Vector3 face = Vector3.Slerp(inward, forward, faceDownTrackBias).normalized;
        if (face.sqrMagnitude < 1e-6f) face = inward;
        Quaternion rot = Quaternion.LookRotation(face, Vector3.up);

        // Place root so muzzle XZ matches the track-edge target → body hangs off-road.
        Vector3 localMuzzle = muzzle != null ? muzzle.localPosition : new Vector3(0f, 0.5f, 0.6f);
        Vector3 rotatedMuzzle = rot * localMuzzle;
        Vector3 rootFlat = muzzleTargetFlat - new Vector3(rotatedMuzzle.x, 0f, rotatedMuzzle.z);
        if (extraOffRoadPadding > 0.001f)
            rootFlat += lateral * (_sideSign * extraOffRoadPadding);

        // Ground the BODY off-track (any surface — not RoadSurface-only, or it misses and sinks).
        Vector3 grounded = SpawnUtils.ProjectOntoSurface(rootFlat, out Vector3 groundNormal, 8f, 50f, null);
        if (groundNormal.sqrMagnitude < 1e-6f) groundNormal = Vector3.up;

        // Re-orient to ground normal while keeping facing.
        Vector3 faceFlat = face; faceFlat.y = 0f;
        if (faceFlat.sqrMagnitude < 1e-6f) faceFlat = forward;
        faceFlat.Normalize();
        rot = Quaternion.LookRotation(faceFlat, groundNormal);

        // Recompute flat offset with final rotation, then sit bottom on the surface.
        rotatedMuzzle = rot * localMuzzle;
        rootFlat = muzzleTargetFlat - new Vector3(rotatedMuzzle.x, 0f, rotatedMuzzle.z);
        if (extraOffRoadPadding > 0.001f)
            rootFlat += lateral * (_sideSign * extraOffRoadPadding);
        grounded = SpawnUtils.ProjectOntoSurface(rootFlat, out groundNormal, 8f, 50f, null);
        if (groundNormal.sqrMagnitude < 1e-6f) groundNormal = Vector3.up;

        transform.SetPositionAndRotation(grounded, rot);

        float bottomOffset = ComputePivotToBottomAlongUp(groundNormal);
        transform.position = grounded + groundNormal * (bottomOffset + heightOffset);

        if (_rb != null)
            _rb.position = transform.position;
    }

    /// <summary>
    /// Distance from pivot to the lowest renderer/collider point, measured along world up/ground normal.
    /// Used so the mesh sits on the ground instead of burying the pivot.
    /// </summary>
    private float ComputePivotToBottomAlongUp(Vector3 up)
    {
        up.Normalize();
        float lowest = 0f;
        bool found = false;

        var renderers = GetComponentsInChildren<Renderer>();
        for (int i = 0; i < renderers.Length; i++)
        {
            if (renderers[i] == null) continue;
            Bounds b = renderers[i].bounds;
            SampleBoundsLowest(b, up, ref lowest, ref found);
        }

        var cols = GetComponentsInChildren<Collider>();
        for (int i = 0; i < cols.Length; i++)
        {
            Collider c = cols[i];
            if (c == null || c.isTrigger) continue;
            SampleBoundsLowest(c.bounds, up, ref lowest, ref found);
        }

        if (!found)
            return DetermineSelfHalfWidth(); // rough fallback

        // lowest is negative if bottom is below pivot along up
        return Mathf.Max(0f, -lowest);
    }

    private void SampleBoundsLowest(Bounds b, Vector3 up, ref float lowest, ref bool found)
    {
        Vector3 ext = b.extents;
        Vector3 ctr = b.center;
        // 8 corners of the AABB
        for (int x = -1; x <= 1; x += 2)
        for (int y = -1; y <= 1; y += 2)
        for (int z = -1; z <= 1; z += 2)
        {
            Vector3 corner = ctr + new Vector3(ext.x * x, ext.y * y, ext.z * z);
            float along = Vector3.Dot(corner - transform.position, up);
            if (!found || along < lowest)
            {
                lowest = along;
                found = true;
            }
        }
    }

    private int ResolveSideSign(Vector3 origin, Vector3 centerOnPath, Vector3 lateral)
    {
        switch (sideMode)
        {
            case SideMode.AlwaysLeft: return -1;
            case SideMode.AlwaysRight: return 1;
            case SideMode.Random: return Random.value < 0.5f ? -1 : 1;
            default:
            {
                float side = Vector3.Dot(origin - centerOnPath, lateral);
                if (Mathf.Abs(side) < 0.05f)
                    return Random.value < 0.5f ? -1 : 1;
                return side >= 0f ? 1 : -1;
            }
        }
    }

    private Transform GetBobTarget() => bobTarget != null ? bobTarget : transform;

    private void CaptureBobBase()
    {
        Transform t = GetBobTarget();
        _bobBaseLocalScale = t.localScale;
        _bobBaseLocalPos = t.localPosition;
        _bobBaseCaptured = true;
    }

    private void KillPreShotTweens(bool resetToBase)
    {
        _windUpActive = false;
        if (_preShotSeq != null && _preShotSeq.IsActive())
            _preShotSeq.Kill(false);
        _preShotSeq = null;

        Transform t = GetBobTarget();
        DOTween.Kill(t, false);
        if (resetToBase && _bobBaseCaptured)
        {
            t.localScale = _bobBaseLocalScale;
            t.localPosition = _bobBaseLocalPos;
        }
    }

    private void BeginWindUpAndFire()
    {
        if (_windUpActive || !_bobBaseCaptured)
            return;

        // Final gate — bob must not play for a dry-fire / aim fail.
        if (!CanFireNow())
        {
            _nextFireTime = Time.time + 0.2f;
            return;
        }

        _windUpActive = true;
        Transform t = GetBobTarget();
        KillPreShotTweens(false);
        _windUpActive = true;

        t.localScale = _bobBaseLocalScale;
        t.localPosition = _bobBaseLocalPos;

        Vector3 squash = new Vector3(
            _bobBaseLocalScale.x * windUpWidenXZ,
            _bobBaseLocalScale.y * windUpSquashY,
            _bobBaseLocalScale.z * windUpWidenXZ);
        Vector3 peak = _bobBaseLocalScale * shootPeakMultiplier;
        Vector3 dipPos = _bobBaseLocalPos + Vector3.down * windUpDip;
        Vector3 hopPos = _bobBaseLocalPos + Vector3.up * shootHop;

        bool fired = false;

        _preShotSeq = DOTween.Sequence()
            .SetTarget(t)
            .SetUpdate(true)
            // Slow wind-up crouch
            .Append(t.DOScale(squash, windUpDuration).SetEase(windUpEase))
            .Join(t.DOLocalMove(dipPos, windUpDuration).SetEase(windUpEase))
            .AppendCallback(() =>
            {
                fired = TryFire();
                if (!fired)
                {
                    // Aim lost during wind-up: abort the shoot kick, ease back quietly.
                    Sequence aborted = _preShotSeq;
                    _preShotSeq = null;
                    if (aborted != null && aborted.IsActive())
                        aborted.Kill(false);

                    _windUpActive = true;
                    _preShotSeq = DOTween.Sequence()
                        .SetTarget(t)
                        .SetUpdate(true)
                        .Append(t.DOScale(_bobBaseLocalScale, settleDuration).SetEase(settleEase))
                        .Join(t.DOLocalMove(_bobBaseLocalPos, settleDuration).SetEase(settleEase))
                        .OnComplete(() =>
                        {
                            _windUpActive = false;
                            _preShotSeq = null;
                            _nextFireTime = Time.time + 0.2f;
                        })
                        .OnKill(() =>
                        {
                            _windUpActive = false;
                            _preShotSeq = null;
                        });
                }
            })
            // Shoot kick only continues if the sequence wasn't killed above
            .Append(t.DOScale(peak, shootKickDuration).SetEase(shootEase))
            .Join(t.DOLocalMove(hopPos, shootKickDuration).SetEase(shootEase))
            .Append(t.DOScale(_bobBaseLocalScale, settleDuration).SetEase(settleEase))
            .Join(t.DOLocalMove(_bobBaseLocalPos, settleDuration).SetEase(settleEase))
            .OnComplete(() =>
            {
                _windUpActive = false;
                _preShotSeq = null;
                _nextFireTime = Time.time + (fired ? fireCooldown : 0.2f);
            })
            .OnKill(() =>
            {
                // Aborted dry-fire replaces _preShotSeq with a settle tween — don't wipe that.
                if (_preShotSeq == null)
                    _windUpActive = false;
            });
    }

    private bool CanFireNow()
    {
        if (_knockedOffBase)
            return false;
        if (_rb != null && !_rb.isKinematic)
            return false;
        return TryResolveAim(out _, out _, out _);
    }

    private bool TryResolveAim(out Vector3 aimDir, out Vector3 muzzlePos, out Vector3 targetPos)
    {
        aimDir = Vector3.forward;
        muzzlePos = muzzle != null ? muzzle.position : transform.position + Vector3.up * 0.5f;
        Vector3 muzzleFwd = muzzle != null ? muzzle.forward : transform.forward;
        targetPos = GetPlayerAimPoint();
        Vector3 targetVel = _carRb != null ? _carRb.velocity : Vector3.zero;

        if (!TryComputeLeadDirection(muzzlePos, targetPos, targetVel, projectileSpeed, maxShotRange, out aimDir))
        {
            float fallbackT = Mathf.Max(0.05f, extraLeadTime > 0f ? extraLeadTime : 0.35f);
            Vector3 fallback = targetPos + targetVel * fallbackT - muzzlePos;
            if (fallback.sqrMagnitude < 1e-6f) return false;
            aimDir = fallback.normalized;
        }
        else if (extraLeadTime > 0.001f)
        {
            Vector3 boosted = targetPos + targetVel * extraLeadTime - muzzlePos;
            if (boosted.sqrMagnitude > 1e-6f)
                aimDir = Vector3.Slerp(aimDir, boosted.normalized, 0.35f).normalized;
        }

        if (Vector3.Dot(muzzleFwd.normalized, aimDir) < minAimDot)
            return false;

        return true;
    }

    private bool TryFire()
    {
        if (_knockedOffBase)
            return false;
        if (_rb != null && !_rb.isKinematic)
            return false;
        if (!TryResolveAim(out Vector3 aimDir, out Vector3 muzzlePos, out Vector3 targetPos))
            return false;

        aimDir = ApplyAimInaccuracy(aimDir, muzzlePos, targetPos);

        Quaternion rot = Quaternion.LookRotation(aimDir, Vector3.up);
        GameObject projGo = Instantiate(projectilePrefab, muzzlePos, rot);

        var proj = projGo.GetComponent<SideShooterProjectile>();
        if (proj == null)
            proj = projGo.AddComponent<SideShooterProjectile>();

        proj.Init(
            projectileSpeed,
            maxShotRange,
            projectileLifetime,
            hitHpDamage,
            hitFuelPercent,
            crashFxSeverity,
            crashImpactSpeed,
            _ownColliders);

        if (verboseDebug)
        {
            Vector3 targetVel = _carRb != null ? _carRb.velocity : Vector3.zero;
            Debug.Log($"[TrackSideShooter] Fired toward {aimDir} (playerVel={targetVel.magnitude:F1})");
        }

        return true;
    }

    /// <summary>
    /// Softens perfect lead + adds a tunable miss cone. <see cref="aimAccuracy"/> 1 = unchanged lead.
    /// </summary>
    private Vector3 ApplyAimInaccuracy(Vector3 perfectAimDir, Vector3 muzzlePos, Vector3 targetPos)
    {
        float accuracy = Mathf.Clamp01(aimAccuracy);
        if (accuracy >= 0.999f)
            return perfectAimDir.normalized;

        float error01 = 1f - accuracy;

        Vector3 toCurrent = targetPos - muzzlePos;
        if (toCurrent.sqrMagnitude > 1e-6f)
        {
            float bleed = Mathf.Clamp01(leadBleedOff) * error01;
            perfectAimDir = Vector3.Slerp(perfectAimDir.normalized, toCurrent.normalized, bleed).normalized;
        }

        float cone = aimErrorMaxDegrees * error01;
        if (cone > 0.01f)
        {
            float yaw = Random.Range(-cone, cone);
            float pitch = Random.Range(-cone * 0.45f, cone * 0.45f);
            Vector3 right = Vector3.Cross(Vector3.up, perfectAimDir);
            if (right.sqrMagnitude < 1e-6f) right = Vector3.right;
            right.Normalize();
            Vector3 up = Vector3.Cross(perfectAimDir, right).normalized;
            perfectAimDir = (Quaternion.AngleAxis(yaw, up) * Quaternion.AngleAxis(pitch, right) * perfectAimDir).normalized;
        }

        return perfectAimDir;
    }

    private void CacheCar(Transform player)
    {
        _car = null;
        _carRb = null;
        _carBodyCollider = null;
        if (player == null) return;
        _car = player.GetComponentInParent<CarController>();
        if (_car != null)
        {
            _carRb = _car.GetComponent<Rigidbody>();
            // CarController's own collider is the driving body — not nested prefab roots/wheels/VFX.
            _carBodyCollider = _car.GetComponent<Collider>();
            if (_carBodyCollider != null && _carBodyCollider.isTrigger)
                _carBodyCollider = null;
        }
        if (_carRb == null)
            _carRb = player.GetComponentInParent<Rigidbody>();
        if (_carBodyCollider == null && _carRb != null)
        {
            var cols = _carRb.GetComponents<Collider>();
            for (int i = 0; i < cols.Length; i++)
            {
                if (cols[i] != null && !cols[i].isTrigger)
                {
                    _carBodyCollider = cols[i];
                    break;
                }
            }
        }
    }

    /// <summary>
    /// Aim at the car body collider / COM — player transform.position is often a nested prefab root
    /// whose "center" sits well above the actual vehicle.
    /// </summary>
    private Vector3 GetPlayerAimPoint()
    {
        if (_carBodyCollider != null)
            return _carBodyCollider.bounds.center;

        if (_carRb != null)
            return _carRb.worldCenterOfMass;

        if (_car != null)
            return _car.transform.position;

        return _player != null ? _player.position : transform.position;
    }

    private float DetermineHalfRoadWidth()
    {
        float roadWidth = overrideRoadWidth > 0f
            ? overrideRoadWidth
            : (trackGenerator != null ? trackGenerator.RoadWidth : 8f);
        return Mathf.Max(0.1f, roadWidth) * 0.5f;
    }

    private float DetermineSelfHalfWidth()
    {
        if (!autoHalfWidthFromRenderer)
            return Mathf.Max(0f, manualHalfWidth);

        float approx = 0f;
        var rends = GetComponentsInChildren<Renderer>();
        if (rends != null)
        {
            for (int i = 0; i < rends.Length; i++)
            {
                if (rends[i] == null) continue;
                Vector3 e = rends[i].bounds.extents;
                approx = Mathf.Max(approx, Mathf.Max(e.x, e.z));
            }
        }
        return approx > 0.01f ? approx : Mathf.Max(0.1f, manualHalfWidth);
    }

    private bool TryResolveTrackFrame(Vector3 worldPos, out Vector3 forward, out Vector3 lateral, out Vector3 centerOnPath)
    {
        forward = Vector3.forward;
        lateral = Vector3.right;
        centerOnPath = worldPos;

        if (trackGenerator == null || trackGenerator.PathPoints == null || trackGenerator.PathPoints.Count < 2)
            return false;

        var path = trackGenerator.PathPoints;
        float best = float.MaxValue;
        int bestIdx = 0;
        float bestT = 0f;

        for (int i = 0; i < path.Count - 1; i++)
        {
            Vector3 a = path[i];
            Vector3 b = path[i + 1];
            Vector3 ab = b - a;
            float abSqr = ab.sqrMagnitude;
            if (abSqr < 1e-6f) continue;

            float t = Mathf.Clamp01(Vector3.Dot(worldPos - a, ab) / abSqr);
            Vector3 proj = a + ab * t;
            float d = (worldPos - proj).sqrMagnitude;
            if (d < best)
            {
                best = d;
                bestIdx = i;
                bestT = t;
            }
        }

        Vector3 A = path[bestIdx];
        Vector3 B = path[bestIdx + 1];
        centerOnPath = Vector3.Lerp(A, B, bestT);
        forward = (B - A);
        forward.y = 0f;
        if (forward.sqrMagnitude < 1e-6f) forward = Vector3.forward;
        forward.Normalize();
        lateral = Vector3.Cross(Vector3.up, forward).normalized;
        return true;
    }

    /// <summary>Quadratic intercept aim (same approach as CarTurretController).</summary>
    private static bool TryComputeLeadDirection(
        Vector3 shooterPos,
        Vector3 targetPos,
        Vector3 targetVel,
        float projectileSpeed,
        float maxTravel,
        out Vector3 leadDir)
    {
        leadDir = Vector3.zero;
        if (projectileSpeed <= 0.01f) return false;

        Vector3 toTarget = targetPos - shooterPos;
        float distSqr = toTarget.sqrMagnitude;
        if (distSqr < 1e-6f) return false;

        if (targetVel.sqrMagnitude < 1e-6f)
        {
            float dist = Mathf.Sqrt(distSqr);
            if (dist > maxTravel) return false;
            leadDir = toTarget.normalized;
            return true;
        }

        Vector3 r = toTarget;
        float v2 = targetVel.sqrMagnitude;
        float s2 = projectileSpeed * projectileSpeed;
        float a = v2 - s2;
        float b = 2f * Vector3.Dot(targetVel, r);
        float c = r.sqrMagnitude;

        float t;
        if (Mathf.Abs(a) < 1e-6f)
        {
            if (Mathf.Abs(b) < 1e-6f) return false;
            t = -c / b;
        }
        else
        {
            float disc = b * b - 4f * a * c;
            if (disc < 0f) return false;
            float sqrtDisc = Mathf.Sqrt(disc);
            float t1 = (-b + sqrtDisc) / (2f * a);
            float t2 = (-b - sqrtDisc) / (2f * a);
            t = float.MaxValue;
            if (t1 > 0f) t = Mathf.Min(t, t1);
            if (t2 > 0f) t = Mathf.Min(t, t2);
            if (!float.IsFinite(t) || t == float.MaxValue) return false;
        }

        if (projectileSpeed * t > maxTravel) return false;

        Vector3 intercept = targetPos + targetVel * t;
        Vector3 toIntercept = intercept - shooterPos;
        if (toIntercept.sqrMagnitude < 1e-6f) return false;
        leadDir = toIntercept.normalized;
        return true;
    }

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(1f, 0.4f, 0.1f, 0.35f);
        Gizmos.DrawWireSphere(transform.position, engageRange);
        Gizmos.color = new Color(1f, 0.2f, 0.1f, 0.2f);
        Gizmos.DrawWireSphere(transform.position, disengageRange);

        if (muzzle != null)
        {
            Gizmos.color = Color.cyan;
            Gizmos.DrawRay(muzzle.position, muzzle.forward * 3f);
        }
    }
#endif
}
