using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using TMPro;

namespace AutoFighter.Core
{
    /// <summary>
    /// Wires the MainMenu UI (Play / Options / Quit + a Reset Save corner
    /// button) to <see cref="SaveSystem"/> and <see cref="SceneLoader"/>.
    /// All UI refs are optional — assign only what your menu has.
    /// </summary>
    public class MainMenuController : MonoBehaviour
    {
        [Header("Target Scenes (must be in Build Settings)")]
        [SerializeField] private string gameplaySceneName = "Racer_Incremental";

        [Header("Primary Buttons")]
        [SerializeField] private Button playButton;
        [SerializeField] private Button optionsButton;
        [SerializeField] private Button quitButton;

        [Header("Reset Save (corner button)")]
        [FormerlySerializedAs("newGameButton")]
        [SerializeField] private Button resetSaveButton;
        [FormerlySerializedAs("newGameConfirmPanel")]
        [SerializeField] private GameObject resetSaveConfirmPanel;
        [FormerlySerializedAs("newGameConfirmYes")]
        [SerializeField] private Button resetSaveConfirmYes;
        [FormerlySerializedAs("newGameConfirmNo")]
        [SerializeField] private Button resetSaveConfirmNo;

        [Header("Options Panel")]
        [Tooltip("Root GameObject of your options panel. Toggled on/off by the Options button.")]
        [SerializeField] private GameObject optionsPanel;
        [Tooltip("Button inside the options panel that closes it (X / Back).")]
        [SerializeField] private Button optionsCloseButton;

        [Header("Dynamic Labels (optional)")]
        [Tooltip("If assigned, this text becomes 'Continue' when a save has progress, " +
                 "else 'Play'. Leave empty to keep your static 'Play' label.")]
        [SerializeField] private TMP_Text playButtonLabel;
        [SerializeField] private string playText = "Play";
        [SerializeField] private string continueText = "Continue";

        [Tooltip("Greeting shown at the top of the menu. Always visible when assigned.")]
        [SerializeField] private TMP_Text welcomeLabel;
        [Tooltip("Shown when the save has no player name yet (fresh save).")]
        [SerializeField] private string welcomeNewPlayerText = "Welcome, New Player!";
        [Tooltip("Shown when the save has a player name. Use {0} as the name placeholder.")]
        [SerializeField] private string welcomeReturningFormat = "Welcome back, {0}!";

        private void Awake()
        {
            if (SaveSystem.Current == null) SaveSystem.Load();
        }

        private void OnEnable()
        {
            if (playButton != null) playButton.onClick.AddListener(OnPlayClicked);
            if (optionsButton != null) optionsButton.onClick.AddListener(OnOptionsClicked);
            if (quitButton != null) quitButton.onClick.AddListener(OnQuitClicked);

            if (resetSaveButton != null) resetSaveButton.onClick.AddListener(OnResetSaveClicked);
            if (resetSaveConfirmYes != null) resetSaveConfirmYes.onClick.AddListener(ConfirmResetSave);
            if (resetSaveConfirmNo != null) resetSaveConfirmNo.onClick.AddListener(CancelResetSave);

            if (optionsCloseButton != null) optionsCloseButton.onClick.AddListener(CloseOptions);

            if (resetSaveConfirmPanel != null) resetSaveConfirmPanel.SetActive(false);
            if (optionsPanel != null) optionsPanel.SetActive(false);

            RefreshUI();
        }

        private void OnDisable()
        {
            if (playButton != null) playButton.onClick.RemoveListener(OnPlayClicked);
            if (optionsButton != null) optionsButton.onClick.RemoveListener(OnOptionsClicked);
            if (quitButton != null) quitButton.onClick.RemoveListener(OnQuitClicked);

            if (resetSaveButton != null) resetSaveButton.onClick.RemoveListener(OnResetSaveClicked);
            if (resetSaveConfirmYes != null) resetSaveConfirmYes.onClick.RemoveListener(ConfirmResetSave);
            if (resetSaveConfirmNo != null) resetSaveConfirmNo.onClick.RemoveListener(CancelResetSave);

            if (optionsCloseButton != null) optionsCloseButton.onClick.RemoveListener(CloseOptions);
        }

        private void RefreshUI()
        {
            bool hasProgress = HasProgress();
            string playerName = SaveSystem.Current != null ? SaveSystem.Current.playerName : null;
            bool hasName = !string.IsNullOrEmpty(playerName);

            if (playButtonLabel != null)
                playButtonLabel.text = hasProgress ? continueText : playText;

            if (welcomeLabel != null)
            {
                welcomeLabel.gameObject.SetActive(true);
                welcomeLabel.text = hasName
                    ? string.Format(welcomeReturningFormat, playerName)
                    : welcomeNewPlayerText;
            }

            // Reset Save stays visible at all times — it's a corner control,
            // not a progression gate. (Clicking with no progress just no-ops.)
            if (resetSaveButton != null)
                resetSaveButton.interactable = hasProgress;
        }

        private bool HasProgress()
        {
            var d = SaveSystem.Current;
            if (d == null) return false;
            return !string.IsNullOrEmpty(d.playerName)
                   || d.totalRuns > 0
                   || d.softCurrency > 0
                   || d.highestLevelReached > 0;
        }

        // ── Play ────────────────────────────────────────────────────────────

        private void OnPlayClicked()
        {
            if (string.IsNullOrEmpty(gameplaySceneName))
            {
                Debug.LogError("[MainMenuController] Gameplay scene name is empty.");
                return;
            }

            if (SceneLoader.Instance != null)
            {
                SceneLoader.Instance.Load(gameplaySceneName);
            }
            else
            {
                Debug.LogWarning("[MainMenuController] No SceneLoader found — falling back to direct load.");
                SceneManager.LoadScene(gameplaySceneName);
            }
        }

        // ── Options ─────────────────────────────────────────────────────────

        private void OnOptionsClicked()
        {
            if (optionsPanel == null)
            {
                Debug.LogWarning("[MainMenuController] Options Panel is not assigned.");
                return;
            }

            optionsPanel.SetActive(!optionsPanel.activeSelf);
        }

        private void CloseOptions()
        {
            if (optionsPanel != null) optionsPanel.SetActive(false);
        }

        // ── Reset Save ──────────────────────────────────────────────────────

        private void OnResetSaveClicked()
        {
            if (!HasProgress())
            {
                Debug.Log("[MainMenuController] Reset Save ignored — no progress to wipe.");
                return;
            }

            if (resetSaveConfirmPanel != null)
            {
                resetSaveConfirmPanel.SetActive(true);
                return;
            }

            ConfirmResetSave();
        }

        private void ConfirmResetSave()
        {
            SaveSystem.Delete();
            SaveSystem.Load();
            if (resetSaveConfirmPanel != null) resetSaveConfirmPanel.SetActive(false);
            RefreshUI();
            Debug.Log("[MainMenuController] Save wiped. Menu refreshed.");
        }

        private void CancelResetSave()
        {
            if (resetSaveConfirmPanel != null) resetSaveConfirmPanel.SetActive(false);
        }

        // ── Quit ────────────────────────────────────────────────────────────

        private void OnQuitClicked()
        {
            SaveSystem.Save();

#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }
    }
}
