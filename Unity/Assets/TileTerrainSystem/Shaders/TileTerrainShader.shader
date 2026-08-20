Shader "Custom/TileTerrainShader"
{
    Properties
    {
        _Texture_Over   ("Texture Over",   2DArray) = "" {}
        _Texture_Mid    ("Texture Mid",    2DArray) = "" {}
        _Texture_Under  ("Texture Under",  2DArray) = "" {}
        _CliffSideTex   ("Cliff Side Texture", 2D) = "black" {}
        _Smoothness ("Smoothness", Range(0,1)) = 0.5
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline" = "UniversalPipeline" }
        LOD 100

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile _ _SHADOWS_SOFT
            #pragma multi_compile_fog
            #pragma multi_compile_fragment _ _DBUFFER_MRT1 _DBUFFER_MRT2 _DBUFFER_MRT3

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DBuffer.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 uv         : TEXCOORD0;   // xy = tile UV, z unused
                float3 uv2        : TEXCOORD1;   // xy = cliff UV, z = flag (1.0 = cliff, 0.0 = flat)
                float3 uv3        : TEXCOORD2;   // x = overIdx, y = midIdx, z = underIdx
                float4 color      : COLOR;
                float3 normalOS   : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                float3 indices    : TEXCOORD1;   // x = overIdx, y = midIdx, z = underIdx
                float4 color      : COLOR;
                float3 normalWS   : TEXCOORD3;
                float3 positionWS : TEXCOORD4;
                float3 cliffUV    : TEXCOORD5;   // xy = cliff UV, z = flag
                // shadowCoord removed — computed per-pixel in frag to avoid
                // dark circle artifacts caused by wrong cascade selection
                // when interpolating across large tile quads.
            };

            TEXTURE2D_ARRAY(_Texture_Over);   SAMPLER(sampler_Texture_Over);
            TEXTURE2D_ARRAY(_Texture_Mid);    SAMPLER(sampler_Texture_Mid);
            TEXTURE2D_ARRAY(_Texture_Under);  SAMPLER(sampler_Texture_Under);
            TEXTURE2D(_CliffSideTex);         SAMPLER(sampler_CliffSideTex);

            CBUFFER_START(UnityPerMaterial)
            float _Smoothness;
            CBUFFER_END

            Varyings vert (Attributes input)
            {
                Varyings output;
                VertexPositionInputs posInputs = GetVertexPositionInputs(input.positionOS.xyz);
                VertexNormalInputs normInputs = GetVertexNormalInputs(input.normalOS);

                output.positionCS = posInputs.positionCS;
                output.positionWS = posInputs.positionWS;
                output.normalWS = normInputs.normalWS;

                output.uv      = input.uv.xy;
                output.cliffUV = input.uv2;
                output.indices = input.uv3;
                output.color   = input.color;

                return output;
            }

            float4 frag (Varyings input) : SV_Target
            {
                // Round indices to ensure we sample the exact slice.
                float overIdx   = round(input.indices.x);
                float midIdx    = round(input.indices.y);
                float underIdx  = round(input.indices.z);

                // ── Blending Logic ──────────────────────────────────────────
                // We start with a transparent base and layer active textures
                // on top. To avoid darkening the base layer during transitions,
                // the FIRST active layer from the bottom up is treated as opaque
                // (or rather, its color is assigned directly to the albedo).
                
                float4 albedo = float4(0, 0, 0, 0);
                bool baseSet = false;

                // 1. Under Layer
                if (underIdx >= 0)
                {
                    albedo = SAMPLE_TEXTURE2D_ARRAY(_Texture_Under, sampler_Texture_Under, input.uv, underIdx);
                    baseSet = true;
                }

                // 2. Mid Layer
                if (midIdx >= 0)
                {
                    float4 colMid = SAMPLE_TEXTURE2D_ARRAY(_Texture_Mid, sampler_Texture_Mid, input.uv, midIdx);
                    if (!baseSet) {
                        albedo = colMid;
                        baseSet = true;
                    } else {
                        albedo = lerp(albedo, colMid, colMid.a);
                    }
                }

                // 3. Over Layer
                if (overIdx >= 0)
                {
                    float4 colOver = SAMPLE_TEXTURE2D_ARRAY(_Texture_Over, sampler_Texture_Over, input.uv, overIdx);
                    if (!baseSet) {
                        albedo = colOver;
                        baseSet = true;
                    } else {
                        albedo = lerp(albedo, colOver, colOver.a);
                    }
                }

                // Apply vertex color (tint/shadows)
                albedo.rgb *= input.color.rgb;

                // 4. Overlap permanent cliff side texture if on a cliff vertex
                if (input.cliffUV.z > 0.5)
                {
                    float4 cliffSideCol = SAMPLE_TEXTURE2D(_CliffSideTex, sampler_CliffSideTex, input.cliffUV.xy);
                    albedo.rgb = lerp(albedo.rgb, cliffSideCol.rgb, cliffSideCol.a);
                }

                // ── Decal ────────────────────────────────────────────────
                float3 normalWS = normalize(input.normalWS);
                #if defined(_DBUFFER)
                    half3 decalSpec = 0;
                    half decalMetallic = 0;
                    half decalOcclusion = 1;
                    half decalSmoothness = 0.5;
                    ApplyDecal(input.positionCS, albedo.rgb, decalSpec, normalWS, decalMetallic, decalOcclusion, decalSmoothness);
                #endif

                // ── Lighting ─────────────────────────────────────────────

                // Compute shadow coord per-pixel from world position.
                // This is the fix for dark circle/halo artifacts: with shadow cascades,
                // cascade selection must be done per-pixel. Doing it in the vertex shader
                // and interpolating across large quads causes the wrong cascade to be
                // selected at triangle edges, producing the dark rings.
                float4 shadowCoord = TransformWorldToShadowCoord(input.positionWS);
                Light mainLight = GetMainLight(shadowCoord);

                float NdotL = saturate(dot(normalWS, mainLight.direction));
                float3 diffuse = NdotL
                               * mainLight.color
                               * mainLight.shadowAttenuation
                               * mainLight.distanceAttenuation;

                float3 ambient = SampleSH(normalWS) * albedo.rgb;
                float3 finalRGB = (diffuse * albedo.rgb) + ambient;

                return float4(finalRGB, 1.0);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }

            ZWrite On
            ZTest LEqual
            ColorMask 0

            HLSLPROGRAM
            #pragma vertex ShadowPassVertex
            #pragma fragment ShadowPassFragment

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
            };

            Varyings ShadowPassVertex(Attributes input)
            {
                Varyings output;
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                float3 normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.positionCS = TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, _MainLightPosition.xyz));
                return output;
            }

            float4 ShadowPassFragment(Varyings input) : SV_TARGET
            {
                return 0;
            }
            ENDHLSL
        }
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }

            ZWrite On
            ColorMask 0

            HLSLPROGRAM
            #pragma vertex DepthOnlyVertex
            #pragma fragment DepthOnlyFragment

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
            };

            Varyings DepthOnlyVertex(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                return output;
            }

            float4 DepthOnlyFragment(Varyings input) : SV_TARGET
            {
                return 0;
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }

            ZWrite On

            HLSLPROGRAM
            #pragma vertex DepthNormalsVertex
            #pragma fragment DepthNormalsFragment
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 normalWS : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings DepthNormalsVertex(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                VertexPositionInputs vertexInput = GetVertexPositionInputs(input.positionOS.xyz);
                VertexNormalInputs normalInput = GetVertexNormalInputs(input.normalOS);

                output.positionCS = vertexInput.positionCS;
                output.normalWS = normalInput.normalWS;
                return output;
            }

            float4 DepthNormalsFragment(Varyings input) : SV_TARGET
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                #if defined(_GBUFFER_NORMALS_OCT)
                    float3 normalWS = normalize(input.normalWS);
                    float2 octNormalWS = PackNormalOctQuadEncode(normalWS);
                    float2 remappedOctNormalWS = saturate(octNormalWS * 0.5 + 0.5);
                    half3 packedNormalWS = PackFloat2To888(remappedOctNormalWS);
                    return half4(packedNormalWS, 0.0);
                #else
                    return half4(NormalizeNormalPerPixel(input.normalWS), 0.0);
                #endif
            }
            ENDHLSL
        }
    }
}
