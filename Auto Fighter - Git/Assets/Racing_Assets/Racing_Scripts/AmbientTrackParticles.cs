using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Parks one particle system and streams world-space motes along the track.
/// Particles stay where they are born (you drive through them). Only a sliding
/// window around the player is kept alive, so the whole course does not need
/// a giant particle budget.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(ParticleSystem))]
public class AmbientTrackParticles : MonoBehaviour
{
    [Header("Window")]
    [Tooltip("How far ahead of the car to keep particles ready.")]
    [SerializeField] private float aheadMeters = 220f;
    [Tooltip("How far behind the car particles are allowed to remain.")]
    [SerializeField] private float behindMeters = 90f;

    [Header("Density")]
    [SerializeField, Min(1f)] private float metersPerCluster = 8f;
    [SerializeField, Range(1, 8)] private int particlesPerCluster = 3;
    [SerializeField, Min(8)] private int maxLiveParticles = 250;

    [Header("Scatter")]
    [SerializeField] private float lateralRadius = 16f;
    [SerializeField] private float heightMin = 0.4f;
    [SerializeField] private float heightMax = 7f;
    [SerializeField] private float driftSpeed = 0.12f;

    private ParticleSystem _ps;
    private Transform _player;
    private readonly List<Vector3> _path = new List<Vector3>(2048);
    private float[] _cum;
    private float _totalLength;
    private readonly HashSet<int> _filled = new HashSet<int>(256);
    private readonly List<int> _forgetScratch = new List<int>(64);
    private TrackDistanceMeter _meter;
    private int _lastClosestIdx;

    public void Initialize(ProceduralTrackGenerator generator, Transform player)
    {
        _player = player;
        _filled.Clear();
        _lastClosestIdx = 0;

        EnsureSystem();
        _ps.Clear(true);
        _ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        if (!TrackPathSampling.RebuildPathFromRoadCenterline(generator, _path, ref _cum, out _totalLength))
            return;

        ConfigureSystem();
        _ps.Play(true);
        FillWindow(GetPlayerDistance());
    }

    private void Awake()
    {
        EnsureSystem();
        ConfigureSystem();
    }

    private void LateUpdate()
    {
        if (_totalLength <= 1f || _ps == null)
            return;

        if (_player == null && GameManager_Racing.Instance != null && GameManager_Racing.Instance.ActiveCar != null)
            _player = GameManager_Racing.Instance.ActiveCar.transform;

        FillWindow(GetPlayerDistance());
    }

    private void EnsureSystem()
    {
        if (_ps == null)
            _ps = GetComponent<ParticleSystem>();
    }

    private void ConfigureSystem()
    {
        if (_ps == null) return;

        var main = _ps.main;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;
        main.maxParticles = Mathf.Max(main.maxParticles, maxLiveParticles);
        main.emitterVelocityMode = ParticleSystemEmitterVelocityMode.Transform;

        var emission = _ps.emission;
        emission.enabled = false;

        var inherit = _ps.inheritVelocity;
        inherit.enabled = false;

        transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
    }

    private void FillWindow(float playerDist)
    {
        if (_path.Count < 2) return;

        float spacing = Mathf.Max(1f, metersPerCluster);
        int slotMin = Mathf.Max(0, Mathf.FloorToInt((playerDist - behindMeters) / spacing));
        int slotMax = Mathf.CeilToInt((playerDist + aheadMeters) / spacing);
        int lastSlot = Mathf.FloorToInt(_totalLength / spacing);

        ForgetSlotsBehind(slotMin);

        int live = _ps.particleCount;
        if (live >= maxLiveParticles)
            return;

        for (int slot = slotMin; slot <= slotMax && slot <= lastSlot; slot++)
        {
            for (int sub = 0; sub < particlesPerCluster; sub++)
            {
                int key = (slot << 4) | (sub & 15);
                if (_filled.Contains(key))
                    continue;
                if (live >= maxLiveParticles)
                    return;

                EmitAt(WorldPosFor(slot, sub, spacing));
                _filled.Add(key);
                live++;
            }
        }
    }

    private void ForgetSlotsBehind(int slotMin)
    {
        if (_filled.Count == 0) return;

        _forgetScratch.Clear();
        foreach (int key in _filled)
        {
            if ((key >> 4) < slotMin)
                _forgetScratch.Add(key);
        }

        for (int i = 0; i < _forgetScratch.Count; i++)
            _filled.Remove(_forgetScratch[i]);
    }

    private Vector3 WorldPosFor(int slot, int sub, float spacing)
    {
        float dist = Mathf.Clamp(slot * spacing, 0f, _totalLength);
        TrackPathSampling.SampleAlongPath(_path, _cum, _totalLength, dist, out Vector3 center, out Vector3 forward);

        Vector3 flat = forward;
        flat.y = 0f;
        if (flat.sqrMagnitude < 1e-6f) flat = Vector3.forward;
        flat.Normalize();
        Vector3 right = Vector3.Cross(Vector3.up, flat);

        float n0 = Hash01(slot, sub, 1);
        float n1 = Hash01(slot, sub, 2);
        float n2 = Hash01(slot, sub, 3);
        float angle = n0 * Mathf.PI * 2f;
        float radius = Mathf.Sqrt(n1) * lateralRadius;

        return center
            + right * (Mathf.Cos(angle) * radius)
            + flat * (Mathf.Sin(angle) * radius * 0.35f)
            + Vector3.up * Mathf.Lerp(heightMin, heightMax, n2);
    }

    private void EmitAt(Vector3 worldPos)
    {
        var emit = new ParticleSystem.EmitParams
        {
            position = worldPos,
            applyShapeToPosition = false,
            velocity = Random.insideUnitSphere * driftSpeed
        };
        _ps.Emit(emit, 1);
    }

    private float GetPlayerDistance()
    {
        if (_meter == null)
            _meter = FindObjectOfType<TrackDistanceMeter>();
        if (_meter != null)
            return Mathf.Clamp(_meter.DistanceAlongTrack, 0f, _totalLength);
        if (_player == null)
            return 0f;
        return Mathf.Clamp(EstimateDistance(_player.position), 0f, _totalLength);
    }

    private float EstimateDistance(Vector3 p)
    {
        if (_path.Count < 2 || _cum == null) return 0f;

        int start = Mathf.Clamp(_lastClosestIdx - 16, 0, _path.Count - 2);
        int end = Mathf.Clamp(_lastClosestIdx + 16, 0, _path.Count - 2);
        float best = float.MaxValue;
        int bestIdx = start;
        float bestT = 0f;

        for (int i = start; i <= end; i++)
        {
            Vector3 a = _path[i];
            Vector3 b = _path[i + 1];
            Vector3 ab = b - a;
            float abSqr = ab.sqrMagnitude;
            float t = abSqr > 1e-6f ? Mathf.Clamp01(Vector3.Dot(p - a, ab) / abSqr) : 0f;
            float dSqr = (p - (a + ab * t)).sqrMagnitude;
            if (dSqr < best)
            {
                best = dSqr;
                bestIdx = i;
                bestT = t;
            }
        }

        _lastClosestIdx = bestIdx;
        return _cum[bestIdx] + Vector3.Distance(_path[bestIdx], _path[bestIdx + 1]) * bestT;
    }

    private static float Hash01(int a, int b, int c)
    {
        unchecked
        {
            int h = a * 374761393 + b * 668265263 + c * 1274126177;
            h = (h ^ (h >> 13)) * 1274126177;
            return ((h ^ (h >> 16)) & 0xFFFF) / 65535f;
        }
    }
}
