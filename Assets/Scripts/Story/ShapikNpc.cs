using UnityEngine;

/// <summary>
/// Шапик — друг героя, «запертый» в серверной за подвалом. Сидит за столом с ноутбуком и монитором (карта зомби), в финале
/// встаёт, смотрит на игрока и протягивает руку. Модель — из примитивов (если у объекта нет своей).
/// Объект ставит CanteenSceneBuilder: начало координат — на полу, где он стоит, лицом (+Z) к двери; стол перед ним.
/// </summary>
public class ShapikNpc : MonoBehaviour
{
    public enum PoseKind { Sit, Stand, Handshake }
    public PoseKind pose = PoseKind.Sit;
    /// <summary>Куда смотрит (обычно камера игрока).</summary>
    public Transform lookTarget;

    Transform body, torso, head;
    readonly Transform[] thigh = new Transform[2], shin = new Transform[2], arm = new Transform[2], fore = new Transform[2];
    Transform[] mapDots;
    float sit = 1f, hand, clock;
    Quaternion headRest = Quaternion.identity;

    /// <summary>Точка головы — для камеры в катсцене.</summary>
    public Vector3 HeadPosition => head != null ? head.position + Vector3.up * 0.1f : transform.position + Vector3.up * 1.6f;

    void Awake()
    {
        if (transform.Find("ShapikModel") == null) BuildModel();
        body = Find("Body"); torso = Find("Torso"); head = Find("Head");
        for (int i = 0; i < 2; i++)
        {
            string s = i == 0 ? "L" : "R";
            thigh[i] = Find("Thigh" + s); shin[i] = Find("Shin" + s); arm[i] = Find("Arm" + s); fore[i] = Find("ForeArm" + s);
        }
        if (head != null) headRest = head.localRotation;
        var dots = Find("MapDots");
        if (dots != null)
        {
            mapDots = new Transform[dots.childCount];
            for (int i = 0; i < mapDots.Length; i++) mapDots[i] = dots.GetChild(i);
        }
    }

    Transform Find(string n)
    {
        foreach (var t in GetComponentsInChildren<Transform>(true)) if (t.name == n) return t;
        return null;
    }

    void Update()
    {
        float dt = Time.deltaTime;
        clock += dt;
        sit = Mathf.MoveTowards(sit, pose == PoseKind.Sit ? 1f : 0f, dt * 1.6f);
        hand = Mathf.MoveTowards(hand, pose == PoseKind.Handshake ? 1f : 0f, dt * 2.5f);
        float s = Mathf.SmoothStep(0f, 1f, sit), h = Mathf.SmoothStep(0f, 1f, hand);

        // Сидит: таз ниже и чуть назад (на стуле), бёдра вперёд, голени вниз.
        if (body != null) body.localPosition = new Vector3(0f, Mathf.Lerp(0.92f, 0.5f, s), Mathf.Lerp(0f, -0.12f, s));
        for (int i = 0; i < 2; i++)
        {
            if (thigh[i] != null) thigh[i].localRotation = Quaternion.Euler(-88f * s, 0f, 0f);
            if (shin[i] != null) shin[i].localRotation = Quaternion.Euler(88f * s, 0f, 0f);
        }
        if (torso != null) torso.localRotation = Quaternion.Euler(8f * s + Mathf.Sin(clock * 1.5f) * 1.5f, 0f, 0f);

        // Руки: печатает на ноутбуке / висят / правая протянута для рукопожатия.
        float typeL = Mathf.Sin(clock * 17f) * 6f * s, typeR = Mathf.Sin(clock * 15f + 1f) * 6f * s;
        if (arm[0] != null) arm[0].localRotation = Quaternion.Euler(-55f * s, 0f, -4f);
        if (fore[0] != null) fore[0].localRotation = Quaternion.Euler(-35f * s + typeL, 0f, 0f);
        if (arm[1] != null) arm[1].localRotation = Quaternion.Euler(Mathf.Lerp(-55f * s, -70f, h), 0f, Mathf.Lerp(4f, -8f, h));
        if (fore[1] != null) fore[1].localRotation = Quaternion.Euler(Mathf.Lerp(-35f * s + typeR, -15f + Mathf.Sin(clock * 9f) * 4f * (hand >= 1f ? 1f : 0f), h), 0f, 0f);

        // Голова — на игрока (сидя — в ноутбук, пока его не позвали).
        if (head != null)
        {
            Quaternion want = headRest * Quaternion.Euler(18f * s, 0f, 0f);
            if (lookTarget != null && pose != PoseKind.Sit)
            {
                Vector3 to = transform.InverseTransformDirection(lookTarget.position - head.position);
                float yaw = Mathf.Clamp(Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg, -70f, 70f);
                float pitch = Mathf.Clamp(-Mathf.Atan2(to.y, new Vector2(to.x, to.z).magnitude) * Mathf.Rad2Deg, -30f, 30f);
                want = Quaternion.Euler(pitch, yaw, 0f) * headRest;
            }
            head.localRotation = Quaternion.Slerp(head.localRotation, want, 1f - Mathf.Exp(-dt * 5f));
        }

        // Красные точки зомби ползают по карте на мониторе.
        if (mapDots != null)
            for (int i = 0; i < mapDots.Length; i++)
            {
                float t = clock * 0.15f + i * 1.7f;
                mapDots[i].localPosition = new Vector3(Mathf.PerlinNoise(t, i) - 0.5f, Mathf.PerlinNoise(i, t) - 0.5f, 0.7f) * 0.85f;
            }
    }

    // ---------- Модель ----------

    void BuildModel()
    {
        var M = (System.Func<Color, float, Material>)((c, sm) => Interactable.Mat(c, sm));
        var skin = M(new Color(0.92f, 0.74f, 0.6f), 0.25f);
        var hoodie = M(new Color(0.22f, 0.45f, 0.3f), 0.2f);
        var jeans = M(new Color(0.18f, 0.25f, 0.42f), 0.25f);
        var shoes = M(new Color(0.92f, 0.92f, 0.9f), 0.4f);
        var hair = M(new Color(0.16f, 0.11f, 0.08f), 0.3f);
        var dark = M(new Color(0.05f, 0.05f, 0.05f), 0.6f);
        var cap = M(new Color(0.75f, 0.15f, 0.12f), 0.3f);

        var model = Interactable.Node(transform, "ShapikModel", Vector3.zero);
        var bodyN = Interactable.Node(model, "Body", new Vector3(0f, 0.92f, 0f));
        for (int i = 0; i < 2; i++)
        {
            float sx = i == 0 ? -1f : 1f;
            string s = i == 0 ? "L" : "R";
            var th = Interactable.Node(bodyN, "Thigh" + s, new Vector3(sx * 0.1f, 0f, 0f));
            Interactable.Part(th, PrimitiveType.Capsule, new Vector3(0f, -0.23f, 0f), new Vector3(0.15f, 0.25f, 0.15f), jeans);
            var sh = Interactable.Node(th, "Shin" + s, new Vector3(0f, -0.45f, 0f));
            Interactable.Part(sh, PrimitiveType.Capsule, new Vector3(0f, -0.21f, 0f), new Vector3(0.13f, 0.23f, 0.13f), jeans);
            Interactable.Part(sh, PrimitiveType.Cube, new Vector3(0f, -0.43f, 0.04f), new Vector3(0.11f, 0.08f, 0.27f), shoes);
        }
        var torsoN = Interactable.Node(bodyN, "Torso", Vector3.zero);
        Interactable.Part(torsoN, PrimitiveType.Capsule, new Vector3(0f, 0.3f, 0f), new Vector3(0.44f, 0.33f, 0.27f), hoodie);
        Interactable.Part(torsoN, PrimitiveType.Sphere, new Vector3(0f, 0.52f, -0.12f), new Vector3(0.3f, 0.14f, 0.14f), hoodie); // капюшон
        Interactable.Part(torsoN, PrimitiveType.Cube, new Vector3(0f, 0.2f, 0.135f), new Vector3(0.22f, 0.1f, 0.01f), M(new Color(0.18f, 0.37f, 0.25f), 0.2f)); // карман
        Interactable.Part(torsoN, PrimitiveType.Cylinder, new Vector3(0f, 0.6f, 0f), new Vector3(0.1f, 0.05f, 0.1f), skin);

        var headN = Interactable.Node(torsoN, "Head", new Vector3(0f, 0.63f, 0f));
        Interactable.Part(headN, PrimitiveType.Sphere, new Vector3(0f, 0.11f, 0.01f), new Vector3(0.19f, 0.23f, 0.21f), skin);
        Interactable.Part(headN, PrimitiveType.Sphere, new Vector3(0f, 0.17f, -0.02f), new Vector3(0.2f, 0.15f, 0.21f), hair);
        // Кепка козырьком назад.
        Interactable.Part(headN, PrimitiveType.Sphere, new Vector3(0f, 0.19f, -0.005f), new Vector3(0.205f, 0.12f, 0.215f), cap);
        Interactable.Part(headN, PrimitiveType.Cube, new Vector3(0f, 0.19f, -0.15f), new Vector3(0.16f, 0.012f, 0.1f), cap, new Vector3(-8f, 0f, 0f));
        // Очки.
        foreach (float sx in new[] { -1f, 1f })
        {
            Interactable.Part(headN, PrimitiveType.Cylinder, new Vector3(sx * 0.045f, 0.12f, 0.105f), new Vector3(0.05f, 0.003f, 0.05f), dark, new Vector3(90f, 0f, 0f));
            Interactable.Part(headN, PrimitiveType.Sphere, new Vector3(sx * 0.045f, 0.12f, 0.1f), new Vector3(0.018f, 0.014f, 0.01f), dark);
        }
        Interactable.Part(headN, PrimitiveType.Cube, new Vector3(0f, 0.12f, 0.108f), new Vector3(0.04f, 0.006f, 0.006f), dark);
        Interactable.Part(headN, PrimitiveType.Sphere, new Vector3(0f, 0.09f, 0.115f), new Vector3(0.028f, 0.04f, 0.03f), skin);
        Interactable.Part(headN, PrimitiveType.Cube, new Vector3(0f, 0.045f, 0.1f), new Vector3(0.05f, 0.008f, 0.01f), M(new Color(0.55f, 0.3f, 0.28f), 0.3f), new Vector3(0f, 0f, 4f)); // ухмылка

        for (int i = 0; i < 2; i++)
        {
            float sx = i == 0 ? -1f : 1f;
            string s = i == 0 ? "L" : "R";
            var a = Interactable.Node(torsoN, "Arm" + s, new Vector3(sx * 0.24f, 0.52f, 0f));
            Interactable.Part(a, PrimitiveType.Capsule, new Vector3(0f, -0.14f, 0f), new Vector3(0.11f, 0.16f, 0.11f), hoodie);
            var f = Interactable.Node(a, "ForeArm" + s, new Vector3(0f, -0.29f, 0f));
            Interactable.Part(f, PrimitiveType.Capsule, new Vector3(0f, -0.12f, 0f), new Vector3(0.1f, 0.14f, 0.1f), hoodie);
            Interactable.Part(f, PrimitiveType.Sphere, new Vector3(0f, -0.28f, 0.01f), new Vector3(0.075f, 0.09f, 0.05f), skin);
        }

        // Стол, ноутбук, монитор с картой университета, стул.
        var wood = M(new Color(0.42f, 0.3f, 0.2f), 0.3f);
        var metal = M(new Color(0.35f, 0.35f, 0.37f), 0.4f);
        var desk = Interactable.Node(transform, "Desk", new Vector3(0f, 0f, 0.62f));
        Interactable.Part(desk, PrimitiveType.Cube, new Vector3(0f, 0.74f, 0f), new Vector3(1.2f, 0.04f, 0.62f), wood, default, true);
        foreach (float x in new[] { -0.55f, 0.55f })
            foreach (float z in new[] { -0.26f, 0.26f })
                Interactable.Part(desk, PrimitiveType.Cube, new Vector3(x, 0.36f, z), new Vector3(0.04f, 0.72f, 0.04f), metal, default, true);
        var laptop = Interactable.Node(desk, "Laptop", new Vector3(-0.1f, 0.76f, -0.08f));
        Interactable.Part(laptop, PrimitiveType.Cube, new Vector3(0f, 0.01f, 0f), new Vector3(0.34f, 0.02f, 0.24f), dark);
        Interactable.Part(laptop, PrimitiveType.Cube, new Vector3(0f, 0.12f, 0.13f), new Vector3(0.34f, 0.22f, 0.012f), dark, new Vector3(-12f, 0f, 0f));
        Interactable.Part(laptop, PrimitiveType.Cube, new Vector3(0f, 0.12f, 0.122f), new Vector3(0.31f, 0.19f, 0.004f),
            Interactable.Mat(new Color(0.05f, 0.1f, 0.15f), 0.8f, 0f, new Color(0.15f, 0.45f, 0.6f)), new Vector3(-12f, 0f, 0f));
        // Монитор: экран развёрнут к Шапику (−Z), точки зомби на нём.
        var mon = Interactable.Node(desk, "Monitor", new Vector3(0.38f, 1.05f, 0.18f));
        mon.localRotation = Quaternion.Euler(0f, 160f, 0f);
        Interactable.Part(mon, PrimitiveType.Cube, Vector3.zero, new Vector3(0.62f, 0.38f, 0.03f), dark);
        var screen = Interactable.Part(mon, PrimitiveType.Cube, new Vector3(0f, 0f, 0.017f), new Vector3(0.58f, 0.34f, 0.004f),
            Interactable.Mat(new Color(0.05f, 0.12f, 0.08f), 0.8f, 0f, new Color(0.1f, 0.35f, 0.18f)));
        Interactable.Part(mon, PrimitiveType.Cube, new Vector3(0f, -0.26f, 0f), new Vector3(0.05f, 0.18f, 0.05f), dark);
        var dots = Interactable.Node(screen, "MapDots", Vector3.zero);
        var red = Interactable.Mat(new Color(1f, 0.1f, 0.1f), 0.5f, 0f, new Color(3f, 0.2f, 0.2f));
        for (int i = 0; i < 9; i++)
        {
            var d = Interactable.Part(dots, PrimitiveType.Cube, Vector3.zero, new Vector3(0.04f, 0.06f, 0.2f), red);
            d.name = "Dot" + i;
        }
        var chair = Interactable.Node(transform, "Chair", new Vector3(0f, 0f, -0.15f));
        Interactable.Part(chair, PrimitiveType.Cube, new Vector3(0f, 0.45f, 0f), new Vector3(0.45f, 0.05f, 0.45f), metal, default, true);
        Interactable.Part(chair, PrimitiveType.Cube, new Vector3(0f, 0.75f, -0.21f), new Vector3(0.45f, 0.55f, 0.04f), metal);
        Interactable.Part(chair, PrimitiveType.Cylinder, new Vector3(0f, 0.22f, 0f), new Vector3(0.05f, 0.22f, 0.05f), metal);
        // Свет монитора на лицо.
        var glow = new GameObject("ScreenGlow").AddComponent<Light>();
        glow.transform.SetParent(desk, false);
        glow.transform.localPosition = new Vector3(0f, 1.0f, -0.1f);
        glow.type = LightType.Point;
        glow.range = 2.2f;
        glow.intensity = 0.8f;
        glow.color = new Color(0.4f, 0.75f, 0.9f);
        glow.shadows = LightShadows.None;
    }
}
