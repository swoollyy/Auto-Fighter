using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// Finish hyper-tunnel as stacked UI. Scene-authored canvases (Racing → Setup Finish Portal System).
/// Runtime only plays / fades — does not spawn canvases.
/// </summary>
[DisallowMultipleComponent]
public class FinishHyperTunnelVfx : MonoBehaviour
{
    [Header("Tunnel FX")]
    [SerializeField, Min(4)] private int ringCount = 18;
    [SerializeField, Min(4)] private int rayCount = 36;
    [SerializeField, Min(0.1f)] private float colorCycleSpeed = 1.4f;
    [SerializeField, Min(0f)] private float ringSpinSpeed = 70f;
    [SerializeField, Min(0f)] private float raySpinSpeed = 95f;
    [SerializeField, Min(0.5f)] private float rushSpeed = 1.35f;
    [Tooltip("How long rings/rays take to ease in from invisible after portal entry.")]
    [SerializeField, Min(0.1f)] private float fxIntroSeconds = 1.35f;
    [Tooltip("Speed multiplier at the end of the drive (just before blackout). 1 = no ramp.")]
    [SerializeField, Min(1f)] private float speedRampEndMultiplier = 3.25f;

    [Header("Layering")]
    [SerializeField] private int voidCanvasSort = 60;
    [SerializeField] private int fxCanvasSort = 120;
    [SerializeField, Range(0.15f, 1f)] private float ringAlpha = 0.85f;
    [SerializeField, Range(0.1f, 1f)] private float rayAlpha = 0.7f;
    [Tooltip("Fallback fade-in if director does not pass a duration.")]
    [SerializeField, Min(0.05f)] private float voidFadeInSeconds = 2.4f;

    [Header("Final Blackout")]
    [Tooltip("Black disc expands from center to full screen after the void is opaque.")]
    [SerializeField, Min(0.05f)] private float finalBlackoutSeconds = 0.22f;
    [SerializeField, Min(20f)] private float finalBlackoutStartSize = 80f;
    [Tooltip("White portal-close spark after black engulfs, before results.")]
    [SerializeField, Min(0.05f)] private float portalSparkSeconds = 0.16f;
    [SerializeField, Min(10f)] private float portalSparkPeakSize = 70f;
    [Tooltip("How strong the soft white wash around the spark feels (Overlay UI can't use real URP bloom).")]
    [SerializeField, Range(0f, 1f)] private float portalSparkGlowStrength = 0.85f;

    [Header("Scene Refs")]
    [SerializeField] private Canvas voidCanvas;
    [SerializeField] private Image voidImage;
    [SerializeField] private Canvas fxCanvas;
    [SerializeField] private RectTransform fxRoot;
    [SerializeField] private RectTransform raySpinRoot;
    [SerializeField] private List<RectTransform> rings = new List<RectTransform>(24);
    [SerializeField] private List<Image> ringImages = new List<Image>(24);
    [SerializeField] private List<RectTransform> rays = new List<RectTransform>(48);
    [SerializeField] private List<Image> rayImages = new List<Image>(48);

    private Sprite _ringSprite;
    private Sprite _raySprite;
    private Sprite _discSprite;
    private Sprite _starSprite;
    private Sprite _softGlowSprite;
    private RectTransform _finalBlackout;
    private Image _finalBlackoutImage;
    private RectTransform _portalSpark;
    private Image _portalSparkImage;
    private RectTransform _portalSparkHalo;
    private Image _portalSparkHaloImage;
    private RectTransform _portalSparkWash;
    private Image _portalSparkWashImage;
    private bool _running;
    private bool _blackoutActive;
    private float _paletteClock;
    private float _voidFadeInSecondsRuntime = 2.4f;
    private float _voidFadeDelayRuntime;
    private float _driveSecondsRuntime = 4.5f;
    private Coroutine _lifeCr;
    private int _savedGameCanvasSort = int.MinValue;
    private Canvas _bumpedGameCanvas;
    private readonly float[] _ringPhase = new float[64];

    // Purple (light→dark), blues, some greens, white — nothing else.
    private static readonly Color[] TunnelPalette =
    {
        new Color(0.92f, 0.78f, 1.00f), // light purple
        new Color(0.72f, 0.42f, 1.00f), // mid purple
        new Color(0.48f, 0.18f, 0.88f), // deep purple
        new Color(0.32f, 0.08f, 0.58f), // dark purple
        new Color(0.55f, 0.35f, 0.95f), // violet
        new Color(0.40f, 0.62f, 1.00f), // bright blue
        new Color(0.22f, 0.42f, 0.95f), // mid blue
        new Color(0.12f, 0.22f, 0.72f), // dark blue
        new Color(0.55f, 0.82f, 1.00f), // light blue
        new Color(0.25f, 0.88f, 0.48f), // green
        new Color(0.18f, 0.70f, 0.42f), // deep green
        new Color(0.55f, 0.95f, 0.65f), // light green
        new Color(1.00f, 1.00f, 1.00f), // white
        new Color(0.88f, 0.92f, 1.00f), // cool white
    };

    private static Color SampleTunnelPalette(float t01)
    {
        int n = TunnelPalette.Length;
        float f = Mathf.Repeat(t01, 1f) * n;
        int i0 = Mathf.FloorToInt(f) % n;
        int i1 = (i0 + 1) % n;
        return Color.Lerp(TunnelPalette[i0], TunnelPalette[i1], f - Mathf.Floor(f));
    }

    public bool IsPlaying => _running;

    public void Play(float durationUnscaled, Transform camTransform, float voidFadeInSeconds = -1f, float voidFadeDelaySeconds = 0f)
    {
        Stop();
        _voidFadeInSecondsRuntime = voidFadeInSeconds > 0f ? voidFadeInSeconds : this.voidFadeInSeconds;
        _voidFadeDelayRuntime = Mathf.Max(0f, voidFadeDelaySeconds);
        _driveSecondsRuntime = Mathf.Max(0.5f, durationUnscaled);
        if (!BindSceneRefs())
        {
            Debug.LogError("[FinishHyperTunnelVfx] Missing scene canvases. Run Racing → Setup Finish Portal System.");
            return;
        }
        _running = true;
        _blackoutActive = false;
        SetCanvasesActive(true);
        ResetFinalBlackoutVisual();
        if (voidImage != null)
            voidImage.color = new Color(1f, 1f, 1f, 0f);
        ResetRingPhases();
        HideTunnelSprites();
        _lifeCr = StartCoroutine(CoPlay(durationUnscaled, persist: false));
    }

    public void PlayPersistent(
        Transform camTransform,
        float voidFadeInSeconds = -1f,
        float voidFadeDelaySeconds = 0f,
        float expectedDriveSeconds = -1f)
    {
        Stop();
        _voidFadeInSecondsRuntime = voidFadeInSeconds > 0f ? voidFadeInSeconds : this.voidFadeInSeconds;
        _voidFadeDelayRuntime = Mathf.Max(0f, voidFadeDelaySeconds);
        float fallbackDrive = _voidFadeDelayRuntime + _voidFadeInSecondsRuntime + 1.5f;
        _driveSecondsRuntime = expectedDriveSeconds > 0f ? expectedDriveSeconds : fallbackDrive;
        if (!BindSceneRefs())
        {
            Debug.LogError("[FinishHyperTunnelVfx] Missing scene canvases. Run Racing → Setup Finish Portal System.");
            return;
        }
        _running = true;
        _blackoutActive = false;
        SetCanvasesActive(true);
        ResetFinalBlackoutVisual();
        if (voidImage != null)
            voidImage.color = new Color(1f, 1f, 1f, 0f);
        ResetRingPhases();
        HideTunnelSprites();
        _lifeCr = StartCoroutine(CoPlay(99999f, persist: true));
    }

    public void SetResultsFriendlyOverlay(bool resultsVisible)
    {
        if (resultsVisible)
            BumpGameCanvasAboveFx();
    }

    /// <summary>
    /// After the colored void is fully opaque: fast black engulf, then a white portal-close spark.
    /// Yields until black + spark finish. Call before showing results.
    /// </summary>
    public IEnumerator CoFinalBlackout(float durationSeconds = -1f)
    {
        if (!BindSceneRefs())
            yield break;

        EnsureSprites();
        EnsureFinalBlackout();
        EnsurePortalSpark();
        if (_finalBlackout == null || _finalBlackoutImage == null)
            yield break;

        float dur = durationSeconds > 0f ? durationSeconds : finalBlackoutSeconds;
        dur = Mathf.Max(0.05f, dur);

        _blackoutActive = true;
        _finalBlackout.gameObject.SetActive(true);
        _finalBlackout.SetAsLastSibling();
        _finalBlackoutImage.color = Color.black;
        _finalBlackoutImage.sprite = _discSprite;

        float start = Mathf.Max(20f, finalBlackoutStartSize);
        float cover = Mathf.Max(fxRoot.rect.width, fxRoot.rect.height);
        if (cover < 32f)
            cover = Mathf.Max(Screen.width, Screen.height);
        // Overshoot so soft edge fully clears the corners.
        float end = cover * 1.75f;

        _finalBlackout.sizeDelta = Vector2.one * start;

        float t0 = Time.unscaledTime;
        float t1 = t0 + dur;
        while (Time.unscaledTime < t1)
        {
            // Ease-out so it snaps across the screen quickly.
            float linear = Mathf.Clamp01(Mathf.InverseLerp(t0, t1, Time.unscaledTime));
            float u = 1f - (1f - linear) * (1f - linear);
            _finalBlackout.sizeDelta = Vector2.one * Mathf.Lerp(start, end, u);
            yield return null;
        }

        _finalBlackout.sizeDelta = Vector2.one * end;
        // Solid black under results — void also locked to black so soft edges can't leak color.
        if (voidImage != null)
            voidImage.color = new Color(0f, 0f, 0f, 1f);
        for (int i = 0; i < ringImages.Count; i++)
        {
            if (ringImages[i] != null)
                ringImages[i].enabled = false;
        }
        for (int i = 0; i < rayImages.Count; i++)
        {
            if (rayImages[i] != null)
                rayImages[i].enabled = false;
        }

        // Closing spark: white flash in the center (player / portal vanishing), then settle to black.
        yield return CoPortalCloseSpark();
    }

    private IEnumerator CoPortalCloseSpark()
    {
        EnsurePortalSpark();
        if (_portalSpark == null || _portalSparkImage == null)
            yield break;

        float sparkDur = Mathf.Max(0.05f, portalSparkSeconds);
        float peak = Mathf.Max(40f, portalSparkPeakSize);
        float glow = Mathf.Clamp01(portalSparkGlowStrength);

        // Overlay UI is drawn after URP post — real bloom can't see this sprite.
        // Fake it: soft halo + ambient white wash stacked under a bright star.
        if (_portalSparkWash != null)
        {
            _portalSparkWash.gameObject.SetActive(true);
            _portalSparkWash.SetAsLastSibling();
            _portalSparkWashImage.color = new Color(1f, 1f, 1f, 0f);
        }
        if (_portalSparkHalo != null)
        {
            _portalSparkHalo.gameObject.SetActive(true);
            _portalSparkHalo.SetAsLastSibling();
            _portalSparkHalo.localRotation = Quaternion.identity;
            _portalSparkHalo.sizeDelta = Vector2.one * (peak * 0.55f);
            _portalSparkHaloImage.color = new Color(1f, 1f, 1f, 0f);
        }

        _portalSpark.gameObject.SetActive(true);
        _portalSpark.SetAsLastSibling();
        _portalSpark.localRotation = Quaternion.identity;
        _portalSpark.sizeDelta = Vector2.one * (peak * 0.2f);
        _portalSparkImage.sprite = _starSprite != null ? _starSprite : _discSprite;
        _portalSparkImage.color = new Color(1f, 1f, 1f, 0f);

        float t0 = Time.unscaledTime;
        float t1 = t0 + sparkDur;
        while (Time.unscaledTime < t1)
        {
            float u = Mathf.Clamp01(Mathf.InverseLerp(t0, t1, Time.unscaledTime));
            // Bright pop early, then fade — slight spin so it reads as a closing spark.
            float pop = u < 0.22f
                ? Mathf.SmoothStep(0f, 1f, u / 0.22f)
                : 1f - Mathf.SmoothStep(0f, 1f, (u - 0.22f) / 0.78f);
            float sizeT = Mathf.SmoothStep(0f, 1f, u);
            float rot = Mathf.Lerp(0f, 35f, sizeT);

            _portalSpark.sizeDelta = Vector2.one * Mathf.Lerp(peak * 0.2f, peak, sizeT);
            _portalSpark.localRotation = Quaternion.Euler(0f, 0f, rot);
            // Keep star fully bright while visible (alpha only fades the edges via sprite).
            _portalSparkImage.color = new Color(1f, 1f, 1f, pop);

            if (_portalSparkHalo != null)
            {
                _portalSparkHalo.sizeDelta = Vector2.one * Mathf.Lerp(peak * 0.55f, peak * 2.4f, sizeT);
                _portalSparkHalo.localRotation = Quaternion.Euler(0f, 0f, -rot * 0.6f);
                _portalSparkHaloImage.color = new Color(1f, 1f, 1f, pop * 0.75f * glow);
            }

            if (_portalSparkWash != null)
            {
                // Ambient white that breathes out from the center over the black screen.
                float wash = pop * 0.55f * glow;
                _portalSparkWashImage.color = new Color(1f, 1f, 1f, wash);
            }

            yield return null;
        }

        _portalSparkImage.color = new Color(1f, 1f, 1f, 0f);
        _portalSpark.gameObject.SetActive(false);
        if (_portalSparkHalo != null)
        {
            _portalSparkHaloImage.color = new Color(1f, 1f, 1f, 0f);
            _portalSparkHalo.gameObject.SetActive(false);
        }
        if (_portalSparkWash != null)
        {
            _portalSparkWashImage.color = new Color(1f, 1f, 1f, 0f);
            _portalSparkWash.gameObject.SetActive(false);
        }
    }

    public void Stop()
    {
        if (_lifeCr != null)
        {
            StopCoroutine(_lifeCr);
            _lifeCr = null;
        }
        _running = false;
        _blackoutActive = false;
        ResetFinalBlackoutVisual();
        RestoreGameCanvasSort();
        SetCanvasesActive(false);
    }

    private void ResetRingPhases()
    {
        // Start near the center (small) so rings rush outward as they fade in — not already mid-screen.
        for (int i = 0; i < rings.Count && i < _ringPhase.Length; i++)
        {
            _ringPhase[i] = Mathf.Lerp(0.02f, 0.28f, (float)i / Mathf.Max(1, rings.Count - 1));
            if (rings[i] != null)
                rings[i].sizeDelta = Vector2.one * Mathf.Lerp(80f, 220f, _ringPhase[i] / 0.28f);
        }
    }

    private void HideTunnelSprites()
    {
        for (int i = 0; i < ringImages.Count; i++)
        {
            if (ringImages[i] == null) continue;
            Color c = ringImages[i].color;
            c.a = 0f;
            ringImages[i].color = c;
        }
        for (int i = 0; i < rayImages.Count; i++)
        {
            if (rayImages[i] == null) continue;
            Color c = rayImages[i].color;
            c.a = 0f;
            rayImages[i].color = c;
        }
    }

    private void EnsureFinalBlackout()
    {
        if (fxRoot == null) return;
        if (_finalBlackoutImage != null && _finalBlackout != null) return;

        var existing = fxRoot.Find("FinalBlackout");
        if (existing != null)
        {
            _finalBlackout = existing as RectTransform;
            _finalBlackoutImage = existing.GetComponent<Image>();
        }

        if (_finalBlackout == null)
        {
            var go = new GameObject("FinalBlackout", typeof(RectTransform));
            go.transform.SetParent(fxRoot, false);
            _finalBlackout = go.GetComponent<RectTransform>();
            _finalBlackout.anchorMin = _finalBlackout.anchorMax = new Vector2(0.5f, 0.5f);
            _finalBlackout.pivot = new Vector2(0.5f, 0.5f);
            _finalBlackoutImage = go.AddComponent<Image>();
            _finalBlackoutImage.raycastTarget = false;
            _finalBlackoutImage.preserveAspect = true;
        }

        if (_finalBlackoutImage != null && _discSprite != null)
            _finalBlackoutImage.sprite = _discSprite;
    }

    private void EnsurePortalSpark()
    {
        if (fxRoot == null) return;

        if (_portalSparkWash == null)
        {
            var washT = fxRoot.Find("PortalCloseWash");
            if (washT != null)
            {
                _portalSparkWash = washT as RectTransform;
                _portalSparkWashImage = washT.GetComponent<Image>();
            }
            else
            {
                var washGo = new GameObject("PortalCloseWash", typeof(RectTransform));
                washGo.transform.SetParent(fxRoot, false);
                _portalSparkWash = washGo.GetComponent<RectTransform>();
                StretchFull(_portalSparkWash);
                _portalSparkWashImage = washGo.AddComponent<Image>();
                _portalSparkWashImage.raycastTarget = false;
            }
            if (_portalSparkWashImage != null && _softGlowSprite != null)
                _portalSparkWashImage.sprite = _softGlowSprite;
            if (_portalSparkWash != null)
                _portalSparkWash.gameObject.SetActive(false);
        }

        if (_portalSparkHalo == null)
        {
            var haloT = fxRoot.Find("PortalCloseHalo");
            if (haloT != null)
            {
                _portalSparkHalo = haloT as RectTransform;
                _portalSparkHaloImage = haloT.GetComponent<Image>();
            }
            else
            {
                var haloGo = new GameObject("PortalCloseHalo", typeof(RectTransform));
                haloGo.transform.SetParent(fxRoot, false);
                _portalSparkHalo = haloGo.GetComponent<RectTransform>();
                _portalSparkHalo.anchorMin = _portalSparkHalo.anchorMax = new Vector2(0.5f, 0.5f);
                _portalSparkHalo.pivot = new Vector2(0.5f, 0.5f);
                _portalSparkHaloImage = haloGo.AddComponent<Image>();
                _portalSparkHaloImage.raycastTarget = false;
                _portalSparkHaloImage.preserveAspect = true;
            }
            if (_portalSparkHaloImage != null)
                _portalSparkHaloImage.sprite = _softGlowSprite != null ? _softGlowSprite : _discSprite;
            if (_portalSparkHalo != null)
                _portalSparkHalo.gameObject.SetActive(false);
        }

        if (_portalSparkImage != null && _portalSpark != null)
        {
            _portalSparkImage.sprite = _starSprite != null ? _starSprite : _discSprite;
            return;
        }

        var existing = fxRoot.Find("PortalCloseSpark");
        if (existing != null)
        {
            _portalSpark = existing as RectTransform;
            _portalSparkImage = existing.GetComponent<Image>();
        }

        if (_portalSpark == null)
        {
            var go = new GameObject("PortalCloseSpark", typeof(RectTransform));
            go.transform.SetParent(fxRoot, false);
            _portalSpark = go.GetComponent<RectTransform>();
            _portalSpark.anchorMin = _portalSpark.anchorMax = new Vector2(0.5f, 0.5f);
            _portalSpark.pivot = new Vector2(0.5f, 0.5f);
            _portalSparkImage = go.AddComponent<Image>();
            _portalSparkImage.raycastTarget = false;
            _portalSparkImage.preserveAspect = true;
        }

        if (_portalSparkImage != null)
            _portalSparkImage.sprite = _starSprite != null ? _starSprite : _discSprite;
        if (_portalSpark != null)
            _portalSpark.gameObject.SetActive(false);
    }

    private void ResetFinalBlackoutVisual()
    {
        if (_finalBlackout != null)
        {
            _finalBlackout.gameObject.SetActive(false);
            _finalBlackout.sizeDelta = Vector2.one * finalBlackoutStartSize;
        }
        if (_portalSpark != null)
        {
            _portalSpark.gameObject.SetActive(false);
            if (_portalSparkImage != null)
                _portalSparkImage.color = new Color(1f, 1f, 1f, 0f);
        }
        if (_portalSparkHalo != null)
        {
            _portalSparkHalo.gameObject.SetActive(false);
            if (_portalSparkHaloImage != null)
                _portalSparkHaloImage.color = new Color(1f, 1f, 1f, 0f);
        }
        if (_portalSparkWash != null)
        {
            _portalSparkWash.gameObject.SetActive(false);
            if (_portalSparkWashImage != null)
                _portalSparkWashImage.color = new Color(1f, 1f, 1f, 0f);
        }
        for (int i = 0; i < ringImages.Count; i++)
        {
            if (ringImages[i] != null)
                ringImages[i].enabled = true;
        }
        for (int i = 0; i < rayImages.Count; i++)
        {
            if (rayImages[i] != null)
                rayImages[i].enabled = true;
        }
    }

    private bool BindSceneRefs()
    {
        if (voidCanvas == null)
        {
            var t = transform.Find("HyperTunnelVoidCanvas");
            if (t != null) voidCanvas = t.GetComponent<Canvas>();
        }
        if (fxCanvas == null)
        {
            var t = transform.Find("HyperTunnelFxCanvas");
            if (t != null) fxCanvas = t.GetComponent<Canvas>();
        }
        if (voidCanvas != null && voidImage == null)
        {
            var imgT = voidCanvas.transform.Find("Void");
            if (imgT != null) voidImage = imgT.GetComponent<Image>();
        }
        if (fxCanvas != null && fxRoot == null)
        {
            var rootT = fxCanvas.transform.Find("FxRoot");
            if (rootT != null) fxRoot = rootT as RectTransform;
        }
        if (fxRoot != null && raySpinRoot == null)
        {
            var rayT = fxRoot.Find("RaySpin");
            if (rayT != null) raySpinRoot = rayT as RectTransform;
        }

        if (voidCanvas != null)
            voidCanvas.sortingOrder = voidCanvasSort;
        if (fxCanvas != null)
            fxCanvas.sortingOrder = fxCanvasSort;

        return voidCanvas != null && voidImage != null && fxCanvas != null && fxRoot != null;
    }

    private void SetCanvasesActive(bool on)
    {
        if (voidCanvas != null)
            voidCanvas.gameObject.SetActive(on);
        if (fxCanvas != null)
            fxCanvas.gameObject.SetActive(on);
    }

    private void BumpGameCanvasAboveFx()
    {
        var ui = Object.FindObjectOfType<UIManager_Racing>(true);
        if (ui == null || ui.GameCanvas == null) return;
        var canvas = ui.GameCanvas.GetComponent<Canvas>();
        if (canvas == null) return;

        if (_bumpedGameCanvas == null)
        {
            _bumpedGameCanvas = canvas;
            _savedGameCanvasSort = canvas.sortingOrder;
        }
        canvas.overrideSorting = true;
        canvas.sortingOrder = Mathf.Max(canvas.sortingOrder, fxCanvasSort + 50);
    }

    private void RestoreGameCanvasSort()
    {
        if (_bumpedGameCanvas != null && _savedGameCanvasSort != int.MinValue)
            _bumpedGameCanvas.sortingOrder = _savedGameCanvasSort;
        _bumpedGameCanvas = null;
        _savedGameCanvasSort = int.MinValue;
    }

#if UNITY_EDITOR
    [ContextMenu("Rebuild Tunnel Hierarchy")]
    public void EditorRebuildHierarchy()
    {
        EnsureSprites();

        // Wipe previous children so rebuild is deterministic.
        for (int i = transform.childCount - 1; i >= 0; i--)
            DestroyImmediate(transform.GetChild(i).gameObject);

        rings.Clear();
        ringImages.Clear();
        rays.Clear();
        rayImages.Clear();

        // Void canvas
        var voidGo = new GameObject("HyperTunnelVoidCanvas", typeof(RectTransform));
        voidGo.transform.SetParent(transform, false);
        voidCanvas = voidGo.AddComponent<Canvas>();
        voidCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
        voidCanvas.sortingOrder = voidCanvasSort;
        voidGo.AddComponent<CanvasScaler>().uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        voidGo.AddComponent<GraphicRaycaster>().enabled = false;

        var imgGo = new GameObject("Void", typeof(RectTransform));
        imgGo.transform.SetParent(voidGo.transform, false);
        voidImage = imgGo.AddComponent<Image>();
        voidImage.raycastTarget = false;
        voidImage.color = new Color(1f, 0f, 1f, 0.35f); // visible in editor while selecting
        StretchFull(voidImage.rectTransform);

        // FX canvas
        var fxGo = new GameObject("HyperTunnelFxCanvas", typeof(RectTransform));
        fxGo.transform.SetParent(transform, false);
        fxCanvas = fxGo.AddComponent<Canvas>();
        fxCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
        fxCanvas.sortingOrder = fxCanvasSort;
        fxGo.AddComponent<CanvasScaler>().uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        fxGo.AddComponent<GraphicRaycaster>().enabled = false;

        var rootGo = new GameObject("FxRoot", typeof(RectTransform));
        rootGo.transform.SetParent(fxGo.transform, false);
        fxRoot = rootGo.GetComponent<RectTransform>();
        StretchFull(fxRoot);

        var raySpinGo = new GameObject("RaySpin", typeof(RectTransform));
        raySpinGo.transform.SetParent(fxRoot, false);
        raySpinRoot = raySpinGo.GetComponent<RectTransform>();
        StretchFull(raySpinRoot);

        for (int i = 0; i < ringCount; i++)
        {
            var ringGo = new GameObject($"Ring_{i}", typeof(RectTransform));
            ringGo.transform.SetParent(fxRoot, false);
            var rt = ringGo.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            float start = Mathf.Lerp(0.15f, 1.6f, (float)i / Mathf.Max(1, ringCount - 1));
            rt.sizeDelta = Vector2.one * (220f * start);
            var img = ringGo.AddComponent<Image>();
            img.sprite = _ringSprite;
            img.type = Image.Type.Simple;
            img.preserveAspect = true;
            img.raycastTarget = false;
            Color c = SampleTunnelPalette(i / (float)Mathf.Max(1, ringCount));
            c.a = ringAlpha;
            img.color = c;
            rings.Add(rt);
            ringImages.Add(img);
        }

        for (int i = 0; i < rayCount; i++)
        {
            var rayGo = new GameObject($"Ray_{i}", typeof(RectTransform));
            rayGo.transform.SetParent(raySpinRoot, false);
            var rt = rayGo.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0f);
            rt.sizeDelta = new Vector2(14f, 900f);
            rt.localRotation = Quaternion.Euler(0f, 0f, (360f / rayCount) * i);
            var img = rayGo.AddComponent<Image>();
            img.sprite = _raySprite;
            img.type = Image.Type.Simple;
            img.raycastTarget = false;
            Color c = SampleTunnelPalette(0.35f + i / (float)Mathf.Max(1, rayCount));
            c.a = rayAlpha * 0.5f;
            img.color = c;
            rays.Add(rt);
            rayImages.Add(img);
        }

        // Start disabled so they don't cover the editor Game view constantly.
        voidGo.SetActive(false);
        fxGo.SetActive(false);

        EditorUtility.SetDirty(this);
        if (!Application.isPlaying)
            EditorUtility.SetDirty(gameObject);
    }
#endif

    private static void StretchFull(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
        rt.localScale = Vector3.one;
    }

    private void EnsureSprites()
    {
        if (_ringSprite == null)
            _ringSprite = BuildRingSprite(128, 0.72f, 0.92f);
        if (_raySprite == null)
            _raySprite = BuildRaySprite(16, 128);
        if (_discSprite == null)
            _discSprite = BuildFilledCircleSprite(128);
        if (_softGlowSprite == null)
            _softGlowSprite = BuildSoftGlowSprite(128);
        if (_starSprite == null)
            _starSprite = BuildStarSprite(160, points: 4);
    }

    private void Awake()
    {
        EnsureSprites();
        BindSceneRefs();
        // Re-apply procedural sprites to scene Images (sprites aren't serialized as assets).
        for (int i = 0; i < ringImages.Count; i++)
        {
            if (ringImages[i] != null)
                ringImages[i].sprite = _ringSprite;
        }
        for (int i = 0; i < rayImages.Count; i++)
        {
            if (rayImages[i] != null)
                rayImages[i].sprite = _raySprite;
        }
        SetCanvasesActive(false);
    }

    private static Sprite BuildRingSprite(int size, float inner, float outer)
    {
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.wrapMode = TextureWrapMode.Clamp;
        tex.filterMode = FilterMode.Bilinear;
        float mid = (size - 1) * 0.5f;
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            float nx = (x - mid) / mid;
            float ny = (y - mid) / mid;
            float r = Mathf.Sqrt(nx * nx + ny * ny);
            float a = 0f;
            if (r >= inner && r <= outer)
            {
                float edge = Mathf.Min(r - inner, outer - r);
                a = Mathf.Clamp01(edge / 0.06f);
            }
            tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
        }
        tex.Apply(false, false);
        return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
    }

    private static Sprite BuildFilledCircleSprite(int size)
    {
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.wrapMode = TextureWrapMode.Clamp;
        tex.filterMode = FilterMode.Bilinear;
        float mid = (size - 1) * 0.5f;
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            float nx = (x - mid) / mid;
            float ny = (y - mid) / mid;
            float r = Mathf.Sqrt(nx * nx + ny * ny);
            // Soft outer edge so the expand doesn't look aliased.
            float a = Mathf.Clamp01((1f - r) / 0.04f);
            tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
        }
        tex.Apply(false, false);
        return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
    }

    /// <summary>Soft radial glow used as halo / ambient wash behind the star.</summary>
    private static Sprite BuildSoftGlowSprite(int size)
    {
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.wrapMode = TextureWrapMode.Clamp;
        tex.filterMode = FilterMode.Bilinear;
        float mid = (size - 1) * 0.5f;
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            float nx = (x - mid) / mid;
            float ny = (y - mid) / mid;
            float r = Mathf.Sqrt(nx * nx + ny * ny);
            // Wide soft falloff — reads like bloom bleeding off a bright point.
            float a = Mathf.Clamp01(1f - r);
            a = a * a * (0.35f + 0.65f * a);
            tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
        }
        tex.Apply(false, false);
        return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
    }

    /// <summary>Soft 4-point (or N-point) star for the portal-close spark.</summary>
    private static Sprite BuildStarSprite(int size, int points = 4)
    {
        points = Mathf.Max(3, points);
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.wrapMode = TextureWrapMode.Clamp;
        tex.filterMode = FilterMode.Bilinear;
        float mid = (size - 1) * 0.5f;
        float outer = 0.92f;
        float inner = 0.26f;
        // Rotate so a tip points up.
        float angleOffset = -Mathf.PI * 0.5f;

        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            float nx = (x - mid) / mid;
            float ny = (y - mid) / mid;
            float r = Mathf.Sqrt(nx * nx + ny * ny);
            float a = 0f;
            if (r < 1.05f)
            {
                float ang = Mathf.Atan2(ny, nx) - angleOffset;
                // Map angle into one star sector (tip → valley → tip).
                float sector = (Mathf.PI * 2f) / points;
                float local = Mathf.Repeat(ang, sector) / sector; // 0..1
                // Distance from tip (0) to valley (0.5) back to tip (1).
                float toValley = 1f - Mathf.Abs(local * 2f - 1f);
                float edgeR = Mathf.Lerp(outer, inner, toValley);

                float edge = Mathf.Clamp01((edgeR - r) / 0.07f);
                float core = Mathf.Clamp01(1f - r / (inner * 1.1f));
                // Hot white core + soft bloom halo around the star body.
                float body = edge * (0.45f + 0.55f * core);
                float halo = Mathf.Clamp01(1f - r / 0.95f);
                halo = halo * halo * 0.55f;
                a = Mathf.Clamp01(Mathf.Max(body, halo));
            }
            tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
        }
        tex.Apply(false, false);
        return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
    }

    private static Sprite BuildRaySprite(int width, int height)
    {
        var tex = new Texture2D(width, height, TextureFormat.RGBA32, false);
        tex.wrapMode = TextureWrapMode.Clamp;
        tex.filterMode = FilterMode.Bilinear;
        float mid = (width - 1) * 0.5f;
        for (int y = 0; y < height; y++)
        {
            float along = y / (float)(height - 1);
            float tip = Mathf.SmoothStep(0.15f, 1f, along);
            for (int x = 0; x < width; x++)
            {
                float nx = Mathf.Abs(x - mid) / mid;
                float a = Mathf.Clamp01(1f - nx) * tip;
                a *= a;
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
            }
        }
        tex.Apply(false, false);
        return Sprite.Create(tex, new Rect(0, 0, width, height), new Vector2(0.5f, 0f), 100f);
    }

    private IEnumerator CoPlay(float durationUnscaled, bool persist)
    {
        float t0 = Time.unscaledTime;
        float t1 = t0 + Mathf.Max(0.5f, durationUnscaled);
        float fadeDelay = Mathf.Max(0f, _voidFadeDelayRuntime);
        float fadeIn = Mathf.Max(0.05f, _voidFadeInSecondsRuntime);
        float intensity = 0f;

        while (persist || Time.unscaledTime < t1)
        {
            float elapsed = Time.unscaledTime - t0;
            float afterDelay = Mathf.Max(0f, elapsed - fadeDelay);
            // Stay transparent during delay, then 0 → 1 over fadeIn.
            float voidOpaque = fadeDelay > 0f && elapsed < fadeDelay
                ? 0f
                : Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(afterDelay / fadeIn));
            float u = persist
                ? voidOpaque
                : Mathf.Max(voidOpaque, Mathf.InverseLerp(t0, t1, Time.unscaledTime));
            intensity = Mathf.Max(intensity, u);

            float dt = Time.unscaledDeltaTime;
            if (dt <= 0f) dt = 1f / 60f;

            // Final black disc owns the frame — freeze colored tunnel updates underneath.
            if (_blackoutActive)
            {
                yield return null;
                continue;
            }

            // Ease rings/rays in from 0 (was hard-starting at ~15% alpha = "pop in").
            float intro = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / Mathf.Max(0.1f, fxIntroSeconds)));
            // Keep early FX soft while the world is still visible; settle to full once void owns the frame.
            float fxMul = intro * Mathf.Lerp(0.55f, 1f, voidOpaque);

            // Accelerate spin/rush through the drive until blackout.
            float drive01 = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / Mathf.Max(0.5f, _driveSecondsRuntime)));
            float speedMul = Mathf.Lerp(1f, speedRampEndMultiplier, drive01);
            float rushMul = rushSpeed * speedMul * (0.45f + 0.55f * intro);
            float ringSpinMul = ringSpinSpeed * speedMul;
            float raySpinMul = raySpinSpeed * speedMul;
            float colorMul = colorCycleSpeed * Mathf.Lerp(1f, 1.6f, drive01);

            _paletteClock = (_paletteClock + colorMul * dt * 0.12f) % 1f;

            if (voidImage != null)
            {
                // Void stays in deep purple/blue — no rainbow wash behind the rings.
                float voidT = Mathf.Repeat(_paletteClock * 0.35f + 0.05f * Mathf.Sin(Time.unscaledTime * 2.5f), 1f);
                Color voidCol = SampleTunnelPalette(voidT) * Mathf.Lerp(0.55f, 0.85f, voidOpaque);
                voidCol.a = voidOpaque;
                voidImage.color = voidCol;
            }

            for (int i = 0; i < rings.Count; i++)
            {
                if (rings[i] == null || i >= ringImages.Count || ringImages[i] == null) continue;

                // Stagger intro slightly per ring so they don't all bloom at once.
                float ringIntro = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((elapsed - i * 0.03f) / Mathf.Max(0.1f, fxIntroSeconds)));

                _ringPhase[i] += rushMul * (0.55f + intensity) * dt * 0.35f;
                if (_ringPhase[i] > 1.75f)
                    _ringPhase[i] -= 1.6f;

                float p = _ringPhase[i];
                float size = Mathf.Lerp(140f, 1400f, Mathf.SmoothStep(0f, 1f, p / 1.75f));
                rings[i].sizeDelta = Vector2.one * size;
                rings[i].Rotate(0f, 0f, ringSpinMul * dt * (i % 2 == 0 ? 1f : -1f));

                Color c = SampleTunnelPalette(_paletteClock + i * (1f / TunnelPalette.Length));
                float fade = 1f - Mathf.Clamp01((_ringPhase[i] - 1.15f) / 0.6f);
                c.a = ringAlpha * fade * fxMul * ringIntro;
                ringImages[i].color = c;
            }

            if (raySpinRoot != null)
                raySpinRoot.Rotate(0f, 0f, raySpinMul * dt);

            for (int i = 0; i < rayImages.Count; i++)
            {
                if (rayImages[i] == null) continue;
                float rayIntro = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((elapsed - 0.08f - i * 0.008f) / Mathf.Max(0.1f, fxIntroSeconds)));
                Color c = SampleTunnelPalette(_paletteClock * 0.7f + 0.4f + i * 0.045f);
                float pulse = 0.65f + 0.35f * Mathf.Sin(Time.unscaledTime * (11f + 6f * drive01) + i);
                c.a = rayAlpha * pulse * fxMul * rayIntro;
                rayImages[i].color = c;

                if (i < rays.Count && rays[i] != null)
                {
                    float w = Mathf.Lerp(8f, 22f, intensity) * pulse;
                    float h = Mathf.Lerp(520f, 1100f, intensity);
                    rays[i].sizeDelta = new Vector2(w, h);
                }
            }

            yield return null;
        }

        Stop();
    }

    private void OnDestroy()
    {
        Stop();
        if (_ringSprite != null)
            Destroy(_ringSprite.texture);
        if (_raySprite != null)
            Destroy(_raySprite.texture);
        if (_discSprite != null)
            Destroy(_discSprite.texture);
        if (_starSprite != null)
            Destroy(_starSprite.texture);
        if (_softGlowSprite != null)
            Destroy(_softGlowSprite.texture);
    }
}
