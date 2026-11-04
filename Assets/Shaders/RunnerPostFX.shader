Shader "JetskiRunner/PostFX"
{
    Properties { _MainTex ("Source", 2D) = "white" {} }
    CGINCLUDE
    #include "UnityCG.cginc"
    sampler2D _MainTex, _BloomTex;
    float4 _MainTex_TexelSize;
    float _Threshold, _Bloom, _Saturation, _Contrast, _Vignette, _Radial;
    float3 _Tint;
    struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };
    v2f vert(appdata_img v) { v2f o; o.pos = UnityObjectToClipPos(v.vertex); o.uv = v.texcoord; return o; }

    // 4-tap box: cheap, and stable enough once chained across mips
    float3 Box(float2 uv, float d)
    {
        float4 o = _MainTex_TexelSize.xyxy * float4(-d, -d, d, d);
        return (tex2D(_MainTex, uv + o.xy).rgb + tex2D(_MainTex, uv + o.zy).rgb + tex2D(_MainTex, uv + o.xw).rgb + tex2D(_MainTex, uv + o.zw).rgb) * .25;
    }
    float4 Prefilter(v2f i) : SV_Target
    {
        float3 c = min(Box(i.uv, 1), 8);           // clamp so single hot pixels do not flicker
        float b = max(c.r, max(c.g, c.b));
        float knee = _Threshold * .5;
        float soft = clamp(b - _Threshold + knee, 0, 2 * knee);
        soft = soft * soft / (4 * knee + 1e-4);
        return float4(c * max(soft, b - _Threshold) / max(b, 1e-4), 1);
    }
    float4 Down(v2f i) : SV_Target { return float4(Box(i.uv, 1), 1); }
    float4 Up(v2f i) : SV_Target { return float4(Box(i.uv, .5), 1); }

    // Keeps the art direction below ~0.75 and rolls highlights off smoothly instead of clipping.
    float3 Shoulder(float3 c)
    {
        const float a = .86;
        float3 over = max(c - a, 0);
        return min(c, a) + (1 - a) * (1 - exp(-over / (1 - a)));
    }
    float4 Composite(v2f i) : SV_Target
    {
        float2 centre = float2(.5, .58), toC = i.uv - centre;
        float3 c = tex2D(_MainTex, i.uv).rgb;
        if (_Radial > .001)                         // speed blur, strongest at the screen edges
        {
            float3 sum = c;
            for (int k = 1; k < 6; k++) sum += tex2D(_MainTex, i.uv - toC * _Radial * k / 5).rgb;
            c = lerp(c, sum / 6, saturate(dot(toC, toC) * 7));
        }
        c += tex2D(_BloomTex, i.uv).rgb * _Bloom * .22;     // the mip chain adds up, so normalise it
        c = Shoulder(c);
    #if !defined(UNITY_COLORSPACE_GAMMA)
        c = LinearToGammaSpace(c);
    #endif
        float l = dot(c, float3(.2126, .7152, .0722));
        c = lerp(l.xxx, c, _Saturation);
        c = (c - .5) * _Contrast + .5;
        c *= _Tint;
        float v = saturate(dot(toC, toC) * 2.4);
        c *= 1 - _Vignette * v * v;
        // dither to stop banding in the sky and fog gradients
        c += (frac(52.9829189 * frac(dot(i.pos.xy, float2(.06711056, .00583715)))) - .5) / 255;
        c = saturate(c);
    #if !defined(UNITY_COLORSPACE_GAMMA)
        c = GammaToLinearSpace(c);
    #endif
        return float4(c, 1);
    }
    // Faint blur at the screen edges only, for settings that skip the rest of the chain.
    float4 EdgeBlur(v2f i) : SV_Target
    {
        float2 toC = i.uv - float2(.5, .58);
        float3 c = tex2D(_MainTex, i.uv).rgb, sum = c;
        for (int k = 1; k < 4; k++) sum += tex2D(_MainTex, i.uv - toC * _Radial * k / 3).rgb;
        return float4(lerp(c, sum / 4, saturate(dot(toC, toC) * 7)), 1);
    }
    ENDCG
    SubShader
    {
        Cull Off ZWrite Off ZTest Always
        Pass { CGPROGRAM
            #pragma vertex vert
            #pragma fragment Prefilter
        ENDCG }
        Pass { CGPROGRAM
            #pragma vertex vert
            #pragma fragment Down
        ENDCG }
        Pass { Blend One One CGPROGRAM
            #pragma vertex vert
            #pragma fragment Up
        ENDCG }
        Pass { CGPROGRAM
            #pragma vertex vert
            #pragma fragment Composite
        ENDCG }
        Pass { CGPROGRAM
            #pragma vertex vert
            #pragma fragment EdgeBlur
        ENDCG }
    }
}
