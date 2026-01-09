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
    Other       // Any other source - rewards coins
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
    protected CreatureKillSource killSource = CreatureKillSource.Car;
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

    // Detection state
    protected bool playerDetected = false;
    protected float playerDistance;
    protected Vector3 playerDirection;

    // Ground state
    protected bool isGrounded = true;
    protected Vector3 groundNormal = Vector3.up;
    protected float currentGroundY;

    // Cached colliders
    private Collider[] _allColliders;

    #endregion

    #region Properties

    public CreatureState CurrentState => currentState;
    public bool IsDead => isDead;
    public CreatureBehaviorType BehaviorType => behaviorType;
    public float DistanceToPlayer => playerDistance;

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

        // Check if hit by player car
        if (IsPlayerCollider(other))
        {
            OnHitByPlayer(other);
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
    protected virtual void UpdateScaredBehavior(float dt)
    {
        switch (currentState)
        {
            case CreatureState.Idle:
            case CreatureState.Wandering:
                // Check for player detection
                if (playerDetected && playerDistance < config.scaredDetectionRadius)
                {
                    SetState(CreatureState.Fleeing);
                }
                else
                {
                    // Wander casually
                    if (currentState != CreatureState.Wandering)
                        SetState(CreatureState.Wandering);
                    UpdateWandering(dt);
                }
                break;

            case CreatureState.Fleeing:
                UpdateFleeing(dt);

                // Keep fleeing as long as player is somewhat close
                // (don't stop fleeing just because player got a bit further)
                if (playerDistance > config.scaredDetectionRadius * 2f)
                {
                    // Player is far enough, go back to wandering
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
                // Check for player detection
                if (playerDetected && playerDistance < config.aggressiveDetectionRadius)
                {
                    SetState(CreatureState.Charging);
                }
                else
                {
                    // Idle or slow wander
                    if (currentState != CreatureState.Idle)
                        SetState(CreatureState.Idle);
                }
                break;

            case CreatureState.Charging:
                UpdateCharging(dt);

                // If player gets too far, give up (optional)
                if (playerDistance > config.aggressiveDetectionRadius * 1.5f)
                {
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
        currentSpeed = config.aggressiveChargeSpeed;

        if (playerTransform == null) return;

        // Get direction toward player
        Vector3 toPlayer = playerTransform.position - transform.position;
        toPlayer.y = 0f;

        if (toPlayer.sqrMagnitude < 0.01f) return;

        Vector3 chargeDirection = toPlayer.normalized;

        // Get track info
        spawner.SamplePath(currentDistanceAlongTrack, out Vector3 pathPos, out Vector3 pathForward);
        Vector3 flatForward = pathForward;
        flatForward.y = 0f;
        flatForward.Normalize();

        Vector3 right = Vector3.Cross(Vector3.up, flatForward).normalized;

        // Move along track toward player
        float forwardDot = Vector3.Dot(chargeDirection, flatForward);
        float trackDirection = forwardDot >= 0 ? 1f : -1f;

        // Weight movement toward player
        float trackMovement = currentSpeed * Mathf.Abs(forwardDot) * trackDirection * dt;
        currentDistanceAlongTrack += trackMovement;

        // Clamp to track bounds
        float totalLength = spawner.GetTotalLength();
        currentDistanceAlongTrack = Mathf.Clamp(currentDistanceAlongTrack, 0f, totalLength);

        // Lateral movement toward player
        float lateralDot = Vector3.Dot(chargeDirection, right);
        float lateralMovement = currentSpeed * lateralDot * dt;

        // Can move off-track to intercept
        float maxOffTrack = config.aggressiveMaxOffTrackDistance;
        float halfWidth = GetRoadHalfWidth();
        float maxLateral = halfWidth + maxOffTrack;

        targetLateralOffset += lateralMovement;
        targetLateralOffset = Mathf.Clamp(targetLateralOffset, -maxLateral, maxLateral);

        currentLateralOffset = Mathf.MoveTowards(currentLateralOffset, targetLateralOffset, currentSpeed * 1.5f * dt);
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

        // Move toward target position
        float moveSpeed = Mathf.Max(currentSpeed, 5f); // Minimum speed for responsiveness
        transform.position = Vector3.MoveTowards(transform.position, targetPos, moveSpeed * dt);

        // Calculate velocity for animation
        currentVelocity = (targetPos - transform.position) / Mathf.Max(dt, 0.001f);
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
                if (playerTransform != null)
                {
                    lookDirection = (playerTransform.position - transform.position);
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
            return;
        }

        Vector3 toPlayer = playerTransform.position - transform.position;
        playerDistance = toPlayer.magnitude;
        playerDirection = playerDistance > 0.01f ? toPlayer / playerDistance : Vector3.zero;
        playerDetected = true;
    }

    protected Vector3 GetFleeDirection()
    {
        if (!playerDetected || playerTransform == null)
        {
            // Default: run forward along track
            spawner.SamplePath(currentDistanceAlongTrack, out _, out Vector3 fwd);
            return fwd;
        }

        // Run directly away from player
        Vector3 fleeDir = transform.position - playerTransform.position;
        fleeDir.y = 0f;

        if (fleeDir.sqrMagnitude < 0.01f)
        {
            // Player is exactly on us, pick a random direction
            fleeDir = Random.insideUnitCircle.normalized;
            fleeDir = new Vector3(fleeDir.x, 0f, fleeDir.y);
        }

        return fleeDir.normalized;
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

        // Only award coins if NOT killed by turret
        // (turret kills award sprockets via RacingBullet)
        if (killSource != CreatureKillSource.Turret)
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