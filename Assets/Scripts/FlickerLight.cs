using UnityEngine;

/// <summary>Мерцающая лампа (подвал): неровное подрагивание яркости и редкие провалы; pulse — плавная пульсация (аварийная лампа).</summary>
[RequireComponent(typeof(Light))]
public class FlickerLight : MonoBehaviour
{
    [Tooltip("0 — ровный свет, 1 — сильное мерцание (неисправная лампа).")]
    [Range(0f, 1f)] public float flicker = 0.15f;
    [Tooltip("Плавная пульсация (раз в секунду), 0 — нет.")]
    public float pulse;

    Light lamp;
    float baseIntensity, seed, dropUntil, nextDrop;

    void Awake()
    {
        lamp = GetComponent<Light>();
        baseIntensity = lamp.intensity;
        seed = Random.Range(0f, 100f);
        nextDrop = Time.time + Random.Range(2f, 8f);
    }

    void Update()
    {
        float t = Time.time;
        float k = 1f - flicker * 0.35f * Mathf.PerlinNoise(seed, t * 9f);
        if (flicker > 0.4f)
        {
            // Неисправная лампа иногда почти гаснет на долю секунды.
            if (t > nextDrop) { dropUntil = t + Random.Range(0.05f, 0.35f); nextDrop = t + Random.Range(0.5f, 4f) / flicker; }
            if (t < dropUntil) k *= 0.1f;
        }
        if (pulse > 0f) k *= 0.35f + 0.65f * (0.5f + 0.5f * Mathf.Sin(t * pulse * Mathf.PI * 2f));
        lamp.intensity = baseIntensity * k;
    }
}
