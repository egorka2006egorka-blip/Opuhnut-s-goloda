using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Аварийная блокировка серверной: когда игрок входит в зал, Шапик оказывается заперт в пультовой, воет сирена,
/// через аварийный выход лезут зомби. Чтобы открыть пультовую, нужно решить три задачи (мини-игры):
///  • BreakerPanel — рубильники в комнате ИБП (головоломка: каждый рычаг переключает и соседний);
///  • InfectedServer ×3 — найти в рядах стоек мигающие красным заражённые серверы и выдернуть их;
///  • HackTerminal — взлом замка на терминале у двери пультовой (поймать бегунок в зелёной зоне).
/// Задачи появляются (включается объект tasks) только с началом блокировки. Ставит CanteenSceneBuilder.
/// </summary>
public class ServerLockdown : MonoBehaviour
{
    public static ServerLockdown Instance { get; private set; }

    [Header("Ссылки (ставит сборщик сцены)")]
    [Tooltip("Дверь пультовой: заперта, пока блокировка не снята.")]
    public SwingGate controlDoor;
    [Tooltip("Объект с задачами (выключен до начала блокировки).")]
    public GameObject tasks;
    [Tooltip("Красные тревожные лампы (выключены до начала блокировки).")]
    public GameObject alarmLights;
    public BreakerPanel breakers;
    public InfectedServer[] servers;
    public HackTerminal terminal;

    [Header("Зал")]
    [Tooltip("Пол зала: внутри него (дальше входной двери) игрок запускает блокировку; здесь же появляются зомби.")]
    public Bounds hall;
    public float floorY;
    [Tooltip("Входная дверь серверной и направление вглубь зала.")]
    public Vector3 entry, inward;
    [Tooltip("Пультовая — там зомби не появляются.")]
    public Bounds controlRoom;
    [Tooltip("Точки, откуда лезут зомби (у аварийного выхода).")]
    public Vector3[] spawnPoints;

    [Header("Зомби")]
    public int maxAlive = 5;
    public float spawnInterval = 4f;

    public enum Task { Breakers, Servers, Terminal }
    public bool Active { get; private set; }
    public bool Done { get; private set; }
    public int Solved => (breakersDone ? 1 : 0) + (serversDone ? 1 : 0) + (terminalDone ? 1 : 0);
    public bool IsSolved(Task t) => t == Task.Breakers ? breakersDone : t == Task.Servers ? serversDone : terminalDone;
    /// <summary>Сколько заражённых серверов уже выдернуто.</summary>
    public int ServersPulled { get; private set; }

    /// <summary>Решена задача (номер — сколько решено всего) и вся блокировка снята.</summary>
    public event System.Action<int> TaskSolved;
    public event System.Action Unlocked;

    bool breakersDone, serversDone, terminalDone;
    readonly List<Zombie> spawned = new List<Zombie>();
    float spawnTimer;
    AudioSource siren;
    Transform player;
    Camera cam;

    void Awake() => Instance = this;
    void OnDestroy() { if (Instance == this) Instance = null; }

    void Start()
    {
        if (controlDoor != null && !Done) { controlDoor.Locked = true; controlDoor.lockedMessage = "Пультовая заблокирована. Надо снять блокировку."; controlDoor.lockedSpeaker = "STUDENT"; }
        if (tasks != null && !Active && !Done) tasks.SetActive(false);
        if (alarmLights != null && !Active) alarmLights.SetActive(false);
    }

    /// <summary>Игрок в зале серверной (прошёл входную дверь).</summary>
    public bool PlayerInside(Vector3 p) =>
        p.y > floorY - 0.6f && p.y < floorY + 2.5f && p.x > hall.min.x && p.x < hall.max.x && p.z > hall.min.z && p.z < hall.max.z &&
        Vector3.Dot(p - entry, inward) > 0.8f;

    public void Begin()
    {
        if (Active || Done) return;
        Active = true;
        if (controlDoor != null) { controlDoor.Locked = true; if (controlDoor.IsOpen) controlDoor.Toggle(entry); }
        if (tasks != null) tasks.SetActive(true);
        if (alarmLights != null) alarmLights.SetActive(true);
        siren = gameObject.AddComponent<AudioSource>();
        siren.clip = Siren();
        siren.loop = true;
        siren.spatialBlend = 0f;
        siren.volume = 0.18f * GameSettings.EffectsVolume;
        siren.Play();
        spawnTimer = 2.5f;
    }

    /// <summary>Загрузка сохранения после снятия блокировки: всё решено, дверь открыта, тревоги нет.</summary>
    public void CompleteInstant()
    {
        breakersDone = serversDone = terminalDone = true;
        Done = true;
        Active = false;
        if (tasks != null) tasks.SetActive(false);
        if (alarmLights != null) alarmLights.SetActive(false);
        if (controlDoor != null) controlDoor.Locked = false;
    }

    public void OnServerPulled()
    {
        ServersPulled++;
        if (servers != null && ServersPulled >= servers.Length) Solve(Task.Servers);
    }

    public void Solve(Task t)
    {
        if (!Active || IsSolved(t)) return;
        if (t == Task.Breakers) breakersDone = true;
        else if (t == Task.Servers) serversDone = true;
        else terminalDone = true;
        if (Solved >= 3) Finish();
        else TaskSolved?.Invoke(Solved);
    }

    void Finish()
    {
        Active = false;
        Done = true;
        if (siren != null) siren.Stop();
        if (alarmLights != null) alarmLights.SetActive(false);
        // С блокировкой отключаются и зомби серверной (они — аниматроники Шапика): падают там, где стоят.
        // Иначе они грызли бы игрока во время финальной катсцены, когда управление заблокировано.
        foreach (var z in spawned)
            if (z != null && !z.IsDead) z.TakeHit(100000f, z.transform.position + Vector3.up, -z.transform.forward, null);
        spawned.Clear();
        if (controlDoor != null)
        {
            controlDoor.Locked = false;
            // Дверь распахивается в зал (от пультовой).
            if (!controlDoor.IsOpen) controlDoor.Toggle(controlRoom.center);
        }
        Unlocked?.Invoke();
    }

    /// <summary>Ближайшая к точке нерешённая задача (для метки задания).</summary>
    public Vector3? NextTarget(Vector3 from)
    {
        Vector3? best = null;
        float bestD = float.MaxValue;
        void Try(Vector3 p) { float d = (p - from).sqrMagnitude; if (d < bestD) { bestD = d; best = p; } }
        if (!breakersDone && breakers != null) Try(breakers.Focus);
        if (!serversDone && servers != null) foreach (var s in servers) if (s != null && !s.Pulled) Try(s.Focus);
        if (!terminalDone && terminal != null) Try(terminal.Focus);
        return best;
    }

    // ---------- Зомби ----------

    void Update()
    {
        if (!Active) return;
        if (siren != null) siren.volume = 0.18f * GameSettings.EffectsVolume;
        spawned.RemoveAll(z => z == null || z.IsDead);
        spawnTimer -= Time.deltaTime;
        if (spawnTimer > 0f || spawned.Count >= maxAlive) return;
        spawnTimer = spawnInterval * Mathf.Lerp(1f, 0.75f, Solved / 2f); // чем ближе к концу, тем чаще
        var sp = ZombieSpawner.Instance;
        if (sp == null || !ZombieSpawner.NavMeshReady) return;
        if (!FindSpawn(out Vector3 pos)) return;
        var z = sp.SpawnAt(pos);
        if (z == null) return;
        z.Configure(90f + 25f * Solved, 1f + 0.15f * Solved, new Vector2Int(2, 6), new[] { "zombie_1", "zombie_2", "zombie_3" });
        spawned.Add(z);
    }

    bool FindSpawn(out Vector3 pos)
    {
        pos = default;
        if (player == null)
        {
            var fpc = FindAnyObjectByType<FirstPersonController>();
            if (fpc == null) return false;
            player = fpc.transform;
            cam = fpc.GetComponentInChildren<Camera>();
        }
        var filter = new NavMeshQueryFilter { agentTypeID = ZombieSpawner.AgentTypeId, areaMask = NavMesh.AllAreas };
        var path = new NavMeshPath();
        NavMesh.SamplePosition(player.position, out NavMeshHit ph, 3f, filter);
        for (int i = 0; i < 30; i++)
        {
            // Чаще всего — у аварийного выхода, иногда — из глубины зала (из-за стоек).
            Vector3 c = spawnPoints != null && spawnPoints.Length > 0 && i % 3 != 2
                ? spawnPoints[Random.Range(0, spawnPoints.Length)] + new Vector3(Random.Range(-1f, 1f), 0f, Random.Range(-1f, 1f))
                : new Vector3(Random.Range(hall.min.x + 1f, hall.max.x - 1f), floorY, Random.Range(hall.min.z + 1f, hall.max.z - 1f));
            if (!NavMesh.SamplePosition(c, out NavMeshHit hit, 1.5f, filter)) continue;
            Vector3 p = hit.position;
            if (Mathf.Abs(p.y - floorY) > 0.5f || controlRoom.Contains(new Vector3(p.x, controlRoom.center.y, p.z))) continue;
            if (Vector3.Dot(p - entry, inward) < 2f) continue; // не в коридоре за спиной у входа
            Vector3 d = p - player.position; d.y = 0f;
            if (d.magnitude < 8f) continue;
            if (i < 20 && Visible(p)) continue;
            if (ph.hit && (!NavMesh.CalculatePath(p, ph.position, filter, path) || path.status != NavMeshPathStatus.PathComplete)) continue;
            pos = p;
            return true;
        }
        return false;
    }

    bool Visible(Vector3 p)
    {
        if (cam == null) return false;
        Vector3 chest = p + Vector3.up * 1.2f;
        Vector3 vp = cam.WorldToViewportPoint(chest);
        if (vp.z <= 0f || vp.x < -0.1f || vp.x > 1.1f || vp.y < -0.1f || vp.y > 1.1f) return false;
        Vector3 from = cam.transform.position, to = chest - from;
        foreach (var h in Physics.RaycastAll(from, to.normalized, to.magnitude - 0.1f, ~0, QueryTriggerInteraction.Ignore))
            if (!h.transform.IsChildOf(player)) return false;
        return true;
    }

    // ---------- Звук ----------

    /// <summary>Сирена: тон плавно гуляет между 600 и 900 Гц (2 с на цикл).</summary>
    static AudioClip Siren()
    {
        const int rate = 22050;
        int n = rate * 2;
        var data = new float[n];
        float phase = 0f;
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)rate;
            float f = 750f + 150f * Mathf.Sin(2f * Mathf.PI * t / 2f);
            phase += f / rate;
            float s = Mathf.Sin(2f * Mathf.PI * phase);
            data[i] = (s + 0.3f * Mathf.Sign(s)) * 0.35f;
        }
        var clip = AudioClip.Create("Siren", n, 1, rate, false);
        clip.SetData(data, 0);
        return clip;
    }

    /// <summary>Общие звуки задач: щелчок, ошибка, успех.</summary>
    public static AudioClip Beep(float freq, float seconds, float decay)
    {
        const int rate = 22050;
        int n = (int)(rate * seconds);
        var data = new float[n];
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)rate;
            data[i] = Mathf.Sign(Mathf.Sin(2f * Mathf.PI * freq * t)) * 0.25f * Mathf.Exp(-t * decay) * Mathf.Min(1f, t * 200f);
        }
        var clip = AudioClip.Create("Beep", n, 1, rate, false);
        clip.SetData(data, 0);
        return clip;
    }

    static AudioClip ok, fail, clunk;
    public static AudioClip Ok => ok != null ? ok : ok = Beep(1320f, 0.25f, 8f);
    public static AudioClip Fail => fail != null ? fail : fail = Beep(180f, 0.35f, 6f);
    public static AudioClip Clunk
    {
        get
        {
            if (clunk != null) return clunk;
            const int rate = 22050;
            int n = rate / 4;
            var data = new float[n];
            var rnd = new System.Random(4);
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)rate;
                data[i] = (Mathf.Sin(2f * Mathf.PI * 120f * t) * 0.6f + ((float)rnd.NextDouble() * 2f - 1f) * 0.4f) * Mathf.Exp(-t * 30f);
            }
            clunk = AudioClip.Create("Clunk", n, 1, rate, false);
            clunk.SetData(data, 0);
            return clunk;
        }
    }
}
