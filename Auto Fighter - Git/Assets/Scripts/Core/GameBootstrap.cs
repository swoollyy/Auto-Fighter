using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace AutoFighter.Core
{
    /// <summary>
    /// Lives in the Boot scene. Runs once at app start: initializes the
    /// save system, loads (or creates) the single save slot, then hands off
    /// to the next scene (typically MainMenu).
    /// </summary>
    [DefaultExecutionOrder(-10000)]
    public class GameBootstrap : MonoBehaviour
    {
        public static GameBootstrap Instance { get; private set; }

        [Header("Flow")]
        [Tooltip("Scene to load once bootstrapping is complete. Must be in Build Settings.")]
        [SerializeField] private string nextSceneName = "MainMenu";

        [Tooltip("Minimum time the boot screen stays visible, in seconds. " +
                 "Prevents a one-frame flash on fast machines.")]
        [SerializeField] private float minBootSeconds = 0.25f;

        [Header("Debug")]
        [Tooltip("If true, any existing save is wiped on boot. Editor-only safety valve.")]
        [SerializeField] private bool wipeSaveOnBoot = false;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            DontDestroyOnLoad(gameObject);
        }

        private void Start()
        {
            StartCoroutine(BootRoutine());
        }

        private IEnumerator BootRoutine()
        {
            float started = Time.realtimeSinceStartup;

#if UNITY_EDITOR
            if (wipeSaveOnBoot)
            {
                Debug.LogWarning("[GameBootstrap] wipeSaveOnBoot is ON — deleting existing save.");
                SaveSystem.Delete();
            }
#endif

            var data = SaveSystem.Load();
            Debug.Log($"[GameBootstrap] Save loaded. " +
                      $"Version={data.version}, " +
                      $"Player='{(string.IsNullOrEmpty(data.playerName) ? "<unset>" : data.playerName)}', " +
                      $"Path={SaveSystem.SavePath}");

            float elapsed = Time.realtimeSinceStartup - started;
            if (elapsed < minBootSeconds)
                yield return new WaitForSecondsRealtime(minBootSeconds - elapsed);

            if (string.IsNullOrEmpty(nextSceneName))
            {
                Debug.LogError("[GameBootstrap] nextSceneName is empty — nowhere to go.");
                yield break;
            }

            if (SceneLoader.Instance != null)
            {
                SceneLoader.Instance.Load(nextSceneName);
                while (SceneLoader.Instance.IsLoading) yield return null;
            }
            else
            {
                Debug.LogWarning("[GameBootstrap] No SceneLoader found — falling back to a hard cut.");
                var op = SceneManager.LoadSceneAsync(nextSceneName);
                while (op != null && !op.isDone) yield return null;
            }
        }

        private void OnApplicationQuit()
        {
            if (SaveSystem.Current != null) SaveSystem.Save();
        }

#if UNITY_ANDROID || UNITY_IOS
        private void OnApplicationPause(bool paused)
        {
            if (paused && SaveSystem.Current != null) SaveSystem.Save();
        }
#endif
    }
}
