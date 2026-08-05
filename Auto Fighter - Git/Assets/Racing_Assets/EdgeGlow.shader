Shader "UI/EdgeGlow"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Glow Color", Color) = (1,1,1,1)
        _Intensity ("Intensity", Range(0, 5)) = 1
        _InnerRadius ("Inner Radius", Range(0, 1)) = 0.4
        _OuterRadius ("Outer Radius", Range(0, 1.5)) = 1.0
        _Softness ("Softness", Range(0.01, 1)) = 0.3
        
        // Required for UI
        _StencilComp ("Stencil Comparison", Float) = 8
        _Stencil ("Stencil ID", Float) = 0
        _StencilOp ("Stencil Operation", Float) = 0
        _StencilWriteMask ("Stencil Write Mask", Float) = 255
        _StencilReadMask ("Stencil Read Mask", Float) = 255
        _ColorMask ("Color Mask", Float) = 15
    }

    SubShader
    {
        Tags
        {
            "Queue"="Transparent"
            "IgnoreProjector"="True"
            "RenderType"="Transparent"
            "PreviewType"="Plane"
            "CanUseSpriteAtlas"="True"
        }

        Stencil
        {
            Ref [_Stencil]
            Comp [_StencilComp]
            Pass [_StencilOp]
            ReadMask [_StencilReadMask]
            WriteMask [_StencilWriteMask]
        }

        Cull Off
        Lighting Off
        ZWrite Off
        ZTest [unity_GUIZTestMode]
        Blend One One // Additive blending for glow

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 2.0

            #include "UnityCG.cginc"
            #include "UnityUI.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
                float4 color : COLOR;
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                float2 uv : TEXCOORD0;
                float4 color : COLOR;
            };

            sampler2D _MainTex;
            float4 _MainTex_ST;
            fixed4 _Color;
            float _Intensity;
            float _InnerRadius;
            float _OuterRadius;
            float _Softness;

            v2f vert (appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                o.color = v.color;
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                // Calculate distance from center (0.5, 0.5)
                float2 center = float2(0.5, 0.5);
                float2 uv = i.uv - center;
                
                // Correct for aspect ratio
                uv.x *= _ScreenParams.x / _ScreenParams.y;
                
                float dist = length(uv);
                
                // Softer edge falloff (wider smoothstep) so the glow doesn't look like a hard band.
                float soft = max(_Softness, 0.05);
                float glow = smoothstep(_InnerRadius, _InnerRadius + soft, dist);
                glow *= smoothstep(_OuterRadius + soft, _OuterRadius, dist);
                // Slight extra ease so the leading edge is less harsh.
                glow = glow * glow * (3.0 - 2.0 * glow);
                
                fixed4 col = _Color * i.color * glow * _Intensity;
                col.a = glow * _Color.a * i.color.a * _Intensity;
                
                return col;
            }
            ENDCG
        }
    }
}
