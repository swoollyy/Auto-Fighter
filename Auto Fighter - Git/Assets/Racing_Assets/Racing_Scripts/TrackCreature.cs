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
/// - Aggressive: Car drives through, triggers crash via code, then dies
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

    [Header("UI Popup (Car Run-Over)")]
    [SerializeField] private bool enableRunOverPopup = true;
    [SerializeField] private RacingPopupType runOverPopupType = RacingPopupType.CreatureSplat;
    [SerializeField] private float runOverPopupHeight = 1.2f;

    [Header("Coin Reward SFX (Car Kills)")]
    [SerializeField] private bool playCoinRewardSound = true;

    [Tooltip("Optional override clips for creature coin rewards. If empty, will try CoinDatabase coin sounds.")]
    [SerializeField] private AudioClip[] coinRewardSoundsOverride;

    [SerializeField, Range(0f, 1f)] private float coinRewardSoundVolume = 1f;
    [SerializeField, Range(0f, 0.3f)] private float coinRewardPitchVariance = 0.05f;
    [SerializeField] private float coinRewardBasePitch = 1f;

    [SerializeField] private float coinRewardMinDistance = 5f;
    [SerializeField] private float coinRewardMaxDistance = 40f;


    [Tooltip("Visual root to animate/rotate separately from physics.")]
    [SerializeField] private Transform visualRoot;

    [Tooltip("Layer mask for ground raycasting.")]
    [SerializeField] private LayerMask groundLayer = ~0;


    [Header("Movement Avoidance")]
    [Tooltip("If enabled, creatures will steer around colliders on these layers while moving (wander/idle/flee/charge).")]
    [SerializeField] private bool enableMovementAvoidance = true;

    [Tooltip("Layers to avoid while moving (obstacles, walls, props, etc).")]
    [SerializeField] private LayerMask movementAvoidanceLayers = 0;

    [Tooltip("Sphere radius for obstacle sensing while moving.")]
    [SerializeField, Min(0.01f)] private float avoidanceRadius = 0.35f;

    [Tooltip("How far ahead we check for blockers (meters).")]
    [SerializeField, Min(0.05f)] private float avoidanceLookAhead = 1.2f;

    [Tooltip("Height above pivot for the sphere cast origin (helps if pivot is low).")]
    [SerializeField] private float avoidanceCastHeight = 0.35f;

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

    [Tooltip("Aggressive (big) creature only dies to obstacles with mass >= this threshold. Passive + Scared always die.")]
    [SerializeField] private float aggressiveCrushMassThreshold = 80f;
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

    // Aggressive hunting target (scared creature)
    protected Transform chaseTargetTransform;
    // Ground state
    protected bool isGrounded = true;
    protected Vector3 groundNormal = Vector3.up;
    protected float currentGroundY;

    // Cached colliders
    private Collider[] _allColliders;

    private const int MAX_THREATS = 8;
    private Collider[] _threatColliderBuffer = new Collider[MAX_THREATS];
    private Vector3 _combinedFleeDirection = Vector3.forward;
    private int _activeThreatCount = 0;

    #endregion

    #region Properties

    public CreatureState CurrentState => currentState;
    public bool IsDead => isDead;
    public CreatureBehaviorType BehaviorType => behaviorType;
    public float DistanceToPlayer => playerDistance;


    public bool IsInitialized => isInitialized;
    public float DistanceAlongTrack => currentDistanceAlongTrack;
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
                SetState(CreatureState.Wandering);
                break;
            case CreatureBehaviorType.Scared:
            case CreatureBehaviorType.Aggressive:
                SetState(CreatureState.Idle);
                break;
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

        // Update animation
        UpdateAnimation();
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

        // Aggressive creature instantly kills scared creature on trigger contact
        if (behaviorType == CreatureBehaviorType.Aggressive)
        {
            TrackCreature otherCreature = other.GetComponentInParent<TrackCreature>();

            if (otherCreature != null &&
                otherCreature != this &&
                !otherCreature.isDead &&
                otherCreature.behaviorType == CreatureBehaviorType.Scared)
            {

                Debug.Log($"Working!");

                // Kill immediately
                otherCreature.killSource = CreatureKillSource.Other;
                otherCreature.Die();

                // Stop chasing and return to idle
                chaseTargetTransform = null;
                SetState(CreatureState.Idle);

                return; // IMPORTANT: stop further trigger processing
            }
            else
            {
                Debug.Log($"Working Not! {other.gameObject.name}");
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

                // Idle anchor for bug-style idle movement (scared + aggressive).
                if (config != null)
                {
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
                currentFleeSpeed = config.scaredBaseFleeSpeed;
                PlaySound(runSound);
                break;

            case CreatureState.Charging:
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
    /// Passive behavior: Wanders randomly, never reacts to player.
    /// </summary>
    protected virtual void UpdatePassiveBehavior(float dt)
    {
        // Always wandering
        if (currentState != CreatureState.Wandering && currentState != CreatureState.Dead)
        {
            SetState(CreatureState.Wandering);
        }

        UpdateWandering(dt);
    }

    /// <summary>
    /// Scared behavior: Wanders until player gets close, then flees.
    /// </summary>
    /// <summary>
    /// Scared behavior: Wanders until player or aggressive creature gets close, then flees.
    /// </summary>
    protected virtual void UpdateScaredBehavior(float dt)
    {
        // Determine if ANY threat is present
        bool hasAggressiveThreat = threatIsAggressive && threatTransform != null &&
                                   threatDistance < config.scaredAggressiveDetectRadius;
        bool hasPlayerThreat = playerDetected && playerDistance < config.scaredDetectionRadius;
        bool shouldFlee = hasAggressiveThreat || hasPlayerThreat;

        switch (currentState)
        {
            case CreatureState.Idle:
            case CreatureState.Wandering:
                if (shouldFlee)
                {
                    // Immediately start fleeing
                    if (currentState != CreatureState.Fleeing)
                        SetState(CreatureState.Fleeing);

                    // Boost flee speed when fleeing from aggressive creatures
                    if (hasAggressiveThreat)
                    {
                        currentFleeSpeed = Mathf.Max(currentFleeSpeed, config.scaredBaseFleeSpeed) *
                                           Mathf.Max(1f, config.scaredAggressiveFleeSpeedMultiplier);
                    }
                }
                else
                {
                    // No threats - do idle/wander behavior
                    if (config != null && config.scaredIdleUseBugMovement)
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
                break;

            case CreatureState.Fleeing:
                // ALWAYS update fleeing movement - never skip this!
                UpdateFleeing(dt);

                // Check if we can calm down (all threats far enough away)
                bool playerFar = !playerDetected || playerDistance > config.scaredDetectionRadius * 2f;
                bool aggroFar = !threatIsAggressive || threatTransform == null ||
                               threatDistance > config.scaredAggressiveLoseFearDistance;

                if (playerFar && aggroFar && _activeThreatCount == 0)
                {
                    // All threats gone - return to idle/wander
                    SetState(CreatureState.Wandering);
                    currentFleeSpeed = config.scaredBaseFleeSpeed;
                }
                break;
        }
    }

    /// <summary>
    /// Aggressive behavior: Idles until player is detected, then charges.
    /// </summary>
    protected virtual void UpdateAggressiveBehavior(float dt)
    {
        switch (currentState)
        {
            case CreatureState.Idle:
            case CreatureState.Wandering:

                // Priority radius: if the player is inside this bubble, ALWAYS target the player.
                bool playerIsClosePriority = playerDetected && playerDistance <= Mathf.Max(0f, aggressivePlayerPriorityRadius);

                if (playerIsClosePriority)
                {
                    chaseTargetTransform = null; // force target = player
                    SetState(CreatureState.Charging);
                }
                else if (config.aggressiveHuntScaredCreatures && chaseTargetTransform != null)

                {
                    SetState(CreatureState.Charging);
                }
                else if (playerDetected && playerDistance < config.aggressiveDetectionRadius)
                {
                    SetState(CreatureState.Charging);
                }

                else
                {
                    // Idle bug movement (gives the big creature life even when not chasing)
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

                // Give up if target is gone / too far
                bool hunting = chaseTargetTransform != null;

                if (hunting)
                {
                    float distToTarget = Vector3.Distance(transform.position, chaseTargetTransform.position);
                    if (distToTarget > config.aggressiveHuntRadius * 1.25f)
                    {
                        chaseTargetTransform = null;
                        SetState(CreatureState.Idle);
                    }
                }
                else
                {
                    if (playerDistance > config.aggressiveDetectionRadius * 1.5f)
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
    protected virtual void UpdateBugIdleMovement(
        float dt,
        float bugSpeed,
        float directionChangeInterval,
        float lateralRadius,
        float forwardRadius,
        float extraOffRoad)
    {
        if (spawner == null) return;

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

        // Smooth lateral
        currentLateralOffset = Mathf.MoveTowards(currentLateralOffset, targetLateralOffset, Mathf.Max(0.01f, currentSpeed) * dt);
    }



    protected virtual void UpdateFleeing(float dt)
    {
        // Build up flee speed over time (scurry effect)
        currentFleeSpeed = Mathf.MoveTowards(
            currentFleeSpeed,
            config.scaredMaxFleeSpeed,
            config.scaredSpeedBuildupRate * dt
        );

        currentSpeed = currentFleeSpeed;

        // Calculate flee direction (away from player)
        Vector3 fleeDirection = GetFleeDirection();

        // Convert flee direction to track movement
        spawner.SamplePath(currentDistanceAlongTrack, out Vector3 pathPos, out Vector3 pathForward);
        Vector3 flatForward = pathForward;
        flatForward.y = 0f;
        flatForward.Normalize();

        Vector3 right = Vector3.Cross(Vector3.up, flatForward).normalized;

        // Determine if we should run forward or backward along track
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

        // Can run off-road when fleeing
        float maxOffRoad = config.scaredMaxOffRoadDistance;
        float halfWidth = GetRoadHalfWidth();
        float maxLateral = halfWidth + maxOffRoad;

        targetLateralOffset += lateralMovement;
        targetLateralOffset = Mathf.Clamp(targetLateralOffset, -maxLateral, maxLateral);

        currentLateralOffset = Mathf.MoveTowards(currentLateralOffset, targetLateralOffset, currentSpeed * dt);
    }

    protected virtual void UpdateCharging(float dt)
    {
        if (spawner == null || config == null) return;



        // Determine target (player has priority if inside aggressivePlayerPriorityRadius).
        Transform target = chaseTargetTransform != null ? chaseTargetTransform : playerTransform;

        bool playerPriority = playerDetected && playerTransform != null &&
                              playerDistance <= Mathf.Max(0f, aggressivePlayerPriorityRadius);

        if (playerPriority)
            target = playerTransform;

        if (target == null) return;

        if (chaseTargetTransform != null)
        {
            if (chaseTargetTransform.TryGetComponent<TrackCreature>(out var tc) && tc.isDead)
            {
                chaseTargetTransform = null;
                SetState(CreatureState.Idle);
                currentSpeed = 0f;
                return;
            }
        }



        bool huntingCreature = (target != playerTransform);

        float speedMult = huntingCreature ? Mathf.Max(0.01f, config.aggressiveHuntSpeedMultiplier) : 1f;
        currentSpeed = Mathf.Max(0f, config.aggressiveChargeSpeed) * speedMult;

        // -------- FIX: move in "track space" toward the target's distance-along-track --------
        // The old dot-product weighting could go ~0 when the target was mostly lateral, causing the
        // big creature to rotate toward the player but barely move.
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

        // Smooth lateral steering so it feels like "chasing" instead of teleporting.
        float lateralStep = moveStep * 1.5f;
        targetLateralOffset = Mathf.MoveTowards(targetLateralOffset, desiredLateral, lateralStep);
        currentLateralOffset = Mathf.MoveTowards(currentLateralOffset, targetLateralOffset, lateralStep);
    }

    #endregion

    #region Movement Core

    protected virtual void UpdateMovement(float dt)
    {
        if (spawner == null) return;

        // Sample path at current distance
        spawner.SamplePath(currentDistanceAlongTrack, out Vector3 pathPos, out Vector3 pathForward);

        // Calculate target position
        Vector3 flatForward = pathForward;
        flatForward.y = 0f;
        if (flatForward.sqrMagnitude < 0.0001f)
            flatForward = Vector3.forward;
        flatForward.Normalize();

        Vector3 right = Vector3.Cross(Vector3.up, flatForward).normalized;

        Vector3 targetPos = pathPos + right * currentLateralOffset;

        // Keep current Y for now (ground snap handles Y)
        targetPos.y = transform.position.y;

        // --- NEW: real velocity from position delta ---
        Vector3 prevPos = transform.position;

        // Move toward target position
        float moveSpeed = Mathf.Max(currentSpeed, 0f); // Use state-driven speed (don't force movement when idle)

        Vector3 desired = targetPos - transform.position;
        desired.y = 0f;

        float desiredDist = desired.magnitude;
        Vector3 moveDir = desiredDist > 0.0001f ? (desired / desiredDist) : Vector3.zero;

        float step = moveSpeed * dt;

        // Layer avoidance (steer moveDir so we don't run into / path through certain layers)
        if (enableMovementAvoidance && movementAvoidanceLayers.value != 0 && moveDir.sqrMagnitude > 0.0001f)
        {
            moveDir = ApplyAvoidanceToMoveDir(moveDir, step, flatForward, right);
        }

        Vector3 newPos = transform.position + moveDir * Mathf.Min(step, desiredDist);
        // Keep current Y for now (ground snap handles Y)
        newPos.y = transform.position.y;

        transform.position = newPos;

        Vector3 delta = transform.position - prevPos;
        currentVelocity = delta / Mathf.Max(dt, 0.001f);

    }

    private Vector3 ApplyAvoidanceToMoveDir(Vector3 moveDir, float step, Vector3 flatForward, Vector3 right)
    {
        Vector3 origin = transform.position + Vector3.up * avoidanceCastHeight;

        float look = Mathf.Max(avoidanceLookAhead, step * 2f);

        // If we'd hit something we want to avoid, steer
        if (Physics.SphereCast(origin, avoidanceRadius, moveDir, out RaycastHit hit, look, movementAvoidanceLayers, QueryTriggerInteraction.Ignore))
        {
            Vector3 n = hit.normal;
            n.y = 0f;

            // If normal is garbage (rare), just treat it as "block forward"
            if (n.sqrMagnitude < 0.0001f)
                n = -moveDir;

            n.Normalize();

            // Slide along the surface
            Vector3 slide = Vector3.ProjectOnPlane(moveDir, n);
            slide.y = 0f;

            if (slide.sqrMagnitude > 0.0001f)
            {
                slide.Normalize();

                // small lateral bias so we don't jitter-stick on edges
                float sideDot = Mathf.Clamp(Vector3.Dot(slide, right), -1f, 1f);
                currentLateralOffset = Mathf.MoveTowards(
                    currentLateralOffset,
                    currentLateralOffset + sideDot * avoidanceSideBias,
                    avoidanceResponse * Time.deltaTime
                );

                return slide;
            }

            // If we can't slide (head-on), choose whichever side is clearer
            float side = ChooseClearSide(origin, right, look);
            Vector3 sidestep = (moveDir + right * side * 0.75f);
            sidestep.y = 0f;

            if (sidestep.sqrMagnitude > 0.0001f)
            {
                sidestep.Normalize();

                currentLateralOffset = Mathf.MoveTowards(
                    currentLateralOffset,
                    currentLateralOffset + side * avoidanceSideBias,
                    avoidanceResponse * Time.deltaTime
                );

                return sidestep;
            }
        }

        return moveDir;
    }

    private float ChooseClearSide(Vector3 origin, Vector3 right, float look)
    {
        // Probe both sides a bit. Pick the side with MORE free space.
        float probeDist = Mathf.Max(0.25f, avoidanceRadius * 2f);

        bool hitR = Physics.SphereCast(origin, avoidanceRadius, right, out _, probeDist, movementAvoidanceLayers, QueryTriggerInteraction.Ignore);
        bool hitL = Physics.SphereCast(origin, avoidanceRadius, -right, out _, probeDist, movementAvoidanceLayers, QueryTriggerInteraction.Ignore);

        if (hitR && !hitL) return -1f;
        if (!hitR && hitL) return 1f;

        // If both clear or both blocked, bias randomly so groups don't all pick the same side.
        return (Random.value < 0.5f) ? -1f : 1f;
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
                    Transform t = chaseTargetTransform != null ? chaseTargetTransform : playerTransform;
                    if (t != null)
                        lookDirection = (t.position - transform.position);
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

        if (spawner == null || config == null) return;

        // Scared: detect ALL nearby aggressive creatures and compute combined flee vector
        if (behaviorType == CreatureBehaviorType.Scared && config.scaredFleeFromAggressive)
        {
            float detectRadius = config.scaredAggressiveDetectRadius;
            int hitCount = Physics.OverlapSphereNonAlloc(
                transform.position,
                detectRadius,
                _threatColliderBuffer,
                creatureSenseLayers,
                QueryTriggerInteraction.Collide
            );

            Vector3 combinedAwayVector = Vector3.zero;
            float closestDistSqr = float.MaxValue;
            Transform closestThreat = null;

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

                // Calculate away vector with distance-based weighting (closer = stronger influence)
                Vector3 awayFromThis = -toThreat;
                awayFromThis.y = 0f;
                float dist = Mathf.Sqrt(distSqr);
                if (dist > 0.01f)
                {
                    // Weight by inverse distance - closer threats have more influence
                    float weight = 1f / Mathf.Max(0.5f, dist);
                    combinedAwayVector += awayFromThis.normalized * weight;
                }

                _activeThreatCount++;

                // Track the closest threat for backward compatibility
                if (distSqr < closestDistSqr)
                {
                    closestDistSqr = distSqr;
                    closestThreat = tc.transform;
                }
            }

            // Set the closest threat as the primary (for legacy code that uses threatTransform)
            if (closestThreat != null)
            {
                threatTransform = closestThreat;
                threatIsAggressive = true;

                Vector3 toThreat = threatTransform.position - transform.position;
                threatDistance = toThreat.magnitude;
                threatDirection = threatDistance > 0.01f ? (toThreat / threatDistance) : Vector3.zero;
            }

            // Also factor in the player as a threat if nearby
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

            // Normalize the combined flee direction
            if (combinedAwayVector.sqrMagnitude > 0.0001f)
            {
                _combinedFleeDirection = combinedAwayVector.normalized;
            }
            else
            {
                // No threats - default to forward along track
                spawner.SamplePath(currentDistanceAlongTrack, out _, out Vector3 fwd);
                fwd.y = 0f;
                _combinedFleeDirection = fwd.sqrMagnitude > 0.0001f ? fwd.normalized : Vector3.forward;
            }
        }

        // Aggressive: optionally detect scared to hunt
        if (behaviorType == CreatureBehaviorType.Aggressive && config.aggressiveHuntScaredCreatures)
        {
            bool playerPriority = playerDetected && playerDistance <= Mathf.Max(0f, aggressivePlayerPriorityRadius);
            if (playerPriority)
            {
                chaseTargetTransform = null;
            }
            else
            {
                var scared = FindNearestCreature(CreatureBehaviorType.Scared, config.aggressiveHuntRadius);
                chaseTargetTransform = scared != null ? scared.transform : null;
            }
        }
        else
        {
            chaseTargetTransform = null;
        }
    }

    protected TrackCreature FindNearestCreature(CreatureBehaviorType targetType, float radius)
    {
        if (radius <= 0.01f) return null;

        Collider[] cols = Physics.OverlapSphere(transform.position, radius, creatureSenseLayers, QueryTriggerInteraction.Collide);

        TrackCreature best = null;
        float bestSqr = float.MaxValue;

        for (int i = 0; i < cols.Length; i++)
        {
            var c = cols[i];
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
        // If we have a valid combined flee direction from multi-threat detection, use it
        if (_activeThreatCount > 0 && _combinedFleeDirection.sqrMagnitude > 0.0001f)
        {
            return _combinedFleeDirection;
        }

        // Fallback: flee from player if detected
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


    protected bool IsCrushingCollider(Collider col, out float otherMass, out float otherSpeed)
    {
        otherMass = 0f;
        otherSpeed = 0f;

        if (col == null) return false;
        if (col.transform == transform || col.transform.IsChildOf(transform)) return false;

        // Only consider things that are on allowed layers
        if ((crushLayers.value & (1 << col.gameObject.layer)) == 0)
            return false;

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

        // Passive + Scared always die to moving obstacle impacts.
        // Aggressive (big) only dies if obstacle is "heavy enough".
        if (behaviorType == CreatureBehaviorType.Aggressive && obstacleMass > 0f)
        {
            float threshold = aggressiveCrushMassThreshold;
            if (config != null)
                threshold = Mathf.Max(0f, config.aggressiveCrushMassThreshold);

            if (obstacleMass < threshold)
                return;
        }

        killSource = CreatureKillSource.Other;
        Die();
    }


    #endregion

    #region Hit & Death

    /// <summary>
    /// Called when the player car enters this creature's trigger.
    /// Handles differently based on behavior type.
    /// </summary>
    protected virtual void OnHitByPlayer(Collider playerCollider)
    {
        if (isDead) return;

        // Mark as car kill for coin reward
        killSource = CreatureKillSource.Car;

        // Aggressive creatures cause crash BEFORE dying
        if (behaviorType == CreatureBehaviorType.Aggressive)
        {
            CausePlayerCrash(playerCollider);
        }

        // NEW: comic popup for running them over
        SpawnRunOverPopup();

        // ALL creatures die and give rewards when hit
        Die();
    }




    /// <summary>
    /// Causes the player to crash via code (not physics).
    /// Uses ApplyExternalCrashDamage so all crash FX/penalties apply.
    /// </summary>
    protected virtual void CausePlayerCrash(Collider playerCollider)
    {
        // Try to find CarController and trigger crash
        var carController = playerCollider.GetComponentInParent<CarController>();
        if (carController != null)
        {
            // Calculate hit direction from creature to car
            Vector3 hitDirection = (carController.transform.position - transform.position).normalized;

            // Use the car's current speed as impact speed, or a minimum
            Rigidbody carRb = carController.GetComponent<Rigidbody>();
            float impactSpeed = carRb != null ? carRb.velocity.magnitude : 10f;
            impactSpeed = Mathf.Max(impactSpeed, 8f); // Minimum impact for creature attacks

            // Contact point is the creature's position
            Vector3 contactPoint = transform.position;

            // Severity based on config (0-1)
            float severity = Mathf.Clamp01(config.aggressiveImpactDamage);

            // Call the proper crash method - this handles all FX, damage, etc.
            carController.ApplyExternalCrashDamage(hitDirection, impactSpeed, contactPoint, severity);
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

        // Destroy after a short delay (allows death animation/effects)
        Destroy(gameObject, 0.5f);
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

        // Register the coins with GameManager_Racing
        var gm = GameManager_Racing.Instance;
        if (gm != null)
        {
            gm.RegisterObstacleReward(rewardAmount);
        }

        // Add to player currency via skill tree manager
        var skillMgr = RacingSkillTreeManager.Instance;
        if (skillMgr != null)
        {
            skillMgr.AddCurrency(rewardAmount);
        }

        // Show coin popup
        if (RacingPopups.IsReady)
        {
            Color textColor = rewardAmount >= 5 ? new Color(1f, 0.84f, 0f) : Color.yellow;
            Color outlineColor = rewardAmount >= 5 ? new Color(0.8f, 0.5f, 0f) : new Color(0.6f, 0.4f, 0f);
            RacingPopups.SpawnCoin(rewardAmount, position + Vector3.up * 0.5f, textColor, outlineColor);
        }
        PlayCoinRewardSFX(rewardAmount, position);

    }

    private void PlayCoinRewardSFX(int rewardAmount, Vector3 position)
    {
        if (!playCoinRewardSound) return;

        AudioClip clip = null;

        // 1) Prefer explicit overrides (fast + predictable)
        if (coinRewardSoundsOverride != null && coinRewardSoundsOverride.Length > 0)
        {
            clip = coinRewardSoundsOverride[Random.Range(0, coinRewardSoundsOverride.Length)];
        }
        else
        {
            // 2) Fallback to CoinDatabase sounds (matches your coin system)
            // Map reward to a rough "coin type" for sound choice.
            CoinType type = rewardAmount >= 10 ? CoinType.Gold : (rewardAmount >= 5 ? CoinType.Silver : CoinType.Bronze);
            var data = CoinDatabase.Get(type);
            if (data != null && data.collectSounds != null && data.collectSounds.Length > 0)
            {
                clip = data.collectSounds[Random.Range(0, data.collectSounds.Length)];

                // If you want, you can also borrow volume/pitch from the coin data:
                coinRewardSoundVolume = data.collectVolume;
                coinRewardPitchVariance = data.pitchVariance;
                coinRewardBasePitch = data.basePitch;
            }
        }

        if (clip == null) return;

        // Need a real AudioSource to support pitch variance (PlayClipAtPoint can't set pitch).
        var go = new GameObject("CreatureCoinRewardSFX");
        go.transform.position = position;

        var src = go.AddComponent<AudioSource>();
        src.spatialBlend = 1f;
        src.rolloffMode = AudioRolloffMode.Linear;
        src.minDistance = Mathf.Max(0.01f, coinRewardMinDistance);
        src.maxDistance = Mathf.Max(src.minDistance + 0.1f, coinRewardMaxDistance);

        src.volume = Mathf.Clamp01(coinRewardSoundVolume);
        src.pitch = Mathf.Clamp(coinRewardBasePitch + Random.Range(-coinRewardPitchVariance, coinRewardPitchVariance), 0.01f, 3f);

        src.clip = clip;
        src.Play();

        Destroy(go, clip.length / Mathf.Max(0.01f, src.pitch));
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
        RacingPopups.Spawn(runOverPopupType, 0f, basePos);
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