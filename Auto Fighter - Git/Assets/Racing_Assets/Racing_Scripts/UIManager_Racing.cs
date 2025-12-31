using TMPro; // ADD
using UnityEngine;
using UnityEngine.UI;

public class UIManager_Racing : MonoBehaviour
{
    [Header("Fuel UI")]
    [SerializeField] private Image fuelFillImage;  // Image set to Filled (Horizontal)
    [SerializeField] private TMP_Text fuelText;    // NEW: Shows "85 / 100" or "85%"
    [SerializeField] private bool showFuelAsPercent = false;

    [Header("Car HP UI")]
    [SerializeField] private Image hpFillImage;    // Image set to Filled (Horizontal)
    [SerializeField] private TMP_Text hpText;      // NEW: Shows "75 / 100" or "75%"
    [SerializeField] private bool showHPAsPercent = false;

    [Header("In-Run UI")]
    [SerializeField] private TMP_Text runCoinsLiveText;   // e.g. top-left HUD text: "Coins: 0"

    // NEW: Run Complete UI
    [Header("Run Complete UI")]
    [SerializeField] private GameObject runCompleteRoot;
    [SerializeField] private TMP_Text runCompleteTitle;     // optional (e.g., "Run Complete")
    [SerializeField] private TMP_Text runDistanceText;      // e.g., "Distance: 123 m"
    [SerializeField] private TMP_Text runCoinsText;         // e.g., "Coins: 184"
    [SerializeField] private TMP_Text runRestartHintText;   // e.g., "Press R to restart"

    // NEW: breakdown texts
    [SerializeField] private TMP_Text runDistanceCoinsText;
    [SerializeField] private TMP_Text runPickupCoinsText;
    [SerializeField] private TMP_Text runObstacleCoinsText;

    [Header("Crash Recovery Mash UI")]
    [SerializeField] private GameObject crashRecoveryRoot;      // Parent object to show/hide
    [SerializeField] private Button crashRecoveryButton;        // The button player mashes
    [SerializeField] private Image crashRecoveryFill;           // Progress bar fill
    [SerializeField] private TMP_Text crashRecoveryText;        // "MASH TO RECOVER!" or click count

    // NEW: show the *final* total currency from the skill tree
    [SerializeField] private TMP_Text runTotalCurrencyText; // e.g. "Total Currency: 250"

    [SerializeField] private TMP_Text totalCurrencyText;


    private CarController car;

    /// <summary>
    /// Called by GameManager_Racing once the car is spawned.
    /// </summary>
    public void BindCar(CarController carController)
    {
        car = carController;
        HideRunComplete();
    }

    private void Start()
    {
        HideRunComplete();
        HideRunCoins();
        HideCrashRecoveryUI();  // ADD THIS

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
                fuelText.text = $"{Mathf.RoundToInt(car.FuelPercent * 100)}%";
            else
                fuelText.text = $"{Mathf.RoundToInt(car.CurrentFuel)} / {Mathf.RoundToInt(car.MaxFuel)}";
        }

        // HP bar
        if (hpFillImage != null)
            hpFillImage.fillAmount = car.HPPercent;

        // HP text
        if (hpText != null)
        {
            if (showHPAsPercent)
                hpText.text = $"{Mathf.RoundToInt(car.HPPercent * 100)}%";
            else
                hpText.text = $"{Mathf.RoundToInt(car.CurrentHP)} / {Mathf.RoundToInt(car.MaxHP)}";
        }

        UpdateCrashRecoveryUI();
    }

    // NEW API: show/hide "Run Complete"
    public void ShowRunComplete(
        int distanceMeters,
        int distanceCoins,
        int pickupCoins,
        int obstacleCoins,
        int totalCurrency)
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


        if (runTotalCurrencyText)
        {
            runTotalCurrencyText.gameObject.SetActive(true);
            runTotalCurrencyText.text = $"Total Currency: {totalCurrency}";
        }

        if (runRestartHintText && string.IsNullOrEmpty(runRestartHintText.text))
            runRestartHintText.text = "Press R to restart";

        if (runCompleteTitle && string.IsNullOrEmpty(runCompleteTitle.text))
            runCompleteTitle.text = "Run Complete";

        if (runCompleteRoot)
            runCompleteRoot.SetActive(true);

        // Hide the in-run HUD coins on the summary screen
        if (runCoinsLiveText)
            runCoinsLiveText.gameObject.SetActive(false);
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

        HideTotalCoins();
    }

    public void HideRunCoins()
    {
        if (runCoinsLiveText)
            runCoinsLiveText.gameObject.SetActive(false);
    }

    public void HideTotalCoins()
    {
        if (totalCurrencyText)
            totalCurrencyText.gameObject.SetActive(false);
    }

    public void HideRunComplete()
    {
        if (runCompleteRoot) runCompleteRoot.SetActive(false);
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
        }

        // Update progress if active
        if (shouldShow)
        {
            if (crashRecoveryFill != null)
                crashRecoveryFill.fillAmount = car.FlipMashProgress;

            if (crashRecoveryText != null)
                crashRecoveryText.text = $"MASH! ({car.FlipMashClicksRemaining} left)";
        }
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

}