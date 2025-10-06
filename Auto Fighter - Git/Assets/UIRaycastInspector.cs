using UnityEngine;
using UnityEngine.EventSystems;
using System.Collections.Generic;

public class UIRaycastInspector : MonoBehaviour
{
    void Update()
    {
        if (!EventSystem.current) return;

        var data = new PointerEventData(EventSystem.current) { position = Input.mousePosition };
        var results = new List<RaycastResult>();
        EventSystem.current.RaycastAll(data, results);

        if (results.Count == 0) return;

        System.Text.StringBuilder sb = new System.Text.StringBuilder("UI Raycast stack:\n");
        for (int i = 0; i < results.Count; i++)
        {
            var r = results[i];
            sb.AppendLine($"{i,2}. {GetPath(r.gameObject)} " +
                          $"(Canvas:{r.module?.transform?.name}  SortingLayer:{r.sortingLayer}  Order:{r.sortingOrder})");
        }
        Debug.Log($"pen {sb.ToString()}");
    }

    string GetPath(GameObject go)
    {
        var t = go.transform; var path = go.name;
        while (t.parent != null) { t = t.parent; path = t.name + "/" + path; }
        return path;
    }
}