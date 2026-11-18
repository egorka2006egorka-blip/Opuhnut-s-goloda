using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Клетка в зале Декана, в ней сидит Шапик (за столом с ноутбуком). Дверь — на передней стороне (+Z), с цепью и амбарным замком.
/// Замок сбивается ломом В РУКАХ: жать E быстро (каждое нажатие — удар ломом). Без лома — не открыть.
/// Начало координат — центр клетки на полу.
/// </summary>
public class ShapikCage : Interactable
{
    public float width = 3f, depth = 3f, height = 2.4f, doorWidth = 1f;
    [Tooltip("Сколько ударов ломом нужно, чтобы сбить замок.")]
    public int hitsNeeded = 6;

    public bool IsOpen { get; private set; }
    Transform hinge, lockGroup;
    float hits, lastHit, openT = -1f, lockedSaid = -10f;
    AudioSource audioSrc;
    PlayerWeaponHandler handler;

    bool CrowbarInHands
    {
        get
        {
            if (handler == null) handler = FindAnyObjectByType<PlayerWeaponHandler>();
            return handler != null && handler.Melee;
        }
    }

    public override string Prompt
    {
        get
        {
            if (!CrowbarInHands)
            {
                var crowbar = CrowbarWeapon.Find();
                return crowbar == null ? "Клетка на замке — нужен лом"
                    : crowbar.Stowed ? "Клетка на замке — достань лом из инвентаря (" + Controls.KeyName(GameAction.Inventory) + ")"
                    : "Клетка на замке — возьми лом в руки";
            }
            int n = Mathf.RoundToInt(hits);
            return "Сбить замок ломом — жми быстро! " + new string('|', n) + new string('.', Mathf.Max(0, hitsNeeded - n));
        }
    }

    public override bool Available => !IsOpen;
    /// <summary>Без лома в руках E ничего не делает — подсказка без клавиши.</summary>
    public override bool Usable => CrowbarInHands;
    public override Vector3 Focus => lockGroup != null ? lockGroup.position : transform.position + transform.forward * depth / 2f + Vector3.up * 1.1f;

    void Awake()
    {
        range = 2.4f;
        audioSrc = gameObject.AddComponent<AudioSource>();
        audioSrc.spatialBlend = 1f;
        audioSrc.minDistance = 2f;
        audioSrc.maxDistance = 25f;
        Build();
    }

    void Build()
    {
        var bar = Mat(new Color(0.22f, 0.23f, 0.24f), 0.45f, 0.75f);
        var rust = Mat(new Color(0.36f, 0.2f, 0.12f), 0.25f, 0.4f);
        float hw = width / 2f, hd = depth / 2f;
        const float step = 0.17f, d = 0.03f;

        // Рама: низ и верх по периметру, угловые стойки, крыша из прутьев.
        void Beam(Vector3 a, Vector3 b, float thick, Material m, bool col)
        {
            Vector3 mid = (a + b) / 2f, dir = b - a;
            var t = Part(transform, PrimitiveType.Cube, mid, new Vector3(thick, thick, dir.magnitude + thick), m, Quaternion.LookRotation(dir).eulerAngles, col);
            t.name = "Frame";
        }
        Vector3[] c = { new Vector3(-hw, 0, -hd), new Vector3(hw, 0, -hd), new Vector3(hw, 0, hd), new Vector3(-hw, 0, hd) };
        for (int i = 0; i < 4; i++)
        {
            Vector3 a = c[i], b = c[(i + 1) % 4];
            Beam(a + Vector3.up * 0.04f, b + Vector3.up * 0.04f, 0.07f, rust, true);
            Beam(a + Vector3.up * height, b + Vector3.up * height, 0.07f, rust, true);
            var post = Part(transform, PrimitiveType.Cube, a + Vector3.up * height / 2f, new Vector3(0.07f, height, 0.07f), rust, default, true);
            post.name = "Post";
        }
        for (float z = -hd + step; z < hd; z += step * 2f)
            Beam(new Vector3(-hw, height, z), new Vector3(hw, height, z), d, bar, false);
        var roof = new GameObject("RoofCollider").AddComponent<BoxCollider>();
        roof.transform.SetParent(transform, false);
        roof.center = new Vector3(0f, height, 0f);
        roof.size = new Vector3(width, 0.05f, depth);

        // Прутья стен (на передней стене — проём под дверь).
        void Bars(Vector3 from, Vector3 to, float skipFrom, float skipTo)
        {
            float len = Vector3.Distance(from, to);
            Vector3 dir = (to - from) / len;
            for (float s = step; s < len - 0.05f; s += step)
            {
                if (s > skipFrom && s < skipTo) continue;
                var p = from + dir * s;
                var t = Part(transform, PrimitiveType.Cylinder, p + Vector3.up * height / 2f, new Vector3(d, height / 2f, d), bar, default, true);
                t.name = "Bar";
            }
        }
        Bars(c[0], c[1], -1f, -1f);
        Bars(c[1], c[2], -1f, -1f);
        Bars(c[3], c[0], -1f, -1f);
        float doorFrom = hw - doorWidth / 2f, doorTo = hw + doorWidth / 2f; // по центру передней стены
        Bars(c[2], c[3], width - doorTo - 0.02f, width - doorFrom + 0.02f);

        // Дверь: петля слева, прутья, поперечины; цепь и замок у правого края.
        hinge = Node(transform, "DoorHinge", new Vector3(-doorWidth / 2f, 0f, hd));
        for (float x = step / 2f; x < doorWidth; x += step)
        {
            var t = Part(hinge, PrimitiveType.Cylinder, new Vector3(x, height / 2f, 0f), new Vector3(d, height / 2f - 0.05f, d), bar, default, true);
            t.name = "DoorBar";
        }
        foreach (float y in new[] { 0.12f, height * 0.5f, height - 0.12f })
            Part(hinge, PrimitiveType.Cube, new Vector3(doorWidth / 2f, y, 0f), new Vector3(doorWidth, 0.05f, 0.04f), rust);

        lockGroup = Node(transform, "Lock", new Vector3(doorWidth / 2f, 1.1f, hd + 0.05f));
        var dark = Mat(new Color(0.12f, 0.12f, 0.12f), 0.5f, 0.8f);
        for (int i = 0; i < 10; i++)
        {
            float a = i / 10f * Mathf.PI * 2f;
            Part(lockGroup, PrimitiveType.Cube, new Vector3(Mathf.Cos(a) * 0.09f, Mathf.Sin(a) * 0.12f, 0f), new Vector3(0.05f, 0.022f, 0.02f), dark, new Vector3(0f, 0f, a * Mathf.Rad2Deg + (i % 2) * 90f));
        }
        Part(lockGroup, PrimitiveType.Cube, new Vector3(0f, -0.16f, 0.02f), new Vector3(0.09f, 0.1f, 0.04f), Mat(new Color(0.65f, 0.5f, 0.2f), 0.6f, 0.9f));
        Part(lockGroup, PrimitiveType.Cylinder, new Vector3(0f, -0.09f, 0.02f), new Vector3(0.065f, 0.012f, 0.065f), dark, new Vector3(90f, 0f, 0f));
    }

    static Transform Part(Transform parent, PrimitiveType type, Vector3 pos, Vector3 size, Material m, Vector3 euler, bool collider) =>
        Interactable.Part(parent, type, pos, size, m, euler, collider);

    public override void Interact(GameObject player)
    {
        if (IsOpen) return;
        if (!CrowbarInHands)
        {
            if (Time.time - lockedSaid > 6f) { lockedSaid = Time.time; DialogueSystem.Say("door_locked"); }
            return;
        }
        handler.PlaySwing();
        hits += 1f;
        lastHit = Time.time;
        audioSrc.pitch = Random.Range(0.9f, 1.15f);
        audioSrc.PlayOneShot(Clang(), GameSettings.EffectsVolume);
        var fpc = player.GetComponent<FirstPersonController>();
        if (fpc != null) fpc.AddShake(0.35f);
        lockGroup.localRotation = Quaternion.Euler(0f, 0f, Random.Range(-25f, 25f));
        if (hits >= hitsNeeded) Break();
    }

    /// <summary>Сразу открыть (загрузка сохранения).</summary>
    public void OpenInstant()
    {
        IsOpen = true;
        if (lockGroup != null) Destroy(lockGroup.gameObject);
        hinge.localRotation = Quaternion.Euler(0f, -100f, 0f);
    }

    void Break()
    {
        IsOpen = true;
        openT = 0f;
        audioSrc.pitch = 0.7f;
        audioSrc.PlayOneShot(Clang(), GameSettings.EffectsVolume * 1.2f);
        var cc = FindAnyObjectByType<CharacterController>();
        var parts = new List<Transform>();
        foreach (Transform t in lockGroup) parts.Add(t);
        foreach (var link in parts)
        {
            link.SetParent(null, true);
            var col = link.gameObject.AddComponent<BoxCollider>();
            var rb = link.gameObject.AddComponent<Rigidbody>();
            rb.mass = 0.3f;
            rb.linearVelocity = Random.insideUnitSphere * 2f + transform.forward * 1.5f;
            if (cc != null) Physics.IgnoreCollision(col, cc);
            Destroy(link.gameObject, 12f);
        }
        Destroy(lockGroup.gameObject);
        if (StoryDirector.Instance != null) StoryDirector.Instance.OnDoorOpened();
    }

    void Update()
    {
        if (!IsOpen && hits > 0f && Time.time - lastHit > 0.8f) hits = Mathf.Max(0f, hits - Time.deltaTime * 3f);
        if (lockGroup != null && !IsOpen) lockGroup.localRotation = Quaternion.Slerp(lockGroup.localRotation, Quaternion.identity, Time.deltaTime * 6f);
        if (openT >= 0f && openT < 1f)
        {
            openT = Mathf.Min(1f, openT + Time.deltaTime / 0.9f);
            float k = 1f - Mathf.Pow(1f - openT, 3f);
            hinge.localRotation = Quaternion.Euler(0f, -100f * k + Mathf.Sin(openT * Mathf.PI * 3f) * 6f * (1f - openT), 0f);
        }
    }

    static AudioClip clang;

    static AudioClip Clang()
    {
        if (clang != null) return clang;
        const int rate = 44100;
        int n = rate / 2;
        var data = new float[n];
        var rnd = new System.Random(3);
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)rate;
            float ring = Mathf.Sin(2f * Mathf.PI * 820f * t) * 0.5f + Mathf.Sin(2f * Mathf.PI * 1370f * t) * 0.35f + Mathf.Sin(2f * Mathf.PI * 2210f * t) * 0.2f;
            data[i] = (ring * Mathf.Exp(-t * 9f) + ((float)rnd.NextDouble() * 2f - 1f) * Mathf.Exp(-t * 60f)) * 0.7f;
        }
        clang = AudioClip.Create("Clang", n, 1, rate, false);
        clang.SetData(data, 0);
        return clang;
    }
}
