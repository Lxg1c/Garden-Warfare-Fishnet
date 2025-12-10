using AI.Neutral;
using Core.Components;
using FishNet.Object;
using System.Collections;
using System.Collections.Generic;
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
            Debug.Log("[RespawnManager] Awake - cleared static dictionaries");
        }

        public override void OnStartNetwork()
        {
            base.OnStartNetwork();
            if (IsServerInitialized)
            {
                // Очищаем ещё раз на сервере для надёжности
                RespawnAllowed.Clear();
                PlayerLifeFruits.Clear();
                Debug.Log("[RespawnManager] OnStartServer - dictionaries reset");
            }
        }

        // ============================
        // Управление разрешением респауна
        // ============================

        public static void SetRespawnEnabled(int clientId, bool enabled)
        {
            RespawnAllowed[clientId] = enabled;
            Debug.Log($"[Server] RespawnManager: SetRespawnEnabled(Client {clientId}, {enabled})");
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
            Debug.Log($"[RespawnManager] StartRespawn called for {deadObject?.name}");

            if (!IsServerInitialized)
            {
                Debug.LogWarning("[RespawnManager] Not server - ignoring");
                return;
            }

            if (!IsPlayer(deadObject))
            {
                Debug.Log($"[RespawnManager] {deadObject?.name} is not a player - ignoring");
                return;
            }

            var netObj = deadObject.GetComponent<NetworkObject>();
            if (netObj == null)
            {
                Debug.LogError("[RespawnManager] No NetworkObject!");
                return;
            }

            int ownerId = netObj.OwnerId;
            Debug.Log($"[RespawnManager] Player {ownerId} died. RespawnAllowed={IsRespawnAllowed(ownerId)}");

            if (!IsRespawnAllowed(ownerId))
            {
                Debug.Log($"Player {ownerId} cannot respawn — LifeFruit destroyed!");

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
                Debug.Log($"[Server] Player {ownerId} respawned at {respawnPos}");
            }
            else
            {
                Debug.LogError($"[RespawnManager] Player {ownerId} has no Health component!");
            }
        }

        /// <summary>
        /// Уведомляет конкретного игрока о том что он выбыл (LifeFruit уничтожен)
        /// </summary>
        [TargetRpc]
        private void TargetRpc_OnPlayerEliminated(FishNet.Connection.NetworkConnection conn)
        {
            Debug.Log("[Client] You have been eliminated! Your LifeFruit was destroyed.");

            // Здесь можно показать UI "Game Over" или подобное
            // Например: GameOverUI.Show("Your LifeFruit was destroyed!");
        }

        /// <summary>
        /// Обрабатывает выбывшего игрока на сервере
        /// </summary>
        private IEnumerator HandleEliminatedPlayer(GameObject deadPlayer, int playerId)
        {
            Debug.Log($"[Server] Player {playerId} eliminated - LifeFruit destroyed");

            yield return new WaitForSeconds(3f);

            // Игрок остается в игре как наблюдатель или выкидывается
            // Пока просто оставляем его "мертвым" - он видит игру но не может играть

            // Опционально: кикнуть игрока из матча
            // if (NetworkManager.ServerManager.Clients.TryGetValue(playerId, out var conn))
            // {
            //     conn.Kick(FishNet.Managing.Server.KickReason.Unset, "LifeFruit destroyed");
            // }
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
            var pi = player.GetComponent<PlayerInfo>();
            if (pi != null && pi.SpawnPosition != Vector3.zero)
            {
                return (pi.SpawnPosition, pi.SpawnRotation);
            }

            if (GameSpawnManager.Instance != null)
            {
                Transform spawnPoint = GameSpawnManager.Instance.GetPlayerSpawnPoint(ownerId);
                if (spawnPoint != null)
                    return (spawnPoint.position, spawnPoint.rotation);
            }

            return (transform.position, Quaternion.identity); // Fallback
        }

        public override void OnStopNetwork()
        {
            base.OnStopNetwork();
            RespawnAllowed.Clear();
            PlayerLifeFruits.Clear();
        }
    }
}