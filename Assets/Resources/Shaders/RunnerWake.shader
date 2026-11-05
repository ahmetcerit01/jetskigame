Shader "JetskiRunner/Wake"
{
    Properties { _Scroll ("Distance travelled", Float) = 0 _Strength ("Strength", Float) = 1 }
    SubShader
    {
        Tags { "Queue"="Transparent+50" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Blend SrcAlpha OneMinusSrcAlpha ZWrite Off Cull Off
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            #include "../../Shaders/RunnerCurve.cginc"
            float _Scroll, _Strength;
            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; float4 color : COLOR; };
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; fixed a : TEXCOORD1; };
            float Hash(float2 p) { return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453); }
            float Noise(float2 p)
            {
                float2 i = floor(p), f = frac(p); f = f * f * (3 - 2 * f);
                return lerp(lerp(Hash(i), Hash(i + float2(1,0)), f.x), lerp(Hash(i + float2(0,1)), Hash(i + float2(1,1)), f.x), f.y);
            }
            v2f vert(appdata v)
            {
                v2f o; o.pos = UnityWorldToClipPos(CurveWorld(mul(unity_ObjectToWorld, v.vertex).xyz)); o.uv = v.uv; o.a = v.color.a;
                return o;
            }
            fixed4 frag(v2f i) : SV_Target
            {
                // uv.x across the streak (-1..1), uv.y metres behind the jetski
                float along = i.uv.y + _Scroll;
                float across = abs(i.uv.x);
                // long streaks stretched along the wake, broken up by finer foam
                float streaks = Noise(float2(i.uv.x * 7.0, along * .12)) * .65 + Noise(float2(i.uv.x * 19.0, along * .3)) * .35;
                float foam = Noise(float2(i.uv.x * 12.0, along * 2.2));
                float core = 1 - across;
                // solid white down the middle, torn into strands and flecks towards the edges
                float a = smoothstep(.55, .78, streaks * .68 + foam * .32 + core * core * .3) * smoothstep(0, .35, core);
                return fixed4(.96, .99, 1, saturate(a * i.a * _Strength));
            }
            ENDCG
        }
    }
}
