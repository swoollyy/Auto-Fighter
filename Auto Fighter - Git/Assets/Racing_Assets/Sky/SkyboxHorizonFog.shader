Shader "Racing/SkyboxHorizonFog"
{
    Properties
    {
        [HDR] _ZenithColor ("Zenith (upper sky)", Color) = (0.207, 0.0, 0.34, 1)
        [HDR] _HorizonColor ("Horizon Fog", Color) = (0.52, 0.48, 0.58, 1)
        [HDR] _GroundColor ("Below Horizon", Color) = (0.28, 0.26, 0.32, 1)
        _HorizonExponent ("Horizon Softness", Range(0.15, 6)) = 1.35
        _HorizonLift ("Horizon Lift", Range(-0.4, 0.4)) = 0.02
        _HorizonBlendWidth ("Horizon Fog Band", Range(0.05, 1.5)) = 0.55
    }

    SubShader
    {
        Tags { "Queue"="Background" "RenderType"="Background" "PreviewType"="Skybox" }
        Cull Off
        ZWrite Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 viewDir : TEXCOORD0;
            };

            half4 _ZenithColor;
            half4 _HorizonColor;
            half4 _GroundColor;
            half _HorizonExponent;
            half _HorizonLift;
            half _HorizonBlendWidth;

            v2f vert(appdata v)
            {
                v2f o;
                // Skybox mesh is already in view space conventions for UNITY_MATRIX_MV
                o.pos = UnityObjectToClipPos(v.vertex);
                o.viewDir = v.vertex.xyz;
                return o;
            }

            half4 frag(v2f i) : SV_Target
            {
                float3 dir = normalize(i.viewDir);
                float y = dir.y - _HorizonLift;

                // Foggy band around the horizon; upper sky leans zenith, below leans ground.
                float up = saturate(y / max(0.001h, _HorizonBlendWidth));
                up = pow(up, _HorizonExponent);

                float down = saturate((-y) / max(0.001h, _HorizonBlendWidth));
                down = pow(down, _HorizonExponent);

                half3 col = _HorizonColor.rgb;
                col = lerp(col, _ZenithColor.rgb, up);
                col = lerp(col, _GroundColor.rgb, down);

                return half4(col, 1);
            }
            ENDCG
        }
    }
    FallBack Off
}
