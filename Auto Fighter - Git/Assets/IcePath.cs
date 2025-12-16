using UnityEngine;

/// <summary>
/// Represents a single ice path segment. When car enters, it signals ice properties.
/// Uses GroundSurface component for physics adjustments.
/// </summary>
[DisallowMultipleComponent]
public class IcePath : MonoBehaviour
{
    [Header("Visual")]
    [Tooltip("Optional visual root to enable/disable (for fade effects).")]
    [SerializeField] private GameObject visualRoot;

    [Header("Physics")]
    [Tooltip("Layer this ice segment should be on (typically 'RoadSurface').")]
    [SerializeField] private string targetLayer = "RoadSurface";

    private GroundSurface _surface;
    private Collider _collider;

    void Awake()
    {
        _surface = GetComponent<GroundSurface>();
        _collider = GetComponent<Collider>();

        // Ensure surface type is Ice
        if (_surface != null)
            _surface.surfaceType = SurfaceType.Ice;

        // Set layer
        int layer = LayerMask.NameToLayer(targetLayer);
        if (layer >= 0)
            gameObject.layer = layer;

        if (visualRoot == null)
            visualRoot = gameObject;
    }

    public void Show()
    {
        if (visualRoot != null)
            visualRoot.SetActive(true);
    }

    public void Hide()
    {
        if (visualRoot != null)
            visualRoot.SetActive(false);
    }

    /// <summary>
    /// Returns the GroundSurface component for inspection.
    /// </summary>
    public GroundSurface Surface => _surface;
}