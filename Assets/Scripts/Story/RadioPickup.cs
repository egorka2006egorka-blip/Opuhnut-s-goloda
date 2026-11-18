using System.Collections.Generic;
using UnityEngine;

/// <summary>Рация на кассе: взять — связь с Шапиком.</summary>
public class RadioPickup : Interactable
{
    public override string Prompt => "Взять рацию";
    public override bool Available => StoryDirector.Instance == null || StoryDirector.Instance.RadioAvailable;

    void Awake()
    {
        if (GetComponentInChildren<Renderer>() != null) return;
        // Рация лежит на боку: корпус, антенна, динамик, лампочка.
        var body = Mat(new Color(0.12f, 0.12f, 0.13f), 0.4f);
        Part(transform, PrimitiveType.Cube, new Vector3(0f, 0.022f, 0f), new Vector3(0.065f, 0.044f, 0.16f), body, default, true);
        Part(transform, PrimitiveType.Cylinder, new Vector3(0.015f, 0.022f, 0.14f), new Vector3(0.012f, 0.06f, 0.012f), body, new Vector3(90f, 0f, 0f));
        Part(transform, PrimitiveType.Cube, new Vector3(0f, 0.0445f, -0.02f), new Vector3(0.045f, 0.002f, 0.06f), Mat(new Color(0.3f, 0.3f, 0.3f), 0.2f));
        Part(transform, PrimitiveType.Sphere, new Vector3(-0.02f, 0.046f, 0.06f), Vector3.one * 0.008f, Mat(new Color(0.2f, 1f, 0.2f), 0.5f, 0f, new Color(0.3f, 1.5f, 0.3f)));
    }

    public override void Interact(GameObject player)
    {
        var inv = player.GetComponent<Inventory>();
        if (inv != null) inv.Add(ItemDatabase.Radio, 1);
        if (StoryDirector.Instance != null) StoryDirector.Instance.OnRadioTaken();
        Destroy(gameObject);
    }
}
