using UnityEngine;

/// <summary>
/// ScriptableObject defining all properties for a specific coin type.
/// Create one for each CoinType (Bronze, Silver, Gold, etc.)
/// </summary>
[CreateAssetMenu(menuName = "Racing/Coin Data", fileName = "CoinData_New")]
public class CoinDataSO : ScriptableObject
{
    [Header("Identity")]
    public CoinType coinType = CoinType.Bronze;
    public string displayName = "Bronze Coin";

    [Header("Prefab")]
    [Tooltip("The prefab to spawn for this coin type.")]
    public GameObject coinPrefab;

    [Header("Value")]
    [Tooltip("Base currency value of this coin.")]
    public int baseValue = 1;

    [Header("Visuals - Colors")]
    [Tooltip("Primary color for this coin (used for popup text color, VFX main color).")]
    public Color primaryColor = new Color(0.8f, 0.5f, 0.2f, 1f);

    [Tooltip("Secondary/accent color (used for popup text outline, screen flash).")]
    public Color secondaryColor = new Color(1f, 0.7f, 0.3f, 1f);

    [Header("Screen Flash")]
    [Tooltip("Enable screen flash when collected.")]
    public bool enableScreenFlash = true;

    [Tooltip("Screen flash intensity when collected.")]
    public float flashIntensity = 0.8f;

    [Tooltip("Screen flash duration.")]
    public float flashDuration = 0.2f;

    [Tooltip("Inner radius for edge glow (smaller = more dramatic).")]
    [Range(0.1f, 0.8f)]
    public float flashInnerRadius = 0.5f;

    [Header("Screen Shake")]
    [Tooltip("Enable screen shake when collected.")]
    public bool enableScreenShake = false;

    [Tooltip("Screen shake duration.")]
    public float shakeDuration = 0.15f;

    [Tooltip("Screen shake strength/intensity.")]
    public float shakeStrength = 0.3f;

    [Tooltip("Screen shake vibrato (number of shakes).")]
    [Range(1, 20)]
    public int shakeVibrato = 10;

    [Tooltip("Screen shake randomness.")]
    [Range(0f, 1f)]
    public float shakeRandomness = 0.5f;

    [Header("Popup Text")]
    [Tooltip("Font size multiplier for popup text.")]
    public float popupSizeMultiplier = 1f;

    [Tooltip("Popup duration.")]
    public float popupDuration = 1f;

    [Tooltip("How high the popup rises.")]
    public float popupRiseDistance = 1.5f;

    [Header("Audio")]
    [Tooltip("Sound clips to play when collected (randomly selected).")]
    public AudioClip[] collectSounds;

    [Tooltip("Volume for collect sound.")]
    [Range(0f, 1f)]
    public float collectVolume = 1f;

    [Tooltip("Pitch variance for collect sound.")]
    [Range(0f, 0.3f)]
    public float pitchVariance = 0.05f;

    [Tooltip("Base pitch (higher value coins can have higher pitch).")]
    public float basePitch = 1f;

    [Header("VFX")]
    [Tooltip("Optional override VFX prefab for this coin type.")]
    public GameObject vfxPrefab;

    [Tooltip("VFX lifetime.")]
    public float vfxLifetime = 2f;

    [Tooltip("VFX scale multiplier.")]
    public float vfxScale = 1f;

    [Header("Spawn Settings")]
    [Tooltip("Rotation speed when idle (degrees per second).")]
    public float rotateSpeed = 90f;

    [Tooltip("Optional bobbing animation amplitude.")]
    public float bobAmplitude = 0f;

    [Tooltip("Bobbing speed.")]
    public float bobSpeed = 1f;

    [Header("Rarity / Spawn Weight")]
    [Tooltip("Higher weight = more likely to spawn. Used by spawners.")]
    [Range(0f, 100f)]
    public float spawnWeight = 50f;
}