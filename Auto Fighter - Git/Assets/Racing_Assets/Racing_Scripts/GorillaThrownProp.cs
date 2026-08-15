using UnityEngine;

/// <summary>
/// Marks an environment prop that a gorilla has claimed and thrown.
/// Handles physics launch and applying crash damage if it hits the player car.
/// </summary>
public class GorillaThrownProp : MonoBehaviour
{
    private float _crashSeverity;
    private float _knockback;
    private float _lift;
    private float _torque;
    private bool _hasHitPlayer;
    private Rigidbody _rb;

    public void Launch(
        Vector3 velocity,
        float angularSpeed,
        float lifetime,
        int layer,
        float crashSeverity,
        float knockback,
        float lift,
        float torque)
    {
        _crashSeverity = Mathf.Clamp01(crashSeverity);
        _knockback = Mathf.Max(0f, knockback);
        _lift = Mathf.Max(0f, lift);
        _torque = Mathf.Max(0f, torque);
        _hasHitPlayer = false;

        if (layer >= 0 && layer <= 31)
            SetLayerRecursively(transform, layer);

        _rb = GetComponent<Rigidbody>();
        if (_rb == null)
            _rb = gameObject.AddComponent<Rigidbody>();

        _rb.isKinematic = false;
        _rb.useGravity = true;
        _rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        _rb.interpolation = RigidbodyInterpolation.Interpolate;
        _rb.velocity = velocity;
        _rb.angularVelocity = Random.onUnitSphere * Mathf.Max(0f, angularSpeed);
        _rb.WakeUp();

        Collider[] cols = GetComponentsInChildren<Collider>(true);
        for (int i = 0; i < cols.Length; i++)
        {
            if (cols[i] == null) continue;
            cols[i].enabled = true;
            cols[i].isTrigger = false;
        }

        if (lifetime > 0.05f)
            Destroy(gameObject, lifetime);
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (_hasHitPlayer || collision == null)
            return;

        var car = collision.collider.GetComponentInParent<CarController>();
        if (car == null)
            return;

        _hasHitPlayer = true;

        Vector3 hitDir = collision.relativeVelocity;
        hitDir.y = 0f;
        if (hitDir.sqrMagnitude < 0.01f)
            hitDir = car.transform.position - transform.position;
        hitDir.y = 0f;
        if (hitDir.sqrMagnitude < 0.01f)
            hitDir = transform.forward;
        hitDir.Normalize();

        Vector3 contact = collision.contactCount > 0
            ? collision.GetContact(0).point
            : transform.position;

        float impactSpeed = collision.relativeVelocity.magnitude;
        car.ApplyExternalCrashDamage(
            hitDir,
            impactSpeed,
            contact,
            _crashSeverity,
            transform,
            _rb,
            collision.contactCount > 0 ? collision.GetContact(0).normal : (Vector3?)null,
            1f);

        Rigidbody carRb = car.GetComponent<Rigidbody>();
        if (carRb != null)
        {
            Vector3 forceDir = hitDir + Vector3.up * (_lift / Mathf.Max(1f, _knockback));
            if (forceDir.sqrMagnitude > 1e-6f)
                forceDir.Normalize();
            if (_knockback > 0f)
                carRb.AddForce(forceDir * _knockback, ForceMode.VelocityChange);
            if (_torque > 0f)
                carRb.AddTorque(Vector3.up * _torque * Mathf.Sign(Random.value - 0.5f), ForceMode.VelocityChange);
        }
    }

    private static void SetLayerRecursively(Transform root, int layer)
    {
        if (root == null) return;
        root.gameObject.layer = layer;
        for (int i = 0; i < root.childCount; i++)
            SetLayerRecursively(root.GetChild(i), layer);
    }
}
