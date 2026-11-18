/// <summary>
/// Синтез жёсткого хруста (как ломается ветка) для мигающей лампы меню.
/// Один хруст — очередь из нескольких неровных изломов: шумовые удары с рваной огибающей,
/// окрашенные широким полосовым фильтром и слегка перегруженные для жёсткости. Без чистых тонов.
/// Чистый C# без Unity API — работает в звуковом потоке.
/// </summary>
public class LampCrunch
{
    const int MaxFragments = 7;
    readonly double[] start = new double[MaxFragments];
    readonly float[] amp = new float[MaxFragments];
    readonly float[] tau = new float[MaxFragments];
    readonly System.Random rng;
    int count;
    double time = 10.0, end;
    float volume, jag = 1f, low, band, fc = 2000f;
    int jagLeft;

    public LampCrunch(System.Random rng) { this.rng = rng; }

    float Rand() => (float)rng.NextDouble();

    /// <summary>Начать новый хруст. loud — длиннее и громче (лампа зажглась после долгой паузы).</summary>
    public void Trigger(float vol, bool loud)
    {
        volume = vol;
        count = loud ? rng.Next(4, 8) : rng.Next(2, 5);
        double ts = 0.0;
        float a = 1f;
        for (int i = 0; i < count; i++)
        {
            start[i] = ts;
            amp[i] = a * (0.7f + 0.3f * Rand());
            // Первый излом — самый плотный, остальные — короткие щепки.
            tau[i] = i == 0 ? 0.003f + 0.003f * Rand() : 0.0012f + 0.0025f * Rand();
            ts += 0.003 + Rand() * (loud ? 0.018 : 0.011);
            a *= 0.55f + 0.3f * Rand();
        }
        end = start[count - 1] + tau[count - 1] * 8.0;
        fc = 1400f + 1800f * Rand(); // окраска «древесины» у каждого хруста своя
        time = 0.0;
    }

    public float Sample(float white, float dt, int sampleRate)
    {
        if (count == 0 || time > end) return 0f;
        double t = time;
        time += dt;

        // Рваная, зернистая огибающая — то, что делает звук хрустом, а не щелчком.
        if (--jagLeft <= 0)
        {
            jagLeft = 8 + rng.Next(24);
            jag = 0.25f + 0.75f * Rand();
        }

        float env = 0f;
        for (int i = 0; i < count; i++)
        {
            double d = t - start[i];
            if (d < 0.0 || d > tau[i] * 8.0) continue;
            float attack = d < 0.00015 ? (float)(d / 0.00015) : 1f;
            env += amp[i] * (float)System.Math.Exp(-d / tau[i]) * attack;
        }
        if (env < 1e-5f) return 0f;

        float x = white * env * jag;
        // Широкий полосовой фильтр (state-variable): древесная окраска без звона.
        float f = 2f * (float)System.Math.Sin(System.Math.PI * fc / sampleRate);
        low += f * band;
        float high = x - low - 0.7f * band;
        band += f * high;
        float y = band * 1.6f + high * 0.5f + x * 0.3f;
        // Перегрузка — жёсткость; громкость применяется после, чтобы характер не зависел от неё.
        return (float)System.Math.Tanh(y * 2.5) * 0.6f * volume;
    }
}
