using UnityEngine;

/// <summary>
/// Projectile fired by <see cref="TrackSideShooterObstacle"/>. Damages the player car on trigger hit.
/// Hits on other track obstacles knock them into physics (bugs/critters from the beast spawner are ignored).
/// </summary>
[RequireComponent(typeof(Collider))]
[RequireComponent(typeof(Rigidbody))]
public class SideShooterProjectile : MonoBehaviour
{
    [Header("Defaults (overridden by Init)")]
    [SerializeField] private float speed = 28f;
    [SerializeField] private float maxDistance = 70f;
    [SerializeField] private float maxLifetime = 3.5f;
    [SerializeField] private float hitHpDamage = 8f;
    [SerializeField, Range(0f, 1f)] private float hitFuelPercent = 0.03f;
    [SerializeField, Range(0f, 1f)] private float crashFxSeverity = 0.45f;
    [SerializeField, Min(0f)] private float crashImpactSpeed = 12f;

    [Header("Obstacle Knock")]
    [Tooltip("Horizontal impulse applied when a shot knocks an obstacle flying.")]
    [SerializeField, Min(0f)] private float obstacleKnockImpulse = 18f;
    [Tooltip("Upward impulse added so props leave the road instead of sliding.")]
    [SerializeField, Min(0f)] private float obstacleKnockUp = 5f;
    [SerializeField, Min(1f)] private float defaultObstacleMass = 10f;

    [Header("FX")]
    [SerializeField] private GameObject hitVFX;
    [SerializeField] private float hitVFXLifetime = 2f;
    [SerializeField] private AudioClip hitSound;
    [SerializeField, Range(0f, 1f)] private float hitSoundVolume = 0.8f;

    private Rigidbody _rb;
    private Collider _col;
    private Vector3 _startPos;
    private float _age;
    private Collider[] _ignoreColliders;
    private bool _consumed;

    private void Awake()
    {
        _rb = GetComponent<Rigidbody>();
        _col = GetComponent<Collider>();
        _col.isTrigger = true;

        _rb.useGravity = false;
        _rb.drag = 0f;
        _rb.angularDrag = 0f;
        _rb.interpolation = RigidbodyInterpolation.Interpolate;
        _rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
    }

    public void Init(
        float speed,
        float maxDistance,
        float lifetime,
        float hpDamage,
        float fuelPercent,
        float fxSeverity,
        float impactSpeed,
        Collider[] ignoreColliders)
    {
        this.speed = Mathf.Max(0.1f, speed);
        this.maxDistance = Mathf.Max(1f, maxDistance);
        maxLifetime = Mathf.Max(0.05f, lifetime);
        hitHpDamage = Mathf.Max(0f, hpDamage);
        hitFuelPercent = Mathf.Clamp01(fuelPercent);
        crashFxSeverity = Mathf.Clamp01(fxSeverity);
        crashImpactSpeed = Mathf.Max(0f, impactSpeed);
        _ignoreColliders = ignoreColliders;

        _startPos = transform.position;
        _age = 0f;
        _consumed = false;

        _rb.velocity = transform.forward * this.speed;
    }

    private void Update()
    {
        if (_consumed) return;

        _age += Time.deltaTime;
        if (_age >= maxLifetime ||
            (transform.position - _startPos).sqrMagnitude >= maxDistance * maxDistance)
        {
            Destroy(gameObject);
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        if (_consumed || other == null || other.isTrigger)
            return;

        if (ShouldIgnore(other))
            return;

        // Bugs / critters from the beast spawner: shots pass through (do not knock or consume).
        if (IsBugOrCritter(other))
            return;

        CarController car = other.GetComponentInParent<CarController>();
        if (car != null)
        {
            HitCar(car, other);
            return;
        }

        if (TryKnockObstacleFlying(other))
        {
            PlayHitFx(other.ClosestPoint(transform.position));
            Consume();
            return;
        }

        // Soft stop on solid world (road walls / leftover static props).
        if (((1 << other.gameObject.layer) & LayerMask.GetMask("RoadSurface", "Default", "Obstacle")) != 0)
        {
            PlayHitFx(transform.position);
            Consume();
        }
    }

    private bool ShouldIgnore(Collider other)
    {
        if (_ignoreColliders == null) return false;
        for (int i = 0; i < _ignoreColliders.Length; i++)
        {
            Collider c = _ignoreColliders[i];
            if (c == null) continue;
            if (other == c) return true;
            if (other.transform.root == c.transform.root) return true;
        }
        return false;
    }

    private static bool IsBugOrCritter(Collider other)
    {
        var creature = other.GetComponentInParent<TrackCreature>();
        if (creature == null) return false;
        return creature.BehaviorType == CreatureBehaviorType.Passive
            || creature.BehaviorType == CreatureBehaviorType.Scared;
    }

    private void HitCar(CarController car, Collider carCol)
    {
        Vector3 hitPoint = carCol.ClosestPoint(transform.position);
        Vector3 hitDir = car.transform.position - transform.position;
        hitDir.y = 0f;
        if (hitDir.sqrMagnitude < 1e-4f) hitDir = transform.forward;
        hitDir.Normalize();
        Vector3 normal = -hitDir;

        // Forcefield can eat the shot if one is active.
        if (car.TryGetComponent(out CarForcefield forcefield) &&
            forcefield.TryInterceptObstacleForOverlapHit(_col))
        {
            PlayHitFx(hitPoint);
            Consume();
            return;
        }

        car.ApplyDirectDamageWithCrashFX(
            hitHpDamage,
            hitFuelPercent,
            hitPoint,
            normal,
            hitDir,
            crashImpactSpeed,
            crashFxSeverity);

        PlayHitFx(hitPoint);
        Consume();
    }

    /// <summary>
    /// Detach scripted movers / free rigidbodies and fling them along the shot direction.
    /// Returns false if this collider is not a launchable track obstacle.
    /// </summary>
    private bool TryKnockObstacleFlying(Collider other)
    {
        Vector3 hitPoint = other.ClosestPoint(transform.position);
        Vector3 planar = ResolveKnockPlanar(hitPoint);
        float strikeSpeed = _rb != null ? Mathf.Max(speed, _rb.velocity.magnitude) : speed;
        float horiz = Mathf.Max(0.1f, obstacleKnockImpulse);
        float up = Mathf.Max(0f, obstacleKnockUp);

        var creature = other.GetComponentInParent<TrackCreature>();
        if (creature != null)
        {
            // Aggressive beasts only (bugs/critters already filtered out).
            if (!creature.TryBeginForcefieldPhysicsLaunch())
                return false;

            Rigidbody crb = creature.GetComponent<Rigidbody>();
            if (crb != null)
                crb.AddForce(planar * horiz + Vector3.up * up, ForceMode.Impulse);

            creature.FinalizeForcefieldLaunchKill();
            return true;
        }

        var rollingLog = other.GetComponentInParent<RollingLogAlongTrack>();
        if (rollingLog != null)
        {
            rollingLog.ApplyBeastStrike(transform.position, Mathf.Max(6f, strikeSpeed));
            return true;
        }

        var bounceBack = other.GetComponentInParent<TrackObstacleBounceBack>();
        if (bounceBack != null)
        {
            bounceBack.ApplyRollingLogRam(planar, horiz, up, hitPoint);
            return true;
        }

        var cross = other.GetComponentInParent<CrossTrackObstacle>();
        if (cross != null)
        {
            cross.ApplyRollingLogRam(planar, Mathf.Max(strikeSpeed, 6f));
            return true;
        }

        var sideShooter = other.GetComponentInParent<TrackSideShooterObstacle>();
        if (sideShooter != null)
        {
            sideShooter.ConvertToPhysicsOnHit(planar * horiz + Vector3.up * up);
            return true;
        }

        var shuttle = other.GetComponentInParent<ShuttleTrackObstacle>();
        if (shuttle != null)
        {
            shuttle.ConvertToPhysicsOnHit();
            Rigidbody srb = shuttle.GetComponent<Rigidbody>();
            if (srb != null)
                ApplyImpulse(srb, planar, horiz, up);
            return true;
        }

        var npc = other.GetComponentInParent<NPCTrafficCar>();
        if (npc != null)
        {
            npc.ForceCrashFromForcefield(transform.position, Mathf.Max(strikeSpeed, 8f), _col);
            return true;
        }

        var racing = other.GetComponentInParent<RacingObstacle>();
        if (racing != null)
        {
            Rigidbody obr = EnsureDynamicRigidbody(racing.gameObject);
            if (obr != null)
                ApplyImpulse(obr, planar, horiz, up);
            return true;
        }

        var thrown = other.GetComponentInParent<ThrownObstacle>();
        if (thrown != null)
        {
            Rigidbody trb = thrown.GetComponent<Rigidbody>();
            if (trb != null)
            {
                if (trb.isKinematic)
                {
                    trb.isKinematic = false;
                    trb.useGravity = true;
                }
                ApplyImpulse(trb, planar, horiz, up);
                return true;
            }
        }

        Rigidbody rb = other.attachedRigidbody;
        if (rb == null) rb = other.GetComponentInParent<Rigidbody>();
        if (rb == null) return false;
        if (rb.GetComponentInParent<CarController>() != null) return false;

        if (SpawnUtils.IsEmbeddedLocked(rb)) return false;

        // Generic Obstacle-layer (or similar) prop with a rigidbody.
        if (rb.isKinematic)
        {
            rb.isKinematic = false;
            rb.useGravity = true;
            rb.constraints = RigidbodyConstraints.None;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            rb.WakeUp();
        }

        ApplyImpulse(rb, planar, horiz, up);
        return true;
    }

    private Vector3 ResolveKnockPlanar(Vector3 hitPoint)
    {
        Vector3 planar = transform.forward;
        planar.y = 0f;
        if (planar.sqrMagnitude < 1e-4f)
        {
            planar = hitPoint - transform.position;
            planar.y = 0f;
        }
        if (planar.sqrMagnitude < 1e-4f)
            planar = Vector3.forward;
        return planar.normalized;
    }

    private void ApplyImpulse(Rigidbody rb, Vector3 planar, float horiz, float up)
    {
        if (rb == null) return;
        if (rb.mass < 0.01f)
            rb.mass = Mathf.Max(0.1f, defaultObstacleMass);

        float mass = Mathf.Max(0.01f, rb.mass);
        // Scale with mass so heavy props still leave the road.
        rb.AddForce((planar * horiz + Vector3.up * up) * mass * 0.35f, ForceMode.Impulse);
    }

    private Rigidbody EnsureDynamicRigidbody(GameObject rootObj)
    {
        if (rootObj == null) return null;

        var shuttle = rootObj.GetComponentInChildren<ShuttleTrackObstacle>(true);
        if (shuttle != null)
            shuttle.ConvertToPhysicsOnHit();

        Rigidbody found = rootObj.GetComponent<Rigidbody>() ?? rootObj.GetComponentInChildren<Rigidbody>();
        if (found == null)
        {
            found = rootObj.AddComponent<Rigidbody>();
            found.mass = Mathf.Max(0.1f, defaultObstacleMass);
        }

        if (SpawnUtils.IsEmbeddedLocked(found)) return null;

        found.constraints = RigidbodyConstraints.None;
        if (found.isKinematic) found.isKinematic = false;
        found.useGravity = true;
        found.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        found.interpolation = RigidbodyInterpolation.Interpolate;
        found.WakeUp();
        Physics.SyncTransforms();
        return found;
    }

    private void PlayHitFx(Vector3 pos)
    {
        if (hitVFX != null)
        {
            var vfx = Instantiate(hitVFX, pos, Quaternion.identity);
            Destroy(vfx, hitVFXLifetime);
        }

        if (hitSound != null)
            AudioSource.PlayClipAtPoint(hitSound, pos, hitSoundVolume);
    }

    private void Consume()
    {
        if (_consumed) return;
        _consumed = true;
        Destroy(gameObject);
    }
}
