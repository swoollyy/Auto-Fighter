using System;
using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Full-screen controls explainer shown once on first Play.
/// Goo creeps to ~50% over CONTROLS text (text reads through the iris hole).
/// On dismiss the overlay stays opaque behind the goo while GameManager finishes
/// the iris seal — text disappears under the closing goo, then this overlay is torn down.
/// UI is generated entirely in code (same pattern as <see cref="TutorialUIHighlightCoach"/>).
/// </summary>
public class FirstRunControlsOverlay : MonoBehaviour
{
    /// <summary>Set once the overlay has been dismissed. Session-scoped, like the other tutorial story flags.</summary>
    public const string ShownStoryFlag = "first_run_controls_shown";

    /// <summary>Ignore dismiss input for this long so the Play click can't skip the overlay instantly.</summary>
    private const float MinShowSeconds = 0.6f;
    private const float FadeInSeconds = 0.25f;
    private const float FadeOutSeconds = 0.15f;
    /// <summary>
    /// Below <see cref="GooIrisScreenTransition"/> (32000) so goo sits on top:
    /// text is visible only through the iris hole and gets covered as the hole closes.
    /// </summary>
    private const int OverlaySortOrder = 31950;

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
        // Keep CONTROLS fully opaque behind the goo until GameManager finishes the iris seal.
        // Fading here would flash the skill tree through the half-open goo hole.
        if (_group != null)
            _group.alpha = 1f;

        var iris = GooIrisScreenTransition.EnsureExists();
        iris.RestoreDefaultSortOrder();

        var cb = _onDismissed;
        _onDismissed = null;
        cb?.Invoke();
    }

    /// <summary>Torn down once the iris is fully sealed (called by GameManager).</summary>
    public static void DestroyIfPresent()
    {
        var existing = FindObjectOfType<FirstRunControlsOverlay>();
        if (existing != null)
            Destroy(existing.gameObject);
    }

    // ---------- UI construction ----------

    private void BuildUi()
    {
        var canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = OverlaySortOrder;

        var scaler = gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        gameObject.AddComponent<GraphicRaycaster>();

        _group = gameObject.AddComponent<CanvasGroup>();
        _group.alpha = 0f;
        _group.blocksRaycasts = true;
        _group.interactable = true;

        // Invisible full-screen raycast blocker (goo iris provides the visible cover).
        CreateChild("HitCatcher", transform, out RectTransform hitRt);
        StretchFull(hitRt);
        var hitImg = hitRt.gameObject.AddComponent<Image>();
        hitImg.color = new Color(0f, 0f, 0f, 0.01f);
        hitImg.raycastTarget = true;

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

        // Goo creeps from the edges to ~50% on TOP of this overlay (text reads through the hole).
        var iris = GooIrisScreenTransition.EnsureExists();
        iris.RestoreDefaultSortOrder();
        iris.BeginCloseToHoleAndHold();

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
