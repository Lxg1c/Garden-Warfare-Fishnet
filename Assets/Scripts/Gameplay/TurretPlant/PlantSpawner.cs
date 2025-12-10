using FishNet.Object;
using UI.HUD.GameTimer;
using UnityEngine;

namespace Gameplay.TurretPlant
{
    /// <summary>
    /// Точка спавна растения. Когда растение забирают - оно респавнится через время.
    /// Работает аналогично Camp для нейтралов.
    /// </summary>
    public class PlantSpawner : NetworkBehaviour
    {
        [Header("Settings")]
        [SerializeField] private string spawnerName = "PlantSpawner_01";
        [SerializeField] private GameObject plantPrefab;

        [Header("Spawn Settings")]
        [SerializeField] private float initialSpawnTime = 10f;
        [SerializeField] private float respawnDelay = 60f;

        [Header("Visual")]
        [SerializeField] private GameObject spawnPointVisual; // Опциональный визуал точки спавна

        private TurretPlant _currentPlant;
        private bool _spawnedInitial;
        private bool _hasPlant;
        private float _nextRespawnTime;

        public override void OnStartServer()
        {
            base.OnStartServer();
            GameTimer.OnTimeChanged += HandleTime;
            Debug.Log($"[PlantSpawner {spawnerName}] Initialized, waiting for time {initialSpawnTime}");
        }

        public override void OnStopServer()
        {
            base.OnStopServer();
            GameTimer.OnTimeChanged -= HandleTime;
        }

        private void HandleTime(float time)
        {
            if (!IsServerInitialized) return;

            // Первичный спавн
            if (!_spawnedInitial && time >= initialSpawnTime)
            {
                Debug.Log($"[PlantSpawner {spawnerName}] Initial spawn at time {time}");
                SpawnPlant();
                _spawnedInitial = true;
            }

            // Респавн после того как растение забрали
            if (_spawnedInitial && !_hasPlant && time >= _nextRespawnTime)
            {
                Debug.Log($"[PlantSpawner {spawnerName}] Respawn at time {time}");
                SpawnPlant();
            }
        }

        [Server]
        private void SpawnPlant()
        {
            if (plantPrefab == null)
            {
                Debug.LogError($"[PlantSpawner {spawnerName}] No plant prefab assigned!");
                return;
            }

            GameObject plantObj = Instantiate(plantPrefab, transform.position, transform.rotation);
            ServerManager.Spawn(plantObj);

            _currentPlant = plantObj.GetComponent<TurretPlant>();
            if (_currentPlant != null)
            {
                _currentPlant.SetSpawner(this);
                _hasPlant = true;

                Debug.Log($"[PlantSpawner {spawnerName}] Plant spawned successfully");
            }
            else
            {
                Debug.LogError($"[PlantSpawner {spawnerName}] Spawned object has no TurretPlant component!");
            }

            // Скрываем визуал точки спавна
            if (spawnPointVisual != null)
            {
                spawnPointVisual.SetActive(false);
            }
        }

        /// <summary>
        /// Вызывается когда растение забирают
        /// </summary>
        [Server]
        public void OnPlantPickedUp()
        {
            Debug.Log($"[PlantSpawner {spawnerName}] Plant picked up, scheduling respawn");

            _currentPlant = null;
            _hasPlant = false;

            // Устанавливаем время следующего респавна
            if (GameTimer.Instance != null)
            {
                _nextRespawnTime = GameTimer.Instance.CurrentTime + respawnDelay;
                Debug.Log($"[PlantSpawner {spawnerName}] Next respawn at {_nextRespawnTime} (in {respawnDelay}s)");
            }

            // Показываем визуал точки спавна (опционально - показать что тут будет растение)
            if (spawnPointVisual != null)
            {
                spawnPointVisual.SetActive(true);
            }
        }

        /// <summary>
        /// Вызывается если растение уничтожили (не забрали, а убили)
        /// </summary>
        [Server]
        public void OnPlantDestroyed()
        {
            Debug.Log($"[PlantSpawner {spawnerName}] Plant destroyed, scheduling respawn");

            _currentPlant = null;
            _hasPlant = false;

            if (GameTimer.Instance != null)
            {
                _nextRespawnTime = GameTimer.Instance.CurrentTime + respawnDelay;
            }

            if (spawnPointVisual != null)
            {
                spawnPointVisual.SetActive(true);
            }
        }

        // ==================
        // GIZMOS
        // ==================

        private void OnDrawGizmos()
        {
            Gizmos.color = _hasPlant ? Color.green : Color.yellow;
            Gizmos.DrawWireSphere(transform.position, 0.5f);

            // Иконка
            Gizmos.DrawIcon(transform.position + Vector3.up, "sv_icon_dot3_pix16_gizmo", true);
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.green;
            Gizmos.DrawWireSphere(transform.position, 1f);

#if UNITY_EDITOR
            UnityEditor.Handles.Label(transform.position + Vector3.up * 2, spawnerName);
#endif
        }
    }
}
