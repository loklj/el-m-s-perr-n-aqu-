// Entornos horneados (Claude, 11 nov 2026): la textura ya trae el color y la luz calculados en
// Blender, así que no se ilumina nada en el visor. Un ruido fino repetido («detalle») disimula
// el desenfoque de la textura cuando se mira de muy cerca. No descarta caras traseras: las hojas
// y las cortinas se ven por ambos lados sin duplicar triángulos.
Shader "FolioXR/Baked"
{
    Properties
    {
        _MainTex ("Luz horneada", 2D) = "white" {}
        _DetailTex ("Detalle", 2D) = "gray" {}
        _DetailTiling ("Repeticiones del detalle", Float) = 0
        _DetailStrength ("Fuerza del detalle", Range(0, 1)) = 0
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" "IgnoreProjector"="True" }
        Cull Off
        ZWrite On
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
                float2 detail : TEXCOORD1;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            sampler2D _MainTex;
            sampler2D _DetailTex;
            float _DetailTiling;
            float _DetailStrength;

            v2f vert (appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_OUTPUT(v2f, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                o.detail = v.uv * _DetailTiling;
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                fixed3 c = tex2D(_MainTex, i.uv).rgb;
                fixed d = tex2D(_DetailTex, i.detail).r;
                c *= lerp(1.0, d * 2.0, _DetailStrength);
                return fixed4(c, 1.0);
            }
            ENDCG
        }
    }
}
