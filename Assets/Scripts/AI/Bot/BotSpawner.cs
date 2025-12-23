using System.Collections.Generic;
using Core.Components;
using FishNet.Object;
using Gameplay;
using UnityEngine;

namespace AI.Bot
{
    public class BotSpawner : NetworkBehaviour
    {
        public static BotSpawner Instance { get; private set; }

        [Header("Prefabs")]
        [SerializeField] private NetworkObject botPrefab;
        [SerializeField] private NetworkObject lifeFruitPrefab;

        [Header("Spawn Settings")]
        [SerializeField] private List<Transform> botSpawnPoints;
        [SerializeField] private int botsToSpawn = 1;
        [SerializeField] private float spawnDelay = 2f;

        [Header("Respawn Settings")]
        [SerializeField] private float respawnDelay = 3f;

        [Header("Bot IDs")]
        [SerializeField] private int botIdStart = 100; // ID ботов начинаются с 100

        private readonly List<BotPlayer> _spawnedBots = new();
        private readonly Dictionary<int, NetworkObject> _botLifeFruits = new();
        private readonly Dictionary<int, Vector3> _botSpawnPositions = new();
        private readonly Dictionary<int, Quaternion> _botSpawnRotations = new();

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

        public override void OnStartServer()
        {
            base.OnStartServer();

            // Спавним ботов с задержкой
            StartCoroutine(SpawnBotsDelayed());
        }

        private System.Collections.IEnumerator SpawnBotsDelayed()
        {
            yield return new WaitForSeconds(spawnDelay);

            for (int i = 0; i < botsToSpawn; i++)
            {
                SpawnBot(i);
                yield return new WaitForSeconds(0.5f);
            }
        }

        [Server]
        private void SpawnBot(int index)
        {
            int botId = botIdStart + index;

            if (botPrefab == null)
            {
                Debug.LogError("[BotSpawner] Bot prefab not assigned!");
                return;
            }

            // Получаем точку спавна
            Transform spawnPoint = GetBotSpawnPoint(index);
            Vector3 spawnPos = spawnPoint != null ? spawnPoint.position : transform.position;
            Quaternion spawnRot = spawnPoint != null ? spawnPoint.rotation : Quaternion.identity;

            // Сохраняем позицию спавна для респавна
            _botSpawnPositions[botId] = spawnPos;
            _botSpawnRotations[botId] = spawnRot;

            // Спавним бота
            NetworkObject botNetObj = Instantiate(botPrefab, spawnPos, spawnRot);
            ServerManager.Spawn(botNetObj);

            BotPlayer bot = botNetObj.GetComponent<BotPlayer>();
            if (bot != null)
            {
                bot.Initialize(botId, spawnPos);

                // Подписываемся на смерть бота
                var health = bot.GetComponent<Health>();
                if (health != null)
                {
                    health.OnDeath += () => OnBotDeath(botId, bot);
                }

                // Спавним LifeFruit для бота
                if (lifeFruitPrefab != null)
                {
                    Vector3 fruitPos = spawnPos + Vector3.back * 2f + Vector3.up * 0.5f;
                    NetworkObject fruitNetObj = Instantiate(lifeFruitPrefab, fruitPos, Quaternion.identity);
                    ServerManager.Spawn(fruitNetObj);

                    LifeFruit lifeFruit = fruitNetObj.GetComponent<LifeFruit>();
                    if (lifeFruit != null)
                    {
                        // Устанавливаем логического владельца для бота
                        lifeFruit.SetLogicalOwner(botId);
                        bot.SetLifeFruit(lifeFruit);
                    }

                    _botLifeFruits[botId] = fruitNetObj;
                }

                _spawnedBots.Add(bot);
            }
        }

        /// <summary>
        /// Обработчик смерти бота
        /// </summary>
        [Server]
        private void OnBotDeath(int botId, BotPlayer bot)
        {
            // Проверяем жив ли LifeFruit бота
            if (IsBotLifeFruitAlive(botId))
            {
                StartCoroutine(RespawnBotCoroutine(botId, bot));
            }
            else
            {
                _spawnedBots.Remove(bot);
            }
        }

        /// <summary>
        /// Проверяет жив ли LifeFruit бота
        /// </summary>
        private bool IsBotLifeFruitAlive(int botId)
        {
            if (!_botLifeFruits.TryGetValue(botId, out var fruitNetObj))
                return false;

            if (fruitNetObj == null || !fruitNetObj.IsSpawned)
                return false;

            var health = fruitNetObj.GetComponent<Health>();
            if (health != null && health.IsDead)
                return false;

            return true;
        }

        /// <summary>
        /// Корутина респавна бота
        /// </summary>
        private System.Collections.IEnumerator RespawnBotCoroutine(int botId, BotPlayer bot)
        {
            yield return new WaitForSeconds(respawnDelay);

            // Ещё раз проверяем LifeFruit (мог умереть за время ожидания)
            if (!IsBotLifeFruitAlive(botId))
            {
                _spawnedBots.Remove(bot);
                yield break;
            }

            // Получаем позицию респавна
            Vector3 respawnPos = _botSpawnPositions.GetValueOrDefault(botId, transform.position);
            Quaternion respawnRot = _botSpawnRotations.GetValueOrDefault(botId, Quaternion.identity);

            // Респавним бота через Health.Revive
            var health = bot.GetComponent<Health>();
            if (health != null)
            {
                health.Revive(respawnPos, respawnRot);
            }
        }

        private Transform GetBotSpawnPoint(int index)
        {
            if (botSpawnPoints == null || botSpawnPoints.Count == 0)
            {
                return transform;
            }
            return botSpawnPoints[index % botSpawnPoints.Count];
        }

        /// <summary>
        /// Деспавнит всех ботов
        /// </summary>
        [Server]
        public void DespawnAllBots()
        {
            foreach (var bot in _spawnedBots)
            {
                if (bot != null && bot.NetworkObject != null && bot.NetworkObject.IsSpawned)
                {
                    ServerManager.Despawn(bot.NetworkObject);
                }
            }
            _spawnedBots.Clear();

            foreach (var kvp in _botLifeFruits)
            {
                if (kvp.Value != null && kvp.Value.IsSpawned)
                {
                    ServerManager.Despawn(kvp.Value);
                }
            }
            _botLifeFruits.Clear();
        }

        /// <summary>
        /// Получает список всех живых ботов
        /// </summary>
        public List<BotPlayer> GetAliveBots()
        {
            _spawnedBots.RemoveAll(b => b == null);
            return _spawnedBots;
        }

        public override void OnStopServer()
        {
            base.OnStopServer();
            _spawnedBots.Clear();
            _botLifeFruits.Clear();
        }
    }
}
