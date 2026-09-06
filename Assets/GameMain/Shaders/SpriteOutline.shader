Shader "SepCore/2D/SpriteOutline"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        [MaterialToggle] PixelSnap ("Pixel snap", Float) = 0
        [HideInInspector] _RendererColor ("RendererColor", Color) = (1,1,1,1)
        [HideInInspector] _Flip ("Flip", Vector) = (1,1,1,1)
        [PerRendererData] _SpriteUVRect ("Sprite UV Rect", Vector) = (0,0,1,1)
        [PerRendererData] _SpriteSize ("Sprite Size (Units)", Vector) = (1,1,0,0)

        [HDR] _OutlineColor ("Outline Color", Color) = (1, 0.9, 0.2, 1)
        _OutlineSize ("Outline Size (Pixels)", Range(0, 16)) = 1.5
        _AlphaThreshold ("Alpha Threshold", Range(0.01, 1)) = 0.1
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Transparent"
            "IgnoreProjector" = "True"
            "RenderType" = "Transparent"
            "PreviewType" = "Plane"
            "CanUseSpriteAtlas" = "True"
            "RenderPipeline" = "UniversalPipeline"
            // Vertex padding is calculated in object space.
            "DisableBatching" = "True"
        }

        Cull Off
        Lighting Off
        ZWrite Off
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            Name "Universal2D"
            Tags { "LightMode" = "Universal2D" }

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma multi_compile _ PIXELSNAP_ON

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS   : POSITION;
                float4 color        : COLOR;
                float2 uv           : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS   : SV_POSITION;
                float4 color        : COLOR;
                float2 uv           : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);
            float4 _MainTex_TexelSize;

            CBUFFER_START(UnityPerMaterial)
                float4 _Color;
                float4 _RendererColor;
                float4 _Flip;
                float4 _OutlineColor;
                float4 _SpriteUVRect;
                float4 _SpriteSize;
                float _OutlineSize;
                float _AlphaThreshold;
            CBUFFER_END

            Varyings vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);

                float4 pos = input.positionOS;
                // Full Rect mesh: extend geometry and UV together to preserve the sprite scale.
                float2 uvSize = _SpriteUVRect.zw - _SpriteUVRect.xy;
                float2 direction = ((input.uv - _SpriteUVRect.xy) / uvSize) * 2.0 - 1.0;
                float2 uvPadding = direction * _MainTex_TexelSize.xy * _OutlineSize;
                pos.xy += uvPadding / uvSize * _SpriteSize.xy;
                pos.xy *= _Flip.xy;

                #if defined(PIXELSNAP_ON)
                pos = UnityPixelSnap(pos);
                #endif

                output.positionCS = TransformObjectToHClip(pos.xyz);
                output.uv = input.uv + uvPadding;
                output.color = input.color * _Color * _RendererColor;

                return output;
            }

            float4 SampleSprite(float2 uv)
            {
                // Extended vertices may lie outside this sprite, but must never read its neighbours.
                if (any(uv < _SpriteUVRect.xy) || any(uv >= _SpriteUVRect.zw))
                {
                    return float4(0, 0, 0, 0);
                }

                float2 halfTexel = _MainTex_TexelSize.xy * 0.5;
                float2 sampleUV = clamp(uv, _SpriteUVRect.xy + halfTexel, _SpriteUVRect.zw - halfTexel);
                // LOD 0 also prevents atlas neighbours from leaking through coarser mip levels.
                return SAMPLE_TEXTURE2D_LOD(_MainTex, sampler_MainTex, sampleUV, 0);
            }

            float4 frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);

                float4 mainColor = SampleSprite(input.uv) * input.color;

                if (_OutlineSize <= 0.001)
                {
                    return mainColor;
                }

                if (mainColor.a > _AlphaThreshold)
                {
                    return mainColor;
                }

                // 8 方向偏移采样周围像素
                float2 offset = _MainTex_TexelSize.xy * _OutlineSize;
                float maxAlpha = 0.0;

                maxAlpha = max(maxAlpha, SampleSprite(input.uv + float2(offset.x, 0)).a);
                maxAlpha = max(maxAlpha, SampleSprite(input.uv - float2(offset.x, 0)).a);
                maxAlpha = max(maxAlpha, SampleSprite(input.uv + float2(0, offset.y)).a);
                maxAlpha = max(maxAlpha, SampleSprite(input.uv - float2(0, offset.y)).a);
                maxAlpha = max(maxAlpha, SampleSprite(input.uv + float2(offset.x, offset.y)).a);
                maxAlpha = max(maxAlpha, SampleSprite(input.uv + float2(-offset.x, offset.y)).a);
                maxAlpha = max(maxAlpha, SampleSprite(input.uv + float2(offset.x, -offset.y)).a);
                maxAlpha = max(maxAlpha, SampleSprite(input.uv - float2(offset.x, offset.y)).a);

                if (maxAlpha > _AlphaThreshold)
                {
                    return float4(_OutlineColor.rgb, _OutlineColor.a * maxAlpha);
                }

                return mainColor;
            }
            ENDHLSL
        }

        Pass
        {
            Name "UniversalForward"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma multi_compile _ PIXELSNAP_ON

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS   : POSITION;
                float4 color        : COLOR;
                float2 uv           : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS   : SV_POSITION;
                float4 color        : COLOR;
                float2 uv           : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);
            float4 _MainTex_TexelSize;

            CBUFFER_START(UnityPerMaterial)
                float4 _Color;
                float4 _RendererColor;
                float4 _Flip;
                float4 _OutlineColor;
                float4 _SpriteUVRect;
                float4 _SpriteSize;
                float _OutlineSize;
                float _AlphaThreshold;
            CBUFFER_END

            Varyings vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);

                float4 pos = input.positionOS;
                // Full Rect mesh: extend geometry and UV together to preserve the sprite scale.
                float2 uvSize = _SpriteUVRect.zw - _SpriteUVRect.xy;
                float2 direction = ((input.uv - _SpriteUVRect.xy) / uvSize) * 2.0 - 1.0;
                float2 uvPadding = direction * _MainTex_TexelSize.xy * _OutlineSize;
                pos.xy += uvPadding / uvSize * _SpriteSize.xy;
                pos.xy *= _Flip.xy;

                #if defined(PIXELSNAP_ON)
                pos = UnityPixelSnap(pos);
                #endif

                output.positionCS = TransformObjectToHClip(pos.xyz);
                output.uv = input.uv + uvPadding;
                output.color = input.color * _Color * _RendererColor;

                return output;
            }

            float4 SampleSprite(float2 uv)
            {
                // Extended vertices may lie outside this sprite, but must never read its neighbours.
                if (any(uv < _SpriteUVRect.xy) || any(uv >= _SpriteUVRect.zw))
                {
                    return float4(0, 0, 0, 0);
                }

                float2 halfTexel = _MainTex_TexelSize.xy * 0.5;
                float2 sampleUV = clamp(uv, _SpriteUVRect.xy + halfTexel, _SpriteUVRect.zw - halfTexel);
                // LOD 0 also prevents atlas neighbours from leaking through coarser mip levels.
                return SAMPLE_TEXTURE2D_LOD(_MainTex, sampler_MainTex, sampleUV, 0);
            }

            float4 frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);

                float4 mainColor = SampleSprite(input.uv) * input.color;

                if (_OutlineSize <= 0.001)
                {
                    return mainColor;
                }

                if (mainColor.a > _AlphaThreshold)
                {
                    return mainColor;
                }

                float2 offset = _MainTex_TexelSize.xy * _OutlineSize;
                float maxAlpha = 0.0;

                maxAlpha = max(maxAlpha, SampleSprite(input.uv + float2(offset.x, 0)).a);
                maxAlpha = max(maxAlpha, SampleSprite(input.uv - float2(offset.x, 0)).a);
                maxAlpha = max(maxAlpha, SampleSprite(input.uv + float2(0, offset.y)).a);
                maxAlpha = max(maxAlpha, SampleSprite(input.uv - float2(0, offset.y)).a);
                maxAlpha = max(maxAlpha, SampleSprite(input.uv + float2(offset.x, offset.y)).a);
                maxAlpha = max(maxAlpha, SampleSprite(input.uv + float2(-offset.x, offset.y)).a);
                maxAlpha = max(maxAlpha, SampleSprite(input.uv + float2(offset.x, -offset.y)).a);
                maxAlpha = max(maxAlpha, SampleSprite(input.uv - float2(offset.x, offset.y)).a);

                if (maxAlpha > _AlphaThreshold)
                {
                    return float4(_OutlineColor.rgb, _OutlineColor.a * maxAlpha);
                }

                return mainColor;
            }
            ENDHLSL
        }
    }
    Fallback "Sprites/Default"
}
