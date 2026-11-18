using System.Collections.Generic;
using UnityEngine;

/// <summary>Записка в ящике в подвале.</summary>
public class StoryNote : Interactable
{
    bool read;
    public override string Prompt => read ? "Перечитать записку" : "Прочитать записку";

    void Awake()
    {
        if (GetComponentInChildren<Renderer>() != null) return;
        var paper = Mat(new Color(0.9f, 0.88f, 0.8f), 0.1f);
        Part(transform, PrimitiveType.Cube, new Vector3(0f, 0.002f, 0f), new Vector3(0.15f, 0.003f, 0.2f), paper, new Vector3(0f, 17f, 0f), true);
        Part(transform, PrimitiveType.Cube, new Vector3(0f, 0.004f, 0.02f), new Vector3(0.1f, 0.001f, 0.004f), Mat(new Color(0.15f, 0.15f, 0.35f)), new Vector3(0f, 17f, 0f));
        Part(transform, PrimitiveType.Cube, new Vector3(0f, 0.004f, -0.01f), new Vector3(0.11f, 0.001f, 0.004f), Mat(new Color(0.15f, 0.15f, 0.35f)), new Vector3(0f, 17f, 0f));
    }

    public override void Interact(GameObject player)
    {
        read = true;
        if (DialogueSystem.CurrentId != "note") DialogueSystem.Play("note");
    }
}
