using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Подготовка уровня: мелкие предметы (кружки, тарелки, кастрюли…) становятся физическими — их можно поднять (E) и бросить (ЛКМ);
/// на столы и стойки раскладываются аптечки и патроны. Вызывается из CanteenSceneBuilder.Build
/// и отдельно — Tools → Canteen → Setup Props and Loot (для уже открытой сцены, без пересборки).
/// </summary>
public static class LevelPropsSetup
{
    const string LootRootName = "Loot";

    /// <summary>Что раскладывается по уровню (вид, количество в пачке).</summary>
    static readonly (string id, int count)[] LootTable =
    {
        (ItemDatabase.Medkit, 1), (ItemDatabase.Medkit, 1), (ItemDatabase.Medkit, 1), (ItemDatabase.Medkit, 1),
        (ItemDatabase.Ammo9, 15), (ItemDatabase.Ammo9, 15), (ItemDatabase.Ammo9, 12), (ItemDatabase.Ammo9, 12),
        (ItemDatabase.Ammo545, 20), (ItemDatabase.Ammo545, 20), (ItemDatabase.Ammo545, 15),
        (ItemDatabase.Ammo12, 6), (ItemDatabase.Ammo12, 6), (ItemDatabase.Ammo12, 5),
    };

    [MenuItem("Tools/Canteen/Setup Props and Loot")]
    static void SetupOpenScene()
    {
        int props = 0;
        foreach (var name in new[] { "Canteen", "UniversityKitchen" })
        {
            var root = GameObject.Find(name);
            if (root != null) props += SetupGrabbableProps(root);
        }
        var fpc = Object.FindAnyObjectByType<FirstPersonController>();
        int loot = PlaceLoot(fpc != null ? fpc.transform.position : Vector3.zero);
        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        Debug.Log($"LevelPropsSetup: физических предметов добавлено: {props}, разложено аптечек и патронов: {loot}. Сохраните сцену (Ctrl+S).");
    }

    /// <summary>Мелкие предметы модели → Rigidbody + выпуклый коллайдер + GrabbableProp (уже настроенные пропускаются).</summary>
    public static int SetupGrabbableProps(GameObject root)
    {
        int n = 0;
        foreach (var mf in root.GetComponentsInChildren<MeshFilter>(true))
        {
            if (mf.sharedMesh == null) continue;
            string title = GrabbableProp.NameFor(mf.name);
            if (title == null) continue;
            var go = mf.gameObject;
            if (go.GetComponent<GrabbableProp>() != null) continue;

            // Статичные объекты склеиваются при запуске (static batching) и не могут двигаться.
            GameObjectUtility.SetStaticEditorFlags(go, 0);
            foreach (var c in go.GetComponents<Collider>()) Object.DestroyImmediate(c);
            var mc = go.AddComponent<MeshCollider>();
            mc.sharedMesh = mf.sharedMesh;
            mc.convex = true;

            var rb = go.AddComponent<Rigidbody>();
            Vector3 size = Vector3.Scale(mf.sharedMesh.bounds.size, go.transform.lossyScale);
            float volume = Mathf.Abs(size.x * size.y * size.z);
            rb.mass = Mathf.Clamp(volume * 250f, 0.15f, 10f);
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;

            go.AddComponent<GrabbableProp>().displayName = title;
            n++;
        }
        return n;
    }

    /// <summary>
    /// Раскладывает аптечки и патроны по столам и стойкам (поверхности на высоте 0,55–1,15 м над полом под ними),
    /// равномерно по уровню и подальше от точки появления. Старый набор (объект «Loot») заменяется.
    /// </summary>
    public static int PlaceLoot(Vector3 spawn)
    {
        var old = GameObject.Find(LootRootName);
        if (old != null) Object.DestroyImmediate(old);
        Physics.SyncTransforms();

        // Границы уровня — по всем коллайдерам сцены.
        bool any = false;
        Bounds area = new Bounds();
        foreach (var c in Object.FindObjectsByType<Collider>(FindObjectsSortMode.None))
        {
            if (!c.enabled || c.isTrigger || c.attachedRigidbody != null) continue;
            var b = c.bounds;
            if (b.size.x > 200f || b.size.z > 200f) continue;
            if (!any) { area = b; any = true; } else area.Encapsulate(b);
        }
        if (!any) { Debug.LogWarning("LevelPropsSetup: в сцене нет коллайдеров — некуда класть предметы."); return 0; }

        var rnd = new System.Random(20261114);
        var candidates = new List<Vector3>();
        const float step = 0.5f;
        for (float x = area.min.x + step / 2f; x < area.max.x; x += step)
            for (float z = area.min.z + step / 2f; z < area.max.z; z += step)
            {
                var p = new Vector3(x + (float)(rnd.NextDouble() - 0.5) * step * 0.6f, area.max.y + 1f, z + (float)(rnd.NextDouble() - 0.5) * step * 0.6f);
                if (FindSurface(p, area.size.y + 2f, out Vector3 surface) && Vector3.Distance(surface, spawn) > 3f)
                    candidates.Add(surface);
            }
        if (candidates.Count == 0) { Debug.LogWarning("LevelPropsSetup: не нашлось столов для предметов."); return 0; }

        // Равномерно по уровню: каждый следующий предмет — в точке, самой далёкой от уже выбранных (и от точки появления).
        var chosen = new List<Vector3>();
        var lootRoot = new GameObject(LootRootName);
        Undo.RegisterCreatedObjectUndo(lootRoot, "Place Loot");
        for (int i = 0; i < LootTable.Length && candidates.Count > 0; i++)
        {
            int best = 0;
            float bestD = -1f;
            for (int k = 0; k < candidates.Count; k++)
            {
                float d = Vector3.Distance(candidates[k], spawn) * 0.6f; // точка появления «отталкивает» слабее
                foreach (var c in chosen) d = Mathf.Min(d, Vector3.Distance(candidates[k], c));
                if (d > bestD) { bestD = d; best = k; }
            }
            Vector3 pos = candidates[best];
            chosen.Add(pos);
            candidates.RemoveAll(c => Vector3.Distance(c, pos) < 1.5f);

            var (id, count) = LootTable[i];
            var go = new GameObject($"Loot_{i + 1:00}_{id}");
            go.transform.SetParent(lootRoot.transform, false);
            go.transform.SetPositionAndRotation(pos, Quaternion.Euler(0f, rnd.Next(0, 360), 0f));
            var pickup = go.AddComponent<ItemPickup>();
            pickup.itemId = id;
            pickup.count = count;
        }
        return chosen.Count;
    }

    /// <summary>
    /// Луч сверху вниз: ищет горизонтальную поверхность стола/стойки — от неё до следующей поверхности (пола) 0,55–1,15 м,
    /// над ней свободно, она ровная вокруг и на ней не стоят физические предметы.
    /// </summary>
    static bool FindSurface(Vector3 top, float length, out Vector3 surface)
    {
        surface = default;
        var hits = Physics.RaycastAll(top, Vector3.down, length, ~0, QueryTriggerInteraction.Ignore)
            .Where(h => h.normal.y > 0.3f).OrderBy(h => h.distance).ToArray();
        for (int i = 0; i + 1 < hits.Length; i++)
        {
            var h = hits[i];
            float height = h.point.y - hits[i + 1].point.y;
            if (height < 0.55f || height > 1.15f || h.normal.y < 0.97f || h.rigidbody != null) continue;
            if (h.collider.name.StartsWith("BoilerRoom_")) continue; // стол в котельной — пустой, по сюжету
            if (Physics.OverlapSphere(h.point, 0.35f, ~0, QueryTriggerInteraction.Ignore).Any(c => c.GetComponentInParent<StoryNote>() != null)) continue; // рядом записка
            if (i > 0 && hits[i - 1].point.y - h.point.y < 0.6f) continue; // сверху мало места (полка, витрина)
            if (!Flat(h.point)) continue;
            if (Physics.CheckSphere(h.point + Vector3.up * 0.2f, 0.13f, ~0, QueryTriggerInteraction.Ignore)) continue; // что-то стоит
            surface = h.point;
            return true;
        }
        return false;
    }

    static bool Flat(Vector3 p)
    {
        const float r = 0.16f;
        foreach (var o in new[] { new Vector3(r, 0, 0), new Vector3(-r, 0, 0), new Vector3(0, 0, r), new Vector3(0, 0, -r) })
        {
            if (!Physics.Raycast(p + o + Vector3.up * 0.25f, Vector3.down, out RaycastHit h, 0.4f, ~0, QueryTriggerInteraction.Ignore)) return false;
            if (Mathf.Abs(h.point.y - p.y) > 0.015f || h.rigidbody != null) return false;
        }
        return true;
    }
}
