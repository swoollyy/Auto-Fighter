using System.Collections;
using System.Collections.Generic;
using UnityEngine;


[System.Serializable]
public class ObstacleType
{
    [Header("Identity")]
    public string id;                           // purely for your sanity in the inspector
    public GameObject prefab;                  // actual obstacle prefab

    [Header("Base Weight")]
    [Min(0f)] public float baseWeight = 1f;    // relative weight vs. other types

    [Header("Distance Band (normalized 0–1 along track)")]
    [Range(0f, 1f)] public float startAtNormalizedDist = 0f;
    [Range(0f, 1f)] public float fullWeightNormalizedDist = 0.2f;
    [Range(0f, 1f)] public float stopAtNormalizedDist = 1f;

    [Header("Per-Type Placement Tweaks (optional)")]
    public float extraHeightOffset = 0f;       // good for tall trucks vs low cones
    public float extraLateralPadding = 0f;     // shrink usable width if needed
}

/// <summary>
/// Streams obstacles along the procedural track.
/// Works similarly to TrackCoinSpawner but uses lower spawn frequency and avoids pattern spawning.
/// </summary>
public class TrackObstacleSpawner : MonoBehaviour, ITrackSpawnQueueSource
{
    [Header("References")]
    [SerializeField] private ProceduralTrackGenerator trackGenerator;
    [SerializeField] private Transform playerTransform;
    [SerializeField] private Transform obstacleParent;

    [Header("Spawn Mode")]
    [Tooltip("If true, fills the whole track with obstacles once in InitializeForRun.")]
    [SerializeField] private bool preSpawnOnInitialize = true;

    [Tooltip("If true, will continue spawning ahead while you drive. Turn OFF if you hate seeing pop-in.")]
    [SerializeField] private bool streamSpawnDuringRun = false;

    [Tooltip("When the spawn queue controls this spawner, keep filling slots ahead of the player (baseline props). " +
             "Without this, only the initial pre-spawn window is used and later spawns wait for the queue.")]
    [SerializeField] private bool streamWhileQueueControlled = true;

    [Header("Obstacle Types")]
    [SerializeField] private List<ObstacleType> obstacleTypes = new List<ObstacleType>();

    [Header("Path Sampling")]
    [SerializeField] private bool useSmoothing = true;
    [SerializeField, Min(1)] private int smoothingSubdivisionsPerSegment = 6;

    [Header("Spawn Settings")]
    [Tooltip("Ideal spacing between potential spawn slots (meters).")]
    [SerializeField] private float obstacleSpacing = 40f;
    [SerializeField] private int maxActiveObstacles = 20;

    [Tooltip("Minimum distance in front of the player where new obstacles are allowed to appear.")]
    [SerializeField] private float minSpawnDistanceAhead = 60f;

    [Tooltip("Maximum distance in front of the player we bother filling with obstacles.")]
    [SerializeField] private float maxSpawnDistanceAhead = 160f;


    [Header("Initial Pre-Spawn")]
    [Tooltip("How far ahead (from start) we pre-fill obstacles before the run begins.")]
    [SerializeField] private float initialPreSpawnDistance = 120f;

    [Tooltip("Do not spawn any behind the player.")]
    [SerializeField] private float despawnBehindDistance = 10f;

    [Header("Randomization")]
    [SerializeField] private float distanceJitter = 12f;
    [SerializeField] private float spawnChancePerSlot = 0.45f;

    [Tooltip("Global spawn chance multiplier based on distance (0=start, 1=end).")]
    [SerializeField]
    private AnimationCurve globalSpawnChanceByDistance =
        AnimationCurve.Linear(0f, 0.4f, 1f, 1f);   // starts chill, ramps to 100%

    [SerializeField, Range(0f, 1f)] private float lateralFraction = 0.6f;
    [SerializeField] private float edgeInnerMargin = 0.5f;

    [Header("Spawn Stabilization")]
    [SerializeField] private bool stabilizeRigidbodiesOnSpawn = true;
    [SerializeField, Min(0f)] private float spawnKinematicDuration = 2.0f;
    [SerializeField] private bool disableGravityWhileKinematic = true;

    [Header("Raycast")]
    [SerializeField] private LayerMask roadLayer;
    [SerializeField] private float raycastStartHeight = 6f;
    [SerializeField] private float raycastDownDistance = 20f;
    [SerializeField] private float obstacleHeightOffset = 0.2f;

    [Header("Timing")]
    [SerializeField] private float updateInterval = 0.5f;

    [Header("Debug")]
    [SerializeField] private bool verboseDebug = false;

    // ------------------------------------------------------------------------
    // Per-trial config (TrialConfig). ApplyConfig copies a trial's ObstacleSettings
    // into these fields (call BEFORE InitializeForRun). CaptureConfig snapshots the
    // current values into a settings object (used by the editor baker).
    // ------------------------------------------------------------------------
    public void ApplyConfig(TrialConfig.ObstacleSettings s)
    {
        if (s == null || !s.overrideObstacles) return;

        preSpawnOnInitialize = s.preSpawnOnInitialize;
        streamSpawnDuringRun = s.streamSpawnDuringRun;
        streamWhileQueueControlled = s.streamWhileQueueControlled;

        obstacleTypes = s.obstacleTypes != null ? new List<ObstacleType>(s.obstacleTypes) : new List<ObstacleType>();

        useSmoothing = s.useSmoothing;
        smoothingSubdivisionsPerSegment = s.smoothingSubdivisionsPerSegment;

        obstacleSpacing = s.obstacleSpacing;
        maxActiveObstacles = s.maxActiveObstacles;
        minSpawnDistanceAhead = s.minSpawnDistanceAhead;
        maxSpawnDistanceAhead = s.maxSpawnDistanceAhead;

        initialPreSpawnDistance = s.initialPreSpawnDistance;
        despawnBehindDistance = s.despawnBehindDistance;

        distanceJitter = s.distanceJitter;
        spawnChancePerSlot = s.spawnChancePerSlot;
        globalSpawnChanceByDistance = s.globalSpawnChanceByDistance;
        lateralFraction = s.lateralFraction;
        edgeInnerMargin = s.edgeInnerMargin;

        stabilizeRigidbodiesOnSpawn = s.stabilizeRigidbodiesOnSpawn;
        spawnKinematicDuration = s.spawnKinematicDuration;
        disableGravityWhileKinematic = s.disableGravityWhileKinematic;

        roadLayer = s.roadLayer;
        raycastStartHeight = s.raycastStartHeight;
        raycastDownDistance = s.raycastDownDistance;
        obstacleHeightOffset = s.obstacleHeightOffset;

        updateInterval = s.updateInterval;
        verboseDebug = s.verboseDebug;
    }

    public TrialConfig.ObstacleSettings CaptureConfig()
    {
        return new TrialConfig.ObstacleSettings
        {
            overrideObstacles = true,
            preSpawnOnInitialize = preSpawnOnInitialize,
            streamSpawnDuringRun = streamSpawnDuringRun,
            streamWhileQueueControlled = streamWhileQueueControlled,
            obstacleTypes = obstacleTypes != null ? new List<ObstacleType>(obstacleTypes) : new List<ObstacleType>(),
            useSmoothing = useSmoothing,
            smoothingSubdivisionsPerSegment = smoothingSubdivisionsPerSegment,
            obstacleSpacing = obstacleSpacing,
            maxActiveObstacles = maxActiveObstacles,
            minSpawnDistanceAhead = minSpawnDistanceAhead,
            maxSpawnDistanceAhead = maxSpawnDistanceAhead,
            initialPreSpawnDistance = initialPreSpawnDistance,
            despawnBehindDistance = despawnBehindDistance,
            distanceJitter = distanceJitter,
            spawnChancePerSlot = spawnChancePerSlot,
            globalSpawnChanceByDistance = globalSpawnChanceByDistance,
            lateralFraction = lateralFraction,
            edgeInnerMargin = edgeInnerMargin,
            stabilizeRigidbodiesOnSpawn = stabilizeRigidbodiesOnSpawn,
            spawnKinematicDuration = spawnKinematicDuration,
            disableGravityWhileKinematic = disableGravityWhileKinematic,
            roadLayer = roadLayer,
            raycastStartHeight = raycastStartHeight,
            raycastDownDistance = raycastDownDistance,
            obstacleHeightOffset = obstacleHeightOffset,
            updateInterval = updateInterval,
            verboseDebug = verboseDebug,
        };
    }


    private List<Vector3> _path = new();
    private float[] _cumLengths;
    private float _totalLength;

    private Dictionary<int, GameObject> _obstaclesBySlot = new();
    private int _maxSlotIndex;
    private float _updateTimer;
    private int _lastClosestIdx = 0;
    private List<int> _toRemove = new();
    private readonly TrackSpawnQueuePendingState _queueState = new();
    private readonly TrackSpawnQueueLastSpawn _queueLastSpawn = new();

    private void Update()
    {
        if (_path.Count < 2 || playerTransform == null || !HasAnyValidObstacleType())
            return;

        if (_queueState.IsControlled)
        {
            float playerDist = GetPlayerDistance();
            DespawnBehindObstacles(playerDist);

            _updateTimer += Time.deltaTime;
            if (_updateTimer >= updateInterval)
            {
                _updateTimer = 0f;

                if (streamSpawnDuringRun || streamWhileQueueControlled)
                    StreamObstacles();

                _queueState.TrySubmit(this);
            }

            return;
        }

        if (!streamSpawnDuringRun)
            return;

        _updateTimer += Time.deltaTime;
        if (_updateTimer < updateInterval) return;

        _updateTimer = 0f;
        StreamObstacles();
    }


    private void RebuildPath()
    {
        _path.Clear();
        _cumLengths = null;
        _totalLength = 0f;
        _lastClosestIdx = 0;

        var src = trackGenerator.PathPoints;
        if (useSmoothing)
            GenerateSmoothedPath(src, smoothingSubdivisionsPerSegment, _path);
        else
            _path.AddRange(src);

        int n = _path.Count;
        _cumLengths = new float[n];
        float len = 0f;
        for (int i = 1; i < n; i++)
        {
            len += Vector3.Distance(_path[i - 1], _path[i]);
            _cumLengths[i] = len;
        }
        _totalLength = len;
    }

    private static void GenerateSmoothedPath(List<Vector3> raw, int subdivisions, List<Vector3> outList)
    {
        outList.Clear();
        outList.Add(raw[0]);
        for (int i = 0; i < raw.Count - 1; i++)
        {
            Vector3 p0 = raw[Mathf.Max(i - 1, 0)];
            Vector3 p1 = raw[i];
            Vector3 p2 = raw[i + 1];
            Vector3 p3 = raw[Mathf.Min(i + 2, raw.Count - 1)];
            for (int s = 1; s <= subdivisions; s++)
            {
                float t = s / (float)subdivisions;
                outList.Add(CatmullRom(p0, p1, p2, p3, t));
            }
        }
    }

    private static Vector3 CatmullRom(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t)
    {
        return 0.5f * (
            (2 * p1) +
            (-p0 + p2) * t +
            (2 * p0 - 5 * p1 + 4 * p2 - p3) * (t * t) +
            (-p0 + 3 * p1 - 3 * p2 + p3) * (t * t * t)
        );
    }

    private void SetupSlots()
    {
        _obstaclesBySlot.Clear();
        _maxSlotIndex = Mathf.FloorToInt(_totalLength / obstacleSpacing);
    }

    private void StreamObstacles()
    {
        if (_totalLength <= 0f || _maxSlotIndex <= 0)
            return;

        float playerDist = GetPlayerDistance();

        float spawnStartDist = Mathf.Clamp(playerDist + minSpawnDistanceAhead, 0f, _totalLength);
        float spawnEndDist = Mathf.Clamp(playerDist + maxSpawnDistanceAhead, 0f, _totalLength);

        int startSlot = Mathf.Clamp(Mathf.FloorToInt(spawnStartDist / obstacleSpacing), 0, _maxSlotIndex);
        int endSlot = Mathf.Clamp(Mathf.FloorToInt(spawnEndDist / obstacleSpacing), 0, _maxSlotIndex);


        // ---------------------------
        // 1) OPTIONAL: spawn ahead
        // ---------------------------
        for (int slot = startSlot; slot <= endSlot; slot++)
        {
            if (_obstaclesBySlot.ContainsKey(slot))
                continue;

            if (_obstaclesBySlot.Count >= maxActiveObstacles)
                break;

            float dist = slot * obstacleSpacing;

            // Just in case, enforce min distance again
            if (dist < playerDist + minSpawnDistanceAhead)
                continue;

            // Distance normalized [0,1] along track
            float norm = (_totalLength > 0f) ? Mathf.Clamp01(dist / _totalLength) : 0f;

            // Global difficulty scaling curve
            float difficultyMult = (globalSpawnChanceByDistance != null)
                ? Mathf.Max(0f, globalSpawnChanceByDistance.Evaluate(norm))
                : 1f;

            float effectiveChance = spawnChancePerSlot * difficultyMult;
            if (effectiveChance <= 0f)
                continue;

            if (Random.value > effectiveChance)
                continue;

            TrySpawnObstacleAtDistance(slot, dist);
        }

        // ---------------------------
        // 2) Despawn behind player
        // ---------------------------
        DespawnBehindObstacles(playerDist);
    }

    private void DespawnBehindObstacles(float playerDist)
    {
        _toRemove.Clear();
        foreach (var kvp in _obstaclesBySlot)
        {
            float dist = kvp.Key * obstacleSpacing;
            if (dist < playerDist - despawnBehindDistance)
                _toRemove.Add(kvp.Key);
        }

        for (int i = 0; i < _toRemove.Count; i++)
        {
            int slot = _toRemove[i];
            if (_obstaclesBySlot.TryGetValue(slot, out var obj) && obj != null)
                Destroy(obj);

            _obstaclesBySlot.Remove(slot);
        }
    }

    private bool TrySpawnOneAhead()
    {
        if (_totalLength <= 0f || _maxSlotIndex <= 0)
            return false;

        float playerDist = GetPlayerDistance();
        float spawnStartDist = Mathf.Clamp(playerDist + minSpawnDistanceAhead, 0f, _totalLength);
        float spawnEndDist = Mathf.Clamp(playerDist + maxSpawnDistanceAhead, 0f, _totalLength);

        int startSlot = Mathf.Clamp(Mathf.FloorToInt(spawnStartDist / obstacleSpacing), 0, _maxSlotIndex);
        int endSlot = Mathf.Clamp(Mathf.FloorToInt(spawnEndDist / obstacleSpacing), 0, _maxSlotIndex);

        for (int slot = startSlot; slot <= endSlot; slot++)
        {
            if (_obstaclesBySlot.ContainsKey(slot))
                continue;

            if (_obstaclesBySlot.Count >= maxActiveObstacles)
                break;

            float dist = slot * obstacleSpacing;
            if (dist < playerDist + minSpawnDistanceAhead)
                continue;

            float norm = (_totalLength > 0f) ? Mathf.Clamp01(dist / _totalLength) : 0f;
            float difficultyMult = (globalSpawnChanceByDistance != null)
                ? Mathf.Max(0f, globalSpawnChanceByDistance.Evaluate(norm))
                : 1f;

            float effectiveChance = spawnChancePerSlot * difficultyMult;
            if (effectiveChance <= 0f)
                continue;

            if (Random.value > effectiveChance)
                continue;

            int before = _obstaclesBySlot.Count;
            TrySpawnObstacleAtDistance(slot, dist);
            if (_obstaclesBySlot.Count > before)
                return true;
        }

        return false;
    }


    private bool HasAnyValidObstacleType()
    {
        if (obstacleTypes == null || obstacleTypes.Count == 0)
            return false;

        for (int i = 0; i < obstacleTypes.Count; i++)
        {
            if (obstacleTypes[i] != null && obstacleTypes[i].prefab != null && obstacleTypes[i].baseWeight > 0f)
                return true;
        }

        return false;
    }

    private float GetPlayerDistance()
    {
        Vector3 p = playerTransform.position;
        float best = float.MaxValue;
        for (int i = 0; i < _path.Count - 1; i++)
        {
            Vector3 a = _path[i], b = _path[i + 1];
            float t = Mathf.Clamp01(Vector3.Dot(p - a, (b - a)) / (b - a).sqrMagnitude);
            Vector3 proj = Vector3.Lerp(a, b, t);
            float d = (p - proj).sqrMagnitude;
            if (d < best) { _lastClosestIdx = i; best = d; }
        }
        float segLen = Vector3.Distance(_path[_lastClosestIdx], _path[_lastClosestIdx + 1]);
        float prog = Mathf.Clamp01(Vector3.Dot(p - _path[_lastClosestIdx], (_path[_lastClosestIdx + 1] - _path[_lastClosestIdx])) / Mathf.Pow(segLen, 2));
        return _cumLengths[_lastClosestIdx] + prog * segLen;
    }

    private void TrySpawnObstacleAtDistance(int slot, float baseDist)
    {
        float jitter = Random.Range(-distanceJitter, distanceJitter);
        float sampleDist = Mathf.Clamp(baseDist + jitter, 0f, _totalLength);

        GameObject chosenPrefab = ChooseObstaclePrefab(sampleDist);
        if (chosenPrefab == null)
            return;

        SampleAlongPath(sampleDist, out var pos, out var forward);

        if (forward.sqrMagnitude < 0.0001f)
            forward = Vector3.forward;

        float halfWidth = trackGenerator.RoadWidth * 0.5f;
        float usable = (halfWidth * lateralFraction) - edgeInnerMargin;
        if (usable <= 0f)
            usable = halfWidth * 0.5f;

        Vector3 flatForward = forward;
        flatForward.y = 0f;
        if (flatForward.sqrMagnitude < 0.0001f)
            flatForward = Vector3.forward;
        flatForward.Normalize();

        Vector3 right = Vector3.Cross(Vector3.up, flatForward);
        pos += right * Random.Range(-usable, usable);

        Vector3 origin = pos + Vector3.up * raycastStartHeight;
        float maxRay = raycastStartHeight + raycastDownDistance;

        if (Physics.Raycast(origin, Vector3.down, out RaycastHit hit, maxRay, roadLayer, QueryTriggerInteraction.Ignore))
        {
            ObstacleType t = GetChosenTypeForDistance(sampleDist);
            float extraHeight = t != null ? t.extraHeightOffset : 0f;
            float extraPad = t != null ? t.extraLateralPadding : 0f;

            Quaternion rot = Quaternion.LookRotation(flatForward, hit.normal);
            Transform parent = obstacleParent ? obstacleParent : transform;

            Vector3 centered = hit.point;
            float rawLateral = Vector3.Dot((pos - centered), right);
            float clampedUsable = usable;
            if (extraPad != 0f)
                clampedUsable = Mathf.Max(0f, usable - Mathf.Abs(extraPad));

            float finalLateral = Mathf.Clamp(rawLateral, -clampedUsable, clampedUsable);
            Vector3 spawnPos = centered + right * finalLateral;

            // Instantiate at ground level first
            GameObject obstacle = Instantiate(chosenPrefab, spawnPos, rot, parent);
            StabilizeObstacleRigidbodies(obstacle);

            // Get the parent object's renderer bounds (not children)
            float parentBottomOffset = GetParentBottomOffset(obstacle);

            // Position so the bottom of the parent sits exactly on the ground
            obstacle.transform.position = hit.point + hit.normal * parentBottomOffset + right * finalLateral;

            _obstaclesBySlot[slot] = obstacle;
            _queueLastSpawn.Record(obstacle.transform.position, chosenPrefab.name);
        }
    }

    /// <summary>
    /// Gets the offset needed to place the parent object's bottom at world origin.
    /// Only considers the parent's renderer/collider, not children.
    /// </summary>
    private float GetParentBottomOffset(GameObject obj)
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
            float bottom = localBounds.min.y - root.position.y; // local space bottom
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
            float bottom = localBounds.min.y - root.position.y; // local space bottom
            if (!foundAny || bottom < lowestPoint)
            {
                lowestPoint = bottom;
                foundAny = true;
            }
        }

        // If nothing found, return a small default
        if (!foundAny) return 0.05f;

        // Return the absolute value (distance from pivot to bottom)
        return Mathf.Abs(lowestPoint);
    }


    public void SetPlayerTransform(Transform player)
    {
        playerTransform = player;
    }

    private void SampleAlongPath(float dist, out Vector3 pos, out Vector3 fwd)
    {
        dist = Mathf.Clamp(dist, 0, _totalLength);
        int idx = 0;
        for (int i = 0; i < _cumLengths.Length - 1; i++)
            if (_cumLengths[i + 1] >= dist) { idx = i; break; }

        float segLen = _cumLengths[idx + 1] - _cumLengths[idx];
        float t = (dist - _cumLengths[idx]) / Mathf.Max(segLen, 0.0001f);
        pos = Vector3.Lerp(_path[idx], _path[idx + 1], t);
        fwd = (_path[idx + 1] - _path[idx]).normalized;
    }

    private void ClearObstacles()
    {
        foreach (var o in _obstaclesBySlot.Values) if (o) Destroy(o);
        _obstaclesBySlot.Clear();
    }

    private ObstacleType GetChosenTypeForDistance(float distanceAlongTrack)
    {
        if (obstacleTypes == null || obstacleTypes.Count == 0 || _totalLength <= 0f)
            return null;

        float norm = Mathf.Clamp01(distanceAlongTrack / _totalLength);

        float totalWeight = 0f;
        float[] weights = new float[obstacleTypes.Count];

        for (int i = 0; i < obstacleTypes.Count; i++)
        {
            ObstacleType t = obstacleTypes[i];
            if (t == null || t.prefab == null || t.baseWeight <= 0f)
            {
                weights[i] = 0f;
                continue;
            }

            float w = GetWeightForType(t, norm);
            weights[i] = w;
            totalWeight += w;
        }

        if (totalWeight <= 0f)
            return null;

        float rand = Random.value * totalWeight;
        float accum = 0f;

        for (int i = 0; i < weights.Length; i++)
        {
            accum += weights[i];
            if (rand <= accum)
                return obstacleTypes[i];
        }

        // Fallback
        return obstacleTypes[obstacleTypes.Count - 1];
    }

    private GameObject ChooseObstaclePrefab(float distanceAlongTrack)
    {
        ObstacleType t = GetChosenTypeForDistance(distanceAlongTrack);
        return t != null ? t.prefab : null;
    }

    private float GetWeightForType(ObstacleType t, float normDist)
    {
        // Normalize band ordering just in case
        float start = Mathf.Clamp01(Mathf.Min(t.startAtNormalizedDist, t.fullWeightNormalizedDist));
        float full = Mathf.Clamp01(Mathf.Max(t.startAtNormalizedDist, t.fullWeightNormalizedDist));
        float stop = Mathf.Clamp01(Mathf.Max(full, t.stopAtNormalizedDist));

        if (normDist < start || normDist > stop)
            return 0f;

        float factor;

        if (normDist < full)
        {
            // Ramp from 0->1 between start and full
            factor = Mathf.InverseLerp(start, full, normDist);
        }
        else
        {
            // Hold at full weight until stop
            factor = 1f;
        }

        return t.baseWeight * factor;
    }

    /// <summary>
    /// Fill a window [0 .. initialPreSpawnDistance] with obstacles once, before the run.
    /// This avoids visible pop-in at the very start of the game.
    /// </summary>
    private void PreSpawnInitialWindow()
    {
        if (_totalLength <= 0f || _maxSlotIndex <= 0)
            return;

        float preSpawnEnd = Mathf.Clamp(initialPreSpawnDistance, 0f, _totalLength);
        int endSlot = Mathf.FloorToInt(preSpawnEnd / obstacleSpacing);

        for (int slot = 0; slot <= endSlot; slot++)
        {
            if (_obstaclesBySlot.ContainsKey(slot))
                continue;

            float dist = slot * obstacleSpacing;

            // Same difficulty logic as runtime
            float norm = (_totalLength > 0f) ? Mathf.Clamp01(dist / _totalLength) : 0f;
            float difficultyMult = (globalSpawnChanceByDistance != null)
                ? Mathf.Max(0f, globalSpawnChanceByDistance.Evaluate(norm))
                : 1f;

            float effectiveChance = spawnChancePerSlot * difficultyMult;
            if (effectiveChance <= 0f) continue;
            if (Random.value > effectiveChance) continue;

            TrySpawnObstacleAtDistance(slot, dist);
        }

        if (verboseDebug)
            Debug.Log($"[TrackObstacleSpawner] PreSpawnInitialWindow spawned {_obstaclesBySlot.Count} obstacles up to {preSpawnEnd:0.0}m.");
    }

    /// <summary>
    /// Gently pushes the spawned obstacle out of the ground collider along the ground normal,
    /// using Physics.ComputePenetration. Ignores trigger colliders on the obstacle.
    /// </summary>
    private void ResolveGroundPenetration(GameObject obstacle, Collider groundCol, Vector3 groundNormal)
    {
        if (!obstacle || !groundCol) return;

        // Collect non-trigger colliders from the obstacle
        var cols = obstacle.GetComponentsInChildren<Collider>(true);
        List<Collider> solidCols = new List<Collider>(cols.Length);
        for (int i = 0; i < cols.Length; i++)
        {
            if (cols[i] && !cols[i].isTrigger) solidCols.Add(cols[i]);
        }
        if (solidCols.Count == 0) return;

        Transform root = obstacle.transform;
        const int maxIters = 4;
        const float minStep = 0.0005f;

        for (int iter = 0; iter < maxIters; iter++)
        {
            float maxPushAlongNormal = 0f;

            // Find the largest penetration depth projected onto the ground normal
            for (int i = 0; i < solidCols.Count; i++)
            {
                Collider c = solidCols[i];
                if (!c) continue;

                Vector3 sepDir = Vector3.zero;
                float sepDist = 0f;

                bool penetrating = Physics.ComputePenetration(
                    c, c.transform.position, c.transform.rotation,
                    groundCol, groundCol.transform.position, groundCol.transform.rotation,
                    out sepDir, out sepDist
                );

                if (!penetrating || sepDist <= 0f) continue;

                // Only push outward along ground normal
                float proj = Vector3.Dot(sepDir * sepDist, groundNormal);
                if (proj > maxPushAlongNormal)
                    maxPushAlongNormal = proj;
            }

            if (maxPushAlongNormal <= minStep)
                break; // good enough

            // Apply push
            root.position += groundNormal * maxPushAlongNormal;
            Physics.SyncTransforms();
        }

        // Tiny non-zero lift to avoid z-fighting / hard contacts
        root.position += groundNormal * 0.002f;
        Physics.SyncTransforms();
    }

    private void StabilizeObstacleRigidbodies(GameObject obstacle)
    {
        if (!stabilizeRigidbodiesOnSpawn || obstacle == null) return;

        // Don’t mess with obstacles that manage their own kinematic/dynamic state
        if (obstacle.GetComponentInChildren<CrossTrackObstacle>(true) != null) return;
        if (obstacle.GetComponentInChildren<ShuttleTrackObstacle>(true) != null) return;
        if (obstacle.GetComponentInChildren<RollingLogAlongTrack>(true) != null) return;

        var rbs = obstacle.GetComponentsInChildren<Rigidbody>(true);
        if (rbs == null || rbs.Length == 0) return;

        for (int i = 0; i < rbs.Length; i++)
        {
            var rb = rbs[i];
            if (rb == null) continue;

            bool prevUseGravity = rb.useGravity;

            rb.isKinematic = true;
            if (disableGravityWhileKinematic) rb.useGravity = false;

            StartCoroutine(CoEnablePhysicsAfterDelay(rb, prevUseGravity, spawnKinematicDuration));
        }
    }

    private IEnumerator CoEnablePhysicsAfterDelay(Rigidbody rb, bool restoreGravity, float delay)
    {
        if (rb == null) yield break;

        // Use realtime so slowmo/timeScale doesn’t make this feel inconsistent
        float end = Time.realtimeSinceStartup + Mathf.Max(0f, delay);
        while (Time.realtimeSinceStartup < end)
        {
            if (rb == null) yield break;
            yield return null;
        }

        if (rb == null) yield break;

        rb.isKinematic = false;
        rb.useGravity = restoreGravity;
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        rb.WakeUp();
    }


    public void InitializeForRun(ProceduralTrackGenerator generator, Transform player)
    {
        trackGenerator = generator;
        playerTransform = player;

        if (trackGenerator == null || playerTransform == null)
        {
            Debug.LogError("[TrackObstacleSpawner] InitializeForRun missing refs. " +
                           $"trackGenerator={trackGenerator}, player={playerTransform}");
            return;
        }

        if (verboseDebug)
            Debug.Log("[TrackObstacleSpawner] InitializeForRun: rebuilding path + slots.");

        RebuildPath();
        ClearObstacles();
        SetupSlots();

        // 🔹 Pre-spawn a window ahead of the starting line so nothing pops in on countdown.
        PreSpawnInitialWindow();

        _updateTimer = 0f;
    }

    public string SpawnQueueLabel => "Track Obstacles";
    public bool IsSpawnQueueReady => _path.Count >= 2 && playerTransform != null && HasAnyValidObstacleType();
    public bool HasSpawnQueueCapacity => _obstaclesBySlot.Count < maxActiveObstacles;
    public bool HasPendingSpawnRequest => _queueState.HasPending;
    public bool TrySubmitSpawnRequest() => _queueState.TrySubmit(this);
    public bool TryExecutePendingSpawn() => _queueState.TryExecute(TrySpawnOneAhead);
    public bool TryConsumeLastSpawnReport(out TrackSpawnQueueSpawnReport report) => _queueLastSpawn.TryConsume(out report);
    public void CancelPendingSpawnRequest() => _queueState.Cancel();
    public void SetQueueControlledAutonomous(bool controlled, TrackSpawnerQueue owner = null) => _queueState.Bind(controlled, owner);
}
