// Transparent fresnel "hologram" surface used for the phantom skin and ghosted equipment (URP).
// Optional clip plane (world space): fragments with dot(pos, plane.xyz) > plane.w are discarded.
Shader "MedPhys/Hologram"
{
    Properties
    {
        _Color ("Body colour (alpha = opacity)", Color) = (0.4,0.8,1,0.08)
        _RimColor ("Rim colour (alpha = rim opacity)", Color) = (0.5,0.9,1,0.8)
        _RimPower ("Rim power", Float) = 2.5
        _Scan ("Scan line strength", Range(0,1)) = 0.15
        _ClipPlane ("Clip plane (xyz normal, w offset)", Vector) = (0,0,0,0)
        _UseClip ("Use clip plane", Float) = 0
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "RenderPipeline"="UniversalPipeline" "IgnoreProjector"="True" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Back
        Pass
        {
            Name "Hologram"
            Tags { "LightMode"="UniversalForward" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _Color;
                float4 _RimColor;
                float _RimPower;
                float _Scan;
                float4 _ClipPlane;
                float _UseClip;
            CBUFFER_END

            struct A { float4 pos : POSITION; float3 n : NORMAL; };
            struct V { float4 pos : SV_POSITION; float3 nW : TEXCOORD0; float3 wP : TEXCOORD1; };

            V vert(A i)
            {
                V o;
                float3 w = TransformObjectToWorld(i.pos.xyz);
                o.wP = w;
                o.pos = TransformWorldToHClip(w);
                o.nW = TransformObjectToWorldNormal(i.n);
                return o;
            }

            half4 frag(V i) : SV_Target
            {
                if (_UseClip > 0.5 && dot(i.wP, _ClipPlane.xyz) > _ClipPlane.w) discard;
                float3 v = normalize(GetWorldSpaceViewDir(i.wP));
                float ndv = saturate(abs(dot(normalize(i.nW), v)));
                float rim = pow(1.0 - ndv, _RimPower);
                float scan = 1.0 + _Scan * sin(i.wP.y * 220.0 + _Time.y * 3.0);
                half3 rgb = _Color.rgb * (0.6 + 0.4 * ndv) + _RimColor.rgb * rim;
                half a = saturate((_Color.a + _RimColor.a * rim) * scan);
                return half4(rgb, a);
            }
            ENDHLSL
        }
    }
}
