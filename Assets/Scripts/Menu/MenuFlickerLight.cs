using UnityEngine;

/// <summary>
/// Неисправная потолочная лампа для фона главного меню: в основном тускло горит с лёгким гулом,
/// время от времени судорожно мигает и иногда гаснет на пару секунд.
///
/// Синхронизация звука и света: мигание рассчитывается в звуковом потоке (OnAudioFilterRead) с точностью до сэмпла —
/// гул, треск и щелчки дросселя звучат ровно в моменты переходов. Свет и свечение панели берут уровень из истории
/// звукового потока на момент, который сейчас слышен (с поправкой на задержку вывода звука).
/// Если звука нет (выключен у лампы или нет аудиоустройства), мигание считается в Update, без звука.
/// Звук приглушённый (фильтр низких частот), громкость зависит от расстояния до камеры и ползунка «Эффекты».
/// </summary>
[RequireComponent(typeof(Light))]
public class MenuFlickerLight : MonoBehaviour
{
    [Tooltip("Яркость света при «нормальной» работе.")]
    public float intensity = 2.5f;
    [Tooltip("Яркость свечения панели (HDR-множитель).")]
    public float emission = 3f;
    public Color emissionColor = new Color(0.85f, 1f, 0.92f);

    [Header("Характер мигания")]
    [Tooltip("Уровень в спокойном режиме (0..1).")]
    [Range(0f, 1f)] public float steadyLevel = 0.85f;
    [Tooltip("Сколько секунд лампа горит спокойно между приступами (мин/макс).")]
    public Vector2 steadyTime = new Vector2(1.5f, 6f);
    [Tooltip("Длительность приступа мигания (мин/макс).")]
    public Vector2 stutterTime = new Vector2(0.3f, 1.6f);
    [Tooltip("Вероятность, что после спокойного режима лампа погаснет совсем.")]
    [Range(0f, 1f)] public float offChance = 0.25f;
    public Vector2 offTime = new Vector2(0.5f, 2.5f);

    [Header("Звук")]
    public bool sound = true;
    [Tooltip("Громкость гула при полном свечении.")]
    [Range(0f, 1f)] public float humVolume = 0.055f;
    [Tooltip("Громкость хруста (как ломается ветка) при вспышке лампы.")]
    [Range(0f, 1f)] public float clickVolume = 0.4f;
    [Tooltip("Громкость потрескивания во время мигания.")]
    [Range(0f, 1f)] public float crackleVolume = 0.05f;
    [Tooltip("Частота среза фильтра — чем ниже, тем глуше звук (Гц).")]
    public float muffle = 1400f;
    [Tooltip("До этого расстояния звук не затихает (м).")]
    public float minDistance = 2.5f;
    [Tooltip("Дополнительный сдвиг света относительно звука (с): больше — свет позже.")]
    public float syncOffset = 0f;

    [Header("Панель светильника")]
    public Renderer panel;
    public int emissionSlot = -1;
    public Material emissionMaterial;

    Light lightSource;
    Material instance;
    Flicker audioFlicker, localFlicker;
    Transform listener;
    float outputLatency;
    int sampleRate;

    // Данные для звукового потока (пишутся в Update).
    volatile float gainL, gainR, fxVolume;
    volatile bool audioReady;

    // История уровня из звукового потока: время DSP и уровень, каждые HistoryStep сэмплов.
    const int HistorySize = 8192, HistoryStep = 32;
    readonly double[] histTime = new double[HistorySize];
    readonly float[] histLevel = new float[HistorySize];
    volatile int histWrite;

    // Состояние синтеза (только звуковой поток).
    System.Random arng;
    float smooth, lp, lpCrack, crackleEnv;
    double lastCrack = -1.0;

    LampCrunch crunch; // жёсткий хруст в момент вспышки (см. LampCrunch)
    double humPhase;
    bool lit = true;
    int sinceHist;

    // ---------- Мигание (одна логика для звукового потока и для Update) ----------

    class Flicker
    {
        enum Mode { Steady, Stutter, Off }
        readonly MenuFlickerLight o;
        readonly System.Random rng;
        Mode mode;
        float modeLeft, toggleLeft, stutterTarget, ph1, ph2;
        public float Level;
        public bool Stuttering => mode == Mode.Stutter;
        /// <summary>Лампа гасла надолго и ещё не зажглась — щелчок при зажигании громче.</summary>
        public bool WasOff;
        public bool IsOff => mode == Mode.Off;

        public Flicker(MenuFlickerLight owner, int seed)
        {
            o = owner;
            rng = new System.Random(seed);
            ph1 = (float)rng.NextDouble() * 6.28f;
            ph2 = (float)rng.NextDouble() * 6.28f;
            EnterSteady();
            Level = o.steadyLevel;
        }

        float Range(float a, float b) => a + (float)rng.NextDouble() * (b - a);
        void EnterSteady() { mode = Mode.Steady; modeLeft = Range(o.steadyTime.x, o.steadyTime.y); }
        void EnterStutter() { mode = Mode.Stutter; modeLeft = Range(o.stutterTime.x, o.stutterTime.y); toggleLeft = 0f; }
        void EnterOff() { mode = Mode.Off; modeLeft = Range(o.offTime.x, o.offTime.y); WasOff = true; }

        /// <summary>Шаг на dt секунд; t — текущее время (для «гула» яркости).</summary>
        public void Step(float dt, double t)
        {
            modeLeft -= dt;
            switch (mode)
            {
                case Mode.Steady:
                    float hum = 0.5f + 0.25f * Mathf.Sin((float)(t * 8.2) + ph1) + 0.25f * Mathf.Sin((float)(t * 23.3) + ph2);
                    float target = o.steadyLevel * (0.93f + 0.07f * hum);
                    Level = Mathf.MoveTowards(Level, target, dt * 4f);
                    if (modeLeft <= 0f) { if (rng.NextDouble() < o.offChance) EnterOff(); else EnterStutter(); }
                    break;
                case Mode.Stutter:
                    toggleLeft -= dt;
                    if (toggleLeft <= 0f)
                    {
                        toggleLeft = Range(0.03f, 0.14f);
                        stutterTarget = rng.NextDouble() < 0.5 ? Range(0f, 0.15f) : Range(0.6f, 1f);
                    }
                    Level = stutterTarget; // лампа щёлкает резко
                    if (modeLeft <= 0f) EnterSteady();
                    break;
                default:
                    Level = Mathf.MoveTowards(Level, 0.02f, dt * 20f);
                    if (modeLeft <= 0f) EnterStutter();
                    break;
            }
        }
    }

    // ---------- Unity ----------

    void Awake()
    {
        lightSource = GetComponent<Light>();
        int seed = System.Environment.TickCount ^ (name.GetHashCode() * 397) ^ transform.position.GetHashCode();
        localFlicker = new Flicker(this, seed);
        if (panel != null && emissionMaterial != null && emissionSlot >= 0)
        {
            instance = new Material(emissionMaterial) { name = emissionMaterial.name + " (flicker)" };
            var mats = panel.sharedMaterials;
            if (emissionSlot < mats.Length)
            {
                mats[emissionSlot] = instance;
                panel.sharedMaterials = mats;
            }
        }

        if (sound)
        {
            sampleRate = AudioSettings.outputSampleRate;
            AudioSettings.GetDSPBufferSize(out int bufLen, out int numBuf);
            // Звук, рассчитанный сейчас, будет слышен примерно через пару буферов вывода.
            outputLatency = sampleRate > 0 ? bufLen * 2f / sampleRate : 0f;
            audioFlicker = new Flicker(this, seed * 31 + 7);
            arng = new System.Random(seed + 13);
            crunch = new LampCrunch(arng);
            smooth = audioFlicker.Level;
            var src = gameObject.AddComponent<AudioSource>();
            src.playOnAwake = false;
            src.loop = true;
            src.spatialBlend = 0f; // громкость и панораму считаем сами — см. UpdateSpatial
            src.dopplerLevel = 0f;
            UpdateSpatial();
            audioReady = true;
            src.Play();
        }
    }

    void OnDestroy()
    {
        audioReady = false;
        if (instance != null) Destroy(instance);
    }

    void Update()
    {
        float level;
        if (sound && TryAudioLevel(AudioSettings.dspTime - outputLatency - syncOffset, out level))
        {
            UpdateSpatial();
        }
        else
        {
            // Звука нет — мигаем по своим часам.
            localFlicker.Step(Time.unscaledDeltaTime, Time.unscaledTime);
            level = localFlicker.Level;
            if (sound) UpdateSpatial();
        }

        lightSource.intensity = intensity * level;
        lightSource.enabled = level > 0.01f;
        if (instance != null)
            instance.SetColor("_EmissionColor", emissionColor * (emission * level));
    }

    /// <summary>Уровень из истории звукового потока на момент time. false — истории ещё нет или она устарела.</summary>
    bool TryAudioLevel(double time, out float level)
    {
        level = 0f;
        int w = histWrite;
        if (w == 0) return false;
        int latest = (w - 1) % HistorySize;
        if (histTime[latest] < time - 0.5) return false; // звуковой поток не работает (пауза, нет устройства)
        int count = Mathf.Min(w, HistorySize);
        for (int k = 0; k < count; k++)
        {
            int i = ((w - 1 - k) % HistorySize + HistorySize) % HistorySize;
            if (histTime[i] <= time) { level = histLevel[i]; return true; }
        }
        level = histLevel[((w - count) % HistorySize + HistorySize) % HistorySize];
        return true;
    }

    /// <summary>Громкость по расстоянию до слушателя и стереопанорама (простая модель вместо 3D-звука Unity).</summary>
    void UpdateSpatial()
    {
        if (listener == null)
        {
            var l = FindAnyObjectByType<AudioListener>();
            listener = l != null ? l.transform : null;
        }
        float att = 1f, pan = 0f;
        if (listener != null)
        {
            Vector3 d = transform.position - listener.position;
            float dist = d.magnitude;
            att = Mathf.Clamp01(minDistance / Mathf.Max(minDistance, dist));
            if (dist > 0.01f) pan = Mathf.Clamp(Vector3.Dot(listener.right, d / dist), -1f, 1f) * 0.8f;
        }
        gainL = att * Mathf.Sqrt(0.5f * (1f - pan));
        gainR = att * Mathf.Sqrt(0.5f * (1f + pan));
        fxVolume = GameSettings.EffectsVolume;
    }

    // ---------- Синтез (звуковой поток) ----------

    static readonly float[] HumHarm = { 1f, 2f, 3f, 4f, 5f, 7f, 11f };
    static readonly float[] HumAmp = { 1f, 0.6f, 0.35f, 0.15f, 0.2f, 0.1f, 0.05f };

    void OnAudioFilterRead(float[] data, int channels)
    {
        if (!audioReady || audioFlicker == null || sampleRate <= 0) return;
        double t0 = AudioSettings.dspTime;
        float dt = 1f / sampleRate;
        float a = 1f - Mathf.Exp(-2f * Mathf.PI * muffle / sampleRate);
        float aCrack = 1f - Mathf.Exp(-2f * Mathf.PI * Mathf.Max(5000f, muffle * 3.5f) / sampleRate);
        float gl = gainL, gr = gainR, fx = fxVolume;
        int frames = data.Length / channels;

        for (int i = 0; i < frames; i++)
        {
            double t = t0 + (double)i / sampleRate;
            audioFlicker.Step(dt, t);
            // Едва сглаживаем (≈1 мс), чтобы не было цифрового треска, но переходы оставались резкими.
            smooth += (audioFlicker.Level - smooth) * 0.03f;

            // Одиночный треск — в момент вспышки (не чаще раза в 0,1 с, чтобы трески не сливались).
            bool nowLit = smooth > 0.35f;
            if (nowLit != lit)
            {
                if (nowLit)
                {
                    bool loud = audioFlicker.WasOff; // зажглась после долгой паузы
                    audioFlicker.WasOff = false;
                    if (loud || t - lastCrack >= 0.1)
                    {
                        float amp = clickVolume * (0.55f + 0.45f * (float)arng.NextDouble()) * (loud ? 1.5f : 1f);
                        crunch.Trigger(amp, loud);
                        lastCrack = t;
                    }
                }
                lit = nowLit;
            }

            float white = (float)(arng.NextDouble() * 2.0 - 1.0);

            // Гул 100 Гц с обертонами; при мигании «дребезжит» — грубее и громче.
            humPhase += 2.0 * System.Math.PI * 100.0 / sampleRate;
            if (humPhase > 2.0 * System.Math.PI) humPhase -= 2.0 * System.Math.PI;
            float h = 0f;
            for (int k = 0; k < HumHarm.Length; k++) h += HumAmp[k] * Mathf.Sin((float)(humPhase * HumHarm[k]) + k * 0.7f);
            h *= 0.25f;
            bool stutter = audioFlicker.Stuttering;
            if (stutter) h = Mathf.Clamp(h * 1.8f, -0.45f, 0.45f); // перегруженный «дребезг» дросселя
            float humS = h * humVolume * smooth;

            // Редкие одиночные искорки во время мигания (тихо, по одной).
            if (stutter && arng.NextDouble() < 0.00008) crackleEnv = 0.6f + 0.4f * (float)arng.NextDouble();
            crackleEnv *= 0.9f;
            float crackle = white * crackleEnv * crackleVolume;

            float crack = crunch.Sample(white, dt, sampleRate);

            // Приглушение: гул — сильно, треск — мягче, чтобы оставался чётким.
            lp += a * (humS * fx - lp);
            lpCrack += aCrack * ((crack + crackle) * fx - lpCrack);
            float outS = lp + lpCrack;

            if (channels >= 2)
            {
                data[i * channels] += outS * gl;
                data[i * channels + 1] += outS * gr;
            }
            else data[i * channels] += outS * (gl + gr) * 0.7f;

            // История уровня — по ней свет показывает то же, что сейчас слышно.
            if (++sinceHist >= HistoryStep)
            {
                sinceHist = 0;
                int w = histWrite;
                int idx = w % HistorySize;
                histTime[idx] = t;
                histLevel[idx] = smooth;
                histWrite = w + 1;
            }
        }
    }
}
