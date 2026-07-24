using UnityEngine;

/// <summary>
/// Shared HUD/popup number display: integers only.
/// Rounds normally; any positive amount below 0.5 (but not zero) shows as 1.
/// </summary>
public static class RacingUiNumberFormat
{
    public static int ToDisplayInt(float value)
    {
        if (value > 0f && value < 0.5f)
            return 1;
        if (value < 0f && value > -0.5f)
            return -1;
        return Mathf.RoundToInt(value);
    }

    public static string ToDisplayString(float value) => ToDisplayInt(value).ToString();
}
