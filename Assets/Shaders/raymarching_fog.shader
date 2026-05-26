Shader "Unlit/raymarching_fog"
{
    Properties
    {
        _MainTex ("MainTex", 2D) = "white" {}
        _Color("Color", Color) = (1, 1, 1, 1)
        _MaxDistance("Max distance", float) = 100
        _StepSize("Step size", Range(0.1, 20)) = 1
        _DensityMultiplier("Density multiplier", Range(0, 10)) = 1
        _NoiseOffset("Noise offset", float) = 0
        
        _FogNoise("Fog noise", 3D) = "white" {}
        _NoiseTiling("Noise tiling", float) = 1
        _DensityThreshold("Density threshold", Range(0, 1)) = 0.1
        
        [HDR]_LightContribution("Light contribution", Color) = (1, 1, 1, 1)
        _LightScattering("Light scattering", Range(0, 1)) = 0.2

        _HeightFalloff("Height falloff", float) = 0.5
        _BaseHeight("Base height", float) = 0.0
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        LOD 100

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "UnityCG.cginc"
            #include "Lighting.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float2 uv : TEXCOORD0;
                float4 vertex : SV_POSITION;
                float3 viewRay : TEXCOORD1;
            };

            sampler2D _MainTex;
            sampler3D _FogNoise;
            sampler2D _CameraDepthTexture;  

            float4 _Color;
            float _MaxDistance;
            float _StepSize;
            float _DensityMultiplier;
            float _NoiseOffset;
            float _NoiseTiling;
            float _DensityThreshold;
            float4 _LightContribution;
            float _LightScattering;   
            float _HeightFalloff;
            float _BaseHeight;

            float henyey_greenstein(float angle, float scattering)
            {
                return (1.0 - angle * angle) / (4.0 * 3.14159265358979 * pow(1.0 + scattering * scattering - (2.0 * scattering) * angle, 1.5f));
            }

            //replacement for InterleavedGradientNoise
            float hash(float2 p) {
                return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453);
            }    

            v2f vert(appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;

                // Reconstruct view-space ray from clip position
                float4 clipPos = float4(v.uv * 2.0 - 1.0, 1.0, 1.0);
                float4 viewPos = mul(unity_CameraInvProjection, clipPos);
                viewPos.xyz /= viewPos.w;
                o.viewRay = mul((float3x3)unity_CameraToWorld, viewPos.xyz);

                return o;
            }
            fixed4 frag (v2f i) : SV_Target
            {
                // sample the texture
                fixed4 col = tex2D(_MainTex, i.uv);
                float depth = LinearEyeDepth(tex2D(_CameraDepthTexture, i.uv).r);

                float3 worldPos = _WorldSpaceCameraPos + normalize(i.viewRay) * depth;
                float3 rayDir = normalize(i.viewRay);

                float viewLen = length(worldPos - _WorldSpaceCameraPos);
                
                float jitter = hash(i.uv * _ScreenParams.xy + _Time.y);
                float distanceTraveled = jitter * _NoiseOffset;
                float distanceLimit = min(viewLen, _MaxDistance);

                float transmittance = 1.0;
                float4 fogColor = _Color;

                [loop]
                while(distanceTraveled < distanceLimit){
                    float3 rayPosition = _WorldSpaceCameraPos + rayDir * distanceTraveled;
                    float heightFactor = exp(-max(0.0, rayPosition.y - _BaseHeight) * _HeightFalloff);
                    float density = _DensityMultiplier * tex3D(_FogNoise, rayPosition * _NoiseTiling + _Time.y).r * heightFactor;
                    
                    if(density > _DensityThreshold){
                        fogColor.rgb += _LightColor0.rgb * _LightContribution.rgb * density * _LightScattering * henyey_greenstein(dot(rayDir, _WorldSpaceLightPos0.xyz), _LightScattering);
                        transmittance *= exp(-density * _StepSize);
                    }
                    distanceTraveled += _StepSize;
                }
                
                return lerp(col, fogColor, 1.0 - saturate(transmittance));
            }
            ENDCG
        }
    }
}
