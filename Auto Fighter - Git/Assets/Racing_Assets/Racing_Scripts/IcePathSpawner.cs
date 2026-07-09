using System.Collections.Generic;
using System.ComponentModel;
using System.Reflection;
using UnityEngine;

/// <summary>
/// Spawns ice path segments along the procedural track.
/// Ice paths follow the track's curvature and can have internal wiggles.
/// Works similarly to TrackObstacleSpawner with pre-spawn and streaming options.
/// </summary>
[DisallowMultipleComponent]
public class IcePathSpawner : MonoBehaviour, ITrackSpawnQueueSource
{
    [Header("References")]
    [SerializeField] private ProceduralTrackGenerator trackGenerator;
    [SerializeField] private Transform playerTransform;
    [SerializeField] private Transform icePathParent;
    [SerializeField] private GameObject iceSegmentPrefab;

    [Header("Spawn Mode")]
    [Tooltip("If true, fills the whole track with ice paths once in InitializeForRun.")]
    [SerializeField] private bool preSpawnOnInitialize = true;

    [Tooltip("If true, will continue spawning ahead while you drive. Turn OFF if you hate seeing pop-in.")]
    [SerializeField] private bool streamSpawnDuringRun = false;

    [Header("Path Sampling")]
    [SerializeField] private bool useSmoothing = true;
    [SerializeField, Min(1)] private int smoothingSubdivisionsPerSegment = 6;

    [Header("Spawn Settings")]
    [Tooltip("Ideal spacing between potential ice path spawn slots (meters).")]
    [SerializeField] private float icePathSpacing = 100f;
    [SerializeField] private int maxActiveIcePaths = 15;

    [Tooltip("Minimum distance in front of the player where new ice paths are allowed to appear.")]
    [SerializeField] private float minSpawnDistanceAhead = 60f;

    [Tooltip("Maximum distance in front of the player we bother filling with ice paths.")]
    [SerializeField] private float maxSpawnDistanceAhead = 200f;

    [Header("Initial Pre-Spawn")]
    [Tooltip("How far ahead (from start) we pre-fill ice paths before the run begins.")]
    [SerializeField] private float initialPreSpawnDistance = 150f;

    [Tooltip("Do not spawn any behind the player.")]
    [SerializeField] private float despawnBehindDistance = 20f;

    [Header("Randomization")]
    [SerializeField] private float distanceJitter = 15f;
    [SerializeField, Range(0f, 1f)] private float spawnChancePerSlot = 0.3f;

    [Tooltip("Global spawn chance multiplier based on distance (0=start, 1=end).")]
    [SerializeField]
    private AnimationCurve globalSpawnChanceByDistance =
        AnimationCurve.Linear(0f, 0.2f, 1f, 0.6f);

    [Header("Ice Path Properties")]
    [Tooltip("Number of segments per ice path.")]
    [SerializeField, Range(1, 30)] private int segmentsPerPath = 8;

    [Tooltip("Length for each ice segment (meters).")]
    [SerializeField] private float segmentLength = 10f;

    [Tooltip("Width of ice path (should match or be slightly narrower than road).")]
    [SerializeField] private float icePathWidth = 3.5f;

    [Header("Path Curvature & Wiggles")]
    [Tooltip("Maximum wiggle angle (degrees) applied to each segment for variation.")]
    [SerializeField, Range(0f, 15f)] private float maxWiggleAngle = 3f;

    [Tooltip("How closely the ice path follows track turns (0 = straight, 1 = perfect follow).")]
    [SerializeField, Range(0f, 1f)] private float trackFollowStrength = 0.85f;

    [Header("Mini Turns (Ice Path Style)")]
    [SerializeField, Range(0f, 12f)] private float miniTurnMaxYawDeg = 4f;   // small turns
    [SerializeField] private float miniTurnFrequency = 0.015f;              // lower = longer arcs
    [SerializeField, Range(0f, 1f)] private float miniTurnSmoothing = 0.15f; // higher = snappier
    [SerializeField] private float miniTurnSeed = 123.456f;                 // deterministic

    [Header("Raycast")]
    [SerializeField] private LayerMask roadLayer;
    [SerializeField] private float raycastStartHeight = 6f;
    [SerializeField] private float raycastDownDistance = 40f;
    [SerializeField] private float iceHeightOffset = 0.02f;

    [Header("Ice Mesh (Single Strip Visual)")]
    [SerializeField] private Material iceMaterial;
    [SerializeField, Min(0.25f)] private float iceSampleSpacing = 1.0f; // smaller = smoother curve
    [SerializeField] private float iceUVTiling = 0.15f;
    [SerializeField] private bool addIceMeshColliderTrigger = true;

    [Header("Timing")]
    [SerializeField] private float updateInterval = 0.5f;

    [Header("Debug")]
    [SerializeField] private bool verboseDebug = false;

    // Runtime
    private List<Vector3> _path = new();
    private float[] _cumLengths;
    private float _totalLength;
    private Dictionary<int, GameObject> _icePathsBySlot = new();
    private readonly Dictionary<int, int> _surfaceReservationBySlot = new();
    private int _maxSlotIndex;
    private float _updateTimer;
    private int _lastClosestIdx = 0;
    private List<int> _toRemove = new();

    // Prefab metrics (measured once)
    private bool _prefabMetricsReady;
    private float _prefabWidth;
    private float _prefabLength;
    private Vector3 _prefabBaseLocalScale = Vector3.one;

    private GroundSurface _prefabGroundSurface;
    private IcePath _prefabIcePath;
    private readonly TrackSpawnQueuePendingState _queueState = new();
    private readonly TrackSpawnQueueLastSpawn _queueLastSpawn = new();

    public void InitializeForRun(ProceduralTrackGenerator generator, Transform player)
    {
        trackGenerator = generator;
        playerTransform = player;

        if (trackGenerator == null || playerTransform == null)
        {
            Debug.LogError("[IcePathSpawner] InitializeForRun missing refs. " +
                           $"trackGenerator={trackGenerator}, player={playerTransform}");
            return;
        }

        if (iceMaterial == null)
        {
            Debug.LogError("[IcePathSpawner] Missing iceMaterial (ice strip uses a MeshRenderer material).");
            return;
        }

        _prefabGroundSurface = iceSegmentPrefab.GetComponentInChildren<GroundSurface>(true);
        _prefabIcePath = iceSegmentPrefab.GetComponentInChildren<IcePath>(true);

        if (_prefabGroundSurface == null)
            Debug.LogWarning("[IcePathSpawner] iceSegmentPrefab is missing GroundSurface (values won't be copied).");

        if (_prefabIcePath == null)
            Debug.LogWarning("[IcePathSpawner] iceSegmentPrefab is missing IcePath (values won't be copied).");


        EnsurePrefabMetrics();

        if (verboseDebug)
            Debug.Log("[IcePathSpawner] InitializeForRun: rebuilding path + slots.");

        RebuildPath();
        ClearIcePaths();
        SetupSlots();

        if (preSpawnOnInitialize)
            PreSpawnInitialWindow();

        _updateTimer = 0f;
    }

    private void Update()
    {
        if (_path.Count < 2 || playerTransform == null || iceMaterial == null)
            return;

        if (_queueState.IsControlled)
        {
            DespawnBehindIcePaths(GetPlayerDistance());
            _updateTimer += Time.deltaTime;
            if (_updateTimer >= updateInterval)
            {
                _updateTimer = 0f;
                _queueState.TrySubmit(this);
            }
            return;
        }

        if (!streamSpawnDuringRun)
            return;

        _updateTimer += Time.deltaTime;
        if (_updateTimer < updateInterval) return;

        _updateTimer = 0f;
        StreamIcePaths();
    }

    private void EnsurePrefabMetrics()
    {
        if (_prefabMetricsReady) return;

        // Instantiate once to measure real-world bounds reliably (no guessing / hardcoding).
        GameObject temp = Instantiate(iceSegmentPrefab);
        temp.SetActive(false);

        _prefabBaseLocalScale = temp.transform.localScale;

        var renderers = temp.GetComponentsInChildren<Renderer>(true);
        if (renderers == null || renderers.Length == 0)
        {
            Debug.LogWarning("[IcePathSpawner] iceSegmentPrefab has no Renderer. Using fallback dimensions 1x1.");
            _prefabWidth = 1f;
            _prefabLength = 1f;
        }
        else
        {
            Bounds b = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++)
                b.Encapsulate(renderers[i].bounds);

            // Width is X, length is Z in world units.
            _prefabWidth = Mathf.Max(0.0001f, b.size.x);
            _prefabLength = Mathf.Max(0.0001f, b.size.z);
        }

        Destroy(temp);
        _prefabMetricsReady = true;

        if (verboseDebug)
            Debug.Log($"[IcePathSpawner] Prefab metrics: width={_prefabWidth:F3}, length={_prefabLength:F3}, baseScale={_prefabBaseLocalScale}");
    }

    private void RebuildPath()
    {
        _path.Clear();
        _cumLengths = null;
        _totalLength = 0f;
        _lastClosestIdx = 0;

        if (trackGenerator == null) return;
        if (!TrackPathSampling.RebuildPathFromRoadCenterline(trackGenerator, _path, ref _cumLengths, out _totalLength))
            Debug.LogError("[IcePathSpawner] Track path has < 2 points. Cannot spawn ice paths.");
    }

    private void SetupSlots()
    {
        _icePathsBySlot.Clear();
        _maxSlotIndex = Mathf.FloorToInt(_totalLength / Mathf.Max(0.0001f, icePathSpacing));
    }

    private void StreamIcePaths()
    {
        if (_totalLength <= 0f || _maxSlotIndex <= 0)
            return;

        float playerDist = GetPlayerDistance();

        float spawnStartDist = Mathf.Clamp(playerDist + minSpawnDistanceAhead, 0f, _totalLength);
        float spawnEndDist = Mathf.Clamp(playerDist + maxSpawnDistanceAhead, 0f, _totalLength);

        int startSlot = Mathf.Clamp(Mathf.FloorToInt(spawnStartDist / icePathSpacing), 0, _maxSlotIndex);
        int endSlot = Mathf.Clamp(Mathf.FloorToInt(spawnEndDist / icePathSpacing), 0, _maxSlotIndex);

        for (int slot = startSlot; slot <= endSlot; slot++)
        {
            if (_icePathsBySlot.ContainsKey(slot))
                continue;

            if (_icePathsBySlot.Count >= maxActiveIcePaths)
                break;

            float dist = slot * icePathSpacing;

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

            TrySpawnIcePathAtDistance(slot, dist);
        }

        DespawnBehindIcePaths(playerDist);
    }

    private void DespawnBehindIcePaths(float playerDist)
    {
        _toRemove.Clear();
        foreach (var kvp in _icePathsBySlot)
        {
            float dist = kvp.Key * icePathSpacing;
            if (dist < playerDist - despawnBehindDistance)
                _toRemove.Add(kvp.Key);
        }

        for (int i = 0; i < _toRemove.Count; i++)
        {
            int slot = _toRemove[i];
            if (_icePathsBySlot.TryGetValue(slot, out var obj) && obj != null)
                Destroy(obj);

            if (_surfaceReservationBySlot.TryGetValue(slot, out int reservationId))
            {
                TrackSurfaceSpawnRegistry.Unregister(reservationId);
                _surfaceReservationBySlot.Remove(slot);
            }

            _icePathsBySlot.Remove(slot);
        }
    }

    private bool TrySpawnOneAhead()
    {
        if (_totalLength <= 0f || _maxSlotIndex <= 0)
            return false;

        float playerDist = GetPlayerDistance();
        float spawnStartDist = Mathf.Clamp(playerDist + minSpawnDistanceAhead, 0f, _totalLength);
        float spawnEndDist = Mathf.Clamp(playerDist + maxSpawnDistanceAhead, 0f, _totalLength);

        int startSlot = Mathf.Clamp(Mathf.FloorToInt(spawnStartDist / icePathSpacing), 0, _maxSlotIndex);
        int endSlot = Mathf.Clamp(Mathf.FloorToInt(spawnEndDist / icePathSpacing), 0, _maxSlotIndex);

        for (int slot = startSlot; slot <= endSlot; slot++)
        {
            if (_icePathsBySlot.ContainsKey(slot))
                continue;

            if (_icePathsBySlot.Count >= maxActiveIcePaths)
                break;

            float dist = slot * icePathSpacing;
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

            int before = _icePathsBySlot.Count;
            TrySpawnIcePathAtDistance(slot, dist);
            if (_icePathsBySlot.Count > before)
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
            Vector3 ab = (b - a);
            float abSqr = ab.sqrMagnitude;
            if (abSqr < 1e-6f) continue;

            float t = Mathf.Clamp01(Vector3.Dot(p - a, ab) / abSqr);
            Vector3 proj = a + ab * t;

            float d = (p - proj).sqrMagnitude;
            if (d < best) { _lastClosestIdx = i; best = d; }
        }

        Vector3 A = _path[_lastClosestIdx];
        Vector3 B = _path[_lastClosestIdx + 1];
        Vector3 AB = (B - A);
        float ABsqr = Mathf.Max(1e-6f, AB.sqrMagnitude);

        float prog = Mathf.Clamp01(Vector3.Dot(p - A, AB) / ABsqr);
        float segLen = Vector3.Distance(A, B);

        return _cumLengths[_lastClosestIdx] + prog * segLen;
    }

    private void TrySpawnIcePathAtDistance(int slot, float baseDist)
    {
        float totalIceLen = TrackSurfaceSpawnUtil.GetIcePathLength(segmentsPerPath, segmentLength);
        if (!TryFindClearIceStart(baseDist, totalIceLen, out float startDist))
        {
            if (verboseDebug)
                Debug.Log($"[IcePathSpawner] Skipped ice path slot {slot} near {baseDist:F1}m (overlaps boost/ramp/ice).");
            return;
        }

        GameObject pathParent = new GameObject($"IcePath_{startDist:F0}");
        Transform parent = icePathParent ? icePathParent : transform;
        pathParent.transform.SetParent(parent);

        _icePathsBySlot[slot] = pathParent;


        GameObject stripGO = new GameObject($"IceStrip_{startDist:F0}");
        stripGO.layer = LayerMask.NameToLayer("RoadSurface");
        stripGO.transform.SetParent(pathParent.transform, worldPositionStays: false);
        stripGO.transform.localPosition = Vector3.zero;
        stripGO.transform.localRotation = Quaternion.identity;
        stripGO.transform.localScale = Vector3.one;

        BuildIceStripMesh(stripGO, startDist, totalIceLen);

        float centerDist = Mathf.Clamp(startDist + totalIceLen * 0.5f, 0f, _totalLength);
        SampleAlongPath(centerDist, out Vector3 centerPos, out _);
        _queueLastSpawn.Record(centerPos, "Ice Path");
        float iceEndDist = Mathf.Min(startDist + totalIceLen, _totalLength);
        _surfaceReservationBySlot[slot] = TrackSurfaceSpawnRegistry.Register(startDist, iceEndDist);

        if (verboseDebug)
            Debug.Log($"[IcePathSpawner] Spawned ice path at slot {slot}, distance {startDist:F1}m");
    }

    private bool TryFindClearIceStart(float baseDist, float totalIceLen, out float startDist)
    {
        const int maxAttempts = 6;
        for (int attempt = 0; attempt < maxAttempts; attempt++)
        {
            float jitter = Random.Range(-distanceJitter, distanceJitter);
            float candidate = Mathf.Clamp(baseDist + jitter, 0f, _totalLength);
            float endDist = Mathf.Min(candidate + totalIceLen, _totalLength);
            if (endDist <= candidate + 0.01f)
                continue;

            if (!TrackSurfaceSpawnRegistry.Overlaps(candidate, endDist))
            {
                startDist = candidate;
                return true;
            }
        }

        startDist = 0f;
        return false;
    }

    private void PreSpawnInitialWindow()
    {
        if (_totalLength <= 0f || _maxSlotIndex <= 0)
            return;

        float preSpawnEnd = Mathf.Clamp(initialPreSpawnDistance, 0f, _totalLength);
        int endSlot = Mathf.FloorToInt(preSpawnEnd / icePathSpacing);

        for (int slot = 0; slot <= endSlot; slot++)
        {
            if (_icePathsBySlot.ContainsKey(slot))
                continue;

            float dist = slot * icePathSpacing;

            float norm = (_totalLength > 0f) ? Mathf.Clamp01(dist / _totalLength) : 0f;
            float difficultyMult = (globalSpawnChanceByDistance != null)
                ? Mathf.Max(0f, globalSpawnChanceByDistance.Evaluate(norm))
                : 1f;

            float effectiveChance = spawnChancePerSlot * difficultyMult;
            if (effectiveChance <= 0f) continue;
            if (Random.value > effectiveChance) continue;

            TrySpawnIcePathAtDistance(slot, dist);
        }

        if (verboseDebug)
            Debug.Log($"[IcePathSpawner] PreSpawnInitialWindow spawned {_icePathsBySlot.Count} ice paths up to {preSpawnEnd:0.0}m.");
    }

    private void BuildIceStripMesh(GameObject owner, float startDist, float length)
    {
        float endDist = Mathf.Min(startDist + length, _totalLength);
        if (endDist <= startDist + 0.01f) return;

        // How many samples along the path (more samples = smoother)
        int samples = Mathf.Max(2, Mathf.CeilToInt((endDist - startDist) / iceSampleSpacing) + 1);

        var verts = new Vector3[samples * 2];
        var normals = new Vector3[samples * 2];
        var uvs = new Vector2[samples * 2];
        var tris = new int[(samples - 1) * 6];

        float yawOffsetDeg = 0f; // mini-turn state (smooth)
        float halfW = icePathWidth * 0.5f;

        for (int i = 0; i < samples; i++)
        {
            float dist = Mathf.Lerp(startDist, endDist, i / (float)(samples - 1));
            Vector3 pos = GetPositionAtDistance(dist);

            // Base forward from path tangent
            Vector3 trackFwd = GetTangentAtDistance(dist);
            if (trackFwd.sqrMagnitude < 1e-6f) trackFwd = Vector3.forward;
            trackFwd.Normalize();

            // Mini-turns: continuous yaw offset over distance (NOT random per segment)
            float noise = Mathf.PerlinNoise(miniTurnSeed + dist * miniTurnFrequency, 0.1234f) * 2f - 1f;
            float targetYaw = noise * miniTurnMaxYawDeg;
            yawOffsetDeg = Mathf.Lerp(yawOffsetDeg, targetYaw, miniTurnSmoothing);

            Vector3 fwd = (Quaternion.AngleAxis(yawOffsetDeg, Vector3.up) * trackFwd).normalized;

            // Raycast to snap onto road + get normal
            Vector3 origin = pos + Vector3.up * raycastStartHeight;

            Vector3 n = Vector3.up;
            Vector3 finalPos = pos;

            bool hitSomething = Physics.Raycast(
                origin, Vector3.down, out RaycastHit hit,
                raycastStartHeight + raycastDownDistance,
                roadLayer, QueryTriggerInteraction.Ignore);

            if (!hitSomething)
            {
                hitSomething = Physics.Raycast(
                    origin, Vector3.down, out hit,
                    raycastStartHeight + raycastDownDistance,
                    ~0, QueryTriggerInteraction.Ignore);
            }

            if (hitSomething)
            {
                n = hit.normal;
                finalPos = hit.point + n * iceHeightOffset;
            }

            // Build left/right using the surface normal
            Vector3 right = Vector3.Cross(n, fwd);
            if (right.sqrMagnitude < 1e-6f) right = Vector3.Cross(Vector3.up, fwd);
            right.Normalize();

            int L = i * 2;
            int R = i * 2 + 1;

            verts[L] = owner.transform.InverseTransformPoint(finalPos - right * halfW);
            verts[R] = owner.transform.InverseTransformPoint(finalPos + right * halfW);

            normals[L] = normals[R] = n;

            float v = (dist - startDist) * iceUVTiling;
            uvs[L] = new Vector2(0f, v);
            uvs[R] = new Vector2(1f, v);
        }

        int ti = 0;
        for (int i = 0; i < samples - 1; i++)
        {
            int i0 = i * 2;
            int i1 = i * 2 + 1;
            int i2 = (i + 1) * 2;
            int i3 = (i + 1) * 2 + 1;

            tris[ti++] = i0;
            tris[ti++] = i2;
            tris[ti++] = i1;

            tris[ti++] = i2;
            tris[ti++] = i3;
            tris[ti++] = i1;
        }

        var mf = owner.GetComponent<MeshFilter>();
        if (mf == null) mf = owner.AddComponent<MeshFilter>();

        var mr = owner.GetComponent<MeshRenderer>();
        if (mr == null) mr = owner.AddComponent<MeshRenderer>();

        Mesh m = new Mesh { name = "IceStripMesh" };
        m.vertices = verts;
        m.normals = normals;
        m.uv = uvs;
        m.triangles = tris;
        m.RecalculateBounds();

        mf.sharedMesh = m;

        // add scripts + values to the strip itself (optional but nice)
        CopyComponent(_prefabGroundSurface, owner);
        CopyComponent(_prefabIcePath, owner);

        BuildIceTriggerBoxes(owner, verts, samples);


        if (iceMaterial != null)
            mr.sharedMaterial = iceMaterial;

    }

    private void BuildIceTriggerBoxes(GameObject owner, Vector3[] vertsLocal, int samples)
    {
        // one box per segment between sample i and i+1
        float triggerHeight = 0.5f; // keep small
        for (int i = 0; i < samples - 1; i++)
        {
            // centerline between left/right verts at sample i and i+1 (local space)
            Vector3 L0 = vertsLocal[i * 2];
            Vector3 R0 = vertsLocal[i * 2 + 1];
            Vector3 L1 = vertsLocal[(i + 1) * 2];
            Vector3 R1 = vertsLocal[(i + 1) * 2 + 1];

            Vector3 C0 = (L0 + R0) * 0.5f;
            Vector3 C1 = (L1 + R1) * 0.5f;
            Vector3 mid = (C0 + C1) * 0.5f;

            Vector3 fwd = (C1 - C0);
            float len = fwd.magnitude;
            if (len < 0.001f) continue;
            fwd /= len;

            var t = new GameObject($"IceTrig_{i}").transform;
            t.gameObject.layer = owner.layer;
            t.SetParent(owner.transform, false);
            t.localPosition = mid;
            t.localRotation = Quaternion.LookRotation(fwd, Vector3.up);

            var box = t.gameObject.AddComponent<BoxCollider>();
            box.isTrigger = true;
            box.size = new Vector3(icePathWidth, .1f, len);
            box.center = new Vector3(0f, box.size.y * -0.5f, 0f);

            CopyComponent(_prefabGroundSurface, t.gameObject);
            CopyComponent(_prefabIcePath, t.gameObject);
        }
    }

    private void SampleAlongPath(float dist, out Vector3 pos, out Vector3 fwd)
    {
        dist = Mathf.Clamp(dist, 0f, _totalLength);

        int idx = 0;
        for (int i = 0; i < _cumLengths.Length - 1; i++)
        {
            if (_cumLengths[i + 1] >= dist)
            {
                idx = i;
                break;
            }
        }

        float segLen = _cumLengths[idx + 1] - _cumLengths[idx];
        float t = (dist - _cumLengths[idx]) / Mathf.Max(segLen, 0.0001f);

        pos = Vector3.Lerp(_path[idx], _path[idx + 1], t);
        fwd = (_path[idx + 1] - _path[idx]).normalized;
        if (fwd.sqrMagnitude < 1e-6f) fwd = Vector3.forward;
    }

    private Vector3 GetPositionAtDistance(float dist)
    {
        SampleAlongPath(dist, out Vector3 pos, out _);
        return pos;
    }

    private Vector3 GetTangentAtDistance(float dist)
    {
        SampleAlongPath(dist, out _, out Vector3 fwd);
        return fwd;
    }

    private void ClearIcePaths()
    {
        foreach (var kvp in _surfaceReservationBySlot)
            TrackSurfaceSpawnRegistry.Unregister(kvp.Value);

        foreach (var o in _icePathsBySlot.Values)
            if (o) Destroy(o);

        _icePathsBySlot.Clear();
        _surfaceReservationBySlot.Clear();
    }

    public void SetPlayerTransform(Transform player)
    {
        playerTransform = player;
    }


    private static T CopyComponent<T>(T source, GameObject destination) where T : UnityEngine.Component
    {
        // Use Unity "fake null" checks
        if (!source || destination == null) return null;

        // Ensure destination has the component
        T copy = destination.GetComponent<T>();
        if (!copy) copy = destination.AddComponent<T>();

        if (!copy)
        {
            Debug.LogError($"[IcePathSpawner] Failed to AddComponent<{typeof(T).Name}> to {destination.name}. " +
                           $"Is the script missing/invalid?");
            return null;
        }

        CopySerializedFieldsRuntime(source, copy);
        return copy;
    }

    private static void CopySerializedFieldsRuntime(UnityEngine.Component src, UnityEngine.Component dst)
    {
        if (!src || !dst) return;

        System.Type type = src.GetType();
        if (type != dst.GetType())
        {
            Debug.LogError($"[IcePathSpawner] CopySerializedFieldsRuntime type mismatch: {type} -> {dst.GetType()}");
            return;
        }

        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        // Copy fields that Unity would serialize: public fields OR [SerializeField] private fields
        foreach (FieldInfo f in type.GetFields(flags))
        {
            if (f.IsStatic) continue;
            if (f.IsInitOnly) continue; // readonly
            if (f.IsNotSerialized) continue;

            bool isUnitySerialized = f.IsPublic || f.GetCustomAttribute<SerializeField>() != null;
            if (!isUnitySerialized) continue;

            object value = f.GetValue(src);
            f.SetValue(dst, value);
        }
    }

    public string SpawnQueueLabel => "Ice Paths";
    public bool IsSpawnQueueReady => _path.Count >= 2 && playerTransform != null && iceMaterial != null && iceSegmentPrefab != null;
    public bool HasSpawnQueueCapacity => _icePathsBySlot.Count < maxActiveIcePaths;
    public bool HasPendingSpawnRequest => _queueState.HasPending;
    public bool TrySubmitSpawnRequest() => _queueState.TrySubmit(this);
    public bool TryExecutePendingSpawn() => _queueState.TryExecute(TrySpawnOneAhead);
    public bool TryConsumeLastSpawnReport(out TrackSpawnQueueSpawnReport report) => _queueLastSpawn.TryConsume(out report);
    public void CancelPendingSpawnRequest() => _queueState.Cancel();
    public void SetQueueControlledAutonomous(bool controlled, TrackSpawnerQueue owner = null) => _queueState.Bind(controlled, owner);
}
