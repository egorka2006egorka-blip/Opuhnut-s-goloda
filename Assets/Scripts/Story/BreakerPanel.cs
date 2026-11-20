using UnityEngine;

/// <summary>
/// Мини-игра 1 (комната ИБП): щиток с рубильниками охранной системы. Над каждым рычагом — лампа: красная — цепь под
/// напряжением, зелёная — отключена. Рычаг переключает свою цепь И соседнюю справа (последний — первую).
/// Нужно погасить все красные. Начальное состояние — несколько случайных нажатий от решённого (всегда решаемо).
/// Начало координат — середина щитка, +Z — к игроку (лицевая сторона), +Y — вверх.
/// </summary>
public class BreakerPanel : MonoBehaviour
{
    public int count = 5;

    bool[] live;
    BreakerLever[] levers;
    Renderer[] lamps;
    Material red, green;
    AudioSource audioSrc;
    bool solved;

    public Vector3 Focus => transform.position + transform.up * 0.15f;

    void Awake()
    {
        audioSrc = gameObject.AddComponent<AudioSource>();
        audioSrc.spatialBlend = 1f;
        audioSrc.minDistance = 2f;
        audioSrc.maxDistance = 20f;
        red = Interactable.Mat(new Color(0.9f, 0.08f, 0.05f), 0.6f, 0f, new Color(2.2f, 0.15f, 0.1f));
        green = Interactable.Mat(new Color(0.1f, 0.85f, 0.2f), 0.6f, 0f, new Color(0.15f, 1.8f, 0.3f));
        Build();

        // Случайная задача: 2–3 разных нажатия от решённого состояния.
        live = new bool[count];
        int presses = Random.Range(2, 4);
        var used = new System.Collections.Generic.HashSet<int>();
        while (used.Count < presses) used.Add(Random.Range(0, count));
        foreach (int i in used) Flip(i);
        if (AllOff()) Flip(0);
        Refresh();
    }

    void Build()
    {
        var box = Interactable.Mat(new Color(0.32f, 0.34f, 0.36f), 0.35f, 0.6f);
        var dark = Interactable.Mat(new Color(0.08f, 0.08f, 0.09f), 0.4f);
        var stripe = Interactable.Mat(new Color(0.95f, 0.75f, 0.1f), 0.3f);
        float w = 0.24f * count + 0.2f;
        Interactable.Part(transform, PrimitiveType.Cube, new Vector3(0f, 0f, -0.06f), new Vector3(w, 0.8f, 0.1f), box, default, true);
        Interactable.Part(transform, PrimitiveType.Cube, new Vector3(0f, 0.37f, 0f), new Vector3(w, 0.04f, 0.012f), stripe);
        Interactable.Part(transform, PrimitiveType.Cube, new Vector3(0f, -0.37f, 0f), new Vector3(w, 0.04f, 0.012f), stripe);
        levers = new BreakerLever[count];
        lamps = new Renderer[count];
        for (int i = 0; i < count; i++)
        {
            float x = (i - (count - 1) / 2f) * 0.24f;
            lamps[i] = Interactable.Part(transform, PrimitiveType.Sphere, new Vector3(x, 0.25f, 0.01f), new Vector3(0.06f, 0.06f, 0.04f), red).GetComponent<Renderer>();
            // Номер цепи — штрихи под лампой.
            for (int k = 0; k <= i; k++)
                Interactable.Part(transform, PrimitiveType.Cube, new Vector3(x - 0.04f * i / 2f + 0.04f * k, 0.17f, 0.002f), new Vector3(0.012f, 0.04f, 0.004f), dark);
            Interactable.Part(transform, PrimitiveType.Cube, new Vector3(x, -0.08f, 0f), new Vector3(0.09f, 0.34f, 0.02f), dark); // прорезь
            var go = new GameObject("Lever_" + (i + 1));
            go.transform.SetParent(transform, false);
            go.transform.localPosition = new Vector3(x, -0.08f, 0.02f);
            var lever = go.AddComponent<BreakerLever>();
            lever.Setup(this, i);
            levers[i] = lever;
        }
    }

    void Flip(int i)
    {
        live[i] = !live[i];
        live[(i + 1) % count] = !live[(i + 1) % count];
    }

    bool AllOff()
    {
        foreach (bool b in live) if (b) return false;
        return true;
    }

    void Refresh()
    {
        for (int i = 0; i < count; i++) lamps[i].sharedMaterial = live[i] ? red : green;
    }

    public bool Solved => solved;
    public bool Usable => !solved && ServerLockdown.Instance != null && ServerLockdown.Instance.Active;

    public void Press(int i)
    {
        if (!Usable) return;
        Flip(i);
        levers[i].Toggle();
        Refresh();
        audioSrc.pitch = Random.Range(0.9f, 1.1f);
        audioSrc.PlayOneShot(ServerLockdown.Clunk, GameSettings.EffectsVolume);
        if (!AllOff()) return;
        solved = true;
        audioSrc.pitch = 1f;
        audioSrc.PlayOneShot(ServerLockdown.Ok, GameSettings.EffectsVolume);
        ServerLockdown.Instance.Solve(ServerLockdown.Task.Breakers);
    }

    public string LeverPrompt(int i) => solved ? "Охрана обесточена"
        : "Рубильник " + (i + 1) + " (переключает цепи " + (i + 1) + " и " + ((i + 1) % count + 1) + ") — погаси все красные лампы";
}
