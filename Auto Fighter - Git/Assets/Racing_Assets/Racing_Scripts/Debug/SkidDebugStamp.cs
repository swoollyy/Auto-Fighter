using UnityEngine;

public class SkidDebugStamp : MonoBehaviour
{
    public RenderTexture skidMask;   // RT_SkidMask
    public Material drawMaterial;    // M_SkidDraw
    [Range(0f, 1f)] public float u = 0.5f;
    [Range(0f, 1f)] public float v = 0.5f;
    public float radius = 0.1f;

    private bool done = false;

    private void Update()
    {
        // Just do it once after everything is initialized
        if (done || skidMask == null || drawMaterial == null) return;

        done = true;
        StampAtUV(new Vector2(u, v));
    }

    private void StampAtUV(Vector2 uv)
    {
        drawMaterial.SetVector("_Center", new Vector4(uv.x, uv.y, 0, 0));
        drawMaterial.SetFloat("_Radius", radius);

        RenderTexture active = RenderTexture.active;
        RenderTexture.active = skidMask;

        GL.PushMatrix();
        GL.LoadOrtho();
        drawMaterial.SetPass(0);

        GL.Begin(GL.QUADS);
        GL.TexCoord2(0, 0); GL.Vertex3(0, 0, 0);
        GL.TexCoord2(1, 0); GL.Vertex3(1, 0, 0);
        GL.TexCoord2(1, 1); GL.Vertex3(1, 1, 0);
        GL.TexCoord2(0, 1); GL.Vertex3(0, 1, 0);
        GL.End();

        GL.PopMatrix();
        RenderTexture.active = active;
    }
}