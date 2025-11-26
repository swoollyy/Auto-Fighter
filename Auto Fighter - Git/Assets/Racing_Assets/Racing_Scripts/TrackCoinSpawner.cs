using System;
using System.Collections.Generic;
using UnityEngine;

public class TrackCoinSpawner : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private ProceduralTrackGenerator trackGenerator;
    [SerializeField] private Transform playerTransform;

    [Header("Prefabs (Order: Bronze / Silver / Gold)")]
    [SerializeField] private GameObject bronzePrefab;
    [SerializeField] private GameObject silverPrefab;
    [SerializeField] private GameObject goldPrefab;

    [Header("Coin Values")]
    [SerializeField] private int bronzeValue = 1;
    [SerializeField] private int silverValue = 3;
    [SerializeField] private int goldValue = 8;

    [Header("Weight Curves (Track Fraction 0..1)")]
    [Tooltip("Bronze starts high then fades out strongly by mid/end.")]
    [SerializeField]
    private AnimationCurve bronzeWeightCurve = new AnimationCurve(
        new Keyframe(0f, 1f),
        new Keyframe(0.25f, 1f),
        new Keyframe(0.45f, 0.6f),
        new Keyframe(0.60f, 0.25f),
        new Keyframe(0.75f, 0.12f),
        new Keyframe(0.90f, 0.05f),
        new Keyframe(1f, 0.03f)
    );

    [Tooltip("Silver ramps up, dominates mid track, tapers near end.")]
    [SerializeField]
    private AnimationCurve silverWeightCurve = new AnimationCurve(
        new Keyframe(0f, 0f),
        new Keyframe(0.15f, 0.25f),
        new Keyframe(0.30f, 0.9f),
        new Keyframe(0.50f, 1.2f),
        new Keyframe(0.65f, 1.1f),
        new Keyframe(0.80f, 0.7f),
        new Keyframe(1f, 0.4f)
    );

    [Tooltip("Gold very rare early, increases late and becomes dominant.")]
    [SerializeField]
    private AnimationCurve goldWeightCurve = new AnimationCurve(
        new Keyframe(0f, 0f),
        new Keyframe(0.25f, 0.05f),
        new Keyframe(0.40f, 0.15f),
        new Keyframe(0.55f, 0.25f),
        new Keyframe(0.70f, 0.55f),
        new Keyframe(0.85f, 1.2f),
        new Keyframe(0.95f, 1.4f),
        new Keyframe(1f, 1.5f)
    );

    [Header("Global Variant Tweaks")]
    [SerializeField, Tooltip("Extra overall scaling applied to bronze weights.")]
    private float bronzeGlobalScale = 1f;
    [SerializeField, Tooltip("Extra overall scaling applied to silver weights.")]
    private float silverGlobalScale = 1f;
    [SerializeField, Tooltip("Extra overall scaling applied to gold weights.")]
    private float goldGlobalScale = 1f;

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
            TrySpawnCoinAtDistance(slot, dist, true);
        }
    }

    private void StreamCoins()
    {
        if (_totalLength <= 0f || _maxSlotIndex <= 0) return;

        float playerDist = GetPlayerDistanceAlongTrack();
        float despawnStart = Mathf.Clamp(playerDist - despawnBehindDistance, 0f, _totalLength);

        _toRemove.Clear();
        foreach (var kvp in _coinsBySlot)
        {
            float slotDist = kvp.Key * coinSpacing;
            if (slotDist < despawnStart)
            {
                if (kvp.Value) Destroy(kvp.Value);
                _toRemove.Add(kvp.Key);
            }
        }
        for (int i = 0; i < _toRemove.Count; i++)
            _coinsBySlot.Remove(_toRemove[i]);

        float spanStart = Mathf.Clamp(playerDist + minSpawnDistanceAhead, 0f, _totalLength);
        float spanEnd = Mathf.Clamp(playerDist + maxSpawnDistanceAhead, 0f, _totalLength);

        int startSlot = Mathf.Clamp(Mathf.FloorToInt(spanStart / coinSpacing), 0, _maxSlotIndex);
        int endSlot = Mathf.Clamp(Mathf.FloorToInt(spanEnd / coinSpacing), 0, _maxSlotIndex);

        for (int slot = startSlot; slot <= endSlot; slot++)
        {
            if (_coinsBySlot.ContainsKey(slot)) continue;
            if (_coinsBySlot.Count >= maxActiveCoins) break;
            float dist = slot * coinSpacing;
            TrySpawnCoinAtDistance(slot, dist, false);
        }
    }

    private void TrySpawnCoinAtDistance(int slotIndex, float distanceAlongTrack, bool preSpawn)
    {
        float jitter = distanceJitter > 0f ? UnityEngine.Random.Range(-distanceJitter, distanceJitter) : 0f;
        float dist = Mathf.Clamp(distanceAlongTrack + jitter, 0f, _totalLength);

        float spawnChance = ComputeSpawnChance(dist);
        if (UnityEngine.Random.value > spawnChance)
            return;

        // Compute normalized distance
        float norm = _totalLength > 0f ? dist / _totalLength : 0f;

        // Variant weights
        float wBronze = Mathf.Max(0f, bronzeWeightCurve != null ? bronzeWeightCurve.Evaluate(norm) * bronzeGlobalScale : 0f);
        float wSilver = Mathf.Max(0f, silverWeightCurve != null ? silverWeightCurve.Evaluate(norm) * silverGlobalScale : 0f);
        float wGold = Mathf.Max(0f, goldWeightCurve != null ? goldWeightCurve.Evaluate(norm) * goldGlobalScale : 0f);

        float total = wBronze + wSilver + wGold;
        if (total <= 0f) return;

        float pick = UnityEngine.Random.value * total;
        GameObject chosen = null;
        int chosenValue = 1;

        if (pick <= wBronze)
        {
            chosen = bronzePrefab;
            chosenValue = bronzeValue;
        }
        else if (pick <= wBronze + wSilver)
        {
            chosen = silverPrefab;
            chosenValue = silverValue;
        }
        else
        {
            chosen = goldPrefab;
            chosenValue = goldValue;
        }

        if (!chosen) return;

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

        GameObject inst = Instantiate(chosen, spawnPos, rot, parent);
        _coinsBySlot[slotIndex] = inst;

        // Assign value if coin has CoinPickup
        var cp = inst.GetComponent<CoinPickup>();
        if (cp != null)
        {
            // Reflection set (private serialized field 'value')
            var fi = typeof(CoinPickup).GetField("value", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (fi != null) fi.SetValue(cp, chosenValue);
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
        return baseDist + segT * segLen;
    }

    private void ClearAllCoins()
    {
        foreach (var kvp in _coinsBySlot)
            if (kvp.Value) Destroy(kvp.Value);
        _coinsBySlot.Clear();
    }

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        if (!showWeightsGizmo || _path.Count < 2 || _totalLength <= 0f)
            return;

        Gizmos.color = Color.white;
        Vector3 basePos = _path.Count > 0 ? _path[0] : transform.position;

        float step = 1f / Mathf.Max(1, gizmoSamples);
        for (int i = 0; i <= gizmoSamples; i++)
        {
            float f = i * step;
            float wb = bronzeWeightCurve != null ? bronzeWeightCurve.Evaluate(f) * bronzeGlobalScale : 0f;
            float ws = silverWeightCurve != null ? silverWeightCurve.Evaluate(f) * silverGlobalScale : 0f;
            float wg = goldWeightCurve != null ? goldWeightCurve.Evaluate(f) * goldGlobalScale : 0f;
            float sum = wb + ws + wg + 0.0001f;

            float yOffset = 0.5f;
            Vector3 bronzeP = basePos + Vector3.right * (f * 10f) + Vector3.up * (wb / sum + yOffset);
            Vector3 silverP = basePos + Vector3.right * (f * 10f) + Vector3.up * (ws / sum + yOffset);
            Vector3 goldP = basePos + Vector3.right * (f * 10f) + Vector3.up * (wg / sum + yOffset);

            Gizmos.color = new Color(0.8f, 0.5f, 0.2f, 0.9f);
            Gizmos.DrawSphere(bronzeP, 0.06f);
            Gizmos.color = new Color(0.75f, 0.75f, 0.75f, 0.9f);
            Gizmos.DrawSphere(silverP, 0.06f);
            Gizmos.color = new Color(0.95f, 0.85f, 0.15f, 0.9f);
            Gizmos.DrawSphere(goldP, 0.06f);
        }
    }
#endif
}