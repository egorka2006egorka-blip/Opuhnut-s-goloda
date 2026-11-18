using UnityEngine;

/// <summary>
/// Лом Декана — предмет в руках (WeaponPickup с melee): берётся на E, держится двумя руками, ЛКМ — удар,
/// им же вскрывается замок клетки Шапика. Создаётся из кода (выпадает из босса), модель — из примитивов.
/// </summary>
public static class CrowbarWeapon
{
    public const string ObjectName = "Crowbar";

    /// <summary>Лом, лежащий на полу под точкой pos, с жёлтой подсветкой.</summary>
    public static WeaponPickup Create(Vector3 pos, float yaw)
    {
        var root = new GameObject(ObjectName);
        // Ось +Z корня — «вперёд» оружия; сам лом в руках наклонён вверх, как перед ударом.
        var tilt = Interactable.Node(root.transform, "Tilt", Vector3.zero);
        tilt.localRotation = Quaternion.Euler(-50f, 0f, 0f);
        var model = Interactable.Node(tilt, "Model", Vector3.zero);
        var red = Interactable.Mat(new Color(0.68f, 0.09f, 0.07f), 0.5f, 0.5f);
        var steel = Interactable.Mat(new Color(0.55f, 0.55f, 0.52f), 0.6f, 0.9f);
        var tape = Interactable.Mat(new Color(0.1f, 0.1f, 0.1f), 0.2f);
        // Стержень вдоль +Z: от −0,32 до +0,42 м.
        Interactable.Part(model, PrimitiveType.Cylinder, new Vector3(0f, 0f, 0.05f), new Vector3(0.024f, 0.37f, 0.024f), red, new Vector3(90f, 0f, 0f));
        Interactable.Part(model, PrimitiveType.Cylinder, new Vector3(0f, 0f, -0.2f), new Vector3(0.028f, 0.1f, 0.028f), tape, new Vector3(90f, 0f, 0f)); // обмотка рукояти
        // Загнутый «гусь» с раздвоенным жалом.
        Interactable.Part(model, PrimitiveType.Cylinder, new Vector3(0f, 0.035f, 0.46f), new Vector3(0.024f, 0.055f, 0.024f), red, new Vector3(45f, 0f, 0f));
        Interactable.Part(model, PrimitiveType.Cylinder, new Vector3(0f, 0.085f, 0.47f), new Vector3(0.022f, 0.035f, 0.022f), red, new Vector3(-20f, 0f, 0f));
        foreach (float x in new[] { -0.008f, 0.008f })
            Interactable.Part(model, PrimitiveType.Cube, new Vector3(x, 0.12f, 0.455f), new Vector3(0.008f, 0.04f, 0.012f), steel, new Vector3(-30f, 0f, 0f));
        // Плоское жало на другом конце.
        Interactable.Part(model, PrimitiveType.Cube, new Vector3(0f, -0.004f, -0.34f), new Vector3(0.03f, 0.01f, 0.05f), steel, new Vector3(-10f, 0f, 0f));

        // Хват: правая рука — у конца рукояти, левая — выше по стержню (оба хвата в наклонённой системе лома).
        var gripR = Interactable.Node(tilt, "Grip_R", new Vector3(0.024f, -0.012f, -0.22f));
        gripR.localRotation = Quaternion.LookRotation(new Vector3(0f, -0.2f, 1f), Vector3.left);
        var gripL = Interactable.Node(tilt, "Grip_L", new Vector3(-0.014f, -0.026f, 0.02f));
        gripL.localRotation = Quaternion.LookRotation(new Vector3(0f, 0f, 1f), new Vector3(0.3f, 1f, 0f));

        var w = root.AddComponent<WeaponPickup>();
        w.displayName = "Лом";
        w.melee = true;
        w.magazineSize = 0;
        w.gripRight = gripR;
        w.gripLeft = gripL;
        w.holdOffset = new Vector3(0.14f, -0.3f, 0.38f);
        w.recoilScale = 0f;

        // Коллайдер по габаритам (чтобы его можно было подобрать взглядом и бросить).
        Bounds b = new Bounds();
        bool first = true;
        foreach (var r in root.GetComponentsInChildren<Renderer>())
            foreach (var c in Corners(r.bounds))
            {
                var p = root.transform.InverseTransformPoint(c);
                if (first) { b = new Bounds(p, Vector3.zero); first = false; } else b.Encapsulate(p);
            }
        var box = root.AddComponent<BoxCollider>();
        box.center = b.center;
        box.size = b.size + Vector3.one * 0.02f;

        // Подсветка «подсвеченный жёлтым».
        var glow = new GameObject("Glow").AddComponent<Light>();
        glow.transform.SetParent(root.transform, false);
        glow.transform.localPosition = new Vector3(0f, 0.2f, 0f);
        glow.type = LightType.Point;
        glow.color = new Color(1f, 0.8f, 0.2f);
        glow.range = 2.5f;
        glow.intensity = 2.5f;
        root.AddComponent<CrowbarGlow>();

        // Лежит на полу: стержень горизонтально, на боку.
        Quaternion lying = Quaternion.Euler(0f, yaw, 0f) * Quaternion.Euler(0f, 0f, 90f) * Quaternion.Inverse(tilt.localRotation);
        root.transform.SetPositionAndRotation(pos + Vector3.up * 0.5f, lying);
        float floorY = pos.y;
        if (Physics.Raycast(pos + Vector3.up * 0.5f, Vector3.down, out RaycastHit hit, 3f, ~0, QueryTriggerInteraction.Ignore)) floorY = hit.point.y;
        float minY = float.MaxValue;
        foreach (var r in root.GetComponentsInChildren<Renderer>()) minY = Mathf.Min(minY, r.bounds.min.y);
        root.transform.position += Vector3.up * (floorY - minY + 0.005f);
        return w;
    }

    static Vector3[] Corners(Bounds b)
    {
        var c = new Vector3[8];
        for (int i = 0; i < 8; i++)
            c[i] = new Vector3((i & 1) == 0 ? b.min.x : b.max.x, (i & 2) == 0 ? b.min.y : b.max.y, (i & 4) == 0 ? b.min.z : b.max.z);
        return c;
    }

    /// <summary>Лом в сцене (на полу или в руках).</summary>
    public static WeaponPickup Find()
    {
        foreach (var w in WeaponPickup.All) if (w != null && w.melee) return w;
        return null;
    }
}

/// <summary>Подсветка лома гаснет, когда его взяли в руки, и мягко пульсирует на полу.</summary>
public class CrowbarGlow : MonoBehaviour
{
    WeaponPickup weapon;
    Light glow;

    void Awake()
    {
        weapon = GetComponent<WeaponPickup>();
        glow = GetComponentInChildren<Light>();
    }

    void Update()
    {
        if (glow == null) return;
        glow.enabled = weapon == null || !weapon.IsHeld;
        glow.intensity = 1.8f + Mathf.Sin(Time.time * 3f) * 0.7f;
    }
}
