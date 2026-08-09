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
    [Tooltip("If true, forcefield only intercepts aggressive TrackCreature (beast).")]
    [SerializeField] private bool affectOnlyAggressiveTrackCreatures = true;

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
    [Tooltip("SHORT grace window (seconds) after a forcefield launch. Only the object that was launched is " +
             "ignored/immune during this window (in case the physics push shoves it back onto the car). " +
             "Kept short on purpose: once it elapses, ANY hit — including the same object coming back — " +
             "registers as a normal crash. This is NOT a general invincibility.")]
    [SerializeField, Min(0f)] private float postLaunchGraceSeconds = 0.6f;

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

    [Header("Post-Processing Smoothing (camera feel)")]
    [Tooltip("Seconds the lens/chroma burst eases IN when the forcefield procs. Higher = smoother, less snappy.")]
    [SerializeField, Min(0.01f)] private float fxFadeIn = 0.18f;
    [Tooltip("Seconds the burst holds at full strength (kept near the launch slow-mo hold so the camera and time feel connected).")]
    [SerializeField, Min(0f)] private float fxHold = 0.14f;
    [Tooltip("Seconds the burst eases OUT back to normal. Higher = smoother release (fixes the 'snaps back' feel).")]
    [SerializeField, Min(0.01f)] private float fxFadeOut = 0.4f;

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

    [Header("Track creature (beast)")]
    [SerializeField, Min(0.1f)] private float forcefieldCreatureCorpseMass = 55f;

    [Header("Launch tagging")]
    [Tooltip("How long the launched body counts as forcefield-launched for obstacle-on-obstacle impact damage.")]
    [SerializeField, Min(0.5f)] private float forcefieldLaunchTagDuration = 4f;

    // Runtime
    private SphereCollider _trigger;
    private bool _armed;
    private float _cooldownRemain;
    private bool _finishPortalShield;
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
    public bool IsFinishPortalShield => _finishPortalShield;

    /// <summary>
    /// Finish-portal sequence: stay armed, launch obstacles on contact, never crash, no bubble/slow-mo/postFX.
    /// </summary>
    public void SetFinishPortalShield(bool on)
    {
        _finishPortalShield = on;
        if (on)
        {
            enabled = true;
            _cooldownRemain = 0f;
            SetArmed(true);
            if (visualRoot != null)
                visualRoot.gameObject.SetActive(false);
        }
        else
        {
            SetArmed(false);
            _cooldownRemain = 0f;
        }
    }

    /// <summary>
    /// Some obstacles (or overlap-based checks) can confirm a hit without going through the forcefield trigger first,
    /// so the trigger may run later or not at all in the same step.
    /// Call this when an obstacle overlap confirms the car is inside the hit volume; if the forcefield consumes the hit, returns true and the caller must not apply crash damage.
    /// </summary>
    public bool TryInterceptObstacleForOverlapHit(Collider obstacleCollider)
    {
        if (!_armed || obstacleCollider == null)
            return false;
        if (ownerCollider != null && obstacleCollider == ownerCollider)
            return false;

        // Finish portal: always consume the hit (crash skipped by caller even if nothing launched).
        if (_finishPortalShield)
        {
            HandleTriggerEnter(obstacleCollider);
            return true;
        }

        bool wasArmed = _armed;
        HandleTriggerEnter(obstacleCollider);
        return wasArmed && !_armed;
    }

    public void ArmNow()
    {
        SetArmed(true);
        _cooldownRemain = 0f;
    }

    public void DisarmNow()
    {
        if (_finishPortalShield) return;
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
        if (disableVisualOnUse && visualRoot && !_finishPortalShield)
            visualRoot.gameObject.SetActive(v);

        if (_trigger)
            _trigger.enabled = v;

        SyncVisual(true);

        // Play rearm sound only when transitioning from disarmed -> armed (not finish-portal shield).
        if (v && !wasArmed && !_finishPortalShield)
        {
            // play rearm SFX at the car position
            Play3DClipAtPoint(forcefieldRearmClip, transform.position, forcefieldRearmVolume);
        }
    }

    private void SyncVisual(bool instant)
    {
        if (!visualRoot) return;
        visualRoot.localScale = Vector3.one * (triggerRadius * 2f);

        // Finish-portal shield launches silently — no bubble visual.
        bool show = !_finishPortalShield && _armed && this.enabled && (!disableVisualOnUse ? true : _armed);
        if (visualRoot.gameObject.activeSelf != show)
            visualRoot.gameObject.SetActive(show);

    }

    private void ConsumeForcefieldAfterLaunch()
    {
        if (_finishPortalShield)
            return;

        SetArmed(false);
        _cooldownRemain = cooldownSeconds;
        if (disableVisualOnUse && visualRoot) visualRoot.gameObject.SetActive(false);
    }

    private void PlayLaunchPresentation(Vector3 fxPos, Quaternion fxRot, Transform parentForVfx)
    {
        if (launchVFX != null)
        {
            var vfx = Instantiate(launchVFX, fxPos, fxRot);
            if (parentVfxToObstacle && vfx != null && parentForVfx != null)
                vfx.transform.SetParent(parentForVfx, true);
        }

        // Finish portal: no slow-mo / postFX / popup spam — just fling the obstacle.
        if (_finishPortalShield)
        {
            Play3DClipAtPoint(forcefieldUseClip, fxPos, forcefieldUseVolume * 0.7f);
            return;
        }

        if (enableLaunchSlowMo)
            StartLaunchSlowMo();

        Play3DClipAtPoint(forcefieldUseClip, fxPos, forcefieldUseVolume);

        if (postFX != null)
            postFX.PlayBurst(fxFadeIn, fxHold, fxFadeOut);

        ShowForcefieldInvinciblePopup();
    }

    internal void HandleTriggerEnter(Collider other)
    {
        if (!_armed) return;
        if (!other || other == ownerCollider) return;

        // Handle TrackCreature BEFORE obstacle layer filtering so beast interception can be independent
        // from shared creature layer setup.
        var creature = other.GetComponentInParent<TrackCreature>();
        if (creature != null && !creature.IsDead)
        {
            if (affectOnlyAggressiveTrackCreatures && creature.BehaviorType != CreatureBehaviorType.Aggressive)
                return;

            if (_recentlyLaunched.Contains(other)) return;

            if (!creature.TryBeginForcefieldPhysicsLaunch(forcefieldCreatureCorpseMass))
                return;

            _recentlyLaunched.Add(other);

            Transform creatureRoot = creature.transform.root;
            Rigidbody creatureRb = creature.GetComponent<Rigidbody>();
            if (creatureRb == null)
            {
                creature.FinalizeForcefieldLaunchKill();
                return;
            }

            Collider[] creatureCols = creatureRoot.GetComponentsInChildren<Collider>(true);
            Collider[] carCols = _carColliders ?? (ownerCollider ? new[] { ownerCollider } : new Collider[0]);

            foreach (var c in creatureCols)
            {
                if (!c) continue;
                _recentlyLaunched.Add(c);
                foreach (var carCol in carCols)
                {
                    if (carCol) Physics.IgnoreCollision(carCol, c, true);
                }
            }

            if (postLaunchGraceSeconds > 0f)
                StartCoroutine(ReenableCollisionsLater(creatureCols, carCols, postLaunchGraceSeconds));

            EnsureLaunchImmunity(creatureRb.transform);
            ApplyForcefieldLaunchPhysicsAndPresentation(other, creatureRb);
            ArmForcefieldLaunchTag(creatureRb.gameObject);

            creature.FinalizeForcefieldLaunchKill();
            return;
        }

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
            thrown.InterceptedByForcefield(t_awayDir, t_awayDV, t_upDV, postLaunchGraceSeconds);

            // Spawn VFX at interception point
            Vector3 vfxPos = other.bounds.ClosestPoint(transform.position);
            Vector3 vfxForward = (ownerCollider ? (ownerCollider.bounds.center - vfxPos) : (transform.position - vfxPos));
            if (vfxForward.sqrMagnitude < 1e-6f) vfxForward = transform.forward;
            vfxForward.Normalize();
            Quaternion vfxRot = Quaternion.LookRotation(vfxForward, Vector3.up);

            PlayLaunchPresentation(vfxPos, vfxRot, other.transform.root);

            // mark as recently handled
            _recentlyLaunched.Add(other);

            ConsumeForcefieldAfterLaunch();

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

            if (gatherAllCarColliders && _car != null)
                _carColliders = _car.GetComponentsInChildren<Collider>(true);
            else if (ownerCollider != null && (_carColliders == null || _carColliders.Length == 0))
                _carColliders = new[] { ownerCollider };

            // Gather NPC colliders
            Transform npcRoot = npc.transform.root;
            Collider[] npcCols = npcRoot.GetComponentsInChildren<Collider>(true);
            Collider[] carCols = _carColliders ?? (ownerCollider ? new[] { ownerCollider } : new Collider[0]);

            // Add immunity marker so CarController ignores it even if something still �touches�
            var immunity = npcRoot.GetComponent<LaunchImmunityMarker>();
            if (!immunity) immunity = npcRoot.gameObject.AddComponent<LaunchImmunityMarker>();
            immunity.Activate(Mathf.Max(0f, postLaunchGraceSeconds + 0.1f));

            // Ignore collisions between NPC and player car temporarily (like launched obstacles)
            foreach (var c in npcCols)
            {
                if (!c) continue;

                _recentlyLaunched.Add(c); // so the forcefield proxy won�t re-handle repeatedly
                foreach (var carCol in carCols)
                {
                    if (carCol) Physics.IgnoreCollision(carCol, c, true);
                }
            }

            if (postLaunchGraceSeconds > 0f)
                StartCoroutine(ReenableCollisionsLater(npcCols, carCols, postLaunchGraceSeconds));


            // FX/SFX like your other intercepts
            Vector3 fxPos = other.bounds.ClosestPoint(transform.position);
            Quaternion fxRot = Quaternion.LookRotation((transform.position - fxPos).normalized, Vector3.up);
            PlayLaunchPresentation(fxPos, fxRot, other.transform.root);

            // Consume the forcefield (optional — delete this if you want NPC-crash to NOT consume it)
            ConsumeForcefieldAfterLaunch();

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

        var bounceBack = other.GetComponentInParent<TrackObstacleBounceBack>();
        if (bounceBack != null)
        {
            bounceBack.DetachForForcefieldLaunch();
        }
        var rollingLog = other.GetComponentInParent<RollingLogAlongTrack>();
        if (rollingLog != null)
        {
            Vector3 ffCenter = ownerCollider ? ownerCollider.bounds.center : transform.position;
            rollingLog.ApplyForcefieldLaunch(ffCenter, speed2, 0f, 0f);
        }
        Rigidbody launchRb = PrepareObstacleForLaunch(other);
        if (launchRb == null) return;

        var obstacleCols = launchRb.GetComponentsInChildren<Collider>(true);
        Collider[] CarCols = _carColliders ?? (ownerCollider ? new[] { ownerCollider } : new Collider[0]);
        foreach (var c in obstacleCols)
        {
            if (!c) continue;
            _recentlyLaunched.Add(c);
            foreach (var carCol in CarCols)
            {
                if (carCol) Physics.IgnoreCollision(carCol, c, true);
            }
        }
        if (postLaunchGraceSeconds > 0f)
            StartCoroutine(ReenableCollisionsLater(obstacleCols, CarCols, postLaunchGraceSeconds));

        ArmForcefieldLaunchTag(launchRb.gameObject);
        ApplyForcefieldLaunchPhysicsAndPresentation(other, launchRb);
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

        var cross = root.GetComponentInChildren<CrossTrackObstacle>(true);
        if (cross != null)
            cross.DetachFromScriptedPathForForcefield();

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

        EnsureLaunchImmunity(rb.transform);

        return rb;
    }

    private void EnsureLaunchImmunity(Transform target)
    {
        if (target == null) return;
        var immunity = target.GetComponent<LaunchImmunityMarker>();
        if (!immunity) immunity = target.gameObject.AddComponent<LaunchImmunityMarker>();
        immunity.Activate(Mathf.Max(0f, postLaunchGraceSeconds + 0.1f));
    }

    private void ArmForcefieldLaunchTag(GameObject host)
    {
        if (host == null) return;
        float dur = Mathf.Max(forcefieldLaunchTagDuration, postLaunchGraceSeconds + 0.5f);
        var tag = host.GetComponent<ForcefieldLaunchTag>();
        if (tag == null) tag = host.AddComponent<ForcefieldLaunchTag>();
        tag.Arm(dur);
    }

    /// <summary>
    /// Penetration resolve, ignore car collisions, impulse, VFX/SFX, disarm. Caller sets up creature/obstacle-specific ignore lists where needed.
    /// </summary>
    private void ApplyForcefieldLaunchPhysicsAndPresentation(Collider other, Rigidbody launchRb)
    {
        if (other == null || launchRb == null) return;

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
                // Horizontal separation only — avoids popping the car upward from hull overlap with tall props.
                correction.y = 0f;
                if (correction.sqrMagnitude > 1e-8f)
                {
                    launchRb.MovePosition(launchRb.position + correction);
                    Physics.SyncTransforms();
                }
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

        Rigidbody otherRb2 = other.attachedRigidbody;
        Vector3 relVel2 = otherRb2 ? (otherRb2.velocity - (carRigidbody ? carRigidbody.velocity : Vector3.zero))
                                 : (carRigidbody ? -carRigidbody.velocity : Vector3.zero);
        float speed2 = relVel2.magnitude;
        float sev2 = Mathf.InverseLerp(minImpactSpeed, maxImpactSpeed, speed2);

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

        PlayLaunchPresentation(vfxPos2, vfxRot2, launchRb.transform);
        ConsumeForcefieldAfterLaunch();
    }

    private void ShowForcefieldInvinciblePopup()
    {
        if (!RacingPopups.IsReady) return;
        Vector3 p = (_car != null ? _car.transform.position : transform.position) + Vector3.up * 1.2f;
        RacingPopups.Invincible(p);
    }

    private void StartLaunchSlowMo()
    {
        if (_slowMoRoutine != null) return;
        _slowMoRoutine = StartCoroutine(LaunchSlowMoRoutine());
    }

    /// <summary>
    /// Immediately end the forcefield launch slow-mo and release its hub ownership. Used by the run-end
    /// flow so a forcefield slow-mo can't linger/stack into the death-stop slow-mo or the results freeze.
    /// </summary>
    public void CancelLaunchSlowMo()
    {
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
            _host.HandleTriggerEnter(other);
        }
    }
}