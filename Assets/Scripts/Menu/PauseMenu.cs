using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

/// <summary>
/// Меню паузы в игре (Esc): «Продолжить», «Сохранить игру» (таблица из 6 слотов), «Загрузить игру»,
/// «Настройки», «Главное меню», «Выход». Быстрое сохранение (по умолчанию F5) — в последний использованный слот.
/// Добавляется автоматически в любую сцену с игроком (FirstPersonController)
/// и применяет сохранение, выбранное в меню загрузки.
/// </summary>
public class PauseMenu : MonoBehaviour
{
    public const string MainMenuScene = "MainMenu";

    public static bool IsPaused { get; private set; }

    enum Page { Main, Settings, Save, Load, ConfirmExit }
    Page page;
    readonly SaveSlotsScreen slots = new SaveSlotsScreen();
    Texture2D snapshot; // кадр игры в момент открытия паузы — он попадёт в сохранение
    bool busy;          // идёт захват кадра
    string toast;
    float toastUntil;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Init()
    {
        IsPaused = false;
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
        Setup();
    }

    static void OnSceneLoaded(Scene scene, LoadSceneMode mode) => Setup();

    /// <summary>Вызывается после Awake объектов сцены, но до их Start — игрок ставится на место до спавна зомби.</summary>
    static void Setup()
    {
        SetPaused(false);
        SaveSystem.HideHud = false;
        if (FindAnyObjectByType<PauseMenu>() != null || FindAnyObjectByType<FirstPersonController>() == null)
        {
            SaveSystem.Pending = null;
            return;
        }
        new GameObject("PauseMenu").AddComponent<PauseMenu>();
        if (SaveSystem.Pending != null)
        {
            SaveSystem.Apply(SaveSystem.Pending);
            SaveSystem.Pending = null;
        }
    }

    static void SetPaused(bool paused)
    {
        IsPaused = paused;
        Time.timeScale = paused ? 0f : 1f;
        AudioListener.pause = paused;
        if (paused) FirstPersonController.LockCursor(false);
    }

    void OnDestroy()
    {
        if (IsPaused) SetPaused(false);
        SaveSystem.HideHud = false;
        DropSnapshot();
        slots.Dispose();
    }

    void DropSnapshot()
    {
        if (snapshot != null) Destroy(snapshot);
        snapshot = null;
    }

    void Update()
    {
        bool escUsed = MenuUI.Tick();
        var kb = Keyboard.current;
        if (busy) return;
        if (kb != null && kb.escapeKey.wasPressedThisFrame && !escUsed && !MenuUI.IsRebinding)
        {
            if (!IsPaused) StartCoroutine(CaptureThen(() => { page = Page.Main; SetPaused(true); }));
            else if (slots.HandleEscape()) { }
            else if (page != Page.Main) { GameSettings.Save(); page = Page.Main; }
            else Resume();
        }
        if (!IsPaused && Controls.WasPressed(GameAction.QuickSave))
            StartCoroutine(CaptureThen(() =>
            {
                int slot = SaveSystem.LastSlot;
                ShowToast(SaveSystem.Save(slot, snapshot) ? "Быстрое сохранение — слот " + (slot + 1) : "Сейчас сохранить нельзя");
                DropSnapshot();
            }));
    }

    /// <summary>Снимок кадра без интерфейса игры, затем действие.</summary>
    IEnumerator CaptureThen(System.Action then)
    {
        busy = true;
        SaveSystem.HideHud = true;
        yield return new WaitForEndOfFrame();
        DropSnapshot();
        snapshot = SaveSystem.CaptureThumbnail();
        SaveSystem.HideHud = false;
        busy = false;
        then();
    }

    void Resume()
    {
        SetPaused(false);
        DropSnapshot();
        if (!PlayerDead()) FirstPersonController.LockCursor(true);
    }

    static bool PlayerDead()
    {
        var h = FindAnyObjectByType<PlayerHealth>();
        return h != null && h.IsDead;
    }

    void ShowToast(string text)
    {
        toast = text;
        toastUntil = Time.unscaledTime + 2.5f;
    }

    void OnGUI()
    {
        GUI.depth = -100; // поверх интерфейса игры

        if (IsPaused)
        {
            MenuUI.Dim(0.7f);
            float cx = Screen.width / 2f;
            string title = page == Page.Save ? "СОХРАНЕНИЕ" : page == Page.Load ? "ЗАГРУЗКА" : page == Page.Settings ? "НАСТРОЙКИ" : "ПАУЗА";
            MenuUI.Title(new Rect(MenuUI.S(40), MenuUI.S(70), Screen.width - MenuUI.S(80), MenuUI.S(140)), title);
            switch (page)
            {
                case Page.Main: DrawMain(cx, Screen.height * 0.33f); break;
                case Page.Settings:
                    if (MenuUI.SettingsScreen(cx)) page = Page.Main;
                    break;
                case Page.Save: DrawSlots(cx, true); break;
                case Page.Load: DrawSlots(cx, false); break;
                case Page.ConfirmExit: DrawConfirm(cx, Screen.height * 0.4f); break;
            }
        }

        if (toast != null && Time.unscaledTime < toastUntil && !SaveSystem.HideHud)
        {
            float a = Mathf.Clamp01((toastUntil - Time.unscaledTime) / 0.5f);
            var style = new GUIStyle(MenuUI.Label) { alignment = TextAnchor.MiddleRight, fontStyle = FontStyle.Bold };
            GUI.color = new Color(1f, 1f, 1f, a);
            MenuUI.ShadowLabel(new Rect(0, MenuUI.S(24), Screen.width - MenuUI.S(32), MenuUI.S(40)), toast, style);
            GUI.color = Color.white;
        }
    }

    void DrawMain(float cx, float top)
    {
        int i = 0;
        bool dead = PlayerDead();
        if (MenuUI.Button(MenuUI.ButtonRect(cx, top, i++), "Продолжить")) Resume();
        if (MenuUI.Button(MenuUI.ButtonRect(cx, top, i++), "Сохранить игру", !dead)) { slots.Refresh(); page = Page.Save; }
        if (MenuUI.Button(MenuUI.ButtonRect(cx, top, i++), "Загрузить игру")) { slots.Refresh(); page = Page.Load; }
        if (MenuUI.Button(MenuUI.ButtonRect(cx, top, i++), "Настройки")) page = Page.Settings;
        if (MenuUI.Button(MenuUI.ButtonRect(cx, top, i++), "Главное меню")) page = Page.ConfirmExit;
        if (MenuUI.Button(MenuUI.ButtonRect(cx, top, i++), "Выход из игры")) MainMenu.Quit();
    }

    void DrawSlots(float cx, bool saveMode)
    {
        int slot = slots.Draw(cx, saveMode, out bool back);
        if (back) { page = Page.Main; return; }
        if (slot < 0) return;
        if (saveMode)
        {
            if (SaveSystem.Save(slot, snapshot))
            {
                slots.RefreshSlot(slot);
                ShowToast("Игра сохранена в слот " + (slot + 1));
            }
            else ShowToast("Сейчас сохранить нельзя");
        }
        else if (SaveSystem.BeginLoad(slot, slots.Data(slot), out string scene))
        {
            SetPaused(false);
            SceneManager.LoadScene(scene);
        }
        else ShowToast("Не удалось загрузить слот " + (slot + 1));
    }

    void DrawConfirm(float cx, float top)
    {
        MenuUI.ShadowLabel(new Rect(cx - MenuUI.S(450), top - MenuUI.S(20), MenuUI.S(900), MenuUI.S(80)),
            "Выйти в главное меню? Несохранённый прогресс будет потерян.", MenuUI.LabelCenter);
        float t = top + MenuUI.S(80);
        if (MenuUI.Button(MenuUI.ButtonRect(cx, t, 0), "Выйти")) GoToMainMenu();
        if (MenuUI.Button(MenuUI.ButtonRect(cx, t, 1), "Отмена")) page = Page.Main;
    }

    static void GoToMainMenu()
    {
        SetPaused(false);
        if (Application.CanStreamedLevelBeLoaded(MainMenuScene)) SceneManager.LoadScene(MainMenuScene);
        else Debug.LogWarning("PauseMenu: сцена MainMenu не добавлена в Build Settings.");
    }
}
