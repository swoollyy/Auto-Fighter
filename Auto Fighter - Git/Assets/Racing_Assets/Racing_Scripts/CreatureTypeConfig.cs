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

    [Header("Idle Rhythm (Walk / Pause)")]
    [Tooltip("When using bug-style idle, walk for a while then pause (all tiers).")]
    public bool idleUseWalkPauseRhythm = true;

    [Min(0.1f)] public float idleWalkSegmentMinSec = 0.9f;
    [Min(0.1f)] public float idleWalkSegmentMaxSec = 3.2f;
    [Min(0f)] public float idlePauseMinSec = 0.25f;
    [Min(0f)] public float idlePauseMaxSec = 1.75f;

    [Header("Idle Minimum Travel")]
    [Tooltip("Minimum straight-line distance (meters, in the idle offset plane) that each new idle target must be from the creature's current spot. Prevents tiny re-targets right next to the creature that make it constantly re-face and spin in place. Automatically clamped down if it exceeds the creature's idle wiggle radius.")]
    [Min(0f)] public float idleMinTravelDistance = 2.5f;

    [Header("Behavior Tuning - Passive")]
    [Tooltip("How fast the passive creature wanders.")]
    [Min(0f)] public float passiveWanderSpeed = 2f;

    [Tooltip("How often the passive creature changes direction.")]
    [Min(0.1f)] public float passiveDirectionChangeInterval = 1.5f;

    [Header("Passive - Flee From Critter (Scared)")]
    [Tooltip("If true, bugs flee when a critter (Scared creature) is nearby.")]
    public bool passiveFleeFromScared = true;

    [Min(0f)] public float passiveScaredDetectRadius = 14f;
    [Min(0f)] public float passiveScaredLoseDistance = 28f;
    [Min(0f)] public float passiveFleeBaseSpeed = 3.5f;
    [Min(0f)] public float passiveFleeMaxSpeed = 10f;
    [Min(0f)] public float passiveFleeBuildupRate = 3f;
    [Min(0f)] public float passiveFleeMaxOffRoadDistance = 3f;

    [Header("Passive - Bug Idle")]
    [Tooltip("If true, uses small anchored wiggles (recommended). If false, uses legacy wander on the spline.")]
    public bool passiveUseBugIdleMovement = true;

    [Min(0f)] public float passiveIdleBugMoveSpeed = 1.15f;
    [Min(0.05f)] public float passiveIdleBugDirectionChangeInterval = 1.05f;
    [Min(0f)] public float passiveIdleBugLateralRadius = 2.2f;
    [Min(0f)] public float passiveIdleBugForwardRadius = 1.3f;

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

    [Header("Scared - Flee NPC Traffic")]
    [Tooltip("If true, critters treat NPC traffic layer (from spawner) as a threat to flee from.")]
    public bool scaredFleeFromNpcTraffic = true;

    [Min(0f)] public float scaredNpcTrafficDetectRadius = 20f;

    [Header("Scared - Hunt Bugs (Passive)")]
    [Tooltip("If true, critters chase and kill passive bugs when no higher-priority threat.")]
    public bool scaredHuntPassiveCreatures = true;

    [Min(0f)] public float scaredHuntPassiveRadius = 12f;
    [Min(0f)] public float scaredPassiveHuntLoseDistance = 20f;
    [Min(0f)] public float scaredBugHuntSpeed = 5f;
    [Min(0.01f)] public float scaredBugHuntSpeedMultiplier = 1.15f;

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

    [Header("Aggressive - Hunt NPC Traffic")]
    [Tooltip("If true, beast can charge NPC cars on the traffic layer (set mask on TrackCreatureSpawner).")]
    public bool aggressiveHuntNpcTraffic = true;

    [Min(0f)] public float aggressiveNpcTrafficDetectRadius = 30f;

    [Header("Behavior Tuning - Aggressive")]
    [Tooltip("Detection radius for the aggressive creature to spot the player.")]
    [Min(0f)] public float aggressiveDetectionRadius = 25f;

    [Tooltip("Charge speed toward the player.")]
    [Min(0f)] public float aggressiveChargeSpeed = 10f;

    [Tooltip("How far off-track the aggressive creature can go to intercept.")]
    [Min(0f)] public float aggressiveMaxOffTrackDistance = 4f;

    [Header("Crush Interaction")]
    [Tooltip("Aggressive (big) creature only dies to obstacles with Rigidbody mass >= this threshold (Passive + Scared always die).")]
    [Min(0f)] public float aggressiveCrushMassThreshold = 80f;

    [Header("Aggressive - Bull Rush")]
    [Tooltip("If true, the aggressive creature will charge up before rushing in a straight line toward the player.")]
    public bool useBullRush = true;

    [Tooltip("Duration in seconds the creature pauses to 'wind up' before charging.")]
    [Min(0f)] public float bullRushChargeUpDuration = 0.8f;

    [Tooltip("Speed multiplier during the bull rush (applied on top of aggressiveChargeSpeed).")]
    [Min(0.1f)] public float bullRushSpeedMultiplier = 1.5f;

    [Tooltip("How long the bull rush lasts before the creature can re-target (seconds).")]
    [Min(0.1f)] public float bullRushDuration = 1.5f;

    [Tooltip("Maximum lateral steering rate during bull rush (degrees per second). Lower = straighter line.")]
    [Min(0f)] public float bullRushMaxSteerRate = 15f;

    [Tooltip("If the creature misses and travels this far past the target, end the rush early.")]
    [Min(1f)] public float bullRushOvershootDistance = 8f;

    [Tooltip("Cooldown after a bull rush before the creature can start another one.")]
    [Min(0f)] public float bullRushCooldown = 1.0f;

    [Header("Aggressive - Bull Rush vs obstacles")]
    [Tooltip("If true, a bull rush shoves dynamic rigidbody obstacles (trees/props) instead of phasing through them. Static colliders still block movement.")]
    public bool bullRushObstaclePushEnabled = true;

    [Tooltip("Base planar velocity change (VelocityChange) applied to obstacles the rush runs into.")]
    [Min(0f)] public float bullRushObstaclePushVelocityChange = 7f;

    [Tooltip("Extra push from current rush speed: total uses pushVelocityChange + rushSpeed * this.")]
    [Min(0f)] public float bullRushObstaclePushSpeedScale = 0.22f;

    [Tooltip("Cap on planar velocity change sent to the obstacle.")]
    [Min(0f)] public float bullRushObstaclePushMaxVelocityChange = 28f;

    [Tooltip("Do not push obstacles heavier than this (0 = no mass limit).")]
    [Min(0f)] public float bullRushObstacleMaxPushMass = 900f;

    [Header("Aggressive - Bull Rush Telegraph (Line Renderer)")]
    [Tooltip("If true, draws a line showing the bull rush path during charge-up.")]
    public bool bullRushShowTelegraph = true;

    [Tooltip("Width of the telegraph line.")]
    [Min(0.01f)] public float bullRushLineWidth = 0.2f;

    [Tooltip("How far ahead the line extends from the creature.")]
    [Min(1f)] public float bullRushLineLength = 25f;

    [Tooltip("Height offset for the line above the ground.")]
    public float bullRushLineYOffset = 0.1f;

    [Tooltip("Color of the telegraph line during charge-up.")]
    public Color bullRushLineColorCharging = new Color(1f, 0.3f, 0f, 0.7f); // Orange

    [Tooltip("Color of the telegraph line during active rush.")]
    public Color bullRushLineColorRushing = new Color(1f, 0f, 0f, 0.9f); // Red

    [Tooltip("Fade out duration when rush ends.")]
    [Min(0f)] public float bullRushLineFadeOutTime = 0.2f;

    [Header("Aggressive - Impact (Crash Settings)")]
    [Tooltip("Crash severity when hitting the player (0-1). Higher = longer recovery, more damage.")]
    [Range(0f, 1f)] public float impactCrashSeverity = 0.5f;

    [Tooltip("Additional impulse force applied to the car on hit (VelocityChange mode). Set higher for bull rush feel.")]
    [Min(0f)] public float impactKnockbackForce = 12f;

    [Tooltip("Upward force component added to the impact (gives the car a bump).")]
    [Min(0f)] public float impactLift = 3f;

    [Tooltip("Torque applied to spin the car on impact.")]
    [Min(0f)] public float impactTorque = 6f;

    [Tooltip("Multiplier applied to all impact values when the hit occurs during a bull rush.")]
    [Min(1f)] public float bullRushImpactMultiplier = 1.5f;

    [Tooltip("If true, the creature despawns after successfully hitting the player.")]
    public bool despawnAfterHit = true;

    [Tooltip("Delay before despawning after hitting the player (allows death FX to play).")]
    [Min(0f)] public float despawnDelay = 0.3f;

    [Header("Thrower (Gorilla) - Hill Spawn")]
    [Tooltip("Spawn off-road on hills instead of on the road. Used by the gorilla.")]
    public bool spawnOffroadOnHills = false;

    [Tooltip("Terrain must be at least this many meters above the road for a valid hill spawn.")]
    [Min(0f)] public float minHillHeightAboveRoad = 5f;

    [Tooltip("Minimum planar distance from the road edge when spawning on hills.")]
    [Min(0f)] public float hillSpawnMinDistanceFromRoad = 10f;

    [Tooltip("Maximum planar distance from the track centerline when spawning on hills.")]
    [Min(1f)] public float hillSpawnMaxDistanceFromCenterline = 55f;

    [Tooltip("How many placement attempts before giving up on a spawn slot.")]
    [Min(1)] public int hillSpawnAttempts = 8;

    [Header("Thrower (Gorilla) - Idle")]
    [Min(0f)] public float throwerIdleMoveSpeed = 1.8f;
    [Min(0.05f)] public float throwerIdleDirectionChangeInterval = 2.4f;
    [Min(0f)] public float throwerIdleWanderRadius = 8f;

    [Header("Thrower (Gorilla) - Seek & Grab")]
    [Tooltip("While idle, look for environment props within this radius.")]
    [Min(0f)] public float throwerObstacleSeekRadius = 16f;

    [Tooltip("When this close to a sought prop, start the run-in.")]
    [Min(0.1f)] public float throwerApproachTriggerRange = 16f;

    [Tooltip("How close the gorilla must get to grab and lift the prop.")]
    [Min(0.1f)] public float throwerGrabRange = 1.8f;

    [Min(0f)] public float throwerRunToObstacleSpeed = 7f;

    [Tooltip("Layers treated as throwable environment obstacles.")]
    public LayerMask throwerObstacleLayers = 1 << 20;

    [Header("Thrower (Gorilla) - Lift")]
    [Min(0f)] public float throwerLiftDuration = 0.55f;
    [Min(0f)] public float throwerLiftHeight = 2.2f;

    [Header("Thrower (Gorilla) - Throw")]
    [Tooltip("Only commit to a grab/throw if the player is within this range.")]
    [Min(0f)] public float throwerMaxPlayerRange = 80f;

    [Tooltip("Horizontal throw speed (m/s). Flight time is distance / this.")]
    [Min(0.1f)] public float throwerThrowSpeed = 22f;

    [Tooltip("Extra upward loft added to the ballistic throw.")]
    [Min(0f)] public float throwerThrowArcHeight = 4f;

    [Tooltip("0 = aim at the player's current lane on the road. 1 = full along-track lead, keeping the same left/right fraction and following turns.")]
    [Range(0f, 1f)] public float throwerPredictionStrength = 0.85f;

    [Header("Thrower (Gorilla) - Accuracy")]
    [Tooltip("Throw accuracy (0–1). X = at track start, Y = at track end. Higher = closer to the predicted player position.")]
    public Vector2 throwerAccuracyByProgress = new Vector2(0.35f, 0.85f);

    [Tooltip("Random +/- added to accuracy each throw so landings are never identical.")]
    [Range(0f, 0.35f)] public float throwerAccuracyWiggle = 0.08f;

    [Tooltip("Max left/right miss on the road (meters) at accuracy 0.")]
    [Min(0f)] public float throwerMaxMissLateral = 4.5f;

    [Tooltip("Max along-track miss (meters) at accuracy 0.")]
    [Min(0f)] public float throwerMaxMissForward = 5.5f;

    [Tooltip("Spin applied to the thrown prop.")]
    [Min(0f)] public float throwerThrowSpin = 8f;

    [Min(0f)] public float throwerThrowCooldown = 3.5f;

    [Tooltip("Seconds the thrown prop stays as physics debris before despawn (0 = leave it).")]
    [Min(0f)] public float throwerThrownPropLifetime = 12f;
                
    [Tooltip("Layer assigned to a prop after it is thrown so it can hit the car and be intercepted by the forcefield. Default 20 = Environment (must be in CarForcefield.obstacleLayers).")]
    public int throwerThrownPropLayer = 20;

    [Header("Thrower (Gorilla) - Thrown Impact")]
    [Range(0f, 1f)] public float throwerThrownCrashSeverity = 0.55f;
    [Min(0f)] public float throwerThrownKnockbackForce = 10f;
    [Min(0f)] public float throwerThrownLift = 2.5f;
    [Min(0f)] public float throwerThrownTorque = 5f;

    [Header("Thrower (Gorilla) - Landing Telegraph")]
    [Tooltip("Ground landing decal, same setup as ThrownObstacleDirector.groundRingPrefab and bounce-back landingTelegraphPrefab. Assign LandingRingDecalGorilla (ThrownMatGorilla).")]
    public GameObject throwerLandingTelegraphPrefab;

}