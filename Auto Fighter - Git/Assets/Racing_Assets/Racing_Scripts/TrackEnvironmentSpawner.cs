using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

[System.Serializable]
public class EnvironmentType
{
    public string id;
    public GameObject prefab;
    [Min(0f)] public float baseWeight = 1f;

    [Header("Placement Tweaks")]
    [Tooltip("Moves this type's parent this many meters up from the ground hit. Negative sinks it and locks that parent so it cannot fall or be shoved through the mesh.")]
    [FormerlySerializedAs("extraHeightOffset")]
    public float extraGroundPadding = 0f;
    public float extraLateralPadding = 0f;
    [Tooltip("If false, this type stays world-upright (useful for trees).")]
    public bool alignToGroundNormal = true;

    [Tooltip("Random spin around up (degrees). 360 = any facing.")]
    [Range(0f, 360f)] public float randomYawDegrees = 360f;

    [Tooltip("Random extra tilt off the placement up axis (degrees). Keep 0 for trees.")]
    [Range(0f, 45f)] public float randomTiltDegrees = 0f;
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
    [SerializeField] private EnvSpawnMode spawnMode = EnvSpawnMode.StreamAroundPlayer;

    [Header("Path Sampling")]
    [SerializeField] private bool useSmoothing = true;
    [SerializeField, Min(1)] private int smoothingSubdivisionsPerSegment = 6;

    [Header("Spawn Settings")]
    [Tooltip("Along-track sample spacing. Each sample can hold several roadside props (see Laterals Per Side).")]
    [SerializeField, Min(0.5f)] private float spacing = 7f;
    [SerializeField, Min(1)] private int maxActive = 480;

    [Header("Roadside Density")]
    [Tooltip("How many props on EACH side of the road at every along-track sample (inner / mid / outer rings).")]
    [SerializeField, Range(1, 8)] private int lateralsPerSide = 3;

    [Tooltip("Minimum XZ distance between any two environment props.")]
    [SerializeField, Min(0.5f)] private float minSeparationMeters = 3.2f;

    [Tooltip("Measure the inner ring from the road edge (RoadWidth/2) plus Min Distance From Road.")]
    [SerializeField] private bool offsetFromRoadEdge = true;

    [Header("Populate Once Settings")]
    [SerializeField, Min(1)] private int targetCount = 480;
    [SerializeField, Min(1)] private int maxAttemptsPerSlot = 4;

    [Tooltip("After the even pass, retry leftover empty slots to fill barren stretches.")]
    [SerializeField] private bool fillEmptyGaps = false;

    [Header("Stream Settings (only if Spawn Mode = StreamAroundPlayer)")]
    [SerializeField] private bool streamSpawnDuringRun = true;
    [SerializeField] private float updateInterval = 0.15f;
    [Tooltip("How far ahead of the player (along the track) environment props are spawned. Raise this to stop trees/rocks popping in.")]
    [SerializeField] private float maxSpawnDistanceAhead = 400f;
    [Tooltip("How far behind the player we still fill empty roadside slots. Does not despawn anything.")]
    [SerializeField] private float maxSpawnDistanceBehind = 90f;
    [Tooltip("Only recycle props this far behind the player. Never despawn anything still ahead or in view.")]
    [SerializeField] private float despawnBehindDistance = 50f;
    [Tooltip("Max new instances created in one Update tick. Keeps streaming off the hitch list.")]
    [SerializeField, Min(1)] private int maxSpawnsPerUpdate = 20;
    [Tooltip("How far ahead of the start to pre-fill during loading.")]
    [SerializeField, Min(20f)] private float preloadAheadMeters = 400f;
    [SerializeField, Min(1)] private int preloadSpawnsPerFrame = 28;
                         
    [Header("Corridor Placement")]
    [Tooltip("Extra meters beyond the road edge (or from the centerline if Offset From Road Edge is off).")]
    [SerializeField, Min(0f)] private float minDistanceFromRoad = 2.5f;

    [Tooltip("Maximum distance from the path centerline for environment placement.")]
    [SerializeField, Min(1f)] private float maxDistanceFromCenterline = 40f;

    [Tooltip("1 = mostly spawn on the sides (±90°), 0 = any direction around the point.")]
    [SerializeField, Range(0f, 1f)] private float sideBias = 0.92f;

    [Tooltip("How many fallback jitter tries if the deterministic ring is blocked.")]
    [SerializeField, Min(1)] private int placementAttempts = 4;

    [Header("Grounding")]
    [SerializeField] private bool autoGroundUsingBounds = true;
    [SerializeField, Min(0f)] private float maxSnapDownDistance = 80f;

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

    // runtime spawned — key packs (slot, lateral index), not one prop per slot
    private readonly Dictionary<int, GameObject> _spawnedByKey = new();
    private readonly Dictionary<int, GameObject> _prefabByKey = new();
    private readonly HashSet<int> _failedKeys = new();
    private readonly Dictionary<GameObject, Stack<GameObject>> _pool = new();
    private Transform _poolRoot;
    private int _runSeed;

    // World-space occupancy (XZ) so lateral picks can't stack props on top of each other.
    private readonly List<Vector2> _occupiedXZ = new(512);
    private readonly Dictionary<long, List<int>> _occupiedGrid = new(512);
    private readonly Dictionary<int, int> _occupiedIndexByKey = new();
    private float _occCellSize = 5.5f;

    // streaming internals
    private float _updateTimer;
    private int _lastClosestIdx = 0;
    private TrackDistanceMeter _distanceMeter;
    private int _streamCursorSlot;
    private readonly List<int> _recycleScratch = new(128);

    private int LateralsPerSlot => Mathf.Max(2, lateralsPerSide * 2);

    private void Update()
    {
        if (spawnMode != EnvSpawnMode.StreamAroundPlayer) return;
        if (!streamSpawnDuringRun) return;
        if (trackGenerator == null || playerTransform == null) return;
        if (_totalLength <= 0f) return;

        _updateTimer -= Time.deltaTime;
        if (_updateTimer > 0f) return;
        _updateTimer = updateInterval;

        StreamWindow(GetPlayerDistanceAlongTrack(), maxSpawnsPerUpdate);
    }

    public void InitializeForRun(ProceduralTrackGenerator generator, Transform player)
    {
        if (!BeginInitialize(generator, player))
            return;

        if (spawnMode == EnvSpawnMode.PopulateOnceAfterTrack)
            PopulateOnceAcrossTrack();
        else
            StreamWindow(0f, int.MaxValue, preloadAheadMeters);
    }

    public IEnumerator CoInitializeForRun(ProceduralTrackGenerator generator, Transform player)
    {
        if (!BeginInitialize(generator, player))
            yield break;

        if (spawnMode == EnvSpawnMode.PopulateOnceAfterTrack)
        {
            PopulateOnceAcrossTrack();
            yield break;
        }

        float preload = Mathf.Min(_totalLength, Mathf.Max(40f, preloadAheadMeters));
        int budget = Mathf.Max(1, preloadSpawnsPerFrame);
        while (StreamWindow(0f, budget, preload) > 0)
            yield return null;
    }

    private bool BeginInitialize(ProceduralTrackGenerator generator, Transform player)
    {
        trackGenerator = generator;
        playerTransform = player;

        if (!trackGenerator)
        {
            Debug.LogError("[TrackEnvironmentSpawner] InitializeForRun missing trackGenerator.");
            return false;
        }

        _runSeed = unchecked(trackGenerator.GetInstanceID() * 1103515245 + 12345);
        EnsurePoolRoot();
        RebuildPath();
        ClearAll();
        SetupSlots();
        ResolveTrackTerrains();
        _streamCursorSlot = 0;
        return true;
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

        int totalSlots = Mathf.Max(1, _maxSlotIndex + 1);
        int capacity = Mathf.Min(EffectiveMaxActive(), totalSlots * LateralsPerSlot);
        int want = Mathf.Clamp(targetCount, 1, capacity);

        for (int slot = 0; slot < totalSlots && _spawnedByKey.Count < want; slot++)
        {
            TryFillSlot(slot);
            if (_spawnedByKey.Count >= EffectiveMaxActive())
                break;
        }

        if (fillEmptyGaps && _spawnedByKey.Count < want)
        {
            for (int slot = 0; slot < totalSlots && _spawnedByKey.Count < want; slot++)
                TryFillSlot(slot);
        }

        if (verboseDebug)
            Debug.Log($"[EnvSpawn] PopulateOnce done: spawned={_spawnedByKey.Count}/{want} slots={totalSlots} laterals={LateralsPerSlot}");
    }

    /// <summary>
    /// Fills / recycles the roadside band around <paramref name="centerDist"/>.
    /// Returns how many new instances were created this call.
    /// </summary>
    private int StreamWindow(float centerDist, int spawnBudget, float aheadOverride = -1f)
    {
        if (_totalLength <= 0f || !HasAnyValidType())
            return 0;

        float ahead = aheadOverride >= 0f ? aheadOverride : maxSpawnDistanceAhead;
        float windowMin = Mathf.Clamp(centerDist - maxSpawnDistanceBehind, 0f, _totalLength);
        float windowMax = Mathf.Clamp(centerDist + ahead, 0f, _totalLength);

        int slotMin = Mathf.Clamp(Mathf.FloorToInt(windowMin / spacing), 0, _maxSlotIndex);
        int slotMax = Mathf.Clamp(Mathf.CeilToInt(windowMax / spacing), 0, _maxSlotIndex);

        RecyclePassedBehind(centerDist);

        int cap = EffectiveMaxActive();
        int spawned = 0;
        int laterals = LateralsPerSlot;
        int start = Mathf.Clamp(_streamCursorSlot, slotMin, slotMax);

        for (int pass = 0; pass < 2 && spawned < spawnBudget; pass++)
        {
            int from = pass == 0 ? start : slotMin;
            int to = pass == 0 ? slotMax : start - 1;
            for (int slot = from; slot <= to && spawned < spawnBudget; slot++)
            {
                for (int sub = 0; sub < laterals && spawned < spawnBudget; sub++)
                {
                    if (_spawnedByKey.Count >= cap)
                        break;
                    if (TrySpawnAtSlot(slot, sub))
                        spawned++;
                }
                _streamCursorSlot = slot + 1;
            }
        }

        if (_streamCursorSlot > slotMax)
            _streamCursorSlot = slotMin;

        return spawned;
    }

    private int EffectiveMaxActive()
    {
        float keepMeters = maxSpawnDistanceAhead + Mathf.Max(despawnBehindDistance, 40f);
        int windowSlots = Mathf.CeilToInt(keepMeters / Mathf.Max(0.5f, spacing)) + 2;
        return Mathf.Max(maxActive, windowSlots * LateralsPerSlot);
    }

    private void RecyclePassedBehind(float playerDist)
    {
        if (_spawnedByKey.Count == 0)
            return;

        float despawnBefore = playerDist - Mathf.Max(0f, despawnBehindDistance);
        Camera cam = Camera.main;
        Plane[] frustum = null;
        if (cam != null)
        {
            float camDist = EstimateDistanceByClosestPoint(cam.transform.position);
            despawnBefore = Mathf.Min(playerDist, camDist) - Mathf.Max(0f, despawnBehindDistance);
            frustum = GeometryUtility.CalculateFrustumPlanes(cam);
        }

        _recycleScratch.Clear();
        foreach (var kv in _spawnedByKey)
        {
            UnpackKey(kv.Key, out int slot, out _);
            float dist = slot * spacing;
            if (dist >= playerDist)
                continue;
            if (dist >= despawnBefore)
                continue;
            if (frustum != null && SpawnUtils.IsInCameraFrustum(kv.Value, frustum))
                continue;

            _recycleScratch.Add(kv.Key);
        }

        for (int i = 0; i < _recycleScratch.Count; i++)
            RecycleKey(_recycleScratch[i]);
    }

    private void TryFillSlot(int slot)
    {
        int laterals = LateralsPerSlot;
        for (int sub = 0; sub < laterals; sub++)
        {
            if (_spawnedByKey.Count >= EffectiveMaxActive())
                return;
            TrySpawnAtSlot(slot, sub);
        }
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

        if (SpawnUtils.TryRaycastDownFromHigh(xzWorld, castMask, rayOriginClearanceAboveTerrain, raycastDownDistance, out RaycastHit highHit))
        {
            groundPoint = highHit.point;
            groundNormal = highHit.normal.sqrMagnitude > 1e-8f ? highHit.normal.normalized : Vector3.up;
            if (TryTerrainSurfaceAtXZ(xzWorld.x, xzWorld.z, out Vector3 tPt, out Vector3 tN) &&
                tPt.y > groundPoint.y + 0.05f)
            {
                groundPoint = tPt;
                groundNormal = tN.sqrMagnitude > 1e-8f ? tN.normalized : Vector3.up;
            }
            return true;
        }

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
        bool any = false;
        int laterals = LateralsPerSlot;
        for (int sub = 0; sub < laterals; sub++)
            any |= TrySpawnAtSlot(slot, sub);
        return any;
    }

    private bool TrySpawnAtSlot(int slot, int sub)
    {
        int key = PackKey(slot, sub);
        if (_spawnedByKey.ContainsKey(key) || _failedKeys.Contains(key)) return false;
        if (_spawnedByKey.Count >= EffectiveMaxActive()) return false;
        if (_totalLength <= 0f) return false;

        float dist = slot * spacing;
        if (dist < 0f || dist > _totalLength) return false;

        GameObject prefab = ChoosePrefab(slot, sub);
        if (!prefab) return false;

        SampleAlongPath(dist, out Vector3 center, out Vector3 forward);

        Vector3 flatForward = forward;
        flatForward.y = 0f;
        if (flatForward.sqrMagnitude < 0.0001f) flatForward = Vector3.forward;
        flatForward.Normalize();

        if (!TryPickRoadsidePoint(center, flatForward, slot, sub, out Vector3 xz))
        {
            _failedKeys.Add(key);
            return false;
        }

        if (Physics.CheckSphere(xz + Vector3.up * 0.5f, roadOverlapRejectRadius, roadExcludeMask, QueryTriggerInteraction.Ignore))
        {
            _failedKeys.Add(key);
            return false;
        }

        ComputePlacementRayOrigin(xz, out Vector3 rayOrigin, out float maxRay);

        if (!TryResolveGroundBelow(xz, rayOrigin, maxRay, out Vector3 groundPoint, out Vector3 groundNormal))
        {
            _failedKeys.Add(key);
            return false;
        }

        if (restrictToTrackTerrains && _trackTerrains.Count > 0 &&
            !TrackTerrainOverlap.IsOnAny(_trackTerrains, groundPoint.x, groundPoint.z))
        {
            _failedKeys.Add(key);
            return false;
        }

        if (IsTooCloseToOccupied(groundPoint.x, groundPoint.z))
        {
            _failedKeys.Add(key);
            return false;
        }

        if (Physics.Raycast(rayOrigin, Vector3.down, out RaycastHit roadHit, maxRay, roadExcludeMask, QueryTriggerInteraction.Ignore))
        {
            if (Mathf.Abs(roadHit.point.y - groundPoint.y) < 0.25f && Vector3.Distance(roadHit.point, groundPoint) < 1.0f)
            {
                _failedKeys.Add(key);
                return false;
            }
        }

        Transform parent = environmentParent != null ? environmentParent : transform;

        var type = FindTypeForPrefab(prefab);
        bool alignToGround = type == null || type.alignToGroundNormal;
        Vector3 up = groundNormal.sqrMagnitude > 1e-8f ? groundNormal.normalized : Vector3.up;
        Vector3 placementUp = alignToGround ? up : Vector3.up;

        Vector3 spawnPos = groundPoint;
        GameObject go = TakeFromPool(prefab, spawnPos, Quaternion.identity, parent);

        if (overrideSpawnedLayer)
        {
            if (applyLayerRecursively) SetLayerRecursively(go.transform, spawnedEnvironmentLayer);
            else go.layer = spawnedEnvironmentLayer;
        }

        SpawnUtils.RandomizeWorldYaw(go);
        if (alignToGround && (placementUp - Vector3.up).sqrMagnitude > 1e-6f)
            go.transform.rotation = Quaternion.FromToRotation(Vector3.up, placementUp) * go.transform.rotation;

        float typePadding = type != null ? type.extraGroundPadding : 0f;
        go.transform.position = groundPoint + Vector3.up * typePadding;
        if (typePadding < 0f)
            SpawnUtils.LockEmbeddedInTerrain(go);
        else
            ConfigureEnvironmentRigidbodies(go);

        _spawnedByKey[key] = go;
        _prefabByKey[key] = prefab;
        RegisterOccupied(key, go.transform.position.x, go.transform.position.z);

        if (verboseDebug)
            Debug.Log($"[EnvSpawn] slot={slot} sub={sub} dist={dist:F1} prefab={prefab.name}");

        return true;
    }

    private static void SetLayerRecursively(Transform root, int layer)
    {
        if (root == null) return;

        root.gameObject.layer = layer;

        for (int i = 0; i < root.childCount; i++)
            SetLayerRecursively(root.GetChild(i), layer);
    }

    private bool TryPickRoadsidePoint(Vector3 center, Vector3 fwd, int slot, int sub, out Vector3 xz)
    {
        xz = center;

        float rMin = GetInnerRadius();
        float rMax = Mathf.Max(rMin + 1.5f, maxDistanceFromCenterline);
        int rings = Mathf.Max(1, lateralsPerSide);
        int ring = Mathf.Clamp(sub / 2, 0, rings - 1);
        float side = (sub & 1) == 0 ? -1f : 1f;

        for (int i = 0; i < placementAttempts; i++)
        {
            float ringT = (ring + 0.35f + Hash01(slot, sub, i) * 0.45f) / rings;
            float r = Mathf.Lerp(rMin, rMax, ringT);

            float angDeg;
            if (Hash01(slot, sub, i + 3) < sideBias)
                angDeg = side * 90f + (Hash01(slot, sub, i + 7) - 0.5f) * 28f;
            else
                angDeg = (Hash01(slot, sub, i + 11) - 0.5f) * 360f;

            Vector3 dir = (Quaternion.Euler(0f, angDeg, 0f) * fwd).normalized;
            Vector3 candidate = center + dir * r;

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

    private float GetInnerRadius()
    {
        float fromRoad = Mathf.Max(0f, minDistanceFromRoad);
        if (!offsetFromRoadEdge || trackGenerator == null)
            return fromRoad;

        return trackGenerator.RoadWidth * 0.5f + fromRoad;
    }

    private static int PackKey(int slot, int sub) => (slot << 4) | (sub & 15);

    private static void UnpackKey(int key, out int slot, out int sub)
    {
        slot = key >> 4;
        sub = key & 15;
    }

    private static float Hash01(int a, int b, int c)
    {
        unchecked
        {
            int h = a * 73856093 ^ b * 19349663 ^ c * 83492791;
            h ^= h << 13;
            h ^= h >> 17;
            h ^= h << 5;
            return ((h & 0x7fffffff) / 2147483647f);
        }
    }

    // =========================
    // Occupancy (anti-stacking)
    // =========================
    private void ClearOccupancy()
    {
        _occupiedXZ.Clear();
        _occupiedGrid.Clear();
        _occupiedIndexByKey.Clear();
        _occCellSize = Mathf.Max(0.5f, minSeparationMeters);
    }

    private void RegisterOccupied(int spawnKey, float worldX, float worldZ)
    {
        int idx = _occupiedXZ.Count;
        _occupiedXZ.Add(new Vector2(worldX, worldZ));
        _occupiedIndexByKey[spawnKey] = idx;

        long cell = OccupancyCellKey(worldX, worldZ);
        if (!_occupiedGrid.TryGetValue(cell, out List<int> list))
        {
            list = new List<int>(4);
            _occupiedGrid[cell] = list;
        }
        list.Add(idx);
    }

    private void UnregisterOccupied(int spawnKey)
    {
        if (!_occupiedIndexByKey.TryGetValue(spawnKey, out int idx))
            return;

        _occupiedIndexByKey.Remove(spawnKey);
        if (idx < 0 || idx >= _occupiedXZ.Count)
            return;

        Vector2 p = _occupiedXZ[idx];
        long cell = OccupancyCellKey(p.x, p.y);
        if (_occupiedGrid.TryGetValue(cell, out List<int> list))
            list.Remove(idx);

        // Leave a sentinel; list size is capped by maxActive during a run.
        _occupiedXZ[idx] = new Vector2(1e8f, 1e8f);
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

    private GameObject ChoosePrefab(int slot, int sub)
    {
        float total = 0f;
        for (int i = 0; i < environmentTypes.Count; i++)
        {
            var t = environmentTypes[i];
            if (t == null || t.prefab == null) continue;
            total += Mathf.Max(0f, t.baseWeight);
        }
        if (total <= 0f) return null;

        float r = Hash01(slot, sub, _runSeed) * total;
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
        _spawnedByKey.Clear();
        ClearOccupancy();
        _maxSlotIndex = (_totalLength <= 0f) ? 0 : Mathf.FloorToInt(_totalLength / spacing);
        _streamCursorSlot = 0;
    }

    private void ClearAll()
    {
        foreach (var kv in _spawnedByKey)
        {
            if (kv.Value != null)
                Destroy(kv.Value);
        }
        _spawnedByKey.Clear();
        _prefabByKey.Clear();
        _failedKeys.Clear();

        foreach (var kv in _pool)
        {
            while (kv.Value.Count > 0)
            {
                GameObject go = kv.Value.Pop();
                if (go != null)
                    Destroy(go);
            }
        }
        _pool.Clear();
        ClearOccupancy();
    }

    private void EnsurePoolRoot()
    {
        if (_poolRoot != null)
            return;

        var go = new GameObject("EnvironmentPool");
        go.transform.SetParent(transform, false);
        go.SetActive(false);
        _poolRoot = go.transform;
    }

    private GameObject TakeFromPool(GameObject prefab, Vector3 pos, Quaternion rot, Transform parent)
    {
        if (_pool.TryGetValue(prefab, out Stack<GameObject> stack) && stack.Count > 0)
        {
            GameObject reused = stack.Pop();
            reused.transform.SetParent(parent, false);
            reused.transform.SetPositionAndRotation(pos, rot);
            reused.SetActive(true);
            return reused;
        }

        return Instantiate(prefab, pos, rot, parent);
    }

    /// <summary>
    /// Detach a live spawned instance from streaming/pooling so a gorilla can lift and throw it.
    /// Returns false if this object is not currently tracked as a spawned environment prop.
    /// </summary>
    public bool TryClaimInstance(GameObject instance)
    {
        if (instance == null)
            return false;

        int foundKey = int.MinValue;
        foreach (var kv in _spawnedByKey)
        {
            if (kv.Value == instance)
            {
                foundKey = kv.Key;
                break;
            }
        }

        if (foundKey == int.MinValue)
        {
            foreach (var kv in _spawnedByKey)
            {
                if (kv.Value != null && instance.transform.IsChildOf(kv.Value.transform))
                {
                    foundKey = kv.Key;
                    instance = kv.Value;
                    break;
                }
            }
        }

        if (foundKey == int.MinValue)
            return false;

        _spawnedByKey.Remove(foundKey);
        _prefabByKey.Remove(foundKey);
        UnregisterOccupied(foundKey);
        instance.transform.SetParent(null, true);
        return true;
    }

    private void RecycleKey(int key)
    {
        if (!_spawnedByKey.TryGetValue(key, out GameObject go))
            return;

        _spawnedByKey.Remove(key);
        _prefabByKey.TryGetValue(key, out GameObject prefab);
        _prefabByKey.Remove(key);
        _failedKeys.Remove(key);
        UnregisterOccupied(key);

        if (go == null)
            return;

        if (prefab == null)
        {
            Destroy(go);
            return;
        }

        go.SetActive(false);
        if (_poolRoot != null)
            go.transform.SetParent(_poolRoot, false);

        if (!_pool.TryGetValue(prefab, out Stack<GameObject> stack))
        {
            stack = new Stack<GameObject>(16);
            _pool[prefab] = stack;
        }
        stack.Push(go);
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

    private static void SnapInstanceToGroundOnPlane(GameObject go, Vector3 surfacePoint, Vector3 surfaceNormal, float pad, float maxDownDistance)
    {
        if (go == null) return;

        Vector3 n = surfaceNormal.sqrMagnitude > 1e-8f ? surfaceNormal.normalized : Vector3.up;
        if (!TryGetBottomMostSignedDistance(go, surfacePoint, n, out float minSigned))
            return;

        float correction = pad - minSigned;
        if (Mathf.Abs(correction) <= 0.0001f) return;

        float cap = Mathf.Abs(maxDownDistance);
        if (cap > 0.0001f)
            correction = Mathf.Clamp(correction, -cap, cap);

        go.transform.position += n * correction;
    }

    private static bool TryGetBottomMostSignedDistance(GameObject go, Vector3 surfacePoint, Vector3 n, out float minSigned)
    {
        minSigned = float.MaxValue;
        bool found = false;

        var meshFilters = go.GetComponentsInChildren<MeshFilter>(true);
        for (int i = 0; i < meshFilters.Length; i++)
        {
            MeshFilter mf = meshFilters[i];
            if (mf == null || mf.sharedMesh == null) continue;
            AccumulateLocalBoundsBottom(mf.transform.localToWorldMatrix, mf.sharedMesh.bounds, surfacePoint, n, ref minSigned, ref found);
        }

        if (!found)
        {
            var rends = go.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < rends.Length; i++)
            {
                Renderer r = rends[i];
                if (r == null || !r.enabled) continue;
                if (r is ParticleSystemRenderer || r is TrailRenderer || r is LineRenderer)
                    continue;
                AccumulateWorldAabbBottom(r.bounds, surfacePoint, n, ref minSigned, ref found);
            }
        }

        return found;
    }

    private static void AccumulateLocalBoundsBottom(
        Matrix4x4 localToWorld, Bounds localBounds, Vector3 surfacePoint, Vector3 n, ref float minSigned, ref bool found)
    {
        Vector3 c = localBounds.center;
        Vector3 e = localBounds.extents;
        for (int i = 0; i < 8; i++)
        {
            Vector3 lp = c + new Vector3(
                (i & 1) == 0 ? -e.x : e.x,
                (i & 2) == 0 ? -e.y : e.y,
                (i & 4) == 0 ? -e.z : e.z);
            float s = Vector3.Dot(localToWorld.MultiplyPoint3x4(lp) - surfacePoint, n);
            if (s < minSigned) minSigned = s;
            found = true;
        }
    }

    private static void AccumulateWorldAabbBottom(Bounds worldBounds, Vector3 surfacePoint, Vector3 n, ref float minSigned, ref bool found)
    {
        Vector3 absN = new Vector3(Mathf.Abs(n.x), Mathf.Abs(n.y), Mathf.Abs(n.z));
        float s = Vector3.Dot(worldBounds.center - surfacePoint, n) - Vector3.Dot(worldBounds.extents, absN);
        if (s < minSigned) minSigned = s;
        found = true;
    }

    private void ConfigureEnvironmentRigidbodies(GameObject go)
    {
        if (!forceKinematicRigidbodies || go == null) return;
        SpawnUtils.ForceKinematicNoGravity(go);
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
