Shader "TileTerrain/FogOfWar"
{
    Properties
    {
        [HideInInspector] _MaskTex        ("Mask", 2D) = "black" {}
        [HideInInspector] _FogColor       ("Fog Color", Color) = (0.02, 0.02, 0.04, 1)
        [HideInInspector] _ExploredColor  ("Explored Color", Color) = (0.35, 0.35, 0.4, 0.55)
        [HideInInspector] _OutsideGridFog ("Outside Grid Fog", Range(0,1)) = 1
        [HideInInspector] _FogBlur        ("Fog Blur (UV)", Range(0,0.1)) = 0.025
        [HideInInspector] _GridOffset     ("Grid Offset (xy)", Vector) = (0,0,0,0)
        [HideInInspector] _GridWorldSize  ("Grid World Size (xy)", Vector) = (1,1,0,0)
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }
        ZTest Always ZWrite Off Cull Off

        Pass
        {
            Name "FogOfWar"

            HLSLPROGRAM
            #pragma vertex   Vert
            #pragma fragment Frag

            // URP 17 / Unity 6 — required includes
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            TEXTURE2D(_MaskTex);
            SAMPLER(sampler_MaskTex);

            float4 _FogColor;
            float4 _ExploredColor;
            float  _OutsideGridFog;
            float  _FogBlur;
            float4 _GridOffset;     // xy = world offset of grid origin (cell 0,0 is at _GridOffset.xy in world XZ)
            float4 _GridWorldSize;  // xy = full grid size in world units

            // 13-tap circular Poisson-style sample. Returns RG average.
            float2 SampleMaskBlurred(float2 uv, float radius)
            {
                // Centre tap.
                float2 sum = SAMPLE_TEXTURE2D(_MaskTex, sampler_MaskTex, uv).rg;
                // 12 taps on a circle. Angles pre-computed (2π/12 = 30°).
                sum += SAMPLE_TEXTURE2D(_MaskTex, sampler_MaskTex, uv + float2( 1.0000000,  0.0000000) * radius).rg;
                sum += SAMPLE_TEXTURE2D(_MaskTex, sampler_MaskTex, uv + float2( 0.8660254,  0.5000000) * radius).rg;
                sum += SAMPLE_TEXTURE2D(_MaskTex, sampler_MaskTex, uv + float2( 0.5000000,  0.8660254) * radius).rg;
                sum += SAMPLE_TEXTURE2D(_MaskTex, sampler_MaskTex, uv + float2( 0.0000000,  1.0000000) * radius).rg;
                sum += SAMPLE_TEXTURE2D(_MaskTex, sampler_MaskTex, uv + float2(-0.5000000,  0.8660254) * radius).rg;
                sum += SAMPLE_TEXTURE2D(_MaskTex, sampler_MaskTex, uv + float2(-0.8660254,  0.5000000) * radius).rg;
                sum += SAMPLE_TEXTURE2D(_MaskTex, sampler_MaskTex, uv + float2(-1.0000000,  0.0000000) * radius).rg;
                sum += SAMPLE_TEXTURE2D(_MaskTex, sampler_MaskTex, uv + float2(-0.8660254, -0.5000000) * radius).rg;
                sum += SAMPLE_TEXTURE2D(_MaskTex, sampler_MaskTex, uv + float2(-0.5000000, -0.8660254) * radius).rg;
                sum += SAMPLE_TEXTURE2D(_MaskTex, sampler_MaskTex, uv + float2( 0.0000000, -1.0000000) * radius).rg;
                sum += SAMPLE_TEXTURE2D(_MaskTex, sampler_MaskTex, uv + float2( 0.5000000, -0.8660254) * radius).rg;
                sum += SAMPLE_TEXTURE2D(_MaskTex, sampler_MaskTex, uv + float2( 0.8660254, -0.5000000) * radius).rg;
                return sum * (1.0 / 13.0);
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                float2 uv = input.texcoord;

                // 1) Sample scene depth, reconstruct world position.
                float rawDepth = SampleSceneDepth(uv);
                float3 worldPos = ComputeWorldSpacePosition(uv, rawDepth, UNITY_MATRIX_I_VP);

                // 2) World XZ → grid UV.
                float2 localXZ = worldPos.xz - _GridOffset.xy;
                float2 gridUV  = localXZ / max(_GridWorldSize.xy, 1e-4);
                bool   outside = any(gridUV < 0.0) || any(gridUV > 1.0);
                gridUV = saturate(gridUV);

                // 3) Sample mask with circular blur for soft fog edge.
                float2 maskRG = SampleMaskBlurred(gridUV, _FogBlur);
                float visible  = maskRG.r;
                float explored = maskRG.g;

                // 4) Combine states.
                float dimStrength = saturate(_ExploredColor.a);
                float dimVis      = explored * dimStrength;
                float vis         = max(visible, dimVis);

                // Outside the grid: optionally fog everything.
                vis = lerp(vis, 0.0, _OutsideGridFog * (outside ? 1.0 : 0.0));

                // 5) Scene + fog blend.
                float3 sceneColor = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv).rgb;
                float3 outCol     = lerp(_FogColor.rgb, sceneColor, vis);

                // 6) Tint explored-only areas.
                float exploredOnly = saturate(saturate(explored) - saturate(visible));
                outCol = lerp(outCol, _ExploredColor.rgb, exploredOnly * dimStrength * 0.85);

                return half4(outCol, 1.0);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
