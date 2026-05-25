Shader "Hidden/CaveFog"
{
    Properties
    {
        _MainTex      ("Texture",        2D)    = "white" {}
        _FogColor     ("Fog Color",      Color) = (0.04, 0.07, 0.14, 1)
        // Densidade: quanto a névoa acumula por metro de raio dentro da gruta
        _FogDensity   ("Fog Density",    Float) = 0.05
        // Raio em torno do barco onde a névoa se dissipa (metros)
        _ClearRadius  ("Clear Radius",   Float) = 10.0
        _BoatWorldPos ("Boat World Pos", Vector) = (0,0,0,0)
        _ScrollSpeed  ("Scroll Speed",   Float) = 0.03
        _NoiseScale   ("Noise Scale",    Float) = 0.08
    }

    SubShader
    {
        Cull Off ZWrite Off ZTest Always

        Pass
        {
            CGPROGRAM
            #pragma vertex   vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            // ── Samplers ──────────────────────────────────────────────
            sampler2D _MainTex;
            sampler2D _CameraDepthTexture;

            // ── Uniforms ──────────────────────────────────────────────
            fixed4   _FogColor;
            float    _FogDensity;
            float    _ClearRadius;
            float4   _BoatWorldPos;
            float    _ScrollSpeed;
            float    _NoiseScale;
            float4x4 _InverseVP;      // Reconstrução de posição mundial
            
            float4x4 _CaveWorldToLocal; // Matriz para transformar raio para OBB
            float3   _CaveLocalMin;     // Limites locais
            float3   _CaveLocalMax;

            // ── Structs ───────────────────────────────────────────────
            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; };
            struct v2f     { float4 vertex : SV_POSITION; float2 uv : TEXCOORD0; };

            // ── Ruído procedural ──────────────────────────────────────
            float hash(float2 p)
            {
                p = frac(p * float2(127.1, 311.7));
                p += dot(p, p + 19.19);
                return frac(p.x * p.y);
            }
            float valueNoise(float2 uv)
            {
                float2 i = floor(uv); float2 f = frac(uv);
                float2 u = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(hash(i),            hash(i+float2(1,0)), u.x),
                            lerp(hash(i+float2(0,1)), hash(i+float2(1,1)), u.x), u.y);
            }
            float fbm(float2 uv)
            {
                float v = 0.0, amp = 0.5;
                for (int o = 0; o < 3; o++) { v += valueNoise(uv)*amp; uv=uv*2.1+float2(1.7,9.2); amp*=0.5; }
                return v;
            }

            // ── Interseção raio-AABB (método das fatias) ──────────────
            // Devolve true se há interseção e preenche tNear/tFar (distâncias ao longo do raio)
            bool rayAABB(float3 ro, float3 rd, float3 bMin, float3 bMax,
                         out float tNear, out float tFar)
            {
                float3 invD = 1.0 / rd;
                float3 t0   = (bMin - ro) * invD;
                float3 t1   = (bMax - ro) * invD;
                float3 tMn  = min(t0, t1);
                float3 tMx  = max(t0, t1);
                tNear = max(max(tMn.x, tMn.y), tMn.z);
                tFar  = min(min(tMx.x, tMx.y), tMx.z);
                return tFar > max(tNear, 0.0);
            }

            // ── Vertex ────────────────────────────────────────────────
            v2f vert(appdata v) { v2f o; o.vertex=UnityObjectToClipPos(v.vertex); o.uv=v.uv; return o; }

            // ── Fragment ──────────────────────────────────────────────
            fixed4 frag(v2f i) : SV_Target
            {
                fixed4 col = tex2D(_MainTex, i.uv);

                // ── Reconstrução da posição mundial ───────────────────
                float rawDepth = SAMPLE_DEPTH_TEXTURE(_CameraDepthTexture, i.uv);
                #if UNITY_REVERSED_Z
                    if (rawDepth < 0.0001) return col;   // sky
                #else
                    if (rawDepth > 0.9999) return col;   // sky
                #endif

                float2 ndc = i.uv * 2.0 - 1.0;
                #if UNITY_UV_STARTS_AT_TOP
                    ndc.y = -ndc.y;
                #endif
                float4 wp4    = mul(_InverseVP, float4(ndc, rawDepth, 1.0));
                float3 worldPos = wp4.xyz / wp4.w;

                // ── Raio câmara → pixel ───────────────────────────────
                float3 camPos   = _WorldSpaceCameraPos;
                float3 rayVec   = worldPos - camPos;
                float  pixelDist = length(rayVec);
                float3 rayDir   = rayVec / pixelDist;

                // ── Transformar para Local Space da Gruta (Oriented Bounding Box) ─
                float3 localCamPos = mul(_CaveWorldToLocal, float4(camPos, 1.0)).xyz;
                float3 localRayDir = mul(_CaveWorldToLocal, float4(rayDir, 0.0)).xyz;

                // ── Interseção do raio com o volume da gruta ──────────
                float tNear, tFar;
                if (!rayAABB(localCamPos, localRayDir, _CaveLocalMin, _CaveLocalMax, tNear, tFar))
                    return col;   // raio não atravessa a gruta — pixel fora

                // Limita ao segmento [câmara … superfície do pixel]
                tNear = max(tNear, 0.0);
                tFar  = min(tFar, pixelDist);
                if (tFar <= tNear) return col;  // superfície está à frente da gruta

                // fogDist = metros do raio que passam pelo interior da gruta
                float fogDist = tFar - tNear;

                // ── Névoa exponencial acumulada ao longo do raio ──────
                // Quanto mais fundo o pixel está na gruta, mais névoa acumula
                float fogFactor = 1.0 - exp(-_FogDensity * fogDist);

                // ── Barco disipa a névoa à sua volta ──────────────────
                // Usa o ponto médio do segmento interior para posicionar os wisps,
                // e a posição da superfície para o raio de limpeza
                float boatDist  = length(worldPos - _BoatWorldPos.xyz);
                float clearMask = 1.0 - smoothstep(0.0, _ClearRadius, boatDist);
                fogFactor      *= (1.0 - clearMask * 0.95);

                // ── Ruído animado nos wisps (ponto médio do segmento) ─
                float3 midPt  = camPos + rayDir * (tNear + fogDist * 0.5);
                float2 noiseUV = midPt.xz * _NoiseScale
                               + float2(_Time.y * _ScrollSpeed,
                                        _Time.y * _ScrollSpeed * 0.6);
                float noise = fbm(noiseUV);
                fogFactor *= (0.55 + noise * 0.45);

                // ── Resultado ─────────────────────────────────────────
                fogFactor = saturate(fogFactor);
                return fixed4(lerp(col.rgb, _FogColor.rgb, fogFactor), col.a);
            }
            ENDCG
        }
    }
    FallBack Off
}
