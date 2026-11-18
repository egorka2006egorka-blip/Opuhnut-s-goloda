using UnityEngine;

/// <summary>
/// Камера фона главного меню: медленно «дышит» — едва заметно покачивается и плавно наезжает и отъезжает.
/// Положение в сцене — исходная точка; двигать камеру можно прямо в сцене MainMenu.
/// </summary>
public class MenuCameraDrift : MonoBehaviour
{
    [Tooltip("Амплитуда покачивания (м).")]
    public float swayAmount = 0.05f;
    [Tooltip("Амплитуда поворота при покачивании (градусы).")]
    public float swayAngle = 0.6f;
    [Tooltip("Скорость покачивания.")]
    public float swaySpeed = 0.12f;
    [Tooltip("Наезд вперёд-назад (м).")]
    public float dollyAmount = 0.35f;
    [Tooltip("Период наезда (с).")]
    public float dollyPeriod = 40f;

    Vector3 startPos;
    Quaternion startRot;

    void Start()
    {
        startPos = transform.position;
        startRot = transform.rotation;
    }

    void LateUpdate()
    {
        float t = Time.unscaledTime;
        float nx = Mathf.PerlinNoise(t * swaySpeed, 0.1f) * 2f - 1f;
        float ny = Mathf.PerlinNoise(0.7f, t * swaySpeed) * 2f - 1f;
        float nr = Mathf.PerlinNoise(t * swaySpeed * 0.7f, 5.3f) * 2f - 1f;
        float dolly = Mathf.Sin(t * Mathf.PI * 2f / Mathf.Max(1f, dollyPeriod)) * dollyAmount;

        transform.position = startPos
            + startRot * new Vector3(nx * swayAmount, ny * swayAmount * 0.6f, dolly);
        transform.rotation = startRot * Quaternion.Euler(ny * swayAngle, nx * swayAngle, nr * swayAngle * 0.5f);
    }
}
