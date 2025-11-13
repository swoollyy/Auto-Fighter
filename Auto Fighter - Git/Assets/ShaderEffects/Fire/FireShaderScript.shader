Shader "Custom/FireWisps_Vel_Advect_UpAxis"
{
    Properties
    {
        _FireTex    ("Fire (RGBA, alpha = flame shape)", 2D) = "white" {}
        _NoiseTex   ("Noise (R)", 2D) = "gray" {}
        _Tint       ("Tint", Color) = (1,0.6,0.2,1)
        _Glow       ("Glow", Range(0,64)) = 3.2

        // Mapping
        _FireScroll ("Fire Scroll (rev/s)", Float) = 0.6
        _FireScaleV ("Fire V Scale", Float) = 1.6

        // Noise swirl
        _NoiseScale ("Noise Tiling", Float) = 3.0
        _NoiseSpinA ("Noise Spin A (rev/s)", Float) = 0.15
        _NoiseSpinB ("Noise Spin B (rev/s)", Float) = 0.10

        // Cutout shaping
        _Cutoff     ("Clip Threshold", Range(0,1)) = 0.52
        _Feather    ("Edge Feather", Range(0.001,0.25)) = 0.07
        _HemiFeather("Hemisphere Feather", Range(0.001,0.35)) = 0.08

        // Extrusion
        _Shell      ("Base Offset (units)", Range(0,0.05)) = 0.012
        _Amplitude  ("Spike Amplitude", Range(0,5)) = 0.40
        _Rise       ("Upward Stretch", Range(0,3)) = 1.2

        // << World up axis selector >>
        _UpWS       ("World Up (x,y,z,0)", Vector) = (0,1,0,0) // set to (0,0,1,0) if Z-up

        // Velocity response
        _VelBend        ("Bend vs Speed", Range(0,4)) = 1.6
        _VelStretchV    ("Trail V-Scale", Range(0,3)) = 1.0
        _VelTwist       ("Extra Swirl vs Speed", Range(0,3)) = 0.9
        _TrailWS        ("Noise Advection (u per m/s)", Range(0,3)) = 0.8
        _VelMin         ("Speed Gate (m/s)", Range(0,5)) = 0.3
        _VelScale       ("Velocity Scale", Range(0,5)) = 1.0

        // Angular-velocity wind (optional)
        _AngWind        ("Angular Wind Strength", Range(0,50)) = 0.6
        _RadiusApprox   ("Ball Radius (m)", Range(0,5)) = 0.5
    }

    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" }
        Blend One One
        ZWrite Off
        Cull Back

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _FireTex, _NoiseTex;
            fixed4 _Tint; float _Glow;
            float _FireScroll, _FireScaleV;
            float _NoiseScale, _NoiseSpinA, _NoiseSpinB;
            float _Cutoff, _Feather, _HemiFeather;
            float _Shell, _Amplitude, _Rise;
            float4 _UpWS;            // (x,y,z,0)

            float _VelBend, _VelStretchV, _VelTwist, _TrailWS, _VelMin, _VelScale;
            float _AngWind, _RadiusApprox;

            // fed from script via MaterialPropertyBlock
            float3 _VelWS;           // linear velocity (world)
            float3 _AngVelWS;        // angular velocity (world), optional

            struct appdata { float4 vertex:POSITION; float3 normal:NORMAL; };
            struct v2f {
                float4 pos:SV_POSITION;
                float3 wp:TEXCOORD0;
                float3 centerWS:TEXCOORD1;
                float3 velDir:TEXCOORD2;
                float  speed:TEXCOORD3;
                float3 upWS:TEXCOORD4;
            };

            float3 rotAxis(float3 p, float3 axis, float ang)
            {
                // Rodrigues' rotation formula
                float s = sin(ang), c = cos(ang);
                return p*c + cross(axis, p)*s + axis*dot(axis,p)*(1.0-c);
            }

            float omniNoise(float3 P, float spinA, float spinB)
            {
                // Two rotating planar samples for full wrap
                float3 p = P * _NoiseScale;

                // rotate around up and a perpendicular axis to keep motion spherical
                float3 up = normalize(_UpWS.xyz + 1e-5);
                float3 ortho = normalize(abs(up.x)>0.5 ? float3(0,1,0) : float3(1,0,0));
                ortho = normalize(cross(up, ortho));

                p = rotAxis(p, up, spinA);
                p = rotAxis(p, ortho, spinB);

                float n1 = tex2Dlod(_NoiseTex, float4(p.xy,0,0)).r;
                float n2 = tex2Dlod(_NoiseTex, float4(p.xz,0,0)).r;
                return 0.5*(n1+n2);
            }

            v2f vert(appdata v)
            {
                v2f o;
                float3 wp = mul(unity_ObjectToWorld, v.vertex).xyz;
                float3 wn = UnityObjectToWorldNormal(v.normal);
                float3 centerWS = float3(unity_ObjectToWorld._m03, unity_ObjectToWorld._m13, unity_ObjectToWorld._m23);
                float3 up = normalize(_UpWS.xyz + 1e-5);

                // Velocity
                float3 vWS = _VelWS * _VelScale;
                float speed = length(vWS);
                float speedMask = saturate((speed - _VelMin) / max(1e-4, _VelMin));
                float3 velDir = (speed>1e-5) ? normalize(vWS) : float3(0,0,0);

                // Add angular "wind" along surface (optional)
                // tangentialWind ≈ ω × r; approximate r as normal * radius
                float3 tangential = cross(_AngVelWS, wn * _RadiusApprox);
                velDir = normalize(velDir + _AngWind * tangential);
                float effSpeed = max(speed, length(tangential)); // if spinning fast, still bend

                // Noise spins + extra twist from effective speed
                float spinA = _Time.y * (_NoiseSpinA * 6.2831853) + effSpeed * _VelTwist * 0.22;
                float spinB = _Time.y * (_NoiseSpinB * 6.2831853) + effSpeed * _VelTwist * 0.17;

                // Advect sample position opposite motion to create lag
                float3 samplePos = wp - velDir * (_TrailWS * effSpeed * speedMask);

                // Spike strength from noise
                float n = omniNoise(samplePos, spinA, spinB);
                float spike = saturate((n - _Cutoff) / max(1e-4, 1.0 - _Cutoff));

                // Only on +Up hemisphere
                float height = dot(wp - centerWS, up);
                float hemi = smoothstep(0.0, _HemiFeather, height);
                spike *= hemi;

                // Favor surfaces facing up a bit
                spike *= saturate(dot(wn, up));

                // Extrusion: normal + up + bend opposite motion
                float baseOff  = _Shell;
                float spikeOff = _Amplitude * spike;
                float upStretch = _Rise * spike * 0.12;

                // bend opposite horizontal component of vel (so "wind" leans back)
                float3 velHoriz = normalize(velDir - up * dot(velDir, up) + 1e-5);
                float3 bendDir = -velHoriz;
                float  bendAmt = _VelBend * spike * speedMask;

                float3 offset = wn * (baseOff + spikeOff) + up * upStretch + bendDir * bendAmt * 0.18;
                wp += offset;

                o.wp = wp;
                o.centerWS = centerWS;
                o.velDir = velDir;
                o.speed = effSpeed * speedMask;
                o.upWS = up;
                o.pos = UnityObjectToClipPos(mul(unity_WorldToObject, float4(wp,1)));
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float3 up = normalize(i.upWS);

                // Cylindrical wrap around chosen up axis: build a stable perpendicular basis
                float3 ex = normalize(abs(up.y)>0.9 ? float3(1,0,0) : cross(up, float3(0,1,0)));
                float3 ez = normalize(cross(ex, up));
                float3 rel = i.wp - i.centerWS;

                // angle around up axis from ex/ez plane
                float uAngle = atan2(dot(rel, ez), dot(rel, ex));
                float u = frac(uAngle * (1.0/(2.0*UNITY_PI)) + 1.0 + _Time.y * _FireScroll);

                // vertical V along up
                float height = dot(rel, up);
                float horizFactor = 1.0 - abs(dot(normalize(i.velDir + 1e-5), up));
                float vScale = _FireScaleV * (1.0 + _VelStretchV * i.speed * horizFactor * 0.35);
                float vTex = height * vScale;

                fixed4 fire = tex2D(_FireTex, float2(u, vTex)); // needs alpha

                // Fragment noise (same advection)
                float spinA = _Time.y * (_NoiseSpinA * 6.2831853) + i.speed * _VelTwist * 0.22;
                float spinB = _Time.y * (_NoiseSpinB * 6.2831853) + i.speed * _VelTwist * 0.17;
                float3 p = (i.wp - normalize(i.velDir + 1e-5) * (_TrailWS * i.speed));
                // rotate p around up and orthogonal
                float3 ex2 = normalize(abs(up.x)>0.5 ? float3(0,1,0) : float3(1,0,0));
                float3 ortho = normalize(cross(up, ex2));
                // axis rotations
                p = rotAxis(p, up, spinA);
                p = rotAxis(p, ortho, spinB);
                p *= _NoiseScale;

                float n1 = tex2D(_NoiseTex, p.xy).r;
                float n2 = tex2D(_NoiseTex, p.xz).r;
                float n  = 0.5*(n1+n2);

                // Mask & clip
                float hemi = smoothstep(0.0, _HemiFeather, height);
                float m = fire.a * n * hemi;
                float edge = smoothstep(_Cutoff, _Cutoff + _Feather, m);
                clip(edge - 0.001);

                // Emission
                float upFactor = saturate(0.4 + 0.6 * saturate(height));
                float speedBoost = 1.0 + 0.3 * saturate(i.speed);
                float3 col = fire.rgb * _Tint.rgb * (_Glow * upFactor * speedBoost);
                return float4(col, 1);
            }
            ENDCG
        }
    }

    FallBack Off
}