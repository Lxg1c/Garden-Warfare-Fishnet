using System;
using System.Collections;
using Core.Components;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;

namespace Gameplay.TurretPlant
{
    [RequireComponent(typeof(Health))]
    public class TurretPlant : NetworkBehaviour
    {
        [Header("Detection Settings")]
        [SerializeField] private float detectionRange = 10f;
        [SerializeField] private float attackRange = 8f;
        [SerializeField] private LayerMask playerLayer;

        [Header("Attack Settings")]
        [SerializeField] private float attackCooldown = 1.5f;
        [SerializeField] private int attackDamage = 15;

        [Header("Wild Mode Settings")]
        [SerializeField] private float wildChaseRange = 6f;
        [SerializeField] private float wildReturnTimer = 3f;

        [Header("Projectile")]
        [SerializeField] private GameObject turretBulletPrefab;
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
        public bool CanBePickedUp => _state.Value == TurretPlantState.Wild;

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

            float dist = Vector3.Distance(transform.position, _wildTarget.position);

            if (dist > attackRange)
            {
                SetWildSubState(WildSubState.Chasing);
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

        private Transform FindPlantedTarget()
        {
            // Ищем других игроков (не владельца)
            Collider[] hits = Physics.OverlapSphere(transform.position, detectionRange, playerLayer);

            Transform closest = null;
            float closestDist = float.MaxValue;

            foreach (var c in hits)
            {
                // Пропускаем себя
                if (c.transform == transform) continue;

                NetworkObject netObj = c.GetComponent<NetworkObject>();
                if (netObj == null) continue;

                // Пропускаем владельца
                if (netObj.OwnerId == _plantedOwnerId.Value) continue;

                // Проверяем что это игрок а не нейтрал
                if (c.GetComponent<AI.Neutral.Neutral>() != null) continue;

                // Проверяем здоровье
                Health h = c.GetComponent<Health>();
                if (h == null || h.GetHealth() <= 0 || h.IsDead) continue;

                // Проверяем видимость
                if (!CanSee(c.transform)) continue;

                float dist = Vector3.Distance(transform.position, c.transform.position);
                if (dist < closestDist)
                {
                    closest = c.transform;
                    closestDist = dist;
                }
            }

            return closest;
        }

        private bool IsValidTarget(Transform target)
        {
            if (target == null) return false;

            Health h = target.GetComponent<Health>();
            if (h == null || h.GetHealth() <= 0 || h.IsDead) return false;

            NetworkObject netObj = target.GetComponent<NetworkObject>();
            if (netObj != null && netObj.OwnerId == _plantedOwnerId.Value) return false;

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
            if (turretBulletPrefab == null) return;

            Transform spawnPoint = shootPoint != null ? shootPoint : transform;
            Vector3 direction = (target.position - spawnPoint.position).normalized;
            Quaternion rotation = Quaternion.LookRotation(direction);

            GameObject bullet = Instantiate(turretBulletPrefab, spawnPoint.position, rotation);

            ServerManager.Spawn(bullet);

            // Настраиваем снаряд
            var bulletScript = bullet.GetComponent<TurretBullet>();
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

            Debug.Log($"[TurretPlant] Picked up by player {playerId}");
            return true;
        }

        [Server]
        public void Drop(Vector3 dropPosition)
        {
            if (_state.Value != TurretPlantState.Carried) return;

            transform.position = dropPosition;
            _state.Value = TurretPlantState.Wild;
            _carrierId.Value = -1;
            _homePosition = dropPosition;

            // Включаем коллайдер
            if (_collider != null)
            {
                _collider.enabled = true;
            }

            Debug.Log($"[TurretPlant] Dropped at {dropPosition}");
        }

        [Server]
        public void Plant(int ownerId, Vector3 position)
        {
            if (_state.Value != TurretPlantState.Carried) return;

            transform.position = position;
            _state.Value = TurretPlantState.Planted;
            _plantedOwnerId.Value = ownerId;
            _carrierId.Value = -1;

            // Включаем коллайдер
            if (_collider != null)
            {
                _collider.enabled = true;
            }

            // Регистрируем в менеджере
            TurretPlantManager.Instance?.RegisterPlantedTurret(ownerId, this);

            Debug.Log($"[TurretPlant] Planted by player {ownerId} at {position}");
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
