using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.RenderGraphModule.Util;
using UnityEngine.Rendering.Universal;

/// <summary>Полноэкранные фильтры. Порядок совпадает с проходами шейдера Hidden/ScreenFilters (None — без прохода).</summary>
public enum ScreenFilterKind { None, VHS, CRT, Noir, Retro }

/// <summary>
/// Накладывает выбранный в настройках фильтр на картинку основной камеры — после постобработки URP,
/// до IMGUI-интерфейса (меню и HUD остаются читаемыми). Проход добавляется из кода, без правки Renderer-ассетов.
/// Для VHS дополнительно рисуется экранная надпись видеомагнитофона.
/// </summary>
public static class ScreenFilters
{
    static Material material;
    static FilterPass pass;
    static float filterTime;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Init()
    {
        RenderPipelineManager.beginCameraRendering -= OnBeginCamera; // на случай выключенной перезагрузки домена
        RenderPipelineManager.beginCameraRendering += OnBeginCamera;
        var go = new GameObject("ScreenFilterOverlay") { hideFlags = HideFlags.HideInHierarchy };
        Object.DontDestroyOnLoad(go);
        go.AddComponent<ScreenFilterOverlay>();
    }

    static void OnBeginCamera(ScriptableRenderContext ctx, Camera cam)
    {
        var kind = GameSettings.Filter;
        if (kind == ScreenFilterKind.None) return;
        // Только игровые камеры, рисующие на экран (иконки инвентаря рендерятся в текстуру — их не трогаем).
        if (cam.cameraType != CameraType.Game || cam.targetTexture != null) return;
        var data = cam.GetUniversalAdditionalCameraData();
        if (data == null || data.renderType != CameraRenderType.Base || data.scriptableRenderer == null) return;

        if (material == null)
        {
            var shader = Resources.Load<Shader>("Shaders/ScreenFilters");
            if (shader == null || !shader.isSupported) return;
            material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
        }
        pass ??= new FilterPass();

        material.SetFloat("_Intensity", GameSettings.FilterIntensity);
        material.SetFloat("_FilterTime", filterTime);
        pass.Setup(material, (int)kind - 1);
        data.scriptableRenderer.EnqueuePass(pass);
    }

    /// <summary>Время фильтра идёт и на паузе (timeScale = 0), чтобы шум не замирал.</summary>
    internal static void Tick() => filterTime = (filterTime + Time.unscaledDeltaTime) % 3600f;

    class FilterPass : ScriptableRenderPass
    {
        Material mat;
        int shaderPass;

        public FilterPass()
        {
            renderPassEvent = RenderPassEvent.AfterRenderingPostProcessing;
            requiresIntermediateTexture = true; // читать и писать цвет камеры нельзя напрямую в back buffer
        }

        public void Setup(Material m, int p) { mat = m; shaderPass = p; }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            var resources = frameData.Get<UniversalResourceData>();
            if (resources.isActiveTargetBackBuffer) return;

            var source = resources.activeColorTexture;
            var desc = renderGraph.GetTextureDesc(source);
            desc.name = "_ScreenFilterColor";
            desc.clearBuffer = false;
            var destination = renderGraph.CreateTexture(desc);

            renderGraph.AddBlitPass(new RenderGraphUtils.BlitMaterialParameters(source, destination, mat, shaderPass), "Screen Filter");
            resources.cameraColor = destination;
        }
    }
}

/// <summary>Надпись «PLAY ▶» и счётчик ленты в режиме VHS, плюс тик времени фильтров.</summary>
class ScreenFilterOverlay : MonoBehaviour
{
    float tape;
    GUIStyle style;
    int styleHeight;

    void Update()
    {
        ScreenFilters.Tick();
        if (GameSettings.Filter == ScreenFilterKind.VHS && !PauseMenu.IsPaused) tape += Time.unscaledDeltaTime;
    }

    void OnGUI()
    {
        if (GameSettings.Filter != ScreenFilterKind.VHS || GameSettings.FilterIntensity < 0.05f) return;
        if (Event.current.type != EventType.Repaint) return;
        if (SaveSystem.HideHud || StoryDirector.HideHud) return;
        GUI.depth = 10; // под остальным интерфейсом

        if (style == null || styleHeight != Screen.height)
        {
            styleHeight = Screen.height;
            style = new GUIStyle(GUI.skin.label)
            {
                fontSize = Mathf.Max(16, Screen.height / 26),
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.UpperRight,
                wordWrap = false,
            };
        }

        bool paused = PauseMenu.IsPaused;
        // На паузе надпись мигает, как на настоящем видеомагнитофоне.
        string head = paused ? (Mathf.Repeat(Time.unscaledTime, 1f) < 0.6f ? "PAUSE ❚❚" : "") : "PLAY ▶";
        int s = (int)tape;
        string counter = $"SP  {s / 3600}:{s / 60 % 60:00}:{s % 60:00}";

        float pad = Screen.height * 0.045f, w = Screen.width * 0.5f, h = style.fontSize * 1.4f;
        var r1 = new Rect(Screen.width - pad - w, pad, w, h);
        var r2 = new Rect(r1.x, r1.y + h, w, h);
        var was = GUI.color;
        float a = 0.85f * GameSettings.FilterIntensity;
        foreach (var (r, text) in new[] { (r1, head), (r2, counter) })
        {
            GUI.color = new Color(0f, 0f, 0f, a * 0.6f);
            GUI.Label(new Rect(r.x + 3, r.y + 3, r.width, r.height), text, style);
            GUI.color = new Color(0.92f, 0.95f, 1f, a);
            GUI.Label(r, text, style);
        }
        GUI.color = was;
    }
}
