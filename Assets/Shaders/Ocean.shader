Shader "Custom/Ocean"
{
    Properties
    {
        // Wave A/B/C: XY = direction, Z = steepness (Q), W = wavelength
        _WaveA     ("Wave A  (dir XY, steep, wavelength)", Vector) = (1, 0, 0.3, 10)
        _WaveB     ("Wave B  (dir XY, steep, wavelength)", Vector) = (0, 1, 0.15, 20)
        _WaveC     ("Wave C  (dir XY, steep, wavelength)", Vector) = (1, 1, 0.1,  30)
        _WaveSpeed ("Wave Speed", Float) = 1.0

        _ShallowColor ("Shallow Color", Color) = (0.1, 0.55, 0.7, 0.85)
        _DeepColor    ("Deep Color",    Color) = (0.02, 0.1, 0.35, 1.0)
        _DepthFade    ("Depth Fade Distance", Float) = 8.0
        _HorizonColor ("Horizon Color", Color) = (0.7, 0.88, 1.0, 1.0)

        _FoamColor     ("Foam Color",     Color)         = (1, 1, 1, 1)
        _FoamThreshold ("Foam Threshold", Range(0, 1))   = 0.75
        _FoamSoftness  ("Foam Softness",  Range(0.01, 0.5)) = 0.1

        _Smoothness   ("Smoothness",    Range(0,1))   = 0.92
        _Metallic     ("Metallic",      Range(0,1))   = 0.0
        _FresnelPower ("Fresnel Power", Range(1,8))   = 4.0

        _NormalTex   ("Normal Map",    2D)          = "bump" {}
        _NormalScale ("Normal Scale",  Range(0,3))  = 1.0
        _NormalSpeed ("Normal Speed",  Float)       = 0.05

        _WakeStrength ("Wake Strength", Range(0,1)) = 0.6
        _WakeLength   ("Wake Length",   Float)      = 40.0

        _NoiseScale    ("Noise Scale",    Float) = 0.03
        _NoiseAmplitude("Noise Amplitude",Float) = 0.3
        _NoiseSpeed    ("Noise Speed",    Float) = 0.2
    }

    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off

        // Stencil Buffer: Não desenha água onde a máscara do barco escreveu o ID 3
        Stencil
        {
            Ref 3
            Comp NotEqual
            Pass Keep
        }

        CGPROGRAM
        #pragma surface surf Standard vertex:vert alpha:fade
        #pragma target 3.0
        #include "UnityCG.cginc"

        // ── Uniforms ──────────────────────────────────────────────────────────
        float4 _WaveA, _WaveB, _WaveC;
        float  _WaveSpeed;

        fixed4 _ShallowColor, _DeepColor, _HorizonColor;
        float  _DepthFade;

        fixed4 _FoamColor;
        float  _FoamThreshold, _FoamSoftness;

        float  _Smoothness, _Metallic, _FresnelPower;

        sampler2D _NormalTex;
        float4    _NormalTex_ST;
        float     _NormalScale, _NormalSpeed;

        float     _WakeStrength, _WakeLength;
        float     _BoatSpeed;
        float4    _BoatPosition, _BoatForward;

        float     _NoiseScale;
        float     _NoiseAmplitude;
        float     _NoiseSpeed;

        sampler2D _CameraDepthTexture;

        // ── Perlin noise with derivatives ────────────────────────────────────
        float2 hash2(float2 p)
        {
            p = float2(dot(p, float2(127.1, 311.7)), dot(p, float2(269.5, 183.3)));
            return -1.0 + 2.0 * frac(sin(p) * 43758.5453123);
        }

        // Returns float3(noise_value, dNoise_dx, dNoise_dy)
        float3 perlinNoiseDeriv(float2 p)
        {
            float2 i = floor(p);
            float2 f = frac(p);
            
            // Quintic interpolation
            float2 u = f * f * f * (f * (f * 6.0 - 15.0) + 10.0);
            float2 du = 30.0 * f * f * (f - 1.0) * (f - 1.0);
            
            float2 ga = hash2(i + float2(0.0, 0.0));
            float2 gb = hash2(i + float2(1.0, 0.0));
            float2 gc = hash2(i + float2(0.0, 1.0));
            float2 gd = hash2(i + float2(1.0, 1.0));
            
            float va = dot(ga, f - float2(0.0, 0.0));
            float vb = dot(gb, f - float2(1.0, 0.0));
            float vc = dot(gc, f - float2(0.0, 1.0));
            float vd = dot(gd, f - float2(1.0, 1.0));
            
            float A = lerp(va, vb, u.x);
            float B = lerp(vc, vd, u.x);
            
            float val = lerp(A, B, u.y);
            
            // Derivatives
            float dx = lerp(lerp(ga.x, gb.x, u.x), lerp(gc.x, gd.x, u.x), u.y) + du.x * lerp(vb - va, vd - vc, u.y);
            float dy = lerp(lerp(ga.y, gb.y, u.x), lerp(gc.y, gd.y, u.x), u.y) + du.y * (B - A);
            
            return float3(val, dx, dy);
        }

        // ── Gerstner wave ─────────────────────────────────────────────────────
        //  wave.xy = direction, wave.z = steepness Q, wave.w = wavelength
        //  Returns xyz world-space displacement; accumulates tangent+binormal
        //  for the analytical surface normal.
        float3 GerstnerWave(float4 wave, float3 wPos,
                            inout float3 tangent, inout float3 binormal)
        {
            float Q  = wave.z;
            float wl = wave.w;
            float k  = 2.0 * UNITY_PI / wl;
            float c  = sqrt(9.8 / k);          // phase speed
            float2 d = normalize(wave.xy);
            float  f = k * (dot(d, wPos.xz) - c * _Time.y * _WaveSpeed);
            float  a = Q / k;                   // amplitude

            // Analytical tangent / binormal accumulation
            tangent  += float3(-d.x*d.x*Q*sin(f),  d.x*Q*cos(f), -d.x*d.y*Q*sin(f));
            binormal += float3(-d.x*d.y*Q*sin(f),  d.y*Q*cos(f), -d.y*d.y*Q*sin(f));

            return float3(d.x*a*cos(f),  a*sin(f),  d.y*a*cos(f));
        }

        // ── Kelvin wake ───────────────────────────────────────────────────────
        float KelvinWake(float3 wPos, float t)
        {
            float2 toFrag = wPos.xz - _BoatPosition.xz;
            float2 fwd    = normalize(_BoatForward.xz + 1e-5);
            float2 perp   = float2(-fwd.y, fwd.x);
            float along   = dot(toFrag, -fwd);
            float across  = dot(toFrag,  perp);
            if (along < 0.5) return 0.0;
            float fade  = 1.0 - smoothstep(0.0, _WakeLength, along);
            float arms  = smoothstep(0.05, 0.0,
                          abs(abs(across) / max(along, 0.01) - 0.3545)) * fade;
            float mid   = smoothstep(max(1.5 - along*0.03, 0.3), 0.0,
                          abs(across)) * fade * 0.5;
            float ripple= sin(along*1.5 - t*3.5)*0.5 + 0.5;
            return saturate((arms + mid) * ripple * _WakeStrength);
        }

        // ── Surface input ─────────────────────────────────────────────────────
        struct Input
        {
            float3 worldPos;
            float4 screenPos;
            float2 uv_NormalTex;
            float3 viewDir;
            float  waveHeight;
        };

        // ── Boat displacement wave/ripple effect ──────────────────────────────
        float CalculateBoatDisplacement(float3 wPos)
        {
            if (_BoatSpeed <= 0.01) return 0.0;

            float2 toFrag = wPos.xz - _BoatPosition.xz;
            float2 fwd    = normalize(_BoatForward.xz + 1e-5);
            float2 perp   = float2(-fwd.y, fwd.x);

            float along  = dot(toFrag, fwd);     // positive in front, negative behind
            float across = abs(dot(toFrag, perp)); // lateral distance

            // Bow wave (pushes water up at the front hull)
            float bowWave = exp(-pow(along - 3.0, 2) / 4.0) * exp(-pow(across, 2) / 2.0) * 0.45;

            // V-wake displacement ridges propagating backwards
            float wakeLine = 0.354 * abs(along);
            float vWake = exp(-pow(across - wakeLine, 2) / 1.5) * exp(-abs(along) / 20.0) * 0.3;

            // Stern depression (sucks water down behind propeller/stern)
            float sternDep = -0.4 * exp(-pow(along + 2.0, 2) / 8.0) * exp(-pow(across, 2) / 1.5);

            return (bowWave + vWake + sternDep) * _BoatSpeed;
        }

        float GetWaveAmplitude(float4 wave)
        {
            float steepness = wave.z;
            float wavelength = wave.w;
            if (wavelength <= 0.0) return 0.0;
            return (steepness * wavelength) / (2.0 * 3.14159265);
        }

        void vert(inout appdata_full v, out Input o)
        {
            UNITY_INITIALIZE_OUTPUT(Input, o);

            float3 wPos    = mul(unity_ObjectToWorld, v.vertex).xyz;
            float3 tangent = float3(1, 0, 0);
            float3 binormal= float3(0, 0, 1);

            float3 disp  = GerstnerWave(_WaveA, wPos, tangent, binormal);
                   disp += GerstnerWave(_WaveB, wPos, tangent, binormal);
                   disp += GerstnerWave(_WaveC, wPos, tangent, binormal);

            // Increment Perlin noise for asymmetry
            float2 noiseCoord = v.vertex.xz * _NoiseScale + float2(_Time.y * _NoiseSpeed, _Time.y * _NoiseSpeed * 0.7);
            float3 noiseVal = perlinNoiseDeriv(noiseCoord);
            
            disp.y += noiseVal.x * _NoiseAmplitude;
            tangent.y += noiseVal.y * _NoiseAmplitude * _NoiseScale;
            binormal.y += noiseVal.z * _NoiseAmplitude * _NoiseScale;

            v.vertex.xyz += disp;

            // Apply boat-moving local displacement on top
            float3 worldPosWithWaves = mul(unity_ObjectToWorld, v.vertex).xyz;
            float boatDisp = CalculateBoatDisplacement(worldPosWithWaves);
            v.vertex.y += boatDisp;

            v.normal      = normalize(cross(binormal, tangent));

            o.screenPos = ComputeScreenPos(UnityObjectToClipPos(v.vertex));
            o.worldPos  = mul(unity_ObjectToWorld, v.vertex).xyz;

            // Calculate the theoretical maximum/minimum height of the waves
            float ampA = GetWaveAmplitude(_WaveA);
            float ampB = GetWaveAmplitude(_WaveB);
            float ampC = GetWaveAmplitude(_WaveC);
            float totalAmp = ampA + ampB + ampC + _NoiseAmplitude;

            float range = 2.0 * totalAmp;
            float heightOffset = v.vertex.y; // Local Y displacement (includes waves and boat)
            o.waveHeight = (range > 0.001) ? saturate((heightOffset + totalAmp) / range) : 0.5;
        }

        // ── Surface shader ────────────────────────────────────────────────────
        void surf(Input IN, inout SurfaceOutputStandard o)
        {
            float  t  = _Time.y;
            float2 wp = IN.worldPos.xz;

            // Two-layer scrolling normal map
            float3 nA = UnpackNormal(tex2D(_NormalTex, wp*0.04 + float2( 0.7, 0.3)*t*_NormalSpeed));
            float3 nB = UnpackNormal(tex2D(_NormalTex, wp*0.08 + float2(-0.4, 0.7)*t*_NormalSpeed*1.4));
            o.Normal   = lerp(float3(0,0,1), normalize(nA+nB), _NormalScale);

            // Depth-based colour
            float rawD  = SAMPLE_DEPTH_TEXTURE_PROJ(_CameraDepthTexture, UNITY_PROJ_COORD(IN.screenPos));
            float depth = saturate((LinearEyeDepth(rawD) - IN.screenPos.w) / _DepthFade);
            fixed4 col  = lerp(_ShallowColor, _DeepColor, depth);

            // Fresnel edge
            float NdotV  = saturate(dot(normalize(o.Normal), normalize(IN.viewDir)));
            float fresnel = pow(1.0 - NdotV, _FresnelPower);
            col.rgb = lerp(col.rgb, _HorizonColor.rgb, fresnel * 0.6);

            // Normalized wave height (0 = trough, 1 = crest)
            float heightFactor = IN.waveHeight;

            // Make ocean color variable to vertex height (brighter/more translucent at wave crests)
            float crestWeight = smoothstep(0.4, 0.8, heightFactor);
            col.rgb = lerp(col.rgb, _ShallowColor.rgb * 1.25, crestWeight * 0.45);

            // Crest foam (smoothstep based on normalized wave height)
            float foam = smoothstep(_FoamThreshold - _FoamSoftness,
                                    _FoamThreshold + _FoamSoftness,
                                    heightFactor);

            // Boat wake
            foam = saturate(foam + KelvinWake(IN.worldPos, t) * _BoatSpeed);

            col = lerp(col, _FoamColor, foam);

            o.Albedo     = col.rgb;
            o.Alpha      = lerp(col.a, 1.0, foam);
            o.Smoothness = _Smoothness * (1.0 - foam * 0.5);
            o.Metallic   = _Metallic;
        }
        ENDCG
    }

    FallBack "Transparent/Diffuse"
}
