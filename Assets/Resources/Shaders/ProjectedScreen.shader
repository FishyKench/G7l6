// A glowing screen picture that ignores the model's own texture layout: the picture is projected straight
// across the screen part of the mesh (its left-right and up-down in the model's space), so it always fills
// the whole screen, curved or flat. Used for TV static and the PC monitor. Set up from code (ScreenProjector).
Shader "G7l6/Projected Screen"
{
    Properties
    {
        _MainTex ("Picture", 2D) = "white" {}
        _Tint ("Tint", Color) = (1, 1, 1, 1)
        _Brightness ("Brightness", Float) = 1.2
        _AxisU ("Left to right (model space)", Vector) = (1, 0, 0, 0)
        _AxisV ("Bottom to top (model space)", Vector) = (0, 1, 0, 0)
        _Range ("Min U, size U, min V, size V", Vector) = (0, 1, 0, 1)
        _Noise ("Static: x = frames per second, y = tiling (0 = off)", Vector) = (0, 0, 0, 0)
        _Collapse ("Switching off: x = height, y = width, z = flash (1, 1, 0 = on)", Vector) = (1, 1, 0, 0)
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" }
        LOD 100

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            fixed4 _Tint;
            float _Brightness;
            float4 _AxisU, _AxisV, _Range, _Noise, _Collapse;

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            v2f vert (appdata_base v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = float2((dot(v.vertex.xyz, _AxisU.xyz) - _Range.x) / _Range.y,
                              (dot(v.vertex.xyz, _AxisV.xyz) - _Range.z) / _Range.w);
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                float2 uv = i.uv;
                fixed4 col;
                if (_Noise.x > 0)
                {
                    // TV static: jump to a random part of the noise picture many times a second.
                    float frame = floor(_Time.y * _Noise.x);
                    float2 jump = frac(float2(sin(frame * 12.9898) * 43758.5453, sin(frame * 78.233) * 12543.1234));
                    uv = uv * _Noise.y + jump;
                    // A faint rolling bar, like an old screen.
                    float roll = frac(i.uv.y * 2.0 - _Time.y * 0.6);
                    col = tex2D(_MainTex, uv) * _Tint * _Brightness * (0.85 + 0.15 * smoothstep(0.0, 0.1, roll));
                }
                else col = tex2D(_MainTex, uv) * _Tint * _Brightness;

                // Switching off like an old TV: the picture flashes white and squashes to a line, then a dot.
                if (_Collapse.x < 0.999 || _Collapse.y < 0.999 || _Collapse.z > 0.001)
                {
                    col = lerp(col, fixed4(1, 1, 1, 1) * 1.8, _Collapse.z);
                    float2 c = abs(i.uv - 0.5);
                    float h = max(_Collapse.x, 0.008) * 0.5, w = _Collapse.y * 0.5;
                    float inside = (1 - smoothstep(h, h + 0.006, c.y)) * (1 - smoothstep(w, w + 0.006, c.x));
                    col *= inside;
                }
                return col;
            }
            ENDCG
        }
    }
}
