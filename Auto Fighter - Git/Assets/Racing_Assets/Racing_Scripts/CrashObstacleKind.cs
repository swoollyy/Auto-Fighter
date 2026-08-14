/// <summary>
/// Every obstacle / system that can contribute to a player-car crash severity.
/// Resolved automatically from components on the obstacle, or overridden via <see cref="CrashObstacleIdentity"/>.
/// </summary>
public enum CrashObstacleKind
{
    Unknown = 0,

    /// <summary>Idle track props: rocks, trees, debris (no specialized script).</summary>
    TrackPropStatic = 1,

    /// <summary><see cref="RacingObstacle"/> or generic destructible track object.</summary>
    RacingObstacle = 2,

    /// <summary><see cref="ShuttleTrackObstacle"/>.</summary>
    Shuttle = 3,

    /// <summary><see cref="CrossTrackObstacle"/>.</summary>
    CrossTrack = 4,

    /// <summary><see cref="TrackObstacleBounceBack"/>.</summary>
    BounceBack = 5,

    /// <summary><see cref="ThrownObstacle"/> (impact / explosion).</summary>
    ThrownObstacle = 6,

    /// <summary><see cref="NPCTrafficCar"/> (standard weight).</summary>
    NpcTrafficCar = 7,

    /// <summary><see cref="NPCTrafficCar"/> with heavy profile enabled.</summary>
    NpcTrafficCarBig = 8,

    /// <summary>Passive or scared <see cref="TrackCreature"/> (rarely causes full crash).</summary>
    TrackCreaturePassive = 9,

    /// <summary>Aggressive / charging <see cref="TrackCreature"/>.</summary>
    TrackCreatureAggressive = 10,

    /// <summary><see cref="RollingLogAlongTrack"/> (scripted roll or free physics).</summary>
    RollingLog = 11,

    /// <summary><see cref="TrackSideShooterObstacle"/> roadside turret.</summary>
    SideShooter = 12,
}
