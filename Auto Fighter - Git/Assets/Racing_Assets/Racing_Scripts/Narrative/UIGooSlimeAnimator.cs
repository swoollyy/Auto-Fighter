using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Canvas only redraws when dirty, so animated UI shaders look frozen.
/// Attach this to the Image using UI/GooSlimePanel — it pushes time every frame
/// and forces a material refresh so the goo actually moves.
/// Also blends Dialogue Box FX fill/rim colors when the speaker changes.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Graphic))]
public class UIGooSlimeAnimator : MonoBehaviour
{
    private static readonly int AnimTimeId = Shader.PropertyToID("_AnimTime");
    private static readonly int ColorId = Shader.PropertyToID("_Color");
    private static readonly int RimColorId = Shader.PropertyToID("_RimColor");

    [Tooltip("Playback speed multiplier for the slime motion.")]
    [SerializeField, Min(0f)] private float speed = 1f;

    [Tooltip("If true, uses unscaled time (keeps moving during pause menus / paused dialogue).")]
    [SerializeField] private bool useUnscaledTime = true;

    private Graphic _graphic;
    private Material _runtimeMat;
    private float _time;

    private Color _currentFill;
    private Color _currentRim;
    private Color _fromFill;
    private Color _fromRim;
    private Color _toFill;
    private Color _toRim;
    private float _blendElapsed;
    private float _blendDuration;
    private bool _blending;
    private bool _hasColors;

    private void Awake()
    {
        _graphic = GetComponent<Graphic>();
    }

    private void OnEnable()
    {
        EnsureRuntimeMaterial();
        _time = 0f;
        PushTime(0f);
        if (_hasColors)
            PushColors(_currentFill, _currentRim);
    }

    private void OnDisable()
    {
        // Leave the Image's shared material alone; only clear our instance ref.
        // Keep color / blend state so a brief disable does not break in-sequence lerps.
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

        if (_blending)
            TickColorBlend(dt);
    }

    /// <summary>
    /// Tint the goo panel for the current dialogue speaker/line.
    /// When <paramref name="immediate"/> is false, smoothly blends from the current colors.
    /// </summary>
    public void SetBlobColors(Color fillColor, Color rimColor, bool immediate = false, float blendDuration = 0.45f)
    {
        EnsureRuntimeMaterial();

        if (!_hasColors || immediate || blendDuration <= 0f)
        {
            _currentFill = fillColor;
            _currentRim = rimColor;
            _toFill = fillColor;
            _toRim = rimColor;
            _hasColors = true;
            _blending = false;
            PushColors(_currentFill, _currentRim);
            return;
        }

        _fromFill = _currentFill;
        _fromRim = _currentRim;
        _toFill = fillColor;
        _toRim = rimColor;
        _blendElapsed = 0f;
        _blendDuration = blendDuration;
        _blending = true;
    }

    /// <summary>Clears tracked colors so the next SetBlobColors snaps instead of blending.</summary>
    public void ResetBlobColorState()
    {
        _hasColors = false;
        _blending = false;
    }

    /// <summary>If a color blend is in progress, snap to the target colors immediately.</summary>
    public void CompleteColorBlendImmediate()
    {
        if (!_blending) return;
        _currentFill = _toFill;
        _currentRim = _toRim;
        _blending = false;
        EnsureRuntimeMaterial();
        PushColors(_currentFill, _currentRim);
    }

    private void TickColorBlend(float dt)
    {
        _blendElapsed += dt;
        float t = _blendDuration <= 0f ? 1f : Mathf.Clamp01(_blendElapsed / _blendDuration);
        // Smoothstep for a softer ease-in/out between speakers.
        float eased = t * t * (3f - 2f * t);

        _currentFill = Color.Lerp(_fromFill, _toFill, eased);
        _currentRim = Color.Lerp(_fromRim, _toRim, eased);
        PushColors(_currentFill, _currentRim);

        if (t >= 1f)
            _blending = false;
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

    private void PushColors(Color fill, Color rim)
    {
        if (_runtimeMat == null)
            return;

        bool dirty = false;
        if (_runtimeMat.HasProperty(ColorId))
        {
            _runtimeMat.SetColor(ColorId, fill);
            dirty = true;
        }
        if (_runtimeMat.HasProperty(RimColorId))
        {
            _runtimeMat.SetColor(RimColorId, rim);
            dirty = true;
        }

        if (dirty && _graphic != null)
            _graphic.SetMaterialDirty();
    }

    private void PushTime(float t)
    {
        if (_runtimeMat != null && _runtimeMat.HasProperty(AnimTimeId))
            _runtimeMat.SetFloat(AnimTimeId, t);

        // Critical: without this, Canvas may never re-render the animated shader.
        _graphic.SetMaterialDirty();
    }
}
