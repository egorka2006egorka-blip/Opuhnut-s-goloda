using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Спавн зомби. При старте запекает NavMesh по коллайдерам сцены (игрок, оружие и зомби не учитываются),
/// затем держит на уровне до maxAlive живых зомби: новые появляются раз в spawnInterval секунд
/// в точках, до которых можно дойти до игрока, — подальше от игрока и по возможности вне поля зрения.
/// </summary>
public class ZombieSpawner : MonoBehaviour
{
    /// <summary>Спавнер сцены (для босса, который призывает зомби).</summary>
    public static ZombieSpawner Instance { get; private set; }
    /// <summary>Тип агента NavMesh, под который запечён NavMesh (нужен боссу). Смотреть только при NavMeshReady:
    /// идентификаторы типов агентов, созданных из кода (NavMesh.CreateSettings), бывают и отрицательными.</summary>
    public static int AgentTypeId { get; private set; } = -1;
    /// <summary>NavMesh запечён — AgentTypeId действителен.</summary>
    public static bool NavMeshReady { get; private set; }

    [Tooltip("Модели зомби (Assets/Characters/Zombies/Models/Zombie_*.fbx). Для каждого нового зомби выбирается случайная.")]
    public GameObject[] zombiePrefabs;
    [Tooltip("Animator Controller зомби (Assets/Characters/Zombies/Animator/Zombie.controller, собирается через Tools → Zombies → Setup Animations).")]
    public RuntimeAnimatorController zombieAnimator;
    [Tooltip("Прах после смерти: хлопья пепла (Assets/Characters/Zombies/Materials/FX_Ash) и пыль (Assets/Effects/FX_Smoke).")]
    public Material ashFlakeMaterial, ashDustMaterial;

    [Tooltip("Сам поддерживает число зомби. Выключено — зомби выпускает сюжет (StoryDirector) волнами через Spawn().")]
    public bool autoSpawn = true;

    [Header("Количество")]
    public int maxAlive = 6;
    [Tooltip("Сколько зомби появляется сразу при старте.")]
    public int initialCount = 3;
    [Tooltip("Пауза между появлениями (с).")]
    public float spawnInterval = 6f;

    [Header("Где появляются")]
    [Tooltip("Точки появления. Если не заданы — случайные точки NavMesh в пределах spawnArea.")]
    public Transform[] spawnPoints;
    public Bounds spawnArea = new Bounds(Vector3.zero, new Vector3(30f, 1f, 20f));
    [Tooltip("Не ближе этого к игроку (м).")]
    public float minPlayerDistance = 9f;

    [Header("NavMesh")]
    [Tooltip("Запечь NavMesh при старте (иначе нужен заранее запечённый для агента Humanoid).")]
    public bool bakeNavMeshOnStart = true;
    public float agentRadius = 0.35f;
    [Tooltip("Высота ступеньки, на которую может шагнуть зомби (ниже стульев и столов — чтобы не лезли на них).")]
    public float agentClimb = 0.4f;

    int agentTypeId;
    Transform player;
    Camera playerCamera;
    float timer;
    NavMeshPath path;
    NavMeshDataInstance navMesh;

    void Awake() => Instance = this;

    void Start()
    {
        path = new NavMeshPath();
        Zombie.AshFlakeMaterial = ashFlakeMaterial;
        Zombie.AshDustMaterial = ashDustMaterial;
        var fpc = FindAnyObjectByType<FirstPersonController>();
        if (fpc != null)
        {
            player = fpc.transform;
            playerCamera = fpc.GetComponentInChildren<Camera>();
        }
        if (bakeNavMeshOnStart) BakeNavMesh();

        if (autoSpawn) for (int i = 0; i < initialCount && Zombie.AliveCount < maxAlive; i++) Spawn();
        timer = spawnInterval;
    }

    void Update()
    {
        if (!autoSpawn) return;
        timer -= Time.deltaTime;
        if (timer > 0f) return;
        timer = spawnInterval;
        if (Zombie.AliveCount < maxAlive) Spawn();
    }

    void BakeNavMesh()
    {
        // Собственный тип агента: ступенька ниже, чем у стандартного Humanoid (0,75 м), — иначе зомби забираются на столы.
        var settings = NavMesh.CreateSettings();
        // Радиус запекания не больше 0,22 м. Проходы между рядами столов (спинка к спинке стульев) — всего 0,68–0,87 м,
        // дверные проёмы — ~0,9 м: с отступом 0,35 м от каждой стороны они закрывались, и зомби не проходили
        // ни между столами, ни в двери (особенно в лестничных тоннелях подвала). Сейчас в самом узком проходе — 0,24 м.
        settings.agentRadius = Mathf.Min(agentRadius, 0.22f);
        settings.agentHeight = 1.8f;
        settings.agentClimb = agentClimb;
        settings.agentSlope = 50f; // пандус лестницы — ровно 45°: с запасом, чтобы пролёт не выпадал из NavMesh
        // Мельче вокселы (по умолчанию радиус / 3 ≈ 0,1 м) — узкие проёмы не «съедаются» округлением.
        settings.overrideVoxelSize = true;
        settings.voxelSize = 0.06f; // самый узкий проход между стульями: 0,68 − 2 × 0,22 = 0,24 м — 4 вокселя
        agentTypeId = settings.agentTypeID;

        // Подвижные объекты в NavMesh не попадают: под игроком и оружием не должно быть дыр.
        var markups = new List<NavMeshBuildMarkup>();
        void Ignore(Transform t) { if (t != null) markups.Add(new NavMeshBuildMarkup { root = t, ignoreFromBuild = true, applyToChildren = true }); }
        Ignore(player);
        foreach (var w in WeaponPickup.All) if (w != null) Ignore(w.transform);
        // Мелкие предметы двигаются (их берут и бросают) — в NavMesh они не препятствия, иначе останутся «дыры».
        foreach (var g in GrabbableProp.All) if (g != null) Ignore(g.transform);
        foreach (var i in ItemPickup.All) if (i != null) Ignore(i.transform);
        foreach (var z in Zombie.All) if (z != null) Ignore(z.transform);
        foreach (var b in ZombieBoss.All) if (b != null) Ignore(b.transform);
        foreach (var s in FindObjectsByType<Shopkeeper>()) Ignore(s.transform);
        // Створки дверей и калиток тоже: закрытая дверь при запекании перекрывала бы проём,
        // и зомби не могли бы пройти в другие помещения. Закрытые двери зомби открывают сами (Zombie → SwingGate.OpenNear).
        foreach (var g in SwingGate.All)
            if (g != null && g.leaves != null)
                foreach (var leaf in g.leaves) Ignore(leaf);

        float t0 = Time.realtimeSinceStartup;
        // Границы — по всем коллайдерам сцены.
        bool any = false;
        Bounds bounds = new Bounds();
        foreach (var c in FindObjectsByType<Collider>())
        {
            if (!c.enabled || c.isTrigger) continue;
            if (!any) { bounds = c.bounds; any = true; } else bounds.Encapsulate(c.bounds);
        }
        if (!any) { Debug.LogWarning("ZombieSpawner: в сцене нет коллайдеров — NavMesh не из чего строить."); return; }
        bounds.Expand(2f);

        var sources = new List<NavMeshBuildSource>();
        NavMeshBuilder.CollectSources(bounds, ~0, NavMeshCollectGeometry.PhysicsColliders, 0, markups, sources);
        var data = NavMeshBuilder.BuildNavMeshData(settings, sources, bounds, Vector3.zero, Quaternion.identity);
        if (data == null) { Debug.LogWarning("ZombieSpawner: не удалось построить NavMesh."); return; }
        navMesh = NavMesh.AddNavMeshData(data);
        AgentTypeId = agentTypeId;
        NavMeshReady = true;
        Debug.Log($"ZombieSpawner: NavMesh запечён за {(Time.realtimeSinceStartup - t0):0.00} с ({sources.Count} объектов).");
        AddPassageLinks();
    }

    readonly List<NavMeshLinkInstance> links = new List<NavMeshLinkInstance>();

    /// <summary>
    /// Страховка проходов: где NavMesh по обе стороны двери (или внизу и наверху лестничного пролёта) есть,
    /// а пути между ними нет или он идёт в обход — добавляем связь (off-mesh link), по которой зомби проходят напрямую.
    /// Двери на пути зомби открывают сами (SwingGate.OpenNear).
    /// </summary>
    void AddPassageLinks()
    {
        var filter = new NavMeshQueryFilter { agentTypeID = agentTypeId, areaMask = NavMesh.AllAreas };
        var p = new NavMeshPath();
        int added = 0;
        var report = new List<string>();

        bool Connected(Vector3 a, Vector3 b)
        {
            if (!NavMesh.CalculatePath(a, b, filter, p) || p.status != NavMeshPathStatus.PathComplete) return false;
            float len = 0f;
            for (int i = 1; i < p.corners.Length; i++) len += Vector3.Distance(p.corners[i - 1], p.corners[i]);
            return len < Vector3.Distance(a, b) * 2.5f + 1f; // путь есть, но не огромный крюк
        }
        void Link(Vector3 a, Vector3 b, string what)
        {
            if (!NavMesh.SamplePosition(a, out NavMeshHit ha, 1f, filter) || !NavMesh.SamplePosition(b, out NavMeshHit hb, 1f, filter))
            {
                report.Add(what + ": нет NavMesh с одной из сторон");
                return;
            }
            if (Connected(ha.position, hb.position)) return;
            var inst = NavMesh.AddLink(new NavMeshLinkData
            {
                startPosition = ha.position, endPosition = hb.position,
                width = 0f, costModifier = -1f, bidirectional = true, area = 0, agentTypeID = agentTypeId,
            });
            if (!NavMesh.IsLinkValid(inst)) { report.Add(what + ": связь не добавилась"); return; }
            links.Add(inst);
            added++;
            report.Add(what + ": связь добавлена");
        }

        // Двери и калитки: по точке в 0,9 м перед створкой и за ней (на уровне пола).
        foreach (var g in SwingGate.All)
        {
            if (g == null || g.leaves == null) continue;
            foreach (var leaf in g.leaves)
            {
                if (leaf == null) continue;
                var rs = leaf.GetComponentsInChildren<Renderer>();
                if (rs.Length == 0) continue;
                Bounds b = rs[0].bounds;
                foreach (var r in rs) b.Encapsulate(r.bounds);
                if (Mathf.Max(b.size.x, b.size.z) < 0.35f) continue;
                Vector3 n = b.size.x < b.size.z ? Vector3.right : Vector3.forward; // толщина створки — поперёк прохода
                Vector3 floor = new Vector3(b.center.x, b.min.y + 0.05f, b.center.z);
                Link(floor - n * 0.9f, floor + n * 0.9f, "дверь " + leaf.name + " (" + g.name + ")");
            }
        }

        // Лестничные пролёты (Entrance*_StairTunnel*): низ пролёта — (0; 0; −0,7), верхняя площадка — (0; 4,4; −6,4).
        foreach (var t in FindObjectsByType<Transform>())
            if (t.name.StartsWith("Entrance") && t.name.Contains("StairTunnel")
                && t.Find("Tunnel_Shell") is Transform shell && shell.gameObject.activeInHierarchy) // убранная лестница — только дверь
                Link(t.TransformPoint(new Vector3(0f, 0.05f, -0.7f)), t.TransformPoint(new Vector3(0f, 4.45f, -6.4f)), "лестница " + t.name);

        if (report.Count > 0) Debug.Log($"ZombieSpawner: проходы — добавлено связей {added}.\n" + string.Join("\n", report));
    }

    void OnDestroy()
    {
        if (navMesh.valid) NavMesh.RemoveNavMeshData(navMesh);
        foreach (var l in links) if (NavMesh.IsLinkValid(l)) NavMesh.RemoveLink(l);
        links.Clear();
        if (Instance == this) { Instance = null; AgentTypeId = -1; NavMeshReady = false; }
    }

    /// <summary>Зомби в ближайшей к pos точке NavMesh (для призыва боссом). null — некуда поставить.</summary>
    public Zombie SpawnAt(Vector3 pos)
    {
        if (zombiePrefabs == null || zombiePrefabs.Length == 0) return null;
        var prefab = zombiePrefabs[Random.Range(0, zombiePrefabs.Length)];
        if (prefab == null) return null;
        var filter = new NavMeshQueryFilter { agentTypeID = agentTypeId, areaMask = NavMesh.AllAreas };
        if (!NavMesh.SamplePosition(pos, out NavMeshHit hit, 2f, filter)) return null;
        Vector3 look = player != null ? player.position - hit.position : Random.insideUnitSphere;
        look.y = 0f;
        if (look.sqrMagnitude < 1e-4f) look = Vector3.forward;
        return Zombie.Create(prefab, zombieAnimator, hit.position, Quaternion.LookRotation(look), agentTypeId);
    }

    public Zombie Spawn()
    {
        if (zombiePrefabs == null || zombiePrefabs.Length == 0) return null;
        GameObject prefab = null;
        for (int i = 0; i < 10 && prefab == null; i++) prefab = zombiePrefabs[Random.Range(0, zombiePrefabs.Length)];
        if (prefab == null) return null;
        if (!FindSpawnPoint(out Vector3 pos)) return null;

        Vector3 look = player != null ? player.position - pos : Random.insideUnitSphere;
        look.y = 0f;
        if (look.sqrMagnitude < 1e-4f) look = Vector3.forward;
        return Zombie.Create(prefab, zombieAnimator, pos, Quaternion.LookRotation(look), agentTypeId);
    }

    bool FindSpawnPoint(out Vector3 pos)
    {
        pos = Vector3.zero;
        var filter = new NavMeshQueryFilter { agentTypeID = agentTypeId, areaMask = NavMesh.AllAreas };
        bool hasTarget = false;
        Vector3 target = Vector3.zero;
        if (player != null && NavMesh.SamplePosition(player.position, out NavMeshHit ph, 3f, filter))
        {
            hasTarget = true;
            target = ph.position;
        }

        const int attempts = 40;
        for (int i = 0; i < attempts; i++)
        {
            Vector3 c;
            if (spawnPoints != null && spawnPoints.Length > 0)
            {
                var sp = spawnPoints[Random.Range(0, spawnPoints.Length)];
                if (sp == null) continue;
                c = sp.position;
            }
            else
                c = new Vector3(Random.Range(spawnArea.min.x, spawnArea.max.x), spawnArea.center.y, Random.Range(spawnArea.min.z, spawnArea.max.z));

            if (!NavMesh.SamplePosition(c, out NavMeshHit hit, 1.5f, filter)) continue;
            Vector3 p = hit.position;
            if (player != null)
            {
                Vector3 d = p - player.position; d.y = 0f;
                if (d.magnitude < minPlayerDistance) continue;
            }
            // Первые попытки — только вне поля зрения игрока: зомби не возникают из воздуха у него на глазах.
            if (i < attempts * 2 / 3 && IsVisible(p)) continue;
            if (hasTarget && (!NavMesh.CalculatePath(p, target, filter, path) || path.status != NavMeshPathStatus.PathComplete)) continue;
            if (Crowded(p)) continue;
            pos = p;
            return true;
        }
        return false;
    }

    bool IsVisible(Vector3 p)
    {
        if (playerCamera == null) return false;
        Vector3 chest = p + Vector3.up * 1.2f;
        Vector3 vp = playerCamera.WorldToViewportPoint(chest);
        if (vp.z <= 0f || vp.x < -0.1f || vp.x > 1.1f || vp.y < -0.1f || vp.y > 1.1f) return false;
        Vector3 from = playerCamera.transform.position;
        Vector3 to = chest - from;
        // Что-то загораживает точку — значит, не видно.
        foreach (var h in Physics.RaycastAll(from, to.normalized, to.magnitude - 0.1f, ~0, QueryTriggerInteraction.Ignore))
            if (player == null || !h.transform.IsChildOf(player)) return false;
        return true;
    }

    static bool Crowded(Vector3 p)
    {
        foreach (var z in Zombie.All)
            if (z != null && !z.IsDead && (z.transform.position - p).sqrMagnitude < 1.2f * 1.2f) return true;
        return false;
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(0.8f, 0.1f, 0.1f, 0.5f);
        Gizmos.DrawWireCube(spawnArea.center, spawnArea.size);
    }
}
