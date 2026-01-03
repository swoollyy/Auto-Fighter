using System;
using UnityEngine;

/// <summary>
/// Interface for the racing popup system.
/// Allows static access via RacingPopups facade.
/// </summary>
public interface IRacingPopupSystem
{
    /// <summary>
    /// Spawn a popup at a world position.
    /// </summary>
    void Spawn(RacingPopupType type, float value, Vector3 worldPosition);

    /// <summary>
    /// Spawn a popup at a world position with custom text.
    /// </summary>
    void Spawn(RacingPopupType type, string text, Vector3 worldPosition);

    /// <summary>
    /// Spawn a popup with color override.
    /// </summary>
    void Spawn(RacingPopupType type, float value, Vector3 worldPosition, Color colorOverride);

    /// <summary>
    /// Spawn a popup with full customization.
    /// </summary>
    void Spawn(RacingPopupType type, float value, Vector3 worldPosition, Color? colorOverride, float? scaleOverride);

    /// <summary>
    /// Spawn at random screen position (for mash rewards, etc.)
    /// </summary>
    void SpawnRandomScreen(RacingPopupType type, float value, Vector2 horizontalRange, Vector2 verticalRange);

    /// <summary>
    /// Spawn a coin popup with separate text color and outline color.
    /// </summary>
    void SpawnCoin(int value, Vector3 worldPosition, Color textColor, Color outlineColor);
}

public static class RacingPopups
{
    public static IRacingPopupSystem System { get; private set; }

    public static bool IsReady => System != null;

    public static void Register(IRacingPopupSystem system) => System = system;
    public static void Unregister(IRacingPopupSystem system)
    {
        if (System == system) System = null;
    }

    // Convenience methods
    public static void Spawn(RacingPopupType type, float value, Vector3 position)
    {
        System?.Spawn(type, value, position);
    }

    public static void Spawn(RacingPopupType type, string text, Vector3 position)
    {
        System?.Spawn(type, text, position);
    }

    public static void Spawn(RacingPopupType type, float value, Vector3 position, Color color)
    {
        System?.Spawn(type, value, position, color);
    }

    /// <summary>
    /// Spawn a crash popup with severity-based text.
    /// </summary>
    public static void Crash(float severity, Vector3 position)
    {
        string text;
        if (severity >= 0.8f)
            text = "MASSIVE CRASH!";
        else if (severity >= 0.5f)
            text = "CRASH!";
        else
            text = "BUMP!";

        Spawn(RacingPopupType.Crash, text, position);
    }

    /// <summary>
    /// Spawn a close call popup with distance-based text.
    /// </summary>
    public static void CloseCall(float distance, Vector3 position)
    {
        string text;
        if (distance <= 0.5f)
            text = "INSANE DODGE!";
        else if (distance <= 1.5f)
            text = "CLOSE CALL!";
        else
            text = "NEAR MISS!";

        Spawn(RacingPopupType.NearMiss, text, position);
    }

    // === SHORTCUT METHODS FOR COMMON POPUPS ===

    public static void HPDamage(float amount, Vector3 position)
        => Spawn(RacingPopupType.HPDamage, amount, position);

    public static void HPGain(float amount, Vector3 position)
        => Spawn(RacingPopupType.HPGain, amount, position);

    public static void FuelLoss(float amount, Vector3 position)
        => Spawn(RacingPopupType.FuelLoss, amount, position);

    public static void FuelGain(float amount, Vector3 position)
        => Spawn(RacingPopupType.FuelGain, amount, position);

    public static void Crash(Vector3 position)
    => Spawn(RacingPopupType.Crash, "CRASH!", position);

    public static void CoinGain(int amount, Vector3 position)
        => Spawn(RacingPopupType.CoinGain, amount, position);

    public static void MashFuel(float amount, Vector3 position)
        => Spawn(RacingPopupType.MashFuelReward, amount, position);

    public static void NearMiss(Vector3 position)
        => Spawn(RacingPopupType.NearMiss, "NEAR MISS!", position);

    public static void Generic(string text, Vector3 position)
        => Spawn(RacingPopupType.Generic, text, position);

    public static void Warning(string text, Vector3 position)
        => Spawn(RacingPopupType.Warning, text, position);

    public static void SpawnRandomScreen(RacingPopupType type, float value, Vector2 horizontalRange, Vector2 verticalRange)
        => System?.SpawnRandomScreen(type, value, horizontalRange, verticalRange);

    public static void MashFuelRandom(float amount)
        => SpawnRandomScreen(RacingPopupType.MashFuelReward, amount, new Vector2(-3f, 3f), new Vector2(-1.5f, 1.5f));

    public static void SprocketGain(int amount, Vector3 position)
    => Spawn(RacingPopupType.SprocketGain, amount, position);

    /// <summary>
    /// Spawn a coin popup with separate text color and outline color.
    /// </summary>
    public static void SpawnCoin(int value, Vector3 position, Color textColor, Color outlineColor)
        => System?.SpawnCoin(value, position, textColor, outlineColor);
}