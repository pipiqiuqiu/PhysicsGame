Shader "Custom/Gear Distance Toon"
{
    Properties
    {
        _BaseColor ("Lit / Base Color", Color) = (0.95, 0.73, 0.38, 1)
        _ShadowColor ("Shadow Color", Color) = (0.18, 0.17, 0.29, 1)
        _DistanceColor ("Distant Color", Color) = (0.35, 0.68, 0.88, 1)
        _DistanceStart ("Distance Start", Float) = 150
        _DistanceEnd ("Distance End", Float) = 700
        _DistanceStrength ("Distant Color Strength", Range(0, 1)) = 0.7
        _LightThreshold ("Light / Shadow Threshold", Range(0, 1)) = 0.45
        _LightFeather ("Light Edge Softness", Range(0.001, 0.5)) = 0.025
    }

    SubShader
    {
        Tags { "Queue" = "Geometry" "RenderType" = "Opaque" }
        Cull Back
        ZWrite On

        Pass
        {
            Tags { "LightMode" = "ForwardBase" }

            CGPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fwdbase

            #include "UnityCG.cginc"
            #include "Lighting.cginc"
            #include "AutoLight.cginc"

            fixed4 _BaseColor;
            fixed4 _ShadowColor;
            fixed4 _DistanceColor;
            float _DistanceStart;
            float _DistanceEnd;
            float _DistanceStrength;
            float _LightThreshold;
            float _LightFeather;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
            };

            struct Varyings
            {
                float4 pos : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                SHADOW_COORDS(2)
            };

            Varyings Vert(Attributes v)
            {
                Varyings o;
                o.pos = UnityObjectToClipPos(v.positionOS);
                o.positionWS = mul(unity_ObjectToWorld, v.positionOS).xyz;
                o.normalWS = UnityObjectToWorldNormal(v.normalOS);
                TRANSFER_SHADOW(o);
                return o;
            }

            fixed4 Frag(Varyings i) : SV_Target
            {
                float3 normalWS = normalize(i.normalWS);
                float3 lightDirectionWS = normalize(
                    _WorldSpaceLightPos0.xyz - i.positionWS * _WorldSpaceLightPos0.w);
                float directLight = saturate(dot(normalWS, lightDirectionWS));
                float lightIntensity = saturate(dot(_LightColor0.rgb,
                    float3(0.2126, 0.7152, 0.0722)));
                float lightValue = directLight * SHADOW_ATTENUATION(i) * lightIntensity;
                float lightMask = smoothstep(_LightThreshold - _LightFeather,
                                             _LightThreshold + _LightFeather,
                                             lightValue);

                // Lighting selects one of two fixed colors; it never scales the
                // rendered color continuously like a conventional lit shader.
                float3 nearColor = lerp(_ShadowColor.rgb, _BaseColor.rgb, lightMask);
                float cameraDistance = distance(_WorldSpaceCameraPos, i.positionWS);
                float distanceBlend = saturate((cameraDistance - _DistanceStart) /
                    max(0.001, _DistanceEnd - _DistanceStart)) * _DistanceStrength;
                return fixed4(lerp(nearColor, _DistanceColor.rgb, distanceBlend), 1);
            }
            ENDCG
        }
    }

    Fallback "Diffuse"
}
