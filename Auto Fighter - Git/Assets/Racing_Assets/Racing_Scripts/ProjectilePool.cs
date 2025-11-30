using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Very small, reusable pool for pooled GameObjects (projectiles, rings, VFX).
/// Usage: ProjectilePool.Instance.Get(prefab) and .Return(go).
/// Keeps objects parented under a pool root.
/// </summary>
public class ProjectilePool : MonoBehaviour
{
    public static ProjectilePool Instance { get; private set; }

    private readonly Dictionary<GameObject, Stack<GameObject>> _pools = new();
    private Transform _root;

    void Awake()
    {
        if (Instance && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        _root = new GameObject("ProjectilePoolRoot").transform;
        _root.SetParent(transform, false);
        DontDestroyOnLoad(gameObject);
    }

    public GameObject Get(GameObject prefab)
    {
        if (prefab == null) return null;
        if (!_pools.TryGetValue(prefab, out var stack) || stack.Count == 0)
        {
            // Instantiate inactive so the caller can position/rotate it before activation to avoid visual pops
            var inst = Instantiate(prefab, _root);
            inst.SetActive(false);
            return inst;
        }
        var go = stack.Pop();
        if (go == null) return Get(prefab);
        // detach from pool root (keep inactive so caller can set transform first)
        go.transform.SetParent(null, true);
        go.SetActive(false);
        return go;
    }

    public void Return(GameObject prefab, GameObject instance)
    {
        if (prefab == null || instance == null) { Destroy(instance); return; }
        instance.SetActive(false);
        instance.transform.SetParent(_root, false);

        if (!_pools.TryGetValue(prefab, out var stack))
        {
            stack = new Stack<GameObject>();
            _pools[prefab] = stack;
        }
        stack.Push(instance);
    }
}