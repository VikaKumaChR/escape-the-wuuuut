Shader "DepthLab/Holographic Wall"
{
    Properties
    {
        _BaseColor ("Deep Navy Base", Color) = (0.03137255, 0.07450980, 0.16078431, 1)
        _CyanColor ("Electric Cyan", Color) = (0, 0.89803922, 1, 1)
        _BlueColor ("Neon Blue", Color) = (0.08627451, 0.46666667, 1, 1)
        _AccentColor ("Restrained Purple", Color) = (0.65882353, 0.33333333, 0.96862745, 1)
        _Intensity ("Pattern Intensity", Range(0, 2)) = 0.85
        _FlowSpeed ("Flow Speed", Range(0, 2)) = 0.65
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "RenderType" = "Opaque"
            "Queue" = "Geometry"
        }

        Pass
        {
            Name "HolographicWall"
            Tags { "LightMode" = "SRPDefaultUnlit" }
            Cull Back
            ZWrite On
            ZTest LEqual
            Blend Off

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                half4 _CyanColor;
                half4 _BlueColor;
                half4 _AccentColor;
                float _Intensity;
                float _FlowSpeed;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 wallUV : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings Vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                float3 world = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = TransformWorldToHClip(world);
                output.wallUV = float2(world.x + world.z, world.y);
                return output;
            }

            float Hash21(float2 p)
            {
                float3 q = frac(float3(p.x, p.y, p.x) * 0.1031);
                q += dot(q, q.yzx + 33.33);
                return frac((q.x + q.y) * q.z);
            }

            // Widths and derivatives are in metres, measured before any cell wrapping.
            float Stroke(float distanceToLine, float halfWidth, float pixelWidth)
            {
                float aa = max(pixelWidth, 0.0001);
                return (1.0 - smoothstep(halfWidth - aa, halfWidth + aa, distanceToLine))
                    * saturate(halfWidth * 2.0 / aa);
            }

            float Box(float2 p, float2 halfSize, float aa)
            {
                float2 d = abs(p) - halfSize;
                return 1.0 - smoothstep(-aa, aa, max(d.x, d.y));
            }

            float RepeatDistance(float value, float period)
            {
                return abs(frac(value / period + 0.5) - 0.5) * period;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                float2 uv = input.wallUV;
                float2 footprint = max(fwidth(uv), float2(0.0001, 0.0001));
                float aa = max(footprint.x, footprint.y);
                float time = _Time.y * _FlowSpeed;
                float fineFade = 1.0 - smoothstep(0.012, 0.055, aa);
                float mediumFade = 1.0 - smoothstep(0.045, 0.18, aa);

                // Large architectural panels remain readable after fine detail fades out.
                float2 panelDistance = float2(RepeatDistance(uv.x, 1.2),
                                              RepeatDistance(uv.y, 2.4));
                float panelGrid = max(Stroke(panelDistance.x, 0.008, footprint.x),
                                      Stroke(panelDistance.y, 0.008, footprint.y));
                float panelHalo = max(1.0 - smoothstep(0.0, 0.07 + aa, panelDistance.x),
                                      1.0 - smoothstep(0.0, 0.07 + aa, panelDistance.y));
                float2 panelID = floor(uv / float2(1.2, 2.4));
                float panelTone = Hash21(panelID);
                float panelFade = 1.0 - smoothstep(0.15, 0.6, aa);

                float2 traceCell = frac(uv / float2(0.6, 0.8)) * float2(0.6, 0.8);
                float traceVertical = Stroke(abs(traceCell.x - 0.08), 0.0025, footprint.x)
                    * Box(float2(0, traceCell.y - 0.36), float2(1, 0.23), aa);
                float traceHorizontal = Stroke(abs(traceCell.y - 0.59), 0.0025, footprint.y)
                    * Box(float2(traceCell.x - 0.16, 0), float2(0.08, 1), aa);
                float traceTerminal = Stroke(length(traceCell - float2(0.24, 0.59)),
                                             0.009, aa);
                float traces = saturate(traceVertical + traceHorizontal + traceTerminal)
                    * fineFade;

                float laneID = floor(uv.x / 0.48);
                float laneSeed = Hash21(float2(laneID, 4.7));
                float laneX = (frac(uv.x / 0.48) - 0.5) * 0.48;
                float column = 1.0 - smoothstep(0.025, 0.17 + footprint.x, abs(laneX));
                float streamY = uv.y + time * (0.22 + laneSeed * 0.18) + laneSeed * 13.0;
                float streamPhase = frac(streamY / 3.6);
                float streamEnvelope = smoothstep(0.03, 0.22, streamPhase)
                    * (1.0 - smoothstep(0.60, 0.98, streamPhase));
                float columns = column * (0.22 + 0.78 * streamEnvelope) * mediumFade;

                // Independent segment selections form abstract symbols, not a font atlas.
                float rowID = floor(streamY / 0.22);
                float2 glyph = float2(laneX, (frac(streamY / 0.22) - 0.5) * 0.22);
                float symbol = Hash21(float2(laneID, rowID));
                float bits = Hash21(float2(laneID + 71.0, rowID + 19.0));
                float horizontal = Stroke(abs(glyph.y - 0.062), 0.004, footprint.y)
                    * step(0.36, frac(symbol * 13.0));
                horizontal += Stroke(abs(glyph.y), 0.004, footprint.y)
                    * step(0.42, frac(symbol * 29.0));
                horizontal += Stroke(abs(glyph.y + 0.062), 0.004, footprint.y)
                    * step(0.40, frac(symbol * 47.0));
                horizontal *= Box(float2(glyph.x, 0), float2(0.037, 1), aa);
                float vertical = Stroke(abs(glyph.x - 0.039), 0.0035, footprint.x)
                    * step(0.48, frac(bits * 17.0));
                vertical += Stroke(abs(glyph.x + 0.039), 0.0035, footprint.x)
                    * step(0.48, frac(bits * 31.0));
                vertical *= Box(float2(0, glyph.y), float2(1, 0.055), aa);
                float glyphs = saturate(horizontal + vertical) * step(0.25, symbol)
                    * (0.18 + 0.82 * streamEnvelope) * fineFade;

                // Wrapping occurs in empty margins, so motion never pops at cell boundaries.
                float2 dataUV = float2(uv.x, uv.y + time * 0.31);
                float2 dataID = floor(dataUV / float2(0.36, 0.54));
                float dataSeed = Hash21(dataID + 9.3);
                float2 dataPoint = (frac(dataUV / float2(0.36, 0.54)) - 0.5)
                    * float2(0.36, 0.54);
                dataPoint.x -= (dataSeed - 0.5) * 0.22;
                float particles = Box(dataPoint, float2(0.006, 0.017), aa)
                    * saturate(0.012 / aa) * step(0.86, dataSeed) * fineFade;

                float2 motifUV = float2(uv.x, uv.y + time * 0.055);
                float2 motifID = floor(motifUV / float2(1.6, 1.2));
                float motifSeed = Hash21(motifID + 37.0);
                float2 motif = (frac(motifUV / float2(1.6, 1.2)) - 0.5)
                    * float2(1.6, 1.2);
                motif.x -= (motifSeed - 0.5) * 0.8;
                float2 m = abs(motif);
                float diamondDistance = (m.x + m.y - 0.075) * 0.70710678;
                float hexDistance = max(dot(m, float2(0.8660254, 0.5)), m.y) - 0.065;
                float motifDistance = lerp(diamondDistance, hexDistance, step(0.78, motifSeed));
                float motifs = Stroke(abs(motifDistance), 0.0035, aa)
                    * step(0.58, motifSeed) * mediumFade;
                float purpleMotifs = motifs * step(0.88, motifSeed);

                // A broad, low-amplitude travelling band; no global brightness oscillation.
                float scanDistance = RepeatDistance(uv.y + time * 0.12, 4.8);
                float scan = 1.0 - smoothstep(0.035, 0.32 + footprint.y, scanDistance);
                float scanEdge = Stroke(abs(scanDistance - 0.055), 0.004, footprint.y)
                    * fineFade;

                half3 color = _BaseColor.rgb * (1.12 + 0.15 * panelTone * panelFade);
                // Persistent blue fill prevents unlit regions from becoming empty black.
                color += _BlueColor.rgb * 0.012;
                half3 blue = _BlueColor.rgb * (0.10 * panelGrid + 0.025 * panelHalo
                    + 0.055 * traces + 0.018 * scan);
                half3 cyan = _CyanColor.rgb * (0.055 * columns + 0.48 * glyphs
                    + 0.50 * particles + 0.23 * (motifs - purpleMotifs)
                    + 0.025 * scan + 0.045 * scanEdge);
                half3 accent = _AccentColor.rgb * (0.18 * purpleMotifs);
                color += (blue + cyan + accent) * _Intensity;
                return half4(color, 1.0);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
