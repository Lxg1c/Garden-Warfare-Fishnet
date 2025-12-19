using System;
using FishNet.Object;
using UnityEngine;
using Core.Spawn;
using FischlWorks_FogWar;

namespace Player
{
    public class PlayerInitializer : NetworkBehaviour
    {
        [Header("Fog of War")]
        [SerializeField] private int fogRevealRadius = 12;
        public FogOfWarController fogController;

        private csFogVisibilityAgent _visibilityAgent;
        
        // Events
        public static event Action<int, csFogWar> OnPlayerFogReady;
        
        public override void OnStartClient()
        {
            if (!IsOwner) return;
            
            base.OnStartClient();

            _visibilityAgent = GetComponent<csFogVisibilityAgent>();
            
            if (fogController == null)
            {
                fogController = FindFirstObjectByType<FogOfWarController>();
            }
                
            csFogWar fogInstance = null;

            if (fogController != null)
            {
                fogController.InitializeForPlayer(transform, fogRevealRadius);
                    
                fogInstance = fogController.GetFogInstance();
                if (fogInstance != null && _visibilityAgent != null)
                {
                    _visibilityAgent.SetFogWar(fogInstance);
                    Debug.Log("Fog of war instance set to visibility agent");
                }
                else
                {
                    Debug.LogWarning("Failed to set fog war to visibility agent (visibilityAgent or fogInstance null)");
                }
            }
            else
            {
                Debug.LogError("FogOfWarController not found!");
            }

            // Вызов события — только если fogInstance есть
            if (fogInstance != null)
            {
                OnPlayerFogReady?.Invoke(OwnerId, fogInstance);
            }
        }

        public override void OnStartServer()
        {
            base.OnStartServer();
            Debug.Log($"PlayerInitializer: Player {OwnerId} connected");
        }

        public override void OnStopServer()
        {
            base.OnStopServer();
            Debug.Log($"PlayerInitializer: Player {OwnerId} disconnected");
        }
        
        [Server]
        public void OnPlayerDeath()
        {
            Debug.Log($"PlayerInitializer: Player {OwnerId} died");
            // Дополнительная логика при смерти игрока
        }
        
        [Server]
        public void OnLifeFruitDestroyed()
        {
            Debug.Log($"PlayerInitializer: LifeFruit for player {OwnerId} destroyed");
            // Дополнительная логика когда LifeFruit уничтожен
        }
    }
}
