using System;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Синтезирует фоновую музыку игры — две бесшовные петли одной длины, темпа и гармонии (их играет GameMusic синхронно
/// и плавно переключает между ними):
///  • Assets/Resources/Music/GameAmbient.wav — тревожное затишье: тёмный пэд, глухой пульс баса, редкие ноты-колокольчики;
///  • Assets/Resources/Music/GameCombat.wav — бой (волны зомби, Декан): тот же пэд + бас восьмыми, барабаны, остинато.
/// Ля минор, 112 уд/мин, 16 тактов (Am – F – Dm – E, во второй половине Am – F – G – E).
/// Файлы можно заменить своими треками с теми же именами. Перегенерировать: Tools → Game → Regenerate Music.
/// </summary>
public static class GameMusicGenerator
{
    public const string AmbientPath = "Assets/Resources/Music/GameAmbient.wav";
    public const string CombatPath = "Assets/Resources/Music/GameCombat.wav";

    const int SR = 44100;
    const double Bpm = 112;
    const double Beat = 60.0 / Bpm;
    const int BarsPerChord = 2;
    const int Bars = 16;
    const double Length = Bars * 4 * Beat; // ≈ 34,3 с

    // Бас (MIDI) и голоса аккорда.
    static readonly int[][] Chords =
    {
        new[] { 33, 57, 60, 64 }, // Am
        new[] { 29, 57, 60, 65 }, // F
        new[] { 26, 57, 62, 65 }, // Dm
        new[] { 28, 56, 59, 64 }, // E
        new[] { 33, 57, 60, 64 }, // Am
        new[] { 29, 57, 60, 65 }, // F
        new[] { 31, 55, 59, 62 }, // G
        new[] { 28, 56, 59, 64 }, // E
    };

    [MenuItem("Tools/Game/Regenerate Music")]
    public static void Regenerate()
    {
        Generate();
        Debug.Log("GameMusicGenerator: музыка игры сгенерирована: " + AmbientPath + ", " + CombatPath);
    }

    /// <summary>Для запуска из командной строки (-executeMethod GameMusicGenerator.Regenerate).</summary>
    public static void EnsureClips()
    {
        if (!File.Exists(AmbientPath) || !File.Exists(CombatPath)) Generate();
    }

    static double Freq(int midi) => 440.0 * Math.Pow(2.0, (midi - 69) / 12.0);
    static int Wrap(long i, int n) => (int)(((i % n) + n) % n);
    static double Smooth(double x) { x = Math.Max(0, Math.Min(1, x)); double s = Math.Sin(x * Math.PI / 2); return s * s; }
    static int[] ChordAt(double t) => Chords[(int)(t / (BarsPerChord * 4 * Beat)) % Chords.Length];

    static void Pan(float pan, out float l, out float r)
    {
        l = (float)Math.Sqrt(0.5 * (1 - pan));
        r = (float)Math.Sqrt(0.5 * (1 + pan));
    }

    class Bus
    {
        public readonly float[] L, R;
        public Bus(int n) { L = new float[n]; R = new float[n]; }
        public void Add(long i, float s, float pan = 0f)
        {
            Pan(pan, out float pl, out float pr);
            int k = Wrap(i, L.Length);
            L[k] += s * pl * 1.41f; R[k] += s * pr * 1.41f;
        }
    }

    public static void Generate()
    {
        int n = (int)Math.Round(SR * Length);
        var rng = new System.Random(20261118);
        try
        {
            EditorUtility.DisplayProgressBar("Музыка игры", "Пэд…", 0f);
            var pad = new Bus(n);
            var drone = new Bus(n);
            var bells = new Bus(n);
            var bassPulse = new Bus(n);
            var bassDrive = new Bus(n);
            var drums = new Bus(n);
            var ostinato = new Bus(n);

            // ---------- Пэд: по аккорду на два такта, с мягкой атакой и перекрытием ----------
            double chordLen = BarsPerChord * 4 * Beat;
            for (int c = 0; c < Chords.Length; c++)
            {
                var chord = Chords[c];
                double start = c * chordLen - 0.6, dur = chordLen + 1.6;
                int len = (int)(dur * SR);
                long s0 = (long)Math.Round(start * SR);
                double[] detune = { -8, 0, 7 };
                float[] pans = { -0.7f, 0f, 0.7f };
                for (int v = 1; v < chord.Length; v++)
                    for (int o = 0; o < 3; o++)
                    {
                        double f = Freq(chord[v]) * Math.Pow(2, detune[o] / 1200.0), ph = rng.NextDouble() * Math.PI * 2;
                        for (int j = 0; j < len; j++)
                        {
                            double t = (double)j / SR;
                            double env = Smooth(t / 0.9) * Smooth((dur - t) / 1.2);
                            ph += 2 * Math.PI * f / SR;
                            float s = (float)((Math.Sin(ph) + 0.3 * Math.Sin(2 * ph) + 0.1 * Math.Sin(3 * ph)) * 0.030 * env);
                            pad.Add(s0 + j, s, pans[o]);
                        }
                    }
            }

            // ---------- Низкий гул: тоника ля с медленной пульсацией ----------
            {
                // Целое число периодов на петлю — иначе на стыке щелчок.
                double loopSec = (double)n / SR, f = Math.Round(Freq(33) * loopSec) / loopSec;
                for (int i = 0; i < n; i++)
                {
                    double t = (double)i / SR;
                    double ph = 2 * Math.PI * f * t;
                    double trem = 0.75 + 0.25 * Math.Sin(2 * Math.PI * 2 / Length * t);
                    drone.Add(i, (float)((Math.Sin(ph) + 0.15 * Math.Sin(2 * ph)) * 0.07 * trem));
                }
            }

            // ---------- Бас ----------
            EditorUtility.DisplayProgressBar("Музыка игры", "Бас…", 0.3f);
            int beats = Bars * 4;
            for (int b = 0; b < beats; b++)
            {
                double t0 = b * Beat;
                int root = ChordAt(t0)[0];
                // Затишье: глухой удар баса на первую и третью долю.
                if (b % 2 == 0) Pluck(bassPulse, t0, Freq(root + 12), 0.55, 0.16f, 220, 0f);
                // Бой: восьмые, на каждой четвёртой — октава вверх.
                for (int e = 0; e < 2; e++)
                {
                    int m = root + 12 + ((b * 2 + e) % 4 == 3 ? 12 : 0);
                    Pluck(bassDrive, t0 + e * Beat / 2, Freq(m), 0.22, 0.17f, 700, 0f);
                }
            }

            // ---------- Колокольчики затишья: редкие ноты аккорда в высоком регистре ----------
            for (int b = 0; b < beats; b += 2)
            {
                if (rng.NextDouble() > 0.4) continue;
                double t0 = b * Beat + (rng.NextDouble() < 0.5 ? 0 : Beat);
                var chord = ChordAt(t0);
                int m = chord[1 + rng.Next(chord.Length - 1)] + 12;
                Bell(bells, t0, Freq(m), 0.5f + 0.4f * (float)rng.NextDouble(), (float)(rng.NextDouble() - 0.5) * 1.2f);
            }

            // ---------- Барабаны боя ----------
            EditorUtility.DisplayProgressBar("Музыка игры", "Барабаны…", 0.5f);
            for (int bar = 0; bar < Bars; bar++)
                for (int s16 = 0; s16 < 16; s16++)
                {
                    double t0 = (bar * 4 + s16 / 4.0) * Beat;
                    bool fill = bar % 4 == 3 && s16 >= 12;
                    if (s16 == 0 || s16 == 8 || (s16 == 10 && bar % 2 == 1) || (s16 == 7 && bar % 4 == 2)) Kick(drums, t0);
                    if (s16 == 4 || s16 == 12 || (fill && s16 % 2 == 0)) Snare(drums, t0, fill && s16 != 12 ? 0.6f : 1f, rng);
                    if (s16 % 2 == 0) Hat(drums, t0, s16 % 4 == 2 ? 0.9f : 0.5f, rng);
                }

            // ---------- Остинато боя: шестнадцатые по звукам аккорда ----------
            int[] pattern = { 0, 2, 1, 2, 0, 2, 1, 3 };
            for (int s = 0; s < Bars * 16; s++)
            {
                double t0 = s * Beat / 4;
                var chord = ChordAt(t0);
                int idx = pattern[s % pattern.Length];
                int m = idx == 3 ? chord[1] + 12 : chord[1 + idx];
                float vel = s % 4 == 0 ? 1f : 0.65f;
                Pluck(ostinato, t0, Freq(m), 0.16, 0.05f * vel, 2200, s % 2 == 0 ? -0.35f : 0.35f);
            }

            // ---------- Сведение ----------
            EditorUtility.DisplayProgressBar("Музыка игры", "Сведение…", 0.75f);
            float[] ambL, ambR, comL, comR;
            Mix(n, out ambL, out ambR, (int k, out float dl, out float dr, out float sl, out float sr) =>
            {
                dl = pad.L[k] * 0.9f + drone.L[k] + bassPulse.L[k] + bells.L[k] * 0.7f;
                dr = pad.R[k] * 0.9f + drone.R[k] + bassPulse.R[k] + bells.R[k] * 0.7f;
                sl = pad.L[k] * 0.6f + bells.L[k] * 1.2f;
                sr = pad.R[k] * 0.6f + bells.R[k] * 1.2f;
            }, 0.55f);
            Mix(n, out comL, out comR, (int k, out float dl, out float dr, out float sl, out float sr) =>
            {
                dl = pad.L[k] * 0.8f + drone.L[k] * 0.6f + bassDrive.L[k] + drums.L[k] + ostinato.L[k];
                dr = pad.R[k] * 0.8f + drone.R[k] * 0.6f + bassDrive.R[k] + drums.R[k] + ostinato.R[k];
                sl = pad.L[k] * 0.5f + drums.L[k] * 0.25f + ostinato.L[k] * 0.6f;
                sr = pad.R[k] * 0.5f + drums.R[k] * 0.25f + ostinato.R[k] * 0.6f;
            }, 0.35f);

            // Общая нормализация: бой немного громче затишья, относительный баланс сохраняется.
            Normalize(ambL, ambR, 0.70f);
            Normalize(comL, comR, 0.89f);

            EditorUtility.DisplayProgressBar("Музыка игры", "Запись файлов…", 0.95f);
            Save(AmbientPath, ambL, ambR);
            Save(CombatPath, comL, comR);
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }
    }

    delegate void Sample(int k, out float dryL, out float dryR, out float sendL, out float sendR);

    /// <summary>Сухой сигнал + реверб; петля прогоняется дважды, чтобы хвост реверба перешёл в начало без шва.</summary>
    static void Mix(int n, out float[] outL, out float[] outR, Sample src, float wet)
    {
        outL = new float[n]; outR = new float[n];
        var revL = new Freeverb(0); var revR = new Freeverb(23);
        for (long i = 0; i < 2L * n; i++)
        {
            int k = (int)(i % n);
            src(k, out float dl, out float dr, out float sl, out float sr);
            float wl = revL.Process(sl), wr = revR.Process(sr);
            if (i >= n) { outL[k] = dl + wl * wet; outR[k] = dr + wr * wet; }
        }
    }

    static void Normalize(float[] l, float[] r, float target)
    {
        float peak = 1e-6f;
        for (int i = 0; i < l.Length; i++) peak = Math.Max(peak, Math.Max(Math.Abs(l[i]), Math.Abs(r[i])));
        float g = target / peak;
        for (int i = 0; i < l.Length; i++) { l[i] *= g; r[i] *= g; }
    }

    /// <summary>Щипковый звук «пилой» (8 гармоник) с фильтром, который закрывается вместе с огибающей.</summary>
    static void Pluck(Bus bus, double start, double f, double decay, float amp, double cutoff, float pan)
    {
        int len = (int)((decay * 4 + 0.05) * SR);
        long s0 = (long)Math.Round(start * SR);
        double ph = 0; float lp = 0;
        for (int j = 0; j < len; j++)
        {
            double t = (double)j / SR;
            ph += 2 * Math.PI * f / SR;
            double saw = 0;
            for (int h = 1; h <= 8; h++) saw += Math.Sin(ph * h) / h;
            double env = Math.Min(1, t / 0.004) * Math.Exp(-t / decay);
            double cut = 80 + cutoff * Math.Exp(-t / (decay * 0.7));
            float a = (float)(1 - Math.Exp(-2 * Math.PI * cut / SR));
            lp += a * ((float)(saw * 0.6) - lp);
            bus.Add(s0 + j, (float)(lp * env * amp * 2.2), pan);
        }
    }

    static void Bell(Bus bus, double start, double f, float vel, float pan)
    {
        int len = (int)(4.0 * SR);
        long s0 = (long)Math.Round(start * SR);
        for (int j = 0; j < len; j++)
        {
            double t = (double)j / SR;
            double w = 2 * Math.PI * f * t;
            double v = Math.Sin(w) * Math.Exp(-t / 1.6) + 0.22 * Math.Sin(w * 2.76) * Math.Exp(-t / 0.5) + 0.08 * Math.Sin(w * 5.4) * Math.Exp(-t / 0.2);
            bus.Add(s0 + j, (float)(v * Math.Min(1, t / 0.006) * 0.06 * vel), pan);
        }
    }

    static void Kick(Bus bus, double start)
    {
        int len = (int)(0.45 * SR);
        long s0 = (long)Math.Round(start * SR);
        double ph = 0;
        for (int j = 0; j < len; j++)
        {
            double t = (double)j / SR;
            double f = 45 + 95 * Math.Exp(-t / 0.035);
            ph += 2 * Math.PI * f / SR;
            double env = Math.Min(1, t / 0.002) * Math.Exp(-t / 0.16);
            double click = Math.Exp(-t / 0.004) * 0.3;
            bus.Add(s0 + j, (float)((Math.Sin(ph) + click) * env * 0.42));
        }
    }

    static void Snare(Bus bus, double start, float vel, System.Random rng)
    {
        int len = (int)(0.3 * SR);
        long s0 = (long)Math.Round(start * SR);
        float prev = 0, lp = 0;
        for (int j = 0; j < len; j++)
        {
            double t = (double)j / SR;
            float noise = (float)(rng.NextDouble() * 2 - 1);
            float hp = noise - prev; prev = noise;          // верха шума
            lp += 0.5f * (hp - lp);                         // без самого резкого «шипения»
            double body = Math.Sin(2 * Math.PI * 185 * t) * Math.Exp(-t / 0.05);
            double env = Math.Exp(-t / 0.09);
            bus.Add(s0 + j, (float)((lp * 0.55 * env + body * 0.35) * 0.32 * vel), 0.08f);
        }
    }

    static void Hat(Bus bus, double start, float vel, System.Random rng)
    {
        int len = (int)(0.07 * SR);
        long s0 = (long)Math.Round(start * SR);
        float prev = 0, prev2 = 0;
        for (int j = 0; j < len; j++)
        {
            double t = (double)j / SR;
            float noise = (float)(rng.NextDouble() * 2 - 1);
            float hp = noise - 2 * prev + prev2; prev2 = prev; prev = noise;
            bus.Add(s0 + j, (float)(hp * Math.Exp(-t / 0.018) * 0.045 * vel), 0.3f);
        }
    }

    static void Save(string path, float[] l, float[] r)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        WriteWav(path, l, r);
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
        if (AssetImporter.GetAtPath(path) is AudioImporter importer)
        {
            var s = importer.defaultSampleSettings;
            s.loadType = AudioClipLoadType.CompressedInMemory;
            s.compressionFormat = AudioCompressionFormat.Vorbis;
            s.quality = 0.6f;
            importer.defaultSampleSettings = s;
            importer.loadInBackground = false; // обе петли должны быть готовы к одному моменту — играют синхронно
            importer.SaveAndReimport();
        }
    }

    /// <summary>Упрощённый Freeverb (как в MenuMusicGenerator), чуть короче — помещение поменьше.</summary>
    class Freeverb
    {
        readonly float[][] comb, ap;
        readonly int[] ci, ai;
        readonly float[] store;
        const float Feedback = 0.82f, Damp = 0.35f, InputGain = 0.02f;

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
