using UnityEngine;

/// <summary>
/// Дверь, которая не открывается никогда (ведёт наружу модели: главный вход, бывшая лестница вверх, выход из серверной).
/// При взгляде на неё — подсказка «Закрыто» без клавиши. Ставится сборщиком сцены на двери без SwingGate.
/// В отличие от запертой SwingGate, через такую дверь не прокладываются пути зомби.
/// </summary>
public class ClosedDoor : Interactable
{
    public string prompt = "Закрыто";

    /// <summary>С какого расстояния (м, от камеры до середины двери) видна подсказка — только вплотную к двери.</summary>
    const float PromptRange = 1.3f;

    protected override void OnEnable()
    {
        range = PromptRange; // в уже собранной сцене могло остаться прежнее значение
        base.OnEnable();
    }

    public override string Prompt => prompt;
    public override bool Usable => false;
    public override void Interact(GameObject player) { }
}
