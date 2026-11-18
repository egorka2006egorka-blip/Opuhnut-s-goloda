using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Простое управление от первого лица: WASD, Shift — бег, Space — прыжок, мышь — обзор.
/// Тело поворачивается только по горизонтали, камера (cameraPivot) — по вертикали.
/// </summary>
[RequireComponent(typeof(CharacterController))]
public class FirstPersonController : MonoBehaviour
{
    [Header("Ссылки")]
    public Transform cameraPivot;
    [Tooltip("Камера (дочерний объект cameraPivot). Нужна для вида от 3-го лица.")]
    public Transform playerCamera;

    [Header("Вид от 3-го лица (клавиша V)")]
    public Vector3 thirdPersonOffset = new Vector3(0.45f, 0.15f, -2.2f);

    [Header("Движение")]
    public float walkSpeed = 3.2f;
    public float sprintSpeed = 5.5f;
    public float acceleration = 12f;
    public float jumpHeight = 1.0f;
    public float gravity = -20f;
    [Tooltip("На спуске по лестнице и наклону персонаж прижимается к полу, если тот не дальше этого (м).")]
    public float groundSnap = 0.4f;

    [Header("Обзор")]
    public float mouseSensitivity = 0.12f;
    public float minPitch = -80f;
    public float maxPitch = 80f;

    [Header("Покачивание головы")]
    public float eyeHeight = 1.65f;
    public float strideLength = 1.5f;
    public float bobAmount = 0.035f;

    public float Pitch { get; private set; }
    public float HorizontalSpeed { get; private set; }
    /// <summary>Фаза шага в радианах — по ней качаются руки, ноги и голова.</summary>
    public float StepPhase { get; private set; }
    /// <summary>0..1 — насколько активно персонаж идёт (0 — стоит, 1 — бег).</summary>
    public float MoveAmount { get; private set; }
    public bool IsGrounded => controller.isGrounded;
    public bool ThirdPerson { get; private set; }
    /// <summary>Множитель чувствительности мыши (меньше 1 при прицеливании).</summary>
    public float LookSensitivityScale { get; set; } = 1f;

    /// <summary>Катсцена: управление (ходьба, обзор, стрельба, E) отключено, камерой управляет StoryDirector.</summary>
    public static bool InputLocked { get; set; }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { InputLocked = false; }
    /// <summary>Сдвиг камеры по высоте (м) и крен (°) — для катсцен (герой лежит головой на столе).</summary>
    public float EyeOffset { get; set; }
    public float CameraRoll { get; set; }

    CharacterController controller;
    Vector3 horizontalVelocity;
    float verticalVelocity;
    float bobBlend;
    float recoilTarget, recoilPitch;
    float shake;

    /// <summary>Пол под ногами не дальше maxGap (м): зазор до него и нормаль. Свои коллайдеры не считаются.</summary>
    bool ProbeGround(float maxGap, out float gap, out Vector3 normal)
    {
        float half = controller.height / 2f - controller.radius;
        Vector3 center = transform.TransformPoint(controller.center);
        gap = float.MaxValue;
        normal = Vector3.up;
        foreach (var hit in Physics.SphereCastAll(center, controller.radius * 0.95f, Vector3.down, half + maxGap, ~0, QueryTriggerInteraction.Ignore))
        {
            if (hit.collider == controller || hit.transform.IsChildOf(transform) || hit.distance <= 0f) continue;
            if (hit.distance - half < gap) { gap = hit.distance - half; normal = hit.normal; }
        }
        return gap < float.MaxValue;
    }

    /// <summary>Шаг вниз по наклону оторвал от пола — опускаемся на пол, если он близко под ногами.</summary>
    void SnapToGround()
    {
        if (controller.isGrounded) return;
        if (ProbeGround(groundSnap, out float gap, out _))
            controller.Move(Vector3.down * (gap + controller.skinWidth));
    }

    void Awake()
    {
        controller = GetComponent<CharacterController>();
        if (GetComponent<PlayerHealth>() == null) gameObject.AddComponent<PlayerHealth>();
        if (GetComponent<Inventory>() == null) gameObject.AddComponent<Inventory>();
        if (GetComponent<InventoryScreen>() == null) gameObject.AddComponent<InventoryScreen>();
        if (GetComponent<PropGrabber>() == null) gameObject.AddComponent<PropGrabber>();
        if (cameraPivot != null)
            Pitch = NormalizeAngle(cameraPivot.localEulerAngles.x);
    }

    void Start() => LockCursor(true);

    void Update()
    {
        // Пауза: управление отключено, курсор отдан меню (Esc обрабатывает PauseMenu).
        if (PauseMenu.IsPaused) return;
        var mouse = Mouse.current;

        // Курсор: клик — снова захватить (если его отпустили, например, сменой окна). В инвентаре курсор нужен для меню.
        if (mouse != null && mouse.leftButton.wasPressedThisFrame && Cursor.lockState != CursorLockMode.Locked && !InventoryScreen.IsOpen && !Shopkeeper.IsShopOpen && !StoryDirector.CursorNeeded) LockCursor(true);

        bool lookActive = Cursor.lockState == CursorLockMode.Locked && !InputLocked;
        if (lookActive && Controls.WasPressed(GameAction.ToggleView)) ThirdPerson = !ThirdPerson;

        // Обзор мышью.
        if (mouse != null && lookActive)
        {
            Vector2 delta = mouse.delta.ReadValue() * mouseSensitivity * LookSensitivityScale * GameSettings.MouseSensitivity;
            transform.Rotate(0f, delta.x, 0f, Space.Self);
            Pitch = Mathf.Clamp(Pitch - delta.y, minPitch, maxPitch);
        }

        // Ввод движения.
        Vector2 input = Vector2.zero;
        bool sprint = false, jump = false;
        if (lookActive)
        {
            if (Controls.IsPressed(GameAction.MoveForward)) input.y += 1f;
            if (Controls.IsPressed(GameAction.MoveBack)) input.y -= 1f;
            if (Controls.IsPressed(GameAction.MoveRight)) input.x += 1f;
            if (Controls.IsPressed(GameAction.MoveLeft)) input.x -= 1f;
            sprint = Controls.IsPressed(GameAction.Sprint) && input.y > 0f;
            jump = Controls.WasPressed(GameAction.Jump);
        }
        Locomotion(input, sprint, jump, Time.deltaTime);
        UpdateCamera();
    }

    /// <summary>Шаг движения за dt: ввод (x — вбок, y — вперёд), бег, прыжок.</summary>
    public void Locomotion(Vector2 input, bool sprint, bool jump, float dt)
    {
        if (controller == null) controller = GetComponent<CharacterController>();
        input = Vector2.ClampMagnitude(input, 1f);

        float targetSpeed = sprint ? sprintSpeed : walkSpeed;
        Vector3 wishVelocity = (transform.right * input.x + transform.forward * input.y) * targetSpeed;
        float accel = controller.isGrounded ? acceleration : acceleration * 0.3f;
        horizontalVelocity = Vector3.MoveTowards(horizontalVelocity, wishVelocity, accel * targetSpeed * dt);

        // Гравитация и прыжок.
        if (controller.isGrounded)
        {
            if (verticalVelocity < 0f) verticalVelocity = -2f;
            if (jump) verticalVelocity = Mathf.Sqrt(jumpHeight * -2f * gravity);
        }
        verticalVelocity += gravity * dt;

        Vector3 motion = horizontalVelocity + Vector3.up * verticalVelocity;
        // На лестнице и наклоне идём вдоль поверхности: горизонтальная скорость та же, что на ровном полу,
        // и вверх, и вниз (иначе вниз капсула слетает с наклона, а вверх упирается в него).
        if (controller.isGrounded && verticalVelocity < 0f
            && ProbeGround(controller.skinWidth + 0.1f, out _, out Vector3 groundNormal)
            && groundNormal.y > Mathf.Cos(controller.slopeLimit * Mathf.Deg2Rad))
            motion.y -= Vector3.Dot(horizontalVelocity, groundNormal) / groundNormal.y;
        bool wasGrounded = controller.isGrounded;
        Vector3 start = transform.position;
        controller.Move(motion * dt);
        if (wasGrounded && verticalVelocity < 0f) SnapToGround();

        // Фактическая скорость за кадр (учитывает упор в стены). controller.velocity не годится:
        // после прижима к полу в нём только движение вниз, и на спуске шаг бы замирал.
        Vector3 v = transform.position - start; v.y = 0f;
        HorizontalSpeed = dt > 0f ? v.magnitude / dt : 0f;
        float targetMove = controller.isGrounded ? Mathf.Clamp01(HorizontalSpeed / sprintSpeed * 1.4f) : 0f;
        MoveAmount = Mathf.MoveTowards(MoveAmount, targetMove, dt * 4f);
        StepPhase += HorizontalSpeed / strideLength * Mathf.PI * dt;
        if (StepPhase > Mathf.PI * 200f) StepPhase -= Mathf.PI * 200f;
    }

    void UpdateCamera()
    {
        if (cameraPivot == null) return;
        bobBlend = Mathf.MoveTowards(bobBlend, MoveAmount, Time.deltaTime * 3f);
        // Голова опускается при каждом шаге (два раза за цикл) и чуть качается в стороны.
        float bobY = -Mathf.Abs(Mathf.Sin(StepPhase)) * bobAmount * bobBlend;
        float bobX = Mathf.Cos(StepPhase) * bobAmount * 0.4f * bobBlend;
        cameraPivot.localPosition = new Vector3(bobX, eyeHeight + bobY + EyeOffset, 0f);
        // Отдача: камера быстро подбрасывается и плавно возвращается.
        recoilTarget = Mathf.MoveTowards(recoilTarget, 0f, Time.deltaTime * 9f);
        recoilPitch = Mathf.Lerp(recoilPitch, recoilTarget, Time.deltaTime * 25f);
        // Тряска от выстрела: быстрое шумовое дрожание, затухающее за доли секунды.
        shake = Mathf.MoveTowards(shake, 0f, Time.deltaTime * 5f);
        float shakeX = (Mathf.PerlinNoise(Time.time * 40f, 0.3f) - 0.5f) * 2f * shake;
        float shakeY = (Mathf.PerlinNoise(0.7f, Time.time * 40f) - 0.5f) * 2f * shake;
        cameraPivot.localRotation = Quaternion.Euler(Pitch - recoilPitch + shakeX, shakeY, recoilPitch * 0.15f + shakeY * 0.5f + CameraRoll);

        if (playerCamera != null)
        {
            Vector3 target = Vector3.zero;
            if (ThirdPerson)
            {
                // Камера позади и сбоку; не даём ей уйти в стену.
                Vector3 worldOffset = cameraPivot.TransformVector(thirdPersonOffset);
                float dist = worldOffset.magnitude;
                if (Physics.SphereCast(cameraPivot.position, 0.15f, worldOffset / dist, out RaycastHit hit, dist, ~0, QueryTriggerInteraction.Ignore)
                    && !hit.transform.IsChildOf(transform))
                    target = thirdPersonOffset * Mathf.Max(0f, (hit.distance - 0.05f) / dist);
                else
                    target = thirdPersonOffset;
            }
            playerCamera.localPosition = Vector3.Lerp(playerCamera.localPosition, target, Time.deltaTime * 12f);
        }
    }

    /// <summary>Катсцена: плавно повернуть взгляд на точку (sharpness — скорость, 1/с).</summary>
    public void LookAt(Vector3 point, float sharpness, float dt)
    {
        Vector3 from = cameraPivot != null ? cameraPivot.position : transform.position + Vector3.up * eyeHeight;
        Vector3 d = point - from;
        Vector3 flat = new Vector3(d.x, 0f, d.z);
        float k = 1f - Mathf.Exp(-sharpness * dt);
        if (flat.sqrMagnitude > 1e-4f)
            transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(flat), k);
        float pitch = -Mathf.Atan2(d.y, flat.magnitude) * Mathf.Rad2Deg;
        Pitch = Mathf.Clamp(Mathf.LerpAngle(Pitch, pitch, k), minPitch, maxPitch);
    }

    public void SetPitch(float pitch) => Pitch = Mathf.Clamp(pitch, minPitch, maxPitch);

    /// <summary>Отдача от выстрела: вверх (градусы) и случайно вбок.</summary>
    public void AddRecoil(float up, float side)
    {
        recoilTarget = Mathf.Min(recoilTarget + up, 8f);
        transform.Rotate(0f, side, 0f, Space.Self);
    }

    /// <summary>Толчок (удар босса): горизонтальная скорость добавляется и гасится трением, вертикальная — подбрасывает.</summary>
    public void AddImpulse(Vector3 velocity)
    {
        horizontalVelocity += new Vector3(velocity.x, 0f, velocity.z);
        if (velocity.y > 0f) verticalVelocity = Mathf.Max(verticalVelocity, velocity.y);
    }

    /// <summary>Тряска камеры (амплитуда в градусах).</summary>
    public void AddShake(float amount)
    {
        shake = Mathf.Min(shake + amount, 1.5f);
    }

    /// <summary>Мгновенно ставит игрока в точку с заданным поворотом (загрузка сохранения).</summary>
    public void Teleport(Vector3 position, float yaw, float pitch)
    {
        if (controller == null) controller = GetComponent<CharacterController>();
        bool was = controller.enabled;
        controller.enabled = false;
        transform.SetPositionAndRotation(position, Quaternion.Euler(0f, yaw, 0f));
        controller.enabled = was;
        Pitch = Mathf.Clamp(pitch, minPitch, maxPitch);
        horizontalVelocity = Vector3.zero;
        verticalVelocity = 0f;
    }

    public static void LockCursor(bool locked)
    {
        Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
        Cursor.visible = !locked;
    }

    static float NormalizeAngle(float a) => a > 180f ? a - 360f : a;
}
