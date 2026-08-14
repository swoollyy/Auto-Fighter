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
    Bounce,
    SideShooter
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
    [Tooltip("Minimum impact speed (m/s) to knock the tree down. Below this, the tree won't topple.")]
    [Min(0f)] public float treeToppleMinVelocity = 2f;
    [Tooltip("Impulse force = impactSpeed * this scale, then clamped to min/max. Higher = tree falls harder.")]
    [SerializeField, Min(0f)] private float treeToppleImpulseScale = 2f;
    [SerializeField, Min(0f)] private float treeToppleImpulseMin = 3f;
    [SerializeField, Min(0f)] private float treeToppleImpulseMax = 25f;
    [Tooltip("Height above pivot (meters) where the push force is applied. Higher = more rotation, less slide.")]
    [SerializeField, Min(0.1f)] private float treeToppleForceHeight = 2f;

    [Header("Impact comic (Crash popup — WHAM / KAPOW)")]
    [SerializeField] private bool enableTreeToppleCrashPopup = true;
    [SerializeField, Min(0f)] private float treeToppleCrashPopupHeight = 1.2f;
    [Tooltip("When this prop hits another special mover / prop hard enough, spawn Crash text at the contact.")]
    [SerializeField] private bool enablePropClashCrashPopup = true;
    [SerializeField, Min(0f)] private float propClashPopupHeight = 1f;
    [SerializeField, Min(0f)] private float propClashMinRelativeSpeed = 2.5f;
    [SerializeField, Min(0f)] private float propClashPairCooldown = 0.2f;

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
            if (GameManager_Racing.Instance != null)
                GameManager_Racing.Instance.RegisterObstacleReward(rewardCurrency);
        }

        Destroy(gameObject);
    }

    /// <summary>
    /// Tree fall: apply a physics impulse so the tree topples in the impact direction. No animation – pure physics.
    /// </summary>
    private void ToppleTree(Collision collision)
    {
        if (_treeToppled) return;
        _treeToppled = true;

        var bounceBack = GetComponent<TrackObstacleBounceBack>();
        if (bounceBack != null)
            bounceBack.enabled = false;

        var rb = GetComponentInParent<Rigidbody>();
        if (rb == null) return;

        Transform pivot = rb.transform;

        // Fall direction = direction of impact (tree falls away from the hitter).
        Vector3 fallDir = collision.relativeVelocity;
        if (fallDir.sqrMagnitude < 0.01f)
            fallDir = pivot.position - collision.transform.position;
        fallDir.y = 0f;
        if (fallDir.sqrMagnitude < 0.01f)
            fallDir = pivot.forward;
        fallDir.Normalize();

        float impactSpeed = collision.relativeVelocity.magnitude;
        float impulse = Mathf.Clamp(impactSpeed * treeToppleImpulseScale, treeToppleImpulseMin, treeToppleImpulseMax);

        rb.isKinematic = false;
        rb.useGravity = true;
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        rb.constraints = RigidbodyConstraints.None;

        // Apply force at a point above the base so the tree rotates around its base instead of just sliding.
        Vector3 applyPoint = pivot.position + Vector3.up * treeToppleForceHeight;
        rb.AddForceAtPosition(fallDir * impulse, applyPoint, ForceMode.Impulse);

        // RollingLogAlongTrack already spawns CrashWorld in PlayRacingObstacleHitFeedback — skip duplicate.
        bool hitByRollingLog = collision.collider != null &&
            collision.collider.GetComponentInParent<RollingLogAlongTrack>() != null;

        if (enableTreeToppleCrashPopup && RacingPopups.IsReady && !hitByRollingLog)
            RacingPopups.CrashWorld(applyPoint + Vector3.up * treeToppleCrashPopupHeight);
    }

    // NEW: obstacle-on-obstacle impact damage when one was launched by the forcefield
    private void OnCollisionEnter(Collision collision)
    {
        // Tree: topple on collision if impact is strong enough.
        if (obstacleType == ObstacleTyping.Tree && !_treeToppled)
        {
            float impactSpeed = collision.relativeVelocity.magnitude;
            if (impactSpeed >= treeToppleMinVelocity)
                ToppleTree(collision);
            return;
        }

        var hitCol = collision.collider;
        if (hitCol != null &&
            hitCol.GetComponentInParent<CarController>() == null &&
            RacingObstacleCollisionPopups.IsObstacleBuddy(hitCol))
        {
            RacingObstacleCollisionPopups.TrySpawnObstacleClash(
                transform.root,
                hitCol.transform.root,
                collision,
                hitCol,
                collision.relativeVelocity.magnitude,
                propClashMinRelativeSpeed,
                propClashPopupHeight,
                propClashPairCooldown,
                enablePropClashCrashPopup);
        }

        var otherRb = collision.rigidbody;
        if (!otherRb) return;

        var myRb = GetComponentInParent<Rigidbody>();
        if (myRb == null) return;

        ForcefieldImpactDamageHelper.TryApply(
            collision,
            myRb,
            _lastImpactDamageTime,
            impactDamageCooldown,
            minRelativeSpeed: 0f);
    }
}