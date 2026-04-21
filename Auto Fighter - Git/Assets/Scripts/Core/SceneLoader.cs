using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace AutoFighter.Core
{
    /// <summary>
    /// Persistent async scene-loader with fade + progress bar.
    /// Place one in the Boot scene; it survives scene loads via
    /// <see cref="Object.DontDestroyOnLoad"/>. Call
    /// <see cref="SceneLoader.Instance"/>.<see cref="Load(string)"/> from anywhere.
    /// </summary>
    [DefaultExecutionOrder(-9000)]
    public class SceneLoader : MonoBehaviour
    {
        public static SceneLoader Instance { get; private set; }

        [Header("UI (optional — leave empty for silent loads)")]
        [SerializeField] private CanvasGroup loadingCanvas;
        [SerializeField] private Slider progressBar;

        [Header("Timing")]
        [Tooltip("Minimum time the loading screen stays visible so fast loads don't flash.")]
        [SerializeField] private float minDisplaySeconds = 0.75f;
        [SerializeField] private float fadeSeconds = 0.25f;

        public bool IsLoading { get; private set; }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            DontDestroyOnLoad(gameObject);

            if (loadingCanvas != null)
            {
                loadingCanvas.alpha = 0f;
                loadingCanvas.blocksRaycasts = false;
                loadingCanvas.interactable = false;
            }
        }

        public void Load(string sceneName)
        {
            if (IsLoading)
            {
                Debug.LogWarning($"[SceneLoader] Already loading; ignored request for '{sceneName}'.");
                return;
            }

            if (string.IsNullOrEmpty(sceneName))
            {
                Debug.LogError("[SceneLoader] Load called with empty scene name.");
                return;
            }

            StartCoroutine(LoadRoutine(sceneName));
        }

        private IEnumerator LoadRoutine(string sceneName)
        {
            IsLoading = true;

            yield return Fade(0f, 1f);
            if (progressBar != null) progressBar.value = 0f;

            var op = SceneManager.LoadSceneAsync(sceneName);
            op.allowSceneActivation = false;

            float started = Time.unscaledTime;

            // Unity stalls progress at 0.9 until activation; remap to a full 0–1 bar.
            while (op.progress < 0.9f)
            {
                if (progressBar != null) progressBar.value = op.progress / 0.9f;
                yield return null;
            }
            if (progressBar != null) progressBar.value = 1f;

            float remaining = minDisplaySeconds - (Time.unscaledTime - started);
            if (remaining > 0f) yield return new WaitForSecondsRealtime(remaining);

            op.allowSceneActivation = true;
            while (!op.isDone) yield return null;

            yield return Fade(1f, 0f);
            IsLoading = false;
        }

        private IEnumerator Fade(float from, float to)
        {
            if (loadingCanvas == null) yield break;

            loadingCanvas.blocksRaycasts = to > 0f;
            loadingCanvas.interactable = to > 0f;

            if (fadeSeconds <= 0f)
            {
                loadingCanvas.alpha = to;
                yield break;
            }

            float t = 0f;
            while (t < fadeSeconds)
            {
                t += Time.unscaledDeltaTime;
                loadingCanvas.alpha = Mathf.Lerp(from, to, t / fadeSeconds);
                yield return null;
            }
            loadingCanvas.alpha = to;
        }
    }
}
