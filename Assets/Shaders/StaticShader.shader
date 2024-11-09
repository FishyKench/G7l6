Shader "Unlit/TVStaticShader"
{
    Properties
    {
        _MainTex("Main Texture", 2D) = "white" {}
        _NoiseTex("Noise Texture", 2D) = "white" {} // Extra texture to add noise
        _Speed("Speed", Float) = 0.02 // Adjust to make movement slightly faster
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

                struct appdata_t
                {
                    float4 vertex : POSITION;
                    float2 uv : TEXCOORD0;
                };

                struct v2f
                {
                    float2 uv : TEXCOORD0;
                    float4 vertex : SV_POSITION;
                };

                sampler2D _MainTex;
                sampler2D _NoiseTex;
                float _Speed;

                v2f vert(appdata_t v)
                {
                    v2f o;
                    o.vertex = UnityObjectToClipPos(v.vertex);
                    o.uv = v.uv;
                    return o;
                }

                fixed4 frag(v2f i) : SV_Target
                {
                    // Increase multiplier for a faster horizontal movement
                    float timeOffset = _Time.y * _Speed * 0.3; // Faster movement with a 0.5 multiplier
                    float2 offsetUV = i.uv + float2(timeOffset, 0); // Horizontal movement

                    fixed4 staticColor = tex2D(_MainTex, offsetUV); // Main static texture
                    fixed4 noiseColor = tex2D(_NoiseTex, i.uv * 2.0); // Noise texture with smaller scale

                    // Blend noise with main static texture
                    fixed4 col = lerp(staticColor, noiseColor, 0.2); // Adjust 0.2 for blend strength
                    return col;
                }
                ENDCG
            }
        }
}
