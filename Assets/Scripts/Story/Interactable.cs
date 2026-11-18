using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Сюжетный объект, с которым игрок взаимодействует на E (рация, дверь на цепи, записка).
/// PlayerWeaponHandler показывает подсказку Prompt и вызывает Interact.
/// </summary>
public abstract class Interactable : MonoBehaviour
{
    public static readonly List<Interactable> All = new List<Interactable>();

    [Tooltip("С какого расстояния (м) можно взаимодействовать.")]
    public float range = 2.4f;

    public abstract string Prompt { get; }
    public virtual bool Available => true;
    public abstract void Interact(GameObject player);

    protected virtual void OnEnable() => All.Add(this);
    protected virtual void OnDisable() => All.Remove(this);

    public virtual Vector3 Focus
    {
        get
        {
            var rs = GetComponentsInChildren<Renderer>();
            if (rs.Length == 0) return transform.position;
            Bounds b = rs[0].bounds;
            foreach (var r in rs) b.Encapsulate(r.bounds);
            return b.center;
        }
    }

    /// <summary>Объект, на который смотрит камера (в пределах range и угла, не загорожен).</summary>
    public static Interactable FindLook(Transform cam, Transform player, float angleLimit = 25f)
    {
        Interactable best = null;
        float bestAngle = float.MaxValue;
        foreach (var it in All)
        {
            if (it == null || !it.Available) continue;
            Vector3 to = it.Focus - cam.position;
            float dist = to.magnitude;
            if (dist > it.range || dist < 1e-3f) continue;
            float angle = Vector3.Angle(cam.forward, to);
            if (angle > Mathf.Lerp(angleLimit * 1.6f, angleLimit * 0.7f, dist / it.range)) continue;
            bool blocked = false;
            foreach (var h in Physics.RaycastAll(cam.position, to / dist, dist - 0.05f, ~0, QueryTriggerInteraction.Ignore))
                if (!h.transform.IsChildOf(it.transform) && !h.transform.IsChildOf(player)) { blocked = true; break; }
            if (blocked) continue;
            if (angle < bestAngle) { best = it; bestAngle = angle; }
        }
        return best;
    }

    // ---------- Общие детали моделей из примитивов ----------

    static readonly Dictionary<Color, Material> mats = new Dictionary<Color, Material>();

    public static Material Mat(Color c, float smooth = 0.3f, float metal = 0f, Color? emission = null)
    {
        var key = new Color(c.r, c.g, c.b, smooth + metal * 10f + (emission.HasValue ? 100f + emission.Value.r + emission.Value.g * 3f + emission.Value.b * 7f : 0f));
        if (mats.TryGetValue(key, out var m) && m != null) return m;
        m = RuntimeMaterials.Create(c, smooth, metal, emission); // не материал примитива: в сборке он фиолетовый
        mats[key] = m;
        return m;
    }

    public static Transform Part(Transform parent, PrimitiveType type, Vector3 pos, Vector3 size, Material m, Vector3 euler = default, bool collider = false)
    {
        var go = GameObject.CreatePrimitive(type);
        if (!collider) DestroyImmediate(go.GetComponent<Collider>());
        go.transform.SetParent(parent, false);
        go.transform.localPosition = pos;
        go.transform.localRotation = Quaternion.Euler(euler);
        go.transform.localScale = size;
        go.GetComponent<Renderer>().sharedMaterial = m;
        return go.transform;
    }

    public static Transform Node(Transform parent, string name, Vector3 pos)
    {
        var t = new GameObject(name).transform;
        t.SetParent(parent, false);
        t.localPosition = pos;
        return t;
    }
}
