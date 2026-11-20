using UnityEngine;

/// <summary>
/// Пасхалка: после нажатия секретной кнопки (SecretDanceButton) все живые зомби бросают погоню и танцуют
/// связку «Я тебя могну» (снята по видео, 14,4 с). Позы — клип Dance или ZombieDancer.
/// Музыка — песня Assets/Resources/Music/DanceTrack: играет целиком, связка всё это время повторяется по кругу.
/// Без песни — синтезированный бит, танец длится одну связку.
/// Часы танца идут по Time.deltaTime — на паузе зомби и музыка замирают.
/// </summary>
public class ZombieDance : MonoBehaviour
{
    public const float Bpm = 120f;
    const int MusicBeats = 32; // длина цикла синтезированного бита (8 тактов)
    const float FadeIn = 0.35f, FadeOut = 0.6f;

    static ZombieDance instance;
    float clock = -1f;
    AudioSource music;
    bool song; // играет песня (а не синтезированный бит)

    /// <summary>Длина одной связки (видео 14,4 с) — танец повторяет её по кругу.</summary>
    public static float LoopLength => ZombieDancer.TotalBeats * 60f / Bpm;

    /// <summary>Сколько длится танец: вся песня или одна связка под синтезированный бит.</summary>
    public static float Duration => instance != null && instance.song && instance.music.clip != null
        ? Mathf.Max(LoopLength, instance.music.clip.length - FadeOut) : LoopLength;

    /// <summary>Зомби стоят и танцуют (включая плавный вход и выход).</summary>
    public static bool Active => instance != null && instance.clock >= 0f && instance.clock < Duration + FadeOut;

    /// <summary>Вес танцевальной позы поверх обычной анимации: 0..1.</summary>
    public static float Weight
    {
        get
        {
            if (!Active) return 0f;
            float t = instance.clock;
            return Mathf.SmoothStep(0f, 1f, Mathf.Min(t / FadeIn, (Duration + FadeOut - t) / FadeOut));
        }
    }

    /// <summary>Секунды от начала танца (по ним зомби, появившийся посреди танца, попадает в такт).</summary>
    public static float Clock => instance != null ? Mathf.Max(0f, instance.clock) : 0f;

    /// <summary>Номер доли внутри текущего повтора связки (0 … TotalBeats).</summary>
    public static float Beat => instance != null ? Mathf.Repeat(Mathf.Max(0f, instance.clock), LoopLength) * Bpm / 60f : 0f;

    public static void Begin()
    {
        if (Active) return; // уже танцуют — кнопка ждёт конца
        if (instance == null)
        {
            var go = new GameObject("ZombieDance");
            instance = go.AddComponent<ZombieDance>();
            var src = go.AddComponent<AudioSource>();
            src.playOnAwake = false;
            src.spatialBlend = 0f;
            src.volume = 0.8f;
            // Песня «Я тебя могну» — Assets/Resources/Music/DanceTrack, играет один раз целиком; без неё — бит по кругу.
            var track = Resources.Load<AudioClip>("Music/DanceTrack");
            instance.song = track != null;
            src.loop = track == null;
            src.clip = track != null ? track : DanceBeat.Create(Bpm, MusicBeats);
            go.AddComponent<SoundCategory>().kind = SoundKind.Music;
            instance.music = src;
        }
        instance.clock = 0f;
        instance.music.time = 0f;
        instance.music.volume = 0.8f * GameSettings.MusicVolume; // после прошлого затухания
        instance.music.Play();
    }

    void OnDestroy() { if (instance == this) instance = null; }

    void Update()
    {
        if (clock < 0f) return;
        clock += Time.deltaTime;
        bool paused = Time.timeScale <= 0f;
        if (clock >= Duration)
        {
            // Музыка затихает вместе с выходом из танца.
            float k = Mathf.Clamp01(1f - (clock - Duration) / FadeOut);
            music.volume = 0.8f * k * GameSettings.MusicVolume;
            if (k <= 0f) { music.Stop(); clock = -1f; }
        }
        if (paused && music.isPlaying) music.Pause();
        else if (!paused && !music.isPlaying && clock >= 0f) music.UnPause();
    }
}
