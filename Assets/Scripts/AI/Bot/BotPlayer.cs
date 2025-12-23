using Core.Components;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using Gameplay;
using Gameplay.TurretPlant;
using UnityEngine;
using UnityEngine.AI;
using AI.Wave;
using AI.Neutral;

namespace AI.Bot
{
    public enum BotState
    {
        Idle,
        GoingToPlant,       // Идёт к дикому растению
        KillingGuards,      // Убивает охранников растения
        DiggingPlant,       // Выкапывает растение
        CarryingPlant,      // Несёт растение
        Planting,           // Сажает растение
        Fighting,           // Бой с врагами
        Defending,          // Защищает LifeFruit
        Raiding             // Атакует базу противника
    }

    [RequireComponent(typeof(NavMeshAgent))]
    [RequireComponent(typeof(Health))]
    public class BotPlayer : NetworkBehaviour
    {
        [Header("Bot Settings")]
        [SerializeField] private float thinkInterval = 0.5f;
        [SerializeField] private float interactRange = 3.5f;
        [SerializeField] private float digTime = 1.5f;
        [SerializeField] private float plantTime = 1f;

        [Header("Planting")]
        [SerializeField] private float minPlantDistance = 3f;  // Минимальное расстояние от LifeFruit
        [SerializeField] private float maxPlantDistance = 6f;  // Максимальное расстояние от LifeFruit

        [Header("Combat")]
        [SerializeField] private float detectionRange = 10f;
        [SerializeField] private float attackRange = 4f;
        [SerializeField] private float playerAggroRange = 12f; // Радиус агро на игроков
        [SerializeField] private float aggroTimeout = 8f;      // Время агро перед отвязкой
        [SerializeField] private float leashRange = 25f;       // Расстояние от дома для отвязки

        [Header("Raiding")]
        [SerializeField] private float raidChance = 0.3f;      // Шанс начать рейд (30%)
        [SerializeField] private float raidCheckInterval = 20f; // Проверка каждые 20 сек
        [SerializeField] private float attackCooldown = 0.5f;
        [SerializeField] private int attackDamage = 10;
        [SerializeField] private GameObject bulletPrefab;
        [SerializeField] private Transform shootPoint;
        [SerializeField] private float bulletForce = 15f;

        [Header("Movement")]
        [SerializeField] private float moveSpeed = 5f;
        [SerializeField] private float carrySpeedMultiplier = 0.7f;

        // ==================
        // Синхронизация
        // ==================

        private readonly SyncVar<BotState> _state = new(BotState.Idle);
        private readonly SyncVar<int> _botId = new(-1);

        // ==================
        // Компоненты
        // ==================

        private NavMeshAgent _agent;
        private Health _health;
        private Animator _animator;

        // ==================
        // AI данные
        // ==================

        private float _thinkTimer;
        private float _actionTimer;
        private float _attackTimer;
        private float _raidCheckTimer;
        private float _aggroTimer;          // Таймер текущего агро

        private TurretPlant _targetPlant;
        private Transform _targetEnemy;
        private LifeFruit _myLifeFruit;
        private TurretPlant _carriedPlant;

        // Рейд
        private LifeFruit _raidTarget;
        private TurretPlant _raidTurretTarget;

        // Позиция спавна (для посадки растений)
        private Vector3 _homePosition;

        // Хеши анимаций (как у игрока)
        private int _moveXHash;
        private int _moveYHash;
        private int _moveSpeedHash;

        // Для плавной анимации
        private float _animMoveX;
        private float _animMoveY;
        private readonly float _animationSmooth = 10f;

        // ==================
        // Properties
        // ==================

        public int BotId => _botId.Value;
        public BotState State => _state.Value;

        // ==================
        // Initialization
        // ==================

        private void Awake()
        {
            _agent = GetComponent<NavMeshAgent>();
            _health = GetComponent<Health>();
            _animator = GetComponent<Animator>();

            // Кешируем хеши анимаций (такие же как у игрока)
            _moveXHash = Animator.StringToHash("moveX");
            _moveYHash = Animator.StringToHash("moveY");
            _moveSpeedHash = Animator.StringToHash("speed");
        }

        public override void OnStartServer()
        {
            base.OnStartServer();

            _agent.speed = moveSpeed;
            _agent.stoppingDistance = 0.5f;
            _homePosition = transform.position;

            _health.OnDeath += OnBotDeath;
            _health.OnRevive += OnBotRevive;
        }

        public override void OnStopServer()
        {
            base.OnStopServer();

            if (_health != null)
            {
                _health.OnDeath -= OnBotDeath;
                _health.OnRevive -= OnBotRevive;
            }
        }

        /// <summary>
        /// Инициализация бота (вызывается после спавна)
        /// </summary>
        [Server]
        public void Initialize(int botId, Vector3 homePosition)
        {
            _botId.Value = botId;
            _homePosition = homePosition;
        }

        /// <summary>
        /// Устанавливает LifeFruit для бота
        /// </summary>
        [Server]
        public void SetLifeFruit(LifeFruit lifeFruit)
        {
            _myLifeFruit = lifeFruit;
        }

        // ==================
        // Update Loop
        // ==================

        private void Update()
        {
            if (!IsServerInitialized) return;

            // Думаем с интервалом
            _thinkTimer -= Time.deltaTime;
            if (_thinkTimer <= 0f)
            {
                Think();
                _thinkTimer = thinkInterval;
            }

            // Выполняем текущее действие
            ExecuteCurrentState();

            // Обновляем таймеры
            _attackTimer -= Time.deltaTime;
            if (_aggroTimer > 0f)
            {
                _aggroTimer -= Time.deltaTime;
            }

            // Обновляем анимации
            UpdateAnimations();
        }

        // ==================
        // AI Logic
        // ==================

        [Server]
        private void Think()
        {
            // Не прерываем активные действия (копание, посадка)
            if (_state.Value == BotState.DiggingPlant ||
                _state.Value == BotState.Planting)
            {
                return;
            }

            bool isWaveActive = WaveManager.Instance != null && WaveManager.Instance.WaveActive;

            // 1. ВЫСШИЙ ПРИОРИТЕТ: Защита базы во время волны
            if (isWaveActive && ShouldDefend())
            {
                ResetAggroState();
                _state.Value = BotState.Defending;
                return;
            }

            // 2. Если несём растение - несём к базе (всегда завершаем)
            if (_carriedPlant != null)
            {
                _state.Value = BotState.CarryingPlant;
                return;
            }

            // 3. Проверяем таймаут агро - если слишком долго сражаемся или далеко от базы, отвязываемся
            if (ShouldDisengage())
            {
                ResetAggroState();
                _state.Value = BotState.Idle;
                return;
            }

            // 4. Во время волны - приоритет на защиту, не отвлекаемся на рейды
            if (isWaveActive)
            {
                // Проверяем угрозы рядом с базой
                if (ShouldDefend())
                {
                    ResetAggroState();
                    _state.Value = BotState.Defending;
                    return;
                }

                // Волновые враги в зоне видимости - сражаемся
                Transform waveEnemy = FindNearestWaveEnemy();
                if (waveEnemy != null)
                {
                    SetNewAggroTarget(waveEnemy);
                    _state.Value = BotState.Fighting;
                    return;
                }

                // Возвращаемся к базе если волна активна
                _state.Value = BotState.Idle;
                return;
            }

            // === МЕЖДУ ВОЛНАМИ ===

            // 5. Проверяем игроков в радиусе агро
            Transform player = FindNearestPlayer();
            if (player != null)
            {
                SetNewAggroTarget(player);
                _state.Value = BotState.Fighting;
                return;
            }

            // 6. Если есть враги в зоне видимости (нейтралы) - сражаемся
            Transform enemy = FindNearestEnemy();
            if (enemy != null)
            {
                SetNewAggroTarget(enemy);
                _state.Value = BotState.Fighting;
                return;
            }

            // 7. Продолжаем рейд если начат
            if (_state.Value == BotState.Raiding && _raidTarget != null)
            {
                return;
            }

            // 8. Проверяем шанс начать рейд (только между волнами)
            _raidCheckTimer -= thinkInterval;
            if (_raidCheckTimer <= 0f)
            {
                _raidCheckTimer = raidCheckInterval;
                if (Random.value < raidChance && TryStartRaid())
                {
                    _state.Value = BotState.Raiding;
                    return;
                }
            }

            // 9. Если можем посадить ещё турелей - ищем растения (фарм)
            bool canPlant = CanPlantMoreTurrets();
            if (canPlant)
            {
                TurretPlant plant = FindNearestWildPlant();
                if (plant != null)
                {
                    _targetPlant = plant;

                    // Проверяем есть ли охранники возле растения
                    Transform guard = FindGuardNearPlant(plant);
                    if (guard != null)
                    {
                        SetNewAggroTarget(guard);
                        _state.Value = BotState.KillingGuards;
                    }
                    else
                    {
                        _state.Value = BotState.GoingToPlant;
                    }
                    return;
                }
            }

            // 10. Иначе - патрулируем возле базы
            _state.Value = BotState.Idle;
        }

        /// <summary>
        /// Проверяет нужно ли отвязаться от текущей цели
        /// </summary>
        private bool ShouldDisengage()
        {
            // Не отвязываемся если не сражаемся
            if (_state.Value != BotState.Fighting && _state.Value != BotState.KillingGuards)
                return false;

            // Отвязываемся если таймер агро истёк
            if (_aggroTimer <= 0f)
                return true;

            // Отвязываемся если слишком далеко от базы
            float distFromHome = Vector3.Distance(transform.position, _homePosition);
            if (distFromHome > leashRange)
                return true;

            return false;
        }

        /// <summary>
        /// Устанавливает новую цель агро и сбрасывает таймер
        /// </summary>
        private void SetNewAggroTarget(Transform target)
        {
            if (_targetEnemy != target)
            {
                _targetEnemy = target;
                _aggroTimer = aggroTimeout;
            }
        }

        /// <summary>
        /// Сбрасывает состояние агро
        /// </summary>
        private void ResetAggroState()
        {
            _targetEnemy = null;
            _aggroTimer = 0f;
            _raidTarget = null;
            _raidTurretTarget = null;
        }

        /// <summary>
        /// Ищет ближайшего волнового врага
        /// </summary>
        private Transform FindNearestWaveEnemy()
        {
            var waveEnemies = FindObjectsByType<WaveEnemy>(FindObjectsSortMode.None);
            Transform nearest = null;
            float nearestDist = float.MaxValue;

            foreach (var enemy in waveEnemies)
            {
                if (enemy == null || enemy.IsDead) continue;

                float dist = Vector3.Distance(transform.position, enemy.transform.position);
                if (dist < detectionRange && dist < nearestDist)
                {
                    nearest = enemy.transform;
                    nearestDist = dist;
                }
            }

            return nearest;
        }

        [Server]
        private void ExecuteCurrentState()
        {
            switch (_state.Value)
            {
                case BotState.Idle:
                    ExecuteIdle();
                    break;
                case BotState.GoingToPlant:
                    ExecuteGoingToPlant();
                    break;
                case BotState.KillingGuards:
                    ExecuteKillingGuards();
                    break;
                case BotState.DiggingPlant:
                    ExecuteDiggingPlant();
                    break;
                case BotState.CarryingPlant:
                    ExecuteCarryingPlant();
                    break;
                case BotState.Planting:
                    ExecutePlanting();
                    break;
                case BotState.Fighting:
                    ExecuteFighting();
                    break;
                case BotState.Defending:
                    ExecuteDefending();
                    break;
                case BotState.Raiding:
                    ExecuteRaiding();
                    break;
            }
        }

        // ==================
        // State Execution
        // ==================

        private void ExecuteIdle()
        {
            // Стоим возле базы или патрулируем
            if (_agent.remainingDistance < 1f)
            {
                // Случайная точка возле дома
                Vector3 randomPoint = _homePosition + Random.insideUnitSphere * 5f;
                randomPoint.y = _homePosition.y;

                if (NavMesh.SamplePosition(randomPoint, out NavMeshHit hit, 5f, NavMesh.AllAreas))
                {
                    _agent.SetDestination(hit.position);
                }
            }
        }

        private void ExecuteGoingToPlant()
        {
            if (_targetPlant == null)
            {
                _state.Value = BotState.Idle;
                return;
            }

            // Проверяем остались ли охранники
            Transform guard = FindGuardNearPlant(_targetPlant);
            if (guard != null)
            {
                _targetEnemy = guard;
                _state.Value = BotState.KillingGuards;
                return;
            }

            float distToPlant = Vector3.Distance(transform.position, _targetPlant.transform.position);

            // Если растение недоступно (атакует или уже взято) - ждём или ищем другое
            if (!_targetPlant.CanBePickedUp)
            {
                // Проверяем это наше целевое растение ещё Wild
                if (_targetPlant.State != TurretPlantState.Wild)
                {
                    _targetPlant = null;
                    _state.Value = BotState.Idle;
                    return;
                }

                // Растение атакует - ждём на безопасном расстоянии

                float safeDist = 5f;
                if (distToPlant < safeDist)
                {
                    Vector3 awayDir = (transform.position - _targetPlant.transform.position).normalized;
                    Vector3 safePos = _targetPlant.transform.position + awayDir * safeDist;
                    _agent.SetDestination(safePos);
                }
                else
                {
                    _agent.ResetPath(); // Стоим и ждём
                }
                return;
            }

            // Растение доступно - подходим и копаем
            if (distToPlant <= interactRange)
            {
                _state.Value = BotState.DiggingPlant;
                _actionTimer = digTime;
                _agent.ResetPath();
            }
            else
            {
                _agent.SetDestination(_targetPlant.transform.position);
            }
        }

        private void ExecuteKillingGuards()
        {
            // Если целевое растение пропало - возвращаемся в Idle
            if (_targetPlant == null || !_targetPlant.CanBePickedUp)
            {
                _state.Value = BotState.Idle;
                return;
            }

            // Если враг мёртв - ищем следующего или идём к растению
            if (_targetEnemy == null || !IsEnemyValid(_targetEnemy))
            {
                Transform nextGuard = FindGuardNearPlant(_targetPlant);
                if (nextGuard != null)
                {
                    _targetEnemy = nextGuard;
                }
                else
                {
                    // Все охранники убиты - идём к растению
                    _state.Value = BotState.GoingToPlant;
                    return;
                }
            }

            float dist = Vector3.Distance(transform.position, _targetEnemy.position);

            RotateTowards(_targetEnemy.position);

            if (dist <= attackRange)
            {
                // Стреляем
                _agent.ResetPath();
                TryShoot(_targetEnemy);
            }
            else
            {
                // Подходим ближе
                _agent.SetDestination(_targetEnemy.position);
            }
        }

        private void ExecuteDiggingPlant()
        {
            if (_targetPlant == null)
            {
                _state.Value = BotState.Idle;
                return;
            }

            if (!_targetPlant.CanBePickedUp)
            {
                _targetPlant = null;
                _state.Value = BotState.Idle;
                return;
            }

            _actionTimer -= Time.deltaTime;

            if (_actionTimer <= 0f)
            {
                // Подбираем растение
                if (_targetPlant.TryPickup(_botId.Value))
                {
                    _carriedPlant = _targetPlant;
                    _targetPlant = null;
                    _agent.speed = moveSpeed * carrySpeedMultiplier;
                    _state.Value = BotState.CarryingPlant;
                }
                else
                {
                    _targetPlant = null;
                    _state.Value = BotState.Idle;
                }
            }
        }

        private void ExecuteCarryingPlant()
        {
            if (_carriedPlant == null)
            {
                _agent.speed = moveSpeed;
                _state.Value = BotState.Idle;
                return;
            }

            // Обновляем позицию растения (несём его)
            Vector3 carryPos = transform.position + transform.forward * 0.5f + Vector3.up * 0.5f;
            _carriedPlant.transform.position = carryPos;

            // Идём к зоне посадки (возле LifeFruit)
            Vector3 plantTarget = GetPlantingPosition();
            float dist = Vector3.Distance(transform.position, plantTarget);

            if (dist <= interactRange)
            {
                _state.Value = BotState.Planting;
                _actionTimer = plantTime;
                _agent.ResetPath();
            }
            else
            {
                _agent.SetDestination(plantTarget);
            }
        }

        private void ExecutePlanting()
        {
            if (_carriedPlant == null)
            {
                _agent.speed = moveSpeed;
                _state.Value = BotState.Idle;
                return;
            }

            _actionTimer -= Time.deltaTime;

            if (_actionTimer <= 0f)
            {
                // Сажаем растение
                Vector3 plantPos = GetPlantingPosition();
                _carriedPlant.Plant(_botId.Value, plantPos);

                _carriedPlant = null;
                _agent.speed = moveSpeed;
                _state.Value = BotState.Idle;
            }
        }

        private void ExecuteFighting()
        {
            if (_targetEnemy == null || !IsEnemyValid(_targetEnemy))
            {
                _targetEnemy = FindNearestEnemy();
                if (_targetEnemy == null)
                {
                    _state.Value = BotState.Idle;
                    return;
                }
            }

            float dist = Vector3.Distance(transform.position, _targetEnemy.position);

            // Поворачиваемся к врагу
            RotateTowards(_targetEnemy.position);

            if (dist <= attackRange)
            {
                // Стреляем
                _agent.ResetPath();
                TryShoot(_targetEnemy);
            }
            else
            {
                // Подходим ближе
                _agent.SetDestination(_targetEnemy.position);
            }
        }

        private void ExecuteDefending()
        {
            // Возвращаемся к LifeFruit и защищаем
            if (_myLifeFruit == null)
            {
                _state.Value = BotState.Idle;
                return;
            }

            Transform threat = FindThreatNearLifeFruit();
            if (threat == null)
            {
                _state.Value = BotState.Idle;
                return;
            }

            _targetEnemy = threat;
            float dist = Vector3.Distance(transform.position, threat.position);

            RotateTowards(threat.position);

            if (dist <= attackRange)
            {
                _agent.ResetPath();
                TryShoot(threat);
            }
            else
            {
                _agent.SetDestination(threat.position);
            }
        }

        private void ExecuteRaiding()
        {
            // Проверяем цель рейда
            if (_raidTarget == null)
            {
                _raidTurretTarget = null;
                _state.Value = BotState.Idle;
                return;
            }

            // Проверяем что цель ещё жива
            var raidTargetHealth = _raidTarget.GetComponent<Health>();
            if (raidTargetHealth != null && raidTargetHealth.IsDead)
            {
                _raidTarget = null;
                _raidTurretTarget = null;
                _state.Value = BotState.Idle;
                return;
            }

            // Приоритет 1: уничтожаем турели рядом с целью
            if (_raidTurretTarget == null || !IsRaidTurretValid(_raidTurretTarget))
            {
                _raidTurretTarget = FindEnemyTurretNear(_raidTarget);
            }

            // Если есть турель - атакуем её
            if (_raidTurretTarget != null)
            {
                float distToTurret = Vector3.Distance(transform.position, _raidTurretTarget.transform.position);
                RotateTowards(_raidTurretTarget.transform.position);

                if (distToTurret <= attackRange)
                {
                    _agent.ResetPath();
                    TryShoot(_raidTurretTarget.transform);
                }
                else
                {
                    _agent.SetDestination(_raidTurretTarget.transform.position);
                }
                return;
            }

            // Приоритет 2: атакуем LifeFruit
            float distToTarget = Vector3.Distance(transform.position, _raidTarget.transform.position);
            RotateTowards(_raidTarget.transform.position);

            if (distToTarget <= attackRange)
            {
                _agent.ResetPath();
                TryShoot(_raidTarget.transform);
            }
            else
            {
                _agent.SetDestination(_raidTarget.transform.position);
            }
        }

        // ==================
        // Combat
        // ==================

        [Server]
        private void TryShoot(Transform target)
        {
            if (_attackTimer > 0f) return;
            if (bulletPrefab == null) return;

            Transform spawnPoint = shootPoint != null ? shootPoint : transform;
            Vector3 spawnPos = spawnPoint.position;

            // Целимся в центр массы врага (высота 1м от пола)
            Vector3 targetPoint = target.position + Vector3.up * 1f;

            // Проверяем что бот повернут к цели (угол < 15 градусов)
            Vector3 toTarget = (targetPoint - transform.position);
            toTarget.y = 0;
            float angle = Vector3.Angle(transform.forward, toTarget);
            if (angle > 15f)
            {
                // Не стреляем пока не повернёмся
                return;
            }

            Vector3 direction = (targetPoint - spawnPos).normalized;
            Quaternion rotation = Quaternion.LookRotation(direction);

            GameObject bullet = Instantiate(bulletPrefab, spawnPos, rotation);
            ServerManager.Spawn(bullet);

            var bulletScript = bullet.GetComponent<Weapon.Projectile.Bullet>();
            if (bulletScript != null)
            {
                bulletScript.SetBotOwner(transform, _botId.Value);
                bulletScript.SetDamage(attackDamage);
            }

            if (bullet.TryGetComponent<Rigidbody>(out var rb))
            {
                rb.AddForce(direction * bulletForce, ForceMode.Impulse);
            }

            _attackTimer = attackCooldown;
        }

        // ==================
        // Detection
        // ==================

        private TurretPlant FindNearestWildPlant()
        {
            var plants = FindObjectsByType<TurretPlant>(FindObjectsSortMode.None);
            TurretPlant nearest = null;
            float nearestDist = float.MaxValue;

            foreach (var plant in plants)
            {
                if (plant == null) continue;

                // Ищем только дикие растения (даже если они атакуют)
                if (plant.State != TurretPlantState.Wild) continue;

                float dist = Vector3.Distance(transform.position, plant.transform.position);
                if (dist < nearestDist)
                {
                    nearest = plant;
                    nearestDist = dist;
                }
            }

            return nearest;
        }

        private Transform FindNearestEnemy()
        {
            Transform nearest = null;
            float nearestDist = float.MaxValue;

            // Ищем волновых врагов
            var waveEnemies = FindObjectsByType<WaveEnemy>(FindObjectsSortMode.None);
            foreach (var enemy in waveEnemies)
            {
                if (enemy == null || enemy.IsDead) continue;

                float dist = Vector3.Distance(transform.position, enemy.transform.position);
                if (dist < detectionRange && dist < nearestDist)
                {
                    nearest = enemy.transform;
                    nearestDist = dist;
                }
            }

            // Ищем нейтралов которые атакуют нас (агро на нас)
            var neutrals = FindObjectsByType<Neutral.Neutral>(FindObjectsSortMode.None);
            foreach (var neutral in neutrals)
            {
                if (neutral == null) continue;

                var health = neutral.GetComponent<Health>();
                if (health == null || health.IsDead) continue;

                float dist = Vector3.Distance(transform.position, neutral.transform.position);
                if (dist < detectionRange && dist < nearestDist)
                {
                    nearest = neutral.transform;
                    nearestDist = dist;
                }
            }

            return nearest;
        }

        /// <summary>
        /// Ищет охранников (нейтралов) возле растения
        /// </summary>
        private Transform FindGuardNearPlant(TurretPlant plant)
        {
            if (plant == null) return null;

            float guardRange = 8f; // Радиус поиска охранников возле растения
            var neutrals = FindObjectsByType<Neutral.Neutral>(FindObjectsSortMode.None);

            Transform nearest = null;
            float nearestDist = float.MaxValue;

            foreach (var neutral in neutrals)
            {
                if (neutral == null) continue;

                var health = neutral.GetComponent<Health>();
                if (health == null || health.IsDead) continue;

                float dist = Vector3.Distance(plant.transform.position, neutral.transform.position);
                if (dist < guardRange && dist < nearestDist)
                {
                    nearest = neutral.transform;
                    nearestDist = dist;
                }
            }

            return nearest;
        }

        private Transform FindThreatNearLifeFruit()
        {
            if (_myLifeFruit == null) return null;

            var enemies = FindObjectsByType<WaveEnemy>(FindObjectsSortMode.None);
            float threatRange = 10f;

            foreach (var enemy in enemies)
            {
                if (enemy == null || enemy.IsDead) continue;

                float dist = Vector3.Distance(_myLifeFruit.transform.position, enemy.transform.position);
                if (dist < threatRange)
                {
                    return enemy.transform;
                }
            }

            return null;
        }

        private bool IsEnemyValid(Transform enemy)
        {
            if (enemy == null) return false;

            var waveEnemy = enemy.GetComponent<WaveEnemy>();
            if (waveEnemy != null && waveEnemy.IsDead) return false;

            var health = enemy.GetComponent<Health>();
            if (health != null && health.IsDead) return false;

            return true;
        }

        private bool ShouldDefend()
        {
            return FindThreatNearLifeFruit() != null;
        }

        private bool CanPlantMoreTurrets()
        {
            if (TurretPlantManager.Instance == null) return true;
            return TurretPlantManager.Instance.CanPlantMore(_botId.Value);
        }

        private Vector3 GetPlantingPosition()
        {
            // Сажаем возле LifeFruit на расстоянии от minPlantDistance до maxPlantDistance
            if (_myLifeFruit != null)
            {
                // Случайное направление
                Vector2 randomDir = Random.insideUnitCircle.normalized;
                // Случайное расстояние в заданном диапазоне
                float distance = Random.Range(minPlantDistance, maxPlantDistance);

                Vector3 offset = new Vector3(randomDir.x, 0, randomDir.y) * distance;
                Vector3 targetPos = _myLifeFruit.transform.position + offset;

                // Проверяем что позиция на NavMesh
                if (NavMesh.SamplePosition(targetPos, out NavMeshHit hit, 2f, NavMesh.AllAreas))
                {
                    return hit.position;
                }

                return targetPos;
            }

            // Fallback - возле дома
            Vector3 fallbackOffset = Random.insideUnitSphere * maxPlantDistance;
            fallbackOffset.y = 0;
            return _homePosition + fallbackOffset;
        }

        /// <summary>
        /// Ищет ближайшего игрока в радиусе агро
        /// </summary>
        private Transform FindNearestPlayer()
        {
            // Ищем игроков через их NetworkObject
            var players = FindObjectsByType<FishNet.Object.NetworkObject>(FindObjectsSortMode.None);
            Transform nearest = null;
            float nearestDist = float.MaxValue;

            foreach (var netObj in players)
            {
                // Проверяем это игрок (имеет Owner и не бот)
                if (netObj.OwnerId < 0) continue;

                // Пропускаем ботов (у ботов нет реального владельца)
                var botPlayer = netObj.GetComponent<BotPlayer>();
                if (botPlayer != null) continue;

                // Проверяем наличие Health компонента
                var health = netObj.GetComponent<Health>();
                if (health == null || health.IsDead) continue;

                // Проверяем это не наш LifeFruit
                var lifeFruit = netObj.GetComponent<LifeFruit>();
                if (lifeFruit != null) continue;

                // Проверяем это не турель
                var turret = netObj.GetComponent<TurretPlant>();
                if (turret != null) continue;

                float dist = Vector3.Distance(transform.position, netObj.transform.position);
                if (dist <= playerAggroRange && dist < nearestDist)
                {
                    nearest = netObj.transform;
                    nearestDist = dist;
                }
            }

            return nearest;
        }

        /// <summary>
        /// Пытается начать рейд на вражескую базу
        /// </summary>
        private bool TryStartRaid()
        {
            // Ищем вражеские LifeFruit (не наш)
            var lifeFruits = FindObjectsByType<LifeFruit>(FindObjectsSortMode.None);
            LifeFruit targetFruit = null;
            float nearestDist = float.MaxValue;

            foreach (var fruit in lifeFruits)
            {
                if (fruit == null || fruit == _myLifeFruit) continue;

                var health = fruit.GetComponent<Health>();
                if (health != null && health.IsDead) continue;

                float dist = Vector3.Distance(transform.position, fruit.transform.position);
                if (dist < nearestDist)
                {
                    targetFruit = fruit;
                    nearestDist = dist;
                }
            }

            if (targetFruit != null)
            {
                _raidTarget = targetFruit;
                _raidTurretTarget = FindEnemyTurretNear(targetFruit);
                return true;
            }

            return false;
        }

        /// <summary>
        /// Ищет вражеские турели рядом с целевым LifeFruit
        /// </summary>
        private TurretPlant FindEnemyTurretNear(LifeFruit target)
        {
            if (target == null) return null;

            float searchRange = 15f;
            var turrets = FindObjectsByType<TurretPlant>(FindObjectsSortMode.None);
            TurretPlant nearest = null;
            float nearestDist = float.MaxValue;

            foreach (var turret in turrets)
            {
                if (turret == null) continue;
                if (turret.State != TurretPlantState.Planted) continue;

                // Не атакуем свои турели
                if (turret.PlantedOwnerId == _botId.Value) continue;

                var health = turret.GetComponent<Health>();
                if (health != null && health.IsDead) continue;

                float distToTarget = Vector3.Distance(target.transform.position, turret.transform.position);
                if (distToTarget > searchRange) continue;

                float distToBot = Vector3.Distance(transform.position, turret.transform.position);
                if (distToBot < nearestDist)
                {
                    nearest = turret;
                    nearestDist = distToBot;
                }
            }

            return nearest;
        }

        /// <summary>
        /// Проверяет валидность целевой турели для рейда
        /// </summary>
        private bool IsRaidTurretValid(TurretPlant turret)
        {
            if (turret == null) return false;
            if (turret.State != TurretPlantState.Planted) return false;

            var health = turret.GetComponent<Health>();
            if (health != null && health.IsDead) return false;

            return true;
        }

        // ==================
        // Helpers
        // ==================

        private void RotateTowards(Vector3 target)
        {
            Vector3 dir = (target - transform.position).normalized;
            dir.y = 0;

            if (dir != Vector3.zero)
            {
                Quaternion rot = Quaternion.LookRotation(dir);
                transform.rotation = Quaternion.Slerp(transform.rotation, rot, Time.deltaTime * 10f);
            }
        }

        private void OnBotDeath()
        {
            // Бросаем растение если несли
            if (_carriedPlant != null)
            {
                _carriedPlant.Drop(transform.position);
                _carriedPlant = null;
            }

            // Останавливаем агента
            if (_agent != null)
            {
                _agent.ResetPath();
                _agent.isStopped = true;
            }

            // Останавливаем анимацию
            if (_animator != null)
            {
                _animator.SetFloat(_moveXHash, 0f);
                _animator.SetFloat(_moveYHash, 0f);
                _animator.SetFloat(_moveSpeedHash, 0f);
            }
        }

        private void OnBotRevive()
        {
            // Сбрасываем состояние
            _state.Value = BotState.Idle;
            _targetPlant = null;
            _targetEnemy = null;
            _carriedPlant = null;
            _raidTarget = null;
            _raidTurretTarget = null;

            // Сбрасываем таймеры
            _thinkTimer = 0f;
            _actionTimer = 0f;
            _attackTimer = 0f;
            _raidCheckTimer = raidCheckInterval;

            // Восстанавливаем агента
            if (_agent != null)
            {
                _agent.isStopped = false;
                _agent.speed = moveSpeed;
            }

            // Сбрасываем анимацию
            _animMoveX = 0f;
            _animMoveY = 0f;
        }

        // ==================
        // Animation
        // ==================

        private void UpdateAnimations()
        {
            if (_animator == null) return;

            // Получаем направление движения в локальных координатах (как у игрока)
            Vector3 velocity = _agent.velocity;
            Vector3 localVelocity = transform.InverseTransformDirection(velocity);

            // Нормализуем для анимации
            float targetMoveX = Mathf.Clamp(localVelocity.x / moveSpeed, -1f, 1f);
            float targetMoveY = Mathf.Clamp(localVelocity.z / moveSpeed, -1f, 1f);

            // Плавно интерполируем
            _animMoveX = Mathf.Lerp(_animMoveX, targetMoveX, Time.deltaTime * _animationSmooth);
            _animMoveY = Mathf.Lerp(_animMoveY, targetMoveY, Time.deltaTime * _animationSmooth);

            // Устанавливаем параметры анимации (такие же как у игрока)
            _animator.SetFloat(_moveXHash, _animMoveX);
            _animator.SetFloat(_moveYHash, _animMoveY);
            _animator.SetFloat(_moveSpeedHash, velocity.magnitude / moveSpeed);
        }

        // ==================
        // Gizmos
        // ==================

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(transform.position, detectionRange);

            Gizmos.color = Color.red;
            Gizmos.DrawWireSphere(transform.position, attackRange);

            Gizmos.color = Color.cyan;
            Gizmos.DrawWireSphere(transform.position, interactRange);
        }
    }
}
