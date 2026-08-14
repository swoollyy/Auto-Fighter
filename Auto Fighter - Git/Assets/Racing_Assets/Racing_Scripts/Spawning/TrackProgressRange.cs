using UnityEngine;

/// <summary>
/// Progress-based value helpers for track spawners.
/// Inspector Vector2 convention: <b>X = at track start</b>, <b>Y = at track end</b>.
/// </summary>
public static class TrackProgressRange
{
    /// <summary>Lerp from range.x (start) to range.y (end) by normalized track progress 0–1.</summary>
    public static float Lerp(Vector2 startToEnd, float normalizedProgress)
    {
        return Mathf.Lerp(startToEnd.x, startToEnd.y, Mathf.Clamp01(normalizedProgress));
    }

    /// <summary>Same as <see cref="Lerp"/>, then clamped to [0, 1] (spawn chances, accuracy, etc.).</summary>
    public static float Lerp01(Vector2 startToEnd, float normalizedProgress)
    {
        return Mathf.Clamp01(Lerp(startToEnd, normalizedProgress));
    }

    public static float NormalizedDistance(float distAlongTrack, float totalLength)
    {
        return totalLength > 1e-4f ? Mathf.Clamp01(distAlongTrack / totalLength) : 0f;
    }
}
