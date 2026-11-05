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
        _WavePhase ("Wave phase", Float) = 0
    }
    SubShader
    {
        // After the opaques so it can read their depth for contact foam; it still writes solid colour.
        Tags { "RenderType"="Transparent" "Queue"="Transparent-50" "IgnoreProjector"="True" }
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #include "UnityCG.cginc"
            #include "RunnerCurve.cginc"
            fixed4 _Shallow, _Deep, _Far, _Trough, _Sky;
            float4 _SunDir;
            float4 _SkyTop, _SunColor, _PlayerPos;
            float _WavePhase, _PlayerX, _Scroll, _Foam, _Caustics;
            UNITY_DECLARE_DEPTH_TEXTURE(_CameraDepthTexture);

            struct appdata { float4 vertex : POSITION; };
            struct v2f { float4 position : SV_POSITION; float3 world : TEXCOORD0; UNITY_FOG_COORDS(1) float4 screen : TEXCOORD2; };

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

            // Web of light on the sea floor: folded sines, cheap enough for every water pixel.
            float Caustic(float2 q, float t)
            {
                float2 p = q * .62;
                float c = 1;
                for (int k = 0; k < 3; k++)
                {
                    float2 w = p + float2(sin(p.y * 1.3 + t * .9), cos(p.x * 1.1 - t * .7)) * .9;
                    c = min(c, abs(sin(w.x) * cos(w.y)));
                    p = float2(p.x * .8 - p.y * .6, p.x * .6 + p.y * .8) * 1.45 + 2.1;
                }
                return pow(saturate(1 - c * 2.4), 2);
            }

            v2f vert(appdata v)
            {
                v2f o;
                float3 world = mul(unity_ObjectToWorld, v.vertex).xyz;
                world.y += Waves(Flow(world), _WavePhase).x;
                o.world = world;    // the waves and foam are worked out on the flat water, only the position bends
                float3 bent = CurveWorld(world);
                o.position = UnityWorldToClipPos(bent);
                UNITY_TRANSFER_FOG(o, o.position);
                o.screen = ComputeScreenPos(o.position);
                o.screen.z = -mul(UNITY_MATRIX_V, float4(bent, 1)).z;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float t = _WavePhase;
                float2 q = Flow(i.world);
                float3 waves = Waves(q, t);
                // fine ripples from scrolling noise
                float2 rq = q * 3.1 + float2(t * .3, -t * .6);
                float n0 = Noise(rq), n1 = Noise(rq * 2.3 + 7.1 - t * .5);
                float2 ripple = float2(n0 - Noise(rq + float2(.37, 0)), n0 - Noise(rq + float2(0, .37))) * .35
                              + float2(n1 - Noise(rq * 2.3 + 7.1 - t * .5 + float2(.31, 0)), n1 - Noise(rq * 2.3 + 7.1 - t * .5 + float2(0, .31))) * .16;
                float3 n = normalize(float3(-(waves.y + ripple.x), 1, -(waves.z + ripple.y)));

                float3 view = normalize(_WorldSpaceCameraPos - i.world);
                float dist = length(_WorldSpaceCameraPos.xz - i.world.xz);

                // colour: turquoise near, deeper blue mid, hazy blue at the horizon, soft darker patches
                float patches = smoothstep(.35, .8, Noise(q * float2(.045, .03)) * .65 + Noise(q * .11) * .35);
                fixed3 col = lerp(_Shallow.rgb, _Deep.rgb, saturate(dist / 60));
                col = lerp(col, _Trough.rgb, patches * .6 * saturate(dist / 12));
                col = lerp(col, _Far.rgb, saturate((dist - 70) / 160));
                col += _Shallow.rgb * Caustic(q, t) * _Caustics * .09 * saturate(1 - dist / 60);
                // brighter aqua over the shallows along both shores, deep blue down the lanes
                float reef = smoothstep(4.2, 8.5, abs(i.world.x)) * (1 - smoothstep(60, 170, dist));
                col = lerp(col, _Shallow.rgb * float3(1.15, 1.42, 1.22) + float3(.02, .05, .04), reef * .72);
                // crests catch light, troughs go darker
                col *= 0.86 + saturate(waves.x * 6 + .5) * .26;

                // light passing through crests: they glow a brighter, greener turquoise
                float3 sun = normalize(_SunDir.xyz);
                float crest = saturate(waves.x * 7 + ripple.x * 1.5 + .25);
                col += _Shallow.rgb * crest * crest * .1 * pow(saturate(dot(view, -sun) * .5 + .5), 2);

                // Schlick fresnel reflecting a real sky gradient rather than one flat colour
                float3 refl = reflect(-view, n);
                float fresnel = .02 + .98 * pow(1 - saturate(dot(n, view)), 5);
                float3 skyRefl = lerp(_Sky.rgb, _SkyTop.rgb, saturate(refl.y * 1.6));
                col = lerp(col, skyRefl, saturate(fresnel * .9));

                float diffuse = saturate(dot(n, normalize(float3(-.3, 1, -.4))));
                col *= .84 + diffuse * .2;
                // HDR sun sparkle (values above 1 feed the bloom), wide sheen under it
                float rs = saturate(dot(refl, sun));
                col += _SunColor.rgb * (pow(rs, 420) * .42 * saturate(dist / 16) + pow(rs, 60) * .035);

                // wind-whipped whitecaps on the crests, more of them in rough biomes
                float caps = Noise(q * float2(1.5, .9) + t * float2(.1, .28)) * .5 + Noise(q * 3.7 - t * .4) * .3 + Noise(q * 9.1 + t * .3) * .2;
                float capMask = smoothstep(.66 - _Foam * .08, .8 - _Foam * .08, caps) * smoothstep(.1, .7, crest + _Foam * .3);
                col = lerp(col, fixed3(.92, .97, 1), capMask * saturate(_Foam) * .75);

                // soft shadow pooling under the jetski; spreads and fades as it leaves the water
                float h = saturate(_PlayerPos.y / 2.5);
                float2 sd = (i.world.xz - _PlayerPos.xz - float2(.15, -.1)) / (float2(.95, 1.75) * (1 + h * .7));
                col *= 1 - .42 * (1 - h * .7) * (1 - smoothstep(.3, 1, length(sd)));

                // contact foam and a dark waterline wherever something pierces the surface
                float sceneZ = LinearEyeDepth(SAMPLE_DEPTH_TEXTURE_PROJ(_CameraDepthTexture, UNITY_PROJ_COORD(i.screen)));
                float gap = max(sceneZ - i.screen.z, 0) * saturate(dot(n, view) + .15);
                col *= lerp(.82, 1, saturate(gap / .55));
                float lace = Noise(q * 5 + t * float2(.3, -.5));
                float contact = (1 - smoothstep(.02, .42 + lace * .4, gap)) * step(gap, 2);
                col = lerp(col, fixed3(.95, .99, 1), contact * .88);

                // jetski wake: two broken foam lines spreading behind the player and a churned centre
                float back = -i.world.z;
                float wakeRange = smoothstep(.3, 1.6, back) * (1 - smoothstep(12, 26, back));
                float edge = abs(abs(i.world.x - _PlayerX) - (.42 + back * .16));
                float broken = smoothstep(.25, .75, Noise(float2(i.world.x * 4, q.y * 2.5)));
                float sideFoam = (1 - smoothstep(.05, .28, edge)) * broken * wakeRange;
                float centre = (1 - smoothstep(.1, .7, abs(i.world.x - _PlayerX))) * (1 - smoothstep(1, 9, back)) * step(0, back);
                float centreFoam = centre * smoothstep(.45, .8, Noise(float2(i.world.x * 6, q.y * 4)));
                col = lerp(col, fixed3(.93, .98, 1), saturate(sideFoam * .45 + centreFoam * .6));

                fixed4 result = fixed4(col, 1);
                UNITY_APPLY_FOG(i.fogCoord, result);
                return result;
            }
            ENDCG
        }
    }
    Fallback "Unlit/Color"
}
