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
            }
        }

        public override void OnStartNetwork()
        {
            base.OnStartNetwork();
            if (IsServerInitialized)
            {
                Debug.Log("RespawnManager started on server");
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
            if (!IsServerInitialized)
                return;

            if (!IsPlayer(deadObject))
                return;

            var netObj = deadObject.GetComponent<NetworkObject>();
            if (netObj == null)
                return;

            int ownerId = netObj.OwnerId;

            if (!IsRespawnAllowed(ownerId))
            {
                Debug.Log($"Player {ownerId} cannot respawn — respawn restricted.");
                StartCoroutine(KickPlayerCoroutine(ownerId));
                return;
            }

            StartCoroutine(RespawnCoroutine(deadObject, ownerId));
        }

        private IEnumerator RespawnCoroutine(GameObject deadPlayer, int ownerId)
        {
            yield return new WaitForSeconds(respawnDelay);

            if (deadPlayer == null)
                yield break;

            Transform respawnPoint = GetRespawnPointForPlayer(deadPlayer, ownerId);
            if (respawnPoint == null)
            {
                Debug.LogError("[RespawnManager] Respawn point not found!");
                yield break;
            }

            var playerInput = deadPlayer.GetComponent<PlayerInput>();
            if (playerInput != null)
                playerInput.enabled = false;
            
            deadPlayer.transform.SetPositionAndRotation(respawnPoint.position, respawnPoint.rotation);
            
            var rb = deadPlayer.GetComponent<Rigidbody>();
            if (rb != null)
            {
                rb.linearVelocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
            }
            
            deadPlayer.SetActive(true);
            
            if (playerInput != null)
                playerInput.enabled = true;
            
            var health = deadPlayer.GetComponent<Health>();
            if (health != null)
                health.SetHealth(health.GetMaxHealth());

            Debug.Log($"[Server] Player {ownerId} respawned at {respawnPoint.name}");
        }

        private IEnumerator KickPlayerCoroutine(int playerId)
        {
            Debug.Log($"[Server] Kicking player {playerId} from match");
            yield return new WaitForSeconds(2f);
            
            // Здесь логика выкидывания игрока из матча
            // Например: NetworkManager.ServerManager.Kick(client, reason);
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

        public override void OnStopNetwork()
        {
            base.OnStopNetwork();
            RespawnAllowed.Clear();
            PlayerLifeFruits.Clear();
        }
    }
}