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

    private float _currentHealth;

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

        // NEW: notify GameManager for breakdown tracking
        if (GameManager_Racing.Instance != null)
        {
            GameManager_Racing.Instance.RegisterObstacleReward(rewardCurrency);
        }

        Destroy(gameObject);
    }
}
