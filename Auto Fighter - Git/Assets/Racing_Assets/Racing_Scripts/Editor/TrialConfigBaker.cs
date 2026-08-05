#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

/// <summary>
/// Editor utility to create or update a <see cref="TrialConfig"/> from scene spawners/generators.
/// </summary>
public static class TrialConfigBaker
{
    /// <summary>
    /// Bakes only Thrown / Rolling Log / Cross settings from the open scene into the
    /// currently selected TrialConfig. Does not touch track, obstacles, creatures, NPC, or coins.
    /// </summary>
    [MenuItem("Tools/Racing/Bake Thrown / Log / Cross Into Selected Trial Config")]
    public static void BakeThrownLogCrossIntoSelected()
    {
        var cfg = Selection.activeObject as TrialConfig;
        if (cfg == null)
        {
            EditorUtility.DisplayDialog(
                "Bake Thrown / Log / Cross",
                "Select a Trial Config asset in the Project window first (e.g. Track1), then run this again.\n\n" +
                "Only Thrown, Rolling Logs, and Cross Obstacles will be overwritten from the open scene. Other sections stay as-is.",
                "OK");
            return;
        }

        var thrown = Object.FindObjectOfType<ThrownObstacleDirector>(true);
        var rollingLogs = Object.FindObjectOfType<RollingLogSpawner>(true);
        var cross = Object.FindObjectOfType<CrossObstacleDirector>(true);

        if (thrown == null && rollingLogs == null && cross == null)
        {
            EditorUtility.DisplayDialog(
                "Bake Thrown / Log / Cross",
                "Couldn't find ThrownObstacleDirector / RollingLogSpawner / CrossObstacleDirector in the open scene.\n" +
                "Open Racer_Incremental.unity first.",
                "OK");
            return;
        }

        Undo.RecordObject(cfg, "Bake Thrown / Log / Cross Into Trial Config");

        if (thrown != null)
            cfg.thrown = thrown.CaptureConfig();
        if (rollingLogs != null)
            cfg.rollingLogs = rollingLogs.CaptureConfig();
        if (cross != null)
            cfg.crossObstacles = cross.CaptureConfig();

        EditorUtility.SetDirty(cfg);
        AssetDatabase.SaveAssets();
        EditorGUIUtility.PingObject(cfg);

        Debug.Log(
            $"[TrialConfigBaker] Updated '{cfg.name}' — thrown={thrown != null}, " +
            $"rollingLogs={rollingLogs != null}, cross={cross != null}. " +
            "Track / obstacles / creatures / NPC / coins left unchanged.");
    }

    [MenuItem("Tools/Racing/Bake Thrown / Log / Cross Into Selected Trial Config", true)]
    private static bool BakeThrownLogCrossIntoSelectedValidate()
    {
        return Selection.activeObject is TrialConfig;
    }
}
#endif
