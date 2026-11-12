Shader "Harbor/Water"
{
    Properties
    {
        _Deep ("Deep water", Color) = (0.025, 0.24, 0.31, 1)
        _Shallow ("Shallow water", Color) = (0.10, 0.58, 0.60, 1)
        _Basin ("Basin edge effect", Range(0, 1)) = 1
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" }
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            fixed4 _Deep, _Shallow;
            float _Basin;
            float _HarborFilmTime;

            struct VertexInput { float4 vertex : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings
            {
                float4 position : SV_POSITION;
                float3 world : TEXCOORD0;
                float2 uv : TEXCOORD1;
            };

            Varyings vert(VertexInput v)
            {
                Varyings o;
                o.position = UnityObjectToClipPos(v.vertex);
                o.world = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.uv = v.uv;
                return o;
            }

            float hash(float2 p)
            {
                p = frac(p * float2(.1031, .1030));
                p += dot(p, p.yx + 33.33);
                return frac((p.x + p.y) * p.x);
            }

            float noise(float2 p)
            {
                float2 cell = floor(p);
                float2 uv = frac(p);
                uv = uv * uv * (3 - 2 * uv);
                return lerp(lerp(hash(cell), hash(cell + float2(1, 0)), uv.x),
                    lerp(hash(cell + float2(0, 1)), hash(cell + 1), uv.x), uv.y);
            }

            fixed4 frag(Varyings i) : SV_Target
            {
                float2 p = i.world.xz;
                float t = _HarborFilmTime > 0 ? _HarborFilmTime - 1 : _Time.y;
                float warp = noise(p * .105 + float2(.045, .02) * t);
                float a = dot(p, float2(1.53, .43)) + t * 1.35 + 4.0 * warp;
                float b = dot(p, float2(-.64, 1.9)) - t * 1.18 + 2.5 * sin(a * .27);
                float c = dot(p, float2(.45, 3.1)) + t * .7 + 5 * warp;
                float fidelity = saturate(1 - max(fwidth(a), fwidth(b)) * .8);
                float3 normal = normalize(float3(
                    (cos(a) * .115 - cos(b) * .075) * fidelity,
                    1,
                    (cos(b) * .14 + cos(c) * .08) * fidelity));
                float3 view = normalize(_WorldSpaceCameraPos.xyz - i.world);
                float3 sun = normalize(float3(-.43, .75, -.49));
                float shine = pow(saturate(dot(reflect(-sun, normal), view)), 85);
                float edge = min(min(i.uv.x, 1 - i.uv.x), min(i.uv.y, 1 - i.uv.y));
                float shelf = _Basin * exp(-edge * 19);
                float swell = .5 + .5 * sin(a + sin(b) * .54);
                float crest = pow(saturate(swell * (.64 + .36 * sin(c))), 8);
                float fresnel = pow(1 - saturate(dot(view, normal)), 4);
                float3 water = lerp(_Deep.rgb, _Shallow.rgb, saturate(.16 + shelf * .73 + swell * .08 * fidelity));
                water += float3(.06, .15, .15) * crest * fidelity * (.45 + shelf * .45);
                water += float3(.02, .047, .049) * (warp - .38);
                water += float3(.47, .61, .56) * shine * .60;
                water = lerp(water, float3(.32, .57, .62), fresnel * .55);
                float haze = saturate((distance(_WorldSpaceCameraPos.xyz, i.world) - 45) / 140);
                water = lerp(water, float3(.28, .45, .49), haze * haze * .87);
                return fixed4(water, 1);
            }
            ENDCG
        }
    }
    Fallback Off
}
