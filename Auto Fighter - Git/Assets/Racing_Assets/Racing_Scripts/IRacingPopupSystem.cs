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

    /// <summary>
    /// Spawn at the given world position and billboard to the camera, ignoring camera-fixed HUD placement when that mode is enabled on the popup component.
    /// </summary>
    void SpawnWorldSpace(RacingPopupType type, float value, Vector3 worldPosition);

    /// <summary>
    /// Same as <see cref="SpawnWorldSpace(RacingPopupType, float, Vector3)"/> with custom text.
    /// </summary>
    void SpawnWorldSpace(RacingPopupType type, string text, Vector3 worldPosition);
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



    // === CRASH / IMPACT POPUPS ===

    /// <summary>
    /// Spawn a crash popup. Uses random text from the Crash style asset.
    /// Pass 0 for value to trigger random text selection from the style.
    /// </summary>
    public static void Crash(Vector3 position)
        => Spawn(RacingPopupType.Crash, 0f, position);

    /// <summary>
    /// Spawn a crash popup with custom text override.
    /// </summary>
    public static void Crash(string text, Vector3 position)
        => Spawn(RacingPopupType.Crash, text, position);


    public static void Crash(float severity, Vector3 position)
=> Spawn(RacingPopupType.Crash, severity, position);
    // === INVINCIBILITY POPUPS ===

    /// <summary>
    /// Spawn "INVINCIBLE!" popup when invincibility activates.
    /// </summary>
    public static void Invincible(Vector3 position)
        => Spawn(RacingPopupType.Invincible, "INVINCIBLE!", position);

    // === CLOSE CALL / NEAR MISS ===

    /// <summary>
    /// Spawn a close call popup with distance-based text.
    /// </summary>
    public static void CloseCall(float distance, Vector3 position)
    {
        string text;
        if (distance <= .15f)
            text = "INSANE DODGE!";
        else if (distance <= .3f)
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

    public static void CoinGain(int amount, Vector3 position)
        => Spawn(RacingPopupType.CoinGain, amount, position);

    public static void MashFuel(float amount, Vector3 position)
        => Spawn(RacingPopupType.MashFuelReward, amount, position);

    public static void NearMiss(Vector3 position)
        => Spawn(RacingPopupType.NearMiss, "NEAR MISS!", position);

    // === BOOST POPUPS (separate styles for drift boost vs boost pad/ramp) ===

    /// <summary>Drift-release boost popup. Uses the BoostActivate style asset.</summary>
    public static void DriftBoost(Vector3 position)
        => Spawn(RacingPopupType.BoostActivate, 0f, position);

    /// <summary>Boost pad / ramp surface popup. Uses the BoostPad style asset.</summary>
    public static void BoostPad(Vector3 position)
        => Spawn(RacingPopupType.BoostPad, 0f, position);

    public static void Generic(string text, Vector3 position)
        => Spawn(RacingPopupType.Generic, text, position);

    public static void Warning(string text, Vector3 position)
        => Spawn(RacingPopupType.Warning, text, position);

    public static void SpawnRandomScreen(RacingPopupType type, float value, Vector2 horizontalRange, Vector2 verticalRange)
        => System?.SpawnRandomScreen(type, value, horizontalRange, verticalRange);

    public static void MashFuelRandom(float amount)
        => SpawnRandomScreen(RacingPopupType.MashFuelReward, amount, new Vector2(-3f, 3f), new Vector2(-1.5f, 1.5f));

    public static void MashClickDamageRandom(float clickStrength)
        => SpawnRandomScreen(RacingPopupType.MashClickDamage, clickStrength, new Vector2(-3f, 3f), new Vector2(-1.5f, 1.5f));

    public static void SprocketGain(int amount, Vector3 position)
        => Spawn(RacingPopupType.SprocketGain, amount, position);

    public static void CoinLoss(int amount, Vector3 position)
        => Spawn(RacingPopupType.CoinLoss, amount, position);

    /// <summary>
    /// Spawn a coin popup with separate text color and outline color.
    /// </summary>
    public static void SpawnCoin(int value, Vector3 position, Color textColor, Color outlineColor)
        => System?.SpawnCoin(value, position, textColor, outlineColor);

    public static void SpawnWorldSpace(RacingPopupType type, float value, Vector3 position)
        => System?.SpawnWorldSpace(type, value, position);

    public static void SpawnWorldSpace(RacingPopupType type, string text, Vector3 position)
        => System?.SpawnWorldSpace(type, text, position);

    /// <summary>Crash-style popup at a world hit point (not fixed in front of the camera).</summary>
    public static void CrashWorld(Vector3 position)
        => SpawnWorldSpace(RacingPopupType.Crash, 0f, position);

    public static void CrashWorld(string text, Vector3 position)
        => SpawnWorldSpace(RacingPopupType.Crash, text, position);

    public static void CrashWorld(float severity, Vector3 position)
        => SpawnWorldSpace(RacingPopupType.Crash, severity, position);
}