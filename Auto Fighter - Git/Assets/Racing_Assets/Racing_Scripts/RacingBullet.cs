using UnityEngine;

/// <summary>
/// Rigidbody-driven bullet:
/// - Applies an initial forward impulse / velocity
/// - Optional gravity drop (enable on the Rigidbody)
/// - Tracks lifetime and max travel distance
/// - Uses trigger collider for simple overlap damage (same logic as before)
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
        var dmg = other.GetComponent<IDamageable>() ?? other.GetComponentInParent<IDamageable>();
        if (dmg != null)
        {
            dmg.ApplyDamage(damage);
        }

        // TODO: spawn hit VFX / sound at hitPoint

        Destroy(gameObject);
    }
}

/// <summary>
/// Simple damage interface for anything the turret can shoot.
/// </summary>
public interface IDamageable
{
    void ApplyDamage(float amount);
}