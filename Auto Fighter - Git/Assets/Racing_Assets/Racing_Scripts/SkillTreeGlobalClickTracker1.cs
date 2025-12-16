using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using System.Collections.Generic;

/// <summary>
/// Installed on the skill tree root (visual container). Does not block raycasts.
/// Listens for pointer clicks (via Input) and performs a GraphicRaycaster query to determine
/// what UI element was clicked. If the click is outside the assigned RacingSkillDetailPanel.infoContainer
/// and not on another interactive Button, the panel will be hidden.
/// This allows clicking other skill buttons to work without double-clicking while still enabling
/// "click anywhere outside the info card to close" behaviour.
/// </summary>
[DisallowMultipleComponent]
public class SkillTreeGlobalClickCatcher : MonoBehaviour
{
    private GraphicRaycaster _raycaster;
    private EventSystem _eventSystem;
    private RacingSkillDetailPanel _panel;
    private Canvas _canvas;

    public void Init(RacingSkillDetailPanel panel)
    {
        _panel = panel;
        _canvas = GetComponentInParent<Canvas>();
        _raycaster = _canvas ? _canvas.GetComponent<GraphicRaycaster>() : null;
        if (_raycaster == null && _canvas != null)
        {
            _raycaster = _canvas.gameObject.AddComponent<GraphicRaycaster>();
        }
        _eventSystem = EventSystem.current;
    }

    void Update()
    {
        if (_panel == null || !_panel.IsInfoVisible) return;
        if (_eventSystem == null || _raycaster == null) return;

        if (Input.GetMouseButtonDown(0))
        {
            var pointerData = new PointerEventData(_eventSystem) { position = Input.mousePosition };
            List<RaycastResult> results = new List<RaycastResult>();
            _raycaster.Raycast(pointerData, results);

            // If nothing hit, treat as outside click -> hide
            if (results.Count == 0)
            {
                _panel.HideInfo();
                return;
            }

            // If any result is inside the info container, do not hide
            foreach (var r in results)
            {
                if (r.gameObject == null) continue;
                if (IsChildOf(r.gameObject, _panel.gameObject)) // click on panel's root (skill tree UI) -> do not hide
                {
                    // But still check if inside the infoContainer specifically
                    // If clicked inside the infoContainer -> do not hide
                    if (_panel.InfoContainer != null && IsChildOf(r.gameObject, _panel.InfoContainer))
                        return;
                }

                // If clicked on any Button (likely another skill), do not hide (let the button handle itself)
                if (r.gameObject.GetComponent<Button>() != null)
                    return;

                // If clicked on other selectable UI (e.g., input fields), don't hide
                if (r.gameObject.GetComponent<Selectable>() != null)
                    return;
            }

            // No protective reason to keep it visible => hide
            _panel.HideInfo();
        }
    }

    private bool IsChildOf(GameObject child, GameObject parent)
    {
        if (child == null || parent == null) return false;
        Transform t = child.transform;
        while (t != null)
        {
            if (t.gameObject == parent) return true;
            t = t.parent;
        }
        return false;
    }
}