public enum RacingPopupType
{
    // Damage / Loss
    HPDamage,
    FuelLoss,

    // Gains / Recovery
    HPGain,
    FuelGain,
    CoinGain,

    // Mash Recovery specific
    MashFuelReward,
    MashClickBonus,

    // Boost / Speed
    BoostActivate,
    SpeedBonus,

    // Combo / Multiplier
    ComboText,
    MultiplierText,

    // Collision / Impact
    Crash,

    // NEW: Creature run-over impact text ("SPLAT!!", "WHAM!!", etc.)
    CreatureSplat,

    // Invincibility
    Invincible,

    // Near Miss / Close Call
    NearMiss,

    // Currency
    SprocketGain,

    // Generic
    Generic,
    Warning,
    Critical
}
