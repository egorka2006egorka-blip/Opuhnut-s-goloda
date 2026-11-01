using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Зомби: идёт к игроку по NavMesh, вблизи замахивается руками, получает урон от пуль (ZombieHitbox),
/// вздрагивает от попаданий, после смерти падает, лежит и уходит под пол.
/// Анимация процедурная (в моделях Zombie_*.glb только скелет в T-позе, без клипов):
/// шаркающая походка с хромотой, наклон корпуса, вытянутые вперёд или висящие руки, скрюченные пальцы, отвисшая челюсть.
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
    public float headMultiplier = 4f;
    public float limbMultiplier = 0.6f;

    [Header("Движение")]
    public float walkSpeed = 1.1f;
    [Tooltip("Длина шага (м): чем меньше, тем чаще перебирает ногами.")]
    public float strideLength = 0.8f;
    public float attackRange = 1.3f;
    [Tooltip("Длительность одного замаха (с).")]
    public float attackInterval = 1.2f;
    public float repathInterval = 0.25f;

    [Header("Атака")]
    [Tooltip("Урон за один удар.")]
    public float attackDamage = 10f;
    [Tooltip("Момент удара внутри замаха (доля attackInterval): руки опускаются на игрока.")]
    [Range(0f, 1f)] public float hitMoment = 0.6f;
    [Tooltip("С какого расстояния зомби открывает закрытую дверь перед собой (м).")]
    public float doorOpenReach = 1.1f;

    [Header("После смерти")]
    public float fallDuration = 0.75f;
    [Tooltip("Сколько тело лежит, прежде чем исчезнуть (с).")]
    public float corpseTime = 10f;
    public float sinkTime = 2.5f;

    public float Health { get; private set; }
    public bool IsDead { get; private set; }

    class Arm
    {
        public bool right;
        public Transform upper, fore, hand;
        public readonly List<Transform> fingers = new List<Transform>();
        public Vector3 curlAxisLocal;
        public bool reach; // руки вытянуты вперёд; иначе висят
    }

    class Leg
    {
        public bool right;
        public Transform thigh, shin, foot;
    }

    static Transform player;
    static PlayerHealth playerHealth;

    Transform pivot, model;
    NavMeshAgent agent;
    Transform hips, spine, chest, neck, head, jaw;
    readonly Arm[] arms = new Arm[2];
    readonly Leg[] legs = new Leg[2];
    readonly List<Transform> restBones = new List<Transform>();
    readonly List<Quaternion> restRots = new List<Quaternion>();
    readonly List<Collider> hitboxes = new List<Collider>();

    // Индивидуальность: у каждого зомби своя походка.
    float speedMul, lean, headTilt, limp, timeOffset;
    int limpLeg;

    float phase, move, attackWeight, attackClock, nextRepath;
    float flinch, headFlinch, stagger;
    Vector3 flinchDir = Vector3.forward, lastPos;
    float deathClock;
    Vector3 fallDir;

    /// <summary>Создаёт зомби из модели prefab в точке pos на NavMesh.</summary>
    public static Zombie Create(GameObject prefab, Vector3 pos, Quaternion rot, int agentTypeId)
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
        foreach (var a in model.GetComponentsInChildren<Animator>(true)) a.enabled = false;
        foreach (var a in model.GetComponentsInChildren<Animation>(true)) a.enabled = false;

        var z = root.AddComponent<Zombie>();
        z.Init(pivot, model.transform, agentTypeId);
        root.SetActive(true);
        return z;
    }

    void OnEnable() => All.Add(this);
    void OnDisable() => All.Remove(this);

    // ---------- Подготовка ----------

    void Init(Transform pivotT, Transform modelT, int agentTypeId)
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
        // Ступни — на пол: самая нижняя точка мешей на уровне корня.
        float minY = float.MaxValue;
        foreach (var smr in model.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            if (smr.sharedMesh == null) continue;
            Bounds mb = smr.sharedMesh.bounds;
            for (int i = 0; i < 8; i++)
            {
                Vector3 c = mb.center + Vector3.Scale(mb.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                minY = Mathf.Min(minY, pivot.InverseTransformPoint(smr.transform.TransformPoint(c)).y);
            }
        }
        if (minY < 0.5f && minY > -0.5f) model.localPosition -= Vector3.up * minY;

        // Какая сторона скелета физически справа.
        Transform upR = B("UpperArm.R"), upL = B("UpperArm.L");
        bool namesMatch = upR == null || upL == null || pivot.InverseTransformPoint(upR.position).x > pivot.InverseTransformPoint(upL.position).x;
        string rs = namesMatch ? "R" : "L", ls = namesMatch ? "L" : "R";

        hips = B("Hips"); spine = B("Spine"); chest = B("Chest"); neck = B("Neck"); head = B("Head"); jaw = B("Jaw");
        for (int i = 0; i < 2; i++)
        {
            string s = i == 0 ? rs : ls;
            var a = new Arm { right = i == 0, upper = B("UpperArm." + s), fore = B("Forearm." + s), hand = B("Hand." + s) };
            foreach (var f in new[] { "Index", "Middle", "Ring", "Pinky" })
                for (int j = 1; j <= 3; j++) { var fb = B(f + j + "." + s); if (fb != null) a.fingers.Add(fb); }
            arms[i] = a.upper != null && a.fore != null && a.hand != null ? a : null;
            var l = new Leg { right = i == 0, thigh = B("Thigh." + s), shin = B("Shin." + s), foot = B("Foot." + s) };
            legs[i] = l.thigh != null && l.shin != null ? l : null;
        }

        foreach (var t in new[] { hips, spine, chest, neck, head, jaw }) AddRest(t);
        foreach (var a in arms)
        {
            if (a == null) continue;
            AddRest(a.upper); AddRest(a.fore); AddRest(a.hand);
            foreach (var f in a.fingers) AddRest(f);
            // Ось сгибания пальцев к ладони (в T-позе ладонь смотрит вниз).
            var middle = B("Middle1." + (a.right ? rs : ls));
            Vector3 F = middle != null ? (middle.position - a.hand.position).normalized : (a.hand.position - a.fore.position).normalized;
            a.curlAxisLocal = a.hand.InverseTransformDirection(Vector3.Cross(F, -pivot.up).normalized);
        }
        foreach (var l in legs)
            if (l != null) { AddRest(l.thigh); AddRest(l.shin); }

        speedMul = Random.Range(0.8f, 1.3f);
        lean = Random.Range(8f, 22f);
        headTilt = Random.Range(-18f, 18f);
        limpLeg = Random.Range(0, 2);
        limp = Random.value < 0.6f ? Random.Range(0.3f, 0.8f) : 0f;
        timeOffset = Random.Range(0f, 100f);
        if (arms[0] != null) arms[0].reach = Random.value < 0.75f;
        if (arms[1] != null) arms[1].reach = Random.value < 0.75f;

        BuildHitboxes();

        var rb = gameObject.AddComponent<Rigidbody>();
        rb.isKinematic = true; // коллайдеры костей движутся вместе с анимацией

        agent = gameObject.AddComponent<NavMeshAgent>();
        agent.agentTypeID = agentTypeId;
        agent.radius = 0.32f;
        agent.height = 1.8f;
        agent.baseOffset = 0f;
        agent.speed = walkSpeed * speedMul;
        agent.acceleration = 6f;
        agent.angularSpeed = 160f;
        agent.stoppingDistance = attackRange * 0.8f;
        agent.autoBraking = true;
        agent.obstacleAvoidanceType = ObstacleAvoidanceType.MedQualityObstacleAvoidance;
        agent.avoidancePriority = Random.Range(30, 70);

        lastPos = transform.position;
    }

    void AddRest(Transform t)
    {
        if (t == null || restBones.Contains(t)) return;
        restBones.Add(t);
        restRots.Add(t.localRotation);
    }

    static string Simplify(string s)
    {
        var sb = new System.Text.StringBuilder();
        foreach (char c in s) if (char.IsLetterOrDigit(c)) sb.Append(char.ToLowerInvariant(c));
        return sb.ToString();
    }

    void BuildHitboxes()
    {
        // Туловище — от таза до шеи, шея — до головы.
        if (hips != null && neck != null) AddCapsule(hips, hips, neck, 0.17f, 1f, false);
        if (neck != null && head != null) AddCapsule(neck, neck, head, 0.07f, 1f, false);

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
            RegisterHitbox(go, sc, headMultiplier, true);
        }

        foreach (var a in arms)
        {
            if (a == null) continue;
            AddCapsule(a.upper, a.upper, a.fore, 0.065f, limbMultiplier, false);
            AddCapsule(a.fore, a.fore, a.hand, 0.055f, limbMultiplier, false);
        }
        foreach (var l in legs)
        {
            if (l == null) continue;
            AddCapsule(l.thigh, l.thigh, l.shin, 0.09f, limbMultiplier, false);
            Transform end = l.foot != null && l.foot.IsChildOf(l.shin) ? l.foot : (l.shin.childCount > 0 ? l.shin.GetChild(0) : null);
            if (end != null) AddCapsule(l.shin, l.shin, end, 0.07f, limbMultiplier, false);
        }
    }

    /// <summary>Капсула от from до to, прикреплённая к кости parent.</summary>
    void AddCapsule(Transform parent, Transform from, Transform to, float radius, float mult, bool isHead)
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
        RegisterHitbox(go, c, mult, isHead);
    }

    void RegisterHitbox(GameObject go, Collider c, float mult, bool isHead)
    {
        var hb = go.AddComponent<ZombieHitbox>();
        hb.owner = this;
        hb.multiplier = mult;
        hb.isHead = isHead;
        hitboxes.Add(c);
    }

    static float Scale(Transform t)
    {
        Vector3 s = t.lossyScale;
        return Mathf.Max(1e-4f, (Mathf.Abs(s.x) + Mathf.Abs(s.y) + Mathf.Abs(s.z)) / 3f);
    }

    // ---------- Урон ----------

    /// <summary>Попадание пули: damage — урон пули (множитель части тела применяется здесь).</summary>
    public void TakeHit(float damage, Vector3 point, Vector3 direction, ZombieHitbox part)
    {
        if (IsDead) return;
        float mult = part != null ? part.multiplier : 1f;
        Health -= damage * mult;

        Vector3 flat = Vector3.ProjectOnPlane(direction, Vector3.up);
        if (flat.sqrMagnitude > 1e-4f) flinchDir = flat.normalized;
        flinch = Mathf.Min(1f, flinch + 0.5f);
        if (part != null && part.isHead) headFlinch = 1f;
        stagger = 0.35f;

        if (Health <= 0f) Die();
    }

    void Die()
    {
        IsDead = true;
        deathClock = 0f;
        fallDir = flinchDir; // падает от стрелка
        foreach (var c in hitboxes) if (c != null) c.enabled = false;
        if (agent != null) agent.enabled = false;
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

        flinch = Mathf.MoveTowards(flinch, 0f, dt * 3f);
        headFlinch = Mathf.MoveTowards(headFlinch, 0f, dt * 3f);
        stagger = Mathf.Max(0f, stagger - dt);

        // Закрытая дверь на пути — толкаем её (NavMesh проходит сквозь дверные проёмы).
        SwingGate.OpenNear(transform.position, doorOpenReach);

        bool playerDead = playerHealth != null && playerHealth.IsDead;
        bool inRange = false;
        if (player != null && playerDead)
        {
            // Игрок мёртв — зомби останавливаются возле него.
            if (agent != null && agent.isOnNavMesh) agent.isStopped = true;
        }
        else if (player != null)
        {
            Vector3 to = player.position - transform.position;
            float dy = to.y;
            to.y = 0f;
            inRange = to.magnitude <= attackRange && Mathf.Abs(dy) < 1.5f;

            if (agent != null && agent.isOnNavMesh)
            {
                agent.speed = walkSpeed * speedMul * (stagger > 0f ? 0.2f : 1f);
                agent.updateRotation = !inRange;
                agent.isStopped = inRange;
                if (!inRange && Time.time >= nextRepath)
                {
                    agent.SetDestination(player.position);
                    nextRepath = Time.time + repathInterval;
                }
            }
            if (inRange && to.sqrMagnitude > 1e-4f)
                transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(to), 220f * dt);
        }

        attackWeight = Mathf.MoveTowards(attackWeight, inRange ? 1f : 0f, dt * 3f);
        float prevAttack = attackClock / attackInterval;
        attackClock = attackWeight > 0f ? attackClock + dt : 0f;
        float curAttack = attackClock / attackInterval;
        // Удар: руки дошли до нижней точки замаха (раз за цикл), игрок рядом и не за стеной/дверью.
        if (inRange && attackWeight > 0.6f && stagger <= 0f
            && Mathf.Floor(curAttack - hitMoment) > Mathf.Floor(prevAttack - hitMoment)
            && playerHealth != null && CanReach(player))
            playerHealth.TakeDamage(attackDamage, transform.position);

        // Шаг — по фактической скорости (упор в стену или других зомби не даёт «бежать на месте»).
        Vector3 v = transform.position - lastPos; v.y = 0f;
        lastPos = transform.position;
        float speed = dt > 0f ? v.magnitude / dt : 0f;
        move = Mathf.MoveTowards(move, Mathf.Clamp01(speed / Mathf.Max(0.1f, walkSpeed * speedMul)), dt * 3f);
        phase += speed / strideLength * Mathf.PI * dt;
        if (phase > Mathf.PI * 200f) phase -= Mathf.PI * 200f;
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

    void UpdateCorpse(float dt)
    {
        deathClock += dt;
        if (deathClock > corpseTime + sinkTime) { Destroy(gameObject); return; }
    }

    // ---------- Анимация ----------

    void LateUpdate()
    {
        for (int i = 0; i < restBones.Count; i++) restBones[i].localRotation = restRots[i];

        float death = 0f;
        if (IsDead)
        {
            // Падение с ускорением, небольшой отскок о пол; потом тело медленно уходит под пол.
            float k = Mathf.Clamp01(deathClock / fallDuration);
            death = k * k;
            float bounce = deathClock > fallDuration ? Mathf.Sin(Mathf.Clamp01((deathClock - fallDuration) / 0.25f) * Mathf.PI) * 0.05f : 0f;
            float sink = Mathf.Clamp01((deathClock - corpseTime) / sinkTime);
            Vector3 axis = Vector3.Cross(Vector3.up, fallDir);
            pivot.rotation = Quaternion.AngleAxis(84f * death - bounce * 60f, axis) * transform.rotation;
            pivot.position = transform.position + Vector3.up * (0.13f * death - 0.6f * sink);
        }
        else
        {
            // Покачивание корпуса из стороны в сторону и вниз-вверх в такт шагам.
            float ph = phase;
            pivot.localPosition = new Vector3(Mathf.Sin(ph) * 0.04f * move, -Mathf.Abs(Mathf.Sin(ph)) * 0.025f * move, 0f);
            pivot.localRotation = Quaternion.Euler(0f, 0f, Mathf.Sin(ph) * 3f * move);
        }

        Vector3 R = pivot.right, U = pivot.up, F = pivot.forward;
        float t = Time.time + timeOffset;
        float alive = 1f - death;

        // Корпус: наклон вперёд, скручивание в такт шагам, вздрагивание от попаданий.
        float twist = Mathf.Sin(phase) * 7f * move * alive;
        Vector3 flinchAxis = Vector3.Cross(Vector3.up, flinchDir);
        if (hips != null) hips.rotation = Quaternion.AngleAxis(twist, U) * hips.rotation;
        if (spine != null)
            spine.rotation = Quaternion.AngleAxis(flinch * 14f, flinchAxis) *
                             Quaternion.AngleAxis(lean * 0.5f * alive + Mathf.Sin(t * 1.3f) * 1.5f, R) * spine.rotation;
        if (chest != null)
            chest.rotation = Quaternion.AngleAxis(lean * 0.5f * alive + attackWeight * Mathf.Sin(AttackPhase(0f) * Mathf.PI * 2f) * 6f, R) *
                             Quaternion.AngleAxis(-twist * 1.4f, U) * chest.rotation;
        if (head != null)
            head.rotation = Quaternion.AngleAxis(headFlinch * 35f, flinchAxis) *
                            Quaternion.AngleAxis(-lean * 0.6f * alive + Mathf.Sin(t * 0.9f) * 4f + death * 25f, R) *
                            Quaternion.AngleAxis(headTilt * (1f + death) + Mathf.Sin(t * 0.6f) * 5f, F) * head.rotation;
        if (jaw != null)
            jaw.rotation = Quaternion.AngleAxis(6f + Mathf.PerlinNoise(t * 1.7f, 0.3f) * 18f + attackWeight * 8f + death * 12f, R) * jaw.rotation;

        foreach (var a in arms) if (a != null) PoseArm(a, R, U, F, t, death);
        foreach (var l in legs) if (l != null) PoseLeg(l, R, death);
    }

    float AttackPhase(float offset) => Mathf.Repeat(attackClock / attackInterval + offset, 1f);

    void PoseArm(Arm a, Vector3 R, Vector3 U, Vector3 F, float t, float death)
    {
        float s = a.right ? 1f : -1f;
        // Мах рук в противофазе ногам; у вытянутых рук — лёгкое покачивание.
        float swing = Mathf.Sin(phase + (a.right ? Mathf.PI : 0f)) * move;
        float bob = Mathf.Sin(t * 1.4f + s) * 0.06f;

        Vector3 upperDir, foreDir;
        if (a.reach)
        {
            upperDir = F * 0.9f + U * (-0.2f + bob + swing * 0.05f) + R * (s * 0.17f);
            foreDir = F + U * (0.03f + bob) - R * (s * 0.06f);
        }
        else
        {
            upperDir = -U + F * (0.12f + swing * 0.3f) + R * (s * 0.2f);
            foreDir = -U + F * (0.35f + swing * 0.25f) + R * (s * 0.05f);
        }

        // Атака: замах вверх-вперёд и удар вниз, руки — вразнобой.
        if (attackWeight > 0f)
        {
            float p = AttackPhase(a.right ? 0f : 0.18f);
            float raise = p < 0.5f ? Mathf.SmoothStep(0f, 1f, p / 0.5f) : 1f - Mathf.SmoothStep(0f, 1f, (p - 0.5f) / 0.2f);
            Vector3 up = F * 0.45f + U * 0.85f + R * (s * 0.25f);
            Vector3 down = F + U * -0.55f - R * (s * 0.1f);
            Vector3 atkUpper = Vector3.Slerp(down.normalized, up.normalized, raise);
            Vector3 atkFore = Vector3.Slerp((F - U * 0.6f).normalized, (F * 0.3f + U).normalized, raise);
            upperDir = Vector3.Slerp(upperDir.normalized, atkUpper, attackWeight);
            foreDir = Vector3.Slerp(foreDir.normalized, atkFore, attackWeight);
        }

        // Смерть: руки безвольно падают на пол.
        if (death > 0f)
        {
            Vector3 limp = (Vector3.down + R * (s * 0.6f)).normalized;
            upperDir = Vector3.Slerp(upperDir.normalized, limp, death);
            foreDir = Vector3.Slerp(foreDir.normalized, limp, death);
        }

        Aim(a.upper, a.fore, upperDir);
        Aim(a.fore, a.hand, foreDir);
        // Кисть свисает, пальцы скрючены.
        a.hand.rotation = Quaternion.AngleAxis(a.reach ? 25f : 10f, R) * a.hand.rotation;
        if (a.fingers.Count > 0)
        {
            Vector3 axis = a.hand.TransformDirection(a.curlAxisLocal);
            float curl = 28f + Mathf.Sin(t * 2.1f + s) * 6f + attackWeight * 15f - death * 15f;
            foreach (var f in a.fingers) f.rotation = Quaternion.AngleAxis(curl, axis) * f.rotation;
        }
    }

    void PoseLeg(Leg l, Vector3 R, float death)
    {
        bool drag = limp > 0f && (limpLeg == 0) == l.right;
        float ph = phase + (l.right ? 0f : Mathf.PI);
        float m = move * (1f - death);
        float swing = Mathf.Sin(ph) * 22f * (drag ? 1f - limp * 0.5f : 1f) * m;
        // Колено сгибается, когда нога идёт вперёд; волочащаяся нога почти не сгибается.
        float knee = Mathf.Max(0f, Mathf.Cos(ph)) * 38f * (drag ? 1f - limp : 1f) * m + 6f + death * 30f;
        l.thigh.rotation = Quaternion.AngleAxis(-swing - knee * 0.3f, R) * l.thigh.rotation;
        l.shin.rotation = Quaternion.AngleAxis(knee, R) * l.shin.rotation;
    }

    static void Aim(Transform bone, Transform child, Vector3 dir)
    {
        Vector3 cur = child.position - bone.position;
        if (cur.sqrMagnitude < 1e-8f || dir.sqrMagnitude < 1e-8f) return;
        bone.rotation = Quaternion.FromToRotation(cur, dir) * bone.rotation;
    }
}
