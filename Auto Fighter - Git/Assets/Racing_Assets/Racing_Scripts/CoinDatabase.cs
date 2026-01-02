using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Central database/registry for all coin types.
/// Singleton that provides easy access to coin data from anywhere.
/// </summary>
public class CoinDatabase : MonoBehaviour
{
    public static CoinDatabase Instance { get; private set; }

    [Header("Coin Data Assets")]
    [Tooltip("Assign all CoinDataSO assets here.")]
    [SerializeField] private List<CoinDataSO> coinDataAssets = new List<CoinDataSO>();

    [Header("Fallback (if coin type not found)")]
    [SerializeField] private CoinDataSO fallbackCoinData;

    // Lookup dictionary for fast access
    private readonly Dictionary<CoinType, CoinDataSO> _coinLookup = new Dictionary<CoinType, CoinDataSO>();

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);

        BuildLookup();
    }

    private void BuildLookup()
    {
        _coinLookup.Clear();
        foreach (var data in coinDataAssets)
        {
            if (data != null && !_coinLookup.ContainsKey(data.coinType))
            {
                _coinLookup[data.coinType] = data;
            }
        }
    }

    /// <summary>
    /// Get coin data for a specific type.
    /// </summary>
    public CoinDataSO GetCoinData(CoinType type)
    {
        if (_coinLookup.TryGetValue(type, out var data))
            return data;
        
        Debug.LogWarning($"[CoinDatabase] No data found for CoinType.{type}, using fallback.");
        return fallbackCoinData;
    }

    /// <summary>
    /// Get the base value for a coin type.
    /// </summary>
    public int GetBaseValue(CoinType type)
    {
        var data = GetCoinData(type);
        return data != null ? data.baseValue : 1;
    }

    /// <summary>
    /// Get the primary color for a coin type.
    /// </summary>
    public Color GetColor(CoinType type)
    {
        var data = GetCoinData(type);
        return data != null ? data.primaryColor : Color.white;
    }

    /// <summary>
    /// Get all registered coin data assets.
    /// </summary>
    public IReadOnlyList<CoinDataSO> GetAllCoinData() => coinDataAssets;

    /// <summary>
    /// Get a random coin type based on spawn weights.
    /// </summary>
    public CoinType GetRandomCoinType()
    {
        float totalWeight = 0f;
        foreach (var data in coinDataAssets)
        {
            if (data != null)
                totalWeight += data.spawnWeight;
        }

        if (totalWeight <= 0f)
            return CoinType.Bronze;

        float random = Random.Range(0f, totalWeight);
        float accumulated = 0f;

        foreach (var data in coinDataAssets)
        {
            if (data == null) continue;
            accumulated += data.spawnWeight;
            if (random <= accumulated)
                return data.coinType;
        }

        return CoinType.Bronze;
    }

    // === STATIC SHORTCUTS ===

    public static CoinDataSO Get(CoinType type) => Instance?.GetCoinData(type);
    public static int Value(CoinType type) => Instance?.GetBaseValue(type) ?? 1;
    public static Color GetCoinColor(CoinType type) => Instance?.GetColor(type) ?? UnityEngine.Color.white;
}
