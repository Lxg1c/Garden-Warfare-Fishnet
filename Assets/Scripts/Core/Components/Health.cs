using System;
using Core.Interfaces;
using Core.Settings;
using FischlWorks_FogWar;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;
using UI.HUD.HealthBar;
using Player;

namespace Core.Components
{
    public class Health : NetworkBehaviour, IDamageable
    {
        private readonly SyncVar<float> _сurrentHealth = new SyncVar<float>();

        [Header("Settings")]
        [SerializeField] private float initialHealth = 100f;
        [SerializeField] private float maxHealth = 100f;

        [Header("Regeneration")]
        [SerializeField] private bool enableRegen = true;
        [SerializeField] private float regenPerSecond = 5f; // HP в секунду
        [SerializeField] private float regenDelay = 3f; // Задержка после получения урона

        [Header("Death Behavior")]
        [SerializeField] private bool disableOnDeath = true; // Отключать GameObject при смерти
        [SerializeField] private bool despawnOnDeath = true; // Деспавнить если нет RespawnManager

        private csFogVisibilityAgent _agent;
        private float _lastDamageTime;
        private float _regenTimer;

        public float MaxHealth => maxHealth;
        public bool IsDead { get; private set; }

        [Header("UI Settings")]
        [Tooltip("Перетащите сюда префаб HealthBarCanvas")]
        [SerializeField] private GameObject healthBarPrefab;

        private HealthBarController _healthBarController;
        
        private CharacterController _characterController;
        private Renderer[] _renderers;
        private Collider[] _colliders;
        private MonoBehaviour[] _movementScripts;

        // Events
        public delegate void DamageEvent(Transform attacker);
        public event DamageEvent OnDamaged;

        public delegate void DeathEvent();
        public event DeathEvent OnDeath;

        public delegate void ReviveEvent();
        public event ReviveEvent OnRevive;

        private void Awake()
        {
            // Кэшируем компоненты сразу в Awake - до любых сетевых событий
            CacheComponents();
            _agent = GetComponent<csFogVisibilityAgent>();
        }

        private void OnDestroy()
        {
            _сurrentHealth.OnChange -= OnHealthChanged;
        }

        private void Update()
        {
            // Регенерация только на сервере
            if (IsServerStarted && enableRegen && !IsDead)
            {
                UpdateRegeneration();
            }
        }

        private void FixedUpdate()
        {
            // Скрываем/показываем HealthBar в зависимости от видимости в тумане войны
            if (_agent == null || _healthBarController == null) return;

            bool isVisible = _agent.GetVisibility();
            _healthBarController.gameObject.SetActive(isVisible);
        }

        /// <summary>
        /// Обновляет регенерацию HP на сервере
        /// </summary>
        private void UpdateRegeneration()
        {
            // Если здоровье полное - не регенерируем
            if (_сurrentHealth.Value >= maxHealth) return;

            // Проверяем прошло ли достаточно времени после последнего урона
            float timeSinceDamage = Time.time - _lastDamageTime;
            if (timeSinceDamage < regenDelay) return;

            // Регенерируем
            float regenAmount = regenPerSecond * Time.deltaTime;
            float newHealth = Mathf.Min(_сurrentHealth.Value + regenAmount, maxHealth);
            _сurrentHealth.Value = newHealth;
        }

        /// <summary>
        /// Сбрасывает таймер регенерации (вызывается при получении урона)
        /// </summary>
        public void ResetRegenTimer()
        {
            _lastDamageTime = Time.time;
        }

        public override void OnStartClient()
        {
            base.OnStartClient();
            InitializeHealthBar();
        }

        public override void OnStartNetwork()
        {
            _сurrentHealth.OnChange += OnHealthChanged;
            if (maxHealth <= 0) maxHealth = initialHealth;

            _сurrentHealth.Value = initialHealth;
            IsDead = false;

            // Перекэшируем на случай если Awake не сработал (late join)
            if (_renderers == null || _renderers.Length == 0)
            {
                CacheComponents();
            }

            base.OnStartNetwork();
        }

        private void CacheComponents()
        {
            _characterController = GetComponent<CharacterController>();
            _renderers = GetComponentsInChildren<Renderer>();
            _colliders = GetComponentsInChildren<Collider>();

            // Находим скрипты которые нужно отключить при смерти
            var scriptsToDisable = new System.Collections.Generic.List<MonoBehaviour>();
            var playerMovement = GetComponent<PlayerMovement>();
            if (playerMovement != null) scriptsToDisable.Add(playerMovement);
            var weaponController = GetComponent<Weapon.WeaponController>();
            if (weaponController != null) scriptsToDisable.Add(weaponController);
            _movementScripts = scriptsToDisable.ToArray();

            Debug.Log($"[Health] {name} CacheComponents: CharCtrl={_characterController != null}, " +
                      $"Renderers={_renderers?.Length ?? 0}, Colliders={_colliders?.Length ?? 0}, " +
                      $"Scripts={_movementScripts?.Length ?? 0}");
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

            // Сбрасываем таймер регенерации
            _lastDamageTime = Time.time;

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
            ObserversRpc_OnDeath(disableOnDeath);

            if (IsServerInitialized)
            {
                var respawn = FindFirstObjectByType<RespawnManager>();
                if (respawn != null)
                {
                    respawn.StartRespawn(gameObject);
                }
                else if (despawnOnDeath)
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
                // Если despawnOnDeath = false, объект остаётся (например LifeFruit - зона видна)
            }
        }

        [ObserversRpc]
        private void ObserversRpc_OnDeath(bool shouldDisable)
        {
            Debug.Log($"[Health] {name} ObserversRpc_OnDeath called (shouldDisable={shouldDisable})");

            IsDead = true;

            // Вызываем событие смерти
            OnDeath?.Invoke();

            // Уничтожаем HealthBar
            if (_healthBarController != null)
            {
                Destroy(_healthBarController.gameObject);
                _healthBarController = null;
            }

            // Отключаем весь GameObject только если разрешено
            if (shouldDisable)
            {
                gameObject.SetActive(false);
                Debug.Log($"[Health] {name} died - GameObject disabled");
            }
            else
            {
                Debug.Log($"[Health] {name} died - GameObject kept active");
            }
        }

        [ObserversRpc]
        private void ObserversRpc_OnRevive(Vector3 position, Quaternion rotation)
        {
            Debug.Log($"[Health] {name} ObserversRpc_OnRevive called");

            IsDead = false;

            // ВАЖНО: Сначала включаем GameObject (он был выключен в OnDeath)
            gameObject.SetActive(true);

            // Отключаем CharacterController для телепортации
            if (_characterController != null)
            {
                _characterController.enabled = false;
            }

            // Телепортируем на точку респавна
            transform.SetPositionAndRotation(position, rotation);

            // Включаем CharacterController обратно
            if (_characterController != null)
            {
                _characterController.enabled = true;
            }

            // Пересоздаем HealthBar
            InitializeHealthBar();

            // Вызываем событие возрождения
            OnRevive?.Invoke();

            Debug.Log($"[Health] {name} revived at {position}");
        }

        /// <summary>
        /// Вызывается сервером для возрождения игрока
        /// </summary>
        [Server]
        public void Revive(Vector3 respawnPosition, Quaternion respawnRotation)
        {
            // Восстанавливаем здоровье
            _сurrentHealth.Value = maxHealth;

            // Синхронизируем респавн на всех клиентах
            ObserversRpc_OnRevive(respawnPosition, respawnRotation);
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