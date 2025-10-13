using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TMPro;

[CreateAssetMenu(menuName = "UI/Damage Numbers/Style", fileName = "DamageNumberStyle")]
public class DamageNumberStyleSO : ScriptableObject
{
    [Header("Typography")]
    public TMP_FontAsset font;
    public Material fontMaterial;
    public float baseFontSize = 4f;
    public Color defaultColor = Color.white;

    [Header("Timing")]
    public float duration = 0.9f;
    [Range(0.01f, 0.3f)] public float fadeInFraction = 0.08f;
    [Range(0.1f, 0.6f)] public float fadeOutFraction = 0.25f;

    [Header("Motion")]
    public float riseDistance = 1.25f;

    [Header("Scale Pop")]
    public float popFromScale = 0.6f;
    public float popToScale = 1.1f;

    [Header("Rendering")]
    public string sortingLayerName = "Default";
    public int sortingOrder = 500;

    [Header("Update")]
    public bool useUnscaledTime = false;
}
