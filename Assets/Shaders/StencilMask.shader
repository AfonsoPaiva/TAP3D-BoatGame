Shader "Custom/StencilMask"
{
    Properties
    {
        [IntRange] _StencilRef ("Stencil Reference", Range(0, 255)) = 1
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry-1" }

        // ------ Stencil: escreve o valor de referencia ------
        Stencil
        {
            Ref  [_StencilRef]
            Comp Always
            Pass Replace
        }

        // mesh invisivel
        ColorMask 0
        ZWrite Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
            };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                return 0;
            }
            ENDCG
        }
    }
}
