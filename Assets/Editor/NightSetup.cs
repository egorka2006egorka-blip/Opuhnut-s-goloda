using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Ночь за окнами столовой и приглушённый свет внутри: из дневной панорамы (Outside_View) делается ночная — тёмное небо со звёздами и луной,
/// тёмные деревья и дорога, горящие фонари со светом на асфальте. Солнце становится луной, небо и окружающий свет — ночными.
/// Вызывается из CanteenSceneBuilder.Build и отдельно — Tools → Canteen → Make Night (для открытой сцены).
/// </summary>
public static class NightSetup
{
    const string Folder = "Assets/Effects/Night";
    const string TexturePath = Folder + "/OutsideNight.png";
    const string ViewMatPath = Folder + "/OutsideNight.mat";
    const string GroundMatPath = Folder + "/OutsideGroundNight.mat";
    const string SkyMatPath = Folder + "/NightSky.mat";
    const string PanelMatPath = Folder + "/LightPanelDim.mat";

    [MenuItem("Tools/Canteen/Make Night (dim interior)")]
    static void MakeNightOpenScene()
    {
        Apply(GameObject.Find("Canteen"));
        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        Debug.Log("NightSetup: на улице ночь. Сохраните сцену (Ctrl+S).");
    }

    public static void Apply(GameObject canteen)
    {
        Directory.CreateDirectory(Folder);
        SetupSkyAndLight();
        DimInterior();
        if (canteen == null) { Debug.LogWarning("NightSetup: объект Canteen не найден — панорама за окнами не изменена."); return; }

        Transform view = null;
        foreach (var t in canteen.GetComponentsInChildren<Transform>(true))
            if (t.name == "Outside_View") { view = t; break; }
        var r = view != null ? view.GetComponent<Renderer>() : null;
        if (r == null) { Debug.LogWarning("NightSetup: Outside_View не найден."); return; }

        var mats = r.sharedMaterials;
        int viewSlot = -1, groundSlot = -1;
        for (int i = 0; i < mats.Length; i++)
        {
            string n = mats[i] != null ? mats[i].name : "";
            if (n.Contains("Ground")) groundSlot = i;
            else if (n.Contains("Outside_View") || n.Contains("OutsideNight")) viewSlot = i;
        }
        if (viewSlot < 0) viewSlot = 0;

        // Ночная панорама строится из дневной (или берётся готовая, если дневной уже нет на объекте).
        var nightTex = AssetDatabase.LoadAssetAtPath<Texture2D>(TexturePath);
        var dayTex = mats[viewSlot] != null && !mats[viewSlot].name.Contains("OutsideNight") ? mats[viewSlot].mainTexture : null;
        if (dayTex != null)
        {
            var png = MakeNightPanorama(dayTex).EncodeToPNG();
            File.WriteAllBytes(TexturePath, png);
            AssetDatabase.ImportAsset(TexturePath, ImportAssetOptions.ForceUpdate);
            nightTex = AssetDatabase.LoadAssetAtPath<Texture2D>(TexturePath);
        }
        if (nightTex == null) { Debug.LogWarning("NightSetup: нет дневной панорамы, чтобы сделать ночную."); return; }

        mats[viewSlot] = SaveLit(ViewMatPath, m =>
        {
            m.SetTexture("_BaseMap", nightTex);
            m.SetColor("_BaseColor", Color.white);
            m.SetFloat("_Smoothness", 0f);
            m.EnableKeyword("_EMISSION");
            m.SetTexture("_EmissionMap", nightTex);
            m.SetColor("_EmissionColor", Color.white); // панорама светится сама — свет сцены на неё почти не влияет
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
        });
        if (groundSlot >= 0)
            mats[groundSlot] = SaveLit(GroundMatPath, m =>
            {
                m.SetColor("_BaseColor", new Color(0.025f, 0.035f, 0.03f));
                m.SetFloat("_Smoothness", 0f);
                m.EnableKeyword("_EMISSION");
                m.SetColor("_EmissionColor", new Color(0.006f, 0.008f, 0.009f));
                m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            });
        r.sharedMaterials = mats;
    }

    static void SetupSkyAndLight()
    {
        // Небо: процедурное, почти чёрное, без солнечного диска.
        var skyShader = Shader.Find("Skybox/Procedural");
        if (skyShader != null)
        {
            var sky = AssetDatabase.LoadAssetAtPath<Material>(SkyMatPath);
            if (sky == null) { sky = new Material(skyShader) { name = "NightSky" }; AssetDatabase.CreateAsset(sky, SkyMatPath); }
            sky.shader = skyShader;
            sky.SetFloat("_SunDisk", 0f);
            sky.SetFloat("_AtmosphereThickness", 0.35f);
            sky.SetColor("_SkyTint", new Color(0.2f, 0.25f, 0.45f));
            sky.SetColor("_GroundColor", new Color(0.02f, 0.02f, 0.03f));
            sky.SetFloat("_Exposure", 0.08f);
            EditorUtility.SetDirty(sky);
            RenderSettings.skybox = sky;
        }

        // Окружающий свет — тёмный, холодный: внутри светят только лампы.
        RenderSettings.ambientMode = AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = new Color(0.035f, 0.04f, 0.06f);
        RenderSettings.ambientEquatorColor = new Color(0.03f, 0.03f, 0.035f);
        RenderSettings.ambientGroundColor = new Color(0.015f, 0.015f, 0.015f);
        RenderSettings.reflectionIntensity = 0.15f;

        // Солнце → луна: тусклый голубоватый свет сверху.
        foreach (var l in Object.FindObjectsByType<Light>(FindObjectsSortMode.None))
        {
            if (l.type != LightType.Directional) continue;
            l.name = "Moon";
            l.transform.rotation = Quaternion.Euler(55f, -35f, 0f);
            l.intensity = 0.12f;
            l.color = new Color(0.6f, 0.7f, 1f);
            l.shadows = LightShadows.Soft;
            l.shadowStrength = 0.7f;
            RenderSettings.sun = l;
            break;
        }
    }

    /// <summary>
    /// Приглушённый свет внутри: в зале — по слабой тёплой лампе прямо под каждой светящейся панелью потолка (LED_…),
    /// на кухне и в тоннеле — слабее и теплее; сами панели светятся тускло.
    /// Значения абсолютные — повторный вызов не затемняет сильнее.
    /// </summary>
    static void DimInterior()
    {
        var warm = new Color(1f, 0.84f, 0.64f);

        // Зал: старая равномерная сетка источников заменяется источниками под панелями.
        var hall = GameObject.Find("Ceiling Lights");
        if (hall == null) hall = new GameObject("Ceiling Lights");
        for (int i = hall.transform.childCount - 1; i >= 0; i--) Object.DestroyImmediate(hall.transform.GetChild(i).gameObject);
        int panels = 0;
        foreach (var t in Object.FindObjectsByType<Transform>(FindObjectsSortMode.None))
        {
            if (!t.name.StartsWith("LED_")) continue;
            var pr = t.GetComponent<Renderer>();
            if (pr == null) continue;
            var go = new GameObject("Light_" + t.name);
            go.transform.SetParent(hall.transform, false);
            go.transform.position = pr.bounds.center - Vector3.up * 0.3f;
            var l = go.AddComponent<Light>();
            l.type = LightType.Point;
            l.range = 6f;
            l.intensity = 0.2f;
            l.color = warm;
            l.shadows = LightShadows.None;
            panels++;
        }
        if (panels == 0) Object.DestroyImmediate(hall);

        foreach (var l in Object.FindObjectsByType<Light>(FindObjectsSortMode.None))
        {
            if (l.type != LightType.Point && l.type != LightType.Spot) continue;
            string parent = l.transform.parent != null ? l.transform.parent.name : "";
            if (parent == "Kitchen Lights") { l.intensity = 0.4f; l.range = 5f; }  // кухня (было 1,6) — свет уже под панелями
            else if (l.name == "Light") { l.intensity = 0.35f; l.range = 4f; }    // лестница в тоннеле (было 1,4)
            else continue;
            l.color = warm;
        }

        // Светящиеся панели и лампы в моделях: тусклая тёплая копия вместо яркого белого свечения.
        var dim = SaveLit(PanelMatPath, m =>
        {
            m.SetColor("_BaseColor", new Color(0.5f, 0.48f, 0.44f));
            m.SetFloat("_Smoothness", 0.3f);
            m.EnableKeyword("_EMISSION");
            m.SetColor("_EmissionColor", new Color(0.26f, 0.21f, 0.15f));
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
        });
        foreach (var r in Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None))
        {
            var mats = r.sharedMaterials;
            bool changed = false;
            for (int i = 0; i < mats.Length; i++)
            {
                string n = mats[i] != null ? mats[i].name : "";
                if (n.StartsWith("LED_Emission") || n.StartsWith("Light_Panel") || n.StartsWith("Tunnel_Light"))
                {
                    mats[i] = dim;
                    changed = true;
                }
            }
            if (changed) r.sharedMaterials = mats;
        }
    }

    static Material SaveLit(string path, System.Action<Material> setup)
    {
        var shader = Shader.Find("Universal Render Pipeline/Lit");
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null) { m = new Material(shader) { name = Path.GetFileNameWithoutExtension(path) }; AssetDatabase.CreateAsset(m, path); }
        m.shader = shader;
        setup(m);
        EditorUtility.SetDirty(m);
        return m;
    }

    // ---------- Ночная панорама ----------

    /// <summary>Читает текстуру через видеокарту (исходная может быть нечитаемой) и перекрашивает в ночь.</summary>
    static Texture2D MakeNightPanorama(Texture src)
    {
        int w = src.width, h = src.height;
        var rt = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
        Graphics.Blit(src, rt);
        var prev = RenderTexture.active;
        RenderTexture.active = rt;
        var day = new Texture2D(w, h, TextureFormat.RGBA32, false);
        day.ReadPixels(new Rect(0, 0, w, h), 0, 0);
        day.Apply();
        RenderTexture.active = prev;
        RenderTexture.ReleaseTemporary(rt);
        var px = day.GetPixels(); // строка 0 — низ картинки
        Object.DestroyImmediate(day);

        Color At(int x, int yTop) => px[(h - 1 - yTop) * w + ((x % w) + w) % w];
        bool IsSky(Color c) => c.b > 0.7f && c.b - c.r > 0.12f;
        bool IsCloud(Color c) => c.r > 0.86f && c.g > 0.86f && c.b > 0.86f;
        bool IsGray(Color c) => Mathf.Abs(c.r - c.g) < 0.05f && Mathf.Abs(c.g - c.b) < 0.05f;

        // Где кончается небо (линия горизонта) и где проходит дорога — по строкам.
        int horizon = Mathf.RoundToInt(h * 0.5f);
        for (int y = 0; y < h; y++)
        {
            int sky = 0;
            for (int x = 0; x < w; x += 4) if (IsSky(At(x, y)) || IsCloud(At(x, y))) sky++;
            if (sky < w / 4 * 0.5f) { horizon = y; break; }
        }
        int roadTop = -1, roadBottom = -1;
        for (int y = horizon; y < h; y++)
        {
            int gray = 0;
            for (int x = 0; x < w; x += 4) { var c = At(x, y); if (IsGray(c) && c.grayscale > 0.4f && c.grayscale < 0.65f) gray++; }
            bool road = gray > w / 4 * 0.35f; // машины частично закрывают дорогу
            if (road && roadTop < 0) roadTop = y;
            if (!road && roadTop >= 0) { roadBottom = y; break; }
        }
        if (roadTop < 0) { roadTop = Mathf.RoundToInt(h * 0.72f); roadBottom = Mathf.RoundToInt(h * 0.79f); }
        if (roadBottom < 0) roadBottom = roadTop + Mathf.RoundToInt(h * 0.07f);

        var night = new Color[w * h];
        var rnd = new System.Random(1337);
        Color skyTop = new Color(0.01f, 0.014f, 0.045f), skyLow = new Color(0.06f, 0.07f, 0.14f);
        for (int yTop = 0; yTop < h; yTop++)
            for (int x = 0; x < w; x++)
            {
                var c = At(x, yTop);
                Color n;
                float g = Mathf.Pow(Mathf.Clamp01((float)yTop / Mathf.Max(1, horizon)), 1.6f);
                Color skyC = Color.Lerp(skyTop, skyLow, g);
                if (yTop < horizon + 4 && IsCloud(c)) n = Color.Lerp(skyC, new Color(0.13f, 0.14f, 0.2f), 0.6f);
                else if (IsSky(c))
                {
                    n = skyC;
                    // Звёзды — только на чистом небе, к горизонту реже.
                    double p = rnd.NextDouble();
                    if (p < 0.0018 * (1.0 - g * 0.8))
                    {
                        float b = 0.35f + (float)rnd.NextDouble() * 0.65f;
                        n = new Color(b * 0.92f, b * 0.95f, b);
                    }
                }
                else n = new Color(c.r * 0.1f + 0.004f, c.g * 0.11f + 0.006f, c.b * 0.16f + 0.012f); // земля, деревья, дорога, машины
                night[(h - 1 - yTop) * w + x] = n;
            }

        void Add(int x, int yTop, Color c)
        {
            if (yTop < 0 || yTop >= h) return;
            int i = (h - 1 - yTop) * w + ((x % w) + w) % w;
            night[i] = new Color(Mathf.Min(1f, night[i].r + c.r), Mathf.Min(1f, night[i].g + c.g), Mathf.Min(1f, night[i].b + c.b));
        }

        // Луна с ореолом.
        int mx = Mathf.RoundToInt(w * 0.8f), my = Mathf.RoundToInt(horizon * 0.28f);
        float mr = Mathf.Max(6f, h * 0.04f);
        for (int dy = -(int)(mr * 6); dy <= mr * 6; dy++)
            for (int dx = -(int)(mr * 6); dx <= mr * 6; dx++)
            {
                float d = Mathf.Sqrt(dx * dx + dy * dy);
                int yy = my + dy;
                if (yy < 0 || yy >= horizon) continue;
                int i = (h - 1 - yy) * w + ((mx + dx) % w + w) % w;
                if (d <= mr)
                {
                    float crater = Mathf.PerlinNoise((mx + dx) * 0.25f, yy * 0.25f) > 0.62f ? 0.85f : 1f;
                    float edge = Mathf.Clamp01(mr - d);
                    night[i] = Color.Lerp(night[i], new Color(0.93f, 0.92f, 0.84f) * crater, edge);
                }
                else Add(mx + dx, yy, new Color(0.16f, 0.18f, 0.24f) * Mathf.Exp(-(d - mr) / (mr * 1.2f)));
            }

        // Фонари: серые столбы — столбцы серых пикселей между горизонтом и дорогой.
        int poleFrom = Mathf.Max(0, horizon - Mathf.RoundToInt(h * 0.06f));
        int lastPole = -100;
        for (int x = 0; x < w; x++)
        {
            int count = 0, rows = 0, top = -1;
            for (int y = poleFrom; y < roadTop; y++)
            {
                rows++;
                var c = At(x, y);
                if (IsGray(c) && c.grayscale > 0.25f && c.grayscale < 0.6f) { count++; if (top < 0) top = y; }
            }
            if (rows == 0 || count < rows * 0.55f || x - lastPole < 20) continue;
            lastPole = x;
            int lampY = Mathf.Max(0, top - 1);
            float roadMid = (roadTop + roadBottom) * 0.5f;
            Color warm = new Color(1f, 0.78f, 0.45f);
            // Светящаяся голова фонаря и ореол.
            for (int dy = -24; dy <= 24; dy++)
                for (int dx = -24; dx <= 24; dx++)
                {
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    Add(x + dx, lampY + dy, warm * (0.55f * Mathf.Exp(-d * d / 60f) + 0.12f * Mathf.Exp(-d / 9f)));
                }
            for (int dx = -2; dx <= 2; dx++) for (int dy = -1; dy <= 1; dy++) Add(x + dx, lampY + dy, new Color(1f, 0.95f, 0.8f));
            // Пятно света на дороге и траве под фонарём.
            for (int y = lampY; y < Mathf.Min(h, roadBottom + 12); y++)
                for (int dx = -45; dx <= 45; dx++)
                {
                    float fy = Mathf.Abs(y - roadMid) / Mathf.Max(4f, (roadBottom - roadTop) * 0.9f);
                    float fx = Mathf.Abs(dx) / 45f;
                    float k = Mathf.Clamp01(1f - (fx * fx + fy * fy));
                    if (k > 0f) Add(x + dx, y, warm * (0.22f * k * k));
                }
        }

        var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
        tex.SetPixels(night);
        tex.Apply();
        return tex;
    }
}
