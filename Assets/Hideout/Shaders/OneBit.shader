Shader "Hideout/OneBit"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
    }

    SubShader
    {
        Pass
        {
            ZTest Always
            Cull Off
            ZWrite Off

            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;

            // Set globally by PaletteManager
            float4 _PaletteColorA; // dark
            float4 _PaletteColorB; // light

            fixed4 frag(v2f_img i) : SV_Target
            {
                fixed4 col = tex2D(_MainTex, i.uv);

                // Luminance
                float lum = dot(col.rgb, float3(0.299, 0.587, 0.114));

                // Snap to 0 or 1 — pure 1-bit, no gradients
                float bit = step(0.5, lum);

                // Map to palette
                return lerp(_PaletteColorA, _PaletteColorB, bit);
            }
            ENDCG
        }
    }
}
