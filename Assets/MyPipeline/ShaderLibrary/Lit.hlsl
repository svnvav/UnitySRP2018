#ifndef MYRP_LIT_INCLUDED
#define MYRP_LIT_INCLUDED
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"

CBUFFER_START(UnityPerFrame)
	float4x4 unity_MatrixVP;
CBUFFER_END

CBUFFER_START(UnityPerDraw)
	float4x4 unity_ObjectToWorld;
	float4 unity_LightIndicesOffsetAndCount;
	float4 unity_4LightIndices0, unity_4LightIndices1;
CBUFFER_END

#define MAX_VISIBLE_LIGHTS 16
CBUFFER_START(_LightBuffer)
    float4 _VisibleLightColors[MAX_VISIBLE_LIGHTS];
    float4 _VisibleLightDirectionsOrPositions[MAX_VISIBLE_LIGHTS];
    float4 _VisibleLightSpotDirections[MAX_VISIBLE_LIGHTS];
    float4 _VisibleLightAttenuation[MAX_VISIBLE_LIGHTS];
CBUFFER_END

float3 DiffuseLight(int lightIndex, float3 normal, float3 worldPos){
    float3 lightColor = _VisibleLightColors[lightIndex].rgb;
    float4 lightDirectionOrPosition = _VisibleLightDirectionsOrPositions[lightIndex];
    float4 lightAttenuation = _VisibleLightAttenuation[lightIndex];
    float3 spotDirection = _VisibleLightSpotDirections[lightIndex].xyz;
    
    float3 lightVector = lightDirectionOrPosition.xyz - worldPos * lightDirectionOrPosition.w;
    float3 lightDirection = normalize(lightVector);
    float3 diffuse = saturate(dot(normal, lightDirection));
    
    float rangeFade = dot(lightVector, lightVector) * lightAttenuation.x;
    rangeFade = saturate(1 - rangeFade * rangeFade);
    rangeFade *= rangeFade;
    
    float spotFade = dot(spotDirection, lightDirection);
	spotFade = saturate(spotFade * lightAttenuation.z + lightAttenuation.w);
	spotFade *= spotFade;
    
    float sqrDistance = max(dot(lightVector, lightVector), 0.000001);
    
    diffuse *= spotFade * rangeFade / sqrDistance;
    return diffuse * lightColor;
}

#define UNITY_MATRIX_M unity_ObjectToWorld

#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/UnityInstancing.hlsl"

UNITY_INSTANCING_BUFFER_START(PerInstance)
	UNITY_DEFINE_INSTANCED_PROP(float4, _Color)
UNITY_INSTANCING_BUFFER_END(PerInstance)

struct VertexInput {
	float4 pos : POSITION;
	float3 normal : NORMAL;
	UNITY_VERTEX_INPUT_INSTANCE_ID
};

struct VertexOutput {
	float4 clipPos : SV_POSITION;
	float3 normal : TEXCOORD0;
	float3 worldPos : TEXCOORD1;
	float3 vertexLighting: TEXCOORD2;
	UNITY_VERTEX_INPUT_INSTANCE_ID
};

VertexOutput LitPassVertex (VertexInput input) {
	VertexOutput output;
	UNITY_SETUP_INSTANCE_ID(input);
	UNITY_TRANSFER_INSTANCE_ID(input, output);
	float4 worldPos = mul(UNITY_MATRIX_M, float4(input.pos.xyz, 1.0));
	output.clipPos = mul(unity_MatrixVP, worldPos);
	output.normal = mul((float3x3)UNITY_MATRIX_M, input.normal);//use transposed for nonuniform scale
	output.worldPos = worldPos.xyz;
	output.vertexLighting = 0;
	for(int i = 4; i < min(unity_LightIndicesOffsetAndCount.y, 8); ++i){
        int lightIndex = unity_4LightIndices1[i - 4];
        output.vertexLighting += DiffuseLight(lightIndex, output.normal, output.worldPos);
    }
	return output;
}

float4 LitPassFragment (VertexOutput input) : SV_TARGET {
    UNITY_SETUP_INSTANCE_ID(input);
    input.normal = normalize(input.normal);
    float3 albedo = UNITY_ACCESS_INSTANCED_PROP(PerInstance, _Color).rgb;
    float3 diffuseLight = input.vertexLighting;
    for(int i = 0; i < min(unity_LightIndicesOffsetAndCount.y, 4); ++i){
        int lightIndex = unity_4LightIndices0[i];
        diffuseLight += DiffuseLight(lightIndex, input.normal, input.worldPos);
    }
	float3 color = albedo * diffuseLight;
	return float4(color, 1);
}

#endif // MYRP_LIT_INCLUDED