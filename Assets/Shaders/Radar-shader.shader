Shader "Custom/RadarShader"
{
    Properties
    {
        _MainTex        ("Texture",            2D)    = "white" {}

        // Driven by RadarHUD.cs
        _SweepRPS       ("Sweep Rot/sec",      Float) = 0.4
        _SweepWidth     ("Sweep Trail (rad)",  Float) = 1.8
        _NumRings       ("Number of Rings",    Float) = 3.0

        // Colours (tweakable in Material Inspector)
        _ScreenColor    ("Radar BG Color",     Color) = (0.03, 0.10, 0.03, 1)
        _RingColor      ("Ring Color",         Color) = (0.0,  0.80, 0.15, 1)
        _SweepColor     ("Sweep Color",        Color) = (0.0,  1.0,  0.25, 1)
        _BlipColor      ("Blip Color",         Color) = (1.0,  1.0,  0.2,  1)
        _BuoyColor      ("Buoy Blip Color",    Color) = (1.0,  0.2,  0.15, 1)
        _BorderColor    ("Border Color",       Color) = (0.0,  0.55, 0.10, 1)
        _Scanline       ("Scanline Intensity", Float) = 0.05
        _RadarSilhouetteAtlas ("Silhouette Atlas", 2D) = "black" {}

        // Post-Processing Settings
        _RadarSize      ("Radar Size (relative to height)", Float) = 0.22
        _RadarMargin    ("Radar Margin (pixels)", Float) = 12.0
    }

    SubShader
    {
        // Render on top of everything, with alpha blending
        Tags { "Queue"="Overlay" "RenderType"="Transparent" "IgnoreProjector"="True" }

        Pass
        {
            ZTest     Always
            ZWrite    Off
            Cull      Off

            CGPROGRAM
            #pragma vertex   vert
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

            float  _SweepRPS;
            float  _SweepWidth;
            float  _NumRings;

            fixed4 _ScreenColor;
            fixed4 _RingColor;
            fixed4 _SweepColor;
            fixed4 _BlipColor;
            fixed4 _BuoyColor;
            fixed4 _BorderColor;
            float  _Scanline;

            sampler2D _RadarSilhouetteAtlas;
            float4    _BuoyAtlasRects[16];   // xy = UV origin, zw = UV size
            float     _BuoyBlipSizes[16];    // radar-space half-size per blip

            float  _RadarSize;
            float  _RadarMargin;
            int     _BuoyCount;
            float4  _BuoyBlips[16];

            #define TWO_PI  6.28318530718
            #define PI      3.14159265359

            v2f vert(appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv     = v.uv;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                // ── Post-processing viewport mapping ────────────────────
                float aspect = _ScreenParams.x / _ScreenParams.y;
                float2 heightUV = i.uv;
                heightUV.x *= aspect; // Map X to height-relative space

                // Radar size and margin in height-normalized space
                float size = _RadarSize;
                float margin = _RadarMargin / _ScreenParams.y;

                // Center of the radar in height-normalized space
                float2 center = float2(margin + size * 0.5, margin + size * 0.5);

                // Distance from current pixel to center of the radar
                float distToCenter = length(heightUV - center);
                float normDistToCenter = distToCenter / (size * 0.5);

                // Sample the scene colour. In Built-in RP, Graphics.Blit always binds
                // the source render texture to _MainTex automatically.
                fixed4 screenCol = tex2D(_MainTex, i.uv);

                // If outside the radar viewport area completely, return screen colour immediately
                if (normDistToCenter > 1.05)
                {
                    return screenCol;
                }

                // Map screen UV to radar internal UV (0..1)
                float2 radarUV = (heightUV - center) / size + float2(0.5, 0.5);

                // Smooth alpha mask for the outer circle edge
                float dist = length(radarUV - float2(0.5, 0.5));
                float alpha = 1.0 - smoothstep(0.48, 0.50, dist);
                if (alpha < 0.001) return screenCol;

                // UV centre of the quad is (0.5, 0.5)
                // delta goes from -0.5 to +0.5 in both axes
                float2 delta    = radarUV - float2(0.5, 0.5);

                // dist: 0 at centre, 0.5 at disc edge (quad fills [0,1])
                float  distVal  = length(delta);
                float  normDist = distVal / 0.5;   // 0..1 inside the disc, >1 outside

                // ── dark background ─────────────────────────────────────
                fixed4 col = _ScreenColor * 0.65;

                // ── distance rings ──────────────────────────────────────
                float ringMask = 0.0;
                float w = 0.015;
                for (float r = 1.0; r <= _NumRings; r += 1.0)
                {
                    float ringR = r / (_NumRings + 1.0);
                    ringMask = max(ringMask,
                        smoothstep(ringR - w, ringR,        normDist) *
                        (1.0 - smoothstep(ringR, ringR + w, normDist)));
                }
                col = lerp(col, _RingColor * 0.55, ringMask * 0.8);

                // ── cross-hair lines ────────────────────────────────────
                float lw    = 0.007;
                float hLine = smoothstep(lw, 0.0, abs(delta.y));
                float vLine = smoothstep(lw, 0.0, abs(delta.x));
                col = lerp(col, _RingColor * 0.4, max(hLine, vLine) * 0.55);

                // ── sweep trail ─────────────────────────────────────────
                float sweepAngle = fmod(_Time.y * _SweepRPS * TWO_PI, TWO_PI);
                float pixAngle = atan2(-delta.y, delta.x);
                if (pixAngle < 0.0) pixAngle += TWO_PI;

                float trailAngle = fmod(sweepAngle - pixAngle + TWO_PI, TWO_PI);

                float sweepMask = 0.0;
                if (trailAngle < _SweepWidth)
                    sweepMask = pow(1.0 - (trailAngle / _SweepWidth), 1.5);

                float edgeGlow = smoothstep(0.15, 0.0, trailAngle) * 1.5;

                col = lerp(col, _SweepColor,       sweepMask * 0.6);
                col = lerp(col, _SweepColor * 1.8, edgeGlow  * 0.75);

                // ── scanlines ───────────────────────────────────────────
                float scan = sin(radarUV.y * 200.0 * PI);
                col.rgb -= _Scanline * (0.5 + 0.5 * scan);

                // ── buoy blips (sweep-triggered, atlas silhouettes) ────────
                [loop]
                for (int b = 0; b < _BuoyCount; b++)
                {
                    float2 buoyUV     = _BuoyBlips[b].xy;
                    float2 buoyCentre = buoyUV - float2(0.5, 0.5);

                    // Discard buoys outside the radar disc
                    if (length(buoyCentre) > 0.48) continue;

                    // ── angle of this buoy on the radar ──────────────────────
                    float buoyAngle = atan2(-buoyCentre.y, buoyCentre.x);
                    if (buoyAngle < 0.0) buoyAngle += TWO_PI;

                    // How far the sweep has rotated past this buoy's angle
                    float sweepAge = fmod(sweepAngle - buoyAngle + TWO_PI, TWO_PI);

                    // Pure sweep-distance fade (no blinking)
                    float intensity = exp(-sweepAge * 2.2);

                    if (intensity < 0.008) continue;

                    // ── sample silhouette atlas ───────────────────────────────
                    float blipHalfSize = _BuoyBlipSizes[b];
                    float2 buoyDelta   = radarUV - buoyUV;

                    // Skip if outside this blip's bounding area (with margin)
                    if (abs(buoyDelta.x) > blipHalfSize || abs(buoyDelta.y) > blipHalfSize)
                        continue;

                    // Map fragment position to 0..1 within the blip's tile
                    float2 localUV = buoyDelta / (blipHalfSize * 2.0) + float2(0.5, 0.5);

                    // Map local UV to atlas tile UV
                    float4 atlasRect = _BuoyAtlasRects[b];
                    float2 atlasUV   = atlasRect.xy + localUV * atlasRect.zw;

                    // Sample the silhouette (white on black)
                    // tex2Dlod required — tex2D has undefined derivatives inside dynamic branches
                    float silhouette = tex2Dlod(_RadarSilhouetteAtlas, float4(atlasUV, 0, 0)).r;

                    // Outer glow around the silhouette shape
                    float distToEdge = length(buoyDelta) / blipHalfSize;
                    float halo = (1.0 - smoothstep(0.5, 1.0, distToEdge)) * 0.4 * intensity;

                    // Core silhouette
                    float core = silhouette * intensity;

                    col = lerp(col, _BuoyColor,       halo);
                    col = lerp(col, _BuoyColor * 2.0, core);
                }

                // ── boat blip at centre ─────────────────────────────────────
                float blipSize = 0.025;
                float blipGlow = 0.07;
                float blip     = 1.0 - smoothstep(blipSize, blipSize + 0.01, normDist);
                float blipHalo = (1.0 - smoothstep(blipSize, blipGlow,       normDist)) * 0.45;
                col = lerp(col, _BlipColor,       blipHalo);
                col = lerp(col, _BlipColor * 2.0, blip);

                // ── outer border ring ───────────────────────────────────
                float borderW   = 0.04;
                float outerEdge = smoothstep(1.0 - borderW, 1.0, normDist);
                col = lerp(col, _BorderColor * 1.5, outerEdge);

                return fixed4(lerp(screenCol.rgb, col.rgb, alpha), 1.0);
            }
            ENDCG
        }
    }
    Fallback Off
}
