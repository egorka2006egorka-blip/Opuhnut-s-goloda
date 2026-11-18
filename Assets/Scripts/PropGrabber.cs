using System.Collections;
using UnityEngine;

/// <summary>
/// Перенос мелких предметов (GrabbableProp): удерживайте «Подобрать» (E), глядя на предмет, — он поднимается
/// и висит перед камерой; отпустите E — предмет падает; левая кнопка мыши — швырнуть.
/// Пока предмет в руках, оружие не стреляет. Добавляется к игроку автоматически (см. FirstPersonController.Awake).
/// </summary>
public class PropGrabber : MonoBehaviour
{
    public float grabRange = 2.6f;
    [Tooltip("Расстояние от камеры до центра удерживаемого предмета (м) — плюс половина его размера.")]
    public float holdDistance = 1.0f;
    [Tooltip("Скорость броска лёгкого предмета (м/с); тяжёлые летят медленнее.")]
    public float throwSpeed = 14f;
    [Tooltip("Тяжелее этого (кг) поднять нельзя.")]
    public float maxMass = 12f;

    public GrabbableProp Held { get; private set; }
    public GrabbableProp LookProp { get; private set; }
    /// <summary>Оружие не стреляет: предмет в руках или ЛКМ ещё зажата после броска.</summary>
    public bool BlocksFire => Held != null || fireHeldAfterThrow;

    Camera cam;
    CharacterController cc;
    PlayerWeaponHandler weapons;
    Quaternion holdRotation; // поворот предмета относительно камеры (по горизонтали)
    float holdOffset;
    bool fireHeldAfterThrow;

    void Start()
    {
        weapons = GetComponent<PlayerWeaponHandler>();
        cc = GetComponent<CharacterController>();
        cam = weapons != null && weapons.playerCamera != null ? weapons.playerCamera : GetComponentInChildren<Camera>();
    }

    void Update()
    {
        if (PauseMenu.IsPaused || InventoryScreen.IsOpen) return;
        if (fireHeldAfterThrow && !Controls.IsPressed(GameAction.Fire)) fireHeldAfterThrow = false;
        bool locked = Cursor.lockState == CursorLockMode.Locked && !FirstPersonController.InputLocked;
        var health = GetComponent<PlayerHealth>();
        if (health != null && health.IsDead) { if (Held != null) Release(Vector3.zero); return; }

        if (Held != null)
        {
            if (Held == null || !Held.isActiveAndEnabled) { Held = null; return; }
            if (locked && Controls.WasPressed(GameAction.Fire)) { Throw(); return; }
            if (!Controls.IsPressed(GameAction.Interact) || !locked) { Release(cc != null ? cc.velocity : Vector3.zero); return; }
            return;
        }

        LookProp = locked && cam != null && !(weapons != null && weapons.HasInteractTarget) ? FindLook() : null;
        if (LookProp != null && Controls.WasPressed(GameAction.Interact)) Grab(LookProp);
    }

    GrabbableProp FindLook()
    {
        Transform c = cam.transform;
        var hits = Physics.RaycastAll(c.position, c.forward, grabRange, ~0, QueryTriggerInteraction.Ignore);
        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
        foreach (var h in hits)
        {
            if (h.transform.IsChildOf(transform)) continue;
            var rb = h.rigidbody;
            var prop = rb != null ? rb.GetComponent<GrabbableProp>() : null;
            return prop != null && rb.mass <= maxMass ? prop : null; // первое препятствие — либо предмет, либо ничего
        }
        return null;
    }

    void Grab(GrabbableProp p)
    {
        Held = p;
        p.IsHeld = true;
        var rb = p.Body;
        rb.WakeUp();
        rb.useGravity = false;
        rb.linearDamping = 8f;
        rb.angularDamping = 8f;
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        SetIgnorePlayer(p, true);
        // Размер предмета: держим его так, чтобы он не упирался в камеру.
        var col = p.GetComponent<Collider>();
        holdOffset = col != null ? col.bounds.extents.magnitude : 0.2f;
        Vector3 camYaw = Vector3.ProjectOnPlane(cam.transform.forward, Vector3.up);
        if (camYaw.sqrMagnitude < 1e-4f) camYaw = transform.forward;
        holdRotation = Quaternion.Inverse(Quaternion.LookRotation(camYaw)) * rb.rotation;
    }

    void FixedUpdate()
    {
        if (Held == null || cam == null) return;
        var rb = Held.Body;
        Transform c = cam.transform;
        Vector3 target = c.position + c.forward * (holdDistance + holdOffset);
        // Не проталкиваем предмет сквозь стену перед собой.
        if (Physics.SphereCast(c.position, 0.05f, c.forward, out RaycastHit wall, holdDistance + holdOffset, ~0, QueryTriggerInteraction.Ignore)
            && !wall.transform.IsChildOf(transform) && wall.rigidbody != rb)
            target = c.position + c.forward * Mathf.Max(0.35f, wall.distance - holdOffset * 0.6f);

        Vector3 delta = target - rb.position;
        // Застрял (зажат мебелью) — выпускаем.
        if (delta.magnitude > 1.6f) { Release(Vector3.zero); return; }
        rb.linearVelocity = Vector3.ClampMagnitude(delta / Time.fixedDeltaTime * 0.5f, 15f);

        Vector3 camYaw = Vector3.ProjectOnPlane(c.forward, Vector3.up);
        if (camYaw.sqrMagnitude > 1e-4f)
        {
            Quaternion want = Quaternion.LookRotation(camYaw) * holdRotation;
            Quaternion diff = want * Quaternion.Inverse(rb.rotation);
            diff.ToAngleAxis(out float angle, out Vector3 axis);
            if (angle > 180f) angle -= 360f;
            if (!float.IsNaN(axis.x) && Mathf.Abs(angle) > 0.1f)
                rb.angularVelocity = Vector3.ClampMagnitude(axis * (angle * Mathf.Deg2Rad / Time.fixedDeltaTime * 0.3f), 12f);
        }
    }

    void Release(Vector3 inherit)
    {
        var p = Held;
        Held = null;
        if (p == null) return;
        p.IsHeld = false;
        var rb = p.Body;
        rb.useGravity = true;
        rb.linearDamping = 0f;
        rb.angularDamping = 0.05f;
        rb.linearVelocity = Vector3.ClampMagnitude(rb.linearVelocity, 3f) + inherit * 0.5f;
        StartCoroutine(RestorePlayerCollision(p));
    }

    void Throw()
    {
        var p = Held;
        Release(Vector3.zero);
        if (p == null) return;
        var rb = p.Body;
        float speed = throwSpeed * Mathf.Lerp(1f, 0.4f, Mathf.InverseLerp(0.5f, maxMass, rb.mass));
        Vector3 playerVel = cc != null ? cc.velocity : Vector3.zero;
        rb.linearVelocity = cam.transform.forward * speed + Vector3.up * 0.8f + playerVel;
        rb.angularVelocity = cam.transform.TransformDirection(new Vector3(Random.Range(2f, 6f), Random.Range(-3f, 3f), Random.Range(-2f, 2f)));
        p.ThrownUntil = Time.time + 2.5f;
        p.SoundUntil = Time.time + 5f;
        fireHeldAfterThrow = true;
    }

    IEnumerator RestorePlayerCollision(GrabbableProp p)
    {
        // Сразу после броска предмет ещё у лица — столкновение с игроком включаем, когда он отлетит.
        yield return new WaitForSeconds(0.6f);
        while (p != null && !p.IsHeld && cc != null && p.GetComponent<Collider>() is Collider col
               && col.bounds.Intersects(cc.bounds))
            yield return new WaitForSeconds(0.2f);
        if (p != null && !p.IsHeld) SetIgnorePlayer(p, false);
    }

    void SetIgnorePlayer(GrabbableProp p, bool ignore)
    {
        if (cc == null || p == null) return;
        foreach (var col in p.GetComponentsInChildren<Collider>())
            Physics.IgnoreCollision(col, cc, ignore);
    }

    void OnDisable()
    {
        if (Held != null) Release(Vector3.zero);
    }
}
