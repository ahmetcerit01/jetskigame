// Alpha-blended splash and foam particles that stay on the curved water at a distance.
Shader "JetskiRunner/CurvedParticle"
{
    Properties
    {
        _TintColor ("Tint", Color) = (0.5,0.5,0.5,0.5)
        _MainTex ("Texture", 2D) = "white" {}
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Blend SrcAlpha OneMinusSrcAlpha
        ColorMask RGB
        Cull Off
        ZWrite Off
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #include "UnityCG.cginc"
            #include "../../Shaders/RunnerCurve.cginc"
            sampler2D _MainTex;
            float4 _MainTex_ST;
            fixed4 _TintColor;
            struct appdata { float4 vertex : POSITION; fixed4 color : COLOR; float2 uv : TEXCOORD0; };
            struct v2f { float4 pos : SV_POSITION; fixed4 color : COLOR; float2 uv : TEXCOORD0; UNITY_FOG_COORDS(1) };
            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityWorldToClipPos(CurveWorld(mul(unity_ObjectToWorld, v.vertex).xyz));
                o.color = v.color;
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                UNITY_TRANSFER_FOG(o, o.pos);
                return o;
            }
            fixed4 frag(v2f i) : SV_Target
            {
                fixed4 c = 2.0 * i.color * _TintColor * tex2D(_MainTex, i.uv);
                UNITY_APPLY_FOG_COLOR(i.fogCoord, c, fixed4(0,0,0,0));
                return c;
            }
            ENDCG
        }
    }
}
