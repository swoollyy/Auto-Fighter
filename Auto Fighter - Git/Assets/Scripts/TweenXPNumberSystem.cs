using UnityEngine;
using TMPro;
using DG.Tweening;
using UnityEngine.Pool;

public class TweenXPNumberSystem : MonoBehaviour, IXPNumberSystem
{
    [Header("Basics")]
    [SerializeField] private Camera targetCamera;
    [SerializeField] private XPNumberStyleSO style;
    [SerializeField] private float cameraForwardOffset = 0.02f;

    [Header("Fallbacks")]
    [SerializeField] private TMP_FontAsset font;
    [SerializeField] private Material fontMaterial;
    [SerializeField] private float baseFontSize = 3.5f;
    [SerializeField] private Color defaultColor = new Color(0.4f, 0.9f, 1f, 1f);

    [Header("Render")]
    [SerializeField] private string sortingLayerName = "Default";
    [SerializeField] private int sortingOrder = 600;
    [SerializeField] private bool incrementalSorting = true;
    [SerializeField] private int sortingOrderWrapRange = 5000;
    private int _nextSortingOrder;

    [Header("Outline")]
    [SerializeField] private bool enableOutline = true;
    [SerializeField, Range(0f, 1f)] private float outlineWidth = 0.35f;
    [SerializeField] private Color outlineColor = Color.black;
    [SerializeField, Range(0f, 1f)] private float faceDilate = 0.05f;

    [Header("Pop & Shrink Behaviour")]
    [Tooltip("Seconds to wait before doing the fast pop.")]
    [SerializeField, Range(0f, 0.25f)] private float popDelaySeconds = 0.08f;
    [Tooltip("Fraction of total duration used for the pop tween.")]
    [SerializeField, Range(0.05f, 0.5f)] private float popDurationFraction = 0.15f;
    [Tooltip("End scale multiplier relative to popped size (1 = hold).")]
    [SerializeField, Range(0.1f, 1f)] private float endShrinkScaleMultiplier = 0.75f;

    [Header("Z Axis Shake")]
    [SerializeField, Range(0f, 45f)] private float tiltAngle = 10f;
    [SerializeField, Range(0.02f, 0.6f)] private float tiltDurationFraction = 0.15f;
    [SerializeField] private bool enableTilt = true;

    [Header("Pool")]
    [SerializeField] private int defaultCapacity = 24;
    [SerializeField] private int maxSize = 128;

    private IObjectPool<GameObject> pool;

    void Awake()
    {
        if (!targetCamera) targetCamera = Camera.main;
        XPNumbers.Register(this);
        _nextSortingOrder = style ? style.sortingOrder : sortingOrder;
        pool = new ObjectPool<GameObject>(Create, OnGet, OnRelease, OnDestroyPooled, true, defaultCapacity, maxSize);
        Prewarm(24);
    }

    public void Spawn(int amount, Vector3 position, Color? overrideColor = null)
    {
        var go = pool.Get();
        DOTween.Kill(go.transform, false);
        var tmp = go.GetComponent<TextMeshPro>();
        DOTween.Kill(tmp, false);
        EnsureTMPOutline(tmp);

        tmp.text = $"+{amount} XP";
        Color useColor = overrideColor ?? (style ? style.defaultColor : defaultColor);
        tmp.color = new Color(useColor.r, useColor.g, useColor.b, 0f);

        float size = style ? style.baseFontSize : baseFontSize;
        tmp.fontSize = size;

        if (incrementalSorting)
        {
            var mr = go.GetComponent<MeshRenderer>();
            if (mr)
            {
                int baseOrder = style ? style.sortingOrder : sortingOrder;
                if (_nextSortingOrder < baseOrder) _nextSortingOrder = baseOrder;
                mr.sortingLayerName = style ? style.sortingLayerName : sortingLayerName;
                mr.sortingOrder = _nextSortingOrder++;
                if (_nextSortingOrder - baseOrder > sortingOrderWrapRange)
                    _nextSortingOrder = baseOrder;
            }
        }

        var cam = targetCamera;
        var basePos = position;
        if (cam) basePos -= cam.transform.forward * cameraForwardOffset;
        go.transform.position = basePos;

        float dur = Mathf.Max(0.05f, style ? style.duration : 0.8f);
        float fadeIn = Mathf.Clamp01(style ? style.fadeInFraction : 0.08f) * dur;
        float fadeOut = Mathf.Clamp01(style ? style.fadeOutFraction : 0.22f) * dur;
        float rise = style ? style.riseDistance : 1.0f;
        float fromS = style ? style.popFromScale : 0.6f;
        float toS = style ? style.popToScale : 1.05f;
        bool unscaled = style ? style.useUnscaledTime : false;

        float popTime = Mathf.Clamp(popDurationFraction, 0.05f, 0.5f) * dur;
        float delay = Mathf.Min(popDelaySeconds, dur - 0.05f);
        float shrinkTime = Mathf.Max(0.0001f, dur - (delay + popTime));
        float endScale = toS * endShrinkScaleMultiplier;

        float tiltZ = 0f;
        go.transform.localScale = Vector3.one * fromS;

        var seq = DOTween.Sequence().SetUpdate(unscaled).SetRecyclable(true);
        seq.Join(go.transform.DOMoveY(basePos.y + rise, dur).SetEase(Ease.OutCubic));
        seq.Insert(0f, tmp.DOFade(1f, fadeIn).SetEase(Ease.OutCubic));
        seq.Insert(dur - fadeOut, tmp.DOFade(0f, fadeOut).SetEase(Ease.InCubic));

        seq.Insert(delay, go.transform.DOScale(toS, popTime).SetEase(Ease.OutCubic));
        if (endShrinkScaleMultiplier < 0.999f)
            seq.Insert(delay + popTime, go.transform.DOScale(endScale, shrinkTime).SetEase(Ease.InQuad));

        if (enableTilt && tiltAngle > 0f)
        {
            float tiltTime = Mathf.Clamp(tiltDurationFraction, 0.02f, 0.9f) * dur;
            float effectiveTiltTime = Mathf.Min(tiltTime, dur - delay);
            var tiltSeq = DOTween.Sequence().SetUpdate(unscaled);
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
            OnRelease(go);
            pool.Release(go);
        });
    }

    public void SpawnFollow(int amount, Transform follow, Vector3 worldOffset, Color? overrideColor = null)
    {
        if (!follow) { Spawn(amount, Vector3.zero, overrideColor); return; }

        var go = pool.Get();
        DOTween.Kill(go.transform, false);
        var tmp = go.GetComponent<TextMeshPro>();
        DOTween.Kill(tmp, false);
        EnsureTMPOutline(tmp);

        tmp.text = $"+{amount}";
        Color useColor = overrideColor ?? (style ? style.defaultColor : defaultColor);
        tmp.color = new Color(useColor.r, useColor.g, useColor.b, 0f);

        float size = style ? style.baseFontSize : baseFontSize;
        tmp.fontSize = size;

        if (incrementalSorting)
        {
            var mr = go.GetComponent<MeshRenderer>();
            if (mr)
            {
                int baseOrder = style ? style.sortingOrder : sortingOrder;
                if (_nextSortingOrder < baseOrder) _nextSortingOrder = baseOrder;
                mr.sortingLayerName = style ? style.sortingLayerName : sortingLayerName;
                mr.sortingOrder = _nextSortingOrder++;
                if (_nextSortingOrder - baseOrder > sortingOrderWrapRange)
                    _nextSortingOrder = baseOrder;
            }
        }

        var cam = targetCamera;
        float dur = Mathf.Max(0.05f, style ? style.duration : 0.8f);
        float fadeIn = Mathf.Clamp01(style ? style.fadeInFraction : 0.08f) * dur;
        float fadeOut = Mathf.Clamp01(style ? style.fadeOutFraction : 0.22f) * dur;
        float rise = style ? style.riseDistance : 1.0f;
        float fromS = style ? style.popFromScale : 0.6f;
        float toS = style ? style.popToScale : 1.05f;
        bool unscaled = style ? style.useUnscaledTime : false;

        float popTime = Mathf.Clamp(popDurationFraction, 0.05f, 0.5f) * dur;
        float delay = Mathf.Min(popDelaySeconds, dur - 0.05f);
        float shrinkTime = Mathf.Max(0.0001f, dur - (delay + popTime));
        float endScale = toS * endShrinkScaleMultiplier;

        Vector3 baseOrigin = follow.position + worldOffset;
        float followWeight = 1f;
        float riseAmt = 0f;
        float tiltZ = 0f;

        Vector3 basePos = follow.position + worldOffset;
        if (cam) basePos -= cam.transform.forward * cameraForwardOffset;
        go.transform.position = basePos;
        go.transform.localScale = Vector3.one * fromS;

        var seq = DOTween.Sequence().SetUpdate(unscaled).SetRecyclable(true);
        seq.Join(DOVirtual.Float(0f, rise, dur, v => riseAmt = v).SetEase(Ease.OutCubic));
        seq.Join(DOVirtual.Float(1f, 0f, dur, v => followWeight = v).SetEase(Ease.InCubic));

        seq.Insert(0f, tmp.DOFade(1f, fadeIn).SetEase(Ease.OutCubic));
        seq.Insert(dur - fadeOut, tmp.DOFade(0f, fadeOut).SetEase(Ease.InCubic));

        seq.Insert(delay, go.transform.DOScale(toS, popTime).SetEase(Ease.OutCubic));
        if (endShrinkScaleMultiplier < 0.999f)
            seq.Insert(delay + popTime, go.transform.DOScale(endScale, shrinkTime).SetEase(Ease.InQuad));

        if (enableTilt && tiltAngle > 0f)
        {
            float tiltTime = Mathf.Clamp(tiltDurationFraction, 0.02f, 0.9f) * dur;
            float effectiveTiltTime = Mathf.Min(tiltTime, dur - delay);
            var tiltSeq = DOTween.Sequence().SetUpdate(unscaled);
            tiltSeq.Append(DOVirtual.Float(0f, tiltAngle, effectiveTiltTime * 0.33f, v => tiltZ = v).SetEase(Ease.OutCubic));
            tiltSeq.Append(DOVirtual.Float(tiltAngle, -tiltAngle, effectiveTiltTime * 0.33f, v => tiltZ = v).SetEase(Ease.InOutCubic));
            tiltSeq.Append(DOVirtual.Float(-tiltAngle, 0f, effectiveTiltTime * 0.34f, v => tiltZ = v).SetEase(Ease.InCubic));
            seq.Insert(delay, tiltSeq);
        }

        seq.OnUpdate(() =>
        {
            var unfollowPos = baseOrigin + Vector3.up * riseAmt;
            var targetFollowPos = follow.position + worldOffset + Vector3.up * riseAmt;
            var pos = Vector3.Lerp(unfollowPos, targetFollowPos, followWeight);
            if (targetCamera) pos -= targetCamera.transform.forward * cameraForwardOffset;
            go.transform.position = pos;

            if (targetCamera)
            {
                var face = Quaternion.LookRotation(targetCamera.transform.forward, Vector3.up);
                go.transform.rotation = face * Quaternion.Euler(0f, 0f, tiltZ);
            }
        })
        .OnComplete(() =>
        {
            OnRelease(go);
            pool.Release(go);
        });
    }

    void Prewarm(int count)
    {
        var arr = new GameObject[count];
        for (int i = 0; i < count; i++) arr[i] = pool.Get();
        for (int i = 0; i < count; i++) pool.Release(arr[i]);
    }

    GameObject Create()
    {
        var go = new GameObject("XPNumber", typeof(TextMeshPro));
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
        tmp.color = new Color(useColor.r, useColor.g, useColor.b, 0f);

        var mr = go.GetComponent<MeshRenderer>();
        var layer = style ? style.sortingLayerName : sortingLayerName;
        var order = style ? style.sortingOrder : sortingOrder;
        if (mr)
        {
            mr.sortingLayerName = layer;
            mr.sortingOrder = order;
        }

        go.SetActive(false);
        return go;
    }

    private static bool IsTMPMaterial(Material m) => m && m.shader && m.shader.name.StartsWith("TextMeshPro/");
    private void EnsureTMPOutline(TextMeshPro tmp)
    {
        if (!tmp) return;
        if (!IsTMPMaterial(tmp.fontMaterial))
        {
            if (tmp.font && IsTMPMaterial(tmp.font.material))
                tmp.fontSharedMaterial = tmp.font.material;
            tmp.fontMaterial = new Material(tmp.fontSharedMaterial);
        }
        ApplyOutlineAndRefresh(tmp);
    }
    private void ApplyOutlineAndRefresh(TextMeshPro tmp)
    {
        if (!enableOutline) return;
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

    void OnGet(GameObject go) => go.SetActive(true);
    void OnRelease(GameObject go)
    {
        var tmp = go.GetComponent<TextMeshPro>();
        tmp.text = string.Empty;
        var c = tmp.color; c.a = 0f; tmp.color = c;
        go.transform.localScale = Vector3.one;
        go.transform.localRotation = Quaternion.identity;
        go.transform.SetParent(transform, false);
        go.SetActive(false);
    }
    void OnDestroyPooled(GameObject go) { if (go) Destroy(go); }
}