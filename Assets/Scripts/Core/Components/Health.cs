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
        private readonly SyncVar<float> _сurrentHealth = new SyncVar<float>();

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

        public delegate void DeathEvent();
        public event DeathEvent OnDeath;

        private void OnDestroy()
        {
            _сurrentHealth.OnChange -= OnHealthChanged;
        }
        
        public override void OnStartClient()
        {
            base.OnStartClient();
            // Подписка на OnChange уже сделана в OnStartNetwork(), не дублируем

            InitializeHealthBar();
        }


        public override void OnStartNetwork()
        {
            _сurrentHealth.OnChange += OnHealthChanged;
            if (maxHealth <= 0) maxHealth = initialHealth;

            _сurrentHealth.Value = initialHealth;
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
                    _healthBarController.UpdateHealthBar(_сurrentHealth.Value, maxHealth);
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
            if (_сurrentHealth.Value <= 0f) return;

            float newVal = Mathf.Clamp(_сurrentHealth.Value - damage, 0f, MaxHealth);
            _сurrentHealth.Value = newVal;

            ObserversRpc_OnDamaged(attackerNo);

            if (_сurrentHealth.Value <= 0f)
            {
                Die();
            }
        }

        [ObserversRpc]
        private void ObserversRpc_OnDamaged(NetworkObject attackerNo)
        {
            Transform attackerTransform = attackerNo != null ? attackerNo.transform : null;
            OnDamaged?.Invoke(attackerTransform);
        }

        private void Die()
        {
            ObserversRpc_OnDeath();
            
            if (IsServerInitialized)
            {
                var respawn = FindFirstObjectByType<RespawnManager>();
                if (respawn != null)
                {
                    respawn.StartRespawn(gameObject);
                }
                else
                {
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
            OnDeath?.Invoke();
            
            // Уничтожаем HealthBar
            if (_healthBarController != null)
            {
                Destroy(_healthBarController.gameObject);
                _healthBarController = null;
            }
            
            var renderer = GetComponent<Renderer>();
            if (renderer != null) renderer.enabled = false;
            var collider = GetComponent<Collider>();
            if (collider != null) collider.enabled = false;
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
            _healthBarController.UpdateHealthBar(_сurrentHealth.Value, MaxHealth);
        }

        // -----------------------
        // Public API
        // -----------------------
        public void SetHealth(float newHealth)
        {
            if (IsServerInitialized)
            {
                _сurrentHealth.Value = Mathf.Clamp(newHealth, 0f, MaxHealth);
            }
        }

        public void Heal(float amount)
        {
            if (IsServerInitialized)
            {
                SetHealth(_сurrentHealth.Value + amount);
            }
        }
        
        public float GetHealth() => _сurrentHealth.Value;
        
        public float GetMaxHealth() => MaxHealth;
    }
}