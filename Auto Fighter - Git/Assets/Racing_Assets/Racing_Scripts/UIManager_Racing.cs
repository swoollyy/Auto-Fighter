using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class UIManager_Racing : MonoBehaviour
{
    [Header("Canvases")]
    [Tooltip("Dialogue canvas (shown during init_dialogue and other sequences).")]
    [SerializeField] private GameObject dialogueCanvas;
    [Tooltip("Game canvas (in-game HUD + skill tree). Disabled during init_dialogue, enabled when dialogue ends.")]
    [SerializeField] private GameObject gameCanvas;

    [Header("Fuel UI")]
    [SerializeField] private Image fuelFillImage;  // Image set to Filled (Horizontal)
    [SerializeField] private TMP_Text fuelText;    // Shows "85 / 100" or "85%"
    [SerializeField] private bool showFuelAsPercent = false;

    [Header("Car HP UI")]
    [SerializeField] private Image hpFillImage;    // Image set to Filled (Horizontal)
    [SerializeField] private TMP_Text hpText;      // Shows "75 / 100" or "75%"
    [SerializeField] private bool showHPAsPercent = false;

    private const KeyCode PAD_X = KeyCode.JoystickButton1; // PS5 Cross (X)

    [Header("In-Run UI")]
    [SerializeField] private TMP_Text runCoinsLiveText;      // e.g. "Coins: 0"
    [SerializeField] private TMP_Text runSprocketsLiveText;  // "Sprockets: 0"

    [Header("Run Complete UI")]
    [SerializeField] private GameObject runCompleteRoot;
    [SerializeField] private TMP_Text runCompleteTitle;
    [SerializeField] private TMP_Text runDistanceText;
    [SerializeField] private TMP_Text runCoinsText;
    [SerializeField] private TMP_Text runRestartHintText;

    // Breakdown texts
    [SerializeField] private TMP_Text runDistanceCoinsText;
    [SerializeField] private TMP_Text runPickupCoinsText;
    [SerializeField] private TMP_Text runObstacleCoinsText;

    // Sprocket breakdown
    [SerializeField] private TMP_Text runSprocketsGainedText;    // "Sprockets Earned: +15"
    [SerializeField] private TMP_Text runTotalSprocketsText;     // "Total Sprockets: 234"

    // Total currency displays
    [SerializeField] private TMP_Text runTotalCurrencyText;      // "Total Coins: 250"
    [SerializeField] private TMP_Text totalCurrencyText;
    [SerializeField] private TMP_Text totalSprocketsText;

    [Header("Crash Recovery Mash UI")]
    [SerializeField] private GameObject crashRecoveryRoot;      // Parent object to show/hide
    [SerializeField] private Button crashRecoveryButton;        // The button player mashes
    [SerializeField] private Image crashRecoveryFill;           // Progress bar fill
    [SerializeField] private TMP_Text crashRecoveryText;        // "MASH! (x left)"

    [Header("Loading Overlay")]
    [SerializeField] private GameObject loadingOverlayRoot;
    [SerializeField] private TMP_Text loadingLabel;

    [Header("Mash Gauge (Progress Bar)")]
    [SerializeField] private Image mashGaugeFill;
    [Tooltip("STATIC max target marker (98%). This must NOT be a 'recent best' marker.")]
    [SerializeField] private Image mashGaugePeakMarker;
    [SerializeField] private TMP_Text mashGaugePercentText;
    [SerializeField] private GameObject mashGaugeMaxedIndicator;
    [SerializeField] private Gradient mashGaugeGradient;

    [Header("Gauge Threshold Markers")]
    [Tooltip("Visual marker for 'good' threshold (e.g., 70%).")]
    [SerializeField] private RectTransform gaugeGoodThresholdMarker;

    [Tooltip("Visual marker for 'max' threshold (e.g., 98%).")]
    [SerializeField] private RectTransform gaugeMaxThresholdMarker;

    [Tooltip("STATIC container for the gauge (background/mask). DO NOT assign the Filled image.")]
    [SerializeField] private RectTransform mashGaugeContainer;

    [Tooltip("Optional: Text label for good threshold.")]
    [SerializeField] private TMP_Text gaugeGoodLabel;

    [Tooltip("Optional: Text label for max threshold.")]
    [SerializeField] private TMP_Text gaugeMaxLabel;

    [Header("Crash Recovery - Smash Button")]
    [SerializeField] private Button smashButton;
    [SerializeField] private TMP_Text smashButtonLabel;

    [Header("Controller UI")]
    [SerializeField] private bool usePlayStationSymbols = true;

    private CarController car;

    /// <summary>
    /// Called by GameManager_Racing once the car is spawned.
    /// </summary>
    public void BindCar(CarController carController)
    {
        car = carController;
        HideRunComplete();

        // Place static threshold markers as soon as the car binds
        UpdateThresholdMarkerPositions();
    }

    public void Awake()
    {
        InitializeMashGaugeGradient();
    }

    private void Start()
    {
        // During init_dialogue only the dialogue canvas should show; game canvas is enabled when dialogue ends (DialogueManager).
        SetGameCanvasVisible(false);

        HideRunComplete();
        HideRunCoins();
        HideCrashRecoveryUI();
        HideTotalSprockets();

        // Bind crash recovery button
        if (crashRecoveryButton != null)
        {
            crashRecoveryButton.onClick.RemoveAllListeners();
            crashRecoveryButton.onClick.AddListener(OnCrashRecoveryButtonClicked);
        }
    }

    private void Update()
    {
        if (car == null) return;

        // Fuel bar
        if (fuelFillImage != null)
            fuelFillImage.fillAmount = car.FuelPercent;

        // Fuel text
        if (fuelText != null)
        {
            if (showFuelAsPercent)
                fuelText.text = $"Fuel - {car.FuelPercent * 100}%";
            else
                fuelText.text = $"Fuel - {Mathf.Round(car.CurrentFuel * 10f) * .1f} / {car.MaxFuel}";
        }

        // HP bar
        if (hpFillImage != null)
            hpFillImage.fillAmount = car.HPPercent;

        // HP text
        if (hpText != null)
        {
            if (showHPAsPercent)
                hpText.text = $"Health - {car.HPPercent * 100}%";
            else
                hpText.text = $"Health - {Mathf.Round(car.CurrentHP * 10f) * .1f} / {car.MaxHP}";
        }

        UpdateCrashRecoveryUI();

        if (car != null && car.IsFlipMashActive)
        {
            if (smashButton != null)
                smashButton.gameObject.SetActive(true);

            if (smashButtonLabel != null)
            {
                string symbol = usePlayStationSymbols
                    ? car.MashSymbolPS
                    : car.MashSymbolXbox;

                smashButtonLabel.text = $"SMASH {GetMashDisplaySymbol()}";
            }

            // Do NOT poll gamepad/keyboard here — CarController.Update already does and calls RegisterFlipMashClick().
            // Only the smash Button's onClick (below) should fire for the on-screen button tap; otherwise we get 2–3 clicks per press.
        }
        else
        {
            if (smashButton != null)
                smashButton.gameObject.SetActive(false);
        }

    }


    public void ShowRunComplete(
        int distanceMeters,
        int distanceCoins,
        int pickupCoins,
        int obstacleCoins,
        int totalCurrency,
        int sprocketsGained = 0,
        int totalSprockets = 0)
    {
        int totalThisRun = distanceCoins + pickupCoins + obstacleCoins;

        if (runDistanceText)
            runDistanceText.text = $"Distance: {distanceMeters} m";

        if (runDistanceCoinsText)
            runDistanceCoinsText.text = $"Distance Coins: {distanceCoins}";

        if (runPickupCoinsText)
            runPickupCoinsText.text = $"Pickup Coins: {pickupCoins}";

        if (runObstacleCoinsText)
            runObstacleCoinsText.text = $"Obstacle Coins: {obstacleCoins}";

        if (runCoinsText)
            runCoinsText.text = $"Coins This Run: {totalThisRun}";

        if (runTotalCurrencyText)
        {
            runTotalCurrencyText.gameObject.SetActive(true);
            runTotalCurrencyText.text = $"Total Coins: {totalCurrency}";
        }

        if (runSprocketsGainedText)
        {
            runSprocketsGainedText.gameObject.SetActive(sprocketsGained > 0);
            runSprocketsGainedText.text = $"Sprockets Earned: +{sprocketsGained}";
        }

        if (runTotalSprocketsText)
        {
            runTotalSprocketsText.gameObject.SetActive(true);
            runTotalSprocketsText.text = $"Total Sprockets: {totalSprockets}";
        }

        if (runRestartHintText && string.IsNullOrEmpty(runRestartHintText.text))
            runRestartHintText.text = "Press R to restart";

        if (runCompleteTitle && string.IsNullOrEmpty(runCompleteTitle.text))
            runCompleteTitle.text = "Run Complete";

        if (runCompleteRoot)
            runCompleteRoot.SetActive(true);

        // Hide the in-run HUD on the summary screen
        HideRunCoins();
        HideRunSprockets();
    }

    public void UpdateRunCoins(int coinsThisRun)
    {
        if (runCoinsLiveText)
            runCoinsLiveText.text = $"Coins: {coinsThisRun}";
    }

    public void ShowRunCoins()
    {
        if (runCoinsLiveText)
            runCoinsLiveText.gameObject.SetActive(true);

        ShowRunSprockets();

        HideTotalCoins();
        HideTotalSprockets();
    }

    public void HideRunCoins()
    {
        if (runCoinsLiveText)
            runCoinsLiveText.gameObject.SetActive(false);

        HideRunSprockets();
    }

    public void HideTotalCoins()
    {
        if (totalCurrencyText)
            totalCurrencyText.gameObject.SetActive(false);

        HideTotalSprockets();
    }

    public void HideTotalSprockets()
    {
        if (totalSprocketsText)
            totalSprocketsText.gameObject.SetActive(false);
    }

    public void HideRunComplete()
    {
        if (runCompleteRoot)
            runCompleteRoot.SetActive(false);

        if (runSprocketsGainedText)
            runSprocketsGainedText.gameObject.SetActive(false);
    }

    // ============================================
    // CRASH RECOVERY MASH UI
    // ============================================

    private bool _crashRecoveryUIActive;

    private void UpdateCrashRecoveryUI()
    {
        if (car == null) return;

        bool shouldShow = car.IsFlipMashActive;

        // Show/hide the UI
        if (shouldShow != _crashRecoveryUIActive)
        {
            _crashRecoveryUIActive = shouldShow;

            if (crashRecoveryRoot != null)
                crashRecoveryRoot.SetActive(shouldShow);

            if (shouldShow)
            {
                // Force layout ready THIS frame, then place markers immediately (no "first click")
                Canvas.ForceUpdateCanvases();
                UpdateThresholdMarkerPositions();
            }
        }

        // Update progress if active
        if (shouldShow)
        {
            if (crashRecoveryFill != null)
                crashRecoveryFill.fillAmount = car.FlipMashProgress;

            if (crashRecoveryText != null)
                crashRecoveryText.text = $"MASH! ({car.FlipMashClicksRemaining} left)";

            UpdateMashGaugeVisuals();
        }
    }

    /// <summary>
    /// Places GOOD (70%) and MAX (98%) markers and also locks the "peak marker"
    /// to MAX (98%) as a static target marker.
    /// These markers NEVER move during gameplay.
    /// </summary>
    private void UpdateThresholdMarkerPositions()
    {
        if (car == null) return;

        // mashGaugeContainer MUST be the full/static bar area (background/mask rect),
        // not the Filled Image rect.
        RectTransform containerRT = mashGaugeContainer;

        // Safe fallback: parent of the fill is usually the stable container.
        if (containerRT == null && mashGaugeFill != null)
            containerRT = mashGaugeFill.rectTransform.parent as RectTransform;

        if (containerRT == null) return;

        float good = Mathf.Clamp01(car.GaugeGoodThreshold);
        float max = Mathf.Clamp01(car.GaugeMaxThreshold);

        SetupAndPlaceMarker(containerRT, gaugeGoodThresholdMarker, good);
        SetupAndPlaceMarker(containerRT, gaugeMaxThresholdMarker, max);

        // IMPORTANT: peak marker is a STATIC "max target" marker (same as max threshold)
        if (mashGaugePeakMarker != null)
            SetupAndPlaceMarker(containerRT, mashGaugePeakMarker.rectTransform, max);

        if (gaugeGoodLabel != null) gaugeGoodLabel.text = $"{Mathf.RoundToInt(good * 100)}%";
        if (gaugeMaxLabel != null) gaugeMaxLabel.text = $"{Mathf.RoundToInt(max * 100)}%";
    }

    /// <summary>
    /// Anchor-based placement: Y is locked by anchors so it cannot "follow" the fill.
    /// </summary>
    private static void SetupAndPlaceMarker(RectTransform containerRT, RectTransform marker, float normalizedY)
    {
        if (containerRT == null || marker == null) return;

        if (!marker.gameObject.activeSelf)
            marker.gameObject.SetActive(true);

        // Markers MUST live under the static container (never under the fill)
        if (marker.parent != containerRT)
            marker.SetParent(containerRT, worldPositionStays: false);

        // Stretch across width, lock Y by anchors (this is the key)
        marker.anchorMin = new Vector2(0f, normalizedY);
        marker.anchorMax = new Vector2(1f, normalizedY);
        marker.pivot = new Vector2(0.5f, 0.5f);

        // No offset. The anchor IS the position.
        marker.anchoredPosition = Vector2.zero;

        // Width matches parent (sizeDelta.x ignored because we stretch)
        Vector2 sd = marker.sizeDelta;
        sd.x = 0f;
        marker.sizeDelta = sd;
    }

    private void UpdateMashGaugeVisuals()
    {
        if (car == null) return;

        float gaugeValue = car.MashGaugeValue;
        float peakValue = car.MashGaugePeakValue;
        bool hasMaxedThisSession = car.MashGaugeMaxed;  // For indicator glow/bonus tracking only

        float goodThreshold = car.GaugeGoodThreshold;
        float maxThreshold = car.GaugeMaxThreshold;

        // Check if CURRENTLY at max (not just "ever reached max")
        bool isCurrentlyAtMax = gaugeValue >= maxThreshold;

        // Update fill
        if (mashGaugeFill != null)
        {
            mashGaugeFill.fillAmount = gaugeValue;

            // Color based on CURRENT tier (not session flag)
            if (isCurrentlyAtMax)
            {
                // Currently at max tier - cyan/gold
                mashGaugeFill.color = Color.cyan;
            }
            else if (gaugeValue >= goodThreshold)
            {
                // Good tier - use gradient in upper range
                mashGaugeFill.color = mashGaugeGradient != null
                    ? mashGaugeGradient.Evaluate(0.7f + (gaugeValue - goodThreshold) / (maxThreshold - goodThreshold) * 0.3f)
                    : Color.green;
            }
            else if (mashGaugeGradient != null)
            {
                // Below good - use gradient normally
                mashGaugeFill.color = mashGaugeGradient.Evaluate(gaugeValue / goodThreshold * 0.7f);
            }
        }

        // PEAK MARKER (unchanged)
        if (mashGaugePeakMarker != null && !mashGaugePeakMarker.gameObject.activeSelf)
            mashGaugePeakMarker.gameObject.SetActive(true);

        // Update percent text with tier indicator - USE CURRENT VALUE, NOT SESSION FLAG
        if (mashGaugePercentText != null)
        {
            int percent = Mathf.RoundToInt(gaugeValue * 100);

            if (isCurrentlyAtMax)  // Changed from: if (isMaxed || gaugeValue >= maxThreshold)
                mashGaugePercentText.text = $"{percent}% MAX!";
            else if (gaugeValue >= goodThreshold)
                mashGaugePercentText.text = $"{percent}% GOOD";
            else
                mashGaugePercentText.text = $"{percent}%";
        }

        // Maxed indicator - can still use session flag for persistent glow effect
        // OR change to current value if you want it to turn off when gauge drops
        if (mashGaugeMaxedIndicator != null)
        {
            // Option 1: Stays on once maxed (shows "you hit max at some point!")
            // mashGaugeMaxedIndicator.SetActive(hasMaxedThisSession);

            // Option 2: Only on when currently at max (turns off when gauge drops)
            mashGaugeMaxedIndicator.SetActive(isCurrentlyAtMax);
        }
    }

    public void UpdateRunSprockets(int sprocketsThisRun)
    {
        if (runSprocketsLiveText)
            runSprocketsLiveText.text = $"Sprockets: {sprocketsThisRun}";
    }

    public void ShowRunSprockets()
    {
        if (runSprocketsLiveText)
        {
            var mgr = RacingSkillTreeManager.Instance;
            bool show = mgr != null && (mgr.HasEverEarnedSprockets || mgr.Sprockets > 0);
            runSprocketsLiveText.gameObject.SetActive(show);
        }
    }

    public void HideRunSprockets()
    {
        if (runSprocketsLiveText)
            runSprocketsLiveText.gameObject.SetActive(false);
    }

    private void InitializeMashGaugeGradient()
    {
        if (mashGaugeGradient == null || mashGaugeGradient.colorKeys.Length == 0)
        {
            mashGaugeGradient = new Gradient();
            mashGaugeGradient.SetKeys(
                new GradientColorKey[] {
                    new GradientColorKey(Color.red, 0f),
                    new GradientColorKey(Color.yellow, 0.5f),
                    new GradientColorKey(Color.green, 0.85f),
                    new GradientColorKey(Color.cyan, 1f)
                },
                new GradientAlphaKey[] {
                    new GradientAlphaKey(1f, 0f),
                    new GradientAlphaKey(1f, 1f)
                }
            );
        }
    }

    public void ShowLoading(string message = "Loading...")
    {
        if (loadingLabel) loadingLabel.text = message;
        if (loadingOverlayRoot) loadingOverlayRoot.SetActive(true);
    }

    public void HideLoading()
    {
        if (loadingOverlayRoot) loadingOverlayRoot.SetActive(false);
    }

    public void OnCrashRecoveryButtonClicked()
    {
        if (car != null)
            car.RegisterFlipMashClick();
    }

    public void HideCrashRecoveryUI()
    {
        _crashRecoveryUIActive = false;
        if (crashRecoveryRoot != null)
            crashRecoveryRoot.SetActive(false);
    }

    /// <summary>Show or hide the game canvas (in-game HUD + skill tree). Called by DialogueManager when dialogue starts/ends.</summary>
    public void SetGameCanvasVisible(bool visible)
    {
        if (gameCanvas != null)
            gameCanvas.SetActive(visible);
    }

    /// <summary>Show or hide the dialogue canvas.</summary>
    public void SetDialogueCanvasVisible(bool visible)
    {
        if (dialogueCanvas != null)
            dialogueCanvas.SetActive(visible);
    }

    /// <summary>Game canvas reference (for DialogueManager or others that need to enable it when dialogue ends).</summary>
    public GameObject GameCanvas => gameCanvas;

    /// <summary>Dialogue canvas reference.</summary>
    public GameObject DialogueCanvas => dialogueCanvas;

    private string GetMashDisplaySymbol()
    {
        if (car == null) return "";

        // Pick the intended display set (PS symbols vs Xbox letters)
        string intended = usePlayStationSymbols ? car.MashSymbolPS : car.MashSymbolXbox;

        // If we're not using PS symbols, just return the letter mapping.
        if (!usePlayStationSymbols) return intended;

        // PS symbols are Unicode. If the TMP font asset doesn't contain the glyph,
        // TextMeshPro will show the "square" tofu fallback.
        // So: detect missing glyph and fall back to safe text.
        if (smashButtonLabel == null || smashButtonLabel.font == null) return intended;

        // intended is 1 char for PS ("✕", "◯", "□", "△")
        char c = intended.Length > 0 ? intended[0] : '?';

        if (smashButtonLabel.font.HasCharacter(c))
            return intended;

        // Fallbacks that virtually all fonts have:
        // Cross: X, Circle: O, Square: [ ], Triangle: ^
        switch (car.RequiredMashButton)
        {
            case CarController.FaceButton.Cross: return "X";
            case CarController.FaceButton.Circle: return "O";
            case CarController.FaceButton.Square: return "[]";
            case CarController.FaceButton.Triangle: return "^";
            default: return "X";
        }
    }


}
