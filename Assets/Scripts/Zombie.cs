using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Зомби: идёт к игроку по NavMesh, если игрок сбоку или сзади — разворачивается на месте (клип Zombie Turn),
/// вблизи атакует, получает урон от пуль (ZombieHitbox), вздрагивает от попаданий (верх тела), после смерти
/// падает (клип смерти), темнеет и рассыпается в прах, который развеивает ветер.
/// Анимации — клипы Mixamo через Animator Controller Zombie.controller (Humanoid, собирается ZombieAnimationSetup).
/// Создаётся через Zombie.Create (это делает ZombieSpawner).
/// </summary>
[DefaultExecutionOrder(50)]
public class Zombie : MonoBehaviour
{
    public static readonly List<Zombie> All = new List<Zombie>();

    public static int AliveCount
    {
        get
        {
            int n = 0;
            foreach (var z in All) if (z != null && !z.IsDead) n++;
            return n;
        }
    }

    [Header("Здоровье")]
    public float maxHealth = 100f;
    [Tooltip("Множители урона по частям тела. Голова — с одного попадания убивает автомат, пистолету нужно два.")]
    public float headMultiplier = 3f;
    public float neckMultiplier = 1.6f;
    public float torsoMultiplier = 0.85f;
    [Tooltip("Плечо и бедро.")]
    public float limbMultiplier = 0.5f;
    [Tooltip("Предплечье и голень.")]
    public float lowerLimbMultiplier = 0.35f;
    [Tooltip("Урон по ногам, после которого зомби хромает в полную силу.")]
    public float legDamageToLimp = 60f;
    [Tooltip("Скорость хромающего зомби (доля от обычной).")]
    [Range(0.2f, 1f)] public float limpSpeed = 0.5f;

    [Header("Движение")]
    public float walkSpeed = 1.1f;
    public float attackRange = 1.3f;
    public float repathInterval = 0.25f;
    [Tooltip("Стоящий зомби, у которого цель дальше этого угла от взгляда, разворачивается на месте (клип Zombie Turn). На ходу поворачивает плавно.")]
    public float turnInPlaceAngle = 75f;
    [Tooltip("Скорость проигрывания клипа разворота на месте.")]
    public float turnPlaybackSpeed = 1.5f;
    [Tooltip("Поворот на ходу (°/с). На крутом повороте пути зомби притормаживает, а не идёт боком.")]
    public float walkTurnRate = 140f;
    [Tooltip("Ближе этого расстояния (м) зомби заходит к игроку со своей стороны — обступают, а не толпятся в одной точке.")]
    public float surroundRadius = 5f;
    [Tooltip("Сколько секунд без продвижения считается застреванием — тогда зомби обходит препятствие боком.")]
    public float stuckTime = 1.2f;
    [Tooltip("Доля зомби с обычной походкой (Walking) вместо зомби-походки.")]
    [Range(0f, 1f)] public float altWalkChance = 0.25f;
    [Tooltip("Клипы ходьбы скачаны «In Place»: при какой скорости (м/с) шаг в клипе Zombie Walk / Walking выглядит естественно.")]
    public float zombieWalkClipSpeed = 0.7f, altWalkClipSpeed = 1.3f;
    [Tooltip("Во сколько раз можно ускорить клип ходьбы. Скорость зомби ограничивается так, чтобы ступни не скользили: " +
             "не больше скорости шага клипа × это число.")]
    public float maxWalkAnimSpeed = 1.8f;

    [Header("Атака")]
    [Tooltip("Урон от удара руками (Zombie Attack).")]
    public float attackDamage = 18f;
    [Tooltip("Урон от укуса (Zombie Neck Bite) — больше, чем от удара.")]
    public float biteDamage = 32f;
    [Tooltip("Доля зомби, которые кусают (Zombie Neck Bite); остальные бьют руками (Zombie Attack).")]
    [Range(0f, 1f)] public float biteChance = 0.5f;
    [Tooltip("Момент удара внутри клипа атаки (доля длительности) — для Zombie Attack и Zombie Neck Bite.")]
    public float[] attackHitMoments = { 0.4f, 0.3f };
    [Tooltip("С какого расстояния зомби открывает закрытую дверь перед собой (м).")]
    public float doorOpenReach = 1.1f;

    [Header("После смерти: прах")]
    [Tooltip("Когда тело начинает рассыпаться — доля длительности клипа смерти (тело уже упало).")]
    [Range(0f, 1f)] public float crumbleAt = 0.8f;
    [Tooltip("За сколько секунд тело темнеет до пепла и оседает.")]
    public float crumbleTime = 1.4f;
    public Color ashColor = new Color(0.16f, 0.15f, 0.14f);
    [Tooltip("Сколько хлопьев пепла и облачков пыли срывается с тела (на всё тело).")]
    public int ashFlakes = 350, ashPuffs = 40;
    [Tooltip("Ветер, уносящий прах (м/с).")]
    public Vector3 ashWind = new Vector3(0.6f, 0.5f, 0.25f);

    /// <summary>Материалы праха (задаёт ZombieSpawner): хлопья — мелкие тёмные частицы без бликов, пыль — мягкое облачко.</summary>
    public static Material AshFlakeMaterial, AshDustMaterial;

    [Header("Добыча")]
    [Tooltip("Вероятность, что после смерти останутся патроны.")]
    [Range(0f, 1f)] public float ammoDropChance = 0.45f;
    [Tooltip("Вероятность, что после смерти останется аптечка.")]
    [Range(0f, 1f)] public float medkitDropChance = 0.1f;

    public float Health { get; private set; }
    public bool IsDead { get; private set; }

    enum Act { Move, Turn, Attack }

    // Имена состояний — как в ZombieAnimationSetup.
    const string LocoState = "Locomotion", LocoAltState = "LocomotionAlt", TurnState = "Turn", TurnMirrorState = "TurnMirror";
    static readonly string[] AttackStates = { "Attack1", "Attack2" };
    static readonly string[] HitStates = { "Hit1", "Hit2" };
    static readonly string[] DeathStates = { "Death1", "Death2" };
    static readonly int MoveParam = Animator.StringToHash("Move"), WalkSpeedParam = Animator.StringToHash("WalkSpeed");

    static Transform player;
    static PlayerHealth playerHealth;

    Transform pivot, model;
    Animator animator;
    NavMeshAgent agent;
    Transform hips, neck, head, jaw;
    readonly List<Collider> hitboxes = new List<Collider>();
    Quaternion jawRest;

    // Длительности и скорости клипов (из контроллера).
    float walkClipSpeed = 1f, turnClipLength;
    bool turnMirrored;
    // Направление поворота в клипе Zombie Turn (+1 — вправо); уточняется по первому повороту.
    static float turnClipSign = 1f;
    static bool turnSignKnown;
    readonly float[] attackLengths = new float[2];

    // Индивидуальность.
    float speedMul, timeOffset, moveSpeed;
    string locoState;

    Act act;
    float actClock, actLength;
    int attackIndex, preferredAttack;
    bool attackHitDone;
    float move, nextRepath, stagger, headFlinch;
    float legDamage; // накопленный урон по ногам — от него зомби хромает

    /// <summary>Множитель скорости от ранений в ноги: 1 — цел, limpSpeed — хромает в полную силу.</summary>
    float LimpFactor => Mathf.Lerp(1f, limpSpeed, Mathf.Clamp01(legDamage / Mathf.Max(1f, legDamageToLimp)));
    float flankAngle, stuckClock, turnCooldown, unstickUntil;
    Vector3 flinchDir = Vector3.forward, lastPos;
    float deathClock, crumbleStart;
    readonly float[] deathLengths = { 3f, 3f };
    readonly List<(Renderer r, int index, string prop, Color color)> tint = new List<(Renderer, int, string, Color)>();
    readonly List<ParticleSystem> ashSystems = new List<ParticleSystem>();
    MaterialPropertyBlock mpb;
    bool crumbling, bodyGone;

    /// <summary>Создаёт зомби из модели prefab в точке pos на NavMesh.</summary>
    public static Zombie Create(GameObject prefab, RuntimeAnimatorController controller, Vector3 pos, Quaternion rot, int agentTypeId)
    {
        var root = new GameObject(prefab.name);
        root.SetActive(false); // агент и коллайдеры настраиваются до включения — без предупреждений NavMesh
        root.transform.SetPositionAndRotation(pos, rot);
        var pivot = new GameObject("Pivot").transform;
        pivot.SetParent(root.transform, false);
        var model = Instantiate(prefab, pivot);
        model.name = "Model";
        model.transform.localPosition = Vector3.zero;
        model.transform.localRotation = Quaternion.identity;

        var z = root.AddComponent<Zombie>();
        z.Init(pivot, model.transform, controller, agentTypeId);
        root.SetActive(true);
        return z;
    }

    void OnEnable() => All.Add(this);
    void OnDisable() => All.Remove(this);

    // ---------- Подготовка ----------

    void Init(Transform pivotT, Transform modelT, RuntimeAnimatorController controller, int agentTypeId)
    {
        pivot = pivotT;
        model = modelT;
        Health = maxHealth;

        // Кости берём из скина: в модели есть и кость Head, и меш Head — по имени в иерархии их не различить.
        var bones = new Dictionary<string, Transform>();
        foreach (var smr in model.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            smr.updateWhenOffscreen = true;
            foreach (var b in smr.bones)
                if (b != null && !bones.ContainsKey(Simplify(b.name))) bones[Simplify(b.name)] = b;
        }
        Transform B(string n)
        {
            string k = Simplify(n);
            if (bones.TryGetValue(k, out var t)) return t;
            foreach (var kv in bones) // имя могло получить числовой суффикс при импорте
                if (kv.Key.StartsWith(k) && kv.Key.Substring(k.Length).TrimStart('0', '1', '2', '3', '4', '5', '6', '7', '8', '9').Length == 0)
                    return kv.Value;
            return null;
        }

        // Лицом вперёд: направление от пяток к носкам должно совпасть с forward корня.
        Transform footR = B("Foot.R"), footL = B("Foot.L"), toeR = B("Toe.R"), toeL = B("Toe.L");
        if (footR != null && footL != null && toeR != null && toeL != null)
        {
            Vector3 toeDir = Vector3.ProjectOnPlane(toeR.position + toeL.position - footR.position - footL.position, pivot.up);
            if (toeDir.sqrMagnitude > 1e-6f)
                model.localRotation = Quaternion.Euler(0f, Vector3.SignedAngle(toeDir, pivot.forward, pivot.up), 0f) * model.localRotation;
        }

        // Хитбоксы строим по T-позе — до того, как Animator начнёт двигать кости.
        hips = B("Hips"); neck = B("Neck"); head = B("Head"); jaw = B("Jaw");
        if (jaw != null) jawRest = jaw.localRotation;
        BuildHitboxes(B);

        speedMul = Random.Range(0.85f, 1.2f);
        timeOffset = Random.Range(0f, 100f);
        locoState = Random.value < altWalkChance ? LocoAltState : LocoState;
        preferredAttack = Random.value < biteChance ? 1 : 0;
        flankAngle = Random.Range(-40f, 40f);

        animator = model.GetComponent<Animator>();
        if (animator == null) animator = model.gameObject.AddComponent<Animator>();
        animator.runtimeAnimatorController = controller;
        animator.applyRootMotion = true; // root motion забирает ZombieAnimatorRelay — нужен только поворот из Turn
        animator.cullingMode = AnimatorCullingMode.AlwaysAnimate; // хитбоксы на костях должны двигаться и за спиной игрока
        model.gameObject.AddComponent<ZombieAnimatorRelay>().owner = this;
        ReadClips(controller);
        if (controller == null) Debug.LogWarning("Zombie: не задан Animator Controller (ZombieSpawner.zombieAnimator) — зомби будет в T-позе.");

        var rb = gameObject.AddComponent<Rigidbody>();
        rb.isKinematic = true; // коллайдеры костей движутся вместе с анимацией

        agent = gameObject.AddComponent<NavMeshAgent>();
        agent.agentTypeID = agentTypeId;
        agent.radius = 0.28f; // меньше радиуса запекания NavMesh (0,35): от стен держатся, а в проходе расходятся друг с другом
        agent.height = 1.8f;
        agent.baseOffset = 0f;
        moveSpeed = walkSpeed * speedMul;
        agent.speed = moveSpeed;
        agent.acceleration = 6f;
        agent.angularSpeed = 140f;
        agent.updateRotation = false; // поворачивает Zombie: по направлению пути, с замедлением на поворотах
        agent.stoppingDistance = 0.1f; // останавливает переход к атаке (attackRange), а не агент
        agent.autoBraking = true;
        agent.obstacleAvoidanceType = ObstacleAvoidanceType.HighQualityObstacleAvoidance;
        agent.avoidancePriority = Random.Range(30, 70);

        lastPos = transform.position;
    }

    void ReadClips(RuntimeAnimatorController controller)
    {
        if (controller == null) return;
        walkClipSpeed = locoState == LocoAltState ? altWalkClipSpeed : zombieWalkClipSpeed;
        foreach (var c in controller.animationClips)
        {
            string n = c.name;
            if (n.EndsWith(locoState == LocoAltState ? "@Walking" : "@Zombie Walk") && c.averageSpeed.magnitude > 0.1f) walkClipSpeed = c.averageSpeed.magnitude;
            else if (n.EndsWith("@Zombie Turn")) turnClipLength = c.length;
            else if (n.EndsWith("@Zombie Attack")) attackLengths[0] = c.length;
            else if (n.EndsWith("@Zombie Neck Bite")) attackLengths[1] = c.length;
            else if (n.EndsWith("@Zombie Death")) deathLengths[0] = c.length;
            else if (n.EndsWith("@Zombie Dying")) deathLengths[1] = c.length;
        }
        for (int i = 0; i < attackLengths.Length; i++)
            if (attackLengths[i] <= 0f) attackLengths[i] = Mathf.Max(attackLengths[0], attackLengths[1], 1.2f);
    }

    static string Simplify(string s)
    {
        var sb = new System.Text.StringBuilder();
        foreach (char c in s) if (char.IsLetterOrDigit(c)) sb.Append(char.ToLowerInvariant(c));
        return sb.ToString();
    }

    void BuildHitboxes(System.Func<string, Transform> B)
    {
        // Туловище — от таза до шеи, шея — до головы.
        if (hips != null && neck != null) AddCapsule(hips, hips, neck, 0.17f, torsoMultiplier, false, ZombieHitbox.Part.Torso);
        if (neck != null && head != null) AddCapsule(neck, neck, head, 0.07f, neckMultiplier, false, ZombieHitbox.Part.Neck);

        // Голова — шар по габаритам меша головы (а если его нет — над костью Head).
        if (head != null)
        {
            Vector3 center = head.position + pivot.up * 0.11f;
            float radius = 0.12f;
            foreach (var smr in model.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                if (smr.sharedMesh != null && Simplify(smr.name).StartsWith("head"))
                {
                    Bounds mb = smr.sharedMesh.bounds;
                    center = smr.transform.TransformPoint(mb.center);
                    radius = Mathf.Clamp(Mathf.Max(mb.extents.x, mb.extents.y, mb.extents.z) * 0.85f, 0.08f, 0.2f);
                }
            var go = new GameObject("Hitbox_Head");
            go.transform.SetParent(head, false);
            go.transform.position = center;
            var sc = go.AddComponent<SphereCollider>();
            sc.radius = radius / Scale(go.transform);
            RegisterHitbox(go, sc, headMultiplier, true, ZombieHitbox.Part.Head);
        }

        foreach (var s in new[] { "L", "R" })
        {
            Transform upper = B("UpperArm." + s), fore = B("Forearm." + s), hand = B("Hand." + s);
            if (upper != null && fore != null) AddCapsule(upper, upper, fore, 0.065f, limbMultiplier, false, ZombieHitbox.Part.UpperArm);
            if (fore != null && hand != null) AddCapsule(fore, fore, hand, 0.055f, lowerLimbMultiplier, false, ZombieHitbox.Part.Forearm);

            Transform thigh = B("Thigh." + s), shin = B("Shin." + s), foot = B("Foot." + s);
            if (thigh == null || shin == null) continue;
            AddCapsule(thigh, thigh, shin, 0.09f, limbMultiplier, false, ZombieHitbox.Part.Thigh);
            Transform end = foot != null && foot.IsChildOf(shin) ? foot : (shin.childCount > 0 ? shin.GetChild(0) : null);
            if (end != null) AddCapsule(shin, shin, end, 0.07f, lowerLimbMultiplier, false, ZombieHitbox.Part.Shin);
        }
    }

    /// <summary>Капсула от from до to, прикреплённая к кости parent.</summary>
    void AddCapsule(Transform parent, Transform from, Transform to, float radius, float mult, bool isHead, ZombieHitbox.Part part)
    {
        Vector3 p0 = from.position, p1 = to.position;
        Vector3 d = p1 - p0;
        if (d.sqrMagnitude < 1e-6f) return;
        var go = new GameObject("Hitbox_" + from.name);
        go.transform.SetParent(parent, false);
        go.transform.SetPositionAndRotation((p0 + p1) * 0.5f,
            Quaternion.LookRotation(d, Mathf.Abs(Vector3.Dot(d.normalized, Vector3.up)) > 0.95f ? Vector3.forward : Vector3.up));
        float s = Scale(go.transform);
        var c = go.AddComponent<CapsuleCollider>();
        c.direction = 2;
        c.radius = radius / s;
        c.height = (d.magnitude + radius * 2f) / s;
        RegisterHitbox(go, c, mult, isHead, part);
    }

    void RegisterHitbox(GameObject go, Collider c, float mult, bool isHead, ZombieHitbox.Part part)
    {
        var hb = go.AddComponent<ZombieHitbox>();
        hb.owner = this;
        hb.multiplier = mult;
        hb.isHead = isHead;
        hb.part = part;
        hitboxes.Add(c);
    }

    static float Scale(Transform t)
    {
        Vector3 s = t.lossyScale;
        return Mathf.Max(1e-4f, (Mathf.Abs(s.x) + Mathf.Abs(s.y) + Mathf.Abs(s.z)) / 3f);
    }

    void Start()
    {
        if (!HasController) return;
        // Скорость шага без скольжения — замерена ZombieAnimationSetup по ступням и записана в параметры контроллера.
        string p = locoState == LocoAltState ? "WalkAltClipSpeed" : "WalkClipSpeed";
        foreach (var par in animator.parameters)
            if (par.name == p && par.defaultFloat > 0.05f) walkClipSpeed = par.defaultFloat;
        // Быстрее, чем позволяет шаг клипа, не идём — иначе ноги не успевают и зомби скользит.
        moveSpeed = Mathf.Min(walkSpeed, walkClipSpeed * maxWalkAnimSpeed) * speedMul;
        // Разные зомби — в разных фазах шага.
        animator.Play(locoState, 0, Random.value);
    }

    // ---------- Урон ----------

    /// <summary>Попадание пули: damage — урон пули (множитель части тела применяется здесь).</summary>
    public void TakeHit(float damage, Vector3 point, Vector3 direction, ZombieHitbox part)
    {
        if (IsDead) return;
        float mult = part != null ? part.multiplier : 1f;
        Health -= damage * mult;
        if (part != null && part.IsLeg) legDamage += damage * mult;

        Vector3 flat = Vector3.ProjectOnPlane(direction, Vector3.up);
        if (flat.sqrMagnitude > 1e-4f) flinchDir = flat.normalized;
        if (part != null && part.isHead) headFlinch = 1f;
        stagger = 0.35f;

        if (Health <= 0f) { Die(); return; }
        if (HasController) animator.CrossFadeInFixedTime(HitStates[Random.Range(0, HitStates.Length)], 0.06f, 1, 0f);
    }

    bool HasController => animator != null && animator.runtimeAnimatorController != null;

    void Die()
    {
        IsDead = true;
        deathClock = 0f;
        ItemPickup.RollZombieDrop(transform.position + Vector3.up * 0.6f, ammoDropChance, medkitDropChance);
        foreach (var c in hitboxes) if (c != null) c.enabled = false;
        if (agent != null) agent.enabled = false;
        if (animator != null) animator.speed = 1f;
        int d = Random.Range(0, DeathStates.Length);
        crumbleStart = deathLengths[d] * crumbleAt;
        if (HasController)
        {
            animator.SetLayerWeight(1, 0f);
            animator.CrossFadeInFixedTime(DeathStates[d], 0.15f, 0, 0f);
        }
    }

    // ---------- Поведение ----------

    void Update()
    {
        float dt = Time.deltaTime;
        if (IsDead) { UpdateCorpse(dt); return; }

        if (player == null)
        {
            var fpc = FindAnyObjectByType<FirstPersonController>();
            if (fpc != null) player = fpc.transform;
        }
        if (playerHealth == null && player != null) playerHealth = player.GetComponent<PlayerHealth>();

        headFlinch = Mathf.MoveTowards(headFlinch, 0f, dt * 3f);
        stagger = Mathf.Max(0f, stagger - dt);
        turnCooldown = Mathf.Max(0f, turnCooldown - dt);
        actClock += dt;

        Vector3 v = transform.position - lastPos; v.y = 0f;
        lastPos = transform.position;
        float speed = dt > 0f ? v.magnitude / dt : 0f;

        // Закрытая дверь на пути — толкаем её (NavMesh проходит сквозь дверные проёмы).
        SwingGate.OpenNear(transform.position, doorOpenReach);

        bool playerDead = playerHealth != null && playerHealth.IsDead;
        bool inRange = false;
        Vector3 toPlayer = Vector3.zero;
        if (player != null)
        {
            toPlayer = player.position - transform.position;
            float dy = toPlayer.y;
            toPlayer.y = 0f;
            inRange = !playerDead && toPlayer.magnitude <= attackRange && Mathf.Abs(dy) < 1.5f;
        }

        bool canMove = player != null && !playerDead;
        bool walking = act == Act.Move && canMove && !inRange;
        bool onMesh = agent != null && agent.isOnNavMesh;
        if (onMesh)
        {
            agent.isStopped = !walking;
            if (walking && Time.time >= nextRepath) Repath(toPlayer);
        }

        // Куда надо смотреть: вблизи — на игрока, на ходу — по направлению пути.
        Vector3 want = toPlayer;
        if (!inRange && onMesh && agent.hasPath)
        {
            Vector3 dv = agent.desiredVelocity; dv.y = 0f;
            Vector3 st = agent.steeringTarget - transform.position; st.y = 0f;
            if (dv.sqrMagnitude > 0.01f) want = dv; else if (st.sqrMagnitude > 0.04f) want = st;
        }
        float angle = want.sqrMagnitude > 1e-4f ? Vector3.SignedAngle(transform.forward, want, Vector3.up) : 0f;

        switch (act)
        {
            case Act.Move:
                // Разворот на месте — только стоя (в начале погони, вблизи игрока, после атаки); на ходу поворачиваем плавно.
                if (canMove && move < 0.2f && Mathf.Abs(angle) > turnInPlaceAngle && turnClipLength > 0f && stagger <= 0f && turnCooldown <= 0f)
                    StartTurn(angle);
                else if (inRange && Mathf.Abs(angle) < 40f && stagger <= 0f)
                    StartAttack();
                else if (want.sqrMagnitude > 1e-4f && (inRange || walking))
                    transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(want), (inRange ? 120f : walkTurnRate) * dt);
                break;

            case Act.Turn:
                // Развернулся (или клип кончился) — снова идём; если угол ещё большой, следующий кадр начнёт новый поворот.
                if (Mathf.Abs(angle) < 15f || actClock >= actLength * 0.95f || !canMove) EndAct();
                break;

            case Act.Attack:
                if (toPlayer.sqrMagnitude > 1e-4f)
                    transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(toPlayer), 60f * dt);
                float hm = attackHitMoments != null && attackIndex < attackHitMoments.Length ? attackHitMoments[attackIndex] : 0.5f;
                if (!attackHitDone && actClock >= actLength * hm)
                {
                    attackHitDone = true;
                    // Удар достаёт, если игрок всё ещё рядом и не за стеной/дверью.
                    if (!playerDead && player != null && toPlayer.magnitude <= attackRange * 1.25f && playerHealth != null && CanReach(player))
                        playerHealth.TakeDamage(attackIndex == 1 ? biteDamage : attackDamage, transform.position);
                }
                if (actClock >= actLength) EndAct();
                break;
        }

        if (onMesh)
        {
            // На крутом повороте пути притормаживаем, пока не довернём, — не идём боком и не срезаем углы о стены.
            float facing = walking ? Mathf.Lerp(0.15f, 1f, Mathf.InverseLerp(100f, 30f, Mathf.Abs(angle))) : 1f;
            agent.speed = moveSpeed * facing * LimpFactor * (stagger > 0f ? 0.2f : 1f);
            UpdateStuck(dt, walking, speed);
        }

        // Шаг — по фактической скорости (упор в стену или других зомби не даёт «бежать на месте»).
        move = Mathf.MoveTowards(move, Mathf.Clamp01(speed / Mathf.Max(0.05f, moveSpeed * 0.5f)), dt * 4f);
        if (HasController)
        {
            animator.SetFloat(MoveParam, move);
            // Ноги — в такт скорости агента, чтобы ступни не скользили.
            animator.SetFloat(WalkSpeedParam, Mathf.Lerp(1f, Mathf.Clamp(speed / walkClipSpeed, 0.5f, 3f), move));
        }
    }

    /// <summary>
    /// Путь к игроку (кратчайший по NavMesh). Вблизи цель — своя точка у игрока со стороны подхода (со сдвигом flankAngle):
    /// зомби обступают игрока, а не давят друг друга в одной точке. Игрок вне NavMesh (на столе, в прыжке) — ближайшая к нему точка.
    /// </summary>
    void Repath(Vector3 toPlayer)
    {
        float dist = toPlayer.magnitude;
        Vector3 target = player.position;
        if (dist < surroundRadius && dist > 0.01f)
        {
            Vector3 side = Quaternion.AngleAxis(flankAngle * Mathf.InverseLerp(attackRange, surroundRadius, dist), Vector3.up) * -toPlayer.normalized;
            target = player.position + side * attackRange * 0.7f;
        }
        var filter = new NavMeshQueryFilter { agentTypeID = agent.agentTypeID, areaMask = agent.areaMask };
        if (NavMesh.SamplePosition(target, out NavMeshHit hit, 2.5f, filter)) target = hit.position;
        if (Time.time >= unstickUntil) agent.SetDestination(target);

        // В проходе ближние к игроку главнее: дальние уступают им дорогу, а не упираются (меньше число — главнее).
        if (Time.time >= unstickUntil) agent.avoidancePriority = Mathf.Clamp(10 + Mathf.RoundToInt(dist * 4f), 10, 90);
        // Далеко от игрока путь меняется медленно — перестраиваем реже.
        nextRepath = Time.time + (dist > 15f ? repathInterval * 3f : repathInterval);
    }

    /// <summary>
    /// Застревание: агент хочет идти, а зомби почти не продвигается (упёрся в толпу, в угол, в косяк).
    /// Тогда — шаг в сторону по NavMesh и пропуск остальных вперёд, затем снова к игроку.
    /// </summary>
    void UpdateStuck(float dt, bool walking, float speed)
    {
        bool trying = walking && stagger <= 0f && agent.hasPath && !agent.pathPending && agent.remainingDistance > attackRange;
        if (trying && speed < moveSpeed * 0.2f) stuckClock += dt;
        else stuckClock = Mathf.Max(0f, stuckClock - dt * 2f);
        if (stuckClock < stuckTime) return;
        stuckClock = 0f;

        Vector3 dir = agent.desiredVelocity.sqrMagnitude > 0.01f ? agent.desiredVelocity.normalized : transform.forward;
        dir.y = 0f;
        var filter = new NavMeshQueryFilter { agentTypeID = agent.agentTypeID, areaMask = agent.areaMask };
        float sideSign = Random.value < 0.5f ? -1f : 1f;
        for (int i = 0; i < 2; i++, sideSign = -sideSign)
        {
            Vector3 p = transform.position + (Vector3.Cross(Vector3.up, dir) * sideSign * 1.2f - dir * 0.4f);
            if (!NavMesh.SamplePosition(p, out NavMeshHit hit, 0.8f, filter)) continue;
            if (NavMesh.Raycast(transform.position, hit.position, out _, filter)) continue; // за стеной — не туда
            agent.SetDestination(hit.position);
            agent.avoidancePriority = 95; // пока обходит — уступает всем
            unstickUntil = Time.time + 0.9f;
            nextRepath = unstickUntil;
            return;
        }
        // Шагнуть некуда — просто перестроить путь заново.
        agent.ResetPath();
        nextRepath = 0f;
    }

    void StartTurn(float angle)
    {
        act = Act.Turn;
        actClock = 0f;
        // Клип поворачивает в одну сторону; в другую — то же состояние зеркально.
        bool right = angle > 0f;
        bool clipRight = turnClipSign > 0f;
        turnMirrored = right != clipRight;
        actLength = turnClipLength / turnPlaybackSpeed;
        animator.speed = turnPlaybackSpeed;
        animator.CrossFadeInFixedTime(turnMirrored ? TurnMirrorState : TurnState, 0.2f, 0, 0f);
    }

    void StartAttack()
    {
        act = Act.Attack;
        actClock = 0f;
        attackIndex = preferredAttack;
        actLength = attackLengths[attackIndex];
        attackHitDone = false;
        if (HasController) animator.CrossFadeInFixedTime(AttackStates[attackIndex], 0.2f, 0, 0f);
    }

    void EndAct()
    {
        if (act == Act.Turn) turnCooldown = 0.6f;
        if (animator != null) animator.speed = 1f;
        act = Act.Move;
        actClock = 0f;
        if (HasController) animator.CrossFadeInFixedTime(locoState, 0.25f, 0);
    }

    /// <summary>Root motion из Animator (через ZombieAnimatorRelay): во время Turn зомби поворачивается вместе с клипом.</summary>
    public void OnAnimatorMoved()
    {
        if (IsDead || act != Act.Turn || animator == null) return;
        float yaw = animator.deltaRotation.eulerAngles.y;
        if (yaw > 180f) yaw -= 360f;
        if (!turnSignKnown && Mathf.Abs(yaw) > 0.2f)
        {
            // Первый заметный поворот показывает, куда на самом деле крутит клип; если не туда — разворот выбран зеркально.
            turnSignKnown = true;
            float clipSign = Mathf.Sign(yaw) * (turnMirrored ? -1f : 1f);
            if (clipSign != turnClipSign) { turnClipSign = clipSign; EndAct(); return; }
        }
        transform.rotation = Quaternion.AngleAxis(yaw, Vector3.up) * transform.rotation;
    }

    /// <summary>Между грудью зомби и игроком нет стены или закрытой двери.</summary>
    bool CanReach(Transform target)
    {
        Vector3 from = transform.position + Vector3.up * 1.3f;
        Vector3 to = target.position + Vector3.up * 1.2f;
        Vector3 d = to - from;
        float dist = d.magnitude;
        if (dist < 1e-3f) return true;
        foreach (var h in Physics.RaycastAll(from, d / dist, dist, ~0, QueryTriggerInteraction.Ignore))
        {
            if (h.transform.IsChildOf(transform) || h.transform.IsChildOf(target)) continue;
            if (h.collider.GetComponent<ZombieHitbox>() != null) continue; // другие зомби не мешают
            return false;
        }
        return true;
    }

    // ---------- Прах ----------

    void UpdateCorpse(float dt)
    {
        deathClock += dt;
        headFlinch = Mathf.MoveTowards(headFlinch, 0f, dt * 3f);
        if (deathClock < crumbleStart) return;
        if (!crumbling) StartCrumble();

        float k = Mathf.Clamp01((deathClock - crumbleStart) / crumbleTime);
        // Тело темнеет до пепла, затем оседает в пыль: сплющивается к полу и чуть расползается.
        float ash = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(k * 2.5f));
        foreach (var t in tint)
        {
            if (t.r == null) continue;
            t.r.GetPropertyBlock(mpb, t.index);
            mpb.SetColor(t.prop, Color.Lerp(t.color, ashColor, ash));
            t.r.SetPropertyBlock(mpb, t.index);
        }
        float fall = Mathf.Clamp01((k - 0.25f) / 0.75f);
        fall *= fall;
        pivot.localScale = new Vector3(1f + 0.15f * fall, Mathf.Max(0.02f, 1f - fall), 1f + 0.15f * fall);

        if (k < 1f) return;
        if (!bodyGone)
        {
            // Тело рассыпалось — дальше только прах в воздухе.
            bodyGone = true;
            foreach (var r in model.GetComponentsInChildren<Renderer>()) r.enabled = false;
            foreach (var ps in ashSystems) if (ps != null) ps.Stop(false, ParticleSystemStopBehavior.StopEmitting);
        }
        foreach (var ps in ashSystems) if (ps != null && ps.IsAlive()) return;
        Destroy(gameObject);
    }

    void StartCrumble()
    {
        crumbling = true;
        if (animator != null) animator.enabled = false; // поза замирает — тело сохраняет форму, пока рассыпается
        mpb = new MaterialPropertyBlock();
        var renderers = model.GetComponentsInChildren<SkinnedMeshRenderer>();
        foreach (var r in renderers)
        {
            var mats = r.sharedMaterials;
            for (int i = 0; i < mats.Length; i++)
            {
                var m = mats[i];
                if (m == null) continue;
                // Материалы glTFast (кожа, одежда с цветами вершин) — baseColorFactor, URP Lit — _BaseColor.
                string prop = m.HasProperty("baseColorFactor") ? "baseColorFactor" : m.HasProperty("_BaseColor") ? "_BaseColor" : null;
                if (prop != null) tint.Add((r, i, prop, m.GetColor(prop)));
            }
            if (r.sharedMesh == null) continue;
            // Частиц с каждого меша — по его доле в площади тела (по габаритам).
            float share = Share(r, renderers);
            if (AshFlakeMaterial != null) ashSystems.Add(AshSystem(r, AshFlakeMaterial, true, Mathf.Max(1, Mathf.RoundToInt(ashFlakes * share))));
            if (AshDustMaterial != null) ashSystems.Add(AshSystem(r, AshDustMaterial, false, Mathf.Max(1, Mathf.RoundToInt(ashPuffs * share))));
        }
        if (AshFlakeMaterial == null && AshDustMaterial == null)
            Debug.LogWarning("Zombie: не заданы материалы праха (ZombieSpawner.ashFlakeMaterial / ashDustMaterial) — тело рассыплется без частиц.");
    }

    static float Share(SkinnedMeshRenderer r, SkinnedMeshRenderer[] all)
    {
        float Area(SkinnedMeshRenderer x) { var s = x.sharedMesh != null ? x.sharedMesh.bounds.size : Vector3.zero; return s.x * s.y + s.y * s.z + s.z * s.x; }
        float total = 0f;
        foreach (var x in all) total += Area(x);
        return total > 0f ? Area(r) / total : 1f / all.Length;
    }

    /// <summary>Частицы, срывающиеся с поверхности меша: хлопья пепла или облачка пыли; их уносит ветром вверх.</summary>
    ParticleSystem AshSystem(SkinnedMeshRenderer source, Material mat, bool flakes, int count)
    {
        var go = new GameObject(flakes ? "AshFlakes" : "AshDust");
        go.transform.SetParent(transform, false);
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        var main = ps.main;
        main.playOnAwake = false;
        main.loop = false;
        main.duration = crumbleTime;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = count + 10;
        main.startLifetime = flakes ? new ParticleSystem.MinMaxCurve(1.6f, 3.2f) : new ParticleSystem.MinMaxCurve(1.8f, 3f);
        main.startSpeed = flakes ? new ParticleSystem.MinMaxCurve(0.05f, 0.4f) : new ParticleSystem.MinMaxCurve(0.02f, 0.15f);
        main.startSize = flakes ? new ParticleSystem.MinMaxCurve(0.02f, 0.05f) : new ParticleSystem.MinMaxCurve(0.25f, 0.55f);
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.startColor = flakes
            ? new ParticleSystem.MinMaxGradient(new Color(0.09f, 0.085f, 0.08f), new Color(0.3f, 0.28f, 0.26f))
            : new ParticleSystem.MinMaxGradient(new Color(0.25f, 0.24f, 0.22f, 0.45f), new Color(0.4f, 0.38f, 0.35f, 0.3f));
        main.gravityModifier = flakes ? -0.02f : -0.01f;

        // Выпускаются по ходу рассыпания, больше всего — в середине.
        var em = ps.emission;
        em.enabled = true;
        em.rateOverTime = new ParticleSystem.MinMaxCurve(count / crumbleTime * 1.3f,
            new AnimationCurve(new Keyframe(0f, 0.2f), new Keyframe(0.4f, 1f), new Keyframe(1f, 0.5f)));

        var shape = ps.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.SkinnedMeshRenderer;
        shape.skinnedMeshRenderer = source;
        shape.meshShapeType = ParticleSystemMeshShapeType.Triangle;

        // Ветер: прах поднимается и уходит в сторону, с завихрениями.
        var vel = ps.velocityOverLifetime;
        vel.enabled = true;
        vel.space = ParticleSystemSimulationSpace.World;
        float wind = flakes ? 1f : 0.7f;
        vel.x = new ParticleSystem.MinMaxCurve(ashWind.x * wind * 0.5f, ashWind.x * wind * 1.3f);
        vel.y = new ParticleSystem.MinMaxCurve(ashWind.y * wind * 0.4f, ashWind.y * wind * 1.2f);
        vel.z = new ParticleSystem.MinMaxCurve(ashWind.z * wind * 0.5f, ashWind.z * wind * 1.3f);
        var noise = ps.noise;
        noise.enabled = true;
        noise.strength = flakes ? 0.6f : 0.25f;
        noise.frequency = 0.8f;
        noise.scrollSpeed = 0.4f;
        noise.quality = ParticleSystemNoiseQuality.Low;
        if (flakes)
        {
            var rot = ps.rotationOverLifetime;
            rot.enabled = true;
            rot.z = new ParticleSystem.MinMaxCurve(-4f, 4f);
        }

        var size = ps.sizeOverLifetime;
        size.enabled = true;
        size.size = flakes
            ? new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(0.6f, 0.8f), new Keyframe(1f, 0f)))
            : new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.5f), new Keyframe(1f, 1.6f)));
        var col = ps.colorOverLifetime;
        col.enabled = true;
        var g = new Gradient();
        g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                  new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.08f), new GradientAlphaKey(flakes ? 0.9f : 0.6f, 0.6f), new GradientAlphaKey(0f, 1f) });
        col.color = g;

        var r = go.GetComponent<ParticleSystemRenderer>();
        r.sharedMaterial = mat;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        r.receiveShadows = false;
        ps.Play();
        return ps;
    }

    // ---------- Поверх анимации ----------

    void LateUpdate()
    {
        // Тело рассыпается: Animator выключен, поза замерла. Поправки ниже накапливались бы каждый кадр (голова крутилась бы).
        if (crumbling) return;
        float t = Time.time + timeOffset;
        // В клипах Mixamo нет челюсти — отвисшая и подрагивающая челюсть добавляется здесь.
        if (jaw != null)
        {
            jaw.localRotation = jawRest;
            jaw.rotation = Quaternion.AngleAxis(6f + Mathf.PerlinNoise(t * 1.7f, 0.3f) * 18f + (act == Act.Attack ? 8f : 0f), pivot.right) * jaw.rotation;
        }
        // Попадание в голову — голову откидывает от выстрела.
        if (head != null && headFlinch > 0f)
            head.rotation = Quaternion.AngleAxis(headFlinch * 30f, Vector3.Cross(Vector3.up, flinchDir)) * head.rotation;
    }
}
