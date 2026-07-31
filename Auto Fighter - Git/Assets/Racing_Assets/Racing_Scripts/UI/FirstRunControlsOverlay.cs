using System;
using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Full-screen controls explainer shown once — the first time the player presses Play
/// to start their very first run (after the intro / skill-tree tutorial).
/// Uses the same dark dim as the dialogue panel and tutorial highlight. Dismissed with
/// any key / click / gamepad button, then the pending run-start callback fires.
/// UI is generated entirely in code (same pattern as <see cref="TutorialUIHighlightCoach"/>),
/// so no scene or prefab wiring is needed.
/// </summary>
public class FirstRunControlsOverlay : MonoBehaviour
{
    /// <summary>Set once the overlay has been dismissed. Session-scoped, like the other tutorial story flags.</summary>
    public const string ShownStoryFlag = "first_run_controls_shown";

    /// <summary>Ignore dismiss input for this long so the Play click can't skip the overlay instantly.</summary>
    private const float MinShowSeconds = 0.6f;
    private const float FadeInSeconds = 0.25f;
    private const float FadeOutSeconds = 0.15f;

    // Matches the dialogue panel's Background Panel tint.
    private static readonly Color DimColor = new Color(0.047f, 0.047f, 0.047f, 0.957f);
    // Matches the tutorial highlight's yellow accent.
    private static readonly Color AccentColor = new Color(1f, 0.847f, 0.302f, 1f);

    // Edit these rows to change what the overlay teaches: { action, bindings }.
    private static readonly string[,] ControlRows =
    {
        { "STEER",      "A / D  or  Left Stick" },
        { "ACCELERATE", "W  or  Right Trigger" },
        { "BRAKE",      "S  or  Left Trigger" },
        { "BOOST",      "Space  or  A / Cross" },
        { "DRIFT",      "Left Shift  or  B / Circle" },
        { "MAP PEEK",   "Hold Tab" },
    };

    private Action _onDismissed;
    private CanvasGroup _group;
    private TextMeshProUGUI _prompt;
    private float _shownFor;
    private bool _dismissed;

    /// <summary>
    /// Shows the overlay if it has never been shown this session. Returns true when the overlay
    /// was displayed (caller should NOT start the run — <paramref name="onDismissed"/> fires when
    /// the player dismisses it). Returns false when the overlay was already shown before, so the
    /// caller should start the run immediately.
    /// </summary>
    public static bool TryShow(Action onDismissed)
    {
        if (NarrativeDirector.HasStoryFlag(ShownStoryFlag))
            return false;
        if (FindObjectOfType<FirstRunControlsOverlay>() != null)
            return false;

        var go = new GameObject("FirstRunControlsOverlay");
        var overlay = go.AddComponent<FirstRunControlsOverlay>();
        overlay._onDismissed = onDismissed;
        overlay.BuildUi();
        return true;
    }

    private void Update()
    {
        if (_dismissed) return;

        _shownFor += Time.unscaledDeltaTime;
        PulsePrompt();

        if (_shownFor < MinShowSeconds) return;
        if (Input.anyKeyDown)
            Dismiss();
    }

    private void OnDestroy()
    {
        // Safety: never leave background UI locked if something tears the overlay down early.
        if (!_dismissed)
            GameplayUIInputGuard.IsTutorialHighlightActive = false;
    }

    private void Dismiss()
    {
        if (_dismissed) return;
        _dismissed = true;

        NarrativeDirector.SetStoryFlag(ShownStoryFlag);
        GameplayUIInputGuard.IsTutorialHighlightActive = false;

        // Start the run immediately so the loading screen comes up behind the fade-out.
        var cb = _onDismissed;
        _onDismissed = null;
        cb?.Invoke();

        StopAllCoroutines();
        StartCoroutine(CoFadeOutAndDestroy());
    }

    // ---------- UI construction ----------

    private void BuildUi()
    {
        var canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 300; // above game + dialogue canvases

        var scaler = gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        gameObject.AddComponent<GraphicRaycaster>();

        _group = gameObject.AddComponent<CanvasGroup>();
        _group.alpha = 0f;
        _group.blocksRaycasts = true;
        _group.interactable = true;

        // Full-screen dim; raycast target so clicks never reach the UI underneath.
        CreateChild("Dim", transform, out RectTransform dimRt);
        StretchFull(dimRt);
        var dimImg = dimRt.gameObject.AddComponent<Image>();
        dimImg.color = DimColor;
        dimImg.raycastTarget = true;

        // Centered content column.
        CreateChild("Content", transform, out RectTransform contentRt);
        StretchFull(contentRt);
        var vlg = contentRt.gameObject.AddComponent<VerticalLayoutGroup>();
        vlg.childAlignment = TextAnchor.MiddleCenter;
        vlg.spacing = 16f;
        vlg.childControlWidth = true;
        vlg.childControlHeight = true;
        vlg.childForceExpandWidth = true;
        vlg.childForceExpandHeight = false;

        TMP_FontAsset font = ResolveDialogueFont();

        CreateText(contentRt, "Title", "CONTROLS", 62f, FontStyles.Bold, AccentColor, font);
        CreateSpacer(contentRt, 20f);

        for (int i = 0; i < ControlRows.GetLength(0); i++)
            CreateRow(contentRt, ControlRows[i, 0], ControlRows[i, 1], font);

        CreateSpacer(contentRt, 34f);
        _prompt = CreateText(contentRt, "Prompt",
            "Press any key or button to start your run", 30f, FontStyles.Italic, Color.white, font);

        // Freeze the skill-tree chrome behind us (pan/zoom, buttons) while the overlay is up.
        GameplayUIInputGuard.IsTutorialHighlightActive = true;

        StartCoroutine(CoFadeIn());
    }

    private void CreateRow(Transform parent, string action, string keys, TMP_FontAsset font)
    {
        CreateChild("Row_" + action, parent, out RectTransform rowRt);
        var hlg = rowRt.gameObject.AddComponent<HorizontalLayoutGroup>();
        hlg.childAlignment = TextAnchor.MiddleCenter;
        hlg.spacing = 46f;
        hlg.childControlWidth = true;
        hlg.childControlHeight = true;
        hlg.childForceExpandWidth = false;
        hlg.childForceExpandHeight = false;

        var actionText = CreateText(rowRt, "Action", action, 34f, FontStyles.Bold, AccentColor, font);
        actionText.alignment = TextAlignmentOptions.MidlineRight;
        var actionLe = actionText.gameObject.AddComponent<LayoutElement>();
        actionLe.minWidth = actionLe.preferredWidth = 320f;

        var keysText = CreateText(rowRt, "Keys", keys, 34f, FontStyles.Normal, Color.white, font);
        keysText.alignment = TextAlignmentOptions.MidlineLeft;
        var keysLe = keysText.gameObject.AddComponent<LayoutElement>();
        keysLe.minWidth = keysLe.preferredWidth = 520f;
    }

    private static TextMeshProUGUI CreateText(
        Transform parent, string name, string text, float size,
        FontStyles style, Color color, TMP_FontAsset font)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var tmp = go.AddComponent<TextMeshProUGUI>();
        if (font != null) tmp.font = font;
        tmp.text = text;
        tmp.fontSize = size;
        tmp.fontStyle = style;
        tmp.color = color;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.raycastTarget = false;
        return tmp;
    }

    private static void CreateSpacer(Transform parent, float height)
    {
        var go = new GameObject("Spacer", typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var le = go.AddComponent<LayoutElement>();
        le.minHeight = le.preferredHeight = height;
    }

    private static void CreateChild(string name, Transform parent, out RectTransform rt)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        rt = go.GetComponent<RectTransform>();
    }

    private static void StretchFull(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
        rt.localScale = Vector3.one;
        rt.localRotation = Quaternion.identity;
    }

    /// <summary>Borrow the dialogue panel's font so the overlay matches the narrative UI. Falls back to the TMP default.</summary>
    private static TMP_FontAsset ResolveDialogueFont()
    {
        var dialogue = FindObjectOfType<DialogueUI>(true);
        if (dialogue == null) return null;
        var text = dialogue.GetComponentInChildren<TMP_Text>(true);
        return text != null ? text.font : null;
    }

    // ---------- Animation ----------

    private void PulsePrompt()
    {
        if (_prompt == null) return;
        float pulse = (Mathf.Sin(Time.unscaledTime * Mathf.PI * 2f * 0.7f) + 1f) * 0.5f;
        var c = _prompt.color;
        c.a = Mathf.Lerp(0.35f, 1f, pulse);
        _prompt.color = c;
    }

    private IEnumerator CoFadeIn()
    {
        float t = 0f;
        while (t < FadeInSeconds)
        {
            t += Time.unscaledDeltaTime;
            if (_group != null)
                _group.alpha = Mathf.Clamp01(t / FadeInSeconds);
            yield return null;
        }
        if (_group != null)
            _group.alpha = 1f;
    }

    private IEnumerator CoFadeOutAndDestroy()
    {
        float start = _group != null ? _group.alpha : 1f;
        float t = 0f;
        while (t < FadeOutSeconds)
        {
            t += Time.unscaledDeltaTime;
            if (_group != null)
                _group.alpha = Mathf.Lerp(start, 0f, t / FadeOutSeconds);
            yield return null;
        }
        Destroy(gameObject);
    }
}
