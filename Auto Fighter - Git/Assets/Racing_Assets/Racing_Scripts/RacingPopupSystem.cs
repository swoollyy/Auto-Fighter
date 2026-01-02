using DG.Tweening;
using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Pool;

/// <summary>
/// Main popup text system for racing game.
/// Handles pooling, styling, and animation of floating text popups.
/// Supports comic-style /\ positioning with HP left, Fuel right.
/// </summary>
public class RacingPopupSystem : MonoBehaviour, IRacingPopupSystem
{
    [Header("Camera")]
    [SerializeField] private Camera targetCamera;
    [SerializeField] private float cameraForwardOffset = 0.1f;

    [Header("Camera-Relative Spawning")]
    [Tooltip("If true, popups are parented to camera and move with it.")]
    [SerializeField] private bool attachToCamera = true;

    [Tooltip("Distance in front of camera to spawn popups.")]
    [SerializeField] private float cameraSpawnDistance = 5f;

    [Tooltip("Base vertical offset from camera center.")]
    [SerializeField] private float cameraVerticalOffset = -0.5f;

    [Header("Styles")]
    [Tooltip("List of styles for each popup type. Order doesn't matter.")]
    [SerializeField] private List<RacingPopupStyleSO> styles = new();

    [Header("Default Style (fallback)")]
    [SerializeField] private TMP_FontAsset defaultFont;
    [SerializeField] private Material defaultFontMaterial;
    [SerializeField] private TMP_SpriteAsset defaultSpriteAsset;
    [SerializeField] private float defaultFontSize = 5f;
    [SerializeField] private Color defaultColor = Color.white;
    [SerializeField] private float defaultDuration = 1f;
    [SerializeField] private float defaultRiseDistance = 1.5f;

    [Header("Default Outline")]
    [SerializeField] private bool defaultEnableOutline = true;
    [SerializeField, Range(0f, 1f)] private float defaultOutlineWidth = 0.3f;
    [SerializeField] private Color defaultOutlineColor = Color.black;
    [SerializeField, Range(0f, 0.5f)] private float defaultFaceDilate = 0.05f;

    [Header("Default Animation")]
    [SerializeField] private float defaultPopFromScale = 0.3f;
    [SerializeField] private float defaultPopToScale = 1.3f;
    [SerializeField] private float defaultEndScale = 1.0f;
    [SerializeField, Range(0f, 0.3f)] private float defaultPopDelay = 0.02f;
    [SerializeField, Range(0.05f, 0.4f)] private float defaultPopDurationFraction = 0.12f;

    [SerializeField, Range(0.01f, 0.5f)] private float defaultFadeInFraction = 0.1f;
    [SerializeField, Range(0.1f, 0.8f)] private float defaultFadeOutFraction = 0.3f;

    [Header("Rendering")]
    [SerializeField] private string defaultSortingLayer = "UI";
    [SerializeField] private int baseSortingOrder = 1000;
    [SerializeField] private bool incrementalSorting = true;
    [SerializeField] private int sortingWrapRange = 5000;

    [Header("Pool")]
    [SerializeField] private int poolDefaultCapacity = 32;
    [SerializeField] private int poolMaxSize = 128;
    [SerializeField] private int prewarmCount = 16;

    [Header("Audio")]
    [SerializeField] private AudioSource audioSource;

    // Internal
    private readonly Dictionary<RacingPopupType, RacingPopupStyleSO> _styleMap = new();
    private IObjectPool<GameObject> _pool;
    private int _nextSortingOrder;

    void Awake()
    {
        if (!targetCamera) targetCamera = Camera.main;

        // Build style lookup
        _styleMap.Clear();
        foreach (var style in styles)
        {
            if (style && !_styleMap.ContainsKey(style.popupType))
                _styleMap[style.popupType] = style;
        }

        // Create pool
        _pool = new ObjectPool<GameObject>(
            CreatePooledObject,
            OnGetFromPool,
            OnReleaseToPool,
            OnDestroyPooled,
            true,
            poolDefaultCapacity,
            poolMaxSize
        );

        // Prewarm
        Prewarm(prewarmCount);

        _nextSortingOrder = baseSortingOrder;

        // Register with static facade
        RacingPopups.Register(this);
    }

    void OnDestroy()
    {
        RacingPopups.Unregister(this);
    }

    private void Prewarm(int count)
    {
        var temp = new GameObject[count];
        for (int i = 0; i < count; i++)
            temp[i] = _pool.Get();
        for (int i = 0; i < count; i++)
            _pool.Release(temp[i]);
    }

    private GameObject CreatePooledObject()
    {
        var go = new GameObject("RacingPopup", typeof(TextMeshPro));
        go.transform.SetParent(transform, false);

        var tmp = go.GetComponent<TextMeshPro>();
        tmp.enableWordWrapping = false;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.enableAutoSizing = false;
        tmp.extraPadding = true;

        if (defaultFont) tmp.font = defaultFont;
        if (defaultSpriteAsset) tmp.spriteAsset = defaultSpriteAsset;

        // Setup material
        Material baseMat = null;
        if (IsTMPMaterial(defaultFontMaterial))
            baseMat = defaultFontMaterial;
        else if (tmp.font)
            baseMat = tmp.font.material;

        if (baseMat)
            tmp.fontSharedMaterial = baseMat;

        tmp.fontMaterial = new Material(tmp.fontSharedMaterial);
        tmp.fontSize = defaultFontSize;

        var mr = go.GetComponent<MeshRenderer>();
        if (mr)
        {
            mr.sortingLayerName = defaultSortingLayer;
            mr.sortingOrder = baseSortingOrder;
        }

        tmp.color = new Color(defaultColor.r, defaultColor.g, defaultColor.b, 0f);
        go.SetActive(false);

        return go;
    }

    private static bool IsTMPMaterial(Material m)
        => m && m.shader && m.shader.name.StartsWith("TextMeshPro/");

    private void OnGetFromPool(GameObject go) => go.SetActive(true);

    private void OnReleaseToPool(GameObject go)
    {
        ResetPopup(go);
        go.transform.SetParent(transform, false); // Return to pool parent
        go.SetActive(false);
    }

    private void OnDestroyPooled(GameObject go)
    {
        if (go) Destroy(go);
    }

    private void ResetPopup(GameObject go)
    {
        var tmp = go.GetComponent<TextMeshPro>();
        if (tmp)
        {
            tmp.text = string.Empty;
            var c = tmp.color;
            c.a = 0f;
            tmp.color = c;
            tmp.fontSize = defaultFontSize;
        }
        go.transform.localScale = Vector3.one;
        go.transform.localRotation = Quaternion.identity;
    }

    // === PUBLIC SPAWN METHODS ===

    public void Spawn(RacingPopupType type, float value, Vector3 worldPosition)
    {
        SpawnInternal(type, value, null, worldPosition, null, null);
    }

    public void Spawn(RacingPopupType type, string text, Vector3 worldPosition)
    {
        SpawnInternal(type, 0f, text, worldPosition, null, null);
    }

    public void Spawn(RacingPopupType type, float value, Vector3 worldPosition, Color colorOverride)
    {
        SpawnInternal(type, value, null, worldPosition, colorOverride, null);
    }

    public void Spawn(RacingPopupType type, float value, Vector3 worldPosition, Color? colorOverride, float? scaleOverride)
    {
        SpawnInternal(type, value, null, worldPosition, colorOverride, scaleOverride);
    }

    /// <summary>
    /// Spawn at random screen position (for mash rewards, etc.)
    /// </summary>
    public void SpawnRandomScreen(RacingPopupType type, float value, Vector2 horizontalRange, Vector2 verticalRange)
    {
        Vector3 randomOffset = new Vector3(
            UnityEngine.Random.Range(horizontalRange.x, horizontalRange.y),
            UnityEngine.Random.Range(verticalRange.x, verticalRange.y),
            0f
        );
        SpawnInternal(type, value, null, randomOffset, null, null, true);
    }

    /// <summary>
    /// Spawn a coin popup with separate text color and outline color.
    /// </summary>
    public void SpawnCoin(int value, Vector3 worldPosition, Color textColor, Color outlineColor)
    {
        var go = _pool.Get();
        DOTween.Kill(go.transform, false);

        var tmp = go.GetComponent<TextMeshPro>();
        DOTween.Kill(tmp, false);

        // Get coin style
        _styleMap.TryGetValue(RacingPopupType.CoinGain, out var style);

        // Apply font
        var useFont = style?.font ?? defaultFont;
        if (useFont) tmp.font = useFont;

        // Apply sprite asset
        var useSpriteAsset = style?.spriteAsset ?? defaultSpriteAsset;
        if (useSpriteAsset) tmp.spriteAsset = useSpriteAsset;

        // Apply material
        var useMat = style?.fontMaterial ?? defaultFontMaterial;
        Material baseMat = null;
        if (IsTMPMaterial(useMat))
            baseMat = useMat;
        else if (tmp.font)
            baseMat = tmp.font.material;

        if (baseMat)
        {
            tmp.fontSharedMaterial = baseMat;
            tmp.fontMaterial = new Material(baseMat);
        }

        // Apply outline with custom color
        ApplyOutlineWithColor(tmp, style, outlineColor);

        // Set text
        if (style != null)
            tmp.text = style.FormatText(value);
        else
            tmp.text = $"+{value}";

        // Use custom text color (start invisible for fade-in)
        tmp.color = new Color(textColor.r, textColor.g, textColor.b, 0f);

        // Font size with value scaling
        float baseFontSize = style?.baseFontSize ?? defaultFontSize;
        float valueScaleMult = style?.ComputeScaleMultiplier(value) ?? 1f;
        tmp.fontSize = baseFontSize * valueScaleMult;

        // Sorting
        if (incrementalSorting)
        {
            var mr = go.GetComponent<MeshRenderer>();
            if (mr)
            {
                int baseOrder = style?.sortingOrder ?? baseSortingOrder;
                string layer = style?.sortingLayerName ?? defaultSortingLayer;
                mr.sortingLayerName = layer;

                if (_nextSortingOrder < baseOrder) _nextSortingOrder = baseOrder;
                mr.sortingOrder = _nextSortingOrder++;

                if (_nextSortingOrder - baseOrder > sortingWrapRange)
                    _nextSortingOrder = baseOrder;
            }
        }

        // Position
        Vector3 positionOffset = style?.GetPositionOffset() ?? Vector3.zero;

        if (attachToCamera && targetCamera)
        {
            go.transform.SetParent(targetCamera.transform, false);
            Vector3 localPos = Vector3.forward * cameraSpawnDistance
                             + Vector3.up * cameraVerticalOffset
                             + Vector3.right * positionOffset.x
                             + Vector3.up * positionOffset.y;
            go.transform.localPosition = localPos;
        }
        else
        {
            go.transform.SetParent(transform, false);
            Vector3 basePos = worldPosition + positionOffset;
            if (targetCamera)
                basePos -= targetCamera.transform.forward * cameraForwardOffset;
            go.transform.position = basePos;
        }

        go.SetActive(true);

        // Animation
        float duration = style?.duration ?? defaultDuration;
        float fadeIn = style?.fadeInFraction ?? defaultFadeInFraction;
        float fadeOut = style?.fadeOutFraction ?? defaultFadeOutFraction;
        float riseDistance = style?.riseDistance ?? defaultRiseDistance;
        var riseEase = style?.riseEase ?? Ease.OutCubic;
        float popFrom = style?.popFromScale ?? defaultPopFromScale;
        float popTo = style?.popToScale ?? defaultPopToScale;
        float endScale = style?.endScale ?? defaultEndScale;
        float popDelay = style?.popDelay ?? defaultPopDelay;
        float popDurFrac = style?.popDurationFraction ?? defaultPopDurationFraction;
        var popEase = style?.popEase ?? Ease.OutBack;
        float fixedRotZ = style?.fixedRotationZ ?? 0f;

        var seq = DOTween.Sequence().SetTarget(go.transform);
        if (style?.useUnscaledTime ?? true)
            seq.SetUpdate(true);

        // Fade in
        seq.Append(DOTween.ToAlpha(
            () => tmp.color, c => tmp.color = c, 1f, duration * fadeIn
        ).SetEase(Ease.OutQuad));

        // Hold then fade out
        float holdTime = duration * (1f - fadeIn - fadeOut);
        seq.AppendInterval(holdTime);
        seq.Append(DOTween.ToAlpha(
            () => tmp.color, c => tmp.color = c, 0f, duration * fadeOut
        ).SetEase(Ease.InQuad));

        // Rise motion
        if (attachToCamera && targetCamera)
        {
            Vector3 startLocal = go.transform.localPosition;
            Vector3 endLocal = startLocal + Vector3.up * riseDistance;
            seq.Join(go.transform.DOLocalMove(endLocal, duration).SetEase(riseEase));
        }
        else
        {
            Vector3 endPos = go.transform.position + Vector3.up * riseDistance;
            seq.Join(go.transform.DOMove(endPos, duration).SetEase(riseEase));
        }

        // Pop scale
        go.transform.localScale = Vector3.one * popFrom;
        var scaleSeq = DOTween.Sequence().SetTarget(go.transform);
        scaleSeq.AppendInterval(popDelay);
        scaleSeq.Append(go.transform.DOScale(popTo, duration * popDurFrac).SetEase(popEase));
        scaleSeq.Append(go.transform.DOScale(endScale, duration * 0.2f).SetEase(Ease.InOutQuad));

        // Rotation
        float dynamicTiltZ = 0f;
        seq.OnUpdate(() =>
        {
            if (attachToCamera && targetCamera)
            {
                go.transform.localRotation = Quaternion.Euler(0f, 0f, fixedRotZ + dynamicTiltZ);
            }
            else
            {
                if (!targetCamera) return;
                var face = Quaternion.LookRotation(targetCamera.transform.forward, Vector3.up);
                go.transform.rotation = face * Quaternion.Euler(0f, 0f, fixedRotZ + dynamicTiltZ);
            }
        });

        seq.OnComplete(() =>
        {
            go.SetActive(false);
            _pool.Release(go);
        });
    }

    private void ApplyOutlineWithColor(TextMeshPro tmp, RacingPopupStyleSO style, Color outlineColor)
    {
        bool enable = style?.enableOutline ?? defaultEnableOutline;
        if (!enable) return;

        var mat = tmp.fontMaterial;
        if (!mat) return;

        mat.EnableKeyword("OUTLINE_ON");

        float width = style?.outlineWidth ?? defaultOutlineWidth;
        float dilate = style?.faceDilate ?? defaultFaceDilate;

        if (mat.HasProperty(ShaderUtilities.ID_OutlineWidth))
            mat.SetFloat(ShaderUtilities.ID_OutlineWidth, width);

        if (mat.HasProperty(ShaderUtilities.ID_OutlineColor))
        {
            if (outlineColor.a <= 0f) outlineColor.a = 1f;

            float intensity = style?.outlineIntensity ?? 3f;
            Color hdrColor = outlineColor * intensity;
            hdrColor.a = outlineColor.a;

            mat.SetColor(ShaderUtilities.ID_OutlineColor, hdrColor);
        }

        if (mat.HasProperty(ShaderUtilities.ID_FaceDilate))
            mat.SetFloat(ShaderUtilities.ID_FaceDilate, dilate);

        tmp.extraPadding = true;
        tmp.UpdateMeshPadding();
        tmp.SetMaterialDirty();
    }
    private void SpawnInternal(RacingPopupType type, float value, string customText, Vector3 worldPosition, Color? colorOverride, float? scaleOverride, bool useRandomScreenPos = false)
    {
        var go = _pool.Get();
        DOTween.Kill(go.transform, false);

        var tmp = go.GetComponent<TextMeshPro>();
        DOTween.Kill(tmp, false);

        // Get style (or use defaults)
        _styleMap.TryGetValue(type, out var style);

        // Apply font
        var useFont = style?.font ?? defaultFont;
        if (useFont) tmp.font = useFont;

        // Apply sprite asset (style-specific first, then fallback to default)
        var useSpriteAsset = style?.spriteAsset ?? defaultSpriteAsset;
        if (useSpriteAsset) tmp.spriteAsset = useSpriteAsset;

        // Apply material
        var useMat = style?.fontMaterial ?? defaultFontMaterial;
        Material baseMat = null;
        if (IsTMPMaterial(useMat))
            baseMat = useMat;
        else if (tmp.font)
            baseMat = tmp.font.material;

        if (baseMat)
        {
            tmp.fontSharedMaterial = baseMat;
            tmp.fontMaterial = new Material(baseMat);
        }

        // Apply outline
        ApplyOutline(tmp, style);

        // Set text
        if (!string.IsNullOrEmpty(customText))
        {
            tmp.text = customText;
        }
        else if (style != null)
        {
            tmp.text = style.FormatText(value);
        }
        else
        {
            tmp.text = value.ToString("0.#");
        }

        // Color
        var useColor = colorOverride ?? style?.textColor ?? defaultColor;
        tmp.color = new Color(useColor.r, useColor.g, useColor.b, 0f);

        // Font size with value scaling
        float baseFontSize = style?.baseFontSize ?? defaultFontSize;
        float valueScaleMult = style?.ComputeScaleMultiplier(value) ?? 1f;
        float finalScale = scaleOverride ?? valueScaleMult;
        tmp.fontSize = baseFontSize * finalScale;

        // Sorting
        if (incrementalSorting)
        {
            var mr = go.GetComponent<MeshRenderer>();
            if (mr)
            {
                int baseOrder = style?.sortingOrder ?? baseSortingOrder;
                string layer = style?.sortingLayerName ?? defaultSortingLayer;
                mr.sortingLayerName = layer;

                if (_nextSortingOrder < baseOrder) _nextSortingOrder = baseOrder;
                mr.sortingOrder = _nextSortingOrder++;

                if (_nextSortingOrder - baseOrder > sortingWrapRange)
                    _nextSortingOrder = baseOrder;
            }
        }



        // === FIXED ROTATION (Comic Style /\ ) ===
        float fixedRotZ = style?.fixedRotationZ ?? 0f;

        // === POSITION WITH OFFSET (Comic Style) ===
        Vector3 positionOffset = style?.GetPositionOffset() ?? Vector3.zero;

        if (attachToCamera && targetCamera)
        {
            // Parent to camera so it moves with it
            go.transform.SetParent(targetCamera.transform, false);

            // Set LOCAL position relative to camera
            Vector3 localPos = Vector3.forward * cameraSpawnDistance
                             + Vector3.up * cameraVerticalOffset
                             + Vector3.right * positionOffset.x
                             + Vector3.up * positionOffset.y;

            // Apply random screen position if requested
            if (useRandomScreenPos)
            {
                localPos += Vector3.right * worldPosition.x;
                localPos += Vector3.up * worldPosition.y;
            }

            go.transform.localPosition = localPos;
            go.transform.localRotation = Quaternion.Euler(0f, 0f, fixedRotZ);
        }
        else
        {
            // Spawn at world position (original behavior)
            go.transform.SetParent(transform, false);
            Vector3 basePos = worldPosition + positionOffset;
            if (targetCamera)
                basePos -= targetCamera.transform.forward * cameraForwardOffset;
            go.transform.position = basePos;
        }

        // Animation parameters
        float duration = style?.duration ?? defaultDuration;
        float riseDistance = style?.riseDistance ?? defaultRiseDistance;
        float fadeInFrac = style?.fadeInFraction ?? 0.1f;
        float fadeOutFrac = style?.fadeOutFraction ?? 0.3f;
        float popFrom = style?.popFromScale ?? defaultPopFromScale;
        float popTo = style?.popToScale ?? defaultPopToScale;
        float endScaleVal = style?.endScale ?? defaultEndScale;
        float popDelay = style?.popDelay ?? defaultPopDelay;
        float popDurFrac = style?.popDurationFraction ?? defaultPopDurationFraction;
        bool enableTiltShake = style?.enableTiltShake ?? false;
        float tiltShakeAngle = style?.tiltShakeAngle ?? 8f;
        float tiltShakeDurFrac = style?.tiltShakeDurationFraction ?? 0.2f;
        bool useUnscaled = style?.useUnscaledTime ?? true;
        var riseEase = style?.riseEase ?? Ease.OutCubic;
        var popEase = style?.popEase ?? Ease.OutBack;

        float fadeIn = Mathf.Clamp01(fadeInFrac) * duration;
        float fadeOut = Mathf.Clamp01(fadeOutFrac) * duration;
        float popTime = Mathf.Clamp(popDurFrac, 0.05f, 0.5f) * duration;
        float shrinkTime = Mathf.Max(0.001f, duration - (popDelay + popTime));

        // Initial scale
        go.transform.localScale = Vector3.one * (popFrom * finalScale);

        // Tilt tracking (for shake animation)
        float dynamicTiltZ = 0f;

        // Build sequence
        var seq = DOTween.Sequence().SetUpdate(useUnscaled).SetRecyclable(true);

        // Rise motion
        if (attachToCamera && targetCamera)
        {
            // Local space rise (relative to camera)
            Vector3 startLocal = go.transform.localPosition;
            Vector3 endLocal = startLocal + Vector3.up * riseDistance;
            seq.Join(go.transform.DOLocalMove(endLocal, duration).SetEase(riseEase));
        }
        else
        {
            // World space rise
            Vector3 endPos = go.transform.position + Vector3.up * riseDistance;
            seq.Join(go.transform.DOMove(endPos, duration).SetEase(riseEase));
        }

        // Fade in/out
        seq.Insert(0f, tmp.DOFade(1f, fadeIn).SetEase(Ease.OutCubic));
        seq.Insert(duration - fadeOut, tmp.DOFade(0f, fadeOut).SetEase(Ease.InCubic));

        // Pop scale (comic book punch!)
        seq.Insert(popDelay, go.transform.DOScale(popTo * finalScale, popTime).SetEase(popEase));

        // Shrink after pop (if different from pop)
        if (endScaleVal < popTo - 0.01f)
        {
            seq.Insert(popDelay + popTime, go.transform.DOScale(endScaleVal * finalScale, shrinkTime).SetEase(Ease.InQuad));
        }

        // Tilt shake animation (optional, on top of fixed rotation)
        if (enableTiltShake && tiltShakeAngle > 0f)
        {
            float tiltTime = Mathf.Clamp(tiltShakeDurFrac, 0.02f, 0.9f) * duration;
            float effectiveTiltTime = Mathf.Min(tiltTime, duration - popDelay);

            var tiltSeq = DOTween.Sequence().SetUpdate(useUnscaled);
            tiltSeq.Append(DOVirtual.Float(0f, tiltShakeAngle, effectiveTiltTime * 0.33f, v => dynamicTiltZ = v).SetEase(Ease.OutCubic));
            tiltSeq.Append(DOVirtual.Float(tiltShakeAngle, -tiltShakeAngle, effectiveTiltTime * 0.33f, v => dynamicTiltZ = v).SetEase(Ease.InOutCubic));
            tiltSeq.Append(DOVirtual.Float(-tiltShakeAngle, 0f, effectiveTiltTime * 0.34f, v => dynamicTiltZ = v).SetEase(Ease.InCubic));
            seq.Insert(popDelay, tiltSeq);
        }

        // Face camera + apply fixed rotation + dynamic tilt
        seq.OnUpdate(() =>
        {
            if (attachToCamera && targetCamera)
            {
                // Already parented to camera, just apply tilt
                go.transform.localRotation = Quaternion.Euler(0f, 0f, fixedRotZ + dynamicTiltZ);
            }
            else
            {
                if (!targetCamera) return;
                var face = Quaternion.LookRotation(targetCamera.transform.forward, Vector3.up);
                go.transform.rotation = face * Quaternion.Euler(0f, 0f, fixedRotZ + dynamicTiltZ);
            }
        });

        // Cleanup
        seq.OnComplete(() =>
        {
            ResetPopup(go);
            _pool.Release(go);
        });

        // Play sound
        if (style?.spawnSound && audioSource)
        {
            audioSource.PlayOneShot(style.spawnSound, style.soundVolume);
        }
    }

    private void ApplyOutline(TextMeshPro tmp, RacingPopupStyleSO style)
    {
        bool enable = style?.enableOutline ?? defaultEnableOutline;
        if (!enable) return;

        var mat = tmp.fontMaterial;
        if (!mat) return;

        mat.EnableKeyword("OUTLINE_ON");

        float width = style?.outlineWidth ?? defaultOutlineWidth;
        Color outColor = style?.outlineColor ?? defaultOutlineColor;
        float dilate = style?.faceDilate ?? defaultFaceDilate;

        if (mat.HasProperty(ShaderUtilities.ID_OutlineWidth))
            mat.SetFloat(ShaderUtilities.ID_OutlineWidth, width);

        if (mat.HasProperty(ShaderUtilities.ID_OutlineColor))
        {
            if (outColor.a <= 0f) outColor.a = 1f;

            // Apply HDR intensity
            float intensity = style?.outlineIntensity ?? 3f;
            Color hdrColor = outColor * intensity;
            hdrColor.a = outColor.a; // Keep original alpha

            mat.SetColor(ShaderUtilities.ID_OutlineColor, hdrColor);
        }

        if (mat.HasProperty(ShaderUtilities.ID_FaceDilate))
            mat.SetFloat(ShaderUtilities.ID_FaceDilate, dilate);


        tmp.extraPadding = true;
        tmp.UpdateMeshPadding();
        tmp.SetMaterialDirty();
    }

    // === EDITOR HELPER ===

#if UNITY_EDITOR
    [ContextMenu("Test HP Damage Popup")]
    private void TestHPDamage()
    {
        if (!Application.isPlaying) return;
        Spawn(RacingPopupType.HPDamage, 25f, transform.position + Vector3.up * 2f);
    }

    [ContextMenu("Test Fuel Loss Popup")]
    private void TestFuelLoss()
    {
        if (!Application.isPlaying) return;
        Spawn(RacingPopupType.FuelLoss, 15f, transform.position + Vector3.up * 2f);
    }

    [ContextMenu("Test Both (Comic Style)")]
    private void TestBothComicStyle()
    {
        if (!Application.isPlaying) return;
        Vector3 pos = transform.position + Vector3.up * 2f;
        Spawn(RacingPopupType.HPDamage, 30f, pos);
        Spawn(RacingPopupType.FuelLoss, 12f, pos);
    }
#endif
}