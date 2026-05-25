Shader "Cave/EntranceMist"
{
    Properties
    {
        [Header(Face Exterior Nevoa Escura)]
        _MistColor     ("Mist Color",         Color)       = (0.03, 0.04, 0.08, 1)
        _MaxAlpha      ("Max Opacity",        Range(0,1))  = 0.97
        _FadeStart     ("Fade Start m",       Float)       = 15.0
        _FadeEnd       ("Fade End m",         Float)       = 4.0
        _EdgeFade      ("Edge Fade",          Range(0,0.5))= 0.18
        _NoiseScale    ("Noise Scale",        Float)       = 3.5
        _ScrollSpeed   ("Scroll Speed",       Float)       = 0.04
        _NoiseStrength ("Noise Strength",     Range(0,1))  = 0.35

        [Header(Face Interior Emissao de Luz)]
        _GlowColor     ("Glow Color",         Color)       = (0.05, 0.12, 0.25, 1)
        _GlowIntensity ("Glow Intensity",     Range(0,3))  = 0.8
        _GlowFadeStart ("Glow Fade Start m",  Float)       = 20.0
        _GlowFadeEnd   ("Glow Fade End m",    Float)       = 2.0
        _GlowEdge      ("Glow Edge Softness", Range(0,0.5))= 0.30
        _FlickerSpeed  ("Flicker Speed",      Float)       = 0.8
        _FlickerAmt    ("Flicker Amount",     Range(0,0.4))= 0.12
    }

    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" }
        ZWrite Off

        // Pass 1: face interior (tras do Quad) - emissao aditiva
        Pass
        {
            Cull Front
            Blend One One

            CGPROGRAM
            #pragma vertex   vert
            #pragma fragment fragGlow
            #include "UnityCG.cginc"

            fixed4 _GlowColor;
            float  _GlowIntensity;
            float  _GlowFadeStart;
            float  _GlowFadeEnd;
            float  _GlowEdge;
            float  _FlickerSpeed;
            float  _FlickerAmt;

            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; };
            struct v2f     { float4 vertex : SV_POSITION; float2 uv : TEXCOORD0; float3 worldPos : TEXCOORD1; };

            float hash2(float2 p)
            {
                p = frac(p * float2(127.1, 311.7));
                p += dot(p, p + 19.19);
                return frac(p.x * p.y);
            }
            float valueNoise2(float2 uv)
            {
                float2 i = floor(uv); float2 f = frac(uv); float2 u = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(hash2(i), hash2(i + float2(1,0)), u.x),
                            lerp(hash2(i + float2(0,1)), hash2(i + float2(1,1)), u.x), u.y);
            }

            v2f vert(appdata v)
            {
                v2f o;
                o.vertex   = UnityObjectToClipPos(v.vertex);
                o.uv       = v.uv;
                o.worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
                return o;
            }

            fixed4 fragGlow(v2f i) : SV_Target
            {
                // Vinheta: brilho concentrado no centro
                float2 edgeDist = abs(i.uv - 0.5) * 2.0;
                float  edgeMask = 1.0 - smoothstep(1.0 - _GlowEdge * 2.0, 1.0,
                                                   max(edgeDist.x, edgeDist.y));
                edgeMask = pow(edgeMask, 1.5);

                // Fade de distancia
                float camDist    = length(_WorldSpaceCameraPos - i.worldPos);
                float distFactor = smoothstep(_GlowFadeEnd, _GlowFadeStart, camDist);

                // Flicker subtil
                float flicker       = valueNoise2(float2(_Time.y * _FlickerSpeed, 0.5));
                float flickerFactor = 1.0 - _FlickerAmt + flicker * _FlickerAmt;

                float glow = _GlowIntensity * edgeMask * distFactor * flickerFactor;
                return fixed4(_GlowColor.rgb * glow, 1.0);
            }
            ENDCG
        }

        // Pass 2: face exterior (frente do Quad) - nevoa escura com alpha
        Pass
        {
            Cull Back
            Blend SrcAlpha OneMinusSrcAlpha

            CGPROGRAM
            #pragma vertex   vert
            #pragma fragment fragMist
            #include "UnityCG.cginc"

            fixed4 _MistColor;
            float  _MaxAlpha;
            float  _FadeStart;
            float  _FadeEnd;
            float  _EdgeFade;
            float  _NoiseScale;
            float  _ScrollSpeed;
            float  _NoiseStrength;

            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; };
            struct v2f     { float4 vertex : SV_POSITION; float2 uv : TEXCOORD0; float3 worldPos : TEXCOORD1; };

            float hash(float2 p)
            {
                p = frac(p * float2(127.1, 311.7));
                p += dot(p, p + 19.19);
                return frac(p.x * p.y);
            }
            float valueNoise(float2 uv)
            {
                float2 i = floor(uv); float2 f = frac(uv); float2 u = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(hash(i), hash(i + float2(1,0)), u.x),
                            lerp(hash(i + float2(0,1)), hash(i + float2(1,1)), u.x), u.y);
            }
            float fbm(float2 uv)
            {
                float v = 0.0, amp = 0.5;
                for (int o = 0; o < 4; o++) { v += valueNoise(uv)*amp; uv = uv*2.1 + float2(1.7,9.2); amp *= 0.5; }
                return v;
            }

            v2f vert(appdata v)
            {
                v2f o;
                o.vertex   = UnityObjectToClipPos(v.vertex);
                o.uv       = v.uv;
                o.worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
                return o;
            }

            fixed4 fragMist(v2f i) : SV_Target
            {
                // Ruido animado
                float2 noiseUV    = i.uv * _NoiseScale
                                  + float2(_Time.y * _ScrollSpeed, _Time.y * _ScrollSpeed * 0.7);
                float  noise      = fbm(noiseUV);
                float  noiseFactor = 1.0 - _NoiseStrength + noise * _NoiseStrength;

                // Fade de borda
                float2 edgeDist = abs(i.uv - 0.5) * 2.0;
                float  edgeMask = 1.0 - smoothstep(1.0 - _EdgeFade * 2.0, 1.0,
                                                   max(edgeDist.x, edgeDist.y));

                // Fade de distancia
                float camDist    = length(_WorldSpaceCameraPos - i.worldPos);
                float distFactor = smoothstep(_FadeEnd, _FadeStart, camDist);

                float alpha = _MaxAlpha * edgeMask * distFactor * noiseFactor;
                return fixed4(_MistColor.rgb, saturate(alpha));
            }
            ENDCG
        }
    }
    FallBack Off
}
