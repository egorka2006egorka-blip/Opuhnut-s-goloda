using System.Collections;
using UnityEngine;

/// <summary>
/// Фоновая музыка главного меню: зациклена, плавно появляется при открытии меню и затихает перед загрузкой игры.
/// Громкость = volume × ползунок «Музыка» в настройках (и общая громкость через AudioListener).
/// </summary>
[RequireComponent(typeof(AudioSource))]
public class MenuMusic : MonoBehaviour
{
    [Range(0f, 1f)] public float volume = 0.8f;
    [Tooltip("Время появления музыки (с).")]
    public float fadeInTime = 6f;

    static MenuMusic current;
    AudioSource source;
    float fade, fadeTarget = 1f, fadeSpeed;

    void Awake()
    {
        current = this;
        source = GetComponent<AudioSource>();
        source.loop = true;
        source.playOnAwake = false;
        source.spatialBlend = 0f;
        source.volume = 0f;
        fadeSpeed = 1f / Mathf.Max(0.01f, fadeInTime);
    }

    void Start()
    {
        if (source.clip != null) source.Play();
    }

    void OnDestroy()
    {
        if (current == this) current = null;
    }

    void Update()
    {
        fade = Mathf.MoveTowards(fade, fadeTarget, Time.unscaledDeltaTime * fadeSpeed);
        source.volume = volume * fade * GameSettings.MusicVolume;
    }

    /// <summary>Плавно заглушить музыку меню (ждать в корутине).</summary>
    public static IEnumerator FadeOut(float duration)
    {
        if (current == null) yield break;
        current.fadeTarget = 0f;
        current.fadeSpeed = 1f / Mathf.Max(0.01f, duration);
        while (current != null && current.fade > 0.001f) yield return null;
    }
}
