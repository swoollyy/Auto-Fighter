using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Spawns every enabled, uncollected Help Patron from the trial config.
/// Each entry has its own track progress. Collected patrons never spawn again.
/// </summary>  
[DisallowMultipleComponent]
public class TrackHelpPatronSpawner : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private ProceduralTrackGenerator trackGenerator;
    [SerializeField] private Transform playerTransform;
    [SerializeField] private GameObject fallbackPrefab;

    [Header("Placement (overridden by TrialConfig when override is on)")]
    [SerializeField] private bool enableSpawning = true;
    [SerializeField] private List<TrialConfig.HelpPatronSpawnEntry> patrons = new()
    {
        new TrialConfig.HelpPatronSpawnEntry()
    };

    [SerializeField] private float heightOffset = 0.85f;
    [SerializeField] private float edgeInnerMargin = 0.2f;
    [SerializeField] private LayerMask roadLayerMask = ~0;
    [SerializeField] private float raycastStartHeight = 6f;
    [SerializeField] private float raycastDownDistance = 20f;
    [SerializeField] private bool alignToSurfaceNormal = true;
    [SerializeField] private bool useSmoothing = true;
    [SerializeField, Min(1)] private int smoothingSubdivisionsPerSegment = 6;

    private readonly List<Vector3> _path = new();
    private readonly List<GameObject> _spawned = new();
    private float[] _cumLengths;
    private float _totalLength;

    public void ApplyConfig(TrialConfig.HelpPatronSettings s)
    {
        if (s == null || !s.overrideHelpPatrons) return;

        enableSpawning = s.enableSpawning;
        patrons = s.patrons != null
            ? new List<TrialConfig.HelpPatronSpawnEntry>(s.patrons)
            : new List<TrialConfig.HelpPatronSpawnEntry>();
        heightOffset = s.heightOffset;
        edgeInnerMargin = s.edgeInnerMargin;
        roadLayerMask = s.roadLayer;
        raycastStartHeight = s.raycastStartHeight;
        raycastDownDistance = s.raycastDownDistance;
        alignToSurfaceNormal = s.alignToSurfaceNormal;
        useSmoothing = s.useSmoothing;
        smoothingSubdivisionsPerSegment = s.smoothingSubdivisionsPerSegment;
        if (s.fallbackPrefab != null)
            fallbackPrefab = s.fallbackPrefab;
    }

    public void InitializeForRun(ProceduralTrackGenerator generator, Transform player)
    {
        trackGenerator = generator;
        playerTransform = player;
        ClearSpawned();
        RebuildPath();
        SpawnUncollectedPatrons();
    }

    public static TrackHelpPatronSpawner EnsureExists()
    {
        var existing = FindObjectOfType<TrackHelpPatronSpawner>(true);
        if (existing != null) return existing;

        var go = new GameObject("TrackHelpPatronSpawner");
        return go.AddComponent<TrackHelpPatronSpawner>();
    }

    private void ClearSpawned()
    {
        for (int i = 0; i < _spawned.Count; i++)
        {
            if (_spawned[i] != null)
                Destroy(_spawned[i]);
        }
        _spawned.Clear();
    }

    private void RebuildPath()
    {
        _path.Clear();
        _cumLengths = null;
        _totalLength = 0f;
        if (trackGenerator == null) return;
        TrackPathSampling.RebuildPathFromRoadCenterline(trackGenerator, _path, ref _cumLengths, out _totalLength);
    }

    private void SpawnUncollectedPatrons()
    {
        if (!enableSpawning || _path.Count < 2 || _totalLength <= 0.01f)
            return;
        if (patrons == null) return;

        var spawnedIds = new HashSet<HelpPatronId>();
        for (int i = 0; i < patrons.Count; i++)
        {
            var entry = patrons[i];
            if (entry == null || !entry.enabled) continue;
            if (HelpPatronProgress.IsCollected(entry.patronId)) continue;
            if (!spawnedIds.Add(entry.patronId)) continue;
            TrySpawnEntry(entry);
        }
    }

    private void TrySpawnEntry(TrialConfig.HelpPatronSpawnEntry entry)
    {
        GameObject prefab = entry.prefab != null ? entry.prefab : fallbackPrefab;
        if (prefab == null)
        {
            Debug.LogWarning("[TrackHelpPatronSpawner] No prefab assigned for " +
                             HelpPatronProgress.DisplayName(entry.patronId) +
                             " on this trial. Set it on the trial config entry.");
            return;
        }

        float norm = Mathf.Clamp01(Mathf.Max(entry.minNormalizedProgress, entry.normalizedProgress));
        float dist = norm * _totalLength;
        TrackPathSampling.SampleAlongPath(_path, _cumLengths, _totalLength, dist, out var center, out var forward);

        var flatFwd = forward;
        flatFwd.y = 0f;
        if (flatFwd.sqrMagnitude < 1e-6f) flatFwd = Vector3.forward;
        flatFwd.Normalize();
        var right = Vector3.Cross(Vector3.up, flatFwd).normalized;

        float halfWidth = trackGenerator != null ? trackGenerator.RoadWidth * 0.5f : 2f;
        float usable = Mathf.Max(0f, halfWidth * entry.lateralFractionOfHalfWidth - edgeInnerMargin);
        Vector3 candidate = center + right * (usable * entry.lateralSign);

        Vector3 origin = candidate + Vector3.up * raycastStartHeight;
        float maxDist = raycastStartHeight + raycastDownDistance;
        Vector3 spawnPos = candidate + Vector3.up * heightOffset;
        Quaternion rot = Quaternion.LookRotation(flatFwd, Vector3.up);

        if (Physics.Raycast(origin, Vector3.down, out RaycastHit hit, maxDist, roadLayerMask, QueryTriggerInteraction.Ignore))
        {
            Vector3 up = alignToSurfaceNormal ? hit.normal : Vector3.up;
            Vector3 fwdOnSurface = Vector3.ProjectOnPlane(flatFwd, up);
            if (fwdOnSurface.sqrMagnitude < 1e-6f)
                fwdOnSurface = Vector3.Cross(up, Vector3.right);
            fwdOnSurface.Normalize();
            rot = Quaternion.LookRotation(fwdOnSurface, up);
            spawnPos = hit.point + up * heightOffset;
        }

        var inst = Instantiate(prefab, spawnPos, rot, transform);
        var pickup = inst.GetComponent<HelpPatronPickup>();
        if (pickup == null)
            pickup = inst.AddComponent<HelpPatronPickup>();
        pickup.Setup(entry.patronId);
        _spawned.Add(inst);
    }
}
