using UnityEngine;

public interface IXPNumberSystem
{
    void Spawn(int amount, Vector3 position, Color? overrideColor = null);
    void SpawnFollow(int amount, Transform follow, Vector3 localOffset, Color? overrideColor = null); // NEW
}

public static class XPNumbers
{
    public static IXPNumberSystem System { get; set; }
    public static bool IsReady => System != null;
    public static void Register(IXPNumberSystem system) => System = system;
    public static void Spawn(int amount, Vector3 position, Color? overrideColor = null)
        => System?.Spawn(amount, position, overrideColor);
    // NEW: follow a Transform (e.g., ball)
    public static void Spawn(int amount, Transform follow, Vector3 localOffset, Color? overrideColor = null)
        => System?.SpawnFollow(amount, follow, localOffset, overrideColor);
}
