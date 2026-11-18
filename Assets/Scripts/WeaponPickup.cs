using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Оружие, которое можно подобрать (клавиша E).
/// gripRight / gripLeft — точки хвата: позиция = центр ладони,
/// forward = направление пальцев, up = нормаль ладони (куда «смотрит» ладонь).
/// Ось +Z объекта — направление ствола.
/// </summary>
public class WeaponPickup : MonoBehaviour
{
    public static readonly List<WeaponPickup> All = new List<WeaponPickup>();

    public string displayName = "Пистолет";
    [Header("Ближний бой")]
    [Tooltip("Оружие ближнего боя (лом): без патронов и прицеливания, ЛКМ — удар.")]
    public bool melee;
    public float meleeDamage = 45f;
    public float meleeRange = 2.1f;

    [Header("Магазин")]
    [Tooltip("Оружие лежит на прилавке как товар: взять его нельзя, пока не куплено у продавщицы (Shopkeeper).")]
    public bool forSale;
    [Tooltip("Цена в монетах.")]
    public int price;
    public Transform gripRight;
    public Transform gripLeft;
    [Tooltip("Держится одной правой рукой. Левая рука берётся за gripLeft только при перезарядке, в остальное время свободна.")]
    public bool oneHanded;
    [Tooltip("Затвор — откатывается при выстреле (необязательно).")]
    public Transform slide;
    [Tooltip("Часть оружия, по верху которой наводится прицел (если не задано — затвор или всё оружие).")]
    public Transform aimSource;
    [Tooltip("Точка прицеливания (верх целика): при ПКМ она встаёт на линию взгляда. Приоритетнее aimSource.")]
    public Transform aimPoint;
    [Tooltip("Где левая рука берётся за рукоять затвора (положение в покое; при взведении рука идёт назад вместе с затвором). Необязательно.")]
    public Transform rackGrab;
    [Tooltip("Куда торчит рукоять затвора (в осях оружия). Ноль — вбок, по знаку X точки rackGrab.")]
    public Vector3 rackGrabSide;
    [Tooltip("Неподвижная при стрельбе рукоять взведения (отдельная деталь): ходит назад только при передёргивании. Необязательно.")]
    public Transform rackHandle;
    [Tooltip("Ход рукояти взведения при передёргивании (м).")]
    public float rackHandleTravel = 0.06f;
    [Tooltip("Наклон оружия при взведении затвора вокруг оси ствола (градусы). Положительный — верх влево, отрицательный — верх вправо.")]
    public float rackRoll = 45f;
    [Tooltip("Длительность передёргивания затвора (с); 0 — по умолчанию из PlayerWeaponHandler.")]
    public float rackDuration = 0f;
    [Tooltip("Смещение оружия относительно камеры, когда оно в руках.")]
    public Vector3 holdOffset = new Vector3(0.08f, -0.15f, 0.36f);

    [Tooltip("Ёмкость магазина.")]
    public int magazineSize = 12;
    [Tooltip("Какие патроны из инвентаря берутся при перезарядке (ammo_9mm, ammo_545, ammo_12g). Пусто — определяется по типу и названию оружия.")]
    public string ammoType = "";
    [Tooltip("Автоматический огонь: стреляет, пока зажата ЛКМ. Иначе — один выстрел на нажатие.")]
    public bool automatic;
    [Tooltip("Интервал между выстрелами (с); 0 — по умолчанию из PlayerWeaponHandler.")]
    public float fireInterval = 0f;
    [Tooltip("Множитель отдачи (подброса взгляда) за выстрел.")]
    public float recoilScale = 1f;
    [Tooltip("Длительность смены магазина (с); 0 — по умолчанию из PlayerWeaponHandler.")]
    public float reloadDuration = 0f;
    [Tooltip("При перезарядке старый магазин убирается в подсумок, а не бросается на пол.")]
    public bool stowMagazine;
    [Tooltip("На какой высоте левая рука берёт магазин: 0 — за нижний край, 1 — за верхний.")]
    [Range(0f, 1f)] public float magGrabHeight = 0.5f;

    [Header("Помповое оружие")]
    [Tooltip("Подвижное цевьё. Если задано — оружие помповое: после выстрела цевьё передёргивается, заряжается по одному патрону.")]
    public Transform pump;
    [Tooltip("Ход цевья назад (м).")]
    public float pumpTravel = 0.075f;
    [Tooltip("Окно заряжания (снизу ствольной коробки): сюда рука вставляет патроны.")]
    public Transform loadingPort;
    [Tooltip("Патрон, который рука держит при заряжании (скрыт, пока не нужен).")]
    public Transform shell;
    [Tooltip("Длительность вставки одного патрона (с).")]
    public float shellInsertDuration = 0.6f;
    [Tooltip("Крен оружия при поштучном заряжании (градусы): отрицательный — верх вправо, окно заряжания поворачивается к левой руке.")]
    public float shellReloadRoll = -30f;

    public bool IsPumpAction => pump != null;
    /// <summary>Положение цевья: 0 — вперёд, 1 — до упора назад. Затвор ходит вместе с ним.</summary>
    public float PumpPos { get; set; }

    public bool IsHeld { get; private set; }
    public int Ammo { get; private set; } = -1;
    public bool IsEmpty => Ammo == 0;
    /// <summary>Затвор зафиксирован сзади (патроны кончились). Снимается передёргиванием.</summary>
    public bool SlideLocked { get; set; }
    /// <summary>Ручное положение затвора (0 — вперёд, 1 — до упора назад, можно больше). Меньше 0 — автоматически.</summary>
    public float SlideManual { get; set; } = -1f;
    /// <summary>Положение рукояти взведения (rackHandle): 0 — вперёд, 1 — оттянута на rackHandleTravel.</summary>
    public float HandlePull { get; set; }
    /// <summary>Съёмный магазин (дочерний объект «Magazine»).</summary>
    public Transform magazine;

    /// <summary>Вставляет полный магазин (без учёта запаса). Затвор остаётся как был.</summary>
    public void Reload() => Ammo = magazineSize;

    /// <summary>Добавляет патроны в магазин (не больше ёмкости). Затвор остаётся как был.</summary>
    public void AddAmmo(int count) => Ammo = Mathf.Clamp(Mathf.Max(0, Ammo) + count, 0, magazineSize);

    /// <summary>Задаёт число патронов в магазине (загрузка сохранения).</summary>
    public void SetAmmo(int ammo)
    {
        Ammo = Mathf.Clamp(ammo, 0, magazineSize);
        SlideLocked = Ammo == 0 && !IsPumpAction;
    }

    /// <summary>Добавляет один патрон (поштучное заряжание).</summary>
    public void AddShell() => Ammo = Mathf.Min(Ammo + 1, magazineSize);

    /// <summary>Расходует один патрон. false — магазин пуст. Когда патроны кончились, затвор остаётся сзади (кроме помпы).</summary>
    public bool TryConsumeAmmo()
    {
        if (Ammo <= 0) return false;
        Ammo--;
        if (Ammo == 0 && !IsPumpAction) SlideLocked = true;
        return true;
    }

    Vector3 slideRestPos;
    Vector3 slideBack;
    float slideKick;
    Vector3 pumpRestPos;
    Vector3 pumpBack;
    Vector3 handleRestPos;
    Vector3 handleBack;

    // Место на прилавке (у оружия-товара): сюда оружие возвращается, если его продать Гале.
    Transform homeParent;
    Vector3 homePos;
    Quaternion homeRot;
    bool hasHome;
    readonly List<(Rigidbody rb, bool kinematic)> homeBodies = new List<(Rigidbody, bool)>();

    /// <summary>Оружие из магазина: его можно продать обратно, и оно вернётся на прилавок.</summary>
    public bool CanReturnToShop => hasHome;

    /// <summary>Вернуть проданное оружие на прилавок: снова товар, видно и с коллайдерами.</summary>
    public void ReturnToShop()
    {
        if (!hasHome) return;
        Unstow();
        IsHeld = false;
        transform.SetParent(homeParent, false);
        transform.localPosition = homePos;
        transform.localRotation = homeRot;
        foreach (var c in GetComponentsInChildren<Collider>(true)) c.enabled = true;
        foreach (var (rb, kinematic) in homeBodies)
        {
            if (rb == null) continue;
            rb.isKinematic = kinematic;
            if (!kinematic) { rb.linearVelocity = Vector3.zero; rb.angularVelocity = Vector3.zero; }
        }
        foreach (var r in GetComponentsInChildren<Renderer>(true)) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
        forSale = true;
    }

    void Awake()
    {
        if (price > 0)
        {
            hasHome = true;
            homeParent = transform.parent;
            homePos = transform.localPosition;
            homeRot = transform.localRotation;
            foreach (var rb in GetComponentsInChildren<Rigidbody>(true)) homeBodies.Add((rb, rb.isKinematic));
        }
        Ammo = magazineSize;
        if (rackHandle != null)
        {
            handleRestPos = rackHandle.localPosition;
            handleBack = rackHandle.parent.InverseTransformDirection(-transform.forward).normalized;
        }
        if (magazine == null)
            foreach (var t in GetComponentsInChildren<Transform>(true))
                if (t.name == "Magazine") { magazine = t; break; }
        if (pump != null)
        {
            pumpRestPos = pump.localPosition;
            pumpBack = pump.parent.InverseTransformDirection(-transform.forward).normalized;
        }
        if (slide == null) return;
        slideRestPos = slide.localPosition;
        // Направление «назад по стволу» в системе координат родителя затвора.
        slideBack = slide.parent.InverseTransformDirection(-transform.forward).normalized;
    }

    void OnEnable() => All.Add(this);
    void OnDisable() => All.Remove(this);

    // ---------- Инвентарь ----------

    string itemId;
    /// <summary>Вид предмета этого оружия в инвентаре (раздел «Снаряжение»). Регистрируется при первом обращении.</summary>
    public string ItemId => itemId ??= ItemDatabase.RegisterWeapon(this);

    /// <summary>Оружие лежит в инвентаре игрока (не в руках и не в мире): скрыто и без коллайдеров.</summary>
    public bool Stowed { get; private set; }
    readonly List<Behaviour> stowHidden = new List<Behaviour>();
    readonly List<Renderer> stowHiddenRenderers = new List<Renderer>();

    /// <summary>Убрать оружие в инвентарь: оно едет с игроком (owner), но не видно и ни с чем не сталкивается.</summary>
    public void Stow(Transform owner)
    {
        _ = ItemId; // модель для иконки снимается, пока оружие ещё видно
        if (Stowed) return;
        Stowed = true;
        IsHeld = false;
        foreach (var rb in GetComponentsInChildren<Rigidbody>(true))
        {
            if (!rb.isKinematic) { rb.linearVelocity = Vector3.zero; rb.angularVelocity = Vector3.zero; }
            rb.isKinematic = true;
        }
        foreach (var c in GetComponentsInChildren<Collider>(true)) c.enabled = false;
        stowHiddenRenderers.Clear();
        foreach (var r in GetComponentsInChildren<Renderer>(true))
            if (r.enabled) { r.enabled = false; stowHiddenRenderers.Add(r); }
        stowHidden.Clear();
        foreach (var l in GetComponentsInChildren<Light>(true))
            if (l.enabled) { l.enabled = false; stowHidden.Add(l); }
        transform.SetParent(owner, false);
        transform.localPosition = new Vector3(0f, 0.9f, 0f);
        transform.localRotation = Quaternion.identity;
    }

    /// <summary>Достать оружие из инвентаря (дальше его берёт в руки PlayerWeaponHandler).</summary>
    public void Unstow()
    {
        if (!Stowed) return;
        Stowed = false;
        foreach (var r in stowHiddenRenderers) if (r != null) r.enabled = true;
        foreach (var b in stowHidden) if (b != null) b.enabled = true;
        stowHiddenRenderers.Clear();
        stowHidden.Clear();
    }

    public void SetHeld(bool held)
    {
        IsHeld = held;
        foreach (var c in GetComponentsInChildren<Collider>(true)) c.enabled = !held;
        foreach (var rb in GetComponentsInChildren<Rigidbody>(true)) rb.isKinematic = true;
        // Тени от оружия в руках у самой камеры выглядят странно — отключаем.
        foreach (var r in GetComponentsInChildren<Renderer>(true))
            r.shadowCastingMode = held ? UnityEngine.Rendering.ShadowCastingMode.Off : UnityEngine.Rendering.ShadowCastingMode.On;
    }

    public void KickSlide() => slideKick = 1f;

    void Update()
    {
        if (rackHandle != null) rackHandle.localPosition = handleRestPos + handleBack * (rackHandleTravel * HandlePull);
        if (pump != null)
        {
            pump.localPosition = pumpRestPos + pumpBack * (pumpTravel * PumpPos);
            // Затвор помпы ходит вместе с цевьём.
            if (slide != null) slide.localPosition = slideRestPos + slideBack * (pumpTravel * PumpPos);
            return;
        }
        if (slide == null) return;
        // Пустой пистолет: затвор остаётся в заднем положении, пока его не передёрнут.
        if (SlideManual >= 0f) slideKick = SlideManual;
        else if (SlideLocked) slideKick = 1f;
        else slideKick = Mathf.MoveTowards(slideKick, 0f, Time.deltaTime * 12f);
        slide.localPosition = slideRestPos + slideBack * (0.035f * slideKick);
    }
}
