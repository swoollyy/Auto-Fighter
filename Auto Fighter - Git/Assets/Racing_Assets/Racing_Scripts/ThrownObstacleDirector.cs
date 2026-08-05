using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Spawns meteors that dive from above toward a predicted player impact on the road.
/// Accuracy = chance of a true predicted hit; misses get deliberate near-miss offsets.
/// Flight speed is derived from path length / flight time so arrival matches the prediction.
/// </summary>
[DisallowMultipleComponent]
public class ThrownObstacleDirector : MonoBehaviour, ITrackSpawnQueueSource
{
    [Header("References")]
    [SerializeField] private ProceduralTrackGenerator trackGenerator;
    [SerializeField] private Transform playerTransform;
    [SerializeField] private CarController carController;

    public Transform PlayerTransform => playerTransform;

    [Header("Prefabs / Pool")]
    [SerializeField] private GameObject projectilePrefabPlain;
    [SerializeField] private GameObject projectilePrefabExplosive;
    [SerializeField] private GameObject groundRingPrefab;

    [Header("Spawn Control")]
    [SerializeField] private bool enabledSpawning = true;
    [SerializeField, Min(0f)] private float spawnCooldownBase = 3.5f;
    [Tooltip("How far ahead (meters along track) the meteor aims, relative to current car speed.")]
    [SerializeField] private Vector2 leadDistanceRange = new Vector2(12f, 36f);
    [SerializeField, Range(1, 6)] private int maxConcurrent = 2;
    [SerializeField, Range(0f, 3f)] private float concurrentScaleByProgress = 1.5f;

    [Header("Spawn Gate")]
    [SerializeField, Range(0f, 0.5f)] private float spawnEnableProgress = 0.10f;

    [Header("Spawn Cooldown Scaling")]
    [SerializeField, Min(0.05f)] private float minSpawnCooldown = 0.6f;
    [SerializeField] private Vector2 spawnCooldownRandomRange = new Vector2(0.85f, 1.15f);

    [Header("Meteor Flight")]
    [Tooltip("Fallback / clamp reference speed when deriving flight time from lead distance.")]
    [SerializeField] private float baseProjectileSpeed = 18f;
    [SerializeField] private Vector2 flightTimeClamp = new Vector2(0.55f, 3.25f);
    [Tooltip("World-space height above the impact point where the meteor spawns.")]
    [SerializeField, Min(2f)] private float meteorSpawnHeight = 22f;
    [Tooltip("Horizontal offset from impact (side approach) so the dive has a clear downward angle.")]
    [SerializeField, Min(1f)] private float meteorHorizontalOffset = 14f;
    [SerializeField, Min(0f)] private float minLeadDistance = 6.0f;
    [SerializeField, Min(0f)] private float minLandingDistanceFromPlayer = 4.0f;
    [SerializeField] private bool allowCloseLandings = true;
    [SerializeField] private LayerMask hitLayers = ~0;
    [SerializeField] private bool explosiveByDefault = false;
    [SerializeField] private float explosionRadius = 6f;
    [SerializeField] private float explosionKnockback = 12f;

    [Header("Projectile Size Variation")]
    [SerializeField, Min(0f)] private Vector2 projectileSizeRange = new Vector2(0.92f, 1.12f);
    [SerializeField, Range(0f, 1f)] private float sizeGainOverDistance = 0.25f;

    [Header("Spawn Variance")]
    [SerializeField, Range(0f, 3f)] private float lateralJitter = 1.0f;
    [SerializeField, Range(0f, 5f)] private float forwardJitter = 1.8f;

    [Header("Rewards")]
    [SerializeField] private int destroyReward = 12;

    [Header("Debug / Tuning")]
    [SerializeField] private bool debugDraw = false;
    [SerializeField] private KeyCode spawnTestKey = KeyCode.T;

    [Header("Accuracy / Misses")]
    [Tooltip("Chance this throw is a TRUE predicted hit on the player (0..1).")]
    [SerializeField, Range(0f, 1f)] private float baseAccuracy = 0.10f;
    [SerializeField] private AnimationCurve accuracyByDistance = AnimationCurve.Linear(0, 1, 1, 1);
    [SerializeField, Min(0f)] private float maxMissLateral = 4f;
    [SerializeField, Min(0f)] private float maxMissForward = 6f;

    [Header("Explosion Frequency")]
    [SerializeField, Range(0f, 1f)] private float explosionBaseChance = 0.06f;
    [SerializeField] private AnimationCurve explosionChanceByDistance = AnimationCurve.Linear(0, 0.5f, 1, 1.5f);

    // Legacy serialized fields kept so existing prefab/scene values don't vanish; unused by new aim model.
#pragma warning disable 0414
    [SerializeField, HideInInspector] private float travelAllowanceMultiplier = 1.05f;
    [SerializeField, HideInInspector] private float arcHeight = 3f;
    [SerializeField, HideInInspector] private float spawnSideOffset = 2.0f;
    [SerializeField, HideInInspector] private float spawnHeight = 2.0f;
    [SerializeField, HideInInspector] private Vector2 speedRandomMultiplierRange = new Vector2(0.5f, 2.2f);
#pragma warning restore 0414

    // ------------------------------------------------------------------------
    // Per-trial config (TrialConfig). ApplyConfig copies a trial's ThrownSettings
    // into these fields (call BEFORE run spawners init). CaptureConfig snapshots
    // current values for the editor baker.
    // ------------------------------------------------------------------------
    public void ApplyConfig(TrialConfig.ThrownSettings s)
    {
        if (s == null || !s.overrideThrown) return;

        if (s.projectilePrefabPlain != null) projectilePrefabPlain = s.projectilePrefabPlain;
        if (s.projectilePrefabExplosive != null) projectilePrefabExplosive = s.projectilePrefabExplosive;
        if (s.groundRingPrefab != null) groundRingPrefab = s.groundRingPrefab;

        enabledSpawning = s.enabledSpawning;
        spawnCooldownBase = s.spawnCooldownBase;
        leadDistanceRange = s.leadDistanceRange;
        maxConcurrent = s.maxConcurrent;
        concurrentScaleByProgress = s.concurrentScaleByProgress;

        spawnEnableProgress = s.spawnEnableProgress;
        minSpawnCooldown = s.minSpawnCooldown;
        spawnCooldownRandomRange = s.spawnCooldownRandomRange;

        baseProjectileSpeed = s.baseProjectileSpeed;
        flightTimeClamp = s.flightTimeClamp;
        meteorSpawnHeight = s.meteorSpawnHeight;
        meteorHorizontalOffset = s.meteorHorizontalOffset;
        minLeadDistance = s.minLeadDistance;
        minLandingDistanceFromPlayer = s.minLandingDistanceFromPlayer;
        allowCloseLandings = s.allowCloseLandings;
        hitLayers = s.hitLayers;
        explosiveByDefault = s.explosiveByDefault;
        explosionRadius = s.explosionRadius;
        explosionKnockback = s.explosionKnockback;

        projectileSizeRange = s.projectileSizeRange;
        sizeGainOverDistance = s.sizeGainOverDistance;
        lateralJitter = s.lateralJitter;
        forwardJitter = s.forwardJitter;
        destroyReward = s.destroyReward;

        baseAccuracy = s.baseAccuracy;
        accuracyByDistance = s.accuracyByDistance;
        maxMissLateral = s.maxMissLateral;
        maxMissForward = s.maxMissForward;

        explosionBaseChance = s.explosionBaseChance;
        explosionChanceByDistance = s.explosionChanceByDistance;
        debugDraw = s.debugDraw;
    }

    public TrialConfig.ThrownSettings CaptureConfig()
    {
        return new TrialConfig.ThrownSettings
        {
            overrideThrown = true,
            projectilePrefabPlain = projectilePrefabPlain,
            projectilePrefabExplosive = projectilePrefabExplosive,
            groundRingPrefab = groundRingPrefab,
            enabledSpawning = enabledSpawning,
            spawnCooldownBase = spawnCooldownBase,
            leadDistanceRange = leadDistanceRange,
            maxConcurrent = maxConcurrent,
            concurrentScaleByProgress = concurrentScaleByProgress,
            spawnEnableProgress = spawnEnableProgress,
            minSpawnCooldown = minSpawnCooldown,
            spawnCooldownRandomRange = spawnCooldownRandomRange,
            baseProjectileSpeed = baseProjectileSpeed,
            flightTimeClamp = flightTimeClamp,
            meteorSpawnHeight = meteorSpawnHeight,
            meteorHorizontalOffset = meteorHorizontalOffset,
            minLeadDistance = minLeadDistance,
            minLandingDistanceFromPlayer = minLandingDistanceFromPlayer,
            allowCloseLandings = allowCloseLandings,
            hitLayers = hitLayers,
            explosiveByDefault = explosiveByDefault,
            explosionRadius = explosionRadius,
            explosionKnockback = explosionKnockback,
            projectileSizeRange = projectileSizeRange,
            sizeGainOverDistance = sizeGainOverDistance,
            lateralJitter = lateralJitter,
            forwardJitter = forwardJitter,
            destroyReward = destroyReward,
            baseAccuracy = baseAccuracy,
            accuracyByDistance = accuracyByDistance,
            maxMissLateral = maxMissLateral,
            maxMissForward = maxMissForward,
            explosionBaseChance = explosionBaseChance,
            explosionChanceByDistance = explosionChanceByDistance,
            debugDraw = debugDraw,
        };
    }

    private float _cooldown;
    private readonly List<ThrownObstacle> _active = new();
    private readonly TrackSpawnQueuePendingState _queueState = new();
    private readonly TrackSpawnQueueLastSpawn _queueLastSpawn = new();
    private readonly Dictionary<GameObject, Vector3> _prefabBaseScales = new();
    private TrackDistanceMeter _distanceMeter;

    private struct AimDecision
    {
        public bool isTrueHit;
        public float accuracy;
    }

    void Awake()
    {
        if (!trackGenerator) trackGenerator = FindObjectOfType<ProceduralTrackGenerator>();
        if (!playerTransform && GameManager_Racing.Instance != null)
        {
            var activeCar = GameManager_Racing.Instance.ActiveCar;
            if (activeCar != null)
                playerTransform = activeCar.transform;
        }
        if (!carController && playerTransform != null)
            carController = playerTransform.GetComponent<CarController>();

        _distanceMeter = FindObjectOfType<TrackDistanceMeter>();

        if (ProjectilePool.Instance == null)
        {
            var go = new GameObject("ProjectilePool");
            go.AddComponent<ProjectilePool>();
        }
    }

    void Update()
    {
        if (!enabledSpawning) return;

        _active.RemoveAll(x => x == null || !x.gameObject.activeInHierarchy);

        if (_queueState.IsControlled)
        {
            int allowedConcurrent = ScaleConcurrentByTrackProgress(maxConcurrent);
            _cooldown -= Time.deltaTime;
            if (_cooldown <= 0f && _active.Count < allowedConcurrent && _queueState.TrySubmit(this))
                _cooldown = Mathf.Max(0.1f, spawnCooldownBase * 0.25f);
            return;
        }

        int allowed = ScaleConcurrentByTrackProgress(maxConcurrent);
        _cooldown -= Time.deltaTime;
        if (_cooldown <= 0f && _active.Count < allowed)
        {
            TrySpawn();
            if (_cooldown <= 0f)
                _cooldown = 0.5f;
        }

        if (debugDraw && Input.GetKeyDown(spawnTestKey))
            TrySpawn(test: true);
    }

    private void TrySpawn(bool test = false)
    {
        if (!trackGenerator || playerTransform == null) return;
        if (projectilePrefabPlain == null && projectilePrefabExplosive == null) return;

        if (_distanceMeter == null)
            _distanceMeter = FindObjectOfType<TrackDistanceMeter>();

        float playerDist = _distanceMeter != null ? _distanceMeter.DistanceAlongTrack : 0f;
        float trackTotal = ComputeTrackTotalLength();
        float distanceNorm = trackTotal > 0f ? Mathf.Clamp01(playerDist / trackTotal) : 0f;

        if (trackTotal > 0f && distanceNorm < spawnEnableProgress)
        {
            if (debugDraw)
                Debug.Log($"[ThrownObstacleDirector] spawn gated until {spawnEnableProgress * 100f:0}% (current {distanceNorm * 100f:0}%).");
            _cooldown = Mathf.Lerp(spawnCooldownBase, minSpawnCooldown, distanceNorm) *
                        UnityEngine.Random.Range(spawnCooldownRandomRange.x, spawnCooldownRandomRange.y);
            return;
        }

        float carSpeed = GetCarSpeedAlongTrack();
        float lead = Mathf.Lerp(leadDistanceRange.x, leadDistanceRange.y, UnityEngine.Random.value);
        lead += UnityEngine.Random.Range(-forwardJitter, forwardJitter);
        lead = Mathf.Max(lead, minLeadDistance);

        // Flight time = time for the car to cover the lead distance (prediction horizon).
        float flightTime = lead / Mathf.Max(carSpeed, Mathf.Max(1f, baseProjectileSpeed * 0.35f));
        flightTime = Mathf.Clamp(flightTime, flightTimeClamp.x, flightTimeClamp.y);

        if (!TryPredictImpactPoint(playerDist, carSpeed, flightTime, out Vector3 impactPos, out Vector3 impactFwd, out Vector3 impactRight))
            return;

        AimDecision aim = DecideAim(distanceNorm);

        if (!aim.isTrueHit)
            ApplyMissOffset(ref impactPos, impactRight, impactFwd, aim.accuracy, distanceNorm);

        impactPos = ProjectToRoad(impactPos);

        // Misses: keep a minimum near-miss gap. True hits may land on the player.
        if (!aim.isTrueHit)
        {
            float landingDistToPlayer = HorizontalDistance(impactPos, playerTransform.position);
            if (landingDistToPlayer < minLandingDistanceFromPlayer)
            {
                if (!allowCloseLandings)
                {
                    if (debugDraw)
                        Debug.Log($"[ThrownObstacleDirector] miss skipped (too close: {landingDistToPlayer:F2})");
                    return;
                }

                Vector3 dir = impactPos - playerTransform.position;
                dir.y = 0f;
                if (dir.sqrMagnitude < 1e-6f) dir = impactFwd;
                if (dir.sqrMagnitude < 1e-6f) dir = Vector3.forward;
                dir.Normalize();
                impactPos = playerTransform.position + dir * minLandingDistanceFromPlayer;
                impactPos = ProjectToRoad(impactPos);
            }

            // Small extra variety on misses only — never on true hits.
            impactPos += impactRight * UnityEngine.Random.Range(-Mathf.Min(lateralJitter, 0.8f), Mathf.Min(lateralJitter, 0.8f));
            impactPos = ProjectToRoad(impactPos);
        }

        Vector3 origin = BuildMeteorOrigin(impactPos, impactFwd, impactRight);
        float pathLen = Vector3.Distance(origin, impactPos);
        if (pathLen < 0.5f)
        {
            origin = impactPos + Vector3.up * meteorSpawnHeight + impactRight * meteorHorizontalOffset;
            pathLen = Vector3.Distance(origin, impactPos);
        }

        // Speed locks arrival to the predicted flight time (no post-hoc speed RNG that desyncs aim).
        float finalSpeed = pathLen / Mathf.Max(0.05f, flightTime);
        finalSpeed = Mathf.Clamp(finalSpeed, baseProjectileSpeed * 0.35f, baseProjectileSpeed * 4.5f);
        // Re-derive time from clamped speed so telegraph / arrival stay consistent.
        flightTime = pathLen / Mathf.Max(0.05f, finalSpeed);

        float explosionChanceMul = explosionChanceByDistance != null ? explosionChanceByDistance.Evaluate(distanceNorm) : 1f;
        bool explosive = explosiveByDefault || UnityEngine.Random.value < Mathf.Clamp01(explosionBaseChance * explosionChanceMul);

        GameObject chosenPrefab = explosive
            ? (projectilePrefabExplosive != null ? projectilePrefabExplosive : projectilePrefabPlain)
            : projectilePrefabPlain;
        if (chosenPrefab == null) return;

        float telegraphRadius = explosive
            ? explosionRadius
            : EstimatePlainTelegraphRadius(chosenPrefab);

        bool previewSpawned = TrySpawnTelegraph(impactPos, telegraphRadius, flightTime);

        SpawnProjectile(
            origin,
            impactPos,
            explosive,
            chosenPrefab,
            finalSpeed,
            test,
            flightTime,
            previewSpawned,
            distanceNorm);

        _queueLastSpawn.Record(impactPos, chosenPrefab.name);

        float baseCd = Mathf.Lerp(spawnCooldownBase, minSpawnCooldown, distanceNorm);
        float jitter = UnityEngine.Random.Range(spawnCooldownRandomRange.x, spawnCooldownRandomRange.y);
        _cooldown = Mathf.Max(minSpawnCooldown, baseCd) * jitter;

        if (debugDraw)
        {
            Debug.DrawLine(origin, impactPos, explosive ? Color.red : Color.yellow, 8f);
            Debug.Log($"[ThrownObstacleDirector] aim={(aim.isTrueHit ? "HIT" : "MISS")} acc={aim.accuracy:0.00} " +
                      $"T={flightTime:0.00}s speed={finalSpeed:0.0} path={pathLen:0.0} explosive={explosive}");
        }
    }

    private bool TryPredictImpactPoint(
        float carDist,
        float carSpeed,
        float flightTime,
        out Vector3 impactPos,
        out Vector3 impactFwd,
        out Vector3 impactRight)
    {
        impactPos = playerTransform.position;
        impactFwd = playerTransform.forward;
        impactRight = playerTransform.right;

        float predictedDist = carDist + Mathf.Max(0f, carSpeed) * flightTime;
        if (trackGenerator != null)
        {
            float total = ComputeTrackTotalLength();
            if (total > 0f)
                predictedDist = Mathf.Clamp(predictedDist, 0f, Mathf.Max(0f, total - 0.25f));
        }

        if (!TrySamplePositionAtDistance(predictedDist, out Vector3 pathPos, out Vector3 pathFwd))
        {
            // Velocity fallback when path sampling fails.
            Rigidbody rb = playerTransform.GetComponent<Rigidbody>();
            Vector3 vel = rb != null ? rb.velocity : playerTransform.forward * carSpeed;
            impactPos = playerTransform.position + vel * flightTime;
            impactFwd = pathFwd.sqrMagnitude > 1e-6f ? pathFwd : playerTransform.forward;
            impactFwd.y = 0f;
            if (impactFwd.sqrMagnitude < 1e-6f) impactFwd = Vector3.forward;
            impactFwd.Normalize();
            impactRight = Vector3.Cross(Vector3.up, impactFwd).normalized;
            impactPos = ProjectToRoad(impactPos);
            return true;
        }

        impactFwd = pathFwd;
        impactFwd.y = 0f;
        if (impactFwd.sqrMagnitude < 1e-6f) impactFwd = playerTransform.forward;
        impactFwd.Normalize();
        impactRight = Vector3.Cross(Vector3.up, impactFwd).normalized;

        // Preserve the player's current lateral offset from the path centerline.
        float lateral = 0f;
        if (TrySamplePositionAtDistance(carDist, out Vector3 pathNow, out Vector3 fwdNow))
        {
            Vector3 rightNow = Vector3.Cross(Vector3.up, fwdNow.normalized).normalized;
            lateral = Vector3.Dot(playerTransform.position - pathNow, rightNow);
        }

        impactPos = pathPos + impactRight * lateral;

        // Blend a bit of raw velocity prediction so sudden steering is respected.
        Rigidbody carRb = playerTransform.GetComponent<Rigidbody>();
        if (carRb != null && carRb.velocity.sqrMagnitude > 0.25f)
        {
            Vector3 velPred = playerTransform.position + carRb.velocity * flightTime;
            impactPos = Vector3.Lerp(impactPos, velPred, 0.25f);
        }

        impactPos = ProjectToRoad(impactPos);
        return true;
    }

    private Vector3 BuildMeteorOrigin(Vector3 impactPos, Vector3 impactFwd, Vector3 impactRight)
    {
        float sideSign = UnityEngine.Random.value < 0.5f ? -1f : 1f;
        float horiz = meteorHorizontalOffset + UnityEngine.Random.Range(-lateralJitter, lateralJitter);
        horiz = Mathf.Max(4f, horiz);

        // Approach from the side and slightly ahead so the dive is clearly downward, not vertical.
        Vector3 origin = impactPos
            + impactRight * (sideSign * horiz)
            + impactFwd * UnityEngine.Random.Range(-horiz * 0.15f, horiz * 0.35f)
            + Vector3.up * meteorSpawnHeight;

        return origin;
    }

    private float GetCarSpeedAlongTrack()
    {
        if (carController != null)
        {
            // Prefer planar speed in the car's forward direction (matches track progress).
            Rigidbody rb = carController.GetComponent<Rigidbody>();
            if (rb != null)
            {
                Vector3 fwd = carController.transform.forward;
                fwd.y = 0f;
                if (fwd.sqrMagnitude > 1e-6f)
                {
                    fwd.Normalize();
                    float along = Vector3.Dot(rb.velocity, fwd);
                    if (along > 0.25f) return along;
                }
                return Mathf.Max(0f, rb.velocity.magnitude);
            }
            return Mathf.Max(0f, carController.CurrentSpeed);
        }

        if (playerTransform != null)
        {
            Rigidbody rb = playerTransform.GetComponent<Rigidbody>();
            if (rb != null) return Mathf.Max(0f, rb.velocity.magnitude);
        }

        return 0f;
    }

    private static float HorizontalDistance(Vector3 a, Vector3 b)
    {
        a.y = 0f;
        b.y = 0f;
        return Vector3.Distance(a, b);
    }

    private Vector3 ProjectToRoad(Vector3 worldPos)
    {
        LayerMask roadMask = LayerMask.GetMask("RoadSurface");
        LayerMask roadGrassMask = LayerMask.GetMask("RoadSurface", "Grass", "Road");
        const float up = 10f;
        const float down = 50f;

        Vector3 projected = SpawnUtils.ProjectOntoSurface(worldPos + Vector3.up * up, up, down, roadMask);
        if (Mathf.Approximately(projected.y, worldPos.y))
            projected = SpawnUtils.ProjectOntoSurface(worldPos + Vector3.up * up, up, down, roadGrassMask);
        if (Mathf.Approximately(projected.y, worldPos.y))
            projected = SpawnUtils.ProjectOntoSurface(worldPos + Vector3.up * up, up, down, null);
        return projected;
    }

    private float EstimatePlainTelegraphRadius(GameObject previewPrefab)
    {
        float r = 1.5f;
        if (previewPrefab == null) return r;

        var sc = previewPrefab.GetComponentInChildren<SphereCollider>();
        if (sc != null)
        {
            r = sc.radius * Mathf.Max(previewPrefab.transform.lossyScale.x, previewPrefab.transform.lossyScale.z);
        }
        else
        {
            var col = previewPrefab.GetComponentInChildren<Collider>();
            if (col != null)
                r = Mathf.Max(col.bounds.extents.x, col.bounds.extents.z);
        }

        return Mathf.Clamp(r, 0.75f, 4.0f);
    }

    private bool TrySpawnTelegraph(Vector3 impactPos, float telegraphRadius, float flightTime)
    {
        if (groundRingPrefab == null || ProjectilePool.Instance == null)
            return false;

        var tele = ProjectilePool.Instance.Get(groundRingPrefab);
        if (tele == null) return false;

        tele.transform.position = impactPos;
        tele.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
        tele.SetActive(true);

        float holdSeconds = Mathf.Max(0f, flightTime - 0.05f);

        var decalTele = tele.GetComponent<URPDecalTelegraph>();
        if (decalTele != null)
        {
            decalTele.SetWorldPose(impactPos);
            decalTele.Play(
                radius: telegraphRadius,
                seconds: holdSeconds,
                onComplete: () => ProjectilePool.Instance.Return(groundRingPrefab, tele));
            return true;
        }

        var gr = tele.GetComponent<GroundRing>();
        if (gr != null)
        {
            gr.Play(
                telegraphRadius,
                onComplete: () => ProjectilePool.Instance.Return(groundRingPrefab, tele),
                holdOverride: holdSeconds);
            return true;
        }

        StartCoroutine(ReturnTelegraphLater(groundRingPrefab, tele, Mathf.Max(0.1f, holdSeconds)));
        return true;
    }

    private AimDecision DecideAim(float distanceNorm)
    {
        float curveMul = accuracyByDistance != null ? accuracyByDistance.Evaluate(distanceNorm) : 1f;
        float acc = Mathf.Clamp01(baseAccuracy * curveMul);
        bool trueHit = UnityEngine.Random.value < acc;
        return new AimDecision { isTrueHit = trueHit, accuracy = acc };
    }

    private void ApplyMissOffset(ref Vector3 interceptPos, Vector3 right, Vector3 spawnFwd, float accuracy, float distanceNorm)
    {
        float missScale = (1f - accuracy) * Mathf.Lerp(0.6f, 1.4f, distanceNorm);
        // Always miss by at least a modest amount so "miss" never lands on the player by chance.
        float minLat = Mathf.Lerp(1.2f, 2.2f, missScale);
        float lateral = UnityEngine.Random.Range(minLat, Mathf.Max(minLat, maxMissLateral * missScale));
        if (UnityEngine.Random.value < 0.5f) lateral = -lateral;

        float forward = UnityEngine.Random.Range(-maxMissForward, maxMissForward) * missScale;
        interceptPos += right * lateral;
        interceptPos += spawnFwd * forward;
    }

    private void SpawnProjectile(
        Vector3 origin,
        Vector3 landPoint,
        bool explosive,
        GameObject chosenPrefab,
        float speed,
        bool test,
        float timeToLanding,
        bool previewSpawned,
        float distanceNorm)
    {
        if (chosenPrefab == null || ProjectilePool.Instance == null) return;

        var go = ProjectilePool.Instance.Get(chosenPrefab);
        if (go == null) return;

        Vector3 diveDir = landPoint - origin;
        if (diveDir.sqrMagnitude < 1e-6f) diveDir = Vector3.down;
        diveDir.Normalize();

        go.transform.position = origin;
        go.transform.rotation = Quaternion.LookRotation(diveDir, Vector3.up);

        if (!_prefabBaseScales.TryGetValue(chosenPrefab, out Vector3 baseScale))
        {
            baseScale = chosenPrefab.transform.localScale;
            _prefabBaseScales[chosenPrefab] = baseScale;
        }

        float baseSize = UnityEngine.Random.Range(projectileSizeRange.x, projectileSizeRange.y);
        float gain = 1f + sizeGainOverDistance * distanceNorm;
        go.transform.localScale = baseScale * (baseSize * gain);
        go.SetActive(true);

        var ob = go.GetComponent<ThrownObstacle>();
        if (ob == null) ob = go.AddComponent<ThrownObstacle>();

        // Arc height unused for dive meteors (0 = straight dive). Kept in API for prefab compatibility.
        ob.Initialize(
            director: this,
            spawnPos: origin,
            landPos: landPoint,
            speed: speed,
            arcHeight: 0f,
            explosive: explosive,
            explosionRadius: explosionRadius,
            explosionImpulse: explosionKnockback,
            hitLayers: hitLayers,
            prefabReference: chosenPrefab,
            ringPrefab: groundRingPrefab,
            rewardOnDestroy: destroyReward,
            previewRingSpawned: previewSpawned);

        _active.Add(ob);

        if (debugDraw && test)
            Debug.Log($"[ThrownObstacleDirector] Spawned {(explosive ? "Explosive" : "Plain")} speed={speed:F2} time={timeToLanding:F2}");
    }

    private bool TrySamplePositionAtDistance(float distanceAlongTrackMeters, out Vector3 pos, out Vector3 forward)
    {
        pos = transform.position;
        forward = transform.forward;

        var pts = trackGenerator?.PathPoints;
        if (pts == null || pts.Count < 2)
            return false;

        int n = pts.Count;
        float[] cum = new float[n];
        cum[0] = 0f;
        float total = 0f;
        for (int i = 1; i < n; i++)
        {
            total += Vector3.Distance(pts[i - 1], pts[i]);
            cum[i] = total;
        }

        if (total <= 0f)
            return false;

        distanceAlongTrackMeters = Mathf.Clamp(distanceAlongTrackMeters, 0f, total);

        int idx = 0;
        for (int i = 0; i < n - 1; i++)
        {
            if (cum[i + 1] >= distanceAlongTrackMeters)
            {
                idx = i;
                break;
            }
        }

        float segStart = cum[idx];
        float segEnd = cum[Mathf.Min(idx + 1, n - 1)];
        float segLen = Mathf.Max(0.0001f, segEnd - segStart);
        float t = Mathf.Clamp01((distanceAlongTrackMeters - segStart) / segLen);

        Vector3 a = pts[idx];
        Vector3 b = pts[Mathf.Min(idx + 1, n - 1)];

        pos = Vector3.Lerp(a, b, t);
        forward = (b - a);
        if (forward.sqrMagnitude < 1e-6f) forward = transform.forward;
        else forward.Normalize();

        return true;
    }

    private int ScaleConcurrentByTrackProgress(int baseVal)
    {
        if (concurrentScaleByProgress <= 0f) return baseVal;

        if (_distanceMeter == null)
            _distanceMeter = FindObjectOfType<TrackDistanceMeter>();

        float norm = 0f;
        if (_distanceMeter != null && trackGenerator != null)
        {
            float total = ComputeTrackTotalLength();
            norm = Mathf.Clamp01(_distanceMeter.DistanceAlongTrack / Mathf.Max(1f, total));
        }

        int extra = Mathf.FloorToInt(norm * concurrentScaleByProgress);
        return Mathf.Clamp(baseVal + extra, 1, 8);
    }

    public void SetCar(CarController car)
    {
        playerTransform = car != null ? car.transform : null;
        carController = car;
    }

    internal void NotifyProjectileStopped(ThrownObstacle ob)
    {
        if (_active.Contains(ob)) _active.Remove(ob);
    }

    internal void NotifyProjectileCloseCall(ThrownObstacle ob, float closestDistance)
    {
        GameManager_Racing.Instance?.HandleProjectileCloseCall(ob.transform.position, closestDistance);
    }

    internal void NotifyProjectileExploded(ThrownObstacle ob, Vector3 position, float radius)
    {
        GameManager_Racing.Instance?.HandleProjectileExplosion(position, radius);
    }

    private System.Collections.IEnumerator ReturnTelegraphLater(GameObject prefab, GameObject instance, float delay)
    {
        yield return new WaitForSeconds(delay);
        if (instance != null && prefab != null && ProjectilePool.Instance != null)
            ProjectilePool.Instance.Return(prefab, instance);
    }

    public void ForceSpawnAt(Vector3 origin, Vector3 landing, bool explosive)
    {
        float pathLen = Vector3.Distance(origin, landing);
        float speed = Mathf.Max(1f, baseProjectileSpeed);
        if (pathLen > 0.01f)
            speed = Mathf.Clamp(pathLen / 1.2f, baseProjectileSpeed * 0.5f, baseProjectileSpeed * 3f);

        GameObject chosen = explosive
            ? (projectilePrefabExplosive != null ? projectilePrefabExplosive : projectilePrefabPlain)
            : projectilePrefabPlain;
        float approxTime = pathLen / Mathf.Max(0.001f, speed);
        SpawnProjectile(origin, landing, explosive, chosen, speed, test: true, timeToLanding: approxTime, previewSpawned: false, distanceNorm: 0.5f);
    }

    private float ComputeTrackTotalLength()
    {
        var pts = trackGenerator?.PathPoints;
        if (pts == null || pts.Count < 2) return 0f;
        float total = 0f;
        for (int i = 1; i < pts.Count; i++)
            total += Vector3.Distance(pts[i - 1], pts[i]);
        return total;
    }

    public string SpawnQueueLabel => "Thrown Obstacles";
    public bool IsSpawnQueueReady => enabledSpawning && trackGenerator != null && playerTransform != null &&
                                     (projectilePrefabPlain != null || projectilePrefabExplosive != null);
    public bool HasSpawnQueueCapacity => _active.Count < ScaleConcurrentByTrackProgress(maxConcurrent);
    public bool HasPendingSpawnRequest => _queueState.HasPending;
    public bool TrySubmitSpawnRequest() => _queueState.TrySubmit(this);
    public bool TryExecutePendingSpawn() => _queueState.TryExecute(() =>
    {
        int before = _active.Count;
        TrySpawn();
        return _active.Count > before;
    });
    public bool TryConsumeLastSpawnReport(out TrackSpawnQueueSpawnReport report) => _queueLastSpawn.TryConsume(out report);
    public void CancelPendingSpawnRequest() => _queueState.Cancel();
    public void SetQueueControlledAutonomous(bool controlled, TrackSpawnerQueue owner = null) => _queueState.Bind(controlled, owner);
}
