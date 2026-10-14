using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Собирает игровую сцену Assets/Scenes/Canteen.unity: столовая, игрок от первого лица (MainCharacter),
/// пистолет на полу, свет. Запускается автоматически один раз, если сцены ещё нет,
/// и вручную через меню Tools → Canteen → Rebuild Scene.
/// </summary>
[InitializeOnLoad]
public static class CanteenSceneBuilder
{
    const string ScenePath = "Assets/Scenes/Canteen.unity";
    const string CanteenPath = "Assets/Models location/Canteen_LowPoly.glb";
    // Новая модель персонажа; если её нет — старая.
    static readonly string[] CharacterPaths = { "Assets/Characters/MainCharacter 1.glb", "Assets/Characters/MainCharacter.glb" };
    // Новая модель пистолета; если её нет — старая.
    static readonly string[] PistolPaths = { "Assets/Guns/Pistol_LowPoly 1.glb", "Assets/Guns/Pistol_LowPoly.glb" };
    const string RiflePath = "Assets/Guns/AssaultRifle_LowPoly.obj";
    const string ShotgunPath = "Assets/Guns/PumpShotgun_LowPoly.glb";
    const string UziPath = "Assets/Guns/Uzi.glb";
    const string Mp5Path = "Assets/Guns/MP5.glb";
    const string EffectsFolder = "Assets/Effects";
    static string AutoBuildKey => "CanteenSceneBuilder.AutoBuilt." + BuildVersion;
    // Увеличивается, когда сборщик меняется — тогда редактор предложит пересобрать сцену.
    const int BuildVersion = 18;
    static string VersionKey => "CanteenSceneBuilder.Version." + Application.dataPath;

    static CanteenSceneBuilder()
    {
        if (Application.isBatchMode || SessionState.GetBool(AutoBuildKey, false)) return;
        bool exists = File.Exists(ScenePath);
        if (exists && EditorPrefs.GetInt(VersionKey, 1) >= BuildVersion) return;
        SessionState.SetBool(AutoBuildKey, true);
        EditorApplication.delayCall += () =>
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            if (!File.Exists(ScenePath) || EditorUtility.DisplayDialog("Обновить сцену Canteen?",
                    "Сборщик сцены обновился (MP5: рука взводит затвор сверху, а не сквозь оружие). Пересобрать Assets/Scenes/Canteen.unity?\n\n" +
                    "Ручные изменения в этой сцене будут потеряны. Пересобрать можно и позже: Tools → Canteen → Rebuild Scene.",
                    "Пересобрать", "Позже"))
                Build();
        };
    }

    [MenuItem("Tools/Canteen/Rebuild Scene")]
    public static void Build()
    {
        var canteenAsset = AssetDatabase.LoadAssetAtPath<GameObject>(CanteenPath);
        var characterAsset = CharacterPaths.Select(AssetDatabase.LoadAssetAtPath<GameObject>).FirstOrDefault(a => a != null);
        var rifleAsset = AssetDatabase.LoadAssetAtPath<GameObject>(RiflePath);
        var shotgunAsset = AssetDatabase.LoadAssetAtPath<GameObject>(ShotgunPath);
        var uziAsset = AssetDatabase.LoadAssetAtPath<GameObject>(UziPath);
        var mp5Asset = AssetDatabase.LoadAssetAtPath<GameObject>(Mp5Path);
        var pistolAsset = PistolPaths.Select(AssetDatabase.LoadAssetAtPath<GameObject>).FirstOrDefault(a => a != null);
        if (canteenAsset == null || characterAsset == null || pistolAsset == null)
        {
            Debug.LogError("CanteenSceneBuilder: не удалось загрузить модели .glb (проверьте, что пакет glTFast импортировал их).");
            return;
        }
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        // ---------- Столовая ----------
        var canteen = Spawn(canteenAsset, "Canteen");
        string[] noCollider = { "Outside_View", "LED_", "Bunting", "HangingSign", "Floor_Footprints", "AC_" };
        foreach (var mf in canteen.GetComponentsInChildren<MeshFilter>(true))
        {
            if (mf.sharedMesh == null || noCollider.Any(n => mf.name.StartsWith(n))) continue;
            if (mf.GetComponent<Collider>() == null) mf.gameObject.AddComponent<MeshCollider>().sharedMesh = mf.sharedMesh;
        }
        foreach (var t in canteen.GetComponentsInChildren<Transform>(true))
            GameObjectUtility.SetStaticEditorFlags(t.gameObject, StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccluderStatic | StaticEditorFlags.OccludeeStatic);

        Bounds floor = RendererBounds(FindChild(canteen.transform, "Floor"), new Bounds(Vector3.zero, new Vector3(30, 0, 20)));
        float floorY = floor.max.y;

        // Точка появления: у главного входа (Door_Near), лицом в зал.
        Vector3 spawn, forward;
        var door = FindChild(canteen.transform, "Door_Near");
        if (door != null)
        {
            Vector3 doorPos = RendererBounds(door, new Bounds(door.position, Vector3.zero)).center;
            Vector3 toRoom = floor.center - doorPos; toRoom.y = 0f;
            forward = Mathf.Abs(toRoom.x) > Mathf.Abs(toRoom.z)
                ? new Vector3(Mathf.Sign(toRoom.x), 0, 0) : new Vector3(0, 0, Mathf.Sign(toRoom.z));
            spawn = new Vector3(doorPos.x, floorY, doorPos.z) + forward * 1.4f;
        }
        else
        {
            spawn = new Vector3(0f, floorY, 19f);
            forward = Vector3.back;
        }

        // ---------- Свет ----------
        SetupLighting(floor);

        // ---------- Игрок ----------
        var player = BuildPlayer(characterAsset, spawn, forward);

        // ---------- Пистолет на полу, в нескольких шагах впереди ----------
        Vector3 right = Vector3.Cross(Vector3.up, forward);
        BuildPistol(pistolAsset, spawn + forward * 4.5f + right * 0.35f, forward, floorY);

        // ---------- Автомат и дробовик — в других частях зала ----------
        Physics.SyncTransforms();
        if (rifleAsset != null)
        {
            var p = GroundPoint(spawn + forward * 10f - right * 5f, floor, floorY);
            // Правая рука — на пистолетной рукояти (z −0,09…−0,03), левая — снизу на заднем краю цевья (z 0,18…0,35).
            // Автомат держится близко к лицу: плечо модели на 0,22 м позади камеры, рука ~0,65 м —
            // при большем выносе левая рука не дотягивается до цевья.
            var rifle = BuildLongGun(rifleAsset, "AssaultRifle", "Автомат", 30, "BoltCarrier", "Body",
                new Vector3(0.03f, -0.08f, -0.062f), new Vector3(-0.012f, -0.03f, 0.20f), new Vector3(0.07f, -0.13f, 0.1f),
                0.04f, 0f, p, Quaternion.Euler(0f, -60f, 0f) * forward, p.y,
                // Линия прицеливания — по верху целика (z 0,145, y 0,071) над мушкой (z 0,52, y 0,076).
                aimPointPos: new Vector3(0f, 0.073f, 0.145f),
                // Рукоять затвора сбоку на BoltCarrier.
                rackGrabPos: new Vector3(-0.056f, 0.036f, 0.135f)).GetComponent<WeaponPickup>();
            rifle.automatic = true;
            rifle.fireInterval = 0.1f; // 600 выстр./мин
            rifle.recoilScale = 0.45f;
            rifle.reloadDuration = 2.4f;
            rifle.stowMagazine = true;
        }
        if (shotgunAsset != null)
        {
            var p = GroundPoint(spawn + forward * 7f + right * 6f, floor, floorY);
            // Правая рука — на шейке приклада (z ≈ −0,085, у спуска), левая — снизу на цевье (цевьё z 0,235…0,46).
            // Держим так же близко, как автомат, иначе левая рука не дотянется до цевья.
            // Дуло: центр Body по высоте занижен прикладом (−0,04), ствол на y ≈ 0,044 → смещение +0,084.
            var shotgun = BuildLongGun(shotgunAsset, "PumpShotgun", "Дробовик", 5, "Bolt", "Body",
                new Vector3(0.027f, -0.018f, -0.085f), new Vector3(-0.012f, -0.032f, 0.27f), new Vector3(0.06f, -0.135f, 0.1f),
                0.084f, 1f, p, Quaternion.Euler(0f, 100f, 0f) * forward, p.y,
                // Целика нет — глаз лежит на планке: линия прицеливания по верху ствольной коробки (0,057) на мушку (0,056 у дула).
                // Точка в 0,28 м перед глазом (aimDistance) → глаз над шейкой приклада (z ≈ −0,10), а не над коробкой.
                aimPointPos: new Vector3(0f, 0.06f, 0.18f));
            SetupPumpAction(shotgun);
        }
        if (uziAsset != null)
        {
            var p = GroundPoint(spawn + forward * 3f - right * 3.5f, floor, floorY);
            // Держится одной правой рукой: ладонь справа на рукояти (в ней магазин, z −0,053…−0,018), спуск — z 0…0,012.
            // Дуло: центр Body по высоте −0,007, ствол на y ≈ 0,028 → смещение +0,034.
            var uzi = BuildLongGun(uziAsset, "Uzi", "Uzi", 25, "Bolt", "Body",
                new Vector3(0.022f, -0.045f, -0.05f), new Vector3(-0.021f, -0.125f, -0.036f), new Vector3(0.11f, -0.16f, 0.3f),
                0.034f, 1f, p, Quaternion.Euler(0f, 150f, 0f) * forward, p.y,
                // Центр диоптра целика (z −0,16, y 0,0775) и верх мушки (z 0,143, y 0,078) — линия почти горизонтальна.
                aimPointPos: new Vector3(0f, 0.078f, -0.02f),
                rackHandleName: "ChargingKnob").GetComponent<WeaponPickup>();
            uzi.oneHanded = true;
            // Левая рука при перезарядке берёт магазин снизу, где он выступает из рукояти, ладонью к нему.
            uzi.gripLeft.localRotation = Quaternion.LookRotation(new Vector3(0f, -0.5f, 1f), Vector3.right);
            uzi.magGrabHeight = 0.15f;
            uzi.automatic = true;
            uzi.fireInterval = 0.05f; // 1200 выстр./мин
            uzi.recoilScale = 0.35f;
            uzi.reloadDuration = 2.1f;
        }
        if (mp5Asset != null)
        {
            // Прямо по ходу от входа, чуть правее — видно сразу после появления.
            var p = GroundPoint(spawn + forward * 6f + right * 1.8f, floor, floorY);
            // Правая рука — на пистолетной рукояти (позади спуска, z ≈ −0,06), левая — снизу на заднем краю цевья (z 0,145…0,27, низ y 0,044):
            // при прицеливании оружие вынесено вперёд, и до середины цевья рука бы не дотянулась.
            // Дуло: центр Body по высоте 0,016, ствол на y ≈ 0,064 → смещение +0,048.
            var mp5 = BuildLongGun(mp5Asset, "MP5", "MP5", 30, "Bolt", "Body",
                new Vector3(0.023f, -0.05f, -0.062f), new Vector3(-0.012f, 0.025f, 0.17f), new Vector3(0.07f, -0.15f, 0.1f),
                0.048f, 1f, p, Quaternion.Euler(0f, 75f, 0f) * forward, p.y, // боком к игроку — виден весь силуэт
                // Линия прицеливания — по верху мушки в кольцевом намушнике (z 0,299, y 0,114); целик (z −0,054, y 0,109) чуть ниже неё.
                // Точка в 0,28 м перед глазом (aimDistance) → глаз в 0,21 м за ней, задний торец коробки (z −0,16) — в 5 см перед камерой,
                // с запасом на отдачу (до 3 см назад); иначе он попадает за ближнюю плоскость отсечения и обрезается.
                aimPointPos: new Vector3(0f, 0.114f, 0.07f),
                rackHandleName: "ChargingHandle").GetComponent<WeaponPickup>();
            mp5.automatic = true;
            mp5.fireInterval = 0.075f; // 800 выстр./мин
            mp5.recoilScale = 0.3f;
            mp5.reloadDuration = 2.3f;
            mp5.rackRoll = 35f; // крен влево поднимает рукоять (она справа над цевьём) навстречу руке, идущей сверху
        }

        Selection.activeGameObject = player;
        Directory.CreateDirectory(Path.GetDirectoryName(ScenePath));
        EditorSceneManager.SaveScene(scene, ScenePath);

        // Сцена — первая в Build Settings.
        var scenes = EditorBuildSettings.scenes.Where(s => s.path != ScenePath).ToList();
        scenes.Insert(0, new EditorBuildSettingsScene(ScenePath, true));
        EditorBuildSettings.scenes = scenes.ToArray();
        EditorPrefs.SetInt(VersionKey, BuildVersion);
        AssetDatabase.SaveAssets();

        Debug.Log("CanteenSceneBuilder: сцена собрана и сохранена в " + ScenePath + ". Нажмите Play.");
    }

    // ---------------------------------------------------------------------------------------------

    static GameObject BuildPlayer(GameObject characterAsset, Vector3 spawn, Vector3 forward)
    {
        var player = new GameObject("Player");
        player.transform.SetPositionAndRotation(spawn, Quaternion.LookRotation(forward, Vector3.up));

        var cc = player.AddComponent<CharacterController>();
        cc.height = 1.8f;
        cc.radius = 0.3f;
        cc.center = new Vector3(0f, 0.9f, 0f);
        cc.stepOffset = 0.35f;
        cc.slopeLimit = 50f;
        cc.skinWidth = 0.03f;

        const float eyeHeight = 1.65f;
        var pivot = new GameObject("CameraPivot").transform;
        pivot.SetParent(player.transform, false);
        pivot.localPosition = new Vector3(0f, eyeHeight, 0f);

        var camGo = new GameObject("PlayerCamera");
        camGo.tag = "MainCamera";
        camGo.transform.SetParent(pivot, false);
        var cam = camGo.AddComponent<Camera>();
        cam.nearClipPlane = 0.01f; // оружие у самого лица (при прицеливании) не должно обрезаться
        cam.farClipPlane = 300f;
        cam.fieldOfView = 70f;
        camGo.AddComponent<AudioListener>();

        var holder = new GameObject("WeaponHolder").transform;
        holder.SetParent(pivot, false); // не камера: в виде от 3-го лица оружие остаётся в руках
        holder.localPosition = new Vector3(0.08f, -0.15f, 0.36f);

        // Модель персонажа. Начало координат скелета (Arms_Root) — уровень глаз.
        var body = Spawn(characterAsset, "MainCharacter");
        body.transform.SetParent(player.transform, false);
        body.transform.localPosition = Vector3.zero;
        body.transform.localRotation = Quaternion.identity;

        // Разворачиваем модель лицом вперёд (среднее направление обеих стоп: пятка → носок).
        var footR = FindChild(body.transform, "Foot.R"); var toeR = FindChild(body.transform, "Toe.R");
        var footL = FindChild(body.transform, "Foot.L"); var toeL = FindChild(body.transform, "Toe.L");
        if (footR != null && toeR != null && footL != null && toeL != null)
        {
            Vector3 toeDir = Vector3.ProjectOnPlane((toeR.position + toeL.position) - (footR.position + footL.position), Vector3.up);
            float yaw = Mathf.Round(Vector3.SignedAngle(toeDir, player.transform.forward, Vector3.up));
            body.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
        }
        // «Глаза» модели (Arms_Root) совмещаем с камерой — так модель и задумана: камера над грудью.
        var armsRoot = FindChild(body.transform, "Arms_Root");
        Vector3 eye = armsRoot != null ? armsRoot.position : body.transform.position;
        Vector3 desiredEye = player.transform.TransformPoint(new Vector3(0f, eyeHeight, 0f));
        body.transform.position += desiredEye - eye;
        foreach (var smr in body.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            smr.updateWhenOffscreen = true;
        foreach (var anim in body.GetComponentsInChildren<Animator>(true)) anim.enabled = false;
        foreach (var anim in body.GetComponentsInChildren<Animation>(true)) anim.enabled = false;

        var fpc = player.AddComponent<FirstPersonController>();
        fpc.cameraPivot = pivot;
        fpc.eyeHeight = eyeHeight;
        fpc.playerCamera = camGo.transform;

        var handler = player.AddComponent<PlayerWeaponHandler>();
        handler.playerCamera = cam;
        handler.controller = fpc;
        handler.weaponHolder = holder;

        var bodyAnim = player.AddComponent<PlayerBodyAnimator>();
        bodyAnim.characterRoot = body.transform;
        bodyAnim.controller = fpc;
        bodyAnim.weapons = handler;
        bodyAnim.cameraTransform = camGo.transform;

        return player;
    }

    static void BuildPistol(GameObject pistolAsset, Vector3 position, Vector3 forward, float floorY)
    {
        // Корень «Pistol»: ось +Z — ствол, +Y — верх. Внутри — сама модель.
        var root = new GameObject("Pistol");
        var model = Spawn(pistolAsset, "Model");
        model.transform.SetParent(root.transform, false);
        foreach (var anim in model.GetComponentsInChildren<Animator>(true)) anim.enabled = false;
        foreach (var anim in model.GetComponentsInChildren<Animation>(true)) anim.enabled = false;

        var slide = FindChild(model.transform, "Slide");
        var mag = FindChild(model.transform, "Magazine");
        if (slide != null && mag != null)
        {
            Vector3 s = model.transform.InverseTransformPoint(RendererBounds(slide, new Bounds(slide.position, Vector3.zero)).center);
            Vector3 m = model.transform.InverseTransformPoint(RendererBounds(mag, new Bounds(mag.position, Vector3.zero)).center);
            if (s.z < m.z) model.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
        }

        // Точки хвата (центр ладони; forward — пальцы, up — куда смотрит ладонь).
        // Правая рука: ладонь на правой стороне рукояти, пальцы обхватывают её спереди, указательный — у спуска.
        var gripR = new GameObject("Grip_R").transform;
        gripR.SetParent(root.transform, false);
        gripR.localPosition = new Vector3(0.018f, -0.035f, -0.056f);
        gripR.localRotation = Quaternion.LookRotation(new Vector3(0f, -0.2f, 1f), Vector3.left);
        // Левая рука: поддерживает снизу-слева, обхватывая пальцы правой.
        var gripL = new GameObject("Grip_L").transform;
        gripL.SetParent(root.transform, false);
        gripL.localPosition = new Vector3(-0.024f, -0.05f, -0.036f);
        gripL.localRotation = Quaternion.LookRotation(new Vector3(0.1f, -0.6f, 1f), new Vector3(1f, 0.3f, 0f));

        var pickup = root.AddComponent<WeaponPickup>();
        pickup.displayName = "Пистолет";
        pickup.gripRight = gripR;
        pickup.gripLeft = gripL;
        pickup.slide = slide;

        FinishWeapon(root, model, pickup, slide, slide, -0.003f, 0.006f, 0f);

        // Лежит на полу на боку, слегка повёрнут.
        PlaceLying(root, model, position, Quaternion.Euler(0f, 35f, 0f) * forward, floorY);
    }

    /// <summary>
    /// Длинное оружие (автомат, дробовик): корень с осью +Z вдоль ствола, точки хвата, коллайдер, эффекты выстрела.
    /// У моделей ствол смотрит в +Z, поэтому разворачивать модель не нужно.
    /// </summary>
    static GameObject BuildLongGun(GameObject asset, string rootName, string displayName, int magazineSize,
        string slideName, string bodyName, Vector3 gripRPos, Vector3 gripLPos, Vector3 holdOffset,
        float muzzleDy, float ejectSide, Vector3 position, Vector3 forward, float surfaceY,
        Vector3? aimPointPos = null, Vector3? rackGrabPos = null, string rackHandleName = null)
    {
        var root = new GameObject(rootName);
        var model = Spawn(asset, "Model");
        model.transform.SetParent(root.transform, false);
        foreach (var anim in model.GetComponentsInChildren<Animator>(true)) anim.enabled = false;
        foreach (var anim in model.GetComponentsInChildren<Animation>(true)) anim.enabled = false;

        var slide = FindChild(model.transform, slideName);
        var body = FindChild(model.transform, bodyName);
        foreach (var part in new[] { slideName, bodyName })
            if (FindChild(model.transform, part) == null)
                Debug.LogWarning($"CanteenSceneBuilder: у модели {asset.name} нет части «{part}» — без неё не будет анимации затвора или точного прицела. " +
                                 "Если это .obj, проверьте, что у частей есть группы «g <имя>»: импорт Unity делит OBJ только по ним.");

        var gripR = new GameObject("Grip_R").transform;
        gripR.SetParent(root.transform, false);
        gripR.localPosition = gripRPos;
        gripR.localRotation = Quaternion.LookRotation(new Vector3(0f, -0.2f, 1f), Vector3.left);
        // Левая рука поддерживает цевьё снизу: ладонь смотрит вверх и чуть вправо.
        var gripL = new GameObject("Grip_L").transform;
        gripL.SetParent(root.transform, false);
        gripL.localPosition = gripLPos;
        gripL.localRotation = Quaternion.LookRotation(new Vector3(0f, 0f, 1f), new Vector3(0.3f, 1f, 0f));

        var pickup = root.AddComponent<WeaponPickup>();
        pickup.displayName = displayName;
        pickup.gripRight = gripR;
        pickup.gripLeft = gripL;
        pickup.slide = slide;
        pickup.aimSource = body;
        pickup.holdOffset = holdOffset;
        pickup.magazineSize = magazineSize;
        if (aimPointPos.HasValue)
        {
            var aim = new GameObject("AimPoint").transform;
            aim.SetParent(root.transform, false);
            aim.localPosition = aimPointPos.Value;
            pickup.aimPoint = aim;
        }
        if (rackGrabPos.HasValue)
        {
            var grab = new GameObject("RackGrab").transform;
            grab.SetParent(root.transform, false);
            // Рукоять торчит в ту сторону, куда затвор выступает от оси ствола (импорт .obj мог отзеркалить X).
            var g = rackGrabPos.Value;
            if (slide != null)
            {
                Bounds sb = LocalRendererBounds(root.transform, slide, new Bounds());
                g.x = Mathf.Abs(g.x) * (sb.center.x < 0f ? -1f : 1f);
            }
            grab.localPosition = g;
            // Кантуем на левый бок (верх уходит влево); левая рука тянется к рукояти, где бы она ни оказалась.
            pickup.rackRoll = 75f;
            pickup.rackDuration = 1.7f;
            pickup.rackGrab = grab;
        }
        var handle = rackHandleName != null ? FindChild(model.transform, rackHandleName) : null;
        if (handle != null)
        {
            // Отдельная рукоять взведения: стоит на месте при стрельбе, при передёргивании рука тянет её назад.
            // Рука всегда берёт её сверху, ладонью вниз: сбоку (особенно справа) левая рука прошла бы сквозь оружие.
            // Рукоять сбоку — ладонь ложится на верхний край со смещением в её сторону, пальцы обхватывают её.
            Bounds hb = LocalRendererBounds(root.transform, handle, new Bounds());
            bool onTop = Mathf.Abs(hb.center.x) < 0.004f;
            float side = hb.center.x < 0f ? -1f : 1f;
            var grab = new GameObject("RackGrab").transform;
            grab.SetParent(root.transform, false);
            grab.localPosition = onTop ? new Vector3(hb.center.x, hb.max.y, hb.center.z)
                                       : new Vector3(side > 0f ? hb.max.x : hb.min.x, hb.max.y + 0.006f, hb.center.z);
            pickup.rackGrab = grab;
            pickup.rackHandle = handle;
            pickup.rackGrabSide = onTop ? Vector3.up : new Vector3(side * 0.35f, 1f, 0f);
            pickup.rackRoll = onTop ? 40f : 75f;
            pickup.rackDuration = 1.6f;
        }
        else if (rackHandleName != null)
            Debug.LogWarning($"CanteenSceneBuilder: у модели {asset.name} нет части «{rackHandleName}» — затвор будет взводиться без рукояти.");

        FinishWeapon(root, model, pickup, slide, body, muzzleDy, 0.006f, ejectSide);

        // Лежит на боку, повёрнут в сторону зала.
        PlaceLying(root, model, position, forward, surfaceY);
        return root;
    }

    /// <summary>
    /// Помповое ружьё: цевьё «Forend» передёргивается после каждого выстрела (левая рука — Grip_L — едет вместе с ним),
    /// патроны вставляются по одному через окно заряжания снизу ствольной коробки, дробь летит веером.
    /// </summary>
    static void SetupPumpAction(GameObject root)
    {
        var pickup = root.GetComponent<WeaponPickup>();
        var fx = root.GetComponent<WeaponEffects>();
        var forend = FindChild(root.transform, "Forend");
        if (forend == null)
        {
            Debug.LogWarning("CanteenSceneBuilder: у дробовика нет части «Forend» — помпа не будет работать.");
            return;
        }
        pickup.pump = forend;
        pickup.pumpTravel = 0.075f;
        pickup.gripLeft.SetParent(forend, true);
        pickup.recoilScale = 1.6f;

        // Окно заряжания — низ ствольной коробки перед спусковой скобой (z 0,06…0,14).
        var port = new GameObject("LoadingPort").transform;
        port.SetParent(root.transform, false);
        port.localPosition = new Vector3(0f, -0.003f, 0.10f);
        pickup.loadingPort = port;

        // Патрон 12 калибра: красная пластиковая гильза Ø20,5 мм × 70 мм с латунным донцем.
        var hullMat = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = "FX_ShellHull" };
        hullMat.SetColor("_BaseColor", new Color(0.62f, 0.08f, 0.06f));
        hullMat.SetFloat("_Smoothness", 0.45f);
        hullMat = SaveAsset(hullMat, EffectsFolder + "/FX_ShellHull.mat");

        var shell = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        shell.name = "Shell";
        Object.DestroyImmediate(shell.GetComponent<Collider>());
        shell.transform.SetParent(root.transform, false);
        shell.transform.localScale = new Vector3(0.0205f, 0.035f, 0.0205f);
        shell.GetComponent<Renderer>().sharedMaterial = hullMat;
        var head = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        head.name = "Head";
        Object.DestroyImmediate(head.GetComponent<Collider>());
        head.transform.SetParent(shell.transform, false);
        head.transform.localPosition = new Vector3(0f, -0.78f, 0f); // донце — нижняя (−Y) часть
        head.transform.localScale = new Vector3(1.05f, 0.22f, 1.05f);
        head.GetComponent<Renderer>().sharedMaterial = fx.casingMaterial; // латунь
        foreach (var r in shell.GetComponentsInChildren<Renderer>()) r.enabled = false;
        pickup.shell = shell.transform;

        // Гильза вылетает при передёргивании, а не при выстреле; дробь — 9 картечин с разбросом.
        fx.ejectOnFire = false;
        fx.casingMaterial = hullMat;
        fx.casingScale = new Vector3(0.0205f, 0.035f, 0.0205f);
        fx.pellets = 9;
        fx.spreadAngle = 3.5f;
    }

    /// <summary>Коллайдер по габаритам модели, дуло и окно выброса гильз, эффекты выстрела.</summary>
    /// <param name="barrelRef">Часть, по переднему торцу которой ставится дуло (у пистолета — затвор).</param>
    /// <param name="ejectSide">-1 — окно слева; иначе справа (как у пистолета).</param>
    static void FinishWeapon(GameObject root, GameObject model, WeaponPickup pickup, Transform slide, Transform barrelRef,
        float muzzleDy, float portDy, float ejectSide)
    {
        Bounds local = LocalRendererBounds(root.transform, model.transform, new Bounds());
        var box = root.AddComponent<BoxCollider>();
        box.center = local.center;
        box.size = local.size;

        // Дуло — по габаритам barrelRef, окно выброса — по затвору.
        Bounds barrelLocal = barrelRef != null ? LocalRendererBounds(root.transform, barrelRef, local) : local;
        Bounds slideLocal = slide != null ? LocalRendererBounds(root.transform, slide, local) : local;
        var muzzle = new GameObject("Muzzle").transform;
        muzzle.SetParent(root.transform, false);
        muzzle.localPosition = new Vector3(barrelLocal.center.x, barrelLocal.center.y + muzzleDy, barrelLocal.max.z + 0.004f);
        var port = new GameObject("EjectionPort").transform;
        port.SetParent(root.transform, false);
        float portX = ejectSide < 0f ? slideLocal.min.x + 0.002f : slideLocal.max.x - 0.002f;
        port.localPosition = new Vector3(portX, slideLocal.center.y + portDy, Mathf.Lerp(slideLocal.min.z, slideLocal.max.z, 0.55f));

        var fx = root.AddComponent<WeaponEffects>();
        fx.muzzle = muzzle;
        fx.ejectionPort = port;
        CreateEffectMaterials(fx);
    }

    /// <summary>Габариты рендереров src в системе координат root; fallback — если рендереров нет.</summary>
    static Bounds LocalRendererBounds(Transform root, Transform src, Bounds fallback)
    {
        bool first = true;
        Bounds b = fallback;
        foreach (var r in src.GetComponentsInChildren<Renderer>())
            foreach (var corner in Corners(r.bounds))
            {
                var p = root.InverseTransformPoint(corner);
                if (first) { b = new Bounds(p, Vector3.zero); first = false; } else b.Encapsulate(p);
            }
        return b;
    }

    /// <summary>Кладёт оружие на бок (+Z вдоль forward) так, чтобы оно стояло на поверхности surfaceY.</summary>
    static void PlaceLying(GameObject root, GameObject model, Vector3 position, Vector3 forward, float surfaceY)
    {
        root.transform.SetPositionAndRotation(position,
            Quaternion.LookRotation(forward, Vector3.up) * Quaternion.Euler(0f, 0f, 90f));
        float minY = model.GetComponentsInChildren<Renderer>().Min(r => r.bounds.min.y);
        root.transform.position += Vector3.up * (surfaceY - minY + 0.002f);
    }

    /// <summary>Точка на поверхности (пол или стол) под p; p ограничивается габаритами пола.</summary>
    static Vector3 GroundPoint(Vector3 p, Bounds floor, float floorY)
    {
        p.x = Mathf.Clamp(p.x, floor.min.x + 1.5f, floor.max.x - 1.5f);
        p.z = Mathf.Clamp(p.z, floor.min.z + 1.5f, floor.max.z - 1.5f);
        p.y = floorY;
        if (Physics.Raycast(new Vector3(p.x, floorY + 1.3f, p.z), Vector3.down, out RaycastHit hit, 2f, ~0, QueryTriggerInteraction.Ignore))
            p.y = hit.point.y;
        return p;
    }

    static void SetupLighting(Bounds floor)
    {
        RenderSettings.skybox = AssetDatabase.GetBuiltinExtraResource<Material>("Default-Skybox.mat");
        RenderSettings.ambientMode = AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = new Color(0.62f, 0.64f, 0.68f);
        RenderSettings.ambientEquatorColor = new Color(0.5f, 0.49f, 0.47f);
        RenderSettings.ambientGroundColor = new Color(0.3f, 0.28f, 0.26f);
        // Без запекания света — всё освещение в реальном времени, Play можно нажимать сразу.
        Lightmapping.lightingSettings = new LightingSettings { name = "Canteen Lighting", bakedGI = false };

        var sunGo = new GameObject("Sun");
        sunGo.transform.rotation = Quaternion.Euler(40f, -35f, 0f);
        var sun = sunGo.AddComponent<Light>();
        sun.type = LightType.Directional;
        sun.intensity = 1.1f;
        sun.color = new Color(1f, 0.96f, 0.88f);
        sun.shadows = LightShadows.Soft;

        // Потолочные светильники: сетка точечных источников без теней.
        var lights = new GameObject("Ceiling Lights").transform;
        const float step = 5f;
        int nx = Mathf.Max(1, Mathf.RoundToInt(floor.size.x / step));
        int nz = Mathf.Max(1, Mathf.RoundToInt(floor.size.z / step));
        for (int ix = 0; ix < nx; ix++)
            for (int iz = 0; iz < nz; iz++)
            {
                var go = new GameObject($"Light_{ix}_{iz}");
                go.transform.SetParent(lights, false);
                go.transform.position = new Vector3(
                    floor.min.x + (ix + 0.5f) * floor.size.x / nx,
                    floor.max.y + 3.2f,
                    floor.min.z + (iz + 0.5f) * floor.size.z / nz);
                var l = go.AddComponent<Light>();
                l.type = LightType.Point;
                l.range = 8f;
                l.intensity = 2.2f;
                l.color = new Color(1f, 0.97f, 0.92f);
                l.shadows = LightShadows.None;
            }
    }

    // ---------- Материалы и текстуры эффектов выстрела (Assets/Effects) ----------

    static void CreateEffectMaterials(WeaponEffects fx)
    {
        Directory.CreateDirectory(EffectsFolder + "/Textures");

        var flashTex = SaveTexture("MuzzleFlash", 128, (x, y) =>
        {
            float r = Mathf.Sqrt(x * x + y * y), a = Mathf.Atan2(y, x);
            float core = Mathf.Exp(-r * 7f) * 1.6f;
            float spikes = Mathf.Pow(Mathf.Abs(Mathf.Cos(a * 3f)), 16f) * Mathf.Exp(-r * 3.2f) * 0.9f
                         + Mathf.Pow(Mathf.Abs(Mathf.Cos(a * 3f + 0.52f)), 30f) * Mathf.Exp(-r * 5f) * 0.5f;
            float v = Mathf.Clamp01((core + spikes) * Mathf.Clamp01((1f - r) * 3f));
            return new Color(1f, Mathf.Lerp(0.75f, 1f, core), Mathf.Lerp(0.45f, 1f, core), v);
        });
        var sparkTex = SaveTexture("Spark", 32, (x, y) =>
        {
            float r2 = x * x + y * y;
            return new Color(1f, 1f, 1f, Mathf.Clamp01(Mathf.Exp(-r2 * 9f) * 1.3f - 0.02f));
        });
        var holeTex = SaveTexture("BulletHole", 64, (x, y) =>
        {
            float r = Mathf.Sqrt(x * x + y * y), a = Mathf.Atan2(y, x);
            float jag = 0.05f * Mathf.Sin(a * 7f) + 0.03f * Mathf.Sin(a * 13f + 1f);
            float edge = 0.62f + jag;
            float alpha = 1f - Mathf.SmoothStep(edge - 0.1f, edge, r);
            float shade = Mathf.Lerp(0.02f, 0.32f, Mathf.SmoothStep(0.12f, 0.5f, r));
            return new Color(shade, shade * 0.95f, shade * 0.9f, alpha);
        });

        fx.flashMaterial = SaveParticleMaterial("FX_MuzzleFlash", flashTex, true);
        fx.sparkMaterial = SaveParticleMaterial("FX_Spark", sparkTex, true);
        fx.decalMaterial = SaveParticleMaterial("FX_BulletHole", holeTex, false);

        var brass = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = "FX_Brass" };
        brass.SetColor("_BaseColor", new Color(0.85f, 0.62f, 0.25f));
        brass.SetFloat("_Metallic", 0.9f);
        brass.SetFloat("_Smoothness", 0.65f);
        fx.casingMaterial = SaveAsset(brass, EffectsFolder + "/FX_Brass.mat");
    }

    static Texture2D SaveTexture(string name, int size, System.Func<float, float, Color> pixel)
    {
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
                tex.SetPixel(x, y, pixel((x + 0.5f) / size * 2f - 1f, (y + 0.5f) / size * 2f - 1f));
        string path = $"{EffectsFolder}/Textures/{name}.png";
        File.WriteAllBytes(path, tex.EncodeToPNG());
        Object.DestroyImmediate(tex);
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
        if (AssetImporter.GetAtPath(path) is TextureImporter ti)
        {
            ti.alphaIsTransparency = true;
            ti.wrapMode = TextureWrapMode.Clamp;
            ti.textureCompression = TextureImporterCompression.Uncompressed;
            ti.SaveAndReimport();
        }
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    /// <summary>Прозрачный материал URP Particles/Unlit: additive — для вспышек и искр, иначе обычное смешивание.</summary>
    static Material SaveParticleMaterial(string name, Texture2D tex, bool additive)
    {
        var mat = new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit")) { name = name };
        mat.SetTexture("_BaseMap", tex);
        mat.SetColor("_BaseColor", Color.white);
        mat.SetFloat("_Surface", 1f);
        mat.SetFloat("_Blend", additive ? 2f : 0f);
        mat.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
        mat.SetFloat("_DstBlend", (float)(additive ? BlendMode.One : BlendMode.OneMinusSrcAlpha));
        mat.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
        mat.SetFloat("_DstBlendAlpha", (float)(additive ? BlendMode.One : BlendMode.OneMinusSrcAlpha));
        mat.SetFloat("_ZWrite", 0f);
        mat.SetFloat("_Cull", (float)CullMode.Off);
        mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        mat.renderQueue = (int)RenderQueue.Transparent;
        return SaveAsset(mat, $"{EffectsFolder}/{name}.mat");
    }

    static Material SaveAsset(Material mat, string path)
    {
        var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (existing != null)
        {
            existing.shader = mat.shader;
            existing.CopyPropertiesFromMaterial(mat);
            existing.shaderKeywords = mat.shaderKeywords;
            existing.renderQueue = mat.renderQueue;
            EditorUtility.SetDirty(existing);
            Object.DestroyImmediate(mat);
            return existing;
        }
        AssetDatabase.CreateAsset(mat, path);
        return mat;
    }

    // ---------------------------------------------------------------------------------------------

    static GameObject Spawn(GameObject asset, string name)
    {
        var go = PrefabUtility.InstantiatePrefab(asset) as GameObject;
        if (go == null) go = Object.Instantiate(asset);
        go.name = name;
        return go;
    }

    static Transform FindChild(Transform root, string name)
    {
        return root.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == name);
    }

    static Bounds RendererBounds(Transform t, Bounds fallback)
    {
        if (t == null) return fallback;
        var rs = t.GetComponentsInChildren<Renderer>(true);
        if (rs.Length == 0) return fallback;
        var b = rs[0].bounds;
        foreach (var r in rs) b.Encapsulate(r.bounds);
        return b;
    }

    static Vector3[] Corners(Bounds b)
    {
        var c = new Vector3[8];
        for (int i = 0; i < 8; i++)
            c[i] = new Vector3((i & 1) == 0 ? b.min.x : b.max.x, (i & 2) == 0 ? b.min.y : b.max.y, (i & 4) == 0 ? b.min.z : b.max.z);
        return c;
    }
}
