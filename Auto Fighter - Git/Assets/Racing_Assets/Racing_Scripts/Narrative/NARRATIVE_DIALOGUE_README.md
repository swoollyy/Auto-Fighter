# Narrative & Dialogue Framework – Setup in Unity

This folder contains a **lightweight narrative/dialogue system** for story and cutscene-style dialogue in your incremental racing game. You can show dialogue lines, optional portraits, and trigger sequences based on story progression (e.g. first run, after N runs, or custom flags).

Dialogue text uses **TextMeshPro** and supports **rich, animatable text** like many indie games: **bold**, *italic*, different colors per word, size changes, and an optional **typewriter effect** that reveals text character-by-character (press advance to skip, press again to go to next line).

---

## 1. Text Formatting (Bold, Italic, Colors, etc.)

Dialogue lines are drawn with **TextMeshPro**, so you can use TMP’s **rich text tags** in your Dialogue Sequence **Text** fields.

### Enable Rich Text in Unity

1. Select the **Dialogue Text** (TMP_Text) GameObject used for the dialogue body.
2. In the Inspector, under **TextMeshPro - Text (UI)** → **Extra Settings**, ensure **Rich Text** is **enabled** (checked). It is usually on by default.

### Tags you can use in dialogue lines

| Tag | Example | Effect |
|-----|--------|--------|
| Bold | `Don't <b>skip</b> the tutorial.` | **skip** |
| Italic | `That was <i>incredible</i>.` | *incredible* |
| Color (hex) | `Watch for <color=#FF0000>obstacles</color>.` | Red word |
| Color (name) | `Use <color=green>boost</color> wisely.` | Green word |
| Size | `Big <size=36>warning</size> here.` | Larger text |
| Size delta | `Small <size=-8>print</size>.` | Smaller text |
| Strikethrough | `<s>Old line</s> New line.` | ~~Old line~~ |

- **Hex colors:** `<color=#RRGGBB>` or `<color=#RRGGBBAA>` (e.g. `<color=#FF00FF>` for magenta).
- **Named colors:** black, blue, green, orange, purple, red, white, yellow (e.g. `<color=red>`).
- Tags can be **nested**: e.g. `A <b><color=#FFFF00>bold yellow</color></b> word.`

Typewriter effect (if enabled on DialogueUI) works with all of these: text is parsed first, then revealed character-by-character, so bold/colors stay correct.

### Using size/color on only certain words

You can mix tags in a single line:

- Bigger word: `This is a <size=140%>BIG</size> word.`
- Colored word: `This is <color=#00FFAA>mint</color>.`
- Combined: `This is <b><size=130%><color=#FFCC00>IMPORTANT</color></size></b>.`

### How per-word vertex effects work (like many indie games)

Yes – having **one effect component per type** (parabola, jitter, shrink/enlarge) is how it’s often done. Each effect runs on the same TMP_Text, but **only certain words** get that effect. The rest of the text stays at default.

**How words are "flagged":** You don’t flag words in code. You **mark them in the dialogue string** using TextMeshPro’s **link** tag. In your Dialogue Sequence **Text** you write:

- `<link="wave">different</link>` – only the word "different" gets the parabola wave.
- `<link="jitter">shaky</link>` – only "shaky" gets the jitter effect.
- `<link="pop">big</link>` – only "big" gets the zoom/pulse effect.

Each effect component has a **Link Tag** field (e.g. `wave`, `jitter`, `pop`). The script checks every character: "Is this character inside a `<link="wave">` … `</link>`?" If yes, it applies the effect to that character’s vertices; otherwise it leaves the mesh at the default (rest) position. So one string can mix normal text with wavy, jittery, and popping words.

**In the script:** Each frame we read TMP’s `textInfo.linkInfo` (which lists all `<link="id">…</link>` ranges). For each character we ask: "Is this character index inside a link whose ID matches this effect’s Link Tag?" If yes, we apply the vertex animation (offset or scale); if no, we copy the cached rest-position vertices. The "default" is the cached mesh from when the text was last changed.

**Summary:** You add one component per effect type. You set the Link Tag on each component to match the ID you use in the text. In your dialogue lines you wrap only the words that should have that effect. No extra setup in Unity beyond adding the components and typing the link tags in the dialogue.

### Why a Coordinator and Uploader? (Layman’s version)

All the text effects (wave, jitter, zoom, rainbow) change the same mesh: they move vertices or change colors. If each effect does its own “refresh” or “draw” step, they can undo each other or fight over when the mesh is reset.

- **The problem:** Something has to tell TMP “recompute the base text layout” (that’s **ForceMeshUpdate**). If more than one script does that, the mesh gets reset in the middle of the frame and other effects’ work is wiped. Similarly, something has to send the final mesh to the GPU (**UpdateGeometry**). If every effect does that, order and overwrites get messy.
- **The fix:** Two “traffic cop” components:
  - **TMP Effect Coordinator** runs first. It is the **only** place that calls ForceMeshUpdate (and only when the text actually changes). It keeps one shared “rest” snapshot (positions and colors) that all effects use as their base. So no effect ever resets the mesh; they all read from the same rest state.
  - **TMP Effect Uploader** runs last. It is the **only** place that pushes the final mesh (vertices + colors) to the GPU. Effects only change data in memory; they never call UpdateGeometry. At the end of the frame the uploader does one push, so what you see is the combined result of all effects.
- **Result:** You can add or remove effects without them breaking each other. New effects you add later should: (1) get the rest cache from the coordinator when present, and (2) never call UpdateGeometry when the uploader is present. Then expansion stays safe and predictable.

**Setup:** On the same GameObject as your Dialogue Text (TMP_Text), add **TMP Effect Coordinator** and **TMP Effect Uploader** once. Then add whichever effect components you want (wave, jitter, zoom, rainbow). The coordinator and uploader are optional: if you don’t add them, each effect falls back to its own cache and upload, but using both is recommended so all effects play nicely together.

### Vertex effects (parabola, jitter, zoom)

Add these components to the **Dialogue Text** (or any TMP_Text) GameObject. Each effect can apply to the **whole line** (leave **Link Tag** empty) or **only words you wrap** in the matching link tag.

| Effect | Component | Link tag (in text) | Use for |
|--------|-----------|--------------------|--------|
| Traveling parabola | **TMP Parabola Wave Effect** | `<link="wave">word</link>` | Wave that moves across the word, then resets. |
| Shake / jitter | **TMP Jitter Effect** | `<link="jitter">word</link>` | Shaky, nervous, or glitchy words. |
| Shrink / enlarge (pulse) | **TMP Zoom Effect** | `<link="pop">word</link>` | Words that pulse or "pop" in size. |
| Rainbow colors (cycle) | **TMP Rainbow Color Effect** | `<link="rainbow">word</link>` | Rainbow cycling colors over time. |

**Example dialogue line:**  
`This is <link="jitter">shaky</link> and <link="wave">smooth</link> and <link="pop">big</link>.`  
– "shaky" jitters, "smooth" gets the parabola wave, "big" pulses; the rest is normal.

**Example with rich text + link effects combined:**  
`This is <link="rainbow"><size=130%>RAINBOW</size></link> and <link="wave"><color=#66CCFF>wavy</color></link>.`  
- `RAINBOW` cycles colors and is bigger (size tag is static).
- `wavy` uses your wave vertex effect and is tinted light-blue via TMP rich text.

**Nested link tags (multiple effects on the same word):**  
Use both opening and closing tags so the same text is inside multiple links. Example:  
`<link="wave"><link="rainbow">heyyyy!</link></link>`  
– "heyyyy!" gets both the wave and the rainbow effect. (Both `</link>` are required.)

**Setup:** Select the Dialogue Text GameObject → **Add Component** → the effect (e.g. **TMP Parabola Wave Effect**). Set **Link Tag** to the ID you use in the text (e.g. `wave`). Leave **Link Tag** empty to apply that effect to the entire line. Tune amplitude, speed, etc. in the Inspector. You can add **all three** components to the same TMP_Text; each will only touch the characters inside its link tag.

---

## 2. Scripts Overview

| Script | Purpose |
|--------|--------|
| **DialogueLineSO** (data) | Defines a single line: speaker name, text, optional portrait, delay, auto-advance. Used inside DialogueSequenceSO. |
| **DialogueSequenceSO** | ScriptableObject: ordered list of dialogue lines, optional “pause game”, optional “set story flag when done”. |
| **DialogueManager** | Singleton: plays a sequence, shows UI, advances on key/click or auto-advance. Restores time scale when done. |
| **DialogueUI** | UI component: wires speaker text, dialogue text, optional portrait. Optional typewriter effect. |
| **TMPLinkEffectHelper** | Static helper: tells if a character is inside a &lt;link="id"&gt; range (used by the effect scripts). |
| **TMPEffectCoordinator** | Single place that calls ForceMeshUpdate and holds the “rest” mesh cache; effects use this when present. |
| **TMPEffectUploader** | Single place that pushes the final mesh to the GPU (UpdateGeometry); effects skip uploading when this is present. |
| **TMPParabolaWaveEffect** | Vertex animation: parabolic wave (only on &lt;link="wave"&gt; words, or whole line if Link Tag empty). |
| **TMPJitterEffect** | Vertex animation: jitter/shake (only on &lt;link="jitter"&gt; words, or whole line if Link Tag empty). |
| **TMPZoomEffect** | Vertex animation: shrink/enlarge pulse (only on &lt;link="pop"&gt; words, or whole line if Link Tag empty). |
| **TMPRainbowColorEffect** | Vertex animation: cycles vertex colors (only on &lt;link="rainbow"&gt; words, or whole line if Link Tag empty). |
| **NarrativeDirector** | Tracks story flags and run count; can auto-trigger dialogue (e.g. intro on first run, “after first run” dialogue). |

---

## 3. Unity Setup (Step by Step)

### Step 1: Dialogue Canvas and UI

1. In your **racing scene** (e.g. Racer_Incremental), create a **UI Canvas** for dialogue (or use an existing Canvas and add a child panel).
2. **Create** (under the Canvas):
   - Empty GameObject named **DialoguePanel**.
   - Add a **Canvas Group** (Add Component → Canvas Group) so you can fade/block raycasts.
   - Add a **Panel** (UI → Panel) or Image as background for the dialogue box.
   - Under the panel:
     - **Speaker name**: UI → Text - TextMeshPro (e.g. “SpeakerName”). Position at top.
     - **Dialogue text**: UI → Text - TextMeshPro (e.g. “DialogueText”). Main body, multi-line.
     - **Portrait** (optional): UI → Image (e.g. “Portrait”). Leave empty if you don’t use portraits.
     - **Advance hint** (optional): TextMeshPro text like “Space to continue”.
3. **DialoguePanel** should be **inactive** by default (uncheck the checkbox in the Inspector), or the DialogueUI script will hide it at Start.

### Step 2: DialogueUI Component

1. Select **DialoguePanel** (or the GameObject that holds the dialogue box).
2. **Add Component** → **Dialogue UI** (script is in `Assets/Racing_Assets/Racing_Scripts/Narrative/`).
3. In the Inspector, assign:
   - **Speaker Text** → your speaker name TMP_Text.
   - **Dialogue Text** → your dialogue body TMP_Text (enable **Rich Text** on this TMP_Text for tags).
   - **Use Typewriter Effect** → optional: reveal text character-by-character; **Typewriter Chars Per Second** sets speed.
   - **Portrait Image** → your portrait Image (optional).
   - **Panel Root** → the GameObject to show/hide (usually this same object or its parent).
   - **Canvas Group** → the Canvas Group on this object (or leave empty to use the one on the same GameObject).
   - **Advance Hint** → optional text or GameObject for “Space to continue”.
4. (Optional) For **per-word vertex effects**: select the **Dialogue Text** GameObject → add **TMP Effect Coordinator** and **TMP Effect Uploader** (so all effects share one rest cache and one final upload), then add **TMP Parabola Wave Effect**, **TMP Jitter Effect**, **TMP Zoom Effect**, and/or **TMP Rainbow Color Effect**. Set each effect’s **Link Tag** (e.g. `wave`, `jitter`, `pop`, `rainbow`) and in your dialogue lines wrap words like: `<link="jitter">shaky</link>`. Leave Link Tag empty to apply that effect to the whole line.

### Step 3: DialogueManager GameObject

1. Create an **empty GameObject** (e.g. **DialogueManager**).
2. **Add Component** → **Dialogue Manager**.
3. Assign **Dialogue UI** → the GameObject that has the **DialogueUI** component (your DialoguePanel).
4. **Game Canvas To Enable When Sequence Ends** (optional) → assign your main game canvas or UI root so it’s turned on when any dialogue sequence finishes (e.g. after init narrative; avoids a blank screen).
5. Optionally change **Advance Key** (default Space) or leave **Advance On Click Or South** checked for mouse/gamepad.

### Step 4: Create a Dialogue Sequence (ScriptableObject)

1. In the **Project** window: **Right-click** → **Create** → **Racing** → **Narrative** → **Dialogue Sequence**.
2. Name it (e.g. `Intro_Dialogue`).
3. In the Inspector:
   - **Lines**: set **Size** to how many lines you want. For each element:
     - **Speaker Name**: e.g. “Mechanic”, “Rival”.
     - **Text**: the line of dialogue.
     - **Portrait**: optional sprite.
     - **Delay Before Show**: optional delay before this line (seconds).
     - **Auto Advance** / **Auto Advance Seconds**: use for cutscene-style lines that advance automatically.
   - **Pause Game While Playing**: check to freeze time during dialogue (recommended for story moments).
   - **Set Story Flag On Complete**: optional string (e.g. `intro_done`) to set when the sequence finishes (for progression).

### Step 5: NarrativeDirector (Optional – for automatic triggers)

1. Create an **empty GameObject** (e.g. **NarrativeDirector**).
2. **Add Component** → **Narrative Director**.
3. In **Trigger Entries**, set **Size** to the number of “rules” you want (e.g. 2: intro + “after first run”).
4. For each entry:
   - **Sequence** → your DialogueSequenceSO (e.g. Intro_Dialogue).
   - **Condition**:
     - **Type**: e.g. **First Run Only** (runs == 0), **After First Run** (runs ≥ 1), **Run Count Equals**, **Has Story Flag**, etc.
     - **Run Count Value** / **Story Flag** as needed.
   - **Play Once**: check so it only plays once per condition.
   - **Flag When Played**: optional string so the system remembers it’s been played (e.g. `intro_shown`).

The director evaluates triggers in **Start()** and when **NotifyRunCompleted()** is called (already hooked in **GameManager_Racing** when a run completes). The **first matching** trigger runs; then you can call **CheckTriggers()** again later (e.g. when entering garage) if you add more trigger points.

### Step 6: When Does Dialogue Play?

- **With NarrativeDirector**:  
  - On **scene load**, **Start()** runs and checks triggers (e.g. **First Run Only** → play intro).  
  - When a run completes, **GameManager_Racing** calls **NarrativeDirector.NotifyRunCompleted()**; the director’s **CheckTriggers()** is called after that, so you can trigger “after first run” dialogue if you added it.
- **Without NarrativeDirector**: from any script, get **DialogueManager.Instance** and call **PlaySequence(yourDialogueSequenceSO)** when you want (e.g. button click, collision, timer).

---

## 4. Optional: Trigger Dialogue From Code

From any script:

```csharp
// Play a sequence once (e.g. from a button or event)
if (DialogueManager.Instance != null && myDialogueSequence != null)
    DialogueManager.Instance.PlaySequence(myDialogueSequence);
```

To play only once and set a flag:

```csharp
if (NarrativeDirector.Instance != null && !NarrativeDirector.HasStoryFlag("intro_done"))
    NarrativeDirector.Instance.PlayDialogueOnce(myIntroSequence, "intro_done");
```

---

## 5. Cutscenes (Light Approach)

- Use a **DialogueSequenceSO** with **Pause Game While Playing** checked and **Auto Advance** on lines for narration.
- For “cutscene” feel: use **Delay Before Show** and **Auto Advance** so lines advance without player input.
- Later you can add a dedicated **CutsceneController** that moves the camera, plays animations, or enables/disables objects while the same **DialogueManager** runs the dialogue.

---

## 6. File Locations

- Scripts: `Assets/Racing_Assets/Racing_Scripts/Narrative/`
- Create dialogue assets: **Create** → **Racing** → **Narrative** → **Dialogue Sequence**

---

## 7. Quick Checklist

- [ ] Canvas with DialoguePanel (TMP speaker + dialogue text, optional portrait).
- [ ] On the **dialogue body** TMP_Text: **Rich Text** enabled (Extra Settings) for bold/italic/color/size.
- [ ] **DialogueUI** on the panel, all references assigned; optionally enable **Use Typewriter Effect**.
- [ ] **DialogueManager** in scene with **Dialogue UI** reference.
- [ ] At least one **Dialogue Sequence** asset with lines.
- [ ] (Optional) **NarrativeDirector** with trigger entries for intro / after first run.
- [ ] **GameManager_Racing** already calls **NarrativeDirector.NotifyRunCompleted()** when a run completes.

You can start with a single intro sequence and one trigger; add more sequences and conditions as you expand the story.
