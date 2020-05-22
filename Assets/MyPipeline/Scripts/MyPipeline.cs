using System.Diagnostics;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace Svnvav.SRP2018
{
    public class MyPipeline : RenderPipeline
    {
        private const int MaxVisibleLights = 16;
        private const string ShadowSoftKeyword = "_SHADOWS_SOFT";
        private const string ShadowHardKeyword = "_SHADOWS_HARD";

        private static int VisibleLightColorsId = Shader.PropertyToID("_VisibleLightColors");

        private static int VisibleLightDirectionsOrPositionsId =
            Shader.PropertyToID("_VisibleLightDirectionsOrPositions");

        private static int VisibleLightSpotDirectionsId = Shader.PropertyToID("_VisibleLightSpotDirections");
        private static int VisibleLightAttenuationId = Shader.PropertyToID("_VisibleLightAttenuation");
        private static int LightIndicesOffsetAndCountID = Shader.PropertyToID("unity_LightIndicesOffsetAndCount");
        private static int ShadowMapId = Shader.PropertyToID("_ShadowMap");
        private static int ShadowMapSizeId = Shader.PropertyToID("_ShadowMapSize");
        private static int WorldToShadowMatricesId = Shader.PropertyToID("_WorldToShadowMatrices");
        private static int ShadowBiasId = Shader.PropertyToID("_ShadowBias");
        private static int ShadowDataId = Shader.PropertyToID("_ShadowData");

        private Vector4[] _visibleLightColors = new Vector4[MaxVisibleLights];
        private Vector4[] _visibleLightDirectionsOrPositions = new Vector4[MaxVisibleLights];
        private Vector4[] _visibleLightSpotDirections = new Vector4[MaxVisibleLights];
        private Vector4[] _visibleLightAttenuation = new Vector4[MaxVisibleLights];
        private Vector4[] _shadowData = new Vector4[MaxVisibleLights];

        private CullResults _cull;

        private CommandBuffer _cameraBuffer = new CommandBuffer
        {
            name = "Render Camera"
        };

        private CommandBuffer _shadowBuffer = new CommandBuffer
        {
            name = "Render Shadow"
        };

        private Material _errorMaterial;

        private DrawRendererFlags _drawFlags;

        private int _shadowMapSize;
        private RenderTexture _shadowMap;
        private int _shadowTileCount;

        public MyPipeline(bool dynamicBatching, bool instancing, int shadowMapSize)
        {
            _shadowMapSize = shadowMapSize;

            GraphicsSettings.lightsUseLinearIntensity = true;
            if (dynamicBatching)
            {
                _drawFlags = DrawRendererFlags.EnableDynamicBatching;
            }

            if (instancing)
            {
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
            if (camera.cameraType == CameraType.SceneView)
            {
                ScriptableRenderContext.EmitWorldGeometryForSceneView(camera);
            }
#endif

            CullResults.Cull(ref cullingParameters, context, ref _cull);

            if (_cull.visibleLights.Count > 0)
            {
                ConfigureLights();
                if (_shadowTileCount > 0)
                {
                    RenderShadows(context);
                }
                else
                {
                    _cameraBuffer.DisableShaderKeyword(ShadowHardKeyword);
                    _cameraBuffer.DisableShaderKeyword(ShadowSoftKeyword);
                }
            }
            else
            {
                _cameraBuffer.SetGlobalVector(LightIndicesOffsetAndCountID, Vector4.zero);
                _cameraBuffer.DisableShaderKeyword(ShadowHardKeyword);
                _cameraBuffer.DisableShaderKeyword(ShadowSoftKeyword);
            }

            context.SetupCameraProperties(camera);

            CameraClearFlags clearFlags = camera.clearFlags;

            _cameraBuffer.ClearRenderTarget(
                (clearFlags & CameraClearFlags.Depth) != 0,
                (clearFlags & CameraClearFlags.Color) != 0,
                camera.backgroundColor
            );


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

            if (_shadowMap)
            {
                RenderTexture.ReleaseTemporary(_shadowMap);
                _shadowMap = null;
            }
        }

        private void ConfigureLights()
        {
            _shadowTileCount = 0;
            for (var i = 0; i < _cull.visibleLights.Count; i++)
            {
                if (i >= MaxVisibleLights)
                {
                    break;
                }

                VisibleLight light = _cull.visibleLights[i];
                _visibleLightColors[i] = light.finalColor;

                Vector4 attenuation = Vector4.zero;
                attenuation.w = 1f;

                Vector4 shadow = Vector4.zero;

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

                        var shadowLight = light.light;
                        Bounds shadowBounds;

                        if (shadowLight.shadows != LightShadows.None &&
                            _cull.GetShadowCasterBounds(i, out shadowBounds))
                        {
                            _shadowTileCount++;
                            shadow.x = shadowLight.shadowStrength;
                            shadow.y = shadowLight.shadows == LightShadows.Soft ? 1f : 0f;
                        }
                    }
                }

                _visibleLightAttenuation[i] = attenuation;
                _shadowData[i] = shadow;
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

        private void RenderShadows(ScriptableRenderContext context)
        {
            int split;
            if (_shadowTileCount <= 1)
            {
                split = 1;
            }
            else if (_shadowTileCount <= 4)
            {
                split = 2;
            }
            else if (_shadowTileCount <= 9)
            {
                split = 3;
            }
            else
            {
                split = 4;
            }

            var tileScale = 1f / split;
            var tileSize = _shadowMapSize * tileScale;
            var tileViewport = new Rect(0, 0, tileSize, tileSize);
            _shadowMap = RenderTexture.GetTemporary(_shadowMapSize, _shadowMapSize, 16, RenderTextureFormat.Shadowmap);
            _shadowMap.filterMode = FilterMode.Bilinear;
            _shadowMap.wrapMode = TextureWrapMode.Clamp;

            CoreUtils.SetRenderTarget(_shadowBuffer, _shadowMap,
                RenderBufferLoadAction.DontCare,
                RenderBufferStoreAction.Store,
                ClearFlag.Depth);

            _shadowBuffer.BeginSample("Render Shadow");
            context.ExecuteCommandBuffer(_shadowBuffer);
            _shadowBuffer.Clear();

            var hardShadows = false;
            var softShadows = false;
            
            var worldToShadowMatrices = new Matrix4x4[MaxVisibleLights];

            var tileIndex = 0;
            for (int i = 0; i < _cull.visibleLights.Count && i < MaxVisibleLights; i++)
            {
                if (_shadowData[i].x <= 0f)
                {
                    continue;
                }

                Matrix4x4 viewMatrix, projMatrix;
                ShadowSplitData shadowSplitData;

                if (!_cull.ComputeSpotShadowMatricesAndCullingPrimitives(i, out viewMatrix, out projMatrix,
                    out shadowSplitData))
                {
                    _shadowData[i].x = 0f;
                    continue;
                }


                var tileOffsetX = tileIndex % split;
                var tileOffsetY = tileIndex / split;
                tileViewport.x = tileSize * tileOffsetX;
                tileViewport.y = tileSize * tileOffsetY;
                if (_shadowTileCount > 1)
                {
                    _shadowBuffer.SetViewport(tileViewport);
                    _shadowBuffer.EnableScissorRect(new Rect(tileViewport.x + 4, tileViewport.y + 4, tileSize - 8,
                        tileSize - 8));
                }

                _shadowBuffer.SetViewProjectionMatrices(viewMatrix, projMatrix);
                _shadowBuffer.SetGlobalFloat(ShadowBiasId, _cull.visibleLights[i].light.shadowBias);
                context.ExecuteCommandBuffer(_shadowBuffer);
                _shadowBuffer.Clear();

                var shadowSettings = new DrawShadowsSettings(_cull, i);
                context.DrawShadows(ref shadowSettings);

                if (SystemInfo.usesReversedZBuffer)
                {
                    projMatrix.m20 = -projMatrix.m20;
                    projMatrix.m21 = -projMatrix.m21;
                    projMatrix.m22 = -projMatrix.m22;
                    projMatrix.m23 = -projMatrix.m23;
                }

                var scaleOffset = Matrix4x4.identity;
                scaleOffset.m00 = scaleOffset.m11 = scaleOffset.m22 = 0.5f;
                scaleOffset.m03 = scaleOffset.m13 = scaleOffset.m23 = 0.5f;
                worldToShadowMatrices[i] = scaleOffset * (projMatrix * viewMatrix);

                if (_shadowTileCount > 1)
                {
                    var tileMatrix = Matrix4x4.identity;
                    tileMatrix.m00 = tileMatrix.m11 = tileScale;
                    tileMatrix.m03 = tileOffsetX * tileScale;
                    tileMatrix.m13 = tileOffsetY * tileScale;
                    worldToShadowMatrices[i] = tileMatrix * worldToShadowMatrices[i];
                }

                _shadowBuffer.SetGlobalMatrixArray(WorldToShadowMatricesId, worldToShadowMatrices);

                if (_shadowData[i].y <= 0)
                {
                    hardShadows = true;
                }
                else
                {
                    softShadows = true;
                }
                
                tileIndex++;
            }

            if (_shadowTileCount > 1)
            {
                _shadowBuffer.DisableScissorRect();
            }

            _shadowBuffer.SetGlobalTexture(ShadowMapId, _shadowMap);

            _shadowBuffer.SetGlobalVectorArray(ShadowDataId, _shadowData);

            var invShadowMapSize = 1f / _shadowMapSize;
            _shadowBuffer.SetGlobalVector(ShadowMapSizeId,
                new Vector4(invShadowMapSize, invShadowMapSize, _shadowMapSize, _shadowMapSize));

            CoreUtils.SetKeyword(_shadowBuffer, ShadowHardKeyword, hardShadows);
            CoreUtils.SetKeyword(_shadowBuffer, ShadowSoftKeyword, softShadows);

            _shadowBuffer.EndSample("Render Shadow");
            context.ExecuteCommandBuffer(_shadowBuffer);
            _shadowBuffer.Clear();
        }

        [Conditional("DEVELOPMENT_BUILD"), Conditional("UNITY_EDITOR")]
        private void DrawDefaultPipeline(ScriptableRenderContext context, Camera camera)
        {
            if (_errorMaterial == null)
            {
                Shader errorShader = Shader.Find("Hidden/InternalErrorShader");
                _errorMaterial = new Material(errorShader)
                {
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