Shader "Harbor/Film Water"
{
    Properties
    {
        _SunDirection ("Sun direction", Vector) = (-0.65, 0.43, 0.59, 0)
        _Haze ("Horizon haze", Color) = (0.56, 0.65, 0.69, 1)
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" }
        Pass
        {
            CGPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            #include "UnityCG.cginc"

            float4 _SunDirection, _Haze, _DockBoat;
            float _HarborFilmTime, _TrialMap;

            struct Interpolators
            {
                float4 position : SV_POSITION;
                float3 world : TEXCOORD0;
            };

            Interpolators Vert(float4 position : POSITION)
            {
                Interpolators output;
                output.position = UnityObjectToClipPos(position);
                output.world = mul(unity_ObjectToWorld, position).xyz;
                return output;
            }

            float Hash(float2 p)
            {
                float3 q = frac(float3(p.x, p.y, p.x) * .1031);
                q += dot(q, q.yzx + 33.33);
                return frac((q.x + q.y) * q.z);
            }

            float Noise(float2 p)
            {
                float2 cell = floor(p);
                float2 f = frac(p);
                f = f * f * (3 - 2 * f);
                return lerp(lerp(Hash(cell), Hash(cell + float2(1, 0)), f.x),
                    lerp(Hash(cell + float2(0, 1)), Hash(cell + 1), f.x), f.y);
            }

            float4 Frag(Interpolators input) : SV_Target
            {
                float2 p = input.world.xz;
                float t = _HarborFilmTime - 1;
                float warp = Noise(p * .34 + float2(.18, -.12) * t);
                float drift = Noise(p * .12 + float2(-.08, .04) * t);
                float a = dot(p, float2(1.6, 2.1)) + 1.8 * t + 4.6 * warp;
                float b = dot(p, float2(-3.1, 4.6)) - 2.2 * t + 5.8 * drift;
                float c = dot(p, float2(8.2, -2.8)) + 3.6 * t + 3.0 * warp;
                float fidelity = saturate(1 - fwidth(c) * .9);
                float swellFidelity = saturate(1 - max(fwidth(a), fwidth(b)) * .68);
                float2 slope = cos(a) * float2(.077, .108) + cos(b) * float2(-.055, .073)
                    + cos(c) * float2(.056, -.037) * fidelity;
                slope *= swellFidelity;

                float2 heading = float2(sin(_DockBoat.z), cos(_DockBoat.z));
                float2 delta = p - _DockBoat.xy;
                float aft = -dot(delta, heading) - 1.05;
                float beam = dot(delta, float2(heading.y, -heading.x));
                float wake = smoothstep(0, .35, aft) * exp(-max(aft, 0) * .29) * smoothstep(.25, 2.7, _DockBoat.w);
                float disturbance = sin(aft * 9.0 + beam * 4.0 - t * 4.0);
                float wakeWidth = .13 + max(aft, 0) * .065;
                float plume = abs(abs(beam) - (.47 + .13 * aft)) / wakeWidth;
                float wakeMask = wake * exp(-plume * plume * .66);
                slope += disturbance * wakeMask * float2(.20, .085);
                float3 normal = normalize(float3(slope.x, 1, slope.y));
                float3 view = normalize(_WorldSpaceCameraPos.xyz - input.world);
                float3 reflected = reflect(-view, normal);
                float rim = pow(1 - saturate(dot(view, normal)), 4.8);
                float glint = saturate(dot(reflected, normalize(_SunDirection.xyz)));

                float tide = .5 + .5 * sin(a + .5 * sin(b));
                float3 deep = lerp(float3(.037, .17, .22), float3(.11, .35, .39),
                    saturate(warp * .31 + tide * .21 * swellFidelity));
                float3 sky = lerp(float3(.55, .65, .70), float3(.31, .46, .57), saturate(reflected.y * 1.8));
                float3 water = lerp(deep, sky, .14 + rim * .67);
                water += float3(.44, .30, .16) * pow(glint, 11) * .17;
                water += float3(.94, .77, .53) * pow(glint, 92) * .31;

                float flecks = Noise(float2(beam * 29, aft * 14 + t * 2.5));
                float foam = wakeMask * smoothstep(.40, .82, flecks) * .72;
                water = lerp(water, float3(.83, .91, .90), foam);

                float haze = 1 - exp(-length(_WorldSpaceCameraPos.xyz - input.world) * .014);
                water = lerp(water, _Haze.rgb, haze * haze);
                float3 map = float3(.045, .245, .28) + warp * float3(.02, .042, .042);
                return float4(lerp(water, map, _TrialMap), 1);
            }
            ENDCG
        }
    }
    Fallback Off
}
