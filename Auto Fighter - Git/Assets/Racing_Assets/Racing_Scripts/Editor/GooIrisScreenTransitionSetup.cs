#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class GooIrisScreenTransitionSetup
{
    [MenuItem("Racing/Setup Goo Iris Screen Transition In Open Scene")]
    public static void SetupInOpenScene()
    {
        var existing = Object.FindObjectOfType<GooIrisScreenTransition>(true);
        if (existing != null)
        {
            Selection.activeGameObject = existing.gameObject;
            Debug.Log("[GooIrisScreenTransitionSetup] Already present in the scene.");
            return;
        }

        var go = new GameObject("GooIrisScreenTransition");
        Undo.RegisterCreatedObjectUndo(go, "Create Goo Iris Screen Transition");
        go.AddComponent<GooIrisScreenTransition>();
        Selection.activeGameObject = go;
        EditorSceneManager.MarkSceneDirty(go.scene);
        Debug.Log("[GooIrisScreenTransitionSetup] Created GooIrisScreenTransition (also auto-spawns at runtime via EnsureExists).");
    }
}
#endif
