using UnityEngine;

/// <summary>
/// Makes world fog feel continuous into the sky by swapping the solid clear color
/// for a horizon-fog skybox. Unity's geometry fog never paints the sky; this does it visually.
/// </summary>
[DisallowMultipleComponent]
public class AmbientHorizonSky : MonoBehaviour
{
    [Header("Sky")]
    [Tooltip("Gradient skybox with a foggy horizon band (Racing/SkyboxHorizonFog).")]
    [SerializeField] private Material skyboxMaterial;

    [Tooltip("Camera to set Clear Flags = Skybox. Leave empty to use this Camera or Camera.main.")]
    [SerializeField] private Camera targetCamera;

    [Header("Optional: match volumetric fog")]
    [Tooltip("If set, copies Horizon Color into FogPulseColorDriver.baseHDR so terrain fog matches the sky haze.")]
    [SerializeField] private FogPulseColorDriver fogPulseDriver;

    [SerializeField] private bool applyOnAwake = true;

    private static readonly int HorizonColorId = Shader.PropertyToID("_HorizonColor");

    private void Awake()
    {
        if (applyOnAwake)
            Apply();
    }

    [ContextMenu("Apply Horizon Sky")]
    public void Apply()
    {
        if (skyboxMaterial == null)
        {
            Debug.LogWarning("[AmbientHorizonSky] No skybox material assigned.", this);
            return;
        }

        RenderSettings.skybox = skyboxMaterial;

        Camera cam = targetCamera;
        if (cam == null)
            cam = GetComponent<Camera>();
        if (cam == null)
            cam = Camera.main;

        if (cam != null)
        {
            cam.clearFlags = CameraClearFlags.Skybox;
            // Keep a matching fallback clear color if something forces Solid Color later.
            if (skyboxMaterial.HasProperty("_ZenithColor"))
                cam.backgroundColor = skyboxMaterial.GetColor("_ZenithColor");
        }

        if (fogPulseDriver != null && skyboxMaterial.HasProperty(HorizonColorId))
        {
            // FogPulseColorDriver.baseHDR is private SerializeField — use reflection-free public path if available.
            // Drive via a small public API added below on FogPulseColorDriver.
            fogPulseDriver.SetBaseColor(skyboxMaterial.GetColor(HorizonColorId));
        }

        DynamicGI.UpdateEnvironment();
    }
}
