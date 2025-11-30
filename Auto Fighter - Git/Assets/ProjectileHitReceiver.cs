using UnityEngine;
/// <summary>
/// Attach to projectile prefab. Allows RacingBullet (which looks for IDamageable)
/// to damage / destroy the projectile by calling ApplyDamage.
/// It forwards to ThrownObstacle.DestroyByPlayer() so director/pool logic remains consistent.
/// </summary>
[DisallowMultipleComponent]
public class ProjectileHitReceiver : MonoBehaviour, IDamageable
{
    private ThrownObstacle _thrown;

    void Awake()
    {
        _thrown = GetComponent<ThrownObstacle>() ?? GetComponentInParent<ThrownObstacle>();
    }

    public void ApplyDamage(float amount)
    {
        if (_thrown != null)
        {
            _thrown.DestroyByPlayer();
        }
        else
        {
            // fallback: destroy this GameObject
            Destroy(gameObject);
        }
    }
}