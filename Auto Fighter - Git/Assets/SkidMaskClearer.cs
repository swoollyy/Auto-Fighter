using UnityEngine;

public class SkidMaskClearer : MonoBehaviour
{
    [SerializeField] private RenderTexture skidMask;

    private void Awake()
    {
        if (skidMask == null) return;

        var active = RenderTexture.active;
        RenderTexture.active = skidMask;

        // Clear to black (no skids)
        GL.Clear(false, true, Color.black);

        RenderTexture.active = active;
    }
}
