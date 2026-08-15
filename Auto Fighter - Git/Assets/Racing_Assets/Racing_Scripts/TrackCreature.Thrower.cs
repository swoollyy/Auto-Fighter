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

    private void PredictThrowerImpact(Vector3 origin, out Vector3 impact)
    {
        impact = playerTransform != null ? playerTransform.position : origin + transform.forward * 10f;

        if (playerTransform == null || spawner == null)
            return;

        float horiz = HorizontalDistanceXZ(origin, playerTransform.position);
        float speed = Mathf.Max(0.1f, config.throwerThrowSpeed);
        float flightTime = Mathf.Clamp(horiz / speed, 0.2f, 4.5f);

        float carDist = spawner.GetDistanceAlongPath(playerTransform.position);
        float carSpeed = 0f;
        var carRb = playerTransform.GetComponent<Rigidbody>();
        if (carRb != null)
        {
            Vector3 fwd = playerTransform.forward;
            fwd.y = 0f;
            if (fwd.sqrMagnitude > 1e-6f)
            {
                fwd.Normalize();
                carSpeed = Mathf.Max(0f, Vector3.Dot(carRb.velocity, fwd));
            }
            if (carSpeed < 0.25f)
                carSpeed = Mathf.Max(0f, carRb.velocity.magnitude);
        }

        float predictedDist = carDist + carSpeed * flightTime;
        predictedDist = Mathf.Clamp(predictedDist, 0f, spawner.GetTotalLength());

        Vector3 predicted = playerTransform.position;
        if (spawner.GetTotalLength() > 0f)
        {
            spawner.SamplePath(predictedDist, out Vector3 pathPos, out Vector3 pathFwd);
            pathFwd.y = 0f;
            if (pathFwd.sqrMagnitude < 1e-6f)
                pathFwd = playerTransform.forward;
            pathFwd.Normalize();
            Vector3 pathRight = Vector3.Cross(Vector3.up, pathFwd).normalized;

            float lateral = 0f;
            spawner.SamplePath(carDist, out Vector3 pathNow, out Vector3 fwdNow);
            fwdNow.y = 0f;
            if (fwdNow.sqrMagnitude > 1e-6f)
            {
                fwdNow.Normalize();
                Vector3 rightNow = Vector3.Cross(Vector3.up, fwdNow).normalized;
                lateral = Vector3.Dot(playerTransform.position - pathNow, rightNow);
            }

            predicted = pathPos + pathRight * lateral;
            if (carRb != null && carRb.velocity.sqrMagnitude > 0.25f)
            {
                Vector3 velPred = playerTransform.position + carRb.velocity * flightTime;
                predicted = Vector3.Lerp(predicted, velPred, 0.25f);
            }
        }

        impact = Vector3.Lerp(playerTransform.position, predicted, Mathf.Clamp01(config.throwerPredictionStrength));
    }

    private Vector3 ComputeThrowerBallisticVelocity(Vector3 origin, Vector3 impact)
    {
        Vector3 toTarget = impact - origin;
        float horiz = new Vector3(toTarget.x, 0f, toTarget.z).magnitude;
        float speed = Mathf.Max(0.1f, config.throwerThrowSpeed);
        float flightTime = Mathf.Clamp(horiz / speed, 0.2f, 4.5f);

        Vector3 vel = toTarget / flightTime;
        vel.y = (toTarget.y / flightTime) + 0.5f * Mathf.Abs(Physics.gravity.y) * flightTime;
        vel.y += config.throwerThrowArcHeight / Mathf.Max(0.2f, flightTime);
        return vel;
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
