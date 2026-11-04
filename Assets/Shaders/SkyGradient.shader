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
            float4 _SunDir, _SunColor;
            float _Rays, _RayTime;
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
                float3 dir = normalize(i.dir);
                float y = dir.y;
                fixed3 col = y > 0 ? lerp(_Horizon.rgb, _Top.rgb, pow(saturate(y * 2.2), .65)) : lerp(_Horizon.rgb, _Below.rgb, saturate(-y * 6));
                // sun: HDR disc for the bloom, tight corona and a wide warm glow over the horizon
                float d = saturate(dot(dir, normalize(_SunDir.xyz)));
                col += _SunColor.rgb * (smoothstep(.9994, .9997, d) * 6 + pow(d, 350) * .6 + pow(d, 70) * .12) * step(-.02, y);
                // light shafts fanning out of the sun, turning slowly
                float3 sunDir = normalize(_SunDir.xyz);
                if (_Rays > .001 && dir.z > .05)
                {
                    float2 fromSun = dir.xy / dir.z - sunDir.xy / sunDir.z;
                    float ang = atan2(fromSun.y, fromSun.x);
                    float shafts = sin(ang * 13.0 + _RayTime) * sin(ang * 7.0 - _RayTime * .6) + sin(ang * 23.0 + _RayTime * .35) * .4;
                    float reach = saturate(1 - length(fromSun) * .85);
                    col += fixed3(.55, .72, 1) * saturate(shafts) * reach * saturate(y * 5) * _Rays * .3;
                }
                return fixed4(col, 1);
            }
            ENDCG
        }
    }
}
