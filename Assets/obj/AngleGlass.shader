Shader "Custom/Angle Glass Unlit"
{
    Properties
    {
        _TintColor ("Glass Color / Overall Alpha", Color) = (0.52, 0.83, 0.95, 1)
        _FaceOpacity ("Face Opacity", Range(0, 1)) = 0.12
        _EdgeOpacity ("Edge Opacity", Range(0, 1)) = 0.72
        _ClearUntilAngle ("Clear Until Angle (Degrees)", Range(0, 90)) = 25
        _OpaqueAtAngle ("Edge Opacity At Angle (Degrees)", Range(0, 90)) = 75
    }

    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" }
        Cull Off
        ZWrite Off
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            CGPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "UnityCG.cginc"

            fixed4 _TintColor;
            float _FaceOpacity;
            float _EdgeOpacity;
            float _ClearUntilAngle;
            float _OpaqueAtAngle;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
            };

            Varyings Vert(Attributes v)
            {
                Varyings o;
                o.positionCS = UnityObjectToClipPos(v.positionOS);
                o.positionWS = mul(unity_ObjectToWorld, v.positionOS).xyz;
                o.normalWS = UnityObjectToWorldNormal(v.normalOS);
                return o;
            }

            fixed4 Frag(Varyings i) : SV_Target
            {
                float3 viewDirectionWS = normalize(_WorldSpaceCameraPos - i.positionWS);
                float facing = saturate(abs(dot(normalize(i.normalWS), viewDirectionWS)));

                // The dot product is 1 when viewed head-on and 0 at a grazing angle.
                float clearAngle = min(_ClearUntilAngle, 89.9);
                float edgeAngle = max(_OpaqueAtAngle, clearAngle + 0.1);
                float clearFacing = cos(radians(clearAngle));
                float edgeFacing = cos(radians(edgeAngle));
                float edgeAmount = saturate((clearFacing - facing) /
                                            max(0.0001, clearFacing - edgeFacing));
                edgeAmount = edgeAmount * edgeAmount * (3.0 - 2.0 * edgeAmount);

                float opacity = lerp(_FaceOpacity, _EdgeOpacity, edgeAmount) * _TintColor.a;
                return fixed4(_TintColor.rgb, saturate(opacity));
            }
            ENDCG
        }
    }

    Fallback Off
}
