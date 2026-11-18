using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Создаёт 4 уровня качества графики — Low / Medium / High / Ultra (в игре: «Низкое», «Среднее», «Высокое», «Ультра»).
/// Для каждого — своя копия настроек URP (Assets/Settings/Quality/URP_*.asset) на основе PC_RPAsset:
/// масштаб рендера, сглаживание (MSAA), разрешение, дальность и мягкость теней, тени от ламп.
/// Плюс в самих уровнях: детализация дальних объектов (LOD), разрешение текстур, анизотропная фильтрация.
/// Запускается один раз автоматически; вручную — Tools → Graphics → Rebuild Quality Levels.
/// </summary>
[InitializeOnLoad]
public static class QualityLevelsSetup
{
    const string SourceAsset = "Assets/Settings/PC_RPAsset.asset";
    const string Folder = "Assets/Settings/Quality";
    const int Version = 1;
    static string VersionKey => "QualityLevelsSetup.Version." + Application.dataPath;

    struct Level
    {
        public string name;
        public float renderScale;
        public int msaa;              // 1 — выкл, 2/4/8 — число выборок
        public int mainShadowRes;
        public bool lampShadows;      // тени от дополнительных источников (лампы)
        public int lampShadowRes;
        public float shadowDistance;
        public int cascades;
        public bool softShadows;
        public int softQuality;       // 1 — низкое, 2 — среднее, 3 — высокое
        public int lightsPerObject;
        public float lodBias;
        public int textureMipLimit;   // 0 — полные текстуры, 1 — вдвое меньше
        public int anisotropic;       // 0 — выкл, 1 — вкл, 2 — принудительно
    }

    static readonly Level[] Levels =
    {
        new Level { name = "Low",    renderScale = 0.7f,  msaa = 1, mainShadowRes = 1024, lampShadows = false, lampShadowRes = 512,
                    shadowDistance = 20f, cascades = 1, softShadows = false, softQuality = 1, lightsPerObject = 2,
                    lodBias = 0.7f, textureMipLimit = 1, anisotropic = 0 },
        new Level { name = "Medium", renderScale = 0.85f, msaa = 1, mainShadowRes = 2048, lampShadows = true,  lampShadowRes = 1024,
                    shadowDistance = 35f, cascades = 2, softShadows = true,  softQuality = 1, lightsPerObject = 4,
                    lodBias = 1f,   textureMipLimit = 0, anisotropic = 1 },
        new Level { name = "High",   renderScale = 1f,    msaa = 2, mainShadowRes = 2048, lampShadows = true,  lampShadowRes = 2048,
                    shadowDistance = 50f, cascades = 4, softShadows = true,  softQuality = 2, lightsPerObject = 6,
                    lodBias = 1.5f, textureMipLimit = 0, anisotropic = 2 },
        new Level { name = "Ultra",  renderScale = 1f,    msaa = 4, mainShadowRes = 4096, lampShadows = true,  lampShadowRes = 4096,
                    shadowDistance = 80f, cascades = 4, softShadows = true,  softQuality = 3, lightsPerObject = 8,
                    lodBias = 2f,   textureMipLimit = 0, anisotropic = 2 },
    };
    const int DefaultLevel = 2; // «Высокое»

    static QualityLevelsSetup()
    {
        if (Application.isBatchMode) return;
        EditorApplication.delayCall += Check;
        EditorApplication.playModeStateChanged -= OnPlayModeChanged;
        EditorApplication.playModeStateChanged += OnPlayModeChanged;
    }

    static void OnPlayModeChanged(PlayModeStateChange state)
    {
        if (state == PlayModeStateChange.EnteredEditMode) EditorApplication.delayCall += Check;
    }

    static void Check()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        if (EditorPrefs.GetInt(VersionKey, 0) < Version) Rebuild();
    }

    [MenuItem("Tools/Graphics/Rebuild Quality Levels")]
    public static void Rebuild()
    {
        if (AssetDatabase.LoadAssetAtPath<ScriptableObject>(SourceAsset) == null)
        {
            Debug.LogError("QualityLevelsSetup: не найден " + SourceAsset);
            return;
        }
        Directory.CreateDirectory(Folder);

        // 1. Настройки URP для каждого уровня.
        var pipelines = new Object[Levels.Length];
        for (int i = 0; i < Levels.Length; i++)
        {
            var lv = Levels[i];
            string path = Folder + "/URP_" + lv.name + ".asset";
            if (!File.Exists(path)) AssetDatabase.CopyAsset(SourceAsset, path);
            var asset = AssetDatabase.LoadAssetAtPath<ScriptableObject>(path);
            var so = new SerializedObject(asset);
            Set(so, "m_RenderScale", lv.renderScale);
            Set(so, "m_MSAA", lv.msaa);
            Set(so, "m_MainLightShadowmapResolution", lv.mainShadowRes);
            Set(so, "m_AdditionalLightShadowsSupported", lv.lampShadows);
            Set(so, "m_AdditionalLightsShadowmapResolution", lv.lampShadowRes);
            Set(so, "m_ShadowDistance", lv.shadowDistance);
            Set(so, "m_ShadowCascadeCount", lv.cascades);
            Set(so, "m_SoftShadowsSupported", lv.softShadows);
            Set(so, "m_SoftShadowQuality", lv.softQuality);
            Set(so, "m_AdditionalLightsPerObjectLimit", lv.lightsPerObject);
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(asset);
            pipelines[i] = asset;
        }

        // 2. Уровни качества в Project Settings → Quality.
        var qualityAsset = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/QualitySettings.asset")[0];
        var qs = new SerializedObject(qualityAsset);
        var arr = qs.FindProperty("m_QualitySettings");

        // Шаблон — уровень «PC» (или последний), остальные удаляем и размножаем шаблон.
        int template = arr.arraySize - 1;
        for (int i = 0; i < arr.arraySize; i++)
            if (arr.GetArrayElementAtIndex(i).FindPropertyRelative("name").stringValue == "PC") template = i;
        for (int i = arr.arraySize - 1; i >= 0; i--)
            if (i != template) arr.DeleteArrayElementAtIndex(i);
        while (arr.arraySize < Levels.Length) arr.InsertArrayElementAtIndex(0);

        for (int i = 0; i < Levels.Length; i++)
        {
            var lv = Levels[i];
            var e = arr.GetArrayElementAtIndex(i);
            e.FindPropertyRelative("name").stringValue = lv.name;
            e.FindPropertyRelative("customRenderPipeline").objectReferenceValue = pipelines[i];
            e.FindPropertyRelative("lodBias").floatValue = lv.lodBias;
            e.FindPropertyRelative("globalTextureMipmapLimit").intValue = lv.textureMipLimit;
            e.FindPropertyRelative("anisotropicTextures").intValue = lv.anisotropic;
            e.FindPropertyRelative("vSyncCount").intValue = 0; // вертикальной синхронизацией управляет игра
            var excluded = e.FindPropertyRelative("excludedTargetPlatforms");
            if (excluded != null) excluded.arraySize = 0;
        }

        qs.FindProperty("m_CurrentQuality").intValue = DefaultLevel;
        var perPlatform = qs.FindProperty("m_PerPlatformDefaultQuality");
        if (perPlatform != null && perPlatform.isArray)
            for (int i = 0; i < perPlatform.arraySize; i++)
            {
                var second = perPlatform.GetArrayElementAtIndex(i).FindPropertyRelative("second");
                if (second != null) second.intValue = DefaultLevel;
            }
        qs.ApplyModifiedPropertiesWithoutUndo();

        AssetDatabase.SaveAssets();
        QualitySettings.SetQualityLevel(DefaultLevel, true);
        EditorPrefs.SetInt(VersionKey, Version);
        Debug.Log("QualityLevelsSetup: созданы уровни качества Low / Medium / High / Ultra (по умолчанию — High).");
    }

    static void Set(SerializedObject so, string name, float v)
    {
        var p = so.FindProperty(name);
        if (p == null) { Debug.LogWarning("QualityLevelsSetup: нет поля " + name); return; }
        if (p.propertyType == SerializedPropertyType.Float) p.floatValue = v;
        else p.intValue = Mathf.RoundToInt(v);
    }

    static void Set(SerializedObject so, string name, int v)
    {
        var p = so.FindProperty(name);
        if (p == null) { Debug.LogWarning("QualityLevelsSetup: нет поля " + name); return; }
        if (p.propertyType == SerializedPropertyType.Boolean) p.boolValue = v != 0;
        else if (p.propertyType == SerializedPropertyType.Float) p.floatValue = v;
        else p.intValue = v;
    }

    static void Set(SerializedObject so, string name, bool v) => Set(so, name, v ? 1 : 0);
}
