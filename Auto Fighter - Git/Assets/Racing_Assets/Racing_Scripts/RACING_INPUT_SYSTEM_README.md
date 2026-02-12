# Racing Game – New Input System Setup & Rundown

## What Was Done

Your car racing game now uses **Unity’s new Input System** (package `com.unity.inputsystem`, **1.14.2** or 1.7+). All racing input goes through a single component, **RacingInputReader**, which works with or without an Input Action Asset. The code is written for the 1.14 API (extension methods on `InputActionAsset`/`InputActionMap`) and uses built-in **processors** (AxisDeadzone, StickDeadzone) on default bindings for better stick/trigger feel.

---

## 1. Scene Setup (Required)

### Step 1: Install / confirm the Input System package

- The package **com.unity.inputsystem** (1.7.0) was added to `Packages/manifest.json`.
- In Unity: **Window > Package Manager** and confirm **Input System** is installed.
- If Unity asks to **restart** or to change **Active Input Handling**, choose:
  - **Edit > Project Settings > Player > Other Settings > Active Input Handling**
  - Set to **Input System Package (New)** or **Both** (recommended: **Both** so keyboard/mouse still work like before).

### Step 2: Add the RacingInputReader to the scene

1. Open your **racing scene** (e.g. **Racer_Incremental**).
2. Create an empty GameObject (e.g. name it **RacingInput**).
3. Add the **RacingInputReader** component to it:
   - **Add Component > Racing Input Reader** (script is in `Assets/Racing_Assets/Racing_Scripts/`).
4. Leave **Input Action Asset** empty unless you create a custom asset (see below).
5. Save the scene.

That’s all that’s required. The reader will create **default bindings at runtime** if no asset is assigned.

### Step 3 (Optional): Use a custom Input Action Asset

- **Create:** Right‑click in Project **> Create > Input Actions**. Name it e.g. **RacingInputActions**.
- **Edit:** Double‑click the asset to open the Input Actions editor.
- Add two **Action Maps**:
  - **Racing** – driving, boost, drift, restart, mash, fire, FOV peek.
  - **SkillTreeUI** – pan (right stick), zoom (triggers).
- Add actions and bindings to match the names and behavior described in **RacingInputReader.cs** (e.g. **Steer**, **Accelerate**, **Brake**, **Boost**, **Drift**, **Restart**, **MashSouth** / **MashNorth** / **MashEast** / **MashWest**, **Fire**, **FovPeek** in **Racing**; **Pan**, **ZoomIn**, **ZoomOut** in **SkillTreeUI**).
- Assign this asset to the **Input Action Asset** field on **RacingInputReader**.

If no asset is assigned, the reader still works using the built‑in default bindings.

---

## 2. What Was Removed (Old System)

- **Legacy Input Manager** usage for racing and UI was replaced by the new Input System where **RacingInputReader** is present.
- No code was deleted; all legacy calls are still used as a **fallback** when `RacingInputReader.Instance == null` (e.g. if you forget to add the reader to the scene).
- Removed / replaced in behavior:
  - **CarController:** `Input.GetAxisRaw("Horizontal")`, `Input.GetKey(KeyCode.W/S)`, `Input.GetAxisRaw("Vertical"/"RightTrigger"/"LeftTrigger")`, `Input.GetKey(driftKey)`, `Input.GetKeyDown(boostKey)`, mash face‑button KeyCodes, and Space for mash.
  - **GameManager_Racing:** `Input.GetKeyDown(KeyCode.R)` and `Input.GetKeyDown(PAD_X)` for restart.
  - **UIManager_Racing:** `Input.GetKeyDown(car.MashFaceButtonKey)` / `MashRequiredKey` for crash recovery mash; now uses `car.GetMashRequiredButtonDown()` (which uses the reader when available).
  - **RacingSkillUI:** `Input.GetAxisRaw(gamepadPanAxisX/Y)` and trigger axes for skill tree pan/zoom; now uses **RacingInputReader** Pan and Zoom when the reader exists.
  - **CarTurretController:** `Input.GetKey(fireKey)` / `Input.GetButton("Fire1")` for fire; now uses reader **FireHeld** when available.
  - **CameraFollow:** `Input.GetKeyDown/GetKeyUp(fovIncreaseKey)` for map peek; now uses reader **FovPeekHeld** (held state) with legacy fallback.

So: the **old system** is “replaced” by the new one when the reader is in the scene; otherwise the same legacy APIs still run.

---

## 3. What Replaced It (New System)

- **RacingInputReader** (singleton):
  - Holds an optional **Input Action Asset** or builds a **default asset at runtime** (keyboard + gamepad).
  - Exposes one place for all racing input: **Steer**, **Accelerate**, **Brake**, **BoostDown**, **DriftHeld**, **RestartDown**, **MashSouth/North/East/West**, **AnyMashDown**, **FireHeld**, **FovPeekHeld**, and for skill tree: **Pan**, **Zoom**, plus **SetSkillTreeMapEnabled** so triggers aren’t used for zoom during a run.
- **CarController** uses helpers that read from **RacingInputReader** when `Instance != null`, else legacy:
  - **GetSteerRaw()**, **GetAccelerateKeyOrTrigger()**, **GetBrakeKeyOrTrigger()**, **GetDriftHeld()**, **GetBoostDown()**, **GetMashRequiredButtonDown()**, **GetFireHeld()** (the last used by turret).
- **GameManager_Racing** uses **RestartDown** from the reader for restart (R and gamepad South).
- **UIManager_Racing** uses **CarController.GetMashRequiredButtonDown()** (which uses the reader when available).
- **RacingSkillUI** uses **Pan** and **Zoom** from the reader and calls **SetSkillTreeMapEnabled(true)** in OnEnable and **SetSkillTreeMapEnabled(false)** in OnDisable so the **SkillTreeUI** map is only active when the skill tree is visible.
- **CarTurretController** uses **FireHeld** from the reader when available.
- **CameraFollow** uses **FovPeekHeld** for map peek (hold Tab), with legacy key fallback.

Default bindings (if no asset is assigned):

- **Steer:** A/D, left stick X  
- **Accelerate:** W, right trigger  
- **Brake:** S, left trigger  
- **Boost:** Space, South (A on Xbox, Cross on PS)  
- **Drift:** Left Shift, East (B on Xbox, Circle on PS)  
- **Restart:** R, South  
- **Mash:** South/North/East/West + Space  
- **Fire:** Left mouse, right trigger  
- **FOV peek:** Tab  
- **Skill tree:** Right stick = pan, triggers = zoom (only when skill tree is active)

---

## 4. Next Steps

1. **Open your racing scene** and add the **RacingInputReader** component to a GameObject (e.g. **RacingInput**) as in Step 2 above.
2. **Set Active Input Handling** to **Both** (or **Input System Package (New)**) in **Project Settings > Player** so the new system is used.
3. **Test:** Run the game, drive with keyboard and controller, open the skill tree and pan/zoom, restart from the results screen with R and with gamepad South. If the reader is missing, the game falls back to the old input and should still run.
4. **Optional:** Create and assign a custom **Input Action Asset** to change bindings or add more devices without code changes.
5. **Moving past freezing:** The new system gives consistent, rebindable input and avoids legacy axis/button quirks. Restart on the results screen uses **RestartDown** (R or South) every frame, so it should work even when timeScale is 0. If you still see freezes, the cause is likely elsewhere (e.g. `carController` null or mash state), as in the earlier analysis.

---

## 5. File / Code Summary

| File | Change |
|------|--------|
| **Packages/manifest.json** | Added `com.unity.inputsystem` 1.7.0. |
| **RacingInputReader.cs** | **New.** Singleton; optional asset or runtime default map; exposes Steer, Accelerate, Brake, Boost, Drift, Restart, Mash, Fire, FovPeek, Pan, Zoom; **SetSkillTreeMapEnabled** for skill tree. |
| **CarController.cs** | Added helpers (e.g. **GetSteerRaw**, **GetBoostDown**, **GetMashRequiredButtonDown**) that use **RacingInputReader** when present, else legacy. Replaced direct **Input.** calls with these helpers. |
| **GameManager_Racing.cs** | Restart uses **RacingInputReader.Instance.RestartDown** when reader exists. |
| **UIManager_Racing.cs** | Mash uses **car.GetMashRequiredButtonDown()**; editor Space mash uses **RacingInputReader.Instance.AnyMashDown** when reader exists. |
| **RacingSkillUI.cs** | Pan/zoom use reader **Pan** and **Zoom** when reader exists; **SetSkillTreeMapEnabled(true/false)** in OnEnable/OnDisable. |
| **CarTurretController.cs** | Fire uses **RacingInputReader.Instance.FireHeld** when reader exists. |
| **CameraFollow.cs** | Map peek uses **FovPeekHeld** (held) with legacy key fallback. |

All legacy **Input.** usage remains as fallback when **RacingInputReader.Instance** is null.
