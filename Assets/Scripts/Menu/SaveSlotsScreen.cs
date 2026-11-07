using UnityEngine;

/// <summary>
/// Таблица из 6 ячеек-слотов сохранения (3 × 2): снимок экрана, номер слота, дата и время записи.
/// В режиме сохранения клик по ячейке записывает игру в неё, в режиме загрузки — загружает.
/// Кнопка «Удалить» на заполненной ячейке стирает сохранение (после подтверждения).
/// </summary>
public class SaveSlotsScreen
{
    SaveSystem.SaveData[] data = new SaveSystem.SaveData[SaveSystem.SlotCount];
    readonly Texture2D[] thumbs = new Texture2D[SaveSystem.SlotCount];
    int flashSlot = -1;
    float flashUntil;
    int confirmDelete = -1; // слот, удаление которого ждёт подтверждения

    /// <summary>Esc: закрыть окно подтверждения удаления. true — оно было открыто.</summary>
    public bool HandleEscape()
    {
        if (confirmDelete < 0) return false;
        confirmDelete = -1;
        return true;
    }

    public bool AnySave
    {
        get
        {
            foreach (var d in data) if (d != null) return true;
            return false;
        }
    }

    public SaveSystem.SaveData Data(int slot) => data[slot];

    /// <summary>Перечитать все слоты с диска.</summary>
    public void Refresh()
    {
        data = SaveSystem.LoadAll();
        for (int i = 0; i < thumbs.Length; i++) ReloadThumb(i);
    }

    /// <summary>Перечитать один слот (после записи) и подсветить его.</summary>
    public void RefreshSlot(int slot)
    {
        data[slot] = SaveSystem.Load(slot);
        ReloadThumb(slot);
        flashSlot = slot;
        flashUntil = Time.unscaledTime + 1.2f;
    }

    void ReloadThumb(int i)
    {
        if (thumbs[i] != null) Object.Destroy(thumbs[i]);
        thumbs[i] = data[i] != null ? SaveSystem.LoadThumbnail(i) : null;
    }

    /// <summary>Самый свежий заполненный слот, -1 — сохранений нет.</summary>
    public int Latest => SaveSystem.LatestSlot(data);

    public void Dispose()
    {
        for (int i = 0; i < thumbs.Length; i++)
        {
            if (thumbs[i] != null) Object.Destroy(thumbs[i]);
            thumbs[i] = null;
        }
    }

    /// <summary>Рисует таблицу. Возвращает номер нажатой ячейки или -1; back — нажата «Назад».</summary>
    public int Draw(float cx, bool saveMode, out bool back)
    {
        float S(float v) => MenuUI.S(v);
        float top = MenuUI.ContentTop;
        int clicked = -1;

        bool modal = confirmDelete >= 0;
        bool guiWas = GUI.enabled;
        if (modal) GUI.enabled = false;

        var caption = saveMode ? "Выберите ячейку, чтобы записать в неё игру" : "Выберите сохранение для загрузки";
        MenuUI.ShadowLabel(new Rect(0, top - S(6), Screen.width, S(44)), caption, MenuUI.LabelCenter);

        const int cols = 3;
        float cellW = S(330), gap = S(26), pad = S(8);
        float thumbH = (cellW - pad * 2f) * 9f / 16f, textH = S(66);
        float cellH = pad + thumbH + textH;
        float gridW = cols * cellW + (cols - 1) * gap;
        float x0 = cx - gridW / 2f, y0 = top + S(54);

        var nameStyle = new GUIStyle(MenuUI.Label) { fontStyle = FontStyle.Bold, fontSize = Mathf.RoundToInt(S(24)) };
        var dateStyle = new GUIStyle(MenuUI.Small) { alignment = TextAnchor.MiddleLeft, fontSize = Mathf.RoundToInt(S(19)) };
        dateStyle.normal.textColor = new Color(1f, 1f, 1f, 0.75f);
        var emptyStyle = new GUIStyle(MenuUI.LabelCenter) { fontSize = Mathf.RoundToInt(S(22)) };
        emptyStyle.normal.textColor = new Color(1f, 1f, 1f, 0.35f);

        for (int i = 0; i < SaveSystem.SlotCount; i++)
        {
            int col = i % cols, row = i / cols;
            var cell = new Rect(x0 + col * (cellW + gap), y0 + row * (cellH + gap), cellW, cellH);
            var d = data[i];
            bool enabled = saveMode || d != null;
            var tr = new Rect(cell.x + pad, cell.y + pad, cell.width - pad * 2f, thumbH);

            // Кнопка «Удалить» в углу снимка. Обрабатывается до ячейки (в IMGUI клик достаётся первой нарисованной кнопке),
            // а рисуется поверх неё ниже.
            var del = new Rect(tr.xMax - S(112), tr.y + S(6), S(106), S(36));
            bool delHover = false;
            if (d != null)
            {
                delHover = !modal && del.Contains(Event.current.mousePosition);
                if (GUI.Button(del, GUIContent.none, GUIStyle.none)) confirmDelete = i;
            }

            if (MenuUI.Button(cell, "", enabled)) clicked = i;

            if (!enabled) GUI.color = new Color(1f, 1f, 1f, 0.5f);
            if (d != null && thumbs[i] != null)
                GUI.DrawTexture(tr, thumbs[i], ScaleMode.ScaleAndCrop);
            else
            {
                MenuUI.Fill(tr, new Color(0f, 0f, 0f, 0.55f));
                GUI.Label(tr, d == null ? "Пусто" : "Нет снимка", emptyStyle);
            }

            var textR = new Rect(cell.x + S(14), tr.yMax + S(4), cell.width - S(28), textH - S(8));
            GUI.Label(new Rect(textR.x, textR.y, textR.width, textR.height * 0.5f), "Слот " + (i + 1), nameStyle);
            GUI.Label(new Rect(textR.x, textR.y + textR.height * 0.5f, textR.width, textR.height * 0.5f),
                d != null ? d.savedAt : (saveMode ? "Нажмите, чтобы сохранить" : "—"), dateStyle);
            GUI.color = Color.white;

            if (d != null)
            {
                MenuUI.Fill(del, delHover ? new Color(0.9f, 0.15f, 0.1f, 0.95f) : new Color(0f, 0f, 0f, 0.7f));
                var delStyle = new GUIStyle(MenuUI.LabelCenter) { fontSize = Mathf.RoundToInt(S(18)), fontStyle = FontStyle.Bold, wordWrap = false };
                GUI.Label(del, "Удалить", delStyle);
            }

            // Подсветка только что записанной ячейки.
            if (i == flashSlot && Time.unscaledTime < flashUntil)
            {
                float a = Mathf.Clamp01((flashUntil - Time.unscaledTime) / 1.2f);
                MenuUI.Frame(cell, S(4), new Color(1f, 0.85f, 0.4f, a));
            }
        }

        var centered = new GUIStyle(MenuUI.ButtonStyle) { alignment = TextAnchor.MiddleCenter, padding = new RectOffset(0, 0, 0, 0) };
        back = MenuUI.Button(MenuUI.ButtonRect(cx, MenuUI.FooterTop, 0), "Назад", true, centered);
        GUI.enabled = guiWas;

        if (modal)
        {
            back = false;
            clicked = -1;
            DrawConfirmDelete(cx, centered);
        }
        return clicked;
    }

    void DrawConfirmDelete(float cx, GUIStyle centered)
    {
        float S(float v) => MenuUI.S(v);
        MenuUI.Dim(0.6f);
        var box = new Rect(cx - S(380), Screen.height / 2f - S(130), S(760), S(240));
        MenuUI.Fill(box, new Color(0.05f, 0.03f, 0.03f, 0.97f));
        MenuUI.Frame(box, S(2), MenuUI.Accent);
        var d = data[confirmDelete];
        string text = "Удалить сохранение в слоте " + (confirmDelete + 1) + "?" + (d != null ? "\n" + d.savedAt : "");
        GUI.Label(new Rect(box.x + S(20), box.y + S(20), box.width - S(40), S(110)), text, MenuUI.LabelCenter);
        float bw = S(300), bh = S(60), by = box.yMax - bh - S(24);
        if (MenuUI.Button(new Rect(cx - bw - S(10), by, bw, bh), "Удалить", true, centered))
        {
            SaveSystem.Delete(confirmDelete);
            data[confirmDelete] = null;
            ReloadThumb(confirmDelete);
            if (flashSlot == confirmDelete) flashSlot = -1;
            confirmDelete = -1;
        }
        else if (MenuUI.Button(new Rect(cx + S(10), by, bw, bh), "Отмена", true, centered))
            confirmDelete = -1;
    }
}
