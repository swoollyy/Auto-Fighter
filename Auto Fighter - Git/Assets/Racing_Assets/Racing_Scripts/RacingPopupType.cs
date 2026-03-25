public enum RacingPopupType
{
    // Damage / Loss
    HPDamage,
    FuelLoss,
    CoinLoss,

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

    // Beast / predator eating another creature ("NOM NOM", "OM NOM NOM", etc. — style asset)
    BeastEat,

    // Invincibility
    Invincible,

    // Near Miss / Close Call
    NearMiss,

    // Currency
    SprocketGain,

    // Generic
    Generic,
    Warning,
    Critical,

    // Mash recovery: per manual click strength (create RacingPopupStyleSO + register on RacingPopupSystem).
    MashClickDamage,
}
