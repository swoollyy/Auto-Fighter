using UnityEngine;

/// <summary>
/// Physics-driven projectile spawned by CarTurretController.
/// - Applies an initial forward impulse / velocity
/// - Optional gravity drop (enable on the Rigidbody)
/// - Tracks lifetime and max travel distance
/// - Uses trigger collider for simple overlap damage
/// - Awards SPROCKETS (not coins) when destroying targets
/// </summary>
[RequireComponent(typeof(Collider))]
[RequireComponent(typeof(Rigidbody))]
public class RacingBullet : MonoBehaviour
{
    [Header("Bullet Defaults (can be overridden by Init)")]
    [SerializeField] private float speed = 60f;          // initial launch speed
    [SerializeField] private float damage = 10f;
    [SerializeField] private float maxDistance = 60f;
    [SerializeField] private float maxLifetime = 3f;

    [Header("Physics")]
    [Tooltip("If true we set velocity directly. If false we use AddForce(Impulse).")]
    [SerializeField] private bool setVelocityDirect = true;
    [Tooltip("Force mode used when not setting velocity directly.")]
    [SerializeField] private ForceMode launchForceMode = ForceMode.Impulse;
    [Tooltip("Enable ContinuousDynamic for fast bullets to reduce tunneling.")]
    [SerializeField] private bool continuousCollision = true;

    [Header("Hit Filtering")]
    [SerializeField] private LayerMask hitLayers = ~0;  // what we can damage

    [Header("Sprocket Rewards")]
    [Tooltip("Base sprockets awarded when this bullet destroys a target.")]
    [SerializeField] private int baseSprocketReward = 1;

    [Header("Hit Effects")]
    [Tooltip("VFX spawned on hit.")]
    [SerializeField] private GameObject hitVFX;
    [Tooltip("Lifetime of hit VFX.")]
    [SerializeField] private float hitVFXLifetime = 2f;
    [Tooltip("Sound played on hit.")]
    [SerializeField] private AudioClip hitSound;
    [SerializeField, Range(0f, 1f)] private float hitSoundVolume = 0.8f;

    [Header("Kill Effects")]
    [Tooltip("Sound played when bullet destroys a target.")]
    [SerializeField] private AudioClip killSound;
    [SerializeField, Range(0f, 1f)] private float killSoundVolume = 1f;

    private Collider _ownerCollider;
    private Vector3 _startPos;
    private float _age;

    private Rigidbody _rb;
    private Collider _col;

    private void Awake()
    {
        _col = GetComponent<Collider>();
        _rb = GetComponent<Rigidbody>();

        // Keep trigger so we manually decide what counts as a hit.
        _col.isTrigger = true;

        // Recommended rigidbody setup for a projectile
        _rb.useGravity = _rb.useGravity; // leave as authored (can toggle per prefab)
        _rb.drag = 0f;
        _rb.angularDrag = 0f;
        _rb.interpolation = RigidbodyInterpolation.None;
        _rb.collisionDetectionMode = continuousCollision
            ? CollisionDetectionMode.ContinuousDynamic
            : CollisionDetectionMode.Discrete;

        _startPos = transform.position;
    }

    /// <summary>
    /// Called by CarTurretController right after instantiation.
    /// </summary>
    /// <param name="damage">Damage applied on hit.</param>
    /// <param name="speed">Launch speed (magnitude of initial forward velocity).</param>
    /// <param name="range">Max travel distance before auto-destroy.</param>
    /// <param name="lifetime">Max lifetime (seconds) before auto-destroy.</param>
    /// <param name="owner">Collider to ignore (car root collider).</param>
    public void Init(float damage, float speed, float range, float lifetime, Collider owner)
    {
        this.damage = damage;
        this.speed = speed;
        this.maxDistance = range;
        this.maxLifetime = lifetime;
        this._ownerCollider = owner;

        _startPos = transform.position;
        _age = 0f;

        Launch();
    }

    private void Launch()
    {
        if (_rb == null) return;

        if (setVelocityDirect)
        {
            _rb.velocity = transform.forward * speed;
        }
        else
        {
            _rb.AddForce(transform.forward * speed, launchForceMode);
        }
    }

    private void Update()
    {
        float dt = Time.deltaTime;
        _age += dt;

        if (_age >= maxLifetime)
        {
            Destroy(gameObject);
            return;
        }

        // Distance check (uses current position from physics)
        if ((transform.position - _startPos).sqrMagnitude >= maxDistance * maxDistance)
        {
            Destroy(gameObject);
            return;
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        // Ignore owner (car + children)
        if (_ownerCollider != null)
        {
            if (other == _ownerCollider) return;
            if (other.transform.root == _ownerCollider.transform.root) return;
        }

        // Layer filtering
        if (((1 << other.gameObject.layer) & hitLayers) == 0)
            return;

        // Allow obstacles or anything implementing IDamageable
        HandleHit(other, transform.position);
    }

    private void HandleHit(Collider other, Vector3 hitPoint)
    {
        // Check if target is damageable
        var dmg = other.GetComponent<ITurretDamageable>() ?? other.GetComponentInParent<ITurretDamageable>();

        if (dmg != null)
        {
            // Use new turret-specific interface that returns kill status and reward
            bool killed = dmg.ApplyTurretDamage(damage, out int sprocketReward);

            if (killed)
            {
                // Award sprockets for the kill
                AwardSprockets(sprocketReward > 0 ? sprocketReward : baseSprocketReward, hitPoint);
                PlayKillEffects(hitPoint);
            }
            else
            {
                PlayHitEffects(hitPoint);
            }
        }
        else
        {
            // Fallback: try legacy IDamageable
            var legacyDmg = other.GetComponent<IDamageable>() ?? other.GetComponentInParent<IDamageable>();
            if (legacyDmg != null)
            {
                legacyDmg.ApplyDamage(damage);

                // For legacy targets, just award base sprockets and assume kill
                // (since we can't know if it died)
                AwardSprockets(baseSprocketReward, hitPoint);
            }

            PlayHitEffects(hitPoint);
        }

        Destroy(gameObject);
    }

    /// <summary>
    /// Awards sprockets to the player with full FX.
    /// </summary>
    private void AwardSprockets(int amount, Vector3 position)
    {
        if (amount <= 0) return;

        // Register with GameManager
        var gm = GameManager_Racing.Instance;
        if (gm != null)
        {
            gm.RegisterSprocketGain(amount);
        }

        // Add to player's sprockets
        var skillMgr = RacingSkillTreeManager.Instance;
        if (skillMgr != null)
        {
            skillMgr.AddSprockets(amount);
        }

        // Show popup
        if (RacingPopups.IsReady)
        {
            RacingPopups.SprocketGain(amount, position + Vector3.up * 0.5f);
        }

        // Screen flash
        ScreenFlashManager.Sprocket(amount);
    }

    private void PlayHitEffects(Vector3 position)
    {
        // Spawn hit VFX
        if (hitVFX != null)
        {
            var vfx = Instantiate(hitVFX, position, Quaternion.identity);
            Destroy(vfx, hitVFXLifetime);
        }

        // Play hit sound
        if (hitSound != null)
        {
            AudioSource.PlayClipAtPoint(hitSound, position, hitSoundVolume);
        }
    }

    private void PlayKillEffects(Vector3 position)
    {
        // Spawn hit VFX (same as regular hit)
        if (hitVFX != null)
        {
            var vfx = Instantiate(hitVFX, position, Quaternion.identity);
            Destroy(vfx, hitVFXLifetime);
        }

        // Play kill sound (or fall back to hit sound)
        AudioClip clip = killSound != null ? killSound : hitSound;
        if (clip != null)
        {
            AudioSource.PlayClipAtPoint(clip, position, killSoundVolume);
        }
    }
}

/// <summary>
/// Enhanced damage interface for turret targets.
/// Returns whether the target was killed and the sprocket reward amount.
/// </summary>
public interface ITurretDamageable
{
    /// <summary>
    /// Apply damage from turret. Returns true if this damage killed the target.
    /// </summary>
    /// <param name="amount">Damage amount</param>
    /// <param name="sprocketReward">Out: sprocket reward for killing this target (0 to use bullet default)</param>
    /// <returns>True if target was destroyed by this damage</returns>
    bool ApplyTurretDamage(float amount, out int sprocketReward);
}

/// <summary>
/// Simple damage interface for anything the turret can shoot (legacy support).
/// </summary>
public interface IDamageable
{
    void ApplyDamage(float amount);
}