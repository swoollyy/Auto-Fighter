using System;
using TMPro;
using UnityEngine;

/// <summary>
/// ScriptableObject defining the visual style for a specific popup type.
/// Create one for each RacingPopupType you want to customize.
/// </summary>
[CreateAssetMenu(menuName = "Racing/Popup Style", fileName = "NewPopupStyle")]
public class RacingPopupStyleSO : ScriptableObject
{
    [Header("Type")]
    [Tooltip("Which popup type this style applies to.")]
    public RacingPopupType popupType = RacingPopupType.Generic;

    [Header("Typography")]
    [Tooltip("Font asset to use. If null, uses system default.")]
    public TMP_FontAsset font;

    [Tooltip("Font material override. If null, uses font's default material.")]
    public Material fontMaterial;

    [Tooltip("Sprite asset for this popup type (for icons like heart, fuel, coin).")]
    public TMP_SpriteAsset spriteAsset;

    [Tooltip("Base font size before scaling.")]
    public float baseFontSize = 5f;

    [Tooltip("Main text color.")]
    public Color textColor = Color.white;

    [Header("Text Content")]
    [Tooltip("Text to show before the value (e.g., '-' for damage, '+' for gains).")]
    public string prefix = "";

    [Tooltip("Text to show after the value (e.g., ' HP', ' Fuel', or TMP sprite tag like '<sprite=0>').")]
    public string suffix = "";

    [Tooltip("Unused for display now — values always show as integers via RacingUiNumberFormat.")]
    public string numberFormat = "0";

    [Header("Random Text Options")]
    [Tooltip("If true and randomTexts has entries, picks a random text instead of using prefix/value/suffix.")]
    public bool useRandomText = false;

    [Tooltip("List of random texts to choose from (e.g., 'WAPOW!', 'KABLAM!', 'POW!').")]
    public string[] randomTexts = new string[0];

    [Header("Outline")]
    public bool enableOutline = true;

    [Range(0f, 1f)]
    public float outlineWidth = 0.3f;

    [ColorUsage(true, true)]
    [Tooltip("Outline color with HDR support. Set intensity in color picker.")]
    public Color outlineColor = Color.black;

    [Tooltip("HDR intensity multiplier for outline (default 3 for visibility).")]
    [Range(0f, 10f)]
    public float outlineIntensity = 3f;

    [Range(0f, 0.5f)]
    public float faceDilate = 0.05f;


    [Header("Position Offset (Comic Style)")]
    [Tooltip("Fixed horizontal offset from spawn point. Negative = left, Positive = right.")]
    public float horizontalOffset = 0f;

    [Tooltip("Fixed vertical offset from spawn point.")]
    public float verticalOffset = 0f;

    [Tooltip("Additional random horizontal drift on top of fixed offset.")]
    public float horizontalDrift = 0f;

    [Tooltip("Additional random vertical drift on top of fixed offset.")]
    public float verticalDrift = 0f;

    [Header("Random Rotation (Comic Style)")]
    [Tooltip("Fixed Z rotation angle. Use for /\\ pyramid effect. Negative = tilt right (/), Positive = tilt left (\\).")]
    public float fixedRotationZ = 0f;

    [Tooltip("If true, picks a random rotation within the range below instead of using fixedRotationZ.")]
    public bool useRandomRotation = false;

    [Tooltip("Min random rotation angle (used if useRandomRotation is true).")]
    public float randomRotationMin = -15f;

    [Tooltip("Max random rotation angle (used if useRandomRotation is true).")]
    public float randomRotationMax = 15f;

    [Header("Timing")]
    [Tooltip("Total duration the popup is visible.")]
    public float duration = 1.0f;

    [Tooltip("Fraction of duration for fade in (0-1).")]
    [Range(0.01f, 0.5f)]
    public float fadeInFraction = 0.1f;

    [Tooltip("Fraction of duration for fade out (0-1).")]
    [Range(0.1f, 0.8f)]
    public float fadeOutFraction = 0.3f;

    [Tooltip("Use unscaled time (works during slow-mo).")]
    public bool useUnscaledTime = true;

    [Header("Motion")]
    [Tooltip("How far the text rises during its lifetime.")]
    public float riseDistance = 1.5f;

    [Tooltip("Ease type for the rise motion.")]
    public DG.Tweening.Ease riseEase = DG.Tweening.Ease.OutCubic;

    [Header("Scale Animation (Pop Effect)")]
    [Tooltip("Starting scale (before pop). Lower = more dramatic pop.")]
    public float popFromScale = 0.3f;

    [Tooltip("Peak scale (after pop). Higher = more 'punch'.")]
    public float popToScale = 1.3f;

    [Tooltip("Final scale (after shrink). Set to popToScale for no shrink.")]
    public float endScale = 1.0f;

    [Tooltip("Delay before the pop animation starts.")]
    [Range(0f, 0.3f)]
    public float popDelay = 0.02f;

    [Tooltip("Fraction of duration for the pop-up animation.")]
    [Range(0.05f, 0.4f)]
    public float popDurationFraction = 0.12f;

    [Tooltip("Ease for the pop-in scale.")]
    public DG.Tweening.Ease popEase = DG.Tweening.Ease.OutBack;

    [Header("Rotation Shake (Optional)")]
    [Tooltip("Enable Z-axis tilt shake animation (on top of fixed rotation).")]
    public bool enableTiltShake = false;

    [Tooltip("Maximum shake tilt angle in degrees.")]
    [Range(0f, 30f)]
    public float tiltShakeAngle = 8f;

    [Tooltip("Fraction of duration for tilt shake animation.")]
    [Range(0.05f, 0.5f)]
    public float tiltShakeDurationFraction = 0.2f;

    [Header("Value-Based Scaling")]
    [Tooltip("Enable scaling font size based on value magnitude.")]
    public bool enableValueScaling = true;

    [Tooltip("Minimum value for scale mapping.")]
    public float minValueForScale = 1f;

    [Tooltip("Maximum value for scale mapping.")]
    public float maxValueForScale = 50f;

    [Tooltip("Scale multiplier at minimum value.")]
    public float minScaleMultiplier = 0.8f;

    [Tooltip("Scale multiplier at maximum value.")]
    public float maxScaleMultiplier = 1.4f;

    [Tooltip("Use logarithmic scaling (better for wide ranges).")]
    public bool useLogScale = false;

    [Header("Rendering")]
    public string sortingLayerName = "UI";
    public int sortingOrder = 1000;

    [Header("Sound (Optional)")]
    [Tooltip("Sound to play when this popup spawns.")]
    public AudioClip spawnSound;

    [Range(0f, 1f)]
    public float soundVolume = 0.5f;

    /// <summary>
    /// Compute scale multiplier based on the value.
    /// </summary>
    public float ComputeScaleMultiplier(float value)
    {
        if (!enableValueScaling) return 1f;

        float absValue = Mathf.Abs(value);

        if (useLogScale)
        {
            float minL = Mathf.Log(minValueForScale + 1f, 10f);
            float maxL = Mathf.Log(maxValueForScale + 1f, 10f);
            float valL = Mathf.Log(absValue + 1f, 10f);
            float t = Mathf.InverseLerp(minL, maxL, valL);
            return Mathf.Lerp(minScaleMultiplier, maxScaleMultiplier, t);
        }

        float linear = Mathf.InverseLerp(minValueForScale, maxValueForScale, absValue);
        return Mathf.Lerp(minScaleMultiplier, maxScaleMultiplier, linear);
    }

    /// <summary>
    /// Format the display text for a given value.
    /// If useRandomText is enabled and randomTexts has entries, returns a random text instead.
    /// </summary>
    public string FormatText(float value)
    {
        if (useRandomText && randomTexts != null && randomTexts.Length > 0)
        {
            return randomTexts[UnityEngine.Random.Range(0, randomTexts.Length)];
        }
        return $"{prefix}{RacingUiNumberFormat.ToDisplayInt(value)}{suffix}";
    }

    /// <summary>
    /// Format the display text for a given integer value.
    /// If useRandomText is enabled and randomTexts has entries, returns a random text instead.
    /// </summary>
    public string FormatText(int value)
    {
        if (useRandomText && randomTexts != null && randomTexts.Length > 0)
        {
            return randomTexts[UnityEngine.Random.Range(0, randomTexts.Length)];
        }
        return $"{prefix}{value}{suffix}";
    }

    /// <summary>
    /// Get a random text from the randomTexts array (or empty string if none).
    /// </summary>
    public string GetRandomText()
    {
        if (randomTexts != null && randomTexts.Length > 0)
        {
            return randomTexts[UnityEngine.Random.Range(0, randomTexts.Length)];
        }
        return prefix + suffix;
    }

    /// <summary>
    /// Get the spawn position offset for this style.
    /// </summary>
    public Vector3 GetPositionOffset()
    {
        float randomH = horizontalDrift > 0 ? UnityEngine.Random.Range(-horizontalDrift, horizontalDrift) : 0f;
        float randomV = verticalDrift > 0 ? UnityEngine.Random.Range(-verticalDrift, verticalDrift) : 0f;
        return new Vector3(horizontalOffset + randomH, verticalOffset + randomV, 0f);
    }

    /// <summary>
    /// Get the rotation Z angle, either fixed or random based on settings.
    /// </summary>
    public float GetRotationZ()
    {
        if (useRandomRotation)
        {
            return UnityEngine.Random.Range(randomRotationMin, randomRotationMax);
        }
        return fixedRotationZ;
    }
}