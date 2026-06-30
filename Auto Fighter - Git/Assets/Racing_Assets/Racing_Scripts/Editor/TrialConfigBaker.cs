#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

/// <summary>
/// Editor utility to create a <see cref="TrialConfig"/> asset from the spawner/generator components
/// currently set up in the open scene. This is the reliable way to "transfer your current inspector
/// settings" into a trial config — it copies the live values (including prefab lists, LayerMasks, and
/// AnimationCurves) exactly, instead of hand-editing YAML.
///
/// Use: open Racer_Incremental.unity, then Tools -> Racing -> Bake Trial Config From Scene.
/// Save it as "Track1" to make your intro trial match the current scene exactly (all override toggles ON).
/// </summary>
public static class TrialConfigBaker
{
    [MenuItem("Tools/Racing/Bake Trial Config From Scene")]
    public static void BakeFromScene()
    {
        var gen = Object.FindObjectOfType<ProceduralTrackGenerator>(true);
        var obstacle = Object.FindObjectOfType<TrackObstacleSpawner>(true);
        var creature = Object.FindObjectOfType<TrackCreatureSpawner>(true);
        var npc = Object.FindObjectOfType<NPCTrafficCarSpawner>(true);

        if (gen == null && obstacle == null && creature == null && npc == null)
        {
            EditorUtility.DisplayDialog(
                "Bake Trial Config",
                "Couldn't find any of ProceduralTrackGenerator / TrackObstacleSpawner / TrackCreatureSpawner / " +
                "NPCTrafficCarSpawner in the open scene. Open your gameplay scene (Racer_Incremental.unity) first.",
                "OK");
            return;
        }

        string path = EditorUtility.SaveFilePanelInProject(
            "Save Trial Config",
            "Track1",
            "asset",
            "Choose where to save the TrialConfig baked from the current scene.");
        if (string.IsNullOrEmpty(path)) return;

        var cfg = ScriptableObject.CreateInstance<TrialConfig>();
        cfg.trialName = System.IO.Path.GetFileNameWithoutExtension(path);

        if (gen != null) cfg.track = gen.CaptureConfig();
        if (obstacle != null) cfg.obstacles = obstacle.CaptureConfig();
        if (creature != null) cfg.creatures = creature.CaptureConfig();
        if (npc != null) cfg.npcTraffic = npc.CaptureConfig();

        AssetDatabase.CreateAsset(cfg, path);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Selection.activeObject = cfg;
        EditorGUIUtility.PingObject(cfg);

        Debug.Log($"[TrialConfigBaker] Baked '{cfg.trialName}' at {path}. " +
                  $"Captured: track={gen != null}, obstacles={obstacle != null}, creatures={creature != null}, npc={npc != null}. " +
                  "All override toggles are ON, so this config now fully drives those systems.");
    }
}
#endif
