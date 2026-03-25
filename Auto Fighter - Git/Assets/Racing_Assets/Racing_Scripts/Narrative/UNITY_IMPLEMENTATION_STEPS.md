# Narrative Dialogue – Unity Implementation (in order)

Do these steps in your **racing scene** (e.g. **Racer_Incremental**). Save the scene when done.

---

## Step 1: Open the scene

1. **File → Open Scene** (or double‑click): `Assets/Scenes/Racer_Incremental.unity`.
2. Save after each section below if you want to checkpoint.

---

## Step 2: Create the dialogue UI

1. In the **Hierarchy**, **right‑click** → **UI** → **Canvas** (if you don’t already have a Canvas for dialogue).
   - If Unity asks to create an **Event System**, say **Yes**.
2. Under the Canvas, **right‑click** → **Create Empty**. Rename to **DialoguePanel**.
3. With **DialoguePanel** selected: **Add Component** → **Canvas Group**.
4. **Right‑click DialoguePanel** → **UI** → **Panel** (adds a background). Resize/position it where you want the dialogue box (e.g. bottom or center).
5. Under **DialoguePanel**, create the text fields:
   - **Right‑click DialoguePanel** → **UI** → **Text - TextMeshPro**. Name it **SpeakerName**. Position at the top of the panel; set placeholder text like "Speaker".
   - **Right‑click DialoguePanel** → **UI** → **Text - TextMeshPro**. Name it **DialogueText**. Position below the speaker; make it larger, multi‑line; set placeholder "Dialogue text here."
6. Select **DialogueText** (the TMP_Text for the body). In the Inspector, open **TextMeshPro - Text (UI)** → **Extra Settings** and ensure **Rich Text** is **checked**.
7. (Optional) **Right‑click DialoguePanel** → **UI** → **Image**, name it **Portrait**. (Optional) Add another TextMeshPro for "Space to continue" as advance hint.
8. In the Hierarchy, **uncheck** the **DialoguePanel** GameObject so it starts inactive (the script will show it when dialogue plays).

---

## Step 3: Wire DialogueUI

1. Select **DialoguePanel**.
2. **Add Component** → search **Dialogue UI** (script: `Assets/Racing_Assets/Racing_Scripts/Narrative/DialogueUI.cs`).
3. In the Inspector, assign:
   - **Speaker Text** → drag **SpeakerName** (the TMP_Text for the speaker).
   - **Dialogue Text** → drag **DialogueText** (the TMP_Text for the body).
   - **Panel Root** → drag **DialoguePanel** (or leave empty to use this object).
   - **Canvas Group** → drag the **Canvas Group** on DialoguePanel (or leave empty to use the one on this object).
   - **Portrait Image** → drag **Portrait** if you created it.
   - **Advance Hint** → optional: the "Space to continue" text or GameObject.
4. Optionally enable **Use Typewriter Effect** and set **Typewriter Chars Per Second** (e.g. 60).

---

## Step 4: Add DialogueManager

1. In the Hierarchy, **right‑click** → **Create Empty**. Rename to **DialogueManager**.
2. **Add Component** → **Dialogue Manager**.
3. Assign **Dialogue UI** → drag the **DialoguePanel** GameObject (the one with the **DialogueUI** component).
4. Leave **Advance Key** as Space and **Advance On Click Or South** checked unless you want to change them.

---

## Step 5: Create a dialogue sequence (ScriptableObject)

1. In the **Project** window, go to a folder for narrative assets (e.g. `Assets/Racing_Assets/` or create `Assets/Racing_Assets/Narrative/`).
2. **Right‑click** in the folder → **Create** → **Racing** → **Narrative** → **Dialogue Sequence**.
3. Name it (e.g. **Intro_Dialogue**).
4. Select it and in the Inspector:
   - **Lines** → set **Size** to **2** (or more).
   - **Element 0**: Speaker Name = `Mechanic`, Text = `Hey. First run? Just drive and don't crash.`
   - **Element 1**: Speaker Name = `Mechanic`, Text = `Press Space when you're done reading.`
   - **Pause Game While Playing**: **checked**.
   - **Set Story Flag On Complete**: e.g. `intro_done` (optional).

---

## Step 6: (Optional) Auto‑play intro with NarrativeDirector

1. In the Hierarchy, **right‑click** → **Create Empty**. Rename to **NarrativeDirector**.
2. **Add Component** → **Narrative Director**.
3. **Trigger Entries** → **Size** = **1**.
4. **Element 0**:
   - **Sequence** → drag your **Intro_Dialogue** asset.
   - **Condition** → **Type** = **First Run Only**.
   - **Play Once** = checked.
   - **Flag When Played** = e.g. `intro_shown`.
5. When you run the game, the intro dialogue should play once at the start (game pauses, dialogue shows; press Space to advance).

---

## Step 7: (Optional) Per‑word effects on dialogue text

1. Select the **DialogueText** GameObject (the TMP_Text used for the dialogue body).
2. **Add Component** → **TMP Parabola Wave Effect** (and/or **TMP Jitter Effect**, **TMP Zoom Effect**).
3. Set each component’s **Link Tag** (e.g. `wave`, `jitter`, `pop`). Leave empty to affect the whole line.
4. In your Dialogue Sequence, in the **Text** of a line, wrap words like:  
   `This is <link="jitter">shaky</link> and <link="wave">smooth</link>.`  
   Only those words will get the effect; the rest stays normal.

---

## Step 8: Test

1. **Press Play**.
2. If you added **NarrativeDirector** with **First Run Only** and **Intro_Dialogue**, you should see the dialogue panel and the first line. Game time should be paused.
3. Press **Space** (or click) to advance. After the last line, the panel should hide and time resume.
4. Stop Play, **save the scene** (Ctrl+S).

---

## Troubleshooting

- **Dialogue never appears**  
  - Ensure **DialogueManager** is in the scene and **Dialogue UI** is assigned to DialoguePanel.  
  - If using NarrativeDirector, ensure **Trigger Entries** has one entry with a **Sequence** assigned and **First Run Only** (or the condition you want).

- **"Create → Racing → Narrative" missing**  
  - Confirm the scripts **DialogueSequenceSO.cs** and **DialogueLineSO.cs** are under `Assets/Racing_Assets/Racing_Scripts/Narrative/` and that Unity has finished compiling (no red errors in the Console).

- **Rich text / link tags not working**  
  - On the **DialogueText** TMP_Text, **Extra Settings** → **Rich Text** must be checked.  
  - Link syntax in the string: `<link="wave">word</link>` (quotes around the id).

- **Effects on wrong words**  
  - Each effect component’s **Link Tag** must match the id in the text (e.g. `wave` for `<link="wave">`).

For full details and options, see **NARRATIVE_DIALOGUE_README.md** in the same folder.
