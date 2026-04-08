using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// UI component to display the mash progress gauge during crash recovery.
/// Shows a progress bar that fills with clicks and drains over time.
/// </summary>
public class MashGaugeUI : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private CarController carController;
    [SerializeField] private GameObject gaugeContainer;
    [SerializeField] private Image gaugeFillImage;
    [SerializeField] private Image gaugeBackgroundImage;
    [SerializeField] private Image peakMarker;
    
    [Header("Optional Elements")]
    [SerializeField] private TextMeshProUGUI percentText;
    [SerializeField] private TextMeshProUGUI clickCountText;
    [SerializeField] private GameObject maxedIndicator;
    
    [Header("Colors")]
    [SerializeField] private Gradient gaugeColorGradient;
    [SerializeField] private Color peakMarkerColor = Color.white;
    [SerializeField] private Color maxedColor = Color.yellow;
    
    [Header("Animation")]
    [SerializeField] private float smoothSpeed = 10f;
    [SerializeField] private bool pulseOnClick = true;
    [SerializeField] private float pulseScale = 1.1f;
    [SerializeField] private float pulseDuration = 0.1f;
    
    [Header("Visibility")]
    [SerializeField] private float showDelay = 0.1f;
    [SerializeField] private float hideDelay = 0.5f;
    
    // Runtime
    private float _displayedValue;
    private float _pulseTimer;
    private Vector3 _originalScale;
    private bool _wasActive;
    private float _hideTimer;
    private int _lastClickCount;
    
    private void Awake()
    {
        if (gaugeFillImage)
            _originalScale = gaugeFillImage.transform.localScale;
        
        // Initialize gradient if not set
        if (gaugeColorGradient == null || gaugeColorGradient.colorKeys.Length == 0)
        {
            gaugeColorGradient = new Gradient();
            gaugeColorGradient.SetKeys(
                new GradientColorKey[] {
                    new GradientColorKey(Color.red, 0f),
                    new GradientColorKey(Color.yellow, 0.5f),
                    new GradientColorKey(Color.green, 0.8f),
                    new GradientColorKey(Color.cyan, 1f)
                },
                new GradientAlphaKey[] {
                    new GradientAlphaKey(1f, 0f),
                    new GradientAlphaKey(1f, 1f)
                }
            );
        }
        
        // Try to find car controller if not assigned
        if (!carController)
            carController = FindObjectOfType<CarController>();
        
        // Hide initially
        if (gaugeContainer)
            gaugeContainer.SetActive(false);
    }
    
    private void Update()
    {
        if (!carController) return;
        
        bool isActive = carController.IsFlipMashUiVisible;
        
        // Handle visibility
        if (isActive && !_wasActive)
        {
            // Just became active
            ShowGauge();
        }
        else if (!isActive && _wasActive)
        {
            // Just became inactive - start hide timer
            _hideTimer = hideDelay;
        }
        
        // Hide after delay
        if (!isActive && gaugeContainer && gaugeContainer.activeSelf)
        {
            _hideTimer -= Time.deltaTime;
            if (_hideTimer <= 0f)
                HideGauge();
        }
        
        _wasActive = isActive;
        
        // Update visuals if visible
        if (gaugeContainer && gaugeContainer.activeSelf)
        {
            UpdateGaugeVisuals();
        }
    }
    
    private void ShowGauge()
    {
        if (gaugeContainer)
            gaugeContainer.SetActive(true);
        
        _displayedValue = 0f;
        _lastClickCount = 0;
        
        if (maxedIndicator)
            maxedIndicator.SetActive(false);
    }
    
    private void HideGauge()
    {
        if (gaugeContainer)
            gaugeContainer.SetActive(false);
    }
    
    private void UpdateGaugeVisuals()
    {
        float targetValue = carController.MashGaugeValue;
        float peakValue = carController.MashGaugePeakValue;
        bool isMaxed = carController.MashGaugeMaxed;
        int clickCount = carController.TotalMashClicksThisSession;
        
        // Smooth the displayed value
        _displayedValue = Mathf.Lerp(_displayedValue, targetValue, Time.deltaTime * smoothSpeed);
        
        // Detect click for pulse
        if (clickCount > _lastClickCount && pulseOnClick)
        {
            TriggerPulse();
        }
        _lastClickCount = clickCount;
        
        // Update fill
        if (gaugeFillImage)
        {
            gaugeFillImage.fillAmount = _displayedValue;
            
            // Color based on value
            Color fillColor = isMaxed ? maxedColor : gaugeColorGradient.Evaluate(_displayedValue);
            gaugeFillImage.color = fillColor;
        }
        
        // Update peak marker
        if (peakMarker)
        {
            peakMarker.gameObject.SetActive(peakValue > 0.01f);
            
            // Position marker at peak
            RectTransform rt = peakMarker.rectTransform;
            RectTransform parentRT = gaugeFillImage?.rectTransform;
            
            if (parentRT)
            {
                float width = parentRT.rect.width;
                rt.anchoredPosition = new Vector2(width * peakValue, rt.anchoredPosition.y);
            }
            
            peakMarker.color = peakMarkerColor;
        }
        
        // Update text
        if (percentText)
        {
            percentText.text = $"{Mathf.RoundToInt(_displayedValue * 100)}%";
        }
        
        if (clickCountText)
        {
            clickCountText.text = $"{clickCount} clicks";
        }
        
        // Maxed indicator
        if (maxedIndicator)
        {
            maxedIndicator.SetActive(isMaxed);
        }
        
        // Handle pulse animation
        if (_pulseTimer > 0f)
        {
            _pulseTimer -= Time.deltaTime;
            float t = 1f - (_pulseTimer / pulseDuration);
            float scale = Mathf.Lerp(pulseScale, 1f, t);
            
            if (gaugeFillImage)
                gaugeFillImage.transform.localScale = _originalScale * scale;
        }
    }
    
    private void TriggerPulse()
    {
        _pulseTimer = pulseDuration;
    }
    
    /// <summary>
    /// Manually set the car controller reference.
    /// </summary>
    public void SetCarController(CarController controller)
    {
        carController = controller;
    }
}
