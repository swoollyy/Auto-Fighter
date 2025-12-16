Shader "Custom/URP/SkidRoadSurface_Reset"
{
    Properties
    {
        // Keep your old names so existing materials can be migrated easier
        _MainTex       ("Base (Albedo)", 2D) = "white" {}
        _SkidMask      ("Skid Mask", 2D)     = "black" {}
        _SkidIntensity ("Skid Intensity", Range(0,1)) = 0.8

        // Optional but useful in URP Lit
        _BaseColor     ("Base Color", Color) = (1,1,1,1)
        _Smoothness    ("Smoothness", Range(0,1)) = 0.5
        _Metallic      ("Metallic", Range(0,1)) = 0.0
    }

    SubShader
    {
Tags
{
    "RenderPipeline"="UniversalPipeline"
    "RenderType"="Opaque"
    "Queue"="Geometry"
}

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            // Lighting & shadows variants (URP standard)
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile _ _SHADOWS_SOFT
            #pragma multi_compile _ _LIGHT_LAYERS
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/SurfaceInput.hlsl"

            // Textures
            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);
            float4 _MainTex_ST;

            TEXTURE2D(_SkidMask);
            SAMPLER(sampler_SkidMask);
            float4 _SkidMask_ST;

            float _SkidIntensity;
            float4 _BaseColor;
            float _Smoothness;
            float _Metallic;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float4 tangentOS  : TANGENT;
                float2 uv         : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS   : TEXCOORD1;
                float4 tangentWS  : TEXCOORD2;
                float2 uv         : TEXCOORD3;
                float  fogCoord   : TEXCOORD4;
                float4 shadowCoord: TEXCOORD5;

                float2 lightmapUV : TEXCOORD6;
half3  vertexSH   : TEXCOORD7;
            };

            Varyings vert (Attributes IN)
            {
                Varyings OUT;

                VertexPositionInputs posInputs = GetVertexPositionInputs(IN.positionOS.xyz);
                VertexNormalInputs   nrmInputs = GetVertexNormalInputs(IN.normalOS, IN.tangentOS);

                OUT.positionCS  = posInputs.positionCS;
                OUT.positionWS  = posInputs.positionWS;
                OUT.normalWS    = nrmInputs.normalWS;
                OUT.tangentWS   = float4(nrmInputs.tangentWS, IN.tangentOS.w);
                OUT.uv          = IN.uv;

                OUT.fogCoord    = ComputeFogFactor(OUT.positionCS.z);
                OUT.shadowCoord = GetShadowCoord(posInputs);

                OUTPUT_LIGHTMAP_UV(IN.uv, unity_LightmapST, OUT.lightmapUV);
OUTPUT_SH(OUT.normalWS, OUT.vertexSH);

                return OUT;
            }

            half4 frag (Varyings IN) : SV_Target
            {
                // Base UV with tiling/offset
                float2 uvRoad = TRANSFORM_TEX(IN.uv, _MainTex);

                // Skid UV: same UVs but wrapped into 0-1 (your original frac logic)
                float2 uvSkid = frac(uvRoad);
                // Apply optional skid tiling/offset AFTER wrapping (feels closest to your original intent)
                uvSkid = uvSkid * _SkidMask_ST.xy + _SkidMask_ST.zw;

                half4 baseColor = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uvRoad) * _BaseColor;
                half  skid      = SAMPLE_TEXTURE2D(_SkidMask, sampler_SkidMask, uvSkid).r;

                half darkFactor = 1.0h - skid * (half)_SkidIntensity;
                baseColor.rgb *= darkFactor;

                // Build URP PBR inputs
                SurfaceData surfaceData;
                ZERO_INITIALIZE(SurfaceData, surfaceData);
                surfaceData.albedo = baseColor.rgb;
                surfaceData.alpha = 1.0h;
                surfaceData.metallic = (half)_Metallic;
                surfaceData.smoothness = (half)_Smoothness;
                surfaceData.normalTS = half3(0,0,1); // no normal map
                surfaceData.occlusion = 1.0h;
                surfaceData.emission = 0;

InputData inputData;
ZERO_INITIALIZE(InputData, inputData);

inputData.positionWS = IN.positionWS;
inputData.normalWS = NormalizeNormalPerPixel(IN.normalWS);
inputData.viewDirectionWS = GetWorldSpaceNormalizeViewDir(IN.positionWS);
inputData.shadowCoord = IN.shadowCoord;
inputData.fogCoord = IN.fogCoord;

// 🔑 THESE TWO LINES FIX THE SHADOW ARTIFACTS
inputData.bakedGI = SAMPLE_GI(IN.lightmapUV, IN.vertexSH, inputData.normalWS);

half4 color = UniversalFragmentPBR(inputData, surfaceData);
color.rgb = MixFog(color.rgb, IN.fogCoord);
return color;

            }
            ENDHLSL
        }

UsePass "Universal Render Pipeline/Lit/ShadowCaster"
    }

    FallBack Off
}
