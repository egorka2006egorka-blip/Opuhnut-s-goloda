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
    public const string Coin = "coin";
    public const string Crowbar = "crowbar";
    public const string Radio = "radio";
    public const string Chocolate = "food_chocolate";
    public const string Pie = "food_pie";
    public const string Shawarma = "food_shawarma";

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
            id = Chocolate, name = "Шоколадка", category = ItemCategory.Gear, heal = 10f,
            description = "Плитка молочного шоколада из буфета. Быстро поднимает настроение и немного — здоровье.",
            build = t => BuildChocolate(t),
        });
        Add(new ItemDef
        {
            id = Pie, name = "Пирожок с картошкой", category = ItemCategory.Gear, heal = 20f,
            description = "Ещё тёплый, из-под прилавка. Восстанавливает немного здоровья.",
            build = t => BuildPie(t),
        });
        Add(new ItemDef
        {
            id = Shawarma, name = "Шаурма", category = ItemCategory.Gear, heal = 40f,
            description = "Большая, сытная, завёрнута в фольгу. Восстанавливает много здоровья.",
            build = t => BuildShawarma(t),
        });
        Add(new ItemDef
        {
            id = Coin, name = "Монеты", unit = "шт.", category = ItemCategory.Important,
            description = "Выпадают из зомби. Продавщица в столовой берёт их в обмен на оружие, патроны и еду.",
            build = t => BuildCoins(t),
        });
        Add(new ItemDef
        {
            id = Crowbar, name = "Лом", unit = "", category = ItemCategory.Important,
            description = "Тяжёлый лом Декана. Им можно сорвать цепь с двери в подвале, за которой заперся Шапик.",
            build = t => BuildCrowbar(t),
        });
        Add(new ItemDef
        {
            id = Radio, name = "Рация", unit = "", category = ItemCategory.Important,
            description = "Связь с Шапиком. Ловит плохо, особенно когда Шапик не хочет отвечать на вопросы.",
            build = t => BuildRadio(t),
        });
        Add(new ItemDef
        {
            id = StudentCard, name = "Студенческий билет", unit = "", category = ItemCategory.Important,
            description = "Твой студенческий. Без него не пустят ни в библиотеку, ни обратно в общагу.",
            build = t => BuildStudentCard(t),
        });

        // Пасхалка: карточки из игры «Покажи, как…», разложены по столовой (CanteenCards).
        const string set = "\n\nОдна из пяти карточек, разбросанных по столовой.";
        AddCard(CardOwl, "owl", "Сова на скакалке",
            "Карточка из игры «Покажи, как…»: изобрази сову, которая прыгает через скакалку. " +
            "Судя по взгляду, эта сова не пропустила ни одной пары физкультуры." + set);
        AddCard(CardDog, "dog", "Собака-гольфист",
            "Карточка из игры «Покажи, как…»: изобрази собаку, которая играет в гольф. " +
            "Кепка, клюшка и твёрдая уверенность, что мячик потом можно догнать и принести." + set);
        AddCard(CardLion, "lion", "Лев-футболист",
            "Карточка из игры «Покажи, как…»: изобрази льва, который играет в футбол. " +
            "В лапе мяч для регби, под ногой футбольный — правила лев придумывает на ходу." + set);
        AddCard(CardSkunk, "skunk", "Скунс-гонщик",
            "Карточка из игры «Покажи, как…»: изобрази скунса за рулём гоночного болида. " +
            "Облако за машиной — это не выхлоп. Обгонять не советуем." + set);
        AddCard(CardTiger, "tiger", "Тигр-фокусник",
            "Карточка из игры «Покажи, как…»: изобрази тигра-фокусника. " +
            "Цилиндр, бабочка, карты веером — загадай любую, всё равно выпадет туз." + set);
    }

    public const string CardOwl = "card_owl", CardDog = "card_dog", CardLion = "card_lion", CardSkunk = "card_skunk", CardTiger = "card_tiger";
    /// <summary>Все карточки-пасхалки по порядку.</summary>
    public static readonly string[] Cards = { CardOwl, CardDog, CardLion, CardSkunk, CardTiger };

    static void AddCard(string id, string texture, string name, string description) => Add(new ItemDef
    {
        id = id, name = name, unit = "", category = ItemCategory.Important, description = description,
        build = t => BuildCard(t, texture),
    });

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

    // ---------- Оружие в инвентаре ----------

    const string WeaponPrefix = "weapon:";
    static readonly Dictionary<string, WeaponPickup> weaponOwners = new Dictionary<string, WeaponPickup>();

    /// <summary>Предмет-оружие (раздел «Снаряжение»): само оружие — WeaponFor(id).</summary>
    public static bool IsWeapon(string id) => id != null && id.StartsWith(WeaponPrefix);

    public static WeaponPickup WeaponFor(string id) => id != null && weaponOwners.TryGetValue(id, out var w) && w != null ? w : null;

    /// <summary>Заводит вид предмета для оружия: название, раздел «Снаряжение» и модель для иконки — копия его мешей.</summary>
    public static string RegisterWeapon(WeaponPickup w)
    {
        Ensure();
        string baseId = WeaponPrefix + w.displayName, id = baseId;
        for (int n = 2; weaponOwners.TryGetValue(id, out var other) && other != null && other != w; n++) id = baseId + "#" + n;
        weaponOwners[id] = w;

        // Снимок мешей относительно корня оружия (видимых сейчас — без спрятанного патрона в руке и т.п.).
        var parts = new List<(Mesh mesh, Material[] mats, Matrix4x4 m)>();
        Matrix4x4 toRoot = w.transform.worldToLocalMatrix;
        foreach (var r in w.GetComponentsInChildren<Renderer>())
        {
            if (!r.enabled) continue;
            Mesh mesh = r is SkinnedMeshRenderer smr ? smr.sharedMesh : r is MeshRenderer ? r.GetComponent<MeshFilter>()?.sharedMesh : null;
            if (mesh == null) continue;
            parts.Add((mesh, r.sharedMaterials, toRoot * r.transform.localToWorldMatrix));
        }

        if (!defs.TryGetValue(id, out var def))
        {
            def = new ItemDef { id = id };
            Add(def);
        }
        def.name = w.displayName;
        def.category = ItemCategory.Gear;
        def.unit = "";
        def.description = w.melee ? "Оружие ближнего боя." : "Огнестрельное оружие.";
        def.build = t =>
        {
            foreach (var p in parts)
            {
                var go = new GameObject("Part");
                go.transform.SetParent(t, false);
                go.transform.localPosition = p.m.GetColumn(3);
                go.transform.localRotation = p.m.rotation;
                go.transform.localScale = p.m.lossyScale;
                go.AddComponent<MeshFilter>().sharedMesh = p.mesh;
                go.AddComponent<MeshRenderer>().sharedMaterials = p.mats;
            }
        };
        return id;
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

    static Material Mat(Color c, float smooth = 0.35f, float metal = 0f)
    {
        var key = new Color(c.r, c.g, c.b, smooth + metal * 10f); // ключ учитывает и гладкость/металл
        if (materials.TryGetValue(key, out var m) && m != null) return m;
        m = RuntimeMaterials.Create(c, smooth, metal); // не материал примитива: в сборке он фиолетовый
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

    static void BuildCoins(Transform t)
    {
        // Небольшая стопка золотых монет и пара монет рядом.
        var gold = Mat(new Color(0.95f, 0.72f, 0.2f), 0.75f, 0.95f);
        var rim = Mat(new Color(0.8f, 0.55f, 0.12f), 0.7f, 0.95f);
        for (int i = 0; i < 4; i++)
        {
            Cyl(t, new Vector3(0.003f * (i % 2), 0.0015f + i * 0.0032f, 0.002f * (i % 3)), 0.026f, 0.003f, i % 2 == 0 ? gold : rim);
        }
        Cyl(t, new Vector3(0.032f, 0.0015f, 0.012f), 0.026f, 0.003f, gold);
        Cyl(t, new Vector3(-0.026f, 0.006f, -0.02f), 0.026f, 0.003f, gold, new Vector3(0f, 0f, 20f));
    }

    static void BuildChocolate(Transform t)
    {
        // Плитка в бумажной обёртке с фольгой на надорванном краю.
        var wrap = Mat(new Color(0.75f, 0.12f, 0.1f), 0.3f);
        var foil = Mat(new Color(0.85f, 0.85f, 0.88f), 0.85f, 0.9f);
        var choc = Mat(new Color(0.3f, 0.16f, 0.08f), 0.4f);
        Box(t, new Vector3(-0.012f, 0.006f, 0f), new Vector3(0.13f, 0.012f, 0.07f), wrap);
        Box(t, new Vector3(0.066f, 0.006f, 0f), new Vector3(0.026f, 0.011f, 0.068f), foil);
        Box(t, new Vector3(0.084f, 0.006f, 0f), new Vector3(0.012f, 0.009f, 0.066f), choc);
        Box(t, new Vector3(-0.012f, 0.0125f, 0f), new Vector3(0.06f, 0.001f, 0.03f), Mat(new Color(0.95f, 0.88f, 0.6f), 0.2f));
    }

    static void BuildPie(Transform t)
    {
        // Румяный пирожок-«лодочка» на бумажной салфетке.
        var crust = Mat(new Color(0.78f, 0.5f, 0.22f), 0.45f);
        var napkin = Mat(new Color(0.92f, 0.92f, 0.9f), 0.1f);
        Box(t, new Vector3(0f, 0.001f, 0f), new Vector3(0.15f, 0.002f, 0.11f), napkin, new Vector3(0f, 12f, 0f));
        Part(t, PrimitiveType.Sphere, new Vector3(0f, 0.022f, 0f), new Vector3(0.13f, 0.045f, 0.065f), crust);
        Part(t, PrimitiveType.Sphere, new Vector3(0f, 0.04f, 0f), new Vector3(0.1f, 0.012f, 0.02f), Mat(new Color(0.68f, 0.4f, 0.16f), 0.5f));
    }

    static void BuildShawarma(Transform t)
    {
        // Рулет в лаваше, наполовину завёрнут в фольгу.
        var lavash = Mat(new Color(0.88f, 0.76f, 0.52f), 0.25f);
        var foil = Mat(new Color(0.82f, 0.83f, 0.86f), 0.8f, 0.9f);
        var filling = Mat(new Color(0.55f, 0.3f, 0.15f), 0.35f);
        Cyl(t, new Vector3(0.02f, 0.028f, 0f), 0.056f, 0.1f, foil, new Vector3(0f, 0f, 90f));
        Cyl(t, new Vector3(-0.05f, 0.028f, 0f), 0.054f, 0.06f, lavash, new Vector3(0f, 0f, 90f));
        Cyl(t, new Vector3(-0.08f, 0.03f, 0f), 0.04f, 0.004f, filling, new Vector3(0f, 0f, 90f));
        Part(t, PrimitiveType.Sphere, new Vector3(-0.082f, 0.04f, 0.008f), new Vector3(0.01f, 0.014f, 0.014f), Mat(new Color(0.3f, 0.6f, 0.2f), 0.4f));
    }

    static void BuildCrowbar(Transform t)
    {
        // Красный стальной лом: прямой стержень, загнутый конец с раздвоенным жалом.
        var red = Mat(new Color(0.7f, 0.1f, 0.08f), 0.5f, 0.6f);
        var steel = Mat(Steel, 0.6f, 0.9f);
        Cyl(t, new Vector3(0f, 0.012f, 0f), 0.022f, 0.62f, red, new Vector3(0f, 0f, 90f));
        Cyl(t, new Vector3(0.33f, 0.04f, 0f), 0.022f, 0.09f, red, new Vector3(0f, 0f, 30f));
        Box(t, new Vector3(0.36f, 0.08f, 0f), new Vector3(0.03f, 0.04f, 0.02f), steel, new Vector3(0f, 0f, 60f));
        Box(t, new Vector3(-0.32f, 0.012f, 0f), new Vector3(0.05f, 0.012f, 0.03f), steel, new Vector3(0f, 0f, -10f));
    }

    static void BuildRadio(Transform t)
    {
        var body = Mat(new Color(0.12f, 0.12f, 0.13f), 0.4f);
        Box(t, new Vector3(0f, 0.08f, 0f), new Vector3(0.065f, 0.16f, 0.04f), body);
        Cyl(t, new Vector3(0.018f, 0.2f, 0f), 0.012f, 0.1f, body);
        Box(t, new Vector3(0f, 0.11f, 0.021f), new Vector3(0.045f, 0.05f, 0.002f), Mat(new Color(0.3f, 0.3f, 0.3f), 0.2f));
        Part(t, PrimitiveType.Sphere, new Vector3(-0.02f, 0.155f, 0.021f), Vector3.one * 0.008f, Mat(new Color(0.2f, 1f, 0.2f), 0.5f));
    }

    static readonly Dictionary<string, Material> cardFaces = new Dictionary<string, Material>();

    /// <summary>
    /// Карточка 63×88 мм (как игральная) лицом вверх: картинка — текстура Resources/Cards/&lt;texture&gt;,
    /// края — белый картон, рубашка — фиолетовая.
    /// </summary>
    static void BuildCard(Transform t, string texture)
    {
        if (!cardFaces.TryGetValue(texture, out var face) || face == null)
        {
            var tex = Resources.Load<Texture2D>("Cards/" + texture);
            // Матовая печать: чуть серее белого, без бликов и отражений неба — иначе под светом инвентаря картинка «светится».
            face = RuntimeMaterials.Create(new Color(0.82f, 0.82f, 0.82f), 0f);
            if (face.HasProperty("_SpecularHighlights")) { face.SetFloat("_SpecularHighlights", 0f); face.EnableKeyword("_SPECULARHIGHLIGHTS_OFF"); }
            if (face.HasProperty("_EnvironmentReflections")) { face.SetFloat("_EnvironmentReflections", 0f); face.EnableKeyword("_ENVIRONMENTREFLECTIONS_OFF"); }
            if (tex != null)
            {
                face.mainTexture = tex;
                if (face.HasProperty("_BaseMap")) face.SetTexture("_BaseMap", tex);
            }
            else Debug.LogWarning("ItemDatabase: нет текстуры карточки Resources/Cards/" + texture);
            cardFaces[texture] = face;
        }
        const float w = 0.063f, h = 0.088f, thick = 0.0012f;
        Box(t, new Vector3(0f, thick / 2f, 0f), new Vector3(w, thick, h), Mat(new Color(0.95f, 0.95f, 0.93f), 0.2f));
        // Quad смотрит в −Z; поворот на 90° по X — лицом вверх, верх картинки — к +Z.
        Part(t, PrimitiveType.Quad, new Vector3(0f, thick + 0.0002f, 0f), new Vector3(w - 0.004f, h - 0.004f, 1f), face, new Vector3(90f, 0f, 0f));
        Part(t, PrimitiveType.Quad, new Vector3(0f, -0.0002f, 0f), new Vector3(w - 0.004f, h - 0.004f, 1f), Mat(new Color(0.35f, 0.2f, 0.6f), 0.3f), new Vector3(-90f, 0f, 0f));
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
