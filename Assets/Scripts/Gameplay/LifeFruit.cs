using Core.Components;
using FishNet.Object;
using UnityEngine;
using Core.Settings;
using FischlWorks_FogWar;
using Player;

namespace Gameplay
{
    [RequireComponent(typeof(Health))]
    [RequireComponent(typeof(csFogVisibilityAgent))]
    public class LifeFruit : NetworkBehaviour
    { 
        private Health _health;
        private csFogVisibilityAgent _visibilityAgent;

        private void Awake()
        {
            _health = GetComponent<Health>();
            _visibilityAgent = GetComponent<csFogVisibilityAgent>();
            
            _health.OnDamaged += OnDamaged;
            _health.OnDeath += OnDeath;
        }

        public override void OnStartServer()
        {
            base.OnStartServer();
            
            Debug.Log($"[Server] LifeFruit spawned for player {OwnerId}");
            
            RespawnManager.SetRespawnEnabled(OwnerId, true);
        }
        
        public override void OnStartClient()
        {
            base.OnStartClient();
            Debug.Log($"[Client] LifeFruit({OwnerId}) created; trying to attach to fog...");
            
            TryAttachToPlayerFog();
            
            PlayerInitializer.OnPlayerFogReady += ApplyFog;
        }

        public override void OnStopClient()
        {
            base.OnStopClient();
            // отписываемся при остановке клиента
            Player.PlayerInitializer.OnPlayerFogReady -= ApplyFog;
        }

        private void TryAttachToPlayerFog()
        {
            var conn = NetworkManager?.ClientManager?.Connection;
            if (conn == null) return;

            var playerObj = conn.FirstObject;
            if (playerObj == null) return;

            var initializer = playerObj.GetComponent<Player.PlayerInitializer>();
            if (initializer == null) return;

            var fog = initializer.fogController?.GetFogInstance();
            if (fog != null)
            {
                ApplyFog(initializer.OwnerId, fog);
            }
        }
        
        private void ApplyFog(int playerId, csFogWar fog)
        {
            if (playerId != OwnerId) return;

            if (_visibilityAgent == null)
                _visibilityAgent = GetComponent<csFogVisibilityAgent>();

            if (_visibilityAgent != null)
            {
                _visibilityAgent.SetFogWar(fog);
                
                fog._FogRevealers.Add(
                    new csFogWar.FogRevealer(transform, 4, true)
                );
            }
        }

        private void OnDamaged(Transform attacker)
        {
            if (!IsServerInitialized) return;
            
            if (attacker != null)
            {
                var attackerNo = attacker.GetComponent<NetworkObject>();
                
                if (attackerNo != null && attackerNo.OwnerId == OwnerId)
                {
                    Debug.Log($"[Server] LifeFruit({OwnerId}) ignored self damage");
                    _health.SetHealth(_health.GetMaxHealth());
                    return;
                }
            }
            
            Debug.Log($"[Server] LifeFruit({OwnerId}) took damage from {attacker?.name}");
        }

        private void OnDeath(Transform dead)
        {
            if (!IsServerInitialized) return;
            
            Debug.Log($"[Server] LifeFruit({OwnerId}) destroyed");
            
            // Запрещаем респаун для владельца
            RespawnManager.SetRespawnEnabled(OwnerId, false);
            
            // Уведомляем PlayerInitializer
            NotifyPlayerInitializer();
            
            // Уничтожаем объект
            ServerManager.Despawn(gameObject);
        }

        [Server]
        private void NotifyPlayerInitializer()
        {
            // Находим PlayerInitializer владельца и уведомляем его
            foreach (var client in NetworkManager.ServerManager.Clients.Values)
            {
                if (client.FirstObject != null)
                {
                    var initializer = client.FirstObject.GetComponent<Player.PlayerInitializer>();
                    if (initializer != null && initializer.OwnerId == OwnerId)
                    {
                        initializer.OnLifeFruitDestroyed();
                        break;
                    }
                }
            }
        }

        public override void OnStopNetwork()
        {
            base.OnStopNetwork();
            if (_health != null)
            {
                _health.OnDamaged -= OnDamaged;
                _health.OnDeath -= OnDeath;
            }

            // Safety unsubscribe
            Player.PlayerInitializer.OnPlayerFogReady -= ApplyFog;
        }
    }
}
