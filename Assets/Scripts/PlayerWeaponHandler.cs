using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Подбор оружия на E, бросок на G, удержание перед камерой (покачивание, инерция, отдача на ЛКМ),
/// прицел и подсказки на экране.
/// </summary>
[DefaultExecutionOrder(-10)] // раньше контроллера: клик, захватывающий курсор, не должен стрелять
public class PlayerWeaponHandler : MonoBehaviour
{
    public Camera playerCamera;
    public FirstPersonController controller;
    [Tooltip("Точка у камеры, к которой крепится оружие в руках.")]
    public Transform weaponHolder;

    public float pickupRange = 2.3f;
    public float pickupAngle = 30f;
    public float pickupDuration = 0.45f;
    [Tooltip("Скорость, с которой оружие выбрасывается вперёд на G (м/с).")]
    public float throwSpeed = 3.5f;
    [Tooltip("Минимальный интервал между выстрелами (с).")]
    public float fireInterval = 0.12f;

    public WeaponPickup CurrentWeapon { get; private set; }
    /// <summary>0 — руки свободны, 1 — оружие полностью в руках. Используется для смешивания поз рук.</summary>
    public float ArmedWeight { get; private set; }

    WeaponPickup lookTarget;
    float pickupT = 1f;
    Vector3 pickupStartLocalPos;
    Quaternion pickupStartLocalRot;
    Vector2 sway;
    float recoil;
    float nextFireTime;
    float bobBlend;

    [Header("Прицеливание (ПКМ)")]
    [Tooltip("Расстояние от камеры до мушки при прицеливании (м).")]
    public float aimDistance = 0.28f;
    public float aimSpeed = 10f;
    [Tooltip("Во сколько раз уменьшается угол обзора при прицеливании.")]
    public float aimFovScale = 0.85f;
    [Tooltip("Множитель чувствительности мыши при прицеливании.")]
    public float aimSensitivityScale = 0.6f;

    [Header("Перезарядка (R)")]
    [Tooltip("Длительность смены магазина (с).")]
    public float reloadDuration = 2.3f;
    [Tooltip("Дополнительное время на передёргивание затвора, если магазин был пуст (с).")]
    public float rackDuration = 1.2f;
    [Tooltip("Наклон пистолета влево при взведении затвора (градусы).")]
    public float rackRollAngle = 45f;
    [Tooltip("Магазин на поясе: позиция в системе координат игрока (слева, на уровне бедра).")]
    public Vector3 beltPosition = new Vector3(-0.2f, 0.95f, 0.12f);

    /// <summary>Момент (доля фазы передёргивания), когда рука отпускает рукоять затвора у оружия с rackGrab.</summary>
    const float RackReleaseHandle = 0.66f;
    /// <summary>Во сколько раз дальше обычного положения «затвор сзади» оттягивается рукоять (в долях хода 0,035 м).</summary>
    const float RackPullMax = 1.7f;

    float RackTime => CurrentWeapon != null && CurrentWeapon.rackDuration > 0f ? CurrentWeapon.rackDuration : rackDuration;
    float ReloadTime => CurrentWeapon != null && CurrentWeapon.reloadDuration > 0f ? CurrentWeapon.reloadDuration : reloadDuration;
    float FireInterval => CurrentWeapon != null && CurrentWeapon.fireInterval > 0f ? CurrentWeapon.fireInterval : fireInterval;
    /// <summary>Спуск заблокирован, пока ЛКМ не отпущена: клик, захвативший курсор, не должен запускать очередь.</summary>
    bool triggerBlocked = true;

    public bool IsReloading { get; private set; }
    /// <summary>Рука, переопределяющая хват при перезарядке (читает PlayerBodyAnimator для левой руки).</summary>
    public float ReloadHandWeight { get; private set; }
    public Vector3 ReloadHandPos { get; private set; }   // центр ладони
    public Quaternion ReloadHandRot { get; private set; } // forward — пальцы, up — ладонь
    /// <summary>0..1 — щипок большим и указательным пальцами левой руки (взведение затвора).</summary>
    public float ReloadPinch { get; private set; }

    [Header("Помповое оружие")]
    [Tooltip("Пауза между выстрелом и передёргиванием цевья (с).")]
    public float pumpDelay = 0.12f;
    [Tooltip("Передёргивание цевья целиком: назад и вперёд (с).")]
    public float pumpDuration = 0.5f;
    [Tooltip("Возврат левой руки на цевьё после последнего патрона (с).")]
    public float shellReturnDuration = 0.25f;

    /// <summary>Идёт передёргивание цевья: стрелять и перезаряжаться нельзя.</summary>
    public bool IsPumping { get; private set; }
    float pumpClock;
    bool pumpEject;

    /// <summary>Патрон в левой руке: смещение от центра ладони (вверх от ладони и вдоль пальцев), ось — вдоль пальцев.</summary>
    static readonly Vector3 ShellInHand = new Vector3(0f, 0.014f, 0.035f);
    /// <summary>Доля цикла вставки патрона, когда он уходит в окно заряжания (и счётчик растёт на 1).</summary>
    const float ShellInsertAt = 0.86f;
    bool shellMode, shellFirst, shellInserted, shellStop, shellFinishing;
    float shellTilt;

    float reloadClock, reloadBump, rackRoll;
    bool rackStage, wasEmpty, ammoFilled, unlocked, oldMagDropped, newMagShown, reloadGeo;
    Bounds magB, slideB;
    Vector3 magRestLocalPos, magRestRelPos;
    Quaternion magRestLocalRot, magRestRelRot;

    /// <summary>0 — от бедра, 1 — полностью прицелился.</summary>
    public float AimWeight { get; private set; }
    float baseFov;
    Vector3 aimPos;
    bool hasAimPos;

    void Start()
    {
        if (playerCamera != null) baseFov = playerCamera.fieldOfView;
    }

    void Update()
    {
        bool locked = Cursor.lockState == CursorLockMode.Locked;
        var keyboard = Keyboard.current;
        var mouse = Mouse.current;

        UpdateAim(mouse, locked);

        lookTarget = FindLookTarget();
        if (locked && keyboard != null && keyboard.eKey.wasPressedThisFrame && lookTarget != null)
            PickUp(lookTarget);
        else if (locked && keyboard != null && keyboard.gKey.wasPressedThisFrame && CurrentWeapon != null)
            Throw();

        UpdatePump();
        if (locked && keyboard != null && keyboard.rKey.wasPressedThisFrame && CanReload())
            StartReload();
        // Поштучное заряжание можно прервать выстрелом: дозаряжается текущий патрон, и рука возвращается на цевьё.
        if (IsReloading && shellMode && locked && mouse != null && mouse.leftButton.wasPressedThisFrame)
            shellStop = true;
        UpdateReload();

        if (!locked) triggerBlocked = true;
        else if (mouse != null && !mouse.leftButton.isPressed) triggerBlocked = false;
        // Автоматическое оружие стреляет, пока зажата ЛКМ; остальное — по одному выстрелу на нажатие.
        bool trigger = mouse != null && CurrentWeapon != null &&
                       (CurrentWeapon.automatic ? mouse.leftButton.isPressed && !triggerBlocked : mouse.leftButton.wasPressedThisFrame);
        if (locked && trigger && pickupT >= 1f && !IsReloading && !IsPumping)
            Fire();

        UpdateHolder(mouse, locked);
        UpdateHeldWeapon();
    }

    bool CanReload() =>
        CurrentWeapon != null && pickupT >= 1f && !IsReloading && !IsPumping && CurrentWeapon.Ammo >= 0 && CurrentWeapon.Ammo < CurrentWeapon.magazineSize;

    void StartReload()
    {
        IsReloading = true;
        reloadClock = 0f;
        rackStage = false;
        ammoFilled = unlocked = oldMagDropped = newMagShown = false;
        wasEmpty = CurrentWeapon.Ammo == 0; // если боезапас израсходован полностью — потом передёргиваем затвор

        // Помпа: патроны вставляются по одному через окно заряжания снизу.
        shellMode = CurrentWeapon.IsPumpAction && CurrentWeapon.loadingPort != null && CurrentWeapon.gripLeft != null;
        if (shellMode)
        {
            shellFirst = true;
            shellInserted = shellStop = shellFinishing = false;
            ammoFilled = true; // счётчик растёт по одному патрону, полной зарядки в конце нет
            reloadGeo = false;
            return;
        }

        var w = CurrentWeapon.transform;
        var m = CurrentWeapon.magazine;
        reloadGeo = m != null && CurrentWeapon.gripLeft != null;
        if (!reloadGeo) return;
        magB = LocalBounds(w, m);
        slideB = CurrentWeapon.slide != null ? LocalBounds(w, CurrentWeapon.slide) : magB;
        magRestLocalPos = m.localPosition;
        magRestLocalRot = m.localRotation;
        magRestRelPos = w.InverseTransformPoint(m.position);
        magRestRelRot = Quaternion.Inverse(w.rotation) * m.rotation;
    }

    void UpdateReload()
    {
        if (!IsReloading) return;
        if (CurrentWeapon == null) { CancelReload(); return; }
        reloadClock += Time.deltaTime;
        if (shellMode) { UpdateShellReload(); return; }

        if (!rackStage)
        {
            float tm = reloadClock / Mathf.Max(0.5f, ReloadTime);
            reloadBump = Mathf.Sin(Mathf.Clamp01(tm) * Mathf.PI);
            if (!ammoFilled && tm >= 0.92f) { CurrentWeapon.Reload(); ammoFilled = true; }
            if (tm >= 1f)
            {
                if (wasEmpty) { rackStage = true; reloadClock = 0f; reloadBump = 0f; }
                else FinishReload();
            }
        }
        else
        {
            float tr = reloadClock / Mathf.Max(0.2f, RackTime);
            // Пистолет кантуется влево, пока затвор оттягивают, и возвращается после отпускания.
            // Автомат (rackGrab): кантуется дольше, затвор отпускается позже — пока рука тянет рукоять.
            bool handle = CurrentWeapon.rackGrab != null;
            rackRoll = S(tr / (handle ? 0.25f : 0.2f)) * (1f - S((tr - (handle ? 0.74f : 0.65f)) / (handle ? 0.22f : 0.25f)));
            if (!unlocked && tr >= (handle ? RackReleaseHandle : 0.5f))
            {
                // Затвор отпущен — он встаёт вперёд.
                CurrentWeapon.SlideManual = -1f;
                CurrentWeapon.SlideLocked = false;
                unlocked = true;
            }
            if (tr >= 1f) FinishReload();
        }
    }

    /// <summary>
    /// Поштучное заряжание: цикл «рука к поясу → берёт патрон → под окно заряжания → вставляет» повторяется,
    /// пока магазин не полон (или игрок не прервал выстрелом). Каждый вставленный патрон сразу добавляется к счётчику.
    /// </summary>
    void UpdateShellReload()
    {
        if (shellFinishing)
        {
            if (reloadClock >= shellReturnDuration)
            {
                bool chamber = wasEmpty;
                FinishReload();
                if (chamber) StartPump(0.05f, false); // ружьё было пустым — досылаем патрон в патронник
            }
            return;
        }
        float t = reloadClock / Mathf.Max(0.2f, CurrentWeapon.shellInsertDuration);
        if (!shellInserted && t >= ShellInsertAt) { CurrentWeapon.AddShell(); shellInserted = true; }
        if (t < 1f) return;
        reloadClock = 0f;
        if (CurrentWeapon.Ammo >= CurrentWeapon.magazineSize || shellStop) shellFinishing = true;
        else { shellInserted = false; shellFirst = false; }
    }

    void StartPump(float delay, bool eject)
    {
        IsPumping = true;
        pumpClock = -delay;
        pumpEject = eject;
    }

    /// <summary>Передёргивание цевья: резко назад (гильза вылетает в крайнем заднем положении), пауза, вперёд.
    /// Левая рука держится за цевьё (Grip_L — дочерний объект цевья), поэтому идёт вместе с ним.</summary>
    void UpdatePump()
    {
        if (!IsPumping) return;
        var wp = CurrentWeapon;
        if (wp == null || !wp.IsPumpAction) { IsPumping = false; return; }
        pumpClock += Time.deltaTime;
        float t = Mathf.Max(0f, pumpClock) / Mathf.Max(0.1f, pumpDuration);
        wp.PumpPos = S(t / 0.4f) * (1f - S((t - 0.55f) / 0.45f));
        if (pumpEject && t >= 0.38f)
        {
            pumpEject = false;
            var effects = wp.GetComponent<WeaponEffects>();
            var cc = GetComponent<CharacterController>();
            if (effects != null) effects.EjectCasing(cc != null ? cc.velocity : Vector3.zero, cc);
        }
        if (t >= 1f) { wp.PumpPos = 0f; IsPumping = false; }
    }

    void FinishReload()
    {
        if (CurrentWeapon != null && !ammoFilled) CurrentWeapon.Reload();
        EndReloadState();
    }

    /// <summary>Прерывание (оружие выброшено): магазин возвращается на место, патроны не добавляются.</summary>
    void CancelReload()
    {
        if (!IsReloading) return;
        EndReloadState();
    }

    void EndReloadState()
    {
        IsReloading = false;
        ReloadHandWeight = 0f;
        ReloadPinch = 0f;
        reloadBump = 0f;
        rackRoll = 0f;
        var w = CurrentWeapon;
        if (w == null) return;
        w.SlideManual = -1f;
        w.HandlePull = 0f;
        if (unlocked) w.SlideLocked = false;
        RestoreMagazine(w);
        if (w.shell != null) SetShellVisible(w, false);
    }

    void RestoreMagazine(WeaponPickup w)
    {
        if (w.magazine == null || !reloadGeo) return;
        SetMagazineVisible(w, true);
        w.magazine.localPosition = magRestLocalPos;
        w.magazine.localRotation = magRestLocalRot;
    }

    static float S(float x) => Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(x));

    /// <summary>Габариты всех рендереров src в системе координат root.</summary>
    static Bounds LocalBounds(Transform root, Transform src)
    {
        bool first = true;
        Bounds b = new Bounds();
        foreach (var r in src.GetComponentsInChildren<Renderer>(true))
        {
            Bounds lb = r.localBounds;
            for (int i = 0; i < 8; i++)
            {
                Vector3 c = lb.center + Vector3.Scale(lb.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                Vector3 p = root.InverseTransformPoint(r.transform.TransformPoint(c));
                if (first) { b = new Bounds(p, Vector3.zero); first = false; } else b.Encapsulate(p);
            }
        }
        return b;
    }

    // Анимация левой руки и магазина считается после всех Update: к этому моменту оружие и камера уже на финальных местах.
    void LateUpdate()
    {
        ReloadHandWeight = 0f;
        ReloadPinch = 0f;
        if (!IsReloading || CurrentWeapon == null) return;
        if (shellMode) { AnimateShellHand(); return; }
        if (!reloadGeo) return;

        var wp = CurrentWeapon;
        Transform w = wp.transform, gl = wp.gripLeft;
        Quaternion baseRot = gl.rotation;
        Vector3 home = gl.position;
        // Ладонь левой руки — слева от магазина (на высоте magGrabHeight), нормаль ладони смотрит на него.
        Vector3 palmOff = new Vector3(magB.min.x - 0.008f - magB.center.x, magB.min.y + magB.size.y * wp.magGrabHeight - magB.center.y, 0f);
        // Одноручное оружие: левая рука плавно подключается к перезарядке и уходит обратно вниз.
        float handWeight = 1f;
        Vector3 magHold = w.TransformPoint(magB.center + palmOff);
        Vector3 magDown = w.TransformPoint(magB.center + palmOff + new Vector3(0f, -magB.size.y * 1.15f, 0f));
        Vector3 belt = transform.TransformPoint(beltPosition);
        Quaternion beltRot = Quaternion.LookRotation((transform.forward * 0.4f - transform.up).normalized, transform.right);

        Vector3 pos = home;
        Quaternion rot = baseRot;

        if (!rackStage && wp.stowMagazine)
        {
            // Старый магазин не бросается: рука убирает его в подсумок на поясе и достаёт новый из соседнего.
            float t = reloadClock / Mathf.Max(0.5f, ReloadTime);
            Vector3 pouchOld = belt;
            Vector3 pouchNew = belt + transform.forward * 0.09f;
            Vector3 tuck = -transform.up * 0.07f; // насколько магазин уходит в подсумок
            bool magInHand = false;
            if (t < 0.10f)                       // рука тянется к магазину
                pos = Vector3.Lerp(home, magHold, S(t / 0.10f));
            else if (t < 0.22f)                  // вынимает его
            {
                pos = Vector3.Lerp(magHold, magDown, S((t - 0.10f) / 0.12f));
                magInHand = true;
            }
            else if (t < 0.42f)                  // несёт к подсумку
            {
                float k = S((t - 0.22f) / 0.20f);
                pos = Vector3.Lerp(magDown, pouchOld, k);
                rot = Quaternion.Slerp(baseRot, beltRot, k);
                magInHand = true;
            }
            else if (t < 0.48f)                  // убирает в подсумок
            {
                pos = Vector3.Lerp(pouchOld, pouchOld + tuck, S((t - 0.42f) / 0.06f));
                rot = beltRot;
                magInHand = true;
            }
            else if (t < 0.54f)                  // перекладывает руку к соседнему подсумку
            {
                pos = Vector3.Lerp(pouchOld + tuck, pouchNew + tuck, S((t - 0.48f) / 0.06f));
                rot = beltRot;
            }
            else if (t < 0.62f)                  // вытаскивает новый магазин
            {
                pos = Vector3.Lerp(pouchNew + tuck, pouchNew, S((t - 0.54f) / 0.08f));
                rot = beltRot;
                magInHand = true;
            }
            else if (t < 0.80f)                  // несёт к оружию
            {
                float k = S((t - 0.62f) / 0.18f);
                pos = Vector3.Lerp(pouchNew, magDown, k);
                rot = Quaternion.Slerp(beltRot, baseRot, k);
                magInHand = true;
            }
            else if (t < 0.92f)                  // вставляет
            {
                pos = Vector3.Lerp(magDown, magHold, S((t - 0.80f) / 0.12f));
                magInHand = true;
            }
            else pos = Vector3.Lerp(magHold, home, S((t - 0.92f) / 0.08f)); // возвращает руку на цевьё

            handWeight = S(t / 0.10f) * (1f - S((t - 0.92f) / 0.08f));
            if (!oldMagDropped && t >= 0.48f) { SetMagazineVisible(wp, false); oldMagDropped = true; }
            if (oldMagDropped && !newMagShown && t >= 0.54f) { SetMagazineVisible(wp, true); newMagShown = true; }
            if (t >= 0.92f) RestoreMagazine(wp);
            else if (magInHand && (!oldMagDropped || newMagShown)) HoldMagazine(wp, pos, rot, palmOff);
        }
        else if (!rackStage)
        {
            float t = reloadClock / Mathf.Max(0.5f, ReloadTime);
            bool magInHand = false;
            if (t < 0.12f)                      // рука тянется к магазину
                pos = Vector3.Lerp(home, magHold, S(t / 0.12f));
            else if (t < 0.30f)                  // вынимает магазин из рукояти
            {
                pos = Vector3.Lerp(magHold, magDown, S((t - 0.12f) / 0.18f));
                magInHand = true;
            }
            else if (t < 0.52f)                  // рука идёт к поясу
            {
                float k = S((t - 0.30f) / 0.22f);
                pos = Vector3.Lerp(magDown, belt, k);
                rot = Quaternion.Slerp(baseRot, beltRot, k);
            }
            else if (t < 0.60f) { pos = belt; rot = beltRot; magInHand = t >= 0.56f; }   // берёт новый магазин
            else if (t < 0.80f)                  // несёт новый магазин к пистолету
            {
                float k = S((t - 0.60f) / 0.20f);
                pos = Vector3.Lerp(belt, magDown, k);
                rot = Quaternion.Slerp(beltRot, baseRot, k);
                magInHand = true;
            }
            else if (t < 0.92f)                  // вставляет
            {
                pos = Vector3.Lerp(magDown, magHold, S((t - 0.80f) / 0.12f));
                magInHand = true;
            }
            else pos = Vector3.Lerp(magHold, home, S((t - 0.92f) / 0.08f)); // возвращает руку на хват

            handWeight = S(t / 0.12f) * (1f - S((t - 0.92f) / 0.08f));
            // Старый магазин падает, как только его выдернули; новый появляется в руке у пояса.
            if (!oldMagDropped && t >= 0.30f) { DropOldMagazine(); oldMagDropped = true; }
            if (oldMagDropped && !newMagShown && t >= 0.56f) { SetMagazineVisible(wp, true); newMagShown = true; }
            if (t >= 0.92f) RestoreMagazine(wp);
            else if (magInHand && (t < 0.30f || newMagShown)) HoldMagazine(wp, pos, rot, palmOff);
        }
        else
        {
            // Передёргивание: левая рука берётся за затвор сзади, чуть оттягивает и отпускает.
            float tr = reloadClock / Mathf.Max(0.2f, RackTime);
            if (wp.rackGrab != null)
            {
                // Оружие кантовано на левый бок. Левая рука заходит к рукояти со стороны, куда та торчит (ладонью к оружию),
                // берёт её, оттягивает назад вместе с затвором, отпускает — затвор уходит вперёд — и возвращается на цевьё.
                Vector3 fwd = w.forward;
                Vector3 outward = wp.rackGrabSide != Vector3.zero
                    ? w.TransformDirection(wp.rackGrabSide).normalized
                    : w.right * (wp.rackGrab.localPosition.x < 0f ? -1f : 1f); // от оружия к рукояти
                Quaternion graspRot = Quaternion.LookRotation(fwd, -outward);
                // Ладонь у рукояти при положении затвора p (1 — как у запертого сзади, больше — оттянут дальше).
                // Отдельная рукоять взведения (rackHandle) в покое стоит впереди и проходит весь ход за 1 → RackPullMax.
                bool hasHandle = wp.rackHandle != null;
                System.Func<float, float> handleFrac = p => Mathf.Clamp01((p - 1f) / (RackPullMax - 1f));
                System.Func<float, Vector3> handAt = p => wp.rackGrab.position + outward * 0.012f
                    - fwd * (hasHandle ? wp.rackHandleTravel * handleFrac(p) : 0.035f * p);
                Vector3 hover = handAt(1f) + outward * 0.06f;
                Vector3 pulledOff = handAt(RackPullMax) - handAt(1f); // насколько рука ушла назад к моменту отпускания
                float pull = 1f;
                if (tr < 0.18f) { float k = S(tr / 0.18f); pos = Vector3.Lerp(home, hover, k); rot = Quaternion.Slerp(baseRot, graspRot, k); }
                else if (tr < 0.28f) { pos = Vector3.Lerp(hover, handAt(1f), S((tr - 0.18f) / 0.1f)); rot = graspRot; }
                else if (tr < 0.4f) { pos = handAt(1f); rot = graspRot; }
                else if (tr < 0.6f) { pull = Mathf.Lerp(1f, RackPullMax, S((tr - 0.4f) / 0.2f)); pos = handAt(pull); rot = graspRot; }
                else if (tr < RackReleaseHandle) { pull = RackPullMax; pos = handAt(pull); rot = graspRot; }
                else
                {
                    // Отпустила: затвор уже ушёл вперёд (его отпускает UpdateReload), рука отходит от рукояти и возвращается.
                    Vector3 released = handAt(RackPullMax);
                    float k = S((tr - RackReleaseHandle) / 0.14f);
                    float back = S((tr - 0.82f) / 0.18f);
                    pos = Vector3.Lerp(Vector3.Lerp(released, hover + pulledOff, k), home, back);
                    rot = Quaternion.Slerp(graspRot, baseRot, back);
                }
                if (tr < RackReleaseHandle) { wp.SlideManual = pull; wp.HandlePull = handleFrac(pull); }
                else wp.HandlePull = 0f;
                handWeight = S(tr / 0.18f) * (1f - S((tr - 0.82f) / 0.18f));
                ReloadPinch = S((tr - 0.28f) / 0.12f) * (1f - S((tr - RackReleaseHandle) / 0.08f));
            }
            else
            {
                Vector3 grab = w.TransformPoint(new Vector3(slideB.min.x - 0.012f, slideB.center.y + 0.004f, slideB.min.z - 0.035f));
                Vector3 back = -w.forward * 0.0105f;
                if (tr < 0.3f) { pos = Vector3.Lerp(home, grab, S(tr / 0.3f)); wp.SlideManual = 1f; }
                else if (tr < 0.5f)
                {
                    float k = S((tr - 0.3f) / 0.2f);
                    pos = grab + back * k;
                    wp.SlideManual = Mathf.Lerp(1f, 1.3f, k);
                }
                else pos = Vector3.Lerp(grab + back, home, S((tr - 0.5f) / 0.5f));
                // Щипок: пальцы сходятся на затворе, пока его держат, и разжимаются при отпускании.
                ReloadPinch = S((tr - 0.1f) / 0.15f) * (1f - S((tr - 0.5f) / 0.15f));
                handWeight = S(tr / 0.3f) * (1f - S((tr - 0.5f) / 0.5f));
            }
        }

        ReloadHandWeight = wp.oneHanded ? handWeight : 1f;
        ReloadHandPos = pos;
        ReloadHandRot = rot;
    }

    /// <summary>
    /// Левая рука при поштучном заряжании: с цевья (или из-под окна после предыдущего патрона) — к патронташу на поясе,
    /// берёт патрон, подносит под окно заряжания ладонью вверх и вталкивает его вверх-вперёд в окно.
    /// </summary>
    void AnimateShellHand()
    {
        var wp = CurrentWeapon;
        Transform w = wp.transform, gl = wp.gripLeft;
        Quaternion baseRot = gl.rotation;
        Vector3 home = gl.position;
        Vector3 pouch = transform.TransformPoint(beltPosition) + transform.forward * 0.05f;
        Quaternion pouchRot = Quaternion.LookRotation((transform.forward * 0.4f - transform.up).normalized, transform.right);
        // Под окном: ладонь смотрит вверх, пальцы — вдоль ствола, патрон лежит на пальцах.
        Quaternion portRot = Quaternion.LookRotation(w.forward, w.up);
        Vector3 atPort = wp.loadingPort.position - portRot * ShellInHand + w.up * 0.006f; // патрон чуть заходит в окно
        // Между патронами рука уходит низко под ружьё — за нижний край кадра.
        Vector3 below = atPort - w.up * 0.15f - w.forward * 0.03f;

        Vector3 pos;
        Quaternion rot;
        bool shellVisible = false;
        if (shellFinishing)
        {
            float k = S(reloadClock / Mathf.Max(0.05f, shellReturnDuration));
            pos = Vector3.Lerp(below, home, k);
            rot = Quaternion.Slerp(portRot, baseRot, k);
        }
        else
        {
            float t = reloadClock / Mathf.Max(0.2f, wp.shellInsertDuration);
            Vector3 from = shellFirst ? home : below;
            Quaternion fromRot = shellFirst ? baseRot : portRot;
            if (t < 0.30f)                       // рука идёт к патронташу
            {
                float k = S(t / 0.30f);
                pos = Vector3.Lerp(from, pouch, k);
                rot = Quaternion.Slerp(fromRot, pouchRot, k);
            }
            else if (t < 0.38f)                  // берёт патрон
            {
                pos = pouch; rot = pouchRot;
                shellVisible = t >= 0.33f;
            }
            else if (t < 0.68f)                  // несёт под окно заряжания
            {
                float k = S((t - 0.38f) / 0.30f);
                pos = Vector3.Lerp(pouch, below, k);
                rot = Quaternion.Slerp(pouchRot, portRot, k);
                shellVisible = true;
            }
            else if (t < ShellInsertAt)          // вталкивает в окно
            {
                pos = Vector3.Lerp(below, atPort, S((t - 0.68f) / (ShellInsertAt - 0.68f)));
                rot = portRot;
                shellVisible = true;
            }
            else                                 // патрон в магазине — рука опускается
            {
                pos = Vector3.Lerp(atPort, below, S((t - ShellInsertAt) / (1f - ShellInsertAt)));
                rot = portRot;
            }
        }

        if (wp.shell != null)
        {
            SetShellVisible(wp, shellVisible);
            // Ось патрона (Y цилиндра) — вдоль пальцев, донце — к запястью.
            if (shellVisible) wp.shell.SetPositionAndRotation(pos + rot * ShellInHand, rot * Quaternion.Euler(90f, 0f, 0f));
        }
        ReloadHandWeight = 1f;
        ReloadHandPos = pos;
        ReloadHandRot = rot;
        ReloadPinch = shellVisible ? 0.6f : 0f;
    }

    static void SetShellVisible(WeaponPickup wp, bool visible)
    {
        foreach (var r in wp.shell.GetComponentsInChildren<Renderer>(true)) r.enabled = visible;
    }

    /// <summary>Ставит магазин в левую руку: ладонь (pos, rot) — сбоку от магазина, на расстоянии palmOff от его центра.</summary>
    void HoldMagazine(WeaponPickup wp, Vector3 pos, Quaternion rot, Vector3 palmOff)
    {
        Quaternion magRot = rot * Quaternion.Inverse(wp.gripLeft.localRotation);
        Vector3 center = pos - magRot * palmOff;
        wp.magazine.rotation = magRot * magRestRelRot;
        wp.magazine.position = center + magRot * (magRestRelPos - magB.center);
    }

    static void SetMagazineVisible(WeaponPickup wp, bool visible)
    {
        foreach (var r in wp.magazine.GetComponentsInChildren<Renderer>(true)) r.enabled = visible;
    }

    void DropOldMagazine()
    {
        var m = CurrentWeapon.magazine;
        var clone = Instantiate(m.gameObject, m.position, m.rotation);
        clone.transform.localScale = m.lossyScale;
        var col = clone.AddComponent<BoxCollider>();
        Bounds b = LocalBounds(clone.transform, clone.transform);
        col.center = b.center;
        col.size = b.size;
        var cc = GetComponent<CharacterController>();
        if (cc != null) Physics.IgnoreCollision(col, cc);
        var rb = clone.AddComponent<Rigidbody>();
        rb.mass = 0.15f;
        rb.linearVelocity = (cc != null ? cc.velocity : Vector3.zero) - Vector3.up * 0.5f;
        rb.angularVelocity = Random.insideUnitSphere * 3f;
        Destroy(clone, 8f);
        SetMagazineVisible(CurrentWeapon, false);
    }

    void UpdateAim(Mouse mouse, bool locked)
    {
        bool aiming = locked && mouse != null && mouse.rightButton.isPressed && !IsReloading
                      && CurrentWeapon != null && pickupT >= 1f && hasAimPos
                      && (controller == null || !controller.ThirdPerson);
        AimWeight = Mathf.MoveTowards(AimWeight, aiming ? 1f : 0f, Time.deltaTime * aimSpeed * 0.5f);
        float t = Mathf.SmoothStep(0f, 1f, AimWeight);

        if (playerCamera != null && baseFov > 0f)
            playerCamera.fieldOfView = Mathf.Lerp(baseFov, baseFov * aimFovScale, t);
        if (controller != null)
            controller.LookSensitivityScale = Mathf.Lerp(1f, aimSensitivityScale, t);
    }

    /// <summary>
    /// Считает положение холдера, при котором верх затвора (мушка) оказывается на линии взгляда
    /// строго по центру экрана. Берётся из габаритов затвора в системе координат оружия.
    /// </summary>
    void ComputeAimPosition(WeaponPickup weapon)
    {
        Transform root = weapon.transform;
        if (weapon.aimPoint != null)
        {
            hasAimPos = true;
            Vector3 p = root.InverseTransformPoint(weapon.aimPoint.position);
            aimPos = new Vector3(-p.x, -p.y, aimDistance - p.z);
            return;
        }
        Transform src = weapon.aimSource != null ? weapon.aimSource : weapon.slide != null ? weapon.slide : root;
        bool first = true;
        Bounds b = new Bounds();
        foreach (var r in src.GetComponentsInChildren<Renderer>())
        {
            Bounds lb = r.localBounds;
            for (int i = 0; i < 8; i++)
            {
                Vector3 c = lb.center + Vector3.Scale(lb.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                Vector3 p = root.InverseTransformPoint(r.transform.TransformPoint(c));
                if (first) { b = new Bounds(p, Vector3.zero); first = false; } else b.Encapsulate(p);
            }
        }
        hasAimPos = !first;
        if (first) return;
        // Мушка — верх затвора по центру; ставим её на ось камеры на расстоянии aimDistance.
        Vector3 sight = new Vector3(b.center.x, b.max.y, b.center.z);
        aimPos = new Vector3(-sight.x, -sight.y, aimDistance - sight.z);
    }

    WeaponPickup FindLookTarget()
    {
        if (playerCamera == null) return null;
        Transform cam = playerCamera.transform;
        WeaponPickup best = null;
        float bestAngle = pickupAngle;
        foreach (var w in WeaponPickup.All)
        {
            if (w == null || w.IsHeld) continue;
            Vector3 center = WeaponCenter(w);
            Vector3 to = center - cam.position;
            float dist = to.magnitude;
            if (dist > pickupRange) continue;
            float angle = Vector3.Angle(cam.forward, to);
            // Чем ближе предмет, тем шире допустимый угол — подбирать с пола удобнее.
            float allowed = Mathf.Lerp(pickupAngle * 1.5f, pickupAngle * 0.6f, dist / pickupRange);
            if (angle > allowed || angle > bestAngle * 1.5f) continue;
            // Проверка видимости: ничего не загораживает предмет.
            if (Physics.Raycast(cam.position, to / dist, out RaycastHit hit, dist - 0.05f, ~0, QueryTriggerInteraction.Ignore)
                && !hit.transform.IsChildOf(w.transform) && !hit.transform.IsChildOf(transform))
                continue;
            if (best == null || angle < bestAngle) { best = w; bestAngle = angle; }
        }
        return best;
    }

    static Vector3 WeaponCenter(WeaponPickup w)
    {
        var r = w.GetComponentInChildren<Renderer>();
        return r != null ? r.bounds.center : w.transform.position;
    }

    void PickUp(WeaponPickup weapon)
    {
        if (CurrentWeapon != null) Drop(weapon.transform.position, weapon.transform.rotation);

        CurrentWeapon = weapon;
        weapon.SetHeld(true);
        ComputeAimPosition(weapon);
        weapon.transform.SetParent(weaponHolder, true);
        pickupStartLocalPos = weapon.transform.localPosition;
        pickupStartLocalRot = weapon.transform.localRotation;
        pickupT = 0f;
    }

    void Drop(Vector3 position, Quaternion rotation)
    {
        CancelReload();
        var w = CurrentWeapon;
        IsPumping = false;
        w.PumpPos = 0f;
        shellTilt = 0f;
        CurrentWeapon = null;
        w.transform.SetParent(null, true);
        w.transform.SetPositionAndRotation(position, rotation);
        w.SetHeld(false);
        ArmedWeight = 0f;
        hasAimPos = false;
    }

    /// <summary>Выбрасывает оружие из рук (G): оно летит вперёд по взгляду, кувыркаясь, и падает с физикой. Подобрать снова — E.</summary>
    void Throw()
    {
        var w = CurrentWeapon;
        Transform wt = w.transform;
        Transform cam = playerCamera != null ? playerCamera.transform : transform;
        var cc = GetComponent<CharacterController>();

        // У стены оружие в руках может оказаться внутри неё — тогда отодвигаем его к камере.
        Vector3 start = wt.position;
        Vector3 center = WeaponCenter(w);
        Vector3 toCenter = center - cam.position;
        if (Physics.Raycast(cam.position, toCenter.normalized, out RaycastHit hit, toCenter.magnitude + 0.1f, ~0, QueryTriggerInteraction.Ignore)
            && !hit.transform.IsChildOf(wt) && !hit.transform.IsChildOf(transform))
            start += hit.point + hit.normal * 0.12f - center;

        Drop(start, wt.rotation);

        var rb = w.GetComponent<Rigidbody>();
        if (rb == null)
        {
            rb = w.gameObject.AddComponent<Rigidbody>();
            rb.mass = 2.5f;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        }
        rb.isKinematic = false;
        // Сам игрок оружие не задевает — ни при броске, ни потом, когда оно лежит под ногами.
        if (cc != null)
            foreach (var col in w.GetComponentsInChildren<Collider>(true)) Physics.IgnoreCollision(col, cc);
        Vector3 playerVel = cc != null ? cc.velocity : Vector3.zero;
        rb.linearVelocity = playerVel + cam.forward * throwSpeed + Vector3.up * 1.2f;
        rb.angularVelocity = cam.TransformDirection(new Vector3(Random.Range(-2f, 2f), Random.Range(-4f, 4f), Random.Range(-6f, 6f)));
    }

    void Fire()
    {
        if (Time.time < nextFireTime) return;
        if (!CurrentWeapon.TryConsumeAmmo()) return; // магазин пуст
        nextFireTime = Time.time + FireInterval;

        recoil = 1f;
        // Помпа: затвор не откатывается сам — его передёргивает левая рука цевьём после выстрела.
        if (CurrentWeapon.IsPumpAction) StartPump(pumpDelay, true);
        else CurrentWeapon.KickSlide();
        float rs = CurrentWeapon.recoilScale;
        if (controller != null) controller.AddRecoil(Random.Range(1.4f, 2.2f) * rs, Random.Range(-0.5f, 0.5f) * rs);

        var effects = CurrentWeapon.GetComponent<WeaponEffects>();
        if (effects != null && playerCamera != null)
        {
            var cc = GetComponent<CharacterController>();
            Transform cam = playerCamera.transform;
            effects.Fire(cam.position, cam.forward, cc != null ? cc.velocity : Vector3.zero, cc);
        }
    }

    void UpdateHolder(Mouse mouse, bool locked)
    {
        if (weaponHolder == null) return;
        Vector3 basePos = CurrentWeapon != null ? CurrentWeapon.holdOffset : new Vector3(0.08f, -0.15f, 0.36f);
        float aim = Mathf.SmoothStep(0f, 1f, AimWeight);
        if (aim > 0f) basePos = Vector3.Lerp(basePos, aimPos, aim);
        float steady = 1f - 0.85f * aim; // при прицеливании качание и инерция почти исчезают

        // Инерция от движения мыши: оружие слегка отстаёт от взгляда.
        Vector2 mouseDelta = (mouse != null && locked) ? mouse.delta.ReadValue() : Vector2.zero;
        sway = Vector2.Lerp(sway, Vector2.ClampMagnitude(mouseDelta * 0.0009f, 0.03f), Time.deltaTime * 8f);

        // Покачивание при ходьбе (в такт шагам).
        float phase = controller != null ? controller.StepPhase : 0f;
        float move = controller != null ? controller.MoveAmount : 0f;
        bobBlend = Mathf.MoveTowards(bobBlend, move, Time.deltaTime * 3f);
        float breathe = Mathf.Sin(Time.time * 1.6f) * 0.002f;
        Vector3 bob = new Vector3(Mathf.Cos(phase) * 0.012f, -Mathf.Abs(Mathf.Sin(phase)) * 0.012f + breathe, 0f) * Mathf.Max(bobBlend, 0.15f);

        recoil = Mathf.MoveTowards(recoil, 0f, Time.deltaTime * 6f);
        float r = recoil * recoil;
        Vector3 recoilPos = new Vector3(0f, 0.01f * r, -0.06f * r);

        // Перезарядка: пистолет опускается, наклоняется стволом вверх и возвращается.
        float rl = IsReloading ? Mathf.SmoothStep(0f, 1f, reloadBump) : 0f;
        Vector3 reloadPos = new Vector3(-0.015f, -0.02f, -0.02f) * rl;
        Vector3 reloadRot = new Vector3(-6f, 0f, 8f) * rl + new Vector3(0f, 0f, CurrentWeapon != null ? CurrentWeapon.rackRoll : rackRollAngle) * Mathf.SmoothStep(0f, 1f, rackRoll);

        // Поштучное заряжание: ружьё кренится, подставляя окно заряжания левой руке, чуть приподнимает ствол
        // и опускается, чтобы рука с патроном оставалась ниже кадра.
        shellTilt = Mathf.MoveTowards(shellTilt, IsReloading && shellMode && !shellFinishing ? 1f : 0f, Time.deltaTime * 4f);
        float st = Mathf.SmoothStep(0f, 1f, shellTilt);
        if (CurrentWeapon != null && st > 0f)
        {
            reloadRot += new Vector3(-8f, 0f, CurrentWeapon.shellReloadRoll) * st;
            reloadPos += new Vector3(-0.02f, -0.025f, -0.02f) * st;
        }
        // Передёргивание цевья: ружьё чуть подаётся назад-вниз вслед за рукой.
        if (CurrentWeapon != null && CurrentWeapon.IsPumpAction)
            reloadPos += new Vector3(0f, -0.004f, -0.012f) * CurrentWeapon.PumpPos;

        weaponHolder.localPosition = basePos + (bob + new Vector3(-sway.x, -sway.y, 0f)) * steady + recoilPos * (1f - 0.5f * aim) + reloadPos;
        weaponHolder.localRotation = Quaternion.Euler(-14f * r * (1f - 0.5f * aim) + sway.y * 60f * steady, sway.x * 60f * steady, -sway.x * 40f * steady) * Quaternion.Euler(reloadRot);
    }

    void UpdateHeldWeapon()
    {
        if (CurrentWeapon == null) { ArmedWeight = Mathf.MoveTowards(ArmedWeight, 0f, Time.deltaTime * 4f); return; }

        if (pickupT < 1f)
        {
            pickupT = Mathf.Min(1f, pickupT + Time.deltaTime / pickupDuration);
            float t = Mathf.SmoothStep(0f, 1f, pickupT);
            CurrentWeapon.transform.localPosition = Vector3.Lerp(pickupStartLocalPos, Vector3.zero, t);
            CurrentWeapon.transform.localRotation = Quaternion.Slerp(pickupStartLocalRot, Quaternion.identity, t);
            ArmedWeight = t;
        }
        else
        {
            CurrentWeapon.transform.localPosition = Vector3.zero;
            CurrentWeapon.transform.localRotation = Quaternion.identity;
            ArmedWeight = 1f;
        }
    }

    void OnGUI()
    {
        // Прицел.
        float cx = Screen.width * 0.5f, cy = Screen.height * 0.5f;
        GUI.color = new Color(1f, 1f, 1f, 0.85f * (1f - AimWeight));
        GUI.DrawTexture(new Rect(cx - 2, cy - 2, 4, 4), Texture2D.whiteTexture);

        var style = new GUIStyle(GUI.skin.label) { fontSize = Mathf.Max(16, Screen.height / 40), alignment = TextAnchor.MiddleCenter };
        if (lookTarget != null && Cursor.lockState == CursorLockMode.Locked)
        {
            string text = "[E]  Подобрать: " + lookTarget.displayName;
            var rect = new Rect(cx - 300, cy + 40, 600, 40);
            GUI.color = new Color(0, 0, 0, 0.8f);
            GUI.Label(new Rect(rect.x + 2, rect.y + 2, rect.width, rect.height), text, style);
            GUI.color = Color.white;
            GUI.Label(rect, text, style);
        }

        // Счётчик патронов — снизу слева.
        if (CurrentWeapon != null && CurrentWeapon.Ammo >= 0)
        {
            int ammo = CurrentWeapon.Ammo;
            var ammoStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = Mathf.Max(28, Screen.height / 18),
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.LowerLeft
            };
            string ammoText = IsReloading && !ammoFilled ? "..." : ammo + " / " + CurrentWeapon.magazineSize;
            var ammoRect = new Rect(24, Screen.height - 84, 400, 70);
            GUI.color = new Color(0, 0, 0, 0.8f);
            GUI.Label(new Rect(ammoRect.x + 2, ammoRect.y + 2, ammoRect.width, ammoRect.height), ammoText, ammoStyle);
            GUI.color = ammo == 0 ? new Color(1f, 0.3f, 0.25f) : Color.white;
            GUI.Label(ammoRect, ammoText, ammoStyle);
        }

        if (Cursor.lockState != CursorLockMode.Locked)
        {
            var small = new GUIStyle(GUI.skin.label) { fontSize = Mathf.Max(12, Screen.height / 60), alignment = TextAnchor.MiddleCenter };
            GUI.color = new Color(1f, 1f, 1f, 0.7f);
            GUI.Label(new Rect(0, Screen.height - 34, Screen.width, 30), "Кликните в окно игры, чтобы управлять", small);
        }
        GUI.color = Color.white;
    }
}
