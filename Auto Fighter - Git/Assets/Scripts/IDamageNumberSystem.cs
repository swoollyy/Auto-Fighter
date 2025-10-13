using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public interface IDamageNumberSystem
{
    void Spawn(float amount, Vector3 position, Color? overrideColor = null);
}


/// Thin static facade so gameplay code can do: DamageNumbers.Spawn(...).
/// Keeps callers decoupled from the underlying UI/animation implementation.
public static class DamageNumbers
{
    /// Set once by the concrete implementation on startup.
    public static IDamageNumberSystem System { get; set; }

    public static bool IsReady => System != null;

    public static void Register(IDamageNumberSystem system) => System = system;

    public static void Spawn(float amount, Vector3 position, Color? overrideColor = null)
        => System?.Spawn(amount, position, overrideColor);
}
