using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

public static class PowerupSystem
{
    private static readonly List<IPowerup> _registry = new();
    private static bool _initialized;

    private const string PickupResourcePath = "Powerups/PowerupPickup"; // Resources/Powerups/PowerupPickup.prefab
    private static GameObject _pickupPrefab; // cached after first load

    // Scans assemblies once to auto-register IPowerup implementations with parameterless constructors.
    public static void EnsureInitialized()
    {
        if (_initialized) return;
        _initialized = true;

        try
        {
            var asms = AppDomain.CurrentDomain.GetAssemblies();
            for (int ai = 0; ai < asms.Length; ai++)
            {
                var asm = asms[ai];
                if (asm == null || asm.IsDynamic) continue;

                Type[] types = null;
                try { types = asm.GetTypes(); }
                catch (ReflectionTypeLoadException rtle) { types = rtle.Types; }
                catch { continue; }

                if (types == null) continue;
                for (int ti = 0; ti < types.Length; ti++)
                {
                    var t = types[ti];
                    if (t == null || t.IsAbstract || t.IsInterface) continue;
                    if (!typeof(IPowerup).IsAssignableFrom(t)) continue;

                    var ctor = t.GetConstructor(Type.EmptyTypes);
                    if (ctor == null) continue;

                    try
                    {
                        var instance = (IPowerup)Activator.CreateInstance(t);
                        Register(instance);
                    }
                    catch (Exception ex)
                    {
                        Debug.LogWarning($"[PowerupSystem] Failed to instantiate {t?.FullName}: {ex.Message}");
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[PowerupSystem] Reflection scan failed: {ex}");
        }

        Debug.Log($"[PowerupSystem] Registered {_registry.Count} powerups.");
    }

    // Adds a powerup to the registry if not already present by id.
    public static void Register(IPowerup powerup)
    {
        if (powerup == null) return;
        if (_registry.Exists(p => p.Id == powerup.Id)) return;
        _registry.Add(powerup);
    }

    // Rolls whether a pickup should drop using the context’s configured chance.
    public static bool TryRoll(IRunContext ctx)
    {
        float chance = GetDropChance(ctx);
        return UnityEngine.Random.value < chance;
    }

    // Returns the clamped drop chance from the Pinball singleton or a default.
    public static float GetDropChance(IRunContext ctx)
    {
        float baseChance = Pinball.Instance != null ? Pinball.Instance.PowerupDropChance : 0.03f;
        return Mathf.Clamp01(baseChance);
    }

    // Triggers a specific powerup by id if eligible for the given Pinball.
    public static bool TryTriggerById(Pinball pm, string id, Vector3 triggerPos)
    {
        EnsureInitialized();
        for (int i = 0; i < _registry.Count; i++)
        {
            var p = _registry[i];
            if (p == null) continue;
            if (p.Id == id && p.CanTrigger(pm))
            {
                Debug.Log($"[PowerupSystem] Triggering: {p.DebugLabel} @ {triggerPos}");
                p.Execute(pm, triggerPos);
                return true;
            }
        }
        return false;
    }

    // Rolls, picks a weighted eligible powerup, and spawns a pickup at the given position.
    public static bool TrySpawnPickupOnHit(Pinball pm, Vector3 pos, IRunContext ctx)
    {
        EnsureInitialized();

        if (!TryRoll(ctx))
            return false;

        var eligibles = ListPool<IPowerup>.Get();
        try
        {
            for (int i = 0; i < _registry.Count; i++)
            {
                var p = _registry[i];
                if (p != null && p.CanTrigger(pm))
                    eligibles.Add(p);
            }
            if (eligibles.Count == 0)
                return false;

            var picked = PickWeighted(eligibles);
            return SpawnPickup(picked.Id, pos);
        }
        finally
        {
            ListPool<IPowerup>.Release(eligibles);
        }
    }

    // Instantiates the pickup prefab from Resources and sets its powerup id.
    private static bool SpawnPickup(string powerupId, Vector3 pos)
    {
        if (_pickupPrefab == null)
        {
            _pickupPrefab = Resources.Load<GameObject>(PickupResourcePath);
            if (_pickupPrefab == null)
            {
                Debug.LogWarning($"[PowerupSystem] Failed to load pickup prefab at Resources/{PickupResourcePath}");
                return false;
            }
        }

        var go = UnityEngine.Object.Instantiate(_pickupPrefab, pos, Quaternion.identity);
        var pickup = go.GetComponent<PowerupPickup>();
        if (pickup == null)
        {
            Debug.LogWarning("[PowerupSystem] Instantiated pickup prefab is missing PowerupPickup component.");
            return false;
        }
        pickup.powerupId = powerupId;
        return true;
    }

    // Picks a powerup from a list using their Weight properties.
    private static IPowerup PickWeighted(List<IPowerup> items)
    {
        float total = 0f;
        for (int i = 0; i < items.Count; i++)
            total += Mathf.Max(.0001f, items[i].Weight);

        float r = UnityEngine.Random.value * total;
        float accum = 0f;
        for (int i = 0; i < items.Count; i++)
        {
            accum += Mathf.Max(.0001f, items[i].Weight);
            if (r <= accum)
                return items[i];
        }
        return items[items.Count - 1];
    }

    // Small GC-free list pool for temporary allocations.
    private static class ListPool<T>
    {
        private static readonly Stack<List<T>> Pool = new();

        public static List<T> Get() => Pool.Count > 0 ? Pool.Pop() : new List<T>();

        public static void Release(List<T> list)
        {
            list.Clear();
            Pool.Push(list);
        }
    }
}