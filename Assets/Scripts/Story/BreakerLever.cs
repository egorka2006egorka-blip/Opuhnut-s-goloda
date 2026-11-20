using UnityEngine;

/// <summary>Рычаг на щитке BreakerPanel: E — переключить (вверх/вниз). Логику цепей держит щиток.</summary>
public class BreakerLever : Interactable
{
    BreakerPanel panel;
    int index;
    Transform handle;
    bool up = true;
    float angle = 35f;

    public override string Prompt => panel != null ? panel.LeverPrompt(index) : "";
    public override bool Available => panel != null && !panel.Solved;
    public override bool Usable => panel != null && panel.Usable;
    public override Vector3 Focus => handle != null ? handle.position : transform.position;

    public void Setup(BreakerPanel p, int i)
    {
        panel = p;
        index = i;
        range = 1.8f;
        var metal = Mat(new Color(0.55f, 0.55f, 0.58f), 0.6f, 0.9f);
        var grip = Mat(new Color(0.75f, 0.1f, 0.08f), 0.4f);
        handle = Node(transform, "Handle", Vector3.zero);
        Part(handle, PrimitiveType.Cube, new Vector3(0f, 0.07f, 0.03f), new Vector3(0.025f, 0.14f, 0.025f), metal, default, true);
        Part(handle, PrimitiveType.Cube, new Vector3(0f, 0.15f, 0.04f), new Vector3(0.07f, 0.035f, 0.04f), grip, default, true);
        handle.localRotation = Quaternion.Euler(angle, 0f, 0f); // +X-поворот наклоняет рычаг к игроку (из щитка)
    }

    public void Toggle() => up = !up;

    public override void Interact(GameObject player) => panel.Press(index);

    void Update()
    {
        if (handle == null) return;
        var target = Quaternion.Euler(up ? angle : 180f - angle, 0f, 0f);
        handle.localRotation = Quaternion.Slerp(handle.localRotation, target, 1f - Mathf.Exp(-Time.deltaTime * 18f));
    }
}
