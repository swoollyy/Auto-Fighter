using System.Collections;
using System.Collections.Generic;
using Unity.VisualScripting;
using UnityEngine;

public static class PowerupSystem
{
    private static readonly List<IPowerup> _registry = new();
    private static bool _initialized;

    public static void EnsureInitialized()
    {
        if (_initialized)
            return;
        _initialized = true;

        Register(new CollectAllXPPowerup());
        Register(new NukeBumpersPowerup());
        Register(new RandomFlingPowerup());
    }

    public static void Register(IPowerup powerup)
    {
        if (powerup == null)
            return;
        if (_registry.Exists(p => p.Id == powerup.Id))
            return;
        _registry.Add(powerup);
    }

    public static bool TryRoll(IRunContext ctx)
    {
        float chance = GetDropChance(ctx);
        return Random.value < chance;
    }

    public static float GetDropChance(IRunContext ctx)
    {
        float baseChance = Pinball.Instance != null ? Pinball.Instance.PowerupDropChance : 0.03f;
        return Mathf.Clamp01(baseChance);
    }

    public static bool TryTriggerRandom(Pinball pm, Vector3 triggerPos)
    {
        EnsureInitialized();

        var eligibles = ListPool<IPowerup>.Get();
        try
        {
            for(int i = 0; i < _registry.Count; i++)
            {
                var p = _registry[i];
                if(p != null && p.CanTrigger(pm))
                    eligibles.Add(p);
            }

            if (eligibles.Count == 0)
                return false;

            var picked = PickWeighted(eligibles);
            picked.Execute(pm, triggerPos);
            return true;
        }
        finally
        {
            ListPool<IPowerup>.Release(eligibles);
        }
    }

    private static IPowerup PickWeighted(List<IPowerup> items)
    {
        float total = 0f;
        for (int i = 0; i < items.Count; i++)
            total += Mathf.Max(.0001f, items[i].Weight);

        float r = Random.value * total;
        float accum = 0f;
        for(int i = 0; i < items.Count; i++)
        {
            accum += Mathf.Max(.0001f, items[i].Weight);
            if(r <= accum)
                return items[i];
        }
        return items[items.Count - 1];
    }

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
