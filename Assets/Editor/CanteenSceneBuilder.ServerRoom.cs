using UnityEditor;
using UnityEngine;

/// <summary>
/// Серверная (часть CanteenSceneBuilder). ServerRoom_LowPoly.glb смоделирована в координатах модели подвала: её коридор
/// начинается у наружной грани задней двери подвала Door_Exit. Между ними ставится лестничный пролёт наверх
/// (копия Entrance2_StairTunnel_Up: подъём 4,4 м на 7,2 м), поэтому серверная — с тем же поворотом, что и подвал,
/// но выше на высоту пролёта и дальше на его длину. Дверь подвала потом заколачивается (сюжет, BoardedDoor).
/// </summary>
public static partial class CanteenSceneBuilder
{
    const string ServerRoomPath = "Assets/Models location/ServerRoom_LowPoly.glb";
    /// <summary>Пролёт лестничного тоннеля: нижний порог — начало координат, верхний — (0; 4,4; −7,2).</summary>
    const float StairRise = 4.4f, StairRun = 7.2f;

    struct ServerRoomInfo
    {
        public bool ok;
        public Transform root;
        /// <summary>Задняя дверь подвала (на лестницу) и габариты её коробки.</summary>
        public Transform basementDoor;
        public Bounds basementDoorFrame;
        /// <summary>Горизонталь из подвала в сторону серверной.</summary>
        public Vector3 back;
        /// <summary>Пол у входной двери серверной (Door_Entry).</summary>
        public Vector3 entry;
    }

    static ServerRoomInfo BuildServerRoom(Transform canteen, BasementInfo basement)
    {
        var info = new ServerRoomInfo();
        if (!basement.ok) return info;
        var asset = AssetDatabase.LoadAssetAtPath<GameObject>(ServerRoomPath);
        var source = FindChild(canteen, "Entrance2_StairTunnel_Up");
        Transform b = basement.root;
        var frame = FindChild(b, "DoorFrame_Exit");
        var door = FindChild(b, "Door_Exit");
        if (asset == null || source == null || frame == null || door == null)
        {
            Debug.LogWarning(asset == null ? "CanteenSceneBuilder: нет модели серверной " + ServerRoomPath + " — серверной не будет."
                : "CanteenSceneBuilder: нет лестницы Entrance2_StairTunnel_Up или задней двери подвала (DoorFrame_Exit / Door_Exit) — серверной не будет.");
            return info;
        }
        Vector3 back = b.rotation * Vector3.back; back.y = 0f; back.Normalize();

        // Лестница: нижний порог — у наружной грани коробки двери подвала, подъём — от подвала.
        Bounds fb = RendererBounds(frame, new Bounds(frame.position, Vector3.zero));
        float half = Mathf.Abs(fb.extents.x * back.x) + Mathf.Abs(fb.extents.z * back.z);
        Vector3 bottom = new Vector3(fb.center.x, basement.entrance.y, fb.center.z) + back * half;
        var stairs = Object.Instantiate(source.gameObject).transform;
        stairs.name = "Entrance5_StairTunnel_ServerRoom";
        stairs.rotation = Quaternion.LookRotation(-back); // в модели подъём идёт вдоль локальной −Z
        stairs.position = bottom;
        // Внизу — своя дверь подвала (заколоченная), наверху — коридор серверной со своей дверью: двери тоннеля не нужны.
        FindChild(stairs, "Door_Bottom")?.gameObject.SetActive(false);
        FindChild(stairs, "Door_Top")?.gameObject.SetActive(false);
        AddTunnelLights(stairs);

        // Серверная — вплотную к верхнему порогу.
        var room = Spawn(asset, "ServerRoom");
        room.transform.SetPositionAndRotation(b.position + Vector3.up * StairRise + back * StairRun, b.rotation);
        room.transform.localScale = b.localScale;
        // Плоские и потолочные мелочи — без коллайдеров.
        MakeStaticWithColliders(room, "Floor_PerforatedTiles", "RubberMat", "CardReader", "Sign_", "SmokeDetector",
            "SecurityCamera", "CeilingLight", "BulkheadLamp", "WallClock");
        LevelPropsSetup.SetupGrabbableProps(room);

        // Коридор серверной (2 м) шире тоннеля (1,6 м): щели по бокам на стыке закрываются вставками из материала стен.
        Vector3 top = bottom + Vector3.up * StairRise + back * StairRun;
        Vector3 side = Vector3.Cross(Vector3.up, back);
        var walls = FindChild(room.transform, "Server_Walls");
        var wallRenderer = walls != null ? walls.GetComponent<Renderer>() : null;
        foreach (float s in new[] { -1f, 1f })
        {
            var filler = GameObject.CreatePrimitive(PrimitiveType.Cube);
            filler.name = "StairJunction_Filler";
            filler.transform.SetParent(room.transform, false);
            filler.transform.SetPositionAndRotation(top + side * s * 0.92f + back * 0.06f + Vector3.up * 1.35f, Quaternion.LookRotation(back));
            filler.transform.localScale = new Vector3(0.24f, 2.7f, 0.12f);
            if (wallRenderer != null) filler.GetComponent<Renderer>().sharedMaterial = wallRenderer.sharedMaterial;
            GameObjectUtility.SetStaticEditorFlags(filler, StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccluderStatic | StaticEditorFlags.OccludeeStatic);
        }

        // Двери: задняя дверь подвала (откроется, когда оторвут доски), вход в серверную, пультовая, комната ИБП.
        // Door_Exit серверной ведёт наружу модели — она остаётся закрытой.
        SetupModelDoor(door);
        // В модели дверь пультовой приоткрыта (повёрнута на 115° вместо 90°) — между ней и коробкой щель.
        // Закрываем её: закрытая створка повёрнута так же, как коробка (петля — в pivot створки, в коробке).
        var crDoor = FindChild(room.transform, "Door_ControlRoom");
        var crFrame = FindChild(room.transform, "DoorFrame_ControlRoom");
        if (crDoor != null && crFrame != null && crDoor.parent == crFrame.parent) crDoor.localRotation = crFrame.localRotation;
        foreach (var name in new[] { "Door_Entry", "Door_ControlRoom", "Door_UPS" })
            SetupModelDoor(FindChild(room.transform, name));

        // Свет: холодный белый под каждой работающей потолочной лампой (CeilingLight_Off_* — перегоревшие), тёплый — над входом.
        var lights = new GameObject("ServerRoom Lights").transform;
        foreach (var t in room.GetComponentsInChildren<Transform>(true))
        {
            bool ceiling = t.name.StartsWith("CeilingLight_") && !t.name.StartsWith("CeilingLight_Off");
            bool bulkhead = t.name.StartsWith("BulkheadLamp");
            if (!ceiling && !bulkhead) continue;
            Bounds lb = RendererBounds(t, new Bounds(t.position, Vector3.zero));
            var go = new GameObject("ServerLight_" + t.name);
            go.transform.SetParent(lights, false);
            go.transform.position = new Vector3(lb.center.x, lb.min.y - 0.25f, lb.center.z);
            var l = go.AddComponent<Light>();
            l.type = LightType.Point;
            l.shadows = LightShadows.None;
            if (bulkhead) { l.color = new Color(1f, 0.85f, 0.6f); l.range = 5f; l.intensity = 1.2f; }
            else { l.color = new Color(0.82f, 0.92f, 1f); l.range = 4.5f; l.intensity = 1.1f; }
        }

        var floorT = FindChild(room.transform, "Server_Floor");
        float floorY = floorT != null ? RendererBounds(floorT, new Bounds(top, Vector3.zero)).max.y : top.y;
        var entryDoor = FindChild(room.transform, "Door_Entry");
        Vector3 entry = entryDoor != null ? RendererBounds(entryDoor, new Bounds(entryDoor.position, Vector3.zero)).center : top + back * 4f;
        entry.y = floorY;

        info.ok = true;
        info.root = room.transform;
        info.basementDoor = door;
        info.basementDoorFrame = fb;
        info.back = back;
        info.entry = entry;
        return info;
    }

    /// <summary>Свет вдоль пролёта лестничного тоннеля, под потолком (как в остальных пролётах).</summary>
    static void AddTunnelLights(Transform stairs)
    {
        foreach (var p in new[] { new Vector3(0f, 2.1f, -0.7f), new Vector3(0f, 4.3f, -3.6f), new Vector3(0f, 6.5f, -6.4f) })
        {
            var go = new GameObject("Light");
            go.transform.SetParent(stairs, false);
            go.transform.localPosition = p;
            var l = go.AddComponent<Light>();
            l.type = LightType.Point;
            l.range = 4.5f;
            l.intensity = 1.4f;
            l.color = new Color(0.97f, 0.98f, 1f);
            l.shadows = LightShadows.None;
        }
    }

    /// <summary>SetupDoor для двери-меша из модели (без меша на самом узле петлю не найти — дверь остаётся закрытой).</summary>
    static void SetupModelDoor(Transform door)
    {
        if (door == null) return;
        if (door.GetComponent<MeshFilter>() == null)
        {
            Debug.LogWarning("CanteenSceneBuilder: у двери " + door.name + " нет меша на самом объекте — она не открывается.");
            return;
        }
        SetupDoor(door);
    }
}
