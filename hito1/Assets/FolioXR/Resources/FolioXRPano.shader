// Agua cercana de los entornos horneados (Claude, 16 nov 2026). El cielo panorámico solo es exacto mirado
// desde su centro: desde el balcón o el muelle, donde debía verse el lago se veía el suelo cercano. Esta
// malla plana (del borde del agua hasta el cielo) se pinta con el mismo panorama, proyectado desde su
// centro: mirada desde ahí es idéntica al cielo, y desde cualquier otro lugar el agua queda donde va.
Shader "FolioXR/Pano"
{
    Properties
    {
        _MainTex ("Panorama", 2D) = "white" {}
        _Eye ("Centro del panorama (en la malla)", Vector) = (0, 1.6, 0, 0)
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry+10" "IgnoreProjector"="True" }
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
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 ray : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            sampler2D _MainTex;
            float4 _Eye;

            v2f vert (appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_OUTPUT(v2f, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.pos = UnityObjectToClipPos(v.vertex);
                o.ray = v.vertex.xyz - _Eye.xyz;
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                // Misma cuenta que la esfera del cielo (BakedEnvironment.SkySphere), al revés.
                float3 d = normalize(i.ray);
                float u = atan2(d.x, d.z) * 0.15915494 + 0.5;
                float v = asin(clamp(d.y, -1.0, 1.0)) * 0.31830989 + 0.5;
                // La longitud salta de 1 a 0 justo hacia el frente: ahí las derivadas se toman de la otra vuelta.
                float u2 = frac(u + 0.5) - 0.5;
                float2 smooth = fwidth(u) <= fwidth(u2) ? float2(u, v) : float2(u2, v);
                return fixed4(tex2Dgrad(_MainTex, float2(u, v), ddx(smooth), ddy(smooth)).rgb, 1.0);
            }
            ENDCG
        }
    }
}
