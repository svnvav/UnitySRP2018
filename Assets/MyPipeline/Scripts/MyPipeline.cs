using System.Diagnostics;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace Svnvav.SRP2018
{
    public class MyPipeline : RenderPipeline
    {
        private const int MaxVisibleLights = 16;

        private static int VisibleLightColorsId = Shader.PropertyToID("_VisibleLightColors");
        private static int VisibleLightDirectionsOrPositionsId = Shader.PropertyToID("_VisibleLightDirectionsOrPositions");
        private static int VisibleLightSpotDirectionsId = Shader.PropertyToID("_VisibleLightSpotDirections");
        private static int VisibleLightAttenuationId = Shader.PropertyToID("_VisibleLightAttenuation");
        private static int LightIndicesOffsetAndCountID = Shader.PropertyToID("unity_LightIndicesOffsetAndCount");
        
        private Vector4[] _visibleLightColors = new Vector4[MaxVisibleLights];
        private Vector4[] _visibleLightDirectionsOrPositions = new Vector4[MaxVisibleLights];
        private Vector4[] _visibleLightSpotDirections = new Vector4[MaxVisibleLights];
        private Vector4[] _visibleLightAttenuation = new Vector4[MaxVisibleLights];

        private CullResults _cull;
        private CommandBuffer _cameraBuffer = new CommandBuffer {
            name = "Render Camera"
        };
        private Material _errorMaterial;
        
        private DrawRendererFlags _drawFlags;

        public MyPipeline (bool dynamicBatching, bool instancing)
        {
            GraphicsSettings.lightsUseLinearIntensity = true;
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

            _cameraBuffer.ClearRenderTarget(
                (clearFlags & CameraClearFlags.Depth) != 0,
                (clearFlags & CameraClearFlags.Color) != 0,
                camera.backgroundColor
            );

            if (_cull.visibleLights.Count > 0)
            {
                ConfigureLights();
            }
            else
            {
                _cameraBuffer.SetGlobalVector(LightIndicesOffsetAndCountID, Vector4.zero);
            }

            _cameraBuffer.BeginSample("Render Camera");
            
            _cameraBuffer.SetGlobalVectorArray(VisibleLightColorsId, _visibleLightColors);
            _cameraBuffer.SetGlobalVectorArray(VisibleLightDirectionsOrPositionsId, _visibleLightDirectionsOrPositions);
            _cameraBuffer.SetGlobalVectorArray(VisibleLightSpotDirectionsId, _visibleLightSpotDirections);
            _cameraBuffer.SetGlobalVectorArray(VisibleLightAttenuationId, _visibleLightAttenuation);

            var drawSettings = new DrawRendererSettings(
                camera,
                new ShaderPassName("SRPDefaultUnlit")
            )
            {
                flags = _drawFlags,
            };

            if (_cull.visibleLights.Count > 0)
            {
                drawSettings.rendererConfiguration = RendererConfiguration.PerObjectLightIndices8;
            }
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

        private void ConfigureLights () {
            for (var i = 0; i < _cull.visibleLights.Count; i++) {
                if (i >= MaxVisibleLights)
                {
                    break;
                }
                VisibleLight light = _cull.visibleLights[i];
                _visibleLightColors[i] = light.finalColor;
                
                Vector4 attenuation = Vector4.zero;
                attenuation.w = 1f;

                if (light.lightType == LightType.Directional)
                {
                    var v = light.localToWorld.GetColumn(2);
                    v.x = -v.x;
                    v.y = -v.y;
                    v.z = -v.z;
                    _visibleLightDirectionsOrPositions[i] = v;
                }
                else
                {
                    _visibleLightDirectionsOrPositions[i] = light.localToWorld.GetColumn(3);
                    attenuation.x = 1f / Mathf.Max(light.range * light.range, 0.000001f);
                    if (light.lightType == LightType.Spot)
                    {
                        var v = light.localToWorld.GetColumn(2);
                        v.x = -v.x;
                        v.y = -v.y;
                        v.z = -v.z;
                        _visibleLightSpotDirections[i] = v;

                        var outerRad = Mathf.Deg2Rad * 0.5f * light.spotAngle;
                        var outerCos = Mathf.Cos(outerRad);
                        var outerTan = Mathf.Tan(outerRad);
                        var innerCos =
                            Mathf.Cos(Mathf.Atan(46f / 64f * outerTan));
                        float angleRange = Mathf.Max(innerCos - outerCos, 0.001f);
                        attenuation.z = 1f / angleRange;
                        attenuation.w = -outerCos * attenuation.z;
                    }
                }

                _visibleLightAttenuation[i] = attenuation;
            }

            if (_cull.visibleLights.Count > MaxVisibleLights)
            {
                var lightIndexMap = _cull.GetLightIndexMap();
                for (int i = MaxVisibleLights; i < _cull.visibleLights.Count; i++)
                {
                    lightIndexMap[i] = -1;
                }
                _cull.SetLightIndexMap(lightIndexMap);
            }
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