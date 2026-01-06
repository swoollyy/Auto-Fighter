public enum SkillType
{
    // ---- Existing core car stats ----
    Acceleration = 0,
    MaxSpeed = 1,
    FuelEfficiency = 2,
    SteeringResponsiveness = 3,

    TurretUnlock,
    DriftUnlock,

    // ---- NEW: Drift Boost Skill Unlock & Stat Chains ----
    BoostUnlock,                 // unlocks space‑bar boost
    BoostForce_Add,              // scales both boostForce and boostSustainAcceleration (add)
    BoostForce_Mul,              // scales both boostForce and boostSustainAcceleration (mul)
    BoostDuration_Add,
    BoostDuration_Mul,
    BoostMaxSpeedMult_Add,
    BoostMaxSpeedMult_Mul,
    BoostCooldown_Add,
    BoostCooldown_Mul,
    BoostFuelCost_Add,
    BoostFuelCost_Mul,

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
    HPPickupAmount_Mul,

    ForcefieldUnlock = 4100,


    // cooldown
    ForcefieldCooldown_Add = 4120,
    ForcefieldCooldown_Mul = 4121,

    // knockback (single pair applied to both away and up)
    ForcefieldKnockback_Add = 4130,
    ForcefieldKnockback_Mul = 4131,

    ForcefieldImpactDamageUnlock = 4200,

    // NEW: impact damage amount scaling (base is 1.0)
    ForcefieldImpactDamage_Add = 4201,
    ForcefieldImpactDamage_Mul = 4202,

    // ------------------------------------------------------------------------
    // NEW: Drift‑Held Boost Skill (unlock + stat chains)
    // ------------------------------------------------------------------------
    DriftHeldBoostUnlock = 4300,
    DriftHeldBoostForce_Add = 4310,
    DriftHeldBoostForce_Mul = 4311,
    DriftHeldBoostDuration_Add = 4320,
    DriftHeldBoostDuration_Mul = 4321,
    DriftHeldBoostMaxSpeedMult_Add = 4330,
    DriftHeldBoostMaxSpeedMult_Mul = 4331,
    DriftHeldBoostCooldown_Add = 4340,
    DriftHeldBoostCooldown_Mul = 4341,


    // ------------------------------------------------------------------------
    // NEW: Distance coins per meter skill chains
    // ------------------------------------------------------------------------
    DistanceCoinsPerMeter_Add = 4400,
    DistanceCoinsPerMeter_Mul = 4401,

    CoinBase_Add = 4450,
    CoinBase_Mul = 4451,

    HPRegen_Add = 4500,
    HPRegen_Mul = 4501,

    // ------------------------------------------------------------------------
    // NEW: Crash Recovery Mash Skills
    // ------------------------------------------------------------------------
    MashClicksPerClick_Add = 4600,      // Each button press counts as multiple clicks
    MashClicksPerClick_Mul = 4601,

    MashPassiveUnlock = 4605,

    MashPassiveClickRate_Add = 4610,    // Auto-clicks per second (passive)
    MashPassiveClickRate_Mul = 4611,

    MashPassiveClickStrength_Add = 4620, // How many clicks each passive tick provides
    MashPassiveClickStrength_Mul = 4621,

    MashFuelPerClick_Add = 4630,        // Fuel reward per click (bonus)
    MashFuelPerClick_Mul = 4631,

    MaxHP_Add = 4700,
    MaxHP_Mul = 4701,

    CloseCallCoins_Add = 4800,              // Coins earned per close call (also acts as unlock)
    CloseCallCoins_Mul = 4801,

    CloseCallSpeedBoostUnlock = 4810,       // Unlock speed boost on close call

    CloseCallSpeedBoostDuration_Add = 4820, // Duration of speed boost
    CloseCallSpeedBoostDuration_Mul = 4821,

    CloseCallInvincibility_Add = 4830,      // Invincibility duration (also acts as unlock)
    CloseCallInvincibility_Mul = 4831,
}


public enum SkillApplicationMode
{
    Additive,
    Multiplicative
}