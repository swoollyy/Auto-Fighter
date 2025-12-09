using UnityEngine;

/// <summary>
/// Attached to each ice path GameObject. Gradually increases the GroundSurface
/// max speed multiplier while accelerating on ice, creating a cumulative speed boost effect.
/// Reduces friction for extra slide. Works by modifying the GroundSurface component dynamically.
/// </summary>
[RequireComponent(typeof(Collider))]
[RequireComponent(typeof(GroundSurface))]
public class IcePathAccelerator : MonoBehaviour
{
    [Header("Max Speed Boost Settings")]
    [Tooltip("How fast the max speed multiplier increases per second while accelerating (additive).")]
    [SerializeField] private float maxSpeedMultiplierIncreaseRate = 0.15f;

    [Tooltip("Maximum max speed multiplier that can be reached (1.0 = no boost, 2.0 = double speed).")]
    [SerializeField] private float maxSpeedMultiplierCap = 2.5f;

    [Tooltip("Base max speed multiplier when you first enter ice (before any boost accumulates).")]
    [SerializeField] private float baseMaxSpeedMultiplier = 1.3f;

    [Tooltip("How long the boost persists after leaving ice (seconds).")]
    [SerializeField] private float boostDecayDelay = 0.3f;

    [Tooltip("Rate at which multiplier decays back to base after delay (per second).")]
    [SerializeField] private float boostDecayRate = 0.3f;

    [Header("Friction Reduction")]
    [Tooltip("Fraction by which to reduce friction (0-1). 0.8 = reduce by 80%.")]
    [SerializeField, Range(0f, 1f)] private float frictionReduction = 0.8f;

    [Header("Visual Feedback (Optional)")]
    [SerializeField] private bool enableVisualFeedback = true;
    [SerializeField] private ParticleSystem boostParticles;
    [SerializeField] private AudioClip boostLoopClip;
    [SerializeField] private AudioSource boostAudioSource;

    // Track which car is currently on this ice
    private CarController _currentCar;
    private Rigidbody _carRb;
    private Collider _carCollider;

    private GroundSurface _groundSurface;
    private float _currentMaxSpeedMultiplier;
    private float _timeSinceLeft = 0f;
    private bool _wasOnIce = false;

    // Store original friction values to restore later
    private PhysicMaterial _originalMaterial;
    private PhysicMaterial _iceMaterial;
    private float _originalDynamicFriction;
    private float _originalStaticFriction;
    private float _baseGroundMaxSpeedMultiplier;


    private void Awake()
    {
        // Get the GroundSurface component we'll be modifying
        _groundSurface = GetComponent<GroundSurface>();

        // Use whatever is set on the GroundSurface as the true base value.
        // This comes from your prefab / template and we do NOT overwrite it here.
        if (_groundSurface != null)
        {
            _baseGroundMaxSpeedMultiplier = _groundSurface.maxSpeedMultiplier;
            baseMaxSpeedMultiplier = _baseGroundMaxSpeedMultiplier;
        }
        else
        {
            // Safety fallback if someone forgets a GroundSurface
            _baseGroundMaxSpeedMultiplier = baseMaxSpeedMultiplier;
        }

        _currentMaxSpeedMultiplier = baseMaxSpeedMultiplier;

        // Create ice physics material with reduced friction
        _iceMaterial = new PhysicMaterial("IceMaterial_Runtime");
    }


    private void Update()
    {
        if (_currentCar != null && _carRb != null)
        {
            // Car is on ice
            bool isAccelerating = Input.GetKey(KeyCode.W);

            if (isAccelerating)
            {
                // Gradually increase max speed multiplier while accelerating
                _currentMaxSpeedMultiplier = Mathf.Min(
                    _currentMaxSpeedMultiplier + maxSpeedMultiplierIncreaseRate * Time.deltaTime,
                    maxSpeedMultiplierCap
                );
            }
            else
            {
                // Not accelerating - slowly drift back toward base
                _currentMaxSpeedMultiplier = Mathf.MoveTowards(
                    _currentMaxSpeedMultiplier,
                    baseMaxSpeedMultiplier,
                    boostDecayRate * 0.5f * Time.deltaTime
                );
            }

            // Apply to GroundSurface so CarController reads it
            if (_groundSurface != null)
            {
                _groundSurface.maxSpeedMultiplier = _currentMaxSpeedMultiplier;
                // TEMP
                Debug.Log($"[IcePathAccelerator] maxSpeedMul={_currentMaxSpeedMultiplier:F2}");
            }

            _timeSinceLeft = 0f;
            _wasOnIce = true;

            // Visual feedback
            if (enableVisualFeedback)
            {
                float intensity = Mathf.InverseLerp(baseMaxSpeedMultiplier, maxSpeedMultiplierCap, _currentMaxSpeedMultiplier);
                UpdateVisualFeedback(true, intensity);
            }
        }
        else if (_wasOnIce)
        {
            // Car left ice - decay boost after delay
            _timeSinceLeft += Time.deltaTime;

            if (_timeSinceLeft >= boostDecayDelay)
            {
                _currentMaxSpeedMultiplier = Mathf.Max(
                    baseMaxSpeedMultiplier,
                    _currentMaxSpeedMultiplier - boostDecayRate * Time.deltaTime
                );

                if (_currentMaxSpeedMultiplier <= baseMaxSpeedMultiplier + 0.01f)
                {
                    _wasOnIce = false;
                    _currentMaxSpeedMultiplier = baseMaxSpeedMultiplier;
                }
            }

            // Keep updating GroundSurface during decay
            if (_groundSurface != null)
            {
                _groundSurface.maxSpeedMultiplier = _currentMaxSpeedMultiplier;
            }

            if (!_wasOnIce && enableVisualFeedback)
            {
                UpdateVisualFeedback(false, 0f);
            }
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        var car = other.GetComponentInParent<CarController>();
        if (car != null)
        {
            _currentCar = car;
            _carRb = car.GetComponent<Rigidbody>();
            _carCollider = other;

            // Reset multiplier to base on entry
            _currentMaxSpeedMultiplier = baseMaxSpeedMultiplier;

            if (_groundSurface != null)
            {
                _groundSurface.maxSpeedMultiplier = _currentMaxSpeedMultiplier;
            }

            // Store original friction values
            if (_carCollider != null && _carCollider.material != null)
            {
                _originalMaterial = _carCollider.material;
                _originalDynamicFriction = _originalMaterial.dynamicFriction;
                _originalStaticFriction = _originalMaterial.staticFriction;

                _iceMaterial.dynamicFriction = _originalDynamicFriction * (1f - frictionReduction);
                _iceMaterial.staticFriction = _originalStaticFriction * (1f - frictionReduction);
                _iceMaterial.frictionCombine = PhysicMaterialCombine.Minimum;

                _carCollider.material = _iceMaterial;
            }
        }
    }

    private void OnTriggerStay(Collider other)
    {
        if (_currentCar == null)
        {
            var car = other.GetComponentInParent<CarController>();
            if (car != null)
            {
                _currentCar = car;
                _carRb = car.GetComponent<Rigidbody>();
                _carCollider = other;
            }
        }
    }

    private void OnTriggerExit(Collider other)
    {
        var car = other.GetComponentInParent<CarController>();
        if (car != null && car == _currentCar)
        {
            if (_carCollider != null)
            {
                _carCollider.material = _originalMaterial;
            }

            _currentCar = null;
            _carRb = null;
            _carCollider = null;
            // boost decays in Update()
        }
    }


    private void UpdateVisualFeedback(bool active, float intensity)
    {
        if (boostParticles != null)
        {
            if (active && !boostParticles.isPlaying)
            {
                boostParticles.Play();
            }
            else if (!active && boostParticles.isPlaying)
            {
                boostParticles.Stop();
            }

            // Scale particle emission by intensity
            if (active)
            {
                var emission = boostParticles.emission;
                emission.rateOverTime = Mathf.Lerp(10f, 50f, intensity);
            }
        }

        if (boostAudioSource != null && boostLoopClip != null)
        {
            if (active && !boostAudioSource.isPlaying)
            {
                boostAudioSource.clip = boostLoopClip;
                boostAudioSource.loop = true;
                boostAudioSource.Play();
            }
            else if (!active && boostAudioSource.isPlaying)
            {
                boostAudioSource.Stop();
            }

            // Scale volume by intensity
            if (active)
            {
                boostAudioSource.volume = Mathf.Lerp(0.3f, 1f, intensity);
            }
        }
    }

    // Public API for skill tree modifications
    public void SetBoostParameters(float increaseRate, float maxCap, float decayDelay, float decayRate)
    {
        maxSpeedMultiplierIncreaseRate = increaseRate;
        maxSpeedMultiplierCap = maxCap;
        boostDecayDelay = decayDelay;
        boostDecayRate = decayRate;
    }

    public float CurrentMultiplier => _currentMaxSpeedMultiplier;
    public float BoostProgress => maxSpeedMultiplierCap > baseMaxSpeedMultiplier
        ? Mathf.InverseLerp(baseMaxSpeedMultiplier, maxSpeedMultiplierCap, _currentMaxSpeedMultiplier)
        : 0f;

    private void OnDisable()
    {
        // Restore friction if we're disabled while car is on ice
        if (_carCollider != null)
        {
            _carCollider.material = _originalMaterial;
        }

        // Reset GroundSurface to its original prefab/base value
        if (_groundSurface != null)
        {
            _groundSurface.maxSpeedMultiplier = _baseGroundMaxSpeedMultiplier;
        }

        if (enableVisualFeedback)
        {
            UpdateVisualFeedback(false, 0f);
        }

        // Clear state so nothing leaks across runs
        _currentCar = null;
        _carRb = null;
        _carCollider = null;
        _wasOnIce = false;
        _timeSinceLeft = 0f;
        _currentMaxSpeedMultiplier = baseMaxSpeedMultiplier;
    }


}