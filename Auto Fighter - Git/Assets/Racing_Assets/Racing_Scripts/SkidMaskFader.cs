using UnityEngine;

public class SkidMaskFader : MonoBehaviour
{
    [SerializeField] private RenderTexture skidMask;
    [SerializeField] private Material fadeMaterial;  // uses Custom/SkidFade
    [SerializeField] private float fadeSpeed = 0.0f; // higher = faster fade

    private void Update()
    {
        if (skidMask == null || fadeMaterial == null) return;

        // convert speed → per-frame fade factor
        float fade = Mathf.Clamp01(1f - fadeSpeed * Time.deltaTime);
        fadeMaterial.SetFloat("_Fade", fade);

        RenderTexture temp = RenderTexture.GetTemporary(skidMask.width, skidMask.height, 0, skidMask.format);
        Graphics.Blit(skidMask, temp);
        Graphics.Blit(temp, skidMask, fadeMaterial);
        RenderTexture.ReleaseTemporary(temp);
    }
}
