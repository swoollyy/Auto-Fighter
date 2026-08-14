using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public class TrackHPSpawner : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private ProceduralTrackGenerator trackGenerator;
    [SerializeField] private Transform playerTransform;

    [Header("Prefab")]
    [SerializeField] private GameObject hpPrefab;

    [Header("Spawn Layout")]
    [SerializeField, Min(0.1f)] private float pickupSpacing = 22f;
    [SerializeField] private int maxActivePickups = 64;
    [SerializeField] private float minSpawnDistanceAhead = 40f;
    [SerializeField] private float maxSpawnDistanceAhead = 140f;
    [SerializeField] private float despawnBehindDistance = 25f;
    [SerializeField] private float initialPreSpawnDistance = 80f;

    [Header("Spawn Probability")]
    [Tooltip("Chance a pickup slot fills (0–1). X = at track start, Y = at track end.")]
    [SerializeField] private Vector2 spawnChanceByProgress = new Vector2(0.12f, 0.12f);

    [Header("Placement")]
    [SerializeField] private float heightOffset = 0.5f;
    [SerializeField, Range(0f, 1f)] private float lateralFractionOfHalfWidth = 0.6f;
    [SerializeField] private float edgeInnerMargin = 0.25f;

    [Header("Raycast")]
    [SerializeField] private LayerMask roadLayerMask = ~0;
    [SerializeField] private float raycastStartHeight = 5f;
    [SerializeField] private float raycastDownDistance = 15f;
    [SerializeField] private bool alignToSurfaceNormal = true;

    [Header("Smoothing & Update")]
    [SerializeField] private bool useSmoothing = true;
    [SerializeField, Min(1)] private int smoothingSubdivisionsPerSegment = 6;
    [SerializeField] private float updateInterval = 0.25f;
    [SerializeField] private float distanceJitter = 1.5f;

    [Header("Debug")]
    [SerializeField] private bool verboseDebug = false;

    private readonly List<Vector3> _path = new();
    private float[] _cumLengths;
    private float _totalLength;
    private int _maxSlotIndex;

    private readonly Dictionary<int, GameObject> _activeBySlot = new();
    private readonly List<int> _toRemove = new();

    private float _updateT;
    private int _lastClosestSegmentIndex;

    public void InitializeForRun(ProceduralTrackGenerator generator, Transform player)
    {
        trackGenerator = generator;
        playerTransform = player;
        RebuildPath();
        ClearAll();
        SetupSlots();

        // Only pre-spawn when the unlock skill is owned; otherwise leave the track empty.
        var mgr = RacingSkillTreeManager.Instance;
        if (mgr != null && mgr.IsHPPickupUnlocked())
            PreSpawnInitial();

        _updateT = 0f;
    }

    private void Update()
    {
        if (_path.Count < 2 || playerTransform == null) return;

        var mgr = RacingSkillTreeManager.Instance;
        if (mgr == null || !mgr.IsHPPickupUnlocked())
            return;

        _updateT += Time.deltaTime;
        if (_updateT >= updateInterval)
        {
            _updateT = 0f;
            Stream();
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
        if (n < 2) { outList.AddRange(raw); return; }
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
        if (_totalLength <= 0f || pickupSpacing <= 0f) { _maxSlotIndex = 0; return; }
        _maxSlotIndex = Mathf.FloorToInt(_totalLength / pickupSpacing);
    }

    private void PreSpawnInitial()
    {
        if (_totalLength <= 0f || _maxSlotIndex <= 0) return;
        float endDist = Mathf.Clamp(initialPreSpawnDistance, 0f, _totalLength);
        int endSlot = Mathf.FloorToInt(endDist / pickupSpacing);

        for (int slot = 0; slot <= endSlot; slot++)
        {
            if (_activeBySlot.Count >= maxActivePickups) break;
            float dist = slot * pickupSpacing;
            TrySpawnAtDistance(slot, dist, preSpawn: true);
        }
    }

    private void Stream()
    {
        if (_totalLength <= 0f || _maxSlotIndex <= 0) return;

        float playerDist = GetPlayerDistanceAlongPath();
        float despawnStart = Mathf.Clamp(playerDist - despawnBehindDistance, 0f, _totalLength);

        _toRemove.Clear();
        foreach (var kvp in _activeBySlot)
        {
            float slotDist = kvp.Key * pickupSpacing;
            if (slotDist < despawnStart)
            {
                if (kvp.Value) Destroy(kvp.Value);
                _toRemove.Add(kvp.Key);
            }
        }
        for (int i = 0; i < _toRemove.Count; i++)
            _activeBySlot.Remove(_toRemove[i]);

        float spanStart = Mathf.Clamp(playerDist + minSpawnDistanceAhead, 0f, _totalLength);
        float spanEnd = Mathf.Clamp(playerDist + maxSpawnDistanceAhead, 0f, _totalLength);

        int startSlot = Mathf.Clamp(Mathf.FloorToInt(spanStart / pickupSpacing), 0, _maxSlotIndex);
        int endSlot = Mathf.Clamp(Mathf.FloorToInt(spanEnd / pickupSpacing), 0, _maxSlotIndex);

        for (int slot = startSlot; slot <= endSlot; slot++)
        {
            if (_activeBySlot.ContainsKey(slot)) continue;
            if (_activeBySlot.Count >= maxActivePickups) break;
            float dist = slot * pickupSpacing;
            TrySpawnAtDistance(slot, dist, preSpawn: false);
        }
    }

    private void TrySpawnAtDistance(int slotIndex, float distanceAlongTrack, bool preSpawn)
    {
        var unlockMgr = RacingSkillTreeManager.Instance;
        if (unlockMgr != null && !unlockMgr.IsHPPickupUnlocked())
            return;

        float jitter = distanceJitter > 0f ? Random.Range(-distanceJitter, distanceJitter) : 0f;
        float dist = Mathf.Clamp(distanceAlongTrack + jitter, 0f, _totalLength);

        float norm = _totalLength > 0f ? dist / _totalLength : 0f;

        float chance = TrackProgressRange.Lerp01(spawnChanceByProgress, norm);

        var mgr = RacingSkillTreeManager.Instance;
        if (mgr != null)
        {
            // small base bonus when unlocked
            if (mgr.IsHPPickupUnlocked())
                chance += 0.05f;

            chance *= mgr.GetHPPickupSpawnRateMultiplier();
        }

        chance = Mathf.Clamp01(chance);
        if (Random.value > chance)
            return;

        SampleAlongPath(dist, out var center, out var forward);

        float halfWidth = (trackGenerator != null ? trackGenerator.RoadWidth * 0.5f : 2f);
        float usableHalfWidth = Mathf.Max(0f, halfWidth * lateralFractionOfHalfWidth - edgeInnerMargin);
        if (usableHalfWidth <= 0f) usableHalfWidth = halfWidth * 0.5f;

        var flatFwd = forward; flatFwd.y = 0f;
        if (flatFwd.sqrMagnitude < 1e-6f) flatFwd = Vector3.forward;
        flatFwd.Normalize();
        var right = Vector3.Cross(Vector3.up, flatFwd).normalized;

        float lateral = Random.Range(-usableHalfWidth, usableHalfWidth);
        Vector3 candidate = center + right * lateral;

        Vector3 origin = candidate + Vector3.up * raycastStartHeight;
        float maxDist = raycastStartHeight + raycastDownDistance;

        if (!Physics.Raycast(origin, Vector3.down, out var hit, maxDist, roadLayerMask, QueryTriggerInteraction.Ignore))
        {
            if (verboseDebug) Debug.DrawRay(origin, Vector3.down * maxDist, Color.red, 2f);
            return;
        }

        Vector3 up = alignToSurfaceNormal ? hit.normal : Vector3.up;
        Vector3 fOnSurface = Vector3.ProjectOnPlane(flatFwd, up);
        if (fOnSurface.sqrMagnitude < 1e-6f)
            fOnSurface = Vector3.Cross(up, Vector3.right);
        fOnSurface.Normalize();

        Quaternion rot = Quaternion.LookRotation(fOnSurface, up);
        Vector3 spawnPos = hit.point + up * heightOffset;

        if (!hpPrefab) return;

        var inst = Instantiate(hpPrefab, spawnPos, rot, transform);
        _activeBySlot[slotIndex] = inst;

        if (verboseDebug)
        {
            Debug.DrawLine(origin, hit.point, Color.yellow, 1.5f);
            Debug.DrawRay(hit.point, up, Color.green, 1.5f);
        }
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
            if (_cumLengths[i + 1] >= targetDistance) { idx = i; break; }
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

    private float GetPlayerDistanceAlongPath()
    {
        if (_path.Count < 2 || _cumLengths == null || playerTransform == null) return 0f;
        Vector3 p = playerTransform.position;

        int bestIndex = _lastClosestSegmentIndex;
        float bestSqrDist = float.MaxValue;

        for (int i = 0; i < _path.Count - 1; i++)
        {
            Vector3 a = _path[i];
            Vector3 b = _path[i + 1];
            Vector3 ab = b - a;
            float abSqr = ab.sqrMagnitude;
            if (abSqr < 1e-6f) continue;

            float t = Vector3.Dot(p - a, ab) / abSqr;
            t = Mathf.Clamp01(t);
            Vector3 proj = a + ab * t;
            float sqrDist = (p - proj).sqrMagnitude;

            if (sqrDist < bestSqrDist) { bestSqrDist = sqrDist; bestIndex = i; }
        }

        _lastClosestSegmentIndex = bestIndex;

        Vector3 aa = _path[bestIndex];
        Vector3 bb = _path[bestIndex + 1];
        Vector3 ab2 = bb - aa;
        float ab2Sqr = ab2.sqrMagnitude;
        float segT = 0f, segLen = 0f;

        if (ab2Sqr > 0.0001f)
        {
            segLen = Mathf.Sqrt(ab2Sqr);
            segT = Mathf.Clamp01(Vector3.Dot(p - aa, ab2) / ab2Sqr);
        }

        float baseDist = _cumLengths[bestIndex];
        return baseDist + segT * segLen;
    }

    private void ClearAll()
    {
        foreach (var kvp in _activeBySlot)
            if (kvp.Value) Destroy(kvp.Value);
        _activeBySlot.Clear();
    }
}