using TMPro; // ADD
using UnityEngine;
using UnityEngine.UI;

public class UIManager_Racing : MonoBehaviour
{
    [Header("Fuel UI")]
    [SerializeField] private Image fuelFillImage;  // Image set to Filled (Horizontal)

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
        // Ensure overlay is hidden on scene start
        HideRunComplete();
        HideRunCoins();

    }

    private void Update()
    {
        if (car == null || fuelFillImage == null)
            return;

        fuelFillImage.fillAmount = car.FuelPercent;
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
}