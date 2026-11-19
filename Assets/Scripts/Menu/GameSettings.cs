using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Настройки игры: громкость (общая, эффекты, музыка, голоса), чувствительность мыши,
/// разрешение, режим экрана, качество графики, вертикальная синхронизация, экранный фильтр.
/// Хранятся в PlayerPrefs и применяются при запуске. Клавиши — в Controls.
/// </summary>
public static class GameSettings
{
    const string MasterKey = "Settings.Volume";
    const string EffectsKey = "Settings.Volume.Effects";
    const string MusicKey = "Settings.Volume.Music";
    const string VoiceKey = "Settings.Volume.Voice";
    const string SensitivityKey = "Settings.MouseSensitivity";
    const string QualityKey = "Settings.QualityLevel"; // новый ключ: старый хранил номер из прежних двух уровней
    const string VSyncKey = "Settings.VSync";
    const string ResWKey = "Settings.ResolutionWidth", ResHKey = "Settings.ResolutionHeight";
    const string ModeKey = "Settings.ScreenMode";
    const string FilterKey = "Settings.Filter", FilterIntensityKey = "Settings.FilterIntensity";

    public const float MinSensitivity = 0.2f, MaxSensitivity = 3f;

    /// <summary>Вызывается при изменении любой громкости (для SoundCategory).</summary>
    public static event System.Action VolumeChanged;

    public static float MasterVolume { get; private set; } = 1f;
    public static float EffectsVolume { get; private set; } = 1f;
    public static float MusicVolume { get; private set; } = 1f;
    public static float VoiceVolume { get; private set; } = 1f;
    /// <summary>Множитель чувствительности мыши (1 — по умолчанию).</summary>
    public static float MouseSensitivity { get; private set; } = 1f;
    public static bool VSync { get; private set; } = true;
    public static Vector2Int Resolution { get; private set; }
    public static FullScreenMode ScreenMode { get; private set; }
    public static ScreenFilterKind Filter { get; private set; } = ScreenFilterKind.None;
    /// <summary>Сила фильтра 0..1.</summary>
    public static float FilterIntensity { get; private set; } = 1f;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Load()
    {
        MasterVolume = Mathf.Clamp01(PlayerPrefs.GetFloat(MasterKey, 1f));
        EffectsVolume = Mathf.Clamp01(PlayerPrefs.GetFloat(EffectsKey, 1f));
        MusicVolume = Mathf.Clamp01(PlayerPrefs.GetFloat(MusicKey, 1f));
        VoiceVolume = Mathf.Clamp01(PlayerPrefs.GetFloat(VoiceKey, 1f));
        MouseSensitivity = Mathf.Clamp(PlayerPrefs.GetFloat(SensitivityKey, 1f), MinSensitivity, MaxSensitivity);
        VSync = PlayerPrefs.GetInt(VSyncKey, 1) == 1;
        AudioListener.volume = MasterVolume;
        if (PlayerPrefs.HasKey(QualityKey))
        {
            int q = PlayerPrefs.GetInt(QualityKey);
            if (q >= 0 && q < QualitySettings.names.Length && q != QualitySettings.GetQualityLevel())
                QualitySettings.SetQualityLevel(q, true);
        }
        QualitySettings.vSyncCount = VSync ? 1 : 0;

        int f = PlayerPrefs.GetInt(FilterKey, 0);
        Filter = System.Enum.IsDefined(typeof(ScreenFilterKind), f) ? (ScreenFilterKind)f : ScreenFilterKind.None;
        FilterIntensity = Mathf.Clamp01(PlayerPrefs.GetFloat(FilterIntensityKey, 1f));

        Resolution = new Vector2Int(PlayerPrefs.GetInt(ResWKey, Screen.width), PlayerPrefs.GetInt(ResHKey, Screen.height));
        ScreenMode = (FullScreenMode)PlayerPrefs.GetInt(ModeKey, (int)Screen.fullScreenMode);
#if !UNITY_EDITOR
        if (PlayerPrefs.HasKey(ResWKey) || PlayerPrefs.HasKey(ModeKey))
            Screen.SetResolution(Resolution.x, Resolution.y, ScreenMode);
#endif
    }

    // ---------- Звук ----------

    public static float CategoryVolume(SoundKind kind)
    {
        switch (kind)
        {
            case SoundKind.Music: return MusicVolume;
            case SoundKind.Voice: return VoiceVolume;
            default: return EffectsVolume;
        }
    }

    public static void SetMasterVolume(float v)
    {
        MasterVolume = Mathf.Clamp01(v);
        AudioListener.volume = MasterVolume;
        PlayerPrefs.SetFloat(MasterKey, MasterVolume);
        VolumeChanged?.Invoke();
    }

    public static void SetCategoryVolume(SoundKind kind, float v)
    {
        v = Mathf.Clamp01(v);
        switch (kind)
        {
            case SoundKind.Music: MusicVolume = v; PlayerPrefs.SetFloat(MusicKey, v); break;
            case SoundKind.Voice: VoiceVolume = v; PlayerPrefs.SetFloat(VoiceKey, v); break;
            default: EffectsVolume = v; PlayerPrefs.SetFloat(EffectsKey, v); break;
        }
        VolumeChanged?.Invoke();
    }

    // ---------- Управление ----------

    public static void SetMouseSensitivity(float v)
    {
        MouseSensitivity = Mathf.Clamp(v, MinSensitivity, MaxSensitivity);
        PlayerPrefs.SetFloat(SensitivityKey, MouseSensitivity);
    }

    // ---------- Графика ----------

    public static void SetVSync(bool on)
    {
        VSync = on;
        QualitySettings.vSyncCount = on ? 1 : 0;
        PlayerPrefs.SetInt(VSyncKey, on ? 1 : 0);
    }

    public static int Quality => QualitySettings.GetQualityLevel();

    public static void SetQuality(int level)
    {
        int n = QualitySettings.names.Length;
        if (n == 0) return;
        level = ((level % n) + n) % n;
        QualitySettings.SetQualityLevel(level, true);
        // vSync хранится в уровне качества — возвращаем выбранное игроком.
        QualitySettings.vSyncCount = VSync ? 1 : 0;
        PlayerPrefs.SetInt(QualityKey, level);
    }

    static readonly Vector2Int[] CommonResolutions =
    {
        new Vector2Int(1280, 720), new Vector2Int(1366, 768), new Vector2Int(1600, 900), new Vector2Int(1920, 1080),
        new Vector2Int(2560, 1080), new Vector2Int(2560, 1440), new Vector2Int(3440, 1440), new Vector2Int(3840, 2160),
    };
    static List<Vector2Int> resolutions;

    /// <summary>Доступные разрешения (поддерживаемые монитором + стандартные, не больше размера монитора).</summary>
    public static List<Vector2Int> Resolutions
    {
        get
        {
            if (resolutions != null) return resolutions;
            int maxW = Display.main.systemWidth, maxH = Display.main.systemHeight;
            if (maxW <= 0 || maxH <= 0) { maxW = Screen.currentResolution.width; maxH = Screen.currentResolution.height; }
            var set = new HashSet<Vector2Int>();
            foreach (var r in Screen.resolutions)
                if (r.width >= 800 && r.height >= 600) set.Add(new Vector2Int(r.width, r.height));
            foreach (var r in CommonResolutions)
                if (r.x <= maxW && r.y <= maxH) set.Add(r);
            set.Add(new Vector2Int(maxW, maxH));
            resolutions = new List<Vector2Int>(set);
            resolutions.Sort((a, b) => a.x != b.x ? a.x.CompareTo(b.x) : a.y.CompareTo(b.y));
            return resolutions;
        }
    }

    /// <summary>Индекс выбранного разрешения в списке (или ближайшего).</summary>
    public static int ResolutionIndex
    {
        get
        {
            var list = Resolutions;
            int best = 0;
            long bestDiff = long.MaxValue;
            for (int i = 0; i < list.Count; i++)
            {
                long d = System.Math.Abs((long)list[i].x * list[i].y - (long)Resolution.x * Resolution.y)
                         + System.Math.Abs(list[i].x - Resolution.x);
                if (d < bestDiff) { bestDiff = d; best = i; }
            }
            return best;
        }
    }

    public static void SetResolution(Vector2Int r)
    {
        Resolution = r;
        PlayerPrefs.SetInt(ResWKey, r.x);
        PlayerPrefs.SetInt(ResHKey, r.y);
        Screen.SetResolution(r.x, r.y, ScreenMode);
    }

    public static readonly FullScreenMode[] ScreenModes =
        { FullScreenMode.ExclusiveFullScreen, FullScreenMode.FullScreenWindow, FullScreenMode.Windowed };

    public static string ScreenModeName(FullScreenMode m)
    {
        switch (m)
        {
            case FullScreenMode.ExclusiveFullScreen: return "Полный экран";
            case FullScreenMode.FullScreenWindow: return "Окно без рамки";
            case FullScreenMode.MaximizedWindow: return "Развёрнутое окно";
            default: return "В окне";
        }
    }

    public static void SetScreenMode(FullScreenMode m)
    {
        ScreenMode = m;
        PlayerPrefs.SetInt(ModeKey, (int)m);
        Screen.SetResolution(Resolution.x, Resolution.y, m);
    }

    public static readonly ScreenFilterKind[] Filters = (ScreenFilterKind[])System.Enum.GetValues(typeof(ScreenFilterKind));

    public static void SetFilter(ScreenFilterKind kind)
    {
        Filter = kind;
        PlayerPrefs.SetInt(FilterKey, (int)kind);
    }

    public static void SetFilterIntensity(float v)
    {
        FilterIntensity = Mathf.Clamp01(v);
        PlayerPrefs.SetFloat(FilterIntensityKey, FilterIntensity);
    }

    public static string FilterName(ScreenFilterKind kind)
    {
        switch (kind)
        {
            case ScreenFilterKind.VHS: return "VHS-кассета";
            case ScreenFilterKind.CRT: return "ЭЛТ-монитор";
            case ScreenFilterKind.Noir: return "Нуар";
            case ScreenFilterKind.Retro: return "Ретро-консоль";
            default: return "Нет";
        }
    }

    /// <summary>Название уровня качества по-русски (для стандартных имён Unity/URP).</summary>
    public static string QualityName(int level)
    {
        var names = QualitySettings.names;
        if (level < 0 || level >= names.Length) return "—";
        switch (names[level])
        {
            case "Very Low": return "Очень низкое";
            case "Low": return "Низкое";
            case "Medium": return "Среднее";
            case "High": return "Высокое";
            case "Very High": return "Очень высокое";
            case "Ultra": return "Ультра";
            case "Performant": return "Производительность";
            case "Balanced": return "Баланс";
            case "High Fidelity": return "Максимальное";
            default: return names[level];
        }
    }

    public static void Save() => PlayerPrefs.Save();
}
