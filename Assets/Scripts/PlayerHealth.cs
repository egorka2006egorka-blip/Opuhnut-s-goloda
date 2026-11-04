using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

/// <summary>
/// Здоровье игрока: полоска внизу справа, красная вспышка по краям экрана при ударе,
/// медленное восстановление, если какое-то время не получать урон.
/// После смерти управление отключается; R или клик — начать заново (перезагрузка сцены).
/// Добавляется к игроку автоматически (см. FirstPersonController.Awake).
/// </summary>
public class PlayerHealth : MonoBehaviour
{
    public float maxHealth = 100f;
    [Tooltip("Через сколько секунд без урона начинается восстановление.")]
    public float regenDelay = 6f;
    [Tooltip("Восстановление здоровья в секунду.")]
    public float regenRate = 4f;

    public float Health { get; private set; }
    public bool IsDead { get; private set; }

    float lastHitTime = -100f;
    float hitFlash;      // 0..1 — красная вспышка от удара
    float shownHealth;   // плавно догоняет Health — «след» потерянного здоровья на полоске
    float deathClock;
    static Texture2D vignette;

    void Awake()
    {
        Health = maxHealth;
        shownHealth = maxHealth;
    }

    /// <summary>Урон игроку. from — откуда ударили (для тряски камеры).</summary>
    public void TakeDamage(float amount, Vector3 from)
    {
        if (IsDead || amount <= 0f) return;
        Health = Mathf.Max(0f, Health - amount);
        lastHitTime = Time.time;
        hitFlash = 1f;
        var fpc = GetComponent<FirstPersonController>();
        if (fpc != null)
        {
            fpc.AddShake(0.6f);
            // Голову слегка отбрасывает в сторону от удара.
            Vector3 local = transform.InverseTransformDirection(transform.position - from);
            fpc.AddRecoil(1.5f, Mathf.Clamp(local.x, -1f, 1f) * 2f);
        }
        if (Health <= 0f) Die();
    }

    public void Heal(float amount)
    {
        if (IsDead) return;
        Health = Mathf.Min(maxHealth, Health + amount);
    }

    void Die()
    {
        IsDead = true;
        deathClock = 0f;
        var fpc = GetComponent<FirstPersonController>();
        if (fpc != null) fpc.enabled = false;
        var weapons = GetComponent<PlayerWeaponHandler>();
        if (weapons != null) weapons.enabled = false;
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    void Update()
    {
        float dt = Time.deltaTime;
        hitFlash = Mathf.MoveTowards(hitFlash, 0f, dt * 1.5f);
        shownHealth = shownHealth > Health ? Mathf.MoveTowards(shownHealth, Health, dt * maxHealth * 0.5f) : Health;

        if (IsDead)
        {
            deathClock += dt;
            var kb = Keyboard.current;
            var mouse = Mouse.current;
            if (deathClock > 1f && ((kb != null && kb.rKey.wasPressedThisFrame) || (mouse != null && mouse.leftButton.wasPressedThisFrame)))
                Restart();
            return;
        }

        if (Health < maxHealth && Time.time - lastHitTime > regenDelay)
            Health = Mathf.Min(maxHealth, Health + regenRate * dt);
    }

    static void Restart()
    {
        var scene = SceneManager.GetActiveScene();
#if UNITY_EDITOR
        // В редакторе сцена может быть не добавлена в Build Settings — грузим по пути.
        UnityEditor.SceneManagement.EditorSceneManager.LoadSceneInPlayMode(scene.path, new LoadSceneParameters(LoadSceneMode.Single));
#else
        SceneManager.LoadScene(scene.buildIndex);
#endif
    }

    // ---------- Интерфейс ----------

    void OnGUI()
    {
        float frac = Mathf.Clamp01(Health / maxHealth);

        // Красные края экрана: вспышка при ударе и постоянное «кровавое» обрамление при низком здоровье.
        float low = Mathf.Clamp01((0.35f - frac) / 0.35f);
        float pulse = low > 0f ? 0.75f + 0.25f * Mathf.Sin(Time.time * 6f) : 0f;
        float vig = Mathf.Max(hitFlash * 0.8f, low * 0.6f * pulse);
        if (IsDead) vig = Mathf.Max(vig, 0.75f);
        if (vig > 0.01f)
        {
            GUI.color = new Color(0.7f, 0f, 0f, vig);
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Vignette());
        }

        // Полоска здоровья — снизу справа.
        float w = Mathf.Clamp(Screen.width * 0.22f, 200f, 420f);
        float h = Mathf.Max(16f, Screen.height / 45f);
        float x = Screen.width - w - 24f, y = Screen.height - h - 28f;

        var label = new GUIStyle(GUI.skin.label)
        {
            fontSize = Mathf.Max(14, Screen.height / 50),
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.LowerLeft
        };
        string text = "ЗДОРОВЬЕ  " + Mathf.CeilToInt(Health);
        var textRect = new Rect(x, y - label.fontSize - 10f, w, label.fontSize + 8f);
        GUI.color = new Color(0, 0, 0, 0.8f);
        GUI.Label(new Rect(textRect.x + 2, textRect.y + 2, textRect.width, textRect.height), text, label);
        GUI.color = Color.white;
        GUI.Label(textRect, text, label);

        // Рамка и фон.
        GUI.color = new Color(0f, 0f, 0f, 0.75f);
        GUI.DrawTexture(new Rect(x - 3, y - 3, w + 6, h + 6), Texture2D.whiteTexture);
        GUI.color = new Color(0.25f, 0.05f, 0.05f, 0.9f);
        GUI.DrawTexture(new Rect(x, y, w, h), Texture2D.whiteTexture);
        // След потерянного здоровья.
        GUI.color = new Color(1f, 0.85f, 0.4f, 0.9f);
        GUI.DrawTexture(new Rect(x, y, w * Mathf.Clamp01(shownHealth / maxHealth), h), Texture2D.whiteTexture);
        // Текущее здоровье: зелёный → жёлтый → красный.
        Color barColor = frac > 0.5f
            ? Color.Lerp(new Color(0.95f, 0.8f, 0.15f), new Color(0.25f, 0.85f, 0.3f), (frac - 0.5f) * 2f)
            : Color.Lerp(new Color(0.9f, 0.15f, 0.1f), new Color(0.95f, 0.8f, 0.15f), frac * 2f);
        GUI.color = barColor;
        GUI.DrawTexture(new Rect(x, y, w * frac, h), Texture2D.whiteTexture);
        // Блик сверху.
        GUI.color = new Color(1f, 1f, 1f, 0.18f);
        GUI.DrawTexture(new Rect(x, y, w * frac, h * 0.35f), Texture2D.whiteTexture);

        if (IsDead)
        {
            var big = new GUIStyle(GUI.skin.label)
            {
                fontSize = Mathf.Max(36, Screen.height / 10),
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter
            };
            var small = new GUIStyle(big) { fontSize = Mathf.Max(16, Screen.height / 36), fontStyle = FontStyle.Normal };
            float a = Mathf.Clamp01(deathClock / 1f);
            var r = new Rect(0, Screen.height * 0.35f, Screen.width, Screen.height * 0.15f);
            GUI.color = new Color(0, 0, 0, 0.85f * a);
            GUI.Label(new Rect(r.x + 3, r.y + 3, r.width, r.height), "ВЫ ПОГИБЛИ", big);
            GUI.color = new Color(1f, 0.2f, 0.15f, a);
            GUI.Label(r, "ВЫ ПОГИБЛИ", big);
            if (deathClock > 1f)
            {
                GUI.color = Color.white;
                GUI.Label(new Rect(0, Screen.height * 0.52f, Screen.width, 40), "R или клик — начать заново", small);
            }
        }
        GUI.color = Color.white;
    }

    /// <summary>Текстура: прозрачный центр, плотные края.</summary>
    static Texture2D Vignette()
    {
        if (vignette != null) return vignette;
        const int n = 64;
        vignette = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        var px = new Color[n * n];
        for (int j = 0; j < n; j++)
            for (int i = 0; i < n; i++)
            {
                float dx = (i + 0.5f) / n * 2f - 1f, dy = (j + 0.5f) / n * 2f - 1f;
                float d = Mathf.Sqrt(dx * dx + dy * dy) / 1.414f;
                float a = Mathf.Clamp01((d - 0.45f) / 0.55f);
                px[j * n + i] = new Color(1f, 1f, 1f, a * a);
            }
        vignette.SetPixels(px);
        vignette.Apply();
        return vignette;
    }
}
