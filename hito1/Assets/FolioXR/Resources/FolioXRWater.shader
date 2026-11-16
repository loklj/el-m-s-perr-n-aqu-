// Agua de los entornos horneados (Claude, 16 nov 2026; rehecha el mismo día tras la prueba en el visor).
// Un plano a la altura del agua que refleja el cielo panorámico según desde dónde se mira, con ondas
// suaves. La primera versión proyectaba el panorama sobre el plano y, al caminar, el paisaje pintado
// quedaba estirado sobre el agua.
Shader "FolioXR/Water"
{
    Properties
    {
        _MainTex ("Panorama del cielo", 2D) = "white" {}
        _Color ("Color del agua", Color) = (0.14, 0.27, 0.35, 1)
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
                float3 view : TEXCOORD0;    // Del ojo al punto, en coordenadas de la malla (las del panorama).
                float2 flat : TEXCOORD1;    // Posición horizontal en la malla, en metros.
                UNITY_VERTEX_OUTPUT_STEREO
            };

            sampler2D _MainTex;
            fixed4 _Color;

            v2f vert (appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_OUTPUT(v2f, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.pos = UnityObjectToClipPos(v.vertex);
                float3 eye = mul(unity_WorldToObject, float4(_WorldSpaceCameraPos, 1.0)).xyz;
                o.view = v.vertex.xyz - eye;
                o.flat = v.vertex.xz;
                return o;
            }

            float3 pano (float3 d, float lod)
            {
                // Misma cuenta que la esfera del cielo (BakedEnvironment.SkySphere), al revés.
                float u = atan2(d.x, d.z) * 0.15915494 + 0.5;
                float v = asin(clamp(d.y, -1.0, 1.0)) * 0.31830989 + 0.5;
                return tex2Dlod(_MainTex, float4(u, v, 0.0, lod)).rgb;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                float dist = length(i.view);
                float3 v = i.view / max(dist, 0.001);
                // Ondas: tres trenes de olas que se cruzan; de lejos se aplanan para que no titilen.
                float t = _Time.y;
                float2 p = i.flat;
                float2 slope = float2(0.0, 0.0);
                slope += float2(0.80, 0.60) * cos(dot(p, float2(0.80, 0.60)) * 2.3 + t * 1.1);
                slope += float2(-0.50, 0.87) * cos(dot(p, float2(-0.50, 0.87)) * 3.7 - t * 1.6) * 0.6;
                slope += float2(0.26, -0.97) * cos(dot(p, float2(0.26, -0.97)) * 6.1 + t * 2.3) * 0.35;
                float calm = saturate(10.0 / dist);
                float3 n = normalize(float3(-slope.x * 0.035 * calm, 1.0, -slope.y * 0.035 * calm));
                if (v.y > 0.0) n = -n;                                  // Visto desde abajo del agua.
                float3 r = reflect(v, n);
                r.y = abs(r.y) + 0.015;
                r = normalize(r);
                float lod = clamp(log2(max(dist, 1.0) * 0.12), 0.0, 4.0);
                float3 mirror = pano(r, lod);
                // Luz general del entorno: el cielo cerca del cenit.
                float3 sky = pano(float3(0.0, 1.0, 0.0), 6.0);
                float light = dot(sky, float3(0.3, 0.55, 0.15));
                float3 deep = _Color.rgb * (0.25 + 0.9 * light);
                float facing = saturate(dot(-v, n));
                float fresnel = clamp(0.03 + 0.97 * pow(1.0 - facing, 5.0), 0.10, 0.90);
                return fixed4(lerp(deep, mirror, fresnel), 1.0);
            }
            ENDCG
        }
    }
}
