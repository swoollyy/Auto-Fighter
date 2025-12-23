using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Director that spawns deterministic thrown obstacles ahead of the player.
/// - Uses track sampling from ProceduralTrackGenerator.PathPoints
/// - Predictive intercept (constant velocity)
/// - Deterministic, non-gravity arc (Rigidbody moved via MovePosition so collisions still happen)
/// - Pooling via ProjectilePool
/// - Supports plain-impact and explosive variants (explosive shows ground ring)
/// - Debug gizmos and spawn hotkey
/// </summary>
[DisallowMultipleComponent]
public class ThrownObstacleDirector : MonoBehaviour
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

    // NEW: gate spawning until player reaches a fraction of track progress (0..1)
    [Header("Spawn Gate")]
    [SerializeField, Range(0f, 0.5f)] private float spawnEnableProgress = 0.10f; // 10% default

    // NEW: cooldown scaling & randomness
    [Header("Spawn Cooldown Scaling")]
    [SerializeField, Min(0.05f)] private float minSpawnCooldown = 0.6f;
    [SerializeField] private Vector2 spawnCooldownRandomRange = new Vector2(0.85f, 1.15f);

    [Header("Projectile Defaults")]
    [SerializeField] private float baseProjectileSpeed = 18f; // used as initial guess only
    [SerializeField] private float travelAllowanceMultiplier = 1.05f; // allow slightly beyond theoretical range
    [SerializeField] private float arcHeight = 3f;
    [SerializeField] private LayerMask hitLayers = ~0; // what projectile collides with (car, road, obstacles)
    [SerializeField] private bool explosiveByDefault = false;
    [SerializeField] private float explosionRadius = 6f;
    [SerializeField] private float explosionKnockback = 12f;

    // NEW: projectile size variation (base range) and growth over distance
    [Header("Projectile Size Variation")]
    [SerializeField, Min(0f)] private Vector2 projectileSizeRange = new Vector2(0.92f, 1.12f);
    [SerializeField, Range(0f, 1f)] private float sizeGainOverDistance = 0.25f; // additional scale at end of track

    // NEW: speed variance (more extreme) applied after initial intercept estimate; prediction refined with chosen speed
    [Header("Projectile Speed Variation")]
    [SerializeField, Min(0.1f)] private Vector2 speedRandomMultiplierRange = new Vector2(0.5f, 2.2f);

    [Header("Spawn Placement")]
    [SerializeField, Min(0f)] private float spawnSideOffset = 2.0f; // lateral offset from center of road
    [SerializeField, Min(0f)] private float spawnHeight = 2.0f;     // height above road
    [SerializeField, Min(0f)] private float minLandingDistanceFromPlayer = 4.0f; // prevents spawning virtually on top of player
    [SerializeField, Min(0f)] private float minLeadDistance = 6.0f; // never pick too small lead

    [Header("Spawn Placement (Close-Landing Policy)")]
    [Tooltip("If true, don't skip spawns that would land within minLandingDistanceFromPlayer; instead clamp the landing point forward of the player.")]
    [SerializeField] private bool allowCloseLandings = true;

    [Header("Spawn Variance")]
    [SerializeField, Range(0f, 3f)] private float lateralJitter = 1.0f;
    [SerializeField, Range(0f, 5f)] private float forwardJitter = 1.8f;

    [Header("Rewards")]
    [Tooltip("Currency awarded to player for destroying a projectile")]
    [SerializeField] private int destroyReward = 12;

    [Header("Debug / Tuning")]
    [SerializeField] private bool debugDraw = false;
    [SerializeField] private KeyCode spawnTestKey = KeyCode.T;

    // ---- New: Accuracy & Explosion scaling ----
    [Header("Accuracy / Misses")]
    [Tooltip("Base accuracy (0..1). 1 = perfect aim, 0 = always miss.")]
    [SerializeField, Range(0f, 1f)] private float baseAccuracy = 0.92f;
    [Tooltip("Curve mapping normalized distance along track (0..1) to a multiplier applied to baseAccuracy.")]
    [SerializeField] private AnimationCurve accuracyByDistance = AnimationCurve.Linear(0, 1, 1, 1);
    [Tooltip("Maximum lateral miss offset (meters) applied when a shot misses; scaled by (1-accuracy).")]
    [SerializeField, Min(0f)] private float maxMissLateral = 4f;
    [Tooltip("Maximum forward/back miss offset (meters) applied when a shot misses; scaled by (1-accuracy).")]
    [SerializeField, Min(0f)] private float maxMissForward = 6f;

    [Header("Explosion Frequency")]
    [Tooltip("Base spawn chance for explosive variant (0..1).")]
    [SerializeField, Range(0f, 1f)] private float explosionBaseChance = 0.06f;
    [Tooltip("Curve mapping normalized distance along track (0..1) to multiplier applied to explosionBaseChance.")]
    [SerializeField] private AnimationCurve explosionChanceByDistance = AnimationCurve.Linear(0, 0.5f, 1, 1.5f);

    private float _cooldown;
    private readonly List<ThrownObstacle> _active = new();
    private readonly System.Random _rng = new System.Random();

    void Awake()
    {
        if (!trackGenerator) trackGenerator = FindObjectOfType<ProceduralTrackGenerator>();
        if (!playerTransform && GameManager_Racing.Instance != null)
            playerTransform = GameManager_Racing.Instance.ActiveCar?.transform;
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

        // remove dead entries early so concurrency checks are accurate
        _active.RemoveAll(x => x == null || !x.gameObject.activeInHierarchy);

        // compute allowed concurrent scaled by track progress (safe fallback to 0->1)
        int allowedConcurrent = ScaleConcurrentByTrackProgress(maxConcurrent);

        _cooldown -= Time.deltaTime;
        if (_cooldown <= 0f && _active.Count < allowedConcurrent)
        {
            TrySpawn();
            // cooldown now computed inside TrySpawn (supports distance-based scaling). If TrySpawn skipped due to gating, avoid immediate retry spam:
            if (_cooldown <= 0f) // if TrySpawn didn't set cooldown (skipped), set small safety cooldown
                _cooldown = 0.5f;
        }

        if (debugDraw && Input.GetKeyDown(spawnTestKey))
            TrySpawn(test: true);
    }

    private void TrySpawn(bool test = false)
    {
        if (!trackGenerator || playerTransform == null) return;
        if (projectilePrefabPlain == null && projectilePrefabExplosive == null) return;

        // choose lead distance with jitter and enforce minLeadDistance (lead is in meters)
        float lead = Mathf.Lerp(leadDistanceRange.x, leadDistanceRange.y, UnityEngine.Random.value);
        lead += UnityEngine.Random.Range(-forwardJitter, forwardJitter);
        lead = Mathf.Max(lead, minLeadDistance);

        // sample forward position on the track to pick a spawn band (ahead of player)
        float playerDist = 0f;
        var distanceMeter = FindObjectOfType<TrackDistanceMeter>();
        if (distanceMeter != null) playerDist = distanceMeter.DistanceAlongTrack;

        // sSpawnMeters is a physical distance (meters) along track
        float sSpawnMeters = Mathf.Max(0f, playerDist + lead);

        // small guard: compute total track length
        float trackTotal = ComputeTrackTotalLength();

        // NEW: hard gate — only allow spawning after the player has progressed at least spawnEnableProgress fraction
        if (trackTotal > 0f)
        {
            float playerProgress = Mathf.Clamp01(playerDist / trackTotal);
            if (playerProgress < spawnEnableProgress)
            {
                if (debugDraw) Debug.Log($"[ThrownObstacleDirector] spawn gated until {spawnEnableProgress * 100f:0}% progress (current {playerProgress * 100f:0}%).");
                // set a small randomized cooldown so we don't hammer TrySpawn every frame while still under gate
                _cooldown = Mathf.Lerp(spawnCooldownBase, minSpawnCooldown, playerProgress) * UnityEngine.Random.Range(spawnCooldownRandomRange.x, spawnCooldownRandomRange.y);
                return;
            }
        }

        // Use the new TrySamplePositionAtDistance helper (one-line change per request)
        if (!TrySamplePositionAtDistance(sSpawnMeters, out Vector3 spawnCenter, out Vector3 spawnFwd))
            return;

        // pick an origin slightly off the road side and up
        Vector3 right = Vector3.Cross(Vector3.up, spawnFwd).normalized;
        float sideSign = (UnityEngine.Random.value < 0.5f) ? -1f : 1f;
        float sideOffset = spawnSideOffset + UnityEngine.Random.Range(-lateralJitter, lateralJitter);
        Vector3 origin = spawnCenter + right * sideSign * sideOffset + Vector3.up * spawnHeight;

        // compute intercept (position + time) using PATH‑BASED predictive solver that follows the track
        Vector3 interceptPos;
        float interceptTime;

        // allow a slightly larger speed allowance for solver (used as a max envelope)
        float speedAllowance = baseProjectileSpeed * travelAllowanceMultiplier;

        // First: try path-based prediction that advances the car along the track by carSpeed*t
        bool found = TryComputePathPredictiveIntercept(origin, speedAllowance, out interceptPos, out interceptTime);

        // If path-based fails, fallback to the legacy constant-velocity solver using rigidbody velocity
        if (!found)
        {
            Vector3 targetPos = playerTransform.position;
            Rigidbody carRb = playerTransform.GetComponent<Rigidbody>();
            Vector3 targetVel = carRb != null ? carRb.velocity : Vector3.zero;

            if (!TryComputeIntercept(origin, targetPos, targetVel, speedAllowance, speedAllowance * 10f, out interceptPos, out interceptTime))
            {
                // fallback: aim slightly ahead of the player using forward vector
                interceptPos = targetPos + playerTransform.forward * Mathf.Clamp(baseProjectileSpeed * 0.65f, 6f, 30f);
                float horizDist = Vector3.Distance(new Vector3(origin.x, 0f, origin.z), new Vector3(interceptPos.x, 0f, interceptPos.z));
                interceptTime = Mathf.Clamp(horizDist / Mathf.Max(2f, baseProjectileSpeed), 0.25f, 6f);
            }
        }

        // guard: don't land too close to player
        float landingDistToPlayer = Vector3.Distance(interceptPos, playerTransform.position);
        if (landingDistToPlayer < minLandingDistanceFromPlayer)
        {
            if (allowCloseLandings)
            {
                // clamp landing point to be at least minLandingDistanceFromPlayer in front of player
                Vector3 dir = (interceptPos - playerTransform.position);
                // if intercept exactly at player position, use player's forward
                if (dir.sqrMagnitude < 1e-6f)
                    dir = playerTransform.forward;
                dir.y = 0f;
                if (dir.sqrMagnitude < 1e-6f)
                    dir = Vector3.forward;
                dir.Normalize();

                interceptPos = playerTransform.position + dir * minLandingDistanceFromPlayer;

                // small lateral jitter to avoid perfect center
                interceptPos += right * UnityEngine.Random.Range(-Mathf.Min(lateralJitter, 1.2f), Mathf.Min(lateralJitter, 1.2f));

                if (debugDraw) Debug.Log($"[ThrownObstacleDirector] close landing allowed — clamped landing to {interceptPos:F3} (dist={minLandingDistanceFromPlayer:F2})");
            }
            else
            {
                if (debugDraw) Debug.Log($"[ThrownObstacleDirector] skip spawn — landing too close: {landingDistToPlayer:F2} < {minLandingDistanceFromPlayer:F2}");
                return;
            }
        }

        // jitter landing laterally for variety but keep consistent small
        interceptPos += right * UnityEngine.Random.Range(-Mathf.Min(lateralJitter, 1.2f), Mathf.Min(lateralJitter, 1.2f));

        // ---- New: distance-normalized modifiers used for accuracy & explosion weighting ----
        float distanceNorm = 0f;
        if (trackTotal > 0f)
            distanceNorm = Mathf.Clamp01(sSpawnMeters / trackTotal);

        // Accuracy: may miss. Higher accuracy -> less lateral error.
        float accuracy = Mathf.Clamp01(baseAccuracy * (accuracyByDistance != null ? accuracyByDistance.Evaluate(distanceNorm) : 1f));
        float missChance = 1f - accuracy;
        bool didMiss = UnityEngine.Random.value < missChance;
        if (didMiss)
        {
            float missScale = (1f - accuracy) * Mathf.Lerp(0.6f, 1.4f, distanceNorm);

            float lateral = UnityEngine.Random.Range(-maxMissLateral, maxMissLateral) * missScale;
            interceptPos += right * lateral;

            float forward = UnityEngine.Random.Range(-maxMissForward, maxMissForward) * missScale;
            interceptPos += spawnFwd * forward;

            if (debugDraw)
                Debug.DrawLine(interceptPos, interceptPos + Vector3.up * 1f, Color.magenta, 6f);
        }
        else
        {
            float micro = 0.15f * (1f - accuracy);
            interceptPos += spawnFwd * UnityEngine.Random.Range(-micro, micro);
        }

        // Explosion selection: chance increases with distance via curve
        float explosionChanceMultiplier = explosionChanceByDistance != null ? explosionChanceByDistance.Evaluate(distanceNorm) : 1f;
        float explosionChance = Mathf.Clamp01(explosionBaseChance * explosionChanceMultiplier);
        bool explosive = UnityEngine.Random.value < explosionChance || explosiveByDefault;

        // select prefab
        GameObject chosenPrefab = explosive ? (projectilePrefabExplosive ?? projectilePrefabPlain) : projectilePrefabPlain;
        if (chosenPrefab == null) return;

        // compute horizontal distance and set projectile speed so it arrives at interceptTime
        Vector3 flatOrigin = new Vector3(origin.x, 0f, origin.z);
        Vector3 flatIntercept = new Vector3(interceptPos.x, 0f, interceptPos.z);
        float horizDistance = Vector3.Distance(flatOrigin, flatIntercept);

        // initial unclamped speed estimate
        float finalSpeed = (interceptTime > 0f) ? (horizDistance / Mathf.Max(0.001f, interceptTime)) : baseProjectileSpeed;

        // apply more extreme randomness to speed now (user requested more extreme randomness)
        float speedRand = UnityEngine.Random.Range(speedRandomMultiplierRange.x, speedRandomMultiplierRange.y);
        finalSpeed *= speedRand;

        // clamp finalSpeed to reasonable bounds to avoid extreme values
        finalSpeed = Mathf.Clamp(finalSpeed, baseProjectileSpeed * 0.25f, baseProjectileSpeed * 3.0f);

        // REFINEMENT: recompute intercept using the *actual* finalSpeed we will use so time matches speed
        bool reRefined = TryComputePathPredictiveIntercept(origin, finalSpeed, out Vector3 refinedPos, out float refinedTime);
        if (reRefined)
        {
            interceptPos = refinedPos;
            interceptTime = refinedTime;
            // recompute horizDistance and finalSpeed based on refined values
            flatIntercept = new Vector3(interceptPos.x, 0f, interceptPos.z);
            horizDistance = Vector3.Distance(flatOrigin, flatIntercept);
            finalSpeed = (interceptTime > 0f) ? (horizDistance / Mathf.Max(0.001f, interceptTime)) : finalSpeed;
            // keep within clamp
            finalSpeed = Mathf.Clamp(finalSpeed, baseProjectileSpeed * 0.25f, baseProjectileSpeed * 3.0f);
        }
        else
        {
            if (debugDraw) Debug.Log("[ThrownObstacleDirector] Refinement with randomized speed failed; using pre-randomized estimate.");
        }

        // If we have a ground ring prefab and projectile is explosive, spawn a preview ring that lasts until arrival
        bool previewSpawned = false;
        if (explosive && groundRingPrefab != null)
        {
            var ring = ProjectilePool.Instance.Get(groundRingPrefab);
            if (ring != null)
            {
                // position BEFORE activation, then activate and play (use refined interceptTime)
                ring.transform.position = interceptPos + Vector3.up * 0.05f;
                ring.transform.rotation = Quaternion.identity;
                ring.SetActive(true);

                var gr = ring.GetComponent<GroundRing>();
                if (gr != null)
                {
                    // set hold equal to interceptTime (plus small buffer) so preview persists until arrival
                    float holdSeconds = Mathf.Max(0f, interceptTime - 0.05f);
                    gr.Play(explosionRadius, onComplete: () => ProjectilePool.Instance.Return(groundRingPrefab, ring), holdOverride: holdSeconds);
                    previewSpawned = true;
                }
                else
                {
                    // no GroundRing behavior -> return to pool immediately
                    ProjectilePool.Instance.Return(groundRingPrefab, ring);
                }
            }
        }

        // --- PATCH: project onto road *before* adding vertical spawn height ---
        // Use the track center as the projection anchor, then rebuild origin above ground.
        LayerMask roadMask = LayerMask.GetMask("RoadSurface");

        // Project the center of the lane down to the road
        Vector3 groundCenter = SpawnUtils.ProjectOntoSurface(spawnCenter, 2f, 25f, roadMask);

        // Project the landing point onto the road
        Vector3 groundLanding = SpawnUtils.ProjectOntoSurface(interceptPos, 2f, 25f, roadMask);

        // Rebuild origin so it’s offset sideways + up from the *grounded* center
        origin = groundCenter + right * sideSign * sideOffset + Vector3.up * spawnHeight;

        // Use the grounded landing point
        interceptPos = groundLanding;

        // Spawn
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
        // compute cooldown scaled by distance travelled (decreases as distance increases) with some jitter
        float baseCd = Mathf.Lerp(spawnCooldownBase, minSpawnCooldown, distanceNorm);
        float jitter = UnityEngine.Random.Range(spawnCooldownRandomRange.x, spawnCooldownRandomRange.y);
        _cooldown = Mathf.Max(minSpawnCooldown, baseCd) * jitter;
    }

    private void SpawnProjectile(Vector3 origin, Vector3 landPoint, Vector3 aimDir, bool explosive, GameObject chosenPrefab, float speed, bool test, float timeToLanding, bool previewSpawned, float distanceNorm)
    {
        if (chosenPrefab == null) return;

        var go = ProjectilePool.Instance.Get(chosenPrefab);
        if (go == null) return;

        // Position and rotate before activation to avoid any visible 'pop' at an unexpected world pos
        go.transform.position = origin;
        go.transform.rotation = Quaternion.LookRotation((landPoint - origin).normalized, Vector3.up);

        // Apply size variation: base random within range then scale up slightly with distanceNorm
        float baseSize = UnityEngine.Random.Range(projectileSizeRange.x, projectileSizeRange.y);
        float gain = 1f + sizeGainOverDistance * distanceNorm;
        float finalSize = baseSize * gain;
        go.transform.localScale = go.transform.localScale * finalSize;

        // Now activate
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
            previewRingSpawned: previewSpawned // tell projectile we already previewed the ring
        );

        _active.Add(ob);

        if (debugDraw)
        {
            Debug.DrawLine(origin, landPoint, explosive ? Color.red : Color.yellow, 8f);
            if (test) Debug.Log($"[ThrownObstacleDirector] Spawned {(explosive ? "Explosive" : "Plain")} projectile from {origin:F3} -> {landPoint:F3} with speed {speed:F2} (size={finalSize:F2}) timeToLanding={timeToLanding:F2}");
        }
    }

    private bool TrySamplePositionAtDistance(float distanceAlongTrackMeters, out Vector3 pos, out Vector3 forward)
    {
        pos = transform.position;
        forward = transform.forward;

        var pts = trackGenerator?.PathPoints;
        if (pts == null || pts.Count < 2)
            return false;

        // Build cumulative lengths across the PathPoints (safe and accurate)
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

        // Clamp distance to valid range
        distanceAlongTrackMeters = Mathf.Clamp(distanceAlongTrackMeters, 0f, total);

        // Find segment index where cumulative distance crosses our target
        int idx = 0;
        // Linear scan is fine here (path count is reasonable); could be binary-searched if needed
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

    // Protective intercept similar to CarTurretController.TryComputeLeadDirection (constant velocity)
    private Vector3 ComputeProtectiveAim(Vector3 origin, Vector3 carForward, out Vector3 landingPoint)
    {
        // Try to compute a proper intercept (position + time) for the moving car and return aim dir.
        landingPoint = origin + carForward * (baseProjectileSpeed * 1.0f);

        var car = playerTransform;
        Vector3 forward = carForward.normalized;
        Rigidbody carRb = car.GetComponent<Rigidbody>();
        Vector3 carVel = carRb != null ? carRb.velocity : Vector3.zero;
        Vector3 targetPos = car.position;

        float speedAllowance = baseProjectileSpeed * travelAllowanceMultiplier;
        if (TryComputeIntercept(origin, targetPos, carVel, speedAllowance, speedAllowance * 10f, out Vector3 interceptPos, out float interceptTime))
        {
            landingPoint = interceptPos;
            // aim toward intercept
            Vector3 aimDir = (interceptPos - origin).normalized;
            return aimDir;
        }
        else
        {
            // fallback: aim ahead by car forward
            landingPoint = targetPos + forward * Mathf.Clamp(baseProjectileSpeed * 0.65f, 6f, 30f);
            return (landingPoint - origin).normalized;
        }
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

    public void SetCar(CarController car)
    {
        playerTransform = car.transform;
        carController = car;
    }

    // Called by projectiles when they deactivate so director can track concurrent count
    internal void NotifyProjectileStopped(ThrownObstacle ob)
    {
        if (_active.Contains(ob)) _active.Remove(ob);
    }

    // Called by projectile when it registers a close-call (near miss)
    internal void NotifyProjectileCloseCall(ThrownObstacle ob, float closestDistance)
    {
        // Forward to GameManager for audio/FX/shake/slowmo handling
        var gm = GameManager_Racing.Instance;
        if (gm != null)
        {
            gm.HandleProjectileCloseCall(ob.transform.position, closestDistance);
        }
    }

    // Optional helper for callers who want immediate explosion notification (director -> GM)
    internal void NotifyProjectileExploded(ThrownObstacle ob, Vector3 position, float radius)
    {
        var gm = GameManager_Racing.Instance;
        if (gm != null)
        {
            gm.HandleProjectileExplosion(position, radius);
        }
    }

    // NEW: Path-based predictive intercept solver.
    // Advances the car along the track by carSpeed * t and iteratively solves for t where projectile_time == t.
    // This version respects the projectile speed passed in so the solver's time estimate matches the final clamped speed.
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

        // Car forward speed (m/s). Use CarController.CurrentSpeed when available; fallback to Rigidbody projection.
        float carSpeed = 0f;
        if (carController != null)
            carSpeed = carController.CurrentSpeed;
        else
        {
            var rb = playerTransform.GetComponent<Rigidbody>();
            if (rb != null)
                carSpeed = Mathf.Max(0f, Vector3.Dot(rb.velocity, playerTransform.forward)); // forward component
            else
                carSpeed = 0f;
        }

        // Speed bounds we will actually use for projectile (must mirror clamping used in Spawn)
        float minProjectileSpeed = Mathf.Max(0.001f, projectileSpeedAllowance * 0.25f);
        float maxProjectileSpeed = Mathf.Max(minProjectileSpeed, projectileSpeedAllowance * 3.0f);

        // initial t guess: use conservative slow projectile so predicted car advance is larger (helps avoid aiming behind)
        Vector3 carPos = playerTransform.position;
        float initialHoriz = Vector3.Distance(new Vector3(origin.x, 0f, origin.z), new Vector3(carPos.x, 0f, carPos.z));
        float guessSpeed = Mathf.Max(minProjectileSpeed, Mathf.Min(maxProjectileSpeed, projectileSpeedAllowance));
        float t = Mathf.Clamp(initialHoriz / Mathf.Max(0.001f, guessSpeed), 0.05f, 18f);

        const int maxIter = 20;
        const float eps = 0.005f;

        for (int i = 0; i < maxIter; i++)
        {
            float predictedCarDist = carDist + carSpeed * t;
            // Use the local TrySamplePositionAtDistance helper
            bool sampled = TrySamplePositionAtDistance(predictedCarDist, out Vector3 predictedPos, out _);

            if (!sampled) return false;

            float horiz = Vector3.Distance(new Vector3(origin.x, 0f, origin.z), new Vector3(predictedPos.x, 0f, predictedPos.z));
            if (horiz < 1e-4f)
            {
                interceptPos = predictedPos;
                interceptTime = 0f;
                return true;
            }

            // proposed unclamped speed if we wanted the current t to be exact
            float unclampedSpeed = horiz / Mathf.Max(1e-5f, t);

            // clamp to the allowed projectile speed window (use provided allowance as central value)
            float usedSpeed = Mathf.Clamp(unclampedSpeed, minProjectileSpeed, maxProjectileSpeed);

            // Now compute the time t that would result with that usedSpeed
            float tNew = horiz / usedSpeed;

            if (!float.IsFinite(tNew) || float.IsNaN(tNew)) return false;

            if (Mathf.Abs(tNew - t) < eps)
            {
                interceptPos = predictedPos;
                interceptTime = tNew;
                return true;
            }

            // Relaxed update for stability
            t = Mathf.Lerp(t, tNew, 0.85f);
            t = Mathf.Clamp(t, 0.01f, 18f);
        }

        // fallback: return last estimate
        float finalPred = carDist + carSpeed * t;
        if (!TrySamplePositionAtDistance(finalPred, out Vector3 finalPos, out _))
            return false;

        interceptPos = finalPos;
        interceptTime = t;
        return true;
    }

    // Optional helper for external spawn triggers
    public void ForceSpawnAt(Vector3 origin, Vector3 landing, bool explosive)
    {
        // compute speed using default baseProjectileSpeed (arrive in approx distance/speed)
        Vector3 flatOrigin = new Vector3(origin.x, 0f, origin.z);
        Vector3 flatLanding = new Vector3(landing.x, 0f, landing.z);
        float horizDist = Vector3.Distance(flatOrigin, flatLanding);
        float speed = Mathf.Max(1f, baseProjectileSpeed);
        if (horizDist > 0.01f) speed = Mathf.Clamp(horizDist / 1.2f, baseProjectileSpeed * 0.5f, baseProjectileSpeed * 2f);
        GameObject chosen = explosive ? (projectilePrefabExplosive ?? projectilePrefabPlain) : projectilePrefabPlain;
        float approxTime = horizDist / Mathf.Max(0.001f, speed);
        // assume mid-track distanceNorm ~ 0.5 for sizing
        SpawnProjectile(origin, landing, (landing - origin).normalized, explosive, chosen, speed, test: true, timeToLanding: approxTime, previewSpawned: false, distanceNorm: 0.5f);
    }

    // small helper to compute approximate total track length
    private float ComputeTrackTotalLength()
    {
        var pts = trackGenerator?.PathPoints;
        if (pts == null || pts.Count < 2) return 0f;
        float total = 0f;
        for (int i = 1; i < pts.Count; i++)
            total += Vector3.Distance(pts[i - 1], pts[i]);
        return total;
    }
}