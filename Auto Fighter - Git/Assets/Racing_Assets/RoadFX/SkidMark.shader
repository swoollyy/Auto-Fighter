Shader "Custom/SkidRoadSurface"
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
            float2 uv_MainTex;      // UV0 for asphalt
            float2 uv2_SkidMask;    // UV2 for skid mask (0..1 along track)
        };

        void surf (Input IN, inout SurfaceOutputStandard o)
        {
            float2 uvRoad = IN.uv_MainTex;
            float2 uvSkid = IN.uv2_SkidMask;   // <<< USE UV2, NO frac, NO tiling

            float4 baseColor = tex2D(_MainTex, uvRoad);
            float  skid      = tex2D(_SkidMask, uvSkid).r;

            float darkFactor = 1.0 - skid * _SkidIntensity;
            baseColor.rgb   *= darkFactor;

            o.Albedo = baseColor.rgb;
            o.Alpha  = 1;
        }
        ENDCG
    }
    FallBack "Standard"
}
