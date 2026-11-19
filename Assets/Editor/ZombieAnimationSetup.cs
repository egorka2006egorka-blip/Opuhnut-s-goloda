using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Анимации зомби: модели Assets/Characters/Zombies/Models/*.fbx и клипы Mixamo из Assets/Characters/Zombies/Animations/*
/// переводятся на Humanoid-риг (клипы Mixamo ретаргетятся на скелет моделей), собирается Animator Controller
/// Assets/Characters/Zombies/Animator/Zombie.controller и назначается спавнерам зомби в открытых сценах.
/// Запускается автоматически один раз (при смене SetupVersion) и вручную через Tools → Zombies → Setup Animations.
/// </summary>
[InitializeOnLoad]
public static class ZombieAnimationSetup
{
    const string Root = "Assets/Characters/Zombies";
    const string ModelsFolder = Root + "/Models";
    const string AnimFolder = Root + "/Animations";
    public const string ControllerPath = Root + "/Animator/Zombie.controller";
    const string UpperBodyMaskPath = Root + "/Animator/ZombieUpperBody.mask";
    // Хлопья пепла — свой неосвещаемый материал (освещаемый FX_Debris бликовал голубым от неба), пыль — дым выстрелов.
    public const string AshFlakePath = Root + "/Materials/FX_Ash.mat", AshDustPath = "Assets/Effects/FX_Smoke.mat";
    const string AshTexturePath = "Assets/Effects/Textures/Smoke.png";
    // Рост зомби (м): у FBX из Blender у Armature масштаб ×500 — без поправки зомби ~9 м.
    const float TargetHeight = 1.8f;

    // Увеличивается, когда настройка меняется — тогда она выполняется заново.
    const int SetupVersion = 12; // 12 — клип танца (Animations/Dance)
    static string VersionKey => "ZombieAnimationSetup.Version." + Application.dataPath;

    // Состояния контроллера — их имена использует Zombie.cs.
    public const string Locomotion = "Locomotion", LocomotionAlt = "LocomotionAlt", Turn = "Turn", TurnMirror = "TurnMirror";
    public static readonly string[] Attacks = { "Attack1", "Attack2" };
    public static readonly string[] Hits = { "Hit1", "Hit2" };
    public static readonly string[] Deaths = { "Death1", "Death2" };
    public const string Dance = "Dance";

    static ZombieAnimationSetup()
    {
        if (Application.isBatchMode || EditorPrefs.GetInt(VersionKey, 0) >= SetupVersion) return;
        EditorApplication.delayCall += () =>
        {
            if (!EditorApplication.isPlayingOrWillChangePlaymode) { Run(); return; }
            // Запустили Play сразу после компиляции — настроим, когда вернёмся в редактор.
            void OnPlayMode(PlayModeStateChange s)
            {
                if (s != PlayModeStateChange.EnteredEditMode) return;
                EditorApplication.playModeStateChanged -= OnPlayMode;
                Run();
            }
            EditorApplication.playModeStateChanged += OnPlayMode;
        };
    }

    [MenuItem("Tools/Zombies/Setup Animations")]
    public static void Run()
    {
        foreach (var p in Fbx(ModelsFolder)) SetupModel(p);

        var clips = new Dictionary<string, AnimationClip>();
        foreach (var p in Fbx(AnimFolder))
        {
            var clip = SetupClip(p);
            if (clip != null) clips[Path.GetFileNameWithoutExtension(p)] = clip;
        }

        var controller = BuildController(clips);
        if (controller == null) return;
        AshMaterial();
        AssignToSpawners(controller);
        EditorPrefs.SetInt(VersionKey, SetupVersion);
        AssetDatabase.SaveAssets();
        Debug.Log("ZombieAnimationSetup: готово, контроллер " + ControllerPath);
    }

    static IEnumerable<string> Fbx(string folder) =>
        AssetDatabase.FindAssets("t:Model", new[] { folder }).Select(AssetDatabase.GUIDToAssetPath)
            .Where(p => p.EndsWith(".fbx", System.StringComparison.OrdinalIgnoreCase)).Distinct().OrderBy(p => p);

    // ---------- Модели ----------

    // Кость Humanoid → кость скелета модели (суффикс .L/.R добавляется ниже).
    static readonly (string human, string bone)[] Body =
    {
        ("Hips", "Hips"), ("Spine", "Spine"), ("Chest", "Chest"), ("Neck", "Neck"), ("Head", "Head"),
    };
    static readonly (string human, string bone)[] Side =
    {
        ("Shoulder", "Clavicle"), ("UpperArm", "UpperArm"), ("LowerArm", "Forearm"), ("Hand", "Hand"),
        ("UpperLeg", "Thigh"), ("LowerLeg", "Shin"), ("Foot", "Foot"), ("Toes", "Toe"),
    };
    static readonly (string human, string bone)[] Fingers =
    {
        ("Thumb", "Thumb"), ("Index", "Index"), ("Middle", "Middle"), ("Ring", "Ring"), ("Little", "Pinky"),
    };

    static void SetupModel(string path)
    {
        // Сначала — переимпорт с ZombieModelPostprocessor (меш Head → Head_Mesh), чтобы скелет ниже собирался по новым именам.
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
        var asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        var importer = AssetImporter.GetAtPath(path) as ModelImporter;
        if (asset == null || importer == null) return;

        // Масштаб импорта — под рост TargetHeight.
        float height = MeshHeight(asset);
        if (height > 0.01f && Mathf.Abs(height / TargetHeight - 1f) > 0.01f)
        {
            importer.globalScale *= TargetHeight / height;
            importer.SaveAndReimport();
            asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            Debug.Log($"ZombieAnimationSetup: {Path.GetFileName(path)} — рост был {height:0.00} м, Scale Factor {importer.globalScale:0.####}, стал {MeshHeight(asset):0.00} м");
        }
        foreach (var r in asset.GetComponentsInChildren<Renderer>(true))
            if (!(r is SkinnedMeshRenderer))
                Debug.Log($"ZombieAnimationSetup: {Path.GetFileName(path)} — не скинированный меш '{r.name}' ({r.GetType().Name}), размер {r.bounds.size}");

        var all = asset.GetComponentsInChildren<Transform>(true);
        Transform Find(string n) => all.FirstOrDefault(t => t.name == n);

        // Какая сторона скелета у персонажа слева: смотрим по T-позе (лицом — от пяток к носкам).
        Transform footL = Find("Foot.L"), footR = Find("Foot.R"), toeL = Find("Toe.L"), toeR = Find("Toe.R");
        Transform upL = Find("UpperArm.L"), upR = Find("UpperArm.R");
        bool swap = false;
        if (footL && footR && toeL && toeR && upL && upR)
        {
            Vector3 fwd = Vector3.ProjectOnPlane(toeL.position + toeR.position - footL.position - footR.position, Vector3.up);
            Vector3 right = Vector3.Cross(Vector3.up, fwd);
            swap = Vector3.Dot(upR.position - upL.position, right) < 0f;
        }
        string L = swap ? ".R" : ".L", R = swap ? ".L" : ".R";

        var human = new List<HumanBone>();
        void Map(string h, string b)
        {
            if (Find(b) == null) return;
            human.Add(new HumanBone { humanName = h, boneName = b, limit = new HumanLimit { useDefaultValues = true } });
        }
        foreach (var (h, b) in Body) Map(h, b);
        foreach (var (side, suf) in new[] { ("Left", L), ("Right", R) })
        {
            foreach (var (h, b) in Side) Map(side + " " + h, b + suf);
            string[] parts = { "Proximal", "Intermediate", "Distal" };
            foreach (var (h, b) in Fingers)
                for (int i = 0; i < 3; i++) Map(side + " " + h + " " + parts[i], b + (i + 1) + suf);
        }
        // Названия Unity: "LeftUpperArm", "Left Thumb Proximal" — у основных костей без пробела.
        for (int i = 0; i < human.Count; i++)
        {
            var hb = human[i];
            if (!hb.humanName.Contains("Proximal") && !hb.humanName.Contains("Intermediate") && !hb.humanName.Contains("Distal"))
                hb.humanName = hb.humanName.Replace(" ", "");
            human[i] = hb;
        }

        var skeleton = TPoseSkeleton(asset, L, R);

        var desc = importer.humanDescription;
        desc.human = human.ToArray();
        desc.skeleton = skeleton;
        desc.upperArmTwist = 0.5f; desc.lowerArmTwist = 0.5f;
        desc.upperLegTwist = 0.5f; desc.lowerLegTwist = 0.5f;
        desc.armStretch = 0.05f; desc.legStretch = 0.05f;
        desc.feetSpacing = 0f;
        desc.hasTranslationDoF = false;

        importer.animationType = ModelImporterAnimationType.Human;
        importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
        importer.humanDescription = desc;
        importer.importAnimation = false;
        // Камера и свет из сцены Blender не нужны: камера каждого зомби перехватывала бы картинку у камеры игрока.
        importer.importCameras = false;
        importer.importLights = false;
        importer.isReadable = true; // прах после смерти вылетает с поверхности меша (ParticleSystem → SkinnedMeshRenderer)
        importer.SaveAndReimport();

        var avatar = AssetDatabase.LoadAllAssetsAtPath(path).OfType<Avatar>().FirstOrDefault();
        Debug.Log($"ZombieAnimationSetup: модель {Path.GetFileName(path)} — костей Humanoid {human.Count}, L/R {(swap ? "переставлены" : "как в именах")}, " +
                  $"аватар {(avatar != null && avatar.isValid && avatar.isHuman ? "OK" : "ОШИБКА")}");
    }

    /// <summary>
    /// Скелет для аватара в T-позе. Модели сделаны в A-позе (руки опущены ~55°), а Humanoid считает позу скелета T-позой:
    /// без поправки все клипы опускают руки лишние 55° — руки заходят за тело и скрещиваются. Как Enforce T-Pose в Unity.
    /// </summary>
    static SkeletonBone[] TPoseSkeleton(GameObject asset, string L, string R)
    {
        var go = Object.Instantiate(asset);
        go.hideFlags = HideFlags.HideAndDontSave;
        try
        {
            var all = go.GetComponentsInChildren<Transform>(true);
            Transform Find(string n) => all.FirstOrDefault(t => t.name == n);
            void Aim(Transform bone, Transform child, Vector3 dir)
            {
                if (bone == null || child == null) return;
                Vector3 cur = child.position - bone.position;
                if (cur.sqrMagnitude > 1e-8f) bone.rotation = Quaternion.FromToRotation(cur, dir) * bone.rotation;
            }
            Vector3 right = go.transform.right;
            foreach (var (suf, side) in new[] { (L, -right), (R, right) })
            {
                Aim(Find("UpperArm" + suf), Find("Forearm" + suf), side);
                Aim(Find("Forearm" + suf), Find("Hand" + suf), side);
                Aim(Find("Hand" + suf), Find("Middle1" + suf), side);
            }
            return all.Select(t => new SkeletonBone
            {
                name = t == go.transform ? asset.name : t.name,
                position = t.localPosition, rotation = t.localRotation, scale = t.localScale,
            }).ToArray();
        }
        finally { Object.DestroyImmediate(go); }
    }

    /// <summary>Рост модели в позе из файла (по вершинам скинов).</summary>
    static float MeshHeight(GameObject asset)
    {
        var go = Object.Instantiate(asset);
        go.hideFlags = HideFlags.HideAndDontSave;
        float min = float.MaxValue, max = float.MinValue;
        try
        {
            foreach (var r in go.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                var mesh = new Mesh();
                r.BakeMesh(mesh, true);
                foreach (var v in mesh.vertices)
                {
                    float y = r.transform.TransformPoint(v).y;
                    min = Mathf.Min(min, y); max = Mathf.Max(max, y);
                }
                Object.DestroyImmediate(mesh);
            }
        }
        finally { Object.DestroyImmediate(go); }
        return max > min ? max - min : 0f;
    }

    // ---------- Клипы ----------

    static AnimationClip SetupClip(string path)
    {
        var importer = AssetImporter.GetAtPath(path) as ModelImporter;
        if (importer == null) return null;
        string folder = Path.GetFileName(Path.GetDirectoryName(path));
        string file = Path.GetFileNameWithoutExtension(path);
        bool loop = folder == "Idle" || folder == "Walk";
        bool isTurn = file.Contains("Turn");
        bool isDance = folder == "Dance";

        importer.animationType = ModelImporterAnimationType.Human;
        importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
        importer.importAnimation = true;
        importer.materialImportMode = ModelImporterMaterialImportMode.None;
        if (isDance && !DanceAvatar(path, importer)) return null;
        var src = importer.clipAnimations.Length > 0 ? importer.clipAnimations : importer.defaultClipAnimations;
        var clips = src.Select(c =>
        {
            c.name = file;
            c.loopTime = loop && !isTurn;
            c.loopPose = false;
            // Высота — как в клипе (Mixamo: ступни на полу). Танец снят с другого роста — по ступням, чтобы не висел и не проваливался.
            c.lockRootHeightY = true; c.keepOriginalPositionY = !isDance; c.heightFromFeet = isDance;
            // Ходьба на месте: смещение уходит в root motion, а его зомби не применяет — двигает NavMeshAgent.
            // У остальных клипов смещение корпуса остаётся в позе (падение, выпад при укусе).
            c.lockRootPositionXZ = folder != "Walk"; c.keepOriginalPositionXZ = true;
            // Поворот: у Turn — в root motion (зомби поворачивается вместе с анимацией), у остальных — в позе.
            c.lockRootRotation = !isTurn; c.keepOriginalOrientation = true;
            return c;
        }).ToArray();
        importer.clipAnimations = clips;
        importer.SaveAndReimport();

        var clip = AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>().FirstOrDefault(c => !c.name.StartsWith("__preview__"));
        if (clip != null)
            Debug.Log($"ZombieAnimationSetup: клип {folder}/{clip.name}: {clip.length:0.00} с, скорость {clip.averageSpeed.magnitude:0.00} м/с " +
                      $"(fwd {clip.averageSpeed.z:0.00}), угл. скорость {clip.averageAngularSpeed:0.000}, humanMotion {clip.humanMotion}, loop {clip.isLooping}");
        return clip;
    }

    // Клип танца из DeepMotion Animate 3D (снят по видео): скелет манекена с костями *_JNT. Автосопоставление Unity
    // на этих именах ненадёжно, поэтому кости Humanoid задаются явно.
    static readonly (string human, string bone)[] DanceBody =
    {
        ("Hips", "hips_JNT"), ("Spine", "spine_JNT"), ("Chest", "spine1_JNT"), ("UpperChest", "spine2_JNT"),
        ("Neck", "neck_JNT"), ("Head", "head_JNT"),
    };
    static readonly (string human, string bone)[] DanceSide =
    {
        ("Shoulder", "shoulder"), ("UpperArm", "arm"), ("LowerArm", "forearm"), ("Hand", "hand"),
        ("UpperLeg", "upleg"), ("LowerLeg", "leg"), ("Foot", "foot"), ("Toes", "toebase"),
    };
    static readonly (string human, string bone)[] DanceFingers =
    {
        ("Thumb", "handThumb"), ("Index", "handIndex"), ("Middle", "handMiddle"), ("Ring", "handRing"), ("Little", "handPinky"),
    };

    /// <summary>Аватар для клипа танца: явное сопоставление костей и скелет в T-позе (руки — горизонтально в стороны).</summary>
    static bool DanceAvatar(string path, ModelImporter importer)
    {
        var asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (asset == null) return false;
        var go = Object.Instantiate(asset);
        go.hideFlags = HideFlags.HideAndDontSave;
        try
        {
            var all = go.GetComponentsInChildren<Transform>(true);
            Transform Find(string n) => all.FirstOrDefault(t => t.name == n);
            var human = new List<HumanBone>();
            void Map(string h, string b)
            {
                if (Find(b) != null) human.Add(new HumanBone { humanName = h, boneName = b, limit = new HumanLimit { useDefaultValues = true } });
            }
            foreach (var (h, b) in DanceBody) Map(h, b);
            string[] parts = { "Proximal", "Intermediate", "Distal" };
            foreach (var (side, p) in new[] { ("Left", "l_"), ("Right", "r_") })
            {
                foreach (var (h, b) in DanceSide) Map(side + h, p + b + "_JNT");
                foreach (var (h, b) in DanceFingers)
                    for (int i = 0; i < 3; i++) Map(side + " " + h + " " + parts[i], p + b + (i + 1) + "_JNT");
            }
            if (human.Count < 15)
            {
                Debug.LogError($"ZombieAnimationSetup: {Path.GetFileName(path)} — не найдены кости DeepMotion (*_JNT), нашлось {human.Count}. Танец пропущен.");
                return false;
            }

            // T-поза: руки от плеча до кисти — горизонтально наружу (если манекен уже в T-позе, ничего не меняется).
            Transform hips = Find("hips_JNT");
            void Aim(Transform bone, Transform child, Vector3 dir)
            {
                if (bone == null || child == null) return;
                Vector3 cur = child.position - bone.position;
                if (cur.sqrMagnitude > 1e-8f) bone.rotation = Quaternion.FromToRotation(cur, dir) * bone.rotation;
            }
            foreach (var p in new[] { "l_", "r_" })
            {
                Transform up = Find(p + "arm_JNT"), lo = Find(p + "forearm_JNT"), hand = Find(p + "hand_JNT"), mid = Find(p + "handMiddle1_JNT");
                if (up == null || hips == null) continue;
                Vector3 r = go.transform.right;
                Vector3 outward = Mathf.Sign(Vector3.Dot(up.position - hips.position, r)) * r;
                Aim(up, lo, outward);
                Aim(lo, hand, outward);
                Aim(hand, mid, outward);
            }

            var desc = importer.humanDescription;
            desc.human = human.ToArray();
            desc.skeleton = all.Select(t => new SkeletonBone
            {
                name = t == go.transform ? asset.name : t.name,
                position = t.localPosition, rotation = t.localRotation, scale = t.localScale,
            }).ToArray();
            desc.upperArmTwist = 0.5f; desc.lowerArmTwist = 0.5f;
            desc.upperLegTwist = 0.5f; desc.lowerLegTwist = 0.5f;
            desc.armStretch = 0.05f; desc.legStretch = 0.05f;
            desc.feetSpacing = 0f;
            desc.hasTranslationDoF = false;
            importer.humanDescription = desc;
            importer.importCameras = false;
            importer.importLights = false;
            Debug.Log($"ZombieAnimationSetup: танец {Path.GetFileName(path)} — костей Humanoid {human.Count}");
            return true;
        }
        finally { Object.DestroyImmediate(go); }
    }

    // ---------- Контроллер ----------

    static AnimationClip Pick(Dictionary<string, AnimationClip> clips, params string[] names)
    {
        foreach (var n in names)
            foreach (var kv in clips)
                if (kv.Key.EndsWith("@" + n)) return kv.Value;
        return null;
    }

    /// <summary>
    /// Скорость ходьбы, под которую сделан клип «на месте»: пока ступня стоит на полу, она равномерно едет назад —
    /// с той скоростью, с которой тело должно идти вперёд. Берём участок от самой передней точки ступни, пока она
    /// движется назад (по кругу клипа): пройденное назад расстояние / время. Меряется на первой модели зомби.
    /// </summary>
    static float StanceSpeed(AnimationClip clip)
    {
        var modelPath = Fbx(ModelsFolder).FirstOrDefault();
        var prefab = modelPath != null ? AssetDatabase.LoadAssetAtPath<GameObject>(modelPath) : null;
        if (prefab == null || clip == null) return 1f;
        var go = Object.Instantiate(prefab);
        go.hideFlags = HideFlags.HideAndDontSave;
        try
        {
            var anim = go.GetComponent<Animator>();
            if (anim == null || !anim.isHuman) return 1f;
            var feet = new[] { anim.GetBoneTransform(HumanBodyBones.LeftFoot), anim.GetBoneTransform(HumanBodyBones.RightFoot) };
            const int steps = 120;
            var z = new float[2, steps];
            for (int i = 0; i < steps; i++)
            {
                clip.SampleAnimation(go, clip.length * i / steps);
                for (int f = 0; f < 2; f++) z[f, i] = go.transform.InverseTransformPoint(feet[f].position).z;
            }
            float dt = clip.length / steps, sum = 0f;
            for (int f = 0; f < 2; f++)
            {
                int start = 0;
                for (int i = 1; i < steps; i++) if (z[f, i] > z[f, start]) start = i;
                int n = 0; float back = 0f;
                for (int k = 0; k < steps; k++)
                {
                    float a0 = z[f, (start + k) % steps], a1 = z[f, (start + k + 1) % steps];
                    if (a1 > a0 + 0.002f) break; // ступня пошла вперёд — шаг в воздухе
                    back += a0 - a1; n++;
                }
                sum += n > 0 ? back / (n * dt) : 0f;
            }
            return Mathf.Clamp(sum / 2f, 0.1f, 3f);
        }
        finally { Object.DestroyImmediate(go); }
    }

    static AnimatorController BuildController(Dictionary<string, AnimationClip> clips)
    {
        var idle = Pick(clips, "Zombie Idle");
        var walk = Pick(clips, "Zombie Walk");
        var walkAlt = Pick(clips, "Walking") ?? walk;
        var turn = Pick(clips, "Zombie Turn");
        var attacks = new[] { Pick(clips, "Zombie Attack"), Pick(clips, "Zombie Neck Bite") };
        var hits = new[] { Pick(clips, "Zombie Reaction Hit"), Pick(clips, "Zombie Reaction Hit (1)") };
        var deaths = new[] { Pick(clips, "Zombie Death"), Pick(clips, "Zombie Dying") };
        if (idle == null || walk == null)
        {
            Debug.LogError("ZombieAnimationSetup: не найдены клипы Zombie Idle / Zombie Walk в " + AnimFolder);
            return null;
        }

        // Собираем во временный файл и копируем поверх старого — GUID контроллера (и ссылки на него в сценах) сохраняется.
        Directory.CreateDirectory(Path.GetDirectoryName(ControllerPath));
        string tmp = Path.ChangeExtension(ControllerPath, null) + "_build.controller";
        AssetDatabase.DeleteAsset(tmp);
        var ac = AnimatorController.CreateAnimatorControllerAtPath(tmp);
        ac.AddParameter("Move", AnimatorControllerParameterType.Float);       // 0 — стоит, 1 — идёт
        ac.AddParameter("WalkSpeed", AnimatorControllerParameterType.Float);  // множитель скорости ходьбы (под скорость агента)
        // Скорость, при которой ступни в клипе ходьбы не скользят (клипы «In Place» — меряем по ступням). Читает Zombie.cs.
        ac.AddParameter("WalkClipSpeed", AnimatorControllerParameterType.Float);
        ac.AddParameter("WalkAltClipSpeed", AnimatorControllerParameterType.Float);
        float walkClipSpeed = StanceSpeed(walk), walkAltClipSpeed = StanceSpeed(walkAlt);
        Debug.Log($"ZombieAnimationSetup: скорость шага без скольжения — {walk.name} {walkClipSpeed:0.00} м/с, {walkAlt.name} {walkAltClipSpeed:0.00} м/с");
        ac.parameters = ac.parameters.Select(p =>
        {
            if (p.name == "WalkSpeed") p.defaultFloat = 1f;
            if (p.name == "WalkClipSpeed") p.defaultFloat = walkClipSpeed;
            if (p.name == "WalkAltClipSpeed") p.defaultFloat = walkAltClipSpeed;
            return p;
        }).ToArray();

        var sm = ac.layers[0].stateMachine;
        AnimatorState Loco(string name, AnimationClip w)
        {
            var tree = new BlendTree { name = name, blendType = BlendTreeType.Simple1D, blendParameter = "Move", useAutomaticThresholds = false, hideFlags = HideFlags.HideInHierarchy };
            AssetDatabase.AddObjectToAsset(tree, ac);
            tree.AddChild(idle, 0f);
            tree.AddChild(w, 1f);
            var s = sm.AddState(name);
            s.motion = tree;
            s.speedParameter = "WalkSpeed";
            s.speedParameterActive = true;
            return s;
        }
        sm.defaultState = Loco(Locomotion, walk);
        Loco(LocomotionAlt, walkAlt);

        AnimatorState State(AnimatorStateMachine m, string name, Motion clip, bool mirror = false)
        {
            var s = m.AddState(name);
            s.motion = clip;
            s.mirror = mirror;
            return s;
        }
        if (turn != null) { State(sm, Turn, turn); State(sm, TurnMirror, turn, true); }
        for (int i = 0; i < Attacks.Length; i++) State(sm, Attacks[i], attacks[i] ?? attacks.FirstOrDefault(a => a != null));
        for (int i = 0; i < Deaths.Length; i++) State(sm, Deaths[i], deaths[i] ?? deaths.FirstOrDefault(a => a != null));
        var dance = Pick(clips, "Dance");
        if (dance != null) State(sm, Dance, dance); // пасхалка: танец по секретной кнопке (Zombie.cs, ZombieDance)

        // Слой реакций на попадания — только верх тела, ноги продолжают идти.
        ac.AddLayer("Hit");
        var layers = ac.layers;
        layers[1].defaultWeight = 1f;
        layers[1].blendingMode = AnimatorLayerBlendingMode.Override;
        layers[1].avatarMask = UpperBodyMask();
        ac.layers = layers;
        var hsm = ac.layers[1].stateMachine;
        var empty = hsm.AddState("Empty");
        hsm.defaultState = empty;
        for (int i = 0; i < Hits.Length; i++)
        {
            var s = State(hsm, Hits[i], hits[i] ?? hits.FirstOrDefault(h => h != null));
            var tr = s.AddTransition(empty);
            tr.hasExitTime = true;
            tr.exitTime = 0.8f;
            tr.duration = 0.2f;
        }

        EditorUtility.SetDirty(ac);
        AssetDatabase.SaveAssets();
        if (File.Exists(ControllerPath))
        {
            File.Copy(tmp, ControllerPath, true);
            AssetDatabase.DeleteAsset(tmp);
            AssetDatabase.ImportAsset(ControllerPath, ImportAssetOptions.ForceUpdate);
        }
        else
        {
            AssetDatabase.MoveAsset(tmp, ControllerPath);
        }
        return AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
    }

    static AvatarMask UpperBodyMask()
    {
        var mask = AssetDatabase.LoadAssetAtPath<AvatarMask>(UpperBodyMaskPath);
        bool created = mask == null;
        if (created) mask = new AvatarMask();
        for (int i = 0; i < (int)AvatarMaskBodyPart.LastBodyPart; i++)
        {
            var part = (AvatarMaskBodyPart)i;
            bool on = part == AvatarMaskBodyPart.Body || part == AvatarMaskBodyPart.Head
                      || part == AvatarMaskBodyPart.LeftArm || part == AvatarMaskBodyPart.RightArm
                      || part == AvatarMaskBodyPart.LeftFingers || part == AvatarMaskBodyPart.RightFingers;
            mask.SetHumanoidBodyPartActive(part, on);
        }
        if (created) AssetDatabase.CreateAsset(mask, UpperBodyMaskPath); else EditorUtility.SetDirty(mask);
        return mask;
    }

    // ---------- Сцена ----------

    /// <summary>Материал хлопьев пепла: неосвещаемые частицы с мягким краем, цвет — из частицы, полупрозрачные.</summary>
    static void AshMaterial()
    {
        var mat = AssetDatabase.LoadAssetAtPath<Material>(AshFlakePath);
        bool created = mat == null;
        var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
        if (shader == null) return;
        if (created) mat = new Material(shader) { name = "FX_Ash" }; else mat.shader = shader;
        mat.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(AshTexturePath));
        mat.SetColor("_BaseColor", Color.white);
        mat.SetFloat("_Surface", 1f);
        mat.SetFloat("_Blend", 0f);
        mat.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
        mat.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        mat.SetFloat("_SrcBlendAlpha", (float)UnityEngine.Rendering.BlendMode.One);
        mat.SetFloat("_DstBlendAlpha", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        mat.SetFloat("_ZWrite", 0f);
        mat.SetFloat("_Cull", (float)UnityEngine.Rendering.CullMode.Off);
        mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        if (created) { Directory.CreateDirectory(Path.GetDirectoryName(AshFlakePath)); AssetDatabase.CreateAsset(mat, AshFlakePath); }
        else EditorUtility.SetDirty(mat);
    }

    static void AssignToSpawners(RuntimeAnimatorController controller)
    {
        foreach (var sp in Object.FindObjectsByType<ZombieSpawner>(FindObjectsInactive.Include))
        {
            var flake = AssetDatabase.LoadAssetAtPath<Material>(AshFlakePath);
            var dust = AssetDatabase.LoadAssetAtPath<Material>(AshDustPath);
            if (sp.zombieAnimator == controller && sp.ashFlakeMaterial == flake && (sp.ashDustMaterial != null || dust == null)) continue;
            bool wasDirty = sp.gameObject.scene.isDirty;
            Undo.RecordObject(sp, "Assign zombie animator");
            sp.zombieAnimator = controller;
            sp.ashFlakeMaterial = flake;
            if (sp.ashDustMaterial == null) sp.ashDustMaterial = dust;
            EditorUtility.SetDirty(sp);
            EditorSceneManager.MarkSceneDirty(sp.gameObject.scene);
            if (!wasDirty) EditorSceneManager.SaveScene(sp.gameObject.scene);
            Debug.Log("ZombieAnimationSetup: контроллер назначен спавнеру в сцене " + sp.gameObject.scene.name +
                      (wasDirty ? " (сохраните сцену)" : " (сцена сохранена)"));
        }
    }
}
