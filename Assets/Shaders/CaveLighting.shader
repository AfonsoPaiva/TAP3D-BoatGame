Shader "Hidden/CaveLighting"
{
    Properties
    {
        _MainTex        ("Texture",          2D)           = "white" {}
        _BoatScreenPos  ("Boat Screen Pos",  Vector)       = (0.5, 0.5, 0, 0)
        _LightRadius    ("Light Radius",     Float)        = 0.3
        _LightSoftness  ("Light Softness",   Float)        = 0.15
        _AspectRatio    ("Aspect Ratio",     Float)        = 1.77
        _EffectIntensity("Effect Intensity", Range(0,1))   = 0.0
        _Darkness       ("Darkness",         Range(0,1))   = 0.92
        _MinBrightness  ("Min Brightness",   Range(0,1))   = 0.08
    }

    SubShader
    {
        Cull Off ZWrite Off ZTest Always

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv     : TEXCOORD0;
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                float2 uv     : TEXCOORD0;
            };

            sampler2D _MainTex;
            float4    _BoatScreenPos;
            float     _LightRadius;
            float     _LightSoftness;
            float     _AspectRatio;
            float     _EffectIntensity;
            float     _Darkness;
            float     _MinBrightness;

            v2f vert (appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv     = v.uv;
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                // Cor original da camara
                fixed4 col = tex2D(_MainTex, i.uv);

                // Sai cedo se o efeito nao esta activo
                if (_EffectIntensity <= 0.001)
                    return col;

                // -------------------------------------------------------
                // PASSO 1 - Escuridao ambiente uniforme em todo o ecra
                // Escurece TODO o ecra proporcionalmente a _EffectIntensity.
                // Nunca cria um "circulo preto" porque e aplicado de forma
                // uniforme antes de qualquer mascara de luz.
                // -------------------------------------------------------
                float ambientDark = _Darkness * _EffectIntensity;

                // -------------------------------------------------------
                // PASSO 2 - Halo de luz a volta do barco
                // Raio FIXO (_LightRadius) - so a intensidade varia com
                // _EffectIntensity. Evita o artefacto de circulo negro
                // a crescer a partir de zero.
                // -------------------------------------------------------
                float2 diff = i.uv - _BoatScreenPos.xy;
                diff.x *= _AspectRatio;
                float dist = length(diff);

                // Halo suave: 1 perto do barco, 0 fora do raio
                float halo = 1.0 - smoothstep(_LightRadius,
                                               _LightRadius + _LightSoftness,
                                               dist);
                // Curva cubica para falloff sem aresta dura
                halo = halo * halo * (3.0 - 2.0 * halo);
                // O halo modula com a intensidade (aparece gradualmente)
                halo *= _EffectIntensity;

                // -------------------------------------------------------
                // PASSO 3 - Combinar
                // A escuridao ambiente reduz-se na zona iluminada pelo halo.
                // _MinBrightness garante que nunca fica totalmente preto
                // (luz ambiente minima da gruta).
                // -------------------------------------------------------
                float darknessApplied = ambientDark * (1.0 - halo);

                // Factor de brilho final: nunca desce abaixo de _MinBrightness
                float brightnessFactor = max(1.0 - darknessApplied, _MinBrightness);

                col.rgb *= brightnessFactor;

                return col;
            }
            ENDCG
        }
    }
}
