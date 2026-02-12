using UnityEngine;
using DG.Tweening;

/// <summary>
/// Obstacle type for behaviour and logic. Rock = default static/destructible; Tree = topples on collision; Cross/Shuttle/Bounce = for future or specialised behaviour.
/// </summary>
public enum ObstacleTyping
{
    Rock,
    Tree,
    Cross,
    Shuttle,
    Bounce
}

[DisallowMultipleComponent]
public class RacingObstacle : MonoBehaviour, IDamageable, ITurretDamageable
{
    [Header("Obstacle Type")]
    [Tooltip("Tree: topples over on collision in the direction of impact. Others: use for logic/spawning.")]
    [SerializeField] private ObstacleTyping obstacleType = ObstacleTyping.Rock;

    [Header("Obstacle Settings")]
    [SerializeField] private bool destructible = true;
    [SerializeField] private float maxHealth = 50f;
    [SerializeField] private float scaleOnHit = 1.07f;
    [SerializeField] private float scalePunchTime = 0.15f;
    [SerializeField] private GameObject destroyVFX;
    [SerializeField] private int rewardCurrency = 3;
    [Header("Tree Topple (ObstacleType.Tree only)")]
    [SerializeField, Min(0.1f)] private float treeToppleDuration = 0.7f;
    [SerializeField] private Ease treeToppleEase = Ease.OutQuad;

    // NEW: Impact damage tuning
    [Header("Forcefield Impact Damage")]
    [Tooltip("Base velocity threshold to qualify as a damaging impact (m/s). Skill chain scales this.")]
    [SerializeField] private float baseImpactVelocityThreshold = 8f;
    [Tooltip("Cooldown (seconds) to avoid repeated damage from sustained contact with the same obstacle.")]
    [SerializeField] private float impactDamageCooldown = 0.25f;

    private float _currentHealth;

    // Track if destroyed by turret to skip coin reward
    private bool _destroyedByTurret = false;

    // Cooldown store per other obstacle
    private System.Collections.Generic.Dictionary<int, float> _lastImpactDamageTime = new System.Collections.Generic.Dictionary<int, float>();

    private bool _treeToppled;

    public ObstacleTyping Type => obstacleType;   

    private void Awake()
    {
        _currentHealth = maxHealth;
    }

    /// <summary>
    /// Legacy damage interface - used by non-turret sources (forcefield, etc).
    /// Awards COINS when destroyed.
    /// </summary>
    public void ApplyDamage(float amount)
    {
        if (!destructible) return;

        _currentHealth -= amount;
        transform.DOPunchScale(Vector3.one * scaleOnHit, scalePunchTime);

        if (_currentHealth <= 0f)
        {
            _destroyedByTurret = false;
            HandleDestroyed();
        }
    }

    /// <summary>
    /// Turret damage interface - used by RacingBullet.
    /// Does NOT award coins (bullet handles sprocket rewards).
    /// </summary>
    public bool ApplyTurretDamage(float amount, out int sprocketReward)
    {
        sprocketReward = rewardCurrency; // Use same value as coin reward

        if (!destructible)
        {
            return false;
        }

        _currentHealth -= amount;
        transform.DOPunchScale(Vector3.one * scaleOnHit, scalePunchTime);

        if (_currentHealth <= 0f)
        {
            _destroyedByTurret = true;
            HandleDestroyed();
            return true; // Was killed
        }

        return false; // Survived
    }

    private void HandleDestroyed()
    {
        if (destroyVFX)
            Instantiate(destroyVFX, transform.position, destroyVFX.transform.rotation);

        // Only award coins if NOT destroyed by turret
        // (turret kills award sprockets via RacingBullet)
        if (!_destroyedByTurret)
        {
            // Award currency globally
            RacingSkillTreeManager.Instance?.AddCurrency(rewardCurrency);

            // Notify GameManager for breakdown tracking
            if (GameManager_Racing.Instance != null)
            {
                GameManager_Racing.Instance.RegisterObstacleReward(rewardCurrency);
            }
        }

        Destroy(gameObject);
    }

    /// <summary>
    /// Tree fall: topple in the direction the hitter is moving (opposite to impact source).
    /// Omnidirectional: hit from left-front -> fall right-back, etc.
    /// </summary>
    private void ToppleTree(Collision collision)
    {
        if (_treeToppled) return;
        _treeToppled = true;

        // Fall direction = opposite to where the hit came from (so "hit from left -> fall right").
        // Use relative velocity: impulse on us is along relativeVelocity; we fall in that direction.
        Vector3 fallDir = collision.relativeVelocity;
        if (fallDir.sqrMagnitude < 0.01f)
            fallDir = transform.position - collision.transform.position;
        fallDir.y = 0f;
        if (fallDir.sqrMagnitude < 0.01f)
            fallDir = transform.forward;
        fallDir.Normalize();

        // Final rotation: tree lies on ground with its up aligned to fall direction (toppled over that way).
        Quaternion fallenRot = Quaternion.FromToRotation(Vector3.up, fallDir);

        // Collider stays enabled so the toppled tree still acts as an obstacle (e.g. car can hit the fallen tree).
        transform.DORotateQuaternion(fallenRot, treeToppleDuration).SetEase(treeToppleEase);
    }

    // NEW: obstacle-on-obstacle impact damage when one was launched by the forcefield
    private void OnCollisionEnter(Collision collision)
    {
        // Tree: topple on any collision, then skip damage logic.
        if (obstacleType == ObstacleTyping.Tree && !_treeToppled)
        {
            ToppleTree(collision);
            return;
        }

        var otherRb = collision.rigidbody;
        if (!otherRb) return;

        // Only consider impacts with other RacingObstacle
        var otherObstacle = otherRb.GetComponent<RacingObstacle>();
        if (!otherObstacle) return;

        // Feature gate via skill unlock (trees and all obstacles take damage when hit by forcefield-launched objects)
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

        // Apply damage to both (as non-turret damage, so coins are awarded)
        this.ApplyDamage(dmg);
        otherObstacle.ApplyDamage(dmg);
    }
}