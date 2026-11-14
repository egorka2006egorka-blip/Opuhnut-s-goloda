using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Предмет на полу, который можно подобрать в инвентарь (клавиша «Подобрать», по умолчанию E).
/// Можно повесить на объект в сцене (модель — своя или сгенерируется из ItemDatabase, если детей нет)
/// или создать из кода: ItemPickup.Spawn (так зомби оставляют патроны и аптечки).
/// </summary>
public class ItemPickup : MonoBehaviour
{
    public static readonly List<ItemPickup> All = new List<ItemPickup>();
    /// <summary>Имена предметов, расставленных в сцене, которые уже подобраны (попадает в сохранение).</summary>
    public static readonly List<string> Collected = new List<string>();

    [Tooltip("Вид предмета: ammo_9mm, ammo_545, ammo_12g, medkit, student_card (см. ItemDatabase).")]
    public string itemId = ItemDatabase.Ammo9;
    [Min(1)] public int count = 10;
    [Tooltip("Во сколько раз модель крупнее настоящего предмета (чтобы её было видно на полу).")]
    public float modelScale = 1.6f;
    /// <summary>Создан во время игры (выпал из зомби), а не расставлен в сцене.</summary>
    [HideInInspector] public bool spawned;

    public ItemDef Def => ItemDatabase.Get(itemId);

    public string Label
    {
        get
        {
            var d = Def;
            if (d == null) return itemId;
            return count > 1 ? d.name + " (" + count + ")" : d.name;
        }
    }

#if UNITY_EDITOR
    // В редакторе модели ещё нет (строится при запуске) — показываем предмет рамкой и подписью.
    void OnDrawGizmos()
    {
        if (Application.isPlaying) return;
        Gizmos.color = itemId == ItemDatabase.Medkit ? new Color(1f, 0.25f, 0.2f) : new Color(1f, 0.8f, 0.2f);
        Gizmos.DrawWireCube(transform.position + Vector3.up * 0.08f, new Vector3(0.25f, 0.16f, 0.18f));
        UnityEditor.Handles.Label(transform.position + Vector3.up * 0.3f, Label);
    }
#endif

    void OnEnable() => All.Add(this);
    void OnDisable() => All.Remove(this);

    void Start()
    {
        // Объект без модели — строим её из базы предметов.
        if (transform.childCount == 0 && GetComponent<Renderer>() == null)
        {
            var model = ItemDatabase.BuildModel(itemId).transform;
            model.SetParent(transform, false);
            model.localScale = Vector3.one * modelScale;
        }
        if (GetComponent<Collider>() == null) FitCollider();
    }

    void FitCollider()
    {
        var rs = GetComponentsInChildren<Renderer>();
        if (rs.Length == 0) return;
        var b = rs[0].bounds;
        foreach (var r in rs) b.Encapsulate(r.bounds);
        var box = gameObject.AddComponent<BoxCollider>();
        box.center = transform.InverseTransformPoint(b.center);
        Vector3 s = transform.lossyScale;
        box.size = new Vector3(b.size.x / Mathf.Max(1e-4f, s.x), b.size.y / Mathf.Max(1e-4f, s.y), b.size.z / Mathf.Max(1e-4f, s.z));
    }

    public Vector3 Center
    {
        get
        {
            var r = GetComponentInChildren<Renderer>();
            return r != null ? r.bounds.center : transform.position;
        }
    }

    /// <summary>Положить предмет в инвентарь и убрать с пола.</summary>
    public void Collect(Inventory inventory)
    {
        if (inventory == null) return;
        inventory.Add(itemId, count);
        if (!spawned && !Collected.Contains(name)) Collected.Add(name);
        Destroy(gameObject);
    }

    /// <summary>Создаёт предмет в точке; он падает на пол с физикой.</summary>
    public static ItemPickup Spawn(string id, int count, Vector3 position)
    {
        if (ItemDatabase.Get(id) == null || count <= 0) return null;
        var go = new GameObject("Pickup_" + id);
        go.transform.SetPositionAndRotation(position, Quaternion.Euler(0f, Random.Range(0f, 360f), 0f));
        var p = go.AddComponent<ItemPickup>();
        p.itemId = id;
        p.count = count;
        p.spawned = true;
        var model = ItemDatabase.BuildModel(id).transform;
        model.SetParent(go.transform, false);
        model.localScale = Vector3.one * p.modelScale;
        p.FitCollider();
        var rb = go.AddComponent<Rigidbody>();
        rb.mass = 0.5f;
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
        rb.linearVelocity = new Vector3(Random.Range(-0.6f, 0.6f), 1.5f, Random.Range(-0.6f, 0.6f));
        // Игрок не спотыкается о предметы.
        var fpc = Object.FindAnyObjectByType<FirstPersonController>();
        var cc = fpc != null ? fpc.GetComponent<CharacterController>() : null;
        if (cc != null) Physics.IgnoreCollision(go.GetComponent<Collider>(), cc);
        return p;
    }

    /// <summary>Что выпадает из зомби: патроны к оружию в руках (или случайные) и иногда аптечка.</summary>
    public static void RollZombieDrop(Vector3 position, float ammoChance, float medkitChance)
    {
        if (Random.value < ammoChance)
        {
            var handler = Object.FindAnyObjectByType<PlayerWeaponHandler>();
            string ammo = handler != null && handler.CurrentWeapon != null && Random.value < 0.75f
                ? ItemDatabase.AmmoFor(handler.CurrentWeapon)
                : new[] { ItemDatabase.Ammo9, ItemDatabase.Ammo545, ItemDatabase.Ammo12 }[Random.Range(0, 3)];
            int n = ammo == ItemDatabase.Ammo12 ? Random.Range(3, 7) : ammo == ItemDatabase.Ammo545 ? Random.Range(10, 21) : Random.Range(8, 16);
            Spawn(ammo, n, position);
        }
        if (Random.value < medkitChance)
            Spawn(ItemDatabase.Medkit, 1, position + new Vector3(0.25f, 0.1f, 0f));
    }

    /// <summary>Предмет, на который смотрит камера (как у оружия: в пределах дальности и угла, не загорожен).</summary>
    public static ItemPickup FindLook(Transform cam, Transform player, float range, float angleLimit)
    {
        ItemPickup best = null;
        float bestAngle = angleLimit;
        foreach (var p in All)
        {
            if (p == null) continue;
            Vector3 to = p.Center - cam.position;
            float dist = to.magnitude;
            if (dist > range || dist < 1e-3f) continue;
            float angle = Vector3.Angle(cam.forward, to);
            float allowed = Mathf.Lerp(angleLimit * 1.5f, angleLimit * 0.6f, dist / range);
            if (angle > allowed || angle > bestAngle * 1.5f) continue;
            if (Physics.Raycast(cam.position, to / dist, out RaycastHit hit, dist - 0.05f, ~0, QueryTriggerInteraction.Ignore)
                && !hit.transform.IsChildOf(p.transform) && !hit.transform.IsChildOf(player))
                continue;
            if (best == null || angle < bestAngle) { best = p; bestAngle = angle; }
        }
        return best;
    }
}
