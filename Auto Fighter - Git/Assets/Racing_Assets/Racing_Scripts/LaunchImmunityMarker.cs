using UnityEngine;

[DisallowMultipleComponent]
public sealed class LaunchImmunityMarker : MonoBehaviour
{
    public float expiresAt { get; private set; }

    public bool IsImmune => Time.time < expiresAt;

    public void Activate(float durationSeconds)
    {
        expiresAt = Time.time + Mathf.Max(0f, durationSeconds);
    }
}