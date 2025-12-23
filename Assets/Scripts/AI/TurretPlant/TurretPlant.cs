using System;
using System.Collections;
using AI.Wave;
using Core.Components;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;
using Weapon.Projectile;

namespace Gameplay.TurretPlant
{
    [RequireComponent(typeof(Health))]
    public class TurretPlant : NetworkBehaviour
    {
        [Header("Detection Settings")]
        [SerializeField] private float detectionRange = 10f;
        [SerializeField] private float attackRange = 8f;
        [SerializeField] private LayerMask playerLayer;
        [SerializeField] private LayerMask enemyLayer; // Слой для волновых мобов

        [Header("Attack Settings")]
        [SerializeField] private float attackCooldown = 0.5f; // Быстрая стрельба
        [SerializeField] private int attackDamage = 15;

        [Header("Wild Mode Settings")]
        [SerializeField] private float wildChaseRange = 6f;
        [SerializeField] private float wildReturnTimer = 3f;

        [Header("Projectile")]
        [SerializeField] private GameObject bulletPrefab;
        [SerializeField] private Transform shootPoint;
        [SerializeField] private float bulletForce = 15f;

        // ==================
        // Синхронизированные данные
        // ==================

        private readonly SyncVar<TurretPlantState> _state = new(TurretPlantState.Wild);
        private readonly SyncVar<int> _plantedOwnerId = new(-1);
        private readonly SyncVar<int> _carrierId = new(-1);

        // ==================
        // Локальные данные
        // ==================

        private Health _health;
        private Vector3 _homePosition;
        private Transform _wildTarget;
        private Transform _currentTarget;
        private float _attackTimer;
        private float _chaseTimer;
        private WildSubState _wildSubState = WildSubState.Idle;

        private Collider _collider;
        private PlantSpawner _spawner; // Спавнер который создал это растение

        // ==================
        // Events
        // ==================

        public event Action OnPickedUp;
        public event Action OnDropped;
        public event Action OnPlanted;

        // ==================
        // Properties
        // ==================

        public TurretPlantState State => _state.Value;
        public int PlantedOwnerId => _plantedOwnerId.Value;
        public int CarrierId => _carrierId.Value;

        /// <summary>
        /// Можно выкопать только если растение дикое И в состоянии Idle (не атакует)
        /// </summary>
        public bool CanBePickedUp => _state.Value == TurretPlantState.Wild && _wildSubState == WildSubState.Idle;

        // ==================
        // Wild SubStates
        // ==================

        private enum WildSubState
        {
            Idle,
            Chasing,
            Attacking,
            Returning
        }

        // ==================
        // Lifecycle
        // ==================

        private void Awake()
        {
            _health = GetComponent<Health>();
            _collider = GetComponent<Collider>();
            _homePosition = transform.position;
        }

        public override void OnStartNetwork()
        {
            base.OnStartNetwork();

            _state.OnChange += OnStateChanged;

            if (_health != null)
            {
                _health.OnDamaged += OnDamaged;
                _health.OnDeath += OnDeath;
            }
        }

        public override void OnStopNetwork()
        {
            base.OnStopNetwork();

            _state.OnChange -= OnStateChanged;

            if (_health != null)
            {
                _health.OnDamaged -= OnDamaged;
                _health.OnDeath -= OnDeath;
            }
        }

        private void Update()
        {
            if (!IsServerStarted) return;

            switch (_state.Value)
            {
                case TurretPlantState.Wild:
                    WildUpdate();
                    break;
                case TurretPlantState.Carried:
                    // Логика переноски в PlayerCarryController
                    break;
                case TurretPlantState.Planted:
                    PlantedUpdate();
                    break;
            }
        }

        // ==================
        // STATE: WILD
        // ==================

        private void WildUpdate()
        {
            switch (_wildSubState)
            {
                case WildSubState.Idle:
                    // Ничего не делает, ждёт атаки
                    break;

                case WildSubState.Chasing:
                    WildChasingUpdate();
                    break;

                case WildSubState.Attacking:
                    WildAttackingUpdate();
                    break;

                case WildSubState.Returning:
                    WildReturningUpdate();
                    break;
            }
        }

        private void WildChasingUpdate()
        {
            if (_wildTarget == null)
            {
                StartWildReturn();
                return;
            }

            float dist = Vector3.Distance(transform.position, _wildTarget.position);

            if (dist <= attackRange)
            {
                SetWildSubState(WildSubState.Attacking);
                return;
            }

            if (dist > wildChaseRange)
            {
                StartWildReturn();
                return;
            }

            _chaseTimer -= Time.deltaTime;
            if (_chaseTimer <= 0f)
            {
                StartWildReturn();
                return;
            }

            // Растение не двигается - только поворачивается
            RotateTowards(_wildTarget.position);
        }

        private void WildAttackingUpdate()
        {
            if (_wildTarget == null)
            {
                StartWildReturn();
                return;
            }

            // Проверяем что цель ещё жива
            Health targetHealth = _wildTarget.GetComponent<Health>();
            if (targetHealth == null || targetHealth.IsDead || targetHealth.GetHealth() <= 0)
            {
                StartWildReturn();
                return;
            }

            float dist = Vector3.Distance(transform.position, _wildTarget.position);

            if (dist > attackRange)
            {
                SetWildSubState(WildSubState.Chasing);
                return;
            }

            // Уменьшаем таймер - через время прекращаем атаковать
            _chaseTimer -= Time.deltaTime;
            if (_chaseTimer <= 0f)
            {
                StartWildReturn();
                return;
            }

            RotateTowards(_wildTarget.position);

            _attackTimer -= Time.deltaTime;
            if (_attackTimer <= 0f)
            {
                ShootAt(_wildTarget);
                _attackTimer = attackCooldown;
            }
        }

        private void WildReturningUpdate()
        {
            // Растение не двигается, просто переходит в Idle
            SetWildSubState(WildSubState.Idle);
            _wildTarget = null;

            // Восстанавливаем здоровье
            if (_health != null)
            {
                _health.SetHealth(_health.GetMaxHealth());
            }
        }

        private void StartWildReturn()
        {
            SetWildSubState(WildSubState.Returning);
        }

        private void SetWildSubState(WildSubState newState)
        {
            _wildSubState = newState;

            switch (newState)
            {
                case WildSubState.Idle:
                    _wildTarget = null;
                    break;
                case WildSubState.Chasing:
                    _chaseTimer = wildReturnTimer;
                    break;
                case WildSubState.Attacking:
                    _attackTimer = 0f;
                    break;
            }
        }

        // ==================
        // STATE: PLANTED
        // ==================

        private void PlantedUpdate()
        {
            // Поиск цели (враги владельца)
            if (_currentTarget == null || !IsValidTarget(_currentTarget))
            {
                _currentTarget = FindPlantedTarget();
            }

            if (_currentTarget == null) return;

            float dist = Vector3.Distance(transform.position, _currentTarget.position);

            if (dist > attackRange)
            {
                _currentTarget = null;
                return;
            }

            RotateTowards(_currentTarget.position);

            _attackTimer -= Time.deltaTime;
            if (_attackTimer <= 0f)
            {
                ShootAt(_currentTarget);
                _attackTimer = attackCooldown;
            }
        }

        private float _lastTargetSearchTime;

        private Transform FindPlantedTarget()
        {
            // Комбинируем слои игроков и врагов
            LayerMask combinedLayers = playerLayer | enemyLayer;

            // Ищем других игроков (не владельца) И всех врагов волн
            Collider[] hits = Physics.OverlapSphere(transform.position, detectionRange, combinedLayers);

            // Дебаг каждые 2 секунды
            if (Time.time - _lastTargetSearchTime > 2f)
            {
                _lastTargetSearchTime = Time.time;
                Debug.Log($"[TurretPlant] FindPlantedTarget: found {hits.Length} colliders in range {detectionRange}, owner={_plantedOwnerId.Value}, layers={combinedLayers.value}");
            }

            // ПРИОРИТЕТ 1: Ищем волновых врагов (они всегда приоритетнее игроков)
            Transform closestWaveEnemy = null;
            float closestWaveEnemyDist = float.MaxValue;

            // ПРИОРИТЕТ 2: Ищем игроков
            Transform closestPlayer = null;
            float closestPlayerDist = float.MaxValue;

            foreach (var c in hits)
            {
                // Пропускаем себя
                if (c.transform == transform) continue;

                // Проверяем волновых врагов - атакуем ВСЕХ (они враги всех игроков)
                WaveEnemy waveEnemy = c.GetComponent<WaveEnemy>();
                if (waveEnemy != null)
                {
                    // Атакуем всех живых волновых врагов
                    if (!waveEnemy.IsDead)
                    {
                        Health h = c.GetComponent<Health>();
                        if (h != null && h.GetHealth() > 0 && !h.IsDead && CanSee(c.transform))
                        {
                            float dist = Vector3.Distance(transform.position, c.transform.position);
                            if (dist < closestWaveEnemyDist)
                            {
                                closestWaveEnemy = c.transform;
                                closestWaveEnemyDist = dist;
                            }
                        }
                    }
                    continue;
                }

                // Проверяем что это не нейтрал
                if (c.GetComponent<AI.Neutral.Neutral>() != null) continue;

                // Проверяем что это игрок (имеет CharacterController или тег Player)
                bool isPlayer = c.GetComponent<CharacterController>() != null || c.CompareTag("Player");
                if (!isPlayer) continue;

                NetworkObject netObj = c.GetComponent<NetworkObject>();

                // Пропускаем владельца
                if (netObj != null && netObj.OwnerId == _plantedOwnerId.Value) continue;

                // Проверяем здоровье
                Health h2 = c.GetComponent<Health>();
                if (h2 == null || h2.GetHealth() <= 0 || h2.IsDead) continue;

                // Проверяем видимость
                if (!CanSee(c.transform)) continue;

                float dist2 = Vector3.Distance(transform.position, c.transform.position);
                if (dist2 < closestPlayerDist)
                {
                    closestPlayer = c.transform;
                    closestPlayerDist = dist2;
                    Debug.Log($"[TurretPlant] Found player target: {c.name}, dist={dist2:F1}");
                }
            }

            // Возвращаем волнового врага если есть, иначе игрока
            if (closestWaveEnemy != null)
            {
                return closestWaveEnemy;
            }

            return closestPlayer;
        }

        private bool IsValidTarget(Transform target)
        {
            if (target == null) return false;

            Health h = target.GetComponent<Health>();
            if (h == null || h.GetHealth() <= 0 || h.IsDead) return false;

            // Проверяем волновых врагов - атакуем всех живых
            WaveEnemy waveEnemy = target.GetComponent<WaveEnemy>();
            if (waveEnemy != null)
            {
                if (waveEnemy.IsDead) return false;
                // Все волновые враги - валидные цели
            }
            else
            {
                // Для других целей - не атакуем владельца
                NetworkObject netObj = target.GetComponent<NetworkObject>();
                if (netObj != null && netObj.OwnerId == _plantedOwnerId.Value) return false;
            }

            float dist = Vector3.Distance(transform.position, target.position);
            return dist <= detectionRange;
        }

        private bool CanSee(Transform target)
        {
            Vector3 dir = target.position - transform.position;
            if (Physics.Raycast(transform.position + Vector3.up * 0.5f, dir.normalized, out var hit, dir.magnitude))
            {
                return hit.transform == target;
            }
            return true;
        }

        // ==================
        // SHOOTING
        // ==================

        [Server]
        private void ShootAt(Transform target)
        {
            if (bulletPrefab == null) return;

            Transform spawnPoint = shootPoint != null ? shootPoint : transform;

            // Целимся в центр массы врага
            Vector3 targetPoint = target.position;
            Vector3 spawnPos = spawnPoint.position;

            Vector3 direction = (targetPoint - spawnPos).normalized;
            Quaternion rotation = Quaternion.LookRotation(direction);

            GameObject bullet = Instantiate(bulletPrefab, spawnPos, rotation);

            ServerManager.Spawn(bullet);

            // Настраиваем снаряд (используем универсальный Bullet)
            var bulletScript = bullet.GetComponent<Bullet>();
            if (bulletScript != null)
            {
                bulletScript.SetOwner(transform, _plantedOwnerId.Value);
                bulletScript.SetDamage(attackDamage);
            }

            if (bullet.TryGetComponent<Rigidbody>(out var rb))
            {
                rb.AddForce(direction * bulletForce, ForceMode.Impulse);
            }

            // Визуал выстрела на всех клиентах
            ShootObserversRpc(spawnPoint.position, direction);
        }

        [ObserversRpc]
        private void ShootObserversRpc(Vector3 position, Vector3 direction)
        {
            // Визуальные эффекты выстрела (партиклы, звук)
        }

        // ==================
        // PICKUP/DROP/PLANT
        // ==================

        [Server]
        public bool TryPickup(int playerId)
        {
            if (_state.Value != TurretPlantState.Wild) return false;

            _state.Value = TurretPlantState.Carried;
            _carrierId.Value = playerId;
            _wildTarget = null;
            _wildSubState = WildSubState.Idle;

            // Отключаем коллайдер чтобы не мешал
            if (_collider != null)
            {
                _collider.enabled = false;
            }

            // Отключаем Rigidbody если есть
            var rb = GetComponent<Rigidbody>();
            if (rb != null)
            {
                rb.isKinematic = true;
            }

            // Уведомляем спавнер что растение забрали
            if (_spawner != null)
            {
                _spawner.OnPlantPickedUp();
                _spawner = null; // Больше не связаны со спавнером
            }

            Debug.Log($"[TurretPlant] Picked up by player {playerId}");
            return true;
        }

        /// <summary>
        /// Привязывает растение к игроку (делает дочерним объектом)
        /// </summary>
        [Server]
        public void AttachToPlayer(Transform playerTransform, Vector3 localOffset)
        {
            if (_state.Value != TurretPlantState.Carried) return;

            transform.SetParent(playerTransform);
            transform.localPosition = localOffset;
            transform.localRotation = Quaternion.identity;

            // Синхронизируем parenting на клиентах
            AttachToPlayerObserversRpc(playerTransform.GetComponent<NetworkObject>(), localOffset);
        }

        [ObserversRpc]
        private void AttachToPlayerObserversRpc(NetworkObject playerNetObj, Vector3 localOffset)
        {
            if (playerNetObj == null) return;

            transform.SetParent(playerNetObj.transform);
            transform.localPosition = localOffset;
            transform.localRotation = Quaternion.identity;
        }

        [ObserversRpc]
        private void DetachFromPlayerObserversRpc()
        {
            transform.SetParent(null);
        }

        /// <summary>
        /// Устанавливает спавнер который создал это растение
        /// </summary>
        [Server]
        public void SetSpawner(PlantSpawner spawner)
        {
            _spawner = spawner;
        }

        [Header("Placement Settings")]
        [SerializeField] private float plantHeightOffset = 0.5f; // Offset вверх от земли при посадке

        [Server]
        public void Drop(Vector3 dropPosition)
        {
            if (_state.Value != TurretPlantState.Carried) return;

            // Отвязываем от игрока (но НЕ включаем Rigidbody - сделаем это вручную)
            transform.SetParent(null);
            DetachFromPlayerObserversRpc();

            // Находим правильную высоту
            Vector3 finalPos = GetGroundPosition(dropPosition);
            transform.position = finalPos;
            _state.Value = TurretPlantState.Wild;
            _carrierId.Value = -1;
            _homePosition = finalPos;

            // Включаем коллайдер
            if (_collider != null)
            {
                _collider.enabled = true;
            }

            // Rigidbody остаётся kinematic - растение не должно падать
            var rb = GetComponent<Rigidbody>();
            if (rb != null)
            {
                rb.isKinematic = true;
                rb.linearVelocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
            }

            Debug.Log($"[TurretPlant] Dropped at {finalPos}");
        }

        [Server]
        public void Plant(int ownerId, Vector3 position)
        {
            if (_state.Value != TurretPlantState.Carried) return;

            // Отвязываем от игрока (без включения физики)
            transform.SetParent(null);
            DetachFromPlayerObserversRpc();

            // Находим правильную высоту через raycast
            Vector3 finalPos = GetGroundPosition(position);
            transform.position = finalPos;

            _state.Value = TurretPlantState.Planted;
            _plantedOwnerId.Value = ownerId;
            _carrierId.Value = -1;

            // Включаем коллайдер
            if (_collider != null)
            {
                _collider.enabled = true;
            }

            // Rigidbody kinematic - растение стоит на месте
            var rb = GetComponent<Rigidbody>();
            if (rb != null)
            {
                rb.isKinematic = true;
                rb.linearVelocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
            }

            // Регистрируем в менеджере
            TurretPlantManager.Instance?.RegisterPlantedTurret(ownerId, this);

            Debug.Log($"[TurretPlant] Planted by player {ownerId} at {finalPos}");
        }

        /// <summary>
        /// Находит позицию на земле через raycast + offset вверх
        /// </summary>
        private Vector3 GetGroundPosition(Vector3 position)
        {
            // Raycast вниз от высокой точки
            Vector3 rayStart = position + Vector3.up * 5f;
            if (Physics.Raycast(rayStart, Vector3.down, out RaycastHit hit, 10f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            {
                // Добавляем offset вверх чтобы растение не было в земле
                return hit.point + Vector3.up * plantHeightOffset;
            }
            // Fallback - используем исходную позицию
            return position + Vector3.up * plantHeightOffset;
        }

        /// <summary>
        /// Обновляет позицию растения когда его несут
        /// </summary>
        [Server]
        public void UpdateCarriedPosition(Vector3 position, Quaternion rotation)
        {
            if (_state.Value != TurretPlantState.Carried) return;

            transform.SetPositionAndRotation(position, rotation);
        }

        // ==================
        // DAMAGE HANDLING
        // ==================

        private void OnDamaged(Transform attacker)
        {
            if (!IsServerStarted) return;

            // Wild: агримся на атакующего
            if (_state.Value == TurretPlantState.Wild && _wildSubState == WildSubState.Idle)
            {
                if (attacker != null)
                {
                    _wildTarget = attacker;
                    SetWildSubState(WildSubState.Chasing);
                    Debug.Log($"[TurretPlant] Wild mode: aggro on {attacker.name}");
                }
            }
        }

        private void OnDeath()
        {
            if (!IsServerStarted) return;

            Debug.Log($"[TurretPlant] OnDeath - state: {_state.Value}");

            // Удаляем из менеджера если была посажена
            if (_state.Value == TurretPlantState.Planted)
            {
                TurretPlantManager.Instance?.UnregisterPlantedTurret(_plantedOwnerId.Value, this);
            }

            // Уведомляем спавнер если растение было уничтожено (в Wild состоянии)
            if (_spawner != null)
            {
                _spawner.OnPlantDestroyed();
                _spawner = null;
            }

            StartCoroutine(DelayedDespawn());
        }

        private IEnumerator DelayedDespawn()
        {
            yield return new WaitForSeconds(2f);

            if (NetworkObject != null && NetworkObject.IsSpawned)
            {
                ServerManager.Despawn(NetworkObject);
            }
        }

        // ==================
        // HELPERS
        // ==================

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

        private void OnStateChanged(TurretPlantState oldState, TurretPlantState newState, bool asServer)
        {
            Debug.Log($"[TurretPlant] State changed: {oldState} -> {newState}");

            switch (newState)
            {
                case TurretPlantState.Wild:
                    OnDropped?.Invoke();
                    break;
                case TurretPlantState.Carried:
                    OnPickedUp?.Invoke();
                    break;
                case TurretPlantState.Planted:
                    OnPlanted?.Invoke();
                    break;
            }
        }

        // ==================
        // GIZMOS
        // ==================

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.red;
            Gizmos.DrawWireSphere(transform.position, attackRange);

            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(transform.position, detectionRange);

            Gizmos.color = Color.green;
            Gizmos.DrawWireSphere(transform.position, wildChaseRange);
        }
    }
}
