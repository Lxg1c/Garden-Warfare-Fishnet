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
        
        private static readonly Dictionary<int, bool> _respawnAllowed;


        private void Awake()
        {
            Instance = this;
        }

        public override void OnStartNetwork()
        {
            base.OnStartNetwork();
            if (IsServerInitialized)
                _respawnAllowed.Clear();
        }

        public override void OnStopNetwork()
        {
            base.OnStopNetwork();
            if (IsServerInitialized)
                _respawnAllowed.Clear();
        }

        // ============================
        // Управление разрешением респауна
        // ============================

        public static void SetRespawnEnabled(int clientId, bool enabled)
        {
            _respawnAllowed[clientId] = enabled;
            Debug.Log($"[Server] RespawnManager: SetRespawnEnabled(Client {clientId}, {enabled})");
        }

        private static bool IsRespawnAllowed(int clientId)
        {
            if (!_respawnAllowed.ContainsKey(clientId))
                _respawnAllowed[clientId] = true;

            return _respawnAllowed[clientId];
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

            // ============================
            // 1. Отключаем CharacterController
            // ============================
            var playerInput = deadPlayer.GetComponent<PlayerInput>();
            if (playerInput != null)
                playerInput.enabled = false;


            // ============================
            // 2. Перемещаем игрока
            // ============================
            deadPlayer.transform.SetPositionAndRotation(respawnPoint.position, respawnPoint.rotation);

            // Сброс физики
            var rb = deadPlayer.GetComponent<Rigidbody>();
            if (rb != null)
            {
                rb.linearVelocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
            }


            // ============================
            // 3. Активируем объект
            // ============================
            deadPlayer.SetActive(true);


            // ============================
            // 4. Включаем CharacterController
            // ============================
            if (playerInput != null)
                playerInput.enabled = true;


            // ============================
            // 5. Восстанавливаем здоровье
            // ============================
            var health = deadPlayer.GetComponent<Health>();
            if (health != null)
                health.SetHealth(health.GetMaxHealth());


            Debug.Log($"[Server] Player {ownerId} respawned at {respawnPoint.name}");
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
    }
}
