using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Босс «Слепой Охотник» (Assets/Characters/BlindHunter.glb) — живёт в подвале под кухней.
/// Модель без скелета: CanteenSceneBuilder строит для неё процедурный скелет (кости Hips, Spine, Chest, Head, руки, ноги)
/// и привязывает к нему вершины, а этот компонент анимирует кости из кода.
///
/// Сильнее любого зомби: 1800 здоровья (у зомби — 100), бронированное тело, быстрее ходит, и пять типов атак:
///  • удар когтями — серия из двух размашистых ударов вблизи;
///  • удар о землю — обеими руками, ударная волна бьёт всех вокруг и отбрасывает (от неё можно уйти прыжком);
///  • таран — разбег по прямой; врезавшись в стену, босс оглушён и получает больше урона;
///  • бросок обломка — по игроку вдалеке (в том числе стоящему в дверях);
///  • рёв — на 70 % и 35 % здоровья призывает зомби. После половины здоровья босс в ярости: быстрее и злее.
/// Голова — уязвимое место. Спит, пока игрок не войдёт в подвал; за пределы подвала не выходит.
/// </summary>
[DefaultExecutionOrder(50)]
public class ZombieBoss : MonoBehaviour
{
    public static readonly List<ZombieBoss> All = new List<ZombieBoss>();
    /// <summary>Босс побеждён (попадает в сохранение).</summary>
    public static bool Defeated { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { Defeated = false; All.Clear(); Woke = Summoned = EnragedEvent = Slammed = StunnedEvent = Died = null; }

    public string bossName = "Декан";

    /// <summary>События для сюжета (реплики Декана и Шапика).</summary>
    public static event System.Action<ZombieBoss> Woke, Summoned, EnragedEvent, Slammed, StunnedEvent, Died;
    /// <summary>Пока true, проснувшийся босс стоит и не нападает (идёт катсцена).</summary>
    [System.NonSerialized] public bool holdForDialogue;
    public Vector3 HeadPosition => head != null ? head.position : transform.position + Vector3.up * 2.5f;

    [Header("Здоровье")]
    public float maxHealth = 1800f;
    public float headMultiplier = 2.5f;
    public float torsoMultiplier = 0.7f;
    public float limbMultiplier = 0.45f;
    public float handMultiplier = 0.3f;
    [Tooltip("Множитель урона, пока босс оглушён (врезался в стену).")]
    public float stunnedDamageMultiplier = 1.6f;
    [Tooltip("Восстановление здоровья в секунду, пока игрок за пределами подвала.")]
    public float leashRegen = 12f;

    [Header("Движение")]
    public float walkSpeed = 2.4f;
    [Tooltip("Множитель скорости и темпа атак в ярости (меньше половины здоровья).")]
    public float enragedSpeed = 1.3f;
    public float turnRate = 150f;

    [Header("Арена")]
    [Tooltip("Подвал: босс просыпается, когда игрок входит сюда, и не выходит за эти границы.")]
    public Bounds arena = new Bounds(Vector3.zero, new Vector3(30f, 4f, 12f));

    [Header("Удар когтями")]
    public float swipeDamage = 32f;
    public float swipeRange = 3f;

    [Header("Удар о землю")]
    public float slamDamage = 50f;
    public float slamRadius = 4.6f;
    public float slamCooldown = 5f;

    [Header("Таран")]
    public float chargeDamage = 55f;
    public float chargeSpeed = 11f;
    public float chargeMaxDistance = 15f;
    public float chargeCooldown = 8f;
    public float stunTime = 2.4f;

    [Header("Бросок обломка")]
    public float throwDamage = 30f;
    public float throwCooldown = 4f;

    [Header("Рёв и призыв зомби")]
    [Tooltip("Доли здоровья, на которых босс ревёт и зовёт зомби.")]
    public float[] summonAt = { 0.7f, 0.35f };
    public int summonCount = 3;
    [Range(0f, 1f)] public float enrageAt = 0.5f;

    [Header("Добыча")]
    public int coinReward = 200;

    public float Health { get; private set; }
    public bool IsDead { get; private set; }
    public bool IsAwake => state != State.Dormant;
    bool Enraged => Health < maxHealth * enrageAt;
    float Tempo => Enraged ? enragedSpeed : 1f;

    enum State { Dormant, Waking, Chase, Return, Swipe, Slam, ChargeWindup, Charging, Stunned, Throw, Roar, Dead }
    State state = State.Dormant;
    float clock, decisionTimer, globalCd, slamCd, chargeCd, throwCd = 2f;
    int nextSummon;
    bool pendingRoar, pendingSummon, roarSummons, hitDone, hitDone2;

    // Ссылки.
    Transform pivot, player, playerCam;
    PlayerHealth playerHealth;
    FirstPersonController fpc;
    CharacterController playerCC;
    NavMeshAgent agent;
    AudioSource audioSource;
    SkinnedMeshRenderer skin;
    readonly List<Collider> hitboxes = new List<Collider>();

    // Кости.
    Transform hips, spine, chest, head, headEnd;
    readonly Transform[] upperArm = new Transform[2], forearm = new Transform[2], hand = new Transform[2], handEnd = new Transform[2];
    readonly Transform[] thigh = new Transform[2], shin = new Transform[2], foot = new Transform[2], footEnd = new Transform[2];
    Vector3 hipsRest;

    // Поза: углы в градусах (см. Pose).
    Pose pose = new Pose(), target = new Pose();
    float walkPhase, flinch, lastStepSin;
    Vector3 flinchDir = Vector3.forward, lastPos, chargeDir, home;
    float chargeTravel, chargeBlocked;
    BossRock heldRock;
    float healthShown;
    float bannerUntil;
    float deathClock;
    bool crumbling, rewardDropped;
    MaterialPropertyBlock mpb;
    readonly List<(int index, string prop, Color color)> tint = new List<(int, string, Color)>();
    readonly List<ParticleSystem> ashSystems = new List<ParticleSystem>();

    // ---------- Подготовка ----------

    void OnEnable() => All.Add(this);
    void OnDisable() => All.Remove(this);

    void Awake()
    {
        Health = maxHealth;
        healthShown = maxHealth;
        pivot = transform.Find("Pivot") ?? transform;
        skin = GetComponentInChildren<SkinnedMeshRenderer>();
        if (skin != null) skin.updateWhenOffscreen = true;

        hips = Bone("Hips"); spine = Bone("Spine"); chest = Bone("Chest"); head = Bone("Head"); headEnd = Bone("Head_End");
        string[] sides = { "L", "R" };
        for (int i = 0; i < 2; i++)
        {
            string s = sides[i];
            upperArm[i] = Bone("UpperArm." + s); forearm[i] = Bone("Forearm." + s); hand[i] = Bone("Hand." + s); handEnd[i] = Bone("Hand." + s + "_End");
            thigh[i] = Bone("Thigh." + s); shin[i] = Bone("Shin." + s); foot[i] = Bone("Foot." + s); footEnd[i] = Bone("Foot." + s + "_End");
        }
        if (hips == null || spine == null || chest == null || head == null)
            Debug.LogWarning("ZombieBoss: у модели нет процедурного скелета — пересоберите сцену (Tools → Canteen → Rebuild Scene).");
        if (hips != null) hipsRest = hips.localPosition;

        BuildHitboxes();
        var rb = GetComponent<Rigidbody>();
        if (rb == null) rb = gameObject.AddComponent<Rigidbody>();
        rb.isKinematic = true;

        audioSource = gameObject.AddComponent<AudioSource>();
        audioSource.spatialBlend = 1f;
        audioSource.minDistance = 4f;
        audioSource.maxDistance = 45f;
        audioSource.rolloffMode = AudioRolloffMode.Linear;
        audioSource.playOnAwake = false;

        home = transform.position;
        lastPos = transform.position;
        SetDormantPose(pose);
    }

    Transform Bone(string n)
    {
        foreach (var t in GetComponentsInChildren<Transform>(true)) if (t.name == n) return t;
        return null;
    }

    void BuildHitboxes()
    {
        void Seg(Transform a, Transform b, float r, float mult, ZombieHitbox.Part part, bool isHead = false)
        {
            if (a == null || b == null) return;
            Vector3 p0 = a.position, p1 = b.position, d = p1 - p0;
            if (d.sqrMagnitude < 1e-6f) return;
            var go = new GameObject("Hitbox_" + a.name);
            go.transform.SetParent(a, false);
            go.transform.SetPositionAndRotation((p0 + p1) * 0.5f, Quaternion.LookRotation(d, Mathf.Abs(Vector3.Dot(d.normalized, Vector3.up)) > 0.95f ? Vector3.forward : Vector3.up));
            float s = Mathf.Max(1e-4f, go.transform.lossyScale.x);
            Collider col;
            if (isHead)
            {
                var sc = go.AddComponent<SphereCollider>();
                sc.radius = r / s;
                col = sc;
            }
            else
            {
                var c = go.AddComponent<CapsuleCollider>();
                c.direction = 2;
                c.radius = r / s;
                c.height = (d.magnitude + r * 2f) / s;
                col = c;
            }
            var hb = go.AddComponent<ZombieHitbox>();
            hb.boss = this;
            hb.multiplier = mult;
            hb.isHead = isHead;
            hb.part = part;
            hitboxes.Add(col);
        }
        Seg(hips, spine, 0.34f, torsoMultiplier, ZombieHitbox.Part.Torso);
        Seg(spine, chest, 0.37f, torsoMultiplier, ZombieHitbox.Part.Torso);
        Seg(chest, head, 0.42f, torsoMultiplier, ZombieHitbox.Part.Torso);
        Seg(head, headEnd, 0.27f, headMultiplier, ZombieHitbox.Part.Head, true);
        for (int i = 0; i < 2; i++)
        {
            Seg(upperArm[i], forearm[i], 0.15f, limbMultiplier, ZombieHitbox.Part.UpperArm);
            Seg(forearm[i], hand[i], 0.13f, limbMultiplier, ZombieHitbox.Part.Forearm);
            Seg(hand[i], handEnd[i], 0.14f, handMultiplier, ZombieHitbox.Part.Forearm);
            Seg(thigh[i], shin[i], 0.17f, limbMultiplier, ZombieHitbox.Part.Thigh);
            Seg(shin[i], foot[i], 0.15f, limbMultiplier, ZombieHitbox.Part.Shin);
            Seg(foot[i], footEnd[i], 0.12f, handMultiplier, ZombieHitbox.Part.Shin);
        }
    }

    /// <summary>Сохранение: босс уже побеждён — убрать его из сцены.</summary>
    public static void RemoveDefeated()
    {
        Defeated = true;
        foreach (var b in All.ToArray()) if (b != null) Destroy(b.gameObject);
    }

    // ---------- Урон ----------

    public void TakeHit(float damage, Vector3 point, Vector3 direction, ZombieHitbox part)
    {
        if (IsDead) return;
        float mult = part != null ? part.multiplier : 1f;
        if (state == State.Stunned) mult *= stunnedDamageMultiplier;
        float before = Health;
        Health -= damage * mult;

        Vector3 flat = Vector3.ProjectOnPlane(direction, Vector3.up);
        if (flat.sqrMagnitude > 1e-4f) flinchDir = flat.normalized;
        flinch = Mathf.Min(1f, flinch + (part != null && part.isHead ? 0.7f : 0.3f));

        // Спящего босса выстрел будит.
        if (state == State.Dormant) Wake();

        while (nextSummon < summonAt.Length && Health <= maxHealth * summonAt[nextSummon] && Health > 0f)
        {
            nextSummon++;
            pendingRoar = pendingSummon = true;
        }
        if (before >= maxHealth * enrageAt && Health < maxHealth * enrageAt && Health > 0f) { pendingRoar = true; EnragedEvent?.Invoke(this); }
        if (Health <= 0f) Die();
    }

    // ---------- Поведение ----------

    void FindPlayer()
    {
        if (player != null) return;
        fpc = FindAnyObjectByType<FirstPersonController>();
        if (fpc == null) return;
        player = fpc.transform;
        playerHealth = fpc.GetComponent<PlayerHealth>();
        playerCC = fpc.GetComponent<CharacterController>();
        var cam = fpc.GetComponentInChildren<Camera>();
        playerCam = cam != null ? cam.transform : player;
    }

    bool PlayerInArena => player != null && arena.Contains(player.position + Vector3.up * 0.5f);

    void EnsureAgent()
    {
        if (agent != null || ZombieSpawner.AgentTypeId < 0) return;
        var filter = new NavMeshQueryFilter { agentTypeID = ZombieSpawner.AgentTypeId, areaMask = NavMesh.AllAreas };
        if (!NavMesh.SamplePosition(transform.position, out NavMeshHit hit, 3f, filter)) return;
        transform.position = hit.position;
        agent = gameObject.AddComponent<NavMeshAgent>();
        agent.agentTypeID = ZombieSpawner.AgentTypeId;
        agent.radius = 0.55f;
        agent.height = 2.6f;
        agent.baseOffset = 0f;
        agent.speed = walkSpeed;
        agent.acceleration = 10f;
        agent.angularSpeed = 0f;
        agent.updateRotation = false;
        agent.stoppingDistance = 1.6f;
        agent.autoBraking = true;
        agent.obstacleAvoidanceType = ObstacleAvoidanceType.HighQualityObstacleAvoidance;
        agent.avoidancePriority = 5; // зомби уступают дорогу
        home = hit.position;
    }

    void Update()
    {
        float dt = Time.deltaTime;
        if (dt <= 0f) return;
        FindPlayer();
        healthShown = Mathf.MoveTowards(healthShown, Mathf.Max(0f, Health), maxHealth * dt * 0.6f);
        flinch = Mathf.MoveTowards(flinch, 0f, dt * 3f);

        if (IsDead) { UpdateDeath(dt); return; }
        EnsureAgent();

        globalCd -= dt * Tempo; slamCd -= dt * Tempo; chargeCd -= dt * Tempo; throwCd -= dt * Tempo;
        clock += dt * (state == State.Charging || state == State.Stunned || state == State.Dormant ? 1f : Tempo);

        bool playerDead = playerHealth != null && playerHealth.IsDead;
        Vector3 toPlayer = player != null ? player.position - transform.position : Vector3.zero;
        toPlayer.y = 0f;
        bool decide = (decisionTimer -= dt) <= 0f;
        if (decide) decisionTimer = 0.25f;
        float dist = toPlayer.magnitude;
        float angle = dist > 0.01f ? Vector3.SignedAngle(transform.forward, toPlayer, Vector3.up) : 0f;
        bool onMesh = agent != null && agent.isOnNavMesh;

        switch (state)
        {
            case State.Dormant:
                SetDormantPose(target);
                if (player != null && !playerDead && PlayerInArena && dist < 14f) Wake();
                break;

            case State.Waking:
                Face(toPlayer, 60f, dt);
                PoseWake(clock);
                if (clock > 0.9f && !hitDone) { hitDone = true; Roar(0f); }
                if (clock > 3f && !holdForDialogue) ToChase();
                break;

            case State.Chase:
            case State.Return:
                if (player == null) break;
                if (playerDead) { state = State.Return; }
                else if (PlayerInArena) state = State.Chase;
                else if (state == State.Chase) state = State.Return;

                Vector3 dest = state == State.Chase ? ClampToArena(player.position) : home;
                if (onMesh)
                {
                    agent.isStopped = false;
                    agent.speed = walkSpeed * Tempo;
                    agent.stoppingDistance = state == State.Chase ? 1.6f : 0.3f;
                    if (decide || !agent.hasPath) agent.SetDestination(dest);
                    Vector3 dv = agent.desiredVelocity; dv.y = 0f;
                    if (dv.sqrMagnitude > 0.05f) Face(dv, turnRate, dt);
                    else if (state == State.Chase) Face(toPlayer, turnRate, dt);
                }
                else if (state == State.Chase) Face(toPlayer, turnRate, dt);

                if (state == State.Return)
                {
                    Health = Mathf.Min(maxHealth, Health + leashRegen * dt);
                    // Игрок стоит в дверях и стреляет — получает обломком.
                    if (!playerDead && throwCd <= 0f && dist < 22f && CanSee()) StartAct(State.Throw);
                }
                else if (decide) Decide(dist, angle);
                break;

            case State.Swipe: UpdateSwipe(toPlayer, dist, angle, dt); break;
            case State.Slam: UpdateSlam(toPlayer, dt); break;
            case State.ChargeWindup: UpdateChargeWindup(toPlayer, dt); break;
            case State.Charging: UpdateCharging(dt); break;
            case State.Stunned:
                PoseStunned(clock);
                if (clock > stunTime) ToChase(0.4f);
                break;
            case State.Throw: UpdateThrow(toPlayer, dist, dt); break;
            case State.Roar: UpdateRoar(toPlayer, dt); break;
        }
        if (onMesh && state != State.Chase && state != State.Return && state != State.Charging) agent.isStopped = true;

        // Шаги: по фактической скорости.
        Vector3 v = transform.position - lastPos; v.y = 0f;
        lastPos = transform.position;
        float speed = v.magnitude / dt;
        if (state == State.Chase || state == State.Return || state == State.Charging)
            Locomotion(speed, dt, state == State.Charging);
    }

    void ToChase(float cd = 0.8f)
    {
        state = PlayerInArena ? State.Chase : State.Return;
        clock = 0f;
        globalCd = Mathf.Max(globalCd, cd);
        DropHeldRock();
    }

    void StartAct(State s)
    {
        state = s;
        clock = 0f;
        hitDone = hitDone2 = false;
        if (agent != null && agent.isOnNavMesh) { agent.isStopped = true; agent.velocity = Vector3.zero; }
        switch (s)
        {
            case State.Slam: slamCd = slamCooldown; break;
            case State.ChargeWindup: chargeCd = chargeCooldown; PlaySound(BossSounds.Roar(), 0.7f, 1.25f); break;
            case State.Throw: throwCd = throwCooldown; break;
            case State.Swipe: PlaySound(BossSounds.Growl(), 0.6f, Random.Range(0.9f, 1.1f)); break;
        }
    }

    void Wake()
    {
        if (state != State.Dormant) return;
        state = State.Waking;
        clock = 0f;
        hitDone = false;
        Woke?.Invoke(this);
    }

    void Decide(float dist, float angle)
    {
        if (globalCd > 0f || player == null) return;
        if (pendingRoar)
        {
            roarSummons = pendingSummon;
            pendingRoar = pendingSummon = false;
            StartAct(State.Roar);
            return;
        }
        bool see = CanSee();
        if (dist < swipeRange && Mathf.Abs(angle) < 55f)
        {
            if (slamCd <= 0f && Random.value < 0.35f) StartAct(State.Slam);
            else StartAct(State.Swipe);
        }
        else if (dist < slamRadius * 0.75f && slamCd <= 0f && Random.value < 0.25f) StartAct(State.Slam);
        else if (dist > 5.5f && dist < chargeMaxDistance && chargeCd <= 0f && see && Mathf.Abs(angle) < 70f && ClearRun() && Random.value < 0.45f)
            StartAct(State.ChargeWindup);
        else if (dist > 6.5f && throwCd <= 0f && see && Random.value < 0.35f) StartAct(State.Throw);
    }

    void Face(Vector3 dir, float rate, float dt)
    {
        dir.y = 0f;
        if (dir.sqrMagnitude < 1e-4f) return;
        transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(dir), rate * Tempo * dt);
    }

    Vector3 ClampToArena(Vector3 p)
    {
        Vector3 min = arena.min + new Vector3(0.8f, 0f, 0.8f), max = arena.max - new Vector3(0.8f, 0f, 0.8f);
        return new Vector3(Mathf.Clamp(p.x, min.x, max.x), p.y, Mathf.Clamp(p.z, min.z, max.z));
    }

    /// <summary>Босс «слышит» игрока: прямая между головой и игроком не перекрыта стенами.</summary>
    bool CanSee()
    {
        if (playerCam == null) return false;
        Vector3 from = transform.position + Vector3.up * 2.3f;
        Vector3 d = playerCam.position - from;
        float len = d.magnitude;
        foreach (var h in Physics.RaycastAll(from, d / len, len, ~0, QueryTriggerInteraction.Ignore))
        {
            if (h.transform.IsChildOf(transform) || h.transform.IsChildOf(player)) continue;
            if (h.collider.GetComponent<ZombieHitbox>() != null || h.rigidbody != null && !h.rigidbody.isKinematic) continue;
            return false;
        }
        return true;
    }

    bool ClearRun()
    {
        if (agent == null || !agent.isOnNavMesh) return false;
        var filter = new NavMeshQueryFilter { agentTypeID = agent.agentTypeID, areaMask = agent.areaMask };
        return !NavMesh.Raycast(transform.position, player.position, out _, filter);
    }

    /// <summary>Игрок в секторе перед боссом (угол и дальность) и на одной с ним высоте.</summary>
    bool PlayerInFront(float range, float maxAngle)
    {
        if (player == null || (playerHealth != null && playerHealth.IsDead)) return false;
        Vector3 to = player.position - transform.position;
        if (Mathf.Abs(to.y) > 1.8f) return false;
        to.y = 0f;
        return to.magnitude <= range && Vector3.Angle(transform.forward, to) <= maxAngle;
    }

    void HurtPlayer(float damage, Vector3 push)
    {
        if (playerHealth == null || playerHealth.IsDead) return;
        playerHealth.TakeDamage(damage, transform.position);
        if (fpc != null) { fpc.AddImpulse(push); fpc.AddShake(0.8f); }
    }

    // ---------- Атаки ----------

    void UpdateSwipe(Vector3 toPlayer, float dist, float angle, float dt)
    {
        if (clock < 0.45f || (clock > 0.6f && clock < 0.95f)) Face(toPlayer, 110f, dt);
        PoseSwipe(clock);
        if (!hitDone && clock >= 0.47f)
        {
            hitDone = true;
            PlaySound(BossSounds.Whoosh(), 0.8f, 1f);
            if (PlayerInFront(swipeRange * 1.15f, 75f)) HurtPlayer(swipeDamage, transform.right * -5f + transform.forward * 3f + Vector3.up * 1.5f);
        }
        if (!hitDone2 && clock >= 0.95f)
        {
            hitDone2 = true;
            PlaySound(BossSounds.Whoosh(), 0.8f, 0.9f);
            if (PlayerInFront(swipeRange * 1.15f, 75f)) HurtPlayer(swipeDamage, transform.right * 5f + transform.forward * 3f + Vector3.up * 1.5f);
        }
        if (clock >= 1.45f) ToChase(Enraged ? 0.4f : 0.8f);
    }

    void UpdateSlam(Vector3 toPlayer, float dt)
    {
        if (clock < 0.8f) Face(toPlayer, 70f, dt);
        PoseSlam(clock);
        if (!hitDone && clock >= 1.05f)
        {
            hitDone = true;
            Vector3 center = transform.position + transform.forward * 1.5f;
            Shockwave(center, slamRadius, slamDamage);
            Slammed?.Invoke(this);
        }
        if (clock >= 2.1f) ToChase(Enraged ? 0.5f : 1f);
    }

    void Shockwave(Vector3 center, float radius, float damage)
    {
        PlaySound(BossSounds.Slam(), 1f, Random.Range(0.9f, 1.05f));
        BossFx.Dust(center + Vector3.up * 0.05f, radius, 70, new Color(0.35f, 0.32f, 0.28f, 0.6f));
        BossFx.Debris(center + Vector3.up * 0.1f, 18);
        if (player != null)
        {
            Vector3 d = player.position - center;
            float h = Mathf.Abs(d.y);
            d.y = 0f;
            float dist = d.magnitude;
            if (fpc != null) fpc.AddShake(Mathf.Lerp(1.4f, 0f, dist / 18f));
            // Ударная волна идёт по полу: в прыжке её можно перепрыгнуть.
            bool airborne = fpc != null && !fpc.IsGrounded && player.position.y > center.y + 0.35f;
            if (dist < radius && h < 1.6f && !airborne)
            {
                float k = dist / radius;
                Vector3 outDir = dist > 0.1f ? d / dist : transform.forward;
                HurtPlayer(Mathf.Lerp(damage, damage * 0.35f, k), outDir * Mathf.Lerp(9f, 3f, k) + Vector3.up * 4f);
            }
        }
        // Ударная волна раскидывает предметы.
        foreach (var c in Physics.OverlapSphere(center, radius, ~0, QueryTriggerInteraction.Ignore))
        {
            var rb = c.attachedRigidbody;
            if (rb != null && !rb.isKinematic) rb.AddExplosionForce(5f, center, radius, 0.8f, ForceMode.Impulse);
        }
    }

    void UpdateChargeWindup(Vector3 toPlayer, float dt)
    {
        Face(toPlayer, 200f, dt);
        PoseChargeWindup(clock);
        if (clock >= 0.9f)
        {
            state = State.Charging;
            clock = 0f;
            chargeTravel = 0f;
            chargeBlocked = 0f;
            chargeDir = toPlayer.sqrMagnitude > 0.01f ? toPlayer.normalized : transform.forward;
            transform.rotation = Quaternion.LookRotation(chargeDir);
        }
    }

    void UpdateCharging(float dt)
    {
        // Первые доли секунды босс ещё доворачивает на игрока, дальше несётся по прямой.
        if (clock < 0.25f && player != null)
        {
            Vector3 to = player.position - transform.position; to.y = 0f;
            if (to.sqrMagnitude > 0.01f) chargeDir = Vector3.RotateTowards(chargeDir, to.normalized, 1.5f * dt, 0f);
            transform.rotation = Quaternion.LookRotation(chargeDir);
        }
        Vector3 before = transform.position;
        Vector3 step = chargeDir * chargeSpeed * dt;
        if (agent != null && agent.isOnNavMesh) agent.Move(step); else transform.position += step;
        Vector3 moved = transform.position - before; moved.y = 0f;
        chargeTravel += moved.magnitude;
        if (fpc != null && player != null && (player.position - transform.position).sqrMagnitude < 100f) fpc.AddShake(0.05f);

        if (PlayerInFront(1.9f, 70f))
        {
            HurtPlayer(chargeDamage, chargeDir * 11f + Vector3.up * 4.5f);
            PlaySound(BossSounds.Slam(), 0.8f, 1.2f);
            ToChase(1.2f);
            return;
        }
        // Упёрся в стену или колонну — оглушён.
        if (moved.magnitude < step.magnitude * 0.35f) chargeBlocked += dt; else chargeBlocked = 0f;
        if (chargeBlocked > 0.08f || !arena.Contains(transform.position + chargeDir * 0.8f + Vector3.up))
        {
            PlaySound(BossSounds.Slam(), 1f, 0.75f);
            BossFx.Dust(transform.position + chargeDir * 0.8f + Vector3.up * 1.5f, 1.5f, 40, new Color(0.4f, 0.38f, 0.35f, 0.6f));
            BossFx.Debris(transform.position + chargeDir * 0.8f + Vector3.up * 2f, 14);
            if (fpc != null && player != null) fpc.AddShake(Mathf.Lerp(1.2f, 0f, Vector3.Distance(player.position, transform.position) / 20f));
            state = State.Stunned;
            clock = 0f;
            StunnedEvent?.Invoke(this);
            return;
        }
        if (chargeTravel >= chargeMaxDistance || clock > 2.5f) ToChase(0.6f);
    }

    void UpdateThrow(Vector3 toPlayer, float dist, float dt)
    {
        if (clock < 0.8f) Face(toPlayer, 140f, dt);
        PoseThrow(clock);
        if (!hitDone && clock >= 0.3f)
        {
            hitDone = true;
            var h = hand[1] != null ? hand[1] : transform;
            heldRock = BossRock.Create(h);
        }
        if (!hitDone2 && clock >= 0.82f)
        {
            hitDone2 = true;
            if (heldRock != null && playerCam != null)
            {
                PlaySound(BossSounds.Whoosh(), 0.9f, 0.7f);
                Vector3 start = heldRock.transform.position;
                Vector3 aim = player.position + Vector3.up * 1.1f;
                if (playerCC != null) aim += new Vector3(playerCC.velocity.x, 0f, playerCC.velocity.z) * 0.35f;
                float t = Mathf.Clamp(Vector3.Distance(start, aim) / 17f, 0.35f, 1.4f);
                Vector3 vel = (aim - start) / t + Vector3.up * (0.5f * BossRock.Gravity * t);
                heldRock.Launch(vel, throwDamage, this);
                heldRock = null;
            }
        }
        if (clock >= 1.35f) ToChase(Enraged ? 0.5f : 1f);
    }

    void DropHeldRock()
    {
        if (heldRock != null) { Destroy(heldRock.gameObject); heldRock = null; }
    }

    void UpdateRoar(Vector3 toPlayer, float dt)
    {
        Face(toPlayer, 60f, dt);
        PoseRoar(clock);
        if (!hitDone && clock >= 0.35f)
        {
            hitDone = true;
            Roar(1f);
        }
        if (!hitDone2 && clock >= 0.8f)
        {
            hitDone2 = true;
            if (roarSummons) { Summon(); Summoned?.Invoke(this); }
        }
        if (clock >= 2.4f) ToChase(0.3f);
    }

    void Roar(float shake)
    {
        PlaySound(BossSounds.Roar(), 1f, Random.Range(0.92f, 1.02f));
        if (fpc != null && player != null)
            fpc.AddShake(Mathf.Lerp(0.9f, 0.1f, Vector3.Distance(player.position, transform.position) / 25f) * Mathf.Max(0.5f, shake));
    }

    void Summon()
    {
        var spawner = ZombieSpawner.Instance;
        if (spawner == null) return;
        for (int i = 0; i < summonCount; i++)
        {
            float a = Random.Range(0f, Mathf.PI * 2f);
            Vector3 p = transform.position + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * Random.Range(3.5f, 6.5f);
            if (!arena.Contains(p + Vector3.up)) p = ClampToArena(p);
            var z = spawner.SpawnAt(p);
            if (z != null) BossFx.Dust(z.transform.position + Vector3.up * 0.1f, 1f, 25, new Color(0.2f, 0.18f, 0.16f, 0.6f));
        }
    }

    // ---------- Смерть ----------

    void Die()
    {
        IsDead = true;
        state = State.Dead;
        Health = 0f;
        deathClock = 0f;
        Defeated = true;
        DropHeldRock();
        foreach (var c in hitboxes) if (c != null) c.enabled = false;
        if (agent != null) agent.enabled = false;
        PlaySound(BossSounds.Roar(), 1f, 0.7f);
        bannerUntil = Time.time + 6f;
        Died?.Invoke(this);
    }

    void UpdateDeath(float dt)
    {
        deathClock += dt;
        PoseDeath(deathClock);
        // Падает вперёд — вокруг ступней.
        float fall = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((deathClock - 1.6f) / 0.9f));
        pivot.localRotation = Quaternion.Euler(78f * fall, 0f, 0f);
        if (!rewardDropped && deathClock > 2.5f)
        {
            rewardDropped = true;
            PlaySound(BossSounds.Slam(), 1f, 0.6f);
            if (fpc != null && player != null) fpc.AddShake(Mathf.Lerp(1f, 0f, Vector3.Distance(player.position, transform.position) / 20f));
            BossFx.Dust(transform.position + transform.forward * 1.5f, 3f, 60, new Color(0.3f, 0.28f, 0.25f, 0.6f));
            DropReward();
        }
        if (deathClock > 3.2f && !crumbling) StartCrumble();
        if (crumbling)
        {
            float k = Mathf.Clamp01((deathClock - 3.2f) / 2.2f);
            if (skin != null)
            {
                foreach (var t in tint)
                {
                    skin.GetPropertyBlock(mpb, t.index);
                    mpb.SetColor(t.prop, Color.Lerp(t.color, new Color(0.15f, 0.14f, 0.13f), Mathf.Clamp01(k * 2.5f)));
                    skin.SetPropertyBlock(mpb, t.index);
                }
            }
            float f = Mathf.Clamp01((k - 0.25f) / 0.75f);
            pivot.localScale = Vector3.Scale(pivotScale, new Vector3(1f + 0.2f * f, Mathf.Max(0.02f, 1f - f * f), 1f + 0.2f * f));
            if (k >= 1f && skin != null && skin.enabled)
            {
                skin.enabled = false;
                foreach (var ps in ashSystems) if (ps != null) ps.Stop(false, ParticleSystemStopBehavior.StopEmitting);
            }
        }
        if (deathClock > 9f && Time.time > bannerUntil) Destroy(gameObject);
    }

    Vector3 pivotScale = Vector3.one;

    void DropReward()
    {
        Vector3 c = transform.position + transform.forward * 1.2f + Vector3.up * 0.8f;
        int piles = 5;
        for (int i = 0; i < piles; i++)
        {
            Vector3 off = Quaternion.Euler(0f, i * 360f / piles, 0f) * Vector3.forward * 0.6f;
            ItemPickup.Spawn(ItemDatabase.Coin, coinReward / piles + (i == 0 ? coinReward % piles : 0), c + off);
        }
        ItemPickup.Spawn(ItemDatabase.Medkit, 2, c + Vector3.up * 0.2f);
        // Лом — им вскрывается клетка Шапика: выкатывается из кармана, подсвечен жёлтым, берётся в руки.
        CrowbarWeapon.Create(transform.position + transform.forward * 2.2f + transform.right * 0.6f, transform.eulerAngles.y + 70f);
        ItemPickup.Spawn(ItemDatabase.Ammo545, 30, c + transform.right * 0.8f);
        ItemPickup.Spawn(ItemDatabase.Ammo12, 10, c - transform.right * 0.8f);
    }

    void StartCrumble()
    {
        crumbling = true;
        pivotScale = pivot.localScale;
        mpb = new MaterialPropertyBlock();
        if (skin == null) return;
        var mats = skin.sharedMaterials;
        for (int i = 0; i < mats.Length; i++)
        {
            var m = mats[i];
            if (m == null) continue;
            string prop = m.HasProperty("baseColorFactor") ? "baseColorFactor" : m.HasProperty("_BaseColor") ? "_BaseColor" : null;
            if (prop != null) tint.Add((i, prop, m.GetColor(prop)));
        }
        if (Zombie.AshFlakeMaterial != null) ashSystems.Add(BossFx.Ash(skin, Zombie.AshFlakeMaterial, true, 900, 2.2f, transform));
        if (Zombie.AshDustMaterial != null) ashSystems.Add(BossFx.Ash(skin, Zombie.AshDustMaterial, false, 90, 2.2f, transform));
    }

    // ---------- Звук ----------

    void PlaySound(AudioClip clip, float volume, float pitch)
    {
        if (clip == null || audioSource == null) return;
        audioSource.pitch = pitch;
        audioSource.PlayOneShot(clip, volume * GameSettings.EffectsVolume);
    }

    // ---------- Позы ----------

    /// <summary>Поза скелета: углы в градусах. Руки и ноги: [0] — левая, [1] — правая.</summary>
    class Pose
    {
        public float hipsDrop, hipsPitch, spinePitch, chestPitch, chestYaw, chestRoll, headPitch, headYaw;
        public float[] armPitch = new float[2], armRoll = new float[2], elbow = new float[2], wrist = new float[2];
        public float[] legPitch = new float[2], knee = new float[2], ankle = new float[2];

        public void CopyFrom(Pose p)
        {
            hipsDrop = p.hipsDrop; hipsPitch = p.hipsPitch; spinePitch = p.spinePitch; chestPitch = p.chestPitch;
            chestYaw = p.chestYaw; chestRoll = p.chestRoll; headPitch = p.headPitch; headYaw = p.headYaw;
            for (int i = 0; i < 2; i++)
            {
                armPitch[i] = p.armPitch[i]; armRoll[i] = p.armRoll[i]; elbow[i] = p.elbow[i]; wrist[i] = p.wrist[i];
                legPitch[i] = p.legPitch[i]; knee[i] = p.knee[i]; ankle[i] = p.ankle[i];
            }
        }

        public void MoveTowards(Pose t, float k)
        {
            hipsDrop = Mathf.Lerp(hipsDrop, t.hipsDrop, k); hipsPitch = Mathf.Lerp(hipsPitch, t.hipsPitch, k);
            spinePitch = Mathf.Lerp(spinePitch, t.spinePitch, k); chestPitch = Mathf.Lerp(chestPitch, t.chestPitch, k);
            chestYaw = Mathf.Lerp(chestYaw, t.chestYaw, k); chestRoll = Mathf.Lerp(chestRoll, t.chestRoll, k);
            headPitch = Mathf.Lerp(headPitch, t.headPitch, k); headYaw = Mathf.Lerp(headYaw, t.headYaw, k);
            for (int i = 0; i < 2; i++)
            {
                armPitch[i] = Mathf.Lerp(armPitch[i], t.armPitch[i], k); armRoll[i] = Mathf.Lerp(armRoll[i], t.armRoll[i], k);
                elbow[i] = Mathf.Lerp(elbow[i], t.elbow[i], k); wrist[i] = Mathf.Lerp(wrist[i], t.wrist[i], k);
                legPitch[i] = Mathf.Lerp(legPitch[i], t.legPitch[i], k); knee[i] = Mathf.Lerp(knee[i], t.knee[i], k);
                ankle[i] = Mathf.Lerp(ankle[i], t.ankle[i], k);
            }
        }
    }

    static float Ease(float a, float b, float t) => Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(a, b, t));

    /// <summary>Сгорбленная стойка: спина согнута, голова вытянута вперёд, длинные руки висят чуть впереди.</summary>
    void SetBase(Pose p)
    {
        p.hipsDrop = 0.04f; p.hipsPitch = 0f; p.spinePitch = 8f; p.chestPitch = 10f; p.chestYaw = 0f; p.chestRoll = 0f;
        p.headPitch = -14f; p.headYaw = 0f;
        for (int i = 0; i < 2; i++)
        {
            p.armPitch[i] = 14f; p.armRoll[i] = 0f; p.elbow[i] = 18f; p.wrist[i] = 10f;
            p.legPitch[i] = 6f; p.knee[i] = 14f; p.ankle[i] = -8f;
        }
    }

    void SetDormantPose(Pose p)
    {
        // Сидит на корточках, опустив голову, и покачивается — «прислушивается».
        SetBase(p);
        float t = Time.time;
        p.hipsDrop = 0.55f; p.spinePitch = 38f; p.chestPitch = 22f; p.headPitch = 25f + Mathf.Sin(t * 0.9f) * 6f;
        p.headYaw = Mathf.Sin(t * 0.37f) * 25f;
        p.chestYaw = Mathf.Sin(t * 0.5f) * 5f;
        for (int i = 0; i < 2; i++)
        {
            p.legPitch[i] = 70f; p.knee[i] = 115f; p.ankle[i] = -45f;
            p.armPitch[i] = 20f; p.armRoll[i] = -8f; p.elbow[i] = 30f;
        }
    }

    void Locomotion(float speed, float dt, bool run)
    {
        SetBase(target);
        float amount = Mathf.Clamp01(speed / (walkSpeed * 0.8f));
        float stride = run ? 2.6f : 1.7f;
        walkPhase += speed / stride * Mathf.PI * dt;
        float s = Mathf.Sin(walkPhase), c = Mathf.Cos(walkPhase);
        float swing = (run ? 38f : 26f) * amount;
        target.legPitch[0] += s * swing; target.legPitch[1] -= s * swing;
        target.knee[0] += Mathf.Max(0f, -c) * 55f * amount; target.knee[1] += Mathf.Max(0f, c) * 55f * amount;
        target.ankle[0] -= s * 10f * amount; target.ankle[1] += s * 10f * amount;
        target.hipsDrop += (1f - Mathf.Abs(c)) * 0.06f * amount;
        target.chestYaw = s * 9f * amount;
        target.chestRoll = c * 4f * amount;
        // Руки болтаются в противофазе ногам и волочатся.
        target.armPitch[0] += -s * 20f * amount; target.armPitch[1] += s * 20f * amount;
        target.elbow[0] += Mathf.Max(0f, s) * 15f; target.elbow[1] += Mathf.Max(0f, -s) * 15f;
        if (run)
        {
            target.spinePitch += 22f; target.chestPitch += 10f; target.headPitch -= 15f;
            for (int i = 0; i < 2; i++) { target.armPitch[i] -= 45f; target.armRoll[i] += 25f; }
        }
        else if (Enraged) { target.spinePitch += 8f; target.headPitch -= 6f; }

        // Тяжёлые шаги: удар о пол и лёгкая тряска рядом.
        if (amount > 0.3f && Mathf.Sign(s) != Mathf.Sign(lastStepSin))
        {
            PlaySound(BossSounds.Step(), run ? 0.7f : 0.45f, Random.Range(0.85f, 1.1f));
            if (fpc != null && player != null)
            {
                float d = Vector3.Distance(player.position, transform.position);
                if (d < 10f) fpc.AddShake((run ? 0.25f : 0.1f) * (1f - d / 10f));
            }
        }
        lastStepSin = s;
    }

    void PoseWake(float t)
    {
        // Встаёт с корточек и ревёт, запрокинув голову.
        SetDormantPose(target);
        var stand = new Pose(); SetBase(stand);
        var roar = new Pose(); SetRoarPose(roar, t);
        float up = Ease(0f, 0.8f, t);
        Blend(target, stand, up);
        Blend(target, roar, Ease(0.6f, 1f, t) * (1f - Ease(2.4f, 3f, t)));
    }

    void SetRoarPose(Pose p, float t)
    {
        SetBase(p);
        float shake = Mathf.Sin(t * 40f) * 2.5f;
        p.spinePitch = -8f; p.chestPitch = -18f; p.headPitch = -50f + shake; p.headYaw = shake;
        p.hipsDrop = 0.12f;
        for (int i = 0; i < 2; i++)
        {
            p.armPitch[i] = 35f; p.armRoll[i] = 55f; p.elbow[i] = 45f + shake; p.wrist[i] = -20f;
            p.legPitch[i] = 18f; p.knee[i] = 30f; p.ankle[i] = -12f;
        }
    }

    void PoseRoar(float t)
    {
        SetBase(target);
        var roar = new Pose(); SetRoarPose(roar, t);
        Blend(target, roar, Ease(0f, 0.35f, t) * (1f - Ease(2f, 2.4f, t)));
    }

    void PoseSwipe(float t)
    {
        // Правая лапа: замах вбок — удар поперёк; затем левая.
        SetBase(target);
        target.spinePitch = 18f; target.hipsDrop = 0.1f;
        for (int i = 0; i < 2; i++) { target.legPitch[i] = 15f; target.knee[i] = 28f; }
        int side; float wind, strike;
        if (t < 0.75f) { side = 1; wind = Ease(0f, 0.38f, t) * (1f - Ease(0.4f, 0.5f, t)); strike = Ease(0.38f, 0.5f, t) * (1f - Ease(0.6f, 0.8f, t)); }
        else { side = 0; wind = Ease(0.6f, 0.86f, t) * (1f - Ease(0.88f, 0.98f, t)); strike = Ease(0.86f, 0.98f, t) * (1f - Ease(1.1f, 1.4f, t)); }
        float dir = side == 1 ? 1f : -1f;
        target.chestYaw = dir * (35f * wind - 40f * strike);
        target.armPitch[side] = 14f + 60f * wind + 75f * strike;
        target.armRoll[side] = 75f * wind - 15f * strike;
        target.elbow[side] = 18f + 40f * wind - 10f * strike;
        target.wrist[side] = 10f - 30f * wind + 20f * strike;
        target.headPitch = -14f - 10f * strike;
        target.spinePitch += 12f * strike;
    }

    void PoseSlam(float t)
    {
        SetBase(target);
        float raise = Ease(0f, 0.9f, t) * (1f - Ease(0.92f, 1.05f, t));
        float down = Ease(0.92f, 1.05f, t) * (1f - Ease(1.6f, 2.1f, t));
        target.spinePitch = 8f - 22f * raise + 32f * down;
        target.chestPitch = 10f - 18f * raise + 20f * down;
        target.headPitch = -14f - 25f * raise + 10f * down;
        target.hipsDrop = 0.04f + 0.05f * raise + 0.4f * down;
        for (int i = 0; i < 2; i++)
        {
            target.armPitch[i] = 14f + 135f * raise + 60f * down;
            target.armRoll[i] = 15f * raise + 8f * down;
            target.elbow[i] = 18f + 25f * raise - 15f * down;
            target.wrist[i] = 10f + 20f * down;
            target.legPitch[i] = 6f + 45f * down;
            target.knee[i] = 14f + 75f * down;
            target.ankle[i] = -8f - 30f * down;
        }
    }

    void PoseChargeWindup(float t)
    {
        SetBase(target);
        float k = Ease(0f, 0.6f, t);
        float shake = Mathf.Sin(t * 50f) * 2f * k;
        target.hipsDrop = 0.04f + 0.28f * k;
        target.spinePitch = 8f + 30f * k; target.chestPitch = 10f + 12f * k; target.headPitch = -14f - 30f * k + shake;
        for (int i = 0; i < 2; i++)
        {
            target.armPitch[i] = 14f - 45f * k; target.armRoll[i] = 40f * k; target.elbow[i] = 18f + 20f * k;
            target.legPitch[i] = 6f + (i == 0 ? 45f : 10f) * k; target.knee[i] = 14f + (i == 0 ? 60f : 35f) * k;
        }
    }

    void PoseStunned(float t)
    {
        // Оглушён: падает на колено, мотает головой.
        SetBase(target);
        float k = Ease(0f, 0.35f, t) * (1f - Ease(stunTime - 0.5f, stunTime, t));
        target.hipsDrop = 0.04f + 0.6f * k;
        target.spinePitch = 8f + 32f * k; target.chestPitch = 10f + 15f * k;
        target.headPitch = -14f + 50f * k; target.headYaw = Mathf.Sin(t * 5f) * 25f * k;
        for (int i = 0; i < 2; i++)
        {
            target.legPitch[i] = 6f + (i == 0 ? 80f : 15f) * k; target.knee[i] = 14f + (i == 0 ? 95f : 110f) * k;
            target.ankle[i] = -8f - 30f * k;
            target.armPitch[i] = 14f - 10f * k; target.armRoll[i] = 6f * k; target.elbow[i] = 18f;
        }
    }

    void PoseThrow(float t)
    {
        // Подбирает обломок с пола, замахивается правой и бросает.
        SetBase(target);
        float grab = Ease(0f, 0.28f, t) * (1f - Ease(0.32f, 0.55f, t));
        float wind = Ease(0.32f, 0.7f, t) * (1f - Ease(0.72f, 0.85f, t));
        float rel = Ease(0.72f, 0.85f, t) * (1f - Ease(1f, 1.35f, t));
        target.spinePitch = 8f + 45f * grab - 18f * wind + 22f * rel;
        target.hipsDrop = 0.04f + 0.25f * grab;
        target.chestYaw = 30f * wind - 28f * rel;
        target.armPitch[1] = 14f + 30f * grab + 150f * wind + 60f * rel;
        target.elbow[1] = 18f + 70f * wind - 15f * rel;
        target.armRoll[1] = 20f * wind;
        target.headPitch = -14f + 20f * grab - 10f * wind;
        for (int i = 0; i < 2; i++) { target.legPitch[i] += 25f * grab; target.knee[i] += 45f * grab; }
    }

    void PoseDeath(float t)
    {
        SetBase(target);
        float back = Ease(0f, 0.4f, t) * (1f - Ease(0.6f, 1.1f, t));
        float kneel = Ease(0.7f, 1.4f, t);
        target.spinePitch = 8f - 25f * back + 30f * kneel;
        target.headPitch = -14f - 40f * back + 40f * kneel;
        target.hipsDrop = 0.04f + 0.6f * kneel;
        for (int i = 0; i < 2; i++)
        {
            target.legPitch[i] = 6f + 75f * kneel; target.knee[i] = 14f + 100f * kneel; target.ankle[i] = -8f - 30f * kneel;
            target.armPitch[i] = 14f + 30f * back - 10f * kneel; target.armRoll[i] = 40f * back;
        }
    }

    static void Blend(Pose a, Pose b, float w)
    {
        if (w <= 0f) return;
        a.MoveTowards(b, Mathf.Clamp01(w));
    }

    void LateUpdate()
    {
        if (crumbling || hips == null) return;
        float dt = Time.deltaTime;
        if (state == State.Dormant) SetDormantPose(target);
        float k = 1f - Mathf.Exp(-dt * (IsDead ? 6f : 14f));
        pose.MoveTowards(target, k);

        // Голова «принюхивается» к игроку (слепой — ориентируется на звук).
        float lookYaw = 0f, lookPitch = 0f;
        if (playerCam != null && !IsDead && state != State.Dormant && head != null)
        {
            Vector3 to = transform.InverseTransformDirection(playerCam.position - head.position);
            lookYaw = Mathf.Clamp(Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg, -50f, 50f);
            lookPitch = Mathf.Clamp(-Mathf.Atan2(to.y, new Vector2(to.x, to.z).magnitude) * Mathf.Rad2Deg, -30f, 30f);
        }
        // Вздрагивание от попаданий.
        Vector3 fl = transform.InverseTransformDirection(flinchDir);
        float flinchPitch = -fl.z * 14f * flinch, flinchRoll = fl.x * 10f * flinch;

        hips.localPosition = hipsRest + Vector3.down * pose.hipsDrop / Mathf.Max(0.01f, pivot.localScale.y);
        hips.localRotation = Quaternion.AngleAxis(pose.hipsPitch, Vector3.right);
        spine.localRotation = Quaternion.AngleAxis(pose.spinePitch + flinchPitch, Vector3.right) * Quaternion.AngleAxis(flinchRoll, Vector3.forward);
        chest.localRotation = Quaternion.AngleAxis(pose.chestYaw + lookYaw * 0.3f, Vector3.up) * Quaternion.AngleAxis(pose.chestPitch, Vector3.right)
                              * Quaternion.AngleAxis(pose.chestRoll, Vector3.forward);
        head.localRotation = Quaternion.AngleAxis(pose.headYaw + lookYaw * 0.7f, Vector3.up) * Quaternion.AngleAxis(pose.headPitch + lookPitch * 0.6f + flinch * 15f, Vector3.right);
        for (int i = 0; i < 2; i++)
        {
            float side = i == 1 ? 1f : -1f; // правая — +X
            if (upperArm[i] != null) upperArm[i].localRotation = Quaternion.AngleAxis(-pose.armPitch[i], Vector3.right) * Quaternion.AngleAxis(side * pose.armRoll[i], Vector3.forward);
            if (forearm[i] != null) forearm[i].localRotation = Quaternion.AngleAxis(-pose.elbow[i], Vector3.right);
            if (hand[i] != null) hand[i].localRotation = Quaternion.AngleAxis(-pose.wrist[i], Vector3.right);
            if (thigh[i] != null) thigh[i].localRotation = Quaternion.AngleAxis(-pose.legPitch[i], Vector3.right);
            if (shin[i] != null) shin[i].localRotation = Quaternion.AngleAxis(pose.knee[i], Vector3.right);
            if (foot[i] != null) foot[i].localRotation = Quaternion.AngleAxis(-pose.ankle[i], Vector3.right);
        }
    }

    // ---------- Интерфейс ----------

    void OnGUI()
    {
        if (SaveSystem.HideHud || StoryDirector.HideHud || PauseMenu.IsPaused || InventoryScreen.IsOpen || Shopkeeper.IsShopOpen) return;
        if (IsDead)
        {
            if (Time.time > bannerUntil) return;
            float a = Mathf.Clamp01((bannerUntil - Time.time) / 1f) * Mathf.Clamp01(deathClock / 0.5f);
            var big = new GUIStyle(GUI.skin.label) { fontSize = Mathf.Max(32, Screen.height / 12), fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            var r = new Rect(0, Screen.height * 0.22f, Screen.width, Screen.height * 0.14f);
            GUI.color = new Color(0, 0, 0, 0.85f * a);
            GUI.Label(new Rect(r.x + 3, r.y + 3, r.width, r.height), "БОСС ПОВЕРЖЕН", big);
            GUI.color = new Color(1f, 0.82f, 0.3f, a);
            GUI.Label(r, "БОСС ПОВЕРЖЕН", big);
            var small = new GUIStyle(big) { fontSize = Mathf.Max(16, Screen.height / 36), fontStyle = FontStyle.Normal };
            GUI.color = new Color(1f, 1f, 1f, a);
            GUI.Label(new Rect(0, r.yMax, Screen.width, 40), "Соберите награду: " + coinReward + " монет", small);
            GUI.color = Color.white;
            return;
        }
        if (state == State.Dormant) return;

        // Полоса здоровья вверху по центру.
        float w = Mathf.Min(Screen.width * 0.55f, 900f), h = Mathf.Max(14f, Screen.height / 60f);
        float x = (Screen.width - w) / 2f, y = Mathf.Max(40f, Screen.height / 16f);
        var nameStyle = new GUIStyle(GUI.skin.label) { fontSize = Mathf.Max(16, Screen.height / 40), fontStyle = FontStyle.Bold, alignment = TextAnchor.LowerCenter };
        string title = bossName + (Enraged ? "  —  в ярости" : "");
        GUI.color = new Color(0, 0, 0, 0.8f);
        GUI.Label(new Rect(x + 2, y - 40 + 2, w, 36), title, nameStyle);
        GUI.color = Enraged ? new Color(1f, 0.45f, 0.35f) : new Color(0.95f, 0.9f, 0.85f);
        GUI.Label(new Rect(x, y - 40, w, 36), title, nameStyle);
        GUI.color = new Color(0f, 0f, 0f, 0.75f);
        GUI.DrawTexture(new Rect(x - 3, y - 3, w + 6, h + 6), Texture2D.whiteTexture);
        GUI.color = new Color(0.9f, 0.75f, 0.3f, 0.9f);
        GUI.DrawTexture(new Rect(x, y, w * Mathf.Clamp01(healthShown / maxHealth), h), Texture2D.whiteTexture);
        GUI.color = new Color(0.75f, 0.08f, 0.06f, 1f);
        GUI.DrawTexture(new Rect(x, y, w * Mathf.Clamp01(Health / maxHealth), h), Texture2D.whiteTexture);
        // Отметки рёва (призыва).
        GUI.color = new Color(0f, 0f, 0f, 0.8f);
        foreach (float f in summonAt) GUI.DrawTexture(new Rect(x + w * f - 1f, y, 2f, h), Texture2D.whiteTexture);
        if (state == State.Stunned)
        {
            var st = new GUIStyle(nameStyle) { alignment = TextAnchor.UpperCenter, fontSize = Mathf.Max(14, Screen.height / 50) };
            GUI.color = new Color(1f, 0.85f, 0.3f, 0.6f + 0.4f * Mathf.Sin(Time.time * 10f));
            GUI.Label(new Rect(x, y + h + 4, w, 30), "ОГЛУШЁН — бей!", st);
        }
        GUI.color = Color.white;
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(0.9f, 0.6f, 0.1f, 0.6f);
        Gizmos.DrawWireCube(arena.center, arena.size);
    }
}

/// <summary>Обломок бетона, который бросает босс: летит по дуге, бьёт игрока и рассыпается.</summary>
public class BossRock : MonoBehaviour
{
    public const float Gravity = 18f;
    Vector3 velocity;
    float damage, life;
    bool flying;
    ZombieBoss owner;

    public static BossRock Create(Transform hand)
    {
        var go = new GameObject("BossRock");
        go.transform.SetParent(hand, false);
        go.transform.localPosition = Vector3.zero;
        var mat = BossFx.ConcreteMaterial;
        for (int i = 0; i < 4; i++)
        {
            var c = GameObject.CreatePrimitive(PrimitiveType.Cube);
            DestroyImmediate(c.GetComponent<Collider>());
            c.transform.SetParent(go.transform, false);
            c.transform.localPosition = Random.insideUnitSphere * 0.08f;
            c.transform.localRotation = Random.rotation;
            c.transform.localScale = Vector3.one * Random.Range(0.18f, 0.3f);
            c.GetComponent<Renderer>().sharedMaterial = mat;
        }
        // Масштаб руки (Pivot босса) не должен влиять на размер камня.
        Vector3 ls = hand.lossyScale;
        go.transform.localScale = new Vector3(1f / Mathf.Max(1e-3f, ls.x), 1f / Mathf.Max(1e-3f, ls.y), 1f / Mathf.Max(1e-3f, ls.z));
        return go.AddComponent<BossRock>();
    }

    public void Launch(Vector3 vel, float dmg, ZombieBoss from)
    {
        transform.SetParent(null, true);
        transform.localScale = Vector3.one;
        velocity = vel;
        damage = dmg;
        owner = from;
        flying = true;
    }

    void Update()
    {
        if (!flying) return;
        float dt = Time.deltaTime;
        life += dt;
        velocity += Vector3.down * Gravity * dt;
        Vector3 step = velocity * dt;
        transform.Rotate(new Vector3(400f, 250f, 120f) * dt, Space.Self);
        var hits = Physics.SphereCastAll(transform.position, 0.2f, step.normalized, step.magnitude, ~0, QueryTriggerInteraction.Ignore);
        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
        foreach (var hit in hits)
        {
            if (owner != null && hit.transform.IsChildOf(owner.transform)) continue;
            if (hit.collider.GetComponentInParent<ItemPickup>() != null || hit.collider.name == "BossDebris") continue;
            Explode(hit.distance > 0f ? hit.point : transform.position, hit.collider);
            return;
        }
        transform.position += step;
        if (life > 5f) Destroy(gameObject);
    }

    void Explode(Vector3 point, Collider col)
    {
        var health = col != null ? col.GetComponentInParent<PlayerHealth>() : null;
        if (health == null)
        {
            // Попал рядом — осколки задевают.
            var fpc = FindAnyObjectByType<FirstPersonController>();
            if (fpc != null && Vector3.Distance(fpc.transform.position + Vector3.up, point) < 1.4f) health = fpc.GetComponent<PlayerHealth>();
            if (health != null) damage *= 0.5f;
        }
        if (health != null && !health.IsDead)
        {
            health.TakeDamage(damage, point - velocity.normalized);
            var fpc = health.GetComponent<FirstPersonController>();
            if (fpc != null) fpc.AddImpulse(new Vector3(velocity.x, 0f, velocity.z).normalized * 4f + Vector3.up * 2f);
        }
        BossFx.Dust(point, 0.8f, 25, new Color(0.45f, 0.43f, 0.4f, 0.6f));
        BossFx.Debris(point, 10);
        AudioSource.PlayClipAtPoint(BossSounds.Slam(), point, 0.6f * GameSettings.EffectsVolume);
        Destroy(gameObject);
    }
}

/// <summary>Эффекты босса: пыль, осколки, прах.</summary>
public static class BossFx
{
    static Material concrete;

    public static Material ConcreteMaterial
    {
        get
        {
            if (concrete != null) return concrete;
            return concrete = RuntimeMaterials.Create(new Color(0.42f, 0.4f, 0.37f), 0.1f);
        }
    }

    /// <summary>Кольцо пыли по полу радиусом radius.</summary>
    public static void Dust(Vector3 pos, float radius, int count, Color color)
    {
        var mat = Zombie.AshDustMaterial;
        if (mat == null) return;
        var go = new GameObject("BossDust");
        go.transform.position = pos;
        go.transform.rotation = Quaternion.Euler(-90f, 0f, 0f);
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main;
        main.playOnAwake = false;
        main.loop = false;
        main.duration = 0.2f;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.9f, 1.8f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(radius * 0.8f, radius * 1.6f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.4f, 1.1f);
        main.startColor = color;
        main.gravityModifier = -0.02f;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.stopAction = ParticleSystemStopAction.Destroy;
        var em = ps.emission;
        em.rateOverTime = 0f;
        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Circle;
        shape.radius = 0.3f;
        var limit = ps.limitVelocityOverLifetime;
        limit.enabled = true;
        limit.limit = 0.3f;
        limit.dampen = 0.12f;
        var col = ps.colorOverLifetime;
        col.enabled = true;
        var g = new Gradient();
        g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                  new[] { new GradientAlphaKey(0.9f, 0f), new GradientAlphaKey(0f, 1f) });
        col.color = g;
        var size = ps.sizeOverLifetime;
        size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.6f), new Keyframe(1f, 1.8f)));
        var r = go.GetComponent<ParticleSystemRenderer>();
        r.sharedMaterial = mat;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        ps.Emit(count);
        ps.Play();
    }

    /// <summary>Осколки бетона, разлетающиеся с физикой и исчезающие.</summary>
    public static void Debris(Vector3 pos, int count)
    {
        var mat = ConcreteMaterial;
        var fpc = Object.FindAnyObjectByType<FirstPersonController>();
        var cc = fpc != null ? fpc.GetComponent<CharacterController>() : null;
        for (int i = 0; i < count; i++)
        {
            var c = GameObject.CreatePrimitive(PrimitiveType.Cube);
            c.name = "BossDebris";
            c.transform.position = pos + Random.insideUnitSphere * 0.3f;
            c.transform.rotation = Random.rotation;
            c.transform.localScale = Vector3.one * Random.Range(0.04f, 0.12f);
            c.GetComponent<Renderer>().sharedMaterial = mat;
            var rb = c.AddComponent<Rigidbody>();
            rb.mass = 0.2f;
            rb.linearVelocity = Random.insideUnitSphere * 4f + Vector3.up * Random.Range(2f, 5f);
            rb.angularVelocity = Random.insideUnitSphere * 10f;
            if (cc != null) Physics.IgnoreCollision(c.GetComponent<Collider>(), cc);
            Object.Destroy(c, Random.Range(2.5f, 4f));
        }
    }

    /// <summary>Прах, срывающийся с поверхности тела (как у зомби).</summary>
    public static ParticleSystem Ash(SkinnedMeshRenderer source, Material mat, bool flakes, int count, float duration, Transform parent)
    {
        var go = new GameObject(flakes ? "AshFlakes" : "AshDust");
        go.transform.SetParent(parent, false);
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main;
        main.playOnAwake = false;
        main.loop = false;
        main.duration = duration;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = count + 10;
        main.startLifetime = new ParticleSystem.MinMaxCurve(1.8f, 3.5f);
        main.startSpeed = flakes ? new ParticleSystem.MinMaxCurve(0.05f, 0.5f) : new ParticleSystem.MinMaxCurve(0.02f, 0.2f);
        main.startSize = flakes ? new ParticleSystem.MinMaxCurve(0.03f, 0.07f) : new ParticleSystem.MinMaxCurve(0.4f, 0.9f);
        main.startColor = flakes
            ? new ParticleSystem.MinMaxGradient(new Color(0.09f, 0.085f, 0.08f), new Color(0.3f, 0.28f, 0.26f))
            : new ParticleSystem.MinMaxGradient(new Color(0.25f, 0.24f, 0.22f, 0.45f), new Color(0.4f, 0.38f, 0.35f, 0.3f));
        main.gravityModifier = -0.02f;
        var em = ps.emission;
        em.rateOverTime = count / duration * 1.2f;
        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.SkinnedMeshRenderer;
        shape.skinnedMeshRenderer = source;
        shape.meshShapeType = ParticleSystemMeshShapeType.Triangle;
        var vel = ps.velocityOverLifetime;
        vel.enabled = true;
        vel.space = ParticleSystemSimulationSpace.World;
        vel.x = new ParticleSystem.MinMaxCurve(0.2f, 0.8f);
        vel.y = new ParticleSystem.MinMaxCurve(0.3f, 0.9f);
        vel.z = new ParticleSystem.MinMaxCurve(0.1f, 0.4f);
        var noise = ps.noise;
        noise.enabled = true;
        noise.strength = flakes ? 0.6f : 0.25f;
        noise.frequency = 0.8f;
        var col = ps.colorOverLifetime;
        col.enabled = true;
        var g = new Gradient();
        g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                  new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.08f), new GradientAlphaKey(0.8f, 0.6f), new GradientAlphaKey(0f, 1f) });
        col.color = g;
        var r = go.GetComponent<ParticleSystemRenderer>();
        r.sharedMaterial = mat;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        ps.Play();
        return ps;
    }
}

/// <summary>Звуки босса, синтезированные в коде (ассеты не нужны): рёв, рык, удар о пол, взмах, шаг.</summary>
public static class BossSounds
{
    static AudioClip roar, growl, slam, whoosh, step;
    const int Rate = 44100;

    static AudioClip Make(string name, float seconds, System.Func<float, System.Random, float> sample)
    {
        int n = Mathf.RoundToInt(seconds * Rate);
        var data = new float[n];
        var rnd = new System.Random(name.GetHashCode());
        float peak = 1e-4f;
        for (int i = 0; i < n; i++) { data[i] = sample(i / (float)Rate, rnd); peak = Mathf.Max(peak, Mathf.Abs(data[i])); }
        for (int i = 0; i < n; i++) data[i] = data[i] / peak * 0.9f;
        var clip = AudioClip.Create(name, n, 1, Rate, false);
        clip.SetData(data, 0);
        return clip;
    }

    // Простейший фильтр низких частот для шума (состояние — в замыкании).
    static System.Func<float, float> LowPass(float k)
    {
        float y = 0f;
        return x => y += (x - y) * k;
    }

    public static AudioClip Roar()
    {
        if (roar != null) return roar;
        var lp = LowPass(0.05f);
        var lp2 = LowPass(0.2f);
        return roar = Make("BossRoar", 2.2f, (t, r) =>
        {
            float env = Mathf.Clamp01(t / 0.25f) * Mathf.Clamp01((2.2f - t) / 0.9f);
            float f = 70f + 25f * Mathf.Sin(t * 3f) + 15f * Mathf.Sin(t * 17f);
            float tone = Mathf.Sin(2f * Mathf.PI * f * t) + 0.6f * Mathf.Sin(2f * Mathf.PI * f * 2.02f * t) + 0.3f * Mathf.Sin(2f * Mathf.PI * f * 3.1f * t);
            float rasp = lp2((float)r.NextDouble() * 2f - 1f) * (0.8f + 0.6f * Mathf.Sin(2f * Mathf.PI * 31f * t));
            float rumble = lp((float)r.NextDouble() * 2f - 1f) * 3f;
            return env * (tone * 0.45f * (1f + 0.5f * Mathf.Sin(2f * Mathf.PI * 9f * t)) + rasp * 0.9f + rumble);
        });
    }

    public static AudioClip Growl()
    {
        if (growl != null) return growl;
        var lp = LowPass(0.12f);
        return growl = Make("BossGrowl", 0.8f, (t, r) =>
        {
            float env = Mathf.Clamp01(t / 0.08f) * Mathf.Clamp01((0.8f - t) / 0.4f);
            float f = 85f - 20f * t;
            return env * (Mathf.Sin(2f * Mathf.PI * f * t) * (0.6f + 0.4f * Mathf.Sin(2f * Mathf.PI * 23f * t)) + lp((float)r.NextDouble() * 2f - 1f) * 1.5f);
        });
    }

    public static AudioClip Slam()
    {
        if (slam != null) return slam;
        var lp = LowPass(0.08f);
        return slam = Make("BossSlam", 1.2f, (t, r) =>
        {
            float thump = Mathf.Sin(2f * Mathf.PI * (48f - 18f * t) * t) * Mathf.Exp(-t * 4.5f);
            float crack = lp((float)r.NextDouble() * 2f - 1f) * Mathf.Exp(-t * 6f) * 2.5f;
            float rattle = ((float)r.NextDouble() * 2f - 1f) * Mathf.Exp(-t * 18f) * 0.4f;
            return thump * 1.3f + crack + rattle;
        });
    }

    public static AudioClip Whoosh()
    {
        if (whoosh != null) return whoosh;
        var lp = LowPass(0.15f);
        return whoosh = Make("BossWhoosh", 0.45f, (t, r) =>
        {
            float env = Mathf.Sin(Mathf.Clamp01(t / 0.45f) * Mathf.PI);
            return lp((float)r.NextDouble() * 2f - 1f) * env * env;
        });
    }

    public static AudioClip Step()
    {
        if (step != null) return step;
        var lp = LowPass(0.06f);
        return step = Make("BossStep", 0.4f, (t, r) =>
            Mathf.Sin(2f * Mathf.PI * 55f * t) * Mathf.Exp(-t * 12f) + lp((float)r.NextDouble() * 2f - 1f) * Mathf.Exp(-t * 10f) * 2f);
    }
}
