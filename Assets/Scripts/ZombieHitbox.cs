using UnityEngine;

/// <summary>
/// Коллайдер части тела зомби (голова, туловище, руки, ноги). Попадание пули передаётся владельцу
/// с множителем урона: в голову — больше, в конечности — меньше.
/// </summary>
public class ZombieHitbox : MonoBehaviour
{
    public Zombie owner;
    public float multiplier = 1f;
    public bool isHead;
}
