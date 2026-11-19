// Полноэкранные фильтры (настройки → «Графика» → «Фильтр»). Один проход на фильтр;
// номер прохода = (int)ScreenFilterKind - 1. Накладываются после постобработки URP (см. ScreenFilters.cs).
Shader "Hidden/ScreenFilters"
{
    HLSLINCLUDE
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
    #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

    float _Intensity;   // 0..1 — сила фильтра
    float _FilterTime;  // время без учёта паузы/timeScale

    float Hash12(float2 p)
    {
        float3 p3 = frac(float3(p.xyx) * 0.1031);
        p3 += dot(p3, p3.yzx + 33.33);
        return frac((p3.x + p3.y) * p3.z);
    }

    half3 Src(float2 uv) { return SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv).rgb; }
    half3 SrcPoint(float2 uv) { return SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_PointClamp, uv).rgb; }
    float Luma(float3 c) { return dot(c, float3(0.299, 0.587, 0.114)); }

    float Vignette(float2 uv, float power)
    {
        float2 d = uv * (1.0 - uv.yx);
        return saturate(pow(abs(d.x * d.y * 16.0), power));
    }

    // ---------- VHS-кассета ----------
    half4 FragVHS(Varyings i) : SV_Target
    {
        float I = _Intensity, t = _FilterTime;
        float2 uv = i.texcoord;
        float2 px = _ScreenParams.xy;

        // Строки «развёртки» (≈ 240 на кадр) дрожат по горизонтали.
        float lineId = floor(uv.y * 240.0);
        float frame = floor(t * 30.0);
        float shake = (Hash12(float2(lineId, frame)) - 0.5) * 0.0018;

        // Медленно ползущая полоса сбоя трекинга.
        float bandY = 1.0 - frac(t * 0.06);
        float band = 1.0 - smoothstep(0.0, 0.035, abs(uv.y - bandY));
        shake += band * (Hash12(float2(lineId, t * 13.0)) - 0.5) * 0.03;

        // Срыв изображения внизу кадра (переключение видеоголовок).
        float bottom = 1.0 - smoothstep(0.0, 0.022, uv.y);
        shake += bottom * (0.015 + 0.01 * Hash12(float2(lineId, frame)));

        // Редкий рывок всего кадра.
        float glitch = step(0.985, Hash12(float2(floor(t * 8.0), 7.0)));
        shake += glitch * 0.004 * sin(uv.y * 60.0 + t * 40.0);

        uv.x += shake * I;

        // Яркость — чётко, цвет — размазан по горизонтали и сдвинут (YIQ).
        float3 sharp = Src(uv);
        float3 smear = 0;
        float off = 0.0045 * I;
        [unroll] for (int k = 0; k < 6; k++)
            smear += Src(uv + float2(off * (k - 1.5) + 0.003 * I, 0));
        smear /= 6.0;
        float3x3 toYIQ = float3x3(0.299, 0.587, 0.114, 0.596, -0.274, -0.322, 0.211, -0.523, 0.312);
        float3x3 toRGB = float3x3(1.0, 0.956, 0.621, 1.0, -0.272, -0.647, 1.0, -1.106, 1.703);
        float3 yiq = float3(mul(toYIQ, sharp).x, mul(toYIQ, smear).yz * 0.8);
        float3 c = mul(toRGB, yiq);

        // Плёночная гамма: приподнятые тени, чуть тёплый оттенок, «мыло» в светах.
        c = c * 0.92 + float3(0.035, 0.025, 0.04);
        c *= float3(1.03, 0.99, 1.04);

        // Строчная структура, шум, белые штрихи в полосе сбоя.
        c *= 0.9 + 0.1 * sin(i.positionCS.y * PI * 0.5);
        c += (Hash12(i.positionCS.xy + frac(t) * 517.0) - 0.5) * 0.09;
        float streak = step(0.93, Hash12(float2(floor(uv.x * 90.0 + lineId * 7.0), lineId + frame)));
        c += (band * 0.8 + bottom * 0.5) * streak * 0.6;
        c *= lerp(1.0, Vignette(i.texcoord, 0.3), 0.8);

        return half4(lerp(Src(i.texcoord), c, I), 1);
    }

    // ---------- ЭЛТ-монитор ----------
    half4 FragCRT(Varyings i) : SV_Target
    {
        float I = _Intensity;
        float2 cc = i.texcoord * 2.0 - 1.0;
        float k = 0.1 * I;
        cc *= 1.0 + k * (cc.yx * cc.yx);
        float2 uv = cc * 0.5 + 0.5;
        if (any(abs(cc) > 1.0)) return half4(0, 0, 0, 1);

        float conv = 0.0009 * I; // несведение лучей
        float3 c = float3(Src(uv + float2(conv, 0)).r, Src(uv).g, Src(uv - float2(conv, 0)).b);

        // Строки: период ~3 пикселя, тёмные промежутки сильнее на тёмном.
        float s = sin(uv.y * _ScreenParams.y * PI / 1.5);
        float scan = lerp(1.0, 0.65 + 0.35 * s * s, I);
        // Апертурная решётка R-G-B по столбцам пикселей.
        int m = (int)fmod(i.positionCS.x, 3.0);
        float3 mask = float3(m == 0, m == 1, m == 2) * 0.35 + 0.65;
        c *= scan * lerp(1.0, mask, I);
        c *= 1.0 + 0.3 * I; // компенсация потери яркости
        c += (Hash12(i.positionCS.xy + frac(_FilterTime) * 311.0) - 0.5) * 0.02 * I;

        // Затемнение к краям и мягкие скруглённые углы.
        float2 e = smoothstep(1.0, 0.96, abs(cc));
        c *= e.x * e.y * lerp(1.0, Vignette(uv, 0.25), I);
        return half4(c, 1);
    }

    // ---------- Нуар (ч/б, красный сохраняется) ----------
    half4 FragNoir(Varyings i) : SV_Target
    {
        float I = _Intensity;
        float3 src = Src(i.texcoord);
        float l = Luma(src);
        l = smoothstep(0.02, 0.85, l); // жёсткий контраст
        float red = saturate((src.r - max(src.g, src.b)) * 4.0 - 0.15);
        float3 c = lerp(l.xxx, float3(src.r * 1.2, src.g * 0.6, src.b * 0.6), red);
        c += (Hash12(i.positionCS.xy + frac(_FilterTime) * 173.0) - 0.5) * 0.06;
        c *= Vignette(i.texcoord, 0.4);
        return half4(lerp(src, c, I), 1);
    }

    // ---------- Ретро-консоль (низкое разрешение, 15-бит цвет, дизеринг) ----------
    float Bayer4(int2 p)
    {
        static const float m[16] = { 0, 8, 2, 10, 12, 4, 14, 6, 3, 11, 1, 9, 15, 7, 13, 5 };
        return m[(p.y & 3) * 4 + (p.x & 3)] / 16.0 - 0.5;
    }

    half4 FragRetro(Varyings i) : SV_Target
    {
        float I = _Intensity;
        float lines = lerp(540.0, 200.0, I);                         // вертикальное разрешение «консоли»
        float2 res = _ScreenParams.xy / max(1.0, floor(_ScreenParams.y / lines));
        float2 cell = floor(i.texcoord * res);
        float3 c = SrcPoint((cell + 0.5) / res);
        float levels = 31.0;                                          // 5 бит на канал
        c = floor(saturate(c) * levels + 0.5 + Bayer4(int2(cell)) * I) / levels;
        return half4(c, 1);
    }
    ENDHLSL

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }
        ZWrite Off ZTest Always Blend Off Cull Off

        Pass
        {
            Name "VHS"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragVHS
            ENDHLSL
        }
        Pass
        {
            Name "CRT"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragCRT
            ENDHLSL
        }
        Pass
        {
            Name "Noir"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragNoir
            ENDHLSL
        }
        Pass
        {
            Name "Retro"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragRetro
            ENDHLSL
        }
    }
}
