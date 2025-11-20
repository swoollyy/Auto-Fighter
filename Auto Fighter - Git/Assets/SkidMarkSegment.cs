using UnityEngine;

public class SkidMarkSegment : MonoBehaviour
{
    public float lifetime = 5f;   // seconds before fully faded

    private float _age;
    private MaterialPropertyBlock _mpb;
    private Renderer _renderer;
    private static readonly int AlphaID = Shader.PropertyToID("_Alpha");

    private void Awake()
    {
        _renderer = GetComponent<Renderer>();
        _mpb = new MaterialPropertyBlock();
        _renderer.GetPropertyBlock(_mpb);
        _mpb.SetFloat(AlphaID, 1f);
        _renderer.SetPropertyBlock(_mpb);
    }

    private void Update()
    {
        _age += Time.deltaTime;
        float t = Mathf.Clamp01(_age / lifetime);
        float alpha = 1f - t;

        _renderer.GetPropertyBlock(_mpb);
        _mpb.SetFloat(AlphaID, alpha);
        _renderer.SetPropertyBlock(_mpb);

        if (_age >= lifetime)
            Destroy(gameObject);
    }
}
