﻿using System.Diagnostics;
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
        private Material _errorMaterial;
        
        private DrawRendererFlags _drawFlags;

        public MyPipeline (bool dynamicBatching, bool instancing) {
            if (dynamicBatching) {
                _drawFlags = DrawRendererFlags.EnableDynamicBatching;
            }
            if (instancing) {
                _drawFlags |= DrawRendererFlags.EnableInstancing;
            }
        }
        
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

#if UNITY_EDITOR
            if (camera.cameraType == CameraType.SceneView) {
                ScriptableRenderContext.EmitWorldGeometryForSceneView(camera);
            }
#endif
            
            CullResults.Cull(ref cullingParameters, context, ref _cull);

            context.SetupCameraProperties(camera);

            CameraClearFlags clearFlags = camera.clearFlags;
            
            
            _cameraBuffer.BeginSample("Render Camera");
            _cameraBuffer.ClearRenderTarget(
                (clearFlags & CameraClearFlags.Depth) != 0,
                (clearFlags & CameraClearFlags.Color) != 0,
                camera.backgroundColor
            );

            var drawSettings = new DrawRendererSettings(
                camera,
                new ShaderPassName("SRPDefaultUnlit")
            );
            drawSettings.flags = _drawFlags;
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

            DrawDefaultPipeline(context, camera);
            
            context.ExecuteCommandBuffer(_cameraBuffer);
            _cameraBuffer.Clear();
            _cameraBuffer.EndSample("Render Camera");
            
            context.Submit();
        }

        [Conditional("DEVELOPMENT_BUILD"), Conditional("UNITY_EDITOR")]
        private void DrawDefaultPipeline(ScriptableRenderContext context, Camera camera)
        {
            if (_errorMaterial == null) {
                Shader errorShader = Shader.Find("Hidden/InternalErrorShader");
                _errorMaterial = new Material(errorShader) {
                    hideFlags = HideFlags.HideAndDontSave
                };
            }
            
            var drawSettings = new DrawRendererSettings(
                camera, new ShaderPassName("ForwardBase")
            );
            drawSettings.SetShaderPassName(1, new ShaderPassName("PrepassBase"));
            drawSettings.SetShaderPassName(2, new ShaderPassName("Always"));
            drawSettings.SetShaderPassName(3, new ShaderPassName("Vertex"));
            drawSettings.SetShaderPassName(4, new ShaderPassName("VertexLMRGBM"));
            drawSettings.SetShaderPassName(5, new ShaderPassName("VertexLM"));
            drawSettings.SetOverrideMaterial(_errorMaterial, 0);
		
            var filterSettings = new FilterRenderersSettings(true);
		
            context.DrawRenderers(
                _cull.visibleRenderers, ref drawSettings, filterSettings
            );
        }
    }
}