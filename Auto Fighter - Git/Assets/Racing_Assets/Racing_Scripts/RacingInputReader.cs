using AutoFighter.Core;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Single source of input for the racing game using the new Input System (1.7+ / 1.14+).
/// Assign an Input Action Asset in the inspector, or leave empty to use built-in default bindings.
/// Access via RacingInputReader.Instance from GameManager_Racing, CarController, UIManager_Racing, etc.
/// </summary>
[DefaultExecutionOrder(-200)]
public class RacingInputReader : MonoBehaviour
{
    public static RacingInputReader Instance { get; private set; }

    [Header("Input Action Asset (optional)")]
    [Tooltip("Assign a RacingInputActions asset here. If null, default bindings are used (keyboard + gamepad).")]
    [SerializeField] private InputActionAsset actionAsset;

    [Header("Options")]
    [SerializeField, Range(0f, 1f)] private float steerDeadzone = 0.12f;
    [SerializeField, Range(0f, 1f)] private float triggerThreshold = 0.1f;
    [SerializeField, Range(0f, 0.5f)] private float skillTreeStickDeadzone = 0.18f;
    [SerializeField] private bool autoHideCursorByInputDevice = true;
    [SerializeField, Range(0f, 1f)] private float cursorGamepadMoveThreshold = 0.12f;

    private InputActionMap _racingMap;
    private InputActionMap _skillTreeMap;
    private InputAction _steerAction;
    private InputAction _accelerateAction;
    private InputAction _brakeAction;
    private InputAction _boostAction;
    private InputAction _driftAction;
    private InputAction _restartAction;
    private InputAction _mashSouthAction;
    private InputAction _mashNorthAction;
    private InputAction _mashEastAction;
    private InputAction _mashWestAction;
    private InputAction _fireAction;
    private InputAction _panAction;
    private InputAction _zoomInAction;
    private InputAction _zoomOutAction;
    private InputAction _fovPeekAction;
    private InputAction _airTricksAction;
    private InputAction _steerVerticalAction;

    private bool _usingRuntimeAsset;
    private float _steerCache;
    private float _steerVerticalCache;
    private bool _airTricksHeldCache;
    private float _accelerateCache;
    private float _brakeCache;
    private bool _boostDownCache;
    private bool _driftHeldCache;
    private bool _restartDownCache;
    private bool _mashSouthDownCache;
    private bool _mashNorthDownCache;
    private bool _mashEastDownCache;
    private bool _mashWestDownCache;
    private bool _fireHeldCache;
    private Vector2 _panCache;
    private float _zoomCache;
    private bool _fovPeekCache;

    public float Steer => _steerCache;
    /// <summary>Left stick Y (or keyboard W/S axis) for airborne pitch tricks.</summary>
    public float SteerVertical => _steerVerticalCache;
    /// <summary>Left bumper — hold while airborne to perform flips and spins.</summary>
    public bool AirTricksHeld => _airTricksHeldCache;
    public float Accelerate => _accelerateCache;
    public float Brake => _brakeCache;
    public bool BoostDown => _boostDownCache;
    public bool DriftHeld => _driftHeldCache;
    public bool RestartDown => _restartDownCache;
    public bool MashSouthDown => _mashSouthDownCache;
    public bool MashNorthDown => _mashNorthDownCache;
    public bool MashEastDown => _mashEastDownCache;
    public bool MashWestDown => _mashWestDownCache;
    public bool AnyMashDown => _mashSouthDownCache || _mashNorthDownCache || _mashEastDownCache || _mashWestDownCache;
    public bool FireHeld => _fireHeldCache;
    public Vector2 Pan => _panCache;
    public float Zoom => _zoomCache;
    public bool FovPeekHeld => _fovPeekCache;

    public enum FaceButton { South, North, East, West }

    public bool GetMashDown(FaceButton button)
    {
        return button switch
        {
            FaceButton.South => _mashSouthDownCache,
            FaceButton.North => _mashNorthDownCache,
            FaceButton.East => _mashEastDownCache,
            FaceButton.West => _mashWestDownCache,
            _ => false
        };
    }

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        if (actionAsset != null)
        {
            _racingMap = actionAsset.FindActionMap("Racing");
            _skillTreeMap = actionAsset.FindActionMap("SkillTreeUI");
            if (_racingMap != null)
                CacheRacingActions();
            if (_skillTreeMap != null)
                CacheSkillTreeActions();
        }

        if (_racingMap == null)
        {
            CreateDefaultAsset();
            _usingRuntimeAsset = true;
        }
    }

    private void CacheRacingActions()
    {
        _steerAction = _racingMap.FindAction("Steer");
        _accelerateAction = _racingMap.FindAction("Accelerate");
        _brakeAction = _racingMap.FindAction("Brake");
        _boostAction = _racingMap.FindAction("Boost");
        _driftAction = _racingMap.FindAction("Drift");
        _restartAction = _racingMap.FindAction("Restart");
        _mashSouthAction = _racingMap.FindAction("MashSouth");
        _mashNorthAction = _racingMap.FindAction("MashNorth");
        _mashEastAction = _racingMap.FindAction("MashEast");
        _mashWestAction = _racingMap.FindAction("MashWest");
        _fireAction = _racingMap.FindAction("Fire");
        _fovPeekAction = _racingMap.FindAction("FovPeek");
        _airTricksAction = _racingMap.FindAction("AirTricks");
        _steerVerticalAction = _racingMap.FindAction("SteerVertical");
    }

    private void CacheSkillTreeActions()
    {
        _panAction = _skillTreeMap.FindAction("Pan");
        _zoomInAction = _skillTreeMap.FindAction("ZoomIn");
        _zoomOutAction = _skillTreeMap.FindAction("ZoomOut");
    }

    private void CreateDefaultAsset()
    {
        actionAsset = ScriptableObject.CreateInstance<InputActionAsset>();
        _racingMap = actionAsset.AddActionMap("Racing");

        // Steer: 1DAxis composite (A/D) + left stick X with deadzone (Input System 1.14+ processors)
        var steer = _racingMap.AddAction("Steer", InputActionType.Value);
        steer.AddCompositeBinding("1DAxis").With("Negative", "<Keyboard>/a").With("Positive", "<Keyboard>/d");
        steer.AddBinding("<Gamepad>/leftStick/x", processors: "AxisDeadzone(min=0.12,max=1)");

        // Accelerate / Brake: keyboard + triggers with optional AxisDeadzone for triggers
        var accel = _racingMap.AddAction("Accelerate", InputActionType.Value);
        accel.AddBinding("<Keyboard>/w");
        accel.AddBinding("<Gamepad>/rightTrigger", processors: "AxisDeadzone(min=0.1,max=1)");

        var brake = _racingMap.AddAction("Brake", InputActionType.Value);
        brake.AddBinding("<Keyboard>/s");
        brake.AddBinding("<Gamepad>/leftTrigger", processors: "AxisDeadzone(min=0.1,max=1)");

        var boost = _racingMap.AddAction("Boost", InputActionType.Button);
        boost.AddBinding("<Keyboard>/space");
        boost.AddBinding("<Gamepad>/buttonSouth");
        var drift = _racingMap.AddAction("Drift", InputActionType.Button);
        drift.AddBinding("<Keyboard>/leftShift");
        drift.AddBinding("<Gamepad>/buttonEast");
        var restart = _racingMap.AddAction("Restart", InputActionType.Button);
        restart.AddBinding("<Keyboard>/r");
        restart.AddBinding("<Gamepad>/buttonSouth");

        var mashSouth = _racingMap.AddAction("MashSouth", InputActionType.Button);
        mashSouth.AddBinding("<Gamepad>/buttonSouth");
        mashSouth.AddBinding("<Keyboard>/space");
        var mashNorth = _racingMap.AddAction("MashNorth", InputActionType.Button);
        mashNorth.AddBinding("<Gamepad>/buttonNorth");
        mashNorth.AddBinding("<Keyboard>/space");
        var mashEast = _racingMap.AddAction("MashEast", InputActionType.Button);
        mashEast.AddBinding("<Gamepad>/buttonEast");
        var mashWest = _racingMap.AddAction("MashWest", InputActionType.Button);
        mashWest.AddBinding("<Gamepad>/buttonWest");

        var fire = _racingMap.AddAction("Fire", InputActionType.Button);
        fire.AddBinding("<Mouse>/leftButton");
        fire.AddBinding("<Gamepad>/rightTrigger");

        var fovPeek = _racingMap.AddAction("FovPeek", InputActionType.Button);
        fovPeek.AddBinding("<Keyboard>/tab");

        var airTricks = _racingMap.AddAction("AirTricks", InputActionType.Button);
        airTricks.AddBinding("<Gamepad>/leftShoulder");
        airTricks.AddBinding("<Keyboard>/q");

        var steerVertical = _racingMap.AddAction("SteerVertical", InputActionType.Value);
        steerVertical.AddCompositeBinding("1DAxis").With("Negative", "<Keyboard>/s").With("Positive", "<Keyboard>/w");
        steerVertical.AddBinding("<Gamepad>/leftStick/y", processors: "AxisDeadzone(min=0.12,max=1)");

        _skillTreeMap = actionAsset.AddActionMap("SkillTreeUI");
        var pan = _skillTreeMap.AddAction("Pan", InputActionType.Value);
        pan.AddBinding("<Gamepad>/rightStick", processors: "StickDeadzone(min=0.18,max=1)");
        var zoomIn = _skillTreeMap.AddAction("ZoomIn", InputActionType.Value);
        zoomIn.AddBinding("<Gamepad>/rightTrigger", processors: "AxisDeadzone(min=0.1,max=1)");
        var zoomOut = _skillTreeMap.AddAction("ZoomOut", InputActionType.Value);
        zoomOut.AddBinding("<Gamepad>/leftTrigger", processors: "AxisDeadzone(min=0.1,max=1)");

        CacheRacingActions();
        CacheSkillTreeActions();
    }

    void OnEnable()
    {
        // Maps must be contained in the asset's state: enable the asset first, then toggle maps.
        if (actionAsset != null)
        {
            actionAsset.Enable();
            _racingMap?.Enable();
            _skillTreeMap?.Disable(); // enabled only when skill tree UI is active
        }
    }

    void OnDisable()
    {
        if (actionAsset != null)
            actionAsset.Disable();
    }

    /// <summary>
    /// Skill tree UI on: enable SkillTreeUI map and <b>disable</b> Racing map.
    /// Otherwise Racing Restart/Boost/Mash share gamepad South (and other bindings) with UI Submit / clicks and can cause phantom "Play" or double-use with the tree.
    /// Skill tree off: disable SkillTreeUI and re-enable Racing.
    /// </summary>
    public void SetSkillTreeMapEnabled(bool enabled)
    {
        if (enabled)
        {
            if (_skillTreeMap == null)
            {
                Debug.LogWarning("[RacingInputReader] SkillTreeUI action map not found — Racing map left enabled (add SkillTreeUI map to your Input Action Asset).");
                return;
            }

            _skillTreeMap.Enable();
            _racingMap?.Disable();
        }
        else
        {
            _skillTreeMap?.Disable();
            _racingMap?.Enable();
        }
    }

    void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
        if (_usingRuntimeAsset && actionAsset != null)
            actionAsset.Disable();
    }

    void Update()
    {
        // Racing and skill-tree maps are mutually exclusive (see SetSkillTreeMapEnabled). Read only enabled map(s).
        if (_racingMap != null && _racingMap.enabled)
        {
            _steerCache = ReadSteer();
            _steerVerticalCache = ReadSteerVertical();
            _airTricksHeldCache = ReadAirTricksHeld();
            _accelerateCache = ReadAccelerate();
            _brakeCache = ReadBrake();
            _boostDownCache = _boostAction != null && _boostAction.triggered;
            _driftHeldCache = _driftAction != null && _driftAction.IsPressed();
            _restartDownCache = _restartAction != null && _restartAction.triggered;
            _mashSouthDownCache = _mashSouthAction != null && _mashSouthAction.triggered;
            _mashNorthDownCache = _mashNorthAction != null && _mashNorthAction.triggered;
            _mashEastDownCache = _mashEastAction != null && _mashEastAction.triggered;
            _mashWestDownCache = _mashWestAction != null && _mashWestAction.triggered;
            _fireHeldCache = _fireAction != null && _fireAction.IsPressed();
            _fovPeekCache = _fovPeekAction != null && _fovPeekAction.IsPressed();
        }
        else
        {
            ZeroRacingCaches();
        }

        if (_skillTreeMap != null && _skillTreeMap.enabled && !GameplayUIInputGuard.IsDialogueBlockingGameplayUi)
        {
            _panCache = ReadPan();
            _zoomCache = ReadZoom();
        }
        else
        {
            _panCache = Vector2.zero;
            _zoomCache = 0f;
        }

        UpdateCursorVisibilityByDevice();
    }

    private void ZeroRacingCaches()
    {
        _steerCache = _steerVerticalCache = _accelerateCache = _brakeCache = 0f;
        _airTricksHeldCache = false;
        _boostDownCache = false;
        _driftHeldCache = false;
        _restartDownCache = false;
        _mashSouthDownCache = false;
        _mashNorthDownCache = false;
        _mashEastDownCache = false;
        _mashWestDownCache = false;
        _fireHeldCache = false;
        _fovPeekCache = false;
    }

    private float ReadSteer()
    {
        if (_steerAction == null) return 0f;
        float v = _steerAction.ReadValue<float>();
        if (Mathf.Abs(v) < steerDeadzone) return 0f;
        return Mathf.Clamp(v, -1f, 1f);
    }

    private float ReadSteerVertical()
    {
        if (_steerVerticalAction != null)
        {
            float v = _steerVerticalAction.ReadValue<float>();
            if (Mathf.Abs(v) < steerDeadzone) return 0f;
            return Mathf.Clamp(v, -1f, 1f);
        }

        if (Gamepad.current != null)
        {
            float v = Gamepad.current.leftStick.y.ReadValue();
            if (Mathf.Abs(v) < steerDeadzone) return 0f;
            return Mathf.Clamp(v, -1f, 1f);
        }

        float kb = 0f;
        if (Keyboard.current != null)
        {
            if (Keyboard.current.wKey.isPressed) kb += 1f;
            if (Keyboard.current.sKey.isPressed) kb -= 1f;
        }
        return Mathf.Clamp(kb, -1f, 1f);
    }

    private bool ReadAirTricksHeld()
    {
        if (_airTricksAction != null)
            return _airTricksAction.IsPressed();

        if (Keyboard.current != null && Keyboard.current.qKey.isPressed)
            return true;
        return Gamepad.current != null && Gamepad.current.leftShoulder.isPressed;
    }

    private float ReadAccelerate()
    {
        if (_accelerateAction == null) return 0f;
        float v = _accelerateAction.ReadValue<float>();
        if (v < triggerThreshold) return 0f;
        return Mathf.Clamp01(v);
    }

    private float ReadBrake()
    {
        if (_brakeAction == null) return 0f;
        float v = _brakeAction.ReadValue<float>();
        if (v < triggerThreshold) return 0f;
        return Mathf.Clamp01(v);
    }

    private Vector2 ReadPan()
    {
        if (_panAction == null) return Vector2.zero;
        Vector2 v = _panAction.ReadValue<Vector2>();
        if (v.magnitude < skillTreeStickDeadzone) return Vector2.zero;
        return v;
    }

    private float ReadZoom()
    {
        if (_zoomInAction == null && _zoomOutAction == null) return 0f;
        float inVal = _zoomInAction != null ? _zoomInAction.ReadValue<float>() : 0f;
        float outVal = _zoomOutAction != null ? _zoomOutAction.ReadValue<float>() : 0f;
        return Mathf.Clamp01(inVal) - Mathf.Clamp01(outVal);
    }

    private void UpdateCursorVisibilityByDevice()
    {
        if (!autoHideCursorByInputDevice) return;
        if (!CoreVirtualCursor.ShouldRacingInputReaderManageCursorVisibility()) return;

        bool mouseUsed = false;
        if (Mouse.current != null)
        {
            Vector2 delta = Mouse.current.delta.ReadValue();
            mouseUsed = delta.sqrMagnitude > 0.01f
                || Mouse.current.leftButton.wasPressedThisFrame
                || Mouse.current.rightButton.wasPressedThisFrame
                || Mouse.current.middleButton.wasPressedThisFrame;
        }

        bool gamepadUsed = false;
        if (Gamepad.current != null)
        {
            Vector2 ls = Gamepad.current.leftStick.ReadValue();
            Vector2 rs = Gamepad.current.rightStick.ReadValue();
            gamepadUsed =
                ls.sqrMagnitude > (cursorGamepadMoveThreshold * cursorGamepadMoveThreshold) ||
                rs.sqrMagnitude > (cursorGamepadMoveThreshold * cursorGamepadMoveThreshold) ||
                Gamepad.current.buttonSouth.wasPressedThisFrame ||
                Gamepad.current.buttonNorth.wasPressedThisFrame ||
                Gamepad.current.buttonEast.wasPressedThisFrame ||
                Gamepad.current.buttonWest.wasPressedThisFrame ||
                Gamepad.current.startButton.wasPressedThisFrame ||
                Gamepad.current.selectButton.wasPressedThisFrame ||
                Gamepad.current.leftShoulder.wasPressedThisFrame ||
                Gamepad.current.rightShoulder.wasPressedThisFrame;
        }

        if (mouseUsed)
            Cursor.visible = true;
        else if (gamepadUsed)
            Cursor.visible = false;
    }
}
