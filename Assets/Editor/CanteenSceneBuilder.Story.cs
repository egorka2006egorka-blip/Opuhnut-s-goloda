using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEngine;

/// <summary>
/// Сюжет (часть CanteenSceneBuilder): рация на кассе, доски на задней двери подвала (отрываются ломом Декана),
/// Шапик в пультовой серверной, аварийная блокировка с тремя мини-играми, записка у заколоченной двери
/// и StoryDirector со ссылками на всё это.
/// Клетки с Шапиком в подвале пока нет (ShapikCage не ставится).
/// </summary>
public static partial class CanteenSceneBuilder
{
    static void BuildStory(Transform canteen, BasementInfo basement, Transform stairsDown, ServerRoomInfo server)
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
        if (basement.ok) director.basementEntrance = basement.entrance;
        if (server.ok)
        {
            director.boardedDoor = BuildBoardedDoor(server, basement.entrance.y, out Vector3 doorFront);
            director.shapik = BuildShapikInControlRoom(server) ?? BuildShapik(server);
            director.lockdown = BuildLockdown(server);
            PlaceNote(basement.root, doorFront, server.back);
        }
        else Debug.LogWarning("CanteenSceneBuilder: серверной нет — Шапика и заколоченной двери не будет, сюжет остановится после лома.");
        if (basement.ok) PlaceExtraNotes(basement.root);
    }

    /// <summary>
    /// Ещё две записки в зале подвала (не в котельной): на ящике в западном углу и на ящике посреди зала.
    /// Ящик ищется по имени в модели; если сверху на нём что-то стоит — записка ложится на это (луч сверху вниз).
    /// </summary>
    static void PlaceExtraNotes(Transform basement)
    {
        Physics.SyncTransforms();
        foreach (var (crateName, lineId, yaw) in new[] { ("WoodenCrate_09", "note_2", -24f), ("WoodenCrate_07", "note_3", 38f) })
        {
            var crate = FindChild(basement, crateName);
            if (crate == null) { Debug.LogWarning("CanteenSceneBuilder: в подвале нет " + crateName + " — записки " + lineId + " не будет."); continue; }
            Bounds cb = RendererBounds(crate, new Bounds(crate.position, Vector3.zero));
            Vector3 pos = new Vector3(cb.center.x, cb.max.y, cb.center.z);
            float surface = float.NegativeInfinity;
            foreach (var hit in Physics.RaycastAll(pos + Vector3.up * 0.4f, Vector3.down, 3f, ~0, QueryTriggerInteraction.Ignore))
                if (hit.point.y <= pos.y + 0.01f) surface = Mathf.Max(surface, hit.point.y);
            if (!float.IsNegativeInfinity(surface)) pos.y = surface;
            var note = new GameObject("Note_" + lineId);
            note.transform.SetPositionAndRotation(pos + Vector3.up * 0.002f, Quaternion.Euler(0f, yaw, 0f));
            note.AddComponent<StoryNote>().lineId = lineId;
        }
    }

    /// <summary>
    /// Доски на задней двери подвала, со стороны подвала (поверх коробки и полотна). Дверь заперта, пока они есть.
    /// doorFront — точка на полу перед доской, в подвале.
    /// </summary>
    static BoardedDoor BuildBoardedDoor(ServerRoomInfo server, float floorY, out Vector3 doorFront)
    {
        Bounds b = server.basementDoorFrame;
        b.Encapsulate(RendererBounds(server.basementDoor, b)); // полотно выступает из коробки в подвал
        Vector3 back = server.back, side = Vector3.Cross(Vector3.up, back);
        float depth = Mathf.Abs(b.extents.x * back.x) + Mathf.Abs(b.extents.z * back.z);
        float width = 2f * (Mathf.Abs(server.basementDoorFrame.extents.x * side.x) + Mathf.Abs(server.basementDoorFrame.extents.z * side.z));
        Vector3 face = new Vector3(b.center.x, floorY, b.center.z) - back * (depth + 0.03f); // + ручка двери

        var go = new GameObject("BoardedDoor");
        go.transform.SetPositionAndRotation(face, Quaternion.LookRotation(-back, Vector3.up));
        var boarded = go.AddComponent<BoardedDoor>();
        boarded.door = server.basementDoor.GetComponent<SwingGate>();
        boarded.width = Mathf.Clamp(width, 0.8f, 1.6f);
        boarded.height = Mathf.Clamp(server.basementDoorFrame.size.y, 1.8f, 2.4f);
        if (boarded.door == null) Debug.LogWarning("CanteenSceneBuilder: у задней двери подвала нет SwingGate — после досок она не откроется.");
        doorFront = face - back * 1.2f;
        return boarded;
    }

    /// <summary>
    /// Шапик в пультовой (там его запирает блокировка): вместо среднего пульта дальнего ряда (OperatorDesk_02 и его стул)
    /// стоит его стол; лицом и столом — к окну и двери пультовой, чтобы его было видно из зала. null — пультовой нет.
    /// </summary>
    static ShapikNpc BuildShapikInControlRoom(ServerRoomInfo server)
    {
        var room = server.root;
        var desk = FindChild(room, "OperatorDesk_02");
        var chair = FindChild(room, "OfficeChair_03");
        var door = FindChild(room, "DoorFrame_ControlRoom");
        if (desk == null || door == null) return null;
        Bounds db = RendererBounds(desk, new Bounds(desk.position, Vector3.zero));
        Vector3 toDoor = RendererBounds(door, new Bounds(door.position, Vector3.zero)).center - db.center;
        Vector3 face = AxisOf(room, toDoor);
        desk.gameObject.SetActive(false);
        if (chair != null) chair.gameObject.SetActive(false);
        Physics.SyncTransforms();

        Quaternion rot = Quaternion.LookRotation(face, Vector3.up);
        Vector3 origin = new Vector3(db.center.x, server.entry.y, db.center.z) - face * 1f; // стол Шапика (0,62 м перед ним) — на месте пульта
        if (Physics.CheckBox(origin + rot * new Vector3(0f, 0.8f, 0.27f), new Vector3(0.7f, 0.62f, 0.75f), rot, ~0, QueryTriggerInteraction.Ignore))
            Debug.LogWarning("CanteenSceneBuilder: место Шапика в пультовой задевает мебель — проверьте вручную.");
        var shapik = new GameObject("Shapik");
        shapik.transform.SetPositionAndRotation(origin, rot);
        return shapik.AddComponent<ShapikNpc>();
    }

    /// <summary>Горизонтальная ось комнаты (±right / ±forward), ближайшая к направлению v.</summary>
    static Vector3 AxisOf(Transform room, Vector3 v)
    {
        v.y = 0f;
        Vector3 r = room.right, f = room.forward;
        float dr = Vector3.Dot(v, r), df = Vector3.Dot(v, f);
        return Mathf.Abs(dr) > Mathf.Abs(df) ? r * Mathf.Sign(dr) : f * Mathf.Sign(df);
    }

    /// <summary>
    /// Аварийная блокировка серверной и три мини-игры (см. ServerLockdown):
    /// щиток с рубильниками — на распределительном щите в комнате ИБП, три заражённых сервера — в разных рядах стоек,
    /// терминал — в зале у двери пультовой (с другой стороны от окна). Плюс красные тревожные лампы и точка, откуда лезут зомби.
    /// </summary>
    static ServerLockdown BuildLockdown(ServerRoomInfo server)
    {
        var room = server.root;
        float floorY = server.entry.y;
        var go = new GameObject("ServerLockdown");
        var lk = go.AddComponent<ServerLockdown>();
        var tasks = new GameObject("LockdownTasks").transform;
        tasks.SetParent(go.transform, false);
        lk.tasks = tasks.gameObject;

        var floorT = FindChild(room, "Server_Floor");
        Bounds hall = RendererBounds(floorT, new Bounds(server.entry, new Vector3(30f, 1f, 20f)));
        lk.hall = hall;
        lk.floorY = floorY;
        lk.entry = server.entry;
        lk.inward = server.back;
        var controlGroup = FindChild(room, "Control_Room");
        Bounds cr = RendererBounds(controlGroup, new Bounds(Vector3.zero, Vector3.zero));
        cr.Expand(new Vector3(0.8f, 20f, 0.8f));
        lk.controlRoom = cr;
        var controlDoor = FindChild(room, "Door_ControlRoom");
        lk.controlDoor = controlDoor != null ? controlDoor.GetComponent<SwingGate>() : null;
        if (lk.controlDoor == null) Debug.LogWarning("CanteenSceneBuilder: у двери пультовой нет SwingGate — Шапик не будет заперт.");
        Physics.SyncTransforms();

        // Зомби лезут через аварийный выход серверной (Door_Exit на дальней стене) — точка в зале перед ним.
        var exit = FindChild(room, "Door_Exit");
        var spawns = new List<Vector3>();
        if (exit != null)
        {
            Vector3 e = RendererBounds(exit, new Bounds(exit.position, Vector3.zero)).center;
            Vector3 inside = AxisOf(room, hall.center - e);
            spawns.Add(new Vector3(e.x, floorY, e.z) + inside * 1.3f);
        }
        lk.spawnPoints = spawns.ToArray();

        // 1. Рубильники — на лицевой стороне распределительного щита в комнате ИБП.
        var board = FindChild(room, "Switchboard_01");
        if (board != null)
        {
            Bounds b = RendererBounds(board, new Bounds(board.position, Vector3.zero));
            bool xThin = b.size.x < b.size.z;
            Vector3 axis = xThin ? Vector3.right : Vector3.forward;
            float ext = xThin ? b.extents.x : b.extents.z;
            Vector3 front = FreeAhead(b.center, axis, ext, board) >= FreeAhead(b.center, -axis, ext, board) ? axis : -axis;
            var panel = new GameObject("BreakerPanel");
            panel.transform.SetParent(tasks, false);
            panel.transform.SetPositionAndRotation(new Vector3(b.center.x, floorY + 1.35f, b.center.z) + front * (ext + 0.12f), Quaternion.LookRotation(front, Vector3.up));
            lk.breakers = panel.AddComponent<BreakerPanel>();
        }
        else Debug.LogWarning("CanteenSceneBuilder: нет щита Switchboard_01 — мини-игры с рубильниками не будет.");

        // 2. Заражённые серверы — по одному в трёх разных рядах стоек, в разных местах ряда, на стороне, обращённой в проход.
        var rows = new SortedDictionary<char, List<Transform>>();
        foreach (var t in room.GetComponentsInChildren<Transform>(true))
            if (Regex.IsMatch(t.name, @"^Rack_[A-Z]\d+$") && t.GetComponentInChildren<Renderer>() != null)
            {
                if (!rows.TryGetValue(t.name[5], out var list)) rows[t.name[5]] = list = new List<Transform>();
                list.Add(t);
            }
        var rowKeys = new List<char>(rows.Keys);
        var servers = new List<InfectedServer>();
        float[] along = { 0.2f, 0.8f, 0.5f };
        for (int k = 0; k < 3 && rowKeys.Count > 0; k++)
        {
            var row = rows[rowKeys[Mathf.Min(rowKeys.Count - 1, Mathf.RoundToInt((k + 0.5f) * rowKeys.Count / 3f - 0.5f))]];
            row.Sort((a, c) => a.name.CompareTo(c.name));
            int start = Mathf.RoundToInt(along[k] * (row.Count - 1));
            // От выбранной стойки — к соседним, пока не найдётся свободная лицевая сторона.
            for (int d = 0; d < row.Count; d++)
            {
                var rack = row[(start + (d % 2 == 0 ? d / 2 : row.Count - 1 - d / 2)) % row.Count];
                Bounds rb = RendererBounds(rack, new Bounds(rack.position, Vector3.zero));
                Vector3 n = rb.size.z > rb.size.x ? Vector3.forward : Vector3.right; // лицевая сторона — поперёк ряда
                float ext = rb.size.z > rb.size.x ? rb.extents.z : rb.extents.x;
                Vector3 side = Vector3.zero;
                foreach (var s in new[] { n, -n })
                    if (FreeAhead(new Vector3(rb.center.x, floorY + 1.25f, rb.center.z), s, ext, rack) >= 0.9f) { side = s; break; }
                if (side == Vector3.zero) continue;
                var srv = new GameObject("InfectedServer_" + rack.name);
                srv.transform.SetParent(tasks, false);
                srv.transform.SetPositionAndRotation(new Vector3(rb.center.x, floorY + 1.25f, rb.center.z) + side * (ext + 0.005f), Quaternion.LookRotation(side, Vector3.up));
                servers.Add(srv.AddComponent<InfectedServer>());
                break;
            }
        }
        lk.servers = servers.ToArray();
        if (servers.Count < 3) Debug.LogWarning("CanteenSceneBuilder: заражённых серверов поставлено " + servers.Count + " из 3 — проверьте ряды стоек.");

        // 3. Терминал — в зале у двери пультовой, с другой стороны от окна, в 0,45 м от стены.
        var frame = FindChild(room, "DoorFrame_ControlRoom");
        var window = FindChild(room, "Window_ControlRoom");
        if (frame != null)
        {
            Bounds fb = RendererBounds(frame, new Bounds(frame.position, Vector3.zero));
            bool xThin = fb.size.x < fb.size.z;
            Vector3 normal = xThin ? Vector3.right : Vector3.forward, wall = xThin ? Vector3.forward : Vector3.right;
            Vector3 toHall = hall.center - fb.center;
            Vector3 hallward = Vector3.Dot(toHall, normal) >= 0f ? normal : -normal;
            if (cr.Contains(new Vector3(fb.center.x, cr.center.y, fb.center.z) + hallward * 1.5f)) hallward = -hallward; // зал — не пультовая
            Vector3 alongWall = wall;
            if (window != null && Vector3.Dot(RendererBounds(window, fb).center - fb.center, wall) > 0f) alongWall = -wall;
            float half = xThin ? fb.extents.z : fb.extents.x, thick = xThin ? fb.extents.x : fb.extents.z;
            var term = new GameObject("HackTerminal");
            term.transform.SetParent(tasks, false);
            term.transform.SetPositionAndRotation(new Vector3(fb.center.x, floorY, fb.center.z) + alongWall * (half + 0.55f) + hallward * (thick + 0.25f),
                Quaternion.LookRotation(hallward, Vector3.up));
            lk.terminal = term.AddComponent<HackTerminal>();
        }
        else Debug.LogWarning("CanteenSceneBuilder: нет двери пультовой — терминала не будет.");

        // Тревога: красные пульсирующие лампы под потолком зала (сеткой 3 × 3, кроме пультовой).
        var alarm = new GameObject("LockdownAlarm");
        alarm.transform.SetParent(go.transform, false);
        lk.alarmLights = alarm;
        for (int ix = 0; ix < 3; ix++)
            for (int iz = 0; iz < 3; iz++)
            {
                Vector3 p = new Vector3(Mathf.Lerp(hall.min.x + 3f, hall.max.x - 3f, ix / 2f), floorY + 2.9f, Mathf.Lerp(hall.min.z + 3f, hall.max.z - 3f, iz / 2f));
                if (cr.Contains(new Vector3(p.x, cr.center.y, p.z))) continue;
                var lgo = new GameObject("AlarmLight");
                lgo.transform.SetParent(alarm.transform, false);
                lgo.transform.position = p;
                var l = lgo.AddComponent<Light>();
                l.type = LightType.Point;
                l.color = new Color(1f, 0.08f, 0.05f);
                l.range = 10f;
                l.intensity = 1.8f;
                l.shadows = LightShadows.None;
                lgo.AddComponent<FlickerLight>().pulse = 1f;
            }
        alarm.SetActive(false);
        tasks.gameObject.SetActive(false); // задачи появляются, когда срабатывает блокировка
        return lk;
    }

    /// <summary>Сколько свободного места (до 1,5 м) от грани габарита (полуразмер ext) в направлении dir; свой объект не в счёт.</summary>
    static float FreeAhead(Vector3 center, Vector3 dir, float ext, Transform self)
    {
        float best = 1.5f;
        foreach (var h in Physics.RaycastAll(center, dir, ext + 1.5f, ~0, QueryTriggerInteraction.Ignore))
        {
            if (h.transform.IsChildOf(self)) continue;
            best = Mathf.Min(best, Mathf.Max(0f, h.distance - ext));
        }
        return best;
    }

    /// <summary>
    /// Шапик за столом в серверной: в самом дальнем свободном месте на прямой от входной двери вглубь зала
    /// (по центральному проходу между стойками), видимом от входа; лицом и столом — к входу.
    /// </summary>
    static ShapikNpc BuildShapik(ServerRoomInfo server)
    {
        Physics.SyncTransforms();
        Vector3 back = server.back;
        Quaternion face = Quaternion.LookRotation(-back, Vector3.up);
        // Габариты Шапика со столом и стулом (начало координат — на полу под ним, стол — по +Z).
        Vector3 boxCenter = new Vector3(0f, 0.8f, 0.27f), boxHalf = new Vector3(0.7f, 0.62f, 0.75f);
        Vector3 eye = server.entry + back * 0.8f + Vector3.up * 1.5f;
        Vector3 best = server.entry + back * 3f;
        bool found = false;
        for (float d = 3f; d <= 24f; d += 0.25f)
        {
            Vector3 c = server.entry + back * d;
            if (Physics.CheckBox(c + face * boxCenter, boxHalf, face, ~0, QueryTriggerInteraction.Ignore)) continue;
            if (!Physics.Raycast(c + Vector3.up * 0.5f, Vector3.down, out RaycastHit floor, 0.8f, ~0, QueryTriggerInteraction.Ignore) || floor.normal.y < 0.9f) continue;
            Vector3 head = c + Vector3.up * 1.2f;
            if (Physics.Raycast(eye, (head - eye).normalized, Vector3.Distance(eye, head) - 0.3f, ~0, QueryTriggerInteraction.Ignore)) continue;
            best = new Vector3(c.x, floor.point.y, c.z);
            found = true;
        }
        if (!found) Debug.LogWarning("CanteenSceneBuilder: в серверной не нашлось свободного места для Шапика на линии от входа — он поставлен в 3 м от двери, проверьте вручную.");

        var shapik = new GameObject("Shapik");
        shapik.transform.SetPositionAndRotation(best, face);
        return shapik.AddComponent<ShapikNpc>();
    }

    /// <summary>Записка — на ближайшем к заколоченной двери ящике в подвале (или на полу у двери).</summary>
    static void PlaceNote(Transform basement, Vector3 doorFront, Vector3 back)
    {
        Physics.SyncTransforms();
        Transform crate = null;
        float bestD = 8f;
        if (basement != null)
            foreach (var t in basement.GetComponentsInChildren<Transform>(true))
            {
                if (!(t.name.StartsWith("WoodenCrate") || t.name.StartsWith("CardboardBox"))) continue;
                Bounds cb = RendererBounds(t, new Bounds(t.position, Vector3.zero));
                if (Physics.CheckBox(cb.center + Vector3.up * (cb.extents.y + 0.15f), new Vector3(0.1f, 0.1f, 0.1f), Quaternion.identity, ~0, QueryTriggerInteraction.Ignore)) continue; // сверху стоит другой ящик
                float d = Vector3.Distance(cb.center, doorFront);
                if (d < bestD) { bestD = d; crate = t; }
            }
        Vector3 notePos;
        if (crate != null)
        {
            Bounds cb = RendererBounds(crate, new Bounds(crate.position, Vector3.zero));
            notePos = new Vector3(cb.center.x, cb.max.y + 0.003f, cb.center.z);
        }
        else notePos = doorFront + Vector3.Cross(Vector3.up, back) * 0.8f;
        // Кладём на то, что под запиской на самом деле: у открытой коробки верх габарита — края клапанов,
        // а крышки нет, и записка висела в воздухе. Луч сверху вниз находит содержимое коробки, дно или пол.
        // Над коробкой могут идти трубы — берём самое высокое попадание не выше прежней точки.
        float surface = float.NegativeInfinity;
        foreach (var hit in Physics.RaycastAll(notePos + Vector3.up * 0.4f, Vector3.down, 3f, ~0, QueryTriggerInteraction.Ignore))
            if (hit.point.y <= notePos.y + 0.01f) surface = Mathf.Max(surface, hit.point.y);
        if (!float.IsNegativeInfinity(surface)) notePos.y = surface + 0.002f;
        var note = new GameObject("Note");
        note.transform.position = notePos;
        note.AddComponent<StoryNote>();
    }
}
