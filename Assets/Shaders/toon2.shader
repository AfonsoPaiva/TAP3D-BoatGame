Shader "ToonWOutline"
{
    Properties
    {
        _Color("Color", Color) = (1,1,1,1)
        _MainTex("Main Texture", 2D) = "white" {}

        _Glossiness("Glossiness", Float) = 32

        _RimAmount ("Rim Amount", Range(0,1)) = 0.7
        _RimThreshold ("Rim Threshold", Range(0,1)) = 0.1

        _OutlineColor("Outline Color", Color) = (0,0,0,1)
        _OutlineThickness("Outline Thickness", Float) = 0.02

        _HatchTex("Hatch Texture", 2D) = "white" {}
        _HatchScale("Hatch Scale", Float) = 5
    }
    SubShader
    {
        //OUTLINE
        Pass
        {
            Name "Outline"

            Cull Front
            ZWrite On

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
            };

            float _OutlineThickness;
            float4 _OutlineColor;

            v2f vert(appdata v)
            {
                v2f o;

                float3 normal = normalize(mul((float3x3)UNITY_MATRIX_IT_MV, v.normal));

                float4 pos = mul(UNITY_MATRIX_MV, v.vertex);

                pos.xyz += normal * _OutlineThickness;

                o.pos = mul(UNITY_MATRIX_P, pos);

                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                return _OutlineColor;
            }

            ENDCG
        }
    Pass
    {
        Tags { "RenderType"="Opaque" }
        LOD 200

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fwdbase

            #include "UnityCG.cginc"
            #include "Lighting.cginc"
            #include "AutoLight.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 worldNormal : NORMAL;
                float2 uv : TEXCOORD0;
                float3 viewDir : TEXCOORD1;

                SHADOW_COORDS(2)
            };

            sampler2D _MainTex;
            sampler2D _HatchTex;
            float _HatchScale;
            float4 _Color;
            float _Glossiness;
            float _RimAmount;
            float _RimThreshold;
            float4 _MainTex_ST;

            v2f vert (appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.worldNormal = UnityObjectToWorldNormal(v.normal);

                o.viewDir = WorldSpaceViewDir(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);

                TRANSFER_SHADOW(o)

                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                //make colors dependent on the main color
                float4 ambientColor = _Color * 0.4;
                float4 rimColor = saturate(_Color * 1.3);
                float4 specularColor = lerp(float4(1,1,1,1), _Color, 0.2);

                float3 normal = normalize(i.worldNormal);
                float3 viewDir = normalize(i.viewDir);

                float NdotL = dot(_WorldSpaceLightPos0, normal);

                float shadow = SHADOW_ATTENUATION(i);

                float lightIntensity = smoothstep(0, 0.01, NdotL * shadow);

                float4 light = lightIntensity * _LightColor0;

                float3 halfVector = normalize(_WorldSpaceLightPos0 + viewDir);

                float NdotH = dot(normal, halfVector);

                float specularIntensity =
                    pow(NdotH * lightIntensity,
                    _Glossiness * _Glossiness);

                float specularIntensitySmooth =
                    smoothstep(0.005, 0.01, specularIntensity);

                float4 specular =
                    specularIntensitySmooth * specularColor;

                float rimDot = 1 - dot(viewDir, normal);

                float rimIntensity =
                    rimDot * pow(NdotL, _RimThreshold);

                rimIntensity =
                    smoothstep(
                        _RimAmount - 0.01,
                        _RimAmount + 0.01,
                        rimIntensity
                    );

                float4 rim = rimIntensity * rimColor;

                float4 sample = tex2D(_MainTex, i.uv);

                float hatch = tex2D(_HatchTex, i.uv * _HatchScale).r;
                float hatchAmount = 1 - lightIntensity;
                float3 hatchColor = lerp(float3(1,1,1), float3(hatch, hatch, hatch), hatchAmount);

                return (light + ambientColor + specular + rim) * _Color * sample * float4(hatchColor, 1);
            }
            ENDCG
        }
        UsePass "Legacy Shaders/VertexLit/SHADOWCASTER"
    }
}