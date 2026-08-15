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
    /// Critter: flees player, NPC traffic, and beast; can hunt bugs (Passive).
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