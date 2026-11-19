using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Пасхалка: пять карточек из игры «Покажи, как…» (ItemDatabase.Cards) лежат на столах по всей столовой.
/// Столы — из модели Canteen_LowPoly (Canteen/Furniture/Tables/Table_###); выбираются как можно дальше друг от друга,
/// раскладка одинаковая в каждой игре. Карточки — обычные ItemPickup: подбираются в инвентарь (раздел «Важное»),
/// подобранные помнит сохранение (ItemPickup.Collected по имени объекта).
/// </summary>
public class CanteenCards : MonoBehaviour
{
    Transform tables;

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
        foreach (var c in FindObjectsByType<CanteenCards>())
            if (c.gameObject.scene == scene) return;
        Transform tables = null;
        foreach (var r in scene.GetRootGameObjects())
        {
            foreach (var t in r.GetComponentsInChildren<Transform>(true))
                if (t.name == "Tables" && t.parent != null && t.parent.name == "Furniture") { tables = t; break; }
            if (tables != null) break;
        }
        if (tables == null) return; // не столовая
        var go = new GameObject("CanteenCards");
        SceneManager.MoveGameObjectToScene(go, scene);
        go.AddComponent<CanteenCards>().tables = tables;
    }

    // Start — после SaveSystem.Apply (он идёт до Start объектов сцены): уже известно, что подобрано.
    void Start()
    {
        var list = new List<(Transform t, Bounds b)>();
        foreach (Transform t in tables)
        {
            if (!t.name.StartsWith("Table_")) continue;
            var rs = t.GetComponentsInChildren<Renderer>();
            if (rs.Length == 0) continue;
            Bounds b = rs[0].bounds;
            foreach (var r in rs) b.Encapsulate(r.bounds);
            list.Add((t, b));
        }
        if (list.Count == 0) { Debug.LogWarning("CanteenCards: в Canteen/Furniture/Tables нет столов Table_* — карточек не будет."); return; }
        list.Sort((a, c) => string.CompareOrdinal(a.t.name, c.t.name));

        // Столы как можно дальше друг от друга: первый — самый дальний от центра зала, дальше — самый дальний от уже выбранных.
        Vector3 center = Vector3.zero;
        foreach (var x in list) center += x.b.center;
        center /= list.Count;
        var chosen = new List<int>();
        float Flat(Vector3 a, Vector3 c) => new Vector2(a.x - c.x, a.z - c.z).sqrMagnitude;
        int first = 0;
        for (int i = 1; i < list.Count; i++) if (Flat(list[i].b.center, center) > Flat(list[first].b.center, center)) first = i;
        chosen.Add(first);
        var cards = ItemDatabase.Cards;
        while (chosen.Count < Mathf.Min(cards.Length, list.Count))
        {
            int best = -1; float bestD = -1f;
            for (int i = 0; i < list.Count; i++)
            {
                if (chosen.Contains(i)) continue;
                float d = float.MaxValue;
                foreach (int c in chosen) d = Mathf.Min(d, Flat(list[i].b.center, list[c].b.center));
                if (d > bestD) { bestD = d; best = i; }
            }
            chosen.Add(best);
        }

        var fpc = FindAnyObjectByType<FirstPersonController>();
        var inventory = fpc != null ? fpc.GetComponent<Inventory>() : null;
        for (int i = 0; i < chosen.Count; i++)
        {
            string id = cards[i];
            string objName = "Card_" + id;
            if (ItemPickup.Collected.Contains(objName) || (inventory != null && inventory.Count(id) > 0)) continue;
            var (table, b) = list[chosen[i]];
            var rng = new System.Random(1000 + i);
            float R() => (float)rng.NextDouble() * 2f - 1f;

            // Точка на столешнице (или на тарелке на ней), не у самого края.
            Vector3 pos = new Vector3(b.center.x, b.max.y, b.center.z);
            for (int attempt = 0; attempt < 6; attempt++)
            {
                Vector3 from = new Vector3(b.center.x + R() * b.extents.x * 0.4f, b.max.y + 0.5f, b.center.z + R() * b.extents.z * 0.4f);
                if (Physics.Raycast(from, Vector3.down, out RaycastHit hit, 1f, ~0, QueryTriggerInteraction.Ignore)
                    && hit.normal.y > 0.85f && hit.point.y > b.max.y - 0.25f)
                {
                    pos = hit.point;
                    break;
                }
            }

            var go = new GameObject(objName);
            SceneManager.MoveGameObjectToScene(go, gameObject.scene);
            go.transform.SetPositionAndRotation(pos + Vector3.up * 0.002f, Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f));
            var p = go.AddComponent<ItemPickup>();
            p.itemId = id;
            p.count = 1;
            p.modelScale = 1.8f; // настоящий размер карточки на столе почти не виден
        }
    }
}
