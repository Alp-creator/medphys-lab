// Additive glow for beams, particles and particle tracks (URP).
Shader "MedPhys/Additive"
{
    Properties
    {
        _Color ("Color", Color) = (1,1,1,1)
        _Intensity ("Intensity", Float) = 1
        _Fresnel ("Fresnel edge fade (0..1)", Range(0,1)) = 0
        _MainTex ("Texture", 2D) = "white" {}
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent+10" "RenderPipeline"="UniversalPipeline" "IgnoreProjector"="True" }
        Blend One One
        ZWrite Off
        Cull Off
        Pass
        {
            Name "Additive"
            Tags { "LightMode"="UniversalForward" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            CBUFFER_START(UnityPerMaterial)
                float4 _Color;
                float _Intensity;
                float _Fresnel;
                float4 _MainTex_ST;
            CBUFFER_END

            struct A { float4 pos : POSITION; float3 n : NORMAL; float2 uv : TEXCOORD0; float4 col : COLOR; };
            struct V { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; float4 col : COLOR; float3 nW : TEXCOORD1; float3 vW : TEXCOORD2; float fog : TEXCOORD3; };

            V vert(A i)
            {
                V o;
                float3 w = TransformObjectToWorld(i.pos.xyz);
                o.pos = TransformWorldToHClip(w);
                o.uv = TRANSFORM_TEX(i.uv, _MainTex);
                o.col = i.col;
                o.nW = TransformObjectToWorldNormal(i.n);
                o.vW = GetWorldSpaceViewDir(w);
                o.fog = ComputeFogFactor(o.pos.z);
                return o;
            }

            half4 frag(V i) : SV_Target
            {
                half4 t = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv);
                half4 c = _Color * i.col * t;
                float f = 1;
                if (_Fresnel > 0)
                {
                    float ndv = abs(dot(normalize(i.nW), normalize(i.vW)));
                    f = lerp(1, ndv * ndv, _Fresnel);
                }
                half3 rgb = c.rgb * c.a * _Intensity * f;
                rgb *= saturate(i.fog * 0.5 + 0.5);
                return half4(rgb, 0);
            }
            ENDHLSL
        }
    }
}
