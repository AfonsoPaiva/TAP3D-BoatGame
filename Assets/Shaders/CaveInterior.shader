Shader "Custom/CaveInterior"
{
    Properties
    {
        [Header(Triplanar Textures)]
        _BaseColor      ("Base Color",          Color)        = (0.5, 0.5, 0.5, 1)
        _SideNormal     ("Side Normal",         2D)           = "bump"  {}
        _TopNormal      ("Top / Ceil Normal",   2D)           = "bump"  {}

        [Header(Triplanar Settings)]
        _TextureScale   ("Texture Scale",       Float)        = 0.1
        _BlendSharpness ("Blend Sharpness",     Range(1, 50)) = 8.0

        [Header(PBR)]
        _Smoothness     ("Smoothness",          Range(0,1))   = 0.15
        _Metallic       ("Metallic",            Range(0,1))   = 0.0
        _AmbientBoost   ("Ambient Boost",       Range(0,1))   = 0.04

        [Header(Proximity Reveal)]
        // Posicao mundo do barco (atualizada pelo CaveInteriorReveal.cs)
        _BoatWorldPos   ("Boat World Pos",      Vector)       = (0,0,0,0)

        // Raio (em unidades mundo) dentro do qual a textura e visivel
        _RevealRadius   ("Reveal Radius",       Float)        = 20.0

        // Zona de transicao (falloff) em unidades mundo apos _RevealRadius
        _RevealFalloff  ("Reveal Falloff",      Float)        = 12.0

        // 0 = sem efeito (fora da gruta), 1 = efeito activo (dentro)
        // Controla o fade-in/out global do reveal ao entrar/sair da gruta
        _RevealIntensity("Reveal Intensity",    Range(0,1))   = 0.0

        // Cor da escuridao (normalmente preto puro)
        _DarkColor      ("Dark Color",          Color)        = (0,0,0,1)

        [Header(Stencil Hole)]
        [IntRange] _StencilRef ("Stencil Reference", Range(0, 255)) = 1
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" }
        Cull Back
        LOD 300

        // Descartar fragmentos onde a mascara escreveu
        Stencil
        {
            Ref  [_StencilRef]
            Comp NotEqual
            Pass Keep
        }

        CGPROGRAM
        #pragma surface surf Standard fullforwardshadows
        #pragma target 3.0

        float4 _BaseColor;
        sampler2D _SideNormal;
        sampler2D _TopNormal;

        float _TextureScale;
        float _BlendSharpness;
        float _Smoothness;
        float _Metallic;
        float _AmbientBoost;

        float4 _BoatWorldPos;
        float  _RevealRadius;
        float  _RevealFalloff;
        float  _RevealIntensity;
        float4 _DarkColor;

        struct Input
        {
            float3 worldPos;
            float3 worldNormal;
            INTERNAL_DATA
        };

        void surf(Input IN, inout SurfaceOutputStandard o)
        {
            // ------- Triplanar blend weights --------
            // como a gruta tem formas irregulares isto faz com que as texturas nao fiquem esticadas
            float3 N = WorldNormalVector(IN, o.Normal);
            float3 w = abs(N);
            w = pow(max(w, 0.0001), _BlendSharpness);
            w /= (w.x + w.y + w.z);   // normaliza para que a soma seja 1

            // ------- UVs triplanares --------
            float2 uvX = IN.worldPos.zy * _TextureScale;
            float2 uvY = IN.worldPos.xz * _TextureScale;
            float2 uvZ = IN.worldPos.xy * _TextureScale;

            // ------- Albedo --------
            float3 albedo = _BaseColor.rgb;

            // ------- Normais --------
            float3 nX = UnpackNormal(tex2D(_SideNormal, uvX));
            float3 nY = UnpackNormal(tex2D(_TopNormal,  uvY));
            float3 nZ = UnpackNormal(tex2D(_SideNormal, uvZ));
            float3 blendedN = normalize(nX * w.x + nY * w.y + nZ * w.z);

            // ------- Reveal por proximidade ao barco --------
            // para o reveal usa o _RevealIntensity do script cainterior.reveal.cs

            // Distancia 3D deste fragmento ao barco (espaco mundo)
            float distToBoat = distance(IN.worldPos, _BoatWorldPos.xyz);

            // t = 1 dentro do raio, 0 fora do raio + falloff
            float proximity = 1.0 - smoothstep(_RevealRadius,
                                                _RevealRadius + _RevealFalloff,
                                                distToBoat);
            proximity = proximity * proximity * (3.0 - 2.0 * proximity);

            // fade-in ao entrar na gruta
            float t = proximity * _RevealIntensity;
            float3 finalAlbedo = lerp(_DarkColor.rgb, albedo, t);

            // ------- Output --------
            o.Albedo     = finalAlbedo;
            o.Normal     = blendedN;
            o.Metallic   = _Metallic;
            o.Smoothness = _Smoothness;
            o.Alpha      = 1.0;

            // Emissao subtil apenas na zona iluminada fica melhor com o halo da luz 
            o.Emission = albedo * (_AmbientBoost * t);
        }
        ENDCG
    }

    FallBack "Diffuse"
}
