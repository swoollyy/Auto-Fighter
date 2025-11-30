using UnityEngine;
using DG.Tweening;

[DisallowMultipleComponent]
public class RacingObstacle : MonoBehaviour, IDamageable
{
    [Header("Obstacle Settings")]
    [SerializeField] private bool destructible = true;
    [SerializeField] private float maxHealth = 50f;
    [SerializeField] private float scaleOnHit = 1.07f;
    [SerializeField] private float scalePunchTime = 0.15f;
    [SerializeField] private GameObject destroyVFX;
    [SerializeField] private int rewardCurrency = 3;

    // NEW: Impact damage tuning
    [Header("Forcefield Impact Damage")]
    [Tooltip("Base velocity threshold to qualify as a damaging impact (m/s). Skill chain scales this.")]
    [SerializeField] private float baseImpactVelocityThreshold = 8f;
    [Tooltip("Cooldown (seconds) to avoid repeated damage from sustained contact with the same obstacle.")]
    [SerializeField] private float impactDamageCooldown = 0.25f;

    private float _currentHealth;

    // Cooldown store per other obstacle
    private System.Collections.Generic.Dictionary<int, float> _lastImpactDamageTime = new System.Collections.Generic.Dictionary<int, float>();

    private void Awake()
    {
        _currentHealth = maxHealth;
    }

    public void ApplyDamage(float amount)
    {
        if (!destructible) return;

        _currentHealth -= amount;
        transform.DOPunchScale(Vector3.one * scaleOnHit, scalePunchTime);

        if (_currentHealth <= 0f)
            HandleDestroyed();
    }

    private void HandleDestroyed()
    {
        if (destroyVFX)
            Instantiate(destroyVFX, transform.position, destroyVFX.transform.rotation);

        // Award currency globally
        RacingSkillTreeManager.Instance?.AddCurrency(rewardCurrency);

        // Notify GameManager for breakdown tracking
        if (GameManager_Racing.Instance != null)
        {
            GameManager_Racing.Instance.RegisterObstacleReward(rewardCurrency);
        }

        Destroy(gameObject);
    }

    // NEW: obstacle-on-obstacle impact damage when one was launched by the forcefield
    private void OnCollisionEnter(Collision collision)
    {
        var otherRb = collision.rigidbody;
        if (!otherRb) return;

        // Only consider impacts with other RacingObstacle
        var otherObstacle = otherRb.GetComponent<RacingObstacle>();
        if (!otherObstacle) return;

        // Feature gate via skill unlock
        var mgr = RacingSkillTreeManager.Instance;
        if (mgr == null || !mgr.IsForcefieldImpactDamageUnlocked()) return;

        // At least one of the two must be a recently forcefield-launched obstacle
        bool thisLaunched = false;
        bool otherLaunched = false;

        var tagThis = GetComponent<ForcefieldLaunchTag>();
        if (tagThis && tagThis.IsActive) thisLaunched = true;

        var tagOther = otherRb.GetComponent<ForcefieldLaunchTag>();
        if (tagOther && tagOther.IsActive) otherLaunched = true;

        if (!thisLaunched && !otherLaunched) return;

        // Relative impact speed
        float relSpeed = collision.relativeVelocity.magnitude;


        // Pair cooldown to avoid rapid re-damage
        int otherId = otherRb.GetInstanceID();
        if (_lastImpactDamageTime.TryGetValue(otherId, out float lastT))
        {
            if (Time.time - lastT < impactDamageCooldown) return;
        }
        _lastImpactDamageTime[otherId] = Time.time;

        // Damage amount from skill (base is 1.0)
        float dmg = mgr.GetForcefieldImpactDamageAmount(1f);

        // Apply damage to both
        this.ApplyDamage(dmg);
        otherObstacle.ApplyDamage(dmg);
    }
}