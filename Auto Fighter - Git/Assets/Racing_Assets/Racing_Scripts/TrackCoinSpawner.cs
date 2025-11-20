using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Streams coin pickups along the procedural track with distance‑aware variety staging,
/// early‑section balancing and dynamic unlocking (similar in concept to an obstacle/object spawner).
/// </summary>
public class TrackCoinSpawner : MonoBehaviour
{
    #region Variant Definition
    [Serializable]
    public class CoinVariant
    {
        [Tooltip("Prefab with CoinPickup component (value taken from variant if set >0).")]
        public GameObject prefab;

        [Tooltip("Minimum track distance (meters) before this variant can appear.")]
        public float unlockDistance = 0f;

        [Tooltip("If > 0 overrides the CoinPickup's value on spawn.")]
        public int overrideValue = 0;

        [Tooltip("Relative weight used in random selection among unlocked variants.")]
        public float weight = 1f;

        [Tooltip("Optional multiplier curve by normalized distance (0=start,1=end). Leave empty for 1.")]
        public AnimationCurve distanceMultiplier = AnimationCurve.Linear(0, 1, 1, 1);
    }
    #endregion

    [Header("References")]
    [SerializeField] private ProceduralTrackGenerator trackGenerator;
    [SerializeField] private Transform playerTransform;

    [Header("Deprecated (kept for backward compatibility)")]
    [SerializeField] private GameObject coinPrefab;          // Fallback/common coin
    [SerializeField] private Transform coinParent;

    [Header("Coin Variants (add higher value types here)")]
    [SerializeField] private List<CoinVariant> variants = new();

    [Header("Coin Height")]
    [SerializeField] private float coinHeightOffset = 0.5f;

    [Header("Path Sampling")]
    [SerializeField] private bool useSmoothing = true;
    [SerializeField, Min(1)] private int smoothingSubdivisionsPerSegment = 6;

    [Header("Slot Layout")]
    [SerializeField, Min(0.1f)] private float coinSpacing = 6f;
    [SerializeField] private int maxActiveCoins = 120;

    [Header("Spawn Window (ahead of player)")]
    [SerializeField] private float minSpawnDistanceAhead = 40f;
    [SerializeField] private float maxSpawnDistanceAhead = 140f;
    [SerializeField] private float despawnBehindDistance = 25f;

    [Header("Initial Pre-Spawn")]
    [SerializeField] private float initialPreSpawnDistance = 80f;

    [Header("Global Spawn Chance")]
    [SerializeField, Range(0f, 1f)] private float baseSpawnChance = 0.85f;
    [Tooltip("Extra scaling by normalized distance (0..1).")]
    [SerializeField] private AnimationCurve spawnChanceDistanceCurve = AnimationCurve.Linear(0, 1, 1, 1);

    [Header("Early Region Balancing")]
    [Tooltip("Meters from start considered 'early'.")]
    [SerializeField] private float earlyRegionMeters = 60f;
    [Tooltip("Chance scale applied inside early region (e.g. 0.6 = fewer coins early).")]
    [SerializeField, Range(0f, 2f)] private float earlyRegionChanceScale = 0.6f;
    [Tooltip("Optionally restrict to only the first (common) variant during early region.")]
    [SerializeField] private bool restrictVariantsEarly = true;
    [Tooltip("Hard cap of active coins allowed inside the early region.")]
    [SerializeField] private int earlyRegionActiveCap = 40;

    [Header("Lateral Placement")]
    [SerializeField, Range(0f, 1f)] private float lateralFractionOfHalfWidth = 0.7f;
    [SerializeField] private float edgeInnerMargin = 0.25f;

    [Header("Raycast Settings")]
    [SerializeField] private LayerMask roadLayerMask = ~0;
    [SerializeField] private float raycastStartHeight = 5f;
    [SerializeField] private float raycastDownDistance = 15f;
    [SerializeField] private bool alignToSurfaceNormal = true;

    [Header("Jitter")]
    [SerializeField] private float distanceJitter = 1.5f;

    [Header("Update")]
    [SerializeField] private float updateInterval = 0.2f;

    [Header("Debug")]
    [SerializeField] private bool verboseDebug = false;

    // Path data
    private readonly List<Vector3> _path = new();
    private float[] _cumLengths;
    private float _totalLength;

    // Slot → GameObject
    private readonly Dictionary<int, GameObject> _coinsBySlot = new();
    private int _maxSlotIndex;

    private float _updateTimer;
    private int _lastClosestSegmentIndex = 0;
    private readonly List<int> _toRemove = new();

    // --- New: track active coins inside early region for cap enforcement ---
    private int _activeEarlyRegion;

    #region Lifecycle
    private void Reset()
    {
        if (trackGenerator == null)
            trackGenerator = FindObjectOfType<ProceduralTrackGenerator>();
    }

    private void Update()
    {
        if (_path.Count < 2 || playerTransform == null)
            return;

        _updateTimer += Time.deltaTime;
        if (_updateTimer >= updateInterval)
        {
            _updateTimer = 0f;
            StreamCoins();
        }
    }
    #endregion

    #region Initialization / Run
    public void InitializeForRun(ProceduralTrackGenerator generator, Transform player)
    {
        trackGenerator = generator;
        playerTransform = player;

        if (trackGenerator == null || playerTransform == null)
        {
            Debug.LogError("[TrackCoinSpawner] InitializeForRun missing references.");
            return;
        }

        RebuildPath();
        ClearAllCoins();
        SetupSlots();
        PreSpawnInitialCoins();
        _updateTimer = 0f;
    }
    #endregion

    #region Path Build
    private void RebuildPath()
    {
        _path.Clear();
        _cumLengths = null;
        _totalLength = 0f;
        _lastClosestSegmentIndex = 0;

        var src = trackGenerator?.PathPoints;
        if (src == null || src.Count < 2) return;

        if (useSmoothing)
            GenerateSmoothedPath(src, Mathf.Max(1, smoothingSubdivisionsPerSegment), _path);
        else
            _path.AddRange(src);

        if (_path.Count < 2) return;

        int n = _path.Count;
        _cumLengths = new float[n];
        _cumLengths[0] = 0f;
        float length = 0f;
        for (int i = 1; i < n; i++)
        {
            length += Vector3.Distance(_path[i - 1], _path[i]);
            _cumLengths[i] = length;
        }
        _totalLength = length;
    }

    private static void GenerateSmoothedPath(List<Vector3> raw, int subdivisions, List<Vector3> outList)
    {
        outList.Clear();
        int n = raw.Count;
        if (n < 2)
        {
            outList.AddRange(raw);
            return;
        }
        outList.Add(raw[0]);
        for (int i = 0; i < n - 1; i++)
        {
            Vector3 p0 = raw[Mathf.Max(i - 1, 0)];
            Vector3 p1 = raw[i];
            Vector3 p2 = raw[i + 1];
            Vector3 p3 = raw[Mathf.Min(i + 2, n - 1)];
            for (int s = 1; s <= subdivisions; s++)
            {
                float t = s / (float)subdivisions;
                outList.Add(CatmullRom(p0, p1, p2, p3, t));
            }
        }
    }

    private static Vector3 CatmullRom(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t)
    {
        float t2 = t * t;
        float t3 = t2 * t;
        return 0.5f * (
            (2f * p1) +
            (-p0 + p2) * t +
            (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 +
            (-p0 + 3f * p1 - 3f * p2 + p3) * t3
        );
    }
    #endregion

    #region Slots / PreSpawn
    private void SetupSlots()
    {
        if (_totalLength <= 0f || coinSpacing <= 0f)
        {
            _maxSlotIndex = 0;
            return;
        }
        _maxSlotIndex = Mathf.FloorToInt(_totalLength / coinSpacing);
    }

    private void PreSpawnInitialCoins()
    {
        if (_totalLength <= 0f || _maxSlotIndex <= 0) return;

        float endDist = Mathf.Clamp(initialPreSpawnDistance, 0f, _totalLength);
        int endSlot = Mathf.FloorToInt(endDist / coinSpacing);

        for (int slot = 0; slot <= endSlot; slot++)
        {
            if (_coinsBySlot.Count >= maxActiveCoins) break;
            float dist = slot * coinSpacing;
            TrySpawnCoinAtDistance(slot, dist, preSpawn: true);
        }
    }
    #endregion

    #region Streaming
    private void StreamCoins()
    {
        if (_totalLength <= 0f || _maxSlotIndex <= 0) return;

        float playerDist = GetPlayerDistanceAlongTrack();

        // Despawn behind
        float despawnStart = Mathf.Clamp(playerDist - despawnBehindDistance, 0f, _totalLength);
        _toRemove.Clear();
        _activeEarlyRegion = 0;

        foreach (var kvp in _coinsBySlot)
        {
            int slot = kvp.Key;
            float slotDist = slot * coinSpacing;
            if (slotDist < despawnStart)
            {
                if (kvp.Value) Destroy(kvp.Value);
                _toRemove.Add(slot);
            }
            else if (slotDist <= earlyRegionMeters)
            {
                _activeEarlyRegion++;
            }
        }
        for (int i = 0; i < _toRemove.Count; i++)
            _coinsBySlot.Remove(_toRemove[i]);

        // Spawn ahead
        float spanStart = Mathf.Clamp(playerDist + minSpawnDistanceAhead, 0f, _totalLength);
        float spanEnd = Mathf.Clamp(playerDist + maxSpawnDistanceAhead, 0f, _totalLength);

        int startSlot = Mathf.Clamp(Mathf.FloorToInt(spanStart / coinSpacing), 0, _maxSlotIndex);
        int endSlot = Mathf.Clamp(Mathf.FloorToInt(spanEnd / coinSpacing), 0, _maxSlotIndex);

        for (int slot = startSlot; slot <= endSlot; slot++)
        {
            if (_coinsBySlot.ContainsKey(slot)) continue;
            if (_coinsBySlot.Count >= maxActiveCoins) break;

            float dist = slot * coinSpacing;
            TrySpawnCoinAtDistance(slot, dist);
        }
    }
    #endregion

    #region Spawn Logic
    private void TrySpawnCoinAtDistance(int slotIndex, float distanceAlongTrack, bool preSpawn = false)
    {
        // Distance jitter
        float jitter = distanceJitter > 0f ? UnityEngine.Random.Range(-distanceJitter, distanceJitter) : 0f;
        float dist = Mathf.Clamp(distanceAlongTrack + jitter, 0f, _totalLength);

        // Compute spawn chance with early balancing and distance curve
        float spawnChance = ComputeSpawnChance(dist);
        if (UnityEngine.Random.value > spawnChance)
            return;

        // Early region active cap
        if (dist <= earlyRegionMeters && _activeEarlyRegion >= earlyRegionActiveCap)
            return;

        // Determine variant
        GameObject chosenPrefab = ResolveVariantPrefab(dist, out int overrideValue);
        if (!chosenPrefab) return;

        // Sample along path
        SampleAlongPath(dist, out var centerPos, out var forward);

        // Lateral placement
        float halfWidth = (trackGenerator != null ? trackGenerator.RoadWidth * 0.5f : 2f);
        float usableHalfWidth = Mathf.Max(0f, halfWidth * lateralFractionOfHalfWidth - edgeInnerMargin);
        if (usableHalfWidth <= 0f) usableHalfWidth = halfWidth * 0.5f;

        var flatForward = forward; flatForward.y = 0f;
        if (flatForward.sqrMagnitude < 1e-6f) flatForward = Vector3.forward;
        flatForward.Normalize();
        var right = Vector3.Cross(Vector3.up, flatForward).normalized;

        float lateral = UnityEngine.Random.Range(-usableHalfWidth, usableHalfWidth);
        Vector3 candidate = centerPos + right * lateral;

        // Raycast to road
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
        Transform parent = coinParent ? coinParent : transform;
        Vector3 spawnPos = hit.point + up * coinHeightOffset;

        GameObject inst = Instantiate(chosenPrefab, spawnPos, rot, parent);
        _coinsBySlot[slotIndex] = inst;

        if (dist <= earlyRegionMeters) _activeEarlyRegion++;

        // Override coin value if variant specifies
        if (overrideValue > 0)
        {
            var pickup = inst.GetComponent<CoinPickup>();
            if (pickup)
            {
                // reflection assign (value is private) – easier: expose public method or use serialized hack.
                // Here we simply use a helper component:
                var helper = inst.GetComponent<VariantCoinValueSetter>() ?? inst.AddComponent<VariantCoinValueSetter>();
                helper.SetValue(overrideValue);
            }
        }

        if (verboseDebug)
        {
            Debug.DrawLine(origin, hit.point, Color.yellow, 1.5f);
            Debug.DrawRay(hit.point, up, Color.green, 1.5f);
        }
    }

    private float ComputeSpawnChance(float distance)
    {
        float norm = _totalLength > 0f ? distance / _totalLength : 0f;
        float baseChance = baseSpawnChance * Mathf.Clamp01(spawnChanceDistanceCurve.Evaluate(norm));

        // Early region scaling
        if (distance <= earlyRegionMeters)
            baseChance *= earlyRegionChanceScale;

        return Mathf.Clamp01(baseChance);
    }

    private GameObject ResolveVariantPrefab(float distance, out int overrideValue)
    {
        overrideValue = 0;

        // Gather unlocked variants
        List<CoinVariant> unlocked = null;
        if (variants != null && variants.Count > 0)
        {
            unlocked = variants.FindAll(v => v != null && distance >= v.unlockDistance);
        }

        // Early restriction
        if (restrictVariantsEarly && distance <= earlyRegionMeters && unlocked != null && unlocked.Count > 0)
        {
            // Use lowest unlockDistance variant(s)
            float minUnlock = float.MaxValue;
            foreach (var v in unlocked) if (v.unlockDistance < minUnlock) minUnlock = v.unlockDistance;
            unlocked = unlocked.FindAll(v => Mathf.Approximately(v.unlockDistance, minUnlock));
        }

        // Fallback
        if (unlocked == null || unlocked.Count == 0)
        {
            if (coinPrefab)
                return coinPrefab;
            return variants != null && variants.Count > 0 && variants[0] != null ? variants[0].prefab : null;
        }

        // Weighted random with distance multiplier
        float totalWeight = 0f;
        float norm = _totalLength > 0f ? distance / _totalLength : 0f;
        foreach (var v in unlocked)
        {
            float mult = v.distanceMultiplier != null ? v.distanceMultiplier.Evaluate(norm) : 1f;
            totalWeight += Mathf.Max(0f, v.weight * mult);
        }
        if (totalWeight <= 0f) return unlocked[0].prefab;

        float pick = UnityEngine.Random.value * totalWeight;
        float accum = 0f;
        foreach (var v in unlocked)
        {
            float mult = v.distanceMultiplier != null ? v.distanceMultiplier.Evaluate(norm) : 1f;
            float w = Mathf.Max(0f, v.weight * mult);
            accum += w;
            if (pick <= accum)
            {
                overrideValue = v.overrideValue;
                return v.prefab;
            }
        }

        // Fallback
        overrideValue = unlocked[unlocked.Count - 1].overrideValue;
        return unlocked[unlocked.Count - 1].prefab;
    }
    #endregion

    #region Helpers
    private void SampleAlongPath(float targetDistance, out Vector3 pos, out Vector3 forward)
    {
        pos = Vector3.zero;
        forward = Vector3.forward;

        if (_path.Count < 2 || _cumLengths == null) return;

        targetDistance = Mathf.Clamp(targetDistance, 0f, _totalLength);

        int idx = 0;
        for (int i = 0; i < _cumLengths.Length - 1; i++)
        {
            if (_cumLengths[i + 1] >= targetDistance)
            {
                idx = i;
                break;
            }
        }

        float segStart = _cumLengths[idx];
        float segEnd = _cumLengths[Mathf.Min(idx + 1, _cumLengths.Length - 1)];
        float segLen = Mathf.Max(0.0001f, segEnd - segStart);
        float t = Mathf.Clamp01((targetDistance - segStart) / segLen);

        Vector3 a = _path[idx];
        Vector3 b = _path[Mathf.Min(idx + 1, _path.Count - 1)];
        pos = Vector3.Lerp(a, b, t);
        forward = (b - a).normalized;
    }

    private float GetPlayerDistanceAlongTrack()
    {
        if (_path.Count < 2 || _cumLengths == null) return 0f;
        Vector3 p = playerTransform.position;

        int bestIndex = _lastClosestSegmentIndex;
        float bestSqrDist = float.MaxValue;

        for (int i = 0; i < _path.Count - 1; i++)
        {
            Vector3 a = _path[i];
            Vector3 b = _path[i + 1];
            Vector3 ab = b - a;
            float abSqrMag = ab.sqrMagnitude;
            if (abSqrMag < 1e-6f) continue;

            float t = Vector3.Dot(p - a, ab) / abSqrMag;
            t = Mathf.Clamp01(t);
            Vector3 proj = a + ab * t;
            float sqrDist = (p - proj).sqrMagnitude;

            if (sqrDist < bestSqrDist)
            {
                bestSqrDist = sqrDist;
                bestIndex = i;
            }
        }

        _lastClosestSegmentIndex = bestIndex;

        Vector3 aa = _path[bestIndex];
        Vector3 bb = _path[bestIndex + 1];
        Vector3 ab2 = bb - aa;
        float ab2Sqr = ab2.sqrMagnitude;
        float segT = 0f;
        float segLen = 0f;

        if (ab2Sqr > 0.0001f)
        {
            segLen = Mathf.Sqrt(ab2Sqr);
            segT = Mathf.Clamp01(Vector3.Dot(p - aa, ab2) / ab2Sqr);
        }

        float baseDist = _cumLengths[bestIndex];
        float dist = baseDist + segT * segLen;
        return dist;
    }

    private void ClearAllCoins()
    {
        foreach (var kvp in _coinsBySlot)
            if (kvp.Value) Destroy(kvp.Value);
        _coinsBySlot.Clear();
    }
    #endregion

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.yellow;
        for (int i = 0; i < _path.Count - 1; i++)
            Gizmos.DrawLine(_path[i], _path[i + 1]);

        if (earlyRegionMeters > 0f)
        {
            Gizmos.color = new Color(0f, 0.6f, 1f, 0.25f);
            Vector3 start = _path.Count > 0 ? _path[0] : transform.position;
            Gizmos.DrawWireSphere(start, 0.4f);
        }
    }
#endif
}

/// <summary>
/// Helper component to override coin pickup value at spawn without changing CoinPickup internals.
/// </summary>
[DisallowMultipleComponent]
public class VariantCoinValueSetter : MonoBehaviour
{
    public void SetValue(int v)
    {
        var pickup = GetComponent<CoinPickup>();
        if (!pickup) return;
        // Using reflection to set private serialized field 'value'
        var fi = typeof(CoinPickup).GetField("value", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        if (fi != null) fi.SetValue(pickup, v);
    }
}