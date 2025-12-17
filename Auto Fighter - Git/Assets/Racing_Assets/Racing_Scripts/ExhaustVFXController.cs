using UnityEngine;

[DisallowMultipleComponent]
public class ExhaustVFXController : MonoBehaviour
{
    [Header("Particle Systems (exhausts)")]
    [SerializeField] private ParticleSystem leftExhaust;
    [SerializeField] private ParticleSystem rightExhaust;

    [Header("Speed → Start Speed")]
    [Tooltip("Below this car speed, exhaust barely emits / is very slow.")]
    [SerializeField] private float minSpeedForSmoke = 0.5f;

    [Tooltip("At or above this speed, exhaust uses max start speed.")]
    [SerializeField] private float maxSpeedForFullBoost = 20f;

    [SerializeField] private float minStartSpeed = 0.5f;
    [SerializeField] private float maxStartSpeed = 4f;

    [Header("Speed → Emission Rate (optional)")]
    [SerializeField] private float minEmissionRate = 5f;
    [SerializeField] private float maxEmissionRate = 40f;

    private CarController car;

    private void Awake()
    {
        // CarController can be on this GameObject or a parent
        car = GetComponent<CarController>();
        if (car == null)
            car = GetComponentInParent<CarController>();
    }

    private void LateUpdate()
    {
        if (car == null)
            return;

        float speed = car.CurrentSpeed;

        // Decide if we should emit at all
        bool shouldEmit = speed > minSpeedForSmoke;

        float t = Mathf.InverseLerp(minSpeedForSmoke, maxSpeedForFullBoost, speed);

        UpdateExhaust(leftExhaust, shouldEmit, t);
        UpdateExhaust(rightExhaust, shouldEmit, t);
    }

    private void UpdateExhaust(ParticleSystem ps, bool shouldEmit, float t)
    {
        if (ps == null)
            return;

        var emission = ps.emission;
        emission.enabled = shouldEmit;

        if (!shouldEmit)
            return;

        // Lerp startSpeed based on car speed
        var main = ps.main;
        main.startSpeed = Mathf.Lerp(minStartSpeed, maxStartSpeed, t);

        // Optional: more speed → more emission
        float rate = Mathf.Lerp(minEmissionRate, maxEmissionRate, t);
        emission.rateOverTime = rate;
    }
}
