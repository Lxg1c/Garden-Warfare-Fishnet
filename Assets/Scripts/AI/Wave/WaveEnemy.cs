using Core.Components;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using Gameplay;
using Gameplay.TurretPlant; // TurretBullet и TurretPlant
using UnityEngine;
using UnityEngine.AI;
using Weapon.Projectile;

namespace AI.Wave
{
    public enum WaveEnemyState
    {
        AttackingLifeFruit, // Атакует LifeFruit (основная цель)
        ChasingCarrier,     // Преследует игрока несущего LifeFruit
        Aggro,              // Агрится на того кто его ударил
        Idle                // LifeFruit уничтожен - стоит на месте, агрится на ближайших
    }

    [RequireComponent(typeof(NavMeshAgent))]
    [RequireComponent(typeof(Health))]
    public class WaveEnemy : NetworkBehaviour
    {
        [Header("Settings")]
        [SerializeField] private float attackRange = 2f;
        [SerializeField] private float attackCooldown = 1f;
        [SerializeField] private int attackDamage = 10;

        [Header("Movement")]
        [SerializeField] private float moveSpeed = 3.5f;

        [Header("Aggro Settings")]
        [SerializeField] private float aggroDuration = 5f; // Сколько времени атаковать обидчика
        [SerializeField] private float idleAggroRange = 5f; // Радиус агро в режиме Idle (когда LifeFruit уничтожен)

        [Header("Network Sync")]
        [SerializeField] private float positionSyncRate = 0.1f;

        [Header("Player Detection")]
        [SerializeField] private LayerMask playerLayer;

        // ==================
        // Синхронизация
        // ==================

        private readonly SyncVar<int> _syncTargetPlayerId = new(-1);
        private readonly SyncVar<bool> _syncIsDead = new(false);

        // ==================
        // Локальные данные
        // ==================

        private Health _health;
        private NavMeshAgent _agent;

        private Transform _targetLifeFruit;
        private LifeFruit _targetLifeFruitComponent; // Кэш компонента LifeFruit
        private Transform _currentTarget; // Текущая цель (LifeFruit, игрок или турель)
        private WaveEnemyState _state = WaveEnemyState.AttackingLifeFruit;
        private float _attackTimer;
        private float _aggroTimer;
        private WaveManager _waveManager;
        private float _lastSyncTime;
        private float _lastTargetSearchTime;

        // ==================
        // Properties
        // ==================

        public int TargetPlayerId => _syncTargetPlayerId.Value;
        public bool IsDead => _syncIsDead.Value;

        // ==================
        // Lifecycle
        // ==================

        private void Awake()
        {
            _agent = GetComponent<NavMeshAgent>();
            _health = GetComponent<Health>();
        }

        public override void OnStartNetwork()
        {
            base.OnStartNetwork();

            if (_health != null)
            {
                _health.OnDamaged += OnDamaged;
                _health.OnDeath += OnDeath;
            }
        }

        public override void OnStopNetwork()
        {
            base.OnStopNetwork();

            if (_health != null)
            {
                _health.OnDamaged -= OnDamaged;
                _health.OnDeath -= OnDeath;
            }
        }

        public override void OnStartClient()
        {
            base.OnStartClient();

            // На клиентах отключаем NavMeshAgent - позиция синхронизируется с сервера
            if (!IsServerInitialized && _agent != null)
            {
                _agent.enabled = false;
            }
        }

        private void Update()
        {
            if (!IsServerStarted) return;
            if (_syncIsDead.Value) return;

            // Проверяем что NavMeshAgent активен и на NavMesh
            if (_agent == null || !_agent.isOnNavMesh) return;

            // Обновляем состояние
            UpdateState();

            // Выполняем логику в зависимости от состояния
            switch (_state)
            {
                case WaveEnemyState.AttackingLifeFruit:
                    UpdateAttackingLifeFruit();
                    break;
                case WaveEnemyState.ChasingCarrier:
                    UpdateChasingCarrier();
                    break;
                case WaveEnemyState.Aggro:
                    UpdateAggro();
                    break;
                case WaveEnemyState.Idle:
                    UpdateIdle();
                    break;
            }

            // Синхронизируем позицию на клиентах
            if (Time.time - _lastSyncTime >= positionSyncRate)
            {
                _lastSyncTime = Time.time;
                SyncPositionObserversRpc(transform.position, transform.rotation);
            }
        }

        private void UpdateState()
        {
            // Проверяем жив ли целевой LifeFruit и несут ли его
            bool lifeFruitAlive = IsLifeFruitAlive();
            bool lifeFruitCarried = IsLifeFruitBeingCarried();

            // Если LifeFruit несут - переключаемся на преследование носителя
            if (lifeFruitCarried && _state != WaveEnemyState.ChasingCarrier && _state != WaveEnemyState.Aggro)
            {
                Transform carrier = GetLifeFruitCarrier();
                if (carrier != null)
                {
                    _state = WaveEnemyState.ChasingCarrier;
                    _currentTarget = carrier;
                }
            }

            // Если несли но уже не несут - возвращаемся к атаке LifeFruit
            if (!lifeFruitCarried && _state == WaveEnemyState.ChasingCarrier)
            {
                if (lifeFruitAlive)
                {
                    _state = WaveEnemyState.AttackingLifeFruit;
                    _currentTarget = _targetLifeFruit;
                }
                else
                {
                    _state = WaveEnemyState.Idle;
                    _currentTarget = null;
                }
            }

            // Если в режиме агро - проверяем таймер
            if (_state == WaveEnemyState.Aggro)
            {
                _aggroTimer -= Time.deltaTime;
                if (_aggroTimer <= 0f || _currentTarget == null || IsTargetDead(_currentTarget))
                {
                    // Возвращаемся к атаке LifeFruit или преследованию носителя
                    if (lifeFruitCarried)
                    {
                        Transform carrier = GetLifeFruitCarrier();
                        if (carrier != null)
                        {
                            _state = WaveEnemyState.ChasingCarrier;
                            _currentTarget = carrier;
                        }
                    }
                    else if (lifeFruitAlive)
                    {
                        _state = WaveEnemyState.AttackingLifeFruit;
                        _currentTarget = _targetLifeFruit;
                    }
                    else
                    {
                        _state = WaveEnemyState.Idle;
                        _currentTarget = null;
                    }
                }
            }

            // Если LifeFruit уничтожен и мы не в агро - переходим в Idle
            if (!lifeFruitAlive && !lifeFruitCarried && _state == WaveEnemyState.AttackingLifeFruit)
            {
                _state = WaveEnemyState.Idle;
                _currentTarget = null;
            }
        }

        /// <summary>
        /// Проверяет несут ли целевой LifeFruit
        /// </summary>
        private bool IsLifeFruitBeingCarried()
        {
            if (_targetLifeFruitComponent == null)
            {
                CacheLifeFruitComponent();
            }

            return _targetLifeFruitComponent != null && _targetLifeFruitComponent.State == LifeFruitState.Carried;
        }

        /// <summary>
        /// Получает Transform игрока который несёт LifeFruit
        /// </summary>
        private Transform GetLifeFruitCarrier()
        {
            if (_targetLifeFruitComponent == null)
            {
                CacheLifeFruitComponent();
            }

            if (_targetLifeFruitComponent == null || _targetLifeFruitComponent.State != LifeFruitState.Carried)
            {
                return null;
            }

            int carrierId = _targetLifeFruitComponent.CarrierId;
            if (carrierId < 0) return null;

            // Находим игрока-носителя
            if (NetworkManager.ServerManager.Clients.TryGetValue(carrierId, out var conn))
            {
                return conn.FirstObject?.transform;
            }

            return null;
        }

        /// <summary>
        /// Кэширует компонент LifeFruit
        /// </summary>
        private void CacheLifeFruitComponent()
        {
            if (_targetLifeFruit == null) return;

            _targetLifeFruitComponent = _targetLifeFruit.GetComponent<LifeFruit>();
            if (_targetLifeFruitComponent == null)
            {
                _targetLifeFruitComponent = _targetLifeFruit.GetComponentInParent<LifeFruit>();
            }
        }

        /// <summary>
        /// Проверяет жив ли целевой LifeFruit
        /// </summary>
        private bool IsLifeFruitAlive()
        {
            if (_targetLifeFruit == null) return false;

            var lifeFruit = _targetLifeFruit.GetComponent<LifeFruit>();
            if (lifeFruit == null)
            {
                // Возможно LifeFruit на родительском объекте
                lifeFruit = _targetLifeFruit.GetComponentInParent<LifeFruit>();
            }

            return lifeFruit != null && lifeFruit.IsAlive;
        }

        private void UpdateAttackingLifeFruit()
        {
            if (!IsLifeFruitAlive())
            {
                _state = WaveEnemyState.Idle;
                _currentTarget = null;
                return;
            }

            _currentTarget = _targetLifeFruit;
            ChaseAndAttack(_currentTarget);
        }

        /// <summary>
        /// Преследует игрока который несёт LifeFruit
        /// </summary>
        private void UpdateChasingCarrier()
        {
            // Проверяем что LifeFruit всё ещё несут
            if (!IsLifeFruitBeingCarried())
            {
                // LifeFruit больше не несут - переключаемся обратно
                if (IsLifeFruitAlive())
                {
                    _state = WaveEnemyState.AttackingLifeFruit;
                    _currentTarget = _targetLifeFruit;
                }
                else
                {
                    _state = WaveEnemyState.Idle;
                    _currentTarget = null;
                }
                return;
            }

            // Обновляем цель (позиция носителя могла измениться)
            Transform carrier = GetLifeFruitCarrier();
            if (carrier == null || IsTargetDead(carrier))
            {
                _state = WaveEnemyState.Idle;
                _currentTarget = null;
                return;
            }

            _currentTarget = carrier;
            ChaseAndAttack(_currentTarget);
        }

        private void UpdateAggro()
        {
            if (_currentTarget == null || IsTargetDead(_currentTarget))
            {
                // Цель мертва - возвращаемся
                if (IsLifeFruitAlive())
                {
                    _state = WaveEnemyState.AttackingLifeFruit;
                    _currentTarget = _targetLifeFruit;
                }
                else
                {
                    _state = WaveEnemyState.Idle;
                    _currentTarget = null;
                }
                return;
            }

            ChaseAndAttack(_currentTarget);
        }

        /// <summary>
        /// Режим Idle: моб стоит на месте и атакует только тех, кто подходит близко или атакует его
        /// </summary>
        private void UpdateIdle()
        {
            // Стоим на месте
            if (_agent.isOnNavMesh)
            {
                _agent.isStopped = true;
            }

            // Ищем цель в малом радиусе (только для атаки, не для погони)
            if (Time.time - _lastTargetSearchTime > 0.5f)
            {
                _lastTargetSearchTime = Time.time;
                _currentTarget = FindNearestTargetInRange(idleAggroRange);
            }

            // Если кто-то в радиусе атаки - атакуем его (но не гонимся)
            if (_currentTarget != null)
            {
                float dist = Vector3.Distance(transform.position, _currentTarget.position);
                if (dist <= attackRange)
                {
                    RotateTowards(_currentTarget.position);

                    _attackTimer -= Time.deltaTime;
                    if (_attackTimer <= 0f)
                    {
                        AttackTarget(_currentTarget);
                        _attackTimer = attackCooldown;
                    }
                }
                else if (dist > idleAggroRange)
                {
                    // Цель вышла из радиуса - забываем о ней
                    _currentTarget = null;
                }
            }
        }

        /// <summary>
        /// Находит ближайшую цель (игрока или турель) в указанном радиусе
        /// </summary>
        private Transform FindNearestTargetInRange(float range)
        {
            Collider[] hits = Physics.OverlapSphere(transform.position, range, playerLayer);

            Transform closest = null;
            float closestDist = float.MaxValue;

            foreach (var c in hits)
            {
                if (c.transform == transform) continue;

                // Проверяем что это игрок или турель
                bool isPlayer = c.GetComponent<CharacterController>() != null;
                bool isTurret = c.GetComponent<TurretPlant>() != null;

                if (!isPlayer && !isTurret) continue;

                // Проверяем здоровье
                Health h = c.GetComponent<Health>();
                if (h == null || h.IsDead || h.GetHealth() <= 0) continue;

                float dist = Vector3.Distance(transform.position, c.transform.position);
                if (dist < closestDist)
                {
                    closest = c.transform;
                    closestDist = dist;
                }
            }

            return closest;
        }

        private void ChaseAndAttack(Transform target)
        {
            if (target == null) return;

            float dist = Vector3.Distance(transform.position, target.position);

            // Останавливаемся на дистанции атаки (с небольшим запасом)
            float stopDistance = attackRange * 0.8f;

            if (dist <= attackRange)
            {
                // Атакуем - полностью останавливаемся
                if (_agent.isOnNavMesh)
                {
                    _agent.isStopped = true;
                    _agent.ResetPath();
                    _agent.velocity = Vector3.zero;
                }
                RotateTowards(target.position);

                _attackTimer -= Time.deltaTime;
                if (_attackTimer <= 0f)
                {
                    AttackTarget(target);
                    _attackTimer = attackCooldown;
                }
            }
            else
            {
                // Идём к цели, но не ближе чем stopDistance
                if (_agent.isOnNavMesh)
                {
                    _agent.isStopped = false;
                    _agent.stoppingDistance = stopDistance;
                    _agent.SetDestination(target.position);
                }
            }
        }

        private bool IsTargetDead(Transform target)
        {
            if (target == null) return true;

            Health targetHealth = target.GetComponent<Health>();
            if (targetHealth != null)
            {
                return targetHealth.IsDead || targetHealth.GetHealth() <= 0;
            }

            return false;
        }

        [ObserversRpc(ExcludeServer = true)]
        private void SyncPositionObserversRpc(Vector3 position, Quaternion rotation)
        {
            // Плавная интерполяция на клиентах
            transform.position = Vector3.Lerp(transform.position, position, 0.5f);
            transform.rotation = Quaternion.Slerp(transform.rotation, rotation, 0.5f);
        }

        // ==================
        // Initialization
        // ==================

        /// <summary>
        /// Инициализирует врага с целью (LifeFruit игрока)
        /// </summary>
        [Server]
        public void Initialize(int targetPlayerId, Transform lifeFruitTransform, WaveManager manager)
        {
            _syncTargetPlayerId.Value = targetPlayerId;
            _targetLifeFruit = lifeFruitTransform;
            _currentTarget = lifeFruitTransform; // Сразу устанавливаем текущую цель
            _waveManager = manager;
            _state = WaveEnemyState.AttackingLifeFruit;

            if (_agent != null)
            {
                if (_agent.isOnNavMesh)
                {
                    _agent.speed = moveSpeed;
                    _agent.stoppingDistance = attackRange * 0.8f; // Останавливаемся на дистанции атаки
                    _agent.isStopped = false;

                    // Сразу задаём цель движения
                    if (lifeFruitTransform != null)
                    {
                        _agent.SetDestination(lifeFruitTransform.position);
                    }
                }
            }
        }

        /// <summary>
        /// Применяет модификаторы волны (сложность)
        /// </summary>
        [Server]
        public void ApplyWaveModifiers(float healthMultiplier, float damageMultiplier, float speedMultiplier)
        {
            if (_health != null)
            {
                float newMaxHealth = _health.GetMaxHealth() * healthMultiplier;
                _health.SetHealth(newMaxHealth);
            }

            attackDamage = Mathf.RoundToInt(attackDamage * damageMultiplier);

            if (_agent != null)
            {
                _agent.speed = moveSpeed * speedMultiplier;
            }

            Debug.Log($"[WaveEnemy] Modifiers applied: HP x{healthMultiplier}, DMG x{damageMultiplier}, SPD x{speedMultiplier}");
        }

        // ==================
        // Combat
        // ==================

        private void AttackTarget(Transform target)
        {
            if (target == null) return;

            // Ищем Health на объекте или на родителях (для случая когда цель - дочерний объект)
            Health targetHealth = target.GetComponent<Health>();
            if (targetHealth == null)
            {
                targetHealth = target.GetComponentInParent<Health>();
            }

            if (targetHealth != null)
            {
                targetHealth.TakeDamage(attackDamage, transform, NetworkObject);
                Debug.Log($"[WaveEnemy] Attacked {targetHealth.name} for {attackDamage} damage");
            }
        }

        private void RotateTowards(Vector3 pos)
        {
            Vector3 dir = (pos - transform.position).normalized;
            dir.y = 0;

            if (dir != Vector3.zero)
            {
                Quaternion rot = Quaternion.LookRotation(dir);
                transform.rotation = Quaternion.Slerp(transform.rotation, rot, Time.deltaTime * 5f);
            }
        }

        // ==================
        // Damage Handling
        // ==================

        private void OnDamaged(Transform attacker)
        {
            if (!IsServerStarted) return;
            if (attacker == null) return;

            // Игнорируем урон от других WaveEnemy
            if (attacker.GetComponent<WaveEnemy>() != null) return;

            // Проверяем что атакующий - игрок или турель
            bool isPlayer = attacker.GetComponent<CharacterController>() != null;
            bool isTurret = attacker.GetComponent<TurretPlant>() != null;

            // Если урон от турели - ищем её владельца (игрока)
            if (isTurret)
            {
                var turret = attacker.GetComponent<TurretPlant>();
                // Турель атаковала нас - агримся на турель
                SetAggro(attacker);
                Debug.Log($"[WaveEnemy] Aggro on turret {attacker.name}");
                return;
            }

            // Если урон от игрока - агримся на него
            if (isPlayer)
            {
                SetAggro(attacker);
                Debug.Log($"[WaveEnemy] Aggro on player {attacker.name}");
                return;
            }

            // Если урон от снаряда турели - ищем владельца снаряда
            var bullet = attacker.GetComponent<Bullet>();
            if (bullet != null)
            {
                Transform bulletOwner = bullet.GetOwnerTransform();
                if (bulletOwner != null)
                {
                    SetAggro(bulletOwner);
                    Debug.Log($"[WaveEnemy] Aggro on bullet owner {bulletOwner.name}");
                }
            }
        }

        /// <summary>
        /// Устанавливает агро на цель
        /// </summary>
        private void SetAggro(Transform target)
        {
            if (target == null) return;

            _state = WaveEnemyState.Aggro;
            _currentTarget = target;
            _aggroTimer = aggroDuration;
        }

        private void OnDeath()
        {
            if (!IsServerStarted) return;

            _syncIsDead.Value = true;
            _agent.isStopped = true;

            // Уведомляем WaveManager
            if (_waveManager != null)
            {
                _waveManager.OnEnemyDied(this);
            }

            Debug.Log($"[WaveEnemy] Died");

            // Деспавн через задержку
            StartCoroutine(DelayedDespawn());
        }

        private System.Collections.IEnumerator DelayedDespawn()
        {
            yield return new WaitForSeconds(2f);

            if (NetworkObject != null && NetworkObject.IsSpawned)
            {
                ServerManager.Despawn(NetworkObject);
            }
        }

        // ==================
        // Gizmos
        // ==================

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.red;
            Gizmos.DrawWireSphere(transform.position, attackRange);

            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(transform.position, idleAggroRange);
        }
    }
}
