/// <summary>
/// Types of popup text that can be displayed in the racing game.
/// Add new types here as needed for future functionality.
/// </summary>
public enum RacingPopupType
{
    // Damage / Loss
    HPDamage,           // HP lost from crash
    FuelLoss,           // Fuel lost from crash

    // Gains / Recovery
    HPGain,             // HP recovered (pickup, regen)
    FuelGain,           // Fuel recovered (pickup, mash reward)
    CoinGain,           // Coins collected

    // Mash Recovery specific
    MashFuelReward,     // Fuel gained per mash click
    MashClickBonus,     // Multi-click bonus display

    // Boost / Speed
    BoostActivate,      // Boost activated
    SpeedBonus,         // Speed bonus text

    // Combo / Multiplier
    ComboText,          // Combo counter
    MultiplierText,     // Score multiplier

    // Collision / Impact (covers crash, invincibility bump, etc.)
    Crash,              // Cartoony impact text - "WAPOW!", "KABLAM!", "CRASH!", etc.

    // Invincibility
    Invincible,         // "INVINCIBLE!" when invincibility activates

    // Near Miss / Close Call
    NearMiss,           // Close call with obstacle

    // Currency
    SprocketGain,

    // Generic
    Generic,            // Generic popup (white, neutral)
    Warning,            // Warning text (orange/yellow)
    Critical            // Critical/important (red, large)
}