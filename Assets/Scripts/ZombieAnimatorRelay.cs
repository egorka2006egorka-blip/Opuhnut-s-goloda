using UnityEngine;

/// <summary>
/// Висит на модели зомби рядом с Animator: передаёт root motion в Zombie (поворот из клипа Zombie Turn).
/// Пока есть OnAnimatorMove, Animator сам модель не двигает — перемещением управляет NavMeshAgent.
/// </summary>
public class ZombieAnimatorRelay : MonoBehaviour
{
    public Zombie owner;

    void OnAnimatorMove()
    {
        if (owner != null) owner.OnAnimatorMoved();
    }
}
