using System;
using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Global goo iris screen transition (close edges→center, open reverse).
/// Topmost Overlay canvas, DontDestroyOnLoad. No portal star — flow transitions only.
/// Dialogue goo (UI/GooSlimePanel) is untouched.
/// </summary>
[DisallowMultipleComponent]
public sealed class GooIrisScreenTransition : MonoBehaviour
{
    public static GooIrisScreenTransition Instance { get; private set; }

    /// <summary>Set before LoadScene; opened after the new scene boots to skill tree.</summary>
    public static bool PendingOpenAfterSceneLoad { get; set; }

    /// <summary>Default iris canvas while covering the screen.</summary>
    public const int DefaultSort = 32000;
    /// <summary>Dialogue overlay when the iris is hidden (above skill tree, below a covering iris).</summary>
    public const int DialogueSort = 32150;
    /// <summary>Dialogue overlay while goo is sealed or opening so the box cannot composite over slime.</summary>
    public const int SortUnderIris = 31000;
    /// <summary>Iris sort while revealing onto dialogue (belt-and-suspenders with <see cref="SortUnderIris"/>).</summary>
    public const int SortOverDialogue = 32500;

    /// <summary>
    /// True while garage/init goo should hide the dialogue box: pending scene-load reveal,
    /// or the iris is currently drawing over the screen.
    /// </summary>
    public static bool ShouldCoverDialogue()
    {
        if (PendingOpenAfterSceneLoad)
            return true;
        var iris = Instance;
        return iris != null && iris.IsVisuallyActive;
    }

    private const int CanvasSortOrder = DefaultSort;
    private static readonly int HoleId = Shader.PropertyToID("_HoleRadius");
    private static readonly int AspectId = Shader.PropertyToID("_Aspect");
    private static readonly int AnimTimeId = Shader.PropertyToID("_AnimTime");
    private static readonly int ColorId = Shader.PropertyToID("_Color");
    private static readonly int RimColorId = Shader.PropertyToID("_RimColor");

    [Header("Timing (matched to portal iris)")]
    [SerializeField, Min(0.05f)] private float closeSeconds = 0.95f;
    [SerializeField, Min(0.05f)] private float openSeconds = 0.5f;
    [Tooltip("End hole for open. Keep near closeStartHole (~1.02) — higher values are overscan overbite that looks clear already but still delays Day/TV intro.")]
    [SerializeField, Range(0.85f, 1.35f)] private float openHole = 1.02f;
    [Tooltip("Starting hole size (1 ≈ screen corners). Same portal rule: edges already dark — no long nothing-happening beat.")]
    [SerializeField, Range(0.85f, 1.35f)] private float closeStartHole = 1.02f;
    [Tooltip("Hole radius used for first-run controls overlay (~0.5 ≈ half screen covered by goo).")]
    [SerializeField, Range(0.15f, 0.9f)] private float controlsHoldHole = 0.52f;
    [SerializeField, Min(0.05f)] private float controlsCreepSeconds = 0.65f;

    [Header("Goo Look (UI/GooIrisClose — same defaults as portal)")]
    [SerializeField, Range(0.001f, 0.08f)] private float edgeSoftness = 0.012f;
    [SerializeField, Range(0f, 0.55f)] private float warpAmount = 0.28f;
    [SerializeField, Range(0.02f, 1.2f)] private float warpDepth = 0.55f;
    [SerializeField, Range(0.5f, 24f)] private float noiseScale = 5.5f;
    [SerializeField, Range(0f, 6f)] private float noiseSpeed = 2.1f;
    [SerializeField, Range(1f, 32f)] private float detailScale = 14f;
    [SerializeField, Range(0f, 0.35f)] private float detailAmount = 0.12f;
    [SerializeField, Range(0f, 8f)] private float detailSpeed = 3.1f;

    [Header("Overscan")]
    [SerializeField] private float overscanPixels = 160f;
    [SerializeField] private float overscanScale = 1.12f;

    [Header("Close Rumble (light — portal-style)")]
    [SerializeField] private bool closeRumble = true;
    [SerializeField, Min(0f)] private float rumblePosPeak = 10f;
    [SerializeField, Min(0f)] private float rumbleRotPeak = 1.1f;
    [SerializeField, Min(0.5f)] private float rumbleSpeed = 22f;

    private Canvas _canvas;
    private RectTransform _root;
    private Image _image;
    private Material _mat;
    private Sprite _whiteSprite;
    private Coroutine _activeCr;
    private float _animClock;
    private float _currentHole = 1.15f;
    private bool _sealed;
    private bool _busy;
    private bool _holdAnim;
    private float _rumbleSeed;
    private Vector2 _rootBasePos;
    private Quaternion _rootBaseRot;

    public bool IsBusy => _busy;
    public bool IsSealed => _sealed;
    public float CurrentHole => _currentHole;
    public float ControlsHoldHole => controlsHoldHole;
    /// <summary>True when the iris canvas/image are enabled (covering or mid-transition).</summary>
    public bool IsVisuallyActive => IsVisible();

    /// <summary>
    /// Prepare for a close over the live skill tree / results. If the iris was left
    /// sealed-but-hidden (or stuck sealed with no animation), reset so <see cref="CoClose"/>
    /// always plays a full edge→center goo close. Preserves an in-progress visible close
    /// (e.g. first-run controls held at ~50%).
    /// </summary>
    public void EnsureReadyToCloseOverScreen()
    {
        StopActiveTracked();
        _busy = false;
        RestoreDefaultSortOrder();

        // Already closing / holding over content — continue from current hole.
        if (IsVisible() && !_sealed && _currentHole < closeStartHole - 0.02f)
        {
            _holdAnim = true;
            return;
        }

        // Force a fresh close from the edges on the next CoClose.
        _sealed = false;
        _holdAnim = false;
        _currentHole = Mathf.Clamp(closeStartHole, 0.85f, 1.35f);
        SetVisible(false);
        if (_mat != null)
            SetHole(_currentHole);
        ResetRumble();
    }

    public static GooIrisScreenTransition EnsureExists()
    {
        if (Instance != null)
        {
            DontDestroyOnLoad(Instance.gameObject);
            return Instance;
        }

        var existing = FindObjectOfType<GooIrisScreenTransition>(true);
        if (existing != null)
        {
            Instance = existing;
            DontDestroyOnLoad(existing.gameObject);
            return existing;
        }

        var go = new GameObject("GooIrisScreenTransition");
        var t = go.AddComponent<GooIrisScreenTransition>();
        if (t._canvas == null)
            t.BuildHierarchy();
        DontDestroyOnLoad(go);
        Instance = t;
        return t;
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);
        if (_canvas == null)
            BuildHierarchy();

        // Critical: after Cross → LoadScene the DDOL iris is already sealed black.
        // A late Awake (or scene duplicate path) must NOT hide/clear that sealed state
        // in a way that desyncs flags — and must not wipe a pending open.
        if (PendingOpenAfterSceneLoad || _sealed)
        {
            SnapSealed(restoreDefaultSort: !PendingOpenAfterSceneLoad);
            if (PendingOpenAfterSceneLoad)
                SetSortOrder(SortOverDialogue);
            return;
        }

        SetVisible(false);
    }

    private void OnEnable()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private void OnDestroy()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        if (Instance == this)
            Instance = null;
        if (_mat != null)
            Destroy(_mat);
        if (_whiteSprite != null && _whiteSprite.texture != null)
            Destroy(_whiteSprite.texture);
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        // Close/open IEnumerators hosted on a destroyed GameManager can latch this.
        _busy = false;

        if (!PendingOpenAfterSceneLoad && !IsBlockingScreen())
            return;

        if (_sceneOpenCr != null)
            StopCoroutine(_sceneOpenCr);
        _sceneOpenCr = StartCoroutine(CoOpenAfterSceneLoadFailsafe());
    }

    private Coroutine _sceneOpenCr;

    private IEnumerator CoOpenAfterSceneLoadFailsafe()
    {
        // Give GameManager.Start a moment to own the open; only self-heal if still stuck.
        yield return null;
        yield return null;
        yield return null;

        if (!PendingOpenAfterSceneLoad && !IsBlockingScreen() && !IsSealed)
        {
            _sceneOpenCr = null;
            yield break;
        }

        if (PendingOpenAfterSceneLoad || IsBlockingScreen() || IsSealed)
            yield return CoOpenRevealingDialogue();

        _sceneOpenCr = null;
    }

    /// <summary>
    /// Seal the iris above the dialogue canvas so garage lines paint under goo.
    /// Used by every results→skill-tree return (Trial 1, Taskmaster, Max Fuel, Init).
    /// </summary>
    public void CoverDialogueForReveal()
    {
        SnapSealed(restoreDefaultSort: false);
        SetSortOrder(SortOverDialogue);
        DialogueUI.RefreshHostSortForIris();
    }

    /// <summary>Open the hole onto whatever is under the sealed goo (skill tree + dialogue), then restore default sort.</summary>
    public IEnumerator CoOpenRevealingDialogue()
    {
        SetSortOrder(SortOverDialogue);
        DialogueUI.RefreshHostSortForIris();
        yield return CoOpen(restoreDefaultSort: false);
        if (IsBlockingScreen() || IsSealed)
            SnapOpenHidden();
        RestoreDefaultSortOrder();
        DialogueUI.RefreshHostSortForIris();
    }

    /// <summary>True when the iris is covering the screen (sealed black or mid-transition visible).</summary>
    public bool IsBlockingScreen()
    {
        return (_sealed || _busy) && IsVisible();
    }

    private void Update()
    {
        if (!_holdAnim || _busy || !IsVisible())
            return;
        float dt = Time.unscaledDeltaTime;
        if (dt <= 0f) dt = 1f / 60f;
        TickAnim(dt);
    }

    /// <summary>
    /// Drop or raise canvas relative to other overlay UI.
    /// Controls overlay sits below the default order so goo covers the text as the iris closes.
    /// </summary>
    public void SetSortOrder(int order)
    {
        if (_canvas == null)
            return;
        _canvas.overrideSorting = true;
        _canvas.sortingOrder = order;
    }

    public void RestoreDefaultSortOrder()
    {
        SetSortOrder(CanvasSortOrder);
    }

    private void BuildHierarchy()
    {
        _canvas = gameObject.GetComponent<Canvas>();
        if (_canvas == null)
            _canvas = gameObject.AddComponent<Canvas>();
        _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        _canvas.sortingOrder = CanvasSortOrder;
        _canvas.overrideSorting = true;
        _canvas.pixelPerfect = false;
        _canvas.additionalShaderChannels =
            AdditionalCanvasShaderChannels.TexCoord1 |
            AdditionalCanvasShaderChannels.Normal |
            AdditionalCanvasShaderChannels.Tangent;

        var scaler = GetComponent<CanvasScaler>();
        if (scaler == null)
            scaler = gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;

        if (GetComponent<GraphicRaycaster>() == null)
            gameObject.AddComponent<GraphicRaycaster>().enabled = false;

        Transform existing = transform.Find("Iris");
        GameObject irisGo;
        if (existing != null)
        {
            irisGo = existing.gameObject;
            _root = existing as RectTransform;
            _image = irisGo.GetComponent<Image>();
        }
        else
        {
            irisGo = new GameObject("Iris", typeof(RectTransform));
            irisGo.transform.SetParent(transform, false);
            _root = irisGo.GetComponent<RectTransform>();
            StretchOverscan(_root);
            _image = irisGo.AddComponent<Image>();
            _image.raycastTarget = false;
            _image.preserveAspect = false;
            _image.maskable = false;
        }

        EnsureMaterialAndSprite();
        _image.sprite = _whiteSprite;
        _image.material = _mat;
        _image.color = Color.white;
        _image.type = Image.Type.Simple;
        ApplyPortalMatchedGooSettings();
        SetHole(openHole);
        CacheRootRestPose();
    }

    private void EnsureMaterialAndSprite()
    {
        if (_mat == null)
        {
            // Builds strip Shader.Find-only shaders. Prefer Resources material (forces include),
            // then Always Included / Shader.Find fallback.
            Material template = Resources.Load<Material>("GooIrisClose");
            if (template != null && template.shader != null)
            {
                _mat = new Material(template);
            }
            else
            {
                Shader shader = Shader.Find("UI/GooIrisClose");
                if (shader == null)
                {
                    Debug.LogError(
                        "[GooIrisScreenTransition] Missing shader UI/GooIrisClose in this build. " +
                        "Ensure Assets/Resources/GooIrisClose.mat exists and the shader is in Always Included Shaders.");
                    return;
                }
                _mat = new Material(shader);
            }
            _mat.name = "GooIrisScreenTransition_Runtime";
            // Same runtime material path as FinishHyperTunnelVfx portal iris.
            _mat.hideFlags = HideFlags.HideAndDontSave;
        }

        if (_whiteSprite == null)
        {
            // Larger bilinear white rect — cleaner sampling than a 4x4 point sprite.
            var tex = new Texture2D(64, 64, TextureFormat.RGBA32, false, true);
            tex.name = "GooIrisWhite";
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.filterMode = FilterMode.Bilinear;
            tex.anisoLevel = 0;
            var fill = new Color[64 * 64];
            for (int i = 0; i < fill.Length; i++)
                fill[i] = Color.white;
            tex.SetPixels(fill);
            tex.Apply(false, false);
            _whiteSprite = Sprite.Create(tex, new Rect(0, 0, 64, 64), new Vector2(0.5f, 0.5f), 100f);
            _whiteSprite.name = "GooIrisWhiteSprite";
        }

        if (_image != null)
        {
            _image.sprite = _whiteSprite;
            _image.material = _mat;
            _image.color = Color.white;
        }
    }

    /// <summary>
    /// Force the same glorp / edge settings the portal iris uses (shader defaults, applied explicitly).
    /// </summary>
    private void ApplyPortalMatchedGooSettings()
    {
        if (_mat == null) return;
        _mat.SetColor(ColorId, Color.black);
        _mat.SetColor(RimColorId, Color.black);
        _mat.SetFloat("_RimWidth", 0f);
        _mat.SetFloat("_RimStrength", 0f);
        _mat.SetFloat("_EdgeSoftness", edgeSoftness);
        _mat.SetFloat("_WarpAmount", warpAmount);
        _mat.SetFloat("_WarpDepth", warpDepth);
        _mat.SetFloat("_NoiseScale", noiseScale);
        _mat.SetFloat("_NoiseSpeed", noiseSpeed);
        _mat.SetFloat("_DetailScale", detailScale);
        _mat.SetFloat("_DetailAmount", detailAmount);
        _mat.SetFloat("_DetailSpeed", detailSpeed);
        float aspect = Screen.height > 0 ? (float)Screen.width / Screen.height : 1.777f;
        _mat.SetFloat(AspectId, aspect);
        _mat.SetFloat(AnimTimeId, _animClock);
    }

    private void ApplyBlackColors() => ApplyPortalMatchedGooSettings();

    private void CacheRootRestPose()
    {
        if (_root == null) return;
        _rootBasePos = _root.anchoredPosition;
        _rootBaseRot = _root.localRotation;
    }

    private void ResetRumble()
    {
        if (_root == null) return;
        _root.anchoredPosition = _rootBasePos;
        _root.localRotation = _rootBaseRot;
    }

    private void ApplyCloseRumble(float progress01)
    {
        if (!closeRumble || _root == null)
            return;

        float ramp = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(progress01));
        float posStr = rumblePosPeak * ramp;
        float rotStr = rumbleRotPeak * ramp;
        float t = Time.unscaledTime * rumbleSpeed;
        float seed = _rumbleSeed;
        float x = (Mathf.PerlinNoise(seed, t) * 2f - 1f) * posStr;
        float y = (Mathf.PerlinNoise(seed + 19.7f, t) * 2f - 1f) * posStr;
        float fast = Time.unscaledTime * rumbleSpeed * 2.4f;
        x += Mathf.Sin(fast + seed) * posStr * 0.35f;
        y += Mathf.Cos(fast * 1.13f + seed) * posStr * 0.35f;
        float rot = (Mathf.PerlinNoise(seed + 41f, t * 0.85f) * 2f - 1f) * rotStr;
        _root.anchoredPosition = _rootBasePos + new Vector2(x, y);
        _root.localRotation = _rootBaseRot * Quaternion.Euler(0f, 0f, rot);
    }

    private void PrepareForTransition()
    {
        EnsureMaterialAndSprite();
        StretchOverscan(_root);
        CacheRootRestPose();
        ResetRumble();
        ApplyPortalMatchedGooSettings();
        if (_image != null)
        {
            _image.material = _mat;
            _image.sprite = _whiteSprite;
            _image.color = Color.white;
            _image.SetAllDirty();
        }
        _rumbleSeed = UnityEngine.Random.value * 1000f;
    }

    private void StretchOverscan(RectTransform rt)
    {
        float pad = Mathf.Max(0f, overscanPixels);
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = new Vector2(-pad, -pad);
        rt.offsetMax = new Vector2(pad, pad);
        rt.localScale = Vector3.one * Mathf.Max(1f, overscanScale);
    }

    private void SetVisible(bool on)
    {
        if (_image != null)
            _image.enabled = on;
        if (_canvas != null)
            _canvas.enabled = on;
    }

    private bool IsVisible()
    {
        return _canvas != null && _canvas.enabled && _image != null && _image.enabled;
    }

    private void SetHole(float hole)
    {
        _currentHole = hole;
        if (_mat == null) return;
        float aspect = Screen.height > 0 ? (float)Screen.width / Screen.height : 1.777f;
        _mat.SetFloat(AspectId, aspect);
        _mat.SetFloat(HoleId, hole);
        if (_image != null)
        {
            _image.material = _mat;
            _image.SetAllDirty();
        }
    }

    private void TickAnim(float dt)
    {
        _animClock += dt * 1.35f;
        if (_mat != null)
            _mat.SetFloat(AnimTimeId, _animClock);
        if (_image != null)
            _image.SetAllDirty();
    }

    private void StopActiveTracked()
    {
        if (_activeCr != null)
        {
            StopCoroutine(_activeCr);
            _activeCr = null;
        }
    }

    public IEnumerator CoClose(float duration = -1f, bool restoreDefaultSort = true)
    {
        EnsureMaterialAndSprite();
        if (_mat == null) yield break;

        // Sealed + visible black — nothing to do.
        // Sealed + hidden (HideVisualKeepSealed leftover) must NOT early-out; caller should
        // have used EnsureReadyToCloseOverScreen, but recover here too.
        if (_sealed && !_busy && IsVisible())
            yield break;

        if (_sealed && !_busy && !IsVisible())
        {
            _sealed = false;
            _currentHole = Mathf.Clamp(closeStartHole, 0.85f, 1.35f);
        }

        // Wait for an in-flight open to finish before closing again.
        while (_busy)
            yield return null;

        if (_sealed && IsVisible())
            yield break;

        _busy = true;
        _holdAnim = false;
        float dur = duration > 0f ? duration : closeSeconds;
        dur = Mathf.Max(0.05f, dur);

        // Skip when caller already raised sort above dialogue / overlays for this close.
        if (restoreDefaultSort)
            RestoreDefaultSortOrder();
        bool wasVisible = IsVisible();
        float edge = Mathf.Clamp(closeStartHole, 0.85f, 1.35f);
        float start = (wasVisible && _currentHole < edge - 0.01f)
            ? _currentHole
            : edge;

        PrepareForTransition();
        SetVisible(true);
        SetHole(start);
        _sealed = false;

        float t0 = Time.unscaledTime;
        float t1 = t0 + dur;
        while (Time.unscaledTime < t1)
        {
            float linear = Mathf.Clamp01(Mathf.InverseLerp(t0, t1, Time.unscaledTime));
            float u = Mathf.SmoothStep(0f, 1f, linear);
            SetHole(Mathf.Lerp(start, 0f, u));
            float dt = Time.unscaledDeltaTime;
            if (dt <= 0f) dt = 1f / 60f;
            TickAnim(dt);
            ApplyCloseRumble(u);
            yield return null;
        }

        SetHole(0f);
        ResetRumble();
        _sealed = true;
        _busy = false;
    }

    /// <summary>
    /// Creep goo inward from the edges and hold at <paramref name="targetHole"/> (living goo).
    /// Used by the first-run controls overlay (~0.5 = half covered).
    /// </summary>
    public IEnumerator CoCloseToHoleAndHold(float targetHole = -1f, float duration = -1f)
    {
        EnsureMaterialAndSprite();
        if (_mat == null) yield break;

        while (_busy)
            yield return null;

        float end = targetHole > 0f ? targetHole : controlsHoldHole;
        end = Mathf.Clamp(end, 0.05f, 1.2f);
        float dur = duration > 0f ? duration : controlsCreepSeconds;
        dur = Mathf.Max(0.05f, dur);

        _busy = true;
        _holdAnim = false;
        _sealed = false;

        bool wasVisible = IsVisible();
        float edge = Mathf.Clamp(closeStartHole, 0.85f, 1.35f);
        float start = (wasVisible && _currentHole > 0.05f) ? _currentHole : edge;

        PrepareForTransition();
        SetVisible(true);
        SetHole(start);

        float t0 = Time.unscaledTime;
        float t1 = t0 + dur;
        while (Time.unscaledTime < t1)
        {
            float linear = Mathf.Clamp01(Mathf.InverseLerp(t0, t1, Time.unscaledTime));
            float u = Mathf.SmoothStep(0f, 1f, linear);
            SetHole(Mathf.Lerp(start, end, u));
            float dt = Time.unscaledDeltaTime;
            if (dt <= 0f) dt = 1f / 60f;
            TickAnim(dt);
            ApplyCloseRumble(u * 0.45f);
            yield return null;
        }

        SetHole(end);
        ResetRumble();
        _busy = false;
        _holdAnim = true;
    }

    public Coroutine BeginCloseToHoleAndHold(float targetHole = -1f, float duration = -1f)
    {
        if (_activeCr != null)
            StopCoroutine(_activeCr);
        _activeCr = StartCoroutine(CoCloseToHoleTracked(targetHole, duration));
        return _activeCr;
    }

    private IEnumerator CoCloseToHoleTracked(float targetHole, float duration)
    {
        yield return CoCloseToHoleAndHold(targetHole, duration);
        _activeCr = null;
    }

    public IEnumerator CoOpen(float duration = -1f, bool restoreDefaultSort = true)
    {
        EnsureMaterialAndSprite();
        if (_mat == null)
        {
            // Never leave the player on permanent black if the shader failed.
            SnapOpenHidden();
            yield break;
        }

        // Already open/hidden — nothing to do.
        if (!_sealed && !IsVisible())
        {
            PendingOpenAfterSceneLoad = false;
            yield break;
        }

        // Wait briefly for an in-flight transition; never hang forever across LoadScene.
        float busyWaitUntil = Time.unscaledTime + 0.75f;
        while (_busy && Time.unscaledTime < busyWaitUntil)
            yield return null;
        _busy = false;

        if (!_sealed && !IsVisible())
        {
            PendingOpenAfterSceneLoad = false;
            yield break;
        }

        _busy = true;
        _holdAnim = false;
        float dur = duration > 0f ? duration : openSeconds;
        dur = Mathf.Max(0.05f, dur);

        PrepareForTransition();
        // Garage/init: keep goo above the box for the whole hole animation.
        // Restoring DefaultSort here while dialogue is at DialogueSort draws the box on top of slime.
        if (ShouldCoverDialogue() || !restoreDefaultSort)
            SetSortOrder(SortOverDialogue);
        else
            RestoreDefaultSortOrder();
        SetVisible(true);
        SetHole(0f);
        _sealed = true;
        DialogueUI.RefreshHostSortForIris();

        // End at visual clear (≈ closeStartHole). Do NOT lerp past into overscan
        // overbite — that reads as "already open" while still blocking Day/TV intro.
        float end = Mathf.Clamp(openHole, 0.85f, 1.35f);
        float t0 = Time.unscaledTime;
        float t1 = t0 + dur;
        while (Time.unscaledTime < t1)
        {
            float linear = Mathf.Clamp01(Mathf.InverseLerp(t0, t1, Time.unscaledTime));
            float u = Mathf.SmoothStep(0f, 1f, linear);
            float hole = Mathf.Lerp(0f, end, u);
            SetHole(hole);
            float dt = Time.unscaledDeltaTime;
            if (dt <= 0f) dt = 1f / 60f;
            TickAnim(dt);
            ApplyCloseRumble(1f - u);

            // Screen is clear — cut the leftover ease so intro can start now.
            if (hole >= end - 0.001f)
                break;

            yield return null;
        }

        ResetRumble();
        _sealed = false;
        _holdAnim = false;
        SetVisible(false);
        PendingOpenAfterSceneLoad = false;
        _busy = false;
        DialogueUI.RefreshHostSortForIris();
    }

    /// <summary>Close to black and stay sealed until <see cref="CoOpen"/>.</summary>
    public IEnumerator CoCloseAndHold(float duration = -1f, bool restoreDefaultSort = true)
    {
        yield return CoClose(duration, restoreDefaultSort);
        _sealed = true;
        SetVisible(true);
        SetHole(0f);
    }

    /// <summary>Close → mid action → open.</summary>
    public IEnumerator CoTransition(Action midBlack, float closeDur = -1f, float openDur = -1f)
    {
        yield return CoCloseAndHold(closeDur);
        try
        {
            midBlack?.Invoke();
        }
        catch (Exception e)
        {
            Debug.LogException(e);
        }
        yield return null;
        yield return CoOpen(openDur);
    }

    public void SnapSealed(bool restoreDefaultSort = true)
    {
        StopActiveTracked();
        PrepareForTransition();
        SetHole(0f);
        _sealed = true;
        _busy = false;
        _holdAnim = false;
        if (restoreDefaultSort)
            RestoreDefaultSortOrder();
        SetVisible(true);
        ResetRumble();
    }

    /// <summary>
    /// Stay logically sealed (no re-close) but hide the iris so underlying UI
    /// (e.g. loading overlay) can show after a full engulf.
    /// </summary>
    public void HideVisualKeepSealed()
    {
        if (!_sealed)
            SnapSealed();
        _holdAnim = false;
        ResetRumble();
        SetVisible(false);
    }

    public void SnapOpenHidden()
    {
        StopActiveTracked();
        SetHole(openHole);
        _sealed = false;
        _busy = false;
        _holdAnim = false;
        RestoreDefaultSortOrder();
        ResetRumble();
        SetVisible(false);
        PendingOpenAfterSceneLoad = false;
    }

    /// <summary>Fire-and-forget close+hold on this component.</summary>
    public Coroutine BeginCloseAndHold(float duration = -1f)
    {
        if (_activeCr != null)
            StopCoroutine(_activeCr);
        _activeCr = StartCoroutine(CoCloseAndHoldTracked(duration));
        return _activeCr;
    }

    private IEnumerator CoCloseAndHoldTracked(float duration)
    {
        yield return CoCloseAndHold(duration);
        _activeCr = null;
    }

    public Coroutine BeginOpen(float duration = -1f)
    {
        if (_activeCr != null)
            StopCoroutine(_activeCr);
        _activeCr = StartCoroutine(CoOpenTracked(duration));
        return _activeCr;
    }

    private IEnumerator CoOpenTracked(float duration)
    {
        yield return CoOpen(duration);
        _activeCr = null;
    }
}
