using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.Serialization;

/// <summary>
/// Spawns full-width icy road sections along the procedural track.
/// Each section covers the entire road width and follows the centerline bends.
/// </summary>
[DisallowMultipleComponent]
public class IcePathSpawner : MonoBehaviour, ITrackSpawnQueueSource
{
    [Header("References")]
    [SerializeField] private ProceduralTrackGenerator trackGenerator;
    [SerializeField] private Transform playerTransform;
    [SerializeField] private Transform icePathParent;
    [Tooltip("Optional template prefab — GroundSurface / IcePath values are copied onto each section.")]
    [SerializeField] private GameObject iceSegmentPrefab;

    [Header("Spawn Mode")]
    [Tooltip("If true, fills ahead of the start once in InitializeForRun.")]
    [SerializeField] private bool preSpawnOnInitialize = true;

    [Tooltip("If true, keeps spawning ice sections ahead while driving.")]
    [SerializeField] private bool streamSpawnDuringRun = true;

    [Header("Path Sampling")]
    [SerializeField] private bool useSmoothing = true;
    [SerializeField, Min(1)] private int smoothingSubdivisionsPerSegment = 6;

    [Header("Spawn Settings")]
    [Tooltip("Distance between candidate ice-section spawn slots (meters).")]
    [FormerlySerializedAs("icePathSpacing")]
    [SerializeField] private float sectionSpacing = 80f;

    [FormerlySerializedAs("maxActiveIcePaths")]
    [Tooltip("Max ice sections for this run. Once this many have spawned, no more will appear (despawning does not free budget). 0 = unlimited.")]
    [SerializeField, Min(0)] private int maxActiveSections = 8;

    [SerializeField] private float minSpawnDistanceAhead = 40f;
    [SerializeField] private float maxSpawnDistanceAhead = 220f;

    [Header("Initial Pre-Spawn")]
    [SerializeField] private float initialPreSpawnDistance = 250f;
    [SerializeField] private float despawnBehindDistance = 30f;

    [Header("Randomization")]
    [SerializeField] private float distanceJitter = 12f;
    [Tooltip("Chance to spawn an ice section (0–1). X = at track start, Y = at track end.")]
    [SerializeField] private Vector2 spawnChanceByProgress = new Vector2(0.4f, 0.55f);

    [Header("Ice Section Shape")]
    [Tooltip("Along-track length of each icy patch (meters).")]
    [SerializeField, Min(4f)] private float sectionLength = 28f;

    [Tooltip("Random +/- added to section length.")]
    [SerializeField, Min(0f)] private float sectionLengthJitter = 8f;

    [Tooltip("1 = exact road width. Slightly above 1 covers road edges.")]
    [SerializeField, Range(0.9f, 1.15f)] private float roadWidthScale = 1.02f;

    [Header("Raycast")]
    [SerializeField] private LayerMask roadLayer = ~0;
    [SerializeField] private float raycastStartHeight = 6f;
    [SerializeField] private float raycastDownDistance = 40f;
    [SerializeField] private float iceHeightOffset = 0.02f;

    [Header("Ice Mesh")]
    [SerializeField] private Material iceMaterial;
    [SerializeField, Min(0.25f)] private float iceSampleSpacing = 1.0f;
    [SerializeField] private float iceUVTiling = 0.15f;
    [SerializeField] private bool addIceMeshColliderTrigger = true;

    [Header("Timing")]
    [SerializeField] private float updateInterval = 0.5f;

    [Header("Debug")]
    [SerializeField] private bool verboseDebug = false;

    // ------------------------------------------------------------------------
    // Per-trial config (TrialConfig). Apply BEFORE InitializeForRun.
    // ------------------------------------------------------------------------
    public void ApplyConfig(TrialConfig.IcePathSettings s)
    {
        if (s == null || !s.overrideIcePaths) return;

        if (s.iceSegmentPrefab != null) iceSegmentPrefab = s.iceSegmentPrefab;
        if (s.iceMaterial != null) iceMaterial = s.iceMaterial;

        preSpawnOnInitialize = s.preSpawnOnInitialize;
        streamSpawnDuringRun = s.streamSpawnDuringRun;

        useSmoothing = s.useSmoothing;
        smoothingSubdivisionsPerSegment = s.smoothingSubdivisionsPerSegment;

        sectionSpacing = s.sectionSpacing;
        maxActiveSections = s.maxActiveSections;
        minSpawnDistanceAhead = s.minSpawnDistanceAhead;
        maxSpawnDistanceAhead = s.maxSpawnDistanceAhead;

        initialPreSpawnDistance = s.initialPreSpawnDistance;
        despawnBehindDistance = s.despawnBehindDistance;

        distanceJitter = s.distanceJitter;
        spawnChanceByProgress = s.spawnChanceByProgress;

        sectionLength = s.sectionLength;
        sectionLengthJitter = s.sectionLengthJitter;
        roadWidthScale = s.roadWidthScale;

        roadLayer = s.roadLayer;
        raycastStartHeight = s.raycastStartHeight;
        raycastDownDistance = s.raycastDownDistance;
        iceHeightOffset = s.iceHeightOffset;

        iceSampleSpacing = s.iceSampleSpacing;
        iceUVTiling = s.iceUVTiling;
        addIceMeshColliderTrigger = s.addIceMeshColliderTrigger;

        updateInterval = s.updateInterval;
        verboseDebug = s.verboseDebug;
    }

    public TrialConfig.IcePathSettings CaptureConfig()
    {
        return new TrialConfig.IcePathSettings
        {
            overrideIcePaths = true,
            iceSegmentPrefab = iceSegmentPrefab,
            iceMaterial = iceMaterial,
            preSpawnOnInitialize = preSpawnOnInitialize,
            streamSpawnDuringRun = streamSpawnDuringRun,
            useSmoothing = useSmoothing,
            smoothingSubdivisionsPerSegment = smoothingSubdivisionsPerSegment,
            sectionSpacing = sectionSpacing,
            maxActiveSections = maxActiveSections,
            minSpawnDistanceAhead = minSpawnDistanceAhead,
            maxSpawnDistanceAhead = maxSpawnDistanceAhead,
            initialPreSpawnDistance = initialPreSpawnDistance,
            despawnBehindDistance = despawnBehindDistance,
            distanceJitter = distanceJitter,
            spawnChanceByProgress = spawnChanceByProgress,
            sectionLength = sectionLength,
            sectionLengthJitter = sectionLengthJitter,
            roadWidthScale = roadWidthScale,
            roadLayer = roadLayer,
            raycastStartHeight = raycastStartHeight,
            raycastDownDistance = raycastDownDistance,
            iceHeightOffset = iceHeightOffset,
            iceSampleSpacing = iceSampleSpacing,
            iceUVTiling = iceUVTiling,
            addIceMeshColliderTrigger = addIceMeshColliderTrigger,
            updateInterval = updateInterval,
            verboseDebug = verboseDebug,
        };
    }

    private readonly List<Vector3> _path = new();
    private readonly Dictionary<int, GameObject> _sectionsBySlot = new();
    private readonly Dictionary<int, int> _surfaceReservationBySlot = new();
    /// <summary>Along-track end distance of each active section (despawn only after player clears this).</summary>
    private readonly Dictionary<int, float> _sectionEndDistBySlot = new();
    private readonly List<int> _toRemove = new();
    private readonly TrackSpawnQueuePendingState _queueState = new();
    private readonly TrackSpawnQueueLastSpawn _queueLastSpawn = new();

    private float[] _cumLengths;
    private float _totalLength;
    private int _maxSlotIndex;
    private float _updateTimer;
    private int _lastClosestIdx;
    /// <summary>Total successful ice section spawns this run (not reduced by despawn).</summary>
    private int _spawnedThisRun;

    private GroundSurface _prefabGroundSurface;
    private IcePath _prefabIcePath;

    private int EffectiveMaxSections => maxActiveSections <= 0 ? int.MaxValue : maxActiveSections;
    private bool CanSpawnMoreSections =>
        _spawnedThisRun < EffectiveMaxSections && _sectionsBySlot.Count < EffectiveMaxSections;

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
            Debug.LogError("[IcePathSpawner] Missing iceMaterial (ice section uses a MeshRenderer material).");
            return;
        }

        CachePrefabTemplates();

        if (verboseDebug)
            Debug.Log("[IcePathSpawner] InitializeForRun: rebuilding path + ice sections.");

        RebuildPath();
        ClearSections();
        SetupSlots();
        _spawnedThisRun = 0;

        if (preSpawnOnInitialize)
            PreSpawnInitialWindow();

        _updateTimer = 0f;

        if (verboseDebug)
            Debug.Log($"[IcePathSpawner] Init done. pathLen={_totalLength:F0}m, slots={_maxSlotIndex + 1}, " +
                      $"spawned={_sectionsBySlot.Count}, maxSections={maxActiveSections}, stream={streamSpawnDuringRun}.");
    }

    private void Update()
    {
        if (_path.Count < 2 || playerTransform == null || iceMaterial == null)
            return;

        float playerDist = GetPlayerDistance();

        // Always cull only after the player has cleared the section end (never mid-patch).
        DespawnBehindSections(playerDist);

        if (_queueState.IsControlled)
        {
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
        StreamSections(playerDist);
    }

    private void CachePrefabTemplates()
    {
        _prefabGroundSurface = null;
        _prefabIcePath = null;

        if (iceSegmentPrefab == null)
            return;

        _prefabGroundSurface = iceSegmentPrefab.GetComponentInChildren<GroundSurface>(true);
        _prefabIcePath = iceSegmentPrefab.GetComponentInChildren<IcePath>(true);

        if (_prefabGroundSurface == null)
            Debug.LogWarning("[IcePathSpawner] iceSegmentPrefab is missing GroundSurface (using Ice defaults).");
        if (_prefabIcePath == null)
            Debug.LogWarning("[IcePathSpawner] iceSegmentPrefab is missing IcePath (adding a basic IcePath).");
    }

    private void RebuildPath()
    {
        _path.Clear();
        _cumLengths = null;
        _totalLength = 0f;
        _lastClosestIdx = 0;

        if (trackGenerator == null) return;
        if (!TrackPathSampling.RebuildPathFromRoadCenterline(trackGenerator, _path, ref _cumLengths, out _totalLength))
            Debug.LogError("[IcePathSpawner] Track path has < 2 points. Cannot spawn ice sections.");
    }

    private void SetupSlots()
    {
        _sectionsBySlot.Clear();
        float spacing = Mathf.Max(1f, sectionSpacing);
        _maxSlotIndex = Mathf.Max(0, Mathf.FloorToInt(_totalLength / spacing) - 1);
    }

    private void StreamSections(float playerDist)
    {
        if (_totalLength <= 0f)
            return;

        TryFillWindow(playerDist + minSpawnDistanceAhead, playerDist + maxSpawnDistanceAhead, respectChance: true);
    }

    private void PreSpawnInitialWindow()
    {
        if (_totalLength <= 0f)
            return;

        float preSpawnEnd = Mathf.Clamp(initialPreSpawnDistance, 0f, _totalLength);
        TryFillWindow(0f, preSpawnEnd, respectChance: true);

        if (verboseDebug)
            Debug.Log($"[IcePathSpawner] PreSpawnInitialWindow spawned {_sectionsBySlot.Count} ice section(s) up to {preSpawnEnd:0.0}m.");
    }

    private void TryFillWindow(float startDist, float endDist, bool respectChance)
    {
        float spacing = Mathf.Max(1f, sectionSpacing);
        startDist = Mathf.Clamp(startDist, 0f, _totalLength);
        endDist = Mathf.Clamp(endDist, 0f, _totalLength);
        if (endDist <= startDist + 0.01f)
            return;

        int startSlot = Mathf.Clamp(Mathf.FloorToInt(startDist / spacing), 0, _maxSlotIndex);
        int endSlot = Mathf.Clamp(Mathf.FloorToInt(endDist / spacing), 0, _maxSlotIndex);

        for (int slot = startSlot; slot <= endSlot; slot++)
        {
            if (_sectionsBySlot.ContainsKey(slot))
                continue;
            if (!CanSpawnMoreSections)
                break;

            float dist = slot * spacing;
            if (dist < startDist - 0.01f || dist > endDist + 0.01f)
                continue;

            if (respectChance && !PassesSpawnChance(dist))
                continue;

            TrySpawnSectionAtDistance(slot, dist);
        }
    }

    private bool PassesSpawnChance(float dist)
    {
        float norm = TrackProgressRange.NormalizedDistance(dist, _totalLength);
        float effectiveChance = TrackProgressRange.Lerp01(spawnChanceByProgress, norm);
        if (effectiveChance <= 0f)
            return false;

        return Random.value <= effectiveChance;
    }

    private bool TrySpawnOneAhead()
    {
        if (_totalLength <= 0f || !CanSpawnMoreSections)
            return false;

        float playerDist = GetPlayerDistance();
        float spacing = Mathf.Max(1f, sectionSpacing);
        float spawnStartDist = Mathf.Clamp(playerDist + minSpawnDistanceAhead, 0f, _totalLength);
        float spawnEndDist = Mathf.Clamp(playerDist + maxSpawnDistanceAhead, 0f, _totalLength);

        int startSlot = Mathf.Clamp(Mathf.FloorToInt(spawnStartDist / spacing), 0, _maxSlotIndex);
        int endSlot = Mathf.Clamp(Mathf.FloorToInt(spawnEndDist / spacing), 0, _maxSlotIndex);

        for (int slot = startSlot; slot <= endSlot; slot++)
        {
            if (_sectionsBySlot.ContainsKey(slot))
                continue;

            float dist = slot * spacing;
            if (dist < spawnStartDist)
                continue;
            if (!PassesSpawnChance(dist))
                continue;

            int before = _sectionsBySlot.Count;
            TrySpawnSectionAtDistance(slot, dist);
            if (_sectionsBySlot.Count > before)
                return true;
        }

        return false;
    }

    private void DespawnBehindSections(float playerDist)
    {
        _toRemove.Clear();
        foreach (var kvp in _sectionsBySlot)
        {
            int slot = kvp.Key;
            // Prefer the real section end; fall back to slot start only if missing.
            float endDist = _sectionEndDistBySlot.TryGetValue(slot, out float storedEnd)
                ? storedEnd
                : slot * Mathf.Max(1f, sectionSpacing);

            // Keep the patch alive until the player is fully past its end + grace distance.
            if (endDist < playerDist - despawnBehindDistance)
                _toRemove.Add(slot);
        }

        for (int i = 0; i < _toRemove.Count; i++)
        {
            int slot = _toRemove[i];
            if (_sectionsBySlot.TryGetValue(slot, out var obj) && obj != null)
                Destroy(obj);

            if (_surfaceReservationBySlot.TryGetValue(slot, out int reservationId))
            {
                TrackSurfaceSpawnRegistry.Unregister(reservationId);
                _surfaceReservationBySlot.Remove(slot);
            }

            _sectionsBySlot.Remove(slot);
            _sectionEndDistBySlot.Remove(slot);
        }
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

    private void TrySpawnSectionAtDistance(int slot, float baseDist)
    {
        if (!CanSpawnMoreSections)
            return;

        float length = Mathf.Max(4f, sectionLength + Random.Range(-sectionLengthJitter, sectionLengthJitter));
        if (!TryFindClearSectionStart(baseDist, length, out float startDist, out float endDist))
        {
            if (verboseDebug)
                Debug.Log($"[IcePathSpawner] Skipped ice section slot {slot} near {baseDist:F1}m (no clear road span).");
            return;
        }

        GameObject sectionRoot = new GameObject($"IceSection_{startDist:F0}");
        Transform parent = icePathParent ? icePathParent : transform;
        sectionRoot.transform.SetParent(parent);

        _sectionsBySlot[slot] = sectionRoot;
        _sectionEndDistBySlot[slot] = endDist;
        _spawnedThisRun++;

        GameObject stripGO = new GameObject($"IceRoad_{startDist:F0}");
        int roadLayerIndex = LayerMask.NameToLayer("RoadSurface");
        if (roadLayerIndex >= 0)
            stripGO.layer = roadLayerIndex;
        stripGO.transform.SetParent(sectionRoot.transform, worldPositionStays: false);

        BuildIceSectionMesh(stripGO, startDist, endDist - startDist);

        float centerDist = Mathf.Clamp(startDist + (endDist - startDist) * 0.5f, 0f, _totalLength);
        SampleAlongPath(centerDist, out Vector3 centerPos, out _);
        _queueLastSpawn.Record(centerPos, "Ice Section");
        _surfaceReservationBySlot[slot] = TrackSurfaceSpawnRegistry.Register(startDist, endDist);

        if (verboseDebug)
            Debug.Log($"[IcePathSpawner] Spawned full-width ice section at {startDist:F1}–{endDist:F1}m (slot {slot}).");
    }

    private bool TryFindClearSectionStart(float baseDist, float length, out float startDist, out float endDist)
    {
        startDist = 0f;
        endDist = 0f;

        float spacing = Mathf.Max(1f, sectionSpacing);
        float searchEnd = Mathf.Min(_totalLength, baseDist + spacing);

        // Prefer near the slot, then walk forward looking for a gap between boost/ramp reservations.
        const int maxAttempts = 12;
        for (int attempt = 0; attempt < maxAttempts; attempt++)
        {
            float candidate;
            if (attempt == 0)
                candidate = baseDist;
            else if (attempt < 5)
                candidate = baseDist + Random.Range(-distanceJitter, distanceJitter);
            else
                candidate = Mathf.Lerp(baseDist, searchEnd, (attempt - 4) / 7f);

            candidate = Mathf.Clamp(candidate, 0f, _totalLength);
            float candidateEnd = Mathf.Min(candidate + length, _totalLength);
            if (candidateEnd <= candidate + 2f)
                continue;

            if (!TrackSurfaceSpawnRegistry.Overlaps(candidate, candidateEnd))
            {
                startDist = candidate;
                endDist = candidateEnd;
                return true;
            }
        }

        return false;
    }

    private void BuildIceSectionMesh(GameObject owner, float startDist, float length)
    {
        float endDist = Mathf.Min(startDist + length, _totalLength);
        if (endDist <= startDist + 0.01f) return;

        float roadW = trackGenerator != null ? Mathf.Max(1f, trackGenerator.RoadWidth) : 4f;
        float halfW = roadW * roadWidthScale * 0.5f;

        int samples = Mathf.Max(2, Mathf.CeilToInt((endDist - startDist) / iceSampleSpacing) + 1);

        var verts = new Vector3[samples * 2];
        var normals = new Vector3[samples * 2];
        var uvs = new Vector2[samples * 2];
        var tris = new int[(samples - 1) * 6];

        for (int i = 0; i < samples; i++)
        {
            float dist = Mathf.Lerp(startDist, endDist, i / (float)(samples - 1));
            Vector3 pos = GetPositionAtDistance(dist);

            Vector3 trackFwd = GetTangentAtDistance(dist);
            if (trackFwd.sqrMagnitude < 1e-6f) trackFwd = Vector3.forward;
            trackFwd.Normalize();

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

            // Full road width: left/right from track tangent + surface normal (no wiggles).
            Vector3 right = Vector3.Cross(n, trackFwd);
            if (right.sqrMagnitude < 1e-6f) right = Vector3.Cross(Vector3.up, trackFwd);
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

        Mesh m = new Mesh { name = "IceSectionMesh" };
        m.vertices = verts;
        m.normals = normals;
        m.uv = uvs;
        m.triangles = tris;
        m.RecalculateBounds();

        mf.sharedMesh = m;

        EnsureIceComponents(owner);

        if (addIceMeshColliderTrigger)
            BuildIceTriggerBoxes(owner, verts, samples, halfW * 2f);

        if (iceMaterial != null)
            mr.sharedMaterial = iceMaterial;
    }

    private void EnsureIceComponents(GameObject owner)
    {
        if (_prefabGroundSurface != null)
            CopyComponent(_prefabGroundSurface, owner);
        else
        {
            var gs = owner.GetComponent<GroundSurface>();
            if (gs == null) gs = owner.AddComponent<GroundSurface>();
            gs.surfaceType = SurfaceType.Ice;
        }

        if (_prefabIcePath != null)
            CopyComponent(_prefabIcePath, owner);
        else if (owner.GetComponent<IcePath>() == null)
            owner.AddComponent<IcePath>();

        var surface = owner.GetComponent<GroundSurface>();
        if (surface != null)
            surface.surfaceType = SurfaceType.Ice;
    }

    private void BuildIceTriggerBoxes(GameObject owner, Vector3[] vertsLocal, int samples, float width)
    {
        for (int i = 0; i < samples - 1; i++)
        {
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
            box.size = new Vector3(width, 0.15f, len);
            box.center = new Vector3(0f, box.size.y * -0.5f, 0f);

            EnsureIceComponents(t.gameObject);
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

    private void ClearSections()
    {
        foreach (var kvp in _surfaceReservationBySlot)
            TrackSurfaceSpawnRegistry.Unregister(kvp.Value);

        foreach (var o in _sectionsBySlot.Values)
            if (o) Destroy(o);

        _sectionsBySlot.Clear();
        _surfaceReservationBySlot.Clear();
        _sectionEndDistBySlot.Clear();
        _spawnedThisRun = 0;
    }

    public void SetPlayerTransform(Transform player) => playerTransform = player;

    private static T CopyComponent<T>(T source, GameObject destination) where T : Component
    {
        if (!source || destination == null) return null;

        T copy = destination.GetComponent<T>();
        if (!copy) copy = destination.AddComponent<T>();
        if (!copy) return null;

        CopySerializedFieldsRuntime(source, copy);
        return copy;
    }

    private static void CopySerializedFieldsRuntime(Component src, Component dst)
    {
        if (!src || !dst) return;

        System.Type type = src.GetType();
        if (type != dst.GetType())
            return;

        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        foreach (FieldInfo f in type.GetFields(flags))
        {
            if (f.IsStatic || f.IsInitOnly || f.IsNotSerialized) continue;
            bool isUnitySerialized = f.IsPublic || f.GetCustomAttribute<SerializeField>() != null;
            if (!isUnitySerialized) continue;
            f.SetValue(dst, f.GetValue(src));
        }
    }

    public string SpawnQueueLabel => "Ice Sections";
    public bool IsSpawnQueueReady => _path.Count >= 2 && playerTransform != null && iceMaterial != null;
    public bool HasSpawnQueueCapacity => CanSpawnMoreSections;
    public bool HasPendingSpawnRequest => _queueState.HasPending;
    public bool TrySubmitSpawnRequest() => _queueState.TrySubmit(this);
    public bool TryExecutePendingSpawn() => _queueState.TryExecute(TrySpawnOneAhead);
    public bool TryConsumeLastSpawnReport(out TrackSpawnQueueSpawnReport report) => _queueLastSpawn.TryConsume(out report);
    public void CancelPendingSpawnRequest() => _queueState.Cancel();
    public void SetQueueControlledAutonomous(bool controlled, TrackSpawnerQueue owner = null) => _queueState.Bind(controlled, owner);
}
