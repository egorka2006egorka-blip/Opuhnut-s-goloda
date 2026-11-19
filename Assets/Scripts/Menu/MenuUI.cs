using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Общее оформление меню (главное меню и пауза) на IMGUI: стили, кнопки, экран настроек
/// с вкладками «Графика», «Звук», «Управление». Размеры масштабируются по высоте экрана (рассчитаны на 1080p).
/// </summary>
public static class MenuUI
{
    /// <summary>Название игры — как в Project Settings → Player → Product Name.</summary>
    public static string GameTitle => Application.productName;

    public static readonly Color Accent = new Color(0.85f, 0.16f, 0.12f);
    public static readonly Color TextColor = new Color(0.93f, 0.9f, 0.86f);

    static Texture2D texButton, texButtonHover, texButtonActive, texAccent, texSlider, texThumb, texThumbHover, texGradient, texVignette, texSide;
    static GUIStyle button, buttonLarge, buttonCenter, tabActive, title, titleLeft, label, labelCenter, small, value, slider, thumb;
    static int stylesHeight = -1;

    /// <summary>Верх содержимого экранов настроек и сохранений (под заголовком).</summary>
    public static float ContentTop => S(290);
    /// <summary>Строка с кнопкой «Назад» на экранах настроек и сохранений.</summary>
    public static float FooterTop => ContentTop + S(610);

    public static float Scale => Mathf.Max(0.5f, Screen.height / 1080f);
    public static float S(float v) => v * Scale;

    public static GUIStyle Label { get { Ensure(); return label; } }
    public static GUIStyle LabelCenter { get { Ensure(); return labelCenter; } }
    public static GUIStyle Small { get { Ensure(); return small; } }
    public static GUIStyle ButtonStyle { get { Ensure(); return button; } }
    /// <summary>Крупные кнопки главного меню.</summary>
    public static GUIStyle ButtonLarge { get { Ensure(); return buttonLarge; } }
    /// <summary>Кнопка с текстом по центру (как вкладки настроек).</summary>
    public static GUIStyle ButtonCenterStyle { get { Ensure(); return buttonCenter; } }

    /// <summary>Вкладка: активная — с красной подложкой. true — по ней кликнули.</summary>
    public static bool Tab(Rect r, string text, bool active)
    {
        Ensure();
        return GUI.Button(r, text, active ? tabActive : buttonCenter);
    }

    static Texture2D Solid(Color c)
    {
        var t = new Texture2D(1, 1, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave };
        t.SetPixel(0, 0, c);
        t.Apply();
        return t;
    }

    static void Ensure()
    {
        if (texButton == null)
        {
            texButton = Solid(new Color(0.06f, 0.05f, 0.05f, 0.72f));
            texButtonHover = Solid(new Color(Accent.r, Accent.g, Accent.b, 0.85f));
            texButtonActive = Solid(new Color(0.55f, 0.08f, 0.06f, 0.95f));
            texAccent = Solid(new Color(Accent.r * 0.8f, Accent.g * 0.8f, Accent.b * 0.8f, 0.95f));
            texSlider = Solid(new Color(1f, 1f, 1f, 0.18f));
            texThumb = Solid(TextColor);
            texThumbHover = Solid(Accent);
            texGradient = Gradient();
            texVignette = Vignette();
            texSide = SideGradient();
        }
        if (stylesHeight == Screen.height && button != null) return;
        stylesHeight = Screen.height;

        button = new GUIStyle(GUI.skin.button)
        {
            fontSize = Mathf.RoundToInt(S(30)),
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleLeft,
            padding = new RectOffset(Mathf.RoundToInt(S(28)), 10, 0, 0),
            border = new RectOffset(0, 0, 0, 0),
            wordWrap = false,
            clipping = TextClipping.Clip,
        };
        button.normal.background = texButton;
        button.hover.background = texButtonHover;
        button.active.background = texButtonActive;
        button.focused.background = texButton;
        button.normal.textColor = TextColor;
        button.hover.textColor = Color.white;
        button.active.textColor = Color.white;
        button.focused.textColor = TextColor;

        // Кнопки главного меню — без фона и рамки, только текст; при наведении текст подсвечивается.
        // Стиль создаётся с нуля: копия кнопки скина тянет за собой его фоны (в т. ч. scaledBackgrounds для HiDPI).
        buttonLarge = new GUIStyle
        {
            font = GUI.skin.button.font,
            fontSize = Mathf.RoundToInt(S(36)),
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleLeft,
            padding = new RectOffset(0, 10, 0, 0),
            border = new RectOffset(0, 0, 0, 0),
            margin = new RectOffset(0, 0, 0, 0),
            overflow = new RectOffset(0, 0, 0, 0),
            wordWrap = false,
            clipping = TextClipping.Overflow,
        };
        buttonLarge.normal.textColor = TextColor;
        buttonLarge.focused.textColor = TextColor;
        buttonLarge.hover.textColor = new Color(0.95f, 0.08f, 0.06f); // красный при наведении
        buttonLarge.active.textColor = new Color(0.65f, 0.03f, 0.02f);
        buttonCenter = new GUIStyle(button) { alignment = TextAnchor.MiddleCenter, padding = new RectOffset(4, 4, 0, 0), fontSize = Mathf.RoundToInt(S(24)) };
        tabActive = new GUIStyle(buttonCenter) { fontSize = Mathf.RoundToInt(S(28)) };
        tabActive.normal.background = texAccent;
        tabActive.hover.background = texAccent;
        tabActive.normal.textColor = Color.white;

        title = new GUIStyle(GUI.skin.label)
        {
            fontSize = Mathf.RoundToInt(S(96)),
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleCenter,
            wordWrap = true,
        };
        title.normal.textColor = Color.white;
        titleLeft = new GUIStyle(title) { alignment = TextAnchor.UpperLeft, wordWrap = false, fontSize = Mathf.RoundToInt(S(78)) };

        label = new GUIStyle(GUI.skin.label)
        {
            fontSize = Mathf.RoundToInt(S(26)),
            alignment = TextAnchor.MiddleLeft,
            wordWrap = false,
            clipping = TextClipping.Clip,
        };
        label.normal.textColor = TextColor;
        labelCenter = new GUIStyle(label) { alignment = TextAnchor.MiddleCenter, wordWrap = true };

        value = new GUIStyle(label) { alignment = TextAnchor.MiddleRight, fontStyle = FontStyle.Bold };
        small = new GUIStyle(label) { fontSize = Mathf.RoundToInt(S(18)), alignment = TextAnchor.MiddleCenter };
        small.normal.textColor = new Color(TextColor.r, TextColor.g, TextColor.b, 0.6f);

        slider = new GUIStyle(GUI.skin.horizontalSlider) { fixedHeight = S(8), border = new RectOffset(0, 0, 0, 0), margin = new RectOffset(0, 0, 0, 0) };
        slider.normal.background = texSlider;
        thumb = new GUIStyle(GUI.skin.horizontalSliderThumb) { fixedWidth = S(18), fixedHeight = S(26), border = new RectOffset(0, 0, 0, 0) };
        thumb.normal.background = texThumb;
        thumb.hover.background = texThumbHover;
        thumb.active.background = texThumbHover;
    }

    /// <summary>Тёмный фон с красноватым градиентом и затемнёнными краями.</summary>
    public static void Background(float alpha = 1f)
    {
        Ensure();
        var full = new Rect(0, 0, Screen.width, Screen.height);
        GUI.color = new Color(1f, 1f, 1f, alpha);
        GUI.DrawTexture(full, texGradient);
        GUI.color = new Color(0f, 0f, 0f, 0.85f * alpha);
        GUI.DrawTexture(full, texVignette);
        GUI.color = Color.white;
    }

    /// <summary>
    /// Затемнение поверх 3D-фона: sidePanel — плотное слева (под названием и кнопками) и прозрачное справа,
    /// иначе — равномерное (экраны настроек и загрузки).
    /// </summary>
    public static void BackgroundOver3D(bool sidePanel)
    {
        Ensure();
        var full = new Rect(0, 0, Screen.width, Screen.height);
        if (sidePanel) GUI.DrawTexture(full, texSide);
        else Fill(full, new Color(0f, 0f, 0f, 0.62f));
        GUI.color = new Color(0f, 0f, 0f, 0.6f);
        GUI.DrawTexture(full, texVignette);
        GUI.color = Color.white;
    }

    /// <summary>Затемнение поверх игры (меню паузы).</summary>
    public static void Dim(float alpha) => Fill(new Rect(0, 0, Screen.width, Screen.height), new Color(0f, 0f, 0f, alpha));

    public static void Fill(Rect r, Color c)
    {
        var was = GUI.color;
        GUI.color = c;
        GUI.DrawTexture(r, Texture2D.whiteTexture);
        GUI.color = was;
    }

    /// <summary>Рамка толщиной t.</summary>
    public static void Frame(Rect r, float t, Color c)
    {
        Fill(new Rect(r.x, r.y, r.width, t), c);
        Fill(new Rect(r.x, r.yMax - t, r.width, t), c);
        Fill(new Rect(r.x, r.y, t, r.height), c);
        Fill(new Rect(r.xMax - t, r.y, t, r.height), c);
    }

    /// <summary>Крупный заголовок с тенью и красной полосой под ним. left — прижать к левому верхнему углу r.</summary>
    public static void Title(Rect r, string text, float pulse = 0f, bool left = false)
    {
        Ensure();
        var style = left ? titleLeft : title;
        var shadow = new Rect(r.x + S(5), r.y + S(5), r.width, r.height);
        var was = GUI.color;
        GUI.color = new Color(0f, 0f, 0f, 0.85f * was.a);
        GUI.Label(shadow, text, style);
        var c = Color.Lerp(new Color(0.95f, 0.88f, 0.8f), new Color(1f, 0.55f, 0.45f), pulse);
        GUI.color = new Color(c.r, c.g, c.b, was.a);
        GUI.Label(r, text, style);
        GUI.color = new Color(Accent.r, Accent.g, Accent.b, was.a);
        if (left)
        {
            // Полоса под текстом — ровно по ширине названия.
            var size = style.CalcSize(new GUIContent(text));
            float pl = style.padding.left, pr = style.padding.right;
            float w = Mathf.Min(size.x - pl - pr, r.width);
            GUI.DrawTexture(new Rect(r.x + pl, r.y + size.y + S(2), w, S(5)), Texture2D.whiteTexture);
        }
        else
        {
            float w = Mathf.Min(r.width * 0.5f, S(520));
            GUI.DrawTexture(new Rect(r.center.x - w / 2f, r.yMax + S(4), w, S(5)), Texture2D.whiteTexture);
        }
        GUI.color = was;
    }

    public static bool Button(Rect r, string text, bool enabled = true, GUIStyle style = null)
    {
        Ensure();
        bool was = GUI.enabled;
        GUI.enabled = was && enabled;
        if (!enabled) GUI.color = new Color(1f, 1f, 1f, 0.4f);
        bool clicked = GUI.Button(r, text, style ?? button);
        GUI.color = Color.white;
        GUI.enabled = was;
        return clicked;
    }

    /// <summary>Вертикальный столбец кнопок: прямоугольник кнопки с номером index.</summary>
    public const float ButtonWidth = 440f;

    public static Rect ButtonRect(float centerX, float top, int index)
    {
        float w = S(ButtonWidth), h = S(64), gap = S(14);
        return new Rect(centerX - w / 2f, top + index * (h + gap), w, h);
    }

    public static void ShadowLabel(Rect r, string text, GUIStyle style)
    {
        var c = GUI.color;
        GUI.color = new Color(0f, 0f, 0f, 0.8f * c.a);
        GUI.Label(new Rect(r.x + 2, r.y + 2, r.width, r.height), text, style);
        GUI.color = c;
        GUI.Label(r, text, style);
    }

    // ---------- Переназначение клавиш ----------

    static GameAction? rebinding;
    static bool blockMouse; // после захвата клавиши — до отпускания кнопок мыши клики по меню игнорируются

    public static bool IsRebinding => rebinding != null;

    /// <summary>Вызывать в Update меню. true — Esc в этом кадре израсходован (отмена переназначения).</summary>
    public static bool Tick()
    {
        var m = Mouse.current;
        if (blockMouse && (m == null || !(m.leftButton.isPressed || m.rightButton.isPressed || m.middleButton.isPressed)))
            blockMouse = false;
        if (rebinding == null) return false;

        var kb = Keyboard.current;
        if (kb != null && kb.escapeKey.wasPressedThisFrame)
        {
            rebinding = null;
            return true;
        }
        if (Controls.TryCapture(out int code))
        {
            Controls.Rebind(rebinding.Value, code);
            rebinding = null;
            blockMouse = true;
        }
        return false;
    }

    // ---------- Экран настроек ----------

    static int settingsTab;
    static readonly string[] tabNames = { "Графика", "Звук", "Управление" };

    /// <summary>Рисует экран настроек. true — нажата «Назад».</summary>
    public static bool SettingsScreen(float centerX)
    {
        Ensure();
        // Пока ждём клавишу (и сразу после) — мышь по меню не кликает.
        if ((rebinding != null || blockMouse) && Event.current.isMouse) Event.current.Use();

        float top = ContentTop;
        float panelW = S(1040), panelH = S(500);
        var panel = new Rect(centerX - panelW / 2f, top + S(76), panelW, panelH);

        // Вкладки.
        float tabW = S(300), tabH = S(58), tabGap = S(16);
        float tabsX = centerX - (tabW * 3 + tabGap * 2) / 2f;
        for (int i = 0; i < tabNames.Length; i++)
        {
            var tr = new Rect(tabsX + i * (tabW + tabGap), top, tabW, tabH);
            if (GUI.Button(tr, tabNames[i], i == settingsTab ? tabActive : buttonCenter) && rebinding == null) settingsTab = i;
        }

        Fill(panel, new Color(0f, 0f, 0f, 0.6f));
        Frame(panel, Mathf.Max(1f, S(2)), new Color(Accent.r, Accent.g, Accent.b, 0.6f));
        var inner = new Rect(panel.x + S(40), panel.y + S(24), panel.width - S(80), panel.height - S(48));

        switch (settingsTab)
        {
            case 0: GraphicsTab(inner); break;
            case 1: SoundTab(inner); break;
            default: ControlsTab(inner); break;
        }

        bool back;
        if (settingsTab == 2)
        {
            float w = S(440), h = S(64), gap = S(20);
            back = Button(new Rect(centerX - w - gap / 2f, FooterTop, w, h), "Назад", true, buttonCenter);
            if (Button(new Rect(centerX + gap / 2f, FooterTop, w, h), "Клавиши по умолчанию", true, buttonCenter))
            {
                Controls.ResetDefaults();
                GameSettings.SetMouseSensitivity(1f);
            }
        }
        else back = Button(ButtonRect(centerX, FooterTop, 0), "Назад", true, buttonCenter);

        if (back)
        {
            rebinding = null;
            GameSettings.Save();
        }
        return back;
    }

    static Rect RowRect(Rect inner, int row, float rowH = 0f)
    {
        if (rowH <= 0f) rowH = S(68);
        return new Rect(inner.x, inner.y + row * rowH, inner.width, rowH);
    }

    static void GraphicsTab(Rect inner)
    {
        var res = GameSettings.Resolutions;
        int ri = GameSettings.ResolutionIndex;
        var r = RowRect(inner, 0);
        int d = SelectorRow(r, "Разрешение", res[ri].x + " × " + res[ri].y);
        // По кругу: левее минимального — максимальное, правее максимального — минимальное.
        if (d != 0) GameSettings.SetResolution(res[(ri + d + res.Count) % res.Count]);

        var modes = GameSettings.ScreenModes;
        int mi = System.Array.IndexOf(modes, GameSettings.ScreenMode);
        if (mi < 0) mi = 0;
        d = SelectorRow(RowRect(inner, 1), "Режим экрана", GameSettings.ScreenModeName(modes[mi]));
        if (d != 0) GameSettings.SetScreenMode(modes[(mi + d + modes.Length) % modes.Length]);

        int q = GameSettings.Quality;
        d = SelectorRow(RowRect(inner, 2), "Качество графики", GameSettings.QualityName(q));
        if (d != 0) GameSettings.SetQuality(q + d);

        if (SelectorRow(RowRect(inner, 3), "Вертикальная синхронизация", GameSettings.VSync ? "Вкл" : "Выкл") != 0)
            GameSettings.SetVSync(!GameSettings.VSync);

        var filters = GameSettings.Filters;
        int fi = System.Array.IndexOf(filters, GameSettings.Filter);
        d = SelectorRow(RowRect(inner, 4), "Фильтр", GameSettings.FilterName(filters[fi]));
        if (d != 0) GameSettings.SetFilter(filters[(fi + d + filters.Length) % filters.Length]);

        // Сила фильтра — только когда фильтр выбран.
        if (GameSettings.Filter != ScreenFilterKind.None)
        {
            float fv = SliderRow(RowRect(inner, 5), "Сила фильтра", GameSettings.FilterIntensity, 0f, 1f, Percent(GameSettings.FilterIntensity));
            if (!Mathf.Approximately(fv, GameSettings.FilterIntensity)) GameSettings.SetFilterIntensity(fv);
        }
    }

    static void SoundTab(Rect inner)
    {
        float v = SliderRow(RowRect(inner, 0), "Общая громкость", GameSettings.MasterVolume, 0f, 1f, Percent(GameSettings.MasterVolume));
        if (!Mathf.Approximately(v, GameSettings.MasterVolume)) GameSettings.SetMasterVolume(v);

        CategoryRow(RowRect(inner, 1), "Эффекты", SoundKind.Effects);
        CategoryRow(RowRect(inner, 2), "Музыка", SoundKind.Music);
        CategoryRow(RowRect(inner, 3), "Голоса", SoundKind.Voice);
    }

    static void CategoryRow(Rect r, string text, SoundKind kind)
    {
        float cur = GameSettings.CategoryVolume(kind);
        float v = SliderRow(r, text, cur, 0f, 1f, Percent(cur));
        if (!Mathf.Approximately(v, cur)) GameSettings.SetCategoryVolume(kind, v);
    }

    static string Percent(float v) => Mathf.RoundToInt(v * 100f) + "%";

    static void ControlsTab(Rect inner)
    {
        float sens = SliderRow(RowRect(inner, 0, S(62)), "Чувствительность мыши", GameSettings.MouseSensitivity,
            GameSettings.MinSensitivity, GameSettings.MaxSensitivity, GameSettings.MouseSensitivity.ToString("0.0"));
        if (!Mathf.Approximately(sens, GameSettings.MouseSensitivity)) GameSettings.SetMouseSensitivity(sens);

        var actions = Controls.Actions;
        int perCol = (actions.Length + 1) / 2;
        float gap = S(40), colW = (inner.width - gap) / 2f, rowH = S(50), y0 = inner.y + S(70);
        for (int i = 0; i < actions.Length; i++)
        {
            int col = i / perCol, row = i % perCol;
            var r = new Rect(inner.x + col * (colW + gap), y0 + row * rowH, colW, rowH - S(8));
            var a = actions[i];
            GUI.Label(new Rect(r.x, r.y, r.width * 0.55f, r.height), Controls.ActionName(a), label);
            var br = new Rect(r.x + r.width * 0.57f, r.y, r.width * 0.43f, r.height);
            if (rebinding == a)
            {
                float blink = 0.6f + 0.4f * Mathf.Sin(Time.unscaledTime * 8f);
                Fill(br, new Color(Accent.r, Accent.g, Accent.b, 0.9f * blink));
                GUI.Label(br, "Нажмите клавишу…", new GUIStyle(labelCenter) { fontSize = Mathf.RoundToInt(S(20)), wordWrap = false });
            }
            else if (GUI.Button(br, Controls.KeyName(a), buttonCenter) && rebinding == null)
                rebinding = a;
        }
    }

    static float SliderRow(Rect r, string text, float v, float min, float max, string valueText)
    {
        float labelW = r.width * 0.42f, valueW = S(90);
        GUI.Label(new Rect(r.x, r.y, labelW, r.height), text, label);
        float cx = r.x + labelW, cw = r.width - labelW;
        var sr = new Rect(cx, r.y + (r.height - S(26)) / 2f, cw - valueW - S(16), S(26));
        float res = GUI.HorizontalSlider(sr, v, min, max, slider, thumb);
        GUI.Label(new Rect(cx + cw - valueW, r.y, valueW, r.height), valueText, value);
        return res;
    }

    /// <summary>Строка «название  &lt; значение &gt;»: -1 / +1 при нажатии стрелок, 0 — ничего.</summary>
    static int SelectorRow(Rect r, string text, string valueText)
    {
        float labelW = r.width * 0.42f;
        GUI.Label(new Rect(r.x, r.y, labelW, r.height), text, label);
        float cx = r.x + labelW, cw = r.width - labelW;
        float bw = S(52), bh = S(46), y = r.y + (r.height - bh) / 2f;
        int res = 0;
        if (GUI.Button(new Rect(cx, y, bw, bh), "<", buttonCenter)) res = -1;
        if (GUI.Button(new Rect(cx + cw - bw, y, bw, bh), ">", buttonCenter)) res = 1;
        var mid = new GUIStyle(labelCenter) { fontStyle = FontStyle.Bold, wordWrap = false };
        GUI.Label(new Rect(cx + bw, r.y, cw - bw * 2f, r.height), valueText, mid);
        return res;
    }

    // ---------- Текстуры ----------

    static Texture2D Gradient()
    {
        const int n = 64;
        var t = new Texture2D(1, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.HideAndDontSave };
        for (int j = 0; j < n; j++)
        {
            float k = j / (n - 1f); // 0 — низ, 1 — верх
            t.SetPixel(0, j, Color.Lerp(new Color(0.02f, 0.01f, 0.01f), new Color(0.22f, 0.04f, 0.03f), k * k));
        }
        t.Apply();
        return t;
    }

    static Texture2D SideGradient()
    {
        const int n = 256;
        var t = new Texture2D(n, 1, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.HideAndDontSave };
        for (int i = 0; i < n; i++)
        {
            float x = i / (n - 1f);
            float a = 0.93f * (1f - Mathf.SmoothStep(0.2f, 0.62f, x));
            t.SetPixel(i, 0, new Color(0.01f, 0.005f, 0.005f, a));
        }
        t.Apply();
        return t;
    }

    static Texture2D Vignette()
    {
        const int n = 64;
        var t = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.HideAndDontSave };
        for (int j = 0; j < n; j++)
            for (int i = 0; i < n; i++)
            {
                float dx = (i + 0.5f) / n * 2f - 1f, dy = (j + 0.5f) / n * 2f - 1f;
                float d = Mathf.Sqrt(dx * dx + dy * dy) / 1.414f;
                float a = Mathf.Clamp01((d - 0.35f) / 0.65f);
                t.SetPixel(i, j, new Color(1f, 1f, 1f, a * a));
            }
        t.Apply();
        return t;
    }
}
