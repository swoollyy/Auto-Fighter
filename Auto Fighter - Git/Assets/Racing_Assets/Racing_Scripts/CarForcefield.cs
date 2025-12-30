using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(CarController))]
public sealed class CarForcefield : MonoBehaviour
{
    [Header("Owner")]
    [SerializeField] private Collider ownerCollider;          // Car's own collider to ignore against
    [SerializeField] private Rigidbody carRigidbody;          // Car's rigidbody for relative speed/severity
    private CarController _car;                               // full car reference

    [Header("Layer / Trigger Filtering")]
    [SerializeField] private string proxyLayerName = "Forcefield"; // make sure you create this layer and remove it from crashLayers
    [SerializeField] private bool useProxyLayer = true;

    [Header("Detection")]
    [Tooltip("Layers considered 'obstacles' for forcefield interception. Set this to the same as CarController.crashLayers.")]
    [SerializeField] private LayerMask obstacleLayers = ~0;
    [Tooltip("Trigger radius around the car. The trigger is created at runtime.")]
    [SerializeField] private float triggerRadius = 2.35f;

    [Header("Arming")]
    [SerializeField] private bool startsArmed = true;
    [Tooltip("Automatically re-arm after cooldown once consumed.")]
    [SerializeField] private bool autoRearm = true;
    [SerializeField, Min(0f)] private float cooldownSeconds = 6f;

    [Header("Knockback")]
    [Tooltip("Minimum impact speed mapped to severity 0.")]
    [SerializeField] private float minImpactSpeed = 4f;
    [Tooltip("Maximum impact speed mapped to severity 1.")]
    [SerializeField] private float maxImpactSpeed = 25f;

    [Tooltip("Horizontal (away from car) delta-velocity at severity 0..1.")]
    [SerializeField] private Vector2 awayVelocityChange = new Vector2(7f, 18f);
    [Tooltip("Upward delta-velocity at severity 0..1 (always adds some up).")]
    [SerializeField] private Vector2 upVelocityChange = new Vector2(2.5f, 8.0f);

    [Tooltip("Torque applied to launched objects (min,max) mapped by severity.")]
    [SerializeField] private Vector2 torqueAtSeverity = new Vector2(6f, 18f);

    [Header("Physics Conversion")]
    [Tooltip("If the obstacle has CrossTrackObstacle, disable it before launching so physics can take over cleanly.")]
    [SerializeField] private bool disableCrossTrackObstacleOnLaunch = true;
    [Tooltip("If the obstacle or its parents have no Rigidbody, add one at runtime to enable physics.")]
    [SerializeField] private bool addRigidbodyIfMissing = true;
    [Tooltip("Default mass to assign if we need to add a Rigidbody.")]
    [SerializeField] private float defaultAddedMass = 20f;

    [Header("Collision Safety")]
    [Tooltip("Seconds to ignore collisions between the launched obstacle and the car to avoid re-colliding/crash logic.")]
    [SerializeField] private float ignoreWithCarSeconds = 2.0f;

    [Header("Optional Visual")]
    [Tooltip("Optional visible bubble to show arming state (scaled to trigger radius).")]
    [SerializeField] private Transform visualRoot;
    [Tooltip("Hide the visual root GameObject after a successful activation, re-enable on re-arm.")]
    [SerializeField] private bool disableVisualOnUse = true;

    [Header("Car Collider Collection")]
    [Tooltip("Gather all colliders on the car to ignore collisions properly (recommended).")]
    [SerializeField] private bool gatherAllCarColliders = true;

    [Header("Launch VFX")]
    [Tooltip("Optional VFX prefab to spawn at the forcefield collision point when an obstacle is launched.")]
    [SerializeField] private GameObject launchVFX;
    [Tooltip("Parent the VFX to the obstacle so it travels with it.")]
    [SerializeField] private bool parentVfxToObstacle = true;

    [Header("Impulse Mode")]
    [Tooltip("If true, compute impulse = mass * deltaV and use ForceMode.Impulse so heavier bodies get pushed proportionally.")]
    [SerializeField] private bool useMassAwareImpulse = true;

    [Header("Slow-Mo On Launch")]
    [SerializeField] private bool enableLaunchSlowMo = true;
    [SerializeField, Range(0.05f, 1f)] private float launchSlowMoScale = 0.5f;
    [SerializeField, Min(0f)] private float launchSlowMoHold = 0.15f;
    [SerializeField, Min(0f)] private float launchSlowMoEaseOut = 0.20f;

    // NEW: PPSv2 PostFX burst controller (Chromatic + Lens Distortion)
    [Header("Post-Processing (PPSv2)")]
    [SerializeField, Tooltip("ForcefieldPostFXController driving Chromatic Aberration and Lens Distortion.")]
    private ForcefieldPostFXController postFX;

    // NEW: Audio
    [Header("Audio (Forcefield)")]
    [SerializeField, Tooltip("3D sound played when the forcefield intercepts / launches an obstacle.")]
    private AudioClip forcefieldUseClip;
    [SerializeField, Range(0f, 1f)]
    private float forcefieldUseVolume = 1f;
    [SerializeField, Tooltip("3D sound played when the forcefield re-arms after cooldown.")]
    private AudioClip forcefieldRearmClip;
    [SerializeField, Range(0f, 1f)]
    private float forcefieldRearmVolume = 0.9f;

    // NEW: start behavior
    [Header("Spawn / Startup")]
    [SerializeField, Tooltip("If true the forcefield will start on its base cooldown when the car spawns instead of being instantly armed.")]
    private bool startOnCooldown = true;

    // Runtime
    private SphereCollider _trigger;
    private bool _armed;
    private float _cooldownRemain;
    private readonly HashSet<Collider> _recentlyLaunched = new HashSet<Collider>(64);

    // Car colliders cache
    private Collider[] _carColliders;

    // Slow-Mo runtime
    private bool _ownsSlowMo;
    private Coroutine _slowMoRoutine;

    void Reset()
    {
        _car = GetComponent<CarController>();
        ownerCollider = GetComponent<Collider>();
        carRigidbody = GetComponent<Rigidbody>();
    }

    void Awake()
    {
        _car = GetComponent<CarController>();
        ownerCollider = ownerCollider ? ownerCollider : GetComponent<Collider>();
        carRigidbody = carRigidbody ? carRigidbody : GetComponent<Rigidbody>();

        if (gatherAllCarColliders && _car != null)
            _carColliders = _car.GetComponentsInChildren<Collider>(true);
        else if (ownerCollider != null)
            _carColliders = new[] { ownerCollider };

        postFX = FindObjectOfType<ForcefieldPostFXController>();

        EnsureTrigger();

        // Safety: ensure trigger starts in the correct enabled state
        if (_trigger) _trigger.enabled = _armed;

        // Maintain existing serialized startsArmed default, but optionally enforce base cooldown on spawn.
        if (startOnCooldown && cooldownSeconds > 0f)
        {
            // start disarmed and begin cooldown so skill upgrades that reduce cooldown are honored before first arm
            SetArmed(false);
            _cooldownRemain = cooldownSeconds;
        }
        else
        {
            SetArmed(startsArmed);
        }

        SyncVisual(true);
    }

    void OnEnable()
    {
        // Don't accidentally enable the bubble when we're disarmed/on cooldown.
        if (_trigger) _trigger.enabled = _armed;
    }

    void OnDisable()
    {
        if (_trigger) _trigger.enabled = false;

        if (_slowMoRoutine != null)
        {
            StopCoroutine(_slowMoRoutine);
            _slowMoRoutine = null;
        }
        if (_ownsSlowMo)
        {
            TimeScaleHub.End(this);
            _ownsSlowMo = false;
        }

        if (visualRoot && disableVisualOnUse)
            visualRoot.gameObject.SetActive(false);
    }

    void Update()
    {
        if (!_armed && autoRearm && cooldownSeconds > 0f)
        {
            _cooldownRemain -= Time.deltaTime;
            if (_cooldownRemain <= 0f)
                SetArmed(true);
        }

        SyncVisual(false);
    }

    public bool IsArmed => _armed;

    public void ArmNow()
    {
        SetArmed(true);
        _cooldownRemain = 0f;
    }

    public void DisarmNow()
    {
        SetArmed(false);
        _cooldownRemain = cooldownSeconds;
    }

    public void SetCooldown(float seconds)
    {
        cooldownSeconds = Mathf.Max(0f, seconds);
    }

    public void SetRadius(float radius)
    {
        triggerRadius = Mathf.Max(0.05f, radius);
        if (_trigger) _trigger.radius = triggerRadius;
        SyncVisual(true);
    }

    public void SetKnockback(Vector2 awayVC, Vector2 upVC)
    {
        awayVelocityChange = new Vector2(Mathf.Max(0f, awayVC.x), Mathf.Max(0f, awayVC.y));
        upVelocityChange = new Vector2(Mathf.Max(0f, upVC.x), Mathf.Max(0f, upVC.y));
    }

    private void EnsureTrigger()
    {
        if (_trigger != null) return;
        var go = new GameObject("ForcefieldTrigger");
        go.transform.SetParent(transform, false);
        go.transform.localPosition = Vector3.zero;
        go.transform.localRotation = Quaternion.identity;
        _trigger = go.AddComponent<SphereCollider>();
        _trigger.isTrigger = true;
        _trigger.radius = triggerRadius;

        if (useProxyLayer && !string.IsNullOrEmpty(proxyLayerName))
        {
            int layer = LayerMask.NameToLayer(proxyLayerName);
            if (layer >= 0) go.layer = layer;
        }

        var proxy = go.AddComponent<ForcefieldTriggerProxy>();
        proxy.Init(this, obstacleLayers);
    }

    public void SetArmed(bool v)
    {
        bool wasArmed = _armed;
        _armed = v;
        if (disableVisualOnUse && visualRoot)
            visualRoot.gameObject.SetActive(v);

        if (_trigger)
            _trigger.enabled = v;

        SyncVisual(true);

        // Play rearm sound only when transitioning from disarmed -> armed
        if (v && !wasArmed)
        {
            // play rearm SFX at the car position
            Play3DClipAtPoint(forcefieldRearmClip, transform.position, forcefieldRearmVolume);
        }
    }

    private void SyncVisual(bool instant)
    {
        if (!visualRoot) return;
        visualRoot.localScale = Vector3.one * (triggerRadius * 2f);

        bool show = _armed && this.enabled && (!disableVisualOnUse ? true : _armed);
        if (visualRoot.gameObject.activeSelf != show)
            visualRoot.gameObject.SetActive(show);

    }

    internal void HandleTriggerEnter(Collider other)
    {
        if (!_armed) return;
        if (!other || other == ownerCollider) return;
        if (((1 << other.gameObject.layer) & obstacleLayers) == 0) return;

        // NEW: If this incoming collider is a thrown projectile, intercept it specially:
        var thrown = other.GetComponentInParent<ThrownObstacle>();
        if (thrown != null)
        {
            // Avoid processing the same collider multiple times
            if (_recentlyLaunched.Contains(other)) return;

            // Compute relative velocity between projectile and car (use attached rb if available)
            Rigidbody otherRb = other.attachedRigidbody ?? thrown.GetComponent<Rigidbody>();
            Vector3 relVel = otherRb ? (otherRb.velocity - (carRigidbody ? carRigidbody.velocity : Vector3.zero))
                                     : (carRigidbody ? -carRigidbody.velocity : Vector3.zero);
            float speed = relVel.magnitude;
            float sev = Mathf.InverseLerp(minImpactSpeed, maxImpactSpeed, speed);

            // Compute knockback DV from configured curves (unique names to avoid shadowing)
            float t_awayDV = Mathf.Lerp(awayVelocityChange.x, awayVelocityChange.y, sev);
            float t_upDV = Mathf.Lerp(upVelocityChange.x, upVelocityChange.y, sev);

            // Direction away from car (unique name)
            Vector3 t_awayDir = other.bounds.center - (ownerCollider ? ownerCollider.bounds.center : transform.position);
            t_awayDir.y = 0f;
            if (t_awayDir.sqrMagnitude < 1e-6f) t_awayDir = transform.forward;
            t_awayDir.Normalize();

            // Call the projectile's public interception handler
            thrown.InterceptedByForcefield(t_awayDir, t_awayDV, t_upDV, ignoreWithCarSeconds);

            // Spawn VFX at interception point
            Vector3 vfxPos = other.bounds.ClosestPoint(transform.position);
            Vector3 vfxForward = (ownerCollider ? (ownerCollider.bounds.center - vfxPos) : (transform.position - vfxPos));
            if (vfxForward.sqrMagnitude < 1e-6f) vfxForward = transform.forward;
            vfxForward.Normalize();
            Quaternion vfxRot = Quaternion.LookRotation(vfxForward, Vector3.up);

            if (launchVFX != null)
            {
                var vfx = Instantiate(launchVFX, vfxPos, vfxRot);
                if (parentVfxToObstacle && vfx != null)
                    vfx.transform.SetParent(other.transform.root, true);
            }

            if (enableLaunchSlowMo)
                StartLaunchSlowMo();

            Play3DClipAtPoint(forcefieldUseClip, vfxPos, forcefieldUseVolume);


            // NEW: trigger PPSv2 PostFX burst (Chromatic + Lens Distortion)
            if (postFX != null)
                postFX.PlayBurst();

            // mark as recently handled
            _recentlyLaunched.Add(other);

            SetArmed(false);
            _cooldownRemain = cooldownSeconds;
            if (disableVisualOnUse && visualRoot) visualRoot.gameObject.SetActive(false);

            return;
        }


        // NEW: If an NPC traffic car hits the forcefield, crash the NPC.
        var npc = other.GetComponentInParent<NPCTrafficCar>();
        if (npc != null && !npc.HasCrashed)
        {
            // Compute impact speed similar to your obstacle path (relative velocity)
            Rigidbody npcRb = other.attachedRigidbody;
            Vector3 relVel = npcRb ? (npcRb.velocity - (carRigidbody ? carRigidbody.velocity : Vector3.zero))
                                   : (carRigidbody ? -carRigidbody.velocity : Vector3.zero);

            float impactSpeed = relVel.magnitude;

            // Crash the NPC away from the player car (use player car position as "impact from")
            npc.ForceCrashFromForcefield(transform.position, impactSpeed, ownerCollider);

            // FX/SFX like your other intercepts
            Vector3 fxPos = other.bounds.ClosestPoint(transform.position);

            if (launchVFX != null)
            {
                Quaternion fxRot = Quaternion.LookRotation((transform.position - fxPos).normalized, Vector3.up);
                var vfx = Instantiate(launchVFX, fxPos, fxRot);
                if (parentVfxToObstacle && vfx != null)
                    vfx.transform.SetParent(other.transform.root, true);
            }

            if (enableLaunchSlowMo)
                StartLaunchSlowMo();

            Play3DClipAtPoint(forcefieldUseClip, fxPos, forcefieldUseVolume);

            if (postFX != null)
                postFX.PlayBurst();

            // Consume the forcefield (optional — delete this if you want NPC-crash to NOT consume it)
            SetArmed(false);
            _cooldownRemain = cooldownSeconds;
            if (disableVisualOnUse && visualRoot) visualRoot.gameObject.SetActive(false);

            return;
        }


        if (_recentlyLaunched.Contains(other)) return;

        if (gatherAllCarColliders && _car != null)
            _carColliders = _car.GetComponentsInChildren<Collider>(true);
        else if (ownerCollider != null && (_carColliders == null || _carColliders.Length == 0))
            _carColliders = new[] { ownerCollider };

        Rigidbody otherRb2 = other.attachedRigidbody;
        Vector3 relVel2 = otherRb2 ? (otherRb2.velocity - (carRigidbody ? carRigidbody.velocity : Vector3.zero))
                                 : (carRigidbody ? -carRigidbody.velocity : Vector3.zero);
        float speed2 = relVel2.magnitude;
        float sev2 = Mathf.InverseLerp(minImpactSpeed, maxImpactSpeed, speed2);

        Rigidbody launchRb = PrepareObstacleForLaunch(other);
        if (launchRb == null) return;

        Collider obstaclePrimaryCol = other;
        if (!obstaclePrimaryCol || obstaclePrimaryCol.attachedRigidbody != launchRb)
        {
            var cols = launchRb.GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < cols.Length; i++)
            {
                if (cols[i] && !cols[i].isTrigger) { obstaclePrimaryCol = cols[i]; break; }
            }
        }

        if (ownerCollider && obstaclePrimaryCol)
        {
            Vector3 sepDir = Vector3.one;
            float sepDist = 0;
            bool penetrates = Physics.ComputePenetration(
                obstaclePrimaryCol, obstaclePrimaryCol.transform.position, obstaclePrimaryCol.transform.rotation,
                ownerCollider, ownerCollider.transform.position, ownerCollider.transform.rotation,
                out sepDir, out sepDist
            );

            if (penetrates && sepDist > 0f)
            {
                float soften = 0.65f;
                Vector3 correction = sepDir * (sepDist * soften);
                launchRb.MovePosition(launchRb.position + correction);
                Physics.SyncTransforms();
            }
        }

        Vector3 vfxPos2 = obstaclePrimaryCol && ownerCollider
            ? obstaclePrimaryCol.ClosestPoint(ownerCollider.bounds.center)
            : (obstaclePrimaryCol ? obstaclePrimaryCol.bounds.ClosestPoint(transform.position) : other.bounds.ClosestPoint(transform.position));

        Vector3 vfxUp = Vector3.up;
        Vector3 vfxForward2 = (ownerCollider ? (ownerCollider.bounds.center - vfxPos2) : (transform.position - vfxPos2));
        if (vfxForward2.sqrMagnitude < 1e-6f) vfxForward2 = transform.forward;
        vfxForward2.Normalize();
        Quaternion vfxRot2 = Quaternion.LookRotation(vfxForward2, vfxUp);

        var obstacleCols = launchRb.GetComponentsInChildren<Collider>(true);
        Collider[] carCols = _carColliders ?? (ownerCollider ? new[] { ownerCollider } : new Collider[0]);
        foreach (var c in obstacleCols)
        {
            if (!c) continue;
            _recentlyLaunched.Add(c);
            foreach (var carCol in carCols)
            {
                if (carCol) Physics.IgnoreCollision(carCol, c, true);
            }
        }
        if (ignoreWithCarSeconds > 0f)
            StartCoroutine(ReenableCollisionsLater(obstacleCols, carCols, ignoreWithCarSeconds));

        Vector3 awayDir = other.bounds.center - (ownerCollider ? ownerCollider.bounds.center : transform.position);
        awayDir.y = 0f;
        if (awayDir.sqrMagnitude < 1e-6f) awayDir = transform.forward;
        awayDir.Normalize();

        float awayDV = Mathf.Lerp(awayVelocityChange.x, awayVelocityChange.y, sev2);
        float upDV = Mathf.Lerp(upVelocityChange.x, upVelocityChange.y, sev2);

        float mass = Mathf.Max(0.01f, launchRb.mass);
        Vector3 desiredDeltaV = awayDir * awayDV + Vector3.up * upDV;
        Vector3 impulse = desiredDeltaV * mass;

        launchRb.AddForce(impulse, ForceMode.Impulse);

        float torqueMag = Mathf.Lerp(torqueAtSeverity.x, torqueAtSeverity.y, sev2);
        float side = Mathf.Sign(Vector3.Dot(awayDir, transform.right));
        if (Mathf.Abs(side) < 0.001f) side = 1f;
        Vector3 yawTorque = Vector3.up * (torqueMag * side);
        Vector3 rollTorque = transform.forward * (torqueMag * side * 0.6f);
        Vector3 pitchTorque = transform.right * (torqueMag * 0.35f * Mathf.Sign(Vector3.Dot(awayDir, transform.forward)));
        launchRb.AddTorque(yawTorque + rollTorque + pitchTorque, ForceMode.VelocityChange);

        if (launchVFX != null)
        {
            var vfx = Instantiate(launchVFX, vfxPos2, vfxRot2);
            if (parentVfxToObstacle && vfx != null)
                vfx.transform.SetParent(launchRb.transform, true);
        }

        if (enableLaunchSlowMo)
            StartLaunchSlowMo();

        Play3DClipAtPoint(forcefieldUseClip, vfxPos2, forcefieldUseVolume);


        // NEW: trigger PPSv2 PostFX burst (Chromatic + Lens Distortion)
        if (postFX != null)
            postFX.PlayBurst();

        SetArmed(false);
        _cooldownRemain = cooldownSeconds;
        if (disableVisualOnUse && visualRoot) visualRoot.gameObject.SetActive(false);
    }

    private IEnumerator ReenableCollisionsLater(Collider[] obstacleCols, Collider[] carCols, float delay)
    {
        float tEnd = Time.time + delay;
        while (Time.time < tEnd) yield return null;

        foreach (var c in obstacleCols)
        {
            if (!c) continue;
            foreach (var carCol in carCols)
            {
                if (carCol) Physics.IgnoreCollision(carCol, c, false);
            }
            _recentlyLaunched.Remove(c);
        }
    }

    private Rigidbody PrepareObstacleForLaunch(Collider hitCol)
    {
        Transform root = hitCol.attachedRigidbody ? hitCol.attachedRigidbody.transform : hitCol.transform.root;
        if (!root) root = hitCol.transform;

        var shuttle = root.GetComponentInChildren<ShuttleTrackObstacle>(true);

        if (disableCrossTrackObstacleOnLaunch)
        {
            if (shuttle) shuttle.enabled = false;
        }

        if (shuttle)
        {
            shuttle.ConvertToPhysicsOnHit(); // disable scripted shuttling, allow physics launch
        }

        // Ensure the launched obstacle has a Rigidbody
        Rigidbody rb = root.GetComponent<Rigidbody>();
        if (!rb && addRigidbodyIfMissing)
        {
            rb = root.gameObject.AddComponent<Rigidbody>();
            rb.mass = Mathf.Max(0.1f, defaultAddedMass);
        }
        if (!rb) return null;

        if (rb.isKinematic) rb.isKinematic = false;
        rb.useGravity = true;
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        rb.WakeUp();
        Physics.SyncTransforms();

        // NEW: Mark obstacle immune to crash logic for the ignore window
        var immunity = root.GetComponent<LaunchImmunityMarker>();
        if (!immunity) immunity = root.gameObject.AddComponent<LaunchImmunityMarker>();
        // Give a tiny grace above ignoreWithCarSeconds to outlast re-enable timing
        immunity.Activate(Mathf.Max(0f, ignoreWithCarSeconds + 0.1f));

        return rb;
    }

    private void StartLaunchSlowMo()
    {
        if (_slowMoRoutine != null) return;
        _slowMoRoutine = StartCoroutine(LaunchSlowMoRoutine());
    }

    private void Play3DClipAtPoint(AudioClip clip, Vector3 pos, float volume = 1f)
    {
        if (clip == null) return;
        GameObject go = new GameObject("SFX_Forcefield_" + (clip ? clip.name : "null"));
        go.transform.position = pos;
        var src = go.AddComponent<AudioSource>();
        src.spatialBlend = 0f; // 3D
        src.clip = clip;
        src.volume = Mathf.Clamp01(volume);
        src.playOnAwake = false;
        src.loop = false;
        src.dopplerLevel = 0f;
        src.Play();
        Destroy(go, clip.length / Mathf.Max(0.01f, Mathf.Abs(src.pitch)));
    }

    private IEnumerator LaunchSlowMoRoutine()
    {
        _ownsSlowMo = true;
        TimeScaleHub.Begin(this, Mathf.Clamp(launchSlowMoScale, 0.05f, 1f), affectFixedDelta: true);

        float holdEnd = Time.realtimeSinceStartup + Mathf.Max(0f, launchSlowMoHold);
        while (Time.realtimeSinceStartup < holdEnd)
            yield return null;

        float ease = Mathf.Max(0f, launchSlowMoEaseOut);
        float t0 = Time.realtimeSinceStartup;
        float t1 = t0 + ease;
        while (Time.realtimeSinceStartup < t1)
        {
            float t = Mathf.InverseLerp(t0, t1, Time.realtimeSinceStartup);
            float scale = Mathf.Lerp(launchSlowMoScale, 1f, t);
            TimeScaleHub.Begin(this, scale, affectFixedDelta: true);
            yield return null;
        }

        TimeScaleHub.End(this);
        _ownsSlowMo = false;
        _slowMoRoutine = null;
    }

    private sealed class ForcefieldTriggerProxy : MonoBehaviour
    {
        private CarForcefield _host;
        private LayerMask _layers;
        public void Init(CarForcefield host, LayerMask layers)
        {
            _host = host;
            _layers = layers;
        }
        private void OnTriggerEnter(Collider other)
        {
            if (!_host) return;
            if (((1 << other.gameObject.layer) & _layers) == 0) return;
            _host.HandleTriggerEnter(other);
        }
    }
}