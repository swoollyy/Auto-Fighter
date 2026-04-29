using System;
using System.Collections;
using UnityEngine;

/// <summary>
/// Plays dialogue sequences: shows lines one by one, advances on input or auto-advance.
/// Add to a GameObject in your scene and assign the DialogueUI reference.
/// Optional: hook up NarrativeDirector to trigger sequences by story progression.
/// </summary>
public class DialogueManager : MonoBehaviour
{
    public static DialogueManager Instance { get; private set; }

    [Header("UI Reference")]
    [SerializeField] private DialogueUI dialogueUI;
    [Tooltip("Optional: when set, game/dialogue canvas show-hide is driven through here (recommended). Otherwise use Game Canvas To Enable When Sequence Ends below.")]
    [SerializeField] private UIManager_Racing uiManagerRacing;
    [Tooltip("Fallback: enable this when a sequence finishes if UIManager Racing is not set. Use so the screen isn't blank after init narrative.")]
    [SerializeField] private GameObject gameCanvasToEnableWhenSequenceEnds;

    [Header("Input")]
    [Tooltip("Key to advance to next line (when not auto-advancing).")]
    [SerializeField] private KeyCode advanceKey = KeyCode.Space;
    [Tooltip("Also advance on any mouse button or gamepad South (A/Cross).")]
    [SerializeField] private bool advanceOnClickOrSouth = true;

    [Header("Time")]
    [Tooltip("Default time scale when no dialogue is playing (restored when sequence ends).")]
    [SerializeField] private float normalTimeScale = 1f;

    private DialogueSequenceSO _currentSequence;
    private int _currentLineIndex;
    private float _savedTimeScale = 1f;
    private Coroutine _playRoutine;
    private bool _isPlaying;
    private bool _hidGameCanvasForCurrentSequence;

    /// <summary>True while a sequence is being played.</summary>
    public bool IsPlaying => _isPlaying;

    /// <summary>Fired when a sequence finishes (with the sequence that finished).</summary>
    public event Action<DialogueSequenceSO> OnSequenceCompleted;
    /// <summary>Fired when a sequence begins (with the sequence that started).</summary>
    public event Action<DialogueSequenceSO> OnSequenceStarted;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    private void Update()
    {
        if (!_isPlaying || _currentSequence == null || dialogueUI == null)
            return;

        DialogueLineData line = GetCurrentLine();
        if (line.autoAdvance)
            return; // advance is handled by coroutine
        if (dialogueUI != null && !dialogueUI.CanAcceptAdvanceInput)
            return;

        if (Input.GetKeyDown(advanceKey) || (advanceOnClickOrSouth && (Input.GetMouseButtonDown(0) || Input.GetMouseButtonDown(1) || IsSouthButtonDown())))
        {
            if (dialogueUI != null && !dialogueUI.IsTypewriterComplete)
                dialogueUI.SkipTypewriter();
            else
                Advance();
        }
    }

    private static bool IsSouthButtonDown()
    {
        if (RacingInputReader.Instance != null && RacingInputReader.Instance.AnyMashDown)
            return true;
        return Input.GetKeyDown(KeyCode.JoystickButton0) || Input.GetKeyDown(KeyCode.JoystickButton1);
    }

    /// <summary>
    /// Start playing a dialogue sequence. If one is already playing, it is stopped and replaced.
    /// </summary>
    public void PlaySequence(DialogueSequenceSO sequence)
    {
        if (sequence == null || !sequence.HasLines)
        {
            if (sequence != null)
                NotifyComplete(sequence);
            return;
        }

        if (_playRoutine != null)
            StopCoroutine(_playRoutine);

        // Clear any prior lock before applying visibility / lock for this sequence.
        uiManagerRacing?.SetGameplayCanvasInputLocked(false);

        _currentSequence = sequence;
        _currentLineIndex = 0;
        _isPlaying = true;
        _hidGameCanvasForCurrentSequence = false;
        OnSequenceStarted?.Invoke(sequence);

        if (sequence.pauseGameWhilePlaying)
        {
            _savedTimeScale = Time.timeScale;
            Time.timeScale = 0f;
        }

        dialogueUI?.Show();

        // Per-sequence option: keep game canvas visible for skill-tree/tutorial dialogue.
        if (!sequence.keepGameCanvasVisibleWhilePlaying)
        {
            _hidGameCanvasForCurrentSequence = true;
            if (uiManagerRacing != null)
                uiManagerRacing.SetGameCanvasVisible(false);
            else if (gameCanvasToEnableWhenSequenceEnds != null)
                gameCanvasToEnableWhenSequenceEnds.SetActive(false);
        }
        else if (uiManagerRacing != null)
        {
            // Skill tree / HUD visible: block buttons & controller Submit; mouse still moves; dialogue handles advance.
            uiManagerRacing.SetGameplayCanvasInputLocked(true);
        }

        _playRoutine = StartCoroutine(PlaySequenceRoutine());
    }

    private IEnumerator PlaySequenceRoutine()
    {
        while (_currentLineIndex < _currentSequence.LineCount)
        {
            DialogueLineData line = GetCurrentLine();
            if (line.delayBeforeShow > 0f)
            {
                float t = 0f;
                while (t < line.delayBeforeShow)
                {
                    t += Time.unscaledDeltaTime;
                    yield return null;
                }
            }

            string resolvedSpeaker = NarrativeTokens.Resolve(line.speakerName);
            string resolvedText = NarrativeTokens.Resolve(line.text);
            dialogueUI?.SetLine(resolvedSpeaker, resolvedText, line.portrait);

            if (line.autoAdvance)
            {
                float wait = line.autoAdvanceSeconds;
                float t = 0f;
                while (t < wait)
                {
                    t += Time.unscaledDeltaTime;
                    yield return null;
                }
                Advance();
            }
            else
            {
                int lineWeAreOn = _currentLineIndex;
                yield return null;
                while (_isPlaying && _currentLineIndex == lineWeAreOn)
                    yield return null;
            }
        }

        EndSequence();
    }

    private DialogueLineData GetCurrentLine()
    {
        if (_currentSequence == null || _currentLineIndex < 0 || _currentLineIndex >= _currentSequence.lines.Length)
            return default;
        return _currentSequence.lines[_currentLineIndex];
    }

    /// <summary>Advance to the next line or end the sequence.</summary>
    public void Advance()
    {
        if (!_isPlaying || _currentSequence == null)
            return;

        _currentLineIndex++;
        if (_currentLineIndex >= _currentSequence.LineCount)
            EndSequence();
    }

    private void EndSequence()
    {
        _isPlaying = false;
        if (_playRoutine != null)
        {
            StopCoroutine(_playRoutine);
            _playRoutine = null;
        }

        if (_currentSequence != null && _currentSequence.pauseGameWhilePlaying)
            Time.timeScale = _savedTimeScale;

        dialogueUI?.Hide();

        DialogueSequenceSO completed = _currentSequence;
        _currentSequence = null;
        _currentLineIndex = 0;

        if (!string.IsNullOrEmpty(completed.setStoryFlagOnComplete))
            NarrativeDirector.SetStoryFlag(completed.setStoryFlagOnComplete);

        // Restore game canvas only if this sequence hid it.
        if (_hidGameCanvasForCurrentSequence)
        {
            if (uiManagerRacing != null)
                uiManagerRacing.SetGameCanvasVisible(true);
            else if (gameCanvasToEnableWhenSequenceEnds != null)
                gameCanvasToEnableWhenSequenceEnds.SetActive(true);
        }
        _hidGameCanvasForCurrentSequence = false;

        uiManagerRacing?.SetGameplayCanvasInputLocked(false);

        OnSequenceCompleted?.Invoke(completed);
    }

    private void NotifyComplete(DialogueSequenceSO sequence)
    {
        OnSequenceCompleted?.Invoke(sequence);
    }

    /// <summary>Stop current dialogue and restore time scale without firing OnSequenceCompleted.</summary>
    public void ForceStop()
    {
        if (!_isPlaying)
            return;
        if (_currentSequence != null && _currentSequence.pauseGameWhilePlaying)
            Time.timeScale = _savedTimeScale;
        _isPlaying = false;
        _currentSequence = null;
        _currentLineIndex = 0;
        if (_playRoutine != null)
        {
            StopCoroutine(_playRoutine);
            _playRoutine = null;
        }
        dialogueUI?.Hide();

        if (_hidGameCanvasForCurrentSequence)
        {
            if (uiManagerRacing != null)
                uiManagerRacing.SetGameCanvasVisible(true);
            else if (gameCanvasToEnableWhenSequenceEnds != null)
                gameCanvasToEnableWhenSequenceEnds.SetActive(true);
        }
        _hidGameCanvasForCurrentSequence = false;

        uiManagerRacing?.SetGameplayCanvasInputLocked(false);
    }
}
