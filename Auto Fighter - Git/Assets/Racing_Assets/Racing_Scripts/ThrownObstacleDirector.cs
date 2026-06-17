using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Director that spawns deterministic thrown obstacles ahead of the player.
/// - Uses track sampling from ProceduralTrackGenerator.PathPoints
/// - Predictive intercept (path-based)
/// - Deterministic, non-gravity arc (projectile moves itself; collisions still happen)
/// - Pooling via ProjectilePool
/// - Supports plain-impact and explosive variants (explosive shows ground ring)
/// - Debug gizmos and spawn hotkey
///
/// Refactor notes:
/// - Accuracy now means "chance this throw is a true hit attempt".
/// - Misses get deliberate offsets (near-miss behavior).
/// - MinLandingDistance is enforced for misses, but NOT for true hits (so 0 distance remains possible sometimes).
/// - Aim error is applied AFTER intercept refinement and BEFORE final ground projection, so it cannot be overwritten.
/// </summary>
[DisallowMultipleComponent]
public class ThrownObstacleDirector : MonoBehaviour, ITrackSpawnQueueSource
{
    [Header("References")]
    [SerializeField] private ProceduralTrackGenerator trackGenerator;
    [SerializeField] private Transform playerTransform;
    [SerializeField] private CarController carController;

    public Transform PlayerTransform => playerTransform; // expose for projectiles

    [Header("Prefabs / Pool (assign two prefabs)")]
    [Tooltip("Projectile prefab (plain-impact).")]
    [SerializeField] private GameObject projectilePrefabPlain;
    [Tooltip("Explosive projectile prefab (optional). If null, plain prefab will be used for explosive too.")]
    [SerializeField] private GameObject projectilePrefabExplosive;
    [Tooltip("Optional ground ring prefab for explosive variant (pooled).")]
    [SerializeField] private GameObject groundRingPrefab;

    [Header("Spawn Control")]
    [SerializeField] private bool enabledSpawning = true;
    [SerializeField, Min(0f)] private float spawnCooldownBase = 3.5f;
    [Tooltip("Min / Max lead distance ahead of player to aim")]
    [SerializeField] private Vector2 leadDistanceRange = new Vector2(12f, 36f);
    [SerializeField, Range(1, 6)] private int maxConcurrent = 2;
    [Tooltip("Scale extra concurrent spawns as track progress increases (0..1 -> added slots).")]
    [SerializeField, Range(0f, 3f)] private float concurrentScaleByProgress = 1.5f;

    [Header("Spawn Gate")]
    [SerializeField, Range(0f, 0.5f)] private float spawnEnableProgress = 0.10f;

    [Header("Spawn Cooldown Scaling")]
    [SerializeField, Min(0.05f)] private float minSpawnCooldown = 0.6f;
    [SerializeField] private Vector2 spawnCooldownRandomRange = new Vector2(0.85f, 1.15f);

    [Header("Projectile Defaults")]
    [SerializeField] private float baseProjectileSpeed = 18f; // used as initial guess only
    [SerializeField] private float travelAllowanceMultiplier = 1.05f;
    [SerializeField] private float arcHeight = 3f;
    [SerializeField] private LayerMask hitLayers = ~0;
    [SerializeField] private bool explosiveByDefault = false;
    [SerializeField] private float explosionRadius = 6f;
    [SerializeField] private float explosionKnockback = 12f;

    [Header("Projectile Size Variation")]
    [SerializeField, Min(0f)] private Vector2 projectileSizeRange = new Vector2(0.92f, 1.12f);
    [SerializeField, Range(0f, 1f)] private float sizeGainOverDistance = 0.25f;

    [Header("Projectile Speed Variation")]
    [SerializeField, Min(0.1f)] private Vector2 speedRandomMultiplierRange = new Vector2(0.5f, 2.2f);

    [Header("Spawn Placement")]
    [SerializeField, Min(0f)] private float spawnSideOffset = 2.0f;
    [SerializeField, Min(0f)] private float spawnHeight = 2.0f;
    [SerializeField, Min(0f)] private float minLandingDistanceFromPlayer = 4.0f;
    [SerializeField, Min(0f)] private float minLeadDistance = 6.0f;

    [Header("Close-Landing Policy")]
    [Tooltip("If true, misses that would land within MinLandingDistance will be clamped forward instead of skipped.")]
    [SerializeField] private bool allowCloseLandings = true;

    [Header("Spawn Variance")]
    [SerializeField, Range(0f, 3f)] private float lateralJitter = 1.0f;
    [SerializeField, Range(0f, 5f)] private float forwardJitter = 1.8f;

    [Header("Rewards")]
    [SerializeField] private int destroyReward = 12;

    [Header("Debug / Tuning")]
    [SerializeField] private bool debugDraw = false;
    [SerializeField] private KeyCode spawnTestKey = KeyCode.T;

    [Header("Accuracy / Misses")]
    [Tooltip("Interpreted as: chance a throw is a TRUE HIT attempt (0..1). Low values = mostly near-misses.")]
    [SerializeField, Range(0f, 1f)] private float baseAccuracy = 0.10f;
    [Tooltip("Multiplier applied to baseAccuracy by normalized distance along track (0..1).")]
    [SerializeField] private AnimationCurve accuracyByDistance = AnimationCurve.Linear(0, 1, 1, 1);
    [Tooltip("Max lateral miss offset (meters).")]
    [SerializeField, Min(0f)] private float maxMissLateral = 4f;
    [Tooltip("Max forward/back miss offset (meters).")]
    [SerializeField, Min(0f)] private float maxMissForward = 6f;

    [Header("Explosion Frequency")]
    [SerializeField, Range(0f, 1f)] private float explosionBaseChance = 0.06f;
    [SerializeField] private AnimationCurve explosionChanceByDistance = AnimationCurve.Linear(0, 0.5f, 1, 1.5f);

    private float _cooldown;
    private readonly List<ThrownObstacle> _active = new();
    private readonly TrackSpawnQueuePendingState _queueState = new();
    private readonly TrackSpawnQueueLastSpawn _queueLastSpawn = new();

    // base scales so pooled objects do not compound scale
    private readonly Dictionary<GameObject, Vector3> _prefabBaseScales = new();

    private struct AimDecision
    {
        public bool isTrueHit;
        public float accuracy; // final 0..1 used
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

        int allowedConcurrentAutonomous = ScaleConcurrentByTrackProgress(maxConcurrent);

        _cooldown -= Time.deltaTime;
        if (_cooldown <= 0f && _active.Count < allowedConcurrentAutonomous)
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

        // choose lead distance with jitter and enforce minLeadDistance
        float lead = Mathf.Lerp(leadDistanceRange.x, leadDistanceRange.y, UnityEngine.Random.value);
        lead += UnityEngine.Random.Range(-forwardJitter, forwardJitter);
        lead = Mathf.Max(lead, minLeadDistance);

        // player distance along track
        float playerDist = 0f;
        var distanceMeter = FindObjectOfType<TrackDistanceMeter>();
        if (distanceMeter != null) playerDist = distanceMeter.DistanceAlongTrack;

        float sSpawnMeters = Mathf.Max(0f, playerDist + lead);
        float trackTotal = ComputeTrackTotalLength();

        // gate spawns early
        if (trackTotal > 0f)
        {
            float playerProgress = Mathf.Clamp01(playerDist / trackTotal);
            if (playerProgress < spawnEnableProgress)
            {
                if (debugDraw) Debug.Log($"[ThrownObstacleDirector] spawn gated until {spawnEnableProgress * 100f:0}% (current {playerProgress * 100f:0}%).");
                _cooldown = Mathf.Lerp(spawnCooldownBase, minSpawnCooldown, playerProgress) *
                            UnityEngine.Random.Range(spawnCooldownRandomRange.x, spawnCooldownRandomRange.y);
                return;
            }
        }

        if (!TrySamplePositionAtDistance(sSpawnMeters, out Vector3 spawnCenter, out Vector3 spawnFwd))
            return;

        // origin
        Vector3 right = Vector3.Cross(Vector3.up, spawnFwd).normalized;
        float sideSign = (UnityEngine.Random.value < 0.5f) ? -1f : 1f;
        float sideOffset = spawnSideOffset + UnityEngine.Random.Range(-lateralJitter, lateralJitter);
        Vector3 origin = spawnCenter + right * sideSign * sideOffset + Vector3.up * spawnHeight;

        // solve intercept
        float speedAllowance = baseProjectileSpeed * travelAllowanceMultiplier;

        bool found = TryComputePathPredictiveIntercept(origin, speedAllowance, out Vector3 interceptPos, out float interceptTime);

        if (!found)
        {
            Vector3 targetPos = playerTransform.position;
            Rigidbody carRb = playerTransform.GetComponent<Rigidbody>();
            Vector3 targetVel = carRb != null ? carRb.velocity : Vector3.zero;

            if (!TryComputeIntercept(origin, targetPos, targetVel, speedAllowance, speedAllowance * 10f, out interceptPos, out interceptTime))
            {
                interceptPos = targetPos + playerTransform.forward * Mathf.Clamp(baseProjectileSpeed * 0.65f, 6f, 30f);
                float horizDist = Vector3.Distance(new Vector3(origin.x, 0f, origin.z), new Vector3(interceptPos.x, 0f, interceptPos.z));
                interceptTime = Mathf.Clamp(horizDist / Mathf.Max(2f, baseProjectileSpeed), 0.25f, 6f);
            }
        }

        // distance norm for tuning curves
        float distanceNorm = (trackTotal > 0f) ? Mathf.Clamp01(sSpawnMeters / trackTotal) : 0f;

        // decide aim intent (true hit vs near-miss)
        AimDecision aim = DecideAim(distanceNorm);

        // explosion selection
        float explosionChanceMultiplier = explosionChanceByDistance != null ? explosionChanceByDistance.Evaluate(distanceNorm) : 1f;
        float explosionChance = Mathf.Clamp01(explosionBaseChance * explosionChanceMultiplier);
        bool explosive = UnityEngine.Random.value < explosionChance || explosiveByDefault;

        // prefab
        GameObject chosenPrefab = explosive ? (projectilePrefabExplosive ?? projectilePrefabPlain) : projectilePrefabPlain;
        if (chosenPrefab == null) return;

        // compute speed estimate
        Vector3 flatOrigin = new Vector3(origin.x, 0f, origin.z);
        Vector3 flatIntercept = new Vector3(interceptPos.x, 0f, interceptPos.z);
        float horizDistance = Vector3.Distance(flatOrigin, flatIntercept);

        float finalSpeed = (interceptTime > 0f) ? (horizDistance / Mathf.Max(0.001f, interceptTime)) : baseProjectileSpeed;

        float speedRand = UnityEngine.Random.Range(speedRandomMultiplierRange.x, speedRandomMultiplierRange.y);
        finalSpeed *= speedRand;
        finalSpeed = Mathf.Clamp(finalSpeed, baseProjectileSpeed * 0.25f, baseProjectileSpeed * 3.0f);

        // refinement using actual chosen speed
        if (TryComputePathPredictiveIntercept(origin, finalSpeed, out Vector3 refinedPos, out float refinedTime))
        {
            interceptPos = refinedPos;
            interceptTime = refinedTime;
        }
        else
        {
            if (debugDraw) Debug.Log("[ThrownObstacleDirector] refinement failed; using initial intercept.");
        }

        // Apply miss offsets ONLY if not a true hit attempt
        if (!aim.isTrueHit)
            ApplyMissOffset(ref interceptPos, right, spawnFwd, aim.accuracy, distanceNorm);

        // Project center + landing to road AFTER aim is final (prevents overwrites)
        LayerMask roadMask = LayerMask.GetMask("RoadSurface");
        Vector3 groundCenter = SpawnUtils.ProjectOntoSurface(spawnCenter, 2f, 25f, roadMask);
        Vector3 groundLanding = SpawnUtils.ProjectOntoSurface(interceptPos, 2f, 25f, roadMask);

        origin = groundCenter + right * sideSign * sideOffset + Vector3.up * spawnHeight;
        interceptPos = groundLanding;

        // close landing rule:
        // - For TRUE HIT: allow distance 0 (your original intent)
        // - For MISS: enforce minLandingDistanceFromPlayer via skip or clamp
        if (!aim.isTrueHit)
        {
            float landingDistToPlayer = Vector3.Distance(interceptPos, playerTransform.position);
            if (landingDistToPlayer < minLandingDistanceFromPlayer)
            {
                if (allowCloseLandings)
                {
                    Vector3 dir = (interceptPos - playerTransform.position);
                    if (dir.sqrMagnitude < 1e-6f) dir = playerTransform.forward;
                    dir.y = 0f;
                    if (dir.sqrMagnitude < 1e-6f) dir = Vector3.forward;
                    dir.Normalize();

                    interceptPos = playerTransform.position + dir * minLandingDistanceFromPlayer;
                    interceptPos += right * UnityEngine.Random.Range(-Mathf.Min(lateralJitter, 1.2f), Mathf.Min(lateralJitter, 1.2f));

                    // re-project after clamp (keeps it on road)
                    interceptPos = SpawnUtils.ProjectOntoSurface(interceptPos, 2f, 25f, roadMask);

                    if (debugDraw) Debug.Log($"[ThrownObstacleDirector] miss clamped to min landing dist {minLandingDistanceFromPlayer:F2}");
                }
                else
                {
                    if (debugDraw) Debug.Log($"[ThrownObstacleDirector] miss skipped (too close: {landingDistToPlayer:F2} < {minLandingDistanceFromPlayer:F2})");
                    return;
                }
            }
        }

        // optional small lateral variety (keep it subtle so it doesn't fight the aim model)
        interceptPos += right * UnityEngine.Random.Range(-Mathf.Min(lateralJitter, 0.8f), Mathf.Min(lateralJitter, 0.8f));
        interceptPos = SpawnUtils.ProjectOntoSurface(interceptPos, 2f, 25f, roadMask);


        float telegraphRadius = explosionRadius;

        if (!explosive)
        {
            // Use the projectile's collider footprint as the non-explosive "hit radius"
            GameObject previewPrefab = projectilePrefabPlain != null ? projectilePrefabPlain : chosenPrefab;

            float r = 1.5f;
            if (previewPrefab != null)
            {
                // SphereCollider preferred
                var sc = previewPrefab.GetComponentInChildren<SphereCollider>();
                if (sc != null) r = sc.radius * Mathf.Max(previewPrefab.transform.lossyScale.x, previewPrefab.transform.lossyScale.z);
                else
                {
                    // Otherwise approximate from bounds
                    var col = previewPrefab.GetComponentInChildren<Collider>();
                    if (col != null)
                    {
                        Bounds b = col.bounds;
                        r = Mathf.Max(b.extents.x, b.extents.z);
                    }
                }
            }

            telegraphRadius = Mathf.Clamp(r, 0.75f, 4.0f);
        }



        // preview telegraph for ALL throws (supports GroundRing OR URPDecalTelegraph)
        bool previewSpawned = false;
        if (groundRingPrefab != null)
        {
            var tele = ProjectilePool.Instance.Get(groundRingPrefab);
            if (tele != null)
            {
                // EXACT landing point + forced rotation
                tele.transform.position = interceptPos;
                tele.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
                tele.SetActive(true);

                float holdSeconds = Mathf.Max(0f, interceptTime - 0.05f);

                // NEW: URP Decal projector telegraph path
                var decalTele = tele.GetComponent<URPDecalTelegraph>();
                if (decalTele != null)
                {
                    decalTele.SetWorldPose(interceptPos); // sets pos + rotation again (safe)
                    decalTele.Play(
                        radius: telegraphRadius,
                        seconds: holdSeconds,
                        onComplete: () => ProjectilePool.Instance.Return(groundRingPrefab, tele)
                    );
                    previewSpawned = true;
                }
                else
                {
                    // Legacy GroundRing path
                    var gr = tele.GetComponent<GroundRing>();
                    if (gr != null)
                    {
                        gr.Play(
                            telegraphRadius,
                            onComplete: () => ProjectilePool.Instance.Return(groundRingPrefab, tele),
                            holdOverride: holdSeconds
                        );
                        previewSpawned = true;
                    }
                    else
                    {
                        // If it's neither, don't silently insta-return in case it has its own visuals
                        // Keep it alive for holdSeconds then return.
                        StartCoroutine(ReturnTelegraphLater(groundRingPrefab, tele, Mathf.Max(0.1f, holdSeconds)));
                        previewSpawned = true;
                    }
                }
            }
        }




        // spawn projectile
        SpawnProjectile(
            origin,
            interceptPos,
            (interceptPos - origin).normalized,
            explosive,
            chosenPrefab,
            finalSpeed,
            test,
            interceptTime,
            previewSpawned,
            distanceNorm
        );

        _queueLastSpawn.Record(interceptPos, chosenPrefab.name);

        // cooldown
        float baseCd = Mathf.Lerp(spawnCooldownBase, minSpawnCooldown, distanceNorm);
        float jitter = UnityEngine.Random.Range(spawnCooldownRandomRange.x, spawnCooldownRandomRange.y);
        _cooldown = Mathf.Max(minSpawnCooldown, baseCd) * jitter;

        if (debugDraw)
        {
            Debug.Log($"[ThrownObstacleDirector] aim={(aim.isTrueHit ? "HIT" : "MISS")} acc={aim.accuracy:0.000} distNorm={distanceNorm:0.00} explosive={explosive}");
        }
    }

    private AimDecision DecideAim(float distanceNorm)
    {
        float curveMul = (accuracyByDistance != null) ? accuracyByDistance.Evaluate(distanceNorm) : 1f;
        float acc = Mathf.Clamp01(baseAccuracy * curveMul);

        // Interpret accuracy as "chance to attempt a true hit"
        bool trueHit = UnityEngine.Random.value < acc;

        return new AimDecision { isTrueHit = trueHit, accuracy = acc };
    }

    private void ApplyMissOffset(ref Vector3 interceptPos, Vector3 right, Vector3 spawnFwd, float accuracy, float distanceNorm)
    {
        // When accuracy is low, miss offsets should be stronger.
        float missScale = (1f - accuracy) * Mathf.Lerp(0.6f, 1.4f, distanceNorm);

        float lateral = UnityEngine.Random.Range(-maxMissLateral, maxMissLateral) * missScale;
        float forward = UnityEngine.Random.Range(-maxMissForward, maxMissForward) * missScale;

        interceptPos += right * lateral;
        interceptPos += spawnFwd * forward;
    }

    private void SpawnProjectile(Vector3 origin, Vector3 landPoint, Vector3 aimDir, bool explosive, GameObject chosenPrefab, float speed, bool test, float timeToLanding, bool previewSpawned, float distanceNorm)
    {
        if (chosenPrefab == null) return;

        var go = ProjectilePool.Instance.Get(chosenPrefab);
        if (go == null) return;

        go.transform.position = origin;
        go.transform.rotation = Quaternion.LookRotation((landPoint - origin).normalized, Vector3.up);

        // reset pooled scale to prefab baseline
        if (!_prefabBaseScales.TryGetValue(chosenPrefab, out Vector3 baseScale))
        {
            baseScale = chosenPrefab.transform.localScale;
            _prefabBaseScales[chosenPrefab] = baseScale;
        }
        go.transform.localScale = baseScale;

        // size variation
        float baseSize = UnityEngine.Random.Range(projectileSizeRange.x, projectileSizeRange.y);
        float gain = 1f + sizeGainOverDistance * distanceNorm;
        float finalSize = baseSize * gain;
        go.transform.localScale = baseScale * finalSize;

        go.SetActive(true);

        var ob = go.GetComponent<ThrownObstacle>();
        if (ob == null) ob = go.AddComponent<ThrownObstacle>();

        ob.Initialize(
            director: this,
            spawnPos: origin,
            landPos: landPoint,
            speed: speed,
            arcHeight: arcHeight,
            explosive: explosive,
            explosionRadius: explosionRadius,
            explosionImpulse: explosionKnockback,
            hitLayers: hitLayers,
            prefabReference: chosenPrefab,
            ringPrefab: groundRingPrefab,
            rewardOnDestroy: destroyReward,
            previewRingSpawned: previewSpawned
        );

        _active.Add(ob);

        if (debugDraw)
        {
            Debug.DrawLine(origin, landPoint, explosive ? Color.red : Color.yellow, 8f);
            if (test) Debug.Log($"[ThrownObstacleDirector] Spawned {(explosive ? "Explosive" : "Plain")} speed={speed:F2} size={finalSize:F2} time={timeToLanding:F2}");
        }
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
        forward = (b - a).normalized;
        if (forward.sqrMagnitude < 1e-6f) forward = transform.forward;

        return true;
    }

    private int ScaleConcurrentByTrackProgress(int baseVal)
    {
        if (concurrentScaleByProgress <= 0f) return baseVal;

        var distanceMeter = FindObjectOfType<TrackDistanceMeter>();
        float norm = 0f;
        if (distanceMeter != null && trackGenerator != null)
        {
            float total = ComputeTrackTotalLength();
            norm = Mathf.Clamp01(distanceMeter.DistanceAlongTrack / Mathf.Max(1f, total));
        }

        int extra = Mathf.FloorToInt(norm * concurrentScaleByProgress);
        return Mathf.Clamp(baseVal + extra, 1, 8);
    }

    private bool TryComputeIntercept(
        Vector3 shooterPos,
        Vector3 targetPos,
        Vector3 targetVel,
        float projectileSpeedLimit,
        float maxTravel,
        out Vector3 interceptPos,
        out float interceptTime)
    {
        interceptPos = Vector3.zero;
        interceptTime = 0f;

        float s = Mathf.Max(0.001f, projectileSpeedLimit);
        Vector3 r = targetPos - shooterPos;
        float c = r.sqrMagnitude;
        if (c < 1e-6f) return false;

        if (targetVel.sqrMagnitude < 1e-6f)
        {
            float dist = Mathf.Sqrt(c);
            if (dist > maxTravel) return false;
            interceptPos = targetPos;
            interceptTime = dist / s;
            return true;
        }

        float v2 = targetVel.sqrMagnitude;
        float a = v2 - s * s;
        float b = 2f * Vector3.Dot(targetVel, r);

        float t;
        if (Mathf.Abs(a) < 1e-6f)
        {
            if (Mathf.Abs(b) < 1e-6f) return false;
            t = -c / b;
            if (t <= 0f) return false;
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

        float travel = s * t;
        if (travel > maxTravel) return false;

        interceptPos = targetPos + targetVel * t;
        interceptTime = t;
        return true;
    }

    // Path-based predictive intercept solver
    private bool TryComputePathPredictiveIntercept(Vector3 origin, float projectileSpeedAllowance, out Vector3 interceptPos, out float interceptTime)
    {
        interceptPos = Vector3.zero;
        interceptTime = 0f;

        if (trackGenerator == null || playerTransform == null)
            return false;

        var distMeter = FindObjectOfType<TrackDistanceMeter>();
        if (distMeter == null)
            return false;

        float carDist = distMeter.DistanceAlongTrack;

        float carSpeed = 0f;
        if (carController != null)
            carSpeed = carController.CurrentSpeed;
        else
        {
            var rb = playerTransform.GetComponent<Rigidbody>();
            if (rb != null)
                carSpeed = Mathf.Max(0f, Vector3.Dot(rb.velocity, playerTransform.forward));
        }

        float minProjectileSpeed = Mathf.Max(0.001f, projectileSpeedAllowance * 0.25f);
        float maxProjectileSpeed = Mathf.Max(minProjectileSpeed, projectileSpeedAllowance * 3.0f);

        Vector3 carPos = playerTransform.position;
        float initialHoriz = Vector3.Distance(new Vector3(origin.x, 0f, origin.z), new Vector3(carPos.x, 0f, carPos.z));
        float guessSpeed = Mathf.Max(minProjectileSpeed, Mathf.Min(maxProjectileSpeed, projectileSpeedAllowance));
        float t = Mathf.Clamp(initialHoriz / Mathf.Max(0.001f, guessSpeed), 0.05f, 18f);

        const int maxIter = 20;
        const float eps = 0.005f;

        for (int i = 0; i < maxIter; i++)
        {
            float predictedCarDist = carDist + carSpeed * t;

            if (!TrySamplePositionAtDistance(predictedCarDist, out Vector3 predictedPos, out _))
                return false;

            float horiz = Vector3.Distance(new Vector3(origin.x, 0f, origin.z), new Vector3(predictedPos.x, 0f, predictedPos.z));
            if (horiz < 1e-4f)
            {
                interceptPos = predictedPos;
                interceptTime = 0f;
                return true;
            }

            float unclampedSpeed = horiz / Mathf.Max(1e-5f, t);
            float usedSpeed = Mathf.Clamp(unclampedSpeed, minProjectileSpeed, maxProjectileSpeed);
            float tNew = horiz / usedSpeed;

            if (!float.IsFinite(tNew) || float.IsNaN(tNew)) return false;

            if (Mathf.Abs(tNew - t) < eps)
            {
                interceptPos = predictedPos;
                interceptTime = tNew;
                return true;
            }

            t = Mathf.Lerp(t, tNew, 0.85f);
            t = Mathf.Clamp(t, 0.01f, 18f);
        }

        float finalPred = carDist + carSpeed * t;
        if (!TrySamplePositionAtDistance(finalPred, out Vector3 finalPos, out _))
            return false;

        interceptPos = finalPos;
        interceptTime = t;
        return true;
    }

    public void SetCar(CarController car)
    {
        playerTransform = car.transform;
        carController = car;
    }

    internal void NotifyProjectileStopped(ThrownObstacle ob)
    {
        if (_active.Contains(ob)) _active.Remove(ob);
    }

    internal void NotifyProjectileCloseCall(ThrownObstacle ob, float closestDistance)
    {
        var gm = GameManager_Racing.Instance;
        if (gm != null)
            gm.HandleProjectileCloseCall(ob.transform.position, closestDistance);
    }

    internal void NotifyProjectileExploded(ThrownObstacle ob, Vector3 position, float radius)
    {
        var gm = GameManager_Racing.Instance;
        if (gm != null)
            gm.HandleProjectileExplosion(position, radius);
    }

    private System.Collections.IEnumerator ReturnTelegraphLater(GameObject prefab, GameObject instance, float delay)
    {
        yield return new WaitForSeconds(delay);
        if (instance != null && prefab != null && ProjectilePool.Instance != null)
            ProjectilePool.Instance.Return(prefab, instance);
    }

    public void ForceSpawnAt(Vector3 origin, Vector3 landing, bool explosive)
    {
        Vector3 flatOrigin = new Vector3(origin.x, 0f, origin.z);
        Vector3 flatLanding = new Vector3(landing.x, 0f, landing.z);
        float horizDist = Vector3.Distance(flatOrigin, flatLanding);
        float speed = Mathf.Max(1f, baseProjectileSpeed);
        if (horizDist > 0.01f) speed = Mathf.Clamp(horizDist / 1.2f, baseProjectileSpeed * 0.5f, baseProjectileSpeed * 2f);

        GameObject chosen = explosive ? (projectilePrefabExplosive ?? projectilePrefabPlain) : projectilePrefabPlain;
        float approxTime = horizDist / Mathf.Max(0.001f, speed);
        SpawnProjectile(origin, landing, (landing - origin).normalized, explosive, chosen, speed, test: true, timeToLanding: approxTime, previewSpawned: false, distanceNorm: 0.5f);
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
