using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Spawns procedurally-generated ice path strips along the track.
/// Each ice path is a curved mesh that follows the track with lateral offset variations.
/// Independent from obstacle/coin spawners to allow layering underneath other hazards.
/// </summary>
public class TrackIcePathSpawner : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private ProceduralTrackGenerator trackGenerator;
    [SerializeField] private Transform playerTransform;
    [SerializeField] private Transform icePathParent;

    [Header("Ice Path Length")]
    [Tooltip("Minimum length of each ice path (meters).")]
    [SerializeField] private float minIcePathLength = 15f;

    [Tooltip("Maximum length of each ice path (meters).")]
    [SerializeField] private float maxIcePathLength = 50f;

    [Header("Ice Path Width")]
    [Tooltip("Width of the ice strip (should be <= track width).")]
    [SerializeField] private float icePathWidth = 3f;

    [Header("Spawn Settings")]
    [Tooltip("Spacing between potential ice path spawn slots (meters).")]
    [SerializeField] private float icePathSpacing = 80f;

    [Tooltip("Probability of spawning ice at each slot (0-1).")]
    [SerializeField, Range(0f, 1f)] private float spawnChance = 0.3f;

    [Tooltip("Difficulty curve adjusts spawn chance over track distance.")]
    [SerializeField] private AnimationCurve spawnChanceByDistance = AnimationCurve.Linear(0, 0.3f, 1, 0.7f);

    [Header("Streaming Settings")]
    [SerializeField] private int maxActiveIcePaths = 15;
    [SerializeField] private float minSpawnDistanceAhead = 60f;
    [SerializeField] private float maxSpawnDistanceAhead = 180f;
    [SerializeField] private float despawnBehindDistance = 30f;
    [SerializeField] private float initialPreSpawnDistance = 100f;

    [Header("Lateral Offset & Sway")]
    [Tooltip("Base lateral offset from track center as fraction of half-width.")]
    [SerializeField, Range(-1f, 1f)] private float baseLateralOffsetFraction = 0f;

    [Tooltip("Maximum lateral sway amplitude (meters) during ice path.")]
    [SerializeField] private float maxLateralSway = 1.5f;

    [Tooltip("Frequency of lateral sway along path (higher = more wiggly).")]
    [SerializeField] private float swayFrequency = 0.08f;

    [Tooltip("Curve to modulate sway intensity (x = 0-1 along ice path).")]
    [SerializeField] private AnimationCurve swayIntensityCurve = AnimationCurve.EaseInOut(0, 0, 1, 1);

    [Header("Mesh Generation")]
    [Tooltip("Segments per meter of ice path (higher = smoother curves).")]
    [SerializeField] private float segmentsPerMeter = 2f;

    [Tooltip("Height offset above road surface.")]
    [SerializeField] private float heightOffset = 0.02f;

    [SerializeField] private Material iceMaterial;

    [Tooltip("UV tiling along path length.")]
    [SerializeField] private float uvTiling = 0.2f;

    [Header("Physics & Components")]
    [SerializeField] private GroundSurface iceGroundSurfaceTemplate;
    [SerializeField] private LayerMask roadLayer;

    [Header("Accelerator Settings")]
    [SerializeField] private float boostAccumulationRate = 2.5f;
    [SerializeField] private float maxSpeedBoost = 15f;
    [SerializeField] private float boostDecayDelay = 0.5f;
    [SerializeField] private float boostDecayRate = 8f;

    [Header("Path Sampling")]
    [SerializeField] private bool useSmoothing = true;
    [SerializeField, Min(1)] private int smoothingSubdivisionsPerSegment = 6;

    [Header("Update")]
    [SerializeField] private float updateInterval = 0.5f;

    [Header("Debug")]
    [SerializeField] private bool verboseDebug = false;

    // Internal
    private readonly List<Vector3> _path = new();
    private float[] _cumLengths;
    private float _totalLength;
    private int _maxSlotIndex;

    private readonly Dictionary<int, GameObject> _icePathsBySlot = new();
    private readonly List<int> _toRemove = new();
    private float _updateTimer;
    private int _lastClosestSegmentIndex = 0;

    private void Update()
    {
        if (_path.Count < 2 || playerTransform == null) return;

        _updateTimer += Time.deltaTime;
        if (_updateTimer >= updateInterval)
        {
            _updateTimer = 0f;
            StreamIcePaths();
        }
    }

    public void InitializeForRun(ProceduralTrackGenerator generator, Transform player)
    {
        trackGenerator = generator;
        playerTransform = player;

        if (trackGenerator == null || playerTransform == null)
        {
            Debug.LogError("[TrackIcePathSpawner] Missing references in InitializeForRun.");
            return;
        }

        // Ensure we have a valid scene-based parent (not a prefab asset)
        if (icePathParent == null || !icePathParent.gameObject.scene.isLoaded)
        {
            // Create a runtime container in the scene
            GameObject parentObj = new GameObject("IcePathContainer");
            parentObj.transform.SetParent(transform);
            parentObj.transform.localPosition = Vector3.zero;
            icePathParent = parentObj.transform;

            if (verboseDebug)
                Debug.Log("[TrackIcePathSpawner] Created scene-based IcePathContainer.");
        }

        // Try to get template from assigned parent if available
        if (iceGroundSurfaceTemplate == null && icePathParent != null)
        {
            iceGroundSurfaceTemplate = icePathParent.GetComponent<GroundSurface>();
        }

        RebuildPath();
        ClearAllIcePaths();
        SetupSlots();
        PreSpawnInitialIcePaths();
        _updateTimer = 0f;

        if (verboseDebug)
            Debug.Log($"[TrackIcePathSpawner] Initialized. Total path length: {_totalLength:F1}m");
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
        if (_totalLength <= 0f || icePathSpacing <= 0f)
        {
            _maxSlotIndex = 0;
            return;
        }
        _maxSlotIndex = Mathf.FloorToInt(_totalLength / icePathSpacing);
    }

    private void PreSpawnInitialIcePaths()
    {
        if (_totalLength <= 0f || _maxSlotIndex <= 0) return;

        float endDist = Mathf.Clamp(initialPreSpawnDistance, 0f, _totalLength);
        int endSlot = Mathf.FloorToInt(endDist / icePathSpacing);

        for (int slot = 0; slot <= endSlot; slot++)
        {
            if (_icePathsBySlot.Count >= maxActiveIcePaths) break;
            float dist = slot * icePathSpacing;
            TrySpawnIcePathAtDistance(slot, dist);
        }

        if (verboseDebug)
            Debug.Log($"[TrackIcePathSpawner] Pre-spawned {_icePathsBySlot.Count} ice paths.");
    }

    private void StreamIcePaths()
    {
        if (_totalLength <= 0f || _maxSlotIndex <= 0) return;

        float playerDist = GetPlayerDistanceAlongTrack();

        // Despawn behind player
        float despawnStart = Mathf.Clamp(playerDist - despawnBehindDistance, 0f, _totalLength);
        _toRemove.Clear();
        foreach (var kvp in _icePathsBySlot)
        {
            float slotDist = kvp.Key * icePathSpacing;
            if (slotDist < despawnStart)
            {
                if (kvp.Value) Destroy(kvp.Value);
                _toRemove.Add(kvp.Key);
            }
        }
        for (int i = 0; i < _toRemove.Count; i++)
            _icePathsBySlot.Remove(_toRemove[i]);

        // Spawn ahead
        float spanStart = Mathf.Clamp(playerDist + minSpawnDistanceAhead, 0f, _totalLength);
        float spanEnd = Mathf.Clamp(playerDist + maxSpawnDistanceAhead, 0f, _totalLength);

        int startSlot = Mathf.Clamp(Mathf.FloorToInt(spanStart / icePathSpacing), 0, _maxSlotIndex);
        int endSlot = Mathf.Clamp(Mathf.FloorToInt(spanEnd / icePathSpacing), 0, _maxSlotIndex);

        for (int slot = startSlot; slot <= endSlot; slot++)
        {
            if (_icePathsBySlot.ContainsKey(slot)) continue;
            if (_icePathsBySlot.Count >= maxActiveIcePaths) break;

            float dist = slot * icePathSpacing;
            TrySpawnIcePathAtDistance(slot, dist);
        }
    }

    private void TrySpawnIcePathAtDistance(int slotIndex, float distanceAlongTrack)
    {
        float norm = _totalLength > 0f ? distanceAlongTrack / _totalLength : 0f;
        float chance = spawnChance;

        if (spawnChanceByDistance != null)
            chance *= Mathf.Clamp01(spawnChanceByDistance.Evaluate(norm));

        if (Random.value > chance)
            return;

        // Random ice path length
        float pathLength = Random.Range(minIcePathLength, maxIcePathLength);
        pathLength = Mathf.Min(pathLength, _totalLength - distanceAlongTrack);

        if (pathLength < minIcePathLength * 0.5f)
            return; // Too short, skip

        GameObject icePath = GenerateIcePathMesh(distanceAlongTrack, pathLength, slotIndex);
        if (icePath != null)
        {
            _icePathsBySlot[slotIndex] = icePath;

            if (verboseDebug)
                Debug.Log($"[TrackIcePathSpawner] Spawned ice path at slot {slotIndex}, distance {distanceAlongTrack:F1}m, length {pathLength:F1}m");
        }
    }

    private GameObject GenerateIcePathMesh(float startDist, float length, int slotIndex)
    {
        int segmentCount = Mathf.Max(2, Mathf.CeilToInt(length * segmentsPerMeter));

        List<Vector3> centerLineWorld = new();
        List<Vector3> leftEdgeWorld = new();
        List<Vector3> rightEdgeWorld = new();

        float halfWidth = icePathWidth * 0.5f;
        float trackHalfWidth = trackGenerator != null ? trackGenerator.RoadWidth * 0.5f : 2f;

        // Per-path noise seed so each strip wiggles differently
        float noiseSeed = slotIndex * 123.456f;

        for (int i = 0; i <= segmentCount; i++)
        {
            float t = i / (float)segmentCount;
            float currentDist = startDist + length * t;

            if (currentDist > _totalLength)
                break;

            SampleAlongPath(currentDist, out Vector3 centerPos, out Vector3 forward);

            // Lateral offset with Perlin noise sway
            float baseOffset = baseLateralOffsetFraction * trackHalfWidth;

            float noiseInput = noiseSeed + t * swayFrequency * 10f;
            float noiseValue = Mathf.PerlinNoise(noiseInput, 0.5f) * 2f - 1f; // -1 to 1

            float swayIntensity = swayIntensityCurve != null ? swayIntensityCurve.Evaluate(t) : 1f;
            float lateralSway = noiseValue * maxLateralSway * swayIntensity;

            float totalOffset = baseOffset + lateralSway;
            totalOffset = Mathf.Clamp(totalOffset, -trackHalfWidth + halfWidth, trackHalfWidth - halfWidth);

            Vector3 flatForward = forward;
            flatForward.y = 0f;
            if (flatForward.sqrMagnitude < 1e-6f)
                flatForward = Vector3.forward;
            flatForward.Normalize();

            Vector3 right = Vector3.Cross(Vector3.up, flatForward).normalized;
            Vector3 offsetCenter = centerPos + right * totalOffset;

            // Raycast to get proper height on road
            Vector3 rayOrigin = offsetCenter + Vector3.up * 10f;
            if (Physics.Raycast(rayOrigin, Vector3.down, out RaycastHit hit, 20f, roadLayer, QueryTriggerInteraction.Ignore))
            {
                offsetCenter = hit.point + hit.normal * heightOffset;
            }

            centerLineWorld.Add(offsetCenter);
            leftEdgeWorld.Add(offsetCenter - right * halfWidth);
            rightEdgeWorld.Add(offsetCenter + right * halfWidth);
        }

        if (centerLineWorld.Count < 2)
            return null;

        // Choose origin as first center point
        Vector3 origin = centerLineWorld[0];

        // Build local-space arrays relative to origin
        List<Vector3> centerLineLocal = new(centerLineWorld.Count);
        List<Vector3> leftEdgeLocal = new(leftEdgeWorld.Count);
        List<Vector3> rightEdgeLocal = new(rightEdgeWorld.Count);

        for (int i = 0; i < centerLineWorld.Count; i++)
        {
            centerLineLocal.Add(centerLineWorld[i] - origin);
            leftEdgeLocal.Add(leftEdgeWorld[i] - origin);
            rightEdgeLocal.Add(rightEdgeWorld[i] - origin);
        }

        // Build mesh in local space
        Mesh mesh = BuildIcePathMesh(leftEdgeLocal, rightEdgeLocal, centerLineLocal);
        if (mesh == null)
            return null;

        // Create GameObject at origin in world space
        GameObject icePathObj = new GameObject($"IcePath_Slot{slotIndex}");
        icePathObj.transform.SetParent(icePathParent != null ? icePathParent : transform);
        icePathObj.transform.position = origin;
        icePathObj.layer = LayerMask.NameToLayer("Road");

        // Mesh components
        var mf = icePathObj.AddComponent<MeshFilter>();
        mf.sharedMesh = mesh;

        var mr = icePathObj.AddComponent<MeshRenderer>();
        mr.sharedMaterial = iceMaterial;

        // *** MeshCollider instead of BoxCollider ***
        var mc = icePathObj.AddComponent<MeshCollider>();
        mc.sharedMesh = mesh;

        // Add GroundSurface based on your template
        GroundSurface gs = null;
        if (iceGroundSurfaceTemplate != null)
        {
            gs = icePathObj.AddComponent<GroundSurface>();
            CopyGroundSurfaceSettings(iceGroundSurfaceTemplate, gs);
        }

        // Add IcePathAccelerator – will read GroundSurface and boost max speed
        IcePathAccelerator accelerator = icePathObj.AddComponent<IcePathAccelerator>();
        accelerator.SetBoostParameters(
            boostAccumulationRate,
            maxSpeedBoost,
            boostDecayDelay,
            boostDecayRate
        );

        return icePathObj;
    }




    private Mesh BuildIcePathMesh(List<Vector3> leftEdge, List<Vector3> rightEdge, List<Vector3> centerLine)
    {
        if (leftEdge.Count != rightEdge.Count || leftEdge.Count < 2)
            return null;

        int pointCount = leftEdge.Count;
        Vector3[] vertices = new Vector3[pointCount * 2];
        Vector3[] normals = new Vector3[pointCount * 2];
        Vector2[] uvs = new Vector2[pointCount * 2];
        int[] triangles = new int[(pointCount - 1) * 6];

        float length = 0f;
        for (int i = 0; i < pointCount; i++)
        {
            if (i > 0)
                length += Vector3.Distance(centerLine[i], centerLine[i - 1]);

            vertices[i * 2] = leftEdge[i];
            vertices[i * 2 + 1] = rightEdge[i];

            normals[i * 2] = Vector3.up;
            normals[i * 2 + 1] = Vector3.up;

            float v = length * uvTiling;
            uvs[i * 2] = new Vector2(0f, v);
            uvs[i * 2 + 1] = new Vector2(1f, v);
        }

        int triIndex = 0;
        for (int i = 0; i < pointCount - 1; i++)
        {
            int i0 = i * 2;
            int i1 = i * 2 + 1;
            int i2 = (i + 1) * 2;
            int i3 = (i + 1) * 2 + 1;

            triangles[triIndex++] = i0;
            triangles[triIndex++] = i2;
            triangles[triIndex++] = i1;

            triangles[triIndex++] = i2;
            triangles[triIndex++] = i3;
            triangles[triIndex++] = i1;
        }

        Mesh mesh = new Mesh
        {
            name = "IcePathMesh",
            vertices = vertices,
            normals = normals,
            uv = uvs,
            triangles = triangles
        };

        return mesh;
    }

    private void CopyGroundSurfaceSettings(GroundSurface source, GroundSurface target)
    {
        // Use reflection or serialization to copy settings
        // For simplicity, manually copy common fields (adjust based on your GroundSurface implementation)
        // Example:
        // target.speedMultiplier = source.speedMultiplier;
        // target.dragMultiplier = source.dragMultiplier;
        // target.turnMultiplier = source.turnMultiplier;

        // If GroundSurface is simple enough, you could use JSON serialization:
        string json = JsonUtility.ToJson(source);
        JsonUtility.FromJsonOverwrite(json, target);
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

    private void ClearAllIcePaths()
    {
        foreach (var kvp in _icePathsBySlot)
            if (kvp.Value) Destroy(kvp.Value);
        _icePathsBySlot.Clear();
    }
}