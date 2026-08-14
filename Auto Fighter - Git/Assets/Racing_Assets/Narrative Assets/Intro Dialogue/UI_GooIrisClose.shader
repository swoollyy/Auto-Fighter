Shader "UI/GooIrisClose"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        [HDR] _Color ("Fill Color", Color) = (0, 0, 0, 1)
        [HDR] _RimColor ("Rim / Blob Color", Color) = (0, 0, 0, 1)

        [Header(Iris)]
        _HoleRadius ("Hole Radius", Range(-0.2, 2.0)) = 1.2
        _Aspect ("Screen Aspect (Width / Height)", Range(0.25, 8)) = 1.777
        _EdgeSoftness ("Edge Softness", Range(0.001, 0.08)) = 0.012

        [Header(Glorp Motion)]
        _AnimTime ("Anim Time (auto)", Float) = 0
        _WarpAmount ("Edge Warp", Range(0, 0.55)) = 0.28
        _WarpDepth ("Warp Edge Depth", Range(0.02, 1.2)) = 0.55
        _NoiseScale ("Blob Size Scale", Range(0.5, 24)) = 5.5
        _NoiseSpeed ("Blob Speed", Range(0, 6)) = 2.1
        _DetailScale ("Detail Scale", Range(1, 32)) = 14
        _DetailAmount ("Detail Warp", Range(0, 0.35)) = 0.12
        _DetailSpeed ("Detail Speed", Range(0, 8)) = 3.1

        [Header(Optional Shine)]
        _RimWidth ("Rim Width", Range(0.0, 0.25)) = 0.0
        _RimStrength ("Rim Strength", Range(0, 2)) = 0.0

        // Required for Unity UI
        _StencilComp ("Stencil Comparison", Float) = 8
        _Stencil ("Stencil ID", Float) = 0
        _StencilOp ("Stencil Operation", Float) = 0
        _StencilWriteMask ("Stencil Write Mask", Float) = 255
        _StencilReadMask ("Stencil Read Mask", Float) = 255
        _ColorMask ("Color Mask", Float) = 15
        [Toggle(UNITY_UI_ALPHACLIP)] _UseUIAlphaClip ("Use Alpha Clip", Float) = 0
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
        Blend SrcAlpha OneMinusSrcAlpha
        ColorMask [_ColorMask]

        Pass
        {
            Name "Default"
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #pragma multi_compile_local _ UNITY_UI_CLIP_RECT
            #pragma multi_compile_local _ UNITY_UI_ALPHACLIP

            #include "UnityCG.cginc"
            #include "UnityUI.cginc"

            struct appdata_t
            {
                float4 vertex : POSITION;
                float4 color : COLOR;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                fixed4 color : COLOR;
                float2 uv : TEXCOORD0;
                float4 worldPosition : TEXCOORD1;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            sampler2D _MainTex;
            float4 _MainTex_ST;
            fixed4 _Color;
            fixed4 _RimColor;
            float4 _ClipRect;
            float4 _TextureSampleAdd;

            float _AnimTime;
            float _HoleRadius;
            float _Aspect;
            float _EdgeSoftness;
            float _WarpAmount;
            float _WarpDepth;
            float _NoiseScale;
            float _NoiseSpeed;
            float _DetailScale;
            float _DetailAmount;
            float _DetailSpeed;
            float _RimWidth;
            float _RimStrength;

            float Hash21(float2 p)
            {
                p = frac(p * float2(123.34, 345.45));
                p += dot(p, p + 34.345);
                return frac(p.x * p.y);
            }

            float ValueNoise(float2 p)
            {
                float2 i = floor(p);
                float2 f = frac(p);
                float2 u = f * f * (3.0 - 2.0 * f);
                float a = Hash21(i);
                float b = Hash21(i + float2(1, 0));
                float c = Hash21(i + float2(0, 1));
                float d = Hash21(i + float2(1, 1));
                return lerp(lerp(a, b, u.x), lerp(c, d, u.x), u.y);
            }

            float Fbm2(float2 p)
            {
                float v = 0.0;
                float a = 0.5;
                v += ValueNoise(p) * a; p = p * 2.02 + 17.1; a *= 0.5;
                v += ValueNoise(p) * a;
                return v;
            }

            v2f vert(appdata_t v)
            {
                v2f OUT;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);
                OUT.worldPosition = v.vertex;
                OUT.vertex = UnityObjectToClipPos(v.vertex);
                OUT.uv = TRANSFORM_TEX(v.uv, _MainTex);
                OUT.color = v.color;
                return OUT;
            }

            fixed4 frag(v2f IN) : SV_Target
            {
                half4 tex = (tex2D(_MainTex, IN.uv) + _TextureSampleAdd);
                half4 baseCol = tex * IN.color * _Color;

                float aspect = max(_Aspect, 0.01);
                float2 p = IN.uv * 2.0 - 1.0;
                p.x *= aspect;

                // Distance to cover screen corners in this aspect-corrected space.
                float cornerDist = length(float2(aspect, 1.0));
                float hole = _HoleRadius * cornerDist;

                float r = length(p);
                // Positive outside the hole (black), negative inside (see-through).
                float sdf = r - hole;

                float edgeMask = 1.0 - smoothstep(0.0, max(_WarpDepth, 1e-4), abs(sdf));

                float t = _AnimTime;
                if (t <= 0.0001)
                    t = _Time.y;

                float2 nUV = p * _NoiseScale + float2(t * _NoiseSpeed * 0.7, t * _NoiseSpeed * 0.45);
                float n1 = Fbm2(nUV) * 2.0 - 1.0;
                float2 dUV = p * _DetailScale + float2(-t * _DetailSpeed * 0.9, t * _DetailSpeed * 0.6);
                float n2 = Fbm2(dUV) * 2.0 - 1.0;

                float warp = (n1 * _WarpAmount + n2 * _DetailAmount) * edgeMask;
                float warpedSdf = sdf - warp;

                float softness = max(_EdgeSoftness, 1e-4);
                // Opaque black outside the gooey hole.
                float fill = smoothstep(-softness, softness, warpedSdf);

                // When fully sealed, force solid cover (no soft hole remnant).
                if (_HoleRadius <= 0.001)
                    fill = 1.0;

                float rim = 0.0;
                if (_RimWidth > 1e-5 && _RimStrength > 1e-5 && _HoleRadius > 0.001)
                {
                    float inner = smoothstep(-_RimWidth - softness, -softness, warpedSdf);
                    float outer = 1.0 - smoothstep(-softness, softness, warpedSdf);
                    rim = saturate(inner * outer) * _RimStrength;
                }

                fixed3 rgb = lerp(baseCol.rgb, _RimColor.rgb * IN.color.rgb, rim);
                float alpha = fill * baseCol.a;

                #ifdef UNITY_UI_CLIP_RECT
                alpha *= UnityGet2DClipping(IN.worldPosition.xy, _ClipRect);
                #endif

                #ifdef UNITY_UI_ALPHACLIP
                clip(alpha - 0.001);
                #endif

                return fixed4(rgb, alpha);
            }
            ENDCG
        }
    }
}
