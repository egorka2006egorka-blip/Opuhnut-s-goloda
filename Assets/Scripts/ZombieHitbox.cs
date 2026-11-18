using UnityEngine;

/// <summary>
/// Коллайдер части тела зомби (голова, туловище, руки, ноги). Попадание пули передаётся владельцу
/// с множителем урона: в голову — больше, в конечности — меньше.
/// </summary>
public class ZombieHitbox : MonoBehaviour
{
    public enum Part { Head, Neck, Torso, UpperArm, Forearm, Thigh, Shin }

    public Zombie owner;
    [Tooltip("Владелец-босс (если хитбокс принадлежит боссу, а не обычному зомби).")]
    public ZombieBoss boss;
    public float multiplier = 1f;
    public bool isHead;
    public Part part = Part.Torso;
    public bool IsLeg => part == Part.Thigh || part == Part.Shin;
    public bool HasOwner => owner != null || boss != null;

    /// <summary>Передаёт попадание пули владельцу (зомби или боссу).</summary>
    public void Hit(float damage, Vector3 point, Vector3 direction)
    {
        if (owner != null) owner.TakeHit(damage, point, direction, this);
        else if (boss != null) boss.TakeHit(damage, point, direction, this);
    }
}
