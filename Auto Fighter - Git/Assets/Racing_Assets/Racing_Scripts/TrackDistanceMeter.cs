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
        TrackPathSampling.RebuildPathFromRoadCenterline(trackGenerator, _path, ref _cumLengths, out _totalLength);
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
}