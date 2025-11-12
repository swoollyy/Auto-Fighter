using UnityEngine;

[DisallowMultipleComponent]
public class SpeedTrailFollower : MonoBehaviour
{
    [Header("Targets")]
    [SerializeField] private Rigidbody targetRb;      // Ball rigidbody
    [SerializeField] private Transform target;        // Ball transform (optional)
    private Ball _ball;                               // Cached Ball component (for glow/ emission)

    [Header("Line Renderer")]
    [SerializeField] private LineRenderer line;       // Assign in inspector or we will auto-create
    [Tooltip("Template material (will be instanced at runtime so emission changes don't affect shared asset).")]
    [SerializeField] private Material lineTemplateMaterial;

    [Header("Placement")]
    [SerializeField] private Vector3 startOffset = new(0f, 0.05f, 0f); // small lift for visibility
    [SerializeField] private float backwardLift = 0f;                  // additional Y offset for tail segment
    [SerializeField] private bool usePlanarXZ = true;                  // ignore Y component (pinball table axis)

    [Header("Speed Mapping")]
    [SerializeField] private float minSpeedToShow = 0.5f;
    [SerializeField] private float maxSpeedForMax = 50f;
    [SerializeField] private float minLength = 0.08f;
    [SerializeField] private float maxLength = 1.6f;
    [SerializeField] private float minWidth = 0.02f;
    [SerializeField] private float maxWidth = 0.20f;

    [Header("Smoothing")]
    [SerializeField, Tooltip("Higher = snappier response.")] private float lengthRiseRate = 8f;
    [SerializeField, Tooltip("Smooth time for length decay.")] private float lengthFallSmooth = 0.18f;
    [SerializeField, Tooltip("Higher = snappier yaw alignment.")] private float yawSmoothing = 20f;
    [SerializeField, Tooltip("Higher = snappier width adaptation.")] private float widthRiseRate = 10f;
    [SerializeField, Tooltip("Smooth time for width decay.")] private float widthFallSmooth = 0.15f;

    [Header("Emission Sync")]
    [Tooltip("Multiplier applied to ball emission intensity when mapping to line emission.")]
    [SerializeField] private float emissionIntensityScale = 1.0f;
    [Tooltip("Update color/emission only when change exceeds this delta (reduces material set calls).")]
    [SerializeField] private float colorUpdateThreshold = 0.02f;

    private Vector3 _lastDir = Vector3.forward;
    private float _curLength;
    private float _velLength;
    private float _curWidth;
    private float _velWidth;
    private bool _wasVisible;

    // Runtime material instance
    private Material _runtimeMat;
    private static readonly int EmissionColorID = Shader.PropertyToID("_EmissionColor");
    private static readonly int BaseColorID = Shader.PropertyToID("_BaseColor");
    private static readonly int ColorID = Shader.PropertyToID("_Color");

    private Color _lastBallGlowColor;
    private float _lastBallEmission;

    void Awake()
    {
        if (!target && targetRb) target = targetRb.transform;
        if (!targetRb && target) targetRb = target.GetComponent<Rigidbody>();
        if (!targetRb && !target)
        {
            Debug.LogWarning("[SpeedTrailFollower] No target assigned.");
            enabled = false;
            return;
        }

        _ball = targetRb ? targetRb.GetComponent<Ball>() : null;

        EnsureLine();

        if (_ball != null)
        {
            _lastBallGlowColor = _ball.GlowColor;
            _lastBallEmission = _ball.EmissionIntensityUI;
            ApplyEmissionToMaterial(force: true);
        }
    }

    void EnsureLine()
    {
        if (!line)
        {
            line = GetComponent<LineRenderer>();
            if (!line) line = gameObject.AddComponent<LineRenderer>();
        }

        line.positionCount = 2;
        line.useWorldSpace = true;
        line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        line.receiveShadows = false;
        line.textureMode = LineTextureMode.Stretch;
        line.alignment = LineAlignment.View;

        if (lineTemplateMaterial)
        {
            _runtimeMat = Instantiate(lineTemplateMaterial);
            _runtimeMat.name = lineTemplateMaterial.name + " (Runtime Trail)";
        }
        else
        {
            // fallback simple material
            _runtimeMat = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            _runtimeMat.enableInstancing = true;
        }
        line.sharedMaterial = _runtimeMat;
    }

    private void LateUpdate()
    {
        if (!targetRb || !target)
        {
            if (line) line.enabled = false;
            _wasVisible = false;
            return;
        }

        if (!line) return;
        line.enabled = true; // keep renderer on so positions stay in sync even when hidden

        Vector3 v = targetRb.velocity;
        Vector3 planar = usePlanarXZ ? new Vector3(v.x, 0f, v.z) : v;
        float speed = planar.magnitude;

        // Direction (retain previous if near zero to avoid jitter)
        if (planar.sqrMagnitude > 0.0004f)
            _lastDir = planar.normalized;

        // Normalized speed
        float max = Mathf.Max(0.01f, maxSpeedForMax);
        float t = Mathf.Clamp01(speed / max);

        // Target length & width
        float targetLength = Mathf.Lerp(minLength, maxLength, t);
        float targetWidth = Mathf.Lerp(minWidth, maxWidth, t);

        // Compute head position
        Vector3 head = target.position + startOffset;

        bool visibleNow = speed >= minSpeedToShow;

        if (!visibleNow)
        {
            // Hidden state: keep the line snapped to the head and width zero
            _curLength = 0f;
            _curWidth = 0f;
            line.widthMultiplier = 0f;

            line.SetPosition(0, head);
            line.SetPosition(1, head);

            _wasVisible = false;

            // Still keep emission in sync to avoid a pop when reappearing
            if (_ball != null) MaybeUpdateEmission();
            return;
        }

        // Visible: smooth length & width (fast rise, smooth fall)
        if (targetLength >= _curLength)
            _curLength = Mathf.MoveTowards(_curLength, targetLength, lengthRiseRate * Time.deltaTime);
        else
            _curLength = Mathf.SmoothDamp(_curLength, targetLength, ref _velLength, lengthFallSmooth, Mathf.Infinity, Time.deltaTime);

        if (targetWidth >= _curWidth)
            _curWidth = Mathf.MoveTowards(_curWidth, targetWidth, widthRiseRate * Time.deltaTime);
        else
            _curWidth = Mathf.SmoothDamp(_curWidth, targetWidth, ref _velWidth, widthFallSmooth, Mathf.Infinity, Time.deltaTime);

        line.widthMultiplier = _curWidth;

        // Compute tail and apply optional yaw smoothing
        Vector3 targetTail = head - _lastDir * _curLength;
        targetTail.y += backwardLift;

        // If we were hidden last frame, snap immediately (no smoothing from stale tail)
        Vector3 prevTail = _wasVisible ? line.GetPosition(line.positionCount - 1) : head;
        float yawLerp = _wasVisible ? 1f - Mathf.Exp(-yawSmoothing * Time.deltaTime) : 1f;
        Vector3 tail = Vector3.Lerp(prevTail, targetTail, yawLerp);

        line.SetPosition(0, head);
        line.SetPosition(1, tail);

        if (_ball != null) MaybeUpdateEmission();

        _wasVisible = true;
    }

    private void MaybeUpdateEmission()
    {
        // Ball provides GlowColor & EmissionIntensityUI
        var glowColor = _ball.GlowColor;
        var intensity = _ball.EmissionIntensityUI * emissionIntensityScale;

        // Only update if change exceeds threshold
        if (_runtimeMat != null &&
            (Mathf.Abs(intensity - _lastBallEmission) > colorUpdateThreshold
             || ColorDistance(glowColor, _lastBallGlowColor) > colorUpdateThreshold))
        {
            ApplyEmission(glowColor, intensity);
            _lastBallGlowColor = glowColor;
            _lastBallEmission = intensity;
        }
    }

    private void ApplyEmissionToMaterial(bool force = false)
    {
        if (_ball == null || _runtimeMat == null) return;
        ApplyEmission(_ball.GlowColor, _ball.EmissionIntensityUI * emissionIntensityScale, force);
    }

    private void ApplyEmission(Color baseColor, float intensity, bool force = false)
    {
        intensity = Mathf.Clamp(intensity, 0f, 8f);
        // Convert to gamma-corrected emissive
        Color emissive = baseColor * Mathf.LinearToGammaSpace(intensity);

        if (force)
        {
            _runtimeMat.EnableKeyword("_EMISSION");
        }

        if (_runtimeMat.HasProperty(EmissionColorID))
            _runtimeMat.SetColor(EmissionColorID, emissive);
        if (_runtimeMat.HasProperty(BaseColorID))
            _runtimeMat.SetColor(BaseColorID, baseColor);
        if (_runtimeMat.HasProperty(ColorID))
            _runtimeMat.SetColor(ColorID, baseColor);
    }

    private static float ColorDistance(Color a, Color b)
    {
        float dr = a.r - b.r;
        float dg = a.g - b.g;
        float db = a.b - b.b;
        return Mathf.Abs(dr) + Mathf.Abs(dg) + Mathf.Abs(db);
    }

    // Public API if you want to swap target at runtime
    public void SetTarget(Rigidbody rb)
    {
        targetRb = rb;
        target = rb ? rb.transform : null;
        _ball = rb ? rb.GetComponent<Ball>() : null;
    }

    public void SetLineRenderer(LineRenderer lr)
    {
        line = lr;
        EnsureLine();
        ApplyEmissionToMaterial(force: true);
    }
}