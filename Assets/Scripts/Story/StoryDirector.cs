using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

/// <summary>
/// Сюжет «Пересдача» (по сценарию Peresdacha_scenarii.docx, под локации игры: столовая → кухня → подвал):
///  1. Пробуждение (катсцена) → разговор с Галей у раздачи (катсцена) → пистолет у входа → рация, связь с Шапиком.
///  2. Оборона столовой: три волны («Зачётные», «Бегуны-опоздуны», «Старосты»), между волнами — магазин Гали.
///  3. Галя открывает кухню: спуск в подвал. Декан (босс) — катсцена при пробуждении, реплики в бою, после смерти — лом.
///  4. Лом — в руки, им сбить замок клетки в зале Декана → Шапик за столом с ноутбуком → развязка, рукопожатие, титры.
/// Реплики и озвучка — DialogueSystem. Ставит CanteenSceneBuilder (ссылки на Галю, пистолет, рацию, двери, Шапика).
/// </summary>
public class StoryDirector : MonoBehaviour
{
    public enum Stage
    {
        Intro, MeetGalya, GetPistol, GetRadio, Wave1, Break1, Wave2, Break2, Wave3,
        GoBasement, BossFight, GetCrowbar, OpenDoor, FindShapik, Finale, Credits
    }

    public static StoryDirector Instance { get; private set; }
    /// <summary>Этап из загруженного сохранения (применяется в Start); -1 — новая игра.</summary>
    public static int PendingStage = -1;
    /// <summary>Скрыть интерфейс игры (катсцены и титры).</summary>
    public static bool HideHud => Instance != null && Instance.cinematic > 0.5f;
    /// <summary>Курсор нужен (титры) — клик не должен его захватывать.</summary>
    public static bool CursorNeeded => false;
    /// <summary>Магазин Гали открыт после первой волны (без сюжета — всегда).</summary>
    public static bool ShopAllowed => Instance == null || Instance.stage >= Stage.Break1;
    /// <summary>Что записать в сохранение: волна — с её начала, бой с боссом — с подхода к подвалу, финал — с поиска Шапика.</summary>
    public static int SaveStage => Instance == null ? 0 : (int)(Instance.stage switch
    {
        Stage.Intro => Stage.MeetGalya,
        Stage.BossFight => Stage.GoBasement,
        Stage.Finale or Stage.Credits => Stage.FindShapik,
        var s => s,
    });

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { Instance = null; PendingStage = -1; }

    [Header("Ссылки (ставит сборщик сцены)")]
    public Shopkeeper galya;
    public WeaponPickup pistol;
    public RadioPickup radio;
    public SwingGate kitchenDoor;
    public ShapikCage shapikCage;
    public ShapikNpc shapik;
    [Tooltip("Верх лестницы в подвал (на кухне) и вход в подвал — для метки задания.")]
    public Vector3 stairsTop, basementEntrance;
    [Tooltip("Ниже этой высоты игрок уже на лестнице в подвал.")]
    public float stairsY = -1.5f;

    [Header("Волны")]
    public float breakTime = 30f;

    Stage stage = Stage.Intro;
    public Stage CurrentStage => stage;
    public bool RadioAvailable => stage >= Stage.GetRadio;

    FirstPersonController fpc;
    PlayerWeaponHandler weapons;
    Inventory inventory;
    Transform cam;
    ZombieSpawner spawner;
    ZombieBoss boss;
    Vector3 bossHome;

    string objective, hint;
    float hintUntil;
    float fade, cinematic;
    float stageClock;
    bool stairsSaid, stunSaid, waitingHandshake, radioDone;
    /// <summary>Идёт катсцена — этапы не переключаются сами.</summary>
    bool busy;
    System.Func<Vector3> lookAt;

    // Волна.
    int waveTotal, waveSpawned;
    readonly List<Zombie> waveAlive = new List<Zombie>();
    float waveTimer;
    bool waveEnding;

    // Титры.
    float creditsClock = -1f;
    bool postScene, theEnd;

    struct WaveDef
    {
        public string title; public int count, maxAlive; public float health, speed, interval, scale; public Vector2Int coins; public string[] barks;
    }

    static readonly WaveDef[] Waves =
    {
        new WaveDef { title = "«Зачётные»", count = 7, maxAlive = 5, health = 100f, speed = 1f, interval = 2.5f, scale = 1f, coins = new Vector2Int(4, 9), barks = new[] { "zombie_1", "zombie_2", "zombie_3" } },
        new WaveDef { title = "«Бегуны-опоздуны»", count = 9, maxAlive = 6, health = 55f, speed = 2.2f, interval = 1.5f, scale = 0.95f, coins = new Vector2Int(3, 7), barks = new[] { "runner_1", "runner_2" } },
        new WaveDef { title = "«Старосты»", count = 6, maxAlive = 4, health = 260f, speed = 0.8f, interval = 3f, scale = 1.15f, coins = new Vector2Int(9, 16), barks = new[] { "starosta_1", "starosta_2" } },
    };

    // ---------- Запуск ----------

    void Awake()
    {
        Instance = this;
        spawner = FindAnyObjectByType<ZombieSpawner>();
        if (spawner != null) spawner.autoSpawn = false; // зомби выпускает сюжет
    }

    void OnEnable()
    {
        ZombieBoss.Woke += OnBossWoke;
        ZombieBoss.Summoned += OnBossSummoned;
        ZombieBoss.EnragedEvent += OnBossEnraged;
        ZombieBoss.Slammed += OnBossSlam;
        ZombieBoss.StunnedEvent += OnBossStunned;
        ZombieBoss.Died += OnBossDied;
    }

    void OnDisable()
    {
        ZombieBoss.Woke -= OnBossWoke;
        ZombieBoss.Summoned -= OnBossSummoned;
        ZombieBoss.EnragedEvent -= OnBossEnraged;
        ZombieBoss.Slammed -= OnBossSlam;
        ZombieBoss.StunnedEvent -= OnBossStunned;
        ZombieBoss.Died -= OnBossDied;
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
        FirstPersonController.InputLocked = false;
        DialogueSystem.AllowSkip = false;
    }

    void Start()
    {
        fpc = FindAnyObjectByType<FirstPersonController>();
        if (fpc != null)
        {
            weapons = fpc.GetComponent<PlayerWeaponHandler>();
            inventory = fpc.GetComponent<Inventory>();
            var c = fpc.GetComponentInChildren<Camera>();
            cam = c != null ? c.transform : fpc.transform;
        }
        boss = ZombieBoss.All.Count > 0 ? ZombieBoss.All[0] : null;
        if (boss != null) bossHome = boss.transform.position;
        if (kitchenDoor != null) kitchenDoor.lockedMessage = "Куда собрался? Сначала прилавок отстоим!";
        if (shapik != null && cam != null) shapik.lookTarget = cam;

        if (PendingStage >= 0)
        {
            var s = (Stage)Mathf.Clamp(PendingStage, 0, (int)Stage.FindShapik);
            PendingStage = -1;
            Restore(s);
        }
        else StartCoroutine(Intro());
    }

    /// <summary>Загрузка сохранения: сразу к этапу s, без пройденных катсцен.</summary>
    void Restore(Stage s)
    {
        if (s > Stage.GetRadio && radio != null) Destroy(radio.gameObject);
        if (s >= Stage.FindShapik && shapikCage != null) shapikCage.OpenInstant();
        // Лом создаётся из кода (выпадает из Декана) — в сохранённой сцене его нет: кладём заново.
        if ((s == Stage.GetCrowbar || s == Stage.OpenDoor) && CrowbarWeapon.Find() == null)
            CrowbarWeapon.Create(s == Stage.OpenDoor && fpc != null ? fpc.transform.position + fpc.transform.forward * 1.2f : bossHome, Random.Range(0f, 360f));
        SetStage(s);
        switch (s)
        {
            case Stage.Wave1: StartWave(0); break;
            case Stage.Wave2: StartWave(1); break;
            case Stage.Wave3: StartWave(2); break;
        }
    }

    void SetStage(Stage s)
    {
        stage = s;
        stageClock = 0f;
        if (kitchenDoor != null) kitchenDoor.Locked = s < Stage.GoBasement;
        objective = s switch
        {
            Stage.Intro => null,
            Stage.MeetGalya => "Подойди к раздаче",
            Stage.GetPistol => "Подбери пистолет дяди Коли у входа",
            Stage.GetRadio => "Возьми рацию на кассе",
            Stage.Wave1 or Stage.Wave2 or Stage.Wave3 => "Защити прилавок Гали",
            Stage.Break1 or Stage.Break2 => "Собери монеты и закупись у Гали",
            Stage.GoBasement => "Спустись в подвал: кухня за раздачей → лестница вниз",
            Stage.BossFight => "Победи Декана и забери лом",
            Stage.GetCrowbar => "Возьми лом Декана в руки",
            Stage.OpenDoor => "Сбей ломом замок с клетки Шапика",
            Stage.FindShapik => "Выпусти Шапика",
            _ => null,
        };
    }

    // ---------- Ход сюжета ----------

    void Update()
    {
        float dt = Time.deltaTime;
        stageClock += dt;
        if (cinematic > 0f || FirstPersonController.InputLocked)
        {
            if (lookAt != null && fpc != null) fpc.LookAt(lookAt(), 4f, dt);
        }
        if (waitingHandshake && Controls.WasPressed(GameAction.Interact)) waitingHandshake = false;
        if (creditsClock >= 0f) UpdateCredits(dt);
        if (fpc == null || busy) return;
        Vector3 p = fpc.transform.position;

        switch (stage)
        {
            case Stage.MeetGalya:
                if (galya != null && Vector3.Distance(p, galya.transform.position) < 4.3f && !DialogueSystem.IsPlaying)
                    StartCoroutine(GalyaScene());
                else if (galya == null) SetStage(Stage.GetPistol);
                break;

            case Stage.GetPistol:
                if (weapons != null && weapons.CurrentWeapon != null)
                {
                    SetStage(Stage.GetRadio);
                    DialogueSystem.Play("pistol_1");
                    if (radio == null) OnRadioTaken();
                }
                break;

            case Stage.GetRadio:
                if (radioDone && stageClock > 0f && !DialogueSystem.IsPlaying)
                {
                    radioDone = false;
                    StartWave(0);
                }
                break;

            case Stage.Wave1: case Stage.Wave2: case Stage.Wave3:
                UpdateWave(dt);
                break;

            case Stage.Break1: case Stage.Break2:
                if (stageClock >= breakTime && !Shopkeeper.IsShopOpen) StartWave(stage == Stage.Break1 ? 1 : 2);
                break;

            case Stage.GoBasement:
                if (!stairsSaid && p.y < stairsY) { stairsSaid = true; DialogueSystem.Play("stairs_radio"); }
                if (ZombieBoss.Defeated || (boss != null && boss.IsDead)) SetStage(CrowbarHeld() ? Stage.OpenDoor : Stage.GetCrowbar);
                else if (boss == null)
                {
                    // Босса в сцене нет — лом лежит у входа в подвал.
                    if (CrowbarWeapon.Find() == null) CrowbarWeapon.Create(basementEntrance - Vector3.forward, 0f);
                    SetStage(Stage.GetCrowbar);
                }
                break;

            case Stage.GetCrowbar:
                if (CrowbarHeld()) { SetStage(Stage.OpenDoor); DialogueSystem.Play("crowbar"); }
                break;

            case Stage.OpenDoor:
                if (shapikCage == null) SetStage(Stage.FindShapik);
                break;

            case Stage.FindShapik:
                if (shapik != null && stageClock > 1.5f && Vector3.Distance(p, shapik.transform.position) < 3.3f)
                    StartCoroutine(Finale());
                break;
        }
    }

    bool CrowbarHeld() => weapons != null && weapons.Melee;

    // ---------- Катсцены ----------

    void Lock(bool on)
    {
        FirstPersonController.InputLocked = on;
        DialogueSystem.AllowSkip = on;
        if (!on) lookAt = null;
    }

    bool SkipPressed() => Keyboard.current != null && (Keyboard.current.spaceKey.wasPressedThisFrame || Keyboard.current.enterKey.wasPressedThisFrame);

    IEnumerator Wait(float seconds)
    {
        for (float t = 0f; t < seconds; t += Time.deltaTime)
        {
            if (SkipPressed()) yield break;
            yield return null;
        }
    }

    IEnumerator Say(params string[] ids)
    {
        bool done = false;
        DialogueSystem.Play(() => done = true, ids);
        while (!done) yield return null;
    }

    IEnumerator Fade(float to, float seconds)
    {
        float from = fade;
        for (float t = 0f; t < seconds; t += Time.deltaTime)
        {
            fade = Mathf.Lerp(from, to, t / seconds);
            yield return null;
        }
        fade = to;
    }

    IEnumerator Cinematic(bool on)
    {
        float from = cinematic, to = on ? 1f : 0f;
        for (float t = 0f; t < 0.4f; t += Time.deltaTime) { cinematic = Mathf.Lerp(from, to, t / 0.4f); yield return null; }
        cinematic = to;
    }

    /// <summary>Сцена 1: пробуждение — герой лежит щекой на конспекте, поднимается.</summary>
    IEnumerator Intro()
    {
        SetStage(Stage.Intro);
        Lock(true);
        cinematic = 1f;
        fade = 1f;
        fpc.EyeOffset = -0.75f;
        fpc.CameraRoll = 28f;
        fpc.SetPitch(35f);
        var hum = gameObject.AddComponent<AudioSource>();
        hum.clip = FridgeHum();
        hum.loop = true;
        hum.volume = 0.35f * GameSettings.EffectsVolume;
        hum.Play();
        yield return Wait(1.2f);
        yield return Fade(0f, 2.5f);
        yield return Wait(0.6f);
        // Встаёт.
        for (float t = 0f; t < 2f; t += Time.deltaTime)
        {
            float k = Mathf.SmoothStep(0f, 1f, t / 2f);
            fpc.EyeOffset = Mathf.Lerp(-0.75f, 0f, k);
            fpc.CameraRoll = Mathf.Lerp(28f, 0f, k);
            fpc.SetPitch(Mathf.Lerp(35f, 0f, k));
            if (SkipPressed()) break;
            yield return null;
        }
        fpc.EyeOffset = 0f;
        fpc.CameraRoll = 0f;
        yield return Say("intro_1");
        fade = 0f;
        yield return Cinematic(false);
        Lock(false);
        hum.volume *= 0.5f;
        SetStage(Stage.MeetGalya);
    }

    /// <summary>Галя объясняет, что происходит.</summary>
    IEnumerator GalyaScene()
    {
        busy = true;
        objective = null;
        Lock(true);
        lookAt = () => galya.transform.position + Vector3.up * 1.55f;
        yield return Cinematic(true);
        yield return Say("galya_1", "galya_2", "galya_3", "galya_4", "galya_5", "galya_6", "galya_7", "galya_8");
        yield return Cinematic(false);
        Lock(false);
        busy = false;
        SetStage(Stage.GetPistol);
    }

    public void OnRadioTaken()
    {
        if (stage != Stage.GetRadio) return;
        objective = null;
        DialogueSystem.Play(() => radioDone = true, "radio_1", "radio_2", "radio_3");
    }

    // ---------- Волны ----------

    void StartWave(int i)
    {
        SetStage(i == 0 ? Stage.Wave1 : i == 1 ? Stage.Wave2 : Stage.Wave3);
        var w = Waves[i];
        waveTotal = w.count;
        waveSpawned = 0;
        waveAlive.Clear();
        waveTimer = 2f;
        waveEnding = false;
        DialogueSystem.Play(i == 0 ? "wave1_start" : i == 1 ? "wave2_start" : "wave3_start");
        Hint("Волна " + (i + 1) + " из 3: " + w.title, 4f);
    }

    int WaveIndex => stage == Stage.Wave1 ? 0 : stage == Stage.Wave2 ? 1 : 2;

    void UpdateWave(float dt)
    {
        var w = Waves[WaveIndex];
        waveAlive.RemoveAll(z => z == null || z.IsDead);
        waveTimer -= dt;
        if (waveSpawned < waveTotal && waveTimer <= 0f && waveAlive.Count < w.maxAlive && spawner != null)
        {
            waveTimer = w.interval;
            var z = spawner.Spawn();
            if (z != null)
            {
                z.Configure(w.health, w.speed, w.coins, w.barks, w.scale);
                waveAlive.Add(z);
                waveSpawned++;
            }
        }
        if (spawner == null) waveSpawned = waveTotal;
        // Галя отвешивает половником одному из «опоздунов».
        if (stage == Stage.Wave2 && stageClock > 12f && stageClock - dt <= 12f) DialogueSystem.Say("wave2_bark");

        if (!waveEnding && waveSpawned >= waveTotal && waveAlive.Count == 0)
        {
            waveEnding = true;
            if (stage == Stage.Wave1) { SetStage(Stage.Break1); DialogueSystem.Play("wave1_end"); }
            else if (stage == Stage.Wave2) { SetStage(Stage.Break2); Hint("Волна отбита! Следующая — «Старосты»", 4f); }
            else StartCoroutine(LeaveCanteen());
        }
    }

    /// <summary>Сцена 3–4: после третьей волны Галя открывает кухню и даёт пирожки.</summary>
    IEnumerator LeaveCanteen()
    {
        SetStage(Stage.GoBasement);
        if (kitchenDoor != null) kitchenDoor.Locked = false;
        yield return Say("wave3_end");
        if (inventory != null) inventory.Add(ItemDatabase.Pie, 3);
        Hint("Получено: пирожок с картошкой ×3", 4f);
        yield return Say("leave_1", "leave_2", "leave_3");
    }

    // ---------- Босс ----------

    void OnBossWoke(ZombieBoss b)
    {
        boss = b;
        if (stage > Stage.BossFight) return;
        b.holdForDialogue = true;
        StartCoroutine(DeanScene(b));
    }

    /// <summary>Сцена 7: Декан выходит из темноты.</summary>
    IEnumerator DeanScene(ZombieBoss b)
    {
        SetStage(Stage.BossFight);
        busy = true;
        DialogueSystem.StopAll();
        Lock(true);
        lookAt = () => b != null ? b.HeadPosition : fpc.transform.position + fpc.transform.forward;
        yield return Cinematic(true);
        yield return Wait(1.8f); // рёв
        yield return Say("dean_1", "dean_2", "dean_3");
        yield return Cinematic(false);
        Lock(false);
        busy = false;
        if (b != null) b.holdForDialogue = false;
        yield return new WaitForSeconds(9f);
        if (stage == Stage.BossFight) DialogueSystem.Say("dean_radio");
    }

    void OnBossSummoned(ZombieBoss b) => DialogueSystem.Say("dean_next");
    void OnBossEnraged(ZombieBoss b) => DialogueSystem.Play("dean_retake");
    void OnBossSlam(ZombieBoss b) { if (Random.value < 0.4f) DialogueSystem.Say("dean_attendance"); }
    void OnBossStunned(ZombieBoss b) { if (!stunSaid) { stunSaid = true; DialogueSystem.Say("dean_stun_radio"); } }

    void OnBossDied(ZombieBoss b)
    {
        if (stage <= Stage.BossFight) SetStage(Stage.GetCrowbar);
    }

    public void OnDoorOpened()
    {
        if (stage <= Stage.OpenDoor) SetStage(Stage.FindShapik);
        DialogueSystem.Play("door_open");
    }

    // ---------- Развязка ----------

    IEnumerator Finale()
    {
        SetStage(Stage.Finale);
        busy = true;
        objective = null;
        DialogueSystem.StopAll();
        Lock(true);
        lookAt = () => shapik.HeadPosition;
        yield return Cinematic(true);
        shapik.pose = ShapikNpc.PoseKind.Stand;
        yield return Wait(1.2f);
        yield return Say("final_1", "final_2", "final_3", "final_4", "final_5");

        // Входит Галя с подносом пирожков.
        var galyaCopy = SpawnGalya();
        if (galyaCopy != null) lookAt = () => galyaCopy.position + Vector3.up * 1.55f;
        yield return Wait(0.8f);
        yield return Say("final_6");
        lookAt = () => shapik.HeadPosition;
        yield return Say("final_7", "final_8", "final_9");

        // Рукопожатие.
        shapik.pose = ShapikNpc.PoseKind.Handshake;
        waitingHandshake = true;
        while (waitingHandshake) yield return null;
        fpc.AddShake(0.4f);
        var a = gameObject.AddComponent<AudioSource>();
        a.PlayOneShot(Clap(), GameSettings.EffectsVolume);
        yield return new WaitForSeconds(1.3f);
        yield return Fade(1f, 1.5f);
        DialogueSystem.StopAll();
        SetStage(Stage.Credits);
        creditsClock = 0f;
    }

    Transform SpawnGalya()
    {
        if (shapikCage == null || fpc == null) return null;
        var go = new GameObject("Galya_Finale");
        // Галя подходит сбоку от игрока, со стороны входа в зал.
        Vector3 toEntrance = basementEntrance - fpc.transform.position; toEntrance.y = 0f;
        Vector3 side = toEntrance.sqrMagnitude > 0.01f ? toEntrance.normalized : -fpc.transform.forward;
        Vector3 pos = fpc.transform.position + side * 1.6f + fpc.transform.right * 0.6f;
        pos.y = fpc.transform.position.y;
        Vector3 look = shapik.transform.position - pos; look.y = 0f;
        go.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(look.sqrMagnitude > 0.01f ? look : fpc.transform.forward));
        Shopkeeper.BuildModel(go.transform);
        // Поднос с пирожками в руках.
        var tray = Interactable.Part(go.transform, PrimitiveType.Cube, new Vector3(0f, 1.05f, 0.32f), new Vector3(0.42f, 0.02f, 0.3f), Interactable.Mat(new Color(0.75f, 0.6f, 0.4f), 0.3f));
        for (int i = 0; i < 5; i++)
            Interactable.Part(tray.parent, PrimitiveType.Sphere, new Vector3(-0.14f + i * 0.07f, 1.08f, 0.3f + (i % 2) * 0.06f), new Vector3(0.09f, 0.035f, 0.05f), Interactable.Mat(new Color(0.78f, 0.5f, 0.22f), 0.45f));
        foreach (var t in go.GetComponentsInChildren<Transform>())
            if (t.name == "ArmL" || t.name == "ArmR") t.localRotation = Quaternion.Euler(-60f, 0f, 0f);
            else if (t.name == "ForeArmL" || t.name == "ForeArmR") t.localRotation = Quaternion.Euler(-30f, 0f, 0f);
        return go.transform;
    }

    /// <summary>Строка титров: текст или фото разработчика (Assets/Resources/Credits/&lt;photo&gt;.jpg) с подписью.</summary>
    struct CreditLine
    {
        public string text, photo;
        public static implicit operator CreditLine(string t) => new CreditLine { text = t };
    }

    static CreditLine Dev(string photo, string text) => new CreditLine { photo = photo, text = text };

    static readonly CreditLine[] Credits =
    {
        "<size=220%><b>Opuhnut-s-goloda</b></size>",
        "<size=120%>Часть первая: «Пересдача»</size>", "", "",
        "<size=130%><b>Разработчики</b></size>", "",
        Dev("kolesnikov", "<b>Надеев Алексей</b>\nмаленький кучерявый разработчик"),
        Dev("lipatov", "<b>Липатов Егор</b>\nсредний кучерявый разработчик"),
        Dev("nadeev", "<b>Колесников Арсений</b>\nбольшой волосатый разработчик"), "",
        "<b>В ролях</b>",
        "Студент — ты, третий курс, проспал апокалипсис",
        "Галя — тридцать лет на раздаче",
        "Шапик — друг и геймдизайнер",
        "Декан — три недели пайки",
        "Зомби — аниматроники кафедры робототехники", "",
        "<b>Озвучка</b>",
        "Голоса Windows: Ирина и Павел", "",
        "<b>Особая благодарность</b>",
        "Компоту из столовой",
        "Матану, который так и не выучили", "",
        "<size=140%>Спасибо, что сыграли в Opuhnut-s-goloda!</size>",
    };

    /// <summary>Сколько секунд идут титры (считается при отрисовке по их высоте).</summary>
    float creditsDuration = 30f;
    readonly Dictionary<string, Texture2D> creditPhotos = new Dictionary<string, Texture2D>();

    Texture2D CreditPhoto(string name)
    {
        if (!creditPhotos.TryGetValue(name, out var tex))
            creditPhotos[name] = tex = Resources.Load<Texture2D>("Credits/" + name);
        return tex;
    }

    void UpdateCredits(float dt)
    {
        creditsClock += dt;
        if (!postScene && (creditsClock > creditsDuration || (creditsClock > 1f && SkipPressed())))
        {
            postScene = true;
            creditsClock = creditsDuration;
            DialogueSystem.Play(() => theEnd = true, "post_1");
        }
        if (theEnd && (Controls.WasPressed(GameAction.Interact) || SkipPressed()))
        {
            creditsClock = -1f;
            Lock(false);
            SceneManager.LoadScene(PauseMenu.MainMenuScene);
        }
    }

    // ---------- Интерфейс ----------

    void Hint(string text, float seconds)
    {
        hint = text;
        hintUntil = Time.time + seconds;
    }

    /// <summary>Текущее задание (для карты в инвентаре); null — задания сейчас нет.</summary>
    public static string Objective => Instance != null ? Instance.objective : null;
    /// <summary>Точка текущего задания в мире (для метки на карте); null — метки нет.</summary>
    public static Vector3? ObjectiveTarget => Instance != null && !string.IsNullOrEmpty(Instance.objective) ? Instance.MapTarget() : null;

    /// <summary>Метка на карте: как на экране, плюс цели, которые на экране не отмечаются (прилавок в волну, Декан в бою).</summary>
    Vector3? MapTarget()
    {
        var t = MarkerTarget();
        if (t.HasValue) return t;
        switch (stage)
        {
            case Stage.Wave1: case Stage.Wave2: case Stage.Wave3:
                return galya != null ? galya.transform.position : (Vector3?)null;
            case Stage.BossFight:
                return boss != null && !boss.IsDead ? boss.transform.position : basementEntrance;
        }
        return null;
    }

    Vector3? MarkerTarget()
    {
        switch (stage)
        {
            case Stage.MeetGalya: case Stage.Break1: case Stage.Break2:
                return galya != null ? galya.transform.position + Vector3.up * 1.9f : (Vector3?)null;
            case Stage.GetPistol:
                return pistol != null && !pistol.IsHeld && !pistol.Stowed ? pistol.transform.position + Vector3.up * 0.3f : (Vector3?)null;
            case Stage.GetRadio:
                return radio != null ? radio.transform.position + Vector3.up * 0.25f : (Vector3?)null;
            case Stage.GoBasement:
                return fpc != null && fpc.transform.position.y > stairsY + 0.5f ? stairsTop + Vector3.up * 1.2f : basementEntrance + Vector3.up * 1.2f;
            case Stage.GetCrowbar:
                var crowbar = CrowbarWeapon.Find();
                return crowbar != null && !crowbar.IsHeld && !crowbar.Stowed ? crowbar.transform.position + Vector3.up * 0.4f : (Vector3?)null;
            case Stage.OpenDoor:
                return shapikCage != null ? shapikCage.Focus + Vector3.up * 0.3f : (Vector3?)null;
            case Stage.FindShapik:
                return shapik != null ? shapik.HeadPosition + Vector3.up * 0.3f : (Vector3?)null;
        }
        return null;
    }

    void OnGUI()
    {
        if (SaveSystem.HideHud) return;
        float H = Screen.height, W = Screen.width;

        // Затемнение и «кинорамка».
        if (cinematic > 0f && creditsClock < 0f)
        {
            float bar = H * 0.1f * cinematic;
            GUI.color = Color.black;
            GUI.DrawTexture(new Rect(0, 0, W, bar), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(0, H - bar, W, bar), Texture2D.whiteTexture);
        }
        if (fade > 0f)
        {
            GUI.color = new Color(0f, 0f, 0f, fade);
            GUI.DrawTexture(new Rect(0, 0, W, H), Texture2D.whiteTexture);
        }
        GUI.color = Color.white;

        if (creditsClock >= 0f) { DrawCredits(); return; }
        if (waitingHandshake && !PauseMenu.IsPaused)
        {
            var hs = new GUIStyle(GUI.skin.label) { fontSize = Mathf.Max(18, (int)(H / 30f)), fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            float pulse = 0.7f + 0.3f * Mathf.Sin(Time.time * 5f);
            var r = new Rect(0, H * 0.62f, W, 50);
            GUI.color = new Color(0f, 0f, 0f, 0.8f); GUI.Label(new Rect(r.x + 2, r.y + 2, r.width, r.height), "[" + Controls.KeyName(GameAction.Interact) + "]  Пожать руку", hs);
            GUI.color = new Color(1f, 0.9f, 0.6f, pulse); GUI.Label(r, "[" + Controls.KeyName(GameAction.Interact) + "]  Пожать руку", hs);
            GUI.color = Color.white;
        }
        if (PauseMenu.IsPaused || InventoryScreen.IsOpen || Shopkeeper.IsShopOpen || cinematic > 0.5f) return;

        var shadow = new Color(0f, 0f, 0f, 0.8f);
        // Задание — сверху слева.
        if (!string.IsNullOrEmpty(objective))
        {
            var st = new GUIStyle(GUI.skin.label) { fontSize = Mathf.Max(14, (int)(H / 45f)), fontStyle = FontStyle.Bold, wordWrap = true };
            string text = "ЗАДАЧА:  " + objective;
            if (stage == Stage.Wave1 || stage == Stage.Wave2 || stage == Stage.Wave3)
                text += "\nВолна " + (WaveIndex + 1) + " из 3 " + Waves[WaveIndex].title + " — осталось " + (waveTotal - waveSpawned + waveAlive.Count);
            if (stage == Stage.Break1 || stage == Stage.Break2)
                text += "\nСледующая волна через " + Mathf.CeilToInt(Mathf.Max(0f, breakTime - stageClock)) + " с";
            var r = new Rect(24, 20, W * 0.45f, H * 0.12f);
            GUI.color = shadow; GUI.Label(new Rect(r.x + 2, r.y + 2, r.width, r.height), text, st);
            GUI.color = new Color(1f, 0.9f, 0.6f); GUI.Label(r, text, st);
        }
        // Подсказка — по центру сверху.
        if (hint != null && Time.time < hintUntil)
        {
            var st = new GUIStyle(GUI.skin.label) { fontSize = Mathf.Max(14, (int)(H / 42f)), alignment = TextAnchor.MiddleCenter };
            var r = new Rect(0, H * 0.14f, W, 40);
            GUI.color = shadow; GUI.Label(new Rect(r.x + 2, r.y + 2, r.width, r.height), hint, st);
            GUI.color = Color.white; GUI.Label(r, hint, st);
        }
        // Метка цели: ромб над целью (или у края экрана, если цель сзади) и расстояние.
        var target = MarkerTarget();
        var camera = cam != null ? cam.GetComponent<Camera>() : null;
        if (target.HasValue && camera != null)
        {
            Vector3 sp = camera.WorldToScreenPoint(target.Value);
            bool behind = sp.z < 0f;
            if (behind) { sp.x = W - sp.x; sp.y = 0f; }
            // Размеры — от высоты экрана (при 1080p ромб 18 px), чтобы метка не менялась с разрешением.
            float s = H / 1080f;
            float d = Mathf.Max(10f, 18f * s);           // сторона ромба
            float labelH = Mathf.Max(16f, 26f * s);
            float m = d + 8f * s;
            float x = Mathf.Clamp(sp.x, m, W - m);
            float y = Mathf.Clamp(H - sp.y, m + H * 0.1f, H - m - labelH - H * 0.12f);
            float dist = Vector3.Distance(cam.position, target.Value);
            float pulse = 0.75f + 0.25f * Mathf.Sin(Time.time * 4f);
            var gold = new Color(1f, 0.85f, 0.3f, pulse);

            // Ромб точно над целью: тёмная обводка и жёлтая заливка.
            var center = new Vector2(x, y);
            var gm = GUI.matrix;
            GUIUtility.RotateAroundPivot(45f, center);
            float o = Mathf.Max(1.5f, 2.5f * s);
            GUI.color = new Color(0f, 0f, 0f, 0.8f * pulse);
            GUI.DrawTexture(new Rect(x - d / 2f - o, y - d / 2f - o, d + o * 2f, d + o * 2f), Texture2D.whiteTexture);
            GUI.color = gold;
            GUI.DrawTexture(new Rect(x - d / 2f, y - d / 2f, d, d), Texture2D.whiteTexture);
            GUI.matrix = gm;

            // Расстояние — под ромбом.
            var st = new GUIStyle(GUI.skin.label) { fontSize = Mathf.Max(12, Mathf.RoundToInt(20f * s)), alignment = TextAnchor.UpperCenter, fontStyle = FontStyle.Bold };
            var lr = new Rect(x - 60f * s, y + d * 0.75f + 2f * s, 120f * s, labelH);
            string distText = Mathf.RoundToInt(dist) + " м";
            GUI.color = shadow; GUI.Label(new Rect(lr.x + 1.5f, lr.y + 1.5f, lr.width, lr.height), distText, st);
            GUI.color = gold; GUI.Label(lr, distText, st);
        }
        GUI.color = Color.white;
    }

    void DrawCredits()
    {
        float H = Screen.height, W = Screen.width;
        GUI.color = Color.black;
        GUI.DrawTexture(new Rect(0, 0, W, H), Texture2D.whiteTexture);
        GUI.color = Color.white;
        var st = new GUIStyle(GUI.skin.label) { fontSize = Mathf.Max(16, (int)(H / 32f)), alignment = TextAnchor.UpperCenter, richText = true, wordWrap = true };
        if (!postScene)
        {
            float lineH = st.fontSize * 1.6f;
            float speed = H * 0.08f;
            float y = H - creditsClock * speed, top = y;
            foreach (var line in Credits)
            {
                if (line.photo != null)
                {
                    // Фото разработчика в рамке и подпись под ним.
                    var tex = CreditPhoto(line.photo);
                    float ph = H * 0.3f, pw = tex != null ? ph * tex.width / tex.height : ph * 0.75f;
                    var pr = new Rect((W - pw) / 2f, y, pw, ph);
                    GUI.color = new Color(0.9f, 0.75f, 0.35f);
                    GUI.DrawTexture(new Rect(pr.x - 4f, pr.y - 4f, pr.width + 8f, pr.height + 8f), Texture2D.whiteTexture);
                    GUI.color = Color.white;
                    if (tex != null) GUI.DrawTexture(pr, tex, ScaleMode.ScaleToFit);
                    y += ph + lineH * 0.4f;
                    GUI.Label(new Rect(W * 0.1f, y, W * 0.8f, lineH * 2.2f), line.text, st);
                    y += lineH * 2.6f;
                    continue;
                }
                GUI.Label(new Rect(W * 0.1f, y, W * 0.8f, lineH * 2.4f), line.text, st);
                y += lineH * (line.text.StartsWith("<size=220%") ? 2.6f : line.text.StartsWith("<size=1") ? 1.4f : 1f);
            }
            // Титры кончаются, когда последняя строка доехала до середины экрана.
            creditsDuration = Mathf.Max(10f, (y - top + H * 0.5f) / speed);
            return;
        }
        // Постсцена: расписание.
        var card = new Rect(W * 0.3f, H * 0.25f, W * 0.4f, H * 0.32f);
        GUI.color = new Color(0.92f, 0.9f, 0.82f);
        GUI.DrawTexture(card, Texture2D.whiteTexture);
        var cs = new GUIStyle(st) { richText = true };
        cs.normal.textColor = new Color(0.1f, 0.1f, 0.15f);
        GUI.color = Color.white;
        GUI.Label(new Rect(card.x + 20, card.y + 20, card.width - 40, card.height - 40),
            "<b>РАСПИСАНИЕ</b>\n\nМатематический анализ\n<color=#b01010><b>ПЕРЕСДАЧА — СЕГОДНЯ, 9:00</b></color>\n(перенесено)", cs);
        if (theEnd)
        {
            GUI.Label(new Rect(0, H * 0.68f, W, 60), "<b>КОНЕЦ</b>", st);
            var small = new GUIStyle(st) { fontSize = Mathf.Max(12, (int)(H / 50f)) };
            GUI.Label(new Rect(0, H * 0.76f, W, 40), "[" + Controls.KeyName(GameAction.Interact) + "] — в главное меню", small);
        }
    }

    // ---------- Звуки ----------

    static AudioClip FridgeHum()
    {
        const int rate = 22050;
        int n = rate * 2;
        var data = new float[n];
        var rnd = new System.Random(5);
        float lp = 0f;
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)rate;
            lp += (((float)rnd.NextDouble() * 2f - 1f) - lp) * 0.02f;
            data[i] = (Mathf.Sin(2f * Mathf.PI * 50f * t) * 0.25f + Mathf.Sin(2f * Mathf.PI * 100f * t) * 0.15f + lp * 0.6f) * 0.5f;
        }
        var clip = AudioClip.Create("FridgeHum", n, 1, rate, false);
        clip.SetData(data, 0);
        return clip;
    }

    static AudioClip Clap()
    {
        const int rate = 44100;
        int n = rate / 4;
        var data = new float[n];
        var rnd = new System.Random(11);
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)rate;
            data[i] = ((float)rnd.NextDouble() * 2f - 1f) * Mathf.Exp(-t * 40f) * 0.8f;
        }
        var clip = AudioClip.Create("Clap", n, 1, rate, false);
        clip.SetData(data, 0);
        return clip;
    }
}
