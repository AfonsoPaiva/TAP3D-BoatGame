Shader "Custom/SeaweedGeometry"
{
    Properties
    {
        [Header(Color Settings)]
        _BaseColor ("Base Color", Color) = (0.10, 0.38, 0.18, 0.95)
        _TipColor ("Tip Color", Color) = (0.28, 0.70, 0.32, 0.90)
        
        [Header(Dimension Settings)]
        _SeaweedHeight ("Seaweed Height", Float) = 2.4
        _SeaweedWidth ("Seaweed Width", Float) = 0.18
        
        [Header(Density Settings)]
        _Density ("Density (Lower Sparser - Higher Denser)", Range(0, 1)) = 0.65
        
        [Header(Sway Animation Settings)]
        _SwaySpeed ("Sway Speed", Float) = 1.6
        _SwayAmplitude ("Sway Amplitude", Float) = 0.35
    }
    
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" }
        LOD 200
        
        Cull Off
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite On

        Pass
        {
            Tags { "LightMode"="ForwardBase" }

            CGPROGRAM
            #pragma vertex vert
            #pragma geometry geom
            #pragma fragment frag
            #pragma target 4.0
            #pragma multi_compile_fwdbase
            #include "UnityCG.cginc"
            #include "Lighting.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                float2 uv     : TEXCOORD0;
            };

            struct v2g
            {
                float4 worldPos : TEXCOORD1; // Absolute world position
                float3 normal   : NORMAL;
                float2 uv       : TEXCOORD0;
            };

            struct g2f
            {
                float4 pos    : SV_POSITION; // Final projected clip-space position
                float2 uv     : TEXCOORD0;   // x = horizontal offset, y = height progress
                float3 normal : TEXCOORD1;    // World-space normal for lighting
            };

            fixed4 _BaseColor;
            fixed4 _TipColor;
            
            float _SeaweedHeight;
            float _SeaweedWidth;
            float _Density;
            float _SwaySpeed;
            float _SwayAmplitude;

            // Deterministic pseudo-random hash
            float rand(float3 pos)
            {
                return frac(sin(dot(pos, float3(12.9898, 78.233, 45.164))) * 43758.5453123);
            }

            v2g vert(appdata v)
            {
                v2g o;
                o.worldPos = mul(unity_ObjectToWorld, v.vertex);
                o.normal = UnityObjectToWorldNormal(v.normal);
                o.uv = v.uv;
                return o;
            }

            // Generates a simple, elegant single-quad blade (4 vertices total)
            [maxvertexcount(4)]
            void geom(triangle v2g input[3], inout TriangleStream<g2f> outStream)
            {
                // Calculate triangle center in absolute World Space
                float3 centerWorld = (input[0].worldPos.xyz + input[1].worldPos.xyz + input[2].worldPos.xyz) / 3.0;

                // Prune using the density threshold
                float densityHash = rand(centerWorld);
                if (densityHash > _Density)
                    return;

                float bladeHeight = _SeaweedHeight;
                float bladeWidth = _SeaweedWidth;

                // Sway offset calculations
                float timeFactor = _Time.y * _SwaySpeed;
                float wavePhase = centerWorld.x * 0.35 + centerWorld.z * 0.35;
                float swayX = sin(timeFactor + wavePhase) * _SwayAmplitude;
                float swayZ = cos(timeFactor * 0.75 + wavePhase) * _SwayAmplitude * 0.5;

                // Random orientation around Y axis
                float rotAngle = rand(centerWorld * 3.7) * 6.2831853;
                float cosA = cos(rotAngle);
                float sinA = sin(rotAngle);
                float3 right = float3(cosA, 0.0, sinA);

                // Compute face normal (perpendicular to blade face)
                float3 up = float3(0.0, 1.0, 0.0);
                float3 faceNormal = normalize(cross(up, right));

                // --- LEVEL 0 (Base) ---
                g2f oBaseLeft;
                float3 posBaseLeft = centerWorld - right * bladeWidth;
                oBaseLeft.pos = UnityWorldToClipPos(posBaseLeft);
                oBaseLeft.uv = float2(-1.0, 0.0);
                oBaseLeft.normal = faceNormal;
                outStream.Append(oBaseLeft);

                g2f oBaseRight;
                float3 posBaseRight = centerWorld + right * bladeWidth;
                oBaseRight.pos = UnityWorldToClipPos(posBaseRight);
                oBaseRight.uv = float2(1.0, 0.0);
                oBaseRight.normal = faceNormal;
                outStream.Append(oBaseRight);

                // --- LEVEL 1 (Tip) ---
                float3 swayOffset = float3(swayX, 0.0, swayZ);

                g2f oTipLeft;
                float3 posTipLeft = centerWorld - right * bladeWidth * 0.1 + float3(0, bladeHeight, 0) + swayOffset;
                oTipLeft.pos = UnityWorldToClipPos(posTipLeft);
                oTipLeft.uv = float2(-0.1, 1.0);
                oTipLeft.normal = faceNormal;
                outStream.Append(oTipLeft);

                g2f oTipRight;
                float3 posTipRight = centerWorld + right * bladeWidth * 0.1 + float3(0, bladeHeight, 0) + swayOffset;
                oTipRight.pos = UnityWorldToClipPos(posTipRight);
                oTipRight.uv = float2(0.1, 1.0);
                oTipRight.normal = faceNormal;
                outStream.Append(oTipRight);

                outStream.RestartStrip();
            }

            fixed4 frag(g2f i) : SV_Target
            {
                // Smooth translucent base-to-tip color gradient
                fixed4 col = lerp(_BaseColor, _TipColor, i.uv.y);

                // Volumetric border shading
                float edgeFactor = abs(i.uv.x);
                col.rgb *= (1.0 - edgeFactor * 0.22);

                // Simple ambient + directional lighting
                float3 n = normalize(i.normal);
                float3 lightDir = normalize(_WorldSpaceLightPos0.xyz);
                float NdotL = saturate(dot(n, lightDir) * 0.5 + 0.5); // Wrapped diffuse
                float3 ambient = ShadeSH9(float4(n, 1.0));
                col.rgb *= ambient + _LightColor0.rgb * NdotL;

                return col;
            }
            ENDCG
        }
    }
    FallBack "Diffuse"
}
