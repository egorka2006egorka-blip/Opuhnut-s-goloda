using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

/// <summary>
/// Создаёт сцену главного меню Assets/Scenes/MainMenu.unity, ставит её первой в Build Settings
/// и (по умолчанию) запускает Play в редакторе с главного меню.
/// Фон меню — тёмная ночная столовая с мигающими потолочными лампами и медленно «дышащей» камерой.
/// Меню: Tools → Main Menu.
/// </summary>
[InitializeOnLoad]
public static class MainMenuSceneSetup
{
    public const string ScenePath = "Assets/Scenes/MainMenu.unity";
    const string AssetsFolder = "Assets/Scenes/MainMenuBackground";
    const string CanteenPath = "Assets/Models location/Canteen_LowPoly.glb";
    const string StartFromMenuPref = "MainMenuSceneSetup.StartFromMenu";
    const string StartFromMenuItem = "Tools/Main Menu/Start Play From Main Menu";
    // Увеличивается, когда сборщик меняется — сцена меню пересобирается автоматически.
    const int SceneVersion = 5;
    static string VersionKey => "MainMenuSceneSetup.Version." + Application.dataPath;

    // Вид на зал (координаты Unity; glTFast зеркалит X модели): от окон из прохода между рядами столов
    // вдоль зала — столы и стулья по обе стороны, справа в кадре ряды у линии раздачи.
    static readonly Vector3 CameraPos = new Vector3(-9f, 1.7f, 8.6f);
    static readonly Vector3 CameraTarget = new Vector3(5.4f, 0.7f, 13.6f); // чуть левее вдоль зала

    static MainMenuSceneSetup()
    {
        if (Application.isBatchMode) return;
        EditorApplication.delayCall += CheckScene;
        // Если скрипты обновились во время Play — пересобрать сцену, как только редактор вернётся в обычный режим.
        EditorApplication.playModeStateChanged -= OnPlayModeChanged;
        EditorApplication.playModeStateChanged += OnPlayModeChanged;
    }

    static void OnPlayModeChanged(PlayModeStateChange state)
    {
        if (state == PlayModeStateChange.EnteredEditMode) EditorApplication.delayCall += CheckScene;
    }

    static void CheckScene()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        if (!File.Exists(ScenePath) || EditorPrefs.GetInt(VersionKey, 1) < SceneVersion) CreateScene();
        EnsureBuildSettings();
        ApplyPlayModeStartScene();
    }

    [MenuItem("Tools/Main Menu/Rebuild Scene")]
    public static void CreateScene()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(ScenePath));
        Scene scene;
        bool opened = false;
        try
        {
            var existing = SceneManager.GetSceneByPath(ScenePath);
            if (existing.IsValid() && existing.isLoaded) scene = existing;
            else if (File.Exists(ScenePath)) { scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive); opened = true; }
            else { scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive); opened = true; }
        }
        catch (System.Exception e)
        {
            Debug.LogError("MainMenuSceneSetup: не удалось открыть сцену меню (сохраните текущую сцену и повторите: Tools → Main Menu → Rebuild Scene). " + e.Message);
            return;
        }

        foreach (var go in scene.GetRootGameObjects()) Object.DestroyImmediate(go);
        var prevActive = SceneManager.GetActiveScene();
        SceneManager.SetActiveScene(scene);

        var camGo = new GameObject("Main Camera") { tag = "MainCamera" };
        var cam = camGo.AddComponent<Camera>();
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = Color.black;
        cam.fieldOfView = 55f;
        cam.nearClipPlane = 0.05f;
        cam.farClipPlane = 80f;
        camGo.AddComponent<AudioListener>();
        camGo.transform.position = CameraPos;
        camGo.transform.rotation = Quaternion.LookRotation(CameraTarget - CameraPos);

        new GameObject("MainMenu").AddComponent<MainMenu>();

        // Фоновая музыка меню (категория «Музыка» в настройках громкости).
        var musicClip = MenuMusicGenerator.EnsureClip();
        var musicGo = new GameObject("Menu Music");
        var musicSource = musicGo.AddComponent<AudioSource>();
        musicSource.clip = musicClip;
        musicSource.loop = true;
        musicSource.playOnAwake = false;
        musicSource.spatialBlend = 0f;
        musicGo.AddComponent<MenuMusic>();

        bool background = BuildBackground(cam);
        if (!background)
        {
            Debug.LogWarning("MainMenuSceneSetup: модель столовой не найдена (" + CanteenPath + ") — фон меню будет без 3D-сцены.");
            RenderSettings.skybox = null;
        }

        if (prevActive.IsValid() && prevActive != scene) SceneManager.SetActiveScene(prevActive);
        EditorSceneManager.SaveScene(scene, ScenePath);
        if (opened && SceneManager.sceneCount > 1) EditorSceneManager.CloseScene(scene, true);
        AssetDatabase.SaveAssets();
        EditorPrefs.SetInt(VersionKey, SceneVersion);
        EnsureBuildSettings();
        ApplyPlayModeStartScene();
        Debug.Log("MainMenuSceneSetup: сцена главного меню собрана: " + ScenePath);
    }

    // ---------- Фон: тёмная столовая ----------

    static bool BuildBackground(Camera cam)
    {
        var canteenAsset = AssetDatabase.LoadAssetAtPath<GameObject>(CanteenPath);
        if (canteenAsset == null) return false;
        Directory.CreateDirectory(AssetsFolder);

        // Ночь: почти нет рассеянного света, тёмный туман, без неба и без запекания.
        RenderSettings.skybox = null;
        RenderSettings.ambientMode = AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(0.075f, 0.062f, 0.045f); // тёплый полумрак
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.ExponentialSquared;
        RenderSettings.fogColor = new Color(0.03f, 0.024f, 0.016f);
        RenderSettings.fogDensity = 0.045f;
        Lightmapping.lightingSettings = new LightingSettings { name = "MainMenu Lighting", bakedGI = false, realtimeGI = false };

        var canteen = PrefabUtility.InstantiatePrefab(canteenAsset, SceneManager.GetActiveScene()) as GameObject;
        if (canteen == null) canteen = Object.Instantiate(canteenAsset);
        canteen.name = "Canteen";
        // Дневной вид за окнами не нужен — за стёклами ночь.
        var outside = FindChild(canteen.transform, "Outside_View");
        if (outside != null) outside.gameObject.SetActive(false);

        // Материалы светящейся части панелей: выключенная и включённая (её яркостью управляет MenuFlickerLight).
        var lit = Shader.Find("Universal Render Pipeline/Lit");
        var ledOff = SaveMaterial(new Material(lit) { name = "MenuLED_Off" }, m =>
        {
            m.SetColor("_BaseColor", new Color(0.32f, 0.33f, 0.34f));
            m.SetFloat("_Smoothness", 0.4f);
        });
        var ledOn = SaveMaterial(new Material(lit) { name = "MenuLED_On" }, m =>
        {
            m.SetColor("_BaseColor", new Color(0.9f, 0.95f, 0.92f));
            m.EnableKeyword("_EMISSION");
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            m.SetColor("_EmissionColor", new Color(0.85f, 1f, 0.92f) * 3f);
        });

        // Панели и слот светящегося материала в каждой.
        var panels = new List<(Renderer r, int slot)>();
        foreach (var r in canteen.GetComponentsInChildren<Renderer>(true))
        {
            if (!r.name.StartsWith("LED_") && !(r.transform.parent != null && r.transform.parent.name.StartsWith("LED_"))) continue;
            var mats = r.sharedMaterials;
            for (int i = 0; i < mats.Length; i++)
                if (mats[i] != null && mats[i].name.Contains("LED_Emission"))
                {
                    panels.Add((r, i));
                    mats[i] = ledOff;
                }
            r.sharedMaterials = mats;
        }

        // Какие лампы горят: выбираем по положению в кадре (кнопки слева, поэтому свет — справа).
        var view = new GameObject("tmp view").AddComponent<Camera>();
        view.transform.SetPositionAndRotation(cam.transform.position, cam.transform.rotation);
        view.fieldOfView = cam.fieldOfView;
        view.aspect = 16f / 9f;
        var candidates = panels.Select(p =>
        {
            Vector3 c = p.r.bounds.center;
            Vector3 vp = view.WorldToViewportPoint(c);
            return (p.r, p.slot, c, vp, dist: Vector3.Distance(c, cam.transform.position));
        }).Where(p => p.vp.z > 0f && p.vp.x > 0f && p.vp.x < 1f && p.vp.y > 0f && p.vp.y < 1.1f).ToList();
        Object.DestroyImmediate(view.gameObject);

        var used = new HashSet<Renderer>();
        (Renderer r, int slot, Vector3 c, Vector3 vp, float dist)? Pick(float vx, float vy, float dMin, float dMax)
        {
            var best = candidates.Where(p => !used.Contains(p.r) && p.dist >= dMin && p.dist <= dMax)
                .OrderBy(p => Mathf.Abs(p.vp.x - vx) + Mathf.Abs(p.vp.y - vy) * 0.5f).ToList();
            if (best.Count == 0) return null;
            used.Add(best[0].r);
            return best[0];
        }

        var lights = new GameObject("Menu Lights").transform;
        // Главная лампа: справа от кнопок, недалеко — неисправная, часто мигает и гаснет.
        var main = Pick(0.68f, 0.85f, 4f, 10f);
        if (main != null)
            AddLamp(lights, main.Value.r, main.Value.slot, ledOn, "Flicker_Main", 4.5f, 12f, new Color(1f, 0.88f, 0.62f),
                steady: 0.85f, offChance: 0.3f, shadows: true);
        // Дальняя лампа справа — тусклее, мигает реже.
        var far = Pick(0.88f, 0.75f, 9f, 22f);
        if (far != null)
            AddLamp(lights, far.Value.r, far.Value.slot, ledOn, "Flicker_Far", 2.6f, 10f, new Color(1f, 0.86f, 0.6f),
                steady: 0.7f, offChance: 0.15f, shadows: false, steadyTime: new Vector2(4f, 10f));
        // Ещё одна в глубине зала — почти ровная, даёт глубину.
        var back = Pick(0.45f, 0.7f, 12f, 30f);
        if (back != null)
            AddLamp(lights, back.Value.r, back.Value.slot, ledOn, "Dim_Back", 1.8f, 9f, new Color(1f, 0.84f, 0.55f),
                steady: 0.7f, offChance: 0.05f, shadows: false, steadyTime: new Vector2(8f, 20f));
        // Ровная тёплая лампа слева — подсвечивает зал под кнопками, почти не мигает.
        var fill = Pick(0.3f, 0.8f, 5f, 16f);
        if (fill != null)
            AddLamp(lights, fill.Value.r, fill.Value.slot, ledOn, "Warm_Fill", 2.2f, 11f, new Color(1f, 0.85f, 0.58f),
                steady: 0.8f, offChance: 0f, shadows: false, steadyTime: new Vector2(15f, 40f));

        // Лунный свет через окна за камерой — ложится полосами на столы в кадре.
        var moonGo = new GameObject("Moonlight");
        moonGo.transform.SetParent(lights, false);
        moonGo.transform.rotation = Quaternion.Euler(28f, 70f, 0f);
        var moon = moonGo.AddComponent<Light>();
        moon.type = LightType.Directional;
        moon.color = new Color(0.45f, 0.55f, 0.85f);
        moon.intensity = 0.12f;
        moon.shadows = LightShadows.Soft;

        cam.gameObject.AddComponent<MenuCameraDrift>();
        SetupPostProcessing(cam);
        return true;
    }

    static void AddLamp(Transform parent, Renderer panel, int slot, Material ledOn, string name, float intensity, float range, Color color,
        float steady, float offChance, bool shadows, Vector2? steadyTime = null)
    {
        var b = panel.bounds;
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.position = new Vector3(b.center.x, b.min.y - 0.15f, b.center.z);
        var l = go.AddComponent<Light>();
        l.type = LightType.Point;
        l.range = range;
        l.intensity = intensity;
        l.color = color;
        l.shadows = shadows ? LightShadows.Soft : LightShadows.None;
        var f = go.AddComponent<MenuFlickerLight>();
        f.intensity = intensity;
        f.emissionColor = color;
        f.emission = 2.5f + intensity * 0.5f;
        f.steadyLevel = steady;
        f.offChance = offChance;
        if (steadyTime != null) f.steadyTime = steadyTime.Value;
        f.panel = panel;
        f.emissionSlot = slot;
        f.emissionMaterial = ledOn;
    }

    /// <summary>Постобработка фона: свечение ламп, виньетка, зерно, приглушённые цвета.</summary>
    static void SetupPostProcessing(Camera cam)
    {
        string path = AssetsFolder + "/MainMenu_PostFX.asset";
        AssetDatabase.DeleteAsset(path);
        var profile = ScriptableObject.CreateInstance<VolumeProfile>();
        AssetDatabase.CreateAsset(profile, path);

        var bloom = profile.Add<Bloom>(true);
        bloom.intensity.Override(1.4f);
        bloom.threshold.Override(0.85f);
        bloom.scatter.Override(0.75f);

        var vignette = profile.Add<Vignette>(true);
        vignette.intensity.Override(0.42f);
        vignette.smoothness.Override(0.5f);

        var grain = profile.Add<FilmGrain>(true);
        grain.type.Override(FilmGrainLookup.Medium3);
        grain.intensity.Override(0.35f);

        var color = profile.Add<ColorAdjustments>(true);
        color.saturation.Override(-18f);
        color.contrast.Override(15f);
        color.postExposure.Override(0.45f);

        var tone = profile.Add<Tonemapping>(true);
        tone.mode.Override(TonemappingMode.ACES);

        foreach (var c in profile.components)
        {
            c.hideFlags = HideFlags.HideInInspector | HideFlags.HideInHierarchy;
            AssetDatabase.AddObjectToAsset(c, profile);
        }
        EditorUtility.SetDirty(profile);

        var volGo = new GameObject("PostFX");
        var vol = volGo.AddComponent<Volume>();
        vol.isGlobal = true;
        vol.sharedProfile = profile;

        var data = cam.GetUniversalAdditionalCameraData();
        data.renderPostProcessing = true;
        data.antialiasing = AntialiasingMode.FastApproximateAntialiasing;
    }

    static Material SaveMaterial(Material m, System.Action<Material> setup)
    {
        string path = AssetsFolder + "/" + m.name + ".mat";
        var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (existing != null)
        {
            existing.shader = m.shader;
            Object.DestroyImmediate(m);
            m = existing;
        }
        else AssetDatabase.CreateAsset(m, path);
        setup(m);
        EditorUtility.SetDirty(m);
        return m;
    }

    static Transform FindChild(Transform root, string name) =>
        root.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == name);

    // ---------- Build Settings и Play ----------

    /// <summary>Главное меню — первая сцена в Build Settings (с неё стартует собранная игра).</summary>
    static void EnsureBuildSettings()
    {
        if (!File.Exists(ScenePath)) return;
        var scenes = EditorBuildSettings.scenes.ToList();
        if (scenes.Count > 0 && scenes[0].path == ScenePath && scenes[0].enabled) return;
        scenes.RemoveAll(s => s.path == ScenePath);
        scenes.Insert(0, new EditorBuildSettingsScene(ScenePath, true));
        EditorBuildSettings.scenes = scenes.ToArray();
        AssetDatabase.SaveAssets();
    }

    static bool StartFromMenu => EditorPrefs.GetBool(StartFromMenuPref, true);

    [MenuItem(StartFromMenuItem)]
    static void ToggleStartFromMenu()
    {
        EditorPrefs.SetBool(StartFromMenuPref, !StartFromMenu);
        ApplyPlayModeStartScene();
    }

    [MenuItem(StartFromMenuItem, true)]
    static bool ToggleStartFromMenuValidate()
    {
        Menu.SetChecked(StartFromMenuItem, StartFromMenu);
        return true;
    }

    static void ApplyPlayModeStartScene()
    {
        EditorSceneManager.playModeStartScene = StartFromMenu ? AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) : null;
    }
}
