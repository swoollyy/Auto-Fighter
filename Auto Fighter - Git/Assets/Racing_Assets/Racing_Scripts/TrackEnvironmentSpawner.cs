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
    [Tooltip("If false, this type stays world-upright (useful for trees).")]
    public bool alignToGroundNormal = true;
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
    [Tooltip("Along-track sample spacing. Keep large enough that targetCount does not crush many props onto the same slots.")]
    [SerializeField, Min(0.5f)] private float spacing = 6f;
    [SerializeField, Min(1)] private int maxActive = 400;

    [Header("Populate Once Settings")]
    [SerializeField, Min(1)] private int targetCount = 280;
    [SerializeField, Min(1)] private int maxAttemptsPerSlot = 10;

    [Tooltip("Minimum XZ distance between any two environment props. Stops trees stacking inside each other.")]
    [SerializeField, Min(0.5f)] private float minSeparationMeters = 5.5f;

    [Tooltip("After the even pass, retry leftover empty slots to fill barren stretches.")]
    [SerializeField] private bool fillEmptyGaps = true;

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
    [SerializeField, Range(0f, 1f)] private float sideBias = 0.85f;

    [Tooltip("How many random candidate points we try for a given slot before giving up.")]
    [SerializeField, Min(1)] private int placementAttempts = 12;

    [Header("Grounding")]
    [SerializeField] private bool autoGroundUsingBounds = true;
    [SerializeField] private float extraGroundPadding = 0.02f;
    [Tooltip("Maximum downward snap distance along ground normal. Prevents extreme teleports on bad bounds.")]
    [SerializeField, Min(0f)] private float maxSnapDownDistance = 2.5f;

    [Header("Physics")]
    [Tooltip("Environment props stay fixed on hills; rigidbodies are forced kinematic with gravity off.")]
    [SerializeField] private bool forceKinematicRigidbodies = true;

    [Header("Raycast / Masks")]
    [Tooltip("What the environment should sit on (e.g. grass colliders). TerrainCollider is often on Default � use Ground Mask Raycast Extra or include those layers here.")]
    [SerializeField] private LayerMask groundMask;

    [Tooltip("OR'd with Ground Mask for the downward placement ray so TerrainCollider (and similar) is hit. Default = layer 0 only; set to Nothing if Ground Mask already includes terrain.")]
    [SerializeField] private LayerMask groundMaskRaycastExtra = 1;

    [Tooltip("The DRIVABLE road layer that environment must NEVER spawn onto.")]
    [SerializeField] private LayerMask roadExcludeMask; // Road

    [SerializeField] private float raycastStartHeight = 12f;
    [SerializeField] private float raycastDownDistance = 60f;

    [Tooltip("Spawn XZ keeps path Y (often 0). Rays must start above sculpted hills or the cast begins inside TerrainCollider and hits wrong. This clearance is added above Terrain.SampleHeight at XZ.")]
    [SerializeField, Min(2f)] private float rayOriginClearanceAboveTerrain = 22f;

    [SerializeField] private float baseHeightOffset = 0.02f;

    [Header("Road Rejection")]
    [Tooltip("If a road collider is found within this radius at the spawn point, reject.")]
    [SerializeField] private float roadOverlapRejectRadius = 0.9f;

    [Header("Debug")]
    [SerializeField] private bool verboseDebug = false;

    [Header("Track Terrain Planes")]
    [Tooltip("Only place environment on terrain tiles the track centerline overlaps. If resolve finds none, falls back to all active terrains.")]
    [SerializeField] private bool restrictToTrackTerrains = true;

    [Tooltip("XZ margin around the track when deciding which terrain planes count as 'under the track'.")]
    [SerializeField, Min(0f)] private float trackTerrainMarginMeters = 40f;

    private readonly RaycastHit[] _rayHits = new RaycastHit[48];
    private readonly List<Terrain> _trackTerrains = new List<Terrain>(16);

    /// <summary>Terrains resolved for the current run (track-overlapping planes).</summary>
    public int TrackTerrainCount => _trackTerrains.Count;

    // runtime path
    private readonly List<Vector3> _path = new();
    private float[] _cumLengths;
    private float _totalLength;
    private int _maxSlotIndex;

    // runtime spawned
    private readonly Dictionary<int, GameObject> _spawnedBySlot = new();

    // World-space occupancy (XZ) so lateral picks can't stack props on top of each other.
    private readonly List<Vector2> _occupiedXZ = new(512);
    private readonly Dictionary<long, List<int>> _occupiedGrid = new(512);
    private float _occCellSize = 5.5f;

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
        ResolveTrackTerrains();

        if (spawnMode == EnvSpawnMode.PopulateOnceAfterTrack)
        {
            PopulateOnceAcrossTrack();
        }
        // else: streaming will fill via Update()
    }

    private void ResolveTrackTerrains()
    {
        _trackTerrains.Clear();
        float margin = Mathf.Max(trackTerrainMarginMeters, maxDistanceFromCenterline + 5f);

        TrackTerrainOverlap.CollectFromTrack(trackGenerator, margin, _trackTerrains);

        if (_trackTerrains.Count == 0)
        {
            Terrain[] active = Terrain.activeTerrains;
            if (active != null)
            {
                for (int i = 0; i < active.Length; i++)
                {
                    if (active[i] != null && active[i].terrainData != null)
                        _trackTerrains.Add(active[i]);
                }
            }
        }

        if (verboseDebug)
            Debug.Log($"[EnvSpawn] Track terrains resolved: {_trackTerrains.Count}");
    }

    // =========================
    // Populate Once (stable)
    // =========================
    private void PopulateOnceAcrossTrack()
    {
        if (_totalLength <= 0f || !HasAnyValidType()) return;

        ClearOccupancy();

        int totalSlots = Mathf.Max(1, _maxSlotIndex + 1);
        // Never request more props than unique along-track slots — that used to map many
        // targets onto the same slot (via RoundToInt) and then "drift" into dense clumps.
        int want = Mathf.Clamp(targetCount, 1, Mathf.Min(maxActive, totalSlots));

        var usedSlots = new bool[totalSlots];
        var slotOrder = new List<int>(want);

        for (int i = 0; i < want; i++)
        {
            // Center each target in its share of the track so coverage is even end-to-end.
            int ideal = Mathf.Clamp(
                Mathf.RoundToInt((i + 0.5f) * totalSlots / (float)want),
                0,
                totalSlots - 1);

            // Tiny unique jitter so it doesn't look grid-locked, without colliding slots.
            int jitterSpan = Mathf.Max(0, Mathf.FloorToInt(totalSlots / (float)(want * 4f)));
            if (jitterSpan > 0)
                ideal = Mathf.Clamp(ideal + Random.Range(-jitterSpan, jitterSpan + 1), 0, totalSlots - 1);

            int slot = FindNearestFreeSlot(ideal, usedSlots);
            if (slot < 0)
                break;

            usedSlots[slot] = true;
            slotOrder.Add(slot);
        }

        for (int i = 0; i < slotOrder.Count; i++)
        {
            int slot = slotOrder[i];
            bool spawned = false;

            // Retry the SAME slot with new lateral candidates — do not hop to neighbor slots
            // (that was a major source of 2–3 trees stacked in one patch).
            for (int a = 0; a < maxAttemptsPerSlot; a++)
            {
                if (TrySpawnAtSlot(slot))
                {
                    spawned = true;
                    break;
                }
            }

            if (!spawned && verboseDebug)
                Debug.Log($"[EnvSpawn] PopulateOnce failed at slot={slot} after {maxAttemptsPerSlot} attempts.");

            if (_spawnedBySlot.Count >= maxActive)
                break;
        }

        // Second pass: fill barren stretches without breaking min-separation.
        if (fillEmptyGaps && _spawnedBySlot.Count < want)
        {
            for (int slot = 0; slot < totalSlots && _spawnedBySlot.Count < want && _spawnedBySlot.Count < maxActive; slot++)
            {
                if (_spawnedBySlot.ContainsKey(slot))
                    continue;

                for (int a = 0; a < maxAttemptsPerSlot; a++)
                {
                    if (TrySpawnAtSlot(slot))
                        break;
                }
            }
        }

        if (verboseDebug)
            Debug.Log($"[EnvSpawn] PopulateOnce done: spawned={_spawnedBySlot.Count}/{want} slots={totalSlots} spacing={spacing:F2} minSep={minSeparationMeters:F2}");
    }

    private static int FindNearestFreeSlot(int ideal, bool[] used)
    {
        if (ideal >= 0 && ideal < used.Length && !used[ideal])
            return ideal;

        int maxRadius = used.Length;
        for (int r = 1; r < maxRadius; r++)
        {
            int lo = ideal - r;
            if (lo >= 0 && !used[lo])
                return lo;

            int hi = ideal + r;
            if (hi < used.Length && !used[hi])
                return hi;
        }

        return -1;
    }

    // =========================
    // Core spawning
    // =========================
    private float GetMaxTerrainSurfaceYAtXZ(float wx, float wz)
    {
        float bestY = float.NegativeInfinity;
        IList<Terrain> terrains = GetTerrainsForPlacement();
        for (int i = 0; i < terrains.Count; i++)
        {
            Terrain t = terrains[i];
            if (t == null || t.terrainData == null) continue;
            Vector3 tp = t.transform.position;
            Vector3 sz = t.terrainData.size;
            if (wx < tp.x || wx > tp.x + sz.x || wz < tp.z || wz > tp.z + sz.z)
                continue;
            float y = t.SampleHeight(new Vector3(wx, 0f, wz)) + tp.y;
            if (y > bestY) bestY = y;
        }

        return bestY;
    }

    private bool TryTerrainSurfaceAtXZ(float wx, float wz, out Vector3 groundPoint, out Vector3 groundNormal)
    {
        float bestY = float.NegativeInfinity;
        Terrain bestTerrain = null;
        IList<Terrain> terrains = GetTerrainsForPlacement();
        for (int i = 0; i < terrains.Count; i++)
        {
            Terrain t = terrains[i];
            if (t == null || t.terrainData == null) continue;
            Vector3 tp = t.transform.position;
            Vector3 sz = t.terrainData.size;
            if (wx < tp.x || wx > tp.x + sz.x || wz < tp.z || wz > tp.z + sz.z)
                continue;
            float y = t.SampleHeight(new Vector3(wx, 0f, wz)) + tp.y;
            if (bestTerrain == null || y > bestY)
            {
                bestY = y;
                bestTerrain = t;
            }
        }

        if (bestTerrain != null)
        {
            groundPoint = new Vector3(wx, bestY, wz);
            Vector3 tp = bestTerrain.transform.position;
            Vector3 sz = bestTerrain.terrainData.size;
            float nx = Mathf.Clamp01((wx - tp.x) / Mathf.Max(1e-6f, sz.x));
            float nz = Mathf.Clamp01((wz - tp.z) / Mathf.Max(1e-6f, sz.z));
            Vector3 localN = bestTerrain.terrainData.GetInterpolatedNormal(nx, nz);
            groundNormal = bestTerrain.transform.TransformDirection(localN).normalized;
            if (groundNormal.sqrMagnitude < 1e-8f) groundNormal = Vector3.up;
            return true;
        }

        groundPoint = default;
        groundNormal = Vector3.up;
        return false;
    }

    private IList<Terrain> GetTerrainsForPlacement()
    {
        if (restrictToTrackTerrains && _trackTerrains.Count > 0)
            return _trackTerrains;

        return Terrain.activeTerrains;
    }

    private void ComputePlacementRayOrigin(Vector3 xzWorld, out Vector3 rayOrigin, out float maxRayDistance)
    {
        float wx = xzWorld.x;
        float wz = xzWorld.z;
        float surfaceY = GetMaxTerrainSurfaceYAtXZ(wx, wz);
        if (float.IsNegativeInfinity(surfaceY))
            surfaceY = xzWorld.y;

        float originY = Mathf.Max(
            surfaceY + rayOriginClearanceAboveTerrain,
            xzWorld.y + raycastStartHeight);

        rayOrigin = new Vector3(wx, originY, wz);
        maxRayDistance = Mathf.Max(
            raycastStartHeight + raycastDownDistance + 200f,
            originY - surfaceY + raycastDownDistance + 50f);
    }

    /// <summary>
    /// Ray from well above the heightmap at XZ (avoids starting inside hills when path Y is low).
    /// Uses topmost hit when multiple colliders overlap. Falls back to Terrain.SampleHeight.
    /// </summary>
    private bool TryResolveGroundBelow(Vector3 xzWorld, Vector3 rayOrigin, float maxRayDist, out Vector3 groundPoint, out Vector3 groundNormal)
    {
        LayerMask castMask = groundMask | groundMaskRaycastExtra;

        int n = Physics.RaycastNonAlloc(rayOrigin, Vector3.down, _rayHits, maxRayDist, castMask, QueryTriggerInteraction.Ignore);
        if (n > 0)
        {
            int best = 0;
            for (int i = 1; i < n; i++)
            {
                if (_rayHits[i].point.y > _rayHits[best].point.y)
                    best = i;
            }

            RaycastHit hit = _rayHits[best];
            groundPoint = hit.point;
            groundNormal = hit.normal.sqrMagnitude > 1e-8f ? hit.normal.normalized : Vector3.up;

            if (TryTerrainSurfaceAtXZ(xzWorld.x, xzWorld.z, out Vector3 tPt, out Vector3 tN) &&
                tPt.y > groundPoint.y + 0.05f)
            {
                groundPoint = tPt;
                groundNormal = tN.sqrMagnitude > 1e-8f ? tN.normalized : Vector3.up;
            }

            return true;
        }

        if (TryTerrainSurfaceAtXZ(xzWorld.x, xzWorld.z, out groundPoint, out groundNormal))
            return true;

        groundPoint = default;
        groundNormal = Vector3.up;
        return false;
    }

    private bool TrySpawnAtSlot(int slot)
    {
        if (_spawnedBySlot.ContainsKey(slot)) return false;
        if (_spawnedBySlot.Count >= maxActive) return false;
        if (_totalLength <= 0f) return false;

        float dist = slot * spacing;
        if (dist < 0f || dist > _totalLength) return false;

        var prefab = ChoosePrefab();
        if (!prefab) return false;

        // Sample center + forward
        SampleAlongPath(dist, out Vector3 center, out Vector3 forward);

        Vector3 flatForward = forward;
        flatForward.y = 0f;
        if (flatForward.sqrMagnitude < 0.0001f) flatForward = Vector3.forward;
        flatForward.Normalize();

        // Prefer alternating sides so left/right corridors fill evenly.
        float preferredSide = (slot & 1) == 0 ? -1f : 1f;

        // Pick an off-road point in a corridor around the path (NOT using RoadWidth math)
        if (!TryPickOffRoadPoint(center, flatForward, preferredSide, out Vector3 xz))
            return false;

        // 1) HARD REJECT: if road is at/near this position
        if (Physics.CheckSphere(xz + Vector3.up * 0.5f, roadOverlapRejectRadius, roadExcludeMask, QueryTriggerInteraction.Ignore))
            return false;

        // 2) Place on ground (ray starts above local terrain height — path Y is often 0 while hills are tall)
        ComputePlacementRayOrigin(xz, out Vector3 rayOrigin, out float maxRay);

        if (!TryResolveGroundBelow(xz, rayOrigin, maxRay, out Vector3 groundPoint, out Vector3 groundNormal))
            return false;

        if (restrictToTrackTerrains && _trackTerrains.Count > 0 &&
            !TrackTerrainOverlap.IsOnAny(_trackTerrains, groundPoint.x, groundPoint.z))
            return false;

        // Separation uses final ground XZ (ray may nudge slightly, but XZ is what matters for stacking).
        if (IsTooCloseToOccupied(groundPoint.x, groundPoint.z))
            return false;

        // 3) SECOND REJECT: if the ray also sees road right under it (belt + suspenders)
        if (Physics.Raycast(rayOrigin, Vector3.down, out RaycastHit roadHit, maxRay, roadExcludeMask, QueryTriggerInteraction.Ignore))
        {
            if (Mathf.Abs(roadHit.point.y - groundPoint.y) < 0.25f && Vector3.Distance(roadHit.point, groundPoint) < 1.0f)
                return false;
        }

        Transform parent = environmentParent != null ? environmentParent : transform;

        var type = FindTypeForPrefab(prefab);
        bool alignToGround = type == null || type.alignToGroundNormal;
        Vector3 up = groundNormal.sqrMagnitude > 1e-8f ? groundNormal.normalized : Vector3.up;
        Vector3 placementUp = alignToGround ? up : Vector3.up;
        Quaternion rot = AlignRotationToGround(flatForward, placementUp);
        float h = baseHeightOffset + (type != null ? type.extraHeightOffset : 0f);

        Vector3 spawnPos = groundPoint + placementUp * h;
        GameObject go = Instantiate(prefab, spawnPos, rot, parent);

        if (overrideSpawnedLayer)
        {
            if (applyLayerRecursively) SetLayerRecursively(go.transform, spawnedEnvironmentLayer);
            else go.layer = spawnedEnvironmentLayer;
        }

        if (autoGroundUsingBounds)
            SnapInstanceToGroundOnPlane(go, groundPoint, placementUp, extraGroundPadding, maxSnapDownDistance);

        ConfigureEnvironmentRigidbodies(go);

        _spawnedBySlot[slot] = go;
        RegisterOccupied(go.transform.position.x, go.transform.position.z);

        if (verboseDebug)
            Debug.Log($"[EnvSpawn] slot={slot} dist={dist:F1} pos=({go.transform.position.x:F1},{go.transform.position.y:F1},{go.transform.position.z:F1}) prefab={prefab.name}");

        return true;
    }

    private static void SetLayerRecursively(Transform root, int layer)
    {
        if (root == null) return;

        root.gameObject.layer = layer;

        for (int i = 0; i < root.childCount; i++)
            SetLayerRecursively(root.GetChild(i), layer);
    }

    private bool TryPickOffRoadPoint(Vector3 center, Vector3 fwd, float preferredSide, out Vector3 xz)
    {
        xz = center;

        float rMin = Mathf.Max(0f, minDistanceFromRoad);
        float rMax = Mathf.Max(rMin + 0.5f, maxDistanceFromCenterline);
        float rMinSq = rMin * rMin;
        float rMaxSq = rMax * rMax;

        for (int i = 0; i < placementAttempts; i++)
        {
            float angDeg;

            // Bias to sides so it feels like "environment lining the road"
            if (Random.value < sideBias)
            {
                // Alternate preferred side, with occasional flips so one bank isn't empty.
                float side = preferredSide;
                if (Random.value < 0.18f)
                    side = -side;
                angDeg = side * 90f + Random.Range(-32f, 32f);
            }
            else
            {
                angDeg = Random.Range(-180f, 180f);
            }

            // Area-uniform sample in the annulus (avoids over-clustering near the road edge).
            float u = Random.value;
            float r = Mathf.Sqrt(Mathf.Lerp(rMinSq, rMaxSq, u));

            Vector3 dir = (Quaternion.Euler(0f, angDeg, 0f) * fwd).normalized;
            Vector3 candidate = center + dir * r;

            // Reject if road nearby at candidate
            if (Physics.CheckSphere(candidate + Vector3.up * 0.5f, roadOverlapRejectRadius, roadExcludeMask, QueryTriggerInteraction.Ignore))
                continue;

            if (IsTooCloseToOccupied(candidate.x, candidate.z))
                continue;

            if (restrictToTrackTerrains && _trackTerrains.Count > 0 &&
                !TrackTerrainOverlap.IsOnAny(_trackTerrains, candidate.x, candidate.z))
                continue;

            xz = candidate;
            return true;
        }

        return false;
    }

    // =========================
    // Occupancy (anti-stacking)
    // =========================
    private void ClearOccupancy()
    {
        _occupiedXZ.Clear();
        _occupiedGrid.Clear();
        _occCellSize = Mathf.Max(0.5f, minSeparationMeters);
    }

    private void RegisterOccupied(float worldX, float worldZ)
    {
        int idx = _occupiedXZ.Count;
        _occupiedXZ.Add(new Vector2(worldX, worldZ));

        long key = OccupancyCellKey(worldX, worldZ);
        if (!_occupiedGrid.TryGetValue(key, out List<int> list))
        {
            list = new List<int>(4);
            _occupiedGrid[key] = list;
        }
        list.Add(idx);
    }

    private bool IsTooCloseToOccupied(float worldX, float worldZ)
    {
        if (_occupiedXZ.Count == 0)
            return false;

        float minSep = Mathf.Max(0.5f, minSeparationMeters);
        float minSq = minSep * minSep;
        float cell = Mathf.Max(0.5f, _occCellSize);

        int gx = Mathf.FloorToInt(worldX / cell);
        int gz = Mathf.FloorToInt(worldZ / cell);
        Vector2 p = new Vector2(worldX, worldZ);

        for (int dz = -1; dz <= 1; dz++)
        {
            for (int dx = -1; dx <= 1; dx++)
            {
                long key = PackOccupancyKey(gx + dx, gz + dz);
                if (!_occupiedGrid.TryGetValue(key, out List<int> list))
                    continue;

                for (int i = 0; i < list.Count; i++)
                {
                    if ((p - _occupiedXZ[list[i]]).sqrMagnitude < minSq)
                        return true;
                }
            }
        }

        return false;
    }

    private long OccupancyCellKey(float worldX, float worldZ)
    {
        float cell = Mathf.Max(0.5f, _occCellSize);
        return PackOccupancyKey(Mathf.FloorToInt(worldX / cell), Mathf.FloorToInt(worldZ / cell));
    }

    private static long PackOccupancyKey(int gx, int gz)
    {
        return ((long)gx << 32) ^ (uint)gz;
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
        ClearOccupancy();
        _maxSlotIndex = (_totalLength <= 0f) ? 0 : Mathf.FloorToInt(_totalLength / spacing);
    }

    private void ClearAll()
    {
        foreach (var kv in _spawnedBySlot)
            if (kv.Value) Destroy(kv.Value);
        _spawnedBySlot.Clear();
        ClearOccupancy();
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
    // Ground snap + alignment (slopes)
    // =========================
    private static Quaternion AlignRotationToGround(Vector3 worldForwardFlat, Vector3 groundNormal)
    {
        Vector3 n = groundNormal.sqrMagnitude > 1e-8f ? groundNormal.normalized : Vector3.up;
        Vector3 f = Vector3.ProjectOnPlane(worldForwardFlat, n);
        if (f.sqrMagnitude < 1e-6f)
            f = Vector3.ProjectOnPlane(Vector3.forward, n);
        if (f.sqrMagnitude < 1e-6f)
            f = Vector3.Cross(n, Vector3.right);
        f.Normalize();
        return Quaternion.LookRotation(f, n);
    }

    /// <summary>
    /// Slide along surface normal so the collider support point sits at <paramref name="pad"/> above the hit.
    /// Uses Collider.ClosestPoint from below for robust grounding even when prefab pivots are centered.
    /// </summary>
    private static void SnapInstanceToGroundOnPlane(GameObject go, Vector3 surfacePoint, Vector3 surfaceNormal, float pad, float maxDownDistance)
    {
        if (go == null) return;

        Vector3 n = surfaceNormal.sqrMagnitude > 1e-8f ? surfaceNormal.normalized : Vector3.up;
        float minSigned = float.MaxValue;
        bool foundSupport = false;

        var cols = go.GetComponentsInChildren<Collider>();
        for (int i = 0; i < cols.Length; i++)
        {
            Collider col = cols[i];
            if (col == null || !col.enabled) continue;

            // Query from well below along -normal to get a stable "bottom support" point.
            float belowDist = Mathf.Max(4f, col.bounds.extents.magnitude * 3f);
            Vector3 queryPoint = surfacePoint - n * belowDist;
            Vector3 support = col.ClosestPoint(queryPoint);
            float s = Vector3.Dot(support - surfacePoint, n);
            if (s < minSigned) minSigned = s;
            foundSupport = true;
        }

        // Fallback for props without colliders.
        if (!foundSupport)
        {
            Bounds? bounds = null;
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

            if (bounds == null) return;
            minSigned = Vector3.Dot(bounds.Value.min - surfacePoint, n);
        }

        float correction = pad - minSigned;
        if (Mathf.Abs(correction) <= 0.0001f) return;

        // Downward movement can be exaggerated by broad world AABB on rotated/complex props,
        // so clamp only the downward correction to a safe distance.
        if (correction < 0f)
            correction = Mathf.Max(correction, -Mathf.Abs(maxDownDistance));

        go.transform.position += n * correction;
    }

    private void ConfigureEnvironmentRigidbodies(GameObject go)
    {
        if (!forceKinematicRigidbodies || go == null) return;

        Rigidbody[] rbs = go.GetComponentsInChildren<Rigidbody>(true);
        for (int i = 0; i < rbs.Length; i++)
        {
            Rigidbody rb = rbs[i];
            rb.isKinematic = true;
            rb.useGravity = false;
            rb.velocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
        }
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
