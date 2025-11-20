using UnityEngine;

public class CameraFollow : MonoBehaviour
{
    [Header("Follow Target")]
    [SerializeField] public Transform target;

    [Header("Camera Settings")]
    [SerializeField] private Vector3 offset = new Vector3(0, 8, -12);
    [SerializeField, Range(5f, 45f)] private float cameraPitch = 20f;

    [Header("Smoothing")]
    [SerializeField] private float positionFollowSpeed = 8f;
    [SerializeField] private float rotationFollowSpeed = 8f;
    [Tooltip("How quickly the *camera's forward* catches up to the car's forward.\nLower = more lag, more looseness.")]
    [SerializeField] private float rotationLag = 4f;

    // Internal smoothed direction so camera doesn't hard-lock to target.forward
    private Vector3 smoothedForward = Vector3.zero;

    private void LateUpdate()
    {
        if (target == null) return;

        // -------------------------
        // POSITION FOLLOW
        // -------------------------
        Vector3 desiredPos = target.position + target.TransformDirection(offset);
        transform.position = Vector3.Lerp(
            transform.position,
            desiredPos,
            positionFollowSpeed * Time.deltaTime
        );

        // -------------------------
        // ROTATION LAG (YAW)
        // -------------------------
        // We only care about horizontal forward to avoid weird vertical tilts
        Vector3 targetForwardFlat = target.forward;
        targetForwardFlat.y = 0f;

        if (targetForwardFlat.sqrMagnitude < 0.0001f)
        {
            targetForwardFlat = Vector3.forward; // safe fallback
        }
        targetForwardFlat.Normalize();

        // Initialize smoothedForward once
        if (smoothedForward == Vector3.zero)
        {
            smoothedForward = targetForwardFlat;
        }

        // This is the "rubber band" part: camera forward slowly catches up
        smoothedForward = Vector3.Slerp(
            smoothedForward,
            targetForwardFlat,
            rotationLag * Time.deltaTime
        );

        // Build yaw from smoothed direction
        Quaternion yawOnly = Quaternion.LookRotation(smoothedForward, Vector3.up);

        // Extract Euler and force our custom pitch
        Vector3 e = yawOnly.eulerAngles;
        e.x = cameraPitch; // fixed downward tilt

        Quaternion desiredRot = Quaternion.Euler(e);

        // Smoothly rotate camera toward desired
        transform.rotation = Quaternion.Slerp(
            transform.rotation,
            desiredRot,
            rotationFollowSpeed * Time.deltaTime
        );
    }

    public void SetTarget(Transform t)
    {
        target = t;
        smoothedForward = Vector3.zero; // re-init on new target
    }
}
