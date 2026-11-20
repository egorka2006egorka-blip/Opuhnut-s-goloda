using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Кодовый навесной замок на двери (дверь в котельную подвала). E — подойти к замку и крутить колёсики:
/// цифры 0–9 — набрать, ←/→ (A/D) — выбрать колёсико, ↑/↓ (W/S) — крутить, Enter/E — проверить, Esc — отойти.
/// Верный код — замок падает, дверь отпирается и открывается. Удар зомби прерывает набор.
/// Начало координат — на лицевой поверхности стены у края двери (в проушине), +Z — к игроку, Y — вверх;
/// дверь — по локальной оси X в сторону doorDir.
/// </summary>
public class CodeLock : Interactable
{
    public string code = "2402";
    [Tooltip("Дверь, которую запирает замок.")]
    public SwingGate gate;
    [Tooltip("В какую сторону по локальной X от замка находится полотно двери (+1 / −1).")]
    public float doorDir = -1f;

    /// <summary>Замок, на котором сейчас набирают код (или null).</summary>
    public static CodeLock Active { get; private set; }
    public bool Unlocked { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => Active = null;

    /// <summary>Закрыть окно набора (Esc из меню паузы).</summary>
    public static void CloseActive() { if (Active != null) Active.Exit(); }

    int[] digits;
    int sel, openFrame;
    float flashUntil, lastHealth, shake;
    bool flashOk;
    PlayerHealth health;
    Transform padlock, shackle;
    Transform[] wheels;
    AudioSource audioSrc;

    public override string Prompt => "Кодовый замок — набрать код";
    public override bool Available => !Unlocked && Active == null;
    public override Vector3 Focus => padlock != null ? padlock.position : transform.position;

    void Awake()
    {
        range = 2.2f;
        digits = new int[Mathf.Max(1, code.Length)];
        audioSrc = gameObject.AddComponent<AudioSource>();
        audioSrc.spatialBlend = 0.8f;
        audioSrc.minDistance = 1f;
        audioSrc.maxDistance = 12f;

        var steel = Mat(new Color(0.55f, 0.56f, 0.58f), 0.65f, 0.9f);
        var dark = Mat(new Color(0.1f, 0.1f, 0.11f), 0.45f, 0.7f);
        var brass = Mat(new Color(0.72f, 0.55f, 0.22f), 0.6f, 0.9f);
        var mark = Mat(new Color(0.95f, 0.95f, 0.9f), 0.3f);

        // Проушина на стене — остаётся на месте.
        Part(transform, PrimitiveType.Cube, new Vector3(0f, 0f, 0.003f), new Vector3(0.05f, 0.09f, 0.006f), steel);
        Part(transform, PrimitiveType.Cube, new Vector3(0f, 0f, 0.018f), new Vector3(0.012f, 0.03f, 0.024f), steel);

        // Накладка (ушко) — на полотне двери, уходит вместе с ним; прорезь надета на проушину.
        Transform leaf = gate != null && gate.leaves != null && gate.leaves.Length > 0 && gate.leaves[0] != null ? gate.leaves[0] : transform;
        var strap = Node(transform, "HaspStrap", new Vector3(doorDir * 0.08f, 0f, 0.008f));
        Part(strap, PrimitiveType.Cube, Vector3.zero, new Vector3(0.2f, 0.045f, 0.005f), dark);
        foreach (float y in new[] { -0.016f, 0.016f })
            Part(strap, PrimitiveType.Cube, new Vector3(doorDir * -0.075f, y, 0f), new Vector3(0.04f, 0.012f, 0.006f), dark);
        for (int i = 0; i < 3; i++)
            Part(strap, PrimitiveType.Cylinder, new Vector3(doorDir * (0.02f + i * 0.04f), 0f, 0.004f), new Vector3(0.01f, 0.002f, 0.01f), steel, new Vector3(90f, 0f, 0f));
        if (leaf != transform) strap.SetParent(leaf, true);

        // Навесной замок: дужка продета в проушину, корпус висит ниже, спереди четыре колёсика с цифрами.
        padlock = Node(transform, "Padlock", new Vector3(0f, -0.005f, 0.026f));
        shackle = Node(padlock, "Shackle", Vector3.zero);
        foreach (float x in new[] { -0.02f, 0.02f })
            Part(shackle, PrimitiveType.Cylinder, new Vector3(x, -0.025f, 0f), new Vector3(0.008f, 0.03f, 0.008f), steel);
        Part(shackle, PrimitiveType.Cube, new Vector3(0f, 0.005f, 0f), new Vector3(0.048f, 0.008f, 0.008f), steel);
        Part(padlock, PrimitiveType.Cube, new Vector3(0f, -0.085f, 0f), new Vector3(0.07f, 0.06f, 0.028f), brass, default, true);
        Part(padlock, PrimitiveType.Cube, new Vector3(0f, -0.085f, 0.0145f), new Vector3(0.062f, 0.024f, 0.002f), dark);
        wheels = new Transform[digits.Length];
        for (int i = 0; i < wheels.Length; i++)
        {
            float x = (i - (wheels.Length - 1) / 2f) * 0.0145f;
            wheels[i] = Node(padlock, "Wheel" + i, new Vector3(x, -0.085f, 0.014f));
            Part(wheels[i], PrimitiveType.Cylinder, Vector3.zero, new Vector3(0.016f, 0.006f, 0.016f), dark, new Vector3(0f, 0f, 90f));
            Part(wheels[i], PrimitiveType.Cube, new Vector3(0f, 0f, 0.0075f), new Vector3(0.0125f, 0.003f, 0.002f), mark); // риска текущей цифры
        }
    }

    void Start()
    {
        if (gate == null || Unlocked) return;
        gate.Locked = true;
        gate.lockedMessage = "Заперто на кодовый замок.";
        gate.lockedSpeaker = "STUDENT";
    }

    public override void Interact(GameObject player)
    {
        if (!Available) return;
        Active = this;
        sel = 0;
        openFrame = Time.frameCount;
        health = player.GetComponent<PlayerHealth>();
        lastHealth = health != null ? health.Health : 0f;
        FirstPersonController.InputLocked = true;
    }

    void Exit()
    {
        if (Active == this) Active = null;
        FirstPersonController.InputLocked = false;
    }

    protected override void OnDisable()
    {
        base.OnDisable();
        if (Active == this) Exit();
    }

    void Update()
    {
        // Колёсики доворачиваются до набранных цифр, при ошибке корпус дёргается.
        if (wheels != null)
            for (int i = 0; i < wheels.Length; i++)
            {
                if (wheels[i] == null) continue;
                var target = Quaternion.Euler(-digits[i] * 36f, 0f, 0f);
                wheels[i].localRotation = Quaternion.RotateTowards(wheels[i].localRotation, target, Time.deltaTime * 900f);
            }
        if (padlock != null && !Unlocked)
        {
            shake = Mathf.MoveTowards(shake, 0f, Time.deltaTime * 3f);
            padlock.localRotation = Quaternion.Euler(0f, 0f, Mathf.Sin(Time.time * 60f) * 12f * shake);
        }

        if (Active != this || PauseMenu.IsPaused) return;
        bool hurt = health != null && (health.Health < lastHealth - 0.01f || health.IsDead);
        if (health != null) lastHealth = health.Health;
        if (hurt) { Exit(); return; }

        var kb = Keyboard.current;
        int typed = -1;
        if (kb != null)
            for (int d = 0; d <= 9; d++)
            {
                Key row = d == 0 ? Key.Digit0 : Key.Digit1 + (d - 1); // в Key цифры идут 1…9, 0
                if (kb[row].wasPressedThisFrame || kb[Key.Numpad0 + d].wasPressedThisFrame) { typed = d; break; }
            }
        if (typed >= 0)
        {
            digits[sel] = typed;
            Click();
            if (sel == digits.Length - 1) Check();
            else sel++;
            return;
        }
        bool left = Controls.WasPressed(GameAction.MoveLeft) || (kb != null && kb.leftArrowKey.wasPressedThisFrame);
        bool right = Controls.WasPressed(GameAction.MoveRight) || (kb != null && kb.rightArrowKey.wasPressedThisFrame);
        bool up = Controls.WasPressed(GameAction.MoveForward) || (kb != null && kb.upArrowKey.wasPressedThisFrame);
        bool down = Controls.WasPressed(GameAction.MoveBack) || (kb != null && kb.downArrowKey.wasPressedThisFrame);
        if (left || (kb != null && kb.backspaceKey.wasPressedThisFrame)) sel = Mathf.Max(0, sel - 1);
        if (right) sel = Mathf.Min(digits.Length - 1, sel + 1);
        if (up) { digits[sel] = (digits[sel] + 1) % 10; Click(); }
        if (down) { digits[sel] = (digits[sel] + 9) % 10; Click(); }
        bool enter = kb != null && (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame);
        if (enter || (Time.frameCount > openFrame && Controls.WasPressed(GameAction.Interact))) Check();
    }

    void Click()
    {
        audioSrc.pitch = Random.Range(1.6f, 1.9f);
        audioSrc.PlayOneShot(ServerLockdown.Clunk, GameSettings.EffectsVolume * 0.35f);
    }

    void Check()
    {
        string entered = "";
        foreach (int d in digits) entered += d;
        flashUntil = Time.time + 0.6f;
        flashOk = entered == code;
        audioSrc.pitch = 1f;
        if (!flashOk)
        {
            audioSrc.PlayOneShot(ServerLockdown.Fail, GameSettings.EffectsVolume * 0.7f);
            shake = 1f;
            sel = 0;
            return;
        }
        audioSrc.PlayOneShot(ServerLockdown.Ok, GameSettings.EffectsVolume * 0.8f);
        Exit();
        Unlock();
    }

    void Unlock()
    {
        Unlocked = true;
        // Дужка отскакивает, замок падает на пол.
        shackle.localPosition += Vector3.up * 0.018f;
        padlock.SetParent(null, true);
        var rb = padlock.gameObject.AddComponent<Rigidbody>();
        rb.mass = 0.4f;
        rb.linearVelocity = transform.forward * 0.4f + Vector3.up * 0.3f;
        rb.angularVelocity = Random.insideUnitSphere * 4f;
        var cc = FindAnyObjectByType<CharacterController>();
        if (cc != null) foreach (var c in padlock.GetComponentsInChildren<Collider>()) Physics.IgnoreCollision(c, cc);
        Destroy(padlock.gameObject, 30f);
        if (gate != null)
        {
            gate.Locked = false;
            var cam = Camera.main;
            if (!gate.IsOpen) gate.Toggle(cam != null ? cam.transform.position : transform.position + transform.forward);
        }
    }

    void OnGUI()
    {
        if (Active != this || PauseMenu.IsPaused) return;
        GUI.depth = -50;
        float H = Screen.height, W = Screen.width, s = H / 1080f;
        var panel = new Rect(W / 2f - 330f * s, H * 0.6f, 660f * s, 250f * s);
        GUI.color = new Color(0.04f, 0.04f, 0.05f, 0.9f);
        GUI.DrawTexture(panel, Texture2D.whiteTexture);
        var st = new GUIStyle(GUI.skin.label) { fontSize = Mathf.RoundToInt(26 * s), alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
        GUI.color = new Color(0.95f, 0.85f, 0.55f);
        GUI.Label(new Rect(panel.x, panel.y + 8f * s, panel.width, 40f * s), "КОДОВЫЙ ЗАМОК", st);

        var big = new GUIStyle(st) { fontSize = Mathf.RoundToInt(64 * s) };
        float cell = 84f * s, gap = 18f * s, total = digits.Length * cell + (digits.Length - 1) * gap;
        for (int i = 0; i < digits.Length; i++)
        {
            var r = new Rect(panel.x + (panel.width - total) / 2f + i * (cell + gap), panel.y + 58f * s, cell, 110f * s);
            bool flash = Time.time < flashUntil;
            GUI.color = flash ? (flashOk ? new Color(0.2f, 0.75f, 0.3f) : new Color(0.75f, 0.15f, 0.12f))
                      : i == sel ? new Color(0.75f, 0.6f, 0.25f) : new Color(0.25f, 0.25f, 0.27f);
            GUI.DrawTexture(r, Texture2D.whiteTexture);
            GUI.color = new Color(0.08f, 0.08f, 0.09f);
            GUI.DrawTexture(new Rect(r.x + 4f * s, r.y + 4f * s, r.width - 8f * s, r.height - 8f * s), Texture2D.whiteTexture);
            GUI.color = Color.white;
            GUI.Label(r, digits[i].ToString(), big);
        }
        if (Time.time < flashUntil && !flashOk)
        {
            var small = new GUIStyle(st) { fontSize = Mathf.RoundToInt(18 * s), fontStyle = FontStyle.Normal };
            GUI.color = new Color(1f, 0.45f, 0.4f);
            GUI.Label(new Rect(panel.x, panel.y + 185f * s, panel.width, 50f * s), "Неверный код", small);
        }
        GUI.color = Color.white;
    }
}
