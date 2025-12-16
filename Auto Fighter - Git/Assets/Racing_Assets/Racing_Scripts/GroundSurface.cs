using UnityEngine;

public enum SurfaceType
{
    Default,
    Grass,
    Dirt,
    Ice
}

[DisallowMultipleComponent]
public class GroundSurface : MonoBehaviour
{
    [Header("Type (optional, for future logic)")]
    public SurfaceType surfaceType = SurfaceType.Default;

    [Header("Physics Multipliers")]
    [Tooltip("Multiply the car's max speed by this value.")]
    public float maxSpeedMultiplier = 1f;

    [Tooltip("Multiply the car's acceleration by this value.")]
    public float accelerationMultiplier = 1f;

    [Tooltip("Multiply the car's steering turn speed by this value.")]
    public float turnSpeedMultiplier = 1f;

    [Tooltip("Multiply the car's drag by this value.")]
    public float dragMultiplier = 1f;

    // NEW: Ice-specific physics material properties
    [Header("Ice Properties (when surfaceType = Ice)")]
    [Tooltip("Dynamic friction multiplier applied to car's physic material (0-1, lower = more slippery).")]
    [Range(0f, 1f)]
    public float iceDynamicFrictionMultiplier = 0.15f;

    [Tooltip("Static friction multiplier applied to car's physic material (0-1, lower = more slippery).")]
    [Range(0f, 1f)]
    public float iceStaticFrictionMultiplier = 0.1f;

    [Tooltip("How ice affects rotation vs velocity alignment (0 = slides straight, 1 = normal grip). Works like drift physics.")]
    [Range(0f, 1f)]
    public float iceHandlingMultiplier = 0.3f;
}