using System;
using FishNet.Object;
using UnityEngine;
using Core.Settings;
using Core.Spawn;
using FischlWorks_FogWar;
using Player.Components;

namespace Player
{
    public class PlayerInitializer : NetworkBehaviour
    {
        [Header("Dependencies")]
        private RespawnManager _respawnManager;
        
        [Header("Spawn Settings")]
        private Transform _defaultSpawnPoint;
        
        // Fog of war
        public FogOfWarController fogController;
        private csFogVisibilityAgent _visibilityAgent;
        
        // Events
        public static event Action<int, csFogWar> OnPlayerFogReady;


        public override void OnStartClient()
        {
            base.OnStartClient();

            _defaultSpawnPoint = transform;
            _visibilityAgent = GetComponent<csFogVisibilityAgent>();
            
            if (fogController == null)
            {
                fogController = FindFirstObjectByType<FogOfWarController>();
            }
                
            csFogWar fogInstance = null;

            if (fogController != null)
            {
                fogController.InitializeForPlayer(transform, 6);
                    
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
            
            if (_respawnManager == null)
            {
                _respawnManager = FindFirstObjectByType<RespawnManager>();
            }
            
            if (_respawnManager == null)
            {
                Debug.LogError("RespawnManager not found!");
                return;
            }
            
            Debug.Log($"PlayerInitializer: Player {OwnerId} connected");
            
            InitializePlayerLifeFruit();
        }

        [Server]
        private void InitializePlayerLifeFruit()
        {
            if (_respawnManager != null)
            {
                Transform spawnPoint = GetSpawnPoint();
                Vector3 fruitPosition = spawnPoint.position + Vector3.up * 1f + Vector3.back * 2f;
                _respawnManager.CreateLifeFruitForPlayer(OwnerId, fruitPosition);
            }
        }

        [Server]
        private Transform GetSpawnPoint()
        {
            PlayerInfo playerInfo = GetComponent<PlayerInfo>();
            if (playerInfo != null && playerInfo.SpawnPoint != null)
            {
                return playerInfo.SpawnPoint;
            }
            
            if (GameSpawnManager.Instance != null)
            {
                Transform spawnPoint = GameSpawnManager.Instance.GetPlayerSpawnPoint(OwnerId);
                if (spawnPoint != null) return spawnPoint;
            }
            
            return _defaultSpawnPoint != null ? _defaultSpawnPoint : transform;
        }

        public override void OnStopServer()
        {
            base.OnStopServer();
            
            if (_respawnManager != null)
            {
                _respawnManager.DestroyLifeFruitForPlayer(OwnerId);
            }
            
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
