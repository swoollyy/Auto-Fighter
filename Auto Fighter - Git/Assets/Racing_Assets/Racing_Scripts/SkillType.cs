public enum SkillType
{
    // ---- Existing core car stats ----
    Acceleration = 0,
    MaxSpeed = 1,
    FuelEfficiency = 2,
    SteeringResponsiveness = 3,

    TurretUnlock,
    DriftUnlock,

    // ---- New additive/multiplicative chains for core stats ----
    // Acceleration
    Acceleration_Add,
    Acceleration_Mul,

    // Max speed
    MaxSpeed_Add,
    MaxSpeed_Mul,

    // Fuel-related stats
    MaxFuel_Add,
    MaxFuel_Mul,
    IdleFuelUse_Add,
    IdleFuelUse_Mul,
    DrivingFuelUse_Add,
    DrivingFuelUse_Mul,

    // Turning (this already existed; just leaving it here for context)
    TurnSpeed_Add,
    TurnSpeed_Mul,

    // Turret stats (you already had these)
    TurretDamage_Add,
    TurretDamage_Mul,
    TurretProjectileSpeed_Add,
    TurretProjectileSpeed_Mul,
    TurretCooldown_Add,
    TurretCooldown_Mul,
    TurretBulletLifetime_Add,
    TurretBulletLifetime_Mul,
    TurretConeAngle_Add,
    TurretConeAngle_Mul,
    TurretScanRadius_Add,
    TurretScanRadius_Mul,

    CoinSpawnRate_Add,
    CoinSpawnRate_Mul,

    CoinDoubleChance_Add,
    CoinDoubleChance_Mul,

    FuelPickupUnlock,            // gate spawning (level > 0 enables)
    FuelPickupSpawnRate_Add,     // spawn chance scaling (add chain)
    FuelPickupSpawnRate_Mul,     // spawn chance scaling (mul chain)
    FuelPickupAmount_Add,        // amount scaling (add chain) – base is 15
    FuelPickupAmount_Mul,         // amount scaling (mul chain)

    HPPickupUnlock,
    HPPickupSpawnRate_Add,
    HPPickupSpawnRate_Mul,
    HPPickupAmount_Add,
    HPPickupAmount_Mul
}


public enum SkillApplicationMode
{
    Additive,
    Multiplicative
}