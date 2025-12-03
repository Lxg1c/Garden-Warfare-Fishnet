using System;
using System.Collections.Generic;
using Core.Components;
using FishNet.Object;
using UnityEngine;
using UI.HUD.GameTimer;

namespace AI.Camp
{
    public class Camp : NetworkBehaviour
    {
        [Header("Camp Settings")] 
        [SerializeField] private string campName = "Camp_01";
        [SerializeField] private GameObject neutralCampPrefab;
        
        [SerializeField] private float initialSpawnTime = 60f;
        [SerializeField] private float respawnInterval = 60f;
        
        private Transform _spawnPoint;
        private bool _isCampClean;
        private bool _isRespawning;
        private float _respawnTimer;
        
        private bool _hasSpawnedInitial;
        private float _nextRespawnTime;
        
        private List<Neutral.Neutral> _neutralUnits = new List<Neutral.Neutral>();

        public event Action<List<Neutral.Neutral>> OnCampReady;

        public string CampName => campName;
        public bool IsCampClean => _isCampClean;

        private void Awake()
        {
            _spawnPoint = transform;
        }

        public override void OnStartServer()
        {
            base.OnStartServer();
            Debug.Log($"Camp '{campName}' initialized with {_neutralUnits.Count} units");
        }

        private void Update()
        {
            if (!IsServerInitialized) return;
            
            if (_isCampClean && !_isRespawning)
            {
                _respawnTimer -= Time.deltaTime;
                if (_respawnTimer <= 0f)
                {
                    SpawnCamp();
                }
            }
        }

        public List<Neutral.Neutral> GetNeutralUnits()
        {
            return _neutralUnits;
        }
        
        /// <summary>
        /// Спавн кемпа
        /// </summary>
        private void SpawnCamp()
        {
            if (_spawnPoint == null) return;
            
            SpawnCampAtPosition(_spawnPoint.position);
            
        }
        
        private void OnGameTimeChanged(float time)
        {
            if (!IsServerInitialized) return;
            
            if (!_hasSpawnedInitial && time >= initialSpawnTime)
            {
                SpawnCamp();
                _hasSpawnedInitial = true;
                _nextRespawnTime = time + respawnInterval;
            }
            
            if (_hasSpawnedInitial && time >= _nextRespawnTime)
            {
                _nextRespawnTime = time + respawnInterval;
            }
        }
        
        /// <summary>
        /// Спавн кемпа в указанной позиции
        /// </summary>
        private void SpawnCampAtPosition(Vector3 position)
        {
            if (neutralCampPrefab == null)
            {
                Debug.LogError("Neutral prefab is not assigned!");
                return;
            }

            GameObject campGo = Instantiate(neutralCampPrefab, position, Quaternion.identity, transform);

            NetworkObject netObj = campGo.GetComponent<NetworkObject>();
            if (netObj == null)
            {
                Debug.LogError("Neutral prefab doesn't have NetworkObject!");
                Destroy(campGo);
                return;
            }
    
            Spawn(netObj);
            
            GetNeutralsInCamp(campGo);
        }


        /// <summary>
        /// Собрать всех нейтралов в этом кемпе
        /// </summary>
        private void GetNeutralsInCamp(GameObject campInstance)
        {
            _neutralUnits.Clear();

            Neutral.Neutral[] neutrals = campInstance.GetComponentsInChildren<Neutral.Neutral>();

            foreach (var neutral in neutrals)
                RegisterNeutral(neutral);

            Debug.Log($"Camp '{campName}': FOUND {_neutralUnits.Count} neutrals");
            
            OnCampReady?.Invoke(_neutralUnits);
        }


        private void RegisterNeutral(Neutral.Neutral neutral)
        {
            if (neutral == null || _neutralUnits.Contains(neutral)) return;
            
            _neutralUnits.Add(neutral);
            
            Debug.Log($"Registered neutral '{neutral.name}' to camp '{campName}'");
        }

        private void UnRegisterNeutral(Neutral.Neutral neutral)
        {
            if (neutral == null) return;
            
            _neutralUnits.Remove(neutral);
            CheckIfCampCleared();
        }
        
        /// <summary>
        /// Проверить, очищен ли кемп
        /// </summary>
        private void CheckIfCampCleared()
        {
            if (_isCampClean) return;
            
            int aliveCount = 0;
            foreach (var neutral in _neutralUnits)
            {
                if (neutral != null && neutral.gameObject.activeSelf)
                {
                    Health health = neutral.GetComponent<Health>();
                    if (health != null && health.GetHealth() > 0)
                    {
                        aliveCount++;
                    }
                }
            }
            
            if (aliveCount == 0)
            {
                _isCampClean = true;
               Debug.Log($"Camp '{campName}' cleared!");
            }
        }
        
        /// <summary>
        /// Респавн одного нейтрала
        /// </summary>
        [Server]
        private void RespawnNeutral(Neutral.Neutral neutral)
        {
            if (neutral == null) return;
            
            // Восстанавливаем здоровье
            Health health = neutral.GetComponent<Health>();
            if (health != null)
            {
                health.SetHealth(health.GetMaxHealth());
            }
            
            // Активируем объект
            neutral.gameObject.SetActive(true);
            
            // Сбрасываем состояние
            NetworkObject netObj = neutral.GetComponent<NetworkObject>();
            if (netObj != null && !netObj.IsSpawned)
            {
                Spawn(netObj);
            }
            
            // Возвращаем на стартовую позицию
            neutral.transform.position = transform.position;
            neutral.SetState(Neutral.AiState.Idle);
            
            Debug.Log($"Camp '{campName}': Respawned {neutral.name}");
        }
        
        private void OnEnable()
        {
            GameTimer.OnTimeChanged += OnGameTimeChanged;
        }

        private void OnDisable()
        {
            GameTimer.OnTimeChanged -= OnGameTimeChanged;
        }
    }
}