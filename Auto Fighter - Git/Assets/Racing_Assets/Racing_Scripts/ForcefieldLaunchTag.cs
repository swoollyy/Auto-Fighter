using UnityEngine;

[DisallowMultipleComponent]
public sealed class ForcefieldLaunchTag : MonoBehaviour
{
    [Tooltip("Seconds this tag is considered 'recently launched'.")]
    [SerializeField] private float activeSeconds = 3.0f;

    private float _expiresAt;

    public bool IsActive => Time.time < _expiresAt;

    public void Arm(float duration)
    {
        _expiresAt = Time.time + Mathf.Max(0f, duration);
    }
}