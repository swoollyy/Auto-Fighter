using System.Collections.Generic;
using AutoFighter.Core;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Runs before <see cref="EventSystem"/> so gamepad Submit cannot fire on the still-selected skill node
/// in the same frame as our synthetic click / dismiss logic.
/// </summary>
[DefaultExecutionOrder(-300)]
[DisallowMultipleComponent]
public class SkillTreeVirtualCursor : MonoBehaviour
{
    private enum ControlMode { Mouse, Virtual }

    [Header("Required")]
    [SerializeField] private RectTransform cursorRect;           // your cursor Image rect
    [SerializeField] private Canvas canvas;                      // the skill tree canvas
    [SerializeField] private RectTransform clampArea;            // usually RacingSkillUI.treeViewport
    [SerializeField] private RacingSkillDetailPanel detailPanel; // so we can close it on empty click
    [SerializeField] private RacingSkillUI skillTreeUi;            // optional; same dismiss rules as mouse

    [Header("Input (Legacy Input Manager)")]
    [SerializeField] private string axisX = "Horizontal";
    [SerializeField] private string axisY = "Vertical";
    [SerializeField] private KeyCode clickKey = KeyCode.JoystickButton1; // PS5 Cross (X)
    [SerializeField] private float deadZone = 0.18f;

    [Header("Motion")]
    [SerializeField] private float cursorSpeedPixelsPerSecond = 1400f;
    [SerializeField] private bool useUnscaledTime = true;

    [Header("Behavior")]
    [SerializeField] private bool closeDetailOnEmptyClick = true;

    [Header("Click Cooldown")]
    [SerializeField, Min(0f)] private float clickCooldown = 0.20f;
    private float _nextAllowedClickTime = 0f;

    [Header("Auto Switch (Mouse <-> Controller)")]
    [SerializeField] private bool autoSwitchInput = true;
    [SerializeField] private float mouseMovePixelsThreshold = 2.5f;   // how much mouse must move to count
    [SerializeField] private float stickActivateThreshold = 0.22f;    // extra gate above deadZone

    private GraphicRaycaster _raycaster;
    private EventSystem _eventSystem;
    private Camera _uiCam;

    private Vector2 _screenPos;
    private GameObject _currentHover;

    private ControlMode _mode = ControlMode.Mouse;

    private Vector2 _lastMousePos;
    private float _lastMouseActivityTime;
    private float _lastControllerActivityTime;

    void Awake()
    {
        if (CoreVirtualCursor.Instance != null)
        {
            if (cursorRect != null)
                cursorRect.gameObject.SetActive(false);
            enabled = false;
            return;
        }

        if (!canvas) canvas = GetComponentInParent<Canvas>();
        if (!cursorRect) Debug.LogError("[SkillTreeVirtualCursor] cursorRect not set.");
        if (!canvas) Debug.LogError("[SkillTreeVirtualCursor] canvas not set/found.");

        _eventSystem = EventSystem.current;
        if (_eventSystem == null) Debug.LogError("[SkillTreeVirtualCursor] No EventSystem in scene.");

        _raycaster = canvas ? canvas.GetComponent<GraphicRaycaster>() : null;
        if (_raycaster == null && canvas != null)
            _raycaster = canvas.gameObject.AddComponent<GraphicRaycaster>();

        _uiCam = (canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay) ? canvas.worldCamera : null;

        if (skillTreeUi == null)
            skillTreeUi = FindObjectOfType<RacingSkillUI>();

        SyncCursorSpeedFromSave();
    }

    void OnEnable()
    {
        // Start in mouse mode (so desktop feels natural), but we can switch instantly on controller input.
        _lastMousePos = Input.mousePosition;
        _screenPos = ClampToArea(Input.mousePosition);
        ApplyScreenPosToCursor(_screenPos);
        SetMode(ControlMode.Mouse, snapVirtualToMouse: false);
    }

    void OnDisable()
    {
        Cursor.visible = true;
        Cursor.lockState = CursorLockMode.None;

        if (cursorRect) cursorRect.gameObject.SetActive(false);
        SetHover(null, null);
    }

    void Update()
    {
        if (!_eventSystem || !_raycaster || !cursorRect || !canvas) return;
        SyncCursorSpeedFromSave();

        if (autoSwitchInput)
            DetectAndSwitchMode();

        if (_mode == ControlMode.Mouse)
        {
            // Let Unity's normal mouse pointer + EventSystem do its thing.
            // We keep our virtual cursor hidden in this mode.
            return;
        }

        // Virtual mode: drive pointer via stick.
        float dt = useUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;

        Vector2 stick = new Vector2(Input.GetAxisRaw(axisX), Input.GetAxisRaw(axisY));

        // Deadzone shaping (same idea you already had)
        float mag = stick.magnitude;
        if (mag < deadZone) stick = Vector2.zero;
        else
        {
            float t = Mathf.InverseLerp(deadZone, 1f, mag);
            stick = stick.normalized * t;
        }

        if (stick != Vector2.zero)
        {
            _screenPos += stick * (cursorSpeedPixelsPerSecond * dt);
            _screenPos = ClampToArea(_screenPos);
            ApplyScreenPosToCursor(_screenPos);
            RefreshHover();
        }

        if (Input.GetKeyDown(clickKey))
        {
            if (GameplayUIInputGuard.IsDialogueBlockingGameplayUi)
                return;

            float now = useUnscaledTime ? Time.unscaledTime : Time.time;
            if (now >= _nextAllowedClickTime)
            {
                _nextAllowedClickTime = now + clickCooldown;
                ClickUnderCursor();
            }
        }
    }

    // ----------------------------
    // Input mode switching
    // ----------------------------
    private void DetectAndSwitchMode()
    {
        // Mouse activity?
        Vector2 mousePos = Input.mousePosition;
        bool mouseMoved = (mousePos - _lastMousePos).sqrMagnitude >= (mouseMovePixelsThreshold * mouseMovePixelsThreshold);
        _lastMousePos = mousePos;

        bool mouseClicked =
            Input.GetMouseButtonDown(0) ||
            Input.GetMouseButtonDown(1) ||
            Input.GetMouseButtonDown(2);

        bool mouseScrolled = Mathf.Abs(Input.mouseScrollDelta.y) > 0.01f;

        if (mouseMoved || mouseClicked || mouseScrolled)
            _lastMouseActivityTime = Time.unscaledTime;

        // Controller activity?
        Vector2 stick = new Vector2(Input.GetAxisRaw(axisX), Input.GetAxisRaw(axisY));
        bool stickActive = stick.magnitude >= Mathf.Max(deadZone, stickActivateThreshold);

        // If you want more buttons later (Circle / Square), add them here.
        bool controllerPressed = Input.GetKeyDown(clickKey);

        if (stickActive || controllerPressed)
            _lastControllerActivityTime = Time.unscaledTime;

        // Decide winner (most recent activity)
        if (_lastControllerActivityTime > _lastMouseActivityTime)
        {
            if (_mode != ControlMode.Virtual)
                SetMode(ControlMode.Virtual, snapVirtualToMouse: true); // <- your requirement
        }
        else
        {
            if (_mode != ControlMode.Mouse)
                SetMode(ControlMode.Mouse, snapVirtualToMouse: false);
        }
    }

    private void SetMode(ControlMode mode, bool snapVirtualToMouse)
    {
        _mode = mode;

        if (_mode == ControlMode.Mouse)
        {
            // Show real cursor, hide virtual cursor image.
            Cursor.visible = true;
            Cursor.lockState = CursorLockMode.None;
            if (cursorRect) cursorRect.gameObject.SetActive(false);
            SetHover(null, null);
        }
        else
        {
            // Hide real cursor, show virtual cursor image.
            Cursor.visible = false;
            Cursor.lockState = CursorLockMode.None;

            if (cursorRect) cursorRect.gameObject.SetActive(true);

            if (snapVirtualToMouse)
            {
                _screenPos = ClampToArea(Input.mousePosition); // snap to mouse position on switch
                ApplyScreenPosToCursor(_screenPos);
            }
            else
            {
                _screenPos = ClampToArea(_screenPos);
                ApplyScreenPosToCursor(_screenPos);
            }

            RefreshHover();
        }
    }

    // ----------------------------
    // Virtual cursor raycast + click
    // ----------------------------
    private void ClickUnderCursor()
    {
        var pointer = new PointerEventData(_eventSystem)
        {
            position = _screenPos,
            button = PointerEventData.InputButton.Left,
            pressPosition = _screenPos
        };

        var results = new List<RaycastResult>();
        _eventSystem.RaycastAll(pointer, results);

        // Full stack: top-most hit alone often lies on tree/mask while Buy is deeper — same rule as mouse dismiss.
        if (closeDetailOnEmptyClick && skillTreeUi != null && skillTreeUi.ShouldDismissSkillDetailForRaycastResults(results))
        {
            skillTreeUi.DismissSkillDetailFromPointerOutside();
            _eventSystem.SetSelectedGameObject(null);
            return;
        }

        // Prefer clickables on the detail card (Buy, etc.) when the stack mixes tree + card hits.
        GameObject target = FindFirstClickableFromResults(results, detailPanel);
        if (!target)
        {
            // Raycast hit something non-interactive; do not leave the previous node selected or Submit will re-fire it.
            _eventSystem.SetSelectedGameObject(null);
            return;
        }

        // Must match the clickable we execute on — using results[0] can desync TMP/child hits vs Button parent.
        pointer.pointerCurrentRaycast = default;
        for (int i = 0; i < results.Count; i++)
        {
            var go = results[i].gameObject;
            if (go != null && (go.transform == target.transform || go.transform.IsChildOf(target.transform)))
            {
                pointer.pointerCurrentRaycast = results[i];
                break;
            }
        }

        if (!pointer.pointerCurrentRaycast.isValid && results.Count > 0)
            pointer.pointerCurrentRaycast = results[0];

        // Mirror the normal "selection" behavior
        _eventSystem.SetSelectedGameObject(target);

        ExecuteEvents.ExecuteHierarchy(target, pointer, ExecuteEvents.pointerDownHandler);
        ExecuteEvents.ExecuteHierarchy(target, pointer, ExecuteEvents.pointerUpHandler);
        ExecuteEvents.ExecuteHierarchy(target, pointer, ExecuteEvents.pointerClickHandler);
    }



    private void RefreshHover()
    {
        var pointer = new PointerEventData(_eventSystem) { position = _screenPos };

        var results = new List<RaycastResult>();
        _eventSystem.RaycastAll(pointer, results); // <-- IMPORTANT

        GameObject newHover = (results.Count > 0) ? results[0].gameObject : null;
        SetHover(newHover, pointer);
    }

    private void SetHover(GameObject newHover, PointerEventData pointer)
    {
        if (_currentHover == newHover) return;

        if (_currentHover != null && pointer != null)
            ExecuteEvents.Execute(_currentHover, pointer, ExecuteEvents.pointerExitHandler);

        _currentHover = newHover;

        if (_currentHover != null && pointer != null)
            ExecuteEvents.Execute(_currentHover, pointer, ExecuteEvents.pointerEnterHandler);
    }

    // ----------------------------
    // Clamping + positioning
    // ----------------------------
    private Vector2 ClampToArea(Vector2 screenPos)
    {
        if (!clampArea)
        {
            screenPos.x = Mathf.Clamp(screenPos.x, 0f, Screen.width);
            screenPos.y = Mathf.Clamp(screenPos.y, 0f, Screen.height);
            return screenPos;
        }

        Vector3[] corners = new Vector3[4];
        clampArea.GetWorldCorners(corners);

        Vector2 a = RectTransformUtility.WorldToScreenPoint(_uiCam, corners[0]); // bottom-left
        Vector2 b = RectTransformUtility.WorldToScreenPoint(_uiCam, corners[2]); // top-right

        float minX = Mathf.Min(a.x, b.x);
        float maxX = Mathf.Max(a.x, b.x);
        float minY = Mathf.Min(a.y, b.y);
        float maxY = Mathf.Max(a.y, b.y);

        screenPos.x = Mathf.Clamp(screenPos.x, minX, maxX);
        screenPos.y = Mathf.Clamp(screenPos.y, minY, maxY);
        return screenPos;
    }

    private static GameObject FindClickable(GameObject go)
    {
        if (!go) return null;

        // Walk up to find something that actually handles UI clicks
        Transform t = go.transform;
        while (t != null)
        {
            var candidate = t.gameObject;

            // Most of your UI is normal Buttons/Selectables
            if (candidate.GetComponent<UnityEngine.UI.Selectable>() != null)
                return candidate;

            // Generic support for custom click handlers
            if (candidate.GetComponent<IPointerClickHandler>() != null)
                return candidate;

            t = t.parent;
        }

        return null;
    }

    private static GameObject FindFirstClickableFromResults(List<RaycastResult> results, RacingSkillDetailPanel detailPanel)
    {
        if (detailPanel != null && detailPanel.IsInfoVisible)
        {
            for (int i = 0; i < results.Count; i++)
            {
                var go = results[i].gameObject;
                if (go == null) continue;
                if (!detailPanel.IsHitInsideDetailUi(go)) continue;
                var clickable = FindClickable(go);
                if (clickable != null)
                    return clickable;
            }
        }

        for (int i = 0; i < results.Count; i++)
        {
            var go = results[i].gameObject;
            var clickable = FindClickable(go);
            if (clickable != null)
                return clickable;
        }

        return null;
    }


    private void ApplyScreenPosToCursor(Vector2 screenPos)
    {
        RectTransform parent = cursorRect.parent as RectTransform;
        if (!parent) return;

        if (RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, screenPos, _uiCam, out Vector2 local))
            cursorRect.anchoredPosition = local;
    }

    private void SyncCursorSpeedFromSave()
    {
        if (SaveSystem.Current == null) return;

        float saved = Mathf.Clamp(
            SaveSystem.Current.cursorSpeedPixelsPerSecond,
            SaveData.MinCursorSpeedPixelsPerSecond,
            SaveData.MaxCursorSpeedPixelsPerSecond);

        if (!Mathf.Approximately(cursorSpeedPixelsPerSecond, saved))
            cursorSpeedPixelsPerSecond = saved;
    }
}
