Shader "Custom/TriplanarTerrain"
{
    Properties
    {
        [Header(Top Texture)]
        _TopTex ("Top Albedo", 2D) = "white" {}
        _TopNormal ("Top Normal", 2D) = "bump" {}
        
        [Header(Side Texture)]
        _SideTex ("Side Albedo", 2D) = "white" {}
        _SideNormal ("Side Normal", 2D) = "bump" {}

        [Header(Triplanar Settings)]
        _TextureScale ("Texture Scale", Float) = 0.2
        _BlendSharpness ("Blend Sharpness", Range(1, 50)) = 10.0
        
        [Header(PBR Settings)]
        _Smoothness ("Smoothness", Range(0, 1)) = 0.1
        _Metallic ("Metallic", Range(0, 1)) = 0.0
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        LOD 200

        CGPROGRAM
        // Physically based Standard lighting model, and enable shadows on all light types
        #pragma surface surf Standard fullforwardshadows

        // Use shader model 3.0 target, to get nicer looking lighting
        #pragma target 3.0

        sampler2D _TopTex;
        sampler2D _TopNormal;
        sampler2D _SideTex;
        sampler2D _SideNormal;

        float _TextureScale;
        float _BlendSharpness;
        float _Smoothness;
        float _Metallic;

        struct Input
        {
            float3 worldPos;
            float3 worldNormal;
            INTERNAL_DATA
        };

        void surf (Input IN, inout SurfaceOutputStandard o)
        {
            // Obter a normal do mundo (baseada na geometria do modelo)
            float3 worldNormal = WorldNormalVector(IN, o.Normal);
            
            // Calcular os pesos de mistura (Blend Weights) com base na direção da normal
            float3 blendWeights = abs(worldNormal);
            // Aumentar a nitidez da transição
            blendWeights = pow(blendWeights, _BlendSharpness);
            // Normalizar para que a soma seja 1
            blendWeights /= (blendWeights.x + blendWeights.y + blendWeights.z);

            // Escalar as coordenadas do mundo para o tamanho da textura
            float2 uvX = IN.worldPos.zy * _TextureScale;
            float2 uvY = IN.worldPos.xz * _TextureScale;
            float2 uvZ = IN.worldPos.xy * _TextureScale;

            // --- ALBEDO ---
            // Y é o topo/baixo, X e Z são as laterais
            float4 colX = tex2D(_SideTex, uvX);
            float4 colY = tex2D(_TopTex,  uvY);
            float4 colZ = tex2D(_SideTex, uvZ);
            
            float4 albedo = colX * blendWeights.x + colY * blendWeights.y + colZ * blendWeights.z;

            // --- NORMALS ---
            float4 normX = tex2D(_SideNormal, uvX);
            float4 normY = tex2D(_TopNormal,  uvY);
            float4 normZ = tex2D(_SideNormal, uvZ);
            
            // Descomprimir normais
            float3 nX = UnpackNormal(normX);
            float3 nY = UnpackNormal(normY);
            float3 nZ = UnpackNormal(normZ);

            // Misturar normais (método simplificado)
            float3 blendedNormal = nX * blendWeights.x + nY * blendWeights.y + nZ * blendWeights.z;

            // Atribuir valores finais
            o.Albedo = albedo.rgb;
            o.Normal = normalize(blendedNormal);
            o.Metallic = _Metallic;
            o.Smoothness = _Smoothness;
            o.Alpha = 1.0;
        }
        ENDCG
    }
    FallBack "Diffuse"
}
