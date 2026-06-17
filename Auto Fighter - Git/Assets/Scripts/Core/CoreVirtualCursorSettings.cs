using UnityEngine;

namespace AutoFighter.Core
{
    /// <summary>
    /// Edit this asset in the Project window (Assets/Resources/CoreVirtualCursorSettings.asset),
    /// or via menu: AutoFighter &gt; Virtual Cursor Settings.
    /// The runtime cursor object is auto-created and is not configured in the Hierarchy.
    /// </summary>
    [CreateAssetMenu(menuName = "AutoFighter/Core/Virtual Cursor Settings", fileName = "CoreVirtualCursorSettings")]
    public class CoreVirtualCursorSettings : ScriptableObject
    {
        public const string DefaultResourcesPath = "CoreVirtualCursorSettings";

        [Header("Assign your cursor prefab here")]
        [Tooltip("Prefab for the on-screen cursor (RectTransform root with an Image). Drag your in-game cursor prefab here.")]
        [SerializeField] private RectTransform cursorVisualPrefab;

        public RectTransform CursorVisualPrefab => cursorVisualPrefab;
    }
}
