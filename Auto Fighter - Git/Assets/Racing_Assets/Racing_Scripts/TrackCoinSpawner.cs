using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Streams coin pickups along the procedural track,
/// spawning them far enough ahead that they never visibly pop in,
/// and despawning them behind the player.
/// </summary>
public class TrackCoinSpawner : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private ProceduralTrackGenerator trackGenerator;
    [SerializeField] private Transform playerTransform;
    [SerializeField] private GameObject coinPrefab;
    [SerializeField] private Transform coinParent; // optional

    [Header("Coin Height")]
    [Tooltip("Extra height above the road surface to place the coin.")]
    [SerializeField] private float coinHeightOffset = 0.5f;

    [Header("Path Sampling")]
    [Tooltip("Use a smoothed path (Catmull-Rom) for coin placement.")]
    [SerializeField] private bool useSmoothing = true;
    [SerializeField, Min(1)] private int smoothingSubdivisionsPerSegment = 6;

    [Header("Coin Slot Layout")]
    [Tooltip("Distance between coin slots along the track (meters).")]
    [SerializeField, Min(0.1f)] private float coinSpacing = 6f;

    [Tooltip("Max total coins that can exist at once.")]
    [SerializeField] private int maxActiveCoins = 120;

    [Header("Spawn Window (ahead of player)")]
    [Tooltip("Minimum distance in front of the player where new coins are allowed to appear.")]
    [SerializeField] private float minSpawnDistanceAhead = 40f;

    [Tooltip("Maximum distance in front of the player we bother filling with coins.")]
    [SerializeField] private float maxSpawnDistanceAhead = 140f;

    [Tooltip("How far behind the player we allow coins to exist before despawning.")]
    [SerializeField] private float despawnBehindDistance = 25f;

    [Header("Initial Pre-Spawn")]
    [Tooltip("How far ahead (from start) we pre-fill coins before the run begins.")]
    [SerializeField] private float initialPreSpawnDistance = 80f;

    [Header("Randomization")]
    [Tooltip("Max random offset along the track (meters) applied per spawned coin.")]
    [SerializeField] private float distanceJitter = 1.5f;

    [Tooltip("Probability [0-1] that a given slot will actually spawn a coin.")]
    [SerializeField, Range(0f, 1f)] private float spawnChancePerSlot = 0.85f;

    [Header("Lateral Placement (across road width)")]
    [Tooltip("Fraction of half-width where coins can appear (0..1).")]
    [SerializeField, Range(0f, 1f)] private float lateralFractionOfHalfWidth = 0.7f;

    [Tooltip("Margin from the physical edge of the road in meters.")]
    [SerializeField] private float edgeInnerMargin = 0.25f;

    [Header("Raycast Settings")]
    [SerializeField] private LayerMask roadLayerMask = ~0;
    [SerializeField] private float raycastStartHeight = 5f;
    [SerializeField] private float raycastDownDistance = 15f;
    [SerializeField] private bool alignToSurfaceNormal = true;

    [Header("Update Settings")]
    [Tooltip("How often to update streaming (seconds).")]
    [SerializeField] private float updateInterval = 0.2f;

    [Header("Debug")]
    [SerializeField] private bool verboseDebug = false;

    // Internal path data
    private readonly List<Vector3> _path = new List<Vector3>();
    private float[] _cumLengths;
    private float _totalLength;

    // Coin slots: slotIndex -> coin GameObject
    private readonly Dictionary<int, GameObject> _coinsBySlot = new Dictionary<int, GameObject>();
    private int _maxSlotIndex;

    // Update timer
    private float _updateTimer;

    // Cache for closest-segment search
    private int _lastClosestSegmentIndex = 0;

    // temp list for removals
    private readonly List<int> _toRemove = new List<int>(64);

    // ----------------------------------------------------------
    // LIFE CYCLE
    // ----------------------------------------------------------
    private void Reset()
    {
        if (trackGenerator == null)
            trackGenerator = FindObjectOfType<ProceduralTrackGenerator>();
    }

    private void Update()
    {
        if (_path.Count < 2 || playerTransform == null || coinPrefab == null)
            return;

        _updateTimer += Time.deltaTime;
        if (_updateTimer < updateInterval)
            return;

        _updateTimer = 0f;
        StreamCoins();
    }

    // ----------------------------------------------------------
    // PATH BUILDING
    // ----------------------------------------------------------
    private void RebuildPath()
    {
        _path.Clear();
        _cumLengths = null;
        _totalLength = 0f;
        _lastClosestSegmentIndex = 0;

        if (trackGenerator == null)
        {
            if (verboseDebug) Debug.LogWarning("[TrackCoinSpawner] No trackGenerator assigned.");
            return;
        }

        var src = trackGenerator.PathPoints;
        if (src == null || src.Count < 2)
        {
            if (verboseDebug) Debug.LogWarning("[TrackCoinSpawner] Track has too few path points.");
            return;
        }

        if (useSmoothing)
            GenerateSmoothedPath(src, Mathf.Max(1, smoothingSubdivisionsPerSegment), _path);
        else
            _path.AddRange(src);

        if (_path.Count < 2)
            return;

        // cumulative lengths
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

        if (verboseDebug)
            Debug.Log($"[TrackCoinSpawner] Path rebuilt: {_path.Count} points, length ~ {_totalLength:0.0}m");
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

    // ----------------------------------------------------------
    // SLOT SETUP
    // ----------------------------------------------------------
    private void SetupSlots()
    {
        _coinsBySlot.Clear();

        if (_totalLength <= 0f || coinSpacing <= 0f)
        {
            _maxSlotIndex = 0;
            return;
        }

        _maxSlotIndex = Mathf.FloorToInt(_totalLength / coinSpacing);
        if (verboseDebug)
            Debug.Log($"[TrackCoinSpawner] Slot setup: {_maxSlotIndex + 1} potential slots along track.");
    }

    private void ClearAllCoins()
    {
        foreach (var kvp in _coinsBySlot)
        {
            if (kvp.Value != null)
                Destroy(kvp.Value);
        }
        _coinsBySlot.Clear();
    }

    // ----------------------------------------------------------
    // STREAMING LOOP
    // ----------------------------------------------------------
    private void StreamCoins()
    {
        if (_totalLength <= 0f || _maxSlotIndex <= 0)
            return;

        // 1) Get player distance along the track
        float playerDist = GetPlayerDistanceAlongTrack();

        // 2) Define the forward spawn band where new coins are allowed
        float spawnStartDist = Mathf.Clamp(playerDist + minSpawnDistanceAhead, 0f, _totalLength);
        float spawnEndDist = Mathf.Clamp(playerDist + maxSpawnDistanceAhead, 0f, _totalLength);

        int startSlot = Mathf.Clamp(Mathf.FloorToInt(spawnStartDist / coinSpacing), 0, _maxSlotIndex);
        int endSlot = Mathf.Clamp(Mathf.FloorToInt(spawnEndDist / coinSpacing), 0, _maxSlotIndex);

        // 3) Spawn coins in the far-ahead band only
        for (int slot = startSlot; slot <= endSlot; slot++)
        {
            if (_coinsBySlot.ContainsKey(slot))
                continue; // already spawned

            if (_coinsBySlot.Count >= maxActiveCoins)
                break;

            float dist = slot * coinSpacing;

            // Safety: never spawn closer than minSpawnDistanceAhead in front of player
            if (dist < playerDist + minSpawnDistanceAhead)
                continue;

            if (Random.value > spawnChancePerSlot)
                continue;

            TrySpawnCoinAtDistance(slot, dist);
        }

        // 4) Despawn coins that are too far behind
        float hardDespawnStart = Mathf.Clamp(playerDist - despawnBehindDistance, 0f, _totalLength);

        _toRemove.Clear();
        foreach (var kvp in _coinsBySlot)
        {
            int slot = kvp.Key;
            float slotDist = slot * coinSpacing;

            // Only despawn behind; allow coins far ahead to exist
            if (slotDist < hardDespawnStart)
            {
                if (kvp.Value != null)
                    Destroy(kvp.Value);

                _toRemove.Add(slot);
            }
        }
        for (int i = 0; i < _toRemove.Count; i++)
        {
            _coinsBySlot.Remove(_toRemove[i]);
        }
    }

    // ----------------------------------------------------------
    // PLAYER DISTANCE APPROX
    // ----------------------------------------------------------
    private float GetPlayerDistanceAlongTrack()
    {
        if (_path.Count < 2 || _cumLengths == null)
            return 0f;

        Vector3 p = playerTransform.position;

        int bestIndex = _lastClosestSegmentIndex;
        float bestSqrDist = float.MaxValue;

        // Simple O(N) search (N ~ ~1000) is fine at 5 Hz
        for (int i = 0; i < _path.Count - 1; i++)
        {
            Vector3 a = _path[i];
            Vector3 b = _path[i + 1];
            Vector3 ab = b - a;
            float abSqrMag = ab.sqrMagnitude;
            if (abSqrMag < 0.0001f)
                continue;

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

    // ----------------------------------------------------------
    // COIN SPAWN HELPER
    // ----------------------------------------------------------
    private void TrySpawnCoinAtDistance(int slotIndex, float distanceAlongTrack)
    {
        // 🎲 randomize along-track position a bit so spacing isn't perfect
        float jitter = (distanceJitter > 0f)
            ? Random.Range(-distanceJitter, distanceJitter)
            : 0f;

        float sampleDist = Mathf.Clamp(distanceAlongTrack + jitter, 0f, _totalLength);

        Vector3 centerPos;
        Vector3 forward;
        SampleAlongPath(sampleDist, out centerPos, out forward);

        if (forward.sqrMagnitude < 0.0001f)
            forward = Vector3.forward;

        // Lateral placement
        float halfWidth = (trackGenerator != null) ? trackGenerator.RoadWidth * 0.5f : 2f;
        float usableHalfWidth = Mathf.Max(0f, halfWidth * lateralFractionOfHalfWidth - edgeInnerMargin);
        if (usableHalfWidth <= 0f)
            usableHalfWidth = halfWidth * 0.5f; // fallback

        // Flatten forward to XZ for right direction
        Vector3 flatForward = forward;
        flatForward.y = 0f;
        if (flatForward.sqrMagnitude < 0.0001f)
            flatForward = Vector3.forward;
        flatForward.Normalize();

        Vector3 right = Vector3.Cross(Vector3.up, flatForward).normalized;

        // 🎲 lateral offset once per slot, fully random within allowed band
        float lateral = Random.Range(-usableHalfWidth, usableHalfWidth);
        Vector3 candidatePos = centerPos + right * lateral;

        // Raycast down to road
        Vector3 origin = candidatePos + Vector3.up * raycastStartHeight;
        float maxDist = raycastStartHeight + raycastDownDistance;

        if (Physics.Raycast(origin, Vector3.down, out RaycastHit hit, maxDist, roadLayerMask, QueryTriggerInteraction.Ignore))
        {
            Vector3 up = alignToSurfaceNormal ? hit.normal : Vector3.up;

            Vector3 fwdOnSurface = Vector3.ProjectOnPlane(flatForward, up);
            if (fwdOnSurface.sqrMagnitude < 0.0001f)
                fwdOnSurface = Vector3.Cross(up, Vector3.right);
            fwdOnSurface.Normalize();

            Quaternion rot = Quaternion.LookRotation(fwdOnSurface, up);
            Transform parent = coinParent != null ? coinParent : transform;

            // ⬆ apply height offset relative to surface normal
            Vector3 spawnPos = hit.point + up * coinHeightOffset;

            GameObject coin = Instantiate(coinPrefab, spawnPos, rot, parent);
            _coinsBySlot[slotIndex] = coin;

#if UNITY_EDITOR
            if (verboseDebug)
            {
                Debug.DrawLine(origin, hit.point, Color.yellow, 2f);
                Debug.DrawRay(hit.point, up, Color.green, 2f);
                Debug.DrawRay(hit.point, fwdOnSurface, Color.blue, 2f);
            }
#endif
        }
        else if (verboseDebug)
        {
            Debug.DrawRay(origin, Vector3.down * maxDist, Color.red, 2f);
            Debug.LogWarning($"[TrackCoinSpawner] Raycast missed road at dist {distanceAlongTrack:0.0}.");
        }
    }

    public void SetPlayerTransform(Transform player)
    {
        playerTransform = player;
    }

    private void SampleAlongPath(float targetDistance, out Vector3 pos, out Vector3 forward)
    {
        pos = Vector3.zero;
        forward = Vector3.forward;

        if (_path.Count < 2 || _cumLengths == null)
            return;

        targetDistance = Mathf.Clamp(targetDistance, 0f, _totalLength);

        int idx = 0;
        // simple linear search; can be upgraded to binary search if you want
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

    // ----------------------------------------------------------
    // INITIALIZE & PRE-SPAWN
    // ----------------------------------------------------------
    public void InitializeForRun(ProceduralTrackGenerator generator, Transform player)
    {
        trackGenerator = generator;
        playerTransform = player;

        if (trackGenerator == null || playerTransform == null)
        {
            Debug.LogError("[TrackCoinSpawner] InitializeForRun missing refs. " +
                           $"trackGenerator={trackGenerator}, player={playerTransform}");
            return;
        }

        if (verboseDebug)
            Debug.Log("[TrackCoinSpawner] InitializeForRun: rebuilding path + slots.");

        RebuildPath();
        ClearAllCoins();
        SetupSlots();

        // Pre-fill the first part of the track so nothing pops in during countdown / early movement
        PreSpawnInitialCoins();

        _updateTimer = 0f;
    }

    private void PreSpawnInitialCoins()
    {
        if (_totalLength <= 0f || _maxSlotIndex <= 0)
            return;

        float preSpawnEnd = Mathf.Clamp(initialPreSpawnDistance, 0f, _totalLength);
        int endSlot = Mathf.FloorToInt(preSpawnEnd / coinSpacing);

        for (int slot = 0; slot <= endSlot; slot++)
        {
            if (_coinsBySlot.ContainsKey(slot))
                continue;

            if (_coinsBySlot.Count >= maxActiveCoins)
                break;

            float dist = slot * coinSpacing;

            if (Random.value > spawnChancePerSlot)
                continue;

            TrySpawnCoinAtDistance(slot, dist);
        }

        if (verboseDebug)
            Debug.Log($"[TrackCoinSpawner] PreSpawnInitialCoins spawned {_coinsBySlot.Count} coins up to {preSpawnEnd:0.0}m.");
    }
}
