using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Экран инвентаря (по умолчанию Tab): разделы «Карта», «Снаряжение», «Важное».
/// Внизу посередине — лицо героя и монитор сердцебиения (здоровье видно только здесь).
/// Пока инвентарь открыт, игра стоит на паузе (время и звук остановлены).
/// Добавляется к игроку автоматически (см. FirstPersonController.Awake).
/// </summary>
public class InventoryScreen : MonoBehaviour
{
    public static bool IsOpen { get; private set; }

    enum Tab { Map, Gear, Important }
    static readonly string[] TabNames = { "Карта", "Снаряжение", "Важное" };
    static Tab tab = Tab.Gear; // запоминается между открытиями

    Inventory inventory;
    PlayerHealth health;
    readonly HealthPanel healthPanel = new HealthPanel();
    readonly ItemPreviewStage stage = new ItemPreviewStage();
    readonly LevelMap map = new LevelMap();
    readonly List<Inventory.Stack> visible = new List<Inventory.Stack>();
    string selected;
    float spin;
    string toast;
    float toastUntil;
    float openTime;
    static Texture2D arrow;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { IsOpen = false; }

    void Awake()
    {
        inventory = GetComponent<Inventory>();
        health = GetComponent<PlayerHealth>();
    }

    void OnDisable()
    {
        if (IsOpen) { IsOpen = false; SetTimePaused(false); }
    }

    /// <summary>Останавливает игровое время и звук (как меню паузы, но без экрана паузы).</summary>
    static void SetTimePaused(bool paused)
    {
        Time.timeScale = paused ? 0f : 1f;
        AudioListener.pause = paused;
    }

    void OnDestroy()
    {
        stage.Dispose();
        map.Dispose();
        healthPanel.Dispose();
        if (arrow != null) { Destroy(arrow); arrow = null; }
        if (circle != null) { Destroy(circle); circle = null; }
    }

    /// <summary>Закрыть инвентарь (Esc в PauseMenu, смерть игрока).</summary>
    public static void Close()
    {
        if (!IsOpen) return;
        IsOpen = false;
        SetTimePaused(false);
        var h = FindAnyObjectByType<PlayerHealth>();
        if (h == null || !h.IsDead) FirstPersonController.LockCursor(true);
    }

    void Open()
    {
        IsOpen = true;
        SetTimePaused(true);
        openTime = Time.unscaledTime;
        spin = -35f;
        FirstPersonController.LockCursor(false);
        if (!map.Ready) map.Capture(transform);
    }

    void Update()
    {
        healthPanel.Tick(Time.unscaledDeltaTime, health); // пульс бежит и на паузе
        if (PauseMenu.IsPaused) return;

        bool dead = health != null && health.IsDead;
        if (IsOpen && dead) Close();
        if (!dead && !MenuUI.IsRebinding && !Shopkeeper.IsShopOpen && !FirstPersonController.InputLocked && Controls.WasPressed(GameAction.Inventory))
        {
            if (IsOpen) Close(); else Open();
        }
        if (!IsOpen) return;

        var kb = Keyboard.current;
        if (kb != null)
        {
            if (kb.digit1Key.wasPressedThisFrame) tab = Tab.Map;
            if (kb.digit2Key.wasPressedThisFrame) tab = Tab.Gear;
            if (kb.digit3Key.wasPressedThisFrame) tab = Tab.Important;
        }
        // Курсор мог захватиться (смена окна и т.п.) — инвентарю он нужен свободным.
        if (Cursor.lockState == CursorLockMode.Locked) FirstPersonController.LockCursor(false);
    }

    // Модели рендерятся вне OnGUI — после всех Update.
    void LateUpdate()
    {
        if (!IsOpen || inventory == null || tab == Tab.Map) return;
        CollectVisible();
        stage.PrepareIcons(visible);
        if (selected != null && inventory.Count(selected) <= 0) selected = null;
        if (selected == null && visible.Count > 0) selected = visible[0].id;
        if (selected != null && !visible.Exists(s => s.id == selected)) selected = visible.Count > 0 ? visible[0].id : null;
        if (selected != null)
        {
            spin += Time.unscaledDeltaTime * 40f;
            stage.RenderDetail(selected, spin);
        }
    }

    void CollectVisible()
    {
        visible.Clear();
        if (inventory == null) return;
        var cat = tab == Tab.Important ? ItemCategory.Important : ItemCategory.Gear;
        foreach (var s in inventory.Items)
        {
            var d = ItemDatabase.Get(s.id);
            if (d != null && d.category == cat) visible.Add(s);
        }
    }

    // ---------- Интерфейс ----------

    void OnGUI()
    {
        if (!IsOpen || SaveSystem.HideHud || PauseMenu.IsPaused) return;
        GUI.depth = -50; // поверх интерфейса игры, под меню паузы
        float fadeIn = Mathf.Clamp01((Time.unscaledTime - openTime) / 0.15f);
        GUI.color = new Color(1f, 1f, 1f, fadeIn);
        MenuUI.Dim(0.72f);

        // Вкладки.
        float tabW = MenuUI.S(280), tabH = MenuUI.S(56), tabGap = MenuUI.S(14);
        float tabsX = Screen.width / 2f - (tabW * 3 + tabGap * 2) / 2f, tabsY = MenuUI.S(36);
        for (int i = 0; i < 3; i++)
        {
            var r = new Rect(tabsX + i * (tabW + tabGap), tabsY, tabW, tabH);
            if (MenuUI.Tab(r, (i + 1) + "  " + TabNames[i], (int)tab == i)) tab = (Tab)i;
        }
        var hint = new GUIStyle(MenuUI.Small) { alignment = TextAnchor.MiddleRight };
        GUI.Label(new Rect(0, tabsY, Screen.width - MenuUI.S(40), tabH),
            "[" + Controls.KeyName(GameAction.Inventory) + "] — закрыть", hint);

        // Область содержимого — между вкладками и блоком здоровья.
        float bottomBlock = MenuUI.S(160) + MenuUI.S(60);
        var content = new Rect(MenuUI.S(100), tabsY + tabH + MenuUI.S(30), Screen.width - MenuUI.S(200),
            Screen.height - (tabsY + tabH + MenuUI.S(30)) - bottomBlock);

        if (tab == Tab.Map) DrawMap(content);
        else DrawItems(content);

        healthPanel.Draw(new Vector2(Screen.width / 2f, Screen.height - MenuUI.S(30)), health);

        if (toast != null && Time.unscaledTime < toastUntil)
        {
            var st = new GUIStyle(MenuUI.LabelCenter) { fontStyle = FontStyle.Bold };
            MenuUI.ShadowLabel(new Rect(0, content.yMax - MenuUI.S(4), Screen.width, MenuUI.S(36)), toast, st);
        }
        GUI.color = Color.white;
    }

    void Panel(Rect r)
    {
        MenuUI.Fill(r, new Color(0f, 0f, 0f, 0.55f));
        MenuUI.Frame(r, Mathf.Max(1f, MenuUI.S(2)), new Color(MenuUI.Accent.r, MenuUI.Accent.g, MenuUI.Accent.b, 0.5f));
    }

    void DrawItems(Rect content)
    {
        CollectVisible();
        float gap = MenuUI.S(24);
        var listRect = new Rect(content.x, content.y, content.width * 0.58f - gap / 2f, content.height);
        var infoRect = new Rect(listRect.xMax + gap, content.y, content.width - listRect.width - gap, content.height);
        Panel(listRect);
        Panel(infoRect);

        if (visible.Count == 0)
        {
            GUI.Label(listRect, tab == Tab.Important ? "Важных предметов пока нет" : "Пусто", MenuUI.LabelCenter);
            return;
        }

        // Сетка ячеек.
        float pad = MenuUI.S(20), slot = MenuUI.S(150), sg = MenuUI.S(14);
        int cols = Mathf.Max(1, Mathf.FloorToInt((listRect.width - pad * 2f + sg) / (slot + sg)));
        var nameStyle = new GUIStyle(MenuUI.Small) { alignment = TextAnchor.LowerCenter, wordWrap = true, fontSize = Mathf.RoundToInt(MenuUI.S(17)) };
        nameStyle.normal.textColor = MenuUI.TextColor;
        var countStyle = new GUIStyle(MenuUI.Label) { alignment = TextAnchor.UpperRight, fontStyle = FontStyle.Bold, fontSize = Mathf.RoundToInt(MenuUI.S(22)) };
        var mouse = Event.current.mousePosition;
        for (int i = 0; i < visible.Count; i++)
        {
            var st = visible[i];
            var def = ItemDatabase.Get(st.id);
            var r = new Rect(listRect.x + pad + (i % cols) * (slot + sg), listRect.y + pad + (i / cols) * (slot + sg), slot, slot);
            if (r.yMax > listRect.yMax) break;
            bool sel = st.id == selected, hover = r.Contains(mouse);
            MenuUI.Fill(r, sel ? new Color(0.25f, 0.06f, 0.05f, 0.9f) : hover ? new Color(0.16f, 0.14f, 0.14f, 0.9f) : new Color(0.08f, 0.08f, 0.09f, 0.9f));
            MenuUI.Frame(r, Mathf.Max(1f, MenuUI.S(sel ? 3 : 1)), sel ? MenuUI.Accent : new Color(1f, 1f, 1f, hover ? 0.4f : 0.15f));
            var icon = stage.Icon(st.id);
            if (icon != null) GUI.DrawTexture(new Rect(r.x + r.width * 0.1f, r.y + r.height * 0.04f, r.width * 0.8f, r.height * 0.8f), icon, ScaleMode.ScaleToFit, true);
            if (def != null) GUI.Label(new Rect(r.x + 4, r.y, r.width - 8, r.height - MenuUI.S(6)), def.name, nameStyle);
            if (st.count > 1 || (def != null && def.unit != ""))
                MenuUI.ShadowLabel(new Rect(r.x, r.y + MenuUI.S(4), r.width - MenuUI.S(8), MenuUI.S(30)), "×" + st.count, countStyle);
            if (GUI.Button(r, GUIContent.none, GUIStyle.none)) { selected = st.id; spin = -35f; }
        }

        // Описание выбранного предмета.
        var sd = ItemDatabase.Get(selected);
        if (sd == null) return;
        float ip = MenuUI.S(24);
        float previewSize = Mathf.Min(infoRect.width - ip * 2f, infoRect.height * 0.5f);
        var pr = new Rect(infoRect.center.x - previewSize / 2f, infoRect.y + ip * 0.5f, previewSize, previewSize);
        if (stage.Detail != null) GUI.DrawTexture(pr, stage.Detail, ScaleMode.ScaleToFit, true);
        float y = pr.yMax + MenuUI.S(6);
        var title = new GUIStyle(MenuUI.Label) { fontStyle = FontStyle.Bold, fontSize = Mathf.RoundToInt(MenuUI.S(32)), wordWrap = true };
        GUI.Label(new Rect(infoRect.x + ip, y, infoRect.width - ip * 2f, MenuUI.S(44)), sd.name, title);
        y += MenuUI.S(46);
        int count = inventory.Count(selected);
        if (sd.unit != "")
        {
            var cs = new GUIStyle(MenuUI.Label);
            cs.normal.textColor = new Color(MenuUI.Accent.r + 0.1f, MenuUI.Accent.g + 0.3f, MenuUI.Accent.b + 0.25f);
            GUI.Label(new Rect(infoRect.x + ip, y, infoRect.width - ip * 2f, MenuUI.S(34)), "Количество: " + count + " " + sd.unit, cs);
            y += MenuUI.S(38);
        }
        var desc = new GUIStyle(MenuUI.Label) { wordWrap = true, alignment = TextAnchor.UpperLeft, fontSize = Mathf.RoundToInt(MenuUI.S(22)) };
        desc.normal.textColor = new Color(MenuUI.TextColor.r, MenuUI.TextColor.g, MenuUI.TextColor.b, 0.8f);
        float buttonH = MenuUI.S(58);
        GUI.Label(new Rect(infoRect.x + ip, y, infoRect.width - ip * 2f, infoRect.yMax - y - buttonH - ip), sd.description, desc);

        if (sd.heal > 0f)
        {
            var br = new Rect(infoRect.x + ip, infoRect.yMax - buttonH - ip * 0.75f, infoRect.width - ip * 2f, buttonH);
            if (MenuUI.Button(br, "Использовать", count > 0, MenuUI.ButtonCenterStyle))
            {
                string res = inventory.Use(selected);
                ShowToast(res ?? sd.name + ": +" + Mathf.RoundToInt(sd.heal) + " к здоровью");
            }
        }
    }

    // Карта: масштаб (1 — видна целиком) и центр видимой области в координатах карты 0..1. Запоминаются между открытиями.
    const float MapMaxZoom = 6f;
    float mapZoom = 1f;
    Vector2 mapCenter = new Vector2(0.5f, 0.5f);
    bool mapDragging;

    void DrawMap(Rect content)
    {
        Panel(content);
        if (!map.Ready)
        {
            GUI.Label(content, "Карта недоступна", MenuUI.LabelCenter);
            return;
        }
        float pad = MenuUI.S(16);
        var area = new Rect(content.x + pad, content.y + pad, content.width - pad * 2f, content.height - pad * 2f - MenuUI.S(30));
        // Размер карты целиком (масштаб 1) — вписана в область.
        float aspect = (float)map.Texture.width / map.Texture.height;
        float fitW = area.width, fitH = fitW / aspect;
        if (fitH > area.height) { fitH = area.height; fitW = fitH * aspect; }

        // Кнопки масштаба — в правом верхнем углу.
        float bs = MenuUI.S(48), bg = MenuUI.S(8);
        var plusR = new Rect(area.xMax - bs - MenuUI.S(10), area.y + MenuUI.S(10), bs, bs);
        var minusR = new Rect(plusR.x, plusR.yMax + bg, bs, bs);
        var meR = new Rect(plusR.x - MenuUI.S(150) - bg, plusR.y, MenuUI.S(150), bs);
        Vector2 playerUV = map.ToUV(transform.position);

        // Мышь: колесо — масштаб относительно курсора, перетаскивание — сдвиг.
        var e = Event.current;
        bool overButtons = plusR.Contains(e.mousePosition) || minusR.Contains(e.mousePosition) || meR.Contains(e.mousePosition);
        if (e.type == EventType.ScrollWheel && area.Contains(e.mousePosition))
        {
            ZoomMap(Mathf.Pow(1.18f, -e.delta.y / 3f), e.mousePosition, area, fitW, fitH);
            e.Use();
        }
        else if (e.type == EventType.MouseDown && area.Contains(e.mousePosition) && !overButtons)
        {
            mapDragging = true;
            e.Use();
        }
        else if (e.type == EventType.MouseDrag && mapDragging)
        {
            mapCenter.x -= e.delta.x / (fitW * mapZoom);
            mapCenter.y += e.delta.y / (fitH * mapZoom);
            e.Use();
        }
        else if (e.rawType == EventType.MouseUp) mapDragging = false;
        ClampMapCenter(area, fitW, fitH);

        // Карта, обрезанная по области просмотра.
        float w = fitW * mapZoom, h = fitH * mapZoom;
        GUI.BeginGroup(area);
        var local = new Vector2(area.width / 2f, area.height / 2f);
        var r = new Rect(local.x - mapCenter.x * w, local.y - (1f - mapCenter.y) * h, w, h);
        GUI.DrawTexture(r, map.Texture);
        MenuUI.Frame(r, Mathf.Max(2f, MenuUI.S(3)), new Color(0.25f, 0.2f, 0.15f, 1f));

        // Игрок — красная стрелка по направлению взгляда, с пульсирующим кругом.
        var p = new Vector2(r.x + playerUV.x * r.width, r.yMax - playerUV.y * r.height);
        float pulse = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 4f);
        float ring = MenuUI.S(26) + MenuUI.S(10) * pulse;
        GUI.color = new Color(MenuUI.Accent.r, MenuUI.Accent.g, MenuUI.Accent.b, 0.35f * (1f - pulse * 0.5f));
        GUI.DrawTexture(new Rect(p.x - ring / 2f, p.y - ring / 2f, ring, ring), Arrow(true));
        GUI.color = Color.white;
        float a = MenuUI.S(30);
        var m = GUI.matrix;
        GUIUtility.RotateAroundPivot(transform.eulerAngles.y, p);
        GUI.color = new Color(0.85f, 0.1f, 0.08f);
        GUI.DrawTexture(new Rect(p.x - a / 2f, p.y - a / 2f, a, a), Arrow(false));
        GUI.matrix = m;
        GUI.color = Color.white;
        GUI.EndGroup();

        // Кнопки.
        var btn = MenuUI.ButtonCenterStyle;
        if (MenuUI.Button(plusR, "+", mapZoom < MapMaxZoom - 0.01f, btn)) ZoomMap(1.4f, area.center, area, fitW, fitH);
        if (MenuUI.Button(minusR, "-", mapZoom > 1.01f, btn)) ZoomMap(1f / 1.4f, area.center, area, fitW, fitH);
        if (MenuUI.Button(meR, "К себе", true, btn))
        {
            if (mapZoom < 2f) mapZoom = 2.5f;
            mapCenter = playerUV;
            ClampMapCenter(area, fitW, fitH);
        }

        var cap = new GUIStyle(MenuUI.Small) { alignment = TextAnchor.MiddleLeft };
        GUI.Label(new Rect(content.x + pad, content.yMax - MenuUI.S(36), content.width - pad * 2f, MenuUI.S(30)),
            "Красная стрелка — вы.   Колесо мыши — масштаб, зажать кнопку мыши и тянуть — сдвинуть карту.", cap);
    }

    /// <summary>Меняет масштаб в factor раз так, чтобы точка карты под pivot (экранные координаты) осталась на месте.</summary>
    void ZoomMap(float factor, Vector2 pivot, Rect area, float fitW, float fitH)
    {
        float newZoom = Mathf.Clamp(mapZoom * factor, 1f, MapMaxZoom);
        Vector2 off = pivot - area.center;
        Vector2 uvAtPivot = mapCenter + new Vector2(off.x / (fitW * mapZoom), -off.y / (fitH * mapZoom));
        mapZoom = newZoom;
        mapCenter = uvAtPivot - new Vector2(off.x / (fitW * mapZoom), -off.y / (fitH * mapZoom));
        ClampMapCenter(area, fitW, fitH);
    }

    /// <summary>Карта не уезжает за края: если она меньше области — по центру, иначе край карты не дальше края области.</summary>
    void ClampMapCenter(Rect area, float fitW, float fitH)
    {
        float w = fitW * mapZoom, h = fitH * mapZoom;
        float hx = area.width / 2f / w, hy = area.height / 2f / h; // половина видимой области в долях карты
        mapCenter.x = w <= area.width ? 0.5f : Mathf.Clamp(mapCenter.x, hx, 1f - hx);
        mapCenter.y = h <= area.height ? 0.5f : Mathf.Clamp(mapCenter.y, hy, 1f - hy);
    }

    static Texture2D circle;

    /// <summary>Стрелка остриём вверх (с тёмной обводкой) или мягкий круг — текстуры для карты.</summary>
    static Texture2D Arrow(bool round)
    {
        if (arrow == null || circle == null)
        {
            const int n = 64;
            arrow = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.HideAndDontSave };
            circle = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.HideAndDontSave };
            var pa = new Color[n * n];
            var pc = new Color[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float u = (x + 0.5f) / n * 2f - 1f, v = (y + 0.5f) / n * 2f - 1f; // v вверх
                    float half = Mathf.Lerp(0.75f, 0f, (v + 0.85f) / 1.7f);
                    bool inside = v > -0.85f && v < 0.85f && Mathf.Abs(u) < half && v > -0.85f + Mathf.Abs(u) * 0.8f;
                    bool outline = !inside && v > -1f && v < 1f && Mathf.Abs(u) < half + 0.14f && v > -1f + Mathf.Abs(u) * 0.8f;
                    pa[y * n + x] = inside ? Color.white : outline ? new Color(0.08f, 0.03f, 0.03f, 1f) : Color.clear;
                    float d = Mathf.Sqrt(u * u + v * v);
                    pc[y * n + x] = new Color(1f, 1f, 1f, Mathf.Clamp01((1f - d) * 6f));
                }
            arrow.SetPixels(pa);
            arrow.Apply();
            circle.SetPixels(pc);
            circle.Apply();
        }
        return round ? circle : arrow;
    }

    void ShowToast(string text)
    {
        toast = text;
        toastUntil = Time.unscaledTime + 2.2f;
    }
}
