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
        }

        [Server]
        public void UnregisterPlantedTurret(int playerId, TurretPlant turret)
        {
            if (_playerTurrets.ContainsKey(playerId))
            {
                _playerTurrets[playerId].Remove(turret);
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
            if (!CanPlantMore(playerId)) return false;

            LifeFruit lifeFruit = FindLifeFruitForPlayer(playerId);
            if (lifeFruit == null || !lifeFruit.IsAlive) return false;

            float dist = Vector3.Distance(position, lifeFruit.transform.position);
            return dist <= plantingRadius;
        }

        public LifeFruit FindLifeFruitForPlayer(int playerId)
        {
            var fruits = FindObjectsByType<LifeFruit>(FindObjectsSortMode.None);
            foreach (var fruit in fruits)
            {
                if (fruit.LogicalOwnerId == playerId)
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
            }
        }

        public override void OnStopNetwork()
        {
            base.OnStopNetwork();
            _playerTurrets.Clear();
        }
    }
}
