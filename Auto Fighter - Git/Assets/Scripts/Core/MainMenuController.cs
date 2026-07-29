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
        [Tooltip("Optional root CanvasGroup for your base menu (Play/Options/Quit area). " +
                 "Used to block controller raycasts while modal panels are open.")]
        [SerializeField] private CanvasGroup mainMenuCanvasGroup;

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
        [Tooltip("Optional cursor-speed slider in options. Saves to SaveSystem and applies globally.")]
        [SerializeField] private Slider cursorSpeedSlider;
        [Tooltip("Optional value label for cursor speed (e.g. '1300').")]
        [SerializeField] private TMP_Text cursorSpeedValueLabel;
        [SerializeField] private float cursorSpeedSliderMin = 300f;
        [SerializeField] private float cursorSpeedSliderMax = 1200f;
        [Header("Cursor Speed Display Mapping")]
        [Tooltip("Displayed value when slider is at cursorSpeedSliderMin.")]
        [SerializeField] private float cursorSpeedDisplayMin = 1f;
        [Tooltip("Displayed value when slider is at cursorSpeedSliderMax.")]
        [SerializeField] private float cursorSpeedDisplayMax = 10f;
        [Tooltip("Number of decimal places shown in the display label.")]
        [SerializeField, Range(0, 4)] private int cursorSpeedDisplayDecimals = 2;

        [Header("Name Entry (optional)")]
        [Tooltip("If assigned, this panel opens when Play is pressed and no player name exists.")]
        [SerializeField] private GameObject nameEntryPanel;
        [Tooltip("Controller for submitting and saving player names from the panel.")]
        [SerializeField] private NameEntryPanelController nameEntryController;

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

        private SliderCommitNotifier _cursorSpeedCommitNotifier;
        private float _pendingCursorSpeedValue;

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
            if (nameEntryController != null) nameEntryController.Submitted += OnNameSubmitted;
            if (cursorSpeedSlider != null) cursorSpeedSlider.onValueChanged.AddListener(OnCursorSpeedSliderChanged);
            EnsureCursorSpeedSliderCommitNotifier();

            if (resetSaveConfirmPanel != null) resetSaveConfirmPanel.SetActive(false);
            if (optionsPanel != null) optionsPanel.SetActive(false);
            if (nameEntryPanel != null) nameEntryPanel.SetActive(false);
            InitializeCursorSpeedSliderFromSave();
            UpdateModalInputState();

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
            if (nameEntryController != null) nameEntryController.Submitted -= OnNameSubmitted;
            if (cursorSpeedSlider != null) cursorSpeedSlider.onValueChanged.RemoveListener(OnCursorSpeedSliderChanged);
            if (_cursorSpeedCommitNotifier != null)
                _cursorSpeedCommitNotifier.Committed -= OnCursorSpeedSliderCommitted;
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
            if (ShouldAskForName())
            {
                OpenNameEntryPanel();
                return;
            }

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
            UpdateModalInputState();
        }

        private void CloseOptions()
        {
            if (optionsPanel != null)
            {
                optionsPanel.SetActive(false);
                UpdateModalInputState();
            }
        }

        private void InitializeCursorSpeedSliderFromSave()
        {
            if (SaveSystem.Current == null) SaveSystem.Load();

            if (cursorSpeedSlider == null) return;

            cursorSpeedSlider.minValue = cursorSpeedSliderMin;
            cursorSpeedSlider.maxValue = cursorSpeedSliderMax;

            float savedValue = SaveSystem.Current != null
                ? SaveSystem.Current.cursorSpeedPixelsPerSecond
                : SaveData.DefaultCursorSpeedPixelsPerSecond;
            float clamped = Mathf.Clamp(savedValue, cursorSpeedSliderMin, cursorSpeedSliderMax);
            cursorSpeedSlider.SetValueWithoutNotify(clamped);
            _pendingCursorSpeedValue = clamped;
            UpdateCursorSpeedValueLabel(clamped);
        }

        private void OnCursorSpeedSliderChanged(float nextValue)
        {
            float clamped = Mathf.Clamp(nextValue, cursorSpeedSliderMin, cursorSpeedSliderMax);
            _pendingCursorSpeedValue = clamped;
            UpdateCursorSpeedValueLabel(clamped);
        }

        private void OnCursorSpeedSliderCommitted(float committedValue)
        {
            if (SaveSystem.Current == null) SaveSystem.Load();
            if (SaveSystem.Current == null) return;

            float clamped = Mathf.Clamp(committedValue, cursorSpeedSliderMin, cursorSpeedSliderMax);
            SaveSystem.Current.cursorSpeedPixelsPerSecond = clamped;
            SaveSystem.Save();
        }

        private void UpdateCursorSpeedValueLabel(float value)
        {
            if (cursorSpeedValueLabel != null)
            {
                float mapped = MapSliderValueToDisplayValue(value);
                cursorSpeedValueLabel.text = mapped.ToString($"F{cursorSpeedDisplayDecimals}");
            }
        }

        private float MapSliderValueToDisplayValue(float sliderValue)
        {
            float sliderRange = cursorSpeedSliderMax - cursorSpeedSliderMin;
            if (sliderRange <= 0.0001f) return cursorSpeedDisplayMin;

            float t = Mathf.InverseLerp(cursorSpeedSliderMin, cursorSpeedSliderMax, sliderValue);
            return Mathf.Lerp(cursorSpeedDisplayMin, cursorSpeedDisplayMax, t);
        }

        private void EnsureCursorSpeedSliderCommitNotifier()
        {
            if (cursorSpeedSlider == null) return;

            _cursorSpeedCommitNotifier = cursorSpeedSlider.GetComponent<SliderCommitNotifier>();
            if (_cursorSpeedCommitNotifier == null)
                _cursorSpeedCommitNotifier = cursorSpeedSlider.gameObject.AddComponent<SliderCommitNotifier>();

            _cursorSpeedCommitNotifier.Committed -= OnCursorSpeedSliderCommitted;
            _cursorSpeedCommitNotifier.Committed += OnCursorSpeedSliderCommitted;
        }

        private bool ShouldAskForName()
        {
            if (nameEntryPanel == null || nameEntryController == null) return false;
            return string.IsNullOrWhiteSpace(SaveSystem.Current != null ? SaveSystem.Current.playerName : string.Empty);
        }

        private void OpenNameEntryPanel()
        {
            if (nameEntryPanel == null) return;
            nameEntryPanel.SetActive(true);
            UpdateModalInputState();
        }

        private void OnNameSubmitted(string playerName)
        {
            if (nameEntryPanel != null) nameEntryPanel.SetActive(false);
            UpdateModalInputState();
            RefreshUI();
            Debug.Log($"[MainMenuController] Player name set to '{playerName}'.");
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
                // Confirm UI must not live under OptionsPanel — that parent is inactive
                // until Options opens, which hid this dialog previously.
                resetSaveConfirmPanel.SetActive(true);
                UpdateModalInputState();
                return;
            }

            ConfirmResetSave();
        }

        private void ConfirmResetSave()
        {
            SaveSystem.Delete();
            SaveSystem.Load();
            if (resetSaveConfirmPanel != null) resetSaveConfirmPanel.SetActive(false);
            InitializeCursorSpeedSliderFromSave();
            UpdateModalInputState();
            RefreshUI();
            Debug.Log("[MainMenuController] Save wiped. Menu refreshed.");
        }

        private void CancelResetSave()
        {
            if (resetSaveConfirmPanel != null)
            {
                resetSaveConfirmPanel.SetActive(false);
                UpdateModalInputState();
            }
        }

        private void UpdateModalInputState()
        {
            bool modalOpen =
                (optionsPanel != null && optionsPanel.activeInHierarchy) ||
                (nameEntryPanel != null && nameEntryPanel.activeInHierarchy) ||
                (resetSaveConfirmPanel != null && resetSaveConfirmPanel.activeInHierarchy);

            bool canUseCanvasGroupBlocking = mainMenuCanvasGroup != null
                                             && !IsChildOfMainMenuGroup(optionsPanel)
                                             && !IsChildOfMainMenuGroup(nameEntryPanel)
                                             && !IsChildOfMainMenuGroup(resetSaveConfirmPanel);

            if (canUseCanvasGroupBlocking)
            {
                mainMenuCanvasGroup.interactable = !modalOpen;
                mainMenuCanvasGroup.blocksRaycasts = !modalOpen;
            }

            // Always keep explicit button gating so base controls never click
            // through modal panels, even when CanvasGroup can't be safely used.
            if (playButton != null) playButton.interactable = !modalOpen;
            if (optionsButton != null) optionsButton.interactable = !modalOpen;
            if (quitButton != null) quitButton.interactable = !modalOpen;
            if (resetSaveButton != null) resetSaveButton.interactable = !modalOpen && HasProgress();
        }

        private bool IsChildOfMainMenuGroup(GameObject panel)
        {
            if (mainMenuCanvasGroup == null || panel == null) return false;
            return panel.transform.IsChildOf(mainMenuCanvasGroup.transform);
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
