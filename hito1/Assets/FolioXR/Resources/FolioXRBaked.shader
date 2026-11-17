// Entornos horneados (Claude, 11 nov 2026): la textura ya trae el color y la luz calculados en
// Blender, así que no se ilumina nada en el visor. Un ruido fino repetido («detalle») disimula
// el desenfoque de la textura cuando se mira de muy cerca. No descarta caras traseras: las hojas
// y las cortinas se ven por ambos lados sin duplicar triángulos.
// Desvanecido (16 nov 2026): el terreno cercano termina a unos 34 m y más allá se ve el panorama. Para que
// no se note el borde, sus últimos metros se van deshaciendo en puntos (cobertura de MSAA), sin mezclar
// colores ni cambiar el orden de dibujo.
// Linterna (17 nov 2026): la luz horneada no cambia, pero la linterna de la bandeja suma su haz con los
// valores globales _FolioTorch* (TripleFlashlight). La normal sale de las derivadas de la posición.
Shader "FolioXR/Baked"
{
    Properties
    {
        _MainTex ("Luz horneada", 2D) = "white" {}
        _DetailTex ("Detalle", 2D) = "gray" {}
        _DetailTiling ("Repeticiones del detalle", Float) = 0
        _DetailStrength ("Fuerza del detalle", Range(0, 1)) = 0
        _Fade ("Desvanecer entre estos radios (inicio, fin; 0 = no; z = 1 sin MSAA)", Vector) = (0, 0, 0, 0)
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" "IgnoreProjector"="True" }
        Cull Off
        ZWrite On
        Pass
        {
            AlphaToMask On
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
                float2 flat : TEXCOORD2;   // Posición horizontal en la malla (metros desde el centro de la casa).
                float3 world : TEXCOORD3;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            sampler2D _MainTex;
            sampler2D _DetailTex;
            float _DetailTiling;
            float _DetailStrength;
            float4 _Fade;
            float4 _FolioTorchPos;     // xyz posición, w = 1 encendida.
            float4 _FolioTorchDir;     // xyz dirección, w = coseno del borde del haz.
            float4 _FolioTorchColor;   // rgb color, w = alcance en metros.

            v2f vert (appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_OUTPUT(v2f, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                o.detail = v.uv * _DetailTiling;
                o.flat = v.vertex.xz;
                o.world = mul(unity_ObjectToWorld, v.vertex).xyz;
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                fixed3 c = tex2D(_MainTex, i.uv).rgb;
                fixed d = tex2D(_DetailTex, i.detail).r;
                c *= lerp(1.0, d * 2.0, _DetailStrength);
                float3 facing = normalize(cross(ddy(i.world), ddx(i.world)));
                if (_FolioTorchPos.w > 0.5)
                {
                    float3 toPoint = i.world - _FolioTorchPos.xyz;
                    float dist = max(length(toPoint), 0.001);
                    float3 dir = toPoint / dist;
                    float cone = smoothstep(_FolioTorchDir.w, lerp(_FolioTorchDir.w, 1.0, 0.55), dot(dir, _FolioTorchDir.xyz));
                    float reach = saturate(1.0 - dist / _FolioTorchColor.w);
                    float falloff = reach * reach / (1.0 + 0.12 * dist * dist);
                    float lambert = 0.3 + 0.7 * abs(dot(facing, dir));
                    float lum = dot(c, float3(0.2126, 0.7152, 0.0722));
                    // Más notoria donde está oscuro; de día apenas aclara.
                    float k = cone * falloff * lambert * saturate(1.15 - lum * 1.4);
                    c += _FolioTorchColor.rgb * k * (c * 4.0 + 0.5);
                }
                float alpha = 1.0;
                if (_Fade.y > 0.0)
                {
                    alpha = saturate((_Fade.y - length(i.flat)) / max(0.01, _Fade.y - _Fade.x));
                    alpha = alpha * alpha * (3.0 - 2.0 * alpha);
                    // Ruido por píxel: reparte los escalones de la cobertura para que no se vean franjas.
                    float n = frac(52.9829189 * frac(dot(i.pos.xy, float2(0.06711056, 0.00583715))));
                    if (_Fade.z > 0.5)
                    {
                        // Sin MSAA no hay cobertura que repartir: el borde se deshace descartando píxeles.
                        clip(alpha >= 0.999 ? 1.0 : alpha - n * 0.98 - 0.01);
                        alpha = 1.0;
                    }
                    else
                    {
                        alpha = alpha >= 0.999 ? 1.0 : alpha + (n - 0.5) * 0.3;
                        clip(alpha - 0.04);
                    }
                }
                return fixed4(c, saturate(alpha));
            }
            ENDCG
        }
    }
}
