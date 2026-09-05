using UnityEngine;

/// <summary>
/// Tracks a target's world position only. Rotation stays locked so a particle
/// emitter can ride with the car without tumbling when the car turns or crashes.
/// </summary>
[DisallowMultipleComponent]
public class FollowTargetPosition : MonoBehaviour
{
    [SerializeField] private Transform target;
    [SerializeField] private Vector3 worldOffset;
    [SerializeField] private bool autoFindCar = true;
    [Tooltip("Keep the authored world rotation. Off = identity.")]
    [SerializeField] private bool lockWorldRotation = true;

    private Quaternion _lockedRotation = Quaternion.identity;

    public void SetTarget(Transform follow)
    {
        target = follow;
    }

    private void Awake()
    {
        _lockedRotation = lockWorldRotation ? transform.rotation : Quaternion.identity;
        if (target == null)
            TryFindCar();
    }

    private void LateUpdate()
    {
        if (target == null && autoFindCar)
            TryFindCar();

        if (target == null)
            return;

        transform.SetPositionAndRotation(target.position + worldOffset, _lockedRotation);
    }

    private void TryFindCar()
    {
        if (GameManager_Racing.Instance != null && GameManager_Racing.Instance.ActiveCar != null)
        {
            target = GameManager_Racing.Instance.ActiveCar.transform;
            return;
        }

        var car = FindObjectOfType<CarController>();
        if (car != null)
            target = car.transform;
    }
}
