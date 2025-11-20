using UnityEngine;

[DisallowMultipleComponent]
public class CarTurretController : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private CarController car;          // root car with movement + collider
    [SerializeField] private Transform muzzle;           // where bullets spawn
    [SerializeField] private GameObject bulletPrefab;    // must have RacingBullet
    [SerializeField] private Collider ownerCollider;     // collider(s) to ignore (car body)

    [Header("Firing")]
    [SerializeField] private bool autoFire = false;      // if true, fires whenever off cooldown
    [SerializeField] private KeyCode fireKey = KeyCode.Mouse0;
    [SerializeField] private float fireCooldown = 0.25f; // seconds between shots

    [Header("Bullet Stats")]
    [SerializeField] private float bulletSpeed = 80f;
    [SerializeField] private float bulletDamage = 10f;
    [SerializeField] private float bulletRange = 60f;
    [SerializeField] private float bulletLifetime = 3f;

    [Header("Targeting / Cone")]
    [Tooltip("Max distance we look for targets in front of the car.")]
    [SerializeField] private float targetScanRadius = 60f;

    [Header("Targeting Mode")]
    [SerializeField] private bool useAutoTargeting = true;

    [Tooltip("Total cone angle in degrees (centered on car forward).")]
    [SerializeField] private float coneAngle = 45f;

    [Tooltip("Layers considered valid targets (enemies/obstacles).")]
    [SerializeField] private LayerMask targetLayers = ~0;

    [Header("Debug")]
    [SerializeField] private bool debugCone = false;

    // Backing base stats (inspector defaults)
    private float baseBulletDamage;
    private float baseBulletSpeed;
    private float baseFireCooldown;
    private float baseBulletLifetime;
    private float baseConeAngle;
    private float baseScanRadius;


    private float _cooldownTimer;

    private void Awake()
    {
        if (car == null)
            car = GetComponentInParent<CarController>();

        if (ownerCollider == null && car != null)
            ownerCollider = car.GetComponent<Collider>();

        if (muzzle == null)
            muzzle = transform;


        baseBulletDamage = bulletDamage;
        baseBulletSpeed = bulletSpeed;
        baseFireCooldown = fireCooldown;
        baseBulletLifetime = bulletLifetime;
        baseConeAngle = coneAngle;
        baseScanRadius = targetScanRadius;

        ApplySkillStats();

    }

    private void OnEnable()
    {
        var mgr = RacingSkillTreeManager.Instance;
        if (mgr != null)
        {
            mgr.OnLevelChanged += HandleSkillChanged;
            mgr.OnSkillsReset += HandleSkillsReset;
        }
    }

    private void OnDisable()
    {
        var mgr = RacingSkillTreeManager.Instance;
        if (mgr != null)
        {
            mgr.OnLevelChanged -= HandleSkillChanged;
            mgr.OnSkillsReset -= HandleSkillsReset;
        }
    }

    private void HandleSkillChanged(SkillType _, int __)
    {
        ApplySkillStats();
    }

    private void HandleSkillsReset()
    {
        // reset runtime values back to base, then reapply (which will be neutral at level 0)
        ApplySkillStats();
    }

    private void Update()
    {
        _cooldownTimer -= Time.deltaTime;

        bool wantsToFire = autoFire
                           ? true
                           : Input.GetKey(fireKey) || Input.GetButton("Fire1");

        if (!wantsToFire || _cooldownTimer > 0f)
            return;

        Vector3 origin = muzzle.position;
        Vector3 carForward = (car != null ? car.transform.forward : transform.forward).normalized;

        // Get a direction within the forward cone (auto-aim toward a target if one exists)
        Vector3 shootDir = useAutoTargeting
            ? GetAutoTargetDirection(origin, carForward)
            : GetRandomConeDirection(carForward);

        FireBullet(origin, shootDir);
        _cooldownTimer = fireCooldown;
    }

    /// <summary>
    /// Picks a target inside the cone in front of the car, or falls back to straight forward.
    /// </summary>
    private Vector3 GetAutoTargetDirection(Vector3 origin, Vector3 fallbackForward)
    {
        fallbackForward.Normalize();

        // No target layers set? Just shoot forward.
        if (targetLayers.value == 0)
            return fallbackForward;

        Collider[] hits = Physics.OverlapSphere(
            origin,
            targetScanRadius,
            targetLayers,
            QueryTriggerInteraction.Ignore
        );

        Transform bestTarget = null;
        float bestSqrDist = float.MaxValue;

        foreach (var col in hits)
        {
            if (!col || col == ownerCollider)
                continue;

            Vector3 toTarget = col.bounds.center - origin;
            float sqrDist = toTarget.sqrMagnitude;
            if (sqrDist < 0.0001f)
                continue;

            float dist = Mathf.Sqrt(sqrDist);
            Vector3 dir = toTarget / dist;
            float angle = Vector3.Angle(fallbackForward, dir);

            // Only accept targets inside the cone
            if (angle > coneAngle * 0.5f)
                continue;

            if (sqrDist < bestSqrDist)
            {
                bestSqrDist = sqrDist;
                bestTarget = col.transform;
            }
        }

        if (bestTarget != null)
        {
            Vector3 dir = (bestTarget.position - origin);
            if (dir.sqrMagnitude > 0.0001f)
                return dir.normalized;
        }

        return fallbackForward;
    }

    private Vector3 GetRandomConeDirection(Vector3 forward)
    {
        forward.Normalize();

        // Random rotation inside cone
        float halfAngle = coneAngle * 0.5f;

        // Random Yaw inside cone
        float angleOffset = Random.Range(-halfAngle, halfAngle);

        // Slight random pitch variation (optional, can set to 0 if you want planar only)
        float pitchOffset = 0f; // Or Random.Range(-halfAngle * 0.2f, halfAngle * 0.2f)

        Quaternion randomRot =
            Quaternion.AngleAxis(angleOffset, Vector3.up) *
            Quaternion.AngleAxis(pitchOffset, Vector3.right);

        Vector3 coneDir = randomRot * forward;
        return coneDir.normalized;
    }

    private void FireBullet(Vector3 origin, Vector3 direction)
    {
        if (!bulletPrefab)
            return;

        direction.Normalize();
        Quaternion rot = Quaternion.LookRotation(direction, Vector3.up);

        GameObject bulletGO = Instantiate(bulletPrefab, origin, rot);
        var bullet = bulletGO.GetComponent<RacingBullet>();
        if (bullet != null)
        {
            bullet.Init(
                damage: bulletDamage,
                speed: bulletSpeed,
                range: bulletRange,
                lifetime: bulletLifetime,
                owner: ownerCollider
            );
        }
    }


    private void ApplySkillStats()
    {
        var mgr = RacingSkillTreeManager.Instance;
        if (mgr == null)
            return;

        // Damage per bullet
        bulletDamage = mgr.ApplyStatChain(
            baseBulletDamage,
            SkillType.TurretDamage_Add,
            SkillType.TurretDamage_Mul
        );

        // Projectile speed
        bulletSpeed = mgr.ApplyStatChain(
            baseBulletSpeed,
            SkillType.TurretProjectileSpeed_Add,
            SkillType.TurretProjectileSpeed_Mul
        );

        // Cooldown between shots (lower is better)
        fireCooldown = mgr.ApplyStatChain(
            baseFireCooldown,
            SkillType.TurretCooldown_Add,
            SkillType.TurretCooldown_Mul
        );
        fireCooldown = Mathf.Max(0.01f, fireCooldown);

        // Bullet lifetime
        bulletLifetime = mgr.ApplyStatChain(
            baseBulletLifetime,
            SkillType.TurretBulletLifetime_Add,
            SkillType.TurretBulletLifetime_Mul
        );
        bulletLifetime = Mathf.Max(0.01f, bulletLifetime);

        // Cone angle
        coneAngle = mgr.ApplyStatChain(
            baseConeAngle,
            SkillType.TurretConeAngle_Add,
            SkillType.TurretConeAngle_Mul
        );
        coneAngle = Mathf.Clamp(coneAngle, 0f, 180f);

        // Target scan radius
        targetScanRadius = mgr.ApplyStatChain(
            baseScanRadius,
            SkillType.TurretScanRadius_Add,
            SkillType.TurretScanRadius_Mul
        );
        targetScanRadius = Mathf.Max(0f, targetScanRadius);
    }

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        if (!debugCone) return;

        Transform refTransform = car != null ? car.transform : transform;
        Vector3 origin = muzzle != null ? muzzle.position : transform.position;
        Vector3 forward = refTransform.forward;

        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(origin, targetScanRadius);

        // Simple visualization: 2 rays at cone edges
        Quaternion leftRot = Quaternion.AngleAxis(-coneAngle * 0.5f, Vector3.up);
        Quaternion rightRot = Quaternion.AngleAxis(coneAngle * 0.5f, Vector3.up);

        Vector3 leftDir = leftRot * forward;
        Vector3 rightDir = rightRot * forward;

        Gizmos.color = Color.yellow;
        Gizmos.DrawRay(origin, leftDir * targetScanRadius);
        Gizmos.DrawRay(origin, rightDir * targetScanRadius);
        Gizmos.DrawRay(origin, forward * targetScanRadius);
    }
#endif
}
