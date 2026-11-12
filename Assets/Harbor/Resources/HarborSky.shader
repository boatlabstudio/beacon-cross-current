Shader "Harbor/Sky"
{
    SubShader
    {
        Tags { "Queue"="Background" "RenderType"="Background" "PreviewType"="Skybox" }
        Cull Off ZWrite Off
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            struct VertexInput { float4 vertex : POSITION; };
            struct Varyings { float4 position : SV_POSITION; float3 direction : TEXCOORD0; };
            Varyings vert(VertexInput v)
            {
                Varyings o;
                o.position = UnityObjectToClipPos(v.vertex);
                o.direction = v.vertex.xyz;
                return o;
            }
            fixed4 frag(Varyings i) : SV_Target
            {
                float3 ray = normalize(i.direction);
                float lift = saturate(ray.y);
                float3 haze = float3(.46, .61, .64);
                float3 zenith = float3(.13, .33, .46);
                float3 sky = lerp(haze, zenith, pow(lift, .55));
                float sun = saturate(dot(ray, normalize(float3(-.43, .30, -.49))));
                sky += float3(.35, .20, .10) * pow(sun, 15) * (1 - lift * .65);
                return fixed4(sky, 1);
            }
            ENDCG
        }
    }
}
