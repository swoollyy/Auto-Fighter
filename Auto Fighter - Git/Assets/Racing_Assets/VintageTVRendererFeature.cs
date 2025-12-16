using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public class VintageTVRendererFeature : ScriptableRendererFeature
{
    [System.Serializable]
    public class Settings
    {
        public RenderPassEvent passEvent = RenderPassEvent.AfterRenderingPostProcessing;
        public Material material;
    }

    public Settings settings = new Settings();

    class Pass : ScriptableRenderPass
    {
        private Material _mat;
        private RTHandle _temp;
        private static readonly ProfilingSampler _prof = new ProfilingSampler("VintageTV Pass");

        public void SetMaterial(Material mat) => _mat = mat;

        public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
        {
            if (_mat == null) return;

            var camType = renderingData.cameraData.cameraType;
            if (camType == CameraType.Preview || camType == CameraType.Reflection)
                return;

            var desc = renderingData.cameraData.cameraTargetDescriptor;
            desc.depthBufferBits = 0;
            RenderingUtils.ReAllocateIfNeeded(ref _temp, desc, name: "_VintageTV_Temp");
        }

        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            if (_mat == null) return;

            var camType = renderingData.cameraData.cameraType;
            if (camType == CameraType.Preview || camType == CameraType.Reflection)
                return;

            var colorTarget = renderingData.cameraData.renderer.cameraColorTargetHandle;
            if (colorTarget == null || _temp == null) return;

            var cmd = CommandBufferPool.Get();
            using (new ProfilingScope(cmd, _prof))
            {
                Blitter.BlitCameraTexture(cmd, colorTarget, _temp);
                Blitter.BlitCameraTexture(cmd, _temp, colorTarget, _mat, 0);
            }

            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }
    }

    private Pass _pass;

    public override void Create()
    {
        _pass = new Pass();
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        if (settings.material == null) return;

        _pass.renderPassEvent = settings.passEvent;
        _pass.SetMaterial(settings.material); // <- critical: refresh the material reference
        renderer.EnqueuePass(_pass);
    }
}
