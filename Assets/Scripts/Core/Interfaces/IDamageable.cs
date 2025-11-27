using UnityEngine;

namespace Core.Interfaces
{
    public interface IDamageable
    {
        void TakeDamage(float amount, Transform attacker = null);
    }
}