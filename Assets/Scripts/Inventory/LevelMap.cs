using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Карта уровня: один раз снимает уровень сверху ортографической камерой (из-под потолка, на уровне этажа игрока),
/// затем перекрашивает снимок в «бумажный план» с обводкой контуров. Зомби, игрок и оружие на снимок не попадают.
/// </summary>
public class LevelMap
{
    public Texture2D Texture { get; private set; }
    /// <summary>Мировые границы снимка по X и Z.</summary>
    public Rect WorldRect { get; private set; }

    const int MaxSide = 1024;
    /// <summary>На сколько метров над полом стоит камера: ниже потолка (~3.1 м), выше столов и стоек.</summary>
    const float CameraHeight = 2.75f;

    public bool Ready => Texture != null;

    public void Capture(Transform player)
    {
        float floor = player.position.y;

        // Границы — по всем рендерерам этажа (кроме игрока, зомби, оружия и предметов).
        bool any = false;
        Bounds area = new Bounds();
        var hidden = new List<Renderer>();
        foreach (var r in Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None))
        {
            if (!r.enabled || !r.gameObject.activeInHierarchy) continue;
            if (IsDynamic(r.transform, player)) { if (!r.forceRenderingOff) { r.forceRenderingOff = true; hidden.Add(r); } continue; }
            if (!(r is MeshRenderer) && !(r is SkinnedMeshRenderer)) continue;
            var b = r.bounds;
            if (b.size.x > 300f || b.size.z > 300f) continue;               // небо, огромные плоскости
            if (r.name.StartsWith("Outside")) continue;                    // панорама за окнами — не часть здания
            if (b.max.y < floor - 1.5f || b.min.y > floor + CameraHeight) continue; // другие этажи
            if (!any) { area = b; any = true; } else area.Encapsulate(b);
        }
        if (!any) { Restore(hidden); return; }

        float margin = 1.5f;
        var world = Rect.MinMaxRect(area.min.x - margin, area.min.z - margin, area.max.x + margin, area.max.z + margin);
        int w, h;
        if (world.width >= world.height) { w = MaxSide; h = Mathf.Max(64, Mathf.RoundToInt(MaxSide * world.height / world.width)); }
        else { h = MaxSide; w = Mathf.Max(64, Mathf.RoundToInt(MaxSide * world.width / world.height)); }

        var go = new GameObject("MapCamera") { hideFlags = HideFlags.HideAndDontSave };
        var cam = go.AddComponent<Camera>();
        cam.enabled = false;
        cam.orthographic = true;
        cam.orthographicSize = world.height / 2f;
        cam.aspect = world.width / world.height;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = Color.black;
        cam.nearClipPlane = 0.02f;
        cam.farClipPlane = CameraHeight + 3f;
        cam.cullingMask = ~(1 << 31);
        cam.allowHDR = false;
        cam.allowMSAA = false;
        go.transform.position = new Vector3(world.center.x, floor + CameraHeight, world.center.y);
        go.transform.rotation = Quaternion.Euler(90f, 0f, 0f); // смотрит вниз, верх кадра — +Z, право — +X

        var rt = OffscreenRender.NewRT(w, h);
        OffscreenRender.Render(cam, rt);
        Restore(hidden);

        var prev = RenderTexture.active;
        RenderTexture.active = rt;
        var shot = new Texture2D(w, h, TextureFormat.RGBA32, false);
        shot.ReadPixels(new Rect(0, 0, w, h), 0, 0);
        shot.Apply();
        RenderTexture.active = prev;
        rt.Release();
        Object.Destroy(rt);
        Object.Destroy(go);

        Texture = Stylize(shot);
        Object.Destroy(shot);
        WorldRect = world;
    }

    static bool IsDynamic(Transform t, Transform player)
    {
        if (t.IsChildOf(player)) return true;
        return t.GetComponentInParent<Zombie>() != null || t.GetComponentInParent<WeaponPickup>() != null
               || t.GetComponentInParent<ItemPickup>() != null || t.GetComponentInParent<ZombieBoss>() != null
               || t.GetComponentInParent<Shopkeeper>() != null || t.gameObject.layer == 31;
    }

    static void Restore(List<Renderer> hidden)
    {
        foreach (var r in hidden) if (r != null) r.forceRenderingOff = false;
    }

    /// <summary>Снимок → план на старой бумаге: яркость растягивается по перцентилям, контуры обводятся тёмным.</summary>
    static Texture2D Stylize(Texture2D src)
    {
        int w = src.width, h = src.height;
        var px = src.GetPixels32();
        var lum = new float[w * h];
        var hist = new int[256];
        for (int i = 0; i < px.Length; i++)
        {
            var c = px[i];
            float l = (0.299f * c.r + 0.587f * c.g + 0.114f * c.b) / 255f;
            lum[i] = l;
            hist[Mathf.Clamp((int)(l * 255f), 0, 255)]++;
        }
        float Percentile(float p)
        {
            int target = Mathf.RoundToInt(px.Length * p), acc = 0;
            for (int i = 0; i < 256; i++) { acc += hist[i]; if (acc >= target) return i / 255f; }
            return 1f;
        }
        float lo = Percentile(0.04f), hi = Mathf.Max(lo + 0.05f, Percentile(0.97f));

        var paper = new Color(0.86f, 0.8f, 0.66f);
        var ink = new Color(0.2f, 0.16f, 0.12f);
        var dark = new Color(0.33f, 0.28f, 0.22f);
        var outPx = new Color32[w * h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int i = y * w + x;
                float l = Mathf.Clamp01((lum[i] - lo) / (hi - lo));
                l = Mathf.Pow(l, 0.8f);
                // Собель — контуры предметов и стен.
                float gx = L(lum, w, h, x + 1, y - 1) + 2 * L(lum, w, h, x + 1, y) + L(lum, w, h, x + 1, y + 1)
                         - L(lum, w, h, x - 1, y - 1) - 2 * L(lum, w, h, x - 1, y) - L(lum, w, h, x - 1, y + 1);
                float gy = L(lum, w, h, x - 1, y + 1) + 2 * L(lum, w, h, x, y + 1) + L(lum, w, h, x + 1, y + 1)
                         - L(lum, w, h, x - 1, y - 1) - 2 * L(lum, w, h, x, y - 1) - L(lum, w, h, x + 1, y - 1);
                float edge = Mathf.Clamp01(Mathf.Sqrt(gx * gx + gy * gy) / (hi - lo) * 1.6f);
                var c = Color.Lerp(dark, paper, l);
                c = Color.Lerp(c, ink, edge * 0.85f);
                // Лёгкая «бумажная» неровность.
                float n = (Hash(x, y) - 0.5f) * 0.04f;
                c.r += n; c.g += n; c.b += n;
                outPx[i] = c;
            }
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, hideFlags = HideFlags.HideAndDontSave };
        tex.SetPixels32(outPx);
        tex.Apply(true);
        return tex;
    }

    static float L(float[] lum, int w, int h, int x, int y) => lum[Mathf.Clamp(y, 0, h - 1) * w + Mathf.Clamp(x, 0, w - 1)];

    static float Hash(int x, int y)
    {
        uint n = (uint)(x * 374761393 + y * 668265263);
        n = (n ^ (n >> 13)) * 1274126177u;
        return (n & 0xffff) / 65535f;
    }

    /// <summary>Мировая точка → координаты 0..1 на карте (v вверх).</summary>
    public Vector2 ToUV(Vector3 world) =>
        new Vector2((world.x - WorldRect.xMin) / WorldRect.width, (world.z - WorldRect.yMin) / WorldRect.height);

    public void Dispose()
    {
        if (Texture != null) Object.Destroy(Texture);
        Texture = null;
    }
}
