using System.Collections.Generic;
using System;
using UnityEngine;

[DefaultExecutionOrder(-1000)]
public class XPCollectorRegistry : MonoBehaviour
{
    public static XPCollectorRegistry I { get; private set; }
    public readonly List<Collider> collectors = new();

    public static event Action OnChanged;

    void Awake() => I = this;

    public void Register(Collider c)
    {
        if (c && !collectors.Contains(c))
        {
            collectors.Add(c);
            OnChanged?.Invoke();
        }
    }

    public void Unregister(Collider c)
    {
        if (c && collectors.Remove(c))
            OnChanged?.Invoke();
    }

    // NEW: safe external notification hook (powerups restore registry)
    public void NotifyChanged() => OnChanged?.Invoke();
}