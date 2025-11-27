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
        // === FishNet SyncVar ===
        private readonly SyncVar<float> CurrentHealth;

        [Header("Settings")]
        [SerializeField] private float initialHealth = 100f;
        [SerializeField] private float maxHealth = 100f;
        
        // Свойство для доступа
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

        private void Awake()
        {
            CurrentHealth.OnChange += OnHealthChanged;

            if (maxHealth <= 0) maxHealth = initialHealth;
            
            CurrentHealth.Value = initialHealth;
        }

        private void OnEnable()
        {
                InitializeHealthBar();
        }

        private void OnDestroy()
        {
            CurrentHealth.OnChange -= OnHealthChanged;
        }

        public override void OnStartNetwork()
        {
            base.OnStartNetwork();
            InitializeHealthBar();
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
                    // Вручную инициализируем контроллер
                    _healthBarController.Initialize(this);
                    // Обновляем значения сразу
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
        public void TakeDamage(float amount, Transform attacker = null)
        {
            if (IsServerInitialized)
            {
                int dmgToSend = Mathf.CeilToInt(amount);
                ApplyDamage(dmgToSend, attacker);
            }
        }
        
        private void ApplyDamage(int damage, Transform attacker)
        {
            if (CurrentHealth.Value <= 0f) return;

            float newVal = Mathf.Clamp(CurrentHealth.Value - damage, 0f, MaxHealth);
            CurrentHealth.Value = newVal;

            NetworkObject attackerNo = attacker != null ? attacker.GetComponent<NetworkObject>() : null;
            ObserversRpc_OnDamaged(attackerNo);

            if (CurrentHealth.Value <= 0f)
            {
                Die();
                ObserversRpc_OnDeath();
            }
        }

        [ObserversRpc]
        private void ObserversRpc_OnDamaged(NetworkObject attackerNo)
        {
            Transform attackerTransform = attackerNo != null ? attackerNo.transform : null;
            OnDamaged?.Invoke(attackerTransform);
        }

        [ObserversRpc]
        private void ObserversRpc_OnDeath()
        {
            OnDeath?.Invoke(transform);
            
            if (_healthBarController != null)
            {
                Destroy(_healthBarController.gameObject);
                _healthBarController = null;
            }
            
            gameObject.SetActive(false);
        }

        private void Die()
        {
            var respawn = FindFirstObjectByType<RespawnManager>();
            if (respawn != null)
                respawn.StartRespawn(gameObject);
            else
                Debug.LogWarning("RespawnManager not found.");
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