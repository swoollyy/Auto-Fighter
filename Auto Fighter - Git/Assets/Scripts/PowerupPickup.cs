using UnityEngine;

[RequireComponent(typeof(Collider))]
[DisallowMultipleComponent]
public sealed class PowerupPickup : MonoBehaviour
{
    [Tooltip("Powerup Id carried by this pickup (e.g., 'collect-all-xp').")]
    public string powerupId;

    private bool _collected; // prevents double-trigger

    [Header("Behaviour")]
    [Tooltip("Seconds before auto-despawn if not collected.")]
    public float lifetime = 10f;

    [Tooltip("Optional initial impulse to make the pickup pop out.")]
    public float spawnImpulse = 2.5f;

    [Header("Feedback")]
    public ParticleSystem pickupVfx;
    public AudioSource audioSource;
    public AudioClip pickupSfx;

    private void Reset()
    {
        var col = GetComponent<Collider>();
        col.isTrigger = true;

        var rb = GetComponent<Rigidbody>();
        if (!rb) rb = gameObject.AddComponent<Rigidbody>();
        rb.isKinematic = true;
        rb.useGravity = false;
    }

    private void OnEnable()
    {
        if (lifetime > 0f)
            Destroy(gameObject, lifetime);

        // Tiny outward push
        var rb = GetComponent<Rigidbody>();
        if (rb && spawnImpulse > 0f)
        {
            var dir = Random.onUnitSphere;
            dir.y = Mathf.Abs(dir.y); // slight up bias
            rb.AddForce(dir.normalized * spawnImpulse, ForceMode.Impulse);
        }

        // Spawn animation
        var tween = GetComponent<PowerupPickupTween>();
        if (tween) tween.PlaySpawn();
    }

    private void OnTriggerEnter(Collider other)
    {
        if (_collected) return;

        var pinball = Pinball.Instance;
        if (!pinball) return;

        // Any Ball collects
        var ball = other.GetComponentInParent<Ball>();
        if (!ball || !ball.isActiveAndEnabled || !ball.IsActive) return;

        if (string.IsNullOrEmpty(powerupId))
        {
            Debug.LogWarning("[PowerupPickup] powerupId is empty. Ensure PowerupSystem assigned it at spawn.");
            return;
        }

        bool ok = PowerupSystem.TryTriggerById(pinball, powerupId, transform.position);
        if (!ok) return;

        _collected = true;

        // Collect feedback
        var tween = GetComponent<PowerupPickupTween>();
        if (tween) tween.PlayCollect();
        if (pickupVfx) Instantiate(pickupVfx, transform.position, Quaternion.identity);

        var col = GetComponent<Collider>();
        if (col) col.enabled = false;

        float delay = 0.05f; // minimum so tween is visible
        if (tween) delay = Mathf.Max(delay, tween.GetCollectDuration());
        if (audioSource && pickupSfx)
        {
            audioSource.PlayOneShot(pickupSfx);
            delay = Mathf.Max(delay, pickupSfx.length);
        }

        Destroy(gameObject, delay);
    }
}