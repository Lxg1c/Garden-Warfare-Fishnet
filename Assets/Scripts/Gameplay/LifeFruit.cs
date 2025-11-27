using Core.Components;
using FishNet.Object;
using UnityEngine;
using Core.Settings;

namespace Gameplay
{
    [RequireComponent(typeof(Health))]
    public class LifeFruit : NetworkBehaviour
    {
        [Header("Owner info")]
        [SerializeField] private int ownerIdDebug = -1;

        private Health _health;

        private void Awake()
        {
            _health = GetComponent<Health>();

            // Подписки на события
            _health.OnDamaged += OnDamaged;
            _health.OnDeath += OnDeath;
        }

        public override void OnStartServer()
        {
            base.OnStartServer();

            int currentOwnerId = OwnerId;
            ownerIdDebug = currentOwnerId;

            // Сообщаем менеджеру, что этот игрок жив и у него есть база
            RespawnManager.SetRespawnEnabled(currentOwnerId, true);
        }

        private void OnDamaged(Transform attacker)
        {
            // ИСПРАВЛЕНИЕ: Используем IsServerInitialized вместо IsServer
            if (!IsServerInitialized) return;

            // Если наш владелец попал по своему предмету, то отменяем урон
            if (attacker != null)
            {
                var attackerNO = attacker.GetComponent<NetworkObject>();

                // Проверяем: атакующий существует И его OwnerId совпадает с OwnerId этого фрукта
                if (attackerNO != null && attackerNO.OwnerId == OwnerId)
                {
                    Debug.Log($"[Server] LifeFruit({OwnerId}) ignored self damage");

                    // Восстанавливаем здоровье
                    _health.SetHealth(_health.GetMaxHealth());
                }
            }
        }

        private void OnDeath(Transform dead)
        {
            // ИСПРАВЛЕНИЕ: Используем IsServerInitialized вместо IsServer
            if (!IsServerInitialized) return;

            // Запрещаем респавн для владельца этого фрукта
            RespawnManager.SetRespawnEnabled(OwnerId, false);
        }

        public override void OnStopNetwork()
        {
            base.OnStopNetwork();
            if (_health != null)
            {
                _health.OnDamaged -= OnDamaged;
                _health.OnDeath -= OnDeath;
            }
        }
    }
}