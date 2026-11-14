using UnityEngine;

/// <summary>
/// Состояние героя в инвентаре: пиксельное лицо студента (меняется от здоровья, моргает)
/// и монитор сердцебиения справа — зелёный спокойный пульс, жёлтый учащённый, красный быстрый и сбивчивый.
/// </summary>
public class HealthPanel
{
    // ---------- Пиксельное лицо ----------

    // Худое лицо студента. Левая половина 32×32 (16 символов), правая — зеркальная. Детали выражений дорисовываются в Face().
    static readonly string[] Base =
    {
        "................",
        "...........K.KKK",
        "........KKKhKHHH",
        "......KKHHHHHHHH",
        ".....KHHHhhHHHhH",
        ".....KHHHHHHHHHH",
        "....KHHHHHHHHHHH",
        "....KHHHHHHHHHHH",
        "....KHHHHHHHHHHH",
        "....KHHHHHHHHHHH",
        "....KHHHHHSHHHSH",
        "....KHHHHSSSHSSS",
        "....KHHSSSSSSSSS",
        ".....KSSSSSSSSSS",
        ".....KSSSSSSSSSS",
        "....KKSSSSSSSSSS",
        "...KsKSSSSSSSSSS",
        "...KssSSSSSSSSSS",
        "...KsKsSSSSSSSSS",
        "....KKsSSSSSSSSS",
        ".....KsSSSSSSSSS",
        "......KsSSSSSSSS",
        "......KsSSSSSSSS",
        ".......KsSSSSSSS",
        ".......KsSSSSSSS",
        "........KsSSSSSS",
        ".........KKsSSSS",
        "...........KKKKs",
        "......CCCCCCKsss",
        "....CCCCCCCCcKss",
        "..CCCCCCCCCCCcKD",
        ".CCCCCCCCCCCCcDD",
    };

    static Color32 Pal(char ch)
    {
        switch (ch)
        {
            case 'K': return new Color32(42, 29, 23, 255);    // контур
            case 'H': return new Color32(74, 47, 31, 255);    // волосы
            case 'h': return new Color32(110, 72, 48, 255);   // блик на волосах
            case 'S': return new Color32(232, 181, 142, 255); // кожа
            case 's': return new Color32(201, 143, 107, 255); // тень на коже
            case 'W': return new Color32(245, 245, 240, 255); // белки глаз
            case 'P': return new Color32(30, 30, 44, 255);    // зрачки
            case 'B': return new Color32(52, 34, 22, 255);    // брови
            case 'M': return new Color32(150, 70, 60, 255);   // губы
            case 'm': return new Color32(90, 30, 30, 255);    // открытый рот
            case 'C': return new Color32(61, 90, 128, 255);   // худи
            case 'c': return new Color32(44, 66, 96, 255);    // тень на худи
            case 'D': return new Color32(220, 220, 220, 255); // футболка под худи
            case 'R': return new Color32(170, 20, 20, 255);   // кровь
            case 'r': return new Color32(120, 10, 10, 255);   // тёмная кровь
            case 'V': return new Color32(130, 80, 120, 255);  // синяк
            case 'v': return new Color32(160, 110, 140, 255);
            case 'T': return new Color32(150, 200, 240, 255); // капля пота
            case 'w': return new Color32(255, 255, 255, 255); // зубы
            default: return new Color32(0, 0, 0, 0);
        }
    }

    /// <summary>Лицо: mood 0 — в порядке, 1 — ранен, 2 — тяжело ранен.</summary>
    static Texture2D Face(int mood, bool blink)
    {
        var g = new char[32, 32];
        for (int y = 0; y < 32; y++)
            for (int x = 0; x < 16; x++) { g[x, y] = Base[y][x]; g[31 - x, y] = Base[y][x]; }
        void P(int x, int y, char c) => g[x, y] = c;
        void Sym(int x, int y, char c) { g[x, y] = c; g[31 - x, y] = c; }
        void Eyes(int top)
        {
            if (blink) { for (int x = 9; x < 13; x++) Sym(x, 17, 'K'); return; }
            for (int x = 9; x < 13; x++)
                for (int y = top; y <= 17; y++) Sym(x, y, 'W');
        }

        Sym(15, 20, 's'); P(15, 19, 's'); // нос
        for (int y = 19; y <= 21; y++) Sym(8, y, 's'); // впалые щёки
        if (mood == 0)
        {
            for (int x = 9; x < 13; x++) Sym(x, 14, 'B');
            Sym(9, 15, 'B');
            Eyes(16);
            if (!blink) for (int y = 16; y <= 17; y++) { Sym(11, y, 'P'); Sym(12, y, 'P'); }
            Sym(14, 24, 'M'); Sym(15, 24, 'M'); Sym(13, 23, 'M'); // улыбка
        }
        else if (mood == 1)
        {
            Sym(9, 15, 'B'); Sym(10, 15, 'B'); Sym(11, 14, 'B'); Sym(12, 14, 'B'); Sym(12, 13, 'B'); // брови домиком
            Eyes(16);
            if (!blink) for (int y = 16; y <= 17; y++) { Sym(11, y, 'P'); Sym(12, y, 'P'); }
            for (int x = 9; x < 13; x++) Sym(x, 18, 's'); // круги под глазами
            for (int x = 13; x < 16; x++) Sym(x, 24, 'M');
            P(25, 12, 'T'); P(25, 13, 'T'); P(24, 13, 'T'); // пот
        }
        else
        {
            Sym(9, 15, 'B'); Sym(10, 14, 'B'); Sym(11, 13, 'B'); Sym(12, 13, 'B');
            Eyes(15);
            if (!blink) { Sym(11, 16, 'P'); Sym(12, 16, 'P'); }
            for (int x = 19; x < 24; x++) P(x, 18, 'V'); // синяк
            P(19, 19, 'v'); P(23, 19, 'v'); P(23, 15, 'V'); P(24, 16, 'V');
            for (int x = 13; x < 16; x++) { Sym(x, 23, 'K'); Sym(x, 25, 'K'); Sym(x, 24, 'm'); } // открытый рот
            Sym(12, 24, 'K'); Sym(14, 24, 'w');
            for (int y = 11; y < 15; y++) P(8, y, 'R'); // кровь со лба и из носа
            P(9, 12, 'R'); P(8, 15, 'r');
            P(17, 21, 'R'); P(17, 22, 'R'); P(16, 22, 'r');
        }

        var tex = new Texture2D(32, 32, TextureFormat.RGBA32, false)
        { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.HideAndDontSave };
        var px = new Color32[32 * 32];
        for (int y = 0; y < 32; y++)
            for (int x = 0; x < 32; x++)
                px[(31 - y) * 32 + x] = Pal(g[x, y]); // в текстуре строки идут снизу вверх
        tex.SetPixels32(px);
        tex.Apply();
        return tex;
    }

    static Texture2D Heart()
    {
        string[] rows = { ".XX.XX.", "XXXXXXX", "XXXXXXX", ".XXXXX.", "..XXX..", "...X..." };
        var tex = new Texture2D(7, 6, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, hideFlags = HideFlags.HideAndDontSave };
        for (int y = 0; y < 6; y++)
            for (int x = 0; x < 7; x++)
                tex.SetPixel(x, 5 - y, rows[y][x] == 'X' ? Color.white : Color.clear);
        tex.Apply();
        return tex;
    }

    readonly Texture2D[,] faces = new Texture2D[3, 2];
    Texture2D heart;

    // ---------- Пульс ----------

    const int Columns = 220;            // ширина ленты монитора в «пикселях» развёртки
    const float SweepSeconds = 3.2f;    // за сколько секунд луч проходит экран
    readonly float[] trace = new float[Columns];
    float cursor;                       // позиция луча (в колонках)
    float sinceBeat = 10f;              // секунд после последнего удара
    float beatAmp = 1f;                 // высота текущего комплекса (при плохом здоровье скачет)
    float nextBeatIn = 1f;
    float bpm = 64f;
    float heartPulse;                   // 0..1 — сердечко «вспыхивает» в такт
    float nextBlink = 2f, blinkUntil;
    float jitterClock;
    Vector2 jitter;

    /// <summary>0 — в порядке, 1 — ранен, 2 — тяжело ранен.</summary>
    public static int Mood(float frac) => frac > 0.6f ? 0 : frac > 0.3f ? 1 : 2;

    static float TargetBpm(float frac) => frac > 0.6f ? 66f : frac > 0.3f ? 102f : 142f;

    static Color MoodColor(int mood) =>
        mood == 0 ? new Color(0.3f, 1f, 0.45f) : mood == 1 ? new Color(1f, 0.82f, 0.2f) : new Color(1f, 0.25f, 0.2f);

    static readonly string[] MoodText = { "В порядке", "Ранен", "Тяжело ранен" };

    /// <summary>Форма одного удара (PQRST) через t секунд после его начала; k — сжатие при частом пульсе.</summary>
    static float Wave(float t, float k)
    {
        t /= k;
        if (t < 0.09f) return 0.12f * Mathf.Sin(t / 0.09f * Mathf.PI);              // P
        if (t < 0.14f) return 0f;
        if (t < 0.17f) return -0.12f * (t - 0.14f) / 0.03f;                          // Q
        if (t < 0.19f) return Mathf.Lerp(-0.12f, 1f, (t - 0.17f) / 0.02f);           // R вверх
        if (t < 0.215f) return Mathf.Lerp(1f, -0.3f, (t - 0.19f) / 0.025f);          // R вниз → S
        if (t < 0.25f) return Mathf.Lerp(-0.3f, 0f, (t - 0.215f) / 0.035f);
        if (t < 0.33f) return 0f;
        if (t < 0.5f) return 0.26f * Mathf.Sin((t - 0.33f) / 0.17f * Mathf.PI);      // T
        return 0f;
    }

    /// <summary>Двигает развёртку пульса. Вызывать каждый кадр (и когда инвентарь закрыт — чтобы лента была живой при открытии).</summary>
    public void Tick(float dt, PlayerHealth health)
    {
        bool dead = health == null || health.IsDead;
        float frac = health != null ? Mathf.Clamp01(health.Health / health.maxHealth) : 1f;
        int mood = Mood(frac);
        bpm = Mathf.MoveTowards(bpm, dead ? 0f : TargetBpm(frac), dt * 25f);
        heartPulse = Mathf.MoveTowards(heartPulse, 0f, dt * 4f);

        float colTime = SweepSeconds / Columns;
        float advance = dt / colTime;
        float start = cursor;
        cursor += advance;
        int from = Mathf.FloorToInt(start), to = Mathf.FloorToInt(cursor);
        // Время продвигаем по колонкам, чтобы каждая получила своё значение волны.
        for (int c = from + 1; c <= to; c++)
        {
            sinceBeat += colTime;
            nextBeatIn -= colTime;
            if (!dead && nextBeatIn <= 0f)
            {
                float interval = 60f / Mathf.Max(30f, bpm);
                // При тяжёлом ранении ритм сбивается.
                float irregular = mood == 2 ? Random.Range(-0.18f, 0.12f) : mood == 1 ? Random.Range(-0.05f, 0.05f) : Random.Range(-0.02f, 0.02f);
                nextBeatIn += interval * (1f + irregular);
                sinceBeat = 0f;
                beatAmp = mood == 2 ? Random.Range(0.7f, 1.05f) : 1f;
                heartPulse = 1f;
            }
            float k = Mathf.Clamp(60f / Mathf.Max(30f, bpm) / 0.85f, 0.55f, 1f);
            float v = dead ? 0f : Wave(sinceBeat, k) * beatAmp;
            float noise = (Random.value - 0.5f) * (mood == 2 ? 0.05f : 0.02f);
            trace[((c % Columns) + Columns) % Columns] = v + noise;
        }
        if (cursor >= Columns * 1000f) cursor -= Columns * 1000f;

        // Моргание и дрожь лица.
        nextBlink -= dt;
        if (nextBlink <= 0f)
        {
            blinkUntil = Time.unscaledTime + 0.13f;
            nextBlink = mood == 2 ? Random.Range(0.8f, 2f) : Random.Range(2.5f, 5f);
        }
        jitterClock -= dt;
        if (jitterClock <= 0f)
        {
            jitterClock = 0.08f;
            jitter = mood == 2 && !dead ? new Vector2(Random.Range(-1, 2), Random.Range(-1, 2)) : Vector2.zero;
        }
    }

    /// <summary>Рисует лицо и монитор; anchorBottomCenter — середина нижнего края блока.</summary>
    public void Draw(Vector2 anchorBottomCenter, PlayerHealth health)
    {
        if (heart == null)
        {
            heart = Heart();
            for (int m = 0; m < 3; m++) { faces[m, 0] = Face(m, false); faces[m, 1] = Face(m, true); }
        }
        bool dead = health == null || health.IsDead;
        float frac = health != null ? Mathf.Clamp01(health.Health / health.maxHealth) : 1f;
        int mood = dead ? 2 : Mood(frac);
        Color col = dead ? new Color(0.5f, 0.5f, 0.5f) : MoodColor(mood);

        float faceSize = MenuUI.S(160), gap = MenuUI.S(18), monW = MenuUI.S(420), h = faceSize;
        float totalW = faceSize + gap + monW;
        float x0 = anchorBottomCenter.x - totalW / 2f, y0 = anchorBottomCenter.y - h;
        float border = Mathf.Max(2f, MenuUI.S(3));

        // Лицо в рамке.
        var faceRect = new Rect(x0, y0, faceSize, faceSize);
        MenuUI.Fill(faceRect, new Color(0.05f, 0.05f, 0.06f, 0.92f));
        MenuUI.Frame(faceRect, border, new Color(col.r, col.g, col.b, 0.75f));
        float pix = Mathf.Floor((faceSize - border * 2f) / 32f);
        float faceW = pix * 32f;
        var img = new Rect(faceRect.x + (faceSize - faceW) / 2f + jitter.x * pix * 0.5f,
                           faceRect.y + (faceSize - faceW) / 2f + jitter.y * pix * 0.5f, faceW, faceW);
        bool blink = Time.unscaledTime < blinkUntil || dead;
        GUI.color = dead ? new Color(0.55f, 0.55f, 0.55f) : Color.white;
        GUI.DrawTexture(img, faces[mood, blink ? 1 : 0]);
        GUI.color = Color.white;

        // Монитор.
        var mon = new Rect(x0 + faceSize + gap, y0, monW, h);
        MenuUI.Fill(mon, new Color(0.01f, 0.03f, 0.02f, 0.92f));
        MenuUI.Frame(mon, border, new Color(col.r, col.g, col.b, 0.75f));
        var inner = new Rect(mon.x + border * 3f, mon.y + MenuUI.S(40), mon.width - border * 6f, mon.height - MenuUI.S(50));
        // Сетка.
        var grid = new Color(col.r, col.g, col.b, 0.08f);
        float cell = MenuUI.S(22);
        for (float gx = inner.x; gx <= inner.xMax; gx += cell) MenuUI.Fill(new Rect(gx, inner.y, 1f, inner.height), grid);
        for (float gy = inner.y; gy <= inner.yMax; gy += cell) MenuUI.Fill(new Rect(inner.x, gy, inner.width, 1f), grid);

        // Подписи: сердечко и частота пульса, состояние.
        float hp = 1f + 0.35f * heartPulse;
        float hs = MenuUI.S(24) * hp;
        var heartRect = new Rect(mon.x + MenuUI.S(16) + (MenuUI.S(24) - hs) / 2f, mon.y + MenuUI.S(10) + (MenuUI.S(21) - hs * 6f / 7f) / 2f, hs, hs * 6f / 7f);
        GUI.color = new Color(col.r, col.g, col.b, 0.6f + 0.4f * heartPulse);
        GUI.DrawTexture(heartRect, heart);
        var label = new GUIStyle(MenuUI.Label) { fontSize = Mathf.RoundToInt(MenuUI.S(22)), fontStyle = FontStyle.Bold };
        GUI.color = col;
        GUI.Label(new Rect(mon.x + MenuUI.S(50), mon.y + MenuUI.S(4), MenuUI.S(200), MenuUI.S(34)), Mathf.RoundToInt(bpm) + " уд/мин", label);
        var right = new GUIStyle(label) { alignment = TextAnchor.MiddleRight, fontStyle = FontStyle.Normal };
        GUI.Label(new Rect(mon.x, mon.y + MenuUI.S(4), mon.width - MenuUI.S(16), MenuUI.S(34)), dead ? "Нет пульса" : MoodText[mood], right);

        // Лента: от старых значений к новым; перед лучом — небольшой разрыв, как на настоящем мониторе.
        float colW = inner.width / Columns;
        float mid = inner.y + inner.height * 0.62f, amp = inner.height * 0.55f;
        float thick = Mathf.Max(2f, MenuUI.S(3));
        int head = Mathf.FloorToInt(cursor);
        int headCol = ((head % Columns) + Columns) % Columns;
        float prevY = 0f;
        for (int i = 0; i < Columns; i++)
        {
            int age = ((headCol - i) % Columns + Columns) % Columns; // 0 — только что нарисовано
            if (age > Columns - 10) { prevY = float.NaN; continue; } // разрыв перед лучом
            float y = mid - trace[i] * amp;
            float x = inner.x + i * colW;
            float fade = Mathf.Lerp(1f, 0.25f, age / (float)Columns);
            float top = float.IsNaN(prevY) || i == 0 ? y : Mathf.Min(prevY, y);
            float bottom = float.IsNaN(prevY) || i == 0 ? y : Mathf.Max(prevY, y);
            // Свечение и сама линия.
            GUI.color = new Color(col.r, col.g, col.b, 0.18f * fade);
            GUI.DrawTexture(new Rect(x - thick, top - thick * 1.5f, colW + thick * 2f, bottom - top + thick * 3f), Texture2D.whiteTexture);
            GUI.color = new Color(col.r, col.g, col.b, fade);
            GUI.DrawTexture(new Rect(x, top - thick / 2f, Mathf.Max(colW, 1f) + 0.5f, bottom - top + thick), Texture2D.whiteTexture);
            prevY = y;
        }
        // Яркая точка луча.
        float hy = mid - trace[headCol] * amp;
        float dot = thick * 2.2f;
        GUI.color = new Color(1f, 1f, 1f, 0.9f);
        GUI.DrawTexture(new Rect(inner.x + headCol * colW - dot / 2f, hy - dot / 2f, dot, dot), Texture2D.whiteTexture);
        GUI.color = Color.white;
    }

    public void Dispose()
    {
        if (heart != null) Object.Destroy(heart);
        foreach (var f in faces) if (f != null) Object.Destroy(f);
    }
}
