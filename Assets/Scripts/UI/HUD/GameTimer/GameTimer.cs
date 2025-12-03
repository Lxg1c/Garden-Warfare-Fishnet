using System;
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
        private const float SyncInterval = 0.5f;
        
        // Events
        public static event Action<float> OnTimeChanged;


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
                _currentTime += Time.deltaTime;

                if (_currentTime - _lastSyncTime >= SyncInterval)
                {
                    UpdateTimerObserversRpc(_currentTime);
                    _lastSyncTime = _currentTime;
                }
            }

            OnTimeChanged?.Invoke(_currentTime);

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