using UnityEngine;

/// <summary>
/// Танцевальный бит для пасхалки, синтезируется в коде (без аудиофайлов): «бочка» на каждую долю,
/// хлопок на 2 и 4, хэты, пружинящий бас на слабые восьмые и короткие аккорды-«стабы». 4 такта, бесшовный цикл.
/// </summary>
public static class DanceBeat
{
    const int Rate = 44100;

    public static AudioClip Create(float bpm, int beats)
    {
        float beatLen = 60f / bpm;
        int n = Mathf.RoundToInt(beats * beatLen * Rate);
        var data = new float[n];
        var rng = new System.Random(1337);
        float Noise() => (float)rng.NextDouble() * 2f - 1f;

        // Гармония по тактам (4 доли): Am – Am – F – G.
        float[] bassRoot = { 55f, 55f, 43.65f, 49f };
        float[][] chords =
        {
            new[] { 220f, 261.63f, 329.63f }, new[] { 220f, 261.63f, 329.63f },
            new[] { 174.61f, 220f, 261.63f }, new[] { 196f, 246.94f, 293.66f },
        };
        // Бас по восьмым: 0 — пауза (доля занята «бочкой»), иначе множитель частоты корня.
        float[] bassPattern = { 0f, 1f, 0f, 1f, 0f, 2f, 0f, 1.5f };

        int step16 = Mathf.RoundToInt(beatLen / 4f * Rate);
        int steps = beats * 4;
        for (int s = 0; s < steps; s++)
        {
            int start = s * step16;
            int bar = (s / 16) % 4;
            bool onBeat = s % 4 == 0, offBeat = s % 4 == 2;
            int beatInBar = (s / 4) % 4;

            if (onBeat) Add(data, start, 0.45f, t => Mathf.Sin(2f * Mathf.PI * (45f * t + 90f / 32f * (1f - Mathf.Exp(-32f * t)))) * Mathf.Exp(-8f * t) * 0.95f);
            if (onBeat && (beatInBar == 1 || beatInBar == 3))
            {
                // Хлопок: три быстрых всплеска шума.
                float prev = 0f;
                Add(data, start, 0.25f, t =>
                {
                    float env = 0f;
                    for (int k = 0; k < 3; k++) { float tt = t - k * 0.011f; if (tt >= 0f) env += Mathf.Exp(-(k < 2 ? 160f : 22f) * tt); }
                    float x = Noise(); float hp = x - prev; prev = x;
                    return hp * env * 0.35f;
                });
            }
            // Хэты: открытый на слабую восьмую, закрытые — на шестнадцатые.
            {
                float prev = 0f, amp = offBeat ? 0.22f : (s % 2 == 1 ? 0.07f : 0.1f), decay = offBeat ? 28f : 70f;
                Add(data, start, offBeat ? 0.18f : 0.06f, t => { float x = Noise(); float hp = x - prev; prev = x; return hp * Mathf.Exp(-decay * t) * amp; });
            }
            if (s % 2 == 0)
            {
                float mul = bassPattern[(s / 2) % 8];
                if (mul > 0f)
                {
                    float f = bassRoot[bar] * mul, lp = 0f;
                    Add(data, start, beatLen * 0.45f, t =>
                    {
                        float saw = 2f * (f * t - Mathf.Floor(f * t + 0.5f));
                        lp += (saw - lp) * Mathf.Lerp(0.25f, 0.05f, Mathf.Clamp01(t * 12f));
                        return lp * Mathf.Min(1f, t * 400f) * Mathf.Exp(-5f * t) * 0.5f;
                    });
                }
            }
            // Стабы на «и» второй и четвёртой доли + синкопа в конце такта.
            int inBar = s % 16;
            if (inBar == 6 || inBar == 14 || inBar == 11)
            {
                var ch = chords[bar];
                Add(data, start, 0.16f, t =>
                {
                    float v = 0f;
                    foreach (var f in ch) v += Mathf.Sign(Mathf.Sin(2f * Mathf.PI * f * 2f * t)) * 0.5f + Mathf.Sin(2f * Mathf.PI * f * t);
                    return v * Mathf.Exp(-14f * t) * 0.045f;
                });
            }
        }

        // Мягкое ограничение и нормализация.
        float peak = 0f;
        for (int i = 0; i < n; i++) { data[i] = (float)System.Math.Tanh(data[i] * 1.3f); peak = Mathf.Max(peak, Mathf.Abs(data[i])); }
        if (peak > 0f) for (int i = 0; i < n; i++) data[i] *= 0.9f / peak;

        var clip = AudioClip.Create("DanceBeat", n, 1, Rate, false);
        clip.SetData(data, 0);
        return clip;
    }

    /// <summary>Добавляет звук длительностью len секунд с позиции start; хвост заворачивается в начало (бесшовный цикл).</summary>
    static void Add(float[] data, int start, float len, System.Func<float, float> f)
    {
        int count = Mathf.RoundToInt(len * Rate);
        for (int i = 0; i < count; i++)
            data[(start + i) % data.Length] += f(i / (float)Rate);
    }

    /// <summary>Короткий щелчок кнопки.</summary>
    public static AudioClip Click()
    {
        int n = Rate / 20;
        var data = new float[n];
        var rng = new System.Random(7);
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)Rate;
            data[i] = ((float)rng.NextDouble() * 2f - 1f) * Mathf.Exp(-400f * t) * 0.6f + Mathf.Sin(2f * Mathf.PI * 1800f * t) * Mathf.Exp(-120f * t) * 0.3f;
        }
        var clip = AudioClip.Create("Click", n, 1, Rate, false);
        clip.SetData(data, 0);
        return clip;
    }
}
