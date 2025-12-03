using Core.Interfaces;
using Core.Settings;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;
using UI.HUD.HealthBar;

namespace Core.Components
{
    public class Health : NetworkBehaviour, IDamageable
    {
        private readonly SyncVar<float> CurrentHealth = new SyncVar<float>();

        [Header("Settings")]
        [SerializeField] private float initialHealth = 100f;
        [SerializeField] private float maxHealth = 100f;
        
        public float MaxHealth => maxHealth;

        [Header("UI Settings")]
        [Tooltip("Перетащите сюда префаб HealthBarCanvas")]
        [SerializeField] private GameObject healthBarPrefab; 
        
        private HealthBarController _healthBarController;

        // Events
        public delegate void DamageEvent(Transform attacker);
        public event DamageEvent OnDamaged;

        public delegate void DeathEvent(Transform deadTransform);
        public event DeathEvent OnDeath;

        private void OnDestroy()
        {
            CurrentHealth.OnChange -= OnHealthChanged;
        }
        
        public override void OnStartClient()
        {
            if (!IsOwner) return;
            
            base.OnStartClient();

            CurrentHealth.OnChange += OnHealthChanged;

            InitializeHealthBar();
        }

        public override void OnStartNetwork()
        {
            CurrentHealth.OnChange += OnHealthChanged;
            if (maxHealth <= 0) maxHealth = initialHealth;

            CurrentHealth.Value = initialHealth;
            base.OnStartNetwork();
        }

        public override void OnStopNetwork()
        {
            base.OnStopNetwork();
            if (_healthBarController != null)
            {
                Destroy(_healthBarController.gameObject);
            }
        }

        /// <summary>
        /// Создает Health Bar из префаба (Ручная настройка через Инспектор)
        /// </summary>
        public void InitializeHealthBar()
        {
            if (_healthBarController != null)
            {
                Destroy(_healthBarController.gameObject);
                _healthBarController = null;
            }

            if (healthBarPrefab != null)
            {
                GameObject healthBarGo = Instantiate(healthBarPrefab);
                _healthBarController = healthBarGo.GetComponent<HealthBarController>();
                
                if (_healthBarController != null)
                {
                    _healthBarController.Initialize(this);
                    _healthBarController.UpdateHealthBar(CurrentHealth.Value, maxHealth);
                }
                else
                {
                    Debug.LogError("HealthBarPrefab does not have a HealthBarController component!");
                }
            }
            else
            {
                Debug.LogWarning($"HealthBarPrefab is not assigned for {name}.");
            }
        }

        // -----------------------
        // Сетевая логика
        // -----------------------
        public void TakeDamage(float amount, Transform attacker = null, NetworkObject attackerNetworkObject = null)
        {
            if (IsServerInitialized)
            {
                int dmgToSend = Mathf.CeilToInt(amount);
        
                // Используем NetworkObject если он передан, иначе пытаемся получить из Transform
                NetworkObject noToSend = attackerNetworkObject ?? 
                                         (attacker != null ? attacker.GetComponent<NetworkObject>() : null);
        
                ApplyDamage(dmgToSend, noToSend);
            }
        }

        private void ApplyDamage(int damage, NetworkObject attackerNo)
        {
            if (CurrentHealth.Value <= 0f) return;

            float newVal = Mathf.Clamp(CurrentHealth.Value - damage, 0f, MaxHealth);
            CurrentHealth.Value = newVal;

            ObserversRpc_OnDamaged(attackerNo);

            if (CurrentHealth.Value <= 0f)
            {
                Transform attackerTransform = attackerNo != null ? attackerNo.transform : null;
                Die(attackerTransform);
            }
        }

        [ObserversRpc]
        private void ObserversRpc_OnDamaged(NetworkObject attackerNo)
        {
            Transform attackerTransform = attackerNo != null ? attackerNo.transform : null;
            OnDamaged?.Invoke(attackerTransform);
        }

        private void Die(Transform killer)
        {
            // Вызываем событие смерти на всех клиентах
            ObserversRpc_OnDeath();
            
            // Только сервер уничтожает объект
            if (IsServerInitialized)
            {
                // Проверяем, нужно ли респавнить
                var respawn = FindFirstObjectByType<RespawnManager>();
                if (respawn != null)
                {
                    respawn.StartRespawn(gameObject);
                }
                else
                {
                    // Если нет RespawnManager, просто уничтожаем
                    NetworkObject netObj = GetComponent<NetworkObject>();
                    if (netObj != null)
                    {
                        netObj.Despawn();
                    }
                    else
                    {
                        Destroy(gameObject);
                    }
                }
            }
        }

        [ObserversRpc]
        private void ObserversRpc_OnDeath()
        {
            // Вызываем событие смерти
            OnDeath?.Invoke(transform);
            
            // Уничтожаем HealthBar
            if (_healthBarController != null)
            {
                Destroy(_healthBarController.gameObject);
                _healthBarController = null;
            }
            
            // Отключаем визуал (опционально)
            var renderer = GetComponent<Renderer>();
            if (renderer != null) renderer.enabled = false;
            var collider = GetComponent<Collider>();
            if (collider != null) collider.enabled = false;
            
            // Не отключаем GameObject! Пусть Despawn сам обработает
        }

        private void OnHealthChanged(float oldVal, float newVal, bool asServer)
        {
            if (_healthBarController != null)
            {
                _healthBarController.UpdateHealthBar(newVal, MaxHealth);
            }
        }

        public void SetHealthBarController(HealthBarController controller)
        {
            _healthBarController = controller;
            _healthBarController.UpdateHealthBar(CurrentHealth.Value, MaxHealth);
        }

        // -----------------------
        // Public API
        // -----------------------
        public void SetHealth(float newHealth)
        {
            if (IsServerInitialized)
            {
                CurrentHealth.Value = Mathf.Clamp(newHealth, 0f, MaxHealth);
            }
        }

        public void Heal(float amount)
        {
            if (IsServerInitialized)
            {
                SetHealth(CurrentHealth.Value + amount);
            }
        }
        
        public float GetHealth() => CurrentHealth.Value;
        
        public float GetMaxHealth() => MaxHealth;
    }
}