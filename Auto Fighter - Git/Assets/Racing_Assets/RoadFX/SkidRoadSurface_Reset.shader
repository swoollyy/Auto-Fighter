Shader "Custom/SkidRoadSurface_Reset"
{
    Properties
    {
        _MainTex       ("Base (Albedo)", 2D) = "white" {}
        _SkidMask      ("Skid Mask", 2D)     = "black" {}
        _SkidIntensity ("Skid Intensity", Range(0,1)) = 0.8
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        LOD 200

        CGPROGRAM
        #pragma surface surf Standard fullforwardshadows

        sampler2D _MainTex;
        sampler2D _SkidMask;
        float _SkidIntensity;

        struct Input
        {
            float2 uv_MainTex;    // single UV set for both
        };

        void surf (Input IN, inout SurfaceOutputStandard o)
        {
            float2 uvRoad = IN.uv_MainTex;

            // Use the same UVs for the skid mask, but wrap into 0–1
            float2 uvSkid = frac(uvRoad);

            float4 baseColor = tex2D(_MainTex, uvRoad);
            float  skid      = tex2D(_SkidMask, uvSkid).r;

            float darkFactor = 1.0 - skid * _SkidIntensity;
            baseColor.rgb *= darkFactor;

            o.Albedo = baseColor.rgb;
            o.Alpha  = 1;
        }
        ENDCG
    }
    FallBack "Standard"
}
