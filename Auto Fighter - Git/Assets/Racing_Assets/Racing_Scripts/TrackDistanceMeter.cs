using System.Collections.Generic;
using UnityEngine;
using TMPro;

[DisallowMultipleComponent]
public class TrackDistanceMeter : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private ProceduralTrackGenerator trackGenerator;
    [SerializeField] public Transform car;

    public enum DisplayMode { WorldDistance, SegmentIndex }
    [Header("Display Mode")]
    [SerializeField] private DisplayMode displayMode = DisplayMode.WorldDistance;

    [Header("Readout (optional)")]
    [SerializeField] private TMP_Text readout;

    [Header("Follow Settings")]
    [SerializeField] private Vector3 worldOffset = new Vector3(0f, 1.6f, 0f);
    [SerializeField] private Vector2 screenOffset = new Vector2(0f, 20f);
    [SerializeField] private bool billboardWorldText = true;

    [Header("Track Sampling")]
    [SerializeField] private bool useSmoothing = false;
    [SerializeField, Min(1)] private int smoothingSubdivisionsPerSegment = 6;

    [Header("Formatting")]
    [SerializeField] private string units = "m";
    [SerializeField] private bool showPercent = true;

    private readonly List<Vector3> _path = new();
    private float[] _cumLengths;
    private float _totalLength;

    public float DistanceAlongTrack { get; private set; }
    public float Normalized => _totalLength > 0f ? DistanceAlongTrack / _totalLength : 0f;

    private Camera _cam;
    private Transform _readoutTransform;
    private Canvas _readoutCanvas;

    public void BindCar(Transform t) => car = t;

    // NEW: allow runtime rebind of generator and car
    public void Configure(ProceduralTrackGenerator gen, Transform carTransform)
    {
        // Unsubscribe old
        if (trackGenerator != null)
            trackGenerator.OnTrackGeneratedSuccessfully -= HandleTrackGenerated;

        trackGenerator = gen;

        if (trackGenerator != null && isActiveAndEnabled)
            trackGenerator.OnTrackGeneratedSuccessfully += HandleTrackGenerated;

        BindCar(carTransform);
        RebuildPath();
    }

    void Awake()
    {
        _cam = Camera.main;
        if (readout) _readoutTransform = readout.transform;
        _readoutCanvas = readout ? readout.GetComponentInParent<Canvas>() : null;
    }

    void OnEnable()
    {
        if (trackGenerator != null)
            trackGenerator.OnTrackGeneratedSuccessfully += HandleTrackGenerated;
        RebuildPath();
    }

    void OnDisable()
    {
        if (trackGenerator != null)
            trackGenerator.OnTrackGeneratedSuccessfully -= HandleTrackGenerated;
    }

    private void HandleTrackGenerated(ProceduralTrackGenerator gen) => RebuildPath();

    private void RebuildPath()
    {
        _path.Clear();
        _totalLength = 0f;
        _cumLengths = null;

        if (trackGenerator == null) return;
        var src = trackGenerator.PathPoints;
        if (src == null || src.Count < 2) return;

        if (useSmoothing)
            GenerateSmoothedPath(src, Mathf.Max(1, smoothingSubdivisionsPerSegment), _path);
        else
            _path.AddRange(src);

        if (_path.Count < 2) return;

        _cumLengths = new float[_path.Count];
        _cumLengths[0] = 0f;
        float length = 0f;
        for (int i = 1; i < _path.Count; i++)
        {
            length += Vector3.Distance(_path[i - 1], _path[i]);
            _cumLengths[i] = length;
        }
        _totalLength = length;
    }

    void LateUpdate()
    {
        if (car == null || _path.Count < 2)
        {
            UpdateReadout(0f);
            return;
        }

        DistanceAlongTrack = ComputeDistanceAlongPath(car.position);
        UpdateReadout(DistanceAlongTrack);
    }

    private void UpdateReadout(float dist)
    {
        if (!readout) return;

        if (displayMode == DisplayMode.WorldDistance)
        {
            int whole = Mathf.RoundToInt(dist);
            if (showPercent && _totalLength > 0f)
            {
                float pct = Normalized * 100f;
                readout.text = $"Dist: {whole} {units} ({pct:0.#}%)";
            }
            else
            {
                readout.text = $"Dist: {whole} {units}";
            }
        }
        else
        {
            int segCount = trackGenerator != null ? trackGenerator.SegmentCount : 0;
            int idx = segCount > 0 ? Mathf.Clamp(Mathf.RoundToInt(Normalized * segCount), 0, segCount) : 0;
            readout.text = $"Seg: {idx}/{segCount}";
        }
    }

    private float ComputeDistanceAlongPath(Vector3 pos)
    {
        if (_path.Count < 2) return 0f;

        int bestIdx = 0;
        float bestSqr = float.PositiveInfinity;
        float bestT = 0f;

        for (int i = 0; i < _path.Count - 1; i++)
        {
            Vector3 a = _path[i];
            Vector3 b = _path[i + 1];
            Vector3 ab = b - a;
            float abLen2 = ab.sqrMagnitude;
            if (abLen2 < 1e-8f) continue;

            float t = Vector3.Dot(pos - a, ab) / abLen2;
            t = Mathf.Clamp01(t);
            Vector3 p = a + ab * t;

            float d2 = (pos - p).sqrMagnitude;
            if (d2 < bestSqr)
            {
                bestSqr = d2;
                bestIdx = i;
                bestT = t;
            }
        }

        float baseDist = _cumLengths != null && bestIdx < _cumLengths.Length ? _cumLengths[bestIdx] : 0f;
        float segLen = Vector3.Distance(_path[bestIdx], _path[bestIdx + 1]);
        return Mathf.Clamp(baseDist + segLen * bestT, 0f, _totalLength);
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
}