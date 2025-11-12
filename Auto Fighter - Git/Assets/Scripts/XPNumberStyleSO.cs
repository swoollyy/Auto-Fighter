using UnityEngine;
using TMPro;

[CreateAssetMenu(menuName = "UI/XP Numbers/Style", fileName = "XPNumberStyle")]
public class XPNumberStyleSO : ScriptableObject
{
    [Header("Typography")]
    public TMP_FontAsset font;
    public Material fontMaterial;
    public float baseFontSize = 3.5f;
    public Color defaultColor = new Color(0.4f, 0.9f, 1f, 1f); // cyan-ish

    [Header("Timing")]
    public float duration = 0.8f;
    [Range(0.01f, 0.3f)] public float fadeInFraction = 0.08f;
    [Range(0.1f, 0.6f)] public float fadeOutFraction = 0.22f;

    [Header("Motion")]
    public float riseDistance = 1.0f;

    [Header("Scale Pop")]
    public float popFromScale = 0.6f;
    public float popToScale = 1.05f;

    [Header("Rendering")]
    public string sortingLayerName = "Default";
    public int sortingOrder = 600;

    [Header("Update")]
    public bool useUnscaledTime = false;
}