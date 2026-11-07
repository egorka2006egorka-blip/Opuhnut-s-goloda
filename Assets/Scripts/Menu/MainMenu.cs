using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

/// <summary>
/// Главное меню (сцена MainMenu — первая в Build Settings): название игры и пункты
/// «Продолжить» (последнее сохранение), «Новая игра», «Загрузить игру», «Настройки», «Выход».
/// </summary>
public class MainMenu : MonoBehaviour
{
    [Tooltip("Сцена, с которой начинается новая игра.")]
    public string gameScene = "Canteen";

    /// <summary>Отступ названия и кнопок от левого края (при 1080p).</summary>
    const float LeftMargin = 70f;

    enum Page { Main, Settings, Load }
    Page page = Page.Main;
    readonly SaveSlotsScreen slots = new SaveSlotsScreen();
    bool loading;
    bool has3DBackground; // в сцене есть фон (тёмная столовая) — рисуем только затемнение
    float loadProgress;
    float fadeIn;

    void Start()
    {
        Time.timeScale = 1f;
        AudioListener.pause = false;
        SaveSystem.HideHud = false;
        FirstPersonController.LockCursor(false);
        has3DBackground = FindAnyObjectByType<MenuFlickerLight>() != null;
        RefreshSaves();
    }

    void OnDestroy() => slots.Dispose();

    void RefreshSaves() => slots.Refresh();

    void Update()
    {
        fadeIn = Mathf.MoveTowards(fadeIn, 1f, Time.unscaledDeltaTime * 1.5f);
        bool escUsed = MenuUI.Tick();
        var kb = Keyboard.current;
        if (!escUsed && !MenuUI.IsRebinding && kb != null && kb.escapeKey.wasPressedThisFrame && page != Page.Main
            && !slots.HandleEscape())
        {
            GameSettings.Save();
            page = Page.Main;
        }
    }

    void OnGUI()
    {
        if (has3DBackground) MenuUI.BackgroundOver3D(page == Page.Main || loading);
        else MenuUI.Background();
        float cx = Screen.width / 2f;
        float pulse = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 1.3f);

        // Название игры — сверху.
        GUI.color = new Color(1f, 1f, 1f, fadeIn);
        MenuUI.Title(new Rect(MenuUI.S(LeftMargin), MenuUI.S(50), Screen.width - MenuUI.S(LeftMargin * 2f), MenuUI.S(200)), MenuUI.GameTitle, pulse * 0.25f, true);
        GUI.color = Color.white;

        if (loading)
        {
            var big = new GUIStyle(MenuUI.LabelCenter) { fontSize = Mathf.RoundToInt(MenuUI.S(34)) };
            MenuUI.ShadowLabel(new Rect(0, Screen.height * 0.55f, Screen.width, MenuUI.S(60)), "Загрузка… " + Mathf.RoundToInt(loadProgress * 100f) + "%", big);
            return;
        }

        switch (page)
        {
            // Кнопки — слева, под названием игры.
            case Page.Main: DrawMain(MenuUI.S(LeftMargin), MenuUI.S(360)); break;
            case Page.Settings:
                if (MenuUI.SettingsScreen(cx)) page = Page.Main;
                break;
            case Page.Load:
                int slot = slots.Draw(cx, false, out bool back);
                if (slot >= 0) LoadSlot(slot);
                else if (back) page = Page.Main;
                break;
        }

        if (page == Page.Main)
        {
            GUI.color = new Color(1f, 1f, 1f, 0.5f);
            GUI.Label(new Rect(0, Screen.height - MenuUI.S(44), Screen.width, MenuUI.S(30)),
                "В игре: Esc — пауза и сохранение, " + Controls.KeyName(GameAction.QuickSave) + " — быстрое сохранение", MenuUI.Small);
            GUI.color = Color.white;
        }
    }

    void DrawMain(float left, float top)
    {
        int i = 0;
        int latest = slots.Latest;
        if (latest >= 0 && BigButton(left, top, i++, "Продолжить")) LoadSlot(latest);
        if (BigButton(left, top, i++, "Новая игра"))
        {
            SaveSystem.Pending = null;
            StartCoroutine(LoadRoutine(gameScene));
        }
        if (latest >= 0 && BigButton(left, top, i++, "Загрузить игру")) { RefreshSaves(); page = Page.Load; }
        if (BigButton(left, top, i++, "Настройки")) page = Page.Settings;
        if (BigButton(left, top, i++, "Выход")) Quit();

        if (latest >= 0)
        {
            var r = BigRect(left, top, i);
            var style = new GUIStyle(MenuUI.Small) { alignment = TextAnchor.MiddleLeft };
            MenuUI.ShadowLabel(new Rect(r.x + MenuUI.S(4), r.y, Screen.width - r.x, MenuUI.S(36)),
                "Последнее сохранение: слот " + (latest + 1) + ", " + slots.Data(latest).savedAt, style);
        }
    }

    // Кнопки главного меню — крупнее, чем в паузе.
    const float BigW = 540f, BigH = 82f, BigGap = 18f;

    static Rect BigRect(float left, float top, int index) =>
        new Rect(left, top + index * MenuUI.S(BigH + BigGap), MenuUI.S(BigW), MenuUI.S(BigH));

    static readonly Color HoverColor = new Color(0.95f, 0.08f, 0.06f);
    static readonly Color PressedColor = new Color(0.65f, 0.03f, 0.02f);

    /// <summary>
    /// Кнопка-надпись без фона. Цвет при наведении выбираем сами: в IMGUI состояние hover у стиля
    /// без фоновой текстуры не применяется, и текст остался бы белым.
    /// </summary>
    static bool BigButton(float left, float top, int index, string text)
    {
        var r = BigRect(left, top, index);
        var style = MenuUI.ButtonLarge;
        // Ширина зоны наведения — по самому тексту, а не по всей строке.
        float textW = style.CalcSize(new GUIContent(text)).x;
        var hit = new Rect(r.x, r.y, Mathf.Min(r.width, textW + MenuUI.S(16)), r.height);
        bool hover = GUI.enabled && hit.Contains(Event.current.mousePosition);
        var mouse = UnityEngine.InputSystem.Mouse.current;
        bool pressed = hover && mouse != null && mouse.leftButton.isPressed;

        var draw = new GUIStyle(style);
        // Тень под текстом — чтобы надпись читалась поверх фона.
        draw.normal.textColor = new Color(0f, 0f, 0f, 0.85f);
        GUI.Label(new Rect(r.x + MenuUI.S(3), r.y + MenuUI.S(3), r.width, r.height), text, draw);
        draw.normal.textColor = pressed ? PressedColor : hover ? HoverColor : MenuUI.TextColor;
        GUI.Label(r, text, draw);

        return GUI.Button(hit, GUIContent.none, GUIStyle.none);
    }

    void LoadSlot(int slot)
    {
        if (loading) return;
        if (SaveSystem.BeginLoad(slot, slots.Data(slot), out string scene)) StartCoroutine(LoadRoutine(scene));
        else Debug.LogWarning("MainMenu: сцена сохранения «" + scene + "» не найдена в Build Settings.");
    }

    IEnumerator LoadRoutine(string scene)
    {
        if (loading) yield break;
        loading = true;
        yield return MenuMusic.FadeOut(1.2f); // музыка меню плавно затихает
        var op = SceneManager.LoadSceneAsync(scene, LoadSceneMode.Single);
        if (op == null) { loading = false; SaveSystem.Pending = null; yield break; }
        while (!op.isDone)
        {
            loadProgress = Mathf.Clamp01(op.progress / 0.9f);
            yield return null;
        }
    }

    public static void Quit()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }
}
