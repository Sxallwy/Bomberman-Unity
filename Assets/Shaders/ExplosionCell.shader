Shader "Bomberman/Explosion Cell"
{
    Properties
    {
        [HDR] _CoreColor ("Hot Core", Color) = (2.0, 1.5, 0.6, 1)
        [HDR] _FlameColor ("Flame", Color) = (1.5, 0.36, 0.025, 1)
        _EdgeColor ("Flame Edge", Color) = (0.7, 0.045, 0.008, 1)
        [HideInInspector] _Progress ("Progress", Float) = 0
        [HideInInspector] _Seed ("Seed", Float) = 0
        [HideInInspector] _Layer ("Layer", Float) = 0
    }
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent+10" "RenderType" = "Transparent" }
        Pass
        {
            Name "Explosion"
            Tags { "LightMode" = "SRPDefaultUnlit" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 3.0
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _CoreColor;
                float4 _FlameColor;
                float4 _EdgeColor;
                float _Progress;
                float _Seed;
                float _Layer;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                float2 data : TEXCOORD1;
            };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            float Hash(float2 p)
            {
                return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453);
            }
            float Noise(float2 p)
            {
                float2 cell = floor(p);
                float2 f = frac(p);
                f = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(Hash(cell), Hash(cell + float2(1, 0)), f.x),
                    lerp(Hash(cell + float2(0, 1)), Hash(cell + 1.0), f.x), f.y);
            }
            float RoundBox(float2 p, float2 halfSize, float radius)
            {
                float2 q = abs(p) - halfSize + radius;
                return min(max(q.x, q.y), 0.0) + length(max(q, 0.0)) - radius;
            }

            Varyings Vert(Attributes input)
            {
                Varyings output;
                float3 position = input.positionOS.xyz;
                if (_Layer > 0.5 && _Layer < 1.5)
                {
                    float t = saturate(_Progress * 1.2);
                    float2 direction = float2(cos(input.data.x), sin(input.data.x));
                    float2 across = float2(-direction.y, direction.x);
                    float radius = lerp(0.035, 0.39 * input.data.y, pow(t, 0.6));
                    position.xz = direction * (radius + position.z * lerp(0.15, 0.045, t))
                        + across * position.x * 0.035;
                    position.y += sin(t * PI) * 0.23;
                }
                output.positionCS = TransformObjectToHClip(position);
                output.uv = input.uv;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float2 p = input.uv - 0.5;
                float t = saturate(_Progress);

                if (_Layer > 0.5 && _Layer < 1.5)
                {
                    float shape = 1.0 - smoothstep(0.25, 0.5, length(p * float2(1.3, 0.65)));
                    float alpha = shape * (1.0 - smoothstep(0.45, 0.95, t));
                    return half4(lerp(_CoreColor.rgb * 1.4, _FlameColor.rgb, t), alpha);
                }

                float radius = length(p);
                if (_Layer > 1.5 && _Layer < 2.5)
                {
                    float phase = saturate(t * 3.0);
                    float ring = abs(radius - lerp(0.06, 0.46, sqrt(phase)));
                    float alpha = (1.0 - smoothstep(0.008, 0.028, ring)) * (1.0 - phase);
                    return half4(_CoreColor.rgb, alpha * 0.9);
                }

                float n = Noise(p * 11.0 + float2(_Seed, -t * 5.0));
                n = n * 0.7 + Noise(p * 22.0 - float2(t * 3.0, _Seed)) * 0.3;
                float cellClip = 1.0 - smoothstep(0.47, 0.495, max(abs(p.x), abs(p.y)));

                if (_Layer > 2.5)
                {
                    float smoke = 1.0 - smoothstep(0.30, 0.46, radius + (n - 0.5) * 0.12);
                    float alpha = smoke * smoothstep(0.18, 0.6, t) * (1.0 - t) * 0.45;
                    return half4(lerp(float3(0.13, 0.10, 0.08), float3(0.28, 0.23, 0.18), n), alpha * cellClip);
                }

                float growth = lerp(0.25, 1.0, smoothstep(0.0, 0.10, t));
                float distance = RoundBox(p / growth, float2(0.455, 0.455), 0.13);
                distance += (n - 0.5) * 0.045;
                float flame = 1.0 - smoothstep(-0.018, 0.022, distance);
                float heat = saturate(1.0 - radius * 1.35 + (n - 0.5) * 0.55);
                float flicker = 0.92 + 0.08 * sin(t * 35.0 + _Seed + n * 5.0);
                float3 color = lerp(_EdgeColor.rgb, _FlameColor.rgb, smoothstep(0.15, 0.65, heat));
                color = lerp(color, _CoreColor.rgb, smoothstep(0.60, 0.95, heat));
                float flash = (1.0 - smoothstep(0.0, 0.18, t)) * 0.65;
                color = lerp(color, _CoreColor.rgb * 1.5, flash) * flicker;
                float fade = lerp(0.96, 0.35, smoothstep(0.55, 1.0, t));
                return half4(color, flame * cellClip * fade);
            }
            ENDHLSL
        }
    }
}
