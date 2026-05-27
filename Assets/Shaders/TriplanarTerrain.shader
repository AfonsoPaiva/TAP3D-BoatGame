Shader "Custom/TriplanarTerrain"
{
    Properties
    {
        [Header(Top Texture)]
        _TopTex ("Top Albedo", 2D) = "white" {}

        [Header(Side Texture)]
        _SideTex ("Side Albedo", 2D) = "white" {}

        [Header(Triplanar Settings)]
        _TextureScale ("Texture Scale", Float) = 0.2
        _BlendSharpness ("Blend Sharpness", Range(1, 50)) = 10.0
        _HeightBlendStrength ("Height Blend Strength", Range(0, 1)) = 0.5

        [Header(PBR Settings)]
        _Smoothness ("Smoothness", Range(0, 1)) = 0.1
        _Metallic ("Metallic", Range(0, 1)) = 0.0
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        LOD 200

        CGPROGRAM
        #pragma surface surf Standard fullforwardshadows
        #pragma target 3.0

        sampler2D _TopTex;
        sampler2D _SideTex;

        float _TextureScale;
        float _BlendSharpness;
        float _HeightBlendStrength;
        float _Smoothness;
        float _Metallic;

        struct Input
        {
            float3 worldPos;
            float3 worldNormal;
            INTERNAL_DATA
        };

        float Luminance(float3 c)
        {
            return dot(c, float3(0.299, 0.587, 0.114));
        }

        void surf (Input IN, inout SurfaceOutputStandard o)
        {
            float3 worldNormal = WorldNormalVector(IN, o.Normal);

            float3 blendWeights = abs(worldNormal);
            blendWeights = pow(blendWeights, _BlendSharpness);
            blendWeights /= (blendWeights.x + blendWeights.y + blendWeights.z);

            float2 uvX = IN.worldPos.zy * _TextureScale;
            float2 uvY = IN.worldPos.xz * _TextureScale;
            float2 uvZ = IN.worldPos.xy * _TextureScale;

            float4 colX = tex2D(_SideTex, uvX);
            float4 colZ = tex2D(_SideTex, uvZ);

            // Y axis: top face uses TopTex, bottom face uses SideTex.
            float4 colYTop = tex2D(_TopTex,  uvY);
            float4 colYBot = tex2D(_SideTex, uvY);
            float topFace  = step(0.0, worldNormal.y);
            float4 colY    = lerp(colYBot, colYTop, topFace);

            // Height-based blending via luminance to hide triplanar seams.
            float hX = Luminance(colX.rgb);
            float hY = Luminance(colY.rgb);
            float hZ = Luminance(colZ.rgb);

            float3 w = blendWeights + float3(hX, hY, hZ) * _HeightBlendStrength;
            w = max(w, 0.0001);
            w /= (w.x + w.y + w.z);

            o.Albedo     = (colX * w.x + colY * w.y + colZ * w.z).rgb;
            o.Metallic   = _Metallic;
            o.Smoothness = _Smoothness;
            o.Alpha      = 1.0;
        }
        ENDCG
    }
    FallBack "Diffuse"
}