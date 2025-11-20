using System;
#if UNITY_EDITOR
using UnityEditor;
#endif
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;

using Debug = UnityEngine.Debug;
using Random = UnityEngine.Random;
using Object = UnityEngine.Object;

[RequireComponent(typeof(Transform))]
public class ProceduralTrackGenerator : MonoBehaviour
{
    // 2D helper (XZ plane)
    private struct Segment2D
    {
        public Vector2 a;
        public Vector2 b;

        public Segment2D(Vector2 a, Vector2 b)
        {
            this.a = a;
            this.b = b;
        }
    }

#if UNITY_EDITOR
    [CustomEditor(typeof(ProceduralTrackGenerator))]
    public class ProceduralTrackGeneratorEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            ProceduralTrackGenerator gen = (ProceduralTrackGenerator)target;

            GUILayout.Space(8);
            GUI.backgroundColor = Color.green;

            if (GUILayout.Button("GENERATE TRACK (Manual Only)", GUILayout.Height(30)))
            {
                EditorApplication.delayCall += () =>
                {
                    if (gen != null)
                        gen.GenerateTrack();
                };
            }

            GUI.backgroundColor = Color.white;
        }
    }
#endif

    // ================================================================
    //  TRACK PARAMETERS
    // ================================================================
    [Header("Track Segment Settings")]
    [SerializeField] private GameObject segmentPrefab;
    [SerializeField] private int segmentCount = 200;
    public int SegmentCount => segmentCount;

    [Header("Segment Length")]
    [SerializeField] private bool autoDetectSegmentLength = true;
    [SerializeField] private float segmentLength = 10f;

    [Header("Turn Tightness (Difficulty)")]
    [SerializeField] private float minTurnAngle = 5f;
    [SerializeField] private float startMaxTurnAngle = 10f;
    [SerializeField] private float endMaxTurnAngle = 40f;
    [SerializeField] private AnimationCurve difficultyCurve = AnimationCurve.Linear(0, 0, 1, 1);

    [Header("Turn Frequency")]
    [Range(0f, 1f)][SerializeField] private float startTurnChance = 0.35f;
    [Range(0f, 1f)][SerializeField] private float endTurnChance = 0.85f;
    [SerializeField] private AnimationCurve turnFrequencyCurve = AnimationCurve.Linear(0, 0, 1, 1);

    [Header("Small Wiggles")]
    [Tooltip("Maximum small wiggle angle added every segment.")]
    [SerializeField] private float maxWiggleAngle = 3f;
    [Tooltip("How much the wiggle grows over distance (0 = constant, 1 = full over distance).")]
    [Range(0f, 1f)][SerializeField] private float wiggleOverDistance = 0.4f;

    [Header("Avoidance (Turn Away From Existing Track)")]
    [SerializeField] private bool useAvoidance = true;
    [Tooltip("How far ahead/around we 'sense' existing track to avoid it.")]
    [SerializeField] private float avoidanceRadius = 40f;
    [Tooltip("Overall strength of avoidance steering (0 = off, 1 = full).")]
    [Range(0f, 2f)][SerializeField] private float avoidanceStrength = 0.7f;
    [Tooltip("Higher = more weight to close segments vs far ones.")]
    [Range(0.1f, 4f)][SerializeField] private float avoidanceFalloff = 1.0f;

    [Header("Global Track Direction")]
    [SerializeField] private bool constrainToGlobalDirection = true;
    [Range(0f, 1f)][SerializeField] private float globalAlignmentStrength = 0.3f;
    [Tooltip("Hard cap on how far away from the starting forward we can aim.")]
    [SerializeField] private float maxHeadingDeviation = 110f;

    [Header("Randomness")]
    [SerializeField] private bool useRandomSeed = true;
    [SerializeField] private int fixedSeed = 12345;

    [Header("Road Mesh")]
    [SerializeField] private bool generateRoadMesh = true;
    [SerializeField] private float roadWidth = 4f;
    [SerializeField] private Material roadMaterial;
    [SerializeField] private float uvTiling = 0.1f;

    [Header("Path Smoothing (Visual Only)")]
    [SerializeField] private bool useSmoothing = false;
    [SerializeField] private int smoothingSubdivisionsPerSegment = 6;

    [Header("Self-Intersection Avoidance")]
    [Tooltip("If true, we strictly avoid overlapping / intersecting track.")]
    [SerializeField] private bool preventSelfIntersections = true;

    [Tooltip("How many yaw samples we try around the preferred yaw to avoid intersections.")]
    [SerializeField] private int yawSearchSamples = 24;

    [Tooltip("Extra padding on top of half the road width when checking for self-collision.")]
    [SerializeField] private float collisionPadding = 0.5f;

    [Tooltip("Multiplier for how much of half roadWidth counts as collision radius.")]
    [SerializeField] private float trackRadiusMultiplier = 1.3f;

    [Tooltip("How many of the most recent segments to ignore in the capsule-distance check (to allow tight consecutive turns).")]
    [SerializeField] private int recentIgnoreCount = 0;   // stricter by default

    // Expose raw path centers
    public List<Vector3> PathPoints => _pathPoints;

    public float RoadWidth => roadWidth;


    // Fired when a full, valid track (no abort) is finished generating (after retries).
    public event Action<ProceduralTrackGenerator> OnTrackGeneratedSuccessfully;

    // ================================================================
    //  INTERNALS
    // ================================================================
    private MeshFilter _meshFilter;
    private MeshRenderer _meshRenderer;
    private MeshCollider _meshCollider;

    private readonly List<Transform> _spawnedSegments = new List<Transform>();
    private readonly List<Vector3> _pathPoints = new List<Vector3>();
    private readonly List<Segment2D> _segments2D = new List<Segment2D>();

    private Vector3 _currentEndPosition;
    private Quaternion _currentRotation;

    private float _currentTurnDirectionSign;
    private bool _hasInitializedHeading = false;
    private Vector3 _globalForwardRef;
    private Vector3 _globalHeadingSmoothed;

    private bool _abortedGeneration = false;

    // For noise-based wiggle
    private float _noiseOffset;

    // Add these fields near the top of the class:
    [HideInInspector] public Vector2 skidMinXZ;
    [HideInInspector] public Vector2 skidInvSizeXZ;

    // ================================================================
    //  RUNTIME QUICK TEST KEYS
    // ================================================================
    private void Update()
    {
        if (!Application.isPlaying) return;

        if (Input.GetKeyDown(KeyCode.G))
        {
            RunMultipleGenerations(1);
        }
        else if (Input.GetKeyDown(KeyCode.H))
        {
            RunMultipleGenerations(5);
        }
        else if (Input.GetKeyDown(KeyCode.J))
        {
            RunMultipleGenerations(10);
        }
    }

    private void RunMultipleGenerations(int iterations)
    {
        for (int i = 0; i < iterations; i++)
        {
            GenerateTrack();
        }

        Debug.Log($"[ProceduralTrackGenerator] Finished {iterations} generation(s).");
    }

    // ================================================================
    //  PUBLIC ENTRY (WITH RETRIES)
    // ================================================================
    public void GenerateTrack(int maxRetries = 5)
    {
        CacheMeshComponents();

        bool success = false;
        int attempt = 0;

        while (!success && attempt <= maxRetries)
        {
            ClearTrackImmediateSafe();
            _abortedGeneration = false;

            success = BuildTrack();

            if (!success)
            {
                attempt++;
                if (attempt <= maxRetries)
                {
                    Debug.LogWarning(
                        $"[ProceduralTrackGenerator] Track generation failed due to self-intersection. Retrying ({attempt}/{maxRetries})...");
                }
                else
                {
                    Debug.LogError(
                        "[ProceduralTrackGenerator] Track generation failed after maximum retries. Keeping last aborted attempt.");
                }
            }
        }

        // If we ended up with a valid track, notify listeners (GameManager, etc.)
        if (success)
        {
            OnTrackGeneratedSuccessfully?.Invoke(this);
        }
    }

    // ================================================================
    //  CORE TRACK GENERATION
    // ================================================================
    private bool BuildTrack()
    {
        if (segmentPrefab == null)
        {
            Debug.LogError("[ProceduralTrackGenerator] No segmentPrefab assigned!");
            return false;
        }

        // Detect length from prefab, and also use prefab X-size as road width hint if needed
        Renderer r = segmentPrefab.GetComponentInChildren<Renderer>();
        if (autoDetectSegmentLength && r != null)
        {
            segmentLength = r.bounds.size.z;
            if (roadWidth <= 0.01f)
                roadWidth = r.bounds.size.x;
        }

        if (useRandomSeed)
        {
            int seed = Random.Range(int.MinValue, int.MaxValue);
            Random.InitState(seed);
            _noiseOffset = seed * 0.001f;
        }
        else
        {
            Random.InitState(fixedSeed);
            _noiseOffset = fixedSeed * 0.001f;
        }

        _pathPoints.Clear();
        _spawnedSegments.Clear();
        _segments2D.Clear();
        _abortedGeneration = false;

        _currentRotation = transform.rotation;
        _currentEndPosition = transform.position;

        _currentTurnDirectionSign = Random.value < 0.5f ? -1f : 1f;

        _hasInitializedHeading = false;
        _globalForwardRef = transform.forward;

        float effLength = Mathf.Max(0.001f, segmentLength);

        for (int i = 0; i < segmentCount && !_abortedGeneration; i++)
        {
            float tNorm = (segmentCount <= 1) ? 0f : (float)i / (segmentCount - 1);

            float maxTurnAngle = Mathf.Lerp(startMaxTurnAngle, endMaxTurnAngle, difficultyCurve.Evaluate(tNorm));
            maxTurnAngle = Mathf.Max(minTurnAngle, maxTurnAngle);

            float turnChance = Mathf.Lerp(startTurnChance, endTurnChance, turnFrequencyCurve.Evaluate(tNorm));

            float preferredYaw = ComputeTurnYawSimple(i, tNorm, maxTurnAngle, turnChance);

            if (constrainToGlobalDirection)
                preferredYaw = ApplyGlobalDirectionBias(preferredYaw, maxTurnAngle);

            float chosenYaw = preferredYaw;

            if (preventSelfIntersections)
            {
                if (!TryFindCollisionFreeYaw(preferredYaw, maxTurnAngle, effLength, out chosenYaw))
                {
                    Debug.LogWarning(
                        "[ProceduralTrackGenerator] Aborting generation attempt: no valid non-intersecting segment could be found.");
                    _abortedGeneration = true;
                    break;
                }
            }

            CommitSegment(chosenYaw, effLength);
        }

        if (!_abortedGeneration && generateRoadMesh)
        {
            BuildRoadMeshFromPath();
        }
        else if (!generateRoadMesh)
        {
            if (_meshFilter != null) _meshFilter.sharedMesh = null;
            if (_meshCollider != null) _meshCollider.sharedMesh = null;
        }

        return !_abortedGeneration && _pathPoints.Count > 1;
    }

    private void CommitSegment(float yawDeg, float length)
    {
        Quaternion segmentRot = _currentRotation * Quaternion.Euler(0f, yawDeg, 0f);
        Vector3 forward3D = segmentRot * Vector3.forward;

        Vector3 segmentStart = _currentEndPosition;
        Vector3 segmentEnd = _currentEndPosition + forward3D * length;
        Vector3 centerPos = (segmentStart + segmentEnd) * 0.5f;

        GameObject seg = Object.Instantiate(segmentPrefab, centerPos, segmentRot, transform);
        _spawnedSegments.Add(seg.transform);

        seg.layer = LayerMask.NameToLayer("Road");

        _pathPoints.Add(centerPos);

        Vector2 a2d = new Vector2(segmentStart.x, segmentStart.z);
        Vector2 b2d = new Vector2(segmentEnd.x, segmentEnd.z);
        _segments2D.Add(new Segment2D(a2d, b2d));

        _currentEndPosition = segmentEnd;
        _currentRotation = segmentRot;
    }

    // ================================================================
    //  TURN YAW CALCULATION (SIMPLE + WIGGLES + AVOIDANCE)
    // ================================================================
    private float ComputeTurnYawSimple(int index, float tNorm, float maxTurnAngle, float turnChance)
    {
        float wiggleScale = Mathf.Lerp(1f, 1f + wiggleOverDistance, tNorm);
        float noiseT = _noiseOffset + index * 0.15f;
        float noise = Mathf.PerlinNoise(noiseT, 0.1234f) * 2f - 1f;
        float wiggle = noise * maxWiggleAngle * wiggleScale;

        float avoidanceBias = ComputeAvoidanceYawBias(maxTurnAngle);

        float yaw = wiggle + avoidanceBias;

        if (Random.value < turnChance)
        {
            float sign = Random.value < 0.5f ? -1f : 1f;
            float bigTurn = Random.Range(minTurnAngle, maxTurnAngle);
            yaw += sign * bigTurn;
            _currentTurnDirectionSign = sign;
        }

        return Mathf.Clamp(yaw, -maxTurnAngle, maxTurnAngle);
    }

    // ================================================================
    //  AVOIDANCE LOGIC
    // ================================================================
    private float ComputeAvoidanceYawBias(float maxTurnAngle)
    {
        if (!useAvoidance || _segments2D.Count == 0 || avoidanceRadius <= 0.01f)
            return 0f;

        Vector2 pos = new Vector2(_currentEndPosition.x, _currentEndPosition.z);
        Vector3 forward3D = _currentRotation * Vector3.forward;
        Vector2 forward2D = new Vector2(forward3D.x, forward3D.z).normalized;

        Vector2 repulsion = Vector2.zero;

        int count = _segments2D.Count;
        for (int i = 0; i < count; i++)
        {
            Segment2D s = _segments2D[i];

            Vector2 closest = ClosestPointOnSegment2D(pos, s.a, s.b);
            Vector2 toMe = pos - closest;
            float dist = toMe.magnitude;

            if (dist < 1e-3f || dist > avoidanceRadius)
                continue;

            float norm = 1f - (dist / avoidanceRadius);
            float weight = Mathf.Pow(norm, avoidanceFalloff);

            repulsion += toMe.normalized * weight;
        }

        if (repulsion.sqrMagnitude < 1e-6f)
            return 0f;

        repulsion.Normalize();

        float signedAngle = SignedAngle2D(forward2D, repulsion);
        float bias = signedAngle * avoidanceStrength;

        return Mathf.Clamp(bias, -maxTurnAngle, maxTurnAngle);
    }

    private Vector2 ClosestPointOnSegment2D(Vector2 p, Vector2 a, Vector2 b)
    {
        Vector2 ab = b - a;
        float abSqr = ab.sqrMagnitude;
        if (abSqr < 1e-6f) return a;
        float t = Vector2.Dot(p - a, ab) / abSqr;
        t = Mathf.Clamp01(t);
        return a + ab * t;
    }

    private float SignedAngle2D(Vector2 from, Vector2 to)
    {
        float cross = from.x * to.y - from.y * to.x;
        float dot = Vector2.Dot(from, to);
        float angle = Mathf.Atan2(cross, dot) * Mathf.Rad2Deg;
        return angle;
    }

    // ================================================================
    //  GLOBAL DIRECTION BIASING
    // ================================================================
    private float ApplyGlobalDirectionBias(float yaw, float maxTurnAngle)
    {
        Vector3 forward = (_currentRotation * Vector3.forward).normalized;

        if (!_hasInitializedHeading)
        {
            _globalHeadingSmoothed = forward;
            _hasInitializedHeading = true;
        }

        _globalHeadingSmoothed = Vector3.Slerp(_globalHeadingSmoothed, forward, 0.1f);
        _globalHeadingSmoothed.Normalize();

        float deviation = Vector3.SignedAngle(_globalHeadingSmoothed, _globalForwardRef, Vector3.up);

        if (Mathf.Abs(deviation) > maxHeadingDeviation)
        {
            float correctionSign = deviation > 0 ? -1f : 1f;
            float correction = correctionSign * globalAlignmentStrength * 2.0f;
            yaw += correction;
        }

        yaw = Mathf.Clamp(yaw, -maxTurnAngle, maxTurnAngle);
        return yaw;
    }

    // ================================================================
    //  COLLISION-FREE YAW SEARCH
    // ================================================================
    private bool TryFindCollisionFreeYaw(float preferredYawDeg, float maxTurnAngle, float segLength, out float resultYaw)
    {
        preferredYawDeg = Mathf.Clamp(preferredYawDeg, -maxTurnAngle, maxTurnAngle);

        if (_segments2D.Count == 0)
        {
            resultYaw = preferredYawDeg;
            return true;
        }

        if (IsYawValid(preferredYawDeg, segLength))
        {
            resultYaw = preferredYawDeg;
            return true;
        }

        int samples = Mathf.Max(1, yawSearchSamples);
        float step = maxTurnAngle / samples;

        for (int i = 1; i <= samples; i++)
        {
            float delta = step * i;

            float yawPlus = Mathf.Clamp(preferredYawDeg + delta, -maxTurnAngle, maxTurnAngle);
            if (IsYawValid(yawPlus, segLength))
            {
                resultYaw = yawPlus;
                return true;
            }

            float yawMinus = Mathf.Clamp(preferredYawDeg - delta, -maxTurnAngle, maxTurnAngle);
            if (IsYawValid(yawMinus, segLength))
            {
                resultYaw = yawMinus;
                return true;
            }
        }

        resultYaw = preferredYawDeg;
        return false;
    }

    private bool IsYawValid(float yawDeg, float segLength)
    {
        if (constrainToGlobalDirection && maxHeadingDeviation < 179f)
        {
            Quaternion newRot = _currentRotation * Quaternion.Euler(0f, yawDeg, 0f);
            Vector3 newForward = newRot * Vector3.forward;
            float deviation = Vector3.SignedAngle(newForward, _globalForwardRef, Vector3.up);
            if (Mathf.Abs(deviation) > maxHeadingDeviation)
                return false;
        }

        if (!preventSelfIntersections)
            return true;

        Quaternion testRot = _currentRotation * Quaternion.Euler(0f, yawDeg, 0f);
        Vector3 forward3D = testRot * Vector3.forward;

        Vector3 start3D = _currentEndPosition;
        Vector3 end3D = _currentEndPosition + forward3D * segLength;

        Vector2 a = new Vector2(start3D.x, start3D.z);
        Vector2 b = new Vector2(end3D.x, end3D.z);

        float halfWidth = roadWidth * 0.5f * trackRadiusMultiplier;
        float capsuleRadius = halfWidth + collisionPadding;
        float capsuleRadiusSq = capsuleRadius * capsuleRadius;

        int count = _segments2D.Count;
        int maxIndex = Mathf.Max(0, count - recentIgnoreCount);

        for (int i = 0; i < count; i++)
        {
            if (i >= maxIndex)
                continue;

            Segment2D s = _segments2D[i];

            if (SegmentsProperlyIntersect(a, b, s.a, s.b))
                return false;

            float distSq = SegmentSegmentDistanceSq(a, b, s.a, s.b);
            if (distSq < capsuleRadiusSq)
                return false;
        }

        return true;
    }

    // ================================================================
    //  SIMPLE 2D GEOMETRY HELPERS
    // ================================================================
    private float SegmentSegmentDistanceSq(Vector2 p1, Vector2 q1, Vector2 p2, Vector2 q2)
    {
        Vector2 u = q1 - p1;
        Vector2 v = q2 - p2;
        Vector2 w = p1 - p2;

        float a = Vector2.Dot(u, u);
        float b = Vector2.Dot(u, v);
        float c = Vector2.Dot(u, w);
        float d = Vector2.Dot(v, v);
        float e = Vector2.Dot(v, w);

        const float EPS = 1e-6f;

        float D = a * d - b * b;
        float sc, sN, sD = D;
        float tc, tN, tD = D;

        if (D < EPS)
        {
            sN = 0.0f; sD = 1.0f;
            tN = e; tD = d;
        }
        else
        {
            sN = (b * e - c * d);
            tN = (a * e - b * c);
            if (sN < 0.0f)
            {
                sN = 0.0f;
                tN = e;
                tD = d;
            }
            else if (sN > sD)
            {
                sN = sD;
                tN = e + b;
                tD = d;
            }
        }

        if (tN < 0.0f)
        {
            tN = 0.0f;
            if (-c < 0.0f) sN = 0.0f;
            else if (-c > a) sN = sD;
            else
            {
                sN = -c;
                sD = a;
            }
        }
        else if (tN > tD)
        {
            tN = tD;
            if ((-c + b) < 0.0f) sN = 0;
            else if ((-c + b) > a) sN = sD;
            else
            {
                sN = (-c + b);
                sD = a;
            }
        }

        sc = (Mathf.Abs(sN) < EPS ? 0.0f : sN / sD);
        tc = (Mathf.Abs(tN) < EPS ? 0.0f : tN / tD);

        Vector2 dP = w + (u * sc) - (v * tc);
        return dP.sqrMagnitude;
    }

    private int Orientation(Vector2 a, Vector2 b, Vector2 c)
    {
        float val = (b.x - a.x) * (c.y - a.y) - (b.y - a.y) * (c.x - a.x);
        const float eps = 1e-6f;
        if (val > eps) return 1;   // CCW
        if (val < -eps) return -1; // CW
        return 0;                  // collinear
    }

    private bool OnSegment(Vector2 a, Vector2 b, Vector2 p)
    {
        return p.x >= Mathf.Min(a.x, b.x) - 1e-6f &&
               p.x <= Mathf.Max(a.x, b.x) + 1e-6f &&
               p.y >= Mathf.Min(a.y, b.y) - 1e-6f &&
               p.y <= Mathf.Max(a.y, b.y) + 1e-6f;
    }

    private bool SegmentsProperlyIntersect(Vector2 p1, Vector2 p2, Vector2 p3, Vector2 p4)
    {
        int o1 = Orientation(p1, p2, p3);
        int o2 = Orientation(p1, p2, p4);
        int o3 = Orientation(p3, p4, p1);
        int o4 = Orientation(p3, p4, p2);

        if (o1 != o2 && o3 != o4)
            return true;

        if (o1 == 0 && OnSegment(p1, p2, p3)) return true;
        if (o2 == 0 && OnSegment(p1, p2, p4)) return true;
        if (o3 == 0 && OnSegment(p3, p4, p1)) return true;
        if (o4 == 0 && OnSegment(p3, p4, p2)) return true;

        return false;
    }

    // ================================================================
    //  CLEAR HELPERS
    // ================================================================
    private void ClearTrackImmediateSafe()
    {
#if UNITY_EDITOR
        if (!Application.isPlaying)
        {
            EditorApplication.delayCall += () =>
            {
                foreach (var seg in _spawnedSegments)
                    if (seg != null)
                        Object.DestroyImmediate(seg.gameObject);

                foreach (Transform child in transform)
                    if (child != null)
                        Object.DestroyImmediate(child.gameObject);

                _spawnedSegments.Clear();

                if (_meshFilter != null && _meshFilter.sharedMesh != null)
                    DestroyImmediate(_meshFilter.sharedMesh);

                if (_meshCollider != null)
                    _meshCollider.sharedMesh = null;

                _pathPoints.Clear();
                _segments2D.Clear();
            };
            return;
        }
#endif
        foreach (Transform child in transform)
            Destroy(child.gameObject);

        _spawnedSegments.Clear();

        if (_meshFilter != null && _meshFilter.sharedMesh != null)
            Destroy(_meshFilter.sharedMesh);

        if (_meshCollider != null)
            _meshCollider.sharedMesh = null;

        _pathPoints.Clear();
        _segments2D.Clear();
    }

    // ================================================================
    //  ROAD MESH GENERATION + COLLIDER
    // ================================================================
    private void CacheMeshComponents()
    {
        _meshFilter = GetComponent<MeshFilter>();
        if (!_meshFilter) _meshFilter = gameObject.AddComponent<MeshFilter>();

        _meshRenderer = GetComponent<MeshRenderer>();
        if (!_meshRenderer) _meshRenderer = gameObject.AddComponent<MeshRenderer>();

        _meshCollider = GetComponent<MeshCollider>();
        if (!_meshCollider) _meshCollider = gameObject.AddComponent<MeshCollider>();

        if (roadMaterial != null)
            _meshRenderer.sharedMaterial = roadMaterial;

        _meshCollider.convex = false;
    }

    private void BuildRoadMeshFromPath()
    {
        if (_pathPoints.Count < 2) return;

        List<Vector3> path = useSmoothing
            ? GenerateSmoothedPath(_pathPoints, smoothingSubdivisionsPerSegment)
            : _pathPoints;

        if (path.Count < 2) return;

        int count = path.Count;

        // Compute cumulative distance so we can normalize 0..1 over entire track
        float[] cumulative = new float[count];
        float totalLength = 0f;
        cumulative[0] = 0f;
        for (int i = 1; i < count; i++)
        {
            totalLength += Vector3.Distance(path[i], path[i - 1]);
            cumulative[i] = totalLength;
        }

        Vector3[] verts = new Vector3[count * 2];
        Vector3[] normals = new Vector3[count * 2];
        Vector2[] uvs = new Vector2[count * 2];
        Vector2[] uv2s = new Vector2[count * 2];
        int[] tris = new int[(count - 1) * 6];

        float halfWidth = roadWidth * 0.5f;
        float length = 0f;

        for (int i = 0; i < count; i++)
        {
            Vector3 pos = path[i];
            Vector3 forward =
                (i == count - 1) ? (pos - path[i - 1]).normalized :
                                   (path[i + 1] - pos).normalized;

            Vector3 right = Vector3.Cross(Vector3.up, forward).normalized;
            Vector3 leftPos = pos - right * halfWidth;
            Vector3 rightPos = pos + right * halfWidth;

            int L = i * 2;
            int R = i * 2 + 1;

            verts[L] = leftPos;
            verts[R] = rightPos;

            normals[L] = normals[R] = Vector3.up;

            if (i > 0)
                length += Vector3.Distance(path[i], path[i - 1]);

            // UV0: regular tiling along the road
            float v = length * uvTiling;
            uvs[L] = new Vector2(0, v);
            uvs[R] = new Vector2(1, v);

            // UV2: 0..1 over ENTIRE track length (this is what skids use)
            float t = (totalLength > 0f) ? (cumulative[i] / totalLength) : 0f;
            uv2s[L] = new Vector2(0, t);
            uv2s[R] = new Vector2(1, t);
        }

        int ti = 0;
        for (int i = 0; i < count - 1; i++)
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

        Mesh m = new Mesh
        {
            name = "ProceduralRoadMesh",
            vertices = verts,
            normals = normals,
            uv = uvs,
            uv2 = uv2s,
            triangles = tris
        };

        _meshFilter.sharedMesh = m;
        if (_meshCollider != null)
            _meshCollider.sharedMesh = m;
    }

    // ================================================================
    //  PATH SMOOTHING
    // ================================================================
    private List<Vector3> GenerateSmoothedPath(List<Vector3> raw, int subdivisions)
    {
        var res = new List<Vector3>();
        int n = raw.Count;

        res.Add(raw[0]);

        for (int i = 0; i < n - 1; i++)
        {
            Vector3 p0 = raw[Mathf.Max(i - 1, 0)];
            Vector3 p1 = raw[i];
            Vector3 p2 = raw[i + 1];
            Vector3 p3 = raw[Mathf.Min(i + 2, n - 1)];

            for (int s = 1; s <= subdivisions; s++)
            {
                float t = s / (float)subdivisions;
                res.Add(CatmullRom(p0, p1, p2, p3, t));
            }
        }

        return res;
    }

    private Vector3 CatmullRom(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t)
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

    // ================================================================
    //  START POINT FOR CAR SPAWN
    // ================================================================
    public void GetStartPoint(out Vector3 pos, out Vector3 forward)
    {
        List<Vector3> pts = useSmoothing
            ? GenerateSmoothedPath(_pathPoints, smoothingSubdivisionsPerSegment)
            : _pathPoints;

        if (pts.Count < 2)
        {
            pos = transform.position;
            forward = transform.forward;
            return;
        }

        pos = pts[0];
        forward = (pts[1] - pts[0]).normalized;
    }

    // ================================================================
    //  GIZMOS
    // ================================================================
    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.yellow;
        for (int i = 0; i < _pathPoints.Count - 1; i++)
            Gizmos.DrawLine(_pathPoints[i], _pathPoints[i + 1]);
    }
}
