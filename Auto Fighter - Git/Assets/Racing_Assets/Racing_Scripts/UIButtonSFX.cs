using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

[DisallowMultipleComponent]
public class UIButtonSfx : MonoBehaviour, IPointerEnterHandler, IPointerClickHandler
{
    private Button _btn;

    void Awake()
    {
        _btn = GetComponent<Button>();
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        // Play hover for any button
        RacingUISoundManager.Instance?.PlayHover();
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        // Play select on click
        if(_btn.interactable)
            RacingUISoundManager.Instance?.PlaySelect();
        else 
            RacingUISoundManager.Instance?.PlayDeselect();
    }
}