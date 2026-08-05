using UnityEngine;
using MirzaBeig.VolumetricFogLite;

[DisallowMultipleComponent]
public class FogPulseColorDriver : MonoBehaviour
{
    [Header("Pulse Source")]
    [SerializeField] private TrackGuideBeacon beacon;

    [Header("Fog Target (pick ONE)")]
    [Tooltip("Preferred: drag your VolumetricFogRendererFeatureLite from the Renderer Features list here.")]
    [SerializeField] private VolumetricFogRendererFeatureLite fogFeature;

    [Tooltip("Alternative: drag the fog material directly if you don't want to reference the feature.")]
    [SerializeField] private Material fogMaterialOverride;

    [Header("Fog Color Property Name")]
    [Tooltip("Your fog shader UI shows 'Colour'. Common internal names: _Colour or _Color. Put the right one here.")]
    [SerializeField] private string fogColorProperty = "_Colour";

    [Header("HDR Colors")]
    [SerializeField] private Color baseHDR = new Color(0.35f, 0.45f, 0.55f, 1f);
    [SerializeField] private Color pulseHDR = new Color(2.5f, 1.0f, 0.2f, 1f);

    [Header("Blend Shaping")]
    [Range(0f, 4f)] public float pulsePower = 1.6f;
    [Range(0f, 1f)] public float maxBlend = 1.0f;
    [Range(0f, 20f)] public float smooth = 10f;

    [Header("Intensity")]
    [Range(0f, 10f)]
    [SerializeField] private float globalIntensity = 1f;   // master on/off / fade

    [Range(0f, 10f)]
    [SerializeField] private float pulseIntensity = 1f;    // how strong the pulse is

    private Material _fogMat;
    private int _propId;
    private float _smoothed01;

    /// <summary>Ambient / resting fog colour (before beacon pulse).</summary>
    public Color BaseHDR
    {
        get => baseHDR;
        set => baseHDR = value;
    }

    public void SetBaseColor(Color color)
    {
        baseHDR = color;
    }

    private void Awake()
    {
        _propId = Shader.PropertyToID(fogColorProperty);
        ResolveFogMaterial();
    }

    private void OnEnable()
    {
        ResolveFogMaterial();
    }

    private void ResolveFogMaterial()
    {
        if (fogMaterialOverride != null)
        {
            _fogMat = fogMaterialOverride;
            return;
        }

        if (fogFeature != null && fogFeature.settings != null)
        {
            _fogMat = fogFeature.settings.fogMaterial;
            return;
        }

        _fogMat = null;
    }

    private void Update()
    {
        if (_fogMat == null)
        {
            ResolveFogMaterial();
            if (_fogMat == null) return;
        }

        if (beacon == null) return;

        float b = Mathf.Clamp01(beacon.CurrentBlink01);

        // Shape the pulse
        b = Mathf.Pow(b, pulsePower);

        // Apply pulse strength + global intensity
        b *= maxBlend * pulseIntensity * globalIntensity;

        _smoothed01 = Mathf.Lerp(_smoothed01, b, 1f - Mathf.Exp(-smooth * Time.deltaTime));

        float t = Mathf.Clamp01(_smoothed01);
        Color c = Color.Lerp(baseHDR, pulseHDR, t);
        _fogMat.SetColor(_propId, c);
    }
}
