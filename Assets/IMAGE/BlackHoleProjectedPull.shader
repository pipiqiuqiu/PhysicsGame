Shader "Custom/Black Hole Projected Pull"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1, 1, 1, 1)
        [HideInInspector] _TargetUV ("Projected Target", Vector) = (0.5, 0.5, 0, 0)
        [HideInInspector] _PullStrength ("Pull Strength", Range(0, 0.2)) = 0
        [PerRendererData] _FalloffTex ("Radial Falloff", 2D) = "white" {}
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

            sampler2D _MainTex;
            sampler2D _FalloffTex;
            fixed4 _Color;
            float4 _TargetUV;
            float _PullStrength;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                half4 color : COLOR;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                half4 color : COLOR;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = UnityObjectToClipPos(input.positionOS);
                output.uv = input.uv;
                output.color = input.color * _Color;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                // Both the target and the deformation live in the sprite's 2D plane.
                float2 fromCenter = (input.uv - 0.5) * 2.0;
                float radius = length(fromCenter);
                float centerWeight = radius >= 1.0 ? 0.0 :
                    tex2D(_FalloffTex, float2(radius, 0.5)).r;

                float2 towardTarget = _TargetUV.xy - input.uv;
                float2 pullDirection = towardTarget / max(length(towardTarget), 0.08);
                float2 sourceUV = input.uv - pullDirection * (_PullStrength * centerWeight);

                return tex2D(_MainTex, sourceUV) * input.color;
            }
            ENDCG
        }
    }

    Fallback Off
}
