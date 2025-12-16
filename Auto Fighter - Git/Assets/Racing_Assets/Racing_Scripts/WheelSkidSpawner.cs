using UnityEngine;

public class WheelSkidSpawner : MonoBehaviour
{
    [System.Serializable]
    private class WheelData
    {
        public Transform point;          // where the wheel is
        [HideInInspector] public Vector3 lastPos;
        [HideInInspector] public bool hasLast;
    }

    [Header("Setup")]
    [SerializeField] private WheelData[] wheels;
    [SerializeField] private GameObject skidSegmentPrefab;
    [SerializeField] private LayerMask roadLayer;
    [SerializeField] private float rayLength = 2f;

    [Header("Skid Settings")]
    [SerializeField] private float minSegmentDistance = 0.2f;  // spacing between quads
    [SerializeField] private float skidLifetime = 5f;

    private void LateUpdate()
    {
        if (skidSegmentPrefab == null) return;

        foreach (var w in wheels)
        {
            if (w.point == null) continue;

            Vector3 origin = w.point.position;
            Vector3 dir = Vector3.down;

            if (Physics.Raycast(origin, dir, out RaycastHit hit, rayLength, roadLayer))
            {
                Vector3 contactPos = hit.point + hit.normal * 0.01f; // avoid z-fighting

                if (!w.hasLast || Vector3.Distance(w.lastPos, contactPos) >= minSegmentDistance)
                {
                    SpawnSkidSegment(w, contactPos, hit.normal);
                    w.lastPos = contactPos;
                    w.hasLast = true;
                }
            }
            else
            {
                w.hasLast = false;
            }
        }
    }

    private void SpawnSkidSegment(WheelData w, Vector3 position, Vector3 normal)
    {
        // Project the car's forward direction onto the road surface
        Vector3 forwardOnPlane = Vector3.ProjectOnPlane(transform.forward, normal).normalized;

        // Fallback if projection degenerates (e.g., straight up)
        if (forwardOnPlane.sqrMagnitude < 0.0001f)
            forwardOnPlane = Vector3.Cross(normal, transform.right);

        Quaternion orient = Quaternion.LookRotation(normal, -forwardOnPlane);

        GameObject seg = Instantiate(skidSegmentPrefab, position, orient);

        // Optionally tweak lifetime
        SkidMarkSegment sms = seg.GetComponent<SkidMarkSegment>();
        if (sms != null)
        {
            sms.lifetime = skidLifetime;
        }
    }
}
