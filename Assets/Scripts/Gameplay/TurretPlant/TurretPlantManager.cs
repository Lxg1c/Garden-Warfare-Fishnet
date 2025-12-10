using System.Collections.Generic;
using FishNet.Object;
using UnityEngine;

namespace Gameplay.TurretPlant
{
    public class TurretPlantManager : NetworkBehaviour
    {
        public static TurretPlantManager Instance { get; private set; }

        [Header("Settings")]
        [SerializeField] private int maxTurretsPerPlayer = 3;
        [SerializeField] private float plantingRadius = 6f;

        // Словарь: PlayerId -> List<TurretPlant>
        private readonly Dictionary<int, List<TurretPlant>> _playerTurrets = new();

        private void Awake()
        {
            if (Instance == null)
            {
                Instance = this;
            }
            else
            {
                Destroy(gameObject);
            }
        }

        public override void OnStartNetwork()
        {
            base.OnStartNetwork();
            _playerTurrets.Clear();
        }

        // ==================
        // TURRET TRACKING
        // ==================

        [Server]
        public void RegisterPlantedTurret(int playerId, TurretPlant turret)
        {
            if (!_playerTurrets.ContainsKey(playerId))
            {
                _playerTurrets[playerId] = new List<TurretPlant>();
            }

            _playerTurrets[playerId].Add(turret);
            Debug.Log($"[TurretPlantManager] Player {playerId} planted turret. Total: {_playerTurrets[playerId].Count}");
        }

        [Server]
        public void UnregisterPlantedTurret(int playerId, TurretPlant turret)
        {
            if (_playerTurrets.ContainsKey(playerId))
            {
                _playerTurrets[playerId].Remove(turret);
                Debug.Log($"[TurretPlantManager] Player {playerId} lost turret. Remaining: {_playerTurrets[playerId].Count}");
            }
        }

        public int GetTurretCount(int playerId)
        {
            if (_playerTurrets.TryGetValue(playerId, out var list))
            {
                list.RemoveAll(t => t == null);
                return list.Count;
            }
            return 0;
        }

        public bool CanPlantMore(int playerId)
        {
            return GetTurretCount(playerId) < maxTurretsPerPlayer;
        }

        // ==================
        // PLANTING ZONE
        // ==================

        public bool CanPlantAt(int playerId, Vector3 position)
        {
            if (!CanPlantMore(playerId))
            {
                Debug.Log($"[TurretPlantManager] Player {playerId} reached turret limit ({maxTurretsPerPlayer})");
                return false;
            }

            LifeFruit lifeFruit = FindLifeFruitForPlayer(playerId);
            if (lifeFruit == null)
            {
                Debug.Log($"[TurretPlantManager] Player {playerId} has no LifeFruit");
                return false;
            }

            float dist = Vector3.Distance(position, lifeFruit.transform.position);
            bool inRange = dist <= plantingRadius;

            if (!inRange)
            {
                Debug.Log($"[TurretPlantManager] Position too far from LifeFruit ({dist:F1}m > {plantingRadius}m)");
            }

            return inRange;
        }

        public LifeFruit FindLifeFruitForPlayer(int playerId)
        {
            var fruits = FindObjectsByType<LifeFruit>(FindObjectsSortMode.None);
            foreach (var fruit in fruits)
            {
                if (fruit.OwnerId == playerId)
                {
                    return fruit;
                }
            }
            return null;
        }

        public Vector3? GetLifeFruitPosition(int playerId)
        {
            var fruit = FindLifeFruitForPlayer(playerId);
            return fruit != null ? fruit.transform.position : null;
        }

        public float GetPlantingRadius() => plantingRadius;
        public int GetMaxTurretsPerPlayer() => maxTurretsPerPlayer;

        // ==================
        // CLEANUP
        // ==================

        [Server]
        public void CleanupPlayerTurrets(int playerId)
        {
            if (_playerTurrets.TryGetValue(playerId, out var turrets))
            {
                foreach (var turret in turrets)
                {
                    if (turret != null && turret.NetworkObject != null && turret.NetworkObject.IsSpawned)
                    {
                        ServerManager.Despawn(turret.NetworkObject);
                    }
                }
                _playerTurrets.Remove(playerId);
                Debug.Log($"[TurretPlantManager] Cleaned up all turrets for player {playerId}");
            }
        }

        public override void OnStopNetwork()
        {
            base.OnStopNetwork();
            _playerTurrets.Clear();
        }
    }
}
