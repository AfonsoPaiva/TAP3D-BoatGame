Shader "Hidden/CaveLighting"
{
    Properties
    {
        _MainTex     ("Texture",       2D)            = "white" {}
        _BoatScreenPos("Boat Screen Pos", Vector)     = (0.5, 0.5, 0, 0)
        _Darkness    ("Darkness",      Range(0, 1))   = 0.9
        _LightRadius ("Light Radius",  Float)         = 0.3
        _AspectRatio ("Aspect Ratio",  Float)         = 1.77
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
            float     _Darkness;
            float     _LightRadius;
            float     _AspectRatio;

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

                // Distância do píxel ao barco (corrigida pelo aspect ratio)
                float2 diff = i.uv - _BoatScreenPos.xy;
                diff.x *= _AspectRatio;
                float dist = length(diff);

                // Máscara de luz circular suave (1 no centro, 0 na borda)
                float lightMask = 1.0 - smoothstep(0.0, _LightRadius, dist);
                lightMask = lightMask * lightMask; // quadrático para falloff mais natural

                // Escuridão = _Darkness no exterior da luz, 0 no centro da luz
                float shadow = _Darkness * (1.0 - lightMask);

                // Aplica a escuridão
                col.rgb *= (1.0 - shadow);

                return col;
            }
            ENDCG
        }
    }
}
