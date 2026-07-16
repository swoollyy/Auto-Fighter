using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace AutoFighter.Core
{
    /// <summary>
    /// Global, scene-safe virtual cursor for controller UI interaction.
    /// It auto-spawns once at runtime and persists across scene loads.
    /// </summary>
    [DefaultExecutionOrder(-400)]
    [DisallowMultipleComponent]
    public class CoreVirtualCursor : MonoBehaviour
    {
        private enum ControlMode
        {
            Mouse,
            Controller
        }

        private static CoreVirtualCursor _instance;

        public static CoreVirtualCursor Instance => _instance;

        public static bool IsManagingVisibility =>
            _instance != null &&
            _instance.enableVirtualCursor &&
            !ShouldRacingInputReaderManageCursorVisibility();

        /// <summary>
        /// When false, <see cref="RacingInputReader"/> must not toggle Cursor.visible.
        /// </summary>
        public static bool ShouldRacingInputReaderManageCursorVisibility()
        {
            if (_instance == null || !_instance.enableVirtualCursor)
                return true;

            if (GameplayUIInputGuard.IsDialogueBlockingGameplayUi)
                return false;

            var gm = global::GameManager_Racing.Instance;
            if (gm == null)
                return false;

            if (gm.ProgressState == global::GameManager_Racing.GameProgressState.Dialogue)
                return false;

            if (gm.ProgressState == global::GameManager_Racing.GameProgressState.LoadingRun)
                return false;

            return gm.ProgressState == global::GameManager_Racing.GameProgressState.InRun;
        }

        [Header("Enable")]
        [SerializeField] private bool enableVirtualCursor = true;
        [SerializeField] private bool autoSwitchInputMode = true;

        [Header("Movement")]
        [SerializeField] private float cursorSpeedPixelsPerSecond = 1300f;
        [SerializeField] private float deadZone = 0.2f;
        [SerializeField] private bool useUnscaledTime = true;

        [Header("Mode Switching")]
        [SerializeField] private float mouseMovePixelsThreshold = 2.5f;
        [SerializeField] private float stickActivateThreshold = 0.25f;

        [Header("Legacy Input fallback")]
        [SerializeField] private string legacyAxisX = "Horizontal";
        [SerializeField] private string legacyAxisY = "Vertical";
        [SerializeField] private KeyCode legacySubmitButton = KeyCode.JoystickButton0;
        [SerializeField] private KeyCode legacyBackspaceButton = KeyCode.JoystickButton2;
        [SerializeField] private KeyCode legacyOneShotShiftButton = KeyCode.JoystickButton6;
        [SerializeField] private KeyCode legacyCapsLockButton = KeyCode.JoystickButton4;

        [Header("Cursor Visual")]
        [SerializeField] private Canvas cursorCanvas;
        [SerializeField] private RectTransform cursorRect;
        [SerializeField] private Color fallbackCursorColor = new Color(1f, 1f, 1f, 0.95f);
        [SerializeField] private Vector2 fallbackCursorSize = new Vector2(22f, 22f);

        private CoreVirtualCursorSettings _settings;

        [Header("Controller Click")]
        [SerializeField] private bool lockEventSystemSelectionToClickedObject = true;
        [SerializeField] private bool clearEventSystemSelectionInControllerMode = true;
        [SerializeField, Min(0f)] private float controllerClickCooldownSeconds = 0.2f;
        [SerializeField, Min(0f)] private float keyboardClickCooldownSeconds = 0.035f;

        [Header("Name Entry Shortcuts")]
        [SerializeField] private bool enableNameEntryControllerShortcuts = true;

        private EventSystem _eventSystem;
#if ENABLE_INPUT_SYSTEM
        private UnityEngine.InputSystem.UI.InputSystemUIInputModule _uiInputModule;
#endif
        private Camera _uiCam;
        private Vector2 _screenPos;
        private Vector2 _lastMousePos;
        private float _lastMouseActivityTime;
        private float _lastMouseKeyboardActivityTime;
        private float _lastControllerActivityTime;
        private float _nextAllowedControllerClickTime;
        private ControlMode _mode = ControlMode.Mouse;
        private readonly List<RaycastResult> _raycastResults = new List<RaycastResult>(32);
        private PointerEventData _activePointerData;
        private GameObject _pressedTarget;
        private GameObject _dragTarget;
        private bool _isPointerPressed;
        private bool _isDragging;
        private Vector2 _pressScreenPos;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void EnsureExists()
        {
            if (_instance != null) return;

            var go = new GameObject("[Core] Virtual Cursor");
            _instance = go.AddComponent<CoreVirtualCursor>();
            DontDestroyOnLoad(go);
        }

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }

            _instance = this;
            DontDestroyOnLoad(gameObject);

            _settings = Resources.Load<CoreVirtualCursorSettings>(CoreVirtualCursorSettings.DefaultResourcesPath);
            if (_settings == null)
            {
                Debug.LogWarning(
                    "[CoreVirtualCursor] Missing settings asset. In the Unity editor, use menu " +
                    "AutoFighter > Virtual Cursor Settings to create/open it, then assign your cursor prefab.");
            }

            SceneManager.sceneLoaded += OnSceneLoaded;
            CacheEventSystemAndCamera();
            EnsureCursorVisual();
            SyncCursorSpeedFromSave();
            float now = Time.unscaledTime;
            _lastMouseActivityTime = now;
            _lastMouseKeyboardActivityTime = now;
            SetMode(ControlMode.Mouse, false);
        }

        private void OnDestroy()
        {
            if (_instance == this) _instance = null;
            SceneManager.sceneLoaded -= OnSceneLoaded;
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            CacheEventSystemAndCamera();
            EnsureCursorVisual();
            SyncCursorSpeedFromSave();

            _screenPos = ClampToScreen(Input.mousePosition);
            ApplyScreenPosToCursor(_screenPos);
            SyncUIInputModuleForMode();
        }

        /// <summary>
        /// Virtual cursor synthesizes pointer submit in controller mode; leave UI Input Module on for mouse/keyboard only.
        /// </summary>
        private void SyncUIInputModuleForMode()
        {
#if ENABLE_INPUT_SYSTEM
            if (_uiInputModule == null && _eventSystem != null)
                _uiInputModule = _eventSystem.GetComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();

            if (_uiInputModule == null)
                return;

            bool virtualCursorHandlesSubmit =
                enableVirtualCursor &&
                IsCursorAllowedForCurrentGameState() &&
                _mode == ControlMode.Controller;

            _uiInputModule.enabled = !virtualCursorHandlesSubmit;
#endif
        }

        private void OnEnable()
        {
            _lastMousePos = Input.mousePosition;
            _screenPos = ClampToScreen(_lastMousePos);
            ApplyCursorVisibility(IsCursorAllowedForCurrentGameState());
        }

        private void Update()
        {
            if (!enableVirtualCursor) return;
            if (_eventSystem == null) CacheEventSystemAndCamera();
            if (_eventSystem == null) return;
            SyncCursorSpeedFromSave();

            if (!IsCursorAllowedForCurrentGameState())
            {
                ReleaseActivePointerIfAny();
                if (autoSwitchInputMode)
                    DetectAndSwitchMode();
                ApplyCursorVisibility(mechanicActive: false);
                SyncUIInputModuleForMode();
                return;
            }

            if (autoSwitchInputMode)
                DetectAndSwitchMode();

            if (_mode == ControlMode.Mouse)
            {
                ReleaseActivePointerIfAny();
                _screenPos = ClampToScreen(Input.mousePosition);
                ApplyScreenPosToCursor(_screenPos);
                ApplyCursorVisibility(mechanicActive: true);
                return;
            }

            ApplyCursorVisibility(mechanicActive: true);

            float dt = useUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;
            Vector2 stick = ReadControllerMoveVector();
            stick = ApplyDeadZone(stick);

            if (clearEventSystemSelectionInControllerMode && _eventSystem.currentSelectedGameObject != null)
                _eventSystem.SetSelectedGameObject(null);

            if (stick != Vector2.zero)
            {
                _screenPos += stick * (cursorSpeedPixelsPerSecond * dt);
                _screenPos = ClampToScreen(_screenPos);
                ApplyScreenPosToCursor(_screenPos);
            }

            HandleNameEntryControllerShortcuts();
            UpdateControllerPointerState(stick != Vector2.zero);
            SyncUIInputModuleForMode();
        }

        private void DetectAndSwitchMode()
        {
            Vector2 mousePos = Input.mousePosition;
            bool mouseMoved = (mousePos - _lastMousePos).sqrMagnitude >= (mouseMovePixelsThreshold * mouseMovePixelsThreshold);
            _lastMousePos = mousePos;

            bool mouseClicked = Input.GetMouseButtonDown(0) || Input.GetMouseButtonDown(1) || Input.GetMouseButtonDown(2);
            bool mouseScrolled = Mathf.Abs(Input.mouseScrollDelta.y) > 0.01f;
            bool mouseHeld = Input.GetMouseButton(0) || Input.GetMouseButton(1) || Input.GetMouseButton(2);
            if (mouseMoved || mouseClicked || mouseScrolled || mouseHeld)
                _lastMouseActivityTime = Time.unscaledTime;

            if (ReadKeyboardActivity())
                _lastMouseKeyboardActivityTime = Time.unscaledTime;

            Vector2 stick = ReadControllerMoveVector();
            bool stickActive = stick.magnitude >= Mathf.Max(deadZone, stickActivateThreshold);
            bool controllerClicked = ReadControllerSubmitDown();
            if (stickActive || controllerClicked)
                _lastControllerActivityTime = Time.unscaledTime;

            float lastMouseKeyboardActivity = Mathf.Max(_lastMouseActivityTime, _lastMouseKeyboardActivityTime);
            if (_lastControllerActivityTime > lastMouseKeyboardActivity)
            {
                if (_mode != ControlMode.Controller)
                    SetMode(ControlMode.Controller, true);
            }
            else
            {
                if (_mode != ControlMode.Mouse)
                    SetMode(ControlMode.Mouse, false);
            }
        }

        private void SetMode(ControlMode nextMode, bool snapVirtualToMouse)
        {
            _mode = nextMode;

            if (snapVirtualToMouse || _mode == ControlMode.Mouse)
                _screenPos = ClampToScreen(Input.mousePosition);
            else
                _screenPos = ClampToScreen(_screenPos);

            ApplyScreenPosToCursor(_screenPos);
            ApplyCursorVisibility(IsCursorAllowedForCurrentGameState());
        }

        /// <summary>
        /// Single place that decides OS vs virtual cursor visibility.
        /// Mouse/keyboard: OS cursor on. Controller: virtual cursor on (when mechanic is active).
        /// </summary>
        private void ApplyCursorVisibility(bool mechanicActive)
        {
            Cursor.lockState = CursorLockMode.None;

            if (!mechanicActive)
            {
                HideCustomCursor();
                // Dialogue: stable OS cursor for mouse/keyboard; no cursor during loading/in-run.
                Cursor.visible = IsDialogueOrCutsceneBlocked() && _mode == ControlMode.Mouse;
                return;
            }

            if (_mode == ControlMode.Mouse)
            {
                HideCustomCursor();
                Cursor.visible = true;
                return;
            }

            Cursor.visible = false;
            ShowCustomCursor();
        }

        private static bool IsDialogueOrCutsceneBlocked()
        {
            if (GameplayUIInputGuard.IsDialogueBlockingGameplayUi)
                return true;

            var gm = global::GameManager_Racing.Instance;
            return gm != null && gm.ProgressState == global::GameManager_Racing.GameProgressState.Dialogue;
        }

        private void ClickUnderCursor()
        {
            var pointer = new PointerEventData(_eventSystem)
            {
                position = _screenPos,
                pressPosition = _screenPos,
                button = PointerEventData.InputButton.Left
            };

            _raycastResults.Clear();
            _eventSystem.RaycastAll(pointer, _raycastResults);
            if (_raycastResults.Count == 0) return;

            GameObject target = FindFirstClickable(_raycastResults);
            if (target == null) return;

            if (lockEventSystemSelectionToClickedObject)
                _eventSystem.SetSelectedGameObject(target);

            ExecuteEvents.ExecuteHierarchy(target, pointer, ExecuteEvents.pointerDownHandler);
            ExecuteEvents.ExecuteHierarchy(target, pointer, ExecuteEvents.pointerUpHandler);
            ExecuteEvents.ExecuteHierarchy(target, pointer, ExecuteEvents.pointerClickHandler);
            _nextAllowedControllerClickTime = CurrentTime() + GetCooldownForTarget(target);
        }

        private void UpdateControllerPointerState(bool cursorMovedThisFrame)
        {
            bool submitDown = ReadControllerSubmitDown();
            bool submitHeld = ReadControllerSubmitHeld();
            bool submitUp = ReadControllerSubmitUp();

            if (submitDown && !_isPointerPressed && CanClickNow())
                BeginPointerPress();

            if (_isPointerPressed && submitHeld)
                ContinuePointerPress(cursorMovedThisFrame);

            if (_isPointerPressed && submitUp)
                EndPointerPress();
        }

        private void BeginPointerPress()
        {
            _activePointerData = new PointerEventData(_eventSystem)
            {
                position = _screenPos,
                pressPosition = _screenPos,
                button = PointerEventData.InputButton.Left
            };

            _raycastResults.Clear();
            _eventSystem.RaycastAll(_activePointerData, _raycastResults);
            if (_raycastResults.Count == 0)
            {
                _activePointerData = null;
                return;
            }

            _pressedTarget = FindFirstClickable(_raycastResults);
            if (_pressedTarget == null)
            {
                _activePointerData = null;
                return;
            }

            _isPointerPressed = true;
            _isDragging = false;
            _dragTarget = null;
            _pressScreenPos = _screenPos;

            if (lockEventSystemSelectionToClickedObject)
                _eventSystem.SetSelectedGameObject(_pressedTarget);

            _activePointerData.pointerPress = _pressedTarget;
            _activePointerData.pointerDrag = _pressedTarget;
            _activePointerData.rawPointerPress = _pressedTarget;

            ExecuteEvents.ExecuteHierarchy(_pressedTarget, _activePointerData, ExecuteEvents.pointerDownHandler);
            ExecuteEvents.ExecuteHierarchy(_pressedTarget, _activePointerData, ExecuteEvents.initializePotentialDrag);
        }

        private void ContinuePointerPress(bool cursorMovedThisFrame)
        {
            if (_activePointerData == null) return;

            _activePointerData.position = _screenPos;

            if (!_isDragging && cursorMovedThisFrame && (_screenPos - _pressScreenPos).sqrMagnitude > 0.01f)
            {
                _dragTarget = ExecuteEvents.GetEventHandler<IDragHandler>(_pressedTarget);
                if (_dragTarget != null)
                {
                    _isDragging = true;
                    _activePointerData.pointerDrag = _dragTarget;
                    ExecuteEvents.Execute(_dragTarget, _activePointerData, ExecuteEvents.beginDragHandler);
                }
            }

            if (_isDragging && _dragTarget != null)
                ExecuteEvents.Execute(_dragTarget, _activePointerData, ExecuteEvents.dragHandler);
        }

        private void EndPointerPress()
        {
            if (_activePointerData == null || _pressedTarget == null)
            {
                ResetPointerPressState();
                return;
            }

            _activePointerData.position = _screenPos;

            _raycastResults.Clear();
            _eventSystem.RaycastAll(_activePointerData, _raycastResults);
            GameObject releaseTarget = FindFirstClickable(_raycastResults);
            if (releaseTarget == null) releaseTarget = _pressedTarget;

            if (_isDragging && _dragTarget != null)
            {
                ExecuteEvents.Execute(_dragTarget, _activePointerData, ExecuteEvents.endDragHandler);
                ExecuteEvents.ExecuteHierarchy(releaseTarget, _activePointerData, ExecuteEvents.dropHandler);
            }

            ExecuteEvents.ExecuteHierarchy(_pressedTarget, _activePointerData, ExecuteEvents.pointerUpHandler);
            if (!_isDragging && releaseTarget == _pressedTarget)
                ExecuteEvents.ExecuteHierarchy(_pressedTarget, _activePointerData, ExecuteEvents.pointerClickHandler);

            _nextAllowedControllerClickTime = CurrentTime() + GetCooldownForTarget(_pressedTarget);
            ResetPointerPressState();
        }

        private void ResetPointerPressState()
        {
            _activePointerData = null;
            _pressedTarget = null;
            _dragTarget = null;
            _isPointerPressed = false;
            _isDragging = false;
            _pressScreenPos = Vector2.zero;
        }

        private void ReleaseActivePointerIfAny()
        {
            if (!_isPointerPressed) return;
            EndPointerPress();
        }

        private bool CanClickNow()
        {
            return CurrentTime() >= _nextAllowedControllerClickTime;
        }

        private float CurrentTime()
        {
            return useUnscaledTime ? Time.unscaledTime : Time.time;
        }

        private float GetCooldownForTarget(GameObject target)
        {
            if (target == null) return controllerClickCooldownSeconds;

            // Allow faster repeated presses for on-screen keyboard keys.
            if (target.GetComponentInParent<NameEntryPanelController>() != null &&
                target.GetComponentInParent<VirtualKeyboardKey>() != null)
            {
                return keyboardClickCooldownSeconds;
            }

            return controllerClickCooldownSeconds;
        }

        private static GameObject FindFirstClickable(List<RaycastResult> results)
        {
            for (int i = 0; i < results.Count; i++)
            {
                var go = results[i].gameObject;
                if (go == null) continue;

                Transform t = go.transform;
                while (t != null)
                {
                    var candidate = t.gameObject;
                    if (candidate.GetComponent<Selectable>() != null || candidate.GetComponent<IPointerClickHandler>() != null)
                        return candidate;
                    t = t.parent;
                }
            }

            return null;
        }

        private void CacheEventSystemAndCamera()
        {
            _eventSystem = EventSystem.current;
#if ENABLE_INPUT_SYSTEM
            _uiInputModule = _eventSystem != null
                ? _eventSystem.GetComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>()
                : null;
#endif

            if (cursorCanvas != null)
                _uiCam = cursorCanvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : cursorCanvas.worldCamera;
            else
                _uiCam = null;
        }

        private void EnsureCursorVisual()
        {
            if (cursorCanvas == null)
            {
                var canvasGo = new GameObject("VirtualCursorCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
                cursorCanvas = canvasGo.GetComponent<Canvas>();
                cursorCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
                cursorCanvas.sortingOrder = short.MaxValue;

                var scaler = canvasGo.GetComponent<CanvasScaler>();
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1920f, 1080f);

                DontDestroyOnLoad(canvasGo);
            }

            if (cursorRect != null)
                return;

            RectTransform prefab = _settings != null ? _settings.CursorVisualPrefab : null;
            if (prefab != null)
            {
                cursorRect = Instantiate(prefab, cursorCanvas.transform);
                cursorRect.name = prefab.name;

                foreach (var graphic in cursorRect.GetComponentsInChildren<Graphic>(true))
                    graphic.raycastTarget = false;
            }
            else
            {
                var cursorGo = new GameObject("VirtualCursor", typeof(RectTransform), typeof(Image));
                cursorGo.transform.SetParent(cursorCanvas.transform, false);
                cursorRect = cursorGo.GetComponent<RectTransform>();
                cursorRect.anchorMin = new Vector2(0.5f, 0.5f);
                cursorRect.anchorMax = new Vector2(0.5f, 0.5f);
                cursorRect.pivot = new Vector2(0.5f, 0.5f);
                cursorRect.sizeDelta = fallbackCursorSize;

                var image = cursorGo.GetComponent<Image>();
                image.color = fallbackCursorColor;
                image.raycastTarget = false;
            }
        }

        private void ShowCustomCursor()
        {
            if (cursorRect != null)
                cursorRect.gameObject.SetActive(true);
        }

        private void HideCustomCursor()
        {
            if (cursorRect != null && cursorRect.gameObject.activeSelf)
                cursorRect.gameObject.SetActive(false);
        }

        private static Vector2 ClampToScreen(Vector2 value)
        {
            value.x = Mathf.Clamp(value.x, 0f, Screen.width);
            value.y = Mathf.Clamp(value.y, 0f, Screen.height);
            return value;
        }

        private void ApplyScreenPosToCursor(Vector2 screenPos)
        {
            if (cursorRect == null) return;
            RectTransform parent = cursorRect.parent as RectTransform;
            if (parent == null) return;

            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, screenPos, _uiCam, out Vector2 local))
                cursorRect.anchoredPosition = local;
        }

        private Vector2 ReadControllerMoveVector()
        {
#if ENABLE_INPUT_SYSTEM
            if (Gamepad.current != null)
                return Gamepad.current.leftStick.ReadValue();
#endif
            return new Vector2(Input.GetAxisRaw(legacyAxisX), Input.GetAxisRaw(legacyAxisY));
        }

        private bool ReadKeyboardActivity()
        {
#if ENABLE_INPUT_SYSTEM
            if (Keyboard.current != null)
                return Keyboard.current.anyKey.wasPressedThisFrame;
#endif
            return Input.inputString.Length > 0 || Input.anyKeyDown;
        }

        private bool ReadControllerSubmitDown()
        {
#if ENABLE_INPUT_SYSTEM
            if (Gamepad.current != null)
                return Gamepad.current.buttonSouth.wasPressedThisFrame;
#endif
            return Input.GetKeyDown(legacySubmitButton);
        }

        private bool ReadControllerSubmitHeld()
        {
#if ENABLE_INPUT_SYSTEM
            if (Gamepad.current != null)
                return Gamepad.current.buttonSouth.isPressed;
#endif
            return Input.GetKey(legacySubmitButton);
        }

        private bool ReadControllerSubmitUp()
        {
#if ENABLE_INPUT_SYSTEM
            if (Gamepad.current != null)
                return Gamepad.current.buttonSouth.wasReleasedThisFrame;
#endif
            return Input.GetKeyUp(legacySubmitButton);
        }

        private Vector2 ApplyDeadZone(Vector2 stick)
        {
            float magnitude = stick.magnitude;
            if (magnitude < deadZone) return Vector2.zero;

            float remap = Mathf.InverseLerp(deadZone, 1f, magnitude);
            return stick.normalized * remap;
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

        private void HandleNameEntryControllerShortcuts()
        {
            if (!enableNameEntryControllerShortcuts) return;

            NameEntryPanelController panel = FindActiveNameEntryPanel();
            if (panel == null) return;

            if (ReadControllerBackspaceDown())
                panel.Backspace();

            panel.SetExternalShiftHeld(ReadControllerOneShotShiftHeld());

            if (ReadControllerCapsLockDown())
                panel.ToggleCapsLock();
        }

        private static NameEntryPanelController FindActiveNameEntryPanel()
        {
            NameEntryPanelController[] all = FindObjectsOfType<NameEntryPanelController>(true);
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i] == null) continue;
                if (!all[i].isActiveAndEnabled) continue;
                if (!all[i].gameObject.activeInHierarchy) continue;
                return all[i];
            }

            return null;
        }

        private bool ReadControllerBackspaceDown()
        {
#if ENABLE_INPUT_SYSTEM
            if (Gamepad.current != null)
                return Gamepad.current.buttonWest.wasPressedThisFrame;
#endif
            return Input.GetKeyDown(legacyBackspaceButton);
        }

        private bool ReadControllerOneShotShiftHeld()
        {
#if ENABLE_INPUT_SYSTEM
            if (Gamepad.current != null)
                return Gamepad.current.leftShoulder.isPressed;
#endif
            return Input.GetKey(legacyOneShotShiftButton);
        }

        private bool ReadControllerCapsLockDown()
        {
#if ENABLE_INPUT_SYSTEM
            if (Gamepad.current != null)
                return Gamepad.current.leftTrigger.wasPressedThisFrame;
#endif
            return Input.GetKeyDown(legacyCapsLockButton);
        }

        private static bool IsCursorAllowedForCurrentGameState()
        {
            if (GameplayUIInputGuard.IsDialogueBlockingGameplayUi)
                return false;

            var gm = global::GameManager_Racing.Instance;
            if (gm == null) return true;

            if (gm.ProgressState == global::GameManager_Racing.GameProgressState.Dialogue)
                return false;

            if (gm.ProgressState == global::GameManager_Racing.GameProgressState.LoadingRun)
                return false;

            // Hide cursor during active driving loop; menus (pause, skill tree, options, etc.) remain cursor-enabled.
            return gm.ProgressState != global::GameManager_Racing.GameProgressState.InRun;
        }
    }
}
