using UnityEngine;

/// <summary>
/// Configuration for a type of track creature.
/// Similar to ObstacleType and NPCCarType for consistent spawner patterns.
/// </summary>
[System.Serializable]
public class CreatureTypeConfig
{
    [Header("Identity")]
    [Tooltip("Name for your sanity in the inspector.")]
    public string id;

    [Tooltip("The creature prefab to spawn.")]
    public GameObject prefab;

    [Tooltip("The behavioral type of this creature.")]
    public CreatureBehaviorType behaviorType = CreatureBehaviorType.Passive;

    [Header("Spawn Weight")]
    [Tooltip("Relative spawn weight compared to other creature types.")]
    [Min(0f)] public float baseWeight = 1f;

    [Header("Distance Band (normalized 0-1 along track)")]
    [Tooltip("Normalized distance along track where this creature starts appearing.")]
    [Range(0f, 1f)] public float startAtNormalizedDist = 0f;

    [Tooltip("Normalized distance where this creature reaches full spawn weight.")]
    [Range(0f, 1f)] public float fullWeightNormalizedDist = 0.2f;

    [Tooltip("Normalized distance where this creature stops appearing.")]
    [Range(0f, 1f)] public float stopAtNormalizedDist = 1f;

    [Header("Placement Tweaks")]
    [Tooltip("Extra height offset for placement (useful for flying creatures).")]
    public float extraHeightOffset = 0f;

    [Tooltip("Shrink usable lateral width if needed.")]
    public float extraLateralPadding = 0f;

    [Header("Coin Reward")]
    [Tooltip("Coins awarded when this creature is killed (by car or turret).")]
    [Min(0)] public int coinReward = 1;

    [Header("Behavior Tuning - Passive")]
    [Tooltip("How fast the passive creature wanders.")]
    [Min(0f)] public float passiveWanderSpeed = 2f;

    [Tooltip("How often the passive creature changes direction.")]
    [Min(0.1f)] public float passiveDirectionChangeInterval = 1.5f;

    [Header("Behavior Tuning - Scared")]
    [Tooltip("Detection radius for the scared creature to notice the player.")]
    [Min(0f)] public float scaredDetectionRadius = 15f;

    [Tooltip("Base flee speed when first startled.")]
    [Min(0f)] public float scaredBaseFleeSpeed = 4f;

    [Tooltip("Maximum flee speed after scurry buildup.")]
    [Min(0f)] public float scaredMaxFleeSpeed = 12f;

    [Tooltip("How quickly the flee speed builds up (speed per second).")]
    [Min(0f)] public float scaredSpeedBuildupRate = 3f;

    [Tooltip("How far off-road the scared creature can run.")]
    [Min(0f)] public float scaredMaxOffRoadDistance = 3f;


    [Header("Scared - Idle Bug Movement (Before Detection)")]
    [Tooltip("If true, scared creatures use the same low-energy 'bug idle' movement until the player is within scaredDetectionRadius.")]
    public bool scaredIdleUseBugMovement = true;

    [Tooltip("Speed used for idle bug movement (small wiggles).")]
    [Min(0f)] public float scaredIdleBugMoveSpeed = 1.25f;

    [Tooltip("How often the idle bug movement picks a new micro-direction.")]
    [Min(0.05f)] public float scaredIdleBugDirectionChangeInterval = 1.0f;

    [Tooltip("Max left/right wiggle distance (meters) around the idle anchor.")]
    [Min(0f)] public float scaredIdleBugLateralRadius = 2.0f;

    [Tooltip("Max forward/back wiggle distance (meters) around the idle anchor (along-track).")]
    [Min(0f)] public float scaredIdleBugForwardRadius = 1.25f;

    [Header("Scared - Flee From Aggressive (Big Creature)")]
    [Tooltip("If true, scared creatures will also flee when an aggressive creature is nearby.")]
    public bool scaredFleeFromAggressive = true;

    [Tooltip("Radius to detect aggressive creatures.")]
    [Min(0f)] public float scaredAggressiveDetectRadius = 18f;

    [Tooltip("If the aggressive creature is farther than this, the scared creature calms down and returns to idle/wander.")]
    [Min(0f)] public float scaredAggressiveLoseFearDistance = 30f;

    [Tooltip("Speed multiplier applied when fleeing from an aggressive creature (on top of scared flee speed).")]
    [Min(0f)] public float scaredAggressiveFleeSpeedMultiplier = 1.25f;

    [Header("Aggressive - Idle & Hunting")]
    [Tooltip("If true, aggressive creatures will also wander using idle bug movement when not chasing.")]
    public bool aggressiveIdleUseBugMovement = true;

    [Tooltip("Speed used for aggressive idle movement.")]
    [Min(0f)] public float aggressiveIdleBugMoveSpeed = 1.0f;

    [Tooltip("How often the aggressive idle movement picks a new micro-direction.")]
    [Min(0.05f)] public float aggressiveIdleBugDirectionChangeInterval = 1.1f;

    [Tooltip("Max left/right wiggle distance (meters) around the aggressive idle anchor.")]
    [Min(0f)] public float aggressiveIdleBugLateralRadius = 2.5f;

    [Tooltip("Max forward/back wiggle distance (meters) around the aggressive idle anchor (along-track).")]
    [Min(0f)] public float aggressiveIdleBugForwardRadius = 1.5f;

    [Tooltip("If true, aggressive creatures will hunt scared (medium) creatures when they are close.")]
    public bool aggressiveHuntScaredCreatures = true;

    [Tooltip("Radius to detect scared creatures to hunt.")]
    [Min(0f)] public float aggressiveHuntRadius = 20f;

    [Tooltip("Speed multiplier applied when hunting scared creatures.")]
    [Min(0f)] public float aggressiveHuntSpeedMultiplier = 1.35f;
    [Header("Behavior Tuning - Aggressive")]
    [Tooltip("Detection radius for the aggressive creature to spot the player.")]
    [Min(0f)] public float aggressiveDetectionRadius = 25f;

    [Tooltip("Charge speed toward the player.")]
    [Min(0f)] public float aggressiveChargeSpeed = 10f;

    [Tooltip("How far off-track the aggressive creature can go to intercept.")]
    [Min(0f)] public float aggressiveMaxOffTrackDistance = 4f;

    [Tooltip("Crash severity caused on collision (0-1).")]
    [Range(0f, 1f)] public float aggressiveImpactDamage = 0.5f;

    [Header("Crush Interaction")]
    [Tooltip("Aggressive (big) creature only dies to obstacles with Rigidbody mass >= this threshold (Passive + Scared always die).")]
    [Min(0f)] public float aggressiveCrushMassThreshold = 80f;
}
