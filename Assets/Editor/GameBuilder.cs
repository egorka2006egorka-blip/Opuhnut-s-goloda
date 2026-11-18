using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

/// <summary>
/// Сборка игры в приложение Windows: Tools → Build → Windows (.exe).
/// Перед сборкой пересобирает сцену Canteen, если сборщик сцены обновился. Результат — чистая отдельная папка
/// Build/Release/Opuhnut-s-goloda (старая удаляется; если она занята запущенной игрой — папка с датой и временем),
/// отчёт — Build/build_result.txt. Если есть файл Build/.request, сборка запускается сама при загрузке редактора (один раз).
/// Из командной строки: Unity.exe -batchmode -quit -projectPath . -executeMethod GameBuilder.BuildWindows
/// </summary>
[InitializeOnLoad]
public static class GameBuilder
{
    public const string GameName = "Opuhnut-s-goloda";
    const string ReleaseDir = "Build/Release";
    const string RequestFile = "Build/.request";
    const string ResultFile = "Build/build_result.txt";

    static GameBuilder()
    {
        if (Application.isBatchMode || !File.Exists(RequestFile)) return;
        File.Delete(RequestFile);
        double start = EditorApplication.timeSinceStartup;
        void Wait()
        {
            // Даём редактору догрузиться (импорт, компиляция), затем собираем.
            if (EditorApplication.timeSinceStartup - start < 3 || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
            EditorApplication.update -= Wait;
            if (EditorApplication.isPlayingOrWillChangePlaymode) { Debug.LogWarning("GameBuilder: выйдите из Play и запустите Tools → Build → Windows (.exe)."); return; }
            BuildWindows();
        }
        EditorApplication.update += Wait;
    }

    [MenuItem("Tools/Build/Windows (.exe)")]
    public static void BuildWindows()
    {
        PlayerSettings.productName = GameName;
        if (PlayerSettings.companyName == "DefaultCompany") PlayerSettings.companyName = "Nadeev, Lipatov, Kolesnikov";
        PlayerSettings.fullScreenMode = FullScreenMode.FullScreenWindow;

        if (!CanteenSceneBuilder.SceneUpToDate)
        {
            Debug.Log("GameBuilder: сцена Canteen устарела — пересобираю.");
            CanteenSceneBuilder.Build();
        }

        // В сборку — главное меню и столовая (без тестовой SampleScene).
        string[] scenes = EditorBuildSettings.scenes
            .Where(s => s.enabled && File.Exists(s.path) && !s.path.EndsWith("SampleScene.unity"))
            .Select(s => s.path).ToArray();
        if (scenes.Length == 0) { Report(false, "Нет сцен в Build Settings."); return; }

        string outDir = PrepareOutDir();
        var options = new BuildPlayerOptions
        {
            scenes = scenes,
            locationPathName = outDir + "/" + GameName + ".exe",
            target = BuildTarget.StandaloneWindows64,
            targetGroup = BuildTargetGroup.Standalone,
            options = BuildOptions.None,
        };
        var report = BuildPipeline.BuildPlayer(options);
        var sum = report.summary;
        bool ok = sum.result == BuildResult.Succeeded;
        if (ok)
        {
            // Отладочные папки Unity игрокам не нужны («DontShip»).
            foreach (var d in Directory.GetDirectories(outDir))
                if (d.Contains("DoNotShip") || d.Contains("DontShip"))
                    try { Directory.Delete(d, true); } catch (System.Exception e) { Debug.LogWarning("GameBuilder: " + e.Message); }
        }
        string text = ok
            ? $"Готово: {Path.GetFullPath(options.locationPathName)}\nРазмер: {sum.totalSize / (1024f * 1024f):0} МБ, время: {sum.totalTime.TotalSeconds:0} с\nСцены: {string.Join(", ", scenes)}"
            : $"Сборка не удалась ({sum.result}), ошибок: {sum.totalErrors}.\n" + string.Join("\n",
                report.steps.SelectMany(s => s.messages).Where(m => m.type == LogType.Error || m.type == LogType.Exception).Select(m => m.content).Take(20));
        Report(ok, text);
        if (ok && !Application.isBatchMode) EditorUtility.RevealInFinder(options.locationPathName);
    }

    /// <summary>Пустая папка для сборки: прежняя удаляется целиком; не удаётся (игра запущена) — новая папка с датой.</summary>
    static string PrepareOutDir()
    {
        string dir = ReleaseDir + "/" + GameName;
        try
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
        catch (System.Exception e)
        {
            Debug.LogWarning("GameBuilder: прежняя сборка занята (" + e.Message + ") — собираю в новую папку.");
            dir = ReleaseDir + "/" + GameName + "_" + System.DateTime.Now.ToString("yyyy-MM-dd_HH-mm");
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
        Directory.CreateDirectory(dir);
        return dir;
    }

    static void Report(bool ok, string text)
    {
        Directory.CreateDirectory("Build");
        File.WriteAllText(ResultFile, (ok ? "OK\n" : "FAIL\n") + text);
        if (ok) Debug.Log("GameBuilder: " + text); else Debug.LogError("GameBuilder: " + text);
        if (!Application.isBatchMode) EditorUtility.DisplayDialog(ok ? "Игра собрана" : "Ошибка сборки", text, "OK");
    }
}
