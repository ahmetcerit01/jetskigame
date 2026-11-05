// Same inputs as Unity's Standard shader for what the game uses, bent by the curved world.
// The shadow and depth passes get the same bend through addshadow, so shadows and water foam stay in place.
Shader "JetskiRunner/CurvedStandard"
{
    Properties
    {
        _Color ("Color", Color) = (1,1,1,1)
        _MainTex ("Albedo", 2D) = "white" {}
        _BumpMap ("Normal map", 2D) = "bump" {}
        _BumpScale ("Normal scale", Float) = 1
        _Metallic ("Metallic", Range(0,1)) = 0
        _Glossiness ("Smoothness", Range(0,1)) = 0.5
        _EmissionColor ("Emission", Color) = (0,0,0,1)
        _EmissionMap ("Emission map", 2D) = "white" {}
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        LOD 300
        CGPROGRAM
        #pragma surface surf Standard vertex:vert addshadow exclude_path:deferred exclude_path:prepass nolightmap nodynlightmap nodirlightmap nometa nolppv noforwardadd
        #pragma target 3.0
        #pragma multi_compile_instancing
        #pragma multi_compile_local __ _NORMALMAP
        #pragma multi_compile_local __ _EMISSION
        #include "../../Shaders/RunnerCurve.cginc"

        sampler2D _MainTex, _BumpMap, _EmissionMap;
        fixed4 _Color, _EmissionColor;
        float _BumpScale, _Metallic, _Glossiness;

        struct Input { float2 uv_MainTex; };

        void vert(inout appdata_full v)
        {
            float3 world = CurveWorld(mul(unity_ObjectToWorld, v.vertex).xyz);
            v.vertex.xyz = mul(unity_WorldToObject, float4(world, 1)).xyz;
        }

        void surf(Input i, inout SurfaceOutputStandard o)
        {
            fixed4 c = tex2D(_MainTex, i.uv_MainTex) * _Color;
            o.Albedo = c.rgb;
            o.Metallic = _Metallic;
            o.Smoothness = _Glossiness;
            o.Alpha = 1;
            #ifdef _NORMALMAP
                o.Normal = UnpackScaleNormal(tex2D(_BumpMap, i.uv_MainTex), _BumpScale);
            #endif
            #ifdef _EMISSION
                o.Emission = tex2D(_EmissionMap, i.uv_MainTex).rgb * _EmissionColor.rgb;
            #endif
        }
        ENDCG
    }
    Fallback "Diffuse"
}
