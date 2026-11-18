using System.Linq;
using UnityEngine;

/// <summary>
/// Сюжет (часть CanteenSceneBuilder): рация на кассе, клетка с Шапиком в зале Декана (подвал),
/// записка, Шапик за столом в клетке и StoryDirector со ссылками на всё это.
/// </summary>
public static partial class CanteenSceneBuilder
{
    static void BuildStory(Transform canteen, BasementInfo basement, Transform stairsDown)
    {
        var go = new GameObject("StoryDirector");
        var director = go.AddComponent<StoryDirector>();
        director.galya = Object.FindAnyObjectByType<Shopkeeper>();
        director.pistol = Object.FindObjectsByType<WeaponPickup>().FirstOrDefault(w => w.name == "Pistol");
        director.kitchenDoor = FindChild(canteen, "Door_001")?.GetComponent<SwingGate>();
        foreach (var sp in Object.FindObjectsByType<ZombieSpawner>()) sp.autoSpawn = false;

        // Рация — на кассовой стойке, на свободном от кассы краю.
        var cashier = FindChild(canteen, "Line_Right_06_C");
        var counter = cashier != null ? cashier.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name.StartsWith("Counter_")) : null;
        var register = cashier != null ? FindChild(cashier, "CashRegister") : null;
        if (counter != null)
        {
            Bounds c = RendererBounds(counter, new Bounds());
            Vector3 reg = register != null ? RendererBounds(register, new Bounds(c.center, Vector3.zero)).center : c.center;
            float x = Mathf.Abs(reg.x - c.min.x) > Mathf.Abs(reg.x - c.max.x) ? c.min.x + 0.22f : c.max.x - 0.22f;
            Vector3 p = new Vector3(x, c.max.y + 0.3f, reg.z);
            if (Physics.Raycast(p, Vector3.down, out RaycastHit hit, 1f, ~0, QueryTriggerInteraction.Ignore)) p = hit.point;
            else p.y = c.max.y;
            var radio = new GameObject("Radio");
            radio.transform.SetPositionAndRotation(p, Quaternion.Euler(0f, 25f, 0f));
            director.radio = radio.AddComponent<RadioPickup>();
        }
        else Debug.LogWarning("CanteenSceneBuilder: не найдена касса Line_Right_06_C — рации не будет, связь с Шапиком начнётся сама.");

        if (stairsDown != null)
        {
            director.stairsTop = stairsDown.TransformPoint(new Vector3(0f, 4.4f, -7.2f));
            director.stairsY = director.stairsTop.y - 1.5f;
        }
        if (basement.ok)
        {
            director.basementEntrance = basement.entrance;
            BuildShapikCage(director, basement);
        }
    }


    /// <summary>
    /// Клетка 3×3 м в зале Декана: свободное место (без ящиков, колонн, стен) в 4,5–9 м от босса, видимое с его места,
    /// подальше от входа; дверь клетки — к входу в подвал. Внутри Шапик за столом, рядом — ящик с запиской.
    /// </summary>
    static void BuildShapikCage(StoryDirector director, BasementInfo basement)
    {
        Physics.SyncTransforms();
        const float half = 1.7f; // клетка 1,5 + зазор 0,2
        float floorY = basement.entrance.y;
        Vector3 boss = basement.bossPos;
        Bounds a = basement.arena;
        bool found = false;
        Vector3 best = boss;
        float bestScore = float.MinValue;
        for (float x = a.min.x + half; x <= a.max.x - half; x += 0.5f)
            for (float z = a.min.z + half; z <= a.max.z - half; z += 0.5f)
            {
                var c = new Vector3(x, floorY, z);
                float dBoss = Vector3.Distance(c, boss), dEntrance = Vector3.Distance(c, basement.entrance);
                if (dBoss < 4.5f || dBoss > 9f || dEntrance < 4f) continue;
                if (Physics.CheckBox(c + Vector3.up * 1.4f, new Vector3(half, 1.15f, half), Quaternion.identity, ~0, QueryTriggerInteraction.Ignore)) continue;
                if (!Physics.Raycast(c + Vector3.up * 0.5f, Vector3.down, out RaycastHit floor, 0.8f, ~0, QueryTriggerInteraction.Ignore) || floor.normal.y < 0.9f) continue;
                Vector3 eyeBoss = boss + Vector3.up * 1.5f, eyeCage = c + Vector3.up * 1.5f;
                if (Physics.Raycast(eyeBoss, (eyeCage - eyeBoss).normalized, Vector3.Distance(eyeBoss, eyeCage), ~0, QueryTriggerInteraction.Ignore)) continue;
                float score = -Mathf.Abs(dBoss - 6.5f) + 0.25f * dEntrance;
                if (score > bestScore) { bestScore = score; best = c; found = true; }
            }
        if (!found) Debug.LogWarning("CanteenSceneBuilder: в зале босса не нашлось свободных 3,4 × 3,4 м под клетку — она поставлена рядом с боссом, проверьте вручную.");

        // Дверь — к входу, стены клетки — вдоль стен подвала.
        Vector3 toEntrance = basement.entrance - best; toEntrance.y = 0f;
        Vector3 front = Mathf.Abs(toEntrance.x) > Mathf.Abs(toEntrance.z)
            ? new Vector3(Mathf.Sign(toEntrance.x), 0f, 0f) : new Vector3(0f, 0f, Mathf.Sign(toEntrance.z));
        var cageGo = new GameObject("ShapikCage");
        cageGo.transform.SetPositionAndRotation(best, Quaternion.LookRotation(front, Vector3.up));
        director.shapikCage = cageGo.AddComponent<ShapikCage>();

        // Шапик — у задней стенки клетки, лицом к двери, стол перед ним.
        var shapik = new GameObject("Shapik");
        shapik.transform.SetPositionAndRotation(best - front * 0.95f, Quaternion.LookRotation(front, Vector3.up));
        director.shapik = shapik.AddComponent<ShapikNpc>();

        // Записка — на ближайшем к клетке ящике (или на полу у двери).
        Transform crate = null;
        float bestD = 8f;
        foreach (var t in basement.root.GetComponentsInChildren<Transform>(true))
        {
            if (!(t.name.StartsWith("WoodenCrate") || t.name.StartsWith("CardboardBox"))) continue;
            Bounds cb = RendererBounds(t, new Bounds(t.position, Vector3.zero));
            if (Physics.CheckBox(cb.center + Vector3.up * (cb.extents.y + 0.15f), new Vector3(0.1f, 0.1f, 0.1f), Quaternion.identity, ~0, QueryTriggerInteraction.Ignore)) continue; // сверху стоит другой ящик
            float d = Vector3.Distance(cb.center, best);
            if (d < bestD) { bestD = d; crate = t; }
        }
        Vector3 notePos;
        if (crate != null)
        {
            Bounds cb = RendererBounds(crate, new Bounds(crate.position, Vector3.zero));
            notePos = new Vector3(cb.center.x, cb.max.y + 0.003f, cb.center.z);
        }
        else notePos = best + front * 2.4f + Vector3.Cross(Vector3.up, front) * 1.2f;
        var note = new GameObject("Note");
        note.transform.position = notePos;
        note.AddComponent<StoryNote>();
    }
}
