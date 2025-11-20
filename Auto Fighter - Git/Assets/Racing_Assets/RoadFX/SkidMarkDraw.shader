Shader "Custom/SkidMarkDraw_Reset"
{
    Properties
    {
        _MainTex  ("SkidMask", 2D) = "black" {}
        _Center   ("Center", Vector) = (0.5, 0.5, 0, 0)
        _Radius   ("Radius", Float) = 0.02
        _Strength ("Strength", Float) = 0.6
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        Pass
        {
            ZWrite Off
            ZTest Always
            Cull Off
            Blend One OneMinusSrcAlpha

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4 _Center;
            float _Radius;
            float _Strength;

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv  : TEXCOORD0;
            };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv  = v.uv;
                return o;
            }

fixed4 frag(v2f i) : SV_Target
{
    float2 uv     = i.uv;
    float2 center = _Center.xy;

    float dist   = distance(uv, center);
    float circle = saturate(1.0 - dist / _Radius);

    float existing = tex2D(_MainTex, uv).r;  // old mask from temp

    // Blend toward white instead of nuking everything
    float added    = circle * _Strength;     // _Strength around 0.4–0.7
    float newValue = saturate(lerp(existing, 1.0, added));

    return fixed4(newValue, newValue, newValue, 1.0);
}

            ENDCG
        }
    }
}
