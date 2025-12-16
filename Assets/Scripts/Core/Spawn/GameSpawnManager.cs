using FishNet.Connection;
using FishNet.Object;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Player.Components;

namespace Core.Spawn
{
    public class GameSpawnManager : NetworkBehaviour
    {
        public static GameSpawnManager Instance;

        [Header("Prefabs")]
        [SerializeField] private NetworkObject playerPrefab;
        [SerializeField] private NetworkObject lifeFruitPrefab;

        [Header("Spawn Points")]
        [SerializeField] private List<Transform> playerSpawnPoints;
        [SerializeField] private List<Transform> lifeFruitSpawnPoints;

        // Отслеживаем заспавненных игроков
        private HashSet<int> _spawnedPlayers = new();

        private void Awake()
        {
            Instance = this;
        }

        public override void OnStartServer()
        {
            base.OnStartServer();

            _spawnedPlayers.Clear();

            Debug.Log("[GameSpawnManager] Server started - spawning all connected players");

            // Подписываемся на загрузку сцены для новых клиентов
            NetworkManager.SceneManager.OnClientLoadedStartScenes += OnClientLoadedScene;

            // Спавним всех уже подключенных игроков
            foreach (NetworkConnection conn in ServerManager.Clients.Values)
            {
                // Небольшая задержка чтобы сцена успела загрузиться
                StartCoroutine(SpawnPlayerDelayed(conn, 0.5f));
            }
        }

        private System.Collections.IEnumerator SpawnPlayerDelayed(NetworkConnection conn, float delay)
        {
            yield return new WaitForSeconds(delay);

            if (conn != null && conn.IsActive)
            {
                SpawnPlayerInternal(conn);
            }
        }

        public override void OnStopServer()
        {
            base.OnStopServer();
            if (NetworkManager != null && NetworkManager.SceneManager != null)
            {
                NetworkManager.SceneManager.OnClientLoadedStartScenes -= OnClientLoadedScene;
            }
            _spawnedPlayers.Clear();
        }

        /// <summary>
        /// Спавнит игрока для указанного подключения.
        /// Вызывается из LobbyManager когда игра начинается.
        /// </summary>
        [Server]
        public void SpawnPlayerForConnection(NetworkConnection conn)
        {
            if (conn == null)
            {
                Debug.LogError("[GameSpawnManager] Cannot spawn - connection is null");
                return;
            }

            int playerId = conn.ClientId;

            // Проверяем не заспавнен ли уже
            if (_spawnedPlayers.Contains(playerId))
            {
                Debug.LogWarning($"[GameSpawnManager] Player {playerId} already spawned!");
                return;
            }

            SpawnPlayerInternal(conn);
        }

        /// <summary>
        /// Вызывается когда клиент загрузил сцену
        /// </summary>
        private void OnClientLoadedScene(NetworkConnection conn, bool asServer)
        {
            if (!asServer) return;

            // Спавним игрока если он ещё не заспавнен
            if (!_spawnedPlayers.Contains(conn.ClientId))
            {
                SpawnPlayerInternal(conn);
            }
        }

        [Server]
        private void SpawnPlayerInternal(NetworkConnection conn)
        {
            int playerId = conn.ClientId;

            Debug.Log($"[GameSpawnManager] Spawning player {playerId}...");

            if (playerPrefab == null)
            {
                Debug.LogError("[GameSpawnManager] Player prefab not assigned!");
                return;
            }

            Transform pSpawn = GetPlayerSpawnPoint(playerId);
            Transform fSpawn = GetFruitSpawnPoint(playerId);

            // --- Спавн игрока ---
            NetworkObject playerObj = Instantiate(playerPrefab, pSpawn.position, pSpawn.rotation);
            ServerManager.Spawn(playerObj, conn);
            Debug.Log($"[GameSpawnManager] Player {playerId} spawned at {pSpawn.position}");

            // --- Настройка PlayerInfo ---
            PlayerInfo info = playerObj.GetComponent<PlayerInfo>();
            if (info != null)
            {
                info.SetActorNumber(playerId);
                info.SetSpawnPoint(pSpawn);
            }

            // --- Спавн LifeFruit ---
            if (lifeFruitPrefab != null && fSpawn != null)
            {
                NetworkObject fruitObj = Instantiate(lifeFruitPrefab, fSpawn.position, fSpawn.rotation);
                ServerManager.Spawn(fruitObj, conn);
                Debug.Log($"[GameSpawnManager] LifeFruit for player {playerId} spawned at {fSpawn.position}");
            }

            // Отмечаем что игрок заспавнен
            _spawnedPlayers.Add(playerId);
        }

        /// <summary>
        /// Удаляет все объекты игрока (для рестарта игры)
        /// </summary>
        [Server]
        public void DespawnPlayer(int playerId)
        {
            _spawnedPlayers.Remove(playerId);

            if (!NetworkManager.ServerManager.Clients.TryGetValue(playerId, out NetworkConnection conn))
            {
                return;
            }

            // Деспавним все объекты игрока
            foreach (var nob in conn.Objects.ToArray())
            {
                if (nob != null && nob.IsSpawned)
                {
                    ServerManager.Despawn(nob);
                }
            }

            Debug.Log($"[GameSpawnManager] Player {playerId} despawned");
        }

        /// <summary>
        /// Деспавнит всех игроков (для возврата в лобби)
        /// </summary>
        [Server]
        public void DespawnAllPlayers()
        {
            foreach (int playerId in _spawnedPlayers.ToArray())
            {
                DespawnPlayer(playerId);
            }
            _spawnedPlayers.Clear();
        }

        public Transform GetPlayerSpawnPoint(int id)
        {
            if (playerSpawnPoints == null || playerSpawnPoints.Count == 0)
            {
                Debug.LogWarning("[GameSpawnManager] No spawn points! Using default position");
                return transform;
            }
            return playerSpawnPoints[id % playerSpawnPoints.Count];
        }

        private Transform GetFruitSpawnPoint(int id)
        {
            if (lifeFruitSpawnPoints == null || lifeFruitSpawnPoints.Count == 0) return null;
            return lifeFruitSpawnPoints[id % lifeFruitSpawnPoints.Count];
        }

        /// <summary>
        /// Проверяет заспавнен ли игрок
        /// </summary>
        public bool IsPlayerSpawned(int playerId)
        {
            return _spawnedPlayers.Contains(playerId);
        }
    }
}