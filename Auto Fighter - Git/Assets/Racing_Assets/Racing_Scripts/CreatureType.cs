/// <summary>
/// Defines the behavioral type of a track creature.
/// Typical mapping: Passive = bug, Scared = critter, Aggressive = beast.
/// </summary>
public enum CreatureBehaviorType
{
    /// <summary>
    /// Bug: low reactivity; bug-style idle; flees from critters (Scared); dodges obstacles.
    /// </summary>
    Passive,

    /// <summary>
    /// Critter: on-road spawn/idle; sprints down the course from the player; old off-road flee from the beast.
    /// </summary>
    Scared,

    /// <summary>
    /// Beast: hunts critters, player, and NPC traffic; bull rush vs player when configured.
    /// </summary>
    Aggressive,

    /// <summary>
    /// Gorilla: idles on hills, seeks nearby environment props, lifts and throws them at the player.
    /// </summary>
    Thrower
}