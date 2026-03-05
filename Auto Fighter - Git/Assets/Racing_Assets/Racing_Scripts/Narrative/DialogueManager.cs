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
    [Tooltip("Optional: enable this (e.g. main game canvas) when any dialogue sequence finishes. Use so the screen isn't blank after init narrative.")]
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

    /// <summary>True while a sequence is being played.</summary>
    public bool IsPlaying => _isPlaying;

    /// <summary>Fired when a sequence finishes (with the sequence that finished).</summary>
    public event Action<DialogueSequenceSO> OnSequenceCompleted;

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

        _currentSequence = sequence;
        _currentLineIndex = 0;
        _isPlaying = true;

        if (sequence.pauseGameWhilePlaying)
        {
            _savedTimeScale = Time.timeScale;
            Time.timeScale = 0f;
        }

        dialogueUI?.Show();
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

            dialogueUI?.SetLine(line.speakerName, line.text, line.portrait);

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

        if (gameCanvasToEnableWhenSequenceEnds != null)
            gameCanvasToEnableWhenSequenceEnds.SetActive(true);

        // After run complete, narrative often plays; when it ends, ensure we return to skill tree so the screen isn't blank.
        if (GameManager_Racing.Instance != null)
            GameManager_Racing.Instance.ReturnToSkillTree();

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
    }
}
