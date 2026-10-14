using UnityEngine;

/// <summary>
/// Процедурная анимация тела персонажа MainCharacter (без Animator):
///  • без оружия руки опущены вдоль тела и качаются в такт шагам, ноги шагают;
///  • с оружием руки через IK обхватывают точки хвата оружия (gripRight / gripLeft), пальцы сжимают рукоять.
/// Работает с любым поворотом осей костей: ориентация кисти считается по положению пальцев.
/// </summary>
[DefaultExecutionOrder(100)]
public class PlayerBodyAnimator : MonoBehaviour
{
    public Transform characterRoot;
    public FirstPersonController controller;
    public PlayerWeaponHandler weapons;
    public Transform cameraTransform;

    [Header("Свободные руки")]
    [Tooltip("Насколько ниже плеча находится запястье (м).")]
    public float handDrop = 0.585f;
    [Tooltip("Амплитуда маха руками при ходьбе (м).")]
    public float armSwing = 0.16f;
    public float handOutward = 0.035f;

    [Header("Ноги")]
    public float legSwingAngle = 26f;
    public float kneeBendAngle = 40f;

    [Header("С оружием")]
    [Range(0f, 1f)] public float armsFollowPitch = 0.6f;

    class Arm
    {
        public bool isRight;
        public Transform upper, fore, hand;
        public Quaternion upperRest, foreRest, handRest;
        public float upperLen, foreLen;
        public Transform[][] fingers = new Transform[4][]; // index, middle, ring, pinky
        public Quaternion[][] fingerRest = new Quaternion[4][];
        public Transform[] thumb;
        public Quaternion[] thumbRest;
        public Vector3 localF, localP, localPalm, localThumbAxis;
        public Quaternion basisInv;
    }

    class Leg
    {
        public Transform thigh, shin;
        public Quaternion thighRest, shinRest;
    }

    static readonly string[] FingerNames = { "Index", "Middle", "Ring", "Pinky" };

    Arm rightArm, leftArm;
    Leg rightLeg, leftLeg;
    Transform armsRoot;
    Quaternion armsRootRest;
    bool ready;

    void Awake()
    {
        if (characterRoot == null) { Debug.LogError("PlayerBodyAnimator: не задан characterRoot"); return; }

        // Определяем, какая сторона скелета физически справа от игрока.
        Transform upR = FindBone("UpperArm.R"), upL = FindBone("UpperArm.L");
        if (upR == null || upL == null) { Debug.LogError("PlayerBodyAnimator: не найдены кости рук"); return; }
        bool namesMatch = transform.InverseTransformPoint(upR.position).x > transform.InverseTransformPoint(upL.position).x;
        string rs = namesMatch ? "R" : "L", ls = namesMatch ? "L" : "R";

        rightArm = BuildArm(rs, true);
        leftArm = BuildArm(ls, false);
        rightLeg = BuildLeg(rs);
        leftLeg = BuildLeg(ls);
        armsRoot = FindBone("Arms_Root");
        if (armsRoot != null) armsRootRest = armsRoot.localRotation;
        ready = rightArm != null && leftArm != null;
    }

    // ---------- Поиск и подготовка костей ----------

    Transform FindBone(string name)
    {
        string key = Simplify(name);
        foreach (var t in characterRoot.GetComponentsInChildren<Transform>(true))
            if (Simplify(t.name) == key) return t;
        return null;
    }

    static string Simplify(string s)
    {
        var sb = new System.Text.StringBuilder();
        foreach (char c in s) if (char.IsLetterOrDigit(c)) sb.Append(char.ToLowerInvariant(c));
        return sb.ToString();
    }

    Arm BuildArm(string s, bool isRight)
    {
        var a = new Arm { isRight = isRight };
        a.upper = FindBone("UpperArm." + s);
        a.fore = FindBone("Forearm." + s);
        a.hand = FindBone("Hand." + s);
        if (a.upper == null || a.fore == null || a.hand == null) return null;
        a.upperRest = a.upper.localRotation;
        a.foreRest = a.fore.localRotation;
        a.handRest = a.hand.localRotation;
        a.upperLen = Vector3.Distance(a.upper.position, a.fore.position);
        a.foreLen = Vector3.Distance(a.fore.position, a.hand.position);

        for (int f = 0; f < 4; f++)
        {
            a.fingers[f] = new Transform[3];
            a.fingerRest[f] = new Quaternion[3];
            for (int j = 0; j < 3; j++)
            {
                a.fingers[f][j] = FindBone(FingerNames[f] + (j + 1) + "." + s);
                if (a.fingers[f][j] != null) a.fingerRest[f][j] = a.fingers[f][j].localRotation;
            }
        }
        a.thumb = new Transform[3];
        a.thumbRest = new Quaternion[3];
        for (int j = 0; j < 3; j++)
        {
            a.thumb[j] = FindBone("Thumb" + (j + 1) + "." + s);
            if (a.thumb[j] != null) a.thumbRest[j] = a.thumb[j].localRotation;
        }

        // Базис кисти по положению пальцев: F — направление пальцев, P — нормаль ладони.
        Vector3 handPos = a.hand.position;
        Transform middle = a.fingers[1][0], index = a.fingers[0][0], pinky = a.fingers[3][0];
        Vector3 F = middle != null ? (middle.position - handPos).normalized : a.hand.up;
        Vector3 T = (index != null && pinky != null) ? (index.position - pinky.position).normalized : a.hand.right;
        Vector3 P = isRight ? Vector3.Cross(F, T) : Vector3.Cross(T, F);
        P = Vector3.ProjectOnPlane(P, F).normalized;
        float palmLen = middle != null ? Vector3.Distance(middle.position, handPos) : 0.08f;
        Vector3 palm = handPos + F * (palmLen * 0.6f) + P * 0.016f;

        a.localF = a.hand.InverseTransformDirection(F);
        a.localP = a.hand.InverseTransformDirection(P);
        a.localPalm = a.hand.InverseTransformPoint(palm);
        a.basisInv = Quaternion.Inverse(Quaternion.LookRotation(a.localF, a.localP));
        if (a.thumb[0] != null && a.thumb[1] != null)
        {
            Vector3 td = (a.thumb[1].position - a.thumb[0].position).normalized;
            a.localThumbAxis = a.hand.InverseTransformDirection(Vector3.Cross(td, P).normalized);
        }
        return a;
    }

    Leg BuildLeg(string s)
    {
        var l = new Leg { thigh = FindBone("Thigh." + s), shin = FindBone("Shin." + s) };
        if (l.thigh == null || l.shin == null) return null;
        l.thighRest = l.thigh.localRotation;
        l.shinRest = l.shin.localRotation;
        return l;
    }

    // ---------- Покадровая анимация ----------

    void LateUpdate()
    {
        if (!ready) return;

        float phase = controller != null ? controller.StepPhase : 0f;
        float move = controller != null ? controller.MoveAmount : 0f;
        float armed = weapons != null ? weapons.ArmedWeight : 0f;
        WeaponPickup weapon = weapons != null ? weapons.CurrentWeapon : null;

        ResetArm(rightArm);
        ResetArm(leftArm);

        // Ноги.
        AnimateLeg(rightLeg, phase, move);
        AnimateLeg(leftLeg, phase + Mathf.PI, move);

        // С оружием плечи немного следуют за наклоном взгляда, чтобы руки дотягивались до оружия.
        if (armsRoot != null)
        {
            armsRoot.localRotation = armsRootRest;
            float pitch = controller != null ? controller.Pitch : 0f;
            armsRoot.rotation = Quaternion.AngleAxis(pitch * armsFollowPitch * armed, transform.right) * armsRoot.rotation;
        }

        AnimateArm(rightArm, phase, move, armed, weapon);
        AnimateArm(leftArm, phase, move, armed, weapon);
    }

    static void ResetArm(Arm a)
    {
        a.upper.localRotation = a.upperRest;
        a.fore.localRotation = a.foreRest;
        a.hand.localRotation = a.handRest;
        for (int f = 0; f < 4; f++)
            for (int j = 0; j < 3; j++)
                if (a.fingers[f][j] != null) a.fingers[f][j].localRotation = a.fingerRest[f][j];
        for (int j = 0; j < 3; j++)
            if (a.thumb[j] != null) a.thumb[j].localRotation = a.thumbRest[j];
    }

    void AnimateLeg(Leg l, float phase, float move)
    {
        if (l == null) return;
        l.thigh.localRotation = l.thighRest;
        l.shin.localRotation = l.shinRest;
        Vector3 right = transform.right;
        float swing = Mathf.Sin(phase) * legSwingAngle * move;
        // Колено сгибается, когда нога идёт вперёд (в воздухе).
        float knee = Mathf.Max(0f, Mathf.Cos(phase)) * kneeBendAngle * move;
        l.thigh.rotation = Quaternion.AngleAxis(-swing - knee * 0.3f, right) * l.thigh.rotation;
        l.shin.rotation = Quaternion.AngleAxis(knee, right) * l.shin.rotation;
    }

    void AnimateArm(Arm a, float phase, float move, float armed, WeaponPickup weapon)
    {
        float side = a.isRight ? 1f : -1f;
        Vector3 right = transform.right, up = transform.up, fwd = transform.forward;
        Vector3 shoulder = a.upper.position;

        // --- Поза «руки вдоль тела» ---
        // Правая рука идёт назад, когда правая нога идёт вперёд (и наоборот) — как у живого человека.
        float swing = -side * Mathf.Sin(phase) * armSwing * move;
        float breathe = Mathf.Sin(Time.time * 1.6f) * 0.004f;
        Vector3 relaxedWrist = shoulder
            + right * (side * handOutward)
            + up * (-handDrop + Mathf.Abs(swing) * 0.3f + breathe)
            + fwd * (0.09f + swing);
        Vector3 relaxedHint = shoulder + up * -0.3f + fwd * -0.5f + right * (side * 0.12f);
        Vector3 relaxedF = (-up + fwd * (0.12f + swing * 2f)).normalized;
        Vector3 relaxedP = (-right * side - fwd * 0.3f).normalized;
        Quaternion relaxedRot = HandRotation(a, relaxedF, relaxedP);

        Vector3 wrist = relaxedWrist, hint = relaxedHint;
        Quaternion handRot = relaxedRot;

        // --- Поза «держит оружие» ---
        Transform grip = weapon != null ? (a.isRight ? weapon.gripRight : weapon.gripLeft) : null;
        float w = grip != null ? armed : 0f;
        // Левая рука подключается чуть позже правой — сначала берём, потом перехватываем.
        if (!a.isRight) w = Mathf.Clamp01((w - 0.35f) / 0.65f);
        // Одноручное оружие: левая рука свободна и берётся за оружие только при перезарядке.
        if (!a.isRight && weapon != null && weapon.oneHanded) w *= weapons.ReloadHandWeight;
        if (w > 0f)
        {
            Vector3 gripPos = grip.position;
            Quaternion gripRot = HandRotation(a, grip.forward, grip.up);
            // Перезарядка: левая рука работает с магазином и затвором, а не держит рукоять.
            if (!a.isRight && weapons != null && weapons.ReloadHandWeight > 0f)
            {
                Quaternion rr = weapons.ReloadHandRot;
                gripPos = weapons.ReloadHandPos;
                gripRot = HandRotation(a, rr * Vector3.forward, rr * Vector3.up);
            }
            Vector3 gripWrist = gripPos - gripRot * Vector3.Scale(a.localPalm, a.hand.lossyScale);
            Quaternion view = cameraTransform != null ? cameraTransform.rotation : transform.rotation;
            Vector3 gripHint = shoulder + view * new Vector3(side * 0.45f, -0.75f, 0.05f);

            wrist = Vector3.Lerp(wrist, gripWrist, w);
            hint = Vector3.Lerp(hint, gripHint, w);
            handRot = Quaternion.Slerp(handRot, gripRot, w);
        }

        SolveIK(a, wrist, hint);
        a.hand.rotation = handRot;

        // --- Пальцы ---
        // Расслабленная кисть: пальцы слегка согнуты.
        Vector3 c = new Vector3(14f, 18f, 10f);
        Vector3 ci = new Vector3(10f, 14f, 8f);
        Vector3 ct = new Vector3(6f, 8f, 6f);
        if (w > 0f)
        {
            Vector3 gc, gi, gt;
            if (a.isRight) { gc = new Vector3(72f, 85f, 45f); gi = new Vector3(18f, 38f, 22f); gt = new Vector3(18f, 28f, 22f); }
            else { gc = new Vector3(55f, 62f, 38f); gi = new Vector3(50f, 55f, 35f); gt = new Vector3(22f, 22f, 12f); }
            c = Vector3.Lerp(c, gc, w); ci = Vector3.Lerp(ci, gi, w); ct = Vector3.Lerp(ct, gt, w);
            // Взведение затвора: указательный и большой пальцы сведены щипком, остальные сжаты.
            if (!a.isRight && weapons != null && weapons.ReloadPinch > 0f)
            {
                float pz = weapons.ReloadPinch;
                c = Vector3.Lerp(c, new Vector3(65f, 80f, 50f), pz);
                ci = Vector3.Lerp(ci, new Vector3(28f, 30f, 18f), pz);
                ct = Vector3.Lerp(ct, new Vector3(30f, 30f, 20f), pz);
            }
        }
        CurlFingers(a, c, ci, ct);
    }

    static Quaternion HandRotation(Arm a, Vector3 fingersDir, Vector3 palmNormal)
    {
        return Quaternion.LookRotation(fingersDir, palmNormal) * a.basisInv;
    }

    /// <summary>Аналитический IK на две кости (плечо — предплечье) с «подсказкой» направления локтя.</summary>
    void SolveIK(Arm a, Vector3 target, Vector3 hint)
    {
        Vector3 root = a.upper.position;
        float l1 = a.upperLen, l2 = a.foreLen;
        Vector3 toTarget = target - root;
        float d = toTarget.magnitude;
        if (d < 1e-4f) return;
        Vector3 dir = toTarget / d;
        d = Mathf.Clamp(d, Mathf.Abs(l1 - l2) + 0.001f, (l1 + l2) * 0.999f);

        float cosA = Mathf.Clamp((l1 * l1 + d * d - l2 * l2) / (2f * l1 * d), -1f, 1f);
        float sinA = Mathf.Sqrt(1f - cosA * cosA);
        Vector3 bend = Vector3.ProjectOnPlane(hint - root, dir);
        if (bend.sqrMagnitude < 1e-6f) bend = Vector3.ProjectOnPlane(-transform.up, dir);
        bend.Normalize();

        Vector3 elbow = root + dir * (l1 * cosA) + bend * (l1 * sinA);
        Vector3 end = root + dir * d;

        // 1) Направляем плечевую кость на локоть.
        a.upper.rotation = Quaternion.FromToRotation(a.fore.position - root, elbow - root) * a.upper.rotation;
        // 2) Скручиваем плечо вокруг своей оси, чтобы локоть сгибался как шарнир в нужной плоскости.
        Vector3 upperDir = (elbow - root).normalized;
        Vector3 curPerp = Vector3.ProjectOnPlane(a.hand.position - a.fore.position, upperDir);
        Vector3 wantPerp = Vector3.ProjectOnPlane(end - elbow, upperDir);
        if (curPerp.sqrMagnitude > 1e-6f && wantPerp.sqrMagnitude > 1e-6f && Vector3.Dot(curPerp.normalized, wantPerp.normalized) > -0.999f)
            a.upper.rotation = Quaternion.FromToRotation(curPerp, wantPerp) * a.upper.rotation;
        // 3) Разгибаем/сгибаем локоть до цели.
        a.fore.rotation = Quaternion.FromToRotation(a.hand.position - a.fore.position, end - a.fore.position) * a.fore.rotation;
    }

    static void CurlFingers(Arm a, Vector3 curl, Vector3 indexCurl, Vector3 thumbCurl)
    {
        Vector3 F = a.hand.TransformDirection(a.localF);
        Vector3 P = a.hand.TransformDirection(a.localP);
        Vector3 axis = Vector3.Cross(F, P).normalized; // поворот вокруг этой оси сгибает пальцы к ладони
        for (int f = 0; f < 4; f++)
        {
            Vector3 c = f == 0 ? indexCurl : curl;
            for (int j = 0; j < 3; j++)
            {
                Transform b = a.fingers[f][j];
                if (b != null) b.rotation = Quaternion.AngleAxis(c[j], axis) * b.rotation;
            }
        }
        if (a.localThumbAxis != Vector3.zero)
        {
            Vector3 tAxis = a.hand.TransformDirection(a.localThumbAxis);
            for (int j = 0; j < 3; j++)
                if (a.thumb[j] != null) a.thumb[j].rotation = Quaternion.AngleAxis(thumbCurl[j], tAxis) * a.thumb[j].rotation;
        }
    }
}
