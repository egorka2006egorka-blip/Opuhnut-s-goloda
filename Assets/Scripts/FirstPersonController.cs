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

    CharacterController controller;
    Vector3 horizontalVelocity;
    float verticalVelocity;
    float bobBlend;
    float recoilTarget, recoilPitch;

    void Awake()
    {
        controller = GetComponent<CharacterController>();
        if (cameraPivot != null)
            Pitch = NormalizeAngle(cameraPivot.localEulerAngles.x);
    }

    void Start() => LockCursor(true);

    void Update()
    {
        var keyboard = Keyboard.current;
        var mouse = Mouse.current;

        // Курсор: Esc — отпустить, клик — снова захватить.
        if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame) LockCursor(false);
        if (mouse != null && mouse.leftButton.wasPressedThisFrame && Cursor.lockState != CursorLockMode.Locked) LockCursor(true);

        bool lookActive = Cursor.lockState == CursorLockMode.Locked;
        if (keyboard != null && lookActive && keyboard.vKey.wasPressedThisFrame) ThirdPerson = !ThirdPerson;

        // Обзор мышью.
        if (mouse != null && lookActive)
        {
            Vector2 delta = mouse.delta.ReadValue() * mouseSensitivity * LookSensitivityScale;
            transform.Rotate(0f, delta.x, 0f, Space.Self);
            Pitch = Mathf.Clamp(Pitch - delta.y, minPitch, maxPitch);
        }

        // Ввод движения.
        Vector2 input = Vector2.zero;
        bool sprint = false, jump = false;
        if (keyboard != null && lookActive)
        {
            if (keyboard.wKey.isPressed) input.y += 1f;
            if (keyboard.sKey.isPressed) input.y -= 1f;
            if (keyboard.dKey.isPressed) input.x += 1f;
            if (keyboard.aKey.isPressed) input.x -= 1f;
            sprint = keyboard.leftShiftKey.isPressed && input.y > 0f;
            jump = keyboard.spaceKey.wasPressedThisFrame;
        }
        input = Vector2.ClampMagnitude(input, 1f);

        float targetSpeed = sprint ? sprintSpeed : walkSpeed;
        Vector3 wishVelocity = (transform.right * input.x + transform.forward * input.y) * targetSpeed;
        float accel = controller.isGrounded ? acceleration : acceleration * 0.3f;
        horizontalVelocity = Vector3.MoveTowards(horizontalVelocity, wishVelocity, accel * targetSpeed * Time.deltaTime);

        // Гравитация и прыжок.
        if (controller.isGrounded)
        {
            if (verticalVelocity < 0f) verticalVelocity = -2f;
            if (jump) verticalVelocity = Mathf.Sqrt(jumpHeight * -2f * gravity);
        }
        verticalVelocity += gravity * Time.deltaTime;

        Vector3 motion = horizontalVelocity + Vector3.up * verticalVelocity;
        controller.Move(motion * Time.deltaTime);

        // Фактическая скорость (учитывает упор в стены).
        Vector3 v = controller.velocity; v.y = 0f;
        HorizontalSpeed = v.magnitude;
        float targetMove = controller.isGrounded ? Mathf.Clamp01(HorizontalSpeed / sprintSpeed * 1.4f) : 0f;
        MoveAmount = Mathf.MoveTowards(MoveAmount, targetMove, Time.deltaTime * 4f);
        StepPhase += HorizontalSpeed / strideLength * Mathf.PI * Time.deltaTime;
        if (StepPhase > Mathf.PI * 200f) StepPhase -= Mathf.PI * 200f;

        UpdateCamera();
    }

    void UpdateCamera()
    {
        if (cameraPivot == null) return;
        bobBlend = Mathf.MoveTowards(bobBlend, MoveAmount, Time.deltaTime * 3f);
        // Голова опускается при каждом шаге (два раза за цикл) и чуть качается в стороны.
        float bobY = -Mathf.Abs(Mathf.Sin(StepPhase)) * bobAmount * bobBlend;
        float bobX = Mathf.Cos(StepPhase) * bobAmount * 0.4f * bobBlend;
        cameraPivot.localPosition = new Vector3(bobX, eyeHeight + bobY, 0f);
        // Отдача: камера быстро подбрасывается и плавно возвращается.
        recoilTarget = Mathf.MoveTowards(recoilTarget, 0f, Time.deltaTime * 9f);
        recoilPitch = Mathf.Lerp(recoilPitch, recoilTarget, Time.deltaTime * 25f);
        cameraPivot.localRotation = Quaternion.Euler(Pitch - recoilPitch, 0f, recoilPitch * 0.15f);

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

    /// <summary>Отдача от выстрела: вверх (градусы) и случайно вбок.</summary>
    public void AddRecoil(float up, float side)
    {
        recoilTarget = Mathf.Min(recoilTarget + up, 8f);
        transform.Rotate(0f, side, 0f, Space.Self);
    }

    static void LockCursor(bool locked)
    {
        Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
        Cursor.visible = !locked;
    }

    static float NormalizeAngle(float a) => a > 180f ? a - 360f : a;
}
