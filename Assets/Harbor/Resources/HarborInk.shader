Shader "Harbor/Ink"
{
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Cull Off
        ZWrite Off
        Blend SrcAlpha OneMinusSrcAlpha
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            struct VertexInput { float4 vertex : POSITION; fixed4 color : COLOR; };
            struct Varyings { float4 position : SV_POSITION; fixed4 color : COLOR; };
            Varyings vert(VertexInput v)
            {
                Varyings o;
                o.position = UnityObjectToClipPos(v.vertex);
                o.color = v.color;
                return o;
            }
            fixed4 frag(Varyings i) : SV_Target { return i.color; }
            ENDCG
        }
    }
}
