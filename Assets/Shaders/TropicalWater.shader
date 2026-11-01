Shader "JetskiRunner/TropicalWater"
{
    Properties
    {
        _Shallow ("Near turquoise", Color) = (0.13,0.72,0.83,1)
        _Deep ("Mid blue", Color) = (0.06,0.56,0.78,1)
        _Far ("Horizon blue", Color) = (0.33,0.66,0.84,1)
        _Trough ("Dark patches", Color) = (0.03,0.42,0.68,1)
        _Sky ("Sky reflection", Color) = (0.80,0.89,0.95,1)
        _SunDir ("Glint sun direction (towards sun)", Vector) = (-0.45,0.55,1,0)
        _Speed ("Wave speed", Float) = 1
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" }
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #include "UnityCG.cginc"
            fixed4 _Shallow, _Deep, _Far, _Trough, _Sky;
            float4 _SunDir;
            float _Speed, _PlayerX, _Scroll;

            struct appdata { float4 vertex : POSITION; };
            struct v2f { float4 position : SV_POSITION; float3 world : TEXCOORD0; UNITY_FOG_COORDS(1) };

            // Scrolled water coordinates: the mesh stays put while the world runs towards the camera.
            float2 Flow(float3 w) { return float2(w.x, w.z + _Scroll); }

            float Hash(float2 p) { return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453); }
            float Noise(float2 p)
            {
                float2 i = floor(p), f = frac(p); f = f * f * (3 - 2 * f);
                return lerp(lerp(Hash(i), Hash(i + float2(1,0)), f.x), lerp(Hash(i + float2(0,1)), Hash(i + float2(1,1)), f.x), f.y);
            }

            // Sum of directional sine waves; returns height in x and slope (d/dx, d/dz) in yz.
            float3 Waves(float2 q, float t)
            {
                const float4 w[6] = {
                    float4( 0.20, 1.00, 0.55, 0.070), float4(-0.55, 0.83, 0.90, 0.045),
                    float4( 0.80, 0.60, 1.60, 0.022), float4(-0.30, 0.95, 2.70, 0.012),
                    float4( 0.95,-0.30, 4.10, 0.007), float4(-0.85,-0.50, 6.30, 0.004) };
                float3 r = 0;
                [unroll] for (int k = 0; k < 6; k++)
                {
                    float2 d = normalize(w[k].xy);
                    float phase = dot(d, q) * w[k].z + t * sqrt(9.8 * w[k].z);
                    r.x += sin(phase) * w[k].w;
                    r.yz += d * cos(phase) * w[k].w * w[k].z;
                }
                return r;
            }

            v2f vert(appdata v)
            {
                v2f o;
                float3 world = mul(unity_ObjectToWorld, v.vertex).xyz;
                world.y += Waves(Flow(world), _Time.y * _Speed).x;
                o.world = world;
                o.position = UnityWorldToClipPos(world);
                UNITY_TRANSFER_FOG(o, o.position);
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float t = _Time.y * _Speed;
                float2 q = Flow(i.world);
                float3 waves = Waves(q, t);
                // fine ripples from scrolling noise
                float2 rq = q * 3.1 + float2(t * .3, -t * .6);
                float2 ripple = float2(Noise(rq) - Noise(rq + float2(.37, 0)), Noise(rq) - Noise(rq + float2(0, .37))) * .35;
                float3 n = normalize(float3(-(waves.y + ripple.x), 1, -(waves.z + ripple.y)));

                float3 view = normalize(_WorldSpaceCameraPos - i.world);
                float dist = length(_WorldSpaceCameraPos.xz - i.world.xz);

                // colour: turquoise near, deeper blue mid, hazy blue at the horizon, soft darker patches
                float patches = smoothstep(.35, .8, Noise(q * float2(.045, .03)) * .65 + Noise(q * .11) * .35);
                fixed3 col = lerp(_Shallow.rgb, _Deep.rgb, saturate(dist / 60));
                col = lerp(col, _Trough.rgb, patches * .6 * saturate(dist / 12));
                col = lerp(col, _Far.rgb, saturate((dist - 70) / 160));
                // crests catch light, troughs go darker
                col *= 0.86 + saturate(waves.x * 6 + .5) * .26;

                float fresnel = pow(1 - saturate(dot(n, view)), 5);
                col = lerp(col, _Sky.rgb, saturate(fresnel * .85 + .04));

                float3 sun = normalize(_SunDir.xyz);
                float3 refl = reflect(-view, n);
                float glint = pow(saturate(dot(refl, sun)), 900) * .9 + pow(saturate(dot(refl, sun)), 60) * .05;
                float diffuse = saturate(dot(n, normalize(float3(-.3, 1, -.4))));
                col = col * (.82 + diffuse * .2) + glint;

                // jetski wake: two broken foam lines spreading behind the player and a churned centre
                float back = -i.world.z;
                float wakeRange = smoothstep(.3, 1.6, back) * (1 - smoothstep(12, 26, back));
                float edge = abs(abs(i.world.x - _PlayerX) - (.42 + back * .16));
                float broken = smoothstep(.25, .75, Noise(float2(i.world.x * 4, q.y * 2.5)));
                float sideFoam = (1 - smoothstep(.05, .28, edge)) * broken * wakeRange;
                float centre = (1 - smoothstep(.1, .7, abs(i.world.x - _PlayerX))) * (1 - smoothstep(1, 9, back)) * step(0, back);
                float centreFoam = centre * smoothstep(.45, .8, Noise(float2(i.world.x * 6, q.y * 4)));
                col = lerp(col, fixed3(.93, .98, 1), saturate(sideFoam * .9 + centreFoam * .85));

                fixed4 result = fixed4(col, 1);
                UNITY_APPLY_FOG(i.fogCoord, result);
                return result;
            }
            ENDCG
        }
    }
    Fallback "Unlit/Color"
}
