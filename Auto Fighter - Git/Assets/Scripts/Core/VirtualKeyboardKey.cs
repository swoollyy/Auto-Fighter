using UnityEngine;
using TMPro;

namespace AutoFighter.Core
{
    /// <summary>
    /// Per-button helper for virtual keyboard keys.
    /// Attach to each key button and wire the button's OnClick to Trigger().
    /// </summary>
    public class VirtualKeyboardKey : MonoBehaviour
    {
        private enum KeyType
        {
            Character,
            Tab,
            Space,
            Backspace,
            Shift,
            CapsLock,
            Enter
        }

        private enum TabBehavior
        {
            None,
            InsertSpace,
            InsertTabCharacter
        }

        [SerializeField] private NameEntryPanelController nameEntryPanel;
        [SerializeField] private KeyType keyType = KeyType.Character;
        [SerializeField] private string characterValue = "a";
        [SerializeField] private string shiftedCharacterValue = "A";
        [SerializeField] private TabBehavior tabBehavior = TabBehavior.InsertSpace;
        [SerializeField] private TMP_Text keyLabel;

        private void Awake()
        {
            if (nameEntryPanel == null)
                nameEntryPanel = GetComponentInParent<NameEntryPanelController>();

            if (keyLabel == null)
                keyLabel = GetComponentInChildren<TMP_Text>();
        }

        private void OnEnable()
        {
            if (nameEntryPanel == null)
                nameEntryPanel = GetComponentInParent<NameEntryPanelController>();
            if (nameEntryPanel != null)
                nameEntryPanel.RegisterKey(this);
            RefreshLabel();
        }

        public void Trigger()
        {
            if (nameEntryPanel == null)
            {
                Debug.LogWarning("[VirtualKeyboardKey] Missing NameEntryPanelController reference.");
                return;
            }

            switch (keyType)
            {
                case KeyType.Character:
                    nameEntryPanel.AppendKey(GetResolvedCharacterValue());
                    break;
                case KeyType.Tab:
                    HandleTab();
                    break;
                case KeyType.Space:
                    nameEntryPanel.AddSpace();
                    break;
                case KeyType.Backspace:
                    nameEntryPanel.Backspace();
                    break;
                case KeyType.Shift:
                    nameEntryPanel.ToggleShift();
                    break;
                case KeyType.CapsLock:
                    nameEntryPanel.ToggleCapsLock();
                    break;
                case KeyType.Enter:
                    nameEntryPanel.Submit();
                    break;
            }
        }

        public void RefreshLabel()
        {
            if (keyLabel == null) return;

            if (keyType != KeyType.Character)
            {
                // Keep custom text for non-character keys (Shift, Enter, etc.).
                return;
            }

            keyLabel.text = GetResolvedCharacterValue();
        }

        private string GetResolvedCharacterValue()
        {
            if (nameEntryPanel == null) return characterValue;

            bool hasShiftVariant = !string.IsNullOrEmpty(shiftedCharacterValue);
            bool isShifted = nameEntryPanel.IsShiftOn;

            if (IsSingleLetter(characterValue))
            {
                bool uppercase = nameEntryPanel.IsLetterUppercase;
                return uppercase ? characterValue.ToUpperInvariant() : characterValue.ToLowerInvariant();
            }

            if (isShifted && hasShiftVariant) return shiftedCharacterValue;
            return characterValue;
        }

        private static bool IsSingleLetter(string value)
        {
            return !string.IsNullOrEmpty(value) && value.Length == 1 && char.IsLetter(value[0]);
        }

        private void HandleTab()
        {
            switch (tabBehavior)
            {
                case TabBehavior.InsertSpace:
                    nameEntryPanel.AddSpace();
                    break;
                case TabBehavior.InsertTabCharacter:
                    nameEntryPanel.AppendKey("\t");
                    break;
                case TabBehavior.None:
                default:
                    break;
            }
        }
    }
}
