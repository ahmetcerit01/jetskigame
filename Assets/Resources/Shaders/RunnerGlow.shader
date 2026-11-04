Shader "JetskiRunner/Glow"
{
    Properties { _Color ("Colour", Color) = (1,.8,.4,1) }
    SubShader
    {
        Tags { "Queue"="Transparent+60" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Blend One One ZWrite Off Cull Off
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            fixed4 _Color;
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };
            v2f vert(float4 vertex : POSITION, float2 uv : TEXCOORD0)
            {
                v2f o; o.pos = UnityObjectToClipPos(vertex); o.uv = uv * 2 - 1;
                return o;
            }
            fixed4 frag(v2f i) : SV_Target
            {
                float r = length(i.uv), ang = atan2(i.uv.y, i.uv.x);
                float rays = .55 + .45 * sin(ang * 9.0) * sin(ang * 5.0 + 1.3);
                float fall = saturate(1 - r);
                return fixed4(_Color.rgb * fall * fall * (.35 + rays * .65) * _Color.a, 1);
            }
            ENDCG
        }
    }
}
