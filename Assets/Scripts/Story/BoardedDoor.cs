using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Заколоченная дверь (в задней стене подвала, за ней — лестница наверх в серверную). Пока на ней доски, дверь (SwingGate) заперта.
/// Доски отрываются ломом В РУКАХ: каждое нажатие E (или удар ЛКМ по доске) поддевает верхнюю доску, после pryPerPlank рывков
/// она с треском отлетает. Когда досок не осталось, дверь отпирается и распахивается.
/// Начало координат — середина проёма на полу, +Z — в сторону, откуда отрывают доски (в подвал).
/// </summary>
public class BoardedDoor : Interactable
{
    [Tooltip("Дверь под досками: заперта, пока доски не оторваны.")]
    public SwingGate door;
    public float width = 1f, height = 2.1f;
    public int planks = 4;
    [Tooltip("Сколько рывков ломом нужно на одну доску.")]
    public int pryPerPlank = 2;

    public bool IsOpen { get; private set; }
    readonly List<Transform> boards = new List<Transform>();
    int pries;
    float lockedSaid = -10f;
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
                return crowbar == null ? "Дверь заколочена — нужен лом"
                    : crowbar.Stowed ? "Дверь заколочена — достань лом из инвентаря (" + Controls.KeyName(GameAction.Inventory) + ")"
                    : "Дверь заколочена — возьми лом в руки";
            }
            return "Отдирать доски ломом (осталось " + boards.Count + ")";
        }
    }

    public override bool Available => !IsOpen;
    /// <summary>Без лома в руках E ничего не делает — подсказка без клавиши.</summary>
    public override bool Usable => CrowbarInHands;
    public override Vector3 Focus => boards.Count > 0 && boards[boards.Count - 1] != null
        ? boards[boards.Count - 1].position : transform.position + Vector3.up * height * 0.5f;

    void Awake()
    {
        range = 2.4f;
        audioSrc = gameObject.AddComponent<AudioSource>();
        audioSrc.spatialBlend = 1f;
        audioSrc.minDistance = 2f;
        audioSrc.maxDistance = 25f;
        Build();
    }

    void Start()
    {
        if (door == null) return;
        door.Locked = !IsOpen;
        door.lockedMessage = "Дверь заколочена — нужен лом";
        door.lockedSpeaker = "STUDENT";
    }

    void Build()
    {
        var wood = Mat(new Color(0.45f, 0.32f, 0.2f), 0.15f);
        var woodDark = Mat(new Color(0.33f, 0.23f, 0.14f), 0.15f);
        var nail = Mat(new Color(0.25f, 0.25f, 0.25f), 0.5f, 0.8f);
        // Доски поверх проёма с заходом на коробку, под небольшим углом; последние — снаружи (их отрывают первыми).
        float[] tilt = { -11f, 8f, -26f, 14f, -6f, 22f };
        float len = width + 0.4f;
        for (int i = 0; i < planks; i++)
        {
            float y = Mathf.Lerp(0.45f, height - 0.35f, planks == 1 ? 0.5f : (float)i / (planks - 1));
            var board = Node(transform, "Plank_" + (i + 1), new Vector3(0f, y, 0.04f + i * 0.03f));
            board.localRotation = Quaternion.Euler(0f, 0f, tilt[i % tilt.Length]);
            var plank = Part(board, PrimitiveType.Cube, Vector3.zero, new Vector3(len, 0.15f, 0.025f), i % 2 == 0 ? wood : woodDark, default, true);
            plank.name = "Board";
            foreach (float sx in new[] { -1f, 1f })
                foreach (float sy in new[] { -0.035f, 0.035f })
                    Part(board, PrimitiveType.Cube, new Vector3(sx * (len / 2f - 0.09f), sy, 0.014f), new Vector3(0.012f, 0.012f, 0.006f), nail);
            boards.Add(board);
        }
    }

    public override void Interact(GameObject player)
    {
        if (IsOpen || boards.Count == 0) return;
        if (!CrowbarInHands)
        {
            if (Time.time - lockedSaid > 6f) { lockedSaid = Time.time; DialogueSystem.Say("door_locked"); }
            return;
        }
        handler.PlaySwing();
        var fpc = player.GetComponent<FirstPersonController>();
        if (fpc != null) fpc.AddShake(0.3f);
        var board = boards[boards.Count - 1];
        audioSrc.pitch = Random.Range(0.85f, 1.15f);
        if (++pries < pryPerPlank)
        {
            // Доска поддета: отходит от двери и перекашивается.
            audioSrc.PlayOneShot(Creak(), GameSettings.EffectsVolume);
            board.localPosition += Vector3.forward * 0.04f;
            board.localRotation *= Quaternion.Euler(Random.Range(-6f, 6f), Random.Range(-8f, 8f), Random.Range(-5f, 5f));
            return;
        }
        pries = 0;
        audioSrc.PlayOneShot(Crack(), GameSettings.EffectsVolume * 1.2f);
        boards.RemoveAt(boards.Count - 1);
        RipOff(board);
        if (boards.Count == 0) Open(player.transform.position);
    }

    /// <summary>Доска отлетает в сторону игрока и падает на пол (сквозь игрока — чтобы не застревал).</summary>
    void RipOff(Transform board)
    {
        board.SetParent(null, true);
        var rb = board.gameObject.AddComponent<Rigidbody>();
        rb.mass = 2f;
        rb.linearVelocity = transform.forward * Random.Range(2f, 3.2f) + Vector3.up * 1.2f + transform.right * Random.Range(-1f, 1f);
        rb.angularVelocity = Random.insideUnitSphere * 6f;
        var cc = FindAnyObjectByType<CharacterController>();
        if (cc != null)
            foreach (var col in board.GetComponentsInChildren<Collider>()) Physics.IgnoreCollision(col, cc);
    }

    void Open(Vector3 viewer)
    {
        IsOpen = true;
        if (door != null)
        {
            door.Locked = false;
            if (!door.IsOpen) door.Toggle(viewer); // от рывка дверь со скрипом распахивается
        }
        if (StoryDirector.Instance != null) StoryDirector.Instance.OnDoorOpened();
    }

    /// <summary>Сразу открыть (загрузка сохранения): досок нет, дверь не заперта.</summary>
    public void OpenInstant()
    {
        IsOpen = true;
        foreach (var b in boards) if (b != null) Destroy(b.gameObject);
        boards.Clear();
        if (door != null) door.Locked = false;
    }

    // ---------- Звуки ----------

    static AudioClip creak, crack;

    /// <summary>Скрип: доска с гвоздями поддаётся.</summary>
    static AudioClip Creak()
    {
        if (creak != null) return creak;
        const int rate = 44100;
        int n = (int)(rate * 0.4f);
        var data = new float[n];
        var rnd = new System.Random(7);
        float phase = 0f;
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)rate;
            float f = Mathf.Lerp(330f, 170f, t / 0.4f) + Mathf.Sin(t * 90f) * 25f;
            phase += f / rate;
            float saw = (phase % 1f) * 2f - 1f;
            float env = Mathf.Sin(Mathf.PI * t / 0.4f);
            data[i] = (saw * 0.45f + ((float)rnd.NextDouble() * 2f - 1f) * 0.15f) * env * 0.6f;
        }
        creak = AudioClip.Create("PlankCreak", n, 1, rate, false);
        creak.SetData(data, 0);
        return creak;
    }

    /// <summary>Треск: доска отрывается, гвозди вылетают.</summary>
    static AudioClip Crack()
    {
        if (crack != null) return crack;
        const int rate = 44100;
        int n = rate / 2;
        var data = new float[n];
        var rnd = new System.Random(13);
        float lp = 0f;
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)rate;
            float noise = (float)rnd.NextDouble() * 2f - 1f;
            lp += (noise - lp) * 0.25f;
            float thump = Mathf.Sin(2f * Mathf.PI * 95f * t) * Mathf.Exp(-t * 18f);
            float squeal = Mathf.Sin(2f * Mathf.PI * 1900f * t) * Mathf.Exp(-t * 30f) * 0.25f;
            data[i] = (lp * Mathf.Exp(-t * 14f) * 0.9f + thump * 0.6f + squeal) * 0.8f;
        }
        crack = AudioClip.Create("PlankCrack", n, 1, rate, false);
        crack.SetData(data, 0);
        return crack;
    }
}
