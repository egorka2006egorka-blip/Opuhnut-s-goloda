using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>Реплика из Assets/Resources/Story/lines.json. Озвучка — Assets/Resources/Voice/&lt;id&gt;.wav (Tools/GenerateVoices.ps1).</summary>
[Serializable]
public class DialogueLine
{
    public string id, speaker, text, say;
    public bool radio;
}

/// <summary>Все реплики сюжета (по id) и персонажи.</summary>
public static class StoryLines
{
    [Serializable] class LineFile { public DialogueLine[] lines; }

    static Dictionary<string, DialogueLine> map;

    public static DialogueLine Get(string id)
    {
        if (map == null)
        {
            map = new Dictionary<string, DialogueLine>();
            var asset = Resources.Load<TextAsset>("Story/lines");
            if (asset != null)
            {
                var file = JsonUtility.FromJson<LineFile>(asset.text);
                if (file?.lines != null) foreach (var l in file.lines) if (l != null && !string.IsNullOrEmpty(l.id)) map[l.id] = l;
            }
            else Debug.LogWarning("StoryLines: нет Assets/Resources/Story/lines.json.");
        }
        return id != null && map.TryGetValue(id, out var line) ? line : null;
    }

    public static string SpeakerName(string speaker) => speaker switch
    {
        "GALYA" => "Галя",
        "STUDENT" => "Студент",
        "SHAPIK" => "Шапик",
        "DEAN" => "Декан",
        "ZOMBIE" or "RUNNER" => "Зомби",
        _ => speaker ?? "",
    };

    public static Color SpeakerColor(string speaker) => speaker switch
    {
        "GALYA" => new Color(1f, 0.72f, 0.45f),
        "STUDENT" => new Color(0.7f, 0.85f, 1f),
        "SHAPIK" => new Color(0.55f, 1f, 0.55f),
        "DEAN" => new Color(1f, 0.35f, 0.3f),
        _ => new Color(0.75f, 0.75f, 0.7f),
    };
}

/// <summary>
/// Диалоги: очередь реплик с субтитрами и озвучкой. Шапик по рации звучит через «радио»-фильтры с треском,
/// у Декана — эхо и хрип, зомби бормочут из своей точки в пространстве.
/// Создаётся сам при первом обращении. Время — реальное (реплики идут и в магазине), на паузе всё замирает.
/// </summary>
public class DialogueSystem : MonoBehaviour
{
    static DialogueSystem inst;

    static DialogueSystem I
    {
        get
        {
            if (inst == null)
            {
                var go = new GameObject("DialogueSystem");
                inst = go.AddComponent<DialogueSystem>();
            }
            return inst;
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { inst = null; AllowSkip = false; }

    class Entry { public DialogueLine line; public Action done; public float hold; }

    readonly Queue<Entry> queue = new Queue<Entry>();
    Entry current;
    float clock, length;
    AudioSource voice, radio, dean;
    bool paused;

    // Короткая надпись без очереди (бормотание зомби, ответ Гали).
    string flashSpeaker, flashText;
    float flashUntil;

    /// <summary>Пропуск реплики (Пробел / Enter) — во время катсцен.</summary>
    public static bool AllowSkip { get; set; }
    public static bool IsPlaying => inst != null && (inst.current != null || inst.queue.Count > 0);
    public static string CurrentId => inst != null && inst.current != null ? inst.current.line.id : null;

    void Awake()
    {
        voice = MakeSource("Voice");
        radio = MakeSource("Radio");
        radio.gameObject.AddComponent<AudioHighPassFilter>().cutoffFrequency = 450f;
        radio.gameObject.AddComponent<AudioLowPassFilter>().cutoffFrequency = 3200f;
        radio.gameObject.AddComponent<AudioDistortionFilter>().distortionLevel = 0.45f;
        dean = MakeSource("Dean");
        var echo = dean.gameObject.AddComponent<AudioEchoFilter>();
        echo.delay = 90f; echo.decayRatio = 0.35f; echo.wetMix = 0.5f;
        dean.gameObject.AddComponent<AudioDistortionFilter>().distortionLevel = 0.3f;
        dean.gameObject.AddComponent<AudioLowPassFilter>().cutoffFrequency = 4500f;
    }

    AudioSource MakeSource(string name)
    {
        var go = new GameObject(name);
        go.transform.SetParent(transform, false);
        var s = go.AddComponent<AudioSource>();
        s.playOnAwake = false;
        s.spatialBlend = 0f;
        s.ignoreListenerPause = true; // реплики идут и в магазине (там игра на паузе)
        return s;
    }

    // ---------- API ----------

    /// <summary>Реплики по очереди; onDone — когда прозвучит последняя.</summary>
    public static void Play(Action onDone, params string[] ids)
    {
        var sys = I;
        int added = 0;
        for (int i = 0; i < ids.Length; i++)
        {
            var line = StoryLines.Get(ids[i]);
            if (line == null) { Debug.LogWarning("DialogueSystem: нет реплики " + ids[i]); continue; }
            sys.queue.Enqueue(new Entry { line = line, done = i == ids.Length - 1 ? onDone : null });
            added++;
        }
        if (added == 0) onDone?.Invoke();
    }

    public static void Play(params string[] ids) => Play(null, ids);

    /// <summary>Реплика, только если сейчас никто не говорит (реакции, подсказки).</summary>
    public static void Say(string id)
    {
        if (!IsPlaying) Play(id);
    }

    /// <summary>Надпись без озвучки (короткий ответ).</summary>
    public static void ShowText(string speaker, string text, float seconds = 2.5f)
    {
        var sys = I;
        sys.flashSpeaker = speaker;
        sys.flashText = text;
        sys.flashUntil = Time.unscaledTime + seconds;
    }

    /// <summary>Бормотание (зомби) из точки в мире; надпись — если близко и нет диалога.</summary>
    public static void Bark(string id, Vector3 position, float pitch = 1f)
    {
        var line = StoryLines.Get(id);
        var clip = Resources.Load<AudioClip>("Voice/" + id);
        if (line == null) return;
        if (clip != null)
        {
            var go = new GameObject("Bark_" + id);
            go.transform.position = position;
            var s = go.AddComponent<AudioSource>();
            s.spatialBlend = 1f;
            s.minDistance = 2f;
            s.maxDistance = 22f;
            s.rolloffMode = AudioRolloffMode.Linear;
            s.pitch = pitch;
            s.volume = GameSettings.VoiceVolume * 0.9f;
            var echo = go.AddComponent<AudioEchoFilter>();
            echo.delay = 70f; echo.decayRatio = 0.3f; echo.wetMix = 0.4f;
            s.clip = clip;
            s.Play();
            Destroy(go, clip.length / Mathf.Max(0.1f, pitch) + 0.6f);
        }
        var cam = Camera.main;
        if (!IsPlaying && cam != null && (cam.transform.position - position).sqrMagnitude < 81f)
            ShowText(line.speaker, line.text, 1.8f);
    }

    /// <summary>Оборвать всё (смена сцены, титры).</summary>
    public static void StopAll()
    {
        if (inst == null) return;
        inst.queue.Clear();
        inst.current = null;
        inst.voice.Stop(); inst.radio.Stop(); inst.dean.Stop();
    }

    // ---------- Проигрывание ----------

    void Update()
    {
        bool pause = PauseMenu.IsPaused;
        if (pause != paused)
        {
            paused = pause;
            foreach (var s in new[] { voice, radio, dean })
                if (pause) s.Pause(); else s.UnPause();
        }
        if (paused) return;

        float dt = Time.unscaledDeltaTime;
        if (current == null)
        {
            if (queue.Count == 0) return;
            Begin(queue.Dequeue());
        }
        clock += dt;
        bool skip = AllowSkip && clock > 0.3f && UnityEngine.InputSystem.Keyboard.current != null &&
                    (UnityEngine.InputSystem.Keyboard.current.spaceKey.wasPressedThisFrame || UnityEngine.InputSystem.Keyboard.current.enterKey.wasPressedThisFrame);
        if (clock >= length || skip) Finish();
    }

    void Begin(Entry e)
    {
        current = e;
        clock = 0f;
        var line = e.line;
        var clip = Resources.Load<AudioClip>("Voice/" + line.id);
        AudioSource src = line.radio ? radio : line.speaker == "DEAN" ? dean : voice;
        src.pitch = line.speaker == "DEAN" ? 0.92f : 1f;
        src.volume = GameSettings.VoiceVolume * (line.radio ? 1.3f : 1f);
        if (line.radio) src.PlayOneShot(RadioCrackle(), 0.5f * GameSettings.VoiceVolume);
        float voiceLen = 0f;
        if (clip != null)
        {
            src.clip = clip;
            src.PlayDelayed(line.radio ? 0.25f : 0f);
            voiceLen = clip.length / src.pitch + (line.radio ? 0.25f : 0f);
        }
        // Без озвучки — по длине текста.
        length = Mathf.Max(voiceLen + 0.35f, clip == null ? 1.2f + line.text.Length * 0.06f : 0f);
    }

    void Finish()
    {
        var e = current;
        current = null;
        voice.Stop(); radio.Stop(); dean.Stop();
        if (e.line.radio) radio.PlayOneShot(RadioCrackle(), 0.35f * GameSettings.VoiceVolume);
        e.done?.Invoke();
    }

    static AudioClip crackle;

    /// <summary>Треск рации: шум с прерываниями.</summary>
    static AudioClip RadioCrackle()
    {
        if (crackle != null) return crackle;
        const int rate = 44100;
        int n = rate / 4;
        var data = new float[n];
        var rnd = new System.Random(7);
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)rate;
            float gate = Mathf.PerlinNoise(t * 60f, 0.5f) > 0.45f ? 1f : 0.2f;
            data[i] = ((float)rnd.NextDouble() * 2f - 1f) * gate * Mathf.Clamp01((0.25f - t) / 0.08f) * 0.6f;
        }
        crackle = AudioClip.Create("RadioCrackle", n, 1, rate, false);
        crackle.SetData(data, 0);
        return crackle;
    }

    // ---------- Субтитры ----------

    void OnGUI()
    {
        if (SaveSystem.HideHud || PauseMenu.IsPaused) return;
        GUI.depth = -60; // поверх магазина и инвентаря
        string speaker = null, text = null;
        bool radioLine = false;
        if (current != null) { speaker = current.line.speaker; text = current.line.text; radioLine = current.line.radio; }
        else if (flashText != null && Time.unscaledTime < flashUntil) { speaker = flashSpeaker; text = flashText; }
        if (text == null) return;

        float w = Mathf.Min(Screen.width - 80f, Screen.height * 1.3f);
        var style = new GUIStyle(GUI.skin.label)
        {
            fontSize = Mathf.Max(16, Screen.height / 38),
            wordWrap = true,
            alignment = TextAnchor.UpperCenter,
            richText = true,
        };
        Color c = StoryLines.SpeakerColor(speaker);
        string name = StoryLines.SpeakerName(speaker) + (radioLine ? " (рация)" : "");
        string body = "<b><color=#" + ColorUtility.ToHtmlStringRGB(c) + ">" + name + ":</color></b> " + text;
        float h = style.CalcHeight(new GUIContent(body), w);
        var r = new Rect((Screen.width - w) / 2f, Screen.height * 0.8f - h, w, h);
        var bg = new Rect(r.x - 14f, r.y - 8f, r.width + 28f, r.height + 16f);
        GUI.color = new Color(0f, 0f, 0f, 0.55f);
        GUI.DrawTexture(bg, Texture2D.whiteTexture);
        GUI.color = Color.white;
        GUI.Label(r, body, style);
        if (AllowSkip && current != null)
        {
            var small = new GUIStyle(style) { fontSize = Mathf.Max(11, Screen.height / 70), alignment = TextAnchor.UpperRight };
            GUI.color = new Color(1f, 1f, 1f, 0.5f);
            GUI.Label(new Rect(bg.x, bg.yMax + 2f, bg.width, 24f), "Пробел — дальше", small);
            GUI.color = Color.white;
        }
    }
}
