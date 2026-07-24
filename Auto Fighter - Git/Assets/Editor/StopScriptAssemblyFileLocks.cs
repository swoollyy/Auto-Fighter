#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

/// <summary>
/// Stops Windows "user-mapped section open" on Library/ScriptAssemblies by
/// disabling Burst compilation in the Editor (Burst ILPP was locking the DLL).
/// </summary>
[InitializeOnLoad]
public static class StopScriptAssemblyFileLocks
{
    private const string PrefKey = "AutoFighter.DisableBurstInEditor";

    static StopScriptAssemblyFileLocks()
    {
        if (!EditorPrefs.HasKey(PrefKey))
            EditorPrefs.SetBool(PrefKey, true);

        EditorApplication.delayCall += Apply;
    }

    private static void Apply()
    {
        if (!EditorPrefs.GetBool(PrefKey, true))
            return;

        if (TrySetBurstCompilation(false))
            Debug.Log("[AutoFighter] Burst disabled in Editor (fixes ScriptAssemblies file lock). Re-enable via AutoFighter menu if needed.");
    }

    // No validate functions — always show so the menu never disappears.
    [MenuItem("AutoFighter/Disable Burst In Editor (fix file locks)", false, 200)]
    private static void DisableBurst()
    {
        EditorPrefs.SetBool(PrefKey, true);
        bool ok = TrySetBurstCompilation(false);
        Debug.Log(ok
            ? "[AutoFighter] Burst DISABLED in Editor."
            : "[AutoFighter] Could not set Burst via script. Use menu: Jobs → Burst → Enable Compilation (uncheck it).");
    }

    [MenuItem("AutoFighter/Enable Burst In Editor", false, 201)]
    private static void EnableBurst()
    {
        EditorPrefs.SetBool(PrefKey, false);
        bool ok = TrySetBurstCompilation(true);
        Debug.Log(ok
            ? "[AutoFighter] Burst ENABLED in Editor."
            : "[AutoFighter] Could not set Burst via script. Use menu: Jobs → Burst → Enable Compilation (check it).");
    }

    private static bool TrySetBurstCompilation(bool enabled)
    {
        var compilerType = System.Type.GetType("Unity.Burst.BurstCompiler, Unity.Burst");
        if (compilerType == null)
            return false;

        var optionsProp = compilerType.GetProperty(
            "Options",
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
        if (optionsProp == null)
            return false;

        object options = optionsProp.GetValue(null);
        if (options == null)
            return false;

        var enableProp = options.GetType().GetProperty("EnableBurstCompilation");
        if (enableProp == null || !enableProp.CanWrite)
            return false;

        enableProp.SetValue(options, enabled);
        return true;
    }
}
#endif
