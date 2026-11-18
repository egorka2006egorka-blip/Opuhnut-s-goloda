using System;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Синтезирует фоновую музыку главного меню и сохраняет её в Assets/Audio/MenuMusic.wav (бесшовная петля 96 с).
/// Медленный тягучий пэд в ля миноре (Am(add9) – Fmaj7 – Dm9 – Esus4 / Am – Fmaj7#11 – D–F–G# – E),
/// низкий гул, редкие одинокие ноты «музыкальной шкатулки» с эхом, большой пустой реверб,
/// тихий шум помещения, далёкий глухой удар и диссонирующий высокий звук.
/// Файл можно заменить своим треком с тем же именем. Перегенерировать: Tools → Main Menu → Regenerate Music.
/// </summary>
public static class MenuMusicGenerator
{
    public const string ClipPath = "Assets/Audio/MenuMusic.wav";

    const int SR = 44100;
    const float ChordLen = 12f;
    const int ChordCount = 8;
    const float Length = ChordLen * ChordCount; // 96 с

    // Первый элемент — бас (MIDI), остальные — голоса пэда.
    static readonly int[][] Chords =
    {
        new[] { 45, 57, 60, 64, 71 }, // Am(add9)
        new[] { 41, 53, 57, 60, 64 }, // Fmaj7
        new[] { 38, 50, 53, 57, 64 }, // Dm9
        new[] { 40, 52, 57, 59, 62 }, // Esus4(7)
        new[] { 45, 57, 60, 64, 67 }, // Am7
        new[] { 41, 53, 57, 59, 64 }, // Fmaj7#11
        new[] { 38, 50, 53, 56, 62 }, // D–F–G# — тритон, тревога
        new[] { 40, 52, 56, 59, 64 }, // E
    };

    [MenuItem("Tools/Main Menu/Regenerate Music")]
    public static void Regenerate()
    {
        Generate();
        Debug.Log("MenuMusicGenerator: музыка меню сгенерирована: " + ClipPath);
    }

    /// <summary>Клип музыки меню; если файла нет — генерирует его.</summary>
    public static AudioClip EnsureClip()
    {
        if (!File.Exists(ClipPath)) Generate();
        return AssetDatabase.LoadAssetAtPath<AudioClip>(ClipPath);
    }

    static double Freq(int midi) => 440.0 * Math.Pow(2.0, (midi - 69) / 12.0);
    static int Wrap(long i, int n) => (int)(((i % n) + n) % n);
    static double Smooth(double x) { x = Math.Max(0, Math.Min(1, x)); double s = Math.Sin(x * Math.PI / 2); return s * s; }

    /// <summary>Медленное «плавание» высоты, как у старой плёнки (период кратен длине петли).</summary>
    static double Wow(double t) =>
        1.0 + 0.0025 * Math.Sin(2 * Math.PI * 6 / Length * t) + 0.0010 * Math.Sin(2 * Math.PI * 37 / Length * t);

    static void Pan(float pan, out float l, out float r)
    {
        l = (float)Math.Sqrt(0.5 * (1 - pan));
        r = (float)Math.Sqrt(0.5 * (1 + pan));
    }

    public static void Generate()
    {
        int n = (int)(SR * Length);
        var padL = new float[n]; var padR = new float[n];
        var melL = new float[n]; var melR = new float[n];
        var fxL = new float[n]; var fxR = new float[n];
        var white = new float[n];
        var rng = new System.Random(20261107);

        try
        {
            EditorUtility.DisplayProgressBar("Музыка меню", "Пэд и бас…", 0f);

            // ---------- Пэд и бас ----------
            const double attack = 5, release = 7, dur = ChordLen + 6;
            for (int c = 0; c < ChordCount; c++)
            {
                EditorUtility.DisplayProgressBar("Музыка меню", "Аккорд " + (c + 1) + " из " + ChordCount, 0.05f + 0.45f * c / ChordCount);
                double start = c * ChordLen - 3;
                var chord = Chords[c];
                int voices = chord.Length;
                double[] detune = { -7, 0, 6 };
                float[] pans = { -0.6f, 0f, 0.6f };
                var phase = new double[voices, 3];
                var breathK = new int[voices];
                var breathPh = new double[voices];
                for (int v = 0; v < voices; v++) { breathK[v] = rng.Next(3, 10); breathPh[v] = rng.NextDouble() * Math.PI * 2; }
                var panL = new float[3]; var panR = new float[3];
                for (int o = 0; o < 3; o++) Pan(pans[o], out panL[o], out panR[o]);

                int len = (int)(dur * SR);
                for (int j = 0; j < len; j++)
                {
                    double tl = (double)j / SR, t = start + tl;
                    double env = tl < attack ? Smooth(tl / attack) : tl > dur - release ? Smooth((dur - tl) / release) : 1;
                    if (env <= 0) continue;
                    double wow = Wow(t);
                    float l = 0, r = 0;

                    // Бас: низкий гул с медленной пульсацией.
                    {
                        double f = Freq(chord[0]) * wow;
                        phase[0, 0] += 2 * Math.PI * f / SR;
                        double p = phase[0, 0];
                        double trem = 0.8 + 0.2 * Math.Sin(2 * Math.PI * 5 / Length * t);
                        float s = (float)((Math.Sin(p) + 0.22 * Math.Sin(2 * p)) * 0.13 * env * trem);
                        l += s; r += s;
                    }
                    // Голоса пэда: по три слегка расстроенных осциллятора, разведённых по стерео.
                    for (int v = 1; v < voices; v++)
                    {
                        double f0 = Freq(chord[v]) * wow;
                        double breath = 1 + 0.3 * Math.Sin(2 * Math.PI * breathK[v] / Length * t + breathPh[v]);
                        double amp = 0.05 * env * breath;
                        for (int o = 0; o < 3; o++)
                        {
                            phase[v, o] += 2 * Math.PI * f0 * Math.Pow(2, detune[o] / 1200.0) / SR;
                            double p = phase[v, o];
                            float s = (float)((Math.Sin(p) + 0.35 * Math.Sin(2 * p) + 0.12 * Math.Sin(3 * p)) * amp);
                            l += s * panL[o]; r += s * panR[o];
                        }
                    }
                    int idx = Wrap((long)Math.Round(t * SR), n);
                    padL[idx] += l; padR[idx] += r;
                }
            }

            // ---------- Одинокие ноты «шкатулки» ----------
            EditorUtility.DisplayProgressBar("Музыка меню", "Мелодия…", 0.55f);
            double tm = 3;
            while (tm < Length - 1)
            {
                AddBell(melL, melR, n, tm, PickNote(rng, tm), 0.5f + 0.5f * (float)rng.NextDouble(), (float)(rng.NextDouble() - 0.5));
                // Иногда — вторая нота, короткая фраза из двух звуков.
                if (rng.NextDouble() < 0.35)
                    AddBell(melL, melR, n, tm + 0.35 + rng.NextDouble() * 0.3, PickNote(rng, tm), 0.35f, (float)(rng.NextDouble() - 0.5));
                tm += 2.5 + rng.NextDouble() * 4.5;
            }

            // ---------- Далёкий глухой удар и диссонирующий звук ----------
            AddBoom(fxL, fxR, n, 46.0, rng);
            AddSwell(padL, padR, n, 58.0, 12.0);
            for (int i = 0; i < n; i++) white[i] = (float)(rng.NextDouble() * 2 - 1);

            // ---------- Сведение: фильтр, эхо, реверб; петля прогоняется дважды для бесшовности ----------
            EditorUtility.DisplayProgressBar("Музыка меню", "Реверберация…", 0.7f);
            var outL = new float[n]; var outR = new float[n];
            var revL = new Freeverb(0); var revR = new Freeverb(23);
            var echoL = new float[(int)(0.9 * SR)]; var echoR = new float[(int)(1.3 * SR)];
            int eiL = 0, eiR = 0;
            float lpL = 0, lpR = 0, eLpL = 0, eLpR = 0, brown = 0, brownLp = 0;
            for (long i = 0; i < 2L * n; i++)
            {
                int k = (int)(i % n);
                double t = (double)k / SR;
                // «Дышащий» фильтр на пэде: 700…1800 Гц.
                double cutoff = 1250 + 550 * Math.Sin(2 * Math.PI * 4 / Length * t);
                float a = (float)(1 - Math.Exp(-2 * Math.PI * cutoff / SR));
                lpL += a * (padL[k] - lpL);
                lpR += a * (padR[k] - lpR);

                // Пинг-понг эхо для мелодии, с затуханием верхов.
                float dL = echoL[eiL], dR = echoR[eiR];
                eLpL += 0.35f * (dL - eLpL); eLpR += 0.35f * (dR - eLpR);
                echoL[eiL] = melL[k] + eLpR * 0.42f;
                echoR[eiR] = melR[k] + eLpL * 0.42f;
                eiL = (eiL + 1) % echoL.Length; eiR = (eiR + 1) % echoR.Length;
                float mL = melL[k] + dL * 0.6f, mR = melR[k] + dR * 0.6f;

                // Шум пустого помещения: «коричневый» шум без постоянной составляющей, медленно плывёт.
                brown = brown * 0.998f + white[k] * 0.02f;
                brownLp += 0.0015f * (brown - brownLp);
                float room = (brown - brownLp) * (float)(0.6 + 0.4 * Math.Sin(2 * Math.PI * 3 / Length * t)) * 0.05f;

                float sendL = lpL * 0.6f + mL * 0.8f + fxL[k];
                float sendR = lpR * 0.6f + mR * 0.8f + fxR[k];
                float wetL = revL.Process(sendL), wetR = revR.Process(sendR);

                if (i >= n)
                {
                    outL[k] = lpL * 0.55f + mL * 0.6f + fxL[k] * 0.5f + wetL * 0.6f + room;
                    outR[k] = lpR * 0.55f + mR * 0.6f + fxR[k] * 0.5f + wetR * 0.6f + room;
                }
            }

            // Нормализация по пику.
            float peak = 1e-6f;
            for (int i = 0; i < n; i++) peak = Math.Max(peak, Math.Max(Math.Abs(outL[i]), Math.Abs(outR[i])));
            float g = 0.89f / peak;
            for (int i = 0; i < n; i++) { outL[i] *= g; outR[i] *= g; }

            EditorUtility.DisplayProgressBar("Музыка меню", "Запись файла…", 0.95f);
            Directory.CreateDirectory(Path.GetDirectoryName(ClipPath));
            WriteWav(ClipPath, outL, outR);
            AssetDatabase.ImportAsset(ClipPath, ImportAssetOptions.ForceUpdate);
            var importer = AssetImporter.GetAtPath(ClipPath) as AudioImporter;
            if (importer != null)
            {
                var s = importer.defaultSampleSettings;
                s.loadType = AudioClipLoadType.CompressedInMemory;
                s.compressionFormat = AudioCompressionFormat.Vorbis;
                s.quality = 0.6f;
                importer.defaultSampleSettings = s;
                importer.loadInBackground = true;
                importer.SaveAndReimport();
            }
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }
    }

    /// <summary>Нота мелодии: звук текущего аккорда в высоком регистре, изредка — «неправильная» соседняя нота.</summary>
    static int PickNote(System.Random rng, double t)
    {
        var chord = Chords[(int)(t / ChordLen) % ChordCount];
        int m = chord[1 + rng.Next(chord.Length - 1)] + 12;
        if (m < 69) m += 12;
        if (rng.NextDouble() < 0.4 && m + 12 <= 88) m += 12;
        if (rng.NextDouble() < 0.1) m += 1; // тревожная полутоновая «ошибка»
        return m;
    }

    /// <summary>Колокольчик / музыкальная шкатулка: чистый тон с неровными призвуками и долгим затуханием.</summary>
    static void AddBell(float[] l, float[] r, int n, double start, int midi, float vel, float pan)
    {
        double f = Freq(midi);
        Pan(pan, out float pl, out float pr);
        int len = (int)(6.0 * SR);
        long s0 = (long)Math.Round(start * SR);
        for (int j = 0; j < len; j++)
        {
            double t = (double)j / SR;
            double att = Math.Min(1, t / 0.008);
            double w = 2 * Math.PI * f * t;
            double v = Math.Sin(w) * Math.Exp(-t / 2.2)
                     + 0.25 * Math.Sin(w * 2.76) * Math.Exp(-t / 0.6)
                     + 0.10 * Math.Sin(w * 5.40) * Math.Exp(-t / 0.25);
            float s = (float)(v * att * 0.09 * vel);
            int k = Wrap(s0 + j, n);
            l[k] += s * pl; r[k] += s * pr;
        }
    }

    /// <summary>Далёкий глухой удар: низкий тон, уходящий вниз, и приглушённый шум.</summary>
    static void AddBoom(float[] l, float[] r, int n, double start, System.Random rng)
    {
        int len = (int)(5.0 * SR);
        long s0 = (long)Math.Round(start * SR);
        double ph = 0; float noiseLp = 0;
        for (int j = 0; j < len; j++)
        {
            double t = (double)j / SR;
            double f = 38 + 17 * Math.Exp(-t / 1.2);
            ph += 2 * Math.PI * f / SR;
            double env = Math.Min(1, t / 0.03) * Math.Exp(-t / 1.4);
            noiseLp += 0.02f * ((float)(rng.NextDouble() * 2 - 1) - noiseLp);
            float s = (float)((Math.Sin(ph) * 0.35 + noiseLp * 1.5) * env);
            int k = Wrap(s0 + j, n);
            l[k] += s; r[k] += s * 0.9f;
        }
    }

    /// <summary>Диссонирующий высокий звук (си–до), медленно нарастает из тишины и уходит.</summary>
    static void AddSwell(float[] l, float[] r, int n, double start, double dur)
    {
        int len = (int)(dur * SR);
        long s0 = (long)Math.Round(start * SR);
        double f1 = Freq(83), f2 = Freq(84);
        for (int j = 0; j < len; j++)
        {
            double t = (double)j / SR;
            double env = Math.Sin(Math.PI * t / dur); env *= env;
            double trem = 0.7 + 0.3 * Math.Sin(2 * Math.PI * 0.3 * t);
            float a = (float)(Math.Sin(2 * Math.PI * f1 * t) * 0.022 * env * trem);
            float b = (float)(Math.Sin(2 * Math.PI * f2 * t) * 0.018 * env * (1.7 - trem));
            int k = Wrap(s0 + j, n);
            l[k] += a + b * 0.6f; r[k] += a * 0.6f + b;
        }
    }

    /// <summary>Упрощённый Freeverb: 8 гребенчатых фильтров и 4 всепропускающих — большой пустой зал.</summary>
    class Freeverb
    {
        readonly float[][] comb, ap;
        readonly int[] ci, ai;
        readonly float[] store;
        const float Feedback = 0.88f, Damp = 0.3f, InputGain = 0.02f;

        public Freeverb(int spread)
        {
            int[] cl = { 1557, 1617, 1491, 1422, 1277, 1356, 1188, 1116 };
            int[] al = { 556, 441, 341, 225 };
            comb = new float[cl.Length][]; ci = new int[cl.Length]; store = new float[cl.Length];
            for (int i = 0; i < cl.Length; i++) comb[i] = new float[cl[i] + spread];
            ap = new float[al.Length][]; ai = new int[al.Length];
            for (int i = 0; i < al.Length; i++) ap[i] = new float[al[i] + spread];
        }

        public float Process(float x)
        {
            float input = x * InputGain, o = 0;
            for (int i = 0; i < comb.Length; i++)
            {
                var b = comb[i];
                float y = b[ci[i]];
                store[i] = y * (1 - Damp) + store[i] * Damp;
                b[ci[i]] = input + store[i] * Feedback;
                ci[i] = (ci[i] + 1) % b.Length;
                o += y;
            }
            for (int i = 0; i < ap.Length; i++)
            {
                var b = ap[i];
                float bv = b[ai[i]];
                float y = -o + bv;
                b[ai[i]] = o + bv * 0.5f;
                ai[i] = (ai[i] + 1) % b.Length;
                o = y;
            }
            return o;
        }
    }

    static void WriteWav(string path, float[] l, float[] r)
    {
        int n = l.Length;
        using (var fs = new FileStream(path, FileMode.Create))
        using (var w = new BinaryWriter(fs))
        {
            int dataBytes = n * 2 * 2;
            w.Write(new[] { (byte)'R', (byte)'I', (byte)'F', (byte)'F' });
            w.Write(36 + dataBytes);
            w.Write(new[] { (byte)'W', (byte)'A', (byte)'V', (byte)'E' });
            w.Write(new[] { (byte)'f', (byte)'m', (byte)'t', (byte)' ' });
            w.Write(16); w.Write((short)1); w.Write((short)2); w.Write(SR); w.Write(SR * 4); w.Write((short)4); w.Write((short)16);
            w.Write(new[] { (byte)'d', (byte)'a', (byte)'t', (byte)'a' });
            w.Write(dataBytes);
            for (int i = 0; i < n; i++)
            {
                w.Write((short)Mathf.Clamp(Mathf.RoundToInt(l[i] * 32767f), -32768, 32767));
                w.Write((short)Mathf.Clamp(Mathf.RoundToInt(r[i] * 32767f), -32768, 32767));
            }
        }
    }
}
