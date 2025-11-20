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
}
