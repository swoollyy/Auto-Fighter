using System;
using System.Collections;
using UnityEngine;

/// <summary>
/// Coin pickup that uses the centralized CoinType system.
/// All visuals, sounds, and values are determined by the coin type.
/// </summary>
public class CoinPickup : MonoBehaviour
{
    [Header("Coin Type")]
    [Tooltip("The type of coin - determines value, color, sounds, etc.")]
    [SerializeField] private CoinType coinType = CoinType.Bronze;

    [Header("Override Settings (Optional)")]
    [Tooltip("If assigned, uses this data instead of looking up from CoinDatabase.")]
    [SerializeField] private CoinDataSO overrideCoinData;

    [Header("Shared VFX (Fallback)")]
    [Tooltip("VFX prefab to use if coin data doesn't specify one.")]
    [SerializeField] private GameObject fallbackVFX;
    [SerializeField] private float fallbackVFXLifetime = 2f;

    // Runtime
    private CoinDataSO _coinData;
    private float _bobTimer;
    private Vector3 _startPos;
    private Transform _visualChild;

    // Cached camera reference for screen shake
    private static CameraFollow _cameraFollow;

    public CoinType Type => coinType;
    public CoinDataSO CoinData => _coinData;

    private void Awake()
    {
        // Cache coin data
        RefreshCoinData();

        _startPos = transform.position;

        // Try to get visual child for rotation
        if (transform.childCount > 0)
            _visualChild = transform.GetChild(0);

        // Cache camera follow for screen shake
        if (_cameraFollow == null)
            _cameraFollow = FindObjectOfType<CameraFollow>();
    }

    private void Start()
    {
        // Ensure collider is trigger
        var col = GetComponent<Collider>();
        if (col != null)
            col.isTrigger = true;
    }

    /// <summary>
    /// Refresh coin data from database or override.
    /// Call this if you change coinType at runtime.
    /// </summary>
    public void RefreshCoinData()
    {
        if (overrideCoinData != null)
        {
            _coinData = overrideCoinData;
        }
        else if (CoinDatabase.Instance != null)
        {
            _coinData = CoinDatabase.Instance.GetCoinData(coinType);
        }
    }

    /// <summary>
    /// Set the coin type at runtime.
    /// </summary>
    public void SetCoinType(CoinType type)
    {
        coinType = type;
        RefreshCoinData();
    }

    private void Update()
    {
        if (_coinData == null) return;

        // Rotation
        if (_coinData.rotateSpeed != 0f && _visualChild != null)
        {
            _visualChild.Rotate(0f, _coinData.rotateSpeed * Time.deltaTime, 0f, Space.World);
        }

        // Bobbing
        if (_coinData.bobAmplitude > 0f)
        {
            _bobTimer += Time.deltaTime * _coinData.bobSpeed;
            float yOffset = Mathf.Sin(_bobTimer) * _coinData.bobAmplitude;
            transform.position = _startPos + Vector3.up * yOffset;
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!other.TryGetComponent<CarController>(out var car))
            return;

        // Ensure we have coin data
        if (_coinData == null)
            RefreshCoinData();

        // Calculate final value
        int baseValue = _coinData?.baseValue ?? 1;
        int finalValue = baseValue;

        var mgr = RacingSkillTreeManager.Instance;
        if (mgr != null)
        {
            // Add skill bonus
            int baseAdd = mgr.GetCoinBaseAdd();
            if (baseAdd > 0)
                finalValue += baseAdd;

            // Double chance
            float dblChance = mgr.GetCoinDoubleChance();
            if (dblChance > 0f && UnityEngine.Random.value < dblChance)
                finalValue *= 2;

            // Add currency
            mgr.AddCurrency(finalValue);
        }

        // Register with game manager
        if (GameManager_Racing.Instance != null)
        {
            GameManager_Racing.Instance.RegisterCoinPickup(finalValue);
        }

        // Get spawn position for effects
        Vector3 spawnPos = transform.position;
        try
        {
            spawnPos = other.ClosestPoint(transform.position);
        }
        catch { /* use transform.position */ }

        // === EFFECTS ===

        // Screen Flash (uses secondary color)
        if (_coinData != null && _coinData.enableScreenFlash && ScreenFlashManager.Instance != null)
        {
            ScreenFlashManager.Instance.Flash(
                _coinData.secondaryColor,  // Use secondary color for flash
                _coinData.flashIntensity,
                _coinData.flashDuration,
                _coinData.flashInnerRadius
            );
        }

        // Screen Shake
        if (_coinData != null && _coinData.enableScreenShake)
        {
            TriggerScreenShake();
        }

        // Popup Text (primary color for text, secondary for outline)
        if (RacingPopups.IsReady && _coinData != null)
        {
            // Use the CoinGain popup with primary color
            // The popup system will use secondary color for outline if configured
            RacingPopups.SpawnCoin(
                finalValue,
                spawnPos + Vector3.up * 0.5f,
                _coinData.primaryColor,
                _coinData.secondaryColor
            );
        }
        else if (RacingPopups.IsReady)
        {
            // Fallback
            RacingPopups.Spawn(
                RacingPopupType.CoinGain,
                finalValue,
                spawnPos + Vector3.up * 0.5f,
                Color.yellow
            );
        }

        // Sound
        PlayCollectSound(spawnPos);

        // VFX
        SpawnVFX(spawnPos);

        // Destroy
        Destroy(gameObject);
    }

    private void TriggerScreenShake()
    {
        if (_coinData == null) return;

        // Try to find camera follow if not cached
        if (_cameraFollow == null)
            _cameraFollow = FindObjectOfType<CameraFollow>();

        if (_cameraFollow != null)
        {
            _cameraFollow.StartShake(
                _coinData.shakeDuration,
                _coinData.shakeStrength,
                _coinData.shakeVibrato,
                _coinData.shakeRandomness
            );
        }
    }

    private void PlayCollectSound(Vector3 position)
    {
        if (_coinData == null) return;
        if (_coinData.collectSounds == null || _coinData.collectSounds.Length == 0) return;

        // Pick random sound
        AudioClip clip = null;
        for (int i = 0; i < 8; i++)
        {
            var candidate = _coinData.collectSounds[UnityEngine.Random.Range(0, _coinData.collectSounds.Length)];
            if (candidate != null) { clip = candidate; break; }
        }
        if (clip == null) return;

        // Calculate pitch
        float pitch = _coinData.basePitch + UnityEngine.Random.Range(-_coinData.pitchVariance, _coinData.pitchVariance);

        // Play
        PlayClipAtPointWithPitch(clip, position, _coinData.collectVolume, pitch);
    }

    private void PlayClipAtPointWithPitch(AudioClip clip, Vector3 pos, float volume, float pitch)
    {
        if (clip == null) return;

        GameObject go = new GameObject("CoinSFX");
        go.transform.position = pos;

        var src = go.AddComponent<AudioSource>();
        src.spatialBlend = 1f;
        src.clip = clip;
        src.volume = Mathf.Clamp01(volume);
        src.pitch = Mathf.Max(0.01f, pitch);
        src.Play();

        Destroy(go, clip.length / Mathf.Max(0.01f, Mathf.Abs(src.pitch)));
    }

    private void SpawnVFX(Vector3 position)
    {
        // Determine which VFX to use
        GameObject vfxPrefab = _coinData?.vfxPrefab ?? fallbackVFX;
        if (vfxPrefab == null) return;

        float lifetime = _coinData?.vfxLifetime ?? fallbackVFXLifetime;
        float scale = _coinData?.vfxScale ?? 1f;
        Color color = _coinData?.primaryColor ?? Color.white;

        Quaternion rotation = Quaternion.Euler(-90f, 0f, 0f);

        // Try pool first
        try
        {
            if (ProjectilePool.Instance != null)
            {
                GameObject inst = ProjectilePool.Instance.Get(vfxPrefab);
                if (inst != null)
                {
                    inst.transform.position = position;
                    inst.transform.rotation = rotation;
                    inst.transform.localScale = Vector3.one * scale;
                    ApplyVFXColor(inst, color);
                    inst.SetActive(true);
                    StartCoroutine(ReturnPooledVFX(vfxPrefab, inst, lifetime));
                    return;
                }
            }
        }
        catch { /* fallback to instantiate */ }

        // Fallback: instantiate
        var go = Instantiate(vfxPrefab, position, rotation);
        go.transform.localScale = Vector3.one * scale;
        ApplyVFXColor(go, color);
        Destroy(go, lifetime);
    }

    private IEnumerator ReturnPooledVFX(GameObject prefab, GameObject instance, float delay)
    {
        yield return new WaitForSeconds(delay);
        if (instance != null && prefab != null && ProjectilePool.Instance != null)
            ProjectilePool.Instance.Return(prefab, instance);
    }

    private void ApplyVFXColor(GameObject vfxRoot, Color color)
    {
        if (vfxRoot == null) return;

        // Apply to all particle systems
        var particles = vfxRoot.GetComponentsInChildren<ParticleSystem>(true);
        foreach (var ps in particles)
        {
            var main = ps.main;
            main.startColor = color;
        }

        // Apply to renderers with _Color property
        var renderers = vfxRoot.GetComponentsInChildren<Renderer>(true);
        foreach (var rend in renderers)
        {
            if (rend.material.HasProperty("_Color"))
                rend.material.color = color;
            if (rend.material.HasProperty("_TintColor"))
                rend.material.SetColor("_TintColor", color);
        }
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        // Auto-refresh in editor when type changes
        if (Application.isPlaying && CoinDatabase.Instance != null)
            RefreshCoinData();
    }

    private void OnDrawGizmosSelected()
    {
        // Show coin type color in editor
        if (CoinDatabase.Instance != null)
        {
            Gizmos.color = CoinDatabase.GetCoinColor(coinType);
        }
        else
        {
            Gizmos.color = Color.yellow;
        }
        Gizmos.DrawWireSphere(transform.position, 0.5f);
    }
#endif
}