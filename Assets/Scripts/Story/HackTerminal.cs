using UnityEngine;

/// <summary>
/// Мини-игра 3: терминал замка пультовой. E — сесть за взлом: по шкале бегает бегунок, жми E (ЛКМ, пробел), когда он
/// в зелёной зоне. Нужно rounds попаданий подряд по ступеням: с каждым зона уже, бегунок быстрее; промах — шаг назад.
/// Пока взламываешь, стоишь на месте: удар зомби или шаг (WASD) прерывает взлом (пройденные ступени сохраняются).
/// Начало координат — на полу, +Z — к игроку (экран смотрит туда).
/// </summary>
public class HackTerminal : Interactable
{
    public int rounds = 4;

    public bool Hacked { get; private set; }
    bool hacking;
    int stage;
    float marker, dir = 1f, zoneCenter, flashUntil, flashOk;
    int openFrame;
    float lastHealth;
    PlayerHealth health;
    Renderer screen;
    Material screenRed, screenGreen;
    AudioSource audioSrc;

    public override string Prompt => "Взломать замок пультовой (пройдено " + stage + " из " + rounds + ")";
    public override bool Available => !Hacked && !hacking && ServerLockdown.Instance != null && ServerLockdown.Instance.Active;
    public override Vector3 Focus => screen != null ? screen.transform.position : transform.position + Vector3.up * 1.3f;

    float ZoneWidth => Mathf.Lerp(0.22f, 0.09f, stage / (float)Mathf.Max(1, rounds - 1));
    float Speed => Mathf.Lerp(0.7f, 1.5f, stage / (float)Mathf.Max(1, rounds - 1));

    void Awake()
    {
        range = 2f;
        audioSrc = gameObject.AddComponent<AudioSource>();
        audioSrc.spatialBlend = 0.6f;
        var body = Mat(new Color(0.2f, 0.21f, 0.23f), 0.4f, 0.5f);
        var dark = Mat(new Color(0.05f, 0.05f, 0.06f), 0.5f);
        screenRed = Mat(new Color(0.3f, 0.02f, 0.02f), 0.8f, 0f, new Color(1.6f, 0.1f, 0.08f));
        screenGreen = Mat(new Color(0.02f, 0.3f, 0.06f), 0.8f, 0f, new Color(0.1f, 1.6f, 0.3f));
        Part(transform, PrimitiveType.Cube, new Vector3(0f, 0.5f, 0f), new Vector3(0.45f, 1f, 0.35f), body, default, true);
        var head = Node(transform, "Head", new Vector3(0f, 1.15f, 0.02f));
        head.localRotation = Quaternion.Euler(-20f, 0f, 0f);
        Part(head, PrimitiveType.Cube, Vector3.zero, new Vector3(0.5f, 0.36f, 0.08f), dark, default, true);
        screen = Part(head, PrimitiveType.Cube, new Vector3(0f, 0f, 0.041f), new Vector3(0.44f, 0.3f, 0.004f), screenRed).GetComponent<Renderer>();
        // Клавиатура-полка.
        Part(transform, PrimitiveType.Cube, new Vector3(0f, 0.98f, 0.22f), new Vector3(0.42f, 0.03f, 0.16f), dark, new Vector3(12f, 0f, 0f));
        var glow = new GameObject("ScreenGlow").AddComponent<Light>();
        glow.transform.SetParent(head, false);
        glow.transform.localPosition = new Vector3(0f, 0f, 0.4f);
        glow.type = LightType.Point;
        glow.range = 2f;
        glow.intensity = 1f;
        glow.color = new Color(1f, 0.3f, 0.25f);
        glow.shadows = LightShadows.None;
    }

    public override void Interact(GameObject player)
    {
        if (!Available) return;
        health = player.GetComponent<PlayerHealth>();
        lastHealth = health != null ? health.Health : 0f;
        hacking = true;
        openFrame = Time.frameCount;
        FirstPersonController.InputLocked = true;
        NewZone();
    }

    void NewZone()
    {
        float w = ZoneWidth;
        // Зона — подальше от бегунка, чтобы не попасть случайно.
        for (int i = 0; i < 10; i++)
        {
            zoneCenter = Random.Range(w / 2f + 0.03f, 1f - w / 2f - 0.03f);
            if (Mathf.Abs(zoneCenter - marker) > 0.3f) break;
        }
    }

    void Exit()
    {
        hacking = false;
        FirstPersonController.InputLocked = false;
    }

    void OnDisable() { if (hacking) Exit(); }

    void Update()
    {
        if (!hacking || PauseMenu.IsPaused) return;
        // Ударили или игрок двинулся — бросает взлом.
        bool hurt = health != null && (health.Health < lastHealth - 0.01f || health.IsDead);
        if (health != null) lastHealth = health.Health;
        bool move = Controls.IsPressed(GameAction.MoveForward) || Controls.IsPressed(GameAction.MoveBack) ||
                    Controls.IsPressed(GameAction.MoveLeft) || Controls.IsPressed(GameAction.MoveRight);
        if (hurt || move || ServerLockdown.Instance == null || !ServerLockdown.Instance.Active) { Exit(); return; }

        marker += dir * Speed * Time.deltaTime;
        if (marker > 1f) { marker = 2f - marker; dir = -1f; }
        if (marker < 0f) { marker = -marker; dir = 1f; }

        bool press = Time.frameCount > openFrame && (Controls.WasPressed(GameAction.Interact) || Controls.WasPressed(GameAction.Fire) ||
                     (UnityEngine.InputSystem.Keyboard.current != null && UnityEngine.InputSystem.Keyboard.current.spaceKey.wasPressedThisFrame));
        if (!press) return;
        if (Mathf.Abs(marker - zoneCenter) <= ZoneWidth / 2f)
        {
            stage++;
            flashOk = 1f;
            flashUntil = Time.time + 0.25f;
            audioSrc.PlayOneShot(ServerLockdown.Ok, GameSettings.EffectsVolume * 0.8f);
            if (stage >= rounds) { Done(); return; }
        }
        else
        {
            stage = Mathf.Max(0, stage - 1);
            flashOk = 0f;
            flashUntil = Time.time + 0.35f;
            audioSrc.PlayOneShot(ServerLockdown.Fail, GameSettings.EffectsVolume * 0.8f);
        }
        NewZone();
    }

    void Done()
    {
        Hacked = true;
        Exit();
        screen.sharedMaterial = screenGreen;
        var glow = GetComponentInChildren<Light>();
        if (glow != null) glow.color = new Color(0.3f, 1f, 0.4f);
        if (ServerLockdown.Instance != null) ServerLockdown.Instance.Solve(ServerLockdown.Task.Terminal);
    }

    void OnGUI()
    {
        if (!hacking || PauseMenu.IsPaused) return;
        GUI.depth = -50;
        float H = Screen.height, W = Screen.width, s = H / 1080f;
        var panel = new Rect(W / 2f - 380f * s, H * 0.62f, 760f * s, 210f * s);
        GUI.color = new Color(0f, 0.05f, 0.02f, 0.88f);
        GUI.DrawTexture(panel, Texture2D.whiteTexture);
        var st = new GUIStyle(GUI.skin.label) { fontSize = Mathf.RoundToInt(26 * s), alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold, richText = true };
        GUI.color = new Color(0.4f, 1f, 0.5f);
        GUI.Label(new Rect(panel.x, panel.y + 8f * s, panel.width, 40f * s), "ВЗЛОМ ЗАМКА ПУЛЬТОВОЙ  —  ключ " + stage + " / " + rounds, st);

        // Шкала, зелёная зона и бегунок.
        var bar = new Rect(panel.x + 40f * s, panel.y + 70f * s, panel.width - 80f * s, 46f * s);
        GUI.color = new Color(0.15f, 0.2f, 0.15f, 1f);
        GUI.DrawTexture(bar, Texture2D.whiteTexture);
        float zw = ZoneWidth * bar.width;
        GUI.color = new Color(0.1f, 0.85f, 0.25f, 0.9f);
        GUI.DrawTexture(new Rect(bar.x + zoneCenter * bar.width - zw / 2f, bar.y, zw, bar.height), Texture2D.whiteTexture);
        GUI.color = Color.white;
        GUI.DrawTexture(new Rect(bar.x + marker * bar.width - 3f * s, bar.y - 8f * s, 6f * s, bar.height + 16f * s), Texture2D.whiteTexture);
        if (Time.time < flashUntil)
        {
            GUI.color = flashOk > 0.5f ? new Color(0.3f, 1f, 0.4f, 0.35f) : new Color(1f, 0.15f, 0.1f, 0.45f);
            GUI.DrawTexture(bar, Texture2D.whiteTexture);
        }
        // Ступени ключа.
        for (int i = 0; i < rounds; i++)
        {
            GUI.color = i < stage ? new Color(0.3f, 1f, 0.4f) : new Color(0.2f, 0.3f, 0.2f);
            GUI.DrawTexture(new Rect(panel.x + panel.width / 2f - rounds * 22f * s + i * 44f * s + 6f * s, panel.y + 128f * s, 32f * s, 14f * s), Texture2D.whiteTexture);
        }
        var small = new GUIStyle(st) { fontSize = Mathf.RoundToInt(18 * s), fontStyle = FontStyle.Normal };
        GUI.color = new Color(0.7f, 1f, 0.75f, 0.9f);
        GUI.Label(new Rect(panel.x, panel.y + 155f * s, panel.width, 40f * s),
            "[" + Controls.KeyName(GameAction.Interact) + "] / ЛКМ / Пробел — поймать в зелёной зоне      WASD — отойти (прогресс сохранится)", small);
        GUI.color = Color.white;
    }
}
