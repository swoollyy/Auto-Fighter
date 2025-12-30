using Pathfinding;
using Pathfinding.RVO;
using System.Collections;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;

public class AstarRuntimeBootstrap : MonoBehaviour
{ 
    public static AstarRuntimeBootstrap Instance { get; private set; }

    [Header("Runtime Recast Build")]
    [Tooltip("Layer that contains ONLY your drivable road meshes/colliders.")]
    [SerializeField] private LayerMask roadSurfaceMask;

    [Tooltip("Put NPC cars on their own layer and exclude it from the recast graph mask to avoid navmesh holes.")]
    [SerializeField] private LayerMask agentLayerMaskToExclude;

    [Tooltip("Extra padding added to computed bounds so the recast graph fully covers the road.")]
    [SerializeField] private Vector3 boundsPadding = new Vector3(10f, 10f, 10f);

    [Tooltip("If true, we rebuild/scan whenever a new scene loads (you can also call ScanForTrack manually).")]
    [SerializeField] private bool rescanOnSceneLoad = true;

    [Header("RVO")]
    [SerializeField] private bool ensureRvoSimulator = true;



    

    private int nonProjectileLayer;
    private int projectileLayer;
    private int npcLayer;
    private int defaultLayer;


    private AstarPath _astar;
    private RVOSimulator _rvo;

    private void Awake()
    {
        nonProjectileLayer = LayerMask.NameToLayer("Non-Colliding Projectile");
        projectileLayer = LayerMask.NameToLayer("Projectile");
        npcLayer = LayerMask.NameToLayer("NPCCar");
        defaultLayer = LayerMask.NameToLayer("Default");
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;

        DontDestroyOnLoad(gameObject);

        _astar = GetComponent<AstarPath>();
        if (_astar == null) _astar = gameObject.AddComponent<AstarPath>();

        if (ensureRvoSimulator)
        {
            _rvo = GetComponent<RVOSimulator>();
            if (_rvo == null) _rvo = gameObject.AddComponent<RVOSimulator>();
        }

        if (rescanOnSceneLoad)
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
        }
    }

    private void OnDestroy()
    {
        if (Instance == this)
            SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        // You probably generate the track after scene load, so we DON'T scan here blindly.
        // Call ScanForTrack(trackRoot) from your GameManager after generation.
    }

    /// <summary>
    /// Call this AFTER the procedural track meshes are spawned/parented.
    /// trackRoot should be the ProceduralTrackGenerator GameObject (or whatever owns the road pieces).
    /// </summary>
    public void ScanForTrack(Transform trackRoot)
    {
        if (_astar == null) _astar = AstarPath.active;
        if (_astar == null || trackRoot == null) return;

        Bounds b;
        if (!TryComputeRoadBounds(trackRoot, out b))
        {
            Debug.LogWarning("[AstarRuntimeBootstrap] No road bounds found to scan. Check roadSurfaceMask.");
            return;
        }

        b.Expand(boundsPadding);

        StartCoroutine(ScanRecastCoroutine(b));
    }

    private IEnumerator ScanRecastCoroutine(Bounds bounds)
    {
        var recast = GetOrCreateRecastGraph();


        recast.rasterizeTerrain = false;
        recast.rasterizeTrees = false;
        recast.rasterizeMeshes = true;     // road mesh layer-filtered
        recast.rasterizeColliders = false;
        // Rasterize ONLY the road layer(s), exclude agent layer so cars don't carve holes.
        recast.mask = roadSurfaceMask & ~agentLayerMaskToExclude;

        recast.forcedBoundsCenter = bounds.center;
        recast.forcedBoundsSize = bounds.size;

        if (recast.perLayerModifications == null)
            recast.perLayerModifications = new System.Collections.Generic.List<RecastGraph.PerLayerModification>();


        /*MarkLayerUnwalkable(recast, "Obstacles");
        MarkLayerUnwalkable(recast, "Projectile");
        MarkLayerUnwalkable(recast, "NPCCar");
        MarkLayerUnwalkable(recast, "Non-Colliding Projectile");
        */
        // Async scan (old API returns IEnumerable<Progress>)
        foreach (var _ in AstarPath.active.ScanAsync(recast))
            yield return null;

        Debug.Log($"[AstarRuntimeBootstrap] Recast scan done. Bounds={bounds.size}");
    }

    private void MarkLayerUnwalkable(RecastGraph graph, string layerName)
    {
        int layer = LayerMask.NameToLayer(layerName);
        if (layer < 0 || layer >= 32) return;

        if (graph.perLayerModifications == null)
            graph.perLayerModifications = new System.Collections.Generic.List<RecastGraph.PerLayerModification>();

        // Find existing entry for this layer
        int idx = graph.perLayerModifications.FindIndex(m => m.layer == layer);

        var mod = new RecastGraph.PerLayerModification
        {
            layer = layer,
            mode = RecastNavmeshModifier.Mode.UnwalkableSurface,
            // surfaceID not needed for UnwalkableSurface
        };

        if (idx >= 0) graph.perLayerModifications[idx] = mod;
        else graph.perLayerModifications.Add(mod);
    }


    private RecastGraph GetOrCreateRecastGraph()
    {
        var data = AstarPath.active.data;

        // Try find existing recast
        var existing = data.graphs?.OfType<RecastGraph>().FirstOrDefault();
        if (existing != null) return existing;

        // Create new recast graph
        var g = (RecastGraph)data.AddGraph(typeof(RecastGraph));

        // Sensible defaults for a racing “ribbon” (you can tune later, but keep it minimal)
        g.characterRadius = .25f;          // roughly half car width clearance feel
        g.walkableHeight = 2.0f;
        g.walkableClimb = 0.6f;
        g.cellSize = 0.1f;
        g.maxEdgeLength = 6f;
        g.maxSlope = 70f;

        // Tiles keep it reasonable (especially if track is long)
        g.useTiles = true;
        g.editorTileSize = 256;

        return g;
    }

    private bool TryComputeRoadBounds(Transform trackRoot, out Bounds bounds)
    {
        bounds = default;

        // Renderer bounds is usually the most reliable for procedural mesh roads
        var rends = trackRoot.GetComponentsInChildren<Renderer>(true)
            .Where(r => ((roadSurfaceMask.value & (1 << r.gameObject.layer)) != 0));

        bool any = false;
        foreach (var r in rends)
        {
            if (!any) { bounds = r.bounds; any = true; }
            else bounds.Encapsulate(r.bounds);
        }

        // Fallback: colliders if renderers aren’t present
        if (!any)
        {
            var cols = trackRoot.GetComponentsInChildren<Collider>(true)
                .Where(c => ((roadSurfaceMask.value & (1 << c.gameObject.layer)) != 0));

            foreach (var c in cols)
            {
                if (!any) { bounds = c.bounds; any = true; }
                else bounds.Encapsulate(c.bounds);
            }
        }

        return any;
    }
}
