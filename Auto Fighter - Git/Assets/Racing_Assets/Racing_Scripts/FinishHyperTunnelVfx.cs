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

    [Header("Layering")]
    [SerializeField] private int voidCanvasSort = 60;
    [SerializeField] private int fxCanvasSort = 120;
    [SerializeField, Range(0.15f, 1f)] private float ringAlpha = 0.85f;
    [SerializeField, Range(0.1f, 1f)] private float rayAlpha = 0.7f;
    [Tooltip("Fallback fade-in if director does not pass a duration.")]
    [SerializeField, Min(0.05f)] private float voidFadeInSeconds = 2.4f;

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
    private bool _running;
    private float _hue;
    private float _voidFadeInSecondsRuntime = 2.4f;
    private float _voidFadeDelayRuntime;
    private Coroutine _lifeCr;
    private int _savedGameCanvasSort = int.MinValue;
    private Canvas _bumpedGameCanvas;
    private readonly float[] _ringPhase = new float[64];

    public bool IsPlaying => _running;

    public void Play(float durationUnscaled, Transform camTransform, float voidFadeInSeconds = -1f, float voidFadeDelaySeconds = 0f)
    {
        Stop();
        _voidFadeInSecondsRuntime = voidFadeInSeconds > 0f ? voidFadeInSeconds : this.voidFadeInSeconds;
        _voidFadeDelayRuntime = Mathf.Max(0f, voidFadeDelaySeconds);
        if (!BindSceneRefs())
        {
            Debug.LogError("[FinishHyperTunnelVfx] Missing scene canvases. Run Racing → Setup Finish Portal System.");
            return;
        }
        _running = true;
        SetCanvasesActive(true);
        if (voidImage != null)
            voidImage.color = new Color(1f, 1f, 1f, 0f);
        ResetRingPhases();
        _lifeCr = StartCoroutine(CoPlay(durationUnscaled, persist: false));
    }

    public void PlayPersistent(Transform camTransform, float voidFadeInSeconds = -1f, float voidFadeDelaySeconds = 0f)
    {
        Stop();
        _voidFadeInSecondsRuntime = voidFadeInSeconds > 0f ? voidFadeInSeconds : this.voidFadeInSeconds;
        _voidFadeDelayRuntime = Mathf.Max(0f, voidFadeDelaySeconds);
        if (!BindSceneRefs())
        {
            Debug.LogError("[FinishHyperTunnelVfx] Missing scene canvases. Run Racing → Setup Finish Portal System.");
            return;
        }
        _running = true;
        SetCanvasesActive(true);
        if (voidImage != null)
            voidImage.color = new Color(1f, 1f, 1f, 0f);
        ResetRingPhases();
        _lifeCr = StartCoroutine(CoPlay(99999f, persist: true));
    }

    public void SetResultsFriendlyOverlay(bool resultsVisible)
    {
        if (resultsVisible)
            BumpGameCanvasAboveFx();
    }

    public void Stop()
    {
        if (_lifeCr != null)
        {
            StopCoroutine(_lifeCr);
            _lifeCr = null;
        }
        _running = false;
        RestoreGameCanvasSort();
        SetCanvasesActive(false);
    }

    private void ResetRingPhases()
    {
        for (int i = 0; i < rings.Count && i < _ringPhase.Length; i++)
            _ringPhase[i] = Mathf.Lerp(0.15f, 1.6f, (float)i / Mathf.Max(1, rings.Count - 1));
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
            Color c = Color.HSVToRGB((i * 0.08f) % 1f, 0.85f, 1f);
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
            Color c = Color.HSVToRGB((i * 0.03f) % 1f, 0.55f, 1f);
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
            _hue = (_hue + colorCycleSpeed * dt) % 1f;

            if (voidImage != null)
            {
                float h = (_hue + 0.08f * Mathf.Sin(Time.unscaledTime * 2.5f)) % 1f;
                if (h < 0f) h += 1f;
                float v = Mathf.Lerp(0.5f, 0.72f, voidOpaque);
                Color voidCol = Color.HSVToRGB(h, 0.8f, v);
                voidCol.a = voidOpaque;
                voidImage.color = voidCol;
            }

            // FX ease in with the void so world/FOV still read during delay + early fade.
            float fxMul = Mathf.Lerp(0.15f, 1f, voidOpaque);

            for (int i = 0; i < rings.Count; i++)
            {
                if (rings[i] == null || i >= ringImages.Count || ringImages[i] == null) continue;

                _ringPhase[i] += rushSpeed * (0.55f + intensity) * dt * 0.35f;
                if (_ringPhase[i] > 1.75f)
                    _ringPhase[i] -= 1.6f;

                float p = _ringPhase[i];
                float size = Mathf.Lerp(140f, 1400f, Mathf.SmoothStep(0f, 1f, p / 1.75f));
                rings[i].sizeDelta = Vector2.one * size;
                rings[i].Rotate(0f, 0f, ringSpinSpeed * dt * (i % 2 == 0 ? 1f : -1f));

                Color c = Color.HSVToRGB((_hue + i * 0.08f) % 1f, 0.85f, 1f);
                float fade = 1f - Mathf.Clamp01((_ringPhase[i] - 1.15f) / 0.6f);
                c.a = ringAlpha * fade * fxMul;
                ringImages[i].color = c;
            }

            if (raySpinRoot != null)
                raySpinRoot.Rotate(0f, 0f, raySpinSpeed * dt);

            for (int i = 0; i < rayImages.Count; i++)
            {
                if (rayImages[i] == null) continue;
                Color c = Color.HSVToRGB((_hue * 1.6f + i * 0.03f) % 1f, 0.55f, 1f);
                float pulse = 0.65f + 0.35f * Mathf.Sin(Time.unscaledTime * 11f + i);
                c.a = rayAlpha * pulse * fxMul;
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
    }
}
