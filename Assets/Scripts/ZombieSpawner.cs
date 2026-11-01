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
    [Tooltip("Модели зомби (Assets/Characters/Zombie_*.glb). Для каждого нового зомби выбирается случайная.")]
    public GameObject[] zombiePrefabs;

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

    void Start()
    {
        path = new NavMeshPath();
        var fpc = FindAnyObjectByType<FirstPersonController>();
        if (fpc != null)
        {
            player = fpc.transform;
            playerCamera = fpc.GetComponentInChildren<Camera>();
        }
        if (bakeNavMeshOnStart) BakeNavMesh();

        for (int i = 0; i < initialCount && Zombie.AliveCount < maxAlive; i++) Spawn();
        timer = spawnInterval;
    }

    void Update()
    {
        timer -= Time.deltaTime;
        if (timer > 0f) return;
        timer = spawnInterval;
        if (Zombie.AliveCount < maxAlive) Spawn();
    }

    void BakeNavMesh()
    {
        // Собственный тип агента: ступенька ниже, чем у стандартного Humanoid (0,75 м), — иначе зомби забираются на столы.
        var settings = NavMesh.CreateSettings();
        settings.agentRadius = agentRadius;
        settings.agentHeight = 1.8f;
        settings.agentClimb = agentClimb;
        settings.agentSlope = 46f; // пандус лестницы — 45°
        agentTypeId = settings.agentTypeID;

        // Подвижные объекты в NavMesh не попадают: под игроком и оружием не должно быть дыр.
        var markups = new List<NavMeshBuildMarkup>();
        void Ignore(Transform t) { if (t != null) markups.Add(new NavMeshBuildMarkup { root = t, ignoreFromBuild = true, applyToChildren = true }); }
        Ignore(player);
        foreach (var w in WeaponPickup.All) if (w != null) Ignore(w.transform);
        foreach (var z in Zombie.All) if (z != null) Ignore(z.transform);
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
        Debug.Log($"ZombieSpawner: NavMesh запечён за {(Time.realtimeSinceStartup - t0):0.00} с ({sources.Count} объектов).");
    }

    void OnDestroy()
    {
        if (navMesh.valid) NavMesh.RemoveNavMeshData(navMesh);
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
        return Zombie.Create(prefab, pos, Quaternion.LookRotation(look), agentTypeId);
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
