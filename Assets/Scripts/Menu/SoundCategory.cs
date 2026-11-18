using UnityEngine;

/// <summary>Категория звука: у каждой своя громкость в настройках (поверх общей громкости).</summary>
public enum SoundKind { Effects, Music, Voice }

/// <summary>
/// Вешается на объект с AudioSource: громкость источника = baseVolume × громкость категории.
/// Для музыки ставьте kind = Music, для реплик персонажей — Voice.
/// Звуки через PlayOneShot / PlayClipAtPoint из кода масштабируются через GameSettings.CategoryVolume.
/// </summary>
[RequireComponent(typeof(AudioSource))]
public class SoundCategory : MonoBehaviour
{
    public SoundKind kind = SoundKind.Effects;
    [Tooltip("Громкость источника при 100% в настройках. Меньше 0 — взять из AudioSource при запуске.")]
    public float baseVolume = -1f;

    AudioSource source;

    void OnEnable()
    {
        source = GetComponent<AudioSource>();
        if (baseVolume < 0f) baseVolume = source.volume;
        GameSettings.VolumeChanged += Apply;
        Apply();
    }

    void OnDisable() => GameSettings.VolumeChanged -= Apply;

    void Apply()
    {
        if (source != null) source.volume = baseVolume * GameSettings.CategoryVolume(kind);
    }
}
