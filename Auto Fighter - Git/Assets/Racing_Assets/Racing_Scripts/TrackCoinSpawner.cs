using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Spawns coins along the track using the CoinType system.
/// Each coin type uses its own prefab from CoinDataSO.
/// Combines base spawn weights from CoinDatabase with distance-based weight curves.
/// </summary>
public class TrackCoinSpawner : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private ProceduralTrackGenerator trackGenerator;
    [SerializeField] private Transform playerTransform;

    [Header("Coin Type Weights")]
    [Tooltip("Configure which coin types can spawn and their distance-based weights.")]
    [SerializeField]
    private List<CoinTypeWeight> coinTypeWeights = new List<CoinTypeWeight>()
    {
        new CoinTypeWeight { coinType = CoinType.Bronze, enabled = true, globalScale = 1.12f },
        new CoinTypeWeight { coinType = CoinType.Silver, enabled = true, globalScale = 0.95f },
        new CoinTypeWeight { coinType = CoinType.Gold, enabled = true, globalScale = 0.8f },
        new CoinTypeWeight { coinType = CoinType.Platinum, enabled = true, globalScale = 0.4f },
        new CoinTypeWeight { coinType = CoinType.Diamond, enabled = true, globalScale = 0.15f },
        new CoinTypeWeight { coinType = CoinType.Legendary, enabled = true, globalScale = 0.05f }
    };

    [System.Serializable]
    public class CoinTypeWeight
    {
        public CoinType coinType = CoinType.Bronze;
        public bool enabled = true;

        [Tooltip("Multiplier applied to the base spawn weight from CoinDatabase.")]
        public float globalScale = 1f;

        [Tooltip("Distance-based weight curve (0 = track start, 1 = track end). Multiplies base weight.")]
        public AnimationCurve distanceCurve = AnimationCurve.Linear(0f, 1f, 1f, 1f);
    }

    [Header("Spawn Layout")]
    [SerializeField, Min(0.1f)] private float coinSpacing = 6f;
    [SerializeField] private int maxActiveCoins = 120;
    [SerializeField] private float minSpawnDistanceAhead = 40f;
    [SerializeField] private float maxSpawnDistanceAhead = 140f;
    [SerializeField] private float despawnBehindDistance = 25f;
    [SerializeField] private float initialPreSpawnDistance = 80f;

    [Header("Spawn Probability")]
    [SerializeField, Range(0f, 1f)] private float baseSpawnChance = 0.85f;
    [Tooltip("Curve remaps distance fraction → multiplier on base spawn chance.")]
    [SerializeField] private AnimationCurve spawnChanceDistanceCurve = AnimationCurve.Linear(0, 1, 1, 1);
    [Tooltip("Optional late-track spawn chance boost.")]
    [SerializeField]
    private AnimationCurve lateTrackSpawnBonusCurve = new AnimationCurve(
        new Keyframe(0f, 1f),
        new Keyframe(0.8f, 1f),
        new Keyframe(1f, 1.15f)
    );

    [Header("Skill Integration")]
    [SerializeField] private bool applySkillSpawnRate = true;

    [Header("Placement")]
    [SerializeField] private float coinHeightOffset = 0.5f;
    [SerializeField, Range(0f, 1f)] private float lateralFractionOfHalfWidth = 0.7f;
    [SerializeField] private float edgeInnerMargin = 0.25f;

    [Header("Raycast")]
    [SerializeField] private LayerMask roadLayerMask = ~0;
    [SerializeField] private float raycastStartHeight = 5f;
    [SerializeField] private float raycastDownDistance = 15f;
    [SerializeField] private bool alignToSurfaceNormal = true;

    [Header("Jitter & Update")]
    [SerializeField] private float distanceJitter = 1.5f;
    [SerializeField] private bool useSmoothing = true;
    [SerializeField, Min(1)] private int smoothingSubdivisionsPerSegment = 6;
    [SerializeField] private float updateInterval = 0.2f;

    [Header("Debug")]
    [SerializeField] private bool verboseDebug = false;
    [SerializeField] private bool showWeightsGizmo = false;
    [SerializeField] private int gizmoSamples = 50;

    // Internal path
    private readonly List<Vector3> _path = new();
    private float[] _cumLengths;
    private float _totalLength;
    private int _maxSlotIndex;

    private readonly Dictionary<int, GameObject> _coinsBySlot = new();
    private readonly List<int> _toRemove = new();
    private float _updateTimer;
    private int _lastClosestSegmentIndex = 0;

    // ------------------------------------------------------------------------
    // Per-trial config (TrialConfig). ApplyConfig copies a trial's CoinSettings into these fields
    // (call BEFORE InitializeForRun). CaptureConfig snapshots the current values (editor baker).
    // ------------------------------------------------------------------------
    public void ApplyConfig(TrialConfig.CoinSettings s)
    {
        if (s == null || !s.overrideCoins) return;

        coinTypeWeights = s.coinTypeWeights != null
            ? new List<CoinTypeWeight>(s.coinTypeWeights)
            : new List<CoinTypeWeight>();

        coinSpacing = s.coinSpacing;
        maxActiveCoins = s.maxActiveCoins;
        minSpawnDistanceAhead = s.minSpawnDistanceAhead;
        maxSpawnDistanceAhead = s.maxSpawnDistanceAhead;
        despawnBehindDistance = s.despawnBehindDistance;
        initialPreSpawnDistance = s.initialPreSpawnDistance;

        baseSpawnChance = s.baseSpawnChance;
        spawnChanceDistanceCurve = s.spawnChanceDistanceCurve;
        lateTrackSpawnBonusCurve = s.lateTrackSpawnBonusCurve;
        applySkillSpawnRate = s.applySkillSpawnRate;

        coinHeightOffset = s.coinHeightOffset;
        lateralFractionOfHalfWidth = s.lateralFractionOfHalfWidth;
        edgeInnerMargin = s.edgeInnerMargin;

        roadLayerMask = s.roadLayer;
        raycastStartHeight = s.raycastStartHeight;
        raycastDownDistance = s.raycastDownDistance;
        alignToSurfaceNormal = s.alignToSurfaceNormal;

        distanceJitter = s.distanceJitter;
        useSmoothing = s.useSmoothing;
        smoothingSubdivisionsPerSegment = s.smoothingSubdivisionsPerSegment;
        updateInterval = s.updateInterval;
        verboseDebug = s.verboseDebug;
    }

    public TrialConfig.CoinSettings CaptureConfig()
    {
        return new TrialConfig.CoinSettings
        {
            overrideCoins = true,
            coinTypeWeights = coinTypeWeights != null
                ? new List<CoinTypeWeight>(coinTypeWeights)
                : new List<CoinTypeWeight>(),
            coinSpacing = coinSpacing,
            maxActiveCoins = maxActiveCoins,
            minSpawnDistanceAhead = minSpawnDistanceAhead,
            maxSpawnDistanceAhead = maxSpawnDistanceAhead,
            despawnBehindDistance = despawnBehindDistance,
            initialPreSpawnDistance = initialPreSpawnDistance,
            baseSpawnChance = baseSpawnChance,
            spawnChanceDistanceCurve = spawnChanceDistanceCurve,
            lateTrackSpawnBonusCurve = lateTrackSpawnBonusCurve,
            applySkillSpawnRate = applySkillSpawnRate,
            coinHeightOffset = coinHeightOffset,
            lateralFractionOfHalfWidth = lateralFractionOfHalfWidth,
            edgeInnerMargin = edgeInnerMargin,
            roadLayer = roadLayerMask,
            raycastStartHeight = raycastStartHeight,
            raycastDownDistance = raycastDownDistance,
            alignToSurfaceNormal = alignToSurfaceNormal,
            distanceJitter = distanceJitter,
            useSmoothing = useSmoothing,
            smoothingSubdivisionsPerSegment = smoothingSubdivisionsPerSegment,
            updateInterval = updateInterval,
            verboseDebug = verboseDebug,
        };
    }

    public void InitializeForRun(ProceduralTrackGenerator generator, Transform player)
    {
        trackGenerator = generator;
        playerTransform = player;
        RebuildPath();
        ClearAllCoins();
        SetupSlots();
        PreSpawnInitialCoins();
        _updateTimer = 0f;
    }

    private void Update()
    {
        if (_path.Count < 2 || playerTransform == null) return;

        _updateTimer += Time.deltaTime;
        if (_updateTimer >= updateInterval)
        {
            _updateTimer = 0f;
            StreamCoins();
        }
    }

    private void RebuildPath()
    {
        _path.Clear();
        _cumLengths = null;
        _totalLength = 0f;
        _lastClosestSegmentIndex = 0;

        if (trackGenerator == null) return;
        TrackPathSampling.RebuildPathFromRoadCenterline(trackGenerator, _path, ref _cumLengths, out _totalLength);
    }

    private void SetupSlots()
    {
        _maxSlotIndex = coinSpacing > 0f ? Mathf.CeilToInt(_totalLength / coinSpacing) : 0;
    }

    private void ClearAllCoins()
    {
        foreach (var kvp in _coinsBySlot)
            if (kvp.Value) Destroy(kvp.Value);
        _coinsBySlot.Clear();
    }

    private void PreSpawnInitialCoins()
    {
        if (_path.Count < 2 || coinSpacing <= 0f) return;

        float playerDist = GetPlayerDistance();
        float aheadLimit = Mathf.Min(playerDist + initialPreSpawnDistance, _totalLength);

        for (float d = playerDist; d < aheadLimit; d += coinSpacing)
        {
            int slot = Mathf.FloorToInt(d / coinSpacing);
            if (!_coinsBySlot.ContainsKey(slot))
                SpawnCoinAtSlot(slot, d);
        }
    }

    private void StreamCoins()
    {
        if (_path.Count < 2 || coinSpacing <= 0f) return;

        float playerDist = GetPlayerDistance();
        float minD = playerDist + minSpawnDistanceAhead;
        float maxD = Mathf.Min(playerDist + maxSpawnDistanceAhead, _totalLength);

        // Spawn ahead
        for (float d = minD; d < maxD; d += coinSpacing)
        {
            int slot = Mathf.FloorToInt(d / coinSpacing);
            if (slot < 0 || slot > _maxSlotIndex) continue;
            if (_coinsBySlot.ContainsKey(slot)) continue;
            if (_coinsBySlot.Count >= maxActiveCoins) break;

            SpawnCoinAtSlot(slot, d);
        }

        // Despawn behind
        float despawnThreshold = playerDist - despawnBehindDistance;
        _toRemove.Clear();
        foreach (var kvp in _coinsBySlot)
        {
            float slotDist = kvp.Key * coinSpacing;
            if (slotDist < despawnThreshold)
            {
                if (kvp.Value) Destroy(kvp.Value);
                _toRemove.Add(kvp.Key);
            }
        }
        foreach (var k in _toRemove)
            _coinsBySlot.Remove(k);
    }

    private float GetPlayerDistance()
    {
        if (_path.Count < 2 || playerTransform == null) return 0f;

        Vector3 playerPos = playerTransform.position;
        float bestDist = float.MaxValue;
        int bestIdx = _lastClosestSegmentIndex;

        // Search around last known position
        int searchStart = Mathf.Max(0, _lastClosestSegmentIndex - 5);
        int searchEnd = Mathf.Min(_path.Count - 1, _lastClosestSegmentIndex + 20);

        for (int i = searchStart; i < searchEnd; i++)
        {
            float d = PointSegmentDistanceSqr(playerPos, _path[i], _path[i + 1]);
            if (d < bestDist)
            {
                bestDist = d;
                bestIdx = i;
            }
        }
        _lastClosestSegmentIndex = bestIdx;

        // Project onto segment
        Vector3 a = _path[bestIdx];
        Vector3 b = _path[bestIdx + 1];
        Vector3 ab = b - a;
        float segLen = ab.magnitude;
        if (segLen < 0.001f) return _cumLengths[bestIdx];

        float t = Mathf.Clamp01(Vector3.Dot(playerPos - a, ab) / (segLen * segLen));
        return _cumLengths[bestIdx] + t * segLen;
    }

    private static float PointSegmentDistanceSqr(Vector3 p, Vector3 a, Vector3 b)
    {
        Vector3 ab = b - a;
        float t = Mathf.Clamp01(Vector3.Dot(p - a, ab) / ab.sqrMagnitude);
        Vector3 proj = a + t * ab;
        return (p - proj).sqrMagnitude;
    }

    private void SpawnCoinAtSlot(int slotIndex, float distanceAlongTrack)
    {
        float jitter = distanceJitter > 0f ? UnityEngine.Random.Range(-distanceJitter, distanceJitter) : 0f;
        float dist = Mathf.Clamp(distanceAlongTrack + jitter, 0f, _totalLength);

        float spawnChance = ComputeSpawnChance(dist);
        if (UnityEngine.Random.value > spawnChance)
            return;

        // Compute normalized distance (0 = start, 1 = end)
        float norm = _totalLength > 0f ? dist / _totalLength : 0f;

        // Select coin type and get its data
        CoinDataSO coinData = SelectCoinData(norm);
        if (coinData == null || coinData.coinPrefab == null)
        {
            if (verboseDebug)
                Debug.LogWarning($"[TrackCoinSpawner] No prefab for selected coin type at slot {slotIndex}");
            return;
        }

        SampleAlongPath(dist, out var centerPos, out var forward);

        float halfWidth = (trackGenerator != null ? trackGenerator.RoadWidth * 0.5f : 2f);
        float usableHalfWidth = Mathf.Max(0f, halfWidth * lateralFractionOfHalfWidth - edgeInnerMargin);
        if (usableHalfWidth <= 0f) usableHalfWidth = halfWidth * 0.5f;

        var flatForward = forward; flatForward.y = 0f;
        if (flatForward.sqrMagnitude < 1e-6f) flatForward = Vector3.forward;
        flatForward.Normalize();
        var right = Vector3.Cross(Vector3.up, flatForward).normalized;

        float lateral = UnityEngine.Random.Range(-usableHalfWidth, usableHalfWidth);
        Vector3 candidate = centerPos + right * lateral;

        Vector3 origin = candidate + Vector3.up * raycastStartHeight;
        float maxDist = raycastStartHeight + raycastDownDistance;

        if (!Physics.Raycast(origin, Vector3.down, out RaycastHit hit, maxDist, roadLayerMask, QueryTriggerInteraction.Ignore))
        {
            if (verboseDebug) Debug.DrawRay(origin, Vector3.down * maxDist, Color.red, 2f);
            return;
        }

        Vector3 up = alignToSurfaceNormal ? hit.normal : Vector3.up;
        Vector3 forwardOnSurface = Vector3.ProjectOnPlane(flatForward, up);
        if (forwardOnSurface.sqrMagnitude < 1e-6f)
            forwardOnSurface = Vector3.Cross(up, Vector3.right);
        forwardOnSurface.Normalize();

        Quaternion rot = Quaternion.LookRotation(forwardOnSurface, up);
        Transform parent = transform;
        Vector3 spawnPos = hit.point + up * coinHeightOffset;

        // Instantiate the correct prefab for this coin type
        GameObject inst = Instantiate(coinData.coinPrefab, spawnPos, rot, parent);
        _coinsBySlot[slotIndex] = inst;

        // Set coin type on the CoinPickup component
        var cp = inst.GetComponent<CoinPickup>();
        if (cp != null)
        {
            cp.SetCoinType(coinData.coinType);
        }

        if (verboseDebug)
        {
            Debug.DrawLine(origin, hit.point, coinData.primaryColor, 1.5f);
            Debug.DrawRay(hit.point, up, Color.green, 1.5f);
        }
    }

    /// <summary>
    /// Select coin data based on combined weights (base weight + distance curve + global scale).
    /// Returns the CoinDataSO for the selected type.
    /// </summary>
    private CoinDataSO SelectCoinData(float normalizedDistance)
    {
        if (CoinDatabase.Instance == null) return null;

        // Calculate weights for each enabled type
        float totalWeight = 0f;
        var weights = new List<(CoinDataSO data, float weight)>();

        foreach (var ctw in coinTypeWeights)
        {
            if (!ctw.enabled) continue;

            // Get coin data from database
            var coinData = CoinDatabase.Get(ctw.coinType);
            if (coinData == null || coinData.coinPrefab == null) continue;

            // Get base weight from CoinDataSO
            float baseWeight = coinData.spawnWeight;

            // Apply distance curve
            float distanceMultiplier = ctw.distanceCurve != null ? ctw.distanceCurve.Evaluate(normalizedDistance) : 1f;

            // Apply global scale
            float finalWeight = Mathf.Max(0f, baseWeight * distanceMultiplier * ctw.globalScale);

            if (finalWeight > 0f)
            {
                weights.Add((coinData, finalWeight));
                totalWeight += finalWeight;
            }
        }

        if (totalWeight <= 0f || weights.Count == 0)
            return null;

        // Weighted random selection
        float pick = UnityEngine.Random.value * totalWeight;
        float accumulated = 0f;

        foreach (var (data, weight) in weights)
        {
            accumulated += weight;
            if (pick <= accumulated)
                return data;
        }

        return weights[weights.Count - 1].data;
    }

    private float ComputeSpawnChance(float distance)
    {
        float norm = _totalLength > 0f ? distance / _totalLength : 0f;
        float chance = baseSpawnChance;

        if (spawnChanceDistanceCurve != null)
            chance *= Mathf.Clamp01(spawnChanceDistanceCurve.Evaluate(norm));

        if (lateTrackSpawnBonusCurve != null)
            chance *= Mathf.Max(0f, lateTrackSpawnBonusCurve.Evaluate(norm));

        if (applySkillSpawnRate)
        {
            var mgr = RacingSkillTreeManager.Instance;
            if (mgr != null)
                chance *= mgr.GetCoinSpawnRateMultiplier();
        }

        return Mathf.Clamp01(chance);
    }

    private void SampleAlongPath(float targetDistance, out Vector3 pos, out Vector3 forward)
    {
        pos = Vector3.zero;
        forward = Vector3.forward;
        if (_path.Count < 2 || _cumLengths == null) return;
        TrackPathSampling.SampleAlongPath(_path, _cumLengths, _totalLength, targetDistance, out pos, out forward);
    }

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        if (!showWeightsGizmo || coinTypeWeights == null || coinTypeWeights.Count == 0) return;
        if (CoinDatabase.Instance == null) return;

        // Draw weight distribution along track
        float gizmoHeight = 5f;
        float gizmoWidth = 10f;
        Vector3 basePos = transform.position + Vector3.up * 2f;

        for (int i = 0; i <= gizmoSamples; i++)
        {
            float f = i / (float)gizmoSamples;
            float x = f * gizmoWidth;

            float yOffset = 0f;
            foreach (var ctw in coinTypeWeights)
            {
                if (!ctw.enabled) continue;

                var coinData = CoinDatabase.Get(ctw.coinType);
                if (coinData == null) continue;

                float baseWeight = coinData.spawnWeight;
                float distMult = ctw.distanceCurve != null ? ctw.distanceCurve.Evaluate(f) : 1f;
                float w = Mathf.Max(0f, baseWeight * distMult * ctw.globalScale * 0.02f);

                Gizmos.color = coinData.primaryColor;
                Vector3 start = basePos + Vector3.right * x + Vector3.up * yOffset;
                Vector3 end = start + Vector3.up * w;
                Gizmos.DrawLine(start, end);
                yOffset += w;
            }
        }
    }
#endif
}