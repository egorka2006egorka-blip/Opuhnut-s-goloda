using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Танец «Я тебя могну» для одного зомби — процедурно, поверх Animator (вызывается из Zombie.LateUpdate).
///
/// Связка снята по видео (14,4 с, 120 BPM, новая фигура каждые 4 доли):
///   0–4    кулаки у плеч, большие пальцы показывают на себя;
///   4–12   «моггинг»: корпус на ¾ влево с наклоном, подбородок вверх, левая ладонь ведёт по челюсти,
///          правая рука вниз-в сторону;
///   12–16  зеркально: правый кулак под подбородком, корпус и голова вправо;
///   16–20  правая рука прямая через грудь, кулаком влево;
///   20–24  правое предплечье поперёк груди;
///   24–29  стоит ровно, руки вдоль тела.
/// Всё время — пружинящее покачивание в такт и покачивание плеч.
///
/// Как строится поза: корпус и голова — поза привязки (модель стоит прямо, руки в A-позе) с поворотами
/// в осях корня зомби; руки и ноги — аналитический IK на две кости (цель кисти/стопы + направление локтя/колена);
/// пальцы — мышцы Humanoid (HumanPoseHandler). Так поза не зависит от осей костей конкретной модели.
/// </summary>
public class ZombieDancer
{
    readonly Transform root;
    readonly Avatar avatar;
    readonly Transform modelRoot;

    readonly Transform hips, spine, chest, neck, head, clavL, clavR;
    readonly Transform upL, loL, handL, upR, loR, handR;
    readonly Transform thighL, shinL, footL, thighR, shinR, footR;

    // Поза привязки: повороты относительно корня зомби, локальные повороты конечностей, точки в осях корня.
    readonly Quaternion hipsB, spineB, chestB, neckB, headB, clavLB, clavRB, footLB, footRB;
    readonly Quaternion upLB, loLB, handLB, upRB, loRB, handRB, thighLB, shinLB, thighRB, shinRB;
    readonly Vector3 hipsPosB, footLPosB, footRPosB;

    readonly Transform[] all;
    readonly Quaternion[] saved;

    HumanPoseHandler poseHandler;
    HumanPose pose;
    int[] fingersL, fingersR, thumbL, thumbR, indexL, indexR, spreadL, spreadR;

    // Сглаженные цели (в осях корня) — движения резкие, но без телепортов между фигурами.
    Vector3 tHandL, tHandR, tElbowL, tElbowR, tHead, tTorso;
    float tHip, tStance, tThumbs, tFistL, tFistR, tFlatL;
    bool primed;

    public static ZombieDancer Create(Animator animator, Transform root)
    {
        if (animator == null || animator.avatar == null || !animator.avatar.isHuman) return null;
        var d = new ZombieDancer(animator, root);
        return d.ok ? d : null;
    }

    readonly bool ok;

    ZombieDancer(Animator animator, Transform rootT)
    {
        root = rootT;
        avatar = animator.avatar;
        modelRoot = animator.transform;

        // Кости — по описанию аватара (GetBoneTransform не работает, пока объект выключен).
        var byName = new Dictionary<string, Transform>();
        foreach (var t in modelRoot.GetComponentsInChildren<Transform>(true))
            if (!byName.ContainsKey(t.name)) byName[t.name] = t;
        var map = new Dictionary<string, Transform>();
        foreach (var hb in avatar.humanDescription.human)
            if (byName.TryGetValue(hb.boneName, out var t)) map[hb.humanName] = t;
        Transform B(string h) => map.TryGetValue(h, out var t) ? t : null;

        hips = B("Hips"); spine = B("Spine"); chest = B("Chest"); neck = B("Neck"); head = B("Head");
        clavL = B("LeftShoulder"); clavR = B("RightShoulder");
        upL = B("LeftUpperArm"); loL = B("LeftLowerArm"); handL = B("LeftHand");
        upR = B("RightUpperArm"); loR = B("RightLowerArm"); handR = B("RightHand");
        thighL = B("LeftUpperLeg"); shinL = B("LeftLowerLeg"); footL = B("LeftFoot");
        thighR = B("RightUpperLeg"); shinR = B("RightLowerLeg"); footR = B("RightFoot");
        if (hips == null || spine == null || head == null || upL == null || loL == null || handL == null || upR == null || loR == null || handR == null ||
            thighL == null || shinL == null || footL == null || thighR == null || shinR == null || footR == null)
            return;
        if (chest == null) chest = spine;
        if (neck == null) neck = head;

        Quaternion inv = Quaternion.Inverse(root.rotation);
        Quaternion Rel(Transform t) => t != null ? inv * t.rotation : Quaternion.identity;
        hipsB = Rel(hips); spineB = Rel(spine); chestB = Rel(chest); neckB = Rel(neck); headB = Rel(head);
        clavLB = Rel(clavL); clavRB = Rel(clavR); footLB = Rel(footL); footRB = Rel(footR);
        upLB = upL.localRotation; loLB = loL.localRotation; handLB = handL.localRotation;
        upRB = upR.localRotation; loRB = loR.localRotation; handRB = handR.localRotation;
        thighLB = thighL.localRotation; shinLB = shinL.localRotation; thighRB = thighR.localRotation; shinRB = shinR.localRotation;
        hipsPosB = root.InverseTransformPoint(hips.position);
        footLPosB = root.InverseTransformPoint(footL.position);
        footRPosB = root.InverseTransformPoint(footR.position);

        var bones = new List<Transform>();
        foreach (var t in map.Values) if (t != null && !bones.Contains(t)) bones.Add(t);
        all = bones.ToArray();
        saved = new Quaternion[all.Length];
        ok = true;
    }

    // ---------- Хореография (снята по видео «Я тебя могну (MOGGED)», 14,4 с, 120 BPM: 1 доля = 0,5 с) ----------

    static float Smooth(float x) => Mathf.SmoothStep(0f, 1f, x);

    /// <summary>Поза в осях корня. S — середина между плечами, всё в метрах модели. Стороны — от лица танцора.</summary>
    struct Figure
    {
        public Vector3 handL, handR, elbowL, elbowR; // цели кистей и направления локтей
        public Vector3 headAngles;                    // наклон (x, «−» — подбородок вверх), поворот (y, «−» — влево), крен (z)
        public Vector3 torso;                         // корпус: наклон назад (x, «−»), разворот (y), отклонение вбок (z, «+» — влево)
        public float hipShift, stance;                // сдвиг таза вправо, насколько шире стойка (м на каждую стопу)
        public float thumbs, fistL, fistR, flatL;     // пальцы: большие на себя, кулаки, ровная левая ладонь
        public float bounce;                          // сила покачивания в такт
        public float pumpL, pumpR;                    // насколько рука «качает» вверх-вниз на каждую долю (0..1)
    }

    /// <summary>Резкий импульс на каждую долю: быстрый удар (≈ 0,04 с) и короткий возврат. 1 — в момент доли.</summary>
    static float Hit(float beat)
    {
        float x = beat - Mathf.Floor(beat);
        float v = x < 0.08f ? x / 0.08f : Mathf.Max(0f, 1f - (x - 0.08f) / 0.32f);
        return v * v * (3f - 2f * v);
    }

    /// <summary>Длина связки в долях (видео 14,4 с при 120 BPM).</summary>
    public const float TotalBeats = 28.8f;
    // Границы фигур в долях — по смене поз на видео: 0,2 / 2,1 / 6,1 / 8,1 / 10,1 / 12,1 с.
    static readonly float[] Marks = { 0.35f, 4.2f, 12.2f, 16.2f, 20.2f, 24.2f };

    static Figure Choreo(float beat, Vector3 S, Vector3 shL, Vector3 shR, Vector3 headPos)
    {
        Vector3 U = Vector3.up;
        var f = new Figure { bounce = 1f, stance = 0.04f, pumpL = 0.3f, pumpR = 0.3f };
        // Опущенная вниз-в сторону рука (свободная рука в «моггинге» и в выпадах).
        Vector3 DownOut(Vector3 sh, float side, float outward) => sh + new Vector3(side * outward, -0.78f, 0.22f).normalized * 0.52f;
        // Точка на челюсти с учётом поворота головы: lateral — к какой стороне (−1 левый угол челюсти, +1 правый), 0 — подбородок.
        Vector3 Chin(float yaw, float lateral)
        {
            Quaternion q = Quaternion.Euler(0f, yaw, 0f);
            Vector3 Fh = q * Vector3.forward, Rh = q * Vector3.right;
            float a = Mathf.Abs(lateral);
            return headPos + Fh * Mathf.Lerp(0.11f, 0.02f, a) + Rh * 0.065f * lateral - U * Mathf.Lerp(0.035f, 0.01f, a);
        }
        Vector3 Neutral(Vector3 sh, float side) => sh + new Vector3(side * 0.05f, -0.5f, 0.07f);

        if (beat < Marks[0] || beat >= Marks[5])
        {
            // Начало и финал: стоит ровно, руки вдоль тела, кисти слегка сжаты, лёгкое покачивание, улыбка в камеру.
            f.handL = Neutral(shL, -1f); f.handR = Neutral(shR, 1f);
            f.elbowL = new Vector3(-0.2f, 0f, -1f); f.elbowR = new Vector3(0.2f, 0f, -1f);
            f.fistL = f.fistR = 0.5f;
            f.headAngles = new Vector3(-4f, 0f, 3f);
            f.bounce = 0.7f;
        }
        else if (beat < Marks[1])
        {
            // «Вот он я»: кулаки у плеч, локти вниз-в стороны, большие пальцы показывают на себя.
            f.handR = S + new Vector3(0.27f, 0.03f, 0.1f);
            f.handL = S + new Vector3(-0.27f, 0.03f, 0.1f);
            f.elbowR = new Vector3(1f, -1f, -0.1f); f.elbowL = new Vector3(-1f, -1f, -0.1f);
            f.thumbs = 1f;
            f.pumpL = f.pumpR = 1f;
            f.headAngles = new Vector3(-3f, 0f, 6f * Mathf.Sin(Mathf.PI * beat * 0.5f));
        }
        else if (beat < Marks[2])
        {
            // Моггинг влево: корпус на ¾ влево, отклонён влево-назад, подбородок вверх; левая ладонь ведёт
            // от подбородка к углу челюсти (2 доли) и держит; правая рука — вниз-в сторону. С 4 с наклон глубже.
            float deep = Smooth((beat - 8.2f) / 0.6f);
            float yaw = 5f; // голова — относительно корпуса (−25°)
            float slide = Smooth((beat - Marks[1] - 0.3f) / 2f);
            Vector3 jaw = Chin(-25f + yaw, -slide * 0.85f);
            f.handL = jaw - U * 0.085f + new Vector3(-0.025f, 0f, 0.035f);
            f.elbowL = new Vector3(-0.35f, -0.8f, 0.6f);
            f.handR = DownOut(shR, 1f, 0.75f);
            f.elbowR = new Vector3(0.2f, 0f, -1f);
            f.flatL = 1f; f.fistR = 0.2f;
            f.pumpL = 0.15f; f.pumpR = 0.6f; // ладонь у челюсти почти не отрывается, свободная рука качает
            f.torso = new Vector3(-5f - 5f * deep, -25f, 6f + 6f * deep);
            f.hipShift = 0.05f + 0.03f * deep;
            f.stance = 0.09f;
            f.headAngles = new Vector3(-16f - 4f * deep, yaw, 10f);
            f.bounce = 0.5f;
        }
        else if (beat < Marks[3])
        {
            // Зеркально: правый кулак через грудь у левой стороны подбородка, левая рука вниз-в сторону, голова вправо.
            float yaw = 5f; // голова — относительно корпуса (+12°)
            f.handR = Chin(12f + yaw, -0.3f) - U * 0.09f + new Vector3(0.02f, 0f, 0.05f);
            f.elbowR = new Vector3(0.6f, -0.6f, 0.6f);
            f.handL = DownOut(shL, -1f, 0.8f);
            f.elbowL = new Vector3(-0.2f, 0f, -1f);
            f.fistR = 1f; f.fistL = 0.2f;
            f.pumpR = 0.6f; f.pumpL = 0.6f;
            f.torso = new Vector3(-6f, 12f, -5f);
            f.hipShift = -0.04f;
            f.stance = 0.09f;
            f.headAngles = new Vector3(-14f, yaw, -8f);
            f.bounce = 0.6f;
        }
        else if (beat < Marks[4])
        {
            // Правая рука почти прямая через грудь на уровне плеча — кулаком влево; корпус развёрнут влево.
            f.handR = S + new Vector3(-0.36f, 0.02f, 0.24f);
            f.elbowR = new Vector3(0f, -1f, 0.3f);
            f.handL = DownOut(shL, -1f, 0.4f);
            f.elbowL = new Vector3(-0.3f, 0f, -1f);
            f.fistR = 1f; f.fistL = 0.3f;
            f.pumpR = 0.8f; f.pumpL = 0.5f;
            f.torso = new Vector3(-3f, -20f, 3f);
            f.hipShift = 0.02f;
            f.stance = 0.08f;
            f.headAngles = new Vector3(-8f, 8f, -4f);
        }
        else
        {
            // Фронтальнее: правое предплечье горизонтально поперёк груди, кулак у левого плеча.
            f.handR = S + new Vector3(-0.14f, -0.03f, 0.27f);
            f.elbowR = new Vector3(1f, -0.6f, 0.3f);
            f.handL = DownOut(shL, -1f, 0.45f);
            f.elbowL = new Vector3(-0.3f, 0f, -1f);
            f.fistR = 1f; f.fistL = 0.3f;
            f.pumpR = 1f; f.pumpL = 0.5f;
            f.torso = new Vector3(-4f, -10f, 2f);
            f.stance = 0.07f;
            f.headAngles = new Vector3(-10f, 0f, 5f);
        }
        return f;
    }

    // ---------- Применение ----------

    /// <summary>Строит танцевальную позу поверх текущей (после Animator) с весом w.</summary>
    public void Apply(float w, float beat, Vector3 camWorld, float dt)
    {
        if (w <= 0f) { primed = false; return; }
        for (int i = 0; i < all.Length; i++) saved[i] = all[i].localRotation;
        Vector3 savedHips = hips.localPosition;
        Vector3 camL = root.InverseTransformPoint(camWorld);

        // Фигура считается дважды: сначала корпус (по нему встают плечи), затем руки от новых плеч.
        var fig0 = Choreo(beat, Vector3.zero, Vector3.zero, Vector3.zero, Vector3.zero);
        float k = primed ? 1f - Mathf.Exp(-dt * 38f) : 1f;    // руки и пальцы: смена поз — рывком, как на видео
        float kb = primed ? 1f - Mathf.Exp(-dt * 24f) : 1f;   // корпус и голова — чуть мягче
        tTorso = Vector3.Lerp(tTorso, fig0.torso, kb);
        tHip = Mathf.Lerp(tHip, fig0.hipShift, kb);
        tStance = Mathf.Lerp(tStance, fig0.stance, kb);
        tThumbs = Mathf.Lerp(tThumbs, fig0.thumbs, k);
        tFistL = Mathf.Lerp(tFistL, fig0.fistL, k);
        tFistR = Mathf.Lerp(tFistR, fig0.fistR, k);
        tFlatL = Mathf.Lerp(tFlatL, fig0.flatL, k);

        // Пальцы — мышцы задаются до IK (SetHumanPose переписывает всю позу).
        Fingers();

        // --- Корпус: пружинящее покачивание в такт (вниз на долю) + наклоны фигуры ---
        float groove = Hit(beat); // резко вниз на долю, быстро обратно
        float sway = Mathf.Sin(Mathf.PI * beat);
        float bounce = Mathf.Lerp(0.4f, 1f, fig0.bounce);
        float drop = 0.03f + 0.05f * bounce * groove + tStance * 0.25f;
        hips.position = root.TransformPoint(hipsPosB + new Vector3(tHip + 0.012f * sway * bounce, -drop, -0.01f));
        Quaternion rq = root.rotation;
        hips.rotation = rq * Quaternion.Euler(0f, tTorso.y * 0.4f, -tTorso.z * 0.3f - 2f * sway * bounce) * hipsB;
        spine.rotation = rq * Quaternion.Euler(tTorso.x * 0.5f + 2f * groove * bounce, tTorso.y * 0.7f, tTorso.z * 0.5f + 1.5f * sway * bounce) * spineB;
        if (chest != spine) chest.rotation = rq * Quaternion.Euler(tTorso.x, tTorso.y, tTorso.z + 3f * sway * bounce) * chestB; // плечи покачиваются
        if (clavL != null) clavL.rotation = rq * Quaternion.Euler(0f, tTorso.y, tTorso.z) * clavLB;
        if (clavR != null) clavR.rotation = rq * Quaternion.Euler(0f, tTorso.y, tTorso.z) * clavRB;
        if (neck != head) neck.rotation = rq * Quaternion.Euler(tTorso.x * 0.5f, tTorso.y, tTorso.z * 0.5f) * neckB;

        // --- Ноги: стопы на полу (в выпадах стойка шире), колени чуть врозь ---
        LegIK(thighL, shinL, footL, thighLB, shinLB, footLPosB + Vector3.left * tStance, footLB, -1f);
        LegIK(thighR, shinR, footR, thighRB, shinRB, footRPosB + Vector3.right * tStance, footRB, 1f);

        // --- Руки и голова ---
        Vector3 shL = root.InverseTransformPoint(upL.position), shR = root.InverseTransformPoint(upR.position);
        Vector3 S = (shL + shR) * 0.5f;
        Vector3 headPos = root.InverseTransformPoint(head.position);
        var fig = Choreo(beat, S, shL, shR, headPos);

        tHandL = Vector3.Lerp(tHandL, fig.handL, k);
        tHandR = Vector3.Lerp(tHandR, fig.handR, k);
        // Согнутые руки качают вверх-вниз на каждую долю: рывок вниз вместе с приседанием и подброс вверх на «и».
        // Плечо на месте, поэтому локоть сам сгибается-разгибается. Импульс — после сглаживания, чтобы не размылся.
        float pump = 0.055f * groove - 0.025f * Hit(beat + 0.5f);
        Vector3 handLNow = tHandL + Vector3.down * pump * fig.pumpL;
        Vector3 handRNow = tHandR + Vector3.down * pump * fig.pumpR;
        tElbowL = Vector3.Lerp(tElbowL, fig.elbowL, k);
        tElbowR = Vector3.Lerp(tElbowR, fig.elbowR, k);

        // Голова: фигура + половина взгляда в камеру (поверх разворота корпуса) + кивок в такт.
        Vector3 toCam = camL - headPos;
        float lookYaw = Mathf.Clamp(Mathf.Atan2(toCam.x, toCam.z) * Mathf.Rad2Deg, -45f, 45f) - tTorso.y;
        float lookPitch = Mathf.Clamp(-Mathf.Atan2(toCam.y, new Vector2(toCam.x, toCam.z).magnitude) * Mathf.Rad2Deg, -25f, 25f);
        Vector3 headWant = new Vector3(lookPitch * 0.5f + 4f * groove * bounce, tTorso.y + lookYaw * 0.5f, 0f) + fig.headAngles;
        tHead = Vector3.Lerp(tHead, headWant, kb);
        primed = true;

        ArmIK(upL, loL, handL, upLB, loLB, handLB, handLNow, tElbowL);
        ArmIK(upR, loR, handR, upRB, loRB, handRB, handRNow, tElbowR);
        head.rotation = rq * Quaternion.Euler(tHead) * headB;

        if (w < 1f)
        {
            for (int i = 0; i < all.Length; i++) all[i].localRotation = Quaternion.Slerp(saved[i], all[i].localRotation, w);
            hips.localPosition = Vector3.Lerp(savedHips, hips.localPosition, w);
        }
    }

    void LegIK(Transform thigh, Transform shin, Transform foot, Quaternion thighB, Quaternion shinB, Vector3 footLocal, Quaternion footB, float side)
    {
        thigh.localRotation = thighB;
        shin.localRotation = shinB;
        Vector3 knee = root.TransformDirection(new Vector3(0.18f * side, 0f, 1f)); // колени вперёд и чуть врозь
        TwoBoneIK(thigh, shin, foot, root.TransformPoint(footLocal), knee);
        foot.rotation = root.rotation * footB; // стопа ровно на полу
    }

    void ArmIK(Transform up, Transform lo, Transform hand, Quaternion upB, Quaternion loB, Quaternion handB, Vector3 target, Vector3 elbowDir)
    {
        up.localRotation = upB;
        lo.localRotation = loB;
        hand.localRotation = handB;
        TwoBoneIK(up, lo, hand, root.TransformPoint(target), root.TransformDirection(elbowDir));
    }

    /// <summary>Аналитический IK на две кости: конец c — в target, средний сустав — в сторону hint.</summary>
    static void TwoBoneIK(Transform a, Transform b, Transform c, Vector3 target, Vector3 hint)
    {
        Vector3 pa = a.position;
        float la = Vector3.Distance(pa, b.position), lb = Vector3.Distance(b.position, c.position);
        if (la < 1e-4f || lb < 1e-4f) return;
        Vector3 toT = target - pa;
        float d = Mathf.Clamp(toT.magnitude, Mathf.Abs(la - lb) + 1e-3f, (la + lb) * 0.999f);
        Vector3 dir = toT.sqrMagnitude > 1e-8f ? toT.normalized : a.forward;
        float cosA = Mathf.Clamp((la * la + d * d - lb * lb) / (2f * la * d), -1f, 1f);
        Vector3 perp = Vector3.ProjectOnPlane(hint, dir);
        if (perp.sqrMagnitude < 1e-6f) perp = Vector3.ProjectOnPlane(Vector3.down, dir);
        perp.Normalize();
        Vector3 mid = pa + dir * (la * cosA) + perp * (la * Mathf.Sqrt(1f - cosA * cosA));

        a.rotation = Quaternion.FromToRotation(b.position - pa, mid - pa) * a.rotation;
        Vector3 end = pa + dir * d;
        b.rotation = Quaternion.FromToRotation(c.position - b.position, end - b.position) * b.rotation;
    }

    // ---------- Пальцы (мышцы Humanoid) ----------

    void Fingers()
    {
        if (poseHandler == null)
        {
            poseHandler = new HumanPoseHandler(avatar, modelRoot);
            pose = new HumanPose();
            var names = HumanTrait.MuscleName;
            int[] Find(string side, string[] fingers, string[] parts)
            {
                var list = new List<int>();
                foreach (var fn in fingers)
                    foreach (var part in parts)
                    {
                        int i = System.Array.IndexOf(names, $"{side} {fn} {part}");
                        if (i >= 0) list.Add(i);
                    }
                return list.ToArray();
            }
            string[] stretched = { "1 Stretched", "2 Stretched", "3 Stretched" };
            string[] others = { "Middle", "Ring", "Little" };
            indexL = Find("Left", new[] { "Index" }, stretched); indexR = Find("Right", new[] { "Index" }, stretched);
            fingersL = Find("Left", others, stretched); fingersR = Find("Right", others, stretched);
            thumbL = Find("Left", new[] { "Thumb" }, stretched); thumbR = Find("Right", new[] { "Thumb" }, stretched);
            spreadL = Find("Left", new[] { "Index", "Middle", "Ring", "Little" }, new[] { "Spread" });
            spreadR = Find("Right", new[] { "Index", "Middle", "Ring", "Little" }, new[] { "Spread" });
        }
        if (indexR.Length == 0 && fingersR.Length == 0) return; // у модели нет пальцев

        poseHandler.GetHumanPose(ref pose);
        // Кулак: пальцы согнуты; «большие на себя»: кулак с прямым большим; ровная ладонь — у челюсти.
        const float relaxed = -0.2f;
        float fr = Mathf.Lerp(relaxed, -1f, Mathf.Max(tFistR, tThumbs));
        float fl = Mathf.Lerp(Mathf.Lerp(relaxed, -1f, Mathf.Max(tFistL, tThumbs)), 0.9f, tFlatL);
        Set(indexR, fr); Set(fingersR, fr);
        Set(indexL, fl); Set(fingersL, fl);
        Set(thumbR, Mathf.Lerp(Mathf.Lerp(relaxed, -0.7f, tFistR), 1f, tThumbs));
        Set(thumbL, Mathf.Lerp(Mathf.Lerp(Mathf.Lerp(relaxed, -0.7f, tFistL), 0.6f, tFlatL), 1f, tThumbs));
        Set(spreadL, -0.3f * tFlatL);
        poseHandler.SetHumanPose(ref pose);
    }

    void Set(int[] idx, float v)
    {
        foreach (int i in idx) pose.muscles[i] = Mathf.Clamp(v, -1f, 1f);
    }
}
