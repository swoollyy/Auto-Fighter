using UnityEngine;

public class TireMarkPainter : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Transform[] wheelPoints;
    [SerializeField] private LayerMask roadLayer;
    [SerializeField] private RenderTexture skidMask;
    [SerializeField] private Material drawMaterial;

    [Header("Settings")]
    [SerializeField] private float brushSize = 0.02f;
    [SerializeField] private float rayLength = 5f;
    [SerializeField] private float minDistance = 0.002f;

    private Vector2[] lastUVs;

    private void Awake()
    {
        lastUVs = new Vector2[wheelPoints.Length];
        for (int i = 0; i < lastUVs.Length; i++)
            lastUVs[i] = new Vector2(-1f, -1f); // invalid
    }

    private void LateUpdate()
    {
        if (wheelPoints == null || skidMask == null || drawMaterial == null)
            return;

        for (int i = 0; i < wheelPoints.Length; i++)
        {
            Transform wheel = wheelPoints[i];
            if (wheel == null) continue;

            Vector3 pos = wheel.position;
            Vector3 dir = Vector3.down;

            if (Physics.Raycast(pos, dir, out RaycastHit hit, rayLength, roadLayer))
            {
                // Use UV2 from the road mesh
                Vector2 uv = hit.textureCoord2;

                // Optional: tiny spacing filter so we don't overdraw
                if (lastUVs[i].x >= 0f && Vector2.Distance(lastUVs[i], uv) < minDistance)
                    continue;

                StampAtUV(uv);
                lastUVs[i] = uv;
            }
        }
    }

    private void StampAtUV(Vector2 uv)
    {
        drawMaterial.SetVector("_Center", new Vector4(uv.x, uv.y, 0, 0));
        drawMaterial.SetFloat("_Radius", brushSize);

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
