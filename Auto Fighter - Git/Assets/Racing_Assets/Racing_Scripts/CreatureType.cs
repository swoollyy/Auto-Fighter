/// <summary>
/// Defines the behavioral type of a track creature.
/// </summary>
public enum CreatureBehaviorType
{
    /// <summary>
    /// Wanders randomly on the track. Does not react to the player.
    /// Rewards coins when hit.
    /// </summary>
    Passive,

    /// <summary>
    /// Wanders until player approaches, then flees away from the player.
    /// Builds up scurry speed when fleeing. Can run off-road.
    /// </summary>
    Scared,

    /// <summary>
    /// Detects player from a distance and charges toward them.
    /// Causes a crash on collision. Can move off-track to intercept.
    /// </summary>
    Aggressive
}