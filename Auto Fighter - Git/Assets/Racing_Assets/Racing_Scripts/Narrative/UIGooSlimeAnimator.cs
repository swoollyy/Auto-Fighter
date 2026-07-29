using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Canvas only redraws when dirty, so animated UI shaders look frozen.
/// Attach this to the Image using UI/GooSlimePanel — it pushes time every frame
/// and forces a material refresh so the goo actually moves.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Graphic))]
public class UIGooSlimeAnimator : MonoBehaviour
{
    private static readonly int AnimTimeId = Shader.PropertyToID("_AnimTime");

    [Tooltip("Playback speed multiplier for the slime motion.")]
    [SerializeField, Min(0f)] private float speed = 1f;

    [Tooltip("If true, uses unscaled time (keeps moving during pause menus).")]
    [SerializeField] private bool useUnscaledTime = true;

    private Graphic _graphic;
    private Material _runtimeMat;
    private float _time;

    private void Awake()
    {
        _graphic = GetComponent<Graphic>();
    }

    private void OnEnable()
    {
        EnsureRuntimeMaterial();
        _time = 0f;
        PushTime(0f);
    }

    private void OnDisable()
    {
        // Leave the Image's shared material alone; only clear our instance ref.
        _runtimeMat = null;
    }

    private void Update()
    {
        if (_graphic == null)
            return;

        EnsureRuntimeMaterial();
        if (_runtimeMat == null)
            return;

        float dt = useUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;
        _time += dt * Mathf.Max(0f, speed);
        PushTime(_time);
    }

    private void EnsureRuntimeMaterial()
    {
        if (_graphic == null)
            return;

        // material getter returns an instance unique to this Graphic — safe to animate.
        Material mat = _graphic.material;
        if (mat == null)
            return;

        if (_runtimeMat != mat)
            _runtimeMat = mat;
    }

    private void PushTime(float t)
    {
        if (_runtimeMat != null && _runtimeMat.HasProperty(AnimTimeId))
            _runtimeMat.SetFloat(AnimTimeId, t);

        // Critical: without this, Canvas may never re-render the animated shader.
        _graphic.SetMaterialDirty();
    }
}
