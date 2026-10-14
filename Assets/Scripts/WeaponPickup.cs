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

    /// <summary>Вставляет полный магазин (запас патронов не ограничен). Затвор остаётся как был.</summary>
    public void Reload() => Ammo = magazineSize;

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

    void Awake()
    {
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
