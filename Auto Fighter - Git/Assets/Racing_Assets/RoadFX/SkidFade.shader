Shader "Custom/SkidFade"
{
    Properties
    {
        _MainTex ("SkidMask", 2D) = "black" {}
        _Fade   ("Fade", Range(0,1)) = 0.98
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        Pass
        {
            ZWrite Off
            ZTest Always
            Cull Off

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float _Fade;

            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; };
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };

            v2f vert (appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                float val = tex2D(_MainTex, i.uv).r;
                val *= _Fade;            // slowly fade toward 0
                return fixed4(val, val, val, 1);
            }
            ENDCG
        }
    }
}
