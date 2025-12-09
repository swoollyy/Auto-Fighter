using System;
using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class CrossObstacleDirector : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private CarController car;
    [SerializeField] private TrackDistanceMeter distanceMeter;
    [SerializeField] private ProceduralTrackGenerator trackGenerator;
    [SerializeField] private GameObject crossObstaclePrefab;

    [Header("Spawn Control")]
    [SerializeField] private bool enabledSpawning = true;
    [SerializeField] private float minPlayerSpeed = 4f;
    [SerializeField] private float spawnCooldownSeconds = 5f;
    [SerializeField] private float minLeadDistance = 15f;
    [Tooltip("LEGACY: No longer used to block spawning near end of track. You can remove this safely.")]
    [SerializeField] private float maxLeadDistance = 80f; // legacy � kept for serialized data
    [SerializeField] private float maxCurvatureHorizonScale = 0.65f;

    [Header("Cross Speed")]
    [SerializeField] private Vector2 crossSpeedRange = new Vector2(5f, 11f);
    [Tooltip("Curve (x=normalized distance) scaling cross speed (multiplier).")]
    [SerializeField] private AnimationCurve crossSpeedMultiplierCurve = AnimationCurve.Linear(0, 1, 1, 1);

    [Header("Size Scaling")]
    [SerializeField] private Vector2 obstacleScaleRange = new Vector2(0.8f, 1.35f);
    [SerializeField] private AnimationCurve sizeCurve = AnimationCurve.Linear(0, 1, 1, 1);

    [Header("Yaw / Accuracy")]
    [SerializeField] private AnimationCurve yawErrorDegreesCurve = AnimationCurve.Linear(0, 22, 1, 4);
    [SerializeField] private AnimationCurve accuracyCurve = AnimationCurve.Linear(0, 0.6f, 1, 0.05f);

    [Header("Spawn Interval Scaling")]
    [SerializeField] private AnimationCurve spawnIntervalCurve = AnimationCurve.Linear(0, 1f, 1, 0.4f);

    [Header("Spawn Cooldown Randomness")]
    [Tooltip("Random multiplier applied to the spawn cooldown each time an obstacle is spawned.")]
    [SerializeField] private Vector2 spawnCooldownRandomRange = new Vector2(0.8f, 1.2f);

    [Header("Curvature Sampling")]
    [SerializeField] private float curvatureSampleLength = 12f;
    [SerializeField] private float highCurvatureThreshold = 0.35f;

    [Header("Yaw Impact (Weighted Miss Variety)")]
    [SerializeField] private bool enableYawWeightedMisses = true;
    [SerializeField] private float yawSpeedImpactMax = 0.4f;
    [SerializeField] private float yawDistanceImpactMax = 12f;
    [SerializeField] private float yawAngleAmplifyMax = 0.6f;

    [Header("Debug")]
    [SerializeField] private bool debugGizmos = false;
    [SerializeField] private bool verboseLog = false;

    private float _cooldownRemain;
    private float _trackTotalLength;
    private List<Vector3> _path = new();
    private float[] _cumDistances;
    private float _smoothedSpeed;
    private float _nextCarSearchTime;
    private const float CarSearchInterval = 0.5f;

    private Vector3 _lastDebugStart;
    private Vector3 _lastDebugEnd;
    private bool _hasDebugPath;

    void Awake()
    {
        if (!trackGenerator) trackGenerator = FindObjectOfType<ProceduralTrackGenerator>();
        if (car) distanceMeter ??= FindObjectOfType<TrackDistanceMeter>();
        if (!distanceMeter) distanceMeter = FindObjectOfType<TrackDistanceMeter>();
    }

    void OnEnable()
    {
        if (trackGenerator)
        {
            trackGenerator.OnTrackGeneratedSuccessfully -= HandleTrackGenerated;
            trackGenerator.OnTrackGeneratedSuccessfully += HandleTrackGenerated;
        }
        RebuildSplineCache();
    }

    void OnDisable()
    {
        if (trackGenerator)
            trackGenerator.OnTrackGeneratedSuccessfully -= HandleTrackGenerated;
    }

    private void HandleTrackGenerated(ProceduralTrackGenerator gen) => RebuildSplineCache();

    public void SetCar(CarController c) => car = c;

    private void LateBindCarIfNeeded()
    {
        if (car != null) return;
        if (Time.time < _nextCarSearchTime) return;
        _nextCarSearchTime = Time.time + CarSearchInterval;

        var gm = GameManager_Racing.Instance;
        if (gm != null)
        {
            var active = gm.GetType().GetProperty("ActiveCar")?.GetValue(gm) as CarController;
            if (active)
            {
                car = active;
                if (verboseLog) Debug.Log("[CrossObstacleDirector] Bound car from GameManager_Racing.");
                return;
            }
        }

        var found = FindObjectOfType<CarController>();
        if (found)
        {
            car = found;
            if (verboseLog) Debug.Log("[CrossObstacleDirector] Found car via scene search.");
        }
    }

    void Update()
    {
        LateBindCarIfNeeded();
        if (!enabledSpawning || !ValidSetup()) return;

        float rawSpeed = car.CurrentSpeed;
        float smoothFactor = 1f - Mathf.Exp(-Time.deltaTime * 6f);
        _smoothedSpeed = Mathf.Lerp(_smoothedSpeed, rawSpeed, smoothFactor);

        _cooldownRemain -= Time.deltaTime;
        if (_cooldownRemain <= 0f) TrySpawnPredictive();
    }

    private bool ValidSetup()
    {
        return car && distanceMeter && trackGenerator && crossObstaclePrefab &&
               _path.Count >= 2 && _trackTotalLength > 0.1f;
    }

    private void RebuildSplineCache()
    {
        _path.Clear();
        if (!trackGenerator || trackGenerator.PathPoints == null) return;
        _path.AddRange(trackGenerator.PathPoints);
        if (_path.Count < 2) return;

        _cumDistances = new float[_path.Count];
        _cumDistances[0] = 0f;
        float accum = 0f;
        for (int i = 1; i < _path.Count; i++)
        {
            accum += Vector3.Distance(_path[i - 1], _path[i]);
            _cumDistances[i] = accum;
        }
        _trackTotalLength = accum;
    }

    private void TrySpawnPredictive()
    {
        if (_smoothedSpeed < minPlayerSpeed) return;

        float sCar = distanceMeter.DistanceAlongTrack;
        float distanceNorm = Mathf.Clamp01(_trackTotalLength > 0f ? sCar / _trackTotalLength : 0f);

        float intervalScale = Mathf.Clamp(spawnIntervalCurve.Evaluate(distanceNorm), 0.05f, 10f);

        // NEW: randomize cooldown so spawns aren�t perfectly periodic
        float cooldownJitter = 1f;
        if (spawnCooldownRandomRange.x != 1f || spawnCooldownRandomRange.y != 1f)
        {
            cooldownJitter = UnityEngine.Random.Range(
                Mathf.Min(spawnCooldownRandomRange.x, spawnCooldownRandomRange.y),
                Mathf.Max(spawnCooldownRandomRange.x, spawnCooldownRandomRange.y)
            );
        }

        float effectiveCooldown = spawnCooldownSeconds * intervalScale;

        float curvatureFactor = SampleCurvature(sCar, curvatureSampleLength);
        bool highCurvature = curvatureFactor > highCurvatureThreshold;

        float baseCrossSpeed = UnityEngine.Random.Range(crossSpeedRange.x, crossSpeedRange.y);
        float speedMul = Mathf.Max(0.1f, crossSpeedMultiplierCurve.Evaluate(distanceNorm));
        float crossSpeed = baseCrossSpeed * speedMul;

        float halfRoad = trackGenerator.RoadWidth * 0.5f;
        float traverseLength = 2f * halfRoad + 2f * 0.75f;
        float tCross = traverseLength / Mathf.Max(0.5f, crossSpeed);
        float tCenter = tCross * 0.5f;
        if (highCurvature)
            tCenter *= Mathf.Clamp(maxCurvatureHorizonScale, 0.25f, 1f);

        float remaining = _trackTotalLength - sCar;
        if (remaining <= minLeadDistance) { _cooldownRemain = effectiveCooldown; return; }

        float predictedLeadDistance = _smoothedSpeed * tCenter;

        float safety = 1.5f;
        float maxAllowedLead = Mathf.Max(minLeadDistance, remaining - safety);
        predictedLeadDistance = Mathf.Clamp(predictedLeadDistance, minLeadDistance, maxAllowedLead);

        float sIntercept = sCar + predictedLeadDistance;
        if (sIntercept >= _trackTotalLength - 0.25f)
            sIntercept = _trackTotalLength - 0.25f;

        SampleSpline(sIntercept, out Vector3 posIntercept, out Vector3 tanIntercept);

        float accuracyErr = Mathf.Clamp01(accuracyCurve.Evaluate(distanceNorm));
        float paramError = predictedLeadDistance * accuracyErr * UnityEngine.Random.Range(-1f, 1f);

        float sSpawn = sCar + Mathf.Clamp(predictedLeadDistance + paramError, minLeadDistance, maxAllowedLead);
        if (sSpawn >= _trackTotalLength - 0.25f) sSpawn = _trackTotalLength - 0.25f;

        float yawErrorDeg = yawErrorDegreesCurve.Evaluate(distanceNorm);
        float appliedYaw = UnityEngine.Random.Range(-yawErrorDeg, yawErrorDeg);

        if (enableYawWeightedMisses && yawErrorDeg > 0.0001f)
        {
            float rA = UnityEngine.Random.value;
            float rB = UnityEngine.Random.value;
            float rC = UnityEngine.Random.value;
            float sum = rA + rB + rC;
            float wSpeed = rA / sum;
            float wAngle = rB / sum;
            float wDistance = rC / sum;

            float yawNorm = Mathf.Clamp01(Mathf.Abs(appliedYaw) / yawErrorDeg);

            float speedFactor = 1f - wSpeed * yawNorm * Mathf.Clamp01(yawSpeedImpactMax);
            crossSpeed *= Mathf.Clamp(speedFactor, 0.25f, 1f);

            float distanceOffset = wDistance * yawNorm * Mathf.Max(0f, yawDistanceImpactMax);
            sSpawn += distanceOffset;
            sSpawn = Mathf.Clamp(sSpawn, sCar + minLeadDistance, sCar + maxAllowedLead);

            appliedYaw *= 1f + wAngle * yawNorm * Mathf.Clamp01(yawAngleAmplifyMax);
        }

        // Center point of the cross (where it tries to hit car)
        SampleSpline(sSpawn, out Vector3 spawnSplinePos, out Vector3 spawnTan);
        LayerMask roadMask = LayerMask.GetMask("RoadSurface");
        float upOffsetForCast = 10f;
        float maxDown = 50f;

        Vector3 spawnSurface = SpawnUtils.ProjectOntoSurface(
            spawnSplinePos + Vector3.up * upOffsetForCast,
            out Vector3 spawnNormal,
            upOffsetForCast,
            maxDown,
            roadMask
        );

        if (Mathf.Approximately(spawnSurface.y, spawnSplinePos.y))
            spawnSurface = SpawnUtils.ProjectOntoSurface(
                spawnSplinePos + Vector3.up * upOffsetForCast,
                out spawnNormal,
                upOffsetForCast,
                maxDown,
                null
            );

        Vector3 up = Vector3.up;
        Vector3 forward = new Vector3(spawnTan.x, 0f, spawnTan.z);
        if (forward.sqrMagnitude < 1e-6f)
            forward = Vector3.forward;
        forward.Normalize();

        Vector3 lateral = Vector3.Cross(up, forward).normalized;

        if (Mathf.Abs(appliedYaw) > 0.0001f)
            lateral = (Quaternion.AngleAxis(appliedYaw, up) * lateral).normalized;

        bool startLeft = UnityEngine.Random.value < 0.5f;
        float sideSign = startLeft ? -1f : 1f;
        float offTrackOffset = halfRoad + 5.0f;

        // Create horizontal positions first
        Vector3 startHorizontal = new Vector3(spawnSurface.x, 0f, spawnSurface.z) + lateral * sideSign * offTrackOffset;
        Vector3 targetHorizontal = new Vector3(spawnSurface.x, 0f, spawnSurface.z) - lateral * sideSign * offTrackOffset;

        // Project both endpoints to ground
        Vector3 startWS = SpawnUtils.ProjectOntoSurface(startHorizontal + Vector3.up * upOffsetForCast, out _, upOffsetForCast, maxDown, roadMask);
        if (Mathf.Approximately(startWS.y, startHorizontal.y))
            startWS = SpawnUtils.ProjectOntoSurface(startHorizontal + Vector3.up * upOffsetForCast, out _, upOffsetForCast, maxDown, null);

        Vector3 targetWS = SpawnUtils.ProjectOntoSurface(targetHorizontal + Vector3.up * upOffsetForCast, out _, upOffsetForCast, maxDown, roadMask);
        if (Mathf.Approximately(targetWS.y, targetHorizontal.y))
            targetWS = SpawnUtils.ProjectOntoSurface(targetHorizontal + Vector3.up * upOffsetForCast, out _, upOffsetForCast, maxDown, null);

        // Size scaling
        float sizeEval = sizeCurve.Evaluate(distanceNorm);
        float sizeRand = UnityEngine.Random.Range(obstacleScaleRange.x, obstacleScaleRange.y);
        float finalScale = sizeRand * sizeEval;

        float initialDelay = Mathf.Clamp(tCenter * 0.15f, 0f, 1.25f);

        Vector3 actualMoveDir = (new Vector3(targetWS.x, 0f, targetWS.z) - new Vector3(startWS.x, 0f, startWS.z)).normalized;
        if (actualMoveDir.sqrMagnitude < 0.0001f)
            actualMoveDir = lateral;
        Quaternion spawnRot = Quaternion.LookRotation(actualMoveDir, up);

        // Instantiate at start position
        var inst = Instantiate(crossObstaclePrefab, startWS, spawnRot);
        inst.transform.localScale *= finalScale;

        // **NEW: Get parent's bottom offset and adjust position**
        float bottomOffset = GetCrossObstacleBottomOffset(inst);
        inst.transform.position = startWS + Vector3.up * bottomOffset;

        // Also adjust target to account for bottom offset
        Vector3 adjustedTarget = targetWS + Vector3.up * bottomOffset;

        var cross = inst.GetComponent<CrossTrackObstacle>();
        if (cross)
        {
            cross.InitializeDirect(inst.transform.position, adjustedTarget, crossSpeed, initialDelay);
        }

        _lastDebugStart = inst.transform.position;
        _lastDebugEnd = adjustedTarget;
        _hasDebugPath = true;

        _cooldownRemain = effectiveCooldown;

        if (verboseLog)
        {
            Debug.Log($"[CrossObstacleDirector] Spawned Cross: " +
                      $"sSpawn={sSpawn:F1}, lead={predictedLeadDistance:F1}, tCenter={tCenter:F2}, " +
                      $"vCross={crossSpeed:F2}, yawBase={yawErrorDeg:F1}, yawFinal={appliedYaw:F1}, " +
                      $"size={finalScale:F2}, curvature={curvatureFactor:F2}");
        }
    }

    private float SampleCurvature(float sCenter, float sampleLength)
    {
        if (_path.Count < 3) return 0f;
        float sA = Mathf.Clamp(sCenter, 0f, _trackTotalLength);
        float sB = Mathf.Clamp(sCenter + sampleLength, 0f, _trackTotalLength);

        SampleSpline(sA, out _, out Vector3 tA);
        SampleSpline(sB, out _, out Vector3 tB);

        float angle = Vector3.Angle(tA, tB);
        return angle / 180f;
    }

    /// <summary>
    /// Gets the offset needed to place the cross obstacle's parent bottom at ground level.
    /// Only considers the parent's renderer/collider, not children.
    /// </summary>
    private float GetCrossObstacleBottomOffset(GameObject obj)
    {
        if (obj == null) return 0f;

        Transform root = obj.transform;
        float lowestPoint = 0f;
        bool foundAny = false;

        // Check parent's renderer only
        Renderer parentRenderer = root.GetComponent<Renderer>();
        if (parentRenderer != null)
        {
            Bounds localBounds = parentRenderer.bounds;
            float bottom = localBounds.min.y - root.position.y;
            if (!foundAny || bottom < lowestPoint)
            {
                lowestPoint = bottom;
                foundAny = true;
            }
        }

        // Check parent's colliders only
        Collider[] parentColliders = root.GetComponents<Collider>();
        foreach (var col in parentColliders)
        {
            if (col == null || col.isTrigger) continue;

            Bounds localBounds = col.bounds;
            float bottom = localBounds.min.y - root.position.y;
            if (!foundAny || bottom < lowestPoint)
            {
                lowestPoint = bottom;
                foundAny = true;
            }
        }

        if (!foundAny) return 0.05f;
        return Mathf.Abs(lowestPoint);
    }

    private void SampleSpline(float distance, out Vector3 position, out Vector3 tangent)
    {
        position = _path[0];
        tangent = (_path.Count > 1 ? _path[1] - _path[0] : Vector3.forward);

        if (_cumDistances == null || _cumDistances.Length != _path.Count)
            return;

        if (distance <= 0f)
        {
            tangent = (_path[1] - _path[0]);
            return;
        }
        if (distance >= _trackTotalLength)
        {
            position = _path[^1];
            tangent = (_path[^1] - _path[^2]);
            return;
        }

        int i = 0;
        while (i < _cumDistances.Length - 1 && _cumDistances[i + 1] < distance) i++;

        float segStart = _cumDistances[i];
        float segEnd = _cumDistances[i + 1];
        float segLen = segEnd - segStart;
        float t = segLen > 0.0001f ? (distance - segStart) / segLen : 0f;

        Vector3 a = _path[i];
        Vector3 b = _path[i + 1];
        position = Vector3.Lerp(a, b, t);
        tangent = (b - a).normalized;
    }

#if UNITY_EDITOR
private void OnDrawGizmosSelected()
{
    if (!debugGizmos || !_hasDebugPath)
        return;

    Gizmos.color = Color.cyan;
    Gizmos.DrawLine(_lastDebugStart, _lastDebugEnd);
    Gizmos.DrawSphere(_lastDebugStart, 0.2f);
    Gizmos.DrawSphere(_lastDebugEnd, 0.2f);
}
#endif
}