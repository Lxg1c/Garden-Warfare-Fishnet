using System;
using UnityEngine;
using FishNet.Object;
using TMPro;

namespace UI.HUD.GameTimer
{
    public class GameTimer : NetworkBehaviour
    {
        public static GameTimer Instance { get; private set; }
        
        [SerializeField] private TMP_Text timerText;
        
        private float _currentTime;
        private float _lastSyncTime;
        private const float SyncInterval = 0.5f;

        public float CurrentTime => _currentTime;
        
        // Events
        public static event Action<float> OnTimeChanged;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
        }

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
            if (IsServerInitialized)
            {
                float previousTime = _currentTime;
                _currentTime += Time.deltaTime;

                // Вызываем событие только на сервере, когда время реально изменилось
                if (Mathf.FloorToInt(_currentTime) != Mathf.FloorToInt(previousTime))
                {
                    OnTimeChanged?.Invoke(_currentTime);
                }

                if (_currentTime - _lastSyncTime >= SyncInterval)
                {
                    UpdateTimerObserversRpc(_currentTime);
                    _lastSyncTime = _currentTime;
                }
            }

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

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }
    }
}