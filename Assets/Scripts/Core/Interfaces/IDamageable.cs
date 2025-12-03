using UnityEngine;
using FishNet.Object;

namespace Core.Interfaces
{
    public interface IDamageable
    {
        void TakeDamage(float amount, Transform attacker = null, NetworkObject attackerNetworkObject = null);
    }
}