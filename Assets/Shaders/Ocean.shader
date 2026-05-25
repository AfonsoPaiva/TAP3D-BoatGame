Shader "Custom/Ocean"
{
    Properties
    {
        // --- Normal map ---
        _NormalTex      ("Normal Map",          2D)           = "bump"  {}
        _NormalStrength ("Normal Strength",     Range(0, 3))  = 1.0
        _NormalSpeed    ("Normal Scroll Speed", Float)        = 0.04

        // --- Colors ---
        _ShallowColor   ("Shallow Color",       Color) = (0.10, 0.55, 0.70, 0.85)
        _DeepColor      ("Deep Color",          Color) = (0.02, 0.10, 0.35, 1.00)
        _DepthMaxDist   ("Depth Fade Distance", Float) = 8.0
        _HorizonColor   ("Horizon / Fresnel Color", Color) = (0.72, 0.88, 1.0, 1.0)

        // --- Foam ---
        _FoamColor            ("Foam Color",           Color)          = (1, 1, 1, 1)
        _FoamThreshold        ("Crest Foam Threshold", Range(-1, 2))   = 0.35
        _FoamSoftness         ("Crest Foam Softness",  Range(0.01, 1)) = 0.20
        _ContactFoamWidth     ("Contact Foam Width",   Float)          = 0.4
        _ContactFoamSharpness ("Contact Foam Sharpness", Range(0.01, 1)) = 0.3

        // --- PBR ---
        _Smoothness   ("Smoothness",    Range(0, 1))   = 0.92
        _Metallic     ("Metallic",      Range(0, 1))   = 0.0
        _FresnelPower ("Fresnel Power", Range(0.5, 8)) = 4.0
        _FresnelBias  ("Fresnel Bias",  Range(0, 1))   = 0.02

        // --- Subsurface scatter ---
        _SubsurfaceColor    ("Subsurface Color",    Color)       = (0.05, 0.45, 0.5, 1)
        _SubsurfaceStrength ("Subsurface Strength", Range(0, 1)) = 0.4

        // --- Wake ---
        _WakeStrength ("Wake Foam Strength", Range(0, 1)) = 0.6
        _WakeLength   ("Wake Fade Length",   Float)       = 40.0
    }

    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" }
        LOD 200
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off

        CGPROGRAM
        #pragma surface surf Standard alpha:fade
        #pragma target 3.0
        #include "UnityCG.cginc"

        // ── Uniforms ──────────────────────────────────────────────────────
        sampler2D _NormalTex;
        float  _NormalStrength, _NormalSpeed;

        fixed4 _ShallowColor, _DeepColor, _HorizonColor;
        float  _DepthMaxDist;

        fixed4 _FoamColor;
        float  _FoamThreshold, _FoamSoftness;
        float  _ContactFoamWidth, _ContactFoamSharpness;

        float  _Smoothness, _Metallic;
        float  _FresnelPower, _FresnelBias;

        fixed4 _SubsurfaceColor;
        float  _SubsurfaceStrength;

        float  _WakeStrength, _WakeLength;

        sampler2D _CameraDepthTexture;

        // ── Boat globals (set by WaterManager / OceanBoatInteraction) ────
        float4 _BoatPosition;
        float4 _BoatForward;
        float  _BoatSpeed;

        // ── Kelvin wake ───────────────────────────────────────────────────
        float KelvinWake(float3 worldPos, float t)
        {
            float2 toFrag = worldPos.xz - _BoatPosition.xz;
            float2 fwd    = normalize(_BoatForward.xz + float2(0.0001, 0.0001));
            float2 perp   = float2(-fwd.y, fwd.x);

            float along  = dot(toFrag, -fwd);
            float across = dot(toFrag,  perp);

            if (along < 0.5) return 0.0;

            float distFade = 1.0 - smoothstep(0.0, _WakeLength, along);

            float kelvinRatio = 0.3545;
            float armDist = abs(abs(across) / max(along, 0.01) - kelvinRatio);
            float arms = smoothstep(0.05, 0.0, armDist) * distFade;

            float centralWidth = max(1.5 - along * 0.03, 0.3);
            float central = smoothstep(centralWidth, 0.0, abs(across)) * distFade * 0.5;

            float ripple = sin(along * 1.5 - t * 3.5) * 0.5 + 0.5;
            return saturate((arms + central) * ripple * _WakeStrength);
        }

        // ── Input ─────────────────────────────────────────────────────────
        struct Input
        {
            float3 worldPos;
            float4 screenPos;
            float2 uv_NormalTex;
            float3 viewDir;
        };

        // ── Surface ───────────────────────────────────────────────────────
        void surf(Input IN, inout SurfaceOutputStandard o)
        {
            float t  = _Time.y;
            float2 wp = IN.worldPos.xz;

            // ── Three normal-map layers scrolling in organic, irrational directions ──
            // Layer A: large, slow
            float2 uvA = wp * 0.03 + float2(0.8, 0.6) * (t * _NormalSpeed);
            float3 nA  = UnpackNormal(tex2D(_NormalTex, uvA));

            // Layer B: medium
            float2 uvB = wp * 0.07 + float2(-0.7, 0.7) * (t * _NormalSpeed * 1.3);
            float3 nB  = UnpackNormal(tex2D(_NormalTex, uvB));

            // Layer C: fine detail
            float2 uvC = wp * 0.14 + float2(0.9, -0.4) * (t * _NormalSpeed * 1.7);
            float3 nC  = UnpackNormal(tex2D(_NormalTex, uvC));

            float3 blendN = normalize(nA + nB + nC);
            o.Normal = lerp(float3(0, 0, 1), blendN, _NormalStrength);

            // ── Depth ─────────────────────────────────────────────────────
            float rawDepth   = SAMPLE_DEPTH_TEXTURE_PROJ(_CameraDepthTexture, UNITY_PROJ_COORD(IN.screenPos));
            float sceneDepth = LinearEyeDepth(rawDepth);
            float surfDepth  = IN.screenPos.w;
            float depthDiff  = saturate((sceneDepth - surfDepth) / _DepthMaxDist);

            // ── Base water colour ─────────────────────────────────────────
            fixed4 waterColor = lerp(_ShallowColor, _DeepColor, depthDiff);

            // ── Fresnel ───────────────────────────────────────────────────
            float NdotV  = saturate(dot(normalize(o.Normal), normalize(IN.viewDir)));
            float fresnel = _FresnelBias + (1.0 - _FresnelBias) * pow(1.0 - NdotV, _FresnelPower);
            waterColor.rgb = lerp(waterColor.rgb, _HorizonColor.rgb, fresnel * 0.65);

            // ── Subsurface scatter ────────────────────────────────────────
            float sss = saturate(1.0 - depthDiff) * pow(NdotV, 1.5);
            waterColor.rgb += _SubsurfaceColor.rgb * _SubsurfaceStrength * sss;

            // ── Contact foam ──────────────────────────────────────────────
            float contactDepth = sceneDepth - surfDepth;
            float contactFoam  = 1.0 - smoothstep(0.0, _ContactFoamWidth, contactDepth);
            contactFoam = pow(contactFoam, 1.0 + _ContactFoamSharpness * 4.0);

            // ── Crest foam (based on vertex height) ───────────────────────
            float crestFoam = smoothstep(_FoamThreshold - _FoamSoftness,
                                         _FoamThreshold + _FoamSoftness,
                                         IN.worldPos.y);

            // ── Kelvin wake ───────────────────────────────────────────────
            float wake = KelvinWake(IN.worldPos, t) * _BoatSpeed;

            // ── Combine ───────────────────────────────────────────────────
            float totalFoam = saturate(contactFoam + crestFoam + wake);
            waterColor = lerp(waterColor, _FoamColor, totalFoam);

            // ── Outputs ───────────────────────────────────────────────────
            o.Albedo     = waterColor.rgb;
            o.Alpha      = lerp(waterColor.a, 1.0, totalFoam);
            o.Smoothness = _Smoothness * (1.0 - totalFoam * 0.6);
            o.Metallic   = _Metallic;
        }
        ENDCG
    }

    FallBack "Transparent/Diffuse"
}
