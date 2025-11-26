using UnityEngine;
using FishNet.Object;
using TMPro;

namespace UI.HUD.GameTimer
{
    public class GameTimer : NetworkBehaviour
    {
        [SerializeField] private TMP_Text timerText;
        
        private float _currentTime;
        private float _lastSyncTime;
        private const float SYNC_INTERVAL = 0.5f; // Синхронизируем каждые 0.5 секунды

        public override void OnStartNetwork()
        {
            base.OnStartNetwork();
            
            if (IsServerInitialized)
            {
                _currentTime = 0f;
                _lastSyncTime = 0f;
            }
        }

        private void Update()
        {
            // Сервер обновляет время
            if (IsServerInitialized)
            {
                _currentTime += Time.deltaTime;
                
                // Синхронизируем только каждые SYNC_INTERVAL секунд для оптимизации
                if (_currentTime - _lastSyncTime >= SYNC_INTERVAL)
                {
                    UpdateTimerObserversRpc(_currentTime);
                    _lastSyncTime = _currentTime;
                }
            }
            
            // Все клиенты обновляют отображение плавно
            UpdateTimerDisplay(_currentTime);
        }

        [ObserversRpc]
        private void UpdateTimerObserversRpc(float time)
        {
            _currentTime = time;
        }

        private void UpdateTimerDisplay(float time)
        {
            if (timerText != null)
            {
                int minutes = Mathf.FloorToInt(time / 60f);
                int seconds = Mathf.FloorToInt(time % 60f);
                timerText.text = string.Format("{0:00}:{1:00}", minutes, seconds);
            }
        }
    }
}