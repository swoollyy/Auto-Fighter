Shader "UI/GooSlimePanel"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        [HDR] _Color ("Fill Color", Color) = (0.02, 0.02, 0.03, 1)
        [HDR] _RimColor ("Rim / Blob Color", Color) = (0.08, 0.08, 0.1, 1)

        [Header(Shape)]
        _Aspect ("Panel Aspect (Width / Height)", Range(0.25, 8)) = 2.4
        _CornerRadius ("Corner Radius", Range(0.02, 0.8)) = 0.22
        _Inset ("Inset (room for glorp)", Range(0.0, 0.35)) = 0.08
        _EdgeSoftness ("Edge Softness", Range(0.002, 0.12)) = 0.028

        [Header(Glorp Motion)]
        _AnimTime ("Anim Time (auto)", Float) = 0
        _WarpAmount ("Edge Warp", Range(0, 0.45)) = 0.16
        _WarpDepth ("Warp Edge Depth", Range(0.02, 0.85)) = 0.22
        _NoiseScale ("Blob Size Scale", Range(0.5, 18)) = 3.2
        _NoiseSpeed ("Blob Speed", Range(0, 5)) = 1.35
        _DetailScale ("Detail Scale", Range(1, 24)) = 9
        _DetailAmount ("Detail Warp", Range(0, 0.25)) = 0.07
        _DetailSpeed ("Detail Speed", Range(0, 6)) = 2.2

        [Header(Optional Shine)]
        _RimWidth ("Rim Width", Range(0.0, 0.2)) = 0.045
        _RimStrength ("Rim Strength", Range(0, 2)) = 0.35

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
            float _Aspect;
            float _CornerRadius;
            float _Inset;
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

            // Cheap value noise → smooth-ish blob field
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

            // 2D rounded box SDF. p in centered space, b = half extents, r = corner radius.
            float SdRoundBox(float2 p, float2 b, float r)
            {
                float2 q = abs(p) - b + r;
                return length(max(q, 0.0)) + min(max(q.x, q.y), 0.0) - r;
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
                // Sample sprite (usually white) so Image source alpha still works.
                half4 tex = (tex2D(_MainTex, IN.uv) + _TextureSampleAdd);
                half4 baseCol = tex * IN.color * _Color;

                float aspect = max(_Aspect, 0.01);

                // Centered coords; X scaled by aspect so circles stay round on wide panels.
                float2 p = IN.uv * 2.0 - 1.0;
                p.x *= aspect;

                // Inset leaves a transparent margin so blobs can spill inside the Image rect.
                float inset = saturate(_Inset);
                float2 halfSize = float2(aspect, 1.0) * (1.0 - inset);
                float radius = min(_CornerRadius, min(halfSize.x, halfSize.y) * 0.95);

                float sdf = SdRoundBox(p, halfSize, radius);

                // Edge mask: warp only near the silhouette (Splatoon-style glorp).
                float edgeMask = 1.0 - smoothstep(0.0, max(_WarpDepth, 1e-4), abs(sdf));

                float t = _AnimTime;
                // Fallback if driver script is missing (may still freeze on static Canvas).
                if (t <= 0.0001)
                    t = _Time.y;

                float2 nUV = p * _NoiseScale + float2(t * _NoiseSpeed * 0.7, t * _NoiseSpeed * 0.45);
                float n1 = Fbm2(nUV) * 2.0 - 1.0;

                float2 dUV = p * _DetailScale + float2(-t * _DetailSpeed * 0.9, t * _DetailSpeed * 0.6);
                float n2 = Fbm2(dUV) * 2.0 - 1.0;

                float warp = (n1 * _WarpAmount + n2 * _DetailAmount) * edgeMask;
                float warpedSdf = sdf - warp;

                float softness = max(_EdgeSoftness, 1e-4);
                float fill = 1.0 - smoothstep(-softness, softness, warpedSdf);

                // Soft rim just inside the edge for a wet / bauble look.
                float rim = 0.0;
                if (_RimWidth > 1e-5 && _RimStrength > 1e-5)
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
