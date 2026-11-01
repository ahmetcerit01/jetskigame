Shader "JetskiRunner/SkyGradient"
{
    Properties
    {
        _Top ("Zenith", Color) = (0.36,0.62,0.84,1)
        _Horizon ("Horizon haze", Color) = (0.80,0.88,0.93,1)
        _Below ("Below horizon", Color) = (0.55,0.75,0.88,1)
    }
    SubShader
    {
        Tags { "Queue"="Background" "RenderType"="Background" }
        Cull Front ZWrite Off
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            fixed4 _Top, _Horizon, _Below;
            struct v2f { float4 position : SV_POSITION; float3 dir : TEXCOORD0; };
            v2f vert(float4 vertex : POSITION)
            {
                v2f o;
                o.position = UnityObjectToClipPos(vertex);
                o.dir = mul((float3x3)unity_ObjectToWorld, vertex.xyz);
                return o;
            }
            fixed4 frag(v2f i) : SV_Target
            {
                float y = normalize(i.dir).y;
                fixed3 col = y > 0 ? lerp(_Horizon.rgb, _Top.rgb, pow(saturate(y * 2.2), .65)) : lerp(_Horizon.rgb, _Below.rgb, saturate(-y * 6));
                return fixed4(col, 1);
            }
            ENDCG
        }
    }
}
