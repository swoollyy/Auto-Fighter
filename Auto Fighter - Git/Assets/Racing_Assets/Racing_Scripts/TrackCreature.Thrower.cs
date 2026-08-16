using System.Collections;
using UnityEngine;

/// <summary>
/// Gorilla / thrower behavior: idle on hills, seek nearby environment props, lift, and throw
/// at the player's predicted track position.
/// </summary>
public partial class TrackCreature
{
    private Transform _throwerProp;
    private Rigidbody _throwerPropRb;
    private Collider[] _throwerPropColliders;
    private Vector3 _throwerIdleAnchor;
    private Vector3 _throwerIdleTarget;
    private Vector3 _throwerWorldMoveDir;
    private bool _throwerHasWorldMove;
    private float _throwerCooldownUntil;
    private float _throwerLiftTimer;
    private float _throwerIdleRetargetAt;
    private Vector3 _throwerHeldLocalPos;
    private TrackEnvironmentSpawner _throwerEnvSpawner;
    private readonly Collider[] _throwerPropBuffer = new Collider[24];

    protected void CaptureThrowerIdleAnchor()
    {
        _throwerIdleAnchor = transform.position;
        _throwerIdleTarget = transform.position;
        _throwerHasWorldMove = false;
        _throwerIdleRetargetAt = 0f;
        PickNewThrowerIdleTarget();
    }

    protected void ReleaseHeldThrowerProp()
    {
        if (_throwerProp == null)
            return;

        RestoreThrowerPropColliders();
        _throwerProp.SetParent(null, true);

        if (_throwerPropRb != null)
        {
            _throwerPropRb.isKinematic = false;
            _throwerPropRb.useGravity = true;
        }

        ClearThrowerPropRefs();
    }

    protected void UpdateThrowerBehavior(float dt)
    {
        if (config == null || currentState == CreatureState.Dead)
            return;

        if (_throwerCooldownUntil > Time.time &&
            (currentState == CreatureState.Idle || currentState == CreatureState.Wandering))
        {
            UpdateThrowerIdle(dt);
            return;
        }

        switch (currentState)
        {
            case CreatureState.Idle:
            case CreatureState.Wandering:
                UpdateThrowerIdle(dt);
                if (TryBeginThrowerApproach())
                    SetState(CreatureState.ApproachingProp);
                break;

            case CreatureState.ApproachingProp:
                UpdateThrowerApproach(dt);
                break;

            case CreatureState.Lifting:
                UpdateThrowerLift(dt);
                break;

            case CreatureState.Throwing:
                ExecuteThrowerThrow();
                SetState(CreatureState.Idle);
                break;
        }
    }

    protected void UpdateThrowerWorldMovement(float dt)
    {
        Vector3 prevPos = transform.position;
        float moveSpeed = Mathf.Max(currentSpeed, 0f);
        float step = moveSpeed * dt;

        Vector3 moveDir = Vector3.zero;
        float desiredDist = 0f;

        if (currentState == CreatureState.Lifting || currentState == CreatureState.Throwing)
        {
            moveDir = Vector3.zero;
        }
        else if (_throwerHasWorldMove && _throwerWorldMoveDir.sqrMagnitude > 1e-6f)
        {
            moveDir = _throwerWorldMoveDir;
            moveDir.y = 0f;
            if (moveDir.sqrMagnitude > 1e-6f)
                moveDir.Normalize();
            desiredDist = step;
        }

        if (enableMovementAvoidance && GetAvoidanceLayerMask().value != 0 && moveDir.sqrMagnitude > 0.0001f)
        {
            spawner.SamplePath(currentDistanceAlongTrack, out _, out Vector3 pathForward);
            Vector3 flatForward = pathForward;
            flatForward.y = 0f;
            if (flatForward.sqrMagnitude < 0.0001f)
                flatForward = Vector3.forward;
            flatForward.Normalize();
            Vector3 right = Vector3.Cross(Vector3.up, flatForward).normalized;
            moveDir = ApplyAvoidanceToMoveDir(moveDir, step, flatForward, right);
        }

        Vector3 newPos = transform.position + moveDir * Mathf.Min(step, desiredDist > 0f ? desiredDist : step);
        newPos.y = transform.position.y;

        if (enableMovementAvoidance && GetAvoidanceLayerMask().value != 0)
            ClampHorizontalMoveToObstacles(prevPos, ref newPos, out _);

        transform.position = newPos;

        Vector3 delta = transform.position - prevPos;
        delta.y = 0f;
        float planarMoved = delta.magnitude;
        if (planarMoved > Mathf.Max(0.02f, step * 0.15f) && moveDir.sqrMagnitude > IntendedDirMinSqr)
        {
            _intendedPlanarMoveDir = moveDir;
            _intendedPlanarMoveDir.y = 0f;
            if (_intendedPlanarMoveDir.sqrMagnitude > IntendedDirMinSqr)
            {
                _intendedPlanarMoveDir.Normalize();
                _hasIntendedPlanarMoveDir = true;
                _lastStableFacingDir = _intendedPlanarMoveDir;
            }
        }

        currentVelocity = (transform.position - prevPos) / Mathf.Max(dt, 0.001f);
        SyncTrackStateFromTransform();
    }

    private void UpdateThrowerIdle(float dt)
    {
        if (!IdleRhythmAllowsWalking())
        {
            currentSpeed = Mathf.MoveTowards(currentSpeed, 0f, Mathf.Max(0.01f, config.throwerIdleMoveSpeed) * 4f * dt);
            _throwerHasWorldMove = false;
            return;
        }

        Vector3 toTarget = _throwerIdleTarget - transform.position;
        toTarget.y = 0f;
        float dist = toTarget.magnitude;
        float arrive = Mathf.Max(0.4f, config.idleMinTravelDistance * 0.15f);

        if (dist <= arrive)
        {
            currentSpeed = Mathf.MoveTowards(currentSpeed, 0f, Mathf.Max(0.01f, config.throwerIdleMoveSpeed) * 6f * dt);
            _throwerHasWorldMove = false;
            if (Time.time >= _throwerIdleRetargetAt)
                PickNewThrowerIdleTarget();
            return;
        }

        currentSpeed = Mathf.MoveTowards(currentSpeed, config.throwerIdleMoveSpeed, Mathf.Max(0.01f, config.throwerIdleMoveSpeed) * 3f * dt);
        _throwerWorldMoveDir = toTarget / Mathf.Max(0.001f, dist);
        _throwerHasWorldMove = true;
    }

    private void PickNewThrowerIdleTarget()
    {
        float radius = Mathf.Max(1f, config.throwerIdleWanderRadius);
        float minMove = Mathf.Min(radius * 0.85f, Mathf.Max(0.75f, config.idleMinTravelDistance));

        for (int i = 0; i < 8; i++)
        {
            Vector2 offset = Random.insideUnitCircle * radius;
            Vector3 candidate = _throwerIdleAnchor + new Vector3(offset.x, 0f, offset.y);
            Vector3 planar = candidate - transform.position;
            planar.y = 0f;
            if (planar.magnitude < minMove)
                continue;

            _throwerIdleTarget = candidate;
            _throwerIdleRetargetAt = Time.time + Mathf.Max(0.2f, config.throwerIdleDirectionChangeInterval);
            return;
        }

        Vector2 fallback = Random.insideUnitCircle.normalized * minMove;
        _throwerIdleTarget = transform.position + new Vector3(fallback.x, 0f, fallback.y);
        _throwerIdleRetargetAt = Time.time + Mathf.Max(0.2f, config.throwerIdleDirectionChangeInterval);
    }

    private bool TryBeginThrowerApproach()
    {
        if (playerTransform == null)
            return false;
        if (playerDistance > config.throwerMaxPlayerRange)
            return false;
        if (!TryFindNearestThrowableProp(config.throwerObstacleSeekRadius, out Transform prop, out float dist))
            return false;
        if (dist > config.throwerApproachTriggerRange)
            return false;

        _throwerProp = prop;
        _throwerPropRb = prop.GetComponent<Rigidbody>();
        if (_throwerPropRb == null)
            _throwerPropRb = prop.GetComponentInChildren<Rigidbody>();
        return true;
    }

    private void UpdateThrowerApproach(float dt)
    {
        if (!IsThrowerPropValid(_throwerProp))
        {
            ClearThrowerPropRefs();
            SetState(CreatureState.Idle);
            return;
        }

        if (playerTransform != null && playerDistance > config.throwerMaxPlayerRange * 1.25f)
        {
            ClearThrowerPropRefs();
            SetState(CreatureState.Idle);
            return;
        }

        Vector3 toProp = GetThrowerPropApproachPoint(_throwerProp) - transform.position;
        toProp.y = 0f;
        float dist = toProp.magnitude;

        if (dist <= config.throwerGrabRange)
        {
            if (TryBeginThrowerLift())
                SetState(CreatureState.Lifting);
            else
            {
                ClearThrowerPropRefs();
                SetState(CreatureState.Idle);
            }
            return;
        }

        currentSpeed = config.throwerRunToObstacleSpeed;
        _throwerWorldMoveDir = toProp / Mathf.Max(0.001f, dist);
        _throwerHasWorldMove = true;
    }

    private bool TryBeginThrowerLift()
    {
        if (!IsThrowerPropValid(_throwerProp))
            return false;

        if (_throwerEnvSpawner == null)
            _throwerEnvSpawner = FindObjectOfType<TrackEnvironmentSpawner>();
        if (_throwerEnvSpawner != null)
            _throwerEnvSpawner.TryClaimInstance(_throwerProp.gameObject);

        CacheAndDisableThrowerPropColliders();

        if (_throwerPropRb != null)
        {
            _throwerPropRb.isKinematic = true;
            _throwerPropRb.useGravity = false;
            _throwerPropRb.velocity = Vector3.zero;
            _throwerPropRb.angularVelocity = Vector3.zero;
        }

        Transform hold = visualRoot != null ? visualRoot : transform;
        _throwerProp.SetParent(hold, true);
        _throwerHeldLocalPos = Vector3.up * Mathf.Max(0.4f, config.throwerLiftHeight);
        _throwerLiftTimer = 0f;
        currentSpeed = 0f;
        _throwerHasWorldMove = false;
        return true;
    }

    private void UpdateThrowerLift(float dt)
    {
        if (!IsThrowerPropValid(_throwerProp))
        {
            ClearThrowerPropRefs();
            SetState(CreatureState.Idle);
            return;
        }

        _throwerLiftTimer += dt;
        float t = config.throwerLiftDuration > 0.01f
            ? Mathf.Clamp01(_throwerLiftTimer / config.throwerLiftDuration)
            : 1f;

        _throwerProp.localPosition = Vector3.Lerp(_throwerProp.localPosition, _throwerHeldLocalPos, 1f - Mathf.Exp(-12f * dt));

        if (playerTransform != null)
        {
            Vector3 toPlayer = playerTransform.position - transform.position;
            toPlayer.y = 0f;
            if (toPlayer.sqrMagnitude > 0.01f)
            {
                _intendedPlanarMoveDir = toPlayer.normalized;
                _hasIntendedPlanarMoveDir = true;
                _lastStableFacingDir = _intendedPlanarMoveDir;
            }
        }

        if (t >= 1f)
            SetState(CreatureState.Throwing);
    }

    private void ExecuteThrowerThrow()
    {
        if (!IsThrowerPropValid(_throwerProp))
        {
            ClearThrowerPropRefs();
            _throwerCooldownUntil = Time.time + Mathf.Max(0.1f, config.throwerThrowCooldown);
            return;
        }

        Vector3 origin = _throwerProp.position;
        PredictThrowerImpact(origin, out Vector3 impact);
        float flightTime = EstimateThrowerFlightTime(origin, impact);
        float telegraphRadius = EstimateThrowerTelegraphRadius(_throwerProp);
        SpawnThrowerLandingTelegraph(impact, telegraphRadius, flightTime);

        _throwerProp.SetParent(null, true);
        RestoreThrowerPropColliders();

        Vector3 velocity = ComputeThrowerBallisticVelocity(origin, impact);

        var thrown = _throwerProp.GetComponent<GorillaThrownProp>();
        if (thrown == null)
            thrown = _throwerProp.gameObject.AddComponent<GorillaThrownProp>();

        thrown.Launch(
            velocity,
            config.throwerThrowSpin,
            config.throwerThrownPropLifetime,
            config.throwerThrownPropLayer,
            config.throwerThrownCrashSeverity,
            config.throwerThrownKnockbackForce,
            config.throwerThrownLift,
            config.throwerThrownTorque);

        ClearThrowerPropRefs();
        _throwerCooldownUntil = Time.time + Mathf.Max(0.1f, config.throwerThrowCooldown);
        currentSpeed = 0f;
        _throwerHasWorldMove = false;
    }

    private const float ThrowerMaxLaneFraction = 0.95f;

    private void PredictThrowerImpact(Vector3 origin, out Vector3 impact)
    {
        impact = playerTransform != null ? playerTransform.position : origin + transform.forward * 10f;

        if (playerTransform == null || spawner == null || spawner.GetTotalLength() <= 0.01f)
        {
            impact = ProjectThrowerLandingToRoad(impact);
            return;
        }

        float total = spawner.GetTotalLength();
        float carDist = Mathf.Clamp(spawner.GetDistanceAlongPath(playerTransform.position), 0f, total);
        float flightTime = EstimateThrowerFlightTime(origin, playerTransform.position);
        float carSpeed = GetThrowerCarSpeedAlongTrack(carDist);

        impact = BuildOnTrackImpact(carDist, carSpeed, flightTime);

        float refinedTime = EstimateThrowerFlightTime(origin, impact);
        if (!Mathf.Approximately(refinedTime, flightTime))
            impact = BuildOnTrackImpact(carDist, carSpeed, refinedTime);
    }

    private Vector3 BuildOnTrackImpact(float carDist, float carSpeed, float flightTime)
    {
        float total = spawner.GetTotalLength();
        float leadScale = config != null ? Mathf.Clamp01(config.throwerPredictionStrength) : 1f;
        float predictedDist = carDist + Mathf.Max(0f, carSpeed) * flightTime * leadScale;
        predictedDist = Mathf.Clamp(predictedDist, 0f, Mathf.Max(0f, total - 0.25f));

        spawner.SamplePath(carDist, out Vector3 pathNow, out Vector3 fwdNow);
        fwdNow.y = 0f;
        if (fwdNow.sqrMagnitude < 1e-6f)
            fwdNow = playerTransform != null ? playerTransform.forward : Vector3.forward;
        fwdNow.y = 0f;
        if (fwdNow.sqrMagnitude < 1e-6f)
            fwdNow = Vector3.forward;
        fwdNow.Normalize();
        Vector3 rightNow = Vector3.Cross(Vector3.up, fwdNow).normalized;

        float halfWidth = Mathf.Max(0.1f, GetRoadHalfWidth());
        Vector3 carPos = playerTransform != null ? playerTransform.position : pathNow;
        float laneFraction = Vector3.Dot(carPos - pathNow, rightNow) / halfWidth;
        laneFraction = Mathf.Clamp(laneFraction, -ThrowerMaxLaneFraction, ThrowerMaxLaneFraction);

        spawner.SamplePath(predictedDist, out Vector3 pathPos, out Vector3 pathFwd);
        pathFwd.y = 0f;
        if (pathFwd.sqrMagnitude < 1e-6f)
            pathFwd = fwdNow;
        pathFwd.Normalize();
        Vector3 pathRight = Vector3.Cross(Vector3.up, pathFwd).normalized;

        return SnapThrowerImpactOntoRoad(pathPos, pathRight, laneFraction * halfWidth);
    }

    private float GetThrowerCarSpeedAlongTrack(float carDist)
    {
        if (playerTransform == null)
            return 0f;

        var carRb = playerTransform.GetComponent<Rigidbody>();
        if (carRb == null)
            return 0f;

        spawner.SamplePath(carDist, out _, out Vector3 pathFwd);
        pathFwd.y = 0f;
        if (pathFwd.sqrMagnitude > 1e-6f)
        {
            pathFwd.Normalize();
            float along = Vector3.Dot(carRb.velocity, pathFwd);
            if (along > 0.25f)
                return along;
        }

        return Mathf.Max(0f, carRb.velocity.magnitude);
    }

    private Vector3 ComputeThrowerBallisticVelocity(Vector3 origin, Vector3 impact)
    {
        Vector3 toTarget = impact - origin;
        float flightTime = EstimateThrowerFlightTime(origin, impact);

        Vector3 vel = toTarget / flightTime;
        vel.y = (toTarget.y / flightTime) + 0.5f * Mathf.Abs(Physics.gravity.y) * flightTime;
        vel.y += config.throwerThrowArcHeight / Mathf.Max(0.2f, flightTime);
        return vel;
    }

    private float EstimateThrowerFlightTime(Vector3 origin, Vector3 impact)
    {
        float horiz = HorizontalDistanceXZ(origin, impact);
        float speed = Mathf.Max(0.1f, config.throwerThrowSpeed);
        return Mathf.Clamp(horiz / speed, 0.2f, 4.5f);
    }

    private static float EstimateThrowerTelegraphRadius(Transform prop)
    {
        float r = 1.5f;
        if (prop == null)
            return r;

        var sc = prop.GetComponentInChildren<SphereCollider>(true);
        if (sc != null)
        {
            r = sc.radius * Mathf.Max(prop.lossyScale.x, prop.lossyScale.z);
        }
        else
        {
            var col = prop.GetComponentInChildren<Collider>(true);
            if (col != null)
                r = Mathf.Max(col.bounds.extents.x, col.bounds.extents.z);
            else
            {
                var rend = prop.GetComponentInChildren<Renderer>(true);
                if (rend != null)
                    r = Mathf.Max(rend.bounds.extents.x, rend.bounds.extents.z);
            }
        }

        return Mathf.Clamp(r, 0.75f, 4.0f);
    }

    private Vector3 SnapThrowerImpactOntoRoad(Vector3 pathPos, Vector3 pathRight, float lateral)
    {
        Vector3 candidate = pathPos + pathRight * lateral;
        if (TryRaycastRoadSurface(candidate, out Vector3 hit))
            return hit;

        for (int i = 1; i <= 4; i++)
        {
            float t = 1f - i * 0.2f;
            candidate = pathPos + pathRight * (lateral * t);
            if (TryRaycastRoadSurface(candidate, out hit))
                return hit;
        }

        if (TryRaycastRoadSurface(pathPos, out hit))
            return hit;

        return pathPos;
    }

    private Vector3 ProjectThrowerLandingToRoad(Vector3 worldPos)
    {
        if (TryRaycastRoadSurface(worldPos, out Vector3 hit))
            return hit;
        return worldPos;
    }

    private static bool TryRaycastRoadSurface(Vector3 worldPos, out Vector3 roadPoint)
    {
        roadPoint = worldPos;
        LayerMask roadMask = LayerMask.GetMask("RoadSurface", "Road");
        if (roadMask.value == 0)
            roadMask = LayerMask.GetMask("RoadSurface");

        const float up = 12f;
        const float down = 50f;
        Vector3 origin = worldPos + Vector3.up * up;
        if (Physics.Raycast(origin, Vector3.down, out RaycastHit hit, up + down, roadMask, QueryTriggerInteraction.Ignore))
        {
            roadPoint = hit.point;
            return true;
        }

        return false;
    }

    private void SpawnThrowerLandingTelegraph(Vector3 impactPos, float telegraphRadius, float flightTime)
    {
        GameObject prefab = config != null ? config.throwerLandingTelegraphPrefab : null;
        if (prefab == null)
            return;

        impactPos = ProjectThrowerLandingToRoad(impactPos);
        float holdSeconds = Mathf.Max(0.05f, flightTime - 0.05f);

        GameObject tele = null;
        bool pooled = ProjectilePool.Instance != null;
        if (pooled)
            tele = ProjectilePool.Instance.Get(prefab);
        else
            tele = Instantiate(prefab);

        if (tele == null)
            return;

        tele.transform.position = impactPos;
        tele.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
        tele.SetActive(true);

        var decalTele = tele.GetComponent<URPDecalTelegraph>();
        if (decalTele != null)
        {
            decalTele.SetWorldPose(impactPos);
            decalTele.Play(
                radius: telegraphRadius,
                seconds: holdSeconds,
                onComplete: () => ReturnThrowerTelegraph(prefab, tele, pooled));
            return;
        }

        var gr = tele.GetComponent<GroundRing>();
        if (gr != null)
        {
            gr.Play(
                telegraphRadius,
                onComplete: () => ReturnThrowerTelegraph(prefab, tele, pooled),
                holdOverride: holdSeconds);
            return;
        }

        StartCoroutine(ReturnThrowerTelegraphLater(prefab, tele, Mathf.Max(0.1f, holdSeconds), pooled));
    }

    private static void ReturnThrowerTelegraph(GameObject prefab, GameObject tele, bool pooled)
    {
        if (tele == null)
            return;

        if (pooled && prefab != null && ProjectilePool.Instance != null)
            ProjectilePool.Instance.Return(prefab, tele);
        else
            Destroy(tele);
    }

    private IEnumerator ReturnThrowerTelegraphLater(GameObject prefab, GameObject tele, float delay, bool pooled)
    {
        yield return new WaitForSeconds(delay);
        ReturnThrowerTelegraph(prefab, tele, pooled);
    }

    private bool TryFindNearestThrowableProp(float radius, out Transform prop, out float distance)
    {
        prop = null;
        distance = float.MaxValue;

        LayerMask mask = config.throwerObstacleLayers.value != 0
            ? config.throwerObstacleLayers
            : (LayerMask)(1 << 20);

        int hits = Physics.OverlapSphereNonAlloc(
            transform.position,
            Mathf.Max(0.5f, radius),
            _throwerPropBuffer,
            mask,
            QueryTriggerInteraction.Collide);

        float bestSqr = float.MaxValue;
        Transform best = null;

        for (int i = 0; i < hits; i++)
        {
            Collider col = _throwerPropBuffer[i];
            if (col == null || IsOwnObstacleCollider(col))
                continue;
            if (!IsThrowableEnvironmentProp(col, out Transform root))
                continue;

            Vector3 to = GetThrowerPropApproachPoint(root) - transform.position;
            to.y = 0f;
            float sqr = to.sqrMagnitude;
            if (sqr < bestSqr)
            {
                bestSqr = sqr;
                best = root;
            }
        }

        if (best == null)
            return false;

        prop = best;
        distance = Mathf.Sqrt(bestSqr);
        return true;
    }

    private static bool IsThrowableEnvironmentProp(Collider col, out Transform root)
    {
        root = null;
        if (col == null)
            return false;

        if (col.GetComponentInParent<TrackCreature>() != null)
            return false;
        if (col.GetComponentInParent<GorillaThrownProp>() != null)
            return false;
        if (col.GetComponentInParent<ThrownObstacle>() != null)
            return false;
        if (col.GetComponentInParent<NPCTrafficCar>() != null)
            return false;

        var obstacle = col.GetComponentInParent<RacingObstacle>();
        if (obstacle != null)
        {
            root = obstacle.transform;
            return true;
        }

        root = col.attachedRigidbody != null ? col.attachedRigidbody.transform : col.transform;
        return root != null;
    }

    private static Vector3 GetThrowerPropApproachPoint(Transform prop)
    {
        if (prop == null)
            return Vector3.zero;

        var col = prop.GetComponentInChildren<Collider>();
        if (col != null)
        {
            Vector3 p = col.bounds.center;
            p.y = prop.position.y;
            return p;
        }

        return prop.position;
    }

    private static bool IsThrowerPropValid(Transform prop)
    {
        return prop != null && prop.gameObject.activeInHierarchy;
    }

    private void CacheAndDisableThrowerPropColliders()
    {
        if (_throwerProp == null)
            return;

        _throwerPropColliders = _throwerProp.GetComponentsInChildren<Collider>(true);
        for (int i = 0; i < _throwerPropColliders.Length; i++)
        {
            if (_throwerPropColliders[i] == null)
                continue;
            _throwerPropColliders[i].enabled = false;
        }
    }

    private void RestoreThrowerPropColliders()
    {
        if (_throwerPropColliders == null)
            return;

        for (int i = 0; i < _throwerPropColliders.Length; i++)
        {
            if (_throwerPropColliders[i] == null)
                continue;
            _throwerPropColliders[i].enabled = true;
            _throwerPropColliders[i].isTrigger = false;
        }
    }

    private void ClearThrowerPropRefs()
    {
        _throwerProp = null;
        _throwerPropRb = null;
        _throwerPropColliders = null;
        _throwerHasWorldMove = false;
    }

    private static float HorizontalDistanceXZ(Vector3 a, Vector3 b)
    {
        a.y = 0f;
        b.y = 0f;
        return Vector3.Distance(a, b);
    }
}
