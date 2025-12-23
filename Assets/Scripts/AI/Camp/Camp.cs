using System;
using System.Collections.Generic;
using FishNet.Object;
using UI.HUD.GameTimer;
using UnityEngine;

namespace AI.Camp
{
    public class Camp : NetworkBehaviour
    {
        [SerializeField] private string campName = "Camp_01";
        [SerializeField] private GameObject campPrefab;
        
        [Header("Respawn Settings")]
        [SerializeField] private float initialSpawnTime = 30f;
        [SerializeField] private float respawnInterval = 45f;

        private readonly List<Neutral.Neutral> _units = new();
        private bool _spawnedInitial;
        private bool _isCampAlive;
        private float _nextRespawnTime;
        private GameObject _currentCampInstance;
        private CampController _currentController;

        public override void OnStartServer()
        {
            base.OnStartServer();
            GameTimer.OnTimeChanged += HandleTime;
        }

        public override void OnStopServer()
        {
            base.OnStopServer();
            GameTimer.OnTimeChanged -= HandleTime;
        }

        private void HandleTime(float time)
        {
            if (!IsServerInitialized) return;
            
            if (!_spawnedInitial && time >= initialSpawnTime)
            {
                SpawnCamp();
                _spawnedInitial = true;
                _isCampAlive = true;
            }

            // Респавн по интервалу, только если кемп мертв
            if (_spawnedInitial && !_isCampAlive && time >= _nextRespawnTime)
            {
                SpawnCamp();
                _isCampAlive = true;
            }
        }

        [Server]
        private void SpawnCamp()
        {
            if (_currentCampInstance != null)
            {
                DespawnCurrentCamp();
            }

            _currentCampInstance = Instantiate(campPrefab, transform.position, Quaternion.identity);
            Spawn(_currentCampInstance);
            
            StartCoroutine(CollectUnitsAfterSpawn(_currentCampInstance));
        }

        private System.Collections.IEnumerator CollectUnitsAfterSpawn(GameObject campInstance)
        {
            yield return new WaitForSeconds(0.1f);

            _units.Clear();

            // Получаем CampController из заспавненного префаба
            _currentController = campInstance.GetComponent<CampController>();

            if (_currentController != null)
            {
                // CampController сам найдет своих юнитов и зарегистрирует их через RegisterUnit()
                _currentController.InitializeUnits(this);
                // Юниты уже добавлены в _units через RegisterUnit(), не дублируем
            }
            else
            {
                // Fallback: ищем напрямую
                var found = campInstance.GetComponentsInChildren<Neutral.Neutral>();
                foreach (var n in found)
                {
                    _units.Add(n);
                    n.SetCampManager(this);
                }
            }

            _isCampAlive = _units.Count > 0;
        }

        [Server]
        private void DespawnCurrentCamp()
        {
            if (_currentCampInstance != null)
            {
                NetworkObject campNetworkObject = _currentCampInstance.GetComponent<NetworkObject>();
                if (campNetworkObject != null && campNetworkObject.IsSpawned)
                {
                    // Используем ServerManager для деспавна
                    ServerManager.Despawn(campNetworkObject);
                }
                else
                {
                    Destroy(_currentCampInstance);
                }
                _currentCampInstance = null;
                _currentController = null;
            }
            _units.Clear();
        }

        [Server]
        private void CheckIfCampCleared()
        {
            if (_units.Count == 0 && _isCampAlive)
            {
                _isCampAlive = false;

                if (GameTimer.Instance != null)
                {
                    _nextRespawnTime = GameTimer.Instance.CurrentTime + respawnInterval;
                }
            }
        }

        [Server]
        public void RemoveUnit(Neutral.Neutral unit)
        {
            if (_units.Remove(unit))
            {
                // Уведомляем контроллер об изменении
                if (_currentController != null)
                {
                    _currentController.OnUnitRemoved(unit);
                }

                CheckIfCampCleared();
            }
        }

        // Метод для регистрации юнита (вызывается из CampController)
        [Server]
        public void RegisterUnit(Neutral.Neutral unit)
        {
            if (!_units.Contains(unit))
            {
                _units.Add(unit);
                unit.SetCampManager(this);
            }
        }
    }
}