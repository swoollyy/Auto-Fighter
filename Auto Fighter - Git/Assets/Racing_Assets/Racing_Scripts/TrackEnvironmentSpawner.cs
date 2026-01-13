using System.Collections.Generic;
using UnityEngine;

[System.Serializable]
public class EnvironmentType
{
    public string id;
    public GameObject prefab;
    [Min(0f)] public float baseWeight = 1f;

    [Header("Placement Tweaks")]
    public float extraHeightOffset = 0f;
    public float extraLateralPadding = 0f;
}

public class TrackEnvironmentSpawner : MonoBehaviour
{
    public enum EnvSpawnMode
    {
        PopulateOnceAfterTrack,   // default: stable fill across whole track
        StreamAroundPlayer        // optional: stream band near player
    }


    [Header("Spawned Instance Layer Override")]
    [Tooltip("If enabled, every spawned environment instance will be forced to this layer (including all children).")]
    [SerializeField] private bool overrideSpawnedLayer = true;

    [Tooltip("Layer index to assign to spawned environment (ex: Environment).")]
    [SerializeField] private int spawnedEnvironmentLayer = 0; // set in Inspector

    [Tooltip("Apply layer override recursively to all children.")]
    [SerializeField] private bool applyLayerRecursively = true;


    [Header("References")]
    [SerializeField] private ProceduralTrackGenerator trackGenerator;
    [SerializeField] private Transform playerTransform;
    [SerializeField] private Transform environmentParent;

    [Header("Types")]
    [SerializeField] private List<EnvironmentType> environmentTypes = new();

    [Header("Spawn Mode")]
    [SerializeField] private EnvSpawnMode spawnMode = EnvSpawnMode.PopulateOnceAfterTrack;

    [Header("Path Sampling")]
    [SerializeField] private bool useSmoothing = true;
    [SerializeField, Min(1)] private int smoothingSubdivisionsPerSegment = 6;

    [Header("Spawn Settings")]
    [SerializeField, Min(0.5f)] private float spacing = 22f;
    [SerializeField, Min(1)] private int maxActive = 180;

    [Header("Populate Once Settings")]
    [SerializeField, Min(1)] private int targetCount = 160;
    [SerializeField, Min(1)] private int maxAttemptsPerSlot = 6;

    [Header("Stream Settings (only if Spawn Mode = StreamAroundPlayer)")]
    [SerializeField] private bool streamSpawnDuringRun = true;
    [SerializeField] private float updateInterval = 0.35f;
    [SerializeField] private float maxSpawnDistanceAhead = 260f;
    [SerializeField] private float maxSpawnDistanceBehind = 180f;
    [SerializeField] private float despawnBehindDistance = 60f;

    [Header("Corridor Placement (NOT RoadWidth-based)")]
    [Tooltip("Minimum distance away from the road area before we even consider placement.")]
    [SerializeField, Min(0f)] private float minDistanceFromRoad = 4.0f;

    [Tooltip("Maximum distance from the path centerline for environment placement. Prevents '500000 meters away'.")]
    [SerializeField, Min(1f)] private float maxDistanceFromCenterline = 28f;

    [Tooltip("1 = mostly spawn on the sides (±90°), 0 = any direction around the point.")]
    [SerializeField, Range(0f, 1f)] private float sideBias = 0.9f;

    [Tooltip("How many random candidate points we try for a given slot before giving up.")]
    [SerializeField, Min(1)] private int placementAttempts = 8;

    [Header("Grounding")]
    [SerializeField] private bool autoGroundUsingBounds = true;
    [SerializeField] private float extraGroundPadding = 0.02f;

    [Header("Raycast / Masks")]
    [Tooltip("What the environment should sit on (your grass/shoulder colliders).")]
    [SerializeField] private LayerMask groundMask; // RoadSurface

    [Tooltip("The DRIVABLE road layer that environment must NEVER spawn onto.")]
    [SerializeField] private LayerMask roadExcludeMask; // Road

    [SerializeField] private float raycastStartHeight = 12f;
    [SerializeField] private float raycastDownDistance = 60f;
    [SerializeField] private float baseHeightOffset = 0.02f;

    [Header("Road Rejection")]
    [Tooltip("If a road collider is found within this radius at the spawn point, reject.")]
    [SerializeField] private float roadOverlapRejectRadius = 0.9f;

    [Header("Debug")]
    [SerializeField] private bool verboseDebug = false;

    // runtime path
    private readonly List<Vector3> _path = new();
    private float[] _cumLengths;
    private float _totalLength;
    private int _maxSlotIndex;

    // runtime spawned
    private readonly Dictionary<int, GameObject> _spawnedBySlot = new();

    // streaming internals
    private float _updateTimer;
    private int _lastClosestIdx = 0;
    private TrackDistanceMeter _distanceMeter;

    private void Update()
    {
        if (spawnMode != EnvSpawnMode.StreamAroundPlayer) return;
        if (!streamSpawnDuringRun) return;
        if (trackGenerator == null || playerTransform == null) return;
        if (_totalLength <= 0f) return;

        _updateTimer -= Time.deltaTime;
        if (_updateTimer > 0f) return;
        _updateTimer = updateInterval;

        float playerDist = GetPlayerDistanceAlongTrack();

        // Clean, predictable streaming window (no funky min/max mixing)
        float windowMin = Mathf.Clamp(playerDist - maxSpawnDistanceBehind, 0f, _totalLength);
        float windowMax = Mathf.Clamp(playerDist + maxSpawnDistanceAhead, 0f, _totalLength);

        int slotMin = Mathf.Clamp(Mathf.FloorToInt(windowMin / spacing), 0, _maxSlotIndex);
        int slotMax = Mathf.Clamp(Mathf.CeilToInt(windowMax / spacing), 0, _maxSlotIndex);

        for (int slot = slotMin; slot <= slotMax; slot++)
            TrySpawnAtSlot(slot);

        // Despawn far behind
        float despawnDist = playerDist - despawnBehindDistance;
        int despawnSlot = Mathf.FloorToInt(despawnDist / spacing);

        if (_spawnedBySlot.Count > 0)
        {
            var toRemove = new List<int>();
            foreach (var kv in _spawnedBySlot)
            {
                if (kv.Key < despawnSlot)
                    toRemove.Add(kv.Key);
            }

            for (int i = 0; i < toRemove.Count; i++)
            {
                int slot = toRemove[i];
                if (_spawnedBySlot.TryGetValue(slot, out var go) && go != null)
                    Destroy(go);
                _spawnedBySlot.Remove(slot);
            }
        }
    }

    public void InitializeForRun(ProceduralTrackGenerator generator, Transform player)
    {
        trackGenerator = generator;
        playerTransform = player;

        if (!trackGenerator)
        {
            Debug.LogError("[TrackEnvironmentSpawner] InitializeForRun missing trackGenerator.");
            return;
        }

        RebuildPath();
        ClearAll();
        SetupSlots();

        if (spawnMode == EnvSpawnMode.PopulateOnceAfterTrack)
        {
            PopulateOnceAcrossTrack();
        }
        // else: streaming will fill via Update()
    }

    // =========================
    // Populate Once (stable)
    // =========================
    private void PopulateOnceAcrossTrack()
    {
        if (_totalLength <= 0f || !HasAnyValidType()) return;

        // Make a stable spread across the full track length
        int totalSlots = Mathf.Max(1, _maxSlotIndex + 1);
        int want = Mathf.Clamp(targetCount, 1, Mathf.Max(1, maxActive));

        float stride = totalSlots / (float)want;
        float jitterFrac = 0.35f; // 0..0.5

        for (int i = 0; i < want; i++)
        {
            int baseSlot = Mathf.RoundToInt(i * stride);
            int jitter = Mathf.RoundToInt((Random.value - 0.5f) * 2f * jitterFrac * stride);
            int slot = Mathf.Clamp(baseSlot + jitter, 0, _maxSlotIndex);

            bool spawned = false;

            // Try a few times per target so rejections don't leave big gaps
            for (int a = 0; a < maxAttemptsPerSlot; a++)
            {
                TrySpawnAtSlot(slot);
                if (_spawnedBySlot.ContainsKey(slot))
                {
                    spawned = true;
                    break;
                }

                // If rejected, drift to a nearby slot and retry
                slot = Mathf.Clamp(slot + Random.Range(-2, 3), 0, _maxSlotIndex);
            }

            if (!spawned && verboseDebug)
                Debug.Log($"[EnvSpawn] PopulateOnce failed near slot={baseSlot} after {maxAttemptsPerSlot} attempts.");

            if (_spawnedBySlot.Count >= maxActive)
                break;
        }
    }

    // =========================
    // Core spawning
    // =========================
    private void TrySpawnAtSlot(int slot)
    {
        if (_spawnedBySlot.ContainsKey(slot)) return;
        if (_spawnedBySlot.Count >= maxActive) return;
        if (_totalLength <= 0f) return;

        float dist = slot * spacing;
        if (dist < 0f || dist > _totalLength) return;

        var prefab = ChoosePrefab();
        if (!prefab) return;

        // Sample center + forward
        SampleAlongPath(dist, out Vector3 center, out Vector3 forward);

        Vector3 flatForward = forward;
        flatForward.y = 0f;
        if (flatForward.sqrMagnitude < 0.0001f) flatForward = Vector3.forward;
        flatForward.Normalize();

        Vector3 right = Vector3.Cross(Vector3.up, flatForward).normalized;

        // Pick an off-road point in a corridor around the path (NOT using RoadWidth math)
        if (!TryPickOffRoadPoint(center, flatForward, right, out Vector3 xz))
            return;

        // 1) HARD REJECT: if road is at/near this position
        if (Physics.CheckSphere(xz + Vector3.up * 0.5f, roadOverlapRejectRadius, roadExcludeMask, QueryTriggerInteraction.Ignore))
            return;

        // 2) Place on groundMask (grass/shoulder)
        Vector3 origin = xz + Vector3.up * raycastStartHeight;
        float maxRay = raycastStartHeight + raycastDownDistance;

        if (!Physics.Raycast(origin, Vector3.down, out RaycastHit hit, maxRay, groundMask, QueryTriggerInteraction.Ignore))
            return;

        // 3) SECOND REJECT: if the ray also sees road right under it (belt + suspenders)
        if (Physics.Raycast(origin, Vector3.down, out RaycastHit roadHit, maxRay, roadExcludeMask, QueryTriggerInteraction.Ignore))
        {
            if (Mathf.Abs(roadHit.point.y - hit.point.y) < 0.25f && Vector3.Distance(roadHit.point, hit.point) < 1.0f)
                return;
        }

        // Rotation: align to track direction
        Quaternion rot = Quaternion.LookRotation(flatForward, Vector3.up);
        Transform parent = environmentParent != null ? environmentParent : transform;

        // Height offsets
        var type = FindTypeForPrefab(prefab);
        float h = baseHeightOffset + (type != null ? type.extraHeightOffset : 0f);

        GameObject go = Instantiate(prefab, hit.point + Vector3.up * h, rot, parent);

        if (overrideSpawnedLayer)
        {
            if (applyLayerRecursively) SetLayerRecursively(go.transform, spawnedEnvironmentLayer);
            else go.layer = spawnedEnvironmentLayer;
        }

        // Center-pivot trees etc.
        if (autoGroundUsingBounds)
            SnapInstanceToGround(go, hit.point.y, extraGroundPadding);

        _spawnedBySlot[slot] = go;

        if (verboseDebug)
            Debug.Log($"[EnvSpawn] slot={slot} dist={dist:F1} pos=({go.transform.position.x:F1},{go.transform.position.y:F1},{go.transform.position.z:F1}) prefab={prefab.name}");
    }

    private static void SetLayerRecursively(Transform root, int layer)
    {
        if (root == null) return;

        root.gameObject.layer = layer;

        for (int i = 0; i < root.childCount; i++)
            SetLayerRecursively(root.GetChild(i), layer);
    }

    private bool TryPickOffRoadPoint(Vector3 center, Vector3 fwd, Vector3 right, out Vector3 xz)
    {
        xz = center;

        for (int i = 0; i < placementAttempts; i++)
        {
            float angDeg;

            // Bias to sides so it feels like "environment lining the road"
            if (Random.value < sideBias)
            {
                float side = (Random.value < 0.5f) ? -1f : 1f;
                angDeg = side * 90f + Random.Range(-25f, 25f);
            }
            else
            {
                angDeg = Random.Range(-180f, 180f);
            }

            float r = Random.Range(minDistanceFromRoad, maxDistanceFromCenterline);

            // Use forward as base axis so angle behaves consistently relative to track
            Vector3 dir = (Quaternion.Euler(0f, angDeg, 0f) * fwd).normalized;

            // Optional per-type padding no longer shifts "road edge math"—
            // it simply increases how far out we sample.
            float extraPad = 0f;
            // Note: we don't know prefab type here; keep simple and stable.

            Vector3 candidate = center + dir * (r + extraPad);

            // Reject if road nearby at candidate
            if (Physics.CheckSphere(candidate + Vector3.up * 0.5f, roadOverlapRejectRadius, roadExcludeMask, QueryTriggerInteraction.Ignore))
                continue;

            xz = candidate;
            return true;
        }

        return false;
    }

    // =========================
    // Helpers: types / selection
    // =========================
    private EnvironmentType FindTypeForPrefab(GameObject prefab)
    {
        if (!prefab) return null;
        for (int i = 0; i < environmentTypes.Count; i++)
            if (environmentTypes[i] != null && environmentTypes[i].prefab == prefab)
                return environmentTypes[i];
        return null;
    }

    private bool HasAnyValidType()
    {
        for (int i = 0; i < environmentTypes.Count; i++)
            if (environmentTypes[i] != null && environmentTypes[i].prefab != null && environmentTypes[i].baseWeight > 0f)
                return true;
        return false;
    }

    private GameObject ChoosePrefab()
    {
        float total = 0f;
        for (int i = 0; i < environmentTypes.Count; i++)
        {
            var t = environmentTypes[i];
            if (t == null || t.prefab == null) continue;
            total += Mathf.Max(0f, t.baseWeight);
        }
        if (total <= 0f) return null;

        float r = Random.value * total;
        for (int i = 0; i < environmentTypes.Count; i++)
        {
            var t = environmentTypes[i];
            if (t == null || t.prefab == null) continue;

            float w = Mathf.Max(0f, t.baseWeight);
            if (w <= 0f) continue;

            r -= w;
            if (r <= 0f) return t.prefab;
        }

        return environmentTypes[environmentTypes.Count - 1].prefab;
    }

    // =========================
    // Path build / sampling
    // =========================
    private void RebuildPath()
    {
        _path.Clear();
        _cumLengths = null;
        _totalLength = 0f;

        var src = trackGenerator.PathPoints;
        if (src == null || src.Count < 2) return;

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

    private void SetupSlots()
    {
        _spawnedBySlot.Clear();
        _maxSlotIndex = (_totalLength <= 0f) ? 0 : Mathf.FloorToInt(_totalLength / spacing);
    }

    private void ClearAll()
    {
        foreach (var kv in _spawnedBySlot)
            if (kv.Value) Destroy(kv.Value);
        _spawnedBySlot.Clear();
    }

    private void SampleAlongPath(float dist, out Vector3 pos, out Vector3 forward)
    {
        dist = Mathf.Clamp(dist, 0f, _totalLength);

        int idx = FindSegmentIndex(dist);
        int i0 = Mathf.Clamp(idx, 0, _path.Count - 2);
        int i1 = i0 + 1;

        float d0 = _cumLengths[i0];
        float d1 = _cumLengths[i1];
        float t = (Mathf.Abs(d1 - d0) < 0.0001f) ? 0f : Mathf.InverseLerp(d0, d1, dist);

        pos = Vector3.Lerp(_path[i0], _path[i1], t);
        forward = (_path[i1] - _path[i0]).normalized;
        if (forward.sqrMagnitude < 0.0001f) forward = Vector3.forward;
    }

    private int FindSegmentIndex(float dist)
    {
        for (int i = 1; i < _cumLengths.Length; i++)
            if (_cumLengths[i] >= dist) return i - 1;
        return Mathf.Max(0, _cumLengths.Length - 2);
    }

    private static void GenerateSmoothedPath(List<Vector3> src, int subdiv, List<Vector3> dst)
    {
        dst.Clear();
        if (src == null || src.Count < 2) return;

        for (int i = 0; i < src.Count - 1; i++)
        {
            Vector3 a = src[i];
            Vector3 b = src[i + 1];

            dst.Add(a);
            for (int s = 1; s < subdiv; s++)
            {
                float t = s / (float)subdiv;
                dst.Add(Vector3.Lerp(a, b, t));
            }
        }
        dst.Add(src[src.Count - 1]);
    }

    // =========================
    // Ground snap for center pivots
    // =========================
    private static void SnapInstanceToGround(GameObject go, float groundY, float pad)
    {
        if (go == null) return;

        Bounds? bounds = null;

        var cols = go.GetComponentsInChildren<Collider>();
        for (int i = 0; i < cols.Length; i++)
        {
            if (cols[i] == null) continue;
            if (bounds == null) bounds = cols[i].bounds;
            else
            {
                Bounds b = bounds.Value;
                b.Encapsulate(cols[i].bounds);
                bounds = b;
            }
        }

        if (bounds == null)
        {
            var rends = go.GetComponentsInChildren<Renderer>();
            for (int i = 0; i < rends.Length; i++)
            {
                if (rends[i] == null) continue;
                if (bounds == null) bounds = rends[i].bounds;
                else
                {
                    Bounds b = bounds.Value;
                    b.Encapsulate(rends[i].bounds);
                    bounds = b;
                }
            }
        }

        if (bounds == null) return;

        float bottomY = bounds.Value.min.y;
        float deltaUp = (groundY - bottomY) + pad;

        if (deltaUp > 0f)
            go.transform.position += Vector3.up * deltaUp;
    }

    // =========================
    // Player distance (only used for streaming mode)
    // =========================
    private float GetPlayerDistanceAlongTrack()
    {
        if (_distanceMeter == null)
            _distanceMeter = FindObjectOfType<TrackDistanceMeter>();

        if (_distanceMeter != null)
            return Mathf.Clamp(_distanceMeter.DistanceAlongTrack, 0f, _totalLength);

        return Mathf.Clamp(EstimateDistanceByClosestPoint(playerTransform.position), 0f, _totalLength);
    }

    private float EstimateDistanceByClosestPoint(Vector3 p)
    {
        if (_path == null || _path.Count < 2) return 0f;

        int start = Mathf.Clamp(_lastClosestIdx - 12, 0, _path.Count - 2);
        int end = Mathf.Clamp(_lastClosestIdx + 12, 0, _path.Count - 2);

        float bestDistSqr = float.MaxValue;
        int bestIdx = start;
        float bestT = 0f;

        for (int i = start; i <= end; i++)
        {
            Vector3 a = _path[i];
            Vector3 b = _path[i + 1];
            Vector3 ab = b - a;

            float t = 0f;
            float abSqr = ab.sqrMagnitude;
            if (abSqr > 0.0001f)
                t = Mathf.Clamp01(Vector3.Dot(p - a, ab) / abSqr);

            Vector3 closest = a + ab * t;
            float dSqr = (p - closest).sqrMagnitude;

            if (dSqr < bestDistSqr)
            {
                bestDistSqr = dSqr;
                bestIdx = i;
                bestT = t;
            }
        }

        _lastClosestIdx = bestIdx;

        float d0 = _cumLengths[bestIdx];
        float segLen = Vector3.Distance(_path[bestIdx], _path[bestIdx + 1]);
        return d0 + segLen * bestT;
    }
}
