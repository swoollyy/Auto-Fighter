using UnityEngine;
using System.Collections.Generic;

public enum RacingCoinRewardSource
{
    Pickup,
    Obstacle
}

/// <summary>
/// Centralized hub for awarding coins and playing shared coin feedback (popup + SFX).
/// Put one in scene so all coin collection behavior is configured in one inspector.
/// </summary>
public class RacingCoinCollectionHub : MonoBehaviour
{
    [System.Serializable]
    private class CoinTypeSettings
    {
        public CoinType coinType;
        public CoinDataSO coinData;
    }

    public static RacingCoinCollectionHub Instance { get; private set; }
    private const CoinType FallbackCoinType = CoinType.DefaultFallback;

    [Header("Default Fallback")]
    [SerializeField] private float popupHeight = 0.5f;
    [SerializeField] private float sfxMinDistance = 5f;
    [SerializeField] private float sfxMaxDistance = 40f;

    [Header("Coin Type Assets (Drag CoinDataSO here)")]
    [Tooltip("Optional per-type CoinData assets. If set, popup/SFX/flash/shake are pulled from these automatically.")]
    [SerializeField] private CoinTypeSettings[] coinTypeSettings;

    private readonly Dictionary<CoinType, CoinDataSO> _coinDataByType = new Dictionary<CoinType, CoinDataSO>();
    private static CameraFollow _cameraFollow;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        RebuildCoinTypeMap();
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    public void AwardCoins(
        int amount,
        Vector3 worldPosition,
        RacingCoinRewardSource source,
        CoinType? coinType = null,
        CoinDataSO customCoinData = null,
        bool useCustomPopupColors = false,
        Color customPopupTextColor = default,
        Color customPopupOutlineColor = default,
        AudioClip[] customCollectSounds = null,
        float customCollectVolume = -1f,
        float customBasePitch = -1f,
        float customPitchVariance = -1f)
    {
        if (amount <= 0) return;

        var gm = GameManager_Racing.Instance;
        if (gm != null)
        {
            if (source == RacingCoinRewardSource.Obstacle)
                gm.RegisterObstacleReward(amount);
            else
                gm.RegisterCoinPickup(amount);
        }

        var skillMgr = RacingSkillTreeManager.Instance;
        if (skillMgr != null)
            skillMgr.AddCurrency(amount);

        RacingQuestUnlockManager.Instance?.RecordCoinsCollected(amount);

        CoinDataSO mappedCoinData = null;
        if (coinType.HasValue)
            _coinDataByType.TryGetValue(coinType.Value, out mappedCoinData);
        _coinDataByType.TryGetValue(FallbackCoinType, out var fallbackCoinData);
        CoinDataSO resolvedCoinData = customCoinData != null
            ? customCoinData
            : (mappedCoinData != null ? mappedCoinData : fallbackCoinData);

        Vector3 popupPos = worldPosition + Vector3.up * popupHeight;

        if (RacingPopups.IsReady)
        {
            if (useCustomPopupColors)
            {
                RacingPopups.SpawnCoin(amount, popupPos, customPopupTextColor, customPopupOutlineColor);
            }
            else if (resolvedCoinData != null)
            {
                RacingPopups.SpawnCoin(amount, popupPos, resolvedCoinData.primaryColor, resolvedCoinData.secondaryColor);
            }
            else
            {
                RacingPopups.CoinGain(amount, popupPos);
            }
        }

        // Drive flash/shake from whichever coin asset was resolved (typed mapping or default fallback).
        if (resolvedCoinData != null)
        {
            if (resolvedCoinData.enableScreenFlash && ScreenFlashManager.Instance != null)
            {
                ScreenFlashManager.Instance.Flash(
                    resolvedCoinData.secondaryColor,
                    resolvedCoinData.flashIntensity,
                    resolvedCoinData.flashDuration,
                    resolvedCoinData.flashInnerRadius
                );
            }

            if (resolvedCoinData.enableScreenShake)
                TriggerScreenShake(resolvedCoinData);
        }

        AudioClip[] mappedSounds = mappedCoinData != null ? mappedCoinData.collectSounds : null;
        AudioClip[] fallbackSounds = fallbackCoinData != null ? fallbackCoinData.collectSounds : null;
        AudioClip clip = PickClip(customCollectSounds, mappedSounds, fallbackSounds);
        if (clip != null)
        {
            float volume = customCollectVolume >= 0f
                ? customCollectVolume
                : (resolvedCoinData != null ? resolvedCoinData.collectVolume : 1f);
            float basePitch = customBasePitch >= 0f
                ? customBasePitch
                : (resolvedCoinData != null ? resolvedCoinData.basePitch : 1f);
            float pitchVariance = customPitchVariance >= 0f
                ? customPitchVariance
                : (resolvedCoinData != null ? resolvedCoinData.pitchVariance : 0.05f);
            float pitch = Mathf.Clamp(basePitch + UnityEngine.Random.Range(-pitchVariance, pitchVariance), 0.01f, 3f);
            PlayClip3D(clip, worldPosition, volume, pitch);
        }
    }

    private static AudioClip PickClip(AudioClip[] preferred, AudioClip[] mapped, AudioClip[] fallback)
    {
        if (TryPick(preferred, out var picked)) return picked;
        if (TryPick(mapped, out picked)) return picked;
        if (TryPick(fallback, out picked)) return picked;
        return null;
    }

    private static bool TryPick(AudioClip[] clips, out AudioClip clip)
    {
        clip = null;
        if (clips == null || clips.Length == 0) return false;

        for (int i = 0; i < 8; i++)
        {
            var candidate = clips[UnityEngine.Random.Range(0, clips.Length)];
            if (candidate != null)
            {
                clip = candidate;
                return true;
            }
        }
        return false;
    }

    private void PlayClip3D(AudioClip clip, Vector3 worldPos, float volume, float pitch)
    {
        if (clip == null) return;

        GameObject go = new GameObject("CoinCollectSFX");
        go.transform.position = worldPos;

        var src = go.AddComponent<AudioSource>();
        src.spatialBlend = 1f;
        src.rolloffMode = AudioRolloffMode.Linear;
        src.minDistance = Mathf.Max(0.01f, sfxMinDistance);
        src.maxDistance = Mathf.Max(src.minDistance + 0.1f, sfxMaxDistance);
        src.clip = clip;
        src.volume = Mathf.Clamp01(volume);
        src.pitch = Mathf.Max(0.01f, pitch);
        src.Play();

        Destroy(go, clip.length / Mathf.Max(0.01f, Mathf.Abs(src.pitch)));
    }

    private void TriggerScreenShake(CoinDataSO coinData)
    {
        if (coinData == null) return;

        if (_cameraFollow == null)
            _cameraFollow = FindObjectOfType<CameraFollow>();

        if (_cameraFollow != null)
        {
            _cameraFollow.StartShake(
                coinData.shakeDuration,
                coinData.shakeStrength,
                coinData.shakeVibrato,
                coinData.shakeRandomness
            );
        }
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        RebuildCoinTypeMap();
    }
#endif

    private void RebuildCoinTypeMap()
    {
        _coinDataByType.Clear();
        if (coinTypeSettings == null) return;

        for (int i = 0; i < coinTypeSettings.Length; i++)
        {
            var entry = coinTypeSettings[i];
            if (entry == null || entry.coinData == null) continue;
            _coinDataByType[entry.coinType] = entry.coinData;
        }
    }
}
