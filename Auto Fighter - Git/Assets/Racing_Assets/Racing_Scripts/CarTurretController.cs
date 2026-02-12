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

    [Header("Audio (Turret)")]
    [SerializeField, Tooltip("One-shot SFX played when the turret fires.")]
    private AudioClip turretFireClip;
    [SerializeField, Range(0f, 1f), Tooltip("Volume for turret fire SFX.")]
    private float turretFireVolume = 1f;

    [Header("Spawn / Startup")]
    [SerializeField, Tooltip("If true the turret will start on its base cooldown when the car spawns instead of being immediately ready.")]
    private bool startOnCooldown = true;

    [Header("Bullet Stats")]
    [SerializeField] private float bulletSpeed = 80f;
    [SerializeField] private float bulletDamage = 10f;
    [SerializeField] private float bulletRange = 60f;
    [SerializeField] private float bulletLifetime = 3f;

    [Header("Targeting / Cone")]
    [Tooltip("Max distance we look for targets in front of the car.")]
    [SerializeField] private float targetScanRadius = 60f;
    [Tooltip("Total cone angle in degrees (centered on car forward).")]
    [SerializeField] private float coneAngle = 45f;
    [Tooltip("Layers considered valid targets (enemies/obstacles).")]
    [SerializeField] private LayerMask targetLayers = ~0;
    [SerializeField] private bool useAutoTargeting = true;

    [Header("Protective Targeting Assist")]
    [Tooltip("Safety window multiplier for bullet reach: bulletSpeed * bulletLifetime * multiplier.")]
    [SerializeField] private float travelAllowanceMultiplier = 1.1f;
    [Tooltip("Extra weight applied to path hazard score (targets directly in car path).")]
    [SerializeField] private float pathPriorityWeight = 2.0f;
    [Tooltip("Bullet must arrive earlier than car * this margin to qualify as a path hazard.")]
    [SerializeField] private float preemptTimeMargin = 1.15f;
    [Tooltip("Minimum forward dot to consider a target 'in front' (0 = hemisphere, 1 = straight ahead).")]
    [SerializeField, Range(0f, 1f)] private float forwardDotThreshold = 0.15f;
    [Tooltip("Small epsilon to avoid division issues in scoring.")]
    [SerializeField] private float lateralEpsilon = 0.25f;

    [Header("Debug")]
    [SerializeField] private bool debugCone = false;
    [SerializeField] private bool debugSelectedTarget = false;
    [SerializeField] private Color debugHazardColor = Color.red;
    [SerializeField] private Color debugFallbackColor = Color.yellow;

    // Backing base stats (inspector defaults)
    private float baseBulletDamage;
    private float baseBulletSpeed;
    private float baseFireCooldown;
    private float baseBulletLifetime;
    private float baseConeAngle;
    private float baseScanRadius;

    private float _cooldownTimer;
    private Rigidbody _carRb;

    private void Awake()
    {
        if (car == null)
            car = GetComponentInParent<CarController>();

        if (ownerCollider == null && car != null)
            ownerCollider = car.GetComponent<Collider>();

        if (muzzle == null)
            muzzle = transform;

        if (car != null)
            _carRb = car.GetComponent<Rigidbody>();

        baseBulletDamage = bulletDamage;
        baseBulletSpeed = bulletSpeed;
        baseFireCooldown = fireCooldown;
        baseBulletLifetime = bulletLifetime;
        baseConeAngle = coneAngle;
        baseScanRadius = targetScanRadius;

        ApplySkillStats();

        // NEW: set initial cooldown on spawn to avoid immediate fire spam
        _cooldownTimer = startOnCooldown ? fireCooldown : 0f;
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

    private void HandleSkillChanged(SkillType _, int __) => ApplySkillStats();
    private void HandleSkillsReset() => ApplySkillStats();

    private void Update()
    {
        _cooldownTimer -= Time.deltaTime;

        bool wantsToFire = autoFire
                           ? true
                           : (RacingInputReader.Instance != null ? RacingInputReader.Instance.FireHeld : (Input.GetKey(fireKey) || Input.GetButton("Fire1")));

        if (!wantsToFire || _cooldownTimer > 0f)
            return;

        Vector3 origin = muzzle.position;
        Vector3 carForward = (car != null ? car.transform.forward : transform.forward).normalized;

        Vector3 shootDir = useAutoTargeting
            ? GetProtectiveTargetDirection(origin, carForward)
            : GetRandomConeDirection(carForward);

        FireBullet(origin, shootDir);
        _cooldownTimer = fireCooldown;
    }

    /// <summary>
    /// Protective targeting: try to clear the car's path first by picking a reachable target
    /// that is centered and can be intercepted before the car collides.
    /// Falls back to the most centered reachable target.
    /// </summary>
    private Vector3 GetProtectiveTargetDirection(Vector3 origin, Vector3 forward)
    {
        forward.Normalize();

        if (targetLayers.value == 0)
            return forward;

        Collider[] hits = Physics.OverlapSphere(origin, targetScanRadius, targetLayers, QueryTriggerInteraction.Collide);

        if (hits == null || hits.Length == 0)
            return forward;

        // Precompute constants
        float halfAngleRad = Mathf.Deg2Rad * (coneAngle * 0.5f);
        float cosThreshold = Mathf.Cos(halfAngleRad); // for cone mask
        float maxTravel = Mathf.Min(bulletSpeed * bulletLifetime * travelAllowanceMultiplier, bulletRange);

        // Car forward speed for prediction
        float carFwdSpeed = GetCarForwardSpeed(forward);
        if (carFwdSpeed < 0.5f)
            carFwdSpeed = 0.5f; // minimal speed to avoid infinite times

        // Best path hazard candidate
        bool hazardFound = false;
        Vector3 hazardAimDir = Vector3.zero;
        float hazardBestScore = float.MinValue;
        float hazardBestDistSqr = float.MaxValue;
        Vector3 hazardPos = Vector3.zero;

        // Best fallback candidate
        Vector3 fallbackAimDir = Vector3.zero;
        float fallbackBestScore = float.MinValue;
        float fallbackBestDistSqr = float.MaxValue;
        Vector3 fallbackPos = Vector3.zero;

        foreach (var col in hits)
        {
            if (!col || col == ownerCollider)
                continue;

            Vector3 targetPoint = col.bounds.center;
            Vector3 toTarget = targetPoint - origin;
            float sqrDist = toTarget.sqrMagnitude;
            if (sqrDist < 0.0001f)
                continue;

            float dist = Mathf.Sqrt(sqrDist);
            Vector3 dir = toTarget / dist;

            // Must be within cone
            float dotForward = Vector3.Dot(forward, dir);
            if (dotForward < cosThreshold)
                continue;

            // Must be reachable within travel envelope
            if (dist > maxTravel)
                continue;

            // Base alignment metrics
            float forwardComponent = Mathf.Max(0f, Vector3.Dot(forward, toTarget)); // signed forward distance
            float lateralComponent = Mathf.Sqrt(Mathf.Max(0f, sqrDist - forwardComponent * forwardComponent));

            // Try lead
            Vector3 targetVel = Vector3.zero;
            var rb = col.attachedRigidbody;
            if (rb != null && rb.gameObject.activeInHierarchy && !rb.isKinematic)
                targetVel = rb.velocity;

            Vector3 aimDir;
            bool canLead = TryComputeLeadDirection(origin, targetPoint, targetVel, bulletSpeed, maxTravel, out aimDir);
            if (!canLead)
                aimDir = dir;

            // Recompute dot with aim direction (if lead changed focus)
            float aimDot = Vector3.Dot(forward, aimDir);

            // Compute path alignment score (higher is better)
            // Emphasize high forward dot, penalize lateral deviation.
            float pathAlignmentScore = aimDot / (1f + lateralComponent / Mathf.Max(lateralEpsilon, forwardComponent + lateralEpsilon));

            // Fallback score (pure centeredness)
            float centeredScore = aimDot;

            // Predict times
            float bulletTime = dist / Mathf.Max(0.01f, bulletSpeed);
            float carTime = forwardComponent > 0f
                ? (forwardComponent / Mathf.Max(0.01f, carFwdSpeed))
                : float.MaxValue;

            bool isInFront = aimDot >= forwardDotThreshold;
            bool canPreempt = bulletTime <= carTime * preemptTimeMargin;

            bool qualifiesHazard = isInFront && forwardComponent > 0f && canPreempt;

            if (qualifiesHazard)
            {
                float weightedScore = pathAlignmentScore * pathPriorityWeight;
                if (weightedScore > hazardBestScore ||
                    (Mathf.Abs(weightedScore - hazardBestScore) < 1e-4f && sqrDist < hazardBestDistSqr))
                {
                    hazardFound = true;
                    hazardBestScore = weightedScore;
                    hazardBestDistSqr = sqrDist;
                    hazardAimDir = aimDir;
                    hazardPos = targetPoint;
                }
            }
            else
            {
                if (centeredScore > fallbackBestScore ||
                    (Mathf.Abs(centeredScore - fallbackBestScore) < 1e-4f && sqrDist < fallbackBestDistSqr))
                {
                    fallbackBestScore = centeredScore;
                    fallbackBestDistSqr = sqrDist;
                    fallbackAimDir = aimDir;
                    fallbackPos = targetPoint;
                }
            }
        }

        // Debug draws
        if (debugSelectedTarget)
        {
            if (hazardFound)
            {
                Debug.DrawLine(origin, hazardPos, debugHazardColor, 0.15f);
                Debug.DrawRay(hazardPos, Vector3.up * 2f, debugHazardColor, 0.15f);
            }
            else if (fallbackBestScore > float.MinValue)
            {
                Debug.DrawLine(origin, fallbackPos, debugFallbackColor, 0.15f);
                Debug.DrawRay(fallbackPos, Vector3.up * 2f, debugFallbackColor, 0.15f);
            }
        }

        if (hazardFound && hazardAimDir.sqrMagnitude > 0.0001f)
            return hazardAimDir.normalized;

        if (fallbackBestScore > float.MinValue && fallbackAimDir.sqrMagnitude > 0.0001f)
            return fallbackAimDir.normalized;

        return forward;
    }

    private float GetCarForwardSpeed(Vector3 forward)
    {
        forward.Normalize();
        if (car == null)
            return 0f;

        // Try Rigidbody velocity
        if (_carRb != null)
        {
            Vector3 vel = _carRb.velocity;
            return Mathf.Abs(Vector3.Dot(vel, forward));
        }

        // If CarController exposes something like CurrentSpeed you can swap this:
        // return Mathf.Abs(car.CurrentSpeed);
        return 0f;
    }

    /// <summary>
    /// Computes an intercept direction for a projectile with given speed to a target moving at targetVel.
    /// Returns false if no feasible intercept (target too fast or out of reach).
    /// </summary>
    private bool TryComputeLeadDirection(Vector3 shooterPos, Vector3 targetPos, Vector3 targetVel, float projectileSpeed, float maxTravel, out Vector3 leadDir)
    {
        leadDir = Vector3.zero;

        if (projectileSpeed <= 0.01f)
            return false;

        Vector3 toTarget = targetPos - shooterPos;
        float distSqr = toTarget.sqrMagnitude;
        if (distSqr < 1e-6f)
            return false;

        // Stationary target fallback
        if (targetVel.sqrMagnitude < 1e-6f)
        {
            float dist = Mathf.Sqrt(distSqr);
            if (dist > maxTravel) return false;
            leadDir = toTarget.normalized;
            return true;
        }

        // Quadratic intercept
        Vector3 r = toTarget;
        float v2 = targetVel.sqrMagnitude;
        float s2 = projectileSpeed * projectileSpeed;

        float a = v2 - s2;
        float b = 2f * Vector3.Dot(targetVel, r);
        float c = r.sqrMagnitude;

        float t;
        if (Mathf.Abs(a) < 1e-6f)
        {
            if (Mathf.Abs(b) < 1e-6f) return false;
            t = -c / b;
        }
        else
        {
            float disc = b * b - 4f * a * c;
            if (disc < 0f) return false;
            float sqrtDisc = Mathf.Sqrt(disc);
            float t1 = (-b + sqrtDisc) / (2f * a);
            float t2 = (-b - sqrtDisc) / (2f * a);
            t = float.MaxValue;
            if (t1 > 0f) t = Mathf.Min(t, t1);
            if (t2 > 0f) t = Mathf.Min(t, t2);
            if (!float.IsFinite(t) || t == float.MaxValue) return false;
        }

        float travel = projectileSpeed * t;
        if (travel > maxTravel) return false;

        Vector3 intercept = targetPos + targetVel * t;
        Vector3 toIntercept = intercept - shooterPos;
        if (toIntercept.sqrMagnitude < 1e-6f) return false;
        leadDir = toIntercept.normalized;
        return true;
    }

    private Vector3 GetRandomConeDirection(Vector3 forward)
    {
        forward.Normalize();
        float halfAngle = coneAngle * 0.5f;
        float yaw = Random.Range(-halfAngle, halfAngle);
        float pitch = 0f; // keep shots mostly planar
        Quaternion rot =
            Quaternion.AngleAxis(yaw, Vector3.up) *
            Quaternion.AngleAxis(pitch, Vector3.right);
        return (rot * forward).normalized;
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

        // Play turret fire SFX (spatialized at muzzle/origin)
        if (turretFireClip != null)
        {
            AudioSource.PlayClipAtPoint(turretFireClip, origin, Mathf.Clamp01(turretFireVolume));
        }
    }

    private void ApplySkillStats()
    {
        var mgr = RacingSkillTreeManager.Instance;
        if (mgr == null)
            return;

        bulletDamage = mgr.ApplyStatChain(baseBulletDamage, SkillType.TurretDamage_Add, SkillType.TurretDamage_Mul);
        bulletSpeed = mgr.ApplyStatChain(baseBulletSpeed, SkillType.TurretProjectileSpeed_Add, SkillType.TurretProjectileSpeed_Mul);
        fireCooldown = mgr.ApplyStatChain(baseFireCooldown, SkillType.TurretCooldown_Add, SkillType.TurretCooldown_Mul);
        fireCooldown = Mathf.Max(0.01f, fireCooldown);
        bulletLifetime = mgr.ApplyStatChain(baseBulletLifetime, SkillType.TurretBulletLifetime_Add, SkillType.TurretBulletLifetime_Mul);
        bulletLifetime = Mathf.Max(0.01f, bulletLifetime);
        coneAngle = mgr.ApplyStatChain(baseConeAngle, SkillType.TurretConeAngle_Add, SkillType.TurretConeAngle_Mul);
        coneAngle = Mathf.Clamp(coneAngle, 0f, 180f);
        targetScanRadius = mgr.ApplyStatChain(baseScanRadius, SkillType.TurretScanRadius_Add, SkillType.TurretScanRadius_Mul);
        targetScanRadius = Mathf.Max(0f, targetScanRadius);
    }

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        if (!debugCone) return;
        Transform refTransform = car != null ? car.transform : transform;
        Vector3 origin = muzzle != null ? muzzle.position : transform.position;
        Vector3 fwd = refTransform.forward;
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(origin, targetScanRadius);

        Quaternion leftRot = Quaternion.AngleAxis(-coneAngle * 0.5f, Vector3.up);
        Quaternion rightRot = Quaternion.AngleAxis(coneAngle * 0.5f, Vector3.up);
        Gizmos.color = Color.yellow;
        Gizmos.DrawRay(origin, (leftRot * fwd) * targetScanRadius);
        Gizmos.DrawRay(origin, (rightRot * fwd) * targetScanRadius);
        Gizmos.DrawRay(origin, fwd * targetScanRadius);
    }
#endif
}