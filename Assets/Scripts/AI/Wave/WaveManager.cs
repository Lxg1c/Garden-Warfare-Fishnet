using System.Collections.Generic;
using Core.Settings;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using Gameplay;
using Gameplay.TurretPlant;
using UI.HUD.GameTimer;
using UnityEngine;

namespace AI.Wave
{
    public class WaveManager : NetworkBehaviour
    {
        public static WaveManager Instance { get; private set; }

        [Header("Wave Settings")]
        [SerializeField] private float firstWaveTime = 60f; // Первая волна через 60 сек
        [SerializeField] private float waveInterval = 45f; // Интервал между волнами
        [SerializeField] private int baseEnemiesPerWave = 3; // Базовое кол-во врагов
        [SerializeField] private int enemiesPerWaveIncrease = 2; // Увеличение врагов с каждой волной

        [Header("Difficulty Scaling")]
        [SerializeField] private float healthScalePerWave = 0.15f; // +15% HP за волну
        [SerializeField] private float damageScalePerWave = 0.1f; // +10% урона за волну
        [SerializeField] private float speedScalePerWave = 0.05f; // +5% скорости за волну

        [Header("Prefabs")]
        [SerializeField] private NetworkObject waveEnemyPrefab;

        [Header("Spawn Settings")]
        [SerializeField] private float spawnRadius = 15f; // Радиус спавна от LifeFruit
        [SerializeField] private float spawnDelay = 0.5f; // Задержка между спавном врагов

        // ==================
        // Синхронизация
        // ==================

        private readonly SyncVar<int> _currentWave = new(0);
        private readonly SyncVar<bool> _waveActive = new(false);

        // ==================
        // Локальные данные
        // ==================

        private float _nextWaveTime;
        private bool _initialized;

        // Враги для каждого игрока
        private readonly Dictionary<int, List<WaveEnemy>> _playerWaveEnemies = new();

        // Живые игроки (не выбыли)
        private readonly List<int> _alivePlayers = new();

        // Выбывшие игроки (умерли без LifeFruit)
        private readonly HashSet<int> _eliminatedPlayers = new();

        // Игроки у которых есть LifeFruit (для спавна волн)
        private readonly List<int> _playersWithLifeFruit = new();

        // Флаг завершения игры
        private bool _gameOver;

        // ==================
        // Properties
        // ==================

        public int CurrentWave => _currentWave.Value;
        public bool WaveActive => _waveActive.Value;
        public float NextWaveTime => _nextWaveTime;

        // ==================
        // Lifecycle
        // ==================

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
                _nextWaveTime = firstWaveTime;
                _playerWaveEnemies.Clear();
                _alivePlayers.Clear();
                _eliminatedPlayers.Clear();
                _playersWithLifeFruit.Clear();
                _gameOver = false;

                // Подписываемся на таймер
                GameTimer.OnTimeChanged += OnGameTimeChanged;
            }
        }

        public override void OnStopNetwork()
        {
            base.OnStopNetwork();

            if (IsServerInitialized)
            {
                GameTimer.OnTimeChanged -= OnGameTimeChanged;
            }

            _playerWaveEnemies.Clear();
            _alivePlayers.Clear();
            _eliminatedPlayers.Clear();
            _playersWithLifeFruit.Clear();
        }

        // ==================
        // WAVE LOGIC
        // ==================

        private void OnGameTimeChanged(float currentTime)
        {
            if (!IsServerInitialized) return;
            if (_gameOver) return;

            // Обновляем списки игроков
            UpdatePlayerLists();

            // Проверяем условие победы
            CheckWinCondition();

            // Проверяем время для следующей волны
            if (currentTime >= _nextWaveTime && !_waveActive.Value)
            {
                StartNextWave();
            }
        }

        [Server]
        private void UpdatePlayerLists()
        {
            _playersWithLifeFruit.Clear();

            // Находим всех игроков/ботов с ЖИВЫМ LifeFruit
            var lifeFruits = FindObjectsByType<LifeFruit>(FindObjectsSortMode.None);
            foreach (var fruit in lifeFruits)
            {
                if (fruit != null && fruit.NetworkObject != null && fruit.NetworkObject.IsSpawned)
                {
                    // Только ЖИВЫЕ посаженные LifeFruit считаются (не мёртвые и не те что несут)
                    if (fruit.IsAlive)
                    {
                        int ownerId = fruit.LogicalOwnerId;
                        if (!_playersWithLifeFruit.Contains(ownerId))
                        {
                            _playersWithLifeFruit.Add(ownerId);
                        }
                    }
                }
            }

            // Обновляем список живых игроков (все подключенные минус выбывшие)
            _alivePlayers.Clear();
            foreach (var client in NetworkManager.ServerManager.Clients.Values)
            {
                if (client.FirstObject != null)
                {
                    int playerId = client.FirstObject.OwnerId;
                    if (!_eliminatedPlayers.Contains(playerId))
                    {
                        _alivePlayers.Add(playerId);
                    }
                }
            }
        }

        [Server]
        private void CheckWinCondition()
        {
            // Если остался только один живой игрок - он победил
            if (_alivePlayers.Count == 1 && _currentWave.Value > 0)
            {
                int winnerId = _alivePlayers[0];
                OnPlayerWon(winnerId);
            }
            else if (_alivePlayers.Count == 0 && _currentWave.Value > 0)
            {
                OnGameDraw();
            }
        }

        /// <summary>
        /// Вызывается когда игрок умирает без LifeFruit - он выбывает
        /// </summary>
        [Server]
        public void OnPlayerEliminated(int playerId)
        {
            if (_eliminatedPlayers.Contains(playerId)) return;

            _eliminatedPlayers.Add(playerId);

            // Очищаем врагов этого игрока
            CleanupEnemiesForPlayer(playerId);

            // Уведомляем клиентов
            NotifyPlayerEliminatedObserversRpc(playerId);
        }

        [ObserversRpc]
        private void NotifyPlayerEliminatedObserversRpc(int playerId)
        {
            // Можно показать UI уведомление
        }

        [Server]
        private void StartNextWave()
        {
            _currentWave.Value++;
            _waveActive.Value = true;

            int enemyCount = baseEnemiesPerWave + (enemiesPerWaveIncrease * (_currentWave.Value - 1));

            // Уведомляем всех клиентов о начале волны
            NotifyWaveStartObserversRpc(_currentWave.Value, enemyCount);

            // Спавним врагов только для игроков у которых есть посаженный LifeFruit
            foreach (int playerId in _playersWithLifeFruit)
            {
                // Не спавним для выбывших
                if (_eliminatedPlayers.Contains(playerId)) continue;

                SpawnWaveEnemiesForPlayer(playerId, enemyCount);
            }
        }

        [Server]
        private void SpawnWaveEnemiesForPlayer(int playerId, int count)
        {
            LifeFruit lifeFruit = TurretPlantManager.Instance?.FindLifeFruitForPlayer(playerId);
            if (lifeFruit == null) return;

            if (!_playerWaveEnemies.ContainsKey(playerId))
            {
                _playerWaveEnemies[playerId] = new List<WaveEnemy>();
            }

            StartCoroutine(SpawnEnemiesCoroutine(playerId, lifeFruit.transform, count));
        }

        private System.Collections.IEnumerator SpawnEnemiesCoroutine(int playerId, Transform lifeFruitTransform, int count)
        {
            for (int i = 0; i < count; i++)
            {
                if (lifeFruitTransform == null) yield break;

                SpawnSingleEnemy(playerId, lifeFruitTransform);
                yield return new WaitForSeconds(spawnDelay);
            }
        }

        [Server]
        private void SpawnSingleEnemy(int playerId, Transform lifeFruitTransform)
        {
            if (waveEnemyPrefab == null) return;

            // Рассчитываем позицию спавна (случайная точка на окружности вокруг LifeFruit)
            Vector3? spawnPos = GetRandomSpawnPosition(lifeFruitTransform.position);

            if (!spawnPos.HasValue) return;

            // Спавним врага на валидной позиции NavMesh
            NetworkObject enemyNetObj = Instantiate(waveEnemyPrefab, spawnPos.Value, Quaternion.identity);
            ServerManager.Spawn(enemyNetObj);

            WaveEnemy enemy = enemyNetObj.GetComponent<WaveEnemy>();
            if (enemy != null)
            {
                // Инициализируем врага (после небольшой задержки чтобы NavMeshAgent успел активироваться)
                StartCoroutine(InitializeEnemyDelayed(enemy, playerId, lifeFruitTransform));
            }
        }

        private System.Collections.IEnumerator InitializeEnemyDelayed(WaveEnemy enemy, int playerId, Transform lifeFruitTransform)
        {
            // Ждём несколько кадров чтобы NavMeshAgent успел активироваться
            yield return null;
            yield return null;

            if (enemy == null) yield break;
            if (lifeFruitTransform == null) yield break;

            // Инициализируем врага
            enemy.Initialize(playerId, lifeFruitTransform, this);

            // Применяем модификаторы сложности
            float healthMult = 1f + (healthScalePerWave * (_currentWave.Value - 1));
            float damageMult = 1f + (damageScalePerWave * (_currentWave.Value - 1));
            float speedMult = 1f + (speedScalePerWave * (_currentWave.Value - 1));
            enemy.ApplyWaveModifiers(healthMult, damageMult, speedMult);

            // Добавляем в список
            if (!_playerWaveEnemies.ContainsKey(playerId))
            {
                _playerWaveEnemies[playerId] = new List<WaveEnemy>();
            }
            _playerWaveEnemies[playerId].Add(enemy);
        }

        private Vector3? GetRandomSpawnPosition(Vector3 center)
        {
            // Пробуем несколько раз найти валидную позицию
            for (int attempt = 0; attempt < 10; attempt++)
            {
                // Случайный угол
                float angle = Random.Range(0f, 360f) * Mathf.Deg2Rad;

                // Позиция на окружности (варьируем радиус)
                float radius = spawnRadius * Random.Range(0.8f, 1.2f);
                Vector3 offset = new Vector3(
                    Mathf.Cos(angle) * radius,
                    0f,
                    Mathf.Sin(angle) * radius
                );

                Vector3 spawnPos = center + offset;

                // Проверяем что позиция на NavMesh (увеличенный радиус поиска)
                if (UnityEngine.AI.NavMesh.SamplePosition(spawnPos, out var hit, 10f, UnityEngine.AI.NavMesh.AllAreas))
                {
                    return hit.position;
                }
            }

            // Если не нашли на окружности, пробуем рядом с центром
            if (UnityEngine.AI.NavMesh.SamplePosition(center, out var centerHit, 20f, UnityEngine.AI.NavMesh.AllAreas))
            {
                return centerHit.position;
            }

            return null;
        }

        // ==================
        // ENEMY CALLBACKS
        // ==================

        [Server]
        public void OnEnemyDied(WaveEnemy enemy)
        {
            // Удаляем врага из списка
            foreach (var kvp in _playerWaveEnemies)
            {
                kvp.Value.Remove(enemy);
            }

            // Проверяем закончилась ли волна
            CheckWaveEnd();
        }

        [Server]
        private void CheckWaveEnd()
        {
            int totalEnemies = 0;
            foreach (var kvp in _playerWaveEnemies)
            {
                kvp.Value.RemoveAll(e => e == null || e.IsDead);
                totalEnemies += kvp.Value.Count;
            }

            if (totalEnemies == 0 && _waveActive.Value)
            {
                _waveActive.Value = false;
                _nextWaveTime = GameTimer.Instance.CurrentTime + waveInterval;
                // Уведомляем клиентов
                NotifyWaveEndObserversRpc(_currentWave.Value);
            }
        }

        // ==================
        // WIN CONDITIONS
        // ==================

        [Server]
        private void OnPlayerWon(int playerId)
        {
            if (_gameOver) return;
            _gameOver = true;

            // Уведомляем всех о победителе
            NotifyGameOverObserversRpc(playerId, false);
        }

        [Server]
        private void OnGameDraw()
        {
            if (_gameOver) return;
            _gameOver = true;

            NotifyGameOverObserversRpc(-1, true);
        }

        // ==================
        // RPCs
        // ==================

        [ObserversRpc]
        private void NotifyWaveStartObserversRpc(int waveNumber, int enemyCount)
        {
            // Можно показать UI уведомление о начале волны
        }

        [ObserversRpc]
        private void NotifyWaveEndObserversRpc(int waveNumber)
        {
            // Можно показать UI уведомление о завершении волны
        }

        [ObserversRpc]
        private void NotifyGameOverObserversRpc(int winnerId, bool isDraw)
        {
            // Можно показать UI Game Over
        }

        // ==================
        // CLEANUP
        // ==================

        [Server]
        public void CleanupEnemiesForPlayer(int playerId)
        {
            if (_playerWaveEnemies.TryGetValue(playerId, out var enemies))
            {
                foreach (var enemy in enemies)
                {
                    if (enemy != null && enemy.NetworkObject != null && enemy.NetworkObject.IsSpawned)
                    {
                        ServerManager.Despawn(enemy.NetworkObject);
                    }
                }
                _playerWaveEnemies.Remove(playerId);
            }
        }

        // ==================
        // GIZMOS
        // ==================

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.magenta;

            // Показываем радиус спавна для всех LifeFruit
            var lifeFruits = FindObjectsByType<LifeFruit>(FindObjectsSortMode.None);
            foreach (var fruit in lifeFruits)
            {
                if (fruit != null)
                {
                    Gizmos.DrawWireSphere(fruit.transform.position, spawnRadius);
                }
            }
        }
    }
}
