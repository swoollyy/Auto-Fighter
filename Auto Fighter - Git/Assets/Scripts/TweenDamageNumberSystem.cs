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


    [SerializeField] private int defaultCapacity = 32;
    [SerializeField] private int maxSize = 256;

    private IObjectPool<GameObject> pool;


    void Awake()
    {
        if(targetCamera == null)
        {
            targetCamera = Camera.main;
        }
        DamageNumbers.Register(this);

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

        var useFont = style != null ? style.font : font;
        var useMat = style != null ? style.fontMaterial : fontMaterial;
        var useSize = style != null ? style.baseFontSize : baseFontSize;
        var useColor = style != null ? style.defaultColor : defaultColor;

        if (useFont != null) tmp.font = useFont;
        if (useMat != null) tmp.fontSharedMaterial = useMat;
        tmp.fontSize = useSize;

        var mr = go.GetComponent<MeshRenderer>();
        var layerName = style != null ? style.sortingLayerName : sortingLayerName;
        var order = style != null ? style.sortingOrder : sortingOrder;
        if (mr != null) { mr.sortingLayerName = layerName; mr.sortingOrder = order; }
        tmp.color = new Color(useColor.r, useColor.g, useColor.b, 0f);

        go.SetActive(false);
        return go;
    }

    private void OnGet(GameObject go) => go.SetActive(true);

    private void OnRelease(GameObject go)
    {
        var tmp = go.GetComponent<TextMeshPro>();
        tmp.text = string.Empty;
        var c = tmp.color; c.a = 0f; tmp.color = c;
        go.transform.localScale = Vector3.one;
        go.SetActive(false);
    }

    private void OnDestroyPooled(GameObject go)
    {
        if(go != null) Destroy(go);
    }




    public void Spawn(float amount, Vector3 position, Color? overrideColor = null)
    {
        var go = pool.Get();
        DOTween.Kill(go.transform, complete: false);
        var tmp = go.GetComponent<TextMeshPro>();
        DOTween.Kill(tmp, complete: false);

        var cam = targetCamera;



        tmp.text = amount.ToString();
        var col = overrideColor ?? defaultColor;
        tmp.color = new Color(col.r, col.g, col.b, 0f);

        var basePos = position;
        if(cam != null) basePos -= cam.transform.forward * cameraForwardOffset;

        go.transform.position = position;
        if(cam != null) go.transform.rotation = Quaternion.LookRotation(cam.transform.forward, Vector3.up);

        var dur = Mathf.Max(0.05f, (style != null ? style.duration : duration));
        var rise = (style != null ? style.riseDistance : riseDistance);
        var fadeIn = Mathf.Clamp01(style != null ? style.fadeInFraction : 0.08f) * dur;
        var fadeOut = Mathf.Clamp01(style != null ? style.fadeOutFraction : 0.25f) * dur;
        var popFrom = (style != null ? style.popFromScale : 0.6f);
        var popTo = (style != null ? style.popToScale : 1.1f);
        var useUnscaled = (style != null ? style.useUnscaledTime : useUnscaledTime);

        DOTween.Sequence()
            .SetUpdate(useUnscaled)
            .SetRecyclable(true)
            .Join(go.transform.DOMoveY(basePos.y + rise, dur).SetEase(Ease.OutCubic))
            .Insert(0f, tmp.DOFade(1f, fadeIn).SetEase(Ease.OutCubic))
            .Insert(dur - fadeOut, tmp.DOFade(0f, fadeOut).SetEase(Ease.InCubic))
            .Join(go.transform.DOScale(popTo, fadeIn).From(popFrom).SetEase(Ease.OutBack, 2f))
            .OnComplete(() => pool.Release(go));

    }

}
