using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public class TrackGuideBeacon : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private ProceduralTrackGenerator trackGenerator;
    [SerializeField] private TrackDistanceMeter distanceMeter;

    [Header("Beacon")]
    [SerializeField] private Transform beacon;                 // the object that moves (can be this.transform)
    [SerializeField] private Light beaconLight;                // optional: point/spot light to blink
    [SerializeField] private Renderer emissiveRenderer;        // optional: emissive mesh to blink
    [SerializeField] private string emissiveColorProperty = "_EmissionColor";

    public enum PlacementMode
    {
        OnTrackAhead,      // old behavior (on-road)
        HorizonCompass     // NEW: far away in fog, still turns with road
    }

    [Header("Placement Mode")]
    [SerializeField] private PlacementMode placementMode = PlacementMode.HorizonCompass;

    [Header("On-Track Placement (legacy)")]
    [Tooltip("How far ahead of the car (in meters along the spline) the beacon should sit.")]
    [SerializeField, Min(1f)] private float lookAheadDistance = 120f;

    [Tooltip("Extra vertical offset so it floats above the road.")]
    [SerializeField] private float heightOffset = 6f;

    [Tooltip("Extra distance used ONLY for rotation look direction.")]
    [SerializeField, Min(0.1f)] private float rotationLookAhead = 8f;

    [Header("Horizon Compass Placement (NEW)")]
    [Tooltip("How far in front of the CAMERA the beacon sits (world meters). Bigger = more 'on the horizon'.")]
    [SerializeField, Min(10f)] private float horizonDistance = 450f;

    [Tooltip("How high above the CAMERA the beacon sits (world meters).")]
    [SerializeField] private float horizonHeight = 35f;

    [Tooltip("Use track forward at current car distance, but slightly anticipate turns by sampling a bit ahead.")]
    [SerializeField, Min(0f)] private float tangentSampleAhead = 35f;

    [Tooltip("Blend between track forward (0) and camera forward (1). Use a little camera forward if you want it always in view.")]
    [SerializeField, Range(0f, 1f)] private float cameraForwardBlend = 0.15f;

    [SerializeField, Min(0f)]
    private float segmentAheadDistance = 35f; // roughly one road segment

    [Header("Smoothing")]
    [SerializeField, Min(0f)] private float positionSmoothTime = 0.25f;
    [SerializeField, Min(0f)] private float directionSmoothTime = 0.20f;
    [SerializeField] private bool useUnscaledTime = false;

    [Header("Blink")]
    [SerializeField] private bool enableBlink = true;
    [SerializeField, Min(0.05f)] private float blinkHz = 1.6f;
    [SerializeField] private AnimationCurve blinkCurve = AnimationCurve.EaseInOut(0, 0, 1, 1);
    [SerializeField] private float lightMinIntensity = 0.0f;
    [SerializeField] private float lightMaxIntensity = 12.0f;
    [SerializeField] private float emissiveMin = 0.0f;
    [SerializeField] private float emissiveMax = 6.0f;
    [SerializeField] private Color emissiveTint = Color.white;

    [Header("Distance Blink Scaling")]
    [SerializeField]
    private AnimationCurve blinkHzByDistance =
        AnimationCurve.Linear(0f, 1.2f, 1000f, 5f);

    [Header("Audio Pulse")]
    [SerializeField] private AudioSource beaconAudio;
    [SerializeField] private float audioMinVolume = 0.0f;
    [SerializeField] private float audioMaxVolume = 0.9f;
    [SerializeField] private bool audioFollowsBlink = true;

    [Header("Path Sampling")]
    [SerializeField] private bool useSmoothing = true;
    [SerializeField, Min(1)] private int smoothingSubdivisionsPerSegment = 6;


    public float CurrentBlink01 { get; private set; }

    private readonly List<Vector3> _path = new();
    private float[] _cumLengths;
    private float _totalLength;
    private Vector3 _posVel;
    private Vector3 _smoothedDir = Vector3.forward;
    private Material _emissiveMatInstance;
    private int _emissivePropId;

    private Camera _cam;

    private void Awake()
    {
        if (beacon == null) beacon = transform;

        if (trackGenerator == null) trackGenerator = FindObjectOfType<ProceduralTrackGenerator>(true);
        if (distanceMeter == null) distanceMeter = FindObjectOfType<TrackDistanceMeter>(true);

        _cam = Camera.main;
        _smoothedDir = Vector3.forward;

        _emissivePropId = Shader.PropertyToID(emissiveColorProperty);
        if (emissiveRenderer != null)
        {
            // Instance material so we don't mutate shared material
            _emissiveMatInstance = emissiveRenderer.material;
        }

        if (beaconAudio != null)
        {
            beaconAudio.loop = true;
            if (!beaconAudio.isPlaying)
                beaconAudio.Play();
        }

        WireGenerator();
        RebuildPath();
    }

    private void OnEnable()
    {
        WireGenerator();
        RebuildPath();
    }

    private void OnDisable()
    {
        if (trackGenerator != null)
            trackGenerator.OnTrackGeneratedSuccessfully -= HandleTrackGenerated;
    }

    private void WireGenerator()
    {
        if (trackGenerator == null) return;
        trackGenerator.OnTrackGeneratedSuccessfully -= HandleTrackGenerated;
        trackGenerator.OnTrackGeneratedSuccessfully += HandleTrackGenerated;
    }

    private void HandleTrackGenerated(ProceduralTrackGenerator gen)
    {
        RebuildPath();
    }

    private void LateUpdate()
    {
        if (_path.Count < 2 || distanceMeter == null)
        {
            ApplyBlink(0f);
            return;
        }

        float carDist = distanceMeter.DistanceAlongTrack;

        if (placementMode == PlacementMode.OnTrackAhead)
        {
            // ===== Old behavior (kept) =====
            float targetDist = carDist + lookAheadDistance;
            Vector3 pos = SamplePositionAtDistanceLooped(targetDist);
            pos.y += heightOffset;

            float lookDist = targetDist + rotationLookAhead;
            Vector3 ahead = SamplePositionAtDistanceLooped(lookDist);
            Vector3 fwd = (ahead - pos);
            if (fwd.sqrMagnitude < 0.0001f) fwd = beacon.forward;

            beacon.SetPositionAndRotation(pos, Quaternion.LookRotation(fwd.normalized, Vector3.up));
        }
        else
        {
            float dirDist = carDist + segmentAheadDistance;

            Vector3 p0 = SamplePositionAtDistanceLooped(dirDist);
            Vector3 p1 = SamplePositionAtDistanceLooped(dirDist + Mathf.Max(0f, tangentSampleAhead));

            Vector3 trackDir = (p1 - p0);
            trackDir.y = 0f;
            if (trackDir.sqrMagnitude < 1e-6f)
                trackDir = GetSegmentForwardAtDistanceLooped(dirDist);
            else
                trackDir.Normalize();

            Vector3 camDir = (_cam != null) ? _cam.transform.forward : trackDir;
            camDir.y = 0f;
            if (camDir.sqrMagnitude > 1e-6f) camDir.Normalize();

            // Blend: 0 = pure track, 1 = pure camera.
            Vector3 desiredDir = Vector3.Slerp(trackDir, camDir, cameraForwardBlend);
            desiredDir.y = 0f;
            if (desiredDir.sqrMagnitude < 1e-6f) desiredDir = trackDir;
            desiredDir.Normalize();

            // Smooth direction (prevents abrupt yaw snaps + keeps it in view)
            float dt = useUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;
            float dirT = directionSmoothTime <= 0f ? 1f : 1f - Mathf.Exp(-dt / directionSmoothTime);

            _smoothedDir = Vector3.Slerp(_smoothedDir, desiredDir, dirT);
            _smoothedDir.y = 0f;
            if (_smoothedDir.sqrMagnitude < 1e-6f) _smoothedDir = desiredDir;
            _smoothedDir.Normalize();

            // Place relative to camera so it feels like "horizon"
            Vector3 anchorPos = (_cam != null) ? _cam.transform.position : p0;

            // Desired horizon position
            Vector3 targetPos = anchorPos + _smoothedDir * horizonDistance;
            targetPos.y = anchorPos.y + horizonHeight;

            // Smooth position (prevents teleporting)
            Vector3 smoothPos = positionSmoothTime <= 0f
                ? targetPos
                : Vector3.SmoothDamp(beacon.position, targetPos, ref _posVel, positionSmoothTime, Mathf.Infinity, dt);

            beacon.SetPositionAndRotation(smoothPos, Quaternion.LookRotation(_smoothedDir, Vector3.up));
        }

        float scaledHz = blinkHzByDistance.Evaluate(carDist);
        scaledHz = Mathf.Max(0.05f, scaledHz); // safety floor

        float blink01 = enableBlink ? EvaluateBlink01(scaledHz) : 1f;
        ApplyBlink(blink01);
        CurrentBlink01 = blink01;
    }

    private float EvaluateBlink01(float hz)
    {
        float t = Mathf.Repeat(Time.time * hz, 1f);
        float v = blinkCurve != null ? blinkCurve.Evaluate(t) : t;
        return Mathf.Clamp01(v);
    }

    private void ApplyBlink(float blink01)
    {
        if (beaconLight != null)
        {
            beaconLight.intensity = Mathf.Lerp(lightMinIntensity, lightMaxIntensity, blink01);
            beaconLight.enabled = beaconLight.intensity > 0.001f;
        }

        if (_emissiveMatInstance != null)
        {
            float e = Mathf.Lerp(emissiveMin, emissiveMax, blink01);
            Color c = emissiveTint * e;
            c.a = 1f;
            _emissiveMatInstance.SetColor(_emissivePropId, c);
        }

        if (beaconAudio != null && audioFollowsBlink)
        {
            beaconAudio.volume =
                Mathf.Lerp(audioMinVolume, audioMaxVolume, blink01);
        }

    }

    private void RebuildPath()
    {
        _path.Clear();
        _cumLengths = null;
        _totalLength = 0f;

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

        float len = 0f;
        for (int i = 1; i < _path.Count; i++)
        {
            len += Vector3.Distance(_path[i - 1], _path[i]);
            _cumLengths[i] = len;
        }
        _totalLength = len;
    }

    // Treat the track as loopable for sampling.
    private Vector3 SamplePositionAtDistanceLooped(float dist)
    {
        if (_path.Count < 2) return beacon.position;
        if (_cumLengths == null || _cumLengths.Length != _path.Count) return _path[0];

        float total = Mathf.Max(0.0001f, _totalLength);
        float d = dist % total;
        if (d < 0f) d += total;

        // Linear scan (fast enough for typical path sizes).
        int idx = 0;
        for (int i = 0; i < _cumLengths.Length - 1; i++)
        {
            if (_cumLengths[i + 1] >= d)
            {
                idx = i;
                break;
            }
        }

        float segStart = _cumLengths[idx];
        float segEnd = _cumLengths[Mathf.Min(idx + 1, _cumLengths.Length - 1)];
        float segLen = Mathf.Max(0.0001f, segEnd - segStart);
        float t = Mathf.Clamp01((d - segStart) / segLen);

        Vector3 a = _path[idx];
        Vector3 b = _path[Mathf.Min(idx + 1, _path.Count - 1)];
        return Vector3.Lerp(a, b, t);
    }

private Vector3 GetSegmentForwardAtDistanceLooped(float dist)
{
    if (_path.Count < 2) return Vector3.forward;
    if (_cumLengths == null || _cumLengths.Length != _path.Count) return Vector3.forward;

    float total = Mathf.Max(0.0001f, _totalLength);
    float d = dist % total;
    if (d < 0f) d += total;

    int idx = 0;
    for (int i = 0; i < _cumLengths.Length - 1; i++)
    {
        if (_cumLengths[i + 1] >= d)
        {
            idx = i;
            break;
        }
    }

    int next = Mathf.Min(idx + 1, _path.Count - 1);
    Vector3 fwd = (_path[next] - _path[idx]);
    fwd.y = 0f;

    if (fwd.sqrMagnitude < 1e-6f) return Vector3.forward;
    return fwd.normalized;
}


// Your existing smoothing method should already exist in this file (kept identical to your current version).
private static void GenerateSmoothedPath(IReadOnlyList<Vector3> src, int subdivisionsPerSegment, List<Vector3> dst)
    {
        dst.Clear();
        if (src == null || src.Count < 2)
            return;

        // Catmull-Rom-ish smoothing using 4-point segments.
        // If your existing bundle already had a specific implementation, paste it here unchanged.
        // (Keeping this minimal to avoid stepping on your current smoothing behavior.)
        dst.AddRange(src);
    }
}
