using AI.Wave;
using FishNet.Object;
using Gameplay;
using Gameplay.TurretPlant;
using TMPro;
using UnityEngine;

namespace UI.HUD.WaveUI
{
    public class WaveUI : NetworkBehaviour
    {
        [Header("Wave Info")]
        [SerializeField] private TMP_Text waveText;
        [SerializeField] private TMP_Text enemiesText;
        [SerializeField] private TMP_Text nextWaveTimerText;

        [Header("Player Stats")]
        [SerializeField] private TMP_Text turretsText;
        [SerializeField] private TMP_Text lifeFruitStatusText;

        private int _localPlayerId = -1;

        public override void OnStartClient()
        {
            base.OnStartClient();

            // Получаем ID локального игрока
            if (NetworkManager?.ClientManager?.Connection?.FirstObject != null)
            {
                _localPlayerId = NetworkManager.ClientManager.Connection.FirstObject.OwnerId;
            }
        }

        private void Update()
        {
            UpdateWaveInfo();
            UpdatePlayerStats();
        }

        private void UpdateWaveInfo()
        {
            if (WaveManager.Instance == null) return;

            int wave = WaveManager.Instance.CurrentWave;
            bool waveActive = WaveManager.Instance.WaveActive;

            // Номер волны
            if (waveText != null)
            {
                if (wave > 0)
                {
                    waveText.text = $"Wave {wave}";
                    waveText.color = waveActive ? Color.red : Color.white;
                }
                else
                {
                    waveText.text = "Preparing...";
                    waveText.color = Color.yellow;
                }
            }

            // Количество врагов
            if (enemiesText != null)
            {
                int enemyCount = CountAliveEnemies();
                if (waveActive && enemyCount > 0)
                {
                    enemiesText.text = $"Enemies: {enemyCount}";
                    enemiesText.color = Color.red;
                    enemiesText.gameObject.SetActive(true);
                }
                else
                {
                    enemiesText.gameObject.SetActive(false);
                }
            }

            // Таймер до следующей волны
            if (nextWaveTimerText != null)
            {
                if (!waveActive && wave >= 0)
                {
                    float timeLeft = GetTimeUntilNextWave();
                    if (timeLeft > 0)
                    {
                        nextWaveTimerText.text = $"Next wave: {Mathf.CeilToInt(timeLeft)}s";
                        nextWaveTimerText.color = timeLeft <= 10f ? Color.yellow : Color.white;
                        nextWaveTimerText.gameObject.SetActive(true);
                    }
                    else
                    {
                        nextWaveTimerText.gameObject.SetActive(false);
                    }
                }
                else
                {
                    nextWaveTimerText.gameObject.SetActive(false);
                }
            }
        }

        private void UpdatePlayerStats()
        {
            // Пробуем получить ID игрока если ещё не получили
            if (_localPlayerId < 0)
            {
                if (NetworkManager?.ClientManager?.Connection?.FirstObject != null)
                {
                    _localPlayerId = NetworkManager.ClientManager.Connection.FirstObject.OwnerId;
                }
                else
                {
                    return;
                }
            }

            // Количество турелей
            if (turretsText != null && TurretPlantManager.Instance != null)
            {
                int turretCount = TurretPlantManager.Instance.GetTurretCount(_localPlayerId);
                int maxTurrets = TurretPlantManager.Instance.GetMaxTurretsPerPlayer();
                turretsText.text = $"Turrets: {turretCount}/{maxTurrets}";
            }

            // Статус LifeFruit
            if (lifeFruitStatusText != null && TurretPlantManager.Instance != null)
            {
                LifeFruit myFruit = TurretPlantManager.Instance.FindLifeFruitForPlayer(_localPlayerId);

                if (myFruit == null)
                {
                    lifeFruitStatusText.text = "LifeFruit: NONE";
                    lifeFruitStatusText.color = Color.red;
                }
                else if (myFruit.IsDead)
                {
                    lifeFruitStatusText.text = "LifeFruit: DESTROYED";
                    lifeFruitStatusText.color = Color.red;
                }
                else if (myFruit.State == LifeFruitState.Carried)
                {
                    lifeFruitStatusText.text = "LifeFruit: STOLEN!";
                    lifeFruitStatusText.color = Color.yellow;
                }
                else
                {
                    lifeFruitStatusText.text = "LifeFruit: OK";
                    lifeFruitStatusText.color = Color.green;
                }
            }
        }

        private int CountAliveEnemies()
        {
            int count = 0;
            var enemies = FindObjectsByType<WaveEnemy>(FindObjectsSortMode.None);
            foreach (var enemy in enemies)
            {
                if (enemy != null && !enemy.IsDead)
                {
                    count++;
                }
            }
            return count;
        }

        private float GetTimeUntilNextWave()
        {
            var gameTimer = UI.HUD.GameTimer.GameTimer.Instance;
            if (gameTimer == null || WaveManager.Instance == null) return 0f;

            // Используем реальное время следующей волны из WaveManager
            float nextWaveTime = WaveManager.Instance.NextWaveTime;
            float currentTime = gameTimer.CurrentTime;

            return Mathf.Max(0f, nextWaveTime - currentTime);
        }

    }
}
