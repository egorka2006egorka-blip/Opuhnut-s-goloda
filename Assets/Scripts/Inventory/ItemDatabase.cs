using System.Collections.Generic;
using UnityEngine;

/// <summary>Раздел инвентаря, в котором показывается предмет.</summary>
public enum ItemCategory { Gear, Important }

/// <summary>Описание вида предмета: название, описание, раздел, 3D-модель (для инвентаря и для предмета на полу).</summary>
public class ItemDef
{
    public string id;
    public string name;
    public string description;
    public ItemCategory category;
    /// <summary>Единица счёта для подписи количества («шт.», «патр.»). Пусто — количество не показывается, если предмет один.</summary>
    public string unit = "шт.";
    /// <summary>Сколько здоровья восстанавливает при использовании (0 — предмет не используется).</summary>
    public float heal;
    /// <summary>Строит модель предмета из примитивов (без коллайдеров). Размеры — в метрах.</summary>
    public System.Action<Transform> build;
}

/// <summary>
/// Все виды предметов игры. Модели собираются из примитивов в коде — ассеты не нужны.
/// Новый предмет: добавить ItemDef в Defs (раздел Gear — «Снаряжение», Important — «Важное»).
/// </summary>
public static class ItemDatabase
{
    public const string Ammo9 = "ammo_9mm";
    public const string Ammo545 = "ammo_545";
    public const string Ammo12 = "ammo_12g";
    public const string Medkit = "medkit";
    public const string StudentCard = "student_card";

    static Dictionary<string, ItemDef> defs;
    static readonly List<ItemDef> ordered = new List<ItemDef>();

    static void Ensure()
    {
        if (defs != null) return;
        defs = new Dictionary<string, ItemDef>();
        Add(new ItemDef
        {
            id = Ammo9, name = "Патроны 9 мм", unit = "патр.", category = ItemCategory.Gear,
            description = "Пистолетные патроны 9×19. Подходят к пистолету, MP5 и Uzi.",
            build = t => BuildPistolAmmo(t),
        });
        Add(new ItemDef
        {
            id = Ammo545, name = "Патроны 5,45 мм", unit = "патр.", category = ItemCategory.Gear,
            description = "Автоматные патроны 5,45×39. Подходят к автомату.",
            build = t => BuildRifleAmmo(t),
        });
        Add(new ItemDef
        {
            id = Ammo12, name = "Патроны 12 калибра", unit = "патр.", category = ItemCategory.Gear,
            description = "Охотничьи патроны с картечью. Подходят к дробовику.",
            build = t => BuildShells(t),
        });
        Add(new ItemDef
        {
            id = Medkit, name = "Аптечка", category = ItemCategory.Gear, heal = 50f,
            description = "Бинты, антисептик и обезболивающее. Восстанавливает половину здоровья.",
            build = t => BuildMedkit(t),
        });
        Add(new ItemDef
        {
            id = StudentCard, name = "Студенческий билет", unit = "", category = ItemCategory.Important,
            description = "Твой студенческий. Без него не пустят ни в библиотеку, ни обратно в общагу.",
            build = t => BuildStudentCard(t),
        });
    }

    static void Add(ItemDef d) { defs[d.id] = d; ordered.Add(d); }

    public static ItemDef Get(string id)
    {
        Ensure();
        return id != null && defs.TryGetValue(id, out var d) ? d : null;
    }

    /// <summary>Порядок предметов в базе (по нему сортируется инвентарь).</summary>
    public static int Order(string id)
    {
        Ensure();
        var d = Get(id);
        return d != null ? ordered.IndexOf(d) : int.MaxValue;
    }

    /// <summary>Какие патроны нужны оружию (по полю ammoType, а если оно пустое — по типу и названию).</summary>
    public static string AmmoFor(WeaponPickup w)
    {
        if (w == null) return null;
        if (!string.IsNullOrEmpty(w.ammoType)) return w.ammoType;
        if (w.IsPumpAction) return Ammo12;
        string n = (w.displayName ?? "").ToLowerInvariant();
        if (n.Contains("автомат") || n.StartsWith("ак") || n.Contains("ak-") || n.Contains("rifle")) return Ammo545;
        return Ammo9;
    }

    /// <summary>Создаёт модель предмета: корень с деталями без коллайдеров.</summary>
    public static GameObject BuildModel(string id)
    {
        var def = Get(id);
        var root = new GameObject(def != null ? def.name : id);
        def?.build?.Invoke(root.transform);
        return root;
    }

    // ---------- Модели ----------

    static readonly Dictionary<Color, Material> materials = new Dictionary<Color, Material>();
    static Material baseMaterial;

    static Material Mat(Color c, float smooth = 0.35f, float metal = 0f)
    {
        var key = new Color(c.r, c.g, c.b, smooth + metal * 10f); // ключ учитывает и гладкость/металл
        if (materials.TryGetValue(key, out var m) && m != null) return m;
        if (baseMaterial == null)
        {
            var tmp = GameObject.CreatePrimitive(PrimitiveType.Cube);
            baseMaterial = tmp.GetComponent<Renderer>().sharedMaterial;
            Object.Destroy(tmp);
        }
        m = new Material(baseMaterial) { hideFlags = HideFlags.DontSave };
        m.color = c;
        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
        if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", smooth);
        if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", metal);
        materials[key] = m;
        return m;
    }

    static Transform Part(Transform parent, PrimitiveType type, Vector3 pos, Vector3 size, Material m, Vector3 euler = default)
    {
        var go = GameObject.CreatePrimitive(type);
        var col = go.GetComponent<Collider>();
        if (col != null) { col.enabled = false; Object.Destroy(col); }
        go.transform.SetParent(parent, false);
        go.transform.localPosition = pos;
        go.transform.localRotation = Quaternion.Euler(euler);
        go.transform.localScale = size;
        go.GetComponent<Renderer>().sharedMaterial = m;
        return go.transform;
    }

    static Transform Box(Transform p, Vector3 pos, Vector3 size, Material m, Vector3 euler = default) => Part(p, PrimitiveType.Cube, pos, size, m, euler);

    /// <summary>Цилиндр диаметром d и длиной len вдоль локальной оси Y (после поворота euler).</summary>
    static Transform Cyl(Transform p, Vector3 pos, float d, float len, Material m, Vector3 euler = default) =>
        Part(p, PrimitiveType.Cylinder, pos, new Vector3(d, len / 2f, d), m, euler);

    static readonly Color Brass = new Color(0.78f, 0.6f, 0.25f);
    static readonly Color Copper = new Color(0.72f, 0.4f, 0.22f);
    static readonly Color Steel = new Color(0.55f, 0.55f, 0.52f);

    /// <summary>Патрон: гильза + пуля. Ось — локальная Y родителя (стоя) или повернуто euler.</summary>
    static void Cartridge(Transform p, Vector3 pos, Vector3 euler, float d, float caseLen, float bulletLen, Color caseColor, Color tip, bool bottleneck)
    {
        var c = new GameObject("Cartridge").transform;
        c.SetParent(p, false);
        c.localPosition = pos;
        c.localRotation = Quaternion.Euler(euler);
        var brass = Mat(caseColor, 0.7f, 0.8f);
        Cyl(c, new Vector3(0f, caseLen / 2f, 0f), d, caseLen, brass);
        float y = caseLen;
        if (bottleneck)
        {
            Cyl(c, new Vector3(0f, y + d * 0.35f, 0f), d * 0.7f, d * 0.7f, brass);
            y += d * 0.7f;
            d *= 0.62f;
        }
        var b = Mat(tip, 0.6f, 0.7f);
        Cyl(c, new Vector3(0f, y + bulletLen * 0.3f, 0f), d * 0.98f, bulletLen * 0.6f, b);
        Part(c, PrimitiveType.Sphere, new Vector3(0f, y + bulletLen * 0.6f, 0f), new Vector3(d * 0.98f, bulletLen * 0.8f, d * 0.98f), b);
    }

    static void BuildPistolAmmo(Transform t)
    {
        // Картонная пачка с красной полосой и три патрона рядом.
        var card = Mat(new Color(0.78f, 0.66f, 0.38f), 0.15f);
        Box(t, new Vector3(0f, 0.02f, 0f), new Vector3(0.11f, 0.04f, 0.075f), card);
        Box(t, new Vector3(0f, 0.0405f, 0f), new Vector3(0.112f, 0.002f, 0.025f), Mat(new Color(0.7f, 0.12f, 0.1f), 0.2f));
        Box(t, new Vector3(0f, 0.02f, 0.0378f), new Vector3(0.06f, 0.016f, 0.001f), Mat(new Color(0.15f, 0.12f, 0.1f), 0.2f));
        for (int i = 0; i < 3; i++)
            Cartridge(t, new Vector3(-0.03f + i * 0.016f, 0.04f, 0.0f), new Vector3(0f, 0f, 0f), 0.0098f, 0.019f, 0.012f, Brass, Copper, false);
        Cartridge(t, new Vector3(0.07f, 0.005f, 0.045f), new Vector3(90f, 0f, 30f), 0.0098f, 0.019f, 0.012f, Brass, Copper, false);
    }

    static void BuildRifleAmmo(Transform t)
    {
        // Зелёная металлическая коробка и патроны на крышке.
        var green = Mat(new Color(0.27f, 0.33f, 0.2f), 0.35f, 0.3f);
        Box(t, new Vector3(0f, 0.035f, 0f), new Vector3(0.13f, 0.07f, 0.06f), green);
        Box(t, new Vector3(0f, 0.072f, 0f), new Vector3(0.132f, 0.006f, 0.062f), Mat(new Color(0.22f, 0.27f, 0.16f), 0.35f, 0.3f));
        Box(t, new Vector3(0f, 0.082f, 0f), new Vector3(0.05f, 0.012f, 0.012f), Mat(Steel, 0.5f, 0.8f)); // ручка
        Box(t, new Vector3(0f, 0.04f, 0.0305f), new Vector3(0.07f, 0.018f, 0.001f), Mat(new Color(0.85f, 0.8f, 0.6f), 0.2f));
        var steelCase = new Color(0.45f, 0.4f, 0.3f); // лакированная стальная гильза
        for (int i = 0; i < 4; i++)
            Cartridge(t, new Vector3(-0.045f + i * 0.026f, 0.0755f + 0.005f, -0.02f), new Vector3(90f, 0f, 0f), 0.01f, 0.039f, 0.02f, steelCase, Copper, true);
    }

    static void BuildShells(Transform t)
    {
        // Ружейные патроны: красная пластиковая гильза + латунное донце. Четыре стоят, два лежат.
        var red = Mat(new Color(0.72f, 0.1f, 0.08f), 0.45f);
        var brass = Mat(Brass, 0.7f, 0.8f);
        void Shell(Vector3 pos, Vector3 euler)
        {
            var s = new GameObject("Shell").transform;
            s.SetParent(t, false);
            s.localPosition = pos;
            s.localRotation = Quaternion.Euler(euler);
            Cyl(s, new Vector3(0f, 0.008f, 0f), 0.0215f, 0.016f, brass);
            Cyl(s, new Vector3(0f, 0.0015f, 0f), 0.0235f, 0.003f, brass);
            Cyl(s, new Vector3(0f, 0.016f + 0.025f, 0f), 0.0205f, 0.05f, red);
            Cyl(s, new Vector3(0f, 0.0665f, 0f), 0.017f, 0.001f, Mat(new Color(0.5f, 0.06f, 0.05f), 0.3f)); // завальцовка
        }
        Shell(new Vector3(-0.012f, 0f, -0.012f), Vector3.zero);
        Shell(new Vector3(0.012f, 0f, -0.012f), Vector3.zero);
        Shell(new Vector3(-0.012f, 0f, 0.012f), Vector3.zero);
        Shell(new Vector3(0.012f, 0f, 0.012f), Vector3.zero);
        Shell(new Vector3(0.06f, 0.012f, 0.0f), new Vector3(0f, 25f, 90f));
        Shell(new Vector3(-0.045f, 0.012f, 0.04f), new Vector3(0f, -60f, -90f));
    }

    static void BuildMedkit(Transform t)
    {
        var white = Mat(new Color(0.9f, 0.9f, 0.87f), 0.5f);
        var red = Mat(new Color(0.8f, 0.08f, 0.07f), 0.4f);
        var dark = Mat(new Color(0.2f, 0.2f, 0.22f), 0.4f);
        Box(t, new Vector3(0f, 0.065f, 0f), new Vector3(0.2f, 0.13f, 0.07f), white);
        Box(t, new Vector3(0f, 0.065f, 0f), new Vector3(0.202f, 0.006f, 0.072f), dark); // шов крышки
        // Красный крест спереди и сзади.
        foreach (float z in new[] { 0.0355f, -0.0355f })
        {
            Box(t, new Vector3(0f, 0.065f, z), new Vector3(0.025f, 0.08f, 0.002f), red);
            Box(t, new Vector3(0f, 0.065f, z), new Vector3(0.08f, 0.025f, 0.002f), red);
        }
        // Ручка сверху.
        Box(t, new Vector3(-0.035f, 0.14f, 0f), new Vector3(0.01f, 0.02f, 0.014f), dark);
        Box(t, new Vector3(0.035f, 0.14f, 0f), new Vector3(0.01f, 0.02f, 0.014f), dark);
        Box(t, new Vector3(0f, 0.152f, 0f), new Vector3(0.08f, 0.008f, 0.016f), dark);
        // Защёлки.
        Box(t, new Vector3(-0.07f, 0.065f, 0.037f), new Vector3(0.016f, 0.024f, 0.004f), Mat(Steel, 0.6f, 0.8f));
        Box(t, new Vector3(0.07f, 0.065f, 0.037f), new Vector3(0.016f, 0.024f, 0.004f), Mat(Steel, 0.6f, 0.8f));
    }

    static void BuildStudentCard(Transform t)
    {
        // Синяя книжечка с золотым тиснением, чуть приоткрыта.
        var blue = Mat(new Color(0.1f, 0.2f, 0.45f), 0.3f);
        var gold = Mat(new Color(0.85f, 0.7f, 0.3f), 0.7f, 0.9f);
        var paper = Mat(new Color(0.92f, 0.9f, 0.84f), 0.1f);
        Box(t, new Vector3(0f, 0.002f, 0f), new Vector3(0.1f, 0.004f, 0.07f), blue);          // нижняя обложка
        Box(t, new Vector3(0f, 0.0055f, 0f), new Vector3(0.096f, 0.003f, 0.066f), paper);      // страницы
        var cover = new GameObject("Cover").transform;
        cover.SetParent(t, false);
        cover.localPosition = new Vector3(-0.05f, 0.008f, 0f);
        cover.localRotation = Quaternion.Euler(0f, 0f, 12f);
        Box(cover, new Vector3(0.05f, 0.001f, 0f), new Vector3(0.1f, 0.004f, 0.07f), blue);
        Box(cover, new Vector3(0.05f, 0.0032f, 0.008f), new Vector3(0.022f, 0.0006f, 0.022f), gold);   // герб
        Box(cover, new Vector3(0.05f, 0.0032f, -0.018f), new Vector3(0.06f, 0.0006f, 0.006f), gold);   // надпись
        Box(cover, new Vector3(0.05f, 0.0032f, -0.027f), new Vector3(0.04f, 0.0006f, 0.004f), gold);
    }
}
