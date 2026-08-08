using UnityEngine;
using TMPro;

/// <summary>
/// Single place that pushes the final mesh (vertices + colors) to the GPU.
/// Add this to the same GameObject as your TMP_Text and effect components.
/// Effects only modify textInfo.meshInfo in memory; they do not call UpdateGeometry.
/// This runs last (order 100) and does one UpdateGeometry per submesh so the final result is drawn.
/// </summary>
[DefaultExecutionOrder(100)]
[RequireComponent(typeof(TMP_Text))]
public class TMPEffectUploader : MonoBehaviour
{
    private TMP_Text _text;

    private void Awake()
    {
        _text = GetComponent<TMP_Text>();
    }

    private void LateUpdate()
    {
        if (_text == null) return;

        TMP_TextInfo textInfo = _text.textInfo;
        if (textInfo.meshInfo == null || textInfo.meshInfo.Length == 0) return;

        // Belt-and-suspenders: never upload stale capacity verts as visible glyphs.
        TMPEffectCoordinator.ClearUnusedMeshVertices(textInfo);

        for (int i = 0; i < textInfo.meshInfo.Length; i++)
        {
            TMP_MeshInfo mi = textInfo.meshInfo[i];
            if (mi.mesh == null) continue;

            mi.mesh.vertices = mi.vertices;
            mi.mesh.colors32 = mi.colors32;
            _text.UpdateGeometry(mi.mesh, i);
        }
    }
}
