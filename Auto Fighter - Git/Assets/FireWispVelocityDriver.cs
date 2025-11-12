using UnityEngine;

[RequireComponent(typeof(Renderer))]
public class FireVelocityFeeder : MonoBehaviour
{
    public int materialIndex = 1;      // fire material slot
    public bool useRigidbody = true;
    public float smoothTime = 0.06f;
    public float velScale = 1f;

    public bool sendAngular = true;
    public float angSmooth = 0.06f;

    Rigidbody _rb;
    Renderer _rend;
    MaterialPropertyBlock _mpb;

    Vector3 _prevPos, _velSmoothed, _angSmoothed;

    void Awake()
    {
        _rb = GetComponent<Rigidbody>();
        _rend = GetComponent<Renderer>();
        _mpb = new MaterialPropertyBlock();
        _prevPos = transform.position;

        // IMPORTANT: PropertyBlocks are ignored by Static Batching
        gameObject.isStatic = false;
    }

    void Update()
    {
        Vector3 vel = Vector3.zero;
        Vector3 ang = Vector3.zero;

        if (useRigidbody && _rb != null)
        {
            vel = _rb.velocity;
            ang = _rb.angularVelocity;
        }
        else
        {
            // Derive velocity from transform motion
            Vector3 pos = transform.position;
            vel = (pos - _prevPos) / Mathf.Max(Time.deltaTime, 1e-5f);
            _prevPos = pos;
        }

        // Smooth to reduce jitter
        float t = 1f - Mathf.Exp(-Time.deltaTime / Mathf.Max(0.0001f, smoothTime));
        _velSmoothed = Vector3.Lerp(_velSmoothed, vel, t);

        float ta = 1f - Mathf.Exp(-Time.deltaTime / Mathf.Max(0.0001f, angSmooth));
        _angSmoothed = Vector3.Lerp(_angSmoothed, ang, ta);

        // Apply to material slot
        _rend.GetPropertyBlock(_mpb, materialIndex);
        _mpb.SetVector("_VelWS", _velSmoothed * velScale);
        if (sendAngular) _mpb.SetVector("_AngVelWS", _angSmoothed);
        _rend.SetPropertyBlock(_mpb, materialIndex);
    }
}
