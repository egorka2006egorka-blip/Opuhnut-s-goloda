using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Секретная кнопка в столовой: маленькая красная кнопка на боковой стенке пожарного шкафа, у пола.
/// Ставится сама при загрузке сцены со столовой (ищет шкаф по имени из модели Canteen_LowPoly).
/// Нажатие (E) — зомби танцуют «Я тебя могну» (ZombieDance).
/// </summary>
public class SecretDanceButton : Interactable
{
    // Где искать — по порядку: первый найденный объект модели столовой.
    static readonly string[] Hosts = { "FireCabinet_001", "FireCabinet_002", "Radiator_001", "CoatRack_001" };

    Transform cap;
    float pressed;
    static AudioClip click;

    public override string Prompt => ZombieDance.Active ? "Кнопка нажата" : "Нажать маленькую кнопку";
    public override bool Usable => !ZombieDance.Active;
    public override Vector3 Focus => transform.position + transform.up * 0.012f;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Init()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
        OnSceneLoaded(SceneManager.GetActiveScene(), LoadSceneMode.Single); // первая сцена уже загружена
    }

    static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (!scene.IsValid() || !scene.isLoaded) return;
        foreach (var it in Interactable.All) if (it is SecretDanceButton && it.gameObject.scene == scene) return;

        Transform host = null;
        foreach (var name in Hosts)
        {
            foreach (var r in scene.GetRootGameObjects())
            {
                foreach (var t in r.GetComponentsInChildren<Transform>(true))
                    if (t.name == name) { host = t; break; }
                if (host != null) break;
            }
            if (host != null) break;
        }
        if (host == null) return; // не столовая

        var rs = host.GetComponentsInChildren<Renderer>();
        if (rs.Length == 0) return;
        Bounds b = rs[0].bounds;
        foreach (var x in rs) b.Encapsulate(x.bounds);

        // Тонкая горизонтальная ось — глубина (шкаф стоит к стене), вдоль стены — боковые стенки.
        bool xThin = b.size.x < b.size.z;
        Vector3 sideAxis = xThin ? Vector3.forward : Vector3.right;
        Vector3 depthAxis = xThin ? Vector3.right : Vector3.forward;
        float sideExt = xThin ? b.extents.z : b.extents.x, depthExt = xThin ? b.extents.x : b.extents.z;

        // Лицевая сторона — где перед шкафом свободно; боковая — где больше места до соседних предметов.
        Vector3 front = Free(b.center, depthAxis, depthExt, host) >= Free(b.center, -depthAxis, depthExt, host) ? depthAxis : -depthAxis;
        Vector3 side = Free(b.center, sideAxis, sideExt, host) >= Free(b.center, -sideAxis, sideExt, host) ? sideAxis : -sideAxis;

        Vector3 pos = b.center + side * (sideExt + 0.002f) + front * (depthExt * 0.45f) + Vector3.down * Mathf.Min(b.extents.y * 0.7f, b.center.y - b.min.y - 0.12f);
        var go = new GameObject("SecretDanceButton");
        SceneManager.MoveGameObjectToScene(go, scene);
        go.transform.SetPositionAndRotation(pos, Quaternion.FromToRotation(Vector3.up, side));
        go.AddComponent<SecretDanceButton>();
    }

    /// <summary>Сколько свободного места (до 1 м) от грани габарита в направлении dir.</summary>
    static float Free(Vector3 center, Vector3 dir, float ext, Transform self)
    {
        float best = 1f;
        foreach (var h in Physics.RaycastAll(center, dir, ext + 1f, ~0, QueryTriggerInteraction.Ignore))
        {
            if (h.transform.IsChildOf(self)) continue;
            best = Mathf.Min(best, Mathf.Max(0f, h.distance - ext));
        }
        return best;
    }

    void Awake()
    {
        range = 1.6f;
        // Ось кнопки — локальная Y (наружу от стенки шкафа).
        var plate = Mat(new Color(0.22f, 0.22f, 0.24f), 0.5f, 0.6f);
        var red = Mat(new Color(0.75f, 0.05f, 0.04f), 0.7f, 0f, new Color(0.25f, 0.01f, 0.01f));
        Part(transform, PrimitiveType.Cube, new Vector3(0f, 0.003f, 0f), new Vector3(0.05f, 0.006f, 0.05f), plate);
        cap = Part(transform, PrimitiveType.Cylinder, new Vector3(0f, 0.011f, 0f), new Vector3(0.026f, 0.005f, 0.026f), red);
    }

    void Update()
    {
        if (cap == null) return;
        pressed = Mathf.MoveTowards(pressed, 0f, Time.deltaTime * 4f);
        cap.localPosition = new Vector3(0f, 0.011f - 0.005f * Mathf.Clamp01(pressed * 3f), 0f);
    }

    public override void Interact(GameObject player)
    {
        if (ZombieDance.Active) return;
        pressed = 1f;
        if (click == null) click = DanceBeat.Click();
        AudioSource.PlayClipAtPoint(click, transform.position, 0.8f * GameSettings.EffectsVolume);
        ZombieDance.Begin();
    }
}
