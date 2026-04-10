using System;
using System.Collections;
using UnityEngine;

using Random = UnityEngine.Random;

/// <summary>
/// Creature behavior states.
/// </summary>
public enum CreatureState
{
    Idle,
    Wandering,
    Fleeing,
    Charging,
    Dead
}

/// <summary>
/// How the creature was killed - affects reward type.
/// </summary>
public enum CreatureKillSource
{
    Car,        // Run over by player - rewards coins
    Turret,     // Shot by turret - rewards sprockets
    Other       // Any other source - NO reward
}

/// <summary>
/// Base behavior component for track creatures.
/// Handles movement, player detection, and behavior state machine.
/// Supports Passive (dumb wandering), Scared (flees from player), and Aggressive (charges player) behaviors.
/// 
/// COLLISION NOTES:
/// - All creatures use TRIGGER colliders to avoid disrupting car physics
/// - Passive/Scared: Car drives through them, they die, give coins
/// - Aggressive (beast): Player contact applies crash/knockback only; beast keeps hunting. Cross/bounce/log/thrown hits fling it with physics, then it dies/despawns (see <see cref="LaunchAggressiveBeastByObstacleThenDie"/>).
/// 
/// REWARD SYSTEM:
/// - Killed by CAR → Coins (handled here)
/// - Killed by TURRET → Sprockets (handled by RacingBullet)
/// </summary>
public class TrackCreature : MonoBehaviour, IDamageable, ITurretDamageable
{
    #region Inspector Overrides (Optional)

    [Header("Optional Overrides")]
    [Tooltip("If assigned, uses this instead of auto-finding.")]
    [SerializeField] private Collider hitCollider;

    [Header("UI Popup (Car Run-Over + NPC crush)")]
    [Tooltip("Comic popup when the player runs over this creature, or when NPC traffic crushes it (same style / height).")]
    [SerializeField] private bool enableRunOverPopup = true;
    [SerializeField] private RacingPopupType runOverPopupType = RacingPopupType.CreatureSplat;
    [SerializeField] private float runOverPopupHeight = 1.2f;

    [Header("UI Popup (creature eats creature — NOM style)")]
    [Tooltip("When this aggressive beast kills a scared critter on contact, spawn eat-style popup at the prey.")]
    [SerializeField] private bool enableBeastEatPopup = true;
    [Tooltip("When this scared critter kills a passive bug on contact, use the same eat popup (type + height below).")]
    [SerializeField] private bool enableCritterEatBugPopup = true;
    [Tooltip("Popup type for both beast→critter and critter→bug (e.g. BeastEat style asset with NOM NOM lines).")]
    [SerializeField] private RacingPopupType beastEatPopupType = RacingPopupType.BeastEat;
    [Tooltip("World height above the prey for both eat interactions.")]
    [SerializeField] private float beastEatPopupHeight = 1.1f;

    [Tooltip("Visual root to animate/rotate separately from physics.")]
    [SerializeField] private Transform visualRoot;

    [Tooltip("Layer mask for ground raycasting.")]
    [SerializeField] private LayerMask groundLayer = ~0;


    [Header("Movement Avoidance")]
    [Tooltip("If enabled, creatures will steer around colliders on these layers while moving (wander/idle/flee/charge).")]
    [SerializeField] private bool enableMovementAvoidance = true;

    [Tooltip("Layers to avoid while moving (obstacles, walls, props, etc).")]
    [SerializeField] private LayerMask movementAvoidanceLayers = 0;

    [Tooltip("Extra layers treated as obstacles for Passive/Scared only (e.g. Rolling Log). Beasts ignore this mask.")]
    [SerializeField] private LayerMask rollingLogAvoidanceLayers = 0;

    [Tooltip("If true, trigger colliders count as obstacles. Many props use triggers; leaving this off makes casts ignore them entirely.")]
    [SerializeField] private bool avoidanceIncludeTriggers = true; // default on so existing trigger props are detected

    [Tooltip("Sphere radius for obstacle sensing while moving.")]
    [SerializeField, Min(0.01f)] private float avoidanceRadius = 0.35f;

    [Tooltip("How far ahead we check for blockers (meters).")]
    [SerializeField, Min(0.05f)] private float avoidanceLookAhead = 2.4f;

    [Tooltip("Height above pivot for the sphere cast origin (helps if pivot is low).")]
    [SerializeField] private float avoidanceCastHeight = 0.35f;

    [Tooltip("Rays in a horizontal fan when picking a way around (3–21).")]
    [SerializeField, Range(3, 21)] private int avoidanceRayCount = 11;

    [Tooltip("Total fan angle (degrees) centered on desired move direction.")]
    [SerializeField, Range(20f, 160f)] private float avoidanceFanAngleDeg = 100f;

    [Tooltip("How quickly smoothed avoidance direction catches up to the best clear direction.")]
    [SerializeField, Min(0.5f)] private float avoidanceSteerSmoothSpeed = 14f;

    [Tooltip("When bug-idle path is blocked, how fast to shift lateral goal (meters/sec on offset).")]
    [SerializeField, Min(0f)] private float avoidanceIdlePathNudgeSpeed = 5f;

    [Tooltip("If we can't slide, we pick a side-step amount (meters per second-ish bias).")]
    [SerializeField] private float avoidanceSideBias = 0.9f;

    [Tooltip("How fast we bias sideways when avoiding (higher = snappier).")]
    [SerializeField]
    private float avoidanceResponse =
12f;

    [Header("Aggressive - Priority")]
    public float aggressivePlayerPriorityRadius = 12f;

    [Header("Creature Sensing")]
    [Tooltip("Layers used for creature-vs-creature sensing (aggressive hunts scared; scared flees aggressive). Put your creatures on a dedicated layer for best results.")]
    [SerializeField] private LayerMask creatureSenseLayers = ~0;

    [Header("Crush / Obstacle Interaction")]
    [Tooltip("Layers that can 'crush' creatures. Only used when the other collider has a Rigidbody OR is tagged as an obstacle.")]
    [SerializeField] private LayerMask crushLayers = ~0;

    [Tooltip("Minimum obstacle speed required to squish a creature. If the obstacle isn't moving, it won't squish.")]
    [SerializeField, Min(0f)] private float minCrushSpeed = 0.75f;

    [Tooltip("If true, tag-based crushers without rigidbodies can still squish (treated as moving). Recommend FALSE.")]
    [SerializeField] private bool allowTagCrushWithoutRigidbody = false;

    [Tooltip("Any collider with one of these tags is treated as an obstacle crush even if it has no Rigidbody.")]
    [SerializeField] private string[] obstacleTags = new string[] { "Obstacle" };

    [Tooltip("Unused for Aggressive (beasts use obstacle-type whitelist for crush death). Kept for serialized prefabs.")]
    [SerializeField] private float aggressiveCrushMassThreshold = 80f;

    [Header("Aggressive — lethal obstacle launch")]
    [Tooltip("Dynamic Rigidbody mass while the beast is being flung by cross / bounce / log / thrown hits.")]
    [SerializeField, Min(0.1f)] private float obstacleLaunchCorpseMass = 85f;
    [SerializeField, Min(0f)] private float obstacleLaunchBaseVelocityChange = 9f;
    [SerializeField, Min(0f)] private float obstacleLaunchSpeedScale = 0.45f;
    [SerializeField, Min(0f)] private float obstacleLaunchMaxVelocityChange = 34f;
    [SerializeField, Min(0f)] private float obstacleLaunchUpVelocityChange = 6f;
    [SerializeField] private float obstacleLaunchTorque = 10f;
    [Tooltip("Time flying before death/despawn is applied (lets the fling read clearly).")]
    [SerializeField, Min(0.05f)] private float obstacleLaunchDeathDelay = 0.42f;

    [Header("Ground Snapping")]
    [SerializeField] private float groundRayHeight = 2f;
    [SerializeField] private float groundRayDistance = 5f;
    [SerializeField] private float groundOffset = 0.05f;
    [SerializeField] private float groundSnapSpeed = 15f;

    [Header("Rotation")]
    [SerializeField] private float rotationSpeed = 10f;
    [SerializeField] private bool alignToGround = true;

    [Header("Animation (Optional)")]
    [SerializeField] private Animator animator;
    [SerializeField] private string speedParam = "Speed";
    [SerializeField] private string runningParam = "IsRunning";
    [SerializeField] private string deadParam = "IsDead";

    [Header("Audio (Optional)")]
    [SerializeField] private AudioSource audioSource;
    [SerializeField] private AudioClip idleSound;
    [SerializeField] private AudioClip runSound;
    [SerializeField] private AudioClip deathSound;
    [SerializeField] private AudioClip aggroSound;

    [Header("Kill Audio")]
    [Tooltip("Sound played when killed by turret.")]
    [SerializeField] private AudioClip turretKillSound;
    [Tooltip("Sound played when killed by car.")]
    [SerializeField] private AudioClip carKillSound;
    [SerializeField, Range(0f, 1f)] private float killSoundVolume = 1f;



    [Header("Effects (Optional)")]
    [SerializeField] private GameObject deathEffectPrefab;
    [SerializeField] private Transform coinSpawnPoint;

    [Header("Debug")]
    [SerializeField] private bool showDebugGizmos = false;

    #endregion

    #region References & Config

    protected TrackCreatureSpawner spawner;
    protected Transform playerTransform;
    protected CreatureTypeConfig config;
    protected ProceduralTrackGenerator trackGenerator;

    #endregion

    #region State

    // Core state
    protected CreatureState currentState = CreatureState.Idle;
    protected CreatureBehaviorType behaviorType;
    protected bool isInitialized = false;
    protected bool isDead = false;

    /// <summary>When true, AI/movement is skipped so physics launch can run (forcefield or lethal obstacle fling) before <see cref="Die"/>.</summary>
    private bool _forcefieldPhysicsLaunchActive;

    // Kill tracking
    protected CreatureKillSource killSource = CreatureKillSource.Other;
    protected float currentHealth = 100f;

    // Track position
    protected float currentDistanceAlongTrack;
    protected float currentLateralOffset;
    protected float targetLateralOffset;

    // Movement
    protected Vector3 currentVelocity;
    protected float currentSpeed;
    protected float currentFleeSpeed; // For scared creatures - builds up over time

    // Wander state
    protected float wanderTimer;
    protected float wanderDirectionX; // -1 to 1 lateral direction
    protected float wanderDirectionZ; // -1 to 1 forward/back direction
    protected float nextWanderChangeTime;


    // Idle "bug" wiggle state (used by Scared while not detected)
    protected float idleAnchorDistance;
    protected float idleAnchorLateral;
    protected float idleTargetDistOffset;
    protected float idleTargetLateralOffset;
    protected float idleCurrentDistOffset;
    protected float idleCurrentLateralOffset;
    protected float nextIdleChangeTime;
    // Detection state
    protected bool playerDetected = false;
    protected float playerDistance;
    protected Vector3 playerDirection;



    // Threat / creature-vs-creature interactions
    protected Transform threatTransform;
    protected bool threatIsAggressive;
    protected float threatDistance;
    protected Vector3 threatDirection;

    // Scared: hunt passive bugs. Aggressive: hunt scared (set in threat scan).
    protected Transform chaseTargetTransform;

    /// <summary>Nearest NPC traffic root (beast chase / critter flee).</summary>
    protected Transform vehicleChaseTransform;
    protected float vehicleChaseDistance = float.MaxValue;

    protected bool isBullRushCharging = false;      // True during wind-up phase
    protected bool isBullRushActive = false;        // True during the actual rush
    protected float bullRushChargeTimer = 0f;       // Timer for charge-up phase
    protected float bullRushActiveTimer = 0f;       // Timer for rush duration
    protected float bullRushCooldownTimer = 0f;    // Cooldown between rushes
    protected Vector3 bullRushDirection;            // Rush heading (XZ); steered slightly toward player, not obstacle-avoidance

    /// <summary>World position when the active rush phase began (for overshoot timeout).</summary>
    private Vector3 _bullRushLaunchStartWorld;

    /// <summary>Horizontal distance to target at rush launch (for overshoot end).</summary>
    private float _bullRushLaunchDirectDistance;

    protected LineRenderer bullRushLineRenderer;
    protected float bullRushLineAlpha = 0f;
    protected bool bullRushLineFadingOut = false;

    // Ground state
    protected bool isGrounded = true;
    protected Vector3 groundNormal = Vector3.up;
    protected float currentGroundY;

    // Cached colliders
    private Collider[] _allColliders;

    private const int MAX_THREATS = 8;
    private Collider[] _threatColliderBuffer = new Collider[MAX_THREATS];
    private readonly Collider[] _creatureQueryBuffer = new Collider[32];
    private readonly Collider[] _npcOverlapBuffer = new Collider[24];
    private Vector3 _combinedFleeDirection = Vector3.forward;
    private int _activeThreatCount = 0;

    private LayerMask _npcTrafficLayers;

    private float _idleRhythmPhaseEndTime;
    private bool _idleRhythmWalking = true;

    private Vector3 _avoidanceSteerSmoothed;

    #endregion

    #region Properties

    public CreatureState CurrentState => currentState;
    public bool IsDead => isDead;
    public CreatureBehaviorType BehaviorType => behaviorType;
    public float DistanceToPlayer => playerDistance;


    public bool IsInitialized => isInitialized;
    public float DistanceAlongTrack => currentDistanceAlongTrack;

    public bool IsBullRushActive => isBullRushActive;
    public Vector3 BullRushDirection => bullRushDirection;

    /// <summary>Movement avoidance mask; beasts do not include <see cref="rollingLogAvoidanceLayers"/>.</summary>
    private LayerMask GetAvoidanceLayerMask()
    {
        LayerMask m = movementAvoidanceLayers;
        if (behaviorType != CreatureBehaviorType.Aggressive)
            m.value |= rollingLogAvoidanceLayers.value;
        return m;
    }
    #endregion

    #region Initialization

    /// <summary>
    /// Initialize the creature with spawner reference and config.
    /// Called by TrackCreatureSpawner after instantiation.
    /// </summary>
    public virtual void Initialize(TrackCreatureSpawner spawnerRef, Transform player, CreatureTypeConfig creatureConfig, float distanceAlongTrack)
    {
        spawner = spawnerRef;
        playerTransform = player;
        config = creatureConfig;
        currentDistanceAlongTrack = distanceAlongTrack;
        behaviorType = creatureConfig.behaviorType;
        trackGenerator = spawner.GetTrackGenerator();
        _npcTrafficLayers = spawner != null ? spawner.NpcTrafficLayerMask : default;

        if (spawnerRef != null && movementAvoidanceLayers.value == 0)
        {
            LayerMask obs = spawnerRef.CreatureObstacleAvoidanceLayers;
            if (obs.value != 0)
                movementAvoidanceLayers = obs;
        }

        // Set health (creatures die in one hit from turret, but this allows for future expansion)
        currentHealth = 1f;

        // Initialize lateral offset based on current position
        InitializeLateralOffset();

        // Initialize wander state
        ResetWanderDirection();

        // Initialize flee speed
        currentFleeSpeed = config.scaredBaseFleeSpeed;

        // IMPORTANT: Make all colliders triggers to avoid physics disruption
        SetupColliders();

        // Set initial state based on behavior type
        switch (behaviorType)
        {
            case CreatureBehaviorType.Passive:
                SetState(config != null && config.passiveUseBugIdleMovement
                    ? CreatureState.Idle
                    : CreatureState.Wandering);
                break;
            case CreatureBehaviorType.Scared:
            case CreatureBehaviorType.Aggressive:
                SetState(CreatureState.Idle);
                break;
        }

        if (behaviorType == CreatureBehaviorType.Aggressive && config.useBullRush && config.bullRushShowTelegraph)
        {
            SetupBullRushLineRenderer();
        }

        isInitialized = true;
    }

    /// <summary>
    /// Sets up all colliders as triggers to prevent physics disruption.
    /// Creatures detect the car via OnTriggerEnter instead of OnCollisionEnter.
    /// </summary>
    private void SetupColliders()
    {
        _allColliders = GetComponentsInChildren<Collider>(true);

        foreach (var col in _allColliders)
        {
            if (col != null)
            {
                // Make all colliders triggers - this prevents physics knockback on the car
                col.isTrigger = true;
            }
        }

        // Also disable any Rigidbody physics that could cause issues
        var rb = GetComponent<Rigidbody>();
        if (rb != null)
        {
            rb.isKinematic = true; // Creatures move via code, not physics
        }
    }

    private void InitializeLateralOffset()
    {
        if (spawner == null || trackGenerator == null) return;

        // Get current path position
        spawner.SamplePath(currentDistanceAlongTrack, out Vector3 pathPos, out Vector3 pathForward);

        // Calculate lateral offset from path center
        Vector3 toCreature = transform.position - pathPos;
        Vector3 flatForward = pathForward;
        flatForward.y = 0f;
        flatForward.Normalize();

        Vector3 right = Vector3.Cross(Vector3.up, flatForward).normalized;
        currentLateralOffset = Vector3.Dot(toCreature, right);
        targetLateralOffset = currentLateralOffset;
    }

    #endregion

    #region IDamageable / ITurretDamageable Implementation

    /// <summary>
    /// Legacy damage interface - used by non-turret sources.
    /// Awards COINS when killed.
    /// </summary>
    public void ApplyDamage(float amount)
    {
        if (isDead) return;

        currentHealth -= amount;

        if (currentHealth <= 0f)
        {
            // Mark as non-turret kill for coin reward
            killSource = CreatureKillSource.Other;
            Die();
        }
    }

    /// <summary>
    /// Turret damage interface - used by RacingBullet.
    /// Does NOT award anything here (bullet handles sprocket rewards).
    /// </summary>
    public bool ApplyTurretDamage(float amount, out int sprocketReward)
    {
        sprocketReward = config != null ? config.coinReward : 1; // Use same value

        if (isDead)
        {
            return false;
        }

        currentHealth -= amount;

        if (currentHealth <= 0f)
        {
            // Mark as turret kill - NO reward given here (bullet handles it)
            killSource = CreatureKillSource.Turret;
            Die();
            return true; // Was killed
        }

        return false; // Survived
    }

    #endregion

    #region Unity Lifecycle

    protected virtual void Update()
    {
        if (!isInitialized || isDead) return;
        if (_forcefieldPhysicsLaunchActive) return;

        float dt = Time.deltaTime;

        // Update player detection
        UpdatePlayerDetection();


        // Update creature-vs-creature threat detection
        UpdateThreatDetection();
        // Update state machine
        UpdateStateMachine(dt);

        // Update movement
        UpdateMovement(dt);

        // Update ground snapping
        UpdateGroundSnap(dt);

        // Update rotation
        UpdateRotation(dt);

        UpdateAnimation();

        // Update bull rush telegraph line (for aggressive creatures)
        if (behaviorType == CreatureBehaviorType.Aggressive && config != null && config.useBullRush)
        {
            UpdateBullRushTelegraph(dt);
        }
    }

    /// <summary>
    /// All creatures use trigger detection to avoid physics disruption.
    /// </summary>
    protected virtual void OnTriggerEnter(Collider other)
    {
        if (isDead) return;
        if (other == null) return;

        // Player car hit (run over)
        if (IsPlayerCollider(other))
        {
            OnHitByPlayer(other);
            return;
        }

        // Beast: kills critters on contact
        if (behaviorType == CreatureBehaviorType.Aggressive)
        {
            TrackCreature otherCreature = other.GetComponentInParent<TrackCreature>();

            if (otherCreature != null &&
                otherCreature != this &&
                !otherCreature.isDead &&
                otherCreature.behaviorType == CreatureBehaviorType.Scared)
            {
                if (enableBeastEatPopup)
                    SpawnEatStylePopupOnPrey(otherCreature);
                otherCreature.killSource = CreatureKillSource.Other;
                otherCreature.Die();
                chaseTargetTransform = null;
                SetState(CreatureState.Idle);
                return;
            }
        }

        // Critter: kills bugs on contact
        if (behaviorType == CreatureBehaviorType.Scared)
        {
            TrackCreature otherCreature = other.GetComponentInParent<TrackCreature>();
            if (otherCreature != null &&
                otherCreature != this &&
                !otherCreature.isDead &&
                otherCreature.behaviorType == CreatureBehaviorType.Passive)
            {
                if (enableCritterEatBugPopup)
                    SpawnEatStylePopupOnPrey(otherCreature);
                otherCreature.killSource = CreatureKillSource.Other;
                otherCreature.Die();
                chaseTargetTransform = null;
                return;
            }
        }

        if (IsCrushingCollider(other, out float otherMass, out float otherSpeed))
        {
            OnHitByCrushingObstacle(other, otherMass, otherSpeed);
        }
    }

    #endregion

    #region State Machine

    protected virtual void UpdateStateMachine(float dt)
    {
        switch (behaviorType)
        {
            case CreatureBehaviorType.Passive:
                UpdatePassiveBehavior(dt);
                break;
            case CreatureBehaviorType.Scared:
                UpdateScaredBehavior(dt);
                break;
            case CreatureBehaviorType.Aggressive:
                UpdateAggressiveBehavior(dt);
                break;
        }
    }

    protected void SetState(CreatureState newState)
    {
        if (currentState == newState) return;

        // Exit current state
        OnExitState(currentState);

        CreatureState oldState = currentState;
        currentState = newState;

        // Enter new state
        OnEnterState(newState, oldState);
    }

    protected virtual void OnEnterState(CreatureState state, CreatureState previousState)
    {
        switch (state)
        {
            case CreatureState.Idle:
                currentSpeed = 0f;
                _idleRhythmPhaseEndTime = 0f;

                // Idle anchor for bug-style idle movement.
                if (config != null)
                {
                    if (behaviorType == CreatureBehaviorType.Passive && config.passiveUseBugIdleMovement)
                        CaptureIdleAnchor();

                    if (behaviorType == CreatureBehaviorType.Scared && config.scaredIdleUseBugMovement)
                        CaptureIdleAnchor();

                    if (behaviorType == CreatureBehaviorType.Aggressive && config.aggressiveIdleUseBugMovement)
                        CaptureIdleAnchor();
                }
                break;


            case CreatureState.Wandering:
                ResetWanderDirection();
                break;

            case CreatureState.Fleeing:
                currentFleeSpeed = behaviorType == CreatureBehaviorType.Passive && config != null
                    ? config.passiveFleeBaseSpeed
                    : config.scaredBaseFleeSpeed;
                PlaySound(runSound);
                break;

            case CreatureState.Charging:
                if (config != null && config.useBullRush)
                {
                    isBullRushCharging = false;
                    isBullRushActive = false;
                    bullRushChargeTimer = 0f;
                    bullRushActiveTimer = 0f;
                }
                PlaySound(aggroSound);
                break;

            case CreatureState.Dead:
                OnDeath();
                break;
        }
    }

    protected virtual void OnExitState(CreatureState state)
    {
        // Clean up state-specific stuff if needed
    }

    #endregion

    #region Behavior Updates

    /// <summary>
    /// Bug (Passive): bug idle or wander; flees from critters (Scared) when configured.
    /// </summary>
    protected virtual void UpdatePassiveBehavior(float dt)
    {
        if (config == null || currentState == CreatureState.Dead) return;

        bool critterSpotted = config.passiveFleeFromScared &&
                              threatTransform != null &&
                              threatDistance < config.passiveScaredDetectRadius;
        bool stillFleeingCritter = config.passiveFleeFromScared &&
                                   threatTransform != null &&
                                   threatDistance < config.passiveScaredLoseDistance;

        if (currentState == CreatureState.Fleeing)
        {
            if (!stillFleeingCritter)
            {
                SetState(config.passiveUseBugIdleMovement ? CreatureState.Idle : CreatureState.Wandering);
                currentFleeSpeed = config.passiveFleeBaseSpeed;
            }
            else
            {
                UpdateFleeing(dt);
                return;
            }
        }

        if (critterSpotted)
        {
            SetState(CreatureState.Fleeing);
            UpdateFleeing(dt);
            return;
        }

        if (config.passiveUseBugIdleMovement)
        {
            if (currentState != CreatureState.Idle)
                SetState(CreatureState.Idle);

            UpdateBugIdleMovement(
                dt,
                config.passiveIdleBugMoveSpeed,
                config.passiveIdleBugDirectionChangeInterval,
                config.passiveIdleBugLateralRadius,
                config.passiveIdleBugForwardRadius,
                config.passiveFleeMaxOffRoadDistance * 0.5f);
        }
        else
        {
            if (currentState != CreatureState.Wandering)
                SetState(CreatureState.Wandering);

            UpdateWandering(dt);
        }
    }

    /// <summary>
    /// Scared behavior: aggressive threats override; otherwise may hunt passive bugs (ignoring cars until hunt ends), else flee from cars, else calm idle/wander.
    /// </summary>
    protected virtual void UpdateScaredBehavior(float dt)
    {
        if (config == null) return;

        bool hasAggressiveThreat = threatIsAggressive && threatTransform != null &&
                                   threatDistance < config.scaredAggressiveDetectRadius;
        bool hasPlayerThreat = playerDetected && playerDistance < config.scaredDetectionRadius;
        bool hasNpcThreat = config.scaredFleeFromNpcTraffic &&
                            _npcTrafficLayers.value != 0 &&
                            vehicleChaseTransform != null &&
                            vehicleChaseDistance < config.scaredNpcTrafficDetectRadius;

        bool shouldFlee = hasAggressiveThreat || hasPlayerThreat || hasNpcThreat;

        switch (currentState)
        {
            case CreatureState.Idle:
            case CreatureState.Wandering:
                if (hasAggressiveThreat)
                {
                    chaseTargetTransform = null;
                    if (currentState != CreatureState.Fleeing)
                        SetState(CreatureState.Fleeing);

                    currentFleeSpeed = Mathf.Max(currentFleeSpeed, config.scaredBaseFleeSpeed) *
                                       Mathf.Max(1f, config.scaredAggressiveFleeSpeedMultiplier);
                }
                else if (config.scaredHuntPassiveCreatures)
                {
                    var bug = FindNearestCreature(CreatureBehaviorType.Passive, config.scaredHuntPassiveRadius);
                    if (bug != null)
                    {
                        chaseTargetTransform = bug.transform;
                        SetState(CreatureState.Charging);
                        RunScaredBugHuntCharging(dt, abortForAggressiveThreat: false);
                    }
                    else if (hasPlayerThreat || hasNpcThreat)
                    {
                        chaseTargetTransform = null;
                        if (currentState != CreatureState.Fleeing)
                            SetState(CreatureState.Fleeing);
                    }
                    else
                    {
                        chaseTargetTransform = null;
                        UpdateScaredCalmIdle(dt);
                    }
                }
                else if (shouldFlee)
                {
                    chaseTargetTransform = null;
                    if (currentState != CreatureState.Fleeing)
                        SetState(CreatureState.Fleeing);
                }
                else
                {
                    chaseTargetTransform = null;
                    UpdateScaredCalmIdle(dt);
                }
                break;

            case CreatureState.Fleeing:
                UpdateFleeing(dt);

                bool playerFar = !playerDetected || playerDistance > config.scaredDetectionRadius * 2f;
                bool aggroFar = !threatIsAggressive || threatTransform == null ||
                                threatDistance > config.scaredAggressiveLoseFearDistance;
                bool npcFar = !hasNpcThreat;

                if (playerFar && aggroFar && npcFar && _activeThreatCount == 0)
                {
                    SetState(config.scaredIdleUseBugMovement ? CreatureState.Idle : CreatureState.Wandering);
                    currentFleeSpeed = config.scaredBaseFleeSpeed;
                    chaseTargetTransform = null;
                }
                break;

            case CreatureState.Charging:
                RunScaredBugHuntCharging(dt, abortForAggressiveThreat: hasAggressiveThreat);
                break;
        }
    }

    /// <summary>Critter chasing a bug (Passive). Player/NPC cars do not abort; only an aggressive creature does.</summary>
    private void RunScaredBugHuntCharging(float dt, bool abortForAggressiveThreat)
    {
        if (config == null) return;

        if (abortForAggressiveThreat)
        {
            chaseTargetTransform = null;
            SetState(CreatureState.Fleeing);
            return;
        }

        if (chaseTargetTransform == null ||
            !chaseTargetTransform.TryGetComponent<TrackCreature>(out var prey) ||
            prey.isDead ||
            prey.behaviorType != CreatureBehaviorType.Passive)
        {
            chaseTargetTransform = null;
            SetState(config.scaredIdleUseBugMovement ? CreatureState.Idle : CreatureState.Wandering);
            return;
        }

        float huntDist = Vector3.Distance(transform.position, chaseTargetTransform.position);
        if (huntDist > config.scaredPassiveHuntLoseDistance)
        {
            chaseTargetTransform = null;
            SetState(config.scaredIdleUseBugMovement ? CreatureState.Idle : CreatureState.Wandering);
            return;
        }

        UpdateCharging(dt);
    }

    private void UpdateScaredCalmIdle(float dt)
    {
        if (config.scaredIdleUseBugMovement)
        {
            if (currentState != CreatureState.Idle)
                SetState(CreatureState.Idle);

            UpdateBugIdleMovement(dt, config.scaredIdleBugMoveSpeed,
                config.scaredIdleBugDirectionChangeInterval,
                config.scaredIdleBugLateralRadius, config.scaredIdleBugForwardRadius,
                config.scaredMaxOffRoadDistance);
        }
        else
        {
            if (currentState != CreatureState.Wandering)
                SetState(CreatureState.Wandering);

            UpdateWandering(dt);
        }
    }

    /// <summary>
    /// Aggressive behavior: Idles until player is detected, then does bull rush.
    /// NO continuous chasing - only bull rush attacks.
    /// </summary>
    protected virtual void UpdateAggressiveBehavior(float dt)
    {
        // Update bull rush cooldown (always ticks down)
        if (bullRushCooldownTimer > 0f)
            bullRushCooldownTimer -= dt;

        switch (currentState)
        {
            case CreatureState.Idle:
            case CreatureState.Wandering:

                // Reset bull rush state when idle
                isBullRushCharging = false;
                isBullRushActive = false;
                bullRushChargeTimer = 0f;
                bullRushActiveTimer = 0f;

                bool canStartNewRush = bullRushCooldownTimer <= 0f;
                ResolveAggressiveChargeTarget(out Transform chargeTarget, out _);

                if (canStartNewRush && chargeTarget != null)
                {
                    SetState(CreatureState.Charging);
                    UpdateCharging(dt);
                }
                // Idle bug movement
                else
                {
                    if (config.aggressiveIdleUseBugMovement)
                    {
                        if (currentState != CreatureState.Idle)
                            SetState(CreatureState.Idle);

                        UpdateBugIdleMovement(
                            dt,
                            config.aggressiveIdleBugMoveSpeed,
                            config.aggressiveIdleBugDirectionChangeInterval,
                            config.aggressiveIdleBugLateralRadius,
                            config.aggressiveIdleBugForwardRadius,
                            config.aggressiveMaxOffTrackDistance);
                    }
                    else
                    {
                        if (currentState != CreatureState.Idle)
                            SetState(CreatureState.Idle);
                    }
                }
                break;

            case CreatureState.Charging:
                UpdateCharging(dt);

                if (!isBullRushActive && !isBullRushCharging)
                {
                    ResolveAggressiveChargeTarget(out Transform focus, out _);
                    if (focus == null)
                    {
                        SetState(CreatureState.Idle);
                        break;
                    }

                    float d = Vector3.Distance(transform.position, focus.position);
                    float abandon = focus == playerTransform
                        ? config.aggressiveDetectionRadius * 1.6f
                        : (vehicleChaseTransform != null && focus == vehicleChaseTransform
                            ? config.aggressiveNpcTrafficDetectRadius * 1.35f
                            : config.aggressiveHuntRadius * 1.35f);

                    if (d > abandon)
                        SetState(CreatureState.Idle);
                }
                break;
        }
    }

    #endregion

    #region Movement Behaviors

    protected virtual void UpdateWandering(float dt)
    {
        // Update wander timer and potentially change direction
        wanderTimer += dt;
        if (wanderTimer >= nextWanderChangeTime)
        {
            ResetWanderDirection();
        }


        // Calculate target speed
        float targetSpeed = config.passiveWanderSpeed;
        currentSpeed = Mathf.MoveTowards(currentSpeed, targetSpeed, targetSpeed * 2f * dt);

        // Move along track (forward/backward based on wanderDirectionZ)
        float trackMovement = currentSpeed * wanderDirectionZ * dt;
        currentDistanceAlongTrack += trackMovement;

        // Clamp to track bounds
        float totalLength = spawner.GetTotalLength();
        currentDistanceAlongTrack = Mathf.Clamp(currentDistanceAlongTrack, 0f, totalLength);

        // Update lateral offset
        float halfWidth = GetRoadHalfWidth();
        float maxLateral = halfWidth * 0.8f; // Stay mostly on road
        targetLateralOffset += wanderDirectionX * config.passiveWanderSpeed * 0.5f * dt;
        targetLateralOffset = Mathf.Clamp(targetLateralOffset, -maxLateral, maxLateral);

        // Smooth lateral movement
        currentLateralOffset = Mathf.MoveTowards(currentLateralOffset, targetLateralOffset, config.passiveWanderSpeed * dt);

        if (enableMovementAvoidance && GetAvoidanceLayerMask().value != 0 && avoidanceIdlePathNudgeSpeed > 0f)
            NudgeWanderGoalAroundObstacles(dt, maxLateral);
    }


    /// <summary>
    /// Captures the current position as the center of our idle "bug wiggle".
    /// This is used by Scared creatures while the player is NOT close enough to spook them.
    /// </summary>
    protected void CaptureIdleAnchor()
    {
        idleAnchorDistance = currentDistanceAlongTrack;
        idleAnchorLateral = currentLateralOffset;

        idleCurrentDistOffset = 0f;
        idleCurrentLateralOffset = 0f;

        idleTargetDistOffset = 0f;
        idleTargetLateralOffset = 0f;

        wanderTimer = 0f;
        nextIdleChangeTime = 0f; // force immediate target pick
    }

    /// <summary>
    /// Very small, low-speed wandering used as "idle bug" movement.
    /// - Lets the creature drift off the road (within extraOffRoad).
    /// - Intended for Scared creatures BEFORE they detect the player.
    /// </summary>
    /// <summary>Walk / pause cycle for bug-style idle on all creature tiers.</summary>
    protected bool IdleRhythmAllowsWalking()
    {
        if (config == null || !config.idleUseWalkPauseRhythm)
            return true;

        if (_idleRhythmPhaseEndTime <= 0f)
        {
            _idleRhythmWalking = true;
            _idleRhythmPhaseEndTime = Time.time + Random.Range(config.idleWalkSegmentMinSec, config.idleWalkSegmentMaxSec);
        }

        if (Time.time >= _idleRhythmPhaseEndTime)
        {
            _idleRhythmWalking = !_idleRhythmWalking;
            float dur = _idleRhythmWalking
                ? Random.Range(config.idleWalkSegmentMinSec, config.idleWalkSegmentMaxSec)
                : Random.Range(config.idlePauseMinSec, config.idlePauseMaxSec);
            _idleRhythmPhaseEndTime = Time.time + Mathf.Max(0.05f, dur);
        }

        return _idleRhythmWalking;
    }

    protected virtual void UpdateBugIdleMovement(
        float dt,
        float bugSpeed,
        float directionChangeInterval,
        float lateralRadius,
        float forwardRadius,
        float extraOffRoad)
    {
        if (spawner == null) return;

        if (!IdleRhythmAllowsWalking())
        {
            currentSpeed = Mathf.MoveTowards(currentSpeed, 0f, Mathf.Max(0.01f, bugSpeed) * 4f * dt);
            return;
        }

        // If we haven't anchored yet (e.g., config toggled at runtime), anchor now.
        if (nextIdleChangeTime <= 0f)
        {
            CaptureIdleAnchor();
        }

        wanderTimer += dt;

        // Pick a new tiny target offset occasionally
        if (wanderTimer >= nextIdleChangeTime)
        {
            float interval = Mathf.Max(0.05f, directionChangeInterval) * Random.Range(0.85f, 1.15f);
            nextIdleChangeTime = wanderTimer + interval;

            idleTargetLateralOffset = Random.Range(-Mathf.Abs(lateralRadius), Mathf.Abs(lateralRadius));
            idleTargetDistOffset = Random.Range(-Mathf.Abs(forwardRadius), Mathf.Abs(forwardRadius));
        }

        // Smooth speed toward bugSpeed
        currentSpeed = Mathf.MoveTowards(currentSpeed, bugSpeed, Mathf.Max(0.01f, bugSpeed) * 3f * dt);

        // Move our current offsets toward the target offsets
        float step = Mathf.Max(0.01f, currentSpeed) * dt;
        idleCurrentLateralOffset = Mathf.MoveTowards(idleCurrentLateralOffset, idleTargetLateralOffset, step);
        idleCurrentDistOffset = Mathf.MoveTowards(idleCurrentDistOffset, idleTargetDistOffset, step);

        // Apply offsets around anchor
        float totalLength = spawner.GetTotalLength();
        currentDistanceAlongTrack = Mathf.Clamp(idleAnchorDistance + idleCurrentDistOffset, 0f, totalLength);

        float halfWidth = GetRoadHalfWidth();
        float maxLateral = halfWidth + Mathf.Max(0f, extraOffRoad);
        targetLateralOffset = Mathf.Clamp(idleAnchorLateral + idleCurrentLateralOffset, -maxLateral, maxLateral);

        if (enableMovementAvoidance && GetAvoidanceLayerMask().value != 0 && avoidanceIdlePathNudgeSpeed > 0f)
            NudgeIdleGoalAroundObstacles(dt, maxLateral);

        // Smooth lateral
        currentLateralOffset = Mathf.MoveTowards(currentLateralOffset, targetLateralOffset, Mathf.Max(0.01f, currentSpeed) * dt);
    }

    /// <summary>
    /// If the spline goal is blocked, shift idle lateral targets toward the clearest fan direction (bugs/critters).
    /// </summary>
    private void NudgeIdleGoalAroundObstacles(float dt, float maxLateralAbs)
    {
        if (spawner == null) return;

        spawner.SamplePath(currentDistanceAlongTrack, out Vector3 pathPos, out Vector3 pathForward);
        Vector3 flatF = pathForward;
        flatF.y = 0f;
        if (flatF.sqrMagnitude < 1e-6f) return;
        flatF.Normalize();
        Vector3 pathRight = Vector3.Cross(Vector3.up, flatF).normalized;

        Vector3 goal = pathPos + pathRight * targetLateralOffset;
        goal.y = transform.position.y;
        Vector3 toGoal = goal - transform.position;
        toGoal.y = 0f;
        float dist = toGoal.magnitude;
        if (dist < 0.05f) return;

        Vector3 wantDir = toGoal / dist;
        Vector3 origin = transform.position + Vector3.up * avoidanceCastHeight;
        float look = Mathf.Max(avoidanceLookAhead * 1.15f, dist + 0.5f);

        float forwardClear = SampleObstacleClearance(origin, wantDir, look);
        if (forwardClear >= look * 0.55f)
            return;

        TryPickBestClearDirection(origin, wantDir, look, out Vector3 bestDir, out float bestClear);
        if (bestClear <= forwardClear + 0.08f)
            return;

        float side = Vector3.Dot(bestDir, pathRight);
        if (Mathf.Abs(side) < 0.12f)
            return;

        float nudge = Mathf.Sign(side) * avoidanceIdlePathNudgeSpeed * dt;
        idleTargetLateralOffset += nudge;
        idleCurrentLateralOffset = Mathf.MoveTowards(idleCurrentLateralOffset, idleTargetLateralOffset, Mathf.Abs(nudge) * 2.5f);

        float anchorLat = idleAnchorLateral + idleCurrentLateralOffset;
        anchorLat = Mathf.Clamp(anchorLat, -maxLateralAbs, maxLateralAbs);
        idleCurrentLateralOffset = anchorLat - idleAnchorLateral;
        idleTargetLateralOffset = Mathf.Clamp(idleTargetLateralOffset,
            idleCurrentLateralOffset - 1.5f,
            idleCurrentLateralOffset + 1.5f);

        targetLateralOffset = Mathf.Clamp(idleAnchorLateral + idleCurrentLateralOffset, -maxLateralAbs, maxLateralAbs);
    }

    private void NudgeWanderGoalAroundObstacles(float dt, float maxLateralAbs)
    {
        if (spawner == null) return;

        spawner.SamplePath(currentDistanceAlongTrack, out Vector3 pathPos, out Vector3 pathForward);
        Vector3 flatF = pathForward;
        flatF.y = 0f;
        if (flatF.sqrMagnitude < 1e-6f) return;
        flatF.Normalize();
        Vector3 pathRight = Vector3.Cross(Vector3.up, flatF).normalized;

        Vector3 goal = pathPos + pathRight * targetLateralOffset;
        goal.y = transform.position.y;
        Vector3 toGoal = goal - transform.position;
        toGoal.y = 0f;
        float dist = toGoal.magnitude;
        if (dist < 0.05f) return;

        Vector3 wantDir = toGoal / dist;
        Vector3 origin = transform.position + Vector3.up * avoidanceCastHeight;
        float look = Mathf.Max(avoidanceLookAhead * 1.15f, dist + 0.5f);

        float forwardClear = SampleObstacleClearance(origin, wantDir, look);
        if (forwardClear >= look * 0.55f)
            return;

        TryPickBestClearDirection(origin, wantDir, look, out Vector3 bestDir, out float bestClear);
        if (bestClear <= forwardClear + 0.08f)
            return;

        float side = Vector3.Dot(bestDir, pathRight);
        if (Mathf.Abs(side) < 0.12f)
            return;

        targetLateralOffset += Mathf.Sign(side) * avoidanceIdlePathNudgeSpeed * dt;
        targetLateralOffset = Mathf.Clamp(targetLateralOffset, -maxLateralAbs, maxLateralAbs);
    }


    protected virtual void UpdateFleeing(float dt)
    {
        float fleeMax = behaviorType == CreatureBehaviorType.Passive
            ? config.passiveFleeMaxSpeed
            : config.scaredMaxFleeSpeed;
        float fleeBuild = behaviorType == CreatureBehaviorType.Passive
            ? config.passiveFleeBuildupRate
            : config.scaredSpeedBuildupRate;

        currentFleeSpeed = Mathf.MoveTowards(currentFleeSpeed, fleeMax, fleeBuild * dt);
        currentSpeed = currentFleeSpeed;

        // Path basis for track + lateral (needed before we apply avoidance)
        spawner.SamplePath(currentDistanceAlongTrack, out Vector3 pathPos, out Vector3 pathForward);
        Vector3 flatForward = pathForward;
        flatForward.y = 0f;
        flatForward.Normalize();

        Vector3 right = Vector3.Cross(Vector3.up, flatForward).normalized;

        // Flee direction (away from player/threats), then steer around obstacles so we don't run into them
        Vector3 fleeDirection = GetFleeDirection();
        if (enableMovementAvoidance && GetAvoidanceLayerMask().value != 0 && fleeDirection.sqrMagnitude > 0.0001f)
        {
            float step = currentSpeed * dt;
            fleeDirection = ApplyAvoidanceToMoveDir(fleeDirection, step, flatForward, right);
        }

        // Convert flee direction to track movement
        float forwardDot = Vector3.Dot(fleeDirection, flatForward);
        float trackDirection = forwardDot >= 0 ? 1f : -1f;

        // Move along track
        float trackMovement = currentSpeed * trackDirection * dt;
        currentDistanceAlongTrack += trackMovement;

        // Clamp to track bounds
        float totalLength = spawner.GetTotalLength();
        currentDistanceAlongTrack = Mathf.Clamp(currentDistanceAlongTrack, 0f, totalLength);

        // Lateral movement - run away from player laterally
        float lateralDot = Vector3.Dot(fleeDirection, right);
        float lateralMovement = currentSpeed * lateralDot * dt;

        float maxOffRoad = behaviorType == CreatureBehaviorType.Passive
            ? config.passiveFleeMaxOffRoadDistance
            : config.scaredMaxOffRoadDistance;
        float halfWidth = GetRoadHalfWidth();
        float maxLateral = halfWidth + maxOffRoad;

        targetLateralOffset += lateralMovement;
        targetLateralOffset = Mathf.Clamp(targetLateralOffset, -maxLateral, maxLateral);

        currentLateralOffset = Mathf.MoveTowards(currentLateralOffset, targetLateralOffset, currentSpeed * dt);
    }

    protected virtual void UpdateCharging(float dt)
    {
        if (spawner == null || config == null) return;

        if (behaviorType == CreatureBehaviorType.Scared)
        {
            if (chaseTargetTransform == null) return;
            UpdateStandardCharging(dt, chaseTargetTransform, huntingCreature: true);
            return;
        }

        if (behaviorType != CreatureBehaviorType.Aggressive)
            return;

        ResolveAggressiveChargeTarget(out Transform target, out bool huntingCreature);

        if (target == null)
        {
            EndBullRush();
            SetState(CreatureState.Idle);
            currentSpeed = 0f;
            return;
        }

        if (chaseTargetTransform != null &&
            chaseTargetTransform.TryGetComponent<TrackCreature>(out var tc) && tc.isDead)
        {
            chaseTargetTransform = null;
            EndBullRush();
            SetState(CreatureState.Idle);
            currentSpeed = 0f;
            return;
        }

        bool useBullRush = config.useBullRush && !huntingCreature;
        if (useBullRush)
            UpdateBullRush(dt, target);
        else
            UpdateStandardCharging(dt, target, huntingCreature);
    }

    /// <summary>
    /// Pick closest valid target: player (priority bubble first), else among player / critter / NPC in range.
    /// </summary>
    private void ResolveAggressiveChargeTarget(out Transform target, out bool huntingCreature)
    {
        target = null;
        huntingCreature = false;

        if (config == null) return;

        if (playerTransform != null && playerDetected &&
            playerDistance <= Mathf.Max(0f, aggressivePlayerPriorityRadius))
        {
            target = playerTransform;
            huntingCreature = false;
            return;
        }

        float bestD = float.MaxValue;
        Transform best = null;

        if (playerTransform != null && playerDetected && playerDistance <= config.aggressiveDetectionRadius &&
            playerDistance < bestD)
        {
            bestD = playerDistance;
            best = playerTransform;
        }

        if (chaseTargetTransform != null)
        {
            float d = Vector3.Distance(transform.position, chaseTargetTransform.position);
            if (d <= config.aggressiveHuntRadius && d < bestD)
            {
                bestD = d;
                best = chaseTargetTransform;
            }
        }

        if (vehicleChaseTransform != null && config.aggressiveHuntNpcTraffic && _npcTrafficLayers.value != 0)
        {
            if (vehicleChaseDistance <= config.aggressiveNpcTrafficDetectRadius &&
                vehicleChaseDistance < bestD)
            {
                bestD = vehicleChaseDistance;
                best = vehicleChaseTransform;
            }
        }

        target = best;
        huntingCreature = target != null && target != playerTransform;
    }

    /// <summary>
    /// Bull rush mechanic: Charge up -> Rush in locked direction -> Return to Idle
    /// NO chasing - only bull rush attacks against the player.
    /// </summary>
    protected virtual void UpdateBullRush(float dt, Transform target)
    {
        // ===== PHASE 1: CHARGE UP (wind up, rotate toward target, don't move) =====
        if (!isBullRushActive && !isBullRushCharging && bullRushCooldownTimer <= 0f)
        {
            // Start charging up
            isBullRushCharging = true;
            bullRushChargeTimer = 0f;
            currentSpeed = 0f;
            Debug.Log("[TrackCreature] Bull rush CHARGE UP started!");
        }

        if (isBullRushCharging)
        {
            bullRushChargeTimer += dt;
            currentSpeed = 0f; // Stay completely still during charge up

            // Keep rotating toward target during charge up
            Vector3 toTarget = target.position - transform.position;
            toTarget.y = 0f;

            if (toTarget.sqrMagnitude > 0.01f)
            {
                bullRushDirection = toTarget.normalized;
            }

            // Check if charge up complete
            if (bullRushChargeTimer >= config.bullRushChargeUpDuration)
            {
                // Lock in the rush direction and start rushing
                isBullRushCharging = false;
                isBullRushActive = true;
                bullRushActiveTimer = 0f;
                _bullRushLaunchStartWorld = transform.position;
                Vector3 toTargetAtLaunch = target.position - transform.position;
                toTargetAtLaunch.y = 0f;
                _bullRushLaunchDirectDistance = toTargetAtLaunch.magnitude;

                // Lock the direction at the moment of release
                Vector3 toTargetFinal = target.position - transform.position;
                toTargetFinal.y = 0f;
                if (toTargetFinal.sqrMagnitude > 0.01f)
                {
                    bullRushDirection = toTargetFinal.normalized;
                }

                Debug.Log("[TrackCreature] Bull rush LAUNCHED!");
            }

            return; // Don't move during charge up
        }

        // ===== PHASE 2: ACTIVE RUSH (move fast in locked direction with slight steering) =====
        if (isBullRushActive)
        {
            bullRushActiveTimer += dt;

            // Rush speed
            float rushSpeed = config.aggressiveChargeSpeed * config.bullRushSpeedMultiplier;
            currentSpeed = rushSpeed;

            // Allow SLIGHT steering toward target (the "bend")
            Vector3 toTargetNow = target.position - transform.position;
            toTargetNow.y = 0f;

            if (toTargetNow.sqrMagnitude > 0.01f)
            {
                Vector3 desiredDir = toTargetNow.normalized;

                // Calculate max rotation this frame based on steer rate
                float maxAngleThisFrame = config.bullRushMaxSteerRate * dt;

                // Smoothly rotate rush direction toward target (limited steering)
                float angleBetween = Vector3.SignedAngle(bullRushDirection, desiredDir, Vector3.up);
                float steerAngle = Mathf.Clamp(angleBetween, -maxAngleThisFrame, maxAngleThisFrame);

                bullRushDirection = Quaternion.Euler(0f, steerAngle, 0f) * bullRushDirection;
                bullRushDirection.Normalize();
            }

            // World-space motion runs in UpdateMovement along bullRushDirection (not spline projection).
            // ===== CHECK END CONDITIONS =====
            bool rushTimedOut = bullRushActiveTimer >= config.bullRushDuration;

            Vector3 horizFromLaunch = transform.position - _bullRushLaunchStartWorld;
            horizFromLaunch.y = 0f;
            float traveledFromLaunch = horizFromLaunch.magnitude;
            bool overshot = traveledFromLaunch > _bullRushLaunchDirectDistance + config.bullRushOvershootDistance;


            if (rushTimedOut || overshot)
            {
                Debug.Log($"[TrackCreature] Bull rush ENDED. Timeout={rushTimedOut}, Overshot={overshot}");
                EndBullRush();

                // GO BACK TO IDLE - no chasing!
                SetState(CreatureState.Idle);
            }

            return;
        }

        // ===== PHASE 3: COOLDOWN - STAY IDLE, NO MOVEMENT =====
        // During cooldown, creature stays still (handled by Idle state)
        // This phase only exists if we're somehow still in Charging state during cooldown
        if (bullRushCooldownTimer > 0f)
        {
            currentSpeed = 0f; // Stay still during cooldown
            // Don't chase - just wait
        }
    }

    /// <summary>
    /// End the bull rush and start cooldown.
    /// </summary>
    protected void EndBullRush()
    {
        isBullRushCharging = false;
        isBullRushActive = false;
        bullRushChargeTimer = 0f;
        bullRushActiveTimer = 0f;
        bullRushCooldownTimer = config.bullRushCooldown;
        currentSpeed = 0f;
    }

    /// <summary>
    /// Creates and configures the LineRenderer for bull rush telegraph.
    /// </summary>
    protected void SetupBullRushLineRenderer()
    {
        if (bullRushLineRenderer != null) return; // Already setup
        if (config == null || !config.bullRushShowTelegraph) return;

        // Create a child GameObject for the line
        var lineObj = new GameObject("BullRushTelegraph");
        lineObj.transform.SetParent(transform);
        lineObj.transform.localPosition = Vector3.zero;
        lineObj.transform.localRotation = Quaternion.identity;

        bullRushLineRenderer = lineObj.AddComponent<LineRenderer>();
        bullRushLineRenderer.useWorldSpace = true;
        bullRushLineRenderer.positionCount = 2;
        bullRushLineRenderer.startWidth = config.bullRushLineWidth;
        bullRushLineRenderer.endWidth = config.bullRushLineWidth * 0.5f; // Taper at end

        // Create a simple material
        bullRushLineRenderer.material = new Material(Shader.Find("Sprites/Default"));
        bullRushLineRenderer.material.color = config.bullRushLineColorCharging;

        // Start hidden
        bullRushLineRenderer.enabled = false;
        bullRushLineAlpha = 0f;
    }

    /// <summary>
    /// Updates the bull rush telegraph line position and appearance.
    /// </summary>
    protected void UpdateBullRushTelegraph(float dt)
    {
        if (config == null || !config.bullRushShowTelegraph) return;
        if (bullRushLineRenderer == null)
        {
            SetupBullRushLineRenderer();
            if (bullRushLineRenderer == null) return;
        }

        bool shouldShow = isBullRushCharging || isBullRushActive;

        if (shouldShow)
        {
            bullRushLineFadingOut = false;

            // Fade in
            bullRushLineAlpha = Mathf.MoveTowards(bullRushLineAlpha, 1f, dt * 5f);

            // Enable the line
            bullRushLineRenderer.enabled = true;

            // Calculate line positions
            Vector3 startPos = transform.position;
            startPos.y += config.bullRushLineYOffset;

            // Direction: use bullRushDirection if available, otherwise forward
            Vector3 lineDir = bullRushDirection;
            if (lineDir.sqrMagnitude < 0.01f)
            {
                lineDir = transform.forward;
            }
            lineDir.y = 0f;
            lineDir.Normalize();

            Vector3 endPos = startPos + lineDir * config.bullRushLineLength;
            endPos.y = startPos.y; // Keep same height

            // Set positions
            bullRushLineRenderer.SetPosition(0, startPos);
            bullRushLineRenderer.SetPosition(1, endPos);

            // Set color based on phase
            Color lineColor = isBullRushActive ? config.bullRushLineColorRushing : config.bullRushLineColorCharging;
            lineColor.a *= bullRushLineAlpha;

            bullRushLineRenderer.startColor = lineColor;
            bullRushLineRenderer.endColor = new Color(lineColor.r, lineColor.g, lineColor.b, lineColor.a * 0.3f); // Fade at end
        }
        else if (bullRushLineAlpha > 0f || bullRushLineFadingOut)
        {
            // Fade out
            bullRushLineFadingOut = true;
            float fadeSpeed = config.bullRushLineFadeOutTime > 0f ? (1f / config.bullRushLineFadeOutTime) : 10f;
            bullRushLineAlpha = Mathf.MoveTowards(bullRushLineAlpha, 0f, dt * fadeSpeed);

            if (bullRushLineAlpha <= 0f)
            {
                bullRushLineRenderer.enabled = false;
                bullRushLineFadingOut = false;
            }
            else
            {
                // Update alpha during fade
                Color lineColor = bullRushLineRenderer.startColor;
                lineColor.a = config.bullRushLineColorCharging.a * bullRushLineAlpha;
                bullRushLineRenderer.startColor = lineColor;
                bullRushLineRenderer.endColor = new Color(lineColor.r, lineColor.g, lineColor.b, lineColor.a * 0.3f);
            }
        }
    }

    /// <summary>
    /// Immediately hides the bull rush telegraph line.
    /// </summary>
    protected void HideBullRushTelegraph()
    {
        if (bullRushLineRenderer != null)
        {
            bullRushLineRenderer.enabled = false;
            bullRushLineAlpha = 0f;
            bullRushLineFadingOut = false;
        }
    }

    /// <summary>
    /// Standard charging behavior (used when bull rush is disabled or for hunting creatures).
    /// </summary>
    protected virtual void UpdateStandardCharging(float dt, Transform target, bool huntingCreature)
    {
        float baseCharge = behaviorType == CreatureBehaviorType.Scared
            ? config.scaredBugHuntSpeed * Mathf.Max(0.01f, config.scaredBugHuntSpeedMultiplier)
            : config.aggressiveChargeSpeed;

        float speedMult = behaviorType == CreatureBehaviorType.Aggressive && huntingCreature
            ? Mathf.Max(0.01f, config.aggressiveHuntSpeedMultiplier)
            : 1f;

        currentSpeed = Mathf.Max(0f, baseCharge) * speedMult;

        // Move in "track space" toward the target's distance-along-track
        float targetDistAlong = spawner.GetDistanceAlongPath(target.position);

        float moveStep = currentSpeed * dt;
        currentDistanceAlongTrack = Mathf.MoveTowards(currentDistanceAlongTrack, targetDistAlong, moveStep);

        // Update lateral intercept based on the path frame at our current distance.
        spawner.SamplePath(currentDistanceAlongTrack, out Vector3 pathPos, out Vector3 pathForward);

        Vector3 flatForward = pathForward;
        flatForward.y = 0f;
        if (flatForward.sqrMagnitude < 0.0001f) flatForward = Vector3.forward;
        flatForward.Normalize();

        Vector3 right = Vector3.Cross(Vector3.up, flatForward).normalized;

        float desiredLateral = Vector3.Dot(target.position - pathPos, right);

        float maxOffTrack = Mathf.Max(0f, config.aggressiveMaxOffTrackDistance);
        float halfWidth = GetRoadHalfWidth();
        float maxLateral = halfWidth + maxOffTrack;

        desiredLateral = Mathf.Clamp(desiredLateral, -maxLateral, maxLateral);

        // Smooth lateral steering
        float lateralStep = moveStep * 1.5f;
        targetLateralOffset = Mathf.MoveTowards(targetLateralOffset, desiredLateral, lateralStep);
        currentLateralOffset = Mathf.MoveTowards(currentLateralOffset, targetLateralOffset, lateralStep);
    }

    #endregion

    #region Movement Core

    protected virtual void UpdateMovement(float dt)
    {
        if (spawner == null) return;

        // Sample path at current distance (spline frame for normal motion; bull rush uses world XZ below)
        spawner.SamplePath(currentDistanceAlongTrack, out Vector3 pathPos, out Vector3 pathForward);

        Vector3 flatForward = pathForward;
        flatForward.y = 0f;
        if (flatForward.sqrMagnitude < 0.0001f)
            flatForward = Vector3.forward;
        flatForward.Normalize();

        Vector3 right = Vector3.Cross(Vector3.up, flatForward).normalized;

        Vector3 targetPos = pathPos + right * currentLateralOffset;
        targetPos.y = transform.position.y;

        Vector3 prevPos = transform.position;
        float moveSpeed = Mathf.Max(currentSpeed, 0f);
        float step = moveSpeed * dt;

        Vector3 moveDir;
        float desiredDist;

        // Active bull rush: straight planar charge along bullRushDirection (homeward steer only, no spline pull / no obstacle weave).
        if (isBullRushActive && bullRushDirection.sqrMagnitude > 0.01f)
        {
            moveDir = bullRushDirection;
            moveDir.y = 0f;
            moveDir.Normalize();
            desiredDist = step;
        }
        else
        {
            Vector3 desired = targetPos - transform.position;
            desired.y = 0f;
            desiredDist = desired.magnitude;
            moveDir = desiredDist > 0.0001f ? (desired / desiredDist) : Vector3.zero;
        }

        // No ApplyAvoidanceToMoveDir during bull rush (committed line); other states use normal avoidance steering.
        bool skipAvoidanceSteer = isBullRushActive || isBullRushCharging;

        if (enableMovementAvoidance && movementAvoidanceLayers.value != 0 && moveDir.sqrMagnitude > 0.0001f && !skipAvoidanceSteer)
        {
            moveDir = ApplyAvoidanceToMoveDir(moveDir, step, flatForward, right);
        }

        Vector3 shoveDir = moveDir.sqrMagnitude > 1e-6f ? moveDir : flatForward;
        shoveDir.y = 0f;
        if (shoveDir.sqrMagnitude > 1e-6f) shoveDir.Normalize();
        else shoveDir = flatForward;

        Vector3 newPos = transform.position + moveDir * Mathf.Min(step, desiredDist);
        newPos.y = transform.position.y;

        bool obstacleClampBlocked = false;
        RaycastHit obstacleClampHit = default;
        if (enableMovementAvoidance && GetAvoidanceLayerMask().value != 0)
            obstacleClampBlocked = ClampHorizontalMoveToObstacles(prevPos, ref newPos, out obstacleClampHit);

        transform.position = newPos;

        if (isBullRushActive && obstacleClampBlocked && config != null && config.bullRushObstaclePushEnabled)
            TryBullRushDisplaceObstacle(obstacleClampHit, shoveDir, currentSpeed);

        Vector3 delta = transform.position - prevPos;
        currentVelocity = delta / Mathf.Max(dt, 0.001f);

        if (!isBullRushActive && !isBullRushCharging)
            SyncTrackStateFromTransform();
    }

    /// <summary>
    /// Reconcile spline coordinates with the world position after movement (reduces obstacle jitter).
    /// </summary>
    protected void SyncTrackStateFromTransform()
    {
        if (spawner == null) return;

        float d = spawner.GetDistanceAlongPath(transform.position);
        d = Mathf.Clamp(d, 0f, spawner.GetTotalLength());
        currentDistanceAlongTrack = d;

        spawner.SamplePath(currentDistanceAlongTrack, out Vector3 pathPos, out Vector3 pathForward);
        Vector3 flatForward = pathForward;
        flatForward.y = 0f;
        if (flatForward.sqrMagnitude < 1e-6f) return;
        flatForward.Normalize();
        Vector3 right = Vector3.Cross(Vector3.up, flatForward).normalized;
        Vector3 toCreature = transform.position - pathPos;
        toCreature.y = 0f;
        currentLateralOffset = Vector3.Dot(toCreature, right);
        targetLateralOffset = currentLateralOffset;
    }

    private QueryTriggerInteraction AvoidanceTriggerQuery =>
        avoidanceIncludeTriggers ? QueryTriggerInteraction.Collide : QueryTriggerInteraction.Ignore;

    private bool IsOwnObstacleCollider(Collider c)
    {
        if (c == null) return true;
        return c.transform == transform || c.transform.IsChildOf(transform);
    }

    /// <summary>
    /// SphereCast that reports the first hit not on this creature (needed when Include Triggers is on).
    /// </summary>
    private bool ObstacleSphereCast(Vector3 origin, float sphereRadius, Vector3 direction, out RaycastHit blockingHit, float maxDistance)
    {
        blockingHit = default;
        direction.y = 0f;
        if (direction.sqrMagnitude < 1e-8f) return false;
        direction.Normalize();

        var q = AvoidanceTriggerQuery;
        Vector3 cursor = origin;
        float remaining = maxDistance;
        const int maxIterations = 20;

        for (int iter = 0; iter < maxIterations && remaining > 0.0005f; iter++)
        {
            if (!Physics.SphereCast(cursor, sphereRadius, direction, out RaycastHit hit, remaining, GetAvoidanceLayerMask(), q))
                return false;

            if (IsOwnObstacleCollider(hit.collider))
            {
                float skip = Mathf.Max(hit.distance, 0.02f) + sphereRadius * 0.2f;
                if (skip >= remaining - 1e-4f)
                    return false;
                cursor += direction * skip;
                remaining -= skip;
                continue;
            }

            blockingHit = hit;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Hard clamp so kinematic movement cannot end inside or past a collider this frame.
    /// </summary>
    /// <returns>True if movement was shortened and <paramref name="blockingHit"/> is the obstacle along the move segment.</returns>
    private bool ClampHorizontalMoveToObstacles(Vector3 prevWorld, ref Vector3 newWorld, out RaycastHit blockingHit)
    {
        blockingHit = default;

        Vector3 o0 = prevWorld + Vector3.up * avoidanceCastHeight;
        Vector3 o1 = newWorld + Vector3.up * avoidanceCastHeight;
        Vector3 delta = o1 - o0;
        delta.y = 0f;
        float dist = delta.magnitude;
        if (dist < 1e-5f) return false;

        Vector3 dir = delta / dist;
        float r = Mathf.Max(0.06f, avoidanceRadius * 0.9f);
        float maxCast = dist + r * 0.35f;

        if (!ObstacleSphereCast(o0, r, dir, out RaycastHit hit, maxCast))
            return false;

        float skin = Mathf.Max(0.02f, r * 0.25f);
        float allowed = Mathf.Max(0f, hit.distance - skin);
        if (allowed >= dist - 0.0005f)
            return false;

        newWorld = prevWorld + dir * Mathf.Min(dist, allowed);
        newWorld.y = prevWorld.y;
        blockingHit = hit;
        return true;
    }

    /// <summary>
    /// During bull rush, impart planar velocity to dynamic obstacles we run into (no tunneling — clamp runs every frame).
    /// </summary>
    private void TryBullRushDisplaceObstacle(in RaycastHit hit, Vector3 planarShoveDir, float rushSpeed)
    {
        if (!isBullRushActive || config == null || !config.bullRushObstaclePushEnabled)
            return;
        if (hit.collider == null) return;
        if (IsPlayerCollider(hit.collider)) return;
        if (hit.collider.GetComponentInParent<NPCTrafficCar>() != null) return;

        Rigidbody obstacleRb = hit.collider.attachedRigidbody;
        if (obstacleRb == null)
            obstacleRb = hit.collider.GetComponentInParent<Rigidbody>();
        if (obstacleRb == null || obstacleRb.isKinematic) return;
        if (obstacleRb.transform == transform || obstacleRb.transform.IsChildOf(transform)) return;

        if (config.bullRushObstacleMaxPushMass > 0f && obstacleRb.mass > config.bullRushObstacleMaxPushMass)
            return;

        planarShoveDir.y = 0f;
        if (planarShoveDir.sqrMagnitude < 1e-6f)
        {
            planarShoveDir = bullRushDirection.sqrMagnitude > 0.01f ? bullRushDirection : transform.forward;
            planarShoveDir.y = 0f;
        }
        if (planarShoveDir.sqrMagnitude < 1e-6f) return;
        planarShoveDir.Normalize();

        float dv = config.bullRushObstaclePushVelocityChange + Mathf.Max(0f, rushSpeed) * config.bullRushObstaclePushSpeedScale;
        dv = Mathf.Min(dv, config.bullRushObstaclePushMaxVelocityChange);
        if (dv <= 0f) return;

        obstacleRb.AddForce(planarShoveDir * dv, ForceMode.VelocityChange);
    }

    private float SampleObstacleClearance(Vector3 origin, Vector3 dir, float look)
    {
        dir.y = 0f;
        if (dir.sqrMagnitude < 1e-6f) return 0f;
        dir.Normalize();

        float r = Mathf.Max(0.05f, avoidanceRadius * 0.5f);
        float castLen = Mathf.Max(0.1f, look - r * 0.45f);
        if (ObstacleSphereCast(origin, r, dir, out RaycastHit hit, castLen))
            return Mathf.Max(0f, hit.distance);

        return look;
    }

    private void TryPickBestClearDirection(Vector3 origin, Vector3 forwardHint, float look, out Vector3 bestDir, out float bestClear)
    {
        bestDir = forwardHint;
        bestClear = -1f;

        forwardHint.y = 0f;
        if (forwardHint.sqrMagnitude < 1e-6f)
        {
            forwardHint = transform.forward;
            forwardHint.y = 0f;
        }
        forwardHint.Normalize();

        int count = Mathf.Clamp(avoidanceRayCount, 3, 21);
        float halfFan = avoidanceFanAngleDeg * 0.5f;
        float step = count > 1 ? avoidanceFanAngleDeg / (count - 1) : 0f;

        for (int i = 0; i < count; i++)
        {
            float ang = -halfFan + step * i;
            Vector3 rayDir = Quaternion.AngleAxis(ang, Vector3.up) * forwardHint;
            rayDir.y = 0f;
            if (rayDir.sqrMagnitude < 1e-6f) continue;
            rayDir.Normalize();

            float clear = SampleObstacleClearance(origin, rayDir, look);
            if (clear > bestClear)
            {
                bestClear = clear;
                bestDir = rayDir;
            }
        }
    }

    private Vector3 ApplyAvoidanceToMoveDir(Vector3 moveDir, float step, Vector3 flatForward, Vector3 right)
    {
        moveDir.y = 0f;
        if (moveDir.sqrMagnitude < 1e-6f) return moveDir;
        moveDir.Normalize();

        Vector3 origin = transform.position + Vector3.up * avoidanceCastHeight;
        float look = Mathf.Max(avoidanceLookAhead, step * 2.5f);

        float forwardClear = SampleObstacleClearance(origin, moveDir, look);
        TryPickBestClearDirection(origin, moveDir, look, out Vector3 bestDir, out float bestClear);

        const float blockedRatio = 0.48f;
        if (forwardClear >= look * blockedRatio && forwardClear >= bestClear - 0.12f)
        {
            Vector3 from = _avoidanceSteerSmoothed.sqrMagnitude > 0.01f ? _avoidanceSteerSmoothed : moveDir;
            _avoidanceSteerSmoothed = Vector3.Slerp(from, moveDir, Mathf.Clamp01(avoidanceSteerSmoothSpeed * Time.deltaTime));
            return _avoidanceSteerSmoothed.normalized;
        }

        Vector3 targetDir = bestClear > forwardClear + 0.02f
            ? Vector3.Slerp(moveDir, bestDir, 0.78f).normalized
            : Vector3.Slerp(moveDir, bestDir, 0.42f).normalized;

        Vector3 baseFrom = _avoidanceSteerSmoothed.sqrMagnitude > 0.01f ? _avoidanceSteerSmoothed : moveDir;
        _avoidanceSteerSmoothed = Vector3.Slerp(baseFrom, targetDir, Mathf.Clamp01(avoidanceSteerSmoothSpeed * Time.deltaTime));
        return _avoidanceSteerSmoothed.sqrMagnitude > 1e-6f ? _avoidanceSteerSmoothed.normalized : targetDir;
    }

    protected virtual void UpdateGroundSnap(float dt)
    {
        Vector3 rayOrigin = transform.position + Vector3.up * groundRayHeight;

        if (Physics.Raycast(rayOrigin, Vector3.down, out RaycastHit hit, groundRayHeight + groundRayDistance, groundLayer, QueryTriggerInteraction.Ignore))
        {
            isGrounded = true;
            groundNormal = hit.normal;
            currentGroundY = hit.point.y + groundOffset;

            // Smoothly snap to ground
            Vector3 pos = transform.position;
            pos.y = Mathf.Lerp(pos.y, currentGroundY, groundSnapSpeed * dt);
            transform.position = pos;
        }
        else
        {
            isGrounded = false;
            // Apply simple gravity if not grounded
            Vector3 pos = transform.position;
            pos.y -= 9.8f * dt;
            transform.position = pos;
        }
    }

    protected virtual void UpdateRotation(float dt)
    {
        // Determine facing direction based on state
        Vector3 lookDirection = Vector3.zero;

        Vector3 moveDir = currentVelocity;
        moveDir.y = 0f;

        if (moveDir.sqrMagnitude > 0.0004f) // ~0.02 m/s threshold
        {
            moveDir.Normalize();

            Quaternion TargetRot;
            if (alignToGround && isGrounded)
            {
                Vector3 forward = Vector3.ProjectOnPlane(moveDir, groundNormal).normalized;
                TargetRot = (forward.sqrMagnitude > 0.01f)
                    ? Quaternion.LookRotation(forward, groundNormal)
                    : Quaternion.LookRotation(moveDir, Vector3.up);
            }
            else
            {
                TargetRot = Quaternion.LookRotation(moveDir, Vector3.up);
            }

            Transform RotTarget = visualRoot != null ? visualRoot : transform;
            RotTarget.rotation = Quaternion.Slerp(RotTarget.rotation, TargetRot, rotationSpeed * dt);
            return; // important: don't override with "look at player" logic
        }

        switch (currentState)
        {
            case CreatureState.Wandering:
                // Face movement direction
                spawner.SamplePath(currentDistanceAlongTrack, out _, out Vector3 pathFwd);
                lookDirection = pathFwd * wanderDirectionZ;
                break;

            case CreatureState.Fleeing:
                lookDirection = GetFleeDirection();
                break;

            case CreatureState.Charging:
                {
                    Transform t = null;
                    if (behaviorType == CreatureBehaviorType.Scared)
                        t = chaseTargetTransform;
                    else if (behaviorType == CreatureBehaviorType.Aggressive)
                        ResolveAggressiveChargeTarget(out t, out _);
                    if (t == null)
                        t = playerTransform;
                    if (t != null)
                        lookDirection = t.position - transform.position;
                }
                break;

            case CreatureState.Idle:
            default:
                // Keep current rotation
                return;
        }

        lookDirection.y = 0f;
        if (lookDirection.sqrMagnitude < 0.01f) return;
        lookDirection.Normalize();

        // Calculate target rotation
        Quaternion targetRot;
        if (alignToGround && isGrounded)
        {
            Vector3 forward = Vector3.ProjectOnPlane(lookDirection, groundNormal).normalized;
            if (forward.sqrMagnitude > 0.01f)
            {
                targetRot = Quaternion.LookRotation(forward, groundNormal);
            }
            else
            {
                targetRot = Quaternion.LookRotation(lookDirection, Vector3.up);
            }
        }
        else
        {
            targetRot = Quaternion.LookRotation(lookDirection, Vector3.up);
        }

        // Apply rotation to visual root or transform
        Transform rotTarget = visualRoot != null ? visualRoot : transform;
        rotTarget.rotation = Quaternion.Slerp(rotTarget.rotation, targetRot, rotationSpeed * dt);
    }

    #endregion

    #region Player Detection

    protected virtual void UpdatePlayerDetection()
    {
        if (playerTransform == null)
        {
            playerDetected = false;
            playerDistance = float.MaxValue;
            playerDirection = Vector3.zero;
            return;
        }

        Vector3 toPlayer = playerTransform.position - transform.position;
        playerDistance = toPlayer.magnitude;
        playerDirection = playerDistance > 0.01f ? (toPlayer / playerDistance) : Vector3.zero;

        // We always have a valid player; state logic decides what to do based on distance thresholds.
        playerDetected = true;
    }

    protected virtual void UpdateThreatDetection()
    {
        threatTransform = null;
        threatIsAggressive = false;
        threatDistance = float.MaxValue;
        threatDirection = Vector3.zero;
        _activeThreatCount = 0;
        _combinedFleeDirection = Vector3.zero;
        vehicleChaseTransform = null;
        vehicleChaseDistance = float.MaxValue;

        if (spawner == null || config == null) return;

        // Passive (bug): flee from nearby critters (Scared)
        if (behaviorType == CreatureBehaviorType.Passive && config.passiveFleeFromScared)
        {
            float detectRadius = Mathf.Max(config.passiveScaredDetectRadius, config.passiveScaredLoseDistance);
            int hitCount = Physics.OverlapSphereNonAlloc(
                transform.position,
                detectRadius,
                _threatColliderBuffer,
                creatureSenseLayers,
                QueryTriggerInteraction.Collide);

            Vector3 combinedAway = Vector3.zero;
            float closestSqr = float.MaxValue;
            Transform closest = null;

            for (int i = 0; i < hitCount; i++)
            {
                var col = _threatColliderBuffer[i];
                if (col == null) continue;
                var tc = col.GetComponentInParent<TrackCreature>();
                if (tc == null || tc == this) continue;
                if (!tc.isInitialized || tc.isDead) continue;
                if (tc.behaviorType != CreatureBehaviorType.Scared) continue;

                Vector3 toT = tc.transform.position - transform.position;
                float distSqr = toT.sqrMagnitude;
                Vector3 away = -toT;
                away.y = 0f;
                float dist = Mathf.Sqrt(distSqr);
                if (dist > 0.01f)
                    combinedAway += away.normalized * (1f / Mathf.Max(0.5f, dist));

                _activeThreatCount++;
                if (distSqr < closestSqr)
                {
                    closestSqr = distSqr;
                    closest = tc.transform;
                }
            }

            if (closest != null)
            {
                threatTransform = closest;
                threatIsAggressive = false;
                Vector3 toThreat = threatTransform.position - transform.position;
                threatDistance = toThreat.magnitude;
                threatDirection = threatDistance > 0.01f ? toThreat / threatDistance : Vector3.zero;
            }

            if (combinedAway.sqrMagnitude > 0.0001f)
                _combinedFleeDirection = combinedAway.normalized;
            else
            {
                spawner.SamplePath(currentDistanceAlongTrack, out _, out Vector3 fwd);
                fwd.y = 0f;
                _combinedFleeDirection = fwd.sqrMagnitude > 0.0001f ? fwd.normalized : Vector3.forward;
            }
        }

        // Scared (critter): beasts (optional) + player + NPC traffic
        if (behaviorType == CreatureBehaviorType.Scared)
        {
            Vector3 combinedAwayVector = Vector3.zero;
            float closestDistSqr = float.MaxValue;
            Transform closestThreat = null;

            if (config.scaredFleeFromAggressive)
            {
                float detectRadius = config.scaredAggressiveDetectRadius;
                int hitCount = Physics.OverlapSphereNonAlloc(
                    transform.position,
                    detectRadius,
                    _threatColliderBuffer,
                    creatureSenseLayers,
                    QueryTriggerInteraction.Collide);

                for (int i = 0; i < hitCount; i++)
                {
                    var col = _threatColliderBuffer[i];
                    if (col == null) continue;

                    var tc = col.GetComponentInParent<TrackCreature>();
                    if (tc == null || tc == this) continue;
                    if (!tc.isInitialized || tc.isDead) continue;
                    if (tc.behaviorType != CreatureBehaviorType.Aggressive) continue;

                    Vector3 toThreat = tc.transform.position - transform.position;
                    float distSqr = toThreat.sqrMagnitude;

                    Vector3 awayFromThis = -toThreat;
                    awayFromThis.y = 0f;
                    float dist = Mathf.Sqrt(distSqr);
                    if (dist > 0.01f)
                        combinedAwayVector += awayFromThis.normalized * (1f / Mathf.Max(0.5f, dist));

                    _activeThreatCount++;

                    if (distSqr < closestDistSqr)
                    {
                        closestDistSqr = distSqr;
                        closestThreat = tc.transform;
                    }
                }

                if (closestThreat != null)
                {
                    threatTransform = closestThreat;
                    threatIsAggressive = true;

                    Vector3 toThreat = threatTransform.position - transform.position;
                    threatDistance = toThreat.magnitude;
                    threatDirection = threatDistance > 0.01f ? toThreat / threatDistance : Vector3.zero;
                }
            }

            if (playerDetected && playerTransform != null && playerDistance < config.scaredDetectionRadius)
            {
                Vector3 awayFromPlayer = transform.position - playerTransform.position;
                awayFromPlayer.y = 0f;
                if (awayFromPlayer.sqrMagnitude > 0.0001f)
                {
                    float playerWeight = 1f / Mathf.Max(0.5f, playerDistance);
                    combinedAwayVector += awayFromPlayer.normalized * playerWeight;
                }
                _activeThreatCount++;
            }

            if (config.scaredFleeFromNpcTraffic && _npcTrafficLayers.value != 0)
            {
                RefreshNearestNpcTraffic(config.scaredNpcTrafficDetectRadius);
                if (vehicleChaseTransform != null && vehicleChaseDistance < config.scaredNpcTrafficDetectRadius)
                {
                    Vector3 awayNpc = transform.position - vehicleChaseTransform.position;
                    awayNpc.y = 0f;
                    if (awayNpc.sqrMagnitude > 0.0001f)
                    {
                        float w = 1f / Mathf.Max(0.5f, vehicleChaseDistance);
                        combinedAwayVector += awayNpc.normalized * w;
                    }
                    _activeThreatCount++;
                }
            }

            if (combinedAwayVector.sqrMagnitude > 0.0001f)
                _combinedFleeDirection = combinedAwayVector.normalized;
            else
            {
                spawner.SamplePath(currentDistanceAlongTrack, out _, out Vector3 fwd);
                fwd.y = 0f;
                _combinedFleeDirection = fwd.sqrMagnitude > 0.0001f ? fwd.normalized : Vector3.forward;
            }
        }

        if (behaviorType == CreatureBehaviorType.Aggressive && config.aggressiveHuntNpcTraffic &&
            _npcTrafficLayers.value != 0)
            RefreshNearestNpcTraffic(config.aggressiveNpcTrafficDetectRadius);

        if (behaviorType == CreatureBehaviorType.Aggressive && config.aggressiveHuntScaredCreatures)
        {
            bool playerPriority = playerDetected && playerDistance <= Mathf.Max(0f, aggressivePlayerPriorityRadius);
            if (playerPriority)
                chaseTargetTransform = null;
            else
            {
                var scared = FindNearestCreature(CreatureBehaviorType.Scared, config.aggressiveHuntRadius);
                chaseTargetTransform = scared != null ? scared.transform : null;
            }
        }
        else if (behaviorType != CreatureBehaviorType.Scared)
        {
            chaseTargetTransform = null;
        }
    }

    private void RefreshNearestNpcTraffic(float radius)
    {
        vehicleChaseTransform = null;
        vehicleChaseDistance = float.MaxValue;
        if (_npcTrafficLayers.value == 0 || radius <= 0.01f) return;

        int n = Physics.OverlapSphereNonAlloc(
            transform.position,
            radius,
            _npcOverlapBuffer,
            _npcTrafficLayers,
            QueryTriggerInteraction.Ignore);

        float bestSqr = float.MaxValue;
        Transform best = null;

        for (int i = 0; i < n; i++)
        {
            var c = _npcOverlapBuffer[i];
            if (c == null) continue;
            if (c.transform == transform || c.transform.IsChildOf(transform)) continue;

            Transform root = c.attachedRigidbody != null ? c.attachedRigidbody.transform : c.transform;
            float sqr = (root.position - transform.position).sqrMagnitude;
            if (sqr < bestSqr)
            {
                bestSqr = sqr;
                best = root;
            }
        }

        if (best != null)
        {
            vehicleChaseTransform = best;
            vehicleChaseDistance = Mathf.Sqrt(bestSqr);
        }
    }

    protected TrackCreature FindNearestCreature(CreatureBehaviorType targetType, float radius)
    {
        if (radius <= 0.01f) return null;

        int n = Physics.OverlapSphereNonAlloc(
            transform.position,
            radius,
            _creatureQueryBuffer,
            creatureSenseLayers,
            QueryTriggerInteraction.Collide);

        TrackCreature best = null;
        float bestSqr = float.MaxValue;

        for (int i = 0; i < n; i++)
        {
            var c = _creatureQueryBuffer[i];
            if (c == null) continue;

            var tc = c.GetComponentInParent<TrackCreature>();
            if (tc == null || tc == this) continue;
            if (!tc.isInitialized || tc.isDead) continue;
            if (tc.behaviorType != targetType) continue;

            float sqr = (tc.transform.position - transform.position).sqrMagnitude;
            if (sqr < bestSqr)
            {
                bestSqr = sqr;
                best = tc;
            }
        }

        return best;
    }

    protected Vector3 GetFleeDirection()
    {
        if (_activeThreatCount > 0 && _combinedFleeDirection.sqrMagnitude > 0.0001f)
            return _combinedFleeDirection;

        // Passive bugs only flee via critter detection above — never from the player car.
        if (behaviorType == CreatureBehaviorType.Passive)
        {
            if (spawner != null)
            {
                spawner.SamplePath(currentDistanceAlongTrack, out _, out Vector3 fwd);
                fwd.y = 0f;
                if (fwd.sqrMagnitude > 0.0001f)
                    return fwd.normalized;
            }
            return Vector3.forward;
        }

        if (playerDetected && playerTransform != null)
        {
            Vector3 fleeDir = transform.position - playerTransform.position;
            fleeDir.y = 0f;

            if (fleeDir.sqrMagnitude > 0.01f)
            {
                return fleeDir.normalized;
            }
        }

        // Last resort: run forward along track
        if (spawner != null)
        {
            spawner.SamplePath(currentDistanceAlongTrack, out _, out Vector3 fwd);
            fwd.y = 0f;
            if (fwd.sqrMagnitude > 0.0001f)
                return fwd.normalized;
        }

        return Vector3.forward;
    }   

    protected bool IsPlayerCollider(Collider col)
    {
        if (col == null) return false;

        // Check for player tag
        if (col.CompareTag("Player")) return true;

        // Check for CarController (the main player car component in this game)
        if (col.GetComponentInParent<CarController>() != null) return true;

        return false;
    }

    #endregion

    #region Crush Detection

    /// <summary>
    /// Aggressive beasts ignore generic moving obstacles (NPC traffic, props). They die only when hit by
    /// <see cref="RollingLogAlongTrack"/> (handled separately), <see cref="CrossTrackObstacle"/>,
    /// <see cref="TrackObstacleBounceBack"/>, or <see cref="ThrownObstacle"/>.
    /// </summary>
    protected static bool IsLethalObstacleFamilyForAggressiveBeast(Collider obstacleCollider)
    {
        if (obstacleCollider == null) return false;
        if (obstacleCollider.GetComponentInParent<CrossTrackObstacle>() != null) return true;
        if (obstacleCollider.GetComponentInParent<TrackObstacleBounceBack>() != null) return true;
        if (obstacleCollider.GetComponentInParent<ThrownObstacle>() != null) return true;
        return false;
    }

    protected bool IsCrushingCollider(Collider col, out float otherMass, out float otherSpeed)
    {
        otherMass = 0f;
        otherSpeed = 0f;

        if (col == null) return false;
        if (col.transform == transform || col.transform.IsChildOf(transform)) return false;

        // Only consider things that are on allowed layers
        if ((crushLayers.value & (1 << col.gameObject.layer)) == 0)
            return false;

        var rollingLog = col.GetComponentInParent<RollingLogAlongTrack>();
        if (rollingLog != null && rollingLog.IsScriptedAlongPath)
        {
            otherMass = rollingLog.RigidbodyMass;
            otherSpeed = rollingLog.CurrentScriptedSpeed;
            return otherSpeed >= minCrushSpeed;
        }

        // NPC traffic uses a kinematic Rigidbody + MovePosition — physics velocity stays ~0, so use scripted speed.
        // After a crash it becomes dynamic; then prefer max(scripted, physics).
        var npcTraffic = col.GetComponentInParent<NPCTrafficCar>();
        if (npcTraffic != null)
        {
            Rigidbody npcRb = col.attachedRigidbody != null ? col.attachedRigidbody : col.GetComponentInParent<Rigidbody>();
            otherMass = npcRb != null ? npcRb.mass : 0f;
            float scripted = npcTraffic.CurrentSpeed;
            float physicsSpd = 0f;
            if (npcRb != null && !npcRb.isKinematic)
                physicsSpd = npcRb.GetPointVelocity(transform.position).magnitude;
            otherSpeed = Mathf.Max(scripted, physicsSpd);
            return otherSpeed >= minCrushSpeed;
        }

        // Prefer Rigidbody detection so we can check velocity
        Rigidbody rb = col.attachedRigidbody != null ? col.attachedRigidbody : col.GetComponentInParent<Rigidbody>();
        if (rb != null)
        {
            otherMass = rb.mass;

            // Use point velocity at the contact point to better reflect "moving into" the creature
            Vector3 v = rb.GetPointVelocity(transform.position);
            otherSpeed = v.magnitude;

            // Only crush if actually moving
            return otherSpeed >= minCrushSpeed;
        }

        var tracker = col.GetComponentInParent<KinematicVelocityTracker>();
        if (tracker != null)
        {
            otherMass = 0f; // unknown / irrelevant for non-RB movers
            otherSpeed = tracker.Speed;

            // only crush if actually moving
            return otherSpeed >= minCrushSpeed;
        }

        // Tag-based fallback: only allow if explicitly enabled (otherwise we can't know velocity)
        if (HasAnyTag(col, obstacleTags))
        {
            if (!allowTagCrushWithoutRigidbody)
                return false;

            // Treat as "moving" if user allows this path (legacy override)
            otherSpeed = minCrushSpeed;
            return true;
        }

        return false;
    }


    protected bool HasAnyTag(Collider col, string[] tags)
    {
        if (col == null || tags == null || tags.Length == 0) return false;
        for (int i = 0; i < tags.Length; i++)
        {
            string t = tags[i];
            if (string.IsNullOrEmpty(t)) continue;
            if (col.CompareTag(t) || col.transform.root.CompareTag(t))
                return true;
        }
        return false;
    }

    protected virtual void OnHitByCrushingObstacle(Collider obstacleCollider, float obstacleMass, float obstacleSpeed)
    {
        if (isDead) return;

        // Extra safety: if not moving, don't squish.
        if (obstacleSpeed < minCrushSpeed)
            return;

        if (behaviorType == CreatureBehaviorType.Aggressive)
        {
            if (_forcefieldPhysicsLaunchActive)
                return;

            var rollingLog = obstacleCollider.GetComponentInParent<RollingLogAlongTrack>();
            if (rollingLog != null)
            {
                rollingLog.ApplyBeastStrike(transform.position, obstacleSpeed);
                LaunchAggressiveBeastByObstacleThenDie(obstacleCollider, obstacleSpeed);
                return;
            }

            if (!IsLethalObstacleFamilyForAggressiveBeast(obstacleCollider))
                return;

            LaunchAggressiveBeastByObstacleThenDie(obstacleCollider, obstacleSpeed);
            return;
        }

        // Passive + Scared: die to moving obstacle impacts (original behavior).
        if (obstacleCollider.GetComponentInParent<NPCTrafficCar>() != null)
            SpawnNpcTrafficCrushPopup(obstacleCollider);

        killSource = CreatureKillSource.Other;
        Die();
    }


    #endregion

    #region Hit & Death

    /// <summary>
    /// Called when the player car enters this creature's trigger.
    /// Handles differently based on behavior type.
    /// </summary>
    /// <summary>
    /// Called when the player car enters this creature's trigger.
    /// Handles differently based on behavior type.
    /// </summary>
    protected virtual void OnHitByPlayer(Collider playerCollider)
    {
        if (isDead) return;

        // CHECK FOR ARMED FORCEFIELD FIRST
        // Only aggressive creatures (beast) are forcefield-intercepted.
        var forcefield = playerCollider.GetComponentInParent<CarForcefield>();
        if (forcefield != null && forcefield.IsArmed && behaviorType == CreatureBehaviorType.Aggressive)
        {
            // The forcefield will handle this via its own trigger detection
            // Just call KilledByForcefield directly to ensure it happens
            KilledByForcefield();
            return;
        }

        // Aggressive (beast): slam the car, stay alive, and go idle so it can hunt again.
        if (behaviorType == CreatureBehaviorType.Aggressive)
        {
            CausePlayerCrash(playerCollider);
            HideBullRushTelegraph();
            EndBullRush();
            SetState(CreatureState.Idle);
            return;
        }

        // Passive / Scared: run over = die + coin reward
        killSource = CreatureKillSource.Car;
        SpawnRunOverPopup();
        Die();
    }

    /// <summary>
    /// Flings the aggressive beast away from the obstacle, then kills it after a short delay (tunable in inspector).
    /// Uses the same physics takeover as the forcefield path (non-kinematic RB, colliders solid).
    /// </summary>
    protected virtual void LaunchAggressiveBeastByObstacleThenDie(Collider obstacleCollider, float obstacleSpeed)
    {
        if (isDead || behaviorType != CreatureBehaviorType.Aggressive || obstacleCollider == null) return;

        killSource = CreatureKillSource.Other;

        HideBullRushTelegraph();
        EndBullRush();

        if (!TryBeginForcefieldPhysicsLaunch(obstacleLaunchCorpseMass))
            return;

        var rb = GetComponent<Rigidbody>();
        if (rb == null)
        {
            Die();
            return;
        }

        Vector3 fromObstacle = transform.position - obstacleCollider.bounds.center;
        fromObstacle.y = 0f;
        if (fromObstacle.sqrMagnitude < 1e-4f)
            fromObstacle = -transform.forward;
        fromObstacle.Normalize();

        float horizontalDv = obstacleLaunchBaseVelocityChange + obstacleSpeed * obstacleLaunchSpeedScale;
        horizontalDv = Mathf.Min(horizontalDv, obstacleLaunchMaxVelocityChange);

        Vector3 dv = fromObstacle * horizontalDv + Vector3.up * obstacleLaunchUpVelocityChange;
        rb.AddForce(dv, ForceMode.VelocityChange);

        if (Mathf.Abs(obstacleLaunchTorque) > 0.01f)
        {
            Vector3 torqueAxis = Vector3.Cross(Vector3.up, fromObstacle);
            if (torqueAxis.sqrMagnitude < 1e-6f)
                torqueAxis = transform.right;
            else
                torqueAxis.Normalize();
            rb.AddTorque(torqueAxis * obstacleLaunchTorque, ForceMode.VelocityChange);
        }

        StartCoroutine(ObstacleLaunchFinalizeAfterDelayCoroutine());
    }

    private IEnumerator ObstacleLaunchFinalizeAfterDelayCoroutine()
    {
        yield return new WaitForSeconds(obstacleLaunchDeathDelay);
        FinalizeObstacleLaunchKill();
    }

    /// <summary>
    /// Ends obstacle fling phase and runs normal death (no car run-over popup; <see cref="CreatureKillSource.Other"/>).
    /// </summary>
    protected virtual void FinalizeObstacleLaunchKill()
    {
        if (isDead) return;

        _forcefieldPhysicsLaunchActive = false;
        Die();

        if (behaviorType == CreatureBehaviorType.Aggressive && config != null && config.despawnAfterHit)
        {
            CancelInvoke();
            Destroy(gameObject, config.despawnDelay);
        }
    }

    /// <summary>
    /// Stops scripted movement and enables a dynamic rigidbody so <see cref="CarForcefield"/> can launch the corpse.
    /// Call <see cref="FinalizeForcefieldLaunchKill"/> after applying impulse.
    /// </summary>
    public virtual bool TryBeginForcefieldPhysicsLaunch(float corpseMass = 55f)
    {
        if (isDead || _forcefieldPhysicsLaunchActive) return false;

        _forcefieldPhysicsLaunchActive = true;

        if (behaviorType == CreatureBehaviorType.Aggressive)
            HideBullRushTelegraph();

        CancelInvoke();
        StopAllCoroutines();

        if (_allColliders != null)
        {
            for (int i = 0; i < _allColliders.Length; i++)
            {
                var c = _allColliders[i];
                if (c != null) c.isTrigger = false;
            }
        }

        var rb = GetComponent<Rigidbody>();
        if (rb == null)
            rb = gameObject.AddComponent<Rigidbody>();

        rb.mass = Mathf.Max(0.1f, corpseMass);
        rb.isKinematic = false;
        rb.useGravity = true;
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        rb.WakeUp();

        return true;
    }

    /// <summary>
    /// Awards run-over popup + coin path and destroys via normal death flow (after physics launch).
    /// </summary>
    public virtual void FinalizeForcefieldLaunchKill()
    {
        if (isDead) return;

        _forcefieldPhysicsLaunchActive = false;
        killSource = CreatureKillSource.Car;
        SpawnRunOverPopup();
        Die();

        if (behaviorType == CreatureBehaviorType.Aggressive && config != null && config.despawnAfterHit)
        {
            CancelInvoke();
            StopAllCoroutines();
            Destroy(gameObject, config.despawnDelay);
        }
    }

    /// <summary>
    /// Called by CarForcefield when the creature is intercepted.
    /// Kills the creature WITHOUT causing a crash. Awards coins.
    /// </summary>
    public virtual void KilledByForcefield()
    {
        if (isDead) return;

        // Mark as car kill for coin reward (forcefield counts as car kill)
        killSource = CreatureKillSource.Car;

        // Hide bull rush telegraph if active
        if (behaviorType == CreatureBehaviorType.Aggressive)
        {
            HideBullRushTelegraph();
        }

        // Comic popup
        SpawnRunOverPopup();

        // Die and give rewards - but NO crash
        Die();

        // Handle despawn for aggressive creatures
        if (behaviorType == CreatureBehaviorType.Aggressive && config != null && config.despawnAfterHit)
        {
            CancelInvoke();
            StopAllCoroutines();
            Destroy(gameObject, config.despawnDelay);
        }
    }


    /// <summary>
    /// Causes the player to crash with knockback.
    /// Impact is amplified if this occurs during a bull rush.
    /// </summary>
    protected virtual void CausePlayerCrash(Collider playerCollider)
    {
        var carController = playerCollider.GetComponentInParent<CarController>();
        if (carController == null) return;

        Rigidbody carRb = carController.GetComponent<Rigidbody>();

        // Determine if this is a bull rush hit
        bool isBullRushHit = isBullRushActive && config.useBullRush;
        float impactMultiplier = isBullRushHit ? Mathf.Max(1f, config.bullRushImpactMultiplier) : 1f;

        // Calculate hit direction
        Vector3 hitDirection;
        if (isBullRushHit && bullRushDirection.sqrMagnitude > 0.01f)
        {
            // Use the bull rush direction (the way we were charging)
            hitDirection = bullRushDirection;
        }
        else
        {
            // Use direction from creature to car
            hitDirection = (carController.transform.position - transform.position).normalized;
        }
        hitDirection.y = 0f;
        hitDirection.Normalize();

        // Calculate impact speed
        float baseImpactSpeed = isBullRushHit
            ? config.aggressiveChargeSpeed * config.bullRushSpeedMultiplier
            : config.aggressiveChargeSpeed;
        float impactSpeed = Mathf.Max(baseImpactSpeed, 8f);

        // Contact point is the creature's position
        Vector3 contactPoint = transform.position;

        // Severity scales with multiplier for bull rush (fallback if car has no CrashSeverityConfig)
        float severity = Mathf.Clamp01(config.impactCrashSeverity * impactMultiplier);
        float extraSeverity = config.impactCrashSeverity * impactMultiplier;

        // Trigger the crash (handles FX, damage, recovery state, etc.)
        carController.ApplyExternalCrashDamage(
            hitDirection,
            impactSpeed,
            contactPoint,
            severity,
            transform,
            GetComponent<Rigidbody>(),
            null,
            extraSeverity);

        // Apply additional knockback force for impact feel
        if (carRb != null)
        {
            ApplyImpactKnockback(carRb, hitDirection, impactMultiplier);
        }

        // Handle despawn
        if (config.despawnAfterHit)
        {
            // Stop movement
            if (isBullRushHit) EndBullRush();
            currentSpeed = 0f;
        }

        if (isBullRushHit)
        {
            Debug.Log($"[TrackCreature] Bull rush HIT! Multiplier={impactMultiplier:F1}x, Severity={severity:F2}");
        }
    }

    /// <summary>
    /// Applies physics knockback to the car.
    /// </summary>
    protected virtual void ApplyImpactKnockback(Rigidbody carRb, Vector3 hitDirection, float multiplier)
    {
        if (carRb == null || config == null) return;

        // Calculate knockback with multiplier
        float knockbackForce = config.impactKnockbackForce * multiplier;
        float lift = config.impactLift * multiplier;
        float torque = config.impactTorque * multiplier;

        // Build force direction with lift
        Vector3 forceDir = hitDirection;
        if (knockbackForce > 0.01f)
        {
            forceDir = hitDirection + Vector3.up * (lift / Mathf.Max(1f, knockbackForce));
            forceDir.Normalize();
        }

        // Apply knockback impulse
        if (knockbackForce > 0f)
        {
            carRb.AddForce(forceDir * knockbackForce, ForceMode.VelocityChange);
        }

        // Apply spin torque
        if (torque > 0f)
        {
            Vector3 toCarLocal = carRb.transform.InverseTransformDirection(hitDirection);
            float sideSign = Mathf.Sign(toCarLocal.x);
            if (Mathf.Abs(sideSign) < 0.1f)
            {
                sideSign = Random.value > 0.5f ? 1f : -1f;
            }

            carRb.AddTorque(Vector3.up * torque * sideSign, ForceMode.VelocityChange);
        }
    }

    /// <summary>
    /// Kill this creature. Can be called externally (e.g., by turret projectiles via IDamageable).
    /// </summary>
    public virtual void Die()
    {
        if (isDead) return;

        isDead = true;
        SetState(CreatureState.Dead);
    }

    protected virtual void OnDeath()
    {

        HideBullRushTelegraph();

        Vector3 effectPos = coinSpawnPoint != null ? coinSpawnPoint.position : transform.position;

        // Play appropriate death sound based on kill source
        PlayKillSound();

        // Spawn death effect
        if (deathEffectPrefab != null)
        {
            Instantiate(deathEffectPrefab, transform.position, Quaternion.identity);
        }

        // Only award coins when the PLAYER car ran it over.
        // Turret kills award sprockets via RacingBullet; obstacle/crush kills award nothing.
        if (killSource == CreatureKillSource.Car)
        {
            SpawnCoinReward(effectPos);
        }

        // Update animation
        if (animator != null && !string.IsNullOrEmpty(deadParam))
        {
            animator.SetBool(deadParam, true);
        }

        // Remove from spawner tracking
        if (spawner != null)
        {
            spawner.RemoveCreature(gameObject);
        }

        if (!IsInvoking() && gameObject != null)
        {
            Destroy(gameObject, 0.5f);
        }
    }

    /// <summary>
    /// Plays the appropriate kill sound based on how the creature died.
    /// </summary>
    protected virtual void PlayKillSound()
    {
        AudioClip clipToPlay = killSource == CreatureKillSource.Turret ? turretKillSound : carKillSound;

        // Fall back to general death sound if specific sound not set
        if (clipToPlay == null)
            clipToPlay = deathSound;

        if (clipToPlay != null)
        {
            AudioSource.PlayClipAtPoint(clipToPlay, transform.position, killSoundVolume);
        }
    }

    /// <summary>
    /// Awards COINS when killed by car.
    /// </summary>
    protected virtual void SpawnCoinReward(Vector3 position)
    {
        int rewardAmount = config.coinReward;
        if (rewardAmount <= 0) return;

        if (RacingCoinCollectionHub.Instance != null)
        {
            RacingCoinCollectionHub.Instance.AwardCoins(
                rewardAmount,
                position,
                RacingCoinRewardSource.Obstacle);
        }
        else
        {
            // Backward-compatible fallback if hub is not present yet.
            var gm = GameManager_Racing.Instance;
            if (gm != null)
                gm.RegisterObstacleReward(rewardAmount);

            var skillMgr = RacingSkillTreeManager.Instance;
            if (skillMgr != null)
                skillMgr.AddCurrency(rewardAmount);

            if (RacingPopups.IsReady)
            {
                Color textColor = rewardAmount >= 5 ? new Color(1f, 0.84f, 0f) : Color.yellow;
                Color outlineColor = rewardAmount >= 5 ? new Color(0.8f, 0.5f, 0f) : new Color(0.6f, 0.4f, 0f);
                RacingPopups.SpawnCoin(rewardAmount, position + Vector3.up * 0.5f, textColor, outlineColor);
            }
        }
    }

    
    #endregion

    #region Wander Helpers

    protected void ResetWanderDirection()
    {
        wanderTimer = 0f;
        nextWanderChangeTime = config.passiveDirectionChangeInterval * Random.Range(0.7f, 1.3f);

        // Random lateral direction
        wanderDirectionX = Random.Range(-1f, 1f);

        // Random forward/back, but bias toward forward
        wanderDirectionZ = Random.Range(-0.3f, 1f);

        // Normalize so we don't get weird speeds at diagonals
        if (Mathf.Abs(wanderDirectionX) > 0.01f || Mathf.Abs(wanderDirectionZ) > 0.01f)
        {
            float mag = Mathf.Sqrt(wanderDirectionX * wanderDirectionX + wanderDirectionZ * wanderDirectionZ);
            wanderDirectionX /= mag;
            wanderDirectionZ /= mag;
        }
        else
        {
            wanderDirectionZ = 1f;
        }
    }

    #endregion

    #region Helpers

    protected float GetRoadHalfWidth()
    {
        if (trackGenerator != null)
        {
            return trackGenerator.RoadWidth * 0.5f;
        }
        return 2f; // Fallback (half of 4-unit wide track)
    }

    protected void PlaySound(AudioClip clip)
    {
        if (audioSource != null && clip != null)
        {
            audioSource.PlayOneShot(clip);
        }
    }

    private void SpawnRunOverPopup()
    {
        if (!enableRunOverPopup) return;
        if (!RacingPopups.IsReady) return;

        Vector3 basePos = (coinSpawnPoint != null) ? coinSpawnPoint.position : transform.position;
        basePos += Vector3.up * runOverPopupHeight;

        // Pass 0 to trigger random text selection (style asset uses useRandomText/randomTexts)
        RacingPopups.SpawnWorldSpace(runOverPopupType, 0f, basePos);
    }

    private void SpawnNpcTrafficCrushPopup(Collider npcCollider)
    {
        if (!enableRunOverPopup) return;
        if (!RacingPopups.IsReady) return;

        Vector3 p = npcCollider != null
            ? npcCollider.ClosestPoint(transform.position)
            : transform.position;
        p += Vector3.up * runOverPopupHeight;
        RacingPopups.SpawnWorldSpace(runOverPopupType, 0f, p);
    }

    /// <summary>Beast→critter and critter→bug; callers gate with enableBeastEatPopup / enableCritterEatBugPopup.</summary>
    private void SpawnEatStylePopupOnPrey(TrackCreature prey)
    {
        if (prey == null || !RacingPopups.IsReady) return;

        Vector3 p = prey.transform.position + Vector3.up * beastEatPopupHeight;
        RacingPopups.SpawnWorldSpace(beastEatPopupType, 0f, p);
    }


    #endregion

    #region Animation

    protected virtual void UpdateAnimation()
    {
        if (animator == null) return;

        float speed = currentSpeed;

        if (!string.IsNullOrEmpty(speedParam))
        {
            animator.SetFloat(speedParam, speed);
        }

        if (!string.IsNullOrEmpty(runningParam))
        {
            bool isRunning = speed > 0.5f && (currentState == CreatureState.Fleeing || currentState == CreatureState.Charging);
            animator.SetBool(runningParam, isRunning);
        }
    }

    #endregion

    #region Debug

    protected virtual void OnDrawGizmosSelected()
    {
        if (!showDebugGizmos) return;

        // Draw detection radius
        if (config != null)
        {
            switch (behaviorType)
            {
                case CreatureBehaviorType.Scared:
                    Gizmos.color = Color.yellow;
                    Gizmos.DrawWireSphere(transform.position, config.scaredDetectionRadius);
                    break;

                case CreatureBehaviorType.Aggressive:
                    Gizmos.color = Color.red;
                    Gizmos.DrawWireSphere(transform.position, config.aggressiveDetectionRadius);
                    break;
            }
        }

        // Draw direction
        Gizmos.color = Color.blue;
        Gizmos.DrawRay(transform.position, transform.forward * 2f);

        // Draw to player
        if (playerTransform != null)
        {
            Gizmos.color = playerDetected ? Color.green : Color.gray;
            Gizmos.DrawLine(transform.position, playerTransform.position);
        }
    }

    #endregion
}