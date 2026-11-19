using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>Рендер камеры в текстуру вне основного кадра (URP — через RenderRequest).</summary>
public static class OffscreenRender
{
    public static void Render(Camera cam, RenderTexture rt)
    {
        cam.targetTexture = rt;
        var req = new RenderPipeline.StandardRequest { destination = rt };
        if (RenderPipeline.SupportsRenderRequest(cam, req)) RenderPipeline.SubmitRenderRequest(cam, req);
        else cam.Render();
        cam.targetTexture = null;
    }

    public static RenderTexture NewRT(int w, int h)
    {
        var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32) { antiAliasing = 1, hideFlags = HideFlags.HideAndDontSave };
        rt.Create();
        return rt;
    }
}

/// <summary>
/// «Фотостудия» для 3D-моделей предметов: модели стоят далеко под уровнем на отдельном слое,
/// своя камера и свет снимают их в RenderTexture — иконки ячеек и вращающийся предмет в описании.
/// </summary>
public class ItemPreviewStage
{
    const int Layer = 31;
    static readonly Vector3 Origin = new Vector3(0f, -2000f, 0f);
    static readonly Color Clear = new Color(0.07f, 0.07f, 0.08f, 0f);

    GameObject root;
    Camera cam;
    readonly Dictionary<string, GameObject> models = new Dictionary<string, GameObject>();
    readonly Dictionary<string, RenderTexture> icons = new Dictionary<string, RenderTexture>();
    RenderTexture detail;

    void Ensure()
    {
        if (root != null) return;
        root = new GameObject("ItemPreviewStage") { hideFlags = HideFlags.HideAndDontSave };
        root.transform.position = Origin;

        var camGo = new GameObject("PreviewCamera");
        camGo.transform.SetParent(root.transform, false);
        cam = camGo.AddComponent<Camera>();
        cam.enabled = false;
        cam.cullingMask = 1 << Layer;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = Clear;
        cam.fieldOfView = 22f;
        cam.nearClipPlane = 0.01f;
        cam.farClipPlane = 10f;
        cam.allowHDR = false;
        cam.allowMSAA = false;

        // Свет: основной сверху-спереди, заполняющий сбоку, контровой сзади.
        AddLight(new Vector3(0.6f, 1.0f, -0.8f), 2.2f, new Color(1f, 0.96f, 0.9f));
        AddLight(new Vector3(-1.0f, 0.3f, -0.4f), 0.9f, new Color(0.75f, 0.85f, 1f));
        AddLight(new Vector3(0f, 0.6f, 1.0f), 1.2f, Color.white);
    }

    void AddLight(Vector3 dir, float intensity, Color c)
    {
        var go = new GameObject("PreviewLight");
        go.transform.SetParent(root.transform, false);
        go.transform.localPosition = dir.normalized * 0.8f;
        var l = go.AddComponent<Light>();
        l.type = LightType.Point;
        l.range = 3f;
        l.intensity = intensity;
        l.color = c;
        l.shadows = LightShadows.None;
    }

    GameObject Model(string id)
    {
        if (models.TryGetValue(id, out var m) && m != null) return m;
        m = ItemDatabase.BuildModel(id);
        m.hideFlags = HideFlags.HideAndDontSave;
        m.transform.SetParent(root.transform, false);
        foreach (var t in m.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = Layer;
        foreach (var r in m.GetComponentsInChildren<Renderer>(true)) r.shadowCastingMode = ShadowCastingMode.Off;
        m.SetActive(false);
        models[id] = m;
        return m;
    }

    /// <summary>Снимает модель: поворот yaw вокруг вертикали, камера сверху-спереди и вписана по размеру.</summary>
    void Shoot(string id, RenderTexture rt, float yaw)
    {
        Ensure();
        var m = Model(id);
        m.SetActive(true);
        // Карточка лежит лицом вверх и ловит верхний свет в лоб — пересвечивается и видна сплющенной.
        // Для снимка ставим её лицом к камере (камера смотрит сверху под 28°): основной свет падает вскользь.
        bool card = System.Array.IndexOf(ItemDatabase.Cards, id) >= 0;
        m.transform.localRotation = card ? Quaternion.Euler(-62f, 0f, 0f) : Quaternion.identity;
        m.transform.localPosition = Vector3.zero;
        // Центруем модель в начале координат сцены-студии.
        var rs = m.GetComponentsInChildren<Renderer>();
        if (rs.Length > 0)
        {
            var b = rs[0].bounds;
            foreach (var r in rs) b.Encapsulate(r.bounds);
            m.transform.position -= b.center - root.transform.position;
            float radius = b.extents.magnitude;
            m.transform.RotateAround(root.transform.position, Vector3.up, yaw);
            Vector3 dir = Quaternion.Euler(28f, 0f, 0f) * Vector3.back;
            float dist = radius / Mathf.Sin(cam.fieldOfView * 0.5f * Mathf.Deg2Rad) * 1.05f;
            cam.transform.position = root.transform.position + dir * dist;
            cam.transform.LookAt(root.transform.position);
            cam.nearClipPlane = Mathf.Max(0.001f, dist - radius * 2f);
            cam.farClipPlane = dist + radius * 2f;
        }
        OffscreenRender.Render(cam, rt);
        m.SetActive(false);
    }

    /// <summary>Иконка предмета (рисуется один раз). null — ещё не готова.</summary>
    public Texture Icon(string id) => icons.TryGetValue(id, out var rt) && rt != null && rt.IsCreated() ? rt : null;

    /// <summary>Готовит недостающие иконки. Вызывать вне OnGUI (из Update/LateUpdate).</summary>
    public void PrepareIcons(IReadOnlyList<Inventory.Stack> items)
    {
        foreach (var s in items)
        {
            if (Icon(s.id) != null) continue;
            var rt = OffscreenRender.NewRT(192, 192);
            Shoot(s.id, rt, System.Array.IndexOf(ItemDatabase.Cards, s.id) >= 0 ? -8f : -35f); // карточку — почти анфас
            icons[s.id] = rt;
        }
    }

    /// <summary>Вращающаяся модель выбранного предмета. Вызывать вне OnGUI.</summary>
    public Texture RenderDetail(string id, float yaw)
    {
        if (detail == null || !detail.IsCreated()) detail = OffscreenRender.NewRT(512, 512);
        Shoot(id, detail, yaw);
        return detail;
    }

    public Texture Detail => detail;

    public void Dispose()
    {
        foreach (var rt in icons.Values) if (rt != null) { rt.Release(); Object.Destroy(rt); }
        icons.Clear();
        if (detail != null) { detail.Release(); Object.Destroy(detail); detail = null; }
        if (root != null) Object.Destroy(root);
        root = null;
        models.Clear();
    }
}
