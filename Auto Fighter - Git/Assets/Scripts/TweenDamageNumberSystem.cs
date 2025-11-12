using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TMPro;
using DG.Tweening;
using UnityEngine.Pool;

public class TweenDamageNumberSystem : MonoBehaviour, IDamageNumberSystem
{
    [Header("Basics")]
    [SerializeField] private Camera targetCamera;
    [SerializeField] private float duration = 0.9f;
    [SerializeField] private float riseDistance = 1.25f;
    [SerializeField] private float surfaceOffset = 0.08f;
    [SerializeField] private bool useUnscaledTime = false;
    [SerializeField] private DamageNumberStyleSO style;
    [SerializeField] private float cameraForwardOffset = 0.02f;

    [Header("Typography")]
    [SerializeField] private TMP_FontAsset font;
    [SerializeField] private Material fontMaterial;
    [SerializeField] private float baseFontSize = 4f;
    [SerializeField] private Color defaultColor = Color.white;

    [Header("Rendering")]
    [SerializeField] private string sortingLayerName = "Default";
    [SerializeField] private int sortingOrder = 500;
    [Tooltip("Increase sorting order each spawn so newest is always on top.")]
    [SerializeField] private bool incrementalSorting = true;
    [Tooltip("Maximum extra ordering range before wrapping back to base order.")]
    [SerializeField] private int sortingOrderWrapRange = 5000;
    private int _nextSortingOrder;

    [Header("Outline")]
    [SerializeField] private bool enableOutline = true;
    [SerializeField, Range(0f, 1f)] private float outlineWidth = 0.35f;
    [SerializeField] private Color outlineColor = Color.black;
    [SerializeField, Range(0f, 1f)] private float faceDilate = 0.05f;

    [Header("Damage -> Size Mapping")]
    [SerializeField] private float minDamageForScale = 1f;
    [SerializeField] private float maxDamageForScale = 100f;
    [SerializeField] private float minScale = 0.75f;
    [SerializeField] private float maxScale = 1.75f;
    [SerializeField] private bool useLogScale = true;
    [SerializeField] private float logBase = 10f;
    [SerializeField] private AnimationCurve sizeCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

    [Header("Pop & Shrink Behaviour")]
    [Tooltip("Seconds to wait before doing the fast pop (so number is visible first).")]
    [SerializeField, Range(0f, 0.25f)] private float popDelaySeconds = 0.08f;
    [Tooltip("Fraction of total duration used for the pop tween itself (fast scale up).")]
    [SerializeField, Range(0.05f, 0.5f)] private float popDurationFraction = 0.15f;
    [Tooltip("Final scale multiplier (relative to popped max scale) the number shrinks to. 1 = no shrink.")]
    [SerializeField, Range(0.1f, 1f)] private float endShrinkScaleMultiplier = 0.75f;

    [Header("Z Axis Shake")]
    [Tooltip("Z axis tilt angle during pop (visual shake).")]
    [SerializeField, Range(0f, 45f)] private float tiltAngle = 12f;
    [Tooltip("Fraction of total duration used for tilt shake (starts at pop).")]
    [SerializeField, Range(0.02f, 0.6f)] private float tiltDurationFraction = 0.15f;
    [SerializeField] private bool enableTilt = true;

    [Header("Pool")]
    [SerializeField] private int defaultCapacity = 32;
    [SerializeField] private int maxSize = 256;

    private IObjectPool<GameObject> pool;

    void Awake()
    {
        if (!targetCamera) targetCamera = Camera.main;
        DamageNumbers.Register(this);
        _nextSortingOrder = style ? style.sortingOrder : sortingOrder;
        pool = new ObjectPool<GameObject>(Create, OnGet, OnRelease, OnDestroyPooled, true, defaultCapacity, maxSize);
        Prewarm(48);
        DOTween.SetTweensCapacity(200, 50);
    }

    public void Prewarm(int count)
    {
        count = Mathf.Max(0, count);
        var arr = new GameObject[count];
        for (int i = 0; i < count; i++) arr[i] = pool.Get();
        for (int i = 0; i < count; i++) pool.Release(arr[i]);
    }

    private GameObject Create()
    {
        var go = new GameObject("DamageNumber", typeof(TextMeshPro));
        go.transform.SetParent(transform, false);
        var tmp = go.GetComponent<TextMeshPro>();
        tmp.enableWordWrapping = false;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.enableAutoSizing = false;
        tmp.extraPadding = true;

        var useFont = style ? style.font : font;
        var useMat = style ? style.fontMaterial : fontMaterial;
        var useSize = style ? style.baseFontSize : baseFontSize;
        var useColor = style ? style.defaultColor : defaultColor;
        if (useFont) tmp.font = useFont;

        Material baseMat = null;
        if (IsTMPMaterial(useMat)) baseMat = useMat;
        else if (tmp.font) baseMat = tmp.font.material;
        if (baseMat) tmp.fontSharedMaterial = baseMat;

        tmp.fontMaterial = new Material(tmp.fontSharedMaterial);
        ApplyOutlineAndRefresh(tmp);

        tmp.fontSize = useSize;
        var mr = go.GetComponent<MeshRenderer>();
        var layerName = style ? style.sortingLayerName : sortingLayerName;
        var baseOrder = style ? style.sortingOrder : sortingOrder;
        if (mr)
        {
            mr.sortingLayerName = layerName;
            mr.sortingOrder = baseOrder;
        }

        tmp.color = new Color(useColor.r, useColor.g, useColor.b, 0f);
        go.SetActive(false);
        return go;
    }

    private static bool IsTMPMaterial(Material m) => m && m.shader && m.shader.name.StartsWith("TextMeshPro/");

    private void ApplyOutlineAndRefresh(TextMeshPro tmp)
    {
        if (!tmp || !enableOutline) return;
        if (!IsTMPMaterial(tmp.fontMaterial) && tmp.font && IsTMPMaterial(tmp.font.material))
        {
            tmp.fontSharedMaterial = tmp.font.material;
            tmp.fontMaterial = new Material(tmp.fontSharedMaterial);
        }
        var mat = tmp.fontMaterial;
        if (!mat) return;
        mat.EnableKeyword("OUTLINE_ON");
        if (mat.HasProperty(ShaderUtilities.ID_OutlineWidth))
            mat.SetFloat(ShaderUtilities.ID_OutlineWidth, outlineWidth);
        if (mat.HasProperty(ShaderUtilities.ID_OutlineColor))
        {
            var col = outlineColor; if (col.a <= 0f) col.a = 1f;
            mat.SetColor(ShaderUtilities.ID_OutlineColor, col);
        }
        if (mat.HasProperty(ShaderUtilities.ID_FaceDilate))
            mat.SetFloat(ShaderUtilities.ID_FaceDilate, faceDilate);
        tmp.extraPadding = true;
        tmp.UpdateMeshPadding();
        tmp.SetMaterialDirty();
    }

    private float ComputeScale(float damage)
    {
        float d = Mathf.Max(damage, 0f);
        if (useLogScale)
        {
            float minL = Mathf.Log(minDamageForScale + 1f, logBase);
            float maxL = Mathf.Log(maxDamageForScale + 1f, logBase);
            float valL = Mathf.Log(d + 1f, logBase);
            float tL = Mathf.InverseLerp(minL, maxL, valL);
            return Mathf.Lerp(minScale, maxScale, sizeCurve.Evaluate(tL));
        }
        float t = Mathf.InverseLerp(minDamageForScale, maxDamageForScale, d);
        return Mathf.Lerp(minScale, maxScale, sizeCurve.Evaluate(t));
    }

    public void Spawn(float amount, Vector3 position, Color? overrideColor = null)
    {
        var go = pool.Get();
        DOTween.Kill(go.transform, false);
        var tmp = go.GetComponent<TextMeshPro>();
        DOTween.Kill(tmp, false);
        ApplyOutlineAndRefresh(tmp);

        tmp.text = amount.ToString("0.#");
        var fallbackColor = style ? style.defaultColor : defaultColor;
        var col = overrideColor ?? fallbackColor;
        tmp.color = new Color(col.r, col.g, col.b, 0f);

        float baseSize = style ? style.baseFontSize : baseFontSize;
        float sizeScale = ComputeScale(amount);
        tmp.fontSize = baseSize * sizeScale;

        if (incrementalSorting)
        {
            var mr = go.GetComponent<MeshRenderer>();
            if (mr)
            {
                int baseOrder = style ? style.sortingOrder : sortingOrder;
                if (_nextSortingOrder < baseOrder) _nextSortingOrder = baseOrder;
                mr.sortingOrder = _nextSortingOrder++;
                if (_nextSortingOrder - baseOrder > sortingOrderWrapRange)
                    _nextSortingOrder = baseOrder;
            }
        }

        var cam = targetCamera;
        var basePos = position;
        if (cam) basePos -= cam.transform.forward * cameraForwardOffset;
        go.transform.position = position;

        var dur = Mathf.Max(0.05f, style ? style.duration : duration);
        var rise = style ? style.riseDistance : riseDistance;
        var fadeIn = Mathf.Clamp01(style ? style.fadeInFraction : 0.2f) * dur;
        var fadeOut = Mathf.Clamp01(style ? style.fadeOutFraction : 0.25f) * dur;
        var popFrom = style ? style.popFromScale : 0.6f;
        var popTo = style ? style.popToScale : 1.1f;
        var useUnscaled = style ? style.useUnscaledTime : useUnscaledTime;

        float popTime = Mathf.Clamp(popDurationFraction, 0.05f, 0.5f) * dur;
        float delay = Mathf.Min(popDelaySeconds, dur - 0.05f);
        float shrinkTime = Mathf.Max(0.0001f, dur - (delay + popTime));
        float endScale = popTo * endShrinkScaleMultiplier;

        float tiltZ = 0f;
        go.transform.localScale = Vector3.one * (popFrom * sizeScale);

        var seq = DOTween.Sequence().SetUpdate(useUnscaled).SetRecyclable(true);
        seq.Join(go.transform.DOMoveY(basePos.y + rise, dur).SetEase(Ease.OutCubic));
        seq.Insert(0f, tmp.DOFade(1f, fadeIn).SetEase(Ease.OutCubic));
        seq.Insert(dur - fadeOut, tmp.DOFade(0f, fadeOut).SetEase(Ease.InCubic));

        // POP after delay
        seq.Insert(delay, go.transform.DOScale(popTo * sizeScale, popTime).SetEase(Ease.OutCubic));

        // Shrink only if endShrink < 1
        if (endShrinkScaleMultiplier < 0.999f)
            seq.Insert(delay + popTime, go.transform.DOScale(endScale * sizeScale, shrinkTime).SetEase(Ease.InQuad));

        // Z-axis shake (starts with pop)
        if (enableTilt && tiltAngle > 0f)
        {
            float tiltTime = Mathf.Clamp(tiltDurationFraction, 0.02f, 0.9f) * dur;
            float effectiveTiltTime = Mathf.Min(tiltTime, dur - delay);
            var tiltSeq = DOTween.Sequence().SetUpdate(useUnscaled);
            tiltSeq.Append(DOVirtual.Float(0f, tiltAngle, effectiveTiltTime * 0.33f, v => tiltZ = v).SetEase(Ease.OutCubic));
            tiltSeq.Append(DOVirtual.Float(tiltAngle, -tiltAngle, effectiveTiltTime * 0.33f, v => tiltZ = v).SetEase(Ease.InOutCubic));
            tiltSeq.Append(DOVirtual.Float(-tiltAngle, 0f, effectiveTiltTime * 0.34f, v => tiltZ = v).SetEase(Ease.InCubic));
            seq.Insert(delay, tiltSeq);
        }

        seq.OnUpdate(() =>
        {
            if (!targetCamera) return;
            var face = Quaternion.LookRotation(targetCamera.transform.forward, Vector3.up);
            go.transform.rotation = face * Quaternion.Euler(0f, 0f, tiltZ);
        });

        seq.OnComplete(() =>
        {
            ResetVisual(go);
            pool.Release(go);
        });
    }

    private void OnGet(GameObject go) => go.SetActive(true);
    private void OnRelease(GameObject go)
    {
        ResetVisual(go);
        go.SetActive(false);
    }
    private void OnDestroyPooled(GameObject go)
    {
        if (go) Destroy(go);
    }

    private void ResetVisual(GameObject go)
    {
        var tmp = go.GetComponent<TextMeshPro>();
        float baseSize = style ? style.baseFontSize : baseFontSize;
        tmp.fontSize = baseSize;
        tmp.enableAutoSizing = false;
        tmp.text = string.Empty;
        var c = tmp.color; c.a = 0f; tmp.color = c;
        go.transform.localScale = Vector3.one;
        go.transform.localRotation = Quaternion.identity;
    }
}