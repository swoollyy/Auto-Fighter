using UnityEngine;

/// <summary>
/// Pins a prop spawned inside terrain so physics hits cannot shove it through the mesh.
/// </summary>
public class TerrainEmbeddedAnchor : MonoBehaviour
{
    private Vector3 _worldPos;
    private Quaternion _worldRot;
    private bool _captured;

    public static bool IsLocked(Component c)
    {
        return c != null && c.GetComponentInParent<TerrainEmbeddedAnchor>() != null;
    }

    public static bool IsLocked(GameObject go)
    {
        return go != null && go.GetComponentInParent<TerrainEmbeddedAnchor>() != null;
    }

    public static void Attach(GameObject go)
    {
        if (go == null) return;

        TerrainEmbeddedAnchor anchor = go.GetComponent<TerrainEmbeddedAnchor>();
        if (anchor == null)
            anchor = go.AddComponent<TerrainEmbeddedAnchor>();
        anchor.CaptureAndLock();
    }

    private void Awake()
    {
        CaptureAndLock();
    }

    private void OnEnable()
    {
        CaptureAndLock();
    }

    public void CaptureAndLock()
    {
        _worldPos = transform.position;
        _worldRot = transform.rotation;
        _captured = true;
        ApplyLock();
    }

    private void FixedUpdate()
    {
        if (!_captured) return;

        if (transform.position != _worldPos || transform.rotation != _worldRot)
            transform.SetPositionAndRotation(_worldPos, _worldRot);

        ApplyLock();
    }

    private void ApplyLock()
    {
        Rigidbody[] rbs = GetComponentsInChildren<Rigidbody>(true);
        for (int i = 0; i < rbs.Length; i++)
        {
            Rigidbody rb = rbs[i];
            if (rb == null) continue;

            rb.isKinematic = true;
            rb.useGravity = false;
            rb.constraints = RigidbodyConstraints.FreezeAll;
            rb.velocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            rb.position = rb.transform.position;
            rb.rotation = rb.transform.rotation;
        }
    }
}
