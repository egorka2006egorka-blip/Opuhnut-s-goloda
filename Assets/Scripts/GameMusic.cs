using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Фоновая музыка в игре: две петли одной длины (Resources/Music/GameAmbient и GameCombat, см. GameMusicGenerator)
/// играют синхронно, громкость плавно переходит от «затишья» к «бою» — во время волн зомби и боя с Деканом.
/// Создаётся сама в каждой сцене с игроком (FirstPersonController). Громкость — ползунок «Музыка» в настройках.
/// Пока говорят персонажи, музыка приглушается; на паузе и в инвентаре продолжает звучать тише.
/// </summary>
public class GameMusic : MonoBehaviour
{
    [Range(0f, 1f)] public float volume = 0.45f;
    [Tooltip("Время перехода между затишьем и боем (с).")]
    public float crossfadeTime = 2.5f;

    AudioSource ambient, combat;
    float mix, master, duck = 1f;
    StoryDirector director;
    PlayerHealth health;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Init()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
        Spawn();
    }

    static void OnSceneLoaded(Scene scene, LoadSceneMode mode) => Spawn();

    static void Spawn()
    {
        if (FindAnyObjectByType<GameMusic>() != null || FindAnyObjectByType<FirstPersonController>() == null) return;
        new GameObject("GameMusic").AddComponent<GameMusic>();
    }

    void Awake()
    {
        ambient = MakeSource(Resources.Load<AudioClip>("Music/GameAmbient"));
        combat = MakeSource(Resources.Load<AudioClip>("Music/GameCombat"));
        if (ambient.clip == null) Debug.LogWarning("GameMusic: нет Resources/Music/GameAmbient — Tools → Game → Regenerate Music.");
    }

    AudioSource MakeSource(AudioClip clip)
    {
        var s = gameObject.AddComponent<AudioSource>();
        s.clip = clip;
        s.loop = true;
        s.playOnAwake = false;
        s.spatialBlend = 0f;
        s.volume = 0f;
        s.priority = 0;
        s.ignoreListenerPause = true; // инвентарь и магазин ставят звук на паузу — музыка звучит дальше
        return s;
    }

    void Start()
    {
        director = StoryDirector.Instance;
        var fpc = FindAnyObjectByType<FirstPersonController>();
        if (fpc != null) health = fpc.GetComponent<PlayerHealth>();
        // Обе петли стартуют в один и тот же момент DSP — бой ложится точно поверх затишья.
        double at = AudioSettings.dspTime + 0.2;
        if (ambient.clip != null) ambient.PlayScheduled(at);
        if (combat.clip != null) combat.PlayScheduled(at);
    }

    bool InCombat()
    {
        if (director == null) director = StoryDirector.Instance;
        if (director != null)
        {
            switch (director.CurrentStage)
            {
                case StoryDirector.Stage.Wave1:
                case StoryDirector.Stage.Wave2:
                case StoryDirector.Stage.Wave3:
                case StoryDirector.Stage.BossFight:
                    return true;
            }
            return false;
        }
        return Zombie.AliveCount > 0; // сцена без сюжета: бой, пока есть зомби
    }

    float TargetMaster()
    {
        if (director == null) director = StoryDirector.Instance;
        if (director != null)
        {
            var s = director.CurrentStage;
            if (s == StoryDirector.Stage.Intro) return 0f; // пробуждение — тишина и гул холодильника
            if (s == StoryDirector.Stage.Finale) return 0.5f;
        }
        if (health != null && health.IsDead) return 0.35f;
        if (PauseMenu.IsPaused || InventoryScreen.IsOpen || Shopkeeper.IsShopOpen) return 0.6f;
        return 1f;
    }

    void Update()
    {
        float dt = Time.unscaledDeltaTime;
        mix = Mathf.MoveTowards(mix, InCombat() ? 1f : 0f, dt / Mathf.Max(0.1f, crossfadeTime));
        master = Mathf.MoveTowards(master, TargetMaster(), dt / 3f);
        duck = Mathf.MoveTowards(duck, DialogueSystem.IsPlaying ? 0.55f : 1f, dt * 2f);
        float v = volume * master * duck * GameSettings.MusicVolume;
        // Равномощный переход: в середине не проваливается по громкости.
        ambient.volume = v * Mathf.Cos(mix * Mathf.PI / 2f);
        combat.volume = v * Mathf.Sin(mix * Mathf.PI / 2f);
    }
}
