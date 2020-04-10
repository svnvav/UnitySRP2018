using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace Svnvav.SRP2018
{
    public class MyPipeline : RenderPipeline
    {
        private CullResults _cull;
        private CommandBuffer _cameraBuffer = new CommandBuffer {
            name = "Render Camera"
        };

        
        public override void Render(
            ScriptableRenderContext context, Camera[] cameras
        )
        {
            base.Render(context, cameras);
            foreach (var camera in cameras)
            {
                Render(context, camera);
            }
        }

        private void Render(ScriptableRenderContext context, Camera camera)
        {
            ScriptableCullingParameters cullingParameters;
            if (!CullResults.GetCullingParameters(camera, out cullingParameters))
            {
                return;
            }

            CullResults.Cull(ref cullingParameters, context, ref _cull);

            context.SetupCameraProperties(camera);

            CameraClearFlags clearFlags = camera.clearFlags;
            _cameraBuffer.ClearRenderTarget(
                (clearFlags & CameraClearFlags.Depth) != 0,
                (clearFlags & CameraClearFlags.Color) != 0,
                camera.backgroundColor
            );
            context.ExecuteCommandBuffer(_cameraBuffer);
            _cameraBuffer.Release();

            var drawSettings = new DrawRendererSettings(
                camera,
                new ShaderPassName("SRPDefaultUnlit")
            );
            drawSettings.sorting.flags = SortFlags.CommonOpaque;

            var filterSettings = new FilterRenderersSettings(true)
            {
                renderQueueRange = RenderQueueRange.opaque
            };

            context.DrawRenderers(
                _cull.visibleRenderers, ref drawSettings, filterSettings
            );

            context.DrawSkybox(camera);
            
            drawSettings.sorting.flags = SortFlags.CommonTransparent;
            filterSettings.renderQueueRange = RenderQueueRange.transparent;
            context.DrawRenderers(
                _cull.visibleRenderers, ref drawSettings, filterSettings
            );

            context.Submit();
        }
    }
}