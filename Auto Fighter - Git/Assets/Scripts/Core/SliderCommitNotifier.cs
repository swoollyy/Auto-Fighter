using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace AutoFighter.Core
{
    /// <summary>
    /// Emits a commit callback when a slider interaction finishes
    /// (pointer up / end drag / submit).
    /// </summary>
    [RequireComponent(typeof(Slider))]
    public class SliderCommitNotifier : MonoBehaviour, IPointerUpHandler, IEndDragHandler, ISubmitHandler
    {
        public event Action<float> Committed;

        private Slider _slider;

        private void Awake()
        {
            _slider = GetComponent<Slider>();
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            Commit();
        }

        public void OnEndDrag(PointerEventData eventData)
        {
            Commit();
        }

        public void OnSubmit(BaseEventData eventData)
        {
            Commit();
        }

        private void Commit()
        {
            if (_slider == null) _slider = GetComponent<Slider>();
            if (_slider == null) return;
            Committed?.Invoke(_slider.value);
        }
    }
}
