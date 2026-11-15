Shader "Harbor/Film Sky"
{
    Properties { _SunDirection ("Sun direction", Vector) = (-0.65, 0.43, 0.59, 0) }
    SubShader
    {
        Tags { "Queue"="Background" "RenderType"="Background" "PreviewType"="Skybox" }
        Cull Off ZWrite Off
        Pass
        {
            CGPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "UnityCG.cginc"
            float4 _SunDirection;

            struct Interpolators
            {
                float4 position : SV_POSITION;
                float3 direction : TEXCOORD0;
            };

            Interpolators Vert(float4 vertex : POSITION)
            {
                Interpolators output;
                output.position = UnityObjectToClipPos(vertex);
                output.direction = vertex.xyz;
                return output;
            }

            float4 Frag(Interpolators input) : SV_Target
            {
                float3 ray = normalize(input.direction);
                float elevation = saturate(ray.y);
                float3 color = lerp(float3(.78, .78, .73), float3(.29, .45, .60), pow(elevation, .49));
                float sun = saturate(dot(ray, normalize(_SunDirection.xyz)));
                color += float3(.19, .12, .057) * pow(sun, 10);
                float bands = sin(ray.y * 46 + ray.x * 9) + .55 * sin(ray.y * 83 - ray.z * 7);
                float cloud = smoothstep(.54, 1.46, bands) * smoothstep(.03, .10, elevation)
                    * (1 - smoothstep(.17, .40, elevation));
                color = lerp(color, float3(.87, .84, .76), cloud * .21);
                return float4(color, 1);
            }
            ENDCG
        }
    }
}
