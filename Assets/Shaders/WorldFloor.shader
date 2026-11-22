// A floor texture laid out in world space (seen from above), so planks run on seamlessly across every floor tile, whatever the
// tile mesh's own UVs are. Used by your room's plank floor (HouseBuilder: "Floor - Plank Flooring 04").
Shader "G7l6/World Floor"
{
    Properties
    {
        _Color ("Tint", Color) = (1,1,1,1)
        _MainTex ("Albedo", 2D) = "white" {}
        [Normal] _BumpMap ("Normal map", 2D) = "bump" {}
        _BumpScale ("Normal strength", Float) = 0.6
        _Scale ("Repeats per metre", Float) = 0.42
        _Rotate ("Turn 90 degrees (0/1)", Float) = 0
        _Glossiness ("Smoothness", Range(0,1)) = 0.3
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        LOD 200

        CGPROGRAM
        #pragma surface surf Standard fullforwardshadows
        #pragma target 3.0

        sampler2D _MainTex;
        sampler2D _BumpMap;
        fixed4 _Color;
        half _BumpScale, _Scale, _Rotate, _Glossiness;

        struct Input
        {
            float3 worldPos;
        };

        void surf (Input IN, inout SurfaceOutputStandard o)
        {
            float2 uv = IN.worldPos.xz * _Scale;
            if (_Rotate > 0.5) uv = uv.yx;
            fixed4 c = tex2D(_MainTex, uv) * _Color;
            o.Albedo = c.rgb;
            o.Normal = UnpackScaleNormal(tex2D(_BumpMap, uv), _BumpScale);
            o.Smoothness = _Glossiness;
            o.Metallic = 0;
            o.Alpha = 1;
        }
        ENDCG
    }
    FallBack "Diffuse"
}
