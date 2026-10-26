using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Распашная калитка или дверь: створки поворачиваются на петлях вокруг вертикали.
/// Открывается и закрывается на E (см. PlayerWeaponHandler). Створки распахиваются от игрока.
/// </summary>
public class SwingGate : MonoBehaviour
{
    static readonly List<SwingGate> all = new List<SwingGate>();

    [Tooltip("Створки.")]
    public Transform[] leaves;
    [Tooltip("Петли створок в системе координат их родителей. Если не заданы — петля в pivot створки.")]
    public Vector3[] hinges;
    [Tooltip("Угол открытия (градусы).")]
    public float openAngle = 80f;
    [Tooltip("Время открытия/закрытия (с).")]
    public float duration = 0.6f;

    [Header("Ручка (у двери)")]
    [Tooltip("Ручка: при открытии поворачивается вокруг своей оси Z (оси, проходящей сквозь дверь).")]
    public Transform handle;
    [Tooltip("Поворот ручки при нажатии (градусы, знак — вниз).")]
    public float handlePressAngle = -30f;
    [Tooltip("Нажать, подержать и отпустить ручку (с). Дверь трогается, когда ручка опущена.")]
    public float handlePressTime = 0.5f;

    public bool IsOpen { get; private set; }

    Quaternion[] closedRot;
    Vector3[] closedPos, hinge;
    float[] dirs;
    float t; // 0 — закрыто, 1 — открыто
    Quaternion handleRest;
    float handleClock = -1f; // < 0 — ручку не трогают

    /// <summary>Калитка, к створке которой относится t (или null).</summary>
    public static SwingGate Find(Transform t)
    {
        foreach (var g in all)
            foreach (var leaf in g.leaves)
                if (leaf != null && t.IsChildOf(leaf)) return g;
        return null;
    }

    void OnEnable() => all.Add(this);
    void OnDisable() => all.Remove(this);

    void Awake()
    {
        int n = leaves.Length;
        closedRot = new Quaternion[n];
        closedPos = new Vector3[n];
        hinge = new Vector3[n];
        dirs = new float[n];
        for (int i = 0; i < n; i++)
        {
            if (leaves[i] == null) continue;
            closedRot[i] = leaves[i].localRotation;
            closedPos[i] = leaves[i].localPosition;
            hinge[i] = hinges != null && i < hinges.Length ? hinges[i] : closedPos[i];
        }
        if (handle != null) handleRest = handle.localRotation;
    }

    /// <summary>Переключает калитку. viewer — откуда смотрят: при открытии створки уходят от него.</summary>
    public void Toggle(Vector3 viewer)
    {
        if (!IsOpen)
        {
            for (int i = 0; i < leaves.Length; i++)
                if (leaves[i] != null) dirs[i] = SwingDirection(i, viewer);
            if (handle != null) handleClock = 0f;
        }
        IsOpen = !IsOpen;
    }

    /// <summary>Знак поворота, при котором свободный край створки уходит от точки from.</summary>
    float SwingDirection(int i, Vector3 from)
    {
        Transform leaf = leaves[i];
        Vector3 hingeWorld = leaf.parent != null ? leaf.parent.TransformPoint(hinge[i]) : hinge[i];
        Vector3 center = LeafCenter(leaf);
        Vector3 edge = center - hingeWorld;
        edge.y = 0f;
        Vector3 away = center - from;
        away.y = 0f;
        // Поворот на +угол вокруг вертикали сдвигает край в сторону Cross(up, edge).
        return Vector3.Dot(Vector3.Cross(Vector3.up, edge), away) >= 0f ? 1f : -1f;
    }

    static Vector3 LeafCenter(Transform leaf)
    {
        var rs = leaf.GetComponentsInChildren<Renderer>();
        if (rs.Length == 0) return leaf.position;
        var b = rs[0].bounds;
        foreach (var r in rs) b.Encapsulate(r.bounds);
        return b.center;
    }

    const float HandleDown = 0.3f, HandleUp = 0.6f; // доли handlePressTime: ручка опущена, начинает подниматься

    void Update()
    {
        UpdateHandle();
        // Сначала нажимаем ручку, потом толкаем дверь.
        if (IsOpen && handleClock >= 0f && handleClock < handlePressTime * HandleDown) return;
        float target = IsOpen ? 1f : 0f;
        if (Mathf.Approximately(t, target)) return;
        t = Mathf.MoveTowards(t, target, Time.deltaTime / Mathf.Max(0.05f, duration));
        float k = Mathf.SmoothStep(0f, 1f, t);
        for (int i = 0; i < leaves.Length; i++)
        {
            if (leaves[i] == null) continue;
            // Вертикаль родителя: створка вращается вокруг своей петли.
            Vector3 up = leaves[i].parent != null ? leaves[i].parent.InverseTransformDirection(Vector3.up) : Vector3.up;
            Quaternion r = Quaternion.AngleAxis(dirs[i] * openAngle * k, up);
            leaves[i].localRotation = r * closedRot[i];
            leaves[i].localPosition = hinge[i] + r * (closedPos[i] - hinge[i]);
        }
    }

    void UpdateHandle()
    {
        if (handleClock < 0f) return;
        handleClock += Time.deltaTime;
        float x = handleClock / Mathf.Max(0.05f, handlePressTime);
        float w = x < HandleDown ? Mathf.SmoothStep(0f, 1f, x / HandleDown)
                : x < HandleUp ? 1f
                : Mathf.SmoothStep(1f, 0f, (x - HandleUp) / (1f - HandleUp));
        if (x >= 1f) { w = 0f; handleClock = -1f; }
        handle.localRotation = handleRest * Quaternion.AngleAxis(handlePressAngle * w, Vector3.forward);
    }
}
