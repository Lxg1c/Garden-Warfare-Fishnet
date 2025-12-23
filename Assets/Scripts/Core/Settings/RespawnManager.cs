using AI.Neutral;
using AI.Wave;
using Core.Components;
using FishNet.Object;
using Gameplay.TurretPlant;
using System.Collections;
using System.Collections.Generic;
using Core.Spawn;
using UnityEngine;
using Player.Components;
using UnityEngine.InputSystem;

namespace Core.Settings
{
    public class RespawnManager : NetworkBehaviour
    {
        public static RespawnManager Instance;

        [Header("Настройки возрождения")]
        public float respawnDelay = 3f;
        
        [Header("Life Fruit Prefab")]
        [SerializeField] private NetworkObject lifeFruitPrefab;
        
        // Инициализируем словарь
        private static readonly Dictionary<int, bool> RespawnAllowed = new Dictionary<int, bool>();
        private static readonly Dictionary<int, NetworkObject> PlayerLifeFruits = new Dictionary<int, NetworkObject>();

        private void Awake()
        {
            if (Instance == null)
            {
                Instance = this;
            }
            else
            {
                Destroy(gameObject);
                return;
            }

            // Очищаем static словари при старте (важно для редактора!)
            RespawnAllowed.Clear();
            PlayerLifeFruits.Clear();
        }

        public override void OnStartNetwork()
        {
            base.OnStartNetwork();
            if (IsServerInitialized)
            {
                // Очищаем ещё раз на сервере для надёжности
                RespawnAllowed.Clear();
                PlayerLifeFruits.Clear();
            }
        }

        // ============================
        // Управление разрешением респауна
        // ============================

        public static void SetRespawnEnabled(int clientId, bool enabled)
        {
            RespawnAllowed[clientId] = enabled;
        }

        public static bool IsRespawnAllowed(int clientId)
        {
            if (!RespawnAllowed.ContainsKey(clientId))
            {
                RespawnAllowed[clientId] = true; 
            }
            return RespawnAllowed[clientId];
        }

        // ============================
        // Создание LifeFruit для игрока
        // ============================

        [Server]
        public void CreateLifeFruitForPlayer(int playerId, Vector3 spawnPoint)
        {
            if (lifeFruitPrefab == null)
            {
                Debug.LogError("LifeFruit prefab not assigned in RespawnManager!");
                return;
            }

            if (PlayerLifeFruits.ContainsKey(playerId) && PlayerLifeFruits[playerId] != null)
            {
                ServerManager.Despawn(PlayerLifeFruits[playerId]);
                PlayerLifeFruits.Remove(playerId);
            }
    
            NetworkObject lifeFruit = Instantiate(lifeFruitPrefab, spawnPoint, Quaternion.identity);
            ServerManager.Spawn(lifeFruit, NetworkManager.ServerManager.Clients[playerId]);

            PlayerLifeFruits[playerId] = lifeFruit;
            SetRespawnEnabled(playerId, true);
        }

        
        [Server]
        public void DestroyLifeFruitForPlayer(int playerId)
        {
            if (PlayerLifeFruits.ContainsKey(playerId) && PlayerLifeFruits[playerId] != null)
            {
                ServerManager.Despawn(PlayerLifeFruits[playerId]);
                PlayerLifeFruits.Remove(playerId);
            }
            SetRespawnEnabled(playerId, false);
        }

        // ============================
        // Запуск возрождения
        // ============================

        public void StartRespawn(GameObject deadObject)
        {
            if (!IsServerInitialized) return;
            if (!IsPlayer(deadObject)) return;

            var netObj = deadObject.GetComponent<NetworkObject>();
            if (netObj == null) return;

            int ownerId = netObj.OwnerId;

            if (!IsRespawnAllowed(ownerId))
            {
                // Уведомляем игрока что он выбыл из игры
                if (NetworkManager.ServerManager.Clients.TryGetValue(ownerId, out var conn))
                {
                    TargetRpc_OnPlayerEliminated(conn);
                }

                StartCoroutine(HandleEliminatedPlayer(deadObject, ownerId));
                return;
            }

            StartCoroutine(RespawnCoroutine(deadObject, ownerId));
        }

        private IEnumerator RespawnCoroutine(GameObject deadPlayer, int ownerId)
        {
            yield return new WaitForSeconds(respawnDelay);

            if (deadPlayer == null)
                yield break;

            // Получаем позицию респавна (используем сохраненную позицию, а не Transform)
            var (respawnPos, respawnRot) = GetRespawnPositionForPlayer(deadPlayer, ownerId);

            // Используем Health.Revive() который синхронизирует респавн на всех клиентах
            var health = deadPlayer.GetComponent<Health>();
            if (health != null)
            {
                health.Revive(respawnPos, respawnRot);
            }
        }

        /// <summary>
        /// Уведомляет конкретного игрока о том что он выбыл (LifeFruit уничтожен)
        /// </summary>
        [TargetRpc]
        private void TargetRpc_OnPlayerEliminated(FishNet.Connection.NetworkConnection conn)
        {
            // Можно показать UI "Game Over"
        }

        /// <summary>
        /// Обрабатывает выбывшего игрока на сервере
        /// </summary>
        private IEnumerator HandleEliminatedPlayer(GameObject deadPlayer, int playerId)
        {
            // Очищаем все турели игрока
            TurretPlantManager.Instance?.CleanupPlayerTurrets(playerId);

            // Уведомляем WaveManager что игрок выбыл (это обновит условия победы)
            WaveManager.Instance?.OnPlayerEliminated(playerId);

            yield return new WaitForSeconds(3f);

            // Игрок остается в игре как наблюдатель или выкидывается
        }

        // ============================
        // Проверки
        // ============================

        private bool IsPlayer(GameObject obj)
        {
            if (obj == null)
                return false;

            if (obj.GetComponent<Neutral>() != null)
                return false; // Это AI

            if (obj.CompareTag("Player"))
                return true;

            if (obj.GetComponent<CharacterController>() != null)
                return true;

            return false;
        }

        // ============================
        // Получение точки спавна
        // ============================

        private Transform GetRespawnPointForPlayer(GameObject player, int ownerId)
        {
            var pi = player.GetComponent<PlayerInfo>();
            if (pi != null && pi.SpawnPoint != null)
                return pi.SpawnPoint;

            if (GameSpawnManager.Instance != null)
                return GameSpawnManager.Instance.GetPlayerSpawnPoint(ownerId);

            return transform; // Fallback
        }

        /// <summary>
        /// Получает позицию и поворот для респавна игрока
        /// </summary>
        private (Vector3 position, Quaternion rotation) GetRespawnPositionForPlayer(GameObject player, int ownerId)
        {
            // 1. Сначала пробуем PlayerInfo (синхронизированная позиция)
            var pi = player.GetComponent<PlayerInfo>();
            if (pi != null && pi.SpawnPosition != Vector3.zero)
            {
                return (pi.SpawnPosition, pi.SpawnRotation);
            }

            // 2. Пробуем GameSpawnManager (если PlayerInfo не настроен)
            if (GameSpawnManager.Instance != null)
            {
                Transform spawnPoint = GameSpawnManager.Instance.GetPlayerSpawnPoint(ownerId);
                if (spawnPoint != null)
                {
                    return (spawnPoint.position, spawnPoint.rotation);
                }
            }

            // 3. Fallback
            return (transform.position, Quaternion.identity);
        }

        public override void OnStopNetwork()
        {
            base.OnStopNetwork();
            RespawnAllowed.Clear();
            PlayerLifeFruits.Clear();
        }
    }
}