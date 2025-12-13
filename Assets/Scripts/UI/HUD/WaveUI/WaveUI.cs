using AI.Wave;
using FishNet.Object;
using TMPro;
using UnityEngine;

namespace UI.HUD.WaveUI
{
    public class WaveUI : NetworkBehaviour
    {
        [Header("UI References")]
        [SerializeField] private TMP_Text waveText;
        [SerializeField] private TMP_Text notificationText;
        [SerializeField] private GameObject notificationPanel;

        [Header("Settings")]
        [SerializeField] private float notificationDuration = 3f;

        private float _notificationTimer;

        private void Update()
        {
            // Обновляем текст волны
            if (waveText != null && WaveManager.Instance != null)
            {
                int wave = WaveManager.Instance.CurrentWave;
                if (wave > 0)
                {
                    waveText.text = $"Wave: {wave}";

                    if (WaveManager.Instance.WaveActive)
                    {
                        waveText.color = Color.red;
                    }
                    else
                    {
                        waveText.color = Color.white;
                    }
                }
                else
                {
                    waveText.text = "Preparing...";
                    waveText.color = Color.yellow;
                }
            }

            // Скрываем уведомление после таймера
            if (notificationPanel != null && notificationPanel.activeSelf)
            {
                _notificationTimer -= Time.deltaTime;
                if (_notificationTimer <= 0f)
                {
                    notificationPanel.SetActive(false);
                }
            }
        }

        /// <summary>
        /// Показывает уведомление о начале волны
        /// </summary>
        public void ShowWaveStart(int waveNumber, int enemyCount)
        {
            if (notificationText != null && notificationPanel != null)
            {
                notificationText.text = $"WAVE {waveNumber}\n{enemyCount} Enemies Incoming!";
                notificationText.color = Color.red;
                notificationPanel.SetActive(true);
                _notificationTimer = notificationDuration;
            }
        }

        /// <summary>
        /// Показывает уведомление о завершении волны
        /// </summary>
        public void ShowWaveCleared(int waveNumber)
        {
            if (notificationText != null && notificationPanel != null)
            {
                notificationText.text = $"WAVE {waveNumber} CLEARED!";
                notificationText.color = Color.green;
                notificationPanel.SetActive(true);
                _notificationTimer = notificationDuration;
            }
        }

        /// <summary>
        /// Показывает экран победы/поражения
        /// </summary>
        public void ShowGameOver(int winnerId, bool isDraw, int localPlayerId)
        {
            if (notificationText != null && notificationPanel != null)
            {
                if (isDraw)
                {
                    notificationText.text = "GAME OVER\nDRAW!";
                    notificationText.color = Color.yellow;
                }
                else if (winnerId == localPlayerId)
                {
                    notificationText.text = "VICTORY!\nYou are the last one standing!";
                    notificationText.color = Color.green;
                }
                else
                {
                    notificationText.text = $"DEFEAT!\nPlayer {winnerId} wins!";
                    notificationText.color = Color.red;
                }

                notificationPanel.SetActive(true);
                _notificationTimer = 10f; // Долгое отображение для финала
            }
        }
    }
}
