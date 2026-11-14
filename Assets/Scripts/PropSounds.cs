using UnityEngine;

/// <summary>Из чего сделан мелкий предмет — от этого зависит звук удара.</summary>
public enum PropMaterial { Plastic, Glass, Ceramic, Metal, Cardboard, Soft }

/// <summary>
/// Процедурные звуки ударов мелких предметов (без аудиофайлов): короткие и жёсткие — металл глухо «бзынкает»,
/// стекло и керамика сухо цокают, пластик и дерево стучат, картон и продукты глухо шлёпают.
/// </summary>
public static class PropSounds
{
    const int Rate = 44100;
    const int Variants = 3;
    static AudioClip[,] clips;

    static readonly (string prefix, PropMaterial mat)[] Materials =
    {
        ("Drink_Tea", PropMaterial.Ceramic), ("Drink_", PropMaterial.Glass), ("OilBottle", PropMaterial.Glass),
        ("SaladPlate", PropMaterial.Ceramic), ("BreadPlate", PropMaterial.Ceramic), ("PlateStack", PropMaterial.Ceramic),
        ("Mug", PropMaterial.Ceramic), ("BowlStack", PropMaterial.Ceramic), ("Bowl_", PropMaterial.Ceramic),
        ("CutleryHolder", PropMaterial.Metal), ("Fork", PropMaterial.Metal), ("Spoon", PropMaterial.Metal),
        ("TableKnife", PropMaterial.Metal), ("ChefKnife", PropMaterial.Metal), ("Whisk", PropMaterial.Metal),
        ("Ladle", PropMaterial.Metal), ("Skimmer", PropMaterial.Metal), ("StockPot", PropMaterial.Metal),
        ("Saucepan", PropMaterial.Metal), ("FryingPan", PropMaterial.Metal), ("GN_", PropMaterial.Metal),
        ("Tray_GN", PropMaterial.Metal), ("CutleryTray", PropMaterial.Metal), ("MixingBowl", PropMaterial.Metal),
        ("FireExtinguisher", PropMaterial.Metal), ("Bin_", PropMaterial.Metal), ("TrashBin", PropMaterial.Metal),
        ("CardboardBox", PropMaterial.Cardboard),
        ("Tomato", PropMaterial.Soft), ("Carrot", PropMaterial.Soft), ("Cucumber", PropMaterial.Soft),
    };

    public static PropMaterial MaterialFor(string objectName)
    {
        foreach (var (prefix, mat) in Materials)
            if (objectName.StartsWith(prefix)) return mat;
        return PropMaterial.Plastic; // подносы, салфетницы, доски, контейнеры, весы, лопатка, канистра
    }

    /// <summary>Звук удара в точке. strength 0..1 — сила удара, mass — масса предмета (тяжёлые звучат ниже).</summary>
    public static void PlayImpact(PropMaterial mat, Vector3 point, float strength, float mass)
    {
        if (clips == null) Build();
        var clip = clips[(int)mat, Random.Range(0, Variants)];
        var go = new GameObject("PropImpactSound");
        go.transform.position = point;
        var src = go.AddComponent<AudioSource>();
        src.clip = clip;
        src.spatialBlend = 1f;
        src.rolloffMode = AudioRolloffMode.Logarithmic;
        src.minDistance = 1.5f;
        src.maxDistance = 30f;
        src.dopplerLevel = 0f;
        src.pitch = Mathf.Clamp(1.25f - Mathf.Log10(1f + mass * 3f) * 0.35f, 0.7f, 1.3f) * Random.Range(0.92f, 1.08f);
        src.volume = Mathf.Lerp(0.08f, 0.5f, strength) * GameSettings.EffectsVolume;
        src.Play();
        Object.Destroy(go, clip.length / Mathf.Max(0.1f, src.pitch) + 0.1f);
    }

    static void Build()
    {
        int n = System.Enum.GetValues(typeof(PropMaterial)).Length;
        clips = new AudioClip[n, Variants];
        for (int m = 0; m < n; m++)
            for (int v = 0; v < Variants; v++)
                clips[m, v] = Create((PropMaterial)m, v);
    }

    static AudioClip Create(PropMaterial mat, int variant)
    {
        var rnd = new System.Random(4271 + (int)mat * 97 + variant * 13);
        float Rand(float a, float b) => a + (float)rnd.NextDouble() * (b - a);
        float Noise() => (float)(rnd.NextDouble() * 2.0 - 1.0);

        float duration;
        float[] freqs, amps, decays;
        float clickAmp, clickDecay, noiseAmp, noiseDecay, noiseCutoff;
        float shift = Rand(0.94f, 1.06f);
        // Удары жёсткие и короткие: основа — щелчок и шумовой «тук», звон обертонов лишь слегка окрашивает и быстро гаснет.
        switch (mat)
        {
            case PropMaterial.Metal: // глухой металлический «бзынк»
                duration = 0.3f;
                freqs = new[] { 430f, 1180f, 2300f };
                amps = new[] { 0.55f, 0.3f, 0.12f };
                decays = new[] { 16f, 24f, 34f };
                clickAmp = 1f; clickDecay = 180f; noiseAmp = 0.8f; noiseDecay = 45f; noiseCutoff = 3000f;
                break;
            case PropMaterial.Glass: // короткий «цок»
                duration = 0.16f;
                freqs = new[] { 1600f, 3500f };
                amps = new[] { 0.4f, 0.15f };
                decays = new[] { 40f, 60f };
                clickAmp = 1f; clickDecay = 260f; noiseAmp = 0.6f; noiseDecay = 70f; noiseCutoff = 4000f;
                break;
            case PropMaterial.Ceramic: // сухой «клац»
                duration = 0.14f;
                freqs = new[] { 950f, 2100f };
                amps = new[] { 0.4f, 0.15f };
                decays = new[] { 55f, 75f };
                clickAmp = 1f; clickDecay = 220f; noiseAmp = 0.8f; noiseDecay = 60f; noiseCutoff = 2800f;
                break;
            case PropMaterial.Cardboard: // глухой «бух»
                duration = 0.2f;
                freqs = new[] { 90f, 170f };
                amps = new[] { 0.8f, 0.3f };
                decays = new[] { 30f, 40f };
                clickAmp = 0.35f; clickDecay = 150f; noiseAmp = 1f; noiseDecay = 35f; noiseCutoff = 550f;
                break;
            case PropMaterial.Soft: // мягкий шлепок
                duration = 0.18f;
                freqs = new[] { 110f };
                amps = new[] { 0.6f };
                decays = new[] { 35f };
                clickAmp = 0.2f; clickDecay = 120f; noiseAmp = 1f; noiseDecay = 38f; noiseCutoff = 350f;
                break;
            default: // пластик и дерево: сухой «тук»
                duration = 0.15f;
                freqs = new[] { 280f, 700f };
                amps = new[] { 0.6f, 0.2f };
                decays = new[] { 50f, 70f };
                clickAmp = 0.9f; clickDecay = 200f; noiseAmp = 0.9f; noiseDecay = 55f; noiseCutoff = 1300f;
                break;
        }

        int length = Mathf.RoundToInt(Rate * duration);
        var data = new float[length];
        var phase = new float[freqs.Length];
        var detune = new float[freqs.Length];
        for (int k = 0; k < freqs.Length; k++) { detune[k] = shift * Rand(0.97f, 1.03f); phase[k] = Rand(0f, 6.283f); }
        float lp = 0f, lpK = 1f - Mathf.Exp(-2f * Mathf.PI * noiseCutoff / Rate);
        float max = 1e-4f;
        for (int i = 0; i < length; i++)
        {
            float t = (float)i / Rate;
            float v = 0f;
            for (int k = 0; k < freqs.Length; k++)
                v += amps[k] * Mathf.Sin(phase[k] + 2f * Mathf.PI * freqs[k] * detune[k] * t) * Mathf.Exp(-decays[k] * t);
            float nz = Noise();
            lp += lpK * (nz - lp);
            v += noiseAmp * lp * Mathf.Exp(-noiseDecay * t);
            v += clickAmp * nz * Mathf.Exp(-clickDecay * t);
            data[i] = v;
            max = Mathf.Max(max, Mathf.Abs(v));
        }
        int fade = Rate / 100;
        for (int i = 0; i < length; i++)
        {
            float tail = Mathf.Min(1f, (length - 1 - i) / (float)fade);
            data[i] = data[i] / max * 0.9f * tail;
        }
        var clip = AudioClip.Create("PropImpact_" + mat + variant, length, 1, Rate, false);
        clip.SetData(data, 0);
        return clip;
    }
}
