using UnityEngine;

/// <summary>
/// Мини-игра 2: заражённый сервер в стойке — блок выдвинут, мигает красным и светится (его видно из прохода).
/// Нужно найти все такие в рядах стоек и выдернуть: несколько рывков на E, блок вылетает на пол.
/// Начало координат — на лицевой стороне стойки, +Z — в проход.
/// </summary>
public class InfectedServer : Interactable
{
    public int tugsNeeded = 3;

    public bool Pulled { get; private set; }
    Transform blade;
    Light glow;
    Renderer[] leds;
    Material ledOn, ledOff;
    int tugs;
    AudioSource audioSrc;

    public override string Prompt => "Выдернуть заражённый сервер (рывков: " + tugs + " из " + tugsNeeded + ")";
    public override bool Available => !Pulled && ServerLockdown.Instance != null && ServerLockdown.Instance.Active;
    public override Vector3 Focus => blade != null ? blade.position : transform.position;

    void Awake()
    {
        range = 2.2f;
        audioSrc = gameObject.AddComponent<AudioSource>();
        audioSrc.spatialBlend = 1f;
        audioSrc.minDistance = 1.5f;
        audioSrc.maxDistance = 18f;
        var body = Mat(new Color(0.12f, 0.12f, 0.13f), 0.45f, 0.5f);
        var handle = Mat(new Color(0.6f, 0.6f, 0.62f), 0.6f, 0.9f);
        ledOn = Mat(new Color(1f, 0.1f, 0.05f), 0.6f, 0f, new Color(3f, 0.2f, 0.1f));
        ledOff = Mat(new Color(0.25f, 0.05f, 0.04f), 0.5f);

        blade = Node(transform, "Blade", new Vector3(0f, 0f, 0.06f));
        Part(blade, PrimitiveType.Cube, Vector3.zero, new Vector3(0.44f, 0.09f, 0.16f), body, default, true);
        foreach (float x in new[] { -0.19f, 0.19f })
            Part(blade, PrimitiveType.Cube, new Vector3(x, 0f, 0.095f), new Vector3(0.025f, 0.06f, 0.03f), handle);
        leds = new Renderer[4];
        for (int i = 0; i < leds.Length; i++)
            leds[i] = Part(blade, PrimitiveType.Cube, new Vector3(-0.09f + i * 0.045f, 0.015f, 0.081f), new Vector3(0.022f, 0.012f, 0.004f), ledOn).GetComponent<Renderer>();
        // Наклейка «заражён» — жёлто-чёрная.
        Part(blade, PrimitiveType.Cube, new Vector3(0.09f, -0.02f, 0.081f), new Vector3(0.08f, 0.03f, 0.003f), Mat(new Color(0.95f, 0.8f, 0.1f), 0.3f));

        glow = new GameObject("Glow").AddComponent<Light>();
        glow.transform.SetParent(transform, false);
        glow.transform.localPosition = new Vector3(0f, 0.05f, 0.45f);
        glow.type = LightType.Point;
        glow.color = new Color(1f, 0.12f, 0.08f);
        glow.range = 3f;
        glow.intensity = 2.2f;
        glow.shadows = LightShadows.None;
        glow.gameObject.AddComponent<FlickerLight>().pulse = 1.6f;
    }

    void Update()
    {
        if (Pulled || leds == null) return;
        // Светодиоды бегут по кругу — тревожное мигание.
        int on = (int)(Time.time * 8f) % leds.Length;
        for (int i = 0; i < leds.Length; i++) leds[i].sharedMaterial = i == on || (Time.time * 2f % 1f) < 0.5f ? ledOn : ledOff;
    }

    public override void Interact(GameObject player)
    {
        if (!Available) return;
        tugs++;
        var fpc = player.GetComponent<FirstPersonController>();
        if (fpc != null) fpc.AddShake(0.2f);
        audioSrc.pitch = Random.Range(0.9f, 1.15f);
        if (tugs < tugsNeeded)
        {
            audioSrc.PlayOneShot(ServerLockdown.Clunk, GameSettings.EffectsVolume * 0.8f);
            blade.localPosition += Vector3.forward * 0.07f;
            return;
        }
        Pulled = true;
        audioSrc.PlayOneShot(ServerLockdown.Fail, GameSettings.EffectsVolume); // треск замыкания
        foreach (var r in leds) r.sharedMaterial = ledOff;
        // Искра и вспышка, потом свет гаснет.
        glow.GetComponent<FlickerLight>().enabled = false;
        glow.color = new Color(0.7f, 0.85f, 1f);
        glow.intensity = 4f;
        Destroy(glow.gameObject, 0.12f);
        // Блок вылетает в проход и падает (сквозь игрока).
        blade.SetParent(null, true);
        var rb = blade.gameObject.AddComponent<Rigidbody>();
        rb.mass = 6f;
        rb.linearVelocity = transform.forward * 2.2f + Vector3.up * 0.8f;
        rb.angularVelocity = Random.insideUnitSphere * 3f;
        var cc = player.GetComponent<CharacterController>();
        if (cc != null) foreach (var c in blade.GetComponentsInChildren<Collider>()) Physics.IgnoreCollision(c, cc);
        if (ServerLockdown.Instance != null) ServerLockdown.Instance.OnServerPulled();
    }
}
