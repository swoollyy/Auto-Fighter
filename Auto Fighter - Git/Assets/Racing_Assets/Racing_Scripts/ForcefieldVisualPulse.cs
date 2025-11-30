using UnityEngine;

[DisallowMultipleComponent]
public class ForcefieldVisualPulse : MonoBehaviour
{
    [SerializeField] private float pulseSpeed = 2f;
    [SerializeField] private float pulseScale = 0.04f;

    private Vector3 _baseScale;

    void Awake() => _baseScale = transform.localScale;

    void Update()
    {
        float s = 1f + Mathf.Sin(Time.time * Mathf.PI * 2f * pulseSpeed) * pulseScale;
        transform.localScale = _baseScale * s;
    }
}