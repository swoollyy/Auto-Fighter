Shader "Hidden/VintageTV"
{
    Properties
    {
        _Intensity("Intensity", Range(0,1)) = 0.7
        _ScanlineStrength("Scanline Strength", Range(0,1)) = 0.75
        _ScanlineDensity("Scanline Density", Range(100,1600)) = 900
        _Noise("Noise", Range(0,1)) = 0.18
        _Vignette("Vignette", Range(0,1)) = 0.35
        _Chromatic("Chromatic", Range(0,1)) = 0.15
        _TimeScale("Time Scale", Range(0,5)) = 1
    }

    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" }
        ZWrite Off ZTest Always Cull Off

        Pass
        {
            Name "VintageTV"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            // Source color texture provided by FullScreenPassRendererFeature
            TEXTURE2D_X(_BlitTexture);
            SAMPLER(sampler_LinearClamp);

            float _Intensity;
            float _ScanlineStrength;
            float _ScanlineDensity;
            float _Noise;
            float _Vignette;
            float _Chromatic;
            float _TimeScale;

            struct Attributes
            {
                uint vertexID : SV_VertexID;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            // Fullscreen triangle (URP-safe)
            Varyings Vert(Attributes IN)
            {
                Varyings OUT;

                // 0,1,2 -> fullscreen triangle
                float2 pos = float2((IN.vertexID == 2) ? 3.0 : -1.0,
                                    (IN.vertexID == 1) ? 3.0 : -1.0);

                OUT.positionHCS = float4(pos, 0.0, 1.0);
                OUT.uv = pos * 0.5 + 0.5;
                return OUT;
            }

            float Hash21(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 34.345);
                return frac(p.x * p.y);
            }

            float3 SampleCol(float2 uv)
            {
                return SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv).rgb;
            }

            half4 Frag(Varyings IN) : SV_Target
            {
                float2 uv = IN.uv;
                uv.y = 1.0 - uv.y;
                float t = _Time.y * _TimeScale;

                float3 baseCol = SampleCol(uv);

                // Scanlines
                float scan = sin((uv.y + t * 0.5) * _ScanlineDensity) * 0.5 + 0.5;
                float scanMul = lerp(1.0, lerp(0.6, 1.0, scan), _ScanlineStrength);

                // Chromatic aberration
                float2 off = float2(0.002, 0.0) * (_Chromatic * _Intensity);
                float r = SampleCol(uv + off).r;
                float g = SampleCol(uv).g;
                float b = SampleCol(uv - off).b;
                float3 col = float3(r, g, b);

                // Noise
                float n = Hash21(uv * float2(1920, 1080) + t) - 0.5;
                col += n * (_Noise * _Intensity);

                col *= scanMul;

                // Vignette
                float2 d = uv * 2.0 - 1.0;
                float vig = saturate(1.0 - dot(d, d) * 0.7);
                col *= lerp(1.0, vig, _Vignette * _Intensity);

                col = lerp(baseCol, col, saturate(_Intensity));
                return half4(col, 1);
            }
            ENDHLSL
        }
    }
}
