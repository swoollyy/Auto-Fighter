using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace AutoFighter.Core
{
    /// <summary>
    /// Reusable name-entry panel controller intended for controller-friendly
    /// virtual keyboards. Wire keyboard buttons to <see cref="AppendKey"/>,
    /// <see cref="Backspace"/>, <see cref="ToggleCapsLock"/>, and
    /// <see cref="Submit"/>.
    /// </summary>
    public class NameEntryPanelController : MonoBehaviour
    {
        [Header("UI")]
        [SerializeField] private TMP_Text namePreviewText;
        [SerializeField] private TMP_Text capsLockIndicatorText;
        [SerializeField] private Button submitButton;
        [SerializeField] private string emptyPreviewPlaceholder = "_";

        [Header("Validation")]
        [SerializeField] private int maxNameLength = 16;
        [SerializeField] private bool trimOnSubmit = true;

        private string _currentName = string.Empty;
        private bool _isCapsLockOn;
        private bool _isShiftOn;
        private bool _isExternalShiftHeld;
        private readonly List<VirtualKeyboardKey> _registeredKeys = new List<VirtualKeyboardKey>();

        public event Action<string> Submitted;

        public string CurrentName => _currentName;
        public bool IsCapsLockOn => _isCapsLockOn;
        public bool IsShiftOn => _isShiftOn || _isExternalShiftHeld;
        public bool IsLetterUppercase => _isCapsLockOn ^ IsShiftOn;

        private void OnEnable()
        {
            if (SaveSystem.Current == null) SaveSystem.Load();
            if (string.IsNullOrEmpty(_currentName) && SaveSystem.Current != null)
                _currentName = SaveSystem.Current.playerName ?? string.Empty;

            RefreshUI();
        }

        public void AppendKey(string keyValue)
        {
            if (string.IsNullOrEmpty(keyValue)) return;
            if (_currentName.Length >= maxNameLength) return;

            _currentName += keyValue;

            // Shift is momentary: consume it after typing one character.
            if (_isShiftOn) _isShiftOn = false;
            RefreshUI();
        }

        public void AddSpace()
        {
            AppendKey(" ");
        }

        public void Backspace()
        {
            if (string.IsNullOrEmpty(_currentName)) return;
            _currentName = _currentName.Substring(0, _currentName.Length - 1);
            RefreshUI();
        }

        public void ToggleCapsLock()
        {
            _isCapsLockOn = !_isCapsLockOn;
            RefreshUI();
        }

        public void ToggleShift()
        {
            _isShiftOn = !_isShiftOn;
            RefreshUI();
        }

        public void ArmOneShotShift()
        {
            _isShiftOn = !_isShiftOn;
            RefreshUI();
        }

        public void SetExternalShiftHeld(bool held)
        {
            if (_isExternalShiftHeld == held) return;
            _isExternalShiftHeld = held;
            RefreshUI();
        }

        public void RegisterKey(VirtualKeyboardKey key)
        {
            if (key == null || _registeredKeys.Contains(key)) return;
            _registeredKeys.Add(key);
            key.RefreshLabel();
        }

        public void SetName(string value)
        {
            _currentName = string.IsNullOrEmpty(value) ? string.Empty : value;
            if (_currentName.Length > maxNameLength)
                _currentName = _currentName.Substring(0, maxNameLength);
            RefreshUI();
        }

        public void Submit()
        {
            string finalName = trimOnSubmit ? _currentName.Trim() : _currentName;
            if (string.IsNullOrEmpty(finalName)) return;

            if (SaveSystem.Current == null) SaveSystem.Load();
            SaveSystem.Current.playerName = finalName;
            SaveSystem.Save();

            Submitted?.Invoke(finalName);
        }

        private void RefreshUI()
        {
            if (namePreviewText != null)
                namePreviewText.text = string.IsNullOrEmpty(_currentName) ? emptyPreviewPlaceholder : _currentName;

            if (capsLockIndicatorText != null)
            {
                string capsText = _isCapsLockOn ? "CAPS: ON" : "caps: off";
                string shiftText = IsShiftOn ? " SHIFT: ON" : string.Empty;
                capsLockIndicatorText.text = capsText + shiftText;
            }

            if (submitButton != null)
            {
                string nameToValidate = trimOnSubmit ? _currentName.Trim() : _currentName;
                submitButton.interactable = !string.IsNullOrEmpty(nameToValidate);
            }

            for (int i = 0; i < _registeredKeys.Count; i++)
            {
                if (_registeredKeys[i] != null) _registeredKeys[i].RefreshLabel();
            }
        }
    }
}
