using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Timed run-start card shown after loading drops and before the player gets car control.
/// Displays the current day/run count over max Vintage TV static. All CRT look values for
/// this window are editable on this component.
/// </summary>
[DisallowMultipleComponent]
public class RunStartIntroUI : MonoBehaviour
{
    public enum RunCountSource
    {
        /// <summary>DayTrialManager.CurrentDay (day within the active trial).</summary>
        TrialDay = 0,
        /// <summary>NarrativeDirector completed runs + 1 (session run index).</summary>
        SessionRunNumber = 1
    }

    [Header("Timing")]
    [Tooltip("How long the intro holds (unscaled seconds) before the player gets control.")]
    [SerializeField, Min(0.05f)] private float holdDuration = 2.2f;
    [SerializeField, Min(0f)] private float fadeInSeconds = 0.2f;
    [Tooltip("UI fade + CRT blend back to gameplay Vintage TV defaults (unscaled seconds).")]
    [SerializeField, Min(0f)] private float fadeOutSeconds = 1f;

    [Header("Run Count Display")]
    [SerializeField] private RunCountSource countSource = RunCountSource.TrialDay;
    [Tooltip("Use {0} for the run/day number. Optional {1} = day limit (trial day mode).")]
    [SerializeField] private string titleFormat = "DAY {0}";
    [SerializeField] private string subtitleWhenLimitKnown = "OF {1}";
    [SerializeField] private bool showSubtitleWithDayLimit = true;

    [Header("UI (optional — leave empty to auto-build)")]
    [SerializeField] private Canvas canvas;
    [SerializeField] private CanvasGroup canvasGroup;
    [SerializeField] private TextMeshProUGUI titleText;
    [SerializeField] private TextMeshProUGUI subtitleText;

    [Header("Auto-built Look")]
    [SerializeField] private Color titleColor = new Color(1f, 0.847f, 0.302f, 1f);
    [SerializeField] private Color subtitleColor = new Color(1f, 1f, 1f, 0.75f);
    [SerializeField] private float titleFontSize = 96f;
    [SerializeField] private float subtitleFontSize = 36f;

    [Header("Vintage TV During Intro")]
    [Tooltip("CRT look forced for the entire hold. Defaults are high-noise / high-static.")]
    [SerializeField] private VintageTVLookSettings introTvLook = VintageTVLookSettings.CreateMaxNoiseDefaults();

    [Tooltip("Optional explicit TV controller. Leave empty to auto-find.")]
    [SerializeField] private VintageTVController vintageTv;

    [Header("Enable")]
    [SerializeField] private bool playOnEveryRun = true;

    private bool _builtRuntimeUi;
    private bool _playing;

    /// <summary>Inspector-editable CRT settings used for the intro window.</summary>
    public VintageTVLookSettings IntroTvLook => introTvLook;

    public float HoldDuration => holdDuration;

    /// <summary>
    /// Clear any stuck in-progress intro so the next run can show DAY N again
    /// (important for quick-replay, which does not reload the scene).
    /// </summary>
    public void PrepareForNewRun()
    {
        _playing = false;
        if (canvasGroup != null)
            canvasGroup.alpha = 0f;
        if (canvas != null)
            canvas.gameObject.SetActive(false);
        vintageTv?.EndIntroOverride(0f);
    }

    /// <summary>
    /// Put DAY/RUN text + TV static up immediately at full strength so they read through
    /// the goo iris open. Pair with <see cref="CoHoldAndFadeOut"/> after the iris finishes.
    /// </summary>
    public bool ShowIntroImmediate()
    {
        if (!playOnEveryRun)
            return false;

        if (_playing)
            PrepareForNewRun();

        _playing = true;
        EnsureUi();
        ResolveVintageTv();

        int number = ResolveRunNumber(out int dayLimit);
        ApplyTexts(number, dayLimit);

        if (canvas != null)
            canvas.gameObject.SetActive(true);
        if (canvasGroup != null)
        {
            canvasGroup.alpha = 1f;
            canvasGroup.blocksRaycasts = true;
            canvasGroup.interactable = false;
        }

        vintageTv?.BeginIntroOverride(introTvLook);
        return true;
    }

    /// <summary>
    /// After the iris has opened: keep the authored hold, then fade text + static out.
    /// </summary>
    public IEnumerator CoHoldAndFadeOut()
    {
        if (!_playing)
            yield break;

        float held = 0f;
        while (held < holdDuration)
        {
            held += Time.unscaledDeltaTime;
            yield return null;
        }

        // Blend CRT intro look → gameplay defaults in lockstep with the UI fade.
        vintageTv?.EndIntroOverride(fadeOutSeconds);

        if (fadeOutSeconds > 0f)
        {
            float start = canvasGroup != null ? canvasGroup.alpha : 0f;
            float t = 0f;
            while (t < fadeOutSeconds)
            {
                t += Time.unscaledDeltaTime;
                if (canvasGroup != null)
                    canvasGroup.alpha = Mathf.Lerp(start, 0f, t / fadeOutSeconds);
                yield return null;
            }
            if (canvasGroup != null)
                canvasGroup.alpha = 0f;
        }
        else if (canvasGroup != null)
        {
            canvasGroup.alpha = 0f;
        }

        if (canvas != null)
            canvas.gameObject.SetActive(false);

        _playing = false;
    }

    /// <summary>
    /// Full standalone intro (fade-in → hold → fade-out). Prefer ShowIntroImmediate +
    /// CoHoldAndFadeOut when coordinating with the goo iris open.
    /// </summary>
    public IEnumerator PlayIntro()
    {
        if (!ShowIntroImmediate())
            yield break;

        // Optional soft fade-in when not revealed under sealed goo.
        if (fadeInSeconds > 0f && canvasGroup != null)
        {
            canvasGroup.alpha = 0f;
            float t = 0f;
            while (t < fadeInSeconds)
            {
                t += Time.unscaledDeltaTime;
                canvasGroup.alpha = Mathf.Clamp01(t / fadeInSeconds);
                yield return null;
            }
            canvasGroup.alpha = 1f;
        }

        yield return CoHoldAndFadeOut();
    }

    private int ResolveRunNumber(out int dayLimit)
    {
        dayLimit = 0;
        if (countSource == RunCountSource.SessionRunNumber)
            return Mathf.Max(1, NarrativeDirector.GetTotalRunsCompleted() + 1);

        var day = DayTrialManager.Instance;
        // DayTrialManager is optional / may have no TrialConfigs yet — still advance via session runs
        // so quick-replay and skill-tree Play both show DAY 2, 3, ... instead of stuck DAY 1.
        if (day == null || day.TrialCount <= 0)
            return Mathf.Max(1, NarrativeDirector.GetTotalRunsCompleted() + 1);

        dayLimit = day.CurrentDayLimit;
        return Mathf.Max(1, day.CurrentDay);
    }

    private void ApplyTexts(int number, int dayLimit)
    {
        string title = string.Format(titleFormat, number, dayLimit);
        if (titleText != null)
            titleText.text = title;

        if (subtitleText == null) return;

        bool showSub = showSubtitleWithDayLimit
                       && countSource == RunCountSource.TrialDay
                       && dayLimit > 0
                       && !string.IsNullOrEmpty(subtitleWhenLimitKnown);
        subtitleText.gameObject.SetActive(showSub);
        if (showSub)
            subtitleText.text = string.Format(subtitleWhenLimitKnown, number, dayLimit);
    }

    private void ResolveVintageTv()
    {
        if (vintageTv == null)
            vintageTv = FindObjectOfType<VintageTVController>(true);
    }

    private void EnsureUi()
    {
        if (canvasGroup != null && titleText != null)
            return;

        if (_builtRuntimeUi) return;
        _builtRuntimeUi = true;

        GameObject root = canvas != null ? canvas.gameObject : new GameObject("RunStartIntroCanvas");
        if (canvas == null)
        {
            root.transform.SetParent(transform, false);
            canvas = root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 280;

            var scaler = root.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            root.AddComponent<GraphicRaycaster>();
        }

        if (canvasGroup == null)
            canvasGroup = root.GetComponent<CanvasGroup>() ?? root.AddComponent<CanvasGroup>();

        TMP_FontAsset font = ResolveFont();

        if (titleText == null)
        {
            var titleGo = new GameObject("Title", typeof(RectTransform));
            titleGo.transform.SetParent(root.transform, false);
            var rt = titleGo.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.1f, 0.4f);
            rt.anchorMax = new Vector2(0.9f, 0.7f);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            titleText = titleGo.AddComponent<TextMeshProUGUI>();
            if (font != null) titleText.font = font;
            titleText.fontSize = titleFontSize;
            titleText.fontStyle = FontStyles.Bold;
            titleText.color = titleColor;
            titleText.alignment = TextAlignmentOptions.Center;
            titleText.raycastTarget = false;
        }

        if (subtitleText == null)
        {
            var subGo = new GameObject("Subtitle", typeof(RectTransform));
            subGo.transform.SetParent(root.transform, false);
            var rt = subGo.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.1f, 0.32f);
            rt.anchorMax = new Vector2(0.9f, 0.42f);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            subtitleText = subGo.AddComponent<TextMeshProUGUI>();
            if (font != null) subtitleText.font = font;
            subtitleText.fontSize = subtitleFontSize;
            subtitleText.fontStyle = FontStyles.Normal;
            subtitleText.color = subtitleColor;
            subtitleText.alignment = TextAlignmentOptions.Center;
            subtitleText.raycastTarget = false;
        }

        canvas.gameObject.SetActive(false);
    }

    private static TMP_FontAsset ResolveFont()
    {
        var dialogue = FindObjectOfType<DialogueUI>(true);
        if (dialogue == null) return null;
        var text = dialogue.GetComponentInChildren<TMP_Text>(true);
        return text != null ? text.font : null;
    }

    private void OnDisable()
    {
        if (_playing)
        {
            vintageTv?.EndIntroOverride();
            _playing = false;
        }
    }
}
