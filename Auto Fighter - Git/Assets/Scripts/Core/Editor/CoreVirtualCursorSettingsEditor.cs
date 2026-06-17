#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEngine;

namespace AutoFighter.Core.Editor
{
    [CustomEditor(typeof(CoreVirtualCursorSettings))]
    public class CoreVirtualCursorSettingsEditor : UnityEditor.Editor
    {
        public const string AssetPath = "Assets/Resources/CoreVirtualCursorSettings.asset";

        public override void OnInspectorGUI()
        {
            EditorGUILayout.HelpBox(
                "The virtual cursor is created automatically at runtime (DontDestroyOnLoad).\n\n" +
                "Assign your cursor prefab here — not on the runtime object in the Hierarchy.",
                MessageType.Info);

            DrawDefaultInspector();
        }

        [MenuItem("AutoFighter/Virtual Cursor Settings")]
        private static void SelectSettingsAsset()
        {
            var settings = GetOrCreateSettingsAsset();
            if (settings == null)
            {
                EditorUtility.DisplayDialog(
                    "Virtual Cursor Settings",
                    "Failed to create CoreVirtualCursorSettings.asset.\n" +
                    "Check the Console for errors.",
                    "OK");
                return;
            }

            Selection.activeObject = settings;
            EditorGUIUtility.PingObject(settings);
        }

        public static CoreVirtualCursorSettings GetOrCreateSettingsAsset()
        {
            var settings = AssetDatabase.LoadAssetAtPath<CoreVirtualCursorSettings>(AssetPath);
            if (settings != null)
                return settings;

            if (File.Exists(AssetPath))
            {
                AssetDatabase.ImportAsset(AssetPath, ImportAssetOptions.ForceUpdate);
                settings = AssetDatabase.LoadAssetAtPath<CoreVirtualCursorSettings>(AssetPath);
                if (settings != null)
                    return settings;
            }

            if (!AssetDatabase.IsValidFolder("Assets/Resources"))
                AssetDatabase.CreateFolder("Assets", "Resources");

            settings = ScriptableObject.CreateInstance<CoreVirtualCursorSettings>();
            AssetDatabase.CreateAsset(settings, AssetPath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log($"[CoreVirtualCursor] Created settings asset at {AssetPath}");
            return AssetDatabase.LoadAssetAtPath<CoreVirtualCursorSettings>(AssetPath);
        }
    }
}
#endif
