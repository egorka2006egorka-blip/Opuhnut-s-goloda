using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Продавщица за прилавком столовой: продаёт оружие (оно лежит на прилавке и не берётся, пока не куплено),
/// патроны, еду и аптечки за монеты, которые выпадают из зомби.
/// E на продавщице или на оружии-товаре — открыть магазин (игра на паузе), E или Esc — закрыть.
/// Модель: если у объекта нет своей (дочерней с рендерерами), собирается из примитивов — женщина в форме буфетчицы.
/// Объект ставит CanteenSceneBuilder; цены оружия — в WeaponPickup.price.
/// </summary>
public class Shopkeeper : MonoBehaviour
{
    public static Shopkeeper Instance { get; private set; }
    public static bool IsShopOpen { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { IsShopOpen = false; Instance = null; }

    [System.Serializable]
    public class Offer
    {
        public string itemId;
        public int count = 1;
        public int price = 1;
        public Offer() { }
        public Offer(string itemId, int count, int price) { this.itemId = itemId; this.count = count; this.price = price; }
    }

    public string sellerName = "Галя";
    [Tooltip("Товары, кроме оружия (оружие — WeaponPickup с forSale/price на прилавке).")]
    public List<Offer> offers = new List<Offer>
    {
        new Offer(ItemDatabase.Ammo9, 15, 5),
        new Offer(ItemDatabase.Ammo545, 20, 9),
        new Offer(ItemDatabase.Ammo12, 6, 7),
        new Offer(ItemDatabase.Chocolate, 1, 3),
        new Offer(ItemDatabase.Pie, 1, 5),
        new Offer(ItemDatabase.Shawarma, 1, 9),
        new Offer(ItemDatabase.Medkit, 1, 15),
    };
    [Tooltip("С какого расстояния (м) можно заговорить с продавщицей.")]
    public float talkRange = 4f;

    static readonly string[] Greetings =
    {
        "Чего тебе, милок? Монетки есть — товар будет.",
        "Зомби зомбями, а торговля по расписанию.",
        "Без денег не отпускаю, даже студентам!",
        "Бери пирожок, пока тёплый. Ну, или автомат.",
    };
    static readonly string[] Thanks = { "Пользуйся на здоровье!", "Заходи ещё!", "Хороший выбор.", "Только в столовой не стреляй… хотя ладно." };

    // Части модели для анимации (у своей модели их может не быть).
    Transform head, armL, armR, foreL, foreR, torso;
    Quaternion headRest = Quaternion.identity;
    Transform playerCam;
    Inventory inventory;
    PlayerHealth playerHealth;
    float waveClock = -1f, nodClock = -1f;
    bool greeted, saidHello;
    float poorSaid = -100f;
    float breathe;

    enum Tab { Weapons, Ammo, Food }
    static readonly string[] TabNames = { "Оружие", "Патроны", "Еда и аптечки" };
    Tab tab;
    WeaponPickup focus;
    string line, toast;
    float toastUntil, openTime;
    int openFrame;

    void Awake()
    {
        Instance = this;
        bool hasModel = GetComponentInChildren<Renderer>() != null;
        if (!hasModel) BuildModel(transform);
        head = Find("Head"); armL = Find("ArmL"); armR = Find("ArmR"); foreL = Find("ForeArmL"); foreR = Find("ForeArmR"); torso = Find("Torso");
        if (head != null) headRest = head.localRotation;
        if (GetComponent<Collider>() == null)
        {
            var cap = gameObject.AddComponent<CapsuleCollider>();
            cap.center = new Vector3(0f, 0.85f, 0f);
            cap.height = 1.7f;
            cap.radius = 0.28f;
        }
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
        if (IsShopOpen) SetOpen(false);
    }

    Transform Find(string n)
    {
        foreach (var t in GetComponentsInChildren<Transform>(true)) if (t.name == n) return t;
        return null;
    }

    // ---------- Взаимодействие ----------

    /// <summary>Продавщица, на которую смотрит камера (в пределах talkRange и небольшого угла; стекло витрины не мешает).</summary>
    public static Shopkeeper FindLook(Transform cam, Transform player)
    {
        var s = Instance;
        if (s == null || !s.isActiveAndEnabled) return null;
        Vector3 target = s.transform.position + Vector3.up * 1.25f;
        Vector3 to = target - cam.position;
        float dist = to.magnitude;
        if (dist > s.talkRange || dist < 0.1f) return null;
        float allowed = Mathf.Lerp(28f, 12f, dist / s.talkRange);
        if (Vector3.Angle(cam.forward, to) > allowed) return null;
        // Только со стороны зала (не из кухни через стену) и если между игроком и прилавком ничего нет.
        if (Vector3.Dot(cam.position - s.transform.position, s.transform.forward) < 0.3f) return null;
        foreach (var h in Physics.RaycastAll(cam.position, to / dist, dist, ~0, QueryTriggerInteraction.Ignore))
        {
            if (h.transform.IsChildOf(s.transform) || h.transform.IsChildOf(player)) continue;
            if (h.distance < dist - 1.3f) return null; // ближе прилавка — стена или дверь
        }
        return s;
    }

    /// <summary>Открыть магазин. focus — оружие, на которое смотрел игрок (откроется вкладка оружия).</summary>
    public static void OpenShop(WeaponPickup focus)
    {
        var s = Instance;
        if (s == null || IsShopOpen) return;
        if (!StoryDirector.ShopAllowed)
        {
            DialogueSystem.ShowText("GALYA", "Торговать потом будем. Сначала дело!", 2.5f);
            return;
        }
        s.focus = focus;
        if (focus != null) s.tab = Tab.Weapons;
        s.line = Greetings[Random.Range(0, Greetings.Length)];
        s.toast = null;
        s.SetOpen(true);
        if (!s.saidHello) { s.saidHello = true; s.line = StoryLines.Get("shop_open")?.text ?? s.line; DialogueSystem.Say("shop_open"); }
    }

    public static void CloseShop()
    {
        if (Instance != null) Instance.SetOpen(false);
        else IsShopOpen = false;
    }

    void SetOpen(bool open)
    {
        if (IsShopOpen == open) return;
        IsShopOpen = open;
        Time.timeScale = open ? 0f : 1f;
        AudioListener.pause = open;
        if (open)
        {
            openTime = Time.unscaledTime;
            openFrame = Time.frameCount;
            FirstPersonController.LockCursor(false);
            if (!greeted) { greeted = true; waveClock = 0f; }
        }
        else
        {
            focus = null;
            if (playerHealth == null || !playerHealth.IsDead) FirstPersonController.LockCursor(true);
        }
    }

    void FindPlayer()
    {
        if (playerCam != null && inventory != null) return;
        var fpc = FindAnyObjectByType<FirstPersonController>();
        if (fpc == null) return;
        inventory = fpc.GetComponent<Inventory>();
        playerHealth = fpc.GetComponent<PlayerHealth>();
        var cam = fpc.GetComponentInChildren<Camera>();
        playerCam = cam != null ? cam.transform : fpc.transform;
    }

    void Update()
    {
        FindPlayer();
        if (IsShopOpen)
        {
            if (playerHealth != null && playerHealth.IsDead) { SetOpen(false); return; }
            if (PauseMenu.IsPaused) return;
            var kb = Keyboard.current;
            if (kb != null)
            {
                if (kb.digit1Key.wasPressedThisFrame) tab = Tab.Weapons;
                if (kb.digit2Key.wasPressedThisFrame) tab = Tab.Ammo;
                if (kb.digit3Key.wasPressedThisFrame) tab = Tab.Food;
            }
            if (Time.frameCount != openFrame && Controls.WasPressed(GameAction.Interact))
                SetOpen(false);
            else if (Cursor.lockState == CursorLockMode.Locked) FirstPersonController.LockCursor(false);
        }
        Animate(Time.unscaledDeltaTime);
    }

    // ---------- Покупки ----------

    int Coins => inventory != null ? inventory.Count(ItemDatabase.Coin) : 0;

    bool Pay(int price)
    {
        if (inventory == null || price < 0 || Coins < price) return false;
        if (price > 0) inventory.Take(ItemDatabase.Coin, price);
        return true;
    }

    void Buy(WeaponPickup w)
    {
        if (w == null || !w.forSale) return;
        if (!Pay(w.price)) { Toast("Не хватает монет: нужно " + w.price + "."); Poor(); return; }
        w.forSale = false;
        Thank();
        Toast(w.displayName + " — ваше! Заберите с прилавка [" + Controls.KeyName(GameAction.Interact) + "].");
    }

    void Buy(Offer o)
    {
        var def = ItemDatabase.Get(o.itemId);
        if (def == null) return;
        if (!Pay(o.price)) { Toast("Не хватает монет: нужно " + o.price + "."); Poor(); return; }
        inventory.Add(o.itemId, o.count);
        Thank();
        Toast("Куплено: " + def.name + (o.count > 1 ? " ×" + o.count : "") + ". Лежит в инвентаре.");
    }

    void Poor()
    {
        if (Time.unscaledTime - poorSaid < 20f) return;
        poorSaid = Time.unscaledTime;
        line = StoryLines.Get("shop_poor")?.text ?? line;
        DialogueSystem.Say("shop_poor");
    }

    void Thank()
    {
        line = Thanks[Random.Range(0, Thanks.Length)];
        nodClock = 0f;
    }

    void Toast(string text)
    {
        toast = text;
        toastUntil = Time.unscaledTime + 3f;
    }

    // ---------- Интерфейс ----------

    void OnGUI()
    {
        if (!IsShopOpen || SaveSystem.HideHud || PauseMenu.IsPaused) return;
        GUI.depth = -50;
        float fade = Mathf.Clamp01((Time.unscaledTime - openTime) / 0.15f);
        GUI.color = new Color(1f, 1f, 1f, fade);
        MenuUI.Dim(0.75f);
        float S(float v) => MenuUI.S(v);

        float w = Mathf.Min(Screen.width - S(80), S(1300));
        float x0 = (Screen.width - w) / 2f;
        float y = S(30);

        var title = new GUIStyle(MenuUI.Label) { fontSize = Mathf.RoundToInt(S(44)), fontStyle = FontStyle.Bold };
        MenuUI.ShadowLabel(new Rect(x0, y, w, S(56)), "Буфет  ·  " + sellerName, title);
        var coinStyle = new GUIStyle(MenuUI.Label) { fontSize = Mathf.RoundToInt(S(34)), fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleRight };
        coinStyle.normal.textColor = new Color(1f, 0.82f, 0.3f);
        MenuUI.ShadowLabel(new Rect(x0, y, w, S(56)), "Монеты: " + Coins, coinStyle);
        y += S(60);
        MenuUI.Fill(new Rect(x0, y, w, S(4)), MenuUI.Accent);
        y += S(14);
        var quote = new GUIStyle(MenuUI.Label) { fontStyle = FontStyle.Italic, fontSize = Mathf.RoundToInt(S(24)) };
        MenuUI.ShadowLabel(new Rect(x0, y, w, S(34)), "— " + line, quote);
        y += S(48);

        // Вкладки.
        float tabW = (w - S(28)) / 3f, tabH = S(52);
        for (int i = 0; i < 3; i++)
            if (MenuUI.Tab(new Rect(x0 + i * (tabW + S(14)), y, tabW, tabH), (i + 1) + "  " + TabNames[i], (int)tab == i)) tab = (Tab)i;
        y += tabH + S(18);

        // Список товаров.
        float bottom = Screen.height - S(110);
        var list = new Rect(x0, y, w, bottom - y);
        MenuUI.Fill(list, new Color(0f, 0f, 0f, 0.55f));
        MenuUI.Frame(list, Mathf.Max(1f, S(2)), new Color(MenuUI.Accent.r, MenuUI.Accent.g, MenuUI.Accent.b, 0.5f));
        float rowH = S(92), pad = S(16);
        float ry = list.y + pad;

        if (tab == Tab.Weapons)
        {
            var stock = new List<WeaponPickup>();
            foreach (var wp in WeaponPickup.All) if (wp != null && wp.price > 0) stock.Add(wp);
            stock.Sort((a, b) => a.price.CompareTo(b.price));
            if (stock.Count == 0) GUI.Label(list, "Оружия в продаже нет", MenuUI.LabelCenter);
            foreach (var wp in stock)
            {
                if (ry + rowH > list.yMax) break;
                string desc = WeaponDescription(wp);
                bool sold = !wp.forSale;
                int choice = Row(new Rect(list.x + pad, ry, list.width - pad * 2f, rowH), wp.displayName, desc, wp.price,
                    sold ? "Куплено" : "Купить", !sold && Coins >= wp.price, wp == focus);
                if (choice == 1) Buy(wp);
                ry += rowH + S(10);
            }
        }
        else
        {
            foreach (var o in offers)
            {
                var def = ItemDatabase.Get(o.itemId);
                if (def == null) continue;
                bool ammo = o.itemId == ItemDatabase.Ammo9 || o.itemId == ItemDatabase.Ammo545 || o.itemId == ItemDatabase.Ammo12;
                if ((tab == Tab.Ammo) != ammo) continue;
                if (ry + rowH > list.yMax) break;
                string name = def.name + (o.count > 1 ? "  ×" + o.count : "");
                string desc = def.description + (def.heal > 0f ? "  (+" + Mathf.RoundToInt(def.heal) + " к здоровью)" : "")
                              + (inventory != null ? "   В инвентаре: " + inventory.Count(o.itemId) : "");
                if (Row(new Rect(list.x + pad, ry, list.width - pad * 2f, rowH), name, desc, o.price, "Купить", Coins >= o.price, false) == 1)
                    Buy(o);
                ry += rowH + S(10);
            }
        }

        if (toast != null && Time.unscaledTime < toastUntil)
        {
            var st = new GUIStyle(MenuUI.LabelCenter) { fontStyle = FontStyle.Bold };
            MenuUI.ShadowLabel(new Rect(0, bottom + S(10), Screen.width, S(40)), toast, st);
        }
        var hint = new GUIStyle(MenuUI.Small) { alignment = TextAnchor.MiddleCenter };
        GUI.Label(new Rect(0, Screen.height - S(50), Screen.width, S(36)),
            "1–3 — разделы    [" + Controls.KeyName(GameAction.Interact) + "] или Esc — уйти от прилавка    Монеты выпадают из зомби", hint);
        GUI.color = Color.white;
    }

    /// <summary>Строка товара: название, описание, цена и кнопка. 1 — нажата кнопка.</summary>
    int Row(Rect r, string name, string desc, int price, string button, bool enabled, bool highlight)
    {
        float S(float v) => MenuUI.S(v);
        MenuUI.Fill(r, highlight ? new Color(0.25f, 0.06f, 0.05f, 0.9f) : new Color(0.08f, 0.08f, 0.09f, 0.9f));
        MenuUI.Frame(r, Mathf.Max(1f, S(highlight ? 3 : 1)), highlight ? MenuUI.Accent : new Color(1f, 1f, 1f, 0.15f));
        float bw = S(210), pw = S(170);
        var nameStyle = new GUIStyle(MenuUI.Label) { fontStyle = FontStyle.Bold, fontSize = Mathf.RoundToInt(S(28)) };
        GUI.Label(new Rect(r.x + S(18), r.y + S(6), r.width - bw - pw - S(40), S(40)), name, nameStyle);
        var descStyle = new GUIStyle(MenuUI.Small) { wordWrap = true, alignment = TextAnchor.UpperLeft, fontSize = Mathf.RoundToInt(S(19)) };
        GUI.Label(new Rect(r.x + S(18), r.y + S(44), r.width - bw - pw - S(40), r.height - S(46)), desc, descStyle);
        var priceStyle = new GUIStyle(MenuUI.Label) { fontStyle = FontStyle.Bold, fontSize = Mathf.RoundToInt(S(28)), alignment = TextAnchor.MiddleRight };
        priceStyle.normal.textColor = Coins >= price ? new Color(1f, 0.82f, 0.3f) : new Color(1f, 0.4f, 0.32f);
        GUI.Label(new Rect(r.xMax - bw - pw - S(24), r.y, pw, r.height), price + " мон.", priceStyle);
        var br = new Rect(r.xMax - bw - S(14), r.y + (r.height - S(58)) / 2f, bw, S(58));
        return MenuUI.Button(br, button, enabled, MenuUI.ButtonCenterStyle) ? 1 : 0;
    }

    static string WeaponDescription(WeaponPickup w)
    {
        var fx = w.GetComponent<WeaponEffects>();
        string ammo = ItemDatabase.Get(ItemDatabase.AmmoFor(w))?.name ?? "";
        string mode = w.IsPumpAction ? "помповое" : w.automatic ? "автоматический огонь" : "одиночный огонь";
        string dmg = fx != null ? "урон " + Mathf.RoundToInt(fx.Damage) + (fx.pellets > 1 ? " × " + fx.pellets + " дробин" : "") : "";
        return mode + ", магазин " + w.magazineSize + ", " + dmg + ". Патроны: " + ammo + "."
               + (w.forSale ? "" : "  Лежит на прилавке — подберите.");
    }

    // ---------- Анимация ----------

    void Animate(float dt)
    {
        breathe += dt;
        if (torso != null) torso.localScale = new Vector3(1f, 1f + Mathf.Sin(breathe * 1.7f) * 0.012f, 1f + Mathf.Sin(breathe * 1.7f) * 0.02f);

        // Голова следит за игроком, если он рядом; кивает после покупки.
        if (head != null)
        {
            Quaternion want = headRest;
            if (playerCam != null)
            {
                Vector3 to = playerCam.position - head.position;
                if (to.magnitude < 9f)
                {
                    Vector3 local = transform.InverseTransformDirection(to);
                    float yaw = Mathf.Clamp(Mathf.Atan2(local.x, local.z) * Mathf.Rad2Deg, -65f, 65f);
                    float pitch = Mathf.Clamp(-Mathf.Atan2(local.y, new Vector2(local.x, local.z).magnitude) * Mathf.Rad2Deg, -25f, 25f);
                    want = Quaternion.Euler(pitch, yaw, 0f) * headRest;
                }
                if (!greeted && to.magnitude < 5f) { greeted = true; waveClock = 0f; }
            }
            if (nodClock >= 0f)
            {
                nodClock += dt;
                want = want * Quaternion.Euler(Mathf.Sin(Mathf.Clamp01(nodClock / 0.8f) * Mathf.PI * 2f) * 14f, 0f, 0f);
                if (nodClock > 0.8f) nodClock = -1f;
            }
            want *= Quaternion.Euler(Mathf.Sin(breathe * 0.7f) * 2f, Mathf.Sin(breathe * 0.45f) * 3f, 0f);
            head.localRotation = Quaternion.Slerp(head.localRotation, want, 1f - Mathf.Exp(-dt * 6f));
        }

        // Руки лежат на прилавке; правая машет при первой встрече.
        float wave = 0f;
        if (waveClock >= 0f)
        {
            waveClock += dt;
            wave = Mathf.Clamp01(Mathf.Min(waveClock / 0.3f, (2.2f - waveClock) / 0.3f));
            if (waveClock > 2.2f) waveClock = -1f;
        }
        if (armL != null) armL.localRotation = Quaternion.Euler(-38f, 0f, -6f);
        if (foreL != null) foreL.localRotation = Quaternion.Euler(-48f, 0f, 0f);
        if (armR != null)
            armR.localRotation = Quaternion.Slerp(Quaternion.Euler(-38f, 0f, 6f), Quaternion.Euler(-20f, 0f, 150f), wave);
        if (foreR != null)
            foreR.localRotation = Quaternion.Slerp(Quaternion.Euler(-48f, 0f, 0f), Quaternion.Euler(0f, 0f, Mathf.Sin(waveClock * 12f) * 25f - 20f), wave);
    }

    // ---------- Модель из примитивов ----------

    static readonly Dictionary<Color, Material> mats = new Dictionary<Color, Material>();

    static Material Mat(Color c, float smooth = 0.3f)
    {
        var key = new Color(c.r, c.g, c.b, smooth);
        if (mats.TryGetValue(key, out var m) && m != null) return m;
        m = RuntimeMaterials.Create(c, smooth); // не материал примитива: в сборке он фиолетовый
        mats[key] = m;
        return m;
    }

    static Transform Node(Transform parent, string name, Vector3 pos)
    {
        var t = new GameObject(name).transform;
        t.SetParent(parent, false);
        t.localPosition = pos;
        return t;
    }

    static Transform Part(Transform parent, PrimitiveType type, Vector3 pos, Vector3 size, Material m, Vector3 euler = default)
    {
        var go = GameObject.CreatePrimitive(type);
        var col = go.GetComponent<Collider>();
        if (col != null) DestroyImmediate(col);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = pos;
        go.transform.localRotation = Quaternion.Euler(euler);
        go.transform.localScale = size;
        go.GetComponent<Renderer>().sharedMaterial = m;
        return go.transform;
    }

    /// <summary>Буфетчица ростом ~1,65 м: голубое платье, белый фартук и колпак, волосы в пучок. Лицом к +Z, ноги на y = 0.</summary>
    public static void BuildModel(Transform root)
    {
        var skin = Mat(new Color(0.93f, 0.76f, 0.64f), 0.25f);
        var dress = Mat(new Color(0.32f, 0.5f, 0.72f), 0.3f);
        var white = Mat(new Color(0.95f, 0.95f, 0.93f), 0.2f);
        var hair = Mat(new Color(0.42f, 0.2f, 0.12f), 0.35f);
        var tights = Mat(new Color(0.22f, 0.17f, 0.15f), 0.3f);
        var shoes = Mat(new Color(0.08f, 0.07f, 0.07f), 0.5f);
        var dark = Mat(new Color(0.05f, 0.04f, 0.04f), 0.6f);
        var lips = Mat(new Color(0.75f, 0.15f, 0.18f), 0.5f);
        var cheeks = Mat(new Color(0.95f, 0.6f, 0.58f), 0.25f);

        var model = Node(root, "SellerModel", Vector3.zero);
        foreach (float s in new[] { -1f, 1f })
        {
            Part(model, PrimitiveType.Cylinder, new Vector3(s * 0.08f, 0.32f, 0f), new Vector3(0.11f, 0.3f, 0.11f), tights);
            Part(model, PrimitiveType.Cube, new Vector3(s * 0.08f, 0.035f, 0.035f), new Vector3(0.1f, 0.07f, 0.22f), shoes);
        }
        // Платье: юбка расширяется книзу (два цилиндра), фартук спереди.
        Part(model, PrimitiveType.Cylinder, new Vector3(0f, 0.68f, 0f), new Vector3(0.46f, 0.12f, 0.36f), dress);
        Part(model, PrimitiveType.Cylinder, new Vector3(0f, 0.9f, 0f), new Vector3(0.42f, 0.14f, 0.32f), dress);
        Part(model, PrimitiveType.Cube, new Vector3(0f, 0.8f, 0.17f), new Vector3(0.34f, 0.46f, 0.025f), white, new Vector3(-4f, 0f, 0f));

        var torso = Node(model, "Torso", new Vector3(0f, 1.02f, 0f));
        Part(torso, PrimitiveType.Capsule, new Vector3(0f, 0.2f, 0f), new Vector3(0.4f, 0.27f, 0.3f), dress);
        Part(torso, PrimitiveType.Sphere, new Vector3(0f, 0.27f, 0.08f), new Vector3(0.36f, 0.2f, 0.2f), dress);
        Part(torso, PrimitiveType.Cube, new Vector3(0f, 0.24f, 0.165f), new Vector3(0.22f, 0.22f, 0.02f), white);   // нагрудник фартука
        Part(torso, PrimitiveType.Cube, new Vector3(0.1f, 0.33f, 0.17f), new Vector3(0.06f, 0.025f, 0.01f), Mat(new Color(0.9f, 0.85f, 0.4f), 0.6f)); // бейдж
        Part(torso, PrimitiveType.Cylinder, new Vector3(0f, 0.45f, 0f), new Vector3(0.1f, 0.05f, 0.1f), skin);        // шея

        var head = Node(torso, "Head", new Vector3(0f, 0.48f, 0f));
        Part(head, PrimitiveType.Sphere, new Vector3(0f, 0.11f, 0.01f), new Vector3(0.2f, 0.24f, 0.21f), skin);
        Part(head, PrimitiveType.Sphere, new Vector3(0f, 0.15f, -0.025f), new Vector3(0.22f, 0.21f, 0.21f), hair);
        Part(head, PrimitiveType.Sphere, new Vector3(0f, 0.2f, -0.11f), new Vector3(0.11f, 0.11f, 0.11f), hair);       // пучок
        Part(head, PrimitiveType.Cylinder, new Vector3(0f, 0.245f, -0.01f), new Vector3(0.19f, 0.035f, 0.19f), white, new Vector3(-12f, 0f, 0f)); // колпак
        foreach (float s in new[] { -1f, 1f })
        {
            Part(head, PrimitiveType.Sphere, new Vector3(s * 0.042f, 0.13f, 0.098f), new Vector3(0.026f, 0.018f, 0.012f), dark);
            Part(head, PrimitiveType.Cube, new Vector3(s * 0.042f, 0.155f, 0.1f), new Vector3(0.04f, 0.007f, 0.01f), hair, new Vector3(0f, 0f, s * -8f));
            Part(head, PrimitiveType.Sphere, new Vector3(s * 0.06f, 0.085f, 0.085f), new Vector3(0.04f, 0.03f, 0.02f), cheeks);
        }
        Part(head, PrimitiveType.Sphere, new Vector3(0f, 0.1f, 0.115f), new Vector3(0.03f, 0.04f, 0.03f), skin);       // нос
        Part(head, PrimitiveType.Cube, new Vector3(0f, 0.055f, 0.105f), new Vector3(0.045f, 0.01f, 0.012f), lips);

        foreach (float s in new[] { -1f, 1f })
        {
            string side = s < 0f ? "L" : "R";
            var arm = Node(torso, "Arm" + side, new Vector3(s * 0.21f, 0.37f, 0f));
            Part(arm, PrimitiveType.Capsule, new Vector3(0f, -0.14f, 0f), new Vector3(0.1f, 0.16f, 0.1f), dress);
            var fore = Node(arm, "ForeArm" + side, new Vector3(0f, -0.28f, 0f));
            Part(fore, PrimitiveType.Capsule, new Vector3(0f, -0.12f, 0f), new Vector3(0.085f, 0.14f, 0.085f), skin);
            Part(fore, PrimitiveType.Sphere, new Vector3(0f, -0.27f, 0f), new Vector3(0.075f, 0.09f, 0.05f), skin);
        }
        foreach (var r in model.GetComponentsInChildren<Renderer>()) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
    }
}
