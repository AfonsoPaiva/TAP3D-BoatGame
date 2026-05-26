// ============================================================
//  StencilMask.shader
//  Aplica-se a uma mesh que define a FORMA do buraco.
//  Escreve no stencil buffer mas NAO renderiza cor nem
//  profundidade — a mesh fica completamente invisivel.
//
//  Uso:
//   1. Criar um material com este shader.
//   2. Atribuir a uma mesh (quad, esfera, etc.) colocada
//      onde se quer o buraco na gruta.
//   3. A mesh da gruta deve usar o shader StencilHole
//      para que os pixels coincidentes sejam descartados.
//
//  _StencilRef deve ser igual ao valor no StencilHole.
// ============================================================
Shader "Custom/StencilMask"
{
    Properties
    {
        [IntRange] _StencilRef ("Stencil Reference", Range(0, 255)) = 1
    }

    SubShader
    {
        // Renderiza ANTES da geometria normal para que o stencil
        // ja esteja escrito quando a gruta for desenhada.
        Tags { "RenderType"="Opaque" "Queue"="Geometry-1" }

        // ------ Stencil: escreve o valor de referencia ------
        Stencil
        {
            Ref  [_StencilRef]
            Comp Always
            Pass Replace
        }

        // Nao escrever cor nem profundidade — mesh invisivel
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
