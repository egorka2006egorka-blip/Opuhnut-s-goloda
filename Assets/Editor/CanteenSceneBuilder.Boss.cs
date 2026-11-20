using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Подвал с боссом и магазин в столовой (часть CanteenSceneBuilder):
///  • подвал (Basement_LowPoly.glb) — под кухней: к лестнице вниз пристраивается второй пролёт, и подвал ставится так,
///    чтобы проём в его передней стене совпал с нижней дверью пролёта;
///  • босс (BlindHunter.glb) — модель без скелета: строится процедурный скелет, вершины привязываются к костям
///    (по расстоянию до кости), скинованный меш сохраняется в Assets/Characters/Boss; анимирует его ZombieBoss;
///  • продавщица за прилавком с горячим (Line_Right), оружие лежит на прилавке со стороны зала как товар.
/// </summary>
public static partial class CanteenSceneBuilder
{
    const string BasementPath = "Assets/Models location/Basement_LowPoly.glb";
    const string BossModelPath = "Assets/Characters/BlindHunter.glb";
    const string BossMeshPath = "Assets/Characters/Boss/BlindHunter_Rigged.asset";
    /// <summary>Если положить модель продавщицы по одному из этих путей — она заменит модель из примитивов.</summary>
    static readonly string[] SellerModelPaths = { "Assets/Characters/Seller.glb", "Assets/Characters/Shopkeeper.glb", "Assets/Characters/Prodavshitsa.glb" };
    /// <summary>Рост босса: модель 2,9 м, балки потолка подвала на высоте 2,85 м.</summary>
    const float BossScale = 0.95f;

    struct BasementInfo
    {
        public bool ok;
        public Transform root;
        /// <summary>Вход в подвал (нижний порог лестницы).</summary>
        public Vector3 entrance;
        public Bounds arena;
        public Vector3 bossPos, bossForward;
    }

    // ---------------------------------------------------------------- Подвал

    static BasementInfo BuildBasement(Transform canteen, Transform stairsDown)
    {
        var info = new BasementInfo();
        var asset = AssetDatabase.LoadAssetAtPath<GameObject>(BasementPath);
        var source = FindChild(canteen, "Entrance2_StairTunnel_Up");
        if (asset == null || stairsDown == null || source == null)
        {
            if (asset == null) Debug.LogWarning("CanteenSceneBuilder: нет модели подвала " + BasementPath + " — босса не будет.");
            return info;
        }

        // Второй пролёт: продолжает лестницу вниз — его верхний порог (0; 4,4; −7,2) встаёт на нижний порог первого.
        var stairs = Object.Instantiate(source.gameObject).transform;
        stairs.name = "Entrance4_StairTunnel_Basement";
        stairs.rotation = stairsDown.rotation;
        stairs.position += stairsDown.position - stairs.TransformPoint(new Vector3(0f, 4.4f, -7.2f));
        // Закрытая нижняя дверь первого пролёта перегородила бы проход — вместо неё дверь второго пролёта.
        FindChild(stairsDown, "Door_Bottom")?.gameObject.SetActive(false);
        var top = FindChild(stairs, "Door_Top");
        if (top != null) SetupDoor(FindChild(top, "Door"));
        var bottom = FindChild(stairs, "Door_Bottom");
        if (bottom != null) SetupDoor(FindChild(bottom, "Door"));
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

        // Сам подвал. Плоские пятна, лужи, бумаги и паутина — без коллайдеров.
        var basement = Spawn(asset, "Basement");
        Vector3 outward = stairsDown.forward; outward.y = 0f; outward.Normalize();
        // Модель сделана со входом в стене, обращённой к +Z (лестница спускается в −Z). Иначе — разворачиваем.
        if (outward.z > 0.5f) basement.transform.rotation = Quaternion.Euler(0f, 180f, 0f);
        MakeStaticWithColliders(basement, "Cobweb", "Puddle", "Stain", "Papers", "HangingBulb", "WarningLamp");
        Physics.SyncTransforms();

        var walls = FindChild(basement.transform, "Basement_Walls");
        var floorT = FindChild(basement.transform, "Basement_Floor");
        if (walls == null || floorT == null) { Debug.LogWarning("CanteenSceneBuilder: в модели подвала нет Basement_Walls / Basement_Floor."); return info; }
        Bounds wb = RendererBounds(walls, new Bounds());
        float floorY = RendererBounds(floorT, new Bounds()).max.y;
        float frontZ = outward.z < 0f ? wb.max.z : wb.min.z;

        // Проём в передней стене: лучи на высоте 1 м вдоль стены — ищем просвет шириной с дверь, ближайший к лестнице.
        float bestX = float.NaN, bestScore = float.MaxValue;
        float gapStart = float.NaN;
        const float step = 0.05f;
        for (float x = wb.min.x; x <= wb.max.x + step; x += step)
        {
            bool open = x <= wb.max.x && !HitsOwn(basement.transform, new Vector3(x, floorY + 1f, frontZ - outward.z * 0.3f), outward, 0.6f);
            if (open && float.IsNaN(gapStart)) gapStart = x;
            else if (!open && !float.IsNaN(gapStart))
            {
                float w = x - gapStart, c = (gapStart + x - step) / 2f;
                if (w > 0.6f && w < 1.6f)
                {
                    float score = Mathf.Abs(c - stairs.position.x);
                    if (score < bestScore) { bestScore = score; bestX = c; }
                }
                gapStart = float.NaN;
            }
        }
        if (float.IsNaN(bestX))
        {
            Debug.LogWarning("CanteenSceneBuilder: не найден проём в стене подвала — вход ставится по центру стены.");
            bestX = wb.center.x;
        }
        Vector3 opening = new Vector3(bestX, floorY, frontZ);
        Vector3 shift = stairs.position - opening;
        basement.transform.position += shift;
        Physics.SyncTransforms();

        // Свет: под каждой лампочкой — тусклая тёплая лампа (разбитая мигает), над выходом — красная аварийная.
        var lights = new GameObject("Basement Lights").transform;
        foreach (var t in basement.GetComponentsInChildren<Transform>(true))
        {
            bool bulb = t.name.StartsWith("HangingBulb"), warn = t.name.StartsWith("WarningLamp");
            if (!bulb && !warn) continue;
            Bounds b = RendererBounds(t, new Bounds(t.position, Vector3.zero));
            var go = new GameObject("BasementLight_" + t.name);
            go.transform.SetParent(lights, false);
            go.transform.position = bulb ? new Vector3(b.center.x, b.min.y - 0.12f, b.center.z) : b.center - outward * 0.3f;
            var l = go.AddComponent<Light>();
            l.type = LightType.Point;
            l.shadows = LightShadows.None;
            var f = go.AddComponent<FlickerLight>();
            if (warn) { l.color = new Color(1f, 0.12f, 0.08f); l.range = 6f; l.intensity = 1.6f; f.flicker = 0f; f.pulse = 0.8f; }
            else if (t.name.Contains("Broken")) { l.color = new Color(1f, 0.75f, 0.5f); l.range = 6f; l.intensity = 0.9f; f.flicker = 0.9f; }
            else { l.color = new Color(1f, 0.78f, 0.52f); l.range = 7.5f; l.intensity = 0.8f; f.flicker = 0.15f; }
        }

        SetupCodeLockedDoor(basement.transform, opening + shift, floorY);
        FurnishBoilerRoom(canteen, basement.transform, floorY);

        Bounds fb = RendererBounds(floorT, new Bounds());
        info.arena = new Bounds(new Vector3(fb.center.x, floorY + 1.6f, fb.center.z), new Vector3(fb.size.x, 3.6f, fb.size.z));
        // Босс — в глубине главного зала, напротив входа, лицом к двери.
        Vector3 door = opening + shift;
        info.bossForward = -outward;
        info.bossPos = FreeSpot(door + outward * 9.3f, basement.transform);
        info.root = basement.transform;
        info.entrance = door;
        info.ok = true;
        return info;
    }

    /// <summary>Луч попадает в коллайдер самого объекта root (не в другие объекты сцены).</summary>
    static bool HitsOwn(Transform root, Vector3 from, Vector3 dir, float length)
    {
        foreach (var h in Physics.RaycastAll(from, dir, length, ~0, QueryTriggerInteraction.Ignore))
            if (h.transform.IsChildOf(root)) return true;
        return false;
    }

    /// <summary>Ближайшая к p точка, где босс (капсула ~0,6 м) ни во что не упирается.</summary>
    static Vector3 FreeSpot(Vector3 p, Transform root)
    {
        for (int ring = 0; ring < 6; ring++)
            for (int k = 0; k < Mathf.Max(1, ring * 8); k++)
            {
                float a = k * Mathf.PI * 2f / Mathf.Max(1, ring * 8);
                Vector3 c = p + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * ring * 0.5f;
                if (!Physics.CheckCapsule(c + Vector3.up * 0.8f, c + Vector3.up * 2.2f, 0.6f, ~0, QueryTriggerInteraction.Ignore)) return c;
            }
        return p;
    }

    /// <summary>
    /// Дверь в котельную подвала (Door_RoomA): в модели приоткрыта на 55° — ставим закрытой (поворот — к ближайшим 90°)
    /// и вешаем кодовый замок (CodeLock, код 2402) на стену у свободного края двери, со стороны зала.
    /// </summary>
    static void SetupCodeLockedDoor(Transform basement, Vector3 hall, float floorY)
    {
        var door = FindChild(basement, "Door_RoomA");
        var mf = door != null ? door.GetComponent<MeshFilter>() : null;
        if (mf == null || mf.sharedMesh == null) { Debug.LogWarning("CanteenSceneBuilder: в подвале нет двери Door_RoomA — кодового замка не будет."); return; }
        Vector3 e = door.localEulerAngles;
        door.localRotation = Quaternion.Euler(e.x, Mathf.Round(e.y / 90f) * 90f, e.z);
        SetupDoor(door);
        var gate = door.GetComponent<SwingGate>();
        if (gate == null) return;

        // Полотно (без ручки): ширина и толщина по вершинам первого подмеша.
        var mesh = mf.sharedMesh;
        var v = mesh.vertices;
        Bounds slab = new Bounds(v[mesh.GetTriangles(0)[0]], Vector3.zero);
        foreach (int i in mesh.GetTriangles(0)) slab.Encapsulate(v[i]);
        Vector3 hinge = door.parent.TransformPoint(gate.hinges[0]);
        Vector3 center = door.TransformPoint(slab.center);
        Vector3 along = center - hinge; along.y = 0f; along.Normalize();
        Vector3 normal = door.forward; normal.y = 0f; normal.Normalize();
        Vector3 side = Vector3.Dot(hall - center, normal) >= 0f ? normal : -normal; // к залу
        Vector3 faceA = door.TransformPoint(new Vector3(slab.center.x, slab.center.y, slab.min.z));
        Vector3 faceB = door.TransformPoint(new Vector3(slab.center.x, slab.center.y, slab.max.z));
        Vector3 face = Vector3.Dot(faceA - faceB, side) > 0f ? faceA : faceB;
        float width = Vector3.Distance(Vector3.ProjectOnPlane(door.TransformPoint(new Vector3(slab.min.x, 0f, 0f)), Vector3.up),
                                       Vector3.ProjectOnPlane(door.TransformPoint(new Vector3(slab.max.x, 0f, 0f)), Vector3.up));

        // Проушина — на стене в 6 см за свободным краем двери, на высоте 1,3 м.
        Vector3 pos = hinge + along * (width + 0.06f);
        pos += side * Vector3.Dot(face - pos, side);
        pos.y = floorY + 1.3f;
        var go = new GameObject("CodeLock_" + door.name);
        go.transform.SetParent(basement, true);
        go.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(side, Vector3.up));
        var lk = go.AddComponent<CodeLock>();
        lk.code = "2402";
        lk.gate = gate;
        lk.doorDir = Vector3.Dot(-along, go.transform.right) >= 0f ? 1f : -1f;
    }

    /// <summary>
    /// Стол и стул в котельной (за дверью с кодовым замком) — копии столовских Table_001 и Chair_001.
    /// Стоят в дальнем конце комнаты, у стены напротив двери, между баком с водой и бочками: стул — у стены,
    /// сидящий за столом смотрит на вход.
    /// Места заданы в координатах модели подвала (.glb, метры по X и Z); в сцену они переводятся по трём опорным
    /// объектам котельной — так не важно, как модель повёрнута и отражена при импорте.
    /// </summary>
    static void FurnishBoilerRoom(Transform canteen, Transform basement, float floorY)
    {
        // Центры опорных объектов в модели подвала (X, Z).
        var anchors = new (string name, Vector2 model)[]
        {
            ("Boiler_01", new Vector2(13.2025f, -23.301f)),
            ("WaterTank_01", new Vector2(17.4f, -24.256f)),
            ("ElectricalPanel_01", new Vector2(11.3f, -20.343f)),
        };
        var world = new Vector2[3];
        for (int i = 0; i < 3; i++)
        {
            var t = FindChild(basement, anchors[i].name);
            if (t == null) { Debug.LogWarning("CanteenSceneBuilder: в подвале нет " + anchors[i].name + " — стола и стула в котельной не будет."); return; }
            Vector3 c = RendererBounds(t, new Bounds(t.position, Vector3.zero)).center;
            world[i] = new Vector2(c.x, c.z);
        }
        // Аффинное отображение модель → сцена по трём точкам: w = M·(m − m0) + w0.
        Vector2 m1 = anchors[1].model - anchors[0].model, m2 = anchors[2].model - anchors[0].model;
        Vector2 w1 = world[1] - world[0], w2 = world[2] - world[0];
        float det = m1.x * m2.y - m2.x * m1.y;
        Vector2 col0 = (w1 * m2.y - w2 * m1.y) / det;  // M·(1, 0)
        Vector2 col1 = (w2 * m1.x - w1 * m2.x) / det;  // M·(0, 1)
        Vector3 ToWorld(Vector2 m)
        {
            Vector2 d = m - anchors[0].model;
            Vector2 w = world[0] + col0 * d.x + col1 * d.y;
            return new Vector3(w.x, floorY, w.y);
        }
        Vector3 Dir(Vector2 m) { var w = col0 * m.x + col1 * m.y; return new Vector3(w.x, 0f, w.y).normalized; }

        var table = canteen != null ? FindChild(canteen, "Table_001") : null;
        var chair = canteen != null ? FindChild(canteen, "Chair_001") : null;
        if (table == null || chair == null) { Debug.LogWarning("CanteenSceneBuilder: в столовой нет Table_001 / Chair_001 — стола и стула в котельной не будет."); return; }

        // Стул в столовой смотрит на свой стол: направление — от стула к столу, по ближайшей оси.
        Vector3 tc = RendererBounds(table, new Bounds(table.position, Vector3.zero)).center;
        Vector3 cc = RendererBounds(chair, new Bounds(chair.position, Vector3.zero)).center;
        Vector3 chairFacing = SnapAxis(tc - cc);
        Bounds tb = RendererBounds(table, new Bounds(table.position, Vector3.zero));
        Vector3 tableLong = tb.size.x >= tb.size.z ? Vector3.right : Vector3.forward;

        // В модели дверь — в западной стене (X = 10, Z ≈ −22,43), дальняя стена — восточная (X ≈ 18,25).
        // Стол — напротив двери, длинной стороной поперёк комнаты (вдоль оси Z модели); за ним — стул (между спинкой
        // и стеной ~0,5 м), развёрнутый к двери (на −X модели). На этот стол аптечки и патроны не кладутся (LevelPropsSetup).
        Vector3 across = Dir(Vector2.up), toDoor = Dir(Vector2.left);
        PlaceCopy(table, "BoilerRoom_Table", basement, ToWorld(new Vector2(16.6f, -22.4f)), tableLong, across, floorY);
        PlaceCopy(chair, "BoilerRoom_Chair", basement, ToWorld(new Vector2(17.26f, -22.4f)), chairFacing, toDoor, floorY);
    }

    static Vector3 SnapAxis(Vector3 v) =>
        Mathf.Abs(v.x) >= Mathf.Abs(v.z) ? new Vector3(Mathf.Sign(v.x), 0f, 0f) : new Vector3(0f, 0f, Mathf.Sign(v.z));

    /// <summary>Копия объекта: направление from поворачивается в to (вокруг вертикали), центр — в target, низ — на пол.</summary>
    static void PlaceCopy(Transform source, string name, Transform parent, Vector3 target, Vector3 from, Vector3 to, float floorY)
    {
        var copy = Object.Instantiate(source.gameObject, source.parent).transform;
        copy.name = name;
        copy.SetParent(parent, true);
        Bounds b = RendererBounds(copy, new Bounds(copy.position, Vector3.zero));
        copy.RotateAround(b.center, Vector3.up, Vector3.SignedAngle(from, to, Vector3.up));
        b = RendererBounds(copy, new Bounds(copy.position, Vector3.zero));
        copy.position += new Vector3(target.x - b.center.x, floorY - b.min.y, target.z - b.center.z);
        if (copy.GetComponent<Collider>() == null)
        {
            var mf = copy.GetComponent<MeshFilter>();
            if (mf != null && mf.sharedMesh != null) copy.gameObject.AddComponent<MeshCollider>().sharedMesh = mf.sharedMesh;
        }
        GameObjectUtility.SetStaticEditorFlags(copy.gameObject, StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccluderStatic | StaticEditorFlags.OccludeeStatic);
    }

    // ---------------------------------------------------------------- Босс

    /// <summary>Сустав в долях модели: X — от середины к правому краю (1,42 м), Y и Z — в метрах при росте 2,89 м.</summary>
    struct Joint { public string name, parent; public Vector3 pos; }

    static void BuildBoss(BasementInfo info)
    {
        var asset = AssetDatabase.LoadAssetAtPath<GameObject>(BossModelPath);
        var mf = asset != null ? asset.GetComponentInChildren<MeshFilter>(true) : null;
        var mr = mf != null ? mf.GetComponent<MeshRenderer>() : null;
        if (mf == null || mf.sharedMesh == null || mr == null)
        {
            Debug.LogWarning("CanteenSceneBuilder: не удалось загрузить модель босса " + BossModelPath + ".");
            return;
        }
        Mesh src = mf.sharedMesh;

        // Вершины — в систему координат корня модели.
        Matrix4x4 toRoot = asset.transform.worldToLocalMatrix * mf.transform.localToWorldMatrix;
        var v = src.vertices;
        var n = src.normals;
        var tg = src.tangents;
        for (int i = 0; i < v.Length; i++)
        {
            v[i] = toRoot.MultiplyPoint3x4(v[i]);
            if (i < n.Length) n[i] = toRoot.MultiplyVector(n[i]).normalized;
            if (i < tg.Length) { Vector3 t3 = toRoot.MultiplyVector(tg[i]).normalized; tg[i] = new Vector4(t3.x, t3.y, t3.z, tg[i].w); }
        }
        Bounds b = GeometryUtility.CalculateBounds(v, Matrix4x4.identity);
        // Лицом к +Z: носки ступней впереди пяток.
        float footZ = 0f; int footN = 0;
        for (int i = 0; i < v.Length; i++) if (v[i].y < b.min.y + b.size.y * 0.04f) { footZ += v[i].z; footN++; }
        bool flip = footN > 0 && footZ / footN < b.center.z;
        var shift = new Vector3(-b.center.x, -b.min.y, 0f);
        for (int i = 0; i < v.Length; i++)
        {
            v[i] += shift;
            if (flip)
            {
                v[i] = new Vector3(-v[i].x, v[i].y, -v[i].z);
                if (i < n.Length) n[i] = new Vector3(-n[i].x, n[i].y, -n[i].z);
                if (i < tg.Length) tg[i] = new Vector4(-tg[i].x, tg[i].y, -tg[i].z, tg[i].w);
            }
        }
        b = GeometryUtility.CalculateBounds(v, Matrix4x4.identity);
        float ky = b.size.y / 2.89f, kx = b.extents.x / 1.42f;
        Vector3 J(float x, float y, float z) => new Vector3(x * kx, y * ky, z * ky);

        // Скелет.
        var joints = new List<Joint>
        {
            new Joint { name = "Hips", parent = "Rig", pos = J(0f, 1.10f, 0f) },
            new Joint { name = "Spine", parent = "Hips", pos = J(0f, 1.50f, -0.02f) },
            new Joint { name = "Chest", parent = "Spine", pos = J(0f, 1.95f, -0.12f) },
            new Joint { name = "Head", parent = "Chest", pos = J(0f, 2.30f, 0f) },
            new Joint { name = "Head_End", parent = "Head", pos = J(0f, 2.70f, 0.32f) },
        };
        foreach (var (side, sx) in new[] { ("L", -1f), ("R", 1f) })
        {
            joints.Add(new Joint { name = "UpperArm." + side, parent = "Chest", pos = J(0.50f * sx, 2.30f, -0.12f) });
            joints.Add(new Joint { name = "Forearm." + side, parent = "UpperArm." + side, pos = J(0.90f * sx, 1.80f, 0.02f) });
            joints.Add(new Joint { name = "Hand." + side, parent = "Forearm." + side, pos = J(1.15f * sx, 1.45f, 0.17f) });
            joints.Add(new Joint { name = "Hand." + side + "_End", parent = "Hand." + side, pos = J(1.40f * sx, 1.12f, 0.22f) });
            joints.Add(new Joint { name = "Thigh." + side, parent = "Hips", pos = J(0.22f * sx, 1.05f, 0f) });
            joints.Add(new Joint { name = "Shin." + side, parent = "Thigh." + side, pos = J(0.26f * sx, 0.55f, 0f) });
            joints.Add(new Joint { name = "Foot." + side, parent = "Shin." + side, pos = J(0.29f * sx, 0.12f, -0.02f) });
            joints.Add(new Joint { name = "Foot." + side + "_End", parent = "Foot." + side, pos = J(0.32f * sx, 0.02f, 0.32f) });
        }
        var pos = joints.ToDictionary(j => j.name, j => j.pos);

        // Кости, к которым привязываются вершины (концевые — нет), и отрезки «кость → следующий сустав».
        var segments = new List<(string bone, Vector3 a, Vector3 b, float side)>
        {
            ("Hips", pos["Hips"], pos["Spine"], 0f),
            ("Hips", pos["Thigh.L"], pos["Thigh.R"], 0f),
            ("Spine", pos["Spine"], pos["Chest"], 0f),
            ("Chest", pos["Chest"], pos["Head"], 0f),
            ("Chest", pos["Chest"], J(0f, 2.55f, -0.45f), 0f),             // горб
            ("Chest", pos["UpperArm.L"], pos["UpperArm.R"], 0f),            // плечи
            ("Head", pos["Head"], pos["Head_End"], 0f),
        };
        foreach (var (side, sx) in new[] { ("L", -1f), ("R", 1f) })
        {
            segments.Add(("UpperArm." + side, pos["UpperArm." + side], pos["Forearm." + side], sx));
            segments.Add(("Forearm." + side, pos["Forearm." + side], pos["Hand." + side], sx));
            segments.Add(("Hand." + side, pos["Hand." + side], pos["Hand." + side + "_End"], sx));
            segments.Add(("Thigh." + side, pos["Thigh." + side], pos["Shin." + side], sx));
            segments.Add(("Shin." + side, pos["Shin." + side], pos["Foot." + side], sx));
            segments.Add(("Foot." + side, pos["Foot." + side], pos["Foot." + side + "_End"], sx));
        }

        // Иерархия: Boss → Pivot (рост) → Model (скин) + Rig (кости). Строится в начале координат, потом переносится.
        var root = new GameObject("Boss_BlindHunter");
        var pivot = new GameObject("Pivot").transform;
        pivot.SetParent(root.transform, false);
        var modelGo = new GameObject("Model");
        modelGo.transform.SetParent(pivot, false);
        var rig = new GameObject("Rig").transform;
        rig.SetParent(pivot, false);
        var bones = new Dictionary<string, Transform> { ["Rig"] = rig };
        foreach (var j in joints)
        {
            var t = new GameObject(j.name).transform;
            t.SetParent(bones[j.parent], false);
            t.position = j.pos;
            bones[j.name] = t;
        }
        var skinBones = joints.Select(j => bones[j.name]).ToArray();
        var boneIndex = new Dictionary<string, int>();
        for (int i = 0; i < joints.Count; i++) boneIndex[joints[i].name] = i;

        // Веса: до трёх ближайших костей, вес ~ 1 / расстояние⁴. Кости рук и ног одной стороны не берут вершины другой.
        var weights = new BoneWeight[v.Length];
        float limbSlack = 0.05f * kx;
        for (int i = 0; i < v.Length; i++)
        {
            var best = new List<(int bone, float w)>();
            var perBone = new Dictionary<int, float>();
            foreach (var s in segments)
            {
                if (s.side != 0f && v[i].x * s.side < -limbSlack) continue;
                float d = DistToSegment(v[i], s.a, s.b) + 0.02f;
                float w = 1f / (d * d * d * d);
                int bi = boneIndex[s.bone];
                perBone[bi] = Mathf.Max(perBone.TryGetValue(bi, out var o) ? o : 0f, w);
            }
            var top = perBone.OrderByDescending(kv => kv.Value).Take(3).ToArray();
            float sum = top.Sum(kv => kv.Value);
            var bw = new BoneWeight();
            if (top.Length > 0) { bw.boneIndex0 = top[0].Key; bw.weight0 = top[0].Value / sum; }
            if (top.Length > 1) { bw.boneIndex1 = top[1].Key; bw.weight1 = top[1].Value / sum; }
            if (top.Length > 2) { bw.boneIndex2 = top[2].Key; bw.weight2 = top[2].Value / sum; }
            weights[i] = bw;
        }

        var mesh = new Mesh { name = "BlindHunter_Rigged", indexFormat = src.indexFormat };
        mesh.vertices = v;
        if (n.Length == v.Length) mesh.normals = n;
        if (tg.Length == v.Length) mesh.tangents = tg;
        var uv = new List<Vector2>();
        for (int ch = 0; ch < 4; ch++)
        {
            src.GetUVs(ch, uv);
            if (uv.Count == v.Length) mesh.SetUVs(ch, uv);
        }
        if (src.colors.Length == v.Length) mesh.colors = src.colors;
        mesh.subMeshCount = src.subMeshCount;
        for (int sm = 0; sm < src.subMeshCount; sm++)
        {
            mesh.SetTriangles(src.GetTriangles(sm), sm); // поворот на 180° — не зеркало: порядок вершин в треугольниках прежний
        }
        if (tg.Length != v.Length && uv.Count == v.Length) mesh.RecalculateTangents();
        mesh.boneWeights = weights;
        mesh.bindposes = skinBones.Select(t => t.worldToLocalMatrix * modelGo.transform.localToWorldMatrix).ToArray();
        mesh.RecalculateBounds();

        Directory.CreateDirectory(Path.GetDirectoryName(BossMeshPath));
        if (AssetDatabase.LoadAssetAtPath<Mesh>(BossMeshPath) != null) AssetDatabase.DeleteAsset(BossMeshPath);
        AssetDatabase.CreateAsset(mesh, BossMeshPath);

        var smr = modelGo.AddComponent<SkinnedMeshRenderer>();
        smr.sharedMesh = mesh;
        smr.sharedMaterials = mr.sharedMaterials;
        smr.bones = skinBones;
        smr.rootBone = bones["Hips"];
        smr.updateWhenOffscreen = true;

        pivot.localScale = Vector3.one * BossScale;
        root.transform.SetPositionAndRotation(info.bossPos, Quaternion.LookRotation(info.bossForward, Vector3.up));
        var boss = root.AddComponent<ZombieBoss>();
        boss.arena = info.arena;
    }

    static float DistToSegment(Vector3 p, Vector3 a, Vector3 b)
    {
        Vector3 ab = b - a;
        float t = ab.sqrMagnitude > 1e-8f ? Mathf.Clamp01(Vector3.Dot(p - a, ab) / ab.sqrMagnitude) : 0f;
        return Vector3.Distance(p, a + ab * t);
    }

    // ---------------------------------------------------------------- Магазин

    struct ShopSpots
    {
        public bool ok;
        public Vector3 sellerPos, sellerForward, along;
        /// <summary>Места для оружия на прилавке со стороны зала: Uzi, MP5, дробовик, автомат.</summary>
        public Vector3[] gunPos;
    }

    /// <summary>Прилавок с горячим (Line_Right): продавщица — за ним (между 3-й и 4-й секцией), оружие — на полке для подносов со стороны зала.</summary>
    static ShopSpots FindShopSpots(Transform canteen, Bounds floor)
    {
        var spots = new ShopSpots();
        string[] sections = { "Line_Right_02_S", "Line_Right_03_H", "Line_Right_04_H", "Line_Right_05_D" };
        var counters = new Bounds[sections.Length];
        var roots = new Transform[sections.Length];
        for (int i = 0; i < sections.Length; i++)
        {
            roots[i] = FindChild(canteen, sections[i]);
            var counter = roots[i] != null ? roots[i].GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name.StartsWith("Counter_")) : null;
            if (counter == null) return spots;
            counters[i] = RendererBounds(counter, new Bounds());
        }
        Physics.SyncTransforms();
        float customer = Mathf.Sign(floor.center.z - counters[1].center.z); // зал — с этой стороны (по Z)
        Vector3 along = counters[3].center - counters[0].center; along.y = 0f;
        spots.along = along.sqrMagnitude > 1e-4f ? along.normalized : Vector3.right;

        spots.gunPos = new Vector3[sections.Length];
        for (int i = 0; i < sections.Length; i++)
        {
            Bounds c = counters[i];
            float z = customer > 0f ? c.max.z - 0.15f : c.min.z + 0.15f;
            float y = c.min.y + 0.9f;
            // Верх полки для подносов: первое попадание сверху на высоте 0,7–1,15 м над полом.
            var hits = Physics.RaycastAll(new Vector3(c.center.x, c.min.y + 1.2f, z), Vector3.down, 1.2f, ~0, QueryTriggerInteraction.Ignore);
            foreach (var h in hits.OrderBy(h => h.distance))
                if (h.point.y > c.min.y + 0.7f && h.point.y < c.min.y + 1.15f && h.rigidbody == null) { y = h.point.y; break; }
            spots.gunPos[i] = new Vector3(c.center.x, y, z);
        }
        Vector3 mid = (counters[1].center + counters[2].center) / 2f;
        float sz = customer > 0f ? counters[1].min.z - 0.45f : counters[1].max.z + 0.45f;
        spots.sellerPos = new Vector3(mid.x, floor.max.y, sz);
        spots.sellerForward = new Vector3(0f, 0f, customer);
        spots.ok = true;
        return spots;
    }

    /// <summary>Оружие становится товаром: не берётся, пока не куплено; модель по центру своего места на прилавке.</summary>
    static void PutOnSale(WeaponPickup weapon, ShopSpots shop, int slot, int price)
    {
        if (weapon == null || !shop.ok) return;
        weapon.forSale = true;
        weapon.price = price;
        // Скрытые детали (патрон в руке у дробовика) не в счёт — у выключенного рендерера габариты не настоящие.
        Bounds b = new Bounds(weapon.transform.position, Vector3.zero);
        bool first = true;
        foreach (var r in weapon.GetComponentsInChildren<Renderer>())
        {
            if (!r.enabled) continue;
            if (first) { b = r.bounds; first = false; } else b.Encapsulate(r.bounds);
        }
        Vector3 d = shop.gunPos[slot] - b.center;
        weapon.transform.position += new Vector3(d.x, 0f, d.z);
    }

    static void BuildShopkeeper(ShopSpots shop)
    {
        var go = new GameObject("Shopkeeper");
        go.transform.SetPositionAndRotation(shop.sellerPos, Quaternion.LookRotation(shop.sellerForward, Vector3.up));
        var custom = SellerModelPaths.Select(AssetDatabase.LoadAssetAtPath<GameObject>).FirstOrDefault(a => a != null);
        if (custom != null)
        {
            // Своя модель: ставим на пол и подгоняем рост (~1,65 м).
            var model = Spawn(custom, "Model");
            model.transform.SetParent(go.transform, false);
            foreach (var a in model.GetComponentsInChildren<Animator>(true)) a.enabled = false;
            Bounds mb = RendererBounds(model.transform, new Bounds(go.transform.position, Vector3.one));
            if (mb.size.y > 0.1f) model.transform.localScale *= 1.65f / mb.size.y;
            mb = RendererBounds(model.transform, mb);
            model.transform.position += new Vector3(go.transform.position.x - mb.center.x, go.transform.position.y - mb.min.y, go.transform.position.z - mb.center.z);
        }
        go.AddComponent<Shopkeeper>();

        // Тёплая лампа над прилавком — продавщицу и товар видно в полутёмном зале.
        var lamp = new GameObject("ShopLight");
        lamp.transform.position = shop.sellerPos + Vector3.up * 2.5f + shop.sellerForward * 0.9f;
        var l = lamp.AddComponent<Light>();
        l.type = LightType.Point;
        l.range = 5f;
        l.intensity = 1.1f;
        l.color = new Color(1f, 0.85f, 0.65f);
        l.shadows = LightShadows.None;
    }
}
